namespace Halgoworks.Services;

/// <summary>
/// Steps through a recorded trace on a timer. A page owns one, renders <see cref="Index"/>,
/// and re-renders whenever <see cref="Changed"/> fires.
/// </summary>
public sealed class Player : IDisposable
{
    public static readonly double[] Speeds = [0.5, 1, 2, 4, 8, 16];

    private const double MillisecondsPerStep = 800;
    private CancellationTokenSource? _cts;

    public int Count { get; private set; }

    public int Index { get; private set; }

    public double Speed { get; private set; } = 2;

    public bool Playing => _cts is not null;

    public bool AtStart => Index <= 0;

    public bool AtEnd => Index >= Count - 1;

    public event Action? Changed;

    /// <summary>Points the player at a new trace and rewinds it.</summary>
    public void Load(int count, bool jumpToEnd = false)
    {
        Pause(notify: false);
        Count = count;
        Index = jumpToEnd ? Math.Max(0, count - 1) : 0;
        Changed?.Invoke();
    }

    /// <summary>Jumps to a step and stops playback, like grabbing the timeline.</summary>
    public void Seek(int index)
    {
        Pause(notify: false);
        Index = Math.Clamp(index, 0, Math.Max(0, Count - 1));
        Changed?.Invoke();
    }

    public void Next() => Seek(Index + 1);

    public void Prev() => Seek(Index - 1);

    public void First() => Seek(0);

    public void Last() => Seek(Count - 1);

    public void Toggle()
    {
        if (Playing) Pause();
        else Play();
    }

    public void SetSpeed(double speed)
    {
        Speed = speed;
        Changed?.Invoke();
    }

    public void Play()
    {
        if (Count <= 1 || Playing) return;
        if (AtEnd) Index = 0;
        _cts = new CancellationTokenSource();
        Changed?.Invoke();
        _ = Run(_cts.Token);
    }

    public void Pause() => Pause(notify: true);

    private void Pause(bool notify)
    {
        if (_cts is null) return;
        _cts.Cancel();
        _cts = null;
        if (notify) Changed?.Invoke();
    }

    private async Task Run(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(MillisecondsPerStep / Speed), token);
                if (token.IsCancellationRequested) return;
                Index = Math.Min(Index + 1, Count - 1);
                if (AtEnd)
                {
                    _cts = null;
                    Changed?.Invoke();
                    return;
                }

                Changed?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose() => Pause(notify: false);
}
