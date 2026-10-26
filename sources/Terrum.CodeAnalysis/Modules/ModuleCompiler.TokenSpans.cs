using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class ModuleCompiler
{
    private const int PrivateSpanVersion = 77;

    private sealed class ModuleReader(Stream stream) : BinaryReader(stream, StrictUtf8, leaveOpen: true)
    {
        internal int FormatVersion { get; set; }
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
