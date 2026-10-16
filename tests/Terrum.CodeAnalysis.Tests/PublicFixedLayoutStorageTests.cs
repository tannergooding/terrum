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
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class PublicFixedLayoutStorageTests
{
    private const string Library = """
        [Blittable] [Layout(LayoutKind.Sequential)]
        public struct Leaf {
            public mut int32 Value;
            public Leaf(int32 value) { Value = value; }
        }
        [Blittable] [Layout(LayoutKind.Sequential)]
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
    [TestCase("bool",1)]
    [TestCase("char16",2)]
    [TestCase("int32",4)]
    [TestCase("uint32",4)]
    [TestCase("int64",8)]
    [TestCase("uint64",8)]
    [TestCase("float32",4)]
    [TestCase("float64",8)]
    public void EveryStorableCorePrimitiveRetainsASourceDeclaredCommitment(string type, int width)
    {
        var program = CompileLibrary($"[Blittable] [Layout(LayoutKind.Sequential)] "
            + $"public struct Box {{ public {type} Value; }}"
            + " public int32 Check(Box value) { return 42; }");
        Assert.That(global::System.Linq.Enumerable.Single(FixedLayoutStorage.Validate(program.Structures)), Is.EqualTo(width));
        SemanticVerifier.VerifyLibrary(program);
        var producer = ModuleCompiler.CreateProducer(program, ["Check"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(78));
        var retained = global::System.Linq.Enumerable.Single(ModuleCompiler.CreateReference(producer).Structures);
        Assert.That(retained.IsSequential, Is.True);
        Assert.That(retained.BlittableTargetRequirement, Is.EqualTo(FixedLayoutStorage.WindowsX64TargetRequirement));
        Assert.That(retained.Size, Is.EqualTo(width));
        Assert.That((global::System.Linq.Enumerable.Single(retained.Fields).Offset, retained.Fields[0].Size), Is.EqualTo((0, width)));
    }

    [Test]
    public void OpaquePointerFieldsRetainTheirRepresentationAcrossPrivateModules()
    {
        var program = CompileLibrary("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct PointerPair {
                public ptr<void> First;
                public ptr<void> Second;
            }
            public int32 RoundTrip(PointerPair value) { return 42; }
            """);
        Assert.That(FixedLayoutStorage.Validate(program.Structures), Is.EqualTo([8]));
        Assert.That(program.Structures[0].Fields.Select(static field => field.Type),
            Is.EqualTo([PrimitiveType.NativeRawPointer, PrimitiveType.NativeRawPointer]));
        Assert.That(program.Structures[0].Fields.Select(static field => field.Offset), Is.EqualTo([0, 8]));
        SemanticVerifier.VerifyLibrary(program);

        var artifact = ModuleCompiler.CreateProducer(program, ["RoundTrip"]);
        var retained = ModuleCompiler.CreateReference(artifact);
        Assert.That(retained.Structures.Single().Fields.Select(static field => field.Type),
            Is.EqualTo([PrimitiveType.NativeRawPointer, PrimitiveType.NativeRawPointer]));
        Assert.That(retained.Structures.Single().Size, Is.EqualTo(16));
    }

    [Test]
    public void TypedCharacterPointerFieldsRetainTheirNativeAddressLayout()
    {
        var program = CompileLibrary("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct CharacterPointer {
                public ptr<char16> Value;
            }
            public int32 RoundTrip(CharacterPointer value) { return 42; }
            """);

        Assert.That(FixedLayoutStorage.Validate(program.Structures), Is.EqualTo([8]));
        Assert.That(program.Structures[0].Fields.Single().Offset, Is.Zero);
        SemanticVerifier.VerifyLibrary(program);
    }

    [TestCase("char16 Prefix; int64 Value; char16 Tail;",24,8,new[] { 0, 8, 16 })]
    [TestCase("uint64 A; uint64 B; uint64 C; uint64 D; uint64 E;",40,8,new[] { 0, 8, 16, 24, 32 })]
    public void LargerBlittableLayoutsRemainEligibleForExecutableUses(
        string fields, int size, int alignment, int[] offsets)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(offsets);
        var sourceFields = string.Join(Environment.NewLine,
            fields.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static field => $"public {field};"));
        var source = $"[Blittable] [Layout(LayoutKind.Sequential)] public struct Large {{ {sourceFields} }}"
            + " public int32 Check() { return 42; }";
        var program = CompileLibrary(source);
        Assert.That(FixedLayoutStorage.Validate(program.Structures), Is.EqualTo([alignment]));
        Assert.That(program.Structures[0].Size, Is.EqualTo(size));
        Assert.That(program.Structures[0].Fields.Select(static field => field.Offset), Is.EqualTo(offsets));
        SemanticVerifier.VerifyLibrary(program);
        _ = ModuleCompiler.CreateProducer(program, ["Check"]);

        var aggregateProgram = CompileLibrary(source.Replace("Check()", "Check(Large value)", StringComparison.Ordinal));
        SemanticVerifier.VerifyLibrary(aggregateProgram);
        _ = ModuleCompiler.CreateProducer(aggregateProgram, ["Check"]);
    }
    [TestCase("missing-root")]
    [TestCase("missing-nested")]
    [TestCase("wrong-target")]
    [TestCase("missing-sequential")]
    [TestCase("transparent")]
    [TestCase("cleanup")]
    [TestCase("offset")]
    [TestCase("width")]
    [TestCase("size")]
    [TestCase("cycle")]
    [TestCase("managed")]
    [TestCase("owner")]
    [TestCase("view")]
    [TestCase("reserved-native-callback-address")]
    [TestCase("native-carrier")]
    [TestCase("unknown-type")]
    [TestCase("negative-type")]
    [TestCase("duplicate-field")]
    [TestCase("conflicting-access")]
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
            "cleanup" => packet with { CleanupMethod = "Drop" },
            "offset" => packet with { Fields = packet.Fields.SetItem(1, packet.Fields[1] with { Offset = 3 }) },
            "width" => packet with { Fields = packet.Fields.SetItem(1, packet.Fields[1] with { Size = 8 }) },
            "size" => packet with { Size = 16 },
            "cycle" => packet with { Fields = packet.Fields.SetItem(1, packet.Fields[1] with { Type = (PrimitiveType)101 }) },
            "managed" => packet with { Fields = packet.Fields.SetItem(1, packet.Fields[1] with { Type = PrimitiveType.ManagedNode }) },
            "owner" => packet with { Fields = packet.Fields.SetItem(1, packet.Fields[1] with { Type = PrimitiveType.OwnedInt32Buffer }) },
            "view" => packet with { Fields = packet.Fields.SetItem(1, packet.Fields[1] with { Type = PrimitiveType.ReadonlyInt32BufferView }) },
            "reserved-native-callback-address" => packet with {
                Fields = packet.Fields.SetItem(1, packet.Fields[1] with {
                    Type = PrimitiveType.LegacyNativeCallbackAddress,
                }),
            },
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
            Assert.That(FixedLayoutStorage.Validate(program.Structures), Is.EqualTo(new[] { 4, 0 }));
        }
        else
        {
            _ = Assert.Throws<ArgumentException>(() => FixedLayoutStorage.Validate(program.Structures))!;
        }
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(program))!;
        if (change is "native-function-pointer" or "native-carrier")
        {
            _ = Assert.Throws<NotSupportedException>(() => ModuleCompiler.CreateProducer(program, ["Make"]))!;
        }
        else
        {
            _ = Assert.Throws<ArgumentException>(() => ModuleCompiler.CreateProducer(program, ["Make"]))!;
        }
    }
    [Test]
    public void UniqueBlittableLayoutRetainsItsIndependentCleanupContract()
    {
        var program = CompileLibrary("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public unique struct Owner {
                private int32 Value;
                public Owner(int32 value) { Value = value; }
                [NoThrow] [Cleanup]
                public void Close() { }
            }
            """);
        Assert.That(FixedLayoutStorage.Validate(program.Structures),
            Is.EqualTo([sizeof(int)]));
        SemanticVerifier.VerifyLibrary(program);
    }
    [TestCase(false)]
    [TestCase(true)]
    public void CachedNestedLayoutsStillEnforceTheWholePathDepth(bool deepestFirst)
    {
        var structures = ImmutableArray.CreateBuilder<BoundStruct>(129);
        for (var index = 0; index < 129; index++)
        {
            var next = deepestFirst ? index - 1 : index + 1;
            var terminal = deepestFirst ? index == 0 : index == 128;
            structures.Add(new BoundStruct($"D{index}",
                [new BoundStructField("Value", terminal ? PrimitiveType.Int32 : (PrimitiveType)(100 + next), 0, 4)], 4) {
                IsSequential = true,
                BlittableTargetRequirement = 1,
            });
        }
        _ = Assert.Throws<NotSupportedException>(() => FixedLayoutStorage.Validate(structures.ToImmutable()))!;
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
        {var __nunitItems0 = FixedLayoutStorage.Validate(structures.ToImmutable());        Assert.Multiple(() => { foreach (var __nunitItem0 in __nunitItems0) { Assert.That(__nunitItem0, Is.EqualTo(4)); } });}
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void IndependentReplacementRetainsNominalStorageAndPublicFieldAccess(BoundRuntimePolicy policy)
    {
        var program = Commit(CompileLibrary(Library, policy));
        Assert.That(FixedLayoutStorage.Validate(program.Structures), Is.EqualTo(new[] { 4, 4 }));
        Assert.That(program.Structures[1].Size, Is.EqualTo(12));
        Assert.That(program.Structures[1].Fields.Select(static field => field.Offset), Is.EqualTo(new[] { 0, 4, 8 }));
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), producer);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4, 4)), Is.EqualTo(78));
        var snapshot = SHA256.HashData(consumer);
        var replacementProgram = CompileLibrary(
            "[Blittable] [Layout(LayoutKind.Sequential)] public struct Other { public uint64 Value; } "
                + Library.Replace(
                "new Packet(value)", "new Packet(value + 1)", StringComparison.Ordinal), policy);
        Assert.That(replacementProgram.Structures[2].Fields[1].Type, Is.Not.EqualTo(program.Structures[1].Fields[1].Type));
        var replacement = ModuleCompiler.CreateProducer(replacementProgram, ["Make"]);
        var nativePolicy = (NativeRuntimePolicy)policy;
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), nativePolicy), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, replacement), nativePolicy), Is.EqualTo(43));
        Assert.That(SHA256.HashData(consumer), Is.EqualTo(snapshot));
    }
    [TestCase("commitment")]
    [TestCase("field-type")]
    [TestCase("field-name")]
    [TestCase("field-order")]
    [TestCase("access")]
    [TestCase("mutation")]
    [TestCase("unsafe")]
    [TestCase("nominal")]
    public void ReplacementRejectsChangedStorageOrFieldContracts(string change)
    {
        var baseProgram = CompileLibrary(Library);
        var original = ModuleCompiler.CreateProducer(baseProgram, ["Make"]);
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Make(42).Item.Value; }"), ModuleCompiler.CreateReference(original));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(), original);
        if (change == "commitment")
        {
            var uncommitted = baseProgram with {
                Structures = [.. baseProgram.Structures.Select(static structure => structure with {
                    BlittableTargetRequirement = 0,
                })],
            };
            _ = Assert.Throws<ArgumentException>(() =>
                ModuleCompiler.CreateProducer(uncommitted, ["Make"]))!;
            return;
        }
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
            _ => throw new InvalidOperationException(),
        };
        Assert.That(source, Is.Not.EqualTo(Library));
        var replacementProgram = CompileLibrary(source);
        if (change == "access")
        {
            _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(replacementProgram, ["Make"]))!;
        }
        else
        {
            var replacement = ModuleCompiler.CreateProducer(replacementProgram, ["Make"]);
            _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer, replacement))!;
        }
    }
    [TestCase("target",0)]
    [TestCase("target",2)]
    [TestCase("sequential",0)]
    [TestCase("sequential",2)]
    [TestCase("size",16)]
    [TestCase("field-type",(int)PrimitiveType.OwnedInt32Buffer)]
    [TestCase("field-offset",3)]
    [TestCase("field-width",8)]
    public void ForgedProducerAndConsumerStorageMetadataRejectsBeforeExecution(string fact, int value)
    {
        var producer = ModuleCompiler.CreateProducer(Commit(CompileLibrary(Library)), ["Make"]);
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Make(42).Item.Value; }"),
            ModuleCompiler.CreateReference(producer));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))(), producer);
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
            _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.GetNativeProfileVersion(forged, isConsumer))!;
            if (isConsumer)
            {
                _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(forged, producer))!;
            }
            else
            {
                _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forged))!;
            }
        }
    }

    [Test]
    public void ErasingEveryCommitmentOrReinterpretingAnOldRevisionRejects()
    {
        var producer = ModuleCompiler.CreateProducer(Commit(CompileLibrary(Library)), ["Make"]);
        var erased = (byte[])producer.Clone();
        foreach (var positions in LocateStructures(erased).Values)
        {
            BinaryPrimitives.WriteInt32LittleEndian(erased.AsSpan(positions.Target, 4), 0);
        }
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(erased))!;
        foreach (var revision in new[] { 76, 77, 79 })
        {
            var forged = (byte[])producer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(4, 4), revision);
            _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forged))!;
        }
    }
    [TestCase("Packet value = Make(42); value.Item.Value = 0; return 0;","TRM3008")]
    [TestCase("mut Packet value = Make(42); value.Prefix = char16(0); return 0;","TRM3008")]
    [TestCase("Packet value = Make(42); return value.Item;","TRM3007")]
    [TestCase("init Packet value; return value.Item.Value;","TRM3016")]
    public void CommittedPublicFieldsKeepOrdinaryMutationTypeAndInitializationRules(string body, string code)
    {
        var producer = ModuleCompiler.CreateProducer(Commit(CompileLibrary(Library)), ["Make"]);
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { " + body + " }"),
            ModuleCompiler.CreateReference(producer));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == code),Is.True);
    }
    [TestCase(false)]
    [TestCase(true)]
    public void ImportedFixedFieldsRetainUnsafeCallerAuthority(bool authorized)
    {
        var producer = ModuleCompiler.CreateProducer(Commit(CompileLibrary(UnsafeLibrary())), ["Make"]);
        var body = authorized ? "unsafe { return Make(42).Item.Value; }" : "return Make(42).Item.Value;";
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { " + body + " }"),
            ModuleCompiler.CreateReference(producer));
        if (!authorized)
        {
            Assert.That(compilation.HasErrors, Is.True);
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3028"),Is.True);
            return;
        }
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
        var consumer = ModuleCompiler.CreateConsumer(program, producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        var main = program.Functions[program.EntryPoint];
        var scope = ((global::System.Func<BoundUnsafe>)(() => { var __nunitActual5 = global::System.Linq.Enumerable.Single(main.Body.Statements); Assert.That(__nunitActual5, Is.TypeOf<BoundUnsafe>()); return (BoundUnsafe)__nunitActual5!; }))();
        var result = ((global::System.Func<BoundReturn>)(() => { var __nunitActual6 = global::System.Linq.Enumerable.Single(scope.Body.Statements); Assert.That(__nunitActual6, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual6!; }))();
        var read = ((global::System.Func<BoundFieldRead>)(() => { var __nunitActual7 = result.Value; Assert.That(__nunitActual7, Is.TypeOf<BoundFieldRead>()); return (BoundFieldRead)__nunitActual7!; }))();
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
        }, producer))!;
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Commit(((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = compilation.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))());
        SemanticVerifier.Verify(program);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        var privateAccess = Compilation.Create(SyntaxTree.Parse(
            Source.Replace("value.Read()", "value.Secret", StringComparison.Ordinal)));
        Assert.That(global::System.Linq.Enumerable.Any(privateAccess.Diagnostics,diagnostic => diagnostic.Code == "TRM3011"),Is.True);

        var booleanProgram = Commit(CompileLibrary(Library));
        var constructor = booleanProgram.Functions.Single(static function => function.DeclaringStruct == "Packet"
            && function.IsConstructor);
        var assignment = global::System.Linq.Enumerable.Single(constructor.Body.Statements.OfType<BoundFieldAssignment>(), static statement => statement.Value.Type == PrimitiveType.Bool);
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
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(forgedBoolean))!;
        _ = Assert.Throws<ArgumentException>(() => ModuleCompiler.CreateProducer(forgedBoolean, ["Make"]))!;
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task NativeGraphMetadataRetainsTheSameFixedStorageReplacementContractAsync(BoundRuntimePolicy policy)
    {
        var nativePolicy = (NativeRuntimePolicy)policy;
        var sourceProgram = CompileLibrary(Library, policy);
        var producer = NativeModuleCompiler.CreateProducer(sourceProgram,
            ["Make"], "FixedLeaf", nativePolicy);
        var reference = NativeModuleCompiler.CreateGraphReference([producer]);
        {var __nunitItems1 = reference.Structures;        Assert.Multiple(() => { foreach (var __nunitItem9 in __nunitItems1) { Assert.That(__nunitItem9.BlittableTargetRequirement, Is.EqualTo(1)); } });}
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Make(42).Item.Value; }"),
            reference, policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateGraphConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = compilation.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))(),
            [producer], nativePolicy);
        var replacement = NativeModuleCompiler.CreateProducer(CompileLibrary(
            Library.Replace("new Packet(value)", "new Packet(value + 1)", StringComparison.Ordinal), policy),
            ["Make"], "FixedLeaf", nativePolicy);
        NativeModuleCompiler.ValidateGraph(consumer, [replacement]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(consumer, [replacement],
            TimeSpan.FromSeconds(10), nativePolicy).ConfigureAwait(true), Is.EqualTo(43));
        var uncommittedProgram = sourceProgram with {
            Structures = [.. sourceProgram.Structures.Select(static structure => structure with {
                BlittableTargetRequirement = 0,
            })],
        };
        _ = Assert.Throws<ArgumentException>(() => NativeModuleCompiler.CreateProducer(
            uncommittedProgram, ["Make"], "FixedLeaf", nativePolicy))!;
    }
    [TestCase("[Blittable] [Layout(LayoutKind.Sequential)] public struct Box { public int32 Value; }")]
    [TestCase("[Layout(Kind: LayoutKind.Sequential)] [Blittable] public struct Box { public int32 Value; }")]
    public void RegisteredSourceLayoutAttributesSelectTheFixedLayoutCommitment(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            source + " public int32 Check(Box value) { return value.Value; }"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var structure = global::System.Linq.Enumerable.Single(compilation.Program!.Structures);
        Assert.That(structure.IsSequential, Is.True);
        Assert.That(structure.BlittableTargetRequirement,
            Is.EqualTo(FixedLayoutStorage.WindowsX64TargetRequirement));
        Assert.That(global::System.Linq.Enumerable.Single(
            ModuleCompiler.CreateReference(ModuleCompiler.CreateProducer(compilation.Program!, ["Check"]))
                .Structures).BlittableTargetRequirement,
            Is.EqualTo(FixedLayoutStorage.WindowsX64TargetRequirement));
    }

    [Test]
    public void InternalStructFieldsDoNotCreateAnExternalLayoutBoundary()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct LocalValue {
                   public int32 Value;
                   public LocalValue(int32 value) { Value = value; }
            }
            int32 Main() {
                   LocalValue value = new LocalValue(42);
                   return value.Value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    private static BoundProgram CompileLibrary(string source, BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = compilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))();
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
        Assert.That(reader.ReadInt32(), Is.EqualTo(78));
        _ = ReadString(reader);
        _ = reader.ReadBoolean();
        _ = reader.ReadByte();
        _ = reader.ReadBoolean();
        _ = ReadString(reader);
        Assert.That(reader.ReadInt32(), Is.EqualTo(0));
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
