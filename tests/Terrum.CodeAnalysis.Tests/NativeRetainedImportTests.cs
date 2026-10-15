// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
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

public sealed class NativeRetainedImportTests
{
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void RetainedCleanupDeclarationDoesNotCreateAnUnusedMachineImport(NativeRuntimePolicy policy)
    {
        var leaf = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)] public unique struct Token {
                public int32 Value;
                public Token(int32 value) { Value = value; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            public Token Make() { return new Token(42); }
            """), (BoundRuntimePolicy)policy);
        Assert.That(leaf.HasErrors, Is.False, string.Join(Environment.NewLine, leaf.Diagnostics));
        var reference = ModuleCompiler.CreateReference(ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = leaf.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(), ["Make", "Token.Close"]));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public Token Forward() { return Make(); }"), reference, (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        BoundFunction[] external = [.. program.Functions.Where(static function => function.IsExternal)];
        Assert.That(global::System.Linq.Enumerable.Any(external,static function => function.Name == "Token.Close"),Is.True);
        NativeImportBinding[] bindings = [.. external.Select(static function =>
            new NativeImportBinding(function.Name, "retained-leaf.dll", function.Name))];
        var image = NativeEmitter.EmitLibraryWithImports(program, ["Forward"], bindings, policy);
        Assert.That(ReadImports(image, "retained-leaf.dll"), Is.EqualTo(["Make"]));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibraryWithImports(program,
            ["Forward"], [new NativeImportBinding("Make", "retained-leaf.dll", "Make")], policy))!;
    }

    private static List<string> ReadImports(byte[] image, string library)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        var header = pe.PEHeaders.PEHeader ?? throw new InvalidOperationException("Missing PE header.");
        var descriptors = pe.GetSectionData(header.ImportTableDirectory.RelativeVirtualAddress).GetReader();
        while (true)
        {
            var lookup = descriptors.ReadInt32();
            _ = descriptors.ReadInt32();
            _ = descriptors.ReadInt32();
            var name = descriptors.ReadInt32();
            _ = descriptors.ReadInt32();
            if (lookup == 0)
            {
                return [];
            }
            if (!ReadName(pe, name).Equals(library, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var result = new List<string>();
            var entries = pe.GetSectionData(lookup).GetReader();
            for (var entry = entries.ReadUInt64(); entry != 0; entry = entries.ReadUInt64())
            {
                Assert.That(entry & 0x8000_0000_0000_0000UL, Is.EqualTo(0UL));
                result.Add(ReadName(pe, checked((int)entry + 2)));
            }
            return result;
        }
    }

    private static string ReadName(PEReader pe, int rva)
    {
        var reader = pe.GetSectionData(rva).GetReader();
        var text = new StringBuilder();
        for (var value = reader.ReadByte(); value != 0; value = reader.ReadByte())
        {
            _ = text.Append((char)value);
        }
        return text.ToString();
    }
}
