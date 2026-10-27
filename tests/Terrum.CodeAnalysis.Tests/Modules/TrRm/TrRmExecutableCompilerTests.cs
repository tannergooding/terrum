// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Tests.Semantics;
using Terrum.CodeAnalysis.Tests.Semantics.Generics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmExecutableCompilerTests
{
    private const uint ScalarInitOutputInitialRuleRevision = 1;
    private const uint ScalarInitOutputRuleRevision = 8;
    public static IEnumerable<object[]> StandardIntegerOperationCases
    {
        get
        {
            byte[] typeCodes = [0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F];
            byte[] operationCodes = [0x20, 0x21, 0x22, 0x23, 0x24, 0x25];
            foreach (var typeCode in typeCodes)
            {
                foreach (var operationCode in operationCodes)
                {
                    yield return [typeCode, operationCode];
                }
            }
        }
    }

    private static readonly byte[] s_nonFloatingBuiltinTypeCodes =
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0x0B, 0x0C, 0x0D, 0x0E];
    private const int NonFloatingBuiltinTypeSpecCount = 14;
    private const int FirstOptionalTypeSpecRow = NonFloatingBuiltinTypeSpecCount + 1;
    private const int Float32TypeSpecRow = NonFloatingBuiltinTypeSpecCount + 1;
    private const int OptionalFloat32TypeSpecRow = Float32TypeSpecRow + 1;

    private static BoundProgram Compile(string source, bool library = false)
    {
        var syntax = SyntaxTree.Parse(source);
        var compilation = library ? Compilation.CreateLibrary(syntax) : Compilation.Create(syntax);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
    }
    [TestCase("int32 Main() { return 42; }",42)]
    [TestCase("int32 Main() { mut int32 x = 10; x = x + 20; x = x ^ 12; return x; }",18)]
    [TestCase("int32 Main() { int32 x = int32(uint64.MaxValue % uint64(100)); return x; }",15)]
    [TestCase("int32 Main() { return int32(char16(55296)); }",55296)]
    public void ScalarExecutableRoundTripsToNativeImageAndJitStatus(string source, int expected)
    {
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "sample");
        TrRmStructuralMetadataValidator.ValidateCompactLinear(artifact);
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void ExecutesStandardRegisterScalarReturnsWithoutDevelopmentFallback()
    {
        foreach (var (register, expected) in new (byte Register, int Expected)[] {
            (0x0E, 0),
            (0x0F, -1),
        })
        {
            var artifact = TrRmRegisterBodyTestData.StandardInt32EntryArtifact(register);
            var loaded = TrRmExecutableCompiler.LoadStandardScalarReturn(artifact);
            var image = NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast);

            Assert.That(image.Length, Is.GreaterThan(2));
            Assert.That(image[0], Is.EqualTo((byte)'M'));
            Assert.That(image[1], Is.EqualTo((byte)'Z'));
            Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast),
                Is.EqualTo(expected));
        }

        var developmentArtifact = TrRmExecutableCompiler.Emit(
            Compile("int32 Main() { return 0; }"), "development-profile");
        var exception = Assert.Catch<Exception>(() =>
            TrRmExecutableCompiler.LoadStandardScalarReturn(developmentArtifact));
        Assert.That(exception, Is.InstanceOf<InvalidDataException>()
            .Or.InstanceOf<NotSupportedException>());
    }

    [TestCase((byte)0x14)]
    [TestCase((byte)0x15)]
    public async Task StandardRegisterBodyExecutesFloatingRemainder(byte typeCode)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var program = TrRmExecutableCompiler.LoadStandardFloatingScalarFunction(
            TrRmRegisterBodyTestData.StandardFloatingRemainderLibraryArtifact(typeCode));
        var image = NativeEmitter.EmitLibrary(
            program, ["Remainder"], NativeRuntimePolicy.FailFast);
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-register-remainder-{Guid.NewGuid():N}.dll");
        IntPtr handle = IntPtr.Zero;
        try
        {
            File.WriteAllBytes(path, image);
            handle = NativeLibrary.Load(path);
            if (typeCode == 0x14)
            {
                var remainder = Marshal.GetDelegateForFunctionPointer<Float32Binary>(
                    NativeLibrary.GetExport(handle, "Remainder"));
                Assert.That(SingleBits(remainder(5.5F, 2.0F)),
                    Is.EqualTo(SingleBits(1.5F)));
                Assert.That(SingleBits(remainder(-5.5F, 2.0F)),
                    Is.EqualTo(SingleBits(-1.5F)));
                Assert.That(SingleBits(remainder(5.5F, -2.0F)),
                    Is.EqualTo(SingleBits(1.5F)));
                Assert.That(SingleBits(remainder(-0.0F, 2.0F)),
                    Is.EqualTo(unchecked((int)0x80000000)));
                Assert.That(SingleBits(remainder(5.5F, float.PositiveInfinity)),
                    Is.EqualTo(SingleBits(5.5F)));
                Assert.That(float.IsNaN(remainder(float.PositiveInfinity, 2.0F)), Is.True);
                Assert.That(float.IsNaN(remainder(5.5F, 0.0F)), Is.True);
            }
            else
            {
                var remainder = Marshal.GetDelegateForFunctionPointer<Float64Binary>(
                    NativeLibrary.GetExport(handle, "Remainder"));
                Assert.That(DoubleBits(remainder(5.5, 2.0)), Is.EqualTo(DoubleBits(1.5)));
                Assert.That(DoubleBits(remainder(-5.5, 2.0)), Is.EqualTo(DoubleBits(-1.5)));
                Assert.That(DoubleBits(remainder(5.5, -2.0)), Is.EqualTo(DoubleBits(1.5)));
                Assert.That(DoubleBits(remainder(-0.0, 2.0)), Is.EqualTo(long.MinValue));
                Assert.That(DoubleBits(remainder(5.5, double.PositiveInfinity)),
                    Is.EqualTo(DoubleBits(5.5)));
                Assert.That(double.IsNaN(remainder(double.PositiveInfinity, 2.0)), Is.True);
                Assert.That(double.IsNaN(remainder(5.5, 0.0)), Is.True);
            }
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
            }
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }

    [TestCase((byte)0x14, (byte)0x0E)]
    [TestCase((byte)0x14, (byte)0x0F)]
    [TestCase((byte)0x15, (byte)0x0E)]
    [TestCase((byte)0x15, (byte)0x0F)]
    public async Task StandardFloatingRemainderUsesTypedSpecialSourceRegisters(
        byte typeCode, byte sourceRegister)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var program = TrRmExecutableCompiler.LoadStandardFloatingScalarFunction(
            TrRmRegisterBodyTestData.StandardFloatingRemainderLibraryArtifact(
                typeCode, binarySourceRegister: sourceRegister));
        var image = NativeEmitter.EmitLibrary(
            program, ["Remainder"], NativeRuntimePolicy.FailFast);
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-register-remainder-special-{Guid.NewGuid():N}.dll");
        IntPtr handle = IntPtr.Zero;
        try
        {
            File.WriteAllBytes(path, image);
            handle = NativeLibrary.Load(path);
            if (typeCode == 0x14)
            {
                var remainder = Marshal.GetDelegateForFunctionPointer<Float32Binary>(
                    NativeLibrary.GetExport(handle, "Remainder"));
                Assert.That(float.IsNaN(remainder(5.5F, 2.0F)), Is.True);
            }
            else
            {
                var remainder = Marshal.GetDelegateForFunctionPointer<Float64Binary>(
                    NativeLibrary.GetExport(handle, "Remainder"));
                Assert.That(double.IsNaN(remainder(5.5, 2.0)), Is.True);
            }
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
            }
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }

    [TestCase((byte)0x14, (byte)0x26)]
    [TestCase((byte)0x14, (byte)0x27)]
    [TestCase((byte)0x14, (byte)0x28)]
    [TestCase((byte)0x14, (byte)0x29)]
    [TestCase((byte)0x14, (byte)0x2A)]
    [TestCase((byte)0x15, (byte)0x26)]
    [TestCase((byte)0x15, (byte)0x27)]
    [TestCase((byte)0x15, (byte)0x28)]
    [TestCase((byte)0x15, (byte)0x29)]
    [TestCase((byte)0x15, (byte)0x2A)]
    public async Task StandardRegisterBodyExecutesAssignedFloatingArithmetic(
        byte typeCode, byte operationCode)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var program = TrRmExecutableCompiler.LoadStandardFloatingScalarFunction(
            TrRmRegisterBodyTestData.StandardFloatingRemainderLibraryArtifact(
                typeCode, operationCode, "Compute"));
        var image = NativeEmitter.EmitLibrary(
            program, ["Compute"], NativeRuntimePolicy.FailFast);
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-register-float-{Guid.NewGuid():N}.dll");
        IntPtr handle = IntPtr.Zero;
        try
        {
            File.WriteAllBytes(path, image);
            handle = NativeLibrary.Load(path);
            var expected = operationCode switch {
                0x26 => -1.5,
                0x27 => 3.5,
                0x28 => -0.5,
                0x29 => 3.0,
                0x2A => 0.75,
                _ => throw new ArgumentOutOfRangeException(nameof(operationCode)),
            };
            if (typeCode == 0x14)
            {
                var operation = Marshal.GetDelegateForFunctionPointer<Float32Binary>(
                    NativeLibrary.GetExport(handle, "Compute"));
                Assert.That(SingleBits(operation(1.5F, 2.0F)),
                    Is.EqualTo(SingleBits((float)expected)));
            }
            else
            {
                var operation = Marshal.GetDelegateForFunctionPointer<Float64Binary>(
                    NativeLibrary.GetExport(handle, "Compute"));
                Assert.That(DoubleBits(operation(1.5, 2.0)), Is.EqualTo(DoubleBits(expected)));
            }
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
            }
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }

    [Test]
    public void StandardFloatingRemainderRejectsInvalidFormalAndOperandTypes()
    {
        var mismatchedTypes = TrRmRegisterBodyTestData.StandardFloatingRemainderLibraryArtifact(
            0x14, secondParameterTypeCode: 0x15);
        TrRmStructuralMetadataValidator.Validate(mismatchedTypes);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandardFloatingScalarFunction(mismatchedTypes));

        var invalidFormal = TrRmRegisterBodyTestData.StandardFloatingRemainderLibraryArtifact(
            0x14, firstFormalOrdinal: 2);
        TrRmStructuralMetadataValidator.Validate(invalidFormal);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandardFloatingScalarFunction(invalidFormal));
    }

    [TestCase((byte)0x14, (byte)0x26)]
    [TestCase((byte)0x14, (byte)0x27)]
    [TestCase((byte)0x14, (byte)0x28)]
    [TestCase((byte)0x14, (byte)0x29)]
    [TestCase((byte)0x14, (byte)0x2A)]
    [TestCase((byte)0x14, (byte)0x2B)]
    [TestCase((byte)0x15, (byte)0x26)]
    [TestCase((byte)0x15, (byte)0x27)]
    [TestCase((byte)0x15, (byte)0x28)]
    [TestCase((byte)0x15, (byte)0x29)]
    [TestCase((byte)0x15, (byte)0x2A)]
    [TestCase((byte)0x15, (byte)0x2B)]
    public void StandardFloatingScalarWriterRoundTripsAssignedOperations(
        byte typeCode, byte operationCode)
    {
        var type = typeCode == 0x14 ? "float32" : "float64";
        var operatorText = operationCode == 0x26 ? string.Empty : operationCode switch
        {
            0x27 => "+",
            0x28 => "-",
            0x29 => "*",
            0x2A => "/",
            0x2B => "%",
            _ => throw new ArgumentOutOfRangeException(nameof(operationCode)),
        };
        var reversed = operationCode is 0x28 or 0x2A or 0x2B;
        var expression = operationCode == 0x26
            ? "-right"
            : reversed ? $"right {operatorText} left" : $"left {operatorText} right";
        var source = $"public {type} Compute({type} left, {type} right) {{ return {expression}; }}";
        var program = TrRmExecutableCompiler.LoadStandardFloatingScalarFunction(
            TrRmExecutableCompiler.EmitStandardFloatingScalarLibrary(
                Compile(source, library: true), "standard-floating"));
        var operation = ((BoundLocal)program.Functions[0].Body.Statements[2]).Initializer;

        if (operationCode == 0x26)
        {
            Assert.That(operation, Is.TypeOf<BoundUnary>());
            var unary = (BoundUnary)operation;
            Assert.That(unary.Operator, Is.EqualTo(TokenKind.Minus));
            Assert.That(((BoundRead)unary.Operand).Variable, Is.EqualTo(3));
        }
        else
        {
            Assert.That(operation, Is.TypeOf<BoundBinary>());
            var binary = (BoundBinary)operation;
            Assert.That(binary.Operator, Is.EqualTo(operationCode switch
            {
                0x27 => TokenKind.Plus,
                0x28 => TokenKind.Minus,
                0x29 => TokenKind.Star,
                0x2A => TokenKind.Slash,
                _ => TokenKind.Percent,
            }));
            Assert.That(((BoundRead)binary.Left).Variable, Is.EqualTo(reversed ? 3 : 2));
            Assert.That(((BoundRead)binary.Right).Variable, Is.EqualTo(reversed ? 2 : 3));
        }
    }

    [TestCase((byte)0x14)]
    [TestCase((byte)0x15)]
    public async Task StandardFloatingScalarWriterArtifactExecutesRemainder(byte typeCode)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var type = typeCode == 0x14 ? "float32" : "float64";
        var source = $"public {type} Remainder({type} left, {type} right) {{ return left % right; }}";
        var artifact = TrRmExecutableCompiler.EmitStandardFloatingScalarLibrary(
            Compile(source, library: true), "standard-floating-remainder");
        var program = TrRmExecutableCompiler.LoadStandardFloatingScalarFunction(artifact);
        var image = NativeEmitter.EmitLibrary(
            program, ["Remainder"], NativeRuntimePolicy.FailFast);
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-register-remainder-writer-{Guid.NewGuid():N}.dll");
        IntPtr handle = IntPtr.Zero;
        try
        {
            File.WriteAllBytes(path, image);
            handle = NativeLibrary.Load(path);
            if (typeCode == 0x14)
            {
                var remainder = Marshal.GetDelegateForFunctionPointer<Float32Binary>(
                    NativeLibrary.GetExport(handle, "Remainder"));
                Assert.That(SingleBits(remainder(5.5F, 2.0F)),
                    Is.EqualTo(SingleBits(1.5F)));
            }
            else
            {
                var remainder = Marshal.GetDelegateForFunctionPointer<Float64Binary>(
                    NativeLibrary.GetExport(handle, "Remainder"));
                Assert.That(DoubleBits(remainder(5.5, 2.0)),
                    Is.EqualTo(DoubleBits(1.5)));
            }
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
            }
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }

    [TestCase((byte)0x20)]
    [TestCase((byte)0x21)]
    [TestCase((byte)0x22)]
    [TestCase((byte)0x23)]
    [TestCase((byte)0x24)]
    [TestCase((byte)0x25)]
    public void StandardIntegerScalarWriterRoundTripsAssignedOperations(byte operationCode)
    {
        var operatorText = operationCode == 0x20 ? string.Empty : operationCode switch
        {
            0x21 => "+",
            0x22 => "-",
            0x23 => "*",
            0x24 => "/",
            0x25 => "%",
            _ => throw new ArgumentOutOfRangeException(nameof(operationCode)),
        };
        var reversed = operationCode is 0x22 or 0x24 or 0x25;
        var expression = operationCode == 0x20
            ? "-right"
            : reversed ? $"right {operatorText} left" : $"left {operatorText} right";
        var source = $"public int32 Compute(int32 left, int32 right) {{ return {expression}; }}";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions { SourceArithmeticProfile = SourceArithmeticProfile.Release });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandardIntegerScalarLibrary(
            compilation.Program!, "standard-integer");
        var program = TrRmExecutableCompiler.LoadStandardIntegerScalarFunction(artifact);
        var operation = ((BoundLocal)program.Functions[0].Body.Statements[2]).Initializer;

        if (operationCode == 0x20)
        {
            Assert.That(operation, Is.TypeOf<BoundUnary>());
            var unary = (BoundUnary)operation;
            Assert.That(unary.Operator, Is.EqualTo(TokenKind.Minus));
            Assert.That(unary.OverflowBehavior, Is.EqualTo(BoundIntegerOverflowBehavior.Wrapping));
            Assert.That(((BoundRead)unary.Operand).Variable, Is.EqualTo(3));
            return;
        }

        Assert.That(operation, Is.TypeOf<BoundBinary>());
        var binary = (BoundBinary)operation;
        Assert.That(binary.Operator, Is.EqualTo(operationCode switch
        {
            0x21 => TokenKind.Plus,
            0x22 => TokenKind.Minus,
            0x23 => TokenKind.Star,
            0x24 => TokenKind.Slash,
            _ => TokenKind.Percent,
        }));
        Assert.That(binary.OverflowBehavior, Is.EqualTo(operationCode is >= 0x21 and <= 0x23
            ? BoundIntegerOverflowBehavior.Wrapping
            : BoundIntegerOverflowBehavior.Checked));
        Assert.That(((BoundRead)binary.Left).Variable, Is.EqualTo(reversed ? 3 : 2));
        Assert.That(((BoundRead)binary.Right).Variable, Is.EqualTo(reversed ? 2 : 3));
    }

    [Test]
    public async Task StandardIntegerScalarWriterArtifactExecutesRemainder()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string source = """
            public int32 Remainder(int32 left, int32 right) { return left % right; }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandardIntegerScalarLibrary(
            compilation.Program!, "standard-integer-remainder");
        var program = TrRmExecutableCompiler.LoadStandardIntegerScalarFunction(artifact);
        var image = NativeEmitter.EmitLibrary(
            program, ["Remainder"], NativeRuntimePolicy.FailFast);
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-register-int-remainder-writer-{Guid.NewGuid():N}.dll");
        IntPtr handle = IntPtr.Zero;
        try
        {
            File.WriteAllBytes(path, image);
            handle = NativeLibrary.Load(path);
            var remainder = Marshal.GetDelegateForFunctionPointer<Int32Binary>(
                NativeLibrary.GetExport(handle, "Remainder"));
            Assert.That(remainder(-7, 3), Is.EqualTo(-1));
            Assert.That(remainder(int.MinValue, -1), Is.EqualTo(0));
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
            }
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }

    [TestCaseSource(nameof(StandardIntegerOperationCases))]
    public void StandardIntegerScalarReaderReconstructsAssignedOperations(
        byte typeCode, byte operationCode)
    {
        var artifact = TrRmRegisterBodyTestData.StandardIntegerOperationLibraryArtifact(
            typeCode, operationCode);
        var program = TrRmExecutableCompiler.LoadStandardIntegerScalarFunction(artifact);
        var expectedType = typeCode switch
        {
            0x06 => PrimitiveType.Int8,
            0x07 => PrimitiveType.Int16,
            0x08 => PrimitiveType.Int32,
            0x09 => PrimitiveType.Int64,
            0x0A => PrimitiveType.Int128,
            0x0B => PrimitiveType.UInt8,
            0x0C => PrimitiveType.UInt16,
            0x0D => PrimitiveType.UInt32,
            0x0E => PrimitiveType.UInt64,
            _ => PrimitiveType.UInt128,
        };
        Assert.That(program.Functions[0].ReturnType, Is.EqualTo(expectedType));
        var operation = ((BoundLocal)program.Functions[0].Body.Statements[2]).Initializer;

        if (operationCode == 0x20)
        {
            Assert.That(operation, Is.TypeOf<BoundUnary>());
            var unary = (BoundUnary)operation;
            Assert.That(unary.Operator, Is.EqualTo(TokenKind.Minus));
            Assert.That(unary.OverflowBehavior, Is.EqualTo(BoundIntegerOverflowBehavior.Wrapping));
            Assert.That(((BoundRead)unary.Operand).Variable, Is.EqualTo(2));
            return;
        }

        Assert.That(operation, Is.TypeOf<BoundBinary>());
        var binary = (BoundBinary)operation;
        Assert.That(binary.Operator, Is.EqualTo(operationCode switch
        {
            0x21 => TokenKind.Plus,
            0x22 => TokenKind.Minus,
            0x23 => TokenKind.Star,
            0x24 => TokenKind.Slash,
            _ => TokenKind.Percent,
        }));
        Assert.That(binary.OverflowBehavior, Is.EqualTo(operationCode is >= 0x21 and <= 0x23
            ? BoundIntegerOverflowBehavior.Wrapping
            : BoundIntegerOverflowBehavior.Checked));
        Assert.That(((BoundRead)binary.Left).Variable, Is.EqualTo(2));
        Assert.That(((BoundRead)binary.Right).Variable, Is.EqualTo(3));
    }

    [Test]
    public void StandardIntegerScalarReaderRejectsMismatchedParameterTypes()
    {
        var artifact = TrRmRegisterBodyTestData.StandardIntegerOperationLibraryArtifact(
            0x08, 0x21, secondParameterTypeCode: 0x09);
        TrRmStructuralMetadataValidator.Validate(artifact);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandardIntegerScalarFunction(artifact));
    }

    [Test]
    public void StandardIntegerScalarReaderPreservesTypedR15ForUInt128()
    {
        var artifact = TrRmRegisterBodyTestData.StandardIntegerOperationLibraryArtifact(
            0x0F, 0x20, unarySourceRegister: 0x0F);
        var program = TrRmExecutableCompiler.LoadStandardIntegerScalarFunction(artifact);
        var unary = (BoundUnary)((BoundLocal)program.Functions[0].Body.Statements[2]).Initializer;

        Assert.That(unary.Operand, Is.TypeOf<BoundConstant>());
        var constant = (BoundConstant)unary.Operand;
        Assert.That(constant.Type, Is.EqualTo(PrimitiveType.UInt128));
        Assert.That(constant.Bits.Low, Is.EqualTo(ulong.MaxValue));
        Assert.That(constant.Bits.High, Is.EqualTo(ulong.MaxValue));
    }

    [TestCase((byte)0x20, int.MinValue, 0, int.MinValue)]
    [TestCase((byte)0x21, int.MaxValue, 1, int.MinValue)]
    [TestCase((byte)0x22, int.MinValue, 1, int.MaxValue)]
    [TestCase((byte)0x23, int.MaxValue, 2, -2)]
    [TestCase((byte)0x24, 7, 2, 3)]
    [TestCase((byte)0x25, int.MinValue, -1, 0)]
    public async Task StandardIntegerRegisterOperationsExecute(
        byte operationCode, int left, int right, int expected)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var artifact = TrRmRegisterBodyTestData.StandardIntegerOperationLibraryArtifact(
            0x08, operationCode, methodName: "Compute");
        var program = TrRmExecutableCompiler.LoadStandardIntegerScalarFunction(artifact);
        var image = NativeEmitter.EmitLibrary(
            program, ["Compute"], NativeRuntimePolicy.FailFast);
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-register-int-{Guid.NewGuid():N}.dll");
        IntPtr handle = IntPtr.Zero;
        try
        {
            File.WriteAllBytes(path, image);
            handle = NativeLibrary.Load(path);
            var operation = Marshal.GetDelegateForFunctionPointer<Int32Binary>(
                NativeLibrary.GetExport(handle, "Compute"));
            Assert.That(operation(left, right), Is.EqualTo(expected));
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
            }
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }

    [TestCase("""
        int32 Fill(int32 prefix, init ref<int32> result, int32 suffix) {
            result = prefix + suffix;
            return suffix;
        }
        int32 Main() {
            init int32 result;
            int32 status = Fill(20, init result, 2);
            return result + status;
        }
        """,24)]
    [TestCase("""
        void Fill(init ref<int32> first, init ref<int32> second) {
            first = 40;
            second = 2;
        }
        int32 Main() {
            init int32 first;
            init int32 second;
            Fill(init first, init second);
            return first + second;
        }
        """,42)]
    [TestCase("""
        void Store(bool flag, char16 character, uint32 unsigned32, int64 signed64,
            uint64 unsigned64, float32 single, float64 wide, init ref<int32> result) {
            result = 42;
        }
        bool GetFlag() { return true; }
        char16 GetCharacter() { return char16(65); }
        uint32 GetUnsigned32() { return uint32(3); }
        int64 GetSigned64() { return int64(4); }
        uint64 GetUnsigned64() { return uint64(5); }
        float32 GetSingle() { return float32(1.5); }
        float64 GetWide() { return float64(2.5); }
        int32 Main() {
            init int32 result;
            Store(GetFlag(), GetCharacter(), GetUnsigned32(), GetSigned64(),
                GetUnsigned64(), GetSingle(), GetWide(), init result);
            return result;
        }
        """,42)]
    public void InitOutputCallsRoundTripWithFeatureAndPublishOnNormalReturn(string source, int expected)
    {
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "init-output");
        var index = TrRmArtifactIndex.ForDevelopment(artifact);

        Assert.That(index.TryGetSection(4, out var featureBytes, out var featureCount), Is.True);
        Assert.That(featureCount, Is.EqualTo(1));
        var features = new TrRmFeatureTableReader(featureBytes, featureCount);
        Assert.That(features.GetFeature(1),
            Is.EqualTo(new TrRmFeatureRow(1, ScalarInitOutputRuleRevision)));

        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));

        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }
    [TestCase("TrueValue() == TrueValue()",42)]
    [TestCase("TrueValue() != FalseValue()",42)]
    [TestCase("FalseValue() == TrueValue()",0)]
    [TestCase("CharacterA() == CharacterA()",42)]
    [TestCase("CharacterA() != CharacterB()",42)]
    [TestCase("Signed32One() == Signed32One()",42)]
    [TestCase("Signed32One() != Signed32Two()",42)]
    [TestCase("Signed32One() < Signed32Two()",42)]
    [TestCase("Signed32Two() <= Signed32Two()",42)]
    [TestCase("Signed32Two() > Signed32One()",42)]
    [TestCase("Signed32Two() >= Signed32Two()",42)]
    [TestCase("Signed32Two() < Signed32One()",0)]
    [TestCase("Unsigned32One() == Unsigned32One()",42)]
    [TestCase("Unsigned32One() != Unsigned32Two()",42)]
    [TestCase("Unsigned32One() < Unsigned32Two()",42)]
    [TestCase("Unsigned32Two() >= Unsigned32Two()",42)]
    [TestCase("Signed64Two() > Signed64One()",42)]
    [TestCase("Signed64One() <= Signed64One()",42)]
    [TestCase("Unsigned64One() == Unsigned64One()",42)]
    [TestCase("Unsigned64One() != Unsigned64Two()",42)]
    [TestCase("Unsigned64Two() > Unsigned64One()",42)]
    [TestCase("(Signed32One() & Signed32Three()) == Signed32One()",42)]
    [TestCase("(Signed32One() | Signed32Two()) == Signed32Three()",42)]
    [TestCase("(Signed32One() ^ Signed32Two()) == Signed32Three()",42)]
    [TestCase("(Unsigned32One() & Unsigned32Three()) == Unsigned32One()",42)]
    [TestCase("(Unsigned32One() | Unsigned32Two()) == Unsigned32Three()",42)]
    [TestCase("(Unsigned32One() ^ Unsigned32Two()) == Unsigned32Three()",42)]
    [TestCase("(Signed64One() & Signed64Three()) == Signed64One()",42)]
    [TestCase("(Signed64One() | Signed64Two()) == Signed64Three()",42)]
    [TestCase("(Signed64One() ^ Signed64Two()) == Signed64Three()",42)]
    [TestCase("(Unsigned64One() & Unsigned64Three()) == Unsigned64One()",42)]
    [TestCase("(Unsigned64One() | Unsigned64Two()) == Unsigned64Three()",42)]
    [TestCase("(Unsigned64One() ^ Unsigned64Two()) == Unsigned64Three()",42)]
    [TestCase("(Signed32One() + Signed32Two()) == Signed32Three()",42)]
    [TestCase("(Signed32Two() - Signed32One()) == Signed32One()",42)]
    [TestCase("(Signed32Two() * Signed32Two()) == Signed32Four()",42)]
    [TestCase("(Signed32Six() / Signed32Two()) == Signed32Three()",42)]
    [TestCase("(Signed32Five() % Signed32Two()) == Signed32One()",42)]
    [TestCase("(Unsigned32One() + Unsigned32Two()) == Unsigned32Three()",42)]
    [TestCase("(Unsigned32Two() - Unsigned32One()) == Unsigned32One()",42)]
    [TestCase("(Unsigned32Two() * Unsigned32Two()) == Unsigned32Four()",42)]
    [TestCase("(Unsigned32Six() / Unsigned32Two()) == Unsigned32Three()",42)]
    [TestCase("(Unsigned32Five() % Unsigned32Two()) == Unsigned32One()",42)]
    [TestCase("(Signed64One() + Signed64Two()) == Signed64Three()",42)]
    [TestCase("(Signed64Two() - Signed64One()) == Signed64One()",42)]
    [TestCase("(Signed64Two() * Signed64Two()) == Signed64Four()",42)]
    [TestCase("(Signed64Six() / Signed64Two()) == Signed64Three()",42)]
    [TestCase("(Signed64Five() % Signed64Two()) == Signed64One()",42)]
    [TestCase("(Unsigned64One() + Unsigned64Two()) == Unsigned64Three()",42)]
    [TestCase("(Unsigned64Two() - Unsigned64One()) == Unsigned64One()",42)]
    [TestCase("(Unsigned64Two() * Unsigned64Two()) == Unsigned64Four()",42)]
    [TestCase("(Unsigned64Six() / Unsigned64Two()) == Unsigned64Three()",42)]
    [TestCase("(Unsigned64Five() % Unsigned64Two()) == Unsigned64One()",42)]
    [TestCase("(Signed32One() << Signed32Two()) == Signed32Four()",42)]
    [TestCase("(Signed32Four() >> Signed32One()) == Signed32Two()",42)]
    [TestCase("(Signed32One() << Signed32ThirtyOne()) == Signed32Minimum()",42)]
    [TestCase("(Unsigned32One() << Signed32Two()) == Unsigned32Four()",42)]
    [TestCase("(Unsigned32Four() >> Signed32One()) == Unsigned32Two()",42)]
    [TestCase("(Signed64One() << Signed32Two()) == Signed64Four()",42)]
    [TestCase("(Signed64Four() >> Signed32One()) == Signed64Two()",42)]
    [TestCase("(Signed64One() << Signed32SixtyThree()) == Signed64Minimum()",42)]
    [TestCase("(Unsigned64One() << Signed32Two()) == Unsigned64Four()",42)]
    [TestCase("(Unsigned64Four() >>> Signed32One()) == Unsigned64Two()",42)]
    [TestCase("((Unsigned64One() << Signed32SixtyThree()) >>> Signed32One()) == (Unsigned64One() << Signed32SixtyTwo())",42)]
    [TestCase("(Signed64MinusTwo() >> Signed32One()) == Signed64MinusOne()",42)]
    [TestCase("(Signed32NegativeFive() / Signed32Two()) == Signed32MinusTwo()",42)]
    [TestCase("(Signed32NegativeFive() % Signed32Two()) == Signed32MinusOne()",42)]
    [TestCase("(Signed32Minimum() % Signed32MinusOne()) == Signed32Zero()",42)]
    [TestCase("(Signed64Minimum() % Signed64MinusOne()) == Signed64Zero()",42)]
    [TestCase("(!FalseValue()) == TrueValue()",42)]
    [TestCase("(-Signed32One()) == Signed32MinusOne()",42)]
    [TestCase("(+Signed32One()) == Signed32One()",42)]
    [TestCase("(~Signed32One()) == Signed32MinusTwo()",42)]
    [TestCase("(-Signed64One()) == Signed64MinusOne()",42)]
    [TestCase("(+Unsigned32One()) == Unsigned32One()",42)]
    [TestCase("(~Unsigned32One()) == Unsigned32ComplementOne()",42)]
    [TestCase("(+Signed64One()) == Signed64One()",42)]
    [TestCase("(~Signed64One()) == Signed64MinusTwo()",42)]
    [TestCase("(+Unsigned64One()) == Unsigned64One()",42)]
    [TestCase("(~Unsigned64One()) == Unsigned64ComplementOne()",42)]
    [TestCase("(-SingleOne()) == NegativeSingleOne()",42)]
    [TestCase("(-WideOne()) == NegativeWideOne()",42)]
    [TestCase("true",42)]
    [TestCase("false",0)]
    [TestCase("(char16(65) == CharacterA())",42)]
    [TestCase("(int32(-1) == Signed32MinusOne())",42)]
    [TestCase("(uint32(1) == Unsigned32One())",42)]
    [TestCase("(int64(Signed32One()) == Signed64One())",42)]
    [TestCase("(int32(Signed64One()) == Signed32One())",42)]
    [TestCase("(uint32(Signed32One()) == Unsigned32One())",42)]
    [TestCase("(uint64(Unsigned32One()) == Unsigned64One())",42)]
    [TestCase("(int32(CharacterA()) == int32(65))",42)]
    [TestCase("(char16(Signed32SixtyFive()) == CharacterA())",42)]
    [TestCase("(float64(SingleOne()) == WideOne())",42)]
    [TestCase("(float32(WideOne()) == SingleOne())",42)]
    [TestCase("(float64(Signed64One()) == WideOne())",42)]
    [TestCase("(float32(Unsigned32AllBits()) == SingleUInt32MaxRounded())",42)]
    [TestCase("(float64(Unsigned64ComplementOne()) == WideUInt64MaxRounded())",42)]
    [TestCase("(float32(CharacterA()) == float32(65.0))",42)]
    [TestCase("(int32(SingleOneAndHalf()) == Signed32One())",42)]
    [TestCase("(int64(WideOne()) == Signed64One())",42)]
    [TestCase("(uint64(WideNegativeHalf()) == Unsigned64Zero())",42)]
    [TestCase("(uint32(0x7FFFFFFF) == Unsigned32High31())",42)]
    [TestCase("(uint32(0x80000000) == Unsigned32HighBit())",42)]
    [TestCase("(uint32(0xFFFFFFFF) == Unsigned32AllBits())",42)]
    [TestCase("(int64(-1) == Signed64MinusOne())",42)]
    [TestCase("(uint64(1) == Unsigned64One())",42)]
    [TestCase("(float32(-1.0) == NegativeSingleOne())",42)]
    [TestCase("(float64(1.0) == WideOne())",42)]
    [TestCase("(SingleOne() + SingleTwo()) == SingleThree()",42)]
    [TestCase("(SingleTwo() - SingleOne()) == SingleOne()",42)]
    [TestCase("(SingleOne() * SingleTwo()) == SingleTwo()",42)]
    [TestCase("(SingleThree() / SingleTwo()) == SingleOneAndHalf()",42)]
    [TestCase("(WideOne() + WideTwo()) == WideThree()",42)]
    [TestCase("(WideTwo() - WideOne()) == WideOne()",42)]
    [TestCase("(WideOne() * WideTwo()) == WideTwo()",42)]
    [TestCase("(WideThree() / WideTwo()) == WideOneAndHalf()",42)]
    [TestCase("SingleOne() == SingleOne()",42)]
    [TestCase("SingleOne() != SingleTwo()",42)]
    [TestCase("SingleOne() < SingleTwo()",42)]
    [TestCase("SingleOne() <= SingleOne()",42)]
    [TestCase("SingleTwo() > SingleOne()",42)]
    [TestCase("SingleOne() >= SingleOne()",42)]
    [TestCase("WideOne() == WideOne()",42)]
    [TestCase("WideOne() != WideTwo()",42)]
    [TestCase("WideOne() < WideTwo()",42)]
    [TestCase("WideOne() <= WideOne()",42)]
    [TestCase("WideTwo() > WideOne()",42)]
    [TestCase("WideOne() >= WideOne()",42)]
    public void ScalarOperationsRoundTripInOutputBodies(string expression, int expected)
    {
        var source = $$"""
            void Fill(int32 value, init ref<int32> result) { result = value; }
            int32 Observe(bool value) { return value ? 42 : 0; }
            bool TrueValue() { return true; }
            bool FalseValue() { return false; }
            char16 CharacterA() { return char16(65); }
            char16 CharacterB() { return char16(66); }
            int32 Signed32One() { return 1; }
            int32 Signed32Two() { return 2; }
            int32 Signed32Three() { return 3; }
            int32 Signed32Four() { return 4; }
            int32 Signed32Five() { return 5; }
            int32 Signed32Six() { return 6; }
            int32 Signed32SixtyFive() { return 65; }
            int32 Signed32ThirtyOne() { return 31; }
            int32 Signed32SixtyThree() { return 63; }
            int32 Signed32SixtyTwo() { return 62; }
            int32 Signed32Zero() { return 0; }
            int32 Signed32Minimum() { return int32.MinValue; }
            int32 Signed32NegativeFive() { return -5; }
            int32 Signed32MinusTwo() { return -2; }
            int32 Signed32MinusOne() { return -1; }
            uint32 Unsigned32One() { return uint32(1); }
            uint32 Unsigned32Two() { return uint32(2); }
            uint32 Unsigned32Three() { return uint32(3); }
            uint32 Unsigned32Four() { return uint32(4); }
            uint32 Unsigned32Five() { return uint32(5); }
            uint32 Unsigned32Six() { return uint32(6); }
            uint32 Unsigned32High31() { return uint32(0x7FFFFFFF); }
            uint32 Unsigned32HighBit() { return uint32(0x80000000); }
            uint32 Unsigned32AllBits() { return uint32(0xFFFFFFFF); }
            uint32 Unsigned32ComplementOne() { return ~uint32(1); }
            int64 Signed64One() { return int64(1); }
            int64 Signed64Two() { return int64(2); }
            int64 Signed64Three() { return int64(3); }
            int64 Signed64Four() { return int64(4); }
            int64 Signed64Five() { return int64(5); }
            int64 Signed64Six() { return int64(6); }
            int64 Signed64Zero() { return int64(0); }
            int64 Signed64Minimum() { return int64.MinValue; }
            int64 Signed64NegativeFive() { return int64(-5); }
            int64 Signed64MinusTwo() { return int64(-2); }
            int64 Signed64MinusOne() { return int64(-1); }
            uint64 Unsigned64One() { return uint64(1); }
            uint64 Unsigned64Zero() { return uint64(0); }
            uint64 Unsigned64Two() { return uint64(2); }
            uint64 Unsigned64Three() { return uint64(3); }
            uint64 Unsigned64Four() { return uint64(4); }
            uint64 Unsigned64Five() { return uint64(5); }
            uint64 Unsigned64Six() { return uint64(6); }
            uint64 Unsigned64ComplementOne() { return ~uint64(1); }
            float32 SingleOne() { return float32(1.0); }
            float32 SingleTwo() { return float32(2.0); }
            float32 SingleThree() { return float32(3.0); }
            float32 SingleOneAndHalf() { return float32(1.5); }
            float32 SingleUInt32MaxRounded() { return float32(4294967296.0); }
            float32 NegativeSingleOne() { return float32(-1.0); }
            float64 WideOne() { return float64(1.0); }
            float64 WideTwo() { return float64(2.0); }
            float64 WideThree() { return float64(3.0); }
            float64 WideOneAndHalf() { return float64(1.5); }
            float64 WideUInt64MaxRounded() { return float64(18446744073709551616.0); }
            float64 WideNegativeHalf() { return float64(-0.5); }
            float64 NegativeWideOne() { return float64(-1.0); }
            int32 Main() {
                init int32 result;
                Fill(Observe({{expression}}), init result);
                return result;
            }
            """;
        var original = Compile(source);
        var loaded = TrRmExecutableCompiler.Load(
            TrRmExecutableCompiler.Emit(original, "output-comparisons"));

        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }
    [TestCase("bool","true","true")]
    [TestCase("char16","char16(0xD800)","char16(0xD800)")]
    [TestCase("int32","int32.MinValue","int32.MinValue")]
    [TestCase("uint32","uint32.MaxValue","uint32.MaxValue")]
    [TestCase("int64","int64.MinValue","int64.MinValue")]
    [TestCase("uint64","uint64.MaxValue","uint64.MaxValue")]
    [TestCase("float32","float32(1.5)","float32(1.5)")]
    [TestCase("float64","float64(2.5)","float64(2.5)")]
    public void InitOutputPlacesRoundTripWithScalarReferents(
        string type, string value, string expected)
    {
        var source = $$"""
            void Fill({{type}} value, init ref<{{type}}> result) { result = value; }
            int32 Observe({{type}} value) { return value == {{expected}} ? 42 : 0; }
            int32 Main() {
                init {{type}} result;
                Fill({{value}}, init result);
                return Observe(result);
            }
            """;
        var original = Compile(source);
        var loaded = TrRmExecutableCompiler.Load(
            TrRmExecutableCompiler.Emit(original, "scalar-output-referent"));

        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }
    [TestCase("char16","char16(65)","char16(65)")]
    [TestCase("int32","int32.MinValue","int32.MinValue")]
    [TestCase("uint32","uint32.MaxValue","uint32.MaxValue")]
    [TestCase("int64","int64.MinValue","int64.MinValue")]
    [TestCase("uint64","uint64.MaxValue","uint64.MaxValue")]
    [TestCase("float32","float32(1.5)","float32(1.5)")]
    [TestCase("float64","float64(2.5)","float64(2.5)")]
    public void InitOutputCallsRoundTripWithNonBooleanScalarResults(
        string type, string value, string expected)
    {
        var source = $$"""
            {{type}} Fill(init ref<int32> result) {
                result = 42;
                return {{value}};
            }
            int32 Check({{type}} returned, int32 result) {
                return result == 42 && returned == {{expected}} ? 42 : 0;
            }
            int32 Main() {
                init int32 result;
                {{type}} returned = Fill(init result);
                return Check(returned, result);
            }
            """;
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "scalar-output-result");
        var loaded = TrRmExecutableCompiler.Load(artifact);

        Assert.That(ScalarInitOutputRevision(artifact),
            Is.EqualTo(ScalarInitOutputRuleRevision));
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));

        foreach (var revision in new uint[] { 1, 2 })
        {
            var legacyArtifact = SetScalarInitOutputRuleRevision(artifact, revision);
            if (type == "int32")
            {
                var legacy = TrRmExecutableCompiler.Load(legacyArtifact);
                Assert.That(NativeJit.Execute(legacy, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
            }
            else
            {
                Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(legacyArtifact));
            }
        }

        var revisionThreeArtifact = SetScalarInitOutputRuleRevision(artifact, 3);
        var revisionThree = TrRmExecutableCompiler.Load(revisionThreeArtifact);
        Assert.That(NativeJit.Execute(revisionThree, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        var revisionFourArtifact = SetScalarInitOutputRuleRevision(artifact, 4);
        var revisionFour = TrRmExecutableCompiler.Load(revisionFourArtifact);
        Assert.That(NativeJit.Execute(revisionFour, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void InitOutputBooleanResultsRequireRuleRevisionFour()
    {
        var program = Compile("""
            bool Fill(init ref<int32> result) {
                result = 42;
                return true;
            }
            int32 Main() {
                init int32 result;
                bool success = Fill(init result);
                return Status(success);
            }
            int32 Status(bool success) { return success ? 42 : 0; }
            """);

        var artifact = TrRmExecutableCompiler.Emit(program, "boolean-output-result");
        Assert.That(ScalarInitOutputRevision(artifact),
            Is.EqualTo(ScalarInitOutputRuleRevision));
        Assert.That(NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));

        foreach (var revision in new uint[] { 1, 2, 3 })
        {
            var legacyArtifact = SetScalarInitOutputRuleRevision(artifact, revision);
            Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(legacyArtifact));
        }

        var revisionFourArtifact = SetScalarInitOutputRuleRevision(artifact, 4);
        var revisionFour = TrRmExecutableCompiler.Load(revisionFourArtifact);
        Assert.That(NativeJit.Execute(revisionFour, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }
    [TestCase(false,0)]
    [TestCase(true,1)]
    public void InitOutputCallsPreserveBooleanResults(bool publish, int expected)
    {
        var publishText = publish ? "true" : "false";
        var source = $$"""
            bool Fill(bool shouldPublish, init ref<bool> flag,
                init ref<char16> marker, init ref<int32> result,
                init ref<uint32> smallCount, init ref<int64> signedCount,
                init ref<uint64> wideCount, init ref<float32> ratio,
                init ref<float64> amount) {
                flag = false;
                marker = char16(65);
                result = 0;
                smallCount = uint32.MaxValue;
                signedCount = int64.MinValue;
                wideCount = 0;
                ratio = float32(1.5);
                amount = 0.0;
                return shouldPublish;
            }
            int32 Status(bool value) { return value ? 1 : 0; }
            int32 Main() {
                init bool flag;
                init char16 marker;
                init int32 result;
                init uint32 smallCount;
                init int64 signedCount;
                init uint64 wideCount;
                init float32 ratio;
                init float64 amount;
                bool published = Fill({{publishText}}, init flag, init marker,
                    init result, init smallCount, init signedCount, init wideCount,
                    init ratio, init amount);
                return Status(published);
            }
            """;
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "boolean-output-result");
        var loaded = TrRmExecutableCompiler.Load(artifact);

        Assert.That(ScalarInitOutputRevision(artifact),
            Is.EqualTo(ScalarInitOutputRuleRevision));
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));

        var revisionFourArtifact = SetScalarInitOutputRuleRevision(artifact, 4);
        Assert.That(NativeJit.Execute(TrRmExecutableCompiler.Load(revisionFourArtifact),
            NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
    }
    [TestCase(false,0)]
    [TestCase(true,42)]
    public void InitOutputConditionalBooleanCallPublishesOnlyOnTrue(bool publish, int expected)
    {
        var publishText = publish ? "true" : "false";
        var source = $$"""
            bool Fill(bool shouldPublish, init ref<bool> flag,
                init ref<char16> marker, init ref<int32> result,
                init ref<uint32> smallCount, init ref<int64> signedCount,
                init ref<uint64> wideCount, init ref<float32> ratio,
                init ref<float64> amount) {
                flag = true;
                marker = char16(65);
                result = 42;
                smallCount = uint32.MaxValue;
                signedCount = int64.MinValue;
                wideCount = uint64.MaxValue;
                ratio = float32(1.5);
                amount = float64(2.5);
                return shouldPublish;
            }
            int32 Check(bool flag, char16 marker, int32 result, uint32 smallCount,
                int64 signedCount, uint64 wideCount, float32 ratio, float64 amount) {
                return flag && marker == char16(65) && result == 42
                    && smallCount == uint32.MaxValue && signedCount == int64.MinValue
                    && wideCount == uint64.MaxValue && ratio == float32(1.5)
                    && amount == float64(2.5) ? 42 : 1;
            }
            int32 Refill(init ref<int32> result) {
                result = 42;
                return 0;
            }
            int32 Run(bool shouldPublish) {
                init bool flag;
                init char16 marker;
                init int32 result;
                init uint32 smallCount;
                init int64 signedCount;
                init uint64 wideCount;
                init float32 ratio;
                init float64 amount;
                init int32 retained;
                retained = 5;
                if (Fill(shouldPublish, init flag, init marker, init result,
                    init smallCount, init signedCount, init wideCount, init ratio, init amount)) {
                    return Check(flag, marker, result, smallCount, signedCount,
                        wideCount, ratio, amount) + retained - 5;
                } else {
                    return Refill(init result) + result + retained - 47;
                }
            }
            int32 Main() { return Run({{publishText}}); }
            """;
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "conditional-output-result");
        var loaded = TrRmExecutableCompiler.Load(artifact);

        Assert.That(ScalarInitOutputRevision(artifact),
            Is.EqualTo(ScalarInitOutputRuleRevision));
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));

        var revisionFourArtifact = SetScalarInitOutputRuleRevision(artifact, 4);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(revisionFourArtifact));
        var revisionFiveArtifact = SetScalarInitOutputRuleRevision(artifact, 5);
        Assert.That(NativeJit.Execute(
            TrRmExecutableCompiler.Load(revisionFiveArtifact), NativeRuntimePolicy.FailFast), Is.EqualTo(expected));

        var forgedTrueState = SetConditionalInheritedPlaceState(artifact, 1, false);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(forgedTrueState));
        var forgedFalseState = SetConditionalInheritedPlaceState(artifact, 2, true);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(forgedFalseState));
    }
    [TestCase(true,true,true)]
    [TestCase(true,false,true)]
    [TestCase(false,false,true)]
    [TestCase(false,true,true)]
    [TestCase(true,true,false)]
    [TestCase(true,false,false)]
    [TestCase(false,false,false)]
    [TestCase(false,true,false)]
    public void InitOutputConditionalArmsPreserveFailReasonOne(
        bool failOnTrue, bool selected, bool zeroArgumentCall)
    {
        var selectedText = selected ? "true" : "false";
        var signature = zeroArgumentCall ? "()" : "(bool selected)";
        var returnExpression = zeroArgumentCall ? selectedText : "selected";
        var invocation = zeroArgumentCall ? "Select()" : $"Select({selectedText})";
        var trueArm = failOnTrue ? "require false;" : "return initialized;";
        var falseArm = failOnTrue ? "return initialized;" : "require false;";
        var expected = selected == failOnTrue ? unchecked((int)0xE0540003) : 42;
        var source = $$"""
            bool Select{{signature}} { return {{returnExpression}}; }
            int32 Main() {
                init int32 initialized;
                init int32 vacant;
                initialized = 42;
                if ({{invocation}}) {
                    {{trueArm}}
                } else {
                    {{falseArm}}
                }
            }
            """;
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "conditional-output-failure");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(
            SetScalarInitOutputRuleRevision(artifact, 5)));

        var forgedTrueState = SetConditionalInheritedPlaceState(
            artifact, blockIndex: 1, initialized: false);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(forgedTrueState));
        var forgedFalseState = SetConditionalInheritedPlaceState(
            artifact, blockIndex: 2, initialized: true, placeId: 2);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(forgedFalseState));

        if (expected == 42)
        {
            Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
            Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public void InitOutputConditionalDirectConditionInitializesPlaceInEitherArm(bool selected)
    {
        var selection = selected ? "true" : "false";
        var program = Compile($$"""
            int32 Fill(init ref<int32> result) {
                result = 42;
                return 42;
            }
            bool Select() { return {{selection}}; }
            int32 Main() {
                init int32 result;
                if (Select()) {
                    return Fill(init result);
                } else {
                    return Fill(init result);
                }
            }
            """);
        var artifact = TrRmExecutableCompiler.Emit(program, "conditional-output-arms");

        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void InitOutputConditionalNoOutputCallInheritsArgumentCallState()
    {
        var program = Compile("""
            bool Fill(init ref<int32> result) {
                result = 7;
                return false;
            }
            bool Select(bool selected) { return selected; }
            int32 Main() {
                init int32 initialized;
                init int32 vacant;
                initialized = 42;
                if (Select(Fill(init vacant))) {
                    return initialized;
                } else {
                    require false;
                }
            }
            """);
        var artifact = TrRmExecutableCompiler.Emit(program, "conditional-value-call-state");

        _ = TrRmExecutableCompiler.Load(artifact);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(
            SetConditionalInheritedPlaceState(artifact, blockIndex: 1,
                initialized: true, placeId: 2)));
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(
            SetConditionalInheritedPlaceState(artifact, blockIndex: 2,
                initialized: false, placeId: 1)));
    }
    [TestCase("true")]
    [TestCase("stored")]
    [TestCase("Select(true) && true")]
    public void InitOutputConditionalStillRequiresDirectBooleanCall(string condition)
    {
        var program = Compile($$"""
            bool Select(bool value) { return value; }
            int32 Main() {
                init int32 vacant;
                bool stored = Select(true);
                if ({{condition}}) { return 42; }
                else { return 0; }
            }
            """);

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(program, "conditional-nondirect-condition"));
    }

    [Test]
    public void InitOutputConditionalStillRejectsNestedSplits()
    {
        var program = Compile("""
            bool Select(bool value) { return value; }
            int32 Main() {
                init int32 vacant;
                if (Select(true)) {
                    if (Select(false)) { return 42; }
                    else { return 0; }
                } else {
                    return 1;
                }
            }
            """);

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(program, "conditional-nested-split"));
    }

    [Test]
    public void OutputBodyUsesMinimalWidthForLongInstructionStreams()
    {
        var additions = string.Join(" + ", Enumerable.Repeat("seed", 64));
        var source = $$"""
            void Fill(int32 seed, init ref<int32> result) { result = {{additions}}; }
            int32 Main() {
                init int32 result;
                Fill(1, init result);
                return result;
            }
            """;
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "long-output-body");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        var (widthCode, operationBytes) = LongestSingleBlockOperationSpan(artifact);

        Assert.That(widthCode, Is.EqualTo(0));
        Assert.That(operationBytes > byte.MaxValue, Is.True);
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(64));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(64));
    }

    [Test]
    public void StoredBooleanResultDoesNotCorrelateItsOutputDestination()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            bool Fill(bool publish, init ref<int32> result) {
                result = 42;
                return publish;
            }
            int32 Main() {
                init int32 result;
                bool success = Fill(false, init result);
                return success ? result : 0;
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3016"),Is.True);
    }

    [Test]
    public void InitOutputPlacesPreserveMixedTypesAndInterleavedArguments()
    {
        var source = """
            void Fill(float64 wide, init ref<uint32> unsigned, int64 signed,
                init ref<bool> flag, char16 marker, init ref<float32> ratio,
                init ref<char16> code, uint64 count, init ref<int32> result,
                int32 value, init ref<int64> signedWide) {
                unsigned = uint32.MaxValue;
                flag = true;
                ratio = float32(1.5);
                code = marker;
                result = value;
                signedWide = signed;
            }
            int32 Check(uint32 unsigned, bool flag, float32 ratio, char16 code,
                int32 result, int64 signedWide, float64 wide, uint64 count) {
                return unsigned == uint32.MaxValue && flag && ratio == float32(1.5)
                    && code == char16(65) && result == 42 && signedWide == int64(-7)
                    && wide == float64(3.5) && count == uint64(9) ? 42 : 0;
            }
            int32 Main() {
                init uint32 unsigned;
                init bool flag;
                init float32 ratio;
                init char16 code;
                init int32 result;
                init int64 signedWide;
                mut float64 wide = float64(2.5);
                wide = float64(3.5);
                Fill(wide, init unsigned, int64(-7), init flag, char16(65),
                    init ratio, init code, uint64(9), init result, 42, init signedWide);
                return Check(unsigned, flag, ratio, code, result, signedWide, wide, uint64(9));
            }
            """;
        var original = Compile(source);
        var loaded = TrRmExecutableCompiler.Load(
            TrRmExecutableCompiler.Emit(original, "mixed-scalar-output-referents"));

        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void InitOutputRuleRevisionOneRemainsLimitedToInt32Referents()
    {
        const string int32Source = """
            void Fill(int32 value, init ref<int32> result) { result = value; }
            int32 Main() {
                init int32 result;
                Fill(42, init result);
                return result;
            }
            """;
        var legacyArtifact = TrRmExecutableCompiler.Emit(Compile(int32Source), "legacy-output");
        legacyArtifact = SetScalarInitOutputRuleRevision(legacyArtifact,
            ScalarInitOutputInitialRuleRevision);
        var loaded = TrRmExecutableCompiler.Load(legacyArtifact);
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));

        const string boolSource = """
            void Fill(bool value, init ref<bool> result) { result = value; }
            int32 Observe(bool value) { return value ? 42 : 0; }
            int32 Main() {
                init bool result;
                Fill(true, init result);
                return Observe(result);
            }
            """;
        var unsupportedArtifact = TrRmExecutableCompiler.Emit(Compile(boolSource), "legacy-output-bool");
        unsupportedArtifact = SetScalarInitOutputRuleRevision(unsupportedArtifact,
            ScalarInitOutputInitialRuleRevision);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(unsupportedArtifact));
    }

    [Test]
    public void OutputBodyCallsEncodeWideMethodReferenceSeparatelyFromArgumentCount()
    {
        var fillers = string.Join(Environment.NewLine,
            Enumerable.Range(0, 255).Select(static index => $"void Helper{index}() {{ return; }}"));
        var source = $$"""
            {{fillers}}
            void Target(int32 value) { return; }
            int32 Main() {
                init int32 result;
                int32 value = 1;
                Target(value);
                result = 42;
                return result;
            }
            """;
        var loaded = TrRmExecutableCompiler.Load(
            TrRmExecutableCompiler.Emit(Compile(source), "wide-output-method-reference"));

        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void InitOutputParameterRequiresModuleFeatureAssignment()
    {
        var artifact = TrRmExecutableCompiler.Emit(Compile("""
            void Fill(init ref<int32> result) { result = 42; }
            int32 Main() {
                init int32 result;
                Fill(init result);
                return result;
            }
            """), "missing-feature");
        var index = TrRmArtifactIndex.ForDevelopment(artifact);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out _, out _, out _);
            if (kind != 1)
            {
                continue;
            }

            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(
                artifact.AsSpan(32 + section * 16 + 8));
            BinaryPrimitives.WriteUInt32LittleEndian(artifact.AsSpan(offset + 16), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(artifact.AsSpan(offset + 24), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(artifact.AsSpan(offset + 28), 0);
            Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
            return;
        }

        throw new InvalidOperationException("Module section missing.");
    }

    [Test]
    public void ExportedInitOutputFunctionRemainsOutsideTheProvisionalProfile()
    {
        const string source = """
            public void Fill(init ref<int32> result) { result = 42; }
            int32 Main() {
                init int32 result;
                Fill(init result);
                return result;
            }
            """;

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(Compile(source), "exported-output"));
    }
    [TestCase("float32","1.5","<","2.0")]
    [TestCase("float32","2.0","<=","2.0")]
    [TestCase("float32","2.0",">","1.5")]
    [TestCase("float32","2.0",">=","2.0")]
    [TestCase("float32","1.5","==","1.5")]
    [TestCase("float32","1.5","!=","2.0")]
    [TestCase("float64","1.5","<","2.0")]
    [TestCase("float64","2.0","<=","2.0")]
    [TestCase("float64","2.0",">","1.5")]
    [TestCase("float64","2.0",">=","2.0")]
    [TestCase("float64","1.0","==","1.0")]
    [TestCase("float64","1.5","!=","2.0")]
    public void FloatingComparisonsRoundTripThroughLinearBodies(
        string type, string left, string operation, string right)
    {
        var source = $$"""
            int32 Main() { return Compare({{left}}, {{right}}) ? 42 : 0; }
            bool Compare({{type}} left, {{type}} right) { return left {{operation}} right; }
            """;
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "float-comparison");
        var loaded = TrRmExecutableCompiler.Load(artifact);

        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void FloatingExpressionOnlyHelperKeepsFloatingTypesInIndexedModule()
    {
        const string source = """
            int32 Main() { return IsLess() ? 42 : 0; }
            bool IsLess() { return Less(1.5, 2.0); }
            bool Less(float64 left, float64 right) { return left < right; }
            """;
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "indexed-float-expression");
        var loaded = TrRmExecutableCompiler.Load(artifact);

        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }
    [TestCase("float32")]
    [TestCase("float64")]
    public void FloatingComparisonRoundTripsThroughIndexedBodies(string type)
    {
        var source = $$"""
            int32 Main() {
                {{type}} negativeZero = -0.0;
                {{type}} positiveZero = 0.0;
                if (negativeZero == positiveZero) { return 42; }
                return 0;
            }
            """;
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "indexed-float-comparison");
        var loaded = TrRmExecutableCompiler.Load(artifact);

        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }
    [TestCase("float32","==",0)]
    [TestCase("float32","!=",42)]
    [TestCase("float32","<",0)]
    [TestCase("float32","<=",0)]
    [TestCase("float32",">",0)]
    [TestCase("float32",">=",0)]
    [TestCase("float64","==",0)]
    [TestCase("float64","!=",42)]
    [TestCase("float64","<",0)]
    [TestCase("float64","<=",0)]
    [TestCase("float64",">",0)]
    [TestCase("float64",">=",0)]
    public void FloatingNaNComparisonsFollowIeee(string type, string operation, int expected)
    {
        var source = $$"""
            int32 Main() {
                {{type}} value = 1.0;
                if (value {{operation}} value) { return 42; }
                return 0;
            }
            """;
        var oneBits = type == "float32"
            ? new byte[] { 0x00, 0x00, 0x80, 0x3F }
            : new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF0, 0x3F };
        var nanBits = type == "float32"
            ? new byte[] { 0x01, 0x00, 0xC0, 0x7F }
            : new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF8, 0x7F };
        var artifact = TrRmExecutableCompiler.Emit(Compile(source), "float-nan");
        var loaded = TrRmExecutableCompiler.Load(
            ReplaceBodyConstant(artifact, 1, oneBits, nanBits));

        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
    }

    [Test]
    public void FloatingRemainderIsAcceptedButOutsideDevelopmentScalarProfile()
    {
        var program = Compile(
            "public float32 Remainder(float32 left, float32 right) { return left % right; }",
            library: true);

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(program, "float-remainder"));
    }
    [TestCase((byte)(0))]
    [TestCase((byte)(7))]
    public void InvalidFloatingComparisonKindsAreRejected(byte kind)
    {
        const string source = """
            int32 Main() { return Compare(1.5, 2.0) ? 42 : 0; }
            bool Compare(float32 left, float32 right) { return left < right; }
            """;
        var artifact = TrRmExecutableCompiler.Emit(Compile(source), "invalid-float-compare");
        var malformed = ReplaceBodyOperationByte(artifact, 2, 0x19, 2, kind);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }
    [TestCase((byte)(0))]
    [TestCase((byte)(1))]
    [TestCase((byte)(2))]
    [TestCase((byte)(3))]
    [TestCase((byte)(7))]
    public void FloatConvertRejectsInvalidDestinationTypeSpecs(byte destinationTypeId)
    {
        const string source = "public float64 Widen(float32 value) { return float64(value); }";
        var artifact = TrRmExecutableCompiler.Emit(Compile(source, true), "invalid-float-convert");
        var malformed = ReplaceBodyOperationByte(artifact, 1, 0x1A, 2, destinationTypeId);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Test]
    public void FloatConvertRejectsConstructedDestinationTypeSpecs()
    {
        const string source = "public float64 Widen(float32 value) { return float64(value); }";
        var artifact = TrRmExecutableCompiler.Emit(Compile(source, true), "constructed-float-convert");
        var index = TrRmArtifactIndex.ForDevelopment(artifact);
        Assert.That(index.TryGetSection(0x11, out var typeBytes, out var typeCount), Is.True);
        var originalTypes = new TrRmTypeSpecTableReader(typeBytes, typeCount, 0, 0, 0, 0);
        var typeRows = new List<ReadOnlyMemory<byte>>(typeCount + 1);
        var int32Row = 0;
        var float64Row = 0;
        var int32Type = TrRmTypeSpecTableWriter.Builtin(0x08);
        var float64Type = TrRmTypeSpecTableWriter.Builtin(0x15);
        for (var row = 1; row <= typeCount; row++)
        {
            var type = originalTypes.GetRow(row);
            typeRows.Add(type.ToArray());
            if (type.SequenceEqual(int32Type))
            {
                int32Row = row;
            }
            if (type.SequenceEqual(float64Type))
            {
                float64Row = row;
            }
        }
        Assert.That(int32Row, Is.Not.EqualTo(0));
        Assert.That(float64Row, Is.Not.EqualTo(0));
        typeRows.Add(TrRmTypeSpecTableWriter.Wrapped(0x06, (uint)int32Row));
        var constructedTypeRow = typeRows.Count;
        var rewrittenTypes = TrRmTypeSpecTableWriter.Encode(typeRows, 0, 0, 0, 0);

        Assert.That(index.TryGetSection(0x20, out var bodyBytes, out var bodyCount), Is.True);
        var originalBodies = new TrRmBodyTableReader(bodyBytes, bodyCount);
        var bodyRows = new ReadOnlyMemory<byte>[bodyCount];
        for (var row = 1; row <= bodyCount; row++)
        {
            bodyRows[row - 1] = originalBodies.GetRow(row).ToArray();
        }
        var body = bodyRows[0].ToArray();
        var localWidth = 1 << (body[0] & 3);
        Assert.That(localWidth, Is.EqualTo(1));
        var typeMapCount = body[1];
        var conversionOffset = body.AsSpan().IndexOf((byte)0x1A);
        Assert.That(conversionOffset >= 0, Is.True);
        var destinationTypeId = body[conversionOffset + 2];
        Assert.That(destinationTypeId, Is.InRange(1, typeMapCount));
        var typeMapOffset = 1 + localWidth + (destinationTypeId - 1) * sizeof(int);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(typeMapOffset)), Is.EqualTo(float64Row));
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(typeMapOffset), constructedTypeRow);
        bodyRows[0] = body;
        var rewrittenBodies = TrRmVariableTableWriter.EncodeBodies(bodyRows);

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags, out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags, (uint)(kind == 0x11 ? typeRows.Count : count),
                kind switch {
                    0x11 => rewrittenTypes,
                    0x20 => rewrittenBodies,
                    _ => bytes.ToArray(),
                }));
        }
        var malformed = TrRmArtifactWriter.EncodeDevelopment(sections);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Test]
    public void OptionalFloatingTypesRemainOutsideExecutableTrRm()
    {
        var artifact = OptionalScalarArtifact(
            IndexedOptionalPresenceBody(
                Float32TypeSpecRow, true, OptionalFloat32TypeSpecRow),
            optionalChar16: false,
            optionalInt32: false,
            optionalFloat32: true);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }

    [Test]
    public void IntegerBodyBytesRemainStableInMixedFloatModules()
    {
        var integerOnly = TrRmExecutableCompiler.Emit(
            Compile("int32 Main() { return 42; }"), "integer-only");
        const string mixedSource = """
            int32 Main() { return Integer(); }
            int32 Integer() { return 42; }
            float32 Identity(float32 value) { return value; }
            """;
        var mixed = TrRmExecutableCompiler.Emit(Compile(mixedSource), "mixed-float");

        var integerOnlyIndex = TrRmArtifactIndex.ForDevelopment(integerOnly);
        Assert.That(integerOnlyIndex.TryGetSection(0x20, out var integerOnlyBodies, out var integerOnlyCount), Is.True);
        var integerOnlyBody = new TrRmBodyTableReader(integerOnlyBodies, integerOnlyCount).GetRow(1);
        var mixedIndex = TrRmArtifactIndex.ForDevelopment(mixed);
        Assert.That(mixedIndex.TryGetSection(0x20, out var mixedBodies, out var mixedCount), Is.True);
        var integerMethodBody = new TrRmBodyTableReader(mixedBodies, mixedCount).GetRow(2);

        Assert.That(integerMethodBody.ToArray(), Is.EqualTo(integerOnlyBody.ToArray()));
    }

    [Test]
    public void FloatingFormatConversionsExecuteThroughLinearAndIndexedBodies()
    {
        const string source = """
            public float64 Widen(float32 value) { return float64(value); }
            public float32 Narrow(float64 value) { return float32(value); }
            public float64 WidenIf(float32 value) {
                if (value < 0.0) { return float64(value); }
                return float64(value);
            }
            public float32 NarrowIf(float64 value) {
                if (value < 0.0) { return float32(value); }
                return float32(value);
            }
            """;
        var original = Compile(source, true);
        var artifact = TrRmExecutableCompiler.Emit(original, "float-conversions");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        string[] names = ["Widen", "Narrow", "WidenIf", "NarrowIf"];
        var images = new[]
        {
            NativeEmitter.EmitLibrary(original, names, NativeRuntimePolicy.FailFast),
            NativeEmitter.EmitLibrary(loaded, names, NativeRuntimePolicy.FailFast),
        };
        var paths = new string[images.Length];
        var handles = new IntPtr[images.Length];
        try
        {
            for (var index = 0; index < images.Length; index++)
            {
                paths[index] = Path.Combine(TestArtifactPaths.DirectoryPath,
                    $"trrm-float-conversion-{Guid.NewGuid():N}-{index}.dll");
                File.WriteAllBytes(paths[index], images[index]);
                handles[index] = NativeLibrary.Load(paths[index]);
            }

            foreach (var handle in handles)
            {
                var widen = Marshal.GetDelegateForFunctionPointer<Float32ToFloat64>(
                    NativeLibrary.GetExport(handle, "Widen"));
                var narrow = Marshal.GetDelegateForFunctionPointer<Float64ToFloat32>(
                    NativeLibrary.GetExport(handle, "Narrow"));
                var widenIf = Marshal.GetDelegateForFunctionPointer<Float32ToFloat64>(
                    NativeLibrary.GetExport(handle, "WidenIf"));
                var narrowIf = Marshal.GetDelegateForFunctionPointer<Float64ToFloat32>(
                    NativeLibrary.GetExport(handle, "NarrowIf"));

                Assert.That(DoubleBits(widen(SingleFromBits(0x3DCCCCCD))),
                    Is.EqualTo(0x3FB99999A0000000L));
                Assert.That(DoubleBits(widen(SingleFromBits(int.MinValue))),
                    Is.EqualTo(long.MinValue));
                Assert.That(DoubleBits(widenIf(SingleFromBits(int.MinValue))),
                    Is.EqualTo(long.MinValue));
                Assert.That(double.IsPositiveInfinity(widenIf(SingleFromBits(0x7F800000))), Is.True);
                Assert.That(double.IsNegativeInfinity(widenIf(SingleFromBits(unchecked((int)0xFF800000)))),
                    Is.True);
                Assert.That(double.IsNaN(widenIf(SingleFromBits(0x7FC00001))), Is.True);
                Assert.That(SingleBits(narrow(DoubleFromBits(0x3FF0000010000000L))),
                    Is.EqualTo(0x3F800000));
                Assert.That(SingleBits(narrow(double.MaxValue)), Is.EqualTo(0x7F800000));
                Assert.That(SingleBits(narrow(double.Epsilon)), Is.EqualTo(0));
                Assert.That(SingleBits(narrow(DoubleFromBits(long.MinValue | 1))),
                    Is.EqualTo(int.MinValue));
                Assert.That(float.IsNaN(narrow(double.NaN)), Is.True);
                Assert.That(SingleBits(narrow(double.PositiveInfinity)), Is.EqualTo(0x7F800000));
                Assert.That(DoubleBits(widenIf(SingleFromBits(0x3DCCCCCD))),
                    Is.EqualTo(0x3FB99999A0000000L));
                Assert.That(SingleBits(narrowIf(double.NegativeInfinity)),
                    Is.EqualTo(unchecked((int)0xFF800000)));
                Assert.That(SingleBits(narrowIf(double.PositiveInfinity)), Is.EqualTo(0x7F800000));
                Assert.That(SingleBits(narrowIf(double.MaxValue)), Is.EqualTo(unchecked((int)0x7F800000)));
                Assert.That(SingleBits(narrowIf(double.Epsilon)), Is.EqualTo(0));
                Assert.That(SingleBits(narrowIf(DoubleFromBits(long.MinValue | 1))),
                    Is.EqualTo(int.MinValue));
                Assert.That(float.IsNaN(narrowIf(double.NaN)), Is.True);
                Assert.That(SingleBits(narrowIf(DoubleFromBits(0x3FF0000010000000L))),
                    Is.EqualTo(0x3F800000));
                Assert.That(SingleBits(narrowIf(DoubleFromBits(0x4000000000000000L))),
                    Is.EqualTo(0x40000000));
            }
        }
        finally
        {
            foreach (var handle in handles)
            {
                if (handle != IntPtr.Zero)
                {
                    NativeLibrary.Free(handle);
                }
            }
            foreach (var path in paths)
            {
                if (path is not null)
                {
                    File.Delete(path);
                }
            }
        }
    }

    [Test]
    public void IntegerToFloatingConversionsExecuteThroughLinearAndIndexedBodies()
    {
        const string source = """
            public float32 Int32ToFloat32(int32 value) { return float32(value); }
            public float32 UInt32ToFloat32(uint32 value) { return float32(value); }
            public float32 Int64ToFloat32(int64 value) { return float32(value); }
            public float32 UInt64ToFloat32(uint64 value) { return float32(value); }
            public float64 Int32ToFloat64(int32 value) { return float64(value); }
            public float64 UInt32ToFloat64(uint32 value) { return float64(value); }
            public float64 Int64ToFloat64(int64 value) { return float64(value); }
            public float64 UInt64ToFloat64(uint64 value) { return float64(value); }
            public float32 Int64ToFloat32If(int64 value) {
                float32 converted = float32(value);
                if (converted == 16777216.0) { return converted; }
                return 0.0;
            }
            int32 Main() {
                if (Int32ToFloat32(16777217) != 16777216.0) { return 1; }
                if (UInt32ToFloat32(4294967295) != 4294967296.0) { return 2; }
                if (Int64ToFloat32(16777219) != 16777220.0) { return 3; }
                if (UInt64ToFloat32(18446744073709551615) != 18446744073709551616.0) { return 4; }
                if (Int32ToFloat64(-2147483648) != -2147483648.0) { return 5; }
                if (UInt32ToFloat64(4294967295) != 4294967295.0) { return 6; }
                if (Int64ToFloat64(-9223372036854775808) != -9223372036854775808.0) { return 7; }
                if (UInt64ToFloat64(18446744073709551615) != 18446744073709551616.0) { return 8; }
                if (Int64ToFloat32If(16777217) != 16777216.0) { return 9; }
                return 42;
            }
            """;
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "integer-float-conversions");
        var loaded = TrRmExecutableCompiler.Load(artifact);

        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void FloatingToIntegerConversionsExecuteThroughLinearAndIndexedBodies()
    {
        const string source = """
            public int32 Float64ToInt32(float64 value) { return int32(value); }
            public uint32 Float64ToUInt32(float64 value) { return uint32(value); }
            public int64 Float64ToInt64(float64 value) { return int64(value); }
            public uint64 Float64ToUInt64(float64 value) { return uint64(value); }
            public int32 Float32ToInt32(float32 value) { return int32(value); }
            public uint32 Float32ToUInt32(float32 value) { return uint32(value); }
            public int64 Float32ToInt64(float32 value) { return int64(value); }
            public uint64 Float32ToUInt64(float32 value) { return uint64(value); }
            public int32 Float64ToInt32IfZero(float64 value) {
                if (value == 0.0) { return int32(value); }
                return 42;
            }
            public uint32 Float64ToUInt32IfZero(float64 value) {
                if (value == 0.0) { return uint32(value); }
                return 42;
            }
            public int64 Float64ToInt64IfZero(float64 value) {
                if (value == 0.0) { return int64(value); }
                return 42;
            }
            public uint64 Float64ToUInt64IfZero(float64 value) {
                if (value == 0.0) { return uint64(value); }
                return 42;
            }
            public int32 Float32ToInt32IfZero(float32 value) {
                if (value == 0.0) { return int32(value); }
                return 42;
            }
            public uint32 Float32ToUInt32IfZero(float32 value) {
                if (value == 0.0) { return uint32(value); }
                return 42;
            }
            public int64 Float32ToInt64IfZero(float32 value) {
                if (value == 0.0) { return int64(value); }
                return 42;
            }
            public uint64 Float32ToUInt64IfZero(float32 value) {
                if (value == 0.0) { return uint64(value); }
                return 42;
            }
            """;
        var original = Compile(source, true);
        var artifact = TrRmExecutableCompiler.Emit(original, "float-integer-conversions");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        string[] names =
        [
            "Float64ToInt32", "Float64ToUInt32", "Float64ToInt64", "Float64ToUInt64",
            "Float32ToInt32", "Float32ToUInt32", "Float32ToInt64", "Float32ToUInt64",
            "Float64ToInt32IfZero", "Float64ToUInt32IfZero", "Float64ToInt64IfZero",
            "Float64ToUInt64IfZero", "Float32ToInt32IfZero", "Float32ToUInt32IfZero",
            "Float32ToInt64IfZero", "Float32ToUInt64IfZero",
        ];
        var images = new[]
        {
            NativeEmitter.EmitLibrary(original, names, NativeRuntimePolicy.FailFast),
            NativeEmitter.EmitLibrary(loaded, names, NativeRuntimePolicy.FailFast),
        };
        var paths = new string[images.Length];
        var handles = new IntPtr[images.Length];
        try
        {
            for (var index = 0; index < images.Length; index++)
            {
                paths[index] = Path.Combine(TestArtifactPaths.DirectoryPath,
                    $"trrm-float-to-integer-{Guid.NewGuid():N}-{index}.dll");
                File.WriteAllBytes(paths[index], images[index]);
                handles[index] = NativeLibrary.Load(paths[index]);
            }

            foreach (var handle in handles)
            {
                var float64ToInt32 = Marshal.GetDelegateForFunctionPointer<Float64ToInt32>(
                    NativeLibrary.GetExport(handle, "Float64ToInt32"));
                var float64ToUInt32 = Marshal.GetDelegateForFunctionPointer<Float64ToUInt32>(
                    NativeLibrary.GetExport(handle, "Float64ToUInt32"));
                var float64ToInt64 = Marshal.GetDelegateForFunctionPointer<Float64ToInt64>(
                    NativeLibrary.GetExport(handle, "Float64ToInt64"));
                var float64ToUInt64 = Marshal.GetDelegateForFunctionPointer<Float64ToUInt64>(
                    NativeLibrary.GetExport(handle, "Float64ToUInt64"));
                var float32ToInt32 = Marshal.GetDelegateForFunctionPointer<Float32ToInt32>(
                    NativeLibrary.GetExport(handle, "Float32ToInt32"));
                var float32ToUInt32 = Marshal.GetDelegateForFunctionPointer<Float32ToUInt32>(
                    NativeLibrary.GetExport(handle, "Float32ToUInt32"));
                var float32ToInt64 = Marshal.GetDelegateForFunctionPointer<Float32ToInt64>(
                    NativeLibrary.GetExport(handle, "Float32ToInt64"));
                var float32ToUInt64 = Marshal.GetDelegateForFunctionPointer<Float32ToUInt64>(
                    NativeLibrary.GetExport(handle, "Float32ToUInt64"));
                var float64ToInt32IfZero = Marshal.GetDelegateForFunctionPointer<Float64ToInt32>(
                    NativeLibrary.GetExport(handle, "Float64ToInt32IfZero"));
                var float64ToUInt32IfZero = Marshal.GetDelegateForFunctionPointer<Float64ToUInt32>(
                    NativeLibrary.GetExport(handle, "Float64ToUInt32IfZero"));
                var float64ToInt64IfZero = Marshal.GetDelegateForFunctionPointer<Float64ToInt64>(
                    NativeLibrary.GetExport(handle, "Float64ToInt64IfZero"));
                var float64ToUInt64IfZero = Marshal.GetDelegateForFunctionPointer<Float64ToUInt64>(
                    NativeLibrary.GetExport(handle, "Float64ToUInt64IfZero"));
                var float32ToInt32IfZero = Marshal.GetDelegateForFunctionPointer<Float32ToInt32>(
                    NativeLibrary.GetExport(handle, "Float32ToInt32IfZero"));
                var float32ToUInt32IfZero = Marshal.GetDelegateForFunctionPointer<Float32ToUInt32>(
                    NativeLibrary.GetExport(handle, "Float32ToUInt32IfZero"));
                var float32ToInt64IfZero = Marshal.GetDelegateForFunctionPointer<Float32ToInt64>(
                    NativeLibrary.GetExport(handle, "Float32ToInt64IfZero"));
                var float32ToUInt64IfZero = Marshal.GetDelegateForFunctionPointer<Float32ToUInt64>(
                    NativeLibrary.GetExport(handle, "Float32ToUInt64IfZero"));

                Assert.That(float64ToInt32(1.9), Is.EqualTo(1));
                Assert.That(float64ToInt32(-1.9), Is.EqualTo(-1));
                Assert.That(float64ToInt32(-0.0), Is.EqualTo(0));
                Assert.That(float64ToInt32(-2147483648.9), Is.EqualTo(int.MinValue));
                Assert.That(float64ToInt32(2147483647.9), Is.EqualTo(int.MaxValue));
                Assert.That(float64ToUInt32(-0.5), Is.EqualTo(0u));
                Assert.That(float64ToUInt32(-0.0), Is.EqualTo(0u));
                Assert.That(float64ToUInt32(4294967295.75), Is.EqualTo(uint.MaxValue));
                Assert.That(float64ToInt64((double)long.MinValue), Is.EqualTo(long.MinValue));
                Assert.That(float64ToInt64(Math.BitDecrement(9223372036854775808.0)),
                    Is.EqualTo(9_223_372_036_854_774_784L));
                Assert.That(float64ToUInt64(Math.BitDecrement(18446744073709551616.0)),
                    Is.EqualTo(18_446_744_073_709_549_568UL));
                Assert.That(float64ToUInt64(-0.5), Is.EqualTo(0UL));
                Assert.That(float64ToUInt64(-0.0), Is.EqualTo(0UL));
                Assert.That(float32ToInt32(3.75F), Is.EqualTo(3));
                Assert.That(float32ToInt32(-0.0F), Is.EqualTo(0));
                Assert.That(float32ToUInt32(-0.5F), Is.EqualTo(0U));
                Assert.That(float32ToUInt32(SingleFromBits(unchecked((int)0x4F7FFFFF))),
                    Is.EqualTo(4_294_967_040U));
                Assert.That(float32ToInt64(SingleFromBits(unchecked((int)0xDF000000))),
                    Is.EqualTo(long.MinValue));
                Assert.That(float32ToUInt64(SingleFromBits(0x5F000000)),
                    Is.EqualTo(9_223_372_036_854_775_808UL));
                Assert.That(float32ToUInt64(1.9F), Is.EqualTo(1UL));
                Assert.That(float64ToInt32IfZero(-0.0), Is.EqualTo(0));
                Assert.That(float64ToInt32IfZero(1.0), Is.EqualTo(42));
                Assert.That(float64ToUInt32IfZero(-0.0), Is.EqualTo(0U));
                Assert.That(float64ToUInt32IfZero(1.0), Is.EqualTo(42U));
                Assert.That(float64ToInt64IfZero(-0.0), Is.EqualTo(0L));
                Assert.That(float64ToInt64IfZero(1.0), Is.EqualTo(42L));
                Assert.That(float64ToUInt64IfZero(-0.0), Is.EqualTo(0UL));
                Assert.That(float64ToUInt64IfZero(1.0), Is.EqualTo(42UL));
                Assert.That(float32ToInt32IfZero(-0.0F), Is.EqualTo(0));
                Assert.That(float32ToInt32IfZero(1.0F), Is.EqualTo(42));
                Assert.That(float32ToUInt32IfZero(-0.0F), Is.EqualTo(0U));
                Assert.That(float32ToUInt32IfZero(1.0F), Is.EqualTo(42U));
                Assert.That(float32ToInt64IfZero(-0.0F), Is.EqualTo(0L));
                Assert.That(float32ToInt64IfZero(1.0F), Is.EqualTo(42L));
                Assert.That(float32ToUInt64IfZero(-0.0F), Is.EqualTo(0UL));
                Assert.That(float32ToUInt64IfZero(1.0F), Is.EqualTo(42UL));
            }
        }
        finally
        {
            foreach (var handle in handles)
            {
                if (handle != IntPtr.Zero)
                {
                    NativeLibrary.Free(handle);
                }
            }
            foreach (var path in paths)
            {
                if (path is not null)
                {
                    File.Delete(path);
                }
            }
        }
    }
    [TestCase(0x7FF8000000000001UL)]
    [TestCase(0x7FF0000000000000UL)]
    [TestCase(0xFFF0000000000000UL)]
    [TestCase(0x41E0000000000000UL)]
    [TestCase(0xC1E0000000200000UL)]
    public async Task FloatingToIntegerFailuresUseTheOverflowFailFastStatusAsync(ulong replacementBits)
    {
        const string source = """
            int32 Main() { return int32(Value()); }
            float64 Value() { return 1.0; }
            """;
        var artifact = TrRmExecutableCompiler.Emit(Compile(source), "float-to-integer-failure");
        var oneBits = new byte[sizeof(ulong)];
        var failureBits = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(oneBits,
            unchecked((ulong)BitConverter.DoubleToInt64Bits(1.0)));
        BinaryPrimitives.WriteUInt64LittleEndian(failureBits, replacementBits);
        var malformed = ReplaceBodyConstant(artifact, 2, oneBits, failureBits);
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-float-to-integer-{Guid.NewGuid():N}.trrm");
        try
        {
            File.WriteAllBytes(path, malformed);
            var (exitCode, output, error) = await RunTrRmAsync(path);

            Assert.That(exitCode, Is.EqualTo(unchecked((int)0xE0540001)));
            Assert.That(output, Is.Empty);
            Assert.That(error, Is.Empty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void FloatToIntegerRejectsAnInvalidDestinationTypeSpec()
    {
        const string source = "public int32 Convert(float64 value) { return int32(value); }";
        var artifact = TrRmExecutableCompiler.Emit(Compile(source, true), "invalid-float-integer");
        var malformed = ReplaceBodyOperationByte(artifact, 1, 0x1F, 2, 0);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Test]
    public void FloatingToIntegerConversionsCannotPromiseNoThrow()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[NoThrow] public int32 Convert(float64 value) { return int32(value); }"));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3017"), Is.True);
    }

    [Test]
    public void ExecutableWriterRejectsRetiredNoThrowClaimsForFloatConversions()
    {
        const string source = """
             public int32 Convert(float64 value) { return 0; }
            public int32 ConvertChecked(float64 value) { return int32(value); }
             public int32 ConvertIf(float64 value) {
                if (value == 0.0) { return 0; }
                return 42;
            }
            public int32 ConvertIfChecked(float64 value) {
                if (value == 0.0) { return int32(value); }
                return 42;
            }
            """;
        var program = BoundProgramTestHelpers.MarkNoThrow(
            Compile(source, true), "Convert", "ConvertIf");

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(program, "retired-float-to-integer-nothrow"));
    }

    [Test]
    public void IntegerToFloatRejectsAnIntegerDestinationTypeSpec()
    {
        const string source = "public float32 Convert(int32 value) { return float32(value); }";
        var artifact = TrRmExecutableCompiler.Emit(Compile(source, true), "invalid-integer-float");
        var malformed = ReplaceBodyOperationByte(artifact, 1, 0x1B, 2, 1);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Test]
    public void Char16ToFloatingConversionsPreserveCodeUnitValues()
    {
        const string source = """
             public float32 ToFloat32(char16 value) { return float32(value); }
             public float64 ToFloat64(char16 value) { return float64(value); }
            public float32 ToFloat32IfBoundary(char16 value) {
                if (value == char16(0) || value == char16(55296) || value == char16(65535)) {
                    return float32(value);
                }
                return 0.0;
            }
            public float64 ToFloat64IfBoundary(char16 value) {
                if (value == char16(0) || value == char16(55296) || value == char16(65535)) {
                    return float64(value);
                }
                return 0.0;
            }
            """;
        var original = Compile(source, true);
        var artifact = TrRmExecutableCompiler.Emit(original, "char16-float-conversions");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        string[] names = ["ToFloat32", "ToFloat64", "ToFloat32IfBoundary", "ToFloat64IfBoundary"];
        var images = new[]
        {
            NativeEmitter.EmitLibrary(original, names, NativeRuntimePolicy.FailFast),
            NativeEmitter.EmitLibrary(loaded, names, NativeRuntimePolicy.FailFast),
        };
        var paths = new string[images.Length];
        var handles = new IntPtr[images.Length];
        try
        {
            for (var index = 0; index < images.Length; index++)
            {
                paths[index] = Path.Combine(TestArtifactPaths.DirectoryPath,
                    $"trrm-char16-float-{Guid.NewGuid():N}-{index}.dll");
                File.WriteAllBytes(paths[index], images[index]);
                handles[index] = NativeLibrary.Load(paths[index]);
            }

            foreach (var handle in handles)
            {
                var toFloat32 = Marshal.GetDelegateForFunctionPointer<Char16ToFloat32>(
                    NativeLibrary.GetExport(handle, "ToFloat32"));
                var toFloat64 = Marshal.GetDelegateForFunctionPointer<Char16ToFloat64>(
                    NativeLibrary.GetExport(handle, "ToFloat64"));
                var toFloat32If = Marshal.GetDelegateForFunctionPointer<Char16ToFloat32>(
                    NativeLibrary.GetExport(handle, "ToFloat32IfBoundary"));
                var toFloat64If = Marshal.GetDelegateForFunctionPointer<Char16ToFloat64>(
                    NativeLibrary.GetExport(handle, "ToFloat64IfBoundary"));

                Assert.That(SingleBits(toFloat32(0)), Is.EqualTo(0));
                Assert.That(SingleBits(toFloat32(0xD800)),
                    Is.EqualTo(BitConverter.SingleToInt32Bits(55296.0F)));
                Assert.That(SingleBits(toFloat32(ushort.MaxValue)),
                    Is.EqualTo(BitConverter.SingleToInt32Bits(65535.0F)));
                Assert.That(DoubleBits(toFloat64(0)), Is.EqualTo(0L));
                Assert.That(DoubleBits(toFloat64(0xD800)),
                    Is.EqualTo(BitConverter.DoubleToInt64Bits(55296.0)));
                Assert.That(DoubleBits(toFloat64(ushort.MaxValue)),
                    Is.EqualTo(BitConverter.DoubleToInt64Bits(65535.0)));
                Assert.That(SingleBits(toFloat32If(0)),
                    Is.EqualTo(BitConverter.SingleToInt32Bits(0.0F)));
                Assert.That(SingleBits(toFloat32If(0xD800)),
                    Is.EqualTo(BitConverter.SingleToInt32Bits(55296.0F)));
                Assert.That(SingleBits(toFloat32If(0xFFFF)),
                    Is.EqualTo(BitConverter.SingleToInt32Bits(65535.0F)));
                Assert.That(DoubleBits(toFloat64If(0)), Is.EqualTo(0L));
                Assert.That(DoubleBits(toFloat64If(0xD800)),
                    Is.EqualTo(BitConverter.DoubleToInt64Bits(55296.0)));
                Assert.That(DoubleBits(toFloat64If(0xFFFF)),
                    Is.EqualTo(BitConverter.DoubleToInt64Bits(65535.0)));
            }
        }
        finally
        {
            foreach (var handle in handles)
            {
                if (handle != IntPtr.Zero)
                {
                    NativeLibrary.Free(handle);
                }
            }
            foreach (var path in paths)
            {
                if (path is not null)
                {
                    File.Delete(path);
                }
            }
        }
    }

    [Test]
    public void Char16ToFloatRejectsAnIntegerDestinationTypeSpec()
    {
        const string source = "public float32 Convert(char16 value) { return float32(value); }";
        var artifact = TrRmExecutableCompiler.Emit(Compile(source, true), "invalid-char16-float");
        var malformed = ReplaceBodyOperationByte(artifact, 1, 0x1E, 2, 1);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Test]
    public void FloatingNegationPreservesPayloadBitsThroughLinearAndIndexedBodies()
    {
        const string source = """
            public float32 Negate32(float32 value) { return -value; }
            public float64 Negate64(float64 value) { return -value; }
            public float32 Negate32If(float32 value) {
                if (value == 0.0) { return -value; }
                return -1.0;
            }
            public float64 Negate64If(float64 value) {
                if (value == 0.0) { return -value; }
                return -1.0;
            }
            """;
        var original = Compile(source, true);
        var artifact = TrRmExecutableCompiler.Emit(original, "float-negation");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        string[] names = ["Negate32", "Negate64", "Negate32If", "Negate64If"];
        var images = new[]
        {
            NativeEmitter.EmitLibrary(original, names, NativeRuntimePolicy.FailFast),
            NativeEmitter.EmitLibrary(loaded, names, NativeRuntimePolicy.FailFast),
        };
        var paths = new string[images.Length];
        var handles = new IntPtr[images.Length];
        try
        {
            for (var index = 0; index < images.Length; index++)
            {
                paths[index] = Path.Combine(TestArtifactPaths.DirectoryPath,
                    $"trrm-float-negation-{Guid.NewGuid():N}-{index}.dll");
                File.WriteAllBytes(paths[index], images[index]);
                handles[index] = NativeLibrary.Load(paths[index]);
            }

            foreach (var handle in handles)
            {
                var negate32 = Marshal.GetDelegateForFunctionPointer<Float32Unary>(
                    NativeLibrary.GetExport(handle, "Negate32"));
                var negate64 = Marshal.GetDelegateForFunctionPointer<Float64Unary>(
                    NativeLibrary.GetExport(handle, "Negate64"));
                var negate32If = Marshal.GetDelegateForFunctionPointer<Float32Unary>(
                    NativeLibrary.GetExport(handle, "Negate32If"));
                var negate64If = Marshal.GetDelegateForFunctionPointer<Float64Unary>(
                    NativeLibrary.GetExport(handle, "Negate64If"));

                Assert.That(SingleBits(negate32(SingleFromBits(0))),
                    Is.EqualTo(unchecked((int)0x80000000)));
                Assert.That(SingleBits(negate32(SingleFromBits(unchecked((int)0x80000000)))),
                    Is.EqualTo(0));
                Assert.That(SingleBits(negate32(SingleFromBits(0x7F800000))),
                    Is.EqualTo(unchecked((int)0xFF800000)));
                Assert.That(SingleBits(negate32(SingleFromBits(unchecked((int)0xFF800000)))),
                    Is.EqualTo(0x7F800000));
                Assert.That(SingleBits(negate32(SingleFromBits(0x7FC12345))),
                    Is.EqualTo(unchecked((int)0xFFC12345)));
                Assert.That(SingleBits(negate32(SingleFromBits(unchecked((int)0xFFC54321)))),
                    Is.EqualTo(unchecked((int)0x7FC54321)));
                Assert.That(DoubleBits(negate64(DoubleFromBits(0))), Is.EqualTo(long.MinValue));
                Assert.That(DoubleBits(negate64(DoubleFromBits(long.MinValue))), Is.EqualTo(0L));
                Assert.That(DoubleBits(negate64(DoubleFromBits(0x7FF0000000000000L))),
                    Is.EqualTo(unchecked((long)0xFFF0000000000000UL)));
                Assert.That(DoubleBits(negate64(DoubleFromBits(unchecked((long)0xFFF0000000000000UL)))),
                    Is.EqualTo(0x7FF0000000000000L));
                Assert.That(DoubleBits(negate64(DoubleFromBits(0x7FF8123456789ABCL))),
                    Is.EqualTo(unchecked((long)0xFFF8123456789ABCUL)));
                Assert.That(DoubleBits(negate64(DoubleFromBits(unchecked((long)0xFFF8ABCDEF012345UL)))),
                    Is.EqualTo(unchecked((long)0x7FF8ABCDEF012345UL)));
                Assert.That(SingleBits(negate32If(SingleFromBits(0))),
                    Is.EqualTo(unchecked((int)0x80000000)));
                Assert.That(SingleBits(negate32If(SingleFromBits(0x3F800000))),
                    Is.EqualTo(unchecked((int)0xBF800000)));
                Assert.That(DoubleBits(negate64If(DoubleFromBits(0))), Is.EqualTo(long.MinValue));
                Assert.That(DoubleBits(negate64If(DoubleFromBits(0x3FF0000000000000L))),
                    Is.EqualTo(unchecked((long)0xBFF0000000000000UL)));
            }
        }
        finally
        {
            foreach (var handle in handles)
            {
                if (handle != IntPtr.Zero)
                {
                    NativeLibrary.Free(handle);
                }
            }
            foreach (var path in paths)
            {
                if (path is not null)
                {
                    File.Delete(path);
                }
            }
        }
    }

    [Test]
    public void FloatNegateRejectsIntegerOperands()
    {
        const string source = "public int32 Negate(int32 value) { return -value; }";
        var artifact = TrRmExecutableCompiler.Emit(Compile(source, true), "invalid-float-negate");
        var malformed = ReplaceBodyOperationByte(artifact, 1, 0x17, 0, 0x1C);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Test]
    public void FloatingArithmeticExecutesThroughLinearAndIndexedBodies()
    {
        static float Single(uint bits) =>
            BitConverter.Int32BitsToSingle(unchecked((int)bits));
        static int SingleBits(float value) => BitConverter.SingleToInt32Bits(value);
        static double Double(ulong bits) =>
            BitConverter.Int64BitsToDouble(unchecked((long)bits));
        static long DoubleBits(double value) => BitConverter.DoubleToInt64Bits(value);

        const string source = """
             public float32 Add32(float32 left, float32 right) { return left + right; }
             public float32 Subtract32(float32 left, float32 right) { return left - right; }
             public float32 Multiply32(float32 left, float32 right) { return left * right; }
             public float32 Divide32(float32 left, float32 right) { return left / right; }
             public float64 Add64(float64 left, float64 right) { return left + right; }
             public float64 Subtract64(float64 left, float64 right) { return left - right; }
             public float64 Multiply64(float64 left, float64 right) { return left * right; }
             public float64 Divide64(float64 left, float64 right) { return left / right; }
            public float32 Add32IfZero(float32 left, float32 right) {
                if (left == 0.0) { return left + right; }
                return 0.0;
            }
            public float64 Add64IfZero(float64 left, float64 right) {
                if (left == 0.0) { return left + right; }
                return 0.0;
            }
            """;
        var original = Compile(source, true);
        var artifact = TrRmExecutableCompiler.Emit(original, "float-arithmetic");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        string[] names = [
            "Add32", "Subtract32", "Multiply32", "Divide32",
            "Add64", "Subtract64", "Multiply64", "Divide64",
            "Add32IfZero", "Add64IfZero",
        ];
        var images = new[]
        {
            NativeEmitter.EmitLibrary(original, names, NativeRuntimePolicy.FailFast),
            NativeEmitter.EmitLibrary(loaded, names, NativeRuntimePolicy.FailFast),
        };
        var paths = new string[images.Length];
        var handles = new IntPtr[images.Length];
        try
        {
            for (var index = 0; index < images.Length; index++)
            {
                paths[index] = Path.Combine(TestArtifactPaths.DirectoryPath,
                    $"trrm-float-arithmetic-{Guid.NewGuid():N}-{index}.dll");
                File.WriteAllBytes(paths[index], images[index]);
                handles[index] = NativeLibrary.Load(paths[index]);
            }

            for (var imageIndex = 0; imageIndex < handles.Length; imageIndex++)
            {
                var handle = handles[imageIndex];
                var add32 = Marshal.GetDelegateForFunctionPointer<Float32Binary>(
                    NativeLibrary.GetExport(handle, "Add32"));
                var subtract32 = Marshal.GetDelegateForFunctionPointer<Float32Binary>(
                    NativeLibrary.GetExport(handle, "Subtract32"));
                var multiply32 = Marshal.GetDelegateForFunctionPointer<Float32Binary>(
                    NativeLibrary.GetExport(handle, "Multiply32"));
                var divide32 = Marshal.GetDelegateForFunctionPointer<Float32Binary>(
                    NativeLibrary.GetExport(handle, "Divide32"));
                var add64 = Marshal.GetDelegateForFunctionPointer<Float64Binary>(
                    NativeLibrary.GetExport(handle, "Add64"));
                var subtract64 = Marshal.GetDelegateForFunctionPointer<Float64Binary>(
                    NativeLibrary.GetExport(handle, "Subtract64"));
                var multiply64 = Marshal.GetDelegateForFunctionPointer<Float64Binary>(
                    NativeLibrary.GetExport(handle, "Multiply64"));
                var divide64 = Marshal.GetDelegateForFunctionPointer<Float64Binary>(
                    NativeLibrary.GetExport(handle, "Divide64"));
                var add32IfZero = Marshal.GetDelegateForFunctionPointer<Float32Binary>(
                    NativeLibrary.GetExport(handle, "Add32IfZero"));
                var add64IfZero = Marshal.GetDelegateForFunctionPointer<Float64Binary>(
                    NativeLibrary.GetExport(handle, "Add64IfZero"));

                Assert.That(SingleBits(add32(Single(0x3FC00000), Single(0x40100000))),
                    Is.EqualTo(0x40700000),
                    $"Floating arithmetic image {imageIndex}.");
                Assert.That(SingleBits(subtract32(Single(0x40800000), Single(0x3FC00000))),
                    Is.EqualTo(0x40200000));
                Assert.That(SingleBits(multiply32(Single(0x40000000), Single(0x40400000))),
                    Is.EqualTo(0x40C00000));
                Assert.That(SingleBits(divide32(Single(0x40400000), Single(0x40000000))),
                    Is.EqualTo(0x3FC00000));
                Assert.That(float.IsNaN(add32(Single(0x7F800000), Single(0xFF800000))), Is.True);
                Assert.That(float.IsNaN(subtract32(Single(0x7F800000), Single(0x7F800000))), Is.True);
                Assert.That(float.IsNaN(multiply32(Single(0), Single(0x7F800000))), Is.True);
                Assert.That(float.IsNaN(divide32(Single(0), Single(0))), Is.True);
                Assert.That(SingleBits(add32(Single(0xFFC12345), Single(0x3F800000))),
                    Is.EqualTo(unchecked((int)0xFFC12345)));
                Assert.That(SingleBits(multiply32(Single(0x80000000), Single(0x3F800000))),
                    Is.EqualTo(unchecked((int)0x80000000)));

                Assert.That(DoubleBits(add64(Double(0x3FF8000000000000), Double(0x4002000000000000))),
                    Is.EqualTo(0x400E000000000000L));
                Assert.That(DoubleBits(subtract64(Double(0x4010000000000000), Double(0x3FF8000000000000))),
                    Is.EqualTo(0x4004000000000000L));
                Assert.That(DoubleBits(multiply64(Double(0x4000000000000000), Double(0x4008000000000000))),
                    Is.EqualTo(0x4018000000000000L));
                Assert.That(DoubleBits(divide64(Double(0x4008000000000000), Double(0x4000000000000000))),
                    Is.EqualTo(0x3FF8000000000000L));
                Assert.That(double.IsNaN(add64(Double(0x7FF0000000000000),
                    Double(0xFFF0000000000000))), Is.True);
                Assert.That(double.IsNaN(subtract64(Double(0x7FF0000000000000),
                    Double(0x7FF0000000000000))), Is.True);
                Assert.That(double.IsNaN(multiply64(Double(0),
                    Double(0x7FF0000000000000))), Is.True);
                Assert.That(double.IsNaN(divide64(Double(0), Double(0))), Is.True);
                Assert.That(DoubleBits(add64(Double(0xFFF8123456789ABC),
                    Double(0x3FF0000000000000))), Is.EqualTo(unchecked((long)0xFFF8123456789ABCUL)));
                Assert.That(DoubleBits(multiply64(Double(0x8000000000000000),
                    Double(0x3FF0000000000000))), Is.EqualTo(long.MinValue));
                Assert.That(SingleBits(add32IfZero(Single(0x80000000), Single(0x80000000))),
                    Is.EqualTo(unchecked((int)0x80000000)));
                Assert.That(SingleBits(add32IfZero(Single(0x3F800000), Single(0x40000000))),
                    Is.EqualTo(0));
                Assert.That(DoubleBits(add64IfZero(Double(0x8000000000000000),
                    Double(0x8000000000000000))), Is.EqualTo(long.MinValue));
                Assert.That(DoubleBits(add64IfZero(Double(0x3FF0000000000000),
                    Double(0x4000000000000000))), Is.EqualTo(0L));
            }
        }
        finally
        {
            foreach (var handle in handles)
            {
                if (handle != IntPtr.Zero)
                {
                    NativeLibrary.Free(handle);
                }
            }
            foreach (var path in paths)
            {
                if (path is not null)
                {
                    File.Delete(path);
                }
            }
        }
    }

    [Test]
    public void FloatBinaryRejectsIntegerOperands()
    {
        const string source = "public int32 Add(int32 left, int32 right) { return left + right; }";
        var artifact = TrRmExecutableCompiler.Emit(Compile(source, true), "invalid-integer-float-binary");
        var malformed = ReplaceBodyOperationByte(artifact, 1, 4, 0, 0x1D);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }
    [TestCase((byte)(0))]
    [TestCase((byte)(5))]
    public void InvalidFloatingArithmeticKindsAreRejected(byte kind)
    {
        const string source = "public float32 Add(float32 left, float32 right) { return left + right; }";
        var artifact = TrRmExecutableCompiler.Emit(Compile(source, true), "invalid-float-binary");
        var malformed = ReplaceBodyOperationByte(artifact, 1, 0x1D, 2, kind);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Test]
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
        Assert.That(program.EntryPoint, Is.EqualTo(-1));
        Assert.That(program.Functions.Length, Is.EqualTo(7));
        Assert.That(program.Functions[0].ReturnType, Is.EqualTo(PrimitiveType.Bool));
        Assert.That(program.Functions[1].Variables[0].Type, Is.EqualTo(PrimitiveType.Char16));
        Assert.That(program.Functions[5].ReturnType, Is.EqualTo(PrimitiveType.UInt64));
        Assert.That(NativeEmitter.EmitLibrary(program,
            ["B", "C", "I", "U", "L", "W"], NativeRuntimePolicy.FailFast), Is.Not.Empty);
        Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(program,
            ["Hidden"], NativeRuntimePolicy.FailFast));
    }
    [TestCase("int32 Main() { return Helper(); } int32 Helper() { return 42; }",42)]
    [TestCase("int32 Main() { mut int32 x = 1; x += Helper(); return x; } int32 Helper() { return 41; }",42)]
    [TestCase("int32 Main() { return Factorial(5); } int32 Factorial(int32 n) { if (n == 0) { return 1; } else { return n * Factorial(n - 1); } }",120)]
    [TestCase("int32 Main() { return Even(6); } int32 Even(int32 n) { if (n == 0) { return 1; } else { return Odd(n - 1); } } int32 Odd(int32 n) { if (n == 0) { return 0; } else { return Even(n - 1); } }",1)]
    [TestCase("int32 Main() { Observe(); return 42; } void Observe() { }",42)]
    [TestCase("int32 Main() { return Mix(true, char16(66), -3, uint32(4), int64(5), uint64(6)); } int32 Mix(bool b, char16 c, int32 i, uint32 u, int64 l, uint64 w) { if (b) { return int32(c) + i + int32(u) + int32(l) + int32(w); } else { return 0; } }",78)]
    [TestCase("int32 Main() { mut int32 n = 0; while (n < 4) { n = Step(n); } return n + Final(); } int32 Step(int32 n) { return n + 1; } int32 Final() { return 38; }",42)]
    public void LocalCallsIncludeForwardNestedAndFiniteRecursion(string source, int expected)
    {
        var program = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(program, "calls");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(loaded, "source-free"));
    }

    [Test]
    public void VoidCallsAndArgumentSnapshotsSurviveMutationAndLazyFlow()
    {
        const string source = """
            void Observe(int32 value) { }
            int32 Sum(int32 a, int32 b, int32 c, int32 d, int32 e, int32 f) {
                return a * 10 + b + c + d + e + f;
            }
            int32 Main() {
                mut int32 x = 1;
                Observe(x++);
                if (false && (Sum(1, 2, 3, 4, 5, 6) == 0)) { Observe(9); }
                Observe(x);
                return Sum(x++, ++x, 3, 4, 5, 6);
            }
            """;
        var program = Compile(source);
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(program, "void-calls"));
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(loaded.Functions[2].Body.Statements, Is.Empty);
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(loaded, "not-source"));
    }

    [Test]
    public void VoidOperationOrdinalsDoNotExhaustScalarValueInventory()
    {
        var source = "int32 Main() { "
            + string.Concat(Enumerable.Repeat("Observe(); ", 260))
            + "return 42; } void Observe() { }";
        var artifact = TrRmExecutableCompiler.Emit(Compile(source), "void-ordinals");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.That(loaded.Functions[0].Variables, Has.Exactly(1).Items);
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }
    [TestCase(" ",false)]
    [TestCase("[DoesNotReturn] ",true)]
    public void EffectsRoundTripWithoutFabricatedProof(string attributes, bool doesNotReturn)
    {
        var haltBody = doesNotReturn ? "while (true) { }" : string.Empty;
        var source = attributes + $"void Halt() {{ {haltBody} }} int32 Main() {{ return 42; }}";
        var compiled = Compile(source);
        var program = TrRmExecutableCompiler.Load(
            TrRmExecutableCompiler.Emit(compiled, "effects"));
        var function = global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == "Halt");
        Assert.That(function.IsNoThrow, Is.False);
        Assert.That(function.DoesNotReturn, Is.EqualTo(doesNotReturn));
    }

    [Test]
    public void ExecutableWriterRejectsRetiredNoThrowClaims()
    {
        var program = BoundProgramTestHelpers.MarkNoThrow(
            Compile("void Halt() { } int32 Main() { return 42; }"), "Halt");

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(program, "retired-nothrow"));
    }
    [TestCase("out int32 value")]
    public void ExecutableWriterRejectsOutputParameters(string parameter)
    {
        var source = $"int32 Output({parameter}) {{ value = 42; return 0; }} "
            + "int32 Main() { return 42; }";
        var program = Compile(source);

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(program, "output-parameters"));
    }
    [TestCase((byte)(1))]
    [TestCase((byte)(2))]
    public void ExecutableLoaderRejectsOutputModesInUncalledIndexedMethods(byte mode)
    {
        const string source = """
            int32 Helper(int32 value) {
                if (value == 0) { return 1; }
                return value;
            }
            int32 Main() { return 42; }
            """;
        var artifact = TrRmExecutableCompiler.Emit(Compile(source), "output-mode");
        var malformed = ReplaceParameterMode(artifact, mode);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Test]
    public void LoadsMultipleEffectContractsFromOneTableIndex()
    {
        const string source = " unsafe void Unsafe() { } "
            + "[DoesNotReturn] void Halt() { while (true) { } } "
            + "int32 Main() { unsafe { Unsafe(); } return 42; }";
        var compiled = Compile(source);
        var program = TrRmExecutableCompiler.Load(
            TrRmExecutableCompiler.Emit(compiled, "multiple-effects"));
        var unsafeFunction = global::System.Linq.Enumerable.Single(program.Functions,
            static function => function.Name == "Unsafe");
        var halt = global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == "Halt");
        Assert.That(unsafeFunction.IsNoThrow, Is.False);
        Assert.That(unsafeFunction.RequiresUnsafeCaller, Is.True);
        Assert.That(halt.IsNoThrow, Is.False);
        Assert.That(halt.DoesNotReturn, Is.True);
    }
    [TestCase("int32 Main() { return 1 << 2; }",4)]
    [TestCase("int32 Main() { return int32.MaxValue >>> 1; }",1073741823)]
    [TestCase("int32 Main() { mut int32 x = 20; int32 old = x; x = x + 2; return old + x; }",42)]
    [TestCase("int32 Main() { return int32.MinValue % -1; }",0)]
    [TestCase("int32 Main() { return int32(int64.MinValue % -1); }",0)]
    public void FoldedConstantsAndRetainedScalarOperationsMatchBothExistingPolicies(string source, int expected)
    {
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var compilation = Compilation.Create(SyntaxTree.Parse(source), policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
            Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(expected));
            if (policy == BoundRuntimePolicy.FailFast)
            {
                Assert.That(NativeJit.Execute(
                    TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(program, "policies")),
                    NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
            }
            else
            {
                Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(program, "policies"));
            }
        }
    }

    [Test]
    public void ScalarLibraryMatchesDirectNativeBytesAndIndependentExportCalls()
    {
        const string source = """
            public bool B(bool value) { bool result = !value; return result; }
            public char16 C(char16 value) { return value; }
            public int32 I(int32 left, int32 right) { int32 result = left + right; return result; }
            public uint32 U(uint32 left, uint32 right) { uint32 result = left ^ right; return result; }
            public int64 L(int64 left, int64 right) { int64 result = left % right; return result; }
            public uint64 W(uint64 left, uint64 right) { uint64 result = left / right; return result; }
            public float32 F(float32 value) { return value; }
            public float64 D(float64 value) { return value; }
            public void V() { }
            """;
        var original = Compile(source, true);
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(original, "native-parity"));
        string[] names = ["B", "C", "I", "U", "L", "W", "F", "D", "V"];
        var expected = NativeEmitter.EmitLibrary(original, names, NativeRuntimePolicy.FailFast);
        var actual = NativeEmitter.EmitLibrary(loaded, names, NativeRuntimePolicy.FailFast);
        Assert.That(actual, Is.EqualTo(expected));
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"trrm-scalar-{Guid.NewGuid():N}.dll");
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
                Assert.That(boolean(0), Is.EqualTo((byte)1));
                Assert.That(boolean(1), Is.EqualTo((byte)0));
                Assert.That(character(0xD800), Is.EqualTo((ushort)0xD800));
                Assert.That(signed(20, 22), Is.EqualTo(42));
                Assert.That(unsigned(0, uint.MaxValue), Is.EqualTo(uint.MaxValue));
                Assert.That(wideSigned(long.MinValue, -1), Is.EqualTo(0));
                Assert.That(wideUnsigned(ulong.MaxValue, 1), Is.EqualTo(ulong.MaxValue));
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
    private static float SingleFromBits(int bits) => BitConverter.Int32BitsToSingle(bits);
    private static int SingleBits(float value) => BitConverter.SingleToInt32Bits(value);
    private static double DoubleFromBits(long bits) => BitConverter.Int64BitsToDouble(bits);
    private static long DoubleBits(double value) => BitConverter.DoubleToInt64Bits(value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double Float32ToFloat64(float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float Float64ToFloat32(double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float Float32Unary(float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double Float64Unary(double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float Float32Binary(float left, float right);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double Float64Binary(double left, double right);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int32Binary(int left, int right);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float Char16ToFloat32(ushort value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double Char16ToFloat64(ushort value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float64ToInt32(double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Float64ToUInt32(double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Float64ToInt64(double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Float64ToUInt64(double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float32ToInt32(float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Float32ToUInt32(float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Float32ToInt64(float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Float32ToUInt64(float value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VoidAction();
    [TestCase("public void V() { }")]
    [TestCase("public void V() { return; }")]
    public void VoidBodyHasOneTerminatorAfterBinderNormalization(string source)
    {
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(Compile(source, true), "void"));
        Assert.That(loaded.Functions[0].Body.Statements, Has.Exactly(1).Items);
        Assert.That(((global::System.Func<BoundReturn>)(() => { var __nunitActual2 = loaded.Functions[0].Body.Statements[0]; Assert.That(__nunitActual2, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual2!; }))().Value, Is.Null);
    }

    [Test]
    public void RequiredSourceProofsAreDischargedBeforeEmission()
    {
        const string source = """
            [ConstEval] int32 Identity(int32 value) { return value; }
            int32 Main() { static require true; return const Identity(42); }
            """;
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(Compile(source), "proven"));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void SourceFreeIndexedCallsPreserveNamedEvaluationAndFormalOrder()
    {
        const string source = """
            int32 Pair(int32 first, int32 second) { return first * 10 + second; }
            int32 Main() { mut int32 trace = 0; return Pair(second: trace++, first: trace); }
            """;
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "named-arguments");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(10));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(10));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public async Task SourceFreeOptionalScalarBodyExecutesPresenceCopySelectAndAot()
    {
        var body = OptionalLinearBody(
            [
                BuiltinTypeSpecRow(0x01),
                BuiltinTypeSpecRow(0x08),
                BuiltinTypeSpecRow(0x09),
                FirstOptionalTypeSpecRow,
            ],
            [
                1, 1, 3, 0,
                8, 2, 4, 1,
                9, 3, 4,
                3, 4, 2,
                0x0A, 5, 4,
                0x0A, 6, 3,
                7, 7, 5, 4, 3,
                0x0A, 8, 7,
                1, 9, 2, 42,
                1, 10, 2, 0,
                7, 11, 8, 9, 10,
                7, 12, 6, 4, 3,
                0x0A, 13, 12,
                7, 14, 13, 9, 10,
                0x15, 15, 1, 11, 14,
            ],
            15, 15);
        var loaded = TrRmExecutableCompiler.Load(OptionalScalarArtifact(body,
            optionalChar16: false, optionalInt32: false, optionalInt64: true));

        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(await StaticProtocolTests.RunAsync(
            NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase((byte)(0x01),true,42)]
    [TestCase((byte)(0x01),false,0)]
    [TestCase((byte)(0x08),true,42)]
    [TestCase((byte)(0x08),false,0)]
    [TestCase((byte)(0x0D),true,42)]
    [TestCase((byte)(0x0D),false,0)]
    [TestCase((byte)(0x0E),true,42)]
    [TestCase((byte)(0x0E),false,0)]
    public async Task SourceFreeOptionalScalarPayloadTypesPreservePresenceInAotAndJit(
        byte payloadTypeCode, bool present, int expected)
    {
        var payloadTypeSpecRow = BuiltinTypeSpecRow(payloadTypeCode);
        var typeRows = new SortedSet<int> {
            BuiltinTypeSpecRow(0x01),
            BuiltinTypeSpecRow(0x08),
            payloadTypeSpecRow,
            FirstOptionalTypeSpecRow,
        }.ToArray();
        var payloadTypeId = Array.IndexOf(typeRows, payloadTypeSpecRow) + 1;
        var int32TypeId = Array.IndexOf(typeRows, BuiltinTypeSpecRow(0x08)) + 1;
        var optionalTypeId = Array.IndexOf(typeRows, FirstOptionalTypeSpecRow) + 1;
        var operations = new List<byte> { 1, 1, (byte)payloadTypeId, 0 };
        if (present)
        {
            operations.AddRange([8, 2, (byte)optionalTypeId, 1]);
        }
        else
        {
            operations.AddRange([9, 2, (byte)optionalTypeId]);
        }
        operations.AddRange([
            3, 3, 2,
            0x0A, 4, 3,
            1, 5, (byte)int32TypeId, 42,
            1, 6, (byte)int32TypeId, 0,
            7, 7, 4, 5, 6,
        ]);
        var body = OptionalLinearBody(typeRows, [.. operations], 7, 7);
        var loaded = TrRmExecutableCompiler.Load(OptionalScalarArtifact(body,
            optionalChar16: false, optionalInt32: false,
            optionalPayloadTypeSpecRow: payloadTypeSpecRow));

        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(await StaticProtocolTests.RunAsync(
            NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(expected));
    }

    [Test]
    public async Task SourceFreeOptionalChar16EqualityDistinguishesPresenceAndPayload()
    {
        var body = OptionalLinearBody(
            [
                BuiltinTypeSpecRow(0x01),
                BuiltinTypeSpecRow(0x08),
                BuiltinTypeSpecRow(0x03),
                FirstOptionalTypeSpecRow,
            ],
            [
                1, 1, 3, 65,
                1, 2, 3, 66,
                8, 3, 4, 1,
                8, 4, 4, 1,
                8, 5, 4, 2,
                5, 6, 4, 3, 4,
                5, 7, 5, 3, 5,
                9, 8, 4,
                5, 9, 4, 8, 8,
                5, 10, 5, 3, 8,
                1, 11, 2, 42,
                1, 12, 2, 0,
                7, 13, 6, 11, 12,
                7, 14, 7, 11, 12,
                7, 15, 9, 11, 12,
                7, 16, 10, 11, 12,
                0x15, 17, 1, 13, 14,
                0x15, 18, 1, 17, 15,
                0x15, 19, 1, 18, 16,
            ],
            19, 19);
        var loaded = TrRmExecutableCompiler.Load(OptionalScalarArtifact(body,
            optionalChar16: true, optionalInt32: false));

        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(168));
        Assert.That(await StaticProtocolTests.RunAsync(
            NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(168));
    }
    [TestCase(false,168)]
    [TestCase(true,169)]
    public async Task IndexedOptionalChar16EqualityAndSelectExecuteThroughNativeGraph(
        bool selectPresent, int expected)
    {
        var loaded = TrRmExecutableCompiler.Load(OptionalScalarArtifact(
            IndexedOptionalChar16Body(selectPresent),
            optionalChar16: true, optionalInt32: false));

        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(await StaticProtocolTests.RunAsync(
            NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(expected));
    }
    [TestCase(false,1)]
    [TestCase(true,1)]
    public async Task IndexedOptionalChar16ComparisonResultsAreCanonicalInSelect(
        bool selectFirst, int expected)
    {
        var loaded = TrRmExecutableCompiler.Load(OptionalScalarArtifact(
            IndexedOptionalChar16SelectionBody(selectFirst),
            optionalChar16: true, optionalInt32: false));

        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(await StaticProtocolTests.RunAsync(
            NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(expected));
    }
    [TestCase(2,false,0)]
    [TestCase(2,true,42)]
    [TestCase(4,false,0)]
    [TestCase(4,true,42)]
    [TestCase(9,false,0)]
    [TestCase(9,true,42)]
    [TestCase(13,false,0)]
    [TestCase(13,true,42)]
    [TestCase(10,false,0)]
    [TestCase(10,true,42)]
    [TestCase(14,false,0)]
    [TestCase(14,true,42)]
    public async Task IndexedOptionalSelectionPreservesPresenceForEveryPayload(
        int payloadTypeSpecRow, bool selectPresent, int expected)
    {
        var body = IndexedOptionalPresenceBody(
            payloadTypeSpecRow, selectPresent, FirstOptionalTypeSpecRow);
        var artifact = payloadTypeSpecRow switch {
            2 => OptionalScalarArtifact(body, optionalChar16: false, optionalInt32: false,
                optionalBool: true),
            4 => OptionalScalarArtifact(body, optionalChar16: true, optionalInt32: false),
            9 => OptionalScalarArtifact(body, optionalChar16: false, optionalInt32: true),
            13 => OptionalScalarArtifact(body, optionalChar16: false, optionalInt32: false,
                optionalUInt32: true),
            10 => OptionalScalarArtifact(body, optionalChar16: false, optionalInt32: false,
                optionalInt64: true),
            14 => OptionalScalarArtifact(body, optionalChar16: false, optionalInt32: false,
                optionalUInt64: true),
            _ => throw new ArgumentOutOfRangeException(nameof(payloadTypeSpecRow)),
        };
        var loaded = TrRmExecutableCompiler.Load(artifact);

        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(await StaticProtocolTests.RunAsync(
            NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(expected));
    }

    [Test]
    public async Task IndexedOptionalEdgePreservesIndirectPayloadAndPresence()
    {
        var loaded = TrRmExecutableCompiler.Load(OptionalScalarArtifact(
            IndexedOptionalTransferBody(), optionalChar16: false,
            optionalInt32: false, optionalInt64: true));

        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        Assert.That(await StaticProtocolTests.RunAsync(
            NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast)).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public void MaterializedValueLimitRejectsWithoutDroppingOperations()
    {
        var source = "int32 Main() { mut int32 x = 0; "
            + string.Concat(Enumerable.Repeat("x = x + 1; ", 128)) + "return x; }";
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(Compile(source), "too-many-values"));
    }

    [Test]
    public void BodyLocalWidthAndInstructionReferenceWidthWidenIndependently()
    {
        var source = new StringBuilder("int32 Main() { mut int32 x = 0; ");
        for (var i = 0; i < 127; i++)
        {
            source.Append("x = x + 1; ");
        }
        source.Append("x = +x; return x; }");
        var artifact = TrRmExecutableCompiler.Emit(Compile(source.ToString()), "wide-values");
        var index = TrRmArtifactIndex.ForDevelopment(artifact);
        Assert.That(index.TryGetSection(0x20, out var bodyBytes, out var count), Is.True);
        var bodies = new TrRmBodyTableReader(bodyBytes, count);
        Assert.That(bodies.GetRow(1)[0], Is.EqualTo((byte)0x05));
        Assert.That(NativeJit.Execute(TrRmExecutableCompiler.Load(artifact),
            NativeRuntimePolicy.FailFast), Is.EqualTo(127));
    }
    [TestCase(2147483648L)]
    [TestCase(-2147483649L)]
    public void CompactLinearReaderRejectsOutOfRangeInt32ConstSignedPayloads(long value)
    {
        var artifact = OptionalScalarArtifact(SignedInt32LinearBody(value),
            optionalChar16: false, optionalInt32: false);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }
    [TestCase(int.MinValue)]
    [TestCase(int.MaxValue)]
    public void CompactLinearReaderAcceptsInt32ConstSignedExtrema(int value)
    {
        var artifact = OptionalScalarArtifact(SignedInt32LinearBody(value),
            optionalChar16: false, optionalInt32: false);
        var loaded = TrRmExecutableCompiler.Load(artifact);

        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(value));
    }

    [Test]
    public void TamperedPolicyAndUnknownSectionsNeverBecomeExecutable()
    {
        var bytes = TrRmExecutableCompiler.Emit(Compile("int32 Main() { return 42; }"), "tamper");
        var index = TrRmArtifactIndex.ForDevelopment(bytes);
        for (var i = 0; i < index.SectionCount; i++)
        {
            index.GetSection(i, out var kind, out _, out _, out _);
            if (kind != 1)
            {
                continue;
            }
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(32 + i * 16 + 8));
            bytes[offset + 8] = 2;
            Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(bytes));
            bytes[offset + 8] = 0;
            bytes[offset + 4] = 0;
            Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(bytes));
            return;
        }
        throw new InvalidOperationException("Module section missing.");
    }

    private static byte[] SignedInt32LinearBody(long value)
    {
        var dataWidth = value is >= sbyte.MinValue and <= sbyte.MaxValue ? 1
            : value is >= short.MinValue and <= short.MaxValue ? 2
            : value is >= int.MinValue and <= int.MaxValue ? 4 : 8;
        var operations = new List<byte>();
        if (dataWidth == 1)
        {
            operations.Add(2);
        }
        else
        {
            operations.Add(0xF0);
            operations.Add((byte)(dataWidth switch { 2 => 0x10, 4 => 0x20, _ => 0x30 }));
            operations.Add(2);
        }
        operations.Add(1);
        operations.Add(1);
        var payload = new byte[dataWidth];
        switch (dataWidth)
        {
            case 1:
                payload[0] = unchecked((byte)value);
                break;
            case 2:
                BinaryPrimitives.WriteUInt16LittleEndian(payload, unchecked((ushort)value));
                break;
            case 4:
                BinaryPrimitives.WriteUInt32LittleEndian(payload, unchecked((uint)value));
                break;
            default:
                BinaryPrimitives.WriteUInt64LittleEndian(payload, unchecked((ulong)value));
                break;
        }
        operations.AddRange(payload);
        return OptionalLinearBody(
            [BuiltinTypeSpecRow(0x08)], [.. operations], 1, 1);
    }

    private static byte[] OptionalLinearBody(int[] typeRows, byte[] operations,
        int operationCount, int returnValue)
    {
        var body = new List<byte> { 0x04, (byte)typeRows.Length };
        foreach (var row in typeRows)
        {
            var bytes = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)row);
            body.AddRange(bytes);
        }
        body.Add((byte)operationCount);
        body.AddRange(operations);
        body.Add(0x83);
        body.Add((byte)returnValue);
        return body.ToArray();
    }

    private static byte[] IndexedOptionalTransferBody()
    {
        var firstBlock = new byte[]
        {
            0x18, 1, 2, 2, 1, 1, 2, 2, 2,
            1, 1, 1, 0,
            8, 2, 2, 1,
            0x80, 2, 1, 2,
        };
        var secondBlock = new byte[]
        {
            0x12, 1, 3, 5, 3, 0, 4, 3, 5, 3, 6, 3, 7, 4, 4,
            0x0A, 4, 3,
            1, 5, 4, 42,
            1, 6, 4, 0,
            7, 7, 4, 5, 6,
            0x83, 7,
        };
        var body = new List<byte> { 0x08, 2, 7, 0, 4 };
        foreach (var row in new[] {
            BuiltinTypeSpecRow(0x09), FirstOptionalTypeSpecRow,
            BuiltinTypeSpecRow(0x01), BuiltinTypeSpecRow(0x08),
        })
        {
            var bytes = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)row);
            body.AddRange(bytes);
        }
        body.AddRange([1, 1, 2, 1, 2, 1, 3, 1, 4, 1, 4, 1, 4, 1]);
        body.AddRange([0, 2, 7]);
        body.AddRange([9, 13, 0xFF, 15, 18, 22, 26]);
        body.AddRange([0, (byte)firstBlock.Length,
            (byte)(firstBlock.Length + secondBlock.Length)]);
        body.AddRange([17, 31]);
        body.AddRange(firstBlock);
        body.AddRange(secondBlock);
        return body.ToArray();
    }

    private static byte[] IndexedOptionalChar16Body(bool selectPresent)
    {
        byte[] valueTypeIds =
        [
            3, 3, 4, 4, 4, 1, 1, 4, 1, 1,
            2, 2, 2, 2, 2, 2, 2, 2, 2, 1,
            4, 4, 1, 2, 2, 2, 2,
        ];
        byte[] lastUseOrdinals =
        [
            3, 4, 22, 5, 20, 12, 13, 9, 14, 15,
            15, 15, 16, 16, 17, 18, 17, 18, 26, 20,
            21, 22, 25, 25, 25, 26, 27,
        ];
        byte[][] operations =
        [
            [1, 1, 3, 65],
            [1, 2, 3, 66],
            [8, 3, 4, 1],
            [8, 4, 4, 1],
            [8, 5, 4, 2],
            [5, 6, 4, 3, 4],
            [5, 7, 5, 3, 5],
            [9, 8, 4],
            [5, 9, 4, 8, 8],
            [5, 10, 5, 3, 8],
            [1, 11, 2, 42],
            [1, 12, 2, 0],
            [7, 13, 6, 11, 12],
            [7, 14, 7, 11, 12],
            [7, 15, 9, 11, 12],
            [7, 16, 10, 11, 12],
            [0x15, 17, 1, 13, 14],
            [0x15, 18, 1, 17, 15],
            [0x15, 19, 1, 18, 16],
            [1, 20, 1, (byte)(selectPresent ? 1 : 0)],
            [7, 21, 20, 3, 5],
            [3, 22, 21],
            [5, 23, 4, 22, 3],
            [1, 24, 2, 1],
            [1, 25, 2, 0],
            [7, 26, 23, 24, 25],
            [0x15, 27, 1, 19, 26],
        ];
        var block = new List<byte> { 16, (byte)valueTypeIds.Length };
        for (var value = 0; value < valueTypeIds.Length; value++)
        {
            block.Add((byte)(value + 1));
            block.Add(lastUseOrdinals[value]);
        }
        block.Add((byte)operations.Length);
        var operationOffsets = new byte[operations.Length];
        for (var operation = 0; operation < operations.Length; operation++)
        {
            operationOffsets[operation] = (byte)block.Count;
            block.AddRange(operations[operation]);
        }
        var terminatorOffset = (byte)block.Count;
        block.Add(0x83);
        block.Add(27);

        var body = new List<byte> { 0x08, 1, (byte)valueTypeIds.Length, 0, 4 };
        foreach (var row in new[] {
            BuiltinTypeSpecRow(0x01), BuiltinTypeSpecRow(0x08),
            BuiltinTypeSpecRow(0x03), FirstOptionalTypeSpecRow,
        })
        {
            var bytes = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)row);
            body.AddRange(bytes);
        }
        foreach (var typeId in valueTypeIds)
        {
            body.Add(typeId);
            body.Add(1);
        }
        body.AddRange([0, (byte)valueTypeIds.Length]);
        body.AddRange(operationOffsets);
        body.AddRange([0, (byte)block.Count, terminatorOffset]);
        body.AddRange(block);
        return body.ToArray();
    }

    private static byte[] IndexedOptionalChar16SelectionBody(bool selectFirst)
    {
        byte[] valueTypeIds = [3, 3, 4, 4, 1, 4, 1, 1, 2, 2, 2, 2, 2];
        byte[] lastUseOrdinals = [2, 3, 7, 5, 5, 7, 10, 11, 11, 11, 12, 12, 13];
        byte[][] operations =
        [
            [1, 1, 3, 65],
            [1, 2, 3, 66],
            [8, 3, 4, 1],
            [8, 4, 4, 2],
            [1, 5, 1, (byte)(selectFirst ? 1 : 0)],
            [7, 6, 5, 3, 4],
            [5, 7, 4, 6, 3],
            [5, 8, 5, 6, 3],
            [1, 9, 2, 1],
            [1, 10, 2, 0],
            [7, 11, 7, 9, 10],
            [7, 12, 8, 9, 10],
            [0x15, 13, 1, 11, 12],
        ];
        var block = new List<byte> { 16, (byte)valueTypeIds.Length };
        for (var value = 0; value < valueTypeIds.Length; value++)
        {
            block.Add((byte)(value + 1));
            block.Add(lastUseOrdinals[value]);
        }
        block.Add((byte)operations.Length);
        var operationOffsets = new byte[operations.Length];
        for (var operation = 0; operation < operations.Length; operation++)
        {
            operationOffsets[operation] = (byte)block.Count;
            block.AddRange(operations[operation]);
        }
        var terminatorOffset = (byte)block.Count;
        block.Add(0x83);
        block.Add(13);

        var body = new List<byte> { 0x08, 1, (byte)valueTypeIds.Length, 0, 4 };
        foreach (var row in new[] {
            BuiltinTypeSpecRow(0x01), BuiltinTypeSpecRow(0x08),
            BuiltinTypeSpecRow(0x03), FirstOptionalTypeSpecRow,
        })
        {
            var bytes = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)row);
            body.AddRange(bytes);
        }
        foreach (var typeId in valueTypeIds)
        {
            body.Add(typeId);
            body.Add(1);
        }
        body.AddRange([0, (byte)valueTypeIds.Length]);
        body.AddRange(operationOffsets);
        body.AddRange([0, (byte)block.Count, terminatorOffset]);
        body.AddRange(block);
        return body.ToArray();
    }

    private static byte[] IndexedOptionalPresenceBody(int payloadTypeSpecRow,
        bool selectPresent, int optionalTypeSpecRow)
    {
        var booleanTypeSpecRow = BuiltinTypeSpecRow(0x01);
        var integerTypeSpecRow = BuiltinTypeSpecRow(0x08);
        var typeRows = new SortedSet<int> {
            booleanTypeSpecRow, integerTypeSpecRow, payloadTypeSpecRow, optionalTypeSpecRow,
        }.ToArray();
        var booleanTypeId = (byte)(Array.IndexOf(typeRows, booleanTypeSpecRow) + 1);
        var integerTypeId = (byte)(Array.IndexOf(typeRows, integerTypeSpecRow) + 1);
        var payloadTypeId = (byte)(Array.IndexOf(typeRows, payloadTypeSpecRow) + 1);
        var optionalTypeId = (byte)(Array.IndexOf(typeRows, optionalTypeSpecRow) + 1);
        byte[] valueTypeIds =
        [
            payloadTypeId, optionalTypeId, optionalTypeId, booleanTypeId, optionalTypeId,
            optionalTypeId, booleanTypeId, integerTypeId, integerTypeId, integerTypeId,
        ];
        byte[] lastUseOrdinals = [1, 4, 4, 4, 5, 6, 9, 9, 9, 10];
        byte[][] operations =
        [
            [1, 1, payloadTypeId, 0],
            [8, 2, optionalTypeId, 1],
            [9, 3, optionalTypeId],
            [1, 4, booleanTypeId, (byte)(selectPresent ? 1 : 0)],
            [7, 5, 4, 2, 3],
            [3, 6, 5],
            [0x0A, 7, 6],
            [1, 8, integerTypeId, 42],
            [1, 9, integerTypeId, 0],
            [7, 10, 7, 8, 9],
        ];
        var block = new List<byte> { 16, (byte)valueTypeIds.Length };
        for (var value = 0; value < valueTypeIds.Length; value++)
        {
            block.Add((byte)(value + 1));
            block.Add(lastUseOrdinals[value]);
        }
        block.Add((byte)operations.Length);
        var operationOffsets = new byte[operations.Length];
        for (var operation = 0; operation < operations.Length; operation++)
        {
            operationOffsets[operation] = (byte)block.Count;
            block.AddRange(operations[operation]);
        }
        var terminatorOffset = (byte)block.Count;
        block.Add(0x83);
        block.Add(10);

        var body = new List<byte> { 0x08, 1, (byte)valueTypeIds.Length, 0, (byte)typeRows.Length };
        foreach (var row in typeRows)
        {
            var bytes = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)row);
            body.AddRange(bytes);
        }
        foreach (var typeId in valueTypeIds)
        {
            body.Add(typeId);
            body.Add(1);
        }
        body.AddRange([0, (byte)valueTypeIds.Length]);
        body.AddRange(operationOffsets);
        body.AddRange([0, (byte)block.Count, terminatorOffset]);
        body.AddRange(block);
        return body.ToArray();
    }

    private static byte[] OptionalScalarArtifact(byte[] body, bool optionalChar16,
        bool optionalInt32, bool optionalInt64 = false, bool optionalBool = false,
        bool optionalUInt32 = false, bool optionalUInt64 = false, bool optionalFloat32 = false,
        int optionalPayloadTypeSpecRow = 0)
    {
        var original = TrRmExecutableCompiler.Emit(
            Compile("int32 Main() { return 0; }"), "optional-body");
        var index = TrRmArtifactIndex.ForDevelopment(original);
        var rows = new List<ReadOnlyMemory<byte>>();
        index.TryGetSection(0x11, out var typeBytes, out var typeCount);
        var existingTypes = new TrRmTypeSpecTableReader(typeBytes, typeCount, 0, 0, 0, 0);
        for (var row = 1; row <= typeCount; row++)
        {
            rows.Add(existingTypes.GetRow(row).ToArray());
        }
        if (optionalPayloadTypeSpecRow != 0)
        {
            rows.Add(TrRmTypeSpecTableWriter.Wrapped(
                0x06, (uint)optionalPayloadTypeSpecRow));
        }
        if (optionalChar16)
        {
            rows.Add(TrRmTypeSpecTableWriter.Wrapped(
                0x06, (uint)BuiltinTypeSpecRow(0x03)));
        }
        if (optionalInt32)
        {
            rows.Add(TrRmTypeSpecTableWriter.Wrapped(
                0x06, (uint)BuiltinTypeSpecRow(0x08)));
        }
        if (optionalInt64)
        {
            rows.Add(TrRmTypeSpecTableWriter.Wrapped(
                0x06, (uint)BuiltinTypeSpecRow(0x09)));
        }
        if (optionalBool)
        {
            rows.Add(TrRmTypeSpecTableWriter.Wrapped(
                0x06, (uint)BuiltinTypeSpecRow(0x01)));
        }
        if (optionalUInt32)
        {
            rows.Add(TrRmTypeSpecTableWriter.Wrapped(
                0x06, (uint)BuiltinTypeSpecRow(0x0D)));
        }
        if (optionalUInt64)
        {
            rows.Add(TrRmTypeSpecTableWriter.Wrapped(
                0x06, (uint)BuiltinTypeSpecRow(0x0E)));
        }
        if (optionalFloat32)
        {
            rows.Add(TrRmTypeSpecTableWriter.Builtin(0x14));
            rows.Add(TrRmTypeSpecTableWriter.Wrapped(0x06, (uint)rows.Count));
        }
        var rewrittenTypes = TrRmTypeSpecTableWriter.Encode(rows, 0, 0, 0, 0);
        var rewrittenBody = TrRmVariableTableWriter.EncodeBodies([body]);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags, out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags, (uint)(kind switch {
                0x11 => rows.Count,
                _ => count,
            }), kind switch {
                0x11 => rewrittenTypes,
                0x20 => rewrittenBody,
                _ => bytes.ToArray(),
            }));
        }
        return TrRmArtifactWriter.EncodeDevelopment(sections);
    }

    private static int BuiltinTypeSpecRow(byte typeCode)
    {
        var index = Array.IndexOf(s_nonFloatingBuiltinTypeCodes, typeCode);
        return index >= 0 ? index + 1
            : throw new ArgumentOutOfRangeException(nameof(typeCode));
    }

    private static byte[] ReplaceBodyRow(byte[] artifact, int destinationRow, int sourceRow)
    {
        var index = TrRmArtifactIndex.ForDevelopment(artifact);
        Assert.That(index.TryGetSection(0x20, out var bodyBytes, out var bodyCount), Is.True);
        var bodies = new TrRmBodyTableReader(bodyBytes, bodyCount);
        var bodyRows = new ReadOnlyMemory<byte>[bodyCount];
        for (var row = 1; row <= bodyCount; row++)
        {
            bodyRows[row - 1] = bodies.GetRow(row).ToArray();
        }
        bodyRows[destinationRow - 1] = bodies.GetRow(sourceRow).ToArray();
        var rewrittenBodies = TrRmVariableTableWriter.EncodeBodies(bodyRows);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags, out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags, (uint)count,
                kind == 0x20 ? rewrittenBodies : bytes.ToArray()));
        }
        return TrRmArtifactWriter.EncodeDevelopment(sections);
    }

    private static byte[] SetScalarInitOutputRuleRevision(byte[] artifact, uint revision)
    {
        var index = TrRmArtifactIndex.ForDevelopment(artifact);
        Assert.That(index.TryGetSection(0x0004, out var featureBytes, out var featureCount), Is.True);
        Assert.That(featureCount, Is.EqualTo(1));
        var features = featureBytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(features.AsSpan(4), revision);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags, out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags, (uint)count,
                kind == 0x0004 ? features : bytes.ToArray()));
        }

        return TrRmArtifactWriter.EncodeDevelopment(sections);
    }

    private static byte[] SetConditionalInheritedPlaceState(byte[] artifact,
        int blockIndex, bool initialized, int placeId = 1)
    {
        var index = TrRmArtifactIndex.ForDevelopment(artifact);
        Assert.That(index.TryGetSection(0x20, out var bodyBytes, out var bodyCount), Is.True);
        var bodies = new TrRmBodyTableReader(bodyBytes, bodyCount);
        var bodyRows = new ReadOnlyMemory<byte>[bodyCount];
        for (var row = 1; row <= bodyCount; row++)
        {
            bodyRows[row - 1] = bodies.GetRow(row).ToArray();
        }

        var found = false;
        for (var row = 0; row < bodyRows.Length && !found; row++)
        {
            var body = bodyRows[row].ToArray();
            if (body.Length < 2 || body[0] > 2)
            {
                continue;
            }

            var width = 1 << body[0];
            var position = 1;
            if (ReadLocal(body, ref position, width) != 3)
            {
                continue;
            }

            var valueCount = ReadLocal(body, ref position, width);
            var placeCount = ReadLocal(body, ref position, width);
            _ = ReadLocal(body, ref position, width);
            var typeCount = ReadLocal(body, ref position, width);
            position += checked(typeCount * 4);
            position += checked(placeCount * (3 * width + 1));
            position += checked(valueCount * (2 * width + 1));
            position += checked((4 * width) + (valueCount * 4));
            var blockOffsetsPosition = position;
            var blockDataStart = checked(blockOffsetsPosition + (4 * 4) + (3 * 4));
            var blockOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                body.AsSpan(blockOffsetsPosition + blockIndex * 4)));
            position = checked(blockDataStart + blockOffset);
            _ = ReadLocal(body, ref position, width);
            SkipIndexList(body, ref position, width);
            SkipIndexList(body, ref position, width);
            SkipIndexList(body, ref position, width);
            var inheritedCount = ReadLocal(body, ref position, width);
            if (placeId <= 0 || inheritedCount < placeId)
            {
                throw new InvalidOperationException("Conditional block has no requested inherited place.");
            }

            position += checked((placeId - 1) * (width + 1) + width);
            body[position] = initialized ? (byte)1 : (byte)0;
            bodyRows[row] = body;
            found = true;
        }

        Assert.That(found, Is.True, "No three-block output Body was found.");
        var rewrittenBodies = TrRmVariableTableWriter.EncodeBodies(bodyRows);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags, out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags, (uint)count,
                kind == 0x20 ? rewrittenBodies : bytes.ToArray()));
        }
        return TrRmArtifactWriter.EncodeDevelopment(sections);

        static int ReadLocal(byte[] body, ref int position, int width)
        {
            var result = width switch {
                1 => body[position],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(position)),
                4 => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(position))),
                _ => throw new InvalidOperationException("Unsupported local width."),
            };
            position += width;
            return result;
        }

        static void SkipIndexList(byte[] body, ref int position, int width)
        {
            var count = ReadLocal(body, ref position, width);
            position += checked(count * width);
        }
    }

    private static (int WidthCode, int OperationBytes) LongestSingleBlockOperationSpan(
        byte[] artifact)
    {
        var index = TrRmArtifactIndex.ForDevelopment(artifact);
        Assert.That(index.TryGetSection(0x20, out var bodyBytes, out var bodyCount), Is.True);
        var bodies = new TrRmBodyTableReader(bodyBytes, bodyCount);
        byte[]? body = null;
        for (var row = 1; row <= bodyCount; row++)
        {
            var candidate = bodies.GetRow(row).ToArray();
            if (body is null || candidate.Length > body.Length)
            {
                body = candidate;
            }
        }
        if (body is null)
        {
            throw new InvalidOperationException("The artifact has no Body rows.");
        }

        var widthCode = body[0];
        if (widthCode > 2)
        {
            throw new InvalidOperationException("The Body has an unsupported local width.");
        }

        var width = 1 << widthCode;
        var position = 1;
        if (ReadLocal(body, ref position, width) != 1)
        {
            throw new InvalidOperationException("The longest Body is not a single block.");
        }

        var valueCount = ReadLocal(body, ref position, width);
        var placeCount = ReadLocal(body, ref position, width);
        _ = ReadLocal(body, ref position, width);
        var typeCount = ReadLocal(body, ref position, width);
        position += checked(typeCount * 4);
        position += checked(placeCount * (3 * width + 1));
        position += checked(valueCount * (2 * width + 1));
        if (ReadLocal(body, ref position, width) != 0
            || ReadLocal(body, ref position, width) != valueCount)
        {
            throw new InvalidOperationException("The Body's value inventory is malformed.");
        }

        position += checked(valueCount * 4);
        if (BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(position)) != 0)
        {
            throw new InvalidOperationException("The Body block offset is not zero.");
        }
        position += 4;
        var blockLength = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(position));
        position += 4;
        var terminatorOffset = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(position));
        position += 4;
        if (blockLength != body.Length - position)
        {
            throw new InvalidOperationException("The Body block extent is malformed.");
        }

        var blockStart = position;
        if (ReadLocal(body, ref position, width) != 0)
        {
            throw new InvalidOperationException("The Body entry block has a nonzero offset.");
        }
        var entryCount = ReadLocal(body, ref position, width);
        position += checked(entryCount * width);
        for (var listIndex = 0; listIndex < 3; listIndex++)
        {
            if (ReadLocal(body, ref position, width) != 0)
            {
                throw new InvalidOperationException("The Body has unexpected block inputs.");
            }
        }

        SkipIndexList(body, ref position, width);
        SkipIndexList(body, ref position, width);
        SkipLastUses(body, ref position, width, valueCount);
        SkipLastUses(body, ref position, width, placeCount);
        _ = ReadLocal(body, ref position, width);
        var operationOffset = checked((uint)(position - blockStart));
        return (widthCode, checked((int)(terminatorOffset - operationOffset)));

        static int ReadLocal(byte[] bytes, ref int offset, int localWidth)
        {
            var result = localWidth switch {
                1 => bytes[offset],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset)),
                4 => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset))),
                _ => throw new InvalidOperationException("Unsupported local width."),
            };
            offset += localWidth;
            return result;
        }

        static void SkipIndexList(byte[] bytes, ref int offset, int localWidth)
        {
            var count = ReadLocal(bytes, ref offset, localWidth);
            offset += checked(count * localWidth);
        }

        static void SkipLastUses(byte[] bytes, ref int offset, int localWidth, int maximum)
        {
            var count = ReadLocal(bytes, ref offset, localWidth);
            if (count > maximum)
            {
                throw new InvalidOperationException("The Body has too many last-use entries.");
            }
            offset += checked(count * 2 * localWidth);
        }
    }

    private static uint ScalarInitOutputRevision(byte[] artifact)
    {
        var index = TrRmArtifactIndex.ForDevelopment(artifact);
        Assert.That(index.TryGetSection(4, out var featureBytes, out var featureCount), Is.True);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount);
        Assert.That(features.TryGetFeature(1,
            out var feature), Is.True);
        return feature.RuleRevision;
    }

    private static byte[] ReplaceParameterMode(byte[] artifact, byte mode)
    {
        var index = TrRmArtifactIndex.ForDevelopment(artifact);
        Assert.That(index.TryGetSection(0x18, out var parameterBytes, out var parameterCount), Is.True);
        Assert.That(parameterCount, Is.EqualTo(1));
        var parameters = parameterBytes.ToArray();
        parameters[12] = mode;
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags, out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags, (uint)count,
                kind == 0x18 ? parameters : bytes.ToArray()));
        }
        return TrRmArtifactWriter.EncodeDevelopment(sections);
    }

    private static byte[] ReplaceBodyConstant(byte[] artifact, int bodyRow,
        byte[] originalBits, byte[] replacementBits)
    {
        Assert.That(replacementBits.Length, Is.EqualTo(originalBits.Length));
        var index = TrRmArtifactIndex.ForDevelopment(artifact);
        Assert.That(index.TryGetSection(0x20, out var bodyBytes, out var bodyCount), Is.True);
        var bodies = new TrRmBodyTableReader(bodyBytes, bodyCount);
        var bodyRows = new ReadOnlyMemory<byte>[bodyCount];
        for (var row = 1; row <= bodyCount; row++)
        {
            bodyRows[row - 1] = bodies.GetRow(row).ToArray();
        }

        var body = bodyRows[bodyRow - 1].ToArray();
        var offset = body.AsSpan().IndexOf(originalBits);
        Assert.That(offset >= 0, Is.True);
        replacementBits.CopyTo(body.AsSpan(offset));
        bodyRows[bodyRow - 1] = body;
        var rewrittenBodies = TrRmVariableTableWriter.EncodeBodies(bodyRows);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags, out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags, (uint)count,
                kind == 0x20 ? rewrittenBodies : bytes.ToArray()));
        }
        return TrRmArtifactWriter.EncodeDevelopment(sections);
    }

    private static byte[] ReplaceBodyOperationByte(byte[] artifact, int bodyRow,
        byte opcode, int fieldOffset, byte replacement)
    {
        var index = TrRmArtifactIndex.ForDevelopment(artifact);
        Assert.That(index.TryGetSection(0x20, out var bodyBytes, out var bodyCount), Is.True);
        var bodies = new TrRmBodyTableReader(bodyBytes, bodyCount);
        var bodyRows = new ReadOnlyMemory<byte>[bodyCount];
        for (var row = 1; row <= bodyCount; row++)
        {
            bodyRows[row - 1] = bodies.GetRow(row).ToArray();
        }

        var body = bodyRows[bodyRow - 1].ToArray();
        var opcodeOffset = body.AsSpan().IndexOf(opcode);
        Assert.That(opcodeOffset >= 0, Is.True);
        body[opcodeOffset + fieldOffset] = replacement;
        bodyRows[bodyRow - 1] = body;
        var rewrittenBodies = TrRmVariableTableWriter.EncodeBodies(bodyRows);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags, out var bytes, out var count);
            sections.Add(new TrRmSection(kind, flags, (uint)count,
                kind == 0x20 ? rewrittenBodies : bytes.ToArray()));
        }
        return TrRmArtifactWriter.EncodeDevelopment(sections);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunTrRmAsync(string artifact)
    {
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Test configuration directory is missing.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var cli = Path.Combine(root, "artifacts", "bin", "sources", "terrumc",
            configuration, frameworkDirectory.Name, "terrumc.dll");
        var start = new ProcessStartInfo("dotnet") {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(cli);
        start.ArgumentList.Add("run-trrm");
        start.ArgumentList.Add(artifact);
        start.ArgumentList.Add("--runtime");
        start.ArgumentList.Add("fail-fast");
        start.ArgumentList.Add("--json");
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("TRRM runner process was not started.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        return (process.ExitCode, await output, await error);
    }
}
