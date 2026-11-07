// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class ModuleCompiler
{
    private readonly record struct Private105NativeParameterRow(
        uint FormalOrdinal, byte Access, byte Retention, byte ExtentKind,
        uint Extent);

    private sealed record NativeFunctionPointerSignatureReadResult(
        ImmutableArray<NativeFunctionPointerSignatureDefinition> Signatures,
        ImmutableArray<ImmutableArray<Private105NativeParameterRow>>
            NativeParameterRows);

    private static void WritePrivateRevision105NativeFunctionPointerSignature(
        BinaryWriter writer,
        NativeFunctionPointerSignatureDefinition signature)
    {
        writer.Write(EncodePrivate105CallingConvention(
            signature.CallingConvention));
        WritePrivateType(writer, signature.ReturnType);
        writer.Write(signature.ParameterTypes.Length);
        foreach (var parameterType in signature.ParameterTypes)
        {
            WritePrivateType(writer, parameterType);
        }

        writer.Write(signature.EffectsMask);
        writer.Write(signature.Selectors.Length);
        foreach (var selector in signature.Selectors.OrderBy(
            static selector => selector.PhaseLocalOrdinal))
        {
            writer.Write(selector.PhaseTag);
            writer.Write((byte)0);
            writer.Write((ushort)0);
            writer.Write(selector.PhaseLocalOrdinal);
            writer.Write((uint)selector.NodeKind);
        }

        writer.Write(signature.OutputStates.Length);
        foreach (var outputState in signature.OutputStates.OrderBy(
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

        writer.Write(signature.Dependencies.Length);
        foreach (var dependency in signature.Dependencies.OrderBy(
            static dependency => dependency.DestinationKind)
            .ThenBy(static dependency => dependency.DestinationOrdinal)
            .ThenBy(static dependency => dependency.Phase)
            .ThenBy(static dependency => dependency.SourceKind)
            .ThenBy(static dependency => dependency.SourceOrdinal))
        {
            writer.Write(dependency.DestinationKind);
            writer.Write(dependency.SourceKind);
            writer.Write(dependency.Relationship);
            writer.Write(dependency.Phase);
            writer.Write(dependency.DestinationOrdinal);
            writer.Write(dependency.SourceOrdinal);
        }

        writer.Write(signature.NativeParameters.Length);
        foreach (var parameter in signature.NativeParameters.OrderBy(
            static parameter => parameter.ParameterIndex))
        {
            if (parameter.ParameterIndex < 0
                || parameter.Retention != NativeParameterRetention.NoEscape)
            {
                throw new ModuleContractException(
                    "Private revision 106 NativeParameter facts must use an explicit formal and NoEscape.");
            }

            writer.Write(checked((uint)parameter.ParameterIndex));
            writer.Write(EncodePrivate105NativeParameterAccess(
                parameter.Access));
            writer.Write((byte)parameter.Retention);
            writer.Write(parameter.Extent == 0 ? (byte)0 : (byte)1);
            writer.Write((byte)0);
            writer.Write(parameter.Extent);
        }
    }

    private static NativeFunctionPointerSignatureReadResult
        ReadNativeFunctionPointerSignatures(BinaryReader reader)
    {
        if (reader is not ModuleReader {
                FormatVersion: >= NativeFunctionPointerVersion,
            } moduleReader)
        {
            return new NativeFunctionPointerSignatureReadResult([], []);
        }

        var isRevision105 = moduleReader.FormatVersion
            == CheckedReferenceVersion;
        var signatureCount = Count(reader, MaximumNodes);
        if (signatureCount == 0
            && moduleReader.FormatVersion == NativeFunctionPointerVersion)
        {
            throw new ModuleFormatException(
                "Native function-pointer revision requires a nonempty signature table.");
        }

        var signatures =
            ImmutableArray.CreateBuilder<NativeFunctionPointerSignatureDefinition>(
                signatureCount);
        var nativeParameterRows =
            ImmutableArray.CreateBuilder<
                ImmutableArray<Private105NativeParameterRow>>(
                    signatureCount);
        for (var index = 0; index < signatureCount; index++)
        {
            var conventionByte = reader.ReadByte();
            var convention = isRevision105
                ? DecodePrivate105CallingConvention(conventionByte)
                : (NativeCallingConvention)conventionByte;
            if (moduleReader.FormatVersion == Version
                && convention != NativeCallingConvention.Native)
            {
                throw new ModuleFormatException(
                    "Structured-module revision 76 contains an unsupported native calling convention.");
            }

            var returnType = GetType(reader);
            var parameterCount = CountRows(reader, MaximumSlots,
                sizeof(int), "function-pointer parameter types");
            var parameterTypes =
                ImmutableArray.CreateBuilder<PrimitiveType>(parameterCount);
            for (var parameter = 0; parameter < parameterCount; parameter++)
            {
                parameterTypes.Add(GetType(reader));
            }

            if (!isRevision105)
            {
                signatures.Add(new NativeFunctionPointerSignatureDefinition(
                    convention, returnType, parameterTypes.MoveToImmutable()));
                nativeParameterRows.Add([]);
                continue;
            }

            var effectsMask = reader.ReadUInt32();
            var selectorCount = CountRows(reader, MaximumNodes, 12,
                "function-pointer selectors");
            var selectors =
                ImmutableArray.CreateBuilder<NativeFunctionPointerSelector>(
                    selectorCount);
            for (var selectorIndex = 0; selectorIndex < selectorCount;
                selectorIndex++)
            {
                var phaseTag = reader.ReadByte();
                var flags = reader.ReadByte();
                var reserved = reader.ReadUInt16();
                var phaseLocalOrdinal = reader.ReadUInt32();
                var nodeKind = reader.ReadUInt32();
                if (flags != 0 || reserved != 0 || nodeKind > byte.MaxValue)
                {
                    throw new ModuleFormatException(
                        "Revision 106 function-pointer selector has invalid reserved or node-kind data.");
                }

                selectors.Add(new NativeFunctionPointerSelector(phaseTag,
                    phaseLocalOrdinal, (byte)nodeKind));
            }

            var outputStateCount = CountRows(reader, MaximumNodes, 12,
                "function-pointer output states");
            var outputStates =
                ImmutableArray.CreateBuilder<NativeFunctionPointerOutputState>(
                    outputStateCount);
            for (var outputIndex = 0; outputIndex < outputStateCount;
                outputIndex++)
            {
                var formalOrdinal = reader.ReadUInt32();
                var selectorOrdinal = reader.ReadUInt32();
                var state = reader.ReadByte();
                var reserved = reader.ReadBytes(3);
                if (reserved.Length != 3 || reserved[0] != 0
                    || reserved[1] != 0 || reserved[2] != 0)
                {
                    throw new ModuleFormatException(
                        "Revision 106 function-pointer output state has nonzero reserved bytes.");
                }

                outputStates.Add(new NativeFunctionPointerOutputState(
                    formalOrdinal, selectorOrdinal, state));
            }

            var dependencyCount = CountRows(reader, MaximumNodes, 12,
                "function-pointer dependencies");
            var dependencies =
                ImmutableArray.CreateBuilder<NativeFunctionPointerDependency>(
                    dependencyCount);
            for (var dependencyIndex = 0;
                dependencyIndex < dependencyCount; dependencyIndex++)
            {
                dependencies.Add(new NativeFunctionPointerDependency(
                    reader.ReadByte(), reader.ReadByte(), reader.ReadByte(),
                    reader.ReadByte(), reader.ReadUInt32(),
                    reader.ReadUInt32()));
            }

            var nativeParameterCount = CountRows(reader, MaximumSlots, 12,
                "function-pointer NativeParameters");
            var parameterRows =
                ImmutableArray.CreateBuilder<Private105NativeParameterRow>(
                    nativeParameterCount);
            for (var parameterIndex = 0;
                parameterIndex < nativeParameterCount; parameterIndex++)
            {
                var formalOrdinal = reader.ReadUInt32();
                var access = reader.ReadByte();
                var retention = reader.ReadByte();
                var extentKind = reader.ReadByte();
                var reserved = reader.ReadByte();
                var extent = reader.ReadUInt32();
                if (formalOrdinal >= (uint)parameterCount
                    || access is not (1 or 2 or 3) || retention != 0
                    || extentKind is not (0 or 1) || reserved != 0
                    || (extentKind == 0 && extent != 0)
                    || (extentKind == 1 && extent == 0))
                {
                    throw new ModuleFormatException(
                        "Revision 106 NativeParameter row has invalid formal, access, retention or extent data.");
                }

                parameterRows.Add(new Private105NativeParameterRow(
                    formalOrdinal, access, retention, extentKind, extent));
            }

            signatures.Add(new NativeFunctionPointerSignatureDefinition(
                convention, returnType, parameterTypes.MoveToImmutable()) {
                EffectsMask = effectsMask,
                Selectors = selectors.MoveToImmutable(),
                OutputStates = outputStates.MoveToImmutable(),
                Dependencies = dependencies.MoveToImmutable(),
                NativeParameters = [],
            });
            nativeParameterRows.Add(parameterRows.MoveToImmutable());
        }

        var result = signatures.MoveToImmutable();
        if (!isRevision105
            && !NativeFunctionPointerContract.IsValidTable(result))
        {
            throw new ModuleFormatException(
                "Module native function-pointer signature table is invalid.");
        }

        return new NativeFunctionPointerSignatureReadResult(result,
            nativeParameterRows.MoveToImmutable());
    }

    private static ImmutableArray<NativeFunctionPointerSignatureDefinition>
        RestorePrivate105NativeParameterPointees(
            NativeFunctionPointerSignatureReadResult readResult,
            ImmutableArray<NativePointerTypeDefinition> pointerTypes)
    {
        if (readResult.Signatures.Length
                != readResult.NativeParameterRows.Length)
        {
            throw new ModuleFormatException(
                "Revision 106 function-pointer parameter rows do not match the signature table.");
        }

        var signatures =
            ImmutableArray.CreateBuilder<NativeFunctionPointerSignatureDefinition>(
                readResult.Signatures.Length);
        for (var index = 0; index < readResult.Signatures.Length; index++)
        {
            var signature = readResult.Signatures[index];
            var parameters =
                ImmutableArray.CreateBuilder<DirectNativeParameter>(
                    readResult.NativeParameterRows[index].Length);
            foreach (var row in readResult.NativeParameterRows[index])
            {
                if (row.FormalOrdinal
                    >= (uint)signature.ParameterTypes.Length)
                {
                    throw new ModuleFormatException(
                        "Revision 106 NativeParameter formal exceeds the signature.");
                }
                var formalOrdinal = (int)row.FormalOrdinal;

                var formalType = signature.ParameterTypes[formalOrdinal];
                PrimitiveType pointeeType;
                if (formalType == PrimitiveType.NativeRawPointer)
                {
                    pointeeType = PrimitiveType.Void;
                }
                else if (NativePointerTypes.IsType(formalType)
                    && (uint)NativePointerTypes.Index(formalType)
                        < (uint)pointerTypes.Length)
                {
                    pointeeType = pointerTypes[
                        NativePointerTypes.Index(formalType)].PointeeType;
                }
                else
                {
                    throw new ModuleFormatException(
                        "Revision 106 NativeParameter formal is not an admitted raw pointer.");
                }

                parameters.Add(new DirectNativeParameter(formalOrdinal,
                    pointeeType, DecodePrivate105NativeParameterAccess(
                        row.Access),
                    NativeParameterRetention.NoEscape, row.Extent));
            }

            signatures.Add(signature with {
                NativeParameters = parameters.MoveToImmutable(),
            });
        }
        return signatures.MoveToImmutable();
    }

    private static byte EncodePrivate105CallingConvention(
        NativeCallingConvention convention)
        => convention switch {
            NativeCallingConvention.Unmanaged => 0,
            NativeCallingConvention.Cdecl => 1,
            NativeCallingConvention.Stdcall => 2,
            NativeCallingConvention.Vectorcall => 3,
            NativeCallingConvention.Native => 4,
            _ => throw new ModuleContractException(
                "Private revision 106 has an unknown calling convention."),
        };

    private static NativeCallingConvention DecodePrivate105CallingConvention(
        byte convention)
        => convention switch {
            0 => NativeCallingConvention.Unmanaged,
            1 => NativeCallingConvention.Cdecl,
            2 => NativeCallingConvention.Stdcall,
            3 => NativeCallingConvention.Vectorcall,
            4 => NativeCallingConvention.Native,
            _ => throw new ModuleFormatException(
                "Revision 106 function-pointer signature has an unknown convention."),
        };

    private static byte EncodePrivate105NativeParameterAccess(
        NativeParameterAccess access)
        => access switch {
            NativeParameterAccess.Read => 1,
            NativeParameterAccess.Write => 2,
            NativeParameterAccess.ReadWrite => 3,
            _ => throw new ModuleContractException(
                "Private revision 106 has an unknown NativeParameter access."),
        };

    private static NativeParameterAccess
        DecodePrivate105NativeParameterAccess(byte access)
        => access switch {
            1 => NativeParameterAccess.Read,
            2 => NativeParameterAccess.Write,
            3 => NativeParameterAccess.ReadWrite,
            _ => throw new ModuleFormatException(
                "Revision 106 NativeParameter has an unknown access."),
        };

    private static string Private105NativeCallableConventionName(
        NativeCallingConvention convention)
        => convention switch {
            NativeCallingConvention.Unmanaged => "Unmanaged",
            NativeCallingConvention.Cdecl => "Cdecl",
            NativeCallingConvention.Stdcall => "Stdcall",
            NativeCallingConvention.Vectorcall => "Vectorcall",
            _ => throw new ModuleContractException(
                "Private revision 106 NativeCallable has an unsupported convention."),
        };

    private static string Private105NativeImportResolutionToken(
        DirectNativeImport import)
        => (import.ResolutionMode, import.ResolutionPolicy) switch {
            (NativeImportResolutionMode.Link,
                NativeImportResolutionPolicy.NotApplicable)
                => "|NativeImportResolution:Link",
            (NativeImportResolutionMode.Dynamic,
                NativeImportResolutionPolicy.Default)
                => "|NativeImportResolution:Dynamic:Default",
            (NativeImportResolutionMode.Dynamic,
                NativeImportResolutionPolicy.CompatibilitySearch)
                => "|NativeImportResolution:Dynamic:CompatibilitySearch",
            _ => throw new ModuleContractException(
                "Private revision 106 has an invalid NativeImport resolution selection."),
        };

    private static (NativeImportResolutionMode Mode,
        NativeImportResolutionPolicy Policy)
        ParsePrivate105NativeImportResolution(string token)
        => token switch {
            "Link" => (NativeImportResolutionMode.Link,
                NativeImportResolutionPolicy.NotApplicable),
            "Dynamic:Default" => (NativeImportResolutionMode.Dynamic,
                NativeImportResolutionPolicy.Default),
            "Dynamic:CompatibilitySearch" =>
                (NativeImportResolutionMode.Dynamic,
                    NativeImportResolutionPolicy.CompatibilitySearch),
            _ => throw new ModuleFormatException(
                "Revision 106 NativeImport resolution token is invalid."),
        };

    private static NativeCallingConvention
        ParsePrivate105NativeImportConvention(string token)
        => token switch {
            "Unmanaged" => NativeCallingConvention.Unmanaged,
            "Cdecl" => NativeCallingConvention.Cdecl,
            "Stdcall" => NativeCallingConvention.Stdcall,
            "Vectorcall" => NativeCallingConvention.Vectorcall,
            _ => throw new ModuleFormatException(
                "Revision 106 NativeImport calling-convention token is invalid."),
        };

    private static BoundFunction RestorePrivate105DirectNativeImportSettings(
        BoundFunction function, string contract)
    {
        var directImport = function.DirectNativeImport
            ?? throw new ModuleFormatException(
                "Revision 106 NativeImport token has no stored direct import.");
        return function with {
            DirectNativeImport =
                RestorePrivate105DirectNativeImportSettings(directImport,
                    contract),
        };
    }

    private static DirectNativeImport
        RestorePrivate105DirectNativeImportSettings(
            DirectNativeImport directImport, string contract)
    {
        var parts = contract.Split('|');
        var resolutionIndex = -1;
        var conventionIndex = -1;
        string? resolutionToken = null;
        string? conventionToken = null;
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].StartsWith("NativeImportResolution:",
                    StringComparison.Ordinal))
            {
                if (resolutionIndex >= 0)
                {
                    throw new ModuleFormatException(
                        "Revision 106 declaration has duplicate NativeImport resolution tokens.");
                }

                resolutionIndex = index;
                resolutionToken = parts[index][
                    "NativeImportResolution:".Length..];
            }
            else if (parts[index].StartsWith("NativeImportCallConv:",
                         StringComparison.Ordinal))
            {
                if (conventionIndex >= 0)
                {
                    throw new ModuleFormatException(
                        "Revision 106 declaration has duplicate NativeImport convention tokens.");
                }

                conventionIndex = index;
                conventionToken = parts[index][
                    "NativeImportCallConv:".Length..];
            }
        }

        if (resolutionIndex < 0
            || (conventionIndex >= 0
                && conventionIndex != resolutionIndex + 1)
            || parts.Any(static part => part.StartsWith(
                "DirectNativeImport:", StringComparison.Ordinal)
                || part.StartsWith("DirectNativeParameter:",
                    StringComparison.Ordinal)
                || part.StartsWith("DirectNativeOutput:",
                    StringComparison.Ordinal)))
        {
            throw new ModuleFormatException(
                "Revision 106 direct import contract has invalid token selection or order.");
        }

        var returnIndex = conventionIndex >= 0
            ? conventionIndex + 1 : resolutionIndex + 1;
        if (returnIndex >= parts.Length
            || parts[returnIndex] is not ("MayReturn" or "DoesNotReturn"))
        {
            throw new ModuleFormatException(
                "Revision 106 NativeImport suffix is incomplete.");
        }
        if (!parts.Contains("CallerUnsafe", StringComparer.Ordinal))
        {
            throw new ModuleFormatException(
                "Revision 106 NativeImport declaration lost caller-unsafety.");
        }

        var (mode, policy) = ParsePrivate105NativeImportResolution(
            resolutionToken!);
        var callingConvention = conventionToken is null
            ? NativeCallingConvention.Native
            : ParsePrivate105NativeImportConvention(conventionToken);
        return directImport with {
            CallingConvention = callingConvention,
            ResolutionMode = mode,
            ResolutionPolicy = policy,
        };
    }

    private static BoundFunction
        RestorePrivate105NativeCallableConvention(string contract,
            BoundFunction function,
            ImmutableArray<NativeFunctionPointerSignatureDefinition>
                signatures)
    {
        const string marker = "|NativeCallable:";
        var markerIndex = contract.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return function;
        }

        var valueStart = markerIndex + marker.Length;
        var valueEnd = contract.IndexOf('|', valueStart,
            StringComparison.Ordinal);
        var token = valueEnd < 0
            ? contract[valueStart..] : contract[valueStart..valueEnd];
        var convention = token switch {
            "Unmanaged" => NativeCallingConvention.Unmanaged,
            "Cdecl" => NativeCallingConvention.Cdecl,
            "Stdcall" => NativeCallingConvention.Stdcall,
            "Vectorcall" => NativeCallingConvention.Vectorcall,
            _ => throw new ModuleFormatException(
                "Revision 106 NativeCallable token has an unknown convention."),
        };
        if (function.NativeCallableConvention
                is NativeCallingConvention existingConvention
            && existingConvention != convention)
        {
            throw new ModuleFormatException(
                "Revision 106 NativeCallable token differs from its stored function-pointer type.");
        }
        if (function.NativeFunctionPointerType
            is PrimitiveType functionPointerType
            && (!NativeFunctionPointerContract.IsValidType(
                    functionPointerType, signatures)
                || signatures[NativeFunctionPointerTypes.Index(
                    functionPointerType)].CallingConvention != convention))
        {
            throw new ModuleFormatException(
                "Revision 106 NativeCallable token differs from its signature row.");
        }

        return function with {
            NativeCallableConvention = convention,
        };
    }

    private static int? Private105NativeAddressContractIndex(
        BoundFunction function,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures)
    {
        if (function.NativeFunctionPointerType
            is PrimitiveType nativeCallableType)
        {
            if (!NativeFunctionPointerContract.IsValidType(
                nativeCallableType, signatures))
            {
                throw new ModuleContractException(
                    "Private revision 106 NativeCallable address contract has no function-pointer row.");
            }
            return NativeFunctionPointerTypes.Index(nativeCallableType);
        }

        if (function.DirectNativeImport is not null
            && NativeFunctionPointerContract.TryCreateNativeImportSignature(
                function, signatures, out var nativeImportSignature)
            && nativeImportSignature is NativeFunctionPointerSignatureDefinition
                signature)
        {
            for (var index = 0; index < signatures.Length; index++)
            {
                if (NativeFunctionPointerContract.AreEquivalent(signature,
                    signatures[index]))
                {
                    return index;
                }
            }

            throw new ModuleContractException(
                "Private revision 106 addressable NativeImport has no signature row.");
        }

        return null;
    }

    private static BoundFunction RestorePrivate105NativeAddressContract(
        string contract, BoundFunction function,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures)
    {
        var parts = contract.Split('|');
        var addressIndex = -1;
        var addressTokenPosition = -1;
        var callableTokenPosition = -1;
        var callableEntryTokenPosition = -1;
        var resolutionTokenPosition = -1;
        var importConventionTokenPosition = -1;
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            if (part.StartsWith("NativeAddressContract:",
                    StringComparison.Ordinal))
            {
                if (addressIndex >= 0)
                {
                    throw new ModuleFormatException(
                        "Revision 106 function has duplicate address-contract roots.");
                }

                addressIndex = ParsePrivate105FunctionPointerReference(
                    part["NativeAddressContract:".Length..], signatures);
                addressTokenPosition = index;
            }
            else if (part.StartsWith("NativeCallable:",
                         StringComparison.Ordinal))
            {
                if (callableTokenPosition >= 0)
                {
                    throw new ModuleFormatException(
                        "Revision 106 function has duplicate NativeCallable roles.");
                }
                callableTokenPosition = index;
            }
            else if (part.StartsWith("NativeCallableEntry:",
                         StringComparison.Ordinal))
            {
                if (callableEntryTokenPosition >= 0)
                {
                    throw new ModuleFormatException(
                        "Revision 106 function has duplicate NativeCallable entry tokens.");
                }
                callableEntryTokenPosition = index;
            }
            else if (part.StartsWith("NativeImportResolution:",
                         StringComparison.Ordinal))
            {
                if (resolutionTokenPosition >= 0)
                {
                    throw new ModuleFormatException(
                        "Revision 106 function has duplicate NativeImport resolution tokens.");
                }
                resolutionTokenPosition = index;
            }
            else if (part.StartsWith("NativeImportCallConv:",
                         StringComparison.Ordinal))
            {
                if (importConventionTokenPosition >= 0)
                {
                    throw new ModuleFormatException(
                        "Revision 106 function has duplicate NativeImport convention tokens.");
                }
                importConventionTokenPosition = index;
            }
        }

        if (addressIndex < 0)
        {
            var isAddressable = function.NativeFunctionPointerType is not null;
            if (!isAddressable
                && function.DirectNativeImport is not null
                && NativeFunctionPointerContract.TryCreateNativeImportSignature(
                    function, signatures, out var nativeImportSignature))
            {
                isAddressable = nativeImportSignature is not null;
            }
            if (isAddressable)
            {
                throw new ModuleFormatException(
                    "Revision 106 addressable declaration lacks its address-contract root.");
            }
            return function;
        }

        if ((callableTokenPosition >= 0
                && addressTokenPosition > callableTokenPosition)
            || (callableEntryTokenPosition >= 0
                && (callableTokenPosition < 0
                    || callableTokenPosition > callableEntryTokenPosition))
            || (resolutionTokenPosition >= 0
                && addressTokenPosition > resolutionTokenPosition)
            || (importConventionTokenPosition >= 0
                && addressTokenPosition > importConventionTokenPosition))
        {
            throw new ModuleFormatException(
                "Revision 106 address-contract root has an invalid token position.");
        }

        var signature = signatures[addressIndex];
        if ((signature.EffectsMask & ~6u) != 0
            || function.DoesNotReturn != ((signature.EffectsMask & 2) != 0))
        {
            throw new ModuleFormatException(
                "Revision 106 address-contract effects differ from the stored function.");
        }

        var restored = function with {
            DeclaredRequiresUnsafeCaller = (signature.EffectsMask & 4) != 0,
        };
        if (function.NativeFunctionPointerType
            is PrimitiveType nativeCallableType)
        {
            if (!NativeFunctionPointerTypes.IsType(nativeCallableType)
                || NativeFunctionPointerTypes.Index(nativeCallableType)
                    != addressIndex
                || !NativeFunctionPointerContract.MatchesFunction(
                    restored, nativeCallableType, signatures))
            {
                throw new ModuleFormatException(
                    "Revision 106 NativeCallable differs from its address-contract root.");
            }
        }
        else if (function.DirectNativeImport is not null)
        {
            if (!NativeFunctionPointerContract.TryCreateNativeImportSignature(
                    restored, signatures, out var nativeImportSignature)
                || nativeImportSignature is null
                || !NativeFunctionPointerContract.AreEquivalent(
                    nativeImportSignature, signature))
            {
                throw new ModuleFormatException(
                    "Revision 106 NativeImport differs from its address-contract root.");
            }
        }
        else
        {
            throw new ModuleFormatException(
                "Revision 106 address-contract root has an inapplicable owner.");
        }

        return restored;
    }

    private static void ValidatePrivate105BodylessFunctionContract(
        string contract, bool isImport,
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<NativeFunctionPointerSignatureDefinition>
            signatures,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrayTypes,
        DirectNativeImport? expectedDirectNativeImport,
        Private105CopyOriginSummaryData? copyOriginSummary,
        NativeOutputDeclarationRole? outputRole,
        ImmutableArray<NativeOutputContract> outputContracts,
        NativeVTableMethodDefinition? importedNativeVTableMethod)
    {
        if (!isImport || contract.Contains("fnptr:",
                StringComparison.Ordinal))
        {
            throw new ModuleFormatException(
                "Revision 106 bodyless function contract has an invalid role or retired function-pointer type.");
        }

        var parts = contract.Split('|');
        var nativeVTableParameterTokens = parts.Where(static part =>
            part.StartsWith("NativeVTableParameter:",
                StringComparison.Ordinal)).ToArray();
        var expectedNativeVTableParameterTokens
            = importedNativeVTableMethod is NativeVTableMethodDefinition
                vtableMethod
            ? vtableMethod.Parameters.OrderBy(static parameter =>
                    parameter.ParameterIndex)
                .Select(parameter =>
                {
                    var token = new StringBuilder(
                        "NativeVTableParameter:");
                    _ = token.Append(parameter.ParameterIndex).Append(':')
                        .Append(ContractType(parameter.PointeeType, structures,
                            signatures, pointerTypes, fixedArrayTypes,
                            private105: true)).Append(':')
                        .Append((byte)parameter.Access).Append(':')
                        .Append((byte)parameter.Retention).Append(':')
                        .Append(parameter.Extent);
                    return token.ToString();
                }).ToArray()
            : [];
        if (!nativeVTableParameterTokens.SequenceEqual(
                expectedNativeVTableParameterTokens, StringComparer.Ordinal))
        {
            throw new ModuleFormatException(
                "Revision 106 NativeVTable parameters differ from their selected separate record.");
        }

        var parameterCount = ContractParameterCount(parts);
        ValidatePrivate105FunctionPointerTypeReferences(parts[1],
            signatures);
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
            if (typeStart < 0)
            {
                throw new ModuleFormatException(
                    "Revision 106 bodyless function formal has an invalid mode.");
            }

            ValidatePrivate105FunctionPointerTypeReferences(
                parameter[typeStart..], signatures);
        }

        var addressIndex = -1;
        var addressTokenPosition = -1;
        var callableConvention = (NativeCallingConvention?)null;
        var callableTokenPosition = -1;
        var callableEntryTokenPosition = -1;
        var resolutionTokenPosition = -1;
        var resolutionToken = (string?)null;
        var importConventionTokenPosition = -1;
        var importConventionToken = (string?)null;
        for (var index = 8 + parameterCount; index < parts.Length; index++)
        {
            if (parts[index].StartsWith("NativeAddressContract:",
                    StringComparison.Ordinal))
            {
                if (addressIndex >= 0)
                {
                    throw new ModuleFormatException(
                        "Revision 106 bodyless function has duplicate address-contract roots.");
                }

                addressIndex = ParsePrivate105FunctionPointerReference(
                    parts[index]["NativeAddressContract:".Length..],
                    signatures);
                addressTokenPosition = index;
            }
            else if (parts[index].StartsWith("NativeCallable:",
                StringComparison.Ordinal))
            {
                if (callableConvention is not null)
                {
                    throw new ModuleFormatException(
                        "Revision 106 bodyless function has duplicate NativeCallable roles.");
                }

                callableConvention = ParsePrivate105NativeCallableConvention(
                    parts[index]["NativeCallable:".Length..]);
                callableTokenPosition = index;
            }
            else if (parts[index].StartsWith("NativeCallableEntry:",
                         StringComparison.Ordinal))
            {
                if (callableEntryTokenPosition >= 0
                    || !IsPrivate105NativeCallableEntry(
                        parts[index]["NativeCallableEntry:".Length..]))
                {
                    throw new ModuleFormatException(
                        "Revision 106 NativeCallable entry token is malformed or duplicated.");
                }

                callableEntryTokenPosition = index;
            }
            else if (parts[index].StartsWith(
                "NativeImportResolution:", StringComparison.Ordinal))
            {
                if (resolutionTokenPosition >= 0)
                {
                    throw new ModuleFormatException(
                        "Revision 106 bodyless function has duplicate NativeImport resolution tokens.");
                }

                resolutionTokenPosition = index;
                resolutionToken = parts[index][
                    "NativeImportResolution:".Length..];
            }
            else if (parts[index].StartsWith("NativeImportCallConv:",
                         StringComparison.Ordinal))
            {
                if (importConventionTokenPosition >= 0)
                {
                    throw new ModuleFormatException(
                        "Revision 106 bodyless function has duplicate NativeImport convention tokens.");
                }

                importConventionTokenPosition = index;
                importConventionToken = parts[index][
                    "NativeImportCallConv:".Length..];
            }
            else if (parts[index].StartsWith("DirectNativeImport:",
                    StringComparison.Ordinal)
                || parts[index].StartsWith("DirectNativeParameter:",
                    StringComparison.Ordinal)
                || parts[index].StartsWith("DirectNativeOutput:",
                    StringComparison.Ordinal))
            {
                throw new ModuleFormatException(
                    "Revision 106 bodyless function contract contains an unselected native record.");
            }
        }

        if (expectedDirectNativeImport is DirectNativeImport expectedImport)
        {
            if (resolutionTokenPosition < 0
                || callableConvention is not null
                || (importConventionTokenPosition >= 0
                    && importConventionTokenPosition
                        != resolutionTokenPosition + 1)
                || (expectedImport.CallingConvention
                        == NativeCallingConvention.Native
                    ? importConventionTokenPosition >= 0
                    : importConventionTokenPosition < 0)
                || !DirectNativeImportContract
                    .IsValidPrivateWindowsLibraryName(
                        expectedImport.LibraryName)
                || !DirectNativeImportContract.IsValidExportName(
                    expectedImport.ExportName)
                || !expectedImport.Outputs.IsEmpty
                || expectedImport.Parameters.IsDefault
                || expectedImport.Parameters.Length > ModuleCompiler.MaximumSlots
                || outputContracts.IsDefault
                || outputContracts.Any(output =>
                    expectedImport.Parameters.Any(parameter =>
                        parameter.ParameterIndex
                            == output.FormalOrdinal))
                || (outputContracts.IsEmpty
                    ? outputRole is not null
                    : outputRole
                        != NativeOutputDeclarationRole.DirectImport))
            {
                throw new ModuleFormatException(
                    "Revision 106 direct-import expectation has invalid role, token or output selection.");
            }

            var returnIndex = importConventionTokenPosition >= 0
                ? importConventionTokenPosition + 1
                : resolutionTokenPosition + 1;
            if (returnIndex + 2 != parts.Length
                || parts[returnIndex] is not ("MayReturn" or "DoesNotReturn")
                || parts[returnIndex + 1] != "CallerUnsafe")
            {
                throw new ModuleFormatException(
                    "Revision 106 direct-import declaration has an invalid return or caller-unsafety suffix.");
            }

            var (resolutionMode, resolutionPolicy)
                = ParsePrivate105NativeImportResolution(
                    resolutionToken!);
            if (resolutionMode != expectedImport.ResolutionMode
                || resolutionPolicy != expectedImport.ResolutionPolicy
                || (importConventionToken is not null
                    && ParsePrivate105NativeImportConvention(
                        importConventionToken)
                        != expectedImport.CallingConvention))
            {
                throw new ModuleFormatException(
                    "Revision 106 direct-import tokens differ from their expected descriptor.");
            }

            var previousFormal = -1;
            foreach (var parameter in expectedImport.Parameters)
            {
                if (parameter.ParameterIndex <= previousFormal
                    || parameter.ParameterIndex >= parameterCount
                    || parameter.Access is not (NativeParameterAccess.Read
                        or NativeParameterAccess.Write
                        or NativeParameterAccess.ReadWrite)
                    || parameter.Retention
                        != NativeParameterRetention.NoEscape
                    || (parameter.PointeeType == PrimitiveType.Void
                        && (parameter.Access != NativeParameterAccess.Read
                            || parameter.Extent != 0))
                    || !DirectNativeImportContract.IsInputPointee(
                        parameter.PointeeType, structures)
                    || (NativePointerTypes.IsType(parameter.PointeeType)
                        && !NativePointerTypeContract.IsValidType(
                            parameter.PointeeType, pointerTypes))
                    || (NativeFunctionPointerTypes.IsType(
                            parameter.PointeeType)
                        && !NativeFunctionPointerContract.IsValidType(
                            parameter.PointeeType, signatures)))
                {
                    throw new ModuleFormatException(
                        "Revision 106 direct-import NativeParameter expectation is invalid.");
                }

                var expectedFormal
                    = Private105NativeParameterFormalType(parameter,
                        structures, signatures, pointerTypes,
                        fixedArrayTypes);
                if (parts[8 + parameter.ParameterIndex]
                    != expectedFormal)
                {
                    throw new ModuleFormatException(
                        "Revision 106 direct-import NativeParameter differs from its stored formal.");
                }
                previousFormal = parameter.ParameterIndex;
            }
        }
        else if (resolutionTokenPosition >= 0
            || importConventionTokenPosition >= 0)
        {
            throw new ModuleFormatException(
                "Revision 106 NativeImport tokens have no retained expectation.");
        }

        if (copyOriginSummary is not null
            && (resolutionTokenPosition >= 0
                || importConventionTokenPosition >= 0
                || callableConvention is not null
                || addressIndex >= 0
                || callableEntryTokenPosition >= 0))
        {
            throw new ModuleFormatException(
                "Revision 106 CopyOrigin summary is not applicable to a foreign declaration.");
        }

        if (addressIndex < 0)
        {
            if (callableConvention is not null)
            {
                throw new ModuleFormatException(
                    "Revision 106 bodyless NativeCallable has no address-contract root.");
            }
            return;
        }

        if (parts.Length < 8 + parameterCount
            || (callableTokenPosition >= 0
                && addressTokenPosition > callableTokenPosition)
            || (callableEntryTokenPosition >= 0
                && (addressTokenPosition < 0
                    || callableTokenPosition < 0
                    || callableTokenPosition
                        > callableEntryTokenPosition))
            || parts[2] != "F"
            || (parts[5] != "-" && parts[4] != "S")
            || parts.Contains("Operator", StringComparer.Ordinal)
            || parts.Contains("Cleanup", StringComparer.Ordinal)
            || parts.Any(static part => part.StartsWith("Property:",
                StringComparison.Ordinal)))
        {
            throw new ModuleFormatException(
                $"Revision 106 address-contract root has an inapplicable owner ({parts[2]}/{parts[4]}/{parts[5]}, parameters={parameterCount}, parts={parts.Length}, root={addressTokenPosition}, callable={callableTokenPosition}, entry={callableEntryTokenPosition}).");
        }

        var signature = signatures[addressIndex];
        if (signature.ParameterTypes.Length != parameterCount
            || parts[1] != ContractType(signature.ReturnType, structures,
                signatures, pointerTypes, fixedArrayTypes, private105: true))
        {
            throw new ModuleFormatException(
                "Revision 106 address-contract root differs from its declaration signature.");
        }

        var signatureOutputs
            = ImmutableArray.CreateBuilder<NativeOutputContract>();
        foreach (var formal in signature.OutputStates
            .Select(static output => output.FormalOrdinal)
            .Distinct()
            .Order())
        {
            if (formal >= (uint)parameterCount
                || !NativeFunctionPointerContract.TryGetNativeOutputContract(
                    signature, checked((int)formal), pointerTypes,
                    out var output)
                || output is null)
            {
                throw new ModuleFormatException(
                    "Revision 106 address-contract output states are invalid.");
            }

            signatureOutputs.Add(output);
        }
        if (!SameNativeOutputContracts(outputContracts,
            signatureOutputs.ToImmutable(), structures, structures,
            signatures, pointerTypes, signatures, pointerTypes))
        {
            throw new ModuleFormatException(
                "Revision 106 address-contract output states differ from their declaration-owned NativeOutput records.");
        }

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
                || parameter[typeStart..] != ContractType(
                    signature.ParameterTypes[index], structures, signatures,
                    pointerTypes, fixedArrayTypes, private105: true))
            {
                throw new ModuleFormatException(
                    "Revision 106 address-contract root differs from a declaration formal.");
            }
        }

        var returnsNormally = parts.Contains("MayReturn",
            StringComparer.Ordinal);
        var doesNotReturn = parts.Contains("DoesNotReturn",
            StringComparer.Ordinal);
        if (returnsNormally == doesNotReturn
            || doesNotReturn != ((signature.EffectsMask & 2) != 0)
            || (callableConvention is NativeCallingConvention selected
                && selected != signature.CallingConvention)
            || (expectedDirectNativeImport
                    is DirectNativeImport directImport
                && directImport.CallingConvention
                    != NativeCallingConvention.Native
                && directImport.CallingConvention
                    != signature.CallingConvention)
            || (expectedDirectNativeImport
                    is DirectNativeImport nativeImport
                && !SameDirectNativeParameters(
                    nativeImport.Parameters, structures, signatures,
                    pointerTypes, signature.NativeParameters, structures,
                    signatures, pointerTypes)))
        {
            throw new ModuleFormatException(
                "Revision 106 address-contract effects or NativeCallable convention differ from the signature row.");
        }
    }

    private static void ValidatePrivate105FunctionPointerTypeReferences(
        string typeName,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures)
    {
        var start = 0;
        while (true)
        {
            var marker = typeName.IndexOf("fnptr#", start,
                StringComparison.Ordinal);
            if (marker < 0)
            {
                return;
            }

            var digitStart = marker + "fnptr#".Length;
            var digitEnd = digitStart;
            while (digitEnd < typeName.Length
                && char.IsAsciiDigit(typeName[digitEnd]))
            {
                digitEnd++;
            }
            if (digitEnd == digitStart
                || (digitEnd - digitStart > 1
                    && typeName[digitStart] == '0')
                || (digitEnd < typeName.Length
                    && typeName[digitEnd] is not ('>' or ',' or ';'
                        or ']' or '?')))
            {
                throw new ModuleFormatException(
                    "Revision 106 function-pointer type reference is malformed.");
            }

            _ = ParsePrivate105FunctionPointerReference(
                typeName[marker..digitEnd], signatures);
            start = digitEnd;
        }
    }

    private static string RemapPrivate105FunctionPointerReferences(
        string contract,
        ImmutableArray<PrimitiveType> functionPointerMapping)
    {
        var result = new StringBuilder(contract.Length);
        var sourceIndex = 0;
        while (true)
        {
            var marker = contract.IndexOf("fnptr#", sourceIndex,
                StringComparison.Ordinal);
            if (marker < 0)
            {
                _ = result.Append(contract.AsSpan(sourceIndex));
                return result.ToString();
            }

            var digitStart = marker + "fnptr#".Length;
            var digitEnd = digitStart;
            while (digitEnd < contract.Length
                && char.IsAsciiDigit(contract[digitEnd]))
            {
                digitEnd++;
            }
            if (digitEnd == digitStart
                || !int.TryParse(contract.AsSpan(digitStart,
                    digitEnd - digitStart), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var index)
                || (digitEnd - digitStart > 1
                    && contract[digitStart] == '0')
                || (uint)index >= (uint)functionPointerMapping.Length
                || !NativeFunctionPointerTypes.IsType(
                    functionPointerMapping[index]))
            {
                throw new ModuleContractException(
                    "Private revision 106 function-pointer contract reference cannot be remapped.");
            }

            _ = result.Append(contract.AsSpan(sourceIndex,
                marker - sourceIndex));
            _ = result.Append("fnptr#")
                .Append(NativeFunctionPointerTypes.Index(
                    functionPointerMapping[index]).ToString(
                    CultureInfo.InvariantCulture));
            sourceIndex = digitEnd;
        }
    }

    private static int ParsePrivate105FunctionPointerReference(
        string reference,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures)
    {
        const string prefix = "fnptr#";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal)
            || !int.TryParse(reference.AsSpan(prefix.Length),
                NumberStyles.None, CultureInfo.InvariantCulture,
                out var index)
            || index < 0 || (uint)index >= (uint)signatures.Length
            || (reference.Length - prefix.Length > 1
                && reference[prefix.Length] == '0'))
        {
            throw new ModuleFormatException(
                "Revision 106 function-pointer type reference is outside the selected table.");
        }

        return index;
    }

    private static NativeCallingConvention
        ParsePrivate105NativeCallableConvention(string token)
        => token switch {
            "Unmanaged" => NativeCallingConvention.Unmanaged,
            "Cdecl" => NativeCallingConvention.Cdecl,
            "Stdcall" => NativeCallingConvention.Stdcall,
            "Vectorcall" => NativeCallingConvention.Vectorcall,
            _ => throw new ModuleFormatException(
                "Revision 106 NativeCallable token has an unknown convention."),
        };

    private static bool IsPrivate105NativeCallableEntry(string token)
    {
        if (token.Length == 0 || (token.Length & 1) != 0
            || token.Any(static character =>
                !char.IsAsciiHexDigit(character)))
        {
            return false;
        }

        var bytes = Convert.FromHexString(token);
        return bytes.All(static value => value <= 0x7F)
            && Convert.ToHexString(bytes) == token
            && DirectNativeImportContract.IsValidExportName(
                Encoding.ASCII.GetString(bytes));
    }

    private static int CountRows(BinaryReader reader, int maximumCount,
        int rowSize, string description)
    {
        var count = Count(reader, maximumCount);
        var byteCount = checked((long)count * rowSize);
        if (reader.BaseStream.CanSeek
            && byteCount > reader.BaseStream.Length
                - reader.BaseStream.Position)
        {
            throw new ModuleFormatException(
                $"Revision 106 {description} exceed the remaining module extent.");
        }

        return count;
    }
}
