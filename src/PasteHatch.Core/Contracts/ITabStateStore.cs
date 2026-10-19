namespace PasteHatch.Core;

/// <summary>Persists the tab strip between launches (order, display names, active tab, window).</summary>
public interface ITabStateStore
{
    WorkspaceState Load();

    void Save(WorkspaceState state);
}
