// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class PublicFixedLayoutStorageTests
{
    private const string Library = """
        public struct Leaf {
            public mut int32 Value;
            public Leaf(int32 value) { Value = value; }
        }
        public struct Packet {
            public char16 Prefix;
            public mut Leaf Item;
            public bool Enabled;
            public Packet(int32 value) {
                Prefix = char16(2); Item = new Leaf(value); Enabled = true;
            }
        }
        public Packet Make(int32 value) { return new Packet(value); }
        """;

    [Theory]
    [InlineData("bool", 1)]
    [InlineData("char16", 2)]
    [InlineData("int32", 4)]
    [InlineData("uint32", 4)]
    [InlineData("int64", 8)]
    [InlineData("uint64", 8)]
    [InlineData("float32", 4)]
    [InlineData("float64", 8)]
    public void EveryStorableCorePrimitiveRetainsAnExplicitProductionCommitment(string type, int width)
    {
        var program = Commit(CompileLibrary($"public struct Box {{ public {type} Value; }}"
            + " public int32 Check(Box value) { return 42; }"));
        Assert.Equal(width, Assert.Single(FixedLayoutStorage.Validate(program.Structures)));
        SemanticVerifier.VerifyLibrary(program);
        var producer = ModuleCompiler.CreateProducer(program, ["Check"]);
        Assert.Equal(78, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        var retained = Assert.Single(ModuleCompiler.CreateReference(producer).Structures);
        Assert.True(retained.IsSequential);
        Assert.Equal(FixedLayoutStorage.WindowsX64TargetRequirement, retained.BlittableTargetRequirement);
        Assert.Equal(width, retained.Size);
        Assert.Equal((0, width), (Assert.Single(retained.Fields).Offset, retained.Fields[0].Size));
    }

    [Theory]
    [InlineData("char16 Prefix; int64 Value; char16 Tail;", 24, 8, new[] { 0, 8, 16 })]
    [InlineData("uint64 A; uint64 B; uint64 C; uint64 D; uint64 E;", 40, 8, new[] { 0, 8, 16, 24, 32 })]
    public void LargerStorageIsEligibleWithoutWideningExecutableAggregates(
        string fields, int size, int alignment, int[] offsets)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(offsets);
        var types = fields.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var boundFields = ImmutableArray.CreateBuilder<BoundStructField>(types.Length);
        for (var index = 0; index < types.Length; index++)
        {
            var words = types[index].Split(' ');
            var type = words[0] switch {
                "char16" => PrimitiveType.Char16,
                "int64" => PrimitiveType.Int64,
                "uint64" => PrimitiveType.UInt64,
                _ => throw new InvalidOperationException(),
            };
            boundFields.Add(new BoundStructField(words[1], type, offsets[index], type == PrimitiveType.Char16 ? 2 : 8));
        }
        var large = new BoundStruct("Large", boundFields.MoveToImmutable(), size) {
            IsSequential = true, BlittableTargetRequirement = 1,
        };
        Assert.Equal(alignment, Assert.Single(FixedLayoutStorage.Validate([large])));
        var program = CompileLibrary("public int32 Check() { return 42; }") with { Structures = [large] };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(program));
        _ = Assert.Throws<ArgumentException>(() => ModuleCompiler.CreateProducer(program, ["Check"]));
    }

    [Theory]
    [InlineData("missing-root")]
    [InlineData("missing-nested")]
    [InlineData("wrong-target")]
    [InlineData("missing-sequential")]
    [InlineData("transparent")]
    [InlineData("unique")]
    [InlineData("cleanup")]
    [InlineData("offset")]
    [InlineData("width")]
    [InlineData("size")]
    [InlineData("cycle")]
    [InlineData("managed")]
    [InlineData("owner")]
    [InlineData("view")]
    [InlineData("native-handle")]
    [InlineData("native-carrier")]
    [InlineData("unknown-type")]
    [InlineData("negative-type")]
    [InlineData("duplicate-field")]
    [InlineData("conflicting-access")]
    public void InvalidCommitmentsFailProductionStorageAndSemanticVerification(string change)
    {
        var program = Commit(CompileLibrary(Library));
        var leaf = program.Structures[0];
        var packet = program.Structures[1];
        var invalid = change switch {
            "missing-root" => packet with { BlittableTargetRequirement = 0 },
            "wrong-target" => packet with { BlittableTargetRequirement = 2 },
            "missing-sequential" => packet with { IsSequential = false },
            "transparent" => packet with { IsTransparent = true },
            "unique" => packet with { IsUnique = true },
            "cleanup" => packet with { CleanupMethod = "Drop" },
            "offset" => packet with { Fields = packet.Fields.SetItem(1, packet.Fields[1] with { Offset = 3 }) },
            "width" => packet with { Fields = packet.Fields.SetItem(1, packet.Fields[1] with { Size = 8 }) },
            "size" => packet with { Size = 16 },
            "cycle" => packet with { Fields = packet.Fields.SetItem(1, packet.Fields[1] with { Type = (PrimitiveType)101 }) },
            "managed" => packet with { Fields = packet.Fields.SetItem(1, packet.Fields[1] with { Type = PrimitiveType.ManagedNode }) },
            "owner" => packet with { Fields = packet.Fields.SetItem(1, packet.Fields[1] with { Type = PrimitiveType.OwnedInt32Buffer }) },
            "view" => packet with { Fields = packet.Fields.SetItem(1, packet.Fields[1] with { Type = PrimitiveType.ReadonlyInt32BufferView }) },
            "native-handle" => packet with { Fields = packet.Fields.SetItem(1, packet.Fields[1] with { Type = PrimitiveType.NativeWindowHandle }) },
            "native-carrier" => packet with { Fields = packet.Fields.SetItem(1, packet.Fields[1] with { Type = PrimitiveType.NativeFixedArray }) },
            "unknown-type" => packet with { Fields = packet.Fields.SetItem(1, packet.Fields[1] with { Type = (PrimitiveType)999 }) },
            "negative-type" => packet with { Fields = packet.Fields.SetItem(1, packet.Fields[1] with { Type = (PrimitiveType)int.MinValue }) },
            "duplicate-field" => packet with { Fields = packet.Fields.SetItem(1, packet.Fields[1] with { Name = "Prefix" }) },
            "conflicting-access" => packet with { Fields = packet.Fields.SetItem(1, packet.Fields[1] with { IsPrivate = true, IsInternal = true }) },
            "missing-nested" => packet,
            _ => throw new InvalidOperationException(),
        };
        if (change == "missing-nested")
        {
            leaf = leaf with { BlittableTargetRequirement = 0 };
        }
        program = program with { Structures = [leaf, invalid] };
        if (change == "missing-root")
        {
            // No root claim is inferred; public Sequential still fails the legacy executable check.
            Assert.Equal(new[] { 4, 0 }, FixedLayoutStorage.Validate(program.Structures));
        }
        else
        {
            _ = Assert.Throws<ArgumentException>(() => FixedLayoutStorage.Validate(program.Structures));
        }
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(program));
        if (change == "native-handle")
        {
            _ = Assert.Throws<NotSupportedException>(() => ModuleCompiler.CreateProducer(program, ["Make"]));
        }
        else
        {
            _ = Assert.Throws<ArgumentException>(() => ModuleCompiler.CreateProducer(program, ["Make"]));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CachedNestedLayoutsStillEnforceTheWholePathDepth(bool deepestFirst)
    {
        var structures = ImmutableArray.CreateBuilder<BoundStruct>(129);
        for (var index = 0; index < 129; index++)
        {
            var next = deepestFirst ? index - 1 : index + 1;
            var terminal = deepestFirst ? index == 0 : index == 128;
            structures.Add(new BoundStruct($"D{index}",
                [new BoundStructField("Value", terminal ? PrimitiveType.Int32 : (PrimitiveType)(100 + next), 0, 4)], 4) {
                IsSequential = true, BlittableTargetRequirement = 1,
            });
        }
        _ = Assert.Throws<NotSupportedException>(() => FixedLayoutStorage.Validate(structures.ToImmutable()));
        if (deepestFirst)
        {
            structures.RemoveAt(128);
        }
        else
        {
            structures[127] = structures[127] with {
                Fields = [new BoundStructField("Value", PrimitiveType.Int32, 0, 4)],
            };
            structures.RemoveAt(128);
        }
        Assert.All(FixedLayoutStorage.Validate(structures.ToImmutable()), alignment => Assert.Equal(4, alignment));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void IndependentReplacementRetainsNominalStorageAndPublicFieldAccess(BoundRuntimePolicy policy)
    {
        var program = Commit(CompileLibrary(Library, policy));
        Assert.Equal(new[] { 4, 4 }, FixedLayoutStorage.Validate(program.Structures));
        Assert.Equal(12, program.Structures[1].Size);
        Assert.Equal(new[] { 0, 4, 8 }, program.Structures[1].Fields.Select(static field => field.Offset));
        var producer = ModuleCompiler.CreateProducer(program, ["Make"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut Packet value = Make(39);
                value.Item.Value = value.Item.Value + 1;
                if (!value.Enabled) { return -1; }
                return value.Item.Value + int32(value.Prefix);
            }
            """), reference, policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(78, BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4, 4)));
        var snapshot = SHA256.HashData(consumer);
        var replacementProgram = Commit(CompileLibrary(
            "public struct Other { public uint64 Value; } " + Library.Replace(
                "new Packet(value)", "new Packet(value + 1)", StringComparison.Ordinal), policy));
        Assert.NotEqual(program.Structures[1].Fields[1].Type, replacementProgram.Structures[2].Fields[1].Type);
        var replacement = ModuleCompiler.CreateProducer(replacementProgram, ["Make"]);
        var nativePolicy = (NativeRuntimePolicy)policy;
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), nativePolicy));
        Assert.Equal(43, NativeJit.Execute(ModuleCompiler.Link(consumer, replacement), nativePolicy));
        Assert.Equal(snapshot, SHA256.HashData(consumer));
    }

    [Theory]
    [InlineData("commitment")]
    [InlineData("field-type")]
    [InlineData("field-name")]
    [InlineData("field-order")]
    [InlineData("access")]
    [InlineData("mutation")]
    [InlineData("unsafe")]
    [InlineData("nominal")]
    public void ReplacementRejectsChangedStorageOrFieldContracts(string change)
    {
        var original = ModuleCompiler.CreateProducer(Commit(CompileLibrary(Library)), ["Make"]);
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Make(42).Item.Value; }"), ModuleCompiler.CreateReference(original));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), original);
        var source = change switch {
            "field-type" => Library.Replace("int32 Value", "uint32 Value", StringComparison.Ordinal)
                .Replace("Value = value", "Value = uint32(value)", StringComparison.Ordinal),
            "field-name" => Library.Replace("Value", "Data", StringComparison.Ordinal),
            "field-order" => Library.Replace("public char16 Prefix;", "", StringComparison.Ordinal)
                .Replace("public mut Leaf Item;", "public mut Leaf Item; public char16 Prefix;", StringComparison.Ordinal),
            "access" => Library.Replace("public mut int32 Value", "internal mut int32 Value", StringComparison.Ordinal),
            "mutation" => Library.Replace("public mut int32 Value", "public int32 Value", StringComparison.Ordinal),
            "unsafe" => UnsafeLibrary(),
            "nominal" => Library.Replace("Leaf", "DifferentLeaf", StringComparison.Ordinal),
            _ => Library,
        };
        var replacementProgram = CompileLibrary(source);
        if (change is not "commitment")
        {
            Assert.NotEqual(Library, source);
        }
        if (change != "commitment")
        {
            replacementProgram = Commit(replacementProgram);
        }
        if (change == "access")
        {
            _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(replacementProgram, ["Make"]));
        }
        else
        {
            var replacement = ModuleCompiler.CreateProducer(replacementProgram, ["Make"]);
            _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer, replacement));
        }
    }

    [Theory]
    [InlineData("target", 0)]
    [InlineData("target", 2)]
    [InlineData("sequential", 0)]
    [InlineData("sequential", 2)]
    [InlineData("size", 16)]
    [InlineData("field-type", (int)PrimitiveType.OwnedInt32Buffer)]
    [InlineData("field-offset", 3)]
    [InlineData("field-width", 8)]
    public void ForgedProducerAndConsumerStorageMetadataRejectsBeforeExecution(string fact, int value)
    {
        var producer = ModuleCompiler.CreateProducer(Commit(CompileLibrary(Library)), ["Make"]);
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Make(42).Item.Value; }"),
            ModuleCompiler.CreateReference(producer));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        foreach (var original in new[] { producer, consumer })
        {
            var forged = (byte[])original.Clone();
            var packet = LocateStructures(forged)["Packet"];
            var position = fact switch {
                "target" => packet.Target,
                "sequential" => packet.Sequential,
                "size" => packet.Size,
                "field-type" => packet.Fields["Item"].Type,
                "field-offset" => packet.Fields["Item"].Offset,
                "field-width" => packet.Fields["Item"].Size,
                _ => throw new InvalidOperationException(),
            };
            if (fact == "sequential")
            {
                forged[position] = (byte)value;
            }
            else
            {
                BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(position, 4), value);
            }
            var isConsumer = ReferenceEquals(original, consumer);
            _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.GetNativeProfileVersion(forged, isConsumer));
            if (isConsumer)
            {
                _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(forged, producer));
            }
            else
            {
                _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forged));
            }
        }
    }

    [Fact]
    public void ErasingEveryCommitmentOrReinterpretingAnOldRevisionRejects()
    {
        var producer = ModuleCompiler.CreateProducer(Commit(CompileLibrary(Library)), ["Make"]);
        var erased = (byte[])producer.Clone();
        foreach (var positions in LocateStructures(erased).Values)
        {
            BinaryPrimitives.WriteInt32LittleEndian(erased.AsSpan(positions.Target, 4), 0);
        }
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(erased));
        foreach (var revision in new[] { 76, 77, 79 })
        {
            var forged = (byte[])producer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(4, 4), revision);
            _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forged));
        }
    }

    [Theory]
    [InlineData("Packet value = Make(42); value.Item.Value = 0; return 0;", "TRM3008")]
    [InlineData("mut Packet value = Make(42); value.Prefix = char16(0); return 0;", "TRM3008")]
    [InlineData("Packet value = Make(42); return value.Item;", "TRM3007")]
    [InlineData("init Packet value; return value.Item.Value;", "TRM3016")]
    public void CommittedPublicFieldsKeepOrdinaryMutationTypeAndInitializationRules(string body, string code)
    {
        var producer = ModuleCompiler.CreateProducer(Commit(CompileLibrary(Library)), ["Make"]);
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { " + body + " }"),
            ModuleCompiler.CreateReference(producer));
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedFixedFieldsRetainUnsafeCallerAuthority(bool authorized)
    {
        var producer = ModuleCompiler.CreateProducer(Commit(CompileLibrary(UnsafeLibrary())), ["Make"]);
        var body = authorized ? "unsafe { return Make(42).Item.Value; }" : "return Make(42).Item.Value;";
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { " + body + " }"),
            ModuleCompiler.CreateReference(producer));
        if (!authorized)
        {
            Assert.True(compilation.HasErrors);
            Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3028");
            return;
        }
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var consumer = ModuleCompiler.CreateConsumer(program, producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), NativeRuntimePolicy.FailFast));
        var main = program.Functions[program.EntryPoint];
        var scope = Assert.IsType<BoundUnsafe>(Assert.Single(main.Body.Statements));
        var result = Assert.IsType<BoundReturn>(Assert.Single(scope.Body.Statements));
        var read = Assert.IsType<BoundFieldRead>(result.Value);
        var forgedMain = main with {
            Body = main.Body with {
                Statements = [scope with {
                    Body = scope.Body with {
                        Statements = [result with { Value = new BoundFieldRead(read.Receiver, read.Field, read.Type, read.Span) }],
                    },
                }],
            },
        };
        _ = Assert.Throws<ArgumentException>(() => ModuleCompiler.CreateConsumer(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, forgedMain),
        }, producer));
    }

    [Fact]
    public void FixedCommitmentsDoNotBypassPrivateAccessOrBooleanValidity()
    {
        const string Source = """
            public struct Box {
                int32 Secret;
                public Box(int32 value) { Secret = value; }
                public int32 Read() { return Secret; }
            }
            int32 Main() { Box value = new Box(42); return value.Read(); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Commit(Assert.IsType<BoundProgram>(compilation.Program));
        SemanticVerifier.Verify(program);
        Assert.Equal(42, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        var privateAccess = Compilation.Create(SyntaxTree.Parse(
            Source.Replace("value.Read()", "value.Secret", StringComparison.Ordinal)));
        Assert.Contains(privateAccess.Diagnostics, diagnostic => diagnostic.Code == "TRM3011");

        var booleanProgram = Commit(CompileLibrary(Library));
        var constructor = booleanProgram.Functions.Single(static function => function.DeclaringStruct == "Packet"
            && function.IsConstructor);
        var assignment = Assert.Single(constructor.Body.Statements.OfType<BoundFieldAssignment>(),
            static statement => statement.Value.Type == PrimitiveType.Bool);
        var assignmentIndex = constructor.Body.Statements.IndexOf(assignment);
        var forgedConstructor = constructor with {
            Body = constructor.Body with {
                Statements = constructor.Body.Statements.SetItem(assignmentIndex, assignment with {
                    Value = new BoundConstant(2, PrimitiveType.Bool, assignment.Span),
                }),
            },
        };
        var forgedBoolean = booleanProgram with {
            Functions = booleanProgram.Functions.SetItem(booleanProgram.Functions.IndexOf(constructor), forgedConstructor),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(forgedBoolean));
        _ = Assert.Throws<ArgumentException>(() => ModuleCompiler.CreateProducer(forgedBoolean, ["Make"]));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NativeGraphMetadataRetainsTheSameFixedStorageReplacementContractAsync(BoundRuntimePolicy policy)
    {
        var nativePolicy = (NativeRuntimePolicy)policy;
        var producer = NativeModuleCompiler.CreateProducer(Commit(CompileLibrary(Library, policy)),
            ["Make"], "FixedLeaf", nativePolicy);
        var reference = NativeModuleCompiler.CreateGraphReference([producer]);
        Assert.All(reference.Structures, structure => Assert.Equal(1, structure.BlittableTargetRequirement));
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Make(42).Item.Value; }"),
            reference, policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateGraphConsumer(Assert.IsType<BoundProgram>(compilation.Program),
            [producer], nativePolicy);
        var replacement = NativeModuleCompiler.CreateProducer(Commit(CompileLibrary(
            Library.Replace("new Packet(value)", "new Packet(value + 1)", StringComparison.Ordinal), policy)),
            ["Make"], "FixedLeaf", nativePolicy);
        NativeModuleCompiler.ValidateGraph(consumer, [replacement]);
        Assert.Equal(43, await NativeModuleCompiler.RunGraphAsync(consumer, [replacement],
            TimeSpan.FromSeconds(10), nativePolicy).ConfigureAwait(true));
        var uncommitted = NativeModuleCompiler.CreateProducer(CompileLibrary(Library, policy),
            ["Make"], "FixedLeaf", nativePolicy);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(consumer, [uncommitted]));
    }

    [Theory]
    [InlineData("[Blittable] public struct Box { public int32 Value; }")]
    [InlineData("[Blittable] [Layout(Sequential)] public struct Box { public int32 Value; }")]
    public void ProvisionalBoundOptInDoesNotRegisterSourceAttributes(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source + " public int32 Check() { return 42; }"));
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3001");
    }

    private static BoundProgram CompileLibrary(string source, BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static BoundProgram Commit(BoundProgram program) => program with {
        Structures = [.. program.Structures.Select(static structure => structure with {
            IsSequential = true,
            BlittableTargetRequirement = FixedLayoutStorage.WindowsX64TargetRequirement,
        })],
    };

    private static string UnsafeLibrary() => Library
        .Replace("public mut int32 Value", "public unsafe mut int32 Value", StringComparison.Ordinal)
        .Replace("Value = value;", "unsafe { Value = value; }", StringComparison.Ordinal);

    private static Dictionary<string, StructurePositions> LocateStructures(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        _ = reader.ReadInt32();
        Assert.Equal(78, reader.ReadInt32());
        _ = ReadString(reader);
        _ = reader.ReadBoolean();
        _ = reader.ReadByte();
        _ = reader.ReadBoolean();
        _ = ReadString(reader);
        Assert.Equal(0, reader.ReadInt32());
        _ = reader.ReadInt32();
        var count = reader.ReadInt32();
        var result = new Dictionary<string, StructurePositions>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var name = ReadString(reader);
            var size = (int)stream.Position;
            _ = reader.ReadInt32();
            _ = reader.ReadBytes(4);
            var target = (int)stream.Position;
            _ = reader.ReadInt32();
            var sequential = (int)stream.Position;
            _ = reader.ReadBoolean();
            if (reader.ReadBoolean())
            {
                _ = ReadString(reader);
            }
            var fields = new Dictionary<string, FieldPositions>(StringComparer.Ordinal);
            var fieldCount = reader.ReadInt32();
            for (var field = 0; field < fieldCount; field++)
            {
                var fieldName = ReadString(reader);
                var position = (int)stream.Position;
                fields.Add(fieldName, new FieldPositions(position, position + 4, position + 8));
                _ = reader.ReadBytes(16);
            }
            result.Add(name, new StructurePositions(size, target, sequential, fields));
        }
        return result;
    }

    private static string ReadString(BinaryReader reader) => Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));

    private sealed record StructurePositions(int Size, int Target, int Sequential, Dictionary<string, FieldPositions> Fields);
    private sealed record FieldPositions(int Type, int Offset, int Size);
}
