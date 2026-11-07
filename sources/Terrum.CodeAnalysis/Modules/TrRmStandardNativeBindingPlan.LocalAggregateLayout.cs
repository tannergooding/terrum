// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal sealed partial class TrRmStandardNativeBindingPlan
{
    private BoundProgram CreateLocalAggregateLayoutLoweringProgram()
    {
        // Reload the owned snapshots, including uncalled Imports, before creating
        // any backend input. The retained semantic Programs are never linked in place.
        var providers = new TrRmStandardArtifactInput[_artifacts.Length - 1];
        for (var index = 0; index < providers.Length; index++)
        {
            providers[index] = new TrRmStandardArtifactInput(
                _schemaIdentities[index + 1], _artifacts[index + 1]);
        }

        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(_schemaIdentities[0],
                _artifacts[0]), ModuleIdentity, providers);
        var original = RootProgram.StandardMethodProviderClosure;
        var closure = reloaded.StandardMethodProviderClosure;
        if (original is null || closure is null)
        {
            if (original is not null || closure is not null
                || _modules.Length != 1
                || RootProgram.Functions.Length != 0
                || reloaded.Functions.Length != 0)
            {
                throw new InvalidDataException(
                    "The L1 native plan lost its semantic provider closure.");
            }

            SemanticVerifier.VerifyLibrary(reloaded);
            return reloaded;
        }

        ValidateLocalAggregateLayoutClosure(original, closure);
        return new LocalAggregateLayoutNativeProgramBuilder(closure).Build();
    }

    private void ValidateLocalAggregateLayoutClosure(
        BoundStandardMethodProviderClosure original,
        BoundStandardMethodProviderClosure actual)
    {
        if (original.RootArtifactIndex != 0
            || actual.RootArtifactIndex != 0
            || original.RootModuleRow != RootModuleRow
            || actual.RootModuleRow != RootModuleRow
            || !StringComparer.Ordinal.Equals(
                original.RootModuleIdentity, ModuleIdentity)
            || !StringComparer.Ordinal.Equals(
                actual.RootModuleIdentity, ModuleIdentity)
            || original.Artifacts.Length != _artifacts.Length
            || actual.Artifacts.Length != _artifacts.Length
            || original.Modules.Length != _modules.Length
            || actual.Modules.Length != _modules.Length
            || !original.MethodEdges.SequenceEqual(actual.MethodEdges)
            || !original.TypeEdges.SequenceEqual(actual.TypeEdges))
        {
            throw new InvalidDataException(
                "The L1 native plan changed its original MethodDef or TypeDef provider closure.");
        }

        for (var index = 0; index < _artifacts.Length; index++)
        {
            var expectedArtifact = original.Artifacts[index];
            var actualArtifact = actual.Artifacts[index];
            if (expectedArtifact.ArtifactIndex != index
                || actualArtifact.ArtifactIndex != index
                || !StringComparer.Ordinal.Equals(
                    expectedArtifact.SchemaIdentity, _schemaIdentities[index])
                || !StringComparer.Ordinal.Equals(
                    actualArtifact.SchemaIdentity, _schemaIdentities[index])
                || !expectedArtifact.ArtifactSnapshot.AsSpan()
                    .SequenceEqual(_artifacts[index])
                || !actualArtifact.ArtifactSnapshot.AsSpan()
                    .SequenceEqual(_artifacts[index]))
            {
                throw new InvalidDataException(
                    "The L1 native plan changed an exact selected artifact.");
            }
        }

        for (var index = 0; index < _modules.Length; index++)
        {
            var expectedModule = original.Modules[index];
            var actualModule = actual.Modules[index];
            var selection = _modules[index];
            if (new TrRmStandardModuleSelection(
                    actualModule.ArtifactIndex, actualModule.ModuleRow,
                    actualModule.Identity, actualModule.TargetRequirement,
                    actualModule.RuntimePolicy, actualModule.EntryMethodDefRow,
                    actualModule.MethodDefStart, actualModule.MethodDefEnd)
                    != selection
                || (expectedModule with { Program = actualModule.Program,
                    FunctionIndexByMethodRow = actualModule.FunctionIndexByMethodRow,
                    FunctionIndexByImportRow = actualModule.FunctionIndexByImportRow,
                }) != actualModule
                || !expectedModule.FunctionIndexByMethodRow.SequenceEqual(
                    actualModule.FunctionIndexByMethodRow)
                || !expectedModule.FunctionIndexByImportRow.SequenceEqual(
                    actualModule.FunctionIndexByImportRow))
            {
                throw new InvalidDataException(
                    "The L1 native plan changed an exact selected Module or MethodDef origin.");
            }
        }
    }

    private sealed class LocalAggregateLayoutNativeProgramBuilder
    {
        private readonly record struct ModuleOrigin(int ArtifactIndex, int ModuleRow);
        private readonly record struct MethodOrigin(ModuleOrigin Module, int MethodDefRow);
        private readonly record struct FunctionOrigin(ModuleOrigin Module, int FunctionIndex);
        private readonly record struct TypeOrigin(ModuleOrigin Module, uint DefinitionId);
        private readonly record struct LocalTypeOrigin(ModuleOrigin Module, PrimitiveType Type);

        private readonly BoundStandardMethodProviderClosure _closure;
        private readonly ModuleOrigin _root;
        private readonly Dictionary<ModuleOrigin, BoundStandardMethodProviderModule> _modules = [];
        private readonly Dictionary<MethodOrigin, int> _methods = [];
        private readonly Dictionary<FunctionOrigin, int> _functions = [];
        private readonly List<(BoundStandardMethodProviderModule Module, int FunctionIndex)> _implementations = [];
        private readonly Dictionary<TypeOrigin, TypeOrigin> _typeProviders = [];
        private readonly Dictionary<TypeOrigin, (BoundStandardMethodProviderModule Module, int Index)> _declarations = [];
        private readonly Dictionary<TypeOrigin, PrimitiveType> _nominalTypes = [];
        private readonly Dictionary<LocalTypeOrigin, PrimitiveType> _types = [];
        private readonly HashSet<LocalTypeOrigin> _pendingTypes = [];
        private readonly List<(BoundStandardMethodProviderModule Module, int Index)> _structures = [];
        private readonly List<BoundNativeFixedArrayType> _arrays = [];
        private readonly Dictionary<(PrimitiveType ElementType, ulong ElementCount), int> _arrayIndexes = [];
        private readonly List<NativePointerTypeDefinition> _pointers = [];
        private readonly Dictionary<NativePointerTypeDefinition, int> _pointerIndexes = [];

        internal LocalAggregateLayoutNativeProgramBuilder(
            BoundStandardMethodProviderClosure closure)
        {
            _closure = closure;
            _root = new ModuleOrigin(closure.RootArtifactIndex,
                closure.RootModuleRow);
        }

        internal BoundProgram Build()
        {
            var modulesByArtifact = _closure.Modules.ToLookup(
                static module => module.ArtifactIndex);
            foreach (var artifact in _closure.Artifacts)
            {
                var tables = TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                        TrRmArtifactIndex.ForLocalAggregateLayoutDraft(
                            artifact.ArtifactSnapshot.AsSpan()));
                foreach (var module in modulesByArtifact[artifact.ArtifactIndex])
                {
                    var location = Location(module);
                    _modules.Add(location, module);
                    ValidateProgram(module.Program);
                    var (typeStart, typeEnd) = tables.Modules.GetTypeDefRange(
                        module.ModuleRow);
                    if (typeEnd - typeStart != module.Program.Structures.Length)
                    {
                        throw new InvalidDataException(
                            "The L1 native type map lost its Module-owned TypeDef range.");
                    }
                    for (var index = 0; index < module.Program.Structures.Length; index++)
                    {
                        _declarations.Add(new TypeOrigin(location,
                            0x1300_0000u | checked((uint)(typeStart + index))),
                            (module, index));
                    }

                    for (var methodRow = module.MethodDefStart;
                        methodRow < module.MethodDefEnd; methodRow++)
                    {
                        var index = module.FunctionIndexByMethodRow[methodRow];
                        if (!module.Program.Functions[index].IsExternal)
                        {
                            if (_implementations.Count >= SemanticVerifier.MaximumFunctions)
                            {
                                throw new NotSupportedException(
                                    "The joint L1 native program exceeds the function limit.");
                            }
                            _methods.Add(new MethodOrigin(location, methodRow),
                                _implementations.Count);
                            _functions.Add(new FunctionOrigin(location, index),
                                _implementations.Count);
                            _implementations.Add((module, index));
                        }
                    }
                }
            }

            foreach (var edge in _closure.TypeEdges)
            {
                var expected = new TypeOrigin(new ModuleOrigin(
                    edge.ConsumerArtifactIndex, edge.ConsumerModuleRow),
                    edge.ExpectedDefinitionId);
                var actual = new TypeOrigin(new ModuleOrigin(
                    edge.ProviderArtifactIndex, edge.ProviderModuleRow),
                    edge.ActualDefinitionId);
                if (_typeProviders.TryGetValue(expected, out var previous)
                    && previous != actual)
                {
                    throw new InvalidDataException(
                        "The L1 native type map has conflicting provider origins.");
                }
                _typeProviders[expected] = actual;
            }

            foreach (var (origin, declaration) in _declarations)
            {
                var actual = ResolveTypeOrigin(origin);
                if (!_nominalTypes.TryGetValue(actual, out var type))
                {
                    if (_structures.Count >= SemanticVerifier.MaximumVariables)
                    {
                        throw new NotSupportedException(
                            "The joint L1 native program exceeds the nominal type limit.");
                    }
                    type = (PrimitiveType)(100 + _structures.Count);
                    _nominalTypes.Add(actual, type);
                    _structures.Add(_declarations[actual]);
                }
                _types.Add(new LocalTypeOrigin(origin.Module,
                    (PrimitiveType)(100 + declaration.Index)), type);
            }

            foreach (var edge in _closure.MethodEdges)
            {
                var consumer = new ModuleOrigin(edge.ConsumerArtifactIndex,
                    edge.ConsumerModuleRow);
                var actual = new MethodOrigin(new ModuleOrigin(
                    edge.Actual.ArtifactIndex, edge.Actual.ModuleRow),
                    edge.Actual.MethodDefRow);
                if (!_methods.TryGetValue(actual, out var target)
                    || MapFunction(_modules[actual.Module],
                        edge.Actual.FunctionIndex) != target)
                {
                    throw new InvalidDataException(
                        "An L1 MethodDef Import has no provider-owned implementation.");
                }
                AddImport(new FunctionOrigin(consumer,
                    edge.Expected.FunctionIndex), target);
                AddImport(new FunctionOrigin(consumer,
                    _modules[consumer].FunctionIndexByMethodRow[
                        edge.Expected.MethodDefRow]), target);
            }
            foreach (var module in _closure.Modules)
            {
                for (var index = 0; index < module.Program.Functions.Length; index++)
                {
                    _ = MapFunction(module, index);
                }
            }

            var structures = ImmutableArray.CreateBuilder<BoundStruct>(_structures.Count);
            for (var index = 0; index < _structures.Count; index++)
            {
                var (module, sourceIndex) = _structures[index];
                var structure = BoundTreeTraversal.RewriteTypes(
                    module.Program.Structures[sourceIndex],
                    type => MapType(module, type));
                structures.Add(structure with {
                    Name = $"__trrm_l1_type_{index}",
                });
            }

            // Reserve root names before allocating backend-only provider names.
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (module, index) in _implementations)
            {
                if (Location(module) == _root
                    && !names.Add(module.Program.Functions[index].Name))
                {
                    throw new NotSupportedException(
                        "The L1 native root contains ambiguous backend function names.");
                }
            }
            var functions = ImmutableArray.CreateBuilder<BoundFunction>(_implementations.Count);
            foreach (var (module, index) in _implementations)
            {
                var function = BoundTreeTraversal.RewriteTypes(
                    module.Program.Functions[index],
                    type => MapType(module, type));
                function = BoundTreeTraversal.RewriteCalls(function,
                    call => call with {
                        Function = MapFunction(module, call.Function),
                    });
                if (Location(module) != _root)
                {
                    var name = $"__trrm_l1_method_{module.ArtifactIndex}_{module.ModuleRow}_{index}";
                    while (!names.Add(name))
                    {
                        name += "_";
                    }
                    function = function with {
                        Name = name,
                        IsInternal = !function.IsPrivate,
                    };
                }
                functions.Add(function);
            }

            var rootProgram = _modules[_root].Program;
            var program = rootProgram with {
                Functions = functions.MoveToImmutable(),
                EntryPoint = rootProgram.EntryPoint < 0 ? -1
                    : MapFunction(_modules[_root], rootProgram.EntryPoint),
                Structures = structures.MoveToImmutable(),
                NativeFixedArrayTypes = [.. _arrays],
                NativePointerTypes = [.. _pointers],
                SourceTypeDeclarations = [],
                SourceTypePublications = default,
                StandardMethodProviderClosure = _closure,
            };
            if (program.EntryPoint < 0)
            {
                SemanticVerifier.VerifyLibrary(program);
            }
            else
            {
                SemanticVerifier.Verify(program);
            }
            return program;
        }

        private static ModuleOrigin Location(
            BoundStandardMethodProviderModule module)
            => new(module.ArtifactIndex, module.ModuleRow);

        private static void ValidateProgram(BoundProgram program)
        {
            if (program.RuntimePolicy != BoundRuntimePolicy.FailFast
                || !program.Classes.IsEmpty || !program.Buffers.IsEmpty
                || !program.NativeFunctionPointerSignatures.IsEmpty
                || !program.CheckedReferenceTypes.IsEmpty
                || !program.GenericNominalTypeDefinitions.IsEmpty
                || !program.GenericNominalTypeInstances.IsEmpty
                || !program.Interfaces.IsEmpty || !program.EnumDefinitions.IsEmpty
                || program.RequiresSourceGrant
                || program.Functions.Any(static function =>
                    function.DirectNativeImport is not null
                    || function.NativeVTableMethod is not null
                    || function.NativeFunctionPointerType is not null
                    || function.NativeCallableEntryName is not null
                    || function.DeclaringStruct is not null
                    || function.IsClassConstructor
                    || function.ClassConstructor >= 0))
            {
                throw new NotSupportedException(
                    "The L1 native operation requires the selected ordinary MethodDef and Copy-layout profile.");
            }
        }

        private TypeOrigin ResolveTypeOrigin(TypeOrigin origin)
        {
            var visited = new HashSet<TypeOrigin>();
            while (_typeProviders.TryGetValue(origin, out var provider))
            {
                if (!visited.Add(origin))
                {
                    throw new InvalidDataException(
                        "The L1 native type map contains a provider identity cycle.");
                }
                origin = provider;
            }
            if (!_declarations.ContainsKey(origin))
            {
                throw new InvalidDataException(
                    "The L1 native type map lost an exact selected TypeDef.");
            }
            return origin;
        }

        private void AddImport(FunctionOrigin origin, int target)
        {
            if (_functions.TryGetValue(origin, out var previous)
                && previous != target)
            {
                throw new InvalidDataException(
                    "The L1 native method map has conflicting provider origins.");
            }
            var function = _modules[origin.Module].Program.Functions[
                origin.FunctionIndex];
            if (!function.IsExternal || !function.IsModuleReference
                || function.ModuleImport is null)
            {
                throw new InvalidDataException(
                    "The L1 native method map lost its original Import expectation.");
            }
            _functions[origin] = target;
        }

        private int MapFunction(BoundStandardMethodProviderModule module,
            int index)
            => _functions.TryGetValue(new FunctionOrigin(Location(module), index),
                out var mapped) ? mapped : throw new InvalidDataException(
                    "The L1 native method map contains an unresolved call target.");

        private PrimitiveType MapType(
            BoundStandardMethodProviderModule module, PrimitiveType type,
            int depth = 0)
        {
            var origin = new LocalTypeOrigin(Location(module), type);
            if (_types.TryGetValue(origin, out var mapped))
            {
                return mapped;
            }
            if (!NativePointerTypes.IsType(type)
                && !NativeFixedArrayTypes.IsType(type))
            {
                if ((int)type >= 100)
                {
                    throw new InvalidDataException(
                        "The L1 native type map contains an unresolved nominal identity.");
                }
                return type;
            }
            if (depth > SemanticVerifier.MaximumDepth
                || !_pendingTypes.Add(origin))
            {
                throw new InvalidDataException(
                    "The L1 native type map contains an invalid structural type recipe.");
            }

            if (NativeFixedArrayTypes.IsType(type))
            {
                var source = module.Program.NativeFixedArrayTypes[
                    NativeFixedArrayTypes.Index(type)];
                var array = source with {
                    ElementType = MapType(module, source.ElementType, depth + 1),
                };
                var identity = (array.ElementType, array.ElementCount);
                if (!_arrayIndexes.TryGetValue(identity, out var index))
                {
                    if (_arrays.Count >= SemanticVerifier.MaximumAnalysisVisits)
                    {
                        throw new NotSupportedException(
                            "The joint L1 native array table exceeds its limit.");
                    }
                    index = _arrays.Count;
                    _arrayIndexes.Add(identity, index);
                    _arrays.Add(array);
                }
                else if (_arrays[index] != array)
                {
                    throw new InvalidDataException(
                        "One L1 array identity has conflicting selected layout facts.");
                }
                mapped = NativeFixedArrayTypes.Type(index);
            }
            else
            {
                var source = module.Program.NativePointerTypes[
                    NativePointerTypes.Index(type)];
                var pointer = source with {
                    PointeeType = MapType(module, source.PointeeType, depth + 1),
                };
                if (!_pointerIndexes.TryGetValue(pointer, out var index))
                {
                    if (_pointers.Count >= SemanticVerifier.MaximumAnalysisVisits)
                    {
                        throw new NotSupportedException(
                            "The joint L1 native pointer table exceeds its limit.");
                    }
                    index = _pointers.Count;
                    _pointerIndexes.Add(pointer, index);
                    _pointers.Add(pointer);
                }
                mapped = NativePointerTypes.Type(index);
            }

            _ = _pendingTypes.Remove(origin);
            _types.Add(origin, mapped);
            return mapped;
        }
    }
}
