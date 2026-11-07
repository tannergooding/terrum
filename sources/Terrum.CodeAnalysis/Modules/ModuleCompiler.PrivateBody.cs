// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class ModuleCompiler
{
    private readonly record struct PrivatePlaceInfo(
        byte Kind,
        BoundExpression? Expression,
        BoundExpression? Descriptor,
        PrimitiveType Type,
        bool IsWritable,
        bool HasScopedProvenance,
        int Variable,
        BoundPrivatePlace? BoundPlace);

    private static void PutPrivateLocalPlace(BinaryWriter writer, int variable,
        ref int remaining, int depth)
    {
        Step(ref remaining, depth);
        if (writer is not ModuleWriter moduleWriter
            || (uint)variable >= (uint)moduleWriter.CurrentVariables.Length)
        {
            throw new ModuleContractException(
                "Private local place does not name a declared function slot.");
        }

        writer.Write((byte)1);
        writer.Write(variable);
    }

    private static void PutPrivateCallArgument(BinaryWriter writer,
        BoundCall call, int argumentIndex, string[] names,
        ref int remaining, int depth, bool inUnsafe)
    {
        var argument = call.Arguments[argumentIndex];
        if (writer is not ModuleWriter {
                FormatVersion: CheckedReferenceVersion,
            } moduleWriter)
        {
            PutExpression(writer, argument, names, ref remaining, depth,
                inUnsafe);
            return;
        }

        if ((uint)call.Function >= (uint)moduleWriter.SourceFunctions.Length
            || (!call.ArgumentParameterIndices.IsDefault
                && call.ArgumentParameterIndices.Length
                    != call.Arguments.Length)
            || (!call.ArgumentImplicitReferenceTemporaries.IsDefault
                && call.ArgumentImplicitReferenceTemporaries.Length
                    != call.Arguments.Length))
        {
            throw new ModuleContractException(
                "Private call argument mapping or target index is invalid.");
        }

        var sourceTarget = moduleWriter.SourceFunctions[call.Function];
        var targetName = names[call.Function];
        var retainedTarget = moduleWriter.ModuleFunctions.FirstOrDefault(
            item => item.Name == targetName)?.Function;
        var target = retainedTarget ?? sourceTarget;
        var parameterIndex = call.ParameterIndex(argumentIndex);
        var parameterSlot = parameterIndex
            + (target.IsConstructor ? 1 : 0);
        if ((uint)parameterSlot >= (uint)target.Variables.Length)
        {
            throw new ModuleContractException(
                "Private implicit-reference formal slot is invalid.");
        }

        var formal = target.Variables[parameterSlot];
        if (!call.ArgumentImplicitReferenceTemporaries.IsDefault)
        {
            throw new ModuleContractException(
                "A canonical private call retains implicit-reference temporary metadata.");
        }
        if (!formal.IsImplicitRef)
        {
            PutExpression(writer, argument, names, ref remaining, depth,
                inUnsafe);
            return;
        }

        if (!formal.IsCanonicalImplicitReference
            || formal.IsMutable || formal.IsOut || formal.IsInit
            || !CheckedReferenceTypeContract.IsCanonicalImplicitReferenceType(
                formal.Type, moduleWriter.Structures,
                moduleWriter.CheckedReferenceTypes)
            || argument.Type != formal.Type)
        {
            throw new ModuleContractException(
                "A private 106 implicit-reference call lacks its exact canonical descriptor operand.");
        }

        PutExpression(writer, argument, names, ref remaining, depth,
            inUnsafe);
    }

    private static void PutPrivateReferentPlace(BinaryWriter writer,
        BoundExpression descriptor, string[] names, ref int remaining,
        int depth, bool inUnsafe)
    {
        Step(ref remaining, depth);
        writer.Write((byte)4);
        PutPrivateBorrowDescriptor(writer, descriptor, names, ref remaining,
            depth + 1, inUnsafe);
    }

    private static void PutPrivateBorrowDescriptor(BinaryWriter writer,
        BoundExpression descriptor, string[] names, ref int remaining,
        int depth, bool inUnsafe)
    {
        if (writer is not ModuleWriter moduleWriter
            || !TryGetPrivateReferenceType(descriptor.Type,
                moduleWriter.CheckedReferenceTypes, out _))
        {
            throw new ModuleContractException(
                "A private referent place requires a complete checked-reference descriptor.");
        }

        if (descriptor is BoundPrivatePlaceRead privateRead)
        {
            if (privateRead.UseKind != 3)
            {
                throw new ModuleContractException(
                    "A referent descriptor slot requires BorrowDescriptor use.");
            }
            PutExpression(writer, descriptor, names, ref remaining, depth,
                inUnsafe, allowBorrowDescriptor: true);
            return;
        }

        if (descriptor is BoundRead or BoundFieldRead
            or BoundNativeFixedArrayElement or BoundBufferRead
            or BoundViewRead)
        {
            if (descriptor is BoundRead read)
            {
                ValidatePrivateLocalPlace(moduleWriter, read.Variable,
                    read.Type, writerArgument: true);
            }
            Step(ref remaining, depth);
            writer.Write((byte)50);
            WritePrivateType(writer, descriptor.Type);
            PutSpan(writer, descriptor.Span);
            writer.Write((byte)3);
            PutPrivatePlace(writer, descriptor, names, ref remaining,
                depth + 1, inUnsafe);
            return;
        }

        PutExpression(writer, descriptor, names, ref remaining, depth,
            inUnsafe);
    }

    private static void PutPrivatePlace(BinaryWriter writer,
        BoundExpression place, string[] names, ref int remaining, int depth,
        bool inUnsafe)
    {
        Step(ref remaining, depth);
        if (writer is not ModuleWriter moduleWriter)
        {
            throw new ModuleContractException(
                "Private places require the selected structured-module writer.");
        }

        switch (place)
        {
            case BoundRead read:
                ValidatePrivateLocalPlace(moduleWriter, read.Variable,
                    read.Type, writerArgument: true);
                writer.Write((byte)1);
                writer.Write(read.Variable);
                break;
            case BoundFieldRead field:
                var (fieldType, isUnsafe, fieldWritable) =
                    GetPrivateField(moduleWriter.Structures,
                        moduleWriter.Classes, field.Receiver.Type,
                        field.Field, writer: true);
                if (field.Type != fieldType
                    || field.IsUnsafeAuthorized != isUnsafe
                    || (isUnsafe && !inUnsafe))
                {
                    throw new ModuleContractException(
                        "Private field place differs from its retained owner or unsafe-field contract.");
                }

                if (ManagedClassTypes.IsClass(field.Receiver.Type))
                {
                    writer.Write((byte)3);
                    WritePrivateType(writer, field.Receiver.Type);
                    writer.Write(checked((uint)field.Field));
                    PutExpression(writer, field.Receiver, names,
                        ref remaining, depth + 1, inUnsafe);
                }
                else
                {
                    writer.Write((byte)2);
                    WritePrivateType(writer, field.Receiver.Type);
                    writer.Write(checked((uint)field.Field));
                    PutPrivatePlace(writer, field.Receiver, names,
                        ref remaining, depth + 1, inUnsafe);
                }

                _ = fieldWritable;
                break;
            case BoundNativeFixedArrayElement element:
                var layout = GetPrivateFixedArrayLayout(moduleWriter,
                    element.Layout.CarrierType);
                if (!IsRevision100RuntimeArrayLayout(layout,
                        moduleWriter.Structures,
                        moduleWriter.NativeFixedArrayTypes,
                        moduleWriter.NativePointerTypes,
                        moduleWriter.NativeFunctionPointerSignatures)
                    || element.Layout != layout
                    || element.Type != layout.ElementType
                    || !IsRevision100IndexType(element.Index.Type))
                {
                    throw new ModuleContractException(
                        "Private fixed-array place has unsupported or inconsistent layout facts.");
                }
                var arrayPlace = element.FieldPlace
                    ?? new BoundRead(element.Variable, layout.CarrierType,
                        element.Span);
                if ((uint)element.Variable
                        >= (uint)moduleWriter.CurrentVariables.Length
                    || (element.FieldPlace is not null
                        && !IsPrivateFixedArrayRootLocal(moduleWriter,
                            element.Variable))
                    || !TryGetPrivatePlaceRootVariable(arrayPlace,
                        out var arrayRoot)
                    || arrayRoot != element.Variable
                    || (element.FieldPlace is null
                        ? !NativeFixedArrayContract.MatchesType(
                            moduleWriter.CurrentVariables[element.Variable].Type,
                            layout, moduleWriter.Structures,
                            moduleWriter.NativeFixedArrayTypes)
                        : element.FieldPlace.Type != layout.CarrierType))
                {
                    throw new ModuleContractException(
                        "Private fixed-array place has no matching retained backing path.");
                }
                writer.Write((byte)5);
                WritePrivateType(writer, layout.CarrierType);
                PutPrivatePlace(writer, arrayPlace, names, ref remaining,
                    depth + 1, inUnsafe);
                PutExpression(writer, element.Index, names, ref remaining,
                    depth + 1, inUnsafe);
                break;
            case BoundNativeFixedArrayPathElement pathElement:
                PutPrivateArrayPathPlace(writer, pathElement, names,
                    ref remaining, depth, inUnsafe);
                break;
            case BoundBufferRead buffer:
                if (buffer.IsEndRelativeOffset
                    || (uint)buffer.Buffer
                        >= (uint)moduleWriter.InvocationBuffers.Length
                    || moduleWriter.InvocationBuffers[buffer.Buffer]
                        .ElementType != buffer.Type
                    || !IsRevision100IndexType(buffer.Index.Type))
                {
                    throw new ModuleContractException(
                        "Private invocation-buffer place has no matching retained binding.");
                }

                writer.Write((byte)6);
                writer.Write(buffer.Buffer);
                PutExpression(writer, buffer.Index, names, ref remaining,
                    depth + 1, inUnsafe);
                break;
            case BoundViewRead view:
                if ((uint)view.Variable
                        >= (uint)moduleWriter.CurrentVariables.Length
                    || moduleWriter.CurrentVariables[view.Variable].Type
                        is not (PrimitiveType.ReadonlyInt32BufferView
                            or PrimitiveType.MutableInt32BufferView)
                    || view.Type != PrimitiveType.Int32
                    || !IsRevision100IndexType(view.Index.Type))
                {
                    throw new ModuleContractException(
                        "Private lexical-view place has no matching view local.");
                }
                writer.Write((byte)7);
                writer.Write(view.Variable);
                PutExpression(writer, view.Index, names, ref remaining,
                    depth + 1, inUnsafe);
                break;
            default:
                throw new ModuleContractException(
                    "The private 106 writer cannot persist this place form.");
        }
    }

    private static void PutPrivateBoundPlace(BinaryWriter writer,
        BoundPrivatePlace place, string[] names, ref int remaining,
        int depth, bool inUnsafe,
        bool allowNestedFieldWriteBacking = false)
    {
        Step(ref remaining, depth);
        if (writer is not ModuleWriter moduleWriter)
        {
            throw new ModuleContractException(
                "Private typed places require the selected structured-module writer.");
        }

        switch (place)
        {
            case BoundPrivateLocalPlace local:
                ValidatePrivateLocalPlace(moduleWriter, local.Variable,
                    local.Type, writerArgument: true);
                writer.Write((byte)1);
                writer.Write(local.Variable);
                break;
            case BoundPrivateInlineFieldPlace field:
                if (field.OwnerType != field.Receiver.Type
                    || field.FieldOrdinal > int.MaxValue)
                {
                    throw new ModuleContractException(
                        "Private inline-field place has inconsistent owner identity.");
                }
                var (inlineFieldType, inlineUnsafe, _) = GetPrivateField(
                    moduleWriter.Structures, moduleWriter.Classes,
                    field.OwnerType, (int)field.FieldOrdinal,
                    writer: true);
                if (ManagedClassTypes.IsClass(field.OwnerType)
                    || inlineFieldType != field.Type
                    || (inlineUnsafe && !inUnsafe))
                {
                    throw new ModuleContractException(
                        "Private inline-field place differs from its retained FieldDef.");
                }
                writer.Write((byte)2);
                WritePrivateType(writer, field.OwnerType);
                writer.Write(field.FieldOrdinal);
                PutPrivateBoundPlace(writer, field.Receiver, names,
                    ref remaining, depth + 1, inUnsafe,
                    allowNestedFieldWriteBacking);
                break;
            case BoundPrivateObjectFieldPlace field:
                if (!ManagedClassTypes.IsClass(field.OwnerType)
                    || field.OwnerType != field.Receiver.Type
                    || field.FieldOrdinal > int.MaxValue)
                {
                    throw new ModuleContractException(
                        "Private object-field place has inconsistent owner identity.");
                }
                var (objectFieldType, objectUnsafe, _) = GetPrivateField(
                    moduleWriter.Structures, moduleWriter.Classes,
                    field.OwnerType, (int)field.FieldOrdinal,
                    writer: true);
                if (objectFieldType != field.Type
                    || (objectUnsafe && !inUnsafe))
                {
                    throw new ModuleContractException(
                        "Private object-field place differs from its retained FieldDef.");
                }
                writer.Write((byte)3);
                WritePrivateType(writer, field.OwnerType);
                writer.Write(field.FieldOrdinal);
                PutExpression(writer, field.Receiver, names, ref remaining,
                    depth + 1, inUnsafe);
                break;
            case BoundPrivateReferentPlace referent:
                if (!TryGetPrivateReferenceType(referent.Descriptor.Type,
                        moduleWriter.CheckedReferenceTypes,
                        out var descriptorType)
                    || descriptorType.IsOptional
                    || descriptorType.IsGarbageCollected
                    || descriptorType.ReferentType != referent.Type)
                {
                    throw new ModuleContractException(
                        "Private referent place has inconsistent descriptor identity.");
                }
                writer.Write((byte)4);
                PutPrivateBorrowDescriptor(writer, referent.Descriptor,
                    names, ref remaining, depth + 1, inUnsafe);
                break;
            case BoundPrivateFixedArrayElementPlace element:
                var layout = GetPrivateFixedArrayLayout(moduleWriter,
                    element.ArrayType);
                var nestedFieldWriteBacking
                    = allowNestedFieldWriteBacking;
                if (element.ArrayPlace.Type != element.ArrayType
                    || element.Type != layout.ElementType
                    || (!IsRevision100RuntimeArrayLayout(layout,
                            moduleWriter.Structures,
                            moduleWriter.NativeFixedArrayTypes,
                            moduleWriter.NativePointerTypes,
                            moduleWriter.NativeFunctionPointerSignatures)
                        && !(moduleWriter.FormatVersion
                                == CheckedReferenceVersion
                            && moduleWriter.SourceProgram
                                is BoundProgram private106Program
                            && IsPrivate106NestedArrayValueLocal(
                                element.ArrayType,
                                moduleWriter.CurrentReturnType,
                                moduleWriter.CurrentVariables,
                                private106Program))
                        && !nestedFieldWriteBacking)
                    || !IsRevision100IndexType(element.Index.Type)
                    || (element.Index is BoundConstant
                        && !NativeFixedArrayContract.IsConstantIndexInRange(
                            element.Index, layout.ElementCount)))
                {
                    throw new ModuleContractException(
                        "Private fixed-array place has inconsistent type or layout facts.");
                }
                writer.Write((byte)5);
                WritePrivateType(writer, element.ArrayType);
                PutPrivateBoundPlace(writer, element.ArrayPlace, names,
                    ref remaining, depth + 1, inUnsafe,
                    allowNestedFieldWriteBacking);
                PutExpression(writer, element.Index, names, ref remaining,
                    depth + 1, inUnsafe);
                break;
            case BoundPrivateInvocationBufferElementPlace buffer:
                if ((uint)buffer.Buffer
                        >= (uint)moduleWriter.InvocationBuffers.Length
                    || moduleWriter.InvocationBuffers[buffer.Buffer]
                        .ElementType != buffer.Type
                    || !IsRevision100IndexType(buffer.Index.Type))
                {
                    throw new ModuleContractException(
                        "Private invocation-buffer place has no matching retained binding.");
                }
                writer.Write((byte)6);
                writer.Write(buffer.Buffer);
                PutExpression(writer, buffer.Index, names, ref remaining,
                    depth + 1, inUnsafe);
                break;
            case BoundPrivateLexicalViewElementPlace view:
                if ((uint)view.Variable
                        >= (uint)moduleWriter.CurrentVariables.Length
                    || moduleWriter.CurrentVariables[view.Variable].Type
                        is not (PrimitiveType.ReadonlyInt32BufferView
                            or PrimitiveType.MutableInt32BufferView)
                    || view.Type != PrimitiveType.Int32
                    || !IsRevision100IndexType(view.Index.Type))
                {
                    throw new ModuleContractException(
                        "Private lexical-view place has no matching view local.");
                }
                writer.Write((byte)7);
                writer.Write(view.Variable);
                PutExpression(writer, view.Index, names, ref remaining,
                    depth + 1, inUnsafe);
                break;
            default:
                throw new ModuleContractException(
                    "The private 106 writer cannot persist this typed place form.");
        }
    }

    private static void ValidatePrivateReferenceForm(BinaryWriter writer,
        PrimitiveType type, byte formationKind, BoundPrivatePlace place,
        bool writerOutput)
    {
        var typeTable = writer is ModuleWriter moduleWriter
            ? moduleWriter.CheckedReferenceTypes
            : [];
        if (!TryGetPrivateReferenceType(type, typeTable,
                out var referenceType)
            || referenceType.IsOptional
            || referenceType.IsGarbageCollected
            || referenceType.ReferentType != place.Type
            || formationKind is not (1 or 2))
        {
            throw writerOutput
                ? new ModuleContractException(
                    "Private reference formation has inconsistent type or place identities.")
                : new ModuleFormatException(
                    "Private reference formation has inconsistent type or place identities.");
        }

        var hasScopedProvenance = PrivatePlaceHasScopedProvenance(place);
        var placeWritable = PrivatePlaceIsWritable(place, writer);
        if ((formationKind == 1 && hasScopedProvenance)
            || (formationKind == 2
                && !PrivatePlaceIsScopedReborrow(place))
            || (formationKind == 2
                && !PrivatePlaceScopedSourceMatches(place,
                    referenceType, typeTable))
            || (referenceType.IsWritable && !placeWritable))
        {
            throw writerOutput
                ? new ModuleContractException(
                    "Private reference formation exceeds its selected place authority.")
                : new ModuleFormatException(
                    "Private reference formation exceeds its selected place authority.");
        }
    }

    private static void ValidatePrivateInitializationAttenuation(
        BinaryWriter writer, PrimitiveType resultType,
        PrimitiveType descriptorType, bool writerOutput)
    {
        var typeTable = writer is ModuleWriter moduleWriter
            ? moduleWriter.CheckedReferenceTypes
            : [];
        if (!TryGetPrivateReferenceType(descriptorType, typeTable,
                out var source)
            || !TryGetPrivateReferenceType(resultType, typeTable,
                out var result)
            || !source.IsInitialization || source.IsOptional
            || source.IsGarbageCollected || result.IsInitialization
            || result.IsOptional || result.IsGarbageCollected
            || source.ReferentType != result.ReferentType
            || source.IsWritable != result.IsWritable)
        {
            throw writerOutput
                ? new ModuleContractException(
                    "Private initialization attenuation has inconsistent descriptor and result types.")
                : new ModuleFormatException(
                    "Private initialization attenuation has inconsistent descriptor and result types.");
        }
    }

    private static void ValidatePrivateReferenceMaterialization(
        BinaryWriter writer, PrimitiveType resultType, int backingVariable,
        PrimitiveType valueType, bool writerOutput)
    {
        var typeTable = writer is ModuleWriter moduleWriter
            ? moduleWriter.CheckedReferenceTypes
            : [];
        if (!TryGetPrivateReferenceType(resultType, typeTable,
                out var referenceType)
            || referenceType.IsInitialization
            || referenceType.IsGarbageCollected
            || referenceType.IsOptional
            || referenceType.IsWritable
            || referenceType.ReferentType != valueType
            || writer is not ModuleWriter privateWriter
            || backingVariable < privateWriter.CurrentParameterCount
                + (privateWriter.CurrentIsConstructor ? 1 : 0)
            || (uint)backingVariable
                >= (uint)privateWriter.CurrentVariables.Length)
        {
            throw writerOutput
                ? new ModuleContractException(
                    "Private implicit-reference materialization has invalid result or backing identities.")
                : new ModuleFormatException(
                    "Private implicit-reference materialization has invalid result or backing identities.");
        }

        var backing = privateWriter.CurrentVariables[backingVariable];
        if (!backing.IsImplicitReferenceTemporary
            || backing.Type != valueType || !backing.IsMutable
            || !backing.IsInitializationLocal
            || backing.IsOut || backing.IsInit)
        {
            throw writerOutput
                ? new ModuleContractException(
                    "Private implicit-reference materialization differs from its bit-04 backing local.")
                : new ModuleFormatException(
                    "Private implicit-reference materialization differs from its bit-04 backing local.");
        }
    }

    private static bool PrivatePlaceHasScopedProvenance(
        BoundPrivatePlace place) => place switch {
        BoundPrivateReferentPlace => true,
        BoundPrivateLexicalViewElementPlace => true,
        BoundPrivateInlineFieldPlace field =>
            PrivatePlaceHasScopedProvenance(field.Receiver),
        BoundPrivateFixedArrayElementPlace element =>
            PrivatePlaceHasScopedProvenance(element.ArrayPlace),
        _ => false,
    };

    private static bool PrivatePlaceIsScopedReborrow(
        BoundPrivatePlace place) => place switch {
        BoundPrivateReferentPlace => true,
        BoundPrivateLexicalViewElementPlace => true,
        BoundPrivateInlineFieldPlace field =>
            PrivatePlaceIsScopedReborrow(field.Receiver),
        BoundPrivateFixedArrayElementPlace element =>
            PrivatePlaceIsScopedReborrow(element.ArrayPlace),
        _ => false,
    };

    private static bool PrivatePlaceScopedSourceMatches(
        BoundPrivatePlace place, CheckedReferenceTypeDefinition resultType,
        System.Collections.Generic.IReadOnlyList<
            CheckedReferenceTypeDefinition> referenceTypes)
    {
        var referent = FindPrivatePlaceReferent(place);
        if (referent is not null)
        {
            return TryGetPrivateReferenceType(referent.Descriptor.Type,
                referenceTypes, out var sourceType)
            && !sourceType.IsOptional
            && !sourceType.IsGarbageCollected
            && sourceType.ReferentType == referent.Type
            && sourceType.IsInitialization == resultType.IsInitialization
            && (!resultType.IsWritable || sourceType.IsWritable);
        }

        return PrivatePlaceHasLexicalView(place)
            && !resultType.IsInitialization;
    }

    private static BoundPrivateReferentPlace? FindPrivatePlaceReferent(
        BoundPrivatePlace place) => place switch {
        BoundPrivateReferentPlace referent => referent,
        BoundPrivateInlineFieldPlace field =>
            FindPrivatePlaceReferent(field.Receiver),
        BoundPrivateFixedArrayElementPlace element =>
            FindPrivatePlaceReferent(element.ArrayPlace),
        _ => null,
    };

    private static bool PrivatePlaceHasLexicalView(
        BoundPrivatePlace place) => place switch {
        BoundPrivateLexicalViewElementPlace => true,
        BoundPrivateInlineFieldPlace field =>
            PrivatePlaceHasLexicalView(field.Receiver),
        BoundPrivateFixedArrayElementPlace element =>
            PrivatePlaceHasLexicalView(element.ArrayPlace),
        _ => false,
    };

    private static bool PrivatePlaceIsWritable(BoundPrivatePlace place,
        BinaryWriter writer)
    {
        if (writer is not ModuleWriter moduleWriter)
        {
            return false;
        }

        return place switch {
            BoundPrivateLocalPlace local =>
                (uint)local.Variable
                    < (uint)moduleWriter.CurrentVariables.Length
                && moduleWriter.CurrentVariables[local.Variable].IsMutable,
            BoundPrivateInlineFieldPlace field =>
                PrivatePlaceIsWritable(field.Receiver, writer)
                && PrivateFieldIsWritable(moduleWriter, field.OwnerType,
                    field.FieldOrdinal),
            BoundPrivateObjectFieldPlace field =>
                ManagedClassTypes.IsWritable(field.OwnerType)
                && PrivateFieldIsWritable(moduleWriter, field.OwnerType,
                    field.FieldOrdinal),
            BoundPrivateReferentPlace referent =>
                GetPrivateReferenceWriteAccess(referent.Descriptor.Type,
                    moduleWriter.CheckedReferenceTypes),
            BoundPrivateFixedArrayElementPlace element =>
                PrivatePlaceIsWritable(element.ArrayPlace, writer)
                && NativeFixedArrayContract.AllowsElementWrites(
                    GetPrivateFixedArrayLayout(moduleWriter,
                        element.ArrayType)),
            BoundPrivateInvocationBufferElementPlace buffer =>
                (uint)buffer.Buffer
                    < (uint)moduleWriter.InvocationBuffers.Length
                && moduleWriter.InvocationBuffers[buffer.Buffer].IsWritable,
            BoundPrivateLexicalViewElementPlace view =>
                (uint)view.Variable
                    < (uint)moduleWriter.CurrentVariables.Length
                && moduleWriter.CurrentVariables[view.Variable].Type
                    == PrimitiveType.MutableInt32BufferView,
            _ => false,
        };
    }

    private static bool PrivateFieldIsWritable(ModuleWriter writer,
        PrimitiveType ownerType, uint fieldOrdinal)
    {
        if (fieldOrdinal > int.MaxValue)
        {
            return false;
        }
        try
        {
            return GetPrivateField(writer.Structures, writer.Classes,
                ownerType, (int)fieldOrdinal, writer: true).IsWritable;
        }
        catch (ModuleContractException)
        {
            return false;
        }
    }

    private static void PutPrivateArrayPathPlace(BinaryWriter writer,
        BoundNativeFixedArrayPathElement path, string[] names,
        ref int remaining, int depth, bool inUnsafe)
    {
        if (writer is not ModuleWriter moduleWriter
            || path.Projections.IsDefaultOrEmpty
            || (path.FieldPlace is not null
                && path.FieldPlace.Type
                    != path.Projections[0].Layout.CarrierType)
            || (uint)path.Variable
                >= (uint)moduleWriter.CurrentVariables.Length)
        {
            throw new ModuleContractException(
                "Private fixed-array path has no complete retained root.");
        }

        var rootType = path.FieldPlace?.Type
            ?? moduleWriter.CurrentVariables[path.Variable].Type;
        if (!NativeFixedArrayContract.MatchesPath(rootType,
                path.Projections, moduleWriter.Structures,
                moduleWriter.NativeFixedArrayTypes, out _, out var leafLayout)
            || leafLayout.ElementType != path.Type
            || path.Projections.Any(projection =>
                !IsRevision100RuntimeArrayLayout(projection.Layout,
                    moduleWriter.Structures,
                    moduleWriter.NativeFixedArrayTypes,
                    moduleWriter.NativePointerTypes,
                    moduleWriter.NativeFunctionPointerSignatures)
                || !IsRevision100IndexType(projection.Index.Type)
                || (projection.Index is BoundConstant
                    && !NativeFixedArrayContract.IsConstantIndexInRange(
                        projection.Index, projection.Layout.ElementCount)))
            || !TryGetPrivatePlaceRootVariable(path.FieldPlace
                ?? new BoundRead(path.Variable, rootType, path.Span),
                out var rootVariable)
            || rootVariable != path.Variable
            || !IsPrivateFixedArrayRootLocal(moduleWriter, path.Variable))
        {
            throw new ModuleContractException(
                "Private fixed-array path differs from its retained array rows.");
        }

        var lastIndex = path.Projections.Length - 1;
        var leafProjection = path.Projections[lastIndex];
        writer.Write((byte)5);
        WritePrivateType(writer, leafProjection.Layout.CarrierType);
        PutPrivateArrayPathPrefix(writer, path, lastIndex, names,
            ref remaining, depth + 1, inUnsafe);
        PutExpression(writer, leafProjection.Index, names, ref remaining,
            depth + 1, inUnsafe);
    }

    private static void PutPrivateArrayPathPrefix(BinaryWriter writer,
        BoundNativeFixedArrayPathElement path, int projectionCount,
        string[] names, ref int remaining, int depth, bool inUnsafe)
    {
        if (projectionCount == 0)
        {
            var rootPlace = path.FieldPlace
                ?? new BoundRead(path.Variable,
                    path.Projections[0].Layout.CarrierType, path.Span);
            PutPrivatePlace(writer, rootPlace, names, ref remaining,
                depth, inUnsafe);
            return;
        }

        var projection = path.Projections[projectionCount - 1];
        Step(ref remaining, depth);
        writer.Write((byte)5);
        WritePrivateType(writer, projection.Layout.CarrierType);
        PutPrivateArrayPathPrefix(writer, path, projectionCount - 1,
            names, ref remaining, depth + 1, inUnsafe);
        PutExpression(writer, projection.Index, names, ref remaining,
            depth + 1, inUnsafe);
    }

    private static bool TryGetPrivatePlaceRootVariable(
        BoundExpression place, out int variable)
    {
        switch (place)
        {
            case BoundRead read:
                variable = read.Variable;
                return true;
            case BoundFieldRead field:
                return TryGetPrivatePlaceRootVariable(field.Receiver,
                    out variable);
            case BoundNativeFixedArrayElement element:
                variable = element.Variable;
                return true;
            case BoundNativeFixedArrayPathElement element:
                variable = element.Variable;
                return true;
            default:
                variable = -1;
                return false;
        }
    }

    private static BoundPrivatePlace CreatePrivateFixedArrayPathWritePlace(
        ModuleWriter writer, BoundNativeFixedArrayPathWrite pathWrite)
    {
            var sourceProgram = writer.SourceProgram
                ?? throw new ModuleContractException(
                    "Private fixed-array path write has no source program.");
            var sourceFunction = writer.CurrentSourceFunction
                ?? throw new ModuleContractException(
                    "Private fixed-array path write has no source function.");
            if (pathWrite.Projections.IsDefaultOrEmpty
                || (uint)pathWrite.Variable >= (uint)writer.CurrentVariables.Length
                || pathWrite.Variable >= sourceFunction.Variables.Length
                || pathWrite.Projections.Length > SemanticVerifier.MaximumDepth
                || pathWrite.LeafFields.Length
                    > SemanticVerifier.MaximumDepth - pathWrite.Projections.Length)
            {
                throw new ModuleContractException(
                    "Private fixed-array path write has an invalid retained root or path.");
            }

            var rootType = pathWrite.FieldPlace?.Type
                ?? writer.CurrentVariables[pathWrite.Variable].Type;
            if (!NativeFixedArrayContract.MatchesPath(rootType,
                    pathWrite.Projections, writer.Structures,
                    writer.NativeFixedArrayTypes, out _, out var leafLayout)
                || (pathWrite.FieldPlace is not null
                    && TryGetPrivatePlaceRootVariable(pathWrite.FieldPlace,
                        out var fieldRoot)
                    && fieldRoot != pathWrite.Variable))
            {
                throw new ModuleContractException(
                    "Private fixed-array path write differs from its retained array rows.");
            }

            var hasInheritedNestedFieldWriteBacking
                = writer.FormatVersion == CheckedReferenceVersion
                    && pathWrite.FieldPlace is null
                    && !pathWrite.LeafFields.IsDefaultOrEmpty
                    && IsPrivateFixedArrayRootLocal(writer,
                        pathWrite.Variable)
                    && TryGetAddressOnlyNestedBackingVariables(sourceProgram,
                        sourceFunction, out var nestedBackingVariables,
                        out _)
                    && nestedBackingVariables.Contains(pathWrite.Variable);
            foreach (var projection in pathWrite.Projections)
            {
                if (!IsRevision100RuntimeArrayLayout(projection.Layout,
                        writer.Structures, writer.NativeFixedArrayTypes,
                        writer.NativePointerTypes,
                        writer.NativeFunctionPointerSignatures)
                    && !IsPrivate106NestedArrayValueLocal(
                        projection.Layout.CarrierType,
                        writer.CurrentReturnType, writer.CurrentVariables,
                        sourceProgram)
                    && !hasInheritedNestedFieldWriteBacking)
                {
                    throw new ModuleContractException(
                        "Private fixed-array path write uses an unadmitted nested array layout.");
                }
            }

            var destinationType = leafLayout.ElementType;
            if (!pathWrite.LeafFields.IsDefaultOrEmpty)
            {
                if (!TryGetPrivateFixedArrayLeafFieldType(
                        leafLayout.ElementType, pathWrite.LeafFields,
                        writer.Structures, writer.NativeFixedArrayTypes,
                        writer.NativePointerTypes,
                        writer.NativeFunctionPointerSignatures,
                        out destinationType))
                {
                    throw new ModuleContractException(
                        "Private fixed-array path write has invalid leaf fields.");
                }
            }
            if (pathWrite.Value.Type != destinationType)
            {
                throw new ModuleContractException(
                    "Private fixed-array path value differs from its retained destination.");
            }

            BoundExpression expression = new BoundNativeFixedArrayPathElement(
                pathWrite.Variable, pathWrite.Projections, leafLayout.ElementType,
                pathWrite.Span) {
                FieldPlace = pathWrite.FieldPlace,
            };
            var currentType = leafLayout.ElementType;
            if (!pathWrite.LeafFields.IsDefaultOrEmpty)
            {
                foreach (var leafField in pathWrite.LeafFields)
                {
                    if (leafField.OwnerType != currentType)
                    {
                        throw new ModuleContractException(
                            "Private fixed-array leaf field owner differs from its parent.");
                    }
                    var (fieldType, isUnsafe, _) = GetPrivateField(
                        writer.Structures, writer.Classes, currentType,
                        leafField.Field, writer: true);
                    expression = new BoundFieldRead(expression,
                        leafField.Field, fieldType, pathWrite.Span) {
                        IsUnsafeAuthorized = isUnsafe,
                    };
                    currentType = fieldType;
                }
            }

            return CreatePrivateBoundPlace(writer, expression,
                pathWrite.Value.Type);
    }

    private static BoundPrivatePlace CreatePrivateBoundPlace(
        ModuleWriter writer, BoundExpression expression,
        PrimitiveType expectedType)
    {
        var sourceProgram = writer.SourceProgram
            ?? throw new ModuleContractException(
                "Private typed place has no source program.");
        var sourceFunction = writer.CurrentSourceFunction
            ?? throw new ModuleContractException(
                "Private typed place has no source function.");
        if (!TryCreatePrivatePlace(expression, sourceFunction, sourceProgram,
                out var place)
            || place.Type != expectedType)
        {
            throw new ModuleContractException(
                "Private expression cannot be represented as the selected typed place.");
        }
        return place;
    }

    private static BoundPrivateReferenceForm GetPrivateReferenceForm(
        BinaryReader reader, PrimitiveType type,
        Terrum.CodeAnalysis.Text.TextSpan span, ref int remaining,
        int depth, bool allowBorrowDescriptor)
    {
        if (reader is not ModuleReader moduleReader
            || !TryGetPrivateReferenceType(type,
                moduleReader.CheckedReferenceTypes,
                out var referenceDefinition)
            || referenceDefinition.IsOptional
            || referenceDefinition.IsGarbageCollected)
        {
            throw new ModuleFormatException(
                "Private reference formation has an invalid result type.");
        }

        var formationKind = reader.ReadByte();
        if (formationKind is not (1 or 2))
        {
            throw new ModuleFormatException(
                "Private reference formation has an unknown formation kind.");
        }

        var place = GetPrivatePlace(reader, ref remaining, depth + 1, span,
            allowBorrowDescriptor);
        if (place.Type != referenceDefinition.ReferentType)
        {
            throw new ModuleFormatException(
                "Private reference formation result and place types differ.");
        }

        if (formationKind == 1)
        {
            if (place.HasScopedProvenance
                || (referenceDefinition.IsWritable && !place.IsWritable))
            {
                throw new ModuleFormatException(
                    "Fresh private reference formation exceeds its place authority.");
            }
            return new BoundPrivateReferenceForm(formationKind,
                place.BoundPlace ?? throw new ModuleFormatException(
                    "Private reference formation has no typed place."),
                type, span);
        }

            var scopedPlaceValid = place.BoundPlace is not null
                && PrivatePlaceIsScopedReborrow(place.BoundPlace)
                && PrivatePlaceScopedSourceMatches(place.BoundPlace,
                    referenceDefinition,
                    moduleReader.CheckedReferenceTypes);
            if (!scopedPlaceValid
                || (referenceDefinition.IsWritable && !place.IsWritable))
            {
                throw new ModuleFormatException(
                    "Scoped private reference formation lacks a valid referent descriptor.");
            }

            return new BoundPrivateReferenceForm(formationKind,
                place.BoundPlace ?? throw new ModuleFormatException(
                    "Scoped private reference formation has no typed place."),
                type, span);
    }

    private static BoundPrivatePlaceRead GetPrivatePlaceRead(BinaryReader reader,
        PrimitiveType type, Terrum.CodeAnalysis.Text.TextSpan span,
        ref int remaining, int depth, bool allowBorrowDescriptor)
    {
        var useKind = reader.ReadByte();
        if (useKind is not (1 or 2 or 3))
        {
            throw new ModuleFormatException(
                "Private place read has an unknown use kind.");
        }
        if (useKind == 3 && !allowBorrowDescriptor)
        {
            throw new ModuleFormatException(
                "A borrowed descriptor may only occur as a referent operand.");
        }

        var place = GetPrivatePlace(reader, ref remaining, depth + 1,
            span, allowBorrowDescriptor);
        if (place.Type != type)
        {
            throw new ModuleFormatException(
                "Private place read result differs from the selected place type.");
        }

        if (useKind == 3)
        {
            if (place.Expression is null
                || !TryGetPrivateReferenceType(type,
                    (reader as ModuleReader)?.CheckedReferenceTypes
                        ?? [], out var descriptorType)
                || descriptorType.IsOptional
                || descriptorType.IsGarbageCollected)
            {
                throw new ModuleFormatException(
                    "BorrowDescriptor requires a stable reference descriptor place.");
            }
        }
        else if (place.Kind == 4)
        {
            if (useKind != 1
                || !TryGetPrivateReferenceType(place.Descriptor!.Type,
                    (reader as ModuleReader)?.CheckedReferenceTypes
                        ?? [], out var referentType)
                || referentType.IsInitialization
                || referentType.IsOptional
                || referentType.IsGarbageCollected)
            {
                throw new ModuleFormatException(
                    "Private referent reads require an ordinary initialized descriptor and Copy use.");
            }
        }

        return new BoundPrivatePlaceRead(useKind,
            place.BoundPlace ?? throw new ModuleFormatException(
                "Private place read has no typed place."), type, span);
    }

    private static BoundPrivatePlaceIsInitialized
        GetPrivatePlaceInitializationTest(
        BinaryReader reader, Terrum.CodeAnalysis.Text.TextSpan span,
        ref int remaining, int depth, bool allowBorrowDescriptor)
    {
        var place = GetPrivatePlace(reader, ref remaining, depth + 1,
            span, allowBorrowDescriptor);
        return new BoundPrivatePlaceIsInitialized(
            place.BoundPlace ?? throw new ModuleFormatException(
                "Private initialization test has no typed place."),
            span);
    }

    private static BoundPrivateCheckedStore GetPrivateCheckedStore(
        BinaryReader reader, Terrum.CodeAnalysis.Text.TextSpan span,
        ref int remaining, int depth, bool allowBorrowDescriptor)
    {
        var transition = reader.ReadByte();
        if (transition is not (1 or 2 or 3))
        {
            throw new ModuleFormatException(
                "Private checked store has an unknown state transition.");
        }

        var place = GetPrivatePlace(reader, ref remaining, depth + 1,
            span, allowBorrowDescriptor,
            allowNestedFieldWriteBacking: transition == 2,
            out var usedNestedFieldWriteBacking);
        if (usedNestedFieldWriteBacking
            && (reader is not ModuleReader moduleReader
                || !IsPrivateNestedArrayFieldWritePlace(
                    place.BoundPlace
                        ?? throw new ModuleFormatException(
                            "Private nested field write has no typed destination."),
                    moduleReader)))
        {
            throw new ModuleFormatException(
                "Private nested array backing is only admitted for direct-local field-suffixed writes.");
        }
        var value = GetExpression(reader, ref remaining, depth + 1);
        if (value.Type != place.Type)
        {
            throw new ModuleFormatException(
                "Private checked-store value and destination types differ.");
        }
        return new BoundPrivateCheckedStore(transition,
            place.BoundPlace ?? throw new ModuleFormatException(
                "Private checked store has no typed destination."), value,
            span);
    }

    private static BoundInitializationAttenuation
        GetPrivateInitializationAttenuation(BinaryReader reader,
            PrimitiveType type,
            Terrum.CodeAnalysis.Text.TextSpan span,
            ref int remaining, int depth)
    {
        var descriptor = GetExpression(reader, ref remaining, depth + 1,
            allowBorrowDescriptor: true);
        if (reader is not ModuleReader moduleReader
            || !TryGetPrivateReferenceType(descriptor.Type,
                moduleReader.CheckedReferenceTypes,
                out var sourceType)
            || !TryGetPrivateReferenceType(type,
                moduleReader.CheckedReferenceTypes, out var resultType)
            || !sourceType.IsInitialization
            || sourceType.IsOptional
            || sourceType.IsGarbageCollected
            || resultType.IsInitialization
            || resultType.IsOptional
            || resultType.IsGarbageCollected
            || sourceType.ReferentType != resultType.ReferentType
            || sourceType.IsWritable != resultType.IsWritable)
        {
            throw new ModuleFormatException(
                "Private initialization attenuation has inconsistent descriptor and result types.");
        }

        return new BoundInitializationAttenuation(descriptor, type, span);
    }

    private static BoundImplicitReferenceMaterialize
        GetPrivateReferenceMaterialization(BinaryReader reader,
            PrimitiveType type,
            Terrum.CodeAnalysis.Text.TextSpan span,
            ref int remaining, int depth)
    {
        if (reader is not ModuleReader moduleReader
            || !TryGetPrivateReferenceType(type,
                moduleReader.CheckedReferenceTypes, out var resultType)
            || resultType.IsInitialization
            || resultType.IsGarbageCollected
            || resultType.IsOptional
            || resultType.IsWritable)
        {
            throw new ModuleFormatException(
                "Private implicit-reference materialization has an invalid result type.");
        }

        var backingVariable = reader.ReadInt32();
        var value = GetExpression(reader, ref remaining, depth + 1);
        var firstLocal = moduleReader.CurrentParameterCount
            + (moduleReader.CurrentIsConstructor ? 1 : 0);
        if (backingVariable < firstLocal
            || (uint)backingVariable
                >= (uint)moduleReader.CurrentVariables.Length)
        {
            throw new ModuleFormatException(
                "Private implicit-reference materialization has an invalid backing local.");
        }

        var backing = moduleReader.CurrentVariables[backingVariable];
        if (!backing.IsImplicitReferenceTemporary
            || backing.Type != value.Type
            || resultType.ReferentType != value.Type
            || !backing.IsMutable || !backing.IsInitializationLocal
            || backing.IsOut || backing.IsInit)
        {
            throw new ModuleFormatException(
                "Private implicit-reference materialization does not match its backing declaration.");
        }

        return new BoundImplicitReferenceMaterialize(backingVariable,
            value, type, span);
    }

    private static PrivatePlaceInfo GetPrivatePlace(BinaryReader reader,
        ref int remaining, int depth,
        Terrum.CodeAnalysis.Text.TextSpan span,
        bool allowBorrowDescriptor)
        => GetPrivatePlace(reader, ref remaining, depth, span,
            allowBorrowDescriptor, allowNestedFieldWriteBacking: false,
            out _);

    private static PrivatePlaceInfo GetPrivatePlace(BinaryReader reader,
        ref int remaining, int depth,
        Terrum.CodeAnalysis.Text.TextSpan span,
        bool allowBorrowDescriptor, bool allowNestedFieldWriteBacking,
        out bool usedNestedFieldWriteBacking)
    {
        usedNestedFieldWriteBacking = false;
        Step(ref remaining, depth);
        if (reader is not ModuleReader moduleReader
            || moduleReader.FormatVersion != CheckedReferenceVersion)
        {
            throw new ModuleFormatException(
                "Private places require exact revision 106.");
        }

        var kind = reader.ReadByte();
        switch (kind)
        {
            case 1:
                var variable = reader.ReadInt32();
                if ((uint)variable >= (uint)moduleReader.CurrentVariables.Length)
                {
                    throw new ModuleFormatException(
                        "Private local place index is invalid.");
                }
                var local = moduleReader.CurrentVariables[variable];
                var localExpression = new BoundRead(variable, local.Type,
                    span);
                return new(kind, localExpression, null, local.Type,
                    local.IsMutable, false, variable,
                    new BoundPrivateLocalPlace(variable, local.Type));
            case 2:
            case 3:
                var ownerType = GetType(reader);
                var fieldOrdinal = reader.ReadUInt32();
                var receiverUsedNestedFieldWriteBacking = false;
                var receiver = kind == 2
                    ? GetPrivatePlace(reader, ref remaining, depth + 1,
                        span, allowBorrowDescriptor,
                        allowNestedFieldWriteBacking,
                        out receiverUsedNestedFieldWriteBacking)
                    : GetPrivateObjectFieldReceiver(reader, ref remaining,
                        depth + 1, span);
                usedNestedFieldWriteBacking
                    = receiverUsedNestedFieldWriteBacking;
                if (receiver.Type != ownerType
                    || (kind == 2 && ManagedClassTypes.IsClass(ownerType))
                    || (kind == 3 && !ManagedClassTypes.IsClass(ownerType))
                    || receiver.Expression is null
                    || fieldOrdinal > int.MaxValue)
                {
                    throw new ModuleFormatException(
                        "Private field owner identity or field ordinal is invalid.");
                }
                var (fieldType, isUnsafe, fieldWritable) =
                    GetPrivateField(moduleReader.Structures,
                        moduleReader.Classes, ownerType,
                        (int)fieldOrdinal, writer: false);
                var fieldExpression = new BoundFieldRead(
                    receiver.Expression!, (int)fieldOrdinal,
                        fieldType, span) {
                        IsUnsafeAuthorized = isUnsafe,
                    };
                BoundPrivatePlace fieldBoundPlace = kind == 2
                    ? new BoundPrivateInlineFieldPlace(ownerType,
                        fieldOrdinal,
                        receiver.BoundPlace ?? throw new ModuleFormatException(
                            "Inline field receiver has no typed place."),
                        fieldType)
                    : new BoundPrivateObjectFieldPlace(ownerType,
                        fieldOrdinal, receiver.Expression, fieldType);
                return new(kind, fieldExpression, null, fieldType,
                    receiver.IsWritable && fieldWritable,
                    receiver.HasScopedProvenance,
                    receiver.Variable, fieldBoundPlace);
            case 4:
                var descriptor = GetExpression(reader, ref remaining,
                    depth + 1, allowBorrowDescriptor: true);
                if (!TryGetPrivateReferenceType(descriptor.Type,
                        moduleReader.CheckedReferenceTypes,
                        out var descriptorType)
                    || descriptorType.IsOptional
                    || descriptorType.IsGarbageCollected)
                {
                    throw new ModuleFormatException(
                        "Private referent place has an invalid descriptor type.");
                }
                return new(kind, null, descriptor,
                    descriptorType.ReferentType,
                    descriptorType.IsWritable, true, -1,
                    new BoundPrivateReferentPlace(descriptor,
                        descriptorType.ReferentType));
            case 5:
                var arrayType = GetType(reader);
                var arrayPlace = GetPrivatePlace(reader, ref remaining,
                    depth + 1, span, allowBorrowDescriptor,
                    allowNestedFieldWriteBacking,
                    out var parentUsedNestedFieldWriteBacking);
                var layout = GetPrivateFixedArrayLayout(reader, arrayType);
                var isRuntimeArrayLayout
                    = IsRevision100RuntimeArrayLayout(layout,
                            moduleReader.Structures,
                            moduleReader.NativeFixedArrayTypes,
                            moduleReader.NativePointerTypes,
                            moduleReader.NativeFunctionPointerSignatures);
                var isAggregateArrayValue
                    = moduleReader.FormatVersion == CheckedReferenceVersion
                        && IsPrivate106NestedArrayValueLocal(arrayType,
                                moduleReader.CurrentReturnType,
                                moduleReader.CurrentVariables,
                                CreatePrivate106CoverageProgram(
                                    moduleReader));
                var isNestedFieldWriteBacking
                    = allowNestedFieldWriteBacking
                        && IsPrivateFixedArrayRootLocal(moduleReader,
                            arrayPlace.Variable);
                if (arrayPlace.Type != arrayType
                    || (!isRuntimeArrayLayout
                        && !isAggregateArrayValue
                        && !isNestedFieldWriteBacking))
                {
                    throw new ModuleFormatException(
                        "Private fixed-array place has an unsupported row or mismatched parent type.");
                }
                usedNestedFieldWriteBacking
                    = parentUsedNestedFieldWriteBacking
                        || (!isRuntimeArrayLayout
                            && !isAggregateArrayValue
                            && isNestedFieldWriteBacking);
                var index = GetExpression(reader, ref remaining,
                    depth + 1);
                if (!IsRevision100IndexType(index.Type)
                    || (index is BoundConstant
                        && !NativeFixedArrayContract.IsConstantIndexInRange(
                            index, layout.ElementCount)))
                {
                    throw new ModuleFormatException(
                        "Private fixed-array place index or backing path is invalid.");
                }
                var fixedArrayPlace = CreatePrivateFixedArrayPlace(
                    moduleReader, arrayPlace, layout, index, span,
                    allowNestedFieldWriteBacking);
                if (!TryGetPrivatePlaceRootVariable(fixedArrayPlace,
                        out var fixedArrayRoot))
                {
                    throw new ModuleFormatException(
                        "Private fixed-array place has no retained root local.");
                }
                return new(kind, fixedArrayPlace,
                    null, layout.ElementType, arrayPlace.IsWritable,
                    arrayPlace.HasScopedProvenance, fixedArrayRoot,
                    new BoundPrivateFixedArrayElementPlace(arrayType,
                        arrayPlace.BoundPlace
                            ?? throw new ModuleFormatException(
                                "Fixed-array parent has no typed place."),
                        index, layout.ElementType));
            case 6:
                var buffer = reader.ReadInt32();
                if ((uint)buffer >= (uint)moduleReader.InvocationBuffers.Length)
                {
                    throw new ModuleFormatException(
                        "Private invocation-buffer index is invalid.");
                }
                var bufferIndex = GetExpression(reader, ref remaining,
                    depth + 1);
                var bufferDefinition =
                    moduleReader.InvocationBuffers[buffer];
                if (!IsRevision100IndexType(bufferIndex.Type))
                {
                    throw new ModuleFormatException(
                        "Private invocation-buffer index expression is invalid.");
                }
                return new(kind, new BoundBufferRead(buffer, bufferIndex,
                    bufferDefinition.ElementType, span), null,
                    bufferDefinition.ElementType,
                    bufferDefinition.IsWritable, false, -1,
                    new BoundPrivateInvocationBufferElementPlace(buffer,
                        bufferIndex, bufferDefinition.ElementType));
            case 7:
                var viewVariable = reader.ReadInt32();
                if ((uint)viewVariable
                    >= (uint)moduleReader.CurrentVariables.Length)
                {
                    throw new ModuleFormatException(
                        "Private lexical-view variable index is invalid.");
                }
                var viewType = moduleReader.CurrentVariables[
                    viewVariable].Type;
                if (viewType is not (PrimitiveType.ReadonlyInt32BufferView
                    or PrimitiveType.MutableInt32BufferView))
                {
                    throw new ModuleFormatException(
                        "Private lexical-view place does not select an admitted view local.");
                }
                var viewIndex = GetExpression(reader, ref remaining,
                    depth + 1);
                if (!IsRevision100IndexType(viewIndex.Type))
                {
                    throw new ModuleFormatException(
                        "Private lexical-view index expression is invalid.");
                }
                return new(kind, new BoundViewRead(viewVariable, viewIndex,
                    span), null, PrimitiveType.Int32,
                    viewType == PrimitiveType.MutableInt32BufferView,
                    true, viewVariable,
                    new BoundPrivateLexicalViewElementPlace(viewVariable,
                        viewIndex, PrimitiveType.Int32));
            default:
                throw new ModuleFormatException(
                    "Private place has an unknown kind.");
        }
    }

    private static BoundExpression CreatePrivateFixedArrayPlace(
        ModuleReader reader, PrivatePlaceInfo arrayPlace,
        BoundNativeFixedArrayLayout layout, BoundExpression index,
        Terrum.CodeAnalysis.Text.TextSpan span,
        bool allowNestedFieldWriteBacking = false)
    {
        if (arrayPlace.Kind == 1)
        {
            ValidatePrivateFixedArrayLocal(reader, arrayPlace.Variable,
                layout, requireMutable: false);
            return new BoundNativeFixedArrayElement(arrayPlace.Variable,
                layout, index, span);
        }

        if (arrayPlace.Expression is BoundFieldRead fieldPlace)
        {
            if (!TryGetPrivatePlaceRootVariable(fieldPlace,
                    out var fieldRoot)
                || !IsPrivateFixedArrayRootLocal(reader, fieldRoot))
            {
                throw new ModuleFormatException(
                    "Private fixed-array field place has no supported local backing.");
            }
            return new BoundNativeFixedArrayElement(fieldRoot, layout,
                index, span) {
                FieldPlace = fieldPlace,
            };
        }

        if (arrayPlace.Expression is BoundNativeFixedArrayElement parentElement
            && parentElement.Type == layout.CarrierType)
        {
            var projections = ImmutableArray.Create(
                new BoundNativeFixedArrayProjection(parentElement.Layout,
                    parentElement.Index),
                new BoundNativeFixedArrayProjection(layout, index));
            var path = new BoundNativeFixedArrayPathElement(
                parentElement.Variable, projections, layout.ElementType,
                span) {
                FieldPlace = parentElement.FieldPlace,
            };
            ValidatePrivateFixedArrayPath(reader, path,
                allowNestedFieldWriteBacking);
            return path;
        }

        if (arrayPlace.Expression is BoundNativeFixedArrayPathElement
                parentPath
            && parentPath.Type == layout.CarrierType)
        {
            var projections = parentPath.Projections.Add(
                new BoundNativeFixedArrayProjection(layout, index));
            var path = new BoundNativeFixedArrayPathElement(
                parentPath.Variable, projections, layout.ElementType, span) {
                FieldPlace = parentPath.FieldPlace,
            };
            ValidatePrivateFixedArrayPath(reader, path,
                allowNestedFieldWriteBacking);
            return path;
        }

        throw new ModuleFormatException(
            "Private fixed-array element path is not representable by the selected runtime layout.");
    }

    private static void ValidatePrivateFixedArrayPath(ModuleReader reader,
        BoundNativeFixedArrayPathElement path,
        bool allowNestedFieldWriteBacking = false)
    {
        if (path.Projections.IsDefaultOrEmpty
            || !IsPrivateFixedArrayRootLocal(reader, path.Variable))
        {
            throw new ModuleFormatException(
                "Private fixed-array path root is invalid.");
        }

        var rootType = path.FieldPlace?.Type
            ?? reader.CurrentVariables[path.Variable].Type;
        var coverageProgram = CreatePrivate106CoverageProgram(reader);
        if (!NativeFixedArrayContract.MatchesPath(rootType,
                path.Projections, reader.Structures,
                reader.NativeFixedArrayTypes, out _, out var leafLayout)
            || leafLayout.ElementType != path.Type
            || path.Projections.Any(projection =>
                (!IsRevision100RuntimeArrayLayout(projection.Layout,
                        reader.Structures, reader.NativeFixedArrayTypes,
                        reader.NativePointerTypes,
                        reader.NativeFunctionPointerSignatures)
                    && !(reader.FormatVersion
                            == CheckedReferenceVersion
                        && IsPrivate106NestedArrayValueLocal(
                            projection.Layout.CarrierType,
                            reader.CurrentReturnType,
                            reader.CurrentVariables, coverageProgram))
                    && !(allowNestedFieldWriteBacking
                        && IsPrivateFixedArrayRootLocal(reader,
                            path.Variable)))
                || !IsRevision100IndexType(projection.Index.Type)
                || (projection.Index is BoundConstant
                    && !NativeFixedArrayContract.IsConstantIndexInRange(
                        projection.Index, projection.Layout.ElementCount))))
        {
            throw new ModuleFormatException(
                "Private fixed-array path does not match its retained rows.");
        }
    }

    private static bool IsPrivateNestedArrayFieldWritePlace(
        BoundPrivatePlace place, ModuleReader reader)
    {
        if (NativeFixedArrayTypes.IsType(place.Type))
        {
            return false;
        }

        var fieldCount = 0;
        while (place is BoundPrivateInlineFieldPlace field)
        {
            fieldCount++;
            place = field.Receiver;
        }

        var arrayCount = 0;
        while (place is BoundPrivateFixedArrayElementPlace element)
        {
            arrayCount++;
            place = element.ArrayPlace;
        }

        return fieldCount != 0 && arrayCount != 0
            && place is BoundPrivateLocalPlace local
            && IsPrivateFixedArrayRootLocal(reader, local.Variable);
    }

    private static bool IsPrivateNestedArrayFieldWritePlace(
        BoundPrivatePlace place, ModuleWriter writer)
    {
        if (NativeFixedArrayTypes.IsType(place.Type))
        {
            return false;
        }

        var fieldCount = 0;
        while (place is BoundPrivateInlineFieldPlace field)
        {
            fieldCount++;
            place = field.Receiver;
        }

        var arrayCount = 0;
        while (place is BoundPrivateFixedArrayElementPlace element)
        {
            arrayCount++;
            place = element.ArrayPlace;
        }

        return fieldCount != 0 && arrayCount != 0
            && place is BoundPrivateLocalPlace local
            && IsPrivateFixedArrayRootLocal(writer, local.Variable);
    }

    private static bool IsPrivateFixedArrayRootLocal(ModuleReader reader,
        int variable) =>
        variable >= reader.CurrentParameterCount
            + (reader.CurrentIsConstructor ? 1 : 0)
        && (uint)variable < (uint)reader.CurrentVariables.Length
        && !ManagedClassTypes.IsClass(
            reader.CurrentVariables[variable].Type);

    private static bool IsPrivateFixedArrayRootLocal(ModuleWriter writer,
        int variable) =>
        variable >= writer.CurrentParameterCount
            + (writer.CurrentIsConstructor ? 1 : 0)
        && (uint)variable < (uint)writer.CurrentVariables.Length
        && !ManagedClassTypes.IsClass(
            writer.CurrentVariables[variable].Type);

    private static PrivatePlaceInfo GetPrivateObjectFieldReceiver(
        BinaryReader reader, ref int remaining, int depth,
        Terrum.CodeAnalysis.Text.TextSpan span)
    {
        var receiver = GetExpression(reader, ref remaining, depth);
        if (!ManagedClassTypes.IsClass(receiver.Type))
        {
            throw new ModuleFormatException(
                "Private object-field receiver is not a nonoptional class handle.");
        }

        return new(3, receiver, null, receiver.Type,
            ManagedClassTypes.IsWritable(receiver.Type), false, -1, null);
    }

    private static (PrimitiveType Type, bool IsUnsafe, bool IsWritable)
        GetPrivateField(System.Collections.Generic.IReadOnlyList<BoundStruct>
            structures,
            System.Collections.Generic.IReadOnlyList<BoundClass> classes,
            PrimitiveType ownerType, int fieldOrdinal, bool writer)
    {
        var fields = ManagedClassTypes.IsClass(ownerType)
            ? (uint)ManagedClassTypes.ClassIndex(ownerType) < (uint)classes.Count
                ? classes[ManagedClassTypes.ClassIndex(ownerType)].Fields
                : []
            : (int)ownerType >= 100
                && (uint)((int)ownerType - 100) < (uint)structures.Count
                    ? structures[(int)ownerType - 100].Fields
                    : [];
        if ((uint)fieldOrdinal >= (uint)fields.Length)
        {
            throw writer
                ? new ModuleContractException(
                    "Private field place has an invalid owner or FieldDef ordinal.")
                : new ModuleFormatException(
                    "Private field place has an invalid owner or FieldDef ordinal.");
        }

        var field = fields[fieldOrdinal];
        return (field.Type, field.RequiresUnsafeCaller, field.IsMutable);
    }

    private static void ValidatePrivateLocalPlace(ModuleWriter writer,
        int variable, PrimitiveType type, bool writerArgument)
    {
        if ((uint)variable >= (uint)writer.CurrentVariables.Length
            || writer.CurrentVariables[variable].Type != type)
        {
            throw writerArgument
                ? new ModuleContractException(
                    "Private local place type differs from its declared slot.")
                : new ModuleFormatException(
                    "Private local place type differs from its declared slot.");
        }
    }

    private static bool TryGetPrivateReferenceType(PrimitiveType type,
        System.Collections.Generic.IReadOnlyList<
            CheckedReferenceTypeDefinition> referenceTypes,
        out CheckedReferenceTypeDefinition definition)
    {
        definition = new(PrimitiveType.Void, false, false, false, false);
        if (!CheckedReferenceTypes.IsType(type))
        {
            return false;
        }

        var index = CheckedReferenceTypes.Index(type);
        if ((uint)index >= (uint)referenceTypes.Count)
        {
            return false;
        }
        definition = referenceTypes[index];
        return true;
    }

    private static bool GetPrivateReferenceWriteAccess(PrimitiveType type,
        System.Collections.Generic.IReadOnlyList<
            CheckedReferenceTypeDefinition> referenceTypes) =>
        TryGetPrivateReferenceType(type, referenceTypes,
            out var definition) && definition.IsWritable;

}
