// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Tests.Semantics;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Storage;

public sealed class NativeFixedArrayStorageTests
{
    private static BoundNativeFixedArrayLayout GetFixedArrayLayout(
        BoundProgram program, string variableName)
    {
        var variable = program.Functions[program.EntryPoint].Variables
            .Single(candidate => candidate.Name == variableName);
        Assert.That(NativeFixedArrayContract.TryGetLayout(variable.Type,
            program.Structures, program.NativeFixedArrayTypes, out var layout),
            Is.True);
        return layout;
    }

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

        var arrayLayout = GetFixedArrayLayout(program, "values");
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
    public void RawUnionPayloadCoverageRemainsPerFixedArrayElement()
    {
        const string prefix = """
            [Blittable] [Layout(Sequential)]
            public struct Sparse {
                public uint8 First;
                public uint32 Last;
                public Sparse(uint8 first, uint32 last) {
                    First = first;
                    Last = last;
                }
            }

            [Blittable] [Layout(Union)]
            public struct RawWord {
                public mut uint64 Wide;
                public mut Sparse Padded;
                public RawWord(uint64 value) {
                    unsafe { Wide = value; }
                }
            }

            public RawWord MakePadded() {
                mut RawWord value = new RawWord(0);
                unsafe { value.Padded = new Sparse(1, 2); }
                return value;
            }
            """;

        var firstWide = Compilation.CreateLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 ReadFirstWide() {
                    [RawWord; 2] values = [
                        new RawWord(1), MakePadded()];
                    unsafe { return values[0].Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(firstWide.HasErrors, Is.False,
            string.Join(Environment.NewLine, firstWide.Diagnostics));

        var secondWide = Compilation.CreateLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 RejectSecondWide() {
                    [RawWord; 2] values = [
                        new RawWord(1), MakePadded()];
                    unsafe { return values[1].Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(secondWide.HasErrors, Is.True,
            string.Join(Environment.NewLine, secondWide.Diagnostics));

        var dynamicAllWide = Compilation.CreateLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 ReadDynamicWide(int32 index) {
                    [RawWord; 2] values = [
                        new RawWord(1), new RawWord(2)];
                    unsafe { return values[index].Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(dynamicAllWide.HasErrors, Is.False,
            string.Join(Environment.NewLine, dynamicAllWide.Diagnostics));

        var dynamicMixed = Compilation.CreateLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 RejectDynamicWide(int32 index) {
                    [RawWord; 2] values = [
                        new RawWord(1), MakePadded()];
                    unsafe { return values[index].Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(dynamicMixed.HasErrors, Is.True,
            string.Join(Environment.NewLine, dynamicMixed.Diagnostics));

        var constantElementWrite = Compilation.CreateLibrary(
            SyntaxTree.Parse(prefix + """
                public uint64 PreserveUnwrittenElementWide() {
                    mut [RawWord; 2] values = [
                        new RawWord(1), new RawWord(2)];
                    values[1] = MakePadded();
                    unsafe { return values[0].Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(constantElementWrite.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                constantElementWrite.Diagnostics));

        var constantWrittenElement = Compilation.CreateLibrary(
            SyntaxTree.Parse(prefix + """
                public uint64 RejectOverwrittenElementWide() {
                    mut [RawWord; 2] values = [
                        new RawWord(1), new RawWord(2)];
                    values[1] = MakePadded();
                    unsafe { return values[1].Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(constantWrittenElement.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                constantWrittenElement.Diagnostics));

        var unknownElementWrite = Compilation.CreateLibrary(
            SyntaxTree.Parse(prefix + """
                public uint64 RejectElementAfterUnknownWrite(int32 index) {
                    mut [RawWord; 2] values = [
                        new RawWord(1), new RawWord(2)];
                    values[index] = MakePadded();
                    unsafe { return values[0].Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(unknownElementWrite.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                unknownElementWrite.Diagnostics));

        var branchElementWrite = Compilation.CreateLibrary(
            SyntaxTree.Parse(prefix + """
                public uint64 PreserveOtherElementAfterBranchWrite(
                    bool replace) {
                    mut [RawWord; 2] values = [
                        new RawWord(1), new RawWord(2)];
                    if (replace) {
                        values[0] = MakePadded();
                    }
                    unsafe { return values[1].Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(branchElementWrite.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                branchElementWrite.Diagnostics));

        var branchWrittenElement = Compilation.CreateLibrary(
            SyntaxTree.Parse(prefix + """
                public uint64 RejectElementAfterBranchWrite(bool replace) {
                    mut [RawWord; 2] values = [
                        new RawWord(1), new RawWord(2)];
                    if (replace) {
                        values[0] = MakePadded();
                    }
                    unsafe { return values[0].Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(branchWrittenElement.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                branchWrittenElement.Diagnostics));

        var copiedSnapshot = Compilation.CreateLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 CopyBeforeElementOverwrite() {
                    mut [RawWord; 2] values = [
                        new RawWord(1), new RawWord(2)];
                    [RawWord; 2] copy = values;
                    values[0] = MakePadded();
                    unsafe { return copy[0].Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(copiedSnapshot.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                copiedSnapshot.Diagnostics));

        var nestedElement = Compilation.CreateLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 ReadNestedElementWide() {
                    [[RawWord; 2]; 2] values = [
                        [new RawWord(1), MakePadded()],
                        [new RawWord(2), new RawWord(3)]];
                    unsafe { return values[1][0].Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(nestedElement.HasErrors, Is.False,
            string.Join(Environment.NewLine, nestedElement.Diagnostics));

        var dynamicNestedElement = Compilation.CreateLibrary(
            SyntaxTree.Parse(prefix + """
                public uint64 RejectDynamicNestedWide(int32 index) {
                    [[RawWord; 2]; 2] values = [
                        [new RawWord(1), MakePadded()],
                        [new RawWord(2), new RawWord(3)]];
                    unsafe { return values[0][index].Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(dynamicNestedElement.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                dynamicNestedElement.Diagnostics));

        var dynamicOuterFull = Compilation.CreateLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 ReadDynamicOuterNestedWide(int32 row) {
                    [[RawWord; 2]; 2] values = [
                        [new RawWord(1), MakePadded()],
                        [new RawWord(2), new RawWord(3)]];
                    unsafe { return values[row][0].Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(dynamicOuterFull.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                dynamicOuterFull.Diagnostics));

        var dynamicOuterMixed = Compilation.CreateLibrary(
            SyntaxTree.Parse(prefix + """
                public uint64 RejectDynamicOuterNestedWide(int32 row) {
                    [[RawWord; 2]; 2] values = [
                        [new RawWord(1), MakePadded()],
                        [new RawWord(2), new RawWord(3)]];
                    unsafe { return values[row][1].Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(dynamicOuterMixed.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                dynamicOuterMixed.Diagnostics));

        var nestedPathWrite = Compilation.CreateLibrary(SyntaxTree.Parse(
            prefix + """
                public uint64 PreserveNestedSiblingAfterPathWrite() {
                    mut [[RawWord; 2]; 2] values = [
                        [new RawWord(1), new RawWord(2)],
                        [new RawWord(3), new RawWord(4)]];
                    values[0][1] = MakePadded();
                    unsafe { return values[0][0].Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(nestedPathWrite.HasErrors, Is.False,
            string.Join(Environment.NewLine, nestedPathWrite.Diagnostics));

        var dynamicOuterPathWrite = Compilation.CreateLibrary(
            SyntaxTree.Parse(prefix + """
                public uint64 RejectAfterDynamicOuterPathWrite(int32 row) {
                    mut [[RawWord; 2]; 2] values = [
                        [new RawWord(1), new RawWord(2)],
                        [new RawWord(3), new RawWord(4)]];
                    values[row][1] = MakePadded();
                    unsafe { return values[0][1].Wide; }
                }
                """), BoundRuntimePolicy.FailFast);
        Assert.That(dynamicOuterPathWrite.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                dynamicOuterPathWrite.Diagnostics));
    }

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void RequiredConstEvalCallsDetermineFixedArrayExtents(
        BoundRuntimePolicy policy)
    {
        const string source = """
            int32 Main() {
                mut [uint32; const (Count())] values = [10, 20, 30];
                return int32(values[2]);
            }
            [ConstEval] uint64 Count() { return Add(1, 2); }
            [ConstEval] uint64 Add(uint64 left, uint64 right) {
                return left + right;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), policy);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        Assert.That(program.NativeFixedArrayTypes.Single().ElementCount,
            Is.EqualTo(3));
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy),
            Is.EqualTo(30));
    }

    [Test]
    public void RequiredFixedArrayExtentRejectsRuntimeAndOverflowingCalls()
    {
        const string runtimeCall = """
            uint64 RuntimeCount() { return 2; }
            int32 Main() {
                mut [uint32; const (RuntimeCount())] values = [1, 2];
                return int32(values[0]);
            }
            """;
        var runtimeCompilation = Compilation.Create(
            SyntaxTree.Parse(runtimeCall));
        Assert.That(runtimeCompilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, runtimeCompilation.Diagnostics));

        const string overflowingCall = """
            [ConstEval] uint64 Overflow() {
                return uint64.MaxValue + 1;
            }
            int32 Main() {
                mut [uint32; const (Overflow())] values = [];
                return 0;
            }
            """;
        var overflowCompilation = Compilation.Create(
            SyntaxTree.Parse(overflowingCall));
        Assert.That(overflowCompilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, overflowCompilation.Diagnostics));

        const string signatureArray = """
            [ConstEval] uint64 Count() { return 1; }
            int32 Read([uint32; const (Count())] values) { return 0; }
            int32 Main() { return 0; }
            """;
        var signatureCompilation = Compilation.Create(
            SyntaxTree.Parse(signatureArray));
        Assert.That(signatureCompilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine,
                signatureCompilation.Diagnostics));

        const string fieldRuntimeCall = """
            [Blittable] [Layout(Sequential)]
            struct RuntimeTable {
                private [uint32; const (RuntimeCount())] Items;
            }
            uint64 RuntimeCount() { return 2; }
            public int32 Value() { return 42; }
            """;
        var fieldRuntimeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(fieldRuntimeCall));
        Assert.That(fieldRuntimeCompilation.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine,
                fieldRuntimeCompilation.Diagnostics));

        const string fieldOverflowCall = """
            [Blittable] [Layout(Sequential)]
            struct OverflowTable {
                private [uint32; const (Overflow())] Items;
            }
            [ConstEval] uint64 Overflow() {
                return uint64.MaxValue + 1;
            }
            public int32 Value() { return 42; }
            """;
        var fieldOverflowCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(fieldOverflowCall));
        Assert.That(fieldOverflowCompilation.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine,
                fieldOverflowCompilation.Diagnostics));
    }

    [Test]
    public void ConstEvalCallsCanDetermineArrayFieldExtents()
    {
        const string source = """
            [Blittable] [Layout(Sequential)]
            struct Table {
                private [uint32; const (Count())] Items;
            }
            [ConstEval] uint64 Count() { return Add(1, 2); }
            [ConstEval] uint64 Add(uint64 left, uint64 right) {
                return left + right;
            }
            public int32 Value() { return 42; }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var table = program.Structures.Single(static structure =>
            structure.Name == "Table");
        Assert.That(table.Fields, Has.Length.EqualTo(1));
        var arrayIndex = NativeFixedArrayTypes.Index(table.Fields[0].Type);
        Assert.That((uint)arrayIndex,
            Is.LessThan((uint)program.NativeFixedArrayTypes.Length));
        Assert.That(program.NativeFixedArrayTypes[arrayIndex].ElementType,
            Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(program.NativeFixedArrayTypes[arrayIndex].ElementCount,
            Is.EqualTo(3));
        Assert.That(NativeFixedArrayContract.IsValidTable(program.Structures,
            program.NativeFixedArrayTypes), Is.True);
    }

    [Test]
    public void ConstEvalArrayAliasesPreserveStructuralIdentityAndNamespaceContext()
    {
        var entry = SyntaxTree.Parse("""
            using Tables;
            int32 Main() {
                Row<uint32> first = [1, 2, 3];
                RowAlias second = [4, 5, 6];
                [uint32; 3] direct = [7, 8, 9];
                return int32(first[2] + second[2] + direct[2]);
            }
            """);
        var definitions = SyntaxTree.Parse("""
            namespace Tables {
                alias Row<T> = [T; const (Count())];
                alias Words = [uint32; const (Count())];
                alias RowAlias = Words;
                [Blittable] [Layout(Sequential)]
                struct Table {
                    private Row<uint32> Items;
                }
                [ConstEval] uint64 Count() { return Add(1, 2); }
                [ConstEval] uint64 Add(uint64 left, uint64 right) {
                    return left + right;
                }
            }
            """);
        var compilation = Compilation.Create([entry, definitions],
            BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var variables = program.Functions[program.EntryPoint].Variables;
        var firstType = variables.Single(static variable =>
            variable.Name == "first").Type;
        var aliasType = variables.Single(static variable =>
            variable.Name == "second").Type;
        var directType = variables.Single(static variable =>
            variable.Name == "direct").Type;
        var table = program.Structures.Single(static structure =>
            structure.Fields.Length == 1
            && NativeFixedArrayTypes.IsType(structure.Fields[0].Type));

        Assert.That(firstType, Is.EqualTo(aliasType));
        Assert.That(aliasType, Is.EqualTo(directType));
        Assert.That(table.Fields[0].Type, Is.EqualTo(firstType));
        Assert.That(program.NativeFixedArrayTypes, Has.Length.EqualTo(1));
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast),
            Is.EqualTo(18));

        const string runtimeCall = """
            alias Values = [uint32; const (RuntimeCount())];
            uint64 RuntimeCount() { return 3; }
            int32 Main() { return 0; }
            """;
        var runtimeCompilation = Compilation.Create(
            SyntaxTree.Parse(runtimeCall), BoundRuntimePolicy.FailFast);
        Assert.That(runtimeCompilation.Program, Is.Null);
        Assert.That(runtimeCompilation.HasErrors, Is.True);

        const string overflowCall = """
            alias Values = [uint32; const (Overflow())];
            [ConstEval] uint64 Overflow() {
                return uint64.MaxValue + 1;
            }
            int32 Main() { return 0; }
            """;
        var overflowCompilation = Compilation.Create(
            SyntaxTree.Parse(overflowCall), BoundRuntimePolicy.FailFast);
        Assert.That(overflowCompilation.Program, Is.Null);
        Assert.That(overflowCompilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, overflowCompilation.Diagnostics));
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

    [TestCase(1, 0, 98)]
    [TestCase(2, 1, 99)]
    [TestCase(2, 2, 3)]
    public void NativeParameterAddressEvaluatesIndexOnceAndChecksSuffix(
        int elementExtent, int index, int expectedResult)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var source = $$"""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "IsBadReadPtr",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, {{elementExtent}})]
            internal extern bool IsBadReadPtr(
                ptr<uint32> memory,
                uint64 byteCount);

            int32 Main() {
                [uint32; 3] values = [10, 20, 30];
                mut int32 selectedIndex = {{index}};
                uint64 byteCount = {{(ulong)elementExtent * sizeof(uint)}};
                unsafe {
                    try {
                        bool isBad = IsBadReadPtr(
                            &values[selectedIndex++], byteCount);
                        return selectedIndex + 97;
                    } catch {
                        return selectedIndex;
                    }
                }
                return 100;
            }
            """;
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.CheckedExceptions,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var compilation = Compilation.Create(SyntaxTree.Parse(source), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var compilerPath = typeof(NativeJit).Assembly.Location;
        var kernel32Path = Path.Combine(Environment.SystemDirectory,
            "kernel32.dll");
        TestContext.WriteLine(
            $"NativeParameter suffix JIT input: extent={elementExtent}; index-expression=selectedIndex++; starting-index={index}; byte-count={(ulong)elementExtent * sizeof(uint)}; source-sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))}; compiler={compilerPath}; compiler-sha256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(compilerPath)))}; import={kernel32Path}!IsBadReadPtr; import-sha256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(kernel32Path)))}.");
        var actualResult = NativeJit.Execute(program,
            NativeRuntimePolicy.CheckedExceptions);
        TestContext.WriteLine(
            $"NativeParameter suffix JIT result: policy=CheckedExceptions; expected={expectedResult}; actual={actualResult}.");
        Assert.That(actualResult, Is.EqualTo(expectedResult));
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
        var layout = GetFixedArrayLayout(program, "values");
        Assert.That(layout.ElementCount, Is.EqualTo(513));
        Assert.That(layout.ByteExtent, Is.EqualTo(4104));
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
        var layout = GetFixedArrayLayout(program, "values");
        Assert.That(layout.ElementCount, Is.Zero);
        Assert.That(layout.ByteExtent, Is.Zero);
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
    public void FixedArrayTypeIdentityIsCanonicalByElementAndCount()
    {
        const string source = """
            [Blittable] [Layout(Sequential)]
            struct FixedArrayFields {
                private [uint32; 2] Pair;
                private [uint16; 2] NarrowPair;
                private [uint32; 3] Triple;
            }

            int32 Main() {
                [uint32; 2] first = [1, 2];
                [uint32; 2] second = [3, 4];
                [uint16; 2] narrow = [5, 6];
                [uint32; 3] triple = [7, 8, 9];
                return int32(first[0] + second[0]
                    + uint32(narrow[0]) + triple[0]);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var variables = program.Functions[program.EntryPoint].Variables;
        var firstType = variables.Single(static variable =>
            variable.Name == "first").Type;
        var secondType = variables.Single(static variable =>
            variable.Name == "second").Type;
        var narrowType = variables.Single(static variable =>
            variable.Name == "narrow").Type;
        var tripleType = variables.Single(static variable =>
            variable.Name == "triple").Type;
        var fields = program.Structures.Single(static structure =>
            structure.Name == "FixedArrayFields").Fields;

        Assert.That(NativeFixedArrayTypes.IsType(firstType), Is.True);
        Assert.That(firstType, Is.Not.EqualTo(PrimitiveType.NativeFixedArray));
        Assert.That(secondType, Is.EqualTo(firstType));
        Assert.That(narrowType, Is.Not.EqualTo(firstType));
        Assert.That(tripleType, Is.Not.EqualTo(firstType));
        Assert.That(fields, Has.Length.EqualTo(3));
        Assert.That(fields[0].Type, Is.EqualTo(firstType));
        Assert.That(fields[1].Type, Is.EqualTo(narrowType));
        Assert.That(fields[2].Type, Is.EqualTo(tripleType));
        Assert.That(program.NativeFixedArrayTypes, Has.Length.EqualTo(3));
        Assert.That(NativeFixedArrayContract.MatchesProgram(program), Is.True);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast),
            Is.EqualTo(16));
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
        var layout = GetFixedArrayLayout(program, "values");
        Assert.That(layout.ElementStride, Is.Zero);
        Assert.That(layout.ByteExtent, Is.Zero);
        Assert.That(layout.Alignment, Is.EqualTo(1));
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
        var outer = GetFixedArrayLayout(program, "values");
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
