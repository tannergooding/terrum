// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeLibraryTests
{
    private const string Source = """
        [Blittable] [Layout(LayoutKind.Sequential)] public struct Twelve {
            public int32 First; public int32 Second; public int32 Third;
            public Twelve(int32 first, int32 second, int32 third) {
                First = first; Second = second; Third = third;
            }
        }
        [Blittable] [Layout(LayoutKind.Sequential)] public struct Wide {
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
            return value.First + value.Second + value.Third + int32(n / 2147483648)
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
    }

    internal static string[] Exports => ["TryWide", "Evaluate", "MakeWide", "MakeTwelve"];

    [Test]
    public void LibraryHasDeterministicExportAndUnwindTables()
    {
        var program = Compile();
        var image = NativeEmitter.EmitLibrary(program, Exports, NativeRuntimePolicy.FailFast);
        Assert.That(NativeEmitter.EmitLibrary(program, Exports.Reverse().ToArray(), NativeRuntimePolicy.FailFast), Is.EqualTo(image));
        Assert.That(NativeEmitter.EmitLibrary(Compile(Source.Replace("Hidden() { return 5;", "Hidden() { return 6;",
            StringComparison.Ordinal)), Exports, NativeRuntimePolicy.FailFast), Is.Not.EqualTo(image));
        using var stream = new MemoryStream(image);
        using var reader = new PEReader(stream);
        var headers = reader.PEHeaders;
        var header = ((global::System.Func<PEHeader>)(() => { var __nunitActual1 = headers.PEHeader; Assert.That(__nunitActual1, Is.TypeOf<PEHeader>()); return (PEHeader)__nunitActual1!; }))();
        Assert.That(header.AddressOfEntryPoint, Is.EqualTo(0));
        Assert.That((int)headers.CoffHeader.Characteristics & 0x2000, Is.Not.EqualTo(0));
        Assert.That((int)headers.CoffHeader.Characteristics & 1, Is.EqualTo(0));
        Assert.That((int)header.DllCharacteristics & 0x60, Is.EqualTo(0x60));
        Assert.That(header.BaseRelocationTableDirectory.Size, Is.EqualTo(0));
        Assert.That(header.CorHeaderTableDirectory.Size, Is.EqualTo(0));
        Assert.That(header.ExceptionTableDirectory.Size, Is.Not.EqualTo(0));
        Assert.That(header.ImportTableDirectory.Size, Is.Not.EqualTo(0));
        Assert.That(header.ImportAddressTableDirectory.Size, Is.Not.EqualTo(0));
        var export = header.ExportTableDirectory;
        var section = global::System.Linq.Enumerable.Single(headers.SectionHeaders, static candidate => candidate.Name == ".edata");
        Assert.That(export.RelativeVirtualAddress, Is.EqualTo(section.VirtualAddress));
        Assert.That(export.Size, Is.EqualTo(section.VirtualSize));
        Assert.That((long)section.PointerToRawData + section.SizeOfRawData, Is.InRange(0, image.Length));
        Assert.That(section.VirtualAddress % header.SectionAlignment, Is.EqualTo(0));
        Assert.That(section.PointerToRawData % header.FileAlignment, Is.EqualTo(0));
        var at = section.PointerToRawData;
        Assert.That(Read32(at + 16), Is.EqualTo(1));
        Assert.That(Read32(at + 20), Is.EqualTo(4));
        Assert.That(Read32(at + 24), Is.EqualTo(4));
        Assert.That(Name(Read32(at + 12)), Is.EqualTo("terrum.dll"));
        var names = Read32(at + 32);
        var addresses = Read32(at + 28);
        var ordinals = Read32(at + 36);
        var expected = Exports.Order(StringComparer.Ordinal).ToArray();
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.That(Name(Read32(Offset(names) + (4 * i))), Is.EqualTo(expected[i]));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(Offset(ordinals) + (2 * i))), Is.EqualTo(i));
            var codeRva = Read32(Offset(addresses) + (4 * i));
            var text = global::System.Linq.Enumerable.Single(headers.SectionHeaders, static candidate => candidate.Name == ".text");
            Assert.That(codeRva, Is.InRange(text.VirtualAddress, text.VirtualAddress + text.VirtualSize - 1));
            Assert.That(codeRva >= export.RelativeVirtualAddress && codeRva < export.RelativeVirtualAddress + export.Size, Is.False);
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
            Assert.That(end > offset, Is.True);
            return System.Text.Encoding.ASCII.GetString(image, offset, end - offset);
        }
    }

    [Test]
    public void RejectsInvalidExportsAndMalformedLibrariesBeforeCodegen()
    {
        var program = Compile();
        foreach (var names in new[] { Array.Empty<string>(), ["Hidden"], ["Unknown"],
            ["Evaluate", "Evaluate"], ["Evaluate\0bad"], ["Evaluate\u0080"] })
        {
            _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(program, names, NativeRuntimePolicy.FailFast))!;
        }
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(program with { EntryPoint = 0 },
            Exports, NativeRuntimePolicy.FailFast))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(program with {
            Functions = program.Functions.SetItem(0, program.Functions[0] with { IsExternal = true }),
        }, Exports, NativeRuntimePolicy.FailFast))!;
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibrary(program with {
            Buffers = [new HostBuffer("Input", PrimitiveType.Int32, [1], IsWritable: false)],
        }, Exports, NativeRuntimePolicy.FailFast))!;
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibrary(program,
            Exports, (NativeRuntimePolicy)42))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast))!;
    }

    [Test]
    public void NativeCallableEntryNamesAreIndependentNamedExports()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [NoThrow]
            [Terrum.Interop.NativeCallable]
            public int32 Callback(int32 value) { return value; }
            public int32 Ordinary() { return 0; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var callback = program.Functions.Single(static function => function.Name == "Callback");
        var callbackIndex = program.Functions.IndexOf(callback);
        program = program with
        {
            Functions = program.Functions.SetItem(callbackIndex,
                callback with { NativeCallableEntryName = "Callback|Native" }),
        };

        var image = NativeEmitter.EmitLibrary(program, ["Ordinary"], NativeRuntimePolicy.FailFast);
        using var stream = new MemoryStream(image);
        using var reader = new PEReader(stream);
        var headers = reader.PEHeaders;
        var section = headers.SectionHeaders.Single(static candidate => candidate.Name == ".edata");
        var exportOffset = section.PointerToRawData;
        var nameTable = Read32(exportOffset + 32);
        var names = Enumerable.Range(0, Read32(exportOffset + 24))
            .Select(index => ReadName(Read32(Offset(nameTable) + (4 * index))))
            .ToArray();

        Assert.That(names, Is.EqualTo(new[] { "Callback|Native", "Ordinary" }));

        var collision = program with
        {
            Functions = program.Functions.SetItem(callbackIndex,
                callback with { NativeCallableEntryName = "Ordinary" }),
        };
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitLibrary(collision, ["Ordinary"], NativeRuntimePolicy.FailFast))!;

        int Read32(int offset) => BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(offset, 4));

        int Offset(int rva) => checked(exportOffset + rva - section.VirtualAddress);

        string ReadName(int rva)
        {
            var offset = Offset(rva);
            var end = Array.IndexOf(image, (byte)0, offset,
                section.PointerToRawData + section.SizeOfRawData - offset);
            Assert.That(end, Is.GreaterThan(offset));
            return System.Text.Encoding.ASCII.GetString(image, offset, end - offset);
        }
    }

    [Test]
    public void RejectsNonPublicValueSignatures()
    {
        var privateType = Compile("struct Private { public int32 Value; } public int32 Read(Private value) { return value.Value; }");
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(privateType, ["Read"], NativeRuntimePolicy.FailFast))!;
        var privateField = Compile("[Blittable] [Layout(LayoutKind.Sequential)] public struct Hidden { int32 Value; public Hidden(int32 n) { Value = n; } }"
            + " public Hidden Make() { return new Hidden(42); }");
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(privateField, ["Make"], NativeRuntimePolicy.FailFast))!;
    }

    [Test]
    public void ValidatesRepeatedNestedValueTypesInLibrarySignatures()
    {
        var publicValues = Compile("""
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Leaf { public int32 Value; }
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Pair { public Leaf First; public Leaf Second; }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Envelope { public Pair Left; public Pair Right; }
            public Envelope Echo(Envelope value) { return value; }
            """);
        Assert.That(NativeEmitter.EmitLibrary(publicValues, ["Echo"], NativeRuntimePolicy.FailFast), Is.Not.Empty);

        var privateValues = Compile("""
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Leaf { int32 Value; }
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Pair { public Leaf First; public Leaf Second; }
            public Pair Echo(Pair value) { return value; }
            """);
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(privateValues, ["Echo"],
            NativeRuntimePolicy.FailFast))!;
    }

    [Test]
    public void AllowsAbsentPrimitiveDeclaringStructOwner()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[Blittable] [Layout(LayoutKind.Sequential)] public struct Cell { public int32 Value; public int32 Read() { return 42; } }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        var function = global::System.Linq.Enumerable.Single(program.Functions);
        var primitiveOwner = program with {
            Functions = program.Functions.SetItem(0, function with {
                Name = "int32.Read",
                DeclaringStruct = "int32",
                Variables = function.Variables.SetItem(0, function.Variables[0] with { Type = PrimitiveType.Int32 }),
            }),
        };

        _ = NativeEmitter.EmitLibrary(primitiveOwner, ["int32.Read"], NativeRuntimePolicy.FailFast);
    }

    [Test]
    public void AllowsPublicMatchingDeclaringStructOwner()
    {
        var program = Compile();
        var function = global::System.Linq.Enumerable.Single(program.Functions, static function => function.DeclaringStruct == "Twelve");

        _ = NativeEmitter.EmitLibrary(program, [function.Name], NativeRuntimePolicy.FailFast);
    }
    [TestCase("struct Private","Private")]
    [TestCase("internal struct Internal","Internal")]
    public void RejectsNonPublicMatchingDeclaringStructOwner(string declaration, string owner)
    {
        var program = Compile($"{declaration} {{ public int32 Value; public {owner}(int32 value) {{ Value = value; }} }}");
        var function = global::System.Linq.Enumerable.Single(program.Functions, function => function.DeclaringStruct == owner);

        var exception = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(program, [function.Name], NativeRuntimePolicy.FailFast))!;
        Assert.That(exception.Message, Does.Contain($"'{function.Name}' is not an accessible native library export."));
    }
}
