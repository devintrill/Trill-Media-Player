using System.Windows.Controls;
using System.Windows;

namespace Trill
{
    public sealed partial class MainWindow
    {
        private void BuildSpectrum()
        {
            Grid host = Find<Grid>("SpectrumBars");
            host.Height = 180;
            host.Children.Add(_spectrum);
        }

        private void StartSpectrum(MediaItem item) { _spectrum.Clear(); _spectrum.Visibility = Visibility.Visible; }
        private void StopSpectrum() { _audioSpectrum.Cancel(); _spectrum.Clear(); }
        private void UpdateSpectrum()
        {
            _spectrum.Visibility = _audioSpectrum.Unavailable ? Visibility.Hidden : Visibility.Visible;
            if (_current != null && _current.IsAudio && _opened && !_failed && (_playing || _scrubbing))
                _audioSpectrum.Request(_scrubbing ? _seek.Value : _video.Position.TotalSeconds);
        }
    }
}