// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

public static partial class SemanticVerifier
{
    internal static bool IsPrivate106RawUnionArrayAggregate(
        PrimitiveType type, IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundNativeFixedArrayType> fixedArrays,
        IReadOnlyList<NativePointerTypeDefinition> pointerTypes,
        IReadOnlyList<NativeFunctionPointerSignatureDefinition>
            functionPointers,
        BoundProgram program) =>
        NativeFixedArrayContract.IsRootlessCopyAggregateWithFixedArrayFields(
            type, structures, fixedArrays, pointerTypes, functionPointers)
        && HasRawUnionReturnCoverageSubject(type, program)
        && ValueLayout.Size(type, structures,
            program.CheckedReferenceTypes,
            program.NativeLoweringAgreement)
            <= ValueLayout.MaximumStructureSize;

    private readonly record struct PrivateBodyPlaceFacts(
        PrimitiveType Type,
        bool IsWritable,
        bool HasScopedProvenance,
        BoundExpression? Address,
        BoundExpression? Descriptor,
        int RootVariable);

    private static PrivateBodyPlaceFacts ValidatePrivateBodyPlace(
        BoundPrivatePlace place, BoundFunction function, BoundProgram program,
        ref int remaining, int depth,
        bool allowNestedFieldWriteBacking = false)
    {
        Count(ref remaining, depth);
        if (place is null || !place.HasValidNestedExpressions)
        {
            throw InvalidEffect(default, "CheckedReference");
        }

        switch (place)
        {
            case BoundPrivateLocalPlace local:
                if ((uint)local.Variable >= (uint)function.Variables.Length
                    || function.Variables[local.Variable].Type != local.Type
                    || local.Type == PrimitiveType.Void)
                {
                    throw InvalidEffect(function.Span, "CheckedReference");
                }
                return new(local.Type, function.Variables[local.Variable].IsMutable,
                    false, new BoundRead(local.Variable, local.Type, function.Span),
                    null, local.Variable);
            case BoundPrivateInlineFieldPlace field:
            {
                var receiver = ValidatePrivateBodyPlace(field.Receiver,
                    function, program, ref remaining, depth + 1,
                    allowNestedFieldWriteBacking);
                var ownerType = field.OwnerType;
                if (ownerType != receiver.Type
                    || ManagedClassTypes.IsClass(ownerType)
                    || !TryGetPrivateBodyField(ownerType, field.FieldOrdinal,
                        program, function, out var definition)
                    || definition.Type != field.Type)
                {
                    throw InvalidEffect(function.Span, "CheckedReference");
                }
                var requiresUnsafe = RequiresUnsafeFieldAccess(program,
                    ownerType, definition);
                var receiverExpression = PrivateBodyPlaceExpression(
                    field.Receiver, function, program, function.Span,
                    ref remaining, depth + 1);
                return new(field.Type,
                    receiver.IsWritable && definition.IsMutable,
                    receiver.HasScopedProvenance,
                    new BoundFieldRead(receiverExpression,
                        checked((int)field.FieldOrdinal), field.Type,
                        function.Span) {
                        IsUnsafeAuthorized = requiresUnsafe,
                    },
                    null, receiver.RootVariable);
            }
            case BoundPrivateObjectFieldPlace field:
            {
                if (!ManagedClassTypes.IsClass(field.OwnerType)
                    || field.Receiver.Type != field.OwnerType
                    || !TryGetPrivateBodyField(field.OwnerType,
                        field.FieldOrdinal, program, function,
                        out var definition)
                    || definition.Type != field.Type)
                {
                    throw InvalidEffect(function.Span, "CheckedReference");
                }
                var requiresUnsafe = RequiresUnsafeFieldAccess(program,
                    field.OwnerType, definition);
                return new(field.Type,
                    ManagedClassTypes.IsWritable(field.OwnerType)
                        && definition.IsMutable,
                    false,
                    new BoundFieldRead(field.Receiver,
                        checked((int)field.FieldOrdinal), field.Type,
                        function.Span) {
                        IsUnsafeAuthorized = requiresUnsafe,
                    },
                    null, -1);
            }
            case BoundPrivateReferentPlace referent:
                if (!CheckedReferenceTypeContract.IsValidType(
                        referent.Descriptor.Type,
                        program.CheckedReferenceTypes))
                {
                    throw InvalidEffect(referent.Descriptor.Span,
                        "CheckedReference");
                }
                var descriptorType = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(referent.Descriptor.Type)];
                if (descriptorType.IsOptional
                    || descriptorType.IsGarbageCollected
                    || descriptorType.ReferentType != referent.Type)
                {
                    throw InvalidEffect(referent.Descriptor.Span,
                        "CheckedReference");
                }
                return new(referent.Type, descriptorType.IsWritable, true,
                    null, referent.Descriptor, -1);
            case BoundPrivateFixedArrayElementPlace element:
            {
                var parent = ValidatePrivateBodyPlace(element.ArrayPlace,
                    function, program, ref remaining, depth + 1,
                    allowNestedFieldWriteBacking);
                if (parent.Type != element.ArrayType
                    || !NativeFixedArrayContract.TryGetLayout(
                        element.ArrayType, program.Structures,
                        program.NativeFixedArrayTypes, out var layout)
                    || layout.ElementType != element.Type
                    || (!NativeFixedArrayContract.IsRevision100RuntimeArrayLayout(
                            layout, program.Structures,
                            program.NativeFixedArrayTypes,
                            program.NativePointerTypes,
                            program.NativeFunctionPointerSignatures)
                        && (parent.RootVariable < 0
                            || (!allowNestedFieldWriteBacking
                                && !IsPrivate106RawUnionArrayAggregate(
                                function.Variables[
                                    parent.RootVariable].Type,
                                program.Structures,
                                program.NativeFixedArrayTypes,
                                program.NativePointerTypes,
                                program.NativeFunctionPointerSignatures,
                                program))))
                    || !NativeFixedArrayContract.IsIndexType(element.Index.Type)
                    || (element.Index is BoundConstant
                        && !NativeFixedArrayContract.IsConstantIndexInRange(
                            element.Index, layout.ElementCount)))
                {
                    throw InvalidEffect(function.Span, "CheckedReference");
                }
                var expression = PrivateBodyPlaceExpression(place,
                    function, program, function.Span, ref remaining,
                    depth + 1);
                return new(element.Type,
                    parent.IsWritable
                        && NativeFixedArrayContract.AllowsElementWrites(layout),
                    parent.HasScopedProvenance, expression, null,
                    parent.RootVariable);
            }
            case BoundPrivateInvocationBufferElementPlace buffer:
                if ((uint)buffer.Buffer >= (uint)program.Buffers.Length
                    || !program.Buffers[buffer.Buffer].IsInvocationScoped
                    || program.Buffers[buffer.Buffer].InvocationProfileId
                        != program.InvocationProfileId
                    || program.Buffers[buffer.Buffer].ElementType
                        != buffer.Type
                    || !NativeFixedArrayContract.IsIndexType(
                        buffer.Index.Type))
                {
                    throw InvalidEffect(function.Span, "CheckedReference");
                }
                return new(buffer.Type,
                    program.Buffers[buffer.Buffer].IsWritable,
                    false, new BoundBufferRead(buffer.Buffer, buffer.Index,
                        buffer.Type, function.Span), null, -1);
            case BoundPrivateLexicalViewElementPlace view:
                if ((uint)view.Variable >= (uint)function.Variables.Length
                    || function.Variables[view.Variable].Type is not (
                        PrimitiveType.ReadonlyInt32BufferView
                        or PrimitiveType.MutableInt32BufferView)
                    || view.Type != PrimitiveType.Int32
                    || view.Index.Type != PrimitiveType.Int32)
                {
                    throw InvalidEffect(function.Span, "CheckedReference");
                }
                return new(view.Type,
                    function.Variables[view.Variable].Type
                        == PrimitiveType.MutableInt32BufferView,
                    true, new BoundViewRead(view.Variable, view.Index,
                        function.Span), null, view.Variable);
            default:
                throw InvalidEffect(function.Span, "CheckedReference");
        }
    }

    private static bool TryGetPrivateBodyField(PrimitiveType ownerType,
        uint fieldOrdinal, BoundProgram program, BoundFunction function,
        out BoundStructField field)
    {
        ImmutableArray<BoundStructField> fields;
        if (ManagedClassTypes.IsClass(ownerType))
        {
            var classIndex = ManagedClassTypes.ClassIndex(ownerType);
            fields = (uint)classIndex < (uint)program.Classes.Length
                ? program.Classes[classIndex].Fields : [];
        }
        else
        {
            var structureIndex = (int)ownerType - 100;
            fields = structureIndex >= 0
                && (uint)structureIndex < (uint)program.Structures.Length
                ? program.Structures[structureIndex].Fields : [];
        }
        if (fieldOrdinal > int.MaxValue
            || fieldOrdinal >= (uint)fields.Length
            || !CanAccessField(function, program, ownerType,
                (int)fieldOrdinal))
        {
            field = default!;
            return false;
        }
        field = fields[(int)fieldOrdinal];
        return true;
    }

    private static bool PrivatePlaceRequiresUnsafe(BoundPrivatePlace place,
        BoundProgram program) => place switch {
        BoundPrivateInlineFieldPlace field =>
            PrivateFieldRequiresUnsafe(field.OwnerType, field.FieldOrdinal,
                program)
            || PrivatePlaceRequiresUnsafe(field.Receiver, program),
        BoundPrivateObjectFieldPlace field =>
            PrivateFieldRequiresUnsafe(field.OwnerType, field.FieldOrdinal,
                program),
        BoundPrivateFixedArrayElementPlace element =>
            PrivatePlaceRequiresUnsafe(element.ArrayPlace, program),
        _ => false,
    };

    private static bool PrivateFieldRequiresUnsafe(PrimitiveType ownerType,
        uint fieldOrdinal, BoundProgram program)
    {
        if (fieldOrdinal > int.MaxValue
            || !HasFields(program, ownerType)
            || fieldOrdinal >= (uint)Fields(program, ownerType).Length)
        {
            return false;
        }
        return RequiresUnsafeFieldAccess(program, ownerType,
            Fields(program, ownerType)[(int)fieldOrdinal]);
    }

    internal static BoundExpression PrivateBodyPlaceExpression(
        BoundPrivatePlace place, BoundFunction function, BoundProgram program,
        Terrum.CodeAnalysis.Text.TextSpan span, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        switch (place)
        {
            case BoundPrivateLocalPlace local:
                return new BoundRead(local.Variable, local.Type, span);
            case BoundPrivateInlineFieldPlace field:
            {
                if (!TryGetPrivateBodyField(field.OwnerType,
                        field.FieldOrdinal, program, function,
                        out var definition)
                    || definition.Type != field.Type)
                {
                    throw InvalidEffect(span, "CheckedReference");
                }
                return new BoundFieldRead(
                    PrivateBodyPlaceExpression(field.Receiver, function,
                        program, span, ref remaining, depth + 1),
                    checked((int)field.FieldOrdinal), field.Type, span) {
                    IsUnsafeAuthorized = RequiresUnsafeFieldAccess(program,
                        field.OwnerType, definition),
                };
            }
            case BoundPrivateObjectFieldPlace field:
            {
                if (!TryGetPrivateBodyField(field.OwnerType,
                        field.FieldOrdinal, program, function,
                        out var definition)
                    || definition.Type != field.Type)
                {
                    throw InvalidEffect(span, "CheckedReference");
                }
                return new BoundFieldRead(field.Receiver,
                    checked((int)field.FieldOrdinal), field.Type, span) {
                    IsUnsafeAuthorized = RequiresUnsafeFieldAccess(program,
                        field.OwnerType, definition),
                };
            }
            case BoundPrivateFixedArrayElementPlace element:
            {
                var parent = PrivateBodyPlaceExpression(element.ArrayPlace,
                    function, program, span, ref remaining, depth + 1);
                if (!NativeFixedArrayContract.TryGetLayout(
                        element.ArrayType, program.Structures,
                        program.NativeFixedArrayTypes, out var layout)
                    || !TryGetPrivateBodyPlaceRoot(parent, out var rootVariable))
                {
                    throw InvalidEffect(span, "CheckedReference");
                }
                if (parent is BoundRead)
                {
                    return new BoundNativeFixedArrayElement(rootVariable,
                        layout, element.Index, span);
                }
                if (parent is BoundFieldRead fieldPlace)
                {
                    return new BoundNativeFixedArrayElement(rootVariable,
                        layout, element.Index, span) {
                        FieldPlace = fieldPlace,
                    };
                }
                var projection = new BoundNativeFixedArrayProjection(layout,
                    element.Index);
                if (parent is BoundNativeFixedArrayElement parentElement)
                {
                    return new BoundNativeFixedArrayPathElement(rootVariable,
                        [new(parentElement.Layout, parentElement.Index),
                            projection], element.Type, span) {
                        FieldPlace = parentElement.FieldPlace,
                    };
                }
                if (parent is BoundNativeFixedArrayPathElement parentPath)
                {
                    return new BoundNativeFixedArrayPathElement(rootVariable,
                        [.. parentPath.Projections, projection],
                        element.Type, span) {
                        FieldPlace = parentPath.FieldPlace,
                    };
                }
                throw InvalidEffect(span, "CheckedReference");
            }
            case BoundPrivateInvocationBufferElementPlace buffer:
                return new BoundBufferRead(buffer.Buffer, buffer.Index,
                    buffer.Type, span);
            case BoundPrivateLexicalViewElementPlace view:
                return new BoundViewRead(view.Variable, view.Index, span);
            case BoundPrivateReferentPlace referent:
                return new BoundReferenceRead(referent.Descriptor,
                    referent.Type, span);
            default:
                throw InvalidEffect(span, "CheckedReference");
        }
    }

    internal static bool TryCreatePrivateNativeArrayPathWrite(
        BoundPrivatePlace destination, BoundExpression value,
        Terrum.CodeAnalysis.Text.TextSpan span, BoundFunction function,
        BoundProgram program, ref int remaining, int depth,
        out BoundNativeFixedArrayPathWrite write)
    {
        write = default!;
        var leafFields = new List<BoundNativeFixedArrayFieldProjection>();
        var current = destination;
        while (current is BoundPrivateInlineFieldPlace field)
        {
            Count(ref remaining, depth++);
            if (field.FieldOrdinal > int.MaxValue)
            {
                return false;
            }
            leafFields.Add(new BoundNativeFixedArrayFieldProjection(
                field.OwnerType, (int)field.FieldOrdinal));
            current = field.Receiver;
        }

        var reverseProjections
            = new List<BoundNativeFixedArrayProjection>();
        while (current is BoundPrivateFixedArrayElementPlace element)
        {
            Count(ref remaining, depth++);
            if (!NativeFixedArrayContract.TryGetLayout(element.ArrayType,
                    program.Structures, program.NativeFixedArrayTypes,
                    out var layout)
                || element.Type != layout.ElementType)
            {
                return false;
            }
            reverseProjections.Add(new(layout, element.Index));
            current = element.ArrayPlace;
        }
        if (reverseProjections.Count == 0)
        {
            return false;
        }

        int rootVariable;
        BoundExpression? fieldPlace;
        if (current is BoundPrivateLocalPlace local)
        {
            rootVariable = local.Variable;
            fieldPlace = null;
        }
        else if (current is BoundPrivateInlineFieldPlace)
        {
            fieldPlace = PrivateBodyPlaceExpression(current, function,
                program, span, ref remaining, depth);
            if (fieldPlace is not BoundFieldRead
                || !TryGetPrivateBodyPlaceRoot(fieldPlace,
                    out rootVariable))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        if ((uint)rootVariable >= (uint)function.Variables.Length
            || current.Type
                != reverseProjections[^1].Layout.CarrierType)
        {
            return false;
        }

        var projections = ImmutableArray.CreateBuilder<
            BoundNativeFixedArrayProjection>(reverseProjections.Count);
        for (var index = reverseProjections.Count - 1; index >= 0; index--)
        {
            projections.Add(reverseProjections[index]);
        }
        var orderedProjections = projections.MoveToImmutable();
        if ((fieldPlace?.Type ?? function.Variables[rootVariable].Type)
            != orderedProjections[0].Layout.CarrierType)
        {
            return false;
        }

        leafFields.Reverse();
        var orderedFields = leafFields.ToImmutableArray();
        var targetType = orderedProjections[^1].Layout.ElementType;
        foreach (var leafField in orderedFields)
        {
            Count(ref remaining, depth++);
            if (leafField.OwnerType != targetType
                || !HasFields(program, targetType)
                || (uint)leafField.Field
                    >= (uint)Fields(program, targetType).Length)
            {
                return false;
            }
            targetType = Fields(program, targetType)[leafField.Field].Type;
        }
        if (value.Type != targetType)
        {
            return false;
        }

        write = new BoundNativeFixedArrayPathWrite(rootVariable,
            orderedProjections, value, span) {
            FieldPlace = fieldPlace,
            LeafFields = orderedFields,
            IsUnsafeAuthorized = PrivatePlaceRequiresUnsafe(destination,
                program),
        };
        return true;
    }

    private static bool TryGetPrivateBodyPlaceRoot(
        BoundExpression place, out int rootVariable)
    {
        switch (place)
        {
            case BoundRead read:
                rootVariable = read.Variable;
                return true;
            case BoundFieldRead field:
                return TryGetFieldRootVariable(field, out rootVariable);
            case BoundNativeFixedArrayElement element:
                rootVariable = element.Variable;
                return true;
            case BoundNativeFixedArrayPathElement element:
                rootVariable = element.Variable;
                return true;
            default:
                rootVariable = -1;
                return false;
        }
    }

    private static bool IsPrivateNestedArrayFieldWritePlace(
        BoundPrivatePlace place, BoundFunction function)
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

        var firstLocal = function.ParameterCount
            + (function.IsConstructor ? 1 : 0);
        return fieldCount != 0 && arrayCount != 0
            && place is BoundPrivateLocalPlace local
            && local.Variable >= firstLocal
            && (uint)local.Variable < (uint)function.Variables.Length
            && !ManagedClassTypes.IsClass(
                function.Variables[local.Variable].Type);
    }

    private static bool EvaluatePrivateBodyPlaceOperands(
        BoundPrivatePlace place, InitializationState initialized,
        uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program,
        ref int remaining, int depth, FailureEdges? failures,
        out bool completes)
    {
        var expressions = place.NestedExpressions;
        for (var index = 0; index < expressions.Length; index++)
        {
            if (EvaluateExpression(expressions[index], initialized,
                    fieldMasks, declarations, function, program,
                    ref remaining, depth + index + 1, failures))
            {
                continue;
            }
            for (var unreachable = index + 1;
                unreachable < expressions.Length; unreachable++)
            {
                ValidateUnreachableExpression(expressions[unreachable],
                    initialized, fieldMasks, declarations, function,
                    program, ref remaining, depth + unreachable + 1);
            }
            completes = false;
            return false;
        }
        completes = true;
        return true;
    }

    private static void VerifyPrivateReferenceForm(
        BoundPrivateReferenceForm formation, InitializationState initialized,
        uint[] fieldMasks, HashSet<int> declarations, BoundFunction function,
        BoundProgram program, ref int remaining, int depth,
        FailureEdges? failures, bool statementCompound,
        BoundBinary? cleanupCounterStep)
    {
        var place = ValidatePrivateBodyPlace(formation.Place, function,
            program, ref remaining, depth + 1);
        if (formation.FormationKind is not (1 or 2)
            || !CheckedReferenceTypeContract.IsValidType(
                formation.Type, program.CheckedReferenceTypes))
        {
            throw InvalidEffect(formation.Span, "CheckedReference");
        }
        var reference = program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(formation.Type)];
        if (reference.IsOptional || reference.IsGarbageCollected
            || reference.ReferentType != place.Type
            || (reference.IsWritable && !place.IsWritable)
            || (formation.FormationKind == 1
                && place.HasScopedProvenance)
            || (formation.FormationKind == 2
                && !place.HasScopedProvenance))
        {
            throw InvalidEffect(formation.Span, "CheckedReference");
        }

        if (formation.FormationKind == 1 && !reference.IsInitialization
            && place.Address is not null)
        {
            Expression(new BoundReferenceFormation(place.Address,
                    formation.Type, formation.Span),
                initialized, fieldMasks, declarations, function, program,
                ref remaining, depth + 1, failures, statementCompound,
                cleanupCounterStep);
            return;
        }

        if (formation.FormationKind == 2 && place.Descriptor is not null)
        {
            if (!CheckedReferenceTypeContract.IsValidType(
                    place.Descriptor.Type, program.CheckedReferenceTypes))
            {
                throw InvalidEffect(formation.Span, "CheckedReference");
            }
            var source = program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(place.Descriptor.Type)];
            if (source.IsOptional || source.IsGarbageCollected
                || source.IsInitialization != reference.IsInitialization
                || source.ReferentType != place.Type
                || (reference.IsWritable && !source.IsWritable))
            {
                throw InvalidEffect(formation.Span, "CheckedReference");
            }
            if (!reference.IsInitialization)
            {
                Expression(new BoundReferenceReborrow(place.Descriptor,
                        formation.Type, formation.Span),
                    initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures, statementCompound,
                    cleanupCounterStep);
                return;
            }
        }

        if (formation.FormationKind == 2 && !reference.IsInitialization
            && place.Address is not null)
        {
            Expression(new BoundReferenceFormation(place.Address,
                    formation.Type, formation.Span),
                initialized, fieldMasks, declarations, function, program,
                ref remaining, depth + 1, failures, statementCompound,
                cleanupCounterStep);
            return;
        }

        if (!EvaluatePrivateBodyPlaceOperands(formation.Place,
                initialized, fieldMasks, declarations, function, program,
                ref remaining, depth + 1, failures, out var operandsComplete)
            || !operandsComplete)
        {
            return;
        }

        if (reference.IsInitialization)
        {
            var invalidInitializationAuthority
                = formation.FormationKind == 1
                    ? (formation.Place is BoundPrivateLocalPlace local
                        && !function.Variables[local.Variable]
                            .IsInitializationLocal)
                        || !PrivateBodyPlaceIsVacant(formation.Place,
                            place, initialized, fieldMasks, function,
                            program, ref remaining, depth + 1)
                    : place.Descriptor is null
                        || !CheckedReferenceTypeContract.IsValidType(
                            place.Descriptor.Type,
                            program.CheckedReferenceTypes)
                        || !program.CheckedReferenceTypes[
                            CheckedReferenceTypes.Index(
                                place.Descriptor.Type)]
                            .IsInitialization
                        || !PrivateBodyPlaceIsVacant(formation.Place,
                            place, initialized, fieldMasks, function,
                            program, ref remaining, depth + 1);
            if (invalidInitializationAuthority)
            {
                throw InvalidEffect(formation.Span, "CheckedReference");
            }
            if (initialized.Dependencies is not BackingDependencies dependencies)
            {
                throw InvalidEffect(formation.Span, "CheckedReference");
            }
            _ = ReferenceTargetsOfExpression(formation,
                dependencies, initialized,
                function, program, ref remaining, depth + 1, fieldMasks);
            _ = ReferenceObligationsOfExpression(formation,
                (BackingDependencies)initialized.Dependencies, initialized,
                function, program, ref remaining, depth + 1, fieldMasks);
        }
        else
        {
            throw InvalidEffect(formation.Span, "CheckedReference");
        }
    }

    private static void VerifyPrivatePlaceRead(
        BoundPrivatePlaceRead read, InitializationState initialized,
        uint[] fieldMasks, HashSet<int> declarations, BoundFunction function,
        BoundProgram program, ref int remaining, int depth,
        FailureEdges? failures, bool statementCompound,
        BoundBinary? cleanupCounterStep)
    {
        var place = ValidatePrivateBodyPlace(read.Place, function, program,
            ref remaining, depth + 1);
        if (read.UseKind is not (1 or 2 or 3)
            || read.Type != place.Type)
        {
            throw InvalidEffect(read.Span, "CheckedReference");
        }

        if (read.UseKind == 3)
        {
            if (!CheckedReferenceTypeContract.IsValidType(
                    read.Type, program.CheckedReferenceTypes))
            {
                throw InvalidEffect(read.Span, "CheckedReference");
            }
            var descriptorType = program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(read.Type)];
            if (descriptorType.IsOptional
                || descriptorType.IsGarbageCollected
                || read.Place is BoundPrivateReferentPlace
                || place.Address is null)
            {
                throw InvalidEffect(read.Span, "CheckedReference");
            }

            Expression(place.Address, initialized, fieldMasks, declarations,
                function, program, ref remaining, depth + 1, failures);
            if (place.RootVariable >= 0
                && ((uint)place.RootVariable >= (uint)initialized.Length
                    || !initialized[place.RootVariable]
                    || initialized.HasRetiredValue(place.RootVariable)
                    || (place.RootVariable >= function.ParameterCount
                        + (function.IsConstructor ? 1 : 0)
                        && !declarations.Contains(place.RootVariable))))
            {
                throw InvalidEffect(read.Span, "CheckedReference");
            }
            if (place.Descriptor is not null)
            {
                _ = ReferenceTargetsOfExpression(place.Descriptor,
                    initialized.Dependencies as BackingDependencies
                        ?? throw InvalidEffect(read.Span, "CheckedReference"),
                    initialized, function, program, ref remaining, depth + 1,
                    fieldMasks);
            }
            return;
        }

        if (read.UseKind == 2)
        {
            switch (read.Place)
            {
                case BoundPrivateLocalPlace local:
                    Expression(new BoundMove(local.Variable, local.Type,
                            read.Span),
                        initialized, fieldMasks, declarations, function,
                        program, ref remaining, depth + 1, failures,
                        statementCompound, cleanupCounterStep);
                    return;
                case BoundPrivateInlineFieldPlace field:
                    Expression(new BoundFieldMove(
                            (BoundFieldRead)(place.Address
                                ?? throw InvalidEffect(read.Span,
                                    "Ownership")),
                            field.Type, read.Span),
                        initialized, fieldMasks, declarations, function,
                        program, ref remaining, depth + 1, failures,
                        statementCompound, cleanupCounterStep);
                    return;
                default:
                    throw InvalidEffect(read.Span, "Ownership");
            }
        }

        if (read.Place is BoundPrivateReferentPlace referent)
        {
            if (!CheckedReferenceTypeContract.IsValidType(
                    referent.Descriptor.Type, program.CheckedReferenceTypes)
                || IsUnique(program, read.Type))
            {
                throw InvalidEffect(read.Span, "CheckedReference");
            }
            Expression(new BoundReferenceRead(referent.Descriptor,
                    read.Type, read.Span),
                initialized, fieldMasks, declarations, function, program,
                ref remaining, depth + 1, failures, statementCompound,
                cleanupCounterStep);
            return;
        }

        Expression(place.Address
                ?? throw InvalidEffect(read.Span, "CheckedReference"),
            initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, failures, statementCompound,
            cleanupCounterStep);
    }

    private static void VerifyPrivatePlaceInitializationTest(
        BoundPrivatePlaceIsInitialized test, InitializationState initialized,
        uint[] fieldMasks, HashSet<int> declarations, BoundFunction function,
        BoundProgram program, ref int remaining, int depth,
        FailureEdges? failures)
    {
        var place = ValidatePrivateBodyPlace(test.Place, function, program,
            ref remaining, depth + 1);
        if (test.Type != PrimitiveType.Bool)
        {
            throw InvalidEffect(test.Span, "CheckedReference");
        }
        if (!EvaluatePrivateBodyPlaceOperands(test.Place, initialized,
                fieldMasks, declarations, function, program, ref remaining,
                depth + 1, failures, out var operandsComplete)
            || !operandsComplete)
        {
            return;
        }
        _ = PrivateBodyPlaceIsInitialized(test.Place, place, test.Span,
            initialized,
            fieldMasks, declarations, function, program, ref remaining,
            depth + 1);
    }

    private static void VerifyPrivateInitializationAttenuation(
        BoundInitializationAttenuation attenuation,
        InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function,
        BoundProgram program, ref int remaining, int depth,
        FailureEdges? failures)
    {
        if (!CheckedReferenceTypeContract.IsValidType(
                attenuation.Descriptor.Type, program.CheckedReferenceTypes)
            || !CheckedReferenceTypeContract.IsValidType(
                attenuation.Type, program.CheckedReferenceTypes))
        {
            throw InvalidEffect(attenuation.Span, "CheckedReference");
        }
        var sourceType = program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(attenuation.Descriptor.Type)];
        var resultType = program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(attenuation.Type)];
        if (!sourceType.IsInitialization
            || sourceType.IsOptional || sourceType.IsGarbageCollected
            || resultType.IsInitialization || resultType.IsOptional
            || resultType.IsGarbageCollected
            || sourceType.ReferentType != resultType.ReferentType
            || sourceType.IsWritable != resultType.IsWritable
            || attenuation.Descriptor is not BoundPrivatePlaceRead {
                UseKind: 3,
            } borrowedDescriptor
            || borrowedDescriptor.Place is not (BoundPrivateLocalPlace
                or BoundPrivateInlineFieldPlace
                or BoundPrivateObjectFieldPlace))
        {
            throw InvalidEffect(attenuation.Span, "CheckedReference");
        }

        Expression(attenuation.Descriptor, initialized, fieldMasks,
            declarations, function, program, ref remaining, depth + 1,
            failures);
        if (initialized.Dependencies is not BackingDependencies dependencies)
        {
            throw InvalidEffect(attenuation.Span, "CheckedReference");
        }
        var targets = ReferenceTargetsOfExpression(
            attenuation.Descriptor, dependencies, initialized, function,
            program, ref remaining, depth + 1, fieldMasks);
        if (targets.Count == 0)
        {
            throw InvalidEffect(attenuation.Span, "CheckedReference");
        }
        foreach (var target in targets)
        {
            if (!PrivateReferenceTargetIsInitialized(target,
                    sourceType.ReferentType, attenuation.Span, initialized,
                    fieldMasks, function, program, ref remaining,
                    depth + 1))
            {
                throw InvalidEffect(attenuation.Span, "CheckedReference");
            }
        }

        var descriptorPlaces = ReferenceDescriptorPlacesOfExpression(
            attenuation.Descriptor, dependencies, initialized, function,
            program, ref remaining, depth + 1, fieldMasks);
        if (descriptorPlaces.Count != 1
            || initialized.HasConsumedInitializationAuthority(
                descriptorPlaces.Single()))
        {
            throw InvalidEffect(attenuation.Span, "CheckedReference");
        }
        initialized.ConsumeInitializationAuthority(
            descriptorPlaces.Single());
    }

    private static void VerifyPrivateCheckedStore(
        BoundPrivateCheckedStore store, InitializationState initialized,
        uint[] fieldMasks, HashSet<int> declarations, BoundFunction function,
        BoundProgram program, ref int remaining, int depth,
        FailureEdges? failures, bool statementCompound,
        BoundBinary? cleanupCounterStep)
    {
        if (store.Type != PrimitiveType.Void
            || store.Transition is not (1 or 2 or 3)
            || store.Value.Type != store.Destination.Type
            || !CheckedReferenceTypeContract.IsSupportedReferenceStoreValueType(
                store.Value.Type, program.Structures, program.Classes,
                program.EnumDefinitions.Length, program.CheckedReferenceTypes))
        {
            throw InvalidEffect(store.Span, "CheckedReference");
        }

        var allowNestedFieldWriteBacking = store.Transition == 2
            && IsPrivateNestedArrayFieldWritePlace(store.Destination,
                function);
        _ = ValidatePrivateBodyPlace(store.Destination, function, program,
            ref remaining, depth + 1, allowNestedFieldWriteBacking);
        var uniqueValue = IsUnique(program, store.Value.Type);
        if ((uniqueValue
                && store.Value is not (BoundMove or BoundFieldMove
                    or BoundCall))
            || (uniqueValue
                && CleanupMayFail(store.Value.Type, program)))
        {
            throw InvalidEffect(store.Span, "Ownership");
        }
        if (store.Transition == 2
            && TryCreatePrivateNativeArrayPathWrite(
                store.Destination, store.Value, store.Span, function,
                program, ref remaining, depth + 1, out var arrayPathWrite))
        {
            Expression(arrayPathWrite, initialized, fieldMasks,
                declarations, function, program, ref remaining, depth + 1,
                failures, statementCompound, cleanupCounterStep);
            return;
        }
        if (store.Destination is BoundPrivateLocalPlace local)
        {
            var variable = function.Variables[local.Variable];
            var authorized = store.Transition switch {
                1 => variable.IsInitializationLocal,
                2 => variable.IsMutable,
                3 => variable.IsInitializationLocal && variable.IsMutable,
                _ => false,
            };
            if (!authorized)
            {
                throw InvalidEffect(store.Span, "CheckedReference");
            }

            if (!EvaluateExpression(store.Value, initialized, fieldMasks,
                    declarations, function, program, ref remaining,
                    depth + 1, failures,
                    statementCompound: statementCompound,
                    cleanupCounterStep: cleanupCounterStep))
            {
                return;
            }
            var localVacant = initialized.IsVacant(local.Variable)
                && !initialized.HasPartialValue(local.Variable);
            var localLive = initialized[local.Variable]
                && !initialized.HasPartialValue(local.Variable)
                && !initialized.HasRetiredValue(local.Variable);
            var localPossibilities
                = initialized.Possibilities(local.Variable);
            var onlyLiveOrVacant = localPossibilities != 0
                && (localPossibilities & ~7) == 0
                && !initialized.HasPartialValue(local.Variable)
                && !initialized.IsNativeOutputReserved(local.Variable);
            if ((store.Transition == 1 && !localVacant)
                || (store.Transition == 2 && !localLive)
                || (store.Transition == 3 && !onlyLiveOrVacant))
            {
                throw InvalidEffect(store.Span, "CheckedReference");
            }
            if (store.Transition != 1
                && (localPossibilities & 2) != 0)
            {
                CheckBackingInvalidation(local.Variable, initialized,
                    store.Span);
                CheckReferencePlaceInvalidation(local.Variable,
                    StorageProjection.WholeObject, initialized, store.Span);
            }
            CommitPrivateWholeObjectStore(local.Variable,
                StorageProjection.WholeObject, store.Value, initialized,
                fieldMasks, function, program, ref remaining, depth + 1,
                store.Span);
            return;
        }

        if (store.Transition == 2
            && store.Destination is BoundPrivateInlineFieldPlace
                or BoundPrivateObjectFieldPlace)
        {
            var fieldPlace = PrivateBodyPlaceExpression(
                store.Destination, function, program, store.Span,
                ref remaining, depth + 1) as BoundFieldRead
                ?? throw InvalidEffect(store.Span, "CheckedReference");
            if (!TryGetFieldProjectionPath(fieldPlace, function, program,
                    out var fieldRoot, out var fieldPath))
            {
                throw InvalidEffect(store.Span, "CheckedReference");
            }
            var segments = fieldPath.Segments;
            _ = Statement(new BoundFieldAssignment(fieldRoot,
                    segments[0].Field, store.Value, store.Span) {
                    NestedFields = [.. segments.Skip(1)
                        .Select(static segment => segment.Field)],
                },
                initialized, fieldMasks, declarations, function, program,
                ref remaining, depth + 1, failures: failures,
                cleanupCounterStep: cleanupCounterStep);
            return;
        }

        if (store.Transition == 2
            && store.Destination
                is BoundPrivateFixedArrayElementPlace arrayPlace)
        {
            var layout = NativeFixedArrayTypes.IsType(arrayPlace.ArrayType)
                && NativeFixedArrayTypes.Index(arrayPlace.ArrayType)
                    < program.NativeFixedArrayTypes.Length
                ? program.NativeFixedArrayTypes[
                    NativeFixedArrayTypes.Index(arrayPlace.ArrayType)]
                : default;
            if (!NativeFixedArrayContract.TryGetLayout(arrayPlace.ArrayType,
                    program.Structures, program.NativeFixedArrayTypes,
                    out var arrayLayout)
                || arrayLayout.ElementType != arrayPlace.Type
                || layout.ElementType != arrayLayout.ElementType)
            {
                throw InvalidEffect(store.Span, "CheckedReference");
            }
            var address = PrivateBodyPlaceExpression(store.Destination,
                function, program, store.Span, ref remaining, depth + 1);
            if (address is not BoundNativeFixedArrayElement element)
            {
                throw InvalidEffect(store.Span, "CheckedReference");
            }
            Expression(new BoundNativeFixedArrayWrite(element.Variable,
                    arrayLayout, element.Index, store.Value, store.Span) {
                    FieldPlace = element.FieldPlace,
                },
                initialized, fieldMasks, declarations, function, program,
                ref remaining, depth + 1, failures, statementCompound,
                cleanupCounterStep);
            return;
        }

        if (store.Transition == 2
            && store.Destination
                is BoundPrivateInvocationBufferElementPlace bufferPlace)
        {
            _ = Statement(new BoundBufferAssignment(bufferPlace.Buffer,
                    bufferPlace.Index, store.Value, store.Span),
                initialized, fieldMasks, declarations, function, program,
                ref remaining, depth + 1, failures: failures,
                cleanupCounterStep: cleanupCounterStep);
            return;
        }

        if (store.Transition == 2
            && store.Destination
                is BoundPrivateLexicalViewElementPlace viewPlace)
        {
            _ = Statement(new BoundViewSet(viewPlace.Variable,
                    viewPlace.Index, store.Value, store.Span),
                initialized, fieldMasks, declarations, function, program,
                ref remaining, depth + 1, failures: failures,
                cleanupCounterStep: cleanupCounterStep);
            return;
        }

        if (store.Transition == 2
            && store.Destination is BoundPrivateReferentPlace referent)
        {
            if (!CheckedReferenceTypeContract.IsValidType(
                    referent.Descriptor.Type, program.CheckedReferenceTypes)
                || !program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(referent.Descriptor.Type)]
                    .IsWritable
                || program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(referent.Descriptor.Type)]
                    is { IsInitialization: true }
                        or { IsOptional: true }
                        or { IsGarbageCollected: true })
            {
                throw InvalidEffect(store.Span, "CheckedReference");
            }
            Expression(new BoundReferenceWrite(referent.Descriptor,
                    store.Value, store.Span),
                initialized, fieldMasks, declarations, function, program,
                ref remaining, depth + 1, failures,
                statementCompound: statementCompound,
                cleanupCounterStep: cleanupCounterStep);
            return;
        }

        if (store.Destination is not BoundPrivateReferentPlace initPlace
            || initialized.Dependencies is not BackingDependencies initDependencies
            || !CheckedReferenceTypeContract.IsValidType(
                initPlace.Descriptor.Type, program.CheckedReferenceTypes))
        {
            throw InvalidEffect(store.Span, "CheckedReference");
        }
        var destinationType = program.CheckedReferenceTypes[
            CheckedReferenceTypes.Index(initPlace.Descriptor.Type)];
        if (destinationType.IsOptional || destinationType.IsGarbageCollected
            || (store.Transition is 1 or 3) != destinationType.IsInitialization
            || (store.Transition == 3 && !destinationType.IsWritable)
            || (store.Transition == 2
                && (!destinationType.IsWritable
                    || destinationType.IsInitialization)))
        {
            throw InvalidEffect(store.Span, "CheckedReference");
        }

        if (!EvaluatePrivateBodyPlaceOperands(store.Destination,
                initialized, fieldMasks, declarations, function, program,
                ref remaining, depth + 1, failures, out var destinationCompletes)
            || !destinationCompletes)
        {
            ValidateUnreachableExpression(store.Value, initialized,
                fieldMasks, declarations, function, program,
                ref remaining, depth + 1);
            return;
        }

        var targets = ReferenceTargetsOfExpression(initPlace.Descriptor,
            initDependencies, initialized, function, program, ref remaining,
            depth + 1, fieldMasks);
        if (targets.Count != 1)
        {
            throw InvalidEffect(store.Span, "CheckedReference");
        }

        var target = targets.Single();
        var storageProjection = StorageProjection.WholeObject;
        if (target.Kind == ProvenanceAnchorKind.ReferencePlace
            && target.Storage is { } storage
            && storage.Projection.Kind is StorageProjectionKind.WholeObject
                or StorageProjectionKind.InlineFieldPath)
        {
            storageProjection = storage.Projection;
        }
        else if (target.Kind == ProvenanceAnchorKind.IncomingReference
            && store.Transition is 1 or 3)
        {
            storageProjection = StorageProjection.WholeObject;
        }
        else
        {
            throw InvalidEffect(store.Span, "CheckedReference");
        }

        if (!EvaluateExpression(store.Value, initialized, fieldMasks,
                declarations, function, program, ref remaining,
                depth + 1, failures,
                statementCompound: statementCompound,
                cleanupCounterStep: cleanupCounterStep))
        {
            return;
        }

        var vacant = PrivateReferenceTargetIsVacant(target,
            destinationType.ReferentType, initialized, fieldMasks, function,
            program);
        var live = PrivateReferenceTargetIsInitialized(target,
            destinationType.ReferentType, store.Span, initialized,
            fieldMasks, function, program, ref remaining, depth + 1);
        var transitionCanCommit = store.Transition != 3
            || PrivateReferenceTargetCanInitializeOrReplace(target,
                initialized, fieldMasks, function, program);
        if ((store.Transition == 1 && !vacant)
            || (store.Transition == 2 && !live)
            || !transitionCanCommit
            || (store.Transition == 3
                && (initialized.HasPartialValue(target.Identity)
                    || initialized.IsNativeOutputReserved(target.Identity)
                    || CleanupMayFail(destinationType.ReferentType,
                        program))))
        {
            throw InvalidEffect(store.Span, "CheckedReference");
        }

        if (target.Kind == ProvenanceAnchorKind.IncomingReference
            && !IsImplicitReferenceScalarType(
                destinationType.ReferentType))
        {
            throw InvalidEffect(store.Span, "Ownership");
        }

        if ((store.Transition == 2 && live)
            || (store.Transition == 3
                && PrivateReferenceTargetMayBeLive(target, initialized,
                    fieldMasks, program)))
        {
            if (target.Kind == ProvenanceAnchorKind.IncomingReference)
            {
                if (IsUnique(program, destinationType.ReferentType)
                    || CleanupMayFail(destinationType.ReferentType,
                        program))
                {
                    throw InvalidEffect(store.Span, "Ownership");
                }
            }
            else if (target.Storage is not StorageKey replacementStorage)
            {
                throw InvalidEffect(store.Span, "CheckedReference");
            }
            else
            {
                CheckBackingInvalidation(target.Identity, initialized,
                    store.Span);
                CheckReferencePlaceInvalidation(target.Identity,
                    replacementStorage.Projection, initialized,
                    store.Span);
            }
        }
        if (target.Kind == ProvenanceAnchorKind.IncomingReference)
        {
            initialized.MarkPrivateReferentInitialized(target);
        }
        else
        {
            CommitPrivateWholeObjectStore(target.Identity,
                storageProjection, store.Value, initialized, fieldMasks,
                function, program, ref remaining, depth + 1, store.Span);
        }
    }

    private static void CommitPrivateWholeObjectStore(int variable,
        StorageProjection projection, BoundExpression value,
        InitializationState initialized, uint[] fieldMasks,
        BoundFunction function, BoundProgram program, ref int remaining,
        int depth, Terrum.CodeAnalysis.Text.TextSpan span)
    {
        Count(ref remaining, depth);
        var matchesDestinationType
            = projection.Kind == StorageProjectionKind.WholeObject
                ? (uint)variable < (uint)function.Variables.Length
                    && function.Variables[variable].Type == value.Type
                : TryGetReferencePlaceType(variable, projection,
                    function, program, out var actualPlaceType)
                    && actualPlaceType == value.Type;
        if ((projection.Kind is not (StorageProjectionKind.WholeObject
                or StorageProjectionKind.InlineFieldPath))
            || (uint)variable >= (uint)initialized.Length
            || !matchesDestinationType)
        {
            throw InvalidEffect(span, "CheckedReference");
        }

        if (projection.Kind == StorageProjectionKind.InlineFieldPath)
        {
            if (projection.FieldPath.Segments.IsEmpty)
            {
                throw InvalidEffect(span, "CheckedReference");
            }
            if (projection.FieldPath.Segments.Length == 1)
            {
                var segment = projection.FieldPath.Segments[0];
                if (segment.OwnerType != function.Variables[variable].Type
                    || (uint)segment.Field
                        >= (uint)Fields(program, segment.OwnerType).Length)
                {
                    throw InvalidEffect(span, "CheckedReference");
                }
                fieldMasks[variable] |= 1u << segment.Field;
                initialized.SetPossibleFieldMask(variable,
                    initialized.PossibleFieldMask(variable)
                        | (1u << segment.Field));
                if (fieldMasks[variable]
                    == FullMask(program, function.Variables[variable].Type))
                {
                    initialized[variable] = true;
                }
                else if (IsUnique(program,
                             function.Variables[variable].Type)
                    || CarriesRawUnionPayloadFacts(program,
                        function.Variables[variable].Type)
                    || NativeFixedArrayContract
                        .HasInlineFixedArrayFieldStorage(
                            function.Variables[variable].Type,
                            program.Structures,
                            program.NativeFixedArrayTypes))
                {
                    initialized.MarkPartialValue(variable);
                }
            }
            else
            {
                SetNestedFieldLiveness(variable, projection.FieldPath,
                    true, initialized, fieldMasks, function, program,
                    span);
            }
            if (CheckedReferenceTypeContract.IsValidType(
                value.Type, program.CheckedReferenceTypes))
            {
                if (initialized.Dependencies
                    is not BackingDependencies dependencies)
                {
                    throw InvalidEffect(span, "CheckedReference");
                }
                var storedReference
                    = ReferenceFieldValueOfDescriptorExpression(value,
                        dependencies, initialized, function, program,
                        ref remaining, depth + 1, fieldMasks);
                if (!storedReference.IsAvailable
                    || storedReference.Targets.Count == 0)
                {
                    throw InvalidEffect(span, "CheckedReference");
                }
                dependencies.SetReferenceFieldValue(variable, projection,
                    storedReference);
            }
            if (TokenSpanContract.IsDependent(value.Type, program.Structures)
                && initialized.Dependencies is BackingDependencies fieldBacking)
            {
                SetBackingFieldValuesFromExpression(variable, projection,
                    value.Type, value, fieldBacking, function, program,
                    ref remaining, depth + 1);
            }
            return;
        }

        initialized.ResetNestedOwnerStates(variable);
        if (HasFields(program, value.Type))
        {
            fieldMasks[variable] = FullMask(program, value.Type);
            initialized.SetPossibleFieldMask(variable,
                fieldMasks[variable]);
        }
        initialized.MarkAssigned(variable);

        if (value is not BoundMove
            && CheckedReferenceTypeContract.IsValidType(
                value.Type, program.CheckedReferenceTypes))
        {
            if (initialized.Dependencies
                is not BackingDependencies dependencies)
            {
                throw InvalidEffect(span, "CheckedReference");
            }
            dependencies.SetReferenceTargets(variable,
                ReferenceTargetsOfExpression(value, dependencies,
                    initialized, function, program, ref remaining,
                    depth + 1, fieldMasks));
            dependencies.SetReferenceObligations(variable,
                ReferenceObligationsOfExpression(value, dependencies,
                    initialized, function, program, ref remaining,
                    depth + 1, fieldMasks));
        }
        if (TokenSpanContract.IsDependent(value.Type, program.Structures)
            && initialized.Dependencies is BackingDependencies wholeObjectBacking)
        {
            wholeObjectBacking.SetBackingSources(variable,
                BackingOf(value, wholeObjectBacking, program,
                    ref remaining, depth + 1));
        }
    }

    private static bool PrivateBodyPlaceIsInitialized(
        BoundPrivatePlace source, PrivateBodyPlaceFacts place,
        Terrum.CodeAnalysis.Text.TextSpan span,
        InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function,
        BoundProgram program, ref int remaining, int depth)
    {
        if (source is BoundPrivateLocalPlace local)
        {
            if (initialized.IsNativeOutputReserved(local.Variable))
            {
                throw InvalidEffect(span, "NativeImport");
            }
            return (uint)local.Variable < (uint)initialized.Length
                && initialized[local.Variable]
                && !initialized.HasRetiredValue(local.Variable)
                && !initialized.HasPartialValue(local.Variable)
                && (local.Variable < function.ParameterCount
                    + (function.IsConstructor ? 1 : 0)
                    || declarations.Contains(local.Variable))
                && !initialized.IsNativeOutputReserved(local.Variable);
        }

        if (place.Descriptor is not null
            && initialized.Dependencies is BackingDependencies dependencies)
        {
            var targets = ReferenceTargetsOfExpression(place.Descriptor,
                dependencies, initialized, function, program,
                ref remaining, depth + 1, fieldMasks);
            if (targets.Count == 0)
            {
                return false;
            }
            foreach (var target in targets)
            {
                if (!PrivateReferenceTargetIsInitialized(target,
                        place.Type, span, initialized, fieldMasks, function,
                        program, ref remaining, depth + 1))
                {
                    return false;
                }
            }
            return true;
        }

        if (place.Address is BoundBufferRead or BoundViewRead)
        {
            return place.Address is BoundBufferRead buffer
                ? (uint)buffer.Buffer < (uint)program.Buffers.Length
                    && program.Buffers[buffer.Buffer].IsInvocationScoped
                : place.Address is BoundViewRead view
                    && (uint)view.Variable < (uint)initialized.Length
                    && initialized[view.Variable]
                    && !initialized.HasRetiredValue(view.Variable);
        }

        if (place.Address is not null
            && TryGetNormalBackingClassPlace(place.Address, function,
                program, out var normalVariable, out var normalProjection))
        {
            return ReferenceTargetPlaceInitialized(normalVariable,
                normalProjection, initialized, fieldMasks, function, program);
        }
        if (place.Address is not null
            && TryGetReferencePlaceStorage(place.Address, function, program,
                out var variable, out var projection))
        {
            return ReferenceTargetPlaceInitialized(variable, projection,
                initialized, fieldMasks, function, program);
        }
        return false;
    }

    private static bool PrivateReferenceTargetIsInitialized(
        ProvenanceAnchor target, PrimitiveType type,
        Terrum.CodeAnalysis.Text.TextSpan span,
        InitializationState initialized, uint[] fieldMasks,
        BoundFunction function, BoundProgram program,
        ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if ((target.Kind is ProvenanceAnchorKind.ReferencePlace
                or ProvenanceAnchorKind.NormalBackingPlace)
            && target.Storage is StorageKey storage)
        {
            if (initialized.IsNativeOutputReserved(target.Identity))
            {
                throw InvalidEffect(span, "NativeImport");
            }
            return ReferenceTargetPlaceInitialized(target.Identity,
                storage.Projection, initialized, fieldMasks, function,
                program);
        }
        if (target.Kind == ProvenanceAnchorKind.IncomingReference)
        {
            if ((uint)target.Identity >= (uint)(function.ParameterCount
                    + (function.IsConstructor ? 1 : 0))
                || !CheckedReferenceTypeContract.IsValidType(
                    function.Variables[target.Identity].Type,
                    program.CheckedReferenceTypes))
            {
                return false;
            }
            var incoming = program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(
                    function.Variables[target.Identity].Type)];
            return incoming.IsInitialization
                ? initialized.IsPrivateReferentInitialized(target)
                : initialized[target.Identity]
                    && !initialized.IsNativeOutputReserved(target.Identity);
        }
        if (target.Kind is ProvenanceAnchorKind.IncomingReferencePlace
            or ProvenanceAnchorKind.IncomingReferenceField)
        {
            if ((uint)target.Identity
                    < (uint)(function.ParameterCount
                        + (function.IsConstructor ? 1 : 0))
                && CheckedReferenceTypeContract.IsValidType(
                    function.Variables[target.Identity].Type,
                    program.CheckedReferenceTypes)
                && program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(
                        function.Variables[target.Identity].Type)]
                    .IsInitialization)
            {
                return initialized.IsPrivateReferentInitialized(target);
            }
            return (uint)target.Identity
                    < (uint)(function.ParameterCount
                        + (function.IsConstructor ? 1 : 0))
                && initialized[target.Identity]
                && !initialized.IsNativeOutputReserved(target.Identity)
                && !function.Variables[target.Identity].IsOut
                && !function.Variables[target.Identity].IsInit;
        }
        if (target.Kind is ProvenanceAnchorKind.IncomingReference
            or ProvenanceAnchorKind.IncomingReferenceContents)
        {
            return (uint)target.Identity
                    < (uint)(function.ParameterCount
                        + (function.IsConstructor ? 1 : 0))
                && initialized[target.Identity]
                && !initialized.IsNativeOutputReserved(target.Identity);
        }
        _ = type;
        return false;
    }

    private static bool PrivateBodyPlaceIsVacant(
        BoundPrivatePlace source, PrivateBodyPlaceFacts place,
        InitializationState initialized, uint[] fieldMasks,
        BoundFunction function, BoundProgram program,
        ref int remaining, int depth)
    {
        if (source is BoundPrivateLocalPlace local)
        {
            return (uint)local.Variable < (uint)initialized.Length
                && initialized.IsVacant(local.Variable)
                && !initialized.HasPartialValue(local.Variable)
                && !function.Variables[local.Variable].IsOut
                && !initialized.HasNativeOutputState(local.Variable);
        }
        if (place.Descriptor is not null
            && initialized.Dependencies is BackingDependencies dependencies)
        {
            var targets = ReferenceTargetsOfExpression(place.Descriptor,
                dependencies, initialized, function, program,
                ref remaining, depth + 1, fieldMasks);
            if (targets.Count == 0)
            {
                return false;
            }
            foreach (var target in targets)
            {
                if (!PrivateReferenceTargetIsVacant(target, place.Type,
                        initialized, fieldMasks, function, program))
                {
                    return false;
                }
            }
            return true;
        }
        if (place.Address is BoundFieldRead field
            && TryGetFieldProjectionPath(field, function, program,
                out var fieldRoot, out var fieldPath)
            && (uint)fieldRoot < (uint)function.Variables.Length
            && function.Variables[fieldRoot].IsInitializationLocal)
        {
            return PrivateStoragePlaceIsVacant(fieldRoot,
                StorageProjection.ForFieldPath(fieldPath), initialized,
                fieldMasks, program);
        }
        if (place.Address is not null
            && TryGetReferencePlaceStorage(place.Address, function, program,
                out var variable, out var projection))
        {
            return PrivateStoragePlaceIsVacant(variable, projection,
                initialized, fieldMasks, program);
        }
        return false;
    }

    private static bool PrivateReferenceTargetIsVacant(
        ProvenanceAnchor target, PrimitiveType type,
        InitializationState initialized, uint[] fieldMasks,
        BoundFunction function, BoundProgram program)
    {
        if (target.Kind == ProvenanceAnchorKind.IncomingReference)
        {
            return (uint)target.Identity
                    < (uint)(function.ParameterCount
                        + (function.IsConstructor ? 1 : 0))
                && CheckedReferenceTypeContract.IsValidType(
                    function.Variables[target.Identity].Type,
                    program.CheckedReferenceTypes)
                && program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(
                        function.Variables[target.Identity].Type)]
                    .IsInitialization
                && !initialized.IsPrivateReferentInitialized(target)
                && !initialized.IsNativeOutputReserved(target.Identity);
        }
        if ((target.Kind is ProvenanceAnchorKind.ReferencePlace
                or ProvenanceAnchorKind.NormalBackingPlace)
            && target.Storage is StorageKey storage)
        {
            return PrivateStoragePlaceIsVacant(target.Identity,
                storage.Projection, initialized, fieldMasks, program);
        }
        _ = type;
        return false;
    }

    private static bool PrivateReferenceTargetCanInitializeOrReplace(
        ProvenanceAnchor target, InitializationState initialized,
        uint[] fieldMasks, BoundFunction function, BoundProgram program)
    {
        if (target.Kind == ProvenanceAnchorKind.IncomingReference)
        {
            return (uint)target.Identity
                    < (uint)(function.ParameterCount
                        + (function.IsConstructor ? 1 : 0))
                && CheckedReferenceTypeContract.IsValidType(
                    function.Variables[target.Identity].Type,
                    program.CheckedReferenceTypes)
                && program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(
                        function.Variables[target.Identity].Type)]
                    .IsInitialization
                && !initialized.IsNativeOutputReserved(target.Identity);
        }
        if (target.Kind != ProvenanceAnchorKind.ReferencePlace
            || target.Storage is not StorageKey storage
            || (uint)target.Identity >= (uint)initialized.Length
            || initialized.IsNativeOutputReserved(target.Identity)
            || initialized.HasPartialValue(target.Identity))
        {
            return false;
        }

        if (storage.Projection.Kind == StorageProjectionKind.WholeObject)
        {
            var possibilities = initialized.Possibilities(target.Identity);
            return possibilities != 0 && (possibilities & ~7) == 0;
        }
        if (storage.Projection.Kind != StorageProjectionKind.InlineFieldPath
            || storage.Projection.FieldPath.Segments.IsEmpty
            || (uint)target.Identity >= (uint)fieldMasks.Length)
        {
            return false;
        }

        var leaf = storage.Projection.FieldPath.Segments[^1];
        var ownerPath = FieldPathPrefix(storage.Projection.FieldPath,
            storage.Projection.FieldPath.Segments.Length - 1);
        var (_, possible) = FieldPathOwnerMasks(target.Identity, ownerPath,
            leaf.OwnerType, initialized, fieldMasks, program);
        return possible == 0 || (possible & (1u << leaf.Field)) != 0;
    }

    private static bool PrivateReferenceTargetMayBeLive(
        ProvenanceAnchor target, InitializationState initialized,
        uint[] fieldMasks, BoundProgram program)
    {
        if (target.Kind == ProvenanceAnchorKind.IncomingReference)
        {
            return initialized.IsPrivateReferentInitialized(target);
        }
        if (target.Storage is not StorageKey storage
            || (uint)target.Identity >= (uint)initialized.Length)
        {
            return false;
        }
        if (storage.Projection.Kind == StorageProjectionKind.WholeObject)
        {
            return (initialized.Possibilities(target.Identity) & 2) != 0;
        }
        if (storage.Projection.Kind == StorageProjectionKind.InlineFieldPath
            && !storage.Projection.FieldPath.Segments.IsEmpty
            && (uint)target.Identity < (uint)fieldMasks.Length)
        {
            var leaf = storage.Projection.FieldPath.Segments[^1];
            var ownerPath = FieldPathPrefix(storage.Projection.FieldPath,
                storage.Projection.FieldPath.Segments.Length - 1);
            var (_, possible) = FieldPathOwnerMasks(target.Identity,
                ownerPath, leaf.OwnerType, initialized, fieldMasks,
                program);
            return (possible & (1u << leaf.Field)) != 0;
        }
        return false;
    }

    private static bool PrivateStoragePlaceIsVacant(int variable,
        StorageProjection projection, InitializationState initialized,
        uint[] fieldMasks, BoundProgram program)
    {
        if ((uint)variable >= (uint)initialized.Length
            || initialized.IsNativeOutputReserved(variable)
            || initialized.HasPartialValue(variable))
        {
            return false;
        }
        if (projection.Kind == StorageProjectionKind.WholeObject)
        {
            return initialized.IsVacant(variable);
        }
        if (projection.Kind != StorageProjectionKind.InlineFieldPath
            || projection.FieldPath.Segments.IsEmpty
            || (uint)variable >= (uint)fieldMasks.Length)
        {
            return false;
        }
        var leaf = projection.FieldPath.Segments[^1];
        var ownerPath = FieldPathPrefix(projection.FieldPath,
            projection.FieldPath.Segments.Length - 1);
        var (definite, possible) = FieldPathOwnerMasks(variable,
            ownerPath, leaf.OwnerType, initialized, fieldMasks, program);
        var fieldBit = 1u << leaf.Field;
        return (possible & fieldBit) == 0
            && (definite & fieldBit) == 0;
    }

    private static void VerifyPrivateReferenceMaterialize(
        BoundImplicitReferenceMaterialize materialize,
        InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function,
        BoundProgram program, ref int remaining, int depth,
        FailureEdges? failures, bool statementCompound,
        BoundBinary? cleanupCounterStep)
    {
        if (!CheckedReferenceTypeContract.IsValidType(
                materialize.Type, program.CheckedReferenceTypes)
            || !IsImplicitReferenceType(materialize.Value.Type, program)
            || IsUnique(program, materialize.Value.Type)
            || materialize.Type != GetCheckedReferenceType(
                program.CheckedReferenceTypes,
                materialize.Value.Type, writable: false,
                initialization: false)
            || materialize.BackingVariable
                < function.ParameterCount
                    + (function.IsConstructor ? 1 : 0)
            || (uint)materialize.BackingVariable
                >= (uint)function.Variables.Length)
        {
            throw InvalidEffect(materialize.Span, "Ownership");
        }

        var backing = function.Variables[materialize.BackingVariable];
        if (!backing.IsImplicitReferenceTemporary
            || backing.Type != materialize.Value.Type
            || !backing.IsMutable || backing.IsOut || backing.IsInit
            || !backing.IsInitializationLocal
            || !declarations.Contains(materialize.BackingVariable)
            || initialized.HasPartialValue(materialize.BackingVariable)
            || initialized.IsNativeOutputReserved(
                materialize.BackingVariable))
        {
            throw InvalidEffect(materialize.Span, "Ownership");
        }

        var valueCompletes = EvaluateExpression(materialize.Value,
            initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, failures,
            statementCompound: statementCompound,
            cleanupCounterStep: cleanupCounterStep);
        if (!valueCompletes)
        {
            return;
        }
        if (initialized[materialize.BackingVariable])
        {
            CheckBackingInvalidation(materialize.BackingVariable,
                initialized, materialize.Span);
            CheckReferencePlaceInvalidation(materialize.BackingVariable,
                StorageProjection.WholeObject, initialized,
                materialize.Span);
        }

        if (HasFields(program, backing.Type))
        {
            initialized.ResetNestedOwnerStates(materialize.BackingVariable);
            fieldMasks[materialize.BackingVariable]
                = FullMask(program, backing.Type);
            initialized.SetPossibleFieldMask(materialize.BackingVariable,
                fieldMasks[materialize.BackingVariable]);
        }
        initialized.MarkAssigned(materialize.BackingVariable);
        _ = declarations.Add(materialize.BackingVariable);

        if (CheckedReferenceTypeContract.IsValidType(backing.Type,
                program.CheckedReferenceTypes))
        {
            if (initialized.Dependencies is not BackingDependencies refs)
            {
                throw InvalidEffect(materialize.Span, "CheckedReference");
            }
            refs.SetReferenceTargets(materialize.BackingVariable,
                ReferenceTargetsOfExpression(materialize.Value, refs,
                    initialized, function, program, ref remaining,
                    depth + 1, fieldMasks));
            refs.SetReferenceObligations(materialize.BackingVariable,
                ReferenceObligationsOfExpression(materialize.Value, refs,
                    initialized, function, program, ref remaining,
                    depth + 1, fieldMasks));
        }
        if (TokenSpanContract.IsDependent(backing.Type, program.Structures)
            && initialized.Dependencies is BackingDependencies backingFacts)
        {
            backingFacts.SetBackingSources(materialize.BackingVariable,
                BackingOf(materialize.Value, backingFacts, program,
                    ref remaining, depth + 1));
        }
        if (initialized.Dependencies is BackingDependencies copyFacts)
        {
            SetCopyOriginValuesFromExpression(materialize.BackingVariable,
                StorageProjection.WholeObject, backing.Type,
                materialize.Value, copyFacts, function, program,
                ref remaining, depth + 1);
        }

        Expression(new BoundReferenceFormation(
                new BoundRead(materialize.BackingVariable, backing.Type,
                    materialize.Span),
                materialize.Type, materialize.Span),
            initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, failures,
            statementCompound, cleanupCounterStep);
    }

    private static PrimitiveType GetCheckedReferenceType(
        ImmutableArray<CheckedReferenceTypeDefinition> referenceTypes,
        PrimitiveType innerType, bool writable, bool initialization)
    {
        for (var index = 0; index < referenceTypes.Length; index++)
        {
            var candidate = referenceTypes[index];
            if (candidate.ReferentType == innerType
                && candidate.IsWritable == writable
                && candidate.IsInitialization == initialization
                && !candidate.IsGarbageCollected
                && !candidate.IsOptional)
            {
                return CheckedReferenceTypes.Type(index);
            }
        }
        return PrimitiveType.Void;
    }
    }
