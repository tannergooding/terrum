// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class NativeWidthScalarTests
{
    [Test]
    public void BindsDistinctNativeWidthIntegerIdentities()
    {
        var signed = Bind("public nint Value() { return 1; }");
        var unsigned = Bind("public nuint Value() { return 1; }");

        Assert.That(signed.Functions[0].ReturnType,
            Is.EqualTo(PrimitiveType.NativeInt));
        Assert.That(unsigned.Functions[0].ReturnType,
            Is.EqualTo(PrimitiveType.NativeUInt));
        Assert.That(PrimitiveType.NativeInt,
            Is.Not.EqualTo(PrimitiveType.Int64));
        Assert.That(PrimitiveType.NativeUInt,
            Is.Not.EqualTo(PrimitiveType.UInt64));
    }

    [Test]
    public void UsesSelectedWindowsX64WidthAndLayout()
    {
        Assert.That(PrimitiveTypeFacts.IntegerWidth(PrimitiveType.NativeInt),
            Is.EqualTo(NativeTargetProfile.PointerSize * 8));
        Assert.That(PrimitiveTypeFacts.IntegerWidth(PrimitiveType.NativeUInt),
            Is.EqualTo(NativeTargetProfile.PointerSize * 8));
        Assert.That(ValueLayout.Size(PrimitiveType.NativeInt, []),
            Is.EqualTo((ulong)NativeTargetProfile.PointerSize));
        Assert.That(ValueLayout.Alignment(PrimitiveType.NativeUInt, []),
            Is.EqualTo(NativeTargetProfile.PointerSize));
        Assert.That(PrimitiveTypeFacts.IsSignedInteger(
            PrimitiveType.NativeInt), Is.True);
        Assert.That(PrimitiveTypeFacts.IsSignedInteger(
            PrimitiveType.NativeUInt), Is.False);
    }

    [Test]
    public void BindsSameIdentityNativeWidthArithmeticAndRejectsFixedWidthMixing()
    {
        var signed = Bind(
            "public nint Add(nint left, nint right) { return left + right; }");
        var unsigned = Bind(
            "public nuint Add(nuint left, nuint right) { return left + right; }");
        var mixed = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public nint Add(nint left, int64 right) { return left + right; }"));

        Assert.That(signed.Functions[0].ReturnType,
            Is.EqualTo(PrimitiveType.NativeInt));
        Assert.That(unsigned.Functions[0].ReturnType,
            Is.EqualTo(PrimitiveType.NativeUInt));
        Assert.That(mixed.HasErrors, Is.True);
    }

    [Test]
    public void EmitsAndExecutesNativeWidthArithmetic()
    {
        if (!OperatingSystem.IsWindows()
            || RuntimeInformation.ProcessArchitecture
                != Architecture.X64)
        {
            Assert.Ignore("Native x64 execution requires Windows x64.");
        }

        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                nint signed = 20;
                nuint unsigned = 22;
                return int32(signed + nint(unsigned));
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void StandardRegisterBodyRoundTripsNativeWidthIntegerIdentities()
    {
        const string schemaIdentity
            = "trrm/1-draft-layout-queries-1";
        const uint targetRequirement
            = TrRmModuleTableReader.WindowsX64NativeTargetRequirement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            nint Add(nint left, nint right) { return left + right; }
            nuint Half(nuint value) { return value / nuint(2); }
            int32 Main() {
                return int32(Add(nint(20), nint(22)) + nint(Half(nuint(22))));
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, "native-width", schemaIdentity,
            targetRequirement);
        var loaded = TrRmExecutableCompiler.Load(artifact);

        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast),
            Is.EqualTo(53));
    }

    [Test]
    public async Task PrivateNativeModuleRoundTripsNativeWidthSignaturesAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public nint Add(nint left, nint right) { return left + right; }
            public nuint Half(nuint value) { return value / nuint(2); }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(
            providerCompilation.Program!, ["Add", "Half"], "NativeWidth",
            NativeRuntimePolicy.FailFast);

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                return int32(Add(nint(20), nint(22))
                    + nint(Half(nuint(22))));
            }
            """), NativeModuleCompiler.CreateReference(provider),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, provider, NativeRuntimePolicy.FailFast);

        Assert.That(await NativeModuleCompiler.RunAsync(consumer, provider,
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast)
            .ConfigureAwait(true), Is.EqualTo(53));
    }

    [Test]
    public void ExecutesNativeWidthSignedAndUnsignedShifts()
    {
        if (!OperatingSystem.IsWindows()
            || RuntimeInformation.ProcessArchitecture
                != Architecture.X64)
        {
            Assert.Ignore("Native x64 execution requires Windows x64.");
        }

        Assert.That(ExecuteMain("""
            int32 Main() {
                nint value = -9223372036854775808;
                nint count = 63;
                return int32(value >> count);
            }
            """), Is.EqualTo(-1));
        Assert.That(ExecuteMain("""
            int32 Main() {
                nuint value = 9223372036854775808;
                nuint count = 63;
                return int32(value >>> count);
            }
            """), Is.EqualTo(1));
    }

    [Test]
    public void ExecutesNativeWidthDivisionRemainderComparisonsAndConversions()
    {
        if (!OperatingSystem.IsWindows()
            || RuntimeInformation.ProcessArchitecture
                != Architecture.X64)
        {
            Assert.Ignore("Native x64 execution requires Windows x64.");
        }

        Assert.That(ExecuteMain("""
            nint FromInt64(int64 value) { return nint(value); }
            int64 ToInt64(nint value) { return int64(value); }
            nuint FromUInt64(uint64 value) { return nuint(value); }
            uint64 ToUInt64(nuint value) { return uint64(value); }
            float64 ToFloat64(nuint value) { return float64(value); }
            nint FromFloat64(float64 value) { return nint(value); }
            nuint FromUnsignedFloat64(float64 value) { return nuint(value); }
            int128 ToInt128(nint value) { return int128(value); }
            uint128 ToUInt128(nuint value) { return uint128(value); }

            int32 Main() {
                nint signed = nint(-9);
                nuint unsigned = nuint(9223372036854775808);
                if (signed / nint(2) != nint(-4)) { return 1; }
                if (signed % nint(2) != nint(-1)) { return 2; }
                if (unsigned / nuint(2) != nuint(4611686018427387904)) { return 3; }
                if (unsigned % nuint(3) != nuint(2)) { return 4; }
                if (!(signed < nint(0)) || !(unsigned > nuint(1))) { return 5; }
                if (FromInt64(-23) != nint(-23) || ToInt64(signed) != -9) { return 6; }
                if (FromUInt64(uint64.MaxValue) != nuint.MaxValue
                    || ToUInt64(unsigned) != 9223372036854775808) { return 7; }
                if (ToFloat64(nuint.MaxValue) != float64(uint64.MaxValue)) { return 8; }
                if (FromFloat64(-23.9) != nint(-23)
                    || FromUnsignedFloat64(42.9) != nuint(42)) { return 9; }
                if (ToInt128(nint.MinValue) != int128(-9223372036854775808)
                    || ToUInt128(unsigned) != uint128(9223372036854775808)) {
                    return 10;
                }
                return 42;
            }
            """), Is.EqualTo(42));
    }

    [Test]
    public void BindsNativeWidthSignedMinimumAndUnsignedMaximum()
    {
        var signed = Bind(
            "public nint Value() { return -9223372036854775808; }");
        var unsigned = Bind(
            "public nuint Value() { return 18446744073709551615; }");
        var signedReturn = (BoundReturn)signed.Functions[0].Body
            .Statements[0];
        var unsignedReturn = (BoundReturn)unsigned.Functions[0]
            .Body.Statements[0];

        Assert.That(signedReturn.Value, Is.TypeOf<BoundConstant>());
        Assert.That(((BoundConstant)signedReturn.Value!).Type,
            Is.EqualTo(PrimitiveType.NativeInt));
        Assert.That(((BoundConstant)signedReturn.Value).Value,
            Is.EqualTo(long.MinValue));
        Assert.That(unsignedReturn.Value, Is.TypeOf<BoundConstant>());
        Assert.That(((BoundConstant)unsignedReturn.Value!).Type,
            Is.EqualTo(PrimitiveType.NativeUInt));
        Assert.That(((BoundConstant)unsignedReturn.Value).Bits.Low,
            Is.EqualTo(ulong.MaxValue));
    }

    [Test]
    public void MapsAssignedStandardTypeSpecScalarCodes()
    {
        Assert.That(TrRmConstantSourceValueValidator.TryGetBoundScalarType(
            0x10, out var signed), Is.True);
        Assert.That(signed, Is.EqualTo(PrimitiveType.NativeInt));
        Assert.That(TrRmConstantSourceValueValidator.TryGetBoundScalarType(
            0x11, out var unsigned), Is.True);
        Assert.That(unsigned, Is.EqualTo(PrimitiveType.NativeUInt));
    }

    [Test]
    public void NativeImportTypeSpecRetainsNativeWidthScalarCodes()
    {
        ReadOnlyMemory<byte>[] typeRows =
        [
            TrRmTypeSpecTableWriter.Builtin(0x10),
            TrRmTypeSpecTableWriter.Builtin(0x11),
        ];
        var typeBytes = TrRmTypeSpecTableWriter.Encode(typeRows, 0, 0, 0);
        var types = new TrRmTypeSpecTableReader(typeBytes, typeRows.Length,
            0, 0, 0);

        Assert.That(TrRmNativeImportTypeContract.TryGetDirectNativeImportResultType(
            types, 1, out var signed), Is.True);
        Assert.That(signed, Is.EqualTo(PrimitiveType.NativeInt));
        Assert.That(TrRmNativeImportTypeContract.TryGetDirectNativeImportResultType(
            types, 2, out var unsigned), Is.True);
        Assert.That(unsigned, Is.EqualTo(PrimitiveType.NativeUInt));
    }

    private static BoundProgram Bind(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return compilation.Program!;
    }

    private static int ExecuteMain(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast);
    }
}
