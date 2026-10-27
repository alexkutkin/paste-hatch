namespace PasteHatch.Core;

/// <summary>
/// Kinds are added only when a consumer gains a real distinction to make; never reserve values for
/// future use. <see cref="Status"/> is a message change with nothing to re-read,
/// <see cref="View"/> means presentation must rebuild, and <see cref="Contents"/> additionally means
/// the app's own writes are on disk, so their watcher echo gets suppressed.
/// </summary>
public enum WorkspaceChangeKind { Status, View, Contents }

/// <summary>One published change of the workspace.</summary>
public sealed class WorkspaceChange(WorkspaceChangeKind kind) : EventArgs
{
    public WorkspaceChangeKind Kind { get; } = kind;
}

/// <summary>
/// What the window must do after coalesced changes: rebuild the tree, or refresh the surrounding
/// presentation only. This is the output contract of <see cref="WorkspaceRefreshCoordinator"/>.
/// </summary>
public readonly record struct ViewRefresh(bool RebuildTree);
