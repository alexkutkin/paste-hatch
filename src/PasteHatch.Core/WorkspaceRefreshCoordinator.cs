namespace PasteHatch.Core;

/// <summary>
/// Owns watch lifetime, stale notification rejection and UI refresh coalescing.
/// Public operations and dispatched work run on the UI thread; watcher events only enqueue work.
/// </summary>
public sealed class WorkspaceRefreshCoordinator : IDisposable
{
    private readonly BasketWorkspace _workspace;
    private readonly IWorkspaceWatcher _watcher;
    private readonly Func<Action, bool> _enqueue;
    private readonly Action<bool> _render;
    private readonly ITextProvider _text;
    private EventHandler? _changed;
    private EventHandler? _error;
    private string? _watchedUid;
    private long _generation;
    private bool _visible;
    private bool _disposed;
    private bool _scheduled;
    private bool _treeDirty;
    private bool _diskDirty;
    private bool _watchFailed;
    private bool _startingWatch;

    public WorkspaceRefreshCoordinator(BasketWorkspace workspace, IWorkspaceWatcher watcher,
        Func<Action, bool> enqueue, Action<bool> render, ITextProvider? text = null)
    {
        _workspace = workspace;
        _watcher = watcher;
        _enqueue = enqueue;
        _render = render;
        _text = text ?? KeyTextProvider.Instance;
        workspace.Changed += OnWorkspaceChanged;
    }

    public void SetVisible(bool visible)
    {
        if (_disposed) return;
        _visible = visible;
        UpdateWatch();
        if (visible) _workspace.Refresh();
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChange change)
    {
        if (_disposed) return;
        UpdateWatch();
        if (change.Kind == WorkspaceChangeKind.Contents && _visible)
            _watcher.SuspendFor(TimeSpan.FromSeconds(1));
        _treeDirty |= change.Kind != WorkspaceChangeKind.Status;
        Schedule();
    }

    private void UpdateWatch()
    {
        if (_startingWatch) return;
        var tab = _visible ? _workspace.ActiveTab : null;
        if (tab?.Uid == _watchedUid) return;
        StopWatch();
        if (tab is null) return;
        var generation = _generation;
        _changed = (_, _) => QueueDiskChange(generation, false);
        _error = (_, _) => QueueDiskChange(generation, true);
        _watcher.Changed += _changed;
        _watcher.Error += _error;
        try
        {
            _startingWatch = true;
            _watcher.Watch(tab.DirectoryPath);
            _watchedUid = tab.Uid;
        }
        catch (Exception ex)
        {
            StopWatch();
            _workspace.ReportStatus(_text.Format("Error_WatchFolder", ex.Message));
        }
        finally { _startingWatch = false; }
    }

    private void QueueDiskChange(long generation, bool error)
    {
        _enqueue(() =>
        {
            if (_disposed || !_visible || generation != _generation) return;
            _diskDirty = true;
            _watchFailed |= error;
            Schedule();
        });
    }

    private void Schedule()
    {
        if (_scheduled) return;
        _scheduled = true;
        if (!_enqueue(Flush)) _scheduled = false;
    }

    private void Flush()
    {
        if (_disposed) return;
        // Keep the pending flag while Refresh emits its own Changed event.
        if (_diskDirty)
        {
            _diskDirty = false;
            if (_watchFailed)
            {
                _watchFailed = false;
                StopWatch();
                UpdateWatch();
            }
            _workspace.Refresh();
        }
        var treeDirty = _treeDirty;
        _treeDirty = false;
        _scheduled = false;
        _render(treeDirty);
    }

    private void StopWatch()
    {
        _generation++;
        if (_changed is not null) _watcher.Changed -= _changed;
        if (_error is not null) _watcher.Error -= _error;
        _changed = _error = null;
        _watcher.Stop();
        _watchedUid = null;
        _diskDirty = _watchFailed = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _workspace.Changed -= OnWorkspaceChanged;
        StopWatch();
        _watcher.Dispose();
    }
}
