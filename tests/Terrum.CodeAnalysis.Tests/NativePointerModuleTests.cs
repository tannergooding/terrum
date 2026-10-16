// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativePointerModuleTests
{
    [Test]
    public void TypedPointerIdentitiesMergeAcrossPrivateModuleReferences()
    {
        var firstCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Payload { int32 Value; }

            public unsafe ptr<Payload> First(ptr<Payload> value) { return value; }
            """));
        var secondCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Decoy { int32 Value; }

            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Other { int32 Value; }

            public unsafe ptr<Other> Second(ptr<Other> value) { return value; }
            public unsafe ptr<Decoy> DecoyPointer(ptr<Decoy> value) { return value; }
            """));

        Assert.That(firstCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, firstCompilation.Diagnostics));
        Assert.That(secondCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, secondCompilation.Diagnostics));
        var firstProducer = ModuleCompiler.CreateProducer(firstCompilation.Program!, ["First"]);
        var secondProducer = ModuleCompiler.CreateProducer(secondCompilation.Program!,
            ["Second", "DecoyPointer"]);
        var reference = ModuleCompiler.CreateReferenceSet([firstProducer, secondProducer]);

        var firstPointer = reference.Functions.Single(static function => function.Name == "First").Variables[0].Type;
        var secondPointer = reference.Functions.Single(static function => function.Name == "Second").Variables[0].Type;
        Assert.That(secondPointer, Is.Not.EqualTo(firstPointer));

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    First(null);
                    Second(null);
                    DecoyPointer(null);
                }
                return 0;
            }
            """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
    }

    [Test]
    public void PrivateModuleReaderRejectsCyclicNativePointerTypes()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public unsafe int32 AcceptScalar(ptr<int32> value) { return 0; }
            public unsafe int32 AcceptNested(ptr<ptr<int32>> value) { return 0; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var producer = ModuleCompiler.CreateProducer(compilation.Program!,
            ["AcceptScalar", "AcceptNested"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var scalarPointerType = (int)reference.Functions.Single(
            static function => function.Name == "AcceptScalar").Variables[0].Type;
        var nestedPointerType = (int)reference.Functions.Single(
            static function => function.Name == "AcceptNested").Variables[0].Type;
        var nestedPointerIndex = nestedPointerType - Math.Min(scalarPointerType, nestedPointerType);
        var tableOffset = FindPointerTableOffset(producer);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(tableOffset)), Is.EqualTo(2));
        var malformed = producer.ToArray();
        var nestedPointeeOffset = tableOffset + sizeof(int)
            + nestedPointerIndex * (sizeof(int) + sizeof(byte));
        BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(nestedPointeeOffset), nestedPointerType);

        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed));
    }

    [Test]
    public void TypedPointerAccessRequiresUnsafeAndWritablePermission()
    {
        var unscopedRead = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                ptr<int32> pointer = null;
                return *pointer;
            }
            """));
        Assert.That(unscopedRead.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3028"), Is.True,
            string.Join(Environment.NewLine, unscopedRead.Diagnostics));

        var readonlyWrite = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    ptr<int32> pointer = null;
                    *pointer = 42;
                }
                return 0;
            }
            """));
        Assert.That(readonlyWrite.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, readonlyWrite.Diagnostics));
    }

    [Test]
    public void PrivateModulesRejectNativeFixedArrayPointerAddresses()
    {
        var compilation = Compilation.Create([SyntaxTree.Parse("""
            int32 Export() {
                InlineArray<uint32, 2> values = [4, 5];
                unsafe {
                    ptr<uint32> first = &values[0];
                    return 0;
                }
            }
            int32 Main() { return 0; }
            """)], BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var exception = Assert.Throws<NotSupportedException>(() =>
            ModuleCompiler.CreateProducer(compilation.Program!, ["Export"]));
        Assert.That(exception!.Message,
            Does.Contain("Source native imports are not supported by semantic modules."));
    }

    [Test]
    public async Task NativeGraphConsumerPreservesTypedPointerContractsAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public unsafe int32 Read(ptr<int32> value) {
                unsafe { return *value; }
            }
            """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(providerCompilation.Program!,
            ["Read"], "PointerProvider", NativeRuntimePolicy.FailFast);
        var reference = NativeModuleCompiler.CreateGraphReference([provider]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init mut int32 value;
                unsafe {
                    write ptr<int32> writable = &value;
                    *writable = 42;
                    ptr<int32> readable = &value;
                    return Read(readable);
                }
            }
            """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateGraphConsumer(
            consumerCompilation.Program!, [provider], NativeRuntimePolicy.FailFast);

        NativeModuleCompiler.ValidateGraph(consumer, [provider]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(consumer, [provider],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast).ConfigureAwait(true), Is.EqualTo(42));
    }

    private static int FindPointerTableOffset(byte[] module)
    {
        using var stream = new MemoryStream(module, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        _ = reader.ReadInt32();
        Assert.That(reader.ReadInt32(), Is.EqualTo(83));
        SkipString(reader);
        _ = reader.ReadBoolean();
        _ = reader.ReadByte();
        _ = reader.ReadBoolean();
        SkipString(reader);
        Assert.That(reader.ReadInt32(), Is.Zero, "The fixture has no invocation buffers.");
        _ = reader.ReadInt32();
        Assert.That(reader.ReadInt32(), Is.Zero, "The fixture has no structures.");
        Assert.That(reader.ReadInt32(), Is.Zero, "The fixture has no classes.");
        Assert.That(reader.ReadInt32(), Is.Zero, "The fixture has no imported structures.");
        Assert.That(reader.ReadInt32(), Is.Zero, "The fixture has no function-pointer signatures.");
        return checked((int)stream.Position);
    }

    private static void SkipString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (reader.ReadBytes(length).Length != length)
        {
            throw new EndOfStreamException();
        }
    }
}
