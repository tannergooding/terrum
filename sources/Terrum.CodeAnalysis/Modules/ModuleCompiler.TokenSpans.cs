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
        if (reader is not ModuleReader { FormatVersion: PrivateSpanVersion or DirectNativeImportVersion })
        {
            throw new ModuleFormatException("Private span IR requires semantic-module revision 77 or 79.");
        }
    }

    private static void ValidatePrivateSpanDeclarations(BoundProgram program)
    {
        if (!program.RequiresSourceGrant
            || program.Structures.Any(static structure =>
                structure.Fields.Any(static field => field.Type == PrimitiveType.TokenSpan))
            || program.Functions.Any(static function => !function.CapturedParameters.IsDefaultOrEmpty
                || function.ReturnsForwardedBacking || !function.ResultDependencyParameters.IsDefaultOrEmpty
                || function.ReturnType == PrimitiveType.TokenSpan
                || function.IsExternal && function.Variables.Any(static variable =>
                    variable.Type == PrimitiveType.TokenSpan)))
        {
            throw new ModuleContractException("Compilation-local span and capture metadata cannot be persisted.");
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
            RequiresSourceGrant = module.RequiresSourceGrant,
        };
        var hasSpans = TokenSpanContract.HasMetadata(program);
        if (module.FormatVersion is Version or FixedLayoutVersion)
        {
            if (hasSpans || module.Functions.Any(static item =>
                ContractContainsType(item.Contract, PrimitiveType.TokenSpan)))
            {
                throw new ModuleFormatException("Revision 76 cannot retain span IR or contracts.");
            }
            return;
        }
        if (module.FormatVersion is not (PrivateSpanVersion or DirectNativeImportVersion)
            || module.FormatVersion == PrivateSpanVersion && !module.RequiresSourceGrant
            || hasSpans && !module.RequiresSourceGrant
            || module.Functions.Any(static item => item.Import
                && ContractContainsType(item.Contract, PrimitiveType.TokenSpan)))
        {
            throw new ModuleFormatException("Unsupported private-span module boundary.");
        }
        if (!hasSpans)
        {
            return;
        }
        try
        {
            ValidatePrivateSpanDeclarations(program);
        }
        catch (ModuleContractException exception)
        {
            throw new ModuleFormatException("Unsupported private-span declarations.", exception);
        }
        var hasSnapshotProjection = false;
        foreach (var function in functions)
        {
            _ = Calls(function.Body, visitExpression: expression => {
                hasSnapshotProjection |= expression is BoundSnapshotSpan;
            });
        }
        if (hasSpans && (!TokenSpanContract.ValidTokenLayout(module.Structures)
            || module.Structures.Count(static structure => structure.TokenSpanRole == TokenSpanTypeRole.Token) != 1
            || module.Structures.Count(static structure => structure.TokenSpanRole == TokenSpanTypeRole.Range) != 1
            || hasSnapshotProjection
                && module.Structures.Count(static structure => structure.TokenSpanRole == TokenSpanTypeRole.Snapshot) != 1))
        {
            throw new ModuleFormatException("Private span IR requires explicit token and range type roles.");
        }
    }
}
