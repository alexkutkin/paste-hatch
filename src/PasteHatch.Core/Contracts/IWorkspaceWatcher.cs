namespace PasteHatch.Core;

/// <summary>
/// Notifications about file-system changes under watch. Events fire after the adapter's internal
/// locks are released, may arrive on any thread, and subscribers marshal into their own context; a
/// subscriber must not call <see cref="Watch"/>, <see cref="Stop"/> or <see cref="SuppressEcho"/>
/// from a handler. The coalescing windows belong to the adapter: it merges bursts of changes and
/// delays the echo of the app's own writes instead of reporting them one by one.
/// </summary>
public interface IWorkspaceWatcher : IDisposable
{
    event EventHandler? Changed;

    event EventHandler? Error;

    void Watch(string directory, bool includeSubdirectories = true);

    void Stop();

    /// <summary>The app just wrote to the watched tree; merge the echo of those writes.</summary>
    void SuppressEcho();
}
