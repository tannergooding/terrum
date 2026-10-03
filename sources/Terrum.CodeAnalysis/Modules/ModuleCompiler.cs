// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

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

/// <summary>A bounded semantic module artifact; linking never reads source text.</summary>
public static partial class ModuleCompiler
{
    private const int Magic = 0x4D525254; // TRRM
    private const int Version = 49;
    private const int MaximumBytes = 4 * 1024 * 1024;
    private const int MaximumFunctions = 1024;
    private const int MaximumSlots = 256;
    private const int MaximumNodes = 100_000;
    private const int MaximumDepth = 128;
    private const int MaximumStringBytes = 1024;

    private static bool HasInitReference(Module module) =>
        module.Functions.Any(static item => item.Contract.Contains("|init:", StringComparison.Ordinal)
            || (item.Function is BoundFunction function
                && function.Variables.Any(static variable => variable.IsInit)));

    private static bool HasExplicitGetter(Module module) =>
        module.Functions.Any(static item => item.Function is BoundFunction { PropertyName: not null });

    private static bool HasTransparentLayout(Module module) =>
        module.Structures.Any(static structure => structure.IsTransparent);

    private static bool HasWideClassFields(Module module) =>
        module.Classes.Any(static @class => @class.Fields.Length > 16);

    private static bool HasCorePrimitiveMembers(Module module, bool gettersOnly = false)
    {
        foreach (var item in module.Functions)
        {
            if (gettersOnly && item.Function?.PropertyName is null
                && !item.Contract.Contains("|Property:", StringComparison.Ordinal))
            {
                continue;
            }
            var owner = item.Function?.DeclaringStruct;
            if (owner is null && item.Import)
            {
                var parts = item.Contract.Split('|');
                owner = parts.Length > 6 ? parts[5] : null;
            }
            if (owner is not null && Binder.TryPrimitiveType(owner, out _)
                && !module.Structures.Any(structure => structure.Name == owner)
                && !module.Classes.Any(@class => @class.Name == owner))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Reads and verifies a producer artifact, exposing only exported declarations and their value layouts to source binding.</summary>
    public static CompilationReference CreateReference(byte[] producerBytes)
    {
        var producer = Read(producerBytes, expectedConsumer: false);
        ValidateProducer(producer);
        return CreateReference(producer);
    }

    private static CompilationReference CreateReference(Module producer)
    {
        var declarations = producer.Functions.Where(static function => function.Import)
            .Select(static item => Declaration(item.Function ?? throw new ModuleFormatException("Missing export declaration.")))
            .ToImmutableArray();
        var required = RequiredStructures(declarations, producer.Structures);
        var structures = SelectStructures(producer.Structures, required);
        return new CompilationReference(structures,
            [.. declarations.Select(function => RemapFunction(function, producer.Structures, structures))], producer.RuntimePolicy);
    }

    internal static ImmutableArray<string> GetConsumerCallImports(byte[] consumerBytes)
    {
        var consumer = Read(consumerBytes, expectedConsumer: true);
        var indexes = consumer.Functions.Select((function, index) => (function.Name, index))
            .ToDictionary(static item => item.Name, static item => item.index, StringComparer.Ordinal);
        var imports = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in consumer.Functions)
        {
            if (function.Function is BoundFunction body)
            {
                var resolved = (BoundBlock)ResolveStatement(body.Body, indexes);
                foreach (var target in Calls(resolved))
                {
                    if (consumer.Functions[target].Import)
                    {
                        _ = imports.Add(consumer.Functions[target].Name);
                    }
                }
            }
        }
        return [.. imports.Order(StringComparer.Ordinal)];
    }

    /// <summary>Reads the native semantic profile of a persisted module, including its retained value contracts.</summary>
    /// <param name="artifact">The bounded semantic module bytes.</param>
    /// <param name="isConsumer">Whether a consumer rather than a producer artifact is expected.</param>
    /// <returns>The native semantic profile recorded and checked against the module's layouts.</returns>
    public static string GetNativeProfileVersion(byte[] artifact, bool isConsumer) =>
        ReadNativeProfile(artifact, isConsumer).Profile;

    internal static (string Profile, BoundRuntimePolicy RuntimePolicy) ReadNativeProfile(byte[] artifact, bool isConsumer)
    {
        var module = Read(artifact, isConsumer);
        return (RetainedNativeProfile(module), module.RuntimePolicy);
    }

    private static string RetainedNativeProfile(Module module)
    {
        if (module.Structures.Any(static structure => structure.Fields.Any(
            static field => field.Type is PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView))
            || module.Functions.Any(static item => item.Import
                && (ContractContainsType(item.Contract, PrimitiveType.ReadonlyInt32BufferView)
                    || ContractContainsType(item.Contract, PrimitiveType.MutableInt32BufferView))))
        {
            throw new ModuleFormatException("Readonly buffer views cannot cross module value contracts.");
        }
        if (module.Classes.Any(static item => item.Fields.Any(
                static field => ManagedClassTypes.IsOptional(field.Type)))
            || module.Functions.Any(static item => item.Function is BoundFunction function
                && (ManagedClassTypes.IsOptional(function.ReturnType)
                    || function.Variables.Any(static variable => ManagedClassTypes.IsOptional(variable.Type)))))
        {
            return "native-20";
        }
        if (!module.Classes.IsEmpty)
        {
            return "native-19";
        }
        if (HasManagedNodes(module.Functions))
        {
            return "native-18";
        }
        var profile = Compilation.GetNativeProfileVersion(module.RuntimePolicy,
            module.Structures.Any(static structure => structure.IsUnique),
            module.Functions.Any(static function => function.HasOwnershipTransfer),
            Compilation.HasCompositeUnique(module.Structures),
            HasOwnedNativeBuffer(module.Structures, module.Functions),
            HasReadonlyNativeView(module.Functions), HasMutableNativeViewOrClear(module.Functions));
        return profile;
    }

    private static void RejectManagedNodes(BoundProgram program)
    {
        RejectManagedClasses(program);
        if (HasManagedNodes(program)
            || program.Structures.Any(static structure =>
                structure.Fields.Any(static field => ManagedClassTypes.IsManaged(field.Type))))
        {
            throw new ModuleFormatException("Managed nodes have no persisted module runtime or ABI.");
        }
    }

    private static void RejectManagedClasses(BoundProgram program)
    {
        if (!program.Classes.IsDefaultOrEmpty)
        {
            throw new ModuleFormatException("Source classes have no persisted module runtime or ABI.");
        }
        if (program.Structures.Any(static structure => structure.Fields.Any(
                static field => ManagedClassTypes.IsOptional(field.Type)))
            || program.Functions.Any(static function => ManagedClassTypes.IsOptional(function.ReturnType)
                || function.Variables.Any(static variable => ManagedClassTypes.IsOptional(variable.Type))))
        {
            throw new ModuleFormatException("Optional managed handles have no persisted module ABI.");
        }
    }

    private static bool HasManagedNodes(IEnumerable<ModuleFunction> functions) =>
        functions.Any(static item => item.Function is BoundFunction function
            && UsesManagedNode(function));

    private static bool HasManagedNodes(BoundProgram program) =>
        program.Functions.Any(UsesManagedNode);

    private static bool HasPrivateManagedState(BoundProgram program) =>
        !program.Classes.IsDefaultOrEmpty || HasManagedNodes(program)
        || program.Functions.Any(static function => ManagedClassTypes.IsOptional(function.ReturnType)
            || function.Variables.Any(static variable => ManagedClassTypes.IsOptional(variable.Type)));

    private static bool HasPrivateManagedState(Module module) =>
        !module.Classes.IsEmpty || HasManagedNodes(module.Functions)
        || module.Functions.Any(static item => item.Function is BoundFunction function
            && (ManagedClassTypes.IsOptional(function.ReturnType)
                || function.Variables.Any(static variable => ManagedClassTypes.IsOptional(variable.Type))));

    private static bool UsesManagedNode(BoundFunction function) =>
        function.Intrinsic is >= BoundIntrinsic.NodeTryCreate and <= BoundIntrinsic.NodeCollect
        || function.ReturnType is PrimitiveType.ManagedNode or PrimitiveType.WriteManagedNode
        || function.Variables.Any(static variable => variable.Type is PrimitiveType.ManagedNode
            or PrimitiveType.WriteManagedNode);

    private static bool HasOwnedNativeBuffer(ImmutableArray<BoundStruct> structures,
        ImmutableArray<ModuleFunction> functions) =>
        Compilation.HasOwnedNativeBuffer(structures,
            functions.Where(static function => function.Function is not null)
                .Select(static function => function.Function ?? throw new ModuleFormatException("Missing function body.")))
        || functions.Any(static function => ContractContainsType(function.Contract, PrimitiveType.OwnedInt32Buffer));

    private static bool ContractContainsType(string contract, PrimitiveType type)
    {
        var parts = contract.Split('|');
        var parameterCount = ContractParameterCount(parts);
        var typeName = ((int)type).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (parts[1] == typeName)
        {
            return true;
        }
        for (var i = 0; i < parameterCount; i++)
        {
            if (parts[8 + i] == "in:" + typeName || parts[8 + i] == "out:" + typeName)
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContractContainsOptionalType(string contract)
    {
        var parts = contract.Split('|');
        var count = ContractParameterCount(parts);
        if (parts.Length < 8 + count)
        {
            throw new ModuleFormatException("Invalid module type contract.");
        }
        if (Optional(parts[1]))
        {
            return true;
        }
        for (var i = 0; i < count; i++)
        {
            var parameter = parts[8 + i];
            if (Optional(parameter[(parameter.IndexOf(':', StringComparison.Ordinal) + 1)..]))
            {
                return true;
            }
        }
        return false;

        static bool Optional(string identity)
        {
            return int.TryParse(identity, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
                && ManagedClassTypes.IsOptional((PrimitiveType)value);
        }
    }

    private static bool HasMutableNativeViewOrClear(ImmutableArray<ModuleFunction> functions)
    {
        foreach (var item in functions)
        {
            if (item.Function is BoundFunction function)
            {
                if (function.Intrinsic == BoundIntrinsic.BufferClear)
                {
                    return true;
                }
                var found = false;
                _ = Calls(function.Body, visitView: view => found |= view.IsMutable);
                if (found)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static int ContractParameterCount(string contract) => ContractParameterCount(contract.Split('|'));

    private static int ContractParameterCount(string[] parts)
    {
        if (parts.Length < 8 || !int.TryParse(parts[7], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var parameterCount)
            || parameterCount < 0 || parameterCount > MaximumSlots || parts.Length < 8 + parameterCount)
        {
            throw new ModuleFormatException("Invalid function contract parameter count.");
        }
        return parameterCount;
    }

    private static bool HasReadonlyNativeView(ImmutableArray<ModuleFunction> functions)
    {
        foreach (var item in functions)
        {
            if (item.Function is BoundFunction function)
            {
                var found = false;
                _ = Calls(function.Body, visitView: view => found |= !view.IsMutable);
                if (found)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static BoundFunction Declaration(BoundFunction function) => function with {
        IsExternal = true,
        Variables = [.. function.Variables.Take(function.ParameterCount + (function.IsConstructor ? 1 : 0))
            .Select(static variable => variable with { Span = default })],
        Body = new BoundBlock([], default),
        Span = default,
    };

    private static HashSet<string> RequiredStructures(IEnumerable<BoundFunction> functions,
        ImmutableArray<BoundStruct> structures, ImmutableArray<BoundClass> classes = default)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<PrimitiveType>();
        if (!classes.IsDefaultOrEmpty)
        {
            foreach (var @class in classes)
            {
                foreach (var field in @class.Fields)
                {
                    Add(field.Type);
                }
            }
        }
        foreach (var function in functions)
        {
            Add(function.ReturnType);
            foreach (var variable in function.Variables)
            {
                Add(variable.Type);
            }
            if (function.DeclaringStruct is string declaring
                && declaring != NativeBufferIntrinsics.TypeName
                && declaring != NativeManagedNodeIntrinsics.TypeName
                && declaring != NativeManagedNodeIntrinsics.HeapName
                && structures.Any(structure => structure.Name == declaring))
            {
                var index = 0;
                while (index < structures.Length && structures[index].Name != declaring)
                {
                    index++;
                }
                Add((PrimitiveType)(100 + index));
            }
            _ = Calls(function.Body, Add);
        }
        while (pending.TryDequeue(out var type))
        {
            foreach (var field in structures[(int)type - 100].Fields)
            {
                Add(field.Type);
            }
        }
        return result;

        void Add(PrimitiveType type)
        {
            var index = (int)type - 100;
            if (index >= 0 && !ManagedClassTypes.IsManaged(type))
            {
                if (index >= structures.Length)
                {
                    throw new ModuleFormatException("Invalid declared type reference.");
                }
                if (result.Add(structures[index].Name))
                {
                    pending.Enqueue(type);
                }
            }
        }
    }

    private static void ValidateProducer(Module producer)
    {
        var names = producer.Functions.Select((function, index) => (function.Name, Index: index))
            .ToDictionary(static item => item.Name, static item => item.Index, StringComparer.Ordinal);
        var program = Resolve(new BoundProgram([.. producer.Functions.Select(static function =>
            function.Function ?? throw new ModuleFormatException("Missing producer body."))], -1) {
            Structures = producer.Structures,
            Classes = producer.Classes,
            RuntimePolicy = producer.RuntimePolicy,
        }, names);
        try
        {
            SemanticVerifier.VerifyLibrary(program);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new ModuleFormatException("Producer IR failed semantic verification.", exception);
        }
    }

    /// <summary>Creates an independently stored producer from named functions in a verified compilation.</summary>
    public static byte[] CreateProducer(BoundProgram program, IEnumerable<string> exports)
    {
        ArgumentNullException.ThrowIfNull(exports);
        ArgumentNullException.ThrowIfNull(program);
        if (ContainsCallerUnsafeContracts(program))
        {
            throw new ModuleContractException("Caller-unsafe callable contracts are not supported in persisted semantic modules.");
        }
        if (program.Functions.Any(static function => function.IndexedBody is not null))
        {
            throw new ModuleFormatException("Indexed executable Bodies cannot be serialized as structured module IR.");
        }
        if (program.EntryPoint == -1)
        {
            SemanticVerifier.VerifyLibrary(program);
        }
        else
        {
            SemanticVerifier.Verify(program);
        }
        if (program.Classes.IsDefaultOrEmpty && program.Functions.Any(static function =>
                ManagedClassTypes.IsOptional(function.ReturnType)
                || function.Variables.Any(static variable => ManagedClassTypes.IsOptional(variable.Type))))
        {
            throw new ModuleFormatException("Optional managed nodes require a private source-class leaf.");
        }
        if (!program.Buffers.IsDefaultOrEmpty)
        {
            throw new ModuleFormatException("Host buffers are not serializable module dependencies.");
        }
        var names = new HashSet<string>(exports, StringComparer.Ordinal);
        if (names.Count == 0 || names.Contains("Main"))
        {
            throw new ModuleFormatException("A producer needs exported functions other than Main.");
        }
        var indexes = new HashSet<int>();
        for (var i = 0; i < program.Functions.Length; i++)
        {
            if (names.Contains(program.Functions[i].Name))
            {
                _ = indexes.Add(i);
            }
        }
        if (indexes.Count != names.Count)
        {
            throw new ModuleFormatException("An exported function is missing.");
        }
        if (indexes.Any(index => !Exportable(program.Functions[index], program.Structures))
            || (HasPrivateManagedState(program)
                && indexes.Any(index => !ManagedExportShape.Supported(program.Functions[index], program.Structures))))
        {
            throw new ModuleContractException("Producer exports must have public, supported native declarations.");
        }
        var exportedTypes = RequiredStructures(indexes.Select(index => Declaration(program.Functions[index])), program.Structures);
        foreach (var structure in program.Structures)
        {
            if (exportedTypes.Contains(structure.Name) && structure.CleanupMethod is string cleanup
                && !names.Contains(cleanup))
            {
                throw new ModuleContractException($"Exported unique type '{structure.Name}' requires its cleanup method to be exported.");
            }
        }
        if (HasPrivateManagedState(program))
        {
            if (program.Functions.Any(static function => function.IsExternal))
            {
                throw new ModuleContractException("Managed leaf implementations cannot import other providers.");
            }
            indexes.UnionWith(Enumerable.Range(0, program.Functions.Length));
        }
        var pending = new Queue<int>(indexes);
        var functionIndexes = program.Functions.Select((function, index) => (function.Name, index))
            .ToDictionary(static item => item.Name, static item => item.index, StringComparer.Ordinal);
        while (pending.Count != 0)
        {
            var index = pending.Dequeue();
            foreach (var callee in Calls(program.Functions[index].Body))
            {
                if (callee == program.EntryPoint)
                {
                    throw new ModuleContractException("Producer cannot depend on the consumer entry point.");
                }
                if (indexes.Add(callee))
                {
                    pending.Enqueue(callee);
                }
            }
            var types = RequiredStructures([program.Functions[index]], program.Structures);
            foreach (var structure in program.Structures)
            {
                if (types.Contains(structure.Name) && structure.CleanupMethod is string cleanup)
                {
                    if (!functionIndexes.TryGetValue(cleanup, out var callee))
                    {
                        throw new ModuleFormatException($"Missing cleanup declaration '{cleanup}'.");
                    }
                    if (indexes.Add(callee))
                    {
                        pending.Enqueue(callee);
                    }
                }
            }
        }
        var functions = program.Functions.Where((_, index) => indexes.Contains(index)).ToImmutableArray();
        var required = RequiredStructures(functions, program.Structures, program.Classes);
        var structures = SelectStructures(program.Structures, required);
        var remapped = functions.Select(function => RemapFunction(function, program.Structures, structures));
        var module = new Module(false, -1, structures, [.. remapped.Select(function =>
            new ModuleFunction(function.Name, Contract(function, structures), function, names.Contains(function.Name)) {
                HasOwnershipTransfer = Compilation.HasOwnershipTransfer(function, structures),
            })]) {
            Classes = [.. program.Classes.Select(@class => RemapClass(@class, program.Structures, structures))],
            RuntimePolicy = program.RuntimePolicy,
        };
        return Write(module, program);
    }

    /// <summary>Stores consumer bound bodies and imported declaration contracts, but never producer bodies.</summary>
    public static byte[] CreateConsumer(BoundProgram program, byte[] referenceProducer)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (ContainsCallerUnsafeContracts(program))
        {
            throw new ModuleContractException("Caller-unsafe callable contracts are not supported in persisted semantic modules.");
        }
        if (program.Functions.Any(static function => function.IndexedBody is not null))
        {
            throw new ModuleFormatException("Indexed executable Bodies cannot be serialized as structured module IR.");
        }
        ArgumentNullException.ThrowIfNull(referenceProducer);
        SemanticVerifier.VerifyReferences(program);
        var hasPrivateManagedState = HasPrivateManagedState(program);
        if (hasPrivateManagedState && !program.Functions.Any(static function => function.IsExternal))
        {
            throw new ModuleContractException("Managed consumers require separately compiled provider imports.");
        }
        if (!program.Buffers.IsDefaultOrEmpty)
        {
            throw new ModuleFormatException("Host buffers are not serializable module dependencies.");
        }
        var producer = Read(referenceProducer, expectedConsumer: false);
        ValidateProducer(producer);
        if (program.RuntimePolicy != producer.RuntimePolicy)
        {
            throw new ModuleContractException("Consumer and producer checked-failure policies differ.");
        }
        var exports = producer.Functions.Where(static function => function.Import)
            .ToDictionary(function => function.Name, StringComparer.Ordinal);
        var implementationFunctions = producer.Functions.Where(static function => !function.Import)
            .Select(function => function.Name).ToHashSet(StringComparer.Ordinal);
        var hasExternalDeclarations = program.Functions.Any(static function => function.IsExternal);
        var declarations = ImmutableArray.CreateBuilder<ModuleFunction>();
        for (var i = 0; i < program.Functions.Length; i++)
        {
            var function = program.Functions[i];
            if (function.Intrinsic == BoundIntrinsic.BufferClear
                && !program.Functions.Any(candidate => Calls(candidate.Body).Contains(i)))
            {
                continue;
            }
            if (!hasExternalDeclarations && implementationFunctions.Contains(function.Name))
            {
                continue;
            }
            if (exports.TryGetValue(function.Name, out var imported))
            {
                if (hasPrivateManagedState && (!ManagedExportShape.Supported(function, program.Structures)
                    || imported.Function is not BoundFunction implementation
                    || !ManagedExportShape.Supported(implementation, producer.Structures)))
                {
                    throw new ModuleContractException($"Managed consumer import '{function.Name}' crosses a non-Copy boundary.");
                }
                if (!Exportable(function, program.Structures))
                {
                    throw new ModuleContractException($"Declaration '{function.Name}' is not a public module import.");
                }
                if (!CompatibleContract(Contract(function, program.Structures), imported.Contract)
                    || Compilation.HasOwnershipTransfer(function, program.Structures) != imported.HasOwnershipTransfer)
                {
                    throw new ModuleContractException($"Reference declaration for '{function.Name}' differs from the producer.");
                }
                declarations.Add(new ModuleFunction(function.Name, imported.Contract, null, true) {
                    HasOwnershipTransfer = Compilation.HasOwnershipTransfer(function, program.Structures),
                });
            }
            else
            {
                if (function.IsExternal)
                {
                    throw new ModuleContractException($"External declaration '{function.Name}' is not provided by this producer.");
                }
                foreach (var callee in Calls(function.Body))
                {
                    if (!hasExternalDeclarations && implementationFunctions.Contains(program.Functions[callee].Name))
                    {
                        throw new ModuleContractException("Consumer cannot call an unexported producer implementation.");
                    }
                }
                declarations.Add(new ModuleFunction(function.Name, Contract(function, program.Structures), function, false) {
                    HasOwnershipTransfer = Compilation.HasOwnershipTransfer(function, program.Structures),
                });
            }
        }
        var entryPoint = -1;
        for (var i = 0; i < declarations.Count; i++)
        {
            if (declarations[i].Name == program.Functions[program.EntryPoint].Name)
            {
                entryPoint = i;
                break;
            }
        }
        if (entryPoint < 0 || declarations[entryPoint].Function is null)
        {
            throw new ModuleContractException("Main must be owned by the consumer.");
        }
        var retained = declarations.ToDictionary(static declaration => declaration.Name, StringComparer.Ordinal);
        var required = RequiredStructures(program.Functions.Where(function => retained.ContainsKey(function.Name))
            .Select(function => retained[function.Name].Import ? Declaration(function) : function), program.Structures);
        var structures = SelectStructures(program.Structures, required);
        var exportedTypes = RequiredStructures(producer.Functions.Where(static function => function.Import)
            .Select(static function => Declaration(function.Function ?? throw new ModuleFormatException("Missing export."))), producer.Structures);
        var producerTypes = producer.Structures.Where(structure => exportedTypes.Contains(structure.Name))
            .ToDictionary(static structure => structure.Name, StringComparer.Ordinal);
        var importedTypes = ImmutableArray.CreateBuilder<string>();
        foreach (var structure in structures)
        {
            if (producerTypes.TryGetValue(structure.Name, out var imported))
            {
                if (!SameStructure(structure, imported, structures, producer.Structures) || structure.IsPrivate || structure.IsInternal)
                {
                    throw new ModuleContractException($"Incompatible or inaccessible imported value layout '{structure.Name}'.");
                }
                importedTypes.Add(structure.Name);
            }
        }
        var remapped = declarations.Select(declaration => declaration.Function is BoundFunction function
            ? declaration with { Function = RemapFunction(function, program.Structures, structures) } : declaration).ToImmutableArray();
        return Write(new Module(true, entryPoint, structures, remapped) {
            Classes = [.. program.Classes.Select(@class => RemapClass(@class, program.Structures, structures))],
            ImportedStructures = importedTypes.ToImmutable(),
            RuntimePolicy = program.RuntimePolicy,
        }, program);
    }

    /// <summary>Resolves symbolic calls against a separately supplied producer, checks contracts and verifies the complete linked IR.</summary>
    public static BoundProgram Link(byte[] consumerBytes, byte[] producerBytes)
    {
        var consumer = Read(consumerBytes, expectedConsumer: true);
        var producer = Read(producerBytes, expectedConsumer: false);
        ValidateProducer(producer);
        if (consumer.RuntimePolicy != producer.RuntimePolicy)
        {
            throw new ModuleContractException("Consumer and producer checked-failure policies differ.");
        }
        ValidateConsumerAgainstReference(consumerBytes, CreateReference(producer));
        if (consumer.Classes.Length + producer.Classes.Length > SemanticVerifier.MaximumVariables)
        {
            throw new ModuleContractException("Combined private managed class layouts exceed the supported limit.");
        }
        var sharedTypes = consumer.ImportedStructures.ToHashSet(StringComparer.Ordinal);
        var producerTypes = producer.Structures.ToDictionary(static structure => structure.Name, StringComparer.Ordinal);
        foreach (var structure in consumer.Structures)
        {
            if (sharedTypes.Contains(structure.Name)
                && (!producerTypes.TryGetValue(structure.Name, out var actual)
                    || !SameStructure(structure, actual, consumer.Structures, producer.Structures) || actual.IsPrivate || actual.IsInternal))
            {
                throw new ModuleContractException($"Incompatible producer type layout '{structure.Name}'.");
            }
        }
        var structures = consumer.Structures.ToBuilder();
        var renamedTypes = producer.Structures.Select(structure => sharedTypes.Contains(structure.Name)
            ? structure : structure with {
                Name = "$producer." + structure.Name,
                CleanupMethod = structure.CleanupMethod is null ? null : "$producer." + structure.CleanupMethod,
            }).ToImmutableArray();
        structures.AddRange(renamedTypes.Where(structure => !sharedTypes.Contains(structure.Name)));
        var allStructures = structures.ToImmutable();
        for (var index = consumer.Structures.Length; index < allStructures.Length; index++)
        {
            structures[index] = RemapStructure(allStructures[index], renamedTypes, allStructures);
        }
        allStructures = structures.ToImmutable();
        var available = producer.Functions.Where(static function => function.Import)
            .ToDictionary(function => function.Name, StringComparer.Ordinal);
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        var producerNames = new Dictionary<string, int>(StringComparer.Ordinal);
        var functions = ImmutableArray.CreateBuilder<BoundFunction>();
        var consumerOwned = new List<int>();
        var producerOwned = new List<int>();
        foreach (var declaration in consumer.Functions)
        {
            if (!names.TryAdd(declaration.Name, functions.Count))
            {
                throw new ModuleFormatException("Duplicate consumer symbol.");
            }
            if (declaration.Import)
            {
                if (!available.TryGetValue(declaration.Name, out var replacement)
                    || !CompatibleContract(declaration.Contract, replacement.Contract)
                    || declaration.HasOwnershipTransfer != replacement.HasOwnershipTransfer)
                {
                    throw new ModuleContractException($"Incompatible or missing producer contract for '{declaration.Name}'.");
                }
                producerNames.Add(declaration.Name, functions.Count);
                producerOwned.Add(functions.Count);
                functions.Add(ImportFunction(replacement, declaration.Name));
            }
            else
            {
                if (declaration.Function?.DeclaringStruct is string owner && sharedTypes.Contains(owner))
                {
                    throw new ModuleContractException("Consumer cannot define members of an imported value type.");
                }
                consumerOwned.Add(functions.Count);
                functions.Add(declaration.Function ?? throw new ModuleFormatException("Consumer has an unowned function."));
            }
        }
        foreach (var exported in producer.Functions)
        {
            if (exported.Function is { Intrinsic: not (BoundIntrinsic.None or BoundIntrinsic.ClassTryCreate) }
                && names.TryGetValue(exported.Name, out var intrinsicIndex))
            {
                producerNames.Add(exported.Name, intrinsicIndex);
                continue;
            }
            if (!producerNames.ContainsKey(exported.Name))
            {
                producerNames.Add(exported.Name, functions.Count);
                producerOwned.Add(functions.Count);
                var owner = exported.Function?.DeclaringStruct;
                var name = exported.Function is { Intrinsic: not (BoundIntrinsic.None or BoundIntrinsic.ClassTryCreate) }
                    || owner is not null && Binder.TryPrimitiveType(owner, out _)
                        && !producer.Structures.Any(structure => structure.Name == owner)
                    ? exported.Name
                    : owner is not null && sharedTypes.Contains(owner)
                    ? owner + ".$producer." + exported.Name[(owner.Length + 1)..] : "$producer." + exported.Name;
                functions.Add(ImportFunction(exported, name));
            }
        }
        foreach (var owner in consumerOwned)
        {
            functions[owner] = functions[owner] with { Body = (BoundBlock)ResolveStatement(functions[owner].Body, names) };
        }
        foreach (var owner in producerOwned)
        {
            var function = functions[owner];
            if (function.ClassConstructor >= 0)
            {
                var constructor = producer.Functions[function.ClassConstructor].Name;
                function = function with { ClassConstructor = producerNames[constructor] };
            }
            functions[owner] = function with { Body = (BoundBlock)ResolveStatement(function.Body, producerNames) };
        }
        var result = new BoundProgram(functions.ToImmutable(), consumer.EntryPoint) {
            Structures = allStructures,
            Classes = [.. consumer.Classes, .. producer.Classes.Select(@class => RemapClass(
                @class with { Name = "$producer." + @class.Name }, renamedTypes, allStructures,
                consumer.Classes.Length))],
            RuntimePolicy = consumer.RuntimePolicy,
        };
        try
        {
            SemanticVerifier.Verify(result);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new ModuleFormatException("Linked module IR failed semantic verification.", exception);
        }
        return result;

        BoundFunction ImportFunction(ModuleFunction declaration, string name)
        {
            var function = declaration.Function ?? throw new ModuleFormatException("Producer has an unowned function.");
            var owner = function.DeclaringStruct;
            if (owner is not null && owner != NativeBufferIntrinsics.TypeName
                && owner != NativeManagedNodeIntrinsics.TypeName
                && owner != NativeManagedNodeIntrinsics.HeapName
                && !(Binder.TryPrimitiveType(owner, out _)
                    && !producer.Structures.Any(structure => structure.Name == owner))
                && !sharedTypes.Contains(owner))
            {
                owner = "$producer." + owner;
            }
            return RemapFunction(function with { Name = name, DeclaringStruct = owner }, renamedTypes,
                allStructures, consumer.Classes.Length);
        }
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Only nodes with nested expressions or statements need traversal.")]
    private static List<int> Calls(BoundStatement body, Action<PrimitiveType>? visitType = null,
        Action<BoundViewScope>? visitView = null, Action? visitLoopTransfer = null,
        Action<BoundMutableFieldLoan>? visitMutableFieldLoan = null,
        Action<BoundStatement>? visitStructuredLoop = null, Action<BoundVacantLocal>? visitVacantLocal = null,
        Action<BoundConditional>? visitConditional = null, Action? visitCoalesce = null,
        Action? visitUpdate = null, Action? visitNominalUpdate = null,
        Action? visitCompoundAssignment = null, Action<BoundAssignment>? visitAssignment = null,
        Action? visitLifetimeEnd = null)
    {
        var calls = new List<int>();
        VisitStatement(body);
        return calls;

        void VisitStatement(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundBlock block:
                    foreach (var child in block.Statements)
                    {
                        VisitStatement(child);
                    }
                    break;
                case BoundLocal local:
                    VisitExpression(local.Initializer);
                    break;
                case BoundVacantLocal vacant:
                    visitVacantLocal?.Invoke(vacant);
                    break;
                case BoundAssignment assignment:
                    visitAssignment?.Invoke(assignment);
                    VisitExpression(assignment.Value);
                    break;
                case BoundLifetimeEnd end:
                    visitLifetimeEnd?.Invoke();
                    if (end.CleanupFunction >= 0)
                    {
                        calls.Add(end.CleanupFunction);
                    }
                    break;
                case SymbolicLifetimeEnd:
                    visitLifetimeEnd?.Invoke();
                    break;
                case BoundOptionalAssignment optionalAssignment:
                    visitCoalesce?.Invoke();
                    VisitExpression(optionalAssignment.Place);
                    VisitExpression(optionalAssignment.Value);
                    break;
                case BoundFieldAssignment field:
                    VisitExpression(field.Value);
                    break;
                case BoundExpressionStatement expression:
                    VisitExpression(expression.Expression);
                    break;
                case BoundIf conditional:
                    VisitExpression(conditional.Condition);
                    VisitStatement(conditional.Then);
                    if (conditional.Else is not null)
                    {
                        VisitStatement(conditional.Else);
                    }
                    break;
                case BoundWhile loop:
                    if (loop.IsPostTested)
                    {
                        visitStructuredLoop?.Invoke(loop);
                    }
                    VisitExpression(loop.Condition);
                    VisitStatement(loop.Body);
                    if (loop.Iterator is not null)
                    {
                        visitLoopTransfer?.Invoke();
                        VisitStatement(loop.Iterator);
                    }
                    break;
                case BoundBreak or BoundContinue:
                    visitLoopTransfer?.Invoke();
                    if ((statement is BoundBreak exit && exit.TargetDepth != 1)
                        || (statement is BoundContinue next && next.TargetDepth != 1))
                    {
                        visitStructuredLoop?.Invoke(statement);
                    }
                    break;
                case BoundTryFinally cleanup:
                    VisitStatement(cleanup.Body);
                    VisitStatement(cleanup.Finally);
                    break;
                case BoundViewScope view:
                    visitView?.Invoke(view);
                    VisitExpression(view.Owner);
                    VisitExpression(view.Start);
                    VisitExpression(view.Length);
                    VisitStatement(view.Body);
                    break;
                case BoundViewSet write:
                    VisitExpression(write.Index);
                    VisitExpression(write.Value);
                    break;
                case BoundDrop drop when drop.CleanupFunction >= 0:
                    calls.Add(drop.CleanupFunction);
                    break;
                case BoundDropConstructedFields:
                    break;
                case BoundUnsafe unsafeBlock:
                    VisitStatement(unsafeBlock.Body);
                    break;
                case BoundTryCatch recovery:
                    VisitStatement(recovery.Body);
                    VisitStatement(recovery.Catch);
                    if (recovery.Finally is not null)
                    {
                        VisitStatement(recovery.Finally);
                    }
                    break;
                case BoundRequire requirement:
                    VisitExpression(requirement.Condition);
                    break;
                case BoundReturn { Value: BoundExpression value }:
                    VisitExpression(value);
                    break;
            }
        }

        void VisitExpression(BoundExpression expression)
        {
            visitType?.Invoke(expression.Type);
            switch (expression)
            {
                case BoundCall call:
                    calls.Add(call.Function);
                    foreach (var argument in call.Arguments)
                    {
                        VisitExpression(argument);
                    }
                    break;
                case SymbolicCall symbolic:
                    foreach (var argument in symbolic.Arguments)
                    {
                        VisitExpression(argument);
                    }
                    break;
                case BoundUnary unary:
                    VisitExpression(unary.Operand);
                    break;
                case BoundUpdate update:
                    visitUpdate?.Invoke();
                    VisitExpression(update.Place);
                    break;
                case BoundCompoundAssignment compound:
                    visitCompoundAssignment?.Invoke();
                    VisitExpression(compound.Place);
                    VisitExpression(compound.Right);
                    break;
                case BoundNominalUpdate update:
                    visitNominalUpdate?.Invoke();
                    calls.Add(update.Function);
                    VisitExpression(update.Place);
                    break;
                case SymbolicNominalUpdate update:
                    visitNominalUpdate?.Invoke();
                    VisitExpression(update.Place);
                    break;
                case BoundBinary binary:
                    if (binary.Operator is TokenKind.ShiftLeft or TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned)
                    {
                        visitCompoundAssignment?.Invoke();
                    }
                    VisitExpression(binary.Left);
                    VisitExpression(binary.Right);
                    break;
                case BoundConditional conditional:
                    visitConditional?.Invoke(conditional);
                    VisitExpression(conditional.Condition);
                    VisitExpression(conditional.WhenTrue);
                    VisitExpression(conditional.WhenFalse);
                    break;
                case BoundCoalesce coalesce:
                    visitCoalesce?.Invoke();
                    VisitExpression(coalesce.Optional);
                    VisitExpression(coalesce.Fallback);
                    break;
                case BoundCast cast:
                    VisitExpression(cast.Operand);
                    break;
                case BoundOptionalLift lift:
                    VisitExpression(lift.Value);
                    break;
                case BoundReadonlyProjection projection:
                    VisitExpression(projection.Value);
                    break;
                case BoundIsNull test:
                    VisitExpression(test.Value);
                    break;
                case BoundIsSome pattern:
                    VisitExpression(pattern.Value);
                    break;
                case BoundIsVariablePattern pattern:
                    VisitExpression(pattern.Value);
                    break;
                case BoundFieldRead field:
                    VisitExpression(field.Receiver);
                    break;
                case BoundFieldLoan loan:
                    VisitExpression(loan.Place);
                    break;
                case BoundMutableFieldLoan loan:
                    visitMutableFieldLoan?.Invoke(loan);
                    VisitExpression(loan.Place);
                    break;
                case BoundViewRead view:
                    VisitExpression(view.Index);
                    break;
                case BoundBufferRead buffer:
                    VisitExpression(buffer.Index);
                    break;
            }
        }
    }

    internal static bool ContainsCallerUnsafeCalls(BoundProgram program) =>
        program.Functions.Any(function => Calls(function.Body).Any(index =>
            index >= 0 && index < program.Functions.Length
            && program.Functions[index].RequiresUnsafeCaller));

    internal static bool ContainsCallerUnsafeContracts(BoundProgram program) =>
        program.Functions.Any(static function => function.RequiresUnsafeCaller)
        || program.Structures.Any(static structure =>
            structure.Fields.Any(static field => field.RequiresUnsafeCaller))
        || program.Classes.Any(static @class => @class.Fields.Any(static field => field.RequiresUnsafeCaller))
        || ContainsCallerUnsafeCalls(program);

    private static bool HasLoopTransfer(ImmutableArray<ModuleFunction> functions)
    {
        var found = false;
        foreach (var item in functions)
        {
            if (item.Function is BoundFunction function)
            {
                _ = Calls(function.Body, visitLoopTransfer: () => found = true);
            }
        }
        return found;
    }

    private static bool HasStructuredLoops(BoundStatement body)
    {
        var found = false;
        _ = Calls(body, visitStructuredLoop: _ => found = true);
        return found;
    }

    private static bool HasVacantLocal(BoundStatement body)
    {
        var found = false;
        _ = Calls(body, visitVacantLocal: _ => found = true);
        return found;
    }

    private static bool HasInitializationAssignment(BoundFunction function)
    {
        var found = false;
        _ = Calls(function.Body, visitAssignment: assignment => found |=
            assignment.Variable >= 0 && assignment.Variable < function.Variables.Length
                && function.Variables[assignment.Variable].IsInitializationLocal);
        return found;
    }

    private static bool HasLifetimeEnd(BoundStatement body)
    {
        var found = false;
        _ = Calls(body, visitLifetimeEnd: () => found = true);
        return found;
    }

    private static bool HasConditional(BoundStatement body)
    {
        var found = false;
        _ = Calls(body, visitConditional: _ => found = true);
        return found;
    }

    private static bool HasUpdate(BoundStatement body)
    {
        var found = false;
        _ = Calls(body, visitUpdate: () => found = true);
        return found;
    }

    private static bool HasNominalUpdate(BoundStatement body)
    {
        var found = false;
        _ = Calls(body, visitNominalUpdate: () => found = true);
        return found;
    }

    private static bool HasCompoundAssignment(BoundStatement body)
    {
        var found = false;
        _ = Calls(body, visitCompoundAssignment: () => found = true);
        return found;
    }

    private static bool HasCoalescing(BoundStatement body)
    {
        var found = false;
        _ = Calls(body, visitType: null, visitCoalesce: () => found = true);
        return found;
    }

    private static bool HasMutableStructReceiver(BoundFunction function) =>
        function.DeclaringStruct is not null && !function.IsStatic && !function.IsConstructor
        && !function.IsClassConstructor && !function.IsCleanup && !function.Variables.IsEmpty
        && function.Variables[0].IsMutable
        && (int)function.Variables[0].Type is >= 100 and < ManagedClassTypes.First;

    private static bool HasUniqueMutableReceiver(BoundFunction function,
        ImmutableArray<BoundStruct> structures)
    {
        var index = function.Variables.IsDefaultOrEmpty ? -1 : (int)function.Variables[0].Type - 100;
        return HasMutableStructReceiver(function) && index >= 0 && index < structures.Length
            && structures[index].IsUnique;
    }

    private static bool HasMutableFieldLoan(BoundFunction function)
    {
        var found = false;
        _ = Calls(function.Body, visitMutableFieldLoan: _ => found = true);
        return found;
    }

    private static bool HasMutableBufferFieldLoan(BoundFunction function)
    {
        var found = false;
        _ = Calls(function.Body, visitMutableFieldLoan: loan =>
            found |= loan.Type == PrimitiveType.OwnedInt32Buffer);
        return found;
    }

    private static bool HasMutableBufferFieldView(BoundFunction function)
    {
        var found = false;
        _ = Calls(function.Body, visitView: view =>
            found |= view.IsMutable && view.Owner is BoundMutableFieldLoan);
        return found;
    }

    private static bool HasUniqueMutableFieldLoan(BoundFunction function, ImmutableArray<BoundStruct> structures)
    {
        var found = false;
        _ = Calls(function.Body, visitMutableFieldLoan: loan => {
            BoundExpression place = loan.Place;
            while (place is BoundFieldRead field)
            {
                place = field.Receiver;
            }
            if (place is BoundRead root)
            {
                var index = (int)root.Type - 100;
                found |= index >= 0 && index < structures.Length && structures[index].IsUnique;
            }
        });
        return found;
    }

    private static string Contract(BoundFunction function, ImmutableArray<BoundStruct> structures)
    {
        var result = new StringBuilder();
        var ownerVisibility = function.DeclaringStruct is string owner
            ? Binder.TryPrimitiveType(owner, out _)
                && !structures.Any(structure => structure.Name == owner) ? 'E'
                : structures.Where(structure => structure.Name == owner)
                    .Select(static structure => Visibility(structure)).FirstOrDefault('?')
            : '-';
        _ = result.Append(function.Name).Append('|').Append(ContractType(function.ReturnType, structures))
            .Append('|').Append(function.IsConstructor ? 'C' : 'F')
            .Append('|').Append(function.IsPrivate ? 'P' : function.IsInternal ? 'N' : 'E')
            .Append('|').Append(function.IsStatic ? 'S' : 'I')
            .Append('|').Append(function.DeclaringStruct ?? "-")
            .Append('|').Append(ownerVisibility)
            .Append('|').Append(function.ParameterCount);
        for (var i = 0; i < function.ParameterCount; i++)
        {
            var parameter = function.Variables[i + (function.IsConstructor ? 1 : 0)];
            // Private v33 headers recorded init as out; their explicit IsInit bit still checks the body.
            var mode = parameter.IsInit && !function.IsPrivate && !function.IsInternal ? "init:"
                : parameter.IsOut ? "out:" : "in:";
            _ = result.Append('|').Append(mode)
                .Append(ContractType(parameter.Type, structures));
        }
        if (HasMutableStructReceiver(function))
        {
            _ = result.Append("|MutReceiver");
        }
        if (function.PropertyName is string property)
        {
            _ = result.Append("|Property:").Append(property);
        }
        if (function.IsCleanup)
        {
            _ = result.Append("|Cleanup");
        }
        _ = result.Append(function.IsNoThrow ? "|NoThrow" : "|MayThrow")
            .Append(function.DoesNotReturn ? "|DoesNotReturn" : "|MayReturn");
        return result.ToString();
    }

    private static bool CompatibleContract(string expected, string actual) =>
        TryContractPromises(expected.AsSpan(), out var expectedSignature, out var expectedNoThrow, out var expectedNoReturn)
        && TryContractPromises(actual.AsSpan(), out var actualSignature, out var actualNoThrow, out var actualNoReturn)
        && expectedSignature.SequenceEqual(actualSignature)
        && (!expectedNoThrow || actualNoThrow)
        && (!expectedNoReturn || actualNoReturn);

    private static bool TryContractPromises(ReadOnlySpan<char> contract, out ReadOnlySpan<char> signature,
        out bool noThrow, out bool noReturn)
    {
        signature = default;
        noThrow = false;
        noReturn = false;
        if (contract.EndsWith("|DoesNotReturn".AsSpan(), StringComparison.Ordinal))
        {
            noReturn = true;
            contract = contract[..^"|DoesNotReturn".Length];
        }
        else if (contract.EndsWith("|MayReturn".AsSpan(), StringComparison.Ordinal))
        {
            contract = contract[..^"|MayReturn".Length];
        }
        else
        {
            return false;
        }
        if (contract.EndsWith("|NoThrow".AsSpan(), StringComparison.Ordinal))
        {
            noThrow = true;
            signature = contract[..^"|NoThrow".Length];
        }
        else if (contract.EndsWith("|MayThrow".AsSpan(), StringComparison.Ordinal))
        {
            signature = contract[..^"|MayThrow".Length];
        }
        return !signature.IsEmpty;
    }

    private static char Visibility(BoundStruct structure) =>
        structure.IsPrivate ? 'P' : structure.IsInternal ? 'N' : 'E';

    private static string ContractType(PrimitiveType type, ImmutableArray<BoundStruct> structures)
    {
        var index = (int)type - 100;
        return index >= 0 && index < structures.Length
            ? structures[index].Name + ":" + Visibility(structures[index])
                + (structures[index].IsUnique ? ":Unique:" + (structures[index].CleanupMethod ?? "-") : "")
            : TypeName(type, structures);
    }

    private static string TypeName(PrimitiveType type, ImmutableArray<BoundStruct> structures)
    {
        var index = (int)type - 100;
        return index >= 0 && index < structures.Length
            ? structures[index].Name : ((int)type).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool Exportable(BoundFunction function, ImmutableArray<BoundStruct> structures)
    {
        var parameterOffset = function.IsConstructor ? 1 : 0;
        if (function.IsPrivate || function.IsInternal || HasMutableStructReceiver(function)
            || function.ParameterCount < 0
            || function.ParameterCount > function.Variables.Length - parameterOffset)
        {
            return false;
        }
        var visited = new HashSet<(int Index, bool PublicFields)>();
        if (function.DeclaringStruct is string declaring
            && !(Binder.TryPrimitiveType(declaring, out _)
                && !structures.Any(structure => structure.Name == declaring))
            && !structures.Select((structure, index) => (structure, index))
                .Any(item => item.structure.Name == declaring && PublicLayout((PrimitiveType)(100 + item.index), false)))
        {
            return false;
        }
        bool PublicLayout(PrimitiveType type, bool publicFields)
        {
            var index = (int)type - 100;
            if (index < 0)
            {
                return type is not (PrimitiveType.ReadonlyInt32BufferView or PrimitiveType.MutableInt32BufferView
                    or PrimitiveType.ManagedNode);
            }
            if (index >= structures.Length)
            {
                return false;
            }
            if (!visited.Add((index, publicFields)))
            {
                return true;
            }
            var structure = structures[index];
            return !structure.IsPrivate && !structure.IsInternal
                && !structure.Fields.IsDefault
                && structure.Fields.All(field => (!publicFields || (!field.IsPrivate && !field.IsInternal))
                    && PublicLayout(field.Type, publicFields));
        }
        if (!PublicLayout(function.ReturnType, true))
        {
            return false;
        }
        for (var index = 0; index < function.ParameterCount; index++)
        {
            if (!PublicLayout(function.Variables[index + parameterOffset].Type, true))
            {
                return false;
            }
        }
        return true;
    }

    private static bool SameStructure(BoundStruct a, BoundStruct b,
        ImmutableArray<BoundStruct> left, ImmutableArray<BoundStruct> right)
    {
        if (a.Name != b.Name || a.Size != b.Size || a.Fields.Length != b.Fields.Length
            || a.IsPrivate != b.IsPrivate || a.IsInternal != b.IsInternal
            || a.IsUnique != b.IsUnique || a.CleanupMethod != b.CleanupMethod
            || a.IsTransparent != b.IsTransparent)
        {
            return false;
        }
        for (var field = 0; field < a.Fields.Length; field++)
        {
            var x = a.Fields[field];
            var y = b.Fields[field];
            if (x.Name != y.Name || x.IsPrivate != y.IsPrivate || x.IsInternal != y.IsInternal
                || x.IsMutable != y.IsMutable
                || TypeName(x.Type, left) != TypeName(y.Type, right)
                || x.Size != y.Size || x.Offset != y.Offset)
            {
                return false;
            }
        }
        return true;
    }

    private static PrimitiveType RemapType(PrimitiveType type, ImmutableArray<BoundStruct> source,
        ImmutableArray<BoundStruct> destination, int classOffset = 0)
    {
        if (classOffset != 0 && (int)type >= ManagedClassTypes.First && ManagedClassTypes.IsManaged(type))
        {
            return (PrimitiveType)((int)type + classOffset);
        }
        var index = (int)type - 100;
        if (index < 0 || ManagedClassTypes.IsManaged(type))
        {
            return type;
        }
        if (index >= source.Length)
        {
            throw new ModuleFormatException("Unknown producer type reference.");
        }
        for (var i = 0; i < destination.Length; i++)
        {
            if (destination[i].Name == source[index].Name)
            {
                return (PrimitiveType)(100 + i);
            }
        }
        throw new ModuleContractException($"Missing type '{source[index].Name}'.");
    }

    private static BoundStruct RemapStructure(BoundStruct structure, ImmutableArray<BoundStruct> source,
        ImmutableArray<BoundStruct> destination) => structure with {
            Fields = [.. structure.Fields.Select(field => field with { Type = RemapType(field.Type, source, destination) })],
        };

    private static BoundClass RemapClass(BoundClass @class, ImmutableArray<BoundStruct> source,
        ImmutableArray<BoundStruct> destination, int classOffset = 0) => @class with {
            Fields = [.. @class.Fields.Select(field => field with {
                Type = RemapType(field.Type, source, destination, classOffset),
            })],
        };

    private static ImmutableArray<BoundStruct> SelectStructures(ImmutableArray<BoundStruct> source, HashSet<string> required)
    {
        var selected = source.Where(structure => required.Contains(structure.Name)).ToImmutableArray();
        return [.. selected.Select(structure => RemapStructure(structure, source, selected))];
    }

    private static BoundFunction RemapFunction(BoundFunction function, ImmutableArray<BoundStruct> source,
        ImmutableArray<BoundStruct> destination, int classOffset = 0)
    {
        return function with {
            ReturnType = RemapType(function.ReturnType, source, destination, classOffset),
            Variables = [.. function.Variables.Select(variable => variable with {
                Type = RemapType(variable.Type, source, destination, classOffset),
            })],
            Body = (BoundBlock)RemapStatement(function.Body, source, destination, classOffset),
        };
    }

    private static BoundExpression RemapExpression(BoundExpression expression, ImmutableArray<BoundStruct> source,
        ImmutableArray<BoundStruct> destination, int classOffset = 0)
    {
        return expression switch {
            BoundConstant constant => constant with { Type = RemapType(constant.Type, source, destination, classOffset) },
            BoundRead read => read with { Type = RemapType(read.Type, source, destination, classOffset) },
            BoundMove move => move with { Type = RemapType(move.Type, source, destination, classOffset) },
            BoundReceiverLoan loan => loan with { Type = RemapType(loan.Type, source, destination, classOffset) },
            BoundFieldLoan loan => loan with {
                Type = RemapType(loan.Type, source, destination, classOffset),
                Place = (BoundFieldRead)RemapExpression(loan.Place, source, destination, classOffset),
            },
            BoundMutableFieldLoan loan => loan with {
                Type = RemapType(loan.Type, source, destination, classOffset),
                Place = (BoundFieldRead)RemapExpression(loan.Place, source, destination, classOffset),
            },
            BoundMutableReceiverLoan loan => loan with { Type = RemapType(loan.Type, source, destination, classOffset) },
            BoundViewLength => expression,
            BoundViewRead view => view with { Index = RemapExpression(view.Index, source, destination, classOffset) },
            BoundUnary unary => unary with {
                Type = RemapType(unary.Type, source, destination, classOffset),
                Operand = RemapExpression(unary.Operand, source, destination, classOffset),
            },
            BoundUpdate update => update with {
                Type = RemapType(update.Type, source, destination, classOffset),
                Place = RemapExpression(update.Place, source, destination, classOffset),
            },
            BoundCompoundAssignment compound => compound with {
                Type = RemapType(compound.Type, source, destination, classOffset),
                Place = RemapExpression(compound.Place, source, destination, classOffset),
                Right = RemapExpression(compound.Right, source, destination, classOffset),
            },
            BoundNominalUpdate update => update with {
                Type = RemapType(update.Type, source, destination, classOffset),
                Place = RemapExpression(update.Place, source, destination, classOffset),
            },
            SymbolicNominalUpdate update => update with {
                Type = RemapType(update.Type, source, destination, classOffset),
                Place = RemapExpression(update.Place, source, destination, classOffset),
            },
            BoundBinary binary => binary with {
                Type = RemapType(binary.Type, source, destination, classOffset),
                Left = RemapExpression(binary.Left, source, destination, classOffset),
                Right = RemapExpression(binary.Right, source, destination, classOffset),
            },
            BoundConditional conditional => conditional with {
                Type = RemapType(conditional.Type, source, destination, classOffset),
                Condition = RemapExpression(conditional.Condition, source, destination, classOffset),
                WhenTrue = RemapExpression(conditional.WhenTrue, source, destination, classOffset),
                WhenFalse = RemapExpression(conditional.WhenFalse, source, destination, classOffset),
            },
            BoundCoalesce coalesce => coalesce with {
                Type = RemapType(coalesce.Type, source, destination, classOffset),
                Optional = RemapExpression(coalesce.Optional, source, destination, classOffset),
                Fallback = RemapExpression(coalesce.Fallback, source, destination, classOffset),
            },
            BoundCast cast => cast with {
                Type = RemapType(cast.Type, source, destination, classOffset),
                Operand = RemapExpression(cast.Operand, source, destination, classOffset),
            },
            BoundNull nullValue => nullValue with { Type = RemapType(nullValue.Type, source, destination, classOffset) },
            BoundOptionalLift lift => lift with {
                Type = RemapType(lift.Type, source, destination, classOffset),
                Value = RemapExpression(lift.Value, source, destination, classOffset),
            },
            BoundReadonlyProjection projection => projection with {
                Type = RemapType(projection.Type, source, destination, classOffset),
                Value = RemapExpression(projection.Value, source, destination, classOffset),
            },
            BoundIsNull test => test with { Value = RemapExpression(test.Value, source, destination, classOffset) },
            BoundIsSome pattern => pattern with { Value = RemapExpression(pattern.Value, source, destination, classOffset) },
            BoundIsVariablePattern pattern => pattern with {
                Value = RemapExpression(pattern.Value, source, destination, classOffset),
            },
            BoundIsInitialized => expression,
            BoundFieldRead field => field with {
                Type = RemapType(field.Type, source, destination, classOffset),
                Receiver = RemapExpression(field.Receiver, source, destination, classOffset),
            },
            BoundOutArgument output => output with { Type = RemapType(output.Type, source, destination, classOffset) },
            BoundCall call => call with {
                Type = RemapType(call.Type, source, destination, classOffset),
                Arguments = RemapExpressions(call.Arguments, source, destination, classOffset),
            },
            SymbolicCall call => call with {
                Type = RemapType(call.Type, source, destination, classOffset),
                Arguments = RemapExpressions(call.Arguments, source, destination, classOffset),
            },
            _ => throw new ModuleFormatException("Unknown producer expression kind."),
        };
    }

    private static ImmutableArray<BoundExpression> RemapExpressions(ImmutableArray<BoundExpression> expressions,
        ImmutableArray<BoundStruct> source, ImmutableArray<BoundStruct> destination, int classOffset = 0)
    {
        var result = ImmutableArray.CreateBuilder<BoundExpression>(expressions.Length);
        foreach (var expression in expressions)
        {
            result.Add(RemapExpression(expression, source, destination, classOffset));
        }
        return result.MoveToImmutable();
    }

    private static BoundStatement RemapStatement(BoundStatement statement, ImmutableArray<BoundStruct> source,
        ImmutableArray<BoundStruct> destination, int classOffset = 0)
    {
        return statement switch {
            BoundBlock block => block with { Statements = RemapStatements(block.Statements, source, destination, classOffset) },
            BoundLocal local => local with { Initializer = RemapExpression(local.Initializer, source, destination, classOffset) },
            BoundLifetimeEnd or SymbolicLifetimeEnd or BoundOwnerDeclaration or BoundVacantLocal => statement,
            BoundAssignment assignment => assignment with { Value = RemapExpression(assignment.Value, source, destination, classOffset) },
            BoundOptionalAssignment optionalAssignment => optionalAssignment with {
                Place = RemapExpression(optionalAssignment.Place, source, destination, classOffset),
                Value = RemapExpression(optionalAssignment.Value, source, destination, classOffset),
            },
            BoundFieldAssignment field => field with { Value = RemapExpression(field.Value, source, destination, classOffset) },
            BoundExpressionStatement expression => expression with {
                Expression = RemapExpression(expression.Expression, source, destination, classOffset),
            },
            BoundIf conditional => conditional with {
                Condition = RemapExpression(conditional.Condition, source, destination, classOffset),
                Then = RemapStatement(conditional.Then, source, destination, classOffset),
                Else = conditional.Else is null ? null : RemapStatement(conditional.Else, source, destination, classOffset),
            },
            BoundWhile loop => loop with {
                Condition = RemapExpression(loop.Condition, source, destination, classOffset),
                Body = RemapStatement(loop.Body, source, destination, classOffset),
                Iterator = loop.Iterator is null ? null : RemapStatement(loop.Iterator, source, destination, classOffset),
            },
            BoundBreak or BoundContinue => statement,
            BoundTryFinally cleanup => cleanup with {
                Body = (BoundBlock)RemapStatement(cleanup.Body, source, destination, classOffset),
                Finally = (BoundBlock)RemapStatement(cleanup.Finally, source, destination, classOffset),
            },
            BoundViewScope view => view with {
                Owner = RemapExpression(view.Owner, source, destination, classOffset),
                Start = RemapExpression(view.Start, source, destination, classOffset),
                Length = RemapExpression(view.Length, source, destination, classOffset),
                Body = (BoundBlock)RemapStatement(view.Body, source, destination, classOffset),
            },
            BoundViewSet write => write with {
                Index = RemapExpression(write.Index, source, destination, classOffset),
                Value = RemapExpression(write.Value, source, destination, classOffset),
            },
            BoundDrop => statement,
            BoundDropConstructedFields => statement,
            SymbolicDrop => statement,
            BoundUnsafe unsafeBlock => unsafeBlock with {
                Body = (BoundBlock)RemapStatement(unsafeBlock.Body, source, destination, classOffset),
            },
            BoundTryCatch recovery => recovery with {
                Body = (BoundBlock)RemapStatement(recovery.Body, source, destination, classOffset),
                Catch = (BoundBlock)RemapStatement(recovery.Catch, source, destination, classOffset),
                Finally = recovery.Finally is null ? null
                    : (BoundBlock)RemapStatement(recovery.Finally, source, destination, classOffset),
            },
            BoundRequire requirement => requirement with {
                Condition = RemapExpression(requirement.Condition, source, destination, classOffset),
            },
            BoundReturn result => result with {
                Value = result.Value is null ? null : RemapExpression(result.Value, source, destination, classOffset),
            },
            _ => throw new ModuleFormatException("Unknown producer statement kind."),
        };
    }

    private static ImmutableArray<BoundStatement> RemapStatements(ImmutableArray<BoundStatement> statements,
        ImmutableArray<BoundStruct> source, ImmutableArray<BoundStruct> destination, int classOffset = 0)
    {
        var result = ImmutableArray.CreateBuilder<BoundStatement>(statements.Length);
        foreach (var statement in statements)
        {
            result.Add(RemapStatement(statement, source, destination, classOffset));
        }
        return result.MoveToImmutable();
    }

    private static byte[] Write(Module module, BoundProgram program)
    {
        if (program.Functions.Any(static function => function.IndexedBody is not null))
        {
            throw new ModuleFormatException("Indexed executable Bodies cannot be serialized as structured module IR.");
        }
        if (module.Consumer && !HasPrivateManagedState(module))
        {
            RejectManagedNodes(program);
        }
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        PutString(writer, RetainedNativeProfile(module));
        writer.Write(module.Consumer);
        writer.Write((byte)program.RuntimePolicy);
        writer.Write(module.EntryPoint);
        writer.Write(module.Structures.Length);
        foreach (var structure in module.Structures)
        {
            PutString(writer, structure.Name);
            writer.Write(structure.Size);
            writer.Write(structure.IsPrivate);
            writer.Write(structure.IsInternal);
            writer.Write(structure.IsUnique);
            writer.Write(structure.IsTransparent);
            writer.Write(structure.CleanupMethod is not null);
            if (structure.CleanupMethod is string cleanup)
            {
                PutString(writer, cleanup);
            }
            writer.Write(structure.Fields.Length);
            foreach (var field in structure.Fields)
            {
                PutString(writer, field.Name);
                writer.Write((int)field.Type);
                writer.Write(field.Offset);
                writer.Write(field.Size);
                writer.Write(field.IsPrivate);
                writer.Write(field.IsInternal);
                writer.Write(field.IsMutable);
            }
        }
        writer.Write(module.Classes.Length);
        foreach (var @class in module.Classes)
        {
            PutString(writer, @class.Name);
            writer.Write(@class.Size);
            writer.Write(@class.IsInternal);
            writer.Write(@class.IsMutable);
            writer.Write(@class.Fields.Length);
            foreach (var field in @class.Fields)
            {
                PutString(writer, field.Name);
                writer.Write((int)field.Type);
                writer.Write(field.Offset);
                writer.Write(field.Size);
                writer.Write(field.IsPrivate);
                writer.Write(field.IsInternal);
                writer.Write(field.IsMutable);
            }
        }
        writer.Write(module.ImportedStructures.Length);
        foreach (var imported in module.ImportedStructures)
        {
            PutString(writer, imported);
        }
        writer.Write(module.Functions.Length);
        var names = program.Functions.Select(function => function.Name).ToArray();
        var remaining = MaximumNodes;
        foreach (var item in module.Functions)
        {
            PutString(writer, item.Name);
            PutString(writer, item.Contract);
            writer.Write(item.Import);
            writer.Write(item.Function is not null);
            writer.Write(item.HasOwnershipTransfer);
            if (item.Function is BoundFunction function)
            {
                PutFunction(writer, function, names, ref remaining);
            }
        }
        if (stream.Length > MaximumBytes)
        {
            throw new ModuleFormatException("Module exceeds the artifact size limit.");
        }
        return stream.ToArray();
    }

    [SuppressMessage("Style", "IDE0078:Use pattern matching", Justification = "Both header reads must execute in order; matching must not reorder or combine reads.")]
    private static Module Read(byte[] bytes, bool expectedConsumer)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is > MaximumBytes or < 17)
        {
            throw new ModuleFormatException("Module length is invalid.");
        }
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream, new UTF8Encoding(false, true), leaveOpen: true);
            var (policy, profile) = ReadModuleHeader(reader, expectedConsumer);
            var entry = reader.ReadInt32();
            var (structures, structureNames) = ReadStructures(reader);
            var classes = ReadClasses(reader, structureNames);
            var importedTypes = ReadImportedTypes(reader, structures.Length, structureNames, expectedConsumer);
            var count = Count(reader, MaximumFunctions);
            if (count == 0 || (expectedConsumer ? entry < 0 || entry >= count : entry != -1))
            {
                throw new ModuleFormatException("Module entry point is invalid.");
            }
            var items = ReadFunctions(reader, count, expectedConsumer, structures, classes);
            var module = new Module(expectedConsumer, entry, structures, items) {
                Classes = classes,
                ImportedStructures = importedTypes,
                RuntimePolicy = policy,
            };
            if (profile != RetainedNativeProfile(module))
            {
                throw new ModuleFormatException("Module semantic profile does not match its retained contracts.");
            }
            if (profile == "native-20" && classes.IsEmpty)
            {
                throw new ModuleFormatException("Optional managed nodes require a private source-class leaf.");
            }
            if (HasPrivateManagedState(module) && !expectedConsumer
                && module.Functions.Any(item =>
                    item.Import && (item.Function is not BoundFunction function
                        || !ManagedExportShape.Supported(function, structures)
                    )))
            {
                throw new ModuleFormatException("Managed leaf references require supported public Copy-value exports.");
            }
            if (stream.Position != stream.Length || (expectedConsumer && items[entry].Import))
            {
                throw new ModuleFormatException("Module has trailing data or an imported entry point.");
            }
            if (!expectedConsumer && HasPrivateManagedState(module))
            {
                ValidateProducer(module);
            }
            return module;
        }
        catch (Exception exception) when (exception is EndOfStreamException or DecoderFallbackException or OverflowException or ArgumentOutOfRangeException or IOException)
        {
            throw new ModuleFormatException("Malformed module bytes.", exception);
        }
    }

    private static (BoundRuntimePolicy Policy, string Profile) ReadModuleHeader(BinaryReader reader, bool expectedConsumer)
    {
        if (reader.ReadInt32() != Magic)
        {
            throw new ModuleFormatException("Unknown module format or version.");
        }
        var version = reader.ReadInt32();
        if (version != Version)
        {
            throw new ModuleFormatException("Unknown module format or version.");
        }
        var profile = GetString(reader);
        if (GetBool(reader) != expectedConsumer)
        {
            throw new ModuleFormatException("Unexpected module kind.");
        }
        var policy = (BoundRuntimePolicy)reader.ReadByte();
        if (policy is not (BoundRuntimePolicy.FailFast or BoundRuntimePolicy.CheckedExceptions)
            || profile is not ("native-10" or "native-11" or "native-12" or "native-13" or "native-14"
                or "native-15" or "native-16" or "native-17" or "native-18" or "native-19" or "native-20"))
        {
            throw new ModuleFormatException("Unsupported module semantic profile or checked-failure policy.");
        }
        return (policy, profile);
    }

    private static (ImmutableArray<BoundStruct> Structures, HashSet<string> Names) ReadStructures(BinaryReader reader)
    {
        var structureCount = Count(reader, MaximumFunctions);
        var structures = ImmutableArray.CreateBuilder<BoundStruct>(structureCount);
        var structureNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < structureCount; i++)
        {
            var name = GetString(reader);
            if (name.Length == 0 || !structureNames.Add(name))
            {
                throw new ModuleFormatException("Duplicate or empty type identity.");
            }
            var size = reader.ReadInt32();
            var isPrivate = GetBool(reader);
            var isInternal = GetBool(reader);
            var isUnique = GetBool(reader);
            var isTransparent = GetBool(reader);
            var cleanup = GetBool(reader) ? GetString(reader) : null;
            var fieldCount = Count(reader, MaximumSlots);
            var fields = ImmutableArray.CreateBuilder<BoundStructField>(fieldCount);
            for (var field = 0; field < fieldCount; field++)
            {
                var fieldName = GetString(reader);
                var fieldType = GetType(reader);
                if (ManagedClassTypes.IsManaged(fieldType))
                {
                    throw new ModuleFormatException("Managed fields have no persisted inline layout.");
                }
                fields.Add(new BoundStructField(fieldName, fieldType,
                    reader.ReadInt32(), reader.ReadInt32()) {
                    IsPrivate = GetBool(reader),
                    IsInternal = GetBool(reader),
                    IsMutable = GetBool(reader),
                });
            }
            structures.Add(new BoundStruct(name, fields.ToImmutable(), size) {
                IsPrivate = isPrivate,
                IsInternal = isInternal,
                IsUnique = isUnique,
                IsTransparent = isTransparent,
                CleanupMethod = cleanup,
            });
        }
        return (structures.ToImmutable(), structureNames);
    }

    private static ImmutableArray<string> ReadImportedTypes(BinaryReader reader, int structureCount,
        HashSet<string> structureNames, bool expectedConsumer)
    {
        var importCount = Count(reader, structureCount);
        var importedTypes = ImmutableArray.CreateBuilder<string>(importCount);
        var importedNames = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < importCount; index++)
        {
            var name = GetString(reader);
            if (!expectedConsumer || !structureNames.Contains(name) || !importedNames.Add(name))
            {
                throw new ModuleFormatException("Invalid imported type identity.");
            }
            importedTypes.Add(name);
        }
        return importedTypes.ToImmutable();
    }

    private static ImmutableArray<BoundClass> ReadClasses(BinaryReader reader, HashSet<string> structureNames)
    {
        var count = Count(reader, 256);
        var classes = ImmutableArray.CreateBuilder<BoundClass>(count);
        var names = new HashSet<string>(structureNames, StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var name = GetString(reader);
            if (name.Length == 0 || !names.Add(name))
            {
                throw new ModuleFormatException("Duplicate or empty managed class identity.");
            }
            var size = reader.ReadInt32();
            var isInternal = GetBool(reader);
            var isMutable = GetBool(reader);
            var fieldCount = Count(reader, ManagedClassTypes.MaximumFields);
            var fields = ImmutableArray.CreateBuilder<BoundStructField>(fieldCount);
            for (var field = 0; field < fieldCount; field++)
            {
                var fieldName = GetString(reader);
                var fieldType = GetType(reader);
                fields.Add(new BoundStructField(fieldName, fieldType,
                    reader.ReadInt32(), reader.ReadInt32()) {
                    IsPrivate = GetBool(reader),
                    IsInternal = GetBool(reader),
                    IsMutable = GetBool(reader),
                });
            }
            classes.Add(new BoundClass(name, fields.MoveToImmutable(), size) {
                IsInternal = isInternal,
                IsMutable = isMutable,
            });
        }
        return classes.MoveToImmutable();
    }

    private static ImmutableArray<ModuleFunction> ReadFunctions(BinaryReader reader, int count, bool expectedConsumer,
        ImmutableArray<BoundStruct> structures, ImmutableArray<BoundClass> classes)
    {
        var items = ImmutableArray.CreateBuilder<ModuleFunction>(count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var remaining = MaximumNodes;
        for (var i = 0; i < count; i++)
        {
            var name = GetString(reader);
            var contract = GetString(reader);
            var import = GetBool(reader);
            var hasBody = GetBool(reader);
            var ownershipTransfer = GetBool(reader);
            if (name.Length == 0 || !seen.Add(name) || (expectedConsumer ? import == hasBody : !hasBody))
            {
                throw new ModuleFormatException("Invalid module declaration.");
            }
            var function = hasBody ? GetFunction(reader, ref remaining) : null;
            if (!expectedConsumer && import && function is BoundFunction exported
                && !Exportable(exported, structures))
            {
                throw new ModuleFormatException("Producer export has a nonpublic declaration or value layout.");
            }
            if (import && (ContractContainsType(contract, PrimitiveType.ManagedNode)
                || ContractContainsType(contract, PrimitiveType.WriteManagedNode)))
            {
                throw new ModuleFormatException("Managed-node handles cannot cross a module declaration.");
            }
            if (ContractContainsOptionalType(contract)
                && import)
            {
                throw new ModuleFormatException("Optional managed handles have no persisted module ABI.");
            }
            if (expectedConsumer && import
                && (contract.Split('|') is not { Length: > 3 } parts || parts[3] != "E"))
            {
                throw new ModuleFormatException("Consumer import lacks an accessible contract.");
            }
            if (function is not null && (function.Name != name || Contract(function, structures) != contract
                || Compilation.HasOwnershipTransfer(function, structures) != ownershipTransfer))
            {
                throw new ModuleFormatException("Function body contradicts its declaration.");
            }
            items.Add(new ModuleFunction(name, contract, function, import) {
                HasOwnershipTransfer = ownershipTransfer,
            });
        }
        return items.ToImmutable();
    }

    private static void PutFunction(BinaryWriter writer, BoundFunction function, string[] names, ref int remaining)
    {
        PutString(writer, function.Name);
        writer.Write((int)function.ReturnType);
        writer.Write(function.IsConstructor);
        writer.Write(function.IsPrivate);
        writer.Write(function.IsInternal);
        writer.Write(function.IsStatic);
        writer.Write(function.IsNoThrow);
        writer.Write(function.DoesNotReturn);
        writer.Write(function.IsCleanup);
        writer.Write(function.PropertyName is not null);
        if (function.PropertyName is string property)
        {
            PutString(writer, property);
        }
        writer.Write((byte)function.Intrinsic);
        writer.Write(function.IsClassConstructor);
        writer.Write(function.ClassConstructor);
        writer.Write(function.DeclaringStruct is not null);
        if (function.DeclaringStruct is string declaringStruct)
        {
            PutString(writer, declaringStruct);
        }
        writer.Write(function.ParameterCount);
        writer.Write(function.Variables.Length);
        PutSpan(writer, function.Span);
        foreach (var variable in function.Variables)
        {
            PutString(writer, variable.Name);
            writer.Write((int)variable.Type);
            writer.Write(variable.IsMutable);
            writer.Write(variable.IsOut);
            writer.Write(variable.IsInit);
            PutSpan(writer, variable.Span);
        }
        PutStatement(writer, function.Body, names, ref remaining, 0);
    }

    private static BoundFunction GetFunction(BinaryReader reader, ref int remaining)
    {
        var name = GetString(reader);
        var type = GetType(reader);
        var constructor = GetBool(reader);
        var isPrivate = GetBool(reader);
        var isInternal = GetBool(reader);
        var isStatic = GetBool(reader);
        var isNoThrow = GetBool(reader);
        var doesNotReturn = GetBool(reader);
        var isCleanup = GetBool(reader);
        var propertyName = GetBool(reader) ? GetString(reader) : null;
        var intrinsic = (BoundIntrinsic)reader.ReadByte();
        var classConstructor = GetBool(reader);
        var classFactoryConstructor = reader.ReadInt32();
        if (intrinsic >= BoundIntrinsic.NodeTryCreate && intrinsic > BoundIntrinsic.ClassTryCreate)
        {
            throw new ModuleFormatException("Managed nodes have no persisted module runtime.");
        }
        var declaringStruct = GetBool(reader) ? GetString(reader) : null;
        var parameters = reader.ReadInt32();
        var count = Count(reader, MaximumSlots);
        if (parameters < 0 || parameters > count - (constructor ? 1 : 0))
        {
            throw new ModuleFormatException("Function parameter count exceeds its available slots.");
        }
        var span = GetSpan(reader);
        var variables = ImmutableArray.CreateBuilder<BoundVariable>(count);
        for (var i = 0; i < count; i++)
        {
            var variableName = GetString(reader);
            var variableType = GetType(reader);
            var mutable = GetBool(reader);
            var isOut = GetBool(reader);
            var isInit = GetBool(reader);
            variables.Add(new BoundVariable(variableName, variableType, mutable, GetSpan(reader)) {
                IsOut = isOut,
                IsInit = isInit,
            });
        }
        var body = GetStatement(reader, ref remaining, 0) as BoundBlock
            ?? throw new ModuleFormatException("Function requires a block body.");
        _ = Calls(body, visitVacantLocal: vacant => {
            if (vacant.Variable >= 0 && vacant.Variable < variables.Count)
            {
                variables[vacant.Variable] = variables[vacant.Variable] with { IsInitializationLocal = true };
            }
        });
        return new BoundFunction(name, type, variables.ToImmutable(), parameters, body, span) {
            IsConstructor = constructor,
            IsPrivate = isPrivate,
            IsInternal = isInternal,
            IsStatic = isStatic,
            IsNoThrow = isNoThrow,
            DoesNotReturn = doesNotReturn,
            IsCleanup = isCleanup,
            PropertyName = propertyName,
            Intrinsic = intrinsic,
            DeclaringStruct = declaringStruct,
            IsClassConstructor = classConstructor,
            ClassConstructor = classFactoryConstructor,
        };
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "The preceding tag selection rejects unsupported statement kinds.")]
    private static void PutStatement(BinaryWriter writer, BoundStatement statement, string[] names,
        ref int remaining, int depth)
    {
        Step(ref remaining, depth);
        writer.Write(statement switch {
            BoundBlock => (byte)1,
            BoundLocal => (byte)2,
            BoundAssignment => (byte)3,
            BoundOptionalAssignment => (byte)25,
            BoundExpressionStatement => (byte)4,
            BoundIf => (byte)5,
            BoundWhile { IsPostTested: true, Iterator: null } => (byte)21,
            BoundWhile { Iterator: not null } => (byte)20,
            BoundWhile { Iterator: null } => (byte)6,
            BoundRequire => (byte)7,
            BoundReturn => (byte)8,
            BoundFieldAssignment => (byte)9,
            BoundTryFinally => (byte)10,
            BoundUnsafe => (byte)11,
            BoundTryCatch => (byte)12,
            BoundDrop => (byte)13,
            BoundOwnerDeclaration => (byte)14,
            BoundVacantLocal => (byte)24,
            BoundLifetimeEnd => (byte)26,
            BoundDropConstructedFields => (byte)15,
            BoundViewScope => (byte)16,
            BoundViewSet => (byte)17,
            BoundBreak { TargetDepth: not 1 } => (byte)22,
            BoundContinue { TargetDepth: not 1 } => (byte)23,
            BoundBreak => (byte)18,
            BoundContinue => (byte)19,
            _ => throw new ModuleFormatException("Unknown statement kind."),
        });
        PutSpan(writer, statement.Span);
        switch (statement)
        {
            case BoundBlock block:
                writer.Write(block.IsManagedLexicalScope);
                writer.Write(block.RetiredManagedLocals.Length);
                foreach (var slot in block.RetiredManagedLocals)
                {
                    Step(ref remaining, depth + 1);
                    writer.Write(slot);
                }
                writer.Write(block.Statements.Length);
                foreach (var child in block.Statements)
                {
                    PutStatement(writer, child, names, ref remaining, depth + 1);
                }
                break;
            case BoundLocal local:
                writer.Write(local.Variable);
                PutExpression(writer, local.Initializer, names, ref remaining, depth + 1);
                break;
            case BoundViewScope view:
                writer.Write(view.Variable);
                writer.Write(view.IsMutable);
                PutExpression(writer, view.Owner, names, ref remaining, depth + 1);
                PutExpression(writer, view.Start, names, ref remaining, depth + 1);
                PutExpression(writer, view.Length, names, ref remaining, depth + 1);
                PutStatement(writer, view.Body, names, ref remaining, depth + 1);
                break;
            case BoundViewSet write:
                writer.Write(write.Variable);
                PutExpression(writer, write.Index, names, ref remaining, depth + 1);
                PutExpression(writer, write.Value, names, ref remaining, depth + 1);
                break;
            case BoundOwnerDeclaration declaration:
                writer.Write(declaration.Variable);
                break;
            case BoundVacantLocal vacant:
                writer.Write(vacant.Variable);
                break;
            case BoundLifetimeEnd end:
                writer.Write(end.Variable);
                writer.Write(end.CleanupFunction >= 0);
                if (end.CleanupFunction >= 0)
                {
                    PutString(writer, names[end.CleanupFunction]);
                }
                break;
            case BoundDropConstructedFields partial:
                writer.Write(partial.Variable);
                writer.Write(partial.AcquisitionOrder.Length);
                foreach (var field in partial.AcquisitionOrder)
                {
                    Step(ref remaining, depth + 1);
                    writer.Write(field);
                }
                break;
            case BoundAssignment assignment:
                writer.Write(assignment.Variable);
                PutExpression(writer, assignment.Value, names, ref remaining, depth + 1);
                break;
            case BoundOptionalAssignment optionalAssignment:
                PutExpression(writer, optionalAssignment.Place, names, ref remaining, depth + 1);
                PutExpression(writer, optionalAssignment.Value, names, ref remaining, depth + 1);
                break;
            case BoundFieldAssignment field:
                writer.Write(field.Variable);
                writer.Write(field.Field);
                writer.Write(field.NestedFields.Length);
                foreach (var nestedField in field.NestedFields)
                {
                    Step(ref remaining, depth + 1);
                    writer.Write(nestedField);
                }
                PutExpression(writer, field.Value, names, ref remaining, depth + 1);
                break;
            case BoundExpressionStatement expression:
                PutExpression(writer, expression.Expression, names, ref remaining, depth + 1);
                break;
            case BoundIf conditional:
                PutExpression(writer, conditional.Condition, names, ref remaining, depth + 1);
                PutStatement(writer, conditional.Then, names, ref remaining, depth + 1);
                writer.Write(conditional.Else is not null);
                if (conditional.Else is not null)
                {
                    PutStatement(writer, conditional.Else, names, ref remaining, depth + 1);
                }
                break;
            case BoundWhile loop:
                PutExpression(writer, loop.Condition, names, ref remaining, depth + 1);
                PutStatement(writer, loop.Body, names, ref remaining, depth + 1);
                if (loop.Iterator is not null)
                {
                    PutStatement(writer, loop.Iterator, names, ref remaining, depth + 1);
                }
                break;
            case BoundBreak { TargetDepth: not 1 } exit:
                writer.Write(exit.TargetDepth);
                break;
            case BoundContinue { TargetDepth: not 1 } next:
                writer.Write(next.TargetDepth);
                break;
            case BoundBreak or BoundContinue:
                break;
            case BoundTryFinally cleanup:
                PutStatement(writer, cleanup.Body, names, ref remaining, depth + 1);
                PutStatement(writer, cleanup.Finally, names, ref remaining, depth + 1);
                break;
            case BoundDrop drop:
                writer.Write(drop.Variable);
                if (drop.CleanupFunction >= names.Length || drop.CleanupFunction < -1)
                {
                    throw new ModuleFormatException("Invalid lexical cleanup target.");
                }
                writer.Write(drop.CleanupFunction >= 0);
                if (drop.CleanupFunction >= 0)
                {
                    PutString(writer, names[drop.CleanupFunction]);
                }
                writer.Write(drop.IsOutput);
                break;
            case BoundUnsafe unsafeBlock:
                PutStatement(writer, unsafeBlock.Body, names, ref remaining, depth + 1);
                break;
            case BoundTryCatch recovery:
                PutStatement(writer, recovery.Body, names, ref remaining, depth + 1);
                PutStatement(writer, recovery.Catch, names, ref remaining, depth + 1);
                writer.Write(recovery.Finally is not null);
                if (recovery.Finally is not null)
                {
                    PutStatement(writer, recovery.Finally, names, ref remaining, depth + 1);
                }
                break;
            case BoundRequire requirement:
                PutExpression(writer, requirement.Condition, names, ref remaining, depth + 1);
                break;
            case BoundReturn result:
                writer.Write(result.Value is not null);
                if (result.Value is not null)
                {
                    PutExpression(writer, result.Value, names, ref remaining, depth + 1);
                }
                break;
        }
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Unknown serialized node tags are rejected.")]
    private static BoundStatement GetStatement(BinaryReader reader, ref int remaining, int depth)
    {
        Step(ref remaining, depth);
        var kind = reader.ReadByte();
        var span = GetSpan(reader);
        switch (kind)
        {
            case 1:
                var managedScope = GetBool(reader);
                var retiredCount = Count(reader, MaximumSlots);
                var slots = ImmutableArray.CreateBuilder<int>(retiredCount);
                for (var i = 0; i < retiredCount; i++)
                {
                    Step(ref remaining, depth + 1);
                    slots.Add(reader.ReadInt32());
                }
                var count = Count(reader, MaximumNodes);
                var statements = ImmutableArray.CreateBuilder<BoundStatement>(count);
                for (var i = 0; i < count; i++)
                {
                    statements.Add(GetStatement(reader, ref remaining, depth + 1));
                }
                return new BoundBlock(statements.ToImmutable(), span) {
                    IsManagedLexicalScope = managedScope,
                    RetiredManagedLocals = slots.MoveToImmutable(),
                };
            case 2:
                return new BoundLocal(reader.ReadInt32(), GetExpression(reader, ref remaining, depth + 1), span);
            case 3:
                return new BoundAssignment(reader.ReadInt32(), GetExpression(reader, ref remaining, depth + 1), span);
            case 25:
                var place = GetExpression(reader, ref remaining, depth + 1);
                return new BoundOptionalAssignment(place,
                    GetExpression(reader, ref remaining, depth + 1), span);
            case 4:
                return new BoundExpressionStatement(GetExpression(reader, ref remaining, depth + 1), span);
            case 5:
                var condition = GetExpression(reader, ref remaining, depth + 1);
                var then = GetStatement(reader, ref remaining, depth + 1);
                var otherwise = GetBool(reader) ? GetStatement(reader, ref remaining, depth + 1) : null;
                return new BoundIf(condition, then, otherwise, span);
            case 6:
                return new BoundWhile(GetExpression(reader, ref remaining, depth + 1),
                    GetStatement(reader, ref remaining, depth + 1), span);
            case 7:
                return new BoundRequire(GetExpression(reader, ref remaining, depth + 1), span);
            case 8:
                return new BoundReturn(GetBool(reader) ? GetExpression(reader, ref remaining, depth + 1) : null, span);
            case 9:
                var variable = reader.ReadInt32();
                var field = reader.ReadInt32();
                var pathLength = Count(reader, MaximumDepth);
                var fields = ImmutableArray.CreateBuilder<int>(pathLength);
                for (var index = 0; index < pathLength; index++)
                {
                    Step(ref remaining, depth + index + 1);
                    fields.Add(reader.ReadInt32());
                }
                return new BoundFieldAssignment(variable, field, GetExpression(reader, ref remaining, depth + 1), span) {
                    NestedFields = fields.ToImmutable(),
                };
            case 10:
                return new BoundTryFinally(GetStatement(reader, ref remaining, depth + 1) as BoundBlock
                    ?? throw new ModuleFormatException("Expected try block."),
                    GetStatement(reader, ref remaining, depth + 1) as BoundBlock
                    ?? throw new ModuleFormatException("Expected finally block."), span);
            case 11:
                return new BoundUnsafe(GetStatement(reader, ref remaining, depth + 1) as BoundBlock
                    ?? throw new ModuleFormatException("Expected unsafe block."), span);
            case 12:
                var body = GetStatement(reader, ref remaining, depth + 1) as BoundBlock
                    ?? throw new ModuleFormatException("Expected protected block.");
                var handler = GetStatement(reader, ref remaining, depth + 1) as BoundBlock
                    ?? throw new ModuleFormatException("Expected recovery block.");
                var cleanup = GetBool(reader) ? GetStatement(reader, ref remaining, depth + 1) as BoundBlock
                    ?? throw new ModuleFormatException("Expected recovery cleanup block.") : null;
                return new BoundTryCatch(body, handler, cleanup, span);
            case 13:
                var owner = reader.ReadInt32();
                var method = GetBool(reader) ? GetString(reader) : null;
                return new SymbolicDrop(owner, method, span) {
                    IsOutput = GetBool(reader),
                };
            case 14:
                return new BoundOwnerDeclaration(reader.ReadInt32(), span);
            case 24:
                return new BoundVacantLocal(reader.ReadInt32(), span);
            case 26:
                var ended = reader.ReadInt32();
                return new SymbolicLifetimeEnd(ended, GetBool(reader) ? GetString(reader) : null, span);
            case 15:
                var constructedOwner = reader.ReadInt32();
                var acquired = Count(reader, ValueLayout.MaximumSize);
                var order = ImmutableArray.CreateBuilder<int>(acquired);
                for (var i = 0; i < acquired; i++)
                {
                    Step(ref remaining, depth + 1);
                    order.Add(reader.ReadInt32());
                }
                return new BoundDropConstructedFields(constructedOwner, order.ToImmutable(), span);
            case 16:
                var viewVariable = reader.ReadInt32();
                var mutable = GetBool(reader);
                var viewOwner = GetExpression(reader, ref remaining, depth + 1);
                var start = GetExpression(reader, ref remaining, depth + 1);
                var length = GetExpression(reader, ref remaining, depth + 1);
                var viewBody = GetStatement(reader, ref remaining, depth + 1) as BoundBlock
                    ?? throw new ModuleFormatException("Expected lexical view block.");
                return new BoundViewScope(viewVariable, viewOwner, start, length, viewBody, span) {
                    IsMutable = mutable,
                };
            case 17:
                return new BoundViewSet(reader.ReadInt32(), GetExpression(reader, ref remaining, depth + 1),
                    GetExpression(reader, ref remaining, depth + 1), span);
            case 18:
                return new BoundBreak(span);
            case 19:
                return new BoundContinue(span);
            case 20:
                var loopCondition = GetExpression(reader, ref remaining, depth + 1);
                var loopBody = GetStatement(reader, ref remaining, depth + 1);
                var iterator = GetStatement(reader, ref remaining, depth + 1);
                return new BoundWhile(loopCondition, loopBody, span) { Iterator = iterator };
            case 21:
                return new BoundWhile(GetExpression(reader, ref remaining, depth + 1),
                    GetStatement(reader, ref remaining, depth + 1), span) { IsPostTested = true };
            case 22:
                return new BoundBreak(span) { TargetDepth = reader.ReadInt32() };
            case 23:
                return new BoundContinue(span) { TargetDepth = reader.ReadInt32() };
            default:
                throw new ModuleFormatException("Unknown statement kind.");
        }
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "The preceding tag selection rejects unsupported expression kinds.")]
    private static void PutExpression(BinaryWriter writer, BoundExpression expression, string[] names, ref int remaining, int depth)
    {
        Step(ref remaining, depth);
        writer.Write(expression switch {
            BoundConstant => (byte)1,
            BoundRead => (byte)2,
            BoundUnary => (byte)3,
            BoundUpdate => (byte)24,
            BoundNominalUpdate => (byte)25,
            BoundCompoundAssignment => (byte)26,
            BoundBinary => (byte)4,
            BoundConditional => (byte)22,
            BoundCoalesce => (byte)23,
            BoundCall => (byte)5,
            BoundCast => (byte)6,
            BoundFieldRead => (byte)7,
            BoundOutArgument { IsInit: true } => (byte)21,
            BoundOutArgument => (byte)8,
            BoundMove => (byte)9,
            BoundReceiverLoan => (byte)10,
            BoundFieldLoan => (byte)11,
            BoundMutableFieldLoan => (byte)20,
            BoundMutableReceiverLoan => (byte)12,
            BoundViewLength => (byte)13,
            BoundViewRead => (byte)14,
            BoundNull => (byte)15,
            BoundOptionalLift => (byte)16,
            BoundReadonlyProjection => (byte)19,
            BoundIsNull => (byte)17,
            BoundIsSome => (byte)18,
            BoundIsVariablePattern => (byte)27,
            BoundIsInitialized => (byte)28,
            _ => throw new ModuleFormatException("Unknown expression kind."),
        });
        writer.Write((int)expression.Type);
        PutSpan(writer, expression.Span);
        switch (expression)
        {
            case BoundConstant constant:
                writer.Write(constant.Value);
                break;
            case BoundRead read:
                writer.Write(read.Variable);
                break;
            case BoundMove move:
                writer.Write(move.Variable);
                break;
            case BoundReceiverLoan loan:
                writer.Write(loan.Variable);
                break;
            case BoundMutableReceiverLoan loan:
                writer.Write(loan.Variable);
                break;
            case BoundViewLength viewLength:
                writer.Write(viewLength.Variable);
                break;
            case BoundViewRead viewRead:
                writer.Write(viewRead.Variable);
                PutExpression(writer, viewRead.Index, names, ref remaining, depth + 1);
                break;
            case BoundFieldLoan fieldLoan:
                PutExpression(writer, fieldLoan.Place, names, ref remaining, depth + 1);
                break;
            case BoundMutableFieldLoan mutableField:
                PutExpression(writer, mutableField.Place, names, ref remaining, depth + 1);
                break;
            case BoundNull:
                break;
            case BoundOptionalLift lift:
                PutExpression(writer, lift.Value, names, ref remaining, depth + 1);
                break;
            case BoundReadonlyProjection projection:
                PutExpression(writer, projection.Value, names, ref remaining, depth + 1);
                break;
            case BoundIsNull test:
                PutExpression(writer, test.Value, names, ref remaining, depth + 1);
                break;
            case BoundIsSome pattern:
                writer.Write(pattern.Variable);
                PutExpression(writer, pattern.Value, names, ref remaining, depth + 1);
                break;
            case BoundIsVariablePattern pattern:
                writer.Write(pattern.Variable);
                writer.Write(pattern.IncludeNull);
                PutExpression(writer, pattern.Value, names, ref remaining, depth + 1);
                break;
            case BoundIsInitialized initializationTest:
                writer.Write(initializationTest.Variable);
                break;
            case BoundUnary unary:
                writer.Write((int)unary.Operator);
                PutExpression(writer, unary.Operand, names, ref remaining, depth + 1);
                break;
            case BoundUpdate update:
                writer.Write((int)update.Operator);
                writer.Write(update.IsPrefix);
                PutExpression(writer, update.Place, names, ref remaining, depth + 1);
                break;
            case BoundCompoundAssignment compound:
                writer.Write((int)compound.Operator);
                PutExpression(writer, compound.Place, names, ref remaining, depth + 1);
                PutExpression(writer, compound.Right, names, ref remaining, depth + 1);
                break;
            case BoundNominalUpdate update:
                writer.Write((int)update.Operator);
                writer.Write(update.IsPrefix);
                if (update.Function < 0 || update.Function >= names.Length)
                {
                    throw new ModuleFormatException("Invalid update operator target.");
                }
                PutString(writer, names[update.Function]);
                PutExpression(writer, update.Place, names, ref remaining, depth + 1);
                break;
            case BoundBinary binary:
                writer.Write((int)binary.Operator);
                PutExpression(writer, binary.Left, names, ref remaining, depth + 1);
                PutExpression(writer, binary.Right, names, ref remaining, depth + 1);
                break;
            case BoundConditional conditional:
                PutExpression(writer, conditional.Condition, names, ref remaining, depth + 1);
                PutExpression(writer, conditional.WhenTrue, names, ref remaining, depth + 1);
                PutExpression(writer, conditional.WhenFalse, names, ref remaining, depth + 1);
                break;
            case BoundCoalesce coalesce:
                PutExpression(writer, coalesce.Optional, names, ref remaining, depth + 1);
                PutExpression(writer, coalesce.Fallback, names, ref remaining, depth + 1);
                break;
            case BoundCast cast:
                PutExpression(writer, cast.Operand, names, ref remaining, depth + 1);
                break;
            case BoundFieldRead field:
                writer.Write(field.Field);
                PutExpression(writer, field.Receiver, names, ref remaining, depth + 1);
                break;
            case BoundOutArgument output:
                writer.Write(output.Variable);
                writer.Write(output.DeclaresVariable);
                break;
            case BoundCall call:
                if (call.Function < 0 || call.Function >= names.Length)
                {
                    throw new ModuleFormatException("Invalid call target.");
                }
                PutString(writer, names[call.Function]);
                writer.Write(call.Arguments.Length);
                foreach (var argument in call.Arguments)
                {
                    PutExpression(writer, argument, names, ref remaining, depth + 1);
                }
                break;
        }
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Unknown serialized node tags are rejected.")]
    private static BoundExpression GetExpression(BinaryReader reader, ref int remaining, int depth)
    {
        Step(ref remaining, depth);
        var kind = reader.ReadByte();
        var type = GetType(reader);
        var span = GetSpan(reader);
        switch (kind)
        {
            case 1:
                return new BoundConstant(reader.ReadInt64(), type, span);
            case 2:
                return new BoundRead(reader.ReadInt32(), type, span);
            case 3:
                return new BoundUnary((TokenKind)reader.ReadInt32(), GetExpression(reader, ref remaining, depth + 1), type, span);
            case 24:
                var updateOperator = (TokenKind)reader.ReadInt32();
                var prefix = reader.ReadBoolean();
                return new BoundUpdate(updateOperator, GetExpression(reader, ref remaining, depth + 1),
                    prefix, type, span);
            case 26:
                var compoundOperator = (TokenKind)reader.ReadInt32();
                var compoundPlace = GetExpression(reader, ref remaining, depth + 1);
                return new BoundCompoundAssignment(compoundOperator, compoundPlace,
                    GetExpression(reader, ref remaining, depth + 1), type, span);
            case 25:
                var nominalOperator = (TokenKind)reader.ReadInt32();
                var nominalPrefix = reader.ReadBoolean();
                var nominalName = GetString(reader);
                return new SymbolicNominalUpdate(nominalOperator,
                    GetExpression(reader, ref remaining, depth + 1),
                    nominalPrefix, nominalName, type, span);
            case 4:
                var op = (TokenKind)reader.ReadInt32();
                var left = GetExpression(reader, ref remaining, depth + 1);
                return new BoundBinary(op, left, GetExpression(reader, ref remaining, depth + 1), type, span);
            case 22:
                var condition = GetExpression(reader, ref remaining, depth + 1);
                var whenTrue = GetExpression(reader, ref remaining, depth + 1);
                return new BoundConditional(condition, whenTrue,
                    GetExpression(reader, ref remaining, depth + 1), type, span);
            case 23:
                var optional = GetExpression(reader, ref remaining, depth + 1);
                return new BoundCoalesce(optional, GetExpression(reader, ref remaining, depth + 1),
                    type, span);
            case 5:
                // Names are resolved after the complete module graph is read.
                var name = GetString(reader);
                var count = Count(reader, 64);
                var args = ImmutableArray.CreateBuilder<BoundExpression>(count);
                for (var i = 0; i < count; i++)
                {
                    args.Add(GetExpression(reader, ref remaining, depth + 1));
                }
                return new SymbolicCall(name, args.ToImmutable(), type, span);
            case 6:
                return new BoundCast(GetExpression(reader, ref remaining, depth + 1), type, span);
            case 7:
                var field = reader.ReadInt32();
                return new BoundFieldRead(GetExpression(reader, ref remaining, depth + 1), field, type, span);
            case 8:
                return new BoundOutArgument(reader.ReadInt32(), GetBool(reader), type, span);
            case 21:
                return new BoundOutArgument(reader.ReadInt32(), GetBool(reader), type, span) { IsInit = true };
            case 9:
                return new BoundMove(reader.ReadInt32(), type, span);
            case 10:
                return new BoundReceiverLoan(reader.ReadInt32(), type, span);
            case 11:
                return new BoundFieldLoan(GetExpression(reader, ref remaining, depth + 1)
                    as BoundFieldRead ?? throw new ModuleFormatException("Expected owning field place."), type, span);
            case 12:
                return new BoundMutableReceiverLoan(reader.ReadInt32(), type, span);
            case 13 when type == PrimitiveType.Int32:
                return new BoundViewLength(reader.ReadInt32(), span);
            case 14 when type == PrimitiveType.Int32:
                var variable = reader.ReadInt32();
                return new BoundViewRead(variable, GetExpression(reader, ref remaining, depth + 1), span);
            case 15 when ManagedClassTypes.IsOptional(type):
                return new BoundNull(type, span);
            case 16 when ManagedClassTypes.IsOptional(type):
                return new BoundOptionalLift(GetExpression(reader, ref remaining, depth + 1), type, span);
            case 17 when type == PrimitiveType.Bool:
                return new BoundIsNull(GetExpression(reader, ref remaining, depth + 1), span);
            case 18 when type == PrimitiveType.Bool:
                var pattern = reader.ReadInt32();
                return new BoundIsSome(GetExpression(reader, ref remaining, depth + 1), pattern, span);
            case 19:
                return new BoundReadonlyProjection(GetExpression(reader, ref remaining, depth + 1), type, span);
            case 20:
                return new BoundMutableFieldLoan(GetExpression(reader, ref remaining, depth + 1)
                    as BoundFieldRead ?? throw new ModuleFormatException("Expected mutable field place."), type, span);
            case 27 when type == PrimitiveType.Bool:
                var variablePatternSlot = reader.ReadInt32();
                var includeNull = GetBool(reader);
                return new BoundIsVariablePattern(GetExpression(reader, ref remaining, depth + 1),
                    variablePatternSlot, includeNull, span);
            case 28 when type == PrimitiveType.Bool:
                return new BoundIsInitialized(reader.ReadInt32(), span);
            default:
                throw new ModuleFormatException("Unknown expression kind.");
        }
    }

    private static BoundProgram Resolve(BoundProgram program, IReadOnlyDictionary<string, int> names)
    {
        var functions = program.Functions.Select(function => function with { Body = (BoundBlock)ResolveStatement(function.Body, names) }).ToImmutableArray();
        return program with { Functions = functions };
    }

    private static BoundStatement ResolveStatement(BoundStatement statement, IReadOnlyDictionary<string, int> names) => statement switch {
        BoundBlock block => block with { Statements = [.. block.Statements.Select(child => ResolveStatement(child, names))] },
        BoundLocal local => local with { Initializer = ResolveExpression(local.Initializer, names) },
        BoundLifetimeEnd or BoundOwnerDeclaration or BoundVacantLocal => statement,
        BoundAssignment assignment => assignment with { Value = ResolveExpression(assignment.Value, names) },
        BoundOptionalAssignment optionalAssignment => optionalAssignment with {
            Place = ResolveExpression(optionalAssignment.Place, names),
            Value = ResolveExpression(optionalAssignment.Value, names),
        },
        BoundFieldAssignment field => field with { Value = ResolveExpression(field.Value, names) },
        BoundExpressionStatement expression => expression with { Expression = ResolveExpression(expression.Expression, names) },
        BoundIf conditional => conditional with {
            Condition = ResolveExpression(conditional.Condition, names),
            Then = ResolveStatement(conditional.Then, names),
            Else = conditional.Else is null ? null : ResolveStatement(conditional.Else, names),
        },
        BoundWhile loop => loop with {
            Condition = ResolveExpression(loop.Condition, names),
            Body = ResolveStatement(loop.Body, names),
            Iterator = loop.Iterator is null ? null : ResolveStatement(loop.Iterator, names),
        },
        BoundBreak or BoundContinue => statement,
        BoundTryFinally cleanup => cleanup with { Body = (BoundBlock)ResolveStatement(cleanup.Body, names), Finally = (BoundBlock)ResolveStatement(cleanup.Finally, names) },
        BoundViewScope view => view with {
            Owner = ResolveExpression(view.Owner, names),
            Start = ResolveExpression(view.Start, names),
            Length = ResolveExpression(view.Length, names),
            Body = (BoundBlock)ResolveStatement(view.Body, names),
        },
        BoundViewSet write => write with {
            Index = ResolveExpression(write.Index, names),
            Value = ResolveExpression(write.Value, names),
        },
        SymbolicDrop drop => drop.Name is null
            ? new BoundDrop(drop.Variable, -1, drop.Span) { IsOutput = drop.IsOutput }
            : names.TryGetValue(drop.Name, out var method)
                ? new BoundDrop(drop.Variable, method, drop.Span) { IsOutput = drop.IsOutput }
                : throw new ModuleContractException($"Unresolved cleanup target '{drop.Name}'."),
        BoundDrop => statement,
        SymbolicLifetimeEnd end => end.Name is null
            ? new BoundLifetimeEnd(end.Variable, -1, end.Span)
            : names.TryGetValue(end.Name, out var cleanup)
                ? new BoundLifetimeEnd(end.Variable, cleanup, end.Span)
                : throw new ModuleContractException($"Unresolved cleanup target '{end.Name}'."),
        BoundDropConstructedFields => statement,
        BoundUnsafe unsafeBlock => unsafeBlock with { Body = (BoundBlock)ResolveStatement(unsafeBlock.Body, names) },
        BoundTryCatch recovery => recovery with {
            Body = (BoundBlock)ResolveStatement(recovery.Body, names),
            Catch = (BoundBlock)ResolveStatement(recovery.Catch, names),
            Finally = recovery.Finally is null ? null : (BoundBlock)ResolveStatement(recovery.Finally, names),
        },
        BoundRequire requirement => requirement with { Condition = ResolveExpression(requirement.Condition, names) },
        BoundReturn result => result with { Value = result.Value is null ? null : ResolveExpression(result.Value, names) },
        _ => throw new ModuleFormatException("Unknown statement kind."),
    };

    private static BoundExpression ResolveExpression(BoundExpression expression, IReadOnlyDictionary<string, int> names) => expression switch {
        SymbolicCall call => names.TryGetValue(call.Name, out var index)
            ? new BoundCall(index, [.. call.Arguments.Select(argument => ResolveExpression(argument, names))], call.Type, call.Span)
            : throw new ModuleContractException($"Unresolved call target '{call.Name}'."),
        BoundUnary unary => unary with { Operand = ResolveExpression(unary.Operand, names) },
        BoundUpdate update => update with { Place = ResolveExpression(update.Place, names) },
        BoundCompoundAssignment compound => compound with {
            Place = ResolveExpression(compound.Place, names),
            Right = ResolveExpression(compound.Right, names),
        },
        SymbolicNominalUpdate update => names.TryGetValue(update.Name, out var method)
            ? new BoundNominalUpdate(update.Operator, ResolveExpression(update.Place, names),
                update.IsPrefix, method, update.Type, update.Span)
            : throw new ModuleContractException($"Unresolved update operator '{update.Name}'."),
        BoundBinary binary => binary with { Left = ResolveExpression(binary.Left, names), Right = ResolveExpression(binary.Right, names) },
        BoundCoalesce coalesce => coalesce with {
            Optional = ResolveExpression(coalesce.Optional, names),
            Fallback = ResolveExpression(coalesce.Fallback, names),
        },
        BoundConditional conditional => conditional with {
            Condition = ResolveExpression(conditional.Condition, names),
            WhenTrue = ResolveExpression(conditional.WhenTrue, names),
            WhenFalse = ResolveExpression(conditional.WhenFalse, names),
        },
        BoundCast cast => cast with { Operand = ResolveExpression(cast.Operand, names) },
        BoundOptionalLift lift => lift with { Value = ResolveExpression(lift.Value, names) },
        BoundReadonlyProjection projection => projection with { Value = ResolveExpression(projection.Value, names) },
        BoundIsNull test => test with { Value = ResolveExpression(test.Value, names) },
        BoundIsSome pattern => pattern with { Value = ResolveExpression(pattern.Value, names) },
        BoundIsVariablePattern pattern => pattern with { Value = ResolveExpression(pattern.Value, names) },
        BoundFieldRead field => field with { Receiver = ResolveExpression(field.Receiver, names) },
        BoundFieldLoan loan => loan with { Place = (BoundFieldRead)ResolveExpression(loan.Place, names) },
        BoundMutableFieldLoan loan => loan with { Place = (BoundFieldRead)ResolveExpression(loan.Place, names) },
        BoundViewRead view => view with { Index = ResolveExpression(view.Index, names) },
        _ => expression,
    };

    private static PrimitiveType GetType(BinaryReader reader)
    {
        return (PrimitiveType)reader.ReadInt32();
    }

    private static void PutSpan(BinaryWriter writer, TextSpan span)
    {
        writer.Write(span.Start);
        writer.Write(span.Length);
    }
    private static bool GetBool(BinaryReader reader)
    {
        var value = reader.ReadByte();
        if (value is not (0 or 1))
        {
            throw new ModuleFormatException("Noncanonical module boolean.");
        }
        return value == 1;
    }
    private static TextSpan GetSpan(BinaryReader reader) => new(reader.ReadInt32(), reader.ReadInt32());
    private static int Count(BinaryReader reader, int maximum)
    {
        var value = reader.ReadInt32();
        if (value < 0 || value > maximum)
        {
            throw new ModuleFormatException("Module count exceeds limits.");
        }
        return value;
    }
    private static void Step(ref int remaining, int depth)
    {
        if (depth > MaximumDepth || --remaining < 0)
        {
            throw new ModuleFormatException("Module graph exceeds limits.");
        }
    }
    private static void PutString(BinaryWriter writer, string value)
    {
        var bytes = new UTF8Encoding(false, true).GetBytes(value);
        if (bytes.Length > MaximumStringBytes)
        {
            throw new ModuleFormatException("Module string exceeds limits.");
        }
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
    private static string GetString(BinaryReader reader)
    {
        var length = Count(reader, MaximumStringBytes);
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
        {
            throw new ModuleFormatException("Truncated module string.");
        }
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    private sealed record Module(bool Consumer, int EntryPoint, ImmutableArray<BoundStruct> Structures, ImmutableArray<ModuleFunction> Functions)
    {
        public ImmutableArray<BoundClass> Classes { get; init; } = [];
        public ImmutableArray<string> ImportedStructures { get; init; } = [];
        public BoundRuntimePolicy RuntimePolicy { get; init; }
    }
    private sealed record ModuleFunction(string Name, string Contract, BoundFunction? Function, bool Import)
    {
        public bool HasOwnershipTransfer { get; init; }
    }
    private sealed record SymbolicCall(string Name, ImmutableArray<BoundExpression> Arguments, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);
    private sealed record SymbolicLifetimeEnd(int Variable, string? Name, TextSpan Span) : BoundStatement(Span);
    private sealed record SymbolicNominalUpdate(TokenKind Operator, BoundExpression Place, bool IsPrefix,
        string Name, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);
    private sealed record SymbolicDrop(int Variable, string? Name, TextSpan Span) : BoundStatement(Span)
    {
        public bool IsOutput { get; init; }
    }
}

/// <summary>Invalid, unsupported or tampered module artifact.</summary>
public sealed class ModuleFormatException : Exception
{
    /// <summary>Creates a module format failure.</summary>
    public ModuleFormatException() { }
    /// <summary>Creates a module format failure.</summary>
    public ModuleFormatException(string message) : base(message) { }
    /// <summary>Creates a module format failure with a cause.</summary>
    public ModuleFormatException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>An imported declaration does not match the separately supplied implementation.</summary>
public sealed class ModuleContractException : Exception
{
    /// <summary>Creates a module contract failure.</summary>
    public ModuleContractException() { }
    /// <summary>Creates a module contract failure.</summary>
    public ModuleContractException(string message) : base(message) { }
    /// <summary>Creates a module contract failure with a cause.</summary>
    public ModuleContractException(string message, Exception innerException) : base(message, innerException) { }
}
