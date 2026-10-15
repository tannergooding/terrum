// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class DirectNativeImportTests
{
    private const string ImportDeclaration = """
        [Terrum.Interop.NativeImport("kernel32.dll", "QueryPerformanceCounter")]
        [Terrum.Interop.NativeOutput(0)]
        extern int32 QueryPerformanceCounter(write ptr<int64> counter);
        """;
    private const string PublicImportDeclaration = """
        [Terrum.Interop.NativeImport("kernel32.dll", "QueryPerformanceCounter")]
        [Terrum.Interop.NativeOutput(0)]
        public extern int32 QueryPerformanceCounter(write ptr<int64> counter);
        """;
    private const string OpaquePointerImportDeclaration = """
        [Terrum.Interop.NativeImport("example.dll", "AcquireInterface")]
        [Terrum.Interop.NativeOutput(0)]
        public extern int32 AcquireInterface(write ptr<ptr<void>> result);
        """;

    [Test]
    public void SourceAuthoredImportEmitsTheNamedWindowsBinding()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ImportDeclaration + """
            int32 Main() {
                mut int64 counter = 0;
                unsafe {
                    if (QueryPerformanceCounter(&counter) != 0) { return 0; }
                }
                return -1;
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var image = NativeEmitter.EmitWithNativeImports(
            compilation.Program!, NativeRuntimePolicy.FailFast);

        Assert.That(Encoding.ASCII.GetString(image),
            Does.Contain("QueryPerformanceCounter"));
        Assert.That(Encoding.ASCII.GetString(image),
            Does.Contain("KERNEL32.dll"));
    }

    [Test]
    public void SourceAuthoredImportRequiresUnsafeCallers()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ImportDeclaration + """
            int32 Main() {
                mut int64 counter = 0;
                return QueryPerformanceCounter(&counter);
            }
            """));

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3028"), Is.True);
    }

    [Test]
    public void SourceAuthoredOutputRequiresMatchingWritableLocal()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ImportDeclaration + """
            int32 Main() {
                mut int32 counter = 0;
                unsafe { return QueryPerformanceCounter(&counter); }
            }
            """));

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True);
    }

    [Test]
    public void WritableNativeOutputSurvivesPrivateSemanticAndNativeModuleDependencies()
    {
        var source = """
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            [Terrum.Interop.NativeOutput(0)]
            extern int32 ReadValue(write ptr<int32> value);
            public int32 ReadAndReturn() {
                mut int32 value = 0;
                unsafe {
                    if (ReadValue(&value) == 0) { return -1; }
                }
                return value;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var semanticProducer = ModuleCompiler.CreateProducer(program, ["ReadAndReturn"]);
        var reference = ModuleCompiler.CreateReference(semanticProducer);
        Assert.That(reference.Functions.Select(static function => function.Name),
            Is.EqualTo(["ReadAndReturn"]));

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() { return ReadAndReturn(); }
            """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var nativeProducer = NativeModuleCompiler.CreateProducer(program, ["ReadAndReturn"],
            "DirectOutput", NativeRuntimePolicy.FailFast);
        var info = NativeModuleCompiler.Inspect(nativeProducer);
        Assert.That(info.IsConsumer, Is.False);
        var packageText = Encoding.ASCII.GetString(nativeProducer);
        Assert.That(packageText, Does.Contain("example.dll"));
        Assert.That(packageText, Does.Contain("ReadValue"));
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(consumerCompilation.Program!,
            nativeProducer, NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.Validate(nativeConsumer, nativeProducer);
    }

    [Test]
    public void DirectNativeOutputCanBeCombinedWithOneNativeProducerDependency()
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 ProviderValue() { return 40; }"));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(providerCompilation.Program!,
            ["ProviderValue"], "ProviderValue", NativeRuntimePolicy.FailFast);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            [Terrum.Interop.NativeOutput(0)]
            public extern int32 ReadValue(write ptr<int32> value);

            int32 Main() {
                mut int32 value = 0;
                unsafe {
                    if (ReadValue(&value) == 0) { return ProviderValue() + value; }
                }
                return -1;
            }
            """), NativeModuleCompiler.CreateReference(provider));
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));

        var consumer = NativeModuleCompiler.CreateConsumer(consumerCompilation.Program!, provider,
            NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.Validate(consumer, provider);

        var packageText = Encoding.ASCII.GetString(consumer);
        Assert.That(packageText, Does.Contain("example.dll"));
        Assert.That(packageText, Does.Contain("ProviderValue.dll"));
        Assert.That(packageText, Does.Contain("ReadValue"));
    }

    [Test]
    public void PrivateSemanticModuleRevalidatesNativeOutputMetadata()
    {
        var source = """
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            [Terrum.Interop.NativeOutput(0)]
            extern int32 ReadValue(write ptr<int32> value);
            public int32 ReadAndReturn() {
                mut int32 value = 0;
                unsafe {
                    if (ReadValue(&value) == 0) { return -1; }
                }
                return value;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = ModuleCompiler.CreateProducer(compilation.Program!, ["ReadAndReturn"]);
        var malformed = artifact.ToArray();
        var library = Encoding.UTF8.GetBytes("example.dll");
        var libraryOffset = IndexOf(malformed, library);
        Assert.That(libraryOffset, Is.GreaterThanOrEqualTo(sizeof(int)));
        var exportLengthOffset = libraryOffset + library.Length;
        var exportLength = BinaryPrimitives.ReadInt32LittleEndian(
            malformed.AsSpan(exportLengthOffset, sizeof(int)));
        var outputCountOffset = exportLengthOffset + sizeof(int) + exportLength;
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            malformed.AsSpan(outputCountOffset, sizeof(int))), Is.EqualTo(1));
        var resultConditionOffset = outputCountOffset + sizeof(int) + sizeof(int) + sizeof(int);
        BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(resultConditionOffset, sizeof(uint)), 2);

        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed));
    }

    [Test]
    public void DirectPointerParameterMustDeclareItsOutputValidity()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            extern int32 ReadValue(write ptr<int32> value);
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True);
    }

    [Test]
    public void SourceAuthoredNestedPointerOutputUsesAnOpaquePointerValue()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(OpaquePointerImportDeclaration + """
            int32 Main() {
                mut ptr<void> result = null;
                unsafe {
                    if (AcquireInterface(&result) == 0) { return -1; }
                }
                return 0;
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var image = NativeEmitter.EmitWithNativeImports(
            compilation.Program!, NativeRuntimePolicy.FailFast);
        Assert.That(Encoding.ASCII.GetString(image), Does.Contain("AcquireInterface"));
    }

    [Test]
    public void TrRmNativeImportRoundTripsNestedPointerOutput()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse(OpaquePointerImportDeclaration));
        Assert.That(provider.HasErrors, Is.False,
            string.Join(Environment.NewLine, provider.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(provider.Program!, "opaque-interface-import");

        var reference = TrRmExecutableCompiler.CreateReference(artifact);
        var consumer = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut ptr<void> result = null;
                unsafe {
                    if (AcquireInterface(&result) == 0) { return -1; }
                }
                return 0;
            }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        var image = NativeEmitter.EmitWithNativeImports(
            consumer.Program!, NativeRuntimePolicy.FailFast);
        Assert.That(Encoding.ASCII.GetString(image), Does.Contain("AcquireInterface"));
    }

    [Test]
    public void TrRmNativeImportRoundTripsIntoAnUnsafeSourceConsumer()
    {
        var artifact = EmitPublicImportArtifact();
        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x0004, out var featureBytes, out var featureCount), Is.True);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount);
        Assert.That(features.GetFeature(1),
            Is.EqualTo(new TrRmFeatureRow(
                TrRmFeatureTableReader.WindowsNamedNativeImportsFeatureId,
                TrRmFeatureTableReader.WindowsNamedNativeImportsRuleRevision)));
        Assert.That(index.TryGetSection(0x0021, out _, out var nativeImportCount), Is.True);
        Assert.That(nativeImportCount, Is.EqualTo(1));

        var reference = TrRmExecutableCompiler.CreateReference(artifact);
        var consumer = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int64 counter = 0;
                unsafe {
                    if (QueryPerformanceCounter(&counter) == 0) { return 1; }
                }
                return 0;
            }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        var image = NativeEmitter.EmitWithNativeImports(
            consumer.Program!, NativeRuntimePolicy.FailFast);
        Assert.That(Encoding.ASCII.GetString(image), Does.Contain("QueryPerformanceCounter"));
        Assert.That(Encoding.ASCII.GetString(image), Does.Contain("KERNEL32.dll"));
    }

    [Test]
    public void TrRmNativeImportRejectsMissingUnsafeEffect()
    {
        var artifact = EmitPublicImportArtifact();
        var malformed = MutateVariableRowByte(artifact, 0x001B, 1, 8, 0);

        Assert.Throws<InvalidDataException>(() => TrRmStructuralMetadataValidator.Validate(malformed));
    }

    [Test]
    public void TrRmNativeImportRejectsReadonlyOutputPointer()
    {
        var artifact = EmitPublicImportArtifact();
        var malformed = RemoveNativeOutputWriteQualifier(artifact);

        Assert.Throws<InvalidDataException>(() => TrRmStructuralMetadataValidator.Validate(malformed));
    }

    [Test]
    public void TrRmNativeImportRejectsMissingRequiredFeature()
    {
        var artifact = EmitPublicImportArtifact();
        var malformed = MutateFixedSectionByte(artifact, 0x0004, 0, 1);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    private static byte[] EmitPublicImportArtifact()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse(PublicImportDeclaration));
        Assert.That(provider.HasErrors, Is.False,
            string.Join(Environment.NewLine, provider.Diagnostics));
        return TrRmExecutableCompiler.Emit(provider.Program!, "windows-clock");
    }

    private static int IndexOf(byte[] source, byte[] value)
    {
        for (var index = 0; index <= source.Length - value.Length; index++)
        {
            if (source.AsSpan(index, value.Length).SequenceEqual(value))
            {
                return index;
            }
        }
        return -1;
    }

    private static byte[] MutateFixedSectionByte(byte[] artifact, ushort sectionKind,
        int byteOffset, byte value)
    {
        var (sectionOffset, count) = FindSection(artifact, sectionKind);
        var result = artifact.ToArray();
        if ((uint)byteOffset >= (uint)count * 8)
        {
            throw new ArgumentOutOfRangeException(nameof(byteOffset));
        }
        result[sectionOffset + byteOffset] = value;
        return result;
    }

    private static byte[] MutateVariableRowByte(byte[] artifact, ushort sectionKind,
        int row, int byteOffset, byte value)
    {
        var (sectionOffset, count) = FindSection(artifact, sectionKind);
        if (row <= 0 || row > count)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        var offsetTableBytes = checked((count + 1) * sizeof(uint));
        var first = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            artifact.AsSpan(sectionOffset + (row - 1) * sizeof(uint), sizeof(uint))));
        var last = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            artifact.AsSpan(sectionOffset + row * sizeof(uint), sizeof(uint))));
        if ((uint)byteOffset >= (uint)(last - first))
        {
            throw new ArgumentOutOfRangeException(nameof(byteOffset));
        }

        var result = artifact.ToArray();
        result[sectionOffset + offsetTableBytes + first + byteOffset] = value;
        return result;
    }

    private static byte[] RemoveNativeOutputWriteQualifier(byte[] artifact)
    {
        var (sectionOffset, count) = FindSection(artifact, 0x0011);
        var offsetTableBytes = checked((count + 1) * sizeof(uint));
        for (var row = 1; row <= count; row++)
        {
            var first = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                artifact.AsSpan(sectionOffset + (row - 1) * sizeof(uint), sizeof(uint))));
            var last = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                artifact.AsSpan(sectionOffset + row * sizeof(uint), sizeof(uint))));
            var typeOffset = sectionOffset + offsetTableBytes + first;
            if (last - first == 4 && artifact[typeOffset] == 1
                && artifact[typeOffset + 1] == 1 && artifact[typeOffset + 2] == 0x8C)
            {
                var result = artifact.ToArray();
                result[typeOffset + 1] = 0;
                return result;
            }
        }

        throw new InvalidOperationException("The emitted artifact has no writable native output pointer.");
    }

    private static (int Offset, int Count) FindSection(byte[] artifact, ushort sectionKind)
    {
        var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(12, sizeof(uint))));
        for (var entry = 0; entry < count; entry++)
        {
            var directoryOffset = 32 + entry * 16;
            if (BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(directoryOffset, sizeof(ushort)))
                == sectionKind)
            {
                return (
                    checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                        artifact.AsSpan(directoryOffset + 8, sizeof(uint)))),
                    checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                        artifact.AsSpan(directoryOffset + 4, sizeof(uint)))));
            }
        }

        throw new InvalidOperationException($"The emitted artifact has no section 0x{sectionKind:X4}.");
    }
}
