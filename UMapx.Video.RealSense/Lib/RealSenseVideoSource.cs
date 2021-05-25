using Intel.RealSense;
using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UMapx.Video.DirectShow;
using static UMapx.Video.RealSense.SensorEvents;

namespace UMapx.Video.RealSense
{
    public class RealSenseVideoSource : IVideoSensorSource, IDisposable
    {
        #region Fields
        
        private readonly Device _device;
        private readonly Pipeline _pipeline;
        private VideoCapabilities[] _videoResolution;
        private CancellationTokenSource _tokenSource;
        private Bitmap _colorBitmap;
        private Bitmap _colorizedDepthBitmap;
        private int _framesReceived;
        private long _bytesReceived;

        #endregion

        #region Constructors

        /// <summary>
        /// Create video source with default <see cref="Stream.Color"/> and <see cref="Stream.Depth"/>
        /// </summary>
        public RealSenseVideoSource(string json = null)
        {
            using var ctx = new Context();
            using var devices = ctx.QueryDevices();
            _device = devices.FirstOrDefault();
            _pipeline = new Pipeline();

            Source = _device.Info[CameraInfo.Name];
            SerialNumber = _device.Info[CameraInfo.SerialNumber];
            FirmwareVersion = _device.Info[CameraInfo.FirmwareVersion];

            if (json is object)
            {
                var adv = AdvancedDevice.FromDevice(_device);
                adv.JsonConfiguration = json;
            }
        }

        /// <summary>
        /// Video resolution to set.
        /// </summary>
        /// 
        /// <remarks><para>The property allows to set one of the video resolutions supported by the camera.
        /// Use <see cref="VideoCapabilities"/> property to get the list of supported video resolutions.</para>
        /// 
        /// <para><note>The property must be set before camera is started to make any effect.</note></para>
        /// 
        /// <para>Default value of the property is set to <see langword="null"/>, which means default video
        /// resolution is used.</para>
        /// </remarks>
        /// 
        public VideoCapabilities[] VideoResolution
        {
            get
            { 
                return _videoResolution;
            }
            set
            {
                if (value.Length != 2)
                    throw new ArgumentException("Video resolution must be configured for 2 sensors.");

                _videoResolution = value; 
            }
        }

        /// <summary>
        /// Returns camera source.
        /// </summary>
        public string Source { get; private set; }

        /// <summary>
        /// Serial number of Intel RealSense camera.
        /// </summary>
        public string SerialNumber { get; private set; }

        /// <summary>
        /// Firmware version of Intel RealSense camera.
        /// </summary>
        public string FirmwareVersion { get; private set; }

        #endregion

        #region Methods

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
        /// Intel RealSense frames action event handler.
        /// </summary>
        public event NewSensorsEventHandler NewSensorsFrames;

        /// <summary>
        /// 
        /// </summary>
        public event NewFrameEventHandler NewFrame;

        /// <summary>
        /// 
        /// </summary>
        public event VideoSourceErrorEventHandler VideoSourceError;

        /// <summary>
        /// 
        /// </summary>
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
                using var config = new Config();
                var sensors = _device.Sensors;

                // depth sensor
                using var depthSensor = sensors[0];
                var depthProfiles = depthSensor.StreamProfiles
                                    .Where(p => p.Stream == Stream.Depth)
                                    .Where(p => p.Format == Format.Z16)
                                    .OrderBy(p => p.Framerate)
                                    .Select(p => p.As<VideoStreamProfile>()).ToArray();

                using var depthProfile = depthProfiles.First();
                var depthResolution = _videoResolution[0];
                config.EnableStream(Stream.Depth, depthResolution.FrameSize.Width, depthResolution.FrameSize.Height, depthProfile.Format, depthResolution.AverageFrameRate);

                // rgb sensor
                using var colorSensor = sensors[1];
                var colorProfiles = colorSensor.StreamProfiles
                                    .Where(p => p.Stream == Stream.Color)
                                    .Where(p => p.Format == Format.Rgb8)
                                    .OrderBy(p => p.Framerate)
                                    .Select(p => p.As<VideoStreamProfile>()).ToArray();

                using var colorProfile = colorProfiles.First();
                var colorResolution = _videoResolution[1];
                
                config.EnableStream(Stream.Color, colorResolution.FrameSize.Width, colorResolution.FrameSize.Height, colorProfile.Format, colorResolution.AverageFrameRate);

                // options
                _tokenSource = new CancellationTokenSource();
                _framesReceived = 0;
                _bytesReceived = 0;

                using var pp = _pipeline.Start(config);
                IsRunning = true;

                Task.Factory.StartNew(() =>
                {
                    while (!_tokenSource.Token.IsCancellationRequested)
                    {
                        using (var frameset = _pipeline.WaitForFrames())
                        {
                            using var colorizer = new Colorizer();
                            using var align = new Align(Stream.Color);

                            using var aligned = align.Process(frameset);
                            using var alignedframeset = aligned.As<FrameSet>();

                            using var colorFrame = alignedframeset.ColorFrame;
                            using var depthFrame = alignedframeset.DepthFrame;

                            using var colorizedDepth = colorizer.Process<VideoFrame>(depthFrame);

                            _colorBitmap?.Dispose();
                            _colorizedDepthBitmap?.Dispose();

                            _colorBitmap = colorFrame.ToBitmap();
                            _colorizedDepthBitmap = colorizedDepth.ToBitmap();

                            OnNewFrames(_colorBitmap, _colorizedDepthBitmap);
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
            _tokenSource?.Cancel();
            _pipeline?.Stop();
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

        #region Protected voids

        /// <summary>
        /// Called when video source gets new frame
        /// </summary>
        /// <param name="colorBitmap"></param>
        protected void OnNewFrames(params Bitmap[] frames)
        {
            int count = frames.Length;

            for (int i = 0; i < count; i++)
            {
                var current = frames[i];

                _framesReceived++;
                _bytesReceived += current.Width * current.Height * (Image.GetPixelFormatSize(current.PixelFormat) >> 3);
            }

            NewSensorsFrames?.Invoke(this, new NewSensorsEventArgs(frames));
            NewFrame?.Invoke(this, new NewFrameEventArgs(frames.FirstOrDefault()));
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Recycles objects necessary for disposal
        /// </summary>
        public void Dispose()
        {
            _device?.Dispose();
            _pipeline?.Dispose();
            _tokenSource?.Dispose();
            _colorBitmap?.Dispose();
            _colorizedDepthBitmap?.Dispose();
        }

        #endregion
    }
}
