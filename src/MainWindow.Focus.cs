using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Trill
{
    public sealed partial class MainWindow
    {
        private bool _focusMode, _focusSidebar, _focusBottom, _focusTop;

        private void SetupFocus()
        {
            Click("FocusButton", delegate { ToggleFocus(); });
            Panel.SetZIndex(Find<Border>("TransportBar"), 30);
        }

        private void ToggleFocus()
        {
            EndScrub(); StopHover(); Keyboard.ClearFocus();
            _focusMode = !_focusMode;
            _focusSidebar = _focusBottom = _focusTop = false;
            Find<Button>("FocusButton").Foreground = _focusMode ? new SolidColorBrush(Color.FromRgb(184, 184, 184)) : Brushes.LightGray;
            Find<Button>("FocusButton").ToolTip = _focusMode ? "Exit focus mode · Esc" : "Focus mode · Ctrl+Shift+F";
            SetPlaylistVisible(); ApplyFocusChrome();
        }

        private void FocusPointer(Point point)
        {
            if (!_focusMode) return;
            bool sidebar = point.X <= 12 || (_focusSidebar && point.X <= _sidebarWidth + 12);
            bool bottom = point.Y >= _root.ActualHeight - 18 || (_focusBottom && point.Y >= _root.ActualHeight - 70);
            bool top = point.Y <= 12 || (_focusTop && point.Y <= 58);
            if (_scrubbing) bottom = true;
            if (_focusSidebar != sidebar) { _focusSidebar = sidebar; SetPlaylistVisible(); if (!sidebar) StopHover(); }
            _focusBottom = bottom; _focusTop = top;
            ApplyFocusChrome();
        }

        private void ApplyFocusChrome()
        {
            Border bar = Find<Border>("TransportBar");
            Grid.SetRow(bar, (_focusMode || _fullscreen) ? 0 : 1);
            bar.VerticalAlignment = (_focusMode || _fullscreen) ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
            bar.Visibility = !_focusMode || _focusBottom ? Visibility.Visible : Visibility.Hidden;
            bar.Opacity = 1; bar.IsHitTestVisible = !_focusMode || _focusBottom;
            Button zoom = Find<Button>("ZoomResetButton");
            zoom.Opacity = !_focusMode || _focusTop ? 1 : 0;
            zoom.IsHitTestVisible = !_focusMode || _focusTop;
            zoom.IsTabStop = !_focusMode || _focusTop;
            Find<Border>("StatusToast").Opacity = _focusMode ? 0 : 1;
            Cursor = _focusMode && !_focusSidebar && !_focusBottom && !_focusTop ? Cursors.None : Cursors.Arrow;
            Find<Grid>("Stage").Cursor = Cursor == Cursors.None ? Cursors.None : _zoom > 1 ? Cursors.Hand : Cursors.Arrow;
        }

        private void SetFocusSidebar()
        {
            Grid body = Find<Grid>("BodyGrid");
            ColumnDefinition column = body.ColumnDefinitions[0];
            _sidebarAnimating = false;
            column.MinWidth = 0;
            UiMotion.SetColumnWidth(column, 0, delegate { column.MaxWidth = 0; column.Width = new GridLength(0); });
            column.MaxWidth = 0;
            body.ColumnDefinitions[1].Width = new GridLength(0);
            body.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            Find<GridSplitter>("SidebarSplitter").Visibility = Visibility.Collapsed;
            Border panel = Find<Border>("PlaylistPanel");
            Grid.SetColumnSpan(panel, 3); Panel.SetZIndex(panel, 20);
            panel.HorizontalAlignment = HorizontalAlignment.Left; panel.Width = _sidebarWidth;
            panel.Visibility = _focusSidebar ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
