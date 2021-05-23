using Intel.RealSense;
using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using UMapx.Video;
using static RealSense.FaceID.SensorEvents;

namespace RealSense.FaceID
{
    public class RealSenseVideoSource : IVideoSensorSource, IDisposable
    {
        #region Fields

        private readonly Colorizer _colorizer;
        private readonly Pipeline _pipeline;
        private readonly Context _ctx;
        private readonly Config _cfg;
        private CancellationTokenSource _tokenSource;
        private Bitmap _colorBitmap;
        private Bitmap _colorizedDepthBitmap;
        private int _framesReceived;
        private long _bytesReceived;

        #endregion

        #region Constructor

        /// <summary>
        /// Launch single Intel RealSense Camera with default <see cref="Stream.Color"/> to <see cref="Stream.Depth"/>
        /// </summary>
        public RealSenseVideoSource()
        {
            _colorizer = new Colorizer();

            _pipeline = new Pipeline();

            _ctx = new Context();

            var devices = _ctx.QueryDevices();
            var dev = devices.FirstOrDefault();

            Source = dev.Info[CameraInfo.Name];
            SerialNumber = dev.Info[CameraInfo.SerialNumber];
            FirmwareVersion = dev.Info[CameraInfo.FirmwareVersion];

            var sensors = dev.Sensors;

            var depthSensor = sensors[0];
            var depthProfile = depthSensor.StreamProfiles
                                .Where(p => p.Stream == Stream.Depth)
                                .OrderBy(p => p.Framerate)
                                .Select(p => p.As<VideoStreamProfile>()).First();


            var colorSensor = sensors[1];
            var colorProfile = colorSensor.StreamProfiles
                                .Where(p => p.Stream == Stream.Color)
                                .OrderBy(p => p.Framerate)
                                .Select(p => p.As<VideoStreamProfile>()).First();

            _cfg = new Config();

            _cfg.EnableStream(Stream.Depth, depthProfile.Width, depthProfile.Height, depthProfile.Format, depthProfile.Framerate);
            _cfg.EnableStream(Stream.Color, colorProfile.Width, colorProfile.Height, colorProfile.Format, colorProfile.Framerate);

            _tokenSource = new CancellationTokenSource();
        }

        // later create constructor with cutom config based on json file

        #endregion

        #region Methods

        public string Source { get; private set; }

        /// <summary>
        /// Serial number of Intel RealSense camera.
        /// </summary>
        public string SerialNumber { get; private set; }

        /// <summary>
        /// Firmware version of Intel RealSense camera.
        /// </summary>
        public string FirmwareVersion { get; private set; }

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
                int frames = _framesReceived;
                _framesReceived = 0;
                return frames;
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
                long bytes = _bytesReceived;
                _bytesReceived = 0;
                return bytes;
            }
        }

        /// <summary>
        /// State of the video source.
        /// </summary>
        /// 
        /// <remarks>Current state of video source object - running or not.</remarks>
        /// 
        public bool IsRunning { get; private set;}

        /// <summary>
        /// Intel RealSense frames actions event handler. 
        /// </summary>
        public event NewSensorsEventHandler NewSensorsFrames;

        /// <summary>
        /// 
        /// </summary>
        public event NewFrameEventHandler NewFrame;

        public event VideoSourceErrorEventHandler VideoSourceError;

        public event PlayingFinishedEventHandler PlayingFinished;

        /// <summary>
        /// Start video source.
        /// </summary>
        /// 
        /// <remarks>Starts video source and return execution to caller. Video source
        /// object creates background thread and notifies about new frames with the
        /// help of <see cref="NewFrame"/> event.</remarks>
        /// 
        public void Start()
        {
            try
            {
                IsRunning = true;
                _framesReceived = 0;
                _bytesReceived = 0;
                var pipelineProfile = _pipeline.Start(_cfg);

                Task.Factory.StartNew(() =>
                {
                    while (!_tokenSource.Token.IsCancellationRequested)
                    {
                        using (var frameset = _pipeline.WaitForFrames())
                        {
                            using var colorFrame = frameset.ColorFrame;
                            using var depthFrame = frameset.DepthFrame;

                            using var colorizedDepth = _colorizer.Process<VideoFrame>(depthFrame);

                            _colorBitmap?.Dispose();
                            _colorizedDepthBitmap?.Dispose();

                            _colorBitmap = Helpers.ToBitmap(colorFrame, PixelFormats.Rgb24);
                            _colorizedDepthBitmap = Helpers.ToBitmap(colorizedDepth, PixelFormats.Rgb24);

                            OnNewFrame(_colorBitmap, _colorizedDepthBitmap);
                        }
                    }
                }, _tokenSource.Token);
            }
            catch (Exception ex)
            {
                VideoSourceError?.Invoke(this, new VideoSourceErrorEventArgs(ex.Message));
                IsRunning = false;
                throw;
            }
        }

        /// <summary>
        /// Stop video source.
        /// </summary>
        /// 
        /// <remarks><para>Stops video source aborting its thread.</para>
        /// 
        /// <para><note>Since the method aborts background thread, its usage is highly not preferred
        /// and should be done only if there are no other options. The correct way of stopping camera
        /// is <see cref="SignalToStop">signaling it stop</see> and then
        /// <see cref="WaitForStop">waiting</see> for background thread's completion.</note></para>
        /// </remarks>
        /// 
        public void Stop()
        {
            _tokenSource.Cancel();
            _tokenSource.Dispose();
            _tokenSource = new CancellationTokenSource();
            PlayingFinished?.Invoke(this, ReasonToFinishPlaying.StoppedByUser);
            IsRunning = false;
        }

        /// <summary>
        /// Signal video source to stop its work.
        /// </summary>
        /// 
        /// <remarks>Signals video source to stop its background thread, stop to
        /// provide new frames and free resources.</remarks>
        /// 
        public void SignalToStop()
        {
            Stop();
        }

        /// <summary>
        /// Wait for video source has stopped.
        /// </summary>
        /// 
        /// <remarks>Waits for source stopping after it was signalled to stop using
        /// <see cref="SignalToStop"/> method.</remarks>
        /// 
        public void WaitForStop()
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Private voids
        /// <summary>
        /// Called when video source gets new frame
        /// </summary>
        /// <param name="colorBitmap"></param>
        /// <param name="colorizedDepthBitmap"></param>
        private void OnNewFrame(Bitmap colorBitmap, Bitmap colorizedDepthBitmap)
        {
            _framesReceived++;
            _bytesReceived += 
                colorBitmap.Width * colorBitmap.Height * (Bitmap.GetPixelFormatSize(colorBitmap.PixelFormat) >> 3) +
                colorizedDepthBitmap.Width * colorizedDepthBitmap.Height * (Bitmap.GetPixelFormatSize(colorizedDepthBitmap.PixelFormat) >> 3);

            // NewFrame?.Invoke(this, new NewFrameEventArgs(colorBitmap));      // delete later
            NewSensorsFrames?.Invoke(this, new NewSensorsEventArgs(colorBitmap, colorizedDepthBitmap));
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Recycles objects necessary for disposal
        /// </summary>
        public void Dispose()
        {
            _colorizer?.Dispose();
            _pipeline?.Dispose();
            _ctx?.Dispose();
            _cfg?.Dispose();
            _tokenSource?.Dispose();
            _colorBitmap?.Dispose();
            _colorizedDepthBitmap?.Dispose();
        }

        #endregion
    }
}
