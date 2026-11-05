// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Semantics;

/// <summary>Validates untrusted structured IR before a native backend consumes it.</summary>
public static partial class SemanticVerifier
{
    private const string FunctionIndexKey = "Terrum.FunctionIndex";
    private const string SourceSpanKey = "Terrum.SourceSpan";
    private const string InvalidEffectKey = "Terrum.InvalidEffect";
    // Share structural optional-presence facts with feasibility caches across flow-state clones.
    private const byte OptionalPresenceAnalyzed = 0x10;
    private const byte OptionalPresencePresent = 0x20;
    private const byte OptionalPresenceAbsent = 0x40;
    private const byte OutcomeEffectFact = 0x01;
    private const byte InitializationTestFact = 0x02;
    private const byte NativeOutputFlowAnalyzedFact = 0x01;
    private const byte NativeOutputFlowEffectFact = 0x02;
    private const byte AllFlowFacts = OutcomeEffectFact | InitializationTestFact;
    internal const int MaximumFunctions = 1024;
    internal const int MaximumVariables = 256;
    internal const int MaximumArguments = 64;
    internal const int MaximumDepth = 128;
    internal const int MaximumAnalysisVisits = 100_000;

    /// <summary>Checks types, control flow, slot mutation, definite assignment, and boundedness of a program. The node limit counts analysis visits, including repeated outcome and feasibility checks.</summary>
    /// <exception cref="ArgumentException">The program is malformed.</exception>
    /// <exception cref="NotSupportedException">The program exceeds an emitter resource limit.</exception>
    public static void Verify(BoundProgram program) =>
        Verify(program, nativeLoweringAgreement: null);

    /// <summary>Checks a program under the exact host-selected native lowering agreement.</summary>
    /// <param name="program">The bound source program.</param>
    /// <param name="nativeLoweringAgreement">The explicit host selection, or null for the existing default.</param>
    /// <exception cref="ArgumentException">The selected agreement does not match the program.</exception>
    public static void Verify(BoundProgram program, string? nativeLoweringAgreement)
    {
        ArgumentNullException.ThrowIfNull(program);
        ValidateNativeLoweringAgreement(program, nativeLoweringAgreement);
        _ = VerifyCore(program, requireEntryPoint: true);
    }

    /// <summary>Checks a fully defined library with no process entry point. Libraries cannot be emitted as executables without linking.</summary>
    /// <exception cref="ArgumentException">The library is malformed or contains an entry point.</exception>
    /// <exception cref="NotSupportedException">The library exceeds verification limits.</exception>
    public static void VerifyLibrary(BoundProgram program) =>
        VerifyLibrary(program, nativeLoweringAgreement: null);

    /// <summary>Checks a library under the exact host-selected native lowering agreement.</summary>
    public static void VerifyLibrary(BoundProgram program, string? nativeLoweringAgreement)
    {
        ArgumentNullException.ThrowIfNull(program);
        ValidateNativeLoweringAgreement(program, nativeLoweringAgreement);
        _ = VerifyCore(program, requireEntryPoint: false);
    }

    internal static void VerifyLibraryWithDirectNativeImports(BoundProgram program,
        string? nativeLoweringAgreement = null,
        IReadOnlySet<int>? duplicateNameViewIndexes = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        VerifyDirectNativeImports(program, requireEntryPoint: false,
            nativeLoweringAgreement, duplicateNameViewIndexes);
    }

    internal static void VerifyWithDirectNativeImports(BoundProgram program,
        string? nativeLoweringAgreement = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        VerifyDirectNativeImports(program, requireEntryPoint: true,
            nativeLoweringAgreement);
    }

    private static void VerifyDirectNativeImports(BoundProgram program,
        bool requireEntryPoint, string? nativeLoweringAgreement,
        IReadOnlySet<int>? duplicateNameViewIndexes = null)
    {
        ValidateNativeLoweringAgreement(program, nativeLoweringAgreement);
        if (program.Functions.Any(static function =>
            function.IsExternal && function.DirectNativeImport is null))
        {
            throw Invalid();
        }
        _ = VerifyCore(program, requireEntryPoint, allowExternal: true,
            duplicateNameViewIndexes: duplicateNameViewIndexes);
    }

    internal static void VerifyReferences(BoundProgram program)
        => VerifyReferences(program, nativeLoweringAgreement: null);

    internal static void VerifyReferences(
        BoundProgram program, string? nativeLoweringAgreement)
    {
        ArgumentNullException.ThrowIfNull(program);
        ValidateNativeLoweringAgreement(program, nativeLoweringAgreement);
        _ = VerifyCore(program, requireEntryPoint: true, allowExternal: true);
    }

    internal static void VerifyLibraryReferences(BoundProgram program)
        => VerifyLibraryReferences(program, nativeLoweringAgreement: null);

    internal static void VerifyLibraryReferences(
        BoundProgram program, string? nativeLoweringAgreement)
    {
        ArgumentNullException.ThrowIfNull(program);
        ValidateNativeLoweringAgreement(program, nativeLoweringAgreement);
        _ = VerifyCore(program, requireEntryPoint: false, allowExternal: true);
    }

    private static void ValidateNativeLoweringAgreement(
        BoundProgram program,
        string? nativeLoweringAgreement)
    {
        if (nativeLoweringAgreement is not
            (null or CompilationOptions.WindowsX64NormalBackingAgreement
                or CompilationOptions.WindowsX64RootlessReferencesAgreement))
        {
            throw new ArgumentException("The native lowering agreement is unsupported.",
                nameof(nativeLoweringAgreement));
        }
        if (!string.Equals(program.NativeLoweringAgreement,
            nativeLoweringAgreement, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The explicit native lowering agreement does not match the bound program.",
                nameof(nativeLoweringAgreement));
        }
    }

    private readonly record struct VerificationSummaries(
        ImmutableArray<BoundCopyOriginSummary?> CopyOrigin,
        ImmutableArray<ImmutableArray<byte>> RawUnionReturnCoverage);

    private sealed class RawUnionReturnCoverageBuilder(int size)
    {
        private byte[]? _intersection;
        internal bool HasValue => _intersection is not null;

        internal void Observe(ReadOnlySpan<byte> initializedBytes)
        {
            if (initializedBytes.Length != size)
            {
                throw Invalid();
            }

            if (_intersection is null)
            {
                _intersection = initializedBytes.ToArray();
                return;
            }

            for (var index = 0; index < _intersection.Length; index++)
            {
                _intersection[index] &= initializedBytes[index];
            }
        }

        internal ImmutableArray<byte> Build()
            => _intersection is null ? [] : [.. _intersection];
    }

    private static VerificationSummaries VerifyCore(
        BoundProgram program, bool requireEntryPoint, bool allowExternal = false,
        bool allowGenericProofDependencies = false,
        bool deriveCopyOriginSummaries = false,
        IReadOnlySet<int>? duplicateNameViewIndexes = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (program.Functions.IsDefault || (requireEntryPoint
            ? program.Functions.IsEmpty || program.EntryPoint < 0
                || program.EntryPoint >= program.Functions.Length
            : program.EntryPoint != -1))
        {
            throw Invalid();
        }
        if (program.RuntimePolicy is not (BoundRuntimePolicy.FailFast or BoundRuntimePolicy.CheckedExceptions))
        {
            throw Invalid();
        }
        if (program.Functions.Length > MaximumFunctions)
        {
            throw Limit();
        }
        if (program.NativeFunctionPointerSignatures.IsDefault
            || program.NativeFunctionPointerSignatures.Length > MaximumAnalysisVisits
            || !NativeFunctionPointerContract.IsValidTable(program.NativeFunctionPointerSignatures))
        {
            throw InvalidEffect(default, "NativeFunctionPointer");
        }
        if (!BoundEnumTypes.IsValidTable(program.EnumDefinitions))
        {
            throw Invalid();
        }
        if (!BoundGenericNominalTypeContract.IsValidDefinitionTable(
                program.GenericNominalTypeDefinitions)
            || !BoundGenericNominalTypeContract.IsValidCoreTaskSelection(
                program.CoreTaskType, program.GenericNominalTypeDefinitions)
            || !BoundGenericNominalTypeContract.IsValidInstanceTable(
                program.GenericNominalTypeInstances,
                program.GenericNominalTypeDefinitions))
        {
            throw InvalidEffect(default, "GenericNominalType");
        }
        var sourceGenericArgumentsValid = true;
        var sourceGenericArgumentsComplete =
            BoundTreeTraversal.VisitSourceGenericTypeArguments(program,
                type => sourceGenericArgumentsValid &=
                    IsValidType(type, false, program));
        if (!IsValidSourceGenericTypeTables(program)
            || !sourceGenericArgumentsComplete
            || !sourceGenericArgumentsValid)
        {
            throw InvalidEffect(default, "SourceGenericType");
        }
        if (!CoreExceptionIdentitySelectionContract.IsValidTable(
            program.CoreExceptionIdentitySelections))
        {
            throw InvalidEffect(default, "CoreExceptionIdentity");
        }
        if (program.NativeCoreRegistration is NativeCoreRegistrationSet coreRegistration
            && !BoundEnumTypes.ContainsRegistration(program.EnumDefinitions, coreRegistration))
        {
            throw Invalid();
        }
        if (program.LayoutCoreRegistration is LayoutCoreRegistrationSet layoutRegistration
            && (!BoundEnumTypes.MatchesLayoutRegistration(
                    program.EnumDefinitions, layoutRegistration)
                || (program.NativeCoreRegistration is NativeCoreRegistrationSet nativeRegistration
                    && !ReferenceEquals(nativeRegistration.ModuleBindings,
                        layoutRegistration.ModuleBindings))))
        {
            throw Invalid();
        }
        if (!NativePointerTypeContract.IsValidTable(program.NativePointerTypes, program.Structures,
                program.NativeFunctionPointerSignatures,
                program.NativeFixedArrayTypes))
        {
            throw InvalidEffect(default, "NativePointer");
        }
        if (!NativeFixedArrayContract.MatchesProgram(program))
        {
            throw InvalidEffect(default, "NativeImport");
        }
        if (!CheckedReferenceTypeContract.IsValidTable(program.CheckedReferenceTypes)
            || program.CheckedReferenceTypes.Any(referenceType =>
                referenceType is null || !ValidType(referenceType.ReferentType, false, program)))
        {
            throw InvalidEffect(default, "CheckedReference");
        }
        if (!NativeFunctionPointerContract.IsValidNativeImportContractTable(
            program.NativeFunctionPointerSignatures, program.NativePointerTypes,
            program.Structures))
        {
            throw InvalidEffect(default, "NativeImport");
        }
        VerifyNativePointerUnsafeScopes(program);
        var inspectFunctionManagedNodes = program.Classes.IsDefaultOrEmpty;
        var usesManagedFunctionNodes = false;
        foreach (var function in program.Functions)
        {
            if (function is null || function.Variables.IsDefault)
            {
                throw Invalid();
            }
            if (function.NativeCallableConvention is NativeCallingConvention nativeCallableConvention
                && !NativeFunctionPointerContract.IsKnownConvention(nativeCallableConvention))
            {
                throw InvalidEffect(function.Span, "NativeFunctionPointer");
            }
            VerifyDependencyContracts(function, program);
            if (function.NativeVTableMethod is not null
                ? !NativeVTableCallContract.IsValid(function, program)
                : function.Intrinsic == BoundIntrinsic.NativeVTableCall)
            {
                throw InvalidEffect(function.Span, "NativeImport");
            }
            if (function.NativeFunctionPointerType is PrimitiveType functionPointerType
                && !NativeFunctionPointerContract.MatchesFunction(function, functionPointerType,
                    program.NativeFunctionPointerSignatures))
            {
                throw InvalidEffect(function.Span, "NativeFunctionPointer");
            }
            if (function.IsOperator)
            {
                VerifyOperatorFunction(function, program);
            }
            if (function.Variables.Length > MaximumVariables)
            {
                throw Limit();
            }
            if (inspectFunctionManagedNodes && !usesManagedFunctionNodes
                && (function.Intrinsic is >= BoundIntrinsic.NodeTryCreate and <= BoundIntrinsic.ClassTryCreate
                    || ManagedClassTypes.IsManaged(function.ReturnType)))
            {
                usesManagedFunctionNodes = true;
            }
            var firstParameter = function.IsConstructor ? 1 : 0;
            for (var variableIndex = 0; variableIndex < function.Variables.Length; variableIndex++)
            {
                var variable = function.Variables[variableIndex] ?? throw Invalid();
                var isParameter = variableIndex >= firstParameter
                    && variableIndex - firstParameter < function.ParameterCount;
                if (variable.IsImplicitRef
                    && (!isParameter || function.IsExternal || variable.IsOut || variable.IsInit
                        || variable.IsInitializationLocal || !IsImplicitReferenceType(variable.Type, program)))
                {
                    throw InvalidEffect(variable.Span, "Ownership");
                }
                if (variable.IsImplicitReferenceTemporary
                    && (isParameter || function.IsExternal || variable.IsOut || variable.IsInit
                        || variable.IsImplicitRef || variable.IsInitializationLocal || variable.IsMutable
                        || !IsImplicitReferenceType(variable.Type, program)))
                {
                    throw InvalidEffect(variable.Span, "Ownership");
                }
                if (inspectFunctionManagedNodes && !usesManagedFunctionNodes
                    && ManagedClassTypes.IsManaged(variable.Type))
                {
                    usesManagedFunctionNodes = true;
                }
            }
        }
        VerifyDirectNativeImports(program);
        if (program.Classes.IsDefault || program.Classes.Length > MaximumVariables)
        {
            throw Invalid();
        }

        var usesManagedNodes = !program.Classes.IsEmpty || usesManagedFunctionNodes;
        // Isolated definition-site proofs may use private managed declarations, but executable
        // and persisted programs must still enforce the public external-call boundary.
        if (usesManagedNodes && !allowGenericProofDependencies && program.Functions.Any(function =>
            function.IsExternal && !ManagedExportShape.Supported(function, program.Structures)))
        {
            throw InvalidEffect(default, "Ownership");
        }
        if (program.Structures.IsDefault || program.Structures.Length > MaximumVariables)
        {
            throw Invalid();
        }
        if (!BoundInterfaceContract.IsValidTable(program.Interfaces,
            type => ValidType(type, false, program)))
        {
            throw Invalid();
        }
        if (program.Buffers.IsDefault || program.Buffers.Length > MaximumVariables)
        {
            throw Invalid();
        }
        if (program.InvocationProfileId is not null
            && (!program.RequiresSourceGrant
                || (!program.Buffers.IsEmpty
                    && !InvocationProfiles.IsBufferSet(program.Buffers, program.InvocationProfileId))))
        {
            throw Invalid();
        }
        var bufferNames = new HashSet<string>(program.Buffers.Length, StringComparer.Ordinal);
        long bufferWords = 0;
        foreach (var buffer in program.Buffers)
        {
            if (buffer is null || string.IsNullOrWhiteSpace(buffer.Name) || !bufferNames.Add(buffer.Name)
                || buffer.ElementType is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Char16 or PrimitiveType.Bool)
                || buffer.Values.IsDefault
                || (buffer.InvocationProfileId is not null
                    && buffer.InvocationProfileId != program.InvocationProfileId)
                || (buffer.IsInvocationScoped && program.InvocationProfileId is null
                    && (buffer.Name != "Source" || buffer.ElementType != PrimitiveType.Char16 || buffer.IsWritable)))
            {
                throw Invalid();
            }
            bufferWords += buffer.Values.Length;
            if (bufferWords > 1_048_576)
            {
                throw Limit();
            }
            foreach (var value in buffer.Values)
            {
                if ((buffer.ElementType == PrimitiveType.Bool && value is not (0 or 1))
                    || (buffer.ElementType == PrimitiveType.Char16 && value is < 0 or > ushort.MaxValue))
                {
                    throw Invalid();
                }
            }
        }
        if (program.Buffers.Any(static buffer => buffer.IsInvocationScoped) && !program.RequiresSourceGrant)
        {
            throw Invalid();
        }
        if (program.Structures.Any(static structure => structure is not null && structure.BlittableTargetRequirement != 0))
        {
            _ = FixedLayoutStorage.Validate(program.Structures,
                program.NativeFixedArrayTypes, duplicateNameViewIndexes);
        }
        var structNames = new Dictionary<string, bool>(program.Structures.Length,
            StringComparer.Ordinal);
        var layoutStates = new byte[program.Structures.Length];
        var alignments = new int[program.Structures.Length];
        var requiredAlignments = new int[program.Structures.Length];
        var layoutHeights = new int[program.Structures.Length];
        for (var index = 0; index < program.Structures.Length; index++)
        {
            VerifyLayout(index, 0);
        }
        var classNames = new HashSet<string>(program.Classes.Length, StringComparer.Ordinal);
        foreach (var @class in program.Classes)
        {
            if (@class is null || string.IsNullOrWhiteSpace(@class.Name)
                || @class.Name is NativeManagedNodeIntrinsics.TypeName or NativeManagedNodeIntrinsics.HeapName
                || !classNames.Add(@class.Name)
                || structNames.ContainsKey(@class.Name)
                || @class.Fields.IsDefaultOrEmpty || @class.Fields.Length > ManagedClassTypes.MaximumFields
                || @class.Size is < 8 or > ManagedClassTypes.MaximumPayload || (@class.Size & 7) != 0)
            {
                throw Invalid();
            }
            var offset = 0;
            var maximumClassFieldSize = program.NativeLoweringAgreement
                == CompilationOptions.WindowsX64NormalBackingAgreement
                ? ValueLayout.MaximumAddressStorageSize : ValueLayout.MaximumSize;
            var fieldNames = new HashSet<string>(@class.Fields.Length, StringComparer.Ordinal);
            foreach (var field in @class.Fields)
            {
                if (field is null || BoundGenericNominalTypeTypes.IsType(field.Type)
                    || !ValidType(field.Type, allowVoid: false, program)
                    || field.Type is PrimitiveType.OwnedInt32Buffer or PrimitiveType.ReadonlyInt32BufferView
                        or PrimitiveType.TokenSpan or PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
                        or PrimitiveType.MutableInt32BufferView
                    || NativeFixedArrayContract.IsCarrier(field.Type)
                    || NativeFixedArrayContract.HasMetadataOnlyArrayType(
                        field.Type, program.Structures,
                        program.NativeFixedArrayTypes,
                        program.NativePointerTypes,
                        program.NativeFunctionPointerSignatures,
                        program.Classes)
                    || IsUnique(program, field.Type))
                {
                    throw Invalid();
                }
                var width = ValueLayout.Size(field.Type, program.Structures,
                    program.CheckedReferenceTypes, program.NativeLoweringAgreement);
                if (width > int.MaxValue || field.Offset > int.MaxValue
                    || field.Size > int.MaxValue)
                {
                    throw Invalid();
                }
                var classFieldWidth = checked((int)width);
                var nested = !NativePointerTypes.IsType(field.Type) && !NativeFunctionPointerTypes.IsType(field.Type)
                    && !BoundEnumTypes.IsType(field.Type)
                    && (int)field.Type >= 100 && !ManagedClassTypes.IsClass(field.Type)
                    ? (int)field.Type - 100 : -1;
                var alignment = width == 0 ? 1 : nested >= 0 && nested < alignments.Length
                    ? alignments[nested] : ValueLayout.Alignment(field.Type,
                        program.Structures, program.CheckedReferenceTypes,
                        program.NativeLoweringAgreement,
                        program.NativeFixedArrayTypes);
                offset = (offset + alignment - 1) & -alignment;
                if (field.Size != (ulong)classFieldWidth
                    || field.Offset != (ulong)offset
                    || (field.IsMutable && !@class.IsMutable)
                    || classFieldWidth < 1
                    || classFieldWidth > maximumClassFieldSize
                    || offset + classFieldWidth > ManagedClassTypes.MaximumPayload
                    || string.IsNullOrWhiteSpace(field.Name) || !fieldNames.Add(field.Name)
                    || (field.IsPrivate && field.IsInternal))
                {
                    throw Invalid();
                }
                offset += classFieldWidth;
            }
            if (((offset + 7) & ~7) != @class.Size)
            {
                throw Invalid();
            }
        }
        if (!BoundGenericNominalTypeContract.IsValidArgumentTypes(program))
        {
            throw InvalidEffect(default, "GenericNominalType");
        }

        void VerifyLayout(int index, int depth)
        {
            if (depth >= MaximumDepth || layoutStates[index] == 1)
            {
                throw Invalid();
            }
            if (layoutStates[index] == 2)
            {
                if (depth + layoutHeights[index] > MaximumDepth)
                {
                    throw Invalid();
                }
                return;
            }
            layoutStates[index] = 1;
            var structure = program.Structures[index];
            if (structure is null || string.IsNullOrWhiteSpace(structure.Name)
                || structure.Name is NativeBufferIntrinsics.TypeName or NativeManagedNodeIntrinsics.TypeName
                    or NativeManagedNodeIntrinsics.HeapName
                || (structure.IsPrivate && structure.IsInternal)
                || structure.Fields.IsDefaultOrEmpty
                || structure.Size < (structure.IsTransparent ? 0UL : 1UL)
                || structure.Alignment < (structure.IsTransparent ? 0 : 1)
                || structure.Alignment > 8192
                || (structure.Alignment != 0
                    && (structure.Alignment & (structure.Alignment - 1)) != 0)
                || structure.RequiredAlignment is < 1 or > 8192
                || (structure.RequiredAlignment
                    & (structure.RequiredAlignment - 1)) != 0
                || (structure.IsSequential || structure.IsUnion
                    ? structure.Packing is not (1 or 2 or 4 or 8 or 16)
                    : structure.Packing != 0)
                || (structure.DeclaredAlignment is int declaredAlignment
                    && ((!structure.IsSequential && !structure.IsUnion)
                        || declaredAlignment is < 1 or > 8192
                        || (declaredAlignment
                            & (declaredAlignment - 1)) != 0)))
            {
                throw Invalid();
            }
            var isDuplicateNameView
                = (duplicateNameViewIndexes?.Contains(index) ?? false)
                    || structure.StandardTypeOrigin?.IsLookupRoot == false;
            if (structNames.TryGetValue(structure.Name,
                    out var existingDuplicateNameView))
            {
                if (!existingDuplicateNameView || !isDuplicateNameView)
                {
                    throw Invalid();
                }
            }
            else
            {
                structNames.Add(structure.Name, isDuplicateNameView);
            }
            ulong offset = 0;
            var alignment = structure.IsTransparent ? 0 : 1;
            var requiredAlignment = 1;
            var height = 1;
            var fieldNames = new HashSet<string>(structure.Fields.Length, StringComparer.Ordinal);
            foreach (var field in structure.Fields)
            {
                if (field is null || BoundGenericNominalTypeTypes.IsType(field.Type)
                    || !ValidType(field.Type, allowVoid: false, program))
                {
                    throw Invalid();
                }
                if (ManagedClassTypes.IsManaged(field.Type))
                {
                    throw InvalidEffect(default, "Ownership");
                }
                var nested = !NativePointerTypes.IsType(field.Type) && !NativeFunctionPointerTypes.IsType(field.Type)
                    && !BoundEnumTypes.IsType(field.Type)
                    && !NativeFixedArrayTypes.IsType(field.Type)
                    && (int)field.Type >= 100 && !ManagedClassTypes.IsClass(field.Type)
                    ? (int)field.Type - 100 : -1;
                if (nested >= program.Structures.Length)
                {
                    throw Invalid();
                }
                if (nested >= 0)
                {
                    VerifyLayout(nested, depth + 1);
                    if (program.Structures[nested].IsUnique && !structure.IsUnique)
                    {
                        throw Invalid();
                    }
                    height = Math.Max(height, layoutHeights[nested] + 1);
                }
                if (field.Type == PrimitiveType.OwnedInt32Buffer && !structure.IsUnique)
                {
                    throw Invalid();
                }
                var fixedArrayField = NativeFixedArrayTypes.IsType(field.Type);
                ulong width;
                int fieldAlignment;
                int fieldRequiredAlignment;
                if (fixedArrayField)
                {
                    var arrayIndex = NativeFixedArrayTypes.Index(field.Type);
                    if ((uint)arrayIndex >= (uint)program.NativeFixedArrayTypes.Length)
                    {
                        throw Invalid();
                    }

                    var array = program.NativeFixedArrayTypes[arrayIndex];
                    if (!NativeFixedArrayContract.TryGetLayout(field.Type,
                            array.ElementType, array.ElementCount,
                            program.Structures, program.NativeFixedArrayTypes,
                            out var layout)
                        || layout.Alignment > int.MaxValue
                        || layout.RequiredAlignment > int.MaxValue)
                    {
                        throw Invalid();
                    }

                    width = layout.ByteExtent;
                    fieldAlignment = (int)layout.Alignment;
                    fieldRequiredAlignment = (int)layout.RequiredAlignment;
                }
                else
                {
                    width = ValueLayout.Size(field.Type, program.Structures,
                        program.CheckedReferenceTypes,
                        program.NativeLoweringAgreement);
                    fieldAlignment = nested >= 0 ? alignments[nested]
                        : ValueLayout.Alignment(field.Type,
                            program.Structures, program.CheckedReferenceTypes,
                            program.NativeLoweringAgreement,
                            program.NativeFixedArrayTypes);
                    fieldRequiredAlignment = nested >= 0
                        ? requiredAlignments[nested]
                        : ValueLayout.RequiredAlignment(field.Type,
                            program.Structures, program.NativeFixedArrayTypes);
                }
                var fieldPlacementAlignment = fieldAlignment;
                if ((structure.IsSequential || structure.IsUnion)
                    && width != 0)
                {
                    fieldPlacementAlignment = Math.Max(
                        Math.Min(fieldAlignment, structure.Packing),
                        fieldRequiredAlignment);
                }
                var alignedOffset = structure.IsTransparent || structure.IsUnion
                    ? 0UL : width == 0 ? offset
                    : AlignLayout(offset, fieldPlacementAlignment);
                if (alignedOffset > ulong.MaxValue - width)
                {
                    throw Invalid();
                }
                var fieldEnd = alignedOffset + width;
                if (string.IsNullOrWhiteSpace(field.Name) || !fieldNames.Add(field.Name)
                    || (field.IsPrivate && field.IsInternal)
                    || (width == 0 && !fixedArrayField)
                    || field.Size != width || field.Offset != alignedOffset)
                {
                    throw Invalid();
                }
                offset = structure.IsUnion
                    ? Math.Max(offset, width) : fieldEnd;
                if (structure.IsTransparent)
                {
                    alignment = fieldAlignment;
                }
                else if (width != 0)
                {
                    alignment = Math.Max(alignment,
                        fieldPlacementAlignment);
                }
                requiredAlignment = Math.Max(requiredAlignment,
                    fieldRequiredAlignment);
            }
            if (structure.DeclaredAlignment is int explicitAlignment)
            {
                if (explicitAlignment < alignment)
                {
                    throw Invalid();
                }
                alignment = explicitAlignment;
                requiredAlignment = Math.Max(requiredAlignment,
                    explicitAlignment);
            }
            if (!structure.IsTransparent && offset == 0)
            {
                offset = 1;
            }
            var size = structure.IsTransparent ? offset
                : AlignLayout(offset, alignment);
            if (size != structure.Size || structure.Alignment != alignment
                || structure.RequiredAlignment != requiredAlignment)
            {
                throw Invalid();
            }
            if (structure.IsTransparent && (structure.IsUnique || structure.Fields.Length != 1
                || (structure.BlittableTargetRequirement == 0
                    ? PrimitiveTypeFacts.ScalarWidth(
                        structure.Fields[0].Type) <= 0
                    : !NativeFixedArrayContract.IsSupportedElementType(
                        structure.Fields[0].Type, program.Structures,
                        program.NativeFixedArrayTypes))
                || structure.Fields[0].Offset != 0 || structure.Fields[0].Size != structure.Size))
            {
                throw Invalid();
            }
            var isSupportedFixedArrayElement = NativeFixedArrayContract.IsSupportedElementType(
                (PrimitiveType)(100 + index), program.Structures,
                program.NativeFixedArrayTypes);
            if (structure.Size > ValueLayout.MaximumSize
                && structure.BlittableTargetRequirement == 0
                && !isSupportedFixedArrayElement
                && program.NativeLoweringAgreement
                    != CompilationOptions.WindowsX64NormalBackingAgreement)
            {
                throw Invalid();
            }
            if (structure.IsSequential && (!structure.IsInternal || structure.IsUnique
                || structure.IsTransparent || structure.Fields.Any(static field => !field.IsPrivate))
                && structure.BlittableTargetRequirement == 0
                && !isSupportedFixedArrayElement)
            {
                throw Invalid();
            }
            alignments[index] = alignment;
            requiredAlignments[index] = requiredAlignment;
            layoutHeights[index] = height;
            layoutStates[index] = 2;
        }

        var remaining = MaximumAnalysisVisits;
        var names = new HashSet<string>(program.Functions.Length, StringComparer.Ordinal);
        Dictionary<string, int>? ownerTypes = null;
        Dictionary<string, int>? classIndexes = null;
        int[]? classConstructorCounts = null;
        int[]? classFactoryCounts = null;
        Dictionary<string, string>? cleanupOwners = null;
        HashSet<string>?[]? fieldNamesByOwner = null;
        if (!allowGenericProofDependencies && !program.Classes.IsEmpty)
        {
            classIndexes = new Dictionary<string, int>(program.Classes.Length, StringComparer.Ordinal);
            classConstructorCounts = new int[program.Classes.Length];
            classFactoryCounts = new int[program.Classes.Length];
            for (var classIndex = 0; classIndex < program.Classes.Length; classIndex++)
            {
                classIndexes.Add(program.Classes[classIndex].Name, classIndex);
            }
        }
        for (var i = 0; i < program.Functions.Length; i++)
        {
            var function = program.Functions[i];
            if (function is null || string.IsNullOrEmpty(function.Name) || !names.Add(function.Name)
                || (function.IsPrivate && function.IsInternal)
                || function.ReturnType is PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
                || (function.ReturnType == PrimitiveType.NativeRawPointer
                    && !function.RequiresUnsafeCaller)
                || function.ReturnType == PrimitiveType.NativeStorageAddress
                || NativeFixedArrayContract.IsCarrier(function.ReturnType)
                || HasUnsupportedMetadataOnlyArrayType(
                    function.ReturnType, program)
                || (function.NativeFunctionPointerType is PrimitiveType functionPointerType
                    && NativeFixedArrayContract.HasMetadataOnlyArrayType(
                        functionPointerType, program.Structures,
                        program.NativeFixedArrayTypes, program.NativePointerTypes,
                        program.NativeFunctionPointerSignatures))
                || !ValidType(function.ReturnType, allowVoid: true, program)
                || (BoundGenericNominalTypeTypes.IsType(function.ReturnType)
                    && !function.IsExternal)
                || function.Variables.IsDefault
                || function.ParameterCount < 0 || function.ParameterCount > function.Variables.Length
                || function.Body is null)
            {
                throw new ArgumentException(
                    $"Invalid bound program function '{function?.Name}' (return={function?.ReturnType}, private={function?.IsPrivate}, internal={function?.IsInternal}, external={function?.IsExternal}, variables={function?.Variables.Length}, parameters={function?.ParameterCount}).",
                    nameof(program));
            }
            if (function.Variables.Length > MaximumVariables || function.ParameterCount > MaximumArguments)
            {
                throw Limit();
            }
            if (function.Intrinsic is BoundIntrinsic.None or BoundIntrinsic.ClassTryCreate)
            {
                for (var variableIndex = 0; variableIndex < function.Variables.Length; variableIndex++)
                {
                    var variable = function.Variables[variableIndex];
                    if (variable is null || string.IsNullOrEmpty(variable.Name)
                        || (variable.Type == PrimitiveType.NativeStorageAddress
                            && DirectNativeImportContract.OutputForParameter(function, variableIndex) is null
                            && NativeVTableCallContract.OutputForParameter(function, variableIndex) is null
                            && DirectNativeImportContract.ParameterForParameter(function, variableIndex) is null)
                        || (NativeFixedArrayContract.IsCarrier(variable.Type)
                            && (function.IsExternal || function.IndexedBody is not null
                                || variableIndex < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                                || variable.IsOut || variable.IsInit
                                || !NativeFixedArrayContract.TryGetLayout(
                                    variable.Type, program.Structures,
                                    program.NativeFixedArrayTypes,
                                    out var fixedArrayLayout)
                                || fixedArrayLayout.CarrierType != variable.Type
                                || (variable.IsMutable
                                    && !NativeFixedArrayContract.AllowsMutableLocal(fixedArrayLayout))
                                || !NativeFixedArrayContract.MatchesLayout(fixedArrayLayout, program)
                                || !NativeFixedArrayContract.MatchesProgram(program)))
                                || (!NativeFixedArrayContract.IsCarrier(variable.Type)
                                    && HasUnsupportedMetadataOnlyArrayType(
                                        variable.Type, program)
                                    && !IsSupportedFixedArrayFieldAggregateVariable(
                                        variableIndex, variable.Type,
                                        function, program))
                                || (variable.Type is PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
                            && (!function.IsExternal || variableIndex >= function.ParameterCount)
                                    && !(function.RequiresUnsafeCaller
                                        && variableIndex < function.ParameterCount + (function.IsConstructor ? 1 : 0))
                                    && !DirectNativeImportContract.IsReadonlyStringParameter(
                                        function, variableIndex, variable.Type))
                        || (variable.Type is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView
                            ? variableIndex < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                                || variable.IsMutable != (variable.Type == PrimitiveType.MutableInt32BufferView)
                                || variable.IsOut || function.IsExternal
                            : !ValidType(variable.Type,
                                    allowVoid: variableIndex >= function.ParameterCount
                                        + (function.IsConstructor ? 1 : 0), program)
                                || (BoundGenericNominalTypeTypes.IsType(variable.Type)
                                    && !function.IsExternal)))
                    {
                        throw Invalid();
                    }
                    if (variable.IsInitializationLocal && (variableIndex < function.ParameterCount
                            + (function.IsConstructor ? 1 : 0) || variable.IsOut || variable.IsInit))
                    {
                        throw Invalid();
                    }
                    if ((variable.IsInit && (!variable.IsOut
                        || (variable.Type is not (PrimitiveType.Bool or PrimitiveType.Char8
                            or PrimitiveType.Char16 or PrimitiveType.Char32 or PrimitiveType.Rune
                            or PrimitiveType.Int8 or PrimitiveType.Int16 or PrimitiveType.Int32
                            or PrimitiveType.UInt8 or PrimitiveType.UInt16 or PrimitiveType.UInt32
                            or PrimitiveType.Int64 or PrimitiveType.UInt64
                            or PrimitiveType.Float32 or PrimitiveType.Float64
                            or PrimitiveType.OwnedInt32Buffer)
                            && !ManagedClassTypes.IsManaged(variable.Type)
                            && ((int)variable.Type < 100 || (int)variable.Type >= ManagedClassTypes.First))
                        || (ManagedClassTypes.IsManaged(variable.Type)
                            && !function.IsPrivate && !function.IsInternal)))
                        || (variable.IsOut && (variableIndex >= function.ParameterCount + (function.IsConstructor ? 1 : 0)
                            || (function.ReturnType != PrimitiveType.Bool
                                && !variable.IsInit
                                && variable.Type is not (PrimitiveType.Bool
                                    or PrimitiveType.Char8 or PrimitiveType.Char16 or PrimitiveType.Char32
                                    or PrimitiveType.Rune or PrimitiveType.Int8 or PrimitiveType.Int16
                                    or PrimitiveType.Int32 or PrimitiveType.UInt8 or PrimitiveType.UInt16
                                    or PrimitiveType.UInt32
                                    or PrimitiveType.Int64 or PrimitiveType.UInt64
                                    or PrimitiveType.Float32 or PrimitiveType.Float64)
                                && !IsCopyStruct(program, variable.Type)
                                && DirectNativeImportContract.OutputForParameter(function, variableIndex) is null
                                && NativeVTableCallContract.OutputForParameter(function, variableIndex) is null))))
                    {
                        throw Invalid();
                    }
                }
            }
            if (!function.Body.Statements.IsEmpty)
            {
                var hasGenericNominalExpression = false;
                if (!BoundTreeTraversal.VisitExpressions(function.Body,
                    expression => hasGenericNominalExpression
                        |= BoundGenericNominalTypeTypes.IsType(expression.Type))
                    || hasGenericNominalExpression)
                {
                    throw InvalidEffect(function.Span, "GenericNominalType");
                }
            }
            function.IndexedBody?.Verify(program, function, ref remaining);
            if (function.Intrinsic == BoundIntrinsic.ClassTryCreate)
            {
                VerifyClassFactory(program, function, i);
                if (classIndexes is not null && classFactoryCounts is not null
                    && function.DeclaringStruct is string factoryClassName
                    && classIndexes.TryGetValue(factoryClassName, out var factoryClassIndex))
                {
                    classFactoryCounts[factoryClassIndex]++;
                }
                continue;
            }
            if (function.Intrinsic == BoundIntrinsic.NativeVTableCall)
            {
                if (!NativeVTableCallContract.IsValid(function, program))
                {
                    throw InvalidEffect(function.Span, "NativeImport");
                }
                continue;
            }
            if (function.Intrinsic != BoundIntrinsic.None)
            {
                if (function.PropertyName is not null
                    || (!NativeBufferIntrinsics.Matches(function) && !NativeManagedNodeIntrinsics.Matches(function)))
                {
                    throw Invalid();
                }
                continue;
            }
            if (function.Name.StartsWith(NativeBufferIntrinsics.TypeName + ".", StringComparison.Ordinal)
                || function.Name.StartsWith(NativeManagedNodeIntrinsics.TypeName + ".", StringComparison.Ordinal)
                || function.Name.StartsWith(NativeManagedNodeIntrinsics.HeapName + ".", StringComparison.Ordinal))
            {
                throw Invalid();
            }
            if (function.IsExternal && (!allowExternal
                || function.Body.Statements.IsDefault || !function.Body.Statements.IsEmpty
                || function.Variables.Length != function.ParameterCount + (function.IsConstructor ? 1 : 0)
                || (!allowGenericProofDependencies && ((function.DirectNativeImport is null
                    && function.NativeVTableMethod is null
                    && (function.IsPrivate || function.IsInternal))
                    || ManagedClassTypes.IsManaged(function.ReturnType)
                    || function.Variables.Any(static variable => ManagedClassTypes.IsManaged(variable.Type))))))
            {
                throw Invalid();
            }
            if (function.ConstructionHomeIndex < -1)
            {
                throw Invalid();
            }
            if (function.ConstructionHomeIndex >= 0)
            {
                var constructionHomeIndex = function.ConstructionHomeIndex;
                var constructionTypeIndex
                    = (int)function.ReturnType - 100;
                if (function.IsConstructor || function.IsClassConstructor
                    || function.IsExternal || function.IsCleanup
                    || !function.IsPrivate || function.IsInternal
                    || !function.IsStatic
                    || function.DirectNativeImport is not null
                    || function.ModuleImport is not null
                    || function.NativeFunctionPointerType is not null
                    || constructionHomeIndex != function.ParameterCount
                    || constructionHomeIndex >= function.Variables.Length
                    || (uint)constructionTypeIndex
                        >= (uint)program.Structures.Length
                    || function.Variables[constructionHomeIndex].Type
                        != function.ReturnType
                    || !function.Variables[constructionHomeIndex].IsMutable
                    || !function.Variables[constructionHomeIndex]
                        .IsInitializationLocal
                    || function.Variables[constructionHomeIndex].IsOut
                    || function.Variables[constructionHomeIndex].IsInit
                    || function.Variables[constructionHomeIndex]
                        .IsImplicitRef
                    || program.Structures[constructionTypeIndex].IsUnique
                    || program.Structures[constructionTypeIndex].CleanupMethod
                        is not null
                    || program.Structures[constructionTypeIndex]
                        .BlittableTargetRequirement
                        != FixedLayoutStorage.WindowsX64TargetRequirement
                    || function.Body.Statements.IsDefaultOrEmpty
                    || function.Body.Statements[0]
                        is not BoundVacantLocal {
                            Variable: var vacantConstructionHome,
                        }
                    || vacantConstructionHome != constructionHomeIndex)
                {
                    throw Invalid();
                }
            }
            var declaringType = -1;
            if (function.DeclaringStruct is string declaringStruct)
            {
                if (ownerTypes is null)
                {
                    ownerTypes = new Dictionary<string, int>(
                        program.Structures.Length + program.Classes.Length, StringComparer.Ordinal);
                    for (var type = 0; type < program.Structures.Length; type++)
                    {
                        ownerTypes.Add(program.Structures[type].Name, type);
                    }
                    for (var type = 0; type < program.Classes.Length; type++)
                    {
                        ownerTypes.Add(program.Classes[type].Name, ManagedClassTypes.First + type);
                    }
                }
                declaringType = ownerTypes.TryGetValue(declaringStruct, out var ownerType)
                    ? ownerType : -1;
                var primitiveOwner = declaringType < 0
                    && Binder.TryPrimitiveType(declaringStruct, out var primitive)
                    ? primitive : (PrimitiveType?)null;
                if ((declaringType < 0 && primitiveOwner is null)
                    || !function.Name.StartsWith(declaringStruct + ".", StringComparison.Ordinal)
                    || (function.IsConstructor && function.IsStatic)
                    || (primitiveOwner is not null && (function.IsConstructor
                        || function.IsClassConstructor || function.IsCleanup
                        || (!function.IsStatic && !function.Variables.IsEmpty
                            && function.Variables[0].IsMutable)))
                    || (!function.IsStatic && (function.Variables.IsEmpty
                        || function.Variables[0].Name != "this"
                        || function.Variables[0].IsOut
                        || ManagedClassTypes.Readonly(function.Variables[0].Type)
                            != (primitiveOwner ?? (PrimitiveType)(declaringType >= ManagedClassTypes.First
                                ? declaringType : 100 + declaringType))
                        || (declaringType >= ManagedClassTypes.First
                            && ManagedClassTypes.IsWritable(function.Variables[0].Type)
                            && !program.Classes[declaringType - ManagedClassTypes.First].IsMutable)
                        || (declaringType >= ManagedClassTypes.First && !function.IsClassConstructor
                            && function.Variables[0].IsMutable)
                        || (!function.IsConstructor && function.ParameterCount < 1))))
                {
                    throw Invalid();
                }
                if (!function.IsStatic && !function.IsConstructor
                    && primitiveOwner is null && declaringType < ManagedClassTypes.First
                    && program.Structures[declaringType].IsUnique
                    && function.Variables[0].IsOut)
                {
                    throw Invalid();
                }
            }
            else if (function.IsConstructor
                || (function.IsPrivate
                    && function.ConstructionHomeIndex < 0))
            {
                throw Invalid();
            }
            if (function.PropertyName is string propertyName
                && (string.IsNullOrEmpty(propertyName) || function.DeclaringStruct is not string owner
                    || (function.Name != owner + ".#get:" + propertyName
                        && function.Name != owner + ".$producer.#get:" + propertyName)
                    || function.IsConstructor || function.IsClassConstructor
                    || function.IsCleanup || function.DoesNotReturn
                    || (function.IsStatic
                        ? declaringType < 0 || declaringType >= ManagedClassTypes.First
                            || program.Structures[declaringType].IsUnique
                            || function.ParameterCount != 0
                            || (function.ReturnType is not (PrimitiveType.Bool or PrimitiveType.Char16
                                or PrimitiveType.Char8 or PrimitiveType.Char32 or PrimitiveType.Rune
                                or PrimitiveType.Int8 or PrimitiveType.Int16 or PrimitiveType.Int32
                                or PrimitiveType.UInt8 or PrimitiveType.UInt16 or PrimitiveType.UInt32
                                or PrimitiveType.Int64 or PrimitiveType.UInt64
                                or PrimitiveType.Float32 or PrimitiveType.Float64)
                                && !IsCopyStruct(program, function.ReturnType))
                        : function.ParameterCount != 1 || function.Variables[0].IsMutable
                            || IsUnique(program, function.Variables[0].Type))
                    || !IsClassArgumentType(function.ReturnType, program.Structures,
                        program.EnumDefinitions.Length)
                    || (declaringType >= 0 && HasFieldName(declaringType, propertyName))))
            {
                throw Invalid();
            }
            if (function.IsConstructor && (function.DeclaringStruct is null
                || function.ReturnType != function.Variables[0].Type
                || (int)function.ReturnType < 100
                || function.ParameterCount >= function.Variables.Length))
            {
                throw Invalid();
            }
            if (function.IsClassConstructor && (function.IsConstructor || function.IsStatic
                || function.IsExternal || function.ReturnType != PrimitiveType.Void
                || function.DeclaringStruct is null || function.Variables.IsEmpty
                || !ManagedClassTypes.IsClass(function.Variables[0].Type)
                || function.Name != function.DeclaringStruct + ".#ctor"
                || function.ParameterCount < 1 || function.ParameterCount > MaximumArguments
                || function.Variables.Skip(1).Take(function.ParameterCount - 1)
                    .Any(variable => variable.IsOut || !IsClassArgumentType(variable.Type,
                        program.Structures, program.EnumDefinitions.Length))))
            {
                throw Invalid();
            }
            if (!function.IsClassConstructor && function.ClassConstructor >= 0)
            {
                throw Invalid();
            }
            if (function.IsCleanup && (function.DeclaringStruct is null || function.IsConstructor
                || function.IsStatic || function.IsPrivate || function.IsInternal
                || function.RequiresUnsafeCaller || function.DoesNotReturn || function.ReturnType != PrimitiveType.Void
                || function.ParameterCount != 1 || function.Variables[0].IsMutable))
            {
                throw Invalid();
            }
            if (function.IsCleanup && (function.Variables.IsEmpty
                || !IsUnique(program, function.Variables[0].Type)
                || program.Structures[(int)function.Variables[0].Type - 100].CleanupMethod != function.Name))
            {
                throw Invalid();
            }
            for (var variableIndex = function.IsConstructor ? 1 : 0;
                variableIndex < function.ParameterCount + (function.IsConstructor ? 1 : 0); variableIndex++)
            {
                if (IsUnique(program, function.Variables[variableIndex].Type)
                    && variableIndex == 0 && function.DeclaringStruct is not null && !function.IsStatic)
                {
                    continue;
                }
                if (IsUnique(program, function.Variables[variableIndex].Type)
                    && function.IsCleanup)
                {
                    throw Invalid();
                }
            }
            if (classIndexes is not null && classConstructorCounts is not null
                && function.IsClassConstructor && function.DeclaringStruct is string constructorClassName
                && classIndexes.TryGetValue(constructorClassName, out var constructorClassIndex))
            {
                var classType = (PrimitiveType)(ManagedClassTypes.First + constructorClassIndex);
                var receiverType = program.Classes[constructorClassIndex].IsMutable
                    ? ManagedClassTypes.Writable(classType) : classType;
                if (function.Variables[0].Type == receiverType)
                {
                    classConstructorCounts[constructorClassIndex]++;
                }
            }
            if (function.IsCleanup && function.DeclaringStruct is string cleanupOwner)
            {
                cleanupOwners ??= new Dictionary<string, string>(StringComparer.Ordinal);
                cleanupOwners.Add(function.Name, cleanupOwner);
            }
        }
        if (classConstructorCounts is not null && classFactoryCounts is not null)
        {
            for (var classIndex = 0; classIndex < program.Classes.Length; classIndex++)
            {
                if (classConstructorCounts[classIndex] != 1 || classFactoryCounts[classIndex] != 1)
                {
                    throw Invalid();
                }
            }
        }
        foreach (var structure in program.Structures)
        {
            if (structure.CleanupMethod is not string cleanup)
            {
                continue;
            }
            if (!structure.IsUnique || cleanupOwners is null
                || !cleanupOwners.TryGetValue(cleanup, out var cleanupOwner)
                || cleanupOwner != structure.Name)
            {
                throw Invalid();
            }
        }

        if (requireEntryPoint)
        {
            var entry = program.Functions[program.EntryPoint];
            if ((entry.Name != "Main" && !entry.Name.EndsWith(".Main", StringComparison.Ordinal)) || entry.ParameterCount != 0
                || entry.IsExternal || entry.DeclaringStruct is not null || entry.ReturnType is not (PrimitiveType.Void or PrimitiveType.Int32))
            {
                throw Invalid();
            }
        }

        var hasSpanDependencies = VerifySpanContracts(program);
        var hasNativeOutputContracts = program.Functions.Any(static function =>
                function is not null && NativeOutputCount(function) != 0)
            || program.NativeFunctionPointerSignatures.Any(static signature =>
                !signature.OutputStates.IsEmpty);
        var hasDoesNotReturnFunctions = program.Functions.Any(
            static function => function.DoesNotReturn)
            || program.NativeFunctionPointerSignatures.Any(
                static signature => (signature.EffectsMask & 2) != 0);
        var nativePointerCaptureEffects = CreateNativePointerCaptureEffects(program);
        var nativePointerWriteEffects = CreateNativePointerWriteEffects(
            program, nativePointerCaptureEffects);
        ValidateNativePointerWriteContracts(
            program, nativePointerWriteEffects, nativePointerCaptureEffects);
        var hasCopyOriginValues = program.Functions.Any(function =>
            CarriesCopyOrigin(function.ReturnType, program)
            || function.Variables.Any(variable => CarriesCopyOrigin(variable.Type, program)));
        var copyOriginSummaries = new BoundCopyOriginSummary?[program.Functions.Length];
        var rawUnionReturnCoverage = new ImmutableArray<byte>[
            program.Functions.Length];
        for (var index = 0; index < program.Functions.Length; index++)
        {
            rawUnionReturnCoverage[index]
                = program.Functions[index].RawUnionReturnCoverage;
        }
        var normalBackingClassConstructorSummaries =
            new NormalBackingClassConstructorSummary?[program.Functions.Length];
        foreach (var function in program.Functions)
        {
            if (function.CopyOriginSummary is { } summary)
            {
                ValidateCopyOriginSummary(function, summary, program);
            }
        }
        for (var functionIndex = 0; functionIndex < program.Functions.Length; functionIndex++)
        {
            if (!deriveCopyOriginSummaries || !IsCopyOriginSummarySourceFunction(program.Functions[functionIndex]))
            {
                copyOriginSummaries[functionIndex] = program.Functions[functionIndex].CopyOriginSummary;
            }
        }
        var recursiveCopyOriginFunctions = new bool[program.Functions.Length];
        var recursiveRawUnionReturnCoverageFunctions
            = new bool[program.Functions.Length];
        var hasRawUnionReturnCoverageCandidates
            = program.Functions.Any(function => !function.IsExternal
                && function.Intrinsic == BoundIntrinsic.None
                && function.IndexedBody is null
                && CarriesRawUnionPayloadFacts(
                    program, function.ReturnType));
        var functionOrder = deriveCopyOriginSummaries
                || hasRawUnionReturnCoverageCandidates
            ? CopyOriginSummaryVerificationOrder(program,
                recursiveCopyOriginFunctions,
                recursiveRawUnionReturnCoverageFunctions)
            : [.. Enumerable.Range(0, program.Functions.Length)];
        var repeatResourceLedger = new RepeatResourceLedger();
        foreach (var functionIndex in functionOrder)
        {
            var function = program.Functions[functionIndex];
            if (function.IsExternal)
            {
                continue;
            }
            if (function.Intrinsic != BoundIntrinsic.None)
            {
                continue;
            }
            if (function.IndexedBody is not null)
            {
                continue;
            }
            using var repeatResourceCallable = repeatResourceLedger.BeginCallable();
            if (CarriesRawUnionPayloadFacts(
                program, function.ReturnType))
            {
                rawUnionReturnCoverage[functionIndex] = [];
            }
            BackingDependencies? backingDependencies;
            try
            {
                backingDependencies = CreateBackingDependencies(functionIndex, function, program, hasSpanDependencies,
                    nativePointerCaptureEffects, copyOriginSummaries,
                    normalBackingClassConstructorSummaries,
                    deriveCopyOriginSummaries && !recursiveCopyOriginFunctions[functionIndex],
                    hasCopyOriginValues);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                exception.Data[FunctionIndexKey] = functionIndex;
                throw;
            }
            var rawUnionReturnCoverageBuilder
                = CarriesRawUnionPayloadFacts(
                    program, function.ReturnType)
                    ? new RawUnionReturnCoverageBuilder(
                        MaterializedPayloadSize(function.ReturnType, program))
                    : null;
            var statementInputs = function.Variables.Any(variable => variable.Type is
                PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView)
                ? new Dictionary<BoundStatement, InitializationState>(ReferenceEqualityComparer.Instance) : null;
            using var repeatStatementInputState = statementInputs is null ? null
                : new RepeatSnapshotReservationScope(repeatResourceLedger);
            var initialized = new InitializationState(program, function.Variables.Length,
                hasNativeOutputContracts, hasDoesNotReturnFunctions) {
                StatementInputs = statementInputs,
                StatementInputSnapshotScope = repeatStatementInputState,
                Dependencies = backingDependencies,
                NativePointerCaptureEffects = nativePointerCaptureEffects,
                ScopeActivation = BackingDependencies.RootScopeActivation,
                RawUnionReturnCoverage = rawUnionReturnCoverageBuilder,
                RawUnionReturnCoverageSummaries = rawUnionReturnCoverage,
                RepeatResources = repeatResourceLedger,
            };
            if (function.IsConstructor)
            {
                initialized.Fill(true, 1, function.ParameterCount);
            }
            else
            {
                initialized.Fill(true, 0, function.ParameterCount);
            }
            if (function.IsClassConstructor)
            {
                initialized[0] = false;
            }
            for (var i = 0; i < function.ParameterCount; i++)
            {
                initialized[i + (function.IsConstructor ? 1 : 0)] &= !function.Variables[i + (function.IsConstructor ? 1 : 0)].IsOut;
            }
            var fieldMasks = new uint[function.Variables.Length];
            for (var i = 0; i < initialized.Length; i++)
            {
                if (initialized[i] && HasFields(program, function.Variables[i].Type))
                {
                    fieldMasks[i] = FullMask(program, function.Variables[i].Type);
                    initialized.SetPossibleFieldMask(i, fieldMasks[i]);
                }
                if (CarriesRawUnionPayloadFacts(
                        program, function.Variables[i].Type))
                {
                    var size = MaterializedPayloadSize(
                        function.Variables[i].Type, program);
                    var initialBytes = initialized[i]
                        ? GuaranteedInitializedPayloadBytes(
                            function.Variables[i].Type, program)
                        : new byte[size];
                    initialized.InitializeRawUnionPayloadFacts(i,
                        initialBytes);
                }
            }
            if (program.NativeLoweringAgreement
                    == CompilationOptions.WindowsX64NormalBackingAgreement
                && function.DeclaringStruct is not null && !function.IsStatic
                && function.Variables.Length != 0
                && IsNormalBackingObjectType(function.Variables[0].Type)
                && backingDependencies is not null)
            {
                var receiverSite = checked(functionIndex + 1);
                backingDependencies.SetNormalBackingObjects(0,
                    new HashSet<ProvenanceAnchor> {
                        ProvenanceAnchor.ForNormalBackingObject(functionIndex,
                            receiverSite, new StorageInstance(
                                BackingDependencies.RootScopeActivation, receiverSite),
                            function.Variables[0].Type),
                    },
                    mayBeUnknown: false);
            }
            var declarations = new HashSet<int>();
            var functionRemaining = MaximumAnalysisVisits;
            try
            {
                VerifyOwnerScopes(function.Body, function, program);
                VerifySpanScopes(function);
                VerifyManagedRoots(function, program);
                var returns = Statement(function.Body, initialized, fieldMasks, declarations, function, program,
                    ref functionRemaining, 0);
                VerifyViewScopes(functionIndex, function.Body, function,
                    initialized.StatementInputs);
                if (function.DoesNotReturn && !returns)
                {
                    throw InvalidEffect(function.Body.Span, "DoesNotReturn");
                }
                if (function.ReturnType != PrimitiveType.Void && !returns)
                {
                    throw InvalidEffect(function.Body.Span, "NormalReturn");
                }
                for (var i = function.ParameterCount + (function.IsConstructor ? 1 : 0); i < function.Variables.Length; i++)
                {
                    if (!declarations.Contains(i))
                    {
                        throw new ArgumentException(
                            $"Invalid bound program: function '{function.Name}' has undeclared local '{function.Variables[i].Name}' at index {i} (type {function.Variables[i].Type}).",
                            nameof(program));
                    }
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                exception.Data[FunctionIndexKey] = functionIndex;
                throw;
            }
            if (rawUnionReturnCoverageBuilder is { HasValue: true }
                && !recursiveRawUnionReturnCoverageFunctions[functionIndex])
            {
                rawUnionReturnCoverage[functionIndex]
                    = rawUnionReturnCoverageBuilder.Build();
            }
            if (program.NativeLoweringAgreement
                    == CompilationOptions.WindowsX64NormalBackingAgreement
                && function.IsClassConstructor && backingDependencies is not null)
            {
                var receiverSite = checked(functionIndex + 1);
                backingDependencies.CaptureNormalBackingClassConstructorSummary(
                    functionIndex,
                    ProvenanceAnchor.ForNormalBackingObject(functionIndex,
                        receiverSite, new StorageInstance(
                            BackingDependencies.RootScopeActivation, receiverSite),
                        function.Variables[0].Type));
            }
            if (deriveCopyOriginSummaries
                && backingDependencies?.CopyOriginSummaryBuilder is { } summaryBuilder)
            {
                copyOriginSummaries[functionIndex] = summaryBuilder.Build();
                if (copyOriginSummaries[functionIndex] is { } summary)
                {
                    ValidateCopyOriginSummary(function, summary, program);
                }
            }
        }

        VerifySourceGrantNativeCallables(program, requireEntryPoint);
        return new VerificationSummaries(
            [.. copyOriginSummaries],
            [.. rawUnionReturnCoverage]);

        bool HasFieldName(int owner, string name)
        {
            fieldNamesByOwner ??= new HashSet<string>?[program.Structures.Length + program.Classes.Length];
            var ownerIndex = owner >= ManagedClassTypes.First
                ? program.Structures.Length + owner - ManagedClassTypes.First : owner;
            var names = fieldNamesByOwner[ownerIndex];
            if (names is null)
            {
                var fields = owner >= ManagedClassTypes.First
                    ? program.Classes[owner - ManagedClassTypes.First].Fields
                    : program.Structures[owner].Fields;
                names = new HashSet<string>(fields.Length, StringComparer.Ordinal);
                if (owner >= ManagedClassTypes.First)
                {
                    foreach (var field in fields)
                    {
                        _ = names.Add(field.Name);
                    }
                }
                else
                {
                    foreach (var field in fields)
                    {
                        _ = names.Add(field.Name);
                    }
                }
                fieldNamesByOwner[ownerIndex] = names;
            }
            return names.Contains(name);
        }
    }

    private static void VerifySourceGrantNativeCallables(BoundProgram program, bool requireEntryPoint)
    {
        if (requireEntryPoint || !program.RequiresSourceGrant
            || !program.Functions.Any(static function => function.NativeFunctionPointerType is not null))
        {
            return;
        }

        bool[] sourceDependent;
        if (NativeSourceGrantClassifier.CanClassify(program))
        {
            sourceDependent = NativeSourceGrantClassifier.Classify(program);
        }
        else if (NativeSourceGrantExperiment.CanClassify(program))
        {
            sourceDependent = NativeSourceGrantExperiment.Classify(program);
        }
        else
        {
            return;
        }

        for (var functionIndex = 0; functionIndex < sourceDependent.Length; functionIndex++)
        {
            var function = program.Functions[functionIndex];
            if (sourceDependent[functionIndex] && function.NativeFunctionPointerType is not null)
            {
                throw InvalidEffect(function.Span, "NativeFunctionPointer");
            }
        }
    }

    private static void VerifyManagedRoots(BoundFunction function, BoundProgram program)
    {
        var retired = new HashSet<int>();
        var active = new HashSet<int>();
        var remaining = MaximumAnalysisVisits;
        for (var slot = 0; slot < function.ParameterCount + (function.IsConstructor ? 1 : 0); slot++)
        {
            if (IsManagedLifetimeValue(program, function.Variables[slot].Type))
            {
                _ = active.Add(slot);
            }
        }

        void Declare(int slot, HashSet<int> scope, TextSpan span)
        {
            if (slot < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                || slot >= function.Variables.Length || slot < 0
                || !IsManagedLifetimeValue(program, function.Variables[slot].Type)
                || !scope.Add(slot) || !active.Add(slot))
            {
                throw InvalidEffect(span, "Ownership");
            }
        }

        void Expression(BoundExpression expression, HashSet<int> scope, int depth)
        {
            Count(ref remaining, depth);
            if (expression is null)
            {
                throw Invalid();
            }
            switch (expression)
            {
                case BoundRead read when IsManagedLifetimeValue(program, read.Type):
                    if (!active.Contains(read.Variable))
                    {
                        throw InvalidEffect(read.Span, "Ownership");
                    }
                    break;
                case BoundOutArgument output when IsManagedLifetimeValue(program, output.Type):
                    if (output.DeclaresVariable)
                    {
                        Declare(output.Variable, scope, output.Span);
                    }
                    else if (!active.Contains(output.Variable))
                    {
                        throw InvalidEffect(output.Span, "Ownership");
                    }
                    break;
                case BoundUnsafeExpression unsafeExpression:
                    Expression(unsafeExpression.Expression, scope, depth + 1);
                    break;
                case BoundCall call:
                    if (call.Arguments.IsDefault)
                    {
                        throw Invalid();
                    }
                    foreach (var argument in call.Arguments)
                    {
                        Expression(argument, scope, depth + 1);
                    }
                    break;
                case BoundIndirectCall call:
                    Expression(call.Target, scope, depth + 1);
                    foreach (var argument in call.Arguments)
                    {
                        Expression(argument, scope, depth + 1);
                    }
                    break;
                case BoundOptionalLift lift:
                    Expression(lift.Value, scope, depth + 1);
                    break;
                case BoundCoalesce coalesce:
                    Expression(coalesce.Optional, scope, depth + 1);
                    Expression(coalesce.Fallback, scope, depth + 1);
                    break;
                case BoundReadonlyProjection projection:
                    Expression(projection.Value, scope, depth + 1);
                    break;
                case BoundIsNull test:
                    Expression(test.Value, scope, depth + 1);
                    break;
                case BoundNativeFixedArrayInitialization fixedArray:
                    if (fixedArray.Elements.IsDefault)
                    {
                        throw Invalid();
                    }
                    foreach (var element in fixedArray.Elements)
                    {
                        Expression(element, scope, depth + 1);
                    }
                    break;
                case BoundNativeFixedArrayRepeatInitialization repeat:
                    if (repeat.Seed is null)
                    {
                        throw Invalid();
                    }
                    Expression(repeat.Seed, scope, depth + 1);
                    break;
                case BoundNativeFixedArrayElement fixedArrayElement:
                    Expression(fixedArrayElement.Index, scope, depth + 1);
                    break;
                case BoundNativeFixedArrayPathElement pathElement:
                    foreach (var projection in pathElement.Projections)
                    {
                        Expression(projection.Index, scope, depth + 1);
                    }
                    break;
                case BoundNativeFixedArrayPointerAddress pointerAddress:
                    if (pointerAddress.Projections.IsDefaultOrEmpty)
                    {
                        Expression(pointerAddress.Index, scope, depth + 1);
                    }
                    else
                    {
                        foreach (var projection in pointerAddress.Projections)
                        {
                            Expression(projection.Index, scope, depth + 1);
                        }
                    }
                    break;
                case BoundNativeStorageAddress address:
                    foreach (var nested in address.NestedExpressions)
                    {
                        Expression(nested, scope, depth + 1);
                    }
                    break;
                case BoundNativeOutputReservation:
                    break;
                case BoundReferenceFormation formation:
                    Expression(formation.Place, scope, depth + 1);
                    break;
                case BoundReferenceReborrow reborrow:
                    Expression(reborrow.Descriptor, scope, depth + 1);
                    break;
                case BoundReferenceRead referenceRead:
                    Expression(referenceRead.Descriptor, scope, depth + 1);
                    break;
                case BoundReferenceWrite referenceWrite:
                    Expression(referenceWrite.Descriptor, scope, depth + 1);
                    Expression(referenceWrite.Value, scope, depth + 1);
                    break;
                case BoundNativePointerRead pointerRead:
                    Expression(pointerRead.Address, scope, depth + 1);
                    break;
                case BoundNativePointerWrite pointerWrite:
                    Expression(pointerWrite.Address, scope, depth + 1);
                    Expression(pointerWrite.Value, scope, depth + 1);
                    break;
                case BoundIsInitialized:
                    break;
                case BoundIsSome pattern:
                    Expression(pattern.Value, scope, depth + 1);
                    if ((uint)pattern.Variable
                        >= (uint)function.Variables.Length)
                    {
                        throw InvalidEffect(pattern.Span, "Ownership");
                    }
                    if (IsManagedLifetimeValue(program,
                            function.Variables[pattern.Variable].Type))
                    {
                        Declare(pattern.Variable, scope, pattern.Span);
                    }
                    break;
                case BoundIsVariablePattern pattern:
                    Expression(pattern.Value, scope, depth + 1);
                    if (pattern.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                        || pattern.Variable < 0 || pattern.Variable >= function.Variables.Length)
                    {
                        throw InvalidEffect(pattern.Span, "Ownership");
                    }
                    if (IsManagedLifetimeValue(program, function.Variables[pattern.Variable].Type))
                    {
                        Declare(pattern.Variable, scope, pattern.Span);
                    }
                    break;
                case BoundUnary unary:
                    Expression(unary.Operand, scope, depth + 1);
                    break;
                case BoundUpdate update:
                    Expression(update.Place, scope, depth + 1);
                    break;
                case BoundCompoundAssignment compound:
                    Expression(compound.Place, scope, depth + 1);
                    Expression(compound.Right, scope, depth + 1);
                    break;
                case BoundNominalUpdate update:
                    Expression(update.Place, scope, depth + 1);
                    break;
                case BoundCast cast:
                    Expression(cast.Operand, scope, depth + 1);
                    break;
                case BoundBinary binary:
                    Expression(binary.Left, scope, depth + 1);
                    Expression(binary.Right, scope, depth + 1);
                    break;
                case BoundConditional conditional:
                    Expression(conditional.Condition, scope, depth + 1);
                    Expression(conditional.WhenTrue, scope, depth + 1);
                    Expression(conditional.WhenFalse, scope, depth + 1);
                    break;
                case BoundFieldRead field:
                    Expression(field.Receiver, scope, depth + 1);
                    break;
                case BoundFieldMove move:
                    Expression(move.Place, scope, depth + 1);
                    break;
                case BoundFieldLoan loan:
                    Expression(loan.Place, scope, depth + 1);
                    break;
                case BoundMutableFieldLoan loan:
                    Expression(loan.Place, scope, depth + 1);
                    break;
                case BoundBufferRead buffer:
                    Expression(buffer.Index, scope, depth + 1);
                    break;
                case BoundViewRead view:
                    Expression(view.Index, scope, depth + 1);
                    break;
                case BoundSpanLength length:
                    Expression(length.View, scope, depth + 1);
                    break;
                case BoundSpanRead read:
                    Expression(read.View, scope, depth + 1);
                    Expression(read.Index, scope, depth + 1);
                    break;
                case BoundSpanAdvance advance:
                    Expression(advance.View, scope, depth + 1);
                    break;
                case BoundSnapshotSpan:
                    break;
                case BoundNativePointerAddress:
                    break;
                case BoundNativeFixedArrayWrite write:
                    Expression(write.Index, scope, depth + 1);
                    Expression(write.Value, scope, depth + 1);
                    break;
                case BoundNativeFixedArrayPathWrite pathWrite:
                    foreach (var projection in pathWrite.Projections)
                    {
                        Expression(projection.Index, scope, depth + 1);
                    }
                    Expression(pathWrite.Value, scope, depth + 1);
                    break;
                case BoundConstant or BoundNull or BoundNativeFunctionAddress
                    or BoundRead or BoundMove or BoundReceiverLoan
                    or BoundMutableReceiverLoan or BoundOutArgument or BoundViewLength or BoundBufferLength:
                    break;
                default:
                    throw Invalid();
            }
        }

        void Statement(BoundStatement statement, HashSet<int> scope, int depth)
        {
            Count(ref remaining, depth);
            if (statement is null)
            {
                throw Invalid();
            }
            switch (statement)
            {
                case BoundBlock block:
                    if (block.RetiredManagedLocals.IsDefault || block.Statements.IsDefault)
                    {
                        throw Invalid();
                    }
                    var nested = block.IsManagedLexicalScope ? new HashSet<int>() : scope;
                    foreach (var child in block.Statements)
                    {
                        Statement(child, nested, depth + 1);
                    }
                    if (block.IsManagedLexicalScope)
                    {
                        if (nested.Count != block.RetiredManagedLocals.Length)
                        {
                            throw InvalidEffect(block.Span, "Ownership");
                        }
                        foreach (var slot in block.RetiredManagedLocals)
                        {
                            if (!nested.Remove(slot) || !active.Remove(slot) || !retired.Add(slot))
                            {
                                throw InvalidEffect(block.Span, "Ownership");
                            }
                        }
                    }
                    else if (!block.RetiredManagedLocals.IsEmpty)
                    {
                        throw InvalidEffect(block.Span, "Ownership");
                    }
                    break;
                case BoundTryFinally cleanup:
                    Statement(cleanup.Body, scope, depth + 1);
                    Statement(cleanup.Finally, scope, depth + 1);
                    break;
                case BoundViewScope view:
                    Expression(view.Start, scope, depth + 1);
                    Expression(view.RangeEnd ?? view.Length, scope, depth + 1);
                    Statement(view.Body, scope, depth + 1);
                    break;
                case BoundTryCatch recovery:
                    Statement(recovery.Body, scope, depth + 1);
                    Statement(recovery.Catch, scope, depth + 1);
                    if (recovery.Finally is not null)
                    {
                        Statement(recovery.Finally, scope, depth + 1);
                    }
                    break;
                case BoundUnsafe unsafeBlock:
                    Statement(unsafeBlock.Body, scope, depth + 1);
                    break;
                case BoundIf conditional:
                    Expression(conditional.Condition, scope, depth + 1);
                    Statement(conditional.Then, scope, depth + 1);
                    if (conditional.Else is not null)
                    {
                        Statement(conditional.Else, scope, depth + 1);
                    }
                    break;
                case BoundWhile loop:
                    Expression(loop.Condition, scope, depth + 1);
                    Statement(loop.Body, scope, depth + 1);
                    if (loop.Iterator is not null)
                    {
                        Statement(loop.Iterator, scope, depth + 1);
                    }
                    break;
                case BoundLocal local:
                    Expression(local.Initializer, scope, depth + 1);
                    if (local.Variable < 0 || local.Variable >= function.Variables.Length)
                    {
                        throw Invalid();
                    }
                    if (IsManagedLifetimeValue(program, function.Variables[local.Variable].Type))
                    {
                        Declare(local.Variable, scope, local.Span);
                    }
                    break;
                case BoundVacantLocal vacant:
                    if (vacant.Variable < 0 || vacant.Variable >= function.Variables.Length)
                    {
                        throw Invalid();
                    }
                    if (IsManagedLifetimeValue(program, function.Variables[vacant.Variable].Type))
                    {
                        Declare(vacant.Variable, scope, vacant.Span);
                    }
                    break;
                case BoundAssignment assignment:
                    Expression(assignment.Value, scope, depth + 1);
                    if (assignment.Variable < 0 || assignment.Variable >= function.Variables.Length)
                    {
                        throw Invalid();
                    }
                    if (IsManagedLifetimeValue(program, function.Variables[assignment.Variable].Type)
                        && !active.Contains(assignment.Variable))
                    {
                        throw InvalidEffect(assignment.Span, "Ownership");
                    }
                    break;
                case BoundOptionalAssignment optionalAssignment:
                    Expression(optionalAssignment.Place, scope, depth + 1);
                    Expression(optionalAssignment.Value, scope, depth + 1);
                    break;
                case BoundFieldAssignment field:
                    Expression(field.Value, scope, depth + 1);
                    break;
                case BoundBufferAssignment buffer:
                    Expression(buffer.Index, scope, depth + 1);
                    Expression(buffer.Value, scope, depth + 1);
                    break;
                case BoundViewSet view:
                    Expression(view.Index, scope, depth + 1);
                    Expression(view.Value, scope, depth + 1);
                    break;
                case BoundExpressionStatement effects:
                    Expression(effects.Expression, scope, depth + 1);
                    break;
                case BoundRequire requirement:
                    Expression(requirement.Condition, scope, depth + 1);
                    break;
                case BoundReturn result when result.Value is not null:
                    Expression(result.Value, scope, depth + 1);
                    break;
                case BoundLifetimeEnd or BoundDrop or BoundDropSpan or BoundDropConstructedFields or BoundOwnerDeclaration or BoundReturn
                    or BoundBreak or BoundContinue:
                    break;
                default:
                    throw Invalid();
            }
        }

        Statement(function.Body, new HashSet<int>(), 0);
        for (var slot = function.ParameterCount + (function.IsConstructor ? 1 : 0);
            slot < function.Variables.Length; slot++)
        {
            if (IsManagedLifetimeValue(program, function.Variables[slot].Type) && !retired.Contains(slot))
            {
                throw InvalidEffect(function.Variables[slot].Span, "Ownership");
            }
        }
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Leaf nodes have no ownership scope children.")]
    private static void VerifyOwnerScopes(BoundBlock body, BoundFunction function, BoundProgram program)
    {
        var owners = new HashSet<BoundLocal>(ReferenceEqualityComparer.Instance);
        var outputOwners = new HashSet<BoundOwnerDeclaration>(ReferenceEqualityComparer.Instance);
        var vacantOwners = new HashSet<BoundVacantLocal>(ReferenceEqualityComparer.Instance);
        var allowed = new HashSet<BoundDrop>(ReferenceEqualityComparer.Instance);
        var drops = new List<BoundDrop>();
        var parameterBody = body;
        for (var slot = function.IsConstructor ? 1 : 0;
            slot < function.ParameterCount + (function.IsConstructor ? 1 : 0); slot++)
        {
            if (!IsUnique(program, function.Variables[slot].Type)
                || (slot == 0 && function.DeclaringStruct is not null && !function.IsStatic))
            {
                continue;
            }
            if (parameterBody.Statements is not [BoundTryFinally parameterRegion]
                || parameterRegion.Finally is not BoundBlock { Statements: [BoundDrop drop] }
                || drop.Variable != slot || drop.IsOutput != function.Variables[slot].IsOut
                || !allowed.Add(drop))
            {
                throw Invalid(function.Span);
            }
            parameterBody = parameterRegion.Body;
        }
        BoundDropConstructedFields? constructedFields = null;
        if (function.IsConstructor && program.Structures[(int)function.ReturnType - 100].Fields
            .Any(field => IsUnique(program, field.Type)))
        {
            if (parameterBody.Statements is not [BoundTryFinally constructorRegion]
                || constructorRegion.Body is null || constructorRegion.Body.Statements.IsDefault
                || constructorRegion.Finally is not BoundBlock { Statements: [BoundDropConstructedFields partial] }
                || partial.Variable != 0 || partial.AcquisitionOrder.IsDefault
                || !partial.AcquisitionOrder.SequenceEqual(ConstructorAcquisitions(constructorRegion.Body, function, program)))
            {
                throw Invalid(function.Span);
            }
            constructedFields = partial;
        }
        var pending = new Stack<(BoundStatement Statement, int Depth)>();
        pending.Push((body, 0));
        var visits = MaximumAnalysisVisits;
        while (pending.TryPop(out var item))
        {
            Count(ref visits, item.Depth);
            switch (item.Statement)
            {
                case BoundBlock block:
                    if (block.Statements.IsDefault)
                    {
                        throw Invalid();
                    }
                    for (var index = 0; index < block.Statements.Length; index++)
                    {
                        var child = block.Statements[index];
                        if (child is BoundLocal local && local.Variable >= 0
                            && local.Variable < function.Variables.Length
                            && IsUnique(program, function.Variables[local.Variable].Type))
                        {
                            if (index + 1 >= block.Statements.Length
                                || block.Statements[index + 1] is not BoundTryFinally region
                                || (index + 2 != block.Statements.Length
                                    && !(index + 3 == block.Statements.Length
                                        && block.Statements[^1] is BoundReturn terminal
                                        && ((function.ReturnType == PrimitiveType.Void && terminal.Value is null)
                                            || (function.IsConstructor && terminal.Value is BoundRead { Variable: 0 }))))
                                || region.Finally is not BoundBlock finallyBlock
                                || finallyBlock.Statements is not [BoundDrop drop]
                                || drop.Variable != local.Variable || drop.IsOutput || !allowed.Add(drop)
                                || !owners.Add(local))
                            {
                                throw Invalid(local.Span);
                            }
                        }
                        if (child is BoundOwnerDeclaration or BoundVacantLocal)
                        {
                            var output = child is BoundOwnerDeclaration owner ? owner.Variable
                                : ((BoundVacantLocal)child).Variable;
                            if (output >= 0 && output < function.Variables.Length
                                && IsUnique(program, function.Variables[output].Type)
                                && (index + 1 >= block.Statements.Length
                                || block.Statements[index + 1] is not BoundTryFinally outputRegion
                                || (index + 2 != block.Statements.Length
                                    && !(index + 3 == block.Statements.Length
                                        && block.Statements[^1] is BoundReturn terminal
                                        && ((function.ReturnType == PrimitiveType.Void && terminal.Value is null)
                                            || (function.IsConstructor && terminal.Value is BoundRead { Variable: 0 }))))
                                || outputRegion.Finally.Statements is not [BoundDrop outputDrop]
                                || outputDrop.Variable != output || outputDrop.IsOutput
                                || !allowed.Add(outputDrop)
                                || (child is BoundOwnerDeclaration declaration && !outputOwners.Add(declaration))
                                || (child is BoundVacantLocal vacant && !vacantOwners.Add(vacant))))
                            {
                                throw Invalid(child.Span);
                            }
                        }
                        pending.Push((child, item.Depth + 1));
                    }
                    break;
                case BoundLocal local:
                    if (local.Initializer is null
                        || (IsUnique(program, local.Initializer.Type) && !owners.Remove(local)))
                    {
                        throw Invalid(local.Span);
                    }
                    break;
                case BoundOwnerDeclaration output:
                    if (!outputOwners.Remove(output))
                    {
                        throw Invalid(output.Span);
                    }
                    break;
                case BoundVacantLocal vacant:
                    if (vacant.Variable >= 0 && vacant.Variable < function.Variables.Length
                        && IsUnique(program, function.Variables[vacant.Variable].Type)
                        && !vacantOwners.Remove(vacant))
                    {
                        throw Invalid(vacant.Span);
                    }
                    break;
                case BoundDropSpan:
                    break;
                case BoundDrop drop:
                    drops.Add(drop);
                    break;
                case BoundDropConstructedFields partial:
                    if (!ReferenceEquals(partial, constructedFields))
                    {
                        throw Invalid(partial.Span);
                    }
                    constructedFields = null;
                    break;
                case BoundTryFinally region:
                    pending.Push((region.Body, item.Depth + 1));
                    pending.Push((region.Finally, item.Depth + 1));
                    break;
                case BoundViewScope view:
                    pending.Push((view.Body, item.Depth + 1));
                    break;
                case BoundTryCatch recovery:
                    pending.Push((recovery.Body, item.Depth + 1));
                    pending.Push((recovery.Catch, item.Depth + 1));
                    if (recovery.Finally is not null)
                    {
                        pending.Push((recovery.Finally, item.Depth + 1));
                    }
                    break;
                case BoundUnsafe unsafeBlock:
                    pending.Push((unsafeBlock.Body, item.Depth + 1));
                    break;
                case BoundIf conditional:
                    pending.Push((conditional.Then, item.Depth + 1));
                    if (conditional.Else is not null)
                    {
                        pending.Push((conditional.Else, item.Depth + 1));
                    }
                    break;
                case BoundWhile loop:
                    pending.Push((loop.Body, item.Depth + 1));
                    if (loop.Iterator is not null)
                    {
                        pending.Push((loop.Iterator, item.Depth + 1));
                    }
                    break;
            }
        }
        foreach (var drop in drops)
        {
            if (!allowed.Remove(drop))
            {
                throw Invalid(drop.Span);
            }
        }
        if (allowed.Count != 0 || owners.Count != 0 || outputOwners.Count != 0 || vacantOwners.Count != 0
            || constructedFields is not null)
        {
            throw Invalid();
        }
    }

    private static void VerifyViewScopes(int functionActivation, BoundBlock body, BoundFunction function,
        Dictionary<BoundStatement, InitializationState>? inputs)
    {
        var roots = new int[function.Variables.Length];
        var exclusive = new bool[roots.Length];
        var activeCapabilitiesByView = new CapabilityKey?[roots.Length];
        var activeCapabilities = new HashSet<CapabilityKey>();
        var capabilityAcquisitionContext = new CapabilityAcquisitionContext();
        var backing = new int[roots.Length];
        var declared = new HashSet<int>();
        var nativeLocals = function.Variables.Any(static variable =>
            NativeFixedArrayContract.IsCarrier(variable.Type))
            ? new HashSet<int>() : null;
        var remaining = MaximumAnalysisVisits;

        bool IsActiveLease(int variable)
        {
            return (uint)variable < (uint)activeCapabilitiesByView.Length
                        && activeCapabilitiesByView[variable] is CapabilityKey capability
                        && capability.Contract.Kind == CapabilityKind.Lease
                        && capability.Contract.Type == function.Variables[variable].Type
                        && capability.Contract.SupportingPlace == backing[variable]
                        && capability.Contract.Projection.Kind == StorageProjectionKind.WholeObject
                        && activeCapabilities.Contains(capability);
        }

        void Expression(BoundExpression expression, int depth, int acquiringRoot = -1)
        {
            Count(ref remaining, depth);
            if (expression is null)
            {
                throw Invalid();
            }
            switch (expression)
            {
                case BoundMove move:
                    if (move.Variable < 0 || move.Variable >= roots.Length
                        || roots[move.Variable] != 0 || exclusive[move.Variable]
                        || move.Type is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView)
                    {
                        throw InvalidEffect(move.Span, "Ownership");
                    }
                    break;
                case BoundFieldMove move:
                    if (move.Place is not BoundFieldRead place
                        || !TryGetFieldRootVariable(place, out var rootVariable)
                        || (uint)rootVariable >= (uint)roots.Length
                        || roots[rootVariable] != 0 || exclusive[rootVariable])
                    {
                        throw InvalidEffect(move.Span, "Ownership");
                    }
                    break;
                case BoundOutArgument output:
                    if (output.Variable < 0 || output.Variable >= roots.Length
                        || roots[output.Variable] != 0 || exclusive[output.Variable]
                        || output.Type is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView)
                    {
                        throw InvalidEffect(output.Span, "Ownership");
                    }
                    break;
                case BoundMutableReceiverLoan loan:
                    if (loan.Variable < 0 || loan.Variable >= roots.Length
                        || roots[loan.Variable] != 0 || exclusive[loan.Variable])
                    {
                        throw InvalidEffect(loan.Span, "Ownership");
                    }
                    break;
                case BoundMutableFieldLoan loan:
                    var mutableRoot = LoanRoot(loan, ref remaining, depth + 1);
                    if (mutableRoot < 0 || mutableRoot >= roots.Length
                        || roots[mutableRoot] != 0 || exclusive[mutableRoot])
                    {
                        throw InvalidEffect(loan.Span, "Ownership");
                    }
                    break;
                case BoundReceiverLoan loan:
                    if (loan.Variable < 0 || loan.Variable >= roots.Length
                        || (exclusive[loan.Variable] && loan.Variable != acquiringRoot))
                    {
                        throw InvalidEffect(loan.Span, "Ownership");
                    }
                    break;
                case BoundFieldLoan loan:
                    var loanRoot = LoanRoot(loan, ref remaining, depth + 1);
                    if (loanRoot < 0 || loanRoot >= roots.Length
                        || (exclusive[loanRoot] && loanRoot != acquiringRoot))
                    {
                        throw InvalidEffect(loan.Span, "Ownership");
                    }
                    break;
                case BoundViewLength length:
                    if (!IsActiveLease(length.Variable))
                    {
                        throw InvalidEffect(length.Span, "Ownership");
                    }
                    break;
                case BoundViewRead viewRead:
                    if (!IsActiveLease(viewRead.Variable))
                    {
                        throw InvalidEffect(viewRead.Span, "Ownership");
                    }
                    Expression(viewRead.Index, depth + 1, acquiringRoot);
                    break;
                case BoundSpanLength length:
                    Expression(length.View, depth + 1, acquiringRoot);
                    break;
                case BoundSpanRead read:
                    Expression(read.View, depth + 1, acquiringRoot);
                    Expression(read.Index, depth + 1, acquiringRoot);
                    break;
                case BoundSpanAdvance advance:
                    Expression(advance.View, depth + 1, acquiringRoot);
                    break;
                case BoundSnapshotSpan snapshot:
                    if (snapshot.Variable < 0 || snapshot.Variable >= roots.Length || exclusive[snapshot.Variable])
                    {
                        throw InvalidEffect(snapshot.Span, "Ownership");
                    }
                    break;
                case BoundNativeStorageAddress address:
                    foreach (var nested in address.NestedExpressions)
                    {
                        Expression(nested, depth + 1, acquiringRoot);
                    }
                    break;
                case BoundNativeOutputReservation:
                    break;
                case BoundNativeFixedArrayInitialization fixedArray:
                    if (fixedArray.Elements.IsDefault)
                    {
                        throw Invalid();
                    }
                    foreach (var element in fixedArray.Elements)
                    {
                        Expression(element, depth + 1, acquiringRoot);
                    }
                    break;
                case BoundNativeFixedArrayRepeatInitialization repeat:
                    Expression(repeat.Seed, depth + 1, acquiringRoot);
                    break;
                case BoundRead localRead when localRead.Type is PrimitiveType.ReadonlyInt32BufferView
                    or PrimitiveType.MutableInt32BufferView:
                    throw InvalidEffect(localRead.Span, "Ownership");
                case BoundRead localRead when localRead.Variable < 0 || localRead.Variable >= roots.Length
                    || (exclusive[localRead.Variable] && localRead.Variable != acquiringRoot):
                    throw InvalidEffect(localRead.Span, "Ownership");
                case BoundUnsafeExpression unsafeExpression:
                    Expression(unsafeExpression.Expression, depth + 1, acquiringRoot);
                    break;
                case BoundCall call:
                    if (call.Arguments.IsDefault)
                    {
                        throw Invalid();
                    }
                    foreach (var argument in call.Arguments)
                    {
                        Expression(argument, depth + 1, acquiringRoot);
                    }
                    break;
                case BoundIndirectCall call:
                    Expression(call.Target, depth + 1, acquiringRoot);
                    foreach (var argument in call.Arguments)
                    {
                        Expression(argument, depth + 1, acquiringRoot);
                    }
                    break;
                case BoundUnary unary:
                    Expression(unary.Operand, depth + 1, acquiringRoot);
                    break;
                case BoundUpdate update:
                    Expression(update.Place, depth + 1, acquiringRoot);
                    break;
                case BoundCompoundAssignment compound:
                    Expression(compound.Place, depth + 1, acquiringRoot);
                    Expression(compound.Right, depth + 1, acquiringRoot);
                    break;
                case BoundNominalUpdate update:
                    Expression(update.Place, depth + 1, acquiringRoot);
                    break;
                case BoundCast cast:
                    Expression(cast.Operand, depth + 1, acquiringRoot);
                    break;
                case BoundOptionalLift lift:
                    Expression(lift.Value, depth + 1, acquiringRoot);
                    break;
                case BoundCoalesce coalesce:
                    Expression(coalesce.Optional, depth + 1, acquiringRoot);
                    Expression(coalesce.Fallback, depth + 1, acquiringRoot);
                    break;
                case BoundReadonlyProjection projection:
                    Expression(projection.Value, depth + 1, acquiringRoot);
                    break;
                case BoundIsNull test:
                    Expression(test.Value, depth + 1, acquiringRoot);
                    break;
                case BoundNativeFixedArrayElement fixedArrayElement:
                    Expression(fixedArrayElement.Index, depth + 1, acquiringRoot);
                    break;
                case BoundNativeFixedArrayPathElement pathElement:
                    foreach (var projection in pathElement.Projections)
                    {
                        Expression(projection.Index, depth + 1, acquiringRoot);
                    }
                    break;
                case BoundNativeFixedArrayPointerAddress pointerAddress:
                    if (pointerAddress.Projections.IsDefaultOrEmpty)
                    {
                        Expression(pointerAddress.Index, depth + 1,
                            acquiringRoot);
                    }
                    else
                    {
                        foreach (var projection in pointerAddress.Projections)
                        {
                            Expression(projection.Index, depth + 1,
                                acquiringRoot);
                        }
                    }
                    break;
                case BoundReferenceFormation formation:
                    Expression(formation.Place, depth + 1, acquiringRoot);
                    break;
                case BoundReferenceReborrow reborrow:
                    Expression(reborrow.Descriptor, depth + 1, acquiringRoot);
                    break;
                case BoundReferenceRead referenceRead:
                    Expression(referenceRead.Descriptor, depth + 1, acquiringRoot);
                    break;
                case BoundReferenceWrite referenceWrite:
                    Expression(referenceWrite.Descriptor, depth + 1, acquiringRoot);
                    Expression(referenceWrite.Value, depth + 1, acquiringRoot);
                    break;
                case BoundNativePointerRead pointerRead:
                    Expression(pointerRead.Address, depth + 1, acquiringRoot);
                    break;
                case BoundNativePointerWrite pointerWrite:
                    Expression(pointerWrite.Address, depth + 1, acquiringRoot);
                    Expression(pointerWrite.Value, depth + 1, acquiringRoot);
                    break;
                case BoundNativeFixedArrayWrite fixedArrayWrite:
                    Expression(fixedArrayWrite.Index, depth + 1, acquiringRoot);
                    Expression(fixedArrayWrite.Value, depth + 1, acquiringRoot);
                    break;
                case BoundNativeFixedArrayPathWrite pathWrite:
                    foreach (var projection in pathWrite.Projections)
                    {
                        Expression(projection.Index, depth + 1, acquiringRoot);
                    }
                    Expression(pathWrite.Value, depth + 1, acquiringRoot);
                    break;
                case BoundIsInitialized:
                    break;
                case BoundIsSome pattern:
                    Expression(pattern.Value, depth + 1, acquiringRoot);
                    break;
                case BoundIsVariablePattern pattern:
                    Expression(pattern.Value, depth + 1, acquiringRoot);
                    break;
                case BoundBinary binary:
                    Expression(binary.Left, depth + 1, acquiringRoot);
                    Expression(binary.Right, depth + 1, acquiringRoot);
                    break;
                case BoundConditional conditional:
                    Expression(conditional.Condition, depth + 1, acquiringRoot);
                    Expression(conditional.WhenTrue, depth + 1, acquiringRoot);
                    Expression(conditional.WhenFalse, depth + 1, acquiringRoot);
                    break;
                case BoundFieldRead field:
                    Expression(field.Receiver, depth + 1, acquiringRoot);
                    break;
                case BoundBufferRead buffer:
                    Expression(buffer.Index, depth + 1, acquiringRoot);
                    break;
                case BoundConstant or BoundNull or BoundNativeFunctionAddress
                    or BoundNativePointerAddress
                    or BoundRead or BoundBufferLength:
                    break;
                default:
                    throw Invalid();
            }
        }

        void Statement(BoundStatement statement, int depth)
        {
            Count(ref remaining, depth);
            if (statement is null)
            {
                throw Invalid();
            }
            // Borrow occupancy follows possible value liveness at this exact flow point,
            // not the descriptor's lexical declaration. Joins retain either path's loan.
            Array.Clear(roots);
            Array.Clear(exclusive);
            if (inputs is not null && !inputs.TryGetValue(statement, out _))
            {
                throw Invalid();
            }
            for (var slot = 0; slot < activeCapabilitiesByView.Length; slot++)
            {
                if (IsActiveLease(slot) && inputs is not null && !inputs[statement].IsVacant(slot))
                {
                    if (function.Variables[slot].Type == PrimitiveType.MutableInt32BufferView)
                    {
                        exclusive[backing[slot]] = true;
                    }
                    else
                    {
                        roots[backing[slot]]++;
                    }
                }
            }
            switch (statement)
            {
                case BoundBlock block:
                    if (block.Statements.IsDefault)
                    {
                        throw Invalid();
                    }
                    var enclosingNativeLocals = block.IsManagedLexicalScope && nativeLocals is not null
                        ? new HashSet<int>(nativeLocals) : null;
                    foreach (var child in block.Statements)
                    {
                        Statement(child, depth + 1);
                    }
                    if (nativeLocals is not null && enclosingNativeLocals is not null)
                    {
                        nativeLocals.IntersectWith(enclosingNativeLocals);
                    }
                    break;
                case BoundViewScope view:
                    if (view.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                        || view.Variable >= roots.Length || view.Variable < 0
                        || function.Variables[view.Variable].Type != (view.IsMutable
                            ? PrimitiveType.MutableInt32BufferView : PrimitiveType.ReadonlyInt32BufferView)
                        || function.Variables[view.Variable].IsMutable != view.IsMutable
                        || !declared.Add(view.Variable)
                        || activeCapabilitiesByView[view.Variable] is not null
                        || view.Body is null || view.Owner is null)
                    {
                        throw InvalidEffect(view.Span, "Ownership");
                    }
                    var root = ViewRoot(view.Owner, ref remaining, depth + 1);
                    if (root < 0 || root >= roots.Length)
                    {
                        throw InvalidEffect(view.Owner.Span, "Ownership");
                    }
                    backing[view.Variable] = root;
                    if (exclusive[root] || (view.IsMutable && roots[root] != 0))
                    {
                        throw InvalidEffect(view.Span, "Ownership");
                    }
                    if (view.IsMutable)
                    {
                        exclusive[root] = true;
                    }
                    else
                    {
                        roots[root]++;
                    }
                    Expression(view.Start, depth + 1, view.IsMutable ? root : -1);
                    Expression(view.RangeEnd ?? view.Length, depth + 1, view.IsMutable ? root : -1);
                    var executionActivation = inputs is null
                        ? BackingDependencies.RootScopeActivation
                        : inputs[view].ScopeActivation;
                    var capability = capabilityAcquisitionContext.Acquire(
                        functionActivation, view, executionActivation,
                        new CapabilityContract(CapabilityKind.Lease,
                            function.Variables[view.Variable].Type, root,
                            StorageProjection.WholeObject));
                    if (!activeCapabilities.Add(capability))
                    {
                        throw InvalidEffect(view.Span, "Ownership");
                    }
                    activeCapabilitiesByView[view.Variable] = capability;
                    bool ReleaseCapability()
                    {
                        var isCurrent = activeCapabilitiesByView[
                                view.Variable] == capability
                            && activeCapabilities.Remove(capability);
                        activeCapabilitiesByView[view.Variable] = null;
                        return isCurrent;
                    }

                    try
                    {
                        Statement(view.Body, depth + 1);
                    }
                    catch
                    {
                        if (!ReleaseCapability())
                        {
                            throw Invalid(view.Span);
                        }
                        throw;
                    }
                    if (!ReleaseCapability())
                    {
                        throw Invalid(view.Span);
                    }
                    break;
                case BoundLocal local:
                    if (local.Variable < 0 || local.Variable >= roots.Length
                        || function.Variables[local.Variable].Type is PrimitiveType.ReadonlyInt32BufferView
                            or PrimitiveType.MutableInt32BufferView)
                    {
                        throw InvalidEffect(local.Span, "Ownership");
                    }
                    Expression(local.Initializer, depth + 1);
                    if (nativeLocals is not null
                        && NativeFixedArrayContract.IsCarrier(function.Variables[local.Variable].Type))
                    {
                        _ = nativeLocals.Add(local.Variable);
                    }
                    break;
                case BoundVacantLocal:
                case BoundDropSpan:
                    break;
                case BoundAssignment assignment:
                    if (assignment.Variable < 0 || assignment.Variable >= roots.Length
                        || roots[assignment.Variable] != 0 || exclusive[assignment.Variable]
                        || function.Variables[assignment.Variable].Type is PrimitiveType.ReadonlyInt32BufferView
                            or PrimitiveType.MutableInt32BufferView)
                    {
                        throw InvalidEffect(assignment.Span, "Ownership");
                    }
                    Expression(assignment.Value, depth + 1);
                    break;
                case BoundOptionalAssignment optionalAssignment:
                    Expression(optionalAssignment.Place, depth + 1);
                    Expression(optionalAssignment.Value, depth + 1);
                    break;
                case BoundFieldAssignment field:
                    if (field.Variable < 0 || field.Variable >= roots.Length
                        || roots[field.Variable] != 0 || exclusive[field.Variable])
                    {
                        throw InvalidEffect(field.Span, "Ownership");
                    }
                    Expression(field.Value, depth + 1);
                    break;
                case BoundLifetimeEnd end:
                    if (end.Variable < 0 || end.Variable >= roots.Length
                        || roots[end.Variable] != 0 || exclusive[end.Variable])
                    {
                        throw InvalidEffect(end.Span, "Ownership");
                    }
                    break;
                case BoundDrop drop:
                    if (drop.Variable < 0 || drop.Variable >= roots.Length
                        || roots[drop.Variable] != 0 || exclusive[drop.Variable])
                    {
                        throw InvalidEffect(drop.Span, "Ownership");
                    }
                    break;
                case BoundBufferAssignment write:
                    Expression(write.Index, depth + 1);
                    Expression(write.Value, depth + 1);
                    break;
                case BoundViewSet viewWrite:
                    if (!IsActiveLease(viewWrite.Variable)
                        || function.Variables[viewWrite.Variable].Type != PrimitiveType.MutableInt32BufferView)
                    {
                        throw InvalidEffect(viewWrite.Span, "Ownership");
                    }
                    Expression(viewWrite.Index, depth + 1);
                    Expression(viewWrite.Value, depth + 1);
                    break;
                case BoundExpressionStatement effects:
                    Expression(effects.Expression, depth + 1);
                    break;
                case BoundIf conditional:
                    Expression(conditional.Condition, depth + 1);
                    Statement(conditional.Then, depth + 1);
                    if (conditional.Else is not null)
                    {
                        Statement(conditional.Else, depth + 1);
                    }
                    break;
                case BoundWhile loop:
                    Expression(loop.Condition, depth + 1);
                    Statement(loop.Body, depth + 1);
                    if (loop.Iterator is not null)
                    {
                        Statement(loop.Iterator, depth + 1);
                    }
                    break;
                case BoundTryFinally cleanup:
                    Statement(cleanup.Body, depth + 1);
                    Statement(cleanup.Finally, depth + 1);
                    break;
                case BoundTryCatch recovery:
                    Statement(recovery.Body, depth + 1);
                    Statement(recovery.Catch, depth + 1);
                    if (recovery.Finally is not null)
                    {
                        Statement(recovery.Finally, depth + 1);
                    }
                    break;
                case BoundUnsafe unsafeBlock:
                    Statement(unsafeBlock.Body, depth + 1);
                    break;
                case BoundRequire requirement:
                    Expression(requirement.Condition, depth + 1);
                    break;
                case BoundReturn result:
                    if (result.Value is not null)
                    {
                        Expression(result.Value, depth + 1);
                    }
                    break;
                case BoundOwnerDeclaration or BoundDropConstructedFields or BoundBreak or BoundContinue:
                    break;
                default:
                    throw Invalid();
            }
        }

        Statement(body, 0);
        if (activeCapabilities.Count != 0
            || activeCapabilitiesByView.Any(static capability => capability.HasValue))
        {
            throw Invalid(function.Span);
        }
        for (var slot = function.ParameterCount + (function.IsConstructor ? 1 : 0); slot < roots.Length; slot++)
        {
            if (!declared.Contains(slot) && function.Variables[slot].Type is
                PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView)
            {
                throw Invalid();
            }
        }
    }

    private static int ViewRoot(BoundExpression owner, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (owner.Type != PrimitiveType.OwnedInt32Buffer)
        {
            throw InvalidEffect(owner.Span, "Ownership");
        }
        if (owner is BoundReceiverLoan or BoundMutableReceiverLoan)
        {
            return owner is BoundReceiverLoan named ? named.Variable : ((BoundMutableReceiverLoan)owner).Variable;
        }
        return LoanRoot(owner, ref remaining, depth + 1);
    }

    private static int LoanRoot(BoundExpression owner, ref int remaining, int depth)
    {
        if (owner is not (BoundFieldLoan or BoundMutableFieldLoan))
        {
            throw InvalidEffect(owner.Span, "Ownership");
        }
        BoundExpression place = owner is BoundFieldLoan field ? field.Place : ((BoundMutableFieldLoan)owner).Place;
        while (place is BoundFieldRead nested)
        {
            Count(ref remaining, depth++);
            place = nested.Receiver;
        }
        return place is BoundRead root ? root.Variable : throw InvalidEffect(owner.Span, "Ownership");
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Only field acquisitions and nested control regions contribute to the order.")]
    private static ImmutableArray<int> ConstructorAcquisitions(BoundBlock body, BoundFunction function, BoundProgram program)
    {
        var order = ImmutableArray.CreateBuilder<int>();
        var pending = new Stack<(BoundStatement Statement, bool Sequential, int Depth)>();
        pending.Push((body, true, 0));
        var remaining = MaximumAnalysisVisits;
        var fields = program.Structures[(int)function.ReturnType - 100].Fields;
        while (pending.TryPop(out var item))
        {
            Count(ref remaining, item.Depth);
            if (item.Statement is null)
            {
                throw Invalid();
            }
            switch (item.Statement)
            {
                case BoundBlock block:
                    if (block.Statements.IsDefault)
                    {
                        throw Invalid();
                    }
                    for (var i = block.Statements.Length - 1; i >= 0; i--)
                    {
                        pending.Push((block.Statements[i], item.Sequential, item.Depth + 1));
                    }
                    break;
                case BoundTryFinally region:
                    pending.Push((region.Finally, false, item.Depth + 1));
                    pending.Push((region.Body, item.Sequential, item.Depth + 1));
                    break;
                case BoundViewScope view:
                    pending.Push((view.Body, item.Sequential, item.Depth + 1));
                    break;
                case BoundFieldAssignment assignment when assignment.Variable == 0
                    && assignment.Field >= 0 && assignment.Field < fields.Length
                    && IsUnique(program, fields[assignment.Field].Type):
                    if (!item.Sequential || !assignment.NestedFields.IsEmpty)
                    {
                        throw InvalidEffect(assignment.Span, "Ownership");
                    }
                    var priorAcquisition = -1;
                    for (var index = 0; index < order.Count; index++)
                    {
                        if (order[index] == assignment.Field)
                        {
                            priorAcquisition = index;
                            break;
                        }
                    }
                    if (priorAcquisition >= 0)
                    {
                        if (!fields[assignment.Field].IsMutable)
                        {
                            throw InvalidEffect(assignment.Span, "Ownership");
                        }
                        order.RemoveAt(priorAcquisition);
                    }
                    order.Add(assignment.Field);
                    break;
                case BoundIf conditional:
                    if (conditional.Else is not null)
                    {
                        pending.Push((conditional.Else, false, item.Depth + 1));
                    }
                    pending.Push((conditional.Then, false, item.Depth + 1));
                    break;
                case BoundWhile loop:
                    pending.Push((loop.Body, false, item.Depth + 1));
                    if (loop.Iterator is not null)
                    {
                        pending.Push((loop.Iterator, false, item.Depth + 1));
                    }
                    break;
                case BoundTryCatch recovery:
                    if (recovery.Finally is not null)
                    {
                        pending.Push((recovery.Finally, false, item.Depth + 1));
                    }
                    pending.Push((recovery.Catch, false, item.Depth + 1));
                    pending.Push((recovery.Body, false, item.Depth + 1));
                    break;
                case BoundUnsafe unsafeBlock:
                    pending.Push((unsafeBlock.Body, item.Sequential, item.Depth + 1));
                    break;
            }
        }
        return order.ToImmutable();
    }

    private static void VerifyNativePointerUnsafeScopes(BoundProgram program)
    {
        static void Push(Stack<(object Node, bool IsUnsafe, int Depth)> pending, object? node,
            bool isUnsafe, int depth)
        {
            if (node is null)
            {
                throw Invalid();
            }
            pending.Push((node, isUnsafe, depth));
        }

        foreach (var function in program.Functions)
        {
            if (function is null || function.Body is null)
            {
                throw Invalid(function?.Span ?? default);
            }
            var pending = new Stack<(object Node, bool IsUnsafe, int Depth)>();
            Push(pending, function.Body, false, 0);
            var remaining = MaximumAnalysisVisits;
            while (pending.TryPop(out var item))
            {
                Count(ref remaining, item.Depth);
                var depth = item.Depth + 1;
                switch (item.Node)
                {
                    case BoundBlock block:
                        if (block.Statements.IsDefault)
                        {
                            throw Invalid(block.Span);
                        }
                        foreach (var statement in block.Statements)
                        {
                            Push(pending, statement, item.IsUnsafe, depth);
                        }
                        break;
                    case BoundUnsafe unsafeBlock:
                        Push(pending, unsafeBlock.Body, true, depth);
                        break;
                    case BoundUnsafeExpression unsafeExpression:
                        Push(pending, unsafeExpression.Expression, true, depth);
                        break;
                    case BoundNativePointerAddress address when !item.IsUnsafe || !address.IsUnsafeAuthorized:
                        throw InvalidEffect(address.Span, "NativePointer");
                    case BoundNativeFixedArrayPointerAddress address
                        when !item.IsUnsafe || !address.IsUnsafeAuthorized:
                        throw InvalidEffect(address.Span, "NativePointer");
                    case BoundNativePointerRead read when !item.IsUnsafe || !read.IsUnsafeAuthorized:
                        throw InvalidEffect(read.Span, "NativePointer");
                    case BoundNativePointerWrite write when !item.IsUnsafe || !write.IsUnsafeAuthorized:
                        throw InvalidEffect(write.Span, "NativePointer");
                    case BoundNativeFunctionAddress address
                        when !item.IsUnsafe || !address.IsUnsafeAuthorized:
                        throw InvalidEffect(address.Span, "NativeFunctionPointer");
                    case BoundCall call when (uint)call.Function < (uint)program.Functions.Length
                        && program.Functions[call.Function] is BoundFunction { RequiresUnsafeCaller: true }
                        && (!item.IsUnsafe || !call.IsUnsafeAuthorized):
                        throw InvalidEffect(call.Span, "UnsafeCaller");
                    case BoundIndirectCall call when !item.IsUnsafe || !call.IsUnsafeAuthorized:
                        throw InvalidEffect(call.Span, "UnsafeCaller");
                    case BoundLocal local:
                        Push(pending, local.Initializer, item.IsUnsafe, depth);
                        break;
                    case BoundAssignment assignment:
                        Push(pending, assignment.Value, item.IsUnsafe, depth);
                        break;
                    case BoundOptionalAssignment assignment:
                        Push(pending, assignment.Place, item.IsUnsafe, depth);
                        Push(pending, assignment.Value, item.IsUnsafe, depth);
                        break;
                    case BoundFieldAssignment assignment:
                        Push(pending, assignment.Value, item.IsUnsafe, depth);
                        break;
                    case BoundBufferAssignment assignment:
                        Push(pending, assignment.Index, item.IsUnsafe, depth);
                        Push(pending, assignment.Value, item.IsUnsafe, depth);
                        break;
                    case BoundViewSet view:
                        Push(pending, view.Index, item.IsUnsafe, depth);
                        Push(pending, view.Value, item.IsUnsafe, depth);
                        break;
                    case BoundExpressionStatement statement:
                        Push(pending, statement.Expression, item.IsUnsafe, depth);
                        break;
                    case BoundIf conditional:
                        Push(pending, conditional.Condition, item.IsUnsafe, depth);
                        Push(pending, conditional.Then, item.IsUnsafe, depth);
                        if (conditional.Else is not null)
                        {
                            Push(pending, conditional.Else, item.IsUnsafe, depth);
                        }
                        break;
                    case BoundWhile loop:
                        Push(pending, loop.Condition, item.IsUnsafe, depth);
                        Push(pending, loop.Body, item.IsUnsafe, depth);
                        if (loop.Iterator is not null)
                        {
                            Push(pending, loop.Iterator, item.IsUnsafe, depth);
                        }
                        break;
                    case BoundTryFinally cleanup:
                        Push(pending, cleanup.Body, item.IsUnsafe, depth);
                        Push(pending, cleanup.Finally, item.IsUnsafe, depth);
                        break;
                    case BoundTryCatch recovery:
                        Push(pending, recovery.Body, item.IsUnsafe, depth);
                        Push(pending, recovery.Catch, item.IsUnsafe, depth);
                        if (recovery.Finally is not null)
                        {
                            Push(pending, recovery.Finally, item.IsUnsafe, depth);
                        }
                        break;
                    case BoundViewScope view:
                        Push(pending, view.Owner, item.IsUnsafe, depth);
                        Push(pending, view.Start, item.IsUnsafe, depth);
                        Push(pending, view.Length, item.IsUnsafe, depth);
                        Push(pending, view.Body, item.IsUnsafe, depth);
                        if (view.RangeEnd is not null)
                        {
                            Push(pending, view.RangeEnd, item.IsUnsafe, depth);
                        }
                        break;
                    case BoundRequire require:
                        Push(pending, require.Condition, item.IsUnsafe, depth);
                        break;
                    case BoundReturn { Value: BoundExpression value }:
                        Push(pending, value, item.IsUnsafe, depth);
                        break;
                    case BoundExpression expression:
                        if (!expression.HasValidNestedExpressions)
                        {
                            throw Invalid(expression.Span);
                        }
                        foreach (var nested in expression.NestedExpressions)
                        {
                            Push(pending, nested, item.IsUnsafe, depth);
                        }
                        break;
                    case BoundReturn:
                    case BoundVacantLocal or BoundLifetimeEnd or BoundOwnerDeclaration or BoundDrop
                        or BoundDropSpan or BoundDropConstructedFields or BoundBreak or BoundContinue:
                        break;
                    default:
                        throw Invalid();
                }
            }
        }
    }

    private static void VerifyDirectNativeImports(BoundProgram program)
    {
        foreach (var function in program.Functions)
        {
            if (function.DirectNativeImport is not null
                && !DirectNativeImportContract.IsValid(function, program.Structures,
                    program.NativeFunctionPointerSignatures, program.NativePointerTypes))
            {
                throw InvalidEffect(function.Span, "NativeImport");
            }
        }
        if (program.Functions.Any(static function => function.DirectNativeImport is null
                && (function.ReturnType is
                    PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
                    || function.Variables.Any(static variable => variable.Type is
                        PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer)))
            || !NativeStringZPoolContract.IsValid(
                program.NativeUtf16ZLiterals, program.NativeUtf8ZLiterals))
        {
            throw InvalidEffect(default, "NativeImport");
        }
    }

    internal static void RejectSourceNativeImports(BoundProgram program, string target,
        bool allowDirectNativeImports = false,
        bool allowFixedArrayStorage = false,
        bool allowVerifiedFixedArrayBodies = false)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        var hasRetiredFixedArrayCarrier
            = HasRetiredFixedArrayCarrier(program);
        var verifiedFixedArrayFailure = allowVerifiedFixedArrayBodies
            ? GetVerifiedFixedArrayUseFailure(program) : null;
        var invalidFixedArrayUse = hasRetiredFixedArrayCarrier
            || (allowVerifiedFixedArrayBodies
                ? verifiedFixedArrayFailure is not null
                : HasInvalidSourceFixedArrayUse(program,
                    allowFixedArrayStorage));
        if (invalidFixedArrayUse)
        {
            throw new NotSupportedException(
                $"Fixed-array runtime forms are not supported by {target}"
                + (allowVerifiedFixedArrayBodies
                    ? $" ({verifiedFixedArrayFailure ?? "retired carrier"})."
                    : "."));
        }
        if ((!allowDirectNativeImports
                && program.Functions.Any(static function => function.DirectNativeImport is not null
                    && (!function.IsModuleReference || function.ModuleImport is null)))
            || program.Functions.Any(static function => function.ReturnType is
                PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
                || function.Variables.Any(variable => variable.Type is
                    PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer))
            || (!program.Structures.IsDefaultOrEmpty && program.Structures.Any(
                static structure => structure.Fields.Any(
                    static field => field.Type is PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
                    )))
            || (!program.Classes.IsDefaultOrEmpty && program.Classes.Any(
                static @class => @class.Fields.Any(
                    static field => field.Type is PrimitiveType.NativeUtf16ZPointer
                        or PrimitiveType.NativeUtf8ZPointer)))
            || !program.NativeUtf16ZLiterals.IsEmpty
            || !program.NativeUtf8ZLiterals.IsEmpty)
        {
            throw new NotSupportedException($"Source native imports are not supported by {target}.");
        }
    }

    private static bool HasInvalidSourceFixedArrayUse(
        BoundProgram program,
        bool allowFixedArrayStorage)
    {
        var hasFixedArrayData = !program.NativeFixedArrayTypes.IsDefaultOrEmpty
            || program.Functions.Any(static function =>
                NativeFixedArrayContract.IsCarrier(function.ReturnType)
                || function.Variables.Any(static variable =>
                    NativeFixedArrayContract.IsCarrier(variable.Type)))
            || program.Structures.Any(static structure =>
                structure.Fields.Any(static field =>
                    NativeFixedArrayContract.IsCarrier(field.Type)))
            || (!program.Classes.IsDefaultOrEmpty
                && program.Classes.Any(static @class =>
                    @class.Fields.Any(static field =>
                        NativeFixedArrayContract.IsCarrier(field.Type))));
        if (!hasFixedArrayData)
        {
            return false;
        }

        if (!allowFixedArrayStorage
            || !NativeFixedArrayContract.MatchesProgram(program))
        {
            return true;
        }

        foreach (var function in program.Functions)
        {
            if (NativeFixedArrayContract.IsCarrier(function.ReturnType)
                || HasUnsupportedMetadataOnlyArrayType(
                    function.ReturnType, program)
                || (function.NativeFunctionPointerType is PrimitiveType functionPointerType
                    && NativeFixedArrayContract.HasMetadataOnlyArrayType(
                        functionPointerType, program.Structures,
                        program.NativeFixedArrayTypes,
                        program.NativePointerTypes,
                        program.NativeFunctionPointerSignatures)))
            {
                return true;
            }

            var firstLocal = function.ParameterCount
                + (function.IsConstructor ? 1 : 0);
            for (var variableIndex = 0;
                variableIndex < function.Variables.Length;
                variableIndex++)
            {
                var variable = function.Variables[variableIndex];
                if (!NativeFixedArrayContract.IsCarrier(variable.Type))
                {
                    if (HasUnsupportedMetadataOnlyArrayType(
                            variable.Type, program)
                        && !IsSupportedFixedArrayFieldAggregateVariable(
                            variableIndex, variable.Type, function, program))
                    {
                        return true;
                    }
                    continue;
                }

                if (function.IsExternal
                    || function.IndexedBody is not null
                    || variableIndex < firstLocal
                    || variable.IsOut
                    || variable.IsInit
                    || variable.IsInitializationLocal
                    || !NativeFixedArrayContract.TryGetLayout(
                        variable.Type, program.Structures,
                        program.NativeFixedArrayTypes, out var layout)
                    || !NativeFixedArrayContract.MatchesLayout(layout, program))
                {
                    return true;
                }
            }
        }

        return !program.Classes.IsDefaultOrEmpty
                && program.Classes.Any(static @class =>
                    @class.Fields.Any(static field =>
                        NativeFixedArrayContract.IsCarrier(field.Type)));
    }

    private static string? GetVerifiedFixedArrayUseFailure(
        BoundProgram program)
    {
        if (!NativeFixedArrayContract.MatchesProgram(program))
        {
            return "fixed-array table does not match the program";
        }

        foreach (var function in program.Functions)
        {
            if (NativeFixedArrayContract.IsCarrier(function.ReturnType)
                || HasUnsupportedMetadataOnlyArrayType(
                    function.ReturnType, program))
            {
                return $"unsupported return type in {function.Name}";
            }

            var firstLocal = function.ParameterCount
                + (function.IsConstructor ? 1 : 0);
            for (var variableIndex = 0;
                variableIndex < function.Variables.Length;
                variableIndex++)
            {
                var variable = function.Variables[variableIndex];
                if (!NativeFixedArrayContract.IsCarrier(variable.Type))
                {
                    if (HasUnsupportedMetadataOnlyArrayType(
                            variable.Type, program)
                        && !IsSupportedFixedArrayFieldAggregateVariable(
                            variableIndex, variable.Type, function, program))
                    {
                        return $"unsupported local type in {function.Name}";
                    }

                    continue;
                }

                var hasLayout = NativeFixedArrayContract.TryGetLayout(
                    variable.Type, program.Structures,
                    program.NativeFixedArrayTypes, out var layout);
                var matchesLayout = hasLayout
                    && NativeFixedArrayContract.MatchesLayout(layout,
                        program);
                if (function.IsExternal || variableIndex < firstLocal
                    || variable.IsOut
                    || variable.IsInit || !matchesLayout)
                {
                    return $"unsupported array local {variableIndex} in {function.Name}";
                }
            }
        }

        return !program.Classes.IsDefaultOrEmpty
            && program.Classes.Any(static @class =>
                @class.Fields.Any(static field =>
                    NativeFixedArrayContract.IsCarrier(field.Type)))
            ? "fixed-array class field" : null;
    }

    private static bool HasRetiredFixedArrayCarrier(BoundProgram program)
        => program.Functions.Any(static function =>
                function.ReturnType == PrimitiveType.NativeFixedArray
                || function.Variables.Any(static variable =>
                    variable.Type == PrimitiveType.NativeFixedArray))
            || program.Structures.Any(static structure =>
                structure.Fields.Any(static field =>
                    field.Type == PrimitiveType.NativeFixedArray))
            || (!program.Classes.IsDefaultOrEmpty
                && program.Classes.Any(static @class =>
                    @class.Fields.Any(static field =>
                        field.Type == PrimitiveType.NativeFixedArray)));

    private static bool IsSupportedLocalFixedArrayFieldAggregate(
        int variable, PrimitiveType type, BoundFunction function,
        BoundProgram program)
    {
        var firstLocal = function.ParameterCount
            + (function.IsConstructor
                || (function.DeclaringStruct is not null
                    && !function.IsStatic) ? 1 : 0);
        return variable >= firstLocal
            && IsInlineStructStorage(type, program)
            && !IsUnique(program, type)
            && NativeFixedArrayContract.IsRootlessCopyFieldType(type,
                program.Structures, program.NativeFixedArrayTypes);
    }

    private static bool HasUnsupportedMetadataOnlyArrayType(
        PrimitiveType type, BoundProgram program)
        => NativeFixedArrayContract.HasMetadataOnlyArrayType(type,
                program.Structures, program.NativeFixedArrayTypes,
                program.NativePointerTypes,
                program.NativeFunctionPointerSignatures)
            && !NativeFixedArrayContract
                .IsRootlessCopyAggregateWithFixedArrayFields(type,
                    program.Structures, program.NativeFixedArrayTypes,
                    program.NativePointerTypes,
                    program.NativeFunctionPointerSignatures);

    private static bool IsSupportedFixedArrayFieldAggregateVariable(
        int variable, PrimitiveType type, BoundFunction function,
        BoundProgram program)
    {
        if (IsSupportedLocalFixedArrayFieldAggregate(variable, type,
                function, program))
        {
            return true;
        }

        if (function.IsExternal
            || !NativeFixedArrayContract
                .IsRootlessCopyAggregateWithFixedArrayFields(type,
                    program.Structures, program.NativeFixedArrayTypes,
                    program.NativePointerTypes,
                    program.NativeFunctionPointerSignatures))
        {
            return false;
        }
        if (function.IsConstructor && variable == 0
            && type == function.ReturnType)
        {
            return true;
        }

        var firstParameter = function.DeclaringStruct is not null
            && !function.IsStatic ? 1 : 0;
        if (function.ParameterCount < 0 || variable < firstParameter
            || (uint)(variable - firstParameter)
                >= (uint)function.ParameterCount)
        {
            return false;
        }

        var parameter = function.Variables[variable];
        return !parameter.IsOut && !parameter.IsInit
            && !parameter.IsImplicitRef && !parameter.IsInitializationLocal;
    }

    internal static bool TryVerifySource(BoundProgram program, out int functionIndex, out TextSpan span, out bool exceededWorkLimit,
        out string? invalidEffect, bool allowGenericProofDependencies = false)
        => TryVerifySourceCore(program, out _, out functionIndex, out span,
            out exceededWorkLimit, out invalidEffect, allowGenericProofDependencies,
            deriveCopyOriginSummaries: false);

    internal static bool TryVerifySourceWithCopyOriginSummaries(BoundProgram program,
        out BoundProgram verifiedProgram, out int functionIndex, out TextSpan span,
        out bool exceededWorkLimit, out string? invalidEffect,
        bool allowGenericProofDependencies = false)
        => TryVerifySourceCore(program, out verifiedProgram, out functionIndex, out span,
            out exceededWorkLimit, out invalidEffect, allowGenericProofDependencies,
            deriveCopyOriginSummaries: true);

    private static bool TryVerifySourceCore(BoundProgram program,
        out BoundProgram verifiedProgram, out int functionIndex, out TextSpan span,
        out bool exceededWorkLimit, out string? invalidEffect,
        bool allowGenericProofDependencies, bool deriveCopyOriginSummaries)
    {
        try
        {
            var hasCopyOriginSummaryCandidates = deriveCopyOriginSummaries
                && program.Functions.Any(function =>
                    IsCopyOriginSummarySourceFunction(function)
                    && CopyOriginSummaryBuilder.Create(function, program) is not null);
            VerificationSummaries summaries;
            if (hasCopyOriginSummaryCandidates)
            {
                summaries = VerifyCore(program,
                    requireEntryPoint: program.EntryPoint != -1, allowExternal: true,
                    allowGenericProofDependencies: allowGenericProofDependencies,
                    deriveCopyOriginSummaries: true);
            }
            else
            {
                summaries = VerifyCore(program,
                    requireEntryPoint: program.EntryPoint != -1, allowExternal: true,
                    allowGenericProofDependencies: allowGenericProofDependencies);
            }
            verifiedProgram = program with {
                Functions = [.. program.Functions.Select((function, index) =>
                    function with {
                        CopyOriginSummary = summaries.CopyOrigin[index],
                        RawUnionReturnCoverage
                            = summaries.RawUnionReturnCoverage[index],
                    })],
            };
            functionIndex = -1;
            span = default;
            exceededWorkLimit = false;
            invalidEffect = null;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            verifiedProgram = program;
            functionIndex = exception.Data[FunctionIndexKey] is int index ? index : Math.Max(0, program.EntryPoint);
            span = exception.Data[SourceSpanKey] is TextSpan sourceSpan
                ? sourceSpan : program.Functions.IsDefaultOrEmpty ? default : program.Functions[functionIndex].Span;
            exceededWorkLimit = exception is NotSupportedException;
            invalidEffect = exception is SemanticRepeatResourceLimitException resourceLimit
                ? $"SemanticResourceLimit:{resourceLimit.Dimension}"
                : exception.Data[InvalidEffectKey] is string effect
                    ? effect
                    : $"{exception.Message} in function '{program.Functions[functionIndex].Name}'{Environment.NewLine}{exception.StackTrace}";
            return false;
        }
    }

    internal static bool RequiresNoFailureProof(BoundFunction function) =>
        function.NoFailureProof || (!function.IsExternal && function.NativeFunctionPointerType is not null);

    internal static bool ProvidesNoFailureGuarantee(BoundFunction function) =>
        function.NoFailureProof || function.NativeFunctionPointerType is not null;

    // Native exception unwinding remains a separate failure edge.
    internal static bool CanCallWithoutTerrumFailure(BoundFunction caller, BoundFunction target) =>
        ProvidesNoFailureGuarantee(target)
        || (!caller.NoFailureProof && caller.NativeFunctionPointerType is not null
            && (target.DirectNativeImport is not null || target.NativeVTableMethod is not null));

    private static bool IsConstructionHome(BoundFunction function,
        int variable) =>
        function.ConstructionHomeIndex >= 0
            ? variable == function.ConstructionHomeIndex
            : (function.IsConstructor || function.IsClassConstructor)
                && variable == 0;

    private static bool Statement(BoundStatement statement, InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, bool reachable = true,
        List<CleanupScope>? cleanups = null, bool inCleanup = false, bool inUnsafe = false, FailureEdges? failures = null,
        List<LoopScope>? loops = null, CleanupCountedLoop? countedLoop = null, BoundBinary? cleanupCounterStep = null)
    {
        Count(ref remaining, depth);
        initialized.Record(statement);
        cleanups ??= [];
        loops ??= [];
        if (!reachable)
        {
            failures = null;
        }
        switch (statement)
        {
            case BoundBlock block:
                if (block.Statements.IsDefault)
                {
                    throw Invalid();
                }
                var previousScopeActivation = initialized.ScopeActivation;
                initialized.ScopeActivation = initialized.EnterBlockActivation(block.Span);
                initialized.EnterStorageScope(block);
                try
                {
                    var counted = inCleanup
                        && loops.Exists(static scope => scope.IsCleanupSwitch)
                        ? RecognizeCleanupCountedLoop(block, function, program,
                            ref remaining, depth + 1)
                        : null;
                    var returns = false;
                    foreach (var child in block.Statements)
                    {
                        if (child is null)
                        {
                            throw Invalid();
                        }
                        var childReturns = Statement(child, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1,
                            reachable && !returns, cleanups, inCleanup, inUnsafe, failures, loops,
                            countedLoop: counted is not null && ReferenceEquals(child, counted.Loop) ? counted : null);
                        returns |= childReturns;
                    }
                    if (reachable && !returns)
                    {
                        CheckNativePointerScopeExit(block, initialized);
                    }
                    return returns;
                }
                finally
                {
                    initialized.ExitStorageScope(block);
                    initialized.ScopeActivation = previousScopeActivation;
                }
            case BoundLocal local:
                if (local.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    || local.Variable >= initialized.Length
                    || !declarations.Add(local.Variable) || initialized[local.Variable]
                    || function.Variables[local.Variable].IsInitializationLocal
                    || function.Variables[local.Variable].IsImplicitReferenceTemporary)
                {
                    throw Invalid();
                }
                initialized.ActivateStorage(local.Variable, (long)local.Span.Start + 1);
                if (!CanCompleteNormally(local.Initializer, initialized, program,
                    ref remaining, depth + 1))
                {
                    using var repeatSnapshotState
                        = CreateRepeatSnapshotScopeIfNeeded(initialized);
                    var beforeCall = initialized.Clone();
                    var beforeMasks = (uint[])fieldMasks.Clone();
                    if (NativeFixedArrayContract.IsCarrier(function.Variables[local.Variable].Type))
                    {
                        VerifyNativeFixedArrayConstruction(local.Initializer, local.Variable, initialized,
                            fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                    }
                    else
                    {
                        Expression(local.Initializer, initialized, fieldMasks, declarations, function, program,
                            ref remaining, depth + 1, failures);
                    }
                    if (local.Initializer.Type != function.Variables[local.Variable].Type)
                    {
                        throw Invalid();
                    }
                    beforeCall.CopyTo(initialized);
                    beforeMasks.CopyTo(fieldMasks, 0);
                    return true;
                }
                if (NativeFixedArrayContract.IsCarrier(function.Variables[local.Variable].Type))
                {
                    VerifyNativeFixedArrayConstruction(local.Initializer, local.Variable, initialized,
                        fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                }
                else
                {
                    if (local.Initializer is BoundMove localMove)
                    {
                        CheckNativePointerLifetimeEnd(
                            localMove.Variable, initialized, localMove.Span);
                        initialized.Dependencies?.TransferBackingSource(
                            localMove.Variable, local.Variable);
                    }
                    Expression(local.Initializer, initialized, fieldMasks, declarations, function, program,
                        ref remaining, depth + 1, failures);
                }
                if (local.Initializer.Type != function.Variables[local.Variable].Type)
                {
                    throw Invalid();
                }
                var nativeOutputResult = NativeOutputResultForValue(
                    local.Initializer, initialized, local.Variable);
                initialized.MarkAssigned(local.Variable);
                if (local.Initializer is BoundRead localAggregateRead
                    && IsInlineStructStorage(local.Initializer.Type, program))
                {
                    _ = initialized.CopyRawUnionArrayFacts(local.Variable,
                        localAggregateRead.Variable);
                }
                if (local.Initializer is BoundCall localCall
                    && initialized.Dependencies is BackingDependencies
                        localCallDependencies
                    && localCallDependencies.TryGetRawUnionArrayCallResult(
                        localCall, out var localArrayFacts))
                {
                    _ = initialized.SetRawUnionArrayFacts(local.Variable,
                        StorageProjection.WholeObject, localArrayFacts,
                        ref remaining, depth + 1);
                    localCallDependencies.ClearRawUnionArrayCallResult(
                        localCall);
                }
                if (CarriesRawUnionPayloadFacts(
                        program, local.Initializer.Type))
                {
                    initialized.SetRawUnionInitializedBytes(local.Variable,
                        InitializedPayloadBytesOfExpression(
                            local.Initializer, local.Initializer.Type,
                            initialized, function, program));
                }
                initialized.SetNativeOutputResult(local.Variable, nativeOutputResult);
                if (program.NativeLoweringAgreement
                        == CompilationOptions.WindowsX64NormalBackingAgreement
                    && IsNormalBackingObjectType(local.Initializer.Type)
                    && initialized.Dependencies is BackingDependencies
                        localObjectDependencies)
                {
                    var objects = NormalBackingObjectsOfExpression(
                        local.Initializer, localObjectDependencies, initialized,
                        function, program, ref remaining, depth + 1,
                        out var mayBeUnknown);
                    localObjectDependencies.SetNormalBackingObjects(
                        local.Variable, objects, mayBeUnknown);
                }
                if (CheckedReferenceTypeContract.IsValidType(
                        local.Initializer.Type, program.CheckedReferenceTypes))
                {
                    if (initialized.Dependencies is not BackingDependencies referenceDependencies)
                    {
                        throw InvalidEffect(local.Span, "CheckedReference");
                    }
                    referenceDependencies.SetReferenceTargets(local.Variable,
                        ReferenceTargetsOfExpression(local.Initializer, referenceDependencies,
                            initialized, function, program, ref remaining, depth + 1,
                            fieldMasks));
                    referenceDependencies.SetReferenceObligations(local.Variable,
                        ReferenceObligationsOfExpression(local.Initializer,
                            referenceDependencies, initialized, function, program,
                            ref remaining, depth + 1, fieldMasks));
                }
                if (HasFields(program, local.Initializer.Type))
                {
                    fieldMasks[local.Variable] = FullMask(program, local.Initializer.Type);
                    initialized.SetPossibleFieldMask(local.Variable, fieldMasks[local.Variable]);
                }
                if (local.Initializer is not BoundMove
                    && TokenSpanContract.IsDependent(local.Initializer.Type, program.Structures)
                    && initialized.Dependencies is BackingDependencies localBackingDependencies)
                {
                    localBackingDependencies.SetBackingSources(local.Variable,
                        BackingOf(local.Initializer, localBackingDependencies, program,
                            ref remaining, depth + 1));
                }
                if (local.Initializer is not BoundMove
                    && IsInlineStructStorage(local.Initializer.Type, program)
                    && TokenSpanContract.IsDependent(local.Initializer.Type, program.Structures)
                    && initialized.Dependencies is BackingDependencies localFieldBackingDependencies)
                {
                    SetBackingFieldValuesFromExpression(local.Variable,
                        StorageProjection.WholeObject, local.Initializer.Type,
                        local.Initializer, localFieldBackingDependencies, function,
                        program, ref remaining, depth + 1);
                }
                if (local.Initializer is not BoundMove
                    && CarriesNativePointer(local.Initializer, program)
                    && initialized.Dependencies is BackingDependencies localPointerDependencies)
                {
                    localPointerDependencies.SetNativePointerProvenance(local.Variable,
                        NativePointerBackingOf(local.Initializer, localPointerDependencies,
                            program, ref remaining, depth + 1),
                        NativePointerMayBeUnknownOf(local.Initializer, localPointerDependencies,
                            program, ref remaining, depth + 1));
                    var extentFact = NativePointerExtentFactsOfExpression(
                        local.Initializer, localPointerDependencies, function, program,
                        ref remaining, depth + 1);
                    localPointerDependencies.SetNativePointerExtentProvenance(
                        local.Variable, extentFact.Sources,
                        extentFact.MayBeOpaque, extentFact.IsUnavailable);
                    if (IsNativePointerFieldAggregateStorage(
                        local.Initializer.Type, program))
                    {
                        if (local.Initializer
                            is not BoundNativeFixedArrayRepeatInitialization)
                        {
                            SetNativePointerFieldValuesFromExpression(
                                local.Variable, StorageProjection.WholeObject,
                                local.Initializer.Type, local.Initializer,
                                localPointerDependencies, function, program,
                                ref remaining, depth + 1);
                        }
                        RefreshNativePointerAggregateProvenance(local.Variable,
                            local.Initializer.Type, localPointerDependencies,
                            program, ref remaining, depth + 1);
                    }
                }
                if (local.Initializer is not BoundMove
                    && initialized.Dependencies is BackingDependencies localCopyOriginDependencies)
                {
                    SetCopyOriginValuesFromExpression(local.Variable,
                        StorageProjection.WholeObject, local.Initializer.Type,
                        local.Initializer, localCopyOriginDependencies, function,
                        program, ref remaining, depth + 1);
                }
                if (local.Initializer is not BoundMove
                    && IsInlineStructStorage(local.Initializer.Type, program)
                    && initialized.Dependencies is BackingDependencies localReferenceFieldDependencies)
                {
                    SetReferenceFieldValuesFromExpression(local.Variable,
                        StorageProjection.WholeObject, local.Initializer.Type,
                        local.Initializer, localReferenceFieldDependencies,
                        initialized, function, program, ref remaining, depth + 1,
                        fieldMasks);
                }
                return false;
            case BoundVacantLocal vacant:
                if (vacant.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    || vacant.Variable >= initialized.Length
                    || !declarations.Add(vacant.Variable) || initialized[vacant.Variable]
                    || !function.Variables[vacant.Variable].IsInitializationLocal)
                {
                    throw Invalid(vacant.Span);
                }
                initialized.ActivateStorage(vacant.Variable, (long)vacant.Span.Start + 1);
                initialized.InvalidateNativeOutputVariable(vacant.Variable);
                initialized[vacant.Variable] = false;
                return false;
            case BoundOwnerDeclaration output:
                if (output.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    || output.Variable >= initialized.Length
                    || !IsUnique(program, function.Variables[output.Variable].Type)
                    || function.Variables[output.Variable].IsInitializationLocal
                    || initialized[output.Variable] || declarations.Contains(output.Variable))
                {
                    throw Invalid(output.Span);
                }
                initialized.ActivateStorage(output.Variable, (long)output.Span.Start + 1);
                return false;
            case BoundLifetimeEnd end:
                if (end.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    || end.Variable >= initialized.Length
                    || !declarations.Contains(end.Variable)
                    || (!initialized[end.Variable] && !initialized.HasPartialValue(end.Variable))
                    || (function.Variables[end.Variable].Type is not (PrimitiveType.ReadonlyInt32BufferView
                        or PrimitiveType.MutableInt32BufferView)
                        && !ValidType(function.Variables[end.Variable].Type, allowVoid: true, program)))
                {
                    throw Invalid(end.Span);
                }
                var endType = function.Variables[end.Variable].Type;
                if (initialized.HasPartialValue(end.Variable)
                    && (!IsUnique(program, endType) || endType == PrimitiveType.OwnedInt32Buffer
                        || ManagedClassTypes.IsClass(endType)
                        || program.Structures[(int)endType - 100].CleanupMethod is not null))
                {
                    throw InvalidEffect(end.Span, "Ownership");
                }
                var endMethod = IsUnique(program, endType) && endType != PrimitiveType.OwnedInt32Buffer
                    ? program.Structures[(int)endType - 100].CleanupMethod : null;
                if (endMethod is null ? end.CleanupFunction != -1
                    : end.CleanupFunction < 0 || end.CleanupFunction >= program.Functions.Length
                        || program.Functions[end.CleanupFunction].Name != endMethod)
                {
                    throw Invalid(end.Span);
                }
                CheckBackingInvalidation(end.Variable, initialized, end.Span);
                CheckReferencePlaceInvalidation(end.Variable,
                    StorageProjection.WholeObject, initialized, end.Span);
                CheckNativePointerLifetimeEnd(end.Variable, initialized, end.Span);
                if (program.RuntimePolicy == BoundRuntimePolicy.CheckedExceptions
                    && CleanupMayFail(endType, program))
                {
                    AddCleanupFailure(failures, initialized, fieldMasks, end.Variable);
                }
                initialized.InvalidateNativeOutputVariable(end.Variable);
                initialized.Retire(end.Variable, (long)end.Span.Start + 1);
                initialized.Dependencies?.ClearBackingSources(end.Variable);
                fieldMasks[end.Variable] = 0;
                initialized.SetPossibleFieldMask(end.Variable, 0);
                return false;
            case BoundDrop drop:
                if (!inCleanup || drop.Variable < 0 || drop.Variable >= initialized.Length
                    || (drop.Variable >= function.ParameterCount + (function.IsConstructor ? 1 : 0)
                        && !declarations.Contains(drop.Variable))
                    || !IsUnique(program, function.Variables[drop.Variable].Type)
                    || (drop.IsOutput && (!function.Variables[drop.Variable].IsOut
                        || (function.ReturnType != PrimitiveType.Bool && !function.Variables[drop.Variable].IsInit))))
                {
                    throw Invalid(drop.Span);
                }
                var method = function.Variables[drop.Variable].Type == PrimitiveType.OwnedInt32Buffer
                    ? null : program.Structures[(int)function.Variables[drop.Variable].Type - 100].CleanupMethod;
                if (method is null ? drop.CleanupFunction != -1
                    : drop.CleanupFunction < 0 || drop.CleanupFunction >= program.Functions.Length
                        || program.Functions[drop.CleanupFunction].Name != method)
                {
                    throw Invalid(drop.Span);
                }
                CheckBackingInvalidation(drop.Variable, initialized, drop.Span);
                CheckReferencePlaceInvalidation(drop.Variable,
                    StorageProjection.WholeObject, initialized, drop.Span);
                CheckNativePointerLifetimeEnd(drop.Variable, initialized, drop.Span);
                if (program.RuntimePolicy == BoundRuntimePolicy.CheckedExceptions
                    && CleanupMayFail(function.Variables[drop.Variable].Type, program))
                {
                    AddCleanupFailure(failures, initialized, fieldMasks, drop.Variable);
                }
                initialized.InvalidateNativeOutputVariable(drop.Variable);
                initialized[drop.Variable] = false;
                initialized.Dependencies?.ClearBackingSources(drop.Variable);
                initialized.AdvanceStorageGeneration(drop.Variable, (long)drop.Span.Start + 1);
                if (initialized.Dependencies is BackingDependencies dropDependencies)
                {
                    dropDependencies.ClearNativePointerProvenance(drop.Variable);
                }
                fieldMasks[drop.Variable] = 0;
                initialized.SetPossibleFieldMask(drop.Variable, 0);
                return false;
            case BoundDropSpan span:
                if (!inCleanup || span.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    || span.Variable >= initialized.Length || !declarations.Contains(span.Variable)
                    || function.Variables[span.Variable].Type != PrimitiveType.TokenSpan)
                {
                    throw InvalidEffect(span.Span, "Ownership");
                }
                initialized[span.Variable] = false;
                initialized.Dependencies?.ClearBackingSources(span.Variable);
                initialized.AdvanceStorageGeneration(span.Variable, (long)span.Span.Start + 1);
                return false;
            case BoundDropConstructedFields partial:
                if (!inCleanup || !function.IsConstructor || partial.Variable != 0
                    || partial.AcquisitionOrder.IsDefault)
                {
                    throw Invalid(partial.Span);
                }
                if (program.RuntimePolicy == BoundRuntimePolicy.CheckedExceptions && failures is not null)
                {
                    var fields = program.Structures[(int)function.ReturnType - 100].Fields;
                    for (var index = partial.AcquisitionOrder.Length - 1; index >= 0; index--)
                    {
                        var fieldIndex = partial.AcquisitionOrder[index];
                        if (fieldIndex < 0 || fieldIndex >= fields.Length)
                        {
                            throw Invalid(partial.Span);
                        }
                        if (IsUnique(program, fields[fieldIndex].Type)
                            && CleanupMayFail(fields[fieldIndex].Type, program))
                        {
                            using var repeatSnapshotState
                                = CreateRepeatSnapshotScopeIfNeeded(initialized);
                            var failureState = initialized.Clone();
                            failureState.MarkConditional(0);
                            var failureMasks = (uint[])fieldMasks.Clone();
                            failureMasks[0] = 0;
                            failures.Add(failureState, failureMasks);
                            break;
                        }
                    }
                }
                return false;
            case BoundAssignment assignment:
                if (assignment.Variable < 0 || assignment.Variable >= initialized.Length
                    || (assignment.Variable < function.ParameterCount && !function.Variables[assignment.Variable].IsOut)
                    || IsConstructionHome(function, assignment.Variable)
                    || (!initialized[assignment.Variable] && !function.Variables[assignment.Variable].IsOut
                        && !declarations.Contains(assignment.Variable))
                    || (!function.Variables[assignment.Variable].IsMutable
                        && (!function.Variables[assignment.Variable].IsInitializationLocal
                            || !initialized.IsVacant(assignment.Variable))))
                {
                    throw Invalid(assignment.Span);
                }
                if (function.Variables[assignment.Variable].IsInitializationLocal
                    && InvalidatesReservedOwner(assignment.Value, assignment.Variable, ref remaining, depth + 1))
                {
                    throw InvalidEffect(assignment.Span, "Ownership");
                }
                var nullaryArrayInitialization
                    = assignment.Value is BoundNativeFixedArrayInitialization
                        { Layout.ElementCount: 0, Elements.IsEmpty: true };
                var wholeArrayValueCopy
                    = NativeFixedArrayContract.IsCarrier(
                        function.Variables[assignment.Variable].Type)
                        && assignment.Value is BoundRead or BoundMove;
                var wholeArrayRepeatInitialization
                    = NativeFixedArrayContract.IsCarrier(
                        function.Variables[assignment.Variable].Type)
                        && assignment.Value
                            is BoundNativeFixedArrayRepeatInitialization;
                var assignmentCouldComplete = nullaryArrayInitialization
                    || CanCompleteNormally(assignment.Value, initialized,
                        program, ref remaining, depth + 1);
                if (assignmentCouldComplete)
                {
                    CheckBackingInvalidation(assignment.Variable, initialized, assignment.Span);
                    if (IsUnique(program, assignment.Value.Type)
                        && HasFields(program, assignment.Value.Type))
                    {
                        CheckReferencePlaceInvalidation(assignment.Variable,
                            StorageProjection.WholeObject, initialized, assignment.Span);
                    }
                }
                if (assignment.Value is BoundMove assignmentMove)
                {
                    CheckNativePointerLifetimeEnd(
                        assignmentMove.Variable, initialized, assignmentMove.Span);
                    initialized.Dependencies?.TransferBackingSource(
                        assignmentMove.Variable, assignment.Variable);
                }
                bool assignmentCompletes;
                if (nullaryArrayInitialization)
                {
                    if (!function.Variables[assignment.Variable].IsInitializationLocal
                        || !initialized.IsVacant(assignment.Variable))
                    {
                        throw InvalidEffect(assignment.Span, "NativeImport");
                    }

                    VerifyNativeFixedArrayConstruction(assignment.Value,
                        assignment.Variable, initialized, fieldMasks,
                        declarations, function, program, ref remaining,
                        depth + 1, failures);
                    assignmentCompletes = true;
                }
                else if (wholeArrayValueCopy
                    || wholeArrayRepeatInitialization)
                {
                    if (wholeArrayRepeatInitialization
                        && !assignmentCouldComplete)
                    {
                        using var repeatSnapshotState
                            = CreateRepeatSnapshotScopeIfNeeded(initialized);
                        var beforeCall = initialized.Clone();
                        var beforeMasks = (uint[])fieldMasks.Clone();
                        VerifyNativeFixedArrayConstruction(assignment.Value,
                            assignment.Variable, initialized, fieldMasks,
                            declarations, function, program, ref remaining,
                            depth + 1, failures);
                        beforeCall.CopyTo(initialized);
                        beforeMasks.CopyTo(fieldMasks, 0);
                        assignmentCompletes = false;
                    }
                    else
                    {
                        VerifyNativeFixedArrayConstruction(assignment.Value,
                            assignment.Variable, initialized, fieldMasks,
                            declarations, function, program, ref remaining,
                            depth + 1, failures);
                        assignmentCompletes = true;
                    }
                }
                else
                {
                    assignmentCompletes = EvaluateExpression(
                        assignment.Value, initialized, fieldMasks,
                        declarations, function, program, ref remaining,
                        depth + 1, failures,
                        cleanupCounterStep: cleanupCounterStep);
                }
                if (assignment.Value.Type != function.Variables[assignment.Variable].Type)
                {
                    throw Invalid();
                }
                if (!assignmentCompletes)
                {
                    return true;
                }
                initialized.ResetNestedOwnerStates(assignment.Variable);
                if (IsUnique(program, assignment.Value.Type)
                    && HasFields(program, assignment.Value.Type))
                {
                    initialized.AdvanceFieldPlaceGeneration(assignment.Variable,
                        StorageProjection.WholeObject, assignment.Span);
                }
                var nativeOutputAssignmentResult = NativeOutputResultForValue(
                    assignment.Value, initialized, assignment.Variable);
                if (HasFields(program, assignment.Value.Type))
                {
                    fieldMasks[assignment.Variable] = FullMask(program, assignment.Value.Type);
                    initialized.SetPossibleFieldMask(
                        assignment.Variable, fieldMasks[assignment.Variable]);
                }
                initialized.MarkAssigned(assignment.Variable);
                if (assignment.Value is BoundRead assignmentAggregateRead
                    && IsInlineStructStorage(assignment.Value.Type, program))
                {
                    _ = initialized.CopyRawUnionArrayFacts(
                        assignment.Variable,
                        assignmentAggregateRead.Variable);
                }
                if (assignment.Value is BoundCall assignmentCall
                    && initialized.Dependencies is BackingDependencies
                        assignmentCallDependencies
                    && assignmentCallDependencies
                        .TryGetRawUnionArrayCallResult(assignmentCall,
                            out var assignmentArrayFacts))
                {
                    _ = initialized.SetRawUnionArrayFacts(
                        assignment.Variable,
                        StorageProjection.WholeObject,
                        assignmentArrayFacts, ref remaining, depth + 1);
                    assignmentCallDependencies
                        .ClearRawUnionArrayCallResult(assignmentCall);
                }
                if (CarriesRawUnionPayloadFacts(
                        program, assignment.Value.Type))
                {
                    initialized.SetRawUnionInitializedBytes(
                        assignment.Variable,
                        InitializedPayloadBytesOfExpression(
                            assignment.Value, assignment.Value.Type,
                            initialized, function, program));
                }
                initialized.SetNativeOutputResult(assignment.Variable, nativeOutputAssignmentResult);
                if (program.NativeLoweringAgreement
                        == CompilationOptions.WindowsX64NormalBackingAgreement
                    && IsNormalBackingObjectType(assignment.Value.Type)
                    && initialized.Dependencies is BackingDependencies
                        assignmentObjectDependencies)
                {
                    var objects = NormalBackingObjectsOfExpression(
                        assignment.Value, assignmentObjectDependencies,
                        initialized, function, program, ref remaining, depth + 1,
                        out var mayBeUnknown);
                    assignmentObjectDependencies.SetNormalBackingObjects(
                        assignment.Variable, objects, mayBeUnknown);
                }
                if (CheckedReferenceTypeContract.IsValidType(
                        assignment.Value.Type, program.CheckedReferenceTypes))
                {
                    if (initialized.Dependencies is not BackingDependencies referenceDependencies)
                    {
                        throw InvalidEffect(assignment.Span, "CheckedReference");
                    }
                    referenceDependencies.SetReferenceTargets(assignment.Variable,
                        ReferenceTargetsOfExpression(assignment.Value, referenceDependencies,
                            initialized, function, program, ref remaining, depth + 1,
                            fieldMasks));
                    referenceDependencies.SetReferenceObligations(assignment.Variable,
                        ReferenceObligationsOfExpression(assignment.Value,
                            referenceDependencies, initialized, function, program,
                            ref remaining, depth + 1, fieldMasks));
                }
                if (assignment.Value is not BoundMove
                    && TokenSpanContract.IsDependent(assignment.Value.Type, program.Structures)
                    && initialized.Dependencies is BackingDependencies assignmentBackingDependencies)
                {
                    assignmentBackingDependencies.SetBackingSources(assignment.Variable,
                        BackingOf(assignment.Value, assignmentBackingDependencies, program,
                            ref remaining, depth + 1));
                }
                if (assignment.Value is not BoundMove
                    && IsInlineStructStorage(assignment.Value.Type, program)
                    && TokenSpanContract.IsDependent(assignment.Value.Type, program.Structures)
                    && initialized.Dependencies is BackingDependencies assignmentFieldBackingDependencies)
                {
                    SetBackingFieldValuesFromExpression(assignment.Variable,
                        StorageProjection.WholeObject, assignment.Value.Type,
                        assignment.Value, assignmentFieldBackingDependencies, function,
                        program, ref remaining, depth + 1);
                }
                if (assignment.Value is not BoundMove
                    && CarriesNativePointer(assignment.Value, program)
                    && initialized.Dependencies is BackingDependencies assignmentPointerDependencies)
                {
                    assignmentPointerDependencies.SetNativePointerProvenance(assignment.Variable,
                        NativePointerBackingOf(assignment.Value, assignmentPointerDependencies,
                            program, ref remaining, depth + 1),
                        NativePointerMayBeUnknownOf(assignment.Value, assignmentPointerDependencies,
                            program, ref remaining, depth + 1));
                    var extentFact = NativePointerExtentFactsOfExpression(
                        assignment.Value, assignmentPointerDependencies, function, program,
                        ref remaining, depth + 1);
                    assignmentPointerDependencies.SetNativePointerExtentProvenance(
                        assignment.Variable, extentFact.Sources,
                        extentFact.MayBeOpaque, extentFact.IsUnavailable);
                    if (IsInlineStructStorage(assignment.Value.Type, program))
                    {
                        SetNativePointerFieldValuesFromExpression(assignment.Variable,
                            StorageProjection.WholeObject, assignment.Value.Type,
                            assignment.Value, assignmentPointerDependencies, function,
                            program, ref remaining, depth + 1);
                        RefreshNativePointerAggregateProvenance(assignment.Variable,
                            assignment.Value.Type, assignmentPointerDependencies,
                            program, ref remaining, depth + 1);
                    }
                }
                if (assignment.Value is not BoundMove
                    && initialized.Dependencies is BackingDependencies assignmentCopyOriginDependencies)
                {
                    SetCopyOriginValuesFromExpression(assignment.Variable,
                        StorageProjection.WholeObject, assignment.Value.Type,
                        assignment.Value, assignmentCopyOriginDependencies, function,
                        program, ref remaining, depth + 1);
                }
                if (assignment.Value is not BoundMove
                    && IsInlineStructStorage(assignment.Value.Type, program)
                    && initialized.Dependencies is BackingDependencies assignmentReferenceFieldDependencies)
                {
                    SetReferenceFieldValuesFromExpression(assignment.Variable,
                        StorageProjection.WholeObject, assignment.Value.Type,
                        assignment.Value, assignmentReferenceFieldDependencies,
                        initialized, function, program, ref remaining, depth + 1,
                        fieldMasks);
                }
                return false;
            case BoundOptionalAssignment optionalAssignment:
            {
                if (optionalAssignment.Place is null || optionalAssignment.Value is null
                    || !IsSupportedOptionalType(optionalAssignment.Place.Type, program)
                    || optionalAssignment.Value.Type != optionalAssignment.Place.Type)
                {
                    throw Invalid(optionalAssignment.Span);
                }
                if (optionalAssignment.Place is BoundRead localPlace)
                {
                    if (localPlace.Variable < 0 || localPlace.Variable >= initialized.Length
                        || (localPlace.Variable < function.ParameterCount
                            && !function.Variables[localPlace.Variable].IsOut)
                        || IsConstructionHome(function, localPlace.Variable)
                        || !function.Variables[localPlace.Variable].IsMutable)
                    {
                        throw Invalid(optionalAssignment.Span);
                    }
                }
                else if (optionalAssignment.Place is BoundFieldRead fieldPlace)
                {
                    if (!ValidType(fieldPlace.Receiver.Type, allowVoid: false, program)
                        || !ManagedClassTypes.IsClass(fieldPlace.Receiver.Type)
                        || !ManagedClassTypes.IsWritable(fieldPlace.Receiver.Type)
                        || fieldPlace.Field < 0
                        || fieldPlace.Field >= Fields(program, fieldPlace.Receiver.Type).Length
                        || !Fields(program, fieldPlace.Receiver.Type)[fieldPlace.Field].IsMutable)
                    {
                        throw Invalid(optionalAssignment.Span);
                    }
                }
                else
                {
                    throw Invalid(optionalAssignment.Span);
                }
                if (!EvaluateExpression(optionalAssignment.Place, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures))
                {
                    ValidateUnreachableExpression(optionalAssignment.Value, initialized, fieldMasks,
                        declarations, function, program, ref remaining, depth + 1);
                    return true;
                }
                using var repeatSnapshotState
                    = CreateRepeatSnapshotScopeIfNeeded(initialized);
                var assignedState = initialized.Clone();
                var assignedMasks = (uint[])fieldMasks.Clone();
                var assignedDeclarations = new HashSet<int>(declarations);
                var valueCompletes = EvaluateExpression(optionalAssignment.Value, assignedState,
                    assignedMasks, assignedDeclarations, function, program, ref remaining,
                    depth + 1, failures);
                declarations.UnionWith(assignedDeclarations);
                if (!valueCompletes)
                {
                    return true;
                }
                if (optionalAssignment.Place is BoundRead optionalReferenceLocal
                    && CheckedReferenceTypeContract.IsValidType(
                        optionalReferenceLocal.Type, program.CheckedReferenceTypes)
                    && program.CheckedReferenceTypes[
                        CheckedReferenceTypes.Index(optionalReferenceLocal.Type)].IsOptional)
                {
                    if (assignedState.Dependencies is not BackingDependencies
                        optionalReferenceDependencies)
                    {
                        throw InvalidEffect(optionalAssignment.Span, "CheckedReference");
                    }
                    var referenceValue = ReferenceFieldValueOfDescriptorExpression(
                        optionalAssignment.Value, optionalReferenceDependencies,
                        assignedState, function, program, ref remaining,
                        depth + 1, assignedMasks);
                    if (!referenceValue.IsAvailable)
                    {
                        throw InvalidEffect(optionalAssignment.Span, "CheckedReference");
                    }
                    optionalReferenceDependencies.SetReferenceTargets(
                        optionalReferenceLocal.Variable, referenceValue.Targets);
                    optionalReferenceDependencies.SetReferenceObligations(
                        optionalReferenceLocal.Variable, referenceValue.Obligations);
                    optionalReferenceDependencies.SetReferenceBackingSources(
                        optionalReferenceLocal.Variable, referenceValue.BackingSources);
                }
                else if (optionalAssignment.Place is BoundFieldRead optionalReferenceField
                    && IsOrdinaryStoredReferenceType(optionalReferenceField.Type, program)
                    && TryGetNormalBackingClassPlace(optionalReferenceField,
                        function, program, out var optionalFieldRoot,
                        out var optionalFieldProjection)
                    && assignedState.Dependencies is BackingDependencies
                        optionalFieldDependencies)
                {
                    SetReferenceFieldValuesFromExpression(
                        optionalFieldRoot, optionalFieldProjection,
                        optionalReferenceField.Type, optionalAssignment.Value,
                        optionalFieldDependencies, assignedState, function,
                        program, ref remaining, depth + 1, assignedMasks);
                }
                for (var index = 0; index < initialized.Length; index++)
                {
                    initialized.Merge(index, assignedState);
                    fieldMasks[index] &= assignedMasks[index];
                }
                return false;
            }
            case BoundFieldAssignment fieldAssignment:
                if (fieldAssignment.Variable < 0 || fieldAssignment.Variable >= initialized.Length
                    || (!function.Variables[fieldAssignment.Variable].IsMutable
                        && !ManagedClassTypes.IsClass(function.Variables[fieldAssignment.Variable].Type))
                    || fieldAssignment.NestedFields.IsDefault)
                {
                    throw Invalid();
                }
                var ownerType = function.Variables[fieldAssignment.Variable].Type;
                if (!TryGetFieldAssignmentProjectionPath(fieldAssignment,
                    ownerType, program, out var fieldProjectionPath))
                {
                    throw Invalid();
                }
                var fieldSegments = fieldProjectionPath.Segments;
                var rootField = Fields(program, ownerType)[
                    fieldAssignment.Field];
                var fixedArrayFieldOwner = fieldSegments[^1].OwnerType;
                var fixedArrayLeafField = Fields(program,
                    fixedArrayFieldOwner)[fieldSegments[^1].Field];
                var fixedArrayFieldPathIsCopy = !IsUnique(program,
                    ownerType)
                    && fieldSegments.All(segment =>
                        !IsUnique(program, segment.OwnerType)
                        && IsInlineStructStorage(segment.OwnerType, program)
                        && program.Structures[
                            (int)segment.OwnerType - 100]
                            .CleanupMethod is null
                        && Fields(program, segment.OwnerType)[
                            segment.Field].IsMutable);
                var initializesVacantArrayAggregateField =
                    (IsConstructionHome(function,
                        fieldAssignment.Variable)
                        || function.Variables[fieldAssignment.Variable]
                            .IsInitializationLocal)
                    && NativeFixedArrayContract
                        .HasInlineFixedArrayFieldStorage(ownerType,
                            program.Structures,
                            program.NativeFixedArrayTypes)
                    && fixedArrayFieldPathIsCopy;
                var initializesVacantCarrierField =
                    fieldSegments.Length == 1
                    && function.Variables[fieldAssignment.Variable]
                        .IsInitializationLocal
                    && CarriesRawUnionPayloadFacts(program, ownerType)
                    && !IsUnique(program, ownerType)
                    && program.Structures[(int)ownerType - 100]
                        .CleanupMethod is null
                    && rootField.IsMutable
                    && !IsUnique(program, rootField.Type);
                var initializesVacantUnion = IsRawUnionType(program, ownerType)
                    && fieldSegments.Length == 1
                    && function.Variables[fieldAssignment.Variable]
                        .IsInitializationLocal
                    && initialized.IsVacant(fieldAssignment.Variable);
                var initializesVacantNestedUnion = false;
                if (fieldSegments.Length > 1
                    && IsRawUnionType(program,
                        fieldSegments[^1].OwnerType)
                    && function.Variables[fieldAssignment.Variable]
                        .IsInitializationLocal)
                {
                    var nestedUnionOwnerPath = FieldPathPrefix(
                        fieldProjectionPath, fieldSegments.Length - 1);
                    var (nestedUnionDefinite, nestedUnionPossible)
                        = FieldPathOwnerMasks(fieldAssignment.Variable,
                            nestedUnionOwnerPath,
                            fieldSegments[^1].OwnerType, initialized,
                            fieldMasks, program);
                    initializesVacantNestedUnion
                        = (nestedUnionDefinite | nestedUnionPossible) == 0;
                }
                var initializesVacantNestedRawUnion
                    = initializesVacantUnion || initializesVacantNestedUnion
                        || initializesVacantCarrierField
                        || initializesVacantArrayAggregateField;
                var initializesNestedRawUnionInConstructor
                    = fieldSegments.Length > 1
                    && IsRawUnionType(program,
                        fieldSegments[^1].OwnerType)
                    && IsConstructionHome(function,
                        fieldAssignment.Variable);
                var initializesNestedRawUnionPath
                    = initializesVacantNestedUnion
                        || initializesNestedRawUnionInConstructor;
                if (!HasFields(program, ownerType)
                    || fieldAssignment.Field < 0 || fieldAssignment.Field >= Fields(program, ownerType).Length
                    || !CanAccessField(function, program, ownerType, fieldAssignment.Field)
                    || (!initialized[fieldAssignment.Variable]
                        && !initialized.HasPartialValue(fieldAssignment.Variable)
                        && !initializesVacantNestedRawUnion
                        && !IsConstructionHome(function,
                            fieldAssignment.Variable)))
                {
                    throw Invalid();
                }
                if (IsRawUnionType(program, ownerType)
                    && function.Variables[fieldAssignment.Variable]
                        .IsInitializationLocal
                    && !initializesVacantUnion)
                {
                    throw InvalidEffect(fieldAssignment.Span, "Ownership");
                }
                if (fieldSegments.Length > 1
                    && IsRawUnionType(program,
                        fieldSegments[^1].OwnerType)
                    && function.Variables[fieldAssignment.Variable]
                        .IsInitializationLocal
                    && !initializesVacantNestedUnion)
                {
                    throw InvalidEffect(fieldAssignment.Span, "Ownership");
                }
                if (IsUnique(program, ownerType) && function.Variables[fieldAssignment.Variable].IsOut)
                {
                    throw InvalidEffect(fieldAssignment.Span, "Ownership");
                }
                var targetField = Fields(program, ownerType)[fieldAssignment.Field];
                var requiresUnsafeCaller = RequiresUnsafeFieldAccess(
                    program, ownerType, targetField);
                if (ManagedClassTypes.IsClass(ownerType) && !ManagedClassTypes.IsWritable(ownerType)
                    && !((function.IsConstructor || function.IsClassConstructor)
                        && fieldAssignment.Variable == 0 && fieldAssignment.NestedFields.IsEmpty)
                    && (fieldAssignment.NestedFields.IsEmpty || !ManagedClassTypes.IsClass(targetField.Type)))
                {
                    throw Invalid();
                }
                var rootFieldBit = 1u << fieldAssignment.Field;
                for (var index = 0; index < fieldAssignment.NestedFields.Length; index++)
                {
                    Count(ref remaining, depth + index + 1);
                    var nestedType = targetField.Type;
                    var nestedField = fieldAssignment.NestedFields[index];
                    if (!HasFields(program, nestedType)
                        || nestedField < 0 || nestedField >= Fields(program, nestedType).Length
                        || !CanAccessField(function, program, nestedType, nestedField))
                    {
                        throw Invalid();
                    }
                    targetField = Fields(program, nestedType)[nestedField];
                    requiresUnsafeCaller |= RequiresUnsafeFieldAccess(
                        program, nestedType, targetField);
                    if (ManagedClassTypes.IsClass(nestedType) && !ManagedClassTypes.IsWritable(nestedType)
                        && (index == fieldAssignment.NestedFields.Length - 1
                            || !ManagedClassTypes.IsClass(targetField.Type)))
                    {
                        throw Invalid();
                    }
                    if (IsUnique(program, nestedType)
                        && !IsUnique(program, ownerType))
                    {
                        throw InvalidEffect(fieldAssignment.Span, "Ownership");
                    }
                    if (!targetField.IsMutable
                        && (!ManagedClassTypes.IsClass(nestedType)
                            || !ManagedClassTypes.IsClass(targetField.Type)
                            || index == fieldAssignment.NestedFields.Length - 1))
                    {
                        throw Invalid();
                    }
                }
                if (!targetField.IsMutable
                    && (!ManagedClassTypes.IsClass(ownerType)
                        || fieldAssignment.NestedFields.IsEmpty
                        || !ManagedClassTypes.IsClass(targetField.Type))
                    && !((function.IsConstructor || function.IsClassConstructor)
                        && fieldAssignment.Variable == 0))
                {
                    throw Invalid();
                }
                var fieldPlaceProjection = StorageProjection.ForFieldPath(
                    fieldProjectionPath);
                var normalBackingPartialReferenceAggregate =
                    program.NativeLoweringAgreement
                        == CompilationOptions.WindowsX64NormalBackingAgreement
                    && (function.IsConstructor || function.IsClassConstructor)
                    && fieldAssignment.Variable == 0
                    && ManagedClassTypes.IsClass(ownerType)
                    && fieldSegments.Length > 1
                    && IsInlineStructStorage(
                        Fields(program, ownerType)[fieldAssignment.Field].Type, program)
                    && ReferenceFieldLeaves(
                        Fields(program, ownerType)[fieldAssignment.Field].Type,
                        program, ref remaining, depth + 1).Count != 0;
                var fieldOwnerPath = FieldPathPrefix(fieldProjectionPath,
                    fieldSegments.Length - 1);
                var fieldOwnerType = fieldSegments[^1].OwnerType;
                var (fieldOwnerDefinite, fieldOwnerPossible) = FieldPathOwnerMasks(
                    fieldAssignment.Variable, fieldOwnerPath, fieldOwnerType,
                    initialized, fieldMasks, program);
                var leafFieldBit = 1u << fieldSegments[^1].Field;
                var fieldMayBeLiveBefore =
                    ((fieldOwnerDefinite | fieldOwnerPossible) & leafFieldBit) != 0;
                var fieldIsDefinitelyVacantBefore = !fieldMayBeLiveBefore;
                var isVacantArrayAggregateFieldInitialization
                    = initializesVacantArrayAggregateField
                        && fieldIsDefinitelyVacantBefore;
                var fieldPathIsUnique = IsUnique(program, ownerType);
                var pathOwnerType = ownerType;
                foreach (var segment in fieldSegments)
                {
                    fieldPathIsUnique &= segment.OwnerType == pathOwnerType
                        && IsUnique(program, pathOwnerType);
                    pathOwnerType = Fields(program, pathOwnerType)[segment.Field].Type;
                }
                var isConstructorFieldInitializationBefore =
                    IsConstructionHome(function,
                        fieldAssignment.Variable)
                    && fieldSegments.Length == 1
                    && fieldIsDefinitelyVacantBefore;
                var ownerPathCanBePartial = fieldPathIsUnique
                    && (IsConstructionHome(function,
                            fieldAssignment.Variable)
                        || (!function.IsConstructor && !function.IsClassConstructor
                            && !(fieldAssignment.Variable == 0
                                && function.DeclaringStruct is not null && !function.IsStatic)
                            && fieldSegments.All(segment =>
                                program.Structures[(int)segment.OwnerType - 100].CleanupMethod is null)));
                var ownerPlaceIsPartial = fieldOwnerPath.Segments.IsEmpty
                    ? initialized.HasPartialValue(fieldAssignment.Variable)
                    : fieldOwnerDefinite != FullMask(program, fieldOwnerType);
                var isCarrierFieldInitialization
                    = initializesVacantCarrierField
                        && fieldIsDefinitelyVacantBefore;
                if (initializesVacantCarrierField
                    && initialized.HasPartialValue(
                        fieldAssignment.Variable)
                    && !fieldIsDefinitelyVacantBefore)
                {
                    throw InvalidEffect(fieldAssignment.Span, "Ownership");
                }
                var isPartialFieldRepairBefore = (
                    IsUnique(program, targetField.Type)
                    && targetField.IsMutable
                    && ownerPlaceIsPartial
                    && fieldIsDefinitelyVacantBefore
                    && ownerPathCanBePartial
                    && (fieldSegments.Length > 1 || fieldAssignment.Variable != 0))
                    || (isCarrierFieldInitialization
                        && ownerPlaceIsPartial)
                    || (isVacantArrayAggregateFieldInitialization
                        && ownerPlaceIsPartial);
                var isLiveFieldReplacementCandidate =
                    fieldPathIsUnique
                    && IsUnique(program, targetField.Type)
                    && targetField.IsMutable
                    && fieldMayBeLiveBefore
                    && !isConstructorFieldInitializationBefore
                    && !isPartialFieldRepairBefore;
                if (fieldAssignment.NestedFields.IsEmpty == false)
                {
                    for (var index = 0; index < fieldSegments.Length - 1; index++)
                    {
                        var segment = fieldSegments[index];
                        var ownerPath = FieldPathPrefix(fieldProjectionPath, index);
                        var (_, ownerPossible) = FieldPathOwnerMasks(fieldAssignment.Variable,
                            ownerPath, segment.OwnerType, initialized, fieldMasks, program);
                        if ((ownerPossible & (1u << segment.Field)) == 0
                            && !(normalBackingPartialReferenceAggregate && index == 0)
                            && !initializesNestedRawUnionPath
                            && !isVacantArrayAggregateFieldInitialization)
                        {
                            throw InvalidEffect(fieldAssignment.Span, "Ownership");
                        }
                    }
                    if (!fieldPathIsUnique
                        && !normalBackingPartialReferenceAggregate
                        && !initializesNestedRawUnionPath
                        && !isVacantArrayAggregateFieldInitialization
                        && (fieldMasks[fieldAssignment.Variable] & rootFieldBit) == 0)
                    {
                        throw InvalidEffect(fieldAssignment.Span, "Ownership");
                    }
                }
                if (initialized.HasPartialValue(fieldAssignment.Variable)
                    && fieldIsDefinitelyVacantBefore
                    && !isPartialFieldRepairBefore
                    && !isConstructorFieldInitializationBefore
                    && !initializesNestedRawUnionPath
                    && !isVacantArrayAggregateFieldInitialization)
                {
                    throw InvalidEffect(fieldAssignment.Span, "Ownership");
                }
                if (requiresUnsafeCaller && !fieldAssignment.IsUnsafeAuthorized)
                {
                    throw InvalidEffect(fieldAssignment.Span, "UnsafeCaller");
                }
                if (IsUnique(program, targetField.Type)
                    && fieldAssignment.Value is not (BoundMove or BoundFieldMove or BoundCall))
                {
                    throw InvalidEffect(fieldAssignment.Span, "Ownership");
                }
                if (fieldAssignment.Value.Type != targetField.Type)
                {
                    throw Invalid();
                }
                var fixedArrayField = NativeFixedArrayTypes.IsType(
                    targetField.Type);
                var fixedArrayFieldPrefix
                    = StorageProjection.WholeObject;
                if (fixedArrayField)
                {
                    if (!NativeFixedArrayContract.TryGetLayout(
                            targetField.Type, program.Structures,
                            program.NativeFixedArrayTypes,
                            out var fixedArrayLayout)
                        || !NativeFixedArrayContract.MatchesLayout(
                            fixedArrayLayout, program)
                        || !NativeFixedArrayContract.IsRootlessCopyFieldType(
                            function.Variables[
                                fieldAssignment.Variable].Type,
                            program.Structures,
                            program.NativeFixedArrayTypes)
                        || targetField.Size < 0
                        || fixedArrayLayout.ByteExtent
                            != (ulong)targetField.Size)
                    {
                        throw InvalidEffect(fieldAssignment.Span,
                            "NativeImport");
                    }

                    var arrayOwnerType
                        = function.Variables[
                            fieldAssignment.Variable].Type;
                    foreach (var segment in fieldSegments)
                    {
                        if (segment.OwnerType != arrayOwnerType
                            || IsUnique(program, arrayOwnerType)
                            || !IsInlineStructStorage(arrayOwnerType, program)
                            || !HasFields(program, arrayOwnerType)
                            || (uint)segment.Field
                                >= (uint)Fields(program,
                                    arrayOwnerType).Length)
                        {
                            throw InvalidEffect(fieldAssignment.Span,
                                "NativeImport");
                        }
                        arrayOwnerType = Fields(program, arrayOwnerType)[
                            segment.Field].Type;
                    }
                    if (arrayOwnerType != targetField.Type)
                    {
                        throw InvalidEffect(fieldAssignment.Span,
                            "NativeImport");
                    }

                    fixedArrayFieldPrefix = StorageProjection.ForFieldPath(
                        fieldProjectionPath);
                }
                // A nested value place remains usable only while its live owner remains complete.
                var preserveValueOwner = initialized[fieldAssignment.Variable]
                    && !ManagedClassTypes.IsClass(ownerType)
                    && !IsRawUnionType(program, ownerType);
                var fieldValueCouldComplete = CanCompleteNormally(fieldAssignment.Value,
                    initialized, program, ref remaining, depth + 1);
                if (fieldValueCouldComplete && isPartialFieldRepairBefore)
                {
                    CheckReferencePlaceInvalidation(fieldAssignment.Variable,
                        fieldPlaceProjection, initialized, fieldAssignment.Span);
                }
                if (fieldValueCouldComplete && !isPartialFieldRepairBefore
                    && !isLiveFieldReplacementCandidate)
                {
                    CheckBackingInvalidation(fieldAssignment.Variable, initialized, fieldAssignment.Span);
                }
                var fieldValueCompletes = fixedArrayField
                    ? VerifyNativeFixedArrayFieldCopy(
                        fieldAssignment.Value, fieldAssignment.Variable,
                        fixedArrayFieldPrefix, targetField.Type,
                        initialized, declarations, function, program,
                        ref remaining, depth + 1)
                    : EvaluateExpression(fieldAssignment.Value, initialized,
                        fieldMasks, declarations, function, program,
                        ref remaining, depth + 1, failures);
                if (!fieldValueCompletes)
                {
                    return true;
                }
                if (fieldValueCouldComplete
                    && (IsOrdinaryStoredReferenceType(targetField.Type, program)
                        || (IsInlineStructStorage(targetField.Type, program)
                            && ReferenceFieldLeaves(targetField.Type, program,
                                ref remaining, depth + 1).Count != 0)))
                {
                    CheckReferenceDescriptorInvalidation(fieldAssignment.Variable,
                        fieldPlaceProjection, initialized, fieldAssignment.Span);
                }
                var (fieldOwnerDefiniteAfter, fieldOwnerPossibleAfter) = FieldPathOwnerMasks(
                    fieldAssignment.Variable, fieldOwnerPath, fieldOwnerType,
                    initialized, fieldMasks, program);
                var fieldMayBeLiveAfter =
                    ((fieldOwnerDefiniteAfter | fieldOwnerPossibleAfter)
                        & leafFieldBit) != 0;
                var fieldIsDefinitelyVacantAfter = !fieldMayBeLiveAfter;
                var isConstructorFieldInitialization =
                    IsConstructionHome(function,
                        fieldAssignment.Variable)
                    && fieldSegments.Length == 1
                    && fieldIsDefinitelyVacantAfter;
                var ownerPlaceIsPartialAfter = fieldOwnerPath.Segments.IsEmpty
                    ? initialized.HasPartialValue(fieldAssignment.Variable)
                    : fieldOwnerDefiniteAfter != FullMask(program, fieldOwnerType);
                var isPartialFieldRepair = (
                    initialized.HasPartialValue(fieldAssignment.Variable)
                    && ownerPlaceIsPartialAfter
                    && targetField.IsMutable
                    && fieldIsDefinitelyVacantAfter
                    && ownerPathCanBePartial
                    && (fieldSegments.Length > 1
                        || (!function.IsConstructor && !function.IsClassConstructor
                            && fieldAssignment.Variable != 0)))
                    || (isCarrierFieldInitialization
                        && ownerPlaceIsPartialAfter)
                    || (isVacantArrayAggregateFieldInitialization
                        && ownerPlaceIsPartialAfter);
                var isLiveFieldReplacement = fieldPathIsUnique
                    && IsUnique(program, targetField.Type)
                    && targetField.IsMutable
                    && fieldMayBeLiveAfter
                    && !isConstructorFieldInitialization
                    && !isPartialFieldRepair;
                if (IsUnique(program, targetField.Type)
                    && !isConstructorFieldInitialization && !isPartialFieldRepair
                    && !isLiveFieldReplacement)
                {
                    throw InvalidEffect(fieldAssignment.Span, "Ownership");
                }
                if (isLiveFieldReplacement)
                {
                    if (CleanupMayFail(targetField.Type, program))
                    {
                        throw InvalidEffect(fieldAssignment.Span, "Ownership");
                    }
                    CheckReferencePlaceInvalidation(fieldAssignment.Variable,
                        fieldPlaceProjection, initialized, fieldAssignment.Span);
                    CheckBackingFieldInvalidation(fieldAssignment.Variable,
                        fieldPlaceProjection, initialized, fieldAssignment.Span);
                }
                else if (isPartialFieldRepair)
                {
                    CheckReferencePlaceInvalidation(fieldAssignment.Variable,
                        fieldPlaceProjection, initialized, fieldAssignment.Span);
                }
                if (preserveValueOwner && (!initialized[fieldAssignment.Variable]
                    || fieldMasks[fieldAssignment.Variable] != FullMask(program, ownerType)))
                {
                    throw InvalidEffect(fieldAssignment.Span, "Ownership");
                }
                if (isPartialFieldRepair || isLiveFieldReplacement)
                {
                    initialized.AdvanceFieldPlaceGeneration(fieldAssignment.Variable,
                        fieldPlaceProjection, fieldAssignment.Span);
                }
                if (CarriesRawUnionPayloadFacts(program,
                        function.Variables[fieldAssignment.Variable].Type)
                    && TryGetFieldPathByteOffset(
                        function.Variables[fieldAssignment.Variable].Type,
                        fieldProjectionPath, program, out var rawUnionOffset))
                {
                    var initializedValueBytes
                        = InitializedPayloadBytesOfExpression(
                            fieldAssignment.Value, targetField.Type,
                            initialized, function, program);
                    initialized.ReplaceRawUnionPayloadRange(
                        fieldAssignment.Variable, rawUnionOffset,
                        initializedValueBytes);
                }
                if (initialized.Dependencies
                    is BackingDependencies rawUnionDependencies)
                {
                    InvalidateOverlappingRawUnionFacts(
                        fieldAssignment.Variable,
                        function.Variables[fieldAssignment.Variable].Type,
                        fieldProjectionPath, targetField.Size,
                        rawUnionDependencies, program);
                }
                if (initialized.Dependencies is BackingDependencies fieldCopyOriginDependencies
                    && TryGetNativePointerFieldAssignmentPlace(fieldAssignment,
                        function, program, out var copyOriginProjection,
                        out var copyOriginType))
                {
                    if (copyOriginType != targetField.Type)
                    {
                        throw Invalid(fieldAssignment.Span);
                    }
                    SetCopyOriginValuesFromExpression(fieldAssignment.Variable,
                        copyOriginProjection, copyOriginType, fieldAssignment.Value,
                        fieldCopyOriginDependencies, function, program,
                        ref remaining, depth + 1);
                }
                if (targetField.Type == PrimitiveType.TokenSpan)
                {
                    VerifySpanFieldWrite(fieldAssignment, function, initialized);
                    if (initialized.Dependencies is BackingDependencies fieldBackingDependencies)
                    {
                        if (!TryGetNativePointerFieldAssignmentPlace(fieldAssignment,
                            function, program, out var backingFieldProjection,
                            out var backingFieldValueType)
                            || backingFieldValueType != targetField.Type)
                        {
                            throw Invalid(fieldAssignment.Span);
                        }
                        SetBackingFieldValuesFromExpression(fieldAssignment.Variable,
                            backingFieldProjection, backingFieldValueType,
                            fieldAssignment.Value, fieldBackingDependencies, function,
                            program, ref remaining, depth + 1);
                    }
                }
                if (CarriesNativePointer(targetField.Type, program)
                    && initialized.Dependencies is BackingDependencies fieldPointerDependencies)
                {
                    if (TryGetNativePointerFieldAssignmentPlace(fieldAssignment,
                            function, program, out var fieldProjection,
                            out var fieldValueType))
                    {
                        if (fieldValueType != targetField.Type)
                        {
                            throw Invalid(fieldAssignment.Span);
                        }
                        SetNativePointerFieldValuesFromExpression(
                            fieldAssignment.Variable, fieldProjection,
                            fieldValueType, fieldAssignment.Value,
                            fieldPointerDependencies, function, program,
                            ref remaining, depth + 1);
                        RefreshNativePointerAggregateProvenance(
                            fieldAssignment.Variable, ownerType,
                            fieldPointerDependencies, program,
                            ref remaining, depth + 1);
                    }
                    else
                    {
                        fieldPointerDependencies.AddNativePointerProvenance(
                            fieldAssignment.Variable,
                            NativePointerBackingOf(fieldAssignment.Value,
                                fieldPointerDependencies, program,
                                ref remaining, depth + 1),
                            NativePointerMayBeUnknownOf(fieldAssignment.Value,
                                fieldPointerDependencies, program,
                                ref remaining, depth + 1));
                    }
                }
                if (fieldSegments.Length == 1)
                {
                    initialized.ResetNestedOwnerSubtree(fieldAssignment.Variable,
                        fieldProjectionPath);
                    var isRawUnion = IsRawUnionType(program, ownerType);
                    var fullFieldMask = FullMask(program, ownerType);
                    if (isRawUnion)
                    {
                        initialized[fieldAssignment.Variable] = true;
                    }
                    fieldMasks[fieldAssignment.Variable] = isRawUnion
                        ? fullFieldMask
                        : fieldMasks[fieldAssignment.Variable] | rootFieldBit;
                    initialized.SetPossibleFieldMask(fieldAssignment.Variable,
                        isRawUnion
                            ? fullFieldMask
                            : initialized.PossibleFieldMask(fieldAssignment.Variable)
                                | rootFieldBit);
                    if (fieldMasks[fieldAssignment.Variable] == fullFieldMask)
                    {
                        initialized[fieldAssignment.Variable] = true;
                    }
                    else if (!isRawUnion
                        && (IsUnique(program, ownerType)
                            || CarriesRawUnionPayloadFacts(program, ownerType)
                            || NativeFixedArrayContract
                                .HasInlineFixedArrayFieldStorage(ownerType,
                                    program.Structures,
                                    program.NativeFixedArrayTypes))
                        && (initialized.PossibleFieldMask(fieldAssignment.Variable) != 0
                            || initialized.HasPartialValue(fieldAssignment.Variable)))
                    {
                        initialized.MarkPartialValue(fieldAssignment.Variable);
                    }
                }
                else if (fieldPathIsUnique && IsUnique(program, targetField.Type))
                {
                    initialized.ResetNestedOwnerSubtree(fieldAssignment.Variable,
                        fieldProjectionPath);
                    SetNestedFieldLiveness(fieldAssignment.Variable,
                        fieldProjectionPath, live: true, initialized, fieldMasks,
                        function, program, fieldAssignment.Span);
                }
                else if (fieldSegments.Length > 1
                    && IsRawUnionType(program,
                        fieldSegments[^1].OwnerType))
                {
                    SetNestedFieldLiveness(fieldAssignment.Variable,
                        fieldProjectionPath, live: true, initialized, fieldMasks,
                        function, program, fieldAssignment.Span);
                }
                else if (fieldSegments.Length > 1
                    && (fixedArrayField
                        || initializesVacantArrayAggregateField))
                {
                    initialized.ResetNestedOwnerSubtree(
                        fieldAssignment.Variable, fieldProjectionPath);
                    SetNestedFieldLiveness(fieldAssignment.Variable,
                        fieldProjectionPath, live: true, initialized,
                        fieldMasks, function, program,
                        fieldAssignment.Span);
                }
                else if (normalBackingPartialReferenceAggregate)
                {
                    SetNestedFieldLiveness(fieldAssignment.Variable,
                        fieldProjectionPath, live: true, initialized, fieldMasks,
                        function, program, fieldAssignment.Span);
                }
                if ((IsInlineStructStorage(ownerType, program)
                        || (program.NativeLoweringAgreement
                                == CompilationOptions.WindowsX64NormalBackingAgreement
                            && IsNormalBackingObjectType(ownerType)))
                    && (IsOrdinaryStoredReferenceType(targetField.Type, program)
                        || IsInlineStructStorage(targetField.Type, program))
                    && initialized.Dependencies is BackingDependencies fieldReferenceDependencies)
                {
                    SetReferenceFieldValuesFromExpression(fieldAssignment.Variable,
                        fieldPlaceProjection, targetField.Type, fieldAssignment.Value,
                        fieldReferenceDependencies, initialized, function, program,
                        ref remaining, depth + 1, fieldMasks);
                }
                if (program.NativeLoweringAgreement
                        == CompilationOptions.WindowsX64NormalBackingAgreement
                    && ManagedClassTypes.IsClass(ownerType)
                    && IsNormalBackingObjectType(targetField.Type)
                    && initialized.Dependencies is BackingDependencies
                        classFieldObjectDependencies)
                {
                    SetNormalBackingClassFieldValueFromExpression(
                        fieldAssignment.Variable, fieldPlaceProjection,
                        targetField.Type, fieldAssignment.Value,
                        classFieldObjectDependencies, initialized,
                        function, program, ref remaining, depth + 1);
                }
                return false;
            case BoundBufferAssignment bufferWrite:
                if (RequiresNoFailureProof(function)
                    && !IsProvenBufferIndex(bufferWrite.Buffer, bufferWrite.Index, program))
                {
                    throw InvalidNoFailureEffect(function, bufferWrite.Span);
                }
                if (bufferWrite.Buffer < 0 || bufferWrite.Buffer >= program.Buffers.Length
                    || !program.Buffers[bufferWrite.Buffer].IsWritable)
                {
                    throw Invalid();
                }
                if (bufferWrite.Index.Type != PrimitiveType.Int32
                    || bufferWrite.Value.Type != program.Buffers[bufferWrite.Buffer].ElementType)
                {
                    throw Invalid();
                }
                if (!EvaluateExpression(bufferWrite.Index, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures))
                {
                    ValidateUnreachableExpression(bufferWrite.Value, initialized, fieldMasks,
                        declarations, function, program, ref remaining, depth + 1);
                    return true;
                }
                if (!EvaluateExpression(bufferWrite.Value, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures))
                {
                    return true;
                }
                failures?.Add(initialized, fieldMasks);
                return false;
            case BoundViewSet viewSet:
                if (viewSet.Variable < 0 || viewSet.Variable >= initialized.Length
                    || !initialized[viewSet.Variable]
                    || function.Variables[viewSet.Variable].Type != PrimitiveType.MutableInt32BufferView)
                {
                    throw InvalidEffect(viewSet.Span, "Ownership");
                }
                if (viewSet.Index.Type != PrimitiveType.Int32 || viewSet.Value.Type != PrimitiveType.Int32)
                {
                    throw Invalid();
                }
                if (RequiresNoFailureProof(function))
                {
                    throw InvalidNoFailureEffect(function, viewSet.Span);
                }
                if (!EvaluateExpression(viewSet.Index, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures))
                {
                    ValidateUnreachableExpression(viewSet.Value, initialized, fieldMasks,
                        declarations, function, program, ref remaining, depth + 1);
                    return true;
                }
                if (!EvaluateExpression(viewSet.Value, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures))
                {
                    return true;
                }
                failures?.Add(initialized, fieldMasks);
                return false;
            case BoundExpressionStatement expressionStatement:
                var effect = UnwrapUnsafeExpression(expressionStatement.Expression);
                if (effect.Type != PrimitiveType.Void
                    && effect is not (BoundCall or BoundIndirectCall or BoundViewRead
                        or BoundUpdate or BoundNominalUpdate or BoundCompoundAssignment
                        or BoundNativeFixedArrayWrite or BoundNativeFixedArrayPathWrite
                        or BoundNativePointerWrite or BoundReferenceWrite))
                {
                    throw Invalid();
                }
                var expressionCompletes = CanCompleteNormally(expressionStatement.Expression,
                    initialized, program, ref remaining, depth + 1);
                if (IsUnique(program, effect.Type) && expressionCompletes)
                {
                    throw InvalidEffect(expressionStatement.Span, "Ownership");
                }
                if (!expressionCompletes)
                {
                    using var repeatSnapshotState
                        = CreateRepeatSnapshotScopeIfNeeded(initialized);
                    var beforeExpression = initialized.Clone();
                    var beforeMasks = (uint[])fieldMasks.Clone();
                    Expression(expressionStatement.Expression, initialized, fieldMasks, declarations,
                        function, program, ref remaining, depth + 1, failures,
                        statementCompound: effect is BoundCompoundAssignment);
                    beforeExpression.CopyTo(initialized);
                    beforeMasks.CopyTo(fieldMasks, 0);
                    return true;
                }
                Expression(expressionStatement.Expression, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures, statementCompound: effect is BoundCompoundAssignment);
                return false;
            case BoundIf conditional:
            {
                using var repeatSnapshotState = new RepeatSnapshotReservationScope(
                    initialized.RepeatResources);
                if (!CanCompleteNormally(conditional.Condition, initialized, program,
                    ref remaining, depth + 1))
                {
                    if (conditional.Condition.Type != PrimitiveType.Bool || conditional.Then is null)
                    {
                        throw Invalid();
                    }
                    var beforeNonCompletingCondition = initialized.Clone(
                        repeatSnapshotState);
                    var beforeMasks = (uint[])fieldMasks.Clone();
                    Expression(conditional.Condition, initialized, fieldMasks, declarations, function, program,
                        ref remaining, depth + 1, failures);
                    beforeNonCompletingCondition.CopyTo(initialized);
                    beforeMasks.CopyTo(fieldMasks, 0);
                    var unreachableThenState = initialized.Clone(
                        repeatSnapshotState);
                    var unreachableThenMasks = (uint[])fieldMasks.Clone();
                    _ = Statement(conditional.Then, unreachableThenState, unreachableThenMasks,
                        declarations, function, program, ref remaining, depth + 1,
                        false, cleanups, inCleanup, inUnsafe, failures, loops);
                    if (conditional.Else is not null)
                    {
                        var unreachableElseState = initialized.Clone(
                            repeatSnapshotState);
                        var unreachableElseMasks = (uint[])fieldMasks.Clone();
                        _ = Statement(conditional.Else, unreachableElseState, unreachableElseMasks,
                            declarations, function, program, ref remaining, depth + 1,
                            false, cleanups, inCleanup, inUnsafe, failures, loops);
                    }
                    return true;
                }
                var beforeCondition = initialized.Clone(repeatSnapshotState);
                Expression(conditional.Condition, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                if (conditional.Condition.Type != PrimitiveType.Bool || conditional.Then is null)
                {
                    throw Invalid();
                }
                var thenReachable = CanBe(conditional.Condition, true, ref remaining, initialized);
                var elseReachable = CanBe(conditional.Condition, false, ref remaining, initialized);
                var thenState = Outcome(conditional.Condition, beforeCondition,
                    true, ref remaining, repeatSnapshotState);
                CopyOptionalReferencePatternFacts(
                    conditional.Condition, initialized, thenState, program);
                var thenMasks = (uint[])fieldMasks.Clone();
                MarkOutMasks(thenState, thenMasks, function, program);
                var thenReturns = Statement(conditional.Then, thenState, thenMasks, declarations, function, program, ref remaining, depth + 1,
                    reachable && thenReachable, cleanups, inCleanup, inUnsafe, failures, loops);
                var elseState = Outcome(conditional.Condition, beforeCondition,
                    false, ref remaining, repeatSnapshotState);
                var elseMasks = (uint[])fieldMasks.Clone();
                MarkOutMasks(elseState, elseMasks, function, program);
                var elseReturns = conditional.Else is not null
                    && Statement(conditional.Else, elseState, elseMasks, declarations, function, program, ref remaining, depth + 1,
                        reachable && elseReachable, cleanups, inCleanup, inUnsafe, failures, loops);
                for (var i = 0; i < initialized.Length; i++)
                {
                    var thenCompletes = thenReachable && !thenReturns;
                    var elseCompletes = elseReachable && !elseReturns;
                    initialized.SetPossibilities(i, !thenCompletes ? elseState.Possibilities(i)
                        : !elseCompletes ? thenState.Possibilities(i)
                        : (byte)(thenState.Possibilities(i) | elseState.Possibilities(i)));
                    fieldMasks[i] = !thenCompletes ? elseMasks[i]
                        : !elseCompletes ? thenMasks[i] : thenMasks[i] & elseMasks[i];
                    initialized.SetPossibleFieldMask(i, !thenCompletes
                        ? elseState.PossibleFieldMask(i)
                        : !elseCompletes ? thenState.PossibleFieldMask(i)
                        : thenState.PossibleFieldMask(i) | elseState.PossibleFieldMask(i));
                    if (thenCompletes && elseCompletes)
                    {
                        _ = initialized.MergeRawUnionInitializedBytes(i,
                            thenState, elseState);
                        _ = initialized.MergeFieldPlaceGenerations(i, thenState, elseState);
                        _ = initialized.MergeNestedOwnerStates(i, thenState, elseState);
                    }
                    else if (thenCompletes)
                    {
                        _ = initialized.CopyRawUnionInitializedBytes(i,
                            thenState);
                        _ = initialized.CopyFieldPlaceGenerations(i, thenState);
                        _ = initialized.CopyNestedOwnerStates(i, thenState);
                    }
                    else if (elseCompletes)
                    {
                        _ = initialized.CopyRawUnionInitializedBytes(i,
                            elseState);
                        _ = initialized.CopyFieldPlaceGenerations(i, elseState);
                        _ = initialized.CopyNestedOwnerStates(i, elseState);
                    }
                    if (thenCompletes && elseCompletes)
                    {
                        _ = initialized.CopyProvenanceSlot(i, thenState);
                        _ = initialized.MergeProvenanceSlot(i, elseState);
                    }
                    else if (thenCompletes)
                    {
                        _ = initialized.CopyProvenanceSlot(i, thenState);
                    }
                    else if (elseCompletes)
                    {
                        _ = initialized.CopyProvenanceSlot(i, elseState);
                    }
                }
                _ = initialized.MergeNativeOutputState(thenState, thenReachable && !thenReturns,
                    elseState, elseReachable && !elseReturns);
                return (!thenReachable || thenReturns) && (!elseReachable || elseReturns);
            }
            case BoundWhile loop:
            {
                using var repeatLoopState = new RepeatSnapshotReservationScope(
                    initialized.RepeatResources);
                if (inCleanup && IsMisidentifiedCleanupSwitchDispatcher(loop, function))
                {
                    throw Invalid(loop.Span);
                }
                if (inCleanup && loop.SwitchArmVariable >= 0)
                {
                    var canBeFacts = new Dictionary<BoundExpression, byte>();
                    if (ProveCleanupSwitch(loop, function, program,
                        ref remaining, depth + 1, canBeFacts) is null)
                    {
                        throw Invalid(loop.Span);
                    }
                }
                if ((loop.IsPostTested && loop.Iterator is not null)
                    || loop.Iterator is not (null or BoundLifetimeEnd or BoundAssignment or BoundExpressionStatement
                    or BoundBlock { Statements.IsEmpty: true, IsManagedLexicalScope: false, RetiredManagedLocals.IsEmpty: true }))
                {
                    throw Invalid(loop.Span);
                }
                var verifiedBody = countedLoop?.Body ?? loop.Body;
                var verifiedIterator = countedLoop?.Iterator ?? loop.Iterator;
                if (loop.Condition is null || loop.Body is null
                    || loop.Condition.Type != PrimitiveType.Bool)
                {
                    throw Invalid();
                }
                if (!loop.IsPostTested && !CanCompleteNormally(loop.Condition, initialized,
                    program, ref remaining, depth + 1))
                {
                    var beforeNonCompletingLoopCondition = initialized.Clone(
                        repeatLoopState);
                    var beforeMasks = (uint[])fieldMasks.Clone();
                    Expression(loop.Condition, initialized, fieldMasks, declarations, function,
                        program, ref remaining, depth + 1, failures);
                    beforeNonCompletingLoopCondition.CopyTo(initialized);
                    beforeMasks.CopyTo(fieldMasks, 0);

                    using var unreachableLoopState
                        = new RepeatSnapshotReservationScope(
                            initialized.RepeatResources);
                    using var unreachableScope = new LoopScope(cleanups.Count,
                        unreachableLoopState) {
                        IsCleanupSwitch = inCleanup && loop.SwitchArmVariable >= 0,
                        ScopeActivation = initialized.ScopeActivation,
                    };
                    loops.Add(unreachableScope);
                    _ = Statement(verifiedBody,
                        initialized.Clone(unreachableLoopState),
                        (uint[])fieldMasks.Clone(),
                        declarations, function, program, ref remaining, depth + 1,
                        false, cleanups, inCleanup, inUnsafe, failures, loops);
                    if (verifiedIterator is not null)
                    {
                        _ = Statement(verifiedIterator,
                            initialized.Clone(unreachableLoopState),
                            (uint[])fieldMasks.Clone(),
                            declarations, function, program, ref remaining, depth + 1,
                            false, cleanups, inCleanup, inUnsafe, failures, loops);
                    }
                    loops.RemoveAt(loops.Count - 1);
                    return true;
                }
                var entryState = initialized.Clone(repeatLoopState);
                var entryMasks = (uint[])fieldMasks.Clone();
                var headState = entryState.Clone(repeatLoopState);
                var headMasks = (uint[])entryMasks.Clone();
                var priorDeclarations = new HashSet<int>(declarations);
                for (var iteration = 0; iteration <= initialized.Length * 9; iteration++)
                {
                    using var iterationSnapshotState
                        = new RepeatSnapshotReservationScope(
                            initialized.RepeatResources);
                    var conditionInput = headState.Clone(
                        iterationSnapshotState);
                    var conditionState = headState.Clone(
                        iterationSnapshotState);
                    var conditionMasks = (uint[])headMasks.Clone();
                    var iterationDeclarations = new HashSet<int>(priorDeclarations);
                    if (!loop.IsPostTested)
                    {
                        Expression(loop.Condition, conditionState, conditionMasks, iterationDeclarations,
                            function, program, ref remaining, depth + 1, failures);
                    }
                    if (loop.Condition.Type != PrimitiveType.Bool || loop.Body is null)
                    {
                        throw Invalid();
                    }
                    var bodyState = loop.IsPostTested
                        ? headState.Clone(iterationSnapshotState)
                        : Outcome(loop.Condition, conditionInput, true,
                            ref remaining, iterationSnapshotState);
                    var bodyMasks = (uint[])conditionMasks.Clone();
                    MarkOutMasks(bodyState, bodyMasks, function, program);
                    var bodyReachable = loop.IsPostTested || CanCompleteAs(
                        loop.Condition, true, conditionInput, program, ref remaining, depth + 1);
                    var exitReachable = CanCompleteAs(
                        loop.Condition, false, conditionInput, program, ref remaining, depth + 1);
                    var iteratorDeclarations = new HashSet<int>(iterationDeclarations);
                    using var loopScope = new LoopScope(cleanups.Count,
                        iterationSnapshotState) {
                        IsCleanupSwitch = inCleanup && loop.SwitchArmVariable >= 0,
                        ScopeActivation = initialized.ScopeActivation,
                    };
                    loops.Add(loopScope);
                    var previousLoopActivation = bodyState.ScopeActivation;
                    bodyState.ScopeActivation = bodyState.EnterLoopActivation(loop.Span, iteration != 0);
                    bool bodyReturns;
                    try
                    {
                        bodyReturns = Statement(verifiedBody, bodyState, bodyMasks,
                            iterationDeclarations, function, program, ref remaining, depth + 1,
                            reachable && bodyReachable, cleanups, inCleanup, inUnsafe, failures, loops);
                    }
                    finally
                    {
                        bodyState.ScopeActivation = previousLoopActivation;
                    }
                    loops.RemoveAt(loops.Count - 1);
                    var exitState = loop.IsPostTested
                        ? conditionState.Clone(iterationSnapshotState)
                        : Outcome(loop.Condition, conditionInput, false,
                            ref remaining, iterationSnapshotState);
                    var exitMasks = (uint[])conditionMasks.Clone();
                    MarkOutMasks(exitState, exitMasks, function, program);
                    using var backEdges = new FailureEdges(
                        iterationSnapshotState);
                    if (bodyReachable && !bodyReturns)
                    {
                        backEdges.Add(bodyState, bodyMasks);
                    }
                    if (loopScope.Continues.HasEdges)
                    {
                        backEdges.Add(loopScope.Continues.State, loopScope.Continues.Masks);
                    }
                    if (verifiedIterator is not null)
                    {
                        var iteratorState = backEdges.HasEdges
                            ? backEdges.State
                            : bodyState.Clone(iterationSnapshotState);
                        var iteratorMasks = backEdges.HasEdges ? backEdges.Masks : (uint[])bodyMasks.Clone();
                        for (var slot = function.ParameterCount + (function.IsConstructor ? 1 : 0);
                            slot < iteratorState.Length; slot++)
                        {
                            if (!iteratorDeclarations.Contains(slot))
                            {
                                iteratorState[slot] = false;
                                iteratorMasks[slot] = 0;
                            }
                        }
                        var iteratorReturns = Statement(verifiedIterator, iteratorState, iteratorMasks, iteratorDeclarations,
                            function, program, ref remaining, depth + 1, reachable && backEdges.HasEdges,
                            cleanups, inCleanup, inUnsafe, failures, loops, cleanupCounterStep: countedLoop?.Step);
                        iterationDeclarations.UnionWith(iteratorDeclarations);
                        backEdges.Reset();
                        if (!iteratorReturns && bodyReachable && (loopScope.Continues.HasEdges || !bodyReturns))
                        {
                            backEdges.Add(iteratorState, iteratorMasks);
                        }
                    }
                    if (loop.IsPostTested)
                    {
                        var conditionInputState = backEdges.HasEdges
                            ? backEdges.State
                            : bodyState.Clone(iterationSnapshotState);
                        var conditionInputMasks = backEdges.HasEdges ? backEdges.Masks : (uint[])bodyMasks.Clone();
                        var conditionDeclarations = new HashSet<int>(iterationDeclarations);
                        for (var slot = function.ParameterCount + (function.IsConstructor ? 1 : 0);
                            slot < conditionInputState.Length; slot++)
                        {
                            if (!priorDeclarations.Contains(slot))
                            {
                                conditionInputState[slot] = false;
                                conditionInputMasks[slot] = 0;
                            }
                        }
                        var evaluatedState = conditionInputState.Clone(
                            iterationSnapshotState);
                        var evaluatedMasks = (uint[])conditionInputMasks.Clone();
                        Expression(loop.Condition, evaluatedState, evaluatedMasks, conditionDeclarations,
                            function, program, ref remaining, depth + 1, failures);
                        var conditionContinuesTrue = CanCompleteAs(loop.Condition, true,
                            conditionInputState, program, ref remaining, depth + 1);
                        var conditionContinuesFalse = CanCompleteAs(loop.Condition, false,
                            conditionInputState, program, ref remaining, depth + 1);
                        var falseState = Outcome(loop.Condition,
                            conditionInputState, false, ref remaining,
                            iterationSnapshotState);
                        var trueState = Outcome(loop.Condition,
                            conditionInputState, true, ref remaining,
                            iterationSnapshotState);
                        var trueMasks = (uint[])evaluatedMasks.Clone();
                        MarkOutMasks(falseState, evaluatedMasks, function, program);
                        MarkOutMasks(trueState, trueMasks, function, program);
                        exitState = falseState;
                        exitMasks = evaluatedMasks;
                        backEdges.Reset();
                        if (bodyReachable && (loopScope.Continues.HasEdges || !bodyReturns)
                            && conditionContinuesTrue)
                        {
                            backEdges.Add(trueState, trueMasks);
                        }
                        exitReachable = conditionContinuesFalse
                            && bodyReachable && (loopScope.Continues.HasEdges || !bodyReturns);
                        iterationDeclarations.UnionWith(conditionDeclarations);
                    }
                    using var exits = new FailureEdges(
                        iterationSnapshotState);
                    if (exitReachable)
                    {
                        exits.Add(exitState, exitMasks);
                    }
                    if (loopScope.Breaks.HasEdges)
                    {
                        exits.Add(loopScope.Breaks.State, loopScope.Breaks.Masks);
                    }
                    var backState = backEdges.HasEdges ? backEdges.State : null;
                    var backMasks = backEdges.HasEdges ? backEdges.Masks : null;
                    var changed = false;
                    for (var i = 0; i < headState.Length; i++)
                    {
                        var localToIteration = i >= function.ParameterCount + (function.IsConstructor ? 1 : 0)
                            && !priorDeclarations.Contains(i);
                        var nextState = backState is null || localToIteration ? entryState.Possibilities(i)
                            : (byte)(entryState.Possibilities(i) | backState.Possibilities(i));
                        var nextMask = entryMasks[i] & (backMasks is null ? uint.MaxValue : backMasks[i]);
                        changed |= nextState != headState.Possibilities(i) || nextMask != headMasks[i];
                        headState.SetPossibilities(i, nextState);
                        headMasks[i] = nextMask;
                        changed |= backState is not null && !localToIteration
                            ? headState.MergeFieldPlaceGenerations(i, entryState, backState)
                            : headState.CopyFieldPlaceGenerations(i, entryState);
                        changed |= backState is not null && !localToIteration
                            ? headState.MergeRawUnionInitializedBytes(i,
                                entryState, backState)
                            : headState.CopyRawUnionInitializedBytes(i,
                                entryState);
                        changed |= backState is not null && !localToIteration
                            ? headState.MergeNestedOwnerStates(i, entryState, backState)
                            : headState.CopyNestedOwnerStates(i, entryState);
                        changed |= backState is not null && !localToIteration
                            ? headState.MergeProvenanceSlot(i, backState)
                            : headState.CopyProvenanceSlot(i, entryState);
                    }
                    var mergedNativeState = entryState.Clone(
                        iterationSnapshotState);
                    if (backState is not null)
                    {
                        _ = mergedNativeState.MergeNativeOutputState(entryState, true, backState, true);
                    }
                    for (var i = function.ParameterCount + (function.IsConstructor ? 1 : 0);
                        i < mergedNativeState.Length; i++)
                    {
                        if (!priorDeclarations.Contains(i))
                        {
                            mergedNativeState.CopyNativeOutputSlot(i, entryState);
                        }
                    }
                    changed |= headState.MergeNativeOutputState(
                        mergedNativeState, false, mergedNativeState, true);
                    if (!changed)
                    {
                        if (exits.HasEdges)
                        {
                            exits.State.CopyTo(initialized);
                            Array.Copy(exits.Masks, fieldMasks, fieldMasks.Length);
                        }
                        declarations.UnionWith(iterationDeclarations);
                        return !exits.HasEdges;
                    }
                }
                throw Limit();
            }
            case BoundBreak or BoundContinue:
            {
                var targetDepth = statement is BoundBreak exit ? exit.TargetDepth
                    : ((BoundContinue)statement).TargetDepth;
                if (targetDepth <= 0 || targetDepth > loops.Count)
                {
                    throw InvalidEffect(statement.Span, inCleanup ? "FinallyReturn" : "LoopTransfer");
                }
                using var repeatSnapshotState
                    = CreateRepeatSnapshotScopeIfNeeded(initialized);
                var target = loops[^targetDepth];
                var transferState = initialized.Clone();
                var transferMasks = (uint[])fieldMasks.Clone();
                var transferDeclarations = new HashSet<int>(declarations);
                var transferDoesNotComplete = VerifyCleanupStack(cleanups, target.CleanupDepth,
                    transferState, transferMasks, transferDeclarations, function, program,
                    ref remaining, depth + 1, reachable, inUnsafe);
                if (reachable && !transferDoesNotComplete)
                {
                    transferState.CheckAndExitStorageScopesTo(target.ScopeActivation);
                    transferState.ScopeActivation = target.ScopeActivation;
                    (statement is BoundBreak ? target.Breaks : target.Continues).Add(transferState, transferMasks);
                }
                return true;
            }
            case BoundTryFinally cleanup:
                return TryFinallyStatement(cleanup, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth, reachable, cleanups, inCleanup, inUnsafe, failures, loops);
            case BoundViewScope view:
            {
                using var repeatSnapshotState
                    = CreateRepeatSnapshotScopeIfNeeded(initialized);
                if (view.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    || view.Variable >= initialized.Length || view.Variable < 0
                    || function.Variables[view.Variable].Type != (view.IsMutable
                        ? PrimitiveType.MutableInt32BufferView : PrimitiveType.ReadonlyInt32BufferView)
                    || function.Variables[view.Variable].IsMutable != view.IsMutable
                    || !declarations.Add(view.Variable) || initialized[view.Variable]
                    || view.Owner is null || view.Body is null)
                {
                    throw InvalidEffect(view.Span, "Ownership");
                }
                if (view.Owner is BoundReceiverLoan named)
                {
                    if (view.IsMutable || named.Type != PrimitiveType.OwnedInt32Buffer
                        || named.Variable < 0 || named.Variable >= initialized.Length
                        || function.Variables[named.Variable].Type != named.Type
                        || !initialized[named.Variable])
                    {
                        throw InvalidEffect(named.Span, "Ownership");
                    }
                }
                else if (view.Owner is BoundMutableReceiverLoan mutable)
                {
                    if (!view.IsMutable || mutable.Type != PrimitiveType.OwnedInt32Buffer
                        || mutable.Variable < 0 || mutable.Variable >= initialized.Length
                        || function.Variables[mutable.Variable].Type != mutable.Type
                        || !function.Variables[mutable.Variable].IsMutable || !initialized[mutable.Variable])
                    {
                        throw InvalidEffect(mutable.Span, "Ownership");
                    }
                }
                else if (view.Owner is BoundFieldLoan fieldLoan)
                {
                    if (view.IsMutable || fieldLoan.Type != PrimitiveType.OwnedInt32Buffer
                        || fieldLoan.Place is null || fieldLoan.Place.Type != fieldLoan.Type)
                    {
                        throw InvalidEffect(fieldLoan.Span, "Ownership");
                    }
                    _ = VerifyUniqueFieldPlace(fieldLoan.Place, initialized, fieldMasks,
                        function, program, ref remaining, depth + 1);
                }
                else if (view.Owner is BoundMutableFieldLoan mutableField)
                {
                    if (!view.IsMutable || mutableField.Type != PrimitiveType.OwnedInt32Buffer
                        || mutableField.Place is null || mutableField.Place.Type != mutableField.Type)
                    {
                        throw InvalidEffect(mutableField.Span, "Ownership");
                    }
                    _ = VerifyMutableFieldPlace(mutableField.Place, initialized, function, program,
                        ref remaining, depth + 1, allowBufferLeaf: true);
                }
                else
                {
                    throw InvalidEffect(view.Owner.Span, "Ownership");
                }
                var rangeBound = view.RangeEnd ?? view.Length;
                var invalidRangeShape = view.RangeEnd is null
                    ? view.IsStartEndRelative || view.IsRangeEndRelative
                    : view.IsMutable || view.Length is not BoundConstant { Type: PrimitiveType.Int32, Value: 0 };
                if (view.Start.Type != PrimitiveType.Int32 || rangeBound.Type != PrimitiveType.Int32
                    || invalidRangeShape)
                {
                    throw Invalid();
                }
                var beforeView = initialized.Clone();
                var beforeViewMasks = (uint[])fieldMasks.Clone();
                if (!EvaluateExpression(view.Start, initialized, fieldMasks, declarations, function,
                    program, ref remaining, depth + 1, failures))
                {
                    ValidateUnreachableExpression(rangeBound, initialized, fieldMasks,
                        declarations, function, program, ref remaining, depth + 1);
                    var unreachableBodyState = beforeView.Clone();
                    unreachableBodyState[view.Variable] = true;
                    _ = Statement(view.Body, unreachableBodyState, (uint[])beforeViewMasks.Clone(),
                        declarations, function, program, ref remaining, depth + 1,
                        false, cleanups, inCleanup, inUnsafe, failures, loops);
                    return true;
                }
                if (!EvaluateExpression(rangeBound, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures))
                {
                    beforeView.CopyTo(initialized);
                    beforeViewMasks.CopyTo(fieldMasks, 0);
                    var unreachableBodyState = beforeView.Clone();
                    unreachableBodyState[view.Variable] = true;
                    _ = Statement(view.Body, unreachableBodyState, (uint[])beforeViewMasks.Clone(),
                        declarations, function, program, ref remaining, depth + 1,
                        false, cleanups, inCleanup, inUnsafe, failures, loops);
                    return true;
                }
                if (RequiresNoFailureProof(function))
                {
                    throw InvalidNoFailureEffect(function, view.Span);
                }
                failures?.Add(initialized, fieldMasks);
                initialized[view.Variable] = true;
                var viewReturns = Statement(view.Body, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, reachable, cleanups, inCleanup, inUnsafe, failures, loops);
                initialized[view.Variable] = false;
                return viewReturns;
            }
            case BoundUnsafe unsafeBlock:
                if (unsafeBlock.Body is null)
                {
                    throw Invalid(unsafeBlock.Span);
                }
                return Statement(unsafeBlock.Body, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, reachable, cleanups, inCleanup, inUnsafe: true, failures: failures, loops: loops);
            case BoundTryCatch recovery:
                return TryCatchStatement(recovery, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth, reachable, cleanups, inCleanup, inUnsafe, failures, loops);
            case BoundRequire requirement:
            {
                using var repeatSnapshotState
                    = CreateRepeatSnapshotScopeIfNeeded(initialized);
                if (!CanCompleteNormally(requirement.Condition, initialized, program,
                    ref remaining, depth + 1))
                {
                    var beforeNonCompletingCondition = initialized.Clone();
                    var beforeMasks = (uint[])fieldMasks.Clone();
                    Expression(requirement.Condition, initialized, fieldMasks, declarations, function, program,
                        ref remaining, depth + 1, failures);
                    beforeNonCompletingCondition.CopyTo(initialized);
                    beforeMasks.CopyTo(fieldMasks, 0);
                    if (requirement.Condition.Type != PrimitiveType.Bool)
                    {
                        throw Invalid();
                    }
                    return true;
                }
                var beforeRequirement = initialized.Clone();
                Expression(requirement.Condition, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                if (requirement.Condition.Type != PrimitiveType.Bool)
                {
                    throw Invalid();
                }
                if (RequiresNoFailureProof(function)
                    && CanBe(requirement.Condition, false, ref remaining, initialized))
                {
                    throw InvalidNoFailureEffect(function, requirement.Span);
                }
                if (CanBe(requirement.Condition, false, ref remaining, initialized))
                {
                    var rejected = Outcome(requirement.Condition,
                        beforeRequirement, false, ref remaining,
                        repeatSnapshotState);
                    var rejectedMasks = (uint[])fieldMasks.Clone();
                    MarkOutMasks(rejected, rejectedMasks, function, program);
                    failures?.Add(rejected, rejectedMasks);
                }
                var accepted = Outcome(requirement.Condition,
                    beforeRequirement, true, ref remaining,
                    repeatSnapshotState);
                accepted.CopyTo(initialized);
                MarkOutMasks(initialized, fieldMasks, function, program);
                return !CanBe(requirement.Condition, true, ref remaining, initialized);
            }
            case BoundReturn result:
            {
                using var repeatSnapshotState
                    = CreateRepeatSnapshotScopeIfNeeded(initialized);
                if (inCleanup)
                {
                    throw InvalidEffect(result.Span, "FinallyReturn");
                }
                if (result.Value is not null
                    && !CanCompleteNormally(result.Value, initialized, program,
                        ref remaining, depth + 1))
                {
                    if (result.Value.Type != function.ReturnType)
                    {
                        throw Invalid();
                    }
                    var beforeValue = initialized.Clone();
                    var beforeMasks = (uint[])fieldMasks.Clone();
                    Expression(result.Value, initialized, fieldMasks, declarations, function, program,
                        ref remaining, depth + 1, failures);
                    beforeValue.CopyTo(initialized);
                    beforeMasks.CopyTo(fieldMasks, 0);
                    return true;
                }
                var copyOriginSummaryBuilder =
                    (initialized.Dependencies as BackingDependencies)?.CopyOriginSummaryBuilder;
                if (function.IsClassConstructor && (!initialized[0]
                    || fieldMasks[0] != FullMask(program, function.Variables[0].Type)))
                {
                    throw InvalidEffect(result.Span, "Ownership");
                }
                var beforeReturn = initialized.Clone();
                if (result.Value is null)
                {
                    if (function.ReturnType != PrimitiveType.Void)
                    {
                        throw Invalid();
                    }
                }
                else
                {
                    if (function.ConstructionHomeIndex >= 0
                        && (result.Value is not BoundRead constructionRead
                            || constructionRead.Variable
                                != function.ConstructionHomeIndex))
                    {
                        throw Invalid(result.Span);
                    }
                    if (function.IsConstructor && result.Value is not BoundRead { Variable: 0 })
                    {
                        throw Invalid(result.Span);
                    }
                    if (result.Value is BoundRead homeResultRead
                        && IsConstructionHome(function,
                            homeResultRead.Variable))
                    {
                        if (!initialized[homeResultRead.Variable]
                            || homeResultRead.Type != function.ReturnType
                            || fieldMasks[homeResultRead.Variable]
                                != FullMask(program, function.ReturnType))
                        {
                            throw InvalidEffect(result.Span, "Ownership");
                        }

                        if (function.IsConstructor
                            && IsUnique(program, function.ReturnType))
                        {
                            if (homeResultRead.Variable != 0)
                            {
                                throw Invalid(result.Span);
                            }
                        }
                        else
                        {
                            Expression(result.Value, initialized, fieldMasks,
                                declarations, function, program,
                                ref remaining, depth + 1, failures);
                        }
                    }
                    else
                    {
                        Expression(result.Value, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                    }
                    if (result.Value.Type != function.ReturnType)
                    {
                        throw Invalid();
                    }
                }
                var rawUnionReturnBytes
                    = result.Value is BoundExpression rawUnionReturnValue
                        && CarriesRawUnionPayloadFacts(
                            program, function.ReturnType)
                            ? InitializedPayloadBytesOfExpression(
                                rawUnionReturnValue, function.ReturnType,
                                initialized, function, program)
                            : null;
                Dictionary<BoundCopyOriginFieldPath, HashSet<BoundCopyOrigin>>? returnCopyOriginFacts = null;
                if (copyOriginSummaryBuilder?.Kind == BoundCopyOriginSummaryKind.NormalResult
                    && result.Value is BoundExpression copyOriginResult
                    && initialized.Dependencies is BackingDependencies returnCopyOriginDependencies)
                {
                    returnCopyOriginFacts = CopyOriginFactsForExpression(copyOriginResult,
                        function.ReturnType, returnCopyOriginDependencies, function, program,
                        ref remaining, depth + 1);
                }
                if (result.Value is BoundExpression pointerResult
                    && CarriesNativePointer(pointerResult.Type, program)
                    && initialized.Dependencies is BackingDependencies pointerResultDependencies
                    && NativePointerBackingOf(pointerResult, pointerResultDependencies,
                        program, ref remaining, depth + 1).Count != 0)
                {
                    throw InvalidEffect(pointerResult.Span, "NativePointer");
                }
                CheckNativePointerReturn(function, initialized, result.Span);
                CheckReferenceReturn(result.Value, function, initialized,
                    fieldMasks, program, result.Span, ref remaining);
                IReadOnlySet<ProvenanceAnchor>? forwardedBacking = null;
                if (result.Value is { Type: PrimitiveType.TokenSpan } spanResult)
                {
                    forwardedBacking = BackingOf(spanResult, initialized.Dependencies, program,
                        ref remaining, depth + 1);
                    if (forwardedBacking.Count == 0)
                    {
                        throw InvalidEffect(result.Span, "Ownership");
                    }
                }
                var parameterOffset = function.IsConstructor ? 1 : 0;
                var hasOutParameter = false;
                for (var i = parameterOffset; i < function.ParameterCount + parameterOffset; i++)
                {
                    hasOutParameter |= function.Variables[i].IsOut;
                }
                var canReturnNormally = true;
                InitializationState? successfulReturnState = null;
                if (cleanups.Count == 0 && !hasOutParameter)
                {
                    successfulReturnState = initialized;
                }
                if (function.ReturnType == PrimitiveType.Bool && result.Value is BoundExpression boolResult)
                {
                    if (cleanups.Count != 0 || hasOutParameter)
                    {
                        canReturnNormally = false;
                        var outcomeCount = cleanups.Count != 0 || function.DoesNotReturn ? 2 : 1;
                        for (var outcome = 0; outcome < outcomeCount; outcome++)
                        {
                            var predicate = outcome == 0;
                            if (!CanBe(boolResult, predicate, ref remaining, initialized))
                            {
                                continue;
                            }
                            var returnState = Outcome(boolResult, beforeReturn,
                                predicate, ref remaining,
                                repeatSnapshotState);
                            var returnMasks = (uint[])fieldMasks.Clone();
                            MarkOutMasks(returnState, returnMasks, function, program);
                            var returnDeclarations = new HashSet<int>(declarations);
                            var cleanupDoesNotComplete = VerifyCleanupStack(cleanups, 0, returnState,
                                returnMasks, returnDeclarations, function, program, ref remaining,
                                depth + 1, reachable, inUnsafe, successfulReturn: predicate);
                            if (!cleanupDoesNotComplete)
                            {
                                canReturnNormally = true;
                                successfulReturnState = returnState;
                                if (predicate)
                                {
                                    for (var i = parameterOffset; i < function.ParameterCount + parameterOffset; i++)
                                    {
                                        if (function.Variables[i].IsOut && !returnState[i])
                                        {
                                            throw Invalid(result.Span);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (cleanups.Count != 0 || hasOutParameter)
                    {
                        canReturnNormally = false;
                        var returnState = initialized.Clone();
                        var returnMasks = (uint[])fieldMasks.Clone();
                        var returnDeclarations = new HashSet<int>(declarations);
                        var dependencies = initialized.Dependencies as BackingDependencies;
                        var stagedCount = dependencies?.Staged.Count ?? 0;
                        if (forwardedBacking is not null)
                        {
                            if (dependencies is null)
                            {
                                throw InvalidEffect(result.Span, "Ownership");
                            }
                            dependencies.Staged.Add(new StagedDependencySource(
                                null, -1, [.. forwardedBacking], EmptyBackingSources,
                                EmptyBackingSources, EmptyReferenceFieldValues,
                                EmptyBackingSources, false,
                                new NativePointerExtentFact([], MayBeOpaque: false,
                                    IsUnavailable: false)));
                        }
                        try
                        {
                            var cleanupDoesNotComplete = VerifyCleanupStack(cleanups, 0,
                                returnState, returnMasks, returnDeclarations, function, program,
                                ref remaining, depth + 1, reachable, inUnsafe, successfulReturn: true);
                            if (!cleanupDoesNotComplete)
                            {
                                canReturnNormally = true;
                                successfulReturnState = returnState;
                                for (var i = parameterOffset; i < function.ParameterCount + parameterOffset; i++)
                                {
                                    if (function.Variables[i].IsOut && !returnState[i])
                                    {
                                        throw Invalid(result.Span);
                                    }
                                }
                            }
                        }
                        finally
                        {
                            dependencies?.Staged.RemoveRange(stagedCount, dependencies.Staged.Count - stagedCount);
                        }
                    }
                }
                if (function.DoesNotReturn && reachable && canReturnNormally)
                {
                    throw InvalidEffect(result.Span, "DoesNotReturn");
                }
                if (reachable && canReturnNormally && copyOriginSummaryBuilder is not null)
                {
                    Dictionary<BoundCopyOriginFieldPath, HashSet<BoundCopyOrigin>> returnFacts;
                    if (copyOriginSummaryBuilder.Kind == BoundCopyOriginSummaryKind.NormalResult)
                    {
                        returnFacts = returnCopyOriginFacts
                            ?? new Dictionary<BoundCopyOriginFieldPath, HashSet<BoundCopyOrigin>>();
                    }
                    else if (copyOriginSummaryBuilder.Kind
                            == BoundCopyOriginSummaryKind.FreshInlineConstruction
                        && function.IsConstructor
                        && successfulReturnState?.Dependencies is BackingDependencies constructionDependencies)
                    {
                        returnFacts = CopyOriginFactsForFreshReceiver(function.ReturnType,
                            constructionDependencies, program, ref remaining, depth + 1);
                    }
                    else
                    {
                        returnFacts = new Dictionary<BoundCopyOriginFieldPath, HashSet<BoundCopyOrigin>>();
                    }
                    copyOriginSummaryBuilder.Observe(result, returnFacts);
                }
                if (reachable && canReturnNormally
                    && rawUnionReturnBytes is not null)
                {
                    initialized.RawUnionReturnCoverage?.Observe(
                        rawUnionReturnBytes);
                }
                return true;
            }
            default:
                throw Invalid();
        }
    }

    private static bool TryFinallyStatement(BoundTryFinally cleanup, InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function, BoundProgram program, ref int remaining, int depth,
        bool reachable, List<CleanupScope> cleanups, bool inCleanup, bool inUnsafe, FailureEdges? failures, List<LoopScope> loops)
    {
        if (cleanup.Body is null || cleanup.Finally is null)
        {
            throw Invalid(cleanup.Span);
        }
        using var repeatSnapshotState
            = CreateRepeatSnapshotScopeIfNeeded(initialized);
        using var cleanupFailures = program.RuntimePolicy == BoundRuntimePolicy.CheckedExceptions
            ? new FailureEdges() : null;
        var cleanupScope = new CleanupScope(cleanup.Finally, [.. declarations], failures);
        cleanups.Add(cleanupScope);
        var tryReturns = Statement(cleanup.Body, initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, reachable, cleanups, inCleanup, inUnsafe, cleanupFailures, loops);
        cleanups.RemoveAt(cleanups.Count - 1);
        if (cleanupFailures?.HasEdges == true)
        {
            var exceptionalState = cleanupFailures.State;
            var exceptionalMasks = cleanupFailures.Masks;
            var finalizerDoesNotComplete = VerifyCleanup(cleanup.Finally, exceptionalState,
                exceptionalMasks, [.. declarations], function, program, ref remaining,
                depth + 1, reachable, inUnsafe, cleanupScope.Visible, failures: failures);
            if (!finalizerDoesNotComplete)
            {
                failures?.Add(exceptionalState, exceptionalMasks);
            }
        }
        if (tryReturns)
        {
            var unreachableState = initialized.Clone();
            var unreachableMasks = (uint[])fieldMasks.Clone();
            for (var index = 0; index < unreachableMasks.Length; index++)
            {
                unreachableState[index] = index < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    || declarations.Contains(index);
                if (unreachableState[index] && HasFields(program, function.Variables[index].Type))
                {
                    unreachableMasks[index] = FullMask(program, function.Variables[index].Type);
                }
            }
            _ = VerifyCleanup(cleanup.Finally, unreachableState, unreachableMasks, declarations, function,
                program, ref remaining, depth + 1, reachable: false, inUnsafe, cleanupScope.Visible);
        }
        else
        {
            var finalizerDoesNotComplete = VerifyCleanup(cleanup.Finally, initialized, fieldMasks,
                declarations, function, program, ref remaining, depth + 1, reachable, inUnsafe,
                cleanupScope.Visible, failures: failures);
            return tryReturns || (reachable && finalizerDoesNotComplete);
        }
        return true;
    }

    private static bool TryCatchStatement(BoundTryCatch recovery, InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function, BoundProgram program, ref int remaining, int depth,
        bool reachable, List<CleanupScope> cleanups, bool inCleanup, bool inUnsafe, FailureEdges? failures, List<LoopScope> loops)
    {
        if (!inUnsafe)
        {
            throw InvalidEffect(recovery.Span, "UnsafeRecovery");
        }
        if (program.RuntimePolicy != BoundRuntimePolicy.CheckedExceptions)
        {
            throw InvalidEffect(recovery.Span, "CheckedPolicy");
        }
        if (inCleanup || recovery.Body is null || recovery.Catch is null)
        {
            throw Invalid(recovery.Span);
        }
        using var repeatSnapshotState
            = CreateRepeatSnapshotScopeIfNeeded(initialized);
        var recoveryEntryState = initialized.Clone();
        var recoveryEntryMasks = (uint[])fieldMasks.Clone();
        var recoveryDeclarations = new HashSet<int>(declarations);
        using var caughtFailures = new FailureEdges();
        var recoveryScope = recovery.Finally is null ? null
            : new CleanupScope(recovery.Finally, [.. declarations], failures);
        if (recovery.Finally is not null)
        {
            cleanups.Add(recoveryScope!);
        }
        var recoveryBodyReturns = Statement(recovery.Body, initialized, fieldMasks, declarations, function,
            program, ref remaining, depth + 1, reachable, cleanups, inCleanup, inUnsafe, caughtFailures, loops);
        var catchState = caughtFailures.HasEdges ? caughtFailures.State : recoveryEntryState;
        var catchMasks = caughtFailures.HasEdges ? caughtFailures.Masks : recoveryEntryMasks;
        for (var index = 0; index < catchState.Length; index++)
        {
            if (index >= function.ParameterCount + (function.IsConstructor ? 1 : 0)
                && !recoveryDeclarations.Contains(index))
            {
                catchState[index] = false;
                catchMasks[index] = 0;
            }
        }
        using var catchFailures = failures is not null || recovery.Finally is not null
            ? new FailureEdges() : null;
        var catchReturns = Statement(recovery.Catch, catchState, catchMasks, declarations, function,
            program, ref remaining, depth + 1, reachable && caughtFailures.HasEdges, cleanups,
            inCleanup, inUnsafe, catchFailures, loops);
        if (recovery.Finally is not null)
        {
            cleanups.RemoveAt(cleanups.Count - 1);
            if (catchFailures?.HasEdges == true)
            {
                var exceptionalState = catchFailures.State;
                var exceptionalMasks = catchFailures.Masks;
                var finalizerDoesNotComplete = VerifyCleanup(recovery.Finally, exceptionalState,
                    exceptionalMasks, [.. declarations], function, program, ref remaining,
                    depth + 1, reachable, inUnsafe, recoveryScope!.Visible, failures: failures);
                if (!finalizerDoesNotComplete)
                {
                    failures?.Add(exceptionalState, exceptionalMasks);
                }
            }
        }
        else if (catchFailures?.HasEdges == true)
        {
            failures!.Add(catchFailures.State, catchFailures.Masks);
        }
        for (var i = 0; i < initialized.Length; i++)
        {
            initialized.SetPossibilities(i, recoveryBodyReturns && caughtFailures.HasEdges ? catchState.Possibilities(i)
                : !caughtFailures.HasEdges || catchReturns ? initialized.Possibilities(i)
                : (byte)(initialized.Possibilities(i) | catchState.Possibilities(i)));
            fieldMasks[i] = recoveryBodyReturns && caughtFailures.HasEdges ? catchMasks[i]
                : !caughtFailures.HasEdges || catchReturns ? fieldMasks[i]
                : fieldMasks[i] & catchMasks[i];
            initialized.SetPossibleFieldMask(i, recoveryBodyReturns && caughtFailures.HasEdges
                ? catchState.PossibleFieldMask(i)
                : !caughtFailures.HasEdges || catchReturns ? initialized.PossibleFieldMask(i)
                : initialized.PossibleFieldMask(i) | catchState.PossibleFieldMask(i));
            if (recoveryBodyReturns && caughtFailures.HasEdges)
            {
                _ = initialized.CopyRawUnionInitializedBytes(i, catchState);
                _ = initialized.CopyFieldPlaceGenerations(i, catchState);
                _ = initialized.CopyNestedOwnerStates(i, catchState);
            }
            else if (caughtFailures.HasEdges && !catchReturns)
            {
                _ = initialized.MergeRawUnionInitializedBytes(i,
                    initialized, catchState);
                _ = initialized.MergeFieldPlaceGenerations(i, initialized, catchState);
                _ = initialized.MergeNestedOwnerStates(i, initialized, catchState);
            }
        }
        _ = initialized.MergeNativeOutputState(initialized, !recoveryBodyReturns,
            catchState, caughtFailures.HasEdges && !catchReturns);
        var recoveryReturns = recoveryBodyReturns && (!caughtFailures.HasEdges || catchReturns);
        if (recovery.Finally is not null)
        {
            var finalizerDoesNotComplete = VerifyCleanup(recovery.Finally, initialized, fieldMasks,
                declarations, function, program, ref remaining, depth + 1,
                reachable && !recoveryReturns, inUnsafe, recoveryScope!.Visible, failures: failures);
            recoveryReturns |= reachable && !recoveryReturns && finalizerDoesNotComplete;
        }
        return recoveryReturns;
    }

    private static bool VerifyCleanup(BoundBlock cleanup, InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function, BoundProgram program, ref int remaining, int depth, bool reachable,
        bool inUnsafe, HashSet<int>? visible = null, bool? successfulReturn = null,
        FailureEdges? failures = null)
    {
        if (visible is not null)
        {
            for (var index = 0; index < initialized.Length; index++)
            {
                if (index >= function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    && !visible.Contains(index))
                {
                    initialized[index] = false;
                    fieldMasks[index] = 0;
                    initialized.SetPossibleFieldMask(index, 0);
                }
            }
        }
        var outputTransfer = successfulReturn == true
            && cleanup.Statements is [BoundDrop { IsOutput: true }];
        using var repeatSnapshotState = outputTransfer
            ? CreateRepeatSnapshotScopeIfNeeded(initialized) : null;
        var checkState = outputTransfer ? initialized.Clone() : initialized;
        var checkMasks = outputTransfer ? (uint[])fieldMasks.Clone() : fieldMasks;
        return Statement(cleanup, checkState, checkMasks, declarations, function,
            program, ref remaining, depth, reachable, inCleanup: true, inUnsafe: inUnsafe,
            failures: failures);
    }

    private static bool VerifyCleanupStack(List<CleanupScope> cleanups, int firstScope,
        InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth,
        bool reachable, bool inUnsafe, bool? successfulReturn = null)
    {
        if (firstScope < 0 || firstScope > cleanups.Count)
        {
            throw Invalid();
        }
        for (var index = cleanups.Count - 1; index >= firstScope; index--)
        {
            var scope = cleanups[index];
            if (VerifyCleanup(scope.Body, initialized, fieldMasks, declarations, function,
                program, ref remaining, depth, reachable, inUnsafe, scope.Visible,
                successfulReturn, scope.OuterFailures))
            {
                return true;
            }
        }
        return false;
    }

    private static bool CleanupMayFail(PrimitiveType type, BoundProgram program)
    {
        var pending = new Stack<PrimitiveType>();
        var visited = new HashSet<PrimitiveType>();
        pending.Push(type);
        while (pending.TryPop(out var current))
        {
            if (current == PrimitiveType.OwnedInt32Buffer || !IsUnique(program, current)
                || !visited.Add(current))
            {
                continue;
            }
            var structureIndex = (int)current - 100;
            if (structureIndex < 0 || structureIndex >= program.Structures.Length)
            {
                throw Invalid();
            }
            var structure = program.Structures[structureIndex];
            if (structure.CleanupMethod is not null)
            {
                BoundFunction? cleanupFunction = null;
                foreach (var function in program.Functions)
                {
                    if (function.Name != structure.CleanupMethod)
                    {
                        continue;
                    }
                    if (cleanupFunction is not null)
                    {
                        return true;
                    }
                    cleanupFunction = function;
                }
                if (cleanupFunction is null
                    || !CleanupBodyCanBeProvenNonThrowing(
                        cleanupFunction, current, program))
                {
                    return true;
                }
            }
            foreach (var field in structure.Fields)
            {
                if (IsUnique(program, field.Type))
                {
                    pending.Push(field.Type);
                }
            }
        }
        return false;
    }

    private static bool CleanupBodyCanBeProvenNonThrowing(
        BoundFunction cleanup, PrimitiveType ownerType, BoundProgram program)
    {
        if (cleanup.IsExternal || cleanup.Intrinsic != BoundIntrinsic.None
            || cleanup.IndexedBody is not null || !cleanup.IsCleanup
            || cleanup.IsConstructor || cleanup.IsClassConstructor || cleanup.IsStatic
            || cleanup.DeclaringStruct is null || cleanup.DirectNativeImport is not null
            || cleanup.NativeVTableMethod is not null
            || cleanup.NativeFunctionPointerType is not null
            || cleanup.DoesNotReturn || cleanup.RequiresUnsafeCaller
            || cleanup.ReturnType != PrimitiveType.Void || cleanup.ParameterCount != 1
            || cleanup.Variables.Length == 0
            || cleanup.Variables[0].Type != ownerType
            || cleanup.Variables[0].IsMutable || cleanup.Variables[0].IsOut
            || cleanup.Variables[0].IsInit)
        {
            return false;
        }

        var remaining = MaximumAnalysisVisits;
        return CleanupStatementCanBeProvenNonThrowing(
            cleanup.Body, cleanup, program, ref remaining, 0);
    }

    private static bool CleanupStatementCanBeProvenNonThrowing(
        BoundStatement statement, BoundFunction function, BoundProgram program,
        ref int remaining, int depth)
    {
        if (statement is null || depth > MaximumDepth || --remaining < 0)
        {
            return false;
        }

        switch (statement)
        {
            case BoundBlock block:
                foreach (var child in block.Statements)
                {
                    if (!CleanupStatementCanBeProvenNonThrowing(
                        child, function, program, ref remaining, depth + 1))
                    {
                        return false;
                    }
                }
                return true;
            case BoundReturn result:
                return result.Value is null
                    && function.ReturnType == PrimitiveType.Void;
            case BoundIf conditional:
                return CleanupExpressionCanBeProvenNonThrowing(
                        conditional.Condition, function, program, ref remaining, depth + 1)
                    && CleanupStatementCanBeProvenNonThrowing(
                        conditional.Then, function, program, ref remaining, depth + 1)
                    && (conditional.Else is null
                        || CleanupStatementCanBeProvenNonThrowing(
                            conditional.Else, function, program, ref remaining, depth + 1));
            case BoundTryFinally cleanup:
                return CleanupStatementCanBeProvenNonThrowing(
                        cleanup.Body, function, program, ref remaining, depth + 1)
                    && CleanupStatementCanBeProvenNonThrowing(
                        cleanup.Finally, function, program, ref remaining, depth + 1);
            case BoundTryCatch recovery:
                return CleanupStatementCanBeProvenNonThrowing(
                        recovery.Body, function, program, ref remaining, depth + 1)
                    && CleanupStatementCanBeProvenNonThrowing(
                        recovery.Catch, function, program, ref remaining, depth + 1)
                    && (recovery.Finally is null
                        || CleanupStatementCanBeProvenNonThrowing(
                            recovery.Finally, function, program, ref remaining, depth + 1));
            case BoundLocal local:
                return (uint)local.Variable < (uint)function.Variables.Length
                    && !IsUnique(program, function.Variables[local.Variable].Type)
                    && CleanupExpressionCanBeProvenNonThrowing(
                        local.Initializer, function, program, ref remaining, depth + 1);
            case BoundAssignment assignment:
                return (uint)assignment.Variable < (uint)function.Variables.Length
                    && !IsUnique(program, function.Variables[assignment.Variable].Type)
                    && CleanupExpressionCanBeProvenNonThrowing(
                        assignment.Value, function, program, ref remaining, depth + 1);
            case BoundFieldAssignment field:
                return field.NestedFields.IsEmpty
                    && (uint)field.Variable < (uint)function.Variables.Length
                    && HasFields(program, function.Variables[field.Variable].Type)
                    && !ManagedClassTypes.IsClass(function.Variables[field.Variable].Type)
                    && (uint)field.Field < (uint)Fields(program,
                        function.Variables[field.Variable].Type).Length
                    && !IsUnique(program, Fields(program,
                        function.Variables[field.Variable].Type)[field.Field].Type)
                    && CleanupExpressionCanBeProvenNonThrowing(
                        field.Value, function, program, ref remaining, depth + 1);
            case BoundExpressionStatement expression:
                return CleanupExpressionCanBeProvenNonThrowing(
                    expression.Expression, function, program, ref remaining, depth + 1);
            case BoundBufferAssignment write:
                return !write.IsEndRelativeOffset
                    && IsProvenBufferIndex(write.Buffer, write.Index, program)
                    && CleanupExpressionCanBeProvenNonThrowing(
                        write.Value, function, program, ref remaining, depth + 1);
            case BoundRequire { Condition: BoundConstant constant }:
                return constant.Type == PrimitiveType.Bool
                    && constant.TryGetLegacyValue(out var value) && value != 0;
            default:
                return false;
        }
    }

    private static bool CleanupExpressionCanBeProvenNonThrowing(
        BoundExpression expression, BoundFunction function, BoundProgram program,
        ref int remaining, int depth)
    {
        if (expression is null || depth > MaximumDepth || --remaining < 0)
        {
            return false;
        }

        return expression switch {
            BoundConstant or BoundNull => true,
            BoundRead read => (uint)read.Variable < (uint)function.Variables.Length
                && function.Variables[read.Variable].Type == read.Type,
            BoundFieldRead field => HasFields(program, field.Receiver.Type)
                && !ManagedClassTypes.IsClass(field.Receiver.Type)
                && (uint)field.Field < (uint)Fields(program, field.Receiver.Type).Length
                && Fields(program, field.Receiver.Type)[field.Field].Type == field.Type
                && CleanupExpressionCanBeProvenNonThrowing(
                    field.Receiver, function, program, ref remaining, depth + 1),
            BoundBufferRead read => !read.IsEndRelativeOffset
                && IsProvenBufferIndex(read.Buffer, read.Index, program)
                && CleanupExpressionCanBeProvenNonThrowing(
                    read.Index, function, program, ref remaining, depth + 1),
            BoundBufferLength length => (uint)length.Buffer < (uint)program.Buffers.Length,
            BoundUnary unary when unary.Operator == TokenKind.Bang
                && unary.Type == PrimitiveType.Bool && unary.Operand.Type == PrimitiveType.Bool =>
                    CleanupExpressionCanBeProvenNonThrowing(
                        unary.Operand, function, program, ref remaining, depth + 1),
            BoundBinary binary when binary.Operator is TokenKind.EqualsEquals
                or TokenKind.BangEquals or TokenKind.Less or TokenKind.LessEquals
                or TokenKind.Greater or TokenKind.GreaterEquals or TokenKind.Ampersand
                or TokenKind.Pipe or TokenKind.Caret or TokenKind.AmpersandAmpersand
                or TokenKind.PipePipe =>
                    CleanupExpressionCanBeProvenNonThrowing(
                            binary.Left, function, program, ref remaining, depth + 1)
                        && CleanupExpressionCanBeProvenNonThrowing(
                            binary.Right, function, program, ref remaining, depth + 1),
            BoundConditional conditional =>
                CleanupExpressionCanBeProvenNonThrowing(
                        conditional.Condition, function, program, ref remaining, depth + 1)
                    && CleanupExpressionCanBeProvenNonThrowing(
                        conditional.WhenTrue, function, program, ref remaining, depth + 1)
                    && CleanupExpressionCanBeProvenNonThrowing(
                        conditional.WhenFalse, function, program, ref remaining, depth + 1),
            BoundIsNull test => CleanupExpressionCanBeProvenNonThrowing(
                test.Value, function, program, ref remaining, depth + 1),
            _ => false,
        };
    }

    private static void AddCleanupFailure(FailureEdges? failures, InitializationState initialized,
        uint[] fieldMasks, int variable)
    {
        if (failures is null)
        {
            return;
        }
        using var repeatSnapshotState
            = CreateRepeatSnapshotScopeIfNeeded(initialized);
        var failureState = initialized.Clone();
        failureState.MarkConditional(variable);
        var failureMasks = (uint[])fieldMasks.Clone();
        failureMasks[variable] = 0;
        failures.Add(failureState, failureMasks);
    }

    private sealed record CleanupScope(BoundBlock Body, HashSet<int> Visible, FailureEdges? OuterFailures);

    private readonly record struct ActiveStorageScope(int Activation, BoundBlock Block);

    private readonly record struct FieldGenerationKey(int Variable, StorageProjection Projection);

    private readonly record struct FieldGenerationEvent(
        int Variable, StorageProjection Projection, int Activation, int Start, int Length);

    private readonly record struct NestedOwnerStateKey(
        int Variable, FieldProjectionPath OwnerPath);

    private readonly record struct NestedOwnerFieldState(
        PrimitiveType Type, uint FullMask, uint DefiniteMask, uint PossibleMask)
    {
        internal bool IsComplete => DefiniteMask == FullMask && PossibleMask == FullMask;
    }

    private sealed class FieldGenerationContext
    {
        private readonly Dictionary<FieldGenerationEvent, long> _generations = [];
        private long _nextGeneration = 1;

        internal long Get(int variable, StorageProjection projection,
            int activation, TextSpan span)
        {
            if (variable < 0 || activation < 0 || span.Start < 0 || span.Length < 0)
            {
                throw Invalid();
            }
            var key = new FieldGenerationEvent(variable, projection,
                activation, span.Start, span.Length);
            if (!_generations.TryGetValue(key, out var generation))
            {
                if (_nextGeneration == StorageKey.UnknownGeneration)
                {
                    throw Invalid(span);
                }
                generation = _nextGeneration++;
                _generations.Add(key, generation);
            }
            return generation;
        }
    }

    private sealed class LoopScope : IDisposable
    {
        internal int CleanupDepth { get; }
        internal bool IsCleanupSwitch { get; init; }
        internal int ScopeActivation { get; init; }
        internal FailureEdges Breaks { get; }
        internal FailureEdges Continues { get; }

        internal LoopScope(int cleanupDepth,
            RepeatSnapshotReservationScope? snapshotScope = null)
        {
            CleanupDepth = cleanupDepth;
            Breaks = new(snapshotScope);
            Continues = new(snapshotScope);
        }

        public void Dispose()
        {
            Breaks.Dispose();
            Continues.Dispose();
        }
    }

    private sealed class RepeatSnapshotReservationScope(
        RepeatResourceLedger resources) : IDisposable
    {
        private readonly List<IDisposable> _leases = [];
        private readonly Dictionary<InitializationState,
            RepeatResourceLedger.StateLease> _states
                = new(ReferenceEqualityComparer.Instance);

        internal void Adopt(IDisposable? lease)
        {
            if (lease is null)
            {
                return;
            }
            if (lease is RepeatResourceLedger.StateLease stateLease
                && !stateLease.IsOwnedBy(resources))
            {
                throw Invalid();
            }
            _leases.Add(lease);
        }

        internal void TrackState(InitializationState state,
            RepeatResourceLedger.StateLease lease,
            bool adoptDependencies = true)
        {
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(lease);
            if (!lease.IsOwnedBy(resources)
                || lease.ReservedBytes
                    != state.GetRawUnionArrayStateBytes()
                || !_states.TryAdd(state, lease))
            {
                throw Invalid();
            }

            state.SetRepeatSnapshotScope(this);
            if (adoptDependencies)
            {
                Adopt(state.Dependencies);
            }
        }

        internal void TrackExistingState(InitializationState state,
            BigInteger bytes)
        {
            ArgumentNullException.ThrowIfNull(state);
            TrackState(state, resources.ReserveState(bytes),
                adoptDependencies: false);
        }

        internal bool TracksState(InitializationState state)
            => _states.ContainsKey(state);

        internal void UpdateState(InitializationState state,
            BigInteger bytes)
        {
            if (!_states.TryGetValue(state, out var lease))
            {
                return;
            }

            if (bytes > lease.ReservedBytes)
            {
                lease.Add(bytes - lease.ReservedBytes);
            }
            else if (bytes < lease.ReservedBytes)
            {
                lease.Remove(lease.ReservedBytes - bytes);
            }
        }

        public void Dispose()
        {
            foreach (var (state, lease) in _states)
            {
                state.SetRepeatSnapshotScope(null);
                lease.Dispose();
            }
            _states.Clear();
            for (var index = _leases.Count - 1; index >= 0; index--)
            {
                _leases[index].Dispose();
            }
            _leases.Clear();
        }
    }

    private sealed class InitializationState
    {
        private static readonly NativeOutputDestinationState UnknownNativeOutputDestination = new(null);
        private readonly record struct RawUnionArrayStateKey(
            int Variable, StorageProjection ArrayPrefix);

        private sealed class RawUnionArrayStateKeyComparer
            : IComparer<RawUnionArrayStateKey>
        {
            internal static RawUnionArrayStateKeyComparer Instance { get; }
                = new();

            public int Compare(
                RawUnionArrayStateKey left, RawUnionArrayStateKey right)
            {
                var variable = left.Variable.CompareTo(right.Variable);
                return variable != 0 ? variable
                    : StorageProjectionComparer.Instance.Compare(
                        left.ArrayPrefix, right.ArrayPrefix);
            }
        }

        internal sealed class RawUnionArrayElementState
        {
            private readonly RepeatResourceLedger _resources;
            private List<KeyValuePair<ulong, byte[]>> _elementBytes = [];

            internal RepeatResourceLedger Resources => _resources;
            internal BoundNativeFixedArrayLayout Layout { get; }
            internal ulong ElementCount => Layout.ElementCount;
            internal byte[] GuaranteedBytes { get; }
            internal byte[] TemplateBytes { get; private set; }

            internal RawUnionArrayElementState(
                BoundNativeFixedArrayLayout layout, byte[] guaranteedBytes,
                RepeatResourceLedger resources)
                : this(layout, guaranteedBytes, guaranteedBytes, resources)
            {
            }

            internal RawUnionArrayElementState(
                BoundNativeFixedArrayLayout layout, byte[] guaranteedBytes,
                ReadOnlySpan<byte> templateBytes,
                RepeatResourceLedger resources,
                bool constructionWorkCharged = false)
            {
                ArgumentNullException.ThrowIfNull(guaranteedBytes);
                ArgumentNullException.ThrowIfNull(resources);
                Layout = layout;
                GuaranteedBytes = guaranteedBytes;
                _resources = resources;
                if (!constructionWorkCharged)
                {
                    ChargeWork(8);
                }
                TemplateBytes = CopyPayload(templateBytes);
            }

            private RawUnionArrayElementState(
                BoundNativeFixedArrayLayout layout, byte[] guaranteedBytes,
                byte[] templateBytes, RepeatResourceLedger resources,
                bool constructionWorkCharged, bool takeTemplateOwnership)
            {
                ArgumentNullException.ThrowIfNull(guaranteedBytes);
                ArgumentNullException.ThrowIfNull(templateBytes);
                ArgumentNullException.ThrowIfNull(resources);
                Layout = layout;
                GuaranteedBytes = guaranteedBytes;
                _resources = resources;
                if (!constructionWorkCharged)
                {
                    ChargeWork(8);
                }
                TemplateBytes = takeTemplateOwnership
                    ? templateBytes : CopyPayload(templateBytes);
            }

            internal RawUnionArrayElementState Clone()
            {
                using var repeatFrame = _resources.EnterFrame();
                RawUnionArrayElementState result;
                if (!_resources.IsStateTreeReservationActive)
                {
                    var stateBytes = LogicalBytes();
                    using var inputState = _resources.ReserveState(
                        stateBytes);
                    using var outputState = _resources.ReserveState(
                        stateBytes);
                    result = CloneCore();
                }
                else
                {
                    result = CloneCore();
                }
                repeatFrame.Complete();
                return result;
            }

            private RawUnionArrayElementState CloneCore()
            {
                var result = new RawUnionArrayElementState(
                    Layout, GuaranteedBytes, TemplateBytes, _resources);
                ChargeWork(8);
                foreach (var (index, bytes) in _elementBytes)
                {
                    ChargeWork(15);
                    result._elementBytes.Add(new KeyValuePair<ulong, byte[]>(
                        index, CopyPayload(bytes)));
                }
                return result;
            }

            internal byte[] Get(ulong index)
            {
                var found = TryFind(index, out var position);
                return CopyPayload(found
                    ? _elementBytes[position].Value : TemplateBytes);
            }

            private int LowerBound(ulong index)
            {
                var low = 0;
                var high = _elementBytes.Count;
                while (true)
                {
                    ChargeWork();
                    if (low >= high)
                    {
                        break;
                    }

                    ChargeWork(3);
                    var middle = low + ((high - low) / 2);
                    ChargeWork(3);
                    if (_elementBytes[middle].Key < index)
                    {
                        ChargeWork();
                        low = middle + 1;
                    }
                    else
                    {
                        high = middle;
                    }
                }
                return low;
            }

            private bool TryFind(ulong index, out int position)
            {
                ChargeWork();
                if (index >= Layout.ElementCount)
                {
                    throw Invalid();
                }
                position = LowerBound(index);
                ChargeWork();
                if (position >= _elementBytes.Count)
                {
                    return false;
                }

                ChargeWork(3);
                return _elementBytes[position].Key == index;
            }

            private void ChargeWork(ulong units = 1)
                => _resources.ConsumeWork(units);

            private byte[] CopyPayload(ReadOnlySpan<byte> bytes)
            {
                var result = new byte[bytes.Length];
                for (var index = 0; index < bytes.Length; index++)
                {
                    ChargeWork(4);
                    result[index] = bytes[index];
                }
                return result;
            }

            private bool PayloadEquals(
                ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
            {
                if (left.Length != right.Length)
                {
                    return false;
                }
                for (var index = 0; index < left.Length; index++)
                {
                    ChargeWork(5);
                    if (left[index] != right[index])
                    {
                        return false;
                    }
                }
                return true;
            }

            private byte[] MeetPayload(
                ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
            {
                if (left.Length != right.Length)
                {
                    throw Invalid();
                }

                var result = new byte[left.Length];
                for (var index = 0; index < left.Length; index++)
                {
                    ChargeWork(7);
                    result[index] = (byte)(left[index] & right[index]);
                }
                return result;
            }

            internal BigInteger LogicalBytes()
            {
                ChargeWork(7);
                var bytes = new BigInteger(56) + TemplateBytes.Length;
                foreach (var (_, elementBytes) in _elementBytes)
                {
                    ChargeWork(7);
                    bytes += 48 + elementBytes.Length;
                }
                return bytes;
            }

            internal byte[] Common()
            {
                ChargeWork(2);
                if (Layout.ElementCount == 0)
                {
                    return [];
                }

                var common = (ulong)_elementBytes.Count
                    < Layout.ElementCount ? CopyPayload(TemplateBytes) : null;
                foreach (var entry in _elementBytes)
                {
                    ChargeWork();
                    var bytes = entry.Value;
                    if (common is null)
                    {
                        common = CopyPayload(bytes);
                        continue;
                    }
                    ChargeWork();
                    common = MeetPayload(common, bytes);
                }
                ChargeWork();
                return common ?? CopyPayload(TemplateBytes);
            }

            internal bool Set(ulong index, ReadOnlySpan<byte> bytes)
            {
                ChargeWork(3);
                if (bytes.Length != GuaranteedBytes.Length)
                {
                    throw Invalid();
                }

                var hasIndex = TryFind(index, out var position);
                if (PayloadEquals(bytes, TemplateBytes))
                {
                    if (!hasIndex)
                    {
                        return false;
                    }

                    var inputBytes = LogicalBytes();
                    var outputBytes = inputBytes - 48
                        - _elementBytes[position].Value.Length;
                    using var inputState
                        = _resources.IsStateTreeReservationActive
                            ? null : _resources.ReserveState(inputBytes);
                    using var outputState
                        = _resources.IsStateTreeReservationActive
                            ? null : _resources.ReserveState(outputBytes);
                    ChargeWork(2);
                    var retained = new List<KeyValuePair<ulong, byte[]>>(
                        _elementBytes.Count - 1);
                    for (var item = 0; item < _elementBytes.Count; item++)
                    {
                        ChargeWork(3);
                        if (item == position)
                        {
                            ChargeWork(2);
                            continue;
                        }
                        ChargeWork(4);
                        retained.Add(_elementBytes[item]);
                    }
                    _elementBytes = retained;
                    return true;
                }

                if (hasIndex && PayloadEquals(
                    _elementBytes[position].Value, bytes))
                {
                    return false;
                }

                var existingBytes = LogicalBytes();
                var replacementBytes = existingBytes
                    + (hasIndex ? BigInteger.Zero
                        : new BigInteger(48) + bytes.Length);
                using var inputReservation
                    = _resources.IsStateTreeReservationActive
                        ? null : _resources.ReserveState(existingBytes);
                using var outputReservation
                    = _resources.IsStateTreeReservationActive
                        ? null : _resources.ReserveState(replacementBytes);
                var storedBytes = CopyPayload(bytes);
                ChargeWork(7);
                if (hasIndex)
                {
                    ChargeWork();
                }
                var replacement = new List<KeyValuePair<ulong, byte[]>>(
                    checked(_elementBytes.Count + (hasIndex ? 0 : 1)));
                for (var item = 0; item < position; item++)
                {
                    ChargeWork(7);
                    replacement.Add(_elementBytes[item]);
                }
                if (hasIndex)
                {
                    ChargeWork(3);
                }
                ChargeWork(4);
                replacement.Add(new KeyValuePair<ulong, byte[]>(
                    index, storedBytes));
                for (var item = position + (hasIndex ? 1 : 0);
                    item < _elementBytes.Count; item++)
                {
                    ChargeWork(7);
                    replacement.Add(_elementBytes[item]);
                }
                _elementBytes = replacement;
                return true;
            }

            internal bool TryGetSetPlan(ulong index,
                ReadOnlySpan<byte> bytes, out int position,
                out bool hasIndex, out bool removesEntry)
            {
                ChargeWork(3);
                if (bytes.Length != GuaranteedBytes.Length)
                {
                    throw Invalid();
                }

                hasIndex = TryFind(index, out position);
                removesEntry = PayloadEquals(bytes, TemplateBytes);
                return removesEntry
                    ? hasIndex
                    : !hasIndex || !PayloadEquals(
                        _elementBytes[position].Value, bytes);
            }

            private void AppendSetEntry(
                RawUnionArrayElementState result, ulong index,
                ReadOnlySpan<byte> bytes,
                RepeatResourceLedger.StateLease outputState)
            {
                ChargeWork(15);
                outputState.Add(new BigInteger(48) + bytes.Length);
                result._elementBytes.Add(new KeyValuePair<ulong, byte[]>(
                    index, CopyPayload(bytes)));
            }

            internal RawUnionArrayElementState CreateSet(ulong index,
                ReadOnlySpan<byte> bytes, int position, bool hasIndex,
                bool removesEntry,
                RepeatResourceLedger.StateLease outputState)
            {
                using var repeatFrame = _resources.EnterFrame();
                if (position < 0 || position > _elementBytes.Count
                    || (hasIndex && position == _elementBytes.Count)
                    || (removesEntry && !hasIndex))
                {
                    throw Invalid();
                }

                ChargeWork(8);
                outputState.Add(new BigInteger(56) + TemplateBytes.Length);
                var result = new RawUnionArrayElementState(
                    Layout, GuaranteedBytes, TemplateBytes, _resources,
                    constructionWorkCharged: true);
                var entryAdded = false;
                for (var item = 0; item < _elementBytes.Count; item++)
                {
                    ChargeWork(7);
                    if (!hasIndex && !entryAdded && item == position)
                    {
                        AppendSetEntry(result, index, bytes, outputState);
                        entryAdded = true;
                    }

                    if (hasIndex && item == position)
                    {
                        if (!removesEntry)
                        {
                            AppendSetEntry(result, index, bytes,
                                outputState);
                            entryAdded = true;
                        }
                        else
                        {
                            ChargeWork(2);
                        }
                        continue;
                    }

                    var entry = _elementBytes[item];
                    AppendSetEntry(result, entry.Key, entry.Value,
                        outputState);
                }

                if (!hasIndex && !entryAdded)
                {
                    if (position != _elementBytes.Count)
                    {
                        throw Invalid();
                    }
                    AppendSetEntry(result, index, bytes, outputState);
                }
                repeatFrame.Complete();
                return result;
            }

            internal static RawUnionArrayElementState CreateInitializedSet(
                BoundNativeFixedArrayLayout layout, byte[] defaultBytes,
                ulong index, ReadOnlySpan<byte> bytes,
                RepeatResourceLedger resources,
                RepeatResourceLedger.StateLease outputState)
            {
                using var repeatFrame = resources.EnterFrame();
                resources.ConsumeWork();
                if (bytes.Length != defaultBytes.Length
                    || index >= layout.ElementCount)
                {
                    throw Invalid();
                }

                resources.ConsumeWork(8);
                outputState.Add(new BigInteger(56) + defaultBytes.Length);
                var result = new RawUnionArrayElementState(layout,
                    defaultBytes, defaultBytes, resources,
                    constructionWorkCharged: true);
                result.ChargeWork(15);
                outputState.Add(new BigInteger(48) + bytes.Length);
                result._elementBytes.Add(new KeyValuePair<ulong, byte[]>(
                    index, result.CopyPayload(bytes)));
                repeatFrame.Complete();
                return result;
            }

            internal BigInteger MergeLogicalBytes(
                RawUnionArrayElementState other)
            {
                using var repeatFrame = _resources.EnterFrame();
                ChargeWork();
                if (Layout != other.Layout
                    || !ReferenceEquals(_resources, other._resources)
                    || !PayloadEquals(GuaranteedBytes,
                        other.GuaranteedBytes))
                {
                    throw Invalid();
                }

                using var templateScratch = _resources.ReserveState(
                    new BigInteger(TemplateBytes.Length));
                var mergedTemplate = MergeTemplate(other, templateScratch,
                    null, outputAlreadyReserved: true);
                var resultBytes = new BigInteger(56)
                    + mergedTemplate.Length;
                ForEachMergedOverride(other, mergedTemplate,
                    (index, mergedBytes, scratch) =>
                    {
                        _ = index;
                        _ = scratch;
                        resultBytes += new BigInteger(48)
                            + mergedBytes.Length;
                    });
                repeatFrame.Complete();
                return resultBytes;
            }

            private byte[] MergeTemplate(
                RawUnionArrayElementState other,
                RepeatResourceLedger.StateLease templateScratch,
                RepeatResourceLedger.StateLease? outputState,
                bool outputAlreadyReserved)
            {
                ChargeWork(9);
                ChargeWork(8);
                if (!outputAlreadyReserved)
                {
                    (outputState ?? throw Invalid()).Add(56);
                }
                var mergedTemplate = MeetPayload(
                    TemplateBytes, other.TemplateBytes);
                ChargeWork();
                _ = PayloadEquals(TemplateBytes, mergedTemplate);
                if (!outputAlreadyReserved)
                {
                    (outputState ?? throw Invalid()).TransferFrom(
                        templateScratch,
                        new BigInteger(mergedTemplate.Length),
                        BigInteger.Zero);
                }

                return mergedTemplate;
            }

            private void ForEachMergedOverride(
                RawUnionArrayElementState other, byte[] mergedTemplate,
                Action<ulong, byte[], RepeatResourceLedger.StateLease> emit)
            {
                var leftIndex = 0;
                var rightIndex = 0;
                var leftLoaded = false;
                var rightLoaded = false;
                ulong leftKey = 0;
                ulong rightKey = 0;
                while (true)
                {
                    ChargeWork();
                    var hasLeft = leftLoaded
                        || leftIndex < _elementBytes.Count;
                    if (hasLeft && !leftLoaded)
                    {
                        ChargeWork(2);
                        leftKey = _elementBytes[leftIndex].Key;
                        leftLoaded = true;
                    }

                    ChargeWork();
                    var hasRight = rightLoaded
                        || rightIndex < other._elementBytes.Count;
                    if (hasRight && !rightLoaded)
                    {
                        ChargeWork(2);
                        rightKey = other._elementBytes[rightIndex].Key;
                        rightLoaded = true;
                    }

                    if (!hasLeft && !hasRight)
                    {
                        break;
                    }

                    var comparison = 0;
                    if (hasLeft && hasRight)
                    {
                        ChargeWork();
                        comparison = leftKey.CompareTo(rightKey);
                    }

                    ulong index;
                    var leftOverride = false;
                    var rightOverride = false;
                    if (!hasRight || (hasLeft && comparison < 0))
                    {
                        index = leftKey;
                        leftOverride = true;
                        leftIndex++;
                        leftLoaded = false;
                    }
                    else if (!hasLeft || comparison > 0)
                    {
                        index = rightKey;
                        rightOverride = true;
                        rightIndex++;
                        rightLoaded = false;
                    }
                    else
                    {
                        index = leftKey;
                        leftOverride = true;
                        rightOverride = true;
                        leftIndex++;
                        rightIndex++;
                        leftLoaded = false;
                        rightLoaded = false;
                    }

                    var leftValue = leftOverride
                        ? _elementBytes[leftIndex - 1].Value
                        : TemplateBytes;
                    var rightValue = rightOverride
                        ? other._elementBytes[rightIndex - 1].Value
                        : other.TemplateBytes;
                    ChargeWork(10);
                    using var candidateScratch = _resources.ReserveState(
                        new BigInteger(TemplateBytes.Length));
                    var mergedBytes = MeetPayload(leftValue, rightValue);
                    ChargeWork();
                    var equalsTemplate = PayloadEquals(
                        mergedBytes, mergedTemplate);
                    _ = PayloadEquals(leftValue, mergedBytes);
                    if (!equalsTemplate)
                    {
                        ChargeWork(9);
                        emit(index, mergedBytes, candidateScratch);
                    }
                    else if (leftOverride)
                    {
                        ChargeWork(2);
                    }
                }
            }

            internal RawUnionArrayElementState CreateMerge(
                RawUnionArrayElementState other,
                RepeatResourceLedger.StateLease outputState,
                bool outputAlreadyReserved = false)
            {
                using var repeatFrame = _resources.EnterFrame();
                ChargeWork();
                if (Layout != other.Layout
                    || !ReferenceEquals(_resources, other._resources)
                    || !PayloadEquals(GuaranteedBytes,
                        other.GuaranteedBytes))
                {
                    throw Invalid();
                }

                using var templateScratch = _resources.ReserveState(
                    new BigInteger(TemplateBytes.Length));
                var mergedTemplate = MergeTemplate(other, templateScratch,
                    outputState, outputAlreadyReserved);
                var result = new RawUnionArrayElementState(
                    Layout, GuaranteedBytes, mergedTemplate, _resources,
                    constructionWorkCharged: true,
                    takeTemplateOwnership: true);

                ForEachMergedOverride(other, mergedTemplate,
                    (index, mergedBytes, candidateScratch) =>
                    {
                        if (!outputAlreadyReserved)
                        {
                            outputState.TransferFrom(candidateScratch,
                                new BigInteger(mergedBytes.Length), 48);
                        }
                        result._elementBytes.Add(
                            new KeyValuePair<ulong, byte[]>(
                                index, mergedBytes));
                    });

                repeatFrame.Complete();
                return result;
            }

            internal RawUnionArrayElementState CloneTo(
                RepeatResourceLedger.StateLease outputState,
                bool outputAlreadyReserved = false)
            {
                using var repeatFrame = _resources.EnterFrame();
                ChargeWork(8);
                if (!outputAlreadyReserved)
                {
                    outputState.Add(new BigInteger(56)
                        + TemplateBytes.Length);
                }
                var result = new RawUnionArrayElementState(
                    Layout, GuaranteedBytes, TemplateBytes, _resources,
                    constructionWorkCharged: true);
                foreach (var (index, bytes) in _elementBytes)
                {
                    ChargeWork(15);
                    if (!outputAlreadyReserved)
                    {
                        outputState.Add(new BigInteger(48) + bytes.Length);
                    }
                    result._elementBytes.Add(
                        new KeyValuePair<ulong, byte[]>(
                            index, CopyPayload(bytes)));
                }
                repeatFrame.Complete();
                return result;
            }

            internal bool WriteRange(ulong index, int offset,
                ReadOnlySpan<byte> bytes)
            {
                ChargeWork(4);
                if (index >= Layout.ElementCount || offset < 0
                    || offset > TemplateBytes.Length
                    || bytes.Length > TemplateBytes.Length - offset)
                {
                    throw Invalid();
                }

                using var scratchState
                    = _resources.ReserveState(
                        new BigInteger(TemplateBytes.Length));
                var elementBytes = Get(index);
                for (var byteIndex = 0; byteIndex < bytes.Length; byteIndex++)
                {
                    ChargeWork(4);
                    elementBytes[offset + byteIndex] = bytes[byteIndex];
                }
                return Set(index, elementBytes);
            }

            internal bool HasTemplateChange(ReadOnlySpan<byte> bytes)
            {
                if (bytes.Length != GuaranteedBytes.Length)
                {
                    throw Invalid();
                }

                var changed = !PayloadEquals(TemplateBytes, bytes);
                foreach (var (_, elementBytes) in _elementBytes)
                {
                    ChargeWork();
                    if (PayloadEquals(elementBytes, bytes))
                    {
                        changed = true;
                    }
                }
                ChargeWork();
                return changed;
            }

            internal RawUnionArrayElementState CreateSetTemplate(
                ReadOnlySpan<byte> bytes,
                RepeatResourceLedger.StateLease? outputState)
            {
                using var repeatFrame = _resources.EnterFrame();
                if (bytes.Length != GuaranteedBytes.Length)
                {
                    throw Invalid();
                }

                ChargeWork(8);
                outputState?.Add(new BigInteger(56) + bytes.Length);
                var result = new RawUnionArrayElementState(
                    Layout, GuaranteedBytes, bytes, _resources,
                    constructionWorkCharged: true);
                foreach (var (index, elementBytes) in _elementBytes)
                {
                    ChargeWork();
                    if (PayloadEquals(elementBytes, bytes))
                    {
                        ChargeWork(2);
                        continue;
                    }

                    ChargeWork(15);
                    outputState?.Add(new BigInteger(48)
                        + elementBytes.Length);
                    result._elementBytes.Add(
                        new KeyValuePair<ulong, byte[]>(
                            index, CopyPayload(elementBytes)));
                }
                repeatFrame.Complete();
                return result;
            }

            internal bool ApplyUnknownRangeWrite(int offset,
                ReadOnlySpan<byte> bytes)
            {
                ChargeWork(2);
                if (Layout.ElementCount == 0)
                {
                    throw Invalid();
                }
                if (Layout.ElementCount == 1)
                {
                    return WriteRange(0, offset, bytes);
                }
                return ApplyPossibleUnknownRangeWrite(offset, bytes);
            }

            internal bool ApplyPossibleExactRangeWrite(ulong index,
                int offset, ReadOnlySpan<byte> bytes)
            {
                ChargeWork(4);
                if (index >= Layout.ElementCount || offset < 0
                    || offset > TemplateBytes.Length
                    || bytes.Length > TemplateBytes.Length - offset)
                {
                    throw Invalid();
                }

                var elementBytes = Get(index);
                for (var byteIndex = 0; byteIndex < bytes.Length;
                    byteIndex++)
                {
                    ChargeWork(7);
                    elementBytes[offset + byteIndex]
                        = (byte)(elementBytes[offset + byteIndex]
                            & bytes[byteIndex]);
                }
                return Set(index, elementBytes);
            }

            internal bool ApplyPossibleUnknownRangeWrite(int offset,
                ReadOnlySpan<byte> bytes)
            {
                ChargeWork(3);
                if (Layout.ElementCount == 0 || offset < 0
                    || offset > TemplateBytes.Length
                    || bytes.Length > TemplateBytes.Length - offset)
                {
                    throw Invalid();
                }

                var mergedTemplate = CopyPayload(TemplateBytes);
                ChargeWork();
                for (var byteIndex = 0; byteIndex < bytes.Length;
                    byteIndex++)
                {
                    ChargeWork(7);
                    mergedTemplate[offset + byteIndex]
                        = (byte)(mergedTemplate[offset + byteIndex]
                            & bytes[byteIndex]);
                }

                var changed = !PayloadEquals(TemplateBytes, mergedTemplate);
                var mergedElements
                    = new List<KeyValuePair<ulong, byte[]>>(
                        _elementBytes.Count);
                foreach (var (index, elementBytes) in _elementBytes)
                {
                    ChargeWork();
                    var mergedBytes = CopyPayload(elementBytes);
                    for (var byteIndex = 0; byteIndex < bytes.Length;
                        byteIndex++)
                    {
                        ChargeWork(7);
                        mergedBytes[offset + byteIndex]
                            = (byte)(mergedBytes[offset + byteIndex]
                                & bytes[byteIndex]);
                    }
                    var equalsTemplate = PayloadEquals(
                        mergedBytes, mergedTemplate);
                    var equalsOriginal = PayloadEquals(
                        elementBytes, mergedBytes);
                    if (!equalsTemplate)
                    {
                        ChargeWork(6);
                        mergedElements.Add(new KeyValuePair<ulong, byte[]>(
                            index, mergedBytes));
                    }
                    else
                    {
                        ChargeWork(2);
                    }
                    changed |= !equalsOriginal || equalsTemplate;
                }

                ChargeWork();
                _elementBytes = mergedElements;
                TemplateBytes = mergedTemplate;
                return changed;
            }

            internal bool ApplyUnknownIndexWrite(ReadOnlySpan<byte> bytes)
            {
                ChargeWork(3);
                if (bytes.Length != GuaranteedBytes.Length)
                {
                    throw Invalid();
                }
                if (Layout.ElementCount == 0)
                {
                    throw Invalid();
                }
                if (Layout.ElementCount == 1)
                {
                    return Set(0, bytes);
                }

                var mergedTemplate = MeetPayload(TemplateBytes, bytes);
                ChargeWork();
                var changed = !PayloadEquals(TemplateBytes, mergedTemplate);
                var mergedElements
                    = new List<KeyValuePair<ulong, byte[]>>(
                        _elementBytes.Count);
                foreach (var (index, elementBytes) in _elementBytes)
                {
                    ChargeWork();
                    var mergedBytes = MeetPayload(elementBytes, bytes);
                    var equalsTemplate = PayloadEquals(
                        mergedBytes, mergedTemplate);
                    var equalsOriginal = PayloadEquals(
                        elementBytes, mergedBytes);
                    if (!equalsTemplate)
                    {
                        ChargeWork(6);
                        mergedElements.Add(new KeyValuePair<ulong, byte[]>(
                            index, mergedBytes));
                    }
                    else
                    {
                        ChargeWork(2);
                    }
                    changed |= !equalsOriginal || equalsTemplate;
                }

                ChargeWork();
                _elementBytes = mergedElements;
                TemplateBytes = mergedTemplate;
                return changed;
            }

            internal bool Merge(RawUnionArrayElementState other,
                RepeatResourceLedger.StateLease? stateOwner = null)
            {
                ChargeWork();
                if (Layout != other.Layout
                    || !ReferenceEquals(_resources, other._resources)
                    || !PayloadEquals(GuaranteedBytes,
                        other.GuaranteedBytes))
                {
                    throw Invalid();
                }

                var inputBytes = LogicalBytes();
                var otherInputBytes = other.LogicalBytes();
                if (stateOwner is not null
                    && !stateOwner.IsOwnedBy(_resources))
                {
                    throw Invalid();
                }
                using var inputState = stateOwner is null
                    ? _resources.ReserveState(inputBytes) : null;
                using var otherInputState = stateOwner is null
                    ? _resources.ReserveState(otherInputBytes) : null;
                using var outputState
                    = _resources.ReserveState(BigInteger.Zero);
                var merged = CreateMerge(other, outputState);
                var changed = !SemanticallyEquals(merged);
                if (!changed)
                {
                    return false;
                }
                if (stateOwner is not null)
                {
                    var mergedBytes = merged.LogicalBytes();
                    if (mergedBytes > inputBytes)
                    {
                        stateOwner.Add(mergedBytes - inputBytes);
                    }
                    else if (mergedBytes < inputBytes)
                    {
                        stateOwner.Remove(inputBytes - mergedBytes);
                    }
                }
                TemplateBytes = merged.TemplateBytes;
                _elementBytes = merged._elementBytes;
                return true;
            }

            internal bool SemanticallyEquals(
                RawUnionArrayElementState other)
            {
                ChargeWork(6);
                if (Layout != other.Layout)
                {
                    return false;
                }
                ChargeWork();
                if (ElementCount != other.ElementCount
                    || !PayloadEquals(GuaranteedBytes, other.GuaranteedBytes)
                    || !PayloadEquals(TemplateBytes, other.TemplateBytes))
                {
                    return false;
                }

                var leftIndex = 0;
                var rightIndex = 0;
                while (true)
                {
                    ChargeWork();
                    var hasLeft = leftIndex < _elementBytes.Count;
                    if (hasLeft)
                    {
                        ChargeWork(2);
                    }
                    ChargeWork();
                    var hasRight = rightIndex < other._elementBytes.Count;
                    if (hasRight)
                    {
                        ChargeWork(2);
                    }
                    if (!hasLeft && !hasRight)
                    {
                        return true;
                    }

                    var comparison = hasLeft && hasRight
                        ? _elementBytes[leftIndex].Key.CompareTo(
                            other._elementBytes[rightIndex].Key)
                        : hasLeft ? -1 : 1;
                    if (hasLeft && hasRight)
                    {
                        ChargeWork();
                    }
                    var leftOverride = false;
                    var rightOverride = false;
                    if (comparison < 0)
                    {
                        leftIndex++;
                        leftOverride = true;
                    }
                    else if (comparison > 0)
                    {
                        rightIndex++;
                        rightOverride = true;
                    }
                    else
                    {
                        leftIndex++;
                        rightIndex++;
                        leftOverride = true;
                        rightOverride = true;
                    }

                    var leftValue = leftOverride
                        ? _elementBytes[leftIndex - 1].Value
                        : TemplateBytes;
                    var rightValue = rightOverride
                        ? other._elementBytes[rightIndex - 1].Value
                        : other.TemplateBytes;
                    ChargeWork(10);
                    if (!PayloadEquals(leftValue, rightValue))
                    {
                        return false;
                    }
                }
            }
        }

        // Joins retain never-published vacancy (1), liveness (2), retired
        // vacancy (4), and partial structural ownership (8).
        private readonly byte[] _possibilities;
        private readonly uint[] _possibleFieldMasks;
        private FieldGenerationContext _fieldGenerationContext;
        private Dictionary<FieldGenerationKey, long> _fieldPlaceGenerations;
        private Dictionary<NestedOwnerStateKey, NestedOwnerFieldState> _nestedOwnerStates;
        private readonly Dictionary<int, byte[]> _rawUnionInitializedBytes = [];
        private SortedDictionary<RawUnionArrayStateKey,
            RawUnionArrayElementState> _rawUnionArrayElementBytes
                = new(RawUnionArrayStateKeyComparer.Instance);
        private readonly Dictionary<BoundExpression, byte> _canBeFacts;
        private readonly Dictionary<BoundExpression, byte> _flowFacts;
        private readonly Dictionary<BoundExpression, byte> _completionShapeFacts;
        private readonly Dictionary<BoundExpression, bool> _completionFacts;
        private readonly Dictionary<BoundExpression, byte> _nativeOutputFlowFacts;
        private readonly NativeOutputDestinationState?[] _nativeOutputDestinations;
        private readonly NativeOutputCallFact?[] _nativeOutputResults;
        private readonly List<ActiveStorageScope> _activeStorageScopes = [];
        private RepeatSnapshotReservationScope? _repeatSnapshotScope;
        private int _nativeOutputStateCount;
        internal bool MayHaveNativeOutputState => _nativeOutputStateCount != 0;
        internal bool HasNativeOutputContracts { get; }
        internal bool HasDoesNotReturnFunctions { get; }
        internal BoundProgram Program { get; }
        internal RepeatSnapshotReservationScope? RepeatSnapshotScope
            => _repeatSnapshotScope;
        internal Dictionary<BoundStatement, InitializationState>? StatementInputs { get; init; }
        internal RepeatSnapshotReservationScope? StatementInputSnapshotScope { get; init; }
        internal BackingDependencies? Dependencies { get; init; }
        internal HashSet<NativePointerWriteEffect>[]? NativePointerCaptureEffects { get; init; }
        internal RawUnionReturnCoverageBuilder? RawUnionReturnCoverage { get; init; }
        internal ImmutableArray<byte>[] RawUnionReturnCoverageSummaries
        { get; init; } = [];
        internal RepeatResourceLedger RepeatResources { get; init; } = new();
        internal int ScopeActivation { get; set; }

        internal InitializationState(BoundProgram program, int length,
            bool hasNativeOutputContracts, bool hasDoesNotReturnFunctions)
            : this(program, length, new Dictionary<BoundExpression, byte>(ReferenceEqualityComparer.Instance),
                new Dictionary<BoundExpression, byte>(ReferenceEqualityComparer.Instance),
                new Dictionary<BoundExpression, byte>(ReferenceEqualityComparer.Instance),
                new Dictionary<BoundExpression, bool>(ReferenceEqualityComparer.Instance),
                new Dictionary<BoundExpression, byte>(ReferenceEqualityComparer.Instance),
                hasNativeOutputContracts, hasDoesNotReturnFunctions,
                new FieldGenerationContext(), new Dictionary<FieldGenerationKey, long>(),
                new Dictionary<NestedOwnerStateKey, NestedOwnerFieldState>())
        {
        }

        private InitializationState(BoundProgram program, int length,
            Dictionary<BoundExpression, byte> canBeFacts, Dictionary<BoundExpression, byte> flowFacts,
            Dictionary<BoundExpression, byte> completionShapeFacts,
            Dictionary<BoundExpression, bool> completionFacts,
            Dictionary<BoundExpression, byte> nativeOutputFlowFacts,
            bool hasNativeOutputContracts, bool hasDoesNotReturnFunctions,
            FieldGenerationContext fieldGenerationContext,
            Dictionary<FieldGenerationKey, long> fieldPlaceGenerations,
            Dictionary<NestedOwnerStateKey, NestedOwnerFieldState> nestedOwnerStates)
        {
            Program = program;
            HasNativeOutputContracts = hasNativeOutputContracts;
            HasDoesNotReturnFunctions = hasDoesNotReturnFunctions;
            _possibilities = new byte[length];
            _possibleFieldMasks = new uint[length];
            _fieldGenerationContext = fieldGenerationContext;
            _fieldPlaceGenerations = fieldPlaceGenerations;
            _nestedOwnerStates = nestedOwnerStates;
            _canBeFacts = canBeFacts;
            _flowFacts = flowFacts;
            _completionShapeFacts = completionShapeFacts;
            _completionFacts = completionFacts;
            _nativeOutputFlowFacts = nativeOutputFlowFacts;
            _nativeOutputDestinations = new NativeOutputDestinationState?[length];
            _nativeOutputResults = new NativeOutputCallFact?[length];
            Array.Fill(_possibilities, (byte)1);
        }

        internal int Length => _possibilities.Length;
        internal Dictionary<BoundExpression, byte> CanBeFacts => _canBeFacts;
        internal Dictionary<BoundExpression, byte> FlowFacts => _flowFacts;
        internal Dictionary<BoundExpression, byte> CompletionShapeFacts
            => _completionShapeFacts;
        internal Dictionary<BoundExpression, bool> CompletionFacts => _completionFacts;

        internal bool TryGetNativeOutputFlowEffect(BoundExpression expression, out bool hasEffect)
        {
            if (_nativeOutputFlowFacts.TryGetValue(expression, out var facts)
                && (facts & NativeOutputFlowAnalyzedFact) != 0)
            {
                hasEffect = (facts & NativeOutputFlowEffectFact) != 0;
                return true;
            }
            hasEffect = false;
            return false;
        }

        internal void RecordNativeOutputFlowEffect(BoundExpression expression, bool hasEffect)
        {
            _nativeOutputFlowFacts[expression] = (byte)(NativeOutputFlowAnalyzedFact
                | (hasEffect ? NativeOutputFlowEffectFact : 0));
        }

        internal void Fill(bool value, int start, int count) =>
            Array.Fill(_possibilities, value ? (byte)2 : (byte)1, start, count);

        internal bool HasNativeOutputState(int index) =>
            (uint)index < (uint)Length
                && (_nativeOutputDestinations[index] is not null || _nativeOutputResults[index] is not null);

        private void SetNativeOutputState(int index, NativeOutputDestinationState? destination,
            NativeOutputCallFact? result)
        {
            var hadState = HasNativeOutputState(index);
            _nativeOutputDestinations[index] = destination;
            _nativeOutputResults[index] = result;
            var hasState = HasNativeOutputState(index);
            if (hadState != hasState)
            {
                _nativeOutputStateCount += hasState ? 1 : -1;
            }
        }

        internal bool this[int index]
        {
            get
            {
                return _possibilities[index] == 2;
            }
            set
            {
                _possibilities[index] = value ? (byte)2 : (byte)1;
            }
        }

        internal bool IsVacant(int index) =>
            (_possibilities[index] & (2 | 8)) == 0 && !IsNativeOutputReserved(index);

        internal bool IsNativeOutputReserved(int index) =>
            (uint)index < (uint)Length
                && _nativeOutputDestinations[index] is { IsReserved: true };

        internal bool HasRetiredValue(int index) => (_possibilities[index] & 4) != 0;
        internal bool HasPartialValue(int index) => (_possibilities[index] & 8) != 0;

        internal uint PossibleFieldMask(int index) =>
            (uint)index < (uint)Length ? _possibleFieldMasks[index] : throw Invalid();

        internal long FieldPlaceGeneration(int variable, StorageProjection projection)
        {
            if ((uint)variable >= (uint)Length
                || (byte)projection.Kind > (byte)StorageProjectionKind.InlineFieldPath
                || (projection.Kind == StorageProjectionKind.InlineFieldPath
                    && projection.FieldPath.Segments.IsEmpty))
            {
                throw Invalid();
            }

            var generation = 0L;
            var generationDepth = -1;
            if (projection.Kind is StorageProjectionKind.UnknownElement
                or StorageProjectionKind.UnknownArrayElement)
            {
                return _fieldPlaceGenerations.Any(pair => pair.Key.Variable == variable
                    && pair.Key.Projection.Kind is StorageProjectionKind.WholeObject
                        or StorageProjectionKind.InlineFieldPath)
                    ? StorageKey.UnknownGeneration : generation;
            }

            foreach (var (key, candidate) in _fieldPlaceGenerations)
            {
                if (key.Variable != variable
                    || !IsGenerationAncestor(key.Projection, projection, out var candidateDepth)
                    || candidateDepth <= generationDepth)
                {
                    continue;
                }
                generation = candidate;
                generationDepth = candidateDepth;
            }
            return generation;
        }

        internal void AdvanceFieldPlaceGeneration(int variable,
            StorageProjection projection, TextSpan span)
        {
            if ((uint)variable >= (uint)Length
                || projection.Kind is not (StorageProjectionKind.WholeObject
                    or StorageProjectionKind.InlineFieldPath)
                || (projection.Kind == StorageProjectionKind.InlineFieldPath
                    && projection.FieldPath.Segments.IsEmpty))
            {
                throw Invalid(span);
            }

            var generation = _fieldGenerationContext.Get(
                variable, projection, ScopeActivation, span);
            foreach (var key in _fieldPlaceGenerations.Keys.Where(key =>
                key.Variable == variable
                && PlaceGenerationAffectedBy(projection, key.Projection)).ToArray())
            {
                _fieldPlaceGenerations[key] = generation;
            }
            _fieldPlaceGenerations[new FieldGenerationKey(variable, projection)] = generation;
        }

        internal bool CopyFieldPlaceGenerations(int variable, InitializationState other)
        {
            if (ReferenceEquals(this, other))
            {
                return false;
            }
            var previous = _fieldPlaceGenerations
                .Where(pair => pair.Key.Variable == variable)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value);
            var next = other._fieldPlaceGenerations
                .Where(pair => pair.Key.Variable == variable)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value);
            ResetFieldPlaceGenerations(variable);
            foreach (var (key, generation) in next)
            {
                _fieldPlaceGenerations.Add(key, generation);
            }
            return !GenerationMapsEqual(previous, next);
        }

        internal bool MergeFieldPlaceGenerations(int variable, InitializationState other)
        {
            return MergeFieldPlaceGenerations(variable, this, other);
        }

        internal bool MergeFieldPlaceGenerations(int variable,
            InitializationState first, InitializationState second)
        {
            var firstValues = first._fieldPlaceGenerations
                .Where(pair => pair.Key.Variable == variable)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value);
            var secondValues = second._fieldPlaceGenerations
                .Where(pair => pair.Key.Variable == variable)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value);
            var keys = new HashSet<FieldGenerationKey>(firstValues.Keys);
            keys.UnionWith(secondValues.Keys);
            var previous = _fieldPlaceGenerations
                .Where(pair => pair.Key.Variable == variable)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value);
            var next = new Dictionary<FieldGenerationKey, long>(keys.Count);
            ResetFieldPlaceGenerations(variable);
            foreach (var key in keys)
            {
                var firstGeneration = firstValues.TryGetValue(key, out var current)
                    ? current : 0;
                var secondGeneration = secondValues.TryGetValue(key, out var other)
                    ? other : 0;
                next.Add(key, firstGeneration == secondGeneration
                    ? firstGeneration : StorageKey.UnknownGeneration);
            }
            foreach (var (key, generation) in next)
            {
                _fieldPlaceGenerations.Add(key, generation);
            }
            return !GenerationMapsEqual(previous, next);
        }

        private void ResetFieldPlaceGenerations(int variable)
        {
            foreach (var key in _fieldPlaceGenerations.Keys
                .Where(key => key.Variable == variable).ToArray())
            {
                _ = _fieldPlaceGenerations.Remove(key);
            }
        }

        private static bool GenerationMapsEqual(
            Dictionary<FieldGenerationKey, long> left,
            Dictionary<FieldGenerationKey, long> right)
        {
            return left.Count == right.Count
                && left.All(pair => right.TryGetValue(pair.Key, out var generation)
                    && generation == pair.Value);
        }

        private static bool IsGenerationAncestor(
            StorageProjection generation, StorageProjection place, out int depth)
        {
            if (generation.Kind == StorageProjectionKind.WholeObject)
            {
                depth = 0;
                return true;
            }
            if (generation.Kind == StorageProjectionKind.InlineFieldPath
                && place.Kind == StorageProjectionKind.InlineFieldPath
                && generation.FieldPath.IsPrefixOf(place.FieldPath))
            {
                depth = generation.FieldPath.Segments.Length;
                return true;
            }
            depth = -1;
            return false;
        }

        private static bool PlaceGenerationAffectedBy(
            StorageProjection mutation, StorageProjection place)
        {
            if (mutation.Kind == StorageProjectionKind.WholeObject)
            {
                return true;
            }
            return mutation.Kind == StorageProjectionKind.InlineFieldPath
                && place.Kind == StorageProjectionKind.InlineFieldPath
                && mutation.FieldPath.IsPrefixOf(place.FieldPath);
        }

        internal NestedOwnerFieldState NestedOwnerFields(
            int variable, FieldProjectionPath ownerPath, PrimitiveType type)
        {
            if ((uint)variable >= (uint)Length
                || ownerPath.Segments.IsEmpty
                || !IsUnique(Program, type)
                || !HasFields(Program, type))
            {
                throw Invalid();
            }

            var fullMask = FullMask(Program, type);
            return NestedOwnerFields(variable, ownerPath, type, fullMask);
        }

        internal NestedOwnerFieldState NestedOwnerFields(
            int variable, FieldProjectionPath ownerPath, PrimitiveType type,
            uint initialMask)
        {
            if ((uint)variable >= (uint)Length
                || ownerPath.Segments.IsEmpty
                || !SupportsNestedOwnerFieldState(type)
                || (initialMask & ~FullMask(Program, type)) != 0)
            {
                throw Invalid();
            }

            var fullMask = FullMask(Program, type);
            var key = new NestedOwnerStateKey(variable, ownerPath);
            if (!_nestedOwnerStates.TryGetValue(key, out var fields))
            {
                return new NestedOwnerFieldState(
                    type, fullMask, initialMask, initialMask);
            }
            if (fields.Type != type || fields.FullMask != fullMask
                || (fields.DefiniteMask & ~fields.PossibleMask) != 0
                || (fields.DefiniteMask & ~fullMask) != 0
                || (fields.PossibleMask & ~fullMask) != 0)
            {
                throw Invalid();
            }
            return fields;
        }

        internal void SetNestedOwnerFields(
            int variable, FieldProjectionPath ownerPath, NestedOwnerFieldState fields)
        {
            if ((uint)variable >= (uint)Length || ownerPath.Segments.IsEmpty
                || !SupportsNestedOwnerFieldState(fields.Type)
                || fields.FullMask != FullMask(Program, fields.Type)
                || (fields.DefiniteMask & ~fields.PossibleMask) != 0
                || (fields.DefiniteMask & ~fields.FullMask) != 0
                || (fields.PossibleMask & ~fields.FullMask) != 0)
            {
                throw Invalid();
            }

            var key = new NestedOwnerStateKey(variable, ownerPath);
            if (fields.IsComplete && IsUnique(Program, fields.Type))
            {
                _ = _nestedOwnerStates.Remove(key);
            }
            else
            {
                _nestedOwnerStates[key] = fields;
            }
        }

        private bool SupportsNestedOwnerFieldState(PrimitiveType type) =>
            HasFields(Program, type) && !ManagedClassTypes.IsClass(type)
            && (IsUnique(Program, type)
                || CarriesRawUnionPayloadFacts(Program, type)
                || NativeFixedArrayContract
                    .HasInlineFixedArrayFieldStorage(type, Program.Structures,
                        Program.NativeFixedArrayTypes)
                || (Program.NativeLoweringAgreement
                        == CompilationOptions.WindowsX64NormalBackingAgreement
                    && (int)type >= 100
                    && (int)type - 100 < Program.Structures.Length));

        internal void ResetNestedOwnerStates(int variable)
        {
            foreach (var key in _nestedOwnerStates.Keys
                .Where(key => key.Variable == variable).ToArray())
            {
                _ = _nestedOwnerStates.Remove(key);
            }
        }

        internal void ResetNestedOwnerSubtree(
            int variable, FieldProjectionPath ownerPath)
        {
            if ((uint)variable >= (uint)Length || ownerPath.Segments.IsEmpty)
            {
                throw Invalid();
            }
            foreach (var key in _nestedOwnerStates.Keys.Where(key =>
                key.Variable == variable && ownerPath.IsPrefixOf(key.OwnerPath)).ToArray())
            {
                _ = _nestedOwnerStates.Remove(key);
            }
        }

        internal bool CopyNestedOwnerStates(int variable, InitializationState other)
        {
            if (ReferenceEquals(this, other))
            {
                return false;
            }
            var previous = _nestedOwnerStates
                .Where(pair => pair.Key.Variable == variable)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value);
            var next = other._nestedOwnerStates
                .Where(pair => pair.Key.Variable == variable)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value);
            ResetNestedOwnerStates(variable);
            foreach (var (key, fields) in next)
            {
                _nestedOwnerStates.Add(key, fields);
            }
            return !NestedOwnerMapsEqual(previous, next);
        }

        internal bool MergeNestedOwnerStates(int variable, InitializationState other)
            => MergeNestedOwnerStates(variable, this, other);

        internal bool MergeNestedOwnerStates(
            int variable, InitializationState first, InitializationState second)
        {
            var firstValues = first._nestedOwnerStates
                .Where(pair => pair.Key.Variable == variable)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value);
            var secondValues = second._nestedOwnerStates
                .Where(pair => pair.Key.Variable == variable)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value);
            var keys = new HashSet<NestedOwnerStateKey>(firstValues.Keys);
            keys.UnionWith(secondValues.Keys);
            var previous = _nestedOwnerStates
                .Where(pair => pair.Key.Variable == variable)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value);
            var next = new Dictionary<NestedOwnerStateKey, NestedOwnerFieldState>(keys.Count);
            ResetNestedOwnerStates(variable);
            foreach (var key in keys)
            {
                var hasFirst = firstValues.TryGetValue(key, out var firstState);
                var hasSecond = secondValues.TryGetValue(key, out var secondState);
                var template = hasFirst ? firstState : secondState;
                var firstFields = hasFirst ? firstState : template with {
                    DefiniteMask = template.FullMask,
                    PossibleMask = template.FullMask,
                };
                var secondFields = hasSecond ? secondState : template with {
                    DefiniteMask = template.FullMask,
                    PossibleMask = template.FullMask,
                };
                if (firstFields.Type != secondFields.Type
                    || firstFields.FullMask != secondFields.FullMask)
                {
                    throw Invalid();
                }
                var fields = new NestedOwnerFieldState(firstFields.Type,
                    firstFields.FullMask,
                    firstFields.DefiniteMask & secondFields.DefiniteMask,
                    firstFields.PossibleMask | secondFields.PossibleMask);
                if (!fields.IsComplete)
                {
                    next.Add(key, fields);
                }
            }
            foreach (var (key, fields) in next)
            {
                _nestedOwnerStates.Add(key, fields);
            }
            return !NestedOwnerMapsEqual(previous, next);
        }

        private static bool NestedOwnerMapsEqual(
            Dictionary<NestedOwnerStateKey, NestedOwnerFieldState> left,
            Dictionary<NestedOwnerStateKey, NestedOwnerFieldState> right)
        {
            return left.Count == right.Count
                && left.All(pair => right.TryGetValue(pair.Key, out var fields)
                    && fields == pair.Value);
        }

        internal void SetPossibleFieldMask(int index, uint mask)
        {
            if ((uint)index >= (uint)Length)
            {
                throw Invalid();
            }
            _possibleFieldMasks[index] = mask;
        }

        internal void MarkPartialValue(int index)
        {
            if ((uint)index >= (uint)Length)
            {
                throw Invalid();
            }
            _possibilities[index] = 8;
        }

        internal void ActivateStorage(int index, long generation)
        {
            ResetFieldPlaceGenerations(index);
            ResetNestedOwnerStates(index);
            ClearRawUnionPayloadFacts(index);
            Dependencies?.ActivateStorage(index, generation, ScopeActivation);
        }

        internal int EnterBlockActivation(TextSpan span) =>
            Dependencies?.EnterBlockActivation(ScopeActivation, span) ?? ScopeActivation;

        internal bool IsStorageScopeActive(int activation)
            => activation == BackingDependencies.RootScopeActivation
                || _activeStorageScopes.Any(scope => scope.Activation == activation);

        internal int EnterLoopActivation(TextSpan span, bool isReentry) =>
            Dependencies?.EnterLoopActivation(ScopeActivation, span, isReentry) ?? ScopeActivation;

        internal void EnterStorageScope(BoundBlock block)
        {
            if (Dependencies is not null)
            {
                _activeStorageScopes.Add(new ActiveStorageScope(ScopeActivation, block));
            }
        }

        internal void ExitStorageScope(BoundBlock block)
        {
            if (Dependencies is null)
            {
                return;
            }
            if (_activeStorageScopes.Count == 0
                || !ReferenceEquals(_activeStorageScopes[^1].Block, block)
                || _activeStorageScopes[^1].Activation != ScopeActivation)
            {
                throw Invalid();
            }
            _activeStorageScopes.RemoveAt(_activeStorageScopes.Count - 1);
        }

        internal void CheckAndExitStorageScopesTo(int activation)
        {
            if (Dependencies is null)
            {
                return;
            }
            var targetIndex = activation == 0 ? -1
                : _activeStorageScopes.FindLastIndex(scope => scope.Activation == activation);
            if (activation != 0 && targetIndex < 0)
            {
                throw Invalid();
            }

            for (var index = _activeStorageScopes.Count - 1; index > targetIndex; index--)
            {
                CheckNativePointerScopeExit(_activeStorageScopes[index].Block, this);
            }
            _activeStorageScopes.RemoveRange(targetIndex + 1,
                _activeStorageScopes.Count - targetIndex - 1);
        }

        internal void AdvanceStorageGeneration(int index, long generation)
        {
            ResetFieldPlaceGenerations(index);
            ResetNestedOwnerStates(index);
            ClearRawUnionPayloadFacts(index);
            Dependencies?.RetireStorage(index, generation);
        }

        internal void Retire(int index, long generation)
        {
            _possibilities[index] = 4;
            _possibleFieldMasks[index] = 0;
            ClearRawUnionPayloadFacts(index);
            SetNativeOutputState(index, null, null);
            Dependencies?.ClearNativePointerProvenance(index);
            AdvanceStorageGeneration(index, generation);
        }

        internal byte Possibilities(int index) => _possibilities[index];

        internal void SetPossibilities(int index, byte value) => _possibilities[index] = value;

        internal void MarkConditional(int index)
        {
            _possibilities[index] = 3;
            SetNativeOutputState(index, null, null);
        }

        internal void Merge(int index, InitializationState other)
        {
            _possibilities[index] |= other._possibilities[index];
            _possibleFieldMasks[index] |= other._possibleFieldMasks[index];
            _ = MergeRawUnionInitializedBytes(index, other);
            _ = MergeFieldPlaceGenerations(index, other);
            _ = MergeNestedOwnerStates(index, other);
            MergeNativeOutputSlot(index, _nativeOutputDestinations[index], _nativeOutputResults[index],
                other._nativeOutputDestinations[index], other._nativeOutputResults[index]);
            if (Dependencies is not null && other.Dependencies is not null)
            {
                _ = Dependencies.MergeSlot(index, other.Dependencies);
            }
            else if (Dependencies is not null || other.Dependencies is not null)
            {
                throw Invalid();
            }
        }

        internal bool MergeProvenanceSlot(int index, InitializationState other)
        {
            if (Dependencies is not null && other.Dependencies is not null)
            {
                return Dependencies.MergeSlot(index, other.Dependencies);
            }
            if (Dependencies is not null || other.Dependencies is not null)
            {
                throw Invalid();
            }
            return false;
        }

        internal bool CopyProvenanceSlot(int index, InitializationState other)
        {
            if (Dependencies is not null && other.Dependencies is not null)
            {
                return Dependencies.CopySlot(index, other.Dependencies);
            }
            if (Dependencies is not null || other.Dependencies is not null)
            {
                throw Invalid();
            }
            return false;
        }

        private void ChargeRepeatRootListHeader()
            => ChargeRepeatRootListHeader(RepeatResources);

        internal static void ChargeRepeatRootListHeader(
            RepeatResourceLedger resources)
            => resources.ConsumeWork(2);

        private void ChargeRepeatRootListHeaderForFirstEntry()
        {
            if (_rawUnionArrayElementBytes.Count == 0)
            {
                ChargeRepeatRootListHeader();
            }
        }

        private void ChargeRepeatRoot(StorageProjection prefix)
            => ChargeRepeatRoot(RepeatResources, prefix);

        internal static void ChargeRepeatRoot(
            RepeatResourceLedger resources, StorageProjection prefix)
        {
            resources.ConsumeWork(5);
            var segments = prefix.ProjectionPath.Segments;
            if (segments.IsEmpty)
            {
                return;
            }

            resources.ConsumeWork(2);
            foreach (var _ in segments)
            {
                resources.ConsumeWork(3);
            }
        }

        internal static BigInteger RawUnionArrayRootLogicalBytes(
            StorageProjection prefix)
        {
            var pathLength = prefix.ProjectionPath.Segments.Length;
            return pathLength == 0
                ? new BigInteger(32)
                : new BigInteger(48) + (16 * pathLength);
        }

        internal static BigInteger RawUnionArrayFactsLogicalBytes(
            IReadOnlyDictionary<StorageProjection,
                RawUnionArrayElementState> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (values.Count == 0)
            {
                return BigInteger.Zero;
            }

            using var enumerator = values.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                throw Invalid();
            }

            var firstState = enumerator.Current.Value ?? throw Invalid();
            var resources = firstState.Resources;
            ChargeRepeatRootListHeader(resources);
            var bytes = new BigInteger(16);
            do
            {
                var (prefix, state) = enumerator.Current;
                if (state is null || !ReferenceEquals(state.Resources,
                        resources))
                {
                    throw Invalid();
                }

                ChargeRepeatRoot(resources, prefix);
                bytes += RawUnionArrayRootLogicalBytes(prefix)
                    + state.LogicalBytes();
            }
            while (enumerator.MoveNext());

            return bytes;
        }

        private BigInteger RawUnionArrayLogicalBytes()
        {
            if (_rawUnionArrayElementBytes.Count == 0)
            {
                return BigInteger.Zero;
            }

            ChargeRepeatRootListHeader();
            var bytes = new BigInteger(16);
            foreach (var (key, state) in _rawUnionArrayElementBytes)
            {
                ChargeRepeatRoot(key.ArrayPrefix);
                var pathLength = key.ArrayPrefix.ProjectionPath.Segments.Length;
                bytes += 32;
                if (pathLength != 0)
                {
                    bytes += 16 + (16 * pathLength);
                }
                bytes += state.LogicalBytes();
            }
            return bytes;
        }

        private void ChargeRepeatRootLookup(RawUnionArrayStateKey target)
        {
            if (_rawUnionArrayElementBytes.Count == 0)
            {
                RepeatResources.ConsumeWork();
                return;
            }

            ChargeRepeatRootListHeader();
            foreach (var (key, _) in _rawUnionArrayElementBytes)
            {
                ChargeRepeatRoot(key.ArrayPrefix);
                RepeatResources.ConsumeWork();
                if (key == target)
                {
                    return;
                }
            }
            RepeatResources.ConsumeWork();
        }

        internal InitializationState Clone()
            => Clone(_repeatSnapshotScope);

        internal void SetRepeatSnapshotScope(
            RepeatSnapshotReservationScope? snapshotScope)
            => _repeatSnapshotScope = snapshotScope;

        internal BigInteger GetRawUnionArrayStateBytes()
            => RawUnionArrayLogicalBytes();

        private void UpdateRepeatSnapshotStateLease()
            => _repeatSnapshotScope?.UpdateState(this,
                RawUnionArrayLogicalBytes());

        internal InitializationState Clone(
            RepeatSnapshotReservationScope? snapshotScope)
        {
            var repeatStateBytes = RawUnionArrayLogicalBytes();
            using var repeatFrame = repeatStateBytes.IsZero
                ? null : RepeatResources.EnterFrame();
            using var repeatInputState
                = snapshotScope?.TracksState(this) == true
                    || _repeatSnapshotScope?.TracksState(this) == true
                    ? null : RepeatResources.ReserveState(repeatStateBytes);
            var repeatOutputState
                = RepeatResources.ReserveState(BigInteger.Zero);
            var repeatOutputStateTransferred = false;
            try
            {
                var clonedDependencies = Dependencies?.Clone();

                var result = new InitializationState(Program, Length,
                    _canBeFacts, _flowFacts, _completionShapeFacts,
                    _completionFacts, _nativeOutputFlowFacts,
                    HasNativeOutputContracts, HasDoesNotReturnFunctions,
                    _fieldGenerationContext,
                    new Dictionary<FieldGenerationKey, long>(
                        _fieldPlaceGenerations),
                    new Dictionary<NestedOwnerStateKey,
                        NestedOwnerFieldState>(_nestedOwnerStates)) {
                    StatementInputs = StatementInputs,
                    StatementInputSnapshotScope
                        = StatementInputSnapshotScope,
                    Dependencies = clonedDependencies,
                    NativePointerCaptureEffects
                        = NativePointerCaptureEffects,
                    RawUnionReturnCoverage = RawUnionReturnCoverage,
                    RawUnionReturnCoverageSummaries
                        = RawUnionReturnCoverageSummaries,
                    RepeatResources = RepeatResources,
                    ScopeActivation = ScopeActivation,
                };
                result._activeStorageScopes.AddRange(_activeStorageScopes);
                Array.Copy(_possibilities, result._possibilities, Length);
                Array.Copy(_possibleFieldMasks, result._possibleFieldMasks,
                    Length);
                Array.Copy(_nativeOutputDestinations,
                    result._nativeOutputDestinations, Length);
                Array.Copy(_nativeOutputResults,
                    result._nativeOutputResults, Length);
                result._nativeOutputStateCount = _nativeOutputStateCount;
                foreach (var (variable, bytes)
                    in _rawUnionInitializedBytes)
                {
                    result._rawUnionInitializedBytes.Add(variable,
                        (byte[])bytes.Clone());
                }
                if (_rawUnionArrayElementBytes.Count != 0)
                {
                    ChargeRepeatRootListHeader();
                    repeatOutputState.Add(16);
                    foreach (var (key, state)
                        in _rawUnionArrayElementBytes)
                    {
                        ChargeRepeatRoot(key.ArrayPrefix);
                        repeatOutputState.Add(
                            RawUnionArrayRootLogicalBytes(
                                key.ArrayPrefix));
                        var clonedState = state.CloneTo(
                            repeatOutputState);
                        result._rawUnionArrayElementBytes.Add(key,
                            clonedState);
                    }
                }

                repeatFrame?.Complete();
                if (snapshotScope is not null)
                {
                    snapshotScope.TrackState(result, repeatOutputState);
                    repeatOutputStateTransferred = true;
                }
                return result;
            }
            finally
            {
                if (!repeatOutputStateTransferred)
                {
                    repeatOutputState.Dispose();
                }
            }
        }

        internal void CopyTo(InitializationState destination)
        {
            if (!ReferenceEquals(RepeatResources, destination.RepeatResources))
            {
                throw Invalid();
            }
            var sourceRepeatBytes = RawUnionArrayLogicalBytes();
            var destinationRepeatBytes
                = destination.RawUnionArrayLogicalBytes();
            using var repeatFrame = sourceRepeatBytes.IsZero
                    && destinationRepeatBytes.IsZero
                ? null : RepeatResources.EnterFrame();
            using var repeatSourceState = RepeatResources.ReserveState(
                sourceRepeatBytes);
            using var repeatDestinationState = RepeatResources.ReserveState(
                destinationRepeatBytes);
            using var repeatOutputState = RepeatResources.ReserveState(
                BigInteger.Zero);
            if (_rawUnionArrayElementBytes.Count != 0)
            {
                ChargeRepeatRootListHeader();
                repeatOutputState.Add(16);
            }
            var rawUnionArrayReplacement
                = new SortedDictionary<RawUnionArrayStateKey,
                    RawUnionArrayElementState>(
                        RawUnionArrayStateKeyComparer.Instance);
            if (_rawUnionArrayElementBytes.Count != 0)
            {
                foreach (var (key, state) in _rawUnionArrayElementBytes)
                {
                    ChargeRepeatRoot(key.ArrayPrefix);
                    repeatOutputState.Add(
                        RawUnionArrayRootLogicalBytes(key.ArrayPrefix));
                    var clonedState = state.CloneTo(repeatOutputState);
                    rawUnionArrayReplacement.Add(key, clonedState);
                }
            }
            repeatFrame?.Complete();
            Array.Copy(_possibilities, destination._possibilities, Length);
            Array.Copy(_possibleFieldMasks, destination._possibleFieldMasks, Length);
            Array.Copy(_nativeOutputDestinations, destination._nativeOutputDestinations, Length);
            Array.Copy(_nativeOutputResults, destination._nativeOutputResults, Length);
            destination._fieldGenerationContext = _fieldGenerationContext;
            destination._fieldPlaceGenerations = new Dictionary<FieldGenerationKey, long>(
                _fieldPlaceGenerations);
            destination._nestedOwnerStates =
                new Dictionary<NestedOwnerStateKey, NestedOwnerFieldState>(_nestedOwnerStates);
            destination._rawUnionInitializedBytes.Clear();
            foreach (var (variable, bytes) in _rawUnionInitializedBytes)
            {
                destination._rawUnionInitializedBytes.Add(variable,
                    (byte[])bytes.Clone());
            }
            destination._rawUnionArrayElementBytes = rawUnionArrayReplacement;
            destination.UpdateRepeatSnapshotStateLease();
            destination._nativeOutputStateCount = _nativeOutputStateCount;
            destination.ScopeActivation = ScopeActivation;
            destination._activeStorageScopes.Clear();
            destination._activeStorageScopes.AddRange(_activeStorageScopes);
            if (Dependencies is not null && destination.Dependencies is not null)
            {
                Dependencies.CopyTo(destination.Dependencies);
            }
            else if (Dependencies is not null || destination.Dependencies is not null)
            {
                throw Invalid();
            }
        }

        internal void MarkAssigned(int index)
        {
            _possibilities[index] = 2;
            SetNativeOutputState(index, null, null);
        }

        internal void InitializeRawUnionPayloadFacts(int variable,
            byte[] initializedBytes)
        {
            ArgumentNullException.ThrowIfNull(initializedBytes);
            if ((uint)variable >= (uint)Length
                || initializedBytes.Length == 0
                || _rawUnionInitializedBytes.ContainsKey(variable))
            {
                throw Invalid();
            }

            _rawUnionInitializedBytes.Add(variable,
                (byte[])initializedBytes.Clone());
        }

        internal byte[] GetRawUnionInitializedBytes(int variable)
        {
            if (!TryGetRawUnionInitializedBytes(variable,
                    out var initializedBytes))
            {
                throw Invalid();
            }

            return initializedBytes;
        }

        internal bool TryGetRawUnionInitializedBytes(int variable,
            out byte[] initializedBytes)
        {
            if (_rawUnionInitializedBytes.TryGetValue(variable,
                    out var storedBytes))
            {
                initializedBytes = (byte[])storedBytes.Clone();
                return true;
            }

            initializedBytes = [];
            return false;
        }

        internal void SetRawUnionInitializedBytes(int variable,
            ReadOnlySpan<byte> initializedBytes)
        {
            if (!_rawUnionInitializedBytes.TryGetValue(variable,
                    out var currentBytes)
                || currentBytes.Length != initializedBytes.Length)
            {
                throw Invalid();
            }

            _rawUnionInitializedBytes[variable] = initializedBytes.ToArray();
        }

        internal void ReplaceRawUnionPayloadRange(int variable,
            int offset, ReadOnlySpan<byte> initializedBytes)
        {
            if (!_rawUnionInitializedBytes.TryGetValue(variable,
                    out var currentBytes)
                || offset < 0 || offset > currentBytes.Length
                || initializedBytes.Length > currentBytes.Length - offset)
            {
                throw Invalid();
            }

            Array.Clear(currentBytes, offset, initializedBytes.Length);
            for (var index = 0; index < initializedBytes.Length; index++)
            {
                currentBytes[offset + index] = initializedBytes[index];
            }
        }

        internal void ClearRawUnionPayloadFacts(int variable)
        {
            if (_rawUnionInitializedBytes.TryGetValue(variable,
                    out var initializedBytes))
            {
                Array.Clear(initializedBytes);
            }
            ClearRawUnionArrayFacts(variable);
        }

        internal void ClearRawUnionArrayFacts(int variable)
            => ClearRawUnionArrayFacts(variable,
                StorageProjection.WholeObject);

        internal void ClearRawUnionArrayFacts(int variable,
            StorageProjection prefix)
        {
            if ((uint)variable >= (uint)Length)
            {
                throw Invalid();
            }

            var inputBytes = RawUnionArrayLogicalBytes();
            using var repeatFrame = RepeatResources.EnterFrame();
            using var inputState
                = RepeatResources.ReserveState(inputBytes);
            using var outputState
                = RepeatResources.ReserveState(BigInteger.Zero);
            var retained = new SortedDictionary<RawUnionArrayStateKey,
                RawUnionArrayElementState>(
                    RawUnionArrayStateKeyComparer.Instance);
            var outputHeaderWritten = false;
            foreach (var (key, state) in _rawUnionArrayElementBytes)
            {
                ChargeRepeatRoot(key.ArrayPrefix);
                if (key.Variable != variable
                    || !prefix.ProjectionPath.IsPrefixOf(
                        key.ArrayPrefix.ProjectionPath))
                {
                    if (!outputHeaderWritten)
                    {
                        ChargeRepeatRootListHeader();
                        outputState.Add(16);
                        outputHeaderWritten = true;
                    }
                    ChargeRepeatRoot(key.ArrayPrefix);
                    outputState.Add(RawUnionArrayRootLogicalBytes(
                        key.ArrayPrefix));
                    retained.Add(key, state.CloneTo(outputState));
                }
                else
                {
                    RepeatResources.ConsumeWork();
                }
            }
            repeatFrame.Complete();
            _rawUnionArrayElementBytes = retained;
            UpdateRepeatSnapshotStateLease();
        }

        internal bool CopyRawUnionArrayFacts(int destination,
            int source)
            => CopyRawUnionArrayFacts(destination,
                StorageProjection.WholeObject, source,
                StorageProjection.WholeObject);

        internal bool CopyRawUnionArrayFacts(int destination,
            StorageProjection destinationPrefix, int source,
            StorageProjection sourcePrefix)
        {
            if ((uint)destination >= (uint)Length
                || (uint)source >= (uint)Length)
            {
                throw Invalid();
            }
            if (destination == source
                && destinationPrefix == sourcePrefix)
            {
                return false;
            }

            var inputBytes = RawUnionArrayLogicalBytes();
            using var repeatFrame = RepeatResources.EnterFrame();
            using var repeatInputState
                = RepeatResources.ReserveState(inputBytes);
            using var repeatOutputState
                = RepeatResources.ReserveState(BigInteger.Zero);
            var replacement = new SortedDictionary<RawUnionArrayStateKey,
                RawUnionArrayElementState>(
                    RawUnionArrayStateKeyComparer.Instance);
            var outputHeaderWritten = false;

            var leftEnumerator = _rawUnionArrayElementBytes.GetEnumerator();
            var rightEnumerator = _rawUnionArrayElementBytes.GetEnumerator();
            var hasLeft = false;
            var hasRight = false;
            var leftKey = default(RawUnionArrayStateKey);
            var rightKey = default(RawUnionArrayStateKey);
            RawUnionArrayElementState? leftState = null;
            RawUnionArrayElementState? rightState = null;

            void AdvanceLeft()
            {
                hasLeft = false;
                leftState = null;
                while (true)
                {
                    if (!leftEnumerator.MoveNext())
                    {
                        RepeatResources.ConsumeWork();
                        return;
                    }

                    RepeatResources.ConsumeWork();
                    var candidateKey = leftEnumerator.Current.Key;
                    ChargeRepeatRoot(candidateKey.ArrayPrefix);
                    RepeatResources.ConsumeWork(2);
                    if (candidateKey.Variable == destination
                        && destinationPrefix.ProjectionPath.IsPrefixOf(
                            candidateKey.ArrayPrefix.ProjectionPath))
                    {
                        continue;
                    }

                    leftKey = candidateKey;
                    leftState = leftEnumerator.Current.Value;
                    hasLeft = true;
                    return;
                }
            }

            void AdvanceRight()
            {
                hasRight = false;
                rightState = null;
                while (true)
                {
                    if (!rightEnumerator.MoveNext())
                    {
                        RepeatResources.ConsumeWork();
                        return;
                    }

                    RepeatResources.ConsumeWork();
                    var candidateKey = rightEnumerator.Current.Key;
                    ChargeRepeatRoot(candidateKey.ArrayPrefix);
                    RepeatResources.ConsumeWork(2);
                    if (candidateKey.Variable != source
                        || !sourcePrefix.ProjectionPath.IsPrefixOf(
                            candidateKey.ArrayPrefix.ProjectionPath))
                    {
                        continue;
                    }

                    var prefix = StorageProjection.Rebase(
                        destinationPrefix, sourcePrefix,
                        candidateKey.ArrayPrefix);
                    if (prefix.Kind
                        == StorageProjectionKind.UnknownElement)
                    {
                        throw Invalid();
                    }
                    rightKey = new RawUnionArrayStateKey(destination, prefix);
                    rightState = rightEnumerator.Current.Value;
                    hasRight = true;
                    return;
                }
            }

            void AppendOutput(RawUnionArrayStateKey key,
                RawUnionArrayElementState state)
            {
                if (!outputHeaderWritten)
                {
                    ChargeRepeatRootListHeader();
                    repeatOutputState.Add(16);
                    outputHeaderWritten = true;
                }
                ChargeRepeatRoot(key.ArrayPrefix);
                repeatOutputState.Add(RawUnionArrayRootLogicalBytes(
                    key.ArrayPrefix));
                var output = state.CloneTo(repeatOutputState);
                if (!replacement.TryAdd(key, output))
                {
                    throw Invalid();
                }
            }

            AdvanceLeft();
            AdvanceRight();
            while (hasLeft || hasRight)
            {
                var comparison = 0;
                if (hasLeft && hasRight)
                {
                    RepeatResources.ConsumeWork();
                    comparison = RawUnionArrayStateKeyComparer.Instance
                        .Compare(leftKey, rightKey);
                    if (comparison == 0)
                    {
                        throw Invalid();
                    }
                }

                if (!hasRight || (hasLeft && comparison < 0))
                {
                    AppendOutput(leftKey, leftState ?? throw Invalid());
                    AdvanceLeft();
                }
                else
                {
                    AppendOutput(rightKey, rightState ?? throw Invalid());
                    AdvanceRight();
                }
            }
            var changed = !RawUnionArrayFactPrefixEquals(
                _rawUnionArrayElementBytes, replacement, destination,
                destinationPrefix);
            repeatFrame.Complete();
            _rawUnionArrayElementBytes = replacement;
            UpdateRepeatSnapshotStateLease();
            return changed;
        }

        internal SortedDictionary<StorageProjection,
            RawUnionArrayElementState> SnapshotRawUnionArrayFacts(
            int variable, StorageProjection prefix,
            ref int remaining, int depth,
            out RepeatResourceLedger.StateLease? outputState)
        {
            outputState = null;
            Count(ref remaining, depth);
            if ((uint)variable >= (uint)Length)
            {
                throw Invalid();
            }

            using var repeatFrame = RepeatResources.EnterFrame();
            using var inputState = RepeatResources.ReserveState(
                RawUnionArrayLogicalBytes());
            var snapshotState = RepeatResources.ReserveState(
                BigInteger.Zero);
            var snapshotStateTransferred = false;
            try
            {
                var result = new SortedDictionary<StorageProjection,
                    RawUnionArrayElementState>(
                        StorageProjectionComparer.Instance);
                var outputHeaderWritten = false;
                foreach (var (key, state) in _rawUnionArrayElementBytes)
                {
                    Count(ref remaining, depth + 1);
                    ChargeRepeatRoot(key.ArrayPrefix);
                    RepeatResources.ConsumeWork();
                    if (key.Variable != variable
                        || !prefix.ProjectionPath.IsPrefixOf(
                            key.ArrayPrefix.ProjectionPath))
                    {
                        continue;
                    }

                    var relativePrefix = StorageProjection.Rebase(
                        StorageProjection.WholeObject, prefix,
                        key.ArrayPrefix);
                    if (relativePrefix.Kind
                        == StorageProjectionKind.UnknownElement)
                    {
                        throw Invalid();
                    }
                    var rootBytes
                        = RawUnionArrayRootLogicalBytes(relativePrefix);
                    if (!outputHeaderWritten)
                    {
                        ChargeRepeatRootListHeader();
                        snapshotState.Add(16);
                        outputHeaderWritten = true;
                    }
                    ChargeRepeatRoot(relativePrefix);
                    snapshotState.Add(rootBytes);
                    var snapshot = state.CloneTo(snapshotState);
                    if (!result.TryAdd(relativePrefix, snapshot))
                    {
                        throw Invalid();
                    }
                }

                if (result.Count == 0)
                {
                    repeatFrame.Complete();
                    return result;
                }

                repeatFrame.Complete();
                outputState = snapshotState;
                snapshotStateTransferred = true;
                return result;
            }
            finally
            {
                if (!snapshotStateTransferred)
                {
                    snapshotState.Dispose();
                }
            }
        }

        private bool TryGetNextRawUnionArraySnapshot(
            ref SortedDictionary<StorageProjection,
                RawUnionArrayElementState>.Enumerator snapshots,
            ref int remaining, int depth, int variable,
            StorageProjection destinationPrefix,
            RepeatResourceLedger.StateLease snapshotInputs,
            out RawUnionArrayStateKey key,
            out RawUnionArrayElementState state)
        {
            if (!snapshots.MoveNext())
            {
                RepeatResources.ConsumeWork();
                key = default;
                state = null!;
                return false;
            }

            RepeatResources.ConsumeWork();
            Count(ref remaining, depth + 1);
            var relativePrefix = snapshots.Current.Key;
            state = snapshots.Current.Value;
            if (state is null
                || !NativeFixedArrayContract.MatchesLayout(state.Layout,
                    Program)
                || !CarriesRawUnionPayloadFacts(
                    Program, state.Layout.ElementType))
            {
                throw Invalid();
            }

            ChargeRepeatRoot(relativePrefix);
            var arrayPrefix = StorageProjection.Rebase(
                destinationPrefix, StorageProjection.WholeObject,
                relativePrefix);
            if (arrayPrefix.Kind == StorageProjectionKind.UnknownElement)
            {
                throw Invalid();
            }

            key = new RawUnionArrayStateKey(variable, arrayPrefix);
            snapshotInputs.Add(RawUnionArrayRootLogicalBytes(relativePrefix)
                + state.LogicalBytes());
            return true;
        }

        internal bool SetRawUnionArrayFacts(int variable,
            StorageProjection destinationPrefix,
            SortedDictionary<StorageProjection,
                RawUnionArrayElementState> snapshots,
            ref int remaining, int depth)
        {
            Count(ref remaining, depth);
            if ((uint)variable >= (uint)Length || snapshots is null)
            {
                throw Invalid();
            }

            var inputBytes = RawUnionArrayLogicalBytes();
            using var inputState
                = RepeatResources.ReserveState(inputBytes);
            using var snapshotInputs
                = RepeatResources.ReserveState(BigInteger.Zero);
            if (snapshots.Count != 0)
            {
                ChargeRepeatRootListHeader();
                snapshotInputs.Add(16);
            }
            using var outputState
                = RepeatResources.ReserveState(BigInteger.Zero);
            var replacement = new SortedDictionary<RawUnionArrayStateKey,
                RawUnionArrayElementState>(
                    RawUnionArrayStateKeyComparer.Instance);
            var outputHeaderWritten = false;
            var leftEnumerator = _rawUnionArrayElementBytes.GetEnumerator();
            var rightEnumerator = snapshots.GetEnumerator();
            var hasLeft = false;
            var leftKey = default(RawUnionArrayStateKey);
            RawUnionArrayElementState? leftState = null;

            void AdvanceLeft()
            {
                hasLeft = false;
                leftState = null;
                while (true)
                {
                    if (!leftEnumerator.MoveNext())
                    {
                        RepeatResources.ConsumeWork();
                        return;
                    }

                    RepeatResources.ConsumeWork();
                    var candidateKey = leftEnumerator.Current.Key;
                    ChargeRepeatRoot(candidateKey.ArrayPrefix);
                    RepeatResources.ConsumeWork(2);
                    if (candidateKey.Variable == variable
                        && destinationPrefix.ProjectionPath.IsPrefixOf(
                            candidateKey.ArrayPrefix.ProjectionPath))
                    {
                        continue;
                    }

                    leftKey = candidateKey;
                    leftState = leftEnumerator.Current.Value;
                    hasLeft = true;
                    return;
                }
            }

            void AppendOutput(RawUnionArrayStateKey key,
                RawUnionArrayElementState state)
            {
                if (!outputHeaderWritten)
                {
                    ChargeRepeatRootListHeader();
                    outputState.Add(16);
                    outputHeaderWritten = true;
                }

                ChargeRepeatRoot(key.ArrayPrefix);
                outputState.Add(RawUnionArrayRootLogicalBytes(
                    key.ArrayPrefix));
                var output = state.CloneTo(outputState);
                if (!replacement.TryAdd(key, output))
                {
                    throw Invalid();
                }
            }

            AdvanceLeft();
            var hasRight = TryGetNextRawUnionArraySnapshot(ref rightEnumerator,
                ref remaining, depth, variable, destinationPrefix,
                snapshotInputs, out var rightKey, out var rightState);
            while (hasLeft || hasRight)
            {
                var comparison = 0;
                if (hasLeft && hasRight)
                {
                    RepeatResources.ConsumeWork();
                    comparison = RawUnionArrayStateKeyComparer.Instance
                        .Compare(leftKey, rightKey);
                    if (comparison == 0)
                    {
                        throw Invalid();
                    }
                }

                if (!hasRight || (hasLeft && comparison < 0))
                {
                    AppendOutput(leftKey, leftState ?? throw Invalid());
                    AdvanceLeft();
                }
                else
                {
                    AppendOutput(rightKey, rightState ?? throw Invalid());
                    hasRight = TryGetNextRawUnionArraySnapshot(
                        ref rightEnumerator, ref remaining, depth, variable,
                        destinationPrefix, snapshotInputs, out rightKey,
                        out rightState);
                }
            }

            var changed = !RawUnionArrayFactPrefixEquals(
                _rawUnionArrayElementBytes, replacement, variable,
                destinationPrefix);
            _rawUnionArrayElementBytes = replacement;
            UpdateRepeatSnapshotStateLease();
            return changed;
        }

        internal void SetRawUnionArrayElementBytes(int variable,
            StorageProjection arrayPrefix,
            BoundNativeFixedArrayLayout layout, ulong? elementIndex,
            ReadOnlySpan<byte> initializedBytes)
        {
            if ((uint)variable >= (uint)Length
                || !NativeFixedArrayContract.MatchesLayout(layout, Program)
                || !CarriesRawUnionPayloadFacts(Program,
                    layout.ElementType))
            {
                throw Invalid();
            }

            var defaultBytes = GuaranteedInitializedPayloadBytes(
                layout.ElementType, Program);
            if (initializedBytes.Length != defaultBytes.Length)
            {
                throw Invalid();
            }

            var key = new RawUnionArrayStateKey(variable, arrayPrefix);
            ChargeRepeatRootLookup(key);
            var hasExactState = _rawUnionArrayElementBytes.TryGetValue(
                key, out var currentState);
            if (arrayPrefix.Kind == StorageProjectionKind.WholeObject
                || arrayPrefix.ProjectionPath.IsConcrete
                || hasExactState)
            {
                if (hasExactState && elementIndex is ulong exactIndex)
                {
                    var existingState = currentState ?? throw Invalid();
                    if (existingState.Layout != layout)
                    {
                        throw Invalid();
                    }
                    _ = ReplaceRawUnionArrayElementState(key,
                        existingState, exactIndex, initializedBytes);
                    return;
                }

                RawUnionArrayElementState? templateState = null;
                var hasTemplateState = !hasExactState
                    && TryGetRawUnionArrayTemplateState(variable,
                        arrayPrefix, layout, out templateState);
                if (!hasExactState && elementIndex is ulong selectedIndex)
                {
                    _ = InsertRawUnionArrayElementState(key, layout,
                        defaultBytes, selectedIndex, initializedBytes,
                        hasTemplateState
                            ? templateState ?? throw Invalid() : null);
                    return;
                }

                RawUnionArrayElementState stagedState;
                if (hasExactState)
                {
                    var existingState = currentState ?? throw Invalid();
                    if (existingState.Layout != layout)
                    {
                        throw Invalid();
                    }
                    stagedState = existingState.Clone();
                }
                else if (hasTemplateState)
                {
                    stagedState = templateState ?? throw Invalid();
                }
                else
                {
                    stagedState = new RawUnionArrayElementState(layout,
                        defaultBytes, RepeatResources);
                }

                if (stagedState.Layout != layout)
                {
                    throw Invalid();
                }

                var changed = elementIndex is ulong index
                    ? stagedState.Set(index, initializedBytes)
                    : stagedState.ApplyUnknownIndexWrite(initializedBytes);
                if (!changed)
                {
                    return;
                }

                ChargeRepeatRootListHeaderForFirstEntry();
                var replacement = CopyRawUnionArrayStateMap();
                replacement[key] = stagedState;
                _rawUnionArrayElementBytes = replacement;
                UpdateRepeatSnapshotStateLease();
                return;
            }

            var updated = CopyRawUnionArrayStateMap();
            var changedAny = false;
            foreach (var (matchedKey, matchedState) in
                _rawUnionArrayElementBytes)
            {
                if (matchedKey.Variable != variable
                    || !arrayPrefix.ProjectionPath.IsPrefixOf(
                        matchedKey.ArrayPrefix.ProjectionPath))
                {
                    continue;
                }
                if (matchedState.Layout != layout)
                {
                    throw Invalid();
                }
                var stagedState = matchedState.Clone();
                var changed = false;
                if (elementIndex is ulong selectedIndex)
                {
                    var mergedBytes = stagedState.Get(selectedIndex);
                    for (var byteIndex = 0;
                        byteIndex < mergedBytes.Length;
                        byteIndex++)
                    {
                        mergedBytes[byteIndex] &= initializedBytes[byteIndex];
                    }
                    changed = stagedState.Set(selectedIndex, mergedBytes);
                }
                else
                {
                    changed = stagedState.ApplyUnknownIndexWrite(
                        initializedBytes);
                }
                if (changed)
                {
                    updated[matchedKey] = stagedState;
                    changedAny = true;
                }
            }
            if (changedAny)
            {
                _rawUnionArrayElementBytes = updated;
                UpdateRepeatSnapshotStateLease();
            }
        }

        private SortedDictionary<RawUnionArrayStateKey,
            RawUnionArrayElementState> CopyRawUnionArrayStateMap()
        {
            var result = new SortedDictionary<RawUnionArrayStateKey,
                RawUnionArrayElementState>(
                    RawUnionArrayStateKeyComparer.Instance);
            if (_rawUnionArrayElementBytes.Count != 0)
            {
                ChargeRepeatRootListHeader();
                ChargeRepeatRootListHeader();
            }
            foreach (var (key, state) in _rawUnionArrayElementBytes)
            {
                ChargeRepeatRoot(key.ArrayPrefix);
                ChargeRepeatRoot(key.ArrayPrefix);
                result.Add(key, state);
            }
            return result;
        }

        private bool ReplaceRawUnionArrayElementState(
            RawUnionArrayStateKey target,
            RawUnionArrayElementState existingState, ulong index,
            ReadOnlySpan<byte> bytes)
        {
            using var repeatFrame = RepeatResources.EnterFrame();
            if (!existingState.TryGetSetPlan(index, bytes,
                out var position, out var hasIndex, out var removesEntry))
            {
                repeatFrame.Complete();
                return false;
            }

            var inputBytes = RawUnionArrayLogicalBytes();
            using var inputState
                = RepeatResources.ReserveState(inputBytes);
            using var outputState
                = RepeatResources.ReserveState(BigInteger.Zero);
            SortedDictionary<RawUnionArrayStateKey,
                RawUnionArrayElementState>? replacement = null;
            var targetWritten = false;
            foreach (var (key, state) in _rawUnionArrayElementBytes)
            {
                ChargeRepeatRoot(key.ArrayPrefix);
                var isTarget = key.Equals(target);
                RepeatResources.ConsumeWork();
                if (replacement is null)
                {
                    ChargeRepeatRootListHeader();
                    outputState.Add(16);
                    replacement = new SortedDictionary<
                        RawUnionArrayStateKey,
                        RawUnionArrayElementState>(
                            RawUnionArrayStateKeyComparer.Instance);
                }

                outputState.Add(RawUnionArrayRootLogicalBytes(
                    key.ArrayPrefix));
                var output = isTarget
                    ? existingState.CreateSet(index, bytes, position,
                        hasIndex, removesEntry, outputState)
                    : state.CloneTo(outputState);
                targetWritten |= isTarget;
                replacement.Add(key, output);
            }

            if (replacement is null || !targetWritten)
            {
                throw Invalid();
            }
            repeatFrame.Complete();
            _rawUnionArrayElementBytes = replacement;
            UpdateRepeatSnapshotStateLease();
            return true;
        }

        private void EnsureRawUnionArrayRootOutputList(
            ref SortedDictionary<RawUnionArrayStateKey,
                RawUnionArrayElementState>? roots,
            ref bool headerWritten,
            RepeatResourceLedger.StateLease outputState)
        {
            if (headerWritten)
            {
                return;
            }

            ChargeRepeatRootListHeader();
            outputState.Add(16);
            roots = new SortedDictionary<RawUnionArrayStateKey,
                RawUnionArrayElementState>(
                    RawUnionArrayStateKeyComparer.Instance);
            headerWritten = true;
        }

        private void AppendRawUnionArrayRootClone(
            ref SortedDictionary<RawUnionArrayStateKey,
                RawUnionArrayElementState>? roots,
            ref bool headerWritten,
            RawUnionArrayStateKey key,
            RawUnionArrayElementState state,
            RepeatResourceLedger.StateLease outputState)
        {
            EnsureRawUnionArrayRootOutputList(ref roots,
                ref headerWritten, outputState);
            ChargeRepeatRoot(key.ArrayPrefix);
            outputState.Add(RawUnionArrayRootLogicalBytes(
                key.ArrayPrefix));
            var clonedState = state.CloneTo(outputState);
            (roots ?? throw Invalid()).Add(key, clonedState);
        }

        private void AppendRawUnionArrayTemplateRoot(
            ref SortedDictionary<RawUnionArrayStateKey,
                RawUnionArrayElementState>? roots,
            ref bool headerWritten,
            RawUnionArrayStateKey key,
            BoundNativeFixedArrayLayout layout,
            byte[] defaultBytes, ReadOnlySpan<byte> templateBytes,
            RepeatResourceLedger.StateLease outputState)
        {
            EnsureRawUnionArrayRootOutputList(ref roots,
                ref headerWritten, outputState);
            ChargeRepeatRoot(key.ArrayPrefix);
            outputState.Add(RawUnionArrayRootLogicalBytes(
                key.ArrayPrefix));
            RepeatResources.ConsumeWork(8);
            outputState.Add(new BigInteger(56) + templateBytes.Length);
            var state = new RawUnionArrayElementState(layout,
                defaultBytes, templateBytes, RepeatResources,
                constructionWorkCharged: true);
            (roots ?? throw Invalid()).Add(key, state);
        }

        private void AppendRawUnionArrayElementRoot(
            ref SortedDictionary<RawUnionArrayStateKey,
                RawUnionArrayElementState>? roots,
            ref bool headerWritten,
            RawUnionArrayStateKey key,
            BoundNativeFixedArrayLayout layout, byte[] defaultBytes,
            ulong index, ReadOnlySpan<byte> bytes,
            RawUnionArrayElementState? inheritedState, int position,
            bool hasIndex, bool removesEntry,
            RepeatResourceLedger.StateLease outputState)
        {
            EnsureRawUnionArrayRootOutputList(ref roots,
                ref headerWritten, outputState);
            ChargeRepeatRoot(key.ArrayPrefix);
            outputState.Add(RawUnionArrayRootLogicalBytes(
                key.ArrayPrefix));
            var state = inheritedState is null
                ? RawUnionArrayElementState.CreateInitializedSet(
                    layout, defaultBytes, index, bytes, RepeatResources,
                    outputState)
                : inheritedState.CreateSet(index, bytes, position,
                    hasIndex, removesEntry, outputState);
            (roots ?? throw Invalid()).Add(key, state);
        }

        private bool InsertRawUnionArrayElementState(
            RawUnionArrayStateKey target,
            BoundNativeFixedArrayLayout layout, byte[] defaultBytes,
            ulong index, ReadOnlySpan<byte> bytes,
            RawUnionArrayElementState? inheritedState = null)
        {
            using var repeatFrame = RepeatResources.EnterFrame();
            var position = 0;
            var hasIndex = false;
            var removesEntry = false;
            if (inheritedState is null)
            {
                if (PayloadBytesEqual(defaultBytes, bytes))
                {
                    repeatFrame.Complete();
                    return false;
                }
            }
            else if (!inheritedState.TryGetSetPlan(index, bytes,
                out position, out hasIndex, out removesEntry))
            {
                repeatFrame.Complete();
                return false;
            }

            var inputBytes = RawUnionArrayLogicalBytes();
            using var inputState
                = RepeatResources.ReserveState(inputBytes);
            using var inheritedInputState = inheritedState is null
                ? null
                : RepeatResources.ReserveState(
                    inheritedState.LogicalBytes());
            using var outputState
                = RepeatResources.ReserveState(BigInteger.Zero);
            SortedDictionary<RawUnionArrayStateKey,
                RawUnionArrayElementState>? replacement = null;
            var headerWritten = false;
            var targetWritten = false;
            foreach (var (key, state) in _rawUnionArrayElementBytes)
            {
                RepeatResources.ConsumeWork();
                var comparison = RawUnionArrayStateKeyComparer.Instance
                    .Compare(key, target);
                if (comparison == 0)
                {
                    throw Invalid();
                }
                if (comparison > 0 && !targetWritten)
                {
                    AppendRawUnionArrayElementRoot(ref replacement,
                        ref headerWritten, target, layout, defaultBytes,
                        index, bytes, inheritedState, position, hasIndex,
                        removesEntry, outputState);
                    targetWritten = true;
                }
                AppendRawUnionArrayRootClone(ref replacement,
                    ref headerWritten, key, state, outputState);
            }

            if (!targetWritten)
            {
                AppendRawUnionArrayElementRoot(ref replacement,
                    ref headerWritten, target, layout, defaultBytes,
                    index, bytes, inheritedState, position, hasIndex,
                    removesEntry, outputState);
            }
            var completedReplacement = replacement ?? throw Invalid();
            repeatFrame.Complete();
            _rawUnionArrayElementBytes = completedReplacement;
            UpdateRepeatSnapshotStateLease();
            return true;
        }

        private bool PayloadBytesEqual(
            ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            if (left.Length != right.Length)
            {
                throw Invalid();
            }
            for (var index = 0; index < left.Length; index++)
            {
                RepeatResources.ConsumeWork(5);
                if (left[index] != right[index])
                {
                    return false;
                }
            }
            return true;
        }

        internal void SetRawUnionArrayTemplateBytes(int variable,
            StorageProjection arrayPrefix,
            BoundNativeFixedArrayLayout layout,
            ReadOnlySpan<byte> initializedBytes)
        {
            if ((uint)variable >= (uint)Length
                || !NativeFixedArrayContract.MatchesLayout(layout, Program)
                || !CarriesRawUnionPayloadFacts(Program,
                    layout.ElementType))
            {
                throw Invalid();
            }

            var defaultBytes = GuaranteedInitializedPayloadBytes(
                layout.ElementType, Program);
            if (initializedBytes.Length != defaultBytes.Length)
            {
                throw Invalid();
            }

            var key = new RawUnionArrayStateKey(variable, arrayPrefix);
            ChargeRepeatRootLookup(key);
            var hasState = _rawUnionArrayElementBytes.TryGetValue(key,
                out var currentState);
            if (hasState)
            {
                var existingState = currentState ?? throw Invalid();
                if (existingState.Layout != layout)
                {
                    throw Invalid();
                }
                if (!existingState.HasTemplateChange(initializedBytes))
                {
                    return;
                }

                var inputMapBytes = RawUnionArrayLogicalBytes();
                using var repeatInputState
                    = RepeatResources.ReserveState(inputMapBytes);
                using var repeatOutputState
                    = RepeatResources.ReserveState(BigInteger.Zero);
                SortedDictionary<RawUnionArrayStateKey,
                    RawUnionArrayElementState>? replacement = null;
                var headerWritten = false;
                var targetWritten = false;
                foreach (var (candidateKey, state) in
                    _rawUnionArrayElementBytes)
                {
                    ChargeRepeatRoot(candidateKey.ArrayPrefix);
                    var isTarget = candidateKey.Equals(key);
                    RepeatResources.ConsumeWork();
                    EnsureRawUnionArrayRootOutputList(ref replacement,
                        ref headerWritten, repeatOutputState);
                    repeatOutputState.Add(RawUnionArrayRootLogicalBytes(
                        candidateKey.ArrayPrefix));
                    var output = isTarget
                        ? existingState.CreateSetTemplate(initializedBytes,
                            repeatOutputState)
                        : state.CloneTo(repeatOutputState);
                    replacement!.Add(candidateKey, output);
                    targetWritten |= isTarget;
                }

                if (replacement is null || !targetWritten)
                {
                    throw Invalid();
                }
                _rawUnionArrayElementBytes = replacement;
                UpdateRepeatSnapshotStateLease();
                return;
            }

            if (!PayloadBytesEqual(defaultBytes, initializedBytes))
            {
                var inputMapBytes = RawUnionArrayLogicalBytes();
                using var repeatInputState
                    = RepeatResources.ReserveState(inputMapBytes);
                using var repeatOutputState
                    = RepeatResources.ReserveState(BigInteger.Zero);
                SortedDictionary<RawUnionArrayStateKey,
                    RawUnionArrayElementState>? replacement = null;
                var headerWritten = false;
                var targetWritten = false;
                foreach (var (candidateKey, state) in
                    _rawUnionArrayElementBytes)
                {
                    RepeatResources.ConsumeWork();
                    var comparison = RawUnionArrayStateKeyComparer.Instance
                        .Compare(candidateKey, key);
                    if (comparison == 0)
                    {
                        throw Invalid();
                    }
                    if (comparison > 0 && !targetWritten)
                    {
                        AppendRawUnionArrayTemplateRoot(ref replacement,
                            ref headerWritten, key, layout, defaultBytes,
                            initializedBytes, repeatOutputState);
                        targetWritten = true;
                    }

                    AppendRawUnionArrayRootClone(ref replacement,
                        ref headerWritten, candidateKey, state,
                        repeatOutputState);
                }

                if (!targetWritten)
                {
                    AppendRawUnionArrayTemplateRoot(ref replacement,
                        ref headerWritten, key, layout, defaultBytes,
                        initializedBytes, repeatOutputState);
                }
                _rawUnionArrayElementBytes = replacement ?? throw Invalid();
                UpdateRepeatSnapshotStateLease();
                return;
            }
        }

        internal void WriteRawUnionArrayElementByteRange(int variable,
            StorageProjection arrayPrefix,
            BoundNativeFixedArrayLayout layout, ulong? elementIndex,
            int offset, ReadOnlySpan<byte> initializedBytes)
        {
            if ((uint)variable >= (uint)Length
                || !NativeFixedArrayContract.MatchesLayout(layout, Program)
                || !CarriesRawUnionPayloadFacts(Program,
                    layout.ElementType))
            {
                throw Invalid();
            }

            var defaultBytes = GuaranteedInitializedPayloadBytes(
                layout.ElementType, Program);
            if (offset < 0 || offset > defaultBytes.Length
                || initializedBytes.Length > defaultBytes.Length - offset)
            {
                throw Invalid();
            }

            var key = new RawUnionArrayStateKey(variable, arrayPrefix);
            ChargeRepeatRootLookup(key);
            var locationIsExact
                = arrayPrefix.Kind == StorageProjectionKind.WholeObject
                    || arrayPrefix.ProjectionPath.IsConcrete
                    || arrayPrefix.ProjectionPath.ExpectedMatchCount == 1;
            var hasState = _rawUnionArrayElementBytes.TryGetValue(key,
                out var currentState);
            if (hasState && elementIndex is ulong exactIndex
                && locationIsExact)
            {
                var existingState = currentState ?? throw Invalid();
                if (existingState.Layout != layout)
                {
                    throw Invalid();
                }

                using var rangeScratch
                    = RepeatResources.ReserveState(
                        new BigInteger(defaultBytes.Length));
                var elementBytes = existingState.Get(exactIndex);
                for (var byteIndex = 0;
                    byteIndex < initializedBytes.Length;
                    byteIndex++)
                {
                    RepeatResources.ConsumeWork(4);
                    elementBytes[offset + byteIndex]
                        = initializedBytes[byteIndex];
                }
                _ = ReplaceRawUnionArrayElementState(key, existingState,
                    exactIndex, elementBytes);
                return;
            }

            if (!hasState && !locationIsExact)
            {
                var replacement = CopyRawUnionArrayStateMap();
                var changedAny = false;
                var matchedAny = false;
                foreach (var (matchedKey, matchedState) in
                    _rawUnionArrayElementBytes)
                {
                    if (matchedKey.Variable != variable
                        || !arrayPrefix.ProjectionPath.IsPrefixOf(
                            matchedKey.ArrayPrefix.ProjectionPath))
                    {
                        continue;
                    }
                    matchedAny = true;
                    if (matchedState.Layout != layout)
                    {
                        throw Invalid();
                    }

                    var stagedState = matchedState.Clone();
                    var changed = elementIndex is ulong selectedIndex
                        ? stagedState.ApplyPossibleExactRangeWrite(
                            selectedIndex, offset, initializedBytes)
                        : stagedState.ApplyPossibleUnknownRangeWrite(
                            offset, initializedBytes);
                    if (changed)
                    {
                        replacement[matchedKey] = stagedState;
                        changedAny = true;
                    }
                }
                if (changedAny)
                {
                    _rawUnionArrayElementBytes = replacement;
                    UpdateRepeatSnapshotStateLease();
                    return;
                }
                if (matchedAny)
                {
                    return;
                }
            }

            RawUnionArrayElementState state;
            if (hasState)
            {
                var existingState = currentState ?? throw Invalid();
                if (existingState.Layout != layout)
                {
                    throw Invalid();
                }
                state = existingState.Clone();
            }
            else if (TryGetRawUnionArrayTemplateState(variable,
                arrayPrefix, layout, out var templateState))
            {
                state = templateState;
            }
            else
            {
                state = new RawUnionArrayElementState(layout, defaultBytes,
                    RepeatResources);
            }

            var stateChanged = elementIndex is ulong index
                ? locationIsExact
                    ? state.WriteRange(index, offset, initializedBytes)
                    : state.ApplyPossibleExactRangeWrite(index, offset,
                        initializedBytes)
                : locationIsExact
                    ? state.ApplyUnknownRangeWrite(
                        offset, initializedBytes)
                    : state.ApplyPossibleUnknownRangeWrite(
                        offset, initializedBytes);
            if (stateChanged)
            {
                ChargeRepeatRootListHeaderForFirstEntry();
                var replacement = CopyRawUnionArrayStateMap();
                replacement[key] = state;
                _rawUnionArrayElementBytes = replacement;
                UpdateRepeatSnapshotStateLease();
            }
        }

        internal byte[] RawUnionArrayElementBytes(int variable,
            StorageProjection arrayPrefix,
            BoundNativeFixedArrayLayout layout, ulong? elementIndex)
        {
            if ((uint)variable >= (uint)Length
                || !NativeFixedArrayContract.MatchesLayout(layout, Program)
                || !CarriesRawUnionPayloadFacts(Program,
                    layout.ElementType))
            {
                throw Invalid();
            }

            var defaultBytes = GuaranteedInitializedPayloadBytes(
                layout.ElementType, Program);
            using var repeatFrame = RepeatResources.EnterFrame();
            var key = new RawUnionArrayStateKey(variable, arrayPrefix);
            ChargeRepeatRootLookup(key);
            if (_rawUnionArrayElementBytes.TryGetValue(key,
                    out var exactState))
            {
                if (exactState.Layout != layout)
                {
                    throw Invalid();
                }
                var bytes = elementIndex is ulong index
                    ? exactState.Get(index) : exactState.Common();
                repeatFrame.Complete();
                return bytes;
            }

            if (arrayPrefix.Kind == StorageProjectionKind.WholeObject
                || arrayPrefix.ProjectionPath.IsConcrete)
            {
                if (!TryGetRawUnionArrayTemplateState(variable,
                        arrayPrefix, layout, out var templateState))
                {
                    repeatFrame.Complete();
                    return defaultBytes;
                }
                var bytes = elementIndex is ulong index
                    ? templateState.Get(index) : templateState.Common();
                repeatFrame.Complete();
                return bytes;
            }

            var expected = arrayPrefix.ProjectionPath.ExpectedMatchCount;
            RepeatResources.ConsumeWork();
            if (expected == 0)
            {
                repeatFrame.Complete();
                return defaultBytes;
            }

            var matched = 0UL;
            var coversExpectedDomain = false;
            byte[]? commonBytes = null;
            if (_rawUnionArrayElementBytes.Count != 0)
            {
                ChargeRepeatRootListHeader();
            }
            foreach (var (stateKey, state) in _rawUnionArrayElementBytes)
            {
                ChargeRepeatRoot(stateKey.ArrayPrefix);
                RepeatResources.ConsumeWork();
                if (stateKey.Variable != variable
                    || !arrayPrefix.ProjectionPath.IsPrefixOf(
                        stateKey.ArrayPrefix.ProjectionPath))
                {
                    continue;
                }
                if (state.Layout != layout)
                {
                    throw Invalid();
                }
                if (stateKey.ArrayPrefix.Kind
                        == StorageProjectionKind.ElementRange
                    && stateKey.ArrayPrefix.ProjectionPath.Segments.Length
                        == arrayPrefix.ProjectionPath.Segments.Length
                    && arrayPrefix.ProjectionPath.IsPrefixOf(
                        stateKey.ArrayPrefix.ProjectionPath))
                {
                    coversExpectedDomain = true;
                }
                if (!coversExpectedDomain)
                {
                    matched++;
                }
                var elementBytes = elementIndex is ulong index
                    ? state.Get(index) : state.Common();
                if (commonBytes is null)
                {
                    commonBytes = elementBytes;
                    continue;
                }
                for (var byteIndex = 0;
                    byteIndex < commonBytes.Length;
                    byteIndex++)
                {
                    RepeatResources.ConsumeWork(7);
                    commonBytes[byteIndex] = (byte)(commonBytes[byteIndex]
                        & elementBytes[byteIndex]);
                }
            }

            RepeatResources.ConsumeWork();
            var result = (!coversExpectedDomain && matched < expected)
                || commonBytes is null
                ? defaultBytes : commonBytes;
            repeatFrame.Complete();
            return result;
        }

        private bool TryGetRawUnionArrayTemplateState(int variable,
            StorageProjection arrayPrefix,
            BoundNativeFixedArrayLayout layout,
            out RawUnionArrayElementState state)
        {
            state = null!;
            var bestSpecificity = -1;
            if (_rawUnionArrayElementBytes.Count != 0)
            {
                ChargeRepeatRootListHeader();
            }
            foreach (var (key, candidate) in _rawUnionArrayElementBytes)
            {
                ChargeRepeatRoot(key.ArrayPrefix);
                RepeatResources.ConsumeWork();
                if (key.Variable != variable || candidate.Layout != layout
                    || !key.ArrayPrefix.ProjectionPath.IsPrefixOf(
                        arrayPrefix.ProjectionPath))
                {
                    continue;
                }

                var specificity = key.ArrayPrefix.ProjectionPath.Segments.Length;
                if (specificity > bestSpecificity)
                {
                    state = candidate.Clone();
                    bestSpecificity = specificity;
                }
                else if (specificity == bestSpecificity)
                {
                    _ = state.Merge(candidate);
                }
            }
            return bestSpecificity >= 0;
        }

        private bool CopyRawUnionArrayFacts(int variable,
            InitializationState other)
        {
            if (!ReferenceEquals(RepeatResources, other.RepeatResources))
            {
                throw Invalid();
            }
            var incoming = new SortedDictionary<StorageProjection,
                RawUnionArrayElementState>(StorageProjectionComparer.Instance);
            if (other._rawUnionArrayElementBytes.Count != 0)
            {
                RepeatResources.ConsumeWork(2);
            }
            var incomingHeaderWritten = false;
            foreach (var (key, state) in other._rawUnionArrayElementBytes)
            {
                ChargeRepeatRoot(key.ArrayPrefix);
                if (key.Variable == variable)
                {
                    if (!incomingHeaderWritten)
                    {
                        ChargeRepeatRootListHeader();
                        incomingHeaderWritten = true;
                    }
                    ChargeRepeatRoot(key.ArrayPrefix);
                    incoming.Add(key.ArrayPrefix, state.Clone());
                }
            }

            var replacement = new SortedDictionary<RawUnionArrayStateKey,
                RawUnionArrayElementState>(
                    RawUnionArrayStateKeyComparer.Instance);
            if (_rawUnionArrayElementBytes.Count != 0)
            {
                RepeatResources.ConsumeWork(2);
            }
            var outputHeaderWritten = false;
            foreach (var (key, state) in _rawUnionArrayElementBytes)
            {
                ChargeRepeatRoot(key.ArrayPrefix);
                if (key.Variable != variable)
                {
                    if (!outputHeaderWritten)
                    {
                        ChargeRepeatRootListHeader();
                        outputHeaderWritten = true;
                    }
                    ChargeRepeatRoot(key.ArrayPrefix);
                    replacement.Add(key, state);
                }
                else
                {
                    RepeatResources.ConsumeWork();
                }
            }
            foreach (var (prefix, state) in incoming)
            {
                if (!outputHeaderWritten)
                {
                    ChargeRepeatRootListHeader();
                    outputHeaderWritten = true;
                }
                ChargeRepeatRoot(prefix);
                replacement.Add(new RawUnionArrayStateKey(variable,
                    prefix), state);
            }
            var changed = !RawUnionArrayFactPrefixEquals(
                _rawUnionArrayElementBytes, replacement, variable,
                StorageProjection.WholeObject);
            _rawUnionArrayElementBytes = replacement;
            UpdateRepeatSnapshotStateLease();
            return changed;
        }

        private bool MergeRawUnionArrayFacts(int variable,
            InitializationState first, InitializationState second)
        {
            if (!ReferenceEquals(RepeatResources, first.RepeatResources)
                || !ReferenceEquals(RepeatResources, second.RepeatResources))
            {
                throw Invalid();
            }

            if (_rawUnionArrayElementBytes.Count == 0
                && first._rawUnionArrayElementBytes.Count == 0
                && second._rawUnionArrayElementBytes.Count == 0)
            {
                return false;
            }

            using var repeatFrame = RepeatResources.EnterFrame();
            var currentBytes = RawUnionArrayLogicalBytes();
            var firstBytes = first.RawUnionArrayLogicalBytes();
            var secondBytes = second.RawUnionArrayLogicalBytes();
            using var currentState = RepeatResources.ReserveState(currentBytes);
            using var firstState = RepeatResources.ReserveState(firstBytes);
            using var secondState = RepeatResources.ReserveState(secondBytes);
            using var outputState
                = RepeatResources.ReserveState(BigInteger.Zero);
            var replacement = new SortedDictionary<RawUnionArrayStateKey,
                RawUnionArrayElementState>(
                    RawUnionArrayStateKeyComparer.Instance);
            var outputHeaderWritten = false;
            var currentCursor = _rawUnionArrayElementBytes.GetEnumerator();
            var hasCurrent = false;
            var currentKey = default(RawUnionArrayStateKey);
            RawUnionArrayElementState? currentFact = null;
            var firstCursor = first._rawUnionArrayElementBytes.GetEnumerator();
            var secondCursor = second._rawUnionArrayElementBytes.GetEnumerator();
            RepeatResources.ConsumeWork();
            var hasFirstEntry = firstCursor.MoveNext();
            RepeatResources.ConsumeWork();
            var hasSecondEntry = secondCursor.MoveNext();
            var hasFirst = TryFindNextVariableRoot(ref firstCursor,
                ref hasFirstEntry, variable, out var firstRoot);
            var hasSecond = TryFindNextVariableRoot(ref secondCursor,
                ref hasSecondEntry, variable, out var secondRoot);
            var hasMerged = false;
            var mergedKey = default(RawUnionArrayStateKey);
            RawUnionArrayElementState? mergedLeft = null;
            RawUnionArrayElementState? mergedRight = null;

            void AdvanceCurrent()
            {
                hasCurrent = false;
                currentFact = null;
                while (true)
                {
                    if (!currentCursor.MoveNext())
                    {
                        RepeatResources.ConsumeWork();
                        return;
                    }

                    RepeatResources.ConsumeWork();
                    var candidate = currentCursor.Current;
                    ChargeRepeatRoot(candidate.Key.ArrayPrefix);
                    RepeatResources.ConsumeWork();
                    if (candidate.Key.Variable == variable)
                    {
                        continue;
                    }

                    currentKey = candidate.Key;
                    currentFact = candidate.Value;
                    hasCurrent = true;
                    return;
                }
            }

            void AdvanceMerged(bool consumeCurrent)
            {
                if (consumeCurrent)
                {
                    hasFirst = TryFindNextVariableRoot(ref firstCursor,
                        ref hasFirstEntry, variable, out firstRoot);
                    hasSecond = TryFindNextVariableRoot(ref secondCursor,
                        ref hasSecondEntry, variable, out secondRoot);
                }

                hasMerged = false;
                while (hasFirst || hasSecond)
                {
                    RepeatResources.ConsumeWork(2);
                    if (!hasFirst)
                    {
                        hasSecond = TryFindNextVariableRoot(
                            ref secondCursor, ref hasSecondEntry,
                            variable, out secondRoot);
                        continue;
                    }
                    if (!hasSecond)
                    {
                        hasFirst = TryFindNextVariableRoot(
                            ref firstCursor, ref hasFirstEntry,
                            variable, out firstRoot);
                        continue;
                    }

                    RepeatResources.ConsumeWork();
                    var comparison = StorageProjectionComparer.Instance
                        .Compare(firstRoot.Key.ArrayPrefix,
                            secondRoot.Key.ArrayPrefix);
                    if (comparison < 0)
                    {
                        hasFirst = TryFindNextVariableRoot(
                            ref firstCursor, ref hasFirstEntry,
                            variable, out firstRoot);
                        continue;
                    }
                    if (comparison > 0)
                    {
                        hasSecond = TryFindNextVariableRoot(
                            ref secondCursor, ref hasSecondEntry,
                            variable, out secondRoot);
                        continue;
                    }

                    mergedKey = new RawUnionArrayStateKey(variable,
                        firstRoot.Key.ArrayPrefix);
                    mergedLeft = firstRoot.Value;
                    mergedRight = secondRoot.Value;
                    hasMerged = true;
                    return;
                }
            }

            void EnsureOutputHeader()
            {
                if (outputHeaderWritten)
                {
                    return;
                }

                ChargeRepeatRootListHeader();
                outputState.Add(16);
                outputHeaderWritten = true;
            }

            void AppendOutput(RawUnionArrayStateKey key,
                RawUnionArrayElementState state)
            {
                EnsureOutputHeader();
                ChargeRepeatRoot(key.ArrayPrefix);
                outputState.Add(RawUnionArrayRootLogicalBytes(
                    key.ArrayPrefix));
                var output = state.CloneTo(outputState);
                replacement.Add(key, output);
            }

            AdvanceCurrent();
            AdvanceMerged(consumeCurrent: false);
            while (hasCurrent || hasMerged)
            {
                var comparison = 0;
                if (hasCurrent && hasMerged)
                {
                    RepeatResources.ConsumeWork();
                    comparison = RawUnionArrayStateKeyComparer.Instance
                        .Compare(currentKey, mergedKey);
                    if (comparison == 0)
                    {
                        throw Invalid();
                    }
                }

                if (!hasMerged || (hasCurrent && comparison < 0))
                {
                    AppendOutput(currentKey,
                        currentFact ?? throw Invalid());
                    AdvanceCurrent();
                }
                else
                {
                    EnsureOutputHeader();
                    ChargeRepeatRoot(mergedKey.ArrayPrefix);
                    outputState.Add(RawUnionArrayRootLogicalBytes(
                        mergedKey.ArrayPrefix));
                    var mergedState = (mergedLeft ?? throw Invalid())
                        .CreateMerge(mergedRight ?? throw Invalid(),
                            outputState);
                    replacement.Add(mergedKey, mergedState);
                    AdvanceMerged(consumeCurrent: true);
                }
            }

            var changed = !RawUnionArrayFactPrefixEquals(
                _rawUnionArrayElementBytes, replacement, variable,
                StorageProjection.WholeObject);
            repeatFrame.Complete();
            _rawUnionArrayElementBytes = replacement;
            UpdateRepeatSnapshotStateLease();
            return changed;
        }

        private bool TryFindNextVariableRoot(
            ref SortedDictionary<RawUnionArrayStateKey,
                RawUnionArrayElementState>.Enumerator cursor,
            ref bool hasEntry, int variable,
            out KeyValuePair<RawUnionArrayStateKey,
                RawUnionArrayElementState> root)
        {
            while (true)
            {
                RepeatResources.ConsumeWork();
                if (!hasEntry)
                {
                    root = default;
                    return false;
                }

                var candidate = cursor.Current;
                ChargeRepeatRoot(candidate.Key.ArrayPrefix);
                RepeatResources.ConsumeWork();
                hasEntry = cursor.MoveNext();
                if (candidate.Key.Variable > variable)
                {
                    root = default;
                    return false;
                }
                if (candidate.Key.Variable == variable)
                {
                    root = candidate;
                    return true;
                }
            }
        }

        private bool RawUnionArrayFactPrefixEquals(
            SortedDictionary<RawUnionArrayStateKey,
                RawUnionArrayElementState> left,
            SortedDictionary<RawUnionArrayStateKey,
                RawUnionArrayElementState> right,
            int variable, StorageProjection prefix)
        {
            if (left.Count != 0)
            {
                ChargeRepeatRootListHeader();
            }
            if (right.Count != 0)
            {
                ChargeRepeatRootListHeader();
            }

            var leftCursor = left.GetEnumerator();
            var rightCursor = right.GetEnumerator();
            var hasLeftEntry = leftCursor.MoveNext();
            var hasRightEntry = rightCursor.MoveNext();
            var hasLeft = TryFindNextRoot(ref leftCursor, ref hasLeftEntry,
                variable, prefix, out var leftRoot);
            var hasRight = TryFindNextRoot(ref rightCursor, ref hasRightEntry,
                variable, prefix, out var rightRoot);
            while (hasLeft || hasRight)
            {
                if (!hasLeft || !hasRight)
                {
                    return false;
                }

                RepeatResources.ConsumeWork();
                if (StorageProjectionComparer.Instance.Compare(
                        leftRoot.Key.ArrayPrefix,
                        rightRoot.Key.ArrayPrefix) != 0
                    || !RawUnionArrayElementStatesEqual(
                        leftRoot.Value, rightRoot.Value))
                {
                    return false;
                }

                hasLeft = TryFindNextRoot(ref leftCursor, ref hasLeftEntry,
                    variable, prefix, out leftRoot);
                hasRight = TryFindNextRoot(ref rightCursor, ref hasRightEntry,
                    variable, prefix, out rightRoot);
            }
            return true;
        }

        private bool TryFindNextRoot(
            ref SortedDictionary<RawUnionArrayStateKey,
                RawUnionArrayElementState>.Enumerator cursor,
            ref bool hasEntry, int variable, StorageProjection prefix,
            out KeyValuePair<RawUnionArrayStateKey,
                RawUnionArrayElementState> root)
        {
            while (true)
            {
                RepeatResources.ConsumeWork();
                if (!hasEntry)
                {
                    root = default;
                    return false;
                }

                var candidate = cursor.Current;
                ChargeRepeatRoot(candidate.Key.ArrayPrefix);
                RepeatResources.ConsumeWork();
                hasEntry = cursor.MoveNext();
                if (candidate.Key.Variable > variable)
                {
                    root = default;
                    return false;
                }
                if (candidate.Key.Variable == variable
                    && prefix.ProjectionPath.IsPrefixOf(
                        candidate.Key.ArrayPrefix.ProjectionPath))
                {
                    root = candidate;
                    return true;
                }
            }
        }

        private static bool RawUnionArrayElementStatesEqual(
            RawUnionArrayElementState left,
            RawUnionArrayElementState right)
            => left.SemanticallyEquals(right);

        internal bool CopyRawUnionInitializedBytes(int variable,
            InitializationState other)
        {
            var arrayChanged = CopyRawUnionArrayFacts(variable, other);
            var hasCurrent = _rawUnionInitializedBytes.TryGetValue(variable,
                out var currentBytes);
            var hasOther = other._rawUnionInitializedBytes.TryGetValue(variable,
                out var otherBytes);
            if (!hasCurrent && !hasOther)
            {
                return arrayChanged;
            }
            if (!hasCurrent || !hasOther
                || currentBytes!.Length != otherBytes!.Length)
            {
                throw Invalid();
            }

            var changed = !currentBytes.AsSpan().SequenceEqual(otherBytes);
            _rawUnionInitializedBytes[variable] = (byte[])otherBytes.Clone();
            return changed || arrayChanged;
        }

        internal bool MergeRawUnionInitializedBytes(int variable,
            InitializationState other)
            => MergeRawUnionInitializedBytes(variable, this, other);

        internal bool MergeRawUnionInitializedBytes(int variable,
            InitializationState first, InitializationState second)
        {
            var arrayChanged = MergeRawUnionArrayFacts(
                variable, first, second);
            var hasCurrent = _rawUnionInitializedBytes.TryGetValue(variable,
                out var currentBytes);
            var hasFirst = first._rawUnionInitializedBytes.TryGetValue(variable,
                out var firstBytes);
            var hasSecond = second._rawUnionInitializedBytes.TryGetValue(variable,
                out var secondBytes);
            if (!hasFirst && !hasSecond && !hasCurrent)
            {
                return arrayChanged;
            }
            if (!hasFirst || !hasSecond || !hasCurrent
                || currentBytes!.Length != firstBytes!.Length
                || firstBytes.Length != secondBytes!.Length)
            {
                throw Invalid();
            }

            var mergedBytes = new byte[firstBytes.Length];
            for (var index = 0; index < mergedBytes.Length; index++)
            {
                mergedBytes[index] = (byte)(firstBytes[index]
                    & secondBytes[index]);
            }
            var changed = !currentBytes.AsSpan().SequenceEqual(mergedBytes);
            _rawUnionInitializedBytes[variable] = mergedBytes;
            return changed || arrayChanged;
        }

        internal void MarkNativeOutputPending(int index)
        {
            var destination = _nativeOutputDestinations[index];
            destination = destination is null
                ? new NativeOutputDestinationState(new object())
                : destination with { Version = new object(), IsReserved = false };
            _possibilities[index] = (byte)(destination.UnavailablePossibilities | 3);
            SetNativeOutputState(index, destination, null);
        }

        internal void ReserveNativeOutput(int index)
        {
            if (!IsVacant(index)
                || _nativeOutputDestinations[index] is { IsReserved: true })
            {
                throw Invalid();
            }
            var unavailablePossibilities = _possibilities[index];
            SetNativeOutputState(index, null, null);
            Dependencies?.ClearNativePointerProvenance(index);
            _possibilities[index] = (byte)(unavailablePossibilities | 8);
            SetNativeOutputState(index, new NativeOutputDestinationState(new object()) {
                UnavailablePossibilities = unavailablePossibilities,
                IsReserved = true,
            }, null);
        }

        internal void CancelUninvokedNativeOutputReservations()
        {
            for (var index = 0; index < Length; index++)
            {
                if (_nativeOutputDestinations[index] is not { IsReserved: true } destination)
                {
                    continue;
                }
                _possibilities[index] = destination.UnavailablePossibilities;
                SetNativeOutputState(index, null, null);
                Dependencies?.ClearNativePointerProvenance(index);
            }
        }

        internal NativeOutputCallFact? NativeOutputResult(int index) => _nativeOutputResults[index];

        internal void SetNativeOutputResult(int index, NativeOutputCallFact? fact)
        {
            SetNativeOutputState(index, _nativeOutputDestinations[index], fact);
        }

        internal object? NativeOutputDestinationVersion(int index)
            => _nativeOutputDestinations[index]?.Version;

        internal void InvalidateNativeOutputVariable(int index)
        {
            if (_nativeOutputDestinations[index] is not null)
            {
                _possibilities[index] = 3;
            }
            SetNativeOutputState(index, null, null);
            Dependencies?.ClearNativePointerProvenance(index);
        }

        internal void InvalidateNativeOutputCorrelations()
        {
            for (var index = 0; index < Length; index++)
            {
                if (_nativeOutputDestinations[index] is NativeOutputDestinationState destination)
                {
                    _possibilities[index] = 3;
                    SetNativeOutputState(index, destination with { Version = null }, null);
                }
                else
                {
                    SetNativeOutputState(index, null, null);
                }
            }
        }

        internal void SetNativeOutputAvailability(NativeOutputDestinationFact destination, bool available)
        {
            if ((uint)destination.Variable < (uint)Length
                && _nativeOutputDestinations[destination.Variable] is NativeOutputDestinationState state
                && ReferenceEquals(state.Version, destination.Version))
            {
                _possibilities[destination.Variable] = available
                    ? (byte)2 : state.UnavailablePossibilities;
                if (!available)
                {
                    Dependencies?.ClearNativePointerProvenance(destination.Variable);
                }
            }
        }

        internal bool MergeNativeOutputState(InitializationState left, bool leftReachable,
            InitializationState right, bool rightReachable)
        {
            var changed = false;
            for (var index = 0; index < Length; index++)
            {
                var previousDestination = _nativeOutputDestinations[index];
                var previousResult = _nativeOutputResults[index];
                if (!leftReachable && rightReachable)
                {
                    CopyNativeOutputSlot(index, right);
                }
                else if (leftReachable && !rightReachable)
                {
                    CopyNativeOutputSlot(index, left);
                }
                else if (!leftReachable)
                {
                    SetNativeOutputState(index, null, null);
                }
                else
                {
                    MergeNativeOutputSlot(index, left._nativeOutputDestinations[index],
                        left._nativeOutputResults[index], right._nativeOutputDestinations[index],
                        right._nativeOutputResults[index]);
                }
                changed |= !ReferenceEquals(previousDestination, _nativeOutputDestinations[index])
                    || !ReferenceEquals(previousResult, _nativeOutputResults[index]);
            }
            return changed;
        }

        private void MergeNativeOutputSlot(int index, NativeOutputDestinationState? leftDestination,
            NativeOutputCallFact? leftResult, NativeOutputDestinationState? rightDestination,
            NativeOutputCallFact? rightResult)
        {
            NativeOutputDestinationState? destination;
            if (ReferenceEquals(leftDestination, rightDestination))
            {
                destination = leftDestination;
            }
            else if (leftDestination is null && rightDestination is null)
            {
                destination = null;
            }
            else if (leftDestination?.Version is object leftVersion
                && rightDestination?.Version is object rightVersion
                && ReferenceEquals(leftVersion, rightVersion))
            {
                destination = leftDestination;
            }
            else if (leftDestination?.Version is null && rightDestination?.Version is null)
            {
                destination = UnknownNativeOutputDestination;
            }
            else
            {
                destination = UnknownNativeOutputDestination;
            }
            SetNativeOutputState(index, destination,
                ReferenceEquals(leftResult, rightResult) ? leftResult : null);
        }

        internal void CopyNativeOutputSlot(int index, InitializationState source)
        {
            SetNativeOutputState(index, source._nativeOutputDestinations[index], source._nativeOutputResults[index]);
        }

        internal void Record(BoundStatement statement)
        {
            if (StatementInputs is null)
            {
                return;
            }
            if (StatementInputs.TryGetValue(statement, out var input))
            {
                for (var index = 0; index < Length; index++)
                {
                    input.Merge(index, this);
                }
            }
            else
            {
                StatementInputs.Add(statement,
                    Clone(StatementInputSnapshotScope));
            }
        }
    }

    private sealed class FailureEdges : IDisposable
    {
        private InitializationState? _state;
        private uint[]? _masks;
        private RepeatSnapshotReservationScope? _snapshotScope;
        private bool _ownsSnapshotScope;

        internal FailureEdges(
            RepeatSnapshotReservationScope? snapshotScope = null)
        {
            _snapshotScope = snapshotScope;
        }

        internal bool HasEdges => _state is not null;

        internal InitializationState State
            => (_state ?? throw Invalid()).Clone(GetSnapshotScope());

        internal uint[] Masks => (uint[])(_masks ?? throw Invalid()).Clone();

        internal void Add(InitializationState state, uint[] masks)
        {
            if (masks.Length != state.Length)
            {
                throw Invalid();
            }
            if (_state is null)
            {
                _state = state.Clone(GetSnapshotScope(state));
                _state.CancelUninvokedNativeOutputReservations();
                _masks = (uint[])masks.Clone();
                ClearStagedReferenceFacts(_state);
                return;
            }

            using var incomingSnapshotScope
                = new RepeatSnapshotReservationScope(
                    state.RepeatResources);
            var failureState = state.Clone(incomingSnapshotScope);
            failureState.CancelUninvokedNativeOutputReservations();
            ClearStagedReferenceFacts(failureState);
            for (var index = 0; index < failureState.Length; index++)
            {
                _state.Merge(index, failureState);
                _masks![index] &= masks[index];
            }
        }

        internal void Reset()
        {
            if (_ownsSnapshotScope)
            {
                _snapshotScope?.Dispose();
                _snapshotScope = null;
                _ownsSnapshotScope = false;
            }
            _state = null;
            _masks = null;
        }

        public void Dispose()
        {
            if (_ownsSnapshotScope)
            {
                _snapshotScope?.Dispose();
            }
            _state = null;
            _masks = null;
            _snapshotScope = null;
            _ownsSnapshotScope = false;
        }

        private RepeatSnapshotReservationScope GetSnapshotScope()
            => _snapshotScope ?? throw Invalid();

        private RepeatSnapshotReservationScope GetSnapshotScope(
            InitializationState state)
        {
            if (_snapshotScope is null)
            {
                _snapshotScope = new RepeatSnapshotReservationScope(
                    state.RepeatResources);
                _ownsSnapshotScope = true;
            }
            return _snapshotScope;
        }

        private static void ClearStagedReferenceFacts(
            InitializationState failureState)
        {
            if (failureState.Dependencies is not BackingDependencies dependencies)
            {
                return;
            }
            // Staged reference arguments expire when the call evaluation unwinds.
            for (var index = 0; index < dependencies.Staged.Count; index++)
            {
                var staged = dependencies.Staged[index];
                if (staged.ReferenceTargets.Count != 0
                    || staged.ReferenceObligations.Count != 0
                    || staged.ReferenceFieldValues.Count != 0)
                {
                    dependencies.Staged[index] = staged with {
                        ReferenceTargets = [],
                        ReferenceObligations = [],
                        ReferenceFieldValues = [],
                    };
                }
            }
        }
    }

    private static void VerifyUpdatePlace(BoundExpression place, BoundFunction function,
        BoundProgram program, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        switch (place)
        {
            case BoundRead read when read.Variable >= 0 && read.Variable < function.Variables.Length
                && read.Type == function.Variables[read.Variable].Type
                && function.Variables[read.Variable].IsMutable:
                return;
            case BoundBufferRead buffer when buffer.Buffer >= 0 && buffer.Buffer < program.Buffers.Length
                && program.Buffers[buffer.Buffer].IsWritable
                && program.Buffers[buffer.Buffer].ElementType == buffer.Type:
                return;
            case BoundFieldRead field when field.Receiver is not null
                && HasFields(program, field.Receiver.Type)
                && field.Field >= 0 && field.Field < Fields(program, field.Receiver.Type).Length
                && CanAccessField(function, program, field.Receiver.Type, field.Field)
                && field.Type == Fields(program, field.Receiver.Type)[field.Field].Type
                && Fields(program, field.Receiver.Type)[field.Field].IsMutable:
                if (ManagedClassTypes.IsClass(field.Receiver.Type))
                {
                    if (ManagedClassTypes.IsWritable(field.Receiver.Type))
                    {
                        return;
                    }
                }
                else if (!IsUnique(program, field.Receiver.Type)
                    || field.Receiver is BoundRead)
                {
                    VerifyUpdatePlace(field.Receiver, function, program, ref remaining, depth + 1);
                    return;
                }
                break;
        }
        throw InvalidEffect(place.Span, "Ownership");
    }

    private static int VerifyUniqueFieldPlace(BoundFieldRead place, InitializationState initialized, uint[] fieldMasks,
        BoundFunction function, BoundProgram program, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (place is null
            || !TryGetFieldProjectionPath(place, function, program,
                out var rootVariable, out var fieldPath)
            || !IsUnique(program, function.Variables[rootVariable].Type))
        {
            throw InvalidEffect(place?.Span ?? default, "Ownership");
        }

        var ownerType = function.Variables[rootVariable].Type;
        foreach (var segment in fieldPath.Segments)
        {
            Count(ref remaining, depth + 1);
            if (segment.OwnerType != ownerType || !IsUnique(program, ownerType)
                || !HasFields(program, ownerType)
                || (uint)segment.Field >= (uint)Fields(program, ownerType).Length
                || !IsUnique(program, Fields(program, ownerType)[segment.Field].Type)
                || !CanAccessField(function, program, ownerType, segment.Field))
            {
                throw InvalidEffect(place.Span, "Ownership");
            }
            ownerType = Fields(program, ownerType)[segment.Field].Type;
        }
        if (ownerType != place.Type
            || !VerifyFieldPathLeafLive(place, initialized, fieldMasks,
                function, program, ref remaining, depth + 1)
            || (IsUnique(program, place.Type) && HasFields(program, place.Type)
                && !initialized.NestedOwnerFields(rootVariable, fieldPath,
                    place.Type).IsComplete))
        {
            throw InvalidEffect(place.Span, "Ownership");
        }
        return rootVariable;
    }

    private static void ExpressionReferencePlace(
        BoundExpression place, InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function, BoundProgram program,
        ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        if (place is BoundFieldRead field)
        {
            var ownerType = field.Receiver.Type;
            if (!HasFields(program, ownerType)
                || (uint)field.Field >= (uint)Fields(program, ownerType).Length
                || !CanAccessField(function, program, ownerType, field.Field)
                || field.Type != Fields(program, ownerType)[field.Field].Type)
            {
                throw Invalid(field.Span);
            }
            if (RequiresUnsafeFieldAccess(program, ownerType,
                    Fields(program, ownerType)[field.Field])
                && !field.IsUnsafeAuthorized)
            {
                throw InvalidEffect(field.Span, "UnsafeCaller");
            }
            ExpressionReferencePlace(field.Receiver, initialized, fieldMasks,
                declarations, function, program, ref remaining, depth + 1,
                failures);
            return;
        }

        if (place is BoundRead constructorReceiver
            && (function.IsConstructor || function.IsClassConstructor)
            && constructorReceiver.Variable == 0
            && constructorReceiver.Type == function.Variables[0].Type
            && ManagedClassTypes.IsClass(constructorReceiver.Type))
        {
            return;
        }

        if (place is BoundRead read && IsUnique(program, read.Type))
        {
            if ((uint)read.Variable >= (uint)initialized.Length
                || function.Variables[read.Variable].Type != read.Type
                || (!initialized[read.Variable]
                    && !initialized.HasPartialValue(read.Variable)))
            {
                throw InvalidEffect(read.Span, "Ownership");
            }
            return;
        }

        Expression(place, initialized, fieldMasks, declarations, function,
            program, ref remaining, depth + 1, failures);
    }

    private static bool TryGetFieldProjectionPath(
        BoundFieldRead place, BoundFunction function, BoundProgram program,
        out int rootVariable, out FieldProjectionPath fieldPath)
    {
        rootVariable = -1;
        fieldPath = default;
        if (place is null)
        {
            return false;
        }

        var fields = new Stack<BoundFieldRead>();
        BoundExpression receiver = place;
        while (receiver is BoundFieldRead field)
        {
            fields.Push(field);
            receiver = field.Receiver;
        }
        if (receiver is not BoundRead root
            || (uint)root.Variable >= (uint)function.Variables.Length
            || root.Type != function.Variables[root.Variable].Type)
        {
            return false;
        }

        rootVariable = root.Variable;
        var ownerType = root.Type;
        while (fields.TryPop(out var field))
        {
            if (field.Receiver.Type != ownerType || !HasFields(program, ownerType)
                || (uint)field.Field >= (uint)Fields(program, ownerType).Length
                || field.Type != Fields(program, ownerType)[field.Field].Type
                || !CanAccessField(function, program, ownerType, field.Field))
            {
                rootVariable = -1;
                fieldPath = default;
                return false;
            }
            if (RequiresUnsafeFieldAccess(program, ownerType,
                    Fields(program, ownerType)[field.Field])
                && !field.IsUnsafeAuthorized)
            {
                throw InvalidEffect(field.Span, "UnsafeCaller");
            }
            fieldPath = fieldPath.Append(ownerType, field.Field);
            ownerType = field.Type;
        }
        return !fieldPath.Segments.IsEmpty;
    }

    private static bool TryGetFieldRootVariable(
        BoundFieldRead place, out int rootVariable)
    {
        BoundExpression receiver = place;
        while (receiver is BoundFieldRead field)
        {
            receiver = field.Receiver;
        }
        if (receiver is BoundRead root)
        {
            rootVariable = root.Variable;
            return true;
        }
        rootVariable = -1;
        return false;
    }

    private static FieldProjectionPath FieldPathPrefix(FieldProjectionPath path, int length)
    {
        if (length < 0 || length > path.Segments.Length)
        {
            throw Invalid();
        }

        var prefix = default(FieldProjectionPath);
        for (var index = 0; index < length; index++)
        {
            var segment = path.Segments[index];
            prefix = prefix.Append(segment.OwnerType, segment.Field);
        }
        return prefix;
    }

    private static bool TryGetFieldAssignmentProjectionPath(
        BoundFieldAssignment assignment, PrimitiveType rootType,
        BoundProgram program, out FieldProjectionPath fieldPath)
    {
        fieldPath = default;
        if (assignment is null || assignment.NestedFields.IsDefault
            || assignment.NestedFields.Length > MaximumDepth)
        {
            return false;
        }

        var ownerType = rootType;
        var field = assignment.Field;
        for (var nestedIndex = -1;
            nestedIndex < assignment.NestedFields.Length;
            nestedIndex++)
        {
            if (!HasFields(program, ownerType)
                || (uint)field >= (uint)Fields(program, ownerType).Length)
            {
                fieldPath = default;
                return false;
            }

            var fieldType = Fields(program, ownerType)[field].Type;
            fieldPath = fieldPath.Append(ownerType, field);
            ownerType = fieldType;
            if (nestedIndex + 1 < assignment.NestedFields.Length)
            {
                field = assignment.NestedFields[nestedIndex + 1];
            }
        }

        return !fieldPath.Segments.IsEmpty;
    }

    private static (uint Definite, uint Possible) FieldPathOwnerMasks(
        int rootVariable, FieldProjectionPath ownerPath, PrimitiveType ownerType,
        InitializationState initialized, uint[] fieldMasks, BoundProgram program)
    {
        if (ownerPath.Segments.IsEmpty)
        {
            return (fieldMasks[rootVariable], initialized.PossibleFieldMask(rootVariable));
        }
        if (IsUnique(program, ownerType) && HasFields(program, ownerType))
        {
            var fields = initialized.NestedOwnerFields(rootVariable, ownerPath, ownerType);
            return (fields.DefiniteMask, fields.PossibleMask);
        }
        if (CarriesRawUnionPayloadFacts(program, ownerType)
            && IsInlineStructStorage(ownerType, program))
        {
            var initialMask = FullMask(program, ownerType);
            var rootSegment = ownerPath.Segments[0];
            if ((fieldMasks[rootVariable] & (1u << rootSegment.Field)) == 0)
            {
                initialMask = 0;
            }
            var fields = initialized.NestedOwnerFields(rootVariable,
                ownerPath, ownerType, initialMask);
            return (fields.DefiniteMask, fields.PossibleMask);
        }
        if (IsInlineStructStorage(ownerType, program)
            && NativeFixedArrayContract
                .HasInlineFixedArrayFieldStorage(ownerType,
                    program.Structures, program.NativeFixedArrayTypes))
        {
            var parentSegment = ownerPath.Segments[^1];
            var parentPath = FieldPathPrefix(ownerPath,
                ownerPath.Segments.Length - 1);
            var (parentDefinite, _) = FieldPathOwnerMasks(rootVariable,
                parentPath, parentSegment.OwnerType, initialized,
                fieldMasks, program);
            var initialMask = (parentDefinite
                    & (1u << parentSegment.Field)) != 0
                ? FullMask(program, ownerType) : 0;
            var fields = initialized.NestedOwnerFields(rootVariable,
                ownerPath, ownerType, initialMask);
            return (fields.DefiniteMask, fields.PossibleMask);
        }
        if (program.NativeLoweringAgreement
                == CompilationOptions.WindowsX64NormalBackingAgreement
            && IsInlineStructStorage(ownerType, program))
        {
            var initialMask = FullMask(program, ownerType);
            var rootSegment = ownerPath.Segments[0];
            if (ManagedClassTypes.IsClass(rootSegment.OwnerType)
                && (fieldMasks[rootVariable] & (1u << rootSegment.Field)) == 0)
            {
                initialMask = 0;
            }
            var fields = initialized.NestedOwnerFields(
                rootVariable, ownerPath, ownerType, initialMask);
            return (fields.DefiniteMask, fields.PossibleMask);
        }
        var fullMask = FullMask(program, ownerType);
        return (fullMask, fullMask);
    }

    private static bool VerifyFieldPathLeafLive(
        BoundFieldRead place, InitializationState initialized, uint[] fieldMasks,
        BoundFunction function, BoundProgram program, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (!TryGetFieldProjectionPath(place, function, program,
                out var rootVariable, out var fieldPath)
            || (!IsUnique(program,
                    function.Variables[rootVariable].Type)
                && !IsInlineStructStorage(
                    function.Variables[rootVariable].Type, program))
            || (!initialized[rootVariable] && !initialized.HasPartialValue(rootVariable)))
        {
            throw InvalidEffect(place?.Span ?? default, "Ownership");
        }

        // A complete nested Union path is not observable before its parent is published.
        if (function.Variables[rootVariable].IsInitializationLocal
            && !initialized[rootVariable]
            && fieldPath.Segments.Length > 1
            && fieldPath.Segments.Any(segment =>
                IsRawUnionType(program, segment.OwnerType)))
        {
            throw InvalidEffect(place.Span, "Ownership");
        }

        var segments = fieldPath.Segments;
        for (var index = 0; index < segments.Length; index++)
        {
            Count(ref remaining, depth + index + 1);
            var segment = segments[index];
            var ownerPath = FieldPathPrefix(fieldPath, index);
            var (definite, possible) = FieldPathOwnerMasks(
                rootVariable, ownerPath, segment.OwnerType,
                initialized, fieldMasks, program);
            var bit = 1u << segment.Field;
            var isLeaf = index == segments.Length - 1;
            if (isLeaf ? (definite & bit) == 0 : (possible & bit) == 0)
            {
                throw InvalidEffect(place.Span, "Ownership");
            }
        }
        return true;
    }

    private static void SetNestedFieldLiveness(
        int rootVariable, FieldProjectionPath fieldPath, bool live,
        InitializationState initialized, uint[] fieldMasks,
        BoundFunction function, BoundProgram program, TextSpan span)
    {
        var segments = fieldPath.Segments;
        if (segments.Length < 2)
        {
            throw Invalid(span);
        }
        var leaf = segments[^1];
        var ownerPath = FieldPathPrefix(fieldPath, segments.Length - 1);
        var (ownerDefinite, ownerPossible) = FieldPathOwnerMasks(
            rootVariable, ownerPath, leaf.OwnerType, initialized,
            fieldMasks, program);
        var ownerState = new NestedOwnerFieldState(
            leaf.OwnerType, FullMask(program, leaf.OwnerType),
            ownerDefinite, ownerPossible);
        var fieldBit = 1u << leaf.Field;
        var unionAlternative = IsRawUnionType(program, leaf.OwnerType);
        var definite = live
            ? unionAlternative ? ownerState.FullMask
                : ownerState.DefiniteMask | fieldBit
            : ownerState.DefiniteMask & ~fieldBit;
        var possible = live
            ? unionAlternative ? ownerState.FullMask
                : ownerState.PossibleMask | fieldBit
            : ownerState.PossibleMask & ~fieldBit;
        initialized.SetNestedOwnerFields(rootVariable, ownerPath,
            ownerState with { DefiniteMask = definite, PossibleMask = possible });
        PropagateNestedOwnerCompleteness(rootVariable, ownerPath, leaf.OwnerType,
            initialized, fieldMasks, function, program, span);
    }

    private static void InvalidateOverlappingRawUnionFacts(
        int variable, PrimitiveType rootType,
        FieldProjectionPath fieldPath, ulong writeSize,
        BackingDependencies dependencies, BoundProgram program)
    {
        if (writeSize == 0
            || !TryGetFieldPathByteOffset(rootType, fieldPath, program,
                out var rootWriteOffset))
        {
            return;
        }
        var segments = fieldPath.Segments;
        for (var unionOwnerIndex = 0;
            unionOwnerIndex < segments.Length;
            unionOwnerIndex++)
        {
            var unionType = segments[unionOwnerIndex].OwnerType;
            if (!IsRawUnionType(program, unionType))
            {
                continue;
            }

            var unionOwnerPath = FieldPathPrefix(fieldPath,
                unionOwnerIndex);
            var unionOffset = 0;
            if (!unionOwnerPath.Segments.IsEmpty
                && !TryGetFieldPathByteOffset(rootType,
                    unionOwnerPath, program, out unionOffset))
            {
                throw Invalid();
            }
            if (unionOffset > rootWriteOffset)
            {
                throw Invalid();
            }

            var unionWriteOffset
                = checked((ulong)(rootWriteOffset - unionOffset));
            var writtenAlternative = segments[unionOwnerIndex].Field;
            var writeEnd = checked(unionWriteOffset + writeSize);
            var unionSize = program.Structures[
                (int)unionType - 100].Size;
            var unionFields = program.Structures[
                (int)unionType - 100].Fields;
            if ((uint)writtenAlternative >= (uint)unionFields.Length
                || unionWriteOffset > unionSize
                || writeSize > unionSize - unionWriteOffset)
            {
                throw Invalid();
            }

            dependencies.SetCopyOriginRoot(variable,
                CopyOriginFact.Unavailable());
            dependencies.ClearCopyOriginFieldValues(variable,
                StorageProjection.WholeObject);
            dependencies.ClearBackingSources(variable);
            dependencies.ClearReferenceTargets(variable);
            dependencies.SetReferenceObligations(variable,
                EmptyBackingSources);
            dependencies.SetReferenceBackingSources(variable,
                EmptyBackingSources);
            dependencies.ClearNativePointerAggregateProvenance(variable);

            var wholeAlternativeWrite
                = unionOwnerIndex == segments.Length - 1;
            for (var field = 0; field < unionFields.Length; field++)
            {
                if (wholeAlternativeWrite && field == writtenAlternative)
                {
                    continue;
                }

                var candidate = unionFields[field];
                var candidateEnd = checked(candidate.Offset
                    + candidate.Size);
                if (candidate.Size == 0
                    || candidate.Offset >= writeEnd
                    || unionWriteOffset >= candidateEnd)
                {
                    continue;
                }

                var path = unionOwnerPath.Append(unionType, field);
                var projection = StorageProjection.ForFieldPath(path);
                dependencies.ClearCopyOriginFieldValues(variable,
                    projection);
                dependencies.ClearBackingFieldValues(variable, projection);
                dependencies.ClearReferenceFieldValues(variable, projection);
                dependencies.InvalidateNativePointerFieldValues(variable,
                    projection);
            }
        }
    }

    private static void PropagateNestedOwnerCompleteness(
        int rootVariable, FieldProjectionPath ownerPath, PrimitiveType ownerType,
        InitializationState initialized, uint[] fieldMasks,
        BoundFunction function, BoundProgram program, TextSpan span)
    {
        while (!ownerPath.Segments.IsEmpty)
        {
            var (ownerDefiniteMask, ownerPossibleMask) = FieldPathOwnerMasks(
                rootVariable, ownerPath, ownerType, initialized,
                fieldMasks, program);
            var ownerState = new NestedOwnerFieldState(
                ownerType, FullMask(program, ownerType),
                ownerDefiniteMask, ownerPossibleMask);
            var ownerIsComplete = ownerState.IsComplete;
            var ownerSegment = ownerPath.Segments[^1];
            var parentPath = FieldPathPrefix(ownerPath, ownerPath.Segments.Length - 1);
            var fieldBit = 1u << ownerSegment.Field;
            if (parentPath.Segments.IsEmpty)
            {
                fieldMasks[rootVariable] = ownerIsComplete
                    ? fieldMasks[rootVariable] | fieldBit
                    : fieldMasks[rootVariable] & ~fieldBit;
                initialized.SetPossibleFieldMask(rootVariable,
                    initialized.PossibleFieldMask(rootVariable) | fieldBit);
                var rootType = function.Variables[rootVariable].Type;
                if (fieldMasks[rootVariable] == FullMask(program, rootType))
                {
                    initialized[rootVariable] = true;
                }
                else if (IsUnique(program, rootType)
                    || CarriesRawUnionPayloadFacts(program, rootType)
                    || NativeFixedArrayContract
                        .HasInlineFixedArrayFieldStorage(rootType,
                            program.Structures,
                            program.NativeFixedArrayTypes))
                {
                    initialized.MarkPartialValue(rootVariable);
                }
                return;
            }

            var parentType = ownerSegment.OwnerType;
            if ((!IsUnique(program, parentType)
                    && !CarriesRawUnionPayloadFacts(program, parentType)
                    && !NativeFixedArrayContract
                        .HasInlineFixedArrayFieldStorage(parentType,
                            program.Structures,
                            program.NativeFixedArrayTypes)
                    && !(program.NativeLoweringAgreement
                            == CompilationOptions.WindowsX64NormalBackingAgreement
                        && IsInlineStructStorage(parentType, program)))
                || !HasFields(program, parentType))
            {
                throw Invalid(span);
            }
            var (parentDefiniteMask, parentPossibleMask) = FieldPathOwnerMasks(
                rootVariable, parentPath, parentType, initialized,
                fieldMasks, program);
            var parentState = new NestedOwnerFieldState(
                parentType, FullMask(program, parentType),
                parentDefiniteMask, parentPossibleMask);
            var parentDefinite = ownerIsComplete
                ? parentState.DefiniteMask | fieldBit
                : parentState.DefiniteMask & ~fieldBit;
            initialized.SetNestedOwnerFields(rootVariable, parentPath,
                parentState with {
                    DefiniteMask = parentDefinite,
                    PossibleMask = parentState.PossibleMask | fieldBit,
                });
            ownerPath = parentPath;
            ownerType = parentType;
        }
    }

    private static int VerifyMutableFieldPlace(BoundFieldRead place, InitializationState initialized,
        BoundFunction function, BoundProgram program, ref int remaining, int depth, bool allowBufferLeaf = false)
    {
        Count(ref remaining, depth);
        if (place is null || place.Receiver is null)
        {
            throw Invalid();
        }
        var owner = (int)place.Receiver.Type - 100;
        if (owner < 0 || owner >= program.Structures.Length
            || place.Field < 0 || place.Field >= program.Structures[owner].Fields.Length
            || !program.Structures[owner].Fields[place.Field].IsMutable
            || !CanAccessField(function, program.Structures[owner], place.Field)
            || place.Type != program.Structures[owner].Fields[place.Field].Type
            || (RequiresUnsafeFieldAccess(program, place.Receiver.Type,
                    program.Structures[owner].Fields[place.Field])
                && !place.IsUnsafeAuthorized)
            || (!(allowBufferLeaf && place.Type == PrimitiveType.OwnedInt32Buffer)
                && (!HasFields(program, place.Type) || ManagedClassTypes.IsClass(place.Type))))
        {
            throw InvalidEffect(place.Span, "Ownership");
        }
        if (place.Receiver is BoundFieldRead parent)
        {
            return VerifyMutableFieldPlace(parent, initialized, function, program, ref remaining, depth + 1);
        }
        if (place.Receiver is not BoundRead root || root.Variable < 0
            || root.Variable >= initialized.Length || !initialized[root.Variable]
            || root.Type != function.Variables[root.Variable].Type
            || !function.Variables[root.Variable].IsMutable)
        {
            throw InvalidEffect(place.Span, "Ownership");
        }
        return root.Variable;
    }

    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "Unsupported token kinds must remain invalid rather than becoming accepted operators.")]
    private static BoundExpression UnwrapUnsafeExpression(BoundExpression expression)
    {
        while (expression is BoundUnsafeExpression unsafeExpression)
        {
            expression = unsafeExpression.Expression;
        }
        return expression;
    }

    // Keep long left-associated operators off the verifier's recursive call stack.
    private const int RecursiveBinaryChainLimit = 16;

    private static bool TryEvaluateLongLeftBinaryChain(
        BoundBinary root,
        InitializationState initialized,
        uint[] fieldMasks,
        HashSet<int> declarations,
        BoundFunction function,
        BoundProgram program,
        ref int remaining,
        int depth,
        FailureEdges? failures,
        BoundBinary? cleanupCounterStep)
    {
        var current = root;
        var chainLength = 1;
        while (chainLength < RecursiveBinaryChainLimit
            && current.Operator is not (TokenKind.AmpersandAmpersand or TokenKind.PipePipe)
            && current.Left is BoundBinary left
            && left.Operator is not (TokenKind.AmpersandAmpersand or TokenKind.PipePipe))
        {
            current = left;
            chainLength++;
        }
        if (chainLength < RecursiveBinaryChainLimit
            || root.Operator is TokenKind.AmpersandAmpersand or TokenKind.PipePipe)
        {
            return false;
        }

        var chain = new BoundBinary[chainLength];
        current = root;
        for (var index = 0; index < chain.Length; index++)
        {
            chain[index] = current;
            if (index + 1 < chain.Length)
            {
                current = (BoundBinary)current.Left;
            }
        }

        for (var index = 1; index < chainLength; index++)
        {
            Count(ref remaining, depth + index);
        }

        var completes = EvaluateExpression(chain[^1].Left, initialized,
            fieldMasks, declarations, function, program, ref remaining,
            depth + chainLength, failures);
        for (var index = chainLength - 1; index >= 0; index--)
        {
            var binary = chain[index];
            var rightDepth = depth + index + 1;
            var rightCompletes = completes && EvaluateExpression(binary.Right,
                initialized, fieldMasks, declarations, function, program,
                ref remaining, rightDepth, failures);
            if (!completes)
            {
                ValidateUnreachableExpression(binary.Right, initialized, fieldMasks,
                    declarations, function, program, ref remaining, rightDepth);
            }
            completes &= rightCompletes;
            VerifyBinaryOperation(binary, completes, initialized, fieldMasks,
                function, program, failures, cleanupCounterStep);
        }
        return true;
    }

    private static bool TryGetLongLeftLogicalChain(BoundBinary root, out BoundBinary[] chain)
    {
        chain = [];
        if (root.Operator is not (TokenKind.AmpersandAmpersand or TokenKind.PipePipe))
        {
            return false;
        }

        var current = root;
        var chainLength = 1;
        while (chainLength < RecursiveBinaryChainLimit
            && current.Left is BoundBinary left
            && left.Operator == root.Operator)
        {
            current = left;
            chainLength++;
        }
        if (chainLength < RecursiveBinaryChainLimit)
        {
            return false;
        }

        chain = new BoundBinary[chainLength];
        current = root;
        for (var index = 0; index < chain.Length; index++)
        {
            chain[index] = current;
            if (index + 1 < chain.Length)
            {
                current = (BoundBinary)current.Left;
            }
        }
        return true;
    }

    private static bool TryEvaluateLongLeftLogicalChain(
        BoundBinary root,
        InitializationState initialized,
        uint[] fieldMasks,
        HashSet<int> declarations,
        BoundFunction function,
        BoundProgram program,
        ref int remaining,
        int depth,
        FailureEdges? failures,
        BoundBinary? cleanupCounterStep)
    {
        if (!TryGetLongLeftLogicalChain(root, out var chain))
        {
            return false;
        }

        for (var index = 1; index < chain.Length; index++)
        {
            Count(ref remaining, depth + index);
        }

        using var repeatSnapshotState = new RepeatSnapshotReservationScope(
            initialized.RepeatResources);
        var beforeLeft = initialized.Clone(repeatSnapshotState);
        var beforeMasks = (uint[])fieldMasks.Clone();
        var leftCompletes = EvaluateExpression(chain[^1].Left, initialized,
            fieldMasks, declarations, function, program, ref remaining,
            depth + chain.Length, failures);
        for (var index = chain.Length - 1; index >= 0; index--)
        {
            var binary = chain[index];
            var evaluateWhenTrue = binary.Operator == TokenKind.AmpersandAmpersand;
            var rightState = Outcome(binary.Left, beforeLeft,
                evaluateWhenTrue, ref remaining, repeatSnapshotState);
            var rightMasks = (uint[])beforeMasks.Clone();
            MarkOutMasks(rightState, rightMasks, function, program);
            var evaluates = leftCompletes
                && CanCompleteAs(binary.Left, evaluateWhenTrue, beforeLeft,
                    program, ref remaining, depth + index + 1);
            var rightCompletes = evaluates && EvaluateExpression(binary.Right,
                rightState, rightMasks, declarations, function, program,
                ref remaining, depth + index + 1, failures);
            if (!evaluates)
            {
                ValidateUnreachableExpression(binary.Right, rightState, rightMasks,
                    declarations, function, program, ref remaining, depth + index + 1);
            }

            var skippedState = Outcome(binary.Left, beforeLeft,
                !evaluateWhenTrue, ref remaining, repeatSnapshotState);
            var skippedMasks = (uint[])beforeMasks.Clone();
            MarkOutMasks(skippedState, skippedMasks, function, program);
            var skips = leftCompletes
                && CanCompleteAs(binary.Left, !evaluateWhenTrue, beforeLeft,
                    program, ref remaining, depth + index + 1);
            if (rightCompletes || skips)
            {
                for (var variable = 0; variable < initialized.Length; variable++)
                {
                    initialized.SetPossibilities(variable, !rightCompletes
                        ? skippedState.Possibilities(variable)
                        : !skips ? rightState.Possibilities(variable)
                        : (byte)(rightState.Possibilities(variable) | skippedState.Possibilities(variable)));
                    fieldMasks[variable] = !rightCompletes ? skippedMasks[variable]
                        : !skips ? rightMasks[variable]
                        : rightMasks[variable] & skippedMasks[variable];
                    initialized.SetPossibleFieldMask(variable, !rightCompletes
                        ? skippedState.PossibleFieldMask(variable)
                        : !skips ? rightState.PossibleFieldMask(variable)
                        : rightState.PossibleFieldMask(variable) | skippedState.PossibleFieldMask(variable));
                    if (rightCompletes && skips)
                    {
                        _ = initialized.MergeRawUnionInitializedBytes(
                            variable, rightState, skippedState);
                        _ = initialized.MergeFieldPlaceGenerations(
                            variable, rightState, skippedState);
                        _ = initialized.MergeNestedOwnerStates(
                            variable, rightState, skippedState);
                    }
                    else if (rightCompletes)
                    {
                        _ = initialized.CopyRawUnionInitializedBytes(
                            variable, rightState);
                        _ = initialized.CopyFieldPlaceGenerations(variable, rightState);
                        _ = initialized.CopyNestedOwnerStates(variable, rightState);
                    }
                    else if (skips)
                    {
                        _ = initialized.CopyRawUnionInitializedBytes(
                            variable, skippedState);
                        _ = initialized.CopyFieldPlaceGenerations(variable, skippedState);
                        _ = initialized.CopyNestedOwnerStates(variable, skippedState);
                    }
                }
                _ = initialized.MergeNativeOutputState(
                    rightState, rightCompletes, skippedState, skips);
            }
            leftCompletes = rightCompletes || skips;
            VerifyBinaryOperation(binary, false, initialized, fieldMasks,
                function, program, failures, cleanupCounterStep);
        }
        return true;
    }

    private static void VerifyBinaryOperation(
        BoundBinary binary,
        bool binaryCompletes,
        InitializationState initialized,
        uint[] fieldMasks,
        BoundFunction function,
        BoundProgram program,
        FailureEdges? failures,
        BoundBinary? cleanupCounterStep)
    {
        var operands = binary.Left.Type == binary.Right.Type
            && binary.Left.Type != PrimitiveType.Void;
        var integerOperands = PrimitiveTypeFacts.IsIntegerOrWide(binary.Left.Type);
        var floatingOperands = PrimitiveTypeFacts.IsFloating(binary.Left.Type);
        var codeUnitOperands = PrimitiveTypeFacts.IsCodeUnitOrRune(binary.Left.Type);
        var floatingArithmetic = floatingOperands && (binary.Operator is TokenKind.Plus
            or TokenKind.Minus or TokenKind.Star or TokenKind.Slash or TokenKind.Percent);
        var integerArithmetic = integerOperands && (binary.Operator is TokenKind.Plus
            or TokenKind.Minus or TokenKind.Star or TokenKind.Slash or TokenKind.Percent);
        var integerOverflowOperation = integerOperands
            && (binary.Operator is TokenKind.Plus or TokenKind.Minus or TokenKind.Star);
        if (binary.OverflowBehavior is not (BoundIntegerOverflowBehavior.Checked
            or BoundIntegerOverflowBehavior.Wrapping)
            || (binary.OverflowBehavior == BoundIntegerOverflowBehavior.Wrapping
                && !integerOverflowOperation))
        {
            throw Invalid();
        }
        var checkedIntegerOperation = (integerOverflowOperation
                && binary.OverflowBehavior == BoundIntegerOverflowBehavior.Checked)
            || (integerOperands && (binary.Operator is TokenKind.Slash or TokenKind.Percent
                or TokenKind.ShiftLeft or TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned));
        var binaryValid = binary.Operator switch {
            TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash
                or TokenKind.Percent =>
                    operands && (integerArithmetic || floatingArithmetic)
                        && binary.Type == binary.Left.Type,
            TokenKind.ShiftLeft or TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned =>
                integerOperands && PrimitiveTypeFacts.IsIntegerOrWide(binary.Right.Type)
                    && binary.Type == binary.Left.Type,
            TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater
                or TokenKind.GreaterEquals =>
                    operands && (integerOperands || codeUnitOperands || floatingOperands)
                        && binary.Type == PrimitiveType.Bool,
            TokenKind.EqualsEquals or TokenKind.BangEquals =>
                operands && (integerOperands || codeUnitOperands
                    || binary.Left.Type == PrimitiveType.Bool || floatingOperands
                    || binary.Left.Type == PrimitiveType.NativeRawPointer)
                    && binary.Type == PrimitiveType.Bool,
            TokenKind.Ampersand or TokenKind.Pipe or TokenKind.Caret =>
                operands && (integerOperands || binary.Left.Type == PrimitiveType.Bool)
                    && binary.Type == binary.Left.Type,
            TokenKind.AmpersandAmpersand or TokenKind.PipePipe =>
                operands && binary.Left.Type == PrimitiveType.Bool
                    && binary.Type == PrimitiveType.Bool,
            _ => false,
        };
        if (!binaryValid)
        {
            throw Invalid();
        }
        var verifiedNoFailureArithmetic = RequiresNoFailureProof(function)
            && checkedIntegerOperation && ReferenceEquals(binary, cleanupCounterStep);
        if (RequiresNoFailureProof(function) && checkedIntegerOperation
            && binaryCompletes && !verifiedNoFailureArithmetic)
        {
            throw InvalidNoFailureEffect(function, binary.Span);
        }
        if (checkedIntegerOperation && binaryCompletes && !verifiedNoFailureArithmetic)
        {
            failures?.Add(initialized, fieldMasks);
        }
    }

    private static void Expression(BoundExpression expression, InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function,
        BoundProgram program, ref int remaining, int depth, FailureEdges? failures = null,
        bool statementCompound = false, BoundBinary? cleanupCounterStep = null)
    {
        Count(ref remaining, depth);
        if (expression is null || !ValidType(expression.Type,
            allowVoid: expression is BoundCall or BoundIndirectCall
                or BoundUnsafeExpression { Type: PrimitiveType.Void }
                or BoundConstant { Type: PrimitiveType.Void }
                or BoundRead { Type: PrimitiveType.Void }
                or BoundConditional { Type: PrimitiveType.Void }
                or BoundNativeFixedArrayElement { Type: PrimitiveType.Void }
                or BoundNativeFixedArrayPathElement { Type: PrimitiveType.Void }
                or BoundNativeFixedArrayWrite or BoundNativeFixedArrayPathWrite
                or BoundNativePointerWrite or BoundReferenceWrite, program))
        {
            throw Invalid();
        }
        switch (expression)
        {
            case BoundConstant constant:
                if (constant.Type == PrimitiveType.NativeUtf16ZPointer)
                {
                    if (constant.Value < 0 || constant.Value >= program.NativeUtf16ZLiterals.Length)
                    {
                        throw InvalidEffect(constant.Span, "NativeImport");
                    }
                }
                else if (constant.Type == PrimitiveType.NativeUtf8ZPointer)
                {
                    if (constant.Value < 0 || constant.Value >= program.NativeUtf8ZLiterals.Length)
                    {
                        throw InvalidEffect(constant.Span, "NativeImport");
                    }
                }
                else if (constant.Type == PrimitiveType.NativeRawPointer)
                {
                    if (constant.Value != 0)
                    {
                        throw Invalid();
                    }
                }
                else if (NativePointerTypeContract.IsValidType(constant.Type, program.NativePointerTypes))
                {
                    if (constant.Value != 0)
                    {
                        throw Invalid();
                    }
                }
                else if (!constant.Bits.IsCanonicalFor(constant.Type)
                    || (constant.Type is not (PrimitiveType.Void or PrimitiveType.Int8 or PrimitiveType.Int16
                        or PrimitiveType.Int32 or PrimitiveType.UInt8 or PrimitiveType.UInt16
                        or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
                        or PrimitiveType.Float32 or PrimitiveType.Float64
                        or PrimitiveType.Char8 or PrimitiveType.Char16 or PrimitiveType.Char32
                        or PrimitiveType.Rune or PrimitiveType.Bool or PrimitiveType.Int128 or PrimitiveType.UInt128
                        or PrimitiveType.Float16 or PrimitiveType.BFloat16 or PrimitiveType.Float128
                        or PrimitiveType.Decimal32 or PrimitiveType.Decimal64 or PrimitiveType.Decimal128)
                        && !BoundEnumTypes.IsValidType(constant.Type, program.EnumDefinitions))
                    || (constant.Type is PrimitiveType.Int32 or PrimitiveType.UInt32
                        && constant.Value is < int.MinValue or > int.MaxValue)
                    || (constant.Type == PrimitiveType.Int8 && constant.Value is < sbyte.MinValue or > sbyte.MaxValue)
                    || (constant.Type == PrimitiveType.Int16 && constant.Value is < short.MinValue or > short.MaxValue)
                    || (constant.Type == PrimitiveType.UInt8 && constant.Value is < byte.MinValue or > byte.MaxValue)
                    || (constant.Type == PrimitiveType.UInt16 && constant.Value is < ushort.MinValue or > ushort.MaxValue)
                    || (constant.Type == PrimitiveType.Bool && constant.Value is not (0 or 1))
                    || (constant.Type == PrimitiveType.Char8 && constant.Value is < byte.MinValue or > byte.MaxValue)
                    || (constant.Type == PrimitiveType.Char16 && constant.Value is < 0 or > ushort.MaxValue)
                    || (constant.Type == PrimitiveType.Char32 && constant.Value is < uint.MinValue or > uint.MaxValue)
                    || (constant.Type == PrimitiveType.Rune
                        && (constant.Value is < 0 or > 0x10_FFFF
                            || constant.Value is >= 0xD800 and <= 0xDFFF))
                    || (constant.Type == PrimitiveType.Float32 && constant.Value is < 0 or > uint.MaxValue))
                {
                    throw Invalid();
                }
                break;
            case BoundNativeFunctionAddress address:
                if (!address.IsUnsafeAuthorized
                    || (uint)address.Function >= (uint)program.Functions.Length
                    || !NativeFunctionPointerContract.MatchesAddressableFunction(
                        program.Functions[address.Function], address.Type,
                        program.NativeFunctionPointerSignatures))
                {
                    throw InvalidEffect(expression.Span, "NativeFunctionPointer");
                }
                break;
            case BoundNativePointerAddress address:
                if (!address.IsUnsafeAuthorized
                    || !NativePointerTypeContract.IsValidType(address.Type, program.NativePointerTypes)
                    || (uint)address.Variable >= (uint)function.Variables.Length
                    || !declarations.Contains(address.Variable)
                    || initialized.HasRetiredValue(address.Variable))
                {
                    throw InvalidEffect(address.Span, "NativePointer");
                }
                var pointerDefinition = program.NativePointerTypes[NativePointerTypes.Index(address.Type)];
                var addressedVariable = function.Variables[address.Variable];
                if (pointerDefinition.PointeeType != addressedVariable.Type
                    || !DirectNativeImportContract.IsInputPointee(addressedVariable.Type, program.Structures)
                    || (pointerDefinition.IsWritable && !addressedVariable.IsMutable))
                {
                    throw InvalidEffect(address.Span, "NativePointer");
                }
                break;
            case BoundNativeFixedArrayPointerAddress pointerAddress:
                VerifyNativeFixedArrayPointerAddress(pointerAddress, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures);
                break;
            case BoundNativePointerRead pointerRead:
                if (!pointerRead.IsUnsafeAuthorized
                    || !NativePointerTypeContract.IsValidType(pointerRead.Address.Type, program.NativePointerTypes))
                {
                    throw InvalidEffect(pointerRead.Span, "NativePointer");
                }
                var readPointerDefinition =
                    program.NativePointerTypes[NativePointerTypes.Index(pointerRead.Address.Type)];
                if (readPointerDefinition.PointeeType == PrimitiveType.Void
                    || readPointerDefinition.PointeeType != pointerRead.Type
                    || !DirectNativeImportContract.IsCopyablePointee(pointerRead.Type, program.Structures))
                {
                    throw InvalidEffect(pointerRead.Span, "NativePointer");
                }
                if (initialized.Dependencies is BackingDependencies nativePointerDependencies)
                {
                    foreach (var backing in NativePointerBackingOf(pointerRead.Address,
                        nativePointerDependencies, program, ref remaining, depth + 1))
                    {
                        if (backing.Kind != ProvenanceAnchorKind.NativeStorage
                            || (uint)backing.Identity >= (uint)initialized.Length)
                        {
                            throw InvalidEffect(pointerRead.Span, "NativePointer");
                        }
                        if (backing.Storage is StorageKey storage
                            && storage.IsKnown
                            && (storage.Activation != nativePointerDependencies.FunctionActivation
                                || HasUnknownStorageInstances(
                                    nativePointerDependencies.StorageInstanceSet(backing.Identity))
                                || !nativePointerDependencies.StorageInstanceSet(backing.Identity)
                                    .Contains(storage.Instance)))
                        {
                            throw InvalidEffect(pointerRead.Span, "NativePointer");
                        }
                        if (initialized.HasRetiredValue(backing.Identity))
                        {
                            throw InvalidEffect(pointerRead.Span, "NativePointer");
                        }
                        if (initialized.HasNativeOutputState(backing.Identity)
                            && !initialized[backing.Identity])
                        {
                            throw InvalidEffect(pointerRead.Span, "NativeImport");
                        }
                    }
                }
                Expression(pointerRead.Address, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                break;
            case BoundReferenceFormation formation:
                if (!CheckedReferenceTypeContract.IsValidType(
                        formation.Type, program.CheckedReferenceTypes))
                {
                    throw InvalidEffect(formation.Span, "CheckedReference");
                }
                var formedReference = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(formation.Type)];
                var incomingWritable = false;
                var hasIncomingPlace = TryGetIncomingReferencePlace(
                    formation.Place, function, program, ref remaining,
                    depth + 1, out _, out incomingWritable);
                var hasNormalBackingPlace = !hasIncomingPlace
                    && TryGetNormalBackingClassPlace(
                        formation.Place, function, program, out _, out _);
                var hasLocalPlace = !hasIncomingPlace && !hasNormalBackingPlace
                    && TryGetReferencePlaceStorage(
                        formation.Place, function, program, out _, out _);
                var placeWritable = hasLocalPlace
                    ? IsWritableReferencePlace(formation.Place, function, program)
                    : hasNormalBackingPlace
                        ? IsWritableReferencePlace(formation.Place, function, program)
                        : incomingWritable;
                if (formedReference.IsGarbageCollected || formedReference.IsInitialization
                    || formedReference.IsOptional
                    || formation.Place.Type != formedReference.ReferentType
                    || (!hasLocalPlace && !hasNormalBackingPlace && !hasIncomingPlace)
                    || (formedReference.IsWritable && !placeWritable))
                {
                    throw InvalidEffect(formation.Span, "CheckedReference");
                }
                if (initialized.Dependencies is not BackingDependencies formationDependencies)
                {
                    throw InvalidEffect(formation.Span, "CheckedReference");
                }
                ExpressionReferencePlace(formation.Place, initialized, fieldMasks,
                    declarations, function, program, ref remaining, depth + 1,
                    failures);
                foreach (var referenceTarget in ReferenceTargetsOfExpression(
                    formation, formationDependencies, initialized, function,
                    program, ref remaining, depth + 1, fieldMasks))
                {
                    ValidateRawUnionReferenceFieldFormation(formation,
                        referenceTarget, initialized, function, program,
                        ref remaining, depth + 1);
                }
                break;
            case BoundReferenceReborrow reborrow:
                if (!CheckedReferenceTypeContract.IsValidType(
                        reborrow.Type, program.CheckedReferenceTypes)
                    || !CheckedReferenceTypeContract.IsValidType(
                        reborrow.Descriptor.Type, program.CheckedReferenceTypes))
                {
                    throw InvalidEffect(reborrow.Span, "CheckedReference");
                }
                var reborrowType = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(reborrow.Type)];
                var sourceReference = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(reborrow.Descriptor.Type)];
                if (reborrowType.IsGarbageCollected || reborrowType.IsInitialization
                    || reborrowType.IsOptional || sourceReference.IsGarbageCollected
                    || sourceReference.IsInitialization || sourceReference.IsOptional
                    || reborrowType.ReferentType != sourceReference.ReferentType
                    || (reborrowType.IsWritable && !sourceReference.IsWritable)
                    || initialized.Dependencies is not BackingDependencies reborrowDependencies)
                {
                    throw InvalidEffect(reborrow.Span, "CheckedReference");
                }
                Expression(reborrow.Descriptor, initialized, fieldMasks, declarations, function,
                    program, ref remaining, depth + 1, failures);
                _ = ReferenceTargetsOfExpression(reborrow, reborrowDependencies,
                    initialized, function, program, ref remaining, depth + 1,
                    fieldMasks);
                break;
            case BoundReferenceRead referenceRead:
                if (!CheckedReferenceTypeContract.IsValidType(
                        referenceRead.Descriptor.Type, program.CheckedReferenceTypes))
                {
                    throw InvalidEffect(referenceRead.Span, "CheckedReference");
                }
                var readReference = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(referenceRead.Descriptor.Type)];
                if (readReference.IsGarbageCollected || readReference.IsInitialization
                    || readReference.IsOptional || readReference.ReferentType != referenceRead.Type)
                {
                    throw InvalidEffect(referenceRead.Span, "CheckedReference");
                }
                if (initialized.Dependencies is not BackingDependencies readDependencies)
                {
                    throw InvalidEffect(referenceRead.Span, "CheckedReference");
                }
                var referenceReadCompletes = CanCompleteNormally(
                    referenceRead.Descriptor, initialized, program,
                    ref remaining, depth + 1);
                Expression(referenceRead.Descriptor, initialized, fieldMasks, declarations, function,
                    program, ref remaining, depth + 1, failures);
                if (referenceReadCompletes)
                {
                    foreach (var referenceTarget in ReferenceTargetsOfExpression(
                        referenceRead.Descriptor, readDependencies, initialized,
                        function, program, ref remaining, depth + 1, fieldMasks))
                    {
                        ValidateReferenceTarget(referenceTarget, readReference.ReferentType,
                            readDependencies, initialized, function, program,
                            referenceRead.Span, fieldMasks);
                    }
                }
                break;
            case BoundReferenceWrite referenceWrite:
                if (!CheckedReferenceTypeContract.IsValidType(
                        referenceWrite.Descriptor.Type, program.CheckedReferenceTypes))
                {
                    throw InvalidEffect(referenceWrite.Span, "CheckedReference");
                }
                var writeReference = program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(referenceWrite.Descriptor.Type)];
                var uniqueStore = IsUnique(program, referenceWrite.Value.Type);
                if (!writeReference.IsWritable || writeReference.IsGarbageCollected
                    || writeReference.IsInitialization || writeReference.IsOptional
                    || writeReference.ReferentType != referenceWrite.Value.Type
                    || !CheckedReferenceTypeContract.IsSupportedReferenceStoreValueType(
                        referenceWrite.Value.Type, program.Structures, program.Classes,
                        program.EnumDefinitions.Length, program.CheckedReferenceTypes)
                    || (uniqueStore
                        && referenceWrite.Value is not (BoundMove or BoundFieldMove or BoundCall)))
                {
                    throw InvalidEffect(referenceWrite.Span,
                        uniqueStore ? "Ownership" : "CheckedReference");
                }
                if (uniqueStore && CleanupMayFail(referenceWrite.Value.Type, program))
                {
                    throw InvalidEffect(referenceWrite.Span, "Ownership");
                }
                if (initialized.Dependencies is not BackingDependencies writeDependencies)
                {
                    throw InvalidEffect(referenceWrite.Span, "CheckedReference");
                }
                var referenceWriteDescriptorCompletes = CanCompleteNormally(referenceWrite.Descriptor,
                    initialized, program, ref remaining, depth + 1);
                Expression(referenceWrite.Descriptor, initialized, fieldMasks, declarations, function,
                    program, ref remaining, depth + 1, failures);
                var referenceWriteTargets = referenceWriteDescriptorCompletes
                    ? ReferenceTargetsOfExpression(referenceWrite.Descriptor,
                        writeDependencies, initialized, function, program,
                        ref remaining, depth + 1, fieldMasks)
                    : new HashSet<ProvenanceAnchor>();
                if (referenceWriteDescriptorCompletes)
                {
                    foreach (var referenceTarget in referenceWriteTargets)
                    {
                        ValidateReferenceTarget(referenceTarget, writeReference.ReferentType,
                            writeDependencies, initialized, function, program,
                            referenceWrite.Span, fieldMasks);
                    }
                }
                var valueCompletes = referenceWriteDescriptorCompletes
                    && CanCompleteNormally(referenceWrite.Value,
                        initialized, program, ref remaining, depth + 1);
                Expression(referenceWrite.Value, initialized, fieldMasks, declarations, function,
                    program, ref remaining, depth + 1, failures);
                if (valueCompletes)
                {
                    foreach (var referenceTarget in referenceWriteTargets)
                    {
                        ValidateReferenceTarget(referenceTarget, writeReference.ReferentType,
                            writeDependencies, initialized, function, program,
                            referenceWrite.Span, fieldMasks);
                    }
                    foreach (var referenceTarget in referenceWriteTargets)
                    {
                        _ = writeDependencies
                            .InvalidateCopyOriginEntryReferent(
                                referenceTarget, program, ref remaining,
                                depth + 1);
                    }

                    if (uniqueStore)
                    {
                        foreach (var referenceTarget in referenceWriteTargets)
                        {
                            if (referenceTarget.Kind != ProvenanceAnchorKind.ReferencePlace)
                            {
                                continue;
                            }

                            var projection = referenceTarget.Storage?.Projection
                                ?? throw Invalid(referenceWrite.Span);
                            var preservedTargets = new HashSet<ProvenanceAnchor> {
                                referenceTarget,
                            };
                            CheckReferencePlaceInvalidation(referenceTarget.Identity,
                                projection, initialized, referenceWrite.Span,
                                preservedTargets);
                            if (projection.Kind == StorageProjectionKind.WholeObject)
                            {
                                CheckBackingInvalidation(referenceTarget.Identity,
                                    initialized, referenceWrite.Span, preservedTargets);
                            }
                            else if (projection.Kind == StorageProjectionKind.InlineFieldPath)
                            {
                                CheckBackingFieldInvalidation(referenceTarget.Identity,
                                    projection, initialized, referenceWrite.Span,
                                    preservedTargets);
                            }
                            else
                            {
                                throw InvalidEffect(referenceWrite.Span, "Ownership");
                            }
                        }
                    }

                    if (TokenSpanContract.IsDependent(
                            writeReference.ReferentType, program.Structures))
                    {
                        var backingSources = BackingOf(referenceWrite.Value,
                            writeDependencies, program, ref remaining, depth + 1);
                        if (backingSources.Count == 0)
                        {
                            throw InvalidEffect(referenceWrite.Span, "Ownership");
                        }
                        foreach (var referenceTarget in referenceWriteTargets)
                        {
                            if (referenceTarget.Kind == ProvenanceAnchorKind.ReferencePlace)
                            {
                                var projection = referenceTarget.Storage?.Projection
                                    ?? throw Invalid(referenceWrite.Span);
                                if (projection.Kind == StorageProjectionKind.WholeObject)
                                {
                                    writeDependencies.SetBackingSources(
                                        referenceTarget.Identity, backingSources);
                                    if (IsInlineStructStorage(
                                            writeReference.ReferentType, program))
                                    {
                                        SetBackingFieldValuesFromExpression(
                                            referenceTarget.Identity, projection,
                                            writeReference.ReferentType, referenceWrite.Value,
                                            writeDependencies, function, program,
                                            ref remaining, depth + 1);
                                    }
                                    RefreshBackingAggregateProvenance(
                                        referenceTarget.Identity,
                                        function.Variables[referenceTarget.Identity].Type,
                                        writeDependencies, program,
                                        ref remaining, depth + 1);
                                }
                                else if (projection.Kind == StorageProjectionKind.InlineFieldPath)
                                {
                                    SetBackingFieldValuesFromExpression(
                                        referenceTarget.Identity, projection,
                                        writeReference.ReferentType, referenceWrite.Value,
                                        writeDependencies, function, program,
                                        ref remaining, depth + 1);
                                    RefreshBackingAggregateProvenance(
                                        referenceTarget.Identity,
                                        function.Variables[referenceTarget.Identity].Type,
                                        writeDependencies, program,
                                        ref remaining, depth + 1);
                                }
                                else
                                {
                                    throw InvalidEffect(referenceWrite.Span, "Ownership");
                                }
                            }
                            else if (referenceTarget.Kind == ProvenanceAnchorKind.IncomingReference)
                            {
                                VerifyReferenceWriteCapture(function, referenceTarget.Identity,
                                    backingSources, referenceWrite.Span);
                                writeDependencies.SetReferenceBackingSources(
                                    referenceTarget.Identity, backingSources);
                            }
                            else
                            {
                                throw InvalidEffect(referenceWrite.Span, "Ownership");
                            }
                        }
                    }
                    else if (IsOrdinaryStoredReferenceType(
                        writeReference.ReferentType, program))
                    {
                        var storedReference = ReferenceFieldValueOfDescriptorExpression(
                            referenceWrite.Value, writeDependencies, initialized,
                            function, program, ref remaining, depth + 1,
                            fieldMasks);
                        if (!storedReference.IsAvailable
                            || storedReference.Targets.Count == 0)
                        {
                            throw InvalidEffect(referenceWrite.Span,
                                "CheckedReference");
                        }
                        var captureSources = ReferenceCaptureSourcesOfValue(
                            storedReference, referenceWrite.Span);
                        var retainedSources = new HashSet<ProvenanceAnchor>(
                            storedReference.Targets);
                        retainedSources.UnionWith(storedReference.Obligations);
                        retainedSources.UnionWith(storedReference.BackingSources);
                        foreach (var referenceTarget in referenceWriteTargets)
                        {
                            if (referenceTarget.Kind == ProvenanceAnchorKind.ReferencePlace)
                            {
                                var projection = referenceTarget.Storage?.Projection
                                    ?? throw Invalid(referenceWrite.Span);
                                if (projection.Kind == StorageProjectionKind.WholeObject)
                                {
                                    writeDependencies.SetReferenceTargets(
                                        referenceTarget.Identity,
                                        storedReference.Targets);
                                    writeDependencies.SetReferenceObligations(
                                        referenceTarget.Identity,
                                        storedReference.Obligations);
                                    writeDependencies.SetReferenceBackingSources(
                                        referenceTarget.Identity,
                                        storedReference.BackingSources);
                                }
                                else if (projection.Kind
                                    == StorageProjectionKind.InlineFieldPath)
                                {
                                    writeDependencies.SetReferenceFieldValue(
                                        referenceTarget.Identity, projection,
                                        storedReference);
                                }
                                else
                                {
                                    throw InvalidEffect(referenceWrite.Span,
                                        "Ownership");
                                }
                            }
                            else if (referenceTarget.Kind is
                                ProvenanceAnchorKind.IncomingReference
                                    or ProvenanceAnchorKind.IncomingReferenceContents
                                    or ProvenanceAnchorKind.IncomingReferenceField
                                    or ProvenanceAnchorKind.IncomingReferencePlace)
                            {
                                if (storedReference.Targets.Any(target =>
                                    target.Kind is ProvenanceAnchorKind.IncomingReference
                                        or ProvenanceAnchorKind.IncomingReferenceContents
                                        or ProvenanceAnchorKind.IncomingReferenceField
                                        or ProvenanceAnchorKind.IncomingReferencePlace
                                    && IsForwardedCaptureSource(function,
                                        referenceTarget.Identity, target.Identity)))
                                {
                                    throw InvalidEffect(referenceWrite.Span,
                                        "CheckedReference");
                                }
                                VerifyReferenceWriteCapture(function,
                                    referenceTarget.Identity, captureSources,
                                    referenceWrite.Span);
                                if (referenceTarget.Kind
                                    == ProvenanceAnchorKind.IncomingReferencePlace)
                                {
                                    var targetProjection =
                                        referenceTarget.IncomingProjection
                                        ?? throw Invalid(referenceWrite.Span);
                                    if (referenceTarget.IncomingReferenceDepth == 0)
                                    {
                                        if (referenceWriteTargets.Count == 1)
                                        {
                                            writeDependencies.SetReferenceFieldValue(
                                                referenceTarget.Identity,
                                                targetProjection, storedReference);
                                        }
                                        else
                                        {
                                            _ = writeDependencies
                                                .TryGetReferenceFieldValue(
                                                    referenceTarget.Identity,
                                                    targetProjection,
                                                    out var previousValue);
                                            _ = previousValue.Merge(storedReference);
                                            writeDependencies.SetReferenceFieldValue(
                                                referenceTarget.Identity,
                                                targetProjection, previousValue);
                                        }
                                    }
                                    else if (referenceWriteTargets.Count == 1)
                                    {
                                        writeDependencies.SetIncomingReferenceFieldValue(
                                            referenceTarget, storedReference);
                                    }
                                    else
                                    {
                                        _ = writeDependencies
                                            .TryGetIncomingReferenceFieldValue(
                                                referenceTarget, out var previousValue);
                                        _ = previousValue.Merge(storedReference);
                                        writeDependencies.SetIncomingReferenceFieldValue(
                                            referenceTarget, previousValue);
                                    }
                                }
                                writeDependencies.AddReferenceBackingSources(
                                    referenceTarget.Identity, retainedSources);
                            }
                            else
                            {
                                throw InvalidEffect(referenceWrite.Span,
                                    "CheckedReference");
                            }
                        }
                    }
                    else
                    {
                        var referenceLeaves = ReferenceFieldLeaves(
                            writeReference.ReferentType, program,
                            ref remaining, depth + 1);
                        if (referenceLeaves.Count != 0)
                        {
                            var storedReferences = ReferenceFieldValuesOfExpression(
                                referenceWrite.Value, writeReference.ReferentType,
                                writeDependencies, initialized, function, program,
                                ref remaining, depth + 1, fieldMasks);
                            foreach (var (fieldProjection, _) in referenceLeaves)
                            {
                                if (!storedReferences.TryGetValue(
                                        fieldProjection, out var storedReference)
                                    || !storedReference.IsAvailable
                                    || storedReference.Targets.Count == 0)
                                {
                                    throw InvalidEffect(referenceWrite.Span,
                                        "CheckedReference");
                                }

                                foreach (var referenceTarget in referenceWriteTargets)
                                {
                                    if (referenceTarget.Kind
                                        == ProvenanceAnchorKind.ReferencePlace)
                                    {
                                        var destinationProjection =
                                            referenceTarget.Storage?.Projection
                                            ?? throw Invalid(referenceWrite.Span);
                                        if (destinationProjection.Kind is not (
                                                StorageProjectionKind.WholeObject
                                                or StorageProjectionKind.InlineFieldPath))
                                        {
                                            throw InvalidEffect(referenceWrite.Span,
                                                "Ownership");
                                        }
                                        if (referenceWriteTargets.Count == 1)
                                        {
                                            var destinationFieldProjection =
                                                StorageProjection.Combine(
                                                    destinationProjection,
                                                    fieldProjection);
                                            if (destinationFieldProjection.Kind
                                                != StorageProjectionKind.InlineFieldPath)
                                            {
                                                throw InvalidEffect(referenceWrite.Span,
                                                    "Ownership");
                                            }
                                            writeDependencies.SetReferenceFieldValue(
                                                referenceTarget.Identity,
                                                destinationFieldProjection,
                                                storedReference);
                                        }
                                        else
                                        {
                                            var destinationFieldProjection =
                                                StorageProjection.Combine(
                                                    destinationProjection,
                                                    fieldProjection);
                                            if (destinationFieldProjection.Kind
                                                != StorageProjectionKind.InlineFieldPath)
                                            {
                                                throw InvalidEffect(referenceWrite.Span,
                                                    "Ownership");
                                            }
                                            _ = writeDependencies.TryGetReferenceFieldValue(
                                                referenceTarget.Identity,
                                                destinationFieldProjection,
                                                out var previousValue);
                                            _ = previousValue.Merge(storedReference);
                                            writeDependencies.SetReferenceFieldValue(
                                                referenceTarget.Identity,
                                                destinationFieldProjection,
                                                previousValue);
                                        }
                                    }
                                    else if (referenceTarget.Kind is
                                        ProvenanceAnchorKind.IncomingReference
                                            or ProvenanceAnchorKind.IncomingReferenceContents
                                            or ProvenanceAnchorKind.IncomingReferenceField
                                            or ProvenanceAnchorKind.IncomingReferencePlace)
                                    {
                                        if (storedReference.Targets.Any(target =>
                                                target.Kind is
                                                    ProvenanceAnchorKind.IncomingReference
                                                    or ProvenanceAnchorKind.IncomingReferenceContents
                                                    or ProvenanceAnchorKind.IncomingReferenceField
                                                    or ProvenanceAnchorKind.IncomingReferencePlace
                                                && IsForwardedCaptureSource(function,
                                                    referenceTarget.Identity,
                                                    target.Identity)))
                                        {
                                            throw InvalidEffect(referenceWrite.Span,
                                                "CheckedReference");
                                        }
                                        VerifyReferenceWriteCapture(function,
                                            referenceTarget.Identity,
                                            ReferenceCaptureSourcesOfValue(
                                                storedReference, referenceWrite.Span),
                                            referenceWrite.Span);
                                        if (referenceTarget.Kind
                                            == ProvenanceAnchorKind.IncomingReferencePlace
                                            && referenceTarget.IncomingProjection
                                                is StorageProjection destinationPrefix)
                                        {
                                            var destinationProjection =
                                                StorageProjection.Combine(
                                                    destinationPrefix,
                                                    fieldProjection);
                                            if (destinationProjection.Kind
                                                != StorageProjectionKind.InlineFieldPath)
                                            {
                                                throw InvalidEffect(
                                                    referenceWrite.Span,
                                                    "Ownership");
                                            }
                                            var destination = ProvenanceAnchor
                                                .ForIncomingReferencePlace(
                                                    referenceTarget.Identity,
                                                    destinationProjection,
                                                    referenceTarget
                                                        .IncomingReferenceDepth);
                                            if (referenceTarget.IncomingReferenceDepth == 0)
                                            {
                                                if (referenceWriteTargets.Count == 1)
                                                {
                                                    writeDependencies
                                                        .SetReferenceFieldValue(
                                                            referenceTarget.Identity,
                                                            destinationProjection,
                                                            storedReference);
                                                }
                                                else
                                                {
                                                    _ = writeDependencies
                                                        .TryGetReferenceFieldValue(
                                                            referenceTarget.Identity,
                                                            destinationProjection,
                                                            out var previousValue);
                                                    _ = previousValue.Merge(
                                                        storedReference);
                                                    writeDependencies
                                                        .SetReferenceFieldValue(
                                                            referenceTarget.Identity,
                                                            destinationProjection,
                                                            previousValue);
                                                }
                                            }
                                            else if (referenceWriteTargets.Count == 1)
                                            {
                                                writeDependencies
                                                    .SetIncomingReferenceFieldValue(
                                                        destination,
                                                        storedReference);
                                            }
                                            else
                                            {
                                                _ = writeDependencies
                                                    .TryGetIncomingReferenceFieldValue(
                                                        destination,
                                                        out var previousValue);
                                                _ = previousValue.Merge(
                                                    storedReference);
                                                writeDependencies
                                                    .SetIncomingReferenceFieldValue(
                                                        destination,
                                                        previousValue);
                                            }
                                        }
                                        var retainedSources =
                                            new HashSet<ProvenanceAnchor>(
                                                storedReference.Targets);
                                        retainedSources.UnionWith(
                                            storedReference.Obligations);
                                        retainedSources.UnionWith(
                                            storedReference.BackingSources);
                                        writeDependencies.AddReferenceBackingSources(
                                            referenceTarget.Identity,
                                            retainedSources);
                                    }
                                    else
                                    {
                                        throw InvalidEffect(referenceWrite.Span,
                                            "CheckedReference");
                                    }
                                }
                            }
                        }
                    }
                }
                break;
            case BoundNativePointerWrite pointerWrite:
                if (!pointerWrite.IsUnsafeAuthorized
                    || !NativePointerTypeContract.IsValidType(pointerWrite.Address.Type, program.NativePointerTypes))
                {
                    throw InvalidEffect(pointerWrite.Span, "NativePointer");
                }
                var writePointerDefinition =
                    program.NativePointerTypes[NativePointerTypes.Index(pointerWrite.Address.Type)];
                if (!writePointerDefinition.IsWritable
                    || writePointerDefinition.PointeeType == PrimitiveType.Void
                    || writePointerDefinition.PointeeType != pointerWrite.Value.Type
                    || !DirectNativeImportContract.IsCopyablePointee(pointerWrite.Value.Type, program.Structures))
                {
                    throw InvalidEffect(pointerWrite.Span, "NativePointer");
                }
                if (!EvaluateExpression(pointerWrite.Address, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures))
                {
                    ValidateUnreachableExpression(pointerWrite.Value, initialized, fieldMasks,
                        declarations, function, program, ref remaining, depth + 1);
                    break;
                }
                if (!EvaluateExpression(pointerWrite.Value, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures))
                {
                    break;
                }
                if (initialized.Dependencies is BackingDependencies pointerWriteDependencies)
                {
                    ApplyNativePointerWrite(pointerWrite.Address, pointerWrite.Value,
                        pointerWriteDependencies, program, ref remaining, depth + 1);
                }
                initialized.InvalidateNativeOutputCorrelations();
                break;
            case BoundNativeFixedArrayInitialization
                or BoundNativeFixedArrayRepeatInitialization:
                throw InvalidEffect(expression.Span, "NativeImport");
            case BoundNativeStorageAddress or BoundNativeOutputReservation:
                throw InvalidEffect(expression.Span, "NativeImport");
            case BoundRead read when NativeFixedArrayContract.IsCarrier(read.Type):
                throw InvalidEffect(read.Span, "NativeImport");
            case BoundNativeFixedArrayElement fixedArrayElement:
                VerifyNativeFixedArrayElement(fixedArrayElement, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures);
                break;
            case BoundNativeFixedArrayPathElement pathElement:
                VerifyNativeFixedArrayPathElement(pathElement, initialized,
                    fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                break;
            case BoundNativeFixedArrayWrite fixedArrayWrite:
                VerifyNativeFixedArrayWrite(fixedArrayWrite, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures);
                break;
            case BoundNativeFixedArrayPathWrite pathWrite:
                VerifyNativeFixedArrayPathWrite(pathWrite, initialized,
                    fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                break;
            case BoundNull:
                if (!IsSupportedOptionalType(expression.Type, program))
                {
                    throw Invalid();
                }
                break;
            case BoundUnsafeExpression unsafeExpression:
                Expression(unsafeExpression.Expression, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures, statementCompound, cleanupCounterStep);
                break;
            case BoundOptionalLift lift:
                if (!TryGetOptionalPayloadType(lift.Type, program, out var liftedType)
                    || lift.Value is null || lift.Value.Type != liftedType)
                {
                    throw Invalid();
                }
                Expression(lift.Value, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                break;
            case BoundCoalesce coalesce:
            {
                if (coalesce.Optional is null || coalesce.Fallback is null
                    || !TryGetOptionalPayloadType(
                        coalesce.Optional.Type, program, out var coalescePayloadType)
                    || (coalesce.Type != coalescePayloadType
                        && !SourceOptionalTypes.IsReadonlyProjection(
                            coalescePayloadType, coalesce.Type, program.CheckedReferenceTypes)
                        && !(IsSupportedOptionalType(coalesce.Type, program)
                            && (coalesce.Type == coalesce.Optional.Type
                                || SourceOptionalTypes.IsReadonlyProjection(
                                    coalesce.Optional.Type, coalesce.Type,
                                    program.CheckedReferenceTypes))))
                    || coalesce.Fallback.Type != coalesce.Type)
                {
                    throw Invalid();
                }
                if (!EvaluateExpression(coalesce.Optional, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures))
                {
                    ValidateUnreachableExpression(coalesce.Fallback, initialized, fieldMasks,
                        declarations, function, program, ref remaining, depth + 1);
                    break;
                }
                var optionalPresence = KnownOptionalPresence(coalesce.Optional, initialized, ref remaining);
                var presentPossible = optionalPresence != false;
                var absentPossible = optionalPresence != true;
                using var repeatSnapshotState
                    = new RepeatSnapshotReservationScope(
                        initialized.RepeatResources);
                var presentState = initialized.Clone(repeatSnapshotState);
                var presentMasks = (uint[])fieldMasks.Clone();
                var absentState = initialized.Clone(repeatSnapshotState);
                var absentMasks = (uint[])fieldMasks.Clone();
                var absentDeclarations = new HashSet<int>(declarations);
                var absentCompletes = absentPossible && EvaluateExpression(coalesce.Fallback,
                    absentState, absentMasks, absentDeclarations, function, program, ref remaining,
                    depth + 1, coalesce.Optional is BoundOptionalLift ? null : failures);
                if (!absentPossible)
                {
                    ValidateUnreachableExpression(coalesce.Fallback, absentState, absentMasks,
                        absentDeclarations, function, program, ref remaining, depth + 1);
                }
                declarations.UnionWith(absentDeclarations);
                if (presentPossible && absentCompletes)
                {
                    for (var index = 0; index < initialized.Length; index++)
                    {
                        initialized.Merge(index, absentState);
                        fieldMasks[index] = presentMasks[index] & absentMasks[index];
                    }
                    _ = initialized.MergeNativeOutputState(
                        presentState, true, absentState, true);
                }
                else if (presentPossible)
                {
                    presentState.CopyTo(initialized);
                    Array.Copy(presentMasks, fieldMasks, fieldMasks.Length);
                    _ = initialized.MergeNativeOutputState(
                        presentState, true, absentState, false);
                }
                else if (absentCompletes)
                {
                    absentState.CopyTo(initialized);
                    Array.Copy(absentMasks, fieldMasks, fieldMasks.Length);
                    _ = initialized.MergeNativeOutputState(
                        presentState, false, absentState, true);
                }
                break;
            }
            case BoundReadonlyProjection projection:
                if (projection.Value is null
                    || !SourceOptionalTypes.IsReadonlyProjection(
                        projection.Value.Type, projection.Type,
                        program.CheckedReferenceTypes))
                {
                    throw Invalid();
                }
                Expression(projection.Value, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                break;
            case BoundIsNull test:
                if (test.Value is null || !IsSupportedOptionalType(test.Value.Type, program))
                {
                    throw Invalid();
                }
                Expression(test.Value, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                break;
            case BoundIsInitialized initializationTest:
                if (initializationTest.Type != PrimitiveType.Bool
                    || initializationTest.Variable < 0 || initializationTest.Variable >= initialized.Length
                    || initialized.IsNativeOutputReserved(initializationTest.Variable)
                    || (initializationTest.Variable >= function.ParameterCount
                        + (function.IsConstructor ? 1 : 0) && !declarations.Contains(initializationTest.Variable)))
                {
                    throw InvalidEffect(initializationTest.Span, "NativeImport");
                }
                break;
            case BoundIsSome pattern:
                if (pattern.Value is null
                    || !TryGetOptionalPayloadType(pattern.Value.Type, program, out var somePayloadType)
                    || pattern.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    || pattern.Variable < 0 || pattern.Variable >= initialized.Length
                    || function.Variables[pattern.Variable].Type != somePayloadType
                    || function.Variables[pattern.Variable].IsOut
                    || function.Variables[pattern.Variable].IsMutable
                    || declarations.Contains(pattern.Variable))
                {
                    throw Invalid();
                }
                Expression(pattern.Value, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                SetOptionalReferencePatternFacts(pattern.Value, pattern.Variable,
                    initialized, fieldMasks, function, program, ref remaining);
                _ = declarations.Add(pattern.Variable);
                break;
            case BoundIsVariablePattern pattern:
                var patternValue = pattern.Value;
                var optionalPattern = patternValue is not null
                    && IsSupportedOptionalType(patternValue.Type, program);
                var optionalPayloadType = default(PrimitiveType);
                var hasOptionalPayload = patternValue is not null && optionalPattern
                    && TryGetOptionalPayloadType(
                        patternValue.Type, program, out optionalPayloadType);
                var supportedValuePattern = patternValue?.Type is PrimitiveType.Bool
                    or PrimitiveType.Char16 or PrimitiveType.Int32 or PrimitiveType.UInt32
                    or PrimitiveType.Int64 or PrimitiveType.UInt64;
                if (patternValue is null || (!optionalPattern && !supportedValuePattern)
                    || (optionalPattern && !pattern.IncludeNull && patternValue is not BoundRead)
                    || pattern.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    || pattern.Variable < 0 || pattern.Variable >= initialized.Length
                    || (optionalPattern && !hasOptionalPayload)
                    || function.Variables[pattern.Variable].Type != (optionalPattern && !pattern.IncludeNull
                        ? optionalPayloadType : patternValue.Type)
                    || function.Variables[pattern.Variable].IsOut
                    || function.Variables[pattern.Variable].IsInitializationLocal
                    || declarations.Contains(pattern.Variable))
                {
                    throw Invalid(pattern.Span);
                }
                if (patternValue is BoundRead sourceRead)
                {
                    if (sourceRead.Variable < 0 || sourceRead.Variable >= initialized.Length
                        || (sourceRead.Variable >= function.ParameterCount
                            + (function.IsConstructor ? 1 : 0) && !declarations.Contains(sourceRead.Variable))
                        || function.Variables[sourceRead.Variable].Type != sourceRead.Type)
                    {
                        throw Invalid(pattern.Span);
                    }
                }
                else
                {
                    Expression(patternValue, initialized, fieldMasks, declarations, function, program,
                        ref remaining, depth + 1, failures);
                }
                SetOptionalReferencePatternFacts(patternValue, pattern.Variable,
                    initialized, fieldMasks, function, program, ref remaining);
                _ = declarations.Add(pattern.Variable);
                break;
            case BoundCast cast:
                var castOperandCompletes = EvaluateExpression(cast.Operand, initialized,
                    fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                var integerConversion = (PrimitiveTypeFacts.IsInteger(cast.Operand.Type)
                    && (PrimitiveTypeFacts.IsInteger(cast.Type) || cast.Type == PrimitiveType.Char16
                        || cast.Type is PrimitiveType.Int128 or PrimitiveType.UInt128))
                    || (cast.Operand.Type == PrimitiveType.Char16
                        && (PrimitiveTypeFacts.IsInteger(cast.Type)
                            || cast.Type is PrimitiveType.Int128 or PrimitiveType.UInt128));
                var integerToFloatingConversion = (PrimitiveTypeFacts.IsInteger(cast.Operand.Type)
                    || cast.Operand.Type == PrimitiveType.Char16)
                    && (cast.Type is PrimitiveType.Float32 or PrimitiveType.Float64);
                var floatingToIntegerConversion = (cast.Operand.Type is PrimitiveType.Float32 or PrimitiveType.Float64)
                    && (cast.Type is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64
                        or PrimitiveType.UInt64);
                var floatingConversion = (cast.Operand.Type is PrimitiveType.Float32 or PrimitiveType.Float64)
                    && (cast.Type is PrimitiveType.Float32 or PrimitiveType.Float64);
                if (!integerConversion && !integerToFloatingConversion
                    && !floatingToIntegerConversion && !floatingConversion)
                {
                    throw Invalid();
                }
                if (!castOperandCompletes)
                {
                    break;
                }
                if (RequiresNoFailureProof(function) && !IsTotalConversion(cast.Operand.Type, cast.Type))
                {
                    throw InvalidNoFailureEffect(function, cast.Span);
                }
                if (!IsTotalConversion(cast.Operand.Type, cast.Type))
                {
                    failures?.Add(initialized, fieldMasks);
                }
                break;
            case BoundFieldRead fieldRead:
                if (fieldRead.Receiver is null)
                {
                    throw Invalid(fieldRead.Span);
                }
                var readType = fieldRead.Receiver.Type;
                if (!HasFields(program, readType)
                    || fieldRead.Field < 0 || fieldRead.Field >= Fields(program, readType).Length
                    || !CanAccessField(function, program, readType, fieldRead.Field)
                    || fieldRead.Type != Fields(program, readType)[fieldRead.Field].Type)
                {
                    throw Invalid();
                }
                if (RequiresUnsafeFieldAccess(program, readType,
                        Fields(program, readType)[fieldRead.Field])
                    && !fieldRead.IsUnsafeAuthorized)
                {
                    throw InvalidEffect(fieldRead.Span, "UnsafeCaller");
                }
                var hasTrackedRootPath = TryGetFieldProjectionPath(fieldRead,
                    function, program, out var fieldRootVariable, out _)
                    && (IsUnique(program,
                            function.Variables[fieldRootVariable].Type)
                        || IsInlineStructStorage(
                            function.Variables[fieldRootVariable].Type,
                            program));
                if (hasTrackedRootPath)
                {
                    if (!IsUnique(program,
                            function.Variables[fieldRootVariable].Type)
                        && !function.Variables[fieldRootVariable]
                            .IsInitializationLocal
                        && !initialized[fieldRootVariable]
                        && !initialized.HasPartialValue(fieldRootVariable))
                    {
                        throw Invalid(fieldRead.Span);
                    }
                    _ = VerifyFieldPathLeafLive(fieldRead, initialized, fieldMasks,
                        function, program, ref remaining, depth + 1);
                }
                else if (fieldRead.Receiver is BoundFieldRead nested && IsUnique(program, nested.Type))
                {
                    _ = VerifyUniqueFieldPlace(nested, initialized, fieldMasks, function, program, ref remaining, depth + 1);
                }
                else if (fieldRead.Receiver is BoundRead partial
                    && (function.IsConstructor || function.IsClassConstructor) && partial.Variable == 0)
                {
                    if (partial.Type != function.Variables[0].Type || (fieldMasks[0] & (1u << fieldRead.Field)) == 0)
                    {
                        throw Invalid(fieldRead.Span);
                    }
                }
                else if (IsUnique(program, readType))
                {
                    throw InvalidEffect(fieldRead.Span, "Ownership");
                }
                else
                {
                    Expression(fieldRead.Receiver, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                }
                if (IsRawUnionType(program, fieldRead.Receiver.Type))
                {
                    var rawUnionField = program.Structures[
                        (int)fieldRead.Receiver.Type - 100]
                        .Fields[fieldRead.Field];
                    var initializedBytes
                        = InitializedPayloadBytesOfExpression(
                            fieldRead.Receiver, fieldRead.Receiver.Type,
                            initialized, function, program);
                    var fieldOffset
                        = MaterializedPayloadOffset(rawUnionField.Offset);
                    var fieldSize
                        = MaterializedPayloadSize(rawUnionField.Size);
                    if (fieldOffset > initializedBytes.Length
                        || fieldSize > initializedBytes.Length - fieldOffset
                        || initializedBytes.AsSpan(fieldOffset, fieldSize)
                            .Contains((byte)0))
                    {
                        throw Invalid(fieldRead.Span);
                    }
                }
                if (IsNativePointerValue(fieldRead.Type, program)
                    && initialized.Dependencies
                        is BackingDependencies pointerFieldDependencies
                    && NativePointerFieldValueInvalidatedOfExpression(
                        fieldRead, fieldRead.Type,
                        StorageProjection.WholeObject,
                        pointerFieldDependencies, function, program,
                        ref remaining, depth + 1))
                {
                    throw InvalidEffect(fieldRead.Span, "NativePointer");
                }
                if (IsUnique(program, fieldRead.Type))
                {
                    throw InvalidEffect(fieldRead.Span, "Ownership");
                }
                break;
            case BoundRead read:
                if (IsUnique(program, read.Type))
                {
                    throw InvalidEffect(read.Span, "Ownership");
                }
                if (read.Variable < 0 || read.Variable >= initialized.Length || !initialized[read.Variable]
                    || read.Type != function.Variables[read.Variable].Type)
                {
                    throw Invalid(read.Span);
                }
                break;
            case BoundSnapshotSpan snapshot:
                if (snapshot.Type != PrimitiveType.TokenSpan
                    || snapshot.Variable < 0 || snapshot.Variable >= initialized.Length
                    || !initialized[snapshot.Variable]
                    || (int)function.Variables[snapshot.Variable].Type < 100
                    || (int)function.Variables[snapshot.Variable].Type - 100 >= program.Structures.Length
                    || !TokenSpanContract.ValidSnapshot(program.Structures[(int)function.Variables[snapshot.Variable].Type - 100])
                    || !TokenSpanContract.ValidTokenLayout(program.Structures)
                    || snapshot.SourceBuffer < 0 || snapshot.SourceBuffer >= program.Buffers.Length
                    || program.Buffers[snapshot.SourceBuffer].Name != "Source"
                    || program.Buffers[snapshot.SourceBuffer].ElementType != PrimitiveType.Char16
                    || program.Buffers[snapshot.SourceBuffer].IsWritable || function.NoFailureProof)
                {
                    throw InvalidEffect(snapshot.Span, "Ownership");
                }
                failures?.Add(initialized, fieldMasks);
                break;
            case BoundSpanLength length:
                if (length.Type != TokenSpanContract.IndexCarrier)
                {
                    throw InvalidEffect(length.Span, "Ownership");
                }
                if (!VerifySpanOperand(length.View, initialized, fieldMasks, declarations, function,
                    program, ref remaining, depth, failures))
                {
                    break;
                }
                break;
            case BoundSpanRead spanRead:
                if (spanRead.Index is null || spanRead.Index.Type != TokenSpanContract.IndexCarrier
                    || !TokenSpanContract.ValidTokenLayout(program.Structures)
                    || spanRead.Type != (PrimitiveType)(100 + TokenSpanContract.Find(program.Structures, TokenSpanContract.TokenName)))
                {
                    throw InvalidEffect(spanRead.Span, "Ownership");
                }
                if (!VerifySpanOperand(spanRead.View, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth, failures))
                {
                    ValidateUnreachableExpression(spanRead.Index, initialized, fieldMasks,
                        declarations, function, program, ref remaining, depth + 1);
                    break;
                }
                var readBacking = BackingOf(spanRead.View, initialized.Dependencies, program, ref remaining, depth + 1);
                initialized.Dependencies?.Staged.Add(new StagedDependencySource(
                    null, -1, [.. readBacking], EmptyBackingSources,
                    EmptyBackingSources, EmptyReferenceFieldValues,
                    EmptyBackingSources, false,
                    new NativePointerExtentFact([], MayBeOpaque: false,
                        IsUnavailable: false)));
                try
                {
                    if (!EvaluateExpression(spanRead.Index, initialized, fieldMasks, declarations,
                        function, program, ref remaining, depth + 1, failures))
                    {
                        break;
                    }
                }
                finally
                {
                    if (initialized.Dependencies is BackingDependencies dependencies)
                    {
                        dependencies.Staged.RemoveAt(dependencies.Staged.Count - 1);
                    }
                }
                if (function.NoFailureProof)
                {
                    throw InvalidEffect(spanRead.Span, "Ownership");
                }
                failures?.Add(initialized, fieldMasks);
                break;
            case BoundSpanAdvance advance:
                if (advance.Type != PrimitiveType.TokenSpan
                    || function.DeclaringStruct != TokenSpanContract.CursorName || function.IsConstructor
                    || function.IsStatic || !function.Variables[0].IsMutable || function.NoFailureProof
                    || advance.View is not BoundFieldRead { Receiver: BoundRead { Variable: 0 }, Field: 0 })
                {
                    throw InvalidEffect(advance.Span, "Ownership");
                }
                if (!VerifySpanOperand(advance.View, initialized, fieldMasks, declarations, function,
                    program, ref remaining, depth, failures))
                {
                    break;
                }
                failures?.Add(initialized, fieldMasks);
                break;
            case BoundViewLength length:
                if (length.Variable < 0 || length.Variable >= initialized.Length
                    || !initialized[length.Variable]
                    || function.Variables[length.Variable].Type is not (PrimitiveType.ReadonlyInt32BufferView
                        or PrimitiveType.MutableInt32BufferView))
                {
                    throw InvalidEffect(length.Span, "Ownership");
                }
                break;
            case BoundBufferLength bufferLength:
                if (bufferLength.Type != PrimitiveType.Int32
                    || bufferLength.Buffer < 0 || bufferLength.Buffer >= program.Buffers.Length)
                {
                    throw Invalid();
                }
                break;
            case BoundViewRead viewRead:
                if (viewRead.Variable < 0 || viewRead.Variable >= initialized.Length
                    || !initialized[viewRead.Variable]
                    || function.Variables[viewRead.Variable].Type is not (PrimitiveType.ReadonlyInt32BufferView
                        or PrimitiveType.MutableInt32BufferView))
                {
                    throw InvalidEffect(viewRead.Span, "Ownership");
                }
                if (viewRead.Index.Type != PrimitiveType.Int32)
                {
                    throw Invalid();
                }
                if (!EvaluateExpression(viewRead.Index, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures))
                {
                    break;
                }
                if (RequiresNoFailureProof(function))
                {
                    throw InvalidNoFailureEffect(function, viewRead.Span);
                }
                failures?.Add(initialized, fieldMasks);
                break;
            case BoundNominalUpdate update:
                if (update.Place is null || update.Operator is not (TokenKind.PlusPlus or TokenKind.MinusMinus)
                    || (int)update.Type < 100 || (int)update.Type - 100 >= program.Structures.Length
                    || IsUnique(program, update.Type) || update.Place.Type != update.Type
                    || update.Function < 0 || update.Function >= program.Functions.Length
                    || (update.Place is BoundRead { Variable: 0 } && function.DeclaringStruct is not null
                        && !function.IsStatic))
                {
                    throw InvalidEffect(update.Span);
                }
                var updateTarget = program.Functions[update.Function];
                var updateOwner = program.Structures[(int)update.Type - 100].Name;
                var updateMember = update.Operator == TokenKind.PlusPlus
                    ? ".op_Increment" : ".op_Decrement";
                if ((updateTarget.Name != updateOwner + updateMember
                        && updateTarget.Name != updateOwner + ".$producer" + updateMember)
                    || updateTarget.DeclaringStruct != updateOwner
                    || !updateTarget.IsStatic || updateTarget.IsConstructor || updateTarget.IsClassConstructor
                    || updateTarget.IsCleanup || updateTarget.Intrinsic != BoundIntrinsic.None
                    || updateTarget.ParameterCount != 1 || updateTarget.Variables.Length == 0
                    || updateTarget.Variables[0].Type != update.Type
                    || updateTarget.Variables[0].IsOut || updateTarget.Variables[0].IsInit
                    || updateTarget.Variables[0].IsMutable || updateTarget.ReturnType != update.Type
                    || (updateTarget.IsPrivate && function.DeclaringStruct != updateTarget.DeclaringStruct))
                {
                    throw Invalid();
                }
                if (updateTarget.RequiresUnsafeCaller && !update.IsUnsafeAuthorized)
                {
                    throw InvalidEffect(update.Span, "UnsafeCaller");
                }
                var updateOperator = update.Operator == TokenKind.PlusPlus
                    ? TokenKind.PlusPlus : TokenKind.MinusMinus;
                if (!updateTarget.IsOperator
                    || !Binder.TryGetStaticOperatorKey(updateTarget.Name, updateOwner, out var operatorKey)
                    || operatorKey.Arity != 1 || operatorKey.Operator != updateOperator)
                {
                    throw Invalid();
                }
                if (RequiresNoFailureProof(function) && !ProvidesNoFailureGuarantee(updateTarget))
                {
                    throw InvalidNoFailureEffect(function, update.Span);
                }
                VerifyUpdatePlace(update.Place, function, program, ref remaining, depth + 1);
                if (!EvaluateExpression(update.Place, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures))
                {
                    break;
                }
                if (!ProvidesNoFailureGuarantee(updateTarget))
                {
                    failures?.Add(initialized, fieldMasks);
                }
                if (updateTarget.DoesNotReturn)
                {
                    break;
                }
                var nominalUpdateRoot = UpdateRoot(update.Place, ref remaining, depth + 1);
                if (nominalUpdateRoot >= 0)
                {
                    initialized.InvalidateNativeOutputVariable(nominalUpdateRoot);
                }
                break;
            case BoundMove move:
                CheckBackingInvalidation(move.Variable, initialized, move.Span);
                CheckReferencePlaceInvalidation(move.Variable,
                    StorageProjection.WholeObject, initialized, move.Span);
                CheckNativePointerLifetimeEnd(move.Variable, initialized, move.Span);
                if (!IsUnique(program, move.Type) || move.Variable < 0 || move.Variable >= initialized.Length
                    || (move.Variable == 0 && function.DeclaringStruct is not null && !function.IsStatic)
                    || !initialized[move.Variable] || move.Type != function.Variables[move.Variable].Type
                    || (move.Variable >= function.ParameterCount + (function.IsConstructor ? 1 : 0)
                        && !declarations.Contains(move.Variable)))
                {
                    throw InvalidEffect(move.Span, "Ownership");
                }
                if (initialized.Dependencies is BackingDependencies moveDependencies)
                {
                    moveDependencies.ClearCopyOriginCallResult(move);
                    if (CarriesCopyOrigin(move.Type, program))
                    {
                        moveDependencies.SetCopyOriginCallResult(move,
                            CopyOriginValueOfExpression(move, move.Type, moveDependencies,
                                function, program, ref remaining, depth + 1));
                    }
                    moveDependencies.ClearReferenceFieldCallResult(move);
                    if (ReferenceFieldLeaves(move.Type, program,
                        ref remaining, depth + 1).Count != 0)
                    {
                        moveDependencies.SetReferenceFieldCallResult(move,
                            ReferenceFieldValuesOfExpression(move, move.Type,
                                moveDependencies, initialized, function, program,
                                ref remaining, depth + 1, fieldMasks));
                    }
                }
                initialized.Retire(move.Variable, (long)move.Span.Start + 1);
                fieldMasks[move.Variable] = 0;
                break;
            case BoundFieldMove move:
                if (move.Place is not BoundFieldRead fieldPlace
                    || move.Type != fieldPlace.Type
                    || !IsUnique(program, move.Type)
                    || !TryGetFieldProjectionPath(fieldPlace, function, program,
                        out var moveRootVariable, out var movedFieldPath)
                    || !IsUnique(program, function.Variables[moveRootVariable].Type))
                {
                    throw InvalidEffect(move.Span, "Ownership");
                }
                _ = VerifyUniqueFieldPlace(fieldPlace, initialized, fieldMasks,
                    function, program, ref remaining, depth + 1);
                var constructorFieldMove = function.IsConstructor && moveRootVariable == 0;
                var movedFieldSegment = movedFieldPath.Segments[^1];
                if (!constructorFieldMove
                    && ((moveRootVariable == 0 && function.DeclaringStruct is not null && !function.IsStatic)
                        || !function.Variables[moveRootVariable].IsMutable
                        || movedFieldPath.Segments.Any(segment =>
                            !Fields(program, segment.OwnerType)[segment.Field].IsMutable
                            || program.Structures[(int)segment.OwnerType - 100].CleanupMethod is not null)))
                {
                    throw InvalidEffect(move.Span, "Ownership");
                }
                var movedFieldProjection = StorageProjection.ForFieldPath(
                    movedFieldPath);
                CheckReferencePlaceInvalidation(moveRootVariable,
                    movedFieldProjection, initialized, move.Span);
                initialized.AdvanceFieldPlaceGeneration(moveRootVariable,
                    movedFieldProjection, move.Span);
                if (initialized.Dependencies is BackingDependencies fieldMoveDependencies)
                {
                    fieldMoveDependencies.ClearBackingCallResult(move);
                    fieldMoveDependencies.ClearNativePointerCallResult(move);
                    fieldMoveDependencies.ClearCopyOriginCallResult(move);
                    if (CarriesCopyOrigin(move.Type, program))
                    {
                        fieldMoveDependencies.SetCopyOriginCallResult(move,
                            CopyOriginValueOfExpression(fieldPlace, move.Type,
                                fieldMoveDependencies, function, program,
                                ref remaining, depth + 1));
                    }
                    fieldMoveDependencies.ClearReferenceFieldCallResult(move);
                    if (ReferenceFieldLeaves(move.Type, program,
                        ref remaining, depth + 1).Count != 0)
                    {
                        fieldMoveDependencies.SetReferenceFieldCallResult(move,
                            ReferenceFieldValuesOfExpression(fieldPlace,
                                move.Type, fieldMoveDependencies, initialized,
                                function, program, ref remaining, depth + 1,
                                fieldMasks));
                    }
                    if (TokenSpanContract.IsDependent(move.Type, program.Structures))
                    {
                        var backingSources = BackingOf(fieldPlace,
                            fieldMoveDependencies, program, ref remaining, depth + 1);
                        var backingValues = BackingFieldValueSnapshotOfExpression(
                            fieldPlace, move.Type, fieldMoveDependencies, function,
                            program, ref remaining, depth + 1);
                        if (backingValues is not null)
                        {
                            backingValues[StorageProjection.WholeObject] =
                                new BackingFieldValue(
                                    new HashSet<ProvenanceAnchor>(backingSources),
                                    isAvailable: true);
                            fieldMoveDependencies.SetBackingCallResult(move, backingValues);
                        }
                    }
                    if (CarriesNativePointer(move.Type, program))
                    {
                        var pointerSources = NativePointerBackingOf(fieldPlace,
                            fieldMoveDependencies, program, ref remaining, depth + 1);
                        var pointerExtent = NativePointerExtentFactsOfExpression(
                            fieldPlace, fieldMoveDependencies, function, program,
                            ref remaining, depth + 1);
                        var pointerValues = NativePointerFieldValueSnapshotOfExpression(
                            fieldPlace, move.Type, fieldMoveDependencies, function,
                            program, ref remaining, depth + 1);
                        pointerValues[StorageProjection.WholeObject] =
                            new NativePointerFieldValue(pointerSources,
                                NativePointerMayBeUnknownOf(fieldPlace,
                                    fieldMoveDependencies, program,
                                    ref remaining, depth + 1),
                                pointerExtent.Sources, pointerExtent.MayBeOpaque,
                                pointerExtent.IsUnavailable);
                        fieldMoveDependencies.SetNativePointerCallResult(move, pointerValues);
                    }
                    fieldMoveDependencies.InvalidateCopyOriginValue(
                        moveRootVariable, movedFieldProjection, program,
                        ref remaining, depth + 1);
                    fieldMoveDependencies.ClearBackingFieldValues(
                        moveRootVariable, movedFieldProjection);
                    fieldMoveDependencies.ClearReferenceFieldValues(
                        moveRootVariable, movedFieldProjection);
                    fieldMoveDependencies.ClearNativePointerFieldValues(
                        moveRootVariable, movedFieldProjection);
                    var rootType = function.Variables[moveRootVariable].Type;
                    if (TokenSpanContract.IsDependent(rootType, program.Structures))
                    {
                        RefreshBackingAggregateProvenance(moveRootVariable, rootType,
                            fieldMoveDependencies, program, ref remaining, depth + 1);
                    }
                    if (CarriesNativePointer(rootType, program))
                    {
                        RefreshNativePointerAggregateProvenance(moveRootVariable, rootType,
                            fieldMoveDependencies, program, ref remaining, depth + 1);
                    }
                }
                if (movedFieldPath.Segments.Length == 1)
                {
                    var movedFieldBit = 1u << movedFieldSegment.Field;
                    fieldMasks[moveRootVariable] &= ~movedFieldBit;
                    initialized.SetPossibleFieldMask(moveRootVariable,
                        initialized.PossibleFieldMask(moveRootVariable) & ~movedFieldBit);
                    initialized.ResetNestedOwnerSubtree(moveRootVariable,
                        movedFieldPath);
                    initialized.MarkPartialValue(moveRootVariable);
                }
                else
                {
                    initialized.ResetNestedOwnerSubtree(moveRootVariable,
                        movedFieldPath);
                    SetNestedFieldLiveness(moveRootVariable, movedFieldPath, live: false,
                        initialized, fieldMasks, function, program, move.Span);
                }
                break;
            case BoundReceiverLoan loan:
                throw InvalidEffect(loan.Span, "Ownership");
            case BoundMutableReceiverLoan mutableLoan:
                throw InvalidEffect(mutableLoan.Span, "Ownership");
            case BoundMutableFieldLoan mutableField:
                throw InvalidEffect(mutableField.Span, "Ownership");
            case BoundFieldLoan fieldLoan:
                throw InvalidEffect(fieldLoan.Span, "Ownership");
            case BoundOutArgument:
                throw Invalid();
            case BoundBufferRead bufferRead:
                if (bufferRead.Buffer < 0 || bufferRead.Buffer >= program.Buffers.Length
                    || bufferRead.Type != program.Buffers[bufferRead.Buffer].ElementType)
                {
                    throw Invalid();
                }
                if (bufferRead.Index.Type != PrimitiveType.Int32)
                {
                    throw Invalid();
                }
                if (!EvaluateExpression(bufferRead.Index, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures))
                {
                    break;
                }
                if (RequiresNoFailureProof(function)
                    && !IsProvenBufferIndex(bufferRead.Buffer, bufferRead.Index, program))
                {
                    throw InvalidNoFailureEffect(function, bufferRead.Span);
                }
                failures?.Add(initialized, fieldMasks);
                break;
            case BoundUnary unary:
                var operandCompletes = EvaluateExpression(unary.Operand, initialized, fieldMasks,
                    declarations, function, program, ref remaining, depth + 1, failures);
                if (unary.OverflowBehavior is not (BoundIntegerOverflowBehavior.Checked
                    or BoundIntegerOverflowBehavior.Wrapping))
                {
                    throw Invalid();
                }
                var integerNegation = unary.Operator == TokenKind.Minus
                    && PrimitiveTypeFacts.IsIntegerOrWide(unary.Type);
                if (unary.OverflowBehavior == BoundIntegerOverflowBehavior.Wrapping
                    && !integerNegation)
                {
                    throw Invalid();
                }
                var integerOperand = PrimitiveTypeFacts.IsIntegerOrWide(unary.Operand.Type);
                var floatingOperand = PrimitiveTypeFacts.IsFloating(unary.Operand.Type);
                var unaryValid = unary.Operator switch {
                    TokenKind.Plus => (integerOperand || floatingOperand) && unary.Type == unary.Operand.Type,
                    TokenKind.Tilde => integerOperand && unary.Type == unary.Operand.Type,
                    TokenKind.Minus => (integerOperand || floatingOperand) && unary.Type == unary.Operand.Type,
                    TokenKind.Bang => unary.Operand.Type == PrimitiveType.Bool && unary.Type == PrimitiveType.Bool,
                    _ => false,
                };
                if (!unaryValid)
                {
                    throw Invalid();
                }
                if (!operandCompletes)
                {
                    break;
                }
                var checkedNegation = integerNegation
                    && unary.OverflowBehavior == BoundIntegerOverflowBehavior.Checked;
                var unaryNegationMayOverflow = checkedNegation
                    && (unary.Operand is not BoundConstant operand
                        || (unary.Type switch {
                            PrimitiveType.Int8 => operand.Value == sbyte.MinValue,
                            PrimitiveType.Int16 => operand.Value == short.MinValue,
                            PrimitiveType.Int32 => operand.Value == int.MinValue,
                            PrimitiveType.UInt8 or PrimitiveType.UInt16 or PrimitiveType.UInt32 => operand.Value != 0,
                            PrimitiveType.Int64 => operand.Value == long.MinValue,
                            PrimitiveType.UInt64 => operand.Value != 0,
                            PrimitiveType.Int128 => operand.Bits.High == 0x8000_0000_0000_0000UL
                                && operand.Bits.Low == 0,
                            PrimitiveType.UInt128 => (operand.Bits.Low | operand.Bits.High) != 0,
                            _ => false,
                        }));
                if (RequiresNoFailureProof(function) && unaryNegationMayOverflow)
                {
                    throw InvalidNoFailureEffect(function, unary.Span);
                }
                if (unaryNegationMayOverflow)
                {
                    failures?.Add(initialized, fieldMasks);
                }
                break;
            case BoundUpdate update:
                var integerUpdate = PrimitiveTypeFacts.IsIntegerOrWide(update.Type);
                var floatingUpdate = PrimitiveTypeFacts.IsFloating(update.Type);
                if (update.Place is null || update.Operator is not (TokenKind.PlusPlus or TokenKind.MinusMinus)
                    || (!integerUpdate && !floatingUpdate)
                    || update.OverflowBehavior is not (BoundIntegerOverflowBehavior.Checked
                        or BoundIntegerOverflowBehavior.Wrapping)
                    || (floatingUpdate && update.OverflowBehavior != BoundIntegerOverflowBehavior.Checked)
                    || update.Place.Type != update.Type
                    || (update.Place is BoundRead { Variable: 0 } && function.DeclaringStruct is not null
                        && !function.IsStatic))
                {
                    throw InvalidEffect(update.Span);
                }
                VerifyUpdatePlace(update.Place, function, program, ref remaining, depth + 1);
                if (!EvaluateExpression(update.Place, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures))
                {
                    break;
                }
                var updateCanFail = integerUpdate
                    && update.OverflowBehavior == BoundIntegerOverflowBehavior.Checked;
                if (RequiresNoFailureProof(function) && updateCanFail)
                {
                    throw InvalidNoFailureEffect(function, update.Span);
                }
                if (updateCanFail)
                {
                    failures?.Add(initialized, fieldMasks);
                }
                var updateRoot = UpdateRoot(update.Place, ref remaining, depth + 1);
                if (updateRoot >= 0)
                {
                    initialized.InvalidateNativeOutputVariable(updateRoot);
                }
                break;
            case BoundCompoundAssignment compound:
                var compoundOperation = CompoundOperators.Binary(compound.Operator);
                var integerCompound = PrimitiveTypeFacts.IsIntegerOrWide(compound.Type);
                var floatingCompound = PrimitiveTypeFacts.IsFloating(compound.Type);
                var floatingArithmeticOperator = compoundOperation is TokenKind.Plus or TokenKind.Minus
                    or TokenKind.Star or TokenKind.Slash or TokenKind.Percent;
                var integerShift = CompoundOperators.IsShift(compound.Operator);
                if (!statementCompound || compound.Place is null || compound.Right is null
                    || !CompoundOperators.IsSupported(compound.Operator)
                    || (!integerCompound && !floatingCompound)
                    || (floatingCompound && !floatingArithmeticOperator)
                    || compound.OverflowBehavior is not (BoundIntegerOverflowBehavior.Checked
                        or BoundIntegerOverflowBehavior.Wrapping)
                    || (floatingCompound && compound.OverflowBehavior != BoundIntegerOverflowBehavior.Checked)
                    || compound.Place.Type != compound.Type
                    || (integerShift
                        ? !integerCompound || !PrimitiveTypeFacts.IsIntegerOrWide(compound.Right.Type)
                        : compound.Right.Type != compound.Type)
                    || (compound.Place is BoundRead { Variable: 0 } && function.DeclaringStruct is not null
                        && !function.IsStatic))
                {
                    throw InvalidEffect(compound.Span);
                }
                VerifyUpdatePlace(compound.Place, function, program, ref remaining, depth + 1);
                if (!EvaluateExpression(compound.Place, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures))
                {
                    ValidateUnreachableExpression(compound.Right, initialized, fieldMasks,
                        declarations, function, program, ref remaining, depth + 1);
                    break;
                }
                var root = UpdateRoot(compound.Place, ref remaining, depth + 1);
                if (root >= 0 && IsUnique(program, function.Variables[root].Type)
                    && InvalidatesReservedOwner(compound.Right, root, ref remaining, depth + 1))
                {
                    throw InvalidEffect(compound.Right.Span, "Ownership");
                }
                if (!EvaluateExpression(compound.Right, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures))
                {
                    break;
                }
                if (root >= 0 && !initialized[root])
                {
                    throw InvalidEffect(compound.Right.Span, "Ownership");
                }
                var compoundUsesProfile = integerCompound
                    && compoundOperation is TokenKind.Plus or TokenKind.Minus or TokenKind.Star;
                if (compound.OverflowBehavior == BoundIntegerOverflowBehavior.Wrapping && !compoundUsesProfile)
                {
                    throw Invalid();
                }
                var compoundCanFail = integerCompound && (!compoundUsesProfile
                    || compound.OverflowBehavior == BoundIntegerOverflowBehavior.Checked);
                if (compoundOperation is not (TokenKind.Ampersand or TokenKind.Pipe or TokenKind.Caret)
                    && compoundCanFail)
                {
                    if (RequiresNoFailureProof(function))
                    {
                        throw InvalidNoFailureEffect(function, compound.Span);
                    }
                    failures?.Add(initialized, fieldMasks);
                }
                if (root >= 0)
                {
                    initialized.InvalidateNativeOutputVariable(root);
                }
                break;
            case BoundBinary binary:
            {
                if (TryEvaluateLongLeftLogicalChain(binary, initialized, fieldMasks,
                    declarations, function, program, ref remaining, depth,
                    failures, cleanupCounterStep))
                {
                    break;
                }
                if (TryEvaluateLongLeftBinaryChain(binary, initialized, fieldMasks,
                    declarations, function, program, ref remaining, depth,
                    failures, cleanupCounterStep))
                {
                    break;
                }
                using var repeatSnapshotState
                    = new RepeatSnapshotReservationScope(
                        initialized.RepeatResources);
                var beforeLeft = initialized.Clone(repeatSnapshotState);
                var leftCompletes = EvaluateExpression(binary.Left, initialized, fieldMasks,
                    declarations, function, program, ref remaining, depth + 1, failures);
                var binaryCompletes = false;
                if (binary.Operator is TokenKind.AmpersandAmpersand or TokenKind.PipePipe)
                {
                    var evaluateWhenTrue = binary.Operator == TokenKind.AmpersandAmpersand;
                    var rightState = Outcome(binary.Left, beforeLeft,
                        evaluateWhenTrue, ref remaining,
                        repeatSnapshotState);
                    var rightMasks = (uint[])fieldMasks.Clone();
                    MarkOutMasks(rightState, rightMasks, function, program);
                    var evaluates = leftCompletes
                        && CanCompleteAs(binary.Left, evaluateWhenTrue, beforeLeft,
                            program, ref remaining, depth + 1);
                    var rightCompletes = evaluates && EvaluateExpression(binary.Right,
                        rightState, rightMasks, declarations, function, program,
                        ref remaining, depth + 1, failures);
                    if (!evaluates)
                    {
                        ValidateUnreachableExpression(binary.Right, rightState, rightMasks,
                            declarations, function, program, ref remaining, depth + 1);
                    }
                    var skippedState = Outcome(binary.Left, beforeLeft,
                        !evaluateWhenTrue, ref remaining,
                        repeatSnapshotState);
                    var skippedMasks = (uint[])fieldMasks.Clone();
                    MarkOutMasks(skippedState, skippedMasks, function, program);
                    var skips = leftCompletes
                        && CanCompleteAs(binary.Left, !evaluateWhenTrue, beforeLeft,
                            program, ref remaining, depth + 1);
                    if (rightCompletes || skips)
                    {
                        for (var i = 0; i < initialized.Length; i++)
                        {
                            initialized.SetPossibilities(i, !rightCompletes ? skippedState.Possibilities(i)
                                : !skips ? rightState.Possibilities(i)
                                : (byte)(rightState.Possibilities(i) | skippedState.Possibilities(i)));
                            fieldMasks[i] = !rightCompletes ? skippedMasks[i] : !skips ? rightMasks[i]
                                : rightMasks[i] & skippedMasks[i];
                            initialized.SetPossibleFieldMask(i, !rightCompletes
                                ? skippedState.PossibleFieldMask(i)
                                : !skips ? rightState.PossibleFieldMask(i)
                                : rightState.PossibleFieldMask(i) | skippedState.PossibleFieldMask(i));
                            if (rightCompletes && skips)
                            {
                                _ = initialized.MergeRawUnionInitializedBytes(i,
                                    rightState, skippedState);
                                _ = initialized.MergeFieldPlaceGenerations(i,
                                    rightState, skippedState);
                                _ = initialized.MergeNestedOwnerStates(i,
                                    rightState, skippedState);
                            }
                            else if (rightCompletes)
                            {
                                _ = initialized.CopyRawUnionInitializedBytes(i,
                                    rightState);
                                _ = initialized.CopyFieldPlaceGenerations(i, rightState);
                                _ = initialized.CopyNestedOwnerStates(i, rightState);
                            }
                            else if (skips)
                            {
                                _ = initialized.CopyRawUnionInitializedBytes(i,
                                    skippedState);
                                _ = initialized.CopyFieldPlaceGenerations(i, skippedState);
                                _ = initialized.CopyNestedOwnerStates(i, skippedState);
                            }
                        }
                        _ = initialized.MergeNativeOutputState(
                            rightState, rightCompletes, skippedState, skips);
                    }
                }
                else
                {
                    var rightCompletes = leftCompletes && EvaluateExpression(binary.Right,
                        initialized, fieldMasks, declarations, function, program,
                        ref remaining, depth + 1, failures);
                    if (!leftCompletes)
                    {
                        ValidateUnreachableExpression(binary.Right, initialized, fieldMasks,
                            declarations, function, program, ref remaining, depth + 1);
                    }
                    binaryCompletes = leftCompletes && rightCompletes;
                }
                VerifyBinaryOperation(binary, binaryCompletes, initialized,
                    fieldMasks, function, program, failures, cleanupCounterStep);
                break;
            }
            case BoundConditional conditional:
            {
                if (conditional.Condition is null || conditional.WhenTrue is null || conditional.WhenFalse is null
                    || conditional.Condition.Type != PrimitiveType.Bool
                    || conditional.WhenTrue.Type != conditional.Type
                    || conditional.WhenFalse.Type != conditional.Type)
                {
                    throw Invalid();
                }
                using var repeatSnapshotState
                    = new RepeatSnapshotReservationScope(
                        initialized.RepeatResources);
                var beforeCondition = initialized.Clone(repeatSnapshotState);
                if (!EvaluateExpression(conditional.Condition, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures))
                {
                    ValidateUnreachableExpression(conditional.WhenTrue, initialized, fieldMasks,
                        declarations, function, program, ref remaining, depth + 1);
                    ValidateUnreachableExpression(conditional.WhenFalse, initialized, fieldMasks,
                        declarations, function, program, ref remaining, depth + 1);
                    break;
                }
                var trueFeasible = CanCompleteAs(conditional.Condition, true, beforeCondition,
                    program, ref remaining, depth + 1);
                var falseFeasible = CanCompleteAs(conditional.Condition, false, beforeCondition,
                    program, ref remaining, depth + 1);
                var trueState = Outcome(conditional.Condition,
                    beforeCondition, true, ref remaining,
                    repeatSnapshotState);
                var falseState = Outcome(conditional.Condition,
                    beforeCondition, false, ref remaining,
                    repeatSnapshotState);
                var trueMasks = (uint[])fieldMasks.Clone();
                var falseMasks = (uint[])fieldMasks.Clone();
                MarkOutMasks(trueState, trueMasks, function, program);
                MarkOutMasks(falseState, falseMasks, function, program);
                var trueCompletes = trueFeasible && CanCompleteNormally(
                    conditional.WhenTrue, trueState, program, ref remaining, depth + 1);
                var falseCompletes = falseFeasible && CanCompleteNormally(
                    conditional.WhenFalse, falseState, program, ref remaining, depth + 1);
                var trueDeclarations = new HashSet<int>(declarations);
                var falseDeclarations = new HashSet<int>(declarations);
                Expression(conditional.WhenTrue, trueState, trueMasks, trueDeclarations, function, program,
                    ref remaining, depth + 1, trueFeasible ? failures : null);
                Expression(conditional.WhenFalse, falseState, falseMasks, falseDeclarations, function, program,
                    ref remaining, depth + 1, falseFeasible ? failures : null);
                declarations.UnionWith(trueDeclarations);
                declarations.UnionWith(falseDeclarations);
                if (trueCompletes || falseCompletes)
                {
                    for (var i = 0; i < initialized.Length; i++)
                    {
                        initialized.SetPossibilities(i, !trueCompletes ? falseState.Possibilities(i)
                            : !falseCompletes ? trueState.Possibilities(i)
                            : (byte)(trueState.Possibilities(i) | falseState.Possibilities(i)));
                        fieldMasks[i] = !trueCompletes ? falseMasks[i] : !falseCompletes ? trueMasks[i]
                            : trueMasks[i] & falseMasks[i];
                        initialized.SetPossibleFieldMask(i, !trueCompletes
                            ? falseState.PossibleFieldMask(i)
                            : !falseCompletes ? trueState.PossibleFieldMask(i)
                            : trueState.PossibleFieldMask(i) | falseState.PossibleFieldMask(i));
                        if (trueCompletes && falseCompletes)
                        {
                            _ = initialized.MergeRawUnionInitializedBytes(i,
                                trueState, falseState);
                            _ = initialized.MergeFieldPlaceGenerations(i,
                                trueState, falseState);
                            _ = initialized.MergeNestedOwnerStates(i,
                                trueState, falseState);
                        }
                        else if (trueCompletes)
                        {
                            _ = initialized.CopyRawUnionInitializedBytes(i,
                                trueState);
                            _ = initialized.CopyFieldPlaceGenerations(i, trueState);
                            _ = initialized.CopyNestedOwnerStates(i, trueState);
                        }
                        else if (falseCompletes)
                        {
                            _ = initialized.CopyRawUnionInitializedBytes(i,
                                falseState);
                            _ = initialized.CopyFieldPlaceGenerations(i, falseState);
                            _ = initialized.CopyNestedOwnerStates(i, falseState);
                        }
                        if (trueCompletes && falseCompletes)
                        {
                            _ = initialized.CopyProvenanceSlot(i, trueState);
                            _ = initialized.MergeProvenanceSlot(i, falseState);
                        }
                        else if (trueCompletes)
                        {
                            _ = initialized.CopyProvenanceSlot(i, trueState);
                        }
                        else if (falseCompletes)
                        {
                            _ = initialized.CopyProvenanceSlot(i, falseState);
                        }
                    }
                    _ = initialized.MergeNativeOutputState(trueState, trueCompletes,
                        falseState, falseCompletes);
                }
                break;
            }
            case BoundIndirectCall call:
                VerifyIndirectCall(call, function, program);
                var indirectSignature = program.NativeFunctionPointerSignatures[
                    NativeFunctionPointerTypes.Index(call.Target.Type)];
                var indirectStagedDependencyCount = initialized.Dependencies?.Staged.Count ?? 0;
                try
                {
                    if (!EvaluateExpression(call.Target, initialized, fieldMasks, declarations,
                        function, program, ref remaining, depth + 1, failures))
                    {
                        foreach (var argument in call.Arguments)
                        {
                            ValidateUnreachableExpression(argument, initialized, fieldMasks,
                                declarations, function, program, ref remaining, depth + 1);
                        }
                        break;
                    }

                    var indirectCallCompletes = true;
                    var indirectOutputTargets = new HashSet<int>();
                    for (var index = 0; index < call.Arguments.Length; index++)
                    {
                        var argument = call.Arguments[index];
                        if (!indirectCallCompletes)
                        {
                            ValidateUnreachableExpression(argument, initialized, fieldMasks,
                                declarations, function, program, ref remaining, depth + 1);
                            continue;
                        }

                        var isNativeOutput = indirectSignature.OutputStates.Any(outputState =>
                            outputState.FormalOrdinal == (uint)index);
                        if (isNativeOutput)
                        {
                            if (!NativeFunctionPointerContract.TryGetNativeOutputContract(
                                indirectSignature, index, program.NativePointerTypes,
                                out var output)
                                || output is null
                                || argument is not BoundNativeOutputReservation reservation
                                || !MatchesReservation(reservation, output, function, program)
                                || !indirectOutputTargets.Add(reservation.Variable))
                            {
                                throw InvalidEffect(call.Span, "NativeImport");
                            }
                            VerifyNativeOutputReservation(reservation, output,
                                initialized, fieldMasks, declarations, function, program,
                                ref remaining, depth + 1, failures);
                            StageIndirectDependencySource(call, index, argument,
                                indirectSignature, initialized, program,
                                ref remaining, depth + 1);
                            continue;
                        }

                        var nativeParameter = indirectSignature.NativeParameters
                            .FirstOrDefault(parameter => parameter.ParameterIndex == index);
                        if (nativeParameter is not null
                            && argument is BoundNativeStorageAddress address)
                        {
                            VerifyDirectNativeParameterAddress(address, nativeParameter,
                                initialized, fieldMasks, declarations, function, program,
                                ref remaining, depth + 1, failures);
                            StageIndirectDependencySource(call, index, argument,
                                indirectSignature, initialized, program,
                                ref remaining, depth + 1);
                            continue;
                        }

                        indirectCallCompletes = EvaluateExpression(argument, initialized,
                            fieldMasks, declarations, function, program, ref remaining,
                            depth + 1, failures);
                        if (indirectCallCompletes)
                        {
                            if (nativeParameter is not null
                                && argument is not BoundNativeStorageAddress
                                && !DirectNativeImportContract.IsStringPointerArgument(
                                    argument, nativeParameter, program))
                            {
                                VerifyNativeParameterValueExtent(argument,
                                    nativeParameter, call.IsUnsafeAuthorized,
                                    initialized, function, program,
                                    ref remaining, depth + 1);
                            }
                            StageIndirectDependencySource(call, index, argument,
                                indirectSignature, initialized, program,
                                ref remaining, depth + 1);
                        }
                    }

                    if (!indirectCallCompletes)
                    {
                        initialized.CancelUninvokedNativeOutputReservations();
                    }
                    if (indirectCallCompletes)
                    {
                        VerifyStagedNativeParameterExtents(call, indirectSignature,
                            initialized, function, program, ref remaining, depth + 1);
                        if (initialized.Dependencies is BackingDependencies dependencies
                            && MayWriteNativePointerStorage(call, program))
                        {
                            dependencies.MarkAllNativePointerProvenanceUnknown();
                            dependencies.InvalidateAllCopyOriginValues(
                                program, ref remaining, depth + 1);
                        }
                        InvalidateNativePointerExtentsForCall(
                            call, indirectSignature, initialized, program);
                        if (indirectSignature.OutputStates.IsEmpty)
                        {
                            initialized.InvalidateNativeOutputCorrelations();
                        }
                        else
                        {
                            ApplyNativeOutputIndirectCallEffects(call,
                                indirectSignature, initialized, ref remaining);
                        }
                        failures?.Add(initialized, fieldMasks);
                    }
                }
                finally
                {
                    if (initialized.Dependencies is BackingDependencies stagedDependencies)
                    {
                        stagedDependencies.Staged.RemoveRange(indirectStagedDependencyCount,
                            stagedDependencies.Staged.Count - indirectStagedDependencyCount);
                    }
                }
                break;
            case BoundCall call:
            {
                if (call.Function < 0 || call.Function >= program.Functions.Length || call.Arguments.IsDefault)
                {
                    throw Invalid();
                }
                if ((!call.ArgumentParameterIndices.IsDefault
                        && call.ArgumentParameterIndices.Length != call.Arguments.Length)
                    || (!call.ArgumentImplicitReferenceTemporaries.IsDefault
                        && call.ArgumentImplicitReferenceTemporaries.Length != call.Arguments.Length))
                {
                    throw Invalid();
                }
                if (call.Arguments.Length > MaximumArguments)
                {
                    throw Limit();
                }
                var target = program.Functions[call.Function];
                var copyOriginSummary = initialized.Dependencies is BackingDependencies summaryDependencies
                    ? summaryDependencies.CopyOriginSummaries[call.Function]
                    : target.CopyOriginSummary;
                if (copyOriginSummary is not null
                    && initialized.Dependencies is null)
                {
                    throw Invalid(call.Span);
                }
                var copyOriginInputViews = copyOriginSummary is null
                    ? null : CopyOriginSummaryInputViews(target, copyOriginSummary);
                var stagedCopyOriginInputs = new Dictionary<int, StagedCopyOriginInput>();
                using var stagedRawUnionSnapshotState
                    = new RepeatSnapshotReservationScope(
                        initialized.RepeatResources);
                if (initialized.Dependencies is BackingDependencies copyOriginDependencies)
                {
                    copyOriginDependencies.ClearCopyOriginCallResult(call);
                    copyOriginDependencies.ClearBackingCallResult(call);
                    copyOriginDependencies.ClearReferenceFieldCallResult(call);
                    copyOriginDependencies.ClearNativePointerCallResult(call);
                    copyOriginDependencies.ClearRawUnionCallCoverageResult(call);
                    copyOriginDependencies.ClearRawUnionArrayCallResult(call);
                }
                if (target.IsCleanup || target.IsClassConstructor)
                {
                    throw InvalidEffect(call.Span, "Ownership");
                }
                if (target.RequiresUnsafeCaller && !call.IsUnsafeAuthorized)
                {
                    throw InvalidEffect(call.Span, "UnsafeCaller");
                }
                if (RequiresNoFailureProof(function) && !CanCallWithoutTerrumFailure(function, target))
                {
                    throw InvalidNoFailureEffect(function, call.Span);
                }
                if (call.Arguments.Length != target.ParameterCount || call.Type != target.ReturnType)
                {
                    throw Invalid();
                }
                if (target.DeclaringStruct is not null && !target.IsStatic && !target.IsConstructor
                    && target.Variables[0].IsMutable
                    && (int)target.Variables[0].Type is >= 100 and < ManagedClassTypes.First
                    && call.Arguments[0] is not (BoundMutableReceiverLoan or BoundMutableFieldLoan))
                {
                    throw Invalid();
                }
                if (target.DeclaringStruct is not null && !target.IsStatic && !target.IsConstructor
                    && (call.Arguments.IsEmpty || call.ParameterIndex(0) != 0))
                {
                    throw Invalid();
                }
                if (target.IsPrivate)
                {
                    if (function.DeclaringStruct != target.DeclaringStruct)
                    {
                        throw Invalid();
                    }
                }
                var outTargets = new HashSet<int>();
                var directNativeOutputTargets = new HashSet<int>();
                var implicitReferenceTemporaries = new HashSet<int>();
                var argumentParameters = call.ArgumentParameterIndices.IsDefault
                    ? null : new bool[call.Arguments.Length];
                var loanRoot = -1;
                var loanOfInitializedField = false;
                var reservedReceiver = -1;
                var stagedDependencyCount = initialized.Dependencies?.Staged.Count ?? 0;
                var argumentsComplete = true;
                InitializationState? nonCompletingArgumentState = null;
                uint[]? nonCompletingArgumentMasks = null;
                try
                {
                    for (var i = 0; i < call.Arguments.Length; i++)
                    {
                        if (!argumentsComplete)
                        {
                            initialized = (nonCompletingArgumentState
                                ?? throw Invalid()).Clone();
                            fieldMasks = (uint[])(nonCompletingArgumentMasks
                                ?? throw Invalid()).Clone();
                            failures = null;
                        }
                        var argument = call.Arguments[i];
                        var parameterIndex = call.ParameterIndex(i);
                        if (argumentParameters is not null)
                        {
                            if ((uint)parameterIndex >= (uint)call.Arguments.Length
                                || argumentParameters[parameterIndex])
                            {
                                throw Invalid();
                            }
                            argumentParameters[parameterIndex] = true;
                        }
                        var parameterSlot = parameterIndex + (target.IsConstructor ? 1 : 0);
                        if ((uint)parameterSlot >= (uint)target.Variables.Length)
                        {
                            throw Invalid();
                        }
                        var nativeOutputContract = NativeOutputContractForParameter(
                            target, parameterIndex);
                        var directNativeOutput = nativeOutputContract is null
                            ? DirectNativeImportContract.OutputForParameter(target, parameterIndex)
                            : null;
                        var nativeVTableOutput = NativeVTableCallContract.OutputForParameter(
                            target, parameterIndex);
                        var directNativeParameter = DirectNativeImportContract.ParameterForParameter(
                            target, parameterIndex);
                        var isDirectNativeStringPointerArgument = directNativeParameter is not null
                            && DirectNativeImportContract.IsStringPointerArgument(
                                argument, directNativeParameter, program);
                        var nativeVTableParameter = NativeVTableCallContract.ParameterForParameter(
                            target, parameterIndex);
                        var isNativeVTableStringPointerArgument = nativeVTableParameter is not null
                            && DirectNativeImportContract.IsStringPointerArgument(
                                argument, nativeVTableParameter, program);
                        var isDirectNativeParameterAddress = argument is BoundNativeStorageAddress
                            directParameterAddress
                            && directNativeParameter is not null
                            && DirectNativeImportContract.MatchesAddress(
                                directParameterAddress, directNativeParameter, function, program);
                        var isDirectNativeParameterValue = directNativeParameter is not null
                            && argument is not BoundNativeStorageAddress
                            && DirectNativeImportContract.MatchesNativeParameterArgumentType(
                                argument.Type, target.Variables[parameterSlot].Type,
                                directNativeParameter, program.NativePointerTypes,
                                DirectNativeImportContract.IsNullNativePointerValue(argument));
                        var isNativeVTableParameterAddress = argument is BoundNativeStorageAddress
                            nativeVTableParameterAddress
                            && nativeVTableParameter is not null
                            && DirectNativeImportContract.MatchesAddress(
                                nativeVTableParameterAddress, nativeVTableParameter, function, program);
                        var isNativeVTableParameterValue = nativeVTableParameter is not null
                            && argument is not BoundNativeStorageAddress
                            && DirectNativeImportContract.MatchesNativeParameterArgumentType(
                                argument.Type, target.Variables[parameterSlot].Type,
                                nativeVTableParameter, program.NativePointerTypes,
                                DirectNativeImportContract.IsNullNativePointerValue(argument));
                        var implicitReferenceTemporary = call.ImplicitReferenceTemporary(i);
                        if (implicitReferenceTemporary < -1)
                        {
                            throw Invalid();
                        }
                        CopyOriginValue? preMoveCopyOriginSnapshot = null;
                        Dictionary<StorageProjection, BackingFieldValue>?
                            preMoveBackingSnapshot = null;
                        Dictionary<StorageProjection, NativePointerFieldValue>?
                            preMoveNativePointerSnapshot = null;
                        Dictionary<StorageProjection, ReferenceFieldValue>?
                            preMoveReferenceSnapshot = null;
                        byte[]? preMoveRawUnionInitializedBytes = null;
                        SortedDictionary<StorageProjection,
                            InitializationState.RawUnionArrayElementState>?
                            preMoveRawUnionArraySnapshot = null;
                        if (argumentsComplete && argument is BoundMove or BoundFieldMove
                            && copyOriginInputViews?.ContainsKey(parameterIndex) == true)
                        {
                            if (initialized.Dependencies is not BackingDependencies copyOriginState)
                            {
                                throw Invalid(argument.Span);
                            }
                            var preMoveValue = argument is BoundFieldMove fieldMoveArgument
                                ? fieldMoveArgument.Place : argument;
                            preMoveCopyOriginSnapshot = CopyOriginValueOfExpression(preMoveValue,
                                argument.Type, copyOriginState, function, program,
                                ref remaining, depth + 1);
                            preMoveBackingSnapshot =
                                BackingFieldValueSnapshotOfExpression(preMoveValue,
                                    argument.Type, copyOriginState, function, program,
                                    ref remaining, depth + 1);
                            preMoveReferenceSnapshot =
                                ReferenceFieldValuesOfExpression(preMoveValue,
                                    argument.Type, copyOriginState, initialized,
                                    function, program, ref remaining, depth + 1,
                                    fieldMasks);
                            if (CarriesNativePointer(argument.Type, program))
                            {
                                preMoveNativePointerSnapshot =
                                    NativePointerFieldValueSnapshotOfExpression(preMoveValue,
                                        argument.Type, copyOriginState, function, program,
                                        ref remaining, depth + 1);
                            }
                            if (CarriesRawUnionPayloadFacts(
                                program, argument.Type))
                            {
                                preMoveRawUnionInitializedBytes
                                    = InitializedPayloadBytesOfExpression(
                                        preMoveValue, argument.Type, initialized,
                                        function, program);
                            }
                            if (CarriesRawUnionArrayFacts(program,
                                    argument.Type))
                            {
                                preMoveRawUnionArraySnapshot
                                    = RawUnionArrayFactsOfExpression(
                                        preMoveValue, initialized, function,
                                        program, ref remaining,
                                        depth + 1,
                                        out var preMoveSnapshotState);
                                stagedRawUnionSnapshotState
                                    .Adopt(preMoveSnapshotState);
                            }
                        }
                        if (target.Variables[parameterSlot].IsImplicitRef)
                        {
                            if (target.IsExternal
                                || !IsImplicitReferenceType(target.Variables[parameterSlot].Type, program)
                                || (implicitReferenceTemporary < 0
                                    && !IsSupportedImplicitReferencePlace(argument, function, program)))
                            {
                                throw InvalidEffect(argument.Span, "Ownership");
                            }
                            if (implicitReferenceTemporary >= 0)
                            {
                                var firstLocal = function.ParameterCount + (function.IsConstructor ? 1 : 0);
                                if (implicitReferenceTemporary < firstLocal
                                    || implicitReferenceTemporary >= function.Variables.Length
                                    || !implicitReferenceTemporaries.Add(implicitReferenceTemporary)
                                    || !function.Variables[implicitReferenceTemporary].IsImplicitReferenceTemporary
                                    || function.Variables[implicitReferenceTemporary].Type
                                        != target.Variables[parameterSlot].Type)
                                {
                                    throw InvalidEffect(argument.Span, "Ownership");
                                }
                            }
                        }
                        else if (implicitReferenceTemporary >= 0)
                        {
                            throw InvalidEffect(argument.Span, "Ownership");
                        }
                        if (reservedReceiver >= 0 && InvalidatesReservedOwner(argument, reservedReceiver,
                            ref remaining, depth + 1))
                        {
                            throw InvalidEffect(argument.Span, "Ownership");
                        }
                        if (argument is BoundOutArgument output)
                        {
                            CheckBackingInvalidation(output.Variable, initialized, output.Span);
                            if (output.Variable < 0 || output.Variable >= initialized.Length
                                || (output.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                                    && (!function.Variables[output.Variable].IsOut || output.DeclaresVariable))
                                || output.Type != function.Variables[output.Variable].Type
                                || (output.DeclaresVariable && (!declarations.Add(output.Variable) || initialized[output.Variable]
                                    || function.Variables[output.Variable].IsInitializationLocal))
                                || (!output.DeclaresVariable && !output.IsInit
                                    && !function.Variables[output.Variable].IsMutable)
                                || (output.IsInit && !output.DeclaresVariable && !initialized.IsVacant(output.Variable))
                                || (output.IsInit && !output.DeclaresVariable
                                    && !function.Variables[output.Variable].IsMutable
                                    && !function.Variables[output.Variable].IsInitializationLocal
                                    && initialized.HasRetiredValue(output.Variable))
                                || output.IsInit != target.Variables[parameterSlot].IsInit)
                            {
                                throw Invalid();
                            }
                            if (!outTargets.Add(output.Variable))
                            {
                                throw InvalidEffect(output.Span, "Ownership");
                            }
                            initialized[output.Variable] = false;
                            fieldMasks[output.Variable] = 0;
                            initialized.SetPossibleFieldMask(output.Variable, 0);
                        }
                        else
                        {
                            var isAllowedStorageAddress =
                                argument is BoundNativeStorageAddress matchedDirectParameterAddress
                                    && directNativeParameter is not null
                                    && DirectNativeImportContract.MatchesAddress(
                                        matchedDirectParameterAddress, directNativeParameter, function, program);
                            isAllowedStorageAddress |= isNativeVTableParameterAddress;
                            if (nativeOutputContract is not null)
                            {
                                if (argument is not BoundNativeOutputReservation reservation
                                    || !MatchesReservation(reservation, nativeOutputContract,
                                        function, program)
                                    || !directNativeOutputTargets.Add(reservation.Variable))
                                {
                                    throw InvalidEffect(argument.Span, "NativeImport");
                                }
                                VerifyNativeOutputReservation(reservation, nativeOutputContract,
                                    initialized, fieldMasks, declarations, function, program,
                                    ref remaining, depth + 1, failures);
                            }
                            else if (directNativeOutput is not null)
                            {
                                if (argument is not BoundNativeOutputReservation reservation
                                    || !DirectNativeImportContract.MatchesReservation(
                                        reservation, directNativeOutput, function, program)
                                    || !directNativeOutputTargets.Add(reservation.Variable))
                                {
                                    throw InvalidEffect(argument.Span, "NativeImport");
                                }
                                VerifyNativeOutputReservation(reservation, directNativeOutput,
                                    initialized, fieldMasks, declarations, function, program,
                                    ref remaining, depth + 1, failures);
                            }
                            else if (nativeVTableOutput is not null)
                            {
                                if (argument is not BoundNativeOutputReservation reservation
                                    || !DirectNativeImportContract.MatchesReservation(
                                        reservation, nativeVTableOutput, function, program)
                                    || !directNativeOutputTargets.Add(reservation.Variable))
                                {
                                    throw InvalidEffect(argument.Span, "NativeImport");
                                }
                                VerifyNativeOutputReservation(reservation, nativeVTableOutput,
                                    initialized, fieldMasks, declarations, function, program,
                                    ref remaining, depth + 1, failures);
                            }
                            else if (argument is BoundNativeOutputReservation)
                            {
                                throw InvalidEffect(argument.Span, "NativeImport");
                            }
                            else if (directNativeParameter is not null)
                            {
                                if (argument is BoundNativeStorageAddress address)
                                {
                                    if (!DirectNativeImportContract.MatchesAddress(
                                        address, directNativeParameter, function, program))
                                    {
                                        throw InvalidEffect(argument.Span, "NativeImport");
                                    }
                                    VerifyDirectNativeParameterAddress(address, directNativeParameter,
                                        initialized, fieldMasks, declarations, function, program,
                                        ref remaining, depth + 1, failures);
                                }
                                else if (!DirectNativeImportContract.MatchesNativeParameterArgumentType(
                                        argument.Type, target.Variables[parameterSlot].Type,
                                        directNativeParameter,
                                        program.NativePointerTypes,
                                        DirectNativeImportContract.IsNullNativePointerValue(argument))
                                    && !isDirectNativeStringPointerArgument)
                                {
                                    throw InvalidEffect(argument.Span, "NativeImport");
                                }
                            }
                            else if (nativeVTableParameter is not null)
                            {
                                if (argument is BoundNativeStorageAddress address)
                                {
                                    if (!DirectNativeImportContract.MatchesAddress(
                                        address, nativeVTableParameter, function, program))
                                    {
                                        throw InvalidEffect(argument.Span, "NativeImport");
                                    }
                                    VerifyDirectNativeParameterAddress(address, nativeVTableParameter,
                                        initialized, fieldMasks, declarations, function, program,
                                        ref remaining, depth + 1, failures);
                                }
                                else if (!DirectNativeImportContract.MatchesNativeParameterArgumentType(
                                        argument.Type, target.Variables[parameterSlot].Type,
                                        nativeVTableParameter,
                                        program.NativePointerTypes,
                                        DirectNativeImportContract.IsNullNativePointerValue(argument))
                                    && !isNativeVTableStringPointerArgument)
                                {
                                    throw InvalidEffect(argument.Span, "NativeImport");
                                }
                            }
                            else if (argument is BoundNativeStorageAddress && !isAllowedStorageAddress)
                            {
                                throw InvalidEffect(argument.Span, "NativeImport");
                            }
                            else if (target.IsExternal
                                && IsNativeUtfZPointer(target.Variables[parameterSlot].Type)
                                && !HasValidNativeStringArgument(argument,
                                    target.Variables[parameterSlot].Type, parameterIndex, target, program))
                            {
                                throw InvalidEffect(argument.Span, "NativeImport");
                            }
                            if (IsNativeUtfZPointer(argument.Type)
                                && argument.Type != target.Variables[parameterSlot].Type)
                            {
                                if (!isDirectNativeStringPointerArgument
                                    && !isNativeVTableStringPointerArgument)
                                {
                                    throw InvalidEffect(argument.Span, "NativeImport");
                                }
                            }
                            if (argument is BoundMutableReceiverLoan mutableLoan)
                            {
                                CheckBackingInvalidation(mutableLoan.Variable, initialized, mutableLoan.Span);
                                var bufferMutation = target.Intrinsic is BoundIntrinsic.BufferSet
                                    or BoundIntrinsic.BufferTryAppend or BoundIntrinsic.BufferClear;
                                var structMutation = target.Intrinsic == BoundIntrinsic.None
                                    && target.DeclaringStruct is not null && !target.IsStatic
                                    && !target.IsConstructor && target.Variables[0].IsMutable
                                    && HasFields(program, mutableLoan.Type)
                                    && !ManagedClassTypes.IsClass(mutableLoan.Type);
                                if (parameterIndex != 0 || (!bufferMutation && !structMutation)
                                    || mutableLoan.Variable < 0 || mutableLoan.Variable >= initialized.Length
                                    || (bufferMutation && mutableLoan.Type != PrimitiveType.OwnedInt32Buffer)
                                    || mutableLoan.Type != target.Variables[0].Type
                                    || function.Variables[mutableLoan.Variable].Type != mutableLoan.Type
                                    || !function.Variables[mutableLoan.Variable].IsMutable
                                    || !initialized[mutableLoan.Variable])
                                {
                                    throw InvalidEffect(mutableLoan.Span, "Ownership");
                                }
                                loanRoot = mutableLoan.Variable;
                            }
                            else if (argument is BoundMutableFieldLoan mutableField)
                            {
                                var bufferMutation = target.Intrinsic is BoundIntrinsic.BufferSet
                                    or BoundIntrinsic.BufferTryAppend or BoundIntrinsic.BufferClear;
                                if (parameterIndex != 0 || (target.Intrinsic != BoundIntrinsic.None && !bufferMutation)
                                    || target.DeclaringStruct is null || target.IsStatic || target.IsConstructor
                                    || !target.Variables[0].IsMutable || mutableField.Place is null
                                    || mutableField.Type != target.Variables[0].Type
                                    || mutableField.Place.Type != mutableField.Type
                                    || (bufferMutation && mutableField.Type != PrimitiveType.OwnedInt32Buffer))
                                {
                                    throw InvalidEffect(mutableField.Span, "Ownership");
                                }
                                loanRoot = VerifyMutableFieldPlace(mutableField.Place, initialized,
                                    function, program, ref remaining, depth + 1, bufferMutation);
                                CheckBackingInvalidation(loanRoot, initialized, mutableField.Span);
                            }
                            else if (argument is BoundReceiverLoan loan)
                            {
                                var isReceiverLoan = parameterIndex == 0
                                    && loan.Type == target.Variables[0].Type
                                    && !target.Variables[0].IsMutable;
                                if (!isReceiverLoan
                                    || target.DeclaringStruct is null || target.IsStatic || target.IsConstructor
                                    || loan.Variable < 0 || loan.Variable >= initialized.Length
                                    || !IsUnique(program, loan.Type)
                                    || !initialized[loan.Variable]
                                    || function.Variables[loan.Variable].Type != loan.Type)
                                {
                                    throw InvalidEffect(loan.Span, "Ownership");
                                }
                                loanRoot = loan.Variable;
                            }
                            else if (argument is BoundFieldLoan fieldLoan)
                            {
                                if (parameterIndex != 0 || target.DeclaringStruct is null || target.IsStatic || target.IsConstructor
                                    || fieldLoan.Place is null
                                    || fieldLoan.Type != target.Variables[0].Type
                                    || target.Variables[0].IsMutable
                                    || fieldLoan.Place.Type != fieldLoan.Type || !IsUnique(program, fieldLoan.Type))
                                {
                                    throw InvalidEffect(fieldLoan.Span, "Ownership");
                                }
                                loanRoot = VerifyUniqueFieldPlace(fieldLoan.Place, initialized, fieldMasks,
                                    function, program, ref remaining, depth + 1);
                                loanOfInitializedField = true;
                            }
                            else
                            {
                                if (target.IsExternal
                                    && IsNativeUtfZPointer(target.Variables[parameterSlot].Type))
                                {
                                    if (!HasValidNativeStringArgument(argument,
                                        target.Variables[parameterSlot].Type, parameterIndex, target, program))
                                    {
                                        throw InvalidEffect(argument.Span, "NativeImport");
                                    }
                                }
                                else if (IsNativeUtfZPointer(argument.Type)
                                    && argument.Type != target.Variables[parameterSlot].Type
                                    && !isDirectNativeStringPointerArgument
                                    && !isNativeVTableStringPointerArgument)
                                {
                                    throw InvalidEffect(argument.Span, "NativeImport");
                                }
                                if (argument is not (BoundNativeStorageAddress or BoundNativeOutputReservation))
                                {
                                    if (IsUnique(program, argument.Type)
                                        && argument is not (BoundMove or BoundFieldMove or BoundCall))
                                    {
                                        throw InvalidEffect(argument.Span, "Ownership");
                                    }
                                    if (!EvaluateExpression(argument, initialized, fieldMasks, declarations,
                                        function, program, ref remaining, depth + 1, failures))
                                    {
                                        argumentsComplete = false;
                                        nonCompletingArgumentState
                                            = initialized.Clone(
                                                stagedRawUnionSnapshotState);
                                        nonCompletingArgumentMasks = (uint[])fieldMasks.Clone();
                                    }
                                    else if (directNativeParameter is not null
                                        && argument is not BoundNativeStorageAddress
                                        && !isDirectNativeStringPointerArgument)
                                    {
                                        VerifyNativeParameterValueExtent(argument,
                                            directNativeParameter, call.IsUnsafeAuthorized,
                                            initialized, function, program,
                                            ref remaining, depth + 1);
                                    }
                                    else if (nativeVTableParameter is not null
                                        && argument is not BoundNativeStorageAddress
                                        && !isNativeVTableStringPointerArgument)
                                    {
                                        VerifyNativeParameterValueExtent(argument,
                                            nativeVTableParameter, call.IsUnsafeAuthorized,
                                            initialized, function, program,
                                            ref remaining, depth + 1);
                                    }
                                }
                            }
                        }
                        if (loanRoot >= 0 && parameterIndex == 0)
                        {
                            reservedReceiver = loanRoot;
                        }
                        if (argument.Type != target.Variables[parameterSlot].Type
                            && !(argument is BoundNativeOutputReservation && nativeOutputContract is not null)
                            && !(argument is BoundNativeOutputReservation && directNativeOutput is not null)
                            && !(argument is BoundNativeOutputReservation && nativeVTableOutput is not null)
                            && !isDirectNativeParameterAddress
                            && !isDirectNativeParameterValue
                            && !isDirectNativeStringPointerArgument
                            && !isNativeVTableParameterAddress
                            && !isNativeVTableParameterValue
                            && !isNativeVTableStringPointerArgument)
                        {
                            throw Invalid();
                        }
                        if ((argument is BoundOutArgument)
                            != target.Variables[parameterSlot].IsOut
                            && directNativeOutput is null && nativeVTableOutput is null)
                        {
                            throw Invalid();
                        }
                        if (implicitReferenceTemporary >= 0)
                        {
                            _ = declarations.Add(implicitReferenceTemporary);
                            initialized[implicitReferenceTemporary] = true;
                            fieldMasks[implicitReferenceTemporary] = 0;
                            initialized.SetPossibleFieldMask(implicitReferenceTemporary, 0);
                        }
                        if (argumentsComplete
                            && copyOriginInputViews is not null
                            && copyOriginInputViews.TryGetValue(parameterIndex, out var sourceView))
                        {
                            if (initialized.Dependencies is not BackingDependencies copyOriginState)
                            {
                                throw Invalid(argument.Span);
                            }
                            var snapshotEntryValue = sourceView
                                    == BoundCopyOriginSourceView.EntryValue
                                || implicitReferenceTemporary >= 0;
                            var snapshot = snapshotEntryValue
                                ? preMoveCopyOriginSnapshot ?? CopyOriginValueOfExpression(argument,
                                    argument.Type, copyOriginState, function, program,
                                    ref remaining, depth + 1)
                                : null;
                            var backingSnapshot = snapshotEntryValue
                                ? preMoveBackingSnapshot
                                    ?? BackingFieldValueSnapshotOfExpression(argument,
                                        argument.Type, copyOriginState, function, program,
                                        ref remaining, depth + 1)
                                : null;
                            var nativePointerSnapshot = snapshotEntryValue
                                && CarriesNativePointer(argument.Type, program)
                                    ? preMoveNativePointerSnapshot
                                        ?? NativePointerFieldValueSnapshotOfExpression(
                                            argument, argument.Type, copyOriginState,
                                            function, program, ref remaining, depth + 1)
                                    : null;
                            var referenceSnapshot = snapshotEntryValue
                                ? preMoveReferenceSnapshot
                                    ?? ReferenceFieldValuesOfExpression(argument,
                                        argument.Type, copyOriginState, initialized,
                                        function, program, ref remaining, depth + 1,
                                        fieldMasks)
                                : null;
                            var rawUnionInitializedBytes = snapshotEntryValue
                                && CarriesRawUnionPayloadFacts(
                                    program, argument.Type)
                                    ? preMoveRawUnionInitializedBytes
                                        ?? InitializedPayloadBytesOfExpression(
                                            argument, argument.Type, initialized,
                                            function, program)
                                    : null;
                            SortedDictionary<StorageProjection,
                                InitializationState.RawUnionArrayElementState>?
                                rawUnionArraySnapshot = null;
                            RepeatResourceLedger.StateLease? rawUnionArraySnapshotState
                                = null;
                            if (snapshotEntryValue
                                && CarriesRawUnionArrayFacts(
                                    program, argument.Type))
                            {
                                if (preMoveRawUnionArraySnapshot is not null)
                                {
                                    rawUnionArraySnapshot
                                        = preMoveRawUnionArraySnapshot;
                                }
                                else
                                {
                                    rawUnionArraySnapshot
                                        = RawUnionArrayFactsOfExpression(
                                            argument, initialized, function,
                                            program, ref remaining,
                                            depth + 1,
                                            out rawUnionArraySnapshotState);
                                }
                            }
                            if (rawUnionArraySnapshot is not null)
                            {
                                if (argument is BoundCall
                                        rawUnionInputCall
                                    && initialized.Dependencies
                                        is BackingDependencies
                                            rawUnionInputDependencies
                                    && rawUnionInputDependencies
                                        .TryGetRawUnionArrayCallResult(
                                            rawUnionInputCall,
                                            out var storedRawUnionFacts)
                                    && ReferenceEquals(
                                        rawUnionArraySnapshot,
                                        storedRawUnionFacts))
                                {
                                    rawUnionArraySnapshot
                                        = BackingDependencies
                                            .CloneRawUnionArrayCallResultSnapshot(
                                                storedRawUnionFacts,
                                                stagedRawUnionSnapshotState);
                                }
                                else
                                {
                                    stagedRawUnionSnapshotState.Adopt(
                                        rawUnionArraySnapshotState);
                                }
                            }
                            if (!stagedCopyOriginInputs.TryAdd(parameterIndex,
                                new StagedCopyOriginInput(sourceView, argument, snapshot,
                                    backingSnapshot, nativePointerSnapshot,
                                    referenceSnapshot, rawUnionInitializedBytes,
                                    rawUnionArraySnapshot)))
                            {
                                throw Invalid(argument.Span);
                            }
                        }
                        StageDependencySource(call, parameterIndex, argument, target,
                            initialized, program, ref remaining, depth + 1, fieldMasks);
                    }
                    if (!argumentsComplete)
                    {
                        initialized.CancelUninvokedNativeOutputReservations();
                        break;
                    }
                    if (loanRoot >= 0 && !initialized[loanRoot]
                        && !(loanOfInitializedField
                            && (initialized.HasPartialValue(loanRoot)
                                || (function.IsConstructor && loanRoot == 0))))
                    {
                        throw InvalidEffect(call.Span, "Ownership");
                    }
                    if (directNativeOutputTargets.Overlaps(outTargets))
                    {
                        throw InvalidEffect(call.Span, "NativeImport");
                    }
                    VerifyStagedNativeParameterExtents(call, target,
                        initialized, program, ref remaining, depth + 1);
                    var copyOriginCallResult = CarriesCopyOrigin(call.Type, program)
                        && initialized.Dependencies is BackingDependencies
                        copyOriginResultDependencies
                        ? CreateCopyOriginCallResult(call, target, copyOriginSummary,
                            stagedCopyOriginInputs, copyOriginResultDependencies,
                            function, program, ref remaining, depth + 1)
                        : null;
                    if (CarriesRawUnionPayloadFacts(program, call.Type)
                        && initialized.Dependencies
                            is BackingDependencies rawUnionCallDependencies)
                    {
                        var rawUnionCallCoverage
                            = RawUnionInitializedPayloadBytesForCall(
                                call, target, copyOriginSummary,
                                stagedCopyOriginInputs,
                                initialized.RawUnionReturnCoverageSummaries,
                                program);
                        rawUnionCallDependencies
                            .SetRawUnionCallCoverageResult(call,
                                rawUnionCallCoverage);
                    }
                    if (CarriesRawUnionArrayFacts(program, call.Type)
                        && initialized.Dependencies
                            is BackingDependencies rawUnionArrayDependencies)
                    {
                        var rawUnionArrayCallFacts
                            = RawUnionArrayFactsForCall(call, target,
                                copyOriginSummary, stagedCopyOriginInputs,
                                initialized, function, program,
                                ref remaining, depth + 1,
                                out var rawUnionArrayCallState);
                        try
                        {
                            if (rawUnionArrayCallFacts.Count != 0)
                            {
                                rawUnionArrayDependencies
                                    .SetRawUnionArrayCallResult(call,
                                        rawUnionArrayCallFacts,
                                        rawUnionArrayCallState
                                            ?? throw Invalid());
                                rawUnionArrayCallState = null;
                            }
                            else
                            {
                                rawUnionArrayDependencies
                                    .ClearRawUnionArrayCallResult(call);
                            }
                        }
                        finally
                        {
                            rawUnionArrayCallState?.Dispose();
                        }
                    }
                    var backingCallResult =
                        initialized.Dependencies is BackingDependencies backingResultDependencies
                        ? CreateBackingCallResult(call, target, copyOriginSummary,
                            stagedCopyOriginInputs, backingResultDependencies,
                            function, program, ref remaining, depth + 1)
                        : null;
                    var nativePointerCallResult =
                        initialized.Dependencies is BackingDependencies
                            nativePointerResultDependencies
                        ? CreateNativePointerCallResult(call, target,
                            copyOriginSummary, stagedCopyOriginInputs,
                            nativePointerResultDependencies, function, program,
                            ref remaining, depth + 1)
                        : null;
                    var referenceFieldCallResult =
                        initialized.Dependencies is BackingDependencies
                            referenceFieldResultDependencies
                        ? CreateReferenceFieldCallResult(call, target,
                            copyOriginSummary, stagedCopyOriginInputs,
                            referenceFieldResultDependencies, initialized,
                            fieldMasks, function, program, ref remaining,
                            depth + 1)
                        : null;
                    var referenceCallResult =
                        CheckedReferenceTypeContract.IsValidType(
                            call.Type, program.CheckedReferenceTypes)
                        && initialized.Dependencies is BackingDependencies
                            referenceCallResultDependencies
                        ? CreateReferenceCallResult(call, target,
                            referenceCallResultDependencies, initialized,
                            fieldMasks, function, program, ref remaining,
                            depth + 1)
                        : null;
                    if (referenceFieldCallResult is not null
                        && initialized.Dependencies is BackingDependencies
                            referenceFieldCallDependencies)
                    {
                        referenceFieldCallDependencies.SetReferenceFieldCallResult(
                            call, referenceFieldCallResult);
                    }
                    ApplyReferenceOwnerStoreCallEffects(call, target, initialized,
                        fieldMasks, function, program, ref remaining, depth + 1);
                    ApplyCopyOriginCallEffects(call, target, initialized,
                        fieldMasks, function, program, ref remaining,
                        depth + 1);
                    ApplyRawUnionArrayCallEffects(call, target, initialized,
                        function, program, ref remaining, depth + 1);
                    ApplyBackingCallEffects(call, target, initialized, function,
                        program, ref remaining, depth + 1);
                    ApplyReferenceBackingCallEffects(call, target, initialized,
                        fieldMasks, function, program, ref remaining, depth + 1);
                    ApplyNativePointerCaptureEffects(call, target, initialized, ref remaining);
                    InvalidateNativePointerExtentsForCall(
                        call, target, initialized, program);
                    ApplyNativeOutputCallEffects(call, target, initialized, ref remaining);
                    if (!ProvidesNoFailureGuarantee(target))
                    {
                        failures?.Add(initialized, fieldMasks);
                    }
                    if (copyOriginCallResult is not null
                        && initialized.Dependencies is BackingDependencies resultDependencies)
                    {
                        resultDependencies.SetCopyOriginCallResult(call, copyOriginCallResult);
                    }
                    if (backingCallResult is not null
                        && initialized.Dependencies is BackingDependencies backingCallDependencies)
                    {
                        backingCallDependencies.SetBackingCallResult(call, backingCallResult);
                    }
                    if (nativePointerCallResult is not null
                        && initialized.Dependencies is BackingDependencies pointerResultDependencies)
                    {
                        pointerResultDependencies.SetNativePointerCallResult(call,
                            nativePointerCallResult);
                    }
                    if (referenceCallResult is not null
                        && initialized.Dependencies is BackingDependencies
                            referenceCallDependencies)
                    {
                        referenceCallDependencies.SetReferenceFieldCallResult(call,
                            new Dictionary<StorageProjection, ReferenceFieldValue> {
                                [StorageProjection.WholeObject] = referenceCallResult,
                            });
                    }
                    foreach (var output in outTargets)
                    {
                        if (target.ReturnType == PrimitiveType.Bool)
                        {
                            initialized.MarkConditional(output);
                        }
                        else
                        {
                            initialized[output] = true;
                            var outputType = function.Variables[output].Type;
                            if (HasFields(program, outputType))
                            {
                                fieldMasks[output] = FullMask(program, outputType);
                                initialized.SetPossibleFieldMask(output, fieldMasks[output]);
                            }
                        }
                        SetNormalBackingFactoryObjectIdentity(
                            call, output, function.Variables[output].Type,
                            initialized, succeeds: true);
                    }
                }
                finally
                {
                    if (initialized.Dependencies is BackingDependencies dependencies)
                    {
                        dependencies.Staged.RemoveRange(stagedDependencyCount, dependencies.Staged.Count - stagedDependencyCount);
                    }
                }
                break;
            }
            default:
                throw Invalid();
        }
    }

    private static void VerifyOperatorFunction(BoundFunction function, BoundProgram program)
    {
        if (!function.IsStatic || function.IsConstructor || function.IsClassConstructor
            || function.IsCleanup || function.PropertyName is not null || function.Intrinsic != BoundIntrinsic.None
            || function.DeclaringStruct is not string owner
            || !Binder.TryGetStaticOperatorKey(function.Name, owner, out var key)
            || function.ParameterCount != key.Arity || function.Variables.Length < key.Arity
            || !ValidType(function.ReturnType, allowVoid: false, program))
        {
            throw Invalid();
        }
        var ownerIndex = -1;
        for (var index = 0; index < program.Structures.Length; index++)
        {
            if (program.Structures[index].Name == owner)
            {
                ownerIndex = index;
                break;
            }
        }
        if (ownerIndex < 0)
        {
            throw Invalid();
        }
        var ownerType = (PrimitiveType)(100 + ownerIndex);
        for (var index = 0; index < key.Arity; index++)
        {
            var parameter = function.Variables[index];
            if (parameter.Type != ownerType || parameter.IsMutable || parameter.IsOut || parameter.IsInit)
            {
                throw Invalid();
            }
        }
        if (key.Operator is TokenKind.PlusPlus or TokenKind.MinusMinus
            && function.ReturnType != ownerType)
        {
            throw Invalid();
        }
        if (key.Arity == 2)
        {
            var expectedResult = Binder.IsComparisonOperator(key.Operator) ? PrimitiveType.Bool : ownerType;
            if (function.ReturnType != expectedResult)
            {
                throw Invalid();
            }
        }
    }

    private static bool IsNativeUtfZPointer(PrimitiveType type) =>
        type is PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer;

    private static bool HasValidNativeStringArgument(BoundExpression argument, PrimitiveType parameterType,
        int parameterIndex, BoundFunction target, BoundProgram program) =>
        argument is BoundConstant { Type: var literalType }
        && literalType == parameterType
        && DirectNativeImportContract.ParameterForParameter(target, parameterIndex)
            is DirectNativeParameter parameter
        && DirectNativeImportContract.IsStringPointerArgument(argument, parameter, program);

    private static bool IsImplicitReferenceScalarType(PrimitiveType type) =>
        type is PrimitiveType.Bool or PrimitiveType.Char8 or PrimitiveType.Char16 or PrimitiveType.Char32
            or PrimitiveType.Rune or PrimitiveType.Int8 or PrimitiveType.Int16 or PrimitiveType.Int32
            or PrimitiveType.Int64 or PrimitiveType.UInt8 or PrimitiveType.UInt16 or PrimitiveType.UInt32
            or PrimitiveType.UInt64 or PrimitiveType.Float32 or PrimitiveType.Float64;

    private static bool IsImplicitReferenceType(PrimitiveType type, BoundProgram program) =>
        IsImplicitReferenceScalarType(type) || IsCopyStruct(program, type);

    private static bool IsSupportedImplicitReferencePlace(
        BoundExpression argument,
        BoundFunction function,
        BoundProgram program) =>
        argument switch
        {
            BoundRead read => (uint)read.Variable < (uint)function.Variables.Length
                && function.Variables[read.Variable].Type == read.Type
                && IsImplicitReferenceType(read.Type, program),
            BoundFieldRead field => IsSupportedImplicitReferenceFieldPlace(field, function, program),
            BoundBufferRead buffer => (uint)buffer.Buffer < (uint)program.Buffers.Length
                && program.Buffers[buffer.Buffer].ElementType == buffer.Type
                && IsImplicitReferenceType(buffer.Type, program),
            BoundViewRead view => (uint)view.Variable < (uint)function.Variables.Length
                && function.Variables[view.Variable].Type is PrimitiveType.ReadonlyInt32BufferView
                    or PrimitiveType.MutableInt32BufferView
                && IsImplicitReferenceType(view.Type, program),
            _ => false,
        };

    private static bool IsSupportedImplicitReferenceFieldPlace(
        BoundFieldRead field,
        BoundFunction function,
        BoundProgram program)
    {
        if (!IsImplicitReferenceType(field.Type, program)
            || !IsCopyStruct(program, field.Receiver.Type))
        {
            return false;
        }
        return field.Receiver switch
        {
            BoundRead read => (uint)read.Variable < (uint)function.Variables.Length
                && function.Variables[read.Variable].Type == read.Type
                && IsCopyStruct(program, read.Type),
            BoundFieldRead parent => IsSupportedImplicitReferenceFieldPlace(parent, function, program),
            _ => false,
        };
    }

    private static void VerifyIndirectCall(BoundIndirectCall call, BoundFunction caller, BoundProgram program)
    {
        if (!call.IsUnsafeAuthorized || call.Arguments.IsDefault
            || call.Arguments.Length > MaximumArguments
            || !NativeFunctionPointerTypes.IsType(call.Target.Type)
            || !NativeFunctionPointerContract.IsValidType(
                call.Target.Type, program.NativeFunctionPointerSignatures))
        {
            throw InvalidEffect(call.Span, "UnsafeCaller");
        }

        var signature = program.NativeFunctionPointerSignatures[NativeFunctionPointerTypes.Index(call.Target.Type)];
        var outputSelectorOrdinals = signature.OutputStates
            .Select(static outputState => outputState.SelectorOrdinal).ToHashSet();
        if (signature.Selectors.Any(selector =>
                !outputSelectorOrdinals.Contains(selector.PhaseLocalOrdinal))
            || signature.Dependencies.Any(static dependency =>
                dependency.DestinationKind != 2))
        {
            throw InvalidEffect(call.Span, "NativeImport");
        }
        if (call.Type != signature.ReturnType || call.Arguments.Length != signature.ParameterTypes.Length)
        {
            throw Invalid();
        }
        for (var index = 0; index < call.Arguments.Length; index++)
        {
            var argument = call.Arguments[index] ?? throw Invalid();
            var isNativeOutput = signature.OutputStates.Any(outputState =>
                outputState.FormalOrdinal == (uint)index);
            if (isNativeOutput)
            {
                if (!NativeFunctionPointerContract.TryGetNativeOutputContract(
                        signature, index, program.NativePointerTypes, out var output)
                    || output is null
                    || argument is not BoundNativeOutputReservation reservation
                    || !MatchesReservation(reservation, output, caller, program))
                {
                    throw InvalidEffect(call.Span, "NativeImport");
                }
                continue;
            }

            var nativeParameter = signature.NativeParameters.FirstOrDefault(
                parameter => parameter.ParameterIndex == index);
            var validNativeParameterArgument = nativeParameter is not null
                && (argument is BoundNativeStorageAddress address
                    ? DirectNativeImportContract.MatchesAddress(
                        address, nativeParameter, caller, program)
                    : DirectNativeImportContract.MatchesNativeParameterArgumentType(
                        argument.Type, signature.ParameterTypes[index], nativeParameter,
                        program.NativePointerTypes,
                        DirectNativeImportContract.IsNullNativePointerValue(argument))
                        || DirectNativeImportContract.IsStringPointerArgument(
                            argument, nativeParameter, program));
            if ((nativeParameter is null
                    ? argument.Type != signature.ParameterTypes[index]
                    : !validNativeParameterArgument)
                || IsUnique(program, argument.Type))
            {
                throw InvalidEffect(call.Span, "NativeImport");
            }
        }
    }

    private static bool IsSupportedOptionalType(
        PrimitiveType type,
        BoundProgram program) =>
        SourceOptionalTypes.IsSupported(type, program.CheckedReferenceTypes,
            program.Structures, program.Classes, program.EnumDefinitions.Length,
            program.NativeLoweringAgreement);

    private static bool TryGetOptionalPayloadType(
        PrimitiveType type,
        BoundProgram program,
        out PrimitiveType payloadType)
    {
        if (IsSupportedOptionalType(type, program))
        {
            return SourceOptionalTypes.TryGetRequired(
                type, program.CheckedReferenceTypes, out payloadType);
        }
        payloadType = default;
        return false;
    }

    private static void SetOptionalReferencePatternFacts(
        BoundExpression value,
        int variable,
        InitializationState initialized,
        uint[] fieldMasks,
        BoundFunction function,
        BoundProgram program,
        ref int remaining)
    {
        if (!CheckedReferenceTypeContract.IsValidType(
                value.Type, program.CheckedReferenceTypes)
            || !program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(value.Type)].IsOptional)
        {
            return;
        }
        if (initialized.Dependencies is not BackingDependencies facts)
        {
            throw InvalidEffect(value.Span, "CheckedReference");
        }

        var targets = ReferenceTargetsOfExpression(value, facts, initialized,
            function, program, ref remaining, 0, fieldMasks);
        var obligations = ReferenceObligationsOfExpression(value, facts,
            initialized, function, program, ref remaining, 0, fieldMasks);
        var referenceValue = ReferenceFieldValueOfDescriptorExpression(value,
            facts, initialized, function, program, ref remaining, 0, fieldMasks);
        if (!referenceValue.IsAvailable)
        {
            throw InvalidEffect(value.Span, "CheckedReference");
        }
        facts.SetReferenceTargets(variable, targets);
        facts.SetReferenceObligations(variable, obligations);
        facts.SetReferenceBackingSources(variable, referenceValue.BackingSources);
    }

    private static void CopyOptionalReferencePatternFacts(
        BoundExpression condition,
        InitializationState source,
        InitializationState destination,
        BoundProgram program)
    {
        if (source.Dependencies is not BackingDependencies sourceFacts
            || destination.Dependencies is not BackingDependencies destinationFacts)
        {
            return;
        }

        var pending = new Stack<BoundExpression>();
        pending.Push(condition);
        var remaining = MaximumAnalysisVisits;
        while (pending.TryPop(out var expression))
        {
            if (--remaining < 0 || !expression.HasValidNestedExpressions)
            {
                throw InvalidEffect(condition.Span, "CheckedReference");
            }

            var value = expression switch {
                BoundIsSome pattern => pattern.Value,
                BoundIsVariablePattern pattern => pattern.Value,
                _ => null,
            };
            var variable = expression switch {
                BoundIsSome pattern => pattern.Variable,
                BoundIsVariablePattern pattern => pattern.Variable,
                _ => -1,
            };
            if (value is not null && (uint)variable < (uint)destination.Length
                && destination[variable]
                && CheckedReferenceTypeContract.IsValidType(
                    value.Type, program.CheckedReferenceTypes)
                && program.CheckedReferenceTypes[
                    CheckedReferenceTypes.Index(value.Type)].IsOptional)
            {
                destinationFacts.SetReferenceTargets(
                    variable, sourceFacts.ReferenceTargets[variable]
                        ?? EmptyBackingSources);
                destinationFacts.SetReferenceObligations(
                    variable, sourceFacts.ReferenceObligations[variable]
                        ?? EmptyBackingSources);
                destinationFacts.SetReferenceBackingSources(
                    variable, sourceFacts.ReferenceBackingSources[variable]
                        ?? EmptyBackingSources);
            }

            foreach (var nested in expression.NestedExpressions)
            {
                pending.Push(nested);
            }
        }
    }

    private static bool ValidType(PrimitiveType type, bool allowVoid, BoundProgram program) =>
        type is PrimitiveType.Char8 or PrimitiveType.Char16 or PrimitiveType.Char32 or PrimitiveType.Rune
            or PrimitiveType.Int8 or PrimitiveType.Int16 or PrimitiveType.Int32 or PrimitiveType.UInt8
            or PrimitiveType.UInt16 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
            or PrimitiveType.Int128 or PrimitiveType.UInt128 or PrimitiveType.Float16 or PrimitiveType.BFloat16
            or PrimitiveType.Float32 or PrimitiveType.Float64 or PrimitiveType.Float128
            or PrimitiveType.Decimal32 or PrimitiveType.Decimal64 or PrimitiveType.Decimal128
            or PrimitiveType.Bool or PrimitiveType.OwnedInt32Buffer or PrimitiveType.TokenSpan
            or PrimitiveType.ManagedNode or PrimitiveType.OptionalManagedNode or PrimitiveType.NativeUtf16ZPointer
            or PrimitiveType.NativeUtf8ZPointer
            or PrimitiveType.NativeStorageAddress
            or PrimitiveType.NativeRawPointer
            || NativeFixedArrayContract.IsCarrier(type)
            || (allowVoid && type == PrimitiveType.Void)
            || ((int)type >= 100 && (int)type - 100 < program.Structures.Length)
            || BoundEnumTypes.IsValidType(type, program.EnumDefinitions)
            || NativePointerTypeContract.IsValidType(type, program.NativePointerTypes)
            || NativeFunctionPointerContract.IsValidType(type, program.NativeFunctionPointerSignatures)
            || BoundGenericNominalTypeContract.IsValidType(type, program)
            || CheckedReferenceTypeContract.IsValidType(type, program.CheckedReferenceTypes)
            || (ManagedClassTypes.IsManaged(type) && (ManagedClassTypes.Readonly(type) is
                PrimitiveType.ManagedNode or PrimitiveType.OptionalManagedNode))
            || ((ManagedClassTypes.IsClass(type) || (ManagedClassTypes.IsOptional(type)
                && type is not (PrimitiveType.OptionalManagedNode or PrimitiveType.OptionalWriteManagedNode)))
                && ManagedClassTypes.ClassIndex(type) < program.Classes.Length
                && (!ManagedClassTypes.IsWritable(type)
                    || program.Classes[ManagedClassTypes.ClassIndex(type)].IsMutable));

    internal static bool IsValidType(PrimitiveType type, bool allowVoid, BoundProgram program) =>
        ValidType(type, allowVoid, program);

    private static bool IsValidSourceGenericTypeTables(BoundProgram program)
    {
        var definitions = program.SourceGenericTypeDefinitions;
        var instances = program.SourceGenericTypeInstances;
        if (definitions.IsDefault || definitions.Length > MaximumAnalysisVisits
            || instances.IsDefault || instances.Length > MaximumAnalysisVisits)
        {
            return false;
        }

        var definitionNames = new HashSet<string>(StringComparer.Ordinal);
        long rowCount = 0;
        for (var definitionIndex = 0; definitionIndex < definitions.Length;
            definitionIndex++)
        {
            var definition = definitions[definitionIndex];
            if (definition is null || definition.DefinitionIndex != definitionIndex
                || definition.Namespace is null || definition.Name is null
                || definition.QualifiedName is null
                || string.IsNullOrWhiteSpace(definition.Name)
                || (definition.Namespace.Length > 0
                    && string.IsNullOrWhiteSpace(definition.Namespace))
                || definition.Formals.IsDefaultOrEmpty
                || definition.Formals.Length > MaximumAnalysisVisits
                || definition.QualifiedName != (definition.Namespace.Length == 0
                    ? definition.Name
                    : definition.Namespace + "." + definition.Name)
                || !definitionNames.Add(definition.QualifiedName))
            {
                return false;
            }

            var formalNames = new HashSet<string>(StringComparer.Ordinal);
            rowCount += definition.Formals.Length;
            if (rowCount > MaximumAnalysisVisits)
            {
                return false;
            }
            for (var ordinal = 0; ordinal < definition.Formals.Length; ordinal++)
            {
                var formal = definition.Formals[ordinal];
                if (formal is null || formal.Ordinal != ordinal
                    || string.IsNullOrWhiteSpace(formal.Name)
                    || !formalNames.Add(formal.Name)
                    || formal.OwnershipDomain is < 1 or > 3
                    || formal.NullabilityDomain is < 1 or > 2)
                {
                    return false;
                }
            }
        }

        var mappedStructures = new HashSet<int>();
        foreach (var instance in instances)
        {
            if (instance is null || (uint)instance.StructureIndex
                    >= (uint)program.Structures.Length
                || (uint)instance.DefinitionIndex >= (uint)definitions.Length
                || instance.TypeArguments.IsDefault
                || instance.TypeArguments.Length
                    != definitions[instance.DefinitionIndex].Formals.Length
                || !mappedStructures.Add(instance.StructureIndex)
                || program.Structures[instance.StructureIndex] is not BoundStruct structure
                || structure.SourceGenericTypeDefinitionIndex
                    != instance.DefinitionIndex)
            {
                return false;
            }
            rowCount += instance.TypeArguments.Length;
            if (rowCount > MaximumAnalysisVisits)
            {
                return false;
            }
        }

        for (var structureIndex = 0; structureIndex < program.Structures.Length;
            structureIndex++)
        {
            var structure = program.Structures[structureIndex];
            if (structure is null)
            {
                return false;
            }
            var definitionIndex = structure.SourceGenericTypeDefinitionIndex;
            if (definitionIndex < -1
                || definitionIndex >= definitions.Length
                || (definitionIndex >= 0)
                    != mappedStructures.Contains(structureIndex))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsClassArgumentType(PrimitiveType type, IReadOnlyList<BoundStruct> structures,
        int enumDefinitionCount = 0) =>
        type is PrimitiveType.Char8 or PrimitiveType.Char16 or PrimitiveType.Char32 or PrimitiveType.Rune
            or PrimitiveType.Int8 or PrimitiveType.Int16 or PrimitiveType.Int32 or PrimitiveType.UInt8
            or PrimitiveType.UInt16 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
            or PrimitiveType.Int128 or PrimitiveType.UInt128 or PrimitiveType.Float16 or PrimitiveType.BFloat16
            or PrimitiveType.Float32 or PrimitiveType.Float64 or PrimitiveType.Float128
            or PrimitiveType.Decimal32 or PrimitiveType.Decimal64 or PrimitiveType.Decimal128 or PrimitiveType.Bool
            || NativePointerTypes.IsType(type)
            || NativeFunctionPointerTypes.IsType(type)
            || ManagedClassTypes.IsManaged(type)
            || BoundEnumTypes.IsValidType(type, enumDefinitionCount)
            || ((int)type >= 100 && (int)type - 100 < structures.Count
                && !structures[(int)type - 100].IsUnique);

    private static bool IsUnique(BoundProgram program, PrimitiveType type) =>
        type == PrimitiveType.OwnedInt32Buffer
            || BoundGenericNominalTypeContract.IsCoreTaskType(type, program)
            || ((int)type >= 100 && (int)type - 100 < program.Structures.Length
                && program.Structures[(int)type - 100].IsUnique);

    private static bool RequiresUnsafeFieldAccess(BoundProgram program,
        PrimitiveType ownerType, BoundStructField field)
        => field.RequiresUnsafeCaller || IsUnion(program, ownerType);

    internal static bool IsRawUnionType(BoundProgram program,
        PrimitiveType type)
    {
        var typeValue = (int)type;
        if (typeValue is < 100 or >= BoundEnumTypes.First)
        {
            return false;
        }

        var index = typeValue - 100;
        return (uint)index < (uint)program.Structures.Length
            && program.Structures[index].IsUnion;
    }

    private static bool CarriesRawUnionPayloadFacts(
        BoundProgram program, PrimitiveType type)
    {
        var pending = new Stack<PrimitiveType>();
        var visited = new HashSet<PrimitiveType>();
        pending.Push(type);
        var carriesRawUnion = false;
        while (pending.TryPop(out var current))
        {
            if (IsRawUnionType(program, current))
            {
                carriesRawUnion = true;
                continue;
            }
            if (NativeFixedArrayContract.IsCarrier(current)
                || ManagedClassTypes.IsClass(current))
            {
                return false;
            }
            if (!HasFields(program, current) || !visited.Add(current))
            {
                continue;
            }

            foreach (var field in Fields(program, current))
            {
                if (NativeFixedArrayContract.IsCarrier(field.Type))
                {
                    return false;
                }
                pending.Push(field.Type);
            }
        }

        return carriesRawUnion;
    }

    private static bool CarriesRawUnionArrayFacts(
        BoundProgram program, PrimitiveType type)
    {
        var pending = new Stack<(PrimitiveType Type,
            bool IsArrayElement)>();
        var visited = new HashSet<(PrimitiveType Type,
            bool IsArrayElement)>();
        pending.Push((type, false));
        while (pending.TryPop(out var item))
        {
            if (NativeFixedArrayTypes.IsType(item.Type))
            {
                if (!NativeFixedArrayContract.TryGetLayout(item.Type,
                        program.Structures, program.NativeFixedArrayTypes,
                        out var layout))
                {
                    throw InvalidEffect(default, "NativeImport");
                }
                if (layout.ElementCount != 0)
                {
                    pending.Push((layout.ElementType,
                        IsArrayElement: true));
                }
                continue;
            }

            if (IsRawUnionType(program, item.Type))
            {
                if (item.IsArrayElement)
                {
                    return true;
                }
                continue;
            }
            if (!HasFields(program, item.Type)
                || !visited.Add(item))
            {
                continue;
            }

            foreach (var field in Fields(program, item.Type))
            {
                pending.Push((field.Type, item.IsArrayElement));
            }
        }

        return false;
    }

    private static bool IsUnion(BoundProgram program, PrimitiveType type)
    {
        if (ManagedClassTypes.IsClass(type) || BoundEnumTypes.IsType(type)
            || BoundGenericNominalTypeTypes.IsType(type))
        {
            return false;
        }

        var index = (int)type - 100;
        return (uint)index < (uint)program.Structures.Length
            && program.Structures[index].IsUnion;
    }

    private static bool IsManagedLifetimeValue(BoundProgram program, PrimitiveType type)
    {
        if (ManagedClassTypes.IsManaged(type))
        {
            return true;
        }
        if (program.NativeLoweringAgreement
                != CompilationOptions.WindowsX64NormalBackingAgreement)
        {
            return false;
        }
        return ContainsNormalBackingReference(program, type, []);
    }

    private static bool ContainsNormalBackingReference(
        BoundProgram program, PrimitiveType type, HashSet<int> activeStructures)
    {
        if (CheckedReferenceTypes.IsType(type))
        {
            if (!CheckedReferenceTypeContract.IsValidType(
                type, program.CheckedReferenceTypes))
            {
                return false;
            }
            var reference = program.CheckedReferenceTypes[
                CheckedReferenceTypes.Index(type)];
            return !reference.IsGarbageCollected && !reference.IsInitialization
                && !reference.IsOptional;
        }

        var structureIndex = (int)type - 100;
        if (structureIndex < 0 || structureIndex >= program.Structures.Length
            || ManagedClassTypes.IsClass(type) || BoundEnumTypes.IsType(type)
            || !activeStructures.Add(structureIndex))
        {
            return false;
        }
        var contains = program.Structures[structureIndex].Fields.Any(field =>
            ContainsNormalBackingReference(program, field.Type, activeStructures));
        _ = activeStructures.Remove(structureIndex);
        return contains;
    }

    private static bool IsCopyStruct(BoundProgram program, PrimitiveType type) =>
        (int)type >= 100 && (int)type - 100 < program.Structures.Length
            && !program.Structures[(int)type - 100].IsUnique;

    private static void VerifyClassFactory(BoundProgram program, BoundFunction function, int index)
    {
        var constructorIndex = function.ClassConstructor;
        if (constructorIndex < 0 || constructorIndex >= program.Functions.Length || constructorIndex == index
            || function.DeclaringStruct is not string name
            || function.Name != name + ".TryCreate"
            || function.ReturnType != PrimitiveType.Bool || function.IsConstructor
            || function.IsClassConstructor || function.IsExternal
            || !function.IsStatic || function.IsCleanup || function.DoesNotReturn
            || function.Body.Statements.IsDefault || !function.Body.Statements.IsEmpty
            || function.ParameterCount < 1 || function.ParameterCount > MaximumArguments
            || function.Variables.Length != function.ParameterCount + 1)
        {
            throw Invalid();
        }
        var constructor = program.Functions[constructorIndex];
        var type = function.Variables[function.ParameterCount - 1].Type;
        if (constructor is null || constructor.Variables.IsDefaultOrEmpty
            || constructor.ParameterCount < 1 || constructor.ParameterCount > MaximumArguments
            || constructor.Variables.Length < constructor.ParameterCount
            || !ManagedClassTypes.IsClass(type)
            || ManagedClassTypes.ClassIndex(type) >= program.Classes.Length
            || program.Classes[ManagedClassTypes.ClassIndex(type)].Name != name
            || ManagedClassTypes.IsWritable(type) != program.Classes[ManagedClassTypes.ClassIndex(type)].IsMutable
            || !constructor.IsClassConstructor || constructor.DeclaringStruct != name
            || function.IsPrivate != constructor.IsPrivate
            || function.IsInternal != !constructor.IsPrivate
            || constructor.ParameterCount != function.ParameterCount
            || constructor.Variables[0].Type != type
            || function.Variables[function.ParameterCount - 1] is not { IsOut: true, IsMutable: false }
            || function.Variables[function.ParameterCount] is not { IsOut: false, IsMutable: false }
            || function.Variables[function.ParameterCount].Type != type
            || function.NoFailureProof != constructor.NoFailureProof
            || function.RequiresUnsafeCaller != constructor.RequiresUnsafeCaller)
        {
            throw Invalid();
        }
        for (var parameter = 0; parameter < function.ParameterCount - 1; parameter++)
        {
            if (function.Variables[parameter].Type != constructor.Variables[parameter + 1].Type
                || function.Variables[parameter].IsOut || function.Variables[parameter].IsMutable
                || constructor.Variables[parameter + 1].IsOut
                || !IsClassArgumentType(function.Variables[parameter].Type, program.Structures,
                    program.EnumDefinitions.Length))
            {
                throw Invalid();
            }
        }
    }

    private static bool InvalidatesReservedOwner(BoundExpression expression, int variable, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        if (expression is BoundCall call)
        {
            foreach (var argument in call.Arguments)
            {
                if (InvalidatesReservedOwner(argument, variable, ref remaining, depth + 1))
                {
                    return true;
                }
            }
            return false;
        }
        return expression switch {
            BoundUnsafeExpression unsafeExpression => InvalidatesReservedOwner(
                unsafeExpression.Expression, variable, ref remaining, depth + 1),
            BoundMove move => move.Variable == variable,
            BoundFieldMove move => TryGetFieldRootVariable(move.Place, out var rootVariable)
                && rootVariable == variable,
            BoundOutArgument output => output.Variable == variable,
            BoundMutableReceiverLoan loan => loan.Variable == variable,
            BoundMutableFieldLoan loan => LoanRoot(loan, ref remaining, depth + 1) == variable,
            BoundUnary unary => InvalidatesReservedOwner(unary.Operand, variable, ref remaining, depth + 1),
            BoundUpdate update => WritesUpdateRoot(update.Place, variable, ref remaining, depth + 1)
                || InvalidatesReservedOwner(update.Place, variable, ref remaining, depth + 1),
            BoundNominalUpdate update => WritesUpdateRoot(update.Place, variable, ref remaining, depth + 1)
                || InvalidatesReservedOwner(update.Place, variable, ref remaining, depth + 1),
            BoundCompoundAssignment compound => WritesUpdateRoot(compound.Place, variable, ref remaining, depth + 1)
                || InvalidatesReservedOwner(compound.Place, variable, ref remaining, depth + 1)
                || InvalidatesReservedOwner(compound.Right, variable, ref remaining, depth + 1),
            BoundCast cast => InvalidatesReservedOwner(cast.Operand, variable, ref remaining, depth + 1),
            BoundOptionalLift lift => InvalidatesReservedOwner(lift.Value, variable, ref remaining, depth + 1),
            BoundReadonlyProjection projection => InvalidatesReservedOwner(projection.Value, variable, ref remaining, depth + 1),
            BoundIsNull test => InvalidatesReservedOwner(test.Value, variable, ref remaining, depth + 1),
            BoundCoalesce coalesce => InvalidatesReservedOwner(coalesce.Optional, variable, ref remaining, depth + 1)
                || InvalidatesReservedOwner(coalesce.Fallback, variable, ref remaining, depth + 1),
            BoundIsSome pattern => InvalidatesReservedOwner(pattern.Value, variable, ref remaining, depth + 1),
            BoundIsVariablePattern pattern => InvalidatesReservedOwner(pattern.Value, variable, ref remaining, depth + 1),
            BoundBinary binary => InvalidatesReservedOwner(binary.Left, variable, ref remaining, depth + 1)
                || InvalidatesReservedOwner(binary.Right, variable, ref remaining, depth + 1),
            BoundConditional conditional => InvalidatesReservedOwner(conditional.Condition, variable, ref remaining, depth + 1)
                || InvalidatesReservedOwner(conditional.WhenTrue, variable, ref remaining, depth + 1)
                || InvalidatesReservedOwner(conditional.WhenFalse, variable, ref remaining, depth + 1),
            BoundFieldRead field => InvalidatesReservedOwner(field.Receiver, variable, ref remaining, depth + 1),
            BoundBufferRead buffer => InvalidatesReservedOwner(buffer.Index, variable, ref remaining, depth + 1),
            BoundViewRead view => InvalidatesReservedOwner(view.Index, variable, ref remaining, depth + 1),
            BoundSpanLength length => InvalidatesReservedOwner(length.View, variable, ref remaining, depth + 1),
            BoundSpanRead read => InvalidatesReservedOwner(read.View, variable, ref remaining, depth + 1)
                || InvalidatesReservedOwner(read.Index, variable, ref remaining, depth + 1),
            BoundSpanAdvance advance => InvalidatesReservedOwner(advance.View, variable, ref remaining, depth + 1),
            _ => false,
        };
    }

    private static bool WritesUpdateRoot(BoundExpression place, int variable, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        return place switch {
            BoundRead read => read.Variable == variable,
            BoundFieldRead field => WritesUpdateRoot(field.Receiver, variable, ref remaining, depth + 1),
            _ => false,
        };
    }

    private static int UpdateRoot(BoundExpression place, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        return place switch {
            BoundRead read => read.Variable,
            BoundFieldRead field when !ManagedClassTypes.IsClass(field.Receiver.Type) =>
                UpdateRoot(field.Receiver, ref remaining, depth + 1),
            _ => -1,
        };
    }

    private static bool IsProvenBufferIndex(int buffer, BoundExpression index, BoundProgram program) =>
        buffer >= 0 && buffer < program.Buffers.Length
            && !program.Buffers[buffer].IsInvocationScoped
            && index is BoundConstant { Type: PrimitiveType.Int32 } constant
            && constant.Value >= 0 && constant.Value < program.Buffers[buffer].Values.Length;

    private static bool IsTotalConversion(PrimitiveType source, PrimitiveType destination)
    {
        if (source == destination)
        {
            return true;
        }
        if (destination == PrimitiveType.Int128)
        {
            return PrimitiveTypeFacts.IsInteger(source) || source == PrimitiveType.Char16;
        }
        if (destination == PrimitiveType.UInt128)
        {
            return source == PrimitiveType.Char16
                || (PrimitiveTypeFacts.IsInteger(source) && !PrimitiveTypeFacts.IsSignedInteger(source));
        }
        if (destination is PrimitiveType.Float32 or PrimitiveType.Float64)
        {
            return PrimitiveTypeFacts.IsInteger(source) || source == PrimitiveType.Char16
                || source is PrimitiveType.Float32 or PrimitiveType.Float64;
        }
        if (source == PrimitiveType.Char16)
        {
            if (!PrimitiveTypeFacts.IsInteger(destination))
            {
                return false;
            }
            var codeUnitDestinationWidth = PrimitiveTypeFacts.IntegerWidth(destination);
            return PrimitiveTypeFacts.IsSignedInteger(destination)
                ? codeUnitDestinationWidth >= 32 : codeUnitDestinationWidth >= 16;
        }
        if (!PrimitiveTypeFacts.IsInteger(source)
            || !(PrimitiveTypeFacts.IsInteger(destination) || destination == PrimitiveType.Char16))
        {
            return false;
        }

        var sourceWidth = PrimitiveTypeFacts.IntegerWidth(source);
        var destinationWidth = destination == PrimitiveType.Char16
            ? 16 : PrimitiveTypeFacts.IntegerWidth(destination);
        var sourceSigned = PrimitiveTypeFacts.IsSignedInteger(source);
        var destinationSigned = destination != PrimitiveType.Char16
            && PrimitiveTypeFacts.IsSignedInteger(destination);
        if (sourceSigned)
        {
            return destinationSigned && destinationWidth >= sourceWidth;
        }
        return destinationSigned ? destinationWidth > sourceWidth : destinationWidth >= sourceWidth;
    }

    private static uint FullMask(BoundProgram program, PrimitiveType type)
    {
        var count = Fields(program, type).Length;
        return count == 32 ? uint.MaxValue : (1u << count) - 1;
    }

    private static byte[] GuaranteedInitializedPayloadBytes(
        PrimitiveType type, BoundProgram program)
    {
        var remaining = MaximumAnalysisVisits;
        return GuaranteedInitializedPayloadBytes(type, program, [], ref remaining,
            depth: 0);
    }

    internal static byte[] RawUnionGuaranteedInitializedPayloadBytes(
        PrimitiveType type, BoundProgram program)
    {
        if (!IsRawUnionType(program, type))
        {
            throw Invalid();
        }

        return GuaranteedInitializedPayloadBytes(type, program);
    }

    private static bool TryGetFieldPathOffset(PrimitiveType rootType,
        FieldProjectionPath fieldPath, BoundProgram program,
        out ulong byteOffset)
    {
        byteOffset = 0;
        var currentType = rootType;
        foreach (var segment in fieldPath.Segments)
        {
            if (segment.OwnerType != currentType
                || !HasFields(program, currentType)
                || (uint)segment.Field
                    >= (uint)Fields(program, currentType).Length)
            {
                return false;
            }

            var field = Fields(program, currentType)[segment.Field];
            byteOffset = checked(byteOffset + field.Offset);
            currentType = field.Type;
        }

        return !fieldPath.Segments.IsDefaultOrEmpty;
    }

    private static bool TryGetFieldPathByteOffset(PrimitiveType rootType,
        FieldProjectionPath fieldPath, BoundProgram program,
        out int byteOffset)
    {
        byteOffset = 0;
        if (!TryGetFieldPathOffset(rootType, fieldPath, program,
                out var offset))
        {
            return false;
        }

        byteOffset = MaterializedPayloadOffset(offset);
        return true;
    }

    private static byte[] RawUnionInitializedPayloadBytesForCall(
        BoundCall call, BoundFunction target,
        BoundCopyOriginSummary? copyOriginSummary,
        IReadOnlyDictionary<int, StagedCopyOriginInput> stagedInputs,
        ImmutableArray<byte>[] returnCoverageSummaries,
        BoundProgram program)
    {
        if (call.Type != target.ReturnType)
        {
            throw Invalid(call.Span);
        }

        var resultSize = MaterializedPayloadSize(call.Type, program);

        var initializedBytes = GuaranteedInitializedPayloadBytes(
            call.Type, program);
        if ((uint)call.Function < (uint)returnCoverageSummaries.Length)
        {
            var returnCoverage = returnCoverageSummaries[call.Function];
            if (!returnCoverage.IsDefaultOrEmpty)
            {
                if (returnCoverage.Length != resultSize)
                {
                    throw Invalid(call.Span);
                }
                initializedBytes = [.. returnCoverage];
            }
        }
        if (copyOriginSummary is null)
        {
            return initializedBytes;
        }

        ValidateCopyOriginSummary(target, copyOriginSummary, program);
        foreach (var fact in copyOriginSummary.Facts)
        {
            if (!TryResolveCopyOriginFieldPath(target.ReturnType,
                    fact.DestinationPath, program,
                    out var destinationType)
                || !CarriesRawUnionPayloadFacts(program, destinationType))
            {
                continue;
            }

            if (!TryGetCopyOriginPathByteOffset(target.ReturnType,
                    fact.DestinationPath, program, out var destinationOffset))
            {
                throw Invalid(call.Span);
            }

            var destinationSize = MaterializedPayloadSize(
                destinationType, program);
            if (destinationOffset > initializedBytes.Length
                || destinationSize > initializedBytes.Length - destinationOffset)
            {
                throw Invalid(call.Span);
            }

            byte[]? commonSourceBytes = null;
            var canForwardCoverage = true;
            foreach (var origin in fact.Origins)
            {
                var parameterIndex = CopyOriginCallParameterIndex(target,
                    origin);
                if (origin.SourceKind != BoundCopyOriginSourceKind.Formal
                    || origin.SourceView
                        != BoundCopyOriginSourceView.EntryValue
                    || !stagedInputs.TryGetValue(parameterIndex,
                        out var stagedInput)
                    || stagedInput.SourceView != origin.SourceView
                    || stagedInput.RawUnionInitializedBytes is not { } sourceBytes
                    || !TryGetCopyOriginSourceType(target, origin, program,
                        out var sourceRootType)
                    || stagedInput.Argument.Type != sourceRootType
                    || !TryResolveCopyOriginFieldPath(sourceRootType,
                        origin.SourcePath, program, out var sourceType)
                    || sourceType != destinationType)
                {
                    canForwardCoverage = false;
                    break;
                }

                var sourceSize = MaterializedPayloadSize(
                    sourceRootType, program);
                if (sourceBytes.Length != sourceSize
                    || !TryGetCopyOriginPathByteOffset(sourceRootType,
                        origin.SourcePath, program, out var sourceOffset))
                {
                    throw Invalid(call.Span);
                }
                if (sourceOffset > sourceBytes.Length
                    || destinationSize > sourceBytes.Length - sourceOffset)
                {
                    throw Invalid(call.Span);
                }

                var currentSourceBytes = sourceBytes.AsSpan(sourceOffset,
                    destinationSize);
                if (commonSourceBytes is null)
                {
                    commonSourceBytes = currentSourceBytes.ToArray();
                }
                else
                {
                    for (var index = 0; index < commonSourceBytes.Length; index++)
                    {
                        commonSourceBytes[index] &= currentSourceBytes[index];
                    }
                }
            }

            if (canForwardCoverage && commonSourceBytes is not null)
            {
                for (var index = 0; index < commonSourceBytes.Length; index++)
                {
                    if (commonSourceBytes[index] != 0)
                    {
                        initializedBytes[destinationOffset + index] = 1;
                    }
                }
            }
        }

        return initializedBytes;
    }

    private static bool TryGetCopyOriginPathByteOffset(
        PrimitiveType rootType, BoundCopyOriginFieldPath path,
        BoundProgram program, out int byteOffset)
    {
        byteOffset = 0;
        var fieldPath = default(FieldProjectionPath);
        foreach (var step in path.Steps)
        {
            fieldPath = fieldPath.Append(step.OwnerType, step.FieldOrdinal);
        }
        return path.Steps.IsEmpty
            || TryGetFieldPathByteOffset(rootType, fieldPath, program,
                out byteOffset);
    }

    private static byte[] InitializedPayloadBytesOfExpression(
        BoundExpression expression, PrimitiveType type,
        InitializationState initialized, BoundFunction function,
        BoundProgram program)
    {
        if (!CarriesRawUnionPayloadFacts(program, type))
        {
            return GuaranteedInitializedPayloadBytes(type, program);
        }

        if (expression is BoundCall call
            && call.Type == type)
        {
            if (initialized.Dependencies is BackingDependencies
                    callDependencies
                && callDependencies.TryGetRawUnionCallCoverageResult(
                    call, out var callInitializedBytes))
            {
                return callInitializedBytes;
            }
            if ((uint)call.Function
                    < (uint)initialized.RawUnionReturnCoverageSummaries.Length
                && initialized.RawUnionReturnCoverageSummaries[
                    call.Function].Length
                    == MaterializedPayloadSize(type, program))
            {
                return [.. initialized.RawUnionReturnCoverageSummaries[
                    call.Function]];
            }
        }

        if (expression is BoundReferenceRead referenceRead
            && referenceRead.Type == type)
        {
            return InitializedRawUnionPayloadBytesOfReferenceRead(
                referenceRead, type, initialized, function, program);
        }

        if (expression is BoundRead read && read.Type == type
            && initialized.TryGetRawUnionInitializedBytes(read.Variable,
                out var readBytes))
        {
            return readBytes;
        }
        if (expression is BoundMove move && move.Type == type
            && initialized.TryGetRawUnionInitializedBytes(move.Variable,
                out var moveBytes))
        {
            return moveBytes;
        }
        if (expression is BoundConditional conditional
            && conditional.Type == type
            && conditional.WhenTrue.Type == type
            && conditional.WhenFalse.Type == type)
        {
            var whenTrue = InitializedPayloadBytesOfExpression(
                conditional.WhenTrue, type, initialized, function, program);
            var whenFalse = InitializedPayloadBytesOfExpression(
                conditional.WhenFalse, type, initialized, function, program);
            if (whenTrue.Length != whenFalse.Length)
            {
                throw Invalid(conditional.Span);
            }
            for (var index = 0; index < whenTrue.Length; index++)
            {
                whenTrue[index] &= whenFalse[index];
            }
            return whenTrue;
        }
        if (expression is BoundNativeFixedArrayElement fixedArrayElement
            && fixedArrayElement.Type == type)
        {
            if (!TryGetNativeFixedArrayArrayPrefix(
                    fixedArrayElement.Variable,
                    fixedArrayElement.FieldPlace,
                    fixedArrayElement.Layout, function, program,
                    out var arrayPrefix))
            {
                throw Invalid(fixedArrayElement.Span);
            }
            var elementIndex
                = NativeFixedArrayContract.TryGetUnsignedIndex(
                    fixedArrayElement.Index, out var selectedIndex)
                    ? selectedIndex : (ulong?)null;
            return initialized.RawUnionArrayElementBytes(
                fixedArrayElement.Variable, arrayPrefix,
                fixedArrayElement.Layout, elementIndex);
        }
        if (expression is BoundNativeFixedArrayPathElement pathElement
            && pathElement.Type == type
            && !pathElement.Projections.IsDefaultOrEmpty)
        {
            var parentProjections = pathElement.Projections.Length == 1
                ? [] : pathElement.Projections.RemoveAt(
                    pathElement.Projections.Length - 1);
            if (!TryGetNativeFixedArrayArrayPrefix(
                    pathElement.Variable, pathElement.FieldPlace,
                    pathElement.Projections[0].Layout, function, program,
                    out var fieldArrayPrefix))
            {
                throw Invalid(pathElement.Span);
            }
            var arrayPrefix = GetNativeFixedArrayPathPrefix(
                fieldArrayPrefix, parentProjections);
            var leafProjection = pathElement.Projections[^1];
            var elementIndex
                = NativeFixedArrayContract.TryGetUnsignedIndex(
                    leafProjection.Index, out var selectedIndex)
                    ? selectedIndex : (ulong?)null;
            return initialized.RawUnionArrayElementBytes(
                pathElement.Variable, arrayPrefix, leafProjection.Layout,
                elementIndex);
        }
        if (expression is BoundFieldRead arrayElementField
            && arrayElementField.Type == type
            && TryGetNativeFixedArrayElementFieldPath(arrayElementField,
                function, program, out var arrayVariable,
                out var arrayFieldPlace, out var arrayProjections,
                out var arrayLeafLayout, out var elementFieldPath)
            && TryGetNativeFixedArrayArrayPrefix(arrayVariable,
                arrayFieldPlace, arrayProjections[0].Layout, function,
                program, out var arrayFieldPrefix)
            && TryGetFieldPathByteOffset(arrayLeafLayout.ElementType,
                elementFieldPath, program, out var fieldOffset))
        {
            var parentProjections = arrayProjections.Length == 1
                ? [] : arrayProjections.RemoveAt(
                    arrayProjections.Length - 1);
            var arrayPrefix = GetNativeFixedArrayPathPrefix(
                arrayFieldPrefix, parentProjections);
            var leafProjection = arrayProjections[^1];
            var elementIndex
                = NativeFixedArrayContract.TryGetUnsignedIndex(
                    leafProjection.Index, out var selectedIndex)
                    ? selectedIndex : (ulong?)null;
            var initializedBytes = initialized.RawUnionArrayElementBytes(
                arrayVariable, arrayPrefix, arrayLeafLayout, elementIndex);
            var size = MaterializedPayloadSize(type, program);
            if (fieldOffset <= initializedBytes.Length
                && size <= initializedBytes.Length - fieldOffset)
            {
                return initializedBytes.AsSpan(fieldOffset, size).ToArray();
            }
        }
        if (expression is BoundFieldRead fieldRead
            && fieldRead.Type == type
            && TryGetFieldProjectionPath(fieldRead, function, program,
                out var rootVariable, out var fieldPath)
            && TryGetFieldPathByteOffset(
                function.Variables[rootVariable].Type, fieldPath, program,
                out var offset)
            && initialized.TryGetRawUnionInitializedBytes(rootVariable,
                out var rootBytes))
        {
            var size = MaterializedPayloadSize(type, program);
            if (offset <= rootBytes.Length
                && size <= rootBytes.Length - offset)
            {
                return rootBytes.AsSpan(offset, size).ToArray();
            }
        }

        return GuaranteedInitializedPayloadBytes(type, program);
    }

    private static byte[] InitializedRawUnionPayloadBytesOfReferenceRead(
        BoundReferenceRead referenceRead, PrimitiveType type,
        InitializationState initialized, BoundFunction function,
        BoundProgram program)
    {
        var fallback = GuaranteedInitializedPayloadBytes(type, program);
        if (referenceRead.Descriptor is not BoundRead descriptor
            || (uint)descriptor.Variable >= (uint)function.Variables.Length
            || function.Variables[descriptor.Variable].Type
                != descriptor.Type
            || initialized.Dependencies is not BackingDependencies facts
            || (uint)descriptor.Variable >= (uint)facts.ReferenceTargets.Length
            || facts.ReferenceTargets[descriptor.Variable]
                is not { Count: > 0 } targets)
        {
            return fallback;
        }

        byte[]? common = null;
        foreach (var target in targets)
        {
            var targetBytes = fallback;
            if ((target.Kind is ProvenanceAnchorKind.NativeStorage
                    or ProvenanceAnchorKind.ReferencePlace)
                && target.Storage is StorageKey storage
                && storage.IsKnown
                && target.Identity == storage.Site
                && (uint)target.Identity < (uint)function.Variables.Length)
            {
                targetBytes = InitializedRawUnionPayloadBytesOfStoragePlace(
                    target.Identity, storage.Projection, type, initialized,
                    function, program, fallback);
            }

            if (common is null)
            {
                common = [.. targetBytes];
                continue;
            }
            for (var index = 0; index < common.Length; index++)
            {
                common[index] &= targetBytes[index];
            }
        }

        return common ?? fallback;
    }

    private static byte[] InitializedRawUnionPayloadBytesOfStoragePlace(
        int variable, StorageProjection projection, PrimitiveType type,
        InitializationState initialized, BoundFunction function,
        BoundProgram program, byte[] fallback)
    {
        var rootType = function.Variables[variable].Type;
        var segments = projection.ProjectionPath.Segments;
        if (segments.IsEmpty)
        {
            return projection.Kind == StorageProjectionKind.WholeObject
                && rootType == type
                && initialized.TryGetRawUnionInitializedBytes(variable,
                    out var storedBytes)
                && storedBytes.Length == fallback.Length
                    ? storedBytes : fallback;
        }

        var currentType = rootType;
        var currentProjection = StorageProjection.WholeObject;
        var rootFieldPath = default(FieldProjectionPath);
        var crossedArray = false;
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment.Kind == StorageProjectionSegmentKind.Field)
            {
                if (segment.OwnerType != currentType
                    || !HasFields(program, currentType)
                    || (uint)segment.Field
                        >= (uint)Fields(program, currentType).Length)
                {
                    return fallback;
                }

                currentProjection = currentProjection.AppendField(
                    currentType, segment.Field);
                rootFieldPath = rootFieldPath.Append(currentType,
                    segment.Field);
                currentType = Fields(program, currentType)[
                    segment.Field].Type;
                continue;
            }

            if (segment.Kind is not (
                    StorageProjectionSegmentKind.ArrayIndex
                    or StorageProjectionSegmentKind.ArrayRange)
                || !NativeFixedArrayContract.TryGetLayout(currentType,
                    program.Structures, program.NativeFixedArrayTypes,
                    out var layout)
                || !NativeFixedArrayContract.MatchesLayout(layout, program)
                || (segment.Kind
                        == StorageProjectionSegmentKind.ArrayIndex
                    && segment.Start >= layout.ElementCount)
                || (segment.Kind
                        == StorageProjectionSegmentKind.ArrayRange
                    && (segment.Length == 0
                        || segment.Start >= layout.ElementCount
                        || segment.Length
                            > layout.ElementCount - segment.Start)))
            {
                return fallback;
            }

            crossedArray = true;
            var elementIndex = segment.Kind
                    == StorageProjectionSegmentKind.ArrayIndex
                ? segment.Start : (ulong?)null;
            if (index == segments.Length - 1
                && layout.ElementType == type)
            {
                var elementBytes = initialized.RawUnionArrayElementBytes(
                    variable, currentProjection, layout, elementIndex);
                return elementBytes.Length == fallback.Length
                    ? elementBytes : fallback;
            }

            if (CarriesRawUnionPayloadFacts(program, layout.ElementType))
            {
                var suffixPath = default(FieldProjectionPath);
                var suffixType = layout.ElementType;
                var suffixIsFields = true;
                for (var suffixIndex = index + 1;
                    suffixIndex < segments.Length; suffixIndex++)
                {
                    var suffix = segments[suffixIndex];
                    if (suffix.Kind
                            != StorageProjectionSegmentKind.Field
                        || suffix.OwnerType != suffixType
                        || !HasFields(program, suffixType)
                        || (uint)suffix.Field
                            >= (uint)Fields(program, suffixType).Length)
                    {
                        suffixIsFields = false;
                        break;
                    }

                    suffixPath = suffixPath.Append(suffixType,
                        suffix.Field);
                    suffixType = Fields(program, suffixType)[
                        suffix.Field].Type;
                }

                if (suffixIsFields && suffixType == type
                    && TryGetFieldPathByteOffset(layout.ElementType,
                        suffixPath, program, out var fieldOffset))
                {
                    var elementBytes
                        = initialized.RawUnionArrayElementBytes(variable,
                            currentProjection, layout, elementIndex);
                    var size = fallback.Length;
                    if (fieldOffset <= elementBytes.Length
                        && size <= elementBytes.Length - fieldOffset)
                    {
                        return elementBytes.AsSpan(fieldOffset, size)
                            .ToArray();
                    }
                    return fallback;
                }
            }

            currentProjection = StorageProjection.Combine(
                currentProjection,
                elementIndex is ulong selectedIndex
                    ? StorageProjection.ForArrayIndex(selectedIndex)
                    : StorageProjection.ForArrayRange(
                        segment.Start, segment.Length));
            currentType = layout.ElementType;
            rootFieldPath = default;
        }

        if (!crossedArray && currentType == type
            && projection.Kind == StorageProjectionKind.InlineFieldPath
            && TryGetFieldPathByteOffset(rootType, rootFieldPath, program,
                out var offset)
            && initialized.TryGetRawUnionInitializedBytes(variable,
                out var rootBytes)
            && offset <= rootBytes.Length
            && fallback.Length <= rootBytes.Length - offset)
        {
            return rootBytes.AsSpan(offset, fallback.Length).ToArray();
        }

        return fallback;
    }

    private static void ValidateRawUnionReferenceFieldFormation(
        BoundReferenceFormation formation, ProvenanceAnchor target,
        InitializationState initialized, BoundFunction function,
        BoundProgram program, ref int remaining, int depth)
    {
        if (target.Kind is not (ProvenanceAnchorKind.NativeStorage
                or ProvenanceAnchorKind.ReferencePlace)
            || target.Storage is not StorageKey storage
            || !storage.IsKnown || target.Identity != storage.Site
            || (uint)target.Identity >= (uint)function.Variables.Length)
        {
            return;
        }

        var segments = storage.Projection.ProjectionPath.Segments;
        if (segments.IsEmpty)
        {
            return;
        }

        var currentType = function.Variables[target.Identity].Type;
        var currentProjection = StorageProjection.WholeObject;
        foreach (var segment in segments)
        {
            Count(ref remaining, depth + 1);
            if (segment.Kind == StorageProjectionSegmentKind.Field)
            {
                if (segment.OwnerType != currentType
                    || !HasFields(program, currentType)
                    || (uint)segment.Field
                        >= (uint)Fields(program, currentType).Length)
                {
                    throw InvalidEffect(formation.Span, "CheckedReference");
                }

                var field = Fields(program, currentType)[segment.Field];
                if (IsRawUnionType(program, currentType))
                {
                    var unionBytes
                        = InitializedRawUnionPayloadBytesOfStoragePlace(
                            target.Identity, currentProjection, currentType,
                            initialized, function, program,
                            GuaranteedInitializedPayloadBytes(currentType,
                                program));
                    var fieldOffset
                        = MaterializedPayloadOffset(field.Offset);
                    var fieldSize = MaterializedPayloadSize(field.Size);
                    if (fieldOffset > unionBytes.Length
                        || fieldSize > unionBytes.Length - fieldOffset
                        || unionBytes.AsSpan(fieldOffset, fieldSize)
                            .Contains((byte)0))
                    {
                        throw Invalid(formation.Span);
                    }
                }

                currentProjection = currentProjection.AppendField(
                    currentType, segment.Field);
                currentType = field.Type;
                continue;
            }

            if (segment.Kind is not (
                    StorageProjectionSegmentKind.ArrayIndex
                    or StorageProjectionSegmentKind.ArrayRange)
                || !NativeFixedArrayContract.TryGetLayout(currentType,
                    program.Structures, program.NativeFixedArrayTypes,
                    out var layout)
                || !NativeFixedArrayContract.MatchesLayout(layout, program)
                || (segment.Kind
                        == StorageProjectionSegmentKind.ArrayIndex
                    && segment.Start >= layout.ElementCount)
                || (segment.Kind
                        == StorageProjectionSegmentKind.ArrayRange
                    && (segment.Length == 0
                        || segment.Start >= layout.ElementCount
                        || segment.Length
                            > layout.ElementCount - segment.Start)))
            {
                throw InvalidEffect(formation.Span, "CheckedReference");
            }

            currentProjection = StorageProjection.Combine(
                currentProjection,
                segment.Kind == StorageProjectionSegmentKind.ArrayIndex
                    ? StorageProjection.ForArrayIndex(segment.Start)
                    : StorageProjection.ForArrayRange(segment.Start,
                        segment.Length));
            currentType = layout.ElementType;
        }
    }

    private static bool TryGetNativeFixedArrayElementFieldPath(
        BoundFieldRead fieldRead, BoundFunction function, BoundProgram program,
        out int variable, out BoundExpression? fieldPlace,
        out ImmutableArray<BoundNativeFixedArrayProjection> projections,
        out BoundNativeFixedArrayLayout leafLayout,
        out FieldProjectionPath fieldPath)
    {
        variable = -1;
        fieldPlace = null;
        projections = [];
        leafLayout = default;
        fieldPath = default;
        var fields = new Stack<BoundFieldRead>();
        BoundExpression root = fieldRead;
        while (true)
        {
            while (root is BoundUnsafeExpression unsafeExpression)
            {
                root = unsafeExpression.Expression;
            }
            if (root is not BoundFieldRead current)
            {
                break;
            }
            fields.Push(current);
            root = current.Receiver;
        }

        if (fields.Count == 0)
        {
            return false;
        }
        while (root is BoundUnsafeExpression unsafeExpression)
        {
            root = unsafeExpression.Expression;
        }

        switch (root)
        {
            case BoundNativeFixedArrayElement element:
                variable = element.Variable;
                fieldPlace = element.FieldPlace;
                projections = [new BoundNativeFixedArrayProjection(
                    element.Layout, element.Index)];
                leafLayout = element.Layout;
                break;
            case BoundNativeFixedArrayPathElement pathElement
                when !pathElement.Projections.IsDefaultOrEmpty:
                variable = pathElement.Variable;
                fieldPlace = pathElement.FieldPlace;
                projections = pathElement.Projections;
                leafLayout = pathElement.Projections[^1].Layout;
                break;
            default:
                return false;
        }

        if (projections.Length + fields.Count > MaximumDepth)
        {
            return false;
        }
        var ownerType = leafLayout.ElementType;
        while (fields.TryPop(out var field))
        {
            if (field.Receiver.Type != ownerType
                || !HasFields(program, ownerType)
                || (uint)field.Field
                    >= (uint)Fields(program, ownerType).Length
                || field.Type
                    != Fields(program, ownerType)[field.Field].Type)
            {
                return false;
            }
            fieldPath = fieldPath.Append(ownerType, field.Field);
            ownerType = field.Type;
        }
        return ownerType == fieldRead.Type;
    }

    private static byte[] GuaranteedInitializedPayloadBytes(
        PrimitiveType type, BoundProgram program,
        HashSet<PrimitiveType> visiting, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        var size = MaterializedPayloadSize(type, program);
        var result = new byte[size];
        if (size == 0)
        {
            return result;
        }

        if (NativeFixedArrayTypes.IsType(type))
        {
            var array = program.NativeFixedArrayTypes[
                NativeFixedArrayTypes.Index(type)];
            var elementBytes = GuaranteedInitializedPayloadBytes(
                array.ElementType, program, visiting, ref remaining,
                depth + 1);
            if (elementBytes.Length == 0)
            {
                return result;
            }
            if (array.ElementStride < (ulong)elementBytes.Length
                || array.ByteExtent != (ulong)size
                || (array.ElementCount != 0
                    && array.ElementStride == 0)
                || array.ElementCount > (ulong)size + 1UL)
            {
                throw Invalid();
            }

            for (ulong element = 0; element < array.ElementCount; element++)
            {
                Count(ref remaining, depth + 1);
                var offset = checked((int)(element * array.ElementStride));
                elementBytes.CopyTo(result, offset);
            }

            return result;
        }

        if (HasFields(program, type)
            && !ManagedClassTypes.IsClass(type))
        {
            if (!visiting.Add(type))
            {
                throw Invalid();
            }

            try
            {
                var structure = program.Structures[(int)type - 100];
                if (structure.Fields.IsDefaultOrEmpty)
                {
                    return result;
                }

                if (structure.IsUnion)
                {
                    Array.Fill(result, (byte)1);
                    foreach (var field in structure.Fields)
                    {
                        Count(ref remaining, depth + 1);
                        var fieldBytes = GuaranteedInitializedPayloadBytes(
                            field.Type, program, visiting, ref remaining,
                            depth + 1);
                        if (field.Offset > (ulong)result.Length
                            || (ulong)fieldBytes.Length
                                > (ulong)result.Length - field.Offset
                            || field.Size != (ulong)fieldBytes.Length)
                        {
                            throw Invalid();
                        }

                        var fieldOffset = checked((int)field.Offset);
                        for (var index = 0; index < result.Length; index++)
                        {
                            var alternativeByte
                                = index >= fieldOffset
                                    && index - fieldOffset < fieldBytes.Length
                                    ? fieldBytes[index - fieldOffset]
                                    : (byte)0;
                            result[index] &= alternativeByte;
                        }
                    }

                    return result;
                }

                foreach (var field in structure.Fields)
                {
                    Count(ref remaining, depth + 1);
                    var fieldBytes = GuaranteedInitializedPayloadBytes(
                        field.Type, program, visiting, ref remaining,
                        depth + 1);
                    if (field.Offset > (ulong)result.Length
                        || (ulong)fieldBytes.Length
                            > (ulong)result.Length - field.Offset
                        || field.Size != (ulong)fieldBytes.Length)
                    {
                        throw Invalid();
                    }

                    fieldBytes.CopyTo(result, checked((int)field.Offset));
                }
            }
            finally
            {
                _ = visiting.Remove(type);
            }

            return result;
        }

        Array.Fill(result, (byte)1);
        return result;
    }

    private static int MaterializedPayloadSize(
        PrimitiveType type, BoundProgram program)
    {
        var size = NativeFixedArrayTypes.IsType(type)
            ? program.NativeFixedArrayTypes[
                NativeFixedArrayTypes.Index(type)].ByteExtent
            : ValueLayout.Size(type, program.Structures,
                program.CheckedReferenceTypes,
                program.NativeLoweringAgreement);
        return MaterializedPayloadSize(size);
    }

    private static int MaterializedPayloadSize(ulong size)
    {
        if (size > (ulong)ValueLayout.MaximumStructureSize)
        {
            throw Limit();
        }

        return checked((int)size);
    }

    private static int MaterializedPayloadOffset(ulong offset)
    {
        if (offset > int.MaxValue)
        {
            throw Limit();
        }

        return checked((int)offset);
    }

    private static bool HasFields(BoundProgram program, PrimitiveType type) =>
        ManagedClassTypes.IsClass(type)
            ? ManagedClassTypes.ClassIndex(type) < program.Classes.Length
            : (int)type >= 100 && (int)type < ManagedClassTypes.First
                && (int)type - 100 < program.Structures.Length;

    private static ImmutableArray<BoundStructField> Fields(BoundProgram program, PrimitiveType type) =>
        ManagedClassTypes.IsClass(type) ? program.Classes[ManagedClassTypes.ClassIndex(type)].Fields
            : program.Structures[(int)type - 100].Fields;

    private static bool CanAccessField(BoundFunction function, BoundProgram program, PrimitiveType type,
        int field) =>
        ManagedClassTypes.IsClass(type)
            ? !program.Classes[ManagedClassTypes.ClassIndex(type)].Fields[field].IsPrivate
                || function.DeclaringStruct == program.Classes[ManagedClassTypes.ClassIndex(type)].Name
            : CanAccessField(function, program.Structures[(int)type - 100], field);

    private static bool CanAccessField(BoundFunction function, BoundStruct structure, int field) =>
        !structure.Fields[field].IsPrivate || function.DeclaringStruct == structure.Name;

    private static void MarkOutMasks(InitializationState initialized, uint[] masks, BoundFunction function, BoundProgram program)
    {
        for (var i = 0; i < initialized.Length; i++)
        {
            var type = function.Variables[i].Type;
            if (initialized[i] && HasFields(program, type))
            {
                masks[i] = FullMask(program, type);
                initialized.SetPossibleFieldMask(i, masks[i]);
            }
        }
    }

    private static void SetNormalBackingFactoryObjectIdentity(
        BoundCall call, int variable, PrimitiveType type,
        InitializationState state, bool succeeds)
    {
        var program = state.Program;
        if (program.NativeLoweringAgreement
                != CompilationOptions.WindowsX64NormalBackingAgreement
            || (uint)call.Function >= (uint)program.Functions.Length
            || program.Functions[call.Function].Intrinsic
                != BoundIntrinsic.ClassTryCreate
            || !IsNormalBackingObjectType(type)
            || state.Dependencies is not BackingDependencies dependencies)
        {
            return;
        }
        if (!succeeds)
        {
            dependencies.SetNormalBackingObjects(variable,
                new HashSet<ProvenanceAnchor>(), mayBeUnknown: false);
            return;
        }
        var allocationSite = checked(call.Span.Start + 1);
        var objectIdentity = ProvenanceAnchor.ForNormalBackingObject(
            dependencies.FunctionActivation, allocationSite,
            new StorageInstance(state.ScopeActivation, allocationSite), type);
        dependencies.SetNormalBackingObjects(variable,
            new HashSet<ProvenanceAnchor> { objectIdentity },
            mayBeUnknown: false);
        dependencies.ApplyNormalBackingClassConstructorSummary(
            program.Functions[call.Function].ClassConstructor,
            objectIdentity, program);
    }

    private static InitializationState CloneOutcomeState(
        InitializationState state,
        RepeatSnapshotReservationScope? snapshotScope) =>
        snapshotScope is null ? state.Clone() : state.Clone(snapshotScope);

    private static RepeatSnapshotReservationScope?
        CreateRepeatSnapshotScopeIfNeeded(InitializationState state)
    {
        if (state.RepeatSnapshotScope is not null)
        {
            return null;
        }

        var stateBytes = state.GetRawUnionArrayStateBytes();
        if (stateBytes.IsZero)
        {
            return null;
        }

        var snapshotScope = new RepeatSnapshotReservationScope(
            state.RepeatResources);
        snapshotScope.TrackExistingState(state, stateBytes);
        return snapshotScope;
    }

    private static InitializationState Outcome(BoundExpression expression,
        InitializationState initialized, bool result, ref int remaining,
        RepeatSnapshotReservationScope? snapshotScope = null)
    {
        snapshotScope ??= initialized.RepeatSnapshotScope;
        var hasOutcomeEffects = HasOutcomeEffects(expression, initialized.FlowFacts, ref remaining);
        var hasNativeOutputEffect = initialized.HasNativeOutputContracts
            && HasNativeOutputFlowEffect(expression, initialized, ref remaining);
        var hasNativePointerExtentEffect =
            HasNativePointerExtentFlowEffect(expression, initialized, ref remaining);
        if (!hasOutcomeEffects && !hasNativeOutputEffect && !hasNativePointerExtentEffect)
        {
            return CloneOutcomeState(initialized, snapshotScope);
        }
        Count(ref remaining, 0);
        if (expression is BoundBinary logical
            && TryGetLongLeftLogicalChain(logical, out var chain))
        {
            for (var index = 1; index < chain.Length; index++)
            {
                Count(ref remaining, 0);
            }

            var continueResult = logical.Operator == TokenKind.AmpersandAmpersand;
            var stopResult = !continueResult;
            var logicalState = initialized;
            InitializationState? stopState = null;
            for (var index = chain.Length; index >= 0; index--)
            {
                var operand = index == chain.Length
                    ? chain[^1].Left
                    : chain[index].Right;
                if (result == stopResult
                    && CanBe(operand, stopResult, ref remaining, logicalState))
                {
                    var operandStopState = Outcome(operand, logicalState,
                        stopResult, ref remaining, snapshotScope);
                    stopState = stopState is null
                        ? operandStopState
                        : Combine(stopState, true, operandStopState, true);
                }
                if (!CanBe(operand, continueResult, ref remaining, logicalState))
                {
                    return result == stopResult
                        ? stopState ?? CloneOutcomeState(initialized,
                            snapshotScope)
                        : CloneOutcomeState(initialized, snapshotScope);
                }
                logicalState = Outcome(operand, logicalState, continueResult,
                    ref remaining, snapshotScope);
            }
            return result == continueResult ? logicalState
                : stopState ?? CloneOutcomeState(initialized, snapshotScope);
        }
        if (expression is BoundUnary { Operator: TokenKind.Bang } unary)
        {
            return Outcome(unary.Operand, initialized, !result, ref remaining,
                snapshotScope);
        }
        if (expression is BoundUnsafeExpression unsafeExpression)
        {
            return Outcome(unsafeExpression.Expression, initialized, result,
                ref remaining, snapshotScope);
        }
        if (expression is BoundBinary { Operator: TokenKind.AmpersandAmpersand } and)
        {
            if (result)
            {
                var leftTrue = Outcome(and.Left, initialized, true,
                    ref remaining, snapshotScope);
                return Outcome(and.Right, leftTrue, true, ref remaining,
                    snapshotScope);
            }
            var firstFalsePossible = CanBe(and.Left, false, ref remaining, initialized);
            var rightFalsePossible = CanBe(and.Left, true, ref remaining, initialized)
                && CanBe(and.Right, false, ref remaining, initialized);
            if (firstFalsePossible && !rightFalsePossible)
            {
                return Outcome(and.Left, initialized, false, ref remaining,
                    snapshotScope);
            }
            if (!firstFalsePossible && rightFalsePossible)
            {
                var leftTrue = Outcome(and.Left, initialized, true,
                    ref remaining, snapshotScope);
                return Outcome(and.Right, leftTrue, false, ref remaining,
                    snapshotScope);
            }
            var leftTrueState = Outcome(and.Left, initialized, true,
                ref remaining, snapshotScope);
            var firstFalse = Outcome(and.Left, initialized, false,
                ref remaining, snapshotScope);
            var rightFalse = Outcome(and.Right, leftTrueState, false,
                ref remaining, snapshotScope);
            return Combine(firstFalse, firstFalsePossible, rightFalse, rightFalsePossible);
        }
        if (expression is BoundBinary { Operator: TokenKind.PipePipe } or)
        {
            if (!result)
            {
                var leftFalse = Outcome(or.Left, initialized, false,
                    ref remaining, snapshotScope);
                return Outcome(or.Right, leftFalse, false, ref remaining,
                    snapshotScope);
            }
            var firstTruePossible = CanBe(or.Left, true, ref remaining, initialized);
            var rightTruePossible = CanBe(or.Left, false, ref remaining, initialized)
                && CanBe(or.Right, true, ref remaining, initialized);
            if (firstTruePossible && !rightTruePossible)
            {
                return Outcome(or.Left, initialized, true, ref remaining,
                    snapshotScope);
            }
            if (!firstTruePossible && rightTruePossible)
            {
                var leftFalse = Outcome(or.Left, initialized, false,
                    ref remaining, snapshotScope);
                return Outcome(or.Right, leftFalse, true, ref remaining,
                    snapshotScope);
            }
            var leftFalseState = Outcome(or.Left, initialized, false,
                ref remaining, snapshotScope);
            var firstTrue = Outcome(or.Left, initialized, true,
                ref remaining, snapshotScope);
            var rightTrue = Outcome(or.Right, leftFalseState, true,
                ref remaining, snapshotScope);
            return Combine(firstTrue, firstTruePossible, rightTrue, rightTruePossible);
        }
        if (expression is BoundConditional conditional)
        {
            var whenTruePossible = CanBe(conditional.Condition, true, ref remaining, initialized)
                && CanBe(conditional.WhenTrue, result, ref remaining, initialized);
            var whenFalsePossible = CanBe(conditional.Condition, false, ref remaining, initialized)
                && CanBe(conditional.WhenFalse, result, ref remaining, initialized);
            if (whenTruePossible && !whenFalsePossible)
            {
                var trueConditionState = Outcome(conditional.Condition,
                    initialized, true, ref remaining, snapshotScope);
                return Outcome(conditional.WhenTrue, trueConditionState,
                    result, ref remaining, snapshotScope);
            }
            if (!whenTruePossible && whenFalsePossible)
            {
                var falseConditionState = Outcome(conditional.Condition,
                    initialized, false, ref remaining, snapshotScope);
                return Outcome(conditional.WhenFalse, falseConditionState,
                    result, ref remaining, snapshotScope);
            }
            var whenTrue = Outcome(conditional.Condition, initialized, true,
                ref remaining, snapshotScope);
            var whenFalse = Outcome(conditional.Condition, initialized,
                false, ref remaining, snapshotScope);
            return Combine(Outcome(conditional.WhenTrue, whenTrue, result,
                    ref remaining, snapshotScope),
                whenTruePossible,
                Outcome(conditional.WhenFalse, whenFalse, result,
                    ref remaining, snapshotScope),
                whenFalsePossible);
        }
        var state = CloneOutcomeState(initialized, snapshotScope);
        var nativeOutputCalls = new Dictionary<BoundExpression, NativeOutputCallFact>(
            ReferenceEqualityComparer.Instance);
        if (hasNativeOutputEffect || hasNativePointerExtentEffect)
        {
            ApplyNativeOutputCalls(expression, state, nativeOutputCalls, ref remaining, 1);
        }
        if (expression is BoundIsInitialized initializationTest)
        {
            if (state.IsNativeOutputReserved(initializationTest.Variable))
            {
                throw InvalidEffect(initializationTest.Span, "NativeImport");
            }
            var possibilities = state.Possibilities(initializationTest.Variable);
            state.SetPossibilities(initializationTest.Variable,
                result ? (byte)(possibilities & 2) : (byte)(possibilities & 5));
            return state;
        }
        if (expression is BoundIsVariablePattern variablePattern)
        {
            if (variablePattern.Value is BoundRead sourceRead)
            {
                if (result)
                {
                    state[sourceRead.Variable] = true;
                }
                else if (variablePattern.IncludeNull)
                {
                    state.SetPossibilities(sourceRead.Variable,
                        (byte)(state.Possibilities(sourceRead.Variable) & 5));
                }
            }
            else
            {
                VacateNestedOutputs(variablePattern.Value, state, ref remaining);
            }
            state.InvalidateNativeOutputVariable(variablePattern.Variable);
            state[variablePattern.Variable] = result;
            return state;
        }
        if (expression is BoundIsSome pattern)
        {
            VacateNestedOutputs(pattern.Value, state, ref remaining);
            state.InvalidateNativeOutputVariable(pattern.Variable);
            state[pattern.Variable] = result;
            return state;
        }
        if (expression is BoundCall call)
        {
            foreach (var argument in call.Arguments)
            {
                if (argument is BoundOutArgument output)
                {
                    state.InvalidateNativeOutputVariable(output.Variable);
                    state[output.Variable] = call.Type != PrimitiveType.Bool || result;
                    SetNormalBackingFactoryObjectIdentity(call, output.Variable,
                        output.Type, state, succeeds: call.Type != PrimitiveType.Bool || result);
                }
                else
                {
                    VacateNestedOutputs(argument, state, ref remaining);
                }
            }
        }
        else
        {
            VacateNestedOutputs(expression, state, ref remaining);
        }
        ApplyNativeOutputCondition(expression, state, nativeOutputCalls, result, ref remaining);
        return state;
    }

    private static void VacateNestedOutputs(BoundExpression expression, InitializationState state, ref int remaining)
    {
        if (expression is BoundIsInitialized
            || !HasOutcomeEffects(expression, state.FlowFacts, ref remaining))
        {
            return;
        }
        Count(ref remaining, 0);
        using var repeatSnapshotState
            = CreateRepeatSnapshotScopeIfNeeded(state);
        switch (expression)
        {
            case BoundUnsafeExpression unsafeExpression:
                VacateNestedOutputs(unsafeExpression.Expression, state, ref remaining);
                break;
            case BoundCall call:
                foreach (var argument in call.Arguments)
                {
                    if (argument is BoundOutArgument output)
                    {
                        if (call.Type == PrimitiveType.Bool)
                        {
                            state.MarkConditional(output.Variable);
                        }
                        else
                        {
                            state.MarkAssigned(output.Variable);
                        }
                    }
                    else
                    {
                        VacateNestedOutputs(argument, state, ref remaining);
                    }
                }
                break;
            case BoundIndirectCall call:
                VacateNestedOutputs(call.Target, state, ref remaining);
                foreach (var argument in call.Arguments)
                {
                    VacateNestedOutputs(argument, state, ref remaining);
                }
                break;
            case BoundBinary { Operator: TokenKind.AmpersandAmpersand or TokenKind.PipePipe } logical:
                var evaluateWhenTrue = logical.Operator == TokenKind.AmpersandAmpersand;
                var evaluatedPossible = CanBe(logical.Left, evaluateWhenTrue, ref remaining, state);
                var skippedPossible = CanBe(logical.Left, !evaluateWhenTrue, ref remaining, state);
                if (evaluatedPossible != skippedPossible)
                {
                    var feasible = Outcome(logical.Left, state, evaluatedPossible
                        ? evaluateWhenTrue : !evaluateWhenTrue, ref remaining);
                    if (evaluatedPossible)
                    {
                        VacateNestedOutputs(logical.Right, feasible, ref remaining);
                    }
                    feasible.CopyTo(state);
                    break;
                }
                var evaluated = Outcome(logical.Left, state, evaluateWhenTrue, ref remaining);
                VacateNestedOutputs(logical.Right, evaluated, ref remaining);
                var skipped = Outcome(logical.Left, state, !evaluateWhenTrue, ref remaining);
                var combined = Combine(evaluated, evaluatedPossible, skipped, skippedPossible);
                combined.CopyTo(state);
                break;
            case BoundBinary binary:
                VacateNestedOutputs(binary.Left, state, ref remaining);
                VacateNestedOutputs(binary.Right, state, ref remaining);
                break;
            case BoundConditional conditional:
                var whenTruePossible = CanBe(conditional.Condition, true, ref remaining, state);
                var whenFalsePossible = CanBe(conditional.Condition, false, ref remaining, state);
                if (whenTruePossible != whenFalsePossible)
                {
                    var feasible = Outcome(conditional.Condition, state, whenTruePossible, ref remaining);
                    VacateNestedOutputs(whenTruePossible ? conditional.WhenTrue : conditional.WhenFalse,
                        feasible, ref remaining);
                    feasible.CopyTo(state);
                    break;
                }
                var trueState = Outcome(conditional.Condition, state, true, ref remaining);
                VacateNestedOutputs(conditional.WhenTrue, trueState, ref remaining);
                var falseState = Outcome(conditional.Condition, state, false, ref remaining);
                VacateNestedOutputs(conditional.WhenFalse, falseState, ref remaining);
                var joined = Combine(trueState, whenTruePossible, falseState, whenFalsePossible);
                joined.CopyTo(state);
                break;
            case BoundCoalesce coalesce:
                var optionalPresence = KnownOptionalPresence(coalesce.Optional, state, ref remaining);
                VacateNestedOutputs(coalesce.Optional, state, ref remaining);
                if (optionalPresence == false)
                {
                    VacateNestedOutputs(coalesce.Fallback, state, ref remaining);
                }
                else if (optionalPresence is null)
                {
                    var absentState = state.Clone();
                    VacateNestedOutputs(coalesce.Fallback, absentState, ref remaining);
                    for (var index = 0; index < state.Length; index++)
                    {
                        state.Merge(index, absentState);
                    }
                }
                break;
            case BoundUnary unary:
                VacateNestedOutputs(unary.Operand, state, ref remaining);
                break;
            case BoundUpdate update:
                VacateNestedOutputs(update.Place, state, ref remaining);
                break;
            case BoundCompoundAssignment compound:
                VacateNestedOutputs(compound.Place, state, ref remaining);
                VacateNestedOutputs(compound.Right, state, ref remaining);
                break;
            case BoundNominalUpdate update:
                VacateNestedOutputs(update.Place, state, ref remaining);
                break;
            case BoundCast cast:
                VacateNestedOutputs(cast.Operand, state, ref remaining);
                break;
            case BoundOptionalLift lift:
                VacateNestedOutputs(lift.Value, state, ref remaining);
                break;
            case BoundReadonlyProjection projection:
                VacateNestedOutputs(projection.Value, state, ref remaining);
                break;
            case BoundIsNull test:
                VacateNestedOutputs(test.Value, state, ref remaining);
                break;
            case BoundNativeFixedArrayInitialization fixedArray:
                foreach (var element in fixedArray.Elements)
                {
                    VacateNestedOutputs(element, state, ref remaining);
                }
                break;
            case BoundNativeFixedArrayRepeatInitialization repeat:
                VacateNestedOutputs(repeat.Seed, state, ref remaining);
                break;
            case BoundNativeFixedArrayElement fixedArrayElement:
                VacateNestedOutputs(fixedArrayElement.Index, state, ref remaining);
                break;
            case BoundNativeFixedArrayPathElement pathElement:
                foreach (var projection in pathElement.Projections)
                {
                    VacateNestedOutputs(projection.Index, state,
                        ref remaining);
                }
                break;
            case BoundNativeFixedArrayPointerAddress pointerAddress:
                if (pointerAddress.Projections.IsDefaultOrEmpty)
                {
                    VacateNestedOutputs(pointerAddress.Index, state,
                        ref remaining);
                }
                else
                {
                    foreach (var projection in pointerAddress.Projections)
                    {
                        VacateNestedOutputs(projection.Index, state,
                            ref remaining);
                    }
                }
                break;
            case BoundNativeStorageAddress address:
                foreach (var nested in address.NestedExpressions)
                {
                    VacateNestedOutputs(nested, state, ref remaining);
                }
                break;
            case BoundNativeOutputReservation:
                break;
            case BoundNativePointerRead pointerRead:
                VacateNestedOutputs(pointerRead.Address, state, ref remaining);
                break;
            case BoundReferenceFormation formation:
                VacateNestedOutputs(formation.Place, state, ref remaining);
                break;
            case BoundReferenceReborrow reborrow:
                VacateNestedOutputs(reborrow.Descriptor, state, ref remaining);
                break;
            case BoundReferenceRead referenceRead:
                VacateNestedOutputs(referenceRead.Descriptor, state, ref remaining);
                break;
            case BoundReferenceWrite referenceWrite:
                VacateNestedOutputs(referenceWrite.Descriptor, state, ref remaining);
                VacateNestedOutputs(referenceWrite.Value, state, ref remaining);
                break;
            case BoundNativePointerWrite pointerWrite:
                VacateNestedOutputs(pointerWrite.Address, state, ref remaining);
                VacateNestedOutputs(pointerWrite.Value, state, ref remaining);
                break;
            case BoundNativeFixedArrayWrite write:
                VacateNestedOutputs(write.Index, state, ref remaining);
                VacateNestedOutputs(write.Value, state, ref remaining);
                break;
            case BoundNativeFixedArrayPathWrite pathWrite:
                foreach (var projection in pathWrite.Projections)
                {
                    VacateNestedOutputs(projection.Index, state,
                        ref remaining);
                }
                VacateNestedOutputs(pathWrite.Value, state, ref remaining);
                break;
            case BoundIsInitialized:
                break;
            case BoundIsSome pattern:
                VacateNestedOutputs(pattern.Value, state, ref remaining);
                state[pattern.Variable] = false;
                break;
            case BoundIsVariablePattern pattern:
                if (pattern.Value is not BoundRead)
                {
                    VacateNestedOutputs(pattern.Value, state, ref remaining);
                }
                state[pattern.Variable] = false;
                break;
            case BoundFieldRead field:
                VacateNestedOutputs(field.Receiver, state, ref remaining);
                break;
            case BoundBufferRead buffer:
                VacateNestedOutputs(buffer.Index, state, ref remaining);
                break;
            case BoundViewRead view:
                VacateNestedOutputs(view.Index, state, ref remaining);
                break;
            case BoundSpanLength length:
                VacateNestedOutputs(length.View, state, ref remaining);
                break;
            case BoundSpanRead read:
                VacateNestedOutputs(read.View, state, ref remaining);
                VacateNestedOutputs(read.Index, state, ref remaining);
                break;
            case BoundSpanAdvance advance:
                VacateNestedOutputs(advance.View, state, ref remaining);
                break;
            case BoundSnapshotSpan:
                break;
            case BoundViewLength:
            case BoundBufferLength:
            case BoundConstant:
            case BoundNull:
            case BoundRead:
            case BoundReceiverLoan:
            case BoundMutableReceiverLoan:
            case BoundMutableFieldLoan:
            case BoundFieldLoan:
            case BoundNativePointerAddress:
                break;
            case BoundMove move:
                if (move.Variable < 0 || move.Variable >= state.Length)
                {
                    throw InvalidEffect(move.Span, "Ownership");
                }
                state.Retire(move.Variable, (long)move.Span.Start + 1);
                break;
            case BoundFieldMove:
                break;
            default:
                throw Invalid();
        }
    }

    private static bool HasOutcomeEffects(BoundExpression expression,
        Dictionary<BoundExpression, byte> flowFacts, ref int remaining) =>
        (FlowFacts(expression, flowFacts, ref remaining) & OutcomeEffectFact) != 0;

    private static bool HasInitializationTests(BoundExpression expression,
        Dictionary<BoundExpression, byte> flowFacts, ref int remaining) =>
        (FlowFacts(expression, flowFacts, ref remaining) & InitializationTestFact) != 0;

    // Structural facts are state-independent and shared across flow-state clones;
    // the work budget counts the first classification, not cache reads.
    // Unknown forms stay conservative for both properties.
    private static byte FlowFacts(BoundExpression expression,
        Dictionary<BoundExpression, byte> flowFacts, ref int remaining)
    {
        if (expression is null)
        {
            return AllFlowFacts;
        }
        if (flowFacts.TryGetValue(expression, out var facts))
        {
            return facts;
        }
        Count(ref remaining, 0);

        facts = expression switch {
            BoundUnsafeExpression unsafeExpression =>
                FlowFacts(unsafeExpression.Expression, flowFacts, ref remaining),
            BoundCall call => FlowFacts(call.Arguments, flowFacts, ref remaining),
            BoundIndirectCall call => FlowFacts([call.Target, .. call.Arguments], flowFacts, ref remaining),
            BoundBinary binary => AddFlowFact(AddFlowFact(0, binary.Left, flowFacts, ref remaining),
                binary.Right, flowFacts, ref remaining),
            BoundConditional conditional => AddFlowFact(
                AddFlowFact(AddFlowFact(0, conditional.Condition, flowFacts, ref remaining),
                    conditional.WhenTrue, flowFacts, ref remaining),
                conditional.WhenFalse, flowFacts, ref remaining),
            BoundCoalesce coalesce => AddFlowFact(AddFlowFact(0, coalesce.Optional, flowFacts, ref remaining),
                coalesce.Fallback, flowFacts, ref remaining),
            BoundUnary unary => FlowFacts(unary.Operand, flowFacts, ref remaining),
            BoundUpdate update => FlowFacts(update.Place, flowFacts, ref remaining),
            BoundCompoundAssignment compound => AddFlowFact(
                AddFlowFact(0, compound.Place, flowFacts, ref remaining), compound.Right, flowFacts, ref remaining),
            BoundNominalUpdate update => FlowFacts(update.Place, flowFacts, ref remaining),
            BoundCast cast => FlowFacts(cast.Operand, flowFacts, ref remaining),
            BoundOptionalLift lift => FlowFacts(lift.Value, flowFacts, ref remaining),
            BoundReadonlyProjection projection => FlowFacts(projection.Value, flowFacts, ref remaining),
            BoundIsNull test => FlowFacts(test.Value, flowFacts, ref remaining),
            BoundNativeFixedArrayInitialization fixedArray =>
                FlowFacts(fixedArray.Elements, flowFacts, ref remaining),
            BoundNativeFixedArrayRepeatInitialization repeat =>
                FlowFacts(repeat.Seed, flowFacts, ref remaining),
            BoundNativeFixedArrayElement fixedArrayElement =>
                FlowFacts(fixedArrayElement.Index, flowFacts, ref remaining),
            BoundNativeFixedArrayPathElement pathElement =>
                FlowFacts([.. pathElement.Projections.Select(
                    static projection => projection.Index)],
                    flowFacts, ref remaining),
            BoundNativeFixedArrayPointerAddress pointerAddress =>
                pointerAddress.Projections.IsDefaultOrEmpty
                    ? FlowFacts(pointerAddress.Index, flowFacts, ref remaining)
                    : FlowFacts([.. pointerAddress.Projections.Select(
                        static projection => projection.Index)],
                        flowFacts, ref remaining),
            BoundNativeFixedArrayWrite fixedArrayWrite => AddFlowFact(
                FlowFacts(fixedArrayWrite.Index, flowFacts, ref remaining),
                fixedArrayWrite.Value, flowFacts, ref remaining),
            BoundNativeFixedArrayPathWrite pathWrite =>
                FlowFacts([.. pathWrite.Projections.Select(
                    static projection => projection.Index), pathWrite.Value],
                    flowFacts, ref remaining),
            BoundNativeStorageAddress address =>
                FlowFacts(address.NestedExpressions, flowFacts,
                    ref remaining),
            BoundReferenceFormation formation => FlowFacts(formation.Place, flowFacts, ref remaining),
            BoundReferenceReborrow reborrow =>
                FlowFacts(reborrow.Descriptor, flowFacts, ref remaining),
            BoundReferenceRead referenceRead => FlowFacts(referenceRead.Descriptor, flowFacts, ref remaining),
            BoundReferenceWrite referenceWrite => AddFlowFact(
                FlowFacts(referenceWrite.Descriptor, flowFacts, ref remaining),
                referenceWrite.Value, flowFacts, ref remaining),
            BoundNativePointerAddress => 0,
            BoundNativePointerRead pointerRead => FlowFacts(pointerRead.Address, flowFacts, ref remaining),
            BoundNativePointerWrite pointerWrite => AddFlowFact(
                FlowFacts(pointerWrite.Address, flowFacts, ref remaining),
                pointerWrite.Value, flowFacts, ref remaining),
            BoundIsSome test => (byte)(OutcomeEffectFact | FlowFacts(test.Value, flowFacts, ref remaining)),
            BoundIsVariablePattern or BoundIsInitialized => AllFlowFacts,
            BoundOutArgument or BoundMove or BoundFieldMove => OutcomeEffectFact,
            BoundFieldRead field => FlowFacts(field.Receiver, flowFacts, ref remaining),
            BoundBufferRead buffer => FlowFacts(buffer.Index, flowFacts, ref remaining),
            BoundViewRead view => FlowFacts(view.Index, flowFacts, ref remaining),
            BoundSpanLength length => FlowFacts(length.View, flowFacts, ref remaining),
            BoundSpanRead read => AddFlowFact(AddFlowFact(0, read.View, flowFacts, ref remaining),
                read.Index, flowFacts, ref remaining),
            BoundSpanAdvance advance => FlowFacts(advance.View, flowFacts, ref remaining),
            BoundSnapshotSpan => 0,
            BoundViewLength or BoundBufferLength or BoundConstant or BoundNull
                or BoundRead or BoundReceiverLoan
                or BoundMutableReceiverLoan or BoundMutableFieldLoan or BoundFieldLoan => 0,
            _ => AllFlowFacts,
        };
        flowFacts.Add(expression, facts);
        return facts;
    }

    private static byte FlowFacts(ImmutableArray<BoundExpression> expressions,
        Dictionary<BoundExpression, byte> flowFacts, ref int remaining)
    {
        byte facts = 0;
        foreach (var expression in expressions)
        {
            if (expression is BoundOutArgument)
            {
                facts |= OutcomeEffectFact;
            }
            facts = AddFlowFact(facts, expression, flowFacts, ref remaining);
            if (facts == AllFlowFacts)
            {
                break;
            }
        }
        return facts;
    }

    private static byte AddFlowFact(byte facts, BoundExpression expression,
        Dictionary<BoundExpression, byte> flowFacts, ref int remaining) =>
        facts == AllFlowFacts ? facts : (byte)(facts | FlowFacts(expression, flowFacts, ref remaining));

    private const byte MayNotCompleteNormallyFact = 1;
    private const byte CompletionDependsOnFlowStateFact = 2;

    private static byte CompletionShape(BoundExpression expression,
        InitializationState initialized, BoundProgram program, ref int remaining, int depth)
    {
        if (expression is null)
        {
            return 0;
        }

        var facts = initialized.CompletionShapeFacts;
        if (facts.TryGetValue(expression, out var shape))
        {
            return shape;
        }

        Count(ref remaining, depth);
        shape = expression switch {
            BoundCall call when (uint)call.Function < (uint)program.Functions.Length
                && program.Functions[call.Function].DoesNotReturn
                => MayNotCompleteNormallyFact,
            BoundIndirectCall call
                when call.Target is not null
                    && NativeFunctionPointerTypes.IsType(call.Target.Type)
                    && (uint)NativeFunctionPointerTypes.Index(call.Target.Type)
                        < (uint)program.NativeFunctionPointerSignatures.Length
                    && (program.NativeFunctionPointerSignatures[
                        NativeFunctionPointerTypes.Index(call.Target.Type)].EffectsMask & 2) != 0
                => MayNotCompleteNormallyFact,
            BoundNominalUpdate update when (uint)update.Function < (uint)program.Functions.Length
                && program.Functions[update.Function].DoesNotReturn
                => MayNotCompleteNormallyFact,
            BoundBinary { Operator: TokenKind.AmpersandAmpersand or TokenKind.PipePipe }
                or BoundConditional or BoundCoalesce
                => CompletionDependsOnFlowStateFact,
            _ => 0,
        };
        if ((shape & MayNotCompleteNormallyFact) == 0)
        {
            foreach (var nested in expression.NestedExpressions)
            {
                shape |= CompletionShape(nested, initialized, program,
                    ref remaining, depth + 1);
            }
        }

        facts.Add(expression, shape);
        return shape;
    }

    // A false result means no evaluated path produces a normal value.
    private static bool CanCompleteNormally(BoundExpression expression,
        InitializationState initialized, BoundProgram program, ref int remaining, int depth)
    {
        if (expression is null || !initialized.HasDoesNotReturnFunctions)
        {
            return true;
        }
        var shape = CompletionShape(expression, initialized, program, ref remaining, depth + 1);
        if ((shape & MayNotCompleteNormallyFact) == 0)
        {
            return true;
        }

        var cacheable = (shape & CompletionDependsOnFlowStateFact) == 0;
        if (cacheable
            && initialized.CompletionFacts.TryGetValue(expression, out var completesNormally))
        {
            return completesNormally;
        }

        using var repeatSnapshotState
            = CreateRepeatSnapshotScopeIfNeeded(initialized);
        completesNormally = CanCompleteNormallyCore(expression, initialized,
            program, ref remaining, depth);
        if (cacheable)
        {
            initialized.CompletionFacts.Add(expression, completesNormally);
        }
        return completesNormally;
    }

    private static bool CanCompleteNormallyCore(BoundExpression expression,
        InitializationState initialized, BoundProgram program, ref int remaining, int depth)
    {
        Count(ref remaining, depth);
        switch (expression)
        {
            case BoundCall call:
                if ((uint)call.Function >= (uint)program.Functions.Length || call.Arguments.IsDefault)
                {
                    return true;
                }
                foreach (var argument in call.Arguments)
                {
                    if (!CanCompleteNormally(argument, initialized, program,
                        ref remaining, depth + 1))
                    {
                        return false;
                    }
                }
                return !program.Functions[call.Function].DoesNotReturn;
            case BoundIndirectCall call:
                if (call.Target is null || call.Arguments.IsDefault
                    || !CanCompleteNormally(call.Target, initialized, program,
                    ref remaining, depth + 1))
                {
                    return false;
                }
                foreach (var argument in call.Arguments)
                {
                    if (!CanCompleteNormally(argument, initialized, program,
                        ref remaining, depth + 1))
                    {
                        return false;
                    }
                }
                var callType = call.Target.Type;
                return !NativeFunctionPointerTypes.IsType(callType)
                    || (uint)NativeFunctionPointerTypes.Index(callType)
                        >= (uint)program.NativeFunctionPointerSignatures.Length
                    || (program.NativeFunctionPointerSignatures[
                        NativeFunctionPointerTypes.Index(callType)].EffectsMask & 2) == 0;
            case BoundBinary { Operator: TokenKind.AmpersandAmpersand or TokenKind.PipePipe } logical:
                return CanCompleteAs(logical, true, initialized, program, ref remaining, depth + 1)
                    || CanCompleteAs(logical, false, initialized, program, ref remaining, depth + 1);
            case BoundConditional conditional:
                if (conditional.Condition is null || conditional.WhenTrue is null
                    || conditional.WhenFalse is null)
                {
                    return true;
                }
                return (CanCompleteAs(conditional.Condition, true, initialized, program,
                            ref remaining, depth + 1)
                        && CanCompleteNormally(conditional.WhenTrue,
                            Outcome(conditional.Condition, initialized, true, ref remaining),
                            program, ref remaining, depth + 1))
                    || (CanCompleteAs(conditional.Condition, false, initialized, program,
                            ref remaining, depth + 1)
                        && CanCompleteNormally(conditional.WhenFalse,
                            Outcome(conditional.Condition, initialized, false, ref remaining),
                            program, ref remaining, depth + 1));
            case BoundCoalesce coalesce:
                if (coalesce.Optional is null || coalesce.Fallback is null)
                {
                    return true;
                }
                if (!CanCompleteNormally(coalesce.Optional, initialized, program,
                    ref remaining, depth + 1))
                {
                    return false;
                }
                return KnownOptionalPresence(coalesce.Optional, initialized, ref remaining) != false
                    || CanCompleteNormally(coalesce.Fallback, initialized, program,
                        ref remaining, depth + 1);
            case BoundNominalUpdate update:
                return CanCompleteNormally(update.Place, initialized, program,
                    ref remaining, depth + 1)
                    && ((uint)update.Function >= (uint)program.Functions.Length
                        || !program.Functions[update.Function].DoesNotReturn);
            default:
                foreach (var nested in expression.NestedExpressions)
                {
                    if (!CanCompleteNormally(nested, initialized, program,
                        ref remaining, depth + 1))
                    {
                        return false;
                    }
                }
                return true;
        }
    }

    private static bool CanCompleteAs(BoundExpression expression, bool result,
        InitializationState initialized, BoundProgram program, ref int remaining, int depth)
    {
        if (expression is null)
        {
            return true;
        }
        using var repeatSnapshotState
            = CreateRepeatSnapshotScopeIfNeeded(initialized);
        Count(ref remaining, depth);
        if (expression is BoundBinary logical
            && TryGetLongLeftLogicalChain(logical, out var chain))
        {
            for (var index = 1; index < chain.Length; index++)
            {
                Count(ref remaining, depth + index);
            }

            var continueWhenTrue = logical.Operator == TokenKind.AmpersandAmpersand;
            var continueResult = continueWhenTrue;
            var stopResult = !continueResult;
            var state = initialized;
            for (var index = chain.Length; index >= 0; index--)
            {
                var operand = index == chain.Length
                    ? chain[^1].Left
                    : chain[index].Right;
                var operandDepth = index == chain.Length
                    ? depth + chain.Length
                    : depth + index + 1;
                if (result == stopResult
                    && CanCompleteAs(operand, stopResult, state, program,
                        ref remaining, operandDepth))
                {
                    return true;
                }
                if (!CanCompleteAs(operand, continueResult, state, program,
                    ref remaining, operandDepth))
                {
                    return false;
                }
                state = Outcome(operand, state, continueResult, ref remaining);
            }
            return result == continueResult;
        }
        switch (expression)
        {
            case BoundUnsafeExpression unsafeExpression:
                return CanCompleteAs(unsafeExpression.Expression, result, initialized,
                    program, ref remaining, depth + 1);
            case BoundUnary { Operator: TokenKind.Bang } unary:
                return CanCompleteAs(unary.Operand, !result, initialized,
                    program, ref remaining, depth + 1);
            case BoundBinary { Operator: TokenKind.AmpersandAmpersand } and:
                return result
                    ? (CanCompleteAs(and.Left, true, initialized, program, ref remaining, depth + 1)
                        && CanCompleteAs(and.Right, true,
                            Outcome(and.Left, initialized, true, ref remaining),
                            program, ref remaining, depth + 1))
                    : (CanCompleteAs(and.Left, false, initialized, program, ref remaining, depth + 1)
                        || (CanCompleteAs(and.Left, true, initialized, program, ref remaining, depth + 1)
                            && CanCompleteAs(and.Right, false,
                                Outcome(and.Left, initialized, true, ref remaining),
                                program, ref remaining, depth + 1)));
            case BoundBinary { Operator: TokenKind.PipePipe } or:
                return result
                    ? (CanCompleteAs(or.Left, true, initialized, program, ref remaining, depth + 1)
                        || (CanCompleteAs(or.Left, false, initialized, program, ref remaining, depth + 1)
                            && CanCompleteAs(or.Right, true,
                                Outcome(or.Left, initialized, false, ref remaining),
                                program, ref remaining, depth + 1)))
                    : (CanCompleteAs(or.Left, false, initialized, program, ref remaining, depth + 1)
                        && CanCompleteAs(or.Right, false,
                            Outcome(or.Left, initialized, false, ref remaining),
                            program, ref remaining, depth + 1));
            case BoundConditional conditional when conditional.Type == PrimitiveType.Bool:
                if (conditional.Condition is null || conditional.WhenTrue is null
                    || conditional.WhenFalse is null)
                {
                    return true;
                }
                return (CanCompleteAs(conditional.Condition, true, initialized, program,
                            ref remaining, depth + 1)
                        && CanCompleteAs(conditional.WhenTrue, result,
                            Outcome(conditional.Condition, initialized, true, ref remaining),
                            program, ref remaining, depth + 1))
                    || (CanCompleteAs(conditional.Condition, false, initialized, program,
                            ref remaining, depth + 1)
                        && CanCompleteAs(conditional.WhenFalse, result,
                            Outcome(conditional.Condition, initialized, false, ref remaining),
                            program, ref remaining, depth + 1));
            default:
                return CanCompleteNormally(expression, initialized, program,
                    ref remaining, depth + 1)
                    && CanBe(expression, result, ref remaining, initialized);
        }
    }

    private static bool EvaluateExpression(BoundExpression expression,
        InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth,
        FailureEdges? failures, bool statementCompound = false,
        BoundBinary? cleanupCounterStep = null)
    {
        using var repeatSnapshotState
            = CreateRepeatSnapshotScopeIfNeeded(initialized);
        var completesNormally = CanCompleteNormally(expression, initialized, program,
            ref remaining, depth);
        if (completesNormally)
        {
            Expression(expression, initialized, fieldMasks, declarations, function, program,
                ref remaining, depth, failures, statementCompound, cleanupCounterStep);
            return true;
        }

        var beforeExpression = initialized.Clone();
        var beforeMasks = (uint[])fieldMasks.Clone();
        Expression(expression, initialized, fieldMasks, declarations, function, program,
            ref remaining, depth, failures, statementCompound, cleanupCounterStep);
        beforeExpression.CopyTo(initialized);
        beforeMasks.CopyTo(fieldMasks, 0);
        return false;
    }

    private static void ValidateUnreachableExpression(BoundExpression expression,
        InitializationState initialized, uint[] fieldMasks, HashSet<int> declarations,
        BoundFunction function, BoundProgram program, ref int remaining, int depth)
    {
        using var repeatSnapshotState
            = CreateRepeatSnapshotScopeIfNeeded(initialized);
        var unreachableState = initialized.Clone();
        var unreachableMasks = (uint[])fieldMasks.Clone();
        Expression(expression, unreachableState, unreachableMasks, declarations,
            function, program, ref remaining, depth, null);
    }

    private static bool CanBe(BoundExpression expression, bool result, ref int remaining,
        InitializationState initialized)
    {
        using var repeatSnapshotState
            = CreateRepeatSnapshotScopeIfNeeded(initialized);
        if (expression is BoundBinary logical
            && TryGetLongLeftLogicalChain(logical, out var chain))
        {
            for (var index = 0; index < chain.Length; index++)
            {
                Count(ref remaining, 0);
            }

            var continueResult = logical.Operator == TokenKind.AmpersandAmpersand;
            var stopResult = !continueResult;
            var state = initialized;
            for (var index = chain.Length; index >= 0; index--)
            {
                var operand = index == chain.Length
                    ? chain[^1].Left
                    : chain[index].Right;
                if (result == stopResult
                    && CanBe(operand, stopResult, ref remaining, state))
                {
                    return true;
                }
                if (!CanBe(operand, continueResult, ref remaining, state))
                {
                    return false;
                }
                state = Outcome(operand, state, continueResult, ref remaining);
            }
            return result == continueResult;
        }
        if (expression is BoundIsNull nullTest
            && KnownOptionalPresence(nullTest.Value, initialized, ref remaining) is bool nullPresence)
        {
            Count(ref remaining, 0);
            return result != nullPresence;
        }
        if (expression is BoundIsSome someTest
            && KnownOptionalPresence(someTest.Value, initialized, ref remaining) is bool somePresence)
        {
            Count(ref remaining, 0);
            return result == somePresence;
        }
        if (expression is BoundIsInitialized initializationTest)
        {
            Count(ref remaining, 0);
            if (initialized.IsNativeOutputReserved(initializationTest.Variable))
            {
                throw InvalidEffect(initializationTest.Span, "NativeImport");
            }
            var possibilities = initialized.Possibilities(initializationTest.Variable);
            return result ? (possibilities & 2) != 0 : (possibilities & 5) != 0;
        }
        if (expression is BoundUnary { Operator: TokenKind.Bang } unary)
        {
            Count(ref remaining, 0);
            return CanBe(unary.Operand, !result, ref remaining, initialized);
        }
        if (expression is BoundBinary { Operator: TokenKind.AmpersandAmpersand } and
            && HasInitializationTests(and, initialized.FlowFacts, ref remaining))
        {
            Count(ref remaining, 0);
            if (result)
            {
                if (!CanBe(and.Left, true, ref remaining, initialized))
                {
                    return false;
                }
                var leftTrue = Outcome(and.Left, initialized, true, ref remaining);
                return CanBe(and.Right, true, ref remaining, leftTrue);
            }
            if (CanBe(and.Left, false, ref remaining, initialized))
            {
                return true;
            }
            if (!CanBe(and.Left, true, ref remaining, initialized))
            {
                return false;
            }
            var leftTrueState = Outcome(and.Left, initialized, true, ref remaining);
            return CanBe(and.Right, false, ref remaining, leftTrueState);
        }
        if (expression is BoundBinary { Operator: TokenKind.PipePipe } or &&
            HasInitializationTests(or, initialized.FlowFacts, ref remaining))
        {
            Count(ref remaining, 0);
            if (result)
            {
                if (CanBe(or.Left, true, ref remaining, initialized))
                {
                    return true;
                }
                if (!CanBe(or.Left, false, ref remaining, initialized))
                {
                    return false;
                }
                var leftFalse = Outcome(or.Left, initialized, false, ref remaining);
                return CanBe(or.Right, true, ref remaining, leftFalse);
            }
            if (!CanBe(or.Left, false, ref remaining, initialized))
            {
                return false;
            }
            var rightFalse = Outcome(or.Left, initialized, false, ref remaining);
            return CanBe(or.Right, false, ref remaining, rightFalse);
        }
        if (expression is BoundConditional conditional &&
            HasInitializationTests(conditional, initialized.FlowFacts, ref remaining))
        {
            Count(ref remaining, 0);
            if (CanBe(conditional.Condition, true, ref remaining, initialized))
            {
                var conditionTrue = Outcome(conditional.Condition, initialized, true, ref remaining);
                if (CanBe(conditional.WhenTrue, result, ref remaining, conditionTrue))
                {
                    return true;
                }
            }
            if (!CanBe(conditional.Condition, false, ref remaining, initialized))
            {
                return false;
            }
            var conditionFalse = Outcome(conditional.Condition, initialized, false, ref remaining);
            return CanBe(conditional.WhenFalse, result, ref remaining, conditionFalse);
        }
        if (expression is BoundIsVariablePattern pattern)
        {
            Count(ref remaining, 0);
            if (!ManagedClassTypes.IsOptional(pattern.Value.Type))
            {
                return result;
            }
            if (pattern.Value is BoundRead sourceRead)
            {
                var possibilities = initialized.Possibilities(sourceRead.Variable);
                return result ? (possibilities & 2) != 0
                    : !pattern.IncludeNull || (possibilities & 5) != 0;
            }
            return result;
        }
        return CanBe(expression, result, ref remaining, initialized.CanBeFacts);
    }

    private static bool? KnownOptionalPresence(BoundExpression expression, InitializationState initialized,
        ref int remaining)
    {
        using var repeatSnapshotState
            = CreateRepeatSnapshotScopeIfNeeded(initialized);
        Count(ref remaining, 0);
        if (!ManagedClassTypes.IsOptional(expression.Type))
        {
            return null;
        }
        var structuralPresenceAnalyzed = TryGetOptionalPresence(expression,
            initialized.CanBeFacts, out var structuralPresence);
        if (structuralPresenceAnalyzed && structuralPresence is bool knownPresence)
        {
            return knownPresence;
        }
        if (expression is BoundCoalesce coalesce)
        {
            var optionalPresence = KnownOptionalPresence(coalesce.Optional, initialized, ref remaining);
            if (optionalPresence == true)
            {
                return true;
            }
            if (optionalPresence == false)
            {
                var optionalState = Outcome(coalesce.Optional, initialized, true, ref remaining);
                return KnownOptionalPresence(coalesce.Fallback, optionalState, ref remaining);
            }
            return null;
        }
        if (expression is not BoundConditional conditional)
        {
            if (expression is BoundUnsafeExpression unsafeExpression)
            {
                return KnownOptionalPresence(unsafeExpression.Expression, initialized, ref remaining);
            }
            return structuralPresenceAnalyzed
                ? null : KnownOptionalPresence(expression, initialized.CanBeFacts, ref remaining);
        }

        var whenTruePossible = CanBe(conditional.Condition, true, ref remaining, initialized);
        var whenFalsePossible = CanBe(conditional.Condition, false, ref remaining, initialized);
        bool? whenTrue = null;
        bool? whenFalse = null;
        if (whenTruePossible)
        {
            var trueState = Outcome(conditional.Condition, initialized, true, ref remaining);
            whenTrue = KnownOptionalPresence(conditional.WhenTrue, trueState, ref remaining);
        }
        if (whenFalsePossible)
        {
            var falseState = Outcome(conditional.Condition, initialized, false, ref remaining);
            whenFalse = KnownOptionalPresence(conditional.WhenFalse, falseState, ref remaining);
        }
        if (!whenTruePossible)
        {
            return whenFalse;
        }
        if (!whenFalsePossible)
        {
            return whenTrue;
        }
        return whenTrue is bool presence && whenFalse == presence ? presence : null;
    }

    private static bool CanBe(BoundExpression expression, bool result, ref int remaining,
        Dictionary<BoundExpression, byte> remainingCanBeFacts)
    {
        Count(ref remaining, 0);
        var known = result ? (byte)1 : (byte)4;
        var possible = result ? (byte)2 : (byte)8;
        if (remainingCanBeFacts.TryGetValue(expression, out var facts) && (facts & known) != 0)
        {
            return (facts & possible) != 0;
        }

        if (expression is BoundBinary logical
            && TryGetLongLeftLogicalChain(logical, out var chain))
        {
            for (var index = 0; index < chain.Length; index++)
            {
                Count(ref remaining, 0);
            }

            var continueResult = logical.Operator == TokenKind.AmpersandAmpersand;
            var stopResult = !continueResult;
            var logicalCanBe = true;
            for (var index = chain.Length; index >= 0; index--)
            {
                var operand = index == chain.Length
                    ? chain[^1].Left
                    : chain[index].Right;
                if (result == stopResult
                    && CanBe(operand, stopResult, ref remaining, remainingCanBeFacts))
                {
                    logicalCanBe = true;
                    break;
                }
                if (!CanBe(operand, continueResult, ref remaining, remainingCanBeFacts))
                {
                    logicalCanBe = false;
                    break;
                }
                if (index == 0)
                {
                    logicalCanBe = result == continueResult;
                }
            }
            remainingCanBeFacts[expression] = (byte)(facts | known | (logicalCanBe ? possible : 0));
            return logicalCanBe;
        }

        var canBe = expression switch {
            BoundConstant { Type: PrimitiveType.Bool } constant => constant.Value != (result ? 0 : 1),
            BoundUnsafeExpression unsafeExpression =>
                CanBe(unsafeExpression.Expression, result, ref remaining, remainingCanBeFacts),
            BoundUnary { Operator: TokenKind.Bang } unary =>
                CanBe(unary.Operand, !result, ref remaining, remainingCanBeFacts),
            BoundIsNull test => KnownOptionalPresence(test.Value, remainingCanBeFacts,
                ref remaining) is not bool isPresent || result != isPresent,
            BoundIsSome test => KnownOptionalPresence(test.Value, remainingCanBeFacts,
                ref remaining) is not bool isPresent || result == isPresent,
            BoundBinary { Operator: TokenKind.AmpersandAmpersand } and => result
                ? CanBe(and.Left, true, ref remaining, remainingCanBeFacts)
                    && CanBe(and.Right, true, ref remaining, remainingCanBeFacts)
                : CanBe(and.Left, false, ref remaining, remainingCanBeFacts)
                    || (CanBe(and.Left, true, ref remaining, remainingCanBeFacts)
                        && CanBe(and.Right, false, ref remaining, remainingCanBeFacts)),
            BoundBinary { Operator: TokenKind.PipePipe } or => result
                ? CanBe(or.Left, true, ref remaining, remainingCanBeFacts)
                    || (CanBe(or.Left, false, ref remaining, remainingCanBeFacts)
                        && CanBe(or.Right, true, ref remaining, remainingCanBeFacts))
                : CanBe(or.Left, false, ref remaining, remainingCanBeFacts)
                    && CanBe(or.Right, false, ref remaining, remainingCanBeFacts),
            BoundConditional conditional =>
                (CanBe(conditional.Condition, true, ref remaining, remainingCanBeFacts)
                    && CanBe(conditional.WhenTrue, result, ref remaining, remainingCanBeFacts))
                || (CanBe(conditional.Condition, false, ref remaining, remainingCanBeFacts)
                    && CanBe(conditional.WhenFalse, result, ref remaining, remainingCanBeFacts)),
            _ => true,
        };
        remainingCanBeFacts[expression] = (byte)(facts | known | (canBe ? possible : 0));
        return canBe;
    }

    private static bool? KnownOptionalPresence(BoundExpression expression,
        Dictionary<BoundExpression, byte> remainingCanBeFacts, ref int remaining)
    {
        Count(ref remaining, 0);
        if (!ManagedClassTypes.IsOptional(expression.Type))
        {
            return null;
        }
        if (TryGetOptionalPresence(expression, remainingCanBeFacts, out var cachedPresence))
        {
            return cachedPresence;
        }

        var presence = expression switch {
            BoundNull => (bool?)false,
            BoundOptionalLift => true,
            BoundCoalesce coalesce => KnownOptionalPresence(coalesce.Optional,
                remainingCanBeFacts, ref remaining) switch {
                true => true,
                false => KnownOptionalPresence(coalesce.Fallback, remainingCanBeFacts, ref remaining),
                null => null,
            },
            BoundConditional conditional =>
                KnownOptionalPresence(conditional.WhenTrue, remainingCanBeFacts, ref remaining) is bool whenTrue
                    && KnownOptionalPresence(conditional.WhenFalse, remainingCanBeFacts, ref remaining) == whenTrue
                    ? whenTrue : null,
            _ => null,
        };
        var presenceFacts = presence switch {
            true => (byte)(OptionalPresenceAnalyzed | OptionalPresencePresent),
            false => (byte)(OptionalPresenceAnalyzed | OptionalPresenceAbsent),
            null => OptionalPresenceAnalyzed,
        };
        _ = remainingCanBeFacts.TryGetValue(expression, out var existingFacts);
        remainingCanBeFacts[expression] = (byte)(existingFacts | presenceFacts);
        return presence;
    }

    private static bool TryGetOptionalPresence(BoundExpression expression,
        Dictionary<BoundExpression, byte> remainingCanBeFacts, out bool? presence)
    {
        if (remainingCanBeFacts.TryGetValue(expression, out var facts)
            && (facts & OptionalPresenceAnalyzed) != 0)
        {
            presence = (facts & OptionalPresencePresent) != 0 ? true
                : (facts & OptionalPresenceAbsent) != 0 ? false : null;
            return true;
        }
        presence = null;
        return false;
    }

    private static InitializationState Combine(InitializationState left, bool leftFeasible,
        InitializationState right, bool rightFeasible)
    {
        if (!leftFeasible)
        {
            return right;
        }
        if (!rightFeasible)
        {
            return left;
        }
        for (var i = 0; i < left.Length; i++)
        {
            left.Merge(i, right);
        }
        return left;
    }

    private static void Count(ref int remaining, int depth)
    {
        if (depth > MaximumDepth || --remaining < 0)
        {
            throw Limit();
        }
    }

    private static ulong AlignLayout(ulong value, int alignment)
    {
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
        {
            throw Invalid();
        }

        var wideAlignment = checked((ulong)alignment);
        var remainder = value & (wideAlignment - 1);
        if (remainder == 0)
        {
            return value;
        }

        var padding = wideAlignment - remainder;
        if (value > ulong.MaxValue - padding)
        {
            throw Invalid();
        }

        return value + padding;
    }

    private static ArgumentException Invalid() =>
        new("Invalid bound program.", "program");
    private static ArgumentException Invalid(TextSpan span)
    {
        var exception = Invalid();
        exception.Data[SourceSpanKey] = span;
        return exception;
    }
    private static ArgumentException InvalidEffect(TextSpan span, string contract = "NoThrow")
    {
        var exception = Invalid(span);
        exception.Data[InvalidEffectKey] = contract;
        return exception;
    }

    private static ArgumentException InvalidNoFailureEffect(BoundFunction function, TextSpan span) =>
        InvalidEffect(span, function.NoFailureProof ? "NoThrow" : "NativeCallable");

    private static NotSupportedException Limit() => new("Bound program exceeds native resource limits.");
}
