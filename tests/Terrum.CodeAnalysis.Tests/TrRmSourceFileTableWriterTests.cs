// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmSourceFileTableWriterTests
{
    [Fact]
    public void EmitsOwnedSourcePathsForTwoModulesInOneArtifact()
    {
        var names = TrRmStringTableWriter.EncodeNames([
            ("A", 0), ("B", 0), ("src/A.terrum", 1), ("src/B.terrum", 2),
        ]);
        var strings = new TrRmStringTableReader(names, 4);
        var modules = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 0, 0, 0, 1, 1, 1, 1),
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 2),
        ], strings, 0, 0, 0, 2);
        var moduleReader = new TrRmModuleTableReader(modules, 2, strings, 0, 0, 0, 2);
        var paths = new[] { 3, 4 };
        var files = TrRmSourceFileTableWriter.Encode(paths, strings, moduleReader);
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 2, modules),
            new TrRmSection(0x0002, 0, 4, names),
            new TrRmSection(0x001F, 0, 2, files),
        ]);
        TrRmStructuralMetadataValidator.Validate(artifact);

        paths[0] = 4;
        var reader = new TrRmSourceFileTableReader(files, 2, strings, moduleReader);
        Assert.Equal("src/A.terrum", reader.GetPath(1));
        Assert.Equal("src/B.terrum", reader.GetPath(2));
        Assert.Equal(1, reader.GetOwnerModule(1));
        Assert.Equal(2, reader.GetOwnerModule(2));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(files.AsSpan(4)));
    }

    [Fact]
    public void RejectsMissingEmptyAndOutOfRangePathsAndWrongPartitions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Encode([]));
        Assert.Throws<InvalidDataException>(() => Encode([0, 4]));
        Assert.Throws<InvalidDataException>(() => Encode([3, 5]));
        Assert.Throws<InvalidDataException>(() => Encode([3]));
        Assert.Throws<InvalidDataException>(() => Encode([3, 4, 4]));

        Assert.Throws<InvalidDataException>(EncodeEmptyPath);
    }

    private static byte[] Encode(int[] paths)
    {
        var names = TrRmStringTableWriter.EncodeNames([
            ("A", 0), ("B", 0), ("src/A.terrum", 1), ("src/B.terrum", 2),
        ]);
        var strings = new TrRmStringTableReader(names, 4);
        var modules = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 0, 0, 0, 1, 1, 1, 1),
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 2),
        ], strings, 0, 0, 0, 2);
        var moduleReader = new TrRmModuleTableReader(modules, 2, strings, 0, 0, 0, 2);
        return TrRmSourceFileTableWriter.Encode(paths, strings, moduleReader);
    }

    private static byte[] EncodeEmptyPath()
    {
        var names = TrRmStringTableWriter.EncodeTextRows([("", 0), ("A", 0), ("B", 0)]);
        var strings = new TrRmStringTableReader(names, 3);
        var modules = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1),
            new TrRmModuleRow(3, 0, 0, 0, 1, 1, 1, 2),
        ], strings, 0, 0, 0, 2);
        var moduleReader = new TrRmModuleTableReader(modules, 2, strings, 0, 0, 0, 2);
        return TrRmSourceFileTableWriter.Encode([1, 2], strings, moduleReader);
    }
}
