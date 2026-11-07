// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Native.Unwind;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class NativeFrameTests
{
    [Test]
    public void SourceFramesSaveRbpWithoutRebasingOutgoingArguments()
    {
        const string Source = """
            struct Value {
                public int32 A; public int32 B; public int32 C;
                public Value(int32 a) { A = a; B = 2; C = 3; }
            }
            int32 Mix(int32 a, int32 b, int32 c, int32 d, int32 e, int32 f) {
                return a + b + c + d + e + f;
            }
            int32 Main() {
                Value value = new Value(42);
                return Mix(value.A, 2, 3, 4, 5, 6);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual0 = compilation.Program;
            Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual0!;
        }))();
        var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        AssertFunctionRangesAreOrdered(code);
        {
            var __nunitItems0 = code.Functions.Skip(6);
            Assert.Multiple(() => {
            foreach (var __nunitItem1 in __nunitItems0)
            {
                {
                    Assert.That(__nunitItem1.UsesFramePointer, Is.True);
                    Assert.That(code.Code[__nunitItem1.Start], Is.EqualTo((byte)0x55));
                    Assert.That(code.Code.AsSpan(__nunitItem1.Start + 8, 3).SequenceEqual(new byte[] { 0x48, 0x89, 0xE5 }), Is.True);
                    Assert.That(__nunitItem1.StackSize & 15, Is.EqualTo(8));
                }
            }
        });
        }
        Assert.That(global::System.Linq.Enumerable.Any(code.Functions.Take(6),function => !function.UsesFramePointer),Is.True);
        Assert.That(code.Code.AsSpan().IndexOf(new byte[] { 0x48, 0x89, 0x84, 0x24, 0x20, 0x00, 0x00, 0x00 }) >= 0, Is.True);
    }

    [Test]
    public void CleanupFuncletsUseTheParentFrameWithTheirOwnCallArea()
    {
        const string Source = """
             int32 Sixth(int32 a, int32 b, int32 c, int32 d, int32 e, int32 f) { return f; }
            int32 Main() {
                mut int32 value = 0;
                try {
                    try { value = 1; }
                    finally { value = Sixth(1, 2, 3, 4, 5, 42); }
                }

                finally { value = Sixth(1, 2, 3, 4, 5, value); }
                return value;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual2 = compilation.Program;
            Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual2!;
        }))();
        var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        AssertFunctionRangesAreOrdered(code);
        var funclets = code.Functions.Where(function => function.UsesParentFrame).ToArray();
        Assert.That(funclets.Length, Is.EqualTo(2));
        {
            var __nunitItems1 = funclets;
            Assert.Multiple(() => {
            foreach (var __nunitItem3 in __nunitItems1)
            {
                {
                    Assert.That(__nunitItem3.UsesFramePointer, Is.True);
                    Assert.That(code.Code[__nunitItem3.Start], Is.EqualTo((byte)0x55));
                    Assert.That(code.Code.AsSpan(__nunitItem3.Start + 8, 3).SequenceEqual(new byte[] { 0x48, 0x89, 0xD5 }), Is.True);
                }
            }
        });
        }
    }

    [Test]
    public async Task LargeFixedArrayFrameIsProbedAndExecutesAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var initializer = string.Join(", ", Enumerable.Repeat("0", 513));
        var source = $$"""
            int32 Main() {
                [uint64; 513] values = [{{initializer}}];
                return int32(values[512]);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        var probed = code.Functions.Single(static function =>
            function.UsesStackProbe && function.UsesFramePointer);

        Assert.That(probed.StackSize, Is.GreaterThan(0xF00));
        Assert.That(code.Functions.Count(static function =>
            function.IsStackProbeHelper), Is.EqualTo(1));
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast),
            Is.EqualTo(0));

        var image = NativeEmitter.EmitWithNativeImports(program,
            NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory("large-array-frame-");
        try
        {
            var path = Path.Combine(directory, "large-array-frame.exe");
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(false);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory,
            }) ?? throw new InvalidOperationException(
                "Could not start the large-frame native program.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                Assert.That(process.ExitCode, Is.EqualTo(0));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(false);
        }
    }

    [TestCase(32, false)]
    [TestCase(8192, true)]
    public async Task OverAlignedPackedArrayFramePreservesFieldsAndUnwindsAsync(
        int alignment, bool usesStackProbe)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var source = $$"""
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1, Alignment: {{alignment}})]
            public struct Packet {
                public mut uint8 Tag;
                public mut uint64 Middle;
                public mut uint8 Tail;
                public Packet(uint8 tag, uint64 middle, uint8 tail) {
                    Tag = tag; Middle = middle; Tail = tail;
                }
            }
            int32 Main() {
                mut Packet first = new Packet(uint8(17),
                    uint64(1234567890123456789), uint8(29));
                mut [Packet; 2] values = [first, first];
                values[1].Tag = uint8(91);
                values[1].Middle = uint64(987654321098765432);
                if (values[0].Tag != uint8(17)
                    || values[0].Middle != uint64(1234567890123456789)
                    || values[0].Tail != uint8(29)
                    || values[1].Tag != uint8(91)
                    || values[1].Middle != uint64(987654321098765432)
                    || values[1].Tail != uint8(29)) {
                    return 1;
                }
                uint32 outOfRange = 2;
                unsafe {
                    try {
                        return int32(values[outOfRange].Middle);
                    } catch {
                        return 0;
                    }
                }
            }
            """;
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.CheckedExceptions,
            LayoutCoreRegistration =
                LayoutCoreRegistrationTestData.Create(),
        };
        var compilation = Compilation.Create(SyntaxTree.Parse(source), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var packet = program.Structures.Single(static structure =>
            structure.Name == "Packet");
        Assert.That((packet.Fields[1].Offset, packet.Fields[2].Offset,
            packet.Size, packet.Alignment, packet.RequiredAlignment),
            Is.EqualTo((1, 9, alignment, alignment, alignment)));
        var array = global::System.Linq.Enumerable.Single(
            program.NativeFixedArrayTypes);
        Assert.That((array.ElementStride, array.ByteExtent,
            array.Alignment, array.RequiredAlignment),
            Is.EqualTo(((ulong)alignment, (ulong)(2 * alignment),
                (ulong)alignment, (ulong)alignment)));

        var code = NativeEmitter.EmitCode(program,
            NativeRuntimePolicy.CheckedExceptions);
        var alignedFrames = code.Functions.Where(static function =>
            function.UsesAlignedFrame).ToArray();
        Assert.That(alignedFrames, Is.Not.Empty);
        Assert.That(alignedFrames.All(function =>
            function.StackAlignment == alignment), Is.True);
        Assert.That(alignedFrames.Any(static function =>
            function.UsesStackProbe), Is.EqualTo(usesStackProbe));
        Assert.That(NativeJit.Execute(program,
            NativeRuntimePolicy.CheckedExceptions), Is.Zero);

        var image = NativeEmitter.EmitWithNativeImports(program,
            NativeRuntimePolicy.CheckedExceptions);
        var directory = TestArtifactPaths.CreateDirectory("overaligned-frame-");
        try
        {
            var path = Path.Combine(directory, "overaligned-frame.exe");
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(false);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory,
            }) ?? throw new InvalidOperationException(
                "Could not start the over-aligned native program.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                Assert.That(process.ExitCode, Is.Zero);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(false);
        }
    }

    [Test]
    public void LargeFixedArrayFrameUsesWideUnwindAllocation()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const int elementCount = 65_536;
        var initializer = string.Join(", ",
            Enumerable.Repeat("0", elementCount));
        var source = $$"""
            int32 Main() {
                [uint64; {{elementCount}}] values = [{{initializer}}];
                return int32(values[{{elementCount - 1}}]);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        var probed = code.Functions.Single(static function =>
            function.UsesStackProbe && function.UsesFramePointer);

        Assert.That(probed.StackSize, Is.GreaterThan(0x80000));
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast),
            Is.EqualTo(0));
    }

    [Test]
    public void LargeFixedArrayFrameUnwindsCheckedBoundsFailure()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var initializer = string.Join(", ", Enumerable.Repeat("0", 513));
        var source = $$"""
            int32 Main() {
                [uint64; 513] values = [{{initializer}}];
                uint32 index = 513;
                unsafe {
                    try {
                        return int32(values[index]);
                    } catch {
                        return 42;
                    }
                }
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");

        Assert.That(NativeJit.Execute(program,
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }

    [Test]
    public void OptionalAssignmentToOutParametersEmitsNativeCode()
    {
        const string Source = """
            bool SetIfEmpty(out ManagedNode? first, out ManagedNode? second) {
                first = null;
                second = null;
                first ??= null;
                second ??= null;
                return true;
            }
            int32 Main() { return 42; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual4 = compilation.Program;
            Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual4!;
        }))();
        var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        AssertFunctionRangesAreOrdered(code);
        Assert.That(code.Code.Length > 0, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(code.Functions.Skip(6),function => function.UsesFramePointer),Is.True);
    }

    private static void AssertFunctionRangesAreOrdered(NativeCode code)
    {
        for (var index = 1; index < code.Functions.Length; index++)
        {
            Assert.That(code.Functions[index - 1].Start < code.Functions[index].Start, Is.True);
        }
    }
}
