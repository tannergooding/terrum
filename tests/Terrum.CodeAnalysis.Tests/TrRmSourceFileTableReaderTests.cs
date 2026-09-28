// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmSourceFileTableReaderTests
{
    [Fact]
    public void ReadsPathsAndOwnersWithoutResolvingFiles()
    {
        var names = TrRmStringTableWriter.EncodeNames([("src/A.terrum", 0), ("src/\u00E9.terrum", 1)]);
        var strings = new TrRmStringTableReader(names, 2);
        var modules = new TrRmModuleTableReader(TwoModules(), 2, strings, 0, 0, 0, 2);
        var files = new TrRmSourceFileTableReader(TwoFiles(), 2, strings, modules);
        Assert.Equal("src/A.terrum", files.GetPath(1));
        Assert.Equal("src/\u00E9.terrum", files.GetPath(2));
        Assert.Equal(1, files.GetOwnerModule(1));
        Assert.Equal(2, files.GetOwnerModule(2));
        Assert.Throws<InvalidDataException>(() => ReadPath(TwoFiles(), 0));
        Assert.Throws<InvalidDataException>(() => ReadOwner(TwoFiles(), 3));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    [InlineData(8, 3)]
    [InlineData(12, 1)]
    public void RejectsMalformedPathReferencesAndUnassignedDigests(int byteIndex, byte replacement)
    {
        var bytes = TwoFiles();
        bytes[byteIndex] = replacement;
        Assert.Throws<InvalidDataException>(() => Create(bytes));
    }

    [Fact]
    public void RejectsTruncatedAndMismatchedSections()
    {
        Assert.Throws<InvalidDataException>(() => Create(TwoFiles()[..^1]));
        Assert.Throws<InvalidDataException>(() => Create(TwoFiles(), 1));
    }

    private static void Create(byte[] bytes, int count = 2)
    {
        var strings = new TrRmStringTableReader(
            TrRmStringTableWriter.EncodeNames([("src/A.terrum", 0), ("src/\u00E9.terrum", 1)]), 2);
        var modules = new TrRmModuleTableReader(TwoModules(), 2, strings, 0, 0, 0, 2);
        _ = new TrRmSourceFileTableReader(bytes, count, strings, modules);
    }

    private static string ReadPath(byte[] bytes, int row)
    {
        var strings = new TrRmStringTableReader(
            TrRmStringTableWriter.EncodeNames([("src/A.terrum", 0), ("src/\u00E9.terrum", 1)]), 2);
        var modules = new TrRmModuleTableReader(TwoModules(), 2, strings, 0, 0, 0, 2);
        var files = new TrRmSourceFileTableReader(bytes, 2, strings, modules);
        return files.GetPath(row);
    }

    private static int ReadOwner(byte[] bytes, int row)
    {
        var strings = new TrRmStringTableReader(
            TrRmStringTableWriter.EncodeNames([("src/A.terrum", 0), ("src/\u00E9.terrum", 1)]), 2);
        var modules = new TrRmModuleTableReader(TwoModules(), 2, strings, 0, 0, 0, 2);
        var files = new TrRmSourceFileTableReader(bytes, 2, strings, modules);
        return files.GetOwnerModule(row);
    }

    private static byte[] TwoFiles()
    {
        var bytes = new byte[16];
        Write(bytes, 0, 1);
        Write(bytes, 8, 2);
        return bytes;
    }

    private static byte[] TwoModules()
    {
        var bytes = new byte[88];
        Write(bytes, 0, 1);
        Write(bytes, 4, 1);
        Write(bytes, 28, 1);
        Write(bytes, 32, 1);
        Write(bytes, 36, 1);
        Write(bytes, 40, 1);
        Write(bytes, 44, 2);
        Write(bytes, 44 + 4, 1);
        Write(bytes, 44 + 28, 1);
        Write(bytes, 44 + 32, 1);
        Write(bytes, 44 + 36, 1);
        Write(bytes, 44 + 40, 2);
        return bytes;
    }

    private static void Write(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
