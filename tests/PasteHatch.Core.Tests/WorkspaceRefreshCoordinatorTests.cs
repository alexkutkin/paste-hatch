namespace PasteHatch.Core.Tests;

public sealed class WorkspaceRefreshCoordinatorTests : IDisposable
{
    private readonly WorkspaceHarness _harness = new();
    private readonly Queue<Action> _queue = new();
    private readonly List<bool> _renders = [];
    private readonly FakeWatcher _watcher = new();
    private readonly FakeWatcher _rootWatcher = new();
    public void Dispose() => _harness.Dispose();

    private WorkspaceRefreshCoordinator Create(BasketWorkspace workspace) => new(workspace, _watcher, _rootWatcher,
        action => { _queue.Enqueue(action); return true; }, refresh => _renders.Add(refresh.RebuildTree), _harness.Text);

    private void Drain()
    {
        var limit = 100;
        while (_queue.TryDequeue(out var action))
        {
            Assert.True(--limit > 0, "Refresh scheduling did not settle.");
            action();
        }
    }

    [Fact]
    public void RenameActiveBasket_RetargetsWatcherWithoutChangingIdentity()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var uid = workspace.RequireActiveTab().Uid;
        using var coordinator = Create(workspace);
        coordinator.SetVisible(true);
        Drain();

        Assert.True(workspace.RenameTab(uid, "Watched basket"));
        Drain();

        Assert.Equal(uid, workspace.RequireActiveTab().Uid);
        Assert.Equal(workspace.RequireActiveTab().DirectoryPath, _watcher.Path);
        Assert.Equal(2, _watcher.Starts);
    }

    [Fact]
    public void RootNotification_AdoptsExternalBasketAndStopsWhenHidden()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        using var coordinator = Create(workspace);
        coordinator.SetVisible(true);
        Drain();
        Assert.Equal(_harness.Temp.BasketsRoot, _rootWatcher.Path);
        Assert.False(_rootWatcher.IncludeSubdirectories);
        _harness.Temp.CreateBasketDirectory("repo-grooming-skill");
        _rootWatcher.Notify();
        Drain();
        Assert.Contains(workspace.Tabs, tab => Path.GetFileName(tab.DirectoryPath) == "repo-grooming-skill");
        coordinator.SetVisible(false);
        Assert.Null(_rootWatcher.Path);
        _harness.Temp.CreateBasketDirectory("while-hidden");
        coordinator.SetVisible(true);
        Drain();
        Assert.Contains(workspace.Tabs, tab => Path.GetFileName(tab.DirectoryPath) == "while-hidden");
    }

    [Fact]
    public void StatusOnlyChange_DoesNotRebuildTreeOrSuppressEcho()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        using var coordinator = Create(workspace);
        coordinator.SetVisible(true);
        Drain();
        _renders.Clear();
        workspace.CopyActiveBasketPath(string.Empty);
        Drain();
        Assert.Equal(new[] { false }, _renders);
        Assert.Equal(0, _watcher.Suppressions);
    }

    [Fact]
    public void QueuedOldBasketNotification_IsIgnoredAfterSwitch()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        using var coordinator = Create(workspace);
        coordinator.SetVisible(true);
        Drain();
        _watcher.Notify();
        workspace.CreateTab();
        var changes = 0;
        workspace.Changed += (_, _) => changes++;
        Drain();
        Assert.Equal(0, changes);
        Assert.Equal(workspace.RequireActiveTab().DirectoryPath, _watcher.Path);
    }

    [Fact]
    public void Hide_IgnoresQueuedNotification_AndShowReadsNewDiskContents()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        using var coordinator = Create(workspace);
        coordinator.SetVisible(true);
        Drain();
        _renders.Clear();
        _watcher.Notify();
        coordinator.SetVisible(false);
        File.WriteAllText(System.IO.Path.Combine(workspace.RequireActiveTab().DirectoryPath, "hidden.txt"), "external");
        Drain();
        Assert.Empty(_renders);
        Assert.Null(_watcher.Path);
        coordinator.SetVisible(true);
        Drain();
        Assert.Equal("hidden.txt", Assert.Single(workspace.GetEntries(string.Empty)).Name);
    }

    [Fact]
    public void WatcherErrorAndBurst_RecreateWatchAndRenderOnce()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        using var coordinator = Create(workspace);
        coordinator.SetVisible(true);
        Drain();
        _renders.Clear();
        _watcher.Notify();
        _watcher.Fail();
        _watcher.Notify();
        Drain();
        Assert.Equal(2, _watcher.Starts);
        Assert.Equal(new[] { true }, _renders);
        Assert.Equal(0, _watcher.Suppressions);
    }

    [Fact]
    public void OwnAndExternalChanges_AreCoalescedWithoutDroppingExternalFile()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        using var coordinator = Create(workspace);
        coordinator.SetVisible(true);
        Drain();
        _renders.Clear();
        workspace.AddQuickNote("own", string.Empty);
        File.WriteAllText(System.IO.Path.Combine(workspace.RequireActiveTab().DirectoryPath, "external.txt"), "external");
        _watcher.Notify();
        Drain();
        Assert.Contains(workspace.GetEntries(string.Empty), e => e.Name == "external.txt");
        Assert.Equal(1, _watcher.Suppressions);
        Assert.NotEmpty(_renders);
        Assert.All(_renders, tree => Assert.True(tree));
    }

    [Fact]
    public void StartFailure_ReportsOnceAndCanRetryOnNextShow()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        using var coordinator = Create(workspace);
        var errors = 0;
        workspace.Changed += (_, change) =>
        {
            if (change.Kind == WorkspaceChangeKind.Status && workspace.StatusIsError) errors++;
        };
        _watcher.ThrowOnStart = true;
        coordinator.SetVisible(true);
        Drain();
        Assert.Null(_watcher.Path);
        Assert.Equal(1, _watcher.Starts);
        Assert.Equal(1, errors);
        workspace.RequireError();
        workspace.ReportStatus("unrelated status");
        Drain();
        Assert.Equal(1, _watcher.Starts);
        _watcher.ThrowOnStart = false;
        coordinator.SetVisible(true);
        Drain();
        Assert.Equal(2, _watcher.Starts);
        Assert.Equal(workspace.RequireActiveTab().DirectoryPath, _watcher.Path);
    }

    [Fact]
    public void ExplicitRefresh_RetriesUnavailableWatchAndReadsDisk()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        using var coordinator = Create(workspace);
        _watcher.ThrowOnStart = true;
        coordinator.SetVisible(true);
        Drain();
        _renders.Clear();
        _watcher.ThrowOnStart = false;
        _harness.Temp.CreateBasketDirectory("manual-refresh");

        coordinator.RequestRefresh();
        Drain();

        Assert.Equal(2, _watcher.Starts);
        Assert.Equal(workspace.RequireActiveTab().DirectoryPath, _watcher.Path);
        Assert.Contains(workspace.Tabs, tab => tab.DisplayName == "manual-refresh");
        Assert.Equal(new[] { true }, _renders);
    }

    [Fact]
    public void SecondWatcherStartFailure_StopsBothAndDoesNotRetryOnStatus()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        using var coordinator = Create(workspace);
        _rootWatcher.ThrowOnStart = true;

        coordinator.SetVisible(true);
        Drain();

        Assert.Null(_watcher.Path);
        Assert.Null(_rootWatcher.Path);
        workspace.RequireError();
        workspace.ReportStatus("unrelated");
        Drain();
        Assert.Equal(1, _watcher.Starts);
        Assert.Equal(1, _rootWatcher.Starts);
    }

    [Fact]
    public void MissingBasketsRoot_PreservesTreeAndCanRefreshAfterRecovery()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        using var coordinator = Create(workspace);
        coordinator.SetVisible(true);
        Drain();
        _renders.Clear();
        var movedRoot = Path.Combine(_harness.Temp.Root, "Baskets-away");
        _watcher.Notify();
        Directory.Move(_harness.Temp.BasketsRoot, movedRoot);

        Drain();

        Assert.Equal(new[] { false }, _renders);
        workspace.RequireError();
        Directory.Move(movedRoot, _harness.Temp.BasketsRoot);
        _harness.Temp.CreateBasketDirectory("recovered-basket");
        _watcher.Notify();
        Drain();
        Assert.Contains(workspace.Tabs, tab => tab.DisplayName == "recovered-basket");
        Assert.Equal(new[] { false, true }, _renders);
    }

    [Fact]
    public void RefreshRequestedDuringDiskRead_IsNotLost()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        using var coordinator = Create(workspace);
        coordinator.SetVisible(true);
        Drain();
        _renders.Clear();
        var requestDuringRead = true;
        workspace.Changed += (_, change) =>
        {
            if (change.Kind != WorkspaceChangeKind.View || !requestDuringRead) return;
            requestDuringRead = false;
            coordinator.RequestRefresh();
        };

        coordinator.RequestRefresh();
        Drain();

        Assert.Equal(new[] { true, true }, _renders);
    }

    [Fact]
    public void RefreshRequestedDuringRender_IsNotLost()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var requestDuringRender = false;
        WorkspaceRefreshCoordinator? coordinator = null;
        using var lifetime = coordinator = new WorkspaceRefreshCoordinator(workspace, _watcher, _rootWatcher,
            action => { _queue.Enqueue(action); return true; }, refresh =>
            {
                _renders.Add(refresh.RebuildTree);
                if (!requestDuringRender) return;
                requestDuringRender = false;
                coordinator!.RequestRefresh();
            }, _harness.Text);
        coordinator.SetVisible(true);
        Drain();
        _renders.Clear();
        requestDuringRender = true;

        workspace.ReportStatus("render then refresh");
        Drain();

        Assert.Equal(new[] { false, true }, _renders);
    }

    [Fact]
    public void RenderFailure_DoesNotPreventNextChangeFromRendering()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var failRender = true;
        using var coordinator = new WorkspaceRefreshCoordinator(workspace, _watcher, _rootWatcher,
            action => { _queue.Enqueue(action); return true; }, refresh =>
            {
                if (failRender) throw new IOException("render failed");
                _renders.Add(refresh.RebuildTree);
            }, _harness.Text);
        coordinator.SetVisible(true);
        Assert.Throws<IOException>(Drain);
        failRender = false;

        workspace.ReportStatus("next change");
        Drain();

        Assert.Equal(new[] { false }, _renders);
    }

    [Fact]
    public void OldCallbackCapturedBeforeSwitch_IsIgnoredWhenInvokedAfterSwitch()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        using var coordinator = Create(workspace);
        coordinator.SetVisible(true);
        Drain();
        var oldNotification = _watcher.CaptureNotification();
        workspace.CreateTab();
        Drain();
        _renders.Clear();

        oldNotification();
        Drain();

        Assert.Empty(_renders);
    }

    [Fact]
    public void Dispose_IgnoresQueuedWorkAndCapturedCallbacks()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var coordinator = Create(workspace);
        coordinator.SetVisible(true);
        Drain();
        _renders.Clear();
        var oldNotification = _watcher.CaptureNotification();
        _watcher.Notify();
        coordinator.RequestRefresh();

        coordinator.Dispose();
        oldNotification();
        coordinator.SetVisible(true);
        coordinator.RequestRefresh();
        Drain();

        Assert.Empty(_renders);
        Assert.Null(_watcher.Path);
        Assert.Null(_rootWatcher.Path);
        Assert.Equal(1, _watcher.Starts);
    }

    [Fact]
    public void RejectedDispatch_CanScheduleNextChange()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        var reject = true;
        using var coordinator = new WorkspaceRefreshCoordinator(workspace, _watcher, _rootWatcher,
            action => { if (reject) return false; _queue.Enqueue(action); return true; }, refresh => _renders.Add(refresh.RebuildTree),
            _harness.Text);
        workspace.Refresh();
        reject = false;
        workspace.ReportStatus("next");
        Drain();
        Assert.Equal(new[] { true }, _renders);
    }

    private sealed class FakeWatcher : IWorkspaceWatcher
    {
        public event EventHandler? Changed;
        public event EventHandler? Error;
        public string? Path { get; private set; }
        public int Starts { get; private set; }
        public int Suppressions { get; private set; }
        public bool ThrowOnStart { get; set; }
        public bool IncludeSubdirectories { get; private set; }
        public void Watch(string directory, bool includeSubdirectories = true)
        {
            Starts++;
            if (ThrowOnStart) throw new IOException("watch failed");
            Path = directory;
            IncludeSubdirectories = includeSubdirectories;
        }
        public void Stop() => Path = null;
        public void SuppressEcho() => Suppressions++;
        public void Dispose() => Stop();
        public void Notify() => Changed?.Invoke(this, EventArgs.Empty);
        public Action CaptureNotification()
        {
            var notification = Changed;
            return () => notification?.Invoke(this, EventArgs.Empty);
        }
        public void Fail() => Error?.Invoke(this, EventArgs.Empty);
    }
}
