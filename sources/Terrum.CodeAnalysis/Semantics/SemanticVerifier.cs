// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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
    private const byte AllFlowFacts = OutcomeEffectFact | InitializationTestFact;
    internal const int MaximumFunctions = 1024;
    internal const int MaximumVariables = 256;
    internal const int MaximumArguments = 64;
    internal const int MaximumDepth = 128;
    internal const int MaximumAnalysisVisits = 100_000;

    /// <summary>Checks types, control flow, slot mutation, definite assignment, and boundedness of a program. The node limit counts analysis visits, including repeated outcome and feasibility checks.</summary>
    /// <exception cref="ArgumentException">The program is malformed.</exception>
    /// <exception cref="NotSupportedException">The program exceeds an emitter resource limit.</exception>
    public static void Verify(BoundProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        VerifyCore(program, requireEntryPoint: true);
    }

    /// <summary>Checks a fully defined library with no process entry point. Libraries cannot be emitted as executables without linking.</summary>
    /// <exception cref="ArgumentException">The library is malformed or contains an entry point.</exception>
    /// <exception cref="NotSupportedException">The library exceeds verification limits.</exception>
    public static void VerifyLibrary(BoundProgram program) => VerifyCore(program, requireEntryPoint: false);

    internal static void VerifyLibraryWithDirectNativeImports(BoundProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        VerifyDirectNativeImports(program, requireEntryPoint: false);
    }

    internal static void VerifyWithDirectNativeImports(BoundProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        VerifyDirectNativeImports(program, requireEntryPoint: true);
    }

    private static void VerifyDirectNativeImports(BoundProgram program, bool requireEntryPoint)
    {
        if (program.Functions.Any(static function =>
            function.IsExternal && function.DirectNativeImport is null))
        {
            throw Invalid();
        }
        VerifyCore(program, requireEntryPoint, allowExternal: true);
    }

    internal static void VerifyReferences(BoundProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        VerifyCore(program, requireEntryPoint: true, allowExternal: true);
    }

    internal static void VerifyLibraryReferences(BoundProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        VerifyCore(program, requireEntryPoint: false, allowExternal: true);
    }

    private static void VerifyCore(BoundProgram program, bool requireEntryPoint, bool allowExternal = false,
        bool allowGenericProofDependencies = false)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (program.Functions.IsDefaultOrEmpty || (requireEntryPoint
            ? program.EntryPoint < 0 || program.EntryPoint >= program.Functions.Length : program.EntryPoint != -1))
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
        if (!NativePointerTypeContract.IsValidTable(program.NativePointerTypes, program.Structures,
                program.NativeFunctionPointerSignatures))
        {
            throw InvalidEffect(default, "NativePointer");
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
                var variable = function.Variables[variableIndex];
                if (variable is null)
                {
                    throw Invalid();
                }
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
            _ = FixedLayoutStorage.Validate(program.Structures);
        }
        var structNames = new HashSet<string>(program.Structures.Length, StringComparer.Ordinal);
        var layoutStates = new byte[program.Structures.Length];
        var alignments = new int[program.Structures.Length];
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
                || !classNames.Add(@class.Name) || structNames.Contains(@class.Name)
                || @class.Fields.IsDefaultOrEmpty || @class.Fields.Length > ManagedClassTypes.MaximumFields
                || @class.Size is < 8 or > ManagedClassTypes.MaximumPayload || (@class.Size & 7) != 0)
            {
                throw Invalid();
            }
            var offset = 0;
            var fieldNames = new HashSet<string>(@class.Fields.Length, StringComparer.Ordinal);
            foreach (var field in @class.Fields)
            {
                if (field is null || !ValidType(field.Type, allowVoid: false, program)
                    || field.Type is PrimitiveType.OwnedInt32Buffer or PrimitiveType.ReadonlyInt32BufferView
                        or PrimitiveType.TokenSpan or PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
                        or PrimitiveType.MutableInt32BufferView
                    || NativeFixedArrayContract.IsCarrier(field.Type)
                    || IsUnique(program, field.Type))
                {
                    throw Invalid();
                }
                var width = ValueLayout.Size(field.Type, program.Structures);
                var nested = !NativePointerTypes.IsType(field.Type) && !NativeFunctionPointerTypes.IsType(field.Type)
                    && (int)field.Type >= 100 && !ManagedClassTypes.IsClass(field.Type)
                    ? (int)field.Type - 100 : -1;
                var alignment = width == 0 ? 1 : nested >= 0 && nested < alignments.Length
                    ? alignments[nested] : ValueLayout.Alignment(field.Type, program.Structures);
                offset = (offset + alignment - 1) & -alignment;
                if (field.Size != width || field.Offset != offset || (field.IsMutable && !@class.IsMutable)
                    || width is < 1 or > ValueLayout.MaximumSize
                    || offset + width > ManagedClassTypes.MaximumPayload
                    || string.IsNullOrWhiteSpace(field.Name) || !fieldNames.Add(field.Name)
                    || (field.IsPrivate && field.IsInternal))
                {
                    throw Invalid();
                }
                offset += width;
            }
            if (((offset + 7) & ~7) != @class.Size)
            {
                throw Invalid();
            }
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
                || !structNames.Add(structure.Name)
                || (structure.IsPrivate && structure.IsInternal)
                || structure.Fields.IsDefaultOrEmpty
                || structure.Size is < 1 or > ValueLayout.MaximumStructureSize)
            {
                throw Invalid();
            }
            var offset = 0;
            var alignment = 1;
            var height = 1;
            var fieldNames = new HashSet<string>(structure.Fields.Length, StringComparer.Ordinal);
            foreach (var field in structure.Fields)
            {
                if (field is null || !ValidType(field.Type, allowVoid: false, program)
                    || NativeFixedArrayContract.IsCarrier(field.Type))
                {
                    throw Invalid();
                }
                if (ManagedClassTypes.IsManaged(field.Type))
                {
                    throw InvalidEffect(default, "Ownership");
                }
                var nested = !NativePointerTypes.IsType(field.Type) && !NativeFunctionPointerTypes.IsType(field.Type)
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
                var width = ValueLayout.Size(field.Type, program.Structures);
                var fieldAlignment = nested >= 0 ? alignments[nested]
                    : field.Type == PrimitiveType.TokenSpan ? 8 : width;
                var alignedOffset = fieldAlignment == 0 ? offset : (offset + fieldAlignment - 1) & -fieldAlignment;
                if (string.IsNullOrWhiteSpace(field.Name) || !fieldNames.Add(field.Name)
                    || (field.IsPrivate && field.IsInternal)
                    || width == 0 || field.Size != width || field.Offset != alignedOffset
                    || alignedOffset + width > ValueLayout.MaximumStructureSize)
                {
                    throw Invalid();
                }
                offset = alignedOffset + width;
                alignment = Math.Max(alignment, fieldAlignment);
            }
            if (((offset + alignment - 1) & -alignment) != structure.Size || structure.Fields.Length > ValueLayout.MaximumSize)
            {
                throw Invalid();
            }
            if (structure.IsTransparent && (structure.IsUnique || structure.Fields.Length != 1
                || structure.Fields[0].Type is not (PrimitiveType.Bool or PrimitiveType.Char16
                    or PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
                    or PrimitiveType.Float32 or PrimitiveType.Float64)
                || structure.Fields[0].Offset != 0 || structure.Fields[0].Size != structure.Size))
            {
                throw Invalid();
            }
            var isSupportedFixedArrayElement = NativeFixedArrayContract.IsSupportedElementType(
                (PrimitiveType)(100 + index), program.Structures);
            if (structure.Size > ValueLayout.MaximumSize
                && structure.BlittableTargetRequirement == 0
                && !isSupportedFixedArrayElement)
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
                || (function.DoesNotReturn && function.ReturnType != PrimitiveType.Void)
                || function.ReturnType is PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
                || (function.ReturnType == PrimitiveType.NativeRawPointer
                    && !function.RequiresUnsafeCaller)
                || function.ReturnType == PrimitiveType.NativeStorageAddress
                || NativeFixedArrayContract.IsCarrier(function.ReturnType)
                || !ValidType(function.ReturnType, allowVoid: true, program)
                || function.Variables.IsDefault
                || function.ParameterCount < 0 || function.ParameterCount > function.Variables.Length
                || function.Body is null)
            {
                throw Invalid();
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
                            && DirectNativeImportContract.ParameterForParameter(function, variableIndex) is null)
                        || (NativeFixedArrayContract.IsCarrier(variable.Type)
                            && (function.IsExternal || function.IndexedBody is not null
                                || variableIndex < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                                || variable.IsOut || variable.IsInit
                                || variable.IsInitializationLocal
                                || variable.NativeFixedArrayLayout is not BoundNativeFixedArrayLayout fixedArrayLayout
                                || fixedArrayLayout.CarrierType != variable.Type
                                || (variable.IsMutable
                                    && !NativeFixedArrayContract.AllowsMutableLocal(fixedArrayLayout))
                                || !NativeFixedArrayContract.MatchesLayout(fixedArrayLayout, program.Structures)
                                || !NativeFixedArrayContract.MatchesProgram(program)))
                                || (!NativeFixedArrayContract.IsCarrier(variable.Type)
                                    && variable.NativeFixedArrayLayout is not null)
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
                            : !ValidType(variable.Type, allowVoid: false, program)))
                    {
                        throw Invalid();
                    }
                    if (variable.IsInitializationLocal && (variableIndex < function.ParameterCount
                            + (function.IsConstructor ? 1 : 0) || variable.IsOut || variable.IsInit))
                    {
                        throw Invalid();
                    }
                    if ((variable.IsInit && (!variable.IsOut
                        || (variable.Type is not (PrimitiveType.Bool or PrimitiveType.Char16 or PrimitiveType.Int32
                            or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
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
                                    or PrimitiveType.Char16 or PrimitiveType.Int32 or PrimitiveType.UInt32
                                    or PrimitiveType.Int64 or PrimitiveType.UInt64
                                    or PrimitiveType.Float32 or PrimitiveType.Float64)
                                && !IsCopyStruct(program, variable.Type)
                                && DirectNativeImportContract.OutputForParameter(function, variableIndex) is null
                                && !NativeVTableCallContract.IsOutputParameter(function, variableIndex,
                                    program)))))
                    {
                        throw Invalid();
                    }
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
            else if (function.IsConstructor || function.IsPrivate)
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
                                or PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64
                                or PrimitiveType.UInt64 or PrimitiveType.Float32 or PrimitiveType.Float64)
                                && !IsCopyStruct(program, function.ReturnType))
                        : function.ParameterCount != 1 || function.Variables[0].IsMutable
                            || IsUnique(program, function.Variables[0].Type))
                    || !IsClassArgumentType(function.ReturnType, program.Structures)
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
                    .Any(variable => variable.IsOut || !IsClassArgumentType(variable.Type, program.Structures))))
            {
                throw Invalid();
            }
            if (!function.IsClassConstructor && function.ClassConstructor >= 0)
            {
                throw Invalid();
            }
            if (function.IsCleanup && (function.DeclaringStruct is null || function.IsConstructor
                || function.IsStatic || function.IsPrivate || function.IsInternal || !function.IsNoThrow
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
        for (var functionIndex = 0; functionIndex < program.Functions.Length; functionIndex++)
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
            BackingDependencies? backingDependencies;
            try
            {
                backingDependencies = CreateBackingDependencies(function, program, hasSpanDependencies);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                exception.Data[FunctionIndexKey] = functionIndex;
                throw;
            }
            var initialized = new InitializationState(function.Variables.Length) {
                StatementInputs = function.Variables.Any(variable => variable.Type is
                    PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView)
                    ? new Dictionary<BoundStatement, InitializationState>(ReferenceEqualityComparer.Instance) : null,
                Dependencies = backingDependencies,
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
                }
            }
            var declarations = new HashSet<int>();
            try
            {
                VerifyOwnerScopes(function.Body, function, program);
                VerifySpanScopes(function);
                VerifyManagedRoots(function);
                var returns = Statement(function.Body, initialized, fieldMasks, declarations, function, program, ref remaining, 0);
                VerifyViewScopes(function.Body, function, initialized.StatementInputs);
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
                        throw Invalid();
                    }
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                exception.Data[FunctionIndexKey] = functionIndex;
                throw;
            }
        }

        VerifySourceGrantNativeCallables(program, requireEntryPoint);

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

    private static void VerifyManagedRoots(BoundFunction function)
    {
        var retired = new HashSet<int>();
        var active = new HashSet<int>();
        var remaining = MaximumAnalysisVisits;
        for (var slot = 0; slot < function.ParameterCount + (function.IsConstructor ? 1 : 0); slot++)
        {
            if (ManagedClassTypes.IsManaged(function.Variables[slot].Type))
            {
                _ = active.Add(slot);
            }
        }

        void Declare(int slot, HashSet<int> scope, TextSpan span)
        {
            if (slot < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                || slot >= function.Variables.Length || slot < 0
                || !ManagedClassTypes.IsManaged(function.Variables[slot].Type)
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
                case BoundRead read when ManagedClassTypes.IsManaged(read.Type):
                    if (!active.Contains(read.Variable))
                    {
                        throw InvalidEffect(read.Span, "Ownership");
                    }
                    break;
                case BoundOutArgument output when ManagedClassTypes.IsManaged(output.Type):
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
                case BoundNativeFixedArrayElement fixedArrayElement:
                    Expression(fixedArrayElement.Index, scope, depth + 1);
                    break;
                case BoundNativeFixedArrayPointerAddress pointerAddress:
                    Expression(pointerAddress.Index, scope, depth + 1);
                    break;
                case BoundNativeStorageAddress address:
                    Expression(address.Index, scope, depth + 1);
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
                    Declare(pattern.Variable, scope, pattern.Span);
                    break;
                case BoundIsVariablePattern pattern:
                    Expression(pattern.Value, scope, depth + 1);
                    Declare(pattern.Variable, scope, pattern.Span);
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
                    if (ManagedClassTypes.IsManaged(function.Variables[local.Variable].Type))
                    {
                        Declare(local.Variable, scope, local.Span);
                    }
                    break;
                case BoundVacantLocal vacant:
                    if (vacant.Variable < 0 || vacant.Variable >= function.Variables.Length)
                    {
                        throw Invalid();
                    }
                    if (ManagedClassTypes.IsManaged(function.Variables[vacant.Variable].Type))
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
                    if (ManagedClassTypes.IsManaged(function.Variables[assignment.Variable].Type)
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
            if (ManagedClassTypes.IsManaged(function.Variables[slot].Type) && !retired.Contains(slot))
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

    private static void VerifyViewScopes(BoundBlock body, BoundFunction function,
        Dictionary<BoundStatement, InitializationState>? inputs)
    {
        var roots = new int[function.Variables.Length];
        var exclusive = new bool[roots.Length];
        var active = new bool[roots.Length];
        var backing = new int[roots.Length];
        var declared = new HashSet<int>();
        var nativeLocals = function.Variables.Any(static variable =>
            NativeFixedArrayContract.IsCarrier(variable.Type))
            ? new HashSet<int>() : null;
        var remaining = MaximumAnalysisVisits;

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
                    if (length.Variable < 0 || length.Variable >= active.Length || !active[length.Variable])
                    {
                        throw InvalidEffect(length.Span, "Ownership");
                    }
                    break;
                case BoundViewRead viewRead:
                    if (viewRead.Variable < 0 || viewRead.Variable >= active.Length || !active[viewRead.Variable])
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
                    Expression(address.Index, depth + 1, acquiringRoot);
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
                case BoundNativeFixedArrayPointerAddress pointerAddress:
                    Expression(pointerAddress.Index, depth + 1, acquiringRoot);
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
            for (var slot = 0; slot < active.Length; slot++)
            {
                if (active[slot] && inputs is not null && !inputs[statement].IsVacant(slot))
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
                        || !declared.Add(view.Variable) || active[view.Variable]
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
                    active[view.Variable] = true;
                    Statement(view.Body, depth + 1);
                    active[view.Variable] = false;
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
                    if (viewWrite.Variable < 0 || viewWrite.Variable >= active.Length || !active[viewWrite.Variable]
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
                    if (!item.Sequential || !assignment.NestedFields.IsEmpty || order.Contains(assignment.Field))
                    {
                        throw InvalidEffect(assignment.Span, "Ownership");
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
        foreach (var function in program.Functions)
        {
            var pending = new Stack<(object Node, bool IsUnsafe, int Depth)>();
            pending.Push((function.Body, false, 0));
            var remaining = MaximumAnalysisVisits;
            while (pending.TryPop(out var item))
            {
                Count(ref remaining, item.Depth);
                var depth = item.Depth + 1;
                switch (item.Node)
                {
                    case BoundBlock block:
                        foreach (var statement in block.Statements)
                        {
                            pending.Push((statement, item.IsUnsafe, depth));
                        }
                        break;
                    case BoundUnsafe unsafeBlock:
                        pending.Push((unsafeBlock.Body, true, depth));
                        break;
                    case BoundUnsafeExpression unsafeExpression:
                        pending.Push((unsafeExpression.Expression, true, depth));
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
                    case BoundLocal local:
                        pending.Push((local.Initializer, item.IsUnsafe, depth));
                        break;
                    case BoundAssignment assignment:
                        pending.Push((assignment.Value, item.IsUnsafe, depth));
                        break;
                    case BoundOptionalAssignment assignment:
                        pending.Push((assignment.Place, item.IsUnsafe, depth));
                        pending.Push((assignment.Value, item.IsUnsafe, depth));
                        break;
                    case BoundFieldAssignment assignment:
                        pending.Push((assignment.Value, item.IsUnsafe, depth));
                        break;
                    case BoundBufferAssignment assignment:
                        pending.Push((assignment.Index, item.IsUnsafe, depth));
                        pending.Push((assignment.Value, item.IsUnsafe, depth));
                        break;
                    case BoundViewSet view:
                        pending.Push((view.Index, item.IsUnsafe, depth));
                        pending.Push((view.Value, item.IsUnsafe, depth));
                        break;
                    case BoundExpressionStatement statement:
                        pending.Push((statement.Expression, item.IsUnsafe, depth));
                        break;
                    case BoundIf conditional:
                        pending.Push((conditional.Condition, item.IsUnsafe, depth));
                        pending.Push((conditional.Then, item.IsUnsafe, depth));
                        if (conditional.Else is not null)
                        {
                            pending.Push((conditional.Else, item.IsUnsafe, depth));
                        }
                        break;
                    case BoundWhile loop:
                        pending.Push((loop.Condition, item.IsUnsafe, depth));
                        pending.Push((loop.Body, item.IsUnsafe, depth));
                        if (loop.Iterator is not null)
                        {
                            pending.Push((loop.Iterator, item.IsUnsafe, depth));
                        }
                        break;
                    case BoundTryFinally cleanup:
                        pending.Push((cleanup.Body, item.IsUnsafe, depth));
                        pending.Push((cleanup.Finally, item.IsUnsafe, depth));
                        break;
                    case BoundTryCatch recovery:
                        pending.Push((recovery.Body, item.IsUnsafe, depth));
                        pending.Push((recovery.Catch, item.IsUnsafe, depth));
                        if (recovery.Finally is not null)
                        {
                            pending.Push((recovery.Finally, item.IsUnsafe, depth));
                        }
                        break;
                    case BoundViewScope view:
                        pending.Push((view.Owner, item.IsUnsafe, depth));
                        pending.Push((view.Start, item.IsUnsafe, depth));
                        pending.Push((view.Length, item.IsUnsafe, depth));
                        pending.Push((view.Body, item.IsUnsafe, depth));
                        if (view.RangeEnd is not null)
                        {
                            pending.Push((view.RangeEnd, item.IsUnsafe, depth));
                        }
                        break;
                    case BoundRequire require:
                        pending.Push((require.Condition, item.IsUnsafe, depth));
                        break;
                    case BoundReturn { Value: BoundExpression value }:
                        pending.Push((value, item.IsUnsafe, depth));
                        break;
                    case BoundExpression expression:
                        if (!expression.HasValidNestedExpressions)
                        {
                            throw Invalid(expression.Span);
                        }
                        foreach (var nested in expression.NestedExpressions)
                        {
                            pending.Push((nested, item.IsUnsafe, depth));
                        }
                        break;
                    case BoundStatement:
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
        bool allowDirectNativeImports = false)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if ((!allowDirectNativeImports
                && program.Functions.Any(static function => function.DirectNativeImport is not null))
            || program.Functions.Any(static function => function.ReturnType is
                PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
                || NativeFixedArrayContract.IsCarrier(function.ReturnType)
                || function.Variables.Any(variable => variable.Type is
                    PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
                    || NativeFixedArrayContract.IsCarrier(variable.Type)
                    || variable.NativeFixedArrayLayout is not null))
            || (!program.Structures.IsDefaultOrEmpty && program.Structures.Any(
                static structure => structure.Fields.Any(
                    static field => field.Type is PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
                        || NativeFixedArrayContract.IsCarrier(field.Type))))
            || (!program.Classes.IsDefaultOrEmpty && program.Classes.Any(
                static @class => @class.Fields.Any(
                static field => field.Type is PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
                            || NativeFixedArrayContract.IsCarrier(field.Type))))
            || !program.NativeUtf16ZLiterals.IsEmpty || !program.NativeUtf8ZLiterals.IsEmpty)
        {
            throw new NotSupportedException($"Source native imports are not supported by {target}.");
        }
    }

    internal static bool TryVerifySource(BoundProgram program, out int functionIndex, out TextSpan span, out bool exceededWorkLimit,
        out string? invalidEffect, bool allowGenericProofDependencies = false)
    {
        try
        {
            VerifyCore(program, requireEntryPoint: program.EntryPoint != -1, allowExternal: true,
                allowGenericProofDependencies: allowGenericProofDependencies);
            functionIndex = -1;
            span = default;
            exceededWorkLimit = false;
            invalidEffect = null;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            functionIndex = exception.Data[FunctionIndexKey] is int index ? index : Math.Max(0, program.EntryPoint);
            span = exception.Data[SourceSpanKey] is TextSpan sourceSpan
                ? sourceSpan : program.Functions.IsDefaultOrEmpty ? default : program.Functions[functionIndex].Span;
            exceededWorkLimit = exception is NotSupportedException;
            invalidEffect = exception.Data[InvalidEffectKey] is string effect
                ? effect
                : exception.Message;
            return false;
        }
    }

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
                var counted = inCleanup && loops.Exists(static scope => scope.IsCleanupSwitch)
                    ? RecognizeCleanupCountedLoop(block, function, program, ref remaining, depth + 1) : null;
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
                return returns;
            case BoundLocal local:
                if (local.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    || local.Variable >= initialized.Length
                    || !declarations.Add(local.Variable) || initialized[local.Variable]
                    || function.Variables[local.Variable].IsInitializationLocal
                    || function.Variables[local.Variable].IsImplicitReferenceTemporary)
                {
                    throw Invalid();
                }
                if (NativeFixedArrayContract.IsCarrier(function.Variables[local.Variable].Type))
                {
                    VerifyNativeFixedArrayInitialization(local.Initializer, local.Variable, initialized,
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
                initialized[local.Variable] = true;
                if (HasFields(program, local.Initializer.Type))
                {
                    fieldMasks[local.Variable] = FullMask(program, local.Initializer.Type);
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
                return false;
            case BoundLifetimeEnd end:
                if (end.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    || end.Variable >= initialized.Length
                    || !declarations.Contains(end.Variable) || !initialized[end.Variable]
                    || (function.Variables[end.Variable].Type is not (PrimitiveType.ReadonlyInt32BufferView
                        or PrimitiveType.MutableInt32BufferView)
                        && !ValidType(function.Variables[end.Variable].Type, allowVoid: false, program)))
                {
                    throw Invalid(end.Span);
                }
                var endType = function.Variables[end.Variable].Type;
                var endMethod = IsUnique(program, endType) && endType != PrimitiveType.OwnedInt32Buffer
                    ? program.Structures[(int)endType - 100].CleanupMethod : null;
                if (endMethod is null ? end.CleanupFunction != -1
                    : end.CleanupFunction < 0 || end.CleanupFunction >= program.Functions.Length
                        || program.Functions[end.CleanupFunction].Name != endMethod)
                {
                    throw Invalid(end.Span);
                }
                CheckBackingInvalidation(end.Variable, initialized, end.Span);
                initialized.Retire(end.Variable);
                fieldMasks[end.Variable] = 0;
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
                initialized[drop.Variable] = false;
                fieldMasks[drop.Variable] = 0;
                return false;
            case BoundDropSpan span:
                if (!inCleanup || span.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    || span.Variable >= initialized.Length || !declarations.Contains(span.Variable)
                    || function.Variables[span.Variable].Type != PrimitiveType.TokenSpan)
                {
                    throw InvalidEffect(span.Span, "Ownership");
                }
                initialized[span.Variable] = false;
                return false;
            case BoundDropConstructedFields partial:
                if (!inCleanup || !function.IsConstructor || partial.Variable != 0
                    || partial.AcquisitionOrder.IsDefault)
                {
                    throw Invalid(partial.Span);
                }
                return false;
            case BoundAssignment assignment:
                CheckBackingInvalidation(assignment.Variable, initialized, assignment.Span);
                if (assignment.Variable < 0 || assignment.Variable >= initialized.Length
                    || (assignment.Variable < function.ParameterCount && !function.Variables[assignment.Variable].IsOut)
                    || (function.IsConstructor && assignment.Variable == 0)
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
                Expression(assignment.Value, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures,
                    cleanupCounterStep: cleanupCounterStep);
                if (assignment.Value.Type != function.Variables[assignment.Variable].Type)
                {
                    throw Invalid();
                }
                if (HasFields(program, assignment.Value.Type))
                {
                    fieldMasks[assignment.Variable] = FullMask(program, assignment.Value.Type);
                }
                initialized[assignment.Variable] = true;
                return false;
            case BoundOptionalAssignment optionalAssignment:
                if (optionalAssignment.Place is null || optionalAssignment.Value is null
                    || !ManagedClassTypes.IsOptional(optionalAssignment.Place.Type)
                    || optionalAssignment.Value.Type != optionalAssignment.Place.Type)
                {
                    throw Invalid(optionalAssignment.Span);
                }
                if (optionalAssignment.Place is BoundRead localPlace)
                {
                    if (localPlace.Variable < 0 || localPlace.Variable >= initialized.Length
                        || (localPlace.Variable < function.ParameterCount
                            && !function.Variables[localPlace.Variable].IsOut)
                        || (function.IsConstructor && localPlace.Variable == 0)
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
                Expression(optionalAssignment.Place, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                var assignedState = initialized.Clone();
                var assignedMasks = (uint[])fieldMasks.Clone();
                var assignedDeclarations = new HashSet<int>(declarations);
                Expression(optionalAssignment.Value, assignedState, assignedMasks, assignedDeclarations, function, program,
                    ref remaining, depth + 1, failures);
                declarations.UnionWith(assignedDeclarations);
                for (var index = 0; index < initialized.Length; index++)
                {
                    initialized.Merge(index, assignedState);
                    fieldMasks[index] &= assignedMasks[index];
                }
                return false;
            case BoundFieldAssignment fieldAssignment:
                CheckBackingInvalidation(fieldAssignment.Variable, initialized, fieldAssignment.Span);
                if (fieldAssignment.Variable < 0 || fieldAssignment.Variable >= initialized.Length
                    || (!function.Variables[fieldAssignment.Variable].IsMutable
                        && !ManagedClassTypes.IsClass(function.Variables[fieldAssignment.Variable].Type))
                    || fieldAssignment.NestedFields.IsDefault)
                {
                    throw Invalid();
                }
                var ownerType = function.Variables[fieldAssignment.Variable].Type;
                if (!HasFields(program, ownerType)
                    || fieldAssignment.Field < 0 || fieldAssignment.Field >= Fields(program, ownerType).Length
                    || !CanAccessField(function, program, ownerType, fieldAssignment.Field)
                    || (!initialized[fieldAssignment.Variable]
                        && (!(function.IsConstructor || function.IsClassConstructor)
                            || fieldAssignment.Variable != 0)))
                {
                    throw Invalid();
                }
                if (IsUnique(program, ownerType) && function.Variables[fieldAssignment.Variable].IsOut)
                {
                    throw InvalidEffect(fieldAssignment.Span, "Ownership");
                }
                var targetField = Fields(program, ownerType)[fieldAssignment.Field];
                var requiresUnsafeCaller = targetField.RequiresUnsafeCaller;
                if (ManagedClassTypes.IsClass(ownerType) && !ManagedClassTypes.IsWritable(ownerType)
                    && !((function.IsConstructor || function.IsClassConstructor)
                        && fieldAssignment.Variable == 0 && fieldAssignment.NestedFields.IsEmpty)
                    && (fieldAssignment.NestedFields.IsEmpty || !ManagedClassTypes.IsClass(targetField.Type)))
                {
                    throw Invalid();
                }
                if (IsUnique(program, targetField.Type)
                    && (!function.IsConstructor || fieldAssignment.Variable != 0
                        || !fieldAssignment.NestedFields.IsEmpty
                        || (fieldMasks[0] & (1u << fieldAssignment.Field)) != 0))
                {
                    throw InvalidEffect(fieldAssignment.Span, "Ownership");
                }
                if (!targetField.IsMutable
                    && (!ManagedClassTypes.IsClass(ownerType) || fieldAssignment.NestedFields.IsEmpty
                        || !ManagedClassTypes.IsClass(targetField.Type))
                    && !((function.IsConstructor || function.IsClassConstructor) && fieldAssignment.Variable == 0))
                {
                    throw Invalid();
                }
                var nestedWrite = !fieldAssignment.NestedFields.IsEmpty;
                if (nestedWrite && (fieldMasks[fieldAssignment.Variable] & (1u << fieldAssignment.Field)) == 0)
                {
                    throw Invalid();
                }
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
                    requiresUnsafeCaller |= targetField.RequiresUnsafeCaller;
                    if (ManagedClassTypes.IsClass(nestedType) && !ManagedClassTypes.IsWritable(nestedType)
                        && (index == fieldAssignment.NestedFields.Length - 1
                            || !ManagedClassTypes.IsClass(targetField.Type)))
                    {
                        throw Invalid();
                    }
                    if (IsUnique(program, nestedType))
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
                if (requiresUnsafeCaller && !fieldAssignment.IsUnsafeAuthorized)
                {
                    throw InvalidEffect(fieldAssignment.Span, "UnsafeCaller");
                }
                Expression(fieldAssignment.Value, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                if (IsUnique(program, targetField.Type)
                    && fieldAssignment.Value is not (BoundMove or BoundCall))
                {
                    throw InvalidEffect(fieldAssignment.Span, "Ownership");
                }
                if (fieldAssignment.Value.Type != targetField.Type
                    || (nestedWrite && (fieldMasks[fieldAssignment.Variable] & (1u << fieldAssignment.Field)) == 0))
                {
                    throw Invalid();
                }
                if (targetField.Type == PrimitiveType.TokenSpan)
                {
                    VerifySpanFieldWrite(fieldAssignment, function, initialized);
                }
                fieldMasks[fieldAssignment.Variable] |= 1u << fieldAssignment.Field;
                initialized[fieldAssignment.Variable] = fieldMasks[fieldAssignment.Variable]
                    == FullMask(program, ownerType);
                return false;
            case BoundBufferAssignment bufferWrite:
                if (function.IsNoThrow && !IsProvenBufferIndex(bufferWrite.Buffer, bufferWrite.Index, program))
                {
                    throw InvalidEffect(bufferWrite.Span);
                }
                if (bufferWrite.Buffer < 0 || bufferWrite.Buffer >= program.Buffers.Length
                    || !program.Buffers[bufferWrite.Buffer].IsWritable)
                {
                    throw Invalid();
                }
                Expression(bufferWrite.Index, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                failures?.Add(initialized, fieldMasks);
                Expression(bufferWrite.Value, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                if (bufferWrite.Index.Type != PrimitiveType.Int32
                    || bufferWrite.Value.Type != program.Buffers[bufferWrite.Buffer].ElementType)
                {
                    throw Invalid();
                }
                return false;
            case BoundViewSet viewSet:
                if (viewSet.Variable < 0 || viewSet.Variable >= initialized.Length
                    || !initialized[viewSet.Variable]
                    || function.Variables[viewSet.Variable].Type != PrimitiveType.MutableInt32BufferView)
                {
                    throw InvalidEffect(viewSet.Span, "Ownership");
                }
                Expression(viewSet.Index, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                Expression(viewSet.Value, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                if (viewSet.Index.Type != PrimitiveType.Int32 || viewSet.Value.Type != PrimitiveType.Int32)
                {
                    throw Invalid();
                }
                if (function.IsNoThrow)
                {
                    throw InvalidEffect(viewSet.Span);
                }
                failures?.Add(initialized, fieldMasks);
                return false;
            case BoundExpressionStatement expressionStatement:
                var effect = UnwrapUnsafeExpression(expressionStatement.Expression);
                if (effect is not (BoundCall or BoundIndirectCall or BoundViewRead or BoundUpdate
                    or BoundNominalUpdate or BoundCompoundAssignment or BoundNativeFixedArrayWrite
                    or BoundNativePointerWrite))
                {
                    throw Invalid();
                }
                if (IsUnique(program, effect.Type))
                {
                    throw InvalidEffect(expressionStatement.Span, "Ownership");
                }
                Expression(expressionStatement.Expression, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures, statementCompound: effect is BoundCompoundAssignment);
                return effect is BoundCall statementCall
                    && program.Functions[statementCall.Function].DoesNotReturn;
            case BoundIf conditional:
                var beforeCondition = initialized.Clone();
                Expression(conditional.Condition, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                if (conditional.Condition.Type != PrimitiveType.Bool || conditional.Then is null)
                {
                    throw Invalid();
                }
                var thenReachable = CanBe(conditional.Condition, true, ref remaining, initialized);
                var elseReachable = CanBe(conditional.Condition, false, ref remaining, initialized);
                var thenState = Outcome(conditional.Condition, beforeCondition, true, ref remaining);
                var thenMasks = (uint[])fieldMasks.Clone();
                MarkOutMasks(thenState, thenMasks, function, program);
                var thenReturns = Statement(conditional.Then, thenState, thenMasks, declarations, function, program, ref remaining, depth + 1,
                    reachable && thenReachable, cleanups, inCleanup, inUnsafe, failures, loops);
                var elseState = Outcome(conditional.Condition, beforeCondition, false, ref remaining);
                var elseMasks = (uint[])fieldMasks.Clone();
                MarkOutMasks(elseState, elseMasks, function, program);
                var elseReturns = conditional.Else is not null
                    && Statement(conditional.Else, elseState, elseMasks, declarations, function, program, ref remaining, depth + 1,
                        reachable && elseReachable, cleanups, inCleanup, inUnsafe, failures, loops);
                for (var i = 0; i < initialized.Length; i++)
                {
                    initialized.SetPossibilities(i, !thenReachable || thenReturns ? elseState.Possibilities(i)
                        : !elseReachable || elseReturns ? thenState.Possibilities(i)
                        : (byte)(thenState.Possibilities(i) | elseState.Possibilities(i)));
                    fieldMasks[i] = !thenReachable || thenReturns ? elseMasks[i]
                        : !elseReachable || elseReturns ? thenMasks[i] : thenMasks[i] & elseMasks[i];
                }
                return (!thenReachable || thenReturns) && (!elseReachable || elseReturns);
            case BoundWhile loop:
                if ((loop.IsPostTested && loop.Iterator is not null)
                    || loop.Iterator is not (null or BoundLifetimeEnd or BoundAssignment or BoundExpressionStatement
                    or BoundBlock { Statements.IsEmpty: true, IsManagedLexicalScope: false, RetiredManagedLocals.IsEmpty: true }))
                {
                    throw Invalid(loop.Span);
                }
                var verifiedBody = countedLoop?.Body ?? loop.Body;
                var verifiedIterator = countedLoop?.Iterator ?? loop.Iterator;
                var entryState = initialized.Clone();
                var entryMasks = (uint[])fieldMasks.Clone();
                var headState = entryState.Clone();
                var headMasks = (uint[])entryMasks.Clone();
                var priorDeclarations = new HashSet<int>(declarations);
                for (var iteration = 0; iteration <= initialized.Length * 9; iteration++)
                {
                    var conditionInput = headState.Clone();
                    var conditionState = headState.Clone();
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
                    var bodyState = loop.IsPostTested ? headState.Clone()
                        : Outcome(loop.Condition, conditionInput, true, ref remaining);
                    var bodyMasks = (uint[])conditionMasks.Clone();
                    MarkOutMasks(bodyState, bodyMasks, function, program);
                    var bodyReachable = loop.IsPostTested || CanBe(loop.Condition, true, ref remaining, initialized);
                    var exitReachable = CanBe(loop.Condition, false, ref remaining, initialized);
                    var iteratorDeclarations = new HashSet<int>(iterationDeclarations);
                    var loopScope = new LoopScope(cleanups.Count) {
                        IsCleanupSwitch = inCleanup && loop.SwitchArmVariable >= 0,
                    };
                    loops.Add(loopScope);
                    var bodyReturns = Statement(verifiedBody, bodyState, bodyMasks, iterationDeclarations, function, program, ref remaining, depth + 1,
                        reachable && bodyReachable, cleanups, inCleanup, inUnsafe, failures, loops);
                    loops.RemoveAt(loops.Count - 1);
                    var exitState = loop.IsPostTested ? conditionState.Clone()
                        : Outcome(loop.Condition, conditionInput, false, ref remaining);
                    var exitMasks = (uint[])conditionMasks.Clone();
                    MarkOutMasks(exitState, exitMasks, function, program);
                    var backEdges = new FailureEdges();
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
                        var iteratorState = backEdges.HasEdges ? backEdges.State : bodyState.Clone();
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
                        backEdges = new FailureEdges();
                        if (!iteratorReturns && bodyReachable && (loopScope.Continues.HasEdges || !bodyReturns))
                        {
                            backEdges.Add(iteratorState, iteratorMasks);
                        }
                    }
                    if (loop.IsPostTested)
                    {
                        var conditionInputState = backEdges.HasEdges ? backEdges.State : bodyState.Clone();
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
                        var evaluatedState = conditionInputState.Clone();
                        var evaluatedMasks = (uint[])conditionInputMasks.Clone();
                        Expression(loop.Condition, evaluatedState, evaluatedMasks, conditionDeclarations,
                            function, program, ref remaining, depth + 1, failures);
                        var falseState = Outcome(loop.Condition, conditionInputState, false, ref remaining);
                        var trueState = Outcome(loop.Condition, conditionInputState, true, ref remaining);
                        var trueMasks = (uint[])evaluatedMasks.Clone();
                        MarkOutMasks(falseState, evaluatedMasks, function, program);
                        MarkOutMasks(trueState, trueMasks, function, program);
                        exitState = falseState;
                        exitMasks = evaluatedMasks;
                        backEdges = new FailureEdges();
                        if (bodyReachable && (loopScope.Continues.HasEdges || !bodyReturns)
                            && CanBe(loop.Condition, true, ref remaining, initialized))
                        {
                            backEdges.Add(trueState, trueMasks);
                        }
                        exitReachable &= bodyReachable && (loopScope.Continues.HasEdges || !bodyReturns);
                        iterationDeclarations.UnionWith(conditionDeclarations);
                    }
                    var exits = new FailureEdges();
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
                    }
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
            case BoundBreak or BoundContinue:
                var targetDepth = statement is BoundBreak exit ? exit.TargetDepth
                    : ((BoundContinue)statement).TargetDepth;
                if (targetDepth <= 0 || targetDepth > loops.Count)
                {
                    throw InvalidEffect(statement.Span, inCleanup ? "FinallyReturn" : "LoopTransfer");
                }
                var target = loops[^targetDepth];
                var transferState = initialized.Clone();
                var transferMasks = (uint[])fieldMasks.Clone();
                var transferDeclarations = new HashSet<int>(declarations);
                for (var index = cleanups.Count - 1; index >= target.CleanupDepth; index--)
                {
                    VerifyCleanup(cleanups[index].Body, transferState, transferMasks, transferDeclarations,
                        function, program, ref remaining, depth + 1, reachable, inUnsafe, cleanups[index].Visible);
                }
                if (reachable)
                {
                    (statement is BoundBreak ? target.Breaks : target.Continues).Add(transferState, transferMasks);
                }
                return true;
            case BoundTryFinally cleanup:
                return TryFinallyStatement(cleanup, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth, reachable, cleanups, inCleanup, inUnsafe, failures, loops);
            case BoundViewScope view:
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
                Expression(view.Start, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                var rangeBound = view.RangeEnd ?? view.Length;
                Expression(rangeBound, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                var invalidRangeShape = view.RangeEnd is null
                    ? view.IsStartEndRelative || view.IsRangeEndRelative
                    : view.IsMutable || view.Length is not BoundConstant { Type: PrimitiveType.Int32, Value: 0 };
                if (view.Start.Type != PrimitiveType.Int32 || rangeBound.Type != PrimitiveType.Int32
                    || invalidRangeShape)
                {
                    throw Invalid();
                }
                if (function.IsNoThrow)
                {
                    throw InvalidEffect(view.Span);
                }
                failures?.Add(initialized, fieldMasks);
                initialized[view.Variable] = true;
                var viewReturns = Statement(view.Body, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, reachable, cleanups, inCleanup, inUnsafe, failures, loops);
                initialized[view.Variable] = false;
                return viewReturns;
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
                var beforeRequirement = initialized.Clone();
                Expression(requirement.Condition, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                if (requirement.Condition.Type != PrimitiveType.Bool)
                {
                    throw Invalid();
                }
                if (function.IsNoThrow && CanBe(requirement.Condition, false, ref remaining, initialized))
                {
                    throw InvalidEffect(requirement.Span);
                }
                if (CanBe(requirement.Condition, false, ref remaining, initialized))
                {
                    var rejected = Outcome(requirement.Condition, beforeRequirement, false, ref remaining);
                    var rejectedMasks = (uint[])fieldMasks.Clone();
                    MarkOutMasks(rejected, rejectedMasks, function, program);
                    failures?.Add(rejected, rejectedMasks);
                }
                var accepted = Outcome(requirement.Condition, beforeRequirement, true, ref remaining);
                accepted.CopyTo(initialized);
                MarkOutMasks(initialized, fieldMasks, function, program);
                return !CanBe(requirement.Condition, true, ref remaining, initialized);
            case BoundReturn result:
                if (inCleanup)
                {
                    throw InvalidEffect(result.Span, "FinallyReturn");
                }
                if (function.DoesNotReturn && reachable)
                {
                    throw InvalidEffect(result.Span, "DoesNotReturn");
                }
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
                    if (function.IsConstructor && result.Value is not BoundRead { Variable: 0 })
                    {
                        throw Invalid(result.Span);
                    }
                    if (function.IsConstructor && IsUnique(program, function.ReturnType)
                        && result.Value is BoundRead { Variable: 0 } receiver)
                    {
                        if (!initialized[0] || receiver.Type != function.ReturnType)
                        {
                            throw Invalid(result.Span);
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
                IReadOnlySet<int>? forwardedBacking = null;
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
                if (function.ReturnType == PrimitiveType.Bool && result.Value is BoundExpression boolResult)
                {
                    if (cleanups.Count != 0 || hasOutParameter)
                    {
                        var outcomeCount = cleanups.Count != 0 ? 2 : 1;
                        for (var outcome = 0; outcome < outcomeCount; outcome++)
                        {
                            var predicate = outcome == 0;
                            if (!CanBe(boolResult, predicate, ref remaining, initialized))
                            {
                                continue;
                            }
                            var returnState = Outcome(boolResult, beforeReturn, predicate, ref remaining);
                            var returnMasks = (uint[])fieldMasks.Clone();
                            MarkOutMasks(returnState, returnMasks, function, program);
                            var returnDeclarations = new HashSet<int>(declarations);
                            for (var index = cleanups.Count - 1; index >= 0; index--)
                            {
                                VerifyCleanup(cleanups[index].Body, returnState, returnMasks, returnDeclarations,
                                    function, program, ref remaining, depth + 1, reachable, inUnsafe,
                                    cleanups[index].Visible, successfulReturn: predicate);
                            }
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
                else
                {
                    if (cleanups.Count != 0 || hasOutParameter)
                    {
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
                            dependencies.Staged.Add(forwardedBacking);
                        }
                        try
                        {
                            for (var index = cleanups.Count - 1; index >= 0; index--)
                            {
                                VerifyCleanup(cleanups[index].Body, returnState, returnMasks, returnDeclarations,
                                    function, program, ref remaining, depth + 1, reachable, inUnsafe,
                                    cleanups[index].Visible, successfulReturn: true);
                            }
                            for (var i = parameterOffset; i < function.ParameterCount + parameterOffset; i++)
                            {
                                if (function.Variables[i].IsOut && !returnState[i])
                                {
                                    throw Invalid(result.Span);
                                }
                            }
                        }
                        finally
                        {
                            dependencies?.Staged.RemoveRange(stagedCount, dependencies.Staged.Count - stagedCount);
                        }
                    }
                }
                return true;
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
        var cleanupFailures = program.RuntimePolicy == BoundRuntimePolicy.CheckedExceptions
            ? new FailureEdges() : null;
        var cleanupScope = new CleanupScope(cleanup.Finally, [.. declarations]);
        cleanups.Add(cleanupScope);
        var tryReturns = Statement(cleanup.Body, initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, reachable, cleanups, inCleanup, inUnsafe, cleanupFailures, loops);
        cleanups.RemoveAt(cleanups.Count - 1);
        if (cleanupFailures?.HasEdges == true)
        {
            var exceptionalState = cleanupFailures.State;
            var exceptionalMasks = cleanupFailures.Masks;
            VerifyCleanup(cleanup.Finally, exceptionalState, exceptionalMasks, [.. declarations],
                function, program, ref remaining, depth + 1, reachable, inUnsafe, cleanupScope.Visible);
            failures?.Add(exceptionalState, exceptionalMasks);
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
            VerifyCleanup(cleanup.Finally, unreachableState, unreachableMasks, declarations, function,
                program, ref remaining, depth + 1, reachable: false, inUnsafe, cleanupScope.Visible);
        }
        else
        {
            VerifyCleanup(cleanup.Finally, initialized, fieldMasks, declarations, function, program,
                ref remaining, depth + 1, reachable, inUnsafe, cleanupScope.Visible);
        }
        return tryReturns;
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
        var recoveryEntryState = initialized.Clone();
        var recoveryEntryMasks = (uint[])fieldMasks.Clone();
        var recoveryDeclarations = new HashSet<int>(declarations);
        var caughtFailures = new FailureEdges();
        var recoveryScope = recovery.Finally is null ? null : new CleanupScope(recovery.Finally, [.. declarations]);
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
        var catchFailures = failures is not null || recovery.Finally is not null
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
                VerifyCleanup(recovery.Finally, exceptionalState, exceptionalMasks, [.. declarations],
                    function, program, ref remaining, depth + 1, reachable, inUnsafe, recoveryScope!.Visible);
                failures?.Add(exceptionalState, exceptionalMasks);
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
        }
        if (recovery.Finally is not null)
        {
            VerifyCleanup(recovery.Finally, initialized, fieldMasks, declarations, function,
                program, ref remaining, depth + 1,
                reachable && !(recoveryBodyReturns && (!caughtFailures.HasEdges || catchReturns)), inUnsafe,
                recoveryScope!.Visible);
        }
        return recoveryBodyReturns && (!caughtFailures.HasEdges || catchReturns);
    }

    private static void VerifyCleanup(BoundBlock cleanup, InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function, BoundProgram program, ref int remaining, int depth, bool reachable,
        bool inUnsafe, HashSet<int>? visible = null, bool? successfulReturn = null)
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
                }
            }
        }
        var outputTransfer = successfulReturn == true
            && cleanup.Statements is [BoundDrop { IsOutput: true }];
        var checkState = outputTransfer ? initialized.Clone() : initialized;
        var checkMasks = outputTransfer ? (uint[])fieldMasks.Clone() : fieldMasks;
        var doesNotComplete = Statement(cleanup, checkState, checkMasks, declarations, function with { IsNoThrow = true },
            program, ref remaining, depth, reachable, inCleanup: true, inUnsafe: inUnsafe);
        // Unreachable validation does not record loop exits. Termination is
        // still checked independently, including on return and exception paths.
        if ((reachable && doesNotComplete)
            || HasUnsupportedCleanupFlow(cleanup, function, program, ref remaining, depth, checkState.CanBeFacts))
        {
            throw InvalidEffect(cleanup.Span, "FinallyNoReturn");
        }
    }

    private sealed record CleanupScope(BoundBlock Body, HashSet<int> Visible);

    private sealed class LoopScope(int cleanupDepth)
    {
        internal int CleanupDepth { get; } = cleanupDepth;
        internal bool IsCleanupSwitch { get; init; }
        internal FailureEdges Breaks { get; } = new();
        internal FailureEdges Continues { get; } = new();
    }

    private static bool HasUnsupportedCleanupFlow(BoundStatement statement, BoundFunction function, BoundProgram program,
        ref int remaining, int depth, Dictionary<BoundExpression, byte> canBeFacts)
    {
        Count(ref remaining, depth);
        switch (statement)
        {
            case BoundBlock block:
                foreach (var child in block.Statements)
                {
                    if (HasUnsupportedCleanupFlow(child, function, program, ref remaining, depth + 1, canBeFacts))
                    {
                        return true;
                    }
                }
                return false;
            case BoundExpressionStatement { Expression: BoundCall call }:
                return program.Functions[call.Function].DoesNotReturn;
            case BoundIf conditional:
                return (CanBe(conditional.Condition, true, ref remaining, canBeFacts)
                    && HasUnsupportedCleanupFlow(conditional.Then, function, program, ref remaining, depth + 1, canBeFacts))
                    || (conditional.Else is not null
                        && CanBe(conditional.Condition, false, ref remaining, canBeFacts)
                        && HasUnsupportedCleanupFlow(conditional.Else, function, program, ref remaining, depth + 1, canBeFacts));
            case BoundWhile loop:
                if (loop.SwitchArmVariable >= 0)
                {
                    return ProveCleanupSwitch(loop, function, program, ref remaining, depth + 1, canBeFacts)
                        is not { CanComplete: true, Transfers.Count: 0 };
                }
                return (loop.IsPostTested || CanBe(loop.Condition, true, ref remaining, canBeFacts))
                    && loop is not {
                        Condition: BoundConstant { Type: PrimitiveType.Bool, Value: 1 },
                        Body: BoundBlock { Statements: [BoundBreak] },
                    };
            case BoundTryFinally nested:
                return HasUnsupportedCleanupFlow(nested.Body, function, program, ref remaining, depth + 1, canBeFacts)
                    || HasUnsupportedCleanupFlow(nested.Finally, function, program, ref remaining, depth + 1, canBeFacts);
            case BoundViewScope view:
                return HasUnsupportedCleanupFlow(view.Body, function, program, ref remaining, depth + 1, canBeFacts);
            case BoundUnsafe unsafeBlock:
                return HasUnsupportedCleanupFlow(unsafeBlock.Body, function, program, ref remaining, depth + 1, canBeFacts);
            default:
                return false;
        }
    }

    private sealed class InitializationState
    {
        // Joins retain never-published vacancy (1), liveness (2) and retired
        // vacancy (4). Retiring an immutable value cannot authorize another init
        // call, but an inline output may still retry before its first publication.
        private readonly byte[] _possibilities;
        private readonly Dictionary<BoundExpression, byte> _canBeFacts;
        private readonly Dictionary<BoundExpression, byte> _flowFacts;
        internal Dictionary<BoundStatement, InitializationState>? StatementInputs { get; init; }
        internal BackingDependencies? Dependencies { get; init; }

        internal InitializationState(int length)
            : this(length, new Dictionary<BoundExpression, byte>(ReferenceEqualityComparer.Instance),
                new Dictionary<BoundExpression, byte>(ReferenceEqualityComparer.Instance))
        {
        }

        private InitializationState(int length, Dictionary<BoundExpression, byte> canBeFacts,
            Dictionary<BoundExpression, byte> flowFacts)
        {
            _possibilities = new byte[length];
            _canBeFacts = canBeFacts;
            _flowFacts = flowFacts;
            Array.Fill(_possibilities, (byte)1);
        }

        internal int Length => _possibilities.Length;
        internal Dictionary<BoundExpression, byte> CanBeFacts => _canBeFacts;
        internal Dictionary<BoundExpression, byte> FlowFacts => _flowFacts;

        internal void Fill(bool value, int start, int count) =>
            Array.Fill(_possibilities, value ? (byte)2 : (byte)1, start, count);

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

        internal bool IsVacant(int index) => (_possibilities[index] & 2) == 0;

        internal bool HasRetiredValue(int index) => (_possibilities[index] & 4) != 0;

        internal void Retire(int index) => _possibilities[index] = 4;

        internal byte Possibilities(int index) => _possibilities[index];

        internal void SetPossibilities(int index, byte value) => _possibilities[index] = value;

        internal void MarkConditional(int index) => _possibilities[index] = 3;

        internal void Merge(int index, InitializationState other) =>
            _possibilities[index] |= other._possibilities[index];

        internal InitializationState Clone()
        {
            var result = new InitializationState(Length, _canBeFacts, _flowFacts) {
                StatementInputs = StatementInputs,
                Dependencies = Dependencies,
            };
            CopyTo(result);
            return result;
        }

        internal void CopyTo(InitializationState destination) =>
            Array.Copy(_possibilities, destination._possibilities, Length);

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
                StatementInputs.Add(statement, Clone());
            }
        }
    }

    private sealed class FailureEdges
    {
        private InitializationState? _state;
        private uint[]? _masks;

        internal bool HasEdges => _state is not null;

        internal InitializationState State => (_state ?? throw Invalid()).Clone();

        internal uint[] Masks => (uint[])(_masks ?? throw Invalid()).Clone();

        internal void Add(InitializationState state, uint[] masks)
        {
            if (_state is null)
            {
                _state = state.Clone();
                _masks = (uint[])masks.Clone();
                return;
            }
            for (var index = 0; index < state.Length; index++)
            {
                _state.Merge(index, state);
                _masks![index] &= masks[index];
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
        if (place is null || place.Receiver is null)
        {
            throw Invalid();
        }
        var owner = (int)place.Receiver.Type - 100;
        if (owner < 0 || owner >= program.Structures.Length
            || place.Field < 0 || place.Field >= program.Structures[owner].Fields.Length
            || !program.Structures[owner].IsUnique || !IsUnique(program, place.Type)
            || place.Type != program.Structures[owner].Fields[place.Field].Type
            || !CanAccessField(function, program.Structures[owner], place.Field))
        {
            throw InvalidEffect(place.Span, "Ownership");
        }
        if (place.Receiver is BoundFieldRead parent)
        {
            return VerifyUniqueFieldPlace(parent, initialized, fieldMasks, function, program,
                ref remaining, depth + 1);
        }
        if (place.Receiver is not BoundRead root || root.Variable < 0
            || root.Variable >= initialized.Length
            || root.Type != function.Variables[root.Variable].Type
            || !IsUnique(program, root.Type)
            || (!initialized[root.Variable]
                && !(function.IsConstructor && root.Variable == 0
                    && (fieldMasks[0] & (1u << place.Field)) != 0)))
        {
            throw InvalidEffect(place.Span, "Ownership");
        }
        return root.Variable;
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
            || (program.Structures[owner].Fields[place.Field].RequiresUnsafeCaller
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

    private static void Expression(BoundExpression expression, InitializationState initialized, uint[] fieldMasks,
        HashSet<int> declarations, BoundFunction function,
        BoundProgram program, ref int remaining, int depth, FailureEdges? failures = null,
        bool statementCompound = false, BoundBinary? cleanupCounterStep = null)
    {
        Count(ref remaining, depth);
        if (expression is null || !ValidType(expression.Type,
            allowVoid: expression is BoundCall or BoundIndirectCall
                or BoundUnsafeExpression { Type: PrimitiveType.Void }
                or BoundNativeFixedArrayWrite or BoundNativePointerWrite, program))
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
                else if (constant.Type is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64
                    or PrimitiveType.UInt64 or PrimitiveType.Float32 or PrimitiveType.Float64
                    or PrimitiveType.Char16 or PrimitiveType.Bool)
                    || (constant.Type is PrimitiveType.Int32 or PrimitiveType.UInt32
                        && constant.Value is < int.MinValue or > int.MaxValue)
                    || (constant.Type == PrimitiveType.Bool && constant.Value is not (0 or 1))
                    || (constant.Type == PrimitiveType.Char16 && constant.Value is < 0 or > ushort.MaxValue)
                    || (constant.Type == PrimitiveType.Float32 && constant.Value is < 0 or > uint.MaxValue))
                {
                    throw Invalid();
                }
                break;
            case BoundNativeFunctionAddress address:
                if (!address.IsUnsafeAuthorized
                    || (uint)address.Function >= (uint)program.Functions.Length
                    || !NativeFunctionPointerContract.MatchesFunction(
                        program.Functions[address.Function], address.Type,
                        program.NativeFunctionPointerSignatures))
                {
                    throw InvalidEffect(expression.Span, "NativeFunctionPointer");
                }
                break;
            case BoundNativePointerAddress address:
                if (!address.IsUnsafeAuthorized
                    || !NativePointerTypeContract.IsValidType(address.Type, program.NativePointerTypes)
                    || (uint)address.Variable >= (uint)function.Variables.Length)
                {
                    throw InvalidEffect(address.Span, "NativePointer");
                }
                var pointerDefinition = program.NativePointerTypes[NativePointerTypes.Index(address.Type)];
                var addressedVariable = function.Variables[address.Variable];
                if (pointerDefinition.PointeeType != addressedVariable.Type
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
                    || !DirectNativeImportContract.IsInputPointee(pointerRead.Type, program.Structures))
                {
                    throw InvalidEffect(pointerRead.Span, "NativePointer");
                }
                Expression(pointerRead.Address, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
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
                    || !DirectNativeImportContract.IsInputPointee(pointerWrite.Value.Type, program.Structures))
                {
                    throw InvalidEffect(pointerWrite.Span, "NativePointer");
                }
                Expression(pointerWrite.Address, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                Expression(pointerWrite.Value, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                break;
            case BoundNativeFixedArrayInitialization:
                throw InvalidEffect(expression.Span, "NativeImport");
            case BoundNativeStorageAddress:
            case BoundRead read when NativeFixedArrayContract.IsCarrier(read.Type):
                throw InvalidEffect(expression.Span, "NativeImport");
            case BoundNativeFixedArrayElement fixedArrayElement:
                VerifyNativeFixedArrayElement(fixedArrayElement, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures);
                break;
            case BoundNativeFixedArrayWrite fixedArrayWrite:
                VerifyNativeFixedArrayWrite(fixedArrayWrite, initialized, fieldMasks, declarations,
                    function, program, ref remaining, depth + 1, failures);
                break;
            case BoundNull:
                if (!ManagedClassTypes.IsOptional(expression.Type))
                {
                    throw Invalid();
                }
                break;
            case BoundUnsafeExpression unsafeExpression:
                Expression(unsafeExpression.Expression, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures, statementCompound, cleanupCounterStep);
                break;
            case BoundOptionalLift lift:
                if (!ManagedClassTypes.IsOptional(lift.Type)
                    || lift.Value is null || lift.Value.Type != ManagedClassTypes.Required(lift.Type))
                {
                    throw Invalid();
                }
                Expression(lift.Value, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                break;
            case BoundCoalesce coalesce:
                if (coalesce.Optional is null || coalesce.Fallback is null
                    || !ManagedClassTypes.IsOptional(coalesce.Optional.Type)
                    || !ManagedClassTypes.IsManaged(coalesce.Type)
                    || (coalesce.Type != ManagedClassTypes.Required(coalesce.Optional.Type)
                        && !(ManagedClassTypes.IsWritable(ManagedClassTypes.Required(coalesce.Optional.Type))
                            && coalesce.Type == ManagedClassTypes.Readonly(
                                ManagedClassTypes.Required(coalesce.Optional.Type)))
                        && !(ManagedClassTypes.IsOptional(coalesce.Type)
                            && (coalesce.Type == coalesce.Optional.Type
                                || (ManagedClassTypes.IsWritable(coalesce.Optional.Type)
                                    && coalesce.Type == ManagedClassTypes.Readonly(coalesce.Optional.Type)))))
                    || coalesce.Fallback.Type != coalesce.Type)
                {
                    throw Invalid();
                }
                Expression(coalesce.Optional, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                var absentState = initialized.Clone();
                var absentMasks = (uint[])fieldMasks.Clone();
                var absentDeclarations = new HashSet<int>(declarations);
                Expression(coalesce.Fallback, absentState, absentMasks, absentDeclarations, function, program,
                    ref remaining, depth + 1, coalesce.Optional is BoundOptionalLift ? null : failures);
                declarations.UnionWith(absentDeclarations);
                if (coalesce.Optional is BoundNull)
                {
                    absentState.CopyTo(initialized);
                    Array.Copy(absentMasks, fieldMasks, fieldMasks.Length);
                }
                else if (coalesce.Optional is not BoundOptionalLift)
                {
                    for (var i = 0; i < initialized.Length; i++)
                    {
                        initialized.Merge(i, absentState);
                        fieldMasks[i] &= absentMasks[i];
                    }
                }
                break;
            case BoundReadonlyProjection projection:
                if (projection.Value is null || !ManagedClassTypes.IsWritable(projection.Value.Type)
                    || projection.Type != ManagedClassTypes.Readonly(projection.Value.Type))
                {
                    throw Invalid();
                }
                Expression(projection.Value, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                break;
            case BoundIsNull test:
                if (test.Value is null || !ManagedClassTypes.IsOptional(test.Value.Type))
                {
                    throw Invalid();
                }
                Expression(test.Value, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                break;
            case BoundIsInitialized initializationTest:
                if (initializationTest.Type != PrimitiveType.Bool
                    || initializationTest.Variable < 0 || initializationTest.Variable >= initialized.Length
                    || (initializationTest.Variable >= function.ParameterCount
                        + (function.IsConstructor ? 1 : 0) && !declarations.Contains(initializationTest.Variable)))
                {
                    throw Invalid(initializationTest.Span);
                }
                break;
            case BoundIsSome pattern:
                if (pattern.Value is null || !ManagedClassTypes.IsOptional(pattern.Value.Type)
                    || pattern.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    || pattern.Variable < 0 || pattern.Variable >= initialized.Length
                    || function.Variables[pattern.Variable].Type != ManagedClassTypes.Required(pattern.Value.Type)
                    || function.Variables[pattern.Variable].IsOut
                    || function.Variables[pattern.Variable].IsMutable
                    || declarations.Contains(pattern.Variable))
                {
                    throw Invalid();
                }
                Expression(pattern.Value, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                _ = declarations.Add(pattern.Variable);
                break;
            case BoundIsVariablePattern pattern:
                if (pattern.Value is null || !ManagedClassTypes.IsOptional(pattern.Value.Type)
                    || (!pattern.IncludeNull && pattern.Value is not BoundRead)
                    || pattern.Variable < function.ParameterCount + (function.IsConstructor ? 1 : 0)
                    || pattern.Variable < 0 || pattern.Variable >= initialized.Length
                    || function.Variables[pattern.Variable].Type != (pattern.IncludeNull
                        ? pattern.Value.Type : ManagedClassTypes.Required(pattern.Value.Type))
                    || function.Variables[pattern.Variable].IsOut
                    || function.Variables[pattern.Variable].IsMutable
                    || function.Variables[pattern.Variable].IsInitializationLocal
                    || declarations.Contains(pattern.Variable))
                {
                    throw Invalid(pattern.Span);
                }
                if (pattern.Value is BoundRead sourceRead)
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
                    Expression(pattern.Value, initialized, fieldMasks, declarations, function, program,
                        ref remaining, depth + 1, failures);
                }
                _ = declarations.Add(pattern.Variable);
                break;
            case BoundCast cast:
                Expression(cast.Operand, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                var integerConversion = (cast.Operand.Type is PrimitiveType.Int32 or PrimitiveType.UInt32
                    or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Char16)
                    && (cast.Type is PrimitiveType.Int32 or PrimitiveType.UInt32
                        or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Char16);
                var integerToFloatingConversion = (cast.Operand.Type is PrimitiveType.Int32 or PrimitiveType.UInt32
                    or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Char16)
                    && (cast.Type is PrimitiveType.Float32 or PrimitiveType.Float64);
                var floatingToIntegerConversion = (cast.Operand.Type is PrimitiveType.Float32 or PrimitiveType.Float64)
                    && (cast.Type is PrimitiveType.Int32 or PrimitiveType.UInt32
                        or PrimitiveType.Int64 or PrimitiveType.UInt64);
                var floatingConversion = (cast.Operand.Type is PrimitiveType.Float32 or PrimitiveType.Float64)
                    && (cast.Type is PrimitiveType.Float32 or PrimitiveType.Float64);
                if (!integerConversion && !integerToFloatingConversion
                    && !floatingToIntegerConversion && !floatingConversion)
                {
                    throw Invalid();
                }
                if (function.IsNoThrow && !IsTotalConversion(cast.Operand.Type, cast.Type))
                {
                    throw InvalidEffect(cast.Span);
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
                if (Fields(program, readType)[fieldRead.Field].RequiresUnsafeCaller
                    && !fieldRead.IsUnsafeAuthorized)
                {
                    throw InvalidEffect(fieldRead.Span, "UnsafeCaller");
                }
                if (IsUnique(program, readType) && fieldRead.Receiver is not BoundRead)
                {
                    if (fieldRead.Receiver is not BoundFieldRead candidate || !IsUnique(program, candidate.Type))
                    {
                        throw InvalidEffect(fieldRead.Span, "Ownership");
                    }
                }
                if (fieldRead.Receiver is BoundRead owned && IsUnique(program, owned.Type))
                {
                    if (owned.Variable < 0 || owned.Variable >= initialized.Length
                        || owned.Type != function.Variables[owned.Variable].Type
                        || (!initialized[owned.Variable]
                            && (!function.IsConstructor || owned.Variable != 0
                                || (fieldMasks[0] & (1u << fieldRead.Field)) == 0)))
                    {
                        throw InvalidEffect(fieldRead.Span, "Ownership");
                    }
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
                else
                {
                    Expression(fieldRead.Receiver, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
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
                    || program.Buffers[snapshot.SourceBuffer].IsWritable || function.IsNoThrow)
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
                VerifySpanOperand(length.View, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth, failures);
                break;
            case BoundSpanRead spanRead:
                VerifySpanOperand(spanRead.View, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth, failures);
                if (spanRead.Index is null || spanRead.Index.Type != TokenSpanContract.IndexCarrier
                    || !TokenSpanContract.ValidTokenLayout(program.Structures)
                    || spanRead.Type != (PrimitiveType)(100 + TokenSpanContract.Find(program.Structures, TokenSpanContract.TokenName))
                    || function.IsNoThrow)
                {
                    throw InvalidEffect(spanRead.Span, "Ownership");
                }
                var readBacking = BackingOf(spanRead.View, initialized.Dependencies, program, ref remaining, depth + 1);
                initialized.Dependencies?.Staged.Add(readBacking);
                try
                {
                    Expression(spanRead.Index, initialized, fieldMasks, declarations, function, program,
                        ref remaining, depth + 1, failures);
                }
                finally
                {
                    if (initialized.Dependencies is BackingDependencies dependencies)
                    {
                        dependencies.Staged.RemoveAt(dependencies.Staged.Count - 1);
                    }
                }
                failures?.Add(initialized, fieldMasks);
                break;
            case BoundSpanAdvance advance:
                if (advance.Type != PrimitiveType.TokenSpan
                    || function.DeclaringStruct != TokenSpanContract.CursorName || function.IsConstructor
                    || function.IsStatic || !function.Variables[0].IsMutable || function.IsNoThrow
                    || advance.View is not BoundFieldRead { Receiver: BoundRead { Variable: 0 }, Field: 0 })
                {
                    throw InvalidEffect(advance.Span, "Ownership");
                }
                VerifySpanOperand(advance.View, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth, failures);
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
                Expression(viewRead.Index, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                if (viewRead.Index.Type != PrimitiveType.Int32)
                {
                    throw Invalid();
                }
                if (function.IsNoThrow)
                {
                    throw InvalidEffect(viewRead.Span);
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
                if (function.IsNoThrow && !updateTarget.IsNoThrow)
                {
                    throw InvalidEffect(update.Span);
                }
                VerifyUpdatePlace(update.Place, function, program, ref remaining, depth + 1);
                Expression(update.Place, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                if (!updateTarget.IsNoThrow)
                {
                    failures?.Add(initialized, fieldMasks);
                }
                break;
            case BoundMove move:
                CheckBackingInvalidation(move.Variable, initialized, move.Span);
                if (!IsUnique(program, move.Type) || move.Variable < 0 || move.Variable >= initialized.Length
                    || (move.Variable == 0 && function.DeclaringStruct is not null && !function.IsStatic)
                    || !initialized[move.Variable] || move.Type != function.Variables[move.Variable].Type
                    || (move.Variable >= function.ParameterCount + (function.IsConstructor ? 1 : 0)
                        && !declarations.Contains(move.Variable)))
                {
                    throw InvalidEffect(move.Span, "Ownership");
                }
                initialized.Retire(move.Variable);
                fieldMasks[move.Variable] = 0;
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
                if (function.IsNoThrow && !IsProvenBufferIndex(bufferRead.Buffer, bufferRead.Index, program))
                {
                    throw InvalidEffect(bufferRead.Span);
                }
                if (bufferRead.Buffer < 0 || bufferRead.Buffer >= program.Buffers.Length
                    || bufferRead.Type != program.Buffers[bufferRead.Buffer].ElementType)
                {
                    throw Invalid();
                }
                Expression(bufferRead.Index, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                failures?.Add(initialized, fieldMasks);
                if (bufferRead.Index.Type != PrimitiveType.Int32)
                {
                    throw Invalid();
                }
                break;
            case BoundUnary unary:
                Expression(unary.Operand, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                var unaryValid = unary.Operator switch {
                    TokenKind.Plus => unary.Operand.Type is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64
                        or PrimitiveType.UInt64 or PrimitiveType.Float32 or PrimitiveType.Float64 && unary.Type == unary.Operand.Type,
                    TokenKind.Tilde => unary.Operand.Type is PrimitiveType.Int32 or PrimitiveType.UInt32
                        or PrimitiveType.Int64 or PrimitiveType.UInt64 && unary.Type == unary.Operand.Type,
                    TokenKind.Minus => unary.Operand.Type is PrimitiveType.Int32 or PrimitiveType.Int64 or PrimitiveType.Float32
                        or PrimitiveType.Float64 && unary.Type == unary.Operand.Type,
                    TokenKind.Bang => unary.Operand.Type == PrimitiveType.Bool && unary.Type == PrimitiveType.Bool,
                    _ => false,
                };
                if (!unaryValid)
                {
                    throw Invalid();
                }
                if (function.IsNoThrow && unary.Operator == TokenKind.Minus
                    && unary.Type is PrimitiveType.Int32 or PrimitiveType.Int64
                    && (unary.Operand is not BoundConstant operand
                        || operand.Value == (unary.Type == PrimitiveType.Int32 ? int.MinValue : long.MinValue)))
                {
                    throw InvalidEffect(unary.Span);
                }
                if (unary.Operator == TokenKind.Minus && unary.Type is PrimitiveType.Int32 or PrimitiveType.Int64)
                {
                    failures?.Add(initialized, fieldMasks);
                }
                break;
            case BoundUpdate update:
                if (update.Place is null || update.Operator is not (TokenKind.PlusPlus or TokenKind.MinusMinus)
                    || update.Type is not (PrimitiveType.Int32 or PrimitiveType.UInt32
                        or PrimitiveType.Int64 or PrimitiveType.UInt64)
                    || update.Place.Type != update.Type || function.IsNoThrow
                    || (update.Place is BoundRead { Variable: 0 } && function.DeclaringStruct is not null
                        && !function.IsStatic))
                {
                    throw InvalidEffect(update.Span);
                }
                VerifyUpdatePlace(update.Place, function, program, ref remaining, depth + 1);
                Expression(update.Place, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                failures?.Add(initialized, fieldMasks);
                break;
            case BoundCompoundAssignment compound:
                if (!statementCompound || compound.Place is null || compound.Right is null
                    || !CompoundOperators.IsSupported(compound.Operator)
                    || compound.Type is not (PrimitiveType.Int32 or PrimitiveType.UInt32
                        or PrimitiveType.Int64 or PrimitiveType.UInt64)
                    || compound.Place.Type != compound.Type
                    || compound.Right.Type != (CompoundOperators.IsShift(compound.Operator)
                        ? PrimitiveType.Int32 : compound.Type)
                    || (compound.Place is BoundRead { Variable: 0 } && function.DeclaringStruct is not null
                        && !function.IsStatic))
                {
                    throw InvalidEffect(compound.Span);
                }
                VerifyUpdatePlace(compound.Place, function, program, ref remaining, depth + 1);
                Expression(compound.Place, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                var root = UpdateRoot(compound.Place, ref remaining, depth + 1);
                if (root >= 0 && IsUnique(program, function.Variables[root].Type)
                    && InvalidatesReservedOwner(compound.Right, root, ref remaining, depth + 1))
                {
                    throw InvalidEffect(compound.Right.Span, "Ownership");
                }
                Expression(compound.Right, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                if (root >= 0 && !initialized[root])
                {
                    throw InvalidEffect(compound.Right.Span, "Ownership");
                }
                if (CompoundOperators.Binary(compound.Operator) is not (TokenKind.Ampersand
                    or TokenKind.Pipe or TokenKind.Caret))
                {
                    if (function.IsNoThrow)
                    {
                        throw InvalidEffect(compound.Span);
                    }
                    failures?.Add(initialized, fieldMasks);
                }
                break;
            case BoundBinary binary:
                var beforeLeft = initialized.Clone();
                Expression(binary.Left, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                if (binary.Operator is TokenKind.AmpersandAmpersand or TokenKind.PipePipe)
                {
                    var evaluateWhenTrue = binary.Operator == TokenKind.AmpersandAmpersand;
                    var rightState = Outcome(binary.Left, beforeLeft, evaluateWhenTrue, ref remaining);
                    var rightMasks = (uint[])fieldMasks.Clone();
                    MarkOutMasks(rightState, rightMasks, function, program);
                    var evaluates = CanBe(binary.Left, evaluateWhenTrue, ref remaining, initialized);
                    Expression(binary.Right, rightState, rightMasks, declarations, function, program, ref remaining, depth + 1,
                        evaluates ? failures : null);
                    var skippedState = Outcome(binary.Left, beforeLeft, !evaluateWhenTrue, ref remaining);
                    var skippedMasks = (uint[])fieldMasks.Clone();
                    MarkOutMasks(skippedState, skippedMasks, function, program);
                    var skips = CanBe(binary.Left, !evaluateWhenTrue, ref remaining, initialized);
                    for (var i = 0; i < initialized.Length; i++)
                    {
                        initialized.SetPossibilities(i, !evaluates ? skippedState.Possibilities(i)
                            : !skips ? rightState.Possibilities(i)
                            : (byte)(rightState.Possibilities(i) | skippedState.Possibilities(i)));
                        fieldMasks[i] = !evaluates ? skippedMasks[i] : !skips ? rightMasks[i]
                            : rightMasks[i] & skippedMasks[i];
                    }
                }
                else
                {
                    Expression(binary.Right, initialized, fieldMasks, declarations, function, program, ref remaining, depth + 1, failures);
                }
                var operands = binary.Left.Type == binary.Right.Type && binary.Left.Type != PrimitiveType.Void;
                var integerOperands = binary.Left.Type is PrimitiveType.Int32 or PrimitiveType.UInt32
                    or PrimitiveType.Int64 or PrimitiveType.UInt64;
                var floatingOperands = binary.Left.Type is PrimitiveType.Float32 or PrimitiveType.Float64;
                var floatingArithmetic = floatingOperands && (binary.Operator is TokenKind.Plus or TokenKind.Minus
                    or TokenKind.Star or TokenKind.Slash);
                var integerArithmetic = integerOperands && (binary.Operator is TokenKind.Plus or TokenKind.Minus
                    or TokenKind.Star or TokenKind.Slash or TokenKind.Percent);
                var checkedIntegerOperation = integerArithmetic || (integerOperands
                    && (binary.Operator is TokenKind.ShiftLeft or TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned));
                var binaryValid = binary.Operator switch {
                    TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash or TokenKind.Percent =>
                        operands && (integerArithmetic || floatingArithmetic)
                            && binary.Type == binary.Left.Type,
                    TokenKind.ShiftLeft or TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned =>
                        integerOperands
                            && binary.Right.Type == PrimitiveType.Int32 && binary.Type == binary.Left.Type,
                    TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater or TokenKind.GreaterEquals =>
                        operands && (integerOperands || binary.Left.Type == PrimitiveType.Char16 || floatingOperands)
                            && binary.Type == PrimitiveType.Bool,
                    TokenKind.EqualsEquals or TokenKind.BangEquals =>
                        operands && (integerOperands || binary.Left.Type is PrimitiveType.Char16 or PrimitiveType.Bool
                            || floatingOperands || binary.Left.Type == PrimitiveType.NativeRawPointer)
                            && binary.Type == PrimitiveType.Bool,
                    TokenKind.Ampersand or TokenKind.Pipe or TokenKind.Caret =>
                        operands && binary.Left.Type is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Bool
                            && binary.Type == binary.Left.Type,
                    TokenKind.AmpersandAmpersand or TokenKind.PipePipe =>
                        operands && binary.Left.Type == PrimitiveType.Bool && binary.Type == PrimitiveType.Bool,
                    _ => false,
                };
                if (!binaryValid)
                {
                    throw Invalid();
                }
                var verifiedNoThrowArithmetic = function.IsNoThrow && checkedIntegerOperation
                    && ReferenceEquals(binary, cleanupCounterStep);
                if (function.IsNoThrow && checkedIntegerOperation && !verifiedNoThrowArithmetic)
                {
                    throw InvalidEffect(binary.Span);
                }
                if (checkedIntegerOperation && !verifiedNoThrowArithmetic)
                {
                    failures?.Add(initialized, fieldMasks);
                }
                break;
            case BoundConditional conditional:
                if (conditional.Condition is null || conditional.WhenTrue is null || conditional.WhenFalse is null
                    || conditional.Condition.Type != PrimitiveType.Bool
                    || conditional.WhenTrue.Type != conditional.Type || conditional.WhenFalse.Type != conditional.Type
                    || conditional.Type == PrimitiveType.Void)
                {
                    throw Invalid();
                }
                var beforeCondition = initialized.Clone();
                Expression(conditional.Condition, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                var trueFeasible = CanBe(conditional.Condition, true, ref remaining, initialized);
                var falseFeasible = CanBe(conditional.Condition, false, ref remaining, initialized);
                var trueState = Outcome(conditional.Condition, beforeCondition, true, ref remaining);
                var falseState = Outcome(conditional.Condition, beforeCondition, false, ref remaining);
                var trueMasks = (uint[])fieldMasks.Clone();
                var falseMasks = (uint[])fieldMasks.Clone();
                MarkOutMasks(trueState, trueMasks, function, program);
                MarkOutMasks(falseState, falseMasks, function, program);
                var trueDeclarations = new HashSet<int>(declarations);
                var falseDeclarations = new HashSet<int>(declarations);
                Expression(conditional.WhenTrue, trueState, trueMasks, trueDeclarations, function, program,
                    ref remaining, depth + 1, trueFeasible ? failures : null);
                Expression(conditional.WhenFalse, falseState, falseMasks, falseDeclarations, function, program,
                    ref remaining, depth + 1, falseFeasible ? failures : null);
                declarations.UnionWith(trueDeclarations);
                declarations.UnionWith(falseDeclarations);
                for (var i = 0; i < initialized.Length; i++)
                {
                    initialized.SetPossibilities(i, !trueFeasible ? falseState.Possibilities(i)
                        : !falseFeasible ? trueState.Possibilities(i)
                        : (byte)(trueState.Possibilities(i) | falseState.Possibilities(i)));
                    fieldMasks[i] = !trueFeasible ? falseMasks[i] : !falseFeasible ? trueMasks[i]
                        : trueMasks[i] & falseMasks[i];
                }
                break;
            case BoundIndirectCall call:
                VerifyIndirectCall(call, function, program);
                Expression(call.Target, initialized, fieldMasks, declarations, function, program,
                    ref remaining, depth + 1, failures);
                foreach (var argument in call.Arguments)
                {
                    Expression(argument, initialized, fieldMasks, declarations, function, program,
                        ref remaining, depth + 1, failures);
                }
                break;
            case BoundCall call:
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
                if (target.IsCleanup || target.IsClassConstructor)
                {
                    throw InvalidEffect(call.Span, "Ownership");
                }
                if (target.RequiresUnsafeCaller && !call.IsUnsafeAuthorized)
                {
                    throw InvalidEffect(call.Span, "UnsafeCaller");
                }
                if (function.IsNoThrow && !target.IsNoThrow)
                {
                    throw InvalidEffect(call.Span);
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
                try
                {
                    for (var i = 0; i < call.Arguments.Length; i++)
                    {
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
                        var directNativeOutput = DirectNativeImportContract.OutputForParameter(
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
                        var isNativeVTableParameterAddress = argument is BoundNativeStorageAddress
                            nativeVTableParameterAddress
                            && nativeVTableParameter is not null
                            && DirectNativeImportContract.MatchesAddress(
                                nativeVTableParameterAddress, nativeVTableParameter, function, program);
                        var implicitReferenceTemporary = call.ImplicitReferenceTemporary(i);
                        if (implicitReferenceTemporary < -1)
                        {
                            throw Invalid();
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
                        }
                        else
                        {
                            var isAllowedStorageAddress = argument is BoundNativeStorageAddress storageAddress
                                && directNativeOutput is not null
                                && DirectNativeImportContract.MatchesAddress(
                                    storageAddress, directNativeOutput, function, program);
                            isAllowedStorageAddress |= argument is BoundNativeStorageAddress matchedDirectParameterAddress
                                && directNativeParameter is not null
                                && DirectNativeImportContract.MatchesAddress(
                                    matchedDirectParameterAddress, directNativeParameter, function, program);
                            isAllowedStorageAddress |= argument is BoundNativeStorageAddress nativeVTableAddress
                                && nativeVTableParameter is not null
                                && DirectNativeImportContract.MatchesAddress(
                                    nativeVTableAddress, nativeVTableParameter, function, program);
                            if (directNativeOutput is not null)
                            {
                                if (argument is not BoundNativeStorageAddress outputAddress
                                    || !DirectNativeImportContract.MatchesAddress(
                                        outputAddress, directNativeOutput, function, program)
                                    || !directNativeOutputTargets.Add(outputAddress.Variable))
                                {
                                    throw InvalidEffect(argument.Span, "NativeImport");
                                }
                                VerifyDirectNativeOutputAddress(outputAddress, directNativeOutput,
                                    initialized, fieldMasks, declarations, function, program,
                                    ref remaining, depth + 1, failures);
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
                                else if (argument.Type != PrimitiveType.NativeRawPointer
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
                                else if (argument.Type != PrimitiveType.NativeRawPointer
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
                                if (directNativeParameter is null
                                    || !isDirectNativeStringPointerArgument)
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
                                if (argument is not BoundNativeStorageAddress)
                                {
                                    if (IsUnique(program, argument.Type)
                                        && argument is not (BoundMove or BoundCall))
                                    {
                                        throw InvalidEffect(argument.Span, "Ownership");
                                    }
                                    Expression(argument, initialized, fieldMasks, declarations, function, program,
                                        ref remaining, depth + 1, failures);
                                }
                            }
                        }
                        if (loanRoot >= 0 && parameterIndex == 0)
                        {
                            reservedReceiver = loanRoot;
                        }
                        if (argument.Type != target.Variables[parameterSlot].Type
                            && !isDirectNativeParameterAddress
                            && !isDirectNativeStringPointerArgument
                            && !isNativeVTableParameterAddress
                            && !isNativeVTableStringPointerArgument)
                        {
                            throw Invalid();
                        }
                        if ((argument is BoundOutArgument)
                            != target.Variables[parameterSlot].IsOut
                            && directNativeOutput is null)
                        {
                            throw Invalid();
                        }
                        if (implicitReferenceTemporary >= 0)
                        {
                            _ = declarations.Add(implicitReferenceTemporary);
                            initialized[implicitReferenceTemporary] = true;
                            fieldMasks[implicitReferenceTemporary] = 0;
                        }
                        if (initialized.Dependencies is BackingDependencies dependencies
                            && TokenSpanContract.IsDependent(argument.Type, program.Structures))
                        {
                            dependencies.Staged.Add(BackingOf(argument, dependencies, program, ref remaining, depth + 1));
                        }
                    }
                    if (loanRoot >= 0 && !initialized[loanRoot]
                        && !(loanOfInitializedField && function.IsConstructor && loanRoot == 0))
                    {
                        throw InvalidEffect(call.Span, "Ownership");
                    }
                    if (!target.IsNoThrow)
                    {
                        failures?.Add(initialized, fieldMasks);
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
                        }
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
        type is PrimitiveType.Bool or PrimitiveType.Char16 or PrimitiveType.Int32 or PrimitiveType.UInt32
            or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Float32 or PrimitiveType.Float64;

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
        if (call.Type != signature.ReturnType || call.Arguments.Length != signature.ParameterTypes.Length)
        {
            throw Invalid();
        }
        for (var index = 0; index < call.Arguments.Length; index++)
        {
            var argument = call.Arguments[index];
            if (argument is null || argument.Type != signature.ParameterTypes[index]
                || IsUnique(program, argument.Type))
            {
                throw Invalid();
            }
        }
    }

    private static bool ValidType(PrimitiveType type, bool allowVoid, BoundProgram program) =>
        type is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64
            or PrimitiveType.Float32 or PrimitiveType.Float64
            or PrimitiveType.Char16 or PrimitiveType.Bool or PrimitiveType.OwnedInt32Buffer or PrimitiveType.TokenSpan
            or PrimitiveType.ManagedNode or PrimitiveType.OptionalManagedNode or PrimitiveType.NativeUtf16ZPointer
            or PrimitiveType.NativeUtf8ZPointer
            or PrimitiveType.NativeStorageAddress
            or PrimitiveType.NativeRawPointer
            || NativeFixedArrayContract.IsCarrier(type)
            || (allowVoid && type == PrimitiveType.Void)
            || ((int)type >= 100 && (int)type - 100 < program.Structures.Length)
            || NativePointerTypeContract.IsValidType(type, program.NativePointerTypes)
            || NativeFunctionPointerContract.IsValidType(type, program.NativeFunctionPointerSignatures)
            || (ManagedClassTypes.IsManaged(type) && (ManagedClassTypes.Readonly(type) is
                PrimitiveType.ManagedNode or PrimitiveType.OptionalManagedNode))
            || ((ManagedClassTypes.IsClass(type) || (ManagedClassTypes.IsOptional(type)
                && type is not (PrimitiveType.OptionalManagedNode or PrimitiveType.OptionalWriteManagedNode)))
                && ManagedClassTypes.ClassIndex(type) < program.Classes.Length
                && (!ManagedClassTypes.IsWritable(type)
                    || program.Classes[ManagedClassTypes.ClassIndex(type)].IsMutable));

    internal static bool IsClassArgumentType(PrimitiveType type, IReadOnlyList<BoundStruct> structures) =>
        type is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64
            or PrimitiveType.UInt64 or PrimitiveType.Float32 or PrimitiveType.Float64
            or PrimitiveType.Char16 or PrimitiveType.Bool
            || NativePointerTypes.IsType(type)
            || NativeFunctionPointerTypes.IsType(type)
            || ManagedClassTypes.IsManaged(type)
            || ((int)type >= 100 && (int)type - 100 < structures.Count
                && !structures[(int)type - 100].IsUnique);

    private static bool IsUnique(BoundProgram program, PrimitiveType type) =>
        type == PrimitiveType.OwnedInt32Buffer
            || ((int)type >= 100 && (int)type - 100 < program.Structures.Length
                && program.Structures[(int)type - 100].IsUnique);

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
            || function.IsNoThrow != constructor.IsNoThrow
            || function.RequiresUnsafeCaller != constructor.RequiresUnsafeCaller)
        {
            throw Invalid();
        }
        for (var parameter = 0; parameter < function.ParameterCount - 1; parameter++)
        {
            if (function.Variables[parameter].Type != constructor.Variables[parameter + 1].Type
                || function.Variables[parameter].IsOut || function.Variables[parameter].IsMutable
                || constructor.Variables[parameter + 1].IsOut
                || !IsClassArgumentType(function.Variables[parameter].Type, program.Structures))
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

    private static bool IsTotalConversion(PrimitiveType source, PrimitiveType destination) =>
        source == destination || source == PrimitiveType.Char16
            || (source == PrimitiveType.Int32 && destination == PrimitiveType.Int64)
            || (source == PrimitiveType.UInt32 && destination is PrimitiveType.Int64 or PrimitiveType.UInt64)
            || ((source is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64)
                && (destination is PrimitiveType.Float32 or PrimitiveType.Float64))
            || ((source is PrimitiveType.Float32 or PrimitiveType.Float64)
                && (destination is PrimitiveType.Float32 or PrimitiveType.Float64));

    private static uint FullMask(BoundProgram program, PrimitiveType type)
    {
        var count = Fields(program, type).Length;
        return count == 32 ? uint.MaxValue : (1u << count) - 1;
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
            }
        }
    }

    private static InitializationState Outcome(BoundExpression expression, InitializationState initialized, bool result, ref int remaining)
    {
        if (!HasOutcomeEffects(expression, initialized.FlowFacts, ref remaining))
        {
            return initialized.Clone();
        }
        Count(ref remaining, 0);
        if (expression is BoundUnary { Operator: TokenKind.Bang } unary)
        {
            return Outcome(unary.Operand, initialized, !result, ref remaining);
        }
        if (expression is BoundUnsafeExpression unsafeExpression)
        {
            return Outcome(unsafeExpression.Expression, initialized, result, ref remaining);
        }
        if (expression is BoundBinary { Operator: TokenKind.AmpersandAmpersand } and)
        {
            if (result)
            {
                var leftTrue = Outcome(and.Left, initialized, true, ref remaining);
                return Outcome(and.Right, leftTrue, true, ref remaining);
            }
            var firstFalsePossible = CanBe(and.Left, false, ref remaining, initialized);
            var rightFalsePossible = CanBe(and.Left, true, ref remaining, initialized)
                && CanBe(and.Right, false, ref remaining, initialized);
            if (firstFalsePossible && !rightFalsePossible)
            {
                return Outcome(and.Left, initialized, false, ref remaining);
            }
            if (!firstFalsePossible && rightFalsePossible)
            {
                var leftTrue = Outcome(and.Left, initialized, true, ref remaining);
                return Outcome(and.Right, leftTrue, false, ref remaining);
            }
            var leftTrueState = Outcome(and.Left, initialized, true, ref remaining);
            var firstFalse = Outcome(and.Left, initialized, false, ref remaining);
            var rightFalse = Outcome(and.Right, leftTrueState, false, ref remaining);
            return Combine(firstFalse, firstFalsePossible, rightFalse, rightFalsePossible);
        }
        if (expression is BoundBinary { Operator: TokenKind.PipePipe } or)
        {
            if (!result)
            {
                var leftFalse = Outcome(or.Left, initialized, false, ref remaining);
                return Outcome(or.Right, leftFalse, false, ref remaining);
            }
            var firstTruePossible = CanBe(or.Left, true, ref remaining, initialized);
            var rightTruePossible = CanBe(or.Left, false, ref remaining, initialized)
                && CanBe(or.Right, true, ref remaining, initialized);
            if (firstTruePossible && !rightTruePossible)
            {
                return Outcome(or.Left, initialized, true, ref remaining);
            }
            if (!firstTruePossible && rightTruePossible)
            {
                var leftFalse = Outcome(or.Left, initialized, false, ref remaining);
                return Outcome(or.Right, leftFalse, true, ref remaining);
            }
            var leftFalseState = Outcome(or.Left, initialized, false, ref remaining);
            var firstTrue = Outcome(or.Left, initialized, true, ref remaining);
            var rightTrue = Outcome(or.Right, leftFalseState, true, ref remaining);
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
                var trueConditionState = Outcome(conditional.Condition, initialized, true, ref remaining);
                return Outcome(conditional.WhenTrue, trueConditionState, result, ref remaining);
            }
            if (!whenTruePossible && whenFalsePossible)
            {
                var falseConditionState = Outcome(conditional.Condition, initialized, false, ref remaining);
                return Outcome(conditional.WhenFalse, falseConditionState, result, ref remaining);
            }
            var whenTrue = Outcome(conditional.Condition, initialized, true, ref remaining);
            var whenFalse = Outcome(conditional.Condition, initialized, false, ref remaining);
            return Combine(Outcome(conditional.WhenTrue, whenTrue, result, ref remaining),
                whenTruePossible,
                Outcome(conditional.WhenFalse, whenFalse, result, ref remaining),
                whenFalsePossible);
        }
        var state = initialized.Clone();
        if (expression is BoundIsInitialized initializationTest)
        {
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
            state[variablePattern.Variable] = result;
            return state;
        }
        if (expression is BoundIsSome pattern)
        {
            VacateNestedOutputs(pattern.Value, state, ref remaining);
            state[pattern.Variable] = result;
            return state;
        }
        if (expression is BoundCall call)
        {
            foreach (var argument in call.Arguments)
            {
                if (argument is BoundOutArgument output)
                {
                    state[output.Variable] = call.Type != PrimitiveType.Bool || result;
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
                            state[output.Variable] = true;
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
            case BoundNativeFixedArrayElement fixedArrayElement:
                VacateNestedOutputs(fixedArrayElement.Index, state, ref remaining);
                break;
            case BoundNativeFixedArrayPointerAddress pointerAddress:
                VacateNestedOutputs(pointerAddress.Index, state, ref remaining);
                break;
            case BoundNativeStorageAddress address:
                VacateNestedOutputs(address.Index, state, ref remaining);
                break;
            case BoundNativePointerRead pointerRead:
                VacateNestedOutputs(pointerRead.Address, state, ref remaining);
                break;
            case BoundNativePointerWrite pointerWrite:
                VacateNestedOutputs(pointerWrite.Address, state, ref remaining);
                VacateNestedOutputs(pointerWrite.Value, state, ref remaining);
                break;
            case BoundNativeFixedArrayWrite write:
                VacateNestedOutputs(write.Index, state, ref remaining);
                VacateNestedOutputs(write.Value, state, ref remaining);
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
                state.Retire(move.Variable);
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

    // These structural classifications share a tree walk and remain independent of flow-state clones.
    // Unknown forms stay conservative for both properties.
    private static byte FlowFacts(BoundExpression expression,
        Dictionary<BoundExpression, byte> flowFacts, ref int remaining)
    {
        Count(ref remaining, 0);
        if (expression is null)
        {
            return AllFlowFacts;
        }
        if (flowFacts.TryGetValue(expression, out var facts))
        {
            return facts;
        }

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
            BoundNativeFixedArrayElement fixedArrayElement =>
                FlowFacts(fixedArrayElement.Index, flowFacts, ref remaining),
            BoundNativeFixedArrayPointerAddress pointerAddress =>
                FlowFacts(pointerAddress.Index, flowFacts, ref remaining),
            BoundNativeFixedArrayWrite fixedArrayWrite => AddFlowFact(
                FlowFacts(fixedArrayWrite.Index, flowFacts, ref remaining),
                fixedArrayWrite.Value, flowFacts, ref remaining),
            BoundNativeStorageAddress address => FlowFacts(address.Index, flowFacts, ref remaining),
            BoundNativePointerAddress => 0,
            BoundNativePointerRead pointerRead => FlowFacts(pointerRead.Address, flowFacts, ref remaining),
            BoundNativePointerWrite pointerWrite => AddFlowFact(
                FlowFacts(pointerWrite.Address, flowFacts, ref remaining),
                pointerWrite.Value, flowFacts, ref remaining),
            BoundIsSome test => (byte)(OutcomeEffectFact | FlowFacts(test.Value, flowFacts, ref remaining)),
            BoundIsVariablePattern or BoundIsInitialized => AllFlowFacts,
            BoundOutArgument or BoundMove => OutcomeEffectFact,
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

    // Ordinary expression feasibility is shared across flow-state clones; initialization-sensitive
    // compound expressions are handled above. Charge every lookup to the existing visit budget.
    private static bool CanBe(BoundExpression expression, bool result, ref int remaining,
        InitializationState initialized)
    {
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

    private static ArgumentException Invalid() => new("Invalid bound program.", "program");
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
    private static NotSupportedException Limit() => new("Bound program exceeds native resource limits.");
}
