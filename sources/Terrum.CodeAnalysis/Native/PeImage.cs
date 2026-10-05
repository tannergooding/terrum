// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

internal static class PeImage
{
    private const int SectionAlignment = 0x1000;
    private const int FileAlignment = 0x200;
    private const int HeaderSize = 0x400;
    private const int TextRva = 0x1000;
    private const ulong ImageBase = 0x140000000;

    internal static byte[] Build(NativeCode native) => BuildCore(native, null);

    internal static byte[] BuildLibrary(NativeCode native, IReadOnlyList<(string Name, int Index)> exports) =>
        BuildCore(native, exports);

    private static byte[] BuildCore(NativeCode native, IReadOnlyList<(string Name, int Index)>? exports)
    {
        var library = exports is not null;
        var code = native.Code;
        var entry = native.AotEntryPoint;
        var functions = native.Functions;
        var importOffsets = native.ExitProcessFixups;
        var readOnlyLength = native.ReadOnlyDataLength;
        var writableLength = native.Data.Length - readOnlyLength;
        var imports = BuildImports(native);
        var importTableSize = imports.Bytes.Length;
        var textRva = TextRva;
        var dataRva = Align(textRva + code.Length, SectionAlignment);
        imports.Relocate(dataRva);
        var readOnlyRva = readOnlyLength == 0 ? 0 : Align(dataRva + importTableSize, SectionAlignment);
        var writableRva = writableLength == 0 ? 0 : Align(
            readOnlyLength == 0 ? dataRva + importTableSize : readOnlyRva + readOnlyLength, SectionAlignment);
        var pdataSize = checked(NativeUnwind.RuntimeFunctionSize * functions.Length);
        var pdataRva = Align(writableLength != 0 ? writableRva + writableLength
            : readOnlyLength != 0 ? readOnlyRva + readOnlyLength : dataRva + importTableSize, SectionAlignment);
        var xdataSize = 0;
        foreach (var function in functions)
        {
            xdataSize = checked(xdataSize + NativeUnwind.InfoSize(function));
        }
        var xdataRva = Align(pdataRva + pdataSize, SectionAlignment);
        var exportSize = 0;
        if (exports is not null)
        {
            exportSize = checked(40 + (10 * exports.Count) + "terrum.dll".Length + 1);
            foreach (var (name, _) in exports)
            {
                exportSize = checked(exportSize + name.Length + 1);
            }
        }
        var exportRva = exportSize == 0 ? 0 : Align(xdataRva + xdataSize, SectionAlignment);
        var imageSize = Align(exportSize == 0 ? xdataRva + xdataSize : exportRva + exportSize, SectionAlignment);
        var textFile = HeaderSize;
        var dataFile = checked(textFile + Align(code.Length, FileAlignment));
        var readOnlyFile = checked(dataFile + Align(importTableSize, FileAlignment));
        var writableFile = checked(readOnlyFile + Align(readOnlyLength, FileAlignment));
        var pdataFile = checked(writableFile + Align(writableLength, FileAlignment));
        var xdataFile = checked(pdataFile + Align(pdataSize, FileAlignment));
        var exportFile = checked(xdataFile + Align(xdataSize, FileAlignment));
        var fileSize = checked(exportFile + Align(exportSize, FileAlignment));
        var image = new byte[fileSize];
        code.CopyTo(image, textFile);
        imports.Bytes.CopyTo(image, dataFile);
        native.Data.AsSpan(0, readOnlyLength).CopyTo(image.AsSpan(readOnlyFile));
        native.Data.AsSpan(readOnlyLength).CopyTo(image.AsSpan(writableFile));

        foreach (var offset in importOffsets)
        {
            Write32(image, textFile + offset, imports.Displacement("KERNEL32.dll", "ExitProcess",
                dataRva, textRva, offset));
        }
        foreach (var fixup in native.NativeImports)
        {
            Write32(image, textFile + fixup.DisplacementOffset, imports.Displacement(fixup.DllName,
                fixup.ExportName, dataRva, textRva, fixup.DisplacementOffset));
        }
        foreach (var fixup in native.DataFixups)
        {
            var targetRva = fixup.DataOffset < readOnlyLength
                ? readOnlyRva + fixup.DataOffset
                : writableLength != 0 ? writableRva + fixup.DataOffset - readOnlyLength : dataRva;
            Write32(image, textFile + fixup.DisplacementOffset,
                checked(targetRva - (textRva + fixup.DisplacementOffset + 4)));
        }
        var infoOffset = 0;
        for (var range = 0; range < functions.Length; range++)
        {
            var function = functions[range];
            NativeUnwind.WriteFunction(image.AsSpan(pdataFile + (range * NativeUnwind.RuntimeFunctionSize)),
                function, textRva, xdataRva + infoOffset);
            NativeUnwind.WriteInfo(image.AsSpan(xdataFile + infoOffset, NativeUnwind.InfoSize(function)), function, textRva);
            infoOffset += NativeUnwind.InfoSize(function);
        }
        if (exports is not null)
        {
            var count = exports.Count;
            var functionsAt = 40;
            var namesAt = checked(functionsAt + (4 * count));
            var ordinalsAt = checked(namesAt + (4 * count));
            var dllNameAt = checked(ordinalsAt + (2 * count));
            var dllName = AsciiBytes("terrum.dll\0");
            dllName.CopyTo(image, exportFile + dllNameAt);
            var stringAt = dllNameAt + dllName.Length;
            Write32(image, exportFile + 12, exportRva + dllNameAt);
            Write32(image, exportFile + 16, 1); // ordinal base
            Write32(image, exportFile + 20, count);
            Write32(image, exportFile + 24, count);
            Write32(image, exportFile + 28, exportRva + functionsAt);
            Write32(image, exportFile + 32, exportRva + namesAt);
            Write32(image, exportFile + 36, exportRva + ordinalsAt);
            for (var i = 0; i < count; i++)
            {
                var (name, index) = exports[i];
                var sourceOffset = index >= 0 && index < native.SourceFunctionOffsets.Length
                    ? native.SourceFunctionOffsets[index] : -1;
                if (sourceOffset < 0 || sourceOffset >= code.Length)
                {
                    throw new ArgumentException("Export does not name an emitted source function.", nameof(exports));
                }
                Write32(image, exportFile + functionsAt + (4 * i), textRva + sourceOffset);
                Write32(image, exportFile + namesAt + (4 * i), exportRva + stringAt);
                Write16(image, exportFile + ordinalsAt + (2 * i), i);
                var nameBytes = AsciiBytes(name + "\0");
                nameBytes.CopyTo(image, exportFile + stringAt);
                stringAt += nameBytes.Length;
            }
        }

        // Code and data references are RIP-relative; imports and unwind entries are RVAs.
        // No image-base-dependent locations need entries in the base relocation directory.
        Write16(image, 0, 0x5A4D);
        Write32(image, 0x3C, 0x80);
        var pe = 0x80;
        Write32(image, pe, 0x00004550);
        var coff = pe + 4;
        Write16(image, coff, (int)NativeTargetProfile.CoffMachine);
        Write16(image, coff + 2, 4 + (readOnlyLength == 0 ? 0 : 1)
            + (writableLength == 0 ? 0 : 1) + (library ? 1 : 0));
        Write16(image, coff + 16, 0xF0);
        Write16(image, coff + 18, library ? 0x2022 : 0x0022); // DLL/executable, large-address-aware
        var optional = coff + 20;
        Write16(image, optional, 0x20B);
        image[optional + 2] = 1;
        Write32(image, optional + 4, Align(code.Length, FileAlignment));
        Write32(image, optional + 8, Align(importTableSize, FileAlignment)
            + Align(readOnlyLength, FileAlignment) + Align(writableLength, FileAlignment)
            + Align(pdataSize, FileAlignment) + Align(xdataSize, FileAlignment)
            + Align(exportSize, FileAlignment));
        Write32(image, optional + 16, library ? 0 : textRva + entry);
        Write32(image, optional + 20, textRva);
        Write64(image, optional + 24, ImageBase);
        Write32(image, optional + 32, SectionAlignment);
        Write32(image, optional + 36, FileAlignment);
        Write16(image, optional + 40, 6);
        Write16(image, optional + 48, 6);
        Write32(image, optional + 56, imageSize);
        Write32(image, optional + 60, HeaderSize);
        Write16(image, optional + 68, 3); // console
        Write16(image, optional + 70, 0x0160); // NX compatible, dynamic base, high-entropy VA
        Write64(image, optional + 72, 0x100000);
        Write64(image, optional + 80, 0x1000);
        Write64(image, optional + 88, 0x100000);
        Write64(image, optional + 96, 0x1000);
        Write32(image, optional + 108, 16);
        if (library)
        {
            Write32(image, optional + 112, exportRva);
            Write32(image, optional + 112 + 4, exportSize);
        }
        Write32(image, optional + 112 + 8, dataRva); // import directory
        Write32(image, optional + 112 + 12, imports.DescriptorSize);
        Write32(image, optional + 112 + 24, pdataRva); // exception directory
        Write32(image, optional + 112 + 28, pdataSize);
        Write32(image, optional + 112 + 96, dataRva + imports.IatStart); // IAT directory
        Write32(image, optional + 112 + 100, imports.IatSize);

        var sections = optional + 0xF0;
        Section(0, ".text", code.Length, textRva, textFile, 0x60000020);
        Section(1, ".idata", importTableSize, dataRva, dataFile, unchecked((int)0xC0000040));
        var section = 2;
        if (readOnlyLength != 0)
        {
            Section(section++, ".rdata", readOnlyLength, readOnlyRva, readOnlyFile, 0x40000040);
        }
        if (writableLength != 0)
        {
            Section(section++, ".data", writableLength, writableRva, writableFile, unchecked((int)0xC0000040));
        }
        Section(section++, ".pdata", pdataSize, pdataRva, pdataFile, 0x40000040);
        Section(section++, ".xdata", xdataSize, xdataRva, xdataFile, 0x40000040);
        if (library)
        {
            Section(section, ".edata", exportSize, exportRva, exportFile, 0x40000040);
        }

        void Section(int index, string name, int size, int rva, int file, int flags)
        {
            var at = sections + (index * 40);
            AsciiBytes(name).CopyTo(image, at);
            Write32(image, at + 8, size);
            Write32(image, at + 12, rva);
            Write32(image, at + 16, Align(size, FileAlignment));
            Write32(image, at + 20, file);
            Write32(image, at + 36, flags);
        }
        return image;
    }

    private static ImportTable BuildImports(NativeCode native)
    {
        var groups = new List<ImportGroup> { new("KERNEL32.dll") };
        var groupsByName = new Dictionary<string, ImportGroup>(StringComparer.OrdinalIgnoreCase)
        {
            [groups[0].DllName] = groups[0],
        };
        groups[0].AddSymbol("ExitProcess");
        // Export names are sorted per group below, so fixups only need DLL ordering here.
        foreach (var fixup in native.NativeImports.OrderBy(static item => item.DllName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.DllName, StringComparer.Ordinal))
        {
            if (!groupsByName.TryGetValue(fixup.DllName, out var group))
            {
                group = new ImportGroup(fixup.DllName);
                groups.Add(group);
                groupsByName.Add(fixup.DllName, group);
            }
            group.AddSymbol(fixup.ExportName);
        }
        foreach (var group in groups)
        {
            group.Symbols.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        }
        var descriptors = checked(20 * (groups.Count + 1));
        var at = Align(descriptors, 8);
        foreach (var group in groups)
        {
            group.Ilt = at;
            at = checked(at + (8 * (group.Symbols.Count + 1)));
        }
        var iatStart = at;
        foreach (var group in groups)
        {
            group.Iat = at;
            for (var index = 0; index < group.Symbols.Count; index++)
            {
                group.Symbols[index].Iat = at + (8 * index);
            }
            at = checked(at + (8 * (group.Symbols.Count + 1)));
        }
        var iatSize = at - iatStart;
        foreach (var group in groups)
        {
            foreach (var symbol in group.Symbols)
            {
                symbol.HintName = at;
                at = Align(checked(at + 2 + symbol.Name.Length + 1), 2);
            }
        }
        at = Align(at, 8);
        foreach (var group in groups)
        {
            group.Name = at;
            at = checked(at + group.DllName.Length + 1);
        }
        var bytes = new byte[Align(at, 8)];
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            for (var symbolIndex = 0; symbolIndex < group.Symbols.Count; symbolIndex++)
            {
                var symbol = group.Symbols[symbolIndex];
                AsciiBytes(symbol.Name + "\0").CopyTo(bytes, symbol.HintName + 2);
            }
            AsciiBytes(group.DllName + "\0").CopyTo(bytes, group.Name);
        }
        var targets = new Dictionary<(string DllName, string ExportName), int>();
        foreach (var group in groups)
        {
            foreach (var symbol in group.Symbols)
            {
                targets.Add((group.DllName.ToUpperInvariant(), symbol.Name), symbol.Iat);
            }
        }
        return new ImportTable(bytes, groups, targets, descriptors, iatStart, iatSize);
    }

    private sealed class ImportGroup(string dllName)
    {
        internal string DllName { get; } = dllName;
        internal List<ImportSymbol> Symbols { get; } = [];
        internal int Ilt { get; set; }
        internal int Iat { get; set; }
        internal int Name { get; set; }

        private HashSet<string> SymbolNames { get; } = new(StringComparer.Ordinal);

        internal void AddSymbol(string name)
        {
            if (SymbolNames.Add(name))
            {
                Symbols.Add(new ImportSymbol(name));
            }
        }
    }

    private sealed class ImportSymbol(string name)
    {
        internal string Name { get; } = name;
        internal int HintName { get; set; }
        internal int Iat { get; set; }
    }

    private sealed record ImportTable(byte[] Bytes, List<ImportGroup> Groups,
        Dictionary<(string DllName, string ExportName), int> Targets, int DescriptorSize, int IatStart, int IatSize)
    {
        internal void Relocate(int dataRva)
        {
            for (var index = 0; index < Groups.Count; index++)
            {
                var group = Groups[index];
                Write32(Bytes, index * 20, checked(dataRva + group.Ilt));
                Write32(Bytes, (index * 20) + 12, checked(dataRva + group.Name));
                Write32(Bytes, (index * 20) + 16, checked(dataRva + group.Iat));
                for (var symbolIndex = 0; symbolIndex < group.Symbols.Count; symbolIndex++)
                {
                    var symbol = group.Symbols[symbolIndex];
                    Write64(Bytes, group.Ilt + (symbolIndex * 8), (ulong)checked(dataRva + symbol.HintName));
                    Write64(Bytes, group.Iat + (symbolIndex * 8), (ulong)checked(dataRva + symbol.HintName));
                }
            }
        }

        internal int Displacement(string dllName, string exportName, int dataRva, int textRva, int offset)
        {
            if (!Targets.TryGetValue((dllName.ToUpperInvariant(), exportName), out var iat))
            {
                throw new InvalidOperationException("Missing native import target.");
            }
            return checked(dataRva + iat - (textRva + offset + 4));
        }
    }

    private static byte[] AsciiBytes(string value) => Encoding.ASCII.GetBytes(value);
    private static int Align(int value, int alignment) => checked((value + alignment - 1) & -alignment);
    private static void Write16(byte[] image, int at, int value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(at), checked((ushort)value));
    private static void Write32(byte[] image, int at, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(at), value);
    private static void Write64(byte[] image, int at, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(at), value);
}
