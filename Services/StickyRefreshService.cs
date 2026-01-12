using Discord;
using Discord.WebSocket;
using System.Collections.Concurrent;

namespace LupeonBot.Services
{
    public sealed class StickyRefreshService
    {
        private readonly DiscordSocketClient _client;
        private readonly ulong _guildId;

        private sealed record ChannelConfig(
            Func<Embed> EmbedFactory,
            Func<MessageComponent?> ComponentsFactory,
            TimeSpan Cooldown // 너무 연타될 때 봇이 과호출 안 하도록 최소 쿨다운(0 가능)
        );

        private readonly ConcurrentDictionary<ulong, ChannelConfig> _channels = new();

        // 채널별 마지막 봇 공지 메시지 ID
        private readonly ConcurrentDictionary<ulong, ulong> _lastBotMsgIdByChannel = new();

        // 채널별 동시성 락
        private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _locks = new();

        // 채널별 마지막 실행 시간(쿨다운용)
        private readonly ConcurrentDictionary<ulong, DateTimeOffset> _lastRunAt = new();

        public StickyRefreshService(DiscordSocketClient client, ulong guildId)
        {
            _client = client;
            _guildId = guildId;
        }

        public void Start() => _client.MessageReceived += OnMessageReceivedAsync;
        public void Stop() => _client.MessageReceived -= OnMessageReceivedAsync;

        // Embed만
        public void UpsertChannel(ulong channelId, Func<Embed> embedFactory, int cooldownMs = 0)
        {
            _channels[channelId] = new ChannelConfig(
                embedFactory,
                () => null,
                TimeSpan.FromMilliseconds(Math.Max(0, cooldownMs))
            );
        }

        // Embed + 버튼(components)
        public void UpsertChannel(ulong channelId, Func<Embed> embedFactory, Func<MessageComponent?> componentsFactory, int cooldownMs = 0)
        {
            _channels[channelId] = new ChannelConfig(
                embedFactory,
                componentsFactory,
                TimeSpan.FromMilliseconds(Math.Max(0, cooldownMs))
            );
        }

        // 초기 1회 강제 전송(Ready에서 쓰면 좋음)
        public async Task ForceSendAsync(ulong channelId)
        {
            if (!_channels.TryGetValue(channelId, out var cfg))
                return;

            await RefreshNowAsync(channelId, cfg);
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

            if (!_channels.TryGetValue(msg.Channel.Id, out var cfg))
                return Task.CompletedTask;

            // ✅ 유저가 치는 즉시 "삭제+재전송" (백그라운드로 수행)
            _ = RefreshNowAsync(msg.Channel.Id, cfg);
            return Task.CompletedTask;
        }

        private async Task RefreshNowAsync(ulong channelId, ChannelConfig cfg)
        {
            var sem = _locks.GetOrAdd(channelId, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync();

            try
            {
                // ✅ 쿨다운(원하면 0으로 끄면 됨)
                if (cfg.Cooldown > TimeSpan.Zero)
                {
                    var now = DateTimeOffset.UtcNow;
                    var last = _lastRunAt.GetOrAdd(channelId, _ => DateTimeOffset.MinValue);
                    if (now - last < cfg.Cooldown)
                        return;

                    _lastRunAt[channelId] = now;
                }

                if (_client.GetChannel(channelId) is not IMessageChannel ch)
                    return;

                // 1) 기존 공지 삭제 (우리가 기억하는 ID 우선)
                if (_lastBotMsgIdByChannel.TryGetValue(channelId, out var prevId) && prevId != 0)
                {
                    try
                    {
                        var old = await ch.GetMessageAsync(prevId);
                        if (old != null) await old.DeleteAsync();
                    }
                    catch
                    {
                        // 이미 삭제됨/권한없음/찾기 실패 등 무시
                    }
                }
                else
                {
                    // (선택) 혹시 ID가 없으면, 마지막 메시지가 우리 봇이면 그것도 삭제 시도
                    try
                    {
                        var lastMsg = (await ch.GetMessagesAsync(1).FlattenAsync()).FirstOrDefault();
                        if (lastMsg != null && lastMsg.Author.Id == _client.CurrentUser.Id)
                            await lastMsg.DeleteAsync();
                    }
                    catch { }
                }

                // 2) 즉시 재전송
                var comps = cfg.ComponentsFactory?.Invoke();
                var sent = await ch.SendMessageAsync(embed: cfg.EmbedFactory(), components: comps);

                _lastBotMsgIdByChannel[channelId] = sent.Id;
            }
            finally
            {
                sem.Release();
            }
        }
    }
}
