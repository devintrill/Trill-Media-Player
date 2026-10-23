using System;
using System.Windows;
using System.Windows.Controls;

namespace Trill
{
    public sealed partial class MainWindow
    {
        private bool _frameStepping;
        private double _frameStepTarget;

        private void StepFrame(int direction)
        {
            if (_current == null || !_current.IsVideo || !_opened || _failed || _scrubbing) return;
            if (_scrubPreview.Unavailable) { Status("Frame stepping unavailable for this format"); return; }
            if (!_frameStepping)
            {
                _playing = false; _video.Pause();
                _frameStepTarget = _video.Position.TotalSeconds;
                _scrubPreview.Start(_current.Path, _frameStepTarget);
                _frameStepping = true;
            }
            double interval = _scrubPreview.FrameDuration;
            long frame = (long)Math.Floor((_frameStepTarget + .000001) / interval);
            _frameStepTarget = Math.Max(0, Math.Min(Math.Max(0, _duration - interval), (frame + direction) * interval));
            _scrubPreview.Request(_frameStepTarget);
            _video.Position = TimeSpan.FromSeconds(_frameStepTarget);
            UpdateTimeline(_frameStepTarget, _duration);
            UpdateControls(); RenderScrub(null, EventArgs.Empty);
        }

        private void FinishFrameStep()
        {

            if (!_frameStepping) return;
            _frameStepping = false;
            _scrubPreview.Stop(); HideScrubPreview();
        }

        private void UpdateZoomControl()
        {
            Button button = Find<Button>("ZoomResetButton");
            button.Content = Math.Round(_zoom * 100) + "%";
            button.Visibility = _current != null && !_current.IsAudio && _opened && !_failed ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
