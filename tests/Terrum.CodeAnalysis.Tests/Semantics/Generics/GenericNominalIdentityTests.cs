// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics.Generics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class GenericNominalIdentityTests
{
    [Test]
    public void BindsExactSelectedCoreTaskIdentityAndItsDeclaredVoidDefault()
    {
        var reference = TrRmExecutableCompiler.CreateReference(
            CreateGenericTypeProvider("Terrum.Threading.Tasks", "Task", 3, 2, 0xF200_0000));
        var definition = reference.GenericNominalTypeDefinitions.Single(static item =>
            item.Name == "Task");
        var selected = reference.SelectCoreTaskType(definition.Identity);
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 0; }"), selected);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(definition.Formals.Single().Default!.DecodedType,
            Is.EqualTo(PrimitiveType.Void));
        Assert.That(definition.Formals.Single().Default!.Reference,
            Is.EqualTo(0xF200_0000));
        var program = compilation.Program!;
        var defaultType = program.Functions.Single(static function =>
            function.Name == "ReadDefault").ReturnType;
        Assert.That(BoundGenericNominalTypeTypes.IsType(defaultType), Is.True);
        Assert.That(program.CoreTaskType, Is.EqualTo(new CoreTaskTypeIdentity(definition.Identity)));

        var defaultInstance = program.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(defaultType)];
        Assert.That(defaultInstance.Definition, Is.EqualTo(definition.Identity));
        Assert.That(defaultInstance.Arguments.Single().Type, Is.EqualTo(PrimitiveType.Void));
        Assert.That(BoundGenericNominalTypeContract.IsCoreTaskType(defaultType, program), Is.True);
        var noInputCapture = program.Functions.Single(static function =>
            function.Name == "ReadDefault").DependencyContracts.Single();
        Assert.That(noInputCapture.Relation, Is.EqualTo(BoundDependencyRelation.Captures));
        Assert.That(noInputCapture.Destination, Is.EqualTo(BoundDependencyDestination.Operation));
        Assert.That(noInputCapture.Sources, Is.Empty);

        var integerType = program.Functions.Single(static function =>
            function.Name == "ReadInteger").ReturnType;
        var integerInstance = program.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(integerType)];
        Assert.That(integerInstance.Arguments.Single().Type, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(integerType, Is.Not.EqualTo(defaultType));
        Assert.That(BoundGenericNominalTypeContract.IsCoreTaskType(integerType, program), Is.True);
        var inputCapture = program.Functions.Single(static function =>
            function.Name == "ReadWithTaskInput").DependencyContracts.Single();
        Assert.That(inputCapture.Relation, Is.EqualTo(BoundDependencyRelation.Captures));
        Assert.That(inputCapture.Destination, Is.EqualTo(BoundDependencyDestination.Operation));
        Assert.That(inputCapture.Sources, Is.EqualTo([
            new BoundDependencySource(BoundDependencySourceKind.Formal, 0),
        ]));

        var writableType = program.Functions.Single(static function =>
            function.Name == "ReadWritable").ReturnType;
        var writableInstance = program.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(writableType)];
        var writableArgument = writableInstance.Arguments.Single();
        Assert.That(writableArgument.Type, Is.Not.EqualTo(PrimitiveType.NativeRawPointer));
        Assert.That(writableArgument.TypeSpecReference, Is.EqualTo(0xF301_8C_00));
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitWithImports(program, [
            new NativeImportBinding("ReadDefault", "Core.dll", "ReadDefault"),
            new NativeImportBinding("ReadInteger", "Core.dll", "ReadInteger"),
            new NativeImportBinding("ReadWritable", "Core.dll", "ReadWritable"),
        ], NativeRuntimePolicy.FailFast));

        var sourceUse = Compilation.Create(SyntaxTree.Parse("""
            using Terrum.Threading.Tasks;
            Task NoReturn() { while (true) { } }
            Task<write ptr<void>> NoReturnPointer() { while (true) { } }
            int32 Main() { return 0; }
            """), selected);
        Assert.That(sourceUse.Diagnostics.Any(static diagnostic =>
            diagnostic.Message.Contains("requires an explicitly selected exact Core Task",
                StringComparison.Ordinal)), Is.False);
        Assert.That(sourceUse.Diagnostics.Any(static diagnostic =>
            diagnostic.Message.Contains("requires its declared type argument count",
                StringComparison.Ordinal)), Is.False);
        Assert.That(sourceUse.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.False);
        Assert.That(sourceUse.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"), Is.True);
    }

    [Test]
    public void CoreTaskSelectionPreservesPrivateEnumOrigins()
    {
        var taskReference = TrRmExecutableCompiler.CreateReference(
            CreateGenericTypeProvider("Terrum.Threading.Tasks", "Task", 3,
                2, 0xF200_0000));
        var taskDefinition = taskReference.GenericNominalTypeDefinitions
            .Single(static definition => definition.Name == "Task");
        var enumType = BoundEnumTypes.Type(
            taskReference.EnumDefinitions.Length);
        var enumOrigin = new BoundPrivateNominalTypeOrigin(enumType,
            BoundPrivateNominalOriginKind.OwnedDefinition,
            IsExported: true, UsingModuleIdentity: "EnumProvider",
            LocalDeclarationKey: "types.color");
        var enumDefinition = BoundEnumDefinition.CreatePrivate(enumOrigin,
            "Color", PrimitiveType.Int32, [
                new BoundPrivateEnumMemberSnapshot("Red", 1, true,
                    "member.red"),
            ], isPublic: true, isPrimary: true, isFlags: false,
            blittableTargetRequirement: 0);
        var reference = new CompilationReference(taskReference.Structures,
            taskReference.Functions, taskReference.RuntimePolicy,
            taskReference.RequiresSourceGrant,
            taskReference.InvocationProfileId,
            taskReference.NativeFunctionPointerSignatures,
            taskReference.NativePointerTypes,
            taskReference.NativeCoreRegistration,
            enumDefinitions: [.. taskReference.EnumDefinitions,
                enumDefinition],
            checkedReferenceTypes: taskReference.CheckedReferenceTypes,
            interfaces: taskReference.Interfaces,
            genericNominalTypeDefinitions:
                taskReference.GenericNominalTypeDefinitions,
            genericNominalTypeInstances:
                taskReference.GenericNominalTypeInstances,
            coreTaskType: taskReference.CoreTaskType,
            nativeFixedArrayTypes: taskReference.NativeFixedArrayTypes,
            coreExceptionIdentitySelections:
                taskReference.CoreExceptionIdentitySelections,
            layoutCoreRegistration: taskReference.LayoutCoreRegistration,
            privateNominalTypeOrigins: [
                .. taskReference.PrivateNominalTypeOrigins, enumOrigin,
            ],
            classes: taskReference.Classes,
            privateClassReferences:
                taskReference.PrivateClassReferences);

        var selected = reference.SelectCoreTaskType(
            taskDefinition.Identity);
        Assert.That(selected.CoreTaskType,
            Is.EqualTo(new CoreTaskTypeIdentity(taskDefinition.Identity)));
        Assert.That(selected.EnumDefinitions.Single().Identity,
            Is.SameAs(enumDefinition.Identity));
        Assert.That(selected.PrivateNominalTypeOrigins,
            Is.EqualTo([enumOrigin]));

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public Color ReadColor() { return Color.Red; }"), selected);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void SameSpelledUnselectedTaskDefinitionHasNoCoreTaskAuthority()
    {
        var reference = TrRmExecutableCompiler.CreateReference(
            CreateGenericTypeProvider("Terrum.Threading.Tasks", "Task", 3, 2, 0xF200_0000));
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Terrum.Threading.Tasks;
            Task<int32> ReadTask() { while (true) { } }
            int32 Main() { return 0; }
            """), reference);

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
            && diagnostic.Message.Contains("exact Core Task definition",
                StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void CoreTaskSelectionRejectsProviderAndModuleIdentityMismatches()
    {
        var reference = TrRmExecutableCompiler.CreateReference(
            CreateGenericTypeProvider("Terrum.Threading.Tasks", "Task", 3, 2, 0xF200_0000));
        var identity = reference.GenericNominalTypeDefinitions.Single(static item =>
            item.Name == "Task").Identity;

        _ = Assert.Throws<ArgumentException>(() => reference.SelectCoreTaskType(
            identity with { ProviderIdentity = "Other.Provider" }));
        _ = Assert.Throws<ArgumentException>(() => reference.SelectCoreTaskType(
            identity with { ModuleIdentity = "Other.Module" }));
    }

    [Test]
    public void CoreTaskSelectionRejectsNonClassNominalDefinitions()
    {
        var reference = TrRmExecutableCompiler.CreateReference(
            CreateGenericTypeProvider("Terrum.Threading.Tasks", "Task", 3, 2,
                0xF200_0000, nominalKind: 1));
        var identity = reference.GenericNominalTypeDefinitions.Single(static item =>
            item.Name == "Task").Identity;

        _ = Assert.Throws<ArgumentException>(() => reference.SelectCoreTaskType(identity));
    }

    [Test]
    public void ExplicitEmptyOperationCaptureNarrowsTheTaskDefault()
    {
        var sourceReference = TrRmExecutableCompiler.CreateReference(
            CreateGenericTypeProvider("Terrum.Threading.Tasks", "Task", 3, 2,
                0xF200_0000));
        var taskDefinition = sourceReference.GenericNominalTypeDefinitions.Single(
            static item => item.Name == "Task");
        var selected = sourceReference.SelectCoreTaskType(taskDefinition.Identity);
        var function = selected.Functions.Single(static item =>
            item.Name == "ReadWithTaskInput");
        var functionIndex = selected.Functions.IndexOf(function);
        var narrowedFunction = function with
        {
            DependencyContracts = [
                new BoundDependencyContract(BoundDependencyRelation.Captures,
                    BoundDependencyDestination.Operation, null, []),
            ],
        };
        var narrowedFunctions = selected.Functions.SetItem(functionIndex,
            narrowedFunction);
        var narrowedReference = new CompilationReference(selected.Structures,
            narrowedFunctions, selected.RuntimePolicy,
            nativeFunctionPointerSignatures: selected.NativeFunctionPointerSignatures,
            nativePointerTypes: selected.NativePointerTypes,
            nativeCoreRegistration: selected.NativeCoreRegistration,
            enumDefinitions: selected.EnumDefinitions,
            checkedReferenceTypes: selected.CheckedReferenceTypes,
            interfaces: selected.Interfaces,
            genericNominalTypeDefinitions: selected.GenericNominalTypeDefinitions,
            genericNominalTypeInstances: selected.GenericNominalTypeInstances,
            coreTaskType: selected.CoreTaskType);
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 0; }"), narrowedReference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var capture = compilation.Program!.Functions.Single(static item =>
            item.Name == "ReadWithTaskInput").DependencyContracts.Single();
        Assert.That(capture.Destination,
            Is.EqualTo(BoundDependencyDestination.Operation));
        Assert.That(capture.Sources, Is.Empty);
    }

    [Test]
    public void RejectsUnsupportedCompleteSignatureTypeSpecsInsteadOfErasingThem()
    {
        var provider = CreateGenericTypeProvider("Terrum.Threading.Tasks",
            "Task", 3, 2, 0xF200_0000, taskInputTypeSpecRef: 0xF300_8B08);

        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.CreateGenericNominalTypeReference(provider));
    }

    [Test]
    public void BindsUnrelatedGenericNominalsThroughTheSameCarrier()
    {
        var reference = TrRmExecutableCompiler.CreateReference(
            CreateGenericTypeProvider("Example", "Box", 3, 2, 0));
        var definition = reference.GenericNominalTypeDefinitions.Single(static item =>
            item.Name == "Box");
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 0; }"), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var returnType = compilation.Program!.Functions.Single(static function =>
            function.Name == "ReadInteger").ReturnType;
        Assert.That(BoundGenericNominalTypeTypes.IsType(returnType), Is.True);
        var instance = compilation.Program.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(returnType)];
        Assert.That(instance.Definition, Is.EqualTo(definition.Identity));
        Assert.That(instance.Arguments.Single().Type, Is.EqualTo(PrimitiveType.Int32));
    }

    [Test]
    public void PreservesOrderedTypeAndConstantSubstitutions()
    {
        var reference = TrRmExecutableCompiler.CreateGenericNominalTypeReference(
            CreateGenericTypeProvider("Example", "Pair", 3, 2, 0,
                includeConstantFormal: true));
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 0; }"), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var returnType = compilation.Program!.Functions.Single(static function =>
            function.Name == "ReadConstants").ReturnType;
        var instance = compilation.Program.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(returnType)];
        Assert.That(instance.Arguments.Length, Is.EqualTo(2));
        Assert.That(instance.Arguments[0].Kind, Is.EqualTo(BoundGenericArgumentKind.Type));
        Assert.That(instance.Arguments[0].Type, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(instance.Arguments[1].Kind, Is.EqualTo(BoundGenericArgumentKind.Constant));
        Assert.That(instance.Arguments[1].ConstantProviderIdentity, Is.EqualTo("Provider"));
        Assert.That(instance.Arguments[1].ConstantSourceRow, Is.EqualTo(1));
    }

    [Test]
    public void RejectsMalformedGenericNominalDefaultsBeforeReferenceBinding()
    {
        var malformed = CreateGenericTypeProvider(
            "Terrum.Threading.Tasks", "Task", 3, 2, 0xF200_0000);
        var genericParamOffset = GetSectionOffset(malformed, 0x0019);
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformed.AsSpan(genericParamOffset + 28), 6);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateGenericNominalTypeReference(malformed));
    }

    private static byte[] CreateGenericTypeProvider(string @namespace, string name,
        byte ownershipDomain, byte nullabilityDomain, uint defaultArgument,
        bool includeConstantFormal = false, byte nominalKind = 2,
        uint? taskInputTypeSpecRef = null)
    {
        var stringsBytes = TrRmStringTableWriter.EncodeNames([
            ("Provider", 0), (@namespace, 0), (name, 0), ("T", 0),
            ("ReadDefault", 0), ("ReadInteger", 0), ("ReadWritable", 0),
            ("NominalType", 0), ("ReadConstants", 0), ("N", 0),
            ("ReadWithTaskInput", 0), ("BoxType", 0),
        ]);
        var strings = new TrRmStringTableReader(stringsBytes, 12);
        var methodCount = includeConstantFormal ? 5 : 4;
        var formalCount = includeConstantFormal ? 2 : 1;
        var featureFirst = includeConstantFormal ? 1 : 0;
        var featureCount = includeConstantFormal ? 1 : 0;
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 0, 0, 0, 1, 1, 1, 1,
                featureFirst, featureCount),
        ], strings, moduleRefCount: 0, typeDefCount: 2,
            methodDefCount: methodCount, sourceFileCount: 0,
            featureCount: featureCount);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            moduleRefCount: 0, typeDefCount: 2, methodDefCount: methodCount,
            sourceFileCount: 0, featureCount: featureCount);
        var typeDefBytes = TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(2, 3, (uint)(2 | (nominalKind << 2)),
                0, 0, 1, formalCount, 1, 1, 0, 0),
            new TrRmTypeDefRow(2, 8, 10, 0, 0, 0, 0, 2, 1, 0, 0),
        ], strings, modules, fieldCount: 0, genericParamCount: formalCount,
            layoutCount: 2, contractCount: 0);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, 2,
            strings, modules, fieldCount: 0, genericParamCount: formalCount,
            layoutCount: 2, contractCount: 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 0, 0),
        ], typeDefs, targetProfiles, layoutFieldCount: 0);
        var typeSpecRows = new List<ReadOnlyMemory<byte>>
        {
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            ConstructedType(0xF200_0000),
            ConstructedType(0xF200_0008),
            ConstructedType(0xF301_8C_00),
        };
        if (includeConstantFormal)
        {
            typeSpecRows.Add(ConstructedType(0xF200_0008));
        }
        var boxTypeSpecRef = (uint)(typeSpecRows.Count + 1);
        typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x02, 2));
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            typeDefCount: 2, importCount: 0, genericParamCount: formalCount);
        var typeSpecTable = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, typeDefCount: 2, importCount: 0,
            genericParamCount: formalCount);
        var signatureRows = new List<TrRmMethodSigRow>
        {
            new(2, 0, 0, 0, 0, 0),
            new(3, 0, 0, 0, 0, 0),
            new(4, 0, 0, 0, 0, 0),
        };
        if (includeConstantFormal)
        {
            signatureRows.Add(new TrRmMethodSigRow(5, 0, 0, 0, 0, 0));
        }
        var taskInputSignatureRow = signatureRows.Count + 1;
        signatureRows.Add(new TrRmMethodSigRow(2, 0, 1, 1, 0, 0));
        var signatureBytes = TrRmMethodSigTableWriter.Encode(signatureRows,
            typeSpecTable, paramCount: 1, genericParamCount: formalCount);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            signatureRows.Count, typeSpecTable, paramCount: 1,
            genericParamCount: formalCount);
        var parameterBytes = TrRmParamTableWriter.Encode([
            new TrRmParamRow(taskInputSignatureRow, 0,
                taskInputTypeSpecRef ?? boxTypeSpecRef, 0),
        ], signatures, typeSpecTable);
        var methodRows = new List<TrRmMethodDefRow>
        {
            new(0, 5, 1, 2, 0, 0, 0),
            new(0, 6, 2, 2, 0, 0, 0),
            new(0, 7, 3, 2, 0, 0, 0),
        };
        if (includeConstantFormal)
        {
            methodRows.Add(new TrRmMethodDefRow(0, 9, 4, 2, 0, 0, 0));
        }
        methodRows.Add(new TrRmMethodDefRow(0, 11, taskInputSignatureRow, 2, 0, 0, 0));
        var methodsBytes = TrRmMethodDefTableWriter.Encode(methodRows,
            strings, modules, typeDefs, signatures,
            contractCount: 0, bodyCount: 0);
        var methods = new TrRmMethodDefTableReader(methodsBytes, methodCount,
            strings, modules, typeDefs, signatures, contractCount: 0, bodyCount: 0);
        var exportRows = new List<TrRmExportRow>
        {
            new(0x1300_0001, 8),
            new(0x1300_0002, 12),
            new(0x1600_0001, 5),
            new(0x1600_0002, 6),
            new(0x1600_0003, 7),
        };
        if (includeConstantFormal)
        {
            exportRows.Add(new TrRmExportRow(0x1600_0004, 9));
        }
        exportRows.Add(new TrRmExportRow(0x1600_0000u | (uint)methodCount, 11));
        var exportsBytes = TrRmExportTableWriter.Encode(exportRows,
            strings, typeDefs, methods, default, default);
        var genericParamRows = new List<TrRmGenericParamRow>
        {
            new(0x1300_0001, 0, 4, 0, 0,
                1, ownershipDomain, nullabilityDomain, 0, defaultArgument),
        };
        if (includeConstantFormal)
        {
            genericParamRows.Add(new TrRmGenericParamRow(0x1300_0001, 1, 10,
                0, 0, 2, 0, 0, 0xF200_0008, 0));
        }
        var constantSourceBytes = includeConstantFormal
            ? TrRmConstantSourceTableWriter.Encode([
                new TrRmConstantSourceRow(1, 1),
            ], frozenValueNodeCount: 1, constantExecutionCount: 0,
                targetValueBindingCount: 0)
            : [];
        var blobBytes = includeConstantFormal
            ? TrRmVariableTableWriter.EncodeBlobs([
                new ReadOnlyMemory<byte>([42, 0, 0, 0]),
            ])
            : [];
        var blobs = includeConstantFormal
            ? new TrRmBlobTableReader(blobBytes, 1)
            : default;
        var frozenValueNodeBytes = includeConstantFormal
            ? TrRmFrozenValueNodeTableWriter.Encode([
                new TrRmFrozenValueNodeRow(0xF200_0008, 1, 1, 0, 0, 0),
            ], typeSpecTable, blobs, childCount: 0, unionCaseDefCount: 0)
            : [];
        var genericParamsBytes = TrRmGenericParamTableWriter.Encode(genericParamRows,
            strings, typeDefs, methods, signatures, typeSpecTable,
            constraintCount: 0, constantSourceCount: includeConstantFormal ? 1 : 0);
        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, 12, stringsBytes),
            new(0x0011, 0, (uint)typeSpecRows.Count, typeSpecBytes),
            new(0x0013, 0, 2, typeDefBytes),
            new(0x0015, 0, 2, layoutBytes),
            new(0x0016, 0, (uint)methodCount, methodsBytes),
            new(0x0017, 0, (uint)signatureRows.Count, signatureBytes),
            new(0x0018, 0, 1, parameterBytes),
            new(0x0019, 0, (uint)formalCount, genericParamsBytes),
            new(0x001D, 0, (uint)exportRows.Count, exportsBytes),
        };
        if (includeConstantFormal)
        {
            sections.Add(new TrRmSection(0x0003, 0, 1, blobBytes));
            sections.Add(new TrRmSection(0x0004, 0, 1,
                TrRmFeatureTableWriter.Encode([TrRmFeatureRow.TypedFrozenValues])));
            sections.Add(new TrRmSection(0x0030, 0, 1, constantSourceBytes));
            sections.Add(new TrRmSection(0x003F, 0, 1, frozenValueNodeBytes));
        }
        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections);

        ReadOnlyMemory<byte> ConstructedType(uint typeArgument)
        {
            return includeConstantFormal
                ? TrRmTypeSpecTableWriter.Constructed(1, [typeArgument, 1])
                : TrRmTypeSpecTableWriter.Constructed(1, [typeArgument]);
        }
    }

    private static int GetSectionOffset(byte[] artifact, ushort sectionKind)
    {
        var entryCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            artifact.AsSpan(12)));
        for (var entry = 0; entry < entryCount; entry++)
        {
            var entryOffset = 32 + (entry * 16);
            if (BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(entryOffset))
                == sectionKind)
            {
                return checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    artifact.AsSpan(entryOffset + 8)));
            }
        }

        throw new InvalidDataException($"Missing TRRM section 0x{sectionKind:X4}.");
    }
}
