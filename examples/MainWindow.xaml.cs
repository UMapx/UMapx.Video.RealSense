using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UMapx.Imaging;

namespace UMapx.Video.RealSense.Example
{
    public partial class MainWindow : System.Windows.Window
    {
        private readonly Func<IVideoDepthSource> _createSource;
        private readonly object _framesSync = new object();
        private readonly DispatcherTimer _renderTimer;
        private volatile IVideoDepthSource _source;
        private BitmapSource _pendingColor;
        private BitmapSource _pendingDepth;
        private Task _connectionTask = Task.CompletedTask;
        private Task _shutdownTask;
        private volatile bool _closing;
        private bool _allowClose;
        private bool _closeInProgress;
        private bool _hasError;
        private bool _finished;

        public MainWindow() : this(() => new RealSenseVideoSource()) { }

        internal MainWindow(Func<IVideoDepthSource> createSource)
        {
            _createSource = createSource ?? throw new ArgumentNullException(nameof(createSource));
            InitializeComponent();
            Loaded += async (_, _) => await ConnectAsync();
            Closing += OnClosing;
            _renderTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _renderTimer.Tick += (_, _) => RenderPendingFrames();
            _renderTimer.Start();
        }

        private async void OnReconnect(object sender, RoutedEventArgs e) => await ConnectAsync();

        internal Task ConnectAsync()
        {
            if (_closing || !_connectionTask.IsCompleted) return _connectionTask;
            return _connectionTask = ConnectCoreAsync();
        }

        private async Task ConnectCoreAsync()
        {
            reconnectButton.IsEnabled = false;
            statusText.Text = "Connecting to RealSense camera...";
            _hasError = false;
            _finished = false;
            try
            {
                await DisconnectAsync();
                var source = await Task.Run(_createSource);
                if (_closing)
                {
                    await Task.Run(source.Dispose);
                    return;
                }
                _source = source;
                source.NewFrame += OnNewFrame;
                source.NewDepth += OnNewDepth;
                source.VideoSourceError += OnVideoSourceError;
                source.PlayingFinished += OnPlayingFinished;
                statusText.Text = "Waiting for color and depth frames...";
                source.Start();
            }
            catch (Exception error)
            {
                ShowError(error.Message);
                try { await DisconnectAsync(); }
                catch (Exception cleanupError) { ShowError(error.Message + " " + cleanupError.Message); }
            }
            finally
            {
                if (!_closing) reconnectButton.IsEnabled = true;
            }
        }

        private async Task DisconnectAsync()
        {
            var source = _source;
            _source = null;
            lock (_framesSync) { _pendingColor = null; _pendingDepth = null; }
            imgColor.Source = null;
            imgDepth.Source = null;
            if (source == null) return;
            source.NewFrame -= OnNewFrame;
            source.NewDepth -= OnNewDepth;
            source.VideoSourceError -= OnVideoSourceError;
            source.PlayingFinished -= OnPlayingFinished;
            // SDK shutdown can wait for a callback; keep the dispatcher free while it completes.
            await Task.Run(() =>
            {
                try { source.SignalToStop(); }
                finally { source.Dispose(); }
            });
        }

        private void OnNewFrame(object sender, NewFrameEventArgs e)
        {
            if (_closing || !ReferenceEquals(sender, _source)) return;
            var image = ToBitmapImage(e.Frame);
            lock (_framesSync)
            {
                if (!_closing && ReferenceEquals(sender, _source)) _pendingColor = image;
            }
        }

        private void OnNewDepth(object sender, NewDepthEventArgs e)
        {
            if (_closing || !ReferenceEquals(sender, _source)) return;
            using var bitmap = e.Depth.Equalize().FromDepth();
            var image = ToBitmapImage(bitmap);
            lock (_framesSync)
            {
                if (!_closing && ReferenceEquals(sender, _source)) _pendingDepth = image;
            }
        }

        internal void RenderPendingFrames()
        {
            if (_closing) return;
            BitmapSource color, depth;
            lock (_framesSync)
            {
                color = _pendingColor;
                depth = _pendingDepth;
                _pendingColor = null;
                _pendingDepth = null;
            }
            if (color != null) imgColor.Source = color;
            if (depth != null) imgDepth.Source = depth;
            if ((color != null || depth != null) && !_hasError && !_finished)
                statusText.Text = "Streaming color and depth.";
        }

        private void OnVideoSourceError(object sender, VideoSourceErrorEventArgs e)
        {
            PostStatus(sender, () => ShowError(e.Description));
        }

        private void OnPlayingFinished(object sender, ReasonToFinishPlaying reason)
        {
            PostStatus(sender, () =>
            {
                _finished = true;
                if (!_hasError) statusText.Text = "Capture stopped. Connect a camera and click Reconnect.";
            });
        }

        private void PostStatus(object sender, Action update)
        {
            if (_closing || Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_closing && ReferenceEquals(sender, _source)) update();
            }));
        }

        private void ShowError(string message)
        {
            _hasError = true;
            statusText.Text = "Camera error: " + message + " Connect a camera and click Reconnect.";
        }

        internal Task ShutdownAsync()
        {
            if (_shutdownTask != null) return _shutdownTask;
            _closing = true;
            _renderTimer.Stop();
            reconnectButton.IsEnabled = false;
            return _shutdownTask = ShutdownCoreAsync();
        }

        private async Task ShutdownCoreAsync()
        {
            try { await _connectionTask; }
            finally { await DisconnectAsync(); }
        }

        private async void OnClosing(object sender, CancelEventArgs e)
        {
            if (_allowClose) return;
            e.Cancel = true;
            if (_closeInProgress) return;
            _closeInProgress = true;
            try { await ShutdownAsync(); }
            catch (Exception error) { Trace.TraceError("RealSense shutdown failed: {0}", error); }
            finally
            {
                _allowClose = true;
                _ = Dispatcher.BeginInvoke(new Action(Close));
            }
        }

        internal static BitmapImage ToBitmapImage(Bitmap bitmap)
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Bmp);
            stream.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
