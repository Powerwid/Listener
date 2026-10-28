namespace TonysGo.Listener.Bbva;

// Uses elapsed realtime, never the clock/date attached to a payment.
public sealed class ListenerRecoveryPlan
{
    private static readonly long[] Delays = [1000, 2000, 4000, 8000, 15000, 30000, 60000];
    private long? _disconnectedAt;
    private long _nextAttempt, _nextRefresh;
    private int _attempt;
    public bool NeedsHelp { get; private set; }

    public ListenerRecoveryStep Check(long now, bool connected, bool permitted, bool unlocked)
    {
        if (connected || !permitted || !unlocked)
        {
            _disconnectedAt = null; _attempt = 0; _nextAttempt = now; NeedsHelp = false;
            return new(false, false, 60000);
        }
        _disconnectedAt ??= now;
        var elapsed = now - _disconnectedAt.Value;
        NeedsHelp = elapsed >= 45000;
        var refresh = elapsed >= 20000 && now >= _nextRefresh;
        if (refresh) _nextRefresh = now + 15 * 60000;
        var request = now >= _nextAttempt || refresh;
        if (request) _nextAttempt = now + Delays[Math.Min(_attempt++, Delays.Length - 1)];
        var next = Math.Max(1, _nextAttempt - now);
        if (elapsed < 20000 && now >= _nextRefresh) next = Math.Min(next, 20000 - elapsed);
        if (!NeedsHelp) next = Math.Min(next, 45000 - elapsed);
        return new(request, refresh, next);
    }
}

public readonly record struct ListenerRecoveryStep(bool RequestBinding, bool RefreshRegistration, long CheckAfterMilliseconds);
