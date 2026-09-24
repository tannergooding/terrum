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

    internal static byte[] Build(byte[] code, int entry, List<(int Start, int End, int Frame)> functions, List<int> importOffsets)
    {
        const int ImportTableSize = 104;
        var textRva = TextRva;
        var dataRva = Align(textRva + code.Length, SectionAlignment);
        var pdataSize = checked(12 * functions.Count);
        var pdataRva = Align(dataRva + ImportTableSize, SectionAlignment);
        var xdataSize = checked(8 * functions.Count);
        var xdataRva = Align(pdataRva + pdataSize, SectionAlignment);
        var imageSize = Align(xdataRva + xdataSize, SectionAlignment);
        var textFile = HeaderSize;
        var dataFile = checked(textFile + Align(code.Length, FileAlignment));
        var pdataFile = checked(dataFile + Align(ImportTableSize, FileAlignment));
        var xdataFile = checked(pdataFile + Align(pdataSize, FileAlignment));
        var fileSize = checked(xdataFile + Align(xdataSize, FileAlignment));
        var image = new byte[fileSize];
        code.CopyTo(image, textFile);

        foreach (var offset in importOffsets)
        {
            Write32(image, textFile + offset, dataRva + 56 - (textRva + offset + 4));
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
        foreach (var (start, end, frame) in functions)
        {
            Unwind(start, end, frame);
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

        // DOS header and PE32+ COFF headers. All absolute addresses live in PE metadata,
        // not code/data; relocations are intentionally stripped and ASLR is not advertised.
        Write16(image, 0, 0x5A4D);
        Write32(image, 0x3C, 0x80);
        var pe = 0x80;
        Write32(image, pe, 0x00004550);
        var coff = pe + 4;
        Write16(image, coff, 0x8664);
        Write16(image, coff + 2, 4);
        Write16(image, coff + 16, 0xF0);
        Write16(image, coff + 18, 0x0023); // executable, relocations stripped, large-address-aware
        var optional = coff + 20;
        Write16(image, optional, 0x20B);
        image[optional + 2] = 1;
        Write32(image, optional + 4, Align(code.Length, FileAlignment));
        Write32(image, optional + 8, Align(ImportTableSize, FileAlignment)
            + Align(pdataSize, FileAlignment) + Align(xdataSize, FileAlignment));
        Write32(image, optional + 16, textRva + entry);
        Write32(image, optional + 20, textRva);
        Write64(image, optional + 24, ImageBase);
        Write32(image, optional + 32, SectionAlignment);
        Write32(image, optional + 36, FileAlignment);
        Write16(image, optional + 40, 6);
        Write16(image, optional + 48, 6);
        Write32(image, optional + 56, imageSize);
        Write32(image, optional + 60, HeaderSize);
        Write16(image, optional + 68, 3); // console
        Write16(image, optional + 70, 0x0100); // NX compatible; no DYNAMIC_BASE without relocations
        Write64(image, optional + 72, 0x100000);
        Write64(image, optional + 80, 0x1000);
        Write64(image, optional + 88, 0x100000);
        Write64(image, optional + 96, 0x1000);
        Write32(image, optional + 108, 16);
        Write32(image, optional + 112 + 8, dataRva); // import directory
        Write32(image, optional + 112 + 12, 40);
        Write32(image, optional + 112 + 24, pdataRva); // exception directory
        Write32(image, optional + 112 + 28, pdataSize);
        Write32(image, optional + 112 + 96, dataRva + 56); // IAT directory
        Write32(image, optional + 112 + 100, 16);

        var sections = optional + 0xF0;
        Section(0, ".text", code.Length, textRva, textFile, 0x60000020);
        Section(1, ".idata", ImportTableSize, dataRva, dataFile, unchecked((int)0xC0000040));
        Section(2, ".pdata", pdataSize, pdataRva, pdataFile, 0x40000040);
        Section(3, ".xdata", xdataSize, xdataRva, xdataFile, 0x40000040);

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
