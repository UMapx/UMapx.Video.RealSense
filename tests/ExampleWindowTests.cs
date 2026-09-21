using System.Drawing;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UMapx.Video;
using UMapx.Video.RealSense.Example;
using Xunit;
using Color = System.Drawing.Color;
using Image = System.Windows.Controls.Image;

namespace UMapx.Video.RealSense.Tests;

public class ExampleWindowTests
{
    [Fact]
    public Task MissingCameraShowsErrorAndWindowCloses() => OnDispatcher(async () =>
    {
        var window = new MainWindow(() => throw new InvalidOperationException("camera not found"));
        await window.ConnectAsync();
        Assert.Contains("camera not found", Status(window));
        Assert.True(((Button)window.FindName("reconnectButton")).IsEnabled);
        var closed = Completion();
        window.Closed += (_, _) => closed.TrySetResult(true);
        window.Close();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    });

    [Fact]
    public Task ReconnectDisposesOldSourceAndRemovesSubscriptions() => OnDispatcher(async () =>
    {
        var first = new FakeSource();
        var second = new FakeSource();
        int attempt = 0;
        var window = new MainWindow(() => ++attempt == 1 ? first : second);
        try
        {
            await window.ConnectAsync();
            Assert.Equal(1, first.StartCount);
            await window.ConnectAsync();
            Assert.Equal(1, first.DisposeCount);
            Assert.Equal(0, first.SubscriberCount);
            Assert.Equal(1, second.StartCount);
        }
        finally { await window.ShutdownAsync(); }
        Assert.Equal(1, second.DisposeCount);
        Assert.Equal(0, second.SubscriberCount);
    });

    [Fact]
    public Task LatestImagesSurviveDisposalOfFrameAndStream() => OnDispatcher(async () =>
    {
        var source = new FakeSource();
        var window = new MainWindow(() => source);
        try
        {
            await window.ConnectAsync();
            await Task.Run(() =>
            {
                using var bitmap = new Bitmap(2, 2);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Color.Red);
                source.EmitColor(bitmap);
                graphics.Clear(Color.Blue);
                for (int i = 0; i < 100; i++) source.EmitColor(bitmap);
                source.EmitDepth(new ushort[,] { { 1, 2 }, { 3, 4 } });
            });
            window.RenderPendingFrames();
            var image = Assert.IsAssignableFrom<BitmapSource>(((Image)window.FindName("imgColor")).Source);
            Assert.True(image.IsFrozen);
            var pixels = new byte[16];
            new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, 8, 0);
            Assert.Equal(255, pixels[0]);
            Assert.Equal(0, pixels[2]);
            Assert.NotNull(((Image)window.FindName("imgDepth")).Source);
            Assert.Contains("Streaming", Status(window));
        }
        finally { await window.ShutdownAsync(); }
    });

    [Fact]
    public Task CaptureErrorIsNotOverwrittenByCompletionOrQueuedFrames() => OnDispatcher(async () =>
    {
        var source = new FakeSource();
        var window = new MainWindow(() => source);
        try
        {
            await window.ConnectAsync();
            await Task.Run(() =>
            {
                using var frame = new Bitmap(2, 2);
                source.EmitColor(frame);
                source.EmitError("USB disconnected");
                source.EmitFinished();
            });
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.RenderPendingFrames();
            Assert.Contains("USB disconnected", Status(window));
        }
        finally { await window.ShutdownAsync(); }
    });

    [Fact]
    public Task ClosingDuringDiscoveryDisposesSourceWithoutStartingIt() => OnDispatcher(async () =>
    {
        var source = new FakeSource();
        using var release = new ManualResetEventSlim();
        var entered = Completion();
        var window = new MainWindow(() =>
        {
            entered.TrySetResult(true);
            Gate(release);
            return source;
        });
        var connecting = window.ConnectAsync();
        Task shutdown = Task.CompletedTask;
        try
        {
            await entered.Task;
            shutdown = window.ShutdownAsync();
            Assert.False(shutdown.IsCompleted);
            Assert.False(((Button)window.FindName("reconnectButton")).IsEnabled);
        }
        finally { release.Set(); }
        await connecting;
        await shutdown;
        Assert.Equal(0, source.StartCount);
        Assert.Equal(1, source.DisposeCount);
    });

    [Fact]
    public Task WindowCloseWaitsForCleanupWithoutBlockingDispatcher() => OnDispatcher(async () =>
    {
        using var release = new ManualResetEventSlim();
        var disposing = Completion();
        var source = new FakeSource { OnDispose = () => { disposing.TrySetResult(true); Gate(release); } };
        var window = new MainWindow(() => source);
        await window.ConnectAsync();
        var closed = Completion();
        window.Closed += (_, _) => closed.TrySetResult(true);
        try
        {
            window.Close();
            await disposing.Task;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(closed.Task.IsCompleted);
        }
        finally { release.Set(); }
        await closed.Task;
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(0, source.SubscriberCount);
    });

    private static string Status(MainWindow window) => ((TextBlock)window.FindName("statusText")).Text;
    private static TaskCompletionSource<bool> Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Gate(ManualResetEventSlim release)
    {
        if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test gate was not released.");
    }

    private static Task OnDispatcher(Func<Task> action)
    {
        var completion = Completion();
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await action(); completion.TrySetResult(true); }
                catch (Exception error) { completion.TrySetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private sealed class FakeSource : IVideoDepthSource
    {
        public event NewFrameEventHandler NewFrame;
        public event NewDepthEventHandler NewDepth;
        public event VideoSourceErrorEventHandler VideoSourceError;
        public event PlayingFinishedEventHandler PlayingFinished;
        public string Source => "Test camera";
        public int FramesReceived => 0;
        public long BytesReceived => 0;
        public bool IsRunning { get; private set; }
        internal int StartCount, DisposeCount;
        internal Action OnDispose;
        internal int SubscriberCount => (NewFrame?.GetInvocationList().Length ?? 0)
            + (NewDepth?.GetInvocationList().Length ?? 0)
            + (VideoSourceError?.GetInvocationList().Length ?? 0)
            + (PlayingFinished?.GetInvocationList().Length ?? 0);
        public void Start() { StartCount++; IsRunning = true; }
        public void SignalToStop() => IsRunning = false;
        public void WaitForStop() { }
        public void Stop() => SignalToStop();
        public void Dispose() { OnDispose?.Invoke(); DisposeCount++; IsRunning = false; }
        internal void EmitColor(Bitmap bitmap) => NewFrame?.Invoke(this, new NewFrameEventArgs(bitmap));
        internal void EmitDepth(ushort[,] depth) => NewDepth?.Invoke(this, new NewDepthEventArgs(depth));
        internal void EmitError(string message) => VideoSourceError?.Invoke(this, new VideoSourceErrorEventArgs(message));
        internal void EmitFinished() { IsRunning = false; PlayingFinished?.Invoke(this, ReasonToFinishPlaying.VideoSourceError); }
    }
}
