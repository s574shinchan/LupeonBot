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

        // 채널 설정(Embed + 디바운스)
        private readonly ConcurrentDictionary<ulong, ChannelConfig> _channels = new();

        // 채널별 마지막 봇 메시지 ID
        private readonly ConcurrentDictionary<ulong, ulong> _lastBotMsgIdByChannel = new();

        // 채널별 락
        private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _locks = new();

        // 채널별 디바운스용 CTS (메시지 오면 이전 예약 취소 후 다시 예약)
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
        /// debounceSeconds: 유저가 연속으로 쳐도 이 시간 내에는 1번만 새로고침
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
                cts.Cancel();
        }

        private Task OnMessageReceivedAsync(SocketMessage msg)
        {
            // 봇/웹훅 무시
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

            // ✅ 디바운스: 메시지 올 때마다 "예약"을 갱신해서 마지막 메시지 기준 N초 후 1회만 실행
            var newCts = new CancellationTokenSource();

            var oldCts = _debounceCts.AddOrUpdate(
                msg.Channel.Id,
                _ => newCts,
                (_, prev) =>
                {
                    try { prev.Cancel(); } catch { }
                    return newCts;
                });

            // oldCts는 AddOrUpdate 내부에서 cancel됨

            _ = DebouncedRefreshAsync(msg.Channel.Id, cfg, newCts.Token);
            return Task.CompletedTask;
        }

        private async Task DebouncedRefreshAsync(ulong channelId, ChannelConfig cfg, CancellationToken ct)
        {
            try
            {
                if (cfg.Debounce > TimeSpan.Zero)
                    await Task.Delay(cfg.Debounce, ct);

                await SendOrReplaceAsync(channelId, cfg, ct);
            }
            catch (OperationCanceledException)
            {
                // 메시지가 또 와서 예약이 취소됨 → 정상
            }
            catch
            {
                // 필요하면 로그 남겨도 됨
            }
        }

        /// <summary>
        /// 즉시 강제 새로고침(초기 1회 전송에도 사용)
        /// </summary>
        public async Task ForceRefreshAsync(ulong channelId)
        {
            if (!_channels.TryGetValue(channelId, out var cfg))
                return;

            await SendOrReplaceAsync(channelId, cfg, CancellationToken.None);
        }

        private async Task SendOrReplaceAsync(ulong channelId, ChannelConfig cfg, CancellationToken ct)
        {
            var sem = _locks.GetOrAdd(channelId, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync(ct);

            try
            {
                if (_client.GetChannel(channelId) is not IMessageChannel ch)
                    return;

                // ✅ "해당 채널의 마지막 메시지 작성자가 봇"이면 재전송 X
                try
                {
                    var last = (await ch.GetMessagesAsync(1).FlattenAsync()).FirstOrDefault();
                    if (last != null && last.Author.Id == _client.CurrentUser.Id)
                        return;
                }
                catch
                {
                    // 마지막 메시지 조회 실패 시에는 진행
                }

                // 기존 봇 메시지 삭제(있으면)
                if (_lastBotMsgIdByChannel.TryGetValue(channelId, out var msgId))
                {
                    try
                    {
                        var old = await ch.GetMessageAsync(msgId);
                        if (old != null) await old.DeleteAsync();
                    }
                    catch
                    {
                        // 이미 삭제/권한없음 등 무시
                    }
                }

                // 새로 전송
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
