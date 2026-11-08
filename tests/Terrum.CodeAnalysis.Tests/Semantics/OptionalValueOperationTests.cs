// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class OptionalValueOperationTests
{
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void TaggedScalarOperationsPreserveAbsenceAndZero(NativeRuntimePolicy policy)
    {
        const string source = """
            mut class Holder {
                public mut int32? Value;
                public Holder() { Value = null; }
            }
            int32? Identity(int32? value) { return value; }
            int64? IdentityWide(int64? value) { return value; }
            int32 Main() {
                int32? absent = null;
                int32 rawZero = 0;
                int64 rawWideZero = 0;
                int32? zero = rawZero;
                int64? wideZero = rawWideZero;
                int32? copiedZero = Identity(zero);
                int64? copiedWideZero = IdentityWide(wideZero);
                mut int32? assigned = null;
                assigned ??= rawZero;
                if (!(absent is null)) { return 1; }
                if (absent is some unreachable) { return 2; }
                if (zero is null) { return 3; }
                if (!(zero is some payload)) { return 4; }
                if (payload != 0) { return 5; }
                if (!(assigned is some assignedPayload)) { return 6; }
                if (assignedPayload != 0) { return 7; }
                if ((absent ?? 42) != 42 || (zero ?? 42) != 0) { return 8; }
                if ((copiedZero ?? 42) != 0
                    || (copiedWideZero ?? 42) != 0) { return 9; }
                require Holder.TryCreate(out var holder);
                holder.Value ??= rawZero;
                if (!(holder.Value is some fieldValue)) { return 10; }
                if (fieldValue != 0 || (holder.Value ?? 42) != 0) { return 11; }
                return 42;
            }
            """;

        Assert.That(NativeJit.Execute(Compile(source, policy), policy), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void IndirectAggregateOperationsPreservePayloads(NativeRuntimePolicy policy)
    {
        const string source = """
            struct Pair {
                public int32 First;
                public int32 Second;
                public Pair(int32 first, int32 second) {
                    First = first;
                    Second = second;
                }
            }
            Pair? IdentityPair(Pair? value) { return value; }
            bool CopyPairIfPresent(Pair? value, out Pair? output) {
                if (value is null) { return false; }
                output = value;
                return true;
            }
            mut class PairHolder {
                public mut Pair? Value;
                public PairHolder() { Value = null; }
            }
            int32 Main() {
                Pair pair = new Pair(10, 32);
                Pair? absent = null;
                Pair? present = pair;
                if (!(present is some snapshot)) { return 1; }
                if (snapshot.First != 10 || snapshot.Second != 32) { return 2; }
                Pair picked = absent ?? pair;
                if (picked.First != 10 || picked.Second != 32) { return 3; }
                Pair? copied = present ?? null;
                if (!(copied is some copiedPayload)) { return 4; }
                if (copiedPayload.First != 10 || copiedPayload.Second != 32) { return 5; }
                Pair? returned = IdentityPair(present);
                if (!(returned is some returnedPayload)) { return 6; }
                if (returnedPayload.First != 10
                    || returnedPayload.Second != 32) { return 7; }
                mut Pair? copiedOut = null;
                if (!CopyPairIfPresent(returned, out copiedOut)) {
                    return 8;
                }
                if (!(copiedOut is some outPayload)
                    || outPayload.First != 10
                    || outPayload.Second != 32) { return 9; }
                mut Pair? vacantOutput = null;
                if (CopyPairIfPresent(absent, out vacantOutput)) {
                    return 10;
                }
                mut Pair? assigned = null;
                assigned ??= pair;
                if (!(assigned is some assignedPayload)) { return 11; }
                if (assignedPayload.First != 10 || assignedPayload.Second != 32) { return 12; }
                require PairHolder.TryCreate(out var holder);
                holder.Value ??= pair;
                if (!(holder.Value is some fieldPayload)) { return 13; }
                if (fieldPayload.First != 10 || fieldPayload.Second != 32) { return 14; }
                return 42;
            }
            """;

        Assert.That(NativeJit.Execute(Compile(source, policy), policy), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void OptionalRawPointerRetainsItsNullNiche(NativeRuntimePolicy policy)
    {
        const string source = """
            int32 Main() {
                unsafe {
                    int32 value = 42;
                    ptr<int32> pointer = &value;
                    ptr<int32>? absent = null;
                    ptr<int32> selected = absent ?? pointer;
                    ptr<int32>? present = &value;
                    if (present is some address) {
                        if (*address == 42) { return *selected; }
                    }
                }
                return 0;
            }
            """;

        Assert.That(NativeJit.Execute(Compile(source, policy), policy), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void OptionalValuesRetainCheckedReferenceFlow(
        NativeRuntimePolicy policy)
    {
        const string source = """
            struct Pair {
                public int32 First;
                public int32 Second;
                public Pair(int32 first, int32 second) {
                    First = first;
                    Second = second;
                }
            }
            int32 Main() {
                Pair raw = new Pair(10, 32);
                mut Pair? local = null;
                mut write ref<Pair?> current = ref local;
                if (!(*current is null)) { return 1; }
                *current = raw;
                if (!(*current is some value)
                    || value.First != 10
                    || value.Second != 32) { return 2; }
                return 42;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions(
                (BoundRuntimePolicy)policy));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException(
                "A successful compilation must contain a bound program.");
        Assert.That(NativeJit.Execute(program, policy,
                nativeLoweringAgreement: program.NativeLoweringAgreement),
            Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void OptionalFunctionPointerUsesTaggedLayout(NativeRuntimePolicy policy)
    {
        const string source = """
            int32 Increment(int32 value) { return value + 1; }
            int32 Main() {
                unsafe {
                    fnptr<int32, int32>? optional = &Increment;
                    if (optional is some invoke) { return invoke(41); }
                }
                return 0;
            }
            """;

        Assert.That(NativeJit.Execute(Compile(source, policy), policy), Is.EqualTo(42));
    }

    [Test]
    public void OptionalRawUnionRequiresAnActiveCaseCopyContract()
    {
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Union)]
            struct Overlay {
                public uint32 Small;
                public uint64 Wide;
            }
            int32 Main() {
                Overlay? value = null;
                if (value is null) { return 42; }
                return 0;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void UnitOptionsDistinguishAbsenceFromPresentUnit(NativeRuntimePolicy policy)
    {
        const string source = """
            int32 Main() {
                mut option<void> absent = null;
                option<void> present = ();
                if (!(absent is null)) { return 1; }
                if (present is null) { return 2; }
                absent ??= ();
                if (absent is null) { return 3; }
                option<void> selected = absent ?? null;
                if (selected is null) { return 4; }
                return 42;
            }
            """;

        Assert.That(NativeJit.Execute(Compile(source, policy), policy), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void TaggedOptionalFixedArrayElementsPreservePresenceAndPayload(
        NativeRuntimePolicy policy)
    {
        const string source = """
            int32 Main() {
                int32 raw = 42;
                mut [int32?; 3] values = [null, raw, null];
                if (!(values[0] is null)) { return 1; }
                if (!(values[1] is some first) || first != 42) { return 2; }
                values[0] = raw;
                if (!(values[0] is some written) || written != 42) { return 3; }
                [int32?; 3] copy = values;
                if (!(copy[1] is some copied) || copied != 42) { return 4; }
                int32 alternative = 99;
                values[1] ??= alternative;
                values[0] ??= raw;
                if (!(values[0] is some assigned)
                    || !(values[1] is some preserved)
                    || assigned != 42 || preserved != 42) { return 5; }
                mut [[int32?; 2]; 2] nested = [
                    [null, raw],
                    [raw, null]];
                nested[0][0] ??= raw;
                if (!(nested[0][0] is some nestedAssigned)
                    || !(nested[1][0] is some nestedPreserved)
                    || nestedAssigned != 42 || nestedPreserved != 42) {
                    return 6;
                }
                [[int32?; 2]; 2] nestedCopy = nested;
                if (!(nestedCopy[0][0] is some nestedCopied)
                    || !(nestedCopy[1][1] is null)
                    || nestedCopied != 42) { return 7; }
                mut [int32?; 3] repeatedAbsent = [null; 3];
                if (!(repeatedAbsent[2] is null)) { return 8; }
                mut [int32?; 2] repeatedPresent = [raw; 2];
                if (!(repeatedPresent[1] is some repeated)
                    || repeated != 42) { return 9; }
                mut [int32?; 2] seedRow = [raw, null];
                mut [[int32?; 2]; 2] repeatedRows = [seedRow; 2];
                if (!(repeatedRows[1][0] is some repeatedRow)
                    || !(repeatedRows[1][1] is null)
                    || repeatedRow != 42) { return 10; }
                return 42;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions(
                (BoundRuntimePolicy)policy));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, policy), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void TaggedOptionalAggregateFixedArrayElementsPreservePayload(
        NativeRuntimePolicy policy)
    {
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            struct Pair {
                public int32 First;
                public int32 Second;
                public Pair(int32 first, int32 second) {
                    First = first;
                    Second = second;
                }
            }
            int32 Main() {
                unsafe {
                    Pair pair = new Pair(10, 32);
                    mut [Pair?; 3] values = [null, pair, null];
                    if (!(values[0] is null)) { return 1; }
                    if (!(values[1] is some first) || first.First != 10
                        || first.Second != 32) { return 2; }
                    values[0] = pair;
                    if (!(values[0] is some written)
                        || written.First != 10 || written.Second != 32) {
                        return 3;
                    }
                    [Pair?; 3] copy = values;
                    if (!(copy[1] is some copied)
                        || copied.First != 10 || copied.Second != 32) {
                        return 4;
                    }
                    mut [Pair?; 3] repeatedAbsent = [null; 3];
                    if (!(repeatedAbsent[2] is null)) { return 5; }
                    mut [Pair?; 2] repeatedPresent = [pair; 2];
                    if (!(repeatedPresent[1] is some repeated)
                        || repeated.First != 10
                        || repeated.Second != 32) { return 6; }
                    return 42;
                }
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions(
                (BoundRuntimePolicy)policy));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, policy),
            Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void OptionalAggregatePayloadsRetainNestedOptionalFields(
        NativeRuntimePolicy policy)
    {
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            struct Envelope {
                public mut int32? Value;
                public int32 Marker;
                public Envelope(int32? value, int32 marker) {
                    Value = value;
                    Marker = marker;
                }
            }
            int32 Main() {
                int32 raw = 42;
                Envelope absentField = new Envelope(null, 13);
                Envelope presentField = new Envelope(raw, 29);
                mut Envelope conditional = new Envelope(null, 37);
                unsafe {
                    conditional.Value ??= raw;
                    if (!(conditional.Value is some conditionalPayload)
                        || conditionalPayload != 42) { return 1; }
                }
                mut [Envelope?; 2] values = [absentField, presentField];
                if (!(values[0] is some first)
                    || !(values[1] is some second)) { return 2; }
                unsafe {
                    if (!(first.Value is null) || first.Marker != 13) {
                        return 3;
                    }
                    if (!(second.Value is some payload)
                        || payload != 42 || second.Marker != 29) {
                        return 4;
                    }
                }
                [Envelope?; 2] copy = values;
                if (!(copy[0] is some copied)) { return 5; }
                unsafe {
                    if (!(copied.Value is null) || copied.Marker != 13) {
                        return 6;
                    }
                    mut [Envelope; 2] repeatedValues = [absentField; 2];
                    if (!(repeatedValues[1].Value is null)
                        || repeatedValues[1].Marker != 13) { return 7; }
                }
                mut [Envelope?; 2] repeatedOptions = [absentField; 2];
                if (!(repeatedOptions[1] is some repeatedPayload)) {
                    return 8;
                }
                unsafe {
                    if (!(repeatedPayload.Value is null)
                        || repeatedPayload.Marker != 13) { return 9; }
                }
                return 42;
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions(
                (BoundRuntimePolicy)policy));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, policy),
            Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void OptionalPointerFixedArrayElementsRetainTheNullNiche(
        NativeRuntimePolicy policy)
    {
        const string source = """
            int32 Main() {
                unsafe {
                    int32 value = 42;
                    ptr<int32> pointer = &value;
                    mut [ptr<int32>?; 2] pointers = [null, pointer];
                    if (!(pointers[0] is null)) { return 1; }
                    pointers[0] ??= pointer;
                    if (!(pointers[0] is some first) || *first != 42) {
                        return 2;
                    }
                    if (!(pointers[1] is some preserved)
                        || *preserved != 42) { return 3; }
                    return 42;
                }
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions(
                (BoundRuntimePolicy)policy));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, policy),
            Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void OptionalFixedArrayCopiesPreserveFloatingPayloadBits(
        NativeRuntimePolicy policy)
    {
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Union)]
            struct FloatBits {
                public mut float32 Value;
                public mut uint32 Bits;
                public FloatBits(uint32 bits) {
                    unsafe { Bits = bits; }
                }
            }
            uint32 GetBits(float32 value) {
                init mut FloatBits bits;
                unsafe {
                    bits.Value = value;
                    return bits.Bits;
                }
            }
            int32 Main() {
                unsafe {
                    FloatBits zeroBits = new FloatBits(uint32(0x80000000));
                    float32 negativeZero = zeroBits.Value;
                    mut [float32?; 2] values = [negativeZero, null];
                    [float32?; 2] zeroCopy = values;
                    if (!(zeroCopy[0] is some copiedZero)
                        || GetBits(copiedZero) != uint32(0x80000000)) {
                        return 1;
                    }
                    uint32 nanPayload = uint32(0x7FC01234);
                    FloatBits nanBits = new FloatBits(nanPayload);
                    values[0] = nanBits.Value;
                    [float32?; 2] nanCopy = values;
                    if (!(nanCopy[0] is some copiedNaN)
                        || GetBits(copiedNaN) != nanPayload) { return 2; }
                    return 42;
                }
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions(
                (BoundRuntimePolicy)policy));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, policy),
            Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void RequiredAlignmentAboveNaturalAlignmentSurvivesOptionalArrays(
        NativeRuntimePolicy policy)
    {
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential, Alignment: 32)]
            struct Payload {
                public uint64 First;
                public uint64 Second;
                public uint64 Third;
                public uint64 Fourth;
                public Payload(uint64 first) {
                    First = first;
                    Second = 2;
                    Third = 3;
                    Fourth = 4;
                }
            }
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 8)]
            struct Holder {
                public Payload Value;
                public Holder(Payload value) { Value = value; }
            }
            int32 Main() {
                unsafe {
                    Payload payload = new Payload(1);
                    Holder holder = new Holder(payload);
                    if (payload.Fourth != 4
                        || holder.Value.Fourth != 4) { return 1; }
                    mut [Holder?; 3] values = [null, holder, null];
                    if (!(values[1] is some current)
                        || current.Value.First != 1
                        || current.Value.Fourth != 4) { return 2; }
                    values[0] ??= holder;
                    [Holder?; 3] copy = values;
                    if (!(copy[0] is some copied)
                        || copied.Value.Second != 2
                        || copied.Value.Third != 3) { return 3; }
                    mut [Holder?; 2] repeated = [holder; 2];
                    if (!(repeated[1] is some repeatedPayload)
                        || repeatedPayload.Value.First != 1
                        || repeatedPayload.Value.Fourth != 4) {
                        return 4;
                    }
                    return 42;
                }
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions(
                (BoundRuntimePolicy)policy));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException(
                "A successful compilation must contain a bound program.");
        var holderLayout = program.Structures.Single(
            static structure => structure.Name == "Holder");
        Assert.That((holderLayout.Size, holderLayout.Alignment,
            holderLayout.RequiredAlignment), Is.EqualTo((32, 32, 32)));
        var arrayLayout = program.NativeFixedArrayTypes.Single(
            static array => array.ElementCount == 3);
        Assert.That((arrayLayout.ElementStride, arrayLayout.Alignment,
            arrayLayout.RequiredAlignment), Is.EqualTo((64UL, 32UL, 32UL)));
        Assert.That(ValueLayout.TryGetOptionalValueLayout(
                arrayLayout.ElementType, program.Structures,
                program.CheckedReferenceTypes,
                program.NativeLoweringAgreement,
                program.NativeFixedArrayTypes, out var optionalLayout),
            Is.True);
        Assert.That((optionalLayout.Alignment,
            optionalLayout.RequiredAlignment, optionalLayout.PayloadOffset,
            optionalLayout.Size), Is.EqualTo((8, 32, 32UL, 64UL)));
        var nativeCode = NativeEmitter.EmitCode(program, policy);
        var alignedFrames = nativeCode.Functions.Where(
            static function => function.UsesAlignedFrame).ToArray();
        Assert.That(alignedFrames, Is.Not.Empty);
        Assert.That(alignedFrames.All(
            static function => function.StackAlignment == 32), Is.True);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void NullablePatternChecksInitializationBeforeReadingOptionalValue(
        NativeRuntimePolicy policy)
    {
        const string source = """
            int32 Main() {
                init int32? vacant;
                if (vacant is var? capturedVacant) { return 1; }
                int32? initializedNull = null;
                if (!(initializedNull is var? capturedNull)) { return 2; }
                if (!(capturedNull is null)) { return 3; }
                int32 rawZero = 0;
                int32? zero = rawZero;
                if (!(zero is var present)) { return 4; }
                if (present != 0) { return 5; }
                return 42;
            }
            """;

        Assert.That(NativeJit.Execute(Compile(source, policy), policy), Is.EqualTo(42));
    }

    [Test]
    public void NullTestDoesNotReadVacantOptionalStorage()
    {
        const string source = """
            int32 Main() {
                init int32? vacant;
                if (vacant is null) { return 42; }
                return 0;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    private static BoundProgram Compile(string source, NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return compilation.Program!;
    }
}
