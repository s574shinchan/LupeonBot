using Discord;
using Discord.WebSocket;
using System.Collections.Concurrent;
using System.Linq;

namespace LupeonBot.Services
{
    public sealed class StickyRefreshService
    {
        private readonly DiscordSocketClient _client;
        private readonly ulong _guildId;

        private sealed record ChannelConfig(
            Func<Embed> EmbedFactory,
            Func<MessageComponent?> ComponentsFactory
        );

        private readonly ConcurrentDictionary<ulong, ChannelConfig> _channels = new();

        // 채널별 마지막 봇 공지 메시지 ID
        private readonly ConcurrentDictionary<ulong, ulong> _lastBotMsgIdByChannel = new();

        // 채널별 동시성 락
        private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _locks = new();

        // 채널별 "추가 갱신 필요" 플래그 (연속 메시지로 작업이 쌓이는 걸 방지)
        private readonly ConcurrentDictionary<ulong, bool> _pending = new();

        private bool _started;

        public StickyRefreshService(DiscordSocketClient client, ulong guildId)
        {
            _client = client;
            _guildId = guildId;
        }

        public void Start()
        {
            if (_started) return;
            _started = true;
            _client.MessageReceived += OnMessageReceivedAsync;
        }

        public void Stop()
        {
            if (!_started) return;
            _started = false;
            _client.MessageReceived -= OnMessageReceivedAsync;
        }

        // Embed만
        public void UpsertChannel(ulong channelId, Func<Embed> embedFactory)
        {
            _channels[channelId] = new ChannelConfig(embedFactory, () => null);
        }

        // Embed + 버튼(components)
        public void UpsertChannel(ulong channelId, Func<Embed> embedFactory, Func<MessageComponent?> componentsFactory)
        {
            _channels[channelId] = new ChannelConfig(embedFactory, componentsFactory);
        }

        // 초기 1회 강제 전송(Ready에서 쓰면 좋음)
        public async Task ForceSendAsync(ulong channelId)
        {
            if (!_channels.TryGetValue(channelId, out var cfg))
                return;

            await RefreshNowAsync(channelId, cfg);
        }

        private async Task OnMessageReceivedAsync(SocketMessage msg)
        {
            // 봇/웹훅 무시
            if (msg.Author.IsBot || msg.Author.IsWebhook)
                return;

            // 길드 채널만 + 특정 길드만
            if (msg.Channel is not SocketGuildChannel gch)
                return;

            if (gch.Guild.Id != _guildId)
                return;

            if (!_channels.TryGetValue(msg.Channel.Id, out var cfg))
                return;

            // ✅ "즉각" 체감 위해 백그라운드로 던지지 않고 await
            await RefreshNowAsync(msg.Channel.Id, cfg);
        }

        private async Task RefreshNowAsync(ulong channelId, ChannelConfig cfg)
        {
            var sem = _locks.GetOrAdd(channelId, _ => new SemaphoreSlim(1, 1));

            // 락이 이미 잡혀 있으면 "추가 갱신 필요"만 표시하고 끝
            if (!await sem.WaitAsync(0))
            {
                _pending[channelId] = true;
                return;
            }

            try
            {
                // 한번 갱신하고, 그 사이에 메시지가 더 왔으면 딱 1번만 더 갱신(작업 누적 방지)
                while (true)
                {
                    _pending[channelId] = false;

                    if (_client.GetChannel(channelId) is not IMessageChannel ch)
                        return;

                    // 1) 기존 공지 즉시 삭제 (ID로 바로 삭제)
                    if (_lastBotMsgIdByChannel.TryGetValue(channelId, out var prevId) && prevId != 0)
                    {
                        try
                        {
                            await ch.DeleteMessageAsync(prevId);
                        }
                        catch
                        {
                            // 실패하면 fallback로 넘어감
                        }
                        finally
                        {
                            _lastBotMsgIdByChannel[channelId] = 0;
                        }
                    }

                    // 2) fallback: 최근 메시지에서 우리 봇이 보낸 메시지 찾아서 삭제
                    //    (봇 재시작/ID 유실/삭제 실패 대비)
                    try
                    {
                        var recent = await ch.GetMessagesAsync(30).FlattenAsync();
                        var lastBot = recent.FirstOrDefault(m => m.Author.Id == _client.CurrentUser.Id);
                        if (lastBot != null)
                        {
                            await lastBot.DeleteAsync();
                        }
                    }
                    catch
                    {
                        // 무시
                    }

                    // 3) 즉시 재전송
                    var comps = cfg.ComponentsFactory?.Invoke();
                    var sent = await ch.SendMessageAsync(embed: cfg.EmbedFactory(), components: comps);
                    _lastBotMsgIdByChannel[channelId] = sent.Id;

                    // ✅ 도중에 메시지가 추가로 왔으면(=pending true) 한 번만 더 반복
                    if (_pending.TryGetValue(channelId, out var need) && need)
                        continue;

                    break;
                }
            }
            finally
            {
                sem.Release();
            }
        }
    }
}
