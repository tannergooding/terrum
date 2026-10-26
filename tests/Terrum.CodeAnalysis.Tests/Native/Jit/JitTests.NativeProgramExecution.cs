// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

public sealed partial class JitTests
{
    internal static async Task AssertProgramExecutionAsync(
        BoundProgram program, int expected, NativeRuntimePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(program);
        AssertWindowsX64();
        var boundPolicy = policy == NativeRuntimePolicy.FailFast
            ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
        var executable = program with { RuntimePolicy = boundPolicy };
        var directory = TestArtifactPaths.CreateDirectory("jit-tests");
        var image = Path.Combine(directory, $"wide-scalar-{Guid.NewGuid():N}.exe");

        try
        {
            await File.WriteAllBytesAsync(image, NativeEmitter.Emit(executable, policy))
                .ConfigureAwait(true);
            var (nativeExit, output, error) = await RunAsync(new ProcessStartInfo(image) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }, []).ConfigureAwait(true);

            Assert.That(nativeExit, Is.EqualTo(expected));
            Assert.That(output, Is.Empty);
            Assert.That(error, Is.Empty);
            Assert.That(NativeJit.Execute(executable, policy), Is.EqualTo(nativeExit));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(image).ConfigureAwait(true);
        }
    }
}
