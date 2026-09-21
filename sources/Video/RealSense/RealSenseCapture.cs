using Intel.RealSense;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace UMapx.Video.RealSense
{
    // Keep SDK calls behind a small boundary so lifecycle tests do not need a camera.
    internal interface IRealSenseCapture : IDisposable
    {
        string Source { get; }
        string SerialNumber { get; }
        string FirmwareVersion { get; }
        VideoCapabilities[] VideoResolutions { get; }
        VideoCapabilities[] DepthResolutions { get; }
        void Start(VideoCapabilities videoResolution, VideoCapabilities depthResolution);
        RealSenseFrame ReadFrame(uint timeoutMilliseconds);
        void Stop();
    }

    internal sealed class RealSenseFrame : IDisposable
    {
        internal Bitmap Color { get; }
        internal ushort[,] Depth { get; }

        internal RealSenseFrame(Bitmap color, ushort[,] depth)
        {
            Color = color;
            Depth = depth;
        }

        public void Dispose() => Color?.Dispose();
    }

    // The source's worker owns Start/ReadFrame/Stop; Dispose runs only after capture ends.
    internal sealed class RealSenseCapture : IRealSenseCapture
    {
        private readonly Device device;
        private readonly Pipeline pipeline;
        private readonly Config config;
        private Align align;
        private bool started;
        private bool disposed;

        public string Source { get; }
        public string SerialNumber { get; }
        public string FirmwareVersion { get; }

        internal RealSenseCapture()
        {
            try
            {
                config = new Config();
                using var context = new Context();
                using var devices = context.QueryDevices();
                device = devices.FirstOrDefault();
                if (device == null)
                    throw new InvalidOperationException("Intel RealSense Depth camera not found");

                Source = device.Info[CameraInfo.Name];
                SerialNumber = GetInfo(CameraInfo.SerialNumber);
                FirmwareVersion = GetInfo(CameraInfo.FirmwareVersion);
                pipeline = new Pipeline();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        // Takes ownership of these handles; also allows native tests with a software device.
        internal RealSenseCapture(Device device, Pipeline pipeline)
        {
            this.device = device ?? throw new ArgumentNullException(nameof(device));
            this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            try
            {
                config = new Config();
                Source = GetInfo(CameraInfo.Name);
                SerialNumber = GetInfo(CameraInfo.SerialNumber);
                FirmwareVersion = GetInfo(CameraInfo.FirmwareVersion);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private string GetInfo(CameraInfo info) => device.Info.Supports(info) ? device.Info[info] : string.Empty;

        public void Start(VideoCapabilities videoResolution, VideoCapabilities depthResolution)
        {
            var depth = depthResolution ?? DepthResolutions.FirstOrDefault();
            var color = videoResolution ?? VideoResolutions.FirstOrDefault();
            if (depth == null || color == null)
                throw new NotSupportedException("The camera must provide depth and color streams.");

            if (!string.IsNullOrEmpty(SerialNumber)) config.EnableDevice(SerialNumber);
            config.EnableStream(Stream.Depth, depth.FrameSize.Width, depth.FrameSize.Height,
                Format.Z16, depth.AverageFrameRate);
            config.EnableStream(Stream.Color, color.FrameSize.Width, color.FrameSize.Height,
                Format.Rgb8, color.AverageFrameRate);
            align = new Align(Stream.Color);
            using var profile = pipeline.Start(config);
            started = true;
        }

        public RealSenseFrame ReadFrame(uint timeoutMilliseconds)
        {
            if (!pipeline.TryWaitForFrames(out var frameset, timeoutMilliseconds))
                return null;

            using (frameset)
            using (var aligned = align.Process(frameset))
            using (var frames = aligned.As<FrameSet>())
            using (var color = frames.ColorFrame)
            using (var depth = frames.DepthFrame)
            {
                var bitmap = color.ToBitmap();
                try { return new RealSenseFrame(bitmap, depth.ToArray()); }
                catch
                {
                    bitmap.Dispose();
                    throw;
                }
            }
        }

        public void Stop()
        {
            try
            {
                if (started)
                {
                    started = false;
                    pipeline.Stop();
                }
            }
            finally
            {
                try { align?.Dispose(); }
                finally
                {
                    align = null;
                    config?.DisableAllStreams();
                }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { Stop(); }
            finally
            {
                try { pipeline?.Dispose(); }
                finally
                {
                    try { config?.Dispose(); }
                    finally { device?.Dispose(); }
                }
            }
        }

        public VideoCapabilities[] DepthResolutions => GetResolutions(Stream.Depth, Format.Z16, 16);

        public VideoCapabilities[] VideoResolutions => GetResolutions(Stream.Color, Format.Rgb8, 32);

        private VideoCapabilities[] GetResolutions(Stream stream, Format format, int bitCount)
        {
            var sensors = device.Sensors;
            try
            {
                var resolutions = new List<VideoCapabilities>();
                foreach (var sensor in sensors)
                {
                    var profiles = sensor.StreamProfiles;
                    try
                    {
                        foreach (var candidate in profiles.Where(p => p.Stream == stream && p.Format == format))
                        {
                            using var profile = candidate.As<VideoStreamProfile>();
                            resolutions.Add(new VideoCapabilities(new Size(profile.Width, profile.Height),
                                profile.Framerate, profile.Framerate, bitCount));
                        }
                    }
                    finally
                    {
                        foreach (var profile in profiles) profile.Dispose();
                    }
                }
                return resolutions.OrderBy(p => p.AverageFrameRate).ToArray();
            }
            finally
            {
                foreach (var sensor in sensors) sensor.Dispose();
            }
        }
    }
}
