// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed record NativeComOwnerLoanFacts(
    int ParameterIndex,
    int Variable,
    NativeComOwnerDefinition Owner,
    NativeComInterfaceDefinition Interface,
    int AcquisitionFunction);

internal sealed record NativeComCallFacts(
    NativeComMethodDefinition Method,
    ImmutableArray<NativeComOwnerLoanFacts> Loans);

internal static class NativeComCallContract
{
    internal const int MissingSuccessfulOutputHResult = unchecked((int)0x8000FFFF);

    internal static bool IsCataloguedOwnerName(string name, NativeImportCatalog? catalog) =>
        !string.IsNullOrWhiteSpace(name) && catalog is not null && catalog.TryGetComOwner(name, out _);

    internal static ImmutableArray<BoundFunction> DirectMethodFunctions(
        NativeImportCatalog catalog, ImmutableArray<BoundStruct> structures,
        IReadOnlySet<string>? usedMethodNames = null)
    {
        var program = new BoundProgram([], -1) {
            Structures = structures,
            NativeImportCatalog = catalog,
        };
        var functions = ImmutableArray.CreateBuilder<BoundFunction>();
        foreach (var owner in catalog.ComOwners)
        {
            var structureIndex = -1;
            for (var index = 0; index < structures.Length; index++)
            {
                if (structures[index].Name == owner.BoundOwnerName)
                {
                    structureIndex = index;
                    break;
                }
            }
            if (structureIndex < 0)
            {
                continue;
            }

            if (!HasOwnerCellLayout(structures[structureIndex], owner)
                || structures[structureIndex].CleanupMethod != owner.BoundOwnerName + ".Close")
            {
                continue;
            }
            var receiverType = (PrimitiveType)(100 + structureIndex);
            if (Owner(receiverType, program) is null)
            {
                continue;
            }
            if (catalog.TryGetComMethod(owner.ReleaseMethodIdentity, out var releaseMethod)
                && releaseMethod is not null)
            {
                var closeFunction = new BoundFunction(owner.BoundOwnerName + ".Close", PrimitiveType.Void,
                    [new("this", receiverType, false, default)], 1, new BoundBlock([], default), default) {
                    DeclaringStruct = owner.BoundOwnerName,
                    IsInternal = true,
                    IsNoThrow = true,
                    IsCleanup = true,
                    Intrinsic = BoundIntrinsic.NativeComOwnerClose,
                    NativeComMethod = releaseMethod,
                };
                if (IsValidOwnerCloseFunction(closeFunction, program))
                {
                    functions.Add(closeFunction);
                }
            }
            foreach (var method in catalog.ComMethods)
            {
                var separator = method.VTableMethodIdentity.LastIndexOf("::", StringComparison.Ordinal);
                var boundMethodName = method.BoundMethodName
                    ?? (separator < 0 ? "" : method.VTableMethodIdentity[(separator + 2)..]);
                if (separator < 0 || separator + 2 == method.VTableMethodIdentity.Length
                    || usedMethodNames is not null
                        && !usedMethodNames.Contains(boundMethodName))
                {
                    continue;
                }
                if (!method.IsCallable || method.OwnerInterfaceIdentity != owner.InterfaceIdentity
                    || !IsDirectMethodEffect(method)
                    || !method.ParameterAdaptations.IsDefaultOrEmpty
                        && method.ParameterAdaptations.Any(adaptation => adaptation is null
                            || adaptation.Kind is not (NativeComParameterAdaptationKind.AggregateAddress
                                or NativeComParameterAdaptationKind.AggregateStorageAddress
                                or NativeComParameterAdaptationKind.ValueAddress
                                or NativeComParameterAdaptationKind.ValueOutputAddress
                                or NativeComParameterAdaptationKind.ValueArrayAddress
                                or NativeComParameterAdaptationKind.PointerPreservingBaseInterface
                                or NativeComParameterAdaptationKind.SingleElementOwnerPointerArray
                                or NativeComParameterAdaptationKind.ComObjectBufferContents))
                    || !NativeComCatalog.TryParseSignature(method.Signature, out var returnIdentity,
                        out var parameterIdentities)
                    || parameterIdentities.Length == 0
                    || !TryResolveBoundType(returnIdentity, catalog, program, allowVoid: true,
                        out var returnType))
                {
                    continue;
                }

                var variables = ImmutableArray.CreateBuilder<BoundVariable>(parameterIdentities.Length);
                variables.Add(new("this", receiverType, false, default));
                var mappings = ImmutableArray.CreateBuilder<NativeComBoundParameterMapping>();
                var bufferContentOwnersBySizeParameter = new Dictionary<int, int>();
                var supported = true;
                for (var index = 1; index < parameterIdentities.Length; index++)
                {
                    if (method.HiddenParameters.Any(parameter => parameter.NativeParameterIndex == index))
                    {
                        continue;
                    }
                    if (bufferContentOwnersBySizeParameter.TryGetValue(index, out var bufferOwnerIndex))
                    {
                        mappings.Add(new(bufferOwnerIndex, index));
                        continue;
                    }
                    var adaptation = method.ParameterAdaptations.IsDefaultOrEmpty
                        ? null : method.ParameterAdaptations.SingleOrDefault(item =>
                            item.ParameterIndex == index);
                    if (adaptation?.Kind == NativeComParameterAdaptationKind.AggregateAddress)
                    {
                        if (!NativeAggregateContract.TryResolveSourceType(adaptation.SourceType,
                                program.Structures, out var aggregateType)
                            || !IsValidAggregateAddressType(aggregateType, adaptation, program))
                        {
                            supported = false;
                            break;
                        }
                        AddParameterMapping(mappings, variables.Count, index);
                        variables.Add(new($"parameter{index}", aggregateType, false, default));
                        continue;
                    }
                    if (adaptation?.Kind == NativeComParameterAdaptationKind.AggregateStorageAddress)
                    {
                        if (!IsValidAggregateStorageAddressType(adaptation, program))
                        {
                            supported = false;
                            break;
                        }
                        AddParameterMapping(mappings, variables.Count, index);
                        variables.Add(new($"parameter{index}", PrimitiveType.NativeStorageAddress,
                            false, default));
                        continue;
                    }
                    if (NativeStorageRecordContract.TryGetParameter(method, index,
                            out var storageRecordParameter))
                    {
                        if (storageRecordParameter is null
                            || !NativeStorageRecordContract.IsValidComParameter(storageRecordParameter,
                                method, catalog.StorageRecordLayouts))
                        {
                            supported = false;
                            break;
                        }
                        AddParameterMapping(mappings, variables.Count, index);
                        variables.Add(new($"parameter{index}", PrimitiveType.NativeStorageAddress,
                            false, default));
                        continue;
                    }
                    if (adaptation?.Kind == NativeComParameterAdaptationKind.ValueAddress)
                    {
                        if (!TryResolveBoundType(adaptation.SourceType, catalog, program,
                                allowVoid: false, out var sourceType))
                        {
                            supported = false;
                            break;
                        }
                        AddParameterMapping(mappings, variables.Count, index);
                        variables.Add(new($"parameter{index}", sourceType, false, default));
                        continue;
                    }
                    if (adaptation?.Kind == NativeComParameterAdaptationKind.ValueArrayAddress)
                    {
                        if (!TryResolveBoundType(adaptation.SourceType, catalog, program,
                                allowVoid: false, out var elementType)
                            || !NativeComCatalog.TryGetValueArrayElementCount(adaptation.TargetType,
                                out var elementCount))
                        {
                            supported = false;
                            break;
                        }
                        for (var elementIndex = 0; elementIndex < elementCount; elementIndex++)
                        {
                            mappings.Add(new(variables.Count, index));
                            variables.Add(new($"parameter{index}_{elementIndex}", elementType, false, default));
                        }
                        continue;
                    }
                    if (adaptation?.Kind == NativeComParameterAdaptationKind.ValueOutputAddress)
                    {
                        if (!IsValidValueOutputAddressType(adaptation, catalog, program.Structures,
                                out _))
                        {
                            supported = false;
                            break;
                        }
                        AddParameterMapping(mappings, variables.Count, index);
                        variables.Add(new($"parameter{index}", PrimitiveType.NativeStorageAddress,
                            false, default));
                        continue;
                    }
                    if (adaptation?.Kind == NativeComParameterAdaptationKind.ComObjectBufferContents)
                    {
                        if (!method.BorrowedOwnerParameters.Contains(index)
                            || adaptation.BufferPointerMethodIdentity is null
                            || adaptation.BufferSizeMethodIdentity is null
                            || !TryResolveOwnerPointerType(adaptation.SourceType, catalog, program,
                                out var bufferOwnerType))
                        {
                            supported = false;
                            break;
                        }
                        bufferOwnerIndex = variables.Count;
                        mappings.Add(new(bufferOwnerIndex, index));
                        bufferContentOwnersBySizeParameter.Add(index + 1, bufferOwnerIndex);
                        variables.Add(new($"parameter{index}", bufferOwnerType, false, default));
                        continue;
                    }
                    if (index == method.OutputParameterIndex)
                    {
                        if (adaptation is not null || method.OutputInterfaceIdentity is null
                            || !TryResolveOutputOwnerType(method.Identity, method.OutputInterfaceIdentity, program,
                                out var outputType)
                            || !method.Effects.HasFlag(NativeComEffects.AcquiresReference)
                            || !method.Effects.HasFlag(NativeComEffects.SignedHResult))
                        {
                            supported = false;
                            break;
                        }
                        AddParameterMapping(mappings, variables.Count, index);
                        variables.Add(new($"parameter{index}", outputType, true, default) { IsOut = true });
                        continue;
                    }
                    if (method.BorrowedOwnerParameters.Contains(index))
                    {
                        var ownerSourceType = adaptation?.SourceType ?? parameterIdentities[index];
                        if (!TryResolveOwnerPointerType(ownerSourceType, catalog, program,
                                out var borrowedOwnerType))
                        {
                            supported = false;
                            break;
                        }
                        AddParameterMapping(mappings, variables.Count, index);
                        variables.Add(new($"parameter{index}", borrowedOwnerType, false, default));
                        continue;
                    }
                    if (IsPointerSignatureType(parameterIdentities[index], catalog))
                    {
                        if (!TryGetNativeParameter(method, index, out var nativeParameter)
                            || nativeParameter is null
                            || nativeParameter.Retention != NativeParameterRetention.NoEscape
                            || nativeParameter.Extent != 1
                            || !TryGetNativeParameterPointeeType(method, index, catalog,
                                program.Structures, out _))
                        {
                            supported = false;
                            break;
                        }
                        AddParameterMapping(mappings, variables.Count, index);
                        variables.Add(new($"parameter{index}", PrimitiveType.NativeStorageAddress,
                            false, default));
                        continue;
                    }
                    if (!TryResolveBoundType(parameterIdentities[index], catalog, program,
                        allowVoid: false, out var parameterType))
                    {
                        supported = false;
                        break;
                    }
                    AddParameterMapping(mappings, variables.Count, index);
                    variables.Add(new($"parameter{index}", parameterType, false, default));
                }
                if (!supported)
                {
                    continue;
                }

                var name = owner.BoundOwnerName + "." + boundMethodName;
                var function = new BoundFunction(name,
                    method.Effects.HasFlag(NativeComEffects.SignedHResult)
                        && returnIdentity == "HRESULT" ? PrimitiveType.Bool : returnType,
                    variables.ToImmutable(), variables.Count, new BoundBlock([], default), default) {
                    DeclaringStruct = owner.BoundOwnerName,
                    IsInternal = true,
                    IsNoThrow = method.IsNoThrow,
                    RequiresUnsafeCaller = true,
                    Intrinsic = BoundIntrinsic.NativeComVTableCall,
                    NativeComMethod = method,
                    NativeComParameterMappings = mappings.ToImmutable(),
                    NativeComHiddenParameters = method.HiddenParameters,
                };
                if (IsValidVTableCallFunction(function, program))
                {
                    functions.Add(function);
                }
            }
        }
        return functions.ToImmutable();
    }

    private static void AddParameterMapping(
        ImmutableArray<NativeComBoundParameterMapping>.Builder mappings, int boundIndex, int nativeIndex)
    {
        if (boundIndex != nativeIndex)
        {
            mappings.Add(new(boundIndex, nativeIndex));
        }
    }

    private static bool IsDirectMethodEffect(NativeComMethodDefinition method)
    {
        if (method.OutputParameterIndex < 0)
        {
            return method.Effects is NativeComEffects.None or NativeComEffects.SignedHResult;
        }
        return method.Effects.HasFlag(NativeComEffects.SignedHResult)
            && method.Effects.HasFlag(NativeComEffects.AcquiresReference)
            && (method.Effects & ~(NativeComEffects.SignedHResult
                | NativeComEffects.AcquiresReference
                | NativeComEffects.OutputMayBeWrittenOnFailure)) == 0;
    }

    internal static bool HasOwnerCellLayout(BoundStruct structure, NativeComOwnerDefinition owner) =>
        structure.IsUnique && structure.Size == owner.CellSize
        && (structure.Fields.IsEmpty
            || structure.Fields is [{ Offset: 0, Size: sizeof(long), Type: PrimitiveType.NativeRawPointer,
                IsMutable: false } field] && field.IsPrivate);

    private static bool TryResolveOwnerType(string interfaceIdentity, BoundProgram program,
        out PrimitiveType type)
    {
        for (var index = 0; index < program.Structures.Length; index++)
        {
            var candidateType = (PrimitiveType)(100 + index);
            if (Owner(candidateType, program) is NativeComOwnerDefinition owner
                && owner.InterfaceIdentity == interfaceIdentity)
            {
                type = candidateType;
                return true;
            }
        }
        type = default;
        return false;
    }

    private static bool TryResolveOutputOwnerType(string methodIdentity, string interfaceIdentity,
        BoundProgram program, out PrimitiveType type)
    {
        for (var index = 0; index < program.Structures.Length; index++)
        {
            var candidateType = (PrimitiveType)(100 + index);
            if (Owner(candidateType, program) is NativeComOwnerDefinition owner
                && owner.InterfaceIdentity == interfaceIdentity
                && (owner.AcquisitionIdentity == methodIdentity
                    || owner.AdditionalAcquisitionMethodIdentities.Contains(methodIdentity)))
            {
                type = candidateType;
                return true;
            }
        }
        type = default;
        return false;
    }

    private static bool TryResolveOwnerPointerType(string sourceType, NativeImportCatalog catalog,
        BoundProgram program, out PrimitiveType type)
    {
        if (!sourceType.EndsWith("*", StringComparison.Ordinal)
            || sourceType.EndsWith("**", StringComparison.Ordinal)
            || !TryResolveInterface(sourceType[..^1].Trim(), catalog, out var interfaceIdentity))
        {
            type = default;
            return false;
        }
        return TryResolveOwnerType(interfaceIdentity, program, out type);
    }

    internal static BoundProgram Bind(BoundProgram program)
    {
        if (!program.Functions.Any(static function => function.NativeComMethod is not null
            && !function.IsCleanup))
        {
            return program;
        }
        return program with {
            Functions = [.. program.Functions.Select(function => {
                var acquisitions = Acquisitions(function, program);
                return BoundTreeTraversal.RewriteCalls(function, call => {
                    if ((uint)call.Function >= (uint)program.Functions.Length || call.Arguments.IsDefault)
                    {
                        throw Invalid();
                    }
                    var target = program.Functions[call.Function];
                    if (target.Intrinsic != BoundIntrinsic.NativeComVTableCall
                        || target.NativeComMethod is not NativeComMethodDefinition method)
                    {
                        return call;
                    }
                    var loans = ImmutableArray.CreateBuilder<NativeComOwnerLoanFacts>();
                    for (var index = 0; index < call.Arguments.Length; index++)
                    {
                        if (call.Arguments[index] is not BoundReceiverLoan loan
                            || Owner(loan.Type, program) is not NativeComOwnerDefinition owner)
                        {
                            continue;
                        }
                        if (acquisitions is null
                            || !acquisitions.TryGetValue(loan.Variable, out var acquisition))
                        {
                            throw Invalid();
                        }
                        var parameter = call.ParameterIndex(index);
                        var nativeParameter = NativeParameterIndex(target, parameter, method);
                        loans.Add(new(parameter, loan.Variable, owner,
                            LoanInterface(method, nativeParameter, owner, program), acquisition));
                    }
                    return call with { NativeComCall = new(method, loans.ToImmutable()) };
                });
            })],
        };
    }

    internal static Dictionary<int, int>? Acquisitions(BoundFunction function, BoundProgram program)
    {
        var declaredAcquisitions = function.NativeComAcquisitionOutputParameterIndexes;
        if (declaredAcquisitions.IsDefault
            || declaredAcquisitions.Distinct().Count() != declaredAcquisitions.Length)
        {
            throw Invalid("the declared acquisition output ordinals are malformed.");
        }
        foreach (var parameter in declaredAcquisitions)
        {
            if ((uint)parameter >= (uint)function.ParameterCount
                || parameter >= function.Variables.Length
                || !function.Variables[parameter].IsOut
                || Owner(function.Variables[parameter].Type, program) is not NativeComOwnerDefinition owner
                || !TryGetForwardedImportOutput(function, parameter, owner, program, out _, out _)
                    && !IsSourceAuthoredAcquisition(function, parameter, owner, program))
            {
                throw Invalid("a NativeComAcquisition output is not backed by a fixed-interface import contract.");
            }
        }
        if (!function.Variables.Any(variable => Owner(variable.Type, program) is not null))
        {
            return null;
        }
        if (!BoundTreeTraversal.IsWithinLimits(function.Body))
        {
            throw Invalid();
        }
        var acquisitions = new Dictionary<int, int>();
        _ = BoundTreeTraversal.RewriteCalls(function, call => {
            if (call.Function < 0 || call.Function >= program.Functions.Length || call.Arguments.IsDefault)
            {
                throw Invalid();
            }
            var target = program.Functions[call.Function];
            for (var index = 0; index < call.Arguments.Length; index++)
            {
                if (call.Arguments[index] is not BoundOutArgument output
                    || Owner(output.Type, program) is not NativeComOwnerDefinition owner)
                {
                    continue;
                }
                var parameter = call.ParameterIndex(index);
                var hasForwardedAcquisition = TryGetForwardedImportOutput(
                    target, parameter, owner, program, out _, out _);
                var hasSourceAcquisition = IsSourceAuthoredAcquisition(target, parameter, owner, program);
                var methodIdentity = target.NativeComMethod?.Identity
                    ?? target.NativeComAcquisitionMethodIdentity;
                var importKey = target.NativeComAcquisitionImportKey
                    ?? (target.IsExternal ? target.NativeImportKey : null);
                var isMethodOutputAcquisition = owner.AcquisitionKind
                    == NativeComOwnerAcquisitionKind.ComMethodOutput
                    && methodIdentity == owner.AcquisitionIdentity;
                var isAdditionalAcquisition = methodIdentity is not null
                    && owner.AdditionalAcquisitionMethodIdentities.Contains(methodIdentity);
                var isImportAcquisition = owner.AcquisitionKind
                    == NativeComOwnerAcquisitionKind.NativeImportOutput
                    && importKey == owner.AcquisitionIdentity;
                if (!isMethodOutputAcquisition && !isAdditionalAcquisition && !isImportAcquisition
                    && !hasForwardedAcquisition && !hasSourceAcquisition)
                {
                    continue;
                }
                var nativeParameter = parameter;
                if (target.NativeComMethod is NativeComMethodDefinition outputMethod
                    && outputMethod.OutputParameterIndex >= 0)
                {
                    nativeParameter = NativeParameterIndex(target, parameter, outputMethod);
                }
                else if (isMethodOutputAcquisition
                    && target.NativeComAcquisitionMethodIdentity is string identity
                    && program.NativeImportCatalog is NativeImportCatalog catalog
                    && catalog.TryGetComMethod(identity, out var catalogMethod)
                    && catalogMethod is not null)
                {
                    nativeParameter = NativeParameterIndex(target, parameter, catalogMethod);
                }
                if (!IsAcquisition(target, owner, nativeParameter, program))
                {
                    continue;
                }
                if (output.Variable < 0 || output.Variable >= function.Variables.Length
                    || output.Type != function.Variables[output.Variable].Type
                    || output.IsInit
                    || !(output.DeclaresVariable
                        && output.Variable >= function.ParameterCount
                        || !output.DeclaresVariable
                            && output.Variable < function.ParameterCount
                            && function.Variables[output.Variable].IsOut)
                    || parameter < 0 || parameter >= target.ParameterCount
                    || parameter >= target.Variables.Length
                    || !target.Variables[parameter].IsOut
                    || target.Variables[parameter].Type != output.Type)
                {
                    throw Invalid();
                }
                if (!acquisitions.TryAdd(output.Variable, call.Function))
                {
                    throw Invalid();
                }
            }
            return call;
        });
        return acquisitions;
    }

    internal static NativeComOwnerDefinition? Owner(PrimitiveType type, BoundProgram program)
    {
        var index = (int)type - 100;
        if (index < 0 || index >= program.Structures.Length
            || program.NativeImportCatalog is not NativeImportCatalog catalog
            || !catalog.TryGetComOwner(program.Structures[index].Name, out var owner))
        {
            return null;
        }
        var structure = program.Structures[index];
        if (owner is null
            || !owner.IsUnique || owner.CellSize != sizeof(long) || owner.CellAlignment != sizeof(long)
            || !HasOwnerCellLayout(structure, owner)
            || structure.CleanupMethod != owner.BoundOwnerName + ".Close")
        {
            throw Invalid();
        }
        return owner;
    }

    internal static NativeComInterfaceDefinition LoanInterface(NativeComMethodDefinition method,
        int parameter, NativeComOwnerDefinition owner, BoundProgram program)
    {
        if (program.NativeImportCatalog is not NativeImportCatalog catalog
            || !catalog.TryGetComMethod(method.Identity, out var selected) || !ReferenceEquals(method, selected)
            || !method.IsCallable || method.BorrowedOwnerParameters.IsDefault)
        {
            throw Invalid("The selected COM method metadata is invalid.");
        }
        if (!NativeComCatalog.TryParseSignature(method.Signature, out _, out var parameterTypes)
            || parameter < 0 || parameter >= parameterTypes.Length
            || (parameter > 0 && !method.BorrowedOwnerParameters.Contains(parameter)))
        {
            throw Invalid($"COM method '{method.Identity}' parameter {parameter} is not catalogued as a borrowed owner.");
        }
        var identity = method.OwnerInterfaceIdentity;
        if (parameter > 0)
        {
            var parameterType = parameterTypes[parameter];
            var adaptation = method.ParameterAdaptations.IsDefaultOrEmpty ? null
                : method.ParameterAdaptations.SingleOrDefault(item => item.ParameterIndex == parameter);
            if (adaptation is not null)
            {
                if (adaptation.SourceType != owner.InterfaceIdentity + "*"
                    || adaptation.TargetType != parameterType)
                {
                    throw Invalid("A COM call uses an invalid pointer-preserving interface adaptation.");
                }
                if (adaptation.Kind == NativeComParameterAdaptationKind.PointerPreservingBaseInterface)
                {
                    var targetInterface = adaptation.TargetType.EndsWith("*", StringComparison.Ordinal)
                        ? adaptation.TargetType[..^1] : "";
                    if (program.NativeImportCatalog is not NativeImportCatalog adaptationCatalog
                        || !adaptationCatalog.IsComBaseEdgePermitted(owner.InterfaceIdentity, targetInterface))
                    {
                        throw Invalid("A COM call uses an unpermitted base-interface adaptation.");
                    }
                    identity = targetInterface;
                }
                else if (adaptation.Kind == NativeComParameterAdaptationKind.SingleElementOwnerPointerArray
                    && adaptation.TargetType == owner.InterfaceIdentity + "*const*")
                {
                    identity = owner.InterfaceIdentity;
                }
                else if (adaptation.Kind == NativeComParameterAdaptationKind.ComObjectBufferContents
                    && adaptation.TargetType == "const void*")
                {
                    identity = owner.InterfaceIdentity;
                }
                else
                {
                    throw Invalid("A COM call uses an unsupported owner parameter adaptation.");
                }
            }
            else
            {
                if (!parameterType.EndsWith("*", StringComparison.Ordinal)
                    || parameterType.EndsWith("**", StringComparison.Ordinal)
                    || !TryResolveInterface(parameterType[..^1].Trim(),
                        program.NativeImportCatalog, out identity)
                    || identity != owner.InterfaceIdentity)
                {
                    throw Invalid("A COM call's borrowed-owner interface does not match its parameter.");
                }
            }
        }
        if ((parameter == 0 && owner.InterfaceIdentity != identity)
            || !catalog.TryGetComInterface(identity, out var definition) || definition is null)
        {
            throw Invalid("A COM call does not resolve to its catalogued interface.");
        }
        return definition;
    }

    internal static bool IsBorrowedOwnerArgument(BoundFunction function, int parameter,
        PrimitiveType expectedType, NativeImportCatalog? catalog)
    {
        if (function.NativeComMethod is not NativeComMethodDefinition method
            || catalog is null || !catalog.TryGetComMethod(method.Identity, out var selected)
            || !ReferenceEquals(method, selected) || parameter <= 0
            || parameter >= function.ParameterCount
            || function.Variables[parameter].Type != expectedType)
        {
            return false;
        }
        var mapping = function.NativeComParameterMappings.IsDefaultOrEmpty
            ? null : function.NativeComParameterMappings.FirstOrDefault(item =>
                item?.BoundParameterIndex == parameter);
        var nativeParameter = mapping?.NativeParameterIndex ?? parameter;
        if (!method.BorrowedOwnerParameters.Contains(nativeParameter))
        {
            return false;
        }
        return method.BorrowedOwnerParameters.Contains(nativeParameter)
            && (method.ParameterAdaptations.IsDefaultOrEmpty
                || method.ParameterAdaptations.All(adaptation =>
                    adaptation.ParameterIndex != nativeParameter
                    || adaptation.Kind == NativeComParameterAdaptationKind.ComObjectBufferContents
                    || adaptation.Kind == NativeComParameterAdaptationKind.PointerPreservingBaseInterface
                    || adaptation.Kind == NativeComParameterAdaptationKind.SingleElementOwnerPointerArray));
    }

    internal static bool IsValidVTableCallFunction(BoundFunction function, BoundProgram program)
    {
        if (function.Intrinsic != BoundIntrinsic.NativeComVTableCall
            || function.NativeComMethod is not NativeComMethodDefinition method
            || function.IsExternal || function.IsCleanup || function.IsConstructor || function.IsStatic
            || function.IsOperator || !function.RequiresUnsafeCaller || function.DoesNotReturn
            || function.DeclaringStruct is null
            || function.Body is null || function.Body.Statements.IsDefault
            || !function.Body.Statements.IsEmpty || function.Variables.IsDefault
            || function.ParameterCount != function.Variables.Length
            || function.NativeComAcquisitionImportKey is not null
            || function.NativeComAcquisitionMethodIdentity is not null
            || program.NativeImportCatalog is not NativeImportCatalog catalog
            || !catalog.TryGetComMethod(method.Identity, out var selected)
            || !ReferenceEquals(method, selected) || !method.IsCallable
            || !NativeComCatalog.TryParseSignature(method.Signature, out var returnType, out var parameters)
            || !TryResolveBoundType(returnType, catalog, program, allowVoid: true, out var expectedReturn))
        {
            return false;
        }
        var returnsHResultSuccess = method.Effects.HasFlag(NativeComEffects.SignedHResult)
            && returnType == "HRESULT" && function.ReturnType == PrimitiveType.Bool;
        if ((method.OutputParameterIndex >= 0 && !returnsHResultSuccess)
            || (method.OutputParameterIndex < 0
                && function.ReturnType != expectedReturn && !returnsHResultSuccess)
            || !TryMapParameters(function, parameters, out var boundParameterByNative, out var hiddenParameters))
        {
            return false;
        }
        if (!method.ParameterAdaptations.IsDefaultOrEmpty)
        {
            foreach (var adaptation in method.ParameterAdaptations)
            {
                if (adaptation?.Kind != NativeComParameterAdaptationKind.ComObjectBufferContents)
                {
                    continue;
                }
                var bufferOwnerParameters = BoundParameterIndices(function, adaptation.ParameterIndex);
                var sizeOwnerParameters = BoundParameterIndices(function, adaptation.ParameterIndex + 1);
                if (bufferOwnerParameters.Length != 1 || sizeOwnerParameters.Length != 1
                    || bufferOwnerParameters[0] != sizeOwnerParameters[0])
                {
                    return false;
                }
            }
        }
        for (var nativeIndex = 0; nativeIndex < parameters.Length; nativeIndex++)
        {
            var boundIndex = boundParameterByNative[nativeIndex];
            var boundIndices = BoundParameterIndices(function, nativeIndex);
            var adaptation = method.ParameterAdaptations.IsDefaultOrEmpty ? null
                : method.ParameterAdaptations.SingleOrDefault(item => item.ParameterIndex == nativeIndex);
            if (adaptation?.Kind == NativeComParameterAdaptationKind.ValueArrayAddress)
            {
                if (!NativeComCatalog.TryGetValueArrayElementCount(adaptation.TargetType, out var elementCount)
                    || boundIndices.Length != elementCount)
                {
                    return false;
                }
                foreach (var elementIndex in boundIndices)
                {
                    var element = function.Variables[elementIndex];
                    if (element is null || element.IsInit || element.IsImplicitRef || element.IsOut
                        || Owner(element.Type, program) is not null
                        || !IsValidBoundParameterType(element.Type, parameters[nativeIndex], nativeIndex,
                            method, catalog, program))
                    {
                        return false;
                    }
                }
            }
            else if (adaptation?.Kind == NativeComParameterAdaptationKind.AggregateAddress)
            {
                if (boundIndices.Length != 1
                    || !IsValidAggregateAddressType(function.Variables[boundIndices[0]].Type,
                        adaptation, program))
                {
                    return false;
                }
            }
            else if (adaptation?.Kind == NativeComParameterAdaptationKind.AggregateStorageAddress)
            {
                if (boundIndices.Length != 1
                    || function.Variables[boundIndices[0]].Type != PrimitiveType.NativeStorageAddress
                    || !IsValidAggregateStorageAddressType(adaptation, program))
                {
                    return false;
                }
            }
            else if (NativeStorageRecordContract.TryGetParameter(method, nativeIndex,
                         out var storageRecordParameter))
            {
                if (boundIndices.Length != 1
                    || function.Variables[boundIndices[0]].Type != PrimitiveType.NativeStorageAddress
                    || storageRecordParameter is null
                    || !NativeStorageRecordContract.IsValidComParameter(storageRecordParameter,
                        method, catalog.StorageRecordLayouts))
                {
                    return false;
                }
            }
            else if (boundIndices.Length > 1)
            {
                return false;
            }
            if (boundIndex < 0)
            {
                if (nativeIndex == method.OutputParameterIndex
                    || !IsValidHiddenParameter(hiddenParameters[nativeIndex], parameters[nativeIndex], catalog))
                {
                    return false;
                }
                continue;
            }
            var variable = function.Variables[boundIndex];
            if (variable is null || variable.IsInit || variable.IsImplicitRef)
            {
                return false;
            }
            if (adaptation?.Kind == NativeComParameterAdaptationKind.ValueOutputAddress)
            {
                if (variable.Type != PrimitiveType.NativeStorageAddress || variable.IsOut
                    || !IsValidBoundParameterType(variable.Type, parameters[nativeIndex], nativeIndex,
                        method, catalog, program))
                {
                    return false;
                }
                continue;
            }
            if (nativeIndex == method.OutputParameterIndex)
            {
                if (!variable.IsMutable || !variable.IsOut
                    || Owner(variable.Type, program) is not NativeComOwnerDefinition outputOwner
                    || outputOwner.InterfaceIdentity != method.OutputInterfaceIdentity)
                {
                    return false;
                }
                continue;
            }
            if (variable.IsOut
                || !IsValidBoundParameterType(variable.Type, parameters[nativeIndex], nativeIndex,
                    method, catalog, program)
                || nativeIndex > 0 && Owner(variable.Type, program) is not null
                    && !method.BorrowedOwnerParameters.Contains(nativeIndex)
                    && ComObjectBufferContentsAdaptation(method, nativeIndex) is null)
            {
                return false;
            }
        }
        return boundParameterByNative[0] == 0 && function.Variables.Length > 0
            && Owner(function.Variables[0].Type, program) is NativeComOwnerDefinition receiver
            && receiver.InterfaceIdentity == method.OwnerInterfaceIdentity;
    }

    private static bool TryMapParameters(BoundFunction function, string[] nativeParameters,
        out int[] boundParameterByNative, out NativeComHiddenParameter?[] hiddenParameters)
    {
        hiddenParameters = new NativeComHiddenParameter?[nativeParameters.Length];
        if (!TryMapBoundParameters(function, nativeParameters, out boundParameterByNative))
        {
            return false;
        }

        var hidden = function.NativeComHiddenParameters;
        if (!hidden.IsDefaultOrEmpty)
        {
            foreach (var parameter in hidden)
            {
                if (parameter is null || parameter.NativeParameterIndex <= 0
                    || parameter.NativeParameterIndex >= nativeParameters.Length
                    || boundParameterByNative[parameter.NativeParameterIndex] >= 0
                    || hiddenParameters[parameter.NativeParameterIndex] is not null)
                {
                    return false;
                }
                hiddenParameters[parameter.NativeParameterIndex] = parameter;
            }
        }

        for (var nativeIndex = 0; nativeIndex < nativeParameters.Length; nativeIndex++)
        {
            if (boundParameterByNative[nativeIndex] < 0 && hiddenParameters[nativeIndex] is null)
            {
                return false;
            }
        }
        for (var boundIndex = 0; boundIndex < function.ParameterCount; boundIndex++)
        {
            if (!boundParameterByNative.Contains(boundIndex)
                && (function.NativeComParameterMappings.IsDefaultOrEmpty
                    || !function.NativeComParameterMappings.Any(mapping =>
                        mapping.BoundParameterIndex == boundIndex)))
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryMapBoundParameters(BoundFunction function, string[] nativeParameters,
        out int[] boundParameterByNative)
    {
        boundParameterByNative = new int[nativeParameters.Length];
        Array.Fill(boundParameterByNative, -1);

        var mappings = function.NativeComParameterMappings;
        var mappedParameters = new HashSet<int>();
        var mappedNativeParameters = new HashSet<int>();
        if (!mappings.IsDefaultOrEmpty)
        {
            foreach (var mapping in mappings)
            {
                if (mapping is null || mapping.BoundParameterIndex < 0
                    || mapping.BoundParameterIndex >= function.ParameterCount
                    || mapping.NativeParameterIndex < 0 || mapping.NativeParameterIndex >= nativeParameters.Length
                    || (!mappedNativeParameters.Add(mapping.NativeParameterIndex)
                        && !IsValueArrayAddressParameter(function, mapping.NativeParameterIndex)))
                {
                    return false;
                }
                _ = mappedParameters.Add(mapping.BoundParameterIndex);
            }
            foreach (var group in mappings.GroupBy(static mapping => mapping.BoundParameterIndex))
            {
                if (group.Count() > 1 && !IsComObjectBufferContentsAliasSet(function, group))
                {
                    return false;
                }
            }
            foreach (var mapping in mappings)
            {
                if (boundParameterByNative[mapping.NativeParameterIndex] >= 0)
                {
                    if (!IsValueArrayAddressParameter(function, mapping.NativeParameterIndex))
                    {
                        return false;
                    }
                    continue;
                }
                boundParameterByNative[mapping.NativeParameterIndex] = mapping.BoundParameterIndex;
            }
        }

        for (var boundIndex = 0; boundIndex < function.ParameterCount; boundIndex++)
        {
            if (mappedParameters.Contains(boundIndex))
            {
                continue;
            }
            var nativeIndex = boundIndex;
            if (nativeIndex >= nativeParameters.Length || mappedNativeParameters.Contains(nativeIndex))
            {
                continue;
            }
            boundParameterByNative[nativeIndex] = boundIndex;
        }
        return true;
    }

    private static bool IsComObjectBufferContentsAliasSet(BoundFunction function,
        IEnumerable<NativeComBoundParameterMapping> mappings)
    {
        if (function.NativeComMethod is not NativeComMethodDefinition method)
        {
            return false;
        }
        var nativeIndices = mappings.Select(static mapping => mapping.NativeParameterIndex)
            .Order().ToArray();
        return !method.ParameterAdaptations.IsDefaultOrEmpty
            && method.ParameterAdaptations.Any(adaptation =>
                adaptation?.Kind == NativeComParameterAdaptationKind.ComObjectBufferContents
                && nativeIndices.SequenceEqual([adaptation.ParameterIndex, adaptation.ParameterIndex + 1]));
    }

    internal static int[] BoundParameterIndices(BoundFunction function, int nativeIndex)
    {
        var mappings = function.NativeComParameterMappings;
        if (mappings.IsDefaultOrEmpty)
        {
            return nativeIndex >= 0 && nativeIndex < function.ParameterCount ? [nativeIndex] : [];
        }
        var explicitMappings = mappings.Where(mapping => mapping.NativeParameterIndex == nativeIndex)
            .Select(static mapping => mapping.BoundParameterIndex).ToArray();
        if (explicitMappings.Length > 0)
        {
            return explicitMappings;
        }
        if (mappings.Any(mapping => mapping.BoundParameterIndex == nativeIndex))
        {
            return [];
        }
        return nativeIndex >= 0 && nativeIndex < function.ParameterCount ? [nativeIndex] : [];
    }

    private static bool IsValueArrayAddressParameter(BoundFunction function, int nativeIndex) =>
        function.NativeComMethod is NativeComMethodDefinition method
        && !method.ParameterAdaptations.IsDefaultOrEmpty
        && method.ParameterAdaptations.Any(adaptation => adaptation.ParameterIndex == nativeIndex
            && adaptation.Kind == NativeComParameterAdaptationKind.ValueArrayAddress);

    internal static bool TryGetNativeParameter(NativeComMethodDefinition method, int nativeIndex,
        out NativeParameterDefinition? parameter)
    {
        foreach (var candidate in method.NativeParameters)
        {
            if (candidate?.ParameterIndex == nativeIndex)
            {
                parameter = candidate;
                return true;
            }
        }
        parameter = null;
        return false;
    }

    internal static bool TryGetNativeParameterPointeeType(NativeComMethodDefinition method,
        int nativeIndex, NativeImportCatalog catalog, ImmutableArray<BoundStruct> structures,
        out PrimitiveType type)
    {
        if (!NativeComCatalog.TryParseSignature(method.Signature, out _, out var parameters)
            || nativeIndex <= 0 || nativeIndex >= parameters.Length
            || !parameters[nativeIndex].EndsWith("*", StringComparison.Ordinal)
            || parameters[nativeIndex].EndsWith("**", StringComparison.Ordinal))
        {
            type = default;
            return false;
        }
        var program = new BoundProgram([], -1) {
            Structures = structures,
            NativeImportCatalog = catalog,
        };
        return TryResolveBoundType(parameters[nativeIndex][..^1].Trim(), catalog, program,
            allowVoid: false, out type);
    }

    internal static bool IsNativeStorageAddressParameter(NativeComMethodDefinition method,
        int nativeIndex, NativeImportCatalog catalog, ImmutableArray<BoundStruct> structures) =>
        TryGetNativeParameter(method, nativeIndex, out var parameter)
        && parameter is not null && parameter.Retention == NativeParameterRetention.NoEscape
        && parameter.Extent == 1
        && TryGetNativeParameterPointeeType(method, nativeIndex, catalog, structures, out var pointeeType)
        && (DirectNativeImportContract.IsScalar(pointeeType)
            || pointeeType == PrimitiveType.NativeRawPointer);

    internal static NativeComParameterAdaptation? ComObjectBufferContentsAdaptation(
        NativeComMethodDefinition method, int nativeIndex) =>
        method.ParameterAdaptations.IsDefaultOrEmpty ? null
            : method.ParameterAdaptations.SingleOrDefault(adaptation =>
                adaptation.Kind == NativeComParameterAdaptationKind.ComObjectBufferContents
                && (adaptation.ParameterIndex == nativeIndex
                    || adaptation.ParameterIndex + 1 == nativeIndex));

    internal static bool IsComObjectBufferSizeParameter(
        NativeComParameterAdaptation adaptation, int nativeIndex) =>
        adaptation.Kind == NativeComParameterAdaptationKind.ComObjectBufferContents
        && adaptation.ParameterIndex + 1 == nativeIndex;

    private static bool IsValidBoundParameterType(PrimitiveType boundType, string signatureType,
        int nativeIndex, NativeComMethodDefinition method, NativeImportCatalog catalog, BoundProgram program)
    {
        if (boundType == PrimitiveType.NativeStorageAddress
            && IsNativeStorageAddressParameter(method, nativeIndex, catalog, program.Structures))
        {
            return true;
        }
        var bufferContents = ComObjectBufferContentsAdaptation(method, nativeIndex);
        if (bufferContents is not null)
        {
            return Owner(boundType, program) is NativeComOwnerDefinition bufferOwner
                && bufferContents.SourceType == bufferOwner.InterfaceIdentity + "*"
                && (IsComObjectBufferSizeParameter(bufferContents, nativeIndex)
                    ? signatureType == "SIZE_T"
                    : method.BorrowedOwnerParameters.Contains(nativeIndex)
                        && bufferContents.TargetType == signatureType
                        && bufferContents.BufferPointerMethodIdentity is not null
                        && bufferContents.BufferSizeMethodIdentity is not null);
        }

        if (NativeStorageRecordContract.TryGetParameter(method, nativeIndex,
                out var storageRecordParameter))
        {
            return boundType == PrimitiveType.NativeStorageAddress
                && storageRecordParameter is not null
                && NativeStorageRecordContract.IsValidComParameter(storageRecordParameter,
                    method, catalog.StorageRecordLayouts);
        }

        var adaptation = method.ParameterAdaptations.IsDefaultOrEmpty ? null
            : method.ParameterAdaptations.SingleOrDefault(item => item.ParameterIndex == nativeIndex);
        if (adaptation?.Kind == NativeComParameterAdaptationKind.AggregateAddress)
        {
            return adaptation.TargetType == signatureType
                && IsValidAggregateAddressType(boundType, adaptation, program);
        }
        if (adaptation?.Kind == NativeComParameterAdaptationKind.AggregateStorageAddress)
        {
            return adaptation.TargetType == signatureType
                && boundType == PrimitiveType.NativeStorageAddress
                && IsValidAggregateStorageAddressType(adaptation, program);
        }
        if (adaptation?.Kind == NativeComParameterAdaptationKind.ValueOutputAddress)
        {
            return adaptation.TargetType == signatureType
                && boundType == PrimitiveType.NativeStorageAddress
                && IsValidValueOutputAddressType(adaptation, catalog, program.Structures, out _);
        }
        if (adaptation?.Kind is NativeComParameterAdaptationKind.ValueAddress
            or NativeComParameterAdaptationKind.ValueArrayAddress)
        {
            return adaptation.TargetType == signatureType
                && TryResolveBoundType(adaptation.SourceType, catalog, program, allowVoid: false,
                    out var sourceType)
                && boundType == sourceType;
        }
        if (method.BorrowedOwnerParameters.Contains(nativeIndex)
            && Owner(boundType, program) is NativeComOwnerDefinition borrowedOwner
            && TryResolveInterface(signatureType.EndsWith("*", StringComparison.Ordinal)
                    ? signatureType[..^1].Trim() : "", catalog, out var borrowedInterface)
            && borrowedOwner.InterfaceIdentity == borrowedInterface)
        {
            return true;
        }
        if (TryResolveBoundType(signatureType, catalog, program, allowVoid: false, out var expectedType)
            && boundType == expectedType)
        {
            return true;
        }
        if (method.ParameterAdaptations.IsDefaultOrEmpty
            || adaptation is null
            || adaptation.TargetType != signatureType
            || adaptation.Kind is not (NativeComParameterAdaptationKind.PointerPreservingBaseInterface
                or NativeComParameterAdaptationKind.SingleElementOwnerPointerArray)
            || !adaptation.SourceType.EndsWith("*", StringComparison.Ordinal)
            || adaptation.SourceType.EndsWith("**", StringComparison.Ordinal)
            || Owner(boundType, program) is not NativeComOwnerDefinition owner
            || !TryResolveInterface(adaptation.SourceType[..^1].Trim(), catalog, out var sourceIdentity)
            || sourceIdentity != owner.InterfaceIdentity)
        {
            return false;
        }
        return adaptation.Kind switch {
            NativeComParameterAdaptationKind.PointerPreservingBaseInterface =>
                signatureType.EndsWith("*", StringComparison.Ordinal)
                && !signatureType.EndsWith("**", StringComparison.Ordinal)
                && TryResolveInterface(signatureType[..^1].Trim(), catalog, out var targetIdentity)
                && catalog.IsComBaseEdgePermitted(sourceIdentity, targetIdentity),
            NativeComParameterAdaptationKind.SingleElementOwnerPointerArray =>
                signatureType == owner.InterfaceIdentity + "*const*",
            _ => false,
        };
    }

    private static bool IsValidAggregateAddressType(PrimitiveType type,
        NativeComParameterAdaptation adaptation, BoundProgram program)
    {
        var index = (int)type - 100;
        if (index < 0 || index >= program.Structures.Length
            || !NativeAggregateContract.TryResolveSourceType(adaptation.SourceType, program.Structures,
                out var sourceType) || sourceType != type
            || program.Structures[index].IsUnique
            || program.Structures[index].CleanupMethod is not null
            || ValueLayout.Size(type, program.Structures) != adaptation.SourceSize
            || ValueLayout.Alignment(type, program.Structures, program.NativeImportCatalog)
                != adaptation.SourceAlignment
            || !NativeAggregateContract.IsPlainValueAggregate(type, program.Structures))
        {
            return false;
        }

        return true;
    }

    internal static bool IsValidValueOutputAddressType(NativeComParameterAdaptation adaptation,
        NativeImportCatalog catalog, IReadOnlyList<BoundStruct> structures, out PrimitiveType sourceType)
    {
        sourceType = default;
        if (adaptation.Kind != NativeComParameterAdaptationKind.ValueOutputAddress || !adaptation.NoEscape
            || !adaptation.TargetType.EndsWith("*", StringComparison.Ordinal)
            || adaptation.TargetType.EndsWith("**", StringComparison.Ordinal)
            || adaptation.TargetType.StartsWith("const ", StringComparison.Ordinal)
            || !TryResolveBoundType(adaptation.SourceType, catalog, structures, allowVoid: false,
                out sourceType)
            || sourceType is not (PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64
                or PrimitiveType.UInt64 or PrimitiveType.Float32 or PrimitiveType.Float64)
            || !TryResolveBoundType(adaptation.TargetType[..^1].Trim(), catalog, structures,
                allowVoid: false, out var targetType))
        {
            return false;
        }
        return sourceType == targetType;
    }

    private static bool IsValidAggregateStorageAddressType(
        NativeComParameterAdaptation adaptation, BoundProgram program)
    {
        var resolved = NativeAggregateContract.TryResolveSourceType(adaptation.SourceType,
            program.Structures, out var type);
        return adaptation.Kind == NativeComParameterAdaptationKind.AggregateStorageAddress
            && adaptation.NoEscape
            && resolved
            && NativeAggregateContract.IsPlainValueAggregate(type, program.Structures)
            && ValueLayout.Size(type, program.Structures) == adaptation.SourceSize
            && ValueLayout.Alignment(type, program.Structures, program.NativeImportCatalog)
                == adaptation.SourceAlignment
            && (adaptation.TargetType == adaptation.SourceType + "*"
                || adaptation.TargetType == "const " + adaptation.SourceType + "*");
    }

    private static bool IsValidHiddenParameter(NativeComHiddenParameter? parameter, string signatureType,
        NativeImportCatalog catalog)
    {
        if (parameter is null)
        {
            return false;
        }
        return parameter.Kind switch {
            NativeComHiddenParameterKind.UInt32 => parameter.InterfaceIdentity is null
                && IsSignatureType(signatureType, catalog, NativeComAbiValueKind.Unsigned32),
            NativeComHiddenParameterKind.NullPointer => parameter.Value == 0
                && parameter.InterfaceIdentity is null && IsPointerSignatureType(signatureType, catalog),
            NativeComHiddenParameterKind.InterfaceIdAddress => parameter.Value == 0
                && parameter.InterfaceIdentity is string interfaceIdentity
                && signatureType == "REFIID"
                && catalog.TryGetComInterface(interfaceIdentity, out var definition)
                && definition is not null,
            _ => false,
        };
    }

    private static bool IsSignatureType(string identity, NativeImportCatalog catalog,
        NativeComAbiValueKind kind) =>
        identity switch {
            "uint32" or "UINT" or "ULONG" => kind == NativeComAbiValueKind.Unsigned32,
            _ => catalog.ComSignatureTypes.Any(type =>
                type.Identity == identity && type.AbiValueKind == kind),
        };

    private static bool IsPointerSignatureType(string identity, NativeImportCatalog catalog) =>
        identity.EndsWith("*", StringComparison.Ordinal) || identity is "LPVOID" or "REFIID"
        || catalog.ComSignatureTypes.Any(type =>
            type.Identity == identity && type.AbiValueKind == NativeComAbiValueKind.Pointer);

    internal static bool IsValidOwnerCloseFunction(BoundFunction function, BoundProgram program)
    {
        if (function.Intrinsic != BoundIntrinsic.NativeComOwnerClose
            || function.NativeComMethod is not NativeComMethodDefinition method
            || function.IsExternal || !function.IsCleanup || !function.IsNoThrow
            || function.IsConstructor || function.IsStatic || function.IsOperator
            || function.RequiresUnsafeCaller || function.DoesNotReturn
            || function.ReturnType != PrimitiveType.Void || function.DeclaringStruct is null
            || function.Body is null || function.Body.Statements.IsDefault
            || !function.Body.Statements.IsEmpty || function.Variables is not [{ IsOut: false, IsInit: false,
                IsImplicitRef: false } receiver]
            || function.ParameterCount != 1 || function.NativeComAcquisitionImportKey is not null
            || function.NativeComAcquisitionMethodIdentity is not null
            || !function.NativeComParameterMappings.IsDefaultOrEmpty
            || !function.NativeComHiddenParameters.IsDefaultOrEmpty
            || program.NativeImportCatalog is not NativeImportCatalog catalog
            || !catalog.TryGetComMethod(method.Identity, out var selected)
            || !ReferenceEquals(method, selected) || !method.IsCallable
            || method.Effects != NativeComEffects.ReleasesReference
            || method.OutputParameterIndex != -1 || method.OutputInterfaceIdentity is not null
            || !NativeComCatalog.TryParseSignature(method.Signature, out var returnType, out var parameters)
            || returnType is not ("ULONG" or "uint32") || parameters.Length != 1)
        {
            return false;
        }

        var owner = Owner(receiver.Type, program);
        return owner is not null && owner.ReleaseMethodIdentity == method.Identity
            && program.NativeImportCatalog is NativeImportCatalog ownerCatalog
            && NativeComCatalog.IsAssignable(owner.InterfaceIdentity, method.OwnerInterfaceIdentity,
                ownerCatalog.ComInterfaces.ToDictionary(
                    static definition => definition.NominalIdentity, StringComparer.Ordinal))
            && function.Name == owner.BoundOwnerName + ".Close"
            && function.DeclaringStruct == owner.BoundOwnerName
            && function.Variables.Length == 1 && function.Variables[0].Type == receiver.Type
            && catalog.TryGetComInterface(owner.InterfaceIdentity, out var definition)
            && definition is not null
            && (parameters[0] == method.OwnerInterfaceIdentity + "*"
                || ownerCatalog.ComInterfaces.Single(item => item.NominalIdentity
                    == method.OwnerInterfaceIdentity).SignatureAliases.Any(alias =>
                    parameters[0] == alias + "*"));
    }

    internal static bool IsValidOwnerCloneFunction(BoundFunction function, BoundProgram program)
    {
        if (function.Intrinsic != BoundIntrinsic.NativeComOwnerTryClone
            || function.NativeComMethod is not NativeComMethodDefinition method
            || function.IsExternal || function.IsCleanup || function.IsConstructor || function.IsStatic
            || function.IsOperator || function.RequiresUnsafeCaller || function.DoesNotReturn
            || !function.IsNoThrow || function.ReturnType != PrimitiveType.Bool
            || function.DeclaringStruct is null || function.Body is null
            || function.Body.Statements.IsDefault || !function.Body.Statements.IsEmpty
            || function.Variables is not [
                { IsOut: false, IsInit: false, IsImplicitRef: false } receiver,
                { IsOut: true, IsInit: false, IsImplicitRef: false } clone,
            ] || function.ParameterCount != 2
            || !function.NativeComParameterMappings.IsDefaultOrEmpty
            || !function.NativeComHiddenParameters.IsDefaultOrEmpty
            || function.NativeComAcquisitionImportKey is not null
            || function.NativeComAcquisitionMethodIdentity is not null
            || program.NativeImportCatalog is not NativeImportCatalog catalog
            || !catalog.TryGetComMethod(method.Identity, out var selected)
            || !ReferenceEquals(method, selected) || !method.IsCallable
            || method.Effects != NativeComEffects.AcquiresReference
            || method.OutputParameterIndex != -1 || method.OutputInterfaceIdentity is not null
            || !NativeComCatalog.TryParseSignature(method.Signature, out var returnType, out var parameters)
            || returnType is not ("ULONG" or "uint32") || parameters.Length != 1)
        {
            return false;
        }

        var owner = Owner(receiver.Type, program);
        if (owner is null || !owner.IsUnique || clone.Type != receiver.Type
            || function.DeclaringStruct != owner.BoundOwnerName
            || method.OwnerInterfaceIdentity != owner.InterfaceIdentity
            || !owner.AdditionalAcquisitionMethodIdentities.Contains(method.Identity)
            || !catalog.TryGetComInterface(owner.InterfaceIdentity, out var definition)
            || definition is null)
        {
            return false;
        }
        return parameters[0] == definition.NominalIdentity + "*"
            || definition.SignatureAliases.Any(alias => parameters[0] == alias + "*");
    }

    internal static bool TryResolveBoundType(string identity, NativeImportCatalog catalog,
        BoundProgram program, bool allowVoid, out PrimitiveType type)
        => TryResolveBoundType(identity, catalog, program.Structures, allowVoid, out type);

    internal static bool TryResolveBoundType(string identity, NativeImportCatalog catalog,
        IReadOnlyList<BoundStruct> structures, bool allowVoid, out PrimitiveType type)
    {
        if (allowVoid && identity == "void")
        {
            type = PrimitiveType.Void;
            return true;
        }

        if (identity.EndsWith("*", StringComparison.Ordinal)
            && !identity.EndsWith("**", StringComparison.Ordinal))
        {
            var interfaceIdentity = identity[..^1].Trim();
            foreach (var definition in catalog.ComInterfaces)
            {
                if (definition.NominalIdentity != interfaceIdentity
                    && !definition.SignatureAliases.Contains(interfaceIdentity))
                {
                    continue;
                }
                var owner = catalog.ComOwners.FirstOrDefault(item =>
                    item.InterfaceIdentity == definition.NominalIdentity);
                if (owner is not null)
                {
                    for (var index = 0; index < structures.Count; index++)
                    {
                        if (structures[index].Name == owner.BoundOwnerName)
                        {
                            type = (PrimitiveType)(100 + index);
                            return true;
                        }
                    }
                }
                break;
            }
            type = default;
            return false;
        }

        var kind = identity switch {
            "int32" or "HRESULT" => NativeComAbiValueKind.Signed32,
            "uint32" or "UINT" or "ULONG" => NativeComAbiValueKind.Unsigned32,
            "int64" => NativeComAbiValueKind.Signed64,
            "uint64" or "SIZE_T" => NativeComAbiValueKind.Unsigned64,
            "float" or "FLOAT" => NativeComAbiValueKind.Float32,
            "double" or "DOUBLE" => NativeComAbiValueKind.Float64,
            _ => (NativeComAbiValueKind)byte.MaxValue,
        };
        if ((byte)kind == byte.MaxValue)
        {
            foreach (var signatureType in catalog.ComSignatureTypes)
            {
                if (signatureType.Identity == identity)
                {
                    kind = signatureType.AbiValueKind;
                    break;
                }
            }
        }
        type = kind switch {
            NativeComAbiValueKind.Signed32 => PrimitiveType.Int32,
            NativeComAbiValueKind.Unsigned32 => PrimitiveType.UInt32,
            NativeComAbiValueKind.Signed64 => PrimitiveType.Int64,
            NativeComAbiValueKind.Unsigned64 => PrimitiveType.UInt64,
            NativeComAbiValueKind.Float32 => PrimitiveType.Float32,
            NativeComAbiValueKind.Float64 => PrimitiveType.Float64,
            _ => default,
        };
        return (byte)kind != byte.MaxValue && kind != NativeComAbiValueKind.Pointer;
    }

    internal static int NativeParameterIndex(BoundFunction function, int parameter,
        NativeComMethodDefinition method)
    {
        if ((uint)parameter >= (uint)function.ParameterCount || parameter >= function.Variables.Length
            || (!ReferenceEquals(function.NativeComMethod, method)
                && function.NativeComAcquisitionMethodIdentity != method.Identity)
            || !NativeComCatalog.TryParseSignature(method.Signature, out _, out var nativeParameters))
        {
            throw Invalid("A COM call parameter does not belong to its closed method signature.");
        }
        int[] boundParameterByNative;
        var validMapping = function.Intrinsic == BoundIntrinsic.NativeComVTableCall
            ? TryMapParameters(function, nativeParameters, out boundParameterByNative, out _)
            : TryMapBoundParameters(function, nativeParameters, out boundParameterByNative);
        if (!validMapping)
        {
            throw Invalid($"COM function '{function.Name}' has an invalid bound-to-native parameter mapping for '{method.Identity}'.");
        }
        var mappedParameter = function.NativeComParameterMappings.IsDefaultOrEmpty ? null
            : function.NativeComParameterMappings.FirstOrDefault(item =>
                item.BoundParameterIndex == parameter);
        if (mappedParameter is not null)
        {
            return mappedParameter.NativeParameterIndex;
        }
        var nativeParameter = Array.IndexOf(boundParameterByNative, parameter);
        if (nativeParameter < 0)
        {
            throw Invalid("A COM call parameter has no native ABI mapping.");
        }
        return nativeParameter;
    }

    internal static NativeComCallFacts? Project(NativeComCallFacts? facts,
        Func<int, int> functionIndex, Func<int, int> variableIndex)
    {
        if (facts is null)
        {
            return null;
        }
        if (facts.Loans.IsDefault || facts.Loans.Length > SemanticVerifier.MaximumArguments
            || facts.Loans.Any(static loan => loan is null))
        {
            throw Invalid();
        }
        return facts with {
            Loans = [.. facts.Loans.Select(loan => loan with {
                Variable = variableIndex(loan.Variable),
                AcquisitionFunction = functionIndex(loan.AcquisitionFunction),
            })],
        };
    }

    private static bool TryResolveInterface(string identity, NativeImportCatalog? catalog,
        out string resolvedIdentity)
    {
        if (catalog is not null)
        {
            foreach (var definition in catalog.ComInterfaces)
            {
                if (definition.NominalIdentity == identity || definition.SignatureAliases.Contains(identity))
                {
                    resolvedIdentity = definition.NominalIdentity;
                    return true;
                }
            }
        }
        resolvedIdentity = "";
        return false;
    }

    private static bool IsAcquisition(BoundFunction target, NativeComOwnerDefinition owner,
        int parameter, BoundProgram program)
    {
        if (IsSourceAuthoredAcquisition(target, parameter, owner, program))
        {
            return true;
        }
        if (TryGetForwardedImportOutput(target, parameter, owner, program, out _, out _))
        {
            return true;
        }

        if (target.NativeComAcquisitionMethodIdentity is string acquisitionIdentity)
        {
            return target.NativeComMethod is null
                && owner.AcquisitionKind == NativeComOwnerAcquisitionKind.ComMethodOutput
                && owner.AcquisitionIdentity == acquisitionIdentity
                && owner.OutputParameterIndex == parameter
                && program.NativeImportCatalog is NativeImportCatalog acquisitionCatalog
                && acquisitionCatalog.TryGetComMethod(acquisitionIdentity, out var acquisitionMethod)
                && acquisitionMethod is not null
                && acquisitionMethod.OutputParameterIndex == parameter
                && acquisitionMethod.OutputInterfaceIdentity == owner.InterfaceIdentity
                && acquisitionMethod.Effects.HasFlag(NativeComEffects.AcquiresReference);
        }
        if (target.NativeComMethod is NativeComMethodDefinition method)
        {
            var isMethodOutput = owner.AcquisitionKind == NativeComOwnerAcquisitionKind.ComMethodOutput
                && method.Identity == owner.AcquisitionIdentity
                && method.OutputParameterIndex == parameter
                && method.OutputInterfaceIdentity == owner.InterfaceIdentity
                && method.OutputParameterIndex == owner.OutputParameterIndex;
            var isAdditionalAcquisition = target.Intrinsic == BoundIntrinsic.NativeComOwnerTryClone
                && owner.AdditionalAcquisitionMethodIdentities.Contains(method.Identity);
            var isAdditionalOutputAcquisition = method.OutputParameterIndex == parameter
                && method.OutputInterfaceIdentity == owner.InterfaceIdentity
                && owner.AdditionalAcquisitionMethodIdentities.Contains(method.Identity);
            return program.NativeImportCatalog is NativeImportCatalog catalog
                && catalog.TryGetComMethod(method.Identity, out var selected)
                && ReferenceEquals(method, selected)
                && method.Effects.HasFlag(NativeComEffects.AcquiresReference)
                && (isMethodOutput || isAdditionalAcquisition || isAdditionalOutputAcquisition);
        }
        var key = target.NativeComAcquisitionImportKey;
        if (key is not null && !target.IsExternal)
        {
            return IsNativeImportOutputAcquisition(owner, key, owner.OutputParameterIndex, program);
        }
        if (key is null && target.IsExternal)
        {
            key = target.NativeImportKey;
        }
        return key is not null && IsNativeImportOutputAcquisition(owner, key, parameter, program);
    }

    private static bool IsSourceAuthoredAcquisition(BoundFunction function, int outputParameter,
        NativeComOwnerDefinition owner, BoundProgram program)
    {
        if (!function.NativeComAcquisitionOutputParameterIndexes.Contains(outputParameter)
            || function.NativeComAcquisitionSources.IsDefault)
        {
            return false;
        }

        foreach (var source in function.NativeComAcquisitionSources)
        {
            if (source.OutputParameterIndex != outputParameter
                || source.OwnerTypeIdentity != owner.BoundOwnerName
                || source.SourceMethodIdentity != owner.AcquisitionIdentity
                || source.SourceOutputParameterIndex != owner.OutputParameterIndex
                || source.OutputMayBeWrittenOnFailure != owner.OutputMayBeWrittenOnFailure)
            {
                continue;
            }

            if (owner.AcquisitionKind == NativeComOwnerAcquisitionKind.ComMethodOutput
                && program.NativeImportCatalog is NativeImportCatalog comCatalog
                && comCatalog.TryGetComMethod(source.SourceMethodIdentity, out var comMethod)
                && comMethod is not null
                && comMethod.OutputParameterIndex == source.SourceOutputParameterIndex
                && comMethod.OutputInterfaceIdentity == owner.InterfaceIdentity
                && comMethod.Effects.HasFlag(NativeComEffects.AcquiresReference)
                && comMethod.Effects.HasFlag(NativeComEffects.OutputMayBeWrittenOnFailure)
                    == source.OutputMayBeWrittenOnFailure)
            {
                return true;
            }

            if (owner.AcquisitionKind == NativeComOwnerAcquisitionKind.DirectNativeImportOutput
                && source.OutputMayBeWrittenOnFailure
                && program.Functions.Any(candidate =>
                    candidate.Name == source.SourceMethodIdentity
                    && candidate.IsExternal && candidate.DirectNativeImport is not null
                    && DirectNativeImportContract.IsValid(candidate)
                    && DirectNativeImportContract.OutputForParameter(candidate,
                        source.SourceOutputParameterIndex) is DirectNativeOutput {
                            PointeeType: PrimitiveType.NativeRawPointer,
                            FailureDisposition: 1,
                        }))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNativeImportOutputAcquisition(NativeComOwnerDefinition owner, string importKey,
        int parameter, BoundProgram program) =>
        owner.AcquisitionKind == NativeComOwnerAcquisitionKind.NativeImportOutput
        && owner.AcquisitionIdentity == importKey
        && (owner.OutputParameterIndex == parameter
            || owner.AdditionalOutputParameterIndexes.Contains(parameter))
        && program.NativeImportCatalog is NativeImportCatalog importCatalog
        && importCatalog.TryGetImport(importKey, out var import) && import is not null
        && parameter >= 0 && parameter < import.ParameterContracts.Length
        && import.ParameterContracts[parameter]
                == NativeImportParameterContract.OwnedNativeComInterfaceOutput
        && import.ComOutputs.Any(output => output.ParameterIndex == parameter
                && output.InterfaceIdentity == owner.InterfaceIdentity
                && output.Effects.HasFlag(NativeComEffects.AcquiresReference));

    private static bool TryGetForwardedImportOutput(BoundFunction function, int parameter,
        NativeComOwnerDefinition owner, BoundProgram program, out int importFunctionIndex,
        out int importParameterIndex)
    {
        importFunctionIndex = -1;
        importParameterIndex = -1;
        if (function.IsExternal || function.IsCleanup || function.IsConstructor || function.IsClassConstructor
            || function.DoesNotReturn || function.Intrinsic != BoundIntrinsic.None
            || function.NativeImportKey is not null || function.NativeComMethod is not null
            || function.Variables.IsDefault || function.ParameterCount != function.Variables.Length
            || parameter < 0 || parameter >= function.ParameterCount
            || !function.NativeComAcquisitionOutputParameterIndexes.Contains(parameter)
            || !function.Variables[parameter].IsOut
            || Owner(function.Variables[parameter].Type, program) != owner
            || function.ReturnType != PrimitiveType.Bool
            || function.Body is not BoundBlock { Statements: [BoundTryFinally outputScope] }
            || !BoundTreeTraversal.IsWithinLimits(function.Body)
            || outputScope.Finally.Statements.IsDefault)
        {
            return false;
        }

        var calls = new List<BoundCall>();
        _ = BoundTreeTraversal.RewriteCalls(function, call => {
            calls.Add(call);
            return call;
        });
        if (calls.Count != 1)
        {
            return false;
        }
        var call = calls[0];
        if (!call.ArgumentParameterIndices.IsDefault || !call.IsUnsafeAuthorized
            || call.Function < 0 || call.Function >= program.Functions.Length
            || !ProvesAcquisitionResult(outputScope.Body, call))
        {
            return false;
        }

        var importFunction = program.Functions[call.Function];
        if (importFunction is not { IsExternal: true, NativeImportKey: string importKey })
        {
            return false;
        }
        if (program.NativeImportCatalog is not NativeImportCatalog catalog
            || !catalog.TryGetImport(importKey, out var import) || import is null
            || !catalog.MatchesFunction(importFunction, import, program.Structures)
            || import.ComOutputs.IsDefaultOrEmpty
            || import.ComOutputs.Any(static output => output is null
                || !output.Effects.HasFlag(NativeComEffects.SignedHResult))
            || outputScope.Finally.Statements.IsDefault)
        {
            return false;
        }

        for (var parameterIndex = 0; parameterIndex < function.ParameterCount; parameterIndex++)
        {
            var outputType = function.Variables[parameterIndex].Type;
            if (!function.Variables[parameterIndex].IsOut
                || Owner(outputType, program) is not NativeComOwnerDefinition)
            {
                continue;
            }
            if (!outputScope.Finally.Statements.Any(statement => statement is BoundDrop {
                    Variable: var outputVariable, IsOutput: true,
                } && outputVariable == parameterIndex))
            {
                return false;
            }
        }

        var foundParameter = false;
        foreach (var outputDefinition in import.ComOutputs)
        {
            var outputArgument = -1;
            for (var argument = 0; argument < call.Arguments.Length; argument++)
            {
                if (call.ParameterIndex(argument) == outputDefinition.ParameterIndex)
                {
                    if (outputArgument >= 0)
                    {
                        return false;
                    }
                    outputArgument = argument;
                }
            }
            if (outputArgument < 0
                || call.Arguments[outputArgument] is not BoundOutArgument output
                || output.DeclaresVariable || output.IsInit
                || output.Variable < 0 || output.Variable >= function.ParameterCount
                || !function.Variables[output.Variable].IsOut
                || function.Variables[output.Variable].Type != output.Type
                || Owner(output.Type, program) is not NativeComOwnerDefinition outputOwner
                || !IsNativeImportOutputAcquisition(outputOwner, importKey,
                    outputDefinition.ParameterIndex, program))
            {
                return false;
            }
            if (output.Variable == parameter && outputOwner == owner)
            {
                foundParameter = true;
                importParameterIndex = outputDefinition.ParameterIndex;
            }
        }

        if (!foundParameter)
        {
            return false;
        }
        importFunctionIndex = call.Function;
        return true;
    }

    private enum AcquisitionResultState : byte
    {
        NotCalled,
        Failure,
        Success,
    }

    private readonly record struct AcquisitionPath(AcquisitionResultState State, bool? ReturnValue);

    private readonly record struct AcquisitionBooleanOutcome(AcquisitionResultState State, bool Value);

    private static bool ProvesAcquisitionResult(BoundBlock body, BoundCall call)
    {
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        var paths = new HashSet<AcquisitionPath> {
            new(AcquisitionResultState.NotCalled, null),
        };
        if (!TryAnalyzeAcquisitionStatement(body, paths, call.Function, call.Span,
            ref remaining, 0, out paths))
        {
            return false;
        }

        var hasSuccessfulReturn = false;
        foreach (var path in paths)
        {
            if (path.ReturnValue is not bool returned || path.State == AcquisitionResultState.NotCalled
                || (returned && path.State != AcquisitionResultState.Success))
            {
                return false;
            }
            hasSuccessfulReturn |= returned && path.State == AcquisitionResultState.Success;
        }
        return hasSuccessfulReturn;
    }

    private static bool TryAnalyzeAcquisitionStatement(BoundStatement statement,
        HashSet<AcquisitionPath> paths, int importFunction, Terrum.CodeAnalysis.Text.TextSpan importSpan,
        ref int remaining, int depth, out HashSet<AcquisitionPath> result)
    {
        result = [];
        if (depth > SemanticVerifier.MaximumDepth || --remaining < 0)
        {
            return false;
        }

        switch (statement)
        {
            case BoundBlock block when !block.Statements.IsDefault:
                var current = paths;
                foreach (var child in block.Statements)
                {
                    if (!TryAnalyzeAcquisitionStatement(child, current, importFunction, importSpan,
                        ref remaining, depth + 1, out current))
                    {
                        return false;
                    }
                }
                result = current;
                return true;

            case BoundIf branch:
                foreach (var path in paths)
                {
                    if (path.ReturnValue is not null)
                    {
                        result.Add(path);
                        continue;
                    }
                    if (!TryEvaluateAcquisitionBoolean(branch.Condition, path.State,
                        importFunction, importSpan, ref remaining, depth + 1, out var outcomes))
                    {
                        return false;
                    }
                    foreach (var outcome in outcomes)
                    {
                        var branchPath = new HashSet<AcquisitionPath> {
                            new(outcome.State, null),
                        };
                        if (outcome.Value || branch.Else is not null)
                        {
                            if (!TryAnalyzeAcquisitionStatement(outcome.Value ? branch.Then : branch.Else!,
                                branchPath, importFunction, importSpan, ref remaining, depth + 1,
                                out var branchResult))
                            {
                                return false;
                            }
                            result.UnionWith(branchResult);
                        }
                        else
                        {
                            result.UnionWith(branchPath);
                        }
                    }
                }
                return true;

            case BoundUnsafe unsafeBlock:
                return TryAnalyzeAcquisitionStatement(unsafeBlock.Body, paths, importFunction, importSpan,
                    ref remaining, depth + 1, out result);

            case BoundReturn { Value: { Type: PrimitiveType.Bool } value }:
                foreach (var path in paths)
                {
                    if (path.ReturnValue is not null)
                    {
                        result.Add(path);
                        continue;
                    }
                    if (!TryEvaluateAcquisitionBoolean(value, path.State, importFunction, importSpan,
                        ref remaining, depth + 1, out var outcomes))
                    {
                        return false;
                    }
                    foreach (var outcome in outcomes)
                    {
                        result.Add(new(outcome.State, outcome.Value));
                    }
                }
                return true;

            default:
                return false;
        }
    }

    private static bool TryEvaluateAcquisitionBoolean(BoundExpression expression, AcquisitionResultState state,
        int importFunction, Terrum.CodeAnalysis.Text.TextSpan importSpan, ref int remaining, int depth,
        out HashSet<AcquisitionBooleanOutcome> outcomes)
    {
        outcomes = [];
        if (expression.Type != PrimitiveType.Bool || depth > SemanticVerifier.MaximumDepth || --remaining < 0)
        {
            return false;
        }

        switch (expression)
        {
            case BoundUnsafeExpression unsafeExpression:
                return TryEvaluateAcquisitionBoolean(unsafeExpression.Expression, state, importFunction, importSpan,
                    ref remaining, depth + 1, out outcomes);

            case BoundConstant { Type: PrimitiveType.Bool } constant:
                outcomes.Add(new(state, constant.Value != 0));
                return true;

            case BoundUnary { Operator: Terrum.CodeAnalysis.Lexing.TokenKind.Bang } unary:
                if (!TryEvaluateAcquisitionBoolean(unary.Operand, state, importFunction, importSpan,
                    ref remaining, depth + 1, out var operandOutcomes))
                {
                    return false;
                }
                foreach (var outcome in operandOutcomes)
                {
                    outcomes.Add(new(outcome.State, !outcome.Value));
                }
                return true;

            case BoundBinary { Operator: Terrum.CodeAnalysis.Lexing.TokenKind.AmpersandAmpersand } and:
                if (!TryEvaluateAcquisitionBoolean(and.Left, state, importFunction, importSpan,
                    ref remaining, depth + 1, out var leftAnd))
                {
                    return false;
                }
                foreach (var left in leftAnd)
                {
                    if (!left.Value)
                    {
                        outcomes.Add(new(left.State, false));
                    }
                    else if (!TryEvaluateAcquisitionBoolean(and.Right, left.State, importFunction, importSpan,
                        ref remaining, depth + 1, out var rightAnd))
                    {
                        return false;
                    }
                    else
                    {
                        outcomes.UnionWith(rightAnd);
                    }
                }
                return true;

            case BoundBinary { Operator: Terrum.CodeAnalysis.Lexing.TokenKind.PipePipe } or:
                if (!TryEvaluateAcquisitionBoolean(or.Left, state, importFunction, importSpan,
                    ref remaining, depth + 1, out var leftOr))
                {
                    return false;
                }
                foreach (var left in leftOr)
                {
                    if (left.Value)
                    {
                        outcomes.Add(new(left.State, true));
                    }
                    else if (!TryEvaluateAcquisitionBoolean(or.Right, left.State, importFunction, importSpan,
                        ref remaining, depth + 1, out var rightOr))
                    {
                        return false;
                    }
                    else
                    {
                        outcomes.UnionWith(rightOr);
                    }
                }
                return true;

            case BoundConditional conditional:
                if (!TryEvaluateAcquisitionBoolean(conditional.Condition, state, importFunction, importSpan,
                    ref remaining, depth + 1, out var conditions))
                {
                    return false;
                }
                foreach (var condition in conditions)
                {
                    if (!TryEvaluateAcquisitionBoolean(condition.Value ? conditional.WhenTrue : conditional.WhenFalse,
                        condition.State, importFunction, importSpan, ref remaining, depth + 1, out var values))
                    {
                        return false;
                    }
                    outcomes.UnionWith(values);
                }
                return true;

            case BoundBinary comparison when TryGetAcquisitionComparison(
                comparison, importFunction, importSpan, out var comparisonOperator, out var constant):
                if (state != AcquisitionResultState.NotCalled)
                {
                    return false;
                }
                AddAcquisitionComparisonOutcomes(outcomes, comparisonOperator, constant);
                return outcomes.Count != 0;

            default:
                outcomes.Add(new(state, false));
                outcomes.Add(new(state, true));
                return true;
        }
    }

    private static bool TryGetAcquisitionComparison(BoundBinary comparison, int importFunction,
        Terrum.CodeAnalysis.Text.TextSpan importSpan, out Terrum.CodeAnalysis.Lexing.TokenKind operation,
        out long constant)
    {
        operation = default;
        constant = 0;
        var left = UnwrapUnsafe(comparison.Left);
        var right = UnwrapUnsafe(comparison.Right);
        if (left is BoundCall { Type: PrimitiveType.Int32 } call
            && call.Function == importFunction && call.Span == importSpan
            && right is BoundConstant { Type: PrimitiveType.Int32 } rightConstant)
        {
            operation = comparison.Operator;
            constant = rightConstant.Value;
            return IsComparison(operation);
        }
        if (right is BoundCall { Type: PrimitiveType.Int32 } reverseCall
            && reverseCall.Function == importFunction && reverseCall.Span == importSpan
            && left is BoundConstant { Type: PrimitiveType.Int32 } leftConstant
            && TryReverseComparison(comparison.Operator, out operation))
        {
            constant = leftConstant.Value;
            return true;
        }
        return false;
    }

    private static BoundExpression UnwrapUnsafe(BoundExpression expression) =>
        expression is BoundUnsafeExpression unsafeExpression ? UnwrapUnsafe(unsafeExpression.Expression) : expression;

    private static bool IsComparison(Terrum.CodeAnalysis.Lexing.TokenKind operation) =>
        operation is Terrum.CodeAnalysis.Lexing.TokenKind.EqualsEquals
            or Terrum.CodeAnalysis.Lexing.TokenKind.BangEquals
            or Terrum.CodeAnalysis.Lexing.TokenKind.Less
            or Terrum.CodeAnalysis.Lexing.TokenKind.LessEquals
            or Terrum.CodeAnalysis.Lexing.TokenKind.Greater
            or Terrum.CodeAnalysis.Lexing.TokenKind.GreaterEquals;

    private static bool TryReverseComparison(Terrum.CodeAnalysis.Lexing.TokenKind operation,
        out Terrum.CodeAnalysis.Lexing.TokenKind reversed)
    {
        reversed = operation switch {
            Terrum.CodeAnalysis.Lexing.TokenKind.Less => Terrum.CodeAnalysis.Lexing.TokenKind.Greater,
            Terrum.CodeAnalysis.Lexing.TokenKind.LessEquals => Terrum.CodeAnalysis.Lexing.TokenKind.GreaterEquals,
            Terrum.CodeAnalysis.Lexing.TokenKind.Greater => Terrum.CodeAnalysis.Lexing.TokenKind.Less,
            Terrum.CodeAnalysis.Lexing.TokenKind.GreaterEquals => Terrum.CodeAnalysis.Lexing.TokenKind.LessEquals,
            Terrum.CodeAnalysis.Lexing.TokenKind.EqualsEquals => Terrum.CodeAnalysis.Lexing.TokenKind.EqualsEquals,
            Terrum.CodeAnalysis.Lexing.TokenKind.BangEquals => Terrum.CodeAnalysis.Lexing.TokenKind.BangEquals,
            _ => default,
        };
        return IsComparison(reversed);
    }

    private static void AddAcquisitionComparisonOutcomes(
        HashSet<AcquisitionBooleanOutcome> outcomes,
        Terrum.CodeAnalysis.Lexing.TokenKind operation, long constant)
    {
        var successCanBeTrue = ComparisonCanBe(operation, 0, int.MaxValue, constant);
        var successCanBeFalse = ComparisonCanBe(ReverseTruth(operation), 0, int.MaxValue, constant);
        var failureCanBeTrue = ComparisonCanBe(operation, int.MinValue, -1, constant);
        var failureCanBeFalse = ComparisonCanBe(ReverseTruth(operation), int.MinValue, -1, constant);
        if (successCanBeTrue)
        {
            outcomes.Add(new(AcquisitionResultState.Success, true));
        }
        if (successCanBeFalse)
        {
            outcomes.Add(new(AcquisitionResultState.Success, false));
        }
        if (failureCanBeTrue)
        {
            outcomes.Add(new(AcquisitionResultState.Failure, true));
        }
        if (failureCanBeFalse)
        {
            outcomes.Add(new(AcquisitionResultState.Failure, false));
        }
    }

    private static bool ComparisonCanBe(Terrum.CodeAnalysis.Lexing.TokenKind operation,
        long minimum, long maximum, long constant) =>
        operation switch {
            Terrum.CodeAnalysis.Lexing.TokenKind.EqualsEquals =>
                constant >= minimum && constant <= maximum,
            Terrum.CodeAnalysis.Lexing.TokenKind.BangEquals =>
                minimum != maximum || constant < minimum || constant > maximum,
            Terrum.CodeAnalysis.Lexing.TokenKind.Less => minimum < constant,
            Terrum.CodeAnalysis.Lexing.TokenKind.LessEquals => minimum <= constant,
            Terrum.CodeAnalysis.Lexing.TokenKind.Greater => maximum > constant,
            Terrum.CodeAnalysis.Lexing.TokenKind.GreaterEquals => maximum >= constant,
            _ => false,
        };

    private static Terrum.CodeAnalysis.Lexing.TokenKind ReverseTruth(
        Terrum.CodeAnalysis.Lexing.TokenKind operation) =>
        operation switch {
            Terrum.CodeAnalysis.Lexing.TokenKind.EqualsEquals => Terrum.CodeAnalysis.Lexing.TokenKind.BangEquals,
            Terrum.CodeAnalysis.Lexing.TokenKind.BangEquals => Terrum.CodeAnalysis.Lexing.TokenKind.EqualsEquals,
            Terrum.CodeAnalysis.Lexing.TokenKind.Less => Terrum.CodeAnalysis.Lexing.TokenKind.GreaterEquals,
            Terrum.CodeAnalysis.Lexing.TokenKind.LessEquals => Terrum.CodeAnalysis.Lexing.TokenKind.Greater,
            Terrum.CodeAnalysis.Lexing.TokenKind.Greater => Terrum.CodeAnalysis.Lexing.TokenKind.LessEquals,
            Terrum.CodeAnalysis.Lexing.TokenKind.GreaterEquals => Terrum.CodeAnalysis.Lexing.TokenKind.Less,
            _ => default,
        };

    private static ArgumentException Invalid(string? reason = null) =>
        new(reason is null
            ? "The closed COM call does not retain its catalogued owner acquisition and interface provenance."
            : $"The closed COM call does not retain its catalogued owner acquisition and interface provenance {reason}");
}
