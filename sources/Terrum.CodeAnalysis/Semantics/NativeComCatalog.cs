// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

[Flags]
internal enum NativeComEffects : byte
{
    None = 0,
    SignedHResult = 1,
    AcquiresReference = 2,
    OutputMayBeWrittenOnFailure = 4,
    ReleasesReference = 8,
}

internal enum NativeComParameterAccess : byte
{
    Read,
    Write,
    ReadWrite,
}

internal enum NativeComParameterRetention : byte
{
    NoEscape,
    MayEscape,
}

internal sealed record NativeComParameterDefinition(int ParameterIndex,
    NativeComParameterAccess Access, NativeComParameterRetention Retention, int Extent);

internal enum NativeComOwnerAcquisitionKind : byte
{
    NativeImportOutput,
    ComMethodOutput,
    DirectNativeImportOutput,
}

internal enum NativeComParameterAdaptationKind : byte
{
    PointerPreservingBaseInterface,
    SingleElementOwnerPointerArray,
    ValueAddress,
    ValueOutputAddress,
    ValueArrayAddress,
    AggregateAddress,
    AggregateStorageAddress,
    ComObjectBufferContents,
}

internal enum NativeComAbiValueKind : byte
{
    Signed32,
    Unsigned32,
    Signed64,
    Unsigned64,
    Float32,
    Float64,
    Pointer,
}

internal sealed record NativeComSignatureTypeDefinition(string Identity, NativeComAbiValueKind AbiValueKind);

internal sealed record NativeComInterfaceDefinition(
    string NominalIdentity,
    Guid SdkIid,
    string? ImmediateBaseIdentity,
    ImmutableArray<string> FlattenedMethods,
    ImmutableArray<string> DeclaredMethods = default)
{
    internal ImmutableArray<string> SignatureAliases { get; init; } = [];
}

internal sealed record NativeComOwnerDefinition(
    string BoundOwnerName,
    string InterfaceIdentity,
    NativeComOwnerAcquisitionKind AcquisitionKind,
    string AcquisitionIdentity,
    int OutputParameterIndex,
    bool IsUnique,
    int CellSize,
    int CellAlignment)
{
    internal ImmutableArray<int> AdditionalOutputParameterIndexes { get; init; } = [];
    internal ImmutableArray<string> AdditionalAcquisitionMethodIdentities { get; init; } = [];
    internal string ReleaseMethodIdentity { get; init; } = "";
    internal bool OutputMayBeWrittenOnFailure { get; init; }
}

internal sealed record NativeComAcquisitionSourceDefinition(
    int OutputParameterIndex,
    string OwnerTypeIdentity,
    string SourceMethodIdentity,
    int SourceOutputParameterIndex,
    bool OutputMayBeWrittenOnFailure);

internal sealed record NativeComParameterAdaptation(
    int ParameterIndex,
    string SourceType,
    string TargetType,
    NativeComParameterAdaptationKind Kind,
    int SourceSize = 0,
    int SourceAlignment = 0,
    bool NoEscape = false,
    string? BufferPointerMethodIdentity = null,
    string? BufferSizeMethodIdentity = null);

internal sealed record NativeComMappedBufferDefinition(
    int OutputParameterIndex,
    string SourceType,
    int PointerOffset,
    int LengthOffset,
    string UnmapMethodIdentity)
{
    internal ImmutableArray<NativeComMappedBufferReadParameter> ReadOnlyMapArguments { get; init; } = [];
}

internal sealed record NativeComMappedBufferReadParameter(int NativeParameterIndex, uint Value);

internal sealed record NativeComBoundParameterMapping(int BoundParameterIndex, int NativeParameterIndex)
{
    internal static bool HaveSameMappings(
        ImmutableArray<NativeComBoundParameterMapping> left,
        ImmutableArray<NativeComBoundParameterMapping> right) =>
        left.IsDefaultOrEmpty ? right.IsDefaultOrEmpty
            : !right.IsDefaultOrEmpty && left.SequenceEqual(right);
}

internal enum NativeComHiddenParameterKind : byte
{
    UInt32,
    NullPointer,
    InterfaceIdAddress,
}

internal sealed record NativeComHiddenParameter(int NativeParameterIndex,
    NativeComHiddenParameterKind Kind, uint Value = 0, string? InterfaceIdentity = null);

internal sealed record NativeComMethodDefinition(
    string Identity,
    string OwnerInterfaceIdentity,
    string DeclaringInterfaceIdentity,
    int Slot,
    string Signature,
    string Abi,
    NativeComEffects Effects,
    bool IsCallable,
    int OutputParameterIndex = -1,
    string? OutputInterfaceIdentity = null,
    ImmutableArray<NativeComParameterAdaptation> ParameterAdaptations = default)
{
    internal string VTableMethodIdentity { get; init; } = "";
    internal string? BoundMethodName { get; init; }
    internal bool IsNoThrow { get; init; }
    internal bool RequiresNativeParameterMetadata { get; init; }
    internal ImmutableArray<NativeComParameterDefinition> NativeParameters { get; init; } = [];
    internal ImmutableArray<int> BorrowedOwnerParameters { get; init; } = [];
    internal ImmutableArray<NativeComHiddenParameter> HiddenParameters { get; init; } = [];
    internal ImmutableArray<NativeStorageRecordParameterDefinition> StorageRecordParameters { get; init; } = [];
    internal NativeComMappedBufferDefinition? MappedBufferOutput { get; init; }
}

internal sealed record NativeComOutputDefinition(int ParameterIndex, string InterfaceIdentity, NativeComEffects Effects);

internal static class NativeComCatalog
{
    internal static bool IsValid(ImmutableArray<NativeComInterfaceDefinition> interfaces,
        ImmutableArray<NativeComOwnerDefinition> owners, ImmutableArray<NativeComMethodDefinition> methods,
        ImmutableArray<(string Source, string Target)> baseEdges, ImmutableArray<NativeImportDefinition> imports)
        => IsValid(interfaces, owners, methods, baseEdges, imports, [], out _);

    internal static bool IsValid(ImmutableArray<NativeComInterfaceDefinition> interfaces,
        ImmutableArray<NativeComOwnerDefinition> owners, ImmutableArray<NativeComMethodDefinition> methods,
        ImmutableArray<(string Source, string Target)> baseEdges, ImmutableArray<NativeImportDefinition> imports,
        out string failureReason)
        => IsValid(interfaces, owners, methods, baseEdges, imports, [], out failureReason);

    internal static bool IsValid(ImmutableArray<NativeComInterfaceDefinition> interfaces,
        ImmutableArray<NativeComOwnerDefinition> owners, ImmutableArray<NativeComMethodDefinition> methods,
        ImmutableArray<(string Source, string Target)> baseEdges, ImmutableArray<NativeImportDefinition> imports,
        ImmutableArray<NativeComSignatureTypeDefinition> signatureTypes, out string failureReason)
    {
        failureReason = "";
        if (interfaces.IsDefaultOrEmpty || owners.IsDefault || methods.IsDefault || baseEdges.IsDefault
            || imports.IsDefault || signatureTypes.IsDefault)
        {
            return Invalid(out failureReason, "The COM catalog arrays must be initialized.");
        }

        var signatureTypesByIdentity = new Dictionary<string, NativeComSignatureTypeDefinition>(StringComparer.Ordinal);
        foreach (var signatureType in signatureTypes)
        {
            if (signatureType is null || string.IsNullOrWhiteSpace(signatureType.Identity)
                || IsBuiltinSignatureType(signatureType.Identity)
                || signatureType.AbiValueKind is not (NativeComAbiValueKind.Signed32
                    or NativeComAbiValueKind.Unsigned32 or NativeComAbiValueKind.Signed64
                    or NativeComAbiValueKind.Unsigned64 or NativeComAbiValueKind.Float32
                    or NativeComAbiValueKind.Float64 or NativeComAbiValueKind.Pointer)
                || !signatureTypesByIdentity.TryAdd(signatureType.Identity, signatureType))
            {
                return Invalid(out failureReason, "A COM signature type identity or ABI classification is invalid.");
            }
        }

        var interfacesByIdentity = new Dictionary<string, NativeComInterfaceDefinition>(StringComparer.Ordinal);
        var interfaceIds = new HashSet<Guid>();
        foreach (var definition in interfaces)
        {
            if (definition is null || string.IsNullOrWhiteSpace(definition.NominalIdentity)
                || definition.SdkIid == Guid.Empty || definition.FlattenedMethods.IsDefault
                || definition.DeclaredMethods.IsDefault || definition.SignatureAliases.IsDefault
                || definition.FlattenedMethods.Any(string.IsNullOrWhiteSpace)
                || definition.DeclaredMethods.Any(string.IsNullOrWhiteSpace)
                || definition.SignatureAliases.Any(string.IsNullOrWhiteSpace)
                || definition.FlattenedMethods.Distinct(StringComparer.Ordinal).Count()
                    != definition.FlattenedMethods.Length
                || definition.DeclaredMethods.Distinct(StringComparer.Ordinal).Count()
                    != definition.DeclaredMethods.Length
                || !interfacesByIdentity.TryAdd(definition.NominalIdentity, definition)
                || !interfaceIds.Add(definition.SdkIid))
            {
                return Invalid(out failureReason, "A COM interface identity, IID, or method list is invalid.");
            }
        }

        var interfaceBySignatureIdentity = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var definition in interfaces)
        {
            if (!interfaceBySignatureIdentity.TryAdd(definition.NominalIdentity, definition.NominalIdentity))
            {
                return Invalid(out failureReason, "A COM signature interface identity is duplicated.");
            }
            foreach (var alias in definition.SignatureAliases)
            {
                if (!interfaceBySignatureIdentity.TryAdd(alias, definition.NominalIdentity))
                {
                    return Invalid(out failureReason, "A COM signature interface alias is ambiguous.");
                }
            }
        }

        var importsByKey = new Dictionary<string, NativeImportDefinition>(StringComparer.Ordinal);
        foreach (var import in imports)
        {
            if (import is null || string.IsNullOrWhiteSpace(import.Key) || import.ParameterTypes.IsDefault
                || (!import.ParameterContracts.IsDefaultOrEmpty
                    && import.ParameterContracts.Length != import.ParameterTypes.Length)
                || import.ComOutputs.IsDefault
                || !importsByKey.TryAdd(import.Key, import))
            {
                return Invalid(out failureReason, "A native import identity or parameter contract list is invalid.");
            }

            var outputIndexes = new HashSet<int>();
            foreach (var output in import.ComOutputs)
            {
                if (output is null || output.ParameterIndex < 0
                    || output.ParameterIndex >= import.ParameterTypes.Length
                    || string.IsNullOrWhiteSpace(output.InterfaceIdentity)
                    || !interfacesByIdentity.ContainsKey(output.InterfaceIdentity)
                    || !outputIndexes.Add(output.ParameterIndex)
                    || !output.Effects.HasFlag(NativeComEffects.AcquiresReference)
                    || (output.Effects & ~(NativeComEffects.SignedHResult | NativeComEffects.AcquiresReference
                        | NativeComEffects.OutputMayBeWrittenOnFailure)) != 0
                    || (output.Effects.HasFlag(NativeComEffects.OutputMayBeWrittenOnFailure)
                        && !output.Effects.HasFlag(NativeComEffects.SignedHResult))
                    || (output.Effects.HasFlag(NativeComEffects.SignedHResult)
                        && import.ReturnType != PrimitiveType.Int32)
                    || import.ParameterTypes[output.ParameterIndex] != PrimitiveType.NativeComInterfaceOutputAddress
                    || import.ParameterContracts.IsDefaultOrEmpty
                    || import.ParameterContracts[output.ParameterIndex]
                        != NativeImportParameterContract.OwnedNativeComInterfaceOutput)
                {
                    return Invalid(out failureReason, $"Native import '{import.Key}' has an invalid COM output.");
                }
            }
            for (var index = 0; index < import.ParameterTypes.Length; index++)
            {
                var hasComOutputContract = !import.ParameterContracts.IsDefaultOrEmpty
                    && import.ParameterContracts[index] == NativeImportParameterContract.OwnedNativeComInterfaceOutput;
                if (hasComOutputContract != outputIndexes.Contains(index))
                {
                    return Invalid(out failureReason,
                        $"Native import '{import.Key}' has an unpaired COM output contract.");
                }
            }
        }

        var expectedBaseEdges = new HashSet<(string Source, string Target)>();
        foreach (var definition in interfaces)
        {
            if (definition.ImmediateBaseIdentity is not string baseIdentity)
            {
                if (!definition.FlattenedMethods.SequenceEqual(definition.DeclaredMethods, StringComparer.Ordinal))
                {
                    return Invalid(out failureReason, "A root COM interface has an inconsistent declared method list.");
                }
                continue;
            }
            if (!interfacesByIdentity.TryGetValue(baseIdentity, out var baseDefinition)
                || !definition.FlattenedMethods.SequenceEqual(
                    baseDefinition.FlattenedMethods.Concat(definition.DeclaredMethods), StringComparer.Ordinal)
                || !expectedBaseEdges.Add((definition.NominalIdentity, baseIdentity)))
            {
                return Invalid(out failureReason, "COM inheritance is cyclic or its flattened slots are inconsistent.");
            }

            var ancestry = new HashSet<string>(StringComparer.Ordinal) { definition.NominalIdentity };
            while (baseDefinition.ImmediateBaseIdentity is string ancestor)
            {
                if (!ancestry.Add(ancestor) || !interfacesByIdentity.TryGetValue(ancestor, out baseDefinition))
                {
                    return Invalid(out failureReason, "COM inheritance contains a cycle or an unknown ancestor.");
                }
            }
        }
        var permittedBaseEdges = new HashSet<(string Source, string Target)>();
        if (baseEdges.Length > expectedBaseEdges.Count
            || baseEdges.Any(edge => !expectedBaseEdges.Contains(edge) || !permittedBaseEdges.Add(edge)))
        {
            return Invalid(out failureReason, "A permitted COM base edge is not a unique direct inheritance edge.");
        }

        var ownerNames = new HashSet<string>(StringComparer.Ordinal);
        var ownerInterfaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var owner in owners)
        {
            if (owner is null || string.IsNullOrWhiteSpace(owner.BoundOwnerName)
                || !ownerNames.Add(owner.BoundOwnerName)
                || string.IsNullOrWhiteSpace(owner.InterfaceIdentity)
                || !ownerInterfaces.Add(owner.InterfaceIdentity)
                || !interfacesByIdentity.ContainsKey(owner.InterfaceIdentity)
                || owner.AcquisitionKind is not (NativeComOwnerAcquisitionKind.NativeImportOutput
                    or NativeComOwnerAcquisitionKind.ComMethodOutput
                    or NativeComOwnerAcquisitionKind.DirectNativeImportOutput)
                || string.IsNullOrWhiteSpace(owner.AcquisitionIdentity)
                || owner.OutputParameterIndex < 0
                || (owner.AcquisitionKind == NativeComOwnerAcquisitionKind.ComMethodOutput
                    && owner.OutputParameterIndex < 1)
                || owner.AdditionalOutputParameterIndexes.IsDefault
                || owner.AdditionalOutputParameterIndexes.Contains(owner.OutputParameterIndex)
                || owner.AdditionalOutputParameterIndexes.Distinct().Count()
                    != owner.AdditionalOutputParameterIndexes.Length
                || (owner.AcquisitionKind != NativeComOwnerAcquisitionKind.NativeImportOutput
                    && !owner.AdditionalOutputParameterIndexes.IsEmpty)
                || !owner.IsUnique
                || owner.CellSize != NativeTargetProfile.PointerSize
                || owner.CellAlignment != NativeTargetProfile.PointerSize)
            {
                return Invalid(out failureReason, "A COM owner has an invalid identity, layout, or acquisition shape.");
            }
        }

        var methodIdentities = new HashSet<string>(StringComparer.Ordinal);
        var methodsByIdentity = new Dictionary<string, NativeComMethodDefinition>(StringComparer.Ordinal);
        foreach (var method in methods)
        {
            if (method is null || string.IsNullOrWhiteSpace(method.Identity)
                || !methodIdentities.Add(method.Identity)
                || method.BoundMethodName is not null
                    && (string.IsNullOrWhiteSpace(method.BoundMethodName)
                        || method.BoundMethodName.Contains(".", StringComparison.Ordinal)
                        || method.BoundMethodName.Contains("::", StringComparison.Ordinal)))
            {
                return Invalid(out failureReason, "A COM method identity is missing or duplicated.");
            }
            if (string.IsNullOrWhiteSpace(method.Signature)
                || !TryParseSignature(method.Signature, out var returnType, out var parameters))
            {
                return Invalid(out failureReason, $"COM method '{method.Identity}' has an invalid signature.");
            }
            if (!MatchesAbi(method.Abi, returnType, parameters, signatureTypesByIdentity))
            {
                return Invalid(out failureReason, $"COM method '{method.Identity}' has ABI '{method.Abi}' inconsistent with '{method.Signature}'.");
            }
            if ((method.Effects & ~(NativeComEffects.SignedHResult | NativeComEffects.AcquiresReference
                    | NativeComEffects.OutputMayBeWrittenOnFailure | NativeComEffects.ReleasesReference)) != 0
                || method.Effects.HasFlag(NativeComEffects.SignedHResult) != (returnType == "HRESULT")
                || (method.Effects.HasFlag(NativeComEffects.OutputMayBeWrittenOnFailure)
                    && !method.Effects.HasFlag(NativeComEffects.SignedHResult)))
            {
                return Invalid(out failureReason, $"COM method '{method.Identity}' has unknown effect flags.");
            }
            if (method.NativeParameters.IsDefault
                || method.NativeParameters.Any(static parameter => parameter is null
                    || parameter.ParameterIndex < 1 || parameter.Extent < 0
                    || parameter.Access is not (NativeComParameterAccess.Read
                        or NativeComParameterAccess.Write or NativeComParameterAccess.ReadWrite)
                    || parameter.Retention is not (NativeComParameterRetention.NoEscape
                        or NativeComParameterRetention.MayEscape))
                || method.NativeParameters.Select(static parameter => parameter.ParameterIndex)
                    .Distinct().Count() != method.NativeParameters.Length)
            {
                return Invalid(out failureReason, $"COM method '{method.Identity}' has invalid native parameter metadata.");
            }
            foreach (var nativeParameter in method.NativeParameters)
            {
                if (nativeParameter.ParameterIndex >= parameters.Length
                    || !parameters[nativeParameter.ParameterIndex].EndsWith("*", StringComparison.Ordinal))
                {
                    return Invalid(out failureReason,
                        $"COM method '{method.Identity}' parameter metadata does not match a pointer parameter.");
                }
            }
            if (method.RequiresNativeParameterMetadata
                && Enumerable.Range(1, Math.Max(0, parameters.Length - 1))
                    .Any(index => parameters[index].EndsWith("*", StringComparison.Ordinal)
                        && !method.NativeParameters.Any(parameter => parameter.ParameterIndex == index)))
            {
                return Invalid(out failureReason,
                    $"COM method '{method.Identity}' is missing pointer access or retention metadata.");
            }
            if (!interfacesByIdentity.TryGetValue(method.OwnerInterfaceIdentity, out var ownerInterface)
                || !interfacesByIdentity.TryGetValue(method.DeclaringInterfaceIdentity, out var declaringInterface))
            {
                return Invalid(out failureReason, $"COM method '{method.Identity}' refers to an unknown interface.");
            }
            if (method.HiddenParameters.IsDefault
                || method.HiddenParameters.Any(static parameter => parameter is null)
                || method.HiddenParameters.Select(static parameter => parameter.NativeParameterIndex)
                    .Distinct().Count() != method.HiddenParameters.Length)
            {
                return Invalid(out failureReason,
                    $"COM method '{method.Identity}' has invalid hidden-parameter metadata.");
            }
            foreach (var hiddenParameter in method.HiddenParameters)
            {
                var hiddenIndex = hiddenParameter.NativeParameterIndex;
                var hiddenType = hiddenIndex >= 0 && hiddenIndex < parameters.Length
                    ? parameters[hiddenIndex] : "";
                var validHiddenParameter = hiddenIndex > 0
                    && hiddenIndex < parameters.Length
                    && hiddenIndex != method.OutputParameterIndex
                    && !method.BorrowedOwnerParameters.IsDefault
                    && !method.BorrowedOwnerParameters.Contains(hiddenIndex)
                    && (method.ParameterAdaptations.IsDefaultOrEmpty
                        || !method.ParameterAdaptations.Any(adaptation =>
                            adaptation.ParameterIndex == hiddenIndex))
                    && (hiddenParameter.Kind switch {
                        NativeComHiddenParameterKind.UInt32 =>
                            hiddenParameter.InterfaceIdentity is null
                            && hiddenType is "UINT" or "uint32" or "ULONG",
                        NativeComHiddenParameterKind.NullPointer =>
                            hiddenParameter.Value == 0 && hiddenParameter.InterfaceIdentity is null
                            && (hiddenType.EndsWith("*", StringComparison.Ordinal)
                                || hiddenType is "LPVOID" or "REFIID"),
                        NativeComHiddenParameterKind.InterfaceIdAddress =>
                            hiddenParameter.Value == 0 && hiddenType == "REFIID"
                            && hiddenParameter.InterfaceIdentity is string hiddenInterface
                            && interfacesByIdentity.ContainsKey(hiddenInterface),
                        _ => false,
                    });
                if (!validHiddenParameter)
                {
                    return Invalid(out failureReason,
                        $"COM method '{method.Identity}' has an invalid hidden parameter.");
                }
            }
            if (!IsAssignable(method.OwnerInterfaceIdentity, method.DeclaringInterfaceIdentity,
                    interfacesByIdentity)
                || method.Slot < 0 || method.Slot >= ownerInterface.FlattenedMethods.Length
                || method.Slot >= declaringInterface.FlattenedMethods.Length
                || string.IsNullOrWhiteSpace(method.VTableMethodIdentity)
                || ownerInterface.FlattenedMethods[method.Slot] != method.VTableMethodIdentity
                || declaringInterface.FlattenedMethods[method.Slot] != method.VTableMethodIdentity)
            {
                return Invalid(out failureReason, $"COM method '{method.Identity}' has an invalid owner, slot, or vtable method.");
            }
            if (parameters.Length == 0 || !parameters[0].EndsWith("*", StringComparison.Ordinal)
                || parameters[0].EndsWith("**", StringComparison.Ordinal))
            {
                return Invalid(out failureReason, $"COM method '{method.Identity}' has an invalid receiver parameter.");
            }
            var receiverIdentity = parameters[0][..^1].Trim();
            if (!interfaceBySignatureIdentity.TryGetValue(receiverIdentity, out var resolvedReceiverIdentity)
                || !IsAssignable(method.OwnerInterfaceIdentity, resolvedReceiverIdentity, interfacesByIdentity))
            {
                return Invalid(out failureReason,
                    $"COM method '{method.Identity}' has a receiver unrelated to its owner interface.");
            }
            if (method.BorrowedOwnerParameters.IsDefault
                || method.BorrowedOwnerParameters.Distinct().Count() != method.BorrowedOwnerParameters.Length)
            {
                return Invalid(out failureReason,
                    $"COM method '{method.Identity}' has invalid borrowed-owner parameter metadata.");
            }
            foreach (var parameterIndex in method.BorrowedOwnerParameters)
            {
                if (parameterIndex < 1 || parameterIndex >= parameters.Length
                    || parameterIndex == method.OutputParameterIndex
                    || !parameters[parameterIndex].EndsWith("*", StringComparison.Ordinal)
                    || parameters[parameterIndex].EndsWith("**", StringComparison.Ordinal))
                {
                    return Invalid(out failureReason,
                        $"COM method '{method.Identity}' has an invalid borrowed-owner parameter.");
                }
                var parameterIdentity = parameters[parameterIndex][..^1].Trim();
                var hasOwnedInterfaceMapping = interfaceBySignatureIdentity.TryGetValue(
                    parameterIdentity, out var borrowedIdentity)
                    && owners.Any(owner => owner.InterfaceIdentity == borrowedIdentity);
                var adaptation = method.ParameterAdaptations.IsDefaultOrEmpty
                    ? null : method.ParameterAdaptations.FirstOrDefault(item =>
                        item?.ParameterIndex == parameterIndex);
                var hasOwnedAdaptationSource = adaptation is not null
                    && !string.IsNullOrWhiteSpace(adaptation.SourceType)
                    && adaptation.SourceType.EndsWith("*", StringComparison.Ordinal)
                    && interfaceBySignatureIdentity.TryGetValue(
                        adaptation.SourceType[..^1].Trim(), out var sourceIdentity)
                    && owners.Any(owner => owner.InterfaceIdentity == sourceIdentity);
                if (!hasOwnedInterfaceMapping && !hasOwnedAdaptationSource)
                {
                    return Invalid(out failureReason,
                        $"COM method '{method.Identity}' borrows a parameter without an owned interface mapping.");
                }
            }

            var declaredMethodIndex = declaringInterface.DeclaredMethods.IndexOf(method.VTableMethodIdentity);
            var firstDeclaredSlot = declaringInterface.FlattenedMethods.Length
                - declaringInterface.DeclaredMethods.Length;
            if (declaredMethodIndex < 0 || method.Slot != firstDeclaredSlot + declaredMethodIndex)
            {
                return Invalid(out failureReason, $"COM method '{method.Identity}' is not declared at its recorded vtable slot.");
            }

            if (method.OutputParameterIndex >= 0)
            {
                if (method.OutputParameterIndex < 1 || method.OutputParameterIndex >= parameters.Length)
                {
                    return Invalid(out failureReason, $"COM method '{method.Identity}' has invalid output metadata.");
                }
                var outputType = parameters[method.OutputParameterIndex];
                var hasTypedOutput = outputType.EndsWith("**", StringComparison.Ordinal)
                    && interfaceBySignatureIdentity.TryGetValue(outputType[..^2].Trim(),
                        out var resolvedOutputInterface)
                    && resolvedOutputInterface == method.OutputInterfaceIdentity;
                if (!method.Effects.HasFlag(NativeComEffects.AcquiresReference)
                    || method.OutputInterfaceIdentity is not string outputInterface
                    || !interfacesByIdentity.ContainsKey(outputInterface)
                    || (outputType != "void**" && !hasTypedOutput)
                    || method.RequiresNativeParameterMetadata
                        && (!method.Effects.HasFlag(NativeComEffects.SignedHResult)
                            || !method.NativeParameters.Any(parameter =>
                                parameter.ParameterIndex == method.OutputParameterIndex
                                && (parameter.Access is NativeComParameterAccess.Write
                                    or NativeComParameterAccess.ReadWrite)
                                && parameter.Retention == NativeComParameterRetention.NoEscape)))
                {
                    return Invalid(out failureReason, $"COM method '{method.Identity}' has invalid output metadata.");
                }
            }
            else if (method.OutputParameterIndex != -1 || method.OutputInterfaceIdentity is not null)
            {
                return Invalid(out failureReason, $"COM method '{method.Identity}' has inconsistent output metadata.");
            }

            if (!method.ParameterAdaptations.IsDefault)
            {
                var adaptedParameters = new HashSet<int>();
                var aggregateStorageAddressCount = 0;
                foreach (var adaptation in method.ParameterAdaptations)
                {
                    if (adaptation is null || adaptation.ParameterIndex < 1
                        || adaptation.ParameterIndex >= parameters.Length
                        || !adaptedParameters.Add(adaptation.ParameterIndex)
                        || string.IsNullOrWhiteSpace(adaptation.SourceType)
                        || string.IsNullOrWhiteSpace(adaptation.TargetType)
                        || (adaptation.Kind is not (NativeComParameterAdaptationKind.AggregateAddress
                            or NativeComParameterAdaptationKind.AggregateStorageAddress)
                            && (adaptation.SourceSize != 0 || adaptation.SourceAlignment != 0))
                        || (adaptation.Kind != NativeComParameterAdaptationKind.ComObjectBufferContents
                            && (adaptation.BufferPointerMethodIdentity is not null
                                || adaptation.BufferSizeMethodIdentity is not null))
                        || adaptation.NoEscape
                            != (adaptation.Kind is NativeComParameterAdaptationKind.AggregateStorageAddress
                                or NativeComParameterAdaptationKind.ValueOutputAddress)
                        || parameters[adaptation.ParameterIndex] != adaptation.TargetType)
                    {
                        return Invalid(out failureReason, $"COM method '{method.Identity}' has an invalid parameter adaptation.");
                    }
                    if (adaptation.Kind == NativeComParameterAdaptationKind.ValueAddress)
                    {
                        if (method.BorrowedOwnerParameters.Contains(adaptation.ParameterIndex)
                            || !IsValidValueAddressAdaptation(adaptation, signatureTypesByIdentity))
                        {
                            return Invalid(out failureReason,
                                $"COM method '{method.Identity}' has an invalid value-address adaptation.");
                        }
                        continue;
                    }
                    if (adaptation.Kind == NativeComParameterAdaptationKind.ValueOutputAddress)
                    {
                        if (method.BorrowedOwnerParameters.Contains(adaptation.ParameterIndex)
                            || !IsValidValueOutputAddressAdaptation(adaptation, signatureTypesByIdentity))
                        {
                            return Invalid(out failureReason,
                                $"COM method '{method.Identity}' has an invalid value-output-address adaptation.");
                        }
                        continue;
                    }
                    if (adaptation.Kind == NativeComParameterAdaptationKind.ValueArrayAddress)
                    {
                        if (method.BorrowedOwnerParameters.Contains(adaptation.ParameterIndex)
                            || !IsValidValueArrayAddressAdaptation(adaptation, signatureTypesByIdentity))
                        {
                            return Invalid(out failureReason,
                                $"COM method '{method.Identity}' has an invalid value-array-address adaptation.");
                        }
                        continue;
                    }
                    if (adaptation.Kind == NativeComParameterAdaptationKind.AggregateAddress)
                    {
                        if (method.BorrowedOwnerParameters.Contains(adaptation.ParameterIndex)
                            || !IsValidAggregateAddressAdaptation(adaptation))
                        {
                            return Invalid(out failureReason,
                                $"COM method '{method.Identity}' has an invalid aggregate-address adaptation.");
                        }
                        continue;
                    }
                    if (adaptation.Kind == NativeComParameterAdaptationKind.AggregateStorageAddress)
                    {
                        if (++aggregateStorageAddressCount > 1
                            || method.BorrowedOwnerParameters.Contains(adaptation.ParameterIndex)
                            || !IsValidAggregateStorageAddressAdaptation(adaptation))
                        {
                            return Invalid(out failureReason,
                                $"COM method '{method.Identity}' has an invalid aggregate-storage address adaptation.");
                        }
                        continue;
                    }
                    if (!method.BorrowedOwnerParameters.Contains(adaptation.ParameterIndex)
                        || !adaptation.SourceType.EndsWith("*", StringComparison.Ordinal)
                        || adaptation.SourceType.EndsWith("**", StringComparison.Ordinal))
                    {
                        return Invalid(out failureReason,
                            $"COM method '{method.Identity}' adapts a parameter without a borrowed owner.");
                    }
                    var sourceSignatureIdentity = adaptation.SourceType[..^1].Trim();
                    if (!interfaceBySignatureIdentity.TryGetValue(
                            sourceSignatureIdentity, out var sourceIdentity)
                        || !owners.Any(owner => owner.InterfaceIdentity == sourceIdentity))
                    {
                        return Invalid(out failureReason,
                            $"COM method '{method.Identity}' adapts a parameter without an owned source interface.");
                    }
                    var validAdaptation = adaptation.Kind switch {
                        NativeComParameterAdaptationKind.PointerPreservingBaseInterface =>
                            adaptation.TargetType.EndsWith("*", StringComparison.Ordinal)
                            && !adaptation.TargetType.EndsWith("**", StringComparison.Ordinal)
                            && interfaceBySignatureIdentity.TryGetValue(
                                adaptation.TargetType[..^1].Trim(), out var targetInterfaceIdentity)
                            && permittedBaseEdges.Contains((sourceIdentity, targetInterfaceIdentity))
                            && interfacesByIdentity[sourceIdentity].ImmediateBaseIdentity
                                == targetInterfaceIdentity,
                        NativeComParameterAdaptationKind.SingleElementOwnerPointerArray =>
                            adaptation.TargetType == sourceSignatureIdentity + "*const*",
                        NativeComParameterAdaptationKind.ComObjectBufferContents =>
                            adaptation.TargetType == "const void*"
                            && !string.IsNullOrWhiteSpace(adaptation.BufferPointerMethodIdentity)
                            && !string.IsNullOrWhiteSpace(adaptation.BufferSizeMethodIdentity)
                            && adaptation.ParameterIndex + 1 < parameters.Length
                            && parameters[adaptation.ParameterIndex + 1] == "SIZE_T"
                            && method.OutputParameterIndex != adaptation.ParameterIndex + 1
                            && !method.ParameterAdaptations.Any(other =>
                                other.ParameterIndex == adaptation.ParameterIndex + 1)
                            && (method.HiddenParameters.IsDefaultOrEmpty
                                || !method.HiddenParameters.Any(hidden =>
                                    hidden.NativeParameterIndex == adaptation.ParameterIndex + 1))
                            && !method.BorrowedOwnerParameters.Contains(adaptation.ParameterIndex + 1),
                        _ => false,
                    };
                    if (!validAdaptation)
                    {
                        return Invalid(out failureReason,
                            $"COM method '{method.Identity}' has an invalid parameter adaptation.");
                    }
                }
            }

            methodsByIdentity.Add(method.Identity, method);
        }

        foreach (var group in methodsByIdentity.Values.GroupBy(
            static method => (method.DeclaringInterfaceIdentity, method.VTableMethodIdentity)))
        {
            var first = group.First();
            if (group.Skip(1).Any(method => !HaveCompatibleVTableSignature(
                first, method, signatureTypesByIdentity)))
            {
                return Invalid(out failureReason,
                    $"COM method '{first.VTableMethodIdentity}' has conflicting signatures.");
            }
        }

        foreach (var method in methodsByIdentity.Values)
        {
            if (method.ParameterAdaptations.IsDefaultOrEmpty)
            {
                continue;
            }
            foreach (var adaptation in method.ParameterAdaptations.Where(static adaptation =>
                adaptation.Kind == NativeComParameterAdaptationKind.ComObjectBufferContents))
            {
                if (!interfaceBySignatureIdentity.TryGetValue(
                        adaptation.SourceType[..^1].Trim(), out var sourceInterface)
                    || adaptation.BufferPointerMethodIdentity is not string pointerIdentity
                    || adaptation.BufferSizeMethodIdentity is not string sizeIdentity
                    || !IsValidBufferContentsAccessor(pointerIdentity, "LPVOID", sourceInterface,
                        methodsByIdentity, interfaceBySignatureIdentity, signatureTypesByIdentity)
                    || !IsValidBufferContentsAccessor(sizeIdentity, "SIZE_T", sourceInterface,
                        methodsByIdentity, interfaceBySignatureIdentity, signatureTypesByIdentity))
                {
                    return Invalid(out failureReason,
                        $"COM method '{method.Identity}' has invalid buffer-content accessor metadata.");
                }
            }
        }

        var mappedBufferTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in methodsByIdentity.Values.Where(static method => method.MappedBufferOutput is not null))
        {
            var mappedBuffer = method.MappedBufferOutput!;
            var adaptation = method.ParameterAdaptations.IsDefaultOrEmpty
                ? null : method.ParameterAdaptations.SingleOrDefault(
                    item => item.ParameterIndex == mappedBuffer.OutputParameterIndex);
            if (string.IsNullOrWhiteSpace(mappedBuffer.SourceType)
                || string.IsNullOrWhiteSpace(mappedBuffer.UnmapMethodIdentity)
                || mappedBuffer.ReadOnlyMapArguments.IsDefaultOrEmpty
                || mappedBuffer.ReadOnlyMapArguments.Any(static parameter => parameter is null)
                || mappedBuffer.ReadOnlyMapArguments.Select(static parameter => parameter.NativeParameterIndex)
                    .Distinct().Count() != mappedBuffer.ReadOnlyMapArguments.Length
                || !mappedBufferTypes.Add(mappedBuffer.SourceType)
                || mappedBuffer.OutputParameterIndex <= 0
                || adaptation is not {
                    Kind: NativeComParameterAdaptationKind.AggregateStorageAddress,
                    NoEscape: true,
                }
                || adaptation.SourceType != mappedBuffer.SourceType
                || mappedBuffer.PointerOffset < 0
                || mappedBuffer.PointerOffset % NativeTargetProfile.PointerSize != 0
                || mappedBuffer.PointerOffset > adaptation.SourceSize - NativeTargetProfile.PointerSize
                || mappedBuffer.LengthOffset < sizeof(uint)
                || mappedBuffer.LengthOffset % sizeof(uint) != 0
                || mappedBuffer.LengthOffset > adaptation.SourceSize - sizeof(uint)
                || mappedBuffer.LengthOffset < mappedBuffer.PointerOffset + NativeTargetProfile.PointerSize
                    && mappedBuffer.LengthOffset + sizeof(uint) > mappedBuffer.PointerOffset
                || !methodsByIdentity.TryGetValue(mappedBuffer.UnmapMethodIdentity, out var unmap)
                || !unmap.IsCallable
                || unmap.OwnerInterfaceIdentity != method.OwnerInterfaceIdentity
                || unmap.DeclaringInterfaceIdentity != method.DeclaringInterfaceIdentity
                || unmap.Effects != NativeComEffects.None
                || !TryParseSignature(unmap.Signature, out var unmapReturn, out _)
                || unmapReturn != "void"
                || !TryParseSignature(method.Signature, out var mapReturn, out var mapParameters)
                || mapReturn != "HRESULT"
                || mappedBuffer.OutputParameterIndex >= mapParameters.Length
                || mapParameters[mappedBuffer.OutputParameterIndex] != mappedBuffer.SourceType + "*"
                || method.VTableMethodIdentity != method.DeclaringInterfaceIdentity + "::Map"
                || unmap.VTableMethodIdentity != unmap.DeclaringInterfaceIdentity + "::Unmap"
                || mappedBuffer.ReadOnlyMapArguments.Any(parameter =>
                    parameter.NativeParameterIndex <= 0
                    || parameter.NativeParameterIndex >= mapParameters.Length
                    || parameter.NativeParameterIndex == mappedBuffer.OutputParameterIndex
                    || !TryGetAbiValueKind(mapParameters[parameter.NativeParameterIndex],
                        signatureTypesByIdentity, allowVoid: false, isParameter: true, out var parameterKind)
                    || parameterKind != NativeComAbiValueKind.Unsigned32))
            {
                return Invalid(out failureReason,
                    $"COM method '{method.Identity}' has invalid mapped-buffer metadata.");
            }
        }

        foreach (var owner in owners)
        {
            if (owner.AdditionalAcquisitionMethodIdentities.IsDefault
                || owner.AdditionalAcquisitionMethodIdentities.Any(string.IsNullOrWhiteSpace)
                || owner.AdditionalAcquisitionMethodIdentities.Distinct(StringComparer.Ordinal).Count()
                    != owner.AdditionalAcquisitionMethodIdentities.Length)
            {
                return Invalid(out failureReason, $"COM owner '{owner.BoundOwnerName}' has invalid additional acquisitions.");
            }
            if (owner.AcquisitionKind == NativeComOwnerAcquisitionKind.NativeImportOutput)
            {
                if (!importsByKey.TryGetValue(owner.AcquisitionIdentity, out var import)
                    || owner.OutputParameterIndex >= import.ParameterTypes.Length
                    || !import.ComOutputs.Any(output => output.ParameterIndex == owner.OutputParameterIndex
                        && output.InterfaceIdentity == owner.InterfaceIdentity
                        && output.Effects.HasFlag(NativeComEffects.AcquiresReference))
                    || owner.AdditionalOutputParameterIndexes.Any(index =>
                        index < 0 || index >= import.ParameterTypes.Length
                        || !import.ComOutputs.Any(output => output.ParameterIndex == index
                            && output.InterfaceIdentity == owner.InterfaceIdentity
                            && output.Effects.HasFlag(NativeComEffects.AcquiresReference))))
                {
                    return Invalid(out failureReason, $"COM owner '{owner.BoundOwnerName}' does not match a native import output.");
                }
            }
            else if (owner.AcquisitionKind == NativeComOwnerAcquisitionKind.ComMethodOutput
                && (!methodsByIdentity.TryGetValue(owner.AcquisitionIdentity, out var method)
                || method.OutputParameterIndex != owner.OutputParameterIndex
                || method.OutputInterfaceIdentity != owner.InterfaceIdentity
                || !method.Effects.HasFlag(NativeComEffects.AcquiresReference)))
            {
                return Invalid(out failureReason, $"COM owner '{owner.BoundOwnerName}' does not match a COM method output.");
            }
            foreach (var additionalAcquisition in owner.AdditionalAcquisitionMethodIdentities)
            {
                if (!methodsByIdentity.TryGetValue(additionalAcquisition, out var additionalMethod)
                    || !additionalMethod.IsCallable
                    || !additionalMethod.Effects.HasFlag(NativeComEffects.AcquiresReference)
                    || additionalMethod.OutputParameterIndex >= 0
                        && (additionalMethod.OutputInterfaceIdentity != owner.InterfaceIdentity
                            || additionalMethod.OutputParameterIndex >=
                                (TryParseSignature(additionalMethod.Signature, out _, out var parameters)
                                    ? parameters.Length : 0))
                    || additionalMethod.OutputParameterIndex < 0
                        && additionalMethod.OwnerInterfaceIdentity != owner.InterfaceIdentity)
                {
                    return Invalid(out failureReason,
                        $"COM owner '{owner.BoundOwnerName}' has an invalid additional method acquisition.");
                }
            }
            if (owner.ReleaseMethodIdentity.Length > 0
                && (!methodsByIdentity.TryGetValue(owner.ReleaseMethodIdentity, out var releaseMethod)
                    || !IsValidOwnerRelease(owner, releaseMethod, interfacesByIdentity)))
            {
                return Invalid(out failureReason,
                    $"COM owner '{owner.BoundOwnerName}' has an invalid release method.");
            }
        }

        return true;
    }

    private static bool IsValidOwnerRelease(NativeComOwnerDefinition owner,
        NativeComMethodDefinition method,
        IReadOnlyDictionary<string, NativeComInterfaceDefinition> interfaces)
    {
        if (!method.IsCallable || !IsAssignable(owner.InterfaceIdentity, method.OwnerInterfaceIdentity, interfaces)
            || method.OutputParameterIndex != -1 || method.OutputInterfaceIdentity is not null
            || method.Effects != NativeComEffects.ReleasesReference
            || !TryParseSignature(method.Signature, out var returnType, out var parameters)
            || returnType is not ("ULONG" or "uint32") || parameters.Length != 1
            || !interfaces.TryGetValue(owner.InterfaceIdentity, out var definition))
        {
            return false;
        }

        var pointerType = method.OwnerInterfaceIdentity + "*";
        return parameters[0] == pointerType
            || interfaces[method.OwnerInterfaceIdentity].SignatureAliases.Any(alias =>
                parameters[0] == alias + "*");
    }

    internal static bool IsAssignable(string sourceIdentity, string targetIdentity,
        IReadOnlyDictionary<string, NativeComInterfaceDefinition> interfaces)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(sourceIdentity) && interfaces.TryGetValue(sourceIdentity, out var definition))
        {
            if (sourceIdentity == targetIdentity)
            {
                return true;
            }
            if (definition.ImmediateBaseIdentity is not string baseIdentity)
            {
                return false;
            }
            sourceIdentity = baseIdentity;
        }

        return false;
    }

    internal static bool IsPermittedBaseEdge(string sourceIdentity, string targetIdentity,
        ImmutableArray<NativeComInterfaceDefinition> interfaces,
        ImmutableArray<(string Source, string Target)> permittedBaseEdges) =>
        permittedBaseEdges.Contains((sourceIdentity, targetIdentity))
        && interfaces.Any(definition => definition.NominalIdentity == sourceIdentity
            && definition.ImmediateBaseIdentity == targetIdentity);

    internal static bool TryParseSignature(string signature, out string returnType, out string[] parameters)
    {
        var openParenthesis = signature.IndexOf('(');
        if (openParenthesis <= 0 || !signature.EndsWith(")", StringComparison.Ordinal)
            || signature.IndexOf('(', openParenthesis + 1) >= 0
            || signature.IndexOf(')') != signature.Length - 1)
        {
            returnType = "";
            parameters = [];
            return false;
        }

        returnType = signature[..openParenthesis].Trim();
        var parameterText = signature[(openParenthesis + 1)..^1];
        if (returnType.Length == 0)
        {
            parameters = [];
            return false;
        }
        if (parameterText.Length == 0)
        {
            parameters = [];
            return true;
        }

        parameters = parameterText.Split(',').Select(static parameter => parameter.Trim()).ToArray();
        return parameters.All(static parameter => parameter.Length != 0);
    }

    internal static bool TryCreateAbi(string signature,
        ImmutableArray<NativeComSignatureTypeDefinition> signatureTypes, out string abi)
    {
        abi = "";
        if (signatureTypes.IsDefault
            || !TryParseSignature(signature, out var returnType, out var parameters))
        {
            return false;
        }

        var signatureTypesByIdentity =
            new Dictionary<string, NativeComSignatureTypeDefinition>(StringComparer.Ordinal);
        foreach (var definition in signatureTypes)
        {
            if (definition is null || string.IsNullOrWhiteSpace(definition.Identity)
                || !signatureTypesByIdentity.TryAdd(definition.Identity, definition))
            {
                return false;
            }
        }

        if (!TryGetAbiValueKind(returnType, signatureTypesByIdentity, allowVoid: true,
                isParameter: false, out var returnKind))
        {
            return false;
        }

        var locations = new string[parameters.Length];
        for (var index = 0; index < parameters.Length; index++)
        {
            if (!TryGetAbiValueKind(parameters[index], signatureTypesByIdentity,
                    allowVoid: false, isParameter: true, out var kind)
                || kind is not NativeComAbiValueKind valueKind)
            {
                return false;
            }

            locations[index] = index switch {
                0 => ArgumentLocation(valueKind, "rcx", "ecx", "xmm0"),
                1 => ArgumentLocation(valueKind, "rdx", "edx", "xmm1"),
                2 => ArgumentLocation(valueKind, "r8", "r8d", "xmm2"),
                3 => ArgumentLocation(valueKind, "r9", "r9d", "xmm3"),
                _ => $"stack{32 + (index - 4) * sizeof(long)}",
            };
        }

        var resultLocation = returnKind switch {
            null => "void",
            NativeComAbiValueKind.Signed32 or NativeComAbiValueKind.Unsigned32 => "eax",
            NativeComAbiValueKind.Signed64 or NativeComAbiValueKind.Unsigned64
                or NativeComAbiValueKind.Pointer => "rax",
            NativeComAbiValueKind.Float32 or NativeComAbiValueKind.Float64 => "xmm0",
            _ => "",
        };
        if (resultLocation.Length == 0)
        {
            return false;
        }

        abi = "windows-x64-" + string.Join("-", locations.Append(resultLocation));
        return true;
    }

    private static string ArgumentLocation(NativeComAbiValueKind kind,
        string register, string narrowRegister, string floatingRegister) =>
        kind switch {
            NativeComAbiValueKind.Signed32 or NativeComAbiValueKind.Unsigned32 => narrowRegister,
            NativeComAbiValueKind.Float32 or NativeComAbiValueKind.Float64 => floatingRegister,
            _ => register,
        };

    private static bool IsValidValueAddressAdaptation(NativeComParameterAdaptation adaptation,
        IReadOnlyDictionary<string, NativeComSignatureTypeDefinition> signatureTypes)
    {
        const string constPrefix = "const ";
        if (!adaptation.TargetType.StartsWith(constPrefix, StringComparison.Ordinal)
            || !adaptation.TargetType.EndsWith("*", StringComparison.Ordinal)
            || adaptation.TargetType.EndsWith("**", StringComparison.Ordinal)
            || !TryGetAbiValueKind(adaptation.SourceType, signatureTypes, allowVoid: false,
                isParameter: false, out var sourceKind)
            || sourceKind is not NativeComAbiValueKind sourceValueKind
            || sourceValueKind == NativeComAbiValueKind.Pointer)
        {
            return false;
        }
        var targetValueType = adaptation.TargetType[constPrefix.Length..^1].Trim();
        return TryGetAbiValueKind(targetValueType, signatureTypes, allowVoid: false,
                isParameter: false, out var targetKind)
            && targetKind == sourceValueKind;
    }

    private static bool IsValidValueOutputAddressAdaptation(NativeComParameterAdaptation adaptation,
        IReadOnlyDictionary<string, NativeComSignatureTypeDefinition> signatureTypes)
    {
        if (!adaptation.NoEscape
            || !adaptation.TargetType.EndsWith("*", StringComparison.Ordinal)
            || adaptation.TargetType.EndsWith("**", StringComparison.Ordinal)
            || adaptation.TargetType.StartsWith("const ", StringComparison.Ordinal)
            || !TryGetAbiValueKind(adaptation.SourceType, signatureTypes, allowVoid: false,
                isParameter: false, out var sourceKind)
            || sourceKind is not NativeComAbiValueKind sourceValueKind
            || sourceValueKind == NativeComAbiValueKind.Pointer)
        {
            return false;
        }
        var targetValueType = adaptation.TargetType[..^1].Trim();
        return !targetValueType.StartsWith("const ", StringComparison.Ordinal)
            && TryGetAbiValueKind(targetValueType, signatureTypes, allowVoid: false,
                isParameter: false, out var targetKind)
            && targetKind == sourceValueKind;
    }

    private static bool IsValidValueArrayAddressAdaptation(NativeComParameterAdaptation adaptation,
        IReadOnlyDictionary<string, NativeComSignatureTypeDefinition> signatureTypes)
    {
        if (!TryGetValueArrayElementCount(adaptation.TargetType, out var elementCount)
            || !TryGetValueArrayElementType(adaptation.TargetType, out var targetElementType)
            || !TryGetAbiValueKind(adaptation.SourceType, signatureTypes, allowVoid: false,
                isParameter: false, out var sourceKind)
            || sourceKind is not NativeComAbiValueKind sourceValueKind
            || sourceValueKind == NativeComAbiValueKind.Pointer
            || !TryGetAbiValueKind(targetElementType, signatureTypes, allowVoid: false,
                isParameter: false, out var targetKind))
        {
            return false;
        }
        return elementCount > 0 && targetKind == sourceValueKind;
    }

    private static bool IsValidBufferContentsAccessor(string identity, string expectedReturnType,
        string sourceInterface,
        IReadOnlyDictionary<string, NativeComMethodDefinition> methods,
        IReadOnlyDictionary<string, string> interfaceBySignatureIdentity,
        IReadOnlyDictionary<string, NativeComSignatureTypeDefinition> signatureTypes)
    {
        if (!methods.TryGetValue(identity, out var accessor)
            || !accessor.IsCallable || accessor.OwnerInterfaceIdentity != sourceInterface
            || accessor.Effects != NativeComEffects.None || accessor.OutputParameterIndex != -1
            || !accessor.ParameterAdaptations.IsDefaultOrEmpty
            || !accessor.HiddenParameters.IsDefaultOrEmpty
            || !TryParseSignature(accessor.Signature, out var returnType, out var parameters)
            || returnType != expectedReturnType || parameters.Length != 1
            || !parameters[0].EndsWith("*", StringComparison.Ordinal)
            || parameters[0].EndsWith("**", StringComparison.Ordinal)
            || !interfaceBySignatureIdentity.TryGetValue(parameters[0][..^1].Trim(),
                out var receiverInterface)
            || receiverInterface != sourceInterface)
        {
            return false;
        }

        var expectedKind = expectedReturnType == "LPVOID"
            ? NativeComAbiValueKind.Pointer : NativeComAbiValueKind.Unsigned64;
        return TryGetAbiValueKind(returnType, signatureTypes, allowVoid: false,
            isParameter: false, out var returnKind) && returnKind == expectedKind;
    }

    private static bool IsValidAggregateAddressAdaptation(NativeComParameterAdaptation adaptation)
    {
        const string constPrefix = "const ";
        if (IsBuiltinSignatureType(adaptation.SourceType)
            || adaptation.SourceType.Contains('*')
            || !adaptation.TargetType.StartsWith(constPrefix, StringComparison.Ordinal)
            || !adaptation.TargetType.EndsWith("*", StringComparison.Ordinal)
            || adaptation.TargetType.EndsWith("**", StringComparison.Ordinal)
            || adaptation.TargetType[constPrefix.Length..^1].Trim() != adaptation.SourceType
            || adaptation.SourceSize <= 0
            || adaptation.SourceAlignment <= 0
            || adaptation.SourceAlignment > 2 * NativeTargetProfile.PointerSize
            || (adaptation.SourceAlignment & (adaptation.SourceAlignment - 1)) != 0
            || adaptation.SourceSize % adaptation.SourceAlignment != 0)
        {
            return false;
        }
        return true;
    }

    private static bool IsValidAggregateStorageAddressAdaptation(
        NativeComParameterAdaptation adaptation)
    {
        const string constPrefix = "const ";
        if (IsBuiltinSignatureType(adaptation.SourceType)
            || adaptation.SourceType.Contains('*')
            || (adaptation.TargetType != adaptation.SourceType + "*"
                && adaptation.TargetType != constPrefix + adaptation.SourceType + "*")
            || adaptation.SourceSize <= 0
            || adaptation.SourceAlignment <= 0
            || adaptation.SourceAlignment > 2 * NativeTargetProfile.PointerSize
            || (adaptation.SourceAlignment & (adaptation.SourceAlignment - 1)) != 0
            || adaptation.SourceSize % adaptation.SourceAlignment != 0)
        {
            return false;
        }
        return true;
    }

    internal static bool TryGetValueArrayElementCount(string targetType, out int elementCount)
    {
        const string constPrefix = "const ";
        var openBracket = targetType.LastIndexOf('[');
        if (!targetType.StartsWith(constPrefix, StringComparison.Ordinal)
            || openBracket <= constPrefix.Length
            || !targetType.EndsWith("]", StringComparison.Ordinal)
            || !int.TryParse(targetType.AsSpan(openBracket + 1, targetType.Length - openBracket - 2),
                NumberStyles.None, CultureInfo.InvariantCulture, out elementCount)
            || elementCount <= 0)
        {
            elementCount = 0;
            return false;
        }
        return true;
    }

    private static bool TryGetValueArrayElementType(string targetType, out string elementType)
    {
        var openBracket = targetType.LastIndexOf('[');
        if (openBracket <= 0 || targetType[openBracket - 1] == '*')
        {
            elementType = "";
            return false;
        }
        elementType = targetType["const ".Length..openBracket].Trim();
        return elementType.Length > 0;
    }

    private static bool MatchesAbi(string abi, string returnType, string[] parameters,
        IReadOnlyDictionary<string, NativeComSignatureTypeDefinition> signatureTypes)
    {
        if (string.IsNullOrWhiteSpace(abi) || !abi.StartsWith("windows-x64-", StringComparison.Ordinal))
        {
            return false;
        }
        if (!TryGetAbiValueKind(returnType, signatureTypes, allowVoid: true, isParameter: false,
                out var returnKind)
            || parameters.Any(parameter => !TryGetAbiValueKind(parameter, signatureTypes,
                allowVoid: false, isParameter: true, out _)))
        {
            return false;
        }

        var parts = abi["windows-x64-".Length..].Split('-');
        if (parts.Length == 0)
        {
            return false;
        }

        var hasReturnLocation = parts[^1] is "void" or "eax" or "rax";
        hasReturnLocation |= parts[^1] is "xmm0";
        var returnLocation = hasReturnLocation ? parts[^1] : "void";
        var argumentPartCount = hasReturnLocation ? parts.Length - 1 : parts.Length;
        var expectedReturnLocation = returnKind switch {
            null => "void",
            NativeComAbiValueKind.Signed32 or NativeComAbiValueKind.Unsigned32 => "eax",
            NativeComAbiValueKind.Signed64 or NativeComAbiValueKind.Unsigned64 or NativeComAbiValueKind.Pointer => "rax",
            NativeComAbiValueKind.Float32 or NativeComAbiValueKind.Float64 => "xmm0",
            _ => "",
        };
        if (returnLocation != expectedReturnLocation
            || (!hasReturnLocation && returnType != "void"))
        {
            return false;
        }

        var locations = new List<string>(parameters.Length);
        var hasDisplacement = false;
        for (var index = 0; index < argumentPartCount; index++)
        {
            switch (parts[index])
            {
                case "rcx":
                case "ecx":
                case "rdx":
                case "r8":
                case "r9":
                case "edx":
                case "r8d":
                case "r9d":
                case "xmm0":
                case "xmm1":
                case "xmm2":
                case "xmm3":
                    if (hasDisplacement)
                    {
                        return false;
                    }
                    locations.Add(parts[index]);
                    break;
                case "disp32":
                    if (hasDisplacement || locations.Count != 3)
                    {
                        return false;
                    }
                    hasDisplacement = true;
                    break;
                default:
                    if (!IsStackLocation(parts[index]) || hasDisplacement)
                    {
                        return false;
                    }
                    locations.Add(parts[index]);
                    break;
            }
        }

        if (locations.Count != parameters.Length)
        {
            return false;
        }
        for (var index = 0; index < locations.Count; index++)
        {
            var expectedRegister = index switch {
                0 => "rcx",
                1 => "rdx",
                2 => "r8",
                3 => "r9",
                _ => null,
            };
            var narrowRegister = index switch {
                0 => "ecx",
                1 => "edx",
                2 => "r8d",
                3 => "r9d",
                _ => null,
            };
            var floatingRegister = index < 4 ? $"xmm{index}" : null;
            if (!TryGetAbiValueKind(parameters[index], signatureTypes, allowVoid: false,
                    isParameter: true, out var kind) || kind is null)
            {
                return false;
            }
            var matches = expectedRegister is null
                ? locations[index] == $"stack{32L + (long)(index - 4) * 8}"
                : kind is NativeComAbiValueKind.Float32 or NativeComAbiValueKind.Float64
                    ? locations[index] == floatingRegister
                    : kind is NativeComAbiValueKind.Signed32 or NativeComAbiValueKind.Unsigned32
                        ? locations[index] == expectedRegister || locations[index] == narrowRegister
                        : locations[index] == expectedRegister;
            if (!matches)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetAbiValueKind(string type,
        IReadOnlyDictionary<string, NativeComSignatureTypeDefinition> signatureTypes, bool allowVoid,
        bool isParameter, out NativeComAbiValueKind? kind)
    {
        if (allowVoid && type == "void")
        {
            kind = null;
            return true;
        }
        if (type.EndsWith("*", StringComparison.Ordinal)
            || (isParameter && type.EndsWith("]", StringComparison.Ordinal)
                && type.LastIndexOf('[') > 0))
        {
            kind = NativeComAbiValueKind.Pointer;
            return true;
        }
        if (TryGetBuiltinSignatureType(type, out var builtinKind))
        {
            kind = builtinKind;
            return true;
        }
        if (signatureTypes.TryGetValue(type, out var definition))
        {
            kind = definition.AbiValueKind;
            return true;
        }
        kind = null;
        return false;
    }

    private static bool HaveCompatibleVTableSignature(NativeComMethodDefinition first,
        NativeComMethodDefinition second,
        IReadOnlyDictionary<string, NativeComSignatureTypeDefinition> signatureTypes)
    {
        if (first.Effects != second.Effects || first.IsNoThrow != second.IsNoThrow
            || !first.NativeParameters.SequenceEqual(second.NativeParameters)
            || first.OutputParameterIndex != second.OutputParameterIndex
            || first.OutputInterfaceIdentity != second.OutputInterfaceIdentity
            || first.BorrowedOwnerParameters.Length != second.BorrowedOwnerParameters.Length
            || !first.BorrowedOwnerParameters.All(second.BorrowedOwnerParameters.Contains)
            || !TryParseSignature(first.Signature, out var firstReturnType, out var firstParameters)
            || !TryParseSignature(second.Signature, out var secondReturnType, out var secondParameters)
            || firstParameters.Length != secondParameters.Length
            || !TryGetAbiValueKind(firstReturnType, signatureTypes, allowVoid: true,
                isParameter: false, out var firstReturnKind)
            || !TryGetAbiValueKind(secondReturnType, signatureTypes, allowVoid: true,
                isParameter: false, out var secondReturnKind)
            || firstReturnKind != secondReturnKind)
        {
            return false;
        }
        for (var index = 0; index < firstParameters.Length; index++)
        {
            if (!TryGetAbiValueKind(firstParameters[index], signatureTypes, allowVoid: false,
                    isParameter: true, out var firstParameterKind)
                || !TryGetAbiValueKind(secondParameters[index], signatureTypes, allowVoid: false,
                    isParameter: true, out var secondParameterKind)
                || firstParameterKind != secondParameterKind)
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryGetBuiltinSignatureType(string identity, out NativeComAbiValueKind kind)
    {
        kind = identity switch {
            "int32" or "HRESULT" => NativeComAbiValueKind.Signed32,
            "uint32" or "UINT" or "ULONG" => NativeComAbiValueKind.Unsigned32,
            "int64" => NativeComAbiValueKind.Signed64,
            "uint64" or "SIZE_T" => NativeComAbiValueKind.Unsigned64,
            "float" or "FLOAT" => NativeComAbiValueKind.Float32,
            "double" or "DOUBLE" => NativeComAbiValueKind.Float64,
            "LPVOID" or "REFIID" => NativeComAbiValueKind.Pointer,
            _ => (NativeComAbiValueKind)byte.MaxValue,
        };
        return (byte)kind != byte.MaxValue;
    }

    private static bool IsBuiltinSignatureType(string identity) =>
        TryGetBuiltinSignatureType(identity, out _);

    private static bool IsStackLocation(string value) =>
        value.StartsWith("stack", StringComparison.Ordinal)
        && int.TryParse(value.AsSpan("stack".Length), out var offset)
        && offset >= 32 && (offset - 32) % 8 == 0;

    private static bool Invalid(out string failureReason, string reason)
    {
        failureReason = reason;
        return false;
    }
}
