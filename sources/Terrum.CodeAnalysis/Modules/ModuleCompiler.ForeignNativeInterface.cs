using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Modules;

public static partial class ModuleCompiler
{
    private sealed record PendingPrivateForeignInterfaceCall(
        int ModuleFunctionIndex, BoundExpression Receiver,
        ImmutableArray<BoundExpression> Arguments,
        ImmutableArray<int> ArgumentParameterIndices,
        PrimitiveType Type, TextSpan Span)
        : BoundExpression(Type, Span)
    {
        internal override bool HasValidNestedExpressions
            => Receiver is not null && !Arguments.IsDefault
                && Arguments.All(static argument => argument is not null)
                && (ArgumentParameterIndices.IsDefault
                    || ArgumentParameterIndices.Length
                        == Arguments.Length);

        internal override ImmutableArray<BoundExpression> NestedExpressions
            => Arguments.IsDefault ? [Receiver] : [Receiver, .. Arguments];

        internal override BoundExpression RewriteNestedExpressions(
            Func<BoundExpression, BoundExpression> rewrite)
            => this with {
                Receiver = rewrite(Receiver),
                Arguments = Arguments.IsDefault
                    ? default : [.. Arguments.Select(rewrite)],
            };
    }

    private static void WritePrivateForeignInterfaceMemberSurface(
        BinaryWriter writer,
        PrivateForeignInterfaceMemberSurface? surface)
    {
        writer.Write((byte)(surface is null ? 0 : 1));
        if (surface is null)
        {
            return;
        }
        if (writer is not ModuleWriter {
                FormatVersion: ForeignInterfacePrivateVersion,
            } moduleWriter
            || surface.ForeignInterfaceIndex < 0
            || surface.ForeignInterfaceIndex
                >= moduleWriter.ForeignInterfaces.Length
            || surface.DeclaredMethodOrdinal < 0
            || surface.Formals.IsDefault
            || surface.Formals.Length + 1 > MaximumSlots)
        {
            throw new ModuleContractException(
                "PF111 foreign-member surface has an invalid owner, ordinal or formal count.");
        }

        writer.Write(checked((uint)(surface.ForeignInterfaceIndex + 1)));
        writer.Write(checked((uint)surface.DeclaredMethodOrdinal));
        WritePrivateType(writer, surface.ResultType);
        WritePrivateType(writer, surface.ReceiverType);
        writer.Write(true);
        writer.Write(surface.DoesNotReturn);
        writer.Write((byte)0);
        writer.Write(surface.Formals.Length);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var formal in surface.Formals)
        {
            if (formal is null || !IsPrivateForeignInterfaceIdentifier(
                    formal.Name) || !names.Add(formal.Name)
                || (formal.HasDefault
                    ? formal.DefaultValue is not BoundConstant constant
                        || constant.Type != formal.Type
                        || !IsPrivateForeignInterfaceFixedScalar(
                            formal.Type)
                        || !constant.Bits.IsCanonicalFor(formal.Type)
                    : formal.DefaultValue is not null))
            {
                throw new ModuleContractException(
                    "PF111 foreign-member formal surface is invalid.");
            }

            PutString(writer, formal.Name);
            WritePrivateType(writer, formal.Type);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)0);
            writer.Write((byte)(formal.HasDefault ? 1 : 0));
            if (formal.HasDefault)
            {
                WritePrivateConstantPayload(writer,
                    formal.DefaultValue
                        ?? throw new ModuleContractException(
                            "PF111 frozen scalar default is missing."));
            }
        }
    }

    private static PrivateForeignInterfaceMemberSurface?
        ReadPrivateForeignInterfaceMemberSurface(BinaryReader reader)
    {
        var kind = reader.ReadByte();
        if (reader is not ModuleReader {
                FormatVersion: ForeignInterfacePrivateVersion,
            } moduleReader)
        {
            if (kind != 0)
            {
                throw new ModuleFormatException(
                    "Foreign-interface member metadata requires PF111.");
            }
            return null;
        }
        if (kind == 0)
        {
            return null;
        }
        if (kind != 1)
        {
            throw new ModuleFormatException(
                "Unknown private foreign-interface member discriminator.");
        }

        var interfaceReference = reader.ReadUInt32();
        var methodOrdinal = reader.ReadUInt32();
        var resultType = GetType(reader);
        var receiverType = GetType(reader);
        var declaredCallerUnsafe = GetBool(reader);
        var doesNotReturn = GetBool(reader);
        var reserved = reader.ReadByte();
        var formalCount = CountRows(reader, MaximumSlots - 1, 14,
            "foreign-interface member formals");
        if (interfaceReference == 0 || interfaceReference > int.MaxValue
            || interfaceReference
                > (uint)moduleReader.ForeignInterfaces.Length
            || methodOrdinal > int.MaxValue || !declaredCallerUnsafe
            || reserved != 0)
        {
            throw new ModuleFormatException(
                "Private foreign-interface member prefix is invalid.");
        }

        var formals = ImmutableArray.CreateBuilder<
            PrivateForeignInterfaceFormal>(formalCount);
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < formalCount; index++)
        {
            var name = GetString(reader);
            var formalType = GetType(reader);
            var mode = reader.ReadByte();
            var flags = reader.ReadByte();
            var formalReserved = reader.ReadUInt16();
            var defaultKind = reader.ReadByte();
            if (!IsPrivateForeignInterfaceIdentifier(name)
                || !names.Add(name) || mode != 0 || flags != 0
                || formalReserved != 0 || defaultKind is not (0 or 1)
                || (defaultKind == 1
                    && !IsPrivateForeignInterfaceFixedScalar(formalType)))
            {
                throw new ModuleFormatException(
                    "Private foreign-interface member formal is invalid.");
            }

            var hasDefault = defaultKind == 1;
            var defaultValue = hasDefault
                ? ReadPrivateConstantPayload(reader, formalType, default)
                : null;
            if (defaultValue is not null
                && !defaultValue.Bits.IsCanonicalFor(formalType))
            {
                throw new ModuleFormatException(
                    "Private foreign-interface scalar default is noncanonical.");
            }
            formals.Add(new PrivateForeignInterfaceFormal(name,
                formalType, hasDefault, defaultValue));
        }

        return new PrivateForeignInterfaceMemberSurface(
            checked((int)interfaceReference - 1),
            checked((int)methodOrdinal), resultType, receiverType,
            doesNotReturn, formals.MoveToImmutable());
    }

    private static void WritePrivateForeignInterfaceBinding(
        BinaryWriter writer, BoundPrivateForeignInterfaceBinding binding)
    {
        if (!IsValidPrivateForeignInterfaceBinding(binding))
        {
            throw new ModuleContractException(
                "Private foreign-interface Binding is invalid.");
        }

        writer.Write((byte)binding.Kind);
        writer.Write(binding.IsExported);
        writer.Write((ushort)0);
        PutString(writer, binding.UsingModuleIdentity);
        PutString(writer, binding.LocalDeclarationKey);
        if (binding.Kind
            == BoundPrivateForeignInterfaceBindingKind.ImportedExpectation)
        {
            PutString(writer, binding.SelectedModuleIdentity!);
            PutString(writer, binding.SelectedExportKey!);
        }
    }

    private static BoundPrivateForeignInterfaceBinding
        ReadPrivateForeignInterfaceBinding(BinaryReader reader)
    {
        var kind = (BoundPrivateForeignInterfaceBindingKind)
            reader.ReadByte();
        var isExported = GetBool(reader);
        var reserved = reader.ReadUInt16();
        var usingModuleIdentity = GetString(reader);
        var localDeclarationKey = GetString(reader);
        string? selectedModuleIdentity = null;
        string? selectedExportKey = null;
        if (kind
            == BoundPrivateForeignInterfaceBindingKind.ImportedExpectation)
        {
            selectedModuleIdentity = GetString(reader);
            selectedExportKey = GetString(reader);
        }
        var binding = new BoundPrivateForeignInterfaceBinding(kind,
            isExported, usingModuleIdentity, localDeclarationKey,
            selectedModuleIdentity, selectedExportKey);
        if (reserved != 0
            || !IsValidPrivateForeignInterfaceBinding(binding))
        {
            throw new ModuleFormatException(
                "Private foreign-interface Binding is malformed.");
        }
        return binding;
    }

    private static bool IsValidPrivateForeignInterfaceBinding(
        BoundPrivateForeignInterfaceBinding binding)
        => binding is not null
            && PrivateModuleIdentityInputs.IsValidModuleIdentity(
                binding.UsingModuleIdentity)
            && PrivateModuleIdentityInputs.IsValidDeclarationKey(
                binding.LocalDeclarationKey)
            && (binding.Kind
                    == BoundPrivateForeignInterfaceBindingKind.OwnedDefinition
                ? binding.SelectedModuleIdentity is null
                    && binding.SelectedExportKey is null
                : binding.Kind
                        == BoundPrivateForeignInterfaceBindingKind
                            .ImportedExpectation
                    && !binding.IsExported
                    && PrivateModuleIdentityInputs.IsValidModuleIdentity(
                        binding.SelectedModuleIdentity)
                    && PrivateModuleIdentityInputs.IsValidDeclarationKey(
                        binding.SelectedExportKey));

    private static bool IsPrivateForeignInterfaceIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name) || name == "_")
        {
            return false;
        }
        var first = true;
        foreach (var rune in name.EnumerateRunes())
        {
            if (rune.Value != '_' && !(first
                    ? UnicodeXid.IsStart(rune.Value)
                    : UnicodeXid.IsContinue(rune.Value)))
            {
                return false;
            }
            first = false;
        }
        return !first;
    }

    private static bool IsPrivateForeignInterfaceFixedScalar(
        PrimitiveType type)
        => type is not (PrimitiveType.NativeInt
                or PrimitiveType.NativeUInt
                or PrimitiveType.NativeRawPointer)
            && !BoundEnumTypes.IsType(type)
            && !NativePointerTypes.IsType(type)
            && !NativeFunctionPointerTypes.IsType(type)
            && !NativeFixedArrayTypes.IsType(type)
            && !CheckedReferenceTypes.IsType(type)
            && !ManagedClassTypes.IsClass(type)
            && PrimitiveTypeFacts.IsBuiltInScalar(type)
            && PrimitiveTypeFacts.ScalarWidth(type) is > 0 and <= 16;

    private static ImmutableArray<PrivateForeignInterface>
        ReadPrivateForeignInterfaceDirectory(BinaryReader reader,
            ImmutableArray<BoundPrivateNominalTypeOrigin> nominalOrigins)
    {
        if (reader is not ModuleReader {
                FormatVersion: CheckedReferenceVersion
                    or ForeignInterfacePrivateVersion,
            } moduleReader)
        {
            return [];
        }

        var maximumOwners
            = moduleReader.FormatVersion == ForeignInterfacePrivateVersion
                ? MaximumFunctions : 0;
        var interfaceCount = CountRows(reader, maximumOwners, 39,
            "foreign-interface directory");
        if (moduleReader.FormatVersion == CheckedReferenceVersion)
        {
            return [];
        }

        var interfaces
            = ImmutableArray.CreateBuilder<PrivateForeignInterface>(
                interfaceCount);
        var localKeys = new Dictionary<string, HashSet<string>>(
            StringComparer.Ordinal);
        foreach (var origin in nominalOrigins)
        {
            if (!TryAddPrivateForeignInterfaceKey(localKeys,
                    origin.UsingModuleIdentity,
                    origin.LocalDeclarationKey))
            {
                throw new ModuleFormatException(
                    "Private nominal and foreign-interface Binding keys collide.");
            }
        }

        var totalMembers = 0;
        for (var interfaceIndex = 0;
            interfaceIndex < interfaceCount;
            interfaceIndex++)
        {
            var binding = ReadPrivateForeignInterfaceBinding(reader);
            var lookupName = GetString(reader);
            var visibility = reader.ReadByte();
            var flags = reader.ReadByte();
            var reserved = reader.ReadUInt16();
            var targetProfileId = reader.ReadUInt32();
            var targetProfileRuleRevision = reader.ReadUInt32();
            var vtableByteOffset = reader.ReadUInt32();
            var methodCount = CountRows(reader,
                MaximumFunctions - totalMembers, 18,
                "foreign-interface member directory");
            if (!IsPrivateForeignInterfaceLookupName(lookupName)
                || visibility is not (0 or 1)
                || flags != 0 || reserved != 0
                || targetProfileId != 1
                || targetProfileRuleRevision != 1
                || methodCount == 0
                || (vtableByteOffset & 7) != 0
                || (ulong)vtableByteOffset + 8 > uint.MaxValue
                || (ulong)methodCount * 8 > uint.MaxValue
                || (binding.Kind
                        == BoundPrivateForeignInterfaceBindingKind
                            .ImportedExpectation
                    && (visibility != 0 || binding.IsExported))
                || (binding.IsExported && visibility != 0)
                || !TryAddPrivateForeignInterfaceKey(localKeys,
                    binding.UsingModuleIdentity,
                    binding.LocalDeclarationKey))
            {
                throw new ModuleFormatException(
                    "Private foreign-interface owner row is invalid.");
            }

            var members
                = ImmutableArray.CreateBuilder<
                    PrivateForeignInterfaceMember>(methodCount);
            for (var memberOrdinal = 0;
                memberOrdinal < methodCount;
                memberOrdinal++)
            {
                var memberBinding
                    = ReadPrivateForeignInterfaceBinding(reader);
                var moduleFunctionIndex = reader.ReadUInt32();
                if (moduleFunctionIndex >= MaximumFunctions
                    || memberBinding.Kind != binding.Kind
                    || memberBinding.IsExported != binding.IsExported
                    || memberBinding.UsingModuleIdentity
                        != binding.UsingModuleIdentity
                    || (binding.Kind
                            == BoundPrivateForeignInterfaceBindingKind
                                .ImportedExpectation
                        && memberBinding.SelectedModuleIdentity
                            != binding.SelectedModuleIdentity)
                    || !TryAddPrivateForeignInterfaceKey(localKeys,
                        memberBinding.UsingModuleIdentity,
                        memberBinding.LocalDeclarationKey))
                {
                    throw new ModuleFormatException(
                        "Private foreign-interface member Binding is inconsistent with its owner.");
                }

                members.Add(new PrivateForeignInterfaceMember(
                    memberBinding, checked((int)moduleFunctionIndex)));
            }

            totalMembers = checked(totalMembers + methodCount);
            interfaces.Add(new PrivateForeignInterface(binding, lookupName,
                visibility == 0, vtableByteOffset,
                members.MoveToImmutable()));
        }
        if (interfaceCount > totalMembers)
        {
            throw new ModuleFormatException(
                "Private foreign-interface directory count exceeds its member count.");
        }
        return interfaces.MoveToImmutable();
    }

    private static void WritePrivateForeignInterfaceDirectory(
        BinaryWriter writer, Module module)
    {
        if (writer is not ModuleWriter
            || module.FormatVersion is not (
                CheckedReferenceVersion or ForeignInterfacePrivateVersion))
        {
            throw new ModuleContractException(
                "Foreign-interface directories require the selected checked-reference private revision.");
        }
        if (module.FormatVersion == CheckedReferenceVersion)
        {
            if (!module.ForeignInterfaces.IsEmpty
                || module.Functions.Any(static item =>
                    item.ForeignInterfaceMember is not null))
            {
                throw new ModuleContractException(
                    "Private revision 106 cannot persist foreign-interface declarations.");
            }
            writer.Write(0);
            return;
        }

        ValidatePrivateForeignInterfaceDirectory(module, reading: false);
        writer.Write(module.ForeignInterfaces.Length);
        foreach (var @interface in module.ForeignInterfaces)
        {
            WritePrivateForeignInterfaceBinding(writer, @interface.Binding);
            PutString(writer, @interface.LookupName);
            writer.Write((byte)(@interface.IsPublic ? 0 : 1));
            writer.Write((byte)0);
            writer.Write((ushort)0);
            writer.Write((uint)1);
            writer.Write((uint)1);
            writer.Write(@interface.VTableByteOffset);
            writer.Write(@interface.Members.Length);
            foreach (var member in @interface.Members)
            {
                WritePrivateForeignInterfaceBinding(writer,
                    member.Binding);
                writer.Write(checked((uint)member.ModuleFunctionIndex));
            }
        }
    }

    private static Module CanonicalizePrivateForeignInterfaceModule(
        Module module)
    {
        if (module.FormatVersion != ForeignInterfacePrivateVersion)
        {
            if (!module.ForeignInterfaces.IsEmpty
                || module.Functions.Any(static item =>
                    item.ForeignInterfaceMember is not null))
            {
                throw new ModuleContractException(
                    "Foreign-interface records require PF111.");
            }
            return module;
        }
        if (module.ForeignInterfaces.IsDefault || module.Functions.IsDefault)
        {
            throw new ModuleContractException(
                "PF111 foreign-interface or ModuleFunction tables are uninitialized.");
        }

        var orderedInterfaces = module.ForeignInterfaces
            .Select((@interface, index) => (@interface, index))
            .OrderBy(static item =>
                    item.@interface.Binding.UsingModuleIdentity,
                StringComparer.Ordinal)
            .ThenBy(static item =>
                    item.@interface.Binding.LocalDeclarationKey,
                StringComparer.Ordinal)
            .ToArray();
        var interfaceMappings = new int[orderedInterfaces.Length];
        for (var index = 0; index < orderedInterfaces.Length; index++)
        {
            interfaceMappings[orderedInterfaces[index].index] = index;
        }

        var functionMappings = new int[module.Functions.Length];
        Array.Fill(functionMappings, -1);
        var functions = ImmutableArray.CreateBuilder<ModuleFunction>(
            module.Functions.Length);
        for (var index = 0; index < module.Functions.Length; index++)
        {
            var function = module.Functions[index];
            if (function.ForeignInterfaceMember is not null)
            {
                continue;
            }
            functionMappings[index] = functions.Count;
            functions.Add(function);
        }

        foreach (var orderedInterface in orderedInterfaces)
        {
            foreach (var member in orderedInterface.@interface.Members)
            {
                if ((uint)member.ModuleFunctionIndex
                        >= (uint)module.Functions.Length
                    || functionMappings[member.ModuleFunctionIndex] >= 0)
                {
                    throw new ModuleContractException(
                        "PF111 foreign-interface directory has an invalid or duplicate ModuleFunction reference.");
                }
                functionMappings[member.ModuleFunctionIndex]
                    = functions.Count;
                functions.Add(module.Functions[
                    member.ModuleFunctionIndex]);
            }
        }

        if (functionMappings.Any(static mapping => mapping < 0))
        {
            throw new ModuleContractException(
                "PF111 has an unreferenced foreign ModuleFunction.");
        }

        var remappedFunctions
            = ImmutableArray.CreateBuilder<ModuleFunction>(
                functions.Count);
        foreach (var function in functions)
        {
            if (function.ForeignInterfaceMember
                is PrivateForeignInterfaceMemberSurface surface)
            {
                if ((uint)surface.ForeignInterfaceIndex
                    >= (uint)interfaceMappings.Length)
                {
                    throw new ModuleContractException(
                        "PF111 foreign ModuleFunction has an invalid interface reference.");
                }
                remappedFunctions.Add(function with {
                    ForeignInterfaceMember = surface with {
                        ForeignInterfaceIndex = interfaceMappings[
                            surface.ForeignInterfaceIndex],
                    },
                });
            }
            else
            {
                remappedFunctions.Add(function);
            }
        }

        var remappedInterfaces
            = ImmutableArray.CreateBuilder<PrivateForeignInterface>(
                orderedInterfaces.Length);
        foreach (var (foreignInterface, _) in orderedInterfaces)
        {
            var members
                = ImmutableArray.CreateBuilder<
                    PrivateForeignInterfaceMember>(
                    foreignInterface.Members.Length);
            foreach (var member in foreignInterface.Members)
            {
                members.Add(member with {
                    ModuleFunctionIndex = functionMappings[
                        member.ModuleFunctionIndex],
                });
            }
            remappedInterfaces.Add(foreignInterface with {
                Members = members.MoveToImmutable(),
            });
        }

        for (var index = 0; index < remappedFunctions.Count; index++)
        {
            var function = remappedFunctions[index];
            if (function.Function is not BoundFunction body)
            {
                continue;
            }

            var rewritten = BoundTreeTraversal.RewriteExpressions(body,
                expression =>
                {
                    if (expression is not
                        PendingPrivateForeignInterfaceCall pending)
                    {
                        return expression;
                    }
                    if ((uint)pending.ModuleFunctionIndex
                            >= (uint)functionMappings.Length
                        || functionMappings[
                            pending.ModuleFunctionIndex] < 0)
                    {
                        throw new ModuleContractException(
                            "PF111 foreign call target has no canonical ModuleFunction mapping.");
                    }

                    return pending with {
                        ModuleFunctionIndex = functionMappings[
                            pending.ModuleFunctionIndex],
                    };
                });
            remappedFunctions[index] = function with {
                Function = rewritten,
            };
        }

        var entryPoint = module.EntryPoint;
        if (entryPoint >= 0)
        {
            if ((uint)entryPoint >= (uint)functionMappings.Length)
            {
                throw new ModuleContractException(
                    "PF111 entry point does not select a ModuleFunction.");
            }
            entryPoint = functionMappings[entryPoint];
        }

        return module with {
            EntryPoint = entryPoint,
            Functions = remappedFunctions.MoveToImmutable(),
            ForeignInterfaces = remappedInterfaces.MoveToImmutable(),
        };
    }

    private static void ValidatePrivateForeignInterfaceDirectory(
        Module module, bool reading)
        => ValidatePrivateForeignInterfaceDirectory(module, reading,
            out _);

    private static void ValidatePrivateForeignInterfaceDirectory(
        Module module, bool reading,
        out ImmutableArray<BoundInterfaceDefinition> reconstructedInterfaces)
    {
        reconstructedInterfaces = [];
        [DoesNotReturn]
        void Reject(string message)
        {
            if (reading)
            {
                throw new ModuleFormatException(message);
            }
            throw new ModuleContractException(message);
        }

        if (module.ForeignInterfaces.IsDefault
            || module.Functions.IsDefault)
        {
            Reject("Private foreign-interface tables are uninitialized.");
        }

        if (module.FormatVersion == CheckedReferenceVersion)
        {
            if (!module.ForeignInterfaces.IsEmpty
                || module.Functions.Any(static item =>
                    item.ForeignInterfaceMember is not null))
            {
                Reject("Private revision 106 cannot retain foreign-interface declarations.");
            }
            return;
        }
        if (module.FormatVersion != ForeignInterfacePrivateVersion)
        {
            if (!module.ForeignInterfaces.IsEmpty
                || module.Functions.Any(static item =>
                    item.ForeignInterfaceMember is not null))
            {
                Reject("Foreign-interface declarations require PF111.");
            }
            return;
        }

        if (module.ForeignInterfaces.Length > MaximumFunctions
            || module.Functions.Length > MaximumFunctions)
        {
            Reject("PF111 foreign-interface tables exceed the ModuleFunction limit.");
        }
        var nominalKeys = new Dictionary<string, HashSet<string>>(
            StringComparer.Ordinal);
        foreach (var origin in module.NominalTypeOrigins)
        {
            if (!TryAddPrivateForeignInterfaceKey(nominalKeys,
                    origin.UsingModuleIdentity,
                    origin.LocalDeclarationKey))
            {
                Reject("Private nominal Binding keys are not unique.");
            }
        }
        var interfaces
            = ImmutableArray.CreateBuilder<BoundInterfaceDefinition>(
                module.ForeignInterfaces.Length);
        var referencedFunctions = new HashSet<int>();
        var foreignExportKeys = new HashSet<(string UsingModuleIdentity,
            string ExportKey)>();
        var totalMembers = 0;
        for (var interfaceIndex = 0;
            interfaceIndex < module.ForeignInterfaces.Length;
            interfaceIndex++)
        {
            var foreignInterface
                = module.ForeignInterfaces[interfaceIndex];
            var binding = foreignInterface?.Binding;
            var members = foreignInterface?.Members
                ?? default;
            if (foreignInterface is null || binding is null
                || !IsValidPrivateForeignInterfaceBinding(binding)
                || !IsPrivateForeignInterfaceLookupName(
                    foreignInterface.LookupName)
                || members.IsDefaultOrEmpty
                || members.Length > MaximumFunctions
                || (binding.Kind
                        == BoundPrivateForeignInterfaceBindingKind
                            .ImportedExpectation
                    && (!foreignInterface.IsPublic
                        || binding.IsExported))
                || (binding.IsExported && !foreignInterface.IsPublic)
                || (module.Consumer && binding.IsExported)
                || (foreignInterface.VTableByteOffset & 7) != 0
                || (ulong)foreignInterface.VTableByteOffset + 8
                    > uint.MaxValue
                || (ulong)members.Length * 8 > uint.MaxValue
                || !TryAddPrivateForeignInterfaceKey(nominalKeys,
                    binding.UsingModuleIdentity,
                    binding.LocalDeclarationKey))
            {
                Reject("Private foreign-interface owner directory row is invalid.");
            }

            var memberBindings
                = ImmutableArray.CreateBuilder<
                    BoundPrivateForeignInterfaceBinding>(members.Length);
            var boundMembers
                = ImmutableArray.CreateBuilder<
                    BoundInterfaceMemberDefinition>(members.Length);
            for (var memberOrdinal = 0;
                memberOrdinal < members.Length;
                memberOrdinal++)
            {
                var memberReference = members[memberOrdinal];
                var memberBinding = memberReference?.Binding;
                if (memberReference is null || memberBinding is null
                    || !IsValidPrivateForeignInterfaceBinding(
                        memberBinding)
                    || memberBinding.Kind != binding.Kind
                    || memberBinding.IsExported != binding.IsExported
                    || memberBinding.UsingModuleIdentity
                        != binding.UsingModuleIdentity
                    || (binding.Kind
                            == BoundPrivateForeignInterfaceBindingKind
                                .ImportedExpectation
                        && memberBinding.SelectedModuleIdentity
                            != binding.SelectedModuleIdentity)
                    || memberBinding.IsExported != binding.IsExported
                    || !TryAddPrivateForeignInterfaceKey(nominalKeys,
                        memberBinding.UsingModuleIdentity,
                        memberBinding.LocalDeclarationKey)
                    || (uint)memberReference.ModuleFunctionIndex
                        >= (uint)module.Functions.Length
                    || !referencedFunctions.Add(
                        memberReference.ModuleFunctionIndex))
                {
                    Reject("Private foreign-interface member Binding or function reference is invalid.");
                }

                var function = module.Functions[
                    memberReference.ModuleFunctionIndex];
                var surface = function.ForeignInterfaceMember;
                var isImported
                    = binding.Kind
                        == BoundPrivateForeignInterfaceBindingKind
                            .ImportedExpectation;
                if (function.Function is not null
                    || function.Import != isImported
                    || function.HasOwnershipTransfer
                    || function.ImportedNativeVTableMethod is not null
                    || function.ExpectedDirectNativeImport is not null
                    || function.HasDeclaredRawUnionReturnCoverage
                    || !function.DeclaredRawUnionReturnCoverage.IsEmpty
                    || function.ExpectedCopyOriginSummaryData is not null
                    || function.HasPrivateCheckedOutputStateMetadata
                    || function.CheckedOutputSelectors.IsDefault
                    || !function.CheckedOutputSelectors.IsEmpty
                    || function.CheckedOutputStates.IsDefault
                    || !function.CheckedOutputStates.IsEmpty
                    || function.GeneralDependencyContracts.IsDefault
                    || function.NativeOutputContracts.IsDefault
                    || surface is null
                    || surface.ForeignInterfaceIndex != interfaceIndex
                    || surface.DeclaredMethodOrdinal != memberOrdinal
                    || surface.Formals.IsDefault
                    || surface.Formals.Length + 1 > MaximumSlots
                    || !IsPrivateForeignInterfaceIdentifier(
                        function.Name)
                    || !TryGetPrivateForeignInterfaceConvention(
                        function.Contract, out var convention))
                {
                    Reject("Private foreign-interface ModuleFunction has an invalid declaration surface.");
                }

                if ((!function.NativeOutputContracts.IsEmpty
                        && function.NativeOutputRole
                            != NativeOutputDeclarationRole
                                .ForeignInterfaceMethod)
                    || (function.NativeOutputContracts.IsEmpty
                        && function.NativeOutputRole is not null)
                    || function.NativeOutputContracts.Any(output =>
                        (uint)output.FormalOrdinal
                            >= (uint)surface.Formals.Length
                        || surface.Formals[output.FormalOrdinal]
                            .HasDefault))
                {
                    Reject("Private foreign-interface NativeOutput collection is invalid.");
                }

                var parameters
                    = ImmutableArray.CreateBuilder<
                        BoundInterfaceParameter>(surface.Formals.Length);
                var formalNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var formal in surface.Formals)
                {
                    if (formal is null
                        || !IsPrivateForeignInterfaceIdentifier(
                            formal.Name)
                        || !formalNames.Add(formal.Name)
                        || (formal.HasDefault
                            ? formal.DefaultValue is not BoundConstant
                                defaultValue
                                || defaultValue.Type != formal.Type
                                || !IsPrivateForeignInterfaceFixedScalar(
                                    formal.Type)
                            : formal.DefaultValue is not null))
                    {
                        Reject("Private foreign-interface formal or frozen default is invalid.");
                    }

                    parameters.Add(new BoundInterfaceParameter(
                        formal.Name,
                        PrivateForeignInterfaceType(formal.Type,
                            module.NativePointerTypes),
                        BoundInterfaceParameterMode.Value, [], []) {
                        DefaultValue = formal.DefaultValue,
                    });
                }

                try
                {
                    _ = CreatePrivate106GeneralDependencyRows(
                        function.GeneralDependencyContracts,
                        function.Contract, function.NativeOutputContracts,
                        surface, module.NativePointerTypes);
                }
                catch (ModuleContractException exception)
                {
                    Reject(
                        $"Private foreign-interface Dependency contract is invalid: {exception.Message}");
                }

                var dependencies
                    = PrivateForeignInterfaceDependencies(function);
                var resultType = PrivateForeignInterfaceType(
                    surface.ResultType, module.NativePointerTypes);
                var member = new BoundInterfaceMemberDefinition(
                    memberOrdinal, function.Name,
                    memberBinding.LocalDeclarationKey,
                    BoundInterfaceMemberKind.Callable,
                    BoundInterfaceAccessibility.Public,
                    IsStatic: false, IsExternal: false, IsPartial: false,
                    RequiresUnsafeCaller: true,
                    DoesNotReturn: surface.DoesNotReturn,
                    IsAsync: false, HasBody: false,
                    Attributes: [], AttributeTokens: [],
                    ContractTokens: [], Operator: default,
                    ResultType: resultType,
                    TypeParameters: [], Constraints: [],
                    Parameters: parameters.MoveToImmutable(),
                    Dependencies: dependencies, Accessors: [],
                    AssociatedDefaultType: null,
                    AssociatedConstantType: null,
                    AssociatedConstantDefault: null) {
                    DeclaredRequiresUnsafeCaller = true,
                    RequiresMutableReceiver =
                        IsPrivateForeignInterfaceWritableVoidPointer(
                            surface.ReceiverType,
                            module.NativePointerTypes),
                    NativeCallableConvention = convention,
                    NativeOutputContracts = function.NativeOutputContracts,
                };
                if (surface.Formals.Any(formal =>
                        !IsRevision100RuntimeType(formal.Type,
                            module.Structures,
                            module.NativeFixedArrayTypes,
                            module.NativePointerTypes,
                            module.NativeFunctionPointerSignatures))
                    || !IsRevision100RuntimeType(surface.ResultType,
                        module.Structures, module.NativeFixedArrayTypes,
                        module.NativePointerTypes,
                        module.NativeFunctionPointerSignatures)
                    || !IsRevision100RuntimeType(surface.ReceiverType,
                        module.Structures, module.NativeFixedArrayTypes,
                        module.NativePointerTypes,
                        module.NativeFunctionPointerSignatures))
                {
                    Reject("Private foreign-interface member signature contains an unsupported runtime type.");
                }

                memberBindings.Add(memberBinding);
                boundMembers.Add(member);
                if (memberBinding.IsExported
                    && !foreignExportKeys.Add((
                        memberBinding.UsingModuleIdentity,
                        memberBinding.LocalDeclarationKey)))
                {
                    Reject("PF111 foreign member export keys are duplicated.");
                }
            }

            totalMembers = checked(totalMembers + members.Length);
            if (binding.IsExported
                && !foreignExportKeys.Add((
                    binding.UsingModuleIdentity,
                    binding.LocalDeclarationKey)))
            {
                Reject("PF111 foreign owner export keys are duplicated.");
            }
            var privateOrigin = new BoundPrivateForeignInterfaceOrigin(
                binding, memberBindings.MoveToImmutable());
            var definition = new BoundInterfaceDefinition(
                foreignInterface.LookupName,
                foreignInterface.IsPublic, IsPartial: false,
                Formals: [], Bases: [], Constraints: [],
                Members: boundMembers.MoveToImmutable())
            {
                Accessibility = foreignInterface.IsPublic
                    ? BoundInterfaceAccessibility.Public
                    : BoundInterfaceAccessibility.Internal,
                AttributeTokens = [],
                ModuleImport = binding.Kind
                    == BoundPrivateForeignInterfaceBindingKind
                        .ImportedExpectation
                    ? new BoundModuleImport(
                        binding.SelectedModuleIdentity!,
                        binding.SelectedExportKey!, 1,
                        (uint)module.RuntimePolicy)
                    : null,
                ForeignNativeInterface =
                    new BoundForeignNativeInterfaceLayout(
                        foreignInterface.VTableByteOffset),
                PrivateForeignInterfaceOrigin = privateOrigin,
                SourceInputOrdinal = -1,
            };
            if (!BoundPrivateForeignInterfaceOriginContract.IsValid(
                    definition))
            {
                Reject("Private foreign-interface owner/member Binding graph is inconsistent.");
            }
            interfaces.Add(definition);
        }

        if (totalMembers > MaximumFunctions
            || module.Functions.Any((item, index) =>
                (item.ForeignInterfaceMember is not null)
                != referencedFunctions.Contains(index)))
        {
            Reject("Private foreign-interface directory has unreferenced or missing ModuleFunction rows.");
        }
        if (!module.Consumer)
        {
            var ordinaryExports = module.Functions
                .Where(static item => item.ForeignInterfaceMember is null
                    && item.Import)
                .Select(static item => item.Name)
                .ToHashSet(StringComparer.Ordinal);
            if (foreignExportKeys.Any(item =>
                    ordinaryExports.Contains(item.ExportKey)))
            {
                Reject("PF111 foreign Binding export keys collide with ordinary ModuleFunction exports.");
            }
        }
        else if (module.EntryPoint >= 0
            && (uint)module.EntryPoint < (uint)module.Functions.Length
            && module.Functions[module.EntryPoint].ForeignInterfaceMember
                is not null)
        {
            Reject("PF111 consumer entry point cannot name a foreign member.");
        }

        var reconstructed = interfaces.MoveToImmutable();
        if (!BoundInterfaceContract.IsValidTable(reconstructed,
                static _ => true)
            || !ForeignNativeInterfaceContract.IsValidTable(reconstructed,
                module.NativePointerTypes,
                module.NativeFunctionPointerSignatures,
                nativeCoreRegistration: null)
            || !NativeOutputContractValidator.IsValidInterfaceTable(
                reconstructed, module.Structures,
                module.NativePointerTypes,
                module.NativeFunctionPointerSignatures,
                module.EnumDefinitions, nativeCoreRegistration: null,
                nativeOutputCoreRegistration: null))
        {
            Reject("Private foreign-interface declarations or output contracts are invalid.");
        }
        if (!HasPrivateForeignInterfaceCopyEvidence(reconstructed, module))
        {
            Reject("PF111 foreign-interface aggregate outputs require positive Copy evidence and a complete rootless layout closure.");
        }
        if (!HasPrivateForeignInterfaceNominalTypeClosure(module,
                reconstructed))
        {
            Reject("PF111 foreign-interface signatures and aggregate storage require complete private nominal type origins.");
        }
        reconstructedInterfaces = reconstructed;
    }

    private static bool HasPrivateForeignInterfaceNominalTypeClosure(
        Module module, ImmutableArray<BoundInterfaceDefinition> interfaces)
    {
        if (module.NominalTypeOrigins.IsDefault)
        {
            return false;
        }

        var visitedTypes = new HashSet<PrimitiveType>();
        foreach (var definition in interfaces)
        {
            foreach (var member in definition.Members)
            {
                if (member.ResultType is not BoundInterfaceType resultType
                    || resultType.Kind
                        != BoundInterfaceTypeKind.Primitive
                    || !Visit(resultType.PrimitiveType))
                {
                    return false;
                }
                if (!ForeignNativeInterfaceContract.TryGetReceiverType(
                        member, module.NativePointerTypes,
                        out var receiverType)
                    || !Visit(receiverType))
                {
                    return false;
                }
                foreach (var parameter in member.Parameters)
                {
                    if (parameter.Type.Kind
                            != BoundInterfaceTypeKind.Primitive
                        || !Visit(parameter.Type.PrimitiveType))
                    {
                        return false;
                    }
                }
                foreach (var output in member.NativeOutputContracts)
                {
                    if (!Visit(output.PointeeType))
                    {
                        return false;
                    }
                }
            }
        }
        return true;

        bool Visit(PrimitiveType type)
        {
            if (!visitedTypes.Add(type))
            {
                return true;
            }
            if (NativeFixedArrayTypes.IsType(type)
                || CheckedReferenceTypes.IsType(type)
                || ManagedClassTypes.IsManaged(type))
            {
                return false;
            }
            if (BoundEnumTypes.IsType(type))
            {
                return BoundEnumTypes.IsValidType(type,
                        module.EnumDefinitions)
                    && HasNominalOrigin(type);
            }
            if (NativePointerTypes.IsType(type))
            {
                return NativePointerTypeContract.IsValidType(type,
                        module.NativePointerTypes)
                    && Visit(module.NativePointerTypes[
                        NativePointerTypes.Index(type)].PointeeType);
            }
            if (NativeFunctionPointerTypes.IsType(type))
            {
                if (!NativeFunctionPointerContract.IsValidType(type,
                        module.NativeFunctionPointerSignatures))
                {
                    return false;
                }
                var signature = module.NativeFunctionPointerSignatures[
                    NativeFunctionPointerTypes.Index(type)];
                return Visit(signature.ReturnType)
                    && signature.ParameterTypes.All(Visit)
                    && signature.NativeParameters.All(parameter =>
                        Visit(parameter.PointeeType));
            }
            if (type is PrimitiveType.Void
                or PrimitiveType.NativeRawPointer
                or PrimitiveType.NativeUtf16ZPointer
                or PrimitiveType.NativeUtf8ZPointer
                || PrimitiveTypeFacts.ScalarWidth(type) > 0)
            {
                return true;
            }

            var structureIndex = (int)type - 100;
            if (structureIndex < 0
                || (uint)structureIndex
                    >= (uint)module.Structures.Length
                || !HasNominalOrigin(type))
            {
                return false;
            }

            return module.Structures[structureIndex].Fields.All(field =>
                Visit(field.Type));
        }

        bool HasNominalOrigin(PrimitiveType type)
            => module.NominalTypeOrigins.Any(origin =>
                origin.PrivateType == type);
    }

    private static bool HasPrivateForeignInterfaceCopyEvidence(
        ImmutableArray<BoundInterfaceDefinition> interfaces, Module module)
    {
        var validatedStructures = new HashSet<int>();
        var visitingStructures = new HashSet<int>();

        foreach (var definition in interfaces)
        {
            foreach (var output in definition.Members.SelectMany(
                         static member => member.NativeOutputContracts))
            {
                if (!HasTypeCopyEvidence(output.PointeeType, module))
                {
                    return false;
                }
            }
        }
        return true;

        bool HasTypeCopyEvidence(PrimitiveType type, Module currentModule)
        {
            if (NativeFixedArrayTypes.IsType(type)
                || CheckedReferenceTypes.IsType(type)
                || ManagedClassTypes.IsManaged(type))
            {
                return false;
            }
            if (type is PrimitiveType.NativeRawPointer
                    or PrimitiveType.NativeUtf16ZPointer
                    or PrimitiveType.NativeUtf8ZPointer
                || PrimitiveTypeFacts.ScalarWidth(type) > 0
                || BoundEnumTypes.IsValidType(type,
                    currentModule.EnumDefinitions))
            {
                return true;
            }
            if (NativePointerTypes.IsType(type))
            {
                return NativePointerTypeContract.IsValidType(type,
                    currentModule.NativePointerTypes);
            }
            if (NativeFunctionPointerTypes.IsType(type))
            {
                return NativeFunctionPointerContract.IsValidType(type,
                    currentModule.NativeFunctionPointerSignatures);
            }

            var structureIndex = (int)type - 100;
            if (structureIndex < 0
                || (uint)structureIndex
                    >= (uint)currentModule.Structures.Length)
            {
                return false;
            }
            if (validatedStructures.Contains(structureIndex))
            {
                return true;
            }
            if (!visitingStructures.Add(structureIndex))
            {
                return false;
            }

            var structure = currentModule.Structures[structureIndex];
            var valid = structure.DeclaredCopyEvidence == true
                && !structure.IsUnique
                && structure.CleanupMethod is null
                && structure.Size > 0
                && NativeFixedArrayContract.IsRootlessCopyFieldType(
                    type, currentModule.Structures,
                    currentModule.NativeFixedArrayTypes,
                    currentModule.CheckedReferenceTypes)
                && structure.Fields.All(field =>
                    !NativeFixedArrayTypes.IsType(field.Type)
                    && !CheckedReferenceTypes.IsType(field.Type)
                    && !ManagedClassTypes.IsManaged(field.Type)
                    && HasTypeCopyEvidence(field.Type, currentModule));
            _ = visitingStructures.Remove(structureIndex);
            if (valid)
            {
                validatedStructures.Add(structureIndex);
            }
            return valid;
        }
    }

    private static ImmutableArray<BoundInterfaceDefinition>
        ReconstructPrivateForeignInterfaces(Module module)
    {
        ValidatePrivateForeignInterfaceDirectory(module, reading: true,
            out var interfaces);
        return interfaces;
    }

    private static bool IsPrivateForeignInterfaceLookupName(string name)
        => !string.IsNullOrEmpty(name)
            && name.Split('.').All(IsPrivateForeignInterfaceIdentifier);

    private static BoundInterfaceType PrivateForeignInterfaceType(
        PrimitiveType type,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes)
    {
        var isWritable = NativePointerTypes.IsType(type)
            && (uint)NativePointerTypes.Index(type)
                < (uint)pointerTypes.Length
            && pointerTypes[NativePointerTypes.Index(type)].IsWritable;
        return new BoundInterfaceType(BoundInterfaceTypeKind.Primitive,
            type, null, -1, [], isWritable, false, false, false);
    }

    private static bool IsPrivateForeignInterfaceWritableVoidPointer(
        PrimitiveType type,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes)
        => NativePointerTypes.IsType(type)
            && (uint)NativePointerTypes.Index(type)
                < (uint)pointerTypes.Length
            && pointerTypes[NativePointerTypes.Index(type)]
                is { PointeeType: PrimitiveType.Void, IsWritable: true };

    private static bool TryGetPrivateForeignInterfaceConvention(
        string contract, out NativeCallingConvention convention)
    {
        convention = contract switch {
            "|NativeCallable:Unmanaged" =>
                NativeCallingConvention.Unmanaged,
            "|NativeCallable:Cdecl" => NativeCallingConvention.Cdecl,
            "|NativeCallable:Stdcall" => NativeCallingConvention.Stdcall,
            _ => default,
        };
        return IsPrivateForeignInterfaceMemberContract(contract);
    }

    private static ImmutableArray<BoundDependencyContract>
        PrivateForeignInterfaceDependencies(ModuleFunction function)
    {
        var dependencies = function.GeneralDependencyContracts;
        var outputs = function.NativeOutputContracts;
        if (dependencies.IsDefault || outputs.IsDefault)
        {
            throw new ModuleFormatException(
                "PF111 foreign-interface Dependency collections are uninitialized.");
        }

        var result
            = ImmutableArray.CreateBuilder<BoundDependencyContract>(
                dependencies.Length + outputs.Length * 2);
        result.AddRange(dependencies);
        foreach (var output in outputs)
        {
            if (output is null || output.Dependencies.IsDefaultOrEmpty)
            {
                throw new ModuleFormatException(
                    "PF111 NativeOutput dependency rows are missing.");
            }
            if (output.Dependencies is [
                    {
                        SourceKind:
                            NativeOutputDependencySourceKind.Empty,
                        Relationship:
                            NativeOutputDependencyRelationship.Empty,
                    },
                ])
            {
                result.Add(new BoundDependencyContract(
                    BoundDependencyRelation.DependsOn,
                    BoundDependencyDestination.InitializedOutput,
                    output.FormalOrdinal, []));
                continue;
            }

            var sources
                = ImmutableArray.CreateBuilder<BoundDependencySource>(
                    output.Dependencies.Length);
            var forwardedSources
                = ImmutableArray.CreateBuilder<BoundDependencySource>();
            foreach (var dependency in output.Dependencies)
            {
                var source = dependency.SourceKind switch {
                    NativeOutputDependencySourceKind.Formal
                        when dependency.SourceOrdinal >= 0
                        => new BoundDependencySource(
                            BoundDependencySourceKind.Formal,
                            dependency.SourceOrdinal),
                    NativeOutputDependencySourceKind.Receiver
                        when dependency.SourceOrdinal == 0
                        => new BoundDependencySource(
                            BoundDependencySourceKind.Receiver, null),
                    _ => throw new ModuleFormatException(
                        "PF111 NativeOutput dependency source is invalid."),
                };
                sources.Add(source);
                if (dependency.Relationship
                    == NativeOutputDependencyRelationship.Forward)
                {
                    forwardedSources.Add(source);
                }
            }
            result.Add(new BoundDependencyContract(
                BoundDependencyRelation.DependsOn,
                BoundDependencyDestination.InitializedOutput,
                output.FormalOrdinal, sources.MoveToImmutable()));
            if (forwardedSources.Count != 0)
            {
                result.Add(new BoundDependencyContract(
                    BoundDependencyRelation.ForwardsBacking,
                    BoundDependencyDestination.InitializedOutput,
                    output.FormalOrdinal,
                    forwardedSources.ToImmutable()));
            }
        }
        return result.ToImmutable();
    }

    private static (
        ImmutableArray<PrivateForeignInterface> Interfaces,
        ImmutableArray<ModuleFunction> Functions)
        CreatePrivateForeignInterfaceModuleRecords(
            BoundProgram program, PrivateModuleIdentityInputs? identityInputs,
            int? selectedFormatVersion, bool isConsumer,
            int firstModuleFunctionIndex,
            Func<PrimitiveType, PrimitiveType> remapType)
    {
        var foreignInterfaceIndexes = new List<int>();
        for (var index = 0; index < program.Interfaces.Length; index++)
        {
            if (program.Interfaces[index].ForeignNativeInterface is not null)
            {
                foreignInterfaceIndexes.Add(index);
            }
            else
            {
                throw new ModuleContractException(
                    "The private semantic module format does not preserve ordinary interface declarations.");
            }
        }
        if (foreignInterfaceIndexes.Count == 0)
        {
            return ([], []);
        }
        if (selectedFormatVersion != ForeignInterfacePrivateVersion
            || program.Interfaces.IsDefault
            || (identityInputs is not null
                && (!identityInputs.IsFor(program)
                    || identityInputs.ForeignInterfaceOrigins.Length
                        != program.Interfaces.Length)))
        {
            throw new ModuleContractException(
                "PF111 foreign-interface declarations require the exact selected private schema and snapshot-bound identities.");
        }
        if (!ForeignNativeInterfaceContract.IsValidTable(program)
            || !NativeOutputContractValidator.IsValidInterfaceTable(program))
        {
            throw new ModuleContractException(
                "The source foreign-interface declaration table is invalid.");
        }

        var interfaces
            = ImmutableArray.CreateBuilder<PrivateForeignInterface>(
                foreignInterfaceIndexes.Count);
        var functions = ImmutableArray.CreateBuilder<ModuleFunction>();
        foreach (var (interfaceIndex, privateInterfaceIndex)
            in foreignInterfaceIndexes.Select(static (value, index) =>
                (value, index)))
        {
            var definition = program.Interfaces[interfaceIndex];
            var origin = identityInputs is not null
                ? identityInputs.ForeignInterfaceOrigins[interfaceIndex]
                : definition.PrivateForeignInterfaceOrigin;
            if (origin is null
                || origin.Members.Length != definition.Members.Length
                || !definition.Formals.IsEmpty || !definition.Bases.IsEmpty
                || !definition.Constraints.IsEmpty
                || ((definition.ModuleImport is null)
                    != (origin.Owner.Kind
                        == BoundPrivateForeignInterfaceBindingKind
                            .OwnedDefinition))
                || (isConsumer && origin.Owner.IsExported)
                || (origin.Owner.Kind
                        == BoundPrivateForeignInterfaceBindingKind
                            .ImportedExpectation
                    && origin.Owner.IsExported))
            {
                throw new ModuleContractException(
                    "PF111 foreign-interface ownership or complete member identity is invalid for this module role.");
            }

            var sourceView = definition with {
                Members = [.. definition.Members.Select(
                    (member, memberOrdinal) => member with {
                        DeclarationKey = origin.Members[memberOrdinal]
                            .LocalDeclarationKey,
                    })],
                PrivateForeignInterfaceOrigin = origin,
            };
            if (!BoundPrivateForeignInterfaceOriginContract.IsValid(
                    sourceView))
            {
                throw new ModuleContractException(
                    "PF111 foreign-interface identity differs from its checked declaration view.");
            }

            var memberReferences
                = ImmutableArray.CreateBuilder<
                    PrivateForeignInterfaceMember>(
                    definition.Members.Length);
            for (var memberOrdinal = 0;
                memberOrdinal < definition.Members.Length;
                memberOrdinal++)
            {
                var member = definition.Members[memberOrdinal];
                var binding = origin.Members[memberOrdinal];
                if (member.Ordinal != memberOrdinal
                    || member.ResultType is not BoundInterfaceType {
                        Kind: BoundInterfaceTypeKind.Primitive,
                    } resultType
                    || member.Parameters.IsDefault
                    || member.Parameters.Any(parameter =>
                        parameter is null
                        || parameter.Mode
                            != BoundInterfaceParameterMode.Value
                        || parameter.Type.Kind
                            != BoundInterfaceTypeKind.Primitive)
                    || !ForeignNativeInterfaceContract.TryGetReceiverType(
                        member, program.NativePointerTypes,
                        out var receiverType)
                    || (binding.Kind
                            == BoundPrivateForeignInterfaceBindingKind
                                .ImportedExpectation
                        && binding.SelectedModuleIdentity
                            != definition.ModuleImport?.ProducerIdentity)
                {
                    throw new ModuleContractException(
                        "PF111 foreign-interface member is not a complete native value declaration.");
                }

                var outputs = member.NativeOutputContracts;
                if (outputs.IsDefault
                    || member.Dependencies.IsDefault)
                {
                    throw new ModuleContractException(
                        "PF111 foreign-interface output or dependency contracts are uninitialized.");
                }
                var outputFormals = outputs.Select(
                        static output => output.FormalOrdinal)
                    .ToHashSet();
                var formals
                    = ImmutableArray.CreateBuilder<
                        PrivateForeignInterfaceFormal>(
                        member.Parameters.Length);
                foreach (var parameter in member.Parameters)
                {
                    var parameterType = parameter.Type.PrimitiveType;
                    var hasDefault = parameter.DefaultValue
                        is BoundConstant defaultValue;
                    if (parameter.HasPendingDefault
                        || (hasDefault
                            && (defaultValue.Type != parameterType
                                || !IsPrivateForeignInterfaceFixedScalar(
                                    parameterType)
                                || !defaultValue.Bits
                                    .IsCanonicalFor(parameterType)))
                        || (!hasDefault
                            && parameter.DefaultValue is not null)
                        || (hasDefault
                            && outputFormals.Contains(
                                formals.Count)))
                    {
                        throw new ModuleContractException(
                            "PF111 foreign-interface default is not an admitted frozen scalar or overlaps an output formal.");
                    }

                    formals.Add(new PrivateForeignInterfaceFormal(
                        parameter.Name, remapType(parameterType),
                        hasDefault,
                        hasDefault
                            ? BoundTreeTraversal.RewriteTypes(
                                defaultValue!, remapType)
                            : null));
                }

                var surface = new PrivateForeignInterfaceMemberSurface(
                    privateInterfaceIndex, memberOrdinal,
                    remapType(resultType.PrimitiveType),
                    remapType(receiverType), member.DoesNotReturn,
                    formals.MoveToImmutable());
                var moduleFunctionIndex = checked(
                    firstModuleFunctionIndex + functions.Count);
                var outputContracts = [.. outputs.Select(output =>
                    output with {
                        PointeeType = remapType(output.PointeeType),
                    })];
                var nativeOutputRole = outputContracts.IsEmpty
                    ? null
                    : NativeOutputDeclarationRole.ForeignInterfaceMethod;
                functions.Add(new ModuleFunction(member.Name,
                    PrivateForeignInterfaceConventionContract(
                        member.NativeCallableConvention
                            ?? throw new ModuleContractException(
                                "PF111 foreign-interface member has no convention.")),
                    null,
                    binding.Kind
                        == BoundPrivateForeignInterfaceBindingKind
                            .ImportedExpectation) {
                    NativeOutputRole = nativeOutputRole,
                    NativeOutputContracts = outputContracts,
                    GeneralDependencyContracts =
                        PrivateForeignInterfaceGeneralDependencies(member),
                    ForeignInterfaceMember = surface,
                });
                memberReferences.Add(new PrivateForeignInterfaceMember(
                    binding, moduleFunctionIndex));
            }

            interfaces.Add(new PrivateForeignInterface(origin.Owner,
                definition.QualifiedName, definition.IsPublic,
                definition.ForeignNativeInterface!.VTableByteOffset,
                memberReferences.MoveToImmutable()));
        }

        return (interfaces.MoveToImmutable(), functions.ToImmutable());
    }

    private static ImmutableArray<BoundDependencyContract>
        PrivateForeignInterfaceGeneralDependencies(
            BoundInterfaceMemberDefinition member)
    {
        if (member.Dependencies.IsDefault
            || member.NativeOutputContracts.IsDefault)
        {
            throw new ModuleContractException(
                "PF111 foreign-interface dependencies are uninitialized.");
        }

        var outputFormals = member.NativeOutputContracts.Select(
                static output => output.FormalOrdinal)
            .ToHashSet();
        return [
            .. member.Dependencies.Where(dependency =>
                dependency.Destination
                        != BoundDependencyDestination.InitializedOutput
                    || dependency.DestinationOrdinal is not int ordinal
                    || !outputFormals.Contains(ordinal)
                    || dependency.Relation is not (
                        BoundDependencyRelation.DependsOn
                        or BoundDependencyRelation.ForwardsBacking)),
        ];
    }

    private static string PrivateForeignInterfaceConventionContract(
        NativeCallingConvention convention)
    {
        var conventionName = convention switch {
            NativeCallingConvention.Unmanaged => "Unmanaged",
            NativeCallingConvention.Cdecl => "Cdecl",
            NativeCallingConvention.Stdcall => "Stdcall",
            _ => throw new ModuleContractException(
                "PF111 foreign-interface convention is unsupported."),
        };
        return "|NativeCallable:" + conventionName;
    }

    private static int GetPrivateForeignInterfaceModuleFunctionIndex(
        BoundInterfaceMemberIdentity identity, ModuleWriter writer)
    {
        var program = writer.SourceProgram
            ?? throw new ModuleContractException(
                "PF111 foreign call has no checked source-program snapshot.");
        if (writer.FormatVersion != ForeignInterfacePrivateVersion
            || !BoundInterfaceContract.TryGetInterfaceMemberDefinition(
                identity, program.Interfaces, static _ => true,
                out var selectedMember,
                genericNominalTypeDefinitions:
                    program.GenericNominalTypeDefinitions,
                sourceGenericTypeDefinitions:
                    program.SourceGenericTypeDefinitions)
            || selectedMember is null)
        {
            throw new ModuleContractException(
                "PF111 foreign call does not identify one checked member.");
        }

        var sourceOwners = program.Interfaces.Select(
                (definition, index) => (definition, index))
            .Where(item => !item.definition.Members.IsDefault
                && (uint)identity.MemberOrdinal
                    < (uint)item.definition.Members.Length
                && ReferenceEquals(
                    item.definition.Members[identity.MemberOrdinal],
                    selectedMember))
            .ToArray();
        if (sourceOwners.Length != 1)
        {
            throw new ModuleContractException(
                "PF111 foreign call member lost its checked owner association.");
        }

        var (sourceDefinition, interfaceOrdinal) = sourceOwners[0];
        var origin = writer.IdentityInputs is PrivateModuleIdentityInputs
            identityInputs
            ? identityInputs.IsFor(program)
                && (uint)interfaceOrdinal
                    < (uint)identityInputs.ForeignInterfaceOrigins.Length
                    ? identityInputs.ForeignInterfaceOrigins[
                        interfaceOrdinal]
                    : null
            : sourceDefinition.PrivateForeignInterfaceOrigin;
        if (origin is null
            || (uint)identity.MemberOrdinal
                >= (uint)origin.Members.Length)
        {
            throw new ModuleContractException(
                "PF111 foreign call has no complete captured owner/member Binding.");
        }

        var directoryRows = writer.ForeignInterfaces.Select(
                (foreignInterface, index) => (foreignInterface, index))
            .Where(item => item.foreignInterface.Binding == origin.Owner
                && StringComparer.Ordinal.Equals(
                    item.foreignInterface.LookupName,
                    sourceDefinition.QualifiedName)
                && (uint)identity.MemberOrdinal
                    < (uint)item.foreignInterface.Members.Length
                && item.foreignInterface.Members[
                    identity.MemberOrdinal].Binding
                    == origin.Members[identity.MemberOrdinal])
            .ToArray();
        if (directoryRows.Length != 1)
        {
            throw new ModuleContractException(
                "PF111 foreign call has no unique persisted owner/member Binding.");
        }

        var (persistedInterface, persistedInterfaceIndex)
            = directoryRows[0];
        var moduleFunctionIndex = persistedInterface.Members[
            identity.MemberOrdinal].ModuleFunctionIndex;
        if ((uint)moduleFunctionIndex
                >= (uint)writer.ModuleFunctions.Length
            || writer.ModuleFunctions[moduleFunctionIndex]
                .ForeignInterfaceMember
                is not PrivateForeignInterfaceMemberSurface surface
            || surface.ForeignInterfaceIndex
                != persistedInterfaceIndex
            || surface.DeclaredMethodOrdinal
                != identity.MemberOrdinal
            || writer.ModuleFunctions[moduleFunctionIndex].Import
                != (origin.Owner.Kind
                    == BoundPrivateForeignInterfaceBindingKind
                        .ImportedExpectation))
        {
            throw new ModuleContractException(
                "PF111 foreign call target does not resolve to its exact ModuleFunction row.");
        }

        return moduleFunctionIndex;
    }

    private static ImmutableArray<BoundInterfaceDefinition>
        CreatePrivateForeignInterfaceReferenceDefinitions(Module module)
    {
        var definitions = ReconstructPrivateForeignInterfaces(module);
        var result
            = ImmutableArray.CreateBuilder<BoundInterfaceDefinition>();
        foreach (var definition in definitions)
        {
            var origin = definition.PrivateForeignInterfaceOrigin
                ?? throw new ModuleFormatException(
                    "Private foreign-interface origin is missing.");
            if (origin.Owner.Kind
                    != BoundPrivateForeignInterfaceBindingKind
                        .OwnedDefinition
                || !origin.Owner.IsExported)
            {
                continue;
            }

            var members = ImmutableArray.CreateBuilder<
                BoundInterfaceMemberDefinition>(definition.Members.Length);
            foreach (var (member, memberOrdinal) in definition.Members
                         .Select((member, ordinal) => (member, ordinal)))
            {
                var binding = origin.Members[memberOrdinal];
                if (binding.Kind
                        != BoundPrivateForeignInterfaceBindingKind
                            .OwnedDefinition
                    || !binding.IsExported)
                {
                    throw new ModuleFormatException(
                        "Exported foreign-interface owner has a nonexported member Binding.");
                }
                members.Add(member with {
                    DeclarationKey = binding.LocalDeclarationKey,
                    AdditionalDeclarationKeys = [],
                });
            }

            result.Add(definition with {
                Members = members.MoveToImmutable(),
                ModuleImport = new BoundModuleImport(
                    origin.Owner.UsingModuleIdentity,
                    origin.Owner.LocalDeclarationKey, 1,
                    (uint)module.RuntimePolicy),
                SourceInputOrdinal = -1,
            });
        }
        return result.ToImmutable();
    }

    private static void ValidatePrivateForeignInterfacesAgainstReference(
        Module consumer, CompilationReference provider,
        ImmutableArray<PrimitiveType> providerFunctionPointerMapping,
        ImmutableArray<PrimitiveType> providerNativePointerMapping,
        int[] providerCheckedReferenceMapping,
        ImmutableArray<int> providerEnumMapping)
    {
        foreach (var expected in ReconstructPrivateForeignInterfaces(consumer))
        {
            var expectedOrigin = expected.PrivateForeignInterfaceOrigin
                ?? throw new ModuleFormatException(
                    "Private foreign-interface origin is missing.");
            var expectedOwner = expectedOrigin.Owner;
            if (expectedOwner.Kind
                != BoundPrivateForeignInterfaceBindingKind.ImportedExpectation)
            {
                continue;
            }

            var actualDefinitions = provider.Interfaces.Where(actual =>
                    actual.PrivateForeignInterfaceOrigin?.Owner is
                        BoundPrivateForeignInterfaceBinding actualOwner
                    && actualOwner.Kind
                        == BoundPrivateForeignInterfaceBindingKind
                            .OwnedDefinition
                    && actualOwner.IsExported
                    && actualOwner.UsingModuleIdentity
                        == expectedOwner.SelectedModuleIdentity
                    && actualOwner.LocalDeclarationKey
                        == expectedOwner.SelectedExportKey)
                .ToArray();
            if (actualDefinitions.Length != 1)
            {
                throw new ModuleContractException(
                    "Selected foreign-interface provider does not publish the exact owned owner export.");
            }

            var actual = actualDefinitions[0];
            var actualOrigin = actual.PrivateForeignInterfaceOrigin!;
            if (actual.ForeignNativeInterface
                    is not BoundForeignNativeInterfaceLayout actualLayout
                || expected.ForeignNativeInterface
                    is not BoundForeignNativeInterfaceLayout expectedLayout
                || actualLayout.VTableByteOffset
                    != expectedLayout.VTableByteOffset
                || actual.ModuleImport is not BoundModuleImport actualImport
                || actualImport.ProducerIdentity
                    != expectedOwner.SelectedModuleIdentity
                || actualImport.ExportKey
                    != expectedOwner.SelectedExportKey
                || expected.ModuleImport
                    is not BoundModuleImport expectedImport
                || actualImport.TargetRequirement
                    != expectedImport.TargetRequirement
                || actualImport.RuntimePolicy
                    != expectedImport.RuntimePolicy
                || actual.Members.Length != expected.Members.Length
                || actualOrigin.Members.Length != expectedOrigin.Members.Length)
            {
                throw new ModuleContractException(
                    "Selected foreign-interface provider changed the owner layout or complete member count.");
            }

            for (var memberOrdinal = 0;
                memberOrdinal < expected.Members.Length;
                memberOrdinal++)
            {
                var expectedBinding = expectedOrigin.Members[
                    memberOrdinal];
                var actualBinding = actualOrigin.Members[memberOrdinal];
                if (expectedBinding.Kind
                        != BoundPrivateForeignInterfaceBindingKind
                            .ImportedExpectation
                    || expectedBinding.SelectedModuleIdentity
                        != expectedOwner.SelectedModuleIdentity
                    || expectedBinding.SelectedExportKey
                        != actualBinding.LocalDeclarationKey
                    || actualBinding.Kind
                        != BoundPrivateForeignInterfaceBindingKind
                            .OwnedDefinition
                    || !actualBinding.IsExported
                    || actualBinding.UsingModuleIdentity
                        != expectedOwner.SelectedModuleIdentity)
                {
                    throw new ModuleContractException(
                        "Selected foreign-interface provider does not publish the exact ordered member export.");
                }

                var expectedMember = expected.Members[memberOrdinal];
                var actualMemberSource = actual.Members[memberOrdinal];
                var actualMember = BoundTreeTraversal.RewriteTypes(
                    actualMemberSource, type =>
                    {
                        type = RemapType(type, provider.Structures,
                            consumer.Structures);
                        type = RemapNativeFunctionPointerType(type,
                            providerFunctionPointerMapping);
                        type = RemapNativePointerType(type,
                            providerNativePointerMapping);
                        type = RemapCheckedReferenceType(type,
                            providerCheckedReferenceMapping);
                        return RemapEnumType(type, providerEnumMapping);
                    });
                if (!ArePrivateForeignInterfaceMembersCompatible(
                        expectedMember, actualMember,
                        consumer.Structures,
                        consumer.NativeFunctionPointerSignatures,
                        consumer.NativePointerTypes)
                    || !ArePrivateForeignInterfaceMemberTypeOriginsCompatible(
                        expectedMember, actualMemberSource, consumer,
                        provider, providerFunctionPointerMapping,
                        providerNativePointerMapping,
                        providerEnumMapping))
                {
                    throw new ModuleContractException(
                        "Selected foreign-interface provider changed an expected member ABI, effect, output or dependency contract.");
                }
            }
        }
    }

    private static Module ResolvePrivateForeignInterfaceCallReferences(
        Module module)
    {
        if (module.FormatVersion != ForeignInterfacePrivateVersion)
        {
            return module;
        }

        var interfaces = ReconstructPrivateForeignInterfaces(module);
        var functions = ImmutableArray.CreateBuilder<ModuleFunction>(
            module.Functions.Length);
        foreach (var item in module.Functions)
        {
            if (item.Function is not BoundFunction function)
            {
                functions.Add(item);
                continue;
            }

            var rewritten = BoundTreeTraversal.RewriteExpressions(function,
                expression =>
                    expression is PendingPrivateForeignInterfaceCall pending
                        ? ResolveCallTarget(pending)
                        : expression);
            functions.Add(item with { Function = rewritten });
        }
        return module with { Functions = functions.MoveToImmutable() };

        BoundCall ResolveCallTarget(
            PendingPrivateForeignInterfaceCall pending)
        {
            if ((uint)pending.ModuleFunctionIndex
                    >= (uint)module.Functions.Length
                || module.Functions[pending.ModuleFunctionIndex]
                    .ForeignInterfaceMember
                    is not PrivateForeignInterfaceMemberSurface surface
                || (uint)surface.ForeignInterfaceIndex
                    >= (uint)interfaces.Length)
            {
                throw new ModuleFormatException(
                    "PF111 foreign call target does not resolve to a complete member surface.");
            }

            var definition = interfaces[surface.ForeignInterfaceIndex];
            var memberOrdinal = surface.DeclaredMethodOrdinal;
            if (definition.ForeignNativeInterface is not
                    BoundForeignNativeInterfaceLayout layout
                || (uint)memberOrdinal >= (uint)definition.Members.Length
                || definition.Members[memberOrdinal] is not
                    BoundInterfaceMemberDefinition member
                || member.Ordinal != memberOrdinal
                || pending.Type != surface.ResultType
                || pending.Arguments.Length != member.Parameters.Length
                || (!pending.ArgumentParameterIndices.IsDefault
                    && pending.ArgumentParameterIndices.Length
                        != pending.Arguments.Length))
            {
                throw new ModuleFormatException(
                    "PF111 foreign call target does not match its member signature.");
            }

            if (!pending.ArgumentParameterIndices.IsDefault)
            {
                var mappedFormals = new bool[member.Parameters.Length];
                foreach (var parameter in
                    pending.ArgumentParameterIndices)
                {
                    if ((uint)parameter >= (uint)mappedFormals.Length
                        || mappedFormals[parameter])
                    {
                        throw new ModuleFormatException(
                            "PF111 foreign call formal mapping is not a permutation.");
                    }
                    mappedFormals[parameter] = true;
                }
                if (mappedFormals.Any(static mapped => !mapped))
                {
                    throw new ModuleFormatException(
                        "PF111 foreign call formal mapping is incomplete.");
                }
            }

            var origin = definition.PrivateForeignInterfaceOrigin
                ?? throw new ModuleFormatException(
                    "PF111 foreign call owner has no private Binding.");
            var declaringInterface = new BoundInterfaceType(
                BoundInterfaceTypeKind.Nominal, PrimitiveType.Void,
                definition.QualifiedName, -1, [], false, false, false,
                false)
            {
                ModuleImport = definition.ModuleImport,
                PrivateForeignInterfaceBinding = origin.Owner,
            };
            var memberIdentity = new BoundInterfaceMemberIdentity(
                declaringInterface, memberOrdinal, member.DeclarationKey);
            var convention = member.NativeCallableConvention
                ?? throw new ModuleFormatException(
                    "PF111 foreign call member has no calling convention.");
            return new BoundCall(BoundCall.ForeignInterfaceFunction,
                pending.Arguments, pending.Type, pending.Span)
            {
                ForeignInterfaceCall = new BoundForeignInterfaceCall(
                    pending.Receiver, memberIdentity,
                    layout.VTableByteOffset, convention,
                    member.RequiresMutableReceiver, member.DoesNotReturn,
                    member.NativeOutputContracts),
                ArgumentParameterIndices =
                    pending.ArgumentParameterIndices,
            };
        }
    }

    private static bool ArePrivateForeignInterfaceMembersCompatible(
        BoundInterfaceMemberDefinition expected,
        BoundInterfaceMemberDefinition actual,
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<NativeFunctionPointerSignatureDefinition>
            functionPointerSignatures,
        ImmutableArray<NativePointerTypeDefinition> pointerTypes)
    {
        if (expected.Ordinal != actual.Ordinal
            || expected.Kind != BoundInterfaceMemberKind.Callable
            || actual.Kind != BoundInterfaceMemberKind.Callable
            || expected.Accessibility
                != BoundInterfaceAccessibility.Public
            || actual.Accessibility
                != BoundInterfaceAccessibility.Public
            || expected.IsStatic != actual.IsStatic
            || expected.IsExternal != actual.IsExternal
            || expected.IsPartial != actual.IsPartial
            || expected.DeclaredRequiresUnsafeCaller
                != actual.DeclaredRequiresUnsafeCaller
            || expected.RequiresUnsafeCaller
                != actual.RequiresUnsafeCaller
            || expected.RequiresMutableReceiver
                != actual.RequiresMutableReceiver
            || expected.DoesNotReturn != actual.DoesNotReturn
            || expected.IsAsync != actual.IsAsync
            || expected.HasBody != actual.HasBody
            || expected.NativeCallableConvention
                != actual.NativeCallableConvention
            || expected.ResultType is not BoundInterfaceType expectedResult
            || actual.ResultType is not BoundInterfaceType actualResult
            || !BoundInterfaceContract.AreEquivalentProviderInterfaceViews(
                expectedResult, actualResult)
            || expected.Parameters.IsDefault
            || actual.Parameters.IsDefault
            || expected.Parameters.Length != actual.Parameters.Length
            || expected.NativeOutputContracts.IsDefault
            || actual.NativeOutputContracts.IsDefault
            || expected.Dependencies.IsDefault
            || actual.Dependencies.IsDefault)
        {
            return false;
        }

        for (var parameterOrdinal = 0;
            parameterOrdinal < expected.Parameters.Length;
            parameterOrdinal++)
        {
            var expectedParameter = expected.Parameters[parameterOrdinal];
            var actualParameter = actual.Parameters[parameterOrdinal];
            if (expectedParameter.Mode != actualParameter.Mode
                || !BoundInterfaceContract
                    .AreEquivalentProviderInterfaceViews(
                        expectedParameter.Type, actualParameter.Type))
            {
                return false;
            }
        }

        if (!SameNativeOutputContracts(
                expected.NativeOutputContracts,
                actual.NativeOutputContracts,
                structures, structures, functionPointerSignatures,
                pointerTypes, functionPointerSignatures, pointerTypes))
        {
            return false;
        }

        if (!ForeignNativeInterfaceContract.TryGetReceiverType(expected,
                pointerTypes, out var expectedReceiverType)
            || !ForeignNativeInterfaceContract.TryGetReceiverType(actual,
                pointerTypes, out var actualReceiverType))
        {
            return false;
        }

        var expectedSurface = CreatePrivateForeignInterfaceMemberSurface(
            expected, expectedReceiverType);
        var actualSurface = CreatePrivateForeignInterfaceMemberSurface(
            actual, actualReceiverType);
        var contract = PrivateForeignInterfaceConventionContract(
            expected.NativeCallableConvention
                ?? throw new ModuleContractException(
                    "Foreign-interface convention is missing."));
        return PrivateForeignInterfaceDependencyContractsSatisfy(
            PrivateForeignInterfaceGeneralDependencies(expected),
            expectedSurface, pointerTypes,
            expected.NativeOutputContracts,
            PrivateForeignInterfaceGeneralDependencies(actual),
            actualSurface, pointerTypes, actual.NativeOutputContracts,
            contract);
    }

    private static bool ArePrivateForeignInterfaceMemberTypeOriginsCompatible(
        BoundInterfaceMemberDefinition expected,
        BoundInterfaceMemberDefinition actual, Module consumer,
        CompilationReference provider,
        ImmutableArray<PrimitiveType> providerFunctionPointerMapping,
        ImmutableArray<PrimitiveType> providerNativePointerMapping,
        ImmutableArray<int> providerEnumMapping)
    {
        if (expected.ResultType is not BoundInterfaceType expectedResult
            || actual.ResultType is not BoundInterfaceType actualResult
            || expected.Parameters.IsDefault
            || actual.Parameters.IsDefault
            || expected.Parameters.Length != actual.Parameters.Length
            || expected.NativeOutputContracts.IsDefault
            || actual.NativeOutputContracts.IsDefault
            || expected.NativeOutputContracts.Length
                != actual.NativeOutputContracts.Length)
        {
            return false;
        }

        var visited = new HashSet<(PrimitiveType Expected,
            PrimitiveType Actual)>();
        if (!AreTypesEquivalent(expectedResult.PrimitiveType,
                actualResult.PrimitiveType))
        {
            return false;
        }
        for (var index = 0; index < expected.Parameters.Length; index++)
        {
            if (!AreTypesEquivalent(
                    expected.Parameters[index].Type.PrimitiveType,
                    actual.Parameters[index].Type.PrimitiveType))
            {
                return false;
            }
        }
        for (var index = 0;
            index < expected.NativeOutputContracts.Length; index++)
        {
            if (!AreTypesEquivalent(
                    expected.NativeOutputContracts[index].PointeeType,
                    actual.NativeOutputContracts[index].PointeeType))
            {
                return false;
            }
        }
        return true;

        bool AreTypesEquivalent(PrimitiveType expectedType,
            PrimitiveType actualType)
        {
            if (!visited.Add((expectedType, actualType)))
            {
                return true;
            }
            if (NativeFixedArrayTypes.IsType(expectedType)
                || NativeFixedArrayTypes.IsType(actualType)
                || CheckedReferenceTypes.IsType(expectedType)
                || CheckedReferenceTypes.IsType(actualType)
                || ManagedClassTypes.IsManaged(expectedType)
                || ManagedClassTypes.IsManaged(actualType))
            {
                return false;
            }

            if (NativePointerTypes.IsType(expectedType)
                || NativePointerTypes.IsType(actualType))
            {
                if (!NativePointerTypes.IsType(expectedType)
                    || !NativePointerTypes.IsType(actualType))
                {
                    return false;
                }
                var expectedIndex
                    = NativePointerTypes.Index(expectedType);
                var actualIndex = NativePointerTypes.Index(actualType);
                if ((uint)expectedIndex
                        >= (uint)consumer.NativePointerTypes.Length
                    || (uint)actualIndex
                        >= (uint)provider.NativePointerTypes.Length
                    || (uint)actualIndex
                        >= (uint)providerNativePointerMapping.Length
                    || providerNativePointerMapping[actualIndex]
                        != expectedType)
                {
                    return false;
                }
                var expectedPointer = consumer.NativePointerTypes[
                    expectedIndex];
                var actualPointer = provider.NativePointerTypes[
                    actualIndex];
                return expectedPointer.IsWritable
                        == actualPointer.IsWritable
                    && AreTypesEquivalent(expectedPointer.PointeeType,
                        actualPointer.PointeeType);
            }

            if (NativeFunctionPointerTypes.IsType(expectedType)
                || NativeFunctionPointerTypes.IsType(actualType))
            {
                if (!NativeFunctionPointerTypes.IsType(expectedType)
                    || !NativeFunctionPointerTypes.IsType(actualType))
                {
                    return false;
                }
                var expectedIndex
                    = NativeFunctionPointerTypes.Index(expectedType);
                var actualIndex
                    = NativeFunctionPointerTypes.Index(actualType);
                if ((uint)expectedIndex
                        >= (uint)consumer.NativeFunctionPointerSignatures
                            .Length
                    || (uint)actualIndex
                        >= (uint)provider.NativeFunctionPointerSignatures
                            .Length
                    || (uint)actualIndex
                        >= (uint)providerFunctionPointerMapping.Length
                    || providerFunctionPointerMapping[actualIndex]
                        != expectedType)
                {
                    return false;
                }

                var expectedSignature
                    = consumer.NativeFunctionPointerSignatures[
                        expectedIndex];
                var actualSignature
                    = provider.NativeFunctionPointerSignatures[
                        actualIndex];
                if (expectedSignature.CallingConvention
                        != actualSignature.CallingConvention
                    || expectedSignature.EffectsMask
                        != actualSignature.EffectsMask
                    || !expectedSignature.Selectors.SequenceEqual(
                        actualSignature.Selectors)
                    || !expectedSignature.OutputStates.SequenceEqual(
                        actualSignature.OutputStates)
                    || !expectedSignature.Dependencies.SequenceEqual(
                        actualSignature.Dependencies)
                    || expectedSignature.ParameterTypes.Length
                        != actualSignature.ParameterTypes.Length
                    || expectedSignature.NativeParameters.Length
                        != actualSignature.NativeParameters.Length
                    || !AreTypesEquivalent(
                        expectedSignature.ReturnType,
                        actualSignature.ReturnType))
                {
                    return false;
                }
                for (var parameterIndex = 0;
                    parameterIndex
                        < expectedSignature.ParameterTypes.Length;
                    parameterIndex++)
                {
                    if (!AreTypesEquivalent(
                            expectedSignature.ParameterTypes[
                                parameterIndex],
                            actualSignature.ParameterTypes[
                                parameterIndex]))
                    {
                        return false;
                    }
                }
                for (var parameterIndex = 0;
                    parameterIndex
                        < expectedSignature.NativeParameters.Length;
                    parameterIndex++)
                {
                    var expectedParameter
                        = expectedSignature.NativeParameters[
                            parameterIndex];
                    var actualParameter
                        = actualSignature.NativeParameters[
                            parameterIndex];
                    if (expectedParameter.ParameterIndex
                            != actualParameter.ParameterIndex
                        || expectedParameter.Access
                            != actualParameter.Access
                        || expectedParameter.Retention
                            != actualParameter.Retention
                        || expectedParameter.Extent
                            != actualParameter.Extent
                        || !AreTypesEquivalent(
                            expectedParameter.PointeeType,
                            actualParameter.PointeeType))
                    {
                        return false;
                    }
                }
                return true;
            }

            if (BoundEnumTypes.IsType(expectedType)
                || BoundEnumTypes.IsType(actualType))
            {
                if (!BoundEnumTypes.IsType(expectedType)
                    || !BoundEnumTypes.IsType(actualType))
                {
                    return false;
                }
                var expectedIndex = BoundEnumTypes.Index(expectedType);
                var actualIndex = BoundEnumTypes.Index(actualType);
                return (uint)expectedIndex
                        < (uint)consumer.EnumDefinitions.Length
                    && (uint)actualIndex
                        < (uint)provider.EnumDefinitions.Length
                    && (uint)actualIndex
                        < (uint)providerEnumMapping.Length
                    && providerEnumMapping[actualIndex] == expectedIndex
                    && SamePrivateEnumContractShape(
                        PrivateEnumExpectation(consumer.EnumDefinitions[
                            expectedIndex]),
                        provider.EnumDefinitions[actualIndex])
                    && HasMatchingNominalOrigins(expectedType,
                        actualType);
            }

            var expectedStructureIndex = (int)expectedType - 100;
            var actualStructureIndex = (int)actualType - 100;
            var isExpectedStructure
                = expectedStructureIndex >= 0
                    && (uint)expectedStructureIndex
                        < (uint)consumer.Structures.Length;
            var isActualStructure = actualStructureIndex >= 0
                && (uint)actualStructureIndex
                    < (uint)provider.Structures.Length;
            if (isExpectedStructure || isActualStructure)
            {
                if (!isExpectedStructure || !isActualStructure)
                {
                    return false;
                }

                var expectedStructure = consumer.Structures[
                    expectedStructureIndex];
                var actualStructure = provider.Structures[
                    actualStructureIndex];
                if (!StringComparer.Ordinal.Equals(
                        expectedStructure.Name, actualStructure.Name)
                    || !SameStructure(expectedStructure,
                        actualStructure, consumer.Structures,
                        provider.Structures,
                        consumer.NativeFunctionPointerSignatures,
                        provider.NativeFunctionPointerSignatures,
                        consumer.NativePointerTypes,
                        provider.NativePointerTypes,
                        consumer.NativeFixedArrayTypes,
                        provider.NativeFixedArrayTypes,
                        consumer.Classes, provider.Classes)
                    || !HasMatchingNominalOrigins(expectedType,
                        actualType)
                    || expectedStructure.Fields.Length
                        != actualStructure.Fields.Length)
                {
                    return false;
                }

                for (var fieldIndex = 0;
                    fieldIndex < expectedStructure.Fields.Length;
                    fieldIndex++)
                {
                    if (!AreTypesEquivalent(
                            expectedStructure.Fields[fieldIndex].Type,
                            actualStructure.Fields[fieldIndex].Type))
                    {
                        return false;
                    }
                }
                return true;
            }

            return expectedType == actualType;
        }

        bool HasMatchingNominalOrigins(PrimitiveType expectedType,
            PrimitiveType actualType)
        {
            var expectedOrigins = consumer.NominalTypeOrigins.Where(
                    origin => origin.PrivateType == expectedType)
                .ToArray();
            var actualOrigins = provider.PrivateNominalTypeOrigins.Where(
                    origin => origin.PrivateType == actualType)
                .ToArray();
            if (expectedOrigins.Length == 0 || actualOrigins.Length == 0)
            {
                return false;
            }

            foreach (var expectedOrigin in expectedOrigins)
            {
                var hasMatchingActual = expectedOrigin.OriginKind switch {
                    BoundPrivateNominalOriginKind.OwnedDefinition =>
                        actualOrigins.Any(actualOrigin =>
                            actualOrigin.OriginKind
                                == BoundPrivateNominalOriginKind
                                    .OwnedDefinition
                            && actualOrigin.UsingModuleIdentity
                                == expectedOrigin.UsingModuleIdentity
                            && actualOrigin.LocalDeclarationKey
                                == expectedOrigin.LocalDeclarationKey),
                    BoundPrivateNominalOriginKind.ImportedExpectation =>
                        actualOrigins.Any(actualOrigin =>
                            actualOrigin.OriginKind
                                == BoundPrivateNominalOriginKind
                                    .OwnedDefinition
                            && actualOrigin.IsExported
                            && actualOrigin.UsingModuleIdentity
                                == expectedOrigin.SelectedModuleIdentity
                            && actualOrigin.LocalDeclarationKey
                                == expectedOrigin.SelectedExportKey),
                    _ => false,
                };
                if (!hasMatchingActual)
                {
                    return false;
                }
            }
            return true;
        }
    }

    private static PrivateForeignInterfaceMemberSurface
        CreatePrivateForeignInterfaceMemberSurface(
            BoundInterfaceMemberDefinition member,
            PrimitiveType receiverType)
    {
        if (member.ResultType is not BoundInterfaceType {
                Kind: BoundInterfaceTypeKind.Primitive,
            } resultType
            || member.Parameters.IsDefault
            || member.Parameters.Any(static parameter =>
                parameter.Type.Kind != BoundInterfaceTypeKind.Primitive))
        {
            throw new ModuleContractException(
                "Foreign-interface member surface has an unsupported type.");
        }

        return new PrivateForeignInterfaceMemberSurface(0,
            member.Ordinal, resultType.PrimitiveType, receiverType,
            member.DoesNotReturn, [
                .. member.Parameters.Select(static parameter =>
                    new PrivateForeignInterfaceFormal(
                        parameter.Name,
                        parameter.Type.PrimitiveType,
                        parameter.DefaultValue is not null,
                        parameter.DefaultValue)),
            ]);
    }

    private static ImmutableArray<PrimitiveType>
        PrivateForeignInterfaceTypeRoots(
            ImmutableArray<BoundInterfaceDefinition> interfaces,
            ImmutableArray<NativePointerTypeDefinition> pointerTypes)
    {
        var roots = ImmutableArray.CreateBuilder<PrimitiveType>();
        foreach (var definition in interfaces)
        {
            foreach (var member in definition.Members)
            {
                if (member.ResultType is not BoundInterfaceType resultType
                    || resultType.Kind
                        != BoundInterfaceTypeKind.Primitive
                    || !ForeignNativeInterfaceContract.TryGetReceiverType(
                        member, pointerTypes, out var receiverType)
                    || member.Parameters.IsDefault
                    || member.Parameters.Any(static parameter =>
                        parameter.Type.Kind
                            != BoundInterfaceTypeKind.Primitive))
                {
                    throw new ModuleContractException(
                        "Private foreign-interface signature has an unsupported type root.");
                }

                roots.Add(resultType.PrimitiveType);
                roots.Add(receiverType);
                roots.AddRange(member.Parameters.Select(
                    static parameter => parameter.Type.PrimitiveType));
                roots.AddRange(member.NativeOutputContracts.Select(
                    static output => output.PointeeType));
            }
        }
        return roots.ToImmutable();
    }

    private static ImmutableArray<PrimitiveType>
        PrivateForeignInterfaceModuleTypeRoots(Module module)
    {
        var roots = ImmutableArray.CreateBuilder<PrimitiveType>();
        foreach (var definition in module.ForeignInterfaces)
        {
            foreach (var member in definition.Members)
            {
                if ((uint)member.ModuleFunctionIndex
                        >= (uint)module.Functions.Length
                    || module.Functions[member.ModuleFunctionIndex]
                        .ForeignInterfaceMember
                        is not PrivateForeignInterfaceMemberSurface surface)
                {
                    throw new ModuleContractException(
                        "PF111 foreign-interface nominal closure has no member surface.");
                }

                roots.Add(surface.ResultType);
                roots.Add(surface.ReceiverType);
                roots.AddRange(surface.Formals.Select(
                    static formal => formal.Type));
                roots.AddRange(module.Functions[
                        member.ModuleFunctionIndex].NativeOutputContracts
                    .Select(static output => output.PointeeType));
            }
        }
        return roots.ToImmutable();
    }

    private static BoundInterfaceDefinition
        RemapPrivateForeignInterfaceDefinition(
            BoundInterfaceDefinition definition,
            ImmutableArray<BoundStruct> sourceStructures,
            ImmutableArray<BoundStruct> destinationStructures,
            NativeContractProjection projection,
            int[] classMapping)
        => RemapPrivateForeignInterfaceDefinition(definition,
            sourceStructures, destinationStructures,
            projection.FixedArrayMapping,
            projection.FunctionPointerMapping,
            projection.PointerMapping,
            projection.CheckedReferenceMapping.ToArray(),
            classMapping, projection.EnumMapping);

    private static BoundInterfaceDefinition
        RemapPrivateForeignInterfaceDefinition(
            BoundInterfaceDefinition definition,
            ImmutableArray<BoundStruct> sourceStructures,
            ImmutableArray<BoundStruct> destinationStructures,
            ImmutableArray<PrimitiveType> fixedArrayMapping,
            ImmutableArray<PrimitiveType> functionPointerMapping,
            ImmutableArray<PrimitiveType> pointerMapping,
            int[] checkedReferenceMapping,
            int[] classMapping, ImmutableArray<int> enumMapping)
        => BoundTreeTraversal.RewriteTypes(definition, type =>
        {
            type = RemapType(type, sourceStructures,
                destinationStructures);
            type = RemapNativeFixedArrayType(type, fixedArrayMapping);
            type = RemapNativeFunctionPointerType(type,
                functionPointerMapping);
            type = RemapNativePointerType(type, pointerMapping);
            type = RemapManagedClassType(type, classMapping);
            type = RemapCheckedReferenceType(type,
                checkedReferenceMapping);
            return RemapEnumType(type, enumMapping);
        });

    private static bool TryAddPrivateForeignInterfaceKey(
        Dictionary<string, HashSet<string>> keysByModule,
        string usingModuleIdentity, string localDeclarationKey)
    {
        if (!keysByModule.TryGetValue(usingModuleIdentity,
                out var keys))
        {
            keys = new HashSet<string>(StringComparer.Ordinal);
            keysByModule.Add(usingModuleIdentity, keys);
        }
        return keys.Add(localDeclarationKey);
    }

}
