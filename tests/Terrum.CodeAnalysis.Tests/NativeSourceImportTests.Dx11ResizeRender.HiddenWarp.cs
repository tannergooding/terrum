// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    [TestCase(960, 640, false, false, false, false)]
    [TestCase(513, 337, false, false, false, false)]
    [TestCase(128, 128, false, false, false, false)]
    [TestCase(1920, 1080, false, false, false, false)]
    [TestCase(960, 640, true, false, false, false)]
    [TestCase(960, 640, false, true, false, false)]
    [TestCase(960, 640, false, true, true, false)]
    [TestCase(960, 640, true, true, true, false)]
    [TestCase(960, 640, false, false, false, true)]
    public Task Dx11ResizeRenderExactMainRedrawsAfterPaintAsync(
        int clientWidth, int clientHeight, bool visibleOffscreen, bool coalesceResize,
        bool repeatResize, bool roundTripResize) =>
        RunDx11ResizeRenderExactMainRedrawsAfterPaintAsync(
            clientWidth, clientHeight, visibleOffscreen, coalesceResize, repeatResize, roundTripResize,
            hardware: false);

    [InteractiveWindowTest]
    [Category("InteractiveWindow")]
    public Task Dx11VisibleWarpExposureRedrawsAtTheSameExtentAsync() =>
        RunDx11ResizeRenderExactMainRedrawsAfterPaintAsync(960, 640,
            visibleOffscreen: false, coalesceResize: false, repeatResize: false,
            roundTripResize: false, hardware: false, visibleExposure: true);

    [InteractiveWindowTest]
    [Category("InteractiveWindow")]
    public Task Dx11VisibleWarpExposureObservesNaturalPaintBehaviorAsync() =>
        RunDx11ResizeRenderExactMainRedrawsAfterPaintAsync(960, 640,
            visibleOffscreen: false, coalesceResize: false, repeatResize: false,
            roundTripResize: false, hardware: false, visibleExposure: true,
            naturalExposure: true);

    [HiddenWarpIntegrationTest]
    [Category("HiddenWarpIntegration")]
    public Task Dx11ResizeRenderExactMainObservesHiddenWarpExtentsAsync() =>
        RunDx11ResizeRenderExactMainRedrawsAfterPaintAsync(960, 640,
            visibleOffscreen: false, coalesceResize: false, repeatResize: true,
            roundTripResize: false, hardware: false, observeExtents: true);

    [HiddenWarpIntegrationTest]
    [Category("HiddenWarpIntegration")]
    public Task Dx11ResizeRenderExactMainMinimizesAndRestoresHiddenWarpAsync() =>
        RunDx11ResizeRenderExactMainRedrawsAfterPaintAsync(513, 337,
            visibleOffscreen: false, coalesceResize: false, repeatResize: false,
            roundTripResize: false, hardware: false, observeExtents: true, minimizeRestore: true);

    [HardwareIntegrationTest]
    [Category("HardwareIntegration")]
    public Task Dx11HardwareResizeRenderExactMainRedrawsAfterPaintAsync() =>
        RunDx11ResizeRenderExactMainRedrawsAfterPaintAsync(960, 640,
            visibleOffscreen: true, coalesceResize: true, repeatResize: true,
            roundTripResize: false, hardware: true);

    [HardwareIntegrationTest]
    [Category("HardwareIntegration")]
    public Task Dx11HardwareResizeRenderExactMainMinimizesAndRestoresSameSizeAsync() =>
        RunDx11ResizeRenderExactMainRedrawsAfterPaintAsync(960, 640,
            visibleOffscreen: false, coalesceResize: false, repeatResize: false,
            roundTripResize: false, hardware: true, minimizeRestore: true, sameSizeRestore: true);

    private async Task RunDx11ResizeRenderExactMainRedrawsAfterPaintAsync(
        int clientWidth, int clientHeight, bool visibleOffscreen, bool coalesceResize,
        bool repeatResize, bool roundTripResize, bool hardware, bool observeExtents = false,
        bool visibleExposure = false, bool minimizeRestore = false,
        bool naturalExposure = false, bool sameSizeRestore = false)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        if (visibleExposure)
        {
            Assert.That(hardware || visibleOffscreen || coalesceResize || repeatResize
                || roundTripResize || observeExtents || minimizeRestore || sameSizeRestore, Is.False);
        }
        if (observeExtents)
        {
            Assert.That(hardware || visibleOffscreen || coalesceResize || roundTripResize, Is.False);
            Assert.That(repeatResize || minimizeRestore, Is.True);
        }
        if (minimizeRestore)
        {
            Assert.That(visibleOffscreen || coalesceResize || repeatResize
                || roundTripResize || visibleExposure || (hardware && !sameSizeRestore), Is.False);
            Assert.That(observeExtents || (hardware && sameSizeRestore), Is.True);
        }
        if (sameSizeRestore)
        {
            Assert.That(hardware && minimizeRestore, Is.True);
            Assert.That(visibleOffscreen || coalesceResize || repeatResize
                || roundTripResize || observeExtents || visibleExposure, Is.False);
        }
        var root = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(root).ConfigureAwait(true);
        var directory = TestArtifactPaths.CreateDirectory("terrum-dx11-composed-hidden-warp-");
        try
        {
            const string dllName = "native-dx11-resize-render-hidden-warp-probe.dll";
            var fixture = Path.Combine(root, "tests", "native", "native-dx11-resize-render-hidden-warp-probe.c");
            var dll = Path.Combine(directory, dllName);
            _ = await RunAsync(new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") {
                WorkingDirectory = directory,
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /wd4201 /Od /TC /LD /Fofixture.obj \"{fixture}\" /link /OUT:\"{dll}\" user32.lib d3d11.lib dxguid.lib\"",
            }, TimeSpan.FromSeconds(60)).ConfigureAwait(true);
            Assert.That(File.Exists(dll), Is.True);

            var sourceName = hardware ? "main-hardware.terrum" : "main.terrum";
            var program = AssertDx11SampleEntryPointUsesExactResizeRenderSeals(sourceName, hardware);
            var code = DirectWindowDeviceShaderCode(program);
            string[] intercepted = [
                "CreateWindowExW", "ShowWindow", "GetProcAddress", "PostMessageW", "PostQuitMessage",
                "DefWindowProcW", "PeekMessageW", "GetMessageW", "GetClientRect",
                "DestroyWindow", "UnregisterClassW",
            ];
            // Queue wrappers forward to user32 and observe only removed resize markers.
            // Instrument only the test image; the exact Terrum source and bound proof stay unchanged.
            var injected = code with {
                NativeImports = [.. code.NativeImports.Select(import => intercepted.Contains(import.ExportName)
                    ? import with { DllName = dllName, ExportName = "NativeFfiHiddenWarp" + import.ExportName }
                    : import)],
            };
            foreach (var export in new[] { "PeekMessageW", "GetMessageW" })
            {
                Assert.That(injected.NativeImports, Has.Some.Matches(import =>
                    import.DllName == dllName && import.ExportName == "NativeFfiHiddenWarp" + export));
            }
            Assert.That(injected.NativeImports, Has.Some.Matches(import =>
                import.DllName == dllName && import.ExportName == "NativeFfiHiddenWarpGetClientRect"));
            foreach (var export in new[] { "DispatchMessageW" })
            {
                var imports = injected.NativeImports.Where(item => item.ExportName == export).ToArray();
                Assert.That(imports, Is.Not.Empty);
                Assert.Multiple(() => {
                    foreach (var import in imports)
                        Assert.That(import.DllName, Is.Not.EqualTo(dllName));
                });
            }
            var executable = Path.Combine(directory, "composed-hidden-warp.exe");
            await File.WriteAllBytesAsync(executable, PeImage.Build(injected)).ConfigureAwait(true);
            File.Copy(Path.Combine(root, "samples", "design", "win32-dx11-triangle", "triangle.hlsl"),
                Path.Combine(directory, "triangle.hlsl"));
            var report = Path.Combine(directory, "evidence.txt");
            var start = new ProcessStartInfo(executable) { WorkingDirectory = directory };
            start.Environment["TERRUM_DX11_HIDDEN_WARP_REPORT"] = report;
            start.Environment["TERRUM_DX11_HIDDEN_WARP_WIDTH"] = clientWidth.ToString(CultureInfo.InvariantCulture);
            start.Environment["TERRUM_DX11_HIDDEN_WARP_HEIGHT"] = clientHeight.ToString(CultureInfo.InvariantCulture);
            start.Environment["TERRUM_DX11_HIDDEN_WARP_OFFSCREEN_VISIBLE"] = visibleOffscreen ? "1" : "0";
            start.Environment["TERRUM_DX11_HIDDEN_WARP_VISIBLE_EXPOSURE"] = visibleExposure ? "1" : "0";
            start.Environment["TERRUM_DX11_HIDDEN_WARP_NATURAL_EXPOSURE"] = naturalExposure ? "1" : "0";
            start.Environment["TERRUM_DX11_HIDDEN_WARP_COALESCE_RESIZE"] = coalesceResize ? "1" : "0";
            start.Environment["TERRUM_DX11_HIDDEN_WARP_REPEAT_RESIZE"] = repeatResize ? "1" : "0";
            start.Environment["TERRUM_DX11_HIDDEN_WARP_ROUND_TRIP_RESIZE"] = roundTripResize ? "1" : "0";
            start.Environment["TERRUM_RUN_INTERACTIVE_WINDOW_TESTS"] = "0";
            start.Environment["TERRUM_RUN_DX11_HARDWARE"] = "0";
            start.Environment["TERRUM_DX11_HIDDEN_WARP_HARDWARE"] = hardware ? "1" : "0";
            start.Environment["TERRUM_DX11_HIDDEN_WARP_OBSERVE_EXTENTS"] = observeExtents ? "1" : "0";
            start.Environment["TERRUM_DX11_HIDDEN_WARP_MINIMIZE_RESTORE"] = minimizeRestore ? "1" : "0";
            start.Environment["TERRUM_DX11_HIDDEN_WARP_MINIMIZE_RESTORE_SAME_SIZE"] =
                sameSizeRestore ? "1" : "0";
            var (exit, output, error) = await RunNativeExecutableAsync(start).ConfigureAwait(true);
            var evidence = File.Exists(report)
                ? await File.ReadAllTextAsync(report).ConfigureAwait(true) : "<missing report>";
            TestContext.Progress.WriteLine(evidence);
            var driver = hardware ? "hardware" : "WARP";
            var execution = naturalExposure ? "Natural visible exposure"
                : visibleExposure ? "Visible exposure"
                : (sameSizeRestore ? "Same-size minimize/restore offscreen"
                    : (minimizeRestore ? "Minimize/restore offscreen" : "Offscreen composed"));
            Assert.That(exit == 0, Is.True, $"{execution} {driver} renderer exited {exit}: {error}\n{evidence}");
            Assert.That(output, Is.Empty);
            Assert.That(error, Is.Empty);
            var events = evidence.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.That(events[0], Is.EqualTo(visibleExposure ? "create-visible-window" : "create-hidden-window"));
            Assert.That(events[^1], Is.EqualTo("complete"));
            if (visibleExposure)
            {
                Assert.That(events, Does.Contain("show-visible"));
                Assert.That(events, Does.Contain("cover-window-shown"));
                Assert.That(events, Does.Contain("cover-window-hidden"));
                var naturalExposureRedraw = naturalExposure
                    && events.Contains("natural-exposure-redraw", StringComparer.Ordinal);
                if (naturalExposure)
                {
                    Assert.That(events, Does.Contain("await-natural-exposure"));
                    Assert.That(events, Does.Not.Contain("invalidate-after-uncover"));
                    Assert.That(events.Single(static item => item is "natural-exposure-redraw"
                            or "natural-exposure-no-paint"), Is.EqualTo(naturalExposureRedraw
                        ? "natural-exposure-redraw" : "natural-exposure-no-paint"));
                    Assert.That(events.Contains("natural-exposure-paint-before-dispatch", StringComparer.Ordinal), Is.EqualTo(naturalExposureRedraw));
                    Assert.That(events.Contains("natural-exposure-paint-validated", StringComparer.Ordinal), Is.EqualTo(naturalExposureRedraw));
                }
                else
                {
                    Assert.That(events, Does.Contain("invalidate-after-uncover"));
                }
                var exposureFrameCount = naturalExposure && !naturalExposureRedraw ? 1 : 2;
                Assert.That(events.Count(static item => item.StartsWith("back-buffer:1:", StringComparison.Ordinal)), Is.EqualTo(exposureFrameCount));
                Assert.That(events.Count(static item => item.StartsWith("render-target:1", StringComparison.Ordinal)), Is.EqualTo(exposureFrameCount));
                Assert.That(events.Count(static item => item.StartsWith("draw:1:", StringComparison.Ordinal)), Is.EqualTo(exposureFrameCount));
                Assert.That(events.Count(static item => item.StartsWith("pixels:1:", StringComparison.Ordinal)), Is.EqualTo(exposureFrameCount));
                Assert.That(events.Count(static item => item.StartsWith("present:1:", StringComparison.Ordinal)), Is.EqualTo(exposureFrameCount));
                Assert.That(events.Count(static item => item == "release-render-target"), Is.EqualTo(exposureFrameCount));
                Assert.That(events.Count(static item => item == "release-back-buffer"), Is.EqualTo(exposureFrameCount));
                var resizeBuffers = events.Where(static item =>
                    item.StartsWith("resize-buffers:", StringComparison.Ordinal)).ToArray();
                var backBuffers = events.Where(static item =>
                    item.StartsWith("back-buffer:", StringComparison.Ordinal)).ToArray();
                var viewports = events.Where(static item =>
                    item.StartsWith("viewport:", StringComparison.Ordinal)).ToArray();
                Assert.That(resizeBuffers, Has.Exactly(1).Items);
                Assert.That(resizeBuffers[0].StartsWith("resize-buffers:1:", StringComparison.Ordinal), Is.True);
                Assert.That(backBuffers.Length, Is.EqualTo(exposureFrameCount));
                Assert.That(viewports.Length, Is.EqualTo(exposureFrameCount));
                if (exposureFrameCount == 2)
                {
                    Assert.That(backBuffers[1].Split(':')[2], Is.EqualTo(backBuffers[0].Split(':')[2]));
                    Assert.That(viewports[1], Is.EqualTo(viewports[0]));
                }
                Assert.That(events, Has.None.Matches(static item => item.StartsWith("client-resize:", StringComparison.Ordinal)));
                Assert.That(events, Does.Not.Contain("resize-marker"));
                Assert.That(events, Does.Contain("post-close"));
                Assert.That(events, Does.Contain("post-quit:0"));
                var milestones = naturalExposure
                    ? naturalExposureRedraw
                        ? new[] {
                            "present:1:1:", "cover-window-shown", "await-natural-exposure",
                            "cover-window-hidden", "natural-exposure-paint-before-dispatch",
                            "natural-exposure-paint-validated", "draw:1:2", "pixels:1:2",
                            "present:1:2:", "natural-exposure-redraw", "post-close", "post-quit:0",
                            "clear-state", "release-context", "release-swap-chain", "release-device",
                            "destroy-visible-window", "unregister-class", "complete",
                        }
                        : new[] {
                            "present:1:1:", "cover-window-shown", "await-natural-exposure",
                            "cover-window-hidden", "natural-exposure-no-paint",
                            "post-close", "post-quit:0", "clear-state", "release-context",
                            "release-swap-chain", "release-device", "destroy-visible-window",
                            "unregister-class", "complete",
                        }
                    : new[] {
                        "present:1:1:", "cover-window-shown", "cover-window-hidden",
                        "invalidate-after-uncover", "draw:1:2", "pixels:1:2",
                        "present:1:2:", "post-close", "post-quit:0",
                        "clear-state", "release-context", "release-swap-chain", "release-device",
                        "destroy-visible-window", "unregister-class", "complete",
                    };
                var exposurePrevious = -1;
                foreach (var milestone in milestones)
                {
                    var next = Array.FindIndex(events, exposurePrevious + 1,
                        item => item.StartsWith(milestone, StringComparison.Ordinal));
                    Assert.That(next > exposurePrevious, Is.True, $"Missing/out-of-order event {milestone}:\n{evidence}");
                    exposurePrevious = next;
                }
                return;
            }
            if (minimizeRestore)
            {
                var minimizeInitialExtent = events.First(static item =>
                    item.StartsWith("back-buffer:1:", StringComparison.Ordinal)).Split(':')[2];
                var restoredExtent = sameSizeRestore
                    ? minimizeInitialExtent : $"{clientWidth}x{clientHeight}";
                if (sameSizeRestore)
                {
                    Assert.That(events, Does.Contain("create-hardware"));
                    Assert.That(events, Does.Not.Contain("create-warp"));
                    Assert.That(events, Does.Contain($"same-size-original-client:{minimizeInitialExtent}"));
                    Assert.That(events, Does.Contain($"same-size-restored-client:{minimizeInitialExtent}"));
                }
                else
                {
                    Assert.That(restoredExtent, Is.Not.EqualTo(minimizeInitialExtent));
                }
                Assert.That(events, Does.Contain("suppress-show"));
                Assert.That(events, Does.Not.Contain("show-offscreen"));
                Assert.That(events, Does.Not.Contain("show-visible"));
                var minimizeIndex = Array.IndexOf(events, "minimized");
                var minimizeMarkerIndex = Array.IndexOf(events, "consumed-size:minimized");
                var minimizedWaitIndex = Array.IndexOf(events, "minimized-wait-entered");
                var quiescentIndex = Array.IndexOf(events, "minimized-quiescent");
                Assert.That(minimizeIndex >= 0 && minimizeMarkerIndex > minimizeIndex
                    && minimizedWaitIndex > minimizeMarkerIndex && quiescentIndex > minimizedWaitIndex, Is.True, $"Missing minimized/quiescent evidence:\n{evidence}");
                Assert.That(events.Skip(minimizeIndex + 1)
                    .Take(quiescentIndex - minimizeIndex - 1), Has.None.Matches(static item =>
                        item.StartsWith("resize-buffers:", StringComparison.Ordinal)
                        || item.StartsWith("back-buffer:", StringComparison.Ordinal)
                        || item.StartsWith("render-target:", StringComparison.Ordinal)
                        || item.StartsWith("viewport:", StringComparison.Ordinal)
                        || item.StartsWith("draw:", StringComparison.Ordinal)
                        || item.StartsWith("pixels:", StringComparison.Ordinal)
                        || item.StartsWith("present:", StringComparison.Ordinal)
                        || item == "release-render-target"
                        || item == "release-back-buffer"
                        || item == "clear-state"));
                Assert.That(events.Count(static item => item.StartsWith("back-buffer:", StringComparison.Ordinal)), Is.EqualTo(2));
                Assert.That(events.Count(static item => item.StartsWith("render-target:", StringComparison.Ordinal)), Is.EqualTo(2));
                Assert.That(events.Count(static item => item.StartsWith("viewport:", StringComparison.Ordinal)), Is.EqualTo(2));
                Assert.That(events.Count(static item => item.StartsWith("draw:", StringComparison.Ordinal)), Is.EqualTo(2));
                Assert.That(events.Count(static item => item.StartsWith("pixels:", StringComparison.Ordinal)), Is.EqualTo(2));
                Assert.That(events.Count(static item => item.StartsWith("present:", StringComparison.Ordinal)), Is.EqualTo(2));
                Assert.That(events.Count(static item => item == "release-render-target"), Is.EqualTo(2));
                Assert.That(events.Count(static item => item == "release-back-buffer"), Is.EqualTo(2));
                var resizeEvents = events.Where(static item =>
                    item.StartsWith("resize-buffers:", StringComparison.Ordinal));
                string[] expectedResizeEvents = sameSizeRestore
                    ? [$"resize-buffers:1:{minimizeInitialExtent}:0"]
                    : [
                        $"resize-buffers:1:{minimizeInitialExtent}:0",
                        $"resize-buffers:2:{restoredExtent}:0",
                    ];
                Assert.That(resizeEvents, Is.EqualTo(expectedResizeEvents));
                if (!sameSizeRestore)
                {
                    foreach (var observation in new[] { "observed-client", "observed-back-buffer", "observed-viewport" })
                    {
                        Assert.That(events.Where(item => item.StartsWith(observation + ":", StringComparison.Ordinal)), Is.EqualTo(new[] {
                            $"{observation}:1:1:{minimizeInitialExtent}",
                            $"{observation}:2:2:{restoredExtent}",
                        }));
                    }
                }
                string[] sameSizeOriginalMilestone = sameSizeRestore
                    ? [$"same-size-original-client:{minimizeInitialExtent}"] : [];
                string[] sameSizeRestoredMilestone = sameSizeRestore
                    ? [$"same-size-restored-client:{minimizeInitialExtent}"] : [];
                string[] restoreInvalidateMilestone = sameSizeRestore
                    ? ["restore-invalidate"] : [];
                string[] resizeMilestone = sameSizeRestore
                    ? [] : [$"resize-buffers:2:{restoredExtent}:0"];
                string[] milestones = [
                    "suppress-show", "draw:1:1", "pixels:1:1", "present:1:1:",
                    "clear-state", "release-render-target", "release-back-buffer",
                    .. sameSizeOriginalMilestone,
                    "window-offscreen", "offscreen-placement-captured", "minimize-request",
                    "size-notification:minimized", "minimized", "consumed-size:minimized",
                    "minimized-wait-entered", "timer-restore-armed", "minimized-quiescent",
                    "restore-window-hidden", "restore-placement-hidden",
                    "restore-position-forced-offscreen",
                    $"queued-size:restored:{restoredExtent}", "restored-offscreen",
                    $"restored:{restoredExtent}",
                    .. sameSizeRestoredMilestone,
                    .. restoreInvalidateMilestone,
                    "consumed-size:restored",
                    .. resizeMilestone,
                    $"back-buffer:{(sameSizeRestore ? 1 : 2)}:{restoredExtent}",
                    $"render-target:{(sameSizeRestore ? 1 : 2)}",
                    $"viewport:{(sameSizeRestore ? 1 : 2)}:{restoredExtent}",
                    $"draw:{(sameSizeRestore ? 1 : 2)}:2",
                    $"pixels:{(sameSizeRestore ? 1 : 2)}:2",
                    $"present:{(sameSizeRestore ? 1 : 2)}:2:",
                    "post-close", "clear-state", "release-render-target", "release-back-buffer",
                    "post-quit:0",
                    "clear-state", "release-context", "release-swap-chain", "release-device",
                    "destroy-restored-offscreen-window", "unregister-class", "complete",
                ];
                var minimizePrevious = -1;
                foreach (var milestone in milestones)
                {
                    var next = Array.FindIndex(events, minimizePrevious + 1,
                        item => item.StartsWith(milestone, StringComparison.Ordinal));
                    Assert.That(next > minimizePrevious, Is.True, $"Missing/out-of-order event {milestone}:\n{evidence}");
                    minimizePrevious = next;
                }
                return;
            }
            var frameCount = repeatResize ? 4 : (roundTripResize ? 2 : 3);
            var finalGeneration = repeatResize ? 3 : (roundTripResize ? 1 : 2);
            var initialExtent = events.First(static item =>
                item.StartsWith("back-buffer:1:", StringComparison.Ordinal)).Split(':')[2];
            var resizedExtent = $"{clientWidth}x{clientHeight}";
            var finalExtent = repeatResize ? "513x337"
                : (roundTripResize ? initialExtent : resizedExtent);
            foreach (var observation in new[] { "observed-client", "observed-back-buffer", "observed-viewport" })
            {
                var actual = events.Where(item => item.StartsWith(observation + ":", StringComparison.Ordinal));
                string[] expected = observeExtents ? [
                    $"{observation}:1:1:{initialExtent}",
                    $"{observation}:2:2:{resizedExtent}",
                    $"{observation}:3:3:{finalExtent}",
                    $"{observation}:3:4:{finalExtent}",
                ] : [];
                Assert.That(actual, Is.EqualTo(expected));
            }
            if (observeExtents)
            {
                Assert.That(events, Does.Contain("suppress-show"));
                Assert.That(events, Does.Not.Contain("show-offscreen"));
            }
            var expectedResizeMarkers = roundTripResize ? 2
                : (coalesceResize ? 2 : 1) * (repeatResize ? 2 : 1);
            Assert.That(events.Count(static item => item.StartsWith("resize-buffers:", StringComparison.Ordinal)), Is.EqualTo(repeatResize ? 3 : (roundTripResize ? 1 : 2)));
            Assert.That(events.Count(static item => item.StartsWith("back-buffer:", StringComparison.Ordinal)), Is.EqualTo(frameCount));
            Assert.That(events.Count(static item => item.StartsWith("render-target:", StringComparison.Ordinal)), Is.EqualTo(frameCount));
            Assert.That(events.Count(static item => item.StartsWith("pixels:", StringComparison.Ordinal)), Is.EqualTo(frameCount));
            Assert.That(events.Count(static item => item.StartsWith("present:", StringComparison.Ordinal)), Is.EqualTo(frameCount));
            Assert.That(events.Count(static item => item == "release-render-target"), Is.EqualTo(frameCount));
            Assert.That(events.Count(static item => item == "release-back-buffer"), Is.EqualTo(frameCount));
            Assert.That(events, Does.Contain($"client-resize:{(roundTripResize ? initialExtent : resizedExtent)}"));
            Assert.That(events.Count(static item => item.StartsWith("client-resize:", StringComparison.Ordinal)), Is.EqualTo(repeatResize ? 2 : 1));
            if (roundTripResize)
            {
                Assert.That(events, Has.None.Matches(static item => item.StartsWith("resize-buffers:2:", StringComparison.Ordinal)));
            }
            else
            {
                Assert.That(events, Does.Contain($"resize-buffers:2:{resizedExtent}:0"));
            }
            Assert.That(events.Count(static item => item == "resize-marker"), Is.EqualTo(expectedResizeMarkers));
            Assert.That(events, Does.Contain($"back-buffer:{finalGeneration}:{finalExtent}"));
            Assert.That(events, Does.Contain($"render-target:{finalGeneration}"));
            Assert.That(events, Does.Contain($"viewport:{finalGeneration}:{finalExtent}"));
            if (roundTripResize)
            {
                Assert.That(events, Does.Contain("draw:1:2"));
                Assert.That(events, Does.Contain("pixels:1:2"));
                Assert.That(events, Has.Some.Matches(static item => item.StartsWith("present:1:2:", StringComparison.Ordinal)));
            }
            else if (repeatResize)
            {
                Assert.That(events, Does.Contain($"client-resize:{finalExtent}"));
                Assert.That(events, Does.Contain($"resize-buffers:3:{finalExtent}:0"));
                Assert.That(events, Does.Contain("draw:3:3"));
                Assert.That(events, Does.Contain("draw:3:4"));
                Assert.That(events, Does.Contain("pixels:3:3"));
                Assert.That(events, Does.Contain("pixels:3:4"));
                Assert.That(events, Has.Some.Matches(static item => item.StartsWith("present:3:3:", StringComparison.Ordinal)));
                Assert.That(events, Has.Some.Matches(static item => item.StartsWith("present:3:4:", StringComparison.Ordinal)));
            }
            else
            {
                Assert.That(events, Does.Contain("draw:2:2"));
                Assert.That(events, Does.Contain("pixels:2:2"));
                Assert.That(events, Has.Some.Matches(static item => item.StartsWith("present:2:2:", StringComparison.Ordinal)));
                Assert.That(events, Does.Contain("draw:2:3"));
                Assert.That(events, Does.Contain("pixels:2:3"));
                Assert.That(events, Has.Some.Matches(static item => item.StartsWith("present:2:3:", StringComparison.Ordinal)));
            }
            var paintRequestEvents = visibleOffscreen
                ? new[] { "invalidate-paint-region" }
                : new[] { "post-paint" };
            var paintDispatchEvents = visibleOffscreen
                ? new[] { "paint-region-before-dispatch", "paint-region-validated" }
                : Array.Empty<string>();
            foreach (var paintEvent in paintRequestEvents.Concat(paintDispatchEvents))
            {
                Assert.That(events.Count(item => item == paintEvent), Is.EqualTo(1));
            }
            var paintIndex = Array.IndexOf(events, paintRequestEvents[0]);
            Assert.That(events.Skip(paintIndex + 1), Has.None.Matches(static item => item.StartsWith("resize-buffers:", StringComparison.Ordinal)));
            Assert.That(events, Does.Contain("post-quit:0"));
            var beforePaint = roundTripResize
                ? new[] { "pixels:1:1", $"client-resize:{initialExtent}" }
                : new[] {
                    "pixels:1:1", "client-resize:" + resizedExtent,
                    "release-render-target", "release-back-buffer",
                    $"resize-buffers:2:{resizedExtent}:0", $"back-buffer:2:{resizedExtent}",
                    "render-target:2", $"viewport:2:{resizedExtent}", "draw:2:2", "pixels:2:2",
                };
            if (repeatResize)
            {
                beforePaint = [
                    .. beforePaint,
                    $"client-resize:{finalExtent}",
                    "release-render-target", "release-back-buffer",
                    $"resize-buffers:3:{finalExtent}:0", $"back-buffer:3:{finalExtent}",
                    "render-target:3", $"viewport:3:{finalExtent}", "draw:3:3", "pixels:3:3",
                ];
            }
            string[] afterRetirement = ["release-render-target", "release-back-buffer"];
            string[] afterPaint = [
                $"back-buffer:{finalGeneration}:{finalExtent}", $"render-target:{finalGeneration}",
                $"viewport:{finalGeneration}:{finalExtent}",
                $"draw:{finalGeneration}:{frameCount}", $"pixels:{finalGeneration}:{frameCount}",
                "post-close", "post-quit:0", "release-context",
                "release-swap-chain", "release-device", "destroy-hidden-window", "unregister-class", "complete",
            ];
            var ordered = beforePaint.Concat(paintRequestEvents).Concat(afterRetirement)
                .Concat(paintDispatchEvents).Concat(afterPaint).ToArray();
            var previous = -1;
            foreach (var item in ordered)
            {
                var next = Array.IndexOf(events, item, previous + 1);
                Assert.That(next > previous, Is.True, $"Missing/out-of-order event {item}:\n{evidence}");
                previous = next;
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class HiddenWarpIntegrationTestAttribute : ConditionalSkipTestAttribute
    {
        protected override string? GetSkipReason()
        {
            if (Environment.GetEnvironmentVariable("TERRUM_RUN_DX11_HIDDEN_WARP") != "1")
            {
                return "Not exercised: opt in with TERRUM_RUN_DX11_HIDDEN_WARP=1.";
            }
            if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                return "Not exercised: requires Windows x64.";
            }
            return null;
        }
    }
}
