// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeImportTests
{
    private const string Provider = """
        public int32 A(int32 value) { return value + 1; }
        public int64 B(int64 n, int32 a, int32 b, int32 c, int32 d) {
            return n + int64(a) + int64(b) + int64(c) + int64(d);
        }
        """;
    private const string Consumer = """
        int32 Main() { return A(10) + int32(B(1, 2, 3, 4, 5)); }
        """;

    private static BoundProgram Compile()
    {
        var producer = Compilation.CreateLibrary(SyntaxTree.Parse(Provider));
        Assert.That(producer.HasErrors, Is.False, string.Join(Environment.NewLine, producer.Diagnostics));
        var artifact = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = producer.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(), ["A", "B"]);
        var consumer = Compilation.Create(SyntaxTree.Parse(Consumer), ModuleCompiler.CreateReference(artifact));
        Assert.That(consumer.HasErrors, Is.False, string.Join(Environment.NewLine, consumer.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = consumer.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
    }

    [Test]
    public void ImportsHaveNamedRvaThunksWithoutExternalBodies()
    {
        var program = Compile();
        var bindings = new[] { new NativeImportBinding("B", "NATIVE-IMPORT-PROVIDER.DLL", "B"),
            new NativeImportBinding("A", "native-import-provider.dll", "A") };
        var image = NativeEmitter.EmitWithImports(program, bindings, NativeRuntimePolicy.FailFast);
        Assert.That(NativeEmitter.EmitWithImports(program, bindings.Reverse().ToArray(), NativeRuntimePolicy.FailFast), Is.EqualTo(image));
        using var stream = new MemoryStream(image);
        using var reader = new PEReader(stream);
        var headers = reader.PEHeaders;
        var header = ((global::System.Func<PEHeader>)(() => { var __nunitActual2 = headers.PEHeader; Assert.That(__nunitActual2, Is.TypeOf<PEHeader>()); return (PEHeader)__nunitActual2!; }))();
        Assert.That((int)headers.CoffHeader.Characteristics & 0x2000, Is.EqualTo(0));
        Assert.That(header.BaseRelocationTableDirectory.Size, Is.EqualTo(0));
        Assert.That((int)header.DllCharacteristics & 0x60, Is.EqualTo(0x60));
        Assert.That(header.ExportTableDirectory.Size, Is.EqualTo(0));
        Assert.That(header.ImportTableDirectory.Size, Is.EqualTo(60));
        var idata = global::System.Linq.Enumerable.Single(headers.SectionHeaders, static section => section.Name == ".idata");
        var pdata = global::System.Linq.Enumerable.Single(headers.SectionHeaders, static section => section.Name == ".pdata");
        Assert.That(header.ExceptionTableDirectory.Size, Is.EqualTo(12 * (6 + program.Functions.Count(static function => !function.IsExternal))));
        Assert.That(header.ExceptionTableDirectory.RelativeVirtualAddress, Is.EqualTo(pdata.VirtualAddress));
        Assert.That((long)idata.PointerToRawData + idata.SizeOfRawData, Is.InRange(0, image.Length));
        Assert.That(header.ImportAddressTableDirectory.RelativeVirtualAddress, Is.InRange(idata.VirtualAddress, idata.VirtualAddress + idata.VirtualSize - header.ImportAddressTableDirectory.Size));
        var import = header.ImportTableDirectory.RelativeVirtualAddress;
        Assert.That(Name(Read32(Offset(import) + 12)), Is.EqualTo("KERNEL32.dll"));
        Assert.That(Name(Read32(Offset(import) + 32)), Is.EqualTo("NATIVE-IMPORT-PROVIDER.DLL"));
        Assert.That(Read32(Offset(import) + 40), Is.EqualTo(0));
        Assert.That(Read32(Offset(import) + 56), Is.EqualTo(0));
        Assert.That(global::System.Linq.Enumerable.Single(ImportNames(import)), Is.EqualTo("ExitProcess"));
        { var __nunitItems3 = global::System.Linq.Enumerable.ToArray(ImportNames(import + 20)); Assert.That(__nunitItems3, Has.Length.EqualTo(2)); Assert.That(__nunitItems3[0], Is.EqualTo("A")); Assert.That(__nunitItems3[1], Is.EqualTo("B")); }

        int Offset(int rva)
        {
            return checked(idata.PointerToRawData + rva - idata.VirtualAddress);
        }
        int Read32(int offset)
        {
            return BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(offset, 4));
        }
        string Name(int rva)
        {
            var offset = Offset(rva);
            var end = Array.IndexOf(image, (byte)0, offset, idata.PointerToRawData + idata.VirtualSize - offset);
            Assert.That(end > offset, Is.True);
            return Encoding.ASCII.GetString(image, offset, end - offset);
        }
        string[] ImportNames(int descriptorRva)
        {
            var lookupRva = Read32(Offset(descriptorRva));
            var addressRva = Read32(Offset(descriptorRva) + 16);
            Assert.That(lookupRva % 8, Is.EqualTo(0));
            Assert.That(addressRva % 8, Is.EqualTo(0));
            var names = new System.Collections.Generic.List<string>();
            for (var index = 0; ; index++)
            {
                var hintRva = (int)BinaryPrimitives.ReadInt64LittleEndian(image.AsSpan(Offset(lookupRva) + (index * 8), 8));
                var addressHint = (int)BinaryPrimitives.ReadInt64LittleEndian(image.AsSpan(Offset(addressRva) + (index * 8), 8));
                Assert.That(addressHint, Is.EqualTo(hintRva));
                if (hintRva == 0)
                {
                    break;
                }
                Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(Offset(hintRva), 2)), Is.EqualTo(0));
                names.Add(Name(hintRva + 2));
            }
            return [.. names];
        }
    }

    [Test]
    public void MultipleDeclarationsCanShareOneNativeImportTarget()
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 First(int32 value) { return value + 1; }
            public int32 Second(int32 value) { return value + 2; }
            """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(providerCompilation.Program!, ["First", "Second"]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() { return First(10) + Second(20); }
            """), ModuleCompiler.CreateReference(producer));
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var program = consumerCompilation.Program!;
        NativeImportBinding[] bindings = [
            new("First", "shared-provider.dll", "Shared"),
            new("Second", "shared-provider.dll", "Shared"),
        ];

        var image = NativeEmitter.EmitWithImports(program, bindings, NativeRuntimePolicy.FailFast);
        using var stream = new MemoryStream(image);
        using var reader = new PEReader(stream);
        var header = reader.PEHeaders.PEHeader!;
        Assert.That(header.ImportAddressTableDirectory.Size, Is.EqualTo(32));
    }

    [Test]
    public void RejectsIncompleteAmbiguousAndUnsafeBindings()
    {
        var program = Compile();
        var a = new NativeImportBinding("A", "provider.dll", "A");
        var b = new NativeImportBinding("B", "provider.dll", "B");
        foreach (var bindings in new[] {
            Array.Empty<NativeImportBinding>(), [a], [a, a], [a, b, b],
            [a, new NativeImportBinding("Other", "provider.dll", "B")],
            [a, new NativeImportBinding("Main", "provider.dll", "B")],
            [a, new NativeImportBinding("B", "..\\provider.dll", "B")],
            [a, new NativeImportBinding("B", "provider.dll", "B\0bad")],
        })
        {
            _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithImports(program, bindings,
                NativeRuntimePolicy.FailFast))!;
        }
        var standalone = Compilation.Create(SyntaxTree.Parse("int32 Main() { return 42; }"));
        Assert.That(standalone.HasErrors, Is.False);
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithImports(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = standalone.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))(), [], NativeRuntimePolicy.FailFast))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast))!;
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitWithImports(program, [a, b],
            (NativeRuntimePolicy)123))!;
        var external = Array.FindIndex(program.Functions.ToArray(), static function => function.IsExternal);
        var invalid = program with {
            Functions = program.Functions.SetItem(external, program.Functions[external] with {
                Body = new BoundBlock([new BoundRequire(new BoundConstant(1, PrimitiveType.Bool, default), default)],
                    default),
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithImports(invalid, [a, b],
            NativeRuntimePolicy.FailFast))!;
    }
}
