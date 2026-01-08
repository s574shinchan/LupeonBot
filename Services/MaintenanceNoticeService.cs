using Discord;
using Discord.WebSocket;
using LupeonBot.Module;
using System;
using System.Threading;
using System.Threading.Tasks;

public sealed class MaintenanceNoticeService
{
    private readonly DiscordSocketClient _client;
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private const ulong MAINT_CHANNEL_ID = 1458858160818491393; // 실제 채널 ID

    // ✅ 체크 주기 (원하면 1~10분)
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    public MaintenanceNoticeService(DiscordSocketClient client)
    {
        _client = client;
    }

    public void Start()
    {
        if (_cts != null) return; // 이미 시작됨

        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(Interval);

        _ = RunAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _timer?.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        // 봇 완전 준비 대기
        await Task.Delay(TimeSpan.FromSeconds(5), ct);

        // ✅ 시작하자마자 1번 즉시 체크 (원하면 제거 가능)
        await ExecuteOnceSafeAsync();

        while (await _timer!.WaitForNextTickAsync(ct))
        {
            try
            {
                await ExecuteOnceSafeAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[공지자동] {ex}");
            }
        }
    }

    private async Task ExecuteOnceSafeAsync()
    {
        if (!await _gate.WaitAsync(0)) return;

        try
        {
            var newNotices = await NoticeModule.FetchNewMaintenanceNoticesAsync();
            if (newNotices == null || newNotices.Count == 0) return;

            var channel = _client.GetChannel(MAINT_CHANNEL_ID) as IMessageChannel;
            if (channel == null)
            {
                Console.WriteLine($"[공지자동] 채널을 찾지 못함: {MAINT_CHANNEL_ID}");
                return;
            }   

            // 오래된 것부터 알림 (NoticeModule에서 이미 OrderBy했으면 그대로 써도 됨)
            foreach (var n in newNotices.OrderBy(x => x.Date))
            {
                var eb = new EmbedBuilder()
                    .WithTitle("로스트아크 - 공지사항")
                    .WithColor(Color.Orange)
                    .WithDescription($"[{n.Type}] [{n.Title}]({n.Link})")
                    .WithFooter($"Develop by. 갱프 ㆍ {DateTime.UtcNow.AddHours(9).ToString("yyyy-MM-dd HH:mm:ss")}");

                await channel.SendMessageAsync(embed: eb.Build());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[공지자동] 오류: {ex}");
        }
        finally
        {
            _gate.Release();
        }
    }
}
