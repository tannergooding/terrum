// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Text;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Pe;

public sealed class PeImageImportGroupingTests
{
    [Test]
    public void ImportGroupsAndSymbolsAreDeterministicAcrossFixupOrder()
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 A() { return 1; } "
            + "public int32 B() { return 2; } "
            + "public int32 C() { return 3; }"));
        Assert.That(providerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = providerCompilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(), ["A", "B", "C"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var bindings = new[]
        {
            new NativeImportBinding("A", "zeta.dll", "Zulu"),
            new NativeImportBinding("B", "Alpha.dll", "Beta"),
            new NativeImportBinding("C", "ALPHA.DLL", "Alpha"),
        };

        var first = CompileConsumer(reference,
            "int32 Main() { return A() + B() + C() + C(); }");
        var second = CompileConsumer(reference,
            "int32 Main() { return C() + C() + B() + A(); }");
        AssertImportLayout(NativeEmitter.EmitWithImports(first, bindings, NativeRuntimePolicy.FailFast));
        AssertImportLayout(NativeEmitter.EmitWithImports(second, bindings, NativeRuntimePolicy.FailFast));
    }

    private static BoundProgram CompileConsumer(CompilationReference reference, string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), reference);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
    }

    private static void AssertImportLayout(byte[] image)
    {
        var imports = ReadImports(image);
        Assert.That(Array.ConvertAll(imports, static item => item.Name), Is.EqualTo(new[] { "KERNEL32.dll", "ALPHA.DLL", "zeta.dll" }));
        Assert.That(imports[1].Symbols, Is.EqualTo(new[] { "Alpha", "Beta" }));
        Assert.That(imports[2].Symbols, Is.EqualTo(new[] { "Zulu" }));
    }

    private static (string Name, string[] Symbols)[] ReadImports(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var reader = new PEReader(stream);
        var headers = reader.PEHeaders;
        var directory = headers.PEHeader?.ImportTableDirectory
            ?? throw new InvalidDataException("Missing PE optional header.");
        var imports = new List<(string, string[])>();
        for (var descriptorIndex = 0; descriptorIndex * 20 < directory.Size; descriptorIndex++)
        {
            var descriptor = Offset(headers.SectionHeaders,
                directory.RelativeVirtualAddress + (descriptorIndex * 20), image.Length);
            var lookup = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(descriptor));
            var nameRva = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(descriptor + 12));
            if (lookup == 0 && nameRva == 0)
            {
                break;
            }

            var name = ReadName(image, Offset(headers.SectionHeaders, nameRva, image.Length));
            var symbols = new List<string>();
            for (var symbolIndex = 0; ; symbolIndex++)
            {
                var hintRva = BinaryPrimitives.ReadInt64LittleEndian(image.AsSpan(
                    Offset(headers.SectionHeaders, lookup + (symbolIndex * 8), image.Length)));
                if (hintRva == 0)
                {
                    break;
                }
                symbols.Add(ReadName(image, Offset(headers.SectionHeaders, checked((int)hintRva) + 2, image.Length)));
            }
            imports.Add((name, symbols.ToArray()));
        }
        return [.. imports];
    }

    private static int Offset(ImmutableArray<SectionHeader> sections, int rva, int imageLength)
    {
        foreach (var section in sections)
        {
            var delta = (long)rva - section.VirtualAddress;
            if (delta >= 0 && delta < section.SizeOfRawData)
            {
                var offset = checked(section.PointerToRawData + (int)delta);
                if (offset < imageLength)
                {
                    return offset;
                }
            }
        }
        throw new InvalidDataException("PE RVA does not map to file data.");
    }

    private static string ReadName(byte[] image, int offset)
    {
        var end = Array.IndexOf(image, (byte)0, offset);
        if (end < 0)
        {
            throw new InvalidDataException("Unterminated PE name.");
        }
        return Encoding.ASCII.GetString(image, offset, end - offset);
    }
}
