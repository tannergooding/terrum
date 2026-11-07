using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Native.Storage;

[TestFixture]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class NativeFixedArrayRepeatInitializationTests
{
    private static readonly int[] s_twoCopyOriginOrdinals = [0, 1];
    private static readonly int[] s_singleCopyOriginOrdinal = [1];

    private static CompilationOptions LayoutOptions(
        BoundRuntimePolicy runtimePolicy)
        => NativeCoreRegistrationTests.CreateRegisteredCompilationOptions(
            runtimePolicy);

    [Test]
    public void RepeatedSeedPreservesFlatAndNestedHighIndexCoverage()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawWord {
                public mut uint8 Low;
                public mut uint64 Wide;
                public RawWord(uint64 value) {
                    unsafe { Wide = value; }
                }
            }

            public uint64 ReadFlatHigh() {
                RawWord seed = new RawWord(uint64(7));
                [RawWord; 5000000000] values = [seed; 5000000000];
                unsafe { return values[uint64(4999999999)].Wide; }
            }

            public uint64 ReadNestedHighOuter() {
                RawWord seed = new RawWord(uint64(7));
                [RawWord; 2] row = [seed; 2];
                [[RawWord; 2]; 5000000000] rows = [row; 5000000000];
                unsafe { return rows[uint64(4999999999)][1].Wide; }
            }

            public uint64 ReadNestedRepeatSeedHighOuter() {
                RawWord seed = new RawWord(uint64(7));
                [[RawWord; 2]; 5000000000] rows = [[seed; 2]; 5000000000];
                unsafe { return rows[uint64(4999999999)][1].Wide; }
            }

            public uint64 ReadNestedHighInner() {
                RawWord seed = new RawWord(uint64(7));
                [RawWord; 5000000000] row = [seed; 5000000000];
                [[RawWord; 5000000000]; 2] rows = [row; 2];
                unsafe { return rows[1][uint64(4999999999)].Wide; }
            }

            public uint64 ReadNestedRepeatSeedHighInner() {
                RawWord seed = new RawWord(uint64(7));
                [[RawWord; 5000000000]; 2] rows = [[seed; 5000000000]; 2];
                unsafe { return rows[1][uint64(4999999999)].Wide; }
            }

            public int32 InitializeZeroCount() {
                RawWord seed = new RawWord(uint64(7));
                [RawWord; 0] values = [seed; 0];
                return 0;
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));

        var nestedFunction = compilation.Program?.Functions.SingleOrDefault(
            static function => function.Name == "ReadNestedHighOuter");
        var nestedLocal = nestedFunction?.Body.Statements
            .OfType<BoundLocal>()
            .SingleOrDefault(local =>
                nestedFunction.Variables[local.Variable].Name == "rows");
        var nestedRepeat = nestedLocal?.Initializer
            as BoundNativeFixedArrayRepeatInitialization;
        var nestedTypeDetails = "Nested repeat node was not produced.";
        if (nestedFunction is not null && nestedLocal is not null
            && nestedRepeat is not null)
        {
            nestedTypeDetails
                = $"seed={nestedRepeat.Seed.Type}, element={nestedRepeat.Layout.ElementType}, "
                    + $"local={nestedFunction.Variables[nestedLocal.Variable].Type}";
        }
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics)
                + Environment.NewLine + nestedTypeDetails);

        var zeroFunction = compilation.Program!.Functions.Single(
            static function => function.Name == "InitializeZeroCount");
        var zeroLocal = zeroFunction.Body.Statements
            .OfType<BoundLocal>()
            .Single(local =>
                zeroFunction.Variables[local.Variable].Name == "values");
        Assert.That(zeroLocal.Initializer,
            Is.TypeOf<BoundNativeFixedArrayRepeatInitialization>());
        var zeroRepeat = (BoundNativeFixedArrayRepeatInitialization)
            zeroLocal.Initializer;
        Assert.That(zeroRepeat.Layout.ElementCount, Is.Zero);
        Assert.That(zeroRepeat.Seed, Is.TypeOf<BoundRead>());
    }

    [Test]
    public void NestedHighCountRepeatCopiesRetainSparseSeedFacts()
    {
        var preservedSnapshot = Compile("""
            public uint64 ReadCopiedWideAfterNestedHighWrite() {
                RawWord wide = new RawWord(uint64(7));
                init mut RawWord low;
                unsafe { low.Low = 1; }
                mut [RawWord; 2] row = [wide; 2];
                row[1] = low;
                mut [[RawWord; 2]; 5000000000] rows
                    = [row; 5000000000];
                [[RawWord; 2]; 5000000000] copy = rows;
                rows[uint64(4999999999)][0] = low;
                unsafe {
                    return copy[uint64(4999999999)][0].Wide;
                }
            }
            """);
        Assert.That(preservedSnapshot.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                preservedSnapshot.Diagnostics));

        var sparseOverride = Compile("""
            public uint64 ReadNarrowNestedHighOverride() {
                RawWord wide = new RawWord(uint64(7));
                init mut RawWord low;
                unsafe { low.Low = 1; }
                mut [RawWord; 2] row = [wide; 2];
                row[1] = low;
                [[RawWord; 2]; 5000000000] rows
                    = [row; 5000000000];
                unsafe {
                    return rows[uint64(4999999999)][1].Wide;
                }
            }
            """);
        Assert.That(sparseOverride.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                sparseOverride.Diagnostics));

        var preservedInnerSnapshot = Compile("""
            public uint64 ReadCopiedInnerWideAfterHighWrite() {
                RawWord wide = new RawWord(uint64(7));
                init mut RawWord low;
                unsafe { low.Low = 1; }
                mut [RawWord; 5000000000] row
                    = [wide; 5000000000];
                row[uint64(4999999999)] = low;
                mut [[RawWord; 5000000000]; 2] rows = [row; 2];
                [[RawWord; 5000000000]; 2] copy = rows;
                rows[1][0] = low;
                unsafe { return copy[1][0].Wide; }
            }
            """);
        Assert.That(preservedInnerSnapshot.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                preservedInnerSnapshot.Diagnostics));

        var innerSparseOverride = Compile("""
            public uint64 ReadNarrowInnerHighOverride() {
                RawWord wide = new RawWord(uint64(7));
                init mut RawWord low;
                unsafe { low.Low = 1; }
                mut [RawWord; 5000000000] row
                    = [wide; 5000000000];
                row[uint64(4999999999)] = low;
                [[RawWord; 5000000000]; 2] rows = [row; 2];
                unsafe {
                    return rows[1][uint64(4999999999)].Wide;
                }
            }
            """);
        Assert.That(innerSparseOverride.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                innerSparseOverride.Diagnostics));
    }

    [Test]
    public void RepeatedNarrowSeedDoesNotEstablishWideCoverage()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawWord {
                public mut uint8 Low;
                public mut uint64 Wide;
                public RawWord(uint8 value) {
                    unsafe { Low = value; }
                }
            }

            public uint64 ReadUninitializedWide() {
                RawWord seed = new RawWord(uint8(7));
                [RawWord; 5000000000] values = [seed; 5000000000];
                unsafe { return values[4999999999].Wide; }
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void HugeRepeatStorageIsRejectedBeforeNativeEmission()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawWord {
                public mut uint64 Wide;
            }

            int32 Main() {
                init mut RawWord seed;
                unsafe { seed.Wide = uint64(7); }
                mut [RawWord; 5000000000] values
                    = [seed; 5000000000];
                return 0;
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var exception = Assert.Throws<NotSupportedException>(() =>
            NativeEmitter.EmitWithNativeImports(compilation.Program!,
                NativeRuntimePolicy.FailFast));
        Assert.That(exception!.Message,
            Does.Contain("signed x64 stack-offset capacity"));
    }

    [Test]
    public void RepeatCountMustMatchTheExactTargetExtent()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct RawWord {
                public mut uint8 Low;
                public mut uint64 Wide;
                public RawWord(uint64 value) {
                    unsafe { Wide = value; }
                }
            }

            public int32 MismatchedExtent() {
                RawWord seed = new RawWord(1);
                [RawWord; 2] values = [seed; 1];
                return 0;
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RepeatSyntaxRequiresAnExactTargetAndClosedCount()
    {
        var noTarget = Compile("""
            public int32 MissingTarget() {
                RawWord seed = new RawWord(uint64(7));
                var values = [seed; 2];
                return 0;
            }
            """);
        Assert.That(noTarget.HasErrors, Is.True,
            string.Join(Environment.NewLine, noTarget.Diagnostics));

        var runtimeCount = Compile("""
            public int32 RuntimeCount(uint64 count) {
                RawWord seed = new RawWord(uint64(7));
                [RawWord; 2] values = [seed; count];
                return 0;
            }
            """);
        Assert.That(runtimeCount.HasErrors, Is.True,
            string.Join(Environment.NewLine, runtimeCount.Diagnostics));

        var trailingComma = Compile("""
            public int32 RepeatTrailingComma() {
                RawWord seed = new RawWord(uint64(7));
                [RawWord; 2] values = [seed; 2,];
                return 0;
            }
            """);
        Assert.That(trailingComma.HasErrors, Is.True,
            string.Join(Environment.NewLine, trailingComma.Diagnostics));
    }

    [Test]
    public void RepeatRequiresRootlessBlittableSeedEvenAtZeroCount()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Cell {
                public mut int32 Value;

                internal Cell() {
                    Value = int32(0);
                }
            }

            int32 Main() {
                mut Cell seed = new Cell();
                mut [Cell; 0] values = [seed; 0];
                return 0;
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void SingletonRepeatInitializesItsOneLogicalElement()
    {
        var compilation = Compile("""
            public uint64 ReadSingleton() {
                RawWord seed = new RawWord(uint64(7));
                [RawWord; 1] values = [seed; 1];
                unsafe { return values[0].Wide; }
            }
            """);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void HighIndexWritesPreserveOtherElementsAndSupportRestoration()
    {
        var otherElement = Compile("""
            public uint64 ReadOtherAfterHighReplacement() {
                RawWord wide = new RawWord(uint64(7));
                init mut RawWord low;
                unsafe { low.Low = 1; }
                mut [RawWord; 5000000000] values = [wide; 5000000000];
                values[uint64(4294967296)] = low;
                unsafe { return values[uint64(4294967295)].Wide; }
            }
            """);
        Assert.That(otherElement.HasErrors, Is.False,
            string.Join(Environment.NewLine, otherElement.Diagnostics));

        var replacedElement = Compile("""
            public uint64 ReadHighAfterNarrowReplacement() {
                RawWord wide = new RawWord(uint64(7));
                init mut RawWord low;
                unsafe { low.Low = 1; }
                mut [RawWord; 5000000000] values = [wide; 5000000000];
                values[uint64(4294967296)] = low;
                unsafe { return values[uint64(4294967296)].Wide; }
            }
            """);
        Assert.That(replacedElement.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                replacedElement.Diagnostics));

        var restoredElement = Compile("""
            public uint64 ReadHighAfterRepeatedWrites() {
                RawWord wide = new RawWord(uint64(7));
                init mut RawWord low;
                unsafe { low.Low = 1; }
                mut [RawWord; 5000000000] values = [wide; 5000000000];
                values[uint64(4294967296)] = low;
                values[uint64(4294967296)] = wide;
                unsafe { return values[uint64(4294967296)].Wide; }
            }
            """);
        Assert.That(restoredElement.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                restoredElement.Diagnostics));
    }

    [Test]
    public void UnknownWritesMeetTheTemplateAndSingleElementWritesAreExact()
    {
        var dynamicRead = Compile("""
            public uint64 ReadAnyRepeatedElement(uint64 index) {
                RawWord wide = new RawWord(uint64(7));
                [RawWord; 5000000000] values = [wide; 5000000000];
                unsafe { return values[index].Wide; }
            }
            """);
        Assert.That(dynamicRead.HasErrors, Is.False,
            string.Join(Environment.NewLine, dynamicRead.Diagnostics));

        var unknownWrite = Compile("""
            public uint64 ReadAfterUnknownNarrowWrite(uint64 index) {
                RawWord wide = new RawWord(uint64(7));
                init mut RawWord low;
                unsafe { low.Low = 1; }
                mut [RawWord; 5000000000] values = [wide; 5000000000];
                values[index] = low;
                unsafe { return values[0].Wide; }
            }
            """);
        Assert.That(unknownWrite.HasErrors, Is.True,
            string.Join(Environment.NewLine, unknownWrite.Diagnostics));

        var nestedUnknownWrite = Compile("""
            public uint64 ReadAfterUnknownNestedNarrowWrite(
                uint64 rowIndex, uint64 elementIndex) {
                RawWord wide = new RawWord(uint64(7));
                init mut RawWord low;
                unsafe { low.Low = 1; }
                mut [[RawWord; 2]; 2] rows = [[wide; 2]; 2];
                rows[rowIndex][elementIndex] = low;
                unsafe { return rows[0][0].Wide; }
            }
            """);
        Assert.That(nestedUnknownWrite.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                nestedUnknownWrite.Diagnostics));

        var nestedUnknownSingletonWrite = Compile("""
            public uint64 ReadAfterUnknownOuterSingletonWrite(
                uint64 rowIndex, uint64 elementIndex) {
                RawWord wide = new RawWord(uint64(7));
                init mut RawWord low;
                unsafe { low.Low = 1; }
                mut [[RawWord; 1]; 2] rows = [[wide; 1]; 2];
                rows[rowIndex][elementIndex] = low;
                unsafe { return rows[0][0].Wide; }
            }
            """);
        Assert.That(nestedUnknownSingletonWrite.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                nestedUnknownSingletonWrite.Diagnostics));

        var knownSingletonIndexUnderUnknownAncestor = Compile("""
            public uint64 ReadAfterUnknownOuterKnownSingletonWrite(
                uint64 rowIndex) {
                RawWord wide = new RawWord(uint64(7));
                init mut RawWord low;
                unsafe { low.Low = 1; }
                mut [[RawWord; 1]; 2] rows = [[wide; 1]; 2];
                rows[rowIndex][0] = low;
                unsafe { return rows[0][0].Wide; }
            }
            """);
        Assert.That(knownSingletonIndexUnderUnknownAncestor.HasErrors,
            Is.True, string.Join(Environment.NewLine,
                knownSingletonIndexUnderUnknownAncestor.Diagnostics));

        var nestedUnknownFieldWrite = Compile("""
            public uint64 ReadAfterUnknownNestedNarrowFieldWrite(
                uint64 rowIndex, uint64 elementIndex) {
                RawWord wide = new RawWord(uint64(7));
                mut [[RawWord; 2]; 2] rows = [[wide; 2]; 2];
                unsafe { rows[rowIndex][elementIndex].Low = uint8(1); }
                unsafe { return rows[0][0].Wide; }
            }
            """);
        Assert.That(nestedUnknownFieldWrite.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                nestedUnknownFieldWrite.Diagnostics));

        var oneElementWrite = Compile("""
            public uint64 ReadAfterUnknownSingleElementWrite(uint64 index) {
                RawWord wide = new RawWord(uint64(7));
                init mut RawWord low;
                unsafe { low.Low = 1; }
                mut [RawWord; 1] values = [low; 1];
                values[index] = wide;
                unsafe { return values[0].Wide; }
            }
            """);
        Assert.That(oneElementWrite.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                oneElementWrite.Diagnostics));
    }

    [Test]
    public void BranchJoinsMeetTemplatesAndSparseOverrides()
    {
        var compilation = Compile("""
            public uint64 ReadCommonAfterAlternativeWrites(
                bool writeFirst, uint64 index) {
                RawWord wide = new RawWord(uint64(7));
                init mut RawWord low;
                unsafe { low.Low = 1; }
                mut [RawWord; 5000000000] values = [wide; 5000000000];
                if (writeFirst) {
                    values[uint64(4294967296)] = low;
                } else {
                    values[uint64(4294967295)] = low;
                }
                unsafe { return values[index].Wide; }
            }
            """);

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void BranchJoinsMeetDifferentRepeatTemplates()
    {
        var compilation = Compile("""
            public uint64 ReadWideAfterDifferentSeedJoin(bool chooseWide) {
                RawWord wide = new RawWord(uint64(7));
                init mut RawWord low;
                unsafe { low.Low = 1; }
                init mut [RawWord; 2] values;
                if (chooseWide) {
                    values = [wide; 2];
                } else {
                    values = [low; 2];
                }
                unsafe { return values[0].Wide; }
            }
            """);

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void LoopFixedPointMeetsRepeatTemplates()
    {
        var compilation = Compile("""
            public uint64 ReadWideAfterLoopTemplateJoin(bool writeLow) {
                RawWord wide = new RawWord(uint64(7));
                init mut RawWord low;
                unsafe { low.Low = 1; }
                mut [RawWord; 2] values = [wide; 2];
                while (writeLow) {
                    values = [low; 2];
                    break;
                }
                unsafe { return values[0].Wide; }
            }
            """);

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void PaddedAlternativeReplacementDoesNotInitializeItsPaddingHole()
    {
        var otherElement = Compile("""
            public uint64 ReadOtherAfterPaddedReplacement() {
                RawWord wide = new RawWord(uint64(7));
                mut RawWord padded = new RawWord(uint64(7));
                unsafe { padded.Padded = new PaddedValue(uint8(1), uint32(2)); }
                mut [RawWord; 5000000000] values = [wide; 5000000000];
                values[uint64(4294967296)] = padded;
                unsafe { return values[uint64(4294967295)].Wide; }
            }
            """);
        Assert.That(otherElement.HasErrors, Is.False,
            string.Join(Environment.NewLine, otherElement.Diagnostics));

        var replacedElement = Compile("""
            public uint64 ReadPaddedReplacementAsWide() {
                RawWord wide = new RawWord(uint64(7));
                mut RawWord padded = new RawWord(uint64(7));
                unsafe { padded.Padded = new PaddedValue(uint8(1), uint32(2)); }
                mut [RawWord; 5000000000] values = [wide; 5000000000];
                values[uint64(4294967296)] = padded;
                unsafe { return values[uint64(4294967296)].Wide; }
            }
            """);
        Assert.That(replacedElement.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                replacedElement.Diagnostics));
    }

    [Test]
    public void LowSubrangeWritesPreserveTheRepeatedWideTail()
    {
        var knownWrite = Compile("""
            public uint64 ReadWideAfterKnownLowWrite() {
                RawWord wide = new RawWord(uint64(7));
                mut [RawWord; 5000000000] values = [wide; 5000000000];
                unsafe { values[uint64(4294967296)].Low = 1; }
                unsafe { return values[uint64(4294967296)].Wide; }
            }
            """);
        Assert.That(knownWrite.HasErrors, Is.False,
            string.Join(Environment.NewLine, knownWrite.Diagnostics));

        var unknownWrite = Compile("""
            public uint64 ReadWideAfterUnknownLowWrite(uint64 index) {
                RawWord wide = new RawWord(uint64(7));
                mut [RawWord; 5000000000] values = [wide; 5000000000];
                unsafe { values[index].Low = 1; }
                unsafe { return values[0].Wide; }
            }
            """);
        Assert.That(unknownWrite.HasErrors, Is.False,
            string.Join(Environment.NewLine, unknownWrite.Diagnostics));
    }

    [Test]
    public void NestedAndFieldArrayLowWritesPreserveWideCoverage()
    {
        var nestedWriteOnly = Compile("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct RawBox {
                public mut RawWord Inner;
                public RawBox(RawWord value) {
                    Inner = value;
                }
            }

            public int32 WriteNestedLow(uint64 outer, uint64 inner) {
                RawWord wide = new RawWord(uint64(7));
                RawBox box = new RawBox(wide);
                mut [[RawBox; 2]; 2] rows = [[box; 2]; 2];
                unsafe { rows[outer][inner].Inner.Low = 1; }
                return 0;
            }
            """);
        Assert.That(nestedWriteOnly.HasErrors, Is.False,
            string.Join(Environment.NewLine, nestedWriteOnly.Diagnostics));

        var nestedReadOnly = Compile("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct RawBox {
                public mut RawWord Inner;
                public RawBox(RawWord value) {
                    Inner = value;
                }
            }

            public uint64 ReadNestedWide() {
                RawWord wide = new RawWord(uint64(7));
                RawBox box = new RawBox(wide);
                [[RawBox; 2]; 2] rows = [[box; 2]; 2];
                unsafe { return rows[0][1].Inner.Wide; }
            }
            """);
        Assert.That(nestedReadOnly.HasErrors, Is.False,
            string.Join(Environment.NewLine, nestedReadOnly.Diagnostics));

        var nestedPath = Compile("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct RawBox {
                public mut RawWord Inner;
                public RawBox(RawWord value) {
                    Inner = value;
                }
            }

            public uint64 ReadAfterNestedLowWrite(uint64 outer, uint64 inner) {
                RawWord wide = new RawWord(uint64(7));
                RawBox box = new RawBox(wide);
                mut [[RawBox; 2]; 2] rows = [[box; 2]; 2];
                unsafe { rows[outer][inner].Inner.Low = 1; }
                unsafe { return rows[0][1].Inner.Wide; }
            }
            """);
        Assert.That(nestedPath.HasErrors, Is.False,
            string.Join(Environment.NewLine, nestedPath.Diagnostics));

        var arrayFieldReadOnly = Compilation.CreateLibrary(
            SyntaxTree.Parse(s_rawWordDeclarations + """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public mut [RawWord; 2] Values;
            }

            public uint64 ReadArrayField() {
                RawWord wide = new RawWord(uint64(7));
                init mut Holder holder;
                holder.Values = [wide, wide];
                unsafe { return holder.Values[0].Wide; }
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));
        Assert.That(arrayFieldReadOnly.HasErrors, Is.False,
            string.Join(Environment.NewLine, arrayFieldReadOnly.Diagnostics));

        var arrayField = Compile("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public mut [RawWord; 2] Values;
            }

            public uint64 ReadAfterArrayFieldLowWrite(uint64 index) {
                RawWord wide = new RawWord(uint64(7));
                init mut Holder holder;
                holder.Values = [wide, wide];
                unsafe { holder.Values[index].Low = 1; }
                unsafe { return holder.Values[0].Wide; }
            }
            """);
        Assert.That(arrayField.HasErrors, Is.False,
            string.Join(Environment.NewLine, arrayField.Diagnostics));
    }

    [Test]
    public void RepeatedFieldArrayCopiesRetainSparseUnionFacts()
    {
        var preservedCopy = Compile("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public mut [RawWord; 2] Values;
            }

            public uint64 ReadCopiedFieldArrayAfterOverwrite() {
                RawWord wide = new RawWord(uint64(7));
                init mut RawWord low;
                unsafe { low.Low = 1; }
                init mut Holder holder;
                holder.Values = [wide; 2];
                Holder copy = holder;
                holder.Values[1] = low;
                unsafe { return copy.Values[1].Wide; }
            }
            """);
        Assert.That(preservedCopy.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                preservedCopy.Diagnostics));

        var narrowedSource = Compile("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public mut [RawWord; 2] Values;
            }

            public uint64 ReadNarrowedFieldArrayElement() {
                RawWord wide = new RawWord(uint64(7));
                init mut RawWord low;
                unsafe { low.Low = 1; }
                init mut Holder holder;
                holder.Values = [wide; 2];
                holder.Values[1] = low;
                unsafe { return holder.Values[1].Wide; }
            }
            """);
        Assert.That(narrowedSource.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                narrowedSource.Diagnostics));
    }

    [Test]
    public void NestedRepeatedUnionSeedCoversUnknownOuterIndexAfterSpecificWrite()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            s_rawWordDeclarations + """
            int32 Main() {
                init mut RawWord seed;
                unsafe { seed.Wide = uint64(0x1122334455667788); }
                mut [[RawWord; 2]; 2] rows = [[seed; 2]; 2];
                mut uint64 rowIndex = 1;
                mut uint64 elementIndex = 1;
                unsafe { rows[1][1].Low = uint8(0xAA); }
                unsafe {
                    return int32(rows[rowIndex][elementIndex].Wide);
                }
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RepeatedUnionRootStateSurvivesSnapshotScopeHandoff()
    {
        var compilation = Compile("""
            public int32 WriteUnknownOuterIndexAfterBranch(bool writeBranch,
                uint64 rowIndex) {
                RawWord seed = new RawWord(uint64(7));
                mut [[RawWord; 2]; 2] rows = [[seed; 2]; 2];
                if (writeBranch) {
                    unsafe { rows[1][1].Low = uint8(1); }
                }
                unsafe { rows[rowIndex][1].Low = uint8(2); }
                return 0;
            }
            """);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RepeatedFieldArrayCoverageSurvivesByValueCalls()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            s_rawWordDeclarations + """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public mut [RawWord; 2] Values;
            }

            public Holder Identity(Holder value) {
                return value;
            }

            int32 Main() {
                RawWord wide = new RawWord(uint64(7));
                init mut Holder holder;
                holder.Values = [wide; 2];
                Holder copy = Identity(holder);
                unsafe { return int32(copy.Values[1].Wide); }
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(7));

        var sparseCopy = Compilation.CreateLibrary(SyntaxTree.Parse(
            s_rawWordDeclarations + """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public mut [RawWord; 2] Values;
            }

            public Holder Identity(Holder value) {
                return value;
            }

            public uint64 RejectCopiedNarrowFieldArrayElement() {
                RawWord wide = new RawWord(uint64(7));
                init mut RawWord low;
                unsafe { low.Low = 1; }
                init mut Holder holder;
                holder.Values = [wide; 2];
                holder.Values[1] = low;
                Holder copy = Identity(holder);
                holder.Values[0] = low;
                unsafe { return copy.Values[1].Wide; }
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));
        Assert.That(sparseCopy.HasErrors, Is.True,
            string.Join(Environment.NewLine, sparseCopy.Diagnostics));
        Assert.That(sparseCopy.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"), Is.True,
            string.Join(Environment.NewLine, sparseCopy.Diagnostics));
    }

    [Test]
    public void RepeatedCallResultCacheMergesAcrossBranchSnapshots()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            s_rawWordDeclarations + """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public mut [RawWord; 2] Values;
            }

            public Holder Identity(Holder value) {
                return value;
            }

            int32 Check(bool mutateFirst) {
                RawWord wide = new RawWord(uint64(7));
                init mut Holder holder;
                holder.Values = [wide; 2];
                Holder copy = Identity(holder);
                init mut RawWord low;
                unsafe { low.Low = 1; }
                if (mutateFirst) {
                    holder.Values[0] = low;
                } else {
                    holder.Values[1] = low;
                }
                unsafe { return int32(copy.Values[1].Wide); }
            }

            int32 Main() { return Check(true); }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(7));
    }

    [Test]
    public void RepeatedCallResultMergesAlternativeCopyOrigins()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            s_rawWordDeclarations + """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public mut [RawWord; 2] Values;
            }

            public Holder Select(bool chooseFirst, Holder first,
                Holder second) {
                if (chooseFirst) {
                    return first;
                }
                return second;
            }

            int32 Main() {
                RawWord wide = new RawWord(uint64(7));
                init mut Holder first;
                first.Values = [wide; 2];
                init mut RawWord low;
                unsafe { low.Low = 1; }
                first.Values[0] = low;
                init mut Holder second;
                second.Values = [wide; 2];
                second.Values[1] = low;
                Holder copy = Select(true, first, second);
                unsafe { return int32(copy.Values[0].Low); }
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(1));
    }

    [Test]
    public void RepeatedNestedCallResultSnapshotRetainsItsOwnLease()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            s_rawWordDeclarations + """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public mut [RawWord; 2] Values;
            }

            public Holder Identity(Holder value) {
                return value;
            }

            int32 Main() {
                RawWord wide = new RawWord(uint64(7));
                init mut Holder holder;
                holder.Values = [wide; 2];
                Holder copy = Identity(Identity(holder));
                unsafe { return int32(copy.Values[1].Wide); }
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(7));
    }

    [Test]
    public void RepeatedFieldArrayCoverageSurvivesReceiverReturnCalls()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            s_rawWordDeclarations + """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public mut [RawWord; 2] Values;

                public Holder Snapshot() {
                    return this;
                }
            }

            int32 Main() {
                RawWord wide = new RawWord(uint64(7));
                init mut Holder holder;
                holder.Values = [wide; 2];
                Holder copy = holder.Snapshot();
                unsafe { return int32(copy.Values[1].Wide); }
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(7));
    }

    [Test]
    public void RepeatedFieldArrayCoverageSurvivesReferenceValueCalls()
    {
        static string Source(int selectedIndex)
        {
            return $$"""
                [Blittable]
                [Layout(LayoutKind.Sequential)]
                public struct Holder {
                    public mut [RawWord; 2] Values;
                }

                public Holder Snapshot(ref<Holder> source, int32 ignored) {
                    return *source;
                }

                int32 MutateElement(write ref<RawWord> element) {
                    init mut RawWord low;
                    unsafe { low.Low = 1; }
                    *element = low;
                    return 0;
                }

                int32 Main() {
                    RawWord wide = new RawWord(uint64(7));
                    init mut Holder holder;
                    holder.Values = [wide; 2];
                    Holder copy = Snapshot(ref holder,
                        MutateElement(ref holder.Values[1]));
                    unsafe {
                        return int32(copy.Values[{{selectedIndex}}].Wide);
                    }
                }
                """;
        }

        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var preserved = Compilation.Create(SyntaxTree.Parse(
            s_rawWordDeclarations + Source(0)),
            LayoutOptions(BoundRuntimePolicy.FailFast));
        Assert.That(preserved.HasErrors, Is.False,
            string.Join(Environment.NewLine, preserved.Diagnostics));
        Assert.That(NativeJit.Execute(preserved.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(7));

        var invalidated = Compilation.CreateLibrary(SyntaxTree.Parse(
            s_rawWordDeclarations + Source(1)),
            LayoutOptions(BoundRuntimePolicy.FailFast));
        Assert.That(invalidated.HasErrors, Is.True,
            string.Join(Environment.NewLine, invalidated.Diagnostics));
        Assert.That(invalidated.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"), Is.True,
            string.Join(Environment.NewLine, invalidated.Diagnostics));
    }

    [Test]
    public void RepeatedFieldArrayCoverageSurvivesAliasedReferenceValueCalls()
    {
        static string Source(int selectedIndex)
        {
            return $$"""
                [Blittable]
                [Layout(LayoutKind.Sequential)]
                public struct Holder {
                    public mut [RawWord; 2] Values;
                }

                public Holder Snapshot(ref<Holder> source, int32 ignored) {
                    return *source;
                }

                int32 MutateElement(write ref<RawWord> element) {
                    init mut RawWord low;
                    unsafe { low.Low = 1; }
                    *element = low;
                    return 0;
                }

                int32 Main() {
                    RawWord wide = new RawWord(uint64(7));
                    init mut Holder holder;
                    holder.Values = [wide; 2];
                    ref<Holder> snapshotSource = ref holder;
                    Holder copy = Snapshot(snapshotSource,
                        MutateElement(ref holder.Values[1]));
                    unsafe {
                        return int32(copy.Values[{{selectedIndex}}].Wide);
                    }
                }
                """;
        }

        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var preserved = Compilation.Create(SyntaxTree.Parse(
            s_rawWordDeclarations + Source(0)),
            LayoutOptions(BoundRuntimePolicy.FailFast));
        Assert.That(preserved.HasErrors, Is.False,
            string.Join(Environment.NewLine, preserved.Diagnostics));
        Assert.That(NativeJit.Execute(preserved.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(7));

        var invalidated = Compilation.CreateLibrary(SyntaxTree.Parse(
            s_rawWordDeclarations + Source(1)),
            LayoutOptions(BoundRuntimePolicy.FailFast));
        Assert.That(invalidated.HasErrors, Is.True,
            string.Join(Environment.NewLine, invalidated.Diagnostics));
        Assert.That(invalidated.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"), Is.True,
            string.Join(Environment.NewLine, invalidated.Diagnostics));
    }

    [Test]
    public void WritableReferenceMutationDoesNotPublishEntryReferentCoverage()
    {
        const string source = """
            public RawWord ReplaceAndReturn(
                write ref<RawWord> source, RawWord replacement) {
                *source = replacement;
                return *source;
            }

            uint64 RejectWideAfterReferentReplacement() {
                mut RawWord wide = new RawWord(uint64(7));
                init mut RawWord low;
                unsafe { low.Low = 1; }
                RawWord copy = ReplaceAndReturn(ref wide, low);
                unsafe { return copy.Wide; }
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            s_rawWordDeclarations + source),
            LayoutOptions(BoundRuntimePolicy.FailFast));

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void ReceiverReturnUsesArrayFactsAfterLaterArgumentEffects()
    {
        static string Source(int selectedIndex)
        {
            return $$"""
                [Blittable]
                [Layout(LayoutKind.Sequential)]
                public struct Holder {
                    public mut [RawWord; 2] Values;

                    public Holder SnapshotAfter(int32 ignored) {
                        return this;
                    }
                }

                int32 MutateElement(write ref<RawWord> element) {
                    init mut RawWord low;
                    unsafe { low.Low = 1; }
                    *element = low;
                    return 0;
                }

                int32 Main() {
                    RawWord wide = new RawWord(uint64(7));
                    init mut Holder holder;
                    holder.Values = [wide; 2];
                    Holder copy = holder.SnapshotAfter(
                        MutateElement(ref holder.Values[1]));
                    unsafe {
                        return int32(
                            copy.Values[{{selectedIndex}}].Wide);
                    }
                }
                """;
        }

        var preserved = Compilation.Create(SyntaxTree.Parse(
            s_rawWordDeclarations + Source(0)),
            LayoutOptions(BoundRuntimePolicy.FailFast));
        Assert.That(preserved.HasErrors, Is.False,
            string.Join(Environment.NewLine, preserved.Diagnostics));
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        Assert.That(NativeJit.Execute(preserved.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(7));

        var invalidated = Compilation.Create(SyntaxTree.Parse(
            s_rawWordDeclarations + Source(1)),
            LayoutOptions(BoundRuntimePolicy.FailFast));
        Assert.That(invalidated.HasErrors, Is.True,
            string.Join(Environment.NewLine, invalidated.Diagnostics));
        Assert.That(invalidated.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"), Is.True,
            string.Join(Environment.NewLine, invalidated.Diagnostics));
    }

    [Test]
    public void ElementFieldWritesRequireUnsafeAndMutableArrayStorage()
    {
        var missingUnsafe = Compile("""
            public int32 MissingUnsafe() {
                RawWord wide = new RawWord(uint64(7));
                mut [RawWord; 2] values = [wide; 2];
                values[0].Low = 1;
                return 0;
            }
            """);
        Assert.That(missingUnsafe.HasErrors, Is.True,
            string.Join(Environment.NewLine, missingUnsafe.Diagnostics));

        var immutableArray = Compile("""
            public int32 ImmutableArray() {
                RawWord wide = new RawWord(uint64(7));
                [RawWord; 2] values = [wide; 2];
                unsafe { values[0].Low = 1; }
                return 0;
            }
            """);
        Assert.That(immutableArray.HasErrors, Is.True,
            string.Join(Environment.NewLine, immutableArray.Diagnostics));
    }

    [Test]
    public void FieldWriteProvenanceUsesCapturedIndicesAndPreservesSiblings()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            struct PointerHolder {
                public ptr<uint32> Value;
                public mut uint32 Tag;
                public unsafe PointerHolder(ptr<uint32> value) {
                    Value = value;
                    Tag = 0;
                }
            }

            [Terrum.Interop.NativeImport("fixture.dll", "Observe")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 3)]
            internal extern int32 Observe(ptr<uint32> value);

            uint32 SetTag(write ref<int32> index) {
                *index = 0;
                return uint32(99);
            }

            int32 Main() {
                unsafe {
                    [uint32; 4] values = [10, 20, 30, 40];
                    PointerHolder first = new PointerHolder(&values[0]);
                    PointerHolder second = new PointerHolder(&values[1]);
                    mut [PointerHolder; 2] holders
                        = [first, second];
                    mut int32 index = 1;
                    holders[index++].Tag = SetTag(ref index);
                    mut int32 firstObservation
                        = Observe(holders[0].Value);
                    mut int32 secondObservation
                        = Observe(holders[1].Value);
                    return firstObservation + secondObservation;
                }
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void FieldWritePreservesPostRhsPointerSiblings()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            struct PointerHolder {
                public mut ptr<uint32> Value;
                public mut uint32 Tag;
                public unsafe PointerHolder(ptr<uint32> value, uint32 tag) {
                    unsafe { Value = value; }
                    Tag = tag;
                }
            }

            uint32 ChangeIndex(write ref<int32> index) {
                *index = 0;
                return uint32(99);
            }

            int32 Main() {
                unsafe {
                    mut [uint32; 3] values = [10, 20, 30];
                    mut [PointerHolder; 2] holders = [
                        new PointerHolder(&values[0], uint32(10)),
                        new PointerHolder(&values[2], uint32(20))];
                    mut int32 index = 1;
                    holders[index++].Tag = ChangeIndex(ref index);
                    return int32(*holders[0].Value)
                        + int32(*holders[1].Value) * 100
                        + int32(holders[1].Tag)
                        + index;
                }
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(3109));
    }

    [Test]
    public void FieldPointerReplacementTransfersBackingToTheSelectedElement()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            struct PointerHolder {
                public mut ptr<uint32> Value;
                public unsafe PointerHolder(ptr<uint32> value) {
                    Value = value;
                }
            }

            int32 Main() {
                [uint32; 3] values = [10, 20, 30];
                unsafe {
                    mut [PointerHolder; 2] holders = [
                        new PointerHolder(&values[0]); 2];
                    holders[1].Value = &values[1];
                    return int32(*holders[0].Value)
                        + int32(*holders[1].Value) * 100;
                }
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(2010));
    }

    [Test]
    public void DynamicFieldPointerReplacementMergesPossibleBackings()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            struct PointerHolder {
                public mut ptr<uint32> Value;
                public unsafe PointerHolder(ptr<uint32> value) {
                    Value = value;
                }
            }

            int32 Main() {
                [uint32; 3] values = [10, 20, 30];
                unsafe {
                    mut [PointerHolder; 2] holders = [
                        new PointerHolder(&values[0]); 2];
                    mut int32 index = 1;
                    holders[index].Value = &values[2];
                    return int32(*holders[0].Value)
                        + int32(*holders[1].Value) * 100;
                }
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(3010));
    }

    [Test]
    public void NestedArrayFieldPointerWritePreservesOtherElements()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            struct PointerHolder {
                public mut ptr<uint32> Value;
                public unsafe PointerHolder(ptr<uint32> value) {
                    Value = value;
                }
            }

            int32 Main() {
                [uint32; 3] values = [10, 20, 30];
                unsafe {
                    PointerHolder seed
                        = new PointerHolder(&values[0]);
                    mut [PointerHolder; 2] row = [seed; 2];
                    mut [[PointerHolder; 2]; 2] rows = [row; 2];
                    rows[1][1].Value = &values[2];
                    return int32(*rows[0][0].Value)
                        + int32(*rows[1][1].Value) * 100;
                }
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(3010));
    }

    [Test]
    public void RepeatedArrayFieldFunctionPointerWritePreservesCodeOrigin()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            struct CallbackBox {
                public mut [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> Callback;
                public CallbackBox([Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> callback) {
                    Callback = callback;
                }
            }

            unsafe [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> Select(
                [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> original,
                [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> replacement, int32 index) {
                CallbackBox seed = new CallbackBox(original);
                mut [CallbackBox; 2] callbacks = [seed; 2];
                unsafe {
                    callbacks[index].Callback = replacement;
                    return callbacks[index].Callback;
                }
            }

            unsafe [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> SelectKnown(
                [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> original,
                [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> replacement) {
                CallbackBox seed = new CallbackBox(original);
                mut [CallbackBox; 2] callbacks = [seed; 2];
                unsafe {
                    callbacks[1].Callback = replacement;
                    return callbacks[1].Callback;
                }
            }

            unsafe [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> SelectExplicit(
                [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> original,
                [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> replacement) {
                CallbackBox first = new CallbackBox(original);
                CallbackBox second = new CallbackBox(replacement);
                mut [CallbackBox; 2] callbacks = [first, second];
                unsafe { return callbacks[1].Callback; }
            }

            [Terrum.Interop.NativeCallable]
            int32 Increment(int32 value) { return 42; }

            [Terrum.Interop.NativeCallable]
            int32 Double(int32 value) { return 24; }

            int32 Main() {
                unsafe {
                    [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> known
                        = SelectKnown(&Increment, &Double);
                    if (known(0) != 24) { return -1; }
                    [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> explicitValue
                        = SelectExplicit(&Increment, &Double);
                    if (explicitValue(0) != 24) { return -2; }
                    [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> selected
                        = Select(&Increment, &Double, 1);
                    return selected(21);
                }
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var select = compilation.Program!.Functions.Single(
            static function => function.Name == "Select");
        Assert.That(select.CopyOriginSummary, Is.Not.Null);
        Assert.That(select.CopyOriginSummary!.Facts, Has.Length.EqualTo(1));
        Assert.That(select.CopyOriginSummary.Facts[0].Origins
            .Select(static origin => origin.SourceOrdinal),
            Is.EquivalentTo(s_twoCopyOriginOrdinals));
        var selectKnown = compilation.Program.Functions.Single(
            static function => function.Name == "SelectKnown");
        Assert.That(selectKnown.CopyOriginSummary, Is.Not.Null);
        Assert.That(selectKnown.CopyOriginSummary!.Facts[0].Origins
            .Select(static origin => origin.SourceOrdinal),
            Is.EqualTo(s_singleCopyOriginOrdinal));
        var selectExplicit = compilation.Program.Functions.Single(
            static function => function.Name == "SelectExplicit");
        Assert.That(selectExplicit.CopyOriginSummary, Is.Not.Null);
        Assert.That(selectExplicit.CopyOriginSummary!.Facts[0].Origins
            .Select(static origin => origin.SourceOrdinal),
            Is.EqualTo(s_singleCopyOriginOrdinal));
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(24));
    }

    [Test]
    public void RepeatedArrayFieldWriteCannotExtendLocalPointerBacking()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            struct PointerHolder {
                public mut ptr<uint32> Value;
                public unsafe PointerHolder(ptr<uint32> value) {
                    Value = value;
                }
            }

            [Terrum.Interop.NativeImport("fixture.dll", "Observe")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern int32 Observe(ptr<uint32> value);

            int32 Main() {
                unsafe {
                    mut [PointerHolder; 1] holders
                        = [new PointerHolder(null); 1];
                    {
                        mut [uint32; 1] local = [10];
                        holders[0].Value = &local[0];
                    }
                    return Observe(holders[0].Value);
                }
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NativeFieldWriteCapturesTheSelectedIndexBeforeRhs()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            struct TaggedValue {
                public mut uint32 Tag;
                public TaggedValue(uint32 tag) { Tag = tag; }
            }

            [Blittable]
            [Layout(LayoutKind.Sequential)]
            struct Holder {
                public mut [TaggedValue; 2] Values;
            }

            uint32 ChangeIndex(write ref<int32> index) {
                *index = 0;
                return uint32(99);
            }

            int32 Main() {
                mut int32 index = 1;
                init mut Holder holder;
                holder.Values = [
                    new TaggedValue(uint32(10)),
                    new TaggedValue(uint32(20))];
                unsafe {
                    holder.Values[index++].Tag = ChangeIndex(ref index);
                }
                unsafe {
                    return int32(holder.Values[0].Tag)
                        + int32(holder.Values[1].Tag) * 100
                        + index;
                }
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(9910));
    }

    [Test]
    public void FailedArrayFieldRhsDoesNotPublishTheReplacement()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            struct TaggedValue {
                public mut uint32 Tag;
                public TaggedValue(uint32 tag) { Tag = tag; }
            }

            uint32 FailAfterIndexMutation(write ref<int32> index) {
                *index = 0;
                require false;
                return uint32(99);
            }

            int32 Main() {
                mut int32 index = 1;
                mut [TaggedValue; 2] values = [
                    new TaggedValue(uint32(10)),
                    new TaggedValue(uint32(20))];
                unsafe {
                    try {
                        values[index++].Tag
                            = FailAfterIndexMutation(ref index);
                    } catch {
                        return int32(values[0].Tag)
                            + int32(values[1].Tag) * 100
                            + index;
                    }
                }
                return -1;
            }
            """), LayoutOptions(BoundRuntimePolicy.CheckedExceptions));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(2010));
    }

    [Test]
    public void FailedRepeatSeedDoesNotPublishTheReplacement()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            struct TaggedValue {
                public mut uint32 Tag;
                public TaggedValue(uint32 tag) { Tag = tag; }
            }

            TaggedValue FailRepeatSeed(write ref<int32> index) {
                *index = 0;
                require false;
                return new TaggedValue(uint32(99));
            }

            int32 Main() {
                mut int32 index = 1;
                mut [TaggedValue; 2] values = [
                    new TaggedValue(uint32(10)),
                    new TaggedValue(uint32(20))];
                unsafe {
                    try {
                        values = [FailRepeatSeed(ref index); 2];
                    } catch {
                        return int32(values[0].Tag)
                            + int32(values[1].Tag) * 100
                            + index;
                    }
                }
                return -1;
            }
            """), LayoutOptions(BoundRuntimePolicy.CheckedExceptions));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(2010));
    }

    [Test]
    public void FieldWriteInvalidatesOverlappingUnionPointerProvenance()
    {
        static string CreateSource(string alternativeWrite)
        {
            return $$"""
                [Blittable]
                [Layout(LayoutKind.Union)]
                public struct PointerOrWide {
                    public mut write ptr<uint32> Pointer;
                    public mut uint64 Wide;
                    public PointerOrWide(write ptr<uint32> pointer) {
                        unsafe { Pointer = pointer; }
                    }
                }

                int32 Main() {
                    mut uint32 data = 42;
                    unsafe {
                        mut PointerOrWide seed
                            = new PointerOrWide(&data);
                        mut [PointerOrWide; 2] values = [seed; 2];
                        {{alternativeWrite}}
                        return int32(*values[1].Pointer);
                    }
                }
                """;
        }

        var preserved = Compilation.Create(
            SyntaxTree.Parse(CreateSource(string.Empty)),
            LayoutOptions(BoundRuntimePolicy.FailFast));
        Assert.That(preserved.HasErrors, Is.False,
            string.Join(Environment.NewLine, preserved.Diagnostics));
        Assert.That(NativeJit.Execute(preserved.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));

        var invalidated = Compilation.Create(
            SyntaxTree.Parse(CreateSource(
                "unsafe { values[1].Wide = uint64(0); }")),
            LayoutOptions(BoundRuntimePolicy.FailFast));
        Assert.That(invalidated.HasErrors, Is.True,
            string.Join(Environment.NewLine, invalidated.Diagnostics));
        Assert.That(invalidated.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"), Is.True,
            string.Join(Environment.NewLine, invalidated.Diagnostics));
    }

    [Test]
    public void NativeFieldSuffixStoreUsesElementOffsetAndPreservesTail()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            s_rawWordDeclarations + """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct RawBox {
                public uint32 Tag;
                public mut RawWord Inner;
                public RawBox(uint32 tag, RawWord value) {
                    Tag = tag;
                    Inner = value;
                }
            }

            int32 Main() {
                RawWord wide = new RawWord(
                    uint64(0x0706_0504_0302_0100));
                RawBox box = new RawBox(19, wide);
                mut [RawBox; 2] values = [box, box];
                unsafe { values[1].Inner.Low = uint8(0xAA); }
                unsafe {
                    if (values[1].Inner.Wide
                        == uint64(0x0706_0504_0302_01AA)) {
                        return 0;
                    }
                }
                return 1;
            }
            """), LayoutOptions(BoundRuntimePolicy.FailFast));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(
            compilation.Program
                ?? throw new AssertionException(
                    "Expected the fixed-array field-write program."),
            NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public void MaximumLayoutValidExtentDoesNotExpandSemanticState()
    {
        var compilation = Compile("""
            public uint64 ReadMaximumLayoutElement() {
                const uint64 H = 0x1FFF_FFFF_FFFF_FFFF;
                RawWord seed = new RawWord(uint64(7));
                [RawWord; H] values = [seed; H];
                unsafe { return values[H - 1].Wide; }
            }
            """);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void SparseUnionUpdatesStopAtThePublicWorkLimit()
    {
        var withinLimit = CompileSparseUnionUpdates(150);
        Assert.That(withinLimit.HasErrors, Is.False,
            string.Join(Environment.NewLine, withinLimit.Diagnostics));

        var overLimit = CompileSparseUnionUpdates(151);

        Assert.That(overLimit.HasErrors, Is.True,
            string.Join(Environment.NewLine, overLimit.Diagnostics));
        Assert.That(overLimit.Diagnostics.Any(diagnostic =>
                diagnostic.Code == "TRM3016"
                && diagnostic.Message.Contains("work limit",
                    StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, overLimit.Diagnostics));
    }

    private const string s_rawWordDeclarations = """
        [Blittable]
        [Layout(LayoutKind.Sequential)]
        public struct PaddedValue {
            public uint8 First;
            public uint32 Last;
            public PaddedValue(uint8 first, uint32 last) {
                First = first;
                Last = last;
            }
        }

        [Blittable]
        [Layout(LayoutKind.Union)]
        public struct RawWord {
            public mut uint8 Low;
            public mut uint64 Wide;
            public mut PaddedValue Padded;
            public RawWord(uint64 value) {
                unsafe { Wide = value; }
            }
        }
        """;

    private static Compilation Compile(string body)
        => Compilation.CreateLibrary(
            SyntaxTree.Parse(s_rawWordDeclarations + body),
            LayoutOptions(BoundRuntimePolicy.FailFast));

    private static Compilation CompileSparseUnionUpdates(int updateCount)
    {
        const int arrayExtent = 256;
        var updates = string.Join(Environment.NewLine,
            Enumerable.Range(0, updateCount).Select(static index =>
                $"values[{index}] = low;"));
        return Compile($$"""
            public int32 UpdateSparseUnionState() {
                RawWord seed = new RawWord(uint64(7));
                init mut RawWord low;
                unsafe { low.Low = 1; }
                mut [RawWord; {{arrayExtent}}] values
                    = [seed; {{arrayExtent}}];
            {{updates}}
                return 0;
            }
            """);
    }
}
