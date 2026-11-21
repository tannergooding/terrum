// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

internal sealed class TrRmNativeOutputMultipleCellsTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ProbeDelegate();

    [Test]
    public async Task StandardNativeOutputInitializesMultipleCellsAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string moduleIdentity = "native-output-multiple-filetimes";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct FileTime {
                public uint32 Low;
                public uint32 High;
            }

            [Terrum.Interop.NativeImport("kernel32.dll", "GetSystemTimes")]
            extern unsafe int32 GetSystemTimes(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<FileTime> idleTime,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<FileTime> kernelTime,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<FileTime> userTime);

            public unsafe int32 Probe() {
                init mut FileTime idleTime;
                init mut FileTime kernelTime;
                init mut FileTime userTime;
                unsafe {
                    return GetSystemTimes(&init idleTime,
                        &init kernelTime, &init userTime);
                }
            }
            """), NativeOutputCoreRegistrationTests
                .CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(compilation.Program!,
            moduleIdentity, schemaIdentity, moduleTargetRequirement: 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, moduleTargetRequirement: 1);
        Assert.That(reemitted, Is.EqualTo(artifact));
        var reemittedInput = new TrRmStandardArtifactInput(schemaIdentity,
            reemitted);
        _ = TrRmExecutableCompiler.LoadStandard(reemittedInput,
            moduleIdentity, []);
        var image = NativeModuleCompiler.EmitStandardLibrary(
            reemittedInput, moduleIdentity, [], ["Probe"],
            NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-native-output-multiple-cells-");
        try
        {
            var imagePath = Path.Combine(directory, "library.dll");
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            var library = NativeLibrary.Load(imagePath);
            try
            {
                var function = Marshal.GetDelegateForFunctionPointer<
                    ProbeDelegate>(NativeLibrary.GetExport(library, "Probe"));
                Assert.That(function(), Is.Not.EqualTo(0));
            }
            finally
            {
                NativeLibrary.Free(library);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }
}
