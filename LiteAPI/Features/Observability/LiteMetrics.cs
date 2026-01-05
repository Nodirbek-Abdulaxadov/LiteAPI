using System.Collections.Concurrent;
using System.Diagnostics;

public sealed class LiteMetrics
{
    private readonly long _startedAtTicks = Stopwatch.GetTimestamp();

    private long _totalRequests;
    private long _activeRequests;
    private long _total4xx;
    private long _total5xx;

    internal void OnRequestStart() => Interlocked.Increment(ref _activeRequests);

    internal void OnRequestEnd(int statusCode)
    {
        Interlocked.Decrement(ref _activeRequests);
        Interlocked.Increment(ref _totalRequests);

        if (statusCode >= 400 && statusCode < 500)
            Interlocked.Increment(ref _total4xx);
        else if (statusCode >= 500)
            Interlocked.Increment(ref _total5xx);
    }

    public LiteMetricsSnapshot Snapshot()
    {
        var elapsedSeconds = (Stopwatch.GetTimestamp() - _startedAtTicks) / (double)Stopwatch.Frequency;
        return new LiteMetricsSnapshot(
            TotalRequests: Interlocked.Read(ref _totalRequests),
            ActiveRequests: Interlocked.Read(ref _activeRequests),
            Total4xx: Interlocked.Read(ref _total4xx),
            Total5xx: Interlocked.Read(ref _total5xx),
            UptimeSeconds: elapsedSeconds);
    }
}

public readonly record struct LiteMetricsSnapshot(
    long TotalRequests,
    long ActiveRequests,
    long Total4xx,
    long Total5xx,
    double UptimeSeconds);
