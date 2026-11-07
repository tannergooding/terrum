// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class ModuleCompiler
{
    private readonly record struct PrivateCheckedReferenceTypeRow(
        byte ConstructorKind, byte Qualifiers, PrimitiveType InnerType);

    internal sealed record Private105CopyOriginSummaryData(
        byte Kind, ImmutableArray<Private105CopyOriginFactData> Facts);

    internal sealed record Private105CopyOriginFactData(
        ImmutableArray<int> DestinationPath,
        ImmutableArray<Private105CopyOriginData> Origins);

    internal readonly record struct Private105CopyOriginData(
        byte SourceKind, byte SourceView, int SourceOrdinal,
        ImmutableArray<int> SourcePath);

    internal readonly record struct Private106FunctionMetadata(
        ImmutableArray<byte> RawUnionReturnCoverage,
        DirectNativeImport? ExpectedDirectNativeImport,
        Private105CopyOriginSummaryData? CopyOriginSummary);

    private static void WritePrivateCheckedReferenceTypes(
        BinaryWriter writer,
        ImmutableArray<CheckedReferenceTypeDefinition> referenceTypes,
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<BoundClass> classes,
        ImmutableArray<NativeFunctionPointerSignatureDefinition>
            functionPointerSignatures,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrayTypes,
        ImmutableArray<BoundEnumDefinition> enumDefinitions = default,
        bool allowUnresolvedEnums = false)
    {
        if (writer is not ModuleWriter {
                FormatVersion: CheckedReferenceVersion,
            } moduleWriter
            || referenceTypes.IsDefault
            || !referenceTypes.SequenceEqual(
                moduleWriter.CheckedReferenceTypes)
            || referenceTypes.Length > MaximumNodes)
        {
            throw new ModuleContractException(
                "The selected private checked-reference table is invalid.");
        }
        if (!CheckedReferenceTypeContract.IsValidTable(referenceTypes))
        {
            throw new ModuleContractException(
                "The selected private checked-reference table is invalid.");
        }
        var seenTypes = new HashSet<CheckedReferenceTypeDefinition>();
        for (var index = 0; index < referenceTypes.Length; index++)
        {
            var referenceType = referenceTypes[index];
            if (!IsValidPrivateReferenceValueType(
                    referenceType.ReferentType, referenceTypes, structures,
                    classes, functionPointerSignatures, pointerTypes,
                    fixedArrayTypes, moduleWriter.EnumDefinitions)
                || !seenTypes.Add(referenceType)
                || (CheckedReferenceTypes.IsType(
                        referenceType.ReferentType)
                    && CheckedReferenceTypes.Index(
                        referenceType.ReferentType) >= index))
            {
                throw new ModuleContractException(
                    "Private checked-reference type table is not canonical or names an absent or unsupported complete type.");
            }
        }
        var byteCount = checked(sizeof(int)
            + ((long)referenceTypes.Length * 8));
        if (byteCount > MaximumBytes - writer.BaseStream.Length)
        {
            throw new ModuleFormatException(
                "Private checked-reference type table exceeds the artifact limit.");
        }

        writer.Write(referenceTypes.Length);
        for (var index = 0; index < referenceTypes.Length; index++)
        {
            var referenceType = referenceTypes[index];
            byte constructorKind;
            byte qualifiers;
            PrimitiveType innerType;
            if (referenceType.IsOptional)
            {
                var requiredDefinition = referenceType with {
                    IsOptional = false,
                };
                var requiredIndex = referenceTypes.IndexOf(
                    requiredDefinition);
                if (requiredIndex < 0 || requiredIndex >= index)
                {
                    throw new ModuleContractException(
                        "Optional checked-reference types require a prior retained complete inner reference row.");
                }

                constructorKind = 3;
                qualifiers = 0;
                innerType = CheckedReferenceTypes.Type(requiredIndex);
            }
            else if (referenceType.IsInitialization)
            {
                if (referenceType.IsGarbageCollected)
                {
                    throw new ModuleContractException(
                        "A GC-backed initialization reference has no private type identity.");
                }

                constructorKind = 2;
                qualifiers = referenceType.IsWritable ? (byte)1 : (byte)0;
                innerType = referenceType.ReferentType;
            }
            else
            {
                constructorKind = 1;
                qualifiers = (byte)((referenceType.IsWritable ? 1 : 0)
                    | (referenceType.IsGarbageCollected ? 2 : 0));
                innerType = referenceType.ReferentType;
            }

            writer.Write(constructorKind);
            writer.Write(qualifiers);
            writer.Write((ushort)0);
            WritePrivateType(writer, innerType);
        }
    }

    private static ImmutableArray<CheckedReferenceTypeDefinition>
        ReadPrivateCheckedReferenceTypes(BinaryReader reader,
            ImmutableArray<BoundStruct> structures,
            ImmutableArray<BoundClass> classes,
            ImmutableArray<NativeFunctionPointerSignatureDefinition>
                functionPointerSignatures,
            ImmutableArray<NativePointerTypeDefinition> pointerTypes,
            ImmutableArray<BoundNativeFixedArrayType> fixedArrayTypes)
    {
        if (reader is not ModuleReader {
                FormatVersion: CheckedReferenceVersion,
            } moduleReader)
        {
            return [];
        }

        var count = Count(reader, MaximumNodes);
        var byteCount = checked((long)count * 8);
        if (reader.BaseStream.CanSeek
            && byteCount > reader.BaseStream.Length
                - reader.BaseStream.Position)
        {
            throw new ModuleFormatException(
                "Private checked-reference type table exceeds the remaining module extent.");
        }

        var rows = new PrivateCheckedReferenceTypeRow[count];
        for (var index = 0; index < count; index++)
        {
            var constructorKind = reader.ReadByte();
            var qualifiers = reader.ReadByte();
            var reserved = reader.ReadUInt16();
            var referentType = (PrimitiveType)reader.ReadInt32();
            if (reserved != 0 || referentType == PrimitiveType.Void)
            {
                throw new ModuleFormatException(
                    "Private checked-reference type row has invalid reserved or referent data.");
            }

            var qualifiersValid = constructorKind switch {
                1 => (qualifiers & ~3) == 0,
                2 => (qualifiers & ~1) == 0,
                3 => qualifiers == 0,
                _ => false,
            };
            if (!qualifiersValid)
            {
                throw new ModuleFormatException(
                    "Private checked-reference type row has an unknown constructor or qualifier.");
            }

            rows[index] = new PrivateCheckedReferenceTypeRow(
                constructorKind, qualifiers, referentType);
        }

        var referenceTypes
            = ImmutableArray.CreateBuilder<CheckedReferenceTypeDefinition>(
                count);
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var referenceType = row.ConstructorKind switch {
                1 => new CheckedReferenceTypeDefinition(row.InnerType,
                    (row.Qualifiers & 1) != 0,
                    (row.Qualifiers & 2) != 0, false, false),
                2 => new CheckedReferenceTypeDefinition(row.InnerType,
                    (row.Qualifiers & 1) != 0, false, true, false),
                3 => ReadOptionalReferenceRow(row.InnerType, rows),
                _ => throw new ModuleFormatException(
                    "Private checked-reference type row has an unknown constructor."),
            };
            referenceTypes.Add(referenceType);
        }

        var result = referenceTypes.MoveToImmutable();
        if (!CheckedReferenceTypeContract.IsValidTable(result))
        {
            throw new ModuleFormatException(
                "Private checked-reference type table is invalid.");
        }
        for (var index = 0; index < result.Length; index++)
        {
            var referenceType = result[index];
            if (!IsValidPrivateReferenceValueType(
                    referenceType.ReferentType, result, structures, classes,
                    functionPointerSignatures, pointerTypes,
                    fixedArrayTypes, allowUnresolvedEnums: true))
            {
                throw new ModuleFormatException(
                    "Private checked-reference type table names an absent or unsupported inner type.");
            }
        }
        moduleReader.CheckedReferenceTypes = result;
        moduleReader.HasReadCheckedReferenceTypes = true;
        return result;
    }

    private static bool HasNominalCheckedReferenceTypeClosure(
        ImmutableArray<CheckedReferenceTypeDefinition> referenceTypes,
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<BoundClass> classes,
        ImmutableArray<NativeFunctionPointerSignatureDefinition>
            functionPointerSignatures,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrayTypes,
        ImmutableArray<BoundEnumDefinition> enumDefinitions = default,
        bool allowUnresolvedEnums = false)
    {
        var visited = new HashSet<PrimitiveType>();
        foreach (var referenceType in referenceTypes)
        {
            if (ContainsNominalType(referenceType.ReferentType))
            {
                return true;
            }
        }
        return false;

        bool ContainsNominalType(PrimitiveType type)
        {
            if (CheckedReferenceTypes.IsType(type))
            {
                return (uint)CheckedReferenceTypes.Index(type)
                        < (uint)referenceTypes.Length
                    && visited.Add(type)
                    && ContainsNominalType(referenceTypes[
                        CheckedReferenceTypes.Index(type)].ReferentType);
            }
            if (NativePointerTypes.IsType(type))
            {
                var index = NativePointerTypes.Index(type);
                return (uint)index < (uint)pointerTypes.Length
                    && visited.Add(type)
                    && ContainsNominalType(pointerTypes[index].PointeeType);
            }
            if (NativeFunctionPointerTypes.IsType(type))
            {
                var index = NativeFunctionPointerTypes.Index(type);
                if ((uint)index >= (uint)functionPointerSignatures.Length
                    || !visited.Add(type))
                {
                    return false;
                }

                var signature = functionPointerSignatures[index];
                return ContainsNominalType(signature.ReturnType)
                    || signature.ParameterTypes.Any(ContainsNominalType);
            }
            if (NativeFixedArrayTypes.IsType(type))
            {
                var index = NativeFixedArrayTypes.Index(type);
                return (uint)index < (uint)fixedArrayTypes.Length
                    && visited.Add(type)
                    && ContainsNominalType(
                        fixedArrayTypes[index].ElementType);
            }
            if (ManagedClassTypes.IsClass(type)
                || ManagedClassTypes.IsOptional(type))
            {
                return true;
            }

            var value = (int)type;
            return value is >= 100 and < BoundEnumTypes.First;
        }
    }

    private static CheckedReferenceTypeDefinition
        ReadOptionalReferenceRow(PrimitiveType innerType,
            PrivateCheckedReferenceTypeRow[] rows)
    {
        if (!CheckedReferenceTypes.IsType(innerType))
        {
            throw new ModuleFormatException(
                "The current private host type carrier cannot reconstruct an optional non-reference value.");
        }

        var innerIndex = CheckedReferenceTypes.Index(innerType);
        if ((uint)innerIndex >= (uint)rows.Length
            || rows[innerIndex].ConstructorKind is not (1 or 2))
        {
            throw new ModuleFormatException(
                "Private checked-reference type table contains a nested or unresolved optional layer.");
        }

        var inner = rows[innerIndex];
        return inner.ConstructorKind == 1
            ? new CheckedReferenceTypeDefinition(inner.InnerType,
                (inner.Qualifiers & 1) != 0,
                (inner.Qualifiers & 2) != 0, false, true)
            : new CheckedReferenceTypeDefinition(inner.InnerType,
                (inner.Qualifiers & 1) != 0, false, true, true);
    }

    private static bool IsValidPrivateReferenceValueType(
        PrimitiveType type,
        ImmutableArray<CheckedReferenceTypeDefinition> referenceTypes,
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<BoundClass> classes,
        ImmutableArray<NativeFunctionPointerSignatureDefinition>
            functionPointerSignatures,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrayTypes,
        ImmutableArray<BoundEnumDefinition> enumDefinitions = default,
        bool allowUnresolvedEnums = false)
    {
        if (type == PrimitiveType.Void)
        {
            return false;
        }
        if (CheckedReferenceTypes.IsType(type))
        {
            return (uint)CheckedReferenceTypes.Index(type)
                < (uint)referenceTypes.Length;
        }
        if (NativePointerTypes.IsType(type))
        {
            return (uint)NativePointerTypes.Index(type)
                < (uint)pointerTypes.Length;
        }
        if (NativeFunctionPointerTypes.IsType(type))
        {
            return (uint)NativeFunctionPointerTypes.Index(type)
                < (uint)functionPointerSignatures.Length;
        }
        if (NativeFixedArrayTypes.IsType(type))
        {
            return (uint)NativeFixedArrayTypes.Index(type)
                < (uint)fixedArrayTypes.Length;
        }
        if (BoundEnumTypes.IsType(type))
        {
            return allowUnresolvedEnums
                || BoundEnumTypes.IsValidType(type, enumDefinitions);
        }
        var classType = ManagedClassTypes.IsOptional(type)
            ? ManagedClassTypes.Required(type) : type;
        if (ManagedClassTypes.IsClass(classType))
        {
            return (uint)ManagedClassTypes.ClassIndex(classType)
                < (uint)classes.Length;
        }

        var typeValue = (int)type;
        if (typeValue is >= 100 and < ManagedClassTypes.First)
        {
            return (uint)(typeValue - 100) < (uint)structures.Length;
        }

        return Enum.IsDefined(type)
            && type is not (PrimitiveType.LegacyNativeCallbackAddress
                or PrimitiveType.NativeFixedArray
                or PrimitiveType.LegacyNativeAddress
                or PrimitiveType.LegacyNativeStorageRecord);
    }

    private static void ValidatePrivateReferenceTypeUses(
        ImmutableArray<CheckedReferenceTypeDefinition> referenceTypes,
        ImmutableArray<HostBuffer> invocationBuffers,
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<BoundClass> classes,
        ImmutableArray<NativeFunctionPointerSignatureDefinition>
            functionPointerSignatures,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrayTypes,
        ImmutableArray<BoundEnumDefinition> enumDefinitions,
        bool reading)
    {
        void RequireType(PrimitiveType type, bool allowVoid)
        {
            if (allowVoid && type == PrimitiveType.Void)
            {
                return;
            }
            if (!IsValidPrivateReferenceValueType(type, referenceTypes,
                    structures, classes, functionPointerSignatures,
                    pointerTypes, fixedArrayTypes, enumDefinitions))
            {
                var message
                    = "Private module contains an absent or unsupported complete type.";
                if (reading)
                {
                    throw new ModuleFormatException(message);
                }

                throw new ModuleContractException(message);
            }
        }

        foreach (var buffer in invocationBuffers)
        {
            RequireType(buffer.ElementType, allowVoid: false);
        }
        foreach (var structure in structures)
        {
            foreach (var field in structure.Fields)
            {
                RequireType(field.Type, allowVoid: false);
            }
        }
        foreach (var @class in classes)
        {
            foreach (var field in @class.Fields)
            {
                RequireType(field.Type, allowVoid: false);
            }
        }
        foreach (var signature in functionPointerSignatures)
        {
            RequireType(signature.ReturnType, allowVoid: true);
            foreach (var parameter in signature.ParameterTypes)
            {
                RequireType(parameter, allowVoid: true);
            }
        }
        foreach (var pointerType in pointerTypes)
        {
            RequireType(pointerType.PointeeType, allowVoid: true);
        }
        foreach (var fixedArray in fixedArrayTypes)
        {
            RequireType(fixedArray.ElementType, allowVoid: true);
        }
    }

    private static ImmutableArray<BoundEnumDefinition>
        ReadPrivateNominalEnumDefinitions(BinaryReader reader,
            ImmutableArray<BoundPrivateNominalTypeOrigin> origins,
            ImmutableArray<BoundStruct> structures)
    {
        if (reader is not ModuleReader {
                FormatVersion: CheckedReferenceVersion,
            })
        {
            return [];
        }

        var count = Count(reader, BoundEnumTypes.MaximumCount);
        if (count != 0
            && structures.Length > BoundEnumTypes.First - 100)
        {
            throw new ModuleFormatException(
                "Private enum identities require the selected 256-row inline structure range.");
        }
        var definitions = ImmutableArray.CreateBuilder<BoundEnumDefinition>(
            count);
        var seenOrigins = new HashSet<int>();
        var totalMembers = 0;
        for (var index = 0; index < count; index++)
        {
            var originReference = reader.ReadUInt32();
            if (originReference == 0
                || originReference > (uint)origins.Length)
            {
                throw new ModuleFormatException(
                    "Private enum directory has an invalid nominal-origin reference.");
            }
            var originIndex = checked((int)originReference - 1);
            var origin = origins[originIndex];
            var lookupName = GetString(reader);
            var underlyingType = (PrimitiveType)reader.ReadInt32();
            var flags = reader.ReadByte();
            var reservedByte = reader.ReadByte();
            var reservedWord = reader.ReadUInt16();
            var blittableTargetRequirement = reader.ReadInt32();
            var memberCount = Count(reader, MaximumNodes - totalMembers);
            totalMembers = checked(totalMembers + memberCount);
            if (origin.PrivateType != BoundEnumTypes.Type(index)
                || !seenOrigins.Add(originIndex)
                || underlyingType != PrimitiveType.Int32
                || (flags & ~1) != 0 || reservedByte != 0
                || reservedWord != 0
                || blittableTargetRequirement is not (0
                    or FixedLayoutStorage.WindowsX64TargetRequirement))
            {
                throw new ModuleFormatException(
                    "Private enum directory row has an invalid type, origin, flags, or target requirement.");
            }

            var members
                = ImmutableArray.CreateBuilder<
                    BoundPrivateEnumMemberSnapshot>(memberCount);
            for (var memberIndex = 0;
                memberIndex < memberCount; memberIndex++)
            {
                var declarationKey = GetString(reader);
                var name = GetString(reader);
                var value = reader.ReadInt32();
                if (!PrivateModuleIdentityInputs.IsValidEnumMemberKey(
                        declarationKey))
                {
                    throw new ModuleFormatException(
                        "Private enum member key is invalid.");
                }
                members.Add(new BoundPrivateEnumMemberSnapshot(name, value,
                    IsPublic: true, declarationKey));
            }

            definitions.Add(BoundEnumDefinition.CreatePrivate(origin,
                lookupName, underlyingType, members.MoveToImmutable(),
                isPublic: true, isPrimary: true,
                isFlags: (flags & 1) != 0,
                blittableTargetRequirement));
        }

        var enumOrigins = origins.Select((origin, index) => (origin, index))
            .Where(static item => BoundEnumTypes.IsType(
                item.origin.PrivateType))
            .ToArray();
        if (enumOrigins.Length != count
            || enumOrigins.Any(item => !seenOrigins.Contains(item.index))
            || !BoundEnumTypes.IsValidTable(definitions.ToImmutable())
            || Count(reader, MaximumNodes) != 0)
        {
            throw new ModuleFormatException(
                "Private enum directory is incomplete or an unsupported foreign-interface directory is present.");
        }

        return definitions.MoveToImmutable();
    }

    private static void WritePrivateRevision106FunctionMetadata(
        BinaryWriter writer, ModuleFunction item)
    {
        var function = item.Function;
        var expectedDirectNativeImport = item.ExpectedDirectNativeImport;
        var copyOriginSummary = function is BoundFunction
            { CopyOriginSummary: BoundCopyOriginSummary summary }
            && writer is ModuleWriter summaryWriter
                ? CreatePrivate105CopyOriginSummaryData(function, summary,
                    summaryWriter.Structures,
                    summaryWriter.CheckedReferenceTypes)
            : item.ExpectedCopyOriginSummaryData;
        var hasPrivateDependencyContract = item.Contract.Contains(
            "|dependency:", StringComparison.Ordinal);
        var hasVTableParameterContract = item.Contract.Contains(
            "|NativeVTableParameter:", StringComparison.Ordinal);
        var hasDirectNativeImportContract = item.Contract.Contains(
            "|DirectNativeImport:", StringComparison.Ordinal);
        var hasNativeImportResolutionContract = item.Contract.Contains(
            "|NativeImportResolution:", StringComparison.Ordinal);
        // Local raw-union coverage is re-derived from the persisted body;
        // declaration-only coverage promises have no private carrier here.
        if ((function is not null
                && item.ExpectedCopyOriginSummaryData is not null)
            || (function is not null
                && ((function.IsExternal
                        != (function.DirectNativeImport is not null))
                    || (function.DirectNativeImport is not null
                        && (writer is not ModuleWriter importWriter
                            || !DirectNativeImportContract
                                .IsValidPrivateWindows(function,
                                    importWriter.Structures,
                                    importWriter.NativeFunctionPointerSignatures,
                                    importWriter.NativePointerTypes)))
                    || (function.NativeVTableMethod
                        is NativeVTableMethodDefinition nativeVTableMethod
                        && !nativeVTableMethod.Outputs.IsEmpty)
                    || (function.NativeFunctionPointerType
                            is PrimitiveType functionPointerType
                        && (function.NativeCallableConvention is null
                                || writer is not ModuleWriter functionWriter
                            || !NativeFunctionPointerContract.MatchesFunction(
                                function, functionPointerType,
                                    functionWriter.NativeFunctionPointerSignatures)))
                    || (!function.DependencyContracts.IsEmpty
                        && !HasOnlyRevision92NativeOutputDependencyContracts(
                            function))
                    || !function.ResultDependencyParameters.IsEmpty
                    || function.ResultDependencyIncludesReceiver
                    || function.ReturnsForwardedBacking
                    || (!function.RawUnionReturnCoverage.IsEmpty
                        && function.IsExternal)))
            || (function is null
                && (!item.Import
                    || hasPrivateDependencyContract
                    || (item.ImportedNativeVTableMethod is null
                        && hasVTableParameterContract)
                    || hasDirectNativeImportContract
                    || (hasNativeImportResolutionContract
                        != (expectedDirectNativeImport is not null)))))
        {
            var details = function is BoundFunction unsupported
                ? $"{item.Name}: dependencies={unsupported.DependencyContracts.Length}, copy-origin={unsupported.CopyOriginSummary is not null}, result-dependencies={unsupported.ResultDependencyParameters.Length}, result-receiver={unsupported.ResultDependencyIncludesReceiver}, forwarded-backing={unsupported.ReturnsForwardedBacking}, raw-union-coverage={unsupported.RawUnionReturnCoverage.Length}"
                : $"{item.Name}: imported-vtable="
                    + $"{item.ImportedNativeVTableMethod is not null}, "
                    + $"dependency-contract={hasPrivateDependencyContract}, "
                    + $"vtable-parameter-contract={hasVTableParameterContract}, "
                    + $"direct-native-import={hasDirectNativeImportContract}, "
                    + $"native-import-resolution={hasNativeImportResolutionContract}, "
                    + $"expected-direct-native-import="
                    + $"{expectedDirectNativeImport is not null}";
            throw new ModuleContractException(
                $"The current private 106 writer slice requires local declarations without unimplemented publication or provenance carriers ({details}).");
        }

        writer.Write(0); // general Dependency count
        writer.Write(0); // checkedSelectorCount
        writer.Write(0); // checkedOutputStateCount
        WritePrivate106RawUnionReturnCoverage(writer, item);
        WritePrivate105CopyOriginSummary(writer, copyOriginSummary);
        writer.Write((byte)0); // no foreign-interface member variant
        if (expectedDirectNativeImport is not null)
        {
            ValidatePrivate105ExpectedDirectNativeImport(writer, item,
                expectedDirectNativeImport);
        }
        writer.Write(expectedDirectNativeImport is not null);
        if (expectedDirectNativeImport is DirectNativeImport expectedImport)
        {
            PutString(writer, expectedImport.LibraryName);
            PutString(writer, expectedImport.ExportName);
            writer.Write(expectedImport.Parameters.Length);
            foreach (var parameter in expectedImport.Parameters.OrderBy(
                static parameter => parameter.ParameterIndex))
            {
                writer.Write(parameter.ParameterIndex);
                WritePrivateType(writer, parameter.PointeeType);
                writer.Write((byte)parameter.Access);
                writer.Write((byte)parameter.Retention);
                writer.Write(parameter.Extent);
            }
        }
    }

    private static Private105CopyOriginSummaryData
        CreatePrivate105CopyOriginSummaryData(BoundFunction function,
            BoundCopyOriginSummary summary,
            ImmutableArray<BoundStruct> structures,
            ImmutableArray<CheckedReferenceTypeDefinition>
                checkedReferenceTypes)
    {
        if ((function.IsExternal && !function.IsModuleReference)
            || function.DirectNativeImport is not null
            || function.NativeVTableMethod is not null
            || function.NativeFunctionPointerType is not null
            || function.NativeCallableEntryName is not null
            || function.IsClassConstructor
            || function.IsCleanup || function.DoesNotReturn
            || summary.Facts.IsDefaultOrEmpty
            || summary.Facts.Length > MaximumNodes)
        {
            throw new ModuleContractException(
                "Private revision 106 CopyOrigin summary has an inapplicable callable.");
        }

        var facts = new List<Private105CopyOriginFactData>(
            summary.Facts.Length);
        foreach (var fact in summary.Facts)
        {
            if (fact is null || fact.Origins.IsDefaultOrEmpty
                || fact.Origins.Length > MaximumNodes)
            {
                throw new ModuleContractException(
                    "Private revision 106 CopyOrigin fact is incomplete.");
            }

            var destinationPath = GetPrivate105CopyOriginPath(
                function.ReturnType, fact.DestinationPath, structures);
            var origins = new List<Private105CopyOriginData>(
                fact.Origins.Length);
            foreach (var origin in fact.Origins)
            {
                var sourceKind = origin.SourceKind switch {
                    BoundCopyOriginSourceKind.Formal => (byte)1,
                    BoundCopyOriginSourceKind.Receiver => (byte)2,
                    _ => throw new ModuleContractException(
                        "Private revision 106 CopyOrigin has an unknown source kind."),
                };
                var sourceView = origin.SourceView switch {
                    BoundCopyOriginSourceView.EntryValue => (byte)1,
                    BoundCopyOriginSourceView.EntryReferentValue => (byte)2,
                    _ => throw new ModuleContractException(
                        "Private revision 106 CopyOrigin has an unknown source view."),
                };
                var sourceType = Private105CopyOriginSourceType(function,
                    origin, checkedReferenceTypes);
                var sourcePath = GetPrivate105CopyOriginPath(sourceType,
                    origin.SourcePath, structures);
                origins.Add(new Private105CopyOriginData(sourceKind,
                    sourceView, origin.SourceOrdinal, sourcePath));
            }
            origins.Sort(ComparePrivate105CopyOriginData);
            facts.Add(new Private105CopyOriginFactData(destinationPath,
                [.. origins]));
        }
        facts.Sort(static (left, right) =>
            ComparePrivate105CopyOriginPaths(left.DestinationPath,
                right.DestinationPath));

        var kind = summary.Kind switch {
            BoundCopyOriginSummaryKind.NormalResult => (byte)1,
            BoundCopyOriginSummaryKind.FreshInlineConstruction => (byte)2,
            _ => throw new ModuleContractException(
                "Private revision 106 CopyOrigin has an unknown summary kind."),
        };
        return new Private105CopyOriginSummaryData(kind,
            [.. facts]);
    }

    private static PrimitiveType Private105CopyOriginSourceType(
        BoundFunction function, BoundCopyOrigin origin,
        ImmutableArray<CheckedReferenceTypeDefinition>
            checkedReferenceTypes)
    {
        var hasBorrowedReceiver = function.DeclaringStruct is not null
            && !function.IsStatic && !function.IsConstructor
            && !function.IsClassConstructor;
        if (origin.SourceKind == BoundCopyOriginSourceKind.Receiver)
        {
            if (!hasBorrowedReceiver || origin.SourceOrdinal != 0
                || origin.SourceView
                    != BoundCopyOriginSourceView.EntryReferentValue
                || function.Variables.IsDefaultOrEmpty)
            {
                throw new ModuleContractException(
                    "Private revision 106 CopyOrigin has an invalid receiver source.");
            }

            return function.Variables[0].Type;
        }
        if (origin.SourceKind != BoundCopyOriginSourceKind.Formal
            || origin.SourceOrdinal < 0
            || origin.SourceOrdinal
                >= function.ParameterCount - (hasBorrowedReceiver ? 1 : 0))
        {
            throw new ModuleContractException(
                "Private revision 106 CopyOrigin has an invalid formal source.");
        }

        var variableIndex = origin.SourceOrdinal
            + (function.IsConstructor || hasBorrowedReceiver ? 1 : 0);
        if ((uint)variableIndex >= (uint)function.Variables.Length)
        {
            throw new ModuleContractException(
                "Private revision 106 CopyOrigin source formal is missing.");
        }

        var formal = function.Variables[variableIndex];
        if (formal.IsOut || formal.IsInit)
        {
            throw new ModuleContractException(
                "Private revision 106 CopyOrigin source is not an ordinary input.");
        }
        if (origin.SourceView == BoundCopyOriginSourceView.EntryValue)
        {
            if (formal.IsImplicitRef)
            {
                throw new ModuleContractException(
                    "Private revision 106 EntryValue cannot select an implicit reference formal.");
            }
            return formal.Type;
        }
        if (origin.SourceView
            != BoundCopyOriginSourceView.EntryReferentValue)
        {
            throw new ModuleContractException(
                "Private revision 106 CopyOrigin has an invalid source view.");
        }
        if (formal.IsImplicitRef)
        {
            return formal.Type;
        }
        if (!CheckedReferenceTypes.IsType(formal.Type)
            || (uint)CheckedReferenceTypes.Index(formal.Type)
                >= (uint)checkedReferenceTypes.Length)
        {
            throw new ModuleContractException(
                "Private revision 106 EntryReferentValue has no checked-reference type.");
        }

        var reference = checkedReferenceTypes[
            CheckedReferenceTypes.Index(formal.Type)];
        if (reference.IsGarbageCollected || reference.IsInitialization
            || reference.IsOptional)
        {
            throw new ModuleContractException(
                "Private revision 106 EntryReferentValue uses an unsupported checked reference.");
        }
        return reference.ReferentType;
    }

    private static ImmutableArray<int> GetPrivate105CopyOriginPath(
        PrimitiveType rootType, BoundCopyOriginFieldPath path,
        ImmutableArray<BoundStruct> structures)
    {
        if (path.Steps.Length > MaximumDepth)
        {
            throw new ModuleContractException(
                "Private revision 106 CopyOrigin path exceeds the supported depth.");
        }

        var ordinals = ImmutableArray.CreateBuilder<int>(
            path.Steps.Length);
        var currentType = rootType;
        foreach (var step in path.Steps)
        {
            var structureIndex = (int)currentType - 100;
            if (step.OwnerType != currentType
                || structureIndex < 0
                || (uint)structureIndex >= (uint)structures.Length
                || BoundEnumTypes.IsType(currentType)
                || (uint)step.FieldOrdinal
                    >= (uint)structures[structureIndex].Fields.Length)
            {
                throw new ModuleContractException(
                    "Private revision 106 CopyOrigin path has an invalid field owner or ordinal.");
            }

            ordinals.Add(step.FieldOrdinal);
            currentType = structures[structureIndex]
                .Fields[step.FieldOrdinal].Type;
        }

        return ordinals.MoveToImmutable();
    }

    private static int ComparePrivate105CopyOriginData(
        Private105CopyOriginData left, Private105CopyOriginData right)
    {
        var compare = left.SourceKind.CompareTo(right.SourceKind);
        if (compare != 0)
        {
            return compare;
        }
        compare = left.SourceOrdinal.CompareTo(right.SourceOrdinal);
        if (compare != 0)
        {
            return compare;
        }
        compare = left.SourceView.CompareTo(right.SourceView);
        return compare != 0 ? compare
            : ComparePrivate105CopyOriginPaths(left.SourcePath,
                right.SourcePath);
    }

    private static int ComparePrivate105CopyOriginPaths(
        ImmutableArray<int> left, ImmutableArray<int> right)
    {
        var length = Math.Min(left.Length, right.Length);
        for (var index = 0; index < length; index++)
        {
            var compare = left[index].CompareTo(right[index]);
            if (compare != 0)
            {
                return compare;
            }
        }
        return left.Length.CompareTo(right.Length);
    }

    private static void WritePrivate105CopyOriginSummary(BinaryWriter writer,
        Private105CopyOriginSummaryData? summary)
    {
        if (summary is null)
        {
            writer.Write((byte)0);
            return;
        }
        if (summary.Kind is not (1 or 2) || summary.Facts.IsDefaultOrEmpty
            || summary.Facts.Length > MaximumNodes)
        {
            throw new ModuleContractException(
                "Private revision 106 CopyOrigin summary is invalid.");
        }

        writer.Write(summary.Kind);
        writer.Write(summary.Facts.Length);
        var previousPath = (ImmutableArray<int>?)null;
        foreach (var fact in summary.Facts)
        {
            if (fact is null || fact.Origins.IsDefaultOrEmpty
                || fact.Origins.Length > MaximumNodes
                || (previousPath is ImmutableArray<int> previous
                    && ComparePrivate105CopyOriginPaths(previous,
                        fact.DestinationPath) >= 0)
                || (previousPath is ImmutableArray<int> previousPathValue
                    && (IsPrivate105CopyOriginPathPrefix(
                            previousPathValue, fact.DestinationPath)
                        || IsPrivate105CopyOriginPathPrefix(
                            fact.DestinationPath, previousPathValue))))
            {
                throw new ModuleContractException(
                    "Private revision 106 CopyOrigin fact order or extent is invalid.");
            }
            WritePrivate105CopyOriginPath(writer, fact.DestinationPath);
            writer.Write(fact.Origins.Length);
            Private105CopyOriginData? previousOrigin = null;
            foreach (var origin in fact.Origins)
            {
                if (origin.SourceKind is not (1 or 2)
                    || origin.SourceView is not (1 or 2)
                    || origin.SourceOrdinal < 0
                    || (origin.SourceKind == 2
                        && origin.SourceOrdinal != 0)
                    || (previousOrigin is Private105CopyOriginData prior
                        && ComparePrivate105CopyOriginData(prior,
                            origin) >= 0))
                {
                    throw new ModuleContractException(
                        "Private revision 106 CopyOrigin alternative is invalid or unordered.");
                }

                writer.Write(origin.SourceKind);
                writer.Write(origin.SourceView);
                writer.Write((ushort)0);
                writer.Write((uint)origin.SourceOrdinal);
                WritePrivate105CopyOriginPath(writer, origin.SourcePath);
                previousOrigin = origin;
            }
            previousPath = fact.DestinationPath;
        }
    }

    private static void WritePrivate105CopyOriginPath(BinaryWriter writer,
        ImmutableArray<int> path)
    {
        if (path.IsDefault || path.Length > MaximumDepth
            || path.Any(static ordinal => ordinal < 0))
        {
            throw new ModuleContractException(
                "Private revision 106 CopyOrigin path is invalid.");
        }

        writer.Write(path.Length);
        foreach (var ordinal in path)
        {
            writer.Write((uint)ordinal);
        }
    }

    private static void WritePrivate106RawUnionReturnCoverage(
        BinaryWriter writer, ModuleFunction item)
    {
        if (writer is not ModuleWriter moduleWriter
            || moduleWriter.SourceProgram is not BoundProgram program)
        {
            throw new ModuleContractException(
                "Private revision 106 coverage requires its source program and type context.");
        }

        var sourceFunction = moduleWriter.SourceFunctions.SingleOrDefault(
            function => function.Name == item.Name)
            ?? throw new ModuleContractException(
                $"Private revision 106 declaration '{item.Name}' has no source coverage context.");
        if (!item.HasDeclaredRawUnionReturnCoverage)
        {
            throw new ModuleContractException(
                $"Private revision 106 declaration '{item.Name}' has no retained raw-Union coverage view.");
        }

        var coverage = item.DeclaredRawUnionReturnCoverage;
        if (item.Function is BoundFunction declarationFunction
            && !coverage.SequenceEqual(
                declarationFunction.RawUnionReturnCoverage))
        {
            throw new ModuleContractException(
                $"Private revision 106 declaration '{item.Name}' differs from its function coverage view.");
        }
        if (!coverage.IsEmpty
            && ((sourceFunction.IsExternal
                    && !sourceFunction.IsModuleReference)
                || sourceFunction.DirectNativeImport is not null
                || sourceFunction.NativeVTableMethod is not null
                || sourceFunction.NativeFunctionPointerType is not null
                || sourceFunction.NativeCallableConvention is not null
                || sourceFunction.NativeCallableEntryName is not null))
        {
            throw new ModuleContractException(
                "Private revision 106 raw-Union coverage is not applicable to a foreign declaration.");
        }
        var normalized = NormalizePrivate106RawUnionReturnCoverage(
            sourceFunction.ReturnType, coverage, program);
        writer.Write(normalized.Length);
        foreach (var value in normalized)
        {
            writer.Write(value);
        }
    }

    private static BoundProgram CreatePrivate106CoverageProgram(
        ModuleReader reader) => CreatePrivate106CoverageProgram(
            reader.Structures, reader.Classes,
            reader.NativeFunctionPointerSignatures,
            reader.NativePointerTypes, reader.NativeFixedArrayTypes,
            reader.CheckedReferenceTypes);

    private static BoundProgram CreatePrivate106CoverageProgram(
        Module module) => CreatePrivate106CoverageProgram(
            module.Structures, module.Classes,
            module.NativeFunctionPointerSignatures,
            module.NativePointerTypes, module.NativeFixedArrayTypes,
            module.CheckedReferenceTypes);

    private static BoundProgram CreatePrivate106CoverageProgram(
        CompilationReference reference) =>
        CreatePrivate106CoverageProgram(reference.Structures,
            reference.Classes, reference.NativeFunctionPointerSignatures,
            reference.NativePointerTypes, reference.NativeFixedArrayTypes,
            reference.CheckedReferenceTypes);

    private static BoundProgram CreatePrivate106CoverageProgram(
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<BoundClass> classes,
        ImmutableArray<NativeFunctionPointerSignatureDefinition>
            functionPointerSignatures,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrayTypes,
        ImmutableArray<CheckedReferenceTypeDefinition> checkedReferenceTypes)
        => new([], -1) {
            Structures = structures,
            Classes = classes,
            NativeFunctionPointerSignatures = functionPointerSignatures,
            NativePointerTypes = pointerTypes,
            NativeFixedArrayTypes = fixedArrayTypes,
            CheckedReferenceTypes = checkedReferenceTypes,
        };

    private static ImmutableArray<byte>
        NormalizePrivate106RawUnionReturnCoverage(PrimitiveType resultType,
            ImmutableArray<byte> coverage, BoundProgram program)
    {
        if (coverage.IsDefault)
        {
            throw new ModuleContractException(
                "Private revision 106 raw-Union coverage is uninitialized.");
        }
        if (coverage.IsEmpty)
        {
            return [];
        }
        if (!SemanticVerifier.HasRawUnionReturnCoverageSubject(resultType,
                program))
        {
            throw new ModuleContractException(
                "Raw-Union coverage is attached to a result without an inline Union.");
        }

        var guaranteed =
            SemanticVerifier.RawUnionReturnCoverageBaseBytes(resultType,
                program);
        var possible =
            SemanticVerifier.RawUnionReturnCoveragePossibleBytes(resultType,
                program);
        if (coverage.Length != guaranteed.Length
            || possible.Length != guaranteed.Length
            || coverage.Length > ValueLayout.MaximumStructureSize)
        {
            throw new ModuleContractException(
                "Private revision 106 raw-Union coverage differs from its result extent.");
        }

        var strongerThanGuarantee = false;
        for (var index = 0; index < coverage.Length; index++)
        {
            if (coverage[index] is not (0 or 1)
                || guaranteed[index] is not (0 or 1)
                || possible[index] is not (0 or 1)
                || (guaranteed[index] != 0 && coverage[index] == 0)
                || (coverage[index] != 0 && possible[index] == 0))
            {
                throw new ModuleContractException(
                    "Private revision 106 raw-Union coverage violates its initialized/payload bounds.");
            }

            strongerThanGuarantee |= coverage[index] != 0
                && guaranteed[index] == 0;
        }

        return strongerThanGuarantee ? coverage : [];
    }

    private static ImmutableArray<byte>
        ValidatePrivate106BodylessRawUnionReturnCoverage(
        string contract, ImmutableArray<byte> coverage,
        ModuleReader reader, bool foreignDeclaration)
    {
        var parts = contract.Split('|');
        if (parts.Length < 2)
        {
            throw new ModuleFormatException(
                "Revision 106 bodyless function contract has no result type.");
        }
        var parameterCount = ContractParameterCount(parts);
        for (var index = 0; index < parameterCount; index++)
        {
            var parameter = parts[8 + index];
            var typeStart = parameter.StartsWith("in:implicit:",
                    StringComparison.Ordinal) ? "in:implicit:".Length
                : parameter.StartsWith("in:", StringComparison.Ordinal)
                    ? "in:".Length
                : parameter.StartsWith("out:", StringComparison.Ordinal)
                    ? "out:".Length
                : parameter.StartsWith("init:", StringComparison.Ordinal)
                    ? "init:".Length : -1;
            if (typeStart < 0
                || IsPrivate106ArrayDerivedTypeContract(
                    parameter[typeStart..], reader))
            {
                throw new ModuleFormatException(
                    "Revision 106 bodyless function formal uses an unsupported array-derived type.");
            }
        }

        var coverageProgram = CreatePrivate106CoverageProgram(reader);
        PrimitiveType? rawUnionResultType = null;
        var hasArrayResult = false;
        for (var index = 0; index < reader.Structures.Length; index++)
        {
            var candidate = (PrimitiveType)(100 + index);
            if (ContractType(candidate, reader.Structures,
                    reader.NativeFunctionPointerSignatures,
                    reader.NativePointerTypes, reader.NativeFixedArrayTypes,
                    private105: true) != parts[1])
            {
                continue;
            }

            var hasArrayFields =
                NativeFixedArrayContract.HasMetadataOnlyArrayType(candidate,
                    reader.Structures, reader.NativeFixedArrayTypes,
                    reader.NativePointerTypes,
                    reader.NativeFunctionPointerSignatures);
            var isArrayAggregate =
                NativeFixedArrayContract
                    .IsRootlessCopyAggregateWithFixedArrayFields(candidate,
                        reader.Structures, reader.NativeFixedArrayTypes,
                        reader.NativePointerTypes,
                        reader.NativeFunctionPointerSignatures);
            if (hasArrayFields && !isArrayAggregate)
            {
                throw new ModuleFormatException(
                    "Revision 106 bodyless function has an unsupported array-derived result type.");
            }
            if (isArrayAggregate)
            {
                if (foreignDeclaration)
                {
                    throw new ModuleFormatException(
                        "Revision 106 array-derived results are not supported across foreign declarations.");
                }
                hasArrayResult = true;
            }

            bool carriesRawUnion;
            try
            {
                carriesRawUnion =
                    SemanticVerifier.HasRawUnionReturnCoverageSubject(
                        candidate, coverageProgram);
            }
            catch (Exception exception) when (exception is
                ArgumentException or NotSupportedException
                    or OverflowException)
            {
                throw new ModuleFormatException(
                    "Revision 106 bodyless function result type is invalid.",
                    exception);
            }
            if (isArrayAggregate && !carriesRawUnion)
            {
                throw new ModuleFormatException(
                    "Revision 106 array-derived results require an inline Raw Union.");
            }
            if (carriesRawUnion)
            {
                if (rawUnionResultType is PrimitiveType previous
                    && previous != candidate)
                {
                    throw new ModuleFormatException(
                        "Revision 106 bodyless function result type is ambiguous.");
                }
                rawUnionResultType = candidate;
            }
        }

        for (var index = 0; index < reader.NativeFixedArrayTypes.Length;
            index++)
        {
            var candidate = NativeFixedArrayTypes.Type(index);
            if (ContractType(candidate, reader.Structures,
                    reader.NativeFunctionPointerSignatures,
                    reader.NativePointerTypes, reader.NativeFixedArrayTypes,
                    private105: true) == parts[1])
            {
                throw new ModuleFormatException(
                    "Revision 106 does not admit a fixed array as a function result.");
            }
        }

        if (rawUnionResultType is PrimitiveType resultType)
        {
            return ValidatePrivate106RawUnionReturnCoverage(resultType,
                coverage, coverageProgram);
        }
        else if (!coverage.IsEmpty || hasArrayResult)
        {
            throw new ModuleFormatException(
                "Revision 106 raw-Union coverage is not applicable to this bodyless result.");
        }
        return [];
    }

    private static bool IsPrivate106ArrayDerivedTypeContract(
        string typeContract, ModuleReader reader)
    {
        for (var index = 0; index < reader.Structures.Length; index++)
        {
            var type = (PrimitiveType)(100 + index);
            if (!IsRevision100RuntimeType(type, reader.Structures,
                    reader.NativeFixedArrayTypes, reader.NativePointerTypes,
                    reader.NativeFunctionPointerSignatures)
                && ContractType(type, reader.Structures,
                    reader.NativeFunctionPointerSignatures,
                    reader.NativePointerTypes, reader.NativeFixedArrayTypes,
                    private105: true) == typeContract)
            {
                return true;
            }
        }
        for (var index = 0; index < reader.NativeFixedArrayTypes.Length;
            index++)
        {
            var type = NativeFixedArrayTypes.Type(index);
            if (ContractType(type, reader.Structures,
                    reader.NativeFunctionPointerSignatures,
                    reader.NativePointerTypes, reader.NativeFixedArrayTypes,
                    private105: true) == typeContract)
            {
                return true;
            }
        }
        for (var index = 0; index < reader.NativePointerTypes.Length;
            index++)
        {
            var type = NativePointerTypes.Type(index);
            if (!IsRevision100RuntimeType(type, reader.Structures,
                    reader.NativeFixedArrayTypes, reader.NativePointerTypes,
                    reader.NativeFunctionPointerSignatures)
                && ContractType(type, reader.Structures,
                    reader.NativeFunctionPointerSignatures,
                    reader.NativePointerTypes, reader.NativeFixedArrayTypes,
                    private105: true) == typeContract)
            {
                return true;
            }
        }
        for (var index = 0;
            index < reader.NativeFunctionPointerSignatures.Length; index++)
        {
            var type = NativeFunctionPointerTypes.Type(index);
            if (!IsRevision100RuntimeType(type, reader.Structures,
                    reader.NativeFixedArrayTypes, reader.NativePointerTypes,
                    reader.NativeFunctionPointerSignatures)
                && ContractType(type, reader.Structures,
                    reader.NativeFunctionPointerSignatures,
                    reader.NativePointerTypes, reader.NativeFixedArrayTypes,
                    private105: true) == typeContract)
            {
                return true;
            }
        }
        return false;
    }

    private static ImmutableArray<byte>
        ValidatePrivate106RawUnionReturnCoverage(PrimitiveType resultType,
            ImmutableArray<byte> coverage, BoundProgram program)
    {
        try
        {
            return NormalizePrivate106RawUnionReturnCoverage(resultType,
                coverage, program);
        }
        catch (Exception exception) when (exception is
            ModuleContractException or ArgumentException
                or NotSupportedException or OverflowException)
        {
            throw new ModuleFormatException(
                "Revision 106 raw-Union coverage is invalid or unsupported.",
                exception);
        }
    }

    private static bool Private106RawUnionReturnCoverageSatisfies(
        ImmutableArray<byte> expected, PrimitiveType expectedType,
        BoundProgram expectedProgram, ImmutableArray<byte> actual,
        PrimitiveType actualType, BoundProgram actualProgram)
    {
        var expectedBytes = Private106EffectiveRawUnionReturnCoverage(
            expectedType, expected, expectedProgram);
        var actualBytes = Private106EffectiveRawUnionReturnCoverage(
            actualType, actual, actualProgram);
        if (expectedBytes.Length != actualBytes.Length)
        {
            return false;
        }

        for (var index = 0; index < expectedBytes.Length; index++)
        {
            if (expectedBytes[index] != 0 && actualBytes[index] == 0)
            {
                return false;
            }
        }
        return true;
    }

    private static byte[] Private106EffectiveRawUnionReturnCoverage(
        PrimitiveType type, ImmutableArray<byte> coverage,
        BoundProgram program)
    {
        var normalized = NormalizePrivate106RawUnionReturnCoverage(
            type, coverage, program);
        return normalized.IsEmpty
            ? SemanticVerifier.HasRawUnionReturnCoverageSubject(type,
                program)
                ? SemanticVerifier.RawUnionReturnCoverageBaseBytes(type,
                    program)
                : []
            : [.. normalized];
    }

    private static bool Private105CopyOriginSummarySatisfies(
        Private105CopyOriginSummaryData? expected,
        Private105CopyOriginSummaryData? actual)
    {
        if (expected is null)
        {
            return true;
        }
        if (actual is null || expected.Kind != actual.Kind)
        {
            return false;
        }

        foreach (var expectedFact in expected.Facts)
        {
            var actualFact = actual.Facts.FirstOrDefault(fact =>
                fact.DestinationPath.SequenceEqual(
                    expectedFact.DestinationPath));
            if (actualFact is null)
            {
                return false;
            }
            foreach (var actualOrigin in actualFact.Origins)
            {
                if (!expectedFact.Origins.Any(expectedOrigin =>
                        SamePrivate105CopyOrigin(expectedOrigin,
                            actualOrigin)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool SamePrivate105CopyOrigin(
        Private105CopyOriginData left,
        Private105CopyOriginData right) =>
        left.SourceKind == right.SourceKind
        && left.SourceView == right.SourceView
        && left.SourceOrdinal == right.SourceOrdinal
        && left.SourcePath.SequenceEqual(right.SourcePath);

    private static void ValidatePrivate105ExpectedDirectNativeImport(
        BinaryWriter writer, ModuleFunction item,
        DirectNativeImport expectedImport)
    {
        if (writer is not ModuleWriter moduleWriter
            || !item.Import || item.Function is not null
            || item.ImportedNativeVTableMethod is not null
            || !expectedImport.Outputs.IsEmpty
            || !DirectNativeImportContract.IsValidPrivateWindowsLibraryName(
                expectedImport.LibraryName)
            || !DirectNativeImportContract.IsValidExportName(
                expectedImport.ExportName)
            || expectedImport.Parameters.IsDefault
            || expectedImport.Parameters.Length > ModuleCompiler.MaximumSlots
            || !item.Contract.Contains("|NativeImportResolution:",
                StringComparison.Ordinal))
        {
            throw new ModuleContractException(
                "Private revision 106 direct-import expectation has an invalid owner or descriptor.");
        }
        if (RestorePrivate105DirectNativeImportSettings(expectedImport,
                item.Contract) != expectedImport)
        {
            throw new ModuleContractException(
                "Private revision 106 direct-import tokens differ from their expected descriptor.");
        }

        var parts = item.Contract.Split('|');
        var parameterCount = ContractParameterCount(parts);
        var previousFormal = -1;
        foreach (var parameter in expectedImport.Parameters.OrderBy(
            static parameter => parameter.ParameterIndex))
        {
            if (parameter.ParameterIndex <= previousFormal
                || parameter.ParameterIndex >= parameterCount
                || parameter.Access is not (NativeParameterAccess.Read
                    or NativeParameterAccess.Write
                    or NativeParameterAccess.ReadWrite)
                || parameter.Retention != NativeParameterRetention.NoEscape
                || !DirectNativeImportContract.IsInputPointee(
                    parameter.PointeeType, moduleWriter.Structures)
                || (NativePointerTypes.IsType(parameter.PointeeType)
                    && (uint)NativePointerTypes.Index(parameter.PointeeType)
                        >= (uint)moduleWriter.NativePointerTypes.Length)
                || (NativeFunctionPointerTypes.IsType(parameter.PointeeType)
                    && !NativeFunctionPointerContract.IsValidType(
                        parameter.PointeeType,
                        moduleWriter.NativeFunctionPointerSignatures)))
            {
                throw new ModuleContractException(
                    "Private revision 106 direct-import expectation has an invalid native parameter.");
            }

            var expectedType = Private105NativeParameterFormalType(
                parameter, moduleWriter.Structures,
                moduleWriter.NativeFunctionPointerSignatures,
                moduleWriter.NativePointerTypes,
                moduleWriter.NativeFixedArrayTypes);
            if (parts[8 + parameter.ParameterIndex] != expectedType)
            {
                throw new ModuleContractException(
                    "Private revision 106 direct-import expectation differs from its stored formal.");
            }
            previousFormal = parameter.ParameterIndex;
        }

        if (item.NativeOutputContracts.Any(output =>
            expectedImport.Parameters.Any(parameter =>
                parameter.ParameterIndex == output.FormalOrdinal)))
        {
            throw new ModuleContractException(
                "Private revision 106 direct-import expectations overlap NativeOutput formals.");
        }
    }

    private static Private106FunctionMetadata
        ReadPrivateRevision106FunctionMetadata(
        BinaryReader reader, bool import, bool hasBody)
    {
        if (reader is not ModuleReader {
                FormatVersion: CheckedReferenceVersion,
            })
        {
            return default;
        }
        if (!import && !hasBody)
        {
            throw new ModuleFormatException(
                "A private revision 106 bodyless declaration must be an import.");
        }

        var dependencyCount = Count(reader, MaximumNodes);
        var selectorCount = Count(reader, MaximumNodes);
        var outputStateCount = Count(reader, MaximumNodes);
        var rawUnionReturnCoverage
            = ReadPrivate106RawUnionReturnCoverage(reader);
        var copyOriginSummary = ReadPrivate105CopyOriginSummary(reader);
        var foreignInterfaceVariant = reader.ReadByte();
        var hasDirectImportExpectation = GetBool(reader);
        if (dependencyCount != 0 || selectorCount != 0
            || outputStateCount != 0
            || foreignInterfaceVariant != 0)
        {
            throw new ModuleFormatException(
                "Private revision 106 declaration metadata exceeds the current checked-reference type-table slice.");
        }

        if (!hasDirectImportExpectation)
        {
            return new Private106FunctionMetadata(rawUnionReturnCoverage,
                null, copyOriginSummary);
        }
        if (!import || hasBody)
        {
            throw new ModuleFormatException(
                "Private revision 106 direct-import expectation has an invalid owner.");
        }

        var libraryName = GetString(reader);
        var exportName = GetString(reader);
        var parameterCount = CountRows(reader, ModuleCompiler.MaximumSlots,
            14, "direct-import native parameters");
        var parameters =
            ImmutableArray.CreateBuilder<DirectNativeParameter>(
                parameterCount);
        for (var index = 0; index < parameterCount; index++)
        {
            var formalOrdinal = reader.ReadInt32();
            var pointeeType = GetType(reader);
            var access = (NativeParameterAccess)reader.ReadByte();
            var retention = (NativeParameterRetention)reader.ReadByte();
            var extent = reader.ReadUInt32();
            if (formalOrdinal < 0
                || access is not (NativeParameterAccess.Read
                    or NativeParameterAccess.Write
                    or NativeParameterAccess.ReadWrite)
                || retention != NativeParameterRetention.NoEscape)
            {
                throw new ModuleFormatException(
                    "Private revision 106 direct-import NativeParameter row is malformed.");
            }

            parameters.Add(new DirectNativeParameter(formalOrdinal,
                pointeeType, access, retention, extent));
        }

        return new Private106FunctionMetadata(
            rawUnionReturnCoverage,
            new DirectNativeImport(libraryName, exportName, []) {
                Parameters = parameters.MoveToImmutable(),
            }, copyOriginSummary);
    }

    private static ImmutableArray<byte>
        ReadPrivate106RawUnionReturnCoverage(BinaryReader reader)
    {
        var byteCount = CountRows(reader,
            ValueLayout.MaximumStructureSize, 1,
            "raw-Union return-coverage bytes");
        var coverage = ImmutableArray.CreateBuilder<byte>(byteCount);
        for (var index = 0; index < byteCount; index++)
        {
            var value = reader.ReadByte();
            if (value is not (0 or 1))
            {
                throw new ModuleFormatException(
                    "Revision 106 raw-Union coverage contains a noncanonical byte.");
            }
            coverage.Add(value);
        }

        return coverage.MoveToImmutable();
    }

    private static Private105CopyOriginSummaryData?
        ReadPrivate105CopyOriginSummary(BinaryReader reader)
    {
        var kind = reader.ReadByte();
        if (kind == 0)
        {
            return null;
        }
        if (kind is not (1 or 2))
        {
            throw new ModuleFormatException(
                "Revision 106 CopyOrigin summary kind is invalid.");
        }

        var factCount = CountRows(reader, ModuleCompiler.MaximumNodes, 24,
            "CopyOrigin facts");
        if (factCount == 0)
        {
            throw new ModuleFormatException(
                "Revision 106 CopyOrigin summary has no facts.");
        }
        var facts = ImmutableArray.CreateBuilder<
            Private105CopyOriginFactData>(factCount);
        for (var factIndex = 0; factIndex < factCount; factIndex++)
        {
            var destinationPath = ReadPrivate105CopyOriginPath(reader);
            var originCount = CountRows(reader, ModuleCompiler.MaximumNodes,
                16, "CopyOrigin alternatives");
            if (originCount == 0)
            {
                throw new ModuleFormatException(
                    "Revision 106 CopyOrigin fact has no alternatives.");
            }

            var origins = ImmutableArray.CreateBuilder<
                Private105CopyOriginData>(originCount);
            for (var originIndex = 0; originIndex < originCount;
                originIndex++)
            {
                var sourceKind = reader.ReadByte();
                var sourceView = reader.ReadByte();
                var reserved = reader.ReadUInt16();
                var sourceOrdinal = reader.ReadUInt32();
                var sourcePath = ReadPrivate105CopyOriginPath(reader);
                if (sourceKind is not (1 or 2)
                    || sourceView is not (1 or 2)
                    || reserved != 0
                    || sourceOrdinal > int.MaxValue
                    || (sourceKind == 2 && sourceOrdinal != 0))
                {
                    throw new ModuleFormatException(
                        "Revision 106 CopyOrigin alternative is invalid.");
                }

                origins.Add(new Private105CopyOriginData(sourceKind,
                    sourceView, (int)sourceOrdinal, sourcePath));
            }

            var normalizedOrigins = origins.ToArray();
            Array.Sort(normalizedOrigins,
                ComparePrivate105CopyOriginData);
            for (var index = 1; index < normalizedOrigins.Length;
                index++)
            {
                if (ComparePrivate105CopyOriginData(
                        normalizedOrigins[index - 1],
                        normalizedOrigins[index]) == 0)
                {
                    throw new ModuleFormatException(
                        "Revision 106 CopyOrigin fact has duplicate alternatives.");
                }
            }

            facts.Add(new Private105CopyOriginFactData(destinationPath,
                [.. normalizedOrigins]));
        }

        var normalizedFacts = facts.ToArray();
        Array.Sort(normalizedFacts, static (left, right) =>
            ComparePrivate105CopyOriginPaths(left.DestinationPath,
                right.DestinationPath));
        for (var index = 1; index < normalizedFacts.Length; index++)
        {
            var previous = normalizedFacts[index - 1].DestinationPath;
            var current = normalizedFacts[index].DestinationPath;
            if (ComparePrivate105CopyOriginPaths(previous, current) == 0
                || IsPrivate105CopyOriginPathPrefix(previous, current)
                || IsPrivate105CopyOriginPathPrefix(current, previous))
            {
                throw new ModuleFormatException(
                    "Revision 106 CopyOrigin destinations overlap.");
            }
        }

        return new Private105CopyOriginSummaryData(kind,
            [.. normalizedFacts]);
    }

    private static ImmutableArray<int> ReadPrivate105CopyOriginPath(
        BinaryReader reader)
    {
        var depth = CountRows(reader, ModuleCompiler.MaximumDepth, 4,
            "CopyOrigin field path steps");
        var path = ImmutableArray.CreateBuilder<int>(depth);
        for (var index = 0; index < depth; index++)
        {
            var ordinal = reader.ReadUInt32();
            if (ordinal > int.MaxValue)
            {
                throw new ModuleFormatException(
                    "Revision 106 CopyOrigin field ordinal is invalid.");
            }
            path.Add((int)ordinal);
        }

        return path.MoveToImmutable();
    }

    private static BoundCopyOriginSummary
        RestorePrivate105CopyOriginSummary(
            Private105CopyOriginSummaryData summary,
            BoundFunction function,
            ImmutableArray<BoundStruct> structures,
            ImmutableArray<CheckedReferenceTypeDefinition>
                checkedReferenceTypes)
    {
        var kind = summary.Kind switch {
            1 => BoundCopyOriginSummaryKind.NormalResult,
            2 => BoundCopyOriginSummaryKind.FreshInlineConstruction,
            _ => throw new ModuleFormatException(
                "Revision 106 CopyOrigin summary kind is invalid."),
        };
        var facts = ImmutableArray.CreateBuilder<BoundCopyFact>(
            summary.Facts.Length);
        foreach (var fact in summary.Facts)
        {
            var destinationPath = RestorePrivate105CopyOriginPath(
                function.ReturnType, fact.DestinationPath, structures,
                out var destinationType);
            var origins = ImmutableArray.CreateBuilder<BoundCopyOrigin>(
                fact.Origins.Length);
            foreach (var origin in fact.Origins)
            {
                var sourceKind = origin.SourceKind switch {
                    1 => BoundCopyOriginSourceKind.Formal,
                    2 => BoundCopyOriginSourceKind.Receiver,
                    _ => throw new ModuleFormatException(
                        "Revision 106 CopyOrigin source kind is invalid."),
                };
                var sourceView = origin.SourceView switch {
                    1 => BoundCopyOriginSourceView.EntryValue,
                    2 => BoundCopyOriginSourceView.EntryReferentValue,
                    _ => throw new ModuleFormatException(
                        "Revision 106 CopyOrigin source view is invalid."),
                };
                var sourceRootType = RestorePrivate105CopyOriginSourceType(
                    function, sourceKind, sourceView,
                    origin.SourceOrdinal, checkedReferenceTypes);
                var sourcePath = RestorePrivate105CopyOriginPath(
                    sourceRootType, origin.SourcePath, structures,
                    out var sourceType);
                if (sourceType != destinationType)
                {
                    throw new ModuleFormatException(
                        "Revision 106 CopyOrigin source and destination field types differ.");
                }

                origins.Add(new BoundCopyOrigin(sourceKind, sourceView,
                    origin.SourceOrdinal, sourcePath));
            }

            facts.Add(new BoundCopyFact(destinationPath,
                origins.MoveToImmutable()));
        }

        return new BoundCopyOriginSummary(kind, facts.MoveToImmutable());
    }

    private static BoundCopyOriginFieldPath
        RestorePrivate105CopyOriginPath(PrimitiveType rootType,
            ImmutableArray<int> ordinals,
            ImmutableArray<BoundStruct> structures,
            out PrimitiveType leafType)
    {
        if (ordinals.IsDefault || ordinals.Length > ModuleCompiler.MaximumDepth)
        {
            throw new ModuleFormatException(
                "Revision 106 CopyOrigin path is invalid.");
        }

        var steps = ImmutableArray.CreateBuilder<BoundCopyOriginFieldStep>(
            ordinals.Length);
        var currentType = rootType;
        foreach (var ordinal in ordinals)
        {
            var structureIndex = (int)currentType - 100;
            if (structureIndex < 0
                || (uint)structureIndex >= (uint)structures.Length
                || BoundEnumTypes.IsType(currentType)
                || (uint)ordinal
                    >= (uint)structures[structureIndex].Fields.Length)
            {
                throw new ModuleFormatException(
                    "Revision 106 CopyOrigin path has an invalid field ordinal.");
            }

            steps.Add(new BoundCopyOriginFieldStep(currentType, ordinal));
            currentType = structures[structureIndex].Fields[ordinal].Type;
        }

        leafType = currentType;
        return new BoundCopyOriginFieldPath(steps.MoveToImmutable());
    }

    private static PrimitiveType RestorePrivate105CopyOriginSourceType(
        BoundFunction function, BoundCopyOriginSourceKind sourceKind,
        BoundCopyOriginSourceView sourceView, int sourceOrdinal,
        ImmutableArray<CheckedReferenceTypeDefinition>
            checkedReferenceTypes)
    {
        var hasBorrowedReceiver = function.DeclaringStruct is not null
            && !function.IsStatic && !function.IsConstructor
            && !function.IsClassConstructor;
        if (sourceKind == BoundCopyOriginSourceKind.Receiver)
        {
            if (!hasBorrowedReceiver || sourceOrdinal != 0
                || sourceView
                    != BoundCopyOriginSourceView.EntryReferentValue
                || function.Variables.IsDefaultOrEmpty)
            {
                throw new ModuleFormatException(
                    "Revision 106 CopyOrigin receiver source is inapplicable.");
            }
            return function.Variables[0].Type;
        }
        if (sourceKind != BoundCopyOriginSourceKind.Formal
            || sourceOrdinal < 0
            || sourceOrdinal >= function.ParameterCount
                - (hasBorrowedReceiver ? 1 : 0))
        {
            throw new ModuleFormatException(
                "Revision 106 CopyOrigin formal source is out of range.");
        }

        var variableIndex = sourceOrdinal
            + (function.IsConstructor || hasBorrowedReceiver ? 1 : 0);
        if ((uint)variableIndex >= (uint)function.Variables.Length)
        {
            throw new ModuleFormatException(
                "Revision 106 CopyOrigin source formal is missing.");
        }

        var formal = function.Variables[variableIndex];
        if (formal.IsOut || formal.IsInit)
        {
            throw new ModuleFormatException(
                "Revision 106 CopyOrigin source formal is not an ordinary input.");
        }
        if (sourceView == BoundCopyOriginSourceView.EntryValue)
        {
            if (formal.IsImplicitRef)
            {
                throw new ModuleFormatException(
                    "Revision 106 EntryValue cannot select an implicit-reference formal.");
            }
            return formal.Type;
        }
        if (sourceView
            != BoundCopyOriginSourceView.EntryReferentValue)
        {
            throw new ModuleFormatException(
                "Revision 106 CopyOrigin source view is invalid.");
        }
        if (formal.IsImplicitRef)
        {
            return formal.Type;
        }
        if (!CheckedReferenceTypes.IsType(formal.Type)
            || (uint)CheckedReferenceTypes.Index(formal.Type)
                >= (uint)checkedReferenceTypes.Length)
        {
            throw new ModuleFormatException(
                "Revision 106 EntryReferentValue has no checked-reference type.");
        }

        var reference = checkedReferenceTypes[
            CheckedReferenceTypes.Index(formal.Type)];
        if (reference.IsGarbageCollected || reference.IsInitialization
            || reference.IsOptional)
        {
            throw new ModuleFormatException(
                "Revision 106 EntryReferentValue uses an unsupported checked reference.");
        }
        return reference.ReferentType;
    }

    private static bool IsPrivate105CopyOriginPathPrefix(
        ImmutableArray<int> prefix, ImmutableArray<int> path)
    {
        if (prefix.Length >= path.Length)
        {
            return false;
        }
        for (var index = 0; index < prefix.Length; index++)
        {
            if (prefix[index] != path[index])
            {
                return false;
            }
        }
        return true;
    }
}
