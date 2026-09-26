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

/// <summary>A bounded, versioned semantic module artifact; linking never reads source text.</summary>
public static partial class ModuleCompiler
{
    private const int Magic = 0x4D525254; // TRRM
    private const int OwnershipVersion = 12;
    private const int TransferVersion = 13;
    private const int CompositeVersion = 14;
    private const int BufferVersion = 15;
    private const int ReadonlyViewVersion = 16;
    private const int WritableViewVersion = 17;
    private const int ManagedNodeVersion = 18;
    private const int Version = 19;
    private const int MaximumBytes = 4 * 1024 * 1024;
    private const int MaximumFunctions = 1024;
    private const int MaximumSlots = 256;
    private const int MaximumNodes = 100_000;
    private const int MaximumDepth = 128;
    private const int MaximumStringBytes = 1024;

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
        // Version 15 mistook a seven-parameter count for the owned-buffer type ID.
        return module.SerializedVersion == BufferVersion && module.SerializedProfile == "native-15"
            && profile is not ("native-15" or "native-16" or "native-17")
            && module.Functions.Any(static item => ContractParameterCount(item.Contract) == 7)
                ? "native-15" : profile;
    }

    private static void RejectManagedNodes(BoundProgram program)
    {
        RejectManagedClasses(program);
        if (HasManagedNodes(program)
            || program.Structures.Any(static structure =>
                structure.Fields.Any(static field => field.Type == PrimitiveType.ManagedNode)))
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

    private static bool IsInlineClassField(PrimitiveType type) =>
        (int)type >= 100 && !ManagedClassTypes.IsManaged(type);

    private static bool UsesManagedNode(BoundFunction function) =>
        function.Intrinsic is >= BoundIntrinsic.NodeTryCreate and <= BoundIntrinsic.NodeCollect
        || function.ReturnType == PrimitiveType.ManagedNode
        || function.Variables.Any(static variable => variable.Type == PrimitiveType.ManagedNode);

    private static bool ScalarManagedExport(BoundFunction function) =>
        (function.ReturnType == PrimitiveType.Void || ScalarManagedType(function.ReturnType))
        && function.Variables.Skip(function.IsConstructor ? 1 : 0).Take(function.ParameterCount)
            .All(static variable => !variable.IsOut && ScalarManagedType(variable.Type));

    private static bool ScalarManagedType(PrimitiveType type) =>
        type is PrimitiveType.Bool or PrimitiveType.Int32 or PrimitiveType.UInt32
            or PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Char16;

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

    private static HashSet<string> RequiredStructures(IEnumerable<BoundFunction> functions, ImmutableArray<BoundStruct> structures)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<PrimitiveType>();
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
        if (program.EntryPoint == -1)
        {
            SemanticVerifier.VerifyLibrary(program);
        }
        else
        {
            SemanticVerifier.Verify(program);
        }
        if (program.Classes.Any(static @class => @class.Fields.Any(
                static field => IsInlineClassField(field.Type))))
        {
            throw new ModuleFormatException("Inline struct class fields require a newer semantic module version.");
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
                && indexes.Any(index => !ScalarManagedExport(program.Functions[index]))))
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
        var required = RequiredStructures(functions, program.Structures);
        var structures = SelectStructures(program.Structures, required);
        var remapped = functions.Select(function => RemapFunction(function, program.Structures, structures));
        var module = new Module(false, -1, structures, [.. remapped.Select(function =>
            new ModuleFunction(function.Name, Contract(function, structures), function, names.Contains(function.Name)) {
                HasOwnershipTransfer = Compilation.HasOwnershipTransfer(function, structures),
            })]) {
            Classes = program.Classes,
            RuntimePolicy = program.RuntimePolicy,
        };
        return Write(module, program);
    }

    /// <summary>Stores consumer bound bodies and imported declaration contracts, but never producer bodies.</summary>
    public static byte[] CreateConsumer(BoundProgram program, byte[] referenceProducer)
    {
        ArgumentNullException.ThrowIfNull(referenceProducer);
        SemanticVerifier.VerifyReferences(program);
        RejectManagedNodes(program);
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
            Classes = [.. producer.Classes.Select(static item => item with { Name = "$producer." + item.Name })],
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
                && !sharedTypes.Contains(owner))
            {
                owner = "$producer." + owner;
            }
            return RemapFunction(function with { Name = name, DeclaringStruct = owner }, renamedTypes, allStructures);
        }
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Only nodes with nested expressions or statements need traversal.")]
    private static List<int> Calls(BoundStatement body, Action<PrimitiveType>? visitType = null,
        Action<BoundViewScope>? visitView = null)
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
                case BoundAssignment assignment:
                    VisitExpression(assignment.Value);
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
                    VisitExpression(loop.Condition);
                    VisitStatement(loop.Body);
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
                case BoundUnary unary:
                    VisitExpression(unary.Operand);
                    break;
                case BoundBinary binary:
                    VisitExpression(binary.Left);
                    VisitExpression(binary.Right);
                    break;
                case BoundCast cast:
                    VisitExpression(cast.Operand);
                    break;
                case BoundOptionalLift lift:
                    VisitExpression(lift.Value);
                    break;
                case BoundIsNull test:
                    VisitExpression(test.Value);
                    break;
                case BoundIsSome pattern:
                    VisitExpression(pattern.Value);
                    break;
                case BoundFieldRead field:
                    VisitExpression(field.Receiver);
                    break;
                case BoundFieldLoan loan:
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

    private static string Contract(BoundFunction function, ImmutableArray<BoundStruct> structures)
    {
        var result = new StringBuilder();
        _ = result.Append(function.Name).Append('|').Append(ContractType(function.ReturnType, structures))
            .Append('|').Append(function.IsConstructor ? 'C' : 'F')
            .Append('|').Append(function.IsPrivate ? 'P' : function.IsInternal ? 'N' : 'E')
            .Append('|').Append(function.IsStatic ? 'S' : 'I')
            .Append('|').Append(function.DeclaringStruct ?? "-")
            .Append('|').Append(function.DeclaringStruct is string owner
                ? structures.Where(structure => structure.Name == owner)
                    .Select(static structure => Visibility(structure)).FirstOrDefault('?') : '-')
            .Append('|').Append(function.ParameterCount);
        for (var i = 0; i < function.ParameterCount; i++)
        {
            var parameter = function.Variables[i + (function.IsConstructor ? 1 : 0)];
            _ = result.Append('|').Append(parameter.IsOut ? "out:" : "in:")
                .Append(ContractType(parameter.Type, structures));
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
        if (function.IsPrivate || function.IsInternal || function.ParameterCount < 0
            || function.ParameterCount > function.Variables.Length - parameterOffset)
        {
            return false;
        }
        var visited = new HashSet<(int Index, bool PublicFields)>();
        if (function.DeclaringStruct is string declaring
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
            || a.IsUnique != b.IsUnique || a.CleanupMethod != b.CleanupMethod)
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
        ImmutableArray<BoundStruct> destination)
    {
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

    private static ImmutableArray<BoundStruct> SelectStructures(ImmutableArray<BoundStruct> source, HashSet<string> required)
    {
        var selected = source.Where(structure => required.Contains(structure.Name)).ToImmutableArray();
        return [.. selected.Select(structure => RemapStructure(structure, source, selected))];
    }

    private static BoundFunction RemapFunction(BoundFunction function, ImmutableArray<BoundStruct> source,
        ImmutableArray<BoundStruct> destination)
    {
        return function with {
            ReturnType = RemapType(function.ReturnType, source, destination),
            Variables = [.. function.Variables.Select(variable => variable with {
                Type = RemapType(variable.Type, source, destination),
            })],
            Body = (BoundBlock)RemapStatement(function.Body, source, destination),
        };
    }

    private static BoundExpression RemapExpression(BoundExpression expression, ImmutableArray<BoundStruct> source,
        ImmutableArray<BoundStruct> destination)
    {
        return expression switch {
            BoundConstant constant => constant with { Type = RemapType(constant.Type, source, destination) },
            BoundRead read => read with { Type = RemapType(read.Type, source, destination) },
            BoundMove move => move with { Type = RemapType(move.Type, source, destination) },
            BoundReceiverLoan loan => loan with { Type = RemapType(loan.Type, source, destination) },
            BoundFieldLoan loan => loan with {
                Type = RemapType(loan.Type, source, destination),
                Place = (BoundFieldRead)RemapExpression(loan.Place, source, destination),
            },
            BoundMutableReceiverLoan loan => loan with { Type = RemapType(loan.Type, source, destination) },
            BoundViewLength => expression,
            BoundViewRead view => view with { Index = RemapExpression(view.Index, source, destination) },
            BoundUnary unary => unary with {
                Type = RemapType(unary.Type, source, destination),
                Operand = RemapExpression(unary.Operand, source, destination),
            },
            BoundBinary binary => binary with {
                Type = RemapType(binary.Type, source, destination),
                Left = RemapExpression(binary.Left, source, destination),
                Right = RemapExpression(binary.Right, source, destination),
            },
            BoundCast cast => cast with {
                Type = RemapType(cast.Type, source, destination),
                Operand = RemapExpression(cast.Operand, source, destination),
            },
            BoundNull => expression,
            BoundOptionalLift lift => lift with {
                Type = RemapType(lift.Type, source, destination),
                Value = RemapExpression(lift.Value, source, destination),
            },
            BoundIsNull test => test with { Value = RemapExpression(test.Value, source, destination) },
            BoundIsSome pattern => pattern with { Value = RemapExpression(pattern.Value, source, destination) },
            BoundFieldRead field => field with {
                Type = RemapType(field.Type, source, destination),
                Receiver = RemapExpression(field.Receiver, source, destination),
            },
            BoundOutArgument output => output with { Type = RemapType(output.Type, source, destination) },
            BoundCall call => call with {
                Type = RemapType(call.Type, source, destination),
                Arguments = RemapExpressions(call.Arguments, source, destination),
            },
            SymbolicCall call => call with {
                Type = RemapType(call.Type, source, destination),
                Arguments = RemapExpressions(call.Arguments, source, destination),
            },
            _ => throw new ModuleFormatException("Unknown producer expression kind."),
        };
    }

    private static ImmutableArray<BoundExpression> RemapExpressions(ImmutableArray<BoundExpression> expressions,
        ImmutableArray<BoundStruct> source, ImmutableArray<BoundStruct> destination)
    {
        var result = ImmutableArray.CreateBuilder<BoundExpression>(expressions.Length);
        foreach (var expression in expressions)
        {
            result.Add(RemapExpression(expression, source, destination));
        }
        return result.MoveToImmutable();
    }

    private static BoundStatement RemapStatement(BoundStatement statement, ImmutableArray<BoundStruct> source,
        ImmutableArray<BoundStruct> destination)
    {
        return statement switch {
            BoundBlock block => block with { Statements = RemapStatements(block.Statements, source, destination) },
            BoundLocal local => local with { Initializer = RemapExpression(local.Initializer, source, destination) },
            BoundOwnerDeclaration => statement,
            BoundAssignment assignment => assignment with { Value = RemapExpression(assignment.Value, source, destination) },
            BoundFieldAssignment field => field with { Value = RemapExpression(field.Value, source, destination) },
            BoundExpressionStatement expression => expression with {
                Expression = RemapExpression(expression.Expression, source, destination),
            },
            BoundIf conditional => conditional with {
                Condition = RemapExpression(conditional.Condition, source, destination),
                Then = RemapStatement(conditional.Then, source, destination),
                Else = conditional.Else is null ? null : RemapStatement(conditional.Else, source, destination),
            },
            BoundWhile loop => loop with {
                Condition = RemapExpression(loop.Condition, source, destination),
                Body = RemapStatement(loop.Body, source, destination),
            },
            BoundTryFinally cleanup => cleanup with {
                Body = (BoundBlock)RemapStatement(cleanup.Body, source, destination),
                Finally = (BoundBlock)RemapStatement(cleanup.Finally, source, destination),
            },
            BoundViewScope view => view with {
                Owner = RemapExpression(view.Owner, source, destination),
                Start = RemapExpression(view.Start, source, destination),
                Length = RemapExpression(view.Length, source, destination),
                Body = (BoundBlock)RemapStatement(view.Body, source, destination),
            },
            BoundViewSet write => write with {
                Index = RemapExpression(write.Index, source, destination),
                Value = RemapExpression(write.Value, source, destination),
            },
            BoundDrop => statement,
            BoundDropConstructedFields => statement,
            SymbolicDrop => statement,
            BoundUnsafe unsafeBlock => unsafeBlock with {
                Body = (BoundBlock)RemapStatement(unsafeBlock.Body, source, destination),
            },
            BoundTryCatch recovery => recovery with {
                Body = (BoundBlock)RemapStatement(recovery.Body, source, destination),
                Catch = (BoundBlock)RemapStatement(recovery.Catch, source, destination),
                Finally = recovery.Finally is null ? null
                    : (BoundBlock)RemapStatement(recovery.Finally, source, destination),
            },
            BoundRequire requirement => requirement with {
                Condition = RemapExpression(requirement.Condition, source, destination),
            },
            BoundReturn result => result with {
                Value = result.Value is null ? null : RemapExpression(result.Value, source, destination),
            },
            _ => throw new ModuleFormatException("Unknown producer statement kind."),
        };
    }

    private static ImmutableArray<BoundStatement> RemapStatements(ImmutableArray<BoundStatement> statements,
        ImmutableArray<BoundStruct> source, ImmutableArray<BoundStruct> destination)
    {
        var result = ImmutableArray.CreateBuilder<BoundStatement>(statements.Length);
        foreach (var statement in statements)
        {
            result.Add(RemapStatement(statement, source, destination));
        }
        return result.MoveToImmutable();
    }

    private static byte[] Write(Module module, BoundProgram program)
    {
        if (module.Consumer)
        {
            RejectManagedNodes(program);
        }
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        var version = module.Classes.IsEmpty ? ManagedNodeVersion : Version;
        writer.Write(Magic);
        writer.Write(version);
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
        if (version >= Version)
        {
            writer.Write(module.Classes.Length);
            foreach (var @class in module.Classes)
            {
                PutString(writer, @class.Name);
                writer.Write(@class.Size);
                writer.Write(@class.IsInternal);
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
                PutFunction(writer, function, names, ref remaining, version);
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
            var (version, policy, profile) = ReadModuleHeader(reader, expectedConsumer);
            var entry = reader.ReadInt32();
            var (structures, structureNames) = ReadStructures(reader, version);
            var classes = version >= Version ? ReadClasses(reader, structureNames) : [];
            if (version >= Version && classes.IsEmpty)
            {
                throw new ModuleFormatException("Class module version requires private managed layouts.");
            }
            var importedTypes = ReadImportedTypes(reader, structures.Length, structureNames, expectedConsumer);
            var count = Count(reader, MaximumFunctions);
            if (count == 0 || (expectedConsumer ? entry < 0 || entry >= count : entry != -1))
            {
                throw new ModuleFormatException("Module entry point is invalid.");
            }
            var items = ReadFunctions(reader, count, expectedConsumer, version, structures, classes);
            var module = new Module(expectedConsumer, entry, structures, items) {
                Classes = classes,
                ImportedStructures = importedTypes,
                RuntimePolicy = policy,
                SerializedVersion = version,
                SerializedProfile = profile,
            };
            if (profile != RetainedNativeProfile(module))
            {
                throw new ModuleFormatException("Module semantic profile does not match its retained contracts.");
            }
            if (profile == "native-20" && classes.IsEmpty)
            {
                throw new ModuleFormatException("Optional managed nodes require a private source-class leaf.");
            }
            if (HasPrivateManagedState(module) && (expectedConsumer || module.Functions.Any(static item =>
                    item.Import && (item.Function is not BoundFunction function || !ScalarManagedExport(function)))))
            {
                throw new ModuleFormatException("Managed leaf references require scalar-only exports.");
            }
            if (stream.Position != stream.Length || (expectedConsumer && items[entry].Import))
            {
                throw new ModuleFormatException("Module has trailing data or an imported entry point.");
            }
            if (!expectedConsumer && version >= Version && HasPrivateManagedState(module))
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

    private static (int Version, BoundRuntimePolicy Policy, string Profile) ReadModuleHeader(BinaryReader reader, bool expectedConsumer)
    {
        if (reader.ReadInt32() != Magic)
        {
            throw new ModuleFormatException("Unknown module format or version.");
        }
        var version = reader.ReadInt32();
        if (version is not (10 or 11 or OwnershipVersion or TransferVersion or CompositeVersion
            or BufferVersion or ReadonlyViewVersion or WritableViewVersion or ManagedNodeVersion or Version))
        {
            throw new ModuleFormatException("Unknown module format or version.");
        }
        var profile = GetString(reader);
        if (GetBool(reader) != expectedConsumer)
        {
            throw new ModuleFormatException("Unexpected module kind.");
        }
        var policy = version == 10 ? BoundRuntimePolicy.FailFast : (BoundRuntimePolicy)reader.ReadByte();
        if (policy is not (BoundRuntimePolicy.FailFast or BoundRuntimePolicy.CheckedExceptions)
            || (profile != Compilation.GetNativeProfileVersion(policy, uniqueValues: false)
                && (version < OwnershipVersion || profile != "native-12")
                && (version < TransferVersion || profile != "native-13")
                && (version < CompositeVersion || profile != "native-14")
                && (version < BufferVersion || profile != "native-15")
                && (version < ReadonlyViewVersion || profile != "native-16")
                && (version < WritableViewVersion || profile != "native-17")
                && (version < ManagedNodeVersion || profile != "native-18")
                && (version < Version || profile is not ("native-19" or "native-20"))))
        {
            throw new ModuleFormatException("Unsupported module semantic profile or checked-failure policy.");
        }
        return (version, policy, profile);
    }

    private static (ImmutableArray<BoundStruct> Structures, HashSet<string> Names) ReadStructures(BinaryReader reader, int version)
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
            var isUnique = version >= OwnershipVersion && GetBool(reader);
            var cleanup = version >= OwnershipVersion && GetBool(reader) ? GetString(reader) : null;
            var fieldCount = Count(reader, MaximumSlots);
            var fields = ImmutableArray.CreateBuilder<BoundStructField>(fieldCount);
            for (var field = 0; field < fieldCount; field++)
            {
                var fieldName = GetString(reader);
                var fieldType = (PrimitiveType)reader.ReadInt32();
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
            var fieldCount = Count(reader, 16);
            var fields = ImmutableArray.CreateBuilder<BoundStructField>(fieldCount);
            for (var field = 0; field < fieldCount; field++)
            {
                var fieldName = GetString(reader);
                var fieldType = (PrimitiveType)reader.ReadInt32();
                if (IsInlineClassField(fieldType))
                {
                    throw new ModuleFormatException("Inline struct class fields require a newer semantic module version.");
                }
                fields.Add(new BoundStructField(fieldName, fieldType,
                    reader.ReadInt32(), reader.ReadInt32()) {
                    IsPrivate = GetBool(reader),
                    IsInternal = GetBool(reader),
                    IsMutable = GetBool(reader),
                });
            }
            classes.Add(new BoundClass(name, fields.MoveToImmutable(), size) { IsInternal = isInternal });
        }
        return classes.MoveToImmutable();
    }

    private static ImmutableArray<ModuleFunction> ReadFunctions(BinaryReader reader, int count, bool expectedConsumer,
        int version, ImmutableArray<BoundStruct> structures, ImmutableArray<BoundClass> classes)
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
            var ownershipTransfer = version >= TransferVersion && GetBool(reader);
            if (name.Length == 0 || !seen.Add(name) || (expectedConsumer ? import == hasBody : !hasBody))
            {
                throw new ModuleFormatException("Invalid module declaration.");
            }
            var function = hasBody ? GetFunction(reader, ref remaining, version) : null;
            if (!expectedConsumer && import && function is BoundFunction exported
                && !Exportable(exported, structures))
            {
                throw new ModuleFormatException("Producer export has a nonpublic declaration or value layout.");
            }
            if (import && ContractContainsType(contract, PrimitiveType.ManagedNode))
            {
                throw new ModuleFormatException("Managed-node handles cannot cross a module declaration.");
            }
            if (ContractContainsOptionalType(contract) && (import || expectedConsumer))
            {
                throw new ModuleFormatException("Optional managed handles have no persisted module ABI.");
            }
            if (expectedConsumer && function is BoundFunction body && (UsesManagedNode(body)
                || ManagedClassTypes.IsManaged(body.ReturnType)
                || body.Variables.Any(static variable => ManagedClassTypes.IsManaged(variable.Type))))
            {
                throw new ModuleFormatException("Managed bodies require an image-private leaf.");
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
        if (classes.Length != 0 && expectedConsumer)
        {
            throw new ModuleFormatException("Managed classes cannot be imported by consumers.");
        }
        return items.ToImmutable();
    }

    private static void PutFunction(BinaryWriter writer, BoundFunction function, string[] names, ref int remaining,
        int version)
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
        writer.Write((byte)function.Intrinsic);
        if (version >= Version)
        {
            writer.Write(function.IsClassConstructor);
            writer.Write(function.ClassConstructor);
        }
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
            PutSpan(writer, variable.Span);
        }
        PutStatement(writer, function.Body, names, ref remaining, 0);
    }

    private static BoundFunction GetFunction(BinaryReader reader, ref int remaining, int version)
    {
        var name = GetString(reader);
        var type = (PrimitiveType)reader.ReadInt32();
        if (version < Version && (ManagedClassTypes.IsOptional(type) || ManagedClassTypes.IsClass(type)))
        {
            throw new ModuleFormatException("Optional managed handles have no persisted module ABI.");
        }
        if (version < ManagedNodeVersion && type == PrimitiveType.ManagedNode)
        {
            throw new ModuleFormatException("Managed nodes have no persisted module ABI.");
        }
        if ((version < ReadonlyViewVersion && type == PrimitiveType.ReadonlyInt32BufferView)
            || (version < WritableViewVersion && type == PrimitiveType.MutableInt32BufferView))
        {
            throw new ModuleFormatException("Native buffer view type requires a newer semantic module version.");
        }
        var constructor = GetBool(reader);
        var isPrivate = GetBool(reader);
        var isInternal = GetBool(reader);
        var isStatic = GetBool(reader);
        var isNoThrow = GetBool(reader);
        var doesNotReturn = GetBool(reader);
        var isCleanup = version >= OwnershipVersion && GetBool(reader);
        var intrinsic = version >= BufferVersion ? (BoundIntrinsic)reader.ReadByte() : BoundIntrinsic.None;
        var classConstructor = version >= Version && GetBool(reader);
        var classFactoryConstructor = version >= Version ? reader.ReadInt32() : -1;
        if (intrinsic >= BoundIntrinsic.NodeTryCreate
            && (version < ManagedNodeVersion || (version < Version && intrinsic > BoundIntrinsic.NodeCollect)
                || intrinsic > BoundIntrinsic.ClassTryCreate))
        {
            throw new ModuleFormatException("Managed nodes have no persisted module runtime.");
        }
        if (version < WritableViewVersion && intrinsic == BoundIntrinsic.BufferClear)
        {
            throw new ModuleFormatException("Buffer.Clear requires semantic module version 17.");
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
            var variableType = (PrimitiveType)reader.ReadInt32();
            if (version < Version && (ManagedClassTypes.IsOptional(variableType)
                || ManagedClassTypes.IsClass(variableType)))
            {
                throw new ModuleFormatException("Optional managed handles have no persisted module ABI.");
            }
            if (version < ManagedNodeVersion && variableType == PrimitiveType.ManagedNode)
            {
                throw new ModuleFormatException("Managed nodes have no persisted module ABI.");
            }
            if ((version < ReadonlyViewVersion && variableType == PrimitiveType.ReadonlyInt32BufferView)
                || (version < WritableViewVersion && variableType == PrimitiveType.MutableInt32BufferView))
            {
                throw new ModuleFormatException("Native buffer view type requires a newer semantic module version.");
            }
            var mutable = GetBool(reader);
            var isOut = GetBool(reader);
            variables.Add(new BoundVariable(variableName, variableType, mutable, GetSpan(reader)) { IsOut = isOut });
        }
        var body = GetStatement(reader, ref remaining, 0, version) as BoundBlock
            ?? throw new ModuleFormatException("Function requires a block body.");
        return new BoundFunction(name, type, variables.ToImmutable(), parameters, body, span) {
            IsConstructor = constructor,
            IsPrivate = isPrivate,
            IsInternal = isInternal,
            IsStatic = isStatic,
            IsNoThrow = isNoThrow,
            DoesNotReturn = doesNotReturn,
            IsCleanup = isCleanup,
            Intrinsic = intrinsic,
            DeclaringStruct = declaringStruct,
            IsClassConstructor = classConstructor,
            ClassConstructor = classFactoryConstructor,
        };
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "The preceding tag selection rejects unsupported statement kinds.")]
    private static void PutStatement(BinaryWriter writer, BoundStatement statement, string[] names, ref int remaining, int depth)
    {
        Step(ref remaining, depth);
        writer.Write(statement switch {
            BoundBlock => (byte)1,
            BoundLocal => (byte)2,
            BoundAssignment => (byte)3,
            BoundExpressionStatement => (byte)4,
            BoundIf => (byte)5,
            BoundWhile => (byte)6,
            BoundRequire => (byte)7,
            BoundReturn => (byte)8,
            BoundFieldAssignment => (byte)9,
            BoundTryFinally => (byte)10,
            BoundUnsafe => (byte)11,
            BoundTryCatch => (byte)12,
            BoundDrop => (byte)13,
            BoundOwnerDeclaration => (byte)14,
            BoundDropConstructedFields => (byte)15,
            BoundViewScope => (byte)16,
            BoundViewSet => (byte)17,
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
    private static BoundStatement GetStatement(BinaryReader reader, ref int remaining, int depth, int version)
    {
        Step(ref remaining, depth);
        var kind = reader.ReadByte();
        if (version == 10 && kind > 10)
        {
            throw new ModuleFormatException("Unknown legacy statement kind.");
        }
        var span = GetSpan(reader);
        switch (kind)
        {
            case 1:
                var managedScope = version >= ManagedNodeVersion && GetBool(reader);
                var retired = ImmutableArray<int>.Empty;
                if (version >= ManagedNodeVersion)
                {
                    var retiredCount = Count(reader, MaximumSlots);
                    var slots = ImmutableArray.CreateBuilder<int>(retiredCount);
                    for (var i = 0; i < retiredCount; i++)
                    {
                        Step(ref remaining, depth + 1);
                        slots.Add(reader.ReadInt32());
                    }
                    retired = slots.MoveToImmutable();
                }
                var count = Count(reader, MaximumNodes);
                var statements = ImmutableArray.CreateBuilder<BoundStatement>(count);
                for (var i = 0; i < count; i++)
                {
                    statements.Add(GetStatement(reader, ref remaining, depth + 1, version));
                }
                return new BoundBlock(statements.ToImmutable(), span) {
                    IsManagedLexicalScope = managedScope,
                    RetiredManagedLocals = retired,
                };
            case 2:
                return new BoundLocal(reader.ReadInt32(), GetExpression(reader, ref remaining, depth + 1, version), span);
            case 3:
                return new BoundAssignment(reader.ReadInt32(), GetExpression(reader, ref remaining, depth + 1, version), span);
            case 4:
                return new BoundExpressionStatement(GetExpression(reader, ref remaining, depth + 1, version), span);
            case 5:
                var condition = GetExpression(reader, ref remaining, depth + 1, version);
                var then = GetStatement(reader, ref remaining, depth + 1, version);
                var otherwise = GetBool(reader) ? GetStatement(reader, ref remaining, depth + 1, version) : null;
                return new BoundIf(condition, then, otherwise, span);
            case 6:
                return new BoundWhile(GetExpression(reader, ref remaining, depth + 1, version),
                    GetStatement(reader, ref remaining, depth + 1, version), span);
            case 7:
                return new BoundRequire(GetExpression(reader, ref remaining, depth + 1, version), span);
            case 8:
                return new BoundReturn(GetBool(reader) ? GetExpression(reader, ref remaining, depth + 1, version) : null, span);
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
                return new BoundFieldAssignment(variable, field, GetExpression(reader, ref remaining, depth + 1, version), span) {
                    NestedFields = fields.ToImmutable(),
                };
            case 10:
                return new BoundTryFinally(GetStatement(reader, ref remaining, depth + 1, version) as BoundBlock
                    ?? throw new ModuleFormatException("Expected try block."),
                    GetStatement(reader, ref remaining, depth + 1, version) as BoundBlock
                    ?? throw new ModuleFormatException("Expected finally block."), span);
            case 11:
                return new BoundUnsafe(GetStatement(reader, ref remaining, depth + 1, version) as BoundBlock
                    ?? throw new ModuleFormatException("Expected unsafe block."), span);
            case 12:
                var body = GetStatement(reader, ref remaining, depth + 1, version) as BoundBlock
                    ?? throw new ModuleFormatException("Expected protected block.");
                var handler = GetStatement(reader, ref remaining, depth + 1, version) as BoundBlock
                    ?? throw new ModuleFormatException("Expected recovery block.");
                var cleanup = GetBool(reader) ? GetStatement(reader, ref remaining, depth + 1, version) as BoundBlock
                    ?? throw new ModuleFormatException("Expected recovery cleanup block.") : null;
                return new BoundTryCatch(body, handler, cleanup, span);
            case 13:
                var owner = reader.ReadInt32();
                var method = GetBool(reader) ? GetString(reader) : null;
                return new SymbolicDrop(owner, method, span) {
                    IsOutput = version >= TransferVersion && GetBool(reader),
                };
            case 14 when version >= TransferVersion:
                return new BoundOwnerDeclaration(reader.ReadInt32(), span);
            case 15 when version >= CompositeVersion:
                var constructedOwner = reader.ReadInt32();
                var acquired = Count(reader, ValueLayout.MaximumSize);
                var order = ImmutableArray.CreateBuilder<int>(acquired);
                for (var i = 0; i < acquired; i++)
                {
                    Step(ref remaining, depth + 1);
                    order.Add(reader.ReadInt32());
                }
                return new BoundDropConstructedFields(constructedOwner, order.ToImmutable(), span);
            case 16 when version >= ReadonlyViewVersion:
                var viewVariable = reader.ReadInt32();
                var mutable = version >= WritableViewVersion && GetBool(reader);
                var viewOwner = GetExpression(reader, ref remaining, depth + 1, version);
                var start = GetExpression(reader, ref remaining, depth + 1, version);
                var length = GetExpression(reader, ref remaining, depth + 1, version);
                var viewBody = GetStatement(reader, ref remaining, depth + 1, version) as BoundBlock
                    ?? throw new ModuleFormatException("Expected lexical view block.");
                return new BoundViewScope(viewVariable, viewOwner, start, length, viewBody, span) {
                    IsMutable = mutable,
                };
            case 17 when version >= WritableViewVersion:
                return new BoundViewSet(reader.ReadInt32(), GetExpression(reader, ref remaining, depth + 1, version),
                    GetExpression(reader, ref remaining, depth + 1, version), span);
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
            BoundBinary => (byte)4,
            BoundCall => (byte)5,
            BoundCast => (byte)6,
            BoundFieldRead => (byte)7,
            BoundOutArgument => (byte)8,
            BoundMove => (byte)9,
            BoundReceiverLoan => (byte)10,
            BoundFieldLoan => (byte)11,
            BoundMutableReceiverLoan => (byte)12,
            BoundViewLength => (byte)13,
            BoundViewRead => (byte)14,
            BoundNull => (byte)15,
            BoundOptionalLift => (byte)16,
            BoundIsNull => (byte)17,
            BoundIsSome => (byte)18,
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
            case BoundNull:
                break;
            case BoundOptionalLift lift:
                PutExpression(writer, lift.Value, names, ref remaining, depth + 1);
                break;
            case BoundIsNull test:
                PutExpression(writer, test.Value, names, ref remaining, depth + 1);
                break;
            case BoundIsSome pattern:
                writer.Write(pattern.Variable);
                PutExpression(writer, pattern.Value, names, ref remaining, depth + 1);
                break;
            case BoundUnary unary:
                writer.Write((int)unary.Operator);
                PutExpression(writer, unary.Operand, names, ref remaining, depth + 1);
                break;
            case BoundBinary binary:
                writer.Write((int)binary.Operator);
                PutExpression(writer, binary.Left, names, ref remaining, depth + 1);
                PutExpression(writer, binary.Right, names, ref remaining, depth + 1);
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
    private static BoundExpression GetExpression(BinaryReader reader, ref int remaining, int depth, int version)
    {
        Step(ref remaining, depth);
        var kind = reader.ReadByte();
        var type = (PrimitiveType)reader.ReadInt32();
        if (version < Version && (ManagedClassTypes.IsOptional(type) || ManagedClassTypes.IsClass(type)))
        {
            throw new ModuleFormatException("Optional managed handles have no persisted module ABI.");
        }
        if (version < ManagedNodeVersion && type == PrimitiveType.ManagedNode)
        {
            throw new ModuleFormatException("Managed-node expressions require semantic module version 18.");
        }
        var span = GetSpan(reader);
        switch (kind)
        {
            case 1:
                return new BoundConstant(reader.ReadInt64(), type, span);
            case 2:
                return new BoundRead(reader.ReadInt32(), type, span);
            case 3:
                return new BoundUnary((TokenKind)reader.ReadInt32(), GetExpression(reader, ref remaining, depth + 1, version), type, span);
            case 4:
                var op = (TokenKind)reader.ReadInt32();
                var left = GetExpression(reader, ref remaining, depth + 1, version);
                return new BoundBinary(op, left, GetExpression(reader, ref remaining, depth + 1, version), type, span);
            case 5:
                // Names are resolved after the complete module graph is read.
                var name = GetString(reader);
                var count = Count(reader, 64);
                var args = ImmutableArray.CreateBuilder<BoundExpression>(count);
                for (var i = 0; i < count; i++)
                {
                    args.Add(GetExpression(reader, ref remaining, depth + 1, version));
                }
                return new SymbolicCall(name, args.ToImmutable(), type, span);
            case 6:
                return new BoundCast(GetExpression(reader, ref remaining, depth + 1, version), type, span);
            case 7:
                var field = reader.ReadInt32();
                return new BoundFieldRead(GetExpression(reader, ref remaining, depth + 1, version), field, type, span);
            case 8:
                return new BoundOutArgument(reader.ReadInt32(), GetBool(reader), type, span);
            case 9:
                return new BoundMove(reader.ReadInt32(), type, span);
            case 10:
                return new BoundReceiverLoan(reader.ReadInt32(), type, span);
            case 11 when version >= CompositeVersion:
                return new BoundFieldLoan(GetExpression(reader, ref remaining, depth + 1, version)
                    as BoundFieldRead ?? throw new ModuleFormatException("Expected owning field place."), type, span);
            case 12 when version >= BufferVersion:
                return new BoundMutableReceiverLoan(reader.ReadInt32(), type, span);
            case 13 when version >= ReadonlyViewVersion && type == PrimitiveType.Int32:
                return new BoundViewLength(reader.ReadInt32(), span);
            case 14 when version >= ReadonlyViewVersion && type == PrimitiveType.Int32:
                var variable = reader.ReadInt32();
                return new BoundViewRead(variable, GetExpression(reader, ref remaining, depth + 1, version), span);
            case 15 when version >= Version && ManagedClassTypes.IsOptional(type):
                return new BoundNull(type, span);
            case 16 when version >= Version && ManagedClassTypes.IsOptional(type):
                return new BoundOptionalLift(GetExpression(reader, ref remaining, depth + 1, version), type, span);
            case 17 when version >= Version && type == PrimitiveType.Bool:
                return new BoundIsNull(GetExpression(reader, ref remaining, depth + 1, version), span);
            case 18 when version >= Version && type == PrimitiveType.Bool:
                var pattern = reader.ReadInt32();
                return new BoundIsSome(GetExpression(reader, ref remaining, depth + 1, version), pattern, span);
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
        BoundOwnerDeclaration => statement,
        BoundAssignment assignment => assignment with { Value = ResolveExpression(assignment.Value, names) },
        BoundFieldAssignment field => field with { Value = ResolveExpression(field.Value, names) },
        BoundExpressionStatement expression => expression with { Expression = ResolveExpression(expression.Expression, names) },
        BoundIf conditional => conditional with {
            Condition = ResolveExpression(conditional.Condition, names),
            Then = ResolveStatement(conditional.Then, names),
            Else = conditional.Else is null ? null : ResolveStatement(conditional.Else, names),
        },
        BoundWhile loop => loop with { Condition = ResolveExpression(loop.Condition, names), Body = ResolveStatement(loop.Body, names) },
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
        BoundBinary binary => binary with { Left = ResolveExpression(binary.Left, names), Right = ResolveExpression(binary.Right, names) },
        BoundCast cast => cast with { Operand = ResolveExpression(cast.Operand, names) },
        BoundOptionalLift lift => lift with { Value = ResolveExpression(lift.Value, names) },
        BoundIsNull test => test with { Value = ResolveExpression(test.Value, names) },
        BoundIsSome pattern => pattern with { Value = ResolveExpression(pattern.Value, names) },
        BoundFieldRead field => field with { Receiver = ResolveExpression(field.Receiver, names) },
        BoundFieldLoan loan => loan with { Place = (BoundFieldRead)ResolveExpression(loan.Place, names) },
        BoundViewRead view => view with { Index = ResolveExpression(view.Index, names) },
        _ => expression,
    };

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
        public int SerializedVersion { get; init; }
        public string? SerializedProfile { get; init; }
    }
    private sealed record ModuleFunction(string Name, string Contract, BoundFunction? Function, bool Import)
    {
        public bool HasOwnershipTransfer { get; init; }
    }
    private sealed record SymbolicCall(string Name, ImmutableArray<BoundExpression> Arguments, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);
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
