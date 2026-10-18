// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Native.SourceGrants;

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
    private const string AnalyzeCharacterLiteralPackedExport = "Terrum.CodeAnalysis.AnalyzeCharacterLiteralPacked";
    private const string CharacterLiteralScalarExport = "Terrum.CodeAnalysis.GetCharacterLiteralScalarOrMinusOne";
    private const string StringLiteralScalarCountExport = "Terrum.CodeAnalysis.GetStringLiteralScalarCountOrMinusOne";
    private const string StringLiteralScalarAtExport = "Terrum.CodeAnalysis.GetStringLiteralScalarOrMinusOne";
    private const string StringLiteralEncodedLengthExport = "Terrum.CodeAnalysis.GetStringLiteralEncodedLengthOrMinusOne";
    private const string StringNameRowByteLengthExport = "Terrum.CodeAnalysis.GetStringNameRowByteLengthOrMinusOne";
    private const string StringNamespaceRowByteLengthExport = "Terrum.CodeAnalysis.GetStringNamespaceRowByteLengthOrMinusOne";
    private const string HexFloat32BitsExport = "Terrum.CodeAnalysis.GetHexFloat32LiteralBitsOrMinusOne";
    private const string HexFloat64BitsExport = "Terrum.CodeAnalysis.GetHexFloat64LiteralBitsOrMinusOne";
    private const string DecimalFloat32BitsExport = "Terrum.CodeAnalysis.GetDecimalFloat32LiteralBitsOrMinusOne";
    private const string DecimalFloat64BitsExport = "Terrum.CodeAnalysis.GetDecimalFloat64LiteralBitsOrMinusOne";
    private const string BasedIntegerFloat32BitsExport = "Terrum.CodeAnalysis.GetBasedIntegerFloat32BitsOrMinusOne";
    private const string BasedIntegerFloat64BitsExport = "Terrum.CodeAnalysis.GetBasedIntegerFloat64BitsOrMinusOne";
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

    [Test]
    public void SourceDependencyTracksPrivateCalleeBodyChanges()
    {
        var sourceBuffer = new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        };
        var sourceFree = Compilation.CreateLibrary(
            [SyntaxTree.Parse("""
                namespace Terrum.CodeAnalysis;
                int32 ReadValue(int32 at) { return at; }
                public int32 GetValue(int32 at) { return ReadValue(at); }
                """)],
            [sourceBuffer], BoundRuntimePolicy.FailFast);
        var sourceFreeProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = sourceFree.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var sourceFreeArtifact = NativeEmitter.EmitLibraryWithSourceGrant(sourceFreeProgram,
            ["Terrum.CodeAnalysis.GetValue"], NativeRuntimePolicy.FailFast);
        Assert.That(global::System.Linq.Enumerable.Single(sourceFreeArtifact.Exports).SourceDependent, Is.False);
        Assert.That(global::System.Linq.Enumerable.Single(sourceFreeArtifact.Exports).ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32]));

        var sourceDependent = Compilation.CreateLibrary(
            [SyntaxTree.Parse("""
                namespace Terrum.CodeAnalysis;
                int32 ReadValue(int32 at) { return int32(Host.Source[at]); }
                public int32 GetValue(int32 at) { return ReadValue(at); }
                """)],
            [sourceBuffer], BoundRuntimePolicy.FailFast);
        var sourceDependentProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = sourceDependent.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        var sourceDependentArtifact = NativeEmitter.EmitLibraryWithSourceGrant(sourceDependentProgram,
            ["Terrum.CodeAnalysis.GetValue"], NativeRuntimePolicy.FailFast);
        Assert.That(global::System.Linq.Enumerable.Single(sourceDependentArtifact.Exports).SourceDependent, Is.True);
    }

    [Test]
    public void SourceDependencyTracksCallerUnsafeExpressionCallees()
    {
        var sourceBuffer = new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        };
        var compilation = Compilation.CreateLibrary(
            [SyntaxTree.Parse("""
                namespace Terrum.CodeAnalysis;
                unsafe int32 ReadValue(int32 at) { return int32(Host.Source[at]); }
                public int32 GetValue(int32 at) { return unsafe(ReadValue(at)); }
                """)],
            [sourceBuffer], BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(program,
            ["Terrum.CodeAnalysis.GetValue"], NativeRuntimePolicy.FailFast);
        Assert.That(global::System.Linq.Enumerable.Single(artifact.Exports).SourceDependent, Is.True);
    }

    [Test]
    public void RejectsUnsupportedSourceGrantCallAndExportShapes()
    {
        var sourceBuffer = new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        };

        var sourceRangePath = TerrumProjectPaths.RepositoryFile(RepositoryRoot(), "sources/Terrum.CodeAnalysis.Terrum/Text/SourceRange.terrum");
        var rangeCompilation = Compilation.CreateLibrary(
            [SyntaxTree.Parse(File.ReadAllText(sourceRangePath), sourceRangePath)],
            [sourceBuffer], BoundRuntimePolicy.FailFast);
        var rangeProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = rangeCompilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
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
        Assert.That(outputParameters.HasErrors, Is.False, string.Join(Environment.NewLine, outputParameters.Diagnostics));
        var outputParameterProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = outputParameters.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
        Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibraryWithSourceGrant(
            outputParameterProgram, ["Terrum.CodeAnalysis.TryReadSource"], NativeRuntimePolicy.FailFast));
        Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibraryWithSourceGrant(
            outputParameterProgram, ["Terrum.CodeAnalysis.TryInitializeSource"], NativeRuntimePolicy.FailFast));

        var ownedExport = Compilation.CreateLibrary(
            [SyntaxTree.Parse("""
                namespace Terrum.CodeAnalysis;
                public OwnedInt32Buffer CreateOwnedSourceResult() {
                    require Host.Source.Length == 1;
                    require OwnedInt32Buffer.TryCreate(1, out var value);
                    return move value;
                }
                """)],
            [sourceBuffer], BoundRuntimePolicy.FailFast);
        Assert.That(ownedExport.HasErrors, Is.False, string.Join(Environment.NewLine, ownedExport.Diagnostics));
        var ownedExportProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = ownedExport.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))();
        Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibraryWithSourceGrant(
            ownedExportProgram, ["Terrum.CodeAnalysis.CreateOwnedSourceResult"], NativeRuntimePolicy.FailFast));

        var managedCall = Compilation.CreateLibrary(
            [SyntaxTree.Parse("""
                namespace Terrum.CodeAnalysis;
                int32 Collect() { return ManagedHeap.Collect(); }
                public int32 ReadSourceAndCollect(int32 at) {
                    return int32(Host.Source[at]) + Collect();
                }
                """)],
            [sourceBuffer], BoundRuntimePolicy.FailFast);
        Assert.That(managedCall.HasErrors, Is.False, string.Join(Environment.NewLine, managedCall.Diagnostics));
        var managedProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = managedCall.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))();
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
        Assert.That(unsupportedCallee.HasErrors, Is.False, string.Join(Environment.NewLine, unsupportedCallee.Diagnostics));
        var unsupportedCalleeProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = unsupportedCallee.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))();
        Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibraryWithSourceGrant(
            unsupportedCalleeProgram, ["Terrum.CodeAnalysis.ReadSourceAndCount"], NativeRuntimePolicy.FailFast));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void EndRelativeSourceReadsUseTheInvocationLength(BoundRuntimePolicy policy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var sourceBuffer = new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        };
        var compilation = Compilation.CreateLibrary(
            [SyntaxTree.Parse("""
                namespace Terrum.CodeAnalysis;
                public int32 ReadFromSourceEnd(int32 offset) { return int32(Host.Source[^offset]); }
                """)],
            [sourceBuffer], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = compilation.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))();
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(program,
            ["Terrum.CodeAnalysis.ReadFromSourceEnd"], (NativeRuntimePolicy)policy);
        Assert.That(global::System.Linq.Enumerable.Single(artifact.Exports).SourceDependent, Is.True);

        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-source-grant-index-from-end-{Guid.NewGuid():N}.dll");
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
                Assert.That(bindings.Invoke<int>("Terrum.CodeAnalysis.ReadFromSourceEnd", ['A'], 1), Is.EqualTo((int)'A'));
                Assert.That(bindings.Invoke<int>("Terrum.CodeAnalysis.ReadFromSourceEnd", ['A', 'B', 'C'], 1), Is.EqualTo((int)'C'));
                Assert.That(bindings.Invoke<int>("Terrum.CodeAnalysis.ReadFromSourceEnd", ['A', 'B', 'C'], 3), Is.EqualTo((int)'A'));
                Assert.That(bindings.Invoke<int>("Terrum.CodeAnalysis.ReadFromSourceEnd",
                    maximumSource.ToCharArray(), 1), Is.EqualTo((int)'Z'));
                Assert.That(bindings.Invoke<int>("Terrum.CodeAnalysis.ReadFromSourceEnd",
                    maximumSource.ToCharArray(), 16_384), Is.EqualTo((int)'x'));
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
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void SourceLineCountMatchesHostedTextLines(BoundRuntimePolicy policy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var characterFactsPath = TerrumProjectPaths.RepositoryFile(RepositoryRoot(), "sources/Terrum.CodeAnalysis.Terrum/Lexing/CharacterFacts.terrum");
        var lineCountPath = TerrumProjectPaths.RepositoryFile(RepositoryRoot(), "sources/Terrum.CodeAnalysis.Terrum/Text/SourceLineCount.terrum");
        var compilation = Compilation.CreateLibrary([
            SyntaxTree.Parse(File.ReadAllText(characterFactsPath), characterFactsPath),
            SyntaxTree.Parse(File.ReadAllText(lineCountPath), lineCountPath),
        ], [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        }], (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = compilation.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))(), [LineCountExport], (NativeRuntimePolicy)policy);
        Assert.That(global::System.Linq.Enumerable.Single(artifact.Exports).SourceDependent, Is.True);

        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-source-grant-line-count-{Guid.NewGuid():N}.dll");
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
                    Assert.That(binding.Invoke<int>(LineCountExport, sample.ToCharArray()), Is.EqualTo(SourceText.From(sample).Lines.Length));
                }

                var maximumSource = new string('\n', 16_384);
                Assert.That(binding.Invoke<int>(LineCountExport, maximumSource.ToCharArray()), Is.EqualTo(SourceText.From(maximumSource).Lines.Length));
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
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void SourceTextLengthMatchesHostedTextLength(BoundRuntimePolicy policy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var characterFactsPath = TerrumProjectPaths.RepositoryFile(RepositoryRoot(), "sources/Terrum.CodeAnalysis.Terrum/Lexing/CharacterFacts.terrum");
        var sourceLengthPath = TerrumProjectPaths.RepositoryFile(RepositoryRoot(), "sources/Terrum.CodeAnalysis.Terrum/Text/SourceTextLength.terrum");
        var compilation = Compilation.CreateLibrary([
            SyntaxTree.Parse(File.ReadAllText(sourceLengthPath), sourceLengthPath),
        ], [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        }], (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = compilation.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))(), [SourceLengthExport], (NativeRuntimePolicy)policy);
        Assert.That(global::System.Linq.Enumerable.Single(artifact.Exports).SourceDependent, Is.True);

        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-source-grant-length-{Guid.NewGuid():N}.dll");
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
                    Assert.That(binding.Invoke<int>(SourceLengthExport, sample.ToCharArray()), Is.EqualTo(SourceText.From(sample).Length));
                }

                var maximumSource = new string('\0', 16_384);
                Assert.That(binding.Invoke<int>(SourceLengthExport, maximumSource.ToCharArray()), Is.EqualTo(SourceText.From(maximumSource).Length));
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
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void SourceCodeUnitPreservesAllUtf16UnitsAndSentinelBounds(BoundRuntimePolicy policy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var sourceCodeUnitPath = TerrumProjectPaths.SourceFile(RepositoryRoot(),
            Path.Combine("Text", "SourceCodeUnit.terrum"));
        var compilation = Compilation.CreateLibrary([
            SyntaxTree.Parse(File.ReadAllText(sourceCodeUnitPath), sourceCodeUnitPath),
        ], [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        }], (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = compilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))(), [SourceCodeUnitExport], (NativeRuntimePolicy)policy);
        Assert.That(global::System.Linq.Enumerable.Single(artifact.Exports).SourceDependent, Is.True);

        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-source-grant-code-unit-{Guid.NewGuid():N}.dll");
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
                    Assert.That(binding.Invoke<int>(SourceCodeUnitExport, singleCodeUnit, 0), Is.EqualTo((int)singleCodeUnit[0]));
                }

                foreach (var sample in LineCountSamples)
                {
                    var source = sample.ToCharArray();
                    var hosted = SourceText.From(sample);
                    for (var position = 0; position < hosted.Length; position++)
                    {
                        Assert.That(binding.Invoke<int>(SourceCodeUnitExport, source, position), Is.EqualTo((int)hosted[position]));
                    }

                    Assert.That(binding.Invoke<int>(SourceCodeUnitExport, source, int.MinValue), Is.EqualTo(-1));
                    Assert.That(binding.Invoke<int>(SourceCodeUnitExport, source, -1), Is.EqualTo(-1));
                    Assert.That(binding.Invoke<int>(SourceCodeUnitExport, source, source.Length), Is.EqualTo(-1));
                    Assert.That(binding.Invoke<int>(SourceCodeUnitExport, source, int.MaxValue), Is.EqualTo(-1));
                }

                var maximumSource = new char[16_384];
                maximumSource[0] = '\uD800';
                maximumSource[^1] = char.MaxValue;
                var maximumHosted = SourceText.From(new string(maximumSource));
                Assert.That(binding.Invoke<int>(SourceCodeUnitExport, maximumSource, 0), Is.EqualTo((int)maximumHosted[0]));
                Assert.That(binding.Invoke<int>(SourceCodeUnitExport, maximumSource, maximumSource.Length - 1), Is.EqualTo((int)maximumHosted[^1]));
                Assert.That(binding.Invoke<int>(SourceCodeUnitExport, maximumSource, int.MinValue), Is.EqualTo(-1));
                Assert.That(binding.Invoke<int>(SourceCodeUnitExport, maximumSource, -1), Is.EqualTo(-1));
                Assert.That(binding.Invoke<int>(SourceCodeUnitExport, maximumSource, maximumSource.Length), Is.EqualTo(-1));
                Assert.That(binding.Invoke<int>(SourceCodeUnitExport, maximumSource, int.MaxValue), Is.EqualTo(-1));
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
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void SourceRangeValidityMatchesHostedGetSubText(BoundRuntimePolicy policy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var sourceRangeValidationPath = TerrumProjectPaths.RepositoryFile(RepositoryRoot(), "sources/Terrum.CodeAnalysis.Terrum/Text/SourceRangeValidation.terrum");
        var compilation = Compilation.CreateLibrary([
            SyntaxTree.Parse(File.ReadAllText(sourceRangeValidationPath), sourceRangeValidationPath),
        ], [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        }], (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual12 = compilation.Program; Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual12!; }))(), [CanGetSourceRangeExport],
            (NativeRuntimePolicy)policy);
        var export = global::System.Linq.Enumerable.Single(artifact.Exports);
        Assert.That(export.SourceDependent, Is.True);
        Assert.That(export.ReturnType, Is.EqualTo(PrimitiveType.Bool));
        Assert.That(export.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32]));

        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-source-grant-range-validity-{Guid.NewGuid():N}.dll");
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
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void PackedStringEscapeMatchesHostedEscapeContract(BoundRuntimePolicy policy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = RepositoryRoot();
        var componentPaths = new[] {
            Path.Combine("Lexing", "CharacterFacts.terrum"),
            Path.Combine("Lexing", "HexDigitValue.terrum"),
            Path.Combine("Text", "Utf16CodeUnits.terrum"),
            Path.Combine("Text", "Utf16Scalar.terrum"),
            Path.Combine("Lexing", "StringEscapePacked.terrum"),
        };
        var compilation = Compilation.CreateLibrary(
            [.. componentPaths.Select(relativePath =>
            {
                var path = TerrumProjectPaths.SourceFile(root, relativePath);
                return SyntaxTree.Parse(File.ReadAllText(path), path);
            })],
            [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
                IsInvocationScoped = true,
            }],
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual13 = compilation.Program; Assert.That(__nunitActual13, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual13!; }))(),
            [AnalyzeStringEscapePackedExport], (NativeRuntimePolicy)policy);
        var export = global::System.Linq.Enumerable.Single(artifact.Exports);
        Assert.That(export.SourceDependent, Is.True);
        Assert.That(export.ReturnType, Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(export.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32]));

        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-source-grant-string-escape-{Guid.NewGuid():N}.dll");
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
                    Assert.That(bindings.Invoke<uint>(AnalyzeStringEscapePackedExport,
                        item.Source.ToCharArray(), item.Start, item.Length), Is.EqualTo(expected));
                }

                var maximumSource = new string('x', 16_382) + "\\n";
                var maximum = maximumSource.ToCharArray();
                Assert.That(bindings.Invoke<uint>(AnalyzeStringEscapePackedExport, maximum, 16_382, 2), Is.EqualTo(PackStringEscapeResult(10, 2, false)));
                var maximumTrailingEscape = new string('x', 16_383) + "\\";
                Assert.That(bindings.Invoke<uint>(AnalyzeStringEscapePackedExport, maximumTrailingEscape.ToCharArray(),
                        16_383, 1), Is.EqualTo(PackStringEscapeResult(0, 1, true)));
                _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    bindings.Invoke<uint>(AnalyzeStringEscapePackedExport, new char[16_385], 0, 1))!;
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
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void CharacterLiteralSourceGrantQueriesMatchHostedLiteralContract(BoundRuntimePolicy policy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = RepositoryRoot();
        var componentPaths = new[] {
            Path.Combine("Lexing", "HexDigitValue.terrum"),
            Path.Combine("Text", "Utf16CodeUnits.terrum"),
            Path.Combine("Text", "Utf16Scalar.terrum"),
            Path.Combine("Lexing", "CharacterLiteralPacked.terrum"),
            Path.Combine("Lexing", "CharacterLiteralScalar.terrum"),
        };
        var compilation = Compilation.CreateLibrary(
            [.. componentPaths.Select(relativePath =>
            {
                var path = TerrumProjectPaths.SourceFile(root, relativePath);
                return SyntaxTree.Parse(File.ReadAllText(path), path);
            })],
            [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
                IsInvocationScoped = true,
            }],
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual14 = compilation.Program; Assert.That(__nunitActual14, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual14!; }))(),
            [AnalyzeCharacterLiteralPackedExport, CharacterLiteralScalarExport], (NativeRuntimePolicy)policy);
        Assert.That(artifact.Exports.Length, Is.EqualTo(2));
        var packedExport = global::System.Linq.Enumerable.Single(artifact.Exports, static item => item.Name == AnalyzeCharacterLiteralPackedExport);
        Assert.That(packedExport.SourceDependent, Is.True);
        Assert.That(packedExport.ReturnType, Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(packedExport.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32]));
        var scalarExport = global::System.Linq.Enumerable.Single(artifact.Exports, static item => item.Name == CharacterLiteralScalarExport);
        Assert.That(scalarExport.SourceDependent, Is.True);
        Assert.That(scalarExport.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(scalarExport.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32]));

        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-source-grant-character-literal-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, artifact.Image);
        try
        {
            var module = NativeLibrary.Load(path);
            try
            {
                var bindings = new NativeSourceGrantTestBindings(module,
                    [.. artifact.Exports.Select(static item => (item.Name, item.SourceDependent,
                        item.ReturnType, item.ParameterTypes))]);
                (string Source, int Start, int Length, uint Expected)[] cases = [
                    ("'A'", 0, 3, PackCharacterLiteralSuccess(65)),
                    ("xx'A'yy", 2, 3, PackCharacterLiteralSuccess(65)),
                    ("'\\0'", 0, 4, PackCharacterLiteralSuccess(0)),
                    ("'''", 0, 3, PackCharacterLiteralSuccess(39)),
                    ("'\n'", 0, 3, PackCharacterLiteralSuccess(10)),
                    ("'\\''", 0, 4, PackCharacterLiteralSuccess(39)),
                    ("''", 0, 2, PackCharacterLiteralError(1, 0, 2)),
                    ("", 0, 0, PackCharacterLiteralError(1, 0, 0)),
                    ("'", 0, 1, PackCharacterLiteralError(1, 0, 1)),
                    ("xA'", 0, 3, PackCharacterLiteralError(1, 0, 3)),
                    ("'\uD800'", 0, 3, PackCharacterLiteralError(1, 1, 1)),
                    ("'\\q'", 0, 4, PackCharacterLiteralError(5, 1, 2)),
                    ("'\\u12'", 0, 6, PackCharacterLiteralError(5, 1, 4)),
                    ("'\\u12'34", 0, 6, PackCharacterLiteralError(5, 1, 4)),
                    ("'\\uD800'", 0, 8, PackCharacterLiteralError(5, 1, 6)),
                    ("'\\\uD800'", 0, 4, PackCharacterLiteralError(5, 1, 2)),
                    ("'a\\q'", 0, 5, PackCharacterLiteralError(5, 2, 2)),
                    ("'ab'", 0, 4, PackCharacterLiteralError(1, 2, 1)),
                    ("'\uD83D\uDE00A'", 0, 5, PackCharacterLiteralError(1, 3, 1)),
                    ("'A'", -1, 3, 0),
                    ("'A'", 0, -1, 0),
                    ("'A'", int.MaxValue, 1, 0),
                    ("'A'", 4, 1, 0),
                ];
                foreach (var item in cases)
                {
                    Assert.That(bindings.Invoke<uint>(AnalyzeCharacterLiteralPackedExport, item.Source.ToCharArray(),
                            item.Start, item.Length), Is.EqualTo(item.Expected));
                    int expectedScalar = (item.Expected & 3) == 1 ? (int)(item.Expected >> 2) : -1;
                    Assert.That(bindings.Invoke<int>(CharacterLiteralScalarExport, item.Source.ToCharArray(),
                            item.Start, item.Length), Is.EqualTo(expectedScalar));
                }

                Assert.That(bindings.Invoke<uint>(AnalyzeCharacterLiteralPackedExport, [], 0, 0), Is.EqualTo(PackCharacterLiteralError(1, 0, 0)));
                Assert.That(bindings.Invoke<int>(CharacterLiteralScalarExport, [], 0, 0), Is.EqualTo(-1));
                var maximumInvalidSource = new string('x', 16_384);
                Assert.That(bindings.Invoke<uint>(AnalyzeCharacterLiteralPackedExport, maximumInvalidSource.ToCharArray(),
                        0, maximumInvalidSource.Length), Is.EqualTo(0x8000_0002u));
                Assert.That(bindings.Invoke<int>(CharacterLiteralScalarExport, maximumInvalidSource.ToCharArray(),
                        0, maximumInvalidSource.Length), Is.EqualTo(-1));
                var maximumValidSource = new string('x', 16_381) + "'A'";
                Assert.That(bindings.Invoke<uint>(AnalyzeCharacterLiteralPackedExport, maximumValidSource.ToCharArray(),
                        16_381, 3), Is.EqualTo(PackCharacterLiteralSuccess(65)));
                Assert.That(bindings.Invoke<int>(CharacterLiteralScalarExport, maximumValidSource.ToCharArray(),
                        16_381, 3), Is.EqualTo(65));
                _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    bindings.Invoke<uint>(AnalyzeCharacterLiteralPackedExport, new char[16_385], 0, 3))!;
                _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    bindings.Invoke<int>(CharacterLiteralScalarExport, new char[16_385], 0, 3))!;
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
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void StringLiteralScalarCountMatchesHostedTryCreate(BoundRuntimePolicy policy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = RepositoryRoot();
        var componentPaths = new[] {
            Path.Combine("Lexing", "CharacterFacts.terrum"),
            Path.Combine("Lexing", "HexDigitValue.terrum"),
            Path.Combine("Text", "Utf16CodeUnits.terrum"),
            Path.Combine("Text", "Utf16Scalar.terrum"),
            Path.Combine("Lexing", "StringEscapePacked.terrum"),
            Path.Combine("Lexing", "StringLiteralScalarCount.terrum"),
        };
        var compilation = Compilation.CreateLibrary(
            [.. componentPaths.Select(relativePath =>
            {
                var path = TerrumProjectPaths.SourceFile(root, relativePath);
                return SyntaxTree.Parse(File.ReadAllText(path), path);
            })],
            [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
                IsInvocationScoped = true,
            }],
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual15 = compilation.Program; Assert.That(__nunitActual15, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual15!; }))(),
            [StringLiteralScalarCountExport], (NativeRuntimePolicy)policy);
        var export = global::System.Linq.Enumerable.Single(artifact.Exports);
        Assert.That(export.SourceDependent, Is.True);
        Assert.That(export.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(export.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32]));

        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-source-grant-string-literal-count-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, artifact.Image);
        try
        {
            var module = NativeLibrary.Load(path);
            try
            {
                var bindings = new NativeSourceGrantTestBindings(module,
                    [.. artifact.Exports.Select(static item => (item.Name, item.SourceDependent,
                        item.ReturnType, item.ParameterTypes))]);
                (string Source, int Start, int Length, int Expected)[] cases = [
                    ("\"\"", 0, 2, 0),
                    ("\"A\"", 0, 3, 1),
                    ("xx\"A\"yy", 2, 3, 1),
                    ("\"\\0\"", 0, 4, 1),
                    ("\"a\\n\\U0001F600b\"", 0, 16, 4),
                    (new string(['"', '\\', '"', '"']), 0, 4, 1),
                    (new string(['"', '"', '"']), 0, 3, -1),
                    (new string(['"', '\n', '"']), 0, 3, -1),
                    (new string(['"', '\r', '"']), 0, 3, -1),
                    (new string(['"', '\u0085', '"']), 0, 3, -1),
                    (new string(['"', '\u2028', '"']), 0, 3, -1),
                    (new string(['"', '\u2029', '"']), 0, 3, -1),
                    ("\"\\q\"", 0, 4, -1),
                    ("\"\\u12\"", 0, 6, -1),
                    ("\"\\uD800\"", 0, 8, -1),
                    (new string(['"', '\uD800', '"']), 0, 3, -1),
                    (new string(['"', '\uD83D', '\uDE00', '"']), 0, 4, 1),
                    ("\"\\uD83D\\uDE00\"", 0, 14, -1),
                    (new string(['"', '\u000B', '\u000C', '"']), 0, 4, 2),
                    (new string(['"', '\\', '"']), 0, 3, -1),
                    ("\"\\u0085\"", 0, 8, 1),
                    ("", 0, 0, -1),
                    ("\"A\"", -1, 3, -1),
                    ("\"A\"", 0, -1, -1),
                    ("\"A\"", int.MaxValue, 3, -1),
                    ("\"A\"", 0, int.MaxValue, -1),
                    ("\"A\"", 0, 1, -1),
                    ("\"A\"", 1, 3, -1),
                    ("\"A\"", 3, 0, -1),
                ];
                foreach (var item in cases)
                {
                    Assert.That(bindings.Invoke<int>(StringLiteralScalarCountExport, item.Source.ToCharArray(),
                            item.Start, item.Length), Is.EqualTo(item.Expected));
                    if (item.Start == 0 && item.Length == item.Source.Length && item.Length >= 2)
                    {
                        var lexed = Lexer.Lex(SourceText.From(item.Source));
                        if (item.Expected >= 0)
                        {
                            Assert.That(lexed.Diagnostics, Is.Empty);
                        }
                        else
                        {
                            Assert.That(lexed.Diagnostics, Is.Not.Empty);
                        }
                    }
                }

                var maximumValidSource = "\"" + new string('a', 16_382) + "\"";
                Assert.That(bindings.Invoke<int>(StringLiteralScalarCountExport, maximumValidSource.ToCharArray(),
                        0, maximumValidSource.Length), Is.EqualTo(16_382));
                var maximumEscapedSource = "\"" + new string('a', 16_380) + "\\n\"";
                Assert.That(bindings.Invoke<int>(StringLiteralScalarCountExport, maximumEscapedSource.ToCharArray(),
                        0, maximumEscapedSource.Length), Is.EqualTo(16_381));
                _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    bindings.Invoke<int>(StringLiteralScalarCountExport, new char[16_385], 0, 2))!;
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
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void StringLiteralScalarAtMatchesHostedTryCreate(BoundRuntimePolicy policy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = RepositoryRoot();
        var componentPaths = new[] {
            Path.Combine("Lexing", "CharacterFacts.terrum"),
            Path.Combine("Lexing", "HexDigitValue.terrum"),
            Path.Combine("Text", "Utf16CodeUnits.terrum"),
            Path.Combine("Text", "Utf16Scalar.terrum"),
            Path.Combine("Lexing", "StringEscapePacked.terrum"),
            Path.Combine("Lexing", "StringLiteralScalarCount.terrum"),
            Path.Combine("Lexing", "StringLiteralScalarAt.terrum"),
        };
        var compilation = Compilation.CreateLibrary(
            [.. componentPaths.Select(relativePath =>
            {
                var path = TerrumProjectPaths.SourceFile(root, relativePath);
                return SyntaxTree.Parse(File.ReadAllText(path), path);
            })],
            [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
                IsInvocationScoped = true,
            }],
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual16 = compilation.Program; Assert.That(__nunitActual16, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual16!; }))(),
            [StringLiteralScalarAtExport], (NativeRuntimePolicy)policy);
        var export = global::System.Linq.Enumerable.Single(artifact.Exports);
        Assert.That(export.SourceDependent, Is.True);
        Assert.That(export.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(export.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32, PrimitiveType.Int32]));

        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-source-grant-string-literal-scalar-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, artifact.Image);
        try
        {
            var module = NativeLibrary.Load(path);
            try
            {
                var bindings = new NativeSourceGrantTestBindings(module,
                    [.. artifact.Exports.Select(static item => (item.Name, item.SourceDependent,
                        item.ReturnType, item.ParameterTypes))]);
                (string Source, int Start, int Length, int Index, int ExpectedScalar, bool ExpectedValid)[] cases = [
                    ("\"\"", 0, 2, -1, -1, true),
                    ("\"\"", 0, 2, 0, -1, true),
                    ("\"A\"", 0, 3, 0, (int)'A', true),
                    ("\"A\"", 0, 3, 1, -1, true),
                    ("\"\\0\"", 0, 4, 0, 0, true),
                    ("\"\\uFFFF\"", 0, 8, 0, 0xFFFF, true),
                    (new string(['"', '\uFFFF', '"']), 0, 3, 0, 0xFFFF, true),
                    ("\"\\U0001F600\"", 0, 12, 0, 0x1F600, true),
                    (new string(['"', '\uD83D', '\uDE00', '"']), 0, 4, 0, 0x1F600, true),
                    ("\"A\\0\\U0001F600\\uFFFF\"", 0, 21, 0, (int)'A', true),
                    ("\"A\\0\\U0001F600\\uFFFF\"", 0, 21, 1, 0, true),
                    ("\"A\\0\\U0001F600\\uFFFF\"", 0, 21, 2, 0x1F600, true),
                    ("\"A\\0\\U0001F600\\uFFFF\"", 0, 21, 3, 0xFFFF, true),
                    ("\"A\\0\\U0001F600\\uFFFF\"", 0, 21, 4, -1, true),
                    ("\"A\\q\"", 0, 5, 0, -1, false),
                    ("\"A\\uD800\"", 0, 9, 0, -1, false),
                    ("\"\\uD83D\\uDE00\"", 0, 14, 0, -1, false),
                    (new string(['"', 'A', '\n', '"']), 0, 4, 0, -1, false),
                    ("xx\"A\\q\"yy", 2, 5, 0, -1, false),
                    ("\"A\"", -1, 3, 0, -1, false),
                    ("\"A\"", 0, int.MaxValue, 0, -1, false),
                    ("\"A\"", int.MaxValue, 3, 0, -1, false),
                    ("\"A\"", 0, 3, -1, -1, true),
                    ("\"A\"", 0, 3, int.MaxValue, -1, true),
                ];
                foreach (var item in cases)
                {
                    Assert.That(bindings.Invoke<int>(StringLiteralScalarAtExport, item.Source.ToCharArray(),
                            item.Start, item.Length, item.Index), Is.EqualTo(item.ExpectedScalar));
                    if (item.Start >= 0 && item.Length >= 0 && item.Start <= item.Source.Length
                        && item.Length <= item.Source.Length - item.Start)
                    {
                        var spelling = item.Source.Substring(item.Start, item.Length);
                        var lexed = Lexer.Lex(SourceText.From(spelling));
                        var isSingleValidLiteral = lexed.Diagnostics.IsEmpty && lexed.Tokens.Length == 2
                            && lexed.Tokens[0].Kind == TokenKind.StringLiteral
                            && lexed.Tokens[0].Span == new TextSpan(0, spelling.Length)
                            && lexed.Tokens[1].Kind == TokenKind.EndOfFile;
                        Assert.That(isSingleValidLiteral, Is.EqualTo(item.ExpectedValid));
                    }
                }

                var maximumValidSource = "\"" + new string('a', 16_382) + "\"";
                Assert.That(bindings.Invoke<int>(StringLiteralScalarAtExport, maximumValidSource.ToCharArray(),
                        0, maximumValidSource.Length, 16_381), Is.EqualTo((int)'a'));
                Assert.That(bindings.Invoke<int>(StringLiteralScalarAtExport, maximumValidSource.ToCharArray(),
                        0, maximumValidSource.Length, 16_382), Is.EqualTo(-1));
                var maximumEscapedSource = "\"" + new string('a', 16_380) + "\\n\"";
                Assert.That(bindings.Invoke<int>(StringLiteralScalarAtExport, maximumEscapedSource.ToCharArray(),
                        0, maximumEscapedSource.Length, 16_380), Is.EqualTo(10));
                _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    bindings.Invoke<int>(StringLiteralScalarAtExport, new char[16_385], 0, 2, 0))!;
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
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void StringLiteralLengthsMatchHostEncodersAndRows(BoundRuntimePolicy policy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = RepositoryRoot();
        var componentPaths = new[] {
            Path.Combine("Lexing", "CharacterFacts.terrum"),
            Path.Combine("Lexing", "HexDigitValue.terrum"),
            Path.Combine("Text", "Utf16CodeUnits.terrum"),
            Path.Combine("Text", "Utf16Scalar.terrum"),
            Path.Combine("Lexing", "StringEscapePacked.terrum"),
            Path.Combine("Lexing", "StringLiteralScalarCount.terrum"),
            Path.Combine("Lexing", "StringLiteralEncodedLength.terrum"),
        };
        var compilation = Compilation.CreateLibrary(
            [.. componentPaths.Select(relativePath =>
            {
                var path = TerrumProjectPaths.SourceFile(root, relativePath);
                return SyntaxTree.Parse(File.ReadAllText(path), path);
            })],
            [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
                IsInvocationScoped = true,
            }],
            (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual17 = compilation.Program; Assert.That(__nunitActual17, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual17!; }))(),
            [StringLiteralEncodedLengthExport, StringNameRowByteLengthExport,
                StringNamespaceRowByteLengthExport], (NativeRuntimePolicy)policy);
        var export = global::System.Linq.Enumerable.Single(artifact.Exports, static item => item.Name == StringLiteralEncodedLengthExport);
        Assert.That(export.SourceDependent, Is.True);
        Assert.That(export.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(export.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32, PrimitiveType.Int32]));
        var nameRowLengthExport = global::System.Linq.Enumerable.Single(artifact.Exports, static item => item.Name == StringNameRowByteLengthExport);
        Assert.That(nameRowLengthExport.SourceDependent, Is.True);
        Assert.That(nameRowLengthExport.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(nameRowLengthExport.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32]));
        var namespaceRowLengthExport = global::System.Linq.Enumerable.Single(artifact.Exports, static item => item.Name == StringNamespaceRowByteLengthExport);
        Assert.That(namespaceRowLengthExport.SourceDependent, Is.True);
        Assert.That(namespaceRowLengthExport.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(namespaceRowLengthExport.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32]));

        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-source-grant-string-literal-length-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, artifact.Image);
        try
        {
            var module = NativeLibrary.Load(path);
            try
            {
                var bindings = new NativeSourceGrantTestBindings(module,
                    [.. artifact.Exports.Select(static item => (item.Name, item.SourceDependent,
                        item.ReturnType, item.ParameterTypes))]);
                var strictUtf8 = new UTF8Encoding(false, true);
                var strictUtf16 = new UnicodeEncoding(false, false, true);
                var strictUtf32 = new UTF32Encoding(false, false, true);
                var strictAscii = Encoding.GetEncoding(Encoding.ASCII.CodePage,
                    EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                var strictLatin1 = Encoding.GetEncoding(28591,
                    EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                int? TryGetByteCount(Encoding encoding, string text)
                {
                    try
                    {
                        return encoding.GetByteCount(text);
                    }
                    catch (EncoderFallbackException)
                    {
                        return null;
                    }
                }
                int? TryGetStringRowLength(string text, bool isName)
                {
                    if ((isName && text.Length == 0) || text.Contains('\0'))
                    {
                        return null;
                    }
                    var section = isName
                        ? TrRmStringTableWriter.EncodeNames([(text, 1)])
                        : TrRmStringTableWriter.EncodeTextRows([(text, 1)]);
                    return new TrRmVariableTableIndex(section, 1, 8, 4).GetRow(1).Length;
                }

                (string Text, string Source, int Start, int Length, bool IsValid)[] fixtures = [
                    ("", "\"\"", 0, 2, true),
                    ("ASCII", "\"ASCII\"", 0, 7, true),
                    ("é", "\"é\"", 0, 3, true),
                    ("é", "\"\\u00E9\"", 0, 8, true),
                    ("😀", "\"😀\"", 0, 4, true),
                    ("😀", "\"\\U0001F600\"", 0, 12, true),
                    ("Aé😀\0", "\"A\\u00E9\\U0001F600\\0\"", 0, 21, true),
                    ("\n", "\"\\n\"", 0, 4, true),
                    ("\u007F\u0080\u00FF\u0100\u07FF\u0800\uD7FF\uE000\uFFFF\U00010000\U0010FFFF",
                        "\"\\u007F\\u0080\\u00FF\\u0100\\u07FF\\u0800\\uD7FF\\uE000\\uFFFF\\U00010000\\U0010FFFF\"",
                        0, 76, true),
                    ("é", "xx\"\\u00E9\"yy", 2, 8, true),
                    ("", "\"A\\q\"", 0, 5, false),
                    ("", "\"A\\uD800\"", 0, 9, false),
                    ("", new string(['"', '\uD800', '"']), 0, 3, false),
                    ("", new string(['"', 'A', '"', '"']), 0, 4, false),
                    ("", new string(['"', 'A', '\n', '"']), 0, 4, false),
                ];
                foreach (var fixture in fixtures)
                {
                    var spelling = fixture.Source.Substring(fixture.Start, fixture.Length);
                    var lexed = Lexer.Lex(SourceText.From(spelling));
                    var isSingleValidLiteral = lexed.Diagnostics.IsEmpty && lexed.Tokens.Length == 2
                        && lexed.Tokens[0].Kind == TokenKind.StringLiteral
                        && lexed.Tokens[0].Span == new TextSpan(0, spelling.Length)
                        && lexed.Tokens[1].Kind == TokenKind.EndOfFile;
                    Assert.That(isSingleValidLiteral, Is.EqualTo(fixture.IsValid));
                    int?[] expectedLengths = [
                        TryGetByteCount(strictAscii, fixture.Text),
                        TryGetByteCount(strictLatin1, fixture.Text),
                        TryGetByteCount(strictUtf8, fixture.Text),
                        fixture.Text.Length,
                        TryGetByteCount(strictUtf16, fixture.Text),
                        TryGetByteCount(strictUtf32, fixture.Text),
                    ];
                    for (var encoding = 0; encoding < expectedLengths.Length; encoding++)
                    {
                        var expected = fixture.IsValid ? expectedLengths[encoding] ?? -1 : -1;
                        Assert.That(bindings.Invoke<int>(StringLiteralEncodedLengthExport, fixture.Source.ToCharArray(),
                                fixture.Start, fixture.Length, encoding), Is.EqualTo(expected));
                    }
                    var expectedNameRowLength = fixture.IsValid
                        ? TryGetStringRowLength(fixture.Text, true) ?? -1
                        : -1;
                    Assert.That(bindings.Invoke<int>(StringNameRowByteLengthExport, fixture.Source.ToCharArray(),
                            fixture.Start, fixture.Length), Is.EqualTo(expectedNameRowLength));
                    var expectedNamespaceRowLength = fixture.IsValid
                        ? TryGetStringRowLength(fixture.Text, false) ?? -1
                        : -1;
                    Assert.That(bindings.Invoke<int>(StringNamespaceRowByteLengthExport, fixture.Source.ToCharArray(),
                            fixture.Start, fixture.Length), Is.EqualTo(expectedNamespaceRowLength));
                }

                foreach (var invalidEncoding in new[] { -1, 6, int.MaxValue })
                {
                    Assert.That(bindings.Invoke<int>(StringLiteralEncodedLengthExport, "\"A\"".ToCharArray(),
                            0, 3, invalidEncoding), Is.EqualTo(-1));
                }
                foreach (var (start, length) in new[] {
                    (-1, 3), (0, int.MaxValue), (int.MaxValue, 3), (0, 1),
                })
                {
                    Assert.That(bindings.Invoke<int>(StringNameRowByteLengthExport, "\"A\"".ToCharArray(),
                            start, length), Is.EqualTo(-1));
                    Assert.That(bindings.Invoke<int>(StringNamespaceRowByteLengthExport, "\"A\"".ToCharArray(),
                            start, length), Is.EqualTo(-1));
                }

                foreach (var (start, length) in new[] {
                    (-1, 3), (0, int.MaxValue), (int.MaxValue, 3), (0, 1),
                })
                {
                    Assert.That(bindings.Invoke<int>(StringLiteralEncodedLengthExport, "\"A\"".ToCharArray(),
                            start, length, 2), Is.EqualTo(-1));
                }

                var maximumAsciiSource = "\"" + new string('a', 16_382) + "\"";
                int[] maximumLengths = [16_382, 16_382, 16_382, 16_382, 32_764, 65_528];
                for (var encoding = 0; encoding < maximumLengths.Length; encoding++)
                {
                    Assert.That(bindings.Invoke<int>(StringLiteralEncodedLengthExport,
                            maximumAsciiSource.ToCharArray(), 0, maximumAsciiSource.Length, encoding), Is.EqualTo(maximumLengths[encoding]));
                }
                Assert.That(bindings.Invoke<int>(StringNameRowByteLengthExport,
                        maximumAsciiSource.ToCharArray(), 0, maximumAsciiSource.Length), Is.EqualTo(16_388));
                Assert.That(bindings.Invoke<int>(StringNamespaceRowByteLengthExport,
                        maximumAsciiSource.ToCharArray(), 0, maximumAsciiSource.Length), Is.EqualTo(16_388));

                var maximumUtf8Text = new string('\u0800', 16_382);
                var maximumUtf8Source = "\"" + maximumUtf8Text + "\"";
                int?[] maximumUtf8Lengths = [
                    TryGetByteCount(strictAscii, maximumUtf8Text),
                    TryGetByteCount(strictLatin1, maximumUtf8Text),
                    TryGetByteCount(strictUtf8, maximumUtf8Text),
                    maximumUtf8Text.Length,
                    TryGetByteCount(strictUtf16, maximumUtf8Text),
                    TryGetByteCount(strictUtf32, maximumUtf8Text),
                ];
                Assert.That(maximumUtf8Lengths, Is.EqualTo(new int?[] { null, null, 49_146, 16_382, 32_764, 65_528 }));
                for (var encoding = 0; encoding < maximumUtf8Lengths.Length; encoding++)
                {
                    Assert.That(bindings.Invoke<int>(StringLiteralEncodedLengthExport,
                            maximumUtf8Source.ToCharArray(), 0, maximumUtf8Source.Length, encoding), Is.EqualTo(maximumUtf8Lengths[encoding] ?? -1));
                }
                Assert.That(bindings.Invoke<int>(StringNameRowByteLengthExport,
                        maximumUtf8Source.ToCharArray(), 0, maximumUtf8Source.Length), Is.EqualTo(49_152));
                Assert.That(bindings.Invoke<int>(StringNamespaceRowByteLengthExport,
                        maximumUtf8Source.ToCharArray(), 0, maximumUtf8Source.Length), Is.EqualTo(49_152));

                var maximumLatin1Text = new string('\u00FF', 16_382);
                var maximumLatin1Source = "\"" + maximumLatin1Text + "\"";
                int?[] maximumLatin1Lengths = [
                    TryGetByteCount(strictAscii, maximumLatin1Text),
                    TryGetByteCount(strictLatin1, maximumLatin1Text),
                    TryGetByteCount(strictUtf8, maximumLatin1Text),
                    maximumLatin1Text.Length,
                    TryGetByteCount(strictUtf16, maximumLatin1Text),
                    TryGetByteCount(strictUtf32, maximumLatin1Text),
                ];
                Assert.That(maximumLatin1Lengths, Is.EqualTo(new int?[] { null, 16_382, 32_764, 16_382, 32_764, 65_528 }));
                for (var encoding = 0; encoding < maximumLatin1Lengths.Length; encoding++)
                {
                    Assert.That(bindings.Invoke<int>(StringLiteralEncodedLengthExport,
                            maximumLatin1Source.ToCharArray(), 0, maximumLatin1Source.Length, encoding), Is.EqualTo(maximumLatin1Lengths[encoding] ?? -1));
                }

                _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    bindings.Invoke<int>(StringLiteralEncodedLengthExport, new char[16_385], 0, 2, 2))!;
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
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void SourceOffsetMatchesHostedTryGetOffset(BoundRuntimePolicy policy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = RepositoryRoot();
        var characterFactsPath = TerrumProjectPaths.SourceFile(root,
            Path.Combine("Lexing", "CharacterFacts.terrum"));
        var sourceOffsetPath = TerrumProjectPaths.SourceFile(root,
            Path.Combine("Text", "SourceOffset.terrum"));
        var compilation = Compilation.CreateLibrary([
            SyntaxTree.Parse(File.ReadAllText(characterFactsPath), characterFactsPath),
            SyntaxTree.Parse(File.ReadAllText(sourceOffsetPath), sourceOffsetPath),
        ], [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        }], (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual18 = compilation.Program; Assert.That(__nunitActual18, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual18!; }))(), [SourceOffsetExport], (NativeRuntimePolicy)policy);
        Assert.That(global::System.Linq.Enumerable.Single(artifact.Exports).SourceDependent, Is.True);

        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-source-grant-offset-{Guid.NewGuid():N}.dll");
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
                    binding.Invoke<int>(SourceOffsetExport, new char[16_385], 0, 0))!;
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
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void SourcePositionComponentsMatchHostedGetLinePosition(BoundRuntimePolicy policy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = RepositoryRoot();
        var characterFactsPath = TerrumProjectPaths.SourceFile(root,
            Path.Combine("Lexing", "CharacterFacts.terrum"));
        var sourceOffsetPath = TerrumProjectPaths.SourceFile(root,
            Path.Combine("Text", "SourceOffset.terrum"));
        var sourcePositionPath = TerrumProjectPaths.SourceFile(root,
            Path.Combine("Text", "SourcePosition.terrum"));
        var compilation = Compilation.CreateLibrary([
            SyntaxTree.Parse(File.ReadAllText(characterFactsPath), characterFactsPath),
            SyntaxTree.Parse(File.ReadAllText(sourceOffsetPath), sourceOffsetPath),
            SyntaxTree.Parse(File.ReadAllText(sourcePositionPath), sourcePositionPath),
        ], [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        }], (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual19 = compilation.Program; Assert.That(__nunitActual19, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual19!; }))(),
            [SourceLineNumberExport, SourceCharacterPositionExport, SourceOffsetExport],
            (NativeRuntimePolicy)policy);
        Assert.That(artifact.Exports.Length, Is.EqualTo(3));
        {var __nunitItems0 = artifact.Exports;        Assert.Multiple(() => {
            foreach (var __nunitItem20 in __nunitItems0)
            {
                {
                    Assert.That(__nunitItem20.SourceDependent, Is.True);
                    Assert.That(__nunitItem20.ReturnType, Is.EqualTo(PrimitiveType.Int32));
                }
            }
        });}
        Assert.That(global::System.Linq.Enumerable.Single(artifact.Exports, static export => export.Name == SourceLineNumberExport).ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32]));
        Assert.That(global::System.Linq.Enumerable.Single(artifact.Exports, static export => export.Name == SourceCharacterPositionExport).ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32]));
        Assert.That(global::System.Linq.Enumerable.Single(artifact.Exports, static export => export.Name == SourceOffsetExport).ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32]));

        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-source-grant-position-{Guid.NewGuid():N}.dll");
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
                    binding.Invoke<int>(SourceLineNumberExport, new char[16_385], 0))!;
                _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    binding.Invoke<int>(SourceCharacterPositionExport, new char[16_385], 0))!;
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
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void SourceLineExtentsMatchHostedContentAndIncludingBreakExtents(BoundRuntimePolicy policy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = RepositoryRoot();
        var characterFactsPath = TerrumProjectPaths.SourceFile(root,
            Path.Combine("Lexing", "CharacterFacts.terrum"));
        var sourceOffsetPath = TerrumProjectPaths.SourceFile(root,
            Path.Combine("Text", "SourceOffset.terrum"));
        var sourceLineEndPath = TerrumProjectPaths.SourceFile(root,
            Path.Combine("Text", "SourceLineEnd.terrum"));
        var compilation = Compilation.CreateLibrary([
            SyntaxTree.Parse(File.ReadAllText(characterFactsPath), characterFactsPath),
            SyntaxTree.Parse(File.ReadAllText(sourceOffsetPath), sourceOffsetPath),
            SyntaxTree.Parse(File.ReadAllText(sourceLineEndPath), sourceLineEndPath),
        ], [new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        }], (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual21 = compilation.Program; Assert.That(__nunitActual21, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual21!; }))(),
            [SourceOffsetExport, SourceLineContentEndExport, SourceLineEndExport],
            (NativeRuntimePolicy)policy);
        var contentEnd = global::System.Linq.Enumerable.Single(artifact.Exports, static export => export.Name == SourceLineContentEndExport);
        Assert.That(contentEnd.SourceDependent, Is.True);
        Assert.That(contentEnd.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(contentEnd.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32]));
        var lineEnd = global::System.Linq.Enumerable.Single(artifact.Exports, static export => export.Name == SourceLineEndExport);
        Assert.That(lineEnd.SourceDependent, Is.True);
        Assert.That(lineEnd.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(lineEnd.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32]));

        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-source-grant-line-end-{Guid.NewGuid():N}.dll");
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
                    Assert.That(bindings.Invoke<int>(SourceLineContentEndExport, sourceAfterCr, 0), Is.EqualTo(hosted.Lines[0].Span.End));
                    Assert.That(bindings.Invoke<int>(SourceLineEndExport, sourceAfterCr, 0), Is.EqualTo(hosted.Lines[0].SpanIncludingLineBreak.End));
                }

                AssertSourceLineEndCases(bindings, new string('x', 16_384), 0, 1);
                AssertSourceLineEndCases(bindings, new string('\n', 16_384), 0, 8_192, 16_383, 16_384, 16_385);
                AssertSourceLineEndCases(bindings, new string('\r', 16_384), 0, 8_192, 16_383, 16_384, 16_385);
                AssertSourceLineEndCases(bindings, string.Concat(Enumerable.Repeat("\r\n", 8_192)),
                    0, 4_096, 8_191, 8_192, 8_193);
                AssertSourceLineEndCases(bindings, new string('x', 16_383) + '\r', 0, 1, 2);
                AssertSourceLineEndCases(bindings, new string('x', 16_382) + "\r\n", 0, 1, 2);
                _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    bindings.Invoke<int>(SourceLineEndExport, new char[16_385], 0))!;
                _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    bindings.Invoke<int>(SourceLineContentEndExport, new char[16_385], 0))!;
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

    [Test]
    public void MaintainedCompilerPrimitivesCompanionUsesItsGeneratedManifest()
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("Missing configuration directory.");
        var artifactDirectory = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources",
            "Terrum.CodeAnalysis.Terrum", configuration);
        var imagePath = Path.Combine(artifactDirectory, "terrum-compiler-primitives-source-grant.dll");
        var manifestPath = Path.Combine(artifactDirectory, "terrum-compiler-primitives-source-grant.abi.json");
        Assert.That(File.Exists(imagePath), Is.True, $"Source-grant compiler-primitives companion was not built: {imagePath}");
        Assert.That(File.Exists(manifestPath), Is.True, $"Source-grant compiler-primitives manifest was not built: {manifestPath}");

        var image = File.ReadAllBytes(imagePath);
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var root = manifest.RootElement;
        Assert.That(root.GetProperty("sourceGrantAbiVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(root.GetProperty("maximumSourceCodeUnits").GetInt32(), Is.EqualTo(16_384));
        Assert.That(root.GetProperty("imageSha256").GetString(), Is.EqualTo(Convert.ToHexString(SHA256.HashData(image))));
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
        string[] actualExportNames = [.. exports.Select(static export => export.Name).OrderBy(static name => name, StringComparer.Ordinal)];
        string[] expectedExportNames = [AnalyzeCharacterLiteralPackedExport, AnalyzeStringEscapePackedExport,
            CanGetSourceRangeExport, DecodeScalarExport,
            BasedIntegerFloat32BitsExport, BasedIntegerFloat64BitsExport,
            CharacterLiteralScalarExport,
            DecimalFloat32BitsExport, DecimalFloat64BitsExport,
            HexFloat32BitsExport, HexFloat64BitsExport,
            SourceCharacterPositionExport,
            SourceCodeUnitExport,
            SourceLineContentEndExport, LineCountExport, SourceLineEndExport, SourceLineNumberExport,
            SourceOffsetExport, SourceLengthExport, StringLiteralEncodedLengthExport,
            StringLiteralScalarCountExport, StringLiteralScalarAtExport, StringNameRowByteLengthExport,
            StringNamespaceRowByteLengthExport,
            CanParseChar16Export,
            CanParseInt32Export, CanParseInt64Export, CanParseUInt32Export, CanParseUInt64Export,
            ParseChar16Export, ParseInt32Export, ParseInt64Export, ParseUInt32Export, ParseUInt64Export,
            ReadScalarExport];
        Assert.That(actualExportNames, Is.EqualTo(expectedExportNames));
        var packedEscape = global::System.Linq.Enumerable.Single(exports, static export => export.Name == AnalyzeStringEscapePackedExport);
        Assert.That(packedEscape.SourceDependent, Is.True);
        Assert.That(packedEscape.ReturnType, Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(packedEscape.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32]));
        var packedCharacterLiteral = global::System.Linq.Enumerable.Single(exports, static export => export.Name == AnalyzeCharacterLiteralPackedExport);
        Assert.That(packedCharacterLiteral.SourceDependent, Is.True);
        Assert.That(packedCharacterLiteral.ReturnType, Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(packedCharacterLiteral.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32]));
        var characterLiteralScalar = global::System.Linq.Enumerable.Single(exports, static export => export.Name == CharacterLiteralScalarExport);
        Assert.That(characterLiteralScalar.SourceDependent, Is.True);
        Assert.That(characterLiteralScalar.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(characterLiteralScalar.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32]));
        var scalarCount = global::System.Linq.Enumerable.Single(exports, static export => export.Name == StringLiteralScalarCountExport);
        Assert.That(scalarCount.SourceDependent, Is.True);
        Assert.That(scalarCount.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(scalarCount.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32]));
        var scalarAt = global::System.Linq.Enumerable.Single(exports, static export => export.Name == StringLiteralScalarAtExport);
        Assert.That(scalarAt.SourceDependent, Is.True);
        Assert.That(scalarAt.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(scalarAt.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32, PrimitiveType.Int32]));
        var encodedLength = global::System.Linq.Enumerable.Single(exports, static export => export.Name == StringLiteralEncodedLengthExport);
        Assert.That(encodedLength.SourceDependent, Is.True);
        Assert.That(encodedLength.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(encodedLength.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32, PrimitiveType.Int32]));
        var nameRowLength = global::System.Linq.Enumerable.Single(exports, static export => export.Name == StringNameRowByteLengthExport);
        Assert.That(nameRowLength.SourceDependent, Is.True);
        Assert.That(nameRowLength.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(nameRowLength.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32]));
        var namespaceRowLength = global::System.Linq.Enumerable.Single(exports, static export => export.Name == StringNamespaceRowByteLengthExport);
        Assert.That(namespaceRowLength.SourceDependent, Is.True);
        Assert.That(namespaceRowLength.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(namespaceRowLength.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32]));
        foreach (var exportName in new[] { HexFloat32BitsExport, HexFloat64BitsExport })
        {
            var hexFloatBits = global::System.Linq.Enumerable.Single(exports, export => export.Name == exportName);
            Assert.That(hexFloatBits.SourceDependent, Is.True);
            Assert.That(hexFloatBits.ReturnType, Is.EqualTo(PrimitiveType.Int64));
            Assert.That(hexFloatBits.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32]));
        }
        foreach (var exportName in new[] { BasedIntegerFloat32BitsExport, BasedIntegerFloat64BitsExport })
        {
            var basedIntegerFloatBits = global::System.Linq.Enumerable.Single(exports, export => export.Name == exportName);
            Assert.That(basedIntegerFloatBits.SourceDependent, Is.True);
            Assert.That(basedIntegerFloatBits.ReturnType, Is.EqualTo(PrimitiveType.Int64));
            Assert.That(basedIntegerFloatBits.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32]));
        }
        foreach (var exportName in new[] { DecimalFloat32BitsExport, DecimalFloat64BitsExport })
        {
            var decimalFloatBits = global::System.Linq.Enumerable.Single(exports, export => export.Name == exportName);
            Assert.That(decimalFloatBits.SourceDependent, Is.True);
            Assert.That(decimalFloatBits.ReturnType, Is.EqualTo(PrimitiveType.Int64));
            Assert.That(decimalFloatBits.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32]));
        }

        var module = NativeLibrary.Load(imagePath);
        try
        {
            var bindings = new NativeSourceGrantTestBindings(module, exports);
            Assert.That(bindings.Invoke<uint>(AnalyzeStringEscapePackedExport, "\\n".ToCharArray(), 0, 2), Is.EqualTo(PackStringEscapeResult(10, 2, false)));
            Assert.That(bindings.Invoke<uint>(AnalyzeStringEscapePackedExport, "\\\uD800A".ToCharArray(), 0, 3), Is.EqualTo(PackStringEscapeResult(-1, 2, true)));
            Assert.That(bindings.Invoke<uint>(AnalyzeCharacterLiteralPackedExport, "'\uD83D\uDE00'".ToCharArray(), 0, 4), Is.EqualTo(PackCharacterLiteralSuccess(0x1F600)));
            Assert.That(bindings.Invoke<int>(CharacterLiteralScalarExport, "'\uD83D\uDE00'".ToCharArray(), 0, 4), Is.EqualTo(0x1F600));
            Assert.That(bindings.Invoke<int>(StringLiteralScalarCountExport, "\"a\\n\"".ToCharArray(), 0, 5), Is.EqualTo(2));
            Assert.That(bindings.Invoke<int>(StringLiteralScalarAtExport, "\"a\\n\"".ToCharArray(), 0, 5, 1), Is.EqualTo(10));
            Assert.That(bindings.Invoke<int>(StringLiteralEncodedLengthExport, "\"a\\n\"".ToCharArray(), 0, 5, 2), Is.EqualTo(2));
            Assert.That(bindings.Invoke<int>(StringNameRowByteLengthExport, "\"a\\n\"".ToCharArray(), 0, 5), Is.EqualTo(8));
            Assert.That(bindings.Invoke<int>(StringNamespaceRowByteLengthExport, "\"a\\n\"".ToCharArray(), 0, 5), Is.EqualTo(8));
            var parserSource = "!1__2?".ToCharArray();
            Assert.That(bindings.Invoke<byte>(CanParseUInt64Export, parserSource, 1, 4, (uint)10), Is.EqualTo((byte)1));
            Assert.That(bindings.Invoke<ulong>(ParseUInt64Export, parserSource, 1, 4, (uint)10), Is.EqualTo(12UL));
            Assert.That(bindings.Invoke<byte>(CanParseInt64Export, parserSource, 1, 4, (uint)10), Is.EqualTo((byte)1));
            Assert.That(bindings.Invoke<long>(ParseInt64Export, parserSource, 1, 4, (uint)10), Is.EqualTo(12L));
            Assert.That(bindings.Invoke<byte>(CanParseUInt32Export, parserSource, 1, 4, (uint)10), Is.EqualTo((byte)1));
            Assert.That(bindings.Invoke<uint>(ParseUInt32Export, parserSource, 1, 4, (uint)10), Is.EqualTo(12U));
            Assert.That(bindings.Invoke<byte>(CanParseInt32Export, parserSource, 1, 4, (uint)10), Is.EqualTo((byte)1));
            Assert.That(bindings.Invoke<int>(ParseInt32Export, parserSource, 1, 4, (uint)10), Is.EqualTo(12));
            Assert.That(bindings.Invoke<long>(DecimalFloat32BitsExport, "0.1".ToCharArray(), 0, 3), Is.EqualTo(0x3DCCCCCDL));
            Assert.That(bindings.Invoke<long>(DecimalFloat64BitsExport, "0.1".ToCharArray(), 0, 3), Is.EqualTo(0x3FB999999999999AL));
            Assert.That(bindings.Invoke<byte>(CanParseChar16Export, parserSource, 1, 4, (uint)10), Is.EqualTo((byte)1));
            Assert.That(bindings.Invoke<ushort>(ParseChar16Export, parserSource, 1, 4, (uint)10), Is.EqualTo((ushort)12));
            var negativeParserSource = "!-12?".ToCharArray();
            Assert.That(bindings.Invoke<long>(ParseInt64Export,
                negativeParserSource, 1, 3, (uint)10), Is.EqualTo(-12L));
            Assert.That(bindings.Invoke<int>(ParseInt32Export,
                negativeParserSource, 1, 3, (uint)10), Is.EqualTo(-12));
            Assert.That(bindings.Invoke<byte>(CanParseUInt64Export, ['?'], 0, 1, (uint)10), Is.EqualTo((byte)0));
            Assert.That(bindings.Invoke<int>(ReadScalarExport, ['A'], 0, 1), Is.EqualTo((int)'A'));
            Assert.That(bindings.Invoke<int>(ReadScalarExport, ['\uD83D', '\uDE00'], 0, 2), Is.EqualTo(0x1F600));
            Assert.That(bindings.Invoke<int>(ReadScalarExport, ['\uD800', 'A'], 0, 1), Is.EqualTo(-1));
            Assert.That(bindings.Invoke<int>(ReadScalarExport, ['\uD800', '\uDC00'], 0, 1), Is.EqualTo(-1));
            Assert.That(bindings.Invoke<int>(ReadScalarExport,
                [.. new string('x', 16_383).ToCharArray(), 'Z'], 16_383, 16_384), Is.EqualTo((int)'Z'));
            foreach (var sample in LineCountSamples)
            {
                Assert.That(bindings.Invoke<int>(LineCountExport, sample.ToCharArray()), Is.EqualTo(SourceText.From(sample).Lines.Length));
            }
            var maximumLineCountSource = new string('\n', 16_384);
            Assert.That(bindings.Invoke<int>(LineCountExport, maximumLineCountSource.ToCharArray()), Is.EqualTo(SourceText.From(maximumLineCountSource).Lines.Length));
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
                Assert.That(bindings.Invoke<int>(SourceLengthExport, source), Is.EqualTo(SourceText.From(sample).Length));
                for (var position = 0; position < hosted.Length; position++)
                {
                    Assert.That(bindings.Invoke<int>(SourceCodeUnitExport, source, position), Is.EqualTo((int)hosted[position]));
                }

                Assert.That(bindings.Invoke<int>(SourceCodeUnitExport, source, int.MinValue), Is.EqualTo(-1));
                Assert.That(bindings.Invoke<int>(SourceCodeUnitExport, source, -1), Is.EqualTo(-1));
                Assert.That(bindings.Invoke<int>(SourceCodeUnitExport, source, source.Length), Is.EqualTo(-1));
                Assert.That(bindings.Invoke<int>(SourceCodeUnitExport, source, int.MaxValue), Is.EqualTo(-1));
            }
            var maximumSourceLength = new string('\0', 16_384);
            Assert.That(bindings.Invoke<int>(SourceLengthExport, maximumSourceLength.ToCharArray()), Is.EqualTo(SourceText.From(maximumSourceLength).Length));
            var maximumCodeUnitSource = new char[16_384];
            maximumCodeUnitSource[0] = '\uD800';
            maximumCodeUnitSource[^1] = char.MaxValue;
            var maximumCodeUnitText = SourceText.From(new string(maximumCodeUnitSource));
            Assert.That(bindings.Invoke<int>(SourceCodeUnitExport, maximumCodeUnitSource, 0), Is.EqualTo((int)maximumCodeUnitText[0]));
            Assert.That(bindings.Invoke<int>(SourceCodeUnitExport, maximumCodeUnitSource, maximumCodeUnitSource.Length - 1), Is.EqualTo((int)maximumCodeUnitText[^1]));
            Assert.That(bindings.Invoke<int>(SourceCodeUnitExport, maximumCodeUnitSource, int.MinValue), Is.EqualTo(-1));
            Assert.That(bindings.Invoke<int>(SourceCodeUnitExport, maximumCodeUnitSource, -1), Is.EqualTo(-1));
            Assert.That(bindings.Invoke<int>(SourceCodeUnitExport, maximumCodeUnitSource,
                maximumCodeUnitSource.Length), Is.EqualTo(-1));
            Assert.That(bindings.Invoke<int>(SourceCodeUnitExport, maximumCodeUnitSource, int.MaxValue), Is.EqualTo(-1));
            AssertMaximumSourceOffsetCases(bindings);
            AssertMaximumSourcePositionCases(bindings);
            AssertSourceLineEndCases(bindings, new string('\n', 16_384),
                0, 8_192, 16_383, 16_384);
            var maximumSource = new string('x', 16_384);
            AssertSourceRangeValidityMatches(bindings, maximumSource.ToCharArray(), SourceText.From(maximumSource));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                bindings.Invoke<int>(ReadScalarExport, new char[16_385], 0, 1))!;
            var singleCodeUnit = new char[1];
            for (var codeUnit = 0; codeUnit <= char.MaxValue; codeUnit++)
            {
                singleCodeUnit[0] = (char)codeUnit;
                var expected = char.IsSurrogate(singleCodeUnit[0]) ? -1 : codeUnit;
                Assert.That(bindings.Invoke<int>(ReadScalarExport, singleCodeUnit, 0, 1), Is.EqualTo(expected));
                Assert.That(bindings.Invoke<int>(SourceCodeUnitExport, singleCodeUnit, 0), Is.EqualTo(codeUnit));
            }
            Assert.That(bindings.Invoke<int>(DecodeScalarExport, null,
                (ushort)'\uD83D', (ushort)'\uDE00'), Is.EqualTo(0x1F600));
            _ = Parallel.For(0, 32, index => {
                var value = (char)('a' + index);
                Assert.That(bindings.Invoke<int>(ReadScalarExport, [value], 0, 1), Is.EqualTo((int)value));
            });
        }
        finally
        {
            NativeLibrary.Free(module);
        }
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void EmitsTransitiveScalarSourceGrantWithSourceFreeExports(BoundRuntimePolicy policy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = RepositoryRoot();
        var trees = new[] {
                Path.Combine("Text", "Utf16CodeUnits.terrum"),
                Path.Combine("Text", "Utf16Scalar.terrum"),
            }
            .Select(relativePath => {
                var path = TerrumProjectPaths.SourceFile(root, relativePath);
                return SyntaxTree.Parse(File.ReadAllText(path), path);
            })
            .ToImmutableArray();
        var sourceBuffer = new HostBuffer("Source", PrimitiveType.Char16, [], false) {
            IsInvocationScoped = true,
        };
        var compilation = Compilation.CreateLibrary(trees, [sourceBuffer], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual22 = compilation.Program; Assert.That(__nunitActual22, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual22!; }))();
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(program,
            [DecodeScalarExport, ReadScalarExport], (NativeRuntimePolicy)policy);

        var readDescriptor = global::System.Linq.Enumerable.Single(artifact.Exports, static export => export.Name == ReadScalarExport);
        Assert.That(readDescriptor.SourceDependent, Is.True);
        Assert.That(readDescriptor.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(readDescriptor.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Int32, PrimitiveType.Int32]));
        var decodeDescriptor = global::System.Linq.Enumerable.Single(artifact.Exports, static export => export.Name == DecodeScalarExport);
        Assert.That(decodeDescriptor.SourceDependent, Is.False);
        Assert.That(decodeDescriptor.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(decodeDescriptor.ParameterTypes.ToArray(), Is.EqualTo([PrimitiveType.Char16, PrimitiveType.Char16]));

        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-source-grant-library-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, artifact.Image);
        try
        {
            var module = NativeLibrary.Load(path);
            try
            {
                var bindings = new NativeSourceGrantTestBindings(module,
                    [.. artifact.Exports.Select(static export => (export.Name, export.SourceDependent,
                        export.ReturnType, export.ParameterTypes))]);
                Assert.That(bindings.Invoke<int>(ReadScalarExport, ['A'], 0, 1), Is.EqualTo((int)'A'));
                Assert.That(bindings.Invoke<int>(ReadScalarExport, ['\uD83D', '\uDE00'], 0, 2), Is.EqualTo(0x1F600));
                Assert.That(bindings.Invoke<int>(ReadScalarExport, ['\uD800', 'A'], 0, 1), Is.EqualTo(-1));
                Assert.That(bindings.Invoke<int>(ReadScalarExport, ['\uD800', '\uDC00'], 0, 1), Is.EqualTo(-1));
                Assert.That(bindings.Invoke<int>(ReadScalarExport, ['D'], 0, 1), Is.EqualTo((int)'D'));
                Assert.That(bindings.Invoke<int>(ReadScalarExport,
                    [.. new string('x', 16_383).ToCharArray(), 'Z'], 16_383, 16_384), Is.EqualTo((int)'Z'));
                _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    bindings.Invoke<int>(ReadScalarExport, new char[16_385], 0, 1))!;
                Assert.That(bindings.Invoke<int>(DecodeScalarExport, null,
                    (ushort)'\uD83D', (ushort)'\uDE00'), Is.EqualTo(0x1F600));
                if (policy == BoundRuntimePolicy.CheckedExceptions)
                {
                    var exception = Assert.Throws<TargetInvocationException>(() =>
                        bindings.Invoke<int>(ReadScalarExport, null, 0, 0))!;
                    _ = ((global::System.Func<SEHException>)(() => { var __nunitActual23 = exception.InnerException; Assert.That(__nunitActual23, Is.TypeOf<SEHException>()); return (SEHException)__nunitActual23!; }))();
                }

                _ = Parallel.For(0, 32, index => {
                    var value = (char)('A' + index);
                    Assert.That(bindings.Invoke<int>(ReadScalarExport, [value], 0, 1), Is.EqualTo((int)value));
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
            bindings.Invoke<int>(SourceOffsetExport, new char[16_385], 0, 0))!;
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
        Assert.That(actualLine, Is.EqualTo(expectedLine));
        Assert.That(actualCharacter, Is.EqualTo(expectedCharacter));
        if (expectedLine >= 0 && expectedCharacter >= 0)
        {
            Assert.That(bindings.Invoke<int>(SourceOffsetExport, source, actualLine, actualCharacter), Is.EqualTo(position));
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
                Assert.That(actual, Is.EqualTo(expected ? (byte)1 : (byte)0));
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
            Assert.That(bindings.Invoke<int>(SourceLineContentEndExport, source, line), Is.EqualTo(-1));
            Assert.That(bindings.Invoke<int>(SourceLineEndExport, source, line), Is.EqualTo(-1));
            return;
        }

        var expectedContent = hosted.Lines[line].Span;
        var expectedIncludingBreak = hosted.Lines[line].SpanIncludingLineBreak;
        var start = bindings.Invoke<int>(SourceOffsetExport, source, line, 0);
        var contentEnd = bindings.Invoke<int>(SourceLineContentEndExport, source, line);
        var includingBreakEnd = bindings.Invoke<int>(SourceLineEndExport, source, line);
        Assert.That(start, Is.EqualTo(expectedContent.Start));
        Assert.That(contentEnd, Is.EqualTo(expectedContent.End));
        Assert.That(start, Is.EqualTo(expectedIncludingBreak.Start));
        Assert.That(includingBreakEnd, Is.EqualTo(expectedIncludingBreak.End));
        Assert.That(new TextSpan(start, contentEnd - start), Is.EqualTo(expectedContent));
        Assert.That(new TextSpan(start, includingBreakEnd - start), Is.EqualTo(expectedIncludingBreak));
        if (verifyOffsetBoundaries)
        {
            Assert.That(bindings.Invoke<int>(SourceOffsetExport, source, line, contentEnd - start), Is.EqualTo(contentEnd));
            var includingBreakCharacter = includingBreakEnd - start;
            var expectedIncludingBreakOffset = line + 1 < hosted.Lines.Length ? -1 : includingBreakEnd;
            Assert.That(bindings.Invoke<int>(SourceOffsetExport, source, line, includingBreakCharacter), Is.EqualTo(expectedIncludingBreakOffset));
        }
    }

    private static void AssertSourceOffsetMatches(NativeSourceGrantTestBindings bindings, char[] source,
        SourceText hosted, int line, int character)
    {
        var expected = hosted.TryGetOffset(line, character, out var offset) ? offset : -1;
        Assert.That(bindings.Invoke<int>(SourceOffsetExport, source, line, character), Is.EqualTo(expected));
    }

    private static uint PackStringEscapeResult(int scalar, int consumedLength, bool hasError) =>
        ((uint)(scalar + 1) << 5) | ((uint)consumedLength << 1) | (hasError ? 1u : 0u);

    private static uint PackCharacterLiteralSuccess(int scalar) => ((uint)scalar << 2) | 1;

    private static uint PackCharacterLiteralError(int errorCode, int relativeStart, int length) =>
        (errorCode == 5 ? 3u : 2u) | ((uint)relativeStart << 2) | ((uint)length << 17);

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
