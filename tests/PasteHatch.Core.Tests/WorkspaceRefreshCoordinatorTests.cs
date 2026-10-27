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
        Assert.Contains(workspace.Tabs, tab => tab.Uid == "repo-grooming-skill");
        coordinator.SetVisible(false);
        Assert.Null(_rootWatcher.Path);
        _harness.Temp.CreateBasketDirectory("while-hidden");
        coordinator.SetVisible(true);
        Drain();
        Assert.Contains(workspace.Tabs, tab => tab.Uid == "while-hidden");
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
        Assert.True(_renders.All(tree => tree));
    }

    [Fact]
    public void StartFailure_ReportsOnceAndCanRetryOnNextShow()
    {
        var workspace = _harness.CreateInitializedWorkspace();
        using var coordinator = Create(workspace);
        _watcher.ThrowOnStart = true;
        coordinator.SetVisible(true);
        Drain();
        Assert.Null(_watcher.Path);
        Assert.NotNull(workspace.StatusMessage);
        _watcher.ThrowOnStart = false;
        coordinator.SetVisible(true);
        Drain();
        Assert.Equal(workspace.RequireActiveTab().DirectoryPath, _watcher.Path);
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
        public void Fail() => Error?.Invoke(this, EventArgs.Empty);
    }
}
