// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
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

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Native.Modules;

public sealed partial class NativeManagedClassModuleTests
{
    private const string Middle = "public int32 Middle() { return Leaf() + 1; }";
    private const string Root = "int32 Main() { Middle(); return Middle() + 1; }";
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task PrivateOptionalClassLeafReplacesUnchangedRootAndIntermediateAsync(NativeRuntimePolicy policy)
    {
        var original = NativeModuleCompiler.CreateProducer(Compile(Sample("managed-class-leaf-original"), policy),
            ["Leaf"], "NodeB", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(Middle, policy,
            NativeModuleCompiler.CreateGraphReference([original])), ["Middle"], "NodeA", [original], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(Root, policy,
            NativeModuleCompiler.CreateGraphReference([middle]), library: false), [middle], policy);
        var rootHash = SHA256.HashData(root);
        var middleHash = SHA256.HashData(middle);
        var replacement = NativeModuleCompiler.CreateProducer(Compile(Sample("managed-class-leaf"), policy),
            ["Leaf"], "NodeB", policy);
        Assert.That(NativeModuleCompiler.Inspect(replacement).NativeProfileVersion, Is.EqualTo("native-20"));
        var declaration = global::System.Linq.Enumerable.Single(NativeModuleCompiler.CreateReference(replacement).Functions);
        Assert.That(declaration.Name, Is.EqualTo("Leaf"));
        NativeModuleCompiler.ValidateGraph(root, [middle, original]);
        NativeModuleCompiler.ValidateGraph(root, [replacement, middle]);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.ValidateGraph(root, [middle]))!;
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateGraph(root, [middle, original, replacement]))!;
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [original, middle],
            TimeSpan.FromSeconds(10), policy), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [middle, replacement],
            TimeSpan.FromSeconds(10), policy), Is.EqualTo(44));
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var repository = RepositoryRoot();
        var apphost = Path.Combine(repository, "artifacts", "bin", "sources", "terrumc",
            configuration, frameworkDirectory.Name, "terrumc.exe");
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root, [middle, original], apphost,
            TimeSpan.FromSeconds(30), policy), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root, [replacement, middle], apphost,
            TimeSpan.FromSeconds(30), policy), Is.EqualTo(44));
        Assert.That(SHA256.HashData(root), Is.EqualTo(rootHash));
        Assert.That(SHA256.HashData(middle), Is.EqualTo(middleHash));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task PrivateCopyClassLeafReplacesUnchangedRootAndIntermediateAsync(NativeRuntimePolicy policy)
    {
        var original = NativeModuleCompiler.CreateProducer(Compile(Sample("managed-class-leaf-original"), policy),
            ["Leaf"], "NodeB", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(Middle, policy,
            NativeModuleCompiler.CreateGraphReference([original])), ["Middle"], "NodeA", [original], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(Root, policy,
            NativeModuleCompiler.CreateGraphReference([middle]), library: false), [middle], policy);
        var rootHash = SHA256.HashData(root);
        var middleHash = SHA256.HashData(middle);
        var program = Compile(Sample("managed-copy-class-leaf"), policy);
        var replacement = NativeModuleCompiler.CreateProducer(program, ["Leaf"], "NodeB", policy);
        Assert.That(NativeModuleCompiler.Inspect(replacement).NativeProfileVersion, Is.EqualTo("native-20"));
        _ = global::System.Linq.Enumerable.Single(NativeModuleCompiler.CreateReference(replacement).Functions);
        NativeModuleCompiler.ValidateGraph(root, [middle, replacement]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [original, middle],
            TimeSpan.FromSeconds(10), policy), Is.EqualTo(42));
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [replacement, middle],
            TimeSpan.FromSeconds(10), policy), Is.EqualTo(44));
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, frameworkDirectory.Name, "terrumc.exe");
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(root, [replacement, middle], apphost,
            TimeSpan.FromSeconds(30), policy), Is.EqualTo(44));
        Assert.That(SHA256.HashData(root), Is.EqualTo(rootHash));
        Assert.That(SHA256.HashData(middle), Is.EqualTo(middleHash));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void SemanticLinkRetainsPrivateClassesWithoutExportingThem(NativeRuntimePolicy policy)
    {
        var producer = ModuleCompiler.CreateProducer(Compile(Sample("managed-class-leaf"), policy), ["Leaf"]);
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.That(reference.Structures, Is.Empty);
        _ = global::System.Linq.Enumerable.Single(reference.Functions);
        var consumer = ModuleCompiler.CreateConsumer(Compile("int32 Main() { return Leaf() + 2; }",
            policy, reference, library: false), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        var @class = global::System.Linq.Enumerable.Single(linked.Classes);
        Assert.That(@class.Name, Is.EqualTo("$producer.Cycle"));
        Assert.That((int)@class.Fields[1].Type, Is.EqualTo((int)ManagedClassTypes.Optional(ManagedClassTypes.Writable(
            (PrimitiveType)ManagedClassTypes.First))));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InlineCopyClassFieldsUseVersionedAccessWithoutChangingTheNativeProfile(NativeRuntimePolicy policy)
    {
        var program = Compile("""
            struct Pair {
                public int32 Left;
                public int32 Right;
                public Pair(int32 left, int32 right) { Left = left; Right = right; }
            }
            class Leaf {
                public Pair Value;
                public Leaf(int32 value) { Value = new Pair(value, 2); }
            }
            public int32 Proof() {
                require Leaf.TryCreate(40, out var leaf);
                return leaf.Value.Left + leaf.Value.Right;
            }
            """, policy);
        var contract = ModuleCompiler.CreateProducer(program, ["Proof"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(contract.AsSpan(4, 4)), Is.EqualTo(76));
        Assert.That(ModuleCompiler.GetNativeProfileVersion(contract, false), Is.EqualTo("native-19"));
        var reference = ModuleCompiler.CreateReference(contract);
        Assert.That(reference.Structures, Is.Empty);
        _ = global::System.Linq.Enumerable.Single(reference.Functions);
        var native = NativeModuleCompiler.CreateProducer(program, ["Proof"], "NodeB", policy);
        Assert.That(NativeModuleCompiler.Inspect(native).NativeProfileVersion, Is.EqualTo("native-19"));
        var oldVersion = (byte[])contract.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(oldVersion.AsSpan(4, 4), 19);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(oldVersion))!;
        oldVersion = (byte[])contract.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(oldVersion.AsSpan(4, 4), 18);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(oldVersion))!;
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task SemanticLinkRemapsNestedPrivateCopyClassFieldsAsync(NativeRuntimePolicy policy)
    {
        var producer = ModuleCompiler.CreateProducer(Compile(Sample("managed-copy-class-leaf"), policy), ["Leaf"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile("""
            struct Cell {
                public int64 Unrelated;
                public Cell(int64 value) { Unrelated = value; }
            }
            struct Stamp {
                public int32 Unrelated;
                public Stamp(int32 value) { Unrelated = value; }
            }
            int32 Main() {
                Cell first = new Cell(1);
                Stamp second = new Stamp(2);
                return Leaf() + int32(first.Unrelated) + second.Unrelated - 1;
            }
            """, policy, ModuleCompiler.CreateReference(producer), library: false), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(linked.Structures.Select(static item => item.Name), Is.EqualTo(["Cell", "Stamp", "$producer.Cell", "$producer.Stamp"]));
        var stamp = linked.Structures[3];
        Assert.That(stamp.Fields[1].Type, Is.EqualTo((PrimitiveType)102));
        var @class = global::System.Linq.Enumerable.Single(linked.Classes);
        Assert.That(@class.Name, Is.EqualTo("$producer.Cycle"));
        Assert.That(@class.Fields[1].Type, Is.EqualTo((PrimitiveType)103));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true), Is.EqualTo(44));
    }

    [Test]
    public void CurrentSemanticModuleRejectsForgedCopyFieldTypesAlignmentAndOwnership()
    {
        var contract = ModuleCompiler.CreateProducer(Compile(Sample("managed-copy-class-leaf"),
            NativeRuntimePolicy.FailFast), ["Leaf"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(contract.AsSpan(4, 4)), Is.EqualTo(76));
        using var reader = new BinaryReader(new MemoryStream(contract));
        reader.BaseStream.Position = 8;
        var profileLength = reader.ReadInt32();
        reader.BaseStream.Position += profileLength + 1 + 1 + 1 + 4 + 4 + 4;
        Assert.That(reader.ReadInt32(), Is.EqualTo(2));
        var unique = -1;
        var nestedOffset = -1;
        for (var structure = 0; structure < 2; structure++)
        {
            var nameLength = reader.ReadInt32();
            reader.BaseStream.Position += nameLength;
            _ = reader.ReadInt32();
            _ = reader.ReadBoolean();
            _ = reader.ReadBoolean();
            if (structure == 1)
            {
                unique = (int)reader.BaseStream.Position;
            }
            _ = reader.ReadBoolean();
            Assert.That(reader.ReadBoolean(), Is.False);
            if (reader.ReadBoolean())
            {
                var cleanupLength = reader.ReadInt32();
                reader.BaseStream.Position += cleanupLength;
            }
            var fields = reader.ReadInt32();
            for (var field = 0; field < fields; field++)
            {
                var fieldNameLength = reader.ReadInt32();
                reader.BaseStream.Position += fieldNameLength;
                _ = reader.ReadInt32();
                if (structure == 1 && field == 1)
                {
                    nestedOffset = (int)reader.BaseStream.Position;
                    Assert.That(reader.ReadInt32(), Is.EqualTo(4));
                    reader.BaseStream.Position -= 4;
                }
                reader.BaseStream.Position += 4 + 4 + 4;
            }
        }
        Assert.That(nestedOffset >= 0, Is.True);
        Assert.That(reader.ReadInt32(), Is.EqualTo(1));
        var classNameLength = reader.ReadInt32();
        reader.BaseStream.Position += classNameLength + 4 + 2;
        var fieldCount = reader.ReadInt32();
        Assert.That(fieldCount, Is.EqualTo(3));
        var firstNameLength = reader.ReadInt32();
        reader.BaseStream.Position += firstNameLength + 4 + 4 + 4 + 3;
        var secondNameLength = reader.ReadInt32();
        reader.BaseStream.Position += secondNameLength;
        var type = (int)reader.BaseStream.Position;
        Assert.That(reader.ReadInt32(), Is.EqualTo(101));
        var offset = (int)reader.BaseStream.Position;
        Assert.That(reader.ReadInt32(), Is.EqualTo(8));

        var malformed = (byte[])contract.Clone();
        malformed[unique] = 1;
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed))!;
        malformed = (byte[])contract.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(nestedOffset, 4), 8);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed))!;
        malformed = (byte[])contract.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(type, 4), 100);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed))!;
        malformed = (byte[])contract.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(offset, 4), 4);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed))!;
        malformed = (byte[])contract.Clone();
        malformed[offset + 8] = 1;
        malformed[offset + 9] = 1;
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed))!;
        var native = NativeModuleCompiler.CreateProducer(Compile(Sample("managed-copy-class-leaf"),
            NativeRuntimePolicy.FailFast), ["Leaf"], "NodeB", NativeRuntimePolicy.FailFast);
        var invalidPackage = (byte[])native.Clone();
        var contractOffset = 18 + invalidPackage[7] + invalidPackage[8] + 2;
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(invalidPackage.AsSpan(10, 4)), Is.EqualTo(contract.Length));
        malformed = (byte[])contract.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(offset, 4), 4);
        malformed.CopyTo(invalidPackage, contractOffset);
        Rehash(invalidPackage);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(invalidPackage))!;
        malformed = (byte[])contract.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(4, 4), 19);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed))!;
        var oldClass = ModuleCompiler.CreateProducer(Compile(Sample("managed-class-leaf"),
            NativeRuntimePolicy.FailFast), ["Leaf"]);
        var newVersion = (byte[])oldClass.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(newVersion.AsSpan(4, 4), 20);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(newVersion))!;
    }

    [Test]
    public void PersistedClassLayoutAndRootsAreVerifiedOnReload()
    {
        var program = Compile(Sample("managed-class-leaf"), NativeRuntimePolicy.FailFast);
        var contract = ModuleCompiler.CreateProducer(program, ["Leaf"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(contract.AsSpan(4, 4)), Is.EqualTo(76));
        Assert.That(ModuleCompiler.GetNativeProfileVersion(contract, false), Is.EqualTo("native-20"));
        using var reader = new BinaryReader(new MemoryStream(contract));
        reader.BaseStream.Position = 8;
        var profileLength = reader.ReadInt32();
        reader.BaseStream.Position += profileLength + 1 + 1 + 1 + 4 + 4 + 4;
        Assert.That(reader.ReadInt32(), Is.EqualTo(0));
        var classCount = (int)reader.BaseStream.Position;
        Assert.That(reader.ReadInt32(), Is.EqualTo(1));
        var nameLength = reader.ReadInt32();
        Assert.That(Encoding.UTF8.GetString(reader.ReadBytes(nameLength)), Is.EqualTo("Cycle"));
        var size = (int)reader.BaseStream.Position;
        Assert.That(reader.ReadInt32(), Is.EqualTo(16));
        var visibility = (int)reader.BaseStream.Position;
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt32(), Is.EqualTo(2));
        var firstNameLength = reader.ReadInt32();
        Assert.That(Encoding.UTF8.GetString(reader.ReadBytes(firstNameLength)), Is.EqualTo("Value"));
        var firstType = (int)reader.BaseStream.Position;
        Assert.That(reader.ReadInt32(), Is.EqualTo((int)PrimitiveType.Int32));
        var firstOffset = (int)reader.BaseStream.Position;
        Assert.That(reader.ReadInt32(), Is.EqualTo(0));
        Assert.That(reader.ReadInt32(), Is.EqualTo(4));
        reader.BaseStream.Position += 3;
        var secondNameLength = reader.ReadInt32();
        Assert.That(Encoding.UTF8.GetString(reader.ReadBytes(secondNameLength)), Is.EqualTo("Next"));
        var edgeType = (int)reader.BaseStream.Position;
        Assert.That(reader.ReadInt32(), Is.EqualTo((int)ManagedClassTypes.Optional(ManagedClassTypes.Writable(
            (PrimitiveType)ManagedClassTypes.First))));

        var malformed = (byte[])contract.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(classCount, 4), 257);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed))!;
        malformed = (byte[])contract.Clone();
        malformed[visibility] = 2;
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed))!;
        malformed = (byte[])contract.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(size, 4), 8);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed))!;
        malformed = (byte[])contract.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(firstOffset, 4), 4);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed))!;
        malformed = (byte[])contract.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(firstType, 4), 100);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed))!;
        malformed = (byte[])contract.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(edgeType, 4),
            ManagedClassTypes.OptionalFirst + 1);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed))!;

        var make = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "MakeValue");
        var retired = make.Body.RetiredManagedLocals;
        Assert.That(retired, Is.Not.Empty);
        var marker = new byte[14 + (4 * retired.Length)];
        marker[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(marker.AsSpan(1), make.Body.Span.Start);
        BinaryPrimitives.WriteInt32LittleEndian(marker.AsSpan(5), make.Body.Span.Length);
        marker[9] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(marker.AsSpan(10), retired.Length);
        for (var i = 0; i < retired.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(marker.AsSpan(14 + (4 * i)), retired[i]);
        }
        var scope = Find(contract, marker) + 9;
        malformed = (byte[])contract.Clone();
        malformed[scope] = 0;
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed))!;
        malformed = (byte[])contract.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(scope + 5, 4), 255);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed))!;

        var factoryIndex = global::System.Linq.Enumerable.Single(program.Functions.Select((function, index) => (function, index)), item => item.function.Intrinsic == BoundIntrinsic.ClassTryCreate).index;
        var factory = program.Functions[factoryIndex];
        var factoryMarker = new byte[6];
        factoryMarker[0] = (byte)BoundIntrinsic.ClassTryCreate;
        BinaryPrimitives.WriteInt32LittleEndian(factoryMarker.AsSpan(2), factory.ClassConstructor);
        malformed = (byte[])contract.Clone();
        malformed[Find(contract, factoryMarker)] = byte.MaxValue;
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed))!;
        malformed = (byte[])contract.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(Find(contract, factoryMarker) + 2, 4), 999);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed))!;
        var forged = program with {
            Functions = program.Functions.SetItem(factoryIndex, factory with { ClassConstructor = 999 }),
        };
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(forged, ["Leaf"]))!;
        forged = program with {
            Functions = program.Functions.SetItem(program.Functions.IndexOf(make),
                make with { Body = make.Body with { RetiredManagedLocals = [] } }),
        };
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(forged, ["Leaf"]))!;
    }

    [Test]
    public void NativePackageRejectsClassProfileAndManagedDataTampering()
    {
        var package = NativeModuleCompiler.CreateProducer(
            Compile(Sample("managed-class-leaf"), NativeRuntimePolicy.FailFast),
            ["Leaf"], "NodeB", NativeRuntimePolicy.FailFast);
        var downgraded = (byte[])package.Clone();
        var profile = 18 + downgraded[7];
        Assert.That(Encoding.ASCII.GetString(downgraded, profile, downgraded[8]), Is.EqualTo("native-20"));
        downgraded[profile + downgraded[8] - 1] = (byte)'9';
        Rehash(downgraded);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(downgraded))!;

        var imageStart = 18 + package[7] + package[8] + 2
            + BinaryPrimitives.ReadInt32LittleEndian(package.AsSpan(10, 4));
        var invalidData = (byte[])package.Clone();
        var pe = imageStart + BinaryPrimitives.ReadInt32LittleEndian(invalidData.AsSpan(imageStart + 0x3C, 4));
        var dataSection = pe + 24 + 0xF0 + (2 * 40);
        Assert.That(invalidData.AsSpan(dataSection, 8).StartsWith(".data"u8), Is.True);
        invalidData[dataSection + 4] = (byte)'X';
        Rehash(invalidData);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(invalidData))!;
        invalidData = (byte[])package.Clone();
        var dataOffset = BinaryPrimitives.ReadInt32LittleEndian(invalidData.AsSpan(dataSection + 20, 4));
        invalidData[imageStart + dataOffset] = 1;
        Rehash(invalidData);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(invalidData))!;

        var invalidImport = (byte[])package.Clone();
        var alloc = invalidImport.AsSpan(imageStart,
            BinaryPrimitives.ReadInt32LittleEndian(invalidImport.AsSpan(14, 4))).IndexOf("HeapAlloc\0"u8);
        Assert.That(alloc >= 0, Is.True);
        invalidImport[imageStart + alloc + 4] = (byte)'X';
        Rehash(invalidImport);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(invalidImport))!;
    }
    [TestCase("public Cycle Leak() { require Cycle.TryCreate(42, out var node); return node; }","Leak")]
    [TestCase("public Cycle? Leak() { return null; }","Leak")]
    [TestCase("public bool Leak(out Cycle? node) { node = null; return true; }","Leak")]
    public void ManagedClassHandlesCannotCrossNativeExports(string declaration, string export)
    {
        var source = """
            class Cycle {
                public int32 Value;
                public Cycle(int32 value) { Value = value; }
            }
            """ + declaration;
        var program = Compile(source, NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateProducer(
            program, [export], "NodeB", NativeRuntimePolicy.FailFast))!;
    }

    [Test]
    public async Task ManagedGraphProducerCanImportScalarProviderAsync()
    {
        var policy = NativeRuntimePolicy.FailFast;
        var dependency = NativeModuleCompiler.CreateProducer(
            Compile("public int32 External() { return 1; }", policy), ["External"], "Other", policy);
        var source = Sample("managed-class-leaf").Replace("return result;",
            "return result + External();", StringComparison.Ordinal);
        var imported = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            NativeModuleCompiler.CreateReference(dependency), (BoundRuntimePolicy)policy);
        Assert.That(imported.HasErrors, Is.False, string.Join(Environment.NewLine, imported.Diagnostics));
        var graph = NativeModuleCompiler.CreateGraphProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = imported.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(),
            ["Leaf"], "NodeB", [dependency], policy);
        Assert.That(GraphSemanticVersion(graph), Is.EqualTo(76));
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile("int32 Main() { return Leaf(); }",
            policy, NativeModuleCompiler.CreateGraphReference([graph]), library: false), [graph], policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(root, [graph, dependency],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(43));
        var program = Compile(Sample("managed-class-leaf"), policy);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.CreateGraphProducer(
            program, ["Leaf"], "NodeB", [dependency], policy))!;
    }

    private static int Find(byte[] bytes, byte[] pattern)
    {
        var index = bytes.AsSpan().IndexOf(pattern);
        Assert.That(index >= 0, Is.True);
        Assert.That(bytes.AsSpan(index + 1).IndexOf(pattern), Is.EqualTo(-1));
        return index;
    }

    private static void Rehash(byte[] package) =>
        SHA256.HashData(package.AsSpan(0, package.Length - 32)).CopyTo(package, package.Length - 32);

    private static async Task<int> ExecuteAsync(byte[] image)
    {
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"managed-class-module-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true })
                ?? throw new InvalidOperationException("Failed to start linked native program.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }
                throw;
            }
            return process.ExitCode;
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }

    private static BoundProgram Compile(string source, NativeRuntimePolicy policy,
        CompilationReference? reference = null, bool library = true)
    {
        var syntax = SyntaxTree.Parse(source);
        var boundPolicy = (BoundRuntimePolicy)policy;
        var compilation = library
            ? reference is null ? Compilation.CreateLibrary(syntax, boundPolicy)
                : Compilation.CreateLibrary(syntax, reference, boundPolicy)
            : Compilation.Create(syntax, ((global::System.Func<CompilationReference>)(() => { var __nunitActual1 = reference; Assert.That(__nunitActual1, Is.TypeOf<CompilationReference>()); return (CompilationReference)__nunitActual1!; }))(), boundPolicy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
    }

    private static string Sample(string name) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "samples", "DependencyGraphServicing", name + ".terrum"));

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", ".."));
}
