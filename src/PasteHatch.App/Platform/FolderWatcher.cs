using System;
using System.IO;
using System.Threading;
using PasteHatch.Core;

namespace PasteHatch.Platform;

/// <summary>
/// One lazy file watcher for the active basket. It uses OS change notifications (no polling),
/// coalesces bursts with a short timer, ignores last-access events so our own previews cannot
/// feed the watcher, and stops completely when the window is hidden.
/// </summary>
public sealed class FolderWatcher : IWorkspaceWatcher
{
    private const int DebounceMilliseconds = 500;
    private const int BufferSize = 64 * 1024;

    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private long _generation;
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
            _timer?.Dispose();
            _timer = null;
            _generation++;
            Interlocked.Exchange(ref _dirty, 0);
            _suspendedUntilUtc = default;
            var generation = _generation;

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

            watcher.Created += (_, _) => Schedule(generation);
            watcher.Changed += (_, _) => Schedule(generation);
            watcher.Deleted += (_, _) => Schedule(generation);
            watcher.Renamed += (_, _) => Schedule(generation);
            watcher.Error += (_, _) => OnWatcherError(generation);

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
            _generation++;
            _suspendedUntilUtc = default;
            Interlocked.Exchange(ref _dirty, 0);
        }
    }

    /// <summary>Coalesces own and external changes until the operation has settled.</summary>
    public void SuspendFor(TimeSpan duration)
    {
        lock (_gate)
        {
            _suspendedUntilUtc = DateTime.UtcNow + duration;
            if (_dirty != 0)
                _timer?.Change(duration + TimeSpan.FromMilliseconds(DebounceMilliseconds), Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _generation++;
            DisposeWatcher();
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Schedule(long generation)
    {
        lock (_gate)
        {
            if (_disposed || _watcher is null || generation != _generation)
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

            _timer ??= new Timer(OnTimer, generation, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(delay, Timeout.Infinite);
            Interlocked.Exchange(ref _dirty, 1);
        }
    }

    private void OnTimer(object? state)
    {
        lock (_gate)
        {
            if (_disposed || _watcher is null || state is not long generation || generation != _generation ||
                Interlocked.Exchange(ref _dirty, 0) == 0)
                return;
            // Keep generation validation and publication atomic with Stop/Watch.
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnWatcherError(long generation)
    {
        lock (_gate)
        {
            if (_disposed || _watcher is null || generation != _generation) return;
            Error?.Invoke(this, EventArgs.Empty);
        }
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
