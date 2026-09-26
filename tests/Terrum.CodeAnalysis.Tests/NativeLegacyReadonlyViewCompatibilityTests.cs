// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeLegacyReadonlyViewCompatibilityTests
{
    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast, "fail-fast",
        "11958CC2BE99A8A7AD5E89F161D0809AB6986319237F1B602190CE1F82B00289",
        "0E620A2328EFE99646FEFCDE03D025869AD0FE273D1BBBABC27CCA69CB389DA2",
        "7D1732CF8E0770FB5DECB071BC6774A6002D8E9ADBD39ACEE2A1133D5D14DD33")]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, "checked-exceptions",
        "6EA5309F3C645E481A22B0AB25954738134D5275BDE3F4C8A16D98EE15BC4C7C",
        "2DCDEE14F3F8DBDEA6107A9004D0C4397947B466A0BB42DF937269B13966CC1C",
        "B25697180BB47E0BA6C9A535D6A1064927BA28A7A0E55FD1DED5CB856EC877A9")]
    public async Task FrozenV16GraphAcceptsNewWritableLeafAsync(NativeRuntimePolicy policy, string name,
        string rootHash, string middleHash, string readonlyHash)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Native");
        var rootPath = Path.Combine(directory, $"scoped-view-{name}-root.trng");
        var middlePath = Path.Combine(directory, $"scoped-view-{name}-middle.trng");
        var root = await File.ReadAllBytesAsync(rootPath);
        var middle = await File.ReadAllBytesAsync(middlePath);
        var readonlyLeaf = await File.ReadAllBytesAsync(
            Path.Combine(directory, $"scoped-view-{name}-replacement.trmn"));
        Assert.Equal(rootHash, Convert.ToHexString(SHA256.HashData(root)));
        Assert.Equal(middleHash, Convert.ToHexString(SHA256.HashData(middle)));
        Assert.Equal(readonlyHash, Convert.ToHexString(SHA256.HashData(readonlyLeaf)));
        Assert.Equal(2, root[4]);
        Assert.Equal(2, middle[4]);
        Assert.Equal(1, readonlyLeaf[4]);
        Assert.Equal(16, SemanticVersion(root));
        Assert.Equal(16, SemanticVersion(middle));
        Assert.Equal(16, SemanticVersion(readonlyLeaf));
        Assert.Equal("native-16", NativeModuleCompiler.Inspect(readonlyLeaf).NativeProfileVersion);

        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var source = await File.ReadAllTextAsync(Path.Combine(repository, "samples", "native", "graph",
            "writable-view-leaf.terrum"));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var writableLeaf = NativeModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(compilation.Program), ["Leaf"], "NodeB", policy);
        Assert.Equal(18, SemanticVersion(writableLeaf));
        Assert.Equal("native-17", NativeModuleCompiler.Inspect(writableLeaf).NativeProfileVersion);

        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(repository, "artifacts", "bin", "sources", "terrumc",
            configuration, frameworkDirectory.Name, "terrumc.exe");
        var savedRoot = (byte[])root.Clone();
        var savedMiddle = (byte[])middle.Clone();
        NativeModuleCompiler.ValidateGraph(root, [middle, readonlyLeaf]);
        Assert.Equal(44, await NativeModuleCompiler.RunGraphAsync(root, [middle, readonlyLeaf],
            TimeSpan.FromSeconds(10), policy));
        Assert.Equal(44, await NativeModuleCompiler.RunGraphJitAsync(root, [readonlyLeaf, middle], apphost,
            TimeSpan.FromSeconds(30), policy));
        NativeModuleCompiler.ValidateGraph(root, [writableLeaf, middle]);
        Assert.Equal(46, await NativeModuleCompiler.RunGraphAsync(root, [middle, writableLeaf],
            TimeSpan.FromSeconds(10), policy));
        Assert.Equal(46, await NativeModuleCompiler.RunGraphJitAsync(root, [middle, writableLeaf], apphost,
            TimeSpan.FromSeconds(30), policy));
        Assert.Equal(savedRoot, root);
        Assert.Equal(savedMiddle, middle);
        Assert.Equal(savedRoot, await File.ReadAllBytesAsync(rootPath));
        Assert.Equal(savedMiddle, await File.ReadAllBytesAsync(middlePath));
    }

    private static int SemanticVersion(byte[] package)
    {
        var graph = package.AsSpan().StartsWith("TNGP"u8);
        var contractLength = BinaryPrimitives.ReadInt32LittleEndian(package.AsSpan(graph ? 11 : 10, 4));
        var imageLength = BinaryPrimitives.ReadInt32LittleEndian(package.AsSpan(graph ? 15 : 14, 4));
        var contract = package.Length - 32 - imageLength - contractLength;
        Assert.True(contractLength >= 12 && contract >= (graph ? 19 : 18));
        Assert.True(package.AsSpan(contract, 4).SequenceEqual("TRRM"u8));
        return BinaryPrimitives.ReadInt32LittleEndian(package.AsSpan(contract + 4, 4));
    }
}
