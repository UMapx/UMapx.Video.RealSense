using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace UMapx.Video.RealSense
{
    /// <summary>
    /// Video source for Intel RealSense Depth camera.
    /// <remarks>
    /// This video source class captures video data from Intel RealSense Depth camera.
    /// More information can be found on the website:
    /// https://www.intelrealsense.com/stereo-depth/
    /// </remarks>
    /// </summary>
    public class RealSenseVideoSource : IVideoDepthSource
    {
        #region Fields

        private readonly object _sync = new object();
        private readonly IRealSenseCapture _capture;
        private VideoCapabilities _videoResolution;
        private VideoCapabilities _depthResolution;
        private CaptureSession _session;
        private int _framesReceived;
        private long _bytesReceived;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates video source for Intel RealSense Depth camera.
        /// </summary>
        /// <remarks>Discovers the first connected device synchronously. SDK loading and
        /// discovery errors are thrown by the constructor, before event handlers can be attached.</remarks>
        /// <exception cref="InvalidOperationException">No RealSense camera was found.</exception>
        public RealSenseVideoSource() : this(new RealSenseCapture())
        {
        }

        internal RealSenseVideoSource(IRealSenseCapture capture)
        {
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
            Source = capture.Source;
            SerialNumber = capture.SerialNumber;
            FirmwareVersion = capture.FirmwareVersion;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Video resolution to set.
        /// </summary>
        /// 
        /// <remarks><para>The property allows to set one of the video resolutions supported by the camera.
        /// Use <see cref="VideoResolutions"/> to get the list of supported video resolutions.</para>
        /// 
        /// <para><note>The property must be set before camera is started to make any effect.</note></para>
        /// 
        /// <para>Default value of the property is set to <see langword="null"/>, which means default video
        /// resolution is used.</para>
        /// </remarks>
        /// 
        public VideoCapabilities VideoResolution
        {
            get
            { 
                return _videoResolution;
            }
            set
            {
                _videoResolution = value; 
            }
        }

        /// <summary>
        /// Depth resolution to set.
        /// </summary>
        /// 
        /// <remarks><para>The property allows to set one of the depth resolutions supported by the camera.
        /// Use <see cref="DepthResolutions"/> to get the list of supported depth resolutions.</para>
        /// 
        /// <para><note>The property must be set before camera is started to make any effect.</note></para>
        /// 
        /// <para>Default value of the property is set to <see langword="null"/>, which means default depth
        /// resolution is used.</para>
        /// </remarks>
        /// 
        public VideoCapabilities DepthResolution
        {
            get
            {
                return _depthResolution;
            }
            set
            {
                _depthResolution = value;
            }
        }

        /// <summary>
        /// Returns camera source.
        /// </summary>
        public virtual string Source { get; private set; }

        /// <summary>
        /// Serial number of Intel RealSense Depth camera.
        /// </summary>
        public virtual string SerialNumber { get; private set; }

        /// <summary>
        /// Firmware version of Intel RealSense Depth camera.
        /// </summary>
        public virtual string FirmwareVersion { get; private set; }

        /// <summary>
        /// Received frames count.
        /// </summary>
        /// 
        /// <remarks>Number of frames the video source provided from the moment of the last
        /// access to the property.
        /// </remarks>
        /// 
        public int FramesReceived
        {
            get
            {
                return Interlocked.Exchange(ref _framesReceived, 0);
            }
        }

        /// <summary>
        /// Received bytes count.
        /// </summary>
        /// 
        /// <remarks>Number of bytes the video source provided from the moment of the last
        /// access to the property.
        /// </remarks>
        /// 
        public long BytesReceived
        {
            get
            {
                return Interlocked.Exchange(ref _bytesReceived, 0);
            }
        }

        /// <summary>
        /// State of the video source.
        /// </summary>
        /// 
        /// <remarks>Remains true until capture, completion handlers and cleanup have returned.
        /// Call <see cref="WaitForStop()"/> before restarting the source.</remarks>
        /// 
        public bool IsRunning
        {
            get { lock (_sync) return _session != null && _session.Thread.IsAlive; }
        }

        #endregion

        #region Events

        /// <summary>
        /// Intel RealSense depth action event handler.
        /// </summary>
        /// <remarks>Raised on the capture thread. Depth is aligned to color and supplied as
        /// a ushort[height, width] array of raw device depth units, not necessarily millimeters.</remarks>
        public event NewDepthEventHandler NewDepth;

        /// <summary>
        /// Intel RealSense frame action event handler.
        /// </summary>
        /// <remarks>Raised on the capture thread. The source disposes the bitmap after the
        /// handler returns; clone it to retain the frame and dispose the clone when finished.</remarks>
        public event NewFrameEventHandler NewFrame;

        /// <summary>
        /// Video source error event.
        /// </summary>
        /// 
        /// <remarks>This event is used to notify clients about any type of errors occurred in
        /// video source object, for example internal exceptions.</remarks>
        /// 
        public event VideoSourceErrorEventHandler VideoSourceError;

        /// <summary>
        /// Video playing finished event.
        /// </summary>
        /// 
        /// <remarks><para>This event is used to notify clients that the video playing has finished.</para>
        /// </remarks>
        /// 
        public event PlayingFinishedEventHandler PlayingFinished;

        #endregion

        #region Methods

        /// <summary>
        /// Start video source.
        /// </summary>
        /// 
        /// <remarks>Starts video source and return execution to caller. Video source
        /// object creates background thread and notifies about new frames with the
        /// help of <see cref="NewFrame"/> event. Camera startup and capture failures are reported
        /// through <see cref="VideoSourceError"/> followed by <see cref="PlayingFinished"/>.
        /// Calling Start while a previous run is still stopping has no effect.</remarks>
        /// 
        public void Start()
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(RealSenseVideoSource));
                if (_session != null && _session.Thread.IsAlive) return;

                Interlocked.Exchange(ref _framesReceived, 0);
                Interlocked.Exchange(ref _bytesReceived, 0);
                var session = new CaptureSession(this, _videoResolution, _depthResolution);
                _session = session;
                try { session.Thread.Start(); }
                catch
                {
                    _session = null;
                    throw;
                }
            }
        }

        /// <summary>
        /// Signal video source to stop its work.
        /// </summary>
        /// <remarks>Requests shutdown without waiting for frame handlers or releasing
        /// resources they may still be using. Call <see cref="WaitForStop()"/> to wait.</remarks>
        public void SignalToStop()
        {
            lock (_sync)
            {
                if (_session != null) _session.StopRequested = true;
            }
        }

        /// <summary>
        /// Stop video source.
        /// </summary>
        /// <remarks>Signals the source to stop and waits for completion. From a source
        /// callback, shutdown completes after the callback returns.</remarks>
        [Obsolete("Use SignalToStop followed by WaitForStop.")]
        public void Stop()
        {
            CaptureSession session;
            lock (_sync)
            {
                session = _session;
                if (session != null) session.StopRequested = true;
            }
            // Wait for the run we signalled, even if another caller starts a later run.
            WaitForStop(session);
        }

        /// <summary>
        /// Wait for video source has stopped.
        /// </summary>
        /// <remarks>Waits for capture, notifications and resource cleanup. Does not block
        /// inside this source's callbacks, which must return before shutdown can finish.</remarks>
        public void WaitForStop()
        {
            CaptureSession session;
            lock (_sync) session = _session;
            WaitForStop(session);
        }

        private static void WaitForStop(CaptureSession session)
        {
            if (session != null && session.Thread != Thread.CurrentThread)
                session.Thread.Join();
        }

        #endregion

        #region Private voids

        private void WorkerThread(CaptureSession session)
        {
            Exception error = null;
            try
            {
                if (!session.StopRequested)
                {
                    _capture.Start(session.VideoResolution, session.DepthResolution);
                    var waiting = Stopwatch.StartNew();
                    while (!session.StopRequested)
                    {
                        // Bound each SDK wait so stopping does not have to close an active pipeline.
                        using var frame = _capture.ReadFrame(100);
                        if (session.StopRequested) break;
                        if (frame == null)
                        {
                            if (waiting.ElapsedMilliseconds >= 5000)
                                throw new TimeoutException("No RealSense frames received for 5 seconds.");
                            continue;
                        }

                        OnNewFrame(frame.Color);
                        if (!session.StopRequested) OnNewDepth(frame.Depth);
                        waiting.Restart();
                    }
                }
            }
            catch (Exception exception)
            {
                error = exception;
            }
            finally
            {
                // This worker alone stops the SDK, including a partially failed startup.
                try { _capture.Stop(); }
                catch (Exception exception) { error = error ?? exception; }
                try { DisposeCaptureIfRequested(); }
                catch (Exception exception) { error = error ?? exception; }

                try
                {
                    if (error != null) OnVideoSourceError(error);
                    OnPlayingFinished(error == null
                        ? ReasonToFinishPlaying.StoppedByUser
                        : ReasonToFinishPlaying.VideoSourceError);
                }
                finally
                {
                    // A terminal event handler may itself have requested disposal.
                    try { DisposeCaptureIfRequested(); }
                    catch (Exception exception) { OnVideoSourceError(exception); }
                }
            }
        }

        private void OnVideoSourceError(Exception error)
        {
            var handlers = VideoSourceError;
            if (handlers == null) return;
            var args = new VideoSourceErrorEventArgs(error.Message);
            foreach (VideoSourceErrorEventHandler handler in handlers.GetInvocationList())
            {
                try { handler(this, args); }
                catch (Exception)
                {
                    // A diagnostic subscriber must not prevent cleanup or completion notification.
                }
            }
        }

        private void OnPlayingFinished(ReasonToFinishPlaying reason)
        {
            var handlers = PlayingFinished;
            if (handlers == null) return;
            foreach (PlayingFinishedEventHandler handler in handlers.GetInvocationList())
            {
                try { handler(this, reason); }
                catch (Exception)
                {
                    // Terminal subscribers cannot be allowed to escape the background thread.
                }
            }
        }

        private sealed class CaptureSession
        {
            internal readonly Thread Thread;
            internal readonly VideoCapabilities VideoResolution;
            internal readonly VideoCapabilities DepthResolution;
            internal volatile bool StopRequested;

            internal CaptureSession(RealSenseVideoSource owner, VideoCapabilities video, VideoCapabilities depth)
            {
                VideoResolution = video;
                DepthResolution = depth;
                Thread = new Thread(() => owner.WorkerThread(this))
                {
                    IsBackground = true,
                    Name = nameof(RealSenseVideoSource)
                };
            }
        }

        /// <summary>
        /// Called when video source gets new frame.
        /// </summary>
        /// <param name="frame">Frame</param>
        private void OnNewFrame(Bitmap frame)
        {
            Interlocked.Increment(ref _framesReceived);
            Interlocked.Add(ref _bytesReceived, (long)frame.Width * frame.Height * (Image.GetPixelFormatSize(frame.PixelFormat) >> 3));
            NewFrame?.Invoke(this, new NewFrameEventArgs(frame));
        }

        /// <summary>
        /// Called when video source gets new depth.
        /// </summary>
        /// <param name="depth">Depth</param>
        private void OnNewDepth(ushort[,] depth)
        {
            NewDepth?.Invoke(this, new NewDepthEventArgs(depth));
        }

        #endregion

        #region IDisposable

        private bool _disposed;
        private bool _captureDisposed;

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            CaptureSession session;
            lock (_sync)
            {
                _disposed = true;
                session = _session;
                if (session != null) session.StopRequested = true;
            }

            // A subscriber on this worker cannot wait for itself. Its finally performs disposal.
            if (session != null && session.Thread == Thread.CurrentThread) return;
            WaitForStop(session);
            DisposeCaptureIfRequested();
        }

        private void DisposeCaptureIfRequested()
        {
            lock (_sync)
            {
                if (_disposed && !_captureDisposed)
                {
                    _captureDisposed = true;
                    _capture.Dispose();
                }
            }
        }

        /// <inheritdoc/>
        ~RealSenseVideoSource()
        {
            Dispose(false);
        }

        #endregion

        #region Video capabilities

        /// <summary>
        /// Returns video capabilities array of depth stream.
        /// </summary>
        public VideoCapabilities[] DepthResolutions
        {
            get
            {
                lock (_sync)
                {
                    if (_disposed) throw new ObjectDisposedException(nameof(RealSenseVideoSource));
                    return _capture.DepthResolutions;
                }
            }
        }

        /// <summary>
        /// Returns video capabilities array of color stream.
        /// </summary>
        public VideoCapabilities[] VideoResolutions
        {
            get
            {
                lock (_sync)
                {
                    if (_disposed) throw new ObjectDisposedException(nameof(RealSenseVideoSource));
                    return _capture.VideoResolutions;
                }
            }
        }

        #endregion
    }
}
