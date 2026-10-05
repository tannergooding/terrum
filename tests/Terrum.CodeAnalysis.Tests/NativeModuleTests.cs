// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeModuleTests
{
    private const string Provider = """
        public struct Twelve {
            public int32 First; public int32 Second; public int32 Third;
            public Twelve(int32 a, int32 b, int32 c) { First = a; Second = b; Third = c; }
        }
        public struct Wide {
            public int64 Signed; public uint64 Unsigned;
            public Wide(int64 signed, uint64 unsigned) { Signed = signed; Unsigned = unsigned; }
        }
        public Twelve MakeTwelve(int32 a, int32 b, int32 c) { return new Twelve(a, b, c); }
        public Wide MakeWide(int64 signed, uint64 unsigned) { return new Wide(signed, unsigned); }
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
    private const string Consumer = """
        bool Forward(bool success, out Wide result) { return TryWide(success, out result); }
        int32 Recurse(int32 count) {
            if (count == 0) {
                return Evaluate(MakeTwelve(10, 11, 12),
                    MakeWide(int64.MinValue, uint64.MaxValue), 2147483648, 1, 2);
            }
            return Recurse(count - 1);
        }
        int32 Main() {
            require Forward(true, out var first);
            if (Forward(false, out var ignored)) { return -1; }
            require Forward(true, out var second);
            require first.Signed == int64.MinValue && second.Unsigned == uint64.MaxValue;
            return Recurse(3);
        }
        """;
    private const string OwnedBufferImportSource = """
        public int32 Run() {
            if (!OwnedInt32Buffer.TryCreate(1, out var created)) { return 0; }
            mut OwnedInt32Buffer buffer = move created;
            if (!buffer.TryAppend(42)) { return 0; }
            return buffer.Read(0);
        }
        """;
    private static readonly string[] s_exports = ["Evaluate", "MakeTwelve", "MakeWide", "TryWide"];

    private static BoundProgram CompileLibrary(string source)
    {
        var result = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        return Assert.IsType<BoundProgram>(result.Program);
    }

    private static byte[] Producer(string source = Provider, string identity = "Example", string[]? exports = null) =>
        NativeModuleCompiler.CreateProducer(CompileLibrary(source), exports ?? s_exports, identity, NativeRuntimePolicy.FailFast);

    private static BoundProgram CompileConsumer(string source, byte[] producer)
    {
        var result = Compilation.Create(SyntaxTree.Parse(source), NativeModuleCompiler.CreateReference(producer));
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        return Assert.IsType<BoundProgram>(result.Program);
    }

    private static byte[] ConsumerPackage(byte[] producer, string source = Consumer) =>
        NativeModuleCompiler.CreateConsumer(CompileConsumer(source, producer), producer, NativeRuntimePolicy.FailFast);

    private static byte[] ProducerForPolicy(string source, NativeRuntimePolicy policy, string identity = "Example")
    {
        var boundPolicy = policy == NativeRuntimePolicy.CheckedExceptions
            ? BoundRuntimePolicy.CheckedExceptions : BoundRuntimePolicy.FailFast;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), boundPolicy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program), ["Run"], identity, policy);
    }

    private static int PackageContractOffset(byte[] artifact)
    {
        var at = 18 + artifact[7] + artifact[8];
        var count = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
        at += 2;
        for (var index = 0; index < count; index++)
        {
            var length = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(at, 2));
            at += 2 + length;
        }
        return at;
    }

    private static int PackageImageOffset(byte[] artifact) => PackageContractOffset(artifact)
        + BinaryPrimitives.ReadInt32LittleEndian(artifact.AsSpan(10, 4));

    private static byte[] ReplacePackageImageSymbol(byte[] artifact, ReadOnlySpan<byte> oldSymbol,
        ReadOnlySpan<byte> newSymbol)
    {
        Assert.Equal(oldSymbol.Length, newSymbol.Length);
        var result = (byte[])artifact.Clone();
        var imageOffset = PackageContractOffset(result);
        var imageLength = BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(14, 4));
        var symbolOffset = result.AsSpan(imageOffset, imageLength).IndexOf(oldSymbol);
        var replacement = newSymbol;
        if (symbolOffset < 0 && oldSymbol.Length > 0 && newSymbol.Length > 0
            && oldSymbol[^1] == 0 && newSymbol[^1] == 0)
        {
            oldSymbol = oldSymbol[..^1];
            replacement = newSymbol[..^1];
            symbolOffset = result.AsSpan(imageOffset, imageLength).IndexOf(oldSymbol);
        }
        Assert.True(symbolOffset >= 0);
        replacement.CopyTo(result.AsSpan(imageOffset + symbolOffset, replacement.Length));
        RehashArtifact(result);
        return result;
    }

    private static void RehashArtifact(byte[] artifact)
    {
        SHA256.HashData(artifact.AsSpan(0, artifact.Length - 32)).CopyTo(artifact, artifact.Length - 32);
    }

    [Fact]
    public void NativePackagesUseCurrentVersionAndRejectPreviousVersion()
    {
        var producer = Producer();
        var consumer = ConsumerPackage(producer);
        Assert.Equal(2, producer[4]);
        Assert.Equal(2, consumer[4]);

        foreach (var artifact in new[] { producer, consumer })
        {
            var previousVersion = (byte[])artifact.Clone();
            previousVersion[4] = 1;
            SHA256.HashData(previousVersion.AsSpan(0, previousVersion.Length - 32))
                .CopyTo(previousVersion, previousVersion.Length - 32);
            _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(previousVersion));
        }
    }

    [Fact]
    public async Task SamePersistedConsumerExecutesCompatibleProvidersWithoutReemissionAsync()
    {
        var original = Producer();
        var replacement = Producer(Provider.Replace("Hidden() { return 5;", "Hidden() { return 6;",
            StringComparison.Ordinal));
        var consumer = ConsumerPackage(original);
        var before = (byte[])consumer.Clone();
        var originalInfo = NativeModuleCompiler.Inspect(original);
        var replacementInfo = NativeModuleCompiler.Inspect(replacement);
        var consumerInfo = NativeModuleCompiler.Inspect(consumer);
        Assert.Equal("Example", originalInfo.Identity);
        Assert.False(originalInfo.IsConsumer);
        Assert.True(consumerInfo.IsConsumer);
        Assert.Equal(originalInfo.ContractLength, replacementInfo.ContractLength);
        Assert.NotEqual(originalInfo.ImageSha256, replacementInfo.ImageSha256);
        NativeModuleCompiler.Validate(consumer, original);
        NativeModuleCompiler.Validate(consumer, replacement);
        Assert.Equal(42, await NativeModuleCompiler.RunAsync(consumer, original, TimeSpan.FromSeconds(10)).ConfigureAwait(true));
        Assert.Equal(43, await NativeModuleCompiler.RunAsync(consumer, replacement, TimeSpan.FromSeconds(10)).ConfigureAwait(true));
        Assert.Equal(before, consumer);
        Assert.Equal(consumerInfo.ImageSha256, NativeModuleCompiler.Inspect(consumer).ImageSha256);
        var failed = ConsumerPackage(original, Consumer.Replace("return Recurse(3);",
            "return Evaluate(MakeTwelve(10, 11, 12), MakeWide(0, 0), 2147483648, 1, 2);",
            StringComparison.Ordinal));
        Assert.Equal(unchecked((int)0xE0540003),
            await NativeModuleCompiler.RunAsync(failed, original, TimeSpan.FromSeconds(10)).ConfigureAwait(true));
    }

    [Fact]
    public async Task NativeConsumerAcceptsCompatibleProviderWithReorderedTypesAsync()
    {
        const string typeA = "public struct A { public int32 Value; public A() { Value = 5; } }";
        const string typeB = "struct B { public int32 Number; public B() { Number = 2; } }";
        const string functions = "public int32 Read() { A value = new A(); return value.Value; }";
        var original = NativeModuleCompiler.CreateProducer(CompileLibrary(typeA + typeB + functions),
            ["Read"], "Reordered", NativeRuntimePolicy.FailFast);
        var consumer = ConsumerPackage(original, "int32 Main() { return Read(); }");
        var replacement = NativeModuleCompiler.CreateProducer(CompileLibrary(typeB + typeA + functions),
            ["Read"], "Reordered", NativeRuntimePolicy.FailFast);

        NativeModuleCompiler.Validate(consumer, replacement);
        Assert.Equal(5, await NativeModuleCompiler.RunAsync(consumer, replacement,
            TimeSpan.FromSeconds(10)).ConfigureAwait(true));
    }

    [Fact]
    public async Task PersistedConsumerExecutesReplacementCleanupFuncletsAsync()
    {
        const string Source = """
            [NoThrow] int32 Value() { return 42; }
            public bool Take(out int32 result) {
                try { return true; }
                finally { result = Value(); }
            }
            """;
        var original = Producer(Source, exports: ["Take"]);
        var replacement = Producer(Source.Replace("return 42;", "return 43;", StringComparison.Ordinal),
            exports: ["Take"]);
        var consumer = ConsumerPackage(original, "int32 Main() { require Take(out var value); return value; }");
        NativeModuleCompiler.Validate(consumer, original);
        NativeModuleCompiler.Validate(consumer, replacement);
        Assert.Equal(42, await NativeModuleCompiler.RunAsync(consumer, original, TimeSpan.FromSeconds(10)));
        Assert.Equal(43, await NativeModuleCompiler.RunAsync(consumer, replacement, TimeSpan.FromSeconds(10)));

        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(
            RedirectFirstExport(original, lastRange: true)));
    }

    [Fact]
    public async Task CurrentNativeConsumerRunsSavedFrameProducerAsync()
    {
        const string Original = """
            [NoThrow] void Disturb() { }
            public int32 Read() {
                mut int32 value = 42;
                try { return value; }
                finally { value = 0; Disturb(); }
            }
            """;
        const string Replacement = """
            [NoThrow] void Disturb() { }
            public int32 Read() {
                mut int32 value = 43;
                try { return value; }
                finally { value = 0; Disturb(); }
            }
            """;
        var original = Producer(Original, identity: "Cleanup", exports: ["Read"]);
        var consumer = ConsumerPackage(original, "int32 Main() { return Read(); }");
        var updated = Producer(Replacement, identity: "Cleanup", exports: ["Read"]);
        NativeModuleCompiler.Validate(consumer, original);
        NativeModuleCompiler.Validate(consumer, updated);
        Assert.Equal(42, await NativeModuleCompiler.RunAsync(consumer, original, TimeSpan.FromSeconds(10))
            .ConfigureAwait(true));
        Assert.Equal(43, await NativeModuleCompiler.RunAsync(consumer, updated, TimeSpan.FromSeconds(10))
            .ConfigureAwait(true));
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(
            RedirectFirstExport(original, lastRange: false)));
        var shortSource = (byte[])original.Clone();
        var image = PackageImageOffset(shortSource);
        using var reader = new PEReader(new MemoryStream(shortSource, image,
            BinaryPrimitives.ReadInt32LittleEndian(shortSource.AsSpan(14, 4))));
        var pdata = Assert.Single(reader.PEHeaders.SectionHeaders, section => section.Name == ".pdata");
        var entry = image + pdata.PointerToRawData;
        var begin = BinaryPrimitives.ReadInt32LittleEndian(shortSource.AsSpan(entry, 4));
        BinaryPrimitives.WriteInt32LittleEndian(shortSource.AsSpan(entry + 4, 4), begin + 6);
        SHA256.HashData(shortSource.AsSpan(0, shortSource.Length - 32)).CopyTo(shortSource, shortSource.Length - 32);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(shortSource));
    }

    private static byte[] RedirectFirstExport(byte[] original, bool lastRange)
    {
        var changed = (byte[])original.Clone();
        var image = PackageImageOffset(changed);
        using var reader = new PEReader(new MemoryStream(changed, image,
            BinaryPrimitives.ReadInt32LittleEndian(changed.AsSpan(14, 4))));
        var header = Assert.IsType<PEHeader>(reader.PEHeaders.PEHeader);
        var pdata = Assert.Single(reader.PEHeaders.SectionHeaders, section => section.Name == ".pdata");
        var edata = Assert.Single(reader.PEHeaders.SectionHeaders, section => section.Name == ".edata");
        var at = image + pdata.PointerToRawData + (lastRange ? header.ExceptionTableDirectory.Size - 12 : 0);
        var targetRva = BinaryPrimitives.ReadInt32LittleEndian(changed.AsSpan(at, 4));
        var exportDirectory = image + edata.PointerToRawData;
        var tableRva = BinaryPrimitives.ReadInt32LittleEndian(changed.AsSpan(exportDirectory + 28, 4));
        var table = exportDirectory + tableRva - edata.VirtualAddress;
        BinaryPrimitives.WriteInt32LittleEndian(changed.AsSpan(table, 4), targetRva);
        SHA256.HashData(changed.AsSpan(0, changed.Length - 32)).CopyTo(changed, changed.Length - 32);
        return changed;
    }

    [Fact]
    public async Task AddedExportMayShareConsumerLocalNameWithoutChangingNativeImportsAsync()
    {
        var originalSource = Provider + " public int32 Unused() { return 100; }";
        var original = Producer(originalSource, exports: [.. s_exports, "Unused"]);
        var consumer = ConsumerPackage(original, "int32 Extra() { return 99; } " + Consumer);
        var snapshot = (byte[])consumer.Clone();
        var replacement = Producer(originalSource.Replace("Hidden() { return 5;", "Hidden() { return 6;",
            StringComparison.Ordinal) + " public int32 Extra() { return 200; }",
            exports: [.. s_exports, "Unused", "Extra"]);

        NativeModuleCompiler.Validate(consumer, original);
        NativeModuleCompiler.Validate(consumer, replacement);
        Assert.Equal(42, await NativeModuleCompiler.RunAsync(consumer, original, TimeSpan.FromSeconds(10)).ConfigureAwait(true));
        Assert.Equal(43, await NativeModuleCompiler.RunAsync(consumer, replacement, TimeSpan.FromSeconds(10)).ConfigureAwait(true));
        Assert.Equal(snapshot, consumer);
    }

    [Theory]
    [InlineData("public int32 First;", "public mut int32 First;")]
    [InlineData("public int32 Third;", "public int32 Third; public int32 Fourth;")]
    [InlineData("public int32 Evaluate(", "public bool Evaluate(")]
    [InlineData("public bool TryWide(bool success, out Wide result)", "public bool TryWide(bool success, Wide result)")]
    public void IncompatibleProducerCannotLaunch(string existing, string replacement)
    {
        var original = Producer();
        var consumer = ConsumerPackage(original);
        var incompatibleSource = Provider.Replace(existing, replacement, StringComparison.Ordinal);
        if (existing == "public int32 Third;")
        {
            incompatibleSource = incompatibleSource.Replace("Third = c;", "Third = c; Fourth = 0;",
                StringComparison.Ordinal);
        }
        if (existing == "public int32 Evaluate(")
        {
            incompatibleSource = incompatibleSource.Replace("return value.First + value.Second + value.Third + int32(n / 2147483648)",
                "return (value.First + value.Second + value.Third + int32(n / 2147483648)",
                StringComparison.Ordinal).Replace("+ d + e + Hidden();", "+ d + e + Hidden()) == 42;", StringComparison.Ordinal);
        }
        if (existing == "public bool TryWide(bool success, out Wide result)")
        {
            incompatibleSource = incompatibleSource.Replace("result = MakeWide(int64.MinValue, uint64.MaxValue);",
                "require result.Signed == int64.MinValue;", StringComparison.Ordinal);
        }
        var mismatched = Producer(incompatibleSource);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.Validate(consumer, mismatched));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.RunAsync(consumer, mismatched,
            TimeSpan.FromSeconds(2)).GetAwaiter().GetResult());
    }

    [Fact]
    public void IdentityAndPackageKindsRejectBeforeLaunch()
    {
        var original = Producer();
        var consumer = ConsumerPackage(original);
        var other = Producer(identity: "Other");
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.Validate(consumer, other));
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Validate(original, consumer));
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.CreateReference(consumer));
        _ = Assert.Throws<ModuleContractException>(() => Producer(Provider.Replace("public int32 Evaluate(",
            "int32 Evaluate(", StringComparison.Ordinal)));
        foreach (var identity in new[] { "", "_Bad", "With.Dots", "../escape", new string('A', 65) })
        {
            _ = Assert.Throws<ArgumentException>(() => Producer(identity: identity));
        }
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void NativeImageAcceptsPolicySpecificSystemImports(NativeRuntimePolicy policy)
    {
        var producer = ProducerForPolicy("public int32 Run() { return 42; }", policy);
        _ = NativeModuleCompiler.Inspect(producer);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void NativeImageAcceptsOwnedBufferImportsForEachFailurePolicy(NativeRuntimePolicy policy)
    {
        var producer = ProducerForPolicy(OwnedBufferImportSource, policy, "OwnedImport");
        _ = NativeModuleCompiler.Inspect(producer);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void NativeImageRejectsUnsupportedOwnedBufferKernelSymbols(NativeRuntimePolicy policy)
    {
        var producer = ProducerForPolicy(OwnedBufferImportSource, policy, "OwnedImport");
        var damaged = ReplacePackageImageSymbol(producer, "GetProcessHeap\0"u8, "GetProcessHxxx\0"u8);

        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(damaged));
    }

    [Fact]
    public void MalformedPackagesAndMismatchedPeHeadersAreRejected()
    {
        var original = Producer();
        var consumer = ConsumerPackage(original);
        var boundConsumer = CompileConsumer(Consumer, original);
        foreach (var at in new[] { 0, 4, 5, 6, 7, 8, 9, 10, original.Length - 1 })
        {
            var mutated = (byte[])original.Clone();
            mutated[at] ^= 0x7F;
            _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Validate(consumer, mutated));
        }
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(original[..^1]));
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect([.. original, 0]));
        var wrongProfile = (byte[])original.Clone();
        wrongProfile[18 + wrongProfile[7]] ^= 1;
        Rehash(wrongProfile);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(wrongProfile));
        var wrongContract = (byte[])original.Clone();
        wrongContract[PackageContractOffset(wrongContract)] ^= 1;
        Rehash(wrongContract);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Validate(consumer, wrongContract));
        var wrongImage = (byte[])original.Clone();
        var image = PackageImageOffset(wrongImage);
        var pe = BinaryPrimitives.ReadInt32LittleEndian(wrongImage.AsSpan(image + 0x3C, 4));
        wrongImage[image + pe + 24 + 16] = 1; // DLL must have no entry point
        Rehash(wrongImage);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Validate(consumer, wrongImage));
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(wrongImage));
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.CreateConsumer(boundConsumer, wrongImage,
            (NativeRuntimePolicy)255));
        var wrongExport = (byte[])original.Clone();
        var exportName = wrongExport.AsSpan(image, BinaryPrimitives.ReadInt32LittleEndian(wrongExport.AsSpan(14, 4)))
            .IndexOf("Evaluate\0"u8);
        Assert.True(exportName >= 0);
        wrongExport[image + exportName] = (byte)'X';
        Rehash(wrongExport);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Validate(consumer, wrongExport));
        var wrongImport = (byte[])consumer.Clone();
        var importImage = PackageImageOffset(wrongImport);
        var importName = wrongImport.AsSpan(importImage,
            BinaryPrimitives.ReadInt32LittleEndian(wrongImport.AsSpan(14, 4))).IndexOf("terrum-Example.dll\0"u8);
        Assert.True(importName >= 0);
        wrongImport[importImage + importName + 7] = (byte)'X';
        Rehash(wrongImport);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Validate(wrongImport, original));
        var wrongImportSymbol = ReplacePackageImageSymbol(consumer, "Evaluate\0"u8, "Evalueee\0"u8);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Validate(wrongImportSymbol, original));
        var wrongUnwind = (byte[])original.Clone();
        var xdata = -1;
        var pdata = -1;
        var text = -1;
        var textRva = -1;
        var edata = -1;
        var edataRva = -1;
        var sections = pe + 24 + 0xF0;
        for (var section = 0; section < 5; section++)
        {
            var at = image + sections + (section * 40);
            if (wrongUnwind.AsSpan(at, 6).SequenceEqual(".xdata"u8))
            {
                xdata = BinaryPrimitives.ReadInt32LittleEndian(wrongUnwind.AsSpan(at + 20, 4));
            }
            else if (wrongUnwind.AsSpan(at, 6).SequenceEqual(".pdata"u8))
            {
                pdata = BinaryPrimitives.ReadInt32LittleEndian(wrongUnwind.AsSpan(at + 20, 4));
            }
            else if (wrongUnwind.AsSpan(at, 5).SequenceEqual(".text"u8))
            {
                textRva = BinaryPrimitives.ReadInt32LittleEndian(wrongUnwind.AsSpan(at + 12, 4));
                text = BinaryPrimitives.ReadInt32LittleEndian(wrongUnwind.AsSpan(at + 20, 4));
            }
            else if (wrongUnwind.AsSpan(at, 6).SequenceEqual(".edata"u8))
            {
                edataRva = BinaryPrimitives.ReadInt32LittleEndian(wrongUnwind.AsSpan(at + 12, 4));
                edata = BinaryPrimitives.ReadInt32LittleEndian(wrongUnwind.AsSpan(at + 20, 4));
            }
        }
        Assert.True(xdata >= 0 && pdata >= 0 && text >= 0 && edata >= 0);
        var savedFrameInfo = image + xdata + (4 * 8); // Four process-failure helpers precede DLL source functions.
        Assert.Equal(3, wrongUnwind[savedFrameInfo + 2]);
        wrongUnwind[savedFrameInfo + 9] ^= 1; // Corrupt the saved-RBP unwind operation.
        Rehash(wrongUnwind);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(wrongUnwind));
        var wrongBasicUnwind = (byte[])original.Clone();
        wrongBasicUnwind[image + xdata + 5] ^= 1;
        Rehash(wrongBasicUnwind);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(wrongBasicUnwind));
        var wrongRange = (byte[])original.Clone();
        wrongRange[image + pdata + 8] ^= 1; // The first .pdata record no longer points into .xdata.
        Rehash(wrongRange);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(wrongRange));
        var wrongProlog = (byte[])original.Clone();
        var sourceRva = BinaryPrimitives.ReadInt32LittleEndian(wrongProlog.AsSpan(image + pdata + (4 * 12), 4));
        Assert.Equal((byte)0x55, wrongProlog[image + text + sourceRva - textRva]);
        wrongProlog[image + text + sourceRva - textRva] = 0x90;
        Rehash(wrongProlog);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(wrongProlog));
        var shortBasic = (byte[])original.Clone();
        var helperRva = BinaryPrimitives.ReadInt32LittleEndian(shortBasic.AsSpan(image + pdata, 4));
        BinaryPrimitives.WriteInt32LittleEndian(shortBasic.AsSpan(image + pdata + 4, 4), helperRva + 6);
        Rehash(shortBasic);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(shortBasic));
        var shortSaved = (byte[])original.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(shortSaved.AsSpan(image + pdata + (4 * 12) + 4, 4), sourceRva + 10);
        Rehash(shortSaved);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(shortSaved));
        var helperExport = (byte[])original.Clone();
        var directory = image + edata;
        var exportTableRva = BinaryPrimitives.ReadInt32LittleEndian(helperExport.AsSpan(directory + 28, 4));
        BinaryPrimitives.WriteInt32LittleEndian(helperExport.AsSpan(
            directory + exportTableRva - edataRva, 4), helperRva);
        Rehash(helperExport);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Inspect(helperExport));

        static void Rehash(byte[] artifact)
        {
            SHA256.HashData(artifact.AsSpan(0, artifact.Length - 32)).CopyTo(artifact, artifact.Length - 32);
        }
    }

    [Fact]
    public async Task TimeoutAndCancellationTerminateOnlyOwnedChildAsync()
    {
        var identity = "Timeout" + Guid.NewGuid().ToString("N");
        var provider = Producer(identity: identity);
        var source = Consumer.Replace("return Recurse(3);", "while (true) { } return 0;", StringComparison.Ordinal);
        var consumer = ConsumerPackage(provider, source);
        var before = Directory.GetDirectories(Path.GetTempPath(), "terrum-native-" + identity + "-*")
            .Order(StringComparer.Ordinal).ToArray();
        _ = await Assert.ThrowsAsync<TimeoutException>(() => NativeModuleCompiler.RunAsync(consumer, provider,
            TimeSpan.FromMilliseconds(200))).ConfigureAwait(true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NativeModuleCompiler.RunAsync(consumer, provider,
            TimeSpan.FromSeconds(10), cancellation.Token)).ConfigureAwait(true);
        var after = Directory.GetDirectories(Path.GetTempPath(), "terrum-native-" + identity + "-*")
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task CleanupWaitsForTransientImageSharingWithoutHidingTimeoutAsync()
    {
        var identity = "Sharing" + Guid.NewGuid().ToString("N");
        var provider = Producer(identity: identity);
        var consumer = ConsumerPackage(provider, Consumer.Replace("return Recurse(3);",
            "while (true) { } return 0;", StringComparison.Ordinal));
        var execution = NativeModuleCompiler.RunAsync(consumer, provider, TimeSpan.FromMilliseconds(800));
        var heldLock = false;
        var elapsed = Stopwatch.StartNew();
        while (!heldLock && elapsed.Elapsed < TimeSpan.FromSeconds(2) && !execution.IsCompleted)
        {
            foreach (var directory in Directory.GetDirectories(Path.GetTempPath(), "terrum-native-" + identity + "-*"))
            {
                try
                {
                    await using var imageLock = File.Open(Path.Combine(directory, "consumer.exe"), FileMode.Open, FileAccess.Read, FileShare.Read);
                    heldLock = true;
                    await Task.Delay(1000).ConfigureAwait(true);
                }
                catch (IOException)
                {
                    // Creation is asynchronous and initially holds an exclusive write handle.
                }
            }
            if (!heldLock)
            {
                await Task.Delay(10).ConfigureAwait(true);
            }
        }
        _ = await Assert.ThrowsAsync<TimeoutException>(() => execution).ConfigureAwait(true);
        Assert.True(heldLock);
        Assert.Empty(Directory.GetDirectories(Path.GetTempPath(), "terrum-native-" + identity + "-*"));
    }
}
