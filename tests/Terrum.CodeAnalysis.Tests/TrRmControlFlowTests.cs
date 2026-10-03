// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmControlFlowTests
{
    private static BoundProgram Compile(string source, bool library = false,
        BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast)
    {
        var tree = SyntaxTree.Parse(source);
        var compilation = library ? Compilation.CreateLibrary(tree, policy) : Compilation.Create(tree, policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    [Theory]
    [InlineData("int32 Main() { if (true) { return 1; } return 0; }", 1)]
    [InlineData("int32 Main() { return true ? 42 : 0; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 5) { x = x + 1; } return x; }", 5)]
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 0) { x = 1 / 0; } return 42; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 0; if (x == 0) { x = 40; } else { x = 1 / 0; } return x + 2; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 0; bool choose = x == 0; return choose ? 42 : 1 / 0; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 0; bool choose = x == 0; if (choose && x == 0) { x = 42; } return x; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 0; bool choose = x != 0; if (choose || x == 0) { x = 42; } return x; }", 42)]
    [InlineData("int32 Main() { int32 x = 0; return (x != 0 && 1 / x == 0) ? 1 : 42; }", 42)]
    [InlineData("int32 Main() { int32 x = 0; return (x == 0 || 1 / x == 0) ? 42 : 1; }", 42)]
    [InlineData("int32 Main() { bool x = true; bool y = x && false; return 0; }", 0)]
    [InlineData("int32 Main() { mut int32 x = 0; x = x == 0 ? 42 : 1 / 0; return x; }", 42)]
    [InlineData("int32 Main() { int32 x = 0; int32 y = x == 0 ? 42 : 1 / 0; return y; }", 42)]
    [InlineData("int32 Main() { int32 x = 0; return x == 0 ? (x != 0 ? 1 / 0 : 42) : 1 / 0; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 3 && x / 1 < 4) { x = x + 1; } return x; }", 3)]
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 5) { x = x + 1; if (x == 2) { continue; } if (x == 4) { break; } } return x; }", 4)]
    [InlineData("int32 Main() { mut int32 x = 0; mut int32 y = 1; while (x < 4) { x = y; y = x + 1; } return x + y; }", 9)]
    [InlineData("int32 Main() { mut int32 x = 0; if (x == 0) { while (x < 3) { x = x + 1; } } else { return 1 / 0; } return x; }", 3)]
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 3) { x = x + 1; if (x == 2) { return 42; } } return x; }", 42)]
    [InlineData("int32 Main() { int32 x = 0; if (x == 0) { return 42; } else { return 1 / 0; } }", 42)]
    [InlineData("int32 Main() { int32 x = 0; return (x == 0 ? 40 : 1 / 0) + (x == 0 ? 2 : 1 / 0); }", 42)]
    [InlineData("int32 Main() { int32 x = 0; int64 y = x == 0 ? 5000000000 : 1; return int32(y - 4999999958); }", 42)]
    [InlineData("int32 Main() { int32 x = 0; uint64 y = x == 0 ? 18446744073709551615 : 1; return int32(y - 18446744073709551573); }", 42)]
    [InlineData("int32 Main() { int32 x = 0; uint32 y = x == 0 ? 4294967295 : 1; return int32(y - 4294967253); }", 42)]
    [InlineData("int32 Main() { int32 x = 0; char16 y = x == 0 ? 97 : 98; return y == char16(97) ? 42 : 0; }", 42)]
    public void IndexedScalarFlowRoundTripsThroughSourceFreeJitAndNativeImage(string source, int expected)
    {
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "indexed");
        TrRmStructuralMetadataValidator.ValidateCompact(artifact);
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.Equal(expected, NativeJit.Execute(original, NativeRuntimePolicy.FailFast));
        Assert.Equal(expected, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
        Assert.NotEmpty(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast));
        var index = new TrRmArtifactIndex(artifact);
        Assert.True(index.TryGetSection(0x20, out var bodyBytes, out var count));
        Assert.Equal((byte)0x08, (byte)(new TrRmBodyTableReader(bodyBytes, count).GetRow(1)[0] & 0x0C));
    }

    [Fact]
    public void NestedLoopsPreserveNearestBreakAndContinue()
    {
        const string source = """
            int32 Main() {
                mut int32 outer = 0;
                mut int32 count = 0;
                mut int32 inner = 0;
                while (outer < 3) {
                    outer = outer + 1;
                    inner = 0;
                    while (inner < 4) {
                        inner = inner + 1;
                        if (inner == 2) { continue; }
                        if (inner == 3) { break; }
                        count = count + 1;
                    }
                }
                return count;
            }
            """;
        var original = Compile(source);
        Assert.Equal(3, NativeJit.Execute(original, NativeRuntimePolicy.FailFast));
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(original, "nested"));
        Assert.Equal(3, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void LongIndexedBodyWidensOffsetsAndPreservesTheDefinitionSentinel()
    {
        var source = new StringBuilder("int32 Main() { mut int32 x = 0; while (x < 1) { ");
        for (var i = 0; i < 28; i++)
        {
            source.Append("x = x + 1; ");
        }

        source.Append("} return x; }");
        var artifact = TrRmExecutableCompiler.Emit(Compile(source.ToString()), "wide-indexed");
        var index = new TrRmArtifactIndex(artifact);
        Assert.True(index.TryGetSection(0x20, out var bodyBytes, out var count));
        var body = new TrRmBodyTableReader(bodyBytes, count).GetRow(1);
        Assert.Equal(0x09, body[0]);
        Assert.Equal(28, NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(125, true)]
    [InlineData(126, false)]
    public void MaterializedValueLimitIsEnforcedAtTheNativeBoundary(int increments, bool supported)
    {
        var source = new StringBuilder("int32 Main() { mut int32 x = 0; while (x < 1) { ");
        for (var i = 0; i < increments; i++)
        {
            source.Append("x = x + 1; ");
        }
        source.Append("} return +x; }");
        var original = Compile(source.ToString());
        if (!supported)
        {
            Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(original, "value-limit"));
            return;
        }
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(original, "value-limit"));
        Assert.Equal(256, loaded.Functions[0].Variables.Length);
        Assert.Equal(increments, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int Choose(int value);

    [Fact]
    public void PersistedIndexedLibraryExportExecutesIndependentCalls()
    {
        const string source = """
            public int32 Choose(int32 value) {
                mut int32 result = value;
                if (value > 0) { result = value + 2; } else { result = 42; }
                return result;
            }
            """;
        var loaded = TrRmExecutableCompiler.Load(
            TrRmExecutableCompiler.Emit(Compile(source, true), "indexed-library"));
        var image = NativeEmitter.EmitLibrary(loaded, ["Choose"], NativeRuntimePolicy.FailFast);
        var path = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-indexed-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(path, image);
            var handle = NativeLibrary.Load(path);
            try
            {
                var choose = Marshal.GetDelegateForFunctionPointer<Choose>(
                    NativeLibrary.GetExport(handle, "Choose"));
                Assert.Equal(42, choose(0));
                Assert.Equal(9, choose(7));
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CheckedExceptionPolicyRemainsAvailableToDirectSourceButNotIndexedPersistence()
    {
        const string source = "int32 Main() { mut int32 x = 0; while (x < 2) { x = x + 1; } return x; }";
        var checkedProgram = Compile(source, policy: BoundRuntimePolicy.CheckedExceptions);
        Assert.Equal(2, NativeJit.Execute(checkedProgram, NativeRuntimePolicy.CheckedExceptions));
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(checkedProgram, "policy"));
    }

    [Fact]
    public void NoThrowCannotRetainAnUnprovedIndexedFailureTerminator()
    {
        var program = Compile("[NoThrow] void Guard() { require true; } int32 Main() { return 42; }");
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(program, "no-throw-failure"));
    }

    [Theory]
    [InlineData("int32 Main() { mut int32 x = 0; while (x < 2) { int32 inner = x; x = inner + 1; } return x; }", 2)]
    [InlineData("int32 Main() { mut int32 x = 0; if (x == 0) { int32 inner = x; x = inner + 1; } return x; }", 1)]
    public void ScopedScalarLocalsExecuteThroughIndexedFlow(string source, int expected)
    {
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(Compile(source), "scoped"));
        Assert.Equal(expected, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void IndexedEvidenceCannotBeReusedUnderDifferentDeclarationContracts()
    {
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(Compile(
            "public void Action(bool choose) { if (choose) { return; } }", true), "contexts"));
        var function = loaded.Functions[0];
        BoundFunction[] invalid =
        [
            function with { DoesNotReturn = true },
            function with { IsNoThrow = !function.IsNoThrow },
            function with { ParameterCount = 0 },
            function with { ReturnType = PrimitiveType.Int32 },
            function with { IsStatic = true },
            function with { IsExternal = true },
            function with { Intrinsic = BoundIntrinsic.ClassTryCreate },
            function with { PropertyName = "Forged" },
            function with { Variables = function.Variables.SetItem(0,
                function.Variables[0] with { Type = PrimitiveType.Int32 }) },
            function with { Body = new BoundBlock([new BoundReturn(null, default)], default) },
        ];
        foreach (var replacement in invalid)
        {
            var program = loaded with { Functions = [replacement] };
            Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(program));
        }
        Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(
            loaded with { RuntimePolicy = BoundRuntimePolicy.CheckedExceptions }));
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateProducer(loaded, ["Action"]));
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(loaded, "not-structured"));
    }
}
