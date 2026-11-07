// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

internal sealed partial class CliTests
{
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task NativeProcessPseudoHandleImportRunsAsync(string policy)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string Source = """
            [Terrum.Interop.NativeImport("kernel32.dll", "GetCurrentProcess")]
            extern ptr<void> GetCurrentProcess();

            int32 Main() {
                unsafe {
                    ptr<void> process = GetCurrentProcess();
                    if (process == null) { return -1; }
                }
                return 42;
            }
            """;

        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-native-process-handle-");
        var nativeManifest = Path.Combine(directory, "native-core.json");
        try
        {
            await File.WriteAllTextAsync(nativeManifest,
                CreateNativeCoreManifest()).ConfigureAwait(false);
            var (exitCode, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes(Source), "jit", "-", "--runtime",
                policy, "--native-core-manifest", nativeManifest, "--json")
                .ConfigureAwait(false);

            Assert.That(exitCode, Is.EqualTo(42), output + error);
            Assert.That(error, Is.Empty);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(false);
        }
    }

    [Test]
    public async Task NativeAggregateWritePointerUsesInitializedStorageAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string Source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            struct FileTime {
                public uint32 Low;
                public uint32 High;
                public FileTime(uint32 low, uint32 high) {
                    Low = low;
                    High = high;
                }
            }

            [Terrum.Interop.NativeImport("kernel32.dll", "GetCurrentProcess")]
            extern ptr<void> GetCurrentProcess();

            [Terrum.Interop.NativeImport("kernel32.dll", "GetProcessTimes")]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(3, Write, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(4, Write, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(5, Write, NoEscape, 0)]
            extern int32 GetProcessTimes(ptr<void> process, write ptr<FileTime> creationTime,
                write ptr<FileTime> exitTime, write ptr<FileTime> kernelTime,
                write ptr<FileTime> userTime);

            int32 Main() {
                unsafe {
                    ptr<void> process = GetCurrentProcess();
                    mut FileTime creationTime = new FileTime(0, 0);
                    mut FileTime exitTime = new FileTime(0, 0);
                    mut FileTime kernelTime = new FileTime(0, 0);
                    mut FileTime userTime = new FileTime(0, 0);
                    int32 status = GetProcessTimes(process, &creationTime, &exitTime,
                        &kernelTime, &userTime);
                    if (status == 0) { return -1; }
                    if (creationTime.Low == 0 && creationTime.High == 0) { return -2; }
                    return 42;
                }
            }
            """;
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-native-aggregate-layout-");
        var layoutManifest = Path.Combine(directory, "layout.json");
        var nativeManifest = Path.Combine(directory, "native-core.json");
        try
        {
            await File.WriteAllTextAsync(layoutManifest,
                CreateLayoutCoreManifest()).ConfigureAwait(false);
            await File.WriteAllTextAsync(nativeManifest,
                CreateNativeCoreManifest()).ConfigureAwait(false);
            var (exitCode, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes(Source), "jit", "-", "--runtime",
                "fail-fast", "--native-core-manifest", nativeManifest,
                "--layout-core-manifest", layoutManifest, "--json")
                .ConfigureAwait(false);

            Assert.That(exitCode, Is.EqualTo(42), output + error);
            Assert.That(error, Is.Empty);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(false);
        }
    }

    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task NativeAggregateOutputPublishesOnlyAfterSuccessfulStatusAsync(string policy)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string Source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            struct FileTime {
                public uint32 Low;
                public uint32 High;
            }

            [Terrum.Interop.NativeImport("kernel32.dll", "GetCurrentProcess")]
            extern ptr<void> GetCurrentProcess();

            [Terrum.Interop.NativeImport("kernel32.dll", "GetProcessTimes")]
            extern int32 GetProcessTimes(
                ptr<void> process,
                [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<FileTime> creationTime,
                [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<FileTime> exitTime,
                [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<FileTime> kernelTime,
                [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<FileTime> userTime);

            int32 Main() {
                unsafe {
                    ptr<void> process = GetCurrentProcess();
                    init FileTime creationTime;
                    init FileTime exitTime;
                    init FileTime kernelTime;
                    init FileTime userTime;
                    int32 status = GetProcessTimes(process, &init creationTime, &init exitTime,
                        &init kernelTime, &init userTime);
                    if (status == 0) { return -1; }
                    if (creationTime.Low == 0 && creationTime.High == 0) { return -2; }
                    return 42;
                }
            }
            """;

        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-native-aggregate-output-layout-");
        var layoutManifest = Path.Combine(directory, "layout.json");
        var nativeManifest = Path.Combine(directory, "native-core.json");
        try
        {
            await File.WriteAllTextAsync(layoutManifest,
                CreateLayoutCoreManifest()).ConfigureAwait(false);
            await File.WriteAllTextAsync(nativeManifest,
                CreateNativeCoreManifest()).ConfigureAwait(false);
            var (exitCode, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes(Source), "jit", "-", "--runtime",
                policy, "--native-core-manifest", nativeManifest,
                "--layout-core-manifest", layoutManifest, "--json")
                .ConfigureAwait(false);

            Assert.That(exitCode, Is.EqualTo(42), output + error);
            Assert.That(error, Is.Empty);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(false);
        }
    }

    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task NativeScalarAndPointerOutputCellsUseExactStorageAsync(string policy)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string Source = """
            [Terrum.Interop.NativeImport("kernel32.dll", "QueryPerformanceCounter")]
            [param(ticks): Terrum.Interop.NativeOutput(
                When: Terrum.Interop.NativeOutputCondition.NonZero)]
            extern int32 QueryPerformanceCounter(write ptr<int64> ticks);

            [Terrum.Interop.NativeImport("kernel32.dll", "GetModuleHandleExW")]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 0)]
            [param(module): Terrum.Interop.NativeOutput(
                When: Terrum.Interop.NativeOutputCondition.NonZero)]
            extern int32 GetModuleHandleExW(uint32 flags, ptr<void> moduleName,
                write ptr<ptr<void>> module);

            int32 Main() {
                unsafe {
                    init int64 ticks;
                    int32 counterStatus = QueryPerformanceCounter(&init ticks);
                    if (counterStatus == 0 || ticks <= int64(0)) { return -1; }

                    init ptr<void> module;
                    int32 moduleStatus = GetModuleHandleExW(2, utf16z("kernel32.dll"),
                        &init module);
                    if (moduleStatus == 0 || module == null) { return -2; }
                }
                return 42;
            }
            """;

        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-native-output-cells-");
        var nativeManifest = Path.Combine(directory, "native-core.json");
        try
        {
            await File.WriteAllTextAsync(nativeManifest,
                CreateNativeCoreManifest()).ConfigureAwait(false);
            var (exitCode, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes(Source), "jit", "-", "--runtime",
                policy, "--native-core-manifest", nativeManifest, "--json")
                .ConfigureAwait(false);

            Assert.That(exitCode, Is.EqualTo(42), output + error);
            Assert.That(error, Is.Empty);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(false);
        }
    }
}
