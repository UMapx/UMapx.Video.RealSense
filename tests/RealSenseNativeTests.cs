using System.Drawing;
using Intel.RealSense;
using Xunit;
using Stream = Intel.RealSense.Stream;

namespace UMapx.Video.RealSense.Tests;

public class RealSenseNativeTests
{
    [Fact]
    public void NativePipelineFindsStreamsAlignsFramesAndRestarts()
    {
        using var camera = new SoftwareCamera(4, 2);
        using var devices = camera.Context.QueryDevices();
        using var capture = new RealSenseCapture(devices.First(d => d.Info[CameraInfo.Name] == "Software-Device"),
            new Pipeline(camera.Context));

        // Color is deliberately sensor zero: discovery must use stream types, not sensor indices.
        Assert.Equal(new Size(4, 2), Assert.Single(capture.VideoResolutions).FrameSize);
        Assert.Equal(new Size(4, 2), Assert.Single(capture.DepthResolutions).FrameSize);
        try
        {
            Assert.ThrowsAny<Exception>(() => capture.Start(new VideoCapabilities(new Size(5, 5), 30, 30, 32), null));
        }
        finally { capture.Stop(); }
        for (int run = 0; run < 3; run++)
        {
            capture.Start(null, null);
            try
            {
                camera.Depth.AddVideoFrame(Enumerable.Repeat((ushort)1000, 8).ToArray(), 8, 2,
                    33.0, TimestampDomain.HardwareClock, 1, camera.DepthProfile);
                camera.Color.AddVideoFrame(Enumerable.Range(0, 8).SelectMany(_ => new byte[] { 11, 22, 33 }).ToArray(),
                    12, 3, 33.0, TimestampDomain.HardwareClock, 1, camera.ColorProfile);
                using var frame = capture.ReadFrame(1000);
                Assert.NotNull(frame);
                Assert.Equal(System.Drawing.Color.FromArgb(11, 22, 33), frame.Color.GetPixel(3, 1));
                Assert.Equal(2, frame.Depth.GetLength(0));
                Assert.Equal(4, frame.Depth.GetLength(1));
                Assert.All(frame.Depth.Cast<ushort>(), value => Assert.Equal(1000, value));
            }
            finally { capture.Stop(); }
        }
    }

    [Theory]
    [InlineData(3, 9)]
    [InlineData(3, 12)]
    [InlineData(4, 16)]
    public void ColorConversionHonorsBothFrameAndBitmapRowStride(int width, int stride)
    {
        using var camera = new SoftwareCamera(width, 2);
        using var queue = new FrameQueue(1);
        camera.Color.Open(camera.ColorProfile);
        camera.Color.Start(queue);
        try
        {
            var pixels = Enumerable.Repeat((byte)255, stride * 2).ToArray();
            for (int y = 0; y < 2; y++)
            for (int x = 0; x < width; x++)
            {
                pixels[y * stride + x * 3] = (byte)(10 + x);
                pixels[y * stride + x * 3 + 1] = (byte)(20 + y);
                pixels[y * stride + x * 3 + 2] = 30;
            }
            camera.Color.AddVideoFrame(pixels, stride, 3, 33.0, TimestampDomain.HardwareClock, 1, camera.ColorProfile);
            using var frame = queue.WaitForFrame<VideoFrame>(1000);
            using var bitmap = frame.ToBitmap();
            for (int y = 0; y < 2; y++)
            for (int x = 0; x < width; x++)
                Assert.Equal(System.Drawing.Color.FromArgb(10 + x, 20 + y, 30), bitmap.GetPixel(x, y));
        }
        finally { camera.Color.Stop(); camera.Color.Close(); }
    }

    [Fact]
    public void DepthConversionHonorsPaddedRows()
    {
        using var camera = new SoftwareCamera(3, 2);
        using var queue = new FrameQueue(1);
        camera.Depth.Open(camera.DepthProfile);
        camera.Depth.Start(queue);
        try
        {
            camera.Depth.AddVideoFrame(new ushort[] { 1, 2, 3, 999, 4, 5, 6, 999 }, 8, 2,
                33.0, TimestampDomain.HardwareClock, 1, camera.DepthProfile);
            using var frame = queue.WaitForFrame<DepthFrame>(1000);
            Assert.Equal(new ushort[] { 1, 2, 3, 4, 5, 6 }, frame.ToArray().Cast<ushort>());
        }
        finally { camera.Depth.Stop(); camera.Depth.Close(); }
    }

    private sealed class SoftwareCamera : IDisposable
    {
        internal readonly SoftwareDevice Device = new();
        internal readonly Context Context = new();
        internal readonly SoftwareSensor Color, Depth;
        internal readonly VideoStreamProfile ColorProfile, DepthProfile;

        internal SoftwareCamera(int width, int height)
        {
            Color = Device.AddSensor("Color");
            Depth = Device.AddSensor("Depth");
            Depth.AddReadOnlyOption(Option.DepthUnits, 0.001f);
            var intrinsics = new Intrinsics
            {
                width = width, height = height, fx = 10, fy = 10,
                ppx = width / 2f, ppy = height / 2f, coeffs = new float[5]
            };
            ColorProfile = Color.AddVideoStream(new SoftwareVideoStream
            {
                type = Stream.Color, index = 0, uid = 2, width = width, height = height,
                fps = 30, bpp = 3, format = Format.Rgb8, intrinsics = intrinsics
            });
            DepthProfile = Depth.AddVideoStream(new SoftwareVideoStream
            {
                type = Stream.Depth, index = 0, uid = 1, width = width, height = height,
                fps = 30, bpp = 2, format = Format.Z16, intrinsics = intrinsics
            });
            DepthProfile.RegisterExtrinsicsTo(ColorProfile, new Extrinsics
            {
                rotation = new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, translation = new float[3]
            });
            Device.AddTo(Context);
        }

        public void Dispose()
        {
            ColorProfile.Dispose();
            DepthProfile.Dispose();
            Color.Dispose();
            Depth.Dispose();
            Context.Dispose();
            Device.Dispose();
        }
    }
}
