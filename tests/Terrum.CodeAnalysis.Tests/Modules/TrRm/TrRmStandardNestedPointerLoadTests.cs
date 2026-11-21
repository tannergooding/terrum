// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmStandardNestedPointerLoadTests
{
    private const string PointerLoadsSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-pointer-loads-1";
    private const string GeneralPredicateGraphsPointerLoadsSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-pointer-loads-1";
    private const string InitializationReferenceSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-1";

    public static IEnumerable<TestCaseData> PointerLoadScalarTypeCases()
    {
        foreach (var schemaIdentity in new[] {
            PointerLoadsSchemaIdentity,
            GeneralPredicateGraphsPointerLoadsSchemaIdentity,
        })
        {
            foreach (var typeName in new[] {
                "bool", "char8", "char16", "char32", "rune",
                "int8", "uint8", "int16", "uint16", "int32", "uint32",
                "int64", "uint64", "nint", "nuint",
                "float32", "float64",
            })
            {
                yield return new TestCaseData(schemaIdentity, typeName);
            }
        }
    }

    public static IEnumerable<TestCaseData> FloatPointerLoadBitCases()
    {
        foreach (var schemaIdentity in new[] {
            PointerLoadsSchemaIdentity,
            GeneralPredicateGraphsPointerLoadsSchemaIdentity,
        })
        {
            yield return new TestCaseData(schemaIdentity, "float32",
                0x80000000UL);
            yield return new TestCaseData(schemaIdentity, "float32",
                0x7FC00042UL);
            yield return new TestCaseData(schemaIdentity, "float64",
                0x8000000000000000UL);
            yield return new TestCaseData(schemaIdentity, "float64",
                0x7FF8000000000042UL);
        }
    }

    public static IEnumerable<TestCaseData> InvalidScalarPointerLoadCases()
    {
        foreach (var schemaIdentity in new[] {
            PointerLoadsSchemaIdentity,
            GeneralPredicateGraphsPointerLoadsSchemaIdentity,
        })
        {
            yield return new TestCaseData(schemaIdentity, "bool", 0x02UL);
            yield return new TestCaseData(schemaIdentity, "rune", 0xD800UL);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadNestedPointerFunction(nint pointer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float ReadFloat32PointerFunction(nint pointer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double ReadFloat64PointerFunction(nint pointer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte ReadBooleanPointerFunction(nint pointer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadRunePointerFunction(nint pointer);

    [TestCase(PointerLoadsSchemaIdentity,
        InitializationReferenceSchemaIdentity)]
    [TestCase(GeneralPredicateGraphsPointerLoadsSchemaIdentity,
        InitializationReferenceSchemaIdentity)]
    [TestCase(PointerLoadsSchemaIdentity,
        GeneralPredicateGraphsPointerLoadsSchemaIdentity)]
    [TestCase(GeneralPredicateGraphsPointerLoadsSchemaIdentity,
        PointerLoadsSchemaIdentity)]
    public void PointerLoadSuccessorRejectsUnusedProviderWithDifferentSchema(
        string schemaIdentity, string providerSchemaIdentity)
    {
        const string rootModuleIdentity = "pointer-load-root";
        const string providerModuleIdentity = "pointer-load-unused-provider";
        var rootCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public unsafe int32 Read(ptr<int32> value) {
                unsafe { return *value; }
            }
            """));
        Assert.That(rootCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var rootArtifact = TrRmExecutableCompiler.EmitStandard(
            rootCompilation.Program!, rootModuleIdentity, schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);

        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Unused() {}"));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerModuleIdentity,
            providerSchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity, rootArtifact),
                rootModuleIdentity, [
                    new TrRmStandardArtifactInput(
                        providerSchemaIdentity,
                        providerArtifact),
                ]));
    }

    [TestCase(PointerLoadsSchemaIdentity)]
    [TestCase(GeneralPredicateGraphsPointerLoadsSchemaIdentity)]
    public async Task NestedPointerCellsPreserveTypedLoadOrderAsync(
        string schemaIdentity)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string moduleIdentity = "standard-nested-pointer-load";
        const string source = """
            public unsafe int32 ReadNested(
                ptr<ptr<ptr<int32>>> outerCell)
            {
                unsafe { return *(*(*outerCell)); }
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        AssertNestedPointerRead(loaded);
        var reemittedModuleIdentity = moduleIdentity + "-reemitted";
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            reemittedModuleIdentity, schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            reemittedModuleIdentity, []);
        AssertNestedPointerRead(loaded);
        var image = NativeEmitter.EmitLibrary(loaded, ["ReadNested"],
            NativeRuntimePolicy.FailFast);
        var libraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-standard-nested-pointer-load-{Guid.NewGuid():N}.dll");
        nint library = 0;
        nint firstBacking = 0;
        nint firstPointerCell = 0;
        nint secondBacking = 0;
        nint secondPointerCell = 0;
        nint outerPointerCell = 0;
        try
        {
            firstBacking = Marshal.AllocHGlobal(sizeof(int));
            firstPointerCell = Marshal.AllocHGlobal(IntPtr.Size);
            secondBacking = Marshal.AllocHGlobal(sizeof(int));
            secondPointerCell = Marshal.AllocHGlobal(IntPtr.Size);
            outerPointerCell = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteInt32(firstBacking, 42);
            Marshal.WriteIntPtr(firstPointerCell, firstBacking);
            Marshal.WriteInt32(secondBacking, 99);
            Marshal.WriteIntPtr(secondPointerCell, secondBacking);
            Marshal.WriteIntPtr(outerPointerCell, firstPointerCell);

            await File.WriteAllBytesAsync(libraryPath, image)
                .ConfigureAwait(false);
            library = NativeLibrary.Load(libraryPath);
            var readNested = Marshal.GetDelegateForFunctionPointer<
                ReadNestedPointerFunction>(NativeLibrary.GetExport(library,
                "ReadNested"));
            Assert.That(readNested(outerPointerCell), Is.EqualTo(42));

            Marshal.WriteIntPtr(outerPointerCell, secondPointerCell);
            Assert.That(readNested(outerPointerCell), Is.EqualTo(99));
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }
            if (outerPointerCell != 0)
            {
                Marshal.FreeHGlobal(outerPointerCell);
            }
            if (secondPointerCell != 0)
            {
                Marshal.FreeHGlobal(secondPointerCell);
            }
            if (secondBacking != 0)
            {
                Marshal.FreeHGlobal(secondBacking);
            }
            if (firstPointerCell != 0)
            {
                Marshal.FreeHGlobal(firstPointerCell);
            }
            if (firstBacking != 0)
            {
                Marshal.FreeHGlobal(firstBacking);
            }
            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(false);
        }
    }

    [TestCaseSource(nameof(PointerLoadScalarTypeCases))]
    public void ScalarPointerLoadTypesRoundTrip(
        string schemaIdentity, string typeName)
    {
        const string moduleIdentity = "standard-scalar-pointer-load";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($"""
            public unsafe {typeName} Read(ptr<{typeName}> value)
            {
                unsafe { return *value; }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        AssertDirectPointerLoad(loaded);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity + "-reemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            moduleIdentity + "-reemitted", []);
        AssertDirectPointerLoad(loaded);
    }

    [TestCaseSource(nameof(FloatPointerLoadBitCases))]
    public async Task FloatPointerLoadsPreserveExactBitsAsync(
        string schemaIdentity, string typeName, ulong bits)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string moduleIdentity = "standard-float-pointer-load";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($"""
            public unsafe {typeName} Read(ptr<{typeName}> value)
            {
                unsafe { return *value; }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity + "-reemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            moduleIdentity + "-reemitted", []);
        var image = NativeEmitter.EmitLibrary(loaded, ["Read"],
            NativeRuntimePolicy.FailFast);
        var libraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-standard-float-pointer-load-{Guid.NewGuid():N}.dll");
        nint library = 0;
        nint backing = 0;
        try
        {
            backing = Marshal.AllocHGlobal(sizeof(long));
            if (typeName == "float32")
            {
                Marshal.WriteInt32(backing, unchecked((int)bits));
            }
            else
            {
                Marshal.WriteInt64(backing, unchecked((long)bits));
            }

            await File.WriteAllBytesAsync(libraryPath, image)
                .ConfigureAwait(false);
            library = NativeLibrary.Load(libraryPath);
            var readAddress = NativeLibrary.GetExport(library, "Read");
            var actualBits = typeName == "float32"
                ? unchecked((uint)BitConverter.SingleToInt32Bits(
                    Marshal.GetDelegateForFunctionPointer<
                        ReadFloat32PointerFunction>(readAddress)(backing)))
                : unchecked((ulong)BitConverter.DoubleToInt64Bits(
                    Marshal.GetDelegateForFunctionPointer<
                        ReadFloat64PointerFunction>(readAddress)(backing)));
            Assert.That(actualBits, Is.EqualTo(bits));
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }
            if (backing != 0)
            {
                Marshal.FreeHGlobal(backing);
            }
            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(false);
        }
    }

    [TestCaseSource(nameof(InvalidScalarPointerLoadCases))]
    public async Task InvalidBooleanAndRunePointerLoadsPreserveBitsAsync(
        string schemaIdentity, string typeName, ulong bits)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string moduleIdentity = "standard-invalid-scalar-pointer-load";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($"""
            public unsafe {typeName} Read(ptr<{typeName}> value)
            {
                unsafe { return *value; }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity + "-reemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            moduleIdentity + "-reemitted", []);
        var image = NativeEmitter.EmitLibrary(loaded, ["Read"],
            NativeRuntimePolicy.FailFast);
        var libraryPath = Path.Combine(Path.GetTempPath(),
            $"terrum-standard-invalid-scalar-pointer-load-{Guid.NewGuid():N}.dll");
        nint library = 0;
        nint backing = 0;
        try
        {
            backing = Marshal.AllocHGlobal(sizeof(long));
            if (typeName == "bool")
            {
                Marshal.WriteByte(backing, unchecked((byte)bits));
            }
            else
            {
                Marshal.WriteInt32(backing, unchecked((int)bits));
            }

            await File.WriteAllBytesAsync(libraryPath, image)
                .ConfigureAwait(false);
            library = NativeLibrary.Load(libraryPath);
            var readAddress = NativeLibrary.GetExport(library, "Read");
            var actualBits = typeName == "bool"
                ? Marshal.GetDelegateForFunctionPointer<
                    ReadBooleanPointerFunction>(readAddress)(backing)
                : unchecked((uint)Marshal.GetDelegateForFunctionPointer<
                    ReadRunePointerFunction>(readAddress)(backing));
            Assert.That((ulong)actualBits, Is.EqualTo(bits));
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }
            if (backing != 0)
            {
                Marshal.FreeHGlobal(backing);
            }
            await TestFileCleanup.DeleteWithRetryAsync(libraryPath)
                .ConfigureAwait(false);
        }
    }

    [TestCase(PointerLoadsSchemaIdentity)]
    [TestCase(GeneralPredicateGraphsPointerLoadsSchemaIdentity)]
    public void NestedPointerLoadProviderSurvivesReemission(
        string schemaIdentity)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string providerIdentity = "nested-pointer-load-provider";
        const string consumerIdentity = "nested-pointer-load-consumer";
        const string reemittedConsumerIdentity
            = "nested-pointer-load-consumer-reemitted";
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public unsafe ptr<ptr<int32>> ReadOuter(
                    ptr<ptr<ptr<int32>>> outerCell)
                {
                    unsafe { return *outerCell; }
                }
                """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerReference = TrRmExecutableCompiler
            .CreateStandardReference(providerInput, providerIdentity, []);

        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public unsafe int32 ReadNested(
                    ptr<ptr<ptr<int32>>> outerCell)
                {
                    unsafe { return *(*ReadOuter(outerCell)); }
                }
                """), providerReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerCompilation.Program!, consumerIdentity, schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var consumerInput = new TrRmStandardArtifactInput(schemaIdentity,
            consumerArtifact);
        var loadedConsumer = TrRmExecutableCompiler.LoadStandard(
            consumerInput, consumerIdentity, [providerInput]);
        var reemittedConsumer = TrRmExecutableCompiler.EmitStandard(
            loadedConsumer, reemittedConsumerIdentity, schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var loadedProvider = TrRmExecutableCompiler.LoadStandard(
            providerInput, providerIdentity, []);
        var providerFunction = loadedProvider.Functions.Single(
            static candidate => candidate.Name == "ReadOuter");
        var providerValue = GetSingleReturnValue(providerFunction);
        if (providerValue is BoundUnsafeExpression unsafeProviderValue)
        {
            providerValue = unsafeProviderValue.Expression;
        }
        Assert.That(providerValue, Is.TypeOf<BoundNativePointerRead>());
        Assert.That(((BoundNativePointerRead)providerValue).Type,
            Is.EqualTo(providerFunction.ReturnType));
        var reemittedProvider = TrRmExecutableCompiler.EmitStandard(
            loadedProvider, providerIdentity, schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reloadedConsumer = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedConsumer),
            reemittedConsumerIdentity, [
                new TrRmStandardArtifactInput(schemaIdentity,
                    reemittedProvider),
            ]);
        var call = AssertNestedPointerCall(reloadedConsumer);
        Assert.That(reloadedConsumer.Functions[call.Function].Name,
            Is.EqualTo("ReadOuter"));

        var incompatibleProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public unsafe ptr<int32> ReadOuter(
                    ptr<ptr<int32>> outerCell)
                {
                    unsafe { return *outerCell; }
                }
                """));
        Assert.That(incompatibleProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleProviderCompilation.Diagnostics));
        var incompatibleProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            incompatibleProviderCompilation.Program!, providerIdentity,
            schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    reemittedConsumer),
                reemittedConsumerIdentity, [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleProviderArtifact),
                ]));
    }

    private static BoundCall AssertNestedPointerCall(BoundProgram program)
    {
        var function = program.Functions.Single(static candidate =>
            candidate.Name == "ReadNested");
        var value = GetSingleReturnValue(function);
        for (var depth = 0; depth < 2; depth++)
        {
            if (value is BoundUnsafeExpression unsafeExpression)
            {
                value = unsafeExpression.Expression;
            }
            Assert.That(value, Is.TypeOf<BoundNativePointerRead>());
            value = ((BoundNativePointerRead)value).Address;
        }
        if (value is BoundUnsafeExpression finalUnsafeExpression)
        {
            value = finalUnsafeExpression.Expression;
        }
        Assert.That(value, Is.TypeOf<BoundCall>());
        return (BoundCall)value;
    }

    private static void AssertNestedPointerRead(BoundProgram program)
    {
        var function = program.Functions.Single(static candidate =>
            candidate.Name == "ReadNested");
        var value = GetSingleReturnValue(function);
        for (var depth = 0; depth < 3; depth++)
        {
            if (value is BoundUnsafeExpression unsafeExpression)
            {
                value = unsafeExpression.Expression;
            }
            Assert.That(value, Is.TypeOf<BoundNativePointerRead>());
            value = ((BoundNativePointerRead)value).Address;
        }
        Assert.That(value, Is.TypeOf<BoundRead>());
    }

    private static void AssertDirectPointerLoad(BoundProgram program)
    {
        var function = program.Functions.Single(static candidate =>
            candidate.Name == "Read");
        var value = GetSingleReturnValue(function);
        if (value is BoundUnsafeExpression unsafeExpression)
        {
            value = unsafeExpression.Expression;
        }
        Assert.That(value, Is.TypeOf<BoundNativePointerRead>());
        var pointerRead = (BoundNativePointerRead)value;
        Assert.That(pointerRead.Type, Is.EqualTo(function.ReturnType));
        var parameterType = function.Variables[0].Type;
        Assert.That(NativePointerTypes.IsType(parameterType), Is.True);
        var pointerType = program.NativePointerTypes[
            NativePointerTypes.Index(parameterType)];
        Assert.That(pointerType.PointeeType, Is.EqualTo(pointerRead.Type));
    }

    private static BoundExpression GetSingleReturnValue(
        BoundFunction function)
    {
        var pendingStatements = new Stack<BoundStatement>();
        pendingStatements.Push(function.Body);
        BoundReturn? returnStatement = null;
        while (pendingStatements.TryPop(out var statement))
        {
            switch (statement)
            {
                case BoundBlock block:
                    foreach (var nested in block.Statements)
                    {
                        pendingStatements.Push(nested);
                    }
                    break;
                case BoundUnsafe unsafeBlock:
                    pendingStatements.Push(unsafeBlock.Body);
                    break;
                case BoundReturn result:
                    Assert.That(returnStatement, Is.Null);
                    returnStatement = result;
                    break;
            }
        }

        Assert.That(returnStatement?.Value, Is.Not.Null);
        return returnStatement!.Value!;
    }
}
