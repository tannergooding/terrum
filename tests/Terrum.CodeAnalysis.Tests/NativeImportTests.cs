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
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeImportTests
{
    private const string Provider = """
        public int32 A(int32 value) { return value + 1; }
        public int64 B(int64 n, int32 a, int32 b, int32 c, int32 d) {
            return n + (int64)a + (int64)b + (int64)c + (int64)d;
        }
        """;
    private const string Consumer = """
        int32 Main() { return A(10) + (int32)B(1, 2, 3, 4, 5); }
        """;

    private static BoundProgram Compile()
    {
        var producer = Compilation.CreateLibrary(SyntaxTree.Parse(Provider));
        Assert.False(producer.HasErrors, string.Join(Environment.NewLine, producer.Diagnostics));
        var artifact = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producer.Program), ["A", "B"]);
        var consumer = Compilation.Create(SyntaxTree.Parse(Consumer), ModuleCompiler.CreateReference(artifact));
        Assert.False(consumer.HasErrors, string.Join(Environment.NewLine, consumer.Diagnostics));
        return Assert.IsType<BoundProgram>(consumer.Program);
    }

    [Fact]
    public void ImportsHaveNamedRvaThunksWithoutExternalBodies()
    {
        var program = Compile();
        var bindings = new[] { new NativeImportBinding("B", "native-import-provider.dll", "B"),
            new NativeImportBinding("A", "native-import-provider.dll", "A") };
        var image = NativeEmitter.EmitWithImports(program, bindings, NativeRuntimePolicy.FailFast);
        Assert.Equal(image, NativeEmitter.EmitWithImports(program, bindings.Reverse().ToArray(), NativeRuntimePolicy.FailFast));
        using var stream = new MemoryStream(image);
        using var reader = new PEReader(stream);
        var headers = reader.PEHeaders;
        var header = Assert.IsType<PEHeader>(headers.PEHeader);
        Assert.Equal(0, (int)headers.CoffHeader.Characteristics & 0x2000);
        Assert.Equal(0, header.BaseRelocationTableDirectory.Size);
        Assert.Equal(0x60, (int)header.DllCharacteristics & 0x60);
        Assert.Equal(0, header.ExportTableDirectory.Size);
        Assert.Equal(60, header.ImportTableDirectory.Size);
        var idata = Assert.Single(headers.SectionHeaders, static section => section.Name == ".idata");
        var pdata = Assert.Single(headers.SectionHeaders, static section => section.Name == ".pdata");
        Assert.Equal(12 * (6 + program.Functions.Count(static function => !function.IsExternal)),
            header.ExceptionTableDirectory.Size);
        Assert.Equal(pdata.VirtualAddress, header.ExceptionTableDirectory.RelativeVirtualAddress);
        Assert.InRange((long)idata.PointerToRawData + idata.SizeOfRawData, 0, image.Length);
        Assert.InRange(header.ImportAddressTableDirectory.RelativeVirtualAddress, idata.VirtualAddress,
            idata.VirtualAddress + idata.VirtualSize - header.ImportAddressTableDirectory.Size);
        var import = header.ImportTableDirectory.RelativeVirtualAddress;
        Assert.Equal("KERNEL32.dll", Name(Read32(Offset(import) + 12)));
        Assert.Equal("native-import-provider.dll", Name(Read32(Offset(import) + 32)));
        Assert.Equal(0, Read32(Offset(import) + 40));
        Assert.Equal(0, Read32(Offset(import) + 56));
        Assert.Equal("ExitProcess", Assert.Single(ImportNames(import)));
        Assert.Collection(ImportNames(import + 20), item => Assert.Equal("A", item),
            item => Assert.Equal("B", item));

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
            Assert.True(end > offset);
            return Encoding.ASCII.GetString(image, offset, end - offset);
        }
        string[] ImportNames(int descriptorRva)
        {
            var lookupRva = Read32(Offset(descriptorRva));
            var addressRva = Read32(Offset(descriptorRva) + 16);
            Assert.Equal(0, lookupRva % 8);
            Assert.Equal(0, addressRva % 8);
            var names = new System.Collections.Generic.List<string>();
            for (var index = 0; ; index++)
            {
                var hintRva = (int)BinaryPrimitives.ReadInt64LittleEndian(image.AsSpan(Offset(lookupRva) + (index * 8), 8));
                var addressHint = (int)BinaryPrimitives.ReadInt64LittleEndian(image.AsSpan(Offset(addressRva) + (index * 8), 8));
                Assert.Equal(hintRva, addressHint);
                if (hintRva == 0)
                {
                    break;
                }
                Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(Offset(hintRva), 2)));
                names.Add(Name(hintRva + 2));
            }
            return [.. names];
        }
    }

    [Fact]
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
            [a, new NativeImportBinding("B", "provider.dll", "A")],
        })
        {
            _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithImports(program, bindings,
                NativeRuntimePolicy.FailFast));
        }
        var standalone = Compilation.Create(SyntaxTree.Parse("int32 Main() { return 42; }"));
        Assert.False(standalone.HasErrors);
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithImports(
            Assert.IsType<BoundProgram>(standalone.Program), [], NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitWithImports(program, [a, b],
            (NativeRuntimePolicy)123));
        var external = Array.FindIndex(program.Functions.ToArray(), static function => function.IsExternal);
        var invalid = program with {
            Functions = program.Functions.SetItem(external, program.Functions[external] with {
                Body = new BoundBlock([new BoundRequire(new BoundConstant(1, PrimitiveType.Bool, default), default)],
                    default),
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithImports(invalid, [a, b],
            NativeRuntimePolicy.FailFast));
    }
}
