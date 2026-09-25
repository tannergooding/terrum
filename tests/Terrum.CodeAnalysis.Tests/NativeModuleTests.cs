// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            return value.First + value.Second + value.Third + (int32)(n / 2147483648)
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
            incompatibleSource = incompatibleSource.Replace("return value.First + value.Second + value.Third + (int32)(n / 2147483648)",
                "return (value.First + value.Second + value.Third + (int32)(n / 2147483648)",
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

    [Fact]
    public void MalformedPackagesAndMismatchedPeHeadersAreRejected()
    {
        var original = Producer();
        var consumer = ConsumerPackage(original);
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
        wrongContract[18 + wrongContract[7] + wrongContract[8]] ^= 1;
        Rehash(wrongContract);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Validate(consumer, wrongContract));
        var wrongImage = (byte[])original.Clone();
        var image = 18 + wrongImage[7] + wrongImage[8]
            + BinaryPrimitives.ReadInt32LittleEndian(wrongImage.AsSpan(10, 4));
        var pe = BinaryPrimitives.ReadInt32LittleEndian(wrongImage.AsSpan(image + 0x3C, 4));
        wrongImage[image + pe + 24 + 16] = 1; // DLL must have no entry point
        Rehash(wrongImage);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Validate(consumer, wrongImage));
        var wrongExport = (byte[])original.Clone();
        var exportName = wrongExport.AsSpan(image, BinaryPrimitives.ReadInt32LittleEndian(wrongExport.AsSpan(14, 4)))
            .IndexOf("Evaluate\0"u8);
        Assert.True(exportName >= 0);
        wrongExport[image + exportName] = (byte)'X';
        Rehash(wrongExport);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Validate(consumer, wrongExport));
        var wrongImport = (byte[])consumer.Clone();
        var importImage = 18 + wrongImport[7] + wrongImport[8]
            + BinaryPrimitives.ReadInt32LittleEndian(wrongImport.AsSpan(10, 4));
        var importName = wrongImport.AsSpan(importImage,
            BinaryPrimitives.ReadInt32LittleEndian(wrongImport.AsSpan(14, 4))).IndexOf("terrum-Example.dll\0"u8);
        Assert.True(importName >= 0);
        wrongImport[importImage + importName + 7] = (byte)'X';
        Rehash(wrongImport);
        _ = Assert.Throws<ModuleFormatException>(() => NativeModuleCompiler.Validate(wrongImport, original));

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
