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
public static class ModuleCompiler
{
    private const int Magic = 0x4D525254; // TRRM
    private const int Version = 2;
    private const int MaximumBytes = 4 * 1024 * 1024;
    private const int MaximumFunctions = 1024;
    private const int MaximumSlots = 256;
    private const int MaximumNodes = 100_000;
    private const int MaximumDepth = 128;
    private const int MaximumStringBytes = 1024;

    /// <summary>Creates an independently stored producer from named functions in a verified compilation.</summary>
    public static byte[] CreateProducer(BoundProgram program, IEnumerable<string> exports)
    {
        ArgumentNullException.ThrowIfNull(exports);
        SemanticVerifier.Verify(program);
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
        if (indexes.Any(index => !Exportable(program.Functions[index], program.Structures)))
        {
            throw new ModuleContractException("Producer exports must have public declarations and public value layouts.");
        }
        var pending = new Queue<int>(indexes);
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
        }
        var functions = program.Functions.Where((_, index) => indexes.Contains(index));
        var module = new Module(false, -1, program.Structures, [.. functions.Select(function =>
            new ModuleFunction(function.Name, Contract(function, program.Structures), function, names.Contains(function.Name)))]);
        return Write(module, program);
    }

    /// <summary>Stores consumer bound bodies and imported declaration contracts, but never producer bodies.</summary>
    public static byte[] CreateConsumer(BoundProgram program, byte[] referenceProducer)
    {
        ArgumentNullException.ThrowIfNull(referenceProducer);
        SemanticVerifier.Verify(program);
        if (!program.Buffers.IsDefaultOrEmpty)
        {
            throw new ModuleFormatException("Host buffers are not serializable module dependencies.");
        }
        var producer = Read(referenceProducer, expectedConsumer: false);
        if (!SameStructures(program.Structures, producer.Structures))
        {
            throw new ModuleContractException("Reference type layouts differ from the producer.");
        }
        var exports = producer.Functions.Where(static function => function.Import)
            .ToDictionary(function => function.Name, StringComparer.Ordinal);
        var implementationFunctions = producer.Functions.Where(static function => !function.Import)
            .Select(function => function.Name).ToHashSet(StringComparer.Ordinal);
        var declarations = ImmutableArray.CreateBuilder<ModuleFunction>();
        for (var i = 0; i < program.Functions.Length; i++)
        {
            var function = program.Functions[i];
            if (implementationFunctions.Contains(function.Name))
            {
                continue;
            }
            if (exports.TryGetValue(function.Name, out var imported))
            {
                if (!Exportable(function, program.Structures))
                {
                    throw new ModuleContractException($"Declaration '{function.Name}' is not a public module import.");
                }
                if (Contract(function, program.Structures) != imported.Contract)
                {
                    throw new ModuleContractException($"Reference declaration for '{function.Name}' differs from the producer.");
                }
                declarations.Add(new ModuleFunction(function.Name, imported.Contract, null, true));
            }
            else
            {
                foreach (var callee in Calls(function.Body))
                {
                    if (implementationFunctions.Contains(program.Functions[callee].Name))
                    {
                        throw new ModuleContractException("Consumer cannot call an unexported producer implementation.");
                    }
                }
                declarations.Add(new ModuleFunction(function.Name, Contract(function, program.Structures), function, false));
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
        return Write(new Module(true, entryPoint, program.Structures, declarations.ToImmutable()), program);
    }

    /// <summary>Resolves symbolic calls against a separately supplied producer, checks contracts and verifies the complete linked IR.</summary>
    public static BoundProgram Link(byte[] consumerBytes, byte[] producerBytes)
    {
        var consumer = Read(consumerBytes, expectedConsumer: true);
        var producer = Read(producerBytes, expectedConsumer: false);
        if (!SameStructures(consumer.Structures, producer.Structures))
        {
            throw new ModuleContractException("Incompatible producer type layout.");
        }
        var available = producer.Functions.Where(static function => function.Import)
            .ToDictionary(function => function.Name, StringComparer.Ordinal);
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        var functions = ImmutableArray.CreateBuilder<BoundFunction>();
        var consumerOwned = new List<int>();
        var producerOnly = new HashSet<int>();
        foreach (var declaration in consumer.Functions)
        {
            if (!names.TryAdd(declaration.Name, functions.Count))
            {
                throw new ModuleFormatException("Duplicate consumer symbol.");
            }
            if (declaration.Import)
            {
                if (!available.TryGetValue(declaration.Name, out var replacement)
                    || declaration.Contract != replacement.Contract)
                {
                    throw new ModuleContractException($"Incompatible or missing producer contract for '{declaration.Name}'.");
                }
                functions.Add(RemapFunction(replacement.Function
                    ?? throw new ModuleFormatException("Producer has an unowned function."), producer.Structures, consumer.Structures));
            }
            else
            {
                consumerOwned.Add(functions.Count);
                functions.Add(declaration.Function ?? throw new ModuleFormatException("Consumer has an unowned function."));
            }
        }
        foreach (var exported in producer.Functions)
        {
            if (!exported.Import)
            {
                if (!names.TryAdd(exported.Name, functions.Count))
                {
                    throw new ModuleFormatException("Unexported producer function conflicts with a consumer symbol.");
                }
                _ = producerOnly.Add(functions.Count);
                functions.Add(RemapFunction(exported.Function
                    ?? throw new ModuleFormatException("Missing private producer body."), producer.Structures, consumer.Structures));
            }
            else if (!names.ContainsKey(exported.Name))
            {
                throw new ModuleContractException($"Producer export '{exported.Name}' was not declared by the consumer.");
            }
        }
        var result = Resolve(new BoundProgram(functions.ToImmutable(), consumer.EntryPoint) { Structures = consumer.Structures }, names);
        foreach (var owner in consumerOwned)
        {
            foreach (var callee in Calls(result.Functions[owner].Body))
            {
                if (producerOnly.Contains(callee))
                {
                    throw new ModuleContractException("Consumer cannot call an unexported producer implementation.");
                }
            }
        }
        try
        {
            SemanticVerifier.Verify(result);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new ModuleFormatException("Linked module IR failed semantic verification.", exception);
        }
        return result;
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Only nodes with nested expressions or statements need traversal.")]
    private static List<int> Calls(BoundStatement body)
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
                case BoundFieldRead field:
                    VisitExpression(field.Receiver);
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
        return result.ToString();
    }

    private static char Visibility(BoundStruct structure) =>
        structure.IsPrivate ? 'P' : structure.IsInternal ? 'N' : 'E';

    private static string ContractType(PrimitiveType type, ImmutableArray<BoundStruct> structures)
    {
        var index = (int)type - 100;
        return index >= 0 && index < structures.Length
            ? structures[index].Name + ":" + Visibility(structures[index])
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
        if (function.DeclaringStruct is string declaring
            && !structures.Any(structure => structure.Name == declaring && !structure.IsPrivate && !structure.IsInternal))
        {
            return false;
        }
        bool PublicValue(PrimitiveType type)
        {
            var index = (int)type - 100;
            if (index < 0)
            {
                return true;
            }
            if (index >= structures.Length)
            {
                return false;
            }
            var structure = structures[index];
            return !structure.IsPrivate && !structure.IsInternal
                && structure.Fields.All(static field => !field.IsPrivate && !field.IsInternal);
        }
        if (!PublicValue(function.ReturnType))
        {
            return false;
        }
        for (var index = 0; index < function.ParameterCount; index++)
        {
            if (!PublicValue(function.Variables[index + parameterOffset].Type))
            {
                return false;
            }
        }
        return true;
    }

    private static bool SameStructures(ImmutableArray<BoundStruct> left, ImmutableArray<BoundStruct> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }
        var names = right.ToDictionary(structure => structure.Name, StringComparer.Ordinal);
        foreach (var a in left)
        {
            if (!names.TryGetValue(a.Name, out var b) || a.Size != b.Size || a.Fields.Length != b.Fields.Length
                || a.IsPrivate != b.IsPrivate || a.IsInternal != b.IsInternal)
            {
                return false;
            }
            for (var field = 0; field < a.Fields.Length; field++)
            {
                var x = a.Fields[field];
                var y = b.Fields[field];
                if (x.Name != y.Name || x.IsPrivate != y.IsPrivate || x.IsInternal != y.IsInternal
                    || TypeName(x.Type, left) != TypeName(y.Type, right)
                    || x.Size != y.Size || x.Offset != y.Offset)
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static BoundFunction RemapFunction(BoundFunction function, ImmutableArray<BoundStruct> source,
        ImmutableArray<BoundStruct> destination)
    {
        PrimitiveType Type(PrimitiveType type)
        {
            var index = (int)type - 100;
            if (index < 0)
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
        BoundExpression Expression(BoundExpression expression)
        {
            return expression switch {
                BoundConstant constant => constant with { Type = Type(constant.Type) },
                BoundRead read => read with { Type = Type(read.Type) },
                BoundUnary unary => unary with { Type = Type(unary.Type), Operand = Expression(unary.Operand) },
                BoundBinary binary => binary with { Type = Type(binary.Type), Left = Expression(binary.Left), Right = Expression(binary.Right) },
                BoundCast cast => cast with { Type = Type(cast.Type), Operand = Expression(cast.Operand) },
                BoundFieldRead field => field with { Type = Type(field.Type), Receiver = Expression(field.Receiver) },
                BoundOutArgument output => output with { Type = Type(output.Type) },
                SymbolicCall call => call with { Type = Type(call.Type), Arguments = [.. call.Arguments.Select(Expression)] },
                _ => throw new ModuleFormatException("Unknown producer expression kind."),
            };
        }

        BoundStatement Statement(BoundStatement statement)
        {
            return statement switch {
                BoundBlock block => block with { Statements = [.. block.Statements.Select(Statement)] },
                BoundLocal local => local with { Initializer = Expression(local.Initializer) },
                BoundAssignment assignment => assignment with { Value = Expression(assignment.Value) },
                BoundFieldAssignment field => field with { Value = Expression(field.Value) },
                BoundExpressionStatement expression => expression with { Expression = Expression(expression.Expression) },
                BoundIf conditional => conditional with {
                    Condition = Expression(conditional.Condition),
                    Then = Statement(conditional.Then),
                    Else = conditional.Else is null ? null : Statement(conditional.Else),
                },
                BoundWhile loop => loop with { Condition = Expression(loop.Condition), Body = Statement(loop.Body) },
                BoundRequire requirement => requirement with { Condition = Expression(requirement.Condition) },
                BoundReturn result => result with { Value = result.Value is null ? null : Expression(result.Value) },
                _ => throw new ModuleFormatException("Unknown producer statement kind."),
            };
        }

        return function with {
            ReturnType = Type(function.ReturnType),
            Variables = [.. function.Variables.Select(variable => variable with { Type = Type(variable.Type) })],
            Body = (BoundBlock)Statement(function.Body),
        };
    }

    private static byte[] Write(Module module, BoundProgram program)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        PutString(writer, Compilation.ProfileVersion);
        writer.Write(module.Consumer);
        writer.Write(module.EntryPoint);
        writer.Write(module.Structures.Length);
        foreach (var structure in module.Structures)
        {
            PutString(writer, structure.Name);
            writer.Write(structure.Size);
            writer.Write(structure.IsPrivate);
            writer.Write(structure.IsInternal);
            writer.Write(structure.Fields.Length);
            foreach (var field in structure.Fields)
            {
                PutString(writer, field.Name);
                writer.Write((int)field.Type);
                writer.Write(field.Offset);
                writer.Write(field.Size);
                writer.Write(field.IsPrivate);
                writer.Write(field.IsInternal);
            }
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
            if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version)
            {
                throw new ModuleFormatException("Unknown module format or version.");
            }
            if (GetString(reader) != Compilation.ProfileVersion)
            {
                throw new ModuleFormatException("Unsupported module semantic profile.");
            }
            if (GetBool(reader) != expectedConsumer)
            {
                throw new ModuleFormatException("Unexpected module kind.");
            }
            var entry = reader.ReadInt32();
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
                var fieldCount = Count(reader, MaximumSlots);
                var fields = ImmutableArray.CreateBuilder<BoundStructField>(fieldCount);
                for (var field = 0; field < fieldCount; field++)
                {
                    fields.Add(new BoundStructField(GetString(reader), (PrimitiveType)reader.ReadInt32(),
                        reader.ReadInt32(), reader.ReadInt32()) {
                        IsPrivate = GetBool(reader),
                        IsInternal = GetBool(reader),
                    });
                }
                structures.Add(new BoundStruct(name, fields.ToImmutable(), size) {
                    IsPrivate = isPrivate,
                    IsInternal = isInternal,
                });
            }
            var count = Count(reader, MaximumFunctions);
            if (count == 0 || (expectedConsumer ? entry < 0 || entry >= count : entry != -1))
            {
                throw new ModuleFormatException("Module entry point is invalid.");
            }
            var items = ImmutableArray.CreateBuilder<ModuleFunction>(count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var remaining = MaximumNodes;
            for (var i = 0; i < count; i++)
            {
                var name = GetString(reader);
                var contract = GetString(reader);
                var import = GetBool(reader);
                var hasBody = GetBool(reader);
                if (name.Length == 0 || !seen.Add(name) || (expectedConsumer ? import == hasBody : !hasBody))
                {
                    throw new ModuleFormatException("Invalid module declaration.");
                }
                var function = hasBody ? GetFunction(reader, ref remaining) : null;
                if (!expectedConsumer && import && function is BoundFunction exported
                    && !Exportable(exported, structures.ToImmutable()))
                {
                    throw new ModuleFormatException("Producer export has a nonpublic declaration or value layout.");
                }
                if (expectedConsumer && import
                    && (contract.Split('|') is not { Length: > 3 } parts || parts[3] != "E"))
                {
                    throw new ModuleFormatException("Consumer import lacks an accessible contract.");
                }
                if (function is not null && (function.Name != name || Contract(function, structures.ToImmutable()) != contract))
                {
                    throw new ModuleFormatException("Function body contradicts its declaration.");
                }
                items.Add(new ModuleFunction(name, contract, function, import));
            }
            if (stream.Position != stream.Length || (expectedConsumer && items[entry].Import))
            {
                throw new ModuleFormatException("Module has trailing data or an imported entry point.");
            }
            return new Module(expectedConsumer, entry, structures.ToImmutable(), items.ToImmutable());
        }
        catch (Exception exception) when (exception is EndOfStreamException or DecoderFallbackException or OverflowException or ArgumentOutOfRangeException or IOException)
        {
            throw new ModuleFormatException("Malformed module bytes.", exception);
        }
    }

    private static void PutFunction(BinaryWriter writer, BoundFunction function, string[] names, ref int remaining)
    {
        PutString(writer, function.Name);
        writer.Write((int)function.ReturnType);
        writer.Write(function.IsConstructor);
        writer.Write(function.IsPrivate);
        writer.Write(function.IsInternal);
        writer.Write(function.IsStatic);
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

    private static BoundFunction GetFunction(BinaryReader reader, ref int remaining)
    {
        var name = GetString(reader);
        var type = (PrimitiveType)reader.ReadInt32();
        var constructor = GetBool(reader);
        var isPrivate = GetBool(reader);
        var isInternal = GetBool(reader);
        var isStatic = GetBool(reader);
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
            var mutable = GetBool(reader);
            var isOut = GetBool(reader);
            variables.Add(new BoundVariable(variableName, variableType, mutable, GetSpan(reader)) { IsOut = isOut });
        }
        var body = GetStatement(reader, ref remaining, 0) as BoundBlock
            ?? throw new ModuleFormatException("Function requires a block body.");
        return new BoundFunction(name, type, variables.ToImmutable(), parameters, body, span) {
            IsConstructor = constructor,
            IsPrivate = isPrivate,
            IsInternal = isInternal,
            IsStatic = isStatic,
            DeclaringStruct = declaringStruct,
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
            _ => throw new ModuleFormatException("Unknown statement kind."),
        });
        PutSpan(writer, statement.Span);
        switch (statement)
        {
            case BoundBlock block:
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
            case BoundAssignment assignment:
                writer.Write(assignment.Variable);
                PutExpression(writer, assignment.Value, names, ref remaining, depth + 1);
                break;
            case BoundFieldAssignment field:
                writer.Write(field.Variable);
                writer.Write(field.Field);
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
                var count = Count(reader, MaximumNodes);
                var statements = ImmutableArray.CreateBuilder<BoundStatement>(count);
                for (var i = 0; i < count; i++)
                {
                    statements.Add(GetStatement(reader, ref remaining, depth + 1));
                }
                return new BoundBlock(statements.ToImmutable(), span);
            case 2:
                return new BoundLocal(reader.ReadInt32(), GetExpression(reader, ref remaining, depth + 1), span);
            case 3:
                return new BoundAssignment(reader.ReadInt32(), GetExpression(reader, ref remaining, depth + 1), span);
            case 4:
                return new BoundExpressionStatement(GetExpression(reader, ref remaining, depth + 1), span);
            case 5:
                var condition = GetExpression(reader, ref remaining, depth + 1);
                var then = GetStatement(reader, ref remaining, depth + 1);
                var otherwise = GetBool(reader) ? GetStatement(reader, ref remaining, depth + 1) : null;
                return new BoundIf(condition, then, otherwise, span);
            case 6:
                return new BoundWhile(GetExpression(reader, ref remaining, depth + 1), GetStatement(reader, ref remaining, depth + 1), span);
            case 7:
                return new BoundRequire(GetExpression(reader, ref remaining, depth + 1), span);
            case 8:
                return new BoundReturn(GetBool(reader) ? GetExpression(reader, ref remaining, depth + 1) : null, span);
            case 9:
                return new BoundFieldAssignment(reader.ReadInt32(), reader.ReadInt32(), GetExpression(reader, ref remaining, depth + 1), span);
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
    private static BoundExpression GetExpression(BinaryReader reader, ref int remaining, int depth)
    {
        Step(ref remaining, depth);
        var kind = reader.ReadByte();
        var type = (PrimitiveType)reader.ReadInt32();
        var span = GetSpan(reader);
        switch (kind)
        {
            case 1:
                return new BoundConstant(reader.ReadInt32(), type, span);
            case 2:
                return new BoundRead(reader.ReadInt32(), type, span);
            case 3:
                return new BoundUnary((TokenKind)reader.ReadInt32(), GetExpression(reader, ref remaining, depth + 1), type, span);
            case 4:
                var op = (TokenKind)reader.ReadInt32();
                var left = GetExpression(reader, ref remaining, depth + 1);
                return new BoundBinary(op, left, GetExpression(reader, ref remaining, depth + 1), type, span);
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
        BoundAssignment assignment => assignment with { Value = ResolveExpression(assignment.Value, names) },
        BoundFieldAssignment field => field with { Value = ResolveExpression(field.Value, names) },
        BoundExpressionStatement expression => expression with { Expression = ResolveExpression(expression.Expression, names) },
        BoundIf conditional => conditional with {
            Condition = ResolveExpression(conditional.Condition, names),
            Then = ResolveStatement(conditional.Then, names),
            Else = conditional.Else is null ? null : ResolveStatement(conditional.Else, names),
        },
        BoundWhile loop => loop with { Condition = ResolveExpression(loop.Condition, names), Body = ResolveStatement(loop.Body, names) },
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
        BoundFieldRead field => field with { Receiver = ResolveExpression(field.Receiver, names) },
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

    private sealed record Module(bool Consumer, int EntryPoint, ImmutableArray<BoundStruct> Structures, ImmutableArray<ModuleFunction> Functions);
    private sealed record ModuleFunction(string Name, string Contract, BoundFunction? Function, bool Import);
    private sealed record SymbolicCall(string Name, ImmutableArray<BoundExpression> Arguments, PrimitiveType Type, TextSpan Span) : BoundExpression(Type, Span);
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
