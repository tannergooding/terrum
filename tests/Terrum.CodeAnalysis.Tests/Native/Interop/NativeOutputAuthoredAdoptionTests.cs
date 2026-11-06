// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Native.Interop;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class NativeOutputAuthoredAdoptionTests
{
    private const string Declarations = """
        [Terrum.Interop.NativeImport("kernel32.dll", "GetCurrentProcess")]
        extern ptr<void> GetCurrentProcess();

        [Terrum.Interop.NativeImport("kernel32.dll", "DuplicateHandle")]
        extern unsafe int32 DuplicateHandle(ptr<void> sourceProcess,
            ptr<void> sourceHandle, ptr<void> targetProcess,
            [Terrum.Interop.NativeOutput(
                When: Terrum.Interop.NativeOutputCondition.NonZero)]
            write ptr<ptr<void>> targetHandle,
            uint32 desiredAccess, bool inheritHandle, uint32 options);

        [Terrum.Interop.NativeImport("kernel32.dll", "GetHandleInformation")]
        extern unsafe int32 GetHandleInformation(ptr<void> handle,
            [Terrum.Interop.NativeOutput(
                When: Terrum.Interop.NativeOutputCondition.NonZero)]
            write ptr<uint32> flags);

        [Terrum.Interop.NativeImport("kernel32.dll", "CloseHandle")]
        extern unsafe int32 CloseHandle(ptr<void> handle);

        unique struct HandleOwner {
            private ptr<void> Value;
            public HandleOwner(ptr<void> value) { Value = value; }

            [Cleanup]
            public void Release() {
                unsafe { require CloseHandle(Value) != 0; }
            }
        }

        void Retire(HandleOwner owner) {}
        """;

    private const string Source = Declarations + """
        public int32 Main() {
            unsafe {
                ptr<void> process = GetCurrentProcess();
                init ptr<void> duplicate;
                int32 status = DuplicateHandle(process, process, process,
                    &init duplicate, 0, false, 2);
                if (status == 0) { return -1; }

                ptr<void> probe = duplicate;
                {
                    HandleOwner owner = new HandleOwner(duplicate);
                }

                init uint32 flags;
                int32 isOpen = GetHandleInformation(probe, &init flags);
                return isOpen == 0 ? 42 : -2;
            }
        }
        """;

    private const string ExceptionalSource = Declarations + """
        public int32 Main() {
            unsafe {
                ptr<void> process = GetCurrentProcess();
                init ptr<void> duplicate;
                int32 status = DuplicateHandle(process, process, process,
                    &init duplicate, 0, false, 2);
                if (status == 0) { return -1; }

                ptr<void> probe = duplicate;
                mut int32 handled = 0;
                try {
                    HandleOwner owner = new HandleOwner(duplicate);
                    try { require false; }
                    finally { Retire(move owner); }
                } catch {
                    handled = 42;
                }

                init uint32 flags;
                int32 isOpen = GetHandleInformation(probe, &init flags);
                return handled == 42 && isOpen == 0 ? 42 : -2;
            }
        }
        """;

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void NativeOutputCustodyUsesAuthoredUniqueRetirement(
        NativeRuntimePolicy policy)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var nativeRegistration
            = NativeCoreRegistrationTests.CreateRegistration();
        var compilation = Compilation.Create(SyntaxTree.Parse(Source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                NativeCoreRegistration = nativeRegistration,
                LayoutCoreRegistration =
                    NativeCoreRegistrationTests.CreateLayoutRegistration(
                        nativeRegistration),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        Assert.That(NativeJit.Execute(compilation.Program!, policy),
            Is.EqualTo(42));
    }

    [Test]
    public void NativeOutputCustodySettlesDuringExplicitFinally()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var nativeRegistration
            = NativeCoreRegistrationTests.CreateRegistration();
        var compilation = Compilation.Create(SyntaxTree.Parse(ExceptionalSource),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.CheckedExceptions,
                NativeCoreRegistration = nativeRegistration,
                LayoutCoreRegistration =
                    NativeCoreRegistrationTests.CreateLayoutRegistration(
                        nativeRegistration),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NativeImageExecutesAuthoredNativeOutputCustodyAsync(
        NativeRuntimePolicy policy)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var nativeRegistration
            = NativeCoreRegistrationTests.CreateRegistration();
        var compilation = Compilation.Create(SyntaxTree.Parse(Source),
            new CompilationOptions {
                RuntimePolicy = (BoundRuntimePolicy)policy,
                NativeCoreRegistration = nativeRegistration,
                LayoutCoreRegistration =
                    NativeCoreRegistrationTests.CreateLayoutRegistration(
                        nativeRegistration),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var image = NativeEmitter.EmitWithNativeImports(
            compilation.Program!, policy);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-native-output-owner-");
        try
        {
            var executable = Path.Combine(directory, "owner.exe");
            await File.WriteAllBytesAsync(executable, image)
                .ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(executable) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = directory,
            }) ?? throw new InvalidOperationException(
                "Could not start the authored NativeOutput owner executable.");
            try
            {
                var standardOutput = process.StandardOutput.ReadToEndAsync();
                var standardError = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(30))
                    .ConfigureAwait(true);
                Assert.That(process.ExitCode, Is.EqualTo(42),
                    $"stdout: {await standardOutput.ConfigureAwait(true)}"
                    + $"{Environment.NewLine}stderr: "
                    + await standardError.ConfigureAwait(true));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }
}
