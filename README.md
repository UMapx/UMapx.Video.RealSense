<p align="center"><img width="25%" src="https://raw.githubusercontent.com/UMapx/UMapx.Video.RealSense/main/docs/umapxnet_big.png" /></p>
<p align="center">UMapx sub-library for interacting with Intel RealSense on Windows</p>

# Installation
Install **UMapx.Video.RealSense** to your project using [NuGet](https://www.nuget.org/packages/UMapx.Video.RealSense/) package manager.

```csharp
using UMapx.Video.RealSense;
```

See the [WPF color and depth example](https://github.com/UMapx/UMapx.Video.RealSense/tree/main/examples).
The window reports camera errors and provides **Reconnect** to retry after connecting a camera.

The `librealsense.x64` NuGet dependency supplies `realsense2.dll`. Keep the native runtime
files produced by build or publish with your application. [RealSense Viewer](https://github.com/realsenseai/librealsense/releases)
is optional and can help check the camera and try [D400 visual presets](https://github.com/realsenseai/librealsense/wiki/D400-Series-Visual-Presets).
The library and example do not automatically load the JSON preset in [resources](https://github.com/UMapx/UMapx.Video.RealSense/tree/main/resources).

<p align="center"><img width="70%" src="https://raw.githubusercontent.com/UMapx/UMapx.Video.RealSense/main/docs/camera.jpg" /></p>

# Platform support

The library targets **.NET Standard 2.0** but requires Windows and a 64-bit application
process: it uses the native RealSense SDK and System.Drawing.Common. The library itself
can remain **AnyCPU**; set the consuming application's platform target to **x64**, as in
the example. Linux, macOS and x86 processes are not supported by this package.

Regression tests cover Windows with .NET 8 in an x64 process. Building the repository
requires the .NET 8 SDK, or a newer SDK with the .NET 8 runtime installed. The WPF example
and its tests also require the .NET 8 Windows Desktop runtime.
ARM64 and .NET Framework are not covered by this test suite.

The camera must provide RGB8 color and Z16 depth streams supported by the bundled SDK.
Physical cameras need separate validation with the intended model, firmware and stream profiles.

# Capturing and stopping

```csharp
using var source = new RealSenseVideoSource();
source.NewFrame += (_, e) =>
{
    // The source owns e.Frame. Clone it when retaining or processing a separate image.
    using var copy = (System.Drawing.Bitmap)e.Frame.Clone();
    // Process copy here; dispose retained copies when they are no longer needed.
};
source.NewDepth += (_, e) =>
{
    // Depth is aligned to color: ushort[height, width] in raw device depth units.
    var depth = e.Depth;
    // Process depth here.
};
source.VideoSourceError += (_, e) => Console.Error.WriteLine(e.Description);
source.Start();

// When capture is no longer needed, outside a source event handler:
source.SignalToStop();
source.WaitForStop();
```

The constructor selects the first connected device. Missing-camera and SDK-loading
errors are thrown synchronously, so handle them around construction. Startup and capture
errors after `Start()`, including exceptions from frame handlers, stop the current run
and raise `VideoSourceError`, then `PlayingFinished` with `ReasonToFinishPlaying.VideoSourceError`.

`SignalToStop()` requests shutdown and returns immediately. `WaitForStop()` waits for the
worker, callbacks and cleanup to finish. `IsRunning` stays true until the worker exits;
`Start()` has no effect during that time. `Stop()` is obsolete; use the signal/wait pair.
`Dispose()` requests shutdown, waits for completion and releases the SDK resources.

From the source's own event handler, `Stop()`, `WaitForStop()` and `Dispose()` do not wait
for the current thread. Shutdown requested there completes after the handler returns.
A blocked SDK call or a handler that never returns can delay shutdown. Handlers should
return promptly. Dispatch UI updates asynchronously and perform blocking shutdown off
the UI thread, as the example does.

Choose `VideoResolution` and `DepthResolution` from `VideoResolutions` and `DepthResolutions`
before `Start()`. The chosen profiles must work together on the camera. `NewDepth` delivers
a matrix aligned to the color image; its raw values are not necessarily millimeters.
Both frame events run on the capture thread. The source disposes the color bitmap after
callbacks complete; clone it if processing must continue afterwards.

# Build and test

Run on Windows from the repository root:

```powershell
dotnet build UMapx.Video.RealSense.sln -c Release
dotnet build examples/UMapx.Video.RealSense.Example.sln -c Release
dotnet test tests/UMapx.Video.RealSense.Tests.csproj -c Release
dotnet test examples/tests/UMapx.Video.RealSense.Example.Tests.csproj -c Release
```

The root solution contains the library and its tests. The example and its WPF tests
are in the separate solution under `examples`.

Tests use a software RealSense device and a simulated capture backend; they do not require
a physical camera. They cover native SDK startup, depth alignment, row padding, conversion,
capture errors, concurrent shutdown, disposal and restarts. The example tests cover frame
display, error handling, reconnection and window shutdown. Sustained capture and USB
disconnect/reconnect still need testing on a physical device.

To run the example:

```powershell
dotnet run --project examples/UMapx.Video.RealSense.Example.csproj -c Release
```

# License
MIT
