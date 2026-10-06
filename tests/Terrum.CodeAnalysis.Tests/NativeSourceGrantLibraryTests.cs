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
    private const string SourceCodeUnitExport = "Terrum.CodeAnalysis.GetSourceCodeUnitOrMinusOne";
    private const string SourceOffsetExport = "Terrum.CodeAnalysis.GetSourceOffsetOrMinusOne";
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

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void SourceCodeUnitPreservesAllUtf16UnitsAndSentinelBounds(BoundRuntimePolicy policy)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var sourceDirectory = Path.Combine(RepositoryRoot(), "self-host");
        var sourceCodeUnitPath = Path.Combine(sourceDirectory, "SourceCodeUnit.terrum");
        var compilation = Compilation.CreateLibrary([
            SyntaxTree.Parse(File.ReadAllText(sourceCodeUnitPath), sourceCodeUnitPath),
        ], [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        }], (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            Assert.IsType<BoundProgram>(compilation.Program), [SourceCodeUnitExport], (NativeRuntimePolicy)policy);
        Assert.True(Assert.Single(artifact.Exports).SourceDependent);

        var path = Path.Combine(Path.GetTempPath(), $"terrum-source-grant-code-unit-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, artifact.Image);
        try
        {
            var module = NativeLibrary.Load(path);
            try
            {
                var binding = new NativeSourceGrantTestBindings(module,
                    [.. artifact.Exports.Select(static export => (export.Name, export.SourceDependent,
                        export.ReturnType, export.ParameterTypes))]);
                var singleCodeUnit = new char[1];
                for (var codeUnit = 0; codeUnit <= char.MaxValue; codeUnit++)
                {
                    singleCodeUnit[0] = (char)codeUnit;
                    Assert.Equal((int)singleCodeUnit[0],
                        binding.Invoke<int>(SourceCodeUnitExport, singleCodeUnit, 0));
                }

                foreach (var sample in LineCountSamples)
                {
                    var source = sample.ToCharArray();
                    var hosted = SourceText.From(sample);
                    for (var position = 0; position < hosted.Length; position++)
                    {
                        Assert.Equal((int)hosted[position],
                            binding.Invoke<int>(SourceCodeUnitExport, source, position));
                    }

                    Assert.Equal(-1, binding.Invoke<int>(SourceCodeUnitExport, source, int.MinValue));
                    Assert.Equal(-1, binding.Invoke<int>(SourceCodeUnitExport, source, -1));
                    Assert.Equal(-1, binding.Invoke<int>(SourceCodeUnitExport, source, source.Length));
                    Assert.Equal(-1, binding.Invoke<int>(SourceCodeUnitExport, source, int.MaxValue));
                }

                var maximumSource = new char[16_384];
                maximumSource[0] = '\uD800';
                maximumSource[^1] = char.MaxValue;
                var maximumHosted = SourceText.From(new string(maximumSource));
                Assert.Equal((int)maximumHosted[0],
                    binding.Invoke<int>(SourceCodeUnitExport, maximumSource, 0));
                Assert.Equal((int)maximumHosted[^1],
                    binding.Invoke<int>(SourceCodeUnitExport, maximumSource, maximumSource.Length - 1));
                Assert.Equal(-1, binding.Invoke<int>(SourceCodeUnitExport, maximumSource, int.MinValue));
                Assert.Equal(-1, binding.Invoke<int>(SourceCodeUnitExport, maximumSource, -1));
                Assert.Equal(-1, binding.Invoke<int>(SourceCodeUnitExport, maximumSource, maximumSource.Length));
                Assert.Equal(-1, binding.Invoke<int>(SourceCodeUnitExport, maximumSource, int.MaxValue));
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
    public void SourceOffsetMatchesHostedTryGetOffset(BoundRuntimePolicy policy)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var sourceDirectory = Path.Combine(RepositoryRoot(), "self-host");
        var characterFactsPath = Path.Combine(sourceDirectory, "CharacterFacts.terrum");
        var sourceOffsetPath = Path.Combine(sourceDirectory, "SourceOffset.terrum");
        var compilation = Compilation.CreateLibrary([
            SyntaxTree.Parse(File.ReadAllText(characterFactsPath), characterFactsPath),
            SyntaxTree.Parse(File.ReadAllText(sourceOffsetPath), sourceOffsetPath),
        ], [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        }], (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            Assert.IsType<BoundProgram>(compilation.Program), [SourceOffsetExport], (NativeRuntimePolicy)policy);
        Assert.True(Assert.Single(artifact.Exports).SourceDependent);

        var path = Path.Combine(Path.GetTempPath(), $"terrum-source-grant-offset-{Guid.NewGuid():N}.dll");
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
                    AssertSourceOffsetParity(binding, sample);
                }

                AssertMaximumSourceOffsetCases(binding);
                _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    binding.Invoke<int>(SourceOffsetExport, new char[16_385], 0, 0));
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
        Assert.Equal([DecodeScalarExport, SourceCodeUnitExport, LineCountExport, SourceOffsetExport,
            SourceLengthExport, ReadScalarExport],
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
                var source = sample.ToCharArray();
                var hosted = SourceText.From(sample);
                AssertSourceOffsetParity(bindings, sample);
                Assert.Equal(SourceText.From(sample).Length,
                    bindings.Invoke<int>(SourceLengthExport, source));
                for (var position = 0; position < hosted.Length; position++)
                {
                    Assert.Equal((int)hosted[position],
                        bindings.Invoke<int>(SourceCodeUnitExport, source, position));
                }

                Assert.Equal(-1, bindings.Invoke<int>(SourceCodeUnitExport, source, int.MinValue));
                Assert.Equal(-1, bindings.Invoke<int>(SourceCodeUnitExport, source, -1));
                Assert.Equal(-1, bindings.Invoke<int>(SourceCodeUnitExport, source, source.Length));
                Assert.Equal(-1, bindings.Invoke<int>(SourceCodeUnitExport, source, int.MaxValue));
            }
            var maximumSourceLength = new string('\0', 16_384);
            Assert.Equal(SourceText.From(maximumSourceLength).Length,
                bindings.Invoke<int>(SourceLengthExport, maximumSourceLength.ToCharArray()));
            var maximumCodeUnitSource = new char[16_384];
            maximumCodeUnitSource[0] = '\uD800';
            maximumCodeUnitSource[^1] = char.MaxValue;
            var maximumCodeUnitText = SourceText.From(new string(maximumCodeUnitSource));
            Assert.Equal((int)maximumCodeUnitText[0],
                bindings.Invoke<int>(SourceCodeUnitExport, maximumCodeUnitSource, 0));
            Assert.Equal((int)maximumCodeUnitText[^1],
                bindings.Invoke<int>(SourceCodeUnitExport, maximumCodeUnitSource, maximumCodeUnitSource.Length - 1));
            Assert.Equal(-1, bindings.Invoke<int>(SourceCodeUnitExport, maximumCodeUnitSource, int.MinValue));
            Assert.Equal(-1, bindings.Invoke<int>(SourceCodeUnitExport, maximumCodeUnitSource, -1));
            Assert.Equal(-1, bindings.Invoke<int>(SourceCodeUnitExport, maximumCodeUnitSource,
                maximumCodeUnitSource.Length));
            Assert.Equal(-1, bindings.Invoke<int>(SourceCodeUnitExport, maximumCodeUnitSource, int.MaxValue));
            AssertMaximumSourceOffsetCases(bindings);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                bindings.Invoke<int>(ReadScalarExport, new char[16_385], 0, 1));
            var singleCodeUnit = new char[1];
            for (var codeUnit = 0; codeUnit <= char.MaxValue; codeUnit++)
            {
                singleCodeUnit[0] = (char)codeUnit;
                var expected = char.IsSurrogate(singleCodeUnit[0]) ? -1 : codeUnit;
                Assert.Equal(expected, bindings.Invoke<int>(ReadScalarExport, singleCodeUnit, 0, 1));
                Assert.Equal(codeUnit, bindings.Invoke<int>(SourceCodeUnitExport, singleCodeUnit, 0));
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

    private static void AssertSourceOffsetParity(NativeSourceGrantTestBindings bindings, string text)
    {
        var source = text.ToCharArray();
        var hosted = SourceText.From(text);
        for (var line = -1; line <= hosted.Lines.Length; line++)
        {
            var maximumCharacter = line >= 0 && line < hosted.Lines.Length
                ? hosted.Lines[line].SpanIncludingLineBreak.Length + 1
                : 1;
            for (var character = -1; character <= maximumCharacter; character++)
            {
                AssertSourceOffsetMatches(bindings, source, hosted, line, character);
            }
        }

        AssertSourceOffsetMatches(bindings, source, hosted, int.MinValue, 0);
        AssertSourceOffsetMatches(bindings, source, hosted, int.MaxValue, 0);
        AssertSourceOffsetMatches(bindings, source, hosted, 0, int.MinValue);
        AssertSourceOffsetMatches(bindings, source, hosted, 0, int.MaxValue);
    }

    private static void AssertMaximumSourceOffsetCases(NativeSourceGrantTestBindings bindings)
    {
        var maximumPlainSource = new string('x', 16_384);
        var maximumPlain = maximumPlainSource.ToCharArray();
        var maximumPlainText = SourceText.From(maximumPlainSource);
        foreach (var character in new[] { 0, 16_383, 16_384, 16_385 })
        {
            AssertSourceOffsetMatches(bindings, maximumPlain, maximumPlainText, 0, character);
        }
        AssertSourceOffsetMatches(bindings, maximumPlain, maximumPlainText, 1, 0);

        var maximumLineFeedSource = new string('\n', 16_384);
        var maximumLineFeed = maximumLineFeedSource.ToCharArray();
        var maximumLineFeedText = SourceText.From(maximumLineFeedSource);
        for (var line = 0; line < maximumLineFeedText.Lines.Length; line++)
        {
            AssertSourceOffsetMatches(bindings, maximumLineFeed, maximumLineFeedText, line, 0);
            if (line + 1 < maximumLineFeedText.Lines.Length)
            {
                AssertSourceOffsetMatches(bindings, maximumLineFeed, maximumLineFeedText, line, 1);
            }
        }

        var maximumCrlfSource = string.Concat(Enumerable.Repeat("\r\n", 8_192));
        var maximumCrlf = maximumCrlfSource.ToCharArray();
        var maximumCrlfText = SourceText.From(maximumCrlfSource);
        for (var line = 0; line < maximumCrlfText.Lines.Length; line++)
        {
            AssertSourceOffsetMatches(bindings, maximumCrlf, maximumCrlfText, line, 0);
            if (line + 1 < maximumCrlfText.Lines.Length)
            {
                AssertSourceOffsetMatches(bindings, maximumCrlf, maximumCrlfText, line, 1);
                AssertSourceOffsetMatches(bindings, maximumCrlf, maximumCrlfText, line, 2);
            }
        }

        var maximumTrailingCrSource = string.Concat(new string('x', 16_383), '\r');
        var maximumTrailingCr = maximumTrailingCrSource.ToCharArray();
        var maximumTrailingCrText = SourceText.From(maximumTrailingCrSource);
        AssertSourceOffsetMatches(bindings, maximumTrailingCr, maximumTrailingCrText, 0, 16_382);
        AssertSourceOffsetMatches(bindings, maximumTrailingCr, maximumTrailingCrText, 0, 16_383);
        AssertSourceOffsetMatches(bindings, maximumTrailingCr, maximumTrailingCrText, 0, 16_384);
        AssertSourceOffsetMatches(bindings, maximumTrailingCr, maximumTrailingCrText, 1, 0);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            bindings.Invoke<int>(SourceOffsetExport, new char[16_385], 0, 0));
    }

    private static void AssertSourceOffsetMatches(NativeSourceGrantTestBindings bindings, char[] source,
        SourceText hosted, int line, int character)
    {
        var expected = hosted.TryGetOffset(line, character, out var offset) ? offset : -1;
        Assert.Equal(expected, bindings.Invoke<int>(SourceOffsetExport, source, line, character));
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
