namespace YunKao.Core.Services;

public enum BrowserRecoveryPhase
{
    Stable,
    Recovering,
    Completed,
    Failed,
}

/// <summary>
/// WebView 恢复的最小状态机。只有导航、Bridge 和首个页面状态都到位后才允许完成恢复。
/// </summary>
public sealed class BrowserRecoveryStateMachine
{
    private readonly object _gate = new();
    private bool _navigationCompleted;
    private bool _bridgeReady;
    private bool _pageStateReceived;
    private bool _practiceReady;

    public BrowserRecoveryPhase Phase
    {
        get { lock (_gate) return _phase; }
    }

    public bool IsRecovering
    {
        get { lock (_gate) return _phase == BrowserRecoveryPhase.Recovering; }
    }

    public bool CanContinue
    {
        get { lock (_gate) return _phase == BrowserRecoveryPhase.Completed && _practiceReady; }
    }

    private BrowserRecoveryPhase _phase = BrowserRecoveryPhase.Stable;

    public bool Begin()
    {
        lock (_gate)
        {
            if (_phase == BrowserRecoveryPhase.Recovering) return false;
            _navigationCompleted = false;
            _bridgeReady = false;
            _pageStateReceived = false;
            _practiceReady = false;
            _phase = BrowserRecoveryPhase.Recovering;
            return true;
        }
    }

    public bool MarkNavigationCompleted(bool success)
    {
        lock (_gate)
        {
            if (_phase != BrowserRecoveryPhase.Recovering) return false;
            if (!success)
            {
                _phase = BrowserRecoveryPhase.Failed;
                return false;
            }

            _navigationCompleted = true;
            return TryComplete();
        }
    }

    public bool MarkBridgeReady(bool available = true)
    {
        lock (_gate)
        {
            if (_phase != BrowserRecoveryPhase.Recovering) return false;
            _bridgeReady = available;
            return TryComplete();
        }
    }

    public bool MarkPageState(bool practiceReady)
    {
        lock (_gate)
        {
            if (_phase != BrowserRecoveryPhase.Recovering) return false;
            _pageStateReceived = true;
            _practiceReady = practiceReady;
            return TryComplete();
        }
    }

    public void Fail()
    {
        lock (_gate) _phase = BrowserRecoveryPhase.Failed;
    }

    private bool TryComplete()
    {
        if (!_navigationCompleted || !_bridgeReady || !_pageStateReceived) return false;
        _phase = BrowserRecoveryPhase.Completed;
        return true;
    }
}
