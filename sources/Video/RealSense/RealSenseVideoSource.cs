using Intel.RealSense;
using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
    public class RealSenseVideoSource : IVideoDepthSource, IVideoSource, IDisposable
    {
        #region Fields

        private readonly Device _device;
        private readonly Pipeline _pipeline;
        private readonly Config _config = new Config();
        private VideoCapabilities _videoResolution;
        private VideoCapabilities _depthResolution;
        private CancellationTokenSource _tokenSource;
        private int _framesReceived;
        private long _bytesReceived;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates video source for Intel RealSense Depth camera.
        /// </summary>
        public RealSenseVideoSource()
        {
            using var ctx = new Context();
            using var devices = ctx.QueryDevices();
            _device = devices.FirstOrDefault();

            if (_device is null)
            {
                throw new NullReferenceException("Intel RealSense Depth camera not found.");
            }
            else
            {
                Source = _device.Info[CameraInfo.Name];
                SerialNumber = _device.Info[CameraInfo.SerialNumber];
                FirmwareVersion = _device.Info[CameraInfo.FirmwareVersion];
                _pipeline = new Pipeline();
            }
        }

        #endregion

        #region Properties

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
        /// Use <see cref="VideoCapabilities"/> property to get the list of supported depth resolutions.</para>
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
        public bool IsRunning { get; private set; }

        #endregion

        #region Events

        /// <summary>
        /// Intel RealSense depth action event handler.
        /// </summary>
        public event NewDepthEventHandler NewDepth;

        /// <summary>
        /// Intel RealSense frame action event handler.
        /// </summary>
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
        /// help of <see cref="NewFrame"/> event.</remarks>
        /// 
        public void Start()
        {
            try
            {
                if (!IsRunning)
                {
                    // depth sensor 
                    var depthProfile = DepthResolutions.FirstOrDefault();

                    if (_depthResolution is object)
                    {
                        _config.EnableStream(Stream.Depth, 
                            _depthResolution.FrameSize.Width, 
                            _depthResolution.FrameSize.Height, Format.Z16, 
                            _depthResolution.AverageFrameRate);
                    }
                    else
                    {
                        _config.EnableStream(Stream.Depth, 
                            depthProfile.FrameSize.Width, 
                            depthProfile.FrameSize.Height, 
                            Format.Z16, 
                            depthProfile.MaximumFrameRate);
                    }

                    // rgb sensor
                    var colorProfile = VideoResolutions.FirstOrDefault();

                    if (_videoResolution is object)
                    {
                        _config.EnableStream(Stream.Color, 
                            _videoResolution.FrameSize.Width, 
                            _videoResolution.FrameSize.Height, 
                            Format.Rgb8, 
                            _videoResolution.AverageFrameRate);
                    }
                    else
                    {
                        _config.EnableStream(Stream.Color, 
                            colorProfile.FrameSize.Width, 
                            colorProfile.FrameSize.Height, 
                            Format.Rgb8, 
                            colorProfile.MaximumFrameRate);
                    }

                    // options
                    _tokenSource = new CancellationTokenSource();
                    _framesReceived = 0;
                    _bytesReceived = 0;
                    IsRunning = true;

                    // pipeline
                    using var pp = _pipeline.Start(_config);
                    var colorBitmap = default(Bitmap);
                    var depthBitmap = default(ushort[,]);

                    Task.Factory.StartNew(() =>
                    {
                        while (!_tokenSource.Token.IsCancellationRequested)
                        {
                            using var frameset = _pipeline.WaitForFrames();
                            using var colorizer = new Colorizer();
                            using var align = new Align(Stream.Color);

                            using var aligned = align.Process(frameset);
                            using var alignedframeset = aligned.As<FrameSet>();

                            using var colorFrame = alignedframeset.ColorFrame;
                            using var depthFrame = alignedframeset.DepthFrame;

                            colorBitmap = colorFrame.ToBitmap();
                            depthBitmap = depthFrame.ToArray();

                            OnNewFrame(colorBitmap);
                            OnNewDepth(depthBitmap);

                            colorBitmap?.Dispose();
                            depthBitmap = null;
                        }
                    }, _tokenSource.Token);
                }
            }
            catch (Exception ex)
            {
                VideoSourceError?.Invoke(this, new VideoSourceErrorEventArgs(ex.Message));
                IsRunning = false;
                throw;
            }
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
            if (IsRunning)
            {
                _tokenSource?.Cancel();
                _pipeline?.Stop();
                _config?.DisableAllStreams();
                PlayingFinished?.Invoke(this, ReasonToFinishPlaying.StoppedByUser);
                IsRunning = false;
            }
        }

        /// <summary>
        /// Stop video source.
        /// </summary>
        /// 
        /// <remarks>Not implemented</remarks>
        /// 
        [Obsolete]
        public void Stop()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Wait for video source has stopped.
        /// </summary>
        /// 
        /// <remarks>Not implemented</remarks>
        [Obsolete]
        public void WaitForStop()
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Private voids

        /// <summary>
        /// Called when video source gets new frame.
        /// </summary>
        /// <param name="frame">Frame</param>
        private void OnNewFrame(Bitmap frame)
        {
            if (frame is object)
            {
                _framesReceived++;
                _bytesReceived += frame.Width * frame.Height * (Image.GetPixelFormatSize(frame.PixelFormat) >> 3);
                NewFrame?.Invoke(this, new NewFrameEventArgs(frame));
            }
        }

        /// <summary>
        /// Called when video source gets new depth.
        /// </summary>
        /// <param name="depth">Depth</param>
        private void OnNewDepth(ushort[,] depth)
        {
            if (depth is object)
            {
                NewDepth?.Invoke(this, new NewDepthEventArgs(depth));
            }
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Disposes Intel RealSense Depth camera source.
        /// </summary>
        public void Dispose()
        {
            _device?.Dispose();
            _pipeline?.Dispose();
            _config?.Dispose();
            _tokenSource?.Dispose();
            GC.SuppressFinalize(this);
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
                var sensors = _device.Sensors;

                // depth sensor
                using var depthSensor = sensors[0];
                var depthProfiles = depthSensor.StreamProfiles
                                    .Where(p => p.Stream == Stream.Depth)
                                    .Where(p => p.Format == Format.Z16)
                                    .OrderBy(p => p.Framerate)
                                    .Select(p => p.As<VideoStreamProfile>()).ToArray();

                var count = depthProfiles.Count();
                var videoCapabilitiesArray = new VideoCapabilities[count];

                for (int i = 0; i < count; i++)
                {
                    using var profile = depthProfiles[i];
                    videoCapabilitiesArray[i] = new VideoCapabilities(new Size
                    {
                        Width = profile.Width,
                        Height = profile.Height
                    },
                    profile.Framerate,
                    profile.Framerate,
                    16);
                }

                return videoCapabilitiesArray;
            }
            
        }

        /// <summary>
        /// Returns video capabilities array of color stream.
        /// </summary>
        public VideoCapabilities[] VideoResolutions
        {
            get
            {
                var sensors = _device.Sensors;

                // rgb sensor
                using var colorSensor = sensors[1];
                var colorProfiles = colorSensor.StreamProfiles
                                    .Where(p => p.Stream == Stream.Color)
                                    .Where(p => p.Format == Format.Rgb8)
                                    .OrderBy(p => p.Framerate)
                                    .Select(p => p.As<VideoStreamProfile>()).ToArray();

                var count = colorProfiles.Count();
                var videoCapabilitiesArray = new VideoCapabilities[count];

                for (int i = 0; i < count; i++)
                {
                    using var profile = colorProfiles[i];
                    videoCapabilitiesArray[i] = new VideoCapabilities(new Size
                    {
                        Width = profile.Width,
                        Height = profile.Height
                    },
                    profile.Framerate,
                    profile.Framerate,
                    32);
                }

                return videoCapabilitiesArray;
            }
        }
        
        #endregion
    }
}
