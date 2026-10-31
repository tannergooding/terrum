// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Tests.Semantics;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Storage;

public sealed class NativeFixedArrayStorageTests
{
    [Test]
    public void CheckedFixedArrayStorageAdmitsUnrelatedBlittableAggregates()
    {
        var source = """
            [Blittable] [Layout(Sequential)]
            struct Measurement {
                int64 First;
                int64 Second;
                int64 Third;
                public Measurement(int64 first, int64 second, int64 third) {
                    First = first;
                    Second = second;
                    Third = third;
                }
                public int64 GetThird() { return Third; }
            }

            int32 Main() {
                [Measurement; 2] values = [
                    new Measurement(1, 2, 3), new Measurement(4, 5, 6)];
                int32 index = 1;
                return int32(values[index].GetThird());
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");

        var layout = program.Functions[program.EntryPoint].Variables
            .Single(static variable => variable.Name == "values").NativeFixedArrayLayout;
        Assert.That(layout, Is.Not.Null);
        var arrayLayout = layout ?? throw new AssertionException("Expected the fixed array layout.");
        Assert.That(arrayLayout.ElementStride, Is.EqualTo(24));
        Assert.That(arrayLayout.ByteExtent, Is.EqualTo(48));
        Assert.That(NativeFixedArrayContract.MatchesProgram(program), Is.True);
        Assert.That(NativeEmitter.EmitWithNativeImports(
            program, NativeRuntimePolicy.CheckedExceptions), Is.Not.Empty);
    }

    [Test]
    public void CheckedFixedArrayArithmeticUsesGenericSourceLayoutWithoutWindowsImports()
    {
        var source = """
            int32 Main() {
                mut [uint32; 3] values = [4, 5, 6];
                values[1] = values[0] + 1;
                return int32(values[2]);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");

        Assert.That(NativeEmitter.EmitWithNativeImports(program,
            NativeRuntimePolicy.CheckedExceptions), Is.Not.Empty);
    }

    [Test]
    public void TypedNativePointerCanAddressACheckedFixedArrayElement()
    {
        const string source = """
            int32 Main() {
                mut [uint32; 3] values = [4, 5, 6];
                int32 index = 1;
                unsafe {
                    write ptr<uint32> selected = &values[index];
                    *selected = 42;
                    return int32(values[1]);
                }
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.CheckedExceptions);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");
        Assert.That(NativeEmitter.EmitWithNativeImports(program,
            NativeRuntimePolicy.CheckedExceptions), Is.Not.Empty);
    }

    [Test]
    public void TypedNativePointerRetainsItsEvaluatedFixedArrayProjection()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string source = """
            int32 Main() {
                mut [uint32; 3] values = [4, 5, 6];
                mut int32 index = 1;
                unsafe {
                    ptr<uint32> first = &values[0];
                    ptr<uint32> selected = &values[index];
                    index = 2;
                    return int32(*first + *selected);
                }
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast), Is.EqualTo(9));
    }

    [Test]
    public void ArrayMetadataDetectionDoesNotTreatRecursivePointersAsArrays()
    {
        var structures = ImmutableArray.Create(new BoundStruct("Node", [
            new BoundStructField("Next", NativePointerTypes.Type(0), 0,
                sizeof(ulong)),
        ], sizeof(ulong)));
        var pointers = ImmutableArray.Create(
            new NativePointerTypeDefinition((PrimitiveType)100, false));
        Assert.That(NativePointerTypeContract.IsValidTable(pointers,
            structures, [], []), Is.True);
        Assert.That(NativeFixedArrayContract.HasMetadataOnlyArrayType(
            (PrimitiveType)100, structures, [], pointers, []), Is.False);
    }

    [TestCase("ptr<char16> selected = &values[0];")]
    [TestCase("write ptr<uint32> selected = &values[0];")]
    [TestCase("ptr<uint32> selected = &values[3];")]
    public void TypedNativePointerAddressRejectsMismatchedElementsAndAccess(string addressDeclaration)
    {
        var source = $$"""
            int32 Main() {
                [uint32; 3] values = [4, 5, 6];
                unsafe {
                    {{addressDeclaration}}
                    return 0;
                }
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.CheckedExceptions);

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void FixedArraysRejectNonBlittableElementsAndPositiveEmptyInitialization()
    {
        const string nonBlittable = """
            [Layout(Sequential)]
            struct Measurement {
                int64 Value;
                public Measurement(int64 value) { Value = value; }
            }
            int32 Main() {
                [Measurement; 1] values = [new Measurement(1)];
                return 0;
            }
            """;
        const string positiveEmpty = """
            int32 Main() {
                [uint64; 513] values = [];
                return 0;
            }
            """;

        Assert.That(Compilation.Create(SyntaxTree.Parse(nonBlittable),
            BoundRuntimePolicy.FailFast).Program, Is.Null);
        Assert.That(Compilation.Create(SyntaxTree.Parse(positiveEmpty),
            BoundRuntimePolicy.FailFast).Program, Is.Null);
    }

    [Test]
    public void FixedArrayExtentCanExceedTheFormerPrototypeLimit()
    {
        var initializer = string.Join(", ", Enumerable.Repeat("0", 513));
        var source = $$"""
            int32 Main() {
                [uint64; 513] values = [{{initializer}}];
                return 0;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var layout = program.Functions[program.EntryPoint].Variables
            .Single(static variable => variable.Name == "values")
            .NativeFixedArrayLayout;
        Assert.That(layout, Is.Not.Null);
        Assert.That(layout!.Value.ElementCount, Is.EqualTo(513));
        Assert.That(layout.Value.ByteExtent, Is.EqualTo(4104));
        Assert.That(NativeEmitter.EmitWithNativeImports(program,
            NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void ZeroLengthFixedArrayAcceptsOnlyAnEmptyInitializer()
    {
        const string source = """
            int32 Main() {
                [uint32; 0] values = [];
                return 0;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var layout = program.Functions[program.EntryPoint].Variables
            .Single(static variable => variable.Name == "values")
            .NativeFixedArrayLayout;
        Assert.That(layout, Is.Not.Null);
        Assert.That(layout!.Value.ElementCount, Is.Zero);
        Assert.That(layout.Value.ByteExtent, Is.Zero);
    }

    [Test]
    public void FixedArrayExtentUsesClosedUInt64Constants()
    {
        const string source = """
            int32 Main() {
                const uint64 Length = 2;
                [uint32; const Length + 1] values = [1, 2, 3];
                [uint32; 3] sameType = [4, 5, 6];
                [uint32; -0] empty = [];
                return int32(values[2] + sameType[2]);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        Assert.That(program.NativeFixedArrayTypes.Length, Is.EqualTo(2));
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast),
            Is.EqualTo(9));
    }

    [Test]
    public void ZeroPayloadArrayStillRequiresEveryElement()
    {
        const string source = """
            int32 Main() {
                int32 index = 1;
                mut [void; 2] values = [(), ()];
                values[index] = values[0];
                return 0;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            BoundRuntimePolicy.CheckedExceptions);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var layout = program.Functions[program.EntryPoint].Variables
            .Single(static variable => variable.Name == "values")
            .NativeFixedArrayLayout;
        Assert.That(layout, Is.Not.Null);
        Assert.That(layout!.Value.ElementStride, Is.Zero);
        Assert.That(layout.Value.ByteExtent, Is.Zero);
        Assert.That(layout.Value.Alignment, Is.EqualTo(1));
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions),
            Is.EqualTo(0));

        var incomplete = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                [void; 2] values = [()];
                return 0;
            }
            """), BoundRuntimePolicy.FailFast);
        Assert.That(incomplete.Program, Is.Null);
        Assert.That(incomplete.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, incomplete.Diagnostics));

        var pointerNested = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    [[ptr<uint32>; 1]; 1] values = [[null]];
                }
                return 0;
            }
            """), BoundRuntimePolicy.FailFast);
        Assert.That(pointerNested.Program, Is.Null);
        Assert.That(pointerNested.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, pointerNested.Diagnostics));
    }

    [Test]
    public void ZeroPayloadArrayDoesNotProvideElementStorage()
    {
        const string source = """
            int32 Main() {
                [void; 2] values = [(), ()];
                unsafe {
                    ptr<void> selected = &values[0];
                    return 0;
                }
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            BoundRuntimePolicy.FailFast);

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void SameNamedInlineArrayDeclarationRemainsNominal()
    {
        const string source = """
            [Blittable] [Layout(Sequential)]
            struct InlineArray {
                public int32 Value;
                public InlineArray(int32 value) { Value = value; }
            }

            int32 Main() {
                [InlineArray; 1] values = [new InlineArray(42)];
                return values[0].Value;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [TestCase("[uint32;]")]
    [TestCase("[; 2]")]
    [TestCase("[uint32; 2")]
    public void FixedArrayTypeRejectsMissingSlotsAndBrackets(string typeName)
    {
        var source = $"int32 Main() {{ {typeName} values = []; return 0; }}";
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void FixedArrayDynamicIndexEffectDependsOnSelectedRuntimePolicy()
    {
        const string source = """

            int32 Index() { return 0; }


            int32 Main() {
                [uint32; 2] values = [4, 5];
                return values[Index()] == 4 ? 0 : 1;
            }
            """;

        var failFast = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.FailFast);
        Assert.That(failFast.HasErrors, Is.False, string.Join(Environment.NewLine, failFast.Diagnostics));
        SemanticVerifier.Verify(BoundProgramTestHelpers.MarkNoFailureProof(
            failFast.Program!, "Main", "Index"));
        var checkedExceptions = Compilation.Create(SyntaxTree.Parse(source),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(checkedExceptions.HasErrors, Is.False,
            string.Join(Environment.NewLine, checkedExceptions.Diagnostics));
        Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(
            BoundProgramTestHelpers.MarkNoFailureProof(checkedExceptions.Program!, "Main", "Index")));
    }

    [TestCase("int8")]
    [TestCase("int16")]
    [TestCase("int32")]
    [TestCase("int64")]
    [TestCase("int128")]
    [TestCase("uint8")]
    [TestCase("uint16")]
    [TestCase("uint32")]
    [TestCase("uint64")]
    [TestCase("uint128")]
    public void FixedArrayPreservesEveryIndexWidth(string indexType)
    {
        var source = $$"""
            int32 Main() {
                [uint32; 3] values = [10, 20, 30];
                return int32(values[{{indexType}}(0)]
                    + values[{{indexType}}(1)]
                    + values[{{indexType}}(2)]);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            BoundRuntimePolicy.CheckedExceptions);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(60));
    }

    [TestCase("int8")]
    [TestCase("int16")]
    [TestCase("int32")]
    [TestCase("int64")]
    [TestCase("int128")]
    [TestCase("uint8")]
    [TestCase("uint16")]
    [TestCase("uint32")]
    [TestCase("uint64")]
    [TestCase("uint128")]
    public void FixedArrayPointerAddressPreservesEveryIndexWidth(string indexType)
    {
        var source = $$"""
            int32 Main() {
                [uint32; 3] values = [10, 20, 30];
                unsafe {
                    ptr<uint32> selected = &values[{{indexType}}(2)];
                    return int32(*selected);
                }
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            BoundRuntimePolicy.CheckedExceptions);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(30));
    }

    [TestCase("int8(3)")]
    [TestCase("int16(3)")]
    [TestCase("int32(3)")]
    [TestCase("int64(3)")]
    [TestCase("int128(3)")]
    [TestCase("uint8(3)")]
    [TestCase("uint16(3)")]
    [TestCase("uint32(3)")]
    [TestCase("uint64(3)")]
    [TestCase("uint128(3)")]
    public void FixedArrayRejectsEndIndices(string indexExpression)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            int32 Main() {
                [uint32; 3] values = [10, 20, 30];
                return int32(values[{{indexExpression}}]);
            }
            """), BoundRuntimePolicy.FailFast);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void FixedArrayRejectsSignedNegativeAndIndicesWiderThanUInt64()
    {
        foreach (var expression in new[] {
            "values[int8(-1)]", "values[int16(-1)]",
            "values[int32(-1)]", "values[int64(-1)]",
            "values[int128(-1)]",
            "values[int128(18446744073709551616)]",
            "values[uint128(18446744073709551616)]",
        })
        {
            var compilation = Compilation.Create(SyntaxTree.Parse($$"""
                int32 Main() {
                    [uint32; 3] values = [10, 20, 30];
                    return int32({{expression}});
                }
                """), BoundRuntimePolicy.FailFast);

            Assert.That(compilation.Program, Is.Null, expression);
            Assert.That(compilation.Diagnostics.Any(static diagnostic =>
                diagnostic.Code == "TRM3030"), Is.True,
                string.Join(Environment.NewLine, compilation.Diagnostics));
        }
    }

    [Test]
    public void FixedArrayIndexIsEvaluatedOnceAndFailureSkipsRightHandSide()
    {
        const string once = """
            int32 Main() {
                mut int32 index = 0;
                mut [uint32; 3] values = [10, 20, 30];
                values[index++] = 42;
                return int32(index) + int32(values[0]) - 43;
            }
            """;
        var onceCompilation = Compilation.Create(SyntaxTree.Parse(once),
            BoundRuntimePolicy.FailFast);
        Assert.That(onceCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, onceCompilation.Diagnostics));
        Assert.That(NativeJit.Execute(onceCompilation.Program!,
            NativeRuntimePolicy.FailFast), Is.Zero);

        const string failedWrite = """
            int32 Main() {
                int32 index = 2;
                mut int32 rightHandSide = 0;
                mut [uint32; 2] values = [10, 20];
                unsafe {
                    try {
                        values[index] = uint32(rightHandSide++);
                    } catch {
                        return rightHandSide;
                    }
                }
                return 99;
            }
            """;
        var failureCompilation = Compilation.Create(SyntaxTree.Parse(failedWrite),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(failureCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, failureCompilation.Diagnostics));
        Assert.That(NativeJit.Execute(failureCompilation.Program!,
            NativeRuntimePolicy.CheckedExceptions), Is.Zero);
    }

    [Test]
    public void NestedFixedArrayInitializationPreservesDimensionsAndOrder()
    {
        const string source = """
            int32 Main() {
                mut int32 trace = 0;
                [[uint32; 2]; 2] values = [
                    [uint32(trace == 0 ? trace++ : 99),
                     uint32(trace == 1 ? trace++ : 99)],
                    [uint32(trace == 2 ? trace++ : 99),
                     uint32(trace == 3 ? trace++ : 99)]];
                return trace;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var outer = program.Functions[program.EntryPoint].Variables
            .Single(static variable => variable.Name == "values")
            .NativeFixedArrayLayout
            ?? throw new AssertionException("Expected the outer array layout.");
        var inner = program.NativeFixedArrayTypes[
            NativeFixedArrayTypes.Index(outer.ElementType)];
        Assert.That(outer.ElementCount, Is.EqualTo(2));
        Assert.That(inner.ElementCount, Is.EqualTo(2));
        Assert.That(inner.ElementType, Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(NativeJit.Execute(program,
            NativeRuntimePolicy.FailFast), Is.EqualTo(4));

        var zeroExtent = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                [[uint32; 0]; 2] values = [[], []];
                return 0;
            }
            """), BoundRuntimePolicy.FailFast);
        Assert.That(zeroExtent.HasErrors, Is.False,
            string.Join(Environment.NewLine, zeroExtent.Diagnostics));
        Assert.That(NativeJit.Execute(zeroExtent.Program!,
            NativeRuntimePolicy.FailFast), Is.Zero);

        var zeroPayload = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                [[void; 2]; 2] values = [[(), ()], [(), ()]];
                return 0;
            }
            """), BoundRuntimePolicy.FailFast);
        Assert.That(zeroPayload.HasErrors, Is.False,
            string.Join(Environment.NewLine, zeroPayload.Diagnostics));
        Assert.That(NativeJit.Execute(zeroPayload.Program!,
            NativeRuntimePolicy.FailFast), Is.Zero);

        var nestedWrites = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut [[uint32; 2]; 2] values = [[1, 2], [3, 4]];
                values[0] = [5, 6];
                values[1] = values[0];
                return 0;
            }
            """), BoundRuntimePolicy.FailFast);
        Assert.That(nestedWrites.HasErrors, Is.False,
            string.Join(Environment.NewLine, nestedWrites.Diagnostics));
        Assert.That(NativeJit.Execute(nestedWrites.Program!,
            NativeRuntimePolicy.FailFast), Is.Zero);

        var incomplete = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                [[uint32; 2]; 2] values = [[1], [3, 4]];
                return 0;
            }
            """), BoundRuntimePolicy.FailFast);
        Assert.That(incomplete.Program, Is.Null);
        Assert.That(incomplete.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, incomplete.Diagnostics));
    }

    [Test]
    public void NestedFixedArraySelectionAndWriteUseEachDimension()
    {
        const string source = """
            int32 Main() {
                mut int32 outer = 1;
                mut int32 inner = 0;
                mut [[uint32; 2]; 2] values = [[1, 2], [3, 4]];
                values[0] = [5, 6];
                values[1] = values[0];
                values[outer++][inner++] = 9;
                unsafe {
                    ptr<uint32> selected = &values[outer - 1][inner - 1];
                    return int32(*selected) + int32(values[0][1])
                        + outer + inner;
                }
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            BoundRuntimePolicy.CheckedExceptions);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(18));

        const string failedSelection = """
            int32 Main() {
                int32 outer = 2;
                mut int32 innerCalls = 0;
                mut int32 rightHandSide = 0;
                mut [[uint32; 2]; 2] values = [[1, 2], [3, 4]];
                unsafe {
                    try {
                        values[outer][innerCalls++] = uint32(rightHandSide++);
                    } catch {
                        return innerCalls + rightHandSide;
                    }
                }
                return 99;
            }
            """;
        var failedCompilation = Compilation.Create(
            SyntaxTree.Parse(failedSelection),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(failedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, failedCompilation.Diagnostics));
        Assert.That(NativeJit.Execute(failedCompilation.Program!,
            NativeRuntimePolicy.CheckedExceptions), Is.Zero);

        const string failedInnerSelection = """
            int32 Main() {
                int32 outer = 1;
                int32 inner = 2;
                mut int32 rightHandSide = 0;
                mut [[uint32; 2]; 2] values = [[1, 2], [3, 4]];
                unsafe {
                    try {
                        values[outer][inner] = uint32(rightHandSide++);
                    } catch {
                        return rightHandSide;
                    }
                }
                return 99;
            }
            """;
        var failedInnerCompilation = Compilation.Create(
            SyntaxTree.Parse(failedInnerSelection),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(failedInnerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                failedInnerCompilation.Diagnostics));
        Assert.That(NativeJit.Execute(failedInnerCompilation.Program!,
            NativeRuntimePolicy.CheckedExceptions), Is.Zero);

        const string failedUnitSelection = """
            int32 Main() {
                int32 outer = 1;
                int32 inner = 2;
                mut int32 rightHandSide = 0;
                mut [[void; 2]; 2] values = [[(), ()], [(), ()]];
                unsafe {
                    try {
                        values[outer][inner] = values[0][rightHandSide++];
                    } catch {
                        return rightHandSide;
                    }
                }
                return 99;
            }
            """;
        var failedUnitCompilation = Compilation.Create(
            SyntaxTree.Parse(failedUnitSelection),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(failedUnitCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                failedUnitCompilation.Diagnostics));
        Assert.That(NativeJit.Execute(failedUnitCompilation.Program!,
            NativeRuntimePolicy.CheckedExceptions), Is.Zero);

        const string threeDimensions = """
            int32 Main() {
                int32 outer = 1;
                int32 middle = 0;
                int32 inner = 1;
                mut [[[uint32; 2]; 2]; 2] values = [
                    [[1, 2], [3, 4]],
                    [[5, 6], [7, 8]]];
                values[outer][middle][inner] = 9;
                unsafe {
                    ptr<uint32> selected =
                        &values[outer][middle][inner];
                    return int32(*selected);
                }
            }
            """;
        var threeDimensionCompilation = Compilation.Create(
            SyntaxTree.Parse(threeDimensions), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(threeDimensionCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                threeDimensionCompilation.Diagnostics));
        Assert.That(NativeJit.Execute(threeDimensionCompilation.Program!,
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(9));
    }

}
