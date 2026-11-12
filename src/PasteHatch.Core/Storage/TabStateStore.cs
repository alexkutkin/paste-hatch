namespace PasteHatch.Core;

public sealed class TabStateStore : ITabStateStore
{
    private readonly JsonFile<WorkspaceState> _file;

    public TabStateStore(string stateFilePath)
    {
        StateFilePath = stateFilePath;
        _file = new JsonFile<WorkspaceState>(stateFilePath);
    }

    public static TabStateStore CreateDefault() => new(PasteHatchPaths.StateFile);

    public string StateFilePath { get; }

    public WorkspaceState Load()
    {
        var state = _file.Load();
        state.Tabs ??= [];
        // Version 2/3 used the UID as its directory component; upgrades must not move files.
        if (state.Version < 4)
        {
            foreach (var tab in state.Tabs) tab.DirectoryName = tab.Uid;
        }
        if (state.PendingRename is { SourceDirectoryName: null } pending)
            pending.SourceDirectoryName = state.Tabs.FirstOrDefault(tab => tab.Uid == pending.Uid)?.DirectoryName;
        state.Version = WorkspaceState.CurrentVersion;
        return state;
    }

    public void Save(WorkspaceState state) => _file.Save(state);
}
