using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

public record UserLeftJob(ulong GuildId, ulong UserId);

public sealed class UserLeftQueue
{
    private readonly Channel<UserLeftJob> _ch;
    private readonly Func<UserLeftJob, CancellationToken, Task> _handler;
    private readonly Action<Exception>? _onError;

    private CancellationTokenSource? _cts;
    private Task? _worker;

    public UserLeftQueue(
        Func<UserLeftJob, CancellationToken, Task> handler,
        Action<Exception>? onError = null)
    {
        _handler = handler;
        _onError = onError;

        _ch = Channel.CreateUnbounded<UserLeftJob>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void Start()
    {
        if (_worker != null) return;
        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => WorkerLoop(_cts.Token));
    }

    public void Enqueue(UserLeftJob job)
    {
        // 이벤트를 절대 막지 않게 TryWrite
        _ch.Writer.TryWrite(job);
    }

    public async Task StopAsync()
    {
        if (_cts == null || _worker == null) return;

        try
        {
            _cts.Cancel();
            _ch.Writer.TryComplete();
            await _worker;
        }
        catch { /* ignore */ }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _worker = null;
        }
    }

    private async Task WorkerLoop(CancellationToken ct)
    {
        await foreach (var job in _ch.Reader.ReadAllAsync(ct))
        {
            try
            {
                await _handler(job, ct);
            }
            catch (Exception ex)
            {
                _onError?.Invoke(ex);
            }
        }
    }
}
