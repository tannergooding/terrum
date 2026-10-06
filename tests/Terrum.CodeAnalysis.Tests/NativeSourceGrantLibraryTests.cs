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
    private const string SourceCharacterPositionExport = "Terrum.CodeAnalysis.GetSourceCharacterPositionOrMinusOne";
    private const string SourceCodeUnitExport = "Terrum.CodeAnalysis.GetSourceCodeUnitOrMinusOne";
    private const string SourceLineNumberExport = "Terrum.CodeAnalysis.GetSourceLineNumberOrMinusOne";
    private const string SourceLineContentEndExport = "Terrum.CodeAnalysis.GetSourceLineContentEndOrMinusOne";
    private const string SourceLineEndExport = "Terrum.CodeAnalysis.GetSourceLineEndOrMinusOne";
    private const string SourceOffsetExport = "Terrum.CodeAnalysis.GetSourceOffsetOrMinusOne";
    private const string SourceLengthExport = "Terrum.CodeAnalysis.GetSourceTextLength";
    private const string CanGetSourceRangeExport = "Terrum.CodeAnalysis.CanGetSourceRange";
    private const string AnalyzeStringEscapePackedExport = "Terrum.CodeAnalysis.AnalyzeStringEscapePacked";
    private const string LineCountExport = "Terrum.CodeAnalysis.GetSourceLineCount";
    private const string CanParseUInt64Export = "Terrum.CodeAnalysis.Numerics64.CanParseUInt64Base";
    private const string ParseUInt64Export = "Terrum.CodeAnalysis.Numerics64.ParseUInt64Base";
    private const string CanParseInt64Export = "Terrum.CodeAnalysis.Numerics64.CanParseInt64Base";
    private const string ParseInt64Export = "Terrum.CodeAnalysis.Numerics64.ParseInt64Base";
    private const string CanParseUInt32Export = "Terrum.CodeAnalysis.Numerics64.CanParseUInt32Base";
    private const string ParseUInt32Export = "Terrum.CodeAnalysis.Numerics64.ParseUInt32Base";
    private const string CanParseInt32Export = "Terrum.CodeAnalysis.Numerics64.CanParseInt32Base";
    private const string ParseInt32Export = "Terrum.CodeAnalysis.Numerics64.ParseInt32Base";
    private const string CanParseChar16Export = "Terrum.CodeAnalysis.Numerics64.CanParseChar16Base";
    private const string ParseChar16Export = "Terrum.CodeAnalysis.Numerics64.ParseChar16Base";
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
    public void SourceRangeValidityMatchesHostedGetSubText(BoundRuntimePolicy policy)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var sourceRangeValidationPath = Path.Combine(RepositoryRoot(), "self-host", "SourceRangeValidation.terrum");
        var compilation = Compilation.CreateLibrary([
            SyntaxTree.Parse(File.ReadAllText(sourceRangeValidationPath), sourceRangeValidationPath),
        ], [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        }], (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            Assert.IsType<BoundProgram>(compilation.Program), [CanGetSourceRangeExport],
            (NativeRuntimePolicy)policy);
        var export = Assert.Single(artifact.Exports);
        Assert.True(export.SourceDependent);
        Assert.Equal(PrimitiveType.Bool, export.ReturnType);
        Assert.Equal([PrimitiveType.Int32, PrimitiveType.Int32], export.ParameterTypes.ToArray());

        var path = Path.Combine(Path.GetTempPath(), $"terrum-source-grant-range-validity-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, artifact.Image);
        try
        {
            var module = NativeLibrary.Load(path);
            try
            {
                var bindings = new NativeSourceGrantTestBindings(module,
                    [.. artifact.Exports.Select(static export => (export.Name, export.SourceDependent,
                        export.ReturnType, export.ParameterTypes))]);
                foreach (var sample in LineCountSamples.Append(new string('x', 16_384)))
                {
                    var source = sample.ToCharArray();
                    var hosted = SourceText.From(sample);
                    AssertSourceRangeValidityMatches(bindings, source, hosted);
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
    public void PackedStringEscapeMatchesHostedEscapeContract(BoundRuntimePolicy policy)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var sourceDirectory = Path.Combine(RepositoryRoot(), "self-host");
        var componentNames = new[] {
            "CharacterFacts", "Utf16CodeUnits", "Utf16Scalar", "StringEscapePacked",
        };
        var compilation = Compilation.CreateLibrary(
            [.. componentNames.Select(name =>
            {
                var path = Path.Combine(sourceDirectory, name + ".terrum");
                return SyntaxTree.Parse(File.ReadAllText(path), path);
            })],
            [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
                IsInvocationScoped = true,
            }],
            (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            Assert.IsType<BoundProgram>(compilation.Program),
            [AnalyzeStringEscapePackedExport], (NativeRuntimePolicy)policy);
        var export = Assert.Single(artifact.Exports);
        Assert.True(export.SourceDependent);
        Assert.Equal(PrimitiveType.UInt32, export.ReturnType);
        Assert.Equal([PrimitiveType.Int32, PrimitiveType.Int32], export.ParameterTypes.ToArray());

        var path = Path.Combine(Path.GetTempPath(), $"terrum-source-grant-string-escape-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, artifact.Image);
        try
        {
            var module = NativeLibrary.Load(path);
            try
            {
                var bindings = new NativeSourceGrantTestBindings(module,
                    [.. artifact.Exports.Select(static item => (item.Name, item.SourceDependent,
                        item.ReturnType, item.ParameterTypes))]);
                (string Source, int Start, int Length, int Scalar, int Consumed, bool HasError)[] cases = [
                    ("\\", 0, 1, 0, 1, true),
                    ("\\n", 0, 2, 10, 2, false),
                    ("\\r", 0, 2, 13, 2, false),
                    ("\\t", 0, 2, 9, 2, false),
                    ("\\\\", 0, 2, 92, 2, false),
                    ("\\\"", 0, 2, 34, 2, false),
                    ("\\0", 0, 2, 0, 2, false),
                    ("\\u00E9suffix", 0, 12, 233, 6, false),
                    ("\\U0001F600!", 0, 11, 0x1F600, 10, false),
                    ("\\U0010FFFF", 0, 10, 0x10FFFF, 10, false),
                    ("\\q", 0, 2, 0, 2, true),
                    ("\\u", 0, 2, 0, 2, true),
                    ("\\u123", 0, 5, 0, 5, true),
                    ("\\u12xz", 0, 6, 0, 4, true),
                    ("\\uD800", 0, 6, 0, 6, true),
                    ("\\U00110000", 0, 10, 0, 10, true),
                    ("\\\uD800A", 0, 3, -1, 2, true),
                    ("\\\uD83D\uDE00", 0, 3, 0, 3, true),
                    ("\\\n", 0, 2, 0, 2, true),
                    ("plain", 0, 5, 0, 0, false),
                    ("\\n", -1, 2, 0, 0, false),
                    ("\\n", 0, -1, 0, 0, false),
                    ("\\n", 0, int.MaxValue, 0, 0, false),
                    ("\\n", int.MaxValue, 1, 0, 0, false),
                    ("\\n", 2, 1, 0, 0, false),
                    ("\\n", 0, 0, 0, 0, false),
                ];
                foreach (var item in cases)
                {
                    var canAnalyze = item.Start >= 0 && item.Length > 0
                        && item.Start <= item.Source.Length
                        && item.Length <= item.Source.Length - item.Start
                        && item.Source[item.Start] == '\\';
                    var expected = canAnalyze
                        ? PackStringEscapeResult(item.Scalar, item.Consumed, item.HasError)
                        : 0u;
                    Assert.Equal(expected, bindings.Invoke<uint>(AnalyzeStringEscapePackedExport,
                        item.Source.ToCharArray(), item.Start, item.Length));
                }

                var maximumSource = new string('x', 16_382) + "\\n";
                var maximum = maximumSource.ToCharArray();
                Assert.Equal(PackStringEscapeResult(10, 2, false),
                    bindings.Invoke<uint>(AnalyzeStringEscapePackedExport, maximum, 16_382, 2));
                var maximumTrailingEscape = new string('x', 16_383) + "\\";
                Assert.Equal(PackStringEscapeResult(0, 1, true),
                    bindings.Invoke<uint>(AnalyzeStringEscapePackedExport, maximumTrailingEscape.ToCharArray(),
                        16_383, 1));
                _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    bindings.Invoke<uint>(AnalyzeStringEscapePackedExport, new char[16_385], 0, 1));
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

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void SourcePositionComponentsMatchHostedGetLinePosition(BoundRuntimePolicy policy)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var sourceDirectory = Path.Combine(RepositoryRoot(), "self-host");
        var characterFactsPath = Path.Combine(sourceDirectory, "CharacterFacts.terrum");
        var sourceOffsetPath = Path.Combine(sourceDirectory, "SourceOffset.terrum");
        var sourcePositionPath = Path.Combine(sourceDirectory, "SourcePosition.terrum");
        var compilation = Compilation.CreateLibrary([
            SyntaxTree.Parse(File.ReadAllText(characterFactsPath), characterFactsPath),
            SyntaxTree.Parse(File.ReadAllText(sourceOffsetPath), sourceOffsetPath),
            SyntaxTree.Parse(File.ReadAllText(sourcePositionPath), sourcePositionPath),
        ], [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        }], (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            Assert.IsType<BoundProgram>(compilation.Program),
            [SourceLineNumberExport, SourceCharacterPositionExport, SourceOffsetExport],
            (NativeRuntimePolicy)policy);
        Assert.Equal(3, artifact.Exports.Length);
        Assert.All(artifact.Exports, static export =>
        {
            Assert.True(export.SourceDependent);
            Assert.Equal(PrimitiveType.Int32, export.ReturnType);
        });
        Assert.Equal([PrimitiveType.Int32], Assert.Single(artifact.Exports,
            static export => export.Name == SourceLineNumberExport).ParameterTypes.ToArray());
        Assert.Equal([PrimitiveType.Int32], Assert.Single(artifact.Exports,
            static export => export.Name == SourceCharacterPositionExport).ParameterTypes.ToArray());
        Assert.Equal([PrimitiveType.Int32, PrimitiveType.Int32], Assert.Single(artifact.Exports,
            static export => export.Name == SourceOffsetExport).ParameterTypes.ToArray());

        var path = Path.Combine(Path.GetTempPath(), $"terrum-source-grant-position-{Guid.NewGuid():N}.dll");
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
                    AssertSourcePositionParity(binding, sample);
                }

                AssertMaximumSourcePositionCases(binding);
                _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    binding.Invoke<int>(SourceLineNumberExport, new char[16_385], 0));
                _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    binding.Invoke<int>(SourceCharacterPositionExport, new char[16_385], 0));
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
    public void SourceLineExtentsMatchHostedContentAndIncludingBreakExtents(BoundRuntimePolicy policy)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var sourceDirectory = Path.Combine(RepositoryRoot(), "self-host");
        var characterFactsPath = Path.Combine(sourceDirectory, "CharacterFacts.terrum");
        var sourceOffsetPath = Path.Combine(sourceDirectory, "SourceOffset.terrum");
        var sourceLineEndPath = Path.Combine(sourceDirectory, "SourceLineEnd.terrum");
        var compilation = Compilation.CreateLibrary([
            SyntaxTree.Parse(File.ReadAllText(characterFactsPath), characterFactsPath),
            SyntaxTree.Parse(File.ReadAllText(sourceOffsetPath), sourceOffsetPath),
            SyntaxTree.Parse(File.ReadAllText(sourceLineEndPath), sourceLineEndPath),
        ], [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        }], (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            Assert.IsType<BoundProgram>(compilation.Program),
            [SourceOffsetExport, SourceLineContentEndExport, SourceLineEndExport],
            (NativeRuntimePolicy)policy);
        var contentEnd = Assert.Single(artifact.Exports,
            static export => export.Name == SourceLineContentEndExport);
        Assert.True(contentEnd.SourceDependent);
        Assert.Equal(PrimitiveType.Int32, contentEnd.ReturnType);
        Assert.Equal([PrimitiveType.Int32], contentEnd.ParameterTypes.ToArray());
        var lineEnd = Assert.Single(artifact.Exports,
            static export => export.Name == SourceLineEndExport);
        Assert.True(lineEnd.SourceDependent);
        Assert.Equal(PrimitiveType.Int32, lineEnd.ReturnType);
        Assert.Equal([PrimitiveType.Int32], lineEnd.ParameterTypes.ToArray());

        var path = Path.Combine(Path.GetTempPath(), $"terrum-source-grant-line-end-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, artifact.Image);
        try
        {
            var module = NativeLibrary.Load(path);
            try
            {
                var bindings = new NativeSourceGrantTestBindings(module,
                    [.. artifact.Exports.Select(static export => (export.Name, export.SourceDependent,
                        export.ReturnType, export.ParameterTypes))]);
                foreach (var sample in LineCountSamples)
                {
                    var source = sample.ToCharArray();
                    var hosted = SourceText.From(sample);
                    for (var line = 0; line <= hosted.Lines.Length; line++)
                    {
                        AssertSourceLineEndMatches(bindings, source, hosted, line, true);
                    }
                    AssertSourceLineEndMatches(bindings, source, hosted, int.MinValue);
                    AssertSourceLineEndMatches(bindings, source, hosted, int.MaxValue);
                }

                var singleCodeUnit = new char[1];
                for (var codeUnit = 0; codeUnit <= char.MaxValue; codeUnit++)
                {
                    singleCodeUnit[0] = (char)codeUnit;
                    var hosted = SourceText.From(new string(singleCodeUnit));
                    AssertSourceLineEndMatches(bindings, singleCodeUnit, hosted, 0);
                    AssertSourceLineEndMatches(bindings, singleCodeUnit, hosted, 1);
                }

                var sourceAfterCr = new char[2] { '\r', '\0' };
                for (var codeUnit = 0; codeUnit <= char.MaxValue; codeUnit++)
                {
                    sourceAfterCr[1] = (char)codeUnit;
                    var hosted = SourceText.From(new string(sourceAfterCr));
                    Assert.Equal(hosted.Lines[0].Span.End,
                        bindings.Invoke<int>(SourceLineContentEndExport, sourceAfterCr, 0));
                    Assert.Equal(hosted.Lines[0].SpanIncludingLineBreak.End,
                        bindings.Invoke<int>(SourceLineEndExport, sourceAfterCr, 0));
                }

                AssertSourceLineEndCases(bindings, new string('x', 16_384), 0, 1);
                AssertSourceLineEndCases(bindings, new string('\n', 16_384), 0, 8_192, 16_383, 16_384, 16_385);
                AssertSourceLineEndCases(bindings, new string('\r', 16_384), 0, 8_192, 16_383, 16_384, 16_385);
                AssertSourceLineEndCases(bindings, string.Concat(Enumerable.Repeat("\r\n", 8_192)),
                    0, 4_096, 8_191, 8_192, 8_193);
                AssertSourceLineEndCases(bindings, new string('x', 16_383) + '\r', 0, 1, 2);
                AssertSourceLineEndCases(bindings, new string('x', 16_382) + "\r\n", 0, 1, 2);
                _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    bindings.Invoke<int>(SourceLineEndExport, new char[16_385], 0));
                _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    bindings.Invoke<int>(SourceLineContentEndExport, new char[16_385], 0));
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
        Assert.Equal([AnalyzeStringEscapePackedExport, CanGetSourceRangeExport, DecodeScalarExport,
            SourceCharacterPositionExport,
            SourceCodeUnitExport,
            SourceLineContentEndExport, LineCountExport, SourceLineEndExport, SourceLineNumberExport,
            SourceOffsetExport, SourceLengthExport, CanParseChar16Export,
            CanParseInt32Export, CanParseInt64Export, CanParseUInt32Export, CanParseUInt64Export,
            ParseChar16Export, ParseInt32Export, ParseInt64Export, ParseUInt32Export, ParseUInt64Export,
            ReadScalarExport],
            [.. exports.Select(static export => export.Name).OrderBy(static name => name, StringComparer.Ordinal)]);
        var packedEscape = Assert.Single(exports,
            static export => export.Name == AnalyzeStringEscapePackedExport);
        Assert.True(packedEscape.SourceDependent);
        Assert.Equal(PrimitiveType.UInt32, packedEscape.ReturnType);
        Assert.Equal([PrimitiveType.Int32, PrimitiveType.Int32], packedEscape.ParameterTypes.ToArray());

        var module = NativeLibrary.Load(imagePath);
        try
        {
            var bindings = new NativeSourceGrantTestBindings(module, exports);
            Assert.Equal(PackStringEscapeResult(10, 2, false),
                bindings.Invoke<uint>(AnalyzeStringEscapePackedExport, "\\n".ToCharArray(), 0, 2));
            Assert.Equal(PackStringEscapeResult(-1, 2, true),
                bindings.Invoke<uint>(AnalyzeStringEscapePackedExport, "\\\uD800A".ToCharArray(), 0, 3));
            var parserSource = "!1__2?".ToCharArray();
            Assert.Equal((byte)1, bindings.Invoke<byte>(CanParseUInt64Export, parserSource, 1, 4, (uint)10));
            Assert.Equal(12UL, bindings.Invoke<ulong>(ParseUInt64Export, parserSource, 1, 4, (uint)10));
            Assert.Equal((byte)1, bindings.Invoke<byte>(CanParseInt64Export, parserSource, 1, 4, (uint)10));
            Assert.Equal(12L, bindings.Invoke<long>(ParseInt64Export, parserSource, 1, 4, (uint)10));
            Assert.Equal((byte)1, bindings.Invoke<byte>(CanParseUInt32Export, parserSource, 1, 4, (uint)10));
            Assert.Equal(12U, bindings.Invoke<uint>(ParseUInt32Export, parserSource, 1, 4, (uint)10));
            Assert.Equal((byte)1, bindings.Invoke<byte>(CanParseInt32Export, parserSource, 1, 4, (uint)10));
            Assert.Equal(12, bindings.Invoke<int>(ParseInt32Export, parserSource, 1, 4, (uint)10));
            Assert.Equal((byte)1, bindings.Invoke<byte>(CanParseChar16Export, parserSource, 1, 4, (uint)10));
            Assert.Equal((ushort)12, bindings.Invoke<ushort>(ParseChar16Export, parserSource, 1, 4, (uint)10));
            var negativeParserSource = "!-12?".ToCharArray();
            Assert.Equal(-12L, bindings.Invoke<long>(ParseInt64Export,
                negativeParserSource, 1, 3, (uint)10));
            Assert.Equal(-12, bindings.Invoke<int>(ParseInt32Export,
                negativeParserSource, 1, 3, (uint)10));
            Assert.Equal((byte)0, bindings.Invoke<byte>(CanParseUInt64Export, ['?'], 0, 1, (uint)10));
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
                AssertSourcePositionParity(bindings, sample);
                for (var line = 0; line < hosted.Lines.Length; line++)
                {
                    AssertSourceLineEndMatches(bindings, source, hosted, line, true);
                }
                AssertSourceRangeValidityMatches(bindings, source, hosted);
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
            AssertMaximumSourcePositionCases(bindings);
            AssertSourceLineEndCases(bindings, new string('\n', 16_384),
                0, 8_192, 16_383, 16_384);
            var maximumSource = new string('x', 16_384);
            AssertSourceRangeValidityMatches(bindings, maximumSource.ToCharArray(), SourceText.From(maximumSource));
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

    private static void AssertSourcePositionParity(NativeSourceGrantTestBindings bindings, string text)
    {
        var source = text.ToCharArray();
        var hosted = SourceText.From(text);
        for (var position = -1; position <= hosted.Length + 1; position++)
        {
            AssertSourcePositionMatches(bindings, source, hosted, position);
        }

        AssertSourcePositionMatches(bindings, source, hosted, int.MinValue);
        AssertSourcePositionMatches(bindings, source, hosted, int.MaxValue);
    }

    private static void AssertMaximumSourcePositionCases(NativeSourceGrantTestBindings bindings)
    {
        var maximumPlainSource = new string('x', 16_384);
        var maximumPlain = maximumPlainSource.ToCharArray();
        var maximumPlainText = SourceText.From(maximumPlainSource);
        foreach (var position in new[] { 0, 16_383, 16_384, 16_385 })
        {
            AssertSourcePositionMatches(bindings, maximumPlain, maximumPlainText, position);
        }

        var maximumLineFeedSource = new string('\n', 16_384);
        var maximumLineFeed = maximumLineFeedSource.ToCharArray();
        var maximumLineFeedText = SourceText.From(maximumLineFeedSource);
        foreach (var position in new[] { 0, 1, 8_192, 16_383, 16_384 })
        {
            AssertSourcePositionMatches(bindings, maximumLineFeed, maximumLineFeedText, position);
        }

        var maximumCrlfSource = string.Concat(Enumerable.Repeat("\r\n", 8_192));
        var maximumCrlf = maximumCrlfSource.ToCharArray();
        var maximumCrlfText = SourceText.From(maximumCrlfSource);
        foreach (var position in new[] { 0, 1, 2, 8_192, 16_382, 16_383, 16_384 })
        {
            AssertSourcePositionMatches(bindings, maximumCrlf, maximumCrlfText, position);
        }

        var maximumTrailingCrSource = string.Concat(new string('x', 16_383), '\r');
        var maximumTrailingCr = maximumTrailingCrSource.ToCharArray();
        var maximumTrailingCrText = SourceText.From(maximumTrailingCrSource);
        foreach (var position in new[] { 16_382, 16_383, 16_384 })
        {
            AssertSourcePositionMatches(bindings, maximumTrailingCr, maximumTrailingCrText, position);
        }
    }

    private static void AssertSourcePositionMatches(NativeSourceGrantTestBindings bindings, char[] source,
        SourceText hosted, int position)
    {
        var expectedLine = -1;
        var expectedCharacter = -1;
        if (position >= 0 && position <= hosted.Length)
        {
            var linePosition = hosted.GetLinePosition(position);
            expectedLine = linePosition.Line;
            expectedCharacter = linePosition.Character;
        }

        var actualLine = bindings.Invoke<int>(SourceLineNumberExport, source, position);
        var actualCharacter = bindings.Invoke<int>(SourceCharacterPositionExport, source, position);
        Assert.Equal(expectedLine, actualLine);
        Assert.Equal(expectedCharacter, actualCharacter);
        if (expectedLine >= 0 && expectedCharacter >= 0)
        {
            Assert.Equal(position,
                bindings.Invoke<int>(SourceOffsetExport, source, actualLine, actualCharacter));
        }
    }

    private static void AssertSourceLineEndCases(NativeSourceGrantTestBindings bindings,
        string sample, params int[] lines)
    {
        var source = sample.ToCharArray();
        var hosted = SourceText.From(sample);
        foreach (var line in lines)
        {
            AssertSourceLineEndMatches(bindings, source, hosted, line, true);
        }
    }

    private static void AssertSourceRangeValidityMatches(NativeSourceGrantTestBindings bindings,
        char[] source, SourceText hosted)
    {
        int[] boundaries = [
            int.MinValue,
            -1,
            0,
            1,
            hosted.Length / 2,
            hosted.Length - 1,
            hosted.Length,
            hosted.Length + 1,
            int.MaxValue,
        ];
        foreach (var start in boundaries)
        {
            foreach (var length in boundaries)
            {
                var expected = IsHostedSourceRangeValid(hosted, start, length);
                var actual = bindings.Invoke<byte>(CanGetSourceRangeExport, source, start, length);
                Assert.Equal(expected ? (byte)1 : (byte)0, actual);
            }
        }
    }

    private static bool IsHostedSourceRangeValid(SourceText hosted, int start, int length)
    {
        try
        {
            _ = hosted.GetSubText(new TextSpan(start, length));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static void AssertSourceLineEndMatches(NativeSourceGrantTestBindings bindings,
        char[] source, SourceText hosted, int line, bool verifyOffsetBoundaries = false)
    {
        if (line < 0 || line >= hosted.Lines.Length)
        {
            Assert.Equal(-1, bindings.Invoke<int>(SourceLineContentEndExport, source, line));
            Assert.Equal(-1, bindings.Invoke<int>(SourceLineEndExport, source, line));
            return;
        }

        var expectedContent = hosted.Lines[line].Span;
        var expectedIncludingBreak = hosted.Lines[line].SpanIncludingLineBreak;
        var start = bindings.Invoke<int>(SourceOffsetExport, source, line, 0);
        var contentEnd = bindings.Invoke<int>(SourceLineContentEndExport, source, line);
        var includingBreakEnd = bindings.Invoke<int>(SourceLineEndExport, source, line);
        Assert.Equal(expectedContent.Start, start);
        Assert.Equal(expectedContent.End, contentEnd);
        Assert.Equal(expectedIncludingBreak.Start, start);
        Assert.Equal(expectedIncludingBreak.End, includingBreakEnd);
        Assert.Equal(expectedContent, new TextSpan(start, contentEnd - start));
        Assert.Equal(expectedIncludingBreak, new TextSpan(start, includingBreakEnd - start));
        if (verifyOffsetBoundaries)
        {
            Assert.Equal(contentEnd,
                bindings.Invoke<int>(SourceOffsetExport, source, line, contentEnd - start));
            var includingBreakCharacter = includingBreakEnd - start;
            var expectedIncludingBreakOffset = line + 1 < hosted.Lines.Length ? -1 : includingBreakEnd;
            Assert.Equal(expectedIncludingBreakOffset,
                bindings.Invoke<int>(SourceOffsetExport, source, line, includingBreakCharacter));
        }
    }

    private static void AssertSourceOffsetMatches(NativeSourceGrantTestBindings bindings, char[] source,
        SourceText hosted, int line, int character)
    {
        var expected = hosted.TryGetOffset(line, character, out var offset) ? offset : -1;
        Assert.Equal(expected, bindings.Invoke<int>(SourceOffsetExport, source, line, character));
    }

    private static uint PackStringEscapeResult(int scalar, int consumedLength, bool hasError) =>
        ((uint)(scalar + 1) << 5) | ((uint)consumedLength << 1) | (hasError ? 1u : 0u);

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
