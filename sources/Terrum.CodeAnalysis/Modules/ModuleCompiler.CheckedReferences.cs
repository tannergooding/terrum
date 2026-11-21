// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
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

    private readonly record struct Private106GeneralDependencyRow(
        byte DestinationKind, byte SourceKind, byte Relationship, byte Phase,
        uint DestinationOrdinal, uint SourceOrdinal);

    internal readonly record struct Private106FunctionMetadata(
        ImmutableArray<byte> RawUnionReturnCoverage,
        DirectNativeImport? ExpectedDirectNativeImport,
        Private105CopyOriginSummaryData? CopyOriginSummary,
        ImmutableArray<BoundDependencyContract> GeneralDependencyContracts,
        ImmutableArray<NativeFunctionPointerSelector> CheckedOutputSelectors,
        ImmutableArray<NativeFunctionPointerOutputState> CheckedOutputStates);

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
                FormatVersion: CheckedReferenceVersion or ForeignInterfacePrivateVersion,
            } moduleWriter
            || referenceTypes.IsDefault
            || !referenceTypes.SequenceEqual(
                moduleWriter.CheckedReferenceTypes)
            || referenceTypes.Length > MaximumNodes)
        {
            throw new ModuleContractException(
                "The selected private checked-reference table is invalid.");
        }
        if (!CheckedReferenceTypeContract.IsValidTable(
                referenceTypes, pointerTypes))
        {
            throw new ModuleContractException(
                "The selected private checked-reference table is invalid.");
        }
        var seenTypes = new HashSet<CheckedReferenceTypeDefinition>();
        for (var index = 0; index < referenceTypes.Length; index++)
        {
            var referenceType = referenceTypes[index];
            if (referenceType.IsReferentWritable
                || (!(referenceType.IsOptionalValue
                        && referenceType.ReferentType == PrimitiveType.Void)
                    && !IsValidPrivateReferenceValueType(
                        referenceType.ReferentType, referenceTypes, structures,
                        classes, functionPointerSignatures, pointerTypes,
                        fixedArrayTypes, moduleWriter.EnumDefinitions))
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
            if (referenceType.IsOptionalValue)
            {
                constructorKind = 3;
                qualifiers = 0;
                innerType = referenceType.ReferentType;
            }
            else if (referenceType.IsOptional)
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
                FormatVersion: CheckedReferenceVersion or ForeignInterfacePrivateVersion,
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
            if (reserved != 0
                || (referentType == PrimitiveType.Void
                    && constructorKind != 3))
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
        if (!CheckedReferenceTypeContract.IsValidTable(result, pointerTypes))
        {
            throw new ModuleFormatException(
                "Private checked-reference type table is invalid.");
        }
        for (var index = 0; index < result.Length; index++)
        {
            var referenceType = result[index];
            if (!(referenceType.IsOptionalValue
                    && referenceType.ReferentType == PrimitiveType.Void)
                && !IsValidPrivateReferenceValueType(
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

    private static (Module Module, int[] Mapping)
        CanonicalizePrivateCheckedReferenceTypes(
        Module module)
    {
        var source = module.CheckedReferenceTypes;
        if (source.IsDefaultOrEmpty)
        {
            return (module, []);
        }
        if (!CheckedReferenceTypeContract.IsValidTable(
                source, module.NativePointerTypes))
        {
            throw new ModuleContractException(
                "The module checked-reference type table is invalid.");
        }

        var mapping = new int[source.Length];
        Array.Fill(mapping, -1);
        var states = new byte[source.Length];
        var canonicalTypes = ImmutableArray.CreateBuilder<
            CheckedReferenceTypeDefinition>();
        var canonicalIndexes = new Dictionary<
            CheckedReferenceTypeDefinition, int>();

        var typeRoots = new List<PrimitiveType>();
        typeRoots.AddRange(module.Structures.SelectMany(
            static structure => structure.Fields.Select(
                static field => field.Type)));
        typeRoots.AddRange(module.Classes.SelectMany(
            static @class => @class.Fields.Select(
                static field => field.Type)));
        typeRoots.AddRange(module.NativeFunctionPointerSignatures.SelectMany(
            static signature => signature.ParameterTypes.Prepend(
                signature.ReturnType).Concat(
                signature.NativeParameters.Select(
                    static parameter => parameter.PointeeType))));
        typeRoots.AddRange(module.NativePointerTypes.Select(
            static pointer => pointer.PointeeType));
        typeRoots.AddRange(module.NativeFixedArrayTypes.Select(
            static array => array.ElementType));
        typeRoots.AddRange(module.InvocationBuffers.Select(
            static buffer => buffer.ElementType));
        typeRoots.AddRange(module.NominalTypeOrigins.Select(
            static origin => origin.PrivateType));
        foreach (var item in module.Functions)
        {
            typeRoots.AddRange(item.NativeOutputContracts.Select(
                static output => output.PointeeType));
            if (item.ExpectedDirectNativeImport
                is DirectNativeImport expectedImport)
            {
                typeRoots.AddRange(expectedImport.Outputs.Select(
                    static output => output.PointeeType));
                typeRoots.AddRange(expectedImport.Parameters.Select(
                    static parameter => parameter.PointeeType));
            }
            if (item.ImportedNativeVTableMethod
                is NativeVTableMethodDefinition importedMethod)
            {
                typeRoots.AddRange(importedMethod.Parameters.Select(
                    static parameter => parameter.PointeeType));
                typeRoots.AddRange(importedMethod.Outputs.Select(
                    static output => output.PointeeType));
            }
            if (item.Function is null)
            {
                AddContractReferenceRoots(item.Contract, typeRoots);
            }
        }

        var requiredTypes = new HashSet<string>(StringComparer.Ordinal);
        var requiredReferenceIndexes = new HashSet<int>();
        var referenceRootOrder = new List<int>();
        PopulateRequiredStructures(
            module.Functions.Where(static item =>
                    item.Function is not null)
                .Select(static item => item.Function!),
            module.Structures, module.Classes, null, requiredTypes,
            new Queue<PrimitiveType>(), module.NativePointerTypes,
            module.NativeFunctionPointerSignatures,
            includeAllInteropTypes: false,
            requiredFixedArrayTypes: null,
            requiredCheckedReferenceTypes: requiredReferenceIndexes,
            nativeFixedArrayTypes: module.NativeFixedArrayTypes,
            checkedReferenceTypes: source,
            enumDefinitions: module.EnumDefinitions,
            additionalTypes: typeRoots,
            requiredCheckedReferenceTypeOrder: referenceRootOrder);

        int MapReferenceType(int sourceIndex)
        {
            if ((uint)sourceIndex >= (uint)source.Length)
            {
                throw new ModuleContractException(
                    "The module checked-reference type table has an invalid row reference.");
            }
            if (states[sourceIndex] == 2)
            {
                return mapping[sourceIndex];
            }
            if (states[sourceIndex] == 1)
            {
                throw new ModuleContractException(
                    "The module checked-reference type table contains a cycle.");
            }

            states[sourceIndex] = 1;
            var referenceType = source[sourceIndex];
            if (referenceType.IsOptional && !referenceType.IsOptionalValue)
            {
                var requiredIndex = source.IndexOf(
                    referenceType with { IsOptional = false });
                if (requiredIndex < 0)
                {
                    throw new ModuleContractException(
                        "Optional checked-reference type lacks its required complete reference row.");
                }
                _ = MapReferenceType(requiredIndex);
            }

            var referentType = referenceType.ReferentType;
            if (CheckedReferenceTypes.IsType(referentType))
            {
                referentType = CheckedReferenceTypes.Type(
                    MapReferenceType(
                        CheckedReferenceTypes.Index(referentType)));
            }

            var canonical = referenceType with {
                ReferentType = referentType,
            };
            if (!canonicalIndexes.TryGetValue(canonical,
                out var destinationIndex))
            {
                destinationIndex = canonicalTypes.Count;
                canonicalTypes.Add(canonical);
                canonicalIndexes.Add(canonical, destinationIndex);
            }
            mapping[sourceIndex] = destinationIndex;
            states[sourceIndex] = 2;
            return destinationIndex;
        }

        foreach (var index in referenceRootOrder)
        {
            _ = MapReferenceType(index);
        }
        if (requiredReferenceIndexes.Any(index =>
                (uint)index >= (uint)mapping.Length
                || mapping[index] < 0))
        {
            throw new ModuleContractException(
                "A required checked-reference type has no canonical row.");
        }

        var selectedTypes = canonicalTypes.ToImmutable();
        if (mapping.Select((destination, sourceIndex) =>
                destination == sourceIndex)
            .All(static isIdentity => isIdentity)
            && selectedTypes.SequenceEqual(source))
        {
            return (module, mapping);
        }

        PrimitiveType RemapType(PrimitiveType type)
        {
            return RemapCheckedReferenceType(type, mapping);
        }

        var structures = ImmutableArray.CreateRange(module.Structures.Select(
            structure => BoundTreeTraversal.RewriteTypes(structure,
                RemapType)));
        var classes = ImmutableArray.CreateRange(module.Classes.Select(
            @class => BoundTreeTraversal.RewriteTypes(@class, RemapType)));
        var functionPointerSignatures = ImmutableArray.CreateRange(
            module.NativeFunctionPointerSignatures.Select(signature =>
                signature with {
                    ReturnType = RemapType(signature.ReturnType),
                    ParameterTypes = [.. signature.ParameterTypes.Select(
                        RemapType)],
                    NativeParameters = signature.NativeParameters.IsDefault
                        ? default
                        : [.. signature.NativeParameters.Select(parameter =>
                            parameter with {
                                PointeeType = RemapType(
                                    parameter.PointeeType),
                            })],
                }));
        var pointerTypes = ImmutableArray.CreateRange(
            module.NativePointerTypes.Select(pointerType =>
                pointerType with {
                    PointeeType = RemapType(pointerType.PointeeType),
                }));
        var fixedArrayTypes = ImmutableArray.CreateRange(
            module.NativeFixedArrayTypes.Select(fixedArray =>
                fixedArray with {
                    ElementType = RemapType(fixedArray.ElementType),
                }));
        var functions = ImmutableArray.CreateBuilder<ModuleFunction>(
            module.Functions.Length);
        foreach (var item in module.Functions)
        {
            var function = item.Function is BoundFunction boundFunction
                ? BoundTreeTraversal.RewriteTypes(
                    RemapFunction(boundFunction, module.Structures,
                        module.Structures), RemapType)
                : null;
            var expectedImport
                = item.ExpectedDirectNativeImport is DirectNativeImport import
                    ? import with {
                        Outputs = import.Outputs.IsDefault ? default
                            : [.. import.Outputs.Select(output => output with {
                                PointeeType = RemapType(output.PointeeType),
                            })],
                        Parameters = import.Parameters.IsDefault ? default
                            : [.. import.Parameters.Select(parameter =>
                                parameter with {
                                    PointeeType = RemapType(
                                        parameter.PointeeType),
                                })],
                    }
                    : null;
            var importedVTableMethod
                = item.ImportedNativeVTableMethod
                    is NativeVTableMethodDefinition method
                        ? method with {
                            Parameters = method.Parameters.IsDefault
                                ? default
                                : [.. method.Parameters.Select(parameter =>
                                    parameter with {
                                        PointeeType = RemapType(
                                            parameter.PointeeType),
                                    })],
                            Outputs = method.Outputs.IsDefault ? default
                                : [.. method.Outputs.Select(output =>
                                    output with {
                                        PointeeType = RemapType(
                                            output.PointeeType),
                                    })],
                        }
                        : null;
            functions.Add(item with {
                Function = function,
                Contract = RemapPrivateCheckedReferenceContract(
                    item.Contract, mapping),
                NativeOutputContracts = item.NativeOutputContracts.IsDefault
                    ? default
                    : [.. item.NativeOutputContracts.Select(output =>
                        output with {
                            PointeeType = RemapType(output.PointeeType),
                        })],
                ExpectedDirectNativeImport = expectedImport,
                ImportedNativeVTableMethod = importedVTableMethod,
            });
        }

        var result = module with {
            FormatVersion = CheckedReferenceVersion,
            Structures = structures,
            Classes = classes,
            NativeFunctionPointerSignatures = functionPointerSignatures,
            NativePointerTypes = pointerTypes,
            NativeFixedArrayTypes = fixedArrayTypes,
            CheckedReferenceTypes = selectedTypes,
            InvocationBuffers = [.. module.InvocationBuffers.Select(buffer =>
                buffer with { ElementType = RemapType(buffer.ElementType) })],
            Functions = functions.MoveToImmutable(),
        };
        if (!CheckedReferenceTypeContract.IsValidTable(
                result.CheckedReferenceTypes, result.NativePointerTypes))
        {
            throw new ModuleContractException(
                "Canonical checked-reference type table is invalid.");
        }
        return (result, mapping);
    }

    private static void AddContractReferenceRoots(string contract,
        List<PrimitiveType> roots)
    {
        const string marker = "ctype#";
        var markerIndex = contract.IndexOf(marker, StringComparison.Ordinal);
        while (markerIndex >= 0)
        {
            var digitsStart = markerIndex + marker.Length;
            var digitsEnd = digitsStart;
            while (digitsEnd < contract.Length
                && contract[digitsEnd] is >= '0' and <= '9')
            {
                digitsEnd++;
            }
            if (digitsEnd == digitsStart
                || !int.TryParse(contract.AsSpan(digitsStart,
                    digitsEnd - digitsStart),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var referenceIndex))
            {
                throw new ModuleContractException(
                    "A bodyless private contract has an invalid checked-reference type identity.");
            }

            roots.Add(CheckedReferenceTypes.Type(referenceIndex));
            markerIndex = contract.IndexOf(marker, digitsEnd,
                StringComparison.Ordinal);
        }
    }

    private static string RemapPrivateCheckedReferenceContract(
        string contract, int[] mapping)
    {
        const string marker = "ctype#";
        var markerIndex = contract.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return contract;
        }

        var builder = new StringBuilder(contract.Length);
        var offset = 0;
        while (markerIndex >= 0)
        {
            _ = builder.Append(contract.AsSpan(offset,
                markerIndex - offset));
            var digitsStart = markerIndex + marker.Length;
            var digitsEnd = digitsStart;
            while (digitsEnd < contract.Length
                && contract[digitsEnd] is >= '0' and <= '9')
            {
                digitsEnd++;
            }
            if (digitsEnd == digitsStart
                || !int.TryParse(contract.AsSpan(digitsStart,
                    digitsEnd - digitsStart),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var sourceIndex)
                || (uint)sourceIndex >= (uint)mapping.Length
                || mapping[sourceIndex] < 0)
            {
                throw new ModuleContractException(
                    "A bodyless private contract has an invalid checked-reference type identity.");
            }

            _ = builder.Append(marker);
            _ = builder.Append(mapping[sourceIndex].ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            offset = digitsEnd;
            markerIndex = contract.IndexOf(marker, offset,
                StringComparison.Ordinal);
        }
        _ = builder.Append(contract.AsSpan(offset));
        return builder.ToString();
    }

    private static ImmutableArray<int> MapCheckedReferenceTypeRows(
        Module destination, CompilationReference source)
    {
        if (!CheckedReferenceTypeContract.IsValidTable(
                destination.CheckedReferenceTypes,
                destination.NativePointerTypes)
            || !CheckedReferenceTypeContract.IsValidTable(
                source.CheckedReferenceTypes,
                source.NativePointerTypes))
        {
            throw new ModuleContractException(
                "A provider checked-reference type table is invalid.");
        }

        var sourceEnumMapping = CreatePrivateEnumTypeMapping(
            source.EnumDefinitions, destination.EnumDefinitions);
        var mappings = ImmutableArray.CreateBuilder<int>(
            source.CheckedReferenceTypes.Length);
        for (var sourceIndex = 0;
            sourceIndex < source.CheckedReferenceTypes.Length;
            sourceIndex++)
        {
            var sourceName = TypeName(
                CheckedReferenceTypes.Type(sourceIndex), source.Structures,
                source.NativeFunctionPointerSignatures,
                source.NativePointerTypes, source.NativeFixedArrayTypes,
                source.Classes,
                checkedReferenceTypes: source.CheckedReferenceTypes,
                enumMapping: sourceEnumMapping);
            var destinationIndex = -1;
            for (var candidate = 0;
                candidate < destination.CheckedReferenceTypes.Length;
                candidate++)
            {
                if (sourceName != TypeName(
                        CheckedReferenceTypes.Type(candidate),
                        destination.Structures,
                        destination.NativeFunctionPointerSignatures,
                        destination.NativePointerTypes,
                        destination.NativeFixedArrayTypes,
                        destination.Classes,
                        checkedReferenceTypes:
                            destination.CheckedReferenceTypes))
                {
                    continue;
                }
                if (destinationIndex >= 0)
                {
                    throw new ModuleContractException(
                        "A checked-reference type has multiple structurally equivalent destination rows.");
                }
                destinationIndex = candidate;
            }
            mappings.Add(destinationIndex);
        }

        return mappings.MoveToImmutable();
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
        if (ManagedClassTypes.IsManaged(innerType))
        {
            throw new ModuleFormatException(
                "Private optional rows duplicate an existing managed optional identity.");
        }

        if (!CheckedReferenceTypes.IsType(innerType))
        {
            return new CheckedReferenceTypeDefinition(innerType,
                IsWritable: false, IsGarbageCollected: false,
                IsInitialization: false, IsOptional: true,
                IsOptionalValue: true);
        }

        var innerIndex = CheckedReferenceTypes.Index(innerType);
        if ((uint)innerIndex >= (uint)rows.Length)
        {
            throw new ModuleFormatException(
                "Private optional row names an absent complete inner type.");
        }
        if (rows[innerIndex].ConstructorKind is not (1 or 2))
        {
            throw new ModuleFormatException(
                "Private checked-reference type table contains a nested optional layer.");
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
                FormatVersion: CheckedReferenceVersion or ForeignInterfacePrivateVersion,
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
        var nativeOutputContracts = item.NativeOutputContracts.IsDefault
            ? item.Function?.NativeOutputContracts ?? default
            : item.NativeOutputContracts;
        var generalDependencyContracts = function is BoundFunction dependencyFunction
            ? PrivateNativeOutputDependenciesMatch(dependencyFunction)
                ? GetPrivateGeneralDependencyContracts(dependencyFunction)
                : throw new ModuleContractException(
                    "Private NativeOutput rows differ from their declaration Dependency contracts.")
            : item.GeneralDependencyContracts;
        var generalDependencyRows = CreatePrivate106GeneralDependencyRows(
            generalDependencyContracts, item.Contract,
            nativeOutputContracts);
        var (checkedOutputSelectors, checkedOutputStates) =
            GetPrivateCheckedOutputStateVectors(item.Function,
                item.Contract, item.CheckedOutputSelectors,
                item.CheckedOutputStates,
                item.HasPrivateCheckedOutputStateMetadata);
        if (writer is not ModuleWriter outputStateWriter)
        {
            throw new ModuleContractException(
                "Private checked output-state metadata has no module type context.");
        }
        var checkedOutputOwner = function
            ?? outputStateWriter.SourceProgram?.Functions.FirstOrDefault(
                sourceFunction => sourceFunction.Name == item.Name);
        if (checkedOutputSelectors.Length > MaximumNodes
            || checkedOutputStates.Length > MaximumNodes
            || !PrivateCheckedOutputFormalTypesAreValid(
                checkedOutputOwner, item.Contract,
                outputStateWriter.Structures, outputStateWriter.Classes,
                outputStateWriter.NativeFunctionPointerSignatures,
                outputStateWriter.NativePointerTypes,
                outputStateWriter.NativeFixedArrayTypes,
                outputStateWriter.CheckedReferenceTypes,
                outputStateWriter.SourceProgram)
            || !PrivateCheckedOutputDependenciesAreValid(
                generalDependencyContracts, function, item.Contract,
                nativeOutputContracts, checkedOutputStates))
        {
            throw new ModuleContractException(
                "Private checked output-state vectors or output dependencies are invalid.");
        }
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
                                    importWriter.NativePointerTypes,
                                    importWriter.EnumDefinitions)))
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

        writer.Write(generalDependencyRows.Length);
        foreach (var dependency in generalDependencyRows)
        {
            writer.Write(dependency.DestinationKind);
            writer.Write(dependency.SourceKind);
            writer.Write(dependency.Relationship);
            writer.Write(dependency.Phase);
            writer.Write(dependency.DestinationOrdinal);
            writer.Write(dependency.SourceOrdinal);
        }
        writer.Write(checkedOutputSelectors.Length);
        foreach (var selector in checkedOutputSelectors.OrderBy(
            static selector => selector.PhaseLocalOrdinal))
        {
            writer.Write(selector.PhaseTag);
            writer.Write((byte)0);
            writer.Write((ushort)0);
            writer.Write(selector.PhaseLocalOrdinal);
            writer.Write((uint)selector.NodeKind);
        }
        writer.Write(checkedOutputStates.Length);
        foreach (var outputState in checkedOutputStates.OrderBy(
            static outputState => outputState.FormalOrdinal)
            .ThenBy(static outputState => outputState.SelectorOrdinal))
        {
            writer.Write(outputState.FormalOrdinal);
            writer.Write(outputState.SelectorOrdinal);
            writer.Write(outputState.State);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((byte)0);
        }
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

    private static ImmutableArray<BoundDependencyContract>
        GetPrivateGeneralDependencyContracts(BoundFunction function)
    {
        var dependencies = function.DependencyContracts;
        var outputs = function.NativeOutputContracts;
        if (dependencies.IsDefault || outputs.IsDefault)
        {
            return default;
        }

        var outputFormals = new HashSet<int>();
        foreach (var output in outputs)
        {
            if (output is null || output.FormalOrdinal < 0
                || !outputFormals.Add(output.FormalOrdinal))
            {
                return default;
            }
        }

        var result = ImmutableArray.CreateBuilder<BoundDependencyContract>(
            dependencies.Length);
        foreach (var dependency in dependencies)
        {
            if (dependency is null || dependency.Sources.IsDefault)
            {
                return default;
            }
            if (dependency.Destination
                    == BoundDependencyDestination.InitializedOutput
                && dependency.DestinationOrdinal is int outputOrdinal
                && outputFormals.Contains(outputOrdinal)
                && dependency.Relation is BoundDependencyRelation.DependsOn
                    or BoundDependencyRelation.ForwardsBacking)
            {
                continue;
            }
            result.Add(dependency);
        }
        return result.ToImmutable();
    }

    private static bool HasPrivateGeneralDependencyContracts(Module module)
    {
        foreach (var item in module.Functions)
        {
            var dependencies = item.Function is BoundFunction function
                ? GetPrivateGeneralDependencyContracts(function)
                : item.GeneralDependencyContracts;
            if (dependencies.IsDefault || !dependencies.IsEmpty)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasPrivateCheckedOutputStateVectors(Module module)
    {
        foreach (var item in module.Functions)
        {
            if (item.HasPrivateCheckedOutputStateMetadata
                || item.Function?.HasPrivateCheckedOutputStateMetadata == true
                || (item.Function is BoundFunction function
                    && (!function.CheckedOutputSelectors.IsEmpty
                        || !function.CheckedOutputStates.IsEmpty)))
            {
                return true;
            }
        }
        return false;
    }

    private static (ImmutableArray<NativeFunctionPointerSelector> Selectors,
        ImmutableArray<NativeFunctionPointerOutputState> OutputStates)
        GetPrivateCheckedOutputStateVectors(
        BoundFunction? function, string contract,
        ImmutableArray<NativeFunctionPointerSelector> selectors,
        ImmutableArray<NativeFunctionPointerOutputState> outputStates,
        bool hasExplicitVectors = false)
    {
        if (function is not null
            && (function.HasPrivateCheckedOutputStateMetadata
                || !function.CheckedOutputSelectors.IsEmpty
                || !function.CheckedOutputStates.IsEmpty))
        {
            selectors = function.CheckedOutputSelectors;
            outputStates = function.CheckedOutputStates;
            hasExplicitVectors |=
                function.HasPrivateCheckedOutputStateMetadata;
        }
        if (selectors.IsDefault || outputStates.IsDefault)
        {
            throw new ModuleContractException(
                "Private checked output-state vectors are uninitialized.");
        }

        var parameterCount = PrivateDependencySignature(contract).ParameterCount;
        var outputFormals = GetPrivateCheckedOutputFormals(function, contract);

        if (!selectors.IsEmpty || !outputStates.IsEmpty)
        {
            if (!ValidatePrivateCheckedOutputStateVectors(contract,
                    outputFormals, selectors, outputStates,
                    checked((uint)parameterCount)))
            {
                throw new ModuleContractException(
                    "Private checked output-state vectors contradict their declaration.");
            }
            return (selectors, outputStates);
        }

        if (hasExplicitVectors)
        {
            if (!ValidatePrivateCheckedOutputStateVectors(contract,
                    outputFormals, selectors, outputStates,
                    checked((uint)parameterCount)))
            {
                throw new ModuleContractException(
                    "Private empty checked output-state vectors contradict their declaration.");
            }
            return (selectors, outputStates);
        }

        var returnType = function?.ReturnType
            ?? PrivateDependencyReturnType(contract);
        if (returnType != PrimitiveType.Bool || outputFormals.IsEmpty)
        {
            return ([], []);
        }

        var defaultSelectors = ImmutableArray.Create(
            new NativeFunctionPointerSelector(3, 0, 1),
            new NativeFunctionPointerSelector(3, 1, 2));
        var defaultOutputStates
            = ImmutableArray.CreateBuilder<
                NativeFunctionPointerOutputState>(
                    checked(outputFormals.Length * 2));
        foreach (var formalOrdinal in outputFormals)
        {
            defaultOutputStates.Add(
                new NativeFunctionPointerOutputState(formalOrdinal, 0, 1));
            defaultOutputStates.Add(
                new NativeFunctionPointerOutputState(formalOrdinal, 1, 0));
        }
        var normalizedOutputStates = defaultOutputStates.MoveToImmutable();
        if (!ValidatePrivateCheckedOutputStateVectors(contract,
                outputFormals, defaultSelectors, normalizedOutputStates,
                checked((uint)parameterCount)))
        {
            throw new ModuleContractException(
                "Private Boolean checked output defaults are invalid.");
        }
        return (defaultSelectors, normalizedOutputStates);
    }

    private static ImmutableArray<uint> GetPrivateCheckedOutputFormals(
        BoundFunction? function, string contract)
    {
        var (parameterCount, hasReceiverSource,
            hasReceiverDestination) = PrivateDependencySignature(contract);
        var outputFormals = ImmutableArray.CreateBuilder<uint>();
        if (function is not null)
        {
            var parameterOffset = hasReceiverDestination ? 1 : 0;
            if (parameterOffset + parameterCount > function.Variables.Length)
            {
                throw new ModuleContractException(
                    "Private checked output-state owner has an invalid parameter layout.");
            }
            for (var formalOrdinal = 0;
                formalOrdinal < parameterCount; formalOrdinal++)
            {
                if (function.Variables[formalOrdinal + parameterOffset].IsInit)
                {
                    outputFormals.Add(checked((uint)formalOrdinal));
                }
            }
        }
        else
        {
            var parts = contract.Split('|');
            var contractParameterOffset = hasReceiverSource ? 1 : 0;
            for (var formalOrdinal = 0;
                formalOrdinal < parameterCount; formalOrdinal++)
            {
                var partIndex = checked(8 + contractParameterOffset
                    + formalOrdinal);
                if (partIndex >= parts.Length)
                {
                    throw new ModuleFormatException(
                        "Private checked output-state contract is missing a formal.");
                }
                if (parts[partIndex].StartsWith("init:",
                    StringComparison.Ordinal))
                {
                    outputFormals.Add(checked((uint)formalOrdinal));
                }
            }
        }
        return outputFormals.ToImmutable();
    }

    private static bool PrivateCheckedOutputFormalTypesAreValid(
        BoundFunction? function, string contract,
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<BoundClass> classes,
        ImmutableArray<NativeFunctionPointerSignatureDefinition>
            functionPointerSignatures,
        ImmutableArray<NativePointerTypeDefinition> nativePointerTypes,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrayTypes,
        ImmutableArray<CheckedReferenceTypeDefinition>
            checkedReferenceTypes,
        BoundProgram? program = null)
    {
        var (parameterCount, hasReceiverSource,
            hasReceiverDestination) = PrivateDependencySignature(contract);
        var outputFormals = GetPrivateCheckedOutputFormals(function, contract);
        if (function is not null)
        {
            var parameterOffset = hasReceiverDestination ? 1 : 0;
            if (parameterOffset + parameterCount > function.Variables.Length)
            {
                return false;
            }
            foreach (var formalOrdinal in outputFormals)
            {
                var type = function.Variables[
                    checked((int)formalOrdinal) + parameterOffset].Type;
                if (!IsValidPrivateCheckedOutputFormalType(type, structures,
                        classes, checkedReferenceTypes, nativePointerTypes,
                        program))
                {
                    return false;
                }
            }
            return true;
        }

        var parts = contract.Split('|');
        var contractParameterOffset = hasReceiverSource ? 1 : 0;
        foreach (var formalOrdinal in outputFormals)
        {
            var partIndex = checked(8 + contractParameterOffset
                + checked((int)formalOrdinal));
            if (partIndex >= parts.Length
                || !parts[partIndex].StartsWith("init:",
                    StringComparison.Ordinal)
                || !TryParsePrivateCheckedOutputFormalType(
                    parts[partIndex].AsSpan("init:".Length), structures,
                    classes, functionPointerSignatures, nativePointerTypes,
                    fixedArrayTypes, checkedReferenceTypes, program,
                    out var type)
                || !IsValidPrivateCheckedOutputFormalType(type, structures,
                    classes, checkedReferenceTypes, nativePointerTypes,
                    program))
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryParsePrivateCheckedOutputFormalType(
        ReadOnlySpan<char> typeName, ImmutableArray<BoundStruct> structures,
        ImmutableArray<BoundClass> classes,
        ImmutableArray<NativeFunctionPointerSignatureDefinition>
            functionPointerSignatures,
        ImmutableArray<NativePointerTypeDefinition> nativePointerTypes,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrayTypes,
        ImmutableArray<CheckedReferenceTypeDefinition>
            checkedReferenceTypes,
        BoundProgram? program, out PrimitiveType type)
    {
        if (int.TryParse(typeName,
                System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture,
                out var typeValue))
        {
            type = (PrimitiveType)typeValue;
            return IsValidPrivateCheckedOutputFormalType(type, structures,
                classes, checkedReferenceTypes, nativePointerTypes, program);
        }
        if (typeName.StartsWith("ctype#".AsSpan(),
                StringComparison.Ordinal)
            && int.TryParse(typeName["ctype#".Length..],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var checkedReferenceIndex)
            && (uint)checkedReferenceIndex
                < (uint)checkedReferenceTypes.Length)
        {
            type = CheckedReferenceTypes.Type(checkedReferenceIndex);
            return IsValidPrivateCheckedOutputFormalType(type, structures,
                classes, checkedReferenceTypes, nativePointerTypes, program);
        }

        for (var index = 0; index < structures.Length; index++)
        {
            var candidate = (PrimitiveType)(100 + index);
            if (typeName.SequenceEqual(ContractType(candidate, structures,
                    functionPointerSignatures, nativePointerTypes,
                    fixedArrayTypes, private105: true).AsSpan())
                && IsValidPrivateCheckedOutputFormalType(candidate,
                    structures, classes, checkedReferenceTypes,
                    nativePointerTypes, program))
            {
                type = candidate;
                return true;
            }
        }
        type = default;
        return false;
    }

    private static bool IsValidPrivateCheckedOutputFormalType(
        PrimitiveType type, ImmutableArray<BoundStruct> structures,
        ImmutableArray<BoundClass> classes,
        ImmutableArray<CheckedReferenceTypeDefinition>
            checkedReferenceTypes,
        ImmutableArray<NativePointerTypeDefinition> nativePointerTypes,
        BoundProgram? program)
    {
        if (!CheckedReferenceTypeContract.IsValidType(type,
                checkedReferenceTypes))
        {
            return false;
        }

        var reference = checkedReferenceTypes[
            CheckedReferenceTypes.Index(type)];
        return reference.IsInitialization
            && !reference.IsGarbageCollected
            && !reference.IsOptional
            && IsValidPrivateCheckedOutputReferentType(
                reference.ReferentType, structures, classes,
                nativePointerTypes, program);
    }

    private static bool IsValidPrivateCheckedOutputReferentType(
        PrimitiveType type, ImmutableArray<BoundStruct> structures,
        ImmutableArray<BoundClass> classes,
        ImmutableArray<NativePointerTypeDefinition> nativePointerTypes,
        BoundProgram? program) =>
        type is PrimitiveType.Bool or PrimitiveType.Char16
            or PrimitiveType.Int32 or PrimitiveType.UInt32
            or PrimitiveType.Int64 or PrimitiveType.UInt64
            or PrimitiveType.NativeInt or PrimitiveType.NativeUInt
            or PrimitiveType.Float32 or PrimitiveType.Float64
            or PrimitiveType.OwnedInt32Buffer
            or PrimitiveType.ManagedNode
            or PrimitiveType.WriteManagedNode
            || ((int)type >= 100
                && (int)type - 100 < structures.Length)
            || (ManagedClassTypes.IsManaged(type)
                && (uint)ManagedClassTypes.ClassIndex(type)
                    < (uint)classes.Length)
            || (NativePointerTypes.IsType(type)
                && (uint)NativePointerTypes.Index(type)
                    < (uint)nativePointerTypes.Length)
            || (program is not null
                && BoundGenericNominalTypeContract.IsCoreTaskType(type,
                    program.GenericNominalTypeInstances,
                    program.CoreTaskType));

    private static PrimitiveType PrivateDependencyReturnType(string contract)
    {
        var parts = contract.Split('|');
        return parts.Length > 1
            && int.TryParse(parts[1],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var typeValue)
            ? (PrimitiveType)typeValue
            : PrimitiveType.Void;
    }

    private static bool ValidatePrivateCheckedOutputStateVectors(
        string contract, ImmutableArray<uint> outputFormals,
        ImmutableArray<NativeFunctionPointerSelector> selectors,
        ImmutableArray<NativeFunctionPointerOutputState> outputStates,
        uint maximumFormalCount = uint.MaxValue,
        bool requireDeclaredOutputFormals = true)
    {
        if (selectors.IsDefault || outputStates.IsDefault)
        {
            return false;
        }
        var returnType = PrivateDependencyReturnType(contract);
        var nodeKinds = returnType switch
        {
            PrimitiveType.Bool => new HashSet<byte>([1, 2]),
            PrimitiveType.Int32 => new HashSet<byte>([3, 4, 5, 6]),
            PrimitiveType.UInt32 => new HashSet<byte>([3, 4]),
            _ => new HashSet<byte>(),
        };
        var selectorOrdinals = new HashSet<uint>();
        foreach (var selector in selectors)
        {
            if (selector.PhaseTag != 3
                || !nodeKinds.Contains(selector.NodeKind)
                || !selectorOrdinals.Add(selector.PhaseLocalOrdinal))
            {
                return false;
            }
        }
        if (selectorOrdinals.Any(ordinal => ordinal >= (uint)selectors.Length))
        {
            return false;
        }
        for (var ordinal = 0; ordinal < selectors.Length; ordinal++)
        {
            if (!selectorOrdinals.Contains(checked((uint)ordinal)))
            {
                return false;
            }
        }

        var outputFormalSet = outputFormals.ToHashSet();
        var rowsByFormal = new Dictionary<uint,
            List<NativeFunctionPointerOutputState>>();
        var occurrenceKeys = new HashSet<(uint FormalOrdinal,
            uint SelectorOrdinal)>();
        foreach (var outputState in outputStates)
        {
            if (outputState.FormalOrdinal >= maximumFormalCount
                || (requireDeclaredOutputFormals
                    && !outputFormalSet.Contains(outputState.FormalOrdinal))
                || outputState.State is not (0 or 1)
                || !selectorOrdinals.Contains(outputState.SelectorOrdinal)
                || !occurrenceKeys.Add((outputState.FormalOrdinal,
                    outputState.SelectorOrdinal)))
            {
                return false;
            }
            if (!rowsByFormal.TryGetValue(outputState.FormalOrdinal,
                    out var rows))
            {
                rows = [];
                rowsByFormal.Add(outputState.FormalOrdinal, rows);
            }
            rows.Add(outputState);
        }

        foreach (var rows in rowsByFormal.Values)
        {
            if (rows.Count != 2 || rows[0].State == rows[1].State)
            {
                return false;
            }

            var firstSelector = selectors.Single(selector =>
                selector.PhaseLocalOrdinal == rows[0].SelectorOrdinal);
            var secondSelector = selectors.Single(selector =>
                selector.PhaseLocalOrdinal == rows[1].SelectorOrdinal);
            var pair = (firstSelector.NodeKind, rows[0].State,
                secondSelector.NodeKind, rows[1].State);
            var validPair = returnType switch
            {
                PrimitiveType.Bool => pair is (1, 1, 2, 0)
                    or (2, 0, 1, 1),
                PrimitiveType.Int32 => pair is (3, 1, 4, 0)
                    or (4, 0, 3, 1) or (5, 1, 6, 0)
                    or (6, 0, 5, 1),
                PrimitiveType.UInt32 => pair is (3, 1, 4, 0)
                    or (4, 0, 3, 1),
                _ => false,
            };
            if (!validPair)
            {
                return false;
            }
        }

        return outputStates.IsEmpty || !selectors.IsEmpty;
    }

    private static bool PrivateCheckedOutputDependenciesAreValid(
        ImmutableArray<BoundDependencyContract> dependencies,
        BoundFunction? function, string contract,
        ImmutableArray<NativeOutputContract> nativeOutputContracts,
        ImmutableArray<NativeFunctionPointerOutputState> outputStates)
    {
        if (dependencies.IsDefault || nativeOutputContracts.IsDefault
            || outputStates.IsDefault)
        {
            return false;
        }

        var (parameterCount, _, _) = PrivateDependencySignature(contract);
        var initFormals = GetPrivateCheckedOutputFormals(function, contract)
            .ToHashSet();
        var nativeOutputFormals = new HashSet<int>();
        foreach (var output in nativeOutputContracts)
        {
            if (output is null || output.FormalOrdinal < 0
                || output.FormalOrdinal >= parameterCount
                || !nativeOutputFormals.Add(output.FormalOrdinal))
            {
                return false;
            }
        }
        var statesByFormal = outputStates
            .GroupBy(static state => state.FormalOrdinal)
            .ToDictionary(static group => group.Key,
                static group => group.ToArray());
        foreach (var dependency in dependencies)
        {
            if (dependency is null || dependency.Sources.IsDefault)
            {
                return false;
            }
            if (dependency.Destination
                    != BoundDependencyDestination.InitializedOutput)
            {
                continue;
            }
            if (dependency.DestinationOrdinal is not int formalOrdinal
                || (uint)formalOrdinal >= (uint)parameterCount
                || nativeOutputFormals.Contains(formalOrdinal)
                || !initFormals.Contains(checked((uint)formalOrdinal)))
            {
                return false;
            }
            if (dependency.Relation
                    == BoundDependencyRelation.Captures
                && statesByFormal.TryGetValue(
                    checked((uint)formalOrdinal), out var formalStates)
                && !formalStates.Any(static state => state.State == 1))
            {
                return false;
            }
        }
        return true;
    }

    private static bool PrivateCheckedOutputStateVectorsMatch(
        ImmutableArray<NativeFunctionPointerSelector> expectedSelectors,
        ImmutableArray<NativeFunctionPointerOutputState> expectedOutputStates,
        ImmutableArray<NativeFunctionPointerSelector> actualSelectors,
        ImmutableArray<NativeFunctionPointerOutputState> actualOutputStates)
        => !expectedSelectors.IsDefault && !expectedOutputStates.IsDefault
            && !actualSelectors.IsDefault && !actualOutputStates.IsDefault
            && expectedSelectors.OrderBy(
                static selector => selector.PhaseLocalOrdinal)
                .SequenceEqual(actualSelectors.OrderBy(
                    static selector => selector.PhaseLocalOrdinal))
            && expectedOutputStates.OrderBy(
                static outputState => outputState.FormalOrdinal)
                .ThenBy(static outputState => outputState.SelectorOrdinal)
                .SequenceEqual(actualOutputStates.OrderBy(
                    static outputState => outputState.FormalOrdinal)
                    .ThenBy(static outputState =>
                        outputState.SelectorOrdinal));

    private static bool PrivateCheckedOutputStateVectorsMatch(
        BoundFunction expectedFunction, string expectedContract,
        BoundFunction actualFunction, string actualContract)
    {
        var (expectedSelectors, expectedOutputStates) =
            GetPrivateCheckedOutputStateVectors(expectedFunction,
                expectedContract, expectedFunction.CheckedOutputSelectors,
                expectedFunction.CheckedOutputStates);
        var (actualSelectors, actualOutputStates) =
            GetPrivateCheckedOutputStateVectors(actualFunction,
                actualContract, actualFunction.CheckedOutputSelectors,
                actualFunction.CheckedOutputStates);
        return PrivateCheckedOutputStateVectorsMatch(expectedSelectors,
            expectedOutputStates, actualSelectors, actualOutputStates);
    }

    private static bool PrivateNativeOutputDependenciesMatch(
        BoundFunction function)
    {
        var dependencies = function.DependencyContracts;
        var outputs = function.NativeOutputContracts;
        if (dependencies.IsDefault || outputs.IsDefault)
        {
            return false;
        }

        foreach (var output in outputs)
        {
            if (output is null || output.Dependencies.IsDefault)
            {
                return false;
            }
            var projected = NativeOutputContractValidator.CreateDependencies(
                dependencies, output.FormalOrdinal);
            if (projected.IsDefault
                || !projected.SequenceEqual(output.Dependencies))
            {
                return false;
            }
        }
        return true;
    }

    private static ImmutableArray<Private106GeneralDependencyRow>
        CreatePrivate106GeneralDependencyRows(
        ImmutableArray<BoundDependencyContract> dependencies,
        string contract,
        ImmutableArray<NativeOutputContract> nativeOutputContracts)
    {
        if (dependencies.IsDefault || nativeOutputContracts.IsDefault)
        {
            throw new ModuleContractException(
                "Private general Dependency contracts are uninitialized.");
        }

        var (parameterCount, hasReceiverSource,
            hasReceiverDestination) = PrivateDependencySignature(contract);
        var maximumRows = PrivateGeneralDependencyRowCountLimit(
            parameterCount, hasReceiverSource);

        var outputFormals = new HashSet<int>();
        foreach (var output in nativeOutputContracts)
        {
            if (output is null || output.FormalOrdinal < 0
                || output.FormalOrdinal >= parameterCount
                || !outputFormals.Add(output.FormalOrdinal))
            {
                throw new ModuleContractException(
                    "Private general Dependency NativeOutput references are invalid.");
            }
        }

        foreach (var dependency in dependencies)
        {
            if (dependency is null || dependency.Sources.IsDefault
                || dependency.Relation is not (
                    BoundDependencyRelation.DependsOn
                    or BoundDependencyRelation.Captures
                    or BoundDependencyRelation.ForwardsBacking
                    or BoundDependencyRelation.BorrowSourceStorage))
            {
                throw new ModuleContractException(
                    "Private general Dependency contract is uninitialized.");
            }
        }

        var rows = new List<Private106GeneralDependencyRow>();
        var refinementRows = new List<Private106GeneralDependencyRow>();
        foreach (var dependency in dependencies)
        {
            var (destinationKind, destinationOrdinal) =
                PrivateGeneralDependencyDestination(dependency,
                    parameterCount, hasReceiverDestination);
            if (dependency.Relation is BoundDependencyRelation.DependsOn
                or BoundDependencyRelation.Captures)
            {
                var phase = PrivateGeneralDependencyPhase(
                    dependency.Relation, destinationKind);
                if (destinationKind == 2 && phase == 1
                    && outputFormals.Contains(
                        checked((int)destinationOrdinal)))
                {
                    throw new ModuleContractException(
                        "Private NativeOutput phase-1 dependencies must remain in their output contract.");
                }

                if (dependency.Sources.IsEmpty)
                {
                    rows.Add(new Private106GeneralDependencyRow(
                        destinationKind, 0, 0, phase, destinationOrdinal, 0));
                    continue;
                }

                foreach (var source in dependency.Sources)
                {
                    var (sourceKind, sourceOrdinal) =
                        PrivateGeneralDependencySource(source,
                            parameterCount, hasReceiverSource);
                    rows.Add(new Private106GeneralDependencyRow(
                        destinationKind, sourceKind, 3, phase,
                        destinationOrdinal, sourceOrdinal));
                }
                continue;
            }

            if (dependency.Sources.IsEmpty)
            {
                throw new ModuleContractException(
                    "Private Dependency refinements require at least one source.");
            }

            var relationshipCode = dependency.Relation
                == BoundDependencyRelation.ForwardsBacking ? (byte)2 : (byte)1;
            foreach (var source in dependency.Sources)
            {
                var (sourceKind, sourceOrdinal) =
                    PrivateGeneralDependencySource(source,
                        parameterCount, hasReceiverSource);
                var hasBaseRelationship = false;
                foreach (var relationship in dependencies)
                {
                    if (relationship.Relation is not (
                            BoundDependencyRelation.DependsOn
                            or BoundDependencyRelation.Captures)
                        || relationship.Sources.IsDefault
                        || relationship.Destination != dependency.Destination
                        || relationship.DestinationOrdinal
                            != dependency.DestinationOrdinal
                        || !relationship.Sources.Contains(source))
                    {
                        continue;
                    }

                    var (relationshipDestinationKind,
                        relationshipDestinationOrdinal)
                        = PrivateGeneralDependencyDestination(relationship,
                            parameterCount, hasReceiverDestination);
                    var phase = PrivateGeneralDependencyPhase(
                        relationship.Relation, relationshipDestinationKind);
                    if (relationshipDestinationKind != destinationKind
                        || relationshipDestinationOrdinal != destinationOrdinal)
                    {
                        continue;
                    }
                    if (destinationKind == 2 && phase == 1
                        && outputFormals.Contains(
                            checked((int)destinationOrdinal)))
                    {
                        throw new ModuleContractException(
                            "Private NativeOutput phase-1 dependencies must remain in their output contract.");
                    }

                    refinementRows.Add(new Private106GeneralDependencyRow(
                        destinationKind, sourceKind, relationshipCode, phase,
                        destinationOrdinal, sourceOrdinal));
                    hasBaseRelationship = true;
                }
                if (!hasBaseRelationship)
                {
                    throw new ModuleContractException(
                        "Private Dependency refinement does not refine a declared source.");
                }
            }
        }

        foreach (var refinement in refinementRows)
        {
            var match = rows.FindIndex(row =>
                row.DestinationKind == refinement.DestinationKind
                && row.DestinationOrdinal == refinement.DestinationOrdinal
                && row.Phase == refinement.Phase
                && row.SourceKind == refinement.SourceKind
                && row.SourceOrdinal == refinement.SourceOrdinal
                && row.Relationship == 3);
            if (match < 0)
            {
                throw new ModuleContractException(
                    "Private Dependency refinements are conflicting or do not refine a declared source.");
            }
            rows[match] = rows[match] with {
                Relationship = refinement.Relationship,
            };
        }

        rows.Sort(static (left, right) =>
        {
            var comparison = left.DestinationKind.CompareTo(
                right.DestinationKind);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = left.DestinationOrdinal.CompareTo(
                right.DestinationOrdinal);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = left.Phase.CompareTo(right.Phase);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = left.SourceKind.CompareTo(right.SourceKind);
            return comparison != 0
                ? comparison
                : left.SourceOrdinal.CompareTo(right.SourceOrdinal);
        });

        var previous = default(Private106GeneralDependencyRow);
        var hasPrevious = false;
        foreach (var row in rows)
        {
            if (hasPrevious
                && previous.DestinationKind == row.DestinationKind
                && previous.DestinationOrdinal == row.DestinationOrdinal
                && previous.Phase == row.Phase
                && previous.SourceKind == row.SourceKind
                && previous.SourceOrdinal == row.SourceOrdinal)
            {
                throw new ModuleContractException(
                    "Private general Dependency rows contain a duplicate occurrence key.");
            }
            previous = row;
            hasPrevious = true;
        }
        for (var start = 0; start < rows.Count;)
        {
            var first = rows[start];
            var end = start + 1;
            while (end < rows.Count
                && rows[end].DestinationKind == first.DestinationKind
                && rows[end].DestinationOrdinal == first.DestinationOrdinal
                && rows[end].Phase == first.Phase)
            {
                end++;
            }
            if (first.SourceKind == 0 && end - start != 1)
            {
                throw new ModuleContractException(
                    "Private empty Dependency source coexists with nonempty sources.");
            }
            start = end;
        }
        if (rows.Count > maximumRows)
        {
            throw new ModuleContractException(
                "Private general Dependency rows exceed the declaration bound.");
        }
        return [.. rows];
    }

    private static (byte Kind, uint Ordinal)
        PrivateGeneralDependencyDestination(
        BoundDependencyContract dependency, int parameterCount,
        bool hasReceiverDestination)
        => dependency.Destination switch
        {
            BoundDependencyDestination.Result
                when dependency.DestinationOrdinal is null or 0
                => (1, 0),
            BoundDependencyDestination.InitializedOutput
                when dependency.DestinationOrdinal is int outputOrdinal
                    && (uint)outputOrdinal < (uint)parameterCount
                => (2, checked((uint)outputOrdinal)),
            BoundDependencyDestination.RetainedFormal
                when dependency.DestinationOrdinal is int formalOrdinal
                    && (uint)formalOrdinal < (uint)parameterCount
                => (3, checked((uint)formalOrdinal)),
            BoundDependencyDestination.Receiver
                when hasReceiverDestination
                    && dependency.DestinationOrdinal is null or 0
                => (4, 0),
            _ => throw new ModuleContractException(
                "Private general Dependency destination is not representable."),
        };

    private static byte PrivateGeneralDependencyPhase(
        BoundDependencyRelation relation, byte destinationKind)
        => (relation, destinationKind) switch
        {
            (BoundDependencyRelation.DependsOn, 1 or 2) => 1,
            (BoundDependencyRelation.Captures, 1 or 2 or 3 or 4) => 2,
            _ => throw new ModuleContractException(
                "Private general Dependency destination/phase pair is not assigned."),
        };

    private static (byte Kind, uint Ordinal) PrivateGeneralDependencySource(
        BoundDependencySource source, int parameterCount,
        bool hasReceiverSource)
        => source.Kind switch
        {
            BoundDependencySourceKind.Formal
                when source.FormalOrdinal is int formalOrdinal
                    && (uint)formalOrdinal < (uint)parameterCount
                => (1, checked((uint)formalOrdinal)),
            BoundDependencySourceKind.Receiver
                when hasReceiverSource && source.FormalOrdinal is null
                => (2, 0),
            _ => throw new ModuleContractException(
                "Private general Dependency source is not representable."),
        };

    private static ImmutableArray<BoundDependencyContract>
        ReadPrivate106GeneralDependencyContracts(
        BinaryReader reader, string contract,
        ImmutableArray<NativeOutputContract> nativeOutputContracts)
    {
        if (nativeOutputContracts.IsDefault)
        {
            throw new ModuleFormatException(
                "Private general Dependency rows have no output-contract collection.");
        }

        var (parameterCount, hasReceiverSource,
            hasReceiverDestination) = PrivateDependencySignature(contract);
        var maximumRows = PrivateGeneralDependencyRowCountLimit(
            parameterCount, hasReceiverSource);
        var rowCount = CountRows(reader, maximumRows, 12,
            "general Dependency rows");
        var outputFormals = new HashSet<int>();
        foreach (var output in nativeOutputContracts)
        {
            if (output is null || output.FormalOrdinal < 0
                || output.FormalOrdinal >= parameterCount
                || !outputFormals.Add(output.FormalOrdinal))
            {
                throw new ModuleFormatException(
                    "Private general Dependency NativeOutput references are invalid.");
            }
        }

        var rows = new List<Private106GeneralDependencyRow>(rowCount);
        var occurrenceKeys = new HashSet<(
            byte DestinationKind, uint DestinationOrdinal, byte Phase,
            byte SourceKind, uint SourceOrdinal)>();
        for (var index = 0; index < rowCount; index++)
        {
            var row = new Private106GeneralDependencyRow(
                reader.ReadByte(), reader.ReadByte(), reader.ReadByte(),
                reader.ReadByte(), reader.ReadUInt32(), reader.ReadUInt32());
            if (!IsValidPrivateGeneralDependencyRow(row, parameterCount,
                    hasReceiverSource, hasReceiverDestination)
                || (row.DestinationKind == 2 && row.Phase == 1
                    && outputFormals.Contains(
                        checked((int)row.DestinationOrdinal)))
                || !occurrenceKeys.Add((row.DestinationKind,
                    row.DestinationOrdinal, row.Phase, row.SourceKind,
                    row.SourceOrdinal)))
            {
                throw new ModuleFormatException(
                    "Private general Dependency row is malformed or duplicates an occurrence key.");
            }
            rows.Add(row);
        }

        rows.Sort(static (left, right) =>
        {
            var comparison = left.DestinationKind.CompareTo(
                right.DestinationKind);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = left.DestinationOrdinal.CompareTo(
                right.DestinationOrdinal);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = left.Phase.CompareTo(right.Phase);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = left.SourceKind.CompareTo(right.SourceKind);
            return comparison != 0
                ? comparison
                : left.SourceOrdinal.CompareTo(right.SourceOrdinal);
        });

        var result = ImmutableArray.CreateBuilder<BoundDependencyContract>();
        var offset = 0;
        while (offset < rows.Count)
        {
            var first = rows[offset];
            var end = offset + 1;
            while (end < rows.Count
                && rows[end].DestinationKind == first.DestinationKind
                && rows[end].DestinationOrdinal == first.DestinationOrdinal
                && rows[end].Phase == first.Phase)
            {
                end++;
            }

            var emptySource = first.SourceKind == 0;
            if (emptySource && end - offset != 1)
            {
                throw new ModuleFormatException(
                    "Private empty Dependency source coexists with nonempty sources.");
            }

            var sources = ImmutableArray.CreateBuilder<BoundDependencySource>(
                emptySource ? 0 : end - offset);
            var forwardedSources
                = ImmutableArray.CreateBuilder<BoundDependencySource>();
            var storageSources
                = ImmutableArray.CreateBuilder<BoundDependencySource>();
            for (var index = offset; index < end; index++)
            {
                var row = rows[index];
                if (row.SourceKind == 0)
                {
                    continue;
                }
                var source = row.SourceKind switch
                {
                    1 => new BoundDependencySource(
                        BoundDependencySourceKind.Formal,
                        checked((int)row.SourceOrdinal)),
                    2 => new BoundDependencySource(
                        BoundDependencySourceKind.Receiver, null),
                    _ => throw new ModuleFormatException(
                        "Private Dependency row has an unknown source kind."),
                };
                sources.Add(source);
                if (row.Relationship == 2)
                {
                    forwardedSources.Add(source);
                }
                else if (row.Relationship == 1)
                {
                    storageSources.Add(source);
                }
            }

            var (destination, destinationOrdinal) =
                PrivateGeneralDependencyDestination(first);
            var relation = first.Phase switch
            {
                1 => BoundDependencyRelation.DependsOn,
                2 => BoundDependencyRelation.Captures,
                _ => throw new ModuleFormatException(
                    "Private general Dependency phase is not supported."),
            };
            result.Add(new BoundDependencyContract(relation, destination,
                destinationOrdinal, sources.MoveToImmutable()));
            if (forwardedSources.Count != 0)
            {
                result.Add(new BoundDependencyContract(
                    BoundDependencyRelation.ForwardsBacking, destination,
                    destinationOrdinal, forwardedSources.ToImmutable()));
            }
            if (storageSources.Count != 0)
            {
                result.Add(new BoundDependencyContract(
                    BoundDependencyRelation.BorrowSourceStorage, destination,
                    destinationOrdinal, storageSources.ToImmutable()));
            }
            offset = end;
        }

        return result.ToImmutable();
    }

    internal static bool PrivateGeneralDependencyContractsSatisfy(
        ImmutableArray<BoundDependencyContract> expected,
        ImmutableArray<BoundDependencyContract> actual,
        string contract)
    {
        if (expected.IsDefault || actual.IsDefault)
        {
            return false;
        }

        ImmutableArray<Private106GeneralDependencyRow> expectedRows;
        ImmutableArray<Private106GeneralDependencyRow> actualRows;
        try
        {
            expectedRows = CreatePrivate106GeneralDependencyRows(expected,
                contract, []);
            actualRows = CreatePrivate106GeneralDependencyRows(actual,
                contract, []);
        }
        catch (ModuleContractException)
        {
            return false;
        }

        var expectedOffset = 0;
        var actualOffset = 0;
        while (expectedOffset < expectedRows.Length
            && actualOffset < actualRows.Length)
        {
            var expectedFirst = expectedRows[expectedOffset];
            var actualFirst = actualRows[actualOffset];
            if (!SamePrivateGeneralDependencyGroup(expectedFirst, actualFirst))
            {
                return false;
            }

            var expectedEnd = expectedOffset + 1;
            while (expectedEnd < expectedRows.Length
                && SamePrivateGeneralDependencyGroup(expectedFirst,
                    expectedRows[expectedEnd]))
            {
                expectedEnd++;
            }
            var actualEnd = actualOffset + 1;
            while (actualEnd < actualRows.Length
                && SamePrivateGeneralDependencyGroup(actualFirst,
                    actualRows[actualEnd]))
            {
                actualEnd++;
            }

            var expectedEmpty = expectedFirst.SourceKind == 0;
            var actualEmpty = actualFirst.SourceKind == 0;
            if (expectedEmpty)
            {
                if (!actualEmpty || expectedEnd - expectedOffset != 1
                    || actualEnd - actualOffset != 1)
                {
                    return false;
                }
            }
            else if (actualEmpty)
            {
                if (actualEnd - actualOffset != 1)
                {
                    return false;
                }
            }
            else
            {
                for (var actualIndex = actualOffset;
                    actualIndex < actualEnd; actualIndex++)
                {
                    var actualRow = actualRows[actualIndex];
                    var matchingExpected = -1;
                    for (var expectedIndex = expectedOffset;
                        expectedIndex < expectedEnd; expectedIndex++)
                    {
                        var expectedRow = expectedRows[expectedIndex];
                        if (expectedRow.SourceKind == actualRow.SourceKind
                            && expectedRow.SourceOrdinal
                                == actualRow.SourceOrdinal)
                        {
                            matchingExpected = expectedIndex;
                            break;
                        }
                    }
                    if (matchingExpected < 0
                        || (((actualRow.Relationship
                                    & ~expectedRows[matchingExpected]
                                        .Relationship) & 0x03) != 0))
                    {
                        return false;
                    }
                }
            }

            expectedOffset = expectedEnd;
            actualOffset = actualEnd;
        }

        return expectedOffset == expectedRows.Length
            && actualOffset == actualRows.Length;
    }

    private static bool SamePrivateGeneralDependencyGroup(
        Private106GeneralDependencyRow left,
        Private106GeneralDependencyRow right)
        => left.DestinationKind == right.DestinationKind
            && left.DestinationOrdinal == right.DestinationOrdinal
            && left.Phase == right.Phase;

    private static bool IsValidPrivateGeneralDependencyRow(
        Private106GeneralDependencyRow row, int parameterCount,
        bool hasReceiverSource, bool hasReceiverDestination)
    {
        var destinationValid = row.DestinationKind switch
        {
            1 => row.DestinationOrdinal == 0,
            2 or 3 => row.DestinationOrdinal < (uint)parameterCount,
            4 => hasReceiverDestination && row.DestinationOrdinal == 0,
            _ => false,
        };
        if (!destinationValid || row.Phase is not (1 or 2))
        {
            return false;
        }

        var phaseValid = row.Phase switch
        {
            1 => row.DestinationKind is 1 or 2,
            2 => row.DestinationKind is 1 or 2 or 3 or 4,
            _ => false,
        };
        if (!phaseValid)
        {
            return false;
        }

        if (row.SourceKind == 0)
        {
            return row.Relationship == 0 && row.SourceOrdinal == 0;
        }

        if (row.Relationship is not (1 or 2 or 3))
        {
            return false;
        }
        return row.SourceKind switch
        {
            1 => row.SourceOrdinal < (uint)parameterCount,
            2 => hasReceiverSource && row.SourceOrdinal == 0,
            _ => false,
        };
    }

    private static (int ParameterCount, bool HasReceiverSource,
        bool HasReceiverDestination) PrivateDependencySignature(
        string contract)
    {
        var parts = contract.Split('|');
        if (parts.Length < 8 || parts[2] is not ("C" or "F")
            || parts[4] is not ("S" or "I") || parts[5].Length == 0)
        {
            throw new ModuleFormatException(
                "Private general Dependency owner has an invalid signature.");
        }
        var hasReceiverDestination = parts[4] == "I" && parts[5] != "-";
        var hasReceiverSource = hasReceiverDestination && parts[2] == "F";
        var formalCount = ContractParameterCount(contract);
        var parameterCount = formalCount - (hasReceiverSource ? 1 : 0);
        if (parameterCount < 0)
        {
            throw new ModuleFormatException(
                "Private general Dependency owner has an invalid receiver formal.");
        }
        return (parameterCount, hasReceiverSource,
            hasReceiverDestination);
    }

    private static int PrivateGeneralDependencyRowCountLimit(
        int parameterCount, bool hasReceiverSource)
    {
        var maximumRows = 3L * (2L + (2L * parameterCount))
            * Math.Max(1L,
                (long)parameterCount + (hasReceiverSource ? 1L : 0L));
        return (int)Math.Min(MaximumNodes, maximumRows);
    }

    private static (BoundDependencyDestination Destination,
        int? DestinationOrdinal) PrivateGeneralDependencyDestination(
        Private106GeneralDependencyRow row)
        => row.DestinationKind switch
        {
            1 => (BoundDependencyDestination.Result, null),
            2 => (BoundDependencyDestination.InitializedOutput,
                checked((int)row.DestinationOrdinal)),
            3 => (BoundDependencyDestination.RetainedFormal,
                checked((int)row.DestinationOrdinal)),
            4 => (BoundDependencyDestination.Receiver, null),
            _ => throw new ModuleFormatException(
                "Private general Dependency destination is not assigned."),
        };

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
        BinaryReader reader, bool import, bool hasBody,
        string contract,
        ImmutableArray<NativeOutputContract> nativeOutputContracts)
    {
        if (reader is not ModuleReader {
                FormatVersion: CheckedReferenceVersion or ForeignInterfacePrivateVersion,
            })
        {
            return default;
        }
        if (!import && !hasBody)
        {
            throw new ModuleFormatException(
                "A private revision 106 bodyless declaration must be an import.");
        }

        var generalDependencyContracts =
            ReadPrivate106GeneralDependencyContracts(reader, contract,
                nativeOutputContracts);
        var (checkedOutputSelectors, checkedOutputStates) =
            ReadPrivate106CheckedOutputStateVectors(reader, contract,
                hasBody, generalDependencyContracts,
                nativeOutputContracts);
        var rawUnionReturnCoverage
            = ReadPrivate106RawUnionReturnCoverage(reader);
        var copyOriginSummary = ReadPrivate105CopyOriginSummary(reader);
        var foreignInterfaceVariant = reader.ReadByte();
        var hasDirectImportExpectation = GetBool(reader);
        if (foreignInterfaceVariant != 0)
        {
            throw new ModuleFormatException(
                "Private revision 106 declaration metadata exceeds the current checked-reference type-table slice.");
        }

        if (!hasDirectImportExpectation)
        {
            return new Private106FunctionMetadata(rawUnionReturnCoverage,
                null, copyOriginSummary, generalDependencyContracts,
                checkedOutputSelectors, checkedOutputStates);
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
            }, copyOriginSummary, generalDependencyContracts,
            checkedOutputSelectors, checkedOutputStates);
    }

    private static (
        ImmutableArray<NativeFunctionPointerSelector> Selectors,
        ImmutableArray<NativeFunctionPointerOutputState> OutputStates)
        ReadPrivate106CheckedOutputStateVectors(
        BinaryReader reader, string contract, bool hasBody,
        ImmutableArray<BoundDependencyContract> dependencies,
        ImmutableArray<NativeOutputContract> nativeOutputContracts)
    {
        var (parameterCount, _, _) = PrivateDependencySignature(contract);
        var selectorCount = CountRows(reader, MaximumNodes, 12,
            "checked output selectors");
        var selectors = ImmutableArray.CreateBuilder<
            NativeFunctionPointerSelector>(selectorCount);
        for (var index = 0; index < selectorCount; index++)
        {
            var phaseTag = reader.ReadByte();
            var flags = reader.ReadByte();
            var reserved = reader.ReadUInt16();
            var ordinal = reader.ReadUInt32();
            var nodeKind = reader.ReadUInt32();
            if (flags != 0 || reserved != 0 || nodeKind > byte.MaxValue)
            {
                throw new ModuleFormatException(
                    "Private checked output selector is noncanonical.");
            }
            selectors.Add(new NativeFunctionPointerSelector(phaseTag,
                ordinal, checked((byte)nodeKind)));
        }

        var outputStateCount = CountRows(reader, MaximumNodes, 12,
            "checked output states");
        var outputStates = ImmutableArray.CreateBuilder<
            NativeFunctionPointerOutputState>(outputStateCount);
        for (var index = 0; index < outputStateCount; index++)
        {
            var formalOrdinal = reader.ReadUInt32();
            var selectorOrdinal = reader.ReadUInt32();
            var state = reader.ReadByte();
            var reserved = reader.ReadBytes(3);
            if (reserved.Length != 3
                || reserved[0] != 0 || reserved[1] != 0
                || reserved[2] != 0)
            {
                throw new ModuleFormatException(
                    "Private checked output state has nonzero reserved bytes.");
            }
            outputStates.Add(new NativeFunctionPointerOutputState(
                formalOrdinal, selectorOrdinal, state));
        }

        var normalizedSelectors = selectors
            .OrderBy(static selector => selector.PhaseLocalOrdinal)
            .ToImmutableArray();
        var normalizedOutputStates = outputStates
            .OrderBy(static outputState => outputState.FormalOrdinal)
            .ThenBy(static outputState => outputState.SelectorOrdinal)
            .ToImmutableArray();
        var outputFormals = hasBody
            ? []
            : GetPrivateCheckedOutputFormals(null, contract);
        if (!hasBody
            && (reader is not ModuleReader outputTypeReader
                || !PrivateCheckedOutputFormalTypesAreValid(null, contract,
                    outputTypeReader.Structures, outputTypeReader.Classes,
                    outputTypeReader.NativeFunctionPointerSignatures,
                    outputTypeReader.NativePointerTypes,
                    outputTypeReader.NativeFixedArrayTypes,
                    outputTypeReader.CheckedReferenceTypes)))
        {
            throw new ModuleFormatException(
                "Private checked output formal has an unsupported type.");
        }
        if (!ValidatePrivateCheckedOutputStateVectors(contract,
                outputFormals, normalizedSelectors, normalizedOutputStates,
                checked((uint)parameterCount), requireDeclaredOutputFormals:
                    !hasBody))
        {
            throw new ModuleFormatException(
                "Private checked output-state vectors contradict their declaration.");
        }
        if (!hasBody
            && !PrivateCheckedOutputDependenciesAreValid(
                dependencies, null, contract, nativeOutputContracts,
                normalizedOutputStates))
        {
            throw new ModuleFormatException(
                "Private checked output dependencies lack a valid publication state.");
        }

        return (normalizedSelectors, normalizedOutputStates);
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
