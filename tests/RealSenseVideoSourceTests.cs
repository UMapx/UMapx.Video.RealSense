using System.Collections.Concurrent;
using System.Drawing;
using UMapx.Video;
using UMapx.Video.RealSense;
using Xunit;

namespace UMapx.Video.RealSense.Tests;

public class RealSenseVideoSourceTests
{
    [Theory]
    [InlineData("start")]
    [InlineData("read")]
    [InlineData("color")]
    [InlineData("depth")]
    [InlineData("stop")]
    public async Task FailuresReportErrorFinishAndAllowRestart(string stage)
    {
        var capture = new FakeCapture();
        using var source = new RealSenseVideoSource(capture);
        var errors = new ConcurrentQueue<string>();
        var reasons = new ConcurrentQueue<ReasonToFinishPlaying>();
        source.VideoSourceError += (_, e) => errors.Enqueue(e.Description);
        source.PlayingFinished += (_, reason) => reasons.Enqueue(reason);
        bool fail = true;
        capture.OnStart = () => { if (fail && stage == "start") throw new Exception(stage); };
        capture.OnRead = () => { if (fail && stage == "read") throw new Exception(stage); };
        capture.OnStop = () => { if (fail && stage == "stop") throw new Exception(stage); };
        source.NewFrame += (_, _) =>
        {
            if (fail && stage == "color") throw new Exception(stage);
        };
        source.NewDepth += (_, _) =>
        {
            if (fail && stage == "depth") throw new Exception(stage);
            source.SignalToStop();
        };

        var frame = capture.EnqueueFrame();
        source.Start();
        await WaitForStop(source);
        Assert.False(source.IsRunning);
        Assert.Equal(new[] { stage }, errors);
        Assert.Equal(new[] { ReasonToFinishPlaying.VideoSourceError }, reasons);
        Assert.Equal(1, capture.StopCount);
        if (stage == "color" || stage == "depth" || stage == "stop")
            AssertDisposed(frame.Color);

        fail = false;
        capture.EnqueueFrame();
        source.Start();
        await WaitForStop(source);
        Assert.False(source.IsRunning);
        Assert.Equal(2, capture.StartCount);
        Assert.Equal(2, capture.StopCount);
        Assert.Equal(2, reasons.Count);
        Assert.Equal(ReasonToFinishPlaying.StoppedByUser, reasons.Last());
        Assert.Single(errors);
        Assert.False(capture.Disposed);
    }

    [Fact]
    public async Task TerminalSubscriberFailuresDoNotPreventOtherSubscribersOrCleanup()
    {
        var capture = new FakeCapture { OnRead = () => throw new Exception("capture failed") };
        using var source = new RealSenseVideoSource(capture);
        int errors = 0, completions = 0;
        source.VideoSourceError += (_, _) => throw new Exception("error observer failed");
        source.VideoSourceError += (_, _) => { Interlocked.Increment(ref errors); source.Dispose(); };
        source.PlayingFinished += (_, _) => throw new Exception("finish observer failed");
        source.PlayingFinished += (_, _) => Interlocked.Increment(ref completions);

        source.Start();
        await WaitForStop(source);
        Assert.False(source.IsRunning);
        Assert.Equal(1, errors);
        Assert.Equal(1, completions);
        Assert.Equal(1, capture.DisposeCount);
        Assert.Throws<ObjectDisposedException>(source.Start);
    }

    [Fact]
    public async Task StopDoesNotCloseCaptureWhileReadIsInProgress()
    {
        var capture = new FakeCapture();
        using var source = new RealSenseVideoSource(capture);
        using var release = new ManualResetEventSlim();
        var entered = Completion();
        var finished = Completion();
        int delivered = 0;
        capture.OnRead = () => { entered.TrySetResult(true); Gate(release); };
        source.NewFrame += (_, _) => Interlocked.Increment(ref delivered);
        source.PlayingFinished += (_, _) => finished.TrySetResult(true);
        var frame = capture.EnqueueFrame();
        source.Start();
        try
        {
            await Within(entered.Task);
            await Within(Task.Run(source.SignalToStop));
            Assert.True(source.IsRunning);
            Assert.Equal(0, capture.StopCount);
            Assert.False(finished.Task.IsCompleted);
        }
        finally { release.Set(); }

        await WaitForStop(source);
        Assert.Equal(0, delivered);
        Assert.Equal(1, capture.StopCount);
        Assert.True(finished.Task.IsCompleted);
        AssertDisposed(frame.Color);
        Assert.Equal(capture.StartThread, capture.StopThread);
    }

    [Fact]
    public async Task StartWhileStoppingCannotReviveTheOldWorker()
    {
        var capture = new FakeCapture();
        using var source = new RealSenseVideoSource(capture);
        using var release = new ManualResetEventSlim();
        var entered = Completion();
        int frames = 0, depths = 0, finished = 0;
        source.NewFrame += (_, _) =>
        {
            if (Interlocked.Increment(ref frames) == 1)
            {
                entered.TrySetResult(true);
                Gate(release);
            }
            else source.SignalToStop();
        };
        source.NewDepth += (_, _) => Interlocked.Increment(ref depths);
        source.PlayingFinished += (_, _) => Interlocked.Increment(ref finished);
        capture.EnqueueFrame();
        source.Start();
        try
        {
            await Within(entered.Task);
            source.SignalToStop();
            await Within(Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(source.Start))));
            Assert.True(source.IsRunning);
            Assert.Equal(1, capture.StartCount);
            Assert.Equal(0, finished);
        }
        finally { release.Set(); }

        await WaitForStop(source);
        Assert.False(source.IsRunning);
        Assert.Equal(0, depths);
        capture.EnqueueFrame();
        source.Start();
        await WaitForStop(source);
        Assert.Equal(2, capture.StartCount);
        Assert.Equal(2, frames);
        Assert.Equal(2, finished);
        Assert.Equal(1, capture.MaxActive);
    }

    [Theory]
    [InlineData("color", false)]
    [InlineData("depth", false)]
    [InlineData("color", true)]
    [InlineData("depth", true)]
    [InlineData("finished", true)]
    public async Task ShutdownFromCallbackDefersCleanupUntilCallbackReturns(string callback, bool dispose)
    {
        var capture = new FakeCapture();
        using var source = new RealSenseVideoSource(capture);
        var errors = new ConcurrentQueue<string>();
        var returned = Completion();
        var frame = capture.EnqueueFrame();
        void Shutdown()
        {
            if (dispose) source.Dispose();
            else { source.Stop(); source.WaitForStop(); }
            Assert.False(capture.Disposed);
            if (callback != "finished") Assert.Equal(2, frame.Color.Width);
            returned.TrySetResult(true);
        }
        source.VideoSourceError += (_, e) => errors.Enqueue(e.Description);
        source.NewFrame += (_, _) => { if (callback == "color") Shutdown(); };
        source.NewDepth += (_, _) =>
        {
            if (callback == "depth") Shutdown();
            else source.SignalToStop();
        };
        source.PlayingFinished += (_, _) => { if (callback == "finished") Shutdown(); };

        source.Start();
        await Within(returned.Task);
        await WaitForStop(source);
        Assert.Empty(errors);
        Assert.False(source.IsRunning);
        AssertDisposed(frame.Color);
        Assert.Equal(dispose ? 1 : 0, capture.DisposeCount);
        if (dispose) Assert.Throws<ObjectDisposedException>(source.Start);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockingShutdownWaitsForFrameHandler(bool dispose)
    {
        var capture = new FakeCapture();
        using var source = new RealSenseVideoSource(capture);
        using var release = new ManualResetEventSlim();
        var entered = Completion();
        var shutdownEntered = Completion();
        source.NewFrame += (_, _) => { entered.TrySetResult(true); Gate(release); };
        var frame = capture.EnqueueFrame();
        source.Start();
        Task shutdown = Task.CompletedTask;
        try
        {
            await Within(entered.Task);
            shutdown = Task.Run(() =>
            {
                source.SignalToStop();
                shutdownEntered.TrySetResult(true);
                if (dispose) source.Dispose();
                else source.WaitForStop();
            });
            await Within(shutdownEntered.Task);
            Assert.NotSame(shutdown, await Task.WhenAny(shutdown, Task.Delay(100)));
            Assert.False(capture.Disposed);
            Assert.Equal(0, capture.StopCount);
            Assert.Equal(2, frame.Color.Width);
        }
        finally { release.Set(); }

        await Within(shutdown);
        Assert.False(source.IsRunning);
        AssertDisposed(frame.Color);
        Assert.Equal(dispose ? 1 : 0, capture.DisposeCount);
    }

    [Fact]
    public async Task DisposeDuringStartupWaitsAndSkipsFrameAcquisition()
    {
        var capture = new FakeCapture();
        using var source = new RealSenseVideoSource(capture);
        using var release = new ManualResetEventSlim();
        var entered = Completion();
        var shutdownEntered = Completion();
        capture.OnStart = () => { entered.TrySetResult(true); Gate(release); };
        source.Start();
        Task shutdown = Task.CompletedTask;
        try
        {
            await Within(entered.Task);
            shutdown = Task.Run(() =>
            {
                source.SignalToStop();
                shutdownEntered.TrySetResult(true);
                source.Dispose();
            });
            await Within(shutdownEntered.Task);
            Assert.NotSame(shutdown, await Task.WhenAny(shutdown, Task.Delay(100)));
            Assert.False(capture.Disposed);
            Assert.Equal(0, capture.StopCount);
        }
        finally { release.Set(); }

        await Within(shutdown);
        Assert.Equal(0, capture.ReadCount);
        Assert.Equal(1, capture.StopCount);
        Assert.Equal(1, capture.DisposeCount);
        Assert.False(source.IsRunning);
    }

    [Fact]
    public async Task WaitForStopIncludesCompletionHandlers()
    {
        var capture = new FakeCapture { OnRead = () => throw new Exception("capture failed") };
        using var source = new RealSenseVideoSource(capture);
        using var release = new ManualResetEventSlim();
        var entered = Completion();
        source.PlayingFinished += (_, _) => { entered.TrySetResult(true); Gate(release); };
        source.Start();
        Task waiter = Task.CompletedTask;
        try
        {
            await Within(entered.Task);
            Assert.True(source.IsRunning);
            source.Start();
            Assert.Equal(1, capture.StartCount);
            waiter = Task.Run(source.WaitForStop);
            Assert.NotSame(waiter, await Task.WhenAny(waiter, Task.Delay(100)));
        }
        finally { release.Set(); }

        await Within(waiter);
        Assert.False(source.IsRunning);
        Assert.Equal(1, capture.StopCount);
    }

    [Fact]
    public async Task CleanupFailureStillReportsErrorAndCompletes()
    {
        var capture = new FakeCapture { OnDispose = () => throw new Exception("cleanup failed") };
        using var source = new RealSenseVideoSource(capture);
        var errors = new ConcurrentQueue<string>();
        var reasons = new ConcurrentQueue<ReasonToFinishPlaying>();
        source.NewFrame += (_, _) => source.Dispose();
        source.VideoSourceError += (_, e) => errors.Enqueue(e.Description);
        source.PlayingFinished += (_, reason) => reasons.Enqueue(reason);
        capture.EnqueueFrame();
        source.Start();
        await WaitForStop(source);
        Assert.False(source.IsRunning);
        Assert.Equal(new[] { "cleanup failed" }, errors);
        Assert.Equal(new[] { ReasonToFinishPlaying.VideoSourceError }, reasons);
        Assert.Equal(1, capture.DisposeCount);
    }

    [Fact]
    public async Task ConcurrentShutdownDisposesOnceAndRejectsRestart()
    {
        var capture = new FakeCapture();
        using var source = new RealSenseVideoSource(capture);
        source.Start();
        await Within(capture.Started.Task);
        await Within(Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(() =>
        {
            if (i % 3 == 0) source.Stop();
            else if (i % 3 == 1) source.Dispose();
            else { source.SignalToStop(); source.WaitForStop(); }
        }))));
        Assert.False(source.IsRunning);
        Assert.Equal(1, capture.StartCount);
        Assert.Equal(1, capture.StopCount);
        Assert.Equal(1, capture.DisposeCount);
        Assert.Throws<ObjectDisposedException>(source.Start);
        source.SignalToStop();
        source.WaitForStop();
        source.Dispose();
        Assert.Equal(1, capture.DisposeCount);
    }

    [Fact]
    public async Task NoFramesReportsTimeoutAndCompletes()
    {
        var capture = new FakeCapture();
        using var source = new RealSenseVideoSource(capture);
        string error = null;
        ReasonToFinishPlaying? reason = null;
        source.VideoSourceError += (_, e) => error = e.Description;
        source.PlayingFinished += (_, e) => reason = e;
        source.Start();
        await WaitForStop(source);
        Assert.Contains("5 seconds", error);
        Assert.Equal(ReasonToFinishPlaying.VideoSourceError, reason);
        Assert.Equal(1, capture.StopCount);
        Assert.False(source.IsRunning);
    }

    [Fact]
    public void DisposeBeforeStartIsIdempotent()
    {
        var capture = new FakeCapture();
        var source = new RealSenseVideoSource(capture);
        source.Stop();
        source.WaitForStop();
        source.Dispose();
        source.Dispose();
        Assert.Equal(1, capture.DisposeCount);
        Assert.Equal(0, capture.StartCount);
        Assert.False(source.IsRunning);
        Assert.Throws<ObjectDisposedException>(source.Start);
        Assert.Throws<ObjectDisposedException>(() => source.VideoResolutions);
        Assert.Throws<ObjectDisposedException>(() => source.DepthResolutions);
    }

    private static TaskCompletionSource<bool> Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task Within(Task task) => task.WaitAsync(TimeSpan.FromSeconds(10));
    private static Task WaitForStop(RealSenseVideoSource source) => Within(Task.Run(source.WaitForStop));
    private static void Gate(ManualResetEventSlim gate)
    {
        if (!gate.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test callback was not released.");
    }
    private static void AssertDisposed(Bitmap bitmap) => Assert.ThrowsAny<Exception>(() => bitmap.GetPixel(0, 0));

    private sealed class FakeCapture : IRealSenseCapture
    {
        private readonly BlockingCollection<RealSenseFrame> frames = new();
        private int active;
        internal int StartCount, ReadCount, StopCount, DisposeCount, MaxActive, StartThread, StopThread;
        internal Action OnStart, OnRead, OnStop, OnDispose;
        internal readonly TaskCompletionSource<bool> Started = Completion();
        internal bool Disposed => Volatile.Read(ref DisposeCount) != 0;
        public string Source => "Test RealSense";
        public string SerialNumber => "test-serial";
        public string FirmwareVersion => "test-firmware";
        public VideoCapabilities[] VideoResolutions => Array.Empty<VideoCapabilities>();
        public VideoCapabilities[] DepthResolutions => Array.Empty<VideoCapabilities>();

        internal RealSenseFrame EnqueueFrame()
        {
            var frame = new RealSenseFrame(new Bitmap(2, 2), new ushort[2, 2]);
            frames.Add(frame);
            return frame;
        }

        public void Start(VideoCapabilities videoResolution, VideoCapabilities depthResolution)
        {
            if (Disposed) throw new ObjectDisposedException(nameof(FakeCapture));
            Interlocked.Increment(ref StartCount);
            int count = Interlocked.Increment(ref active);
            MaxActive = Math.Max(MaxActive, count);
            StartThread = Environment.CurrentManagedThreadId;
            Started.TrySetResult(true);
            OnStart?.Invoke();
        }

        public RealSenseFrame ReadFrame(uint timeoutMilliseconds)
        {
            if (Disposed) throw new ObjectDisposedException(nameof(FakeCapture));
            Interlocked.Increment(ref ReadCount);
            OnRead?.Invoke();
            return frames.TryTake(out var frame, (int)timeoutMilliseconds) ? frame : null;
        }

        public void Stop()
        {
            if (Disposed) throw new ObjectDisposedException(nameof(FakeCapture));
            Interlocked.Increment(ref StopCount);
            StopThread = Environment.CurrentManagedThreadId;
            Interlocked.Exchange(ref active, 0);
            OnStop?.Invoke();
        }

        public void Dispose()
        {
            Interlocked.Increment(ref DisposeCount);
            while (frames.TryTake(out var frame)) frame.Dispose();
            frames.Dispose();
            OnDispose?.Invoke();
        }
    }
}
