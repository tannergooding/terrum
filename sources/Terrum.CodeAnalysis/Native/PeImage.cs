// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Terrum.CodeAnalysis.Native;

internal static class PeImage
{
    private const int SectionAlignment = 0x1000;
    private const int FileAlignment = 0x200;
    private const int HeaderSize = 0x400;
    private const int TextRva = 0x1000;
    private const ulong ImageBase = 0x140000000;

    internal static byte[] Build(NativeCode native) => BuildCore(native, null, 0);

    internal static byte[] BuildLibrary(NativeCode native, IReadOnlyList<(string Name, int Index)> exports,
        int sourceFunctionCount) => BuildCore(native, exports, sourceFunctionCount);

    private static byte[] BuildCore(NativeCode native, IReadOnlyList<(string Name, int Index)>? exports,
        int sourceFunctionCount)
    {
        var library = exports is not null;
        var code = native.Code;
        var entry = native.AotEntryPoint;
        var functions = native.Functions;
        var importOffsets = native.ExitProcessFixups;
        var readOnlyLength = native.ReadOnlyDataLength;
        var writableLength = native.Data.Length - readOnlyLength;
        const int ImportTableSize = 104;
        var textRva = TextRva;
        var dataRva = Align(textRva + code.Length, SectionAlignment);
        var readOnlyRva = readOnlyLength == 0 ? 0 : Align(dataRva + ImportTableSize, SectionAlignment);
        var writableRva = writableLength == 0 ? 0 : Align(
            readOnlyLength == 0 ? dataRva + ImportTableSize : readOnlyRva + readOnlyLength, SectionAlignment);
        var pdataSize = checked(12 * functions.Length);
        var pdataRva = Align(writableLength != 0 ? writableRva + writableLength
            : readOnlyLength != 0 ? readOnlyRva + readOnlyLength : dataRva + ImportTableSize, SectionAlignment);
        var xdataSize = checked(8 * functions.Length);
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
        var readOnlyFile = checked(dataFile + Align(ImportTableSize, FileAlignment));
        var writableFile = checked(readOnlyFile + Align(readOnlyLength, FileAlignment));
        var pdataFile = checked(writableFile + Align(writableLength, FileAlignment));
        var xdataFile = checked(pdataFile + Align(pdataSize, FileAlignment));
        var exportFile = checked(xdataFile + Align(xdataSize, FileAlignment));
        var fileSize = checked(exportFile + Align(exportSize, FileAlignment));
        var image = new byte[fileSize];
        code.CopyTo(image, textFile);
        native.Data.AsSpan(0, readOnlyLength).CopyTo(image.AsSpan(readOnlyFile));
        native.Data.AsSpan(readOnlyLength).CopyTo(image.AsSpan(writableFile));

        foreach (var offset in importOffsets)
        {
            Write32(image, textFile + offset, dataRva + 56 - (textRva + offset + 4));
        }
        foreach (var fixup in native.DataFixups)
        {
            var targetRva = fixup.DataOffset < readOnlyLength
                ? readOnlyRva + fixup.DataOffset
                : writableLength != 0 ? writableRva + fixup.DataOffset - readOnlyLength : dataRva;
            Write32(image, textFile + fixup.DisplacementOffset,
                checked(targetRva - (textRva + fixup.DisplacementOffset + 4)));
        }
        // A single import descriptor, null terminator, ILT, IAT, hint/name, and DLL name.
        Write32(image, dataFile, dataRva + 40);
        Write32(image, dataFile + 12, dataRva + 88);
        Write32(image, dataFile + 16, dataRva + 56);
        Write64(image, dataFile + 40, (ulong)(dataRva + 72));
        Write64(image, dataFile + 56, (ulong)(dataRva + 72));
        Encoding.ASCII.GetBytes("ExitProcess\0").CopyTo(image, dataFile + 74);
        Encoding.ASCII.GetBytes("KERNEL32.dll\0").CopyTo(image, dataFile + 88);

        var range = 0;
        foreach (var function in functions)
        {
            Unwind(function.Start, function.End, function.StackSize);
        }
        if (exports is not null)
        {
            var count = exports.Count;
            var functionsAt = 40;
            var namesAt = checked(functionsAt + (4 * count));
            var ordinalsAt = checked(namesAt + (4 * count));
            var dllNameAt = checked(ordinalsAt + (2 * count));
            var dllName = Encoding.ASCII.GetBytes("terrum.dll\0");
            dllName.CopyTo(image, exportFile + dllNameAt);
            var stringAt = dllNameAt + dllName.Length;
            Write32(image, exportFile + 12, exportRva + dllNameAt);
            Write32(image, exportFile + 16, 1); // ordinal base
            Write32(image, exportFile + 20, count);
            Write32(image, exportFile + 24, count);
            Write32(image, exportFile + 28, exportRva + functionsAt);
            Write32(image, exportFile + 32, exportRva + namesAt);
            Write32(image, exportFile + 36, exportRva + ordinalsAt);
            // The encoder appends source-function ranges after its runtime failure helpers.
            var sourceStart = functions.Length - sourceFunctionCount;
            for (var i = 0; i < count; i++)
            {
                var (name, index) = exports[i];
                Write32(image, exportFile + functionsAt + (4 * i), textRva + functions[sourceStart + index].Start);
                Write32(image, exportFile + namesAt + (4 * i), exportRva + stringAt);
                Write16(image, exportFile + ordinalsAt + (2 * i), i);
                var nameBytes = Encoding.ASCII.GetBytes(name + "\0");
                nameBytes.CopyTo(image, exportFile + stringAt);
                stringAt += nameBytes.Length;
            }
        }

        void Unwind(int start, int end, int frame)
        {
            var record = pdataFile + (range * 12);
            Write32(image, record, textRva + start);
            Write32(image, record + 4, textRva + end);
            Write32(image, record + 8, xdataRva + (range * 8));
            var data = xdataFile + (range * 8);
            image[data] = 1; // UNWIND_INFO v1, no flags
            image[data + 1] = 7; // sub rsp,imm32 occupies 7 prolog bytes
            image[data + 2] = 2; // UWOP_ALLOC_LARGE consumes one extra slot
            image[data + 3] = 0;
            image[data + 4] = 7;
            image[data + 5] = 1; // UWOP_ALLOC_LARGE, opinfo 0: next 16-bit value is bytes / 8
            Write16(image, data + 6, frame / 8);
            range++;
        }

        // Code and data references are RIP-relative; imports and unwind entries are RVAs.
        // No image-base-dependent locations need entries in the base relocation directory.
        Write16(image, 0, 0x5A4D);
        Write32(image, 0x3C, 0x80);
        var pe = 0x80;
        Write32(image, pe, 0x00004550);
        var coff = pe + 4;
        Write16(image, coff, 0x8664);
        Write16(image, coff + 2, 4 + (readOnlyLength == 0 ? 0 : 1)
            + (writableLength == 0 ? 0 : 1) + (library ? 1 : 0));
        Write16(image, coff + 16, 0xF0);
        Write16(image, coff + 18, library ? 0x2022 : 0x0022); // DLL/executable, large-address-aware
        var optional = coff + 20;
        Write16(image, optional, 0x20B);
        image[optional + 2] = 1;
        Write32(image, optional + 4, Align(code.Length, FileAlignment));
        Write32(image, optional + 8, Align(ImportTableSize, FileAlignment)
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
        Write32(image, optional + 112 + 12, 40);
        Write32(image, optional + 112 + 24, pdataRva); // exception directory
        Write32(image, optional + 112 + 28, pdataSize);
        Write32(image, optional + 112 + 96, dataRva + 56); // IAT directory
        Write32(image, optional + 112 + 100, 16);

        var sections = optional + 0xF0;
        Section(0, ".text", code.Length, textRva, textFile, 0x60000020);
        Section(1, ".idata", ImportTableSize, dataRva, dataFile, unchecked((int)0xC0000040));
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
            Encoding.ASCII.GetBytes(name).CopyTo(image, at);
            Write32(image, at + 8, size);
            Write32(image, at + 12, rva);
            Write32(image, at + 16, Align(size, FileAlignment));
            Write32(image, at + 20, file);
            Write32(image, at + 36, flags);
        }
        return image;
    }

    private static int Align(int value, int alignment) => checked((value + alignment - 1) & -alignment);
    private static void Write16(byte[] image, int at, int value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(at), checked((ushort)value));
    private static void Write32(byte[] image, int at, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(at), value);
    private static void Write64(byte[] image, int at, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(at), value);
}
