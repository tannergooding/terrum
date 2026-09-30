// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmExecutableCompilerTests
{
    private static BoundProgram Compile(string source, bool library = false)
    {
        var syntax = SyntaxTree.Parse(source);
        var compilation = library ? Compilation.CreateLibrary(syntax) : Compilation.Create(syntax);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    [Theory]
    [InlineData("int32 Main() { return 42; }", 42)]
    [InlineData("int32 Main() { mut int32 x = 10; x = x + 20; x = x ^ 12; return x; }", 18)]
    [InlineData("int32 Main() { int32 x = int32(uint64.MaxValue % uint64(100)); return x; }", 15)]
    [InlineData("int32 Main() { return int32(char16(55296)); }", 55296)]
    public void ScalarExecutableRoundTripsToNativeImageAndJitStatus(string source, int expected)
    {
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "sample");
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.Equal(expected, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
        Assert.Equal(expected, NativeJit.Execute(original, NativeRuntimePolicy.FailFast));
        Assert.NotEmpty(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void LibraryPreservesScalarSignaturesVisibilityAndExportIdentity()
    {
        var source = """
            public bool B(bool value) { return !value; }
            public char16 C(char16 value) { return value; }
            public int32 I(int32 value) { return value + 1; }
            public uint32 U(uint32 value) { return value ^ uint32.MaxValue; }
            public int64 L(int64 value) { return value + int64.MinValue; }
            public uint64 W(uint64 value) { return value - 1; }
            int32 Hidden(int32 value) { return value; }
            """;
        var artifact = TrRmExecutableCompiler.Emit(Compile(source, true), "scalar-library");
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.Equal(-1, program.EntryPoint);
        Assert.Equal(7, program.Functions.Length);
        Assert.Equal(PrimitiveType.Bool, program.Functions[0].ReturnType);
        Assert.Equal(PrimitiveType.Char16, program.Functions[1].Variables[0].Type);
        Assert.Equal(PrimitiveType.UInt64, program.Functions[5].ReturnType);
        Assert.NotEmpty(NativeEmitter.EmitLibrary(program,
            ["B", "C", "I", "U", "L", "W"], NativeRuntimePolicy.FailFast));
        Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(program,
            ["Hidden"], NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData("int32 Main() { return Helper(); } int32 Helper() { return 42; }")]
    [InlineData("int32 Main() { mut int32 x = 1; x += Helper(); return x; } int32 Helper() { return 41; }")]
    public void UnsupportedConstructsFailExplicitly(string source)
        => Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(Compile(source), "unsupported"));

    [Fact]
    public void NoThrowEffectRoundTripsWithoutFabricatedProof()
    {
        var source = "[NoThrow] int32 Main() { return 42; }";
        var program = TrRmExecutableCompiler.Load(
            TrRmExecutableCompiler.Emit(Compile(source), "no-throw"));
        Assert.True(program.Functions[0].IsNoThrow);
    }

    [Theory]
    [InlineData("int32 Main() { return 1 << 2; }", 4)]
    [InlineData("int32 Main() { return int32.MaxValue >>> 1; }", 1073741823)]
    [InlineData("int32 Main() { mut int32 x = 20; int32 old = x; x = x + 2; return old + x; }", 42)]
    [InlineData("int32 Main() { return int32.MinValue % -1; }", 0)]
    [InlineData("int32 Main() { return int32(int64.MinValue % -1); }", 0)]
    public void FoldedConstantsAndRetainedScalarOperationsMatchBothExistingPolicies(string source, int expected)
    {
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var compilation = Compilation.Create(SyntaxTree.Parse(source), policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(expected, NativeJit.Execute(program, (NativeRuntimePolicy)policy));
            if (policy == BoundRuntimePolicy.FailFast)
            {
                Assert.Equal(expected, NativeJit.Execute(
                    TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(program, "policies")),
                    NativeRuntimePolicy.FailFast));
            }
            else
            {
                Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(program, "policies"));
            }
        }
    }

    [Fact]
    public void ScalarLibraryMatchesDirectNativeBytesAndIndependentExportCalls()
    {
        const string source = """
            public bool B(bool value) { bool result = !value; return result; }
            public char16 C(char16 value) { return value; }
            public int32 I(int32 left, int32 right) { int32 result = left + right; return result; }
            public uint32 U(uint32 left, uint32 right) { uint32 result = left ^ right; return result; }
            public int64 L(int64 left, int64 right) { int64 result = left % right; return result; }
            public uint64 W(uint64 left, uint64 right) { uint64 result = left / right; return result; }
            public void V() { }
            """;
        var original = Compile(source, true);
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(original, "native-parity"));
        string[] names = ["B", "C", "I", "U", "L", "W", "V"];
        var expected = NativeEmitter.EmitLibrary(original, names, NativeRuntimePolicy.FailFast);
        var actual = NativeEmitter.EmitLibrary(loaded, names, NativeRuntimePolicy.FailFast);
        Assert.Equal(expected, actual);
        var path = Path.Combine(Directory.GetCurrentDirectory(), $"trrm-scalar-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(path, actual);
            var handle = NativeLibrary.Load(path);
            try
            {
                var boolean = Marshal.GetDelegateForFunctionPointer<BoolIdentity>(NativeLibrary.GetExport(handle, "B"));
                var character = Marshal.GetDelegateForFunctionPointer<CharIdentity>(NativeLibrary.GetExport(handle, "C"));
                var signed = Marshal.GetDelegateForFunctionPointer<IntBinary>(NativeLibrary.GetExport(handle, "I"));
                var unsigned = Marshal.GetDelegateForFunctionPointer<UIntBinary>(NativeLibrary.GetExport(handle, "U"));
                var wideSigned = Marshal.GetDelegateForFunctionPointer<LongBinary>(NativeLibrary.GetExport(handle, "L"));
                var wideUnsigned = Marshal.GetDelegateForFunctionPointer<ULongBinary>(NativeLibrary.GetExport(handle, "W"));
                var noResult = Marshal.GetDelegateForFunctionPointer<VoidAction>(NativeLibrary.GetExport(handle, "V"));
                Assert.Equal((byte)1, boolean(0));
                Assert.Equal((byte)0, boolean(1));
                Assert.Equal((ushort)0xD800, character(0xD800));
                Assert.Equal(42, signed(20, 22));
                Assert.Equal(uint.MaxValue, unsigned(0, uint.MaxValue));
                Assert.Equal(0, wideSigned(long.MinValue, -1));
                Assert.Equal(ulong.MaxValue, wideUnsigned(ulong.MaxValue, 1));
                noResult();
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate byte BoolIdentity(byte value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate ushort CharIdentity(ushort value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int IntBinary(int left, int right);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint UIntBinary(uint left, uint right);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate long LongBinary(long left, long right);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate ulong ULongBinary(ulong left, ulong right);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VoidAction();

    [Theory]
    [InlineData("public void V() { }")]
    [InlineData("public void V() { return; }")]
    public void VoidBodyHasOneTerminatorAfterBinderNormalization(string source)
    {
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(Compile(source, true), "void"));
        Assert.Single(loaded.Functions[0].Body.Statements);
        Assert.Null(Assert.IsType<BoundReturn>(loaded.Functions[0].Body.Statements[0]).Value);
    }

    [Fact]
    public void RequiredSourceProofsAreDischargedBeforeEmission()
    {
        const string source = """
            [ConstEval] int32 Identity(int32 value) { return value; }
            int32 Main() { static require true; return const Identity(42); }
            """;
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(Compile(source), "proven"));
        Assert.Equal(42, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void MaterializedValueLimitRejectsWithoutDroppingOperations()
    {
        var source = "int32 Main() { mut int32 x = 0; "
            + string.Concat(Enumerable.Repeat("x = x + 1; ", 128)) + "return x; }";
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(Compile(source), "too-many-values"));
    }

    [Fact]
    public void BodyLocalWidthAndInstructionReferenceWidthWidenIndependently()
    {
        var source = new StringBuilder("int32 Main() { mut int32 x = 0; ");
        for (var i = 0; i < 127; i++)
        {
            source.Append("x = x + 1; ");
        }
        source.Append("x = +x; return x; }");
        var artifact = TrRmExecutableCompiler.Emit(Compile(source.ToString()), "wide-values");
        var index = new TrRmArtifactIndex(artifact);
        Assert.True(index.TryGetSection(0x20, out var bodyBytes, out var count));
        var bodies = new TrRmBodyTableReader(bodyBytes, count);
        Assert.Equal((byte)0x05, bodies.GetRow(1)[0]);
        Assert.Equal(127, NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void TamperedPolicyAndUnknownSectionsNeverBecomeExecutable()
    {
        var bytes = TrRmExecutableCompiler.Emit(Compile("int32 Main() { return 42; }"), "tamper");
        var index = new TrRmArtifactIndex(bytes);
        for (var i = 0; i < index.SectionCount; i++)
        {
            index.GetSection(i, out var kind, out _, out _, out _);
            if (kind != 1)
            {
                continue;
            }
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(32 + i * 16 + 8));
            bytes[offset + 8] = 1;
            Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(bytes));
            bytes[offset + 8] = 0;
            bytes[offset + 4] = 0;
            Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(bytes));
            return;
        }
        throw new InvalidOperationException("Module section missing.");
    }
}
