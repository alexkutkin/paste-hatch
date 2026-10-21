namespace PasteHatch.Core;

/// <summary>Notifications for one active basket; callbacks may arrive on a background thread.</summary>
public interface IWorkspaceWatcher : IDisposable
{
    event EventHandler? Changed;
    event EventHandler? Error;
    void Watch(string directory, bool includeSubdirectories = true);
    void Stop();
    void SuspendFor(TimeSpan duration);
}
