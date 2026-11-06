using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using PasteHatch.Core;
using PasteHatch.Platform;
using Windows.Graphics;

namespace PasteHatch;

public sealed partial class MainWindow
{
    private const int DefaultWidthDip = 1200;
    private const int DefaultHeightDip = 800;
    private const double RailWidth = 220;
    private const double PreviewWidth = 320;
    private const double SplitterWidth = 8;
    private const double MinRailWidth = 160;
    private const double MinTreeWidth = 160;
    private const double MinPreviewWidth = 200;
    private double _railWidth = RailWidth;
    private double _previewWidth = PreviewWidth;

    private void ConfigureWindowShell()
    {
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 720;
            presenter.PreferredMinimumHeight = 480;
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        AppIcon.ApplyToWindow(this);
    }

    private void ConfigureWindowPlacement()
    {
        try
        {
            var scale = NativeWindow.GetDpiScale(GetWindowHandle());
            var workingArea = NativeWindow.GetWorkingArea(GetWindowHandle());
            var placement = _settings.RememberWindowPlacement ? _workspace.Geometry.Placement : null;

            if (placement is not null)
            {
                var width = Math.Clamp(placement.Width, 400, workingArea.Width);
                var height = Math.Clamp(placement.Height, 300, workingArea.Height);
                var left = Math.Clamp(placement.Left, workingArea.Left, workingArea.Right - width);
                var top = Math.Clamp(placement.Top, workingArea.Top, workingArea.Bottom - height);
                _appWindow.MoveAndResize(new RectInt32(left, top, width, height));
                return;
            }

            var defaultWidth = (int)Math.Round(DefaultWidthDip * scale);
            var defaultHeight = (int)Math.Round(DefaultHeightDip * scale);
            defaultWidth = Math.Min(defaultWidth, workingArea.Width);
            defaultHeight = Math.Min(defaultHeight, workingArea.Height);

            _appWindow.MoveAndResize(new RectInt32(
                workingArea.Left + (workingArea.Width - defaultWidth) / 2,
                workingArea.Top + (workingArea.Height - defaultHeight) / 2,
                defaultWidth,
                defaultHeight));
        }
        catch (Exception)
        {
            // Placement is a convenience; the default OS placement is still usable.
        }
    }

    private void PersistWindowPlacement()
    {
        if (_isExiting || !_settings.RememberWindowPlacement)
        {
            return;
        }

        try
        {
            var position = _appWindow.Position;
            var size = _appWindow.Size;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return;
            }

            if (_workspace.Geometry.Save(new WindowPlacementState
                {
                    Left = position.X,
                    Top = position.Y,
                    Width = size.Width,
                    Height = size.Height,
                }))
            {
                _workspace.SaveState();
            }
        }
        catch (Exception)
        {
            // Losing the window rectangle is not worth interrupting the user.
        }
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        ApplyPaneWidths();
        ApplyTheme();
        RefreshAll();
        UpdateAddButtonState();
        _liveRefresh.SetVisible(NativeWindow.IsVisible(GetWindowHandle()));

        if (_startupError is not null && !_startupErrorReported)
        {
            _startupErrorReported = true;
            ShowError(_startupError);
        }
    }

    private void ApplyTheme() => WindowTitleBarTheme.Apply(this, RootGrid.ActualTheme);

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExiting)
        {
            return;
        }

        args.Cancel = true;
        HideToTray();
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPositionChange && !args.DidSizeChange)
        {
            return;
        }

        _placementTimer.Stop();
        _placementTimer.Start();
    }

    private void OnRailSplitterDragDelta(object sender, DragDeltaEventArgs e)
    {
        _railWidth += e.HorizontalChange;
        ApplyPaneWidths();
    }

    private void OnPreviewSplitterDragDelta(object sender, DragDeltaEventArgs e)
    {
        _previewWidth = PreviewColumn.ActualWidth - e.HorizontalChange;
        ApplyPaneWidths();
        _previewWidth = PreviewColumn.Width.Value;
    }

    private void OnPreviewSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_workspace.Geometry.SavePreviewWidth(_previewWidth))
        {
            _workspace.SaveState();
        }
    }

    private void OnWorkspaceSizeChanged(object sender, SizeChangedEventArgs e) => ApplyPaneWidths();

    private void ApplyPaneWidths()
    {
        var available = WorkspaceGrid.ActualWidth - 2 * SplitterWidth;
        if (available <= 0) return;

        var railMaximum = Math.Max(MinRailWidth, Math.Min(360, available - MinTreeWidth - MinPreviewWidth));
        _railWidth = Math.Clamp(_railWidth, MinRailWidth, railMaximum);
        var previewMaximum = Math.Max(MinPreviewWidth, available - _railWidth - MinTreeWidth);
        RailColumn.Width = new GridLength(_railWidth);
        PreviewColumn.Width = new GridLength(Math.Clamp(_previewWidth, MinPreviewWidth, previewMaximum));
    }
}
