// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Modules;

public sealed class NativeOutputModulePersistenceTests
{
    private const int RevisionOffset = sizeof(int);
    private const int Revision92 = 92;

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

    [Test]
    public void ProducerUsesExactRevision92AndRejectsOtherRevisions()
    {
        var artifact = CreateOutputProducer();
        Assert.That(ReadRevision(artifact), Is.EqualTo(Revision92));
        Assert.That(ModuleCompiler.CreateReference(artifact), Is.Not.Null);

        foreach (var revision in new[] { 89, 90, 91, 93 })
        {
            var forged = (byte[])artifact.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(RevisionOffset), revision);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forged),
                $"revision {revision} must be rejected before decoding");
        }
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

    private static byte[] CreateDependencyOutputDeclarationProducer(bool packetFirst)
    {
        var source = DependencyOutputTypes(packetFirst)
            + DependencyOutputAttributes
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
            public struct Packet { public uint32 Value; }
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
