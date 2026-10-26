// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

public sealed partial class JitTests
{
    [Test]
    public async Task NarrowIntegerConversionsPreserveValuesAcrossAotAndJitAsync()
    {
        const string source = """
            int128 WidenCodeUnit(char16 value) { return int128(value); }
            uint128 WidenUnsignedCodeUnit(char16 value) { return uint128(value); }
            int128 WidenSigned(int64 value) { return int128(value); }
            uint128 WidenUnsigned(uint64 value) { return uint128(value); }
            [ConstEval] int128 WidenConstant(char16 value) { return int128(value); }
            [ConstEval] uint128 WidenUnsignedConstant(char16 value) { return uint128(value); }
            [ConstEval] int8 NarrowConstant(int32 value) { return int8(value); }
            int32 Main() {
                const int128 constant = WidenConstant(char16(65535));
                const uint128 unsignedConstant = WidenUnsignedConstant(char16(65535));
                const int8 narrowConstant = NarrowConstant(127);
                _ = constant;
                _ = unsignedConstant;
                int16 signExtended = int16(int8(-128));
                uint16 zeroExtended = uint16(uint8(255));
                int32 signedWide = int32(int16(-32768));
                uint32 unsignedWide = uint32(uint16(65535));
                int64 signedLong = int64(int8(-128));
                uint64 unsignedLong = uint64(uint8(255));
                char16 codeUnit = char16(uint8(255));
                float32 signedFloat = float32(int8(-3));
                float64 unsignedFloat = float64(uint16(65535));
                float32 codeUnitFloat32 = float32(char16(65535));
                float64 codeUnitFloat64 = float64(char16(65535));
                _ = WidenCodeUnit(char16(65535));
                _ = WidenUnsignedCodeUnit(char16(65535));
                _ = WidenSigned(int64(-1));
                _ = WidenUnsigned(uint64.MaxValue);
                if (signExtended != int16(-128) || zeroExtended != uint16(255)
                    || signedWide != -32768 || unsignedWide != 65535
                    || signedLong != -128 || unsignedLong != 255
                    || codeUnit != char16(255) || signedFloat != float32(-3)
                    || unsignedFloat != float64(65535)
                    || codeUnitFloat32 != float32(65535)
                    || codeUnitFloat64 != float64(65535)
                    || int32(narrowConstant) != 127) { return 1; }
                return 42;
            }
            """;
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    [Test]
    public async Task NarrowConversionFailuresDoNotCommitDestinationsAcrossAotAndJitAsync()
    {
        const string source = """
            int32 Main() {
                mut int8 signed = int8(7);
                mut uint8 unsigned = uint8(8);
                mut uint16 wideUnsigned = uint16(9);
                mut char16 codeUnit = char16(10);
                mut int8 codeUnitSigned = int8(11);
                mut uint8 codeUnitUnsigned = uint8(12);
                mut int16 codeUnitWideSigned = int16(13);
                mut uint64 wideTarget = uint64(14);
                uint16 unsignedWideSource = uint16(32768);
                uint64 unsignedLongSource = uint64.MaxValue;
                mut int16 unsignedSignedTarget = int16(15);
                mut int64 unsignedLongTarget = int64(16);
                mut int32 failures = 0;
                unsafe {
                    try { signed = int8(int16(128)); }
                    catch { if (signed != int8(7)) { return 1; } failures += 1; }
                    try { unsigned = uint8(int16(-1)); }
                    catch { if (unsigned != uint8(8)) { return 2; } failures += 1; }
                    try { wideUnsigned = uint16(uint32(65536)); }
                    catch { if (wideUnsigned != uint16(9)) { return 3; } failures += 1; }
                    try { codeUnit = char16(int64(-1)); }
                    catch { if (codeUnit != char16(10)) { return 4; } failures += 1; }
                    try { uint128 invalid = uint128(int64(-1)); } catch { failures += 1; }
                    try { codeUnitSigned = int8(char16(128)); }
                    catch { if (codeUnitSigned != int8(11)) { return 5; } failures += 1; }
                    try { codeUnitUnsigned = uint8(char16(256)); }
                    catch { if (codeUnitUnsigned != uint8(12)) { return 6; } failures += 1; }
                    try { codeUnitWideSigned = int16(char16(32768)); }
                    catch { if (codeUnitWideSigned != int16(13)) { return 7; } failures += 1; }
                    try { codeUnit = char16(uint32(65536)); }
                    catch { if (codeUnit != char16(10)) { return 8; } failures += 1; }
                    try { wideTarget = uint64(int64(-1)); }
                    catch { if (wideTarget != uint64(14)) { return 9; } failures += 1; }
                    try { unsignedSignedTarget = int16(unsignedWideSource); }
                    catch { if (unsignedSignedTarget != int16(15)) { return 10; } failures += 1; }
                    try { unsignedLongTarget = int64(unsignedLongSource); }
                    catch { if (unsignedLongTarget != int64(16)) { return 11; } failures += 1; }
                }
                return failures == 12 ? 42 : 0;
            }
            """;
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }

    [Test]
    public async Task EveryIntegerWidthAndSignednessPairConvertsAcrossAotAndJitAsync()
    {
        var types = new[] { "int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64" };
        var source = new StringBuilder("int32 Main() {\n");
        var pair = 0;
        foreach (var sourceType in types)
        {
            foreach (var destinationType in types)
            {
                if (sourceType == destinationType)
                {
                    continue;
                }
                source.AppendLine($"    {sourceType} source{pair} = {sourceType}(1);");
                source.AppendLine($"    {destinationType} result{pair} = {destinationType}(source{pair});");
                source.AppendLine($"    if (int32(result{pair}) != 1) {{ return {pair + 1}; }}");
                pair++;
            }
        }
        Assert.That(pair, Is.EqualTo(types.Length * (types.Length - 1)));
        source.AppendLine("    return 42;\n}");

        await AssertFailFastExecutionAsync(source.ToString(), 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source.ToString(), 42).ConfigureAwait(true);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task Char16ConversionsCoverEveryCodeUnitAcrossAotAndJitAsync(
        NativeRuntimePolicy policy)
    {
        const string source = """
            int8 ToInt8(char16 value) { return int8(value); }
            uint8 ToUInt8(char16 value) { return uint8(value); }
            int16 ToInt16(char16 value) { return int16(value); }
            uint16 ToUInt16(char16 value) { return uint16(value); }
            int32 ToInt32(char16 value) { return int32(value); }
            uint32 ToUInt32(char16 value) { return uint32(value); }
            int64 ToInt64(char16 value) { return int64(value); }
            uint64 ToUInt64(char16 value) { return uint64(value); }
            int128 ToInt128(char16 value) { return int128(value); }
            uint128 ToUInt128(char16 value) { return uint128(value); }
            int32 Main() {
                mut int32 value = 0;
                while (value <= 65535) {
                    char16 unit = char16(value);
                    if (ToUInt16(unit) != uint16(value)
                        || ToInt32(unit) != value || ToUInt32(unit) != uint32(value)
                        || ToInt64(unit) != int64(value) || ToUInt64(unit) != uint64(value)) {
                        return 1;
                    }
                    if (value <= 127 && ToInt8(unit) != int8(value)) { return 2; }
                    if (value <= 255 && ToUInt8(unit) != uint8(value)) { return 3; }
                    if (value <= 32767 && ToInt16(unit) != int16(value)) { return 4; }
                    _ = ToInt128(unit);
                    _ = ToUInt128(unit);
                    value++;
                }
                return 42;
            }
            """;

        if (policy == NativeRuntimePolicy.FailFast)
        {
            await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        }
        else
        {
            await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
        }
    }

    [Test]
    public void NarrowCheckedConversionSurvivesPrivateModuleLinking()
    {
        var runtimePolicy = BoundRuntimePolicy.CheckedExceptions;
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int8 Narrow(int32 value) { return int8(value); }"),
            new CompilationOptions { RuntimePolicy = runtimePolicy });
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(providerCompilation.Program!, ["Narrow"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                if (int32(Narrow(127)) != 127) { return 1; }
                unsafe {
                    try { _ = Narrow(128); return 2; }
                    catch { return 42; }
                }
                return 0;
            }
            """), reference, new CompilationOptions { RuntimePolicy = runtimePolicy });
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(consumerCompilation.Program!, producer);
        var linked = ModuleCompiler.Link(consumer, producer);

        Assert.That(NativeJit.Execute(linked, NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }

    [TestCase("int8 value = int8(float32(1));")]
    [TestCase("char8 value = char8(uint16(1));")]
    [TestCase("char32 value = char32(uint16(1));")]
    [TestCase("rune value = rune(uint16(1));")]
    public void UnsupportedScalarConversionPairsReject(string statement)
    {
        var compilation = Terrum.CodeAnalysis.Semantics.Compilation.Create(
            Terrum.CodeAnalysis.Syntax.SyntaxTree.Parse("int32 Main() { " + statement + " return 0; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3007"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase((ushort)0)]
    [TestCase((ushort)1)]
    [TestCase((ushort)0xD7FF)]
    [TestCase((ushort)0xD800)]
    [TestCase((ushort)0xDFFF)]
    [TestCase((ushort)0xE000)]
    [TestCase(ushort.MaxValue)]
    public async Task CheckedChar16WideningsAlwaysPublishCodeUnitAcrossAotAndJitAsync(ushort codeUnit)
    {
        var component = await File.ReadAllTextAsync(
            TerrumProjectPaths.RepositoryFile(RepositoryRoot(), "sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/IntegerCheckedConversions.terrum"))
            .ConfigureAwait(true);
        var source = component + $$"""

            int32 Main() {
                require TryConvertChar16ToInt32(char16({{codeUnit}}), init var signed32);
                require TryConvertChar16ToUInt32(char16({{codeUnit}}), init var unsigned32);
                require TryConvertChar16ToInt64(char16({{codeUnit}}), init var signed64);
                require TryConvertChar16ToUInt64(char16({{codeUnit}}), init var unsigned64);
                if (signed32 != {{codeUnit}} || unsigned32 != {{codeUnit}}
                    || signed64 != {{codeUnit}} || unsigned64 != {{codeUnit}}) { return 1; }
                return 42;
            }
            """;
        await AssertFailFastExecutionAsync(source, 42).ConfigureAwait(true);
        await AssertCheckedExecutionAsync(source, 42).ConfigureAwait(true);
    }
}
