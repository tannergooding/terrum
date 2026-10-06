// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeSourceGrantLibraryTests
{
    private const string ReadScalarExport = "Terrum.CodeAnalysis.ReadUtf16ScalarChecked";
    private const string DecodeScalarExport = "Terrum.CodeAnalysis.DecodeUtf16Scalar";
    private const string SourceLengthExport = "Terrum.CodeAnalysis.GetSourceTextLength";
    private const string LineCountExport = "Terrum.CodeAnalysis.GetSourceLineCount";
    private static readonly string[] LineCountSamples = [
        "",
        "plain",
        "\r",
        "\n",
        "\r\n",
        "\r\r\n\n\r",
        "a\r\nb\rc\nd\u0085e\u2028f\u2029",
        "\0\uD800\uDC00\uDFFF",
        "\0\n",
        "\r\0\n",
        "\r\uD800\n",
        "\u000B\u000C\u001C\u001D\u001E\u001F\u00A0",
    ];

    [Fact]
    public void SourceDependencyTracksPrivateCalleeBodyChanges()
    {
        var sourceBuffer = new HostBuffer("Source", PrimitiveType.Char16, [], false)
        {
            IsInvocationScoped = true,
        };
        var sourceFree = Compilation.CreateLibrary(
            [SyntaxTree.Parse("""
                namespace Terrum.CodeAnalysis;
                int32 ReadValue(int32 at) { return at; }
                public int32 GetValue(int32 at) { return ReadValue(at); }
                """)],
            [sourceBuffer], BoundRuntimePolicy.FailFast);
        var sourceFreeProgram = Assert.IsType<BoundProgram>(sourceFree.Program);
        var sourceFreeArtifact = NativeEmitter.EmitLibraryWithSourceGrant(sourceFreeProgram,
            ["Terrum.CodeAnalysis.GetValue"], NativeRuntimePolicy.FailFast);
        Assert.False(Assert.Single(sourceFreeArtifact.Exports).SourceDependent);
        Assert.Equal([PrimitiveType.Int32],
            Assert.Single(sourceFreeArtifact.Exports).ParameterTypes.ToArray());

        var sourceDependent = Compilation.CreateLibrary(
            [SyntaxTree.Parse("""
                namespace Terrum.CodeAnalysis;
                int32 ReadValue(int32 at) { return int32(Host.Source[at]); }
                public int32 GetValue(int32 at) { return ReadValue(at); }
                """)],
            [sourceBuffer], BoundRuntimePolicy.FailFast);
        var sourceDependentProgram = Assert.IsType<BoundProgram>(sourceDependent.Program);
        var sourceDependentArtifact = NativeEmitter.EmitLibraryWithSourceGrant(sourceDependentProgram,
            ["Terrum.CodeAnalysis.GetValue"], NativeRuntimePolicy.FailFast);
        Assert.True(Assert.Single(sourceDependentArtifact.Exports).SourceDependent);
    }

    [Fact]
    public void RejectsUnsupportedSourceGrantCallAndExportShapes()
    {
        var sourceBuffer = new HostBuffer("Source", PrimitiveType.Char16, [], false)
        {
            IsInvocationScoped = true,
        };

        var sourceRangePath = Path.Combine(RepositoryRoot(), "self-host", "SourceRange.terrum");
        var rangeCompilation = Compilation.CreateLibrary(
            [SyntaxTree.Parse(File.ReadAllText(sourceRangePath), sourceRangePath)],
            [sourceBuffer], BoundRuntimePolicy.FailFast);
        var rangeProgram = Assert.IsType<BoundProgram>(rangeCompilation.Program);
        Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibraryWithSourceGrant(rangeProgram,
            ["Terrum.CodeAnalysis.SourceRange.TryCreate"], NativeRuntimePolicy.FailFast));

        var outputParameters = Compilation.CreateLibrary(
            [SyntaxTree.Parse("""
                namespace Terrum.CodeAnalysis;
                public bool TryReadSource(int32 at, out int32 value) {
                    if (at < Host.Source.Length) {
                        value = int32(Host.Source[at]);
                        return true;
                    }
                    value = 0;
                    return false;
                }
                public bool TryInitializeSource(int32 at, init ref<int32> value) {
                    if (at < Host.Source.Length) {
                        value = int32(Host.Source[at]);
                        return true;
                    }
                    return false;
                }
                """)],
            [sourceBuffer], BoundRuntimePolicy.FailFast);
        Assert.False(outputParameters.HasErrors,
            string.Join(Environment.NewLine, outputParameters.Diagnostics));
        var outputParameterProgram = Assert.IsType<BoundProgram>(outputParameters.Program);
        Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibraryWithSourceGrant(
            outputParameterProgram, ["Terrum.CodeAnalysis.TryReadSource"], NativeRuntimePolicy.FailFast));
        Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibraryWithSourceGrant(
            outputParameterProgram, ["Terrum.CodeAnalysis.TryInitializeSource"], NativeRuntimePolicy.FailFast));

        var managedCall = Compilation.CreateLibrary(
            [SyntaxTree.Parse("""
                namespace Terrum.CodeAnalysis;
                int32 Collect() { return ManagedHeap.Collect(); }
                public int32 ReadSourceAndCollect(int32 at) {
                    return int32(Host.Source[at]) + Collect();
                }
                """)],
            [sourceBuffer], BoundRuntimePolicy.FailFast);
        Assert.False(managedCall.HasErrors, string.Join(Environment.NewLine, managedCall.Diagnostics));
        var managedProgram = Assert.IsType<BoundProgram>(managedCall.Program);
        Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibraryWithSourceGrant(managedProgram,
            ["Terrum.CodeAnalysis.ReadSourceAndCollect"], NativeRuntimePolicy.FailFast));

        var unsupportedCallee = Compilation.CreateLibrary(
            [SyntaxTree.Parse("""
                namespace Terrum.CodeAnalysis;
                bool TryGetCount(out int32 count) {
                    count = 1;
                    return true;
                }
                public int32 ReadSourceAndCount(int32 at) {
                    if (TryGetCount(out var count)) {
                        return int32(Host.Source[at]) + count;
                    }
                    return 0;
                }
                """)],
            [sourceBuffer], BoundRuntimePolicy.FailFast);
        Assert.False(unsupportedCallee.HasErrors,
            string.Join(Environment.NewLine, unsupportedCallee.Diagnostics));
        var unsupportedCalleeProgram = Assert.IsType<BoundProgram>(unsupportedCallee.Program);
        Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibraryWithSourceGrant(
            unsupportedCalleeProgram, ["Terrum.CodeAnalysis.ReadSourceAndCount"], NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void EndRelativeSourceReadsUseTheInvocationLength(BoundRuntimePolicy policy)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var sourceBuffer = new HostBuffer("Source", PrimitiveType.Char16, [], false)
        {
            IsInvocationScoped = true,
        };
        var compilation = Compilation.CreateLibrary(
            [SyntaxTree.Parse("""
                namespace Terrum.CodeAnalysis;
                public int32 ReadFromSourceEnd(int32 offset) { return int32(Host.Source[^offset]); }
                """)],
            [sourceBuffer], policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(program,
            ["Terrum.CodeAnalysis.ReadFromSourceEnd"], (NativeRuntimePolicy)policy);
        Assert.True(Assert.Single(artifact.Exports).SourceDependent);

        var path = Path.Combine(Path.GetTempPath(), $"terrum-source-grant-index-from-end-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, artifact.Image);
        try
        {
            var module = NativeLibrary.Load(path);
            try
            {
                var bindings = new NativeSourceGrantTestBindings(module,
                    [.. artifact.Exports.Select(static export => (export.Name, export.SourceDependent,
                        export.ReturnType, export.ParameterTypes))]);
                var maximumSource = new string('x', 16_383) + "Z";
                Assert.Equal((int)'A', bindings.Invoke<int>("Terrum.CodeAnalysis.ReadFromSourceEnd", ['A'], 1));
                Assert.Equal((int)'C', bindings.Invoke<int>("Terrum.CodeAnalysis.ReadFromSourceEnd", ['A', 'B', 'C'], 1));
                Assert.Equal((int)'A', bindings.Invoke<int>("Terrum.CodeAnalysis.ReadFromSourceEnd", ['A', 'B', 'C'], 3));
                Assert.Equal((int)'Z', bindings.Invoke<int>("Terrum.CodeAnalysis.ReadFromSourceEnd",
                    maximumSource.ToCharArray(), 1));
                Assert.Equal((int)'x', bindings.Invoke<int>("Terrum.CodeAnalysis.ReadFromSourceEnd",
                    maximumSource.ToCharArray(), 16_384));
                if (policy == BoundRuntimePolicy.CheckedExceptions)
                {
                    Assert.Throws<TargetInvocationException>(() =>
                        bindings.Invoke<int>("Terrum.CodeAnalysis.ReadFromSourceEnd", ['A'], 0));
                    Assert.Throws<TargetInvocationException>(() =>
                        bindings.Invoke<int>("Terrum.CodeAnalysis.ReadFromSourceEnd", ['A'], 2));
                    Assert.Throws<TargetInvocationException>(() =>
                        bindings.Invoke<int>("Terrum.CodeAnalysis.ReadFromSourceEnd", [], 1));
                }
            }
            finally
            {
                NativeLibrary.Free(module);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void SourceLineCountMatchesHostedTextLines(BoundRuntimePolicy policy)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var characterFactsPath = Path.Combine(RepositoryRoot(), "self-host", "CharacterFacts.terrum");
        var lineCountPath = Path.Combine(RepositoryRoot(), "self-host", "SourceLineCount.terrum");
        var compilation = Compilation.CreateLibrary([
            SyntaxTree.Parse(File.ReadAllText(characterFactsPath), characterFactsPath),
            SyntaxTree.Parse(File.ReadAllText(lineCountPath), lineCountPath),
        ], [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        }], (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            Assert.IsType<BoundProgram>(compilation.Program), [LineCountExport], (NativeRuntimePolicy)policy);
        Assert.True(Assert.Single(artifact.Exports).SourceDependent);

        var path = Path.Combine(Path.GetTempPath(), $"terrum-source-grant-line-count-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, artifact.Image);
        try
        {
            var module = NativeLibrary.Load(path);
            try
            {
                var binding = new NativeSourceGrantTestBindings(module,
                    [.. artifact.Exports.Select(static export => (export.Name, export.SourceDependent,
                        export.ReturnType, export.ParameterTypes))]);
                foreach (var sample in LineCountSamples)
                {
                    Assert.Equal(SourceText.From(sample).Lines.Length,
                        binding.Invoke<int>(LineCountExport, sample.ToCharArray()));
                }

                var maximumSource = new string('\n', 16_384);
                Assert.Equal(SourceText.From(maximumSource).Lines.Length,
                    binding.Invoke<int>(LineCountExport, maximumSource.ToCharArray()));
            }
            finally
            {
                NativeLibrary.Free(module);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void SourceTextLengthMatchesHostedTextLength(BoundRuntimePolicy policy)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var characterFactsPath = Path.Combine(RepositoryRoot(), "self-host", "CharacterFacts.terrum");
        var sourceLengthPath = Path.Combine(RepositoryRoot(), "self-host", "SourceTextLength.terrum");
        var compilation = Compilation.CreateLibrary([
            SyntaxTree.Parse(File.ReadAllText(sourceLengthPath), sourceLengthPath),
        ], [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        }], (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            Assert.IsType<BoundProgram>(compilation.Program), [SourceLengthExport], (NativeRuntimePolicy)policy);
        Assert.True(Assert.Single(artifact.Exports).SourceDependent);

        var path = Path.Combine(Path.GetTempPath(), $"terrum-source-grant-length-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, artifact.Image);
        try
        {
            var module = NativeLibrary.Load(path);
            try
            {
                var binding = new NativeSourceGrantTestBindings(module,
                    [.. artifact.Exports.Select(static export => (export.Name, export.SourceDependent,
                        export.ReturnType, export.ParameterTypes))]);
                foreach (var sample in LineCountSamples)
                {
                    Assert.Equal(SourceText.From(sample).Length,
                        binding.Invoke<int>(SourceLengthExport, sample.ToCharArray()));
                }

                var maximumSource = new string('\0', 16_384);
                Assert.Equal(SourceText.From(maximumSource).Length,
                    binding.Invoke<int>(SourceLengthExport, maximumSource.ToCharArray()));
            }
            finally
            {
                NativeLibrary.Free(module);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MaintainedCompilerPrimitivesCompanionUsesItsGeneratedManifest()
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("Missing configuration directory.");
        var artifactDirectory = Path.Combine(RepositoryRoot(), "artifacts", "bin", "self-host", configuration);
        var imagePath = Path.Combine(artifactDirectory, "terrum-compiler-primitives-source-grant.dll");
        var manifestPath = Path.Combine(artifactDirectory, "terrum-compiler-primitives-source-grant.abi.json");
        Assert.True(File.Exists(imagePath), $"Source-grant compiler-primitives companion was not built: {imagePath}");
        Assert.True(File.Exists(manifestPath), $"Source-grant compiler-primitives manifest was not built: {manifestPath}");

        var image = File.ReadAllBytes(imagePath);
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var root = manifest.RootElement;
        Assert.Equal(1, root.GetProperty("sourceGrantAbiVersion").GetInt32());
        Assert.Equal(16_384, root.GetProperty("maximumSourceCodeUnits").GetInt32());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(image)), root.GetProperty("imageSha256").GetString());
        var exports = root.GetProperty("exports").EnumerateArray()
            .Select(static export => (
                Name: export.GetProperty("name").GetString()
                    ?? throw new InvalidDataException("Missing export name."),
                SourceDependent: export.GetProperty("sourceDependent").GetBoolean(),
                ReturnType: Enum.Parse<PrimitiveType>(export.GetProperty("returnType").GetString()
                    ?? throw new InvalidDataException("Missing return type.")),
                ParameterTypes: export.GetProperty("parameterTypes").EnumerateArray()
                    .Select(static type => Enum.Parse<PrimitiveType>(type.GetString()
                        ?? throw new InvalidDataException("Missing parameter type.")))
                    .ToImmutableArray()))
            .ToImmutableArray();
        Assert.Equal([DecodeScalarExport, LineCountExport, SourceLengthExport, ReadScalarExport],
            [.. exports.Select(static export => export.Name).OrderBy(static name => name, StringComparer.Ordinal)]);

        var module = NativeLibrary.Load(imagePath);
        try
        {
            var bindings = new NativeSourceGrantTestBindings(module, exports);
            Assert.Equal((int)'A', bindings.Invoke<int>(ReadScalarExport, ['A'], 0, 1));
            Assert.Equal(0x1F600, bindings.Invoke<int>(ReadScalarExport, ['\uD83D', '\uDE00'], 0, 2));
            Assert.Equal(-1, bindings.Invoke<int>(ReadScalarExport, ['\uD800', 'A'], 0, 1));
            Assert.Equal(-1, bindings.Invoke<int>(ReadScalarExport, ['\uD800', '\uDC00'], 0, 1));
            Assert.Equal((int)'Z', bindings.Invoke<int>(ReadScalarExport,
                [.. new string('x', 16_383).ToCharArray(), 'Z'], 16_383, 16_384));
            foreach (var sample in LineCountSamples)
            {
                Assert.Equal(SourceText.From(sample).Lines.Length,
                    bindings.Invoke<int>(LineCountExport, sample.ToCharArray()));
            }
            var maximumLineCountSource = new string('\n', 16_384);
            Assert.Equal(SourceText.From(maximumLineCountSource).Lines.Length,
                bindings.Invoke<int>(LineCountExport, maximumLineCountSource.ToCharArray()));
            foreach (var sample in LineCountSamples)
            {
                Assert.Equal(SourceText.From(sample).Length,
                    bindings.Invoke<int>(SourceLengthExport, sample.ToCharArray()));
            }
            var maximumSourceLength = new string('\0', 16_384);
            Assert.Equal(SourceText.From(maximumSourceLength).Length,
                bindings.Invoke<int>(SourceLengthExport, maximumSourceLength.ToCharArray()));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                bindings.Invoke<int>(ReadScalarExport, new char[16_385], 0, 1));
            var singleCodeUnit = new char[1];
            for (var codeUnit = 0; codeUnit <= char.MaxValue; codeUnit++)
            {
                singleCodeUnit[0] = (char)codeUnit;
                var expected = char.IsSurrogate(singleCodeUnit[0]) ? -1 : codeUnit;
                Assert.Equal(expected, bindings.Invoke<int>(ReadScalarExport, singleCodeUnit, 0, 1));
            }
            Assert.Equal(0x1F600, bindings.Invoke<int>(DecodeScalarExport, null,
                (ushort)'\uD83D', (ushort)'\uDE00'));
            _ = Parallel.For(0, 32, index => {
                var value = (char)('a' + index);
                Assert.Equal((int)value, bindings.Invoke<int>(ReadScalarExport, [value], 0, 1));
            });
        }
        finally
        {
            NativeLibrary.Free(module);
        }
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void EmitsTransitiveScalarSourceGrantWithSourceFreeExports(BoundRuntimePolicy policy)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var sourceDirectory = Path.Combine(RepositoryRoot(), "self-host");
        var trees = new[] { "Utf16CodeUnits.terrum", "Utf16Scalar.terrum" }
            .Select(file => SyntaxTree.Parse(File.ReadAllText(Path.Combine(sourceDirectory, file)), file))
            .ToImmutableArray();
        var sourceBuffer = new HostBuffer("Source", PrimitiveType.Char16, [], false)
        {
            IsInvocationScoped = true,
        };
        var compilation = Compilation.CreateLibrary(trees, [sourceBuffer], policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(program,
            [DecodeScalarExport, ReadScalarExport], (NativeRuntimePolicy)policy);

        var readDescriptor = Assert.Single(artifact.Exports,
            static export => export.Name == ReadScalarExport);
        Assert.True(readDescriptor.SourceDependent);
        Assert.Equal(PrimitiveType.Int32, readDescriptor.ReturnType);
        Assert.Equal([PrimitiveType.Int32, PrimitiveType.Int32], readDescriptor.ParameterTypes.ToArray());
        var decodeDescriptor = Assert.Single(artifact.Exports,
            static export => export.Name == DecodeScalarExport);
        Assert.False(decodeDescriptor.SourceDependent);
        Assert.Equal(PrimitiveType.Int32, decodeDescriptor.ReturnType);
        Assert.Equal([PrimitiveType.Char16, PrimitiveType.Char16], decodeDescriptor.ParameterTypes.ToArray());

        var path = Path.Combine(Path.GetTempPath(), $"terrum-source-grant-library-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, artifact.Image);
        try
        {
            var module = NativeLibrary.Load(path);
            try
            {
                var bindings = new NativeSourceGrantTestBindings(module,
                    [.. artifact.Exports.Select(static export => (export.Name, export.SourceDependent,
                        export.ReturnType, export.ParameterTypes))]);
                Assert.Equal((int)'A', bindings.Invoke<int>(ReadScalarExport, ['A'], 0, 1));
                Assert.Equal(0x1F600, bindings.Invoke<int>(ReadScalarExport, ['\uD83D', '\uDE00'], 0, 2));
                Assert.Equal(-1, bindings.Invoke<int>(ReadScalarExport, ['\uD800', 'A'], 0, 1));
                Assert.Equal(-1, bindings.Invoke<int>(ReadScalarExport, ['\uD800', '\uDC00'], 0, 1));
                Assert.Equal((int)'D', bindings.Invoke<int>(ReadScalarExport, ['D'], 0, 1));
                Assert.Equal((int)'Z', bindings.Invoke<int>(ReadScalarExport,
                    [.. new string('x', 16_383).ToCharArray(), 'Z'], 16_383, 16_384));
                _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    bindings.Invoke<int>(ReadScalarExport, new char[16_385], 0, 1));
                Assert.Equal(0x1F600, bindings.Invoke<int>(DecodeScalarExport, null,
                    (ushort)'\uD83D', (ushort)'\uDE00'));
                if (policy == BoundRuntimePolicy.CheckedExceptions)
                {
                    var exception = Assert.Throws<TargetInvocationException>(() =>
                        bindings.Invoke<int>(ReadScalarExport, null, 0, 0));
                    _ = Assert.IsType<SEHException>(exception.InnerException);
                }

                _ = Parallel.For(0, 32, index => {
                    var value = (char)('A' + index);
                    Assert.Equal((int)value,
                        bindings.Invoke<int>(ReadScalarExport, [value], 0, 1));
                });
            }
            finally
            {
                NativeLibrary.Free(module);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docs", "roadmap.md")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("Could not locate the Terrum repository root.");
    }
}
