namespace PasteHatch.Core;

public enum WorkspaceChangeKind { Status, View, Contents }

public sealed class WorkspaceChange(WorkspaceChangeKind kind) : EventArgs
{
    public WorkspaceChangeKind Kind { get; } = kind;
}
