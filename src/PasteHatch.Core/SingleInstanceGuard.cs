using System;
using System.Threading;

namespace PasteHatch.Core;

/// <summary>
/// Keeps PasteHatch a single-running-instance tray application: a second launch asks the
/// already running window to come back to the foreground instead of starting a second
/// process that would compete for the same baskets and tab state.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    public const string MutexName = @"Local\PasteHatch.SingleInstance";
    public const string ShowWindowEventName = @"Local\PasteHatch.ShowWindow";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showWindowEvent;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle showWindowEvent, bool isPrimary)
    {
        _mutex = mutex;
        _showWindowEvent = showWindowEvent;
        IsPrimary = isPrimary;
    }

    /// <summary>True for the first process; false for a second launch that must step aside.</summary>
    public bool IsPrimary { get; }

    public static SingleInstanceGuard TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        var showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
        return new SingleInstanceGuard(mutex, showWindowEvent, createdNew);
    }

    /// <summary>Wakes the primary instance so it restores and activates its window.</summary>
    public void SignalPrimaryInstance() => _showWindowEvent.Set();

    /// <summary>Waits (briefly) for a second launch asking the primary instance to show itself.</summary>
    public bool WaitForShowRequest(TimeSpan timeout) => _showWindowEvent.WaitOne(timeout);

    public void Dispose()
    {
        if (IsPrimary)
        {
            _mutex.ReleaseMutex();
        }

        _showWindowEvent.Dispose();
        _mutex.Dispose();
    }
}
