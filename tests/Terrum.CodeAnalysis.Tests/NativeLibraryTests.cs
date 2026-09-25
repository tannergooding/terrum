// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeLibraryTests
{
    private const string Source = """
        public struct Twelve {
            public int32 First; public int32 Second; public int32 Third;
            public Twelve(int32 first, int32 second, int32 third) {
                First = first; Second = second; Third = third;
            }
        }
        public struct Wide {
            public int64 Signed; public uint64 Unsigned;
            public Wide(int64 signed, uint64 unsigned) { Signed = signed; Unsigned = unsigned; }
        }
        public Twelve MakeTwelve(int32 first, int32 second, int32 third) {
            return new Twelve(first, second, third);
        }
        public Wide MakeWide(int64 signed, uint64 unsigned) {
            return new Wide(signed, unsigned);
        }
        int32 Hidden() { return 5; }
        public int32 Evaluate(Twelve value, Wide wide, int64 n, int32 d, int32 e) {
            require wide.Signed == int64.MinValue && wide.Unsigned == uint64.MaxValue;
            return value.First + value.Second + value.Third + (int32)(n / 2147483648)
                + d + e + Hidden();
        }
        public bool TryWide(bool success, out Wide result) {
            if (!success) { return false; }
            result = MakeWide(int64.MinValue, uint64.MaxValue);
            return true;
        }
        """;

    internal static BoundProgram Compile(string source = Source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    internal static string[] Exports => ["TryWide", "Evaluate", "MakeWide", "MakeTwelve"];

    [Fact]
    public void LibraryHasDeterministicExportAndUnwindTables()
    {
        var program = Compile();
        var image = NativeEmitter.EmitLibrary(program, Exports, NativeRuntimePolicy.FailFast);
        Assert.Equal(image, NativeEmitter.EmitLibrary(program, Exports.Reverse().ToArray(), NativeRuntimePolicy.FailFast));
        Assert.NotEqual(image, NativeEmitter.EmitLibrary(Compile(Source.Replace("Hidden() { return 5;", "Hidden() { return 6;",
            StringComparison.Ordinal)), Exports, NativeRuntimePolicy.FailFast));
        using var stream = new MemoryStream(image);
        using var reader = new PEReader(stream);
        var headers = reader.PEHeaders;
        var header = Assert.IsType<PEHeader>(headers.PEHeader);
        Assert.Equal(0, header.AddressOfEntryPoint);
        Assert.NotEqual(0, (int)headers.CoffHeader.Characteristics & 0x2000);
        Assert.Equal(0, (int)headers.CoffHeader.Characteristics & 1);
        Assert.Equal(0x60, (int)header.DllCharacteristics & 0x60);
        Assert.Equal(0, header.BaseRelocationTableDirectory.Size);
        Assert.Equal(0, header.CorHeaderTableDirectory.Size);
        Assert.NotEqual(0, header.ExceptionTableDirectory.Size);
        Assert.NotEqual(0, header.ImportTableDirectory.Size);
        Assert.NotEqual(0, header.ImportAddressTableDirectory.Size);
        var export = header.ExportTableDirectory;
        var section = Assert.Single(headers.SectionHeaders, static candidate => candidate.Name == ".edata");
        Assert.Equal(section.VirtualAddress, export.RelativeVirtualAddress);
        Assert.Equal(section.VirtualSize, export.Size);
        Assert.InRange((long)section.PointerToRawData + section.SizeOfRawData, 0, image.Length);
        Assert.Equal(0, section.VirtualAddress % header.SectionAlignment);
        Assert.Equal(0, section.PointerToRawData % header.FileAlignment);
        var at = section.PointerToRawData;
        Assert.Equal(1, Read32(at + 16));
        Assert.Equal(4, Read32(at + 20));
        Assert.Equal(4, Read32(at + 24));
        Assert.Equal("terrum.dll", Name(Read32(at + 12)));
        var names = Read32(at + 32);
        var addresses = Read32(at + 28);
        var ordinals = Read32(at + 36);
        var expected = Exports.Order(StringComparer.Ordinal).ToArray();
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], Name(Read32(Offset(names) + (4 * i))));
            Assert.Equal(i, BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(Offset(ordinals) + (2 * i))));
            var codeRva = Read32(Offset(addresses) + (4 * i));
            var text = Assert.Single(headers.SectionHeaders, static candidate => candidate.Name == ".text");
            Assert.InRange(codeRva, text.VirtualAddress, text.VirtualAddress + text.VirtualSize - 1);
            Assert.False(codeRva >= export.RelativeVirtualAddress && codeRva < export.RelativeVirtualAddress + export.Size);
        }

        int Read32(int offset)
        {
            return BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(offset, 4));
        }
        int Offset(int rva)
        {
            return checked(at + rva - section.VirtualAddress);
        }
        string Name(int rva)
        {
            var offset = Offset(rva);
            var end = Array.IndexOf(image, (byte)0, offset, at + section.VirtualSize - offset);
            Assert.True(end > offset);
            return System.Text.Encoding.ASCII.GetString(image, offset, end - offset);
        }
    }

    [Fact]
    public void RejectsInvalidExportsAndMalformedLibrariesBeforeCodegen()
    {
        var program = Compile();
        foreach (var names in new[] { Array.Empty<string>(), ["Hidden"], ["Unknown"],
            ["Evaluate", "Evaluate"], ["Evaluate\0bad"], ["Evaluate\u0080"] })
        {
            _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(program, names, NativeRuntimePolicy.FailFast));
        }
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(program with { EntryPoint = 0 },
            Exports, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(program with {
            Functions = program.Functions.SetItem(0, program.Functions[0] with { IsExternal = true }),
        }, Exports, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibrary(program with {
            Buffers = [new HostBuffer("Input", PrimitiveType.Int32, [1], IsWritable: false)],
        }, Exports, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibrary(program,
            Exports, (NativeRuntimePolicy)42));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void RejectsNonPublicValueSignatures()
    {
        var privateType = Compile("struct Private { public int32 Value; } public int32 Read(Private value) { return value.Value; }");
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(privateType, ["Read"], NativeRuntimePolicy.FailFast));
        var privateField = Compile("public struct Hidden { int32 Value; public Hidden(int32 n) { Value = n; } }"
            + " public Hidden Make() { return new Hidden(42); }");
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(privateField, ["Make"], NativeRuntimePolicy.FailFast));
    }
}
