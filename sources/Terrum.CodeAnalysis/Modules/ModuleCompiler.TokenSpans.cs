using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class ModuleCompiler
{
    private sealed class ModuleWriter(Stream stream)
        : BinaryWriter(stream, StrictUtf8, leaveOpen: true)
    {
        internal int FormatVersion { get; init; }
        internal ImmutableArray<BoundStruct> Structures { get; init; } = [];
        internal ImmutableArray<BoundClass> Classes { get; init; } = [];
        internal ImmutableArray<NativeFunctionPointerSignatureDefinition>
            NativeFunctionPointerSignatures
        { get; init; } = [];
        internal ImmutableArray<NativePointerTypeDefinition> NativePointerTypes { get; init; } = [];
        internal ImmutableArray<BoundNativeFixedArrayType> NativeFixedArrayTypes { get; init; } = [];
        internal ImmutableArray<CheckedReferenceTypeDefinition>
            CheckedReferenceTypes { get; init; } = [];
        internal ImmutableArray<BoundPrivateNominalTypeOrigin>
            NominalTypeOrigins { get; init; } = [];
        internal ImmutableArray<BoundEnumDefinition> EnumDefinitions { get; init; } = [];
        internal ImmutableArray<HostBuffer> InvocationBuffers { get; init; } = [];
        internal ImmutableArray<BoundFunction> SourceFunctions { get; init; } = [];
        internal BoundProgram? SourceProgram { get; init; }
        internal ImmutableArray<ModuleFunction> ModuleFunctions { get; init; } = [];
        internal BoundFunction? CurrentSourceFunction { get; set; }
        internal ImmutableArray<BoundVariable> CurrentVariables { get; set; } = [];
        internal int CurrentParameterCount { get; set; }
        internal bool CurrentIsConstructor { get; set; }
        internal PrimitiveType CurrentReturnType { get; set; }
        internal HashSet<int> AddressOnlyNestedBackingVariables { get; set; } = [];
        internal int CurrentNestedBackingVariable { get; set; } = -1;
    }

    private sealed class ModuleReader(Stream stream) : BinaryReader(stream, StrictUtf8, leaveOpen: true)
    {
        internal int FormatVersion { get; set; }
        internal ImmutableArray<BoundStruct> Structures { get; set; } = [];
        internal ImmutableArray<BoundClass> Classes { get; set; } = [];
        internal ImmutableArray<NativeFunctionPointerSignatureDefinition>
            NativeFunctionPointerSignatures
        { get; set; } = [];
        internal ImmutableArray<BoundNativeFixedArrayType> NativeFixedArrayTypes { get; set; } = [];
        internal ImmutableArray<CheckedReferenceTypeDefinition>
            CheckedReferenceTypes { get; set; } = [];
        internal ImmutableArray<BoundEnumDefinition>
            EnumDefinitions { get; set; } = [];
        internal bool HasReadEnumDefinitions { get; set; }
        internal ImmutableArray<BoundPrivateNominalTypeOrigin>
            NominalTypeOrigins { get; set; } = [];
        internal ImmutableArray<HostBuffer> InvocationBuffers { get; set; } = [];
        internal bool HasReadCheckedReferenceTypes { get; set; }
        internal ImmutableArray<NativePointerTypeDefinition> NativePointerTypes { get; set; } = [];
        internal ImmutableArray<BoundVariable> CurrentVariables { get; set; } = [];
        internal int CurrentParameterCount { get; set; }
        internal bool CurrentIsConstructor { get; set; }
        internal PrimitiveType CurrentReturnType { get; set; }
        internal int CurrentNestedBackingVariable { get; set; } = -1;
    }

    private static void RequirePrivateSpanRevision(BinaryReader reader)
    {
        _ = reader;
        throw new ModuleFormatException("Compilation-local span expressions are not supported in semantic modules.");
    }

    private static void RequireNativePointerRevision(BinaryReader reader)
    {
        if (reader is not ModuleReader { FormatVersion: >= NativePointerVersion })
        {
            throw new ModuleFormatException("Typed native pointers require semantic-module revision 83.");
        }
    }

    private static void ValidatePrivateSpanModule(Module module)
    {
        var functions = ImmutableArray.CreateBuilder<BoundFunction>();
        foreach (var item in module.Functions)
        {
            if (item.Function is BoundFunction function)
            {
                functions.Add(function);
            }
        }
        var program = new BoundProgram(functions.ToImmutable(), -1) {
            Structures = module.Structures,
            NativeFunctionPointerSignatures = module.NativeFunctionPointerSignatures,
            NativePointerTypes = module.NativePointerTypes,
            NativeFixedArrayTypes = module.NativeFixedArrayTypes,
            RequiresSourceGrant = module.RequiresSourceGrant,
            InvocationProfileId = module.InvocationProfileId,
        };
        if ((!module.Structures.IsDefaultOrEmpty
                && module.Structures.Any(static structure =>
                    structure.TokenSpanRole != TokenSpanTypeRole.None
                    || structure.Fields.Any(static field => field.Type == PrimitiveType.TokenSpan)))
            || module.Functions.Any(static item => ContractContainsType(item.Contract, PrimitiveType.TokenSpan))
            || module.Functions.Any(static item => item.Function is BoundFunction function
                && (function.ReturnType == PrimitiveType.TokenSpan
                    || function.Variables.Any(static variable => variable.Type == PrimitiveType.TokenSpan)))
            || TokenSpanContract.HasMetadata(program))
        {
            throw new ModuleFormatException(
                "Compilation-local spans are not supported in private semantic modules.");
        }
    }
}
