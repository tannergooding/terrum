// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    private static byte[] EmitLocalAggregateLayoutDraft(
        BoundProgram program, string moduleIdentity,
        System.Collections.Generic.IReadOnlyList<TrRmSourceTypeExport>?
            sourceTypeExports)
    {
        program = SelectLocalAggregateLayoutRootProgram(program,
            moduleIdentity);
        if (program.Functions.IsEmpty)
        {
            return EmitForeignInvocationDraftNativeAddressLibrary(program,
                moduleIdentity, TrRmArtifactSchema.LocalAggregateLayoutDraft,
                sourceTypeExports);
        }

        if (program.EntryPoint < -1)
        {
            throw Unsupported(
                "L1 Body emission has an invalid entry MethodDef index.");
        }

        if (sourceTypeExports is { Count: > 0 })
        {
            throw Unsupported(
                "Explicit source TypeDef exports are supported only by the L1 type-only provider route.");
        }

        var normalized = NormalizeLocalAggregateConstructors(program);
        var hasModuleReferences = normalized.Functions.Any(
            static function => function.IsModuleReference);
        if (normalized.EntryPoint == -1)
        {
            if (hasModuleReferences)
            {
                SemanticVerifier.VerifyLibraryReferences(normalized);
            }
            else
            {
                SemanticVerifier.VerifyLibraryWithDirectNativeImports(
                    normalized);
            }
        }
        else if (hasModuleReferences)
        {
            SemanticVerifier.VerifyReferences(normalized);
        }
        else
        {
            SemanticVerifier.VerifyWithDirectNativeImports(normalized);
        }
        return EmitLocalAggregateLayoutRegisterBodies(normalized,
            moduleIdentity);
    }

    private static BoundProgram SelectLocalAggregateLayoutRootProgram(
        BoundProgram program, string moduleIdentity)
    {
        if (program.StandardMethodProviderClosure is not { } closure)
        {
            return program;
        }

        var rootModules = closure.Modules.Where(module =>
            module.ArtifactIndex == closure.RootArtifactIndex
                && module.ModuleRow == closure.RootModuleRow).ToArray();
        if (closure.RootArtifactIndex != 0
            || !StringComparer.Ordinal.Equals(closure.RootModuleIdentity,
                moduleIdentity)
            || rootModules.Length != 1
            || !StringComparer.Ordinal.Equals(rootModules[0].Identity,
                moduleIdentity)
            || rootModules[0].Program.StandardMethodProviderClosure is not null)
        {
            throw Unsupported(
                "L1 Body re-emission requires one exact retained root Module program.");
        }

        var rootModule = rootModules[0];
        bool IsRetainedFunction(int functionIndex)
        {
            if ((uint)functionIndex >= (uint)program.Functions.Length
                || (uint)functionIndex
                    >= (uint)rootModule.Program.Functions.Length)
            {
                return false;
            }

            var suppliedFunction = program.Functions[functionIndex];
            var retainedFunction = rootModule.Program.Functions[
                functionIndex];
            return StringComparer.Ordinal.Equals(suppliedFunction.Name,
                    retainedFunction.Name)
                && suppliedFunction.ModuleImport
                    == retainedFunction.ModuleImport
                && suppliedFunction.ReturnType
                    == retainedFunction.ReturnType
                && suppliedFunction.ParameterCount
                    == retainedFunction.ParameterCount
                && suppliedFunction.Variables.SequenceEqual(
                    retainedFunction.Variables);
        }

        foreach (var edge in closure.MethodEdges)
        {
            if (edge.ConsumerArtifactIndex != closure.RootArtifactIndex
                || edge.ConsumerModuleRow != closure.RootModuleRow)
            {
                continue;
            }

            var importRow = edge.ImportRow;
            if (importRow <= 0
                || (uint)importRow
                    >= (uint)rootModule.FunctionIndexByImportRow.Length
                || edge.Expected.FunctionIndex
                    != rootModule.FunctionIndexByImportRow[importRow]
                || edge.Expected.MethodDefRow <= 0
                || (uint)edge.Expected.MethodDefRow
                    >= (uint)rootModule.FunctionIndexByMethodRow.Length)
            {
                throw Unsupported(
                    "L1 MethodDef Import re-emission has an invalid retained function mapping.");
            }

            var methodFunctionIndex
                = rootModule.FunctionIndexByMethodRow[
                    edge.Expected.MethodDefRow];
            if (!IsRetainedFunction(edge.Expected.FunctionIndex)
                || !IsRetainedFunction(methodFunctionIndex))
            {
                throw Unsupported(
                    "An L1 MethodDef Import no longer matches its exact retained provider edge.");
            }
        }

        var rootProgram = rootModule.Program;
        if (program.Functions.Length != rootProgram.Functions.Length
            || program with {
                Functions = rootProgram.Functions,
                StandardMethodProviderClosure = null,
            } != rootProgram)
        {
            throw Unsupported(
                "L1 Body re-emission cannot discard changed root Module metadata.");
        }

        for (var functionIndex = 0;
            functionIndex < rootProgram.Functions.Length; functionIndex++)
        {
            var suppliedFunction = program.Functions[functionIndex];
            var retainedFunction = rootProgram.Functions[functionIndex];
            if (suppliedFunction with {
                    Body = retainedFunction.Body,
                } != retainedFunction
                || ((retainedFunction.IsExternal
                        || retainedFunction.ModuleImport is not null)
                    && suppliedFunction.Body != retainedFunction.Body))
            {
                throw Unsupported(
                    "L1 Body re-emission permits changes only to retained root function bodies.");
            }
        }

        return rootProgram with {
            Functions = program.Functions,
            StandardMethodProviderClosure = closure,
        };
    }

    private static BoundProgram NormalizeLocalAggregateConstructors(
        BoundProgram program)
    {
        var functions = ImmutableArray.CreateBuilder<BoundFunction>(
            program.Functions.Length);
        for (var functionIndex = 0;
            functionIndex < program.Functions.Length; functionIndex++)
        {
            var function = program.Functions[functionIndex];
            if (!function.IsConstructor)
            {
                functions.Add(function);
                continue;
            }

            if (!function.IsPrivate && !function.IsInternal)
            {
                throw Unsupported(
                    "L1 cannot normalize a public constructor because its exported source identity is not represented by the local-helper contract.");
            }

            var structureIndex = (int)function.ReturnType - 100;
            if (function.IsExternal || function.IsClassConstructor
                || function.DeclaringStruct is null
                || (uint)structureIndex >= (uint)program.Structures.Length
                || !StringComparer.Ordinal.Equals(
                    function.DeclaringStruct,
                    program.Structures[structureIndex].Name)
                || function.Variables.Length < function.ParameterCount + 1
                || function.Variables[0].Type != function.ReturnType)
            {
                throw Unsupported(
                    "L1 constructor normalization requires a local struct construction receiver.");
            }

            var structure = program.Structures[structureIndex];
            if (structure.IsUnique || structure.CleanupMethod is not null)
            {
                throw Unsupported(
                    "L1 constructor normalization requires a rootless Copy result without Cleanup.");
            }
            if (structure.BlittableTargetRequirement
                != FixedLayoutStorage.WindowsX64TargetRequirement)
            {
                throw Unsupported(
                    "L1 constructor normalization requires the selected fixed-layout target.");
            }

            var constructionHomeIndex = function.ParameterCount;
            var variables = ImmutableArray.CreateBuilder<BoundVariable>(
                function.Variables.Length);
            for (var parameter = 0; parameter < function.ParameterCount;
                parameter++)
            {
                variables.Add(function.Variables[parameter + 1]);
            }

            variables.Add(function.Variables[0] with {
                Name = "$construction",
                IsMutable = true,
                IsOut = false,
                IsInit = false,
                IsImplicitRef = false,
                IsInitializationLocal = true,
            });
            for (var local = function.ParameterCount + 1;
                local < function.Variables.Length; local++)
            {
                variables.Add(function.Variables[local]);
            }

            int RemapVariable(int variable)
            {
                return variable switch {
                    0 => constructionHomeIndex,
                    > 0 when variable <= function.ParameterCount => variable - 1,
                    _ => variable,
                };
            }

            var normalized = BoundTreeTraversal.RewriteVariables(function,
                variables.MoveToImmutable(), RemapVariable);
            normalized = normalized with {
                Body = normalized.Body with {
                    Statements = [
                        new BoundVacantLocal(constructionHomeIndex,
                            function.Span),
                        .. normalized.Body.Statements,
                    ],
                },
                IsConstructor = false,
                IsStatic = true,
                IsPrivate = true,
                IsInternal = false,
                DeclaringStruct = null,
                ConstructionHomeIndex = constructionHomeIndex,
            };
            functions.Add(normalized);
        }

        return program with { Functions = functions.MoveToImmutable() };
    }
}
