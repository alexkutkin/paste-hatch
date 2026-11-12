namespace PasteHatch.Core;

/// <summary>
/// Owns watch lifetime, stale notification rejection and UI refresh coalescing. Public operations
/// and dispatched work run on the UI thread; watcher events only enqueue work. The two watchers
/// share one contract with different watch configuration: one recursive on the active basket and
/// one on the baskets root that adopts folders copied there.
/// </summary>
public sealed class WorkspaceRefreshCoordinator : IDisposable
{
    private readonly BasketWorkspace _workspace;
    private readonly IWorkspaceWatcher _watcher;
    private readonly IWorkspaceWatcher _rootWatcher;
    private readonly Func<Action, bool> _enqueue;
    private readonly Action<ViewRefresh> _render;
    private readonly ITextProvider _text;
    private EventHandler? _changed;
    private EventHandler? _error;
    private WatchAttempt? _watchAttempt;
    private long _generation;
    private bool _visible;
    private bool _disposed;
    private bool _scheduled;
    private PendingWork _pending;

    private sealed record WatchTarget(string Uid, string Path);
    private sealed record WatchAttempt(WatchTarget Target, WatchOutcome Outcome);
    private enum WatchOutcome { Watching, Unavailable }
    private enum PendingWork { None, Presentation, Tree, ReadDisk, RestartWatchAndReadDisk }

    public WorkspaceRefreshCoordinator(BasketWorkspace workspace, IWorkspaceWatcher basketWatcher, IWorkspaceWatcher rootWatcher,
        Func<Action, bool> enqueue, Action<ViewRefresh> render, ITextProvider? text = null)
    {
        _workspace = workspace;
        _watcher = basketWatcher;
        _rootWatcher = rootWatcher;
        _enqueue = enqueue;
        _render = render;
        _text = text ?? KeyTextProvider.Instance;
        workspace.Changed += OnWorkspaceChanged;
    }

    public void SetVisible(bool visible)
    {
        if (_disposed) return;
        _visible = visible;
        if (visible) RequestRefresh();
        else UpdateWatch();
    }

    /// <summary>Explicitly re-reads disk and retries an unavailable watch for the current target.</summary>
    public void RequestRefresh()
    {
        if (_disposed) return;
        UpdateWatch(retryUnavailable: true);
        Request(PendingWork.ReadDisk);
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChange change)
    {
        if (_disposed) return;
        UpdateWatch();
        if (change.Kind == WorkspaceChangeKind.Contents && _watchAttempt?.Outcome == WatchOutcome.Watching)
            _watcher.SuppressEcho();
        Request(change.Kind == WorkspaceChangeKind.Status ? PendingWork.Presentation : PendingWork.Tree);
    }

    private void UpdateWatch(bool retryUnavailable = false)
    {
        var tab = _visible ? _workspace.ActiveTab : null;
        var target = tab is null ? null : new WatchTarget(tab.Uid, tab.DirectoryPath);
        if (target == _watchAttempt?.Target &&
            (!retryUnavailable || _watchAttempt?.Outcome != WatchOutcome.Unavailable)) return;
        StopWatch();
        if (target is null) return;
        var generation = _generation;
        _changed = (_, _) => QueueDiskChange(generation, false);
        _error = (_, _) => QueueDiskChange(generation, true);
        _watcher.Changed += _changed;
        _watcher.Error += _error;
        _rootWatcher.Changed += _changed;
        _rootWatcher.Error += _error;
        try
        {
            _watcher.Watch(target.Path);
            _rootWatcher.Watch(_workspace.BasketsRoot, includeSubdirectories: false);
            _watchAttempt = new WatchAttempt(target, WatchOutcome.Watching);
        }
        catch (Exception ex)
        {
            StopWatch();
            _watchAttempt = new WatchAttempt(target, WatchOutcome.Unavailable);
            _workspace.ReportError(_text.Format("Error_WatchFolder", ex.Message));
        }
    }

    private void QueueDiskChange(long generation, bool error)
    {
        _enqueue(() =>
        {
            if (_disposed || !_visible || generation != _generation) return;
            Request(error ? PendingWork.RestartWatchAndReadDisk : PendingWork.ReadDisk);
        });
    }

    private void Request(PendingWork work)
    {
        if (work > _pending) _pending = work;
        Schedule();
    }

    private void Schedule()
    {
        if (_scheduled) return;
        _scheduled = true;
        if (!_enqueue(Flush)) _scheduled = false;
    }

    private void Flush()
    {
        var work = _pending;
        _pending = PendingWork.None;
        try
        {
            if (_disposed) return;
            if (work == PendingWork.RestartWatchAndReadDisk)
            {
                StopWatch();
                UpdateWatch();
            }
            var readSucceeded = work < PendingWork.ReadDisk || _workspace.Refresh();
            // A successful Refresh publishes its own View change. Render that change in this pass,
            // but leave a new disk request queued for the next pass, including requests from render.
            var emittedWork = _pending;
            if (emittedWork <= PendingWork.Tree && (readSucceeded || emittedWork != PendingWork.Tree))
                _pending = PendingWork.None;
            var rebuildTree = readSucceeded && (work >= PendingWork.Tree || emittedWork >= PendingWork.Tree);
            _render(new ViewRefresh(RebuildTree: rebuildTree));
        }
        finally
        {
            _scheduled = false;
            if (!_disposed && _pending != PendingWork.None) Schedule();
        }
    }

    private void StopWatch()
    {
        _generation++;
        if (_changed is not null) _watcher.Changed -= _changed;
        if (_error is not null) _watcher.Error -= _error;
        if (_changed is not null) _rootWatcher.Changed -= _changed;
        if (_error is not null) _rootWatcher.Error -= _error;
        _changed = _error = null;
        _watcher.Stop();
        _rootWatcher.Stop();
        _watchAttempt = null;
        if (_pending >= PendingWork.ReadDisk) _pending = PendingWork.Presentation;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pending = PendingWork.None;
        _workspace.Changed -= OnWorkspaceChanged;
        StopWatch();
        _watcher.Dispose();
        _rootWatcher.Dispose();
    }
}
