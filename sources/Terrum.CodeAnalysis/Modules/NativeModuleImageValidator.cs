// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using Terrum.CodeAnalysis.Native;

namespace Terrum.CodeAnalysis.Modules;

internal static class NativeModuleImageValidator
{
    // The semantic verifier limits visited function/region nodes to 100,000;
    // allow eight additional entry and process-failure helpers.
    private const int MaximumUnwindRecords = 100_008;

    internal static void Validate(byte[] image, bool isConsumer, string providerDll, IReadOnlyList<string> exports,
        NativeRuntimePolicy policy = NativeRuntimePolicy.FailFast, bool allowOwnedBufferRuntime = false,
        bool allowManagedRuntimeData = false)
        => Validate(image, isConsumer, isConsumer
            ? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [providerDll] = exports }
            : new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal), exports, policy,
            allowOwnedBufferRuntime, allowManagedRuntimeData);

    internal static void Validate(byte[] image, bool isConsumer,
        IReadOnlyDictionary<string, IReadOnlyList<string>> providerImports, IReadOnlyList<string> exports,
        NativeRuntimePolicy policy = NativeRuntimePolicy.FailFast, bool allowOwnedBufferRuntime = false,
        bool allowManagedRuntimeData = false)
    {
        if (policy is not (NativeRuntimePolicy.FailFast or NativeRuntimePolicy.CheckedExceptions))
        {
            throw Invalid();
        }
        var checkedExceptions = policy == NativeRuntimePolicy.CheckedExceptions;
        try
        {
            using var stream = new MemoryStream(image, writable: false);
            using var reader = new PEReader(stream);
            var headers = reader.PEHeaders;
            if (headers.PEHeader is not PEHeader header
                || headers.CoffHeader.Machine != Machine.Amd64 || header.Magic != PEMagic.PE32Plus
                || headers.CoffHeader.SizeOfOptionalHeader != 0xF0
                || header.NumberOfRvaAndSizes != 16 || header.SectionAlignment != 0x1000
                || header.FileAlignment != 0x200 || header.ImageBase != 0x140000000
                || ((int)headers.CoffHeader.Characteristics & 0x23) != 0x22
                || (((int)headers.CoffHeader.Characteristics & 0x2000) != (isConsumer ? 0 : 0x2000))
                || ((int)header.DllCharacteristics & 0x160) != 0x160
                || header.CorHeaderTableDirectory.Size != 0
                || header.BaseRelocationTableDirectory.Size != 0)
            {
                throw Invalid();
            }
            var sections = headers.SectionHeaders;
            var managedData = allowManagedRuntimeData;
            if (sections.Length != (isConsumer ? managedData ? 5 : 4 : managedData ? 6 : 5)
                || sections[0].Name != ".text"
                || sections[1].Name != ".idata"
                || (managedData && sections[2].Name != ".data")
                || sections[managedData ? 3 : 2].Name != ".pdata"
                || sections[managedData ? 4 : 3].Name != ".xdata"
                || (!isConsumer && sections[managedData ? 5 : 4].Name != ".edata"))
            {
                throw Invalid();
            }
            long previousRaw = header.SizeOfHeaders;
            long previousRva = header.SizeOfHeaders;
            for (var index = 0; index < sections.Length; index++)
            {
                var section = sections[index];
                var flags = unchecked((int)section.SectionCharacteristics);
                var writable = (flags & unchecked((int)0x80000000)) != 0;
                var executable = (flags & 0x20000000) != 0;
                var expectedFlags = index == 0 ? 0x60000020
                    : index == 1 || (managedData && index == 2)
                        ? unchecked((int)0xC0000040) : 0x40000040;
                if (section.VirtualSize <= 0 || section.SizeOfRawData < section.VirtualSize
                    || section.PointerToRawData < previousRaw || section.VirtualAddress < previousRva
                    || section.PointerToRawData % header.FileAlignment != 0
                    || section.VirtualAddress % header.SectionAlignment != 0
                    || (long)section.PointerToRawData + section.SizeOfRawData > image.Length
                    || (long)section.VirtualAddress + section.VirtualSize > header.SizeOfImage
                    || (writable && executable) || flags != expectedFlags)
                {
                    throw Invalid();
                }
                previousRaw = (long)section.PointerToRawData + section.SizeOfRawData;
                previousRva = ((long)section.VirtualAddress + section.VirtualSize + header.SectionAlignment - 1)
                    / header.SectionAlignment * header.SectionAlignment;
            }
            if (managedData)
            {
                var data = sections[2];
                if (data.VirtualSize != 24
                    || image.AsSpan(data.PointerToRawData, 24).IndexOfAnyExcept((byte)0) >= 0)
                {
                    throw Invalid();
                }
            }
            if (previousRaw != image.Length || previousRva != header.SizeOfImage)
            {
                throw Invalid();
            }
            if ((header.AddressOfEntryPoint == 0 && isConsumer)
                || (header.AddressOfEntryPoint != 0 && !isConsumer))
            {
                throw Invalid();
            }
            CheckDirectory(header.ImportTableDirectory, ".idata");
            CheckDirectory(header.ImportAddressTableDirectory, ".idata");
            CheckDirectory(header.ExceptionTableDirectory, ".pdata");
            if (isConsumer)
            {
                if (header.ExportTableDirectory.Size != 0 || header.ExportTableDirectory.RelativeVirtualAddress != 0)
                {
                    throw Invalid();
                }
                _ = Offset(header.AddressOfEntryPoint, 1, ".text");
            }
            else
            {
                CheckDirectory(header.ExportTableDirectory, ".edata");
            }
            var pe = Read32(0x3C);
            var optional = checked(pe + 24);
            for (var index = 0; index < 16; index++)
            {
                if (index is 0 or 1 or 3 or 12)
                {
                    continue;
                }
                if (Read32(optional + 112 + (index * 8)) != 0 || Read32(optional + 116 + (index * 8)) != 0)
                {
                    throw Invalid();
                }
            }
            var sourceFunctionStarts = new HashSet<int>();
            CheckImports();
            CheckUnwind();
            if (!isConsumer)
            {
                CheckExports(exports);
            }

            void CheckDirectory(DirectoryEntry directory, string name)
            {
                if (directory.RelativeVirtualAddress <= 0 || directory.Size <= 0)
                {
                    throw Invalid();
                }
                _ = Offset(directory.RelativeVirtualAddress, directory.Size, name);
            }

            int Offset(int rva, int size, string? name = null)
            {
                if (rva < 0 || size < 0)
                {
                    throw Invalid();
                }
                foreach (var section in sections)
                {
                    if (name is not null && section.Name != name)
                    {
                        continue;
                    }
                    var delta = (long)rva - section.VirtualAddress;
                    if (delta >= 0 && delta + size <= section.VirtualSize && delta + size <= section.SizeOfRawData)
                    {
                        return checked(section.PointerToRawData + (int)delta);
                    }
                }
                throw Invalid();
            }

            int Read32(int at)
            {
                if (at < 0 || (long)at + 4 > image.Length)
                {
                    throw Invalid();
                }
                return BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at, 4));
            }

            int Rva32(int rva)
            {
                return Read32(Offset(rva, 4));
            }

            int Rva64(int rva)
            {
                var value = BinaryPrimitives.ReadUInt64LittleEndian(image.AsSpan(Offset(rva, 8), 8));
                if (value > int.MaxValue)
                {
                    throw Invalid();
                }
                return (int)value;
            }

            string Name(int rva)
            {
                const int maximumLength = 256;
                Span<byte> bytes = stackalloc byte[maximumLength];
                for (var index = 0; index <= maximumLength; index++)
                {
                    var character = image[Offset(checked(rva + index), 1)];
                    if (character == 0)
                    {
                        if (index == 0)
                        {
                            throw Invalid();
                        }
                        return Encoding.ASCII.GetString(bytes[..index]);
                    }
                    if (character is < 33 or > 126 || index == maximumLength)
                    {
                        throw Invalid();
                    }
                    bytes[index] = character;
                }
                throw Invalid();
            }

            void CheckImports()
            {
                var table = header.ImportTableDirectory;
                var count = 1 + providerImports.Count + (checkedExceptions ? 1 : 0);
                if (table.Size != 20 * (count + 1))
                {
                    throw Invalid();
                }
                var names = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                var systemCount = checkedExceptions ? 2 : 1;
                var expectedDlls = providerImports.Keys.Order(StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static (string item) => item, StringComparer.Ordinal).ToArray();
                for (var index = 0; index < count; index++)
                {
                    var at = Offset(table.RelativeVirtualAddress + (index * 20), 20, ".idata");
                    var lookup = Read32(at);
                    var name = Read32(at + 12);
                    var address = Read32(at + 16);
                    if (Read32(at + 4) != 0 || Read32(at + 8) != 0 || lookup % 8 != 0 || address % 8 != 0)
                    {
                        throw Invalid();
                    }
                    var dll = Name(name);
                    if (dll != (index == 0 ? "KERNEL32.dll"
                        : checkedExceptions && index == 1 ? "NTDLL.dll" : expectedDlls[index - systemCount]))
                    {
                        throw Invalid();
                    }
                    var symbols = new List<string>();
                    string? previousSymbol = null;
                    var hasExitProcess = false;
                    var hasRaiseException = false;
                    var hasGetProcessHeap = false;
                    var hasHeapAlloc = false;
                    var hasHeapFree = false;
                    var hasUnsupportedKernelSymbol = false;
                    var hasOwnedBufferKernelSymbol = false;
                    for (var symbol = 0; symbol <= 1024; symbol++)
                    {
                        var hint = Rva64(checked(lookup + (symbol * 8)));
                        var iat = Rva64(checked(address + (symbol * 8)));
                        if (hint != iat)
                        {
                            throw Invalid();
                        }
                        if (hint == 0)
                        {
                            break;
                        }
                        if (symbol == 1024 || BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(Offset(hint, 2), 2)) != 0)
                        {
                            throw Invalid();
                        }
                        var symbolName = Name(hint + 2);
                        if (previousSymbol is not null
                            && StringComparer.Ordinal.Compare(previousSymbol, symbolName) >= 0)
                        {
                            throw Invalid();
                        }
                        if (index == 0)
                        {
                            switch (symbolName)
                            {
                                case "ExitProcess":
                                    hasExitProcess = true;
                                    break;
                                case "RaiseException":
                                    hasRaiseException = true;
                                    break;
                                case "GetProcessHeap":
                                    hasOwnedBufferKernelSymbol = true;
                                    hasGetProcessHeap = true;
                                    break;
                                case "HeapAlloc":
                                    hasOwnedBufferKernelSymbol = true;
                                    hasHeapAlloc = true;
                                    break;
                                case "HeapFree":
                                    hasOwnedBufferKernelSymbol = true;
                                    hasHeapFree = true;
                                    break;
                                default:
                                    hasUnsupportedKernelSymbol = true;
                                    break;
                            }
                        }
                        symbols.Add(symbolName);
                        previousSymbol = symbolName;
                    }
                    if (symbols.Count == 0
                        || lookup < table.RelativeVirtualAddress + table.Size
                        || lookup >= header.ImportAddressTableDirectory.RelativeVirtualAddress
                        || address < header.ImportAddressTableDirectory.RelativeVirtualAddress
                        || (long)address + ((symbols.Count + 1) * 8)
                            > (long)header.ImportAddressTableDirectory.RelativeVirtualAddress
                                + header.ImportAddressTableDirectory.Size)
                    {
                        throw Invalid();
                    }
                    if (index == 0 && (!hasExitProcess
                        || hasRaiseException != checkedExceptions
                        || hasUnsupportedKernelSymbol
                        || (hasOwnedBufferKernelSymbol && !allowOwnedBufferRuntime)
                        || (hasGetProcessHeap != (hasHeapAlloc || hasHeapFree))))
                    {
                        throw Invalid();
                    }
                    if (checkedExceptions && index == 1 && !symbols.SequenceEqual(["__C_specific_handler"]))
                    {
                        throw Invalid();
                    }
                    if (index >= systemCount)
                    {
                        names.Add(dll, symbols);
                    }
                }
                var terminator = Offset(table.RelativeVirtualAddress + (count * 20), 20, ".idata");
                if (image.AsSpan(terminator, 20).IndexOfAnyExcept((byte)0) >= 0
                    || header.ImportAddressTableDirectory.Size < 16)
                {
                    throw Invalid();
                }
                if (names.Count != providerImports.Count
                    || names.Any(pair => !pair.Value.SequenceEqual(providerImports[pair.Key].Order(StringComparer.Ordinal))))
                {
                    throw Invalid();
                }
            }

            void CheckUnwind()
            {
                var table = header.ExceptionTableDirectory;
                // Earlier exact section-order validation fixes .text first and .xdata last before provider .edata.
                var infoSection = sections[sections.Length - (isConsumer ? 1 : 2)];
                if (table.Size % 12 != 0 || table.Size < 12
                    || table.Size > MaximumUnwindRecords * NativeUnwind.RuntimeFunctionSize)
                {
                    throw Invalid();
                }
                // The fixed runtime prelude is allocation-only; current source
                // functions and cleanup funclets use saved-frame layouts.
                var sourceStart = isConsumer ? 6 : 4;
                if (table.Size / NativeUnwind.RuntimeFunctionSize <= sourceStart)
                {
                    throw Invalid();
                }
                var nextInfo = infoSection.VirtualAddress;
                var previousEnd = sections[0].VirtualAddress;
                var sawSourceFrame = false;
                var sawCleanupFrame = false;
                var sawManagedCollector = false;
                var cleanupStarts = new HashSet<int>();
                var exceptionalCleanups = new List<int>();
                for (var index = 0; index < table.Size / 12; index++)
                {
                    var at = Offset(table.RelativeVirtualAddress + (index * 12), 12, ".pdata");
                    var begin = Read32(at);
                    var end = Read32(at + 4);
                    var unwind = Read32(at + 8);
                    if (begin < previousEnd || end <= begin || unwind != nextInfo)
                    {
                        throw Invalid();
                    }
                    var info = Offset(unwind, 8, ".xdata");
                    var savedFrame = image[info + 2] == 3;
                    var baseSize = savedFrame ? 12 : 8;
                    var flags = image[info];
                    var scoped = flags == 0x19;
                    if ((flags != 1 && (!checkedExceptions || !scoped))
                        || (!checkedExceptions && scoped))
                    {
                        throw Invalid();
                    }
                    var size = baseSize;
                    if (scoped)
                    {
                        var metadataAt = Offset(unwind + baseSize, 8, ".xdata");
                        var handler = Read32(metadataAt);
                        var count = Read32(metadataAt + 4);
                        var thunk = Offset(handler, 6, ".text");
                        if (count is < 1 or > MaximumUnwindRecords
                            || image[thunk] != 0xFF || image[thunk + 1] != 0x25)
                        {
                            throw Invalid();
                        }
                        size = checked(baseSize + 8 + (count * 16));
                        var scopes = Offset(unwind + baseSize + 8, count * 16, ".xdata");
                        for (var scope = 0; scope < count; scope++)
                        {
                            var rangeBegin = Read32(scopes + (scope * 16));
                            var rangeEnd = Read32(scopes + (scope * 16) + 4);
                            var handlerAddress = Read32(scopes + (scope * 16) + 8);
                            var target = Read32(scopes + (scope * 16) + 12);
                            if (rangeBegin < begin || rangeEnd > end || rangeBegin >= rangeEnd)
                            {
                                throw Invalid();
                            }
                            if (target == 0)
                            {
                                exceptionalCleanups.Add(handlerAddress);
                            }
                            else
                            {
                                var filter = Offset(handlerAddress, 3, ".text");
                                if (target < begin || target >= end || image[filter] != 0x48
                                    || image[filter + 1] != 0x8B || image[filter + 2] != 0x01)
                                {
                                    throw Invalid();
                                }
                            }
                        }
                    }
                    if (end - begin < (savedFrame ? 11 : 7))
                    {
                        throw Invalid();
                    }
                    _ = Offset(begin, end - begin, ".text");
                    info = Offset(unwind, size, ".xdata");
                    if (image[info] != flags || image[info + 1] != (savedFrame ? 11 : 7)
                        || image[info + 2] != (savedFrame ? 3 : 2) || image[info + 3] != 0
                        || image[info + 4] != (savedFrame ? 8 : 7) || image[info + 5] != 1
                        || (savedFrame && (image[info + 8] != 1 || image[info + 9] != 0x50
                            || image[info + 10] != 0 || image[info + 11] != 0)))
                    {
                        throw Invalid();
                    }
                    var allocation = 8 * BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(info + 6, 2));
                    var stackSize = allocation + (savedFrame ? 8 : 0);
                    if (stackSize is < 8 or > 0xF00 || (stackSize & 15) != 8)
                    {
                        throw Invalid();
                    }
                    var code = Offset(begin, savedFrame ? 11 : 7, ".text");
                    if (savedFrame
                        ? image[code] != 0x55 || image[code + 1] != 0x48 || image[code + 2] != 0x81
                            || image[code + 3] != 0xEC
                            || Read32(code + 4) != allocation
                            || image[code + 8] != 0x48 || image[code + 9] != 0x89
                            || image[code + 10] is not (0xE5 or 0xD5)
                        : image[code] != 0x48 || image[code + 1] != 0x81 || image[code + 2] != 0xEC
                            || Read32(code + 3) != allocation)
                    {
                        throw Invalid();
                    }
                    if (index < sourceStart)
                    {
                        if (savedFrame)
                        {
                            throw Invalid();
                        }
                    }
                    else if (!savedFrame)
                    {
                        // The optional image-private collector is the only
                        // allocation-only function after the current prelude.
                        if (managedData && index == (table.Size / 12) - 1
                            && (sawSourceFrame || sawCleanupFrame) && allocation == 120)
                        {
                            sawManagedCollector = true;
                        }
                        else
                        {
                            throw Invalid();
                        }
                    }
                    else if (image[code + 10] == 0xD5)
                    {
                        if (!sawSourceFrame)
                        {
                            throw Invalid();
                        }
                        sawCleanupFrame = true;
                        _ = cleanupStarts.Add(begin);
                    }
                    else
                    {
                        if (sawCleanupFrame)
                        {
                            throw Invalid();
                        }
                        sawSourceFrame = true;
                        _ = sourceFunctionStarts.Add(begin);
                    }
                    previousEnd = end;
                    nextInfo = checked(nextInfo + size);
                }
                if ((managedData && !sawManagedCollector)
                    || exceptionalCleanups.Any(handler => !cleanupStarts.Contains(handler)))
                {
                    throw Invalid();
                }
                if (nextInfo != infoSection.VirtualAddress + infoSection.VirtualSize)
                {
                    throw Invalid();
                }
            }

            void CheckExports(IReadOnlyList<string> expected)
            {
                var table = header.ExportTableDirectory;
                var at = Offset(table.RelativeVirtualAddress, 40, ".edata");
                var count = Read32(at + 20);
                if (Read32(at + 4) != 0 || Read32(at + 8) != 0 || Read32(at + 16) != 1
                    || count is < 1 or > 1024 || count != expected.Count || count != Read32(at + 24)
                    || Name(Read32(at + 12)) != "terrum.dll")
                {
                    throw Invalid();
                }
                var functions = Read32(at + 28);
                var names = Read32(at + 32);
                var ordinals = Read32(at + 36);
                var sorted = expected.Order(StringComparer.Ordinal).ToArray();
                for (var index = 0; index < count; index++)
                {
                    var ordinal = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(Offset(ordinals + (index * 2), 2), 2));
                    var address = Rva32(functions + (index * 4));
                    _ = Offset(address, 1, ".text");
                    if (ordinal != index || !sourceFunctionStarts.Contains(address)
                        || Name(Rva32(names + (index * 4))) != sorted[index])
                    {
                        throw Invalid();
                    }
                }
            }
        }
        catch (BadImageFormatException exception)
        {
            throw new ModuleFormatException("Native module image is not a valid PE32+ image.", exception);
        }
    }

    private static ModuleFormatException Invalid() => new("Malformed or incompatible native module PE image.");
}
