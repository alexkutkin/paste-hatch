using System;
using System.Threading;
using PasteHatch.Core;

namespace PasteHatch.Platform;

/// <summary>
/// Wraps <see cref="SingleInstanceGuard"/> for the application startup path. The primary
/// instance owns the guard and a background listener thread that turns "a second PasteHatch was
/// launched" into an <c>onShowRequest</c> callback; a second instance signals the primary one and
/// gets <c>null</c> from <see cref="Acquire"/> so it can exit immediately.
/// </summary>
public sealed class SingleInstanceHost : IDisposable
{
    private static readonly TimeSpan ListenTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ShutdownJoinTimeout = TimeSpan.FromSeconds(2);

    private readonly SingleInstanceGuard _guard;
    private readonly Action _onShowRequest;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Thread _listenerThread;
    private bool _disposed;

    private SingleInstanceHost(SingleInstanceGuard guard, Action onShowRequest)
    {
        _guard = guard;
        _onShowRequest = onShowRequest;
        _listenerThread = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name = "PasteHatch.SingleInstanceListener",
        };
        _listenerThread.Start();
    }

    /// <summary>
    /// Acquires single-instance ownership for this process. Returns <c>null</c> when another
    /// PasteHatch instance is already running; in that case the running instance has already been
    /// signalled to show its window and the caller should exit.
    /// </summary>
    /// <param name="onShowRequest">
    /// Invoked on the listener (background) thread every time a second launch asks for the window.
    /// The callback must marshal to the UI thread itself (for example via
    /// <c>DispatcherQueue.TryEnqueue</c>) and must not block the listener thread.
    /// </param>
    public static SingleInstanceHost? Acquire(Action onShowRequest)
    {
        ArgumentNullException.ThrowIfNull(onShowRequest);

        var guard = SingleInstanceGuard.TryAcquire();
        if (!guard.IsPrimary)
        {
            guard.SignalPrimaryInstance();
            guard.Dispose();
            return null;
        }

        try
        {
            return new SingleInstanceHost(guard, onShowRequest);
        }
        catch (Exception)
        {
            guard.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Stops the listener and releases the single-instance mutex. Call this on the thread that
    /// called <see cref="Acquire"/> (normally the UI thread), because releasing the mutex is
    /// thread-affine.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();

        if (_listenerThread != Thread.CurrentThread)
        {
            _listenerThread.Join(ShutdownJoinTimeout);
        }

        _guard.Dispose();
        _shutdown.Dispose();
    }

    private void ListenLoop()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            bool showRequested;
            try
            {
                showRequested = _guard.WaitForShowRequest(ListenTimeout);
            }
            catch (ObjectDisposedException)
            {
                // The guard was disposed while the listener was waiting; nothing left to do.
                return;
            }
            catch (Exception)
            {
                // Defensive: a broken wait must not take the whole process down.
                return;
            }

            if (!showRequested)
            {
                continue;
            }

            try
            {
                _onShowRequest();
            }
            catch (Exception)
            {
                // A failing callback (for example, the window is not created yet) must not
                // stop the listener from handling later show requests.
            }
        }
    }
}
