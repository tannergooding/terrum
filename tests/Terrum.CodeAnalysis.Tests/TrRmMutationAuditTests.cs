// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmMutationAuditTests
{
    // One indexed block: signed -1 and count 1, two uint32 reinterpretations,
    // unsigned right shift, signed result reinterpretation. All inventories are handwritten.
    internal static byte[] ShiftBody() =>
    [
        8, 1, 6, 0, 2, 1, 0, 0, 0, 4, 0, 0, 0,
        1, 1, 1, 1, 2, 1, 2, 1, 2, 1, 1, 1,
        0, 6, 15, 19, 23, 28, 33, 38, 0, 45, 43,
        16, 6, 1, 2, 2, 3, 3, 4, 4, 4, 5, 5, 6, 6, 6,
        2, 1, 1, 0xFF, 1, 2, 1, 1,
        6, 3, 2, 3, 1, 6, 4, 2, 3, 2,
        0x15, 5, 7, 3, 4, 6, 6, 1, 3, 5, 0x83, 6,
    ];

    [Fact]
    public void HandwrittenIndexedRepresentationShiftExecutes()
    {
        var artifact = TrRmExecutionAuditTests.Artifact(ShiftBody());
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        var program = TrRmExecutableCompiler.Load(artifact);
        Assert.Equal(int.MaxValue, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        Assert.Throws<ArgumentException>(() =>
            NativeEmitter.Emit(program, NativeRuntimePolicy.CheckedExceptions));
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateProducer(program, ["Main"]));
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(
            TrRmExecutionAuditTests.Artifact(ShiftBody(), effect: 1)));
    }

    [Theory]
    [InlineData(5, 2)]   // bool source mapping
    [InlineData(9, 6)]   // uint64 destination mapping
    [InlineData(18, 2)]  // forged use-block count
    [InlineData(26, 5)]  // incomplete definition range
    [InlineData(29, 24)] // conversion's definition offset
    [InlineData(34, 44)] // block extent
    [InlineData(35, 42)] // terminator offset
    [InlineData(39, 3)]  // false last-use ordinal
    [InlineData(62, 0)]  // unassigned conversion policy
    [InlineData(62, 1)]  // wrapping is assigned but not executable here
    [InlineData(62, 2)]  // saturation is assigned but not executable here
    [InlineData(62, 4)]  // unassigned conversion policy
    [InlineData(63, 3)]  // self-dependent conversion definition
    [InlineData(71, 8)]  // no logical-right kind exists
    [InlineData(73, 2)]  // int32 count is not a TRRM shift operand
    [InlineData(76, 2)]  // conversion result disagrees with indexed type
    public void HandwrittenIndexedTamperingRejects(int offset, byte value)
    {
        var body = ShiftBody();
        body[offset] = value;
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body)));
    }

    [Fact]
    public void EveryTruncatedIndexedShiftRejects()
    {
        var body = ShiftBody();
        for (var length = 1; length < body.Length; length++)
        {
            Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(
                TrRmExecutionAuditTests.Artifact(body[..length])));
        }
    }

    [Theory]
    [InlineData("int32")]
    [InlineData("uint32")]
    [InlineData("int64")]
    [InlineData("uint64")]
    public void LazyRhsAndNestedLoopTransfersPreserveSnapshots(string type)
    {
        var source = $$"""
            int32 Main() {
                mut {{type}} x = {{type}}(4);
                x += x++ == {{type}}(4) ? ++x : {{type}}(1) / {{type}}(0);
                mut int32 i = 0;
                mut int32 j = 0;
                while (i++ < 2) {
                    j = 0;
                    while (j++ < 3) {
                        if (j == 1) { x <<= 1; continue; }
                        x--;
                        break;
                    }
                }
                return x == {{type}}(37) && i == 3 && j == 2 ? 42 : 0;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var original = Assert.IsType<BoundProgram>(compilation.Program);
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(original, "audit"));
        Assert.Equal(42, NativeJit.Execute(original, NativeRuntimePolicy.FailFast));
        Assert.Equal(42, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(127, true)]
    [InlineData(128, false)]
    public void UpdatesCountEveryMaterializedValue(int updates, bool supported)
    {
        var source = new StringBuilder("int32 Main() { mut int32 x = 0; ");
        for (var index = 0; index < updates; index++)
        {
            source.Append("x++; ");
        }
        source.Append("return +x; }");
        var compilation = Compilation.Create(SyntaxTree.Parse(source.ToString()));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var original = Assert.IsType<BoundProgram>(compilation.Program);
        if (!supported)
        {
            Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(original, "limit"));
            return;
        }
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(original, "limit"));
        Assert.Equal(256, loaded.Functions[0].Variables.Length);
        Assert.Equal(updates, NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast));
    }
}
