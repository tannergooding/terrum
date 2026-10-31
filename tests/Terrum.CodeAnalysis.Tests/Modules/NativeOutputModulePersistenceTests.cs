// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Tests.Native.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules;

public sealed class NativeOutputModulePersistenceTests
{
    private const int RevisionOffset = sizeof(int);
    private const int Revision100 = 100;

    private const string OutputSource = """
        [Blittable]
        [Layout(LayoutKind.Sequential)]
        public struct Packet { public uint32 Value; }
        [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
        extern int32 ReadValue(
            [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
            write ptr<Packet> value);
        public int32 ReadAndReturn() {
            init Packet value;
            unsafe {
                if (ReadValue(&init value) == 0) { return -1; }
            }
            return int32(value.Value);
        }
        """;
    private const string DirectOutputDeclarationSource = """
        [Blittable]
        [Layout(LayoutKind.Sequential)]
        public struct Packet { public uint32 Value; }
        [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
        public extern int32 ReadValue(
            [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
            write ptr<Packet> packet);
        """;
    private const string NativeCounterOutputSource = """
        [Blittable]
        [Layout(LayoutKind.Sequential)]
        public struct Counter { public int64 Value; }
        [Terrum.Interop.NativeImport("kernel32.dll", "QueryPerformanceCounter")]
        public extern int32 ReadCounter(
            [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
            write ptr<Counter> counter);
        """;
    private const string NativeScalarAndPointerOutputSource = """
        [Terrum.Interop.NativeImport("kernel32.dll", "QueryPerformanceCounter")]
        public extern int32 ReadCounter(
            [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
            write ptr<int64> counter);

        [Terrum.Interop.NativeImport("kernel32.dll", "GetModuleHandleExW")]
        [Terrum.Interop.NativeParameter(2, Read, NoEscape, 0)]
        public extern int32 ReadModule(uint32 flags, ptr<void> moduleName,
            [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
            write ptr<ptr<void>> module);
        """;
    private const string NativeHandlePointerOutputSource = """
        [Terrum.Interop.NativeImport("kernel32.dll", "GetCurrentProcess")]
        extern ptr<void> GetCurrentProcess();

        [Terrum.Interop.NativeImport("kernel32.dll", "CreateEventW")]
        extern ptr<void> CreateEventW(ptr<void> security, bool manualReset,
            bool initialState, ptr<void> name);

        [Terrum.Interop.NativeImport("kernel32.dll", "DuplicateHandle")]
        extern int32 DuplicateHandle(ptr<void> sourceProcess, ptr<void> sourceHandle,
            ptr<void> targetProcess,
            [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
            write ptr<ptr<void>> targetHandle,
            uint32 desiredAccess, bool inheritHandle, uint32 options);

        [Terrum.Interop.NativeImport("kernel32.dll", "CloseHandle")]
        extern int32 CloseHandle(ptr<void> handle);

        public int32 ExercisePointerOutput() {
            unsafe {
                ptr<void> process = GetCurrentProcess();
                ptr<void> original = CreateEventW(null, false, false, null);
                if (original == null) { return -1; }

                init ptr<void> duplicate;
                int32 status = DuplicateHandle(process, original, process, &init duplicate,
                    0, false, 2);
                if (status == 0) {
                    if (CloseHandle(original) == 0) { return -2; }
                    return -3;
                }
                if (duplicate == null) {
                    int32 closeOriginal = CloseHandle(original);
                    return closeOriginal == 0 ? -4 : -5;
                }

                int32 closeDuplicate = CloseHandle(duplicate);
                int32 closeOriginal = CloseHandle(original);
                if (closeDuplicate == 0 || closeOriginal == 0) { return -6; }
                return 0;
            }
        }
        """;

    [Test]
    public void ProducerUsesExactRevision100AndRejectsOtherRevisions()
    {
        var artifact = CreateOutputProducer();
        Assert.That(ReadRevision(artifact), Is.EqualTo(Revision100));
        Assert.That(ModuleCompiler.CreateReference(artifact), Is.Not.Null);

        foreach (var revision in new[] {
            76, 79, 80, 82, 83, 84, 85, 86, 87, 88, 89, 92,
            93, 94, 95, 96, 97, 98, 99, 101,
        })
        {
            var forged = (byte[])artifact.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(RevisionOffset), revision);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forged),
                $"revision {revision} must be rejected before decoding");
        }
    }

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task FlatFixedArrayOperationsRoundTripThroughRevision100Async(
        BoundRuntimePolicy policy)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 ReadArray() {
                mut [uint32; 3] values = [10, 20, 30];
                values[1] = values[0] + 1;
                unsafe {
                    ptr<uint32> selected = &values[2];
                    return int32(values[1] + *selected);
                }
            }
            """), policy);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var artifact = ModuleCompiler.CreateProducer(program, ["ReadArray"]);
        Assert.That(ReadRevision(artifact), Is.EqualTo(Revision100));

        using var stream = new MemoryStream(artifact, writable: false);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadInt32();
        _ = reader.ReadInt32();
        SkipModuleString(reader);
        _ = reader.ReadBoolean();
        _ = reader.ReadByte();
        _ = reader.ReadBoolean();
        SkipModuleString(reader);
        Assert.That(reader.ReadInt32(), Is.Zero);
        _ = reader.ReadInt32();
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.Zero);
        var pointerTypeCount = reader.ReadInt32();
        Assert.That(pointerTypeCount, Is.EqualTo(1));
        Assert.That(reader.ReadInt32(), Is.EqualTo((int)PrimitiveType.UInt32));
        Assert.That(reader.ReadBoolean(), Is.False);
        var arrayTypeCount = reader.ReadInt32();
        var arrayRowOffset = (int)stream.Position;
        Assert.That(arrayTypeCount, Is.EqualTo(1));
        Assert.That(reader.ReadInt32(), Is.EqualTo((int)PrimitiveType.UInt32));
        Assert.That(reader.ReadUInt64(), Is.EqualTo(3));
        Assert.That(reader.ReadUInt64(), Is.EqualTo(4));
        Assert.That(reader.ReadUInt64(), Is.EqualTo(12));
        Assert.That(reader.ReadUInt64(), Is.EqualTo(4));
        Assert.That(reader.ReadUInt64(), Is.EqualTo(1));
        Assert.That(reader.ReadInt32(), Is.EqualTo(1));
        Assert.That(ModuleCompiler.CreateReference(artifact), Is.Not.Null);
        var malformed = (byte[])artifact.Clone();
        BinaryPrimitives.WriteUInt64LittleEndian(
            malformed.AsSpan(arrayRowOffset + sizeof(int) + (sizeof(ulong) * 2)),
            11);
        Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(malformed));
        var invalidElementType = (byte[])artifact.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(
            invalidElementType.AsSpan(arrayRowOffset),
            (int)NativeFixedArrayTypes.Type(1));
        Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(invalidElementType));
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(
            artifact.AsSpan(0, arrayRowOffset + (44 * arrayTypeCount) - 1)
                .ToArray()));

        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                int32 Main() {
                    mut [uint16; 2] small = [1, 2];
                    mut [uint32; 3] matching = [7, 8, 9];
                    return ReadArray() + int32(small[0]) + int32(matching[1]);
                }
                """),
            ModuleCompiler.CreateReference(artifact), policy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, artifact);
        var linked = ModuleCompiler.Link(consumer, artifact);
        Assert.That(NativeJit.Execute(linked, (NativeRuntimePolicy)policy),
            Is.EqualTo(50));
        var image = NativeEmitter.Emit(linked, (NativeRuntimePolicy)policy);
        Assert.That(image, Is.Not.Empty);
        Assert.That(await ExecuteNativeImageAsync(image).ConfigureAwait(true),
            Is.EqualTo(50));
    }

    [Test]
    public void FlatFixedArrayBodiesRoundTripThroughRevision100ModuleSets()
    {
        var firstCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Add(int32 value) { return value; }"));
        var secondCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 AddTwo(int32 value) { return value; }"));
        Assert.That(firstCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, firstCompilation.Diagnostics));
        Assert.That(secondCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, secondCompilation.Diagnostics));
        var first = ModuleCompiler.CreateProducer(firstCompilation.Program!,
            ["Add"]);
        var second = ModuleCompiler.CreateProducer(secondCompilation.Program!,
            ["AddTwo"]);
        var reference = ModuleCompiler.CreateReferenceSet([first, second]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut [uint32; 2] values = [20, 22];
                return Add(int32(values[0])) + AddTwo(int32(values[1]));
            }
            """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));

        var consumer = ModuleCompiler.CreateConsumerSet(
            consumerCompilation.Program!, [first, second]);
        Assert.That(ReadRevision(consumer), Is.EqualTo(Revision100));
        ModuleCompiler.ValidateConsumerSet(consumer, [first, second]);
    }

    [Test]
    public void FlatFixedArrayBodiesRoundTripThroughRevision100GraphLibraries()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 ReadArray() {
                mut [uint32; 2] values = [20, 22];
                return int32(values[0] + values[1]);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var dependencies = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast);

        var graph = ModuleCompiler.CreateGraphLibrary(compilation.Program!,
            ["ReadArray"], dependencies);
        Assert.That(ReadRevision(graph), Is.EqualTo(Revision100));
        Assert.That(ModuleCompiler.CreateGraphReference(graph, ["ReadArray"]),
            Is.Not.Null);
    }

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void FlatArrayProviderReplacementRemapsPrivateRows(
        BoundRuntimePolicy policy)
    {
        var originalCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 ReadArray() {
                mut [uint32; 3] values = [10, 20, 30];
                return int32(values[1]);
            }
            """), policy);
        var replacementCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 ReadArray() {
                mut [uint64; 1] values = [42];
                return int32(values[0]);
            }
            """), policy);
        Assert.That(originalCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, originalCompilation.Diagnostics));
        Assert.That(replacementCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, replacementCompilation.Diagnostics));
        var original = ModuleCompiler.CreateProducer(
            originalCompilation.Program!, ["ReadArray"]);
        var replacement = ModuleCompiler.CreateProducer(
            replacementCompilation.Program!, ["ReadArray"]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut [uint16; 2] local = [1, 2];
                return ReadArray() + int32(local[0]);
            }
            """), ModuleCompiler.CreateReference(original), policy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, original);

        var nativePolicy = (NativeRuntimePolicy)policy;
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, original),
            nativePolicy), Is.EqualTo(21));
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, replacement),
            nativePolicy), Is.EqualTo(43));
    }

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void FlatArrayElementRetainsItsSelectedProviderLayout(
        BoundRuntimePolicy policy)
    {
        const string OriginalSource = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Element {
                public uint32 Value;
                public Element(uint32 value) { this.Value = value; }
            }
            public Element Make(uint32 value) { return new Element(value); }
            public int32 Read(Element value) { return int32(value.Value); }
            """;
        const string ChangedSource = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Element {
                public uint32 Value;
                public uint32 Padding;
                public Element(uint32 value) {
                    this.Value = value;
                    this.Padding = 0;
                }
            }
            public Element Make(uint32 value) { return new Element(value); }
            public int32 Read(Element value) { return int32(value.Value); }
            """;

        var originalCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(OriginalSource), policy);
        var changedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(ChangedSource), policy);
        Assert.That(originalCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, originalCompilation.Diagnostics));
        Assert.That(changedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, changedCompilation.Diagnostics));
        var original = ModuleCompiler.CreateProducer(
            originalCompilation.Program!, ["Make", "Read"]);
        var changed = ModuleCompiler.CreateProducer(
            changedCompilation.Program!, ["Make", "Read"]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut [Element; 2] values = [Make(20), Make(22)];
                return Read(values[0]) + Read(values[1]);
            }
            """), ModuleCompiler.CreateReference(original), policy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, original);

        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, original),
            (NativeRuntimePolicy)policy), Is.EqualTo(42));
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(consumer, changed));
    }

    [Test]
    public void ProducerPrunesUnreferencedArraysAndCanonicalizesFirstUseOrder()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            uint32 Seed() { return 10; }
            public int32 ReadArray() {
                mut [uint32; 3] values = [Seed(), 20, 30];
                return int32(values[0]);
            }
            int32 UnusedArray() {
                mut [uint16; 2] values = [1, 2];
                return int32(values[0]);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        Assert.That(program.NativeFixedArrayTypes, Has.Length.EqualTo(2));
        Assert.That(program.Functions.Single(function =>
            function.Name == "ReadArray").Variables.Any(variable =>
                NativeFixedArrayTypes.IsType(variable.Type)), Is.True);
        var sourceCallNames = new List<string>();
        var sourceTraversalCompleted = BoundTreeTraversal.VisitExpressions(
            (BoundBlock)program.Functions.Single(function =>
                function.Name == "ReadArray").Body,
            expression => {
                if (expression is BoundCall call)
                {
                    sourceCallNames.Add(program.Functions[call.Function].Name);
                }
            });
        Assert.That(sourceTraversalCompleted, Is.True);
        Assert.That(sourceCallNames, Does.Contain("Seed"));

        var producer = ModuleCompiler.CreateProducer(program, ["ReadArray"]);
        var (arrayCount, functionCount, nextSectionOffset) =
            ReadArrayTypeCount(producer);
        Assert.That(arrayCount, Is.EqualTo(1),
            $"revision={ReadRevision(producer)}; functionCount={functionCount}; "
            + $"nextSectionOffset={nextSectionOffset}");

        var remappedFunctions = program.Functions.Select(function =>
            BoundTreeTraversal.RewriteTypes(function, type =>
                NativeFixedArrayTypes.IsType(type)
                    ? NativeFixedArrayTypes.Type(
                        1 - NativeFixedArrayTypes.Index(type))
                    : type)).ToImmutableArray();
        var permutedProgram = program with {
            Functions = remappedFunctions,
            NativeFixedArrayTypes =
                [.. program.NativeFixedArrayTypes.Reverse()],
        };
        var permutedProducer = ModuleCompiler.CreateProducer(
            permutedProgram, ["ReadArray"]);
        Assert.That(permutedProducer, Is.EqualTo(producer));
    }

    [Test]
    public void FlatArrayOperationsUseRevision100FramingVectors()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Probe() {
                mut [uint32; 2] values = [10, 20];
                values[0] = 30;
                unsafe {
                    ptr<uint32> selected = &values[1];
                    return int32(values[0] + *selected) - 50;
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = ModuleCompiler.CreateProducer(compilation.Program!,
            ["Probe"]);
        Assert.That(ReadRevision(artifact), Is.EqualTo(Revision100));
        Assert.That(FindFlatArrayRecord(artifact, 43,
            NativeFixedArrayTypes.Type(0), 59), Is.GreaterThanOrEqualTo(0));
        Assert.That(FindFlatArrayRecord(artifact, 44,
            PrimitiveType.UInt32, 42), Is.GreaterThanOrEqualTo(0));
        Assert.That(FindFlatArrayRecord(artifact, 45,
            NativePointerTypes.Type(0), 42), Is.GreaterThanOrEqualTo(0));
        Assert.That(FindFlatArrayRecord(artifact, 46,
            PrimitiveType.Void, 63), Is.GreaterThanOrEqualTo(0));
        var initializerOffset = FindFlatArrayRecord(artifact, 43,
            NativeFixedArrayTypes.Type(0), 59);
        var shortInitializer = (byte[])artifact.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(
            shortInitializer.AsSpan(initializerOffset + 13, sizeof(int)), 1);
        Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(shortInitializer));

        var elementOffset = FindFlatArrayRecord(artifact, 44,
            PrimitiveType.UInt32, 42);
        var wrongElementLocal = (byte[])artifact.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(
            wrongElementLocal.AsSpan(elementOffset + 13, sizeof(int)), 1);
        Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(wrongElementLocal));

        var addressOffset = FindFlatArrayRecord(artifact, 45,
            NativePointerTypes.Type(0), 42);
        var onePastAddress = (byte[])artifact.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(
            onePastAddress.AsSpan(addressOffset + 21 + 13, sizeof(int)), 2);
        Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(onePastAddress));

        var writeOffset = FindFlatArrayRecord(artifact, 46,
            PrimitiveType.Void, 63);
        var wrongWriteValue = (byte[])artifact.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(
            wrongWriteValue.AsSpan(writeOffset + 42 + 1, sizeof(int)),
            (int)PrimitiveType.Int32);
        Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(wrongWriteValue));
    }

    [Test]
    public void MetadataOnlyNestedAndPointerArrayRecipesSurviveRevision100()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Value() { return 42; }"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var arrays = new List<BoundNativeFixedArrayType>();
        Assert.That(NativeFixedArrayContract.TryCreateType(
            PrimitiveType.UInt32, 3, program.Structures, arrays,
            out var innerType, out _), Is.True);
        Assert.That(NativeFixedArrayContract.TryCreateType(
            innerType, 2, program.Structures, arrays,
            out var outerType, out _), Is.True);
        var pointerType = NativePointerTypes.Type(0);
        var functionPointerType = NativeFunctionPointerTypes.Type(0);
        var pointer = new NativePointerTypeDefinition(outerType, false);
        var signature = new NativeFunctionPointerSignatureDefinition(
            NativeCallingConvention.Native, PrimitiveType.Void, [pointerType]);
        var metadataProgram = program with {
            NativeFixedArrayTypes = [.. arrays],
            NativePointerTypes = [pointer],
            NativeFunctionPointerSignatures = [signature],
        };

        var artifact = ModuleCompiler.CreateProducer(metadataProgram, ["Value"]);
        Assert.That(ReadRevision(artifact), Is.EqualTo(Revision100));
        using var stream = new MemoryStream(artifact, writable: false);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadInt32();
        _ = reader.ReadInt32();
        SkipModuleString(reader);
        _ = reader.ReadBoolean();
        _ = reader.ReadByte();
        _ = reader.ReadBoolean();
        SkipModuleString(reader);
        Assert.That(reader.ReadInt32(), Is.Zero);
        _ = reader.ReadInt32();
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.Zero);
        var signatureCount = reader.ReadInt32();
        Assert.That(signatureCount, Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte)NativeCallingConvention.Native));
        Assert.That(reader.ReadInt32(), Is.EqualTo((int)PrimitiveType.Void));
        Assert.That(reader.ReadInt32(), Is.EqualTo(1));
        Assert.That(reader.ReadInt32(), Is.EqualTo((int)pointerType));
        var pointerCount = reader.ReadInt32();
        Assert.That(pointerCount, Is.EqualTo(1));
        Assert.That(reader.ReadInt32(),
            Is.EqualTo((int)NativeFixedArrayTypes.Type(1)));
        Assert.That(reader.ReadBoolean(), Is.False);
        var arrayCount = reader.ReadInt32();
        Assert.That(arrayCount, Is.EqualTo(2));
        Assert.That(reader.ReadInt32(), Is.EqualTo((int)PrimitiveType.UInt32));
        Assert.That(reader.ReadUInt64(), Is.EqualTo(3));
        Assert.That(reader.ReadUInt64(), Is.EqualTo(4));
        Assert.That(reader.ReadUInt64(), Is.EqualTo(12));
        Assert.That(reader.ReadUInt64(), Is.EqualTo(4));
        Assert.That(reader.ReadUInt64(), Is.EqualTo(1));
        Assert.That(reader.ReadInt32(),
            Is.EqualTo((int)NativeFixedArrayTypes.Type(0)));
        Assert.That(reader.ReadUInt64(), Is.EqualTo(2));
        Assert.That(reader.ReadUInt64(), Is.EqualTo(12));
        Assert.That(reader.ReadUInt64(), Is.EqualTo(24));
        Assert.That(reader.ReadUInt64(), Is.EqualTo(4));
        Assert.That(reader.ReadUInt64(), Is.EqualTo(1));
        Assert.That(ModuleCompiler.CreateReference(artifact), Is.Not.Null);

        var reorderedArrays = ImmutableArray.Create(
            arrays[1] with { ElementType = NativeFixedArrayTypes.Type(1) },
            arrays[0]);
        var reorderedProgram = metadataProgram with {
            NativeFixedArrayTypes = reorderedArrays,
            NativePointerTypes = [
                pointer with { PointeeType = NativeFixedArrayTypes.Type(0) },
            ],
        };
        Assert.That(ModuleCompiler.CreateProducer(reorderedProgram, ["Value"]),
            Is.EqualTo(artifact));

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut [uint16; 2] local = [1, 2];
                return Value() + int32(local[0]);
            }
            """), ModuleCompiler.CreateReference(artifact));
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, artifact);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, artifact),
            NativeRuntimePolicy.FailFast), Is.EqualTo(43));

        var metadataFunction = metadataProgram.Functions.Single();
        var pointerValueFunction = metadataFunction with {
            Variables = [new BoundVariable("arrayAddress", pointerType, false,
                default)],
            ParameterCount = 1,
            RequiresUnsafeCaller = true,
            DeclaredRequiresUnsafeCaller = true,
        };
        var runtimePointerProgram = metadataProgram with {
            Functions = [pointerValueFunction],
        };
        Assert.Throws<NotSupportedException>(() =>
            ModuleCompiler.CreateProducer(runtimePointerProgram, ["Value"]));
    }

    [Test]
    public void Revision100RejectsRuntimeNestedArrayLocals()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Nested() {
                mut [[uint32; 2]; 2] values = [[1, 2], [3, 4]];
                return 0;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(compilation.Program!, ["Nested"]));
    }

    [Test]
    public void Revision100DistinguishesZeroPayloadNominalElementsFromUnit()
    {
        var structures = ImmutableArray.Create(new BoundStruct("ZeroNominal",
            [new BoundStructField("Tag", PrimitiveType.Void, 0, 0)], 0) {
            IsTransparent = true,
            BlittableTargetRequirement =
                FixedLayoutStorage.WindowsX64TargetRequirement,
        });
        var nominalArrays = new List<BoundNativeFixedArrayType>();
        Assert.That(NativeFixedArrayContract.TryCreateType(
            (PrimitiveType)100, 0, structures, nominalArrays,
            out var nominalArrayType, out var nominalLayout), Is.True);
        Assert.That(nominalLayout.ElementStride, Is.Zero);
        Assert.That(NativeFixedArrayContract.IsValidTable(structures,
            nominalArrays), Is.True);
        Assert.That(NativeFixedArrayContract.IsRevision100RuntimeArrayLayout(
            nominalLayout, structures, nominalArrays, [], []), Is.False);

        var unitArrays = new List<BoundNativeFixedArrayType>();
        Assert.That(NativeFixedArrayContract.TryCreateType(
            PrimitiveType.Void, 0, structures, unitArrays,
            out _, out var unitLayout), Is.True);
        Assert.That(unitLayout.ElementType, Is.Not.EqualTo(nominalLayout.ElementType));
        Assert.That(unitLayout, Is.EqualTo(nominalLayout with {
            CarrierType = unitLayout.CarrierType,
            ElementType = PrimitiveType.Void,
        }));
        Assert.That(NativeFixedArrayContract.IsRevision100RuntimeArrayLayout(
            unitLayout, structures, unitArrays, [], []), Is.True);
    }

    [Test]
    public void Revision100RejectsRecursivePointerArrayMetadataRecipes()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Value() { return 42; }"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var pointerType = NativePointerTypes.Type(0);
        var arrays = new List<BoundNativeFixedArrayType>();
        Assert.That(NativeFixedArrayContract.TryCreateType(pointerType, 1,
            program.Structures, arrays, out var arrayType, out _), Is.True);
        var forged = program with {
            NativeFixedArrayTypes = [.. arrays],
            NativePointerTypes = [
                new NativePointerTypeDefinition(arrayType, false),
            ],
        };
        Assert.That(NativeFixedArrayContract.IsValidTable(forged.Structures,
            forged.NativeFixedArrayTypes), Is.True);
        Assert.That(NativePointerTypeContract.IsValidTable(
            forged.NativePointerTypes, forged.Structures,
            forged.NativeFunctionPointerSignatures,
            forged.NativeFixedArrayTypes), Is.False);
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(forged, ["Value"]));
    }

    [Test]
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task UnitFixedArrayOperationsRoundTripThroughRevision100Async(
        BoundRuntimePolicy policy)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 UnitArray() {
                mut [void; 2] values = [(), ()];
                values[1] = values[0];
                return 42;
            }
            """), policy);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(compilation.Program!,
            ["UnitArray"]);
        Assert.That(ReadArrayTypeCount(producer).ArrayCount,
            Is.EqualTo(1));
        Assert.That(ReadSingleArrayRow(producer), Is.EqualTo(
            new BoundNativeFixedArrayType(PrimitiveType.Void, 2, 0, 0, 1, 1)));
        _ = FindFlatArrayRecord(producer, 43,
            NativeFixedArrayTypes.Type(0), 59,
            elementType: PrimitiveType.Void);
        _ = FindFlatArrayRecord(producer, 44, PrimitiveType.Void, 42,
            elementType: PrimitiveType.Void);
        var initializerOffset = FindFlatArrayRecord(producer, 43,
            NativeFixedArrayTypes.Type(0), 59,
            elementType: PrimitiveType.Void);
        var invalidUnitPayload = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt64LittleEndian(
            invalidUnitPayload.AsSpan(initializerOffset + 17 + 13,
                sizeof(long)), 1);
        Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(invalidUnitPayload));

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return UnitArray(); }"),
            ModuleCompiler.CreateReference(producer), policy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        var nativePolicy = (NativeRuntimePolicy)policy;
        Assert.That(NativeJit.Execute(linked, nativePolicy), Is.EqualTo(42));
        var image = NativeEmitter.Emit(linked, nativePolicy);
        Assert.That(await ExecuteNativeImageAsync(image).ConfigureAwait(true),
            Is.EqualTo(42));
    }

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task EmptyPositivePayloadArrayRoundTripsThroughRevision100Async(
        BoundRuntimePolicy policy)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 EmptyArray() {
                mut [uint32; 0] values = [];
                return 42;
            }
            """), policy);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            compilation.Program!, ["EmptyArray"]);
        Assert.That(ReadSingleArrayRow(producer), Is.EqualTo(
            new BoundNativeFixedArrayType(PrimitiveType.UInt32, 0, 4, 0, 4, 1)));
        _ = FindFlatArrayRecord(producer, 43,
            NativeFixedArrayTypes.Type(0), 17, elementCount: 0);

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return EmptyArray(); }"),
            ModuleCompiler.CreateReference(producer), policy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        var nativePolicy = (NativeRuntimePolicy)policy;
        Assert.That(NativeJit.Execute(linked, nativePolicy), Is.EqualTo(42));
        Assert.That(await ExecuteNativeImageAsync(
            NativeEmitter.Emit(linked, nativePolicy)).ConfigureAwait(true),
            Is.EqualTo(42));
    }

    [Test]
    public void MetadataOnlyArrayRecipesMergeThroughPrivateSetsAndGraphs()
    {
        var first = CreateMetadataRecipeReference("First", reverseRows: false);
        var second = CreateMetadataRecipeReference("Second", reverseRows: true);

        var set = ModuleCompiler.CreateReferenceSetFromReferences([first, second]);
        var graph = ModuleCompiler.MergeGraphReferences([first, second]);
        foreach (var reference in new[] { set, graph })
        {
            Assert.That(reference.NativeFixedArrayTypes, Has.Length.EqualTo(2));
            Assert.That(reference.NativeFixedArrayTypes[0].ElementType,
                Is.EqualTo(PrimitiveType.UInt32));
            Assert.That(reference.NativeFixedArrayTypes[0].ElementCount,
                Is.EqualTo(3));
            Assert.That(reference.NativeFixedArrayTypes[1].ElementType,
                Is.EqualTo(NativeFixedArrayTypes.Type(0)));
            Assert.That(reference.NativeFixedArrayTypes[1].ElementCount,
                Is.EqualTo(2));
            Assert.That(reference.NativePointerTypes, Has.Length.EqualTo(1));
            Assert.That(reference.NativePointerTypes[0].PointeeType,
                Is.EqualTo(NativeFixedArrayTypes.Type(1)));
            Assert.That(reference.NativeFunctionPointerSignatures,
                Has.Length.EqualTo(1));
            Assert.That(reference.NativeFunctionPointerSignatures[0]
                .ParameterTypes, Is.EqualTo([NativePointerTypes.Type(0)]));
        }
    }

    [Test]
    public void MetadataOnlyArrayFieldsMergeThroughPrivateSetsAndGraphs()
    {
        var first = CreateMetadataArrayFieldReference(
            "FirstField", "FirstDescription", reverseRows: false);
        var second = CreateMetadataArrayFieldReference(
            "SecondField", "SecondDescription", reverseRows: true);

        var set = ModuleCompiler.CreateReferenceSetFromReferences(
            [first, second]);
        var graph = ModuleCompiler.MergeGraphReferences([first, second]);
        AssertMetadataArrayField(set, "FirstDescription");
        AssertMetadataArrayField(set, "SecondDescription");
        AssertMetadataArrayField(graph, "FirstDescription");
        AssertMetadataArrayField(graph, "SecondDescription");
    }

    [Test]
    public void MetadataOnlyArrayFieldsSurvivePrivateLinkRemapping()
    {
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public int32 Value() { return 42; }"));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerProgram = AddMetadataArrayField(
            providerCompilation.Program!, "ProviderDescription",
            reverseRows: true);
        var provider = ModuleCompiler.CreateProducer(providerProgram, ["Value"]);
        var providerReference = ModuleCompiler.CreateReference(provider);
        Assert.That(providerReference.NativeFixedArrayTypes, Is.Empty);
        Assert.That(providerReference.Structures.Any(structure =>
            structure.Name == "ProviderDescription"), Is.False);

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Value(); }"),
            providerReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumerProgram = AddMetadataArrayField(
            consumerCompilation.Program!, "ConsumerDescription",
            reverseRows: false);
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram, provider);
        var linked = ModuleCompiler.Link(consumer, provider);

        AssertMetadataArrayField(linked, "ConsumerDescription");
        AssertMetadataArrayField(linked, "$producer.ProviderDescription");
        Assert.That(NativeJit.Execute(linked, NativeRuntimePolicy.FailFast),
            Is.EqualTo(42));
    }

    [Test]
    public void MetadataOnlyArrayFieldsRejectMalformedRowsAndRuntimeCarriers()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Value() { return 42; }"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = AddMetadataArrayField(compilation.Program!,
            "MetadataDescription", reverseRows: false);
        var structureIndex = program.Structures.Length - 1;
        var malformedStructure = program.Structures[structureIndex] with {
            Fields = [program.Structures[structureIndex].Fields[0] with {
                Type = NativeFixedArrayTypes.Type(
                    program.NativeFixedArrayTypes.Length),
            }],
        };
        var malformed = program with {
            Structures = program.Structures.SetItem(structureIndex,
                malformedStructure),
        };
        Assert.Throws<NotSupportedException>(() =>
            ModuleCompiler.CreateProducer(malformed, ["Value"]));

        var artifact = ModuleCompiler.CreateProducer(program, ["Value"]);
        var forgedArtifact = (byte[])artifact.Clone();
        var fieldTypeOffset = forgedArtifact.AsSpan().IndexOf(
            BitConverter.GetBytes((int)NativeFixedArrayTypes.Type(0)));
        Assert.That(fieldTypeOffset, Is.GreaterThanOrEqualTo(0));
        BinaryPrimitives.WriteInt32LittleEndian(
            forgedArtifact.AsSpan(fieldTypeOffset),
            (int)NativeFixedArrayTypes.Type(99));
        Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(forgedArtifact));

        var structureType = (PrimitiveType)(100 + structureIndex);
        var function = program.Functions[0] with {
            Variables = [new BoundVariable("metadata", structureType, false,
                default)],
        };
        var runtimeCarrier = program with { Functions = [function] };
        Assert.Throws<NotSupportedException>(() =>
            ModuleCompiler.CreateProducer(runtimeCarrier, ["Value"]));

        var classRuntimeCarrier = program with {
            Classes = [
                new BoundClass("MetadataPointerCarrier", [
                    new BoundStructField("Items",
                        NativePointerTypes.Type(0), 0, sizeof(ulong)) {
                        IsPrivate = true,
                    },
                ], sizeof(ulong)) {
                    IsInternal = true,
                },
            ],
        };
        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(classRuntimeCarrier, ["Value"]));
        var nestedClassRuntimeCarrier = ImmutableArray.Create(
            new BoundClass("PointerOwner", [
                new BoundStructField("Nested",
                    (PrimitiveType)(ManagedClassTypes.First + 1), 0,
                    sizeof(ulong)),
            ], sizeof(ulong)),
            new BoundClass("PointerHolder", [
                new BoundStructField("Items",
                    NativePointerTypes.Type(0), 0, sizeof(ulong)),
            ], sizeof(ulong)));
        Assert.That(NativeFixedArrayContract.HasMetadataOnlyArrayClassFields(
            nestedClassRuntimeCarrier, program.Structures,
            program.NativeFixedArrayTypes, program.NativePointerTypes,
            program.NativeFunctionPointerSignatures), Is.True);
    }

    [Test]
    public void PrivateConsumerReaderRejectsArrayDerivedManagedClassFields()
    {
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public int32 Export() { return 42; }"));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = ModuleCompiler.CreateProducer(
            providerCompilation.Program!, ["Export"]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            int32 Main() { return Export(); }
            """), ModuleCompiler.CreateReference(provider));
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var program = AddMetadataArrayField(consumerCompilation.Program!,
            "ConsumerDescription", reverseRows: false);

        var consumer = ModuleCompiler.CreateConsumer(program, provider);
        var (pointerTableOffset, classFieldTypeOffset) =
            FindPrivateTypeTableOffsets(consumer, "Leaf", "Value");
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            consumer.AsSpan(pointerTableOffset)), Is.EqualTo(1));
        var malformed = consumer.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            malformed.AsSpan(pointerTableOffset + sizeof(int)),
            (int)NativeFixedArrayTypes.Type(0));
        BinaryPrimitives.WriteInt32LittleEndian(
            malformed.AsSpan(classFieldTypeOffset),
            (int)NativePointerTypes.Type(0));
        Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.GetConsumerCallImports(malformed));
    }

    [Test]
    public async Task MetadataOnlyArrayRecipesSurviveNativePackageValidationAsync()
    {
        var metadataProgram = CreateMetadataRecipeProgram();
        var producer = NativeModuleCompiler.CreateProducer(metadataProgram,
            ["Value"], "ArrayMetadata", NativeRuntimePolicy.FailFast);
        var reference = NativeModuleCompiler.CreateReference(producer);
        Assert.That(reference.Functions.Length, Is.EqualTo(1));

        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Value(); }"), reference,
            BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer,
            NativeRuntimePolicy.FailFast);
        Assert.That(await NativeModuleCompiler.RunAsync(consumer, producer,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true), Is.EqualTo(42));
    }

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task PrivateNativeModuleSetExecutesFlatArrayProvidersAsync(
        BoundRuntimePolicy policy)
    {
        var nativePolicy = (NativeRuntimePolicy)policy;
        var arrayProviderCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 ReadArray() {
                mut [uint32; 2] values = [20, 22];
                return int32(values[0] + values[1]);
            }
            """), policy);
        var scalarProviderCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Identity(int32 value) { return value; }"), policy);
        Assert.That(arrayProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, arrayProviderCompilation.Diagnostics));
        Assert.That(scalarProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, scalarProviderCompilation.Diagnostics));
        var arrayProviderProgram = AddMetadataArrayField(
            arrayProviderCompilation.Program!, "SetArrayDescription",
            reverseRows: true);
        var arrayProvider = NativeModuleCompiler.CreateProducer(
            arrayProviderProgram, ["ReadArray"], "ArrayValues",
            nativePolicy);
        var scalarProvider = NativeModuleCompiler.CreateProducer(
            scalarProviderCompilation.Program!, ["Identity"], "ArrayIdentity",
            nativePolicy);
        var reference = NativeModuleCompiler.CreateReferenceSet(
            [arrayProvider, scalarProvider]);
        Assert.That(reference.NativeFixedArrayTypes, Is.Empty);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut [uint32; 2] local = [1, 2];
                return ReadArray() + Identity(int32(local[0]));
            }
            """), reference, policy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumerSet(
            consumerCompilation.Program!, [arrayProvider, scalarProvider],
            nativePolicy);

        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumer,
            [scalarProvider, arrayProvider], TimeSpan.FromSeconds(10),
            nativePolicy).ConfigureAwait(true), Is.EqualTo(43));

        var replacementCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 ReadArray() {
                mut [uint64; 1] values = [50];
                return int32(values[0]);
            }
            """), policy);
        Assert.That(replacementCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, replacementCompilation.Diagnostics));
        var replacementProgram = AddMetadataArrayField(
            replacementCompilation.Program!,
            "SetReplacementDescription", reverseRows: false);
        var replacementProvider = NativeModuleCompiler.CreateProducer(
            replacementProgram, ["ReadArray"], "ArrayValues",
            nativePolicy);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(consumer,
            [replacementProvider, scalarProvider], TimeSpan.FromSeconds(10),
            nativePolicy).ConfigureAwait(true), Is.EqualTo(51));
    }

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task PrivateNativeGraphExecutesFlatArrayProviderAsync(
        BoundRuntimePolicy policy)
    {
        var nativePolicy = (NativeRuntimePolicy)policy;
        var baseCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Identity(int32 value) { return value; }"), policy);
        Assert.That(baseCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, baseCompilation.Diagnostics));
        var baseProvider = NativeModuleCompiler.CreateProducer(
            baseCompilation.Program!, ["Identity"], "ArrayGraphBase",
            nativePolicy);
        var graphCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 ReadArray() {
                mut [uint32; 2] values = [20, 22];
                return Identity(int32(values[0] + values[1]));
            }
            """), NativeModuleCompiler.CreateGraphReference([baseProvider]),
            policy);
        Assert.That(graphCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphProgram = AddMetadataArrayField(
            graphCompilation.Program!, "GraphArrayDescription",
            reverseRows: true);
        var graphProvider = NativeModuleCompiler.CreateGraphProducer(
            graphProgram, ["ReadArray"], "ArrayGraphValues",
            [baseProvider], nativePolicy);
        var graphReference = NativeModuleCompiler.CreateGraphReference(
            [graphProvider, baseProvider]);
        Assert.That(graphReference.NativeFixedArrayTypes, Is.Empty);

        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return ReadArray(); }"),
            graphReference, policy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateGraphConsumer(
            consumerCompilation.Program!, [graphProvider, baseProvider],
            nativePolicy);

        Assert.That(await NativeModuleCompiler.RunGraphAsync(consumer,
            [graphProvider, baseProvider], TimeSpan.FromSeconds(10),
            nativePolicy).ConfigureAwait(true), Is.EqualTo(42));

        var replacementGraphCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public int32 ReadArray() {
                    mut [uint64; 1] values = [50];
                    return Identity(int32(values[0]));
                }
                """), NativeModuleCompiler.CreateGraphReference([baseProvider]),
            policy);
        Assert.That(replacementGraphCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                replacementGraphCompilation.Diagnostics));
        var replacementGraphProgram = AddMetadataArrayField(
            replacementGraphCompilation.Program!,
            "GraphReplacementDescription", reverseRows: false);
        var replacementGraph = NativeModuleCompiler.CreateGraphProducer(
            replacementGraphProgram, ["ReadArray"],
            "ArrayGraphValues", [baseProvider], nativePolicy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(consumer,
            [replacementGraph, baseProvider], TimeSpan.FromSeconds(10),
            nativePolicy).ConfigureAwait(true), Is.EqualTo(50));
    }

    [Test]
    public void ConsumerRejectsRetiredNoThrowTokenInImportedContract()
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Helper() { return 40; }"));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = ModuleCompiler.CreateProducer(providerCompilation.Program!, ["Helper"]);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Helper(); }"),
            ModuleCompiler.CreateReference(provider));
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(consumerCompilation.Program!, provider);
        var forged = InsertRetiredNoThrowToken(consumer, "Helper");

        var exception = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.Link(forged, provider));
        Assert.That(exception!.Message,
            Is.EqualTo("Module declaration contains an invalid or retired effect token."));
    }

    [Test]
    public void NoThrowIsStillAcceptedAsAnOrdinaryFunctionName()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 NoThrow() { return 40; }"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(compilation.Program!, ["NoThrow"]);

        Assert.That(ModuleCompiler.CreateReference(producer), Is.Not.Null);
    }

    [Test]
    public void ReaderRejectsForgedNativeOutputReservationLocal()
    {
        var forged = CreateOutputProducer();
        var reservationOffset = FindReservation(forged);
        BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(reservationOffset + 13), int.MaxValue);

        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forged));
    }

    [Test]
    public void ReaderRejectsForgedNativeOutputReservationPointee()
    {
        var forged = CreateOutputProducer();
        var reservationOffset = FindReservation(forged);
        BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(reservationOffset + 17),
            (int)PrimitiveType.Int32);

        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forged));
    }

    [Test]
    public void ReaderRejectsNativeOutputTypeThatDiffersFromItsFormal()
    {
        var forged = CreateDirectOutputDeclarationProducer("NonZero");
        var outputOffset = FindOutputDependencyOffset(forged, "ReadValue") - 20;
        BinaryPrimitives.WriteInt32LittleEndian(
            forged.AsSpan(outputOffset + sizeof(uint)), (int)PrimitiveType.Int32);

        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forged));
    }

    [Test]
    public void OutputReservationsSurviveTypeTableReorderingOnReplacement()
    {
        var original = CreateOutputWrapperProducer(packetFirst: false);
        var reordered = CreateOutputWrapperProducer(packetFirst: true);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return ReadAndReturn(); }"),
            ModuleCompiler.CreateReference(original));
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(consumerCompilation.Program!, original);

        _ = ModuleCompiler.Link(consumer, reordered);
    }

    [Test]
    public void NonemptyNativeOutputDependenciesRoundTripAndSurviveReplacementRemapping()
    {
        var original = CreateDependencyOutputDeclarationProducer(packetFirst: false);
        var reordered = CreateDependencyOutputDeclarationProducer(packetFirst: true);
        var originalReference = ModuleCompiler.CreateReference(original);
        var reorderedReference = ModuleCompiler.CreateReference(reordered);
        AssertOutputDependencyProjection(originalReference.Functions, originalReference.Structures);
        AssertOutputDependencyProjection(reorderedReference.Functions, reorderedReference.Structures);

        var helper = ModuleCompiler.CreateProducer(
            Compilation.CreateLibrary(SyntaxTree.Parse("public int32 Helper() { return 40; }")).Program!,
            ["Helper"]);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse(CreateDependencyOutputConsumerSource(packetFirst: false)),
            ModuleCompiler.CreateReference(helper));
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(consumerCompilation.Program!, helper);
        var linked = ModuleCompiler.Link(consumer, helper);
        AssertOutputDependencyProjection(linked.Functions, linked.Structures);

        var reorderedConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse(CreateDependencyOutputConsumerSource(packetFirst: true)),
            ModuleCompiler.CreateReference(helper));
        Assert.That(reorderedConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, reorderedConsumerCompilation.Diagnostics));
        var reorderedConsumer = ModuleCompiler.CreateConsumer(
            reorderedConsumerCompilation.Program!, helper);
        var reorderedLinked = ModuleCompiler.Link(reorderedConsumer, helper);
        AssertOutputDependencyProjection(reorderedLinked.Functions, reorderedLinked.Structures);

        var replacement = ModuleCompiler.CreateProducer(
            Compilation.CreateLibrary(SyntaxTree.Parse("public int32 Helper() { return 41; }")).Program!,
            ["Helper"]);
        var replacementLinked = ModuleCompiler.Link(reorderedConsumer, replacement);
        AssertOutputDependencyProjection(replacementLinked.Functions, replacementLinked.Structures);
    }

    [Test]
    public void ImportedNativeOutputDependenciesBindAndRejectProviderChanges()
    {
        var original = CreateDependencyOutputDeclarationProducer(packetFirst: false);
        var reordered = CreateDependencyOutputDeclarationProducer(packetFirst: true);
        var empty = CreateDependencyOutputDeclarationProducer(
            packetFirst: false, includeSources: false);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int32 source = 7;
                init Packet packet;
                unsafe {
                    if (ReadValue(&source, &init packet) == 0) { return -1; }
                }
                return int32(packet.Value);
            }
            """), ModuleCompiler.CreateReference(original));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var consumer = ModuleCompiler.CreateConsumer(compilation.Program!, original);
        var linked = ModuleCompiler.Link(consumer, reordered);
        AssertOutputDependencyProjection(linked.Functions, linked.Structures);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer, empty));
    }

    [Test]
    public void ImportedDirectOutputBindsToProviderAndRejectsChangedOutputContract()
    {
        var provider = CreateDirectOutputDeclarationProducer("NonZero");
        var changed = CreateDirectOutputDeclarationProducer("NonNegative");
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init Packet packet;
                unsafe {
                    if (ReadValue(&init packet) == 0) { return -1; }
                }
                return 42;
            }
            """), ModuleCompiler.CreateReference(provider));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var consumer = ModuleCompiler.CreateConsumer(compilation.Program!, provider);
        var linked = ModuleCompiler.Link(consumer, provider);
        AssertEmptyOutputProjection(linked.Functions, linked.Structures);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer, changed));
    }

    [Test]
    public void SourceRejectsUniqueNativeOutputCells()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "ReadOwned")]
            extern int32 ReadOwned(
                [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<OwnedInt32Buffer> value);
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"),
            Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void NativePointerOutputContractsSurvivePrivateTypeReordering()
    {
        var original = CreatePointerOutputDeclarationProducer(reverse: false);
        var reordered = CreatePointerOutputDeclarationProducer(reverse: true);
        var originalReference = ModuleCompiler.CreateReference(original);
        var reorderedReference = ModuleCompiler.CreateReference(reordered);
        Assert.That(originalReference.NativePointerTypes.Select(static pointer => pointer.PointeeType),
            Is.Not.EqualTo(reorderedReference.NativePointerTypes.Select(static pointer => pointer.PointeeType)));
        Assert.That(originalReference.NativeFunctionPointerSignatures,
            Is.Not.EqualTo(reorderedReference.NativeFunctionPointerSignatures));

        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init ptr<int32> value;
                init fnptr<NativeAbi, int32, int32> callback;
                unsafe {
                    if (ReadPointer32(&init value) == 0) { return -1; }
                    if (ReadCallback32(&init callback) == 0) { return -2; }
                }
                return 0;
            }
            """), originalReference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(compilation.Program!, original);

        var linked = ModuleCompiler.Link(consumer, reordered);
        var callbackOutput = linked.Functions.Single(static function =>
            function.Name == "ReadCallback32").NativeOutputContracts.Single();
        Assert.That(NativeFunctionPointerTypes.IsType(callbackOutput.PointeeType), Is.True);
        var callbackSignature = linked.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(callbackOutput.PointeeType)];
        Assert.That(callbackSignature.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(callbackSignature.ParameterTypes, Is.EqualTo(new[] { PrimitiveType.Int32 }));
    }

    [Test]
    public void NativePointerOutputDependenciesRoundTrip()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [param(value): DependsOn(nameof(source))]
            [param(value): ForwardsBacking(nameof(source))]
            [Terrum.Interop.NativeImport("example.dll", "ReadPointer")]
            public extern int32 ReadPointer(ptr<int32> source,
                [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<ptr<int32>> value);
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = ModuleCompiler.CreateProducer(compilation.Program!, ["ReadPointer"]);
        var reference = ModuleCompiler.CreateReference(artifact);
        var output = reference.Functions.Single(static function =>
            function.Name == "ReadPointer").NativeOutputContracts.Single();

        Assert.That(NativePointerTypes.IsType(output.PointeeType), Is.True);
        Assert.That(output.Dependencies, Is.EqualTo(new[]
        {
            new NativeOutputDependency(2, NativeOutputDependencySourceKind.Formal,
                NativeOutputDependencyRelationship.Forward,
                NativeOutputPublicationPhase.NormalCompletion, 1, 0),
        }));
    }

    [TestCase("char8", PrimitiveType.Char8)]
    [TestCase("char32", PrimitiveType.Char32)]
    [TestCase("rune", PrimitiveType.Rune)]
    [TestCase("int8", PrimitiveType.Int8)]
    [TestCase("int16", PrimitiveType.Int16)]
    [TestCase("uint8", PrimitiveType.UInt8)]
    [TestCase("uint16", PrimitiveType.UInt16)]
    public void NativeOutputScalarCellsPreserveTheirExactType(string typeName,
        PrimitiveType expectedType)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($"""
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            public extern int32 ReadValue(
                [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<{typeName}> value);
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = ModuleCompiler.CreateProducer(compilation.Program!, ["ReadValue"]);
        var reference = ModuleCompiler.CreateReference(artifact);
        var output = reference.Functions.Single(static function =>
            function.Name == "ReadValue").NativeOutputContracts.Single();

        Assert.That(output.PointeeType, Is.EqualTo(expectedType));
    }

    [Test]
    public void NativePointerOutputDependencyRetainsAndChecksInputBacking()
    {
        const string Imports = """
            [param(result): DependsOn(nameof(source))]
            [param(result): ForwardsBacking(nameof(source))]
            [Terrum.Interop.NativeImport("example.dll", "GetPointer")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            extern int32 GetPointer(ptr<void> source,
                [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<ptr<void>> result);

            [Terrum.Interop.NativeImport("example.dll", "ObservePointer")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            extern int32 ObservePointer(ptr<void> value);
            """;
        var valid = Compilation.Create(SyntaxTree.Parse(Imports + """
            int32 Main() {
                mut int32 backing = 7;
                init ptr<void> result;
                unsafe {
                    if (GetPointer(&backing, &init result) == 0) { return -1; }
                    return ObservePointer(result);
                }
            }
            """));
        Assert.That(valid.HasErrors, Is.False,
            string.Join(Environment.NewLine, valid.Diagnostics));

        var invalid = Compilation.Create(SyntaxTree.Parse(Imports + """
            int32 Main() {
                init ptr<void> result;
                {
                    mut int32 backing = 7;
                    unsafe {
                        if (GetPointer(&backing, &init result) == 0) { return -1; }
                    }
                }
                unsafe { return ObservePointer(result); }
            }
            """));
        var diagnostic = invalid.Diagnostics.Single();
        Assert.That(diagnostic.Code, Is.EqualTo("TRM3016"));
        Assert.That(diagnostic.Message, Does.Contain("NativePointer"));
    }

    [Test]
    public void SourceDirectOutputImportStillRejectsProviderNameCollision()
    {
        var provider = CreateDirectOutputDeclarationProducer("NonZero");
        var compilation = Compilation.Create(SyntaxTree.Parse(DirectOutputDeclarationSource + """
            int32 Main() {
                init Packet packet;
                unsafe {
                    if (ReadValue(&init packet) == 0) { return -1; }
                }
                return int32(packet.Value);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        Assert.Throws<ModuleContractException>(
            () => ModuleCompiler.CreateConsumer(compilation.Program!, provider));
    }

    [Test]
    public void NativePackageExportsOwnedDirectOutputThroughValidatedLeafThunk()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(DirectOutputDeclarationSource));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var provider = NativeModuleCompiler.CreateProducer(compilation.Program!,
            ["ReadValue"], "OutputProvider", NativeRuntimePolicy.FailFast);

        var info = NativeModuleCompiler.Inspect(provider);
        Assert.That(info.Identity, Is.EqualTo("OutputProvider"));
        Assert.That(info.IsConsumer, Is.False);
    }

    [Test]
    public void NativeImportExpectationsRemapPointeeTypesAndRejectParameterChanges()
    {
        var original = CreateNativeParameterOutputProvider(packetFirst: false, extent: 1);
        var reordered = CreateNativeParameterOutputProvider(packetFirst: true, extent: 1);
        var changed = CreateNativeParameterOutputProvider(packetFirst: true, extent: 2);
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 0; }"), NativeModuleCompiler.CreateReference(original));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumer(
            compilation.Program!, original, NativeRuntimePolicy.FailFast);
        var machineImportCountOffset = 18 + consumer[7] + consumer[8];
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            consumer.AsSpan(machineImportCountOffset, sizeof(ushort))), Is.Zero);
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            consumer.AsSpan(machineImportCountOffset + sizeof(ushort), sizeof(ushort))),
            Is.EqualTo(1));

        NativeModuleCompiler.Validate(consumer, reordered);
        Assert.Throws<ModuleContractException>(
            () => NativeModuleCompiler.Validate(consumer, changed));
    }

    [Test]
    public void NativeImportExpectationsRemapNativePointerPointeeIdentities()
    {
        var original = CreateNestedNativeParameterOutputProvider(reverse: false, access: "Read");
        var reordered = CreateNestedNativeParameterOutputProvider(reverse: true, access: "Read");
        var changed = CreateNestedNativeParameterOutputProvider(reverse: true, access: "Write");
        var originalReference = NativeModuleCompiler.CreateReference(original);
        var reorderedReference = NativeModuleCompiler.CreateReference(reordered);
        Assert.That(originalReference.NativePointerTypes.Select(static pointer => pointer.PointeeType),
            Is.Not.EqualTo(reorderedReference.NativePointerTypes.Select(static pointer => pointer.PointeeType)));
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 0; }"), originalReference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumer(
            compilation.Program!, original, NativeRuntimePolicy.FailFast);

        NativeModuleCompiler.Validate(consumer, reordered);
        Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.Validate(consumer, changed));
    }

    [Test]
    public void NativePackageDirectImportThunkMustTargetItsDeclaredIatSlot()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(NativeCounterOutputSource));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(compilation.Program!,
            ["ReadCounter"], "CounterProvider", NativeRuntimePolicy.FailFast);
        var forged = (byte[])provider.Clone();
        var imageOffset = PackageImageOffset(forged);
        var imageLength = BinaryPrimitives.ReadInt32LittleEndian(forged.AsSpan(14, sizeof(int)));
        using var reader = new PEReader(new MemoryStream(forged, imageOffset, imageLength));
        var headers = reader.PEHeaders;
        var text = headers.SectionHeaders.Single(static section => section.Name == ".text");
        var exports = headers.SectionHeaders.Single(static section => section.Name == ".edata");
        var exportOffset = imageOffset + exports.PointerToRawData;
        var functionsRva = BinaryPrimitives.ReadInt32LittleEndian(forged.AsSpan(exportOffset + 28, 4));
        var functionOffset = imageOffset + exports.PointerToRawData
            + functionsRva - exports.VirtualAddress;
        var thunkRva = BinaryPrimitives.ReadInt32LittleEndian(forged.AsSpan(functionOffset, 4));
        var thunkOffset = imageOffset + text.PointerToRawData + thunkRva - text.VirtualAddress;
        Assert.That(forged[thunkOffset], Is.EqualTo(0xFF));
        Assert.That(forged[thunkOffset + 1], Is.EqualTo(0x25));
        var displacement = BinaryPrimitives.ReadInt32LittleEndian(
            forged.AsSpan(thunkOffset + 2, sizeof(int)));
        BinaryPrimitives.WriteInt32LittleEndian(
            forged.AsSpan(thunkOffset + 2, sizeof(int)), displacement + 1);
        System.Security.Cryptography.SHA256.HashData(forged.AsSpan(0, forged.Length - 32))
            .CopyTo(forged, forged.Length - 32);

        Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(forged));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async System.Threading.Tasks.Task NativePackageDirectOutputThunkExecutesSelectedProvider(
        NativeRuntimePolicy runtimePolicy)
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(NativeCounterOutputSource),
            (BoundRuntimePolicy)runtimePolicy);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(providerCompilation.Program!,
            ["ReadCounter"], "CounterProvider", runtimePolicy);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init Counter counter;
                unsafe {
                    if (ReadCounter(&init counter) == 0) { return -1; }
                }
                if (counter.Value <= 0) { return -2; }
                return 0;
            }
            """), NativeModuleCompiler.CreateReference(provider), (BoundRuntimePolicy)runtimePolicy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, provider, runtimePolicy);

        Assert.That(await NativeModuleCompiler.RunAsync(consumer, provider, TimeSpan.FromSeconds(30)),
            Is.Zero);

        var changedSource = NativeCounterOutputSource.Replace(
            "QueryPerformanceCounter", "QueryPerformanceFrequency", StringComparison.Ordinal);
        var changedCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(changedSource),
            (BoundRuntimePolicy)runtimePolicy);
        Assert.That(changedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, changedCompilation.Diagnostics));
        var changedProvider = NativeModuleCompiler.CreateProducer(changedCompilation.Program!,
            ["ReadCounter"], "CounterProvider", runtimePolicy);
        Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.Validate(consumer, changedProvider));

        var changedSelectorCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            NativeCounterOutputSource.Replace("NonZero", "NonNegative", StringComparison.Ordinal)),
            (BoundRuntimePolicy)runtimePolicy);
        Assert.That(changedSelectorCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, changedSelectorCompilation.Diagnostics));
        var changedSelectorProvider = NativeModuleCompiler.CreateProducer(
            changedSelectorCompilation.Program!, ["ReadCounter"], "CounterProvider", runtimePolicy);
        Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.Validate(consumer, changedSelectorProvider));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async System.Threading.Tasks.Task NativePackageScalarOutputExecutesWithPointerContractPresent(
        NativeRuntimePolicy runtimePolicy)
    {
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(NativeScalarAndPointerOutputSource), (BoundRuntimePolicy)runtimePolicy);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(providerCompilation.Program!,
            ["ReadCounter", "ReadModule"], "ScalarPointerOutputProvider", runtimePolicy);
        var providerReference = NativeModuleCompiler.CreateReference(provider);
        var moduleDeclaration = providerReference.Functions.Single(static function =>
            function.Name == "ReadModule");
        Assert.That(moduleDeclaration.NativeOutputContracts.Single().PointeeType,
            Is.EqualTo(PrimitiveType.NativeRawPointer));
        Assert.That(DirectNativeImportContract.IsValid(moduleDeclaration,
            providerReference.Structures, providerReference.NativeFunctionPointerSignatures,
            providerReference.NativePointerTypes), Is.True);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init int64 counter;
                unsafe {
                    if (ReadCounter(&init counter) == 0) { return -1; }
                }
                if (counter <= int64(0)) { return -2; }
                return 0;
            }
            """), providerReference, (BoundRuntimePolicy)runtimePolicy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, provider, runtimePolicy);

        Assert.That(await NativeModuleCompiler.RunAsync(
            consumer, provider, TimeSpan.FromSeconds(30)), Is.Zero);

        var changedSource = NativeScalarAndPointerOutputSource.Replace(
            "write ptr<ptr<void>> module", "write ptr<uint32> module", StringComparison.Ordinal);
        var changedCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(changedSource), (BoundRuntimePolicy)runtimePolicy);
        Assert.That(changedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, changedCompilation.Diagnostics));
        var changedProvider = NativeModuleCompiler.CreateProducer(changedCompilation.Program!,
            ["ReadCounter", "ReadModule"], "ScalarPointerOutputProvider", runtimePolicy);
        Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.Validate(consumer, changedProvider));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async System.Threading.Tasks.Task NativePackagePointerOutputExecutesWithExplicitHandleCleanup(
        NativeRuntimePolicy runtimePolicy)
    {
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(NativeHandlePointerOutputSource), (BoundRuntimePolicy)runtimePolicy);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(providerCompilation.Program!,
            ["ExercisePointerOutput"], "PointerOutputProvider", runtimePolicy);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() { return ExercisePointerOutput(); }
            """), NativeModuleCompiler.CreateReference(provider), (BoundRuntimePolicy)runtimePolicy);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, provider, runtimePolicy);

        Assert.That(await NativeModuleCompiler.RunAsync(
            consumer, provider, TimeSpan.FromSeconds(30)), Is.Zero);
    }

    [Test]
    public void NativePackageSourceDirectOutputImportStillRejectsProviderCollision()
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(NativeCounterOutputSource));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(providerCompilation.Program!,
            ["ReadCounter"], "CounterProvider", NativeRuntimePolicy.FailFast);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(NativeCounterOutputSource + """
            int32 Main() {
                init Counter counter;
                unsafe {
                    if (ReadCounter(&init counter) == 0) { return -1; }
                }
                return 0;
            }
            """));
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));

        Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.CreateConsumer(
                consumerCompilation.Program!, provider, NativeRuntimePolicy.FailFast));
    }

    [TestCase("foreign-library")]
    [TestCase("parameter-count")]
    [TestCase("provider-identity")]
    public void NativeConsumerRejectsMalformedDirectImportExpectations(string malformedField)
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(NativeCounterOutputSource));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(providerCompilation.Program!,
            ["ReadCounter"], "CounterProvider", NativeRuntimePolicy.FailFast);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init Counter counter;
                unsafe {
                    if (ReadCounter(&init counter) == 0) { return -1; }
                }
                return 0;
            }
            """), NativeModuleCompiler.CreateReference(provider));
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, provider, NativeRuntimePolicy.FailFast);
        var forged = (byte[])consumer.Clone();
        switch (malformedField)
        {
            case "foreign-library":
                forged[FindDirectExpectationStringOffset(forged, "ReadCounter", 3)] = (byte)'/';
                break;
            case "parameter-count":
                var countOffset = FindDirectExpectationParameterCountOffset(forged, "ReadCounter");
                BinaryPrimitives.WriteUInt16LittleEndian(forged.AsSpan(countOffset), 1);
                break;
            case "provider-identity":
                forged[FindDirectExpectationStringOffset(forged, "ReadCounter", 1)] = (byte)'Z';
                break;
            default:
                throw new InvalidOperationException("Unsupported direct expectation mutation.");
        }
        System.Security.Cryptography.SHA256.HashData(forged.AsSpan(0, forged.Length - 32))
            .CopyTo(forged, forged.Length - 32);

        if (malformedField == "provider-identity")
        {
            Assert.Throws<ModuleContractException>(
                () => NativeModuleCompiler.Validate(forged, provider));
        }
        else
        {
            Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(forged));
        }
    }

    [TestCase("borrow")]
    [TestCase("receiver")]
    [TestCase("source-ordinal")]
    public void ConsumerReaderRejectsUnsupportedNativeOutputDependencyRows(string forgedField)
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Helper() { return 40; }"));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = ModuleCompiler.CreateProducer(providerCompilation.Program!, ["Helper"]);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse(CreateDependencyOutputConsumerSource(packetFirst: false)),
            ModuleCompiler.CreateReference(provider));
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(consumerCompilation.Program!, provider);
        _ = ModuleCompiler.Link(consumer, provider);
        var forged = (byte[])consumer.Clone();
        var dependencyOffset = FindOutputDependencyOffset(forged, "ReadValue");
        switch (forgedField)
        {
            case "borrow":
                forged[dependencyOffset + 2] = (byte)NativeOutputDependencyRelationship.Borrow;
                break;
            case "receiver":
                forged[dependencyOffset + 1] = (byte)NativeOutputDependencySourceKind.Receiver;
                break;
            case "source-ordinal":
                BinaryPrimitives.WriteUInt32LittleEndian(
                    forged.AsSpan(dependencyOffset + 8), uint.MaxValue);
                break;
            default:
                throw new InvalidOperationException("Unsupported mutation case.");
        }

        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(forged, provider));
    }

    private static byte[] CreateOutputProducer()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(OutputSource));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ModuleCompiler.CreateProducer(compilation.Program!, ["ReadAndReturn"]);
    }

    private static CompilationReference CreateMetadataRecipeReference(
        string functionName, bool reverseRows)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"public int32 {functionName}() {{ return 42; }}"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var arrays = new List<BoundNativeFixedArrayType>();
        Assert.That(NativeFixedArrayContract.TryCreateType(
            PrimitiveType.UInt32, 3, program.Structures, arrays,
            out var innerType, out _), Is.True);
        Assert.That(NativeFixedArrayContract.TryCreateType(
            innerType, 2, program.Structures, arrays,
            out var outerType, out _), Is.True);
        if (reverseRows)
        {
            arrays = [
                arrays[1] with { ElementType = NativeFixedArrayTypes.Type(1) },
                arrays[0],
            ];
            outerType = NativeFixedArrayTypes.Type(0);
        }
        var pointerType = NativePointerTypes.Type(0);
        var signature = new NativeFunctionPointerSignatureDefinition(
            NativeCallingConvention.Native, PrimitiveType.Void, [pointerType]);
        return new CompilationReference([], program.Functions,
            BoundRuntimePolicy.FailFast,
            nativeFunctionPointerSignatures: [signature],
            nativePointerTypes: [
                new NativePointerTypeDefinition(outerType, false),
            ],
            nativeFixedArrayTypes: [.. arrays]);
    }

    private static CompilationReference CreateMetadataArrayFieldReference(
        string functionName, string structureName, bool reverseRows)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"public int32 {functionName}() {{ return 42; }}"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var metadataProgram = AddMetadataArrayField(
            program, structureName, reverseRows);
        return new CompilationReference(metadataProgram.Structures,
            metadataProgram.Functions, BoundRuntimePolicy.FailFast,
            nativeFunctionPointerSignatures:
                metadataProgram.NativeFunctionPointerSignatures,
            nativePointerTypes: metadataProgram.NativePointerTypes,
            nativeFixedArrayTypes: metadataProgram.NativeFixedArrayTypes);
    }

    private static BoundProgram AddMetadataArrayField(
        BoundProgram program, string structureName, bool reverseRows)
    {
        var arrays = new List<BoundNativeFixedArrayType>(
            program.NativeFixedArrayTypes);
        var firstNewArrayIndex = arrays.Count;
        var elementCount = 2UL;
        PrimitiveType fieldType;
        BoundNativeFixedArrayLayout fieldLayout;
        do
        {
            Assert.That(NativeFixedArrayContract.TryCreateType(
                PrimitiveType.UInt16, elementCount, program.Structures,
                arrays, out fieldType, out fieldLayout), Is.True);
            if (NativeFixedArrayTypes.Index(fieldType)
                >= firstNewArrayIndex)
            {
                break;
            }
            elementCount++;
        } while (elementCount <= (ulong)(ValueLayout.MaximumStructureSize / 2));
        Assert.That(NativeFixedArrayTypes.Index(fieldType),
            Is.EqualTo(firstNewArrayIndex));
        Assert.That(fieldLayout.ByteExtent, Is.LessThanOrEqualTo(int.MaxValue));
        var structureIndex = program.Structures.Length;
        var structureType = (PrimitiveType)(100 + structureIndex);
        var structure = new BoundStruct(structureName, [
            new BoundStructField("Items", fieldType, 0,
                (int)fieldLayout.ByteExtent) {
                IsPrivate = true,
            },
        ], (int)fieldLayout.ByteExtent) {
            IsInternal = true,
            IsSequential = true,
            BlittableTargetRequirement =
                FixedLayoutStorage.WindowsX64TargetRequirement,
        };
        var structures = program.Structures.Add(structure);
        Assert.That(NativeFixedArrayContract.TryCreateType(
            structureType, 1, structures, arrays,
            out var outerType, out _), Is.True);
        if (reverseRows)
        {
            var outerIndex = NativeFixedArrayTypes.Index(outerType);
            (arrays[firstNewArrayIndex], arrays[outerIndex])
                = (arrays[outerIndex], arrays[firstNewArrayIndex]);
            fieldType = NativeFixedArrayTypes.Type(outerIndex);
            outerType = NativeFixedArrayTypes.Type(firstNewArrayIndex);
            structure = structure with {
                Fields = [structure.Fields[0] with { Type = fieldType }],
            };
            structures = structures.SetItem(structureIndex, structure);
        }

        var pointerType = NativePointerTypes.Type(
            program.NativePointerTypes.Length);
        var signature = new NativeFunctionPointerSignatureDefinition(
            NativeCallingConvention.Native, PrimitiveType.Void,
            [pointerType]);
        return program with {
            Structures = structures,
            NativeFixedArrayTypes = [.. arrays],
            NativePointerTypes = [
                .. program.NativePointerTypes,
                new NativePointerTypeDefinition(outerType, false),
            ],
            NativeFunctionPointerSignatures = [
                .. program.NativeFunctionPointerSignatures,
                signature,
            ],
        };
    }

    private static void AssertMetadataArrayField(
        CompilationReference reference, string structureName)
        => AssertMetadataArrayField(reference.Structures,
            reference.NativeFixedArrayTypes, structureName);

    private static void AssertMetadataArrayField(
        BoundProgram program, string structureName)
        => AssertMetadataArrayField(program.Structures,
            program.NativeFixedArrayTypes, structureName);

    private static void AssertMetadataArrayField(
        ImmutableArray<BoundStruct> structures,
        ImmutableArray<BoundNativeFixedArrayType> fixedArrays,
        string structureName)
    {
        var structureIndex = -1;
        for (var index = 0; index < structures.Length; index++)
        {
            if (structures[index].Name == structureName)
            {
                structureIndex = index;
                break;
            }
        }
        Assert.That(structureIndex, Is.GreaterThanOrEqualTo(0));
        var structure = structures[structureIndex];
        Assert.That(structure.Fields, Has.Length.EqualTo(1));
        var innerArrayIndex = -1;
        for (var index = 0; index < fixedArrays.Length;
            index++)
        {
            var array = fixedArrays[index];
            if (array.ElementType == PrimitiveType.UInt16
                && array.ElementCount == 2)
            {
                innerArrayIndex = index;
                break;
            }
        }
        Assert.That(innerArrayIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(structure.Fields[0].Type,
            Is.EqualTo(NativeFixedArrayTypes.Type(innerArrayIndex)));
        Assert.That(NativeFixedArrayContract.IsValidTable(
            structures, fixedArrays), Is.True);
    }

    private static BoundProgram CreateMetadataRecipeProgram()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Value() { return 42; }"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException("Expected a bound program.");
        var arrays = new List<BoundNativeFixedArrayType>();
        Assert.That(NativeFixedArrayContract.TryCreateType(
            PrimitiveType.UInt32, 3, program.Structures, arrays,
            out var innerType, out _), Is.True);
        Assert.That(NativeFixedArrayContract.TryCreateType(
            innerType, 2, program.Structures, arrays,
            out var outerType, out _), Is.True);
        var pointerType = NativePointerTypes.Type(0);
        var signature = new NativeFunctionPointerSignatureDefinition(
            NativeCallingConvention.Native, PrimitiveType.Void, [pointerType]);
        return program with {
            NativeFixedArrayTypes = [.. arrays],
            NativePointerTypes = [
                new NativePointerTypeDefinition(outerType, false),
            ],
            NativeFunctionPointerSignatures = [signature],
        };
    }

    private static byte[] CreateOutputWrapperProducer(bool packetFirst)
    {
        const string Anchor = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Anchor {
                public int32 Value;
                public Anchor(int32 value) { Value = value; }
            }
            """;
        const string Packet = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Packet { public uint32 Value; }
            """;
        var source = (packetFirst ? Packet + Anchor : Anchor + Packet)
            + """
                [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
                extern int32 ReadValue(
                    [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                    write ptr<Packet> value);
                public int32 ReadAndReturn() {
                    init Packet value;
                    unsafe {
                        if (ReadValue(&init value) == 0) { return -1; }
                    }
                    return int32(value.Value);
                }
                public int32 UseAnchor(Anchor value) { return value.Value; }
                """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ModuleCompiler.CreateProducer(compilation.Program!, ["ReadAndReturn", "UseAnchor"]);
    }

    private static byte[] CreateDependencyOutputDeclarationProducer(bool packetFirst,
        bool includeSources = true)
    {
        var source = DependencyOutputTypes(packetFirst)
            + (includeSources ? DependencyOutputAttributes : "")
            + """
                [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
                public extern int32 ReadValue(ptr<int32> source,
                    [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                    write ptr<Packet> packet);
                public int32 UseAnchor(Anchor value) { return value.Value; }
                """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ModuleCompiler.CreateProducer(compilation.Program!, ["ReadValue", "UseAnchor"]);
    }

    private static byte[] CreateDirectOutputDeclarationProducer(string resultCondition)
    {
        var source = DirectOutputDeclarationSource.Replace(
            "NonZero", resultCondition, StringComparison.Ordinal);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return ModuleCompiler.CreateProducer(compilation.Program!, ["ReadValue"]);
    }

    private static byte[] CreatePointerOutputDeclarationProducer(bool reverse)
    {
        const string Pointer32 = """
            [Terrum.Interop.NativeImport("example.dll", "ReadPointer32")]
            public extern int32 ReadPointer32(
                [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<ptr<int32>> value);
            """;
        const string Pointer64 = """
            [Terrum.Interop.NativeImport("example.dll", "ReadPointer64")]
            public extern int32 ReadPointer64(
                [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<ptr<int64>> value);
            """;
        const string Callback32 = """
            [Terrum.Interop.NativeImport("example.dll", "ReadCallback32")]
            public extern int32 ReadCallback32(
                [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<fnptr<NativeAbi, int32, int32>> callback);
            """;
        const string Callback64 = """
            [Terrum.Interop.NativeImport("example.dll", "ReadCallback64")]
            public extern int32 ReadCallback64(
                [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<fnptr<NativeAbi, int64, int64>> callback);
            """;
        var source = reverse
            ? Callback64 + Callback32 + Pointer64 + Pointer32
            : Pointer32 + Pointer64 + Callback32 + Callback64;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return ModuleCompiler.CreateProducer(compilation.Program!,
            ["ReadPointer32", "ReadPointer64", "ReadCallback32", "ReadCallback64"]);
    }

    private static byte[] CreateNativeParameterOutputProvider(bool packetFirst, int extent)
    {
        const string Anchor = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Anchor { public uint32 Value; }
            """;
        const string Packet = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Packet { public uint32 Value; }
            """;
        var source = (packetFirst ? Packet + Anchor : Anchor + Packet)
            + $$"""
                [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
                [Terrum.Interop.NativeParameter(1, Read, NoEscape, {{extent}})]
                public extern int32 ReadValue(ptr<Packet> source,
                    [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                    write ptr<Packet> packet);
                public int32 UseAnchor(Anchor value) { return int32(value.Value); }
                """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateProducer(compilation.Program!,
            ["ReadValue", "UseAnchor"], "ParameterProvider", NativeRuntimePolicy.FailFast);
    }

    private static byte[] CreateNestedNativeParameterOutputProvider(bool reverse, string access)
    {
        const string Packet = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Packet { public uint32 Value; }
            """;
        const string ReadInt32 = """
            [Terrum.Interop.NativeImport("example.dll", "ReadNestedInt32")]
            [Terrum.Interop.NativeParameter(1, ACCESS, NoEscape, 1)]
            public extern int32 ReadNestedInt32(POINTER source,
                [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<Packet> packet);
            """;
        const string ReadInt64 = """
            [Terrum.Interop.NativeImport("example.dll", "ReadNestedInt64")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            public extern int32 ReadNestedInt64(ptr<ptr<int64>> source,
                [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<Packet> packet);
            """;
        var pointerType = access switch
        {
            "Read" => "ptr<ptr<int32>>",
            "Write" => "write ptr<ptr<int32>>",
            _ => throw new ArgumentOutOfRangeException(nameof(access)),
        };
        var first = ReadInt32
            .Replace("ACCESS", access, StringComparison.Ordinal)
            .Replace("POINTER", pointerType, StringComparison.Ordinal);
        var declarations = reverse ? Packet + ReadInt64 + first : Packet + first + ReadInt64;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(declarations));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateProducer(compilation.Program!,
            ["ReadNestedInt32", "ReadNestedInt64"], "NestedParameterProvider",
            NativeRuntimePolicy.FailFast);
    }

    private static string DependencyOutputTypes(bool packetFirst)
    {
        const string Anchor = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Anchor {
                public int32 Value;
                public Anchor(int32 value) { Value = value; }
            }
            """;
        const string Packet = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Packet { public ptr<int32> Data; public uint32 Value; }
            """;
        return packetFirst ? Packet + Anchor : Anchor + Packet;
    }

    private const string DependencyOutputAttributes =
        "[param(packet): DependsOn(nameof(source))]\n"
        + "[param(packet): ForwardsBacking(nameof(source))]\n";

    private static string CreateDependencyOutputConsumerSource(bool packetFirst) =>
        DependencyOutputTypes(packetFirst)
        + DependencyOutputAttributes
        + """
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            extern int32 ReadValue(ptr<int32> source,
                [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<Packet> packet);
            int32 Main() {
                Anchor anchor = new Anchor(1);
                return Helper() + anchor.Value;
            }
            """;

    private static void AssertOutputDependencyProjection(
        IReadOnlyList<BoundFunction> functions, IReadOnlyList<BoundStruct> structures)
    {
        var function = functions.Single(static candidate => candidate.Name == "ReadValue");
        var output = function.NativeOutputContracts.Single();
        Assert.That(output.Dependencies, Is.EqualTo(new[]
        {
            new NativeOutputDependency(2, NativeOutputDependencySourceKind.Formal,
                NativeOutputDependencyRelationship.Forward,
                NativeOutputPublicationPhase.NormalCompletion, 1, 0),
        }));
        var pointeeIndex = (int)output.PointeeType - 100;
        Assert.That(pointeeIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(structures[pointeeIndex].Name, Is.EqualTo("Packet"));
    }

    private static void AssertEmptyOutputProjection(
        IReadOnlyList<BoundFunction> functions, IReadOnlyList<BoundStruct> structures)
    {
        var function = functions.Single(static candidate => candidate.Name == "ReadValue");
        var output = function.NativeOutputContracts.Single();
        Assert.That(output.FormalOrdinal, Is.Zero);
        Assert.That(output.Dependencies, Is.EqualTo(new[]
        {
            new NativeOutputDependency(2, NativeOutputDependencySourceKind.Empty,
                NativeOutputDependencyRelationship.Empty,
                NativeOutputPublicationPhase.NormalCompletion, 0, 0),
        }));
        var pointeeIndex = (int)output.PointeeType - 100;
        Assert.That(pointeeIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(structures[pointeeIndex].Name, Is.EqualTo("Packet"));
    }

    private static int FindOutputDependencyOffset(byte[] artifact, string functionName)
    {
        var name = System.Text.Encoding.UTF8.GetBytes(functionName);
        var nameRecord = new byte[sizeof(int) + name.Length];
        BinaryPrimitives.WriteInt32LittleEndian(nameRecord, name.Length);
        name.CopyTo(nameRecord, sizeof(int));
        var functionOffset = IndexOf(artifact, nameRecord);
        Assert.That(functionOffset, Is.GreaterThanOrEqualTo(0));
        var contractLengthOffset = functionOffset + nameRecord.Length;
        var contractLength = BinaryPrimitives.ReadInt32LittleEndian(
            artifact.AsSpan(contractLengthOffset, sizeof(int)));
        var outputCountOffset = contractLengthOffset + sizeof(int) + contractLength
            + 3 * sizeof(byte);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            artifact.AsSpan(outputCountOffset, sizeof(int))), Is.EqualTo(1));
        var outputStart = outputCountOffset + 8;
        var dependencyCount = BinaryPrimitives.ReadInt32LittleEndian(
            artifact.AsSpan(outputStart + 16, sizeof(int)));
        Assert.That(dependencyCount, Is.EqualTo(1));
        return outputStart + 20;
    }

    private static int FindDirectExpectationStringOffset(byte[] artifact,
        string functionName, int fieldIndex)
    {
        var at = 18 + artifact[7] + artifact[8];
        var importCount = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
        at += 2;
        for (var index = 0; index < importCount; index++)
        {
            var length = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
            at += 2 + length;
        }
        var expectationCount = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
        at += 2;
        Assert.That(expectationCount, Is.GreaterThan(0));
        for (var expectation = 0; expectation < expectationCount; expectation++)
        {
            var localName = string.Empty;
            for (var field = 0; field < 5; field++)
            {
                var length = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
                var valueOffset = at + 2;
                var value = System.Text.Encoding.UTF8.GetString(artifact, valueOffset, length);
                if (field == 0)
                {
                    localName = value;
                }
                if (localName == functionName && field == fieldIndex)
                {
                    return valueOffset;
                }
                at = valueOffset + length;
            }
            var parameterCount = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
            at += 2 + (parameterCount * 14);
        }
        throw new InvalidOperationException(
            $"Native package has no direct expectation field {fieldIndex} for '{functionName}'.");
    }

    private static int FindDirectExpectationParameterCountOffset(byte[] artifact,
        string functionName)
    {
        var at = 18 + artifact[7] + artifact[8];
        var importCount = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
        at += 2;
        for (var index = 0; index < importCount; index++)
        {
            var length = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
            at += 2 + length;
        }
        var expectationCount = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
        at += 2;
        for (var expectation = 0; expectation < expectationCount; expectation++)
        {
            var localName = string.Empty;
            for (var field = 0; field < 5; field++)
            {
                var length = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
                var valueOffset = at + 2;
                var value = System.Text.Encoding.UTF8.GetString(artifact, valueOffset, length);
                if (field == 0)
                {
                    localName = value;
                }
                at = valueOffset + length;
            }
            if (localName == functionName)
            {
                return at;
            }
            var parameterCount = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
            at += 2 + (parameterCount * 14);
        }
        throw new InvalidOperationException(
            $"Native package has no direct expectation parameter count for '{functionName}'.");
    }

    private static int PackageImageOffset(byte[] artifact)
        => NativePackageTestHelpers.ImageOffset(artifact);

    private static byte[] InsertRetiredNoThrowToken(byte[] artifact, string functionName)
    {
        var name = System.Text.Encoding.UTF8.GetBytes(functionName);
        var nameRecord = new byte[sizeof(int) + name.Length];
        BinaryPrimitives.WriteInt32LittleEndian(nameRecord, name.Length);
        name.CopyTo(nameRecord, sizeof(int));
        var functionOffset = IndexOf(artifact, nameRecord);
        Assert.That(functionOffset, Is.GreaterThanOrEqualTo(0));
        var contractLengthOffset = functionOffset + nameRecord.Length;
        var contractLength = BinaryPrimitives.ReadInt32LittleEndian(
            artifact.AsSpan(contractLengthOffset, sizeof(int)));
        var contractStart = contractLengthOffset + sizeof(int);
        var contract = System.Text.Encoding.UTF8.GetString(
            artifact, contractStart, contractLength);
        var separator = contract.IndexOf('|');
        Assert.That(separator, Is.GreaterThanOrEqualTo(0));
        var forgedContract = System.Text.Encoding.UTF8.GetBytes(
            contract[..(separator + 1)] + "NoThrow|" + contract[(separator + 1)..]);
        var result = new byte[artifact.Length + forgedContract.Length - contractLength];
        artifact.AsSpan(0, contractLengthOffset).CopyTo(result);
        BinaryPrimitives.WriteInt32LittleEndian(
            result.AsSpan(contractLengthOffset), forgedContract.Length);
        forgedContract.CopyTo(result.AsSpan(contractStart));
        artifact.AsSpan(contractStart + contractLength)
            .CopyTo(result.AsSpan(contractStart + forgedContract.Length));
        return result;
    }

    private static int IndexOf(byte[] source, byte[] value)
    {
        for (var index = 0; index <= source.Length - value.Length; index++)
        {
            if (source.AsSpan(index, value.Length).SequenceEqual(value))
            {
                return index;
            }
        }
        return -1;
    }

    private static int ReadRevision(byte[] artifact) =>
        BinaryPrimitives.ReadInt32LittleEndian(artifact.AsSpan(RevisionOffset));

    private static void SkipModuleString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (reader.ReadBytes(length).Length != length)
        {
            throw new EndOfStreamException();
        }
    }

    private static (int PointerTableOffset, int ClassFieldTypeOffset)
        FindPrivateTypeTableOffsets(byte[] artifact, string className,
            string fieldName)
    {
        using var stream = new MemoryStream(artifact, writable: false);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadInt32();
        Assert.That(reader.ReadInt32(), Is.EqualTo(Revision100));
        SkipModuleString(reader);
        _ = reader.ReadBoolean();
        _ = reader.ReadByte();
        _ = reader.ReadBoolean();
        SkipModuleString(reader);
        var bufferCount = reader.ReadInt32();
        for (var index = 0; index < bufferCount; index++)
        {
            SkipModuleString(reader);
            _ = reader.ReadInt32();
            _ = reader.ReadBoolean();
        }
        _ = reader.ReadInt32();
        var structureCount = reader.ReadInt32();
        for (var index = 0; index < structureCount; index++)
        {
            SkipModuleString(reader);
            _ = reader.ReadByte();
            _ = reader.ReadInt32();
            for (var flag = 0; flag < 4; flag++)
            {
                _ = reader.ReadBoolean();
            }
            _ = reader.ReadInt32();
            _ = reader.ReadBoolean();
            if (reader.ReadBoolean())
            {
                SkipModuleString(reader);
            }
            var fieldCount = reader.ReadInt32();
            for (var field = 0; field < fieldCount; field++)
            {
                SkipModuleString(reader);
                _ = reader.ReadInt32();
                _ = reader.ReadInt32();
                _ = reader.ReadInt32();
                for (var flag = 0; flag < 4; flag++)
                {
                    _ = reader.ReadBoolean();
                }
            }
        }
        var classCount = reader.ReadInt32();
        var classFieldTypeOffset = -1;
        for (var index = 0; index < classCount; index++)
        {
            var currentClassName = ReadModuleString(reader);
            _ = reader.ReadInt32();
            _ = reader.ReadBoolean();
            _ = reader.ReadBoolean();
            var fieldCount = reader.ReadInt32();
            for (var field = 0; field < fieldCount; field++)
            {
                var currentFieldName = ReadModuleString(reader);
                var fieldTypeOffset = checked((int)stream.Position);
                _ = reader.ReadInt32();
                if (currentClassName == className
                    && currentFieldName == fieldName)
                {
                    classFieldTypeOffset = fieldTypeOffset;
                }
                _ = reader.ReadInt32();
                _ = reader.ReadInt32();
                _ = reader.ReadBoolean();
                _ = reader.ReadBoolean();
                _ = reader.ReadBoolean();
            }
        }
        var importedTypeCount = reader.ReadInt32();
        for (var index = 0; index < importedTypeCount; index++)
        {
            SkipModuleString(reader);
        }
        var signatureCount = reader.ReadInt32();
        for (var index = 0; index < signatureCount; index++)
        {
            _ = reader.ReadByte();
            _ = reader.ReadInt32();
            var parameterCount = reader.ReadInt32();
            stream.Position += (long)parameterCount * sizeof(int);
        }

        Assert.That(classFieldTypeOffset, Is.GreaterThanOrEqualTo(0));
        return (checked((int)stream.Position), classFieldTypeOffset);
    }

    private static string ReadModuleString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
        {
            throw new EndOfStreamException();
        }
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static (int ArrayCount, int FunctionCount, long NextSectionOffset)
        ReadArrayTypeCount(byte[] artifact)
    {
        using var stream = new MemoryStream(artifact, writable: false);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadInt32();
        _ = reader.ReadInt32();
        SkipModuleString(reader);
        _ = reader.ReadBoolean();
        _ = reader.ReadByte();
        _ = reader.ReadBoolean();
        SkipModuleString(reader);
        Assert.That(reader.ReadInt32(), Is.Zero);
        _ = reader.ReadInt32();
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.Zero);
        var arrayCount = reader.ReadInt32();
        if (arrayCount is < 0 or > 1000)
        {
            return (arrayCount, -1, stream.Position);
        }
        stream.Position += (long)arrayCount * 44;
        return (arrayCount, reader.ReadInt32(), stream.Position);
    }

    private static int FindFlatArrayRecord(byte[] artifact, byte tag,
        PrimitiveType expressionType, int recordLength, int elementCount = 2,
        PrimitiveType elementType = PrimitiveType.UInt32)
    {
        var matches = new List<int>();
        var arrayCarrier = (int)NativeFixedArrayTypes.Type(0);
        for (var offset = sizeof(int) * 2;
            offset <= artifact.Length - recordLength;
            offset++)
        {
            if (artifact[offset] != tag
                || BinaryPrimitives.ReadInt32LittleEndian(
                    artifact.AsSpan(offset + 1, sizeof(int)))
                    != (int)expressionType)
            {
                continue;
            }

            var matchesShape = tag switch {
                43 => recordLength == 17 + (elementCount * 21)
                    && BinaryPrimitives.ReadInt32LittleEndian(
                        artifact.AsSpan(offset + 13, sizeof(int))) == elementCount
                    && Enumerable.Range(0, elementCount).All(index =>
                        IsConstant(offset + 17 + (index * 21), elementType)),
                44 => recordLength == 42
                    && BinaryPrimitives.ReadInt32LittleEndian(
                        artifact.AsSpan(offset + 13, sizeof(int))) == 0
                    && BinaryPrimitives.ReadInt32LittleEndian(
                        artifact.AsSpan(offset + 17, sizeof(int))) == arrayCarrier
                    && IsConstant(offset + 21, PrimitiveType.Int32),
                45 => recordLength == 42
                    && BinaryPrimitives.ReadInt32LittleEndian(
                        artifact.AsSpan(offset + 13, sizeof(int))) == 0
                    && BinaryPrimitives.ReadInt32LittleEndian(
                        artifact.AsSpan(offset + 17, sizeof(int))) == arrayCarrier
                    && IsConstant(offset + 21, PrimitiveType.Int32),
                46 => recordLength == 63
                    && BinaryPrimitives.ReadInt32LittleEndian(
                        artifact.AsSpan(offset + 13, sizeof(int))) == 0
                    && BinaryPrimitives.ReadInt32LittleEndian(
                        artifact.AsSpan(offset + 17, sizeof(int))) == arrayCarrier
                    && IsConstant(offset + 21, PrimitiveType.Int32)
                    && IsConstant(offset + 42, elementType),
                _ => false,
            };
            if (matchesShape)
            {
                matches.Add(offset);
            }
        }

        var candidates = Enumerable.Range(0,
                Math.Max(0, artifact.Length - sizeof(int)))
            .Where(offset => artifact[offset] == tag
                && BinaryPrimitives.ReadInt32LittleEndian(
                    artifact.AsSpan(offset + 1, sizeof(int)))
                    == (int)expressionType)
            .Take(10);
        Assert.That(matches, Has.Count.EqualTo(1),
            $"expected one revision-{Revision100} tag-{tag} record; "
            + $"candidate offsets: {string.Join(",", candidates)}");
        return matches.Single();

        bool IsConstant(int offset, PrimitiveType type) =>
            artifact[offset] == 1
            && BinaryPrimitives.ReadInt32LittleEndian(
                artifact.AsSpan(offset + 1, sizeof(int))) == (int)type;
    }

    private static BoundNativeFixedArrayType ReadSingleArrayRow(
        byte[] artifact)
    {
        using var stream = new MemoryStream(artifact, writable: false);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadInt32();
        _ = reader.ReadInt32();
        SkipModuleString(reader);
        _ = reader.ReadBoolean();
        _ = reader.ReadByte();
        _ = reader.ReadBoolean();
        SkipModuleString(reader);
        Assert.That(reader.ReadInt32(), Is.Zero);
        _ = reader.ReadInt32();
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.EqualTo(1));
        return new BoundNativeFixedArrayType((PrimitiveType)reader.ReadInt32(),
            reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(),
            reader.ReadUInt64(), reader.ReadUInt64());
    }

    private static async Task<int> ExecuteNativeImageAsync(byte[] image)
    {
        Assert.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"private-array-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException(
                "Failed to start the emitted fixed-array program.");
            try
            {
                await process.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
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

    private static int FindReservation(byte[] artifact)
    {
        var candidates = new List<int>();
        for (var offset = sizeof(int) * 2; offset <= artifact.Length - 21; offset++)
        {
            if (artifact[offset] == 41
                && BinaryPrimitives.ReadInt32LittleEndian(artifact.AsSpan(offset + 1)) == (int)PrimitiveType.Void
                && BinaryPrimitives.ReadInt32LittleEndian(artifact.AsSpan(offset + 13)) >= 0
                && BinaryPrimitives.ReadInt32LittleEndian(artifact.AsSpan(offset + 17)) >= 100)
            {
                candidates.Add(offset);
            }
        }
        Assert.That(candidates, Has.Count.EqualTo(1), "expected one 21-byte tag-41 reservation");
        return candidates.Single();
    }
}
