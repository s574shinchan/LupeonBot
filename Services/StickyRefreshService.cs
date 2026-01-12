using Discord;
using Discord.WebSocket;
using System.Collections.Concurrent;

namespace LupeonBot.Services
{
    public sealed class StickyRefreshService
    {
        private readonly DiscordSocketClient _client;
        private readonly ulong _guildId;

        private sealed record ChannelConfig(Func<Embed> EmbedFactory, TimeSpan Debounce);

        // 채널 설정(채널별 Embed + 디바운스)
        private readonly ConcurrentDictionary<ulong, ChannelConfig> _channels = new();

        // 채널별 마지막 봇 메시지 ID
        private readonly ConcurrentDictionary<ulong, ulong> _lastBotMsgIdByChannel = new();

        // 채널별 락
        private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _locks = new();

        // 채널별 디바운스 예약 취소 토큰
        private readonly ConcurrentDictionary<ulong, CancellationTokenSource> _debounceCts = new();

        public StickyRefreshService(DiscordSocketClient client, ulong guildId)
        {
            _client = client;
            _guildId = guildId;
        }

        public void Start() => _client.MessageReceived += OnMessageReceivedAsync;
        public void Stop() => _client.MessageReceived -= OnMessageReceivedAsync;

        /// <summary>
        /// 채널 등록/갱신 (채널별 Embed + 디바운스)
        /// debounceSeconds: 마지막 유저 메시지 이후 조용해진 뒤 이 시간 후 재전송
        /// </summary>
        public void UpsertChannel(ulong channelId, Func<Embed> embedFactory, int debounceSeconds = 3)
        {
            if (debounceSeconds < 0) debounceSeconds = 0;
            _channels[channelId] = new ChannelConfig(embedFactory, TimeSpan.FromSeconds(debounceSeconds));
        }

        public void RemoveChannel(ulong channelId)
        {
            _channels.TryRemove(channelId, out _);
            _lastBotMsgIdByChannel.TryRemove(channelId, out _);

            if (_debounceCts.TryRemove(channelId, out var cts))
            {
                try { cts.Cancel(); } catch { }
            }
        }

        /// <summary>초기 1회 전송(강제)</summary>
        public async Task ForceSendAsync(ulong channelId)
        {
            if (!_channels.TryGetValue(channelId, out var cfg))
                return;

            await SendStickyAsync(channelId, cfg, CancellationToken.None);
        }

        private Task OnMessageReceivedAsync(SocketMessage msg)
        {
            // 봇/웹훅 무시(무한루프 방지)
            if (msg.Author.IsBot || msg.Author.IsWebhook)
                return Task.CompletedTask;

            // 길드 채널만 + 특정 길드만
            if (msg.Channel is not SocketGuildChannel gch)
                return Task.CompletedTask;

            if (gch.Guild.Id != _guildId)
                return Task.CompletedTask;

            // 등록된 채널만
            if (!_channels.TryGetValue(msg.Channel.Id, out var cfg))
                return Task.CompletedTask;

            // ✅ 1) 유저가 치자마자 기존 공지 즉각 삭제
            _ = DeleteStickyNowAsync(msg.Channel.Id);

            // ✅ 2) 디바운스 후 재전송 예약(도배방지)
            var newCts = new CancellationTokenSource();
            _debounceCts.AddOrUpdate(
                msg.Channel.Id,
                _ => newCts,
                (_, prev) =>
                {
                    try { prev.Cancel(); } catch { }
                    return newCts;
                });

            _ = DebouncedResendAsync(msg.Channel.Id, cfg, newCts.Token);
            return Task.CompletedTask;
        }

        private async Task DeleteStickyNowAsync(ulong channelId)
        {
            var sem = _locks.GetOrAdd(channelId, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync();

            try
            {
                if (_client.GetChannel(channelId) is not IMessageChannel ch)
                    return;

                // 우리가 저장해둔 마지막 봇 메시지가 있으면 삭제
                if (_lastBotMsgIdByChannel.TryGetValue(channelId, out var msgId) && msgId != 0)
                {
                    try
                    {
                        var old = await ch.GetMessageAsync(msgId);
                        if (old != null)
                            await old.DeleteAsync();
                    }
                    catch
                    {
                        // 이미 삭제됨/권한없음 등은 무시
                    }
                    finally
                    {
                        // ✅ 삭제 시도했으면 “현재 공지 없음” 상태로
                        _lastBotMsgIdByChannel[channelId] = 0;
                    }
                }
            }
            finally
            {
                sem.Release();
            }
        }

        private async Task DebouncedResendAsync(ulong channelId, ChannelConfig cfg, CancellationToken ct)
        {
            try
            {
                if (cfg.Debounce > TimeSpan.Zero)
                    await Task.Delay(cfg.Debounce, ct);

                await SendStickyAsync(channelId, cfg, ct);
            }
            catch (OperationCanceledException)
            {
                // 도배로 인해 예약이 취소됨(정상)
            }
            catch
            {
                // 필요하면 로그
            }
        }

        private async Task SendStickyAsync(ulong channelId, ChannelConfig cfg, CancellationToken ct)
        {
            var sem = _locks.GetOrAdd(channelId, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync(ct);

            try
            {
                if (_client.GetChannel(channelId) is not IMessageChannel ch)
                    return;

                // ✅ 마지막 메시지가 "우리 봇"이면 이미 공지 상태로 판단하고 재전송 X
                // (중복 전송 방지)
                try
                {
                    var last = (await ch.GetMessagesAsync(1).FlattenAsync()).FirstOrDefault();
                    if (last != null && last.Author.Id == _client.CurrentUser.Id)
                    {
                        _lastBotMsgIdByChannel[channelId] = last.Id;
                        return;
                    }
                }
                catch { }

                var sent = await ch.SendMessageAsync(embed: cfg.EmbedFactory());
                _lastBotMsgIdByChannel[channelId] = sent.Id;
            }
            finally
            {
                sem.Release();
            }
        }
    }
}
