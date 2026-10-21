using System;
using System.IO;
using System.Threading;

namespace PasteHatch.Platform;

/// <summary>
/// One lazy file watcher for the active basket. It uses OS change notifications (no polling),
/// coalesces bursts with a short timer, ignores last-access events so our own previews cannot
/// feed the watcher, and stops completely when the window is hidden.
/// </summary>
public sealed class FolderWatcher : IDisposable
{
    private const int DebounceMilliseconds = 500;
    private const int BufferSize = 64 * 1024;

    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private string? _directory;
    private DateTime _suspendedUntilUtc;
    private int _dirty;
    private bool _disposed;

    /// <summary>Raised on a background thread after a burst of file-system changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the watcher loses track of changes (for example buffer overflow).</summary>
    public event EventHandler? Error;

    public void Watch(string directory, bool includeSubdirectories = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            DisposeWatcher();
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            Interlocked.Exchange(ref _dirty, 0);
            _directory = directory;

            var watcher = new FileSystemWatcher(directory)
            {
                Filter = "*",
                IncludeSubdirectories = includeSubdirectories,
                InternalBufferSize = BufferSize,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.CreationTime
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size,
            };

            watcher.Created += (_, _) => Schedule();
            watcher.Changed += (_, _) => Schedule();
            watcher.Deleted += (_, _) => Schedule();
            watcher.Renamed += (_, _) => Schedule();
            watcher.Error += (_, _) => OnWatcherError();

            _watcher = watcher;
            watcher.EnableRaisingEvents = true;
        }
    }

    /// <summary>Stops notifications but keeps the object reusable.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            DisposeWatcher();
            _timer?.Dispose();
            _timer = null;
            _directory = null;
            Interlocked.Exchange(ref _dirty, 0);
        }
    }

    /// <summary>Ignores events caused by our own paste/move/delete for a short while.</summary>
    public void SuspendFor(TimeSpan duration)
    {
        lock (_gate)
        {
            _suspendedUntilUtc = DateTime.UtcNow + duration;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            DisposeWatcher();
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Schedule()
    {
        lock (_gate)
        {
            if (_disposed || _watcher is null)
            {
                return;
            }

            var delay = DebounceMilliseconds;
            var remaining = _suspendedUntilUtc - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                // Our own paste/move is in progress. Coalesce its events and check once after the
                // suppression window instead of dropping them and missing a later external change.
                delay += (int)remaining.TotalMilliseconds;
            }

            _timer ??= new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(delay, Timeout.Infinite);
            Interlocked.Exchange(ref _dirty, 1);
        }
    }

    private void OnTimer(object? state)
    {
        if (Interlocked.Exchange(ref _dirty, 0) == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed || _watcher is null)
            {
                return;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnWatcherError()
    {
        lock (_gate)
        {
            if (_disposed || _watcher is null)
            {
                return;
            }
        }

        Error?.Invoke(this, EventArgs.Empty);
    }

    private void DisposeWatcher()
    {
        if (_watcher is null)
        {
            return;
        }

        try
        {
            _watcher.EnableRaisingEvents = false;
        }
        catch (Exception)
        {
            // The directory may already be gone.
        }

        _watcher.Dispose();
        _watcher = null;
    }
}
