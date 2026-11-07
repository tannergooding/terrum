// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmForeignInvocationAddressEmitterTests
{
    private static readonly string[] s_initializerCallNames = ["MutateAndGet", "RecordPair"];
    private static readonly string[] s_selectedRecordCallNames = ["RecordFirst", "RecordSecond"];

    private const string A2EUScalarFrozenValuesSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1";
    private const string A2R1RepeatInitializationSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-repeat-initialization-1";

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int NativeAddressExport();

    private static CompilationOptions WithLayoutRegistration(
        CompilationOptions options)
    {
        if (options.LayoutCoreRegistration is not null)
        {
            return options;
        }

        var layoutRegistration
            = options.NativeCoreRegistration is NativeCoreRegistrationSet native
                ? NativeCoreRegistrationTests.CreateLayoutRegistration(native)
                : LayoutCoreRegistrationTestData.Create();
        return options with { LayoutCoreRegistration = layoutRegistration };
    }

    [Test]
    public async Task A2R1RepeatRoundTripsCapturedArrayFieldWrites()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string moduleIdentity = "a2r1-repeat-array-field-write";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct Word {
                public mut uint8 Low;
                public mut uint32 Wide;
            }

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<uint32> source,
                uint64 byteCount);

            int32 Main() {
                init mut Word seed;
                unsafe { seed.Wide = uint32(0x11223344); }
                mut [Word; 2] values = [seed; 2];
                mut int32 observedWide = 0;
                mut uint64 selectedIndex = 1;
                uint64 byteCount = 4;
                unsafe {
                    values[1].Low = uint8(0xAA);
                    uint32 source = values[selectedIndex].Wide;
                    var callback = &RtlMoveMemory;
                    callback(&observedWide, &source, byteCount);
                }
                return observedWide;
            }
            """;
        var nativeRegistration
            = NativeCoreRegistrationTests.CreateRegistration();
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration = nativeRegistration,
                LayoutCoreRegistration = NativeCoreRegistrationTests
                    .CreateLayoutRegistration(nativeRegistration),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, A2R1RepeatInitializationSchemaIdentity, 1);
        AssertA2R1RepeatBodyRecords(artifact, moduleIdentity,
            expectedRepeatCopies: 1);
        var input = new TrRmStandardArtifactInput(
            A2R1RepeatInitializationSchemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, A2R1RepeatInitializationSchemaIdentity, 1),
            Is.EqualTo(artifact));

        var fieldWrites = new List<BoundNativeFixedArrayPathWrite>();
        var loadedCaller = loaded.Functions.Single(
            static function => function.Name == "Main");
        Assert.That(BoundTreeTraversal.VisitStatements(
            loadedCaller.Body, statement =>
            {
                if (statement is BoundExpressionStatement {
                    Expression: BoundNativeFixedArrayPathWrite fieldWrite,
                })
                {
                    fieldWrites.Add(fieldWrite);
                }
            }), Is.True);
        var fieldWrite = fieldWrites.Single();
        Assert.That(fieldWrite.Projections.Length, Is.EqualTo(1));
        Assert.That(fieldWrite.LeafFields.Length, Is.EqualTo(1));
        Assert.That(fieldWrite.Value.Type, Is.EqualTo(PrimitiveType.UInt8));
        Assert.That(fieldWrite.IsUnsafeAuthorized, Is.True);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "RtlMoveMemory").DirectNativeImport,
            Is.Not.Null);
        var jitResult = NativeJit.Execute(loaded,
            NativeRuntimePolicy.FailFast);
        var compilerPath = typeof(TrRmExecutableCompiler).Assembly.Location;
        var compilerHash = Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(compilerPath)
                .ConfigureAwait(true)));
        TestContext.WriteLine(
            $"A2R1+Repeat source-free JIT: target=WindowsX64; policy=FailFast; schema={A2R1RepeatInitializationSchemaIdentity}; module={moduleIdentity}; expected=0x112233AA; actual=0x{jitResult:X}; source-sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))}; artifact-sha256={Convert.ToHexString(SHA256.HashData(artifact))}; compiler={compilerPath}; compiler-sha256={compilerHash}.");
        Assert.That(jitResult, Is.EqualTo(0x112233AA));

        await AssertNativeImageExitCodeAsync(
            NativeEmitter.EmitWithNativeImports(program,
                NativeRuntimePolicy.FailFast),
            $"{moduleIdentity}-source", 0x112233AA).ConfigureAwait(true);
        var image = NativeModuleCompiler.EmitStandardExecutable(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast);
        Assert.That(image, Is.Not.Empty);
        await AssertNativeImageExitCodeAsync(image, moduleIdentity, 0x112233AA)
            .ConfigureAwait(true);
    }

    [Test]
    public async Task A2R1RepeatRoundTripsDirectlyNestedRepeatSeeds()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string moduleIdentity = "a2r1-directly-nested-repeat-seeds";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct Word {
                public mut uint8 Low;
                public mut uint32 Wide;
            }

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<uint32> source,
                uint64 byteCount);

            int32 Main() {
                init mut Word seed;
                unsafe { seed.Wide = uint32(0x11223344); }
                mut [[Word; 2]; 2] rows = [[seed; 2]; 2];
                mut int32 observed = 0;
                mut uint64 rowIndex = 1;
                mut uint64 elementIndex = 1;
                uint64 byteCount = 4;
                unsafe {
                    rows[1][1].Low = uint8(0xAA);
                    uint32 source = rows[rowIndex][elementIndex].Wide;
                    var callback = &RtlMoveMemory;
                    callback(&observed, &source, byteCount);
                }
                return observed;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            WithLayoutRegistration(new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration
                    = NativeCoreRegistrationTests.CreateRegistration(),
            }));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, A2R1RepeatInitializationSchemaIdentity, 1);
        AssertA2R1RepeatBodyRecords(artifact, moduleIdentity,
            expectedRepeatCopies: 2);
        var input = new TrRmStandardArtifactInput(
            A2R1RepeatInitializationSchemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        var jitResult = NativeJit.Execute(loaded,
            NativeRuntimePolicy.FailFast);
        Assert.That(jitResult, Is.EqualTo(0x112233AA));
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, A2R1RepeatInitializationSchemaIdentity, 1);
        AssertA2R1RepeatBodyRecords(reemitted, moduleIdentity,
            expectedRepeatCopies: 2);
        var reemittedInput = new TrRmStandardArtifactInput(
            A2R1RepeatInitializationSchemaIdentity, reemitted);
        var reloaded = TrRmExecutableCompiler.LoadStandard(reemittedInput,
            moduleIdentity, []);
        Assert.That(TrRmExecutableCompiler.EmitStandard(reloaded,
                moduleIdentity, A2R1RepeatInitializationSchemaIdentity, 1),
            Is.EqualTo(reemitted));
        Assert.That(NativeJit.Execute(reloaded,
                NativeRuntimePolicy.FailFast),
            Is.EqualTo(0x112233AA));
        var image = NativeModuleCompiler.EmitStandardExecutable(reemittedInput,
            moduleIdentity, [], NativeRuntimePolicy.FailFast);
        await AssertNativeImageExitCodeAsync(image, moduleIdentity,
            0x112233AA)
            .ConfigureAwait(true);
    }

    [Test]
    public async Task SourceFreeStandardOverAlignedPackedFieldsPreserveNeighborsAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-local-aggregate-layout-execution-1";
        const string moduleIdentity = "standard-overaligned-packed-fields";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1, Alignment: 32)]
            public struct Packet {
                public mut uint8 Tag;
                public mut uint64 Middle;
                public mut uint8 Tail;
                internal Packet(uint8 tag, uint64 middle, uint8 tail) {
                    Tag = tag; Middle = middle; Tail = tail;
                }
            }
            int32 Main() {
                mut Packet first = new Packet(uint8(17),
                    uint64(1234567890123456789), uint8(29));
                first.Middle = uint64(987654321098765432);
                if (first.Tag != uint8(17)
                    || first.Middle != uint64(987654321098765432)
                    || first.Tail != uint8(29)) {
                    return 1;
                }
                return 0;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, schemaIdentity, 1);
        var input = new TrRmStandardArtifactInput(
            schemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1);
        var reemitted = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemittedArtifact),
            moduleIdentity, []);
        var loadedPacket = reemitted.Structures.Single(
            static structure => structure.Name == "Packet");
        Assert.That((loadedPacket.Packing, loadedPacket.DeclaredAlignment,
            loadedPacket.Alignment, loadedPacket.RequiredAlignment),
            Is.EqualTo((1, (int?)32, 32, 32)));

        Assert.That(NativeJit.Execute(loaded,
            NativeRuntimePolicy.FailFast), Is.Zero);
        Assert.That(NativeJit.Execute(reemitted,
            NativeRuntimePolicy.FailFast), Is.Zero);
        var image = NativeEmitter.Emit(reemitted,
            NativeRuntimePolicy.FailFast);
        await AssertNativeImageExitCodeAsync(image, moduleIdentity, 0)
            .ConfigureAwait(true);
    }

    [Test]
    public async Task A2R1RepeatRoundTripsCapturedArrayFieldRootWrites()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string moduleIdentity = "a2r1-repeat-array-field-root-write";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct Word {
                public mut uint8 Low;
                public mut uint32 Wide;
            }

            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public mut [Word; 2] Values;
            }

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<uint32> source,
                uint64 byteCount);

            int32 Main() {
                init mut Word seed;
                unsafe { seed.Wide = uint32(0x11223344); }
                init mut Holder holder;
                holder.Values = [seed; 2];
                mut int32 observedWide = 0;
                mut uint64 selectedIndex = 1;
                uint64 byteCount = 4;
                unsafe {
                    holder.Values[selectedIndex].Low = uint8(0xAA);
                    uint32 source = holder.Values[selectedIndex].Wide;
                    var callback = &RtlMoveMemory;
                    callback(&observedWide, &source, byteCount);
                }
                return observedWide;
            }
            """;
        var nativeRegistration
            = NativeCoreRegistrationTests.CreateRegistration();
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration = nativeRegistration,
                LayoutCoreRegistration = NativeCoreRegistrationTests
                    .CreateLayoutRegistration(nativeRegistration),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, A2R1RepeatInitializationSchemaIdentity, 1);
        AssertA2R1RepeatBodyRecords(artifact, moduleIdentity,
            expectedRepeatCopies: 1);
        var input = new TrRmStandardArtifactInput(
            A2R1RepeatInitializationSchemaIdentity, artifact);
        foreach (var fieldSelectionIndex in new[] { 0, 1 })
        {
            var malformedFieldWrite = RewriteA2BodyArtifact(artifact,
                "Main", (_, _, records, _) =>
                {
                    var writableFieldSelections = records.Where(
                        static record => record.Length == 24
                            && record[0] == 0x04
                            && record[4] == 2 && record[5] == 2)
                        .ToArray();
                    Assert.That(writableFieldSelections,
                        Has.Length.EqualTo(2));
                    writableFieldSelections[fieldSelectionIndex][5] = 1;
                }, isRepeatInitialization: true);
            _ = Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.LoadStandard(
                    new TrRmStandardArtifactInput(
                        A2R1RepeatInitializationSchemaIdentity,
                        malformedFieldWrite), moduleIdentity, []))!;
        }
        var malformedFieldOwner = RewriteA2BodyArtifact(artifact, "Main",
            (_, _, records, _) =>
            {
                var writableFieldSelections = records.Where(
                    static record => record.Length == 24
                        && record[0] == 0x04
                        && record[4] == 2 && record[5] == 2)
                    .ToArray();
                Assert.That(writableFieldSelections,
                    Has.Length.EqualTo(2));
                writableFieldSelections[1].AsSpan(16, sizeof(uint))
                    .CopyTo(writableFieldSelections[0].AsSpan(16));
            }, isRepeatInitialization: true,
            preserveInvalidBodyRecords: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(
                    A2R1RepeatInitializationSchemaIdentity,
                    malformedFieldOwner), moduleIdentity, []))!;
        var malformedRootHome = RewriteA2BodyArtifact(artifact, "Main",
            (_, _, records, _) =>
            {
                var rootFieldSelection = records.Single(
                    static record => record.Length == 24
                        && record[0] == 0x04
                        && record[4] == 2 && record[5] == 2
                        && record[7] == 2);
                BinaryPrimitives.WriteUInt16LittleEndian(
                    rootFieldSelection.AsSpan(10), ushort.MaxValue);
            }, isRepeatInitialization: true,
            preserveInvalidBodyRecords: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(
                    A2R1RepeatInitializationSchemaIdentity,
                    malformedRootHome), moduleIdentity, []))!;
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, A2R1RepeatInitializationSchemaIdentity, 1),
            Is.EqualTo(artifact));

        BoundNativeFixedArrayPathWrite? fieldWrite = null;
        var loadedMain = loaded.Functions.Single(
            static function => function.Name == "Main");
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundNativeFixedArrayPathWrite write)
                {
                    fieldWrite = write;
                }
            }), Is.True);
        Assert.That(fieldWrite, Is.Not.Null);
        Assert.That(fieldWrite!.FieldPlace, Is.Not.Null);
        Assert.That(fieldWrite.Projections, Has.Length.EqualTo(1));
        Assert.That(fieldWrite.LeafFields, Has.Length.EqualTo(1));

        var jitResult = NativeJit.Execute(loaded,
            NativeRuntimePolicy.FailFast);
        var compilerPath = typeof(TrRmExecutableCompiler).Assembly.Location;
        var compilerHash = Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(compilerPath)
                .ConfigureAwait(true)));
        TestContext.WriteLine(
            $"A2R1+Repeat field-root source-free JIT: target=WindowsX64; policy=FailFast; schema={A2R1RepeatInitializationSchemaIdentity}; module={moduleIdentity}; expected=0x112233AA; actual=0x{jitResult:X}; source-sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))}; artifact-sha256={Convert.ToHexString(SHA256.HashData(artifact))}; compiler={compilerPath}; compiler-sha256={compilerHash}.");
        Assert.That(jitResult, Is.EqualTo(0x112233AA));

        var sourceImage = NativeEmitter.EmitWithNativeImports(program,
            NativeRuntimePolicy.FailFast);
        await AssertNativeImageExitCodeAsync(sourceImage,
            $"{moduleIdentity}-source", 0x112233AA).ConfigureAwait(true);
        var image = NativeModuleCompiler.EmitStandardExecutable(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast);
        Assert.That(image, Is.Not.Empty);
        await AssertNativeImageExitCodeAsync(image, moduleIdentity,
            0x112233AA).ConfigureAwait(true);

        var outOfBoundsSource = source.Replace(
            "mut uint64 selectedIndex = 1;",
            "mut uint64 selectedIndex = 2;", StringComparison.Ordinal);
        var outOfBoundsNativeRegistration
            = NativeCoreRegistrationTests.CreateRegistration();
        var outOfBoundsCompilation = Compilation.Create(
            SyntaxTree.Parse(outOfBoundsSource),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration = outOfBoundsNativeRegistration,
                LayoutCoreRegistration = NativeCoreRegistrationTests
                    .CreateLayoutRegistration(outOfBoundsNativeRegistration),
            });
        Assert.That(outOfBoundsCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                outOfBoundsCompilation.Diagnostics));
        var outOfBoundsModuleIdentity = $"{moduleIdentity}-out-of-bounds";
        var outOfBoundsArtifact = TrRmExecutableCompiler.EmitStandard(
            outOfBoundsCompilation.Program!, outOfBoundsModuleIdentity,
            A2R1RepeatInitializationSchemaIdentity, 1);
        await AssertStandardA2SourceFreeJitAndAotAsync(
            outOfBoundsModuleIdentity,
            new TrRmStandardArtifactInput(
                A2R1RepeatInitializationSchemaIdentity,
                outOfBoundsArtifact),
            outOfBoundsSource, unchecked((int)0xE0540004),
            "field-rooted array path bounds failure",
            runJit: false).ConfigureAwait(true);
    }

    [Test]
    public async Task A2R1RepeatFieldRootWritesSurviveBranchRemapping()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string moduleIdentity = "a2r1-repeat-field-root-branch";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct Word {
                public mut uint8 Low;
                public mut uint32 Wide;
            }

            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public mut [Word; 2] Values;
            }

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<uint32> source,
                uint64 byteCount);

            int32 Main() {
                init mut Word seed;
                unsafe { seed.Wide = uint32(0x11223344); }
                init mut Holder holder;
                holder.Values = [seed; 2];
                mut uint64 selectedIndex = 1;
                mut int32 observedWide = 0;
                uint64 byteCount = 4;
                unsafe {
                    if (selectedIndex == 1) {
                        holder.Values[selectedIndex].Low = uint8(0xAA);
                    } else {
                        holder.Values[0].Low = uint8(0xBB);
                    }
                    uint32 source = holder.Values[selectedIndex].Wide;
                    var callback = &RtlMoveMemory;
                    callback(&observedWide, &source, byteCount);
                }
                return observedWide;
            }
            """;

        foreach (var (index, expected) in new[] {
            (SelectedIndex: "1", Result: 0x112233AA),
            (SelectedIndex: "0", Result: 0x112233BB),
        })
        {
            var selectedSource = source.Replace(
                "mut uint64 selectedIndex = 1;",
                $"mut uint64 selectedIndex = {index};",
                StringComparison.Ordinal);
            var compilation = Compilation.Create(
                SyntaxTree.Parse(selectedSource),
                WithLayoutRegistration(new CompilationOptions {
                    RuntimePolicy = BoundRuntimePolicy.FailFast,
                    NativeCoreRegistration
                        = NativeCoreRegistrationTests.CreateRegistration(),
                }));
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            var selectedModuleIdentity = $"{moduleIdentity}-{index}";
            var artifact = TrRmExecutableCompiler.EmitStandard(
                compilation.Program!, selectedModuleIdentity,
                A2R1RepeatInitializationSchemaIdentity, 1);
            await AssertStandardA2SourceFreeJitAndAotAsync(
                selectedModuleIdentity,
                new TrRmStandardArtifactInput(
                    A2R1RepeatInitializationSchemaIdentity, artifact),
                selectedSource, expected,
                "field-rooted store across the selected branch")
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task A2R1RepeatFieldRootBodiesSurviveProviderRemappingAndReplacement()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity = A2R1RepeatInitializationSchemaIdentity;
        const string providerIdentity = "a2r1-field-root-provider";
        const string consumerIdentity = "a2r1-field-root-consumer";
        const string providerSource = """
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct Word {
                public mut uint8 Low;
                public mut uint32 Wide;
            }

            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public mut [Word; 2] Values;
            }

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            public extern void Observe(
                write ptr<int32> destination,
                ptr<uint32> source,
                uint64 byteCount);

            internal int32 ProviderBody() {
                init mut Word seed;
                unsafe { seed.Wide = uint32(0x11223344); }
                init mut Holder holder;
                mut uint64 selectedIndex = 1;
                unsafe {
                    holder.Values = [seed; 2];
                    holder.Values[selectedIndex].Low = uint8(0xAA);
                }
                mut int32 observed = 0;
                uint64 byteCount = 4;
                unsafe {
                    uint32 source = holder.Values[selectedIndex].Wide;
                    var callback = &Observe;
                    callback(&observed, &source, byteCount);
                }
                return observed;
            }
            """;
        const string consumerSource = """
            int32 Main() {
                mut int32 observed = 0;
                uint32 source = uint32(0x112233AA);
                uint64 byteCount = 4;
                unsafe {
                    var callback = &Observe;
                    callback(&observed, &source, byteCount);
                }
                return observed;
            }
            """;
        var coreNativeRegistration
            = NativeCoreRegistrationTests.CreateRegistration();
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: coreNativeRegistration,
            layoutCoreRegistration: NativeCoreRegistrationTests
                .CreateLayoutRegistration(coreNativeRegistration));

        TrRmStandardArtifactInput CompileProvider(string source,
            string identity)
        {
            var compilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(source), core);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    compilation.Diagnostics));
            var artifact = TrRmExecutableCompiler.EmitStandard(
                compilation.Program!, identity, schemaIdentity, 1);
            return new TrRmStandardArtifactInput(schemaIdentity, artifact);
        }

        void AssertProviderFieldWrite(TrRmStandardArtifactInput input,
            string identity, int expectedLeafField)
        {
            var loaded = TrRmExecutableCompiler.LoadStandard(input,
                identity, []);
            var body = loaded.Functions.Single(
                static function => function.Name == "ProviderBody");
            var fieldWrites = new List<BoundNativeFixedArrayPathWrite>();
            Assert.That(BoundTreeTraversal.VisitStatements(body.Body,
                statement =>
                {
                    if (statement is BoundExpressionStatement {
                        Expression: BoundNativeFixedArrayPathWrite write,
                    })
                    {
                        fieldWrites.Add(write);
                    }
                }), Is.True);
            Assert.That(fieldWrites, Has.Count.EqualTo(1));
            Assert.That(fieldWrites[0].FieldPlace, Is.Not.Null);
            Assert.That(fieldWrites[0].Projections, Has.Length.EqualTo(1));
            Assert.That(fieldWrites[0].LeafFields, Has.Length.EqualTo(1));
            Assert.That(fieldWrites[0].LeafFields[0].Field,
                Is.EqualTo(expectedLeafField));
            Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                    identity, schemaIdentity, 1),
                Is.EqualTo(input.Artifact.ToArray()));
        }

        var providerInput = CompileProvider(providerSource, providerIdentity);
        AssertProviderFieldWrite(providerInput, providerIdentity,
            expectedLeafField: 0);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, []);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse(consumerSource), providerReference,
            WithLayoutRegistration(new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration = coreNativeRegistration,
            }));
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerCompilation.Program!, consumerIdentity, schemaIdentity, 1);
        var consumerInput = new TrRmStandardArtifactInput(schemaIdentity,
            consumerArtifact);

        var auxiliaryInput = CompileProvider(providerSource,
            "a2r1-field-root-auxiliary-provider");
        var providerPermutations = (TrRmStandardArtifactInput[][])[
            [auxiliaryInput, providerInput],
            [providerInput, auxiliaryInput],
        ];
        foreach (var providers in providerPermutations)
        {
            var loaded = TrRmExecutableCompiler.LoadStandard(consumerInput,
                consumerIdentity, providers);
            var importedObserve = loaded.Functions.Single(
                static function => function.Name == "Observe");
            Assert.That(importedObserve.ModuleImport!.ProducerIdentity,
                Is.EqualTo(providerIdentity));
            Assert.That(importedObserve.ModuleImport.ExportKey,
                Is.EqualTo("Observe"));
            Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                    consumerIdentity, schemaIdentity, 1),
                Is.EqualTo(consumerArtifact));
            Assert.That(NativeModuleCompiler.ExecuteStandard(consumerInput,
                    consumerIdentity, providers,
                    NativeRuntimePolicy.FailFast),
                Is.EqualTo(0x112233AA));
        }

        var compatibleProviderSource = providerSource.Replace(
            "public mut uint8 Low;",
            "public mut uint32 Prefix;\npublic mut uint8 Low;",
            StringComparison.Ordinal);
        var compatibleProviderInput = CompileProvider(
            compatibleProviderSource, providerIdentity);
        Assert.That(compatibleProviderInput.Artifact.Span.SequenceEqual(
            providerInput.Artifact.Span), Is.False);
        AssertProviderFieldWrite(compatibleProviderInput, providerIdentity,
            expectedLeafField: 1);
        var compatibleProviders = new[] {
            auxiliaryInput, compatibleProviderInput,
        };
        var compatibleLoaded = TrRmExecutableCompiler.LoadStandard(
            consumerInput, consumerIdentity, compatibleProviders);
        Assert.That(TrRmExecutableCompiler.EmitStandard(compatibleLoaded,
                consumerIdentity, schemaIdentity, 1),
            Is.EqualTo(consumerArtifact));
        Assert.That(NativeModuleCompiler.ExecuteStandard(consumerInput,
                consumerIdentity, compatibleProviders,
                NativeRuntimePolicy.FailFast),
            Is.EqualTo(0x112233AA));
        var image = NativeModuleCompiler.EmitStandardExecutable(
            consumerInput, consumerIdentity, compatibleProviders,
            NativeRuntimePolicy.FailFast);
        await AssertNativeImageExitCodeAsync(image, consumerIdentity,
            0x112233AA).ConfigureAwait(true);
    }

    [Test]
    public async Task A2R1RepeatRoundTripsNestedCapturedArrayFieldWrites()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string moduleIdentity = "a2r1-repeat-nested-array-field-write";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct Word {
                public mut uint8 Low;
                public mut uint32 Wide;
            }

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<uint32> source,
                uint64 byteCount);

            int32 Main() {
                init mut Word seed;
                unsafe { seed.Wide = uint32(0x11223344); }
                mut [Word; 2] row = [seed; 2];
                mut [[Word; 2]; 2] values = [row; 2];
                mut int32 observedWide = 0;
                mut uint64 rowIndex = 1;
                mut uint64 elementIndex = 1;
                uint64 byteCount = 4;
                unsafe {
                    values[1][1].Low = uint8(0xAA);
                    uint32 source = values[rowIndex][elementIndex].Wide;
                    var callback = &RtlMoveMemory;
                    callback(&observedWide, &source, byteCount);
                }
                return observedWide;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            WithLayoutRegistration(new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration
                    = NativeCoreRegistrationTests.CreateRegistration(),
            }));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, A2R1RepeatInitializationSchemaIdentity, 1);
        AssertA2R1RepeatBodyRecords(artifact, moduleIdentity,
            expectedRepeatCopies: 2);
        var input = new TrRmStandardArtifactInput(
            A2R1RepeatInitializationSchemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, A2R1RepeatInitializationSchemaIdentity, 1),
            Is.EqualTo(artifact));

        var fieldWrites = new List<BoundNativeFixedArrayPathWrite>();
        var loadedCaller = loaded.Functions.Single(
            static function => function.Name == "Main");
        Assert.That(BoundTreeTraversal.VisitStatements(
            loadedCaller.Body, statement =>
            {
                if (statement is BoundExpressionStatement {
                    Expression: BoundNativeFixedArrayPathWrite fieldWrite,
                })
                {
                    fieldWrites.Add(fieldWrite);
                }
            }), Is.True);
        var fieldWrite = fieldWrites.Single();
        Assert.That(fieldWrite.Projections.Length, Is.EqualTo(2));
        Assert.That(fieldWrite.LeafFields.Length, Is.EqualTo(1));
        Assert.That(fieldWrite.Value.Type, Is.EqualTo(PrimitiveType.UInt8));

        var jitResult = NativeJit.Execute(loaded,
            NativeRuntimePolicy.FailFast);
        var compilerPath = typeof(TrRmExecutableCompiler).Assembly.Location;
        var compilerHash = Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(compilerPath)
                .ConfigureAwait(true)));
        TestContext.WriteLine(
            $"A2R1+Repeat nested source-free JIT: target=WindowsX64; policy=FailFast; schema={A2R1RepeatInitializationSchemaIdentity}; module={moduleIdentity}; expected=0x112233AA; actual=0x{jitResult:X}; source-sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))}; artifact-sha256={Convert.ToHexString(SHA256.HashData(artifact))}; compiler={compilerPath}; compiler-sha256={compilerHash}.");
        Assert.That(jitResult, Is.EqualTo(0x112233AA));
        var image = NativeModuleCompiler.EmitStandardExecutable(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast);
        await AssertNativeImageExitCodeAsync(image, moduleIdentity, 0x112233AA)
            .ConfigureAwait(true);
    }

    [Test]
    public void A2RejectsFieldPlaceLoadWithWriteAccess()
    {
        const string moduleIdentity = "a2-array-field-read-access";
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct Word {
                public mut uint8 Low;
                public mut uint32 Wide;
            }

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<uint32> source,
                uint64 byteCount);

            int32 Main() {
                init mut Word seed;
                unsafe { seed.Wide = uint32(0x11223344); }
                mut [Word; 2] values = [seed; 2];
                mut int32 observedWide = 0;
                mut uint64 selectedIndex = 1;
                uint64 byteCount = 4;
                unsafe {
                    values[1].Low = uint8(0xAA);
                    uint32 source = values[selectedIndex].Wide;
                    var callback = &RtlMoveMemory;
                    callback(&observedWide, &source, byteCount);
                }
                return observedWide;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            WithLayoutRegistration(new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration
                    = NativeCoreRegistrationTests.CreateRegistration(),
            }));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, A2R1RepeatInitializationSchemaIdentity, 1);
        var malformedArtifact = RewriteA2BodyArtifact(artifact, "Main",
            (_, _, records, _) =>
            {
                var fieldReadSelection = records.SingleOrDefault(
                    static record => record.Length == 24
                        && record[0] == 0x04
                        && record[4] == 2 && record[5] == 1);
                Assert.That(fieldReadSelection, Is.Not.Null);
                fieldReadSelection![5] = 2;
            }, isRepeatInitialization: true);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(
                    A2R1RepeatInitializationSchemaIdentity,
                    malformedArtifact), moduleIdentity, []));
    }

    private static void AssertA2R1RepeatBodyRecords(byte[] artifact,
        string moduleIdentity, int expectedRepeatCopies)
    {
        const string previousSchemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-1";
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(previousSchemaIdentity,
                    artifact),
                moduleIdentity, []));

        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                        artifact));
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                callerRow = row;
                break;
            }
        }

        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        var repeatCopyCount = 0;
        var fieldReplacementCount = 0;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record.Length != 20 || record[0] != 0x05)
            {
                continue;
            }

            var payload = record[4..];
            repeatCopyCount += payload[0] == 1
                && payload[2] == 1 && payload[3] == 6 ? 1 : 0;
            fieldReplacementCount += payload[0] == 1
                && payload[2] == 2 && payload[3] == 1 ? 1 : 0;
        }

        Assert.That(repeatCopyCount, Is.EqualTo(expectedRepeatCopies));
        Assert.That(fieldReplacementCount, Is.EqualTo(1));
    }

    private static async Task AssertNativeImageExitCodeAsync(byte[] image,
        string moduleIdentity, int expectedExitCode)
    {
        var temporaryDirectory = Path.Combine(AppContext.BaseDirectory, "tmp");
        _ = Directory.CreateDirectory(temporaryDirectory);
        var imagePath = Path.Combine(temporaryDirectory,
            $"terrum-{moduleIdentity}-{Guid.NewGuid():N}.exe");
        Process? process = null;
        try
        {
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            process = Process.Start(new ProcessStartInfo(imagePath) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException(
                "The Standard Repeat executable was not started.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(true);
            }
            catch (TimeoutException exception)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                throw new TimeoutException(
                    $"Standard Repeat executable exceeded its 10-second deadline. {await stdout.ConfigureAwait(true)}{Environment.NewLine}{await stderr.ConfigureAwait(true)}",
                    exception);
            }

            var output = await stdout.ConfigureAwait(true);
            var error = await stderr.ConfigureAwait(true);
            TestContext.WriteLine(
                $"Native AOT: module={moduleIdentity}; pid={process.Id}; expected=0x{expectedExitCode:X}; actual=0x{process.ExitCode:X}; image-sha256={Convert.ToHexString(SHA256.HashData(image))}; deadline=10s.");
            Assert.That(output, Is.Empty);
            Assert.That(error, Is.Empty);
            Assert.That(process.ExitCode, Is.EqualTo(expectedExitCode),
                $"Native image {moduleIdentity} exited with 0x{process.ExitCode:X8}.");
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                process.Dispose();
            }

            await TestFileCleanup.DeleteWithRetryAsync(imagePath)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task NativeImageExecutesRepeatedArrayFieldReplacement()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct Word {
                public mut uint8 Low;
                public mut uint32 Wide;
            }

            int32 Main() {
                init mut Word seed;
                unsafe { seed.Wide = uint32(0x11); }
                mut [Word; 2] values = [seed; 2];
                unsafe { values[1].Low = uint8(0xAA); }
                mut int32 result = 0;
                unsafe { result = int32(values[1].Low); }
                return result;
            }
            """), WithLayoutRegistration(new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        }));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        await AssertNativeImageExitCodeAsync(
            NativeEmitter.EmitWithNativeImports(program,
                NativeRuntimePolicy.FailFast),
            "native-repeat-array-field-replacement", 0xAA)
            .ConfigureAwait(true);
    }

    [Test]
    public async Task NativeImageExecutesScalarRepeatInitialization()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut [uint32; 2] values = [uint32(0x5A); 2];
                return int32(values[1]);
            }
            """), BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        await AssertNativeImageExitCodeAsync(
            NativeEmitter.EmitWithNativeImports(program,
                NativeRuntimePolicy.FailFast),
            "native-scalar-repeat-initialization", 0x5A)
            .ConfigureAwait(true);
    }

    [Test]
    public async Task NativeImageExecutesNestedRepeatedArrayFieldReplacement()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct Word {
                public mut uint8 Low;
                public mut uint32 Wide;
            }

            int32 Main() {
                init mut Word seed;
                unsafe { seed.Wide = uint32(0x11); }
                mut [[Word; 2]; 2] values = [[seed; 2]; 2];
                unsafe { values[1][1].Low = uint8(0xAA); }
                mut int32 result = 0;
                unsafe { result = int32(values[1][1].Low); }
                return result;
            }
            """), WithLayoutRegistration(new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        }));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        await AssertNativeImageExitCodeAsync(
            NativeEmitter.EmitWithNativeImports(program,
                NativeRuntimePolicy.FailFast),
            "native-nested-repeat-array-field-replacement", 0xAA)
            .ConfigureAwait(true);
    }

    [TestCase(0, 1)]
    [TestCase(2, 101)]
    public async Task NativeImageEvaluatesRepeatedArraySeedExactlyOnce(
        int elementCount, int expectedResult)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var repeatBody = elementCount == 0
            ? "mut [Word; 0] values = [NextSeed(); 0];"
            : """
                mut [Word; 2] values = [NextSeed(); 2];
                mut int32 result = 0;
                unsafe { result = int32(values[1].Wide); }
                """;
        var returnExpression = elementCount == 0
            ? "int32(calls)"
            : "int32(calls) * 100 + result";
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            [Blittable]
            [Layout(LayoutKind.Union)]
            public struct Word {
                public mut uint32 Wide;
            }

            [Terrum.Interop.NativeImport("kernel32.dll", "GetLastError")]
            internal extern uint32 GetLastError();

            [Terrum.Interop.NativeImport("kernel32.dll", "SetLastError")]
            internal extern void SetLastError(uint32 value);

            Word NextSeed() {
                mut uint32 value = 0;
                unsafe { value = GetLastError() + 1; SetLastError(value); }
                init mut Word seed;
                unsafe { seed.Wide = value; }
                return seed;
            }

            int32 Main() {
                unsafe { SetLastError(0); }
                {{repeatBody}}
                mut uint32 calls = 0;
                unsafe { calls = GetLastError(); }
                return {{returnExpression}};
            }
            """), WithLayoutRegistration(new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        }));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        await AssertNativeImageExitCodeAsync(
            NativeEmitter.EmitWithNativeImports(program,
                NativeRuntimePolicy.FailFast),
            $"native-repeat-seed-evaluated-once-{elementCount}",
            expectedResult)
            .ConfigureAwait(true);
    }

    private static void AssertHomeFirstUseTypeMap(
        TrRmRegisterBodyReader body)
    {
        var firstTypeMapIndexByToken
            = new Dictionary<uint, ushort>(body.TypeCount);
        for (var homeIndex = 0; homeIndex < body.HomeCount; homeIndex++)
        {
            var home = body.HomeMap.Slice(homeIndex * 8, 8);
            var typeMapIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(home[2..]);
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                body.TypeMap.Slice(typeMapIndex * sizeof(uint),
                    sizeof(uint)));
            if (!firstTypeMapIndexByToken.TryGetValue(token,
                    out var firstTypeMapIndex))
            {
                firstTypeMapIndex
                    = checked((ushort)firstTypeMapIndexByToken.Count);
                firstTypeMapIndexByToken.Add(token, firstTypeMapIndex);
            }

            Assert.That(typeMapIndex, Is.EqualTo(firstTypeMapIndex));
        }

        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] != 0x04)
            {
                continue;
            }

            var typeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                record[20..]);
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                body.TypeMap.Slice(typeMapIndex * sizeof(uint),
                    sizeof(uint)));
            if (!firstTypeMapIndexByToken.TryGetValue(token,
                    out var firstTypeMapIndex))
            {
                firstTypeMapIndex
                    = checked((ushort)firstTypeMapIndexByToken.Count);
                firstTypeMapIndexByToken.Add(token, firstTypeMapIndex);
            }

            Assert.That(typeMapIndex, Is.EqualTo(firstTypeMapIndex));
        }

        Assert.That(firstTypeMapIndexByToken.Count,
            Is.EqualTo(body.TypeCount));
    }

    [Test]
    public void ExplicitNativeImportConventionProducesAnInferredAddress()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void Observe();

            internal void Form() {
                unsafe { _ = &Observe; }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var target = program.Functions.Single(static function =>
            function.Name == "Observe");
        var form = program.Functions.Single(static function =>
            function.Name == "Form");
        var addresses = new List<BoundNativeFunctionAddress>();
        Assert.That(BoundTreeTraversal.VisitExpressions(form.Body, expression =>
        {
            if (expression is BoundNativeFunctionAddress address)
            {
                addresses.Add(address);
            }
        }), Is.True);

        var address = addresses.Single();
        Assert.That(address.Function, Is.EqualTo(
            Enumerable.Range(0, program.Functions.Length)
                .Single(index => program.Functions[index].Name == "Observe")));
        Assert.That(target.DirectNativeImport!.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Unmanaged));
        Assert.That(program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(address.Type)].CallingConvention,
            Is.EqualTo(NativeCallingConvention.Unmanaged));

        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(program, "native-address");
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        var targetRow = 0;
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            targetRow = name == "Observe" ? row : targetRow;
            callerRow = name == "Form" ? row : callerRow;
        }
        Assert.That(targetRow, Is.GreaterThan(0));
        Assert.That(callerRow, Is.GreaterThan(0));
        Assert.That(tables.StandardNativeImports.HasImportForMethod(targetRow), Is.True);
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        Assert.That(body.MapMask, Is.EqualTo((byte)0x06));
        Assert.That(body.MethodMap.Length, Is.EqualTo(sizeof(uint)));
        Assert.That(body.TypeMap.Length, Is.EqualTo(sizeof(uint)));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] { 0xE9, 0, 1, 0, 0x18, 0 }));
    }

    [Test]
    public void StandardNativeOutputDeclarationsEmitSharedSelectorContracts()
    {
        const string source = """
            [Terrum.Interop.NativeImport("example.dll", "Observe")]
            internal extern int32 Observe(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<uint32> value);

            internal void Form() {
                unsafe { _ = &Observe; }
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, "native-output-anchor",
            "trrm/1-draft-foreign-invocation-contracts-1", 1);
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        var targetRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Observe")
            {
                targetRow = row;
                break;
            }
        }
        var anchorRow = tables.Methods.Count;

        Assert.That(TrRmFeatureManifestValidator.Requires(tables.Features,
            tables.Modules, 1,
            TrRmFeatureTableReader.NativeOutputCellsCandidateFeatureId),
            Is.True);
        Assert.That(TrRmFeatureManifestValidator.Requires(tables.Features,
            tables.Modules, 1,
            TrRmFeatureTableReader.PublishedPredicatesFeatureId), Is.True);
        Assert.That(tables.StandardNativeImports.HasImportForMethod(targetRow),
            Is.True);
        var targetOutputStates
            = tables.Contracts.GetOutputStateContracts(targetRow);
        Assert.That(targetOutputStates.Select(static output => output.State),
            Is.EqualTo(new byte[] { 1, 0 }));
        var outputDependencies
            = tables.Contracts.GetDependencyContracts(targetRow);
        Assert.That(outputDependencies, Has.Length.EqualTo(1));
        Assert.That(outputDependencies[0],
            Is.EqualTo(new TrRmDependencyContract(2, 0, 0, 1, 0, 0)));
        Assert.That(index.TryGetSection(0x003B, out var predicateNodeBytes,
            out var predicateNodeCount), Is.True);
        Assert.That(predicateNodeCount, Is.EqualTo(4));
        var predicateNodeKinds = new uint[checked((int)predicateNodeCount)];
        for (var node = 0; node < predicateNodeKinds.Length; node++)
        {
            predicateNodeKinds[node] = BinaryPrimitives.ReadUInt32LittleEndian(
                predicateNodeBytes.Slice(node * 16, sizeof(uint)));
        }
        Assert.That(predicateNodeKinds, Is.EqualTo(new uint[] { 3, 4, 3, 4 }));
        foreach (var method in new[] { targetRow, anchorRow })
        {
            Assert.That(tables.Contracts.GetOutputStateContracts(method),
                Has.Length.EqualTo(2));
            Assert.That(tables.Contracts.GetPredicateGraphContracts(method),
                Has.Length.EqualTo(2));
            Assert.That(tables.Contracts.GetDependencyContracts(method),
                Has.Length.EqualTo(1));
        }

        var standardInput = new TrRmStandardArtifactInput(
            "trrm/1-draft-foreign-invocation-contracts-1", artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(standardInput,
            "native-output-anchor", []);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                "native-output-anchor",
                "trrm/1-draft-foreign-invocation-contracts-1", 1),
            Is.EqualTo(artifact));

        var a3Artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, "native-output-a3",
            "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1",
            1);
        TrRmA3ResourceLedgerTestHelper.AssertExactWork(ledger =>
        {
            var measured = TrRmExecutableCompiler
                .EmitForeignInvocationDraftNativeAddressLibrary(
                    compilation.Program!, "native-output-a3",
                    TrRmArtifactSchema
                        .ForeignInvocationStorageFixedArrayGenericCopyDraft,
                    a3LayoutResourceLedger: ledger);
            Assert.That(measured, Is.EqualTo(a3Artifact));
        });
        var a3Input = new TrRmStandardArtifactInput(
            "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1",
            a3Artifact);
        var a3Loaded = TrRmExecutableCompiler.LoadStandard(a3Input,
            "native-output-a3", []);
        Assert.That(TrRmExecutableCompiler.EmitStandard(a3Loaded,
                "native-output-a3",
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1",
                1),
            Is.EqualTo(a3Artifact));
    }

    [Test]
    public void StandardDirectNativeOutputCallsRoundTripReservations()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string moduleIdentity = "native-output-direct-call";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "Observe")]
            internal extern int32 Observe(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<uint32> value);

            public void Probe() {
                init mut uint32 value;
                unsafe { Observe(&init value); }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        var targetRow = 0;
        var callerRow = 0;
        for (var method = 1; method <= tables.Methods.Count; method++)
        {
            switch (tables.Methods.GetName(method))
            {
                case "Observe":
                    targetRow = method;
                    break;
                case "Probe":
                    callerRow = method;
                    break;
            }
        }

        Assert.That(targetRow, Is.EqualTo(1));
        Assert.That(callerRow, Is.EqualTo(2));
        Assert.That(tables.StandardNativeImports.HasImportForMethod(targetRow),
            Is.True);
        Assert.That(tables.Contracts.GetOutputStateContracts(targetRow),
            Has.Length.EqualTo(2));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        Assert.That(body.GetRecord(2)[0], Is.EqualTo(0x0C));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xDF, 0, 2, 0, 0xE0, 0, 1, 0, 0x18, 0,
            }));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var caller = loaded.Functions.Single(static function =>
            function.Name == "Probe");
        BoundNativeOutputReservation? reservation = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundNativeOutputReservation output)
                {
                    reservation = output;
                }
            }), Is.True);
        Assert.That(reservation, Is.Not.Null);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
        var image = NativeEmitter.EmitLibraryWithNativeImports(loaded,
            ["Probe"], NativeRuntimePolicy.FailFast);
        using var imageStream = new MemoryStream(image);
        using var imageReader = new PEReader(imageStream);
        Assert.That(imageReader.PEHeaders.PEHeader, Is.Not.Null);
    }

    [Test]
    public void StandardDirectNativeOutputCallsRoundTripSequentialInvokes()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string moduleIdentity = "native-output-sequential-calls";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "Observe")]
            internal extern int32 Observe(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<uint32> value);

            public void Probe() {
                init mut uint32 first;
                init mut uint32 second;
                unsafe {
                    Observe(&init first);
                    Observe(&init second);
                }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        tables.ValidateRegisterBodyMapsForMethod(2);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(2), tables.Contracts.Count);
        Assert.That(body.RecordCount, Is.EqualTo(5));
        Assert.That(body.GetRecord(3)[0], Is.EqualTo(0x0C));
        Assert.That(body.GetRecord(4)[0], Is.EqualTo(0x0C));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xDF, 0, 3, 0,
                0xE0, 0, 1, 0,
                0xDF, 0, 4, 0,
                0xE0, 0, 2, 0,
                0x18, 0,
            }));

        var crossedRecords
            = new List<ReadOnlyMemory<byte>>(body.RecordCount);
        for (var record = 0; record < body.RecordCount; record++)
        {
            crossedRecords.Add(body.GetRecord(record).ToArray());
        }

        var crossedReservation = crossedRecords[4].ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(crossedReservation.AsSpan(4),
            1);
        crossedRecords[4] = crossedReservation;
        var rejectedCrossCallReservation = false;
        try
        {
            _ = EncodeNativeAddressBody(body,
                body.TypeMap.ToArray(), body.MethodMap.ToArray(),
                body.ConstantMap.ToArray(), tables.Contracts.Count,
                TrRmArtifactSchema.ForeignInvocationDraft, crossedRecords);
        }
        catch (InvalidDataException)
        {
            rejectedCrossCallReservation = true;
        }

        Assert.That(rejectedCrossCallReservation, Is.True);

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var caller = loaded.Functions.Single(static function =>
            function.Name == "Probe");
        var outputCalls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundCall call
                    && call.Arguments.Any(static argument =>
                        argument is BoundNativeOutputReservation))
                {
                    outputCalls.Add(call);
                }
            }), Is.True);
        Assert.That(outputCalls, Has.Count.EqualTo(2));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
        var image = NativeEmitter.EmitLibraryWithNativeImports(loaded,
            ["Probe"], NativeRuntimePolicy.FailFast);
        using var imageStream = new MemoryStream(image);
        using var imageReader = new PEReader(imageStream);
        Assert.That(imageReader.PEHeaders.PEHeader, Is.Not.Null);
        Assert.That(imageReader.PEHeaders.PEHeader!.ImportTableDirectory.Size,
            Is.GreaterThan(0));
    }

    [Test]
    public void StorageAddressNativeOutputCallsRoundTripSequentialInvokes()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-addresses-1";
        const string moduleIdentity = "native-output-sequential-a2-calls";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [param(result): DependsOn(nameof(source))]
            [param(result): ForwardsBacking(nameof(source))]
            [Terrum.Interop.NativeImport("example.dll", "GetPointer")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            internal extern int32 GetPointer(
                ptr<int32> source,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<ptr<void>> result);

            public void Probe(int32 source) {
                int32 backing = source;
                init mut ptr<void> first;
                init mut ptr<void> second;
                unsafe {
                    GetPointer(&backing, &init first);
                    GetPointer(&backing, &init second);
                }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageAddressDraft(artifact));
        tables.ValidateRegisterBodyMapsForMethod(2);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(2), tables.Contracts.Count);
        Assert.That(body.RecordCount, Is.EqualTo(7));
        Assert.That(body.GetRecord(3)[0], Is.EqualTo(0x0E));
        Assert.That(body.GetRecord(4)[0], Is.EqualTo(0x0C));
        Assert.That(body.GetRecord(5)[0], Is.EqualTo(0x0E));
        Assert.That(body.GetRecord(6)[0], Is.EqualTo(0x0C));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 2, 0, 0xD6, 0x10, 3, 0,
                0xEC, 0, 3, 0, 0xDF, 0, 4, 0,
                0xE0, 0, 1, 0,
                0xEC, 0, 5, 0, 0xDF, 0, 6, 0,
                0xE0, 0, 2, 0,
                0x18, 0,
            }));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var target = loaded.Functions.Single(static function =>
            function.Name == "GetPointer");
        Assert.That(target.DirectNativeImport!.Parameters, Has.Length.EqualTo(1));
        Assert.That(target.NativeOutputContracts.Single().Dependencies,
            Is.EqualTo([
                new NativeOutputDependency(2,
                    NativeOutputDependencySourceKind.Formal,
                    NativeOutputDependencyRelationship.Forward,
                    NativeOutputPublicationPhase.NormalCompletion, 1, 0),
            ]));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
        var image = NativeEmitter.EmitLibraryWithNativeImports(loaded,
            ["Probe"], NativeRuntimePolicy.FailFast);
        using var imageStream = new MemoryStream(image);
        using var imageReader = new PEReader(imageStream);
        Assert.That(imageReader.PEHeaders.PEHeader, Is.Not.Null);
        Assert.That(imageReader.PEHeaders.PEHeader!.ImportTableDirectory.Size,
            Is.GreaterThan(0));
    }

    [Test]
    public void StandardDirectNativeOutputCallsRoundTripConditionalInvoke()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string moduleIdentity = "native-output-conditional-call";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "Observe")]
            internal extern int32 Observe(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<uint32> value);

            public void Probe(bool invoke) {
                init mut uint32 value;
                if (invoke) {
                    unsafe { Observe(&init value); }
                }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        tables.ValidateRegisterBodyMapsForMethod(2);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(2), tables.Contracts.Count);
        Assert.That(body.BlockCount, Is.EqualTo(4));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] { 0xD5, 0, 1, 0, 0xE4, 0, 3, 0 }));
        Assert.That(body.GetBlockCode(1).ToArray(),
            Is.EqualTo(new byte[] {
                0xDF, 0, 2, 0, 0xE0, 0, 1, 0, 0xE3, 0, 3, 0,
            }));
        Assert.That(body.GetBlockCode(2).ToArray(),
            Is.EqualTo(new byte[] { 0xE3, 0, 3, 0 }));
        Assert.That(body.GetBlockCode(3).ToArray(),
            Is.EqualTo(new byte[] { 0x18, 0 }));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
        var image = NativeEmitter.EmitLibraryWithNativeImports(loaded,
            ["Probe"], NativeRuntimePolicy.FailFast);
        using var imageStream = new MemoryStream(image);
        using var imageReader = new PEReader(imageStream);
        Assert.That(imageReader.PEHeaders.PEHeader, Is.Not.Null);
        Assert.That(imageReader.PEHeaders.PEHeader!.ImportTableDirectory.Size,
            Is.GreaterThan(0));
    }

    [TestCase("trrm/1-draft-foreign-invocation-contracts-1")]
    [TestCase("trrm/1-draft-foreign-invocation-storage-addresses-1")]
    public void StandardDirectNativeOutputCallsRoundTripNegatedBooleanCondition(
        string schemaIdentity)
    {
        const string moduleIdentity = "native-output-negated-boolean-condition";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "Observe")]
            internal extern int32 Observe(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<uint32> value);

            public void Probe(bool invoke) {
                init mut uint32 value;
                if (!invoke) {
                    unsafe { Observe(&init value); }
                }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var index = StringComparer.Ordinal.Equals(schemaIdentity,
                "trrm/1-draft-foreign-invocation-contracts-1")
            ? TrRmArtifactIndex.ForForeignInvocationDraft(artifact)
            : TrRmArtifactIndex
                .ForForeignInvocationStorageAddressDraft(artifact);
        var tables = StringComparer.Ordinal.Equals(schemaIdentity,
                "trrm/1-draft-foreign-invocation-contracts-1")
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationDraftAndGetExecutionTables(index)
            : TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(index);
        var callerRow = 0;
        for (var method = 1; method <= tables.Methods.Count; method++)
        {
            if (tables.Methods.GetName(method) == "Probe")
            {
                callerRow = method;
                break;
            }
        }
        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 1, 0,
                0xE4, 0, 3, 0,
            }));
        Assert.That(body.GetBlockCode(1).ToArray(),
            Is.EqualTo(new byte[] { 0xE3, 0, 3, 0 }));
        Assert.That(body.GetBlockCode(2).ToArray(),
            Is.EqualTo(new byte[] { 0xDF, 0, 2, 0, 0xE0, 0, 1, 0,
                0xE3, 0, 3, 0 }));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
    }

    [Test]
    [TestCase("trrm/1-draft-foreign-invocation-contracts-1", false)]
    [TestCase("trrm/1-draft-foreign-invocation-storage-addresses-1", true)]
    public void StandardDirectNativeOutputCallsRoundTripNestedConditionals(
        string schemaIdentity, bool storageAddressSchema)
    {
        const string moduleIdentity = "native-output-nested-conditionals";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "Observe")]
            internal extern int32 Observe(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<uint32> value);

            public void Probe(bool outer, bool inner) {
                init mut uint32 first;
                init mut uint32 second;
                init mut uint32 third;
                if (outer) {
                    if (inner) {
                        unsafe { Observe(&init first); }
                    }
                    else {
                        unsafe { Observe(&init second); }
                    }
                }
                else {
                    unsafe { Observe(&init third); }
                }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var index = storageAddressSchema
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageAddressDraft(artifact)
            : TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = storageAddressSchema
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(
                    index)
            : TrRmStructuralMetadataValidator
                .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        tables.ValidateRegisterBodyMapsForMethod(2);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(2), tables.Contracts.Count);
        Assert.That(body.BlockCount, Is.EqualTo(7));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var conditionals = new List<BoundIf>();
        Assert.That(BoundTreeTraversal.VisitStatements(
            loaded.Functions.Single(static function =>
                function.Name == "Probe").Body,
            statement =>
            {
                if (statement is BoundIf conditional)
                {
                    conditionals.Add(conditional);
                }
            }), Is.True);
        Assert.That(conditionals, Has.Count.EqualTo(2));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
        var image = NativeEmitter.EmitLibraryWithNativeImports(loaded,
            ["Probe"], NativeRuntimePolicy.FailFast);
        using var imageStream = new MemoryStream(image);
        using var imageReader = new PEReader(imageStream);
        Assert.That(imageReader.PEHeaders.PEHeader, Is.Not.Null);
        Assert.That(imageReader.PEHeaders.PEHeader!.ImportTableDirectory.Size,
            Is.GreaterThan(0));
    }

    [Test]
    public void StorageAddressNativeOutputCallsRoundTripConditionalInvoke()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-addresses-1";
        const string moduleIdentity = "native-output-conditional-a2-call";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [param(result): DependsOn(nameof(source))]
            [param(result): ForwardsBacking(nameof(source))]
            [Terrum.Interop.NativeImport("example.dll", "GetPointer")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            internal extern int32 GetPointer(
                ptr<int32> source,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<ptr<void>> result);

            public void Probe(int32 source, bool invoke) {
                int32 backing = source;
                init mut ptr<void> result;
                if (invoke) {
                    unsafe { GetPointer(&backing, &init result); }
                }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageAddressDraft(artifact));
        tables.ValidateRegisterBodyMapsForMethod(2);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(2), tables.Contracts.Count);
        Assert.That(body.BlockCount, Is.EqualTo(4));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 1, 0, 0xD6, 0x10, 3, 0,
                0xD5, 0, 2, 0, 0xE4, 0, 4, 0,
            }));
        Assert.That(body.GetBlockCode(1).ToArray(),
            Is.EqualTo(new byte[] {
                0xEC, 0, 2, 0, 0xDF, 0, 3, 0,
                0xE0, 0, 1, 0, 0xE3, 0, 3, 0,
            }));
        Assert.That(body.GetBlockCode(2).ToArray(),
            Is.EqualTo(new byte[] { 0xE3, 0, 3, 0 }));
        Assert.That(body.GetBlockCode(3).ToArray(),
            Is.EqualTo(new byte[] { 0x18, 0 }));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var target = loaded.Functions.Single(static function =>
            function.Name == "GetPointer");
        Assert.That(target.DirectNativeImport!.Parameters, Has.Length.EqualTo(1));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
        var image = NativeEmitter.EmitLibraryWithNativeImports(loaded,
            ["Probe"], NativeRuntimePolicy.FailFast);
        using var imageStream = new MemoryStream(image);
        using var imageReader = new PEReader(imageStream);
        Assert.That(imageReader.PEHeaders.PEHeader, Is.Not.Null);
        Assert.That(imageReader.PEHeaders.PEHeader!.ImportTableDirectory.Size,
            Is.GreaterThan(0));
    }

    [Test]
    public void StandardDirectAggregateNativeOutputCallsRoundTripLayouts()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string moduleIdentity = "native-output-aggregate-direct-call";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            internal struct Packet {
                public uint32 Kind;
                public uint32 Count;
            }

            [Terrum.Interop.NativeImport("example.dll", "Observe")]
            internal extern int32 Observe(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<Packet> value);

            internal void Probe() {
                init mut Packet value;
                unsafe { Observe(&init value); }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        Assert.That(tables.TypeDefs.Count, Is.EqualTo(1));
        Assert.That(tables.Layouts.Count, Is.EqualTo(2));
        Assert.That(tables.LayoutFields.Count, Is.EqualTo(4));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        Assert.That(loaded.Structures, Has.Length.EqualTo(1));
        Assert.That(loaded.Structures[0].Name, Is.EqualTo("Packet"));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
    }

    [Test]
    public void StandardDirectNativeOutputCallsRetainOrdinaryParameterHomes()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string moduleIdentity = "native-output-input-home";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "Observe")]
            internal extern int32 Observe(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<uint32> value,
                int32 selector);

            internal void Probe(int32 selector) {
                init mut uint32 value;
                unsafe { Observe(&init value, selector); }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(2), tables.Contracts.Count);
        var callSite = body.GetRecord(1);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            callSite[12..]), Is.EqualTo(2));
        Assert.That(callSite[20..28].ToArray(),
            Is.EqualTo(new byte[] { 2, 6, 0, 0, 2, 1, 1, 0 }));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var caller = loaded.Functions.Single(static function =>
            function.Name == "Probe");
        BoundRead? loadedInput = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundRead read && read.Variable == 0)
                {
                    loadedInput = read;
                }
            }), Is.True);
        Assert.That(loadedInput, Is.Not.Null);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
    }

    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public void StandardDirectNativeOutputCallsRoundTripLocalInputHome(
        bool copyThroughLocal)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string moduleIdentity = "native-output-local-input-home";
        var stagedInitialization = copyThroughLocal
            ? "int32 staged = selector;\nint32 stagedAgain = staged;"
            : "int32 staged = selector;";
        var stagedArgument = copyThroughLocal ? "stagedAgain" : "staged";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [Terrum.Interop.NativeImport("example.dll", "Observe")]
            internal extern int32 Observe(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<uint32> value,
                int32 selector);

            internal void Probe(int32 selector) {
                init mut uint32 value;
                {{stagedInitialization}}
                unsafe { Observe(&init value, {{stagedArgument}}); }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(2), tables.Contracts.Count);
        Assert.That(body.GetBlockCode(0)[0], Is.EqualTo(0xD5));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var caller = loaded.Functions.Single(static function =>
            function.Name == "Probe");
        var loadedLocals = new List<BoundLocal>();
        Assert.That(BoundTreeTraversal.VisitStatements(caller.Body,
            statement =>
            {
                if (statement is BoundLocal local)
                {
                    loadedLocals.Add(local);
                }
            }), Is.True);
        Assert.That(loadedLocals, Has.Count.EqualTo(
            copyThroughLocal ? 2 : 1));
        Assert.That(loadedLocals.All(static local =>
            local.Initializer is BoundRead), Is.True);
        var localFromFormal = loadedLocals.Single(local =>
            ((BoundRead)local.Initializer!).Variable == 0);
        var inputVariable = localFromFormal.Variable;
        if (copyThroughLocal)
        {
            inputVariable = loadedLocals.Single(local =>
                ((BoundRead)local.Initializer!).Variable
                    == localFromFormal.Variable).Variable;
        }

        BoundCall? loadedCall = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundCall call
                    && call.Arguments.Any(static argument =>
                        argument is BoundNativeOutputReservation))
                {
                    loadedCall = call;
                }
            }), Is.True);
        Assert.That(loadedCall, Is.Not.Null);
        Assert.That(loadedCall!.Arguments[1], Is.TypeOf<BoundRead>());
        Assert.That(((BoundRead)loadedCall.Arguments[1]).Variable,
            Is.EqualTo(inputVariable));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
    }

    [TestCase(SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, 0x02)]
    [TestCase(SourceArithmeticProfile.Release,
        BoundIntegerOverflowBehavior.Wrapping, 0x06)]
    public void StandardDirectNativeOutputCallsRoundTripComputedLocalInputHome(
        SourceArithmeticProfile profile,
        BoundIntegerOverflowBehavior overflowBehavior,
        byte arithmeticControl)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string moduleIdentity = "native-output-computed-local-input";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "Observe")]
            internal extern int32 Observe(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<uint32> value,
                int32 selector);

            internal void Probe(int32 selector) {
                init mut uint32 value;
                int32 adjusted = selector + 1;
                unsafe { Observe(&init value, adjusted); }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions()
                with { SourceArithmeticProfile = profile });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(2), tables.Contracts.Count);
        var code = body.GetBlockCode(0).ToArray();
        var arithmeticOffset = Array.IndexOf(code, (byte)0xC0);
        Assert.That(arithmeticOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(code[arithmeticOffset + 1],
            Is.EqualTo(arithmeticControl));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            code.AsSpan(arithmeticOffset + 2)), Is.EqualTo(0xF0F0));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var caller = loaded.Functions.Single(static function =>
            function.Name == "Probe");
        var locals = new List<BoundLocal>();
        Assert.That(BoundTreeTraversal.VisitStatements(caller.Body,
            statement =>
            {
                if (statement is BoundLocal local)
                {
                    locals.Add(local);
                }
            }), Is.True);
        var adjusted = locals.Single(static local =>
            local.Initializer is BoundBinary);
        var initializer = (BoundBinary)adjusted.Initializer!;
        Assert.That(initializer.Operator, Is.EqualTo(TokenKind.Minus));
        Assert.That(initializer.OverflowBehavior,
            Is.EqualTo(overflowBehavior));
        Assert.That(initializer.Left, Is.TypeOf<BoundRead>());
        Assert.That(initializer.Right, Is.TypeOf<BoundConstant>());
        Assert.That(((BoundConstant)initializer.Right).Value, Is.EqualTo(-1));
        BoundCall? call = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundCall candidate
                    && candidate.Arguments.Any(static argument =>
                        argument is BoundNativeOutputReservation))
                {
                    call = candidate;
                }
            }), Is.True);
        Assert.That(call, Is.Not.Null);
        Assert.That(call!.Arguments[1], Is.TypeOf<BoundRead>());
        Assert.That(((BoundRead)call.Arguments[1]).Variable,
            Is.EqualTo(adjusted.Variable));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
    }

    [TestCase(SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, 0x01, TokenKind.Plus,
        "int32", "left + right")]
    [TestCase(SourceArithmeticProfile.Release,
        BoundIntegerOverflowBehavior.Wrapping, 0x05, TokenKind.Plus,
        "int32", "left + right")]
    [TestCase(SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, 0x03, TokenKind.Star,
        "int32", "left * right")]
    [TestCase(SourceArithmeticProfile.Release,
        BoundIntegerOverflowBehavior.Wrapping, 0x07, TokenKind.Star,
        "int32", "left * right")]
    [TestCase(SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, 0x0C, TokenKind.Slash,
        "int32", "left / right")]
    [TestCase(SourceArithmeticProfile.Release,
        BoundIntegerOverflowBehavior.Checked, 0x0C, TokenKind.Slash,
        "int32", "left / right")]
    [TestCase(SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, 0x0D, TokenKind.Percent,
        "int32", "left % right")]
    [TestCase(SourceArithmeticProfile.Release,
        BoundIntegerOverflowBehavior.Checked, 0x0D, TokenKind.Percent,
        "int32", "left % right")]
    [TestCase(SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, 0x20, TokenKind.Plus,
        "float32", "left + right")]
    [TestCase(SourceArithmeticProfile.Release,
        BoundIntegerOverflowBehavior.Checked, 0x20, TokenKind.Plus,
        "float32", "left + right")]
    [TestCase(SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, 0x20, TokenKind.Plus,
        "float64", "left + right")]
    [TestCase(SourceArithmeticProfile.Release,
        BoundIntegerOverflowBehavior.Checked, 0x20, TokenKind.Plus,
        "float64", "left + right")]
    [TestCase(SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, 0x21, TokenKind.Minus,
        "float32", "left - right")]
    [TestCase(SourceArithmeticProfile.Release,
        BoundIntegerOverflowBehavior.Checked, 0x21, TokenKind.Minus,
        "float32", "left - right")]
    [TestCase(SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, 0x22, TokenKind.Star,
        "float32", "left * right")]
    [TestCase(SourceArithmeticProfile.Release,
        BoundIntegerOverflowBehavior.Checked, 0x22, TokenKind.Star,
        "float32", "left * right")]
    [TestCase(SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, 0x23, TokenKind.Slash,
        "float32", "left / right")]
    [TestCase(SourceArithmeticProfile.Release,
        BoundIntegerOverflowBehavior.Checked, 0x23, TokenKind.Slash,
        "float32", "left / right")]
    [TestCase(SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, 0x24, TokenKind.Percent,
        "float32", "left % right")]
    [TestCase(SourceArithmeticProfile.Release,
        BoundIntegerOverflowBehavior.Checked, 0x24, TokenKind.Percent,
        "float32", "left % right")]
    public void StandardDirectNativeOutputCallsRoundTripBinaryLocalInputHome(
        SourceArithmeticProfile profile,
        BoundIntegerOverflowBehavior overflowBehavior,
        byte arithmeticControl, TokenKind operation, string type,
        string expression)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string moduleIdentity = "native-output-binary-local-input";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [Terrum.Interop.NativeImport("example.dll", "Observe")]
            internal extern int32 Observe(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<uint32> value,
                {{type}} selector);

            internal void Probe({{type}} left, {{type}} right) {
                init mut uint32 value;
                {{type}} adjusted = {{expression}};
                unsafe { Observe(&init value, adjusted); }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions()
                with { SourceArithmeticProfile = profile });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(2), tables.Contracts.Count);
        var code = body.GetBlockCode(0).ToArray();
        var arithmeticOffset = Array.IndexOf(code, (byte)0xC0);
        Assert.That(arithmeticOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(code[arithmeticOffset + 1],
            Is.EqualTo(arithmeticControl));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            code.AsSpan(arithmeticOffset + 2)), Is.EqualTo(0x10F0));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var caller = loaded.Functions.Single(static function =>
            function.Name == "Probe");
        var local = new List<BoundLocal>();
        Assert.That(BoundTreeTraversal.VisitStatements(caller.Body,
            statement =>
            {
                if (statement is BoundLocal candidate)
                {
                    local.Add(candidate);
                }
            }), Is.True);
        var adjusted = local.Single(static candidate =>
            candidate.Initializer is BoundBinary);
        var initializer = (BoundBinary)adjusted.Initializer!;
        Assert.That(initializer.Operator, Is.EqualTo(operation));
        Assert.That(initializer.OverflowBehavior,
            Is.EqualTo(overflowBehavior));
        Assert.That(initializer.Left, Is.TypeOf<BoundRead>());
        Assert.That(((BoundRead)initializer.Left).Variable, Is.EqualTo(0));
        Assert.That(initializer.Right, Is.TypeOf<BoundRead>());
        Assert.That(((BoundRead)initializer.Right).Variable, Is.EqualTo(1));
        BoundCall? call = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundCall candidate
                    && candidate.Arguments.Any(static argument =>
                        argument is BoundNativeOutputReservation))
                {
                    call = candidate;
                }
            }), Is.True);
        Assert.That(call, Is.Not.Null);
        Assert.That(call!.Arguments[1], Is.TypeOf<BoundRead>());
        Assert.That(((BoundRead)call.Arguments[1]).Variable,
            Is.EqualTo(adjusted.Variable));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
    }

    [TestCase(SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, 0x04, "int32")]
    [TestCase(SourceArithmeticProfile.Release,
        BoundIntegerOverflowBehavior.Wrapping, 0x08, "int32")]
    [TestCase(SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, 0x25, "float32")]
    [TestCase(SourceArithmeticProfile.Release,
        BoundIntegerOverflowBehavior.Checked, 0x25, "float32")]
    [TestCase(SourceArithmeticProfile.Development,
        BoundIntegerOverflowBehavior.Checked, 0x25, "float64")]
    [TestCase(SourceArithmeticProfile.Release,
        BoundIntegerOverflowBehavior.Checked, 0x25, "float64")]
    public void StandardDirectNativeOutputCallsRoundTripUnaryLocalInputHome(
        SourceArithmeticProfile profile,
        BoundIntegerOverflowBehavior overflowBehavior,
        byte arithmeticControl, string type)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string moduleIdentity = "native-output-unary-local-input";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [Terrum.Interop.NativeImport("example.dll", "Observe")]
            internal extern int32 Observe(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<uint32> value,
                {{type}} selector);

            internal void Probe({{type}} selector) {
                init mut uint32 value;
                {{type}} adjusted = -selector;
                unsafe { Observe(&init value, adjusted); }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions()
                with { SourceArithmeticProfile = profile });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(2), tables.Contracts.Count);
        var code = body.GetBlockCode(0).ToArray();
        var arithmeticOffset = Array.IndexOf(code, (byte)0xC0);
        Assert.That(arithmeticOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(code[arithmeticOffset + 1],
            Is.EqualTo(arithmeticControl));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            code.AsSpan(arithmeticOffset + 2)), Is.EqualTo(0xF0F0));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var caller = loaded.Functions.Single(static function =>
            function.Name == "Probe");
        var local = new List<BoundLocal>();
        Assert.That(BoundTreeTraversal.VisitStatements(caller.Body,
            statement =>
            {
                if (statement is BoundLocal candidate)
                {
                    local.Add(candidate);
                }
            }), Is.True);
        var adjusted = local.Single(static candidate =>
            candidate.Initializer is BoundUnary);
        var initializer = (BoundUnary)adjusted.Initializer!;
        Assert.That(initializer.Operator, Is.EqualTo(TokenKind.Minus));
        Assert.That(initializer.OverflowBehavior,
            Is.EqualTo(overflowBehavior));
        Assert.That(initializer.Operand, Is.TypeOf<BoundRead>());
        Assert.That(((BoundRead)initializer.Operand).Variable,
            Is.EqualTo(0));
        BoundCall? call = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundCall candidate
                    && candidate.Arguments.Any(static argument =>
                        argument is BoundNativeOutputReservation))
                {
                    call = candidate;
                }
            }), Is.True);
        Assert.That(call, Is.Not.Null);
        Assert.That(call!.Arguments[1], Is.TypeOf<BoundRead>());
        Assert.That(((BoundRead)call.Arguments[1]).Variable,
            Is.EqualTo(adjusted.Variable));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
    }

    [Test]
    public void StandardDirectNativeOutputCallsReserveMultipleFormalHomes()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string moduleIdentity = "native-output-multiple-homes";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "ReadPair")]
            internal extern int32 ReadPair(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<uint32> first,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                write ptr<uint32> second);

            internal void Probe() {
                init mut uint32 first;
                init mut uint32 second;
                unsafe { ReadPair(&init first, &init second); }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(2), tables.Contracts.Count);
        Assert.That(body.HomeCount, Is.EqualTo(2));
        Assert.That(body.GetRecord(2)[0], Is.EqualTo(0x0C));
        Assert.That(body.GetRecord(3)[0], Is.EqualTo(0x0C));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xDF, 0, 2, 0, 0xDF, 0, 3, 0, 0xE0, 0, 1, 0, 0x18, 0,
            }));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
    }

    [Test]
    public void StandardDirectNativeOutputCallsRoundTripFormalDependencies()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string moduleIdentity = "native-output-formal-dependency";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            internal struct Packet {
                public uint32 Kind;
                public uint32 Count;
            }

            [param(packet): DependsOn(nameof(source))]
            [param(packet): ForwardsBacking(nameof(source))]
            [Terrum.Interop.NativeImport("example.dll", "Observe")]
            internal extern int32 Observe(
                ptr<int32> source,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<Packet> packet);

            internal void Probe(ptr<int32> source) {
                init mut Packet packet;
                unsafe { Observe(source, &init packet); }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        Assert.That(tables.Contracts.GetDependencyContracts(1), Has.Length.EqualTo(1));
        Assert.That(tables.Contracts.GetDependencyContracts(1)[0],
            Is.EqualTo(new TrRmDependencyContract(2, 1, 2, 1, 1, 0)));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var import = loaded.Functions.Single(static function =>
            function.Name == "Observe");
        Assert.That(import.NativeOutputContracts.Single().Dependencies,
            Is.EqualTo([
                new NativeOutputDependency(2,
                    NativeOutputDependencySourceKind.Formal,
                    NativeOutputDependencyRelationship.Forward,
                    NativeOutputPublicationPhase.NormalCompletion, 1, 0),
            ]));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
    }

    [Test]
    public void StorageAddressSchemaDirectNativeOutputBodyRoundTrips()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-addresses-1";
        const string moduleIdentity = "native-output-storage-address-body";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "Observe")]
            internal extern int32 Observe(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<uint32> value);

            internal void Probe() {
                init mut uint32 value;
                unsafe { Observe(&init value); }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageAddressDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(
                index);
        Assert.That(tables.Schema,
            Is.EqualTo(TrRmArtifactSchema.ForeignInvocationStorageAddressDraft));
        tables.ValidateRegisterBodyMapsForMethod(2);

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
    }

    [Test]
    [TestCase("Read", false, 0u, false)]
    [TestCase("Read", false, 1u, false)]
    [TestCase("Read", false, 0u, true)]
    [TestCase("Read", false, 1u, true)]
    [TestCase("Write", true, 0u, false)]
    [TestCase("Write", true, 1u, false)]
    [TestCase("Write", true, 0u, true)]
    [TestCase("Write", true, 1u, true)]
    [TestCase("ReadWrite", true, 0u, false)]
    [TestCase("ReadWrite", true, 1u, false)]
    [TestCase("ReadWrite", true, 0u, true)]
    [TestCase("ReadWrite", true, 1u, true)]
    public void StorageAddressNativeOutputBodyPreservesNativeParameterBacking(
        string access, bool isMutable, uint extent,
        bool copyThroughLocal)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-addresses-1";
        const string moduleIdentity = "native-output-parameter-backing";
        var pointerAccess = isMutable ? "write " : "";
        var localMutability = isMutable ? "mut " : "";
        var backingInitialization = copyThroughLocal
            ? $"{localMutability}int32 intermediate = source;\n"
                + $"{localMutability}int32 backing = intermediate;"
            : $"{localMutability}int32 backing = source;";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [param(result): DependsOn(nameof(source))]
            [param(result): ForwardsBacking(nameof(source))]
            [Terrum.Interop.NativeImport("example.dll", "GetPointer")]
            [Terrum.Interop.NativeParameter(1, {{access}}, NoEscape, {{extent}})]
            internal extern int32 GetPointer(
                {{pointerAccess}}ptr<int32> source,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<ptr<void>> result);

            public int32 Probe(int32 source) {
                {{backingInitialization}}
                init mut ptr<void> result;
                unsafe { return GetPointer(&backing, &init result); }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageAddressDraft(artifact));
        tables.ValidateRegisterBodyMapsForMethod(2);

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var target = loaded.Functions.Single(static function =>
            function.Name == "GetPointer");
        Assert.That(target.DirectNativeImport!.Parameters, Has.Length.EqualTo(1));
        Assert.That(target.DirectNativeImport.Parameters[0].Access.ToString(),
            Is.EqualTo(access));
        Assert.That(target.DirectNativeImport.Parameters[0].Extent,
            Is.EqualTo(extent));
        Assert.That(target.NativeOutputContracts.Single().Dependencies,
            Is.EqualTo([
                new NativeOutputDependency(2,
                    NativeOutputDependencySourceKind.Formal,
                    NativeOutputDependencyRelationship.Forward,
                    NativeOutputPublicationPhase.NormalCompletion, 1, 0),
            ]));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
        var image = NativeEmitter.EmitLibraryWithNativeImports(loaded,
            ["Probe"], NativeRuntimePolicy.FailFast);
        using var imageStream = new MemoryStream(image);
        using var imageReader = new PEReader(imageStream);
        Assert.That(imageReader.PEHeaders.PEHeader, Is.Not.Null);
        Assert.That(imageReader.PEHeaders.PEHeader!.ImportTableDirectory.Size,
            Is.GreaterThan(0));
    }

    [Test]
    [TestCase("Read", false, 0u, false)]
    [TestCase("Read", false, 1u, false)]
    [TestCase("Read", false, 3u, true)]
    [TestCase("Write", true, 0u, false)]
    [TestCase("Write", true, 1u, false)]
    [TestCase("Write", true, 3u, true)]
    [TestCase("ReadWrite", true, 0u, false)]
    [TestCase("ReadWrite", true, 1u, false)]
    [TestCase("ReadWrite", true, 3u, true)]
    public void NativeOutputBodyPreservesOpaqueNativeParameterPointerFormal(
        string access, bool isMutable, uint extent, bool copyToLocal)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-addresses-1";
        const string moduleIdentity = "native-output-opaque-pointer-parameter";
        var pointerAccess = isMutable ? "write " : "";
        var pointerArgument = copyToLocal ? "backing" : "source";
        var localCopy = copyToLocal
            ? $"{pointerAccess}ptr<int32> backing = source;"
            : "";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [param(result): DependsOn(nameof(source))]
            [param(result): ForwardsBacking(nameof(source))]
            [Terrum.Interop.NativeImport("example.dll", "GetPointer")]
            [Terrum.Interop.NativeParameter(1, {{access}}, NoEscape, {{extent}})]
            internal extern int32 GetPointer(
                {{pointerAccess}}ptr<int32> source,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<ptr<void>> result);

            public int32 Probe({{pointerAccess}}ptr<int32> source) {
                {{localCopy}}
                init mut ptr<void> result;
                unsafe { return GetPointer({{pointerArgument}}, &init result); }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageAddressDraft(artifact));
        tables.ValidateRegisterBodyMapsForMethod(2);

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var target = loaded.Functions.Single(static function =>
            function.Name == "GetPointer");
        Assert.That(target.DirectNativeImport!.Parameters, Has.Length.EqualTo(1));
        Assert.That(target.DirectNativeImport.Parameters[0].Access.ToString(),
            Is.EqualTo(access));
        Assert.That(target.DirectNativeImport.Parameters[0].Extent,
            Is.EqualTo(extent));
        Assert.That(target.NativeOutputContracts.Single().Dependencies,
            Is.EqualTo([
                new NativeOutputDependency(2,
                    NativeOutputDependencySourceKind.Formal,
                    NativeOutputDependencyRelationship.Forward,
                    NativeOutputPublicationPhase.NormalCompletion, 1, 0),
            ]));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
        var image = NativeEmitter.EmitLibraryWithNativeImports(loaded,
            ["Probe"], NativeRuntimePolicy.FailFast);
        using var imageStream = new MemoryStream(image);
        using var imageReader = new PEReader(imageStream);
        Assert.That(imageReader.PEHeaders.PEHeader, Is.Not.Null);
        Assert.That(imageReader.PEHeaders.PEHeader!.ImportTableDirectory.Size,
            Is.GreaterThan(0));
    }

    [Test]
    public void StandardDirectNativeOutputCallExecutesThroughJit()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string moduleIdentity = "native-output-jit-call";
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "kernel32.dll", "QueryPerformanceCounter")]
            extern int32 QueryPerformanceCounter(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<int64> ticks);

            int32 Main() {
                init mut int64 ticks;
                unsafe { return QueryPerformanceCounter(&init ticks); }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
        var image = NativeEmitter.EmitWithNativeImports(loaded,
            NativeRuntimePolicy.FailFast);
        using (var imageStream = new MemoryStream(image))
        using (var imageReader = new PEReader(imageStream))
        {
            Assert.That(imageReader.PEHeaders.PEHeader, Is.Not.Null);
            Assert.That(imageReader.PEHeaders.PEHeader!.ImportTableDirectory.Size,
                Is.GreaterThan(0));
        }

        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast),
            Is.Not.Zero);
    }

    [Test]
    public void StandardDirectNativeOutputSequentialCallsExecuteThroughJit()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string moduleIdentity = "native-output-sequential-jit-calls";
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "kernel32.dll", "QueryPerformanceCounter")]
            extern int32 QueryPerformanceCounter(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<int64> ticks);

            int32 Main() {
                init mut int64 firstTicks;
                init mut int64 secondTicks;
                unsafe {
                    QueryPerformanceCounter(&init firstTicks);
                    return QueryPerformanceCounter(&init secondTicks);
                }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast),
            Is.Not.Zero);
    }

    [TestCase(
        "trrm/1-draft-foreign-invocation-contracts-1",
        "native-output-result-correlated-read-f")]
    [TestCase(
        "trrm/1-draft-foreign-invocation-storage-addresses-1",
        "native-output-result-correlated-read-a2")]
    public void StandardDirectNativeOutputResultCorrelatesAReadThroughJit(
        string schemaIdentity, string moduleIdentity)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "kernel32.dll", "QueryPerformanceCounter")]
            extern int32 QueryPerformanceCounter(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<int64> ticks);

            void Main() {
                init mut int64 ticks;
                unsafe {
                    if (QueryPerformanceCounter(&init ticks) != 0) {
                        if (ticks > 0) { }
                    }
                }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));

        var main = loaded.Functions.Single(static function =>
            function.Name == "Main");
        BoundCall? outputCall = null;
        var outputReads = new List<BoundRead>();
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundCall call
                    && call.Arguments.Any(static argument =>
                        argument is BoundNativeOutputReservation))
                {
                    outputCall = call;
                }
                else if (expression is BoundRead read
                    && read.Variable >= main.ParameterCount)
                {
                    outputReads.Add(read);
                }
            }), Is.True);
        Assert.That(outputCall, Is.Not.Null);
        Assert.That(outputReads, Has.Count.EqualTo(1));
        var reservation = outputCall!.Arguments
            .OfType<BoundNativeOutputReservation>().Single();
        Assert.That(outputReads.Single().Variable,
            Is.EqualTo(reservation.Variable));
        Assert.That(outputReads.Single().Type,
            Is.EqualTo(PrimitiveType.Int64));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast),
            Is.Zero);
    }

    [TestCase("trrm/1-draft-foreign-invocation-contracts-1", false)]
    [TestCase("trrm/1-draft-foreign-invocation-storage-addresses-1", true)]
    public void StandardDirectNativeOutputNestedResultCorrelatesARead(
        string schemaIdentity, bool storageAddressSchema)
    {
        const string moduleIdentity = "native-output-nested-result-read";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "kernel32.dll", "QueryPerformanceCounter")]
            extern int32 QueryPerformanceCounter(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<int64> ticks);

            public void Probe(bool invoke) {
                init mut int64 ticks;
                if (invoke) {
                    unsafe {
                        if (QueryPerformanceCounter(&init ticks) != 0) {
                            if (ticks > 0) { }
                        }
                    }
                }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var index = storageAddressSchema
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageAddressDraft(artifact)
            : TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = storageAddressSchema
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(
                    index)
            : TrRmStructuralMetadataValidator
                .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        tables.ValidateRegisterBodyMapsForMethod(2);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(2), tables.Contracts.Count);
        Assert.That(body.BlockCount, Is.EqualTo(10));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var probe = loaded.Functions.Single(static function =>
            function.Name == "Probe");
        var conditionals = new List<BoundIf>();
        var outputReads = new List<BoundRead>();
        Assert.That(BoundTreeTraversal.VisitStatements(probe.Body,
            statement =>
            {
                if (statement is BoundIf conditional)
                {
                    conditionals.Add(conditional);
                }
            }), Is.True);
        Assert.That(BoundTreeTraversal.VisitExpressions(probe.Body,
            expression =>
            {
                if (expression is BoundRead read
                    && read.Variable >= probe.ParameterCount)
                {
                    outputReads.Add(read);
                }
            }), Is.True);
        Assert.That(conditionals, Has.Count.EqualTo(3));
        Assert.That(outputReads, Has.Count.EqualTo(1));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
        var image = NativeEmitter.EmitLibraryWithNativeImports(loaded,
            ["Probe"], NativeRuntimePolicy.FailFast);
        using var imageStream = new MemoryStream(image);
        using var imageReader = new PEReader(imageStream);
        Assert.That(imageReader.PEHeaders.PEHeader, Is.Not.Null);
        Assert.That(imageReader.PEHeaders.PEHeader!.ImportTableDirectory.Size,
            Is.GreaterThan(0));
    }

    [Test]
    public void StandardDirectNativeOutputNonNegativeResultCorrelatesARead()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string moduleIdentity = "native-output-nonnegative-result";
        var source = """
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            internal extern int32 ReadValue(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
                write ptr<int32> value);

            public void Probe() {
                init mut int32 value;
                unsafe {
                    if (ReadValue(&init value) >= 0) {
                        if (value >= 0) { }
                    }
                }
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        tables.ValidateRegisterBodyMapsForMethod(2);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(2), tables.Contracts.Count);
        var foundNonNegativePredicate = false;
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var code = body.GetBlockCode(blockIndex);
            for (var position = 0; position < code.Length;)
            {
                var length = TrRmRegisterBodyReader.InstructionLength(
                    code[position], body.Schema);
                if (code[position] == 0xC3 && code[position + 1] == 6)
                {
                    foundNonNegativePredicate = true;
                }

                position += length;
            }
        }

        Assert.That(foundNonNegativePredicate, Is.True);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));

        var incompatible = Compilation.CreateLibrary(
            SyntaxTree.Parse(source.Replace(
                "ReadValue(&init value) >= 0",
                "ReadValue(&init value) != 0",
                StringComparison.Ordinal)),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(incompatible.HasErrors, Is.True,
            "A NonZero branch must not prove a NonNegative output live.");

        var complementary = Compilation.CreateLibrary(
            SyntaxTree.Parse(source.Replace(
                "ReadValue(&init value) >= 0",
                "ReadValue(&init value) < 0",
                StringComparison.Ordinal)),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(complementary.HasErrors, Is.True,
            "The NonNegative complement must leave the output unavailable.");
    }

    [Test]
    public void A3NativeAddressProducerUsesOneRootLedgerForArtifactValidation()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
        const string moduleIdentity = "native-address-a3";
        var compilation = CreateNativeAddressCompilation("example.dll");
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var publicArtifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, schemaIdentity, 1);

        TrRmA3ResourceLedgerTestHelper.AssertExactWork(ledger =>
        {
            var artifact = TrRmExecutableCompiler
                .EmitForeignInvocationDraftNativeAddressLibrary(program,
                    moduleIdentity,
                    TrRmArtifactSchema
                        .ForeignInvocationStorageFixedArrayGenericCopyDraft,
                    a3LayoutResourceLedger: ledger);
            Assert.That(artifact, Is.EqualTo(publicArtifact));
        });
    }

    [Test]
    public void A3StorageAddressProducerUsesOneRootLedgerForStaging()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
        const string moduleIdentity = "native-storage-address-a3";
        var sourceArtifact = CreateStorageAddressArtifact(moduleIdentity,
            schemaIdentity);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, sourceArtifact),
            moduleIdentity, []);
        var publicArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1);
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                publicArtifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                index);
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                callerRow = row;
                break;
            }
        }
        Assert.That(callerRow, Is.GreaterThan(0));
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        Assert.That(body.GetRecord(2)[0], Is.EqualTo(0x0E));

        TrRmA3ResourceLedgerTestHelper.AssertExactWork(ledger =>
        {
            var artifact = TrRmExecutableCompiler
                .EmitForeignInvocationDraftNativeAddressLibrary(loaded,
                    moduleIdentity,
                    TrRmArtifactSchema
                        .ForeignInvocationStorageFixedArrayGenericCopyDraft,
                    a3LayoutResourceLedger: ledger);
            Assert.That(artifact, Is.EqualTo(publicArtifact));
        });
    }

    [Test]
    public void StorageAddressSchemaSelectionPreservesForeignInvocationLoading()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-addresses-1";
        const string moduleIdentity = "native-address-storage-schema";
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            public extern void Observe();

            internal void Form() {
                unsafe { _ = &Observe; }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageAddressDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(index);
        Assert.That(tables.Schema, Is.EqualTo(
            TrRmArtifactSchema.ForeignInvocationStorageAddressDraft));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "Observe").DirectNativeImport, Is.Not.Null);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "Form").Body, Is.Not.Null);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        Assert.That(reference.Functions.Single(static function =>
            function.Name == "Observe").ModuleImport?.ProducerIdentity,
            Is.EqualTo(moduleIdentity));
    }

    [TestCase("trrm/1-draft-foreign-invocation-storage-fixed-arrays-2",
        false, false, false)]
    [TestCase("trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1",
        false, true, false)]
    [TestCase("trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1",
        false, false, true)]
    [TestCase("trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1",
        true, false, false)]
    public void FixedArraySchemaSelectionPreservesStorageAddressLoading(
        string schemaIdentity, bool usesGenericSchema,
        bool usesEmptyInitializationSchema, bool usesUnitInitializationSchema)
    {
        const string moduleIdentity = "native-address-fixed-array-schema";
        var expectedSchema = usesGenericSchema
            ? TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
            : usesUnitInitializationSchema
                ? TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                : usesEmptyInitializationSchema
                    ? TrRmArtifactSchema
                        .ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                    : TrRmArtifactSchema
                        .ForeignInvocationStorageFixedArrayCopyDraft;
        var artifact = CreateStorageAddressArtifact(moduleIdentity,
            schemaIdentity);
        var index = expectedSchema switch
        {
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                => TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact),
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                => TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                        artifact),
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                => TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft(
                        artifact),
            _ => TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyDraft(artifact),
        };
        var tables = expectedSchema switch
        {
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                => TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                        index),
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                => TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                        index),
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                => TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageFixedArrayCopyEmptyInitializationDraftAndGetExecutionTables(
                        index),
            _ => TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    index),
        };
        Assert.That(tables.Schema, Is.EqualTo(expectedSchema));
        Assert.That(tables.FixedArrayLayouts.Count, Is.Zero);
        var caller = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                caller = row;
                break;
            }
        }
        Assert.That(caller, Is.GreaterThan(0));
        var a3Ledger = tables.A3LayoutResourceLedger;
        var workRemainingBeforeBodyValidation = usesGenericSchema
            ? a3Ledger?.WorkRemaining
            : null;
        tables.ValidateRegisterBodyMapsForMethod(caller);
        if (usesGenericSchema)
        {
            Assert.That(a3Ledger, Is.Not.Null);
            Assert.That(a3Ledger!.WorkRemaining,
                Is.LessThan(workRemainingBeforeBodyValidation!.Value));
        }
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);
        AssertHomeFirstUseTypeMap(body);
        var hasStorageAddressRecord = false;
        for (var record = 0; record < body.RecordCount; record++)
        {
            hasStorageAddressRecord |= body.GetRecord(record)[0] == 0x0E;
        }
        Assert.That(hasStorageAddressRecord, Is.True);

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "GetSystemTimeAdjustment").DirectNativeImport,
            Is.Not.Null);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "Main").Body, Is.Not.Null);
    }

    [Test]
    public void StandardA2EInitializesEmptyLocalAndRejectsTheRecordUnderA2()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1";
        const string moduleIdentity = "a2e-empty-array-initialization";
        var options = WithLayoutRegistration(new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        });
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
            internal extern void Observe(
                ptr<uint32> value,
                write ptr<int32> output);

            int32 Main() {
                [uint32; 0] empty = [];
                init [uint32; 0] fixedEmpty;
                fixedEmpty = [];
                init mut [uint32; 0] mutableEmpty;
                mutableEmpty = [];
                int32 zeroIndex = 0;
                mut [[uint32; 0]; 2] nested = [[], []];
                mut uint32 input = 0;
                mut int32 output = 0;
                unsafe {
                    var callback = &Observe;
                    callback(&input, &output);
                }
                return output;
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2", 1));

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft(
                        artifact));
        Assert.That(tables.FixedArrayLayouts.Count, Is.EqualTo(2));
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                callerRow = row;
                break;
            }
        }
        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        AssertHomeFirstUseTypeMap(body);
        var emptyInitializationCode = body.GetBlockCode(0).ToArray();
        Assert.That(emptyInitializationCode, Does.Contain((byte)0x91));
        Assert.That(emptyInitializationCode, Does.Contain((byte)0x92));
        var homePlaceSelections = 0;
        var elementPlaceSelections = 0;
        var nullaryStores = 0;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] == 0x04 && record[4] == 1)
            {
                homePlaceSelections++;
            }
            else if (record[0] == 0x04 && record[4] == 3)
            {
                elementPlaceSelections++;
            }
            else if (record[0] == 0x05 && record[5] == 0)
            {
                nullaryStores++;
                Assert.That(record[6], Is.EqualTo(1));
                Assert.That(record[7], Is.Zero);
                Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                    record[10..]), Is.Zero);
            }
        }
        Assert.That(homePlaceSelections, Is.EqualTo(3));
        Assert.That(elementPlaceSelections, Is.EqualTo(2));
        Assert.That(nullaryStores, Is.EqualTo(5));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var emptyInitializations = new List<BoundNativeFixedArrayInitialization>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundNativeFixedArrayInitialization initialization
                    && initialization.Layout.ElementCount == 0)
                {
                    emptyInitializations.Add(initialization);
                }
            }), Is.True);
        Assert.That(emptyInitializations, Has.Count.EqualTo(5));
        Assert.That(emptyInitializations.All(static initialization =>
            initialization.Elements.IsEmpty), Is.True);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));

        var a2Tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        var a2CallerRow = 0;
        for (var row = 1; row <= a2Tables.Methods.Count; row++)
        {
            if (a2Tables.Methods.GetName(row) == "Main")
            {
                a2CallerRow = row;
                break;
            }
        }
        Assert.That(a2CallerRow, Is.GreaterThan(0));
        var rejectedByA2 = false;
        try
        {
            a2Tables.ValidateRegisterBodyMapsForMethod(a2CallerRow);
        }
        catch (InvalidDataException)
        {
            rejectedByA2 = true;
        }
        Assert.That(rejectedByA2, Is.True);

        foreach (var defect in new[] {
            "nonzero-source-kind",
            "nonzero-source-index",
            "nonzero-source-use",
            "wrong-transition",
            "nonzero-dependency",
            "formal-present",
            "nonzero-flags",
            "nonzero-formal-ordinal",
            "wrong-place-kind",
            "wrong-home-access",
            "wrong-home-type",
            "out-of-range-home",
            "wrong-home-role",
        })
        {
            var malformed = RewriteA2EEmptyInitializationArtifact(artifact,
                defect);
            _ = Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.LoadStandard(
                    new TrRmStandardArtifactInput(schemaIdentity, malformed),
                    moduleIdentity, Array.Empty<TrRmStandardArtifactInput>()));
        }

        var doubleInitialization
            = DuplicateA2EEmptyHomeInitialization(artifact);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    doubleInitialization),
                moduleIdentity, Array.Empty<TrRmStandardArtifactInput>()));

    }

    [Test]
    public async Task StandardA2ESourceFreeJitAndAotExecuteEmptyInitialization()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1";
        const string moduleIdentity = "a2e-empty-array-runtime";
        var options = WithLayoutRegistration(new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        });
        const string source = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetSystemTimeAdjustment",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(3, Write, NoEscape, 1)]
            internal extern int32 GetSystemTimeAdjustment(
                write ptr<uint32> adjustment,
                write ptr<uint32> increment,
                write ptr<int32> disabled);

            int32 Main() {
                [uint32; 0] ordinary = [];
                init [uint32; 0] fixedEmpty;
                fixedEmpty = [];
                init mut [uint32; 0] mutableEmpty;
                mutableEmpty = [];
                int32 zeroIndex = 0;
                mut [[uint32; 0]; 2] nested = [[], []];
                mut uint32 adjustment = 0;
                mut uint32 increment = 0;
                mut int32 disabled = -1;
                unsafe {
                    var callback = &GetSystemTimeAdjustment;
                    callback(&adjustment, &increment, &disabled);
                }
                return disabled;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var input = new TrRmStandardArtifactInput(schemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        var main = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var initializations
            = new List<BoundNativeFixedArrayInitialization>();
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundNativeFixedArrayInitialization init)
                {
                    initializations.Add(init);
                }
            }), Is.True);
        Assert.That(initializations, Has.Count.EqualTo(6));
        Assert.That(initializations.All(static init =>
            init.Layout.ByteExtent == 0), Is.True);
        Assert.That(initializations.Count(static init =>
            init.Layout.ElementCount == 0), Is.EqualTo(5));

        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft(
                        artifact));
        var mainRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                mainRow = row;
                break;
            }
        }
        Assert.That(mainRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(mainRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(mainRow), tables.Contracts.Count);
        var storageAddressRecordCount = 0;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            storageAddressRecordCount += body.GetRecord(recordIndex)[0]
                == 0x0E ? 1 : 0;
        }
        Assert.That(storageAddressRecordCount, Is.EqualTo(3),
            "The only storage addresses are the three scalar import outputs.");

        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
        var compilerAssemblyPath = typeof(TrRmExecutableCompiler).Assembly.Location;
        var compilerAssemblyHash = Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(compilerAssemblyPath).ConfigureAwait(true)));
        TestContext.WriteLine(
            $"A2E source-free runtime input: schema={schemaIdentity}; module={moduleIdentity}; source-sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))}; artifact-sha256={Convert.ToHexString(SHA256.HashData(artifact))}; compiler={compilerAssemblyPath}; compiler-sha256={compilerAssemblyHash}.");
        var jitResult = NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast);
        TestContext.WriteLine(
            $"A2E source-free JIT: policy=FailFast; actual={jitResult}; allowed-disabled-range=0..1.");
        Assert.That(jitResult, Is.InRange(0, 1));

        var image = NativeModuleCompiler.EmitStandardExecutable(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast);
        Assert.That(image, Is.Not.Empty);
        var imagePath = Path.Combine(Path.GetTempPath(),
            $"terrum-{moduleIdentity}-{Guid.NewGuid():N}.exe");
        Process? process = null;
        try
        {
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            process = Process.Start(new ProcessStartInfo(imagePath) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException(
                "The A2E native executable was not started.");
            var processId = process.Id;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(true);
            }
            catch (TimeoutException exception)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                throw new TimeoutException(
                    $"A2E native executable exceeded its 10-second deadline. {await stdout.ConfigureAwait(true)}{Environment.NewLine}{await stderr.ConfigureAwait(true)}",
                    exception);
            }

            var output = await stdout.ConfigureAwait(true);
            var error = await stderr.ConfigureAwait(true);
            TestContext.WriteLine(
                $"A2E source-free AOT: policy=FailFast; pid={processId}; actual={process.ExitCode}; allowed-disabled-range=0..1; image-sha256={Convert.ToHexString(SHA256.HashData(image))}; deadline=10s.");
            Assert.That(output, Is.Empty);
            Assert.That(error, Is.Empty);
            Assert.That(process.ExitCode, Is.InRange(0, 1));
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                process.Dispose();
            }

            await TestFileCleanup.DeleteWithRetryAsync(imagePath)
                .ConfigureAwait(true);
        }
    }

    [TestCase("init [uint32; 0] empty; return int32(empty[0]);")]
    [TestCase("init [uint32; 0] empty; empty = []; empty = []; return 0;")]
    [TestCase("init mut [uint32; 0] empty; empty = []; empty = []; return 0;")]
    public void A2ERejectsVacantReadsAndRepeatedNullaryInitialization(
        string body)
    {
        var options = WithLayoutRegistration(new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        });
        var source = $"int32 Main() {{ {body} }}";
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            options);
        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase(0, "")]
    [TestCase(1, "()")]
    [TestCase(2, "(), ()")]
    public void StandardA2EUStoresTypedUnitElementsAndRejectsA2AndA2E(
        int unitElementCount, string unitInitializers)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        const string moduleIdentity = "a2eu-unit-array-initialization";
        var options = WithLayoutRegistration(new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        });
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
            internal extern void Observe(
                ptr<uint32> value,
                write ptr<int32> output);

            int32 Main() {
                int32 zeroIndex = 0;
                mut [void; {{unitElementCount}}] values = [{{unitInitializers}}];
                mut uint32 input = 0;
                mut int32 output = 0;
                unsafe {
                    var callback = &Observe;
                    callback(&input, &output);
                }
                return output;
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var unsupportedSchemas = unitElementCount == 0
            ? new[] {
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2",
            }
            : [
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2",
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1",
            ];
        foreach (var unsupportedSchema in unsupportedSchemas)
        {
            _ = Assert.Throws<NotSupportedException>(() =>
                TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
                    unsupportedSchema, 1));
        }

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                        artifact));
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                callerRow = row;
                break;
            }
        }
        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        AssertHomeFirstUseTypeMap(body);
        var unitElementStores = 0;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] == 0x05 && record[4] == 1
                && record[5] == 1)
            {
                Assert.That(record[6], Is.EqualTo(1));
                Assert.That(record[7], Is.EqualTo(1));
                Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                    record[10..]), Is.EqualTo(14));
                unitElementStores++;
            }
        }
        Assert.That(unitElementStores, Is.EqualTo(unitElementCount));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var unitArray = new List<BoundNativeFixedArrayInitialization>();
        var unitConstants = new List<BoundConstant>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundNativeFixedArrayInitialization
                    initialization
                    && initialization.Layout.ElementCount
                        == (ulong)unitElementCount
                    && initialization.Layout.ElementType == PrimitiveType.Void)
                {
                    unitArray.Add(initialization);
                }
                else if (expression is BoundConstant
                    { Type: PrimitiveType.Void } constant)
                {
                    unitConstants.Add(constant);
                }
            }), Is.True);
        Assert.That(unitArray, Has.Count.EqualTo(1));
        Assert.That(unitArray[0].Elements,
            Has.Length.EqualTo(unitElementCount));
        Assert.That(unitConstants, Has.Count.EqualTo(unitElementCount));
        Assert.That(unitConstants.All(static constant => constant.Value == 0),
            Is.True);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));

        var incompatibleSchemas = unitElementCount == 0
            ? new[] {
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2",
            }
            : [
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2",
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1",
            ];
        foreach (var unsupportedSchema in incompatibleSchemas)
        {
            _ = Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.LoadStandard(
                    new TrRmStandardArtifactInput(unsupportedSchema,
                        artifact),
                    moduleIdentity,
                    Array.Empty<TrRmStandardArtifactInput>()));
        }

        string[] defects = unitElementCount == 0
            ? []
            : [
                "unit-source-r15",
                "unit-store-source-use",
                "unit-store-transition",
                "unit-store-place-kind",
                "unit-store-dependency",
                "unit-store-formal",
            ];
        foreach (var defect in defects)
        {
            var malformed = RewriteA2EUUnitStoreArtifact(artifact, defect);
            _ = Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.LoadStandard(
                    new TrRmStandardArtifactInput(schemaIdentity, malformed),
                    moduleIdentity, Array.Empty<TrRmStandardArtifactInput>()));
        }
    }

    [Test]
    public void StandardA2EUCapturesUnitInitializerCallsBeforeElementStores()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        const string moduleIdentity = "a2eu-unit-array-effect-calls";
        var options = WithLayoutRegistration(new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        });
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
            internal extern void Observe(
                ptr<uint32> value,
                write ptr<int32> output);

            int32 Main() {
                int32 zeroIndex = 0;
                mut uint32 input1 = 0;
                mut int32 output1 = 0;
                mut uint32 input2 = 0;
                mut int32 output2 = 0;
                unsafe {
                    mut [void; 2] values = [
                        Observe(&input1, &output1),
                        Observe(&input2, &output2)
                    ];
                    var callback = &Observe;
                    callback(&input1, &output1);
                }
                return output1;
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                        artifact));
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                callerRow = row;
                break;
            }
        }
        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);

        var directCallRecords = new List<int>();
        var code = body.GetBlockCode(0);
        var instructionOffsets = new Dictionary<int, int>();
        for (var position = 0; position < code.Length;)
        {
            var instructionLength = TrRmRegisterBodyReader.InstructionLength(
                code[position], body.Schema);
            if (code[position] == 0xE0)
            {
                var recordIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                    code[(position + 2)..]);
                instructionOffsets.Add(recordIndex, position);
                if (body.GetRecord(recordIndex)[4] == 1)
                {
                    directCallRecords.Add(recordIndex);
                }
            }
            position += instructionLength;
        }

        Assert.That(directCallRecords, Has.Count.EqualTo(2));
        var firstUnitStoreOffset = int.MaxValue;
        var inputSourceHomes = new List<ushort>();
        var outputSourceHomes = new List<ushort>();
        foreach (var record in directCallRecords)
        {
            var callRecord = body.GetRecord(record);
            foreach (var (inputOffset, sourceHomes) in new[] {
                (22, inputSourceHomes),
                (26, outputSourceHomes),
            })
            {
                var inputHome = BinaryPrimitives.ReadUInt16LittleEndian(
                    callRecord[inputOffset..]);
                var foundStorageRecord = false;
                for (var recordIndex = 0;
                    recordIndex < body.RecordCount;
                    recordIndex++)
                {
                    var storageRecord = body.GetRecord(recordIndex);
                    if (storageRecord[0] != 0x0E
                        || BinaryPrimitives.ReadUInt16LittleEndian(
                            storageRecord[8..]) != inputHome)
                    {
                        continue;
                    }

                    Assert.That(storageRecord[4], Is.EqualTo(2));
                    sourceHomes.Add(BinaryPrimitives
                        .ReadUInt16LittleEndian(storageRecord[6..]));
                    foundStorageRecord = true;
                    break;
                }
                Assert.That(foundStorageRecord, Is.True);
            }
        }
        Assert.That(inputSourceHomes[0], Is.LessThan(inputSourceHomes[1]));
        Assert.That(outputSourceHomes[0], Is.LessThan(outputSourceHomes[1]));
        for (var position = 0; position < code.Length;)
        {
            var instructionLength = TrRmRegisterBodyReader.InstructionLength(
                code[position], body.Schema);
            if (code[position] == 0xD9)
            {
                firstUnitStoreOffset = Math.Min(firstUnitStoreOffset, position);
            }
            position += instructionLength;
        }
        Assert.That(instructionOffsets[directCallRecords[^1]],
            Is.LessThan(firstUnitStoreOffset));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var unitArrays = new List<BoundNativeFixedArrayInitialization>();
        var initializerCalls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundNativeFixedArrayInitialization
                    { Layout.ElementType: PrimitiveType.Void } initialization
                    && initialization.Layout.ElementCount == 2)
                {
                    unitArrays.Add(initialization);
                }
                if (expression is BoundCall call)
                {
                    initializerCalls.Add(call);
                }
            }), Is.True);
        Assert.That(unitArrays, Has.Count.EqualTo(1));
        Assert.That(unitArrays[0].Elements.OfType<BoundConstant>().ToArray(),
            Has.Length.EqualTo(2));
        Assert.That(initializerCalls, Has.Count.EqualTo(2));
        Assert.That(initializerCalls.All(static call => call.Arguments
            .OfType<BoundNativeStorageAddress>().Count() == 2), Is.True);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
    }

    [Test]
    public void StandardA2EUCallsDistinctUnitImportsInSourceOrder()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        const string moduleIdentity = "a2eu-unit-array-distinct-calls";
        var options = WithLayoutRegistration(new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        });
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void MarkFirst();

            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void MarkSecond();

            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
            internal extern void Observe(
                ptr<uint32> value,
                write ptr<int32> output);

            int32 Main() {
                int32 zeroIndex = 0;
                mut uint32 input = 0;
                mut int32 output = 0;
                unsafe {
                    mut [void; 2] values = [
                        MarkFirst(),
                        MarkSecond()
                    ];
                    var callback = &Observe;
                    callback(&input, &output);
                }
                return output;
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                        artifact));
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                callerRow = row;
                break;
            }
        }
        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        var directCallTargets = new List<string>();
        var lastDirectCallOffset = -1;
        var firstStoreOffset = int.MaxValue;
        var code = body.GetBlockCode(0);
        for (var position = 0; position < code.Length;)
        {
            var instructionLength = TrRmRegisterBodyReader.InstructionLength(
                code[position], body.Schema);
            if (code[position] == 0xE0)
            {
                var callRecord = body.GetRecord(
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        code[(position + 2)..]));
                if (callRecord[4] == 1)
                {
                    var methodMapIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            callRecord[8..]);
                    var methodToken
                        = BinaryPrimitives.ReadUInt32LittleEndian(
                            body.MethodMap[(methodMapIndex * sizeof(uint))..]);
                    var methodRow = checked((int)(methodToken
                        & 0x00FF_FFFF));
                    directCallTargets.Add(tables.Methods.GetName(methodRow));
                    lastDirectCallOffset = position;
                }
            }
            else if (code[position] == 0xD9)
            {
                firstStoreOffset = Math.Min(firstStoreOffset, position);
            }
            position += instructionLength;
        }
        Assert.That(directCallTargets,
            Is.EqualTo(["MarkFirst", "MarkSecond"]));
        Assert.That(lastDirectCallOffset, Is.LessThan(firstStoreOffset));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var unitArrays = new List<BoundNativeFixedArrayInitialization>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundNativeFixedArrayInitialization
                    { Layout.ElementType: PrimitiveType.Void } initialization
                    && initialization.Layout.ElementCount == 2)
                {
                    unitArrays.Add(initialization);
                }
            }), Is.True);
        Assert.That(unitArrays, Has.Count.EqualTo(1));
        Assert.That(unitArrays[0].Elements.OfType<BoundConstant>().ToArray(),
            Has.Length.EqualTo(2));
        var callStatementTargets = new List<string>();
        foreach (var unsafeBlock in loadedMain.Body.Statements
            .OfType<BoundUnsafe>())
        {
            foreach (var statement in unsafeBlock.Body.Statements
                .OfType<BoundExpressionStatement>())
            {
                if (statement.Expression is BoundCall call
                    && loaded.Functions[call.Function].DirectNativeImport
                        is not null)
                {
                    Assert.That(call.IsUnsafeAuthorized, Is.True);
                    callStatementTargets.Add(
                        loaded.Functions[call.Function].Name);
                }
            }
        }
        Assert.That(callStatementTargets,
            Is.EqualTo(["MarkFirst", "MarkSecond"]));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
    }

    [Test]
    public void StandardA2EUCapturesNestedScalarCallArgumentsBeforeMutation()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        const string moduleIdentity = "a2eu-unit-array-nested-call";
        var options = WithLayoutRegistration(new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        });
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            internal extern int32 MutateAndGet(write ptr<int32> value);

            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void RecordPair(int32 first, int32 second);

            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
            internal extern void Observe(
                ptr<uint32> value,
                write ptr<int32> output);

            int32 Main() {
                int32 zeroIndex = 0;
                mut int32 marker = 0;
                mut uint32 input = 0;
                mut int32 output = 0;
                unsafe {
                    mut [void; 1] values = [
                        RecordPair(marker, MutateAndGet(&marker))
                    ];
                    var callback = &Observe;
                    callback(&input, &output);
                }
                return output;
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                        artifact));
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                callerRow = row;
                break;
            }
        }
        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);

        var directCallTargets = new List<string>();
        var directCallResultKinds = new List<byte>();
        var directCallRecords = new List<ReadOnlyMemory<byte>>();
        var code = body.GetBlockCode(0);
        var firstCopyOffset = int.MaxValue;
        var firstDirectCallOffset = int.MaxValue;
        var lastDirectCallOffset = -1;
        var firstUnitStoreOffset = int.MaxValue;
        for (var position = 0; position < code.Length;)
        {
            var instructionLength = TrRmRegisterBodyReader.InstructionLength(
                code[position], body.Schema);
            if (code[position] is 0xD5 or 0xD6)
            {
                firstCopyOffset = Math.Min(firstCopyOffset, position);
            }
            if (code[position] == 0xE0)
            {
                var callRecord = body.GetRecord(
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        code[(position + 2)..]));
                if (callRecord[4] == 1)
                {
                    var methodMapIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            callRecord[8..]);
                    var methodToken
                        = BinaryPrimitives.ReadUInt32LittleEndian(
                            body.MethodMap[(methodMapIndex * sizeof(uint))..]);
                    var methodRow = checked((int)(methodToken
                        & 0x00FF_FFFF));
                    directCallTargets.Add(tables.Methods.GetName(methodRow));
                    directCallResultKinds.Add(callRecord[5]);
                    directCallRecords.Add(callRecord.ToArray());
                    firstDirectCallOffset = Math.Min(firstDirectCallOffset,
                        position);
                    lastDirectCallOffset = position;
                }
            }
            else if (code[position] == 0xD9)
            {
                firstUnitStoreOffset = Math.Min(firstUnitStoreOffset,
                    position);
            }
            position += instructionLength;
        }
        Assert.That(firstCopyOffset, Is.LessThan(firstDirectCallOffset));
        Assert.That(directCallTargets,
            Is.EqualTo(["MutateAndGet", "RecordPair"]));
        Assert.That(directCallResultKinds,
            Is.EqualTo(new byte[] { 2, 0 }));
        Assert.That(lastDirectCallOffset, Is.LessThan(firstUnitStoreOffset));
        var nestedResultHome = BinaryPrimitives.ReadUInt16LittleEndian(
            directCallRecords[0].Span[10..]);
        var recordPairCall = directCallRecords[1].Span;
        var snapshotHome = BinaryPrimitives.ReadUInt16LittleEndian(
            recordPairCall[22..]);
        var nestedResultInputHome = BinaryPrimitives.ReadUInt16LittleEndian(
            recordPairCall[26..]);
        Assert.That(snapshotHome, Is.Not.EqualTo(nestedResultHome));
        Assert.That(nestedResultInputHome, Is.EqualTo(nestedResultHome));
        Assert.That(body.HomeMap[nestedResultHome * 8], Is.EqualTo(3));
        Assert.That(body.HomeMap[snapshotHome * 8], Is.EqualTo(3));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var unitArrays = new List<BoundNativeFixedArrayInitialization>();
        var initializerCalls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundNativeFixedArrayInitialization
                    { Layout.ElementType: PrimitiveType.Void } initialization
                    && initialization.Layout.ElementCount == 1)
                {
                    unitArrays.Add(initialization);
                }
                if (expression is BoundCall call)
                {
                    initializerCalls.Add(call);
                }
            }), Is.True);
        Assert.That(unitArrays, Has.Count.EqualTo(1));
        Assert.That(unitArrays[0].Elements.OfType<BoundConstant>().ToArray(),
            Has.Length.EqualTo(1));
        Assert.That(initializerCalls.Select(call =>
                loaded.Functions[call.Function].Name),
            Is.EquivalentTo(s_initializerCallNames));
        var recordPair = initializerCalls.Single(call =>
            loaded.Functions[call.Function].Name == "RecordPair");
        Assert.That(recordPair.Arguments.OfType<BoundRead>().ToArray(),
            Has.Length.EqualTo(2));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
    }

    [Test]
    public void StandardA2EUCapturesCheckedDivisionArgumentsBeforeUnitCalls()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        const string moduleIdentity = "a2eu-unit-array-checked-arguments";
        var options = WithLayoutRegistration(new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        });
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void Sleep(uint32 milliseconds);

            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void ObserveSigned(int32 value);

            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
            internal extern void Observe(
                ptr<uint32> value,
                write ptr<int32> output);

            int32 Main() {
                int32 zeroIndex = 0;
                mut uint32 numerator = 0;
                mut uint32 divisor = 0;
                mut int32 signedNumerator = -1;
                mut int32 signedDivisor = -1;
                mut uint32 input = 0;
                mut int32 output = 0;
                unsafe {
                    mut [void; 3] values = [
                        Sleep(numerator / divisor),
                        Sleep(numerator % divisor),
                        ObserveSigned(signedNumerator / signedDivisor)
                    ];
                    var callback = &Observe;
                    callback(&input, &output);
                }
                return output;
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                        artifact));
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                callerRow = row;
                break;
            }
        }
        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);

        var arithmeticInstructions = new List<(byte Opcode, int Offset)>();
        var directCallOffsets = new List<int>();
        var firstUnitStoreOffset = int.MaxValue;
        var code = body.GetBlockCode(0);
        for (var position = 0; position < code.Length;)
        {
            var opcode = code[position];
            var instructionLength = TrRmRegisterBodyReader.InstructionLength(
                opcode, body.Schema);
            if (opcode is 0x24 or 0x25)
            {
                arithmeticInstructions.Add((opcode, position));
            }
            else if (opcode == 0xE0)
            {
                var callRecord = body.GetRecord(
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        code[(position + 2)..]));
                if (callRecord[4] == 1)
                {
                    directCallOffsets.Add(position);
                }
            }
            else if (opcode == 0xD9)
            {
                firstUnitStoreOffset = Math.Min(firstUnitStoreOffset,
                    position);
            }

            position += instructionLength;
        }
        Assert.That(arithmeticInstructions.Select(
                static instruction => instruction.Opcode),
            Is.EqualTo("$%$"u8.ToArray()));
        Assert.That(directCallOffsets, Has.Count.EqualTo(3));
        for (var index = 0; index < arithmeticInstructions.Count; index++)
        {
            Assert.That(arithmeticInstructions[index].Offset,
                Is.LessThan(directCallOffsets[index]));
        }
        Assert.That(directCallOffsets[^1],
            Is.LessThan(firstUnitStoreOffset));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var capturedChecks = new List<BoundBinary>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundBinary {
                        Operator: TokenKind.Slash or TokenKind.Percent,
                    } check)
                {
                    capturedChecks.Add(check);
                }
            }), Is.True);
        Assert.That(capturedChecks, Has.Count.EqualTo(3));
        Assert.That(capturedChecks.All(static check =>
            (check.Type is PrimitiveType.Int32 or PrimitiveType.UInt32)
            && check.OverflowBehavior
                == BoundIntegerOverflowBehavior.Checked), Is.True);
        Assert.That(capturedChecks.Count(static check =>
            check.Type == PrimitiveType.UInt32), Is.EqualTo(2));
        Assert.That(capturedChecks.Count(static check =>
            check.Type == PrimitiveType.Int32), Is.EqualTo(1));
        Assert.That(capturedChecks.Count(static check =>
            check.Operator == TokenKind.Slash), Is.EqualTo(2));
        Assert.That(capturedChecks.Count(static check =>
            check.Operator == TokenKind.Percent), Is.EqualTo(1));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));

        var malformed = RewriteA2EUCheckedArithmeticArtifact(artifact);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity, malformed),
                moduleIdentity, Array.Empty<TrRmStandardArtifactInput>()));
    }

    [Test]
    public void StandardA2EUCapturesCheckedArrayAddressBeforeUnitCalls()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        const string moduleIdentity = "a2eu-unit-array-checked-address";
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern void ReadElement(ptr<uint32> value);

            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void RecordUnit();

            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
            internal extern void Observe(
                ptr<uint32> value,
                write ptr<int32> output);

            int32 Main() {
                int32 zeroIndex = 0;
                int32 invalidIndex = -1;
                mut [uint32; 1] source = [0];
                mut uint32 input = 0;
                mut int32 output = 0;
                unsafe {
                    mut [void; 2] values = [
                        ReadElement(&source[invalidIndex]),
                        RecordUnit()
                    ];
                    var callback = &Observe;
                    callback(&input, &output);
                }
                return output;
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                        artifact));
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                callerRow = row;
                break;
            }
        }
        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);

        var checkedAddressOffset = -1;
        var storageAddressOffset = -1;
        var directCallOffsets = new List<int>();
        var storeOffsets = new List<int>();
        var code = body.GetBlockCode(0);
        for (var position = 0; position < code.Length;)
        {
            var opcode = code[position];
            var instructionLength = TrRmRegisterBodyReader.InstructionLength(
                opcode, body.Schema);
            if (opcode == 0xD8)
            {
                var selection = body.GetRecord(
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        code[(position + 2)..]));
                if (selection[0] == 0x04 && selection[4] == 3
                    && selection[5] == 1 && selection[12] == 2
                    && selection[13] == 3)
                {
                    Assert.That(checkedAddressOffset, Is.EqualTo(-1));
                    checkedAddressOffset = position;
                }
            }
            else if (opcode == 0xEC && checkedAddressOffset >= 0
                && storageAddressOffset < 0)
            {
                storageAddressOffset = position;
            }
            else if (opcode == 0xE0)
            {
                var callRecord = body.GetRecord(
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        code[(position + 2)..]));
                if (callRecord[4] == 1)
                {
                    directCallOffsets.Add(position);
                }
            }
            else if (opcode == 0xD9)
            {
                storeOffsets.Add(position);
            }

            position += instructionLength;
        }
        Assert.That(checkedAddressOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(storageAddressOffset,
            Is.GreaterThan(checkedAddressOffset));
        Assert.That(directCallOffsets, Has.Count.EqualTo(2));
        Assert.That(storageAddressOffset,
            Is.LessThan(directCallOffsets[0]));
        Assert.That(directCallOffsets[^1],
            Is.LessThan(storeOffsets.First(offset =>
                offset > directCallOffsets[^1])));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var checkedAddressCalls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundCall call
                    && loaded.Functions[call.Function].Name == "ReadElement")
                {
                    checkedAddressCalls.Add(call);
                }
            }), Is.True);
        var checkedAddressCall = checkedAddressCalls.Single();
        var address = checkedAddressCall.Arguments
            .OfType<BoundNativeStorageAddress>().Single();
        Assert.That(address.Index, Is.TypeOf<BoundRead>());
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
    }

    [Test]
    public void StandardA2EUCapturesAndReconstructsUnitConditionalArms()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        const string moduleIdentity = "a2eu-unit-array-conditional";
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void RecordFirst(int32 value);

            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern void RecordSecond(ptr<uint32> value);

            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void RecordThird(uint32 value);

            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
            internal extern void Observe(
                ptr<uint32> value,
                write ptr<int32> output);

            int32 Main() {
                int32 zeroIndex = 0;
                bool choose = true;
                bool useFirst = false;
                int32 numerator = 0;
                int32 divisor = -1;
                uint32 unsignedNumerator = 0;
                uint32 unsignedDivisor = 0;
                int32 invalidIndex = -1;
                mut [uint32; 1] source = [0];
                mut uint32 input = 0;
                mut int32 output = 0;
                unsafe {
                    mut [void; 2] values = [
                        choose
                            ? (useFirst
                                ? RecordFirst(numerator / divisor)
                                : RecordSecond(&source[invalidIndex]))
                            : RecordThird(unsignedNumerator % unsignedDivisor),
                        ()
                    ];
                    var callback = &Observe;
                    callback(&input, &output);
                }
                return output;
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                        artifact));
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                callerRow = row;
                break;
            }
        }
        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        Assert.That(body.BlockCount, Is.EqualTo(7));

        var conditionalEdges = new List<(
            int BlockIndex, int RecordIndex, int TrueBlock, int FalseBlock)>();
        var directCallBlocks = new List<int>();
        var storeBlocks = new List<int>();
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var code = body.GetBlockCode(blockIndex);
            for (var position = 0; position < code.Length;)
            {
                var opcode = code[position];
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(opcode,
                        body.Schema);
                if (opcode == 0xE4)
                {
                    var conditionalEdgeIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            code[(position + 2)..]);
                    var edge = body.GetRecord(conditionalEdgeIndex);
                    conditionalEdges.Add((blockIndex, conditionalEdgeIndex,
                        BinaryPrimitives.ReadUInt16LittleEndian(edge[4..]),
                        BinaryPrimitives.ReadUInt16LittleEndian(edge[6..])));
                }
                else if (opcode == 0xE0)
                {
                    var callSite = body.GetRecord(
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            code[(position + 2)..]));
                    if (callSite[4] == 1)
                    {
                        directCallBlocks.Add(blockIndex);
                    }
                }
                else if (opcode == 0xD9)
                {
                    storeBlocks.Add(blockIndex);
                }

                position += instructionLength;
            }
        }

        Assert.That(conditionalEdges, Has.Count.EqualTo(2));
        var (BlockIndex, RecordIndex, TrueBlock, FalseBlock) = conditionalEdges.Single(static edge =>
            edge.BlockIndex == 0);
        var innerEdge = conditionalEdges.Single(static edge =>
            edge.BlockIndex == 1);
        Assert.That(RecordIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(innerEdge.RecordIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(body.GetRecord(RecordIndex)[0],
            Is.EqualTo(0x06));
        Assert.That(body.GetRecord(innerEdge.RecordIndex)[0],
            Is.EqualTo(0x06));
        Assert.That(TrueBlock, Is.Not.EqualTo(FalseBlock));
        Assert.That(innerEdge.TrueBlock, Is.Not.EqualTo(innerEdge.FalseBlock));
        Assert.That(directCallBlocks,
            Is.EquivalentTo(new[] {
                innerEdge.TrueBlock, innerEdge.FalseBlock, FalseBlock,
            }));
        var unitStoreBlocks = storeBlocks.Where(blockIndex =>
            blockIndex > Math.Max(innerEdge.FalseBlock,
                FalseBlock)).ToArray();
        Assert.That(unitStoreBlocks, Has.Length.EqualTo(2));
        Assert.That(unitStoreBlocks.All(blockIndex =>
            blockIndex == unitStoreBlocks[0]), Is.True);
        var trueArmCode = body.GetBlockCode(innerEdge.TrueBlock);
        var falseArmCode = body.GetBlockCode(innerEdge.FalseBlock);
        var outerFalseArmCode = body.GetBlockCode(FalseBlock);
        Assert.That(trueArmCode.IndexOf((byte)0x24),
            Is.GreaterThanOrEqualTo(0));
        Assert.That(falseArmCode.IndexOf((byte)0xD8),
            Is.GreaterThanOrEqualTo(0));
        Assert.That(falseArmCode.IndexOf((byte)0xEC),
            Is.GreaterThan(falseArmCode.IndexOf((byte)0xD8)));
        Assert.That(outerFalseArmCode.IndexOf((byte)0x25),
            Is.GreaterThanOrEqualTo(0));
        static int FindInstructionOffset(ReadOnlySpan<byte> code,
            byte targetOpcode)
        {
            for (var position = 0; position < code.Length;)
            {
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        code[position], TrRmArtifactSchema
                            .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft);
                if (code[position] == targetOpcode)
                {
                    return position;
                }

                position += instructionLength;
            }

            return -1;
        }

        Assert.That(FindInstructionOffset(trueArmCode, 0x24),
            Is.LessThan(FindInstructionOffset(trueArmCode, 0xE0)));
        var falsePlaceOffset = FindInstructionOffset(falseArmCode, 0xD8);
        Assert.That(falsePlaceOffset,
            Is.LessThan(FindInstructionOffset(falseArmCode, 0xEC)));
        Assert.That(FindInstructionOffset(falseArmCode, 0xEC),
            Is.LessThan(FindInstructionOffset(falseArmCode, 0xE0)));
        Assert.That(FindInstructionOffset(outerFalseArmCode, 0x25),
            Is.LessThan(FindInstructionOffset(outerFalseArmCode, 0xE0)));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var conditionals = new List<BoundIf>();
        Assert.That(BoundTreeTraversal.VisitStatements(loadedMain.Body,
            statement =>
            {
                if (statement is BoundIf branch)
                {
                    conditionals.Add(branch);
                }
            }), Is.True);
        Assert.That(conditionals, Has.Count.EqualTo(2));
        var outerConditional = conditionals.Single(branch =>
        {
            var nestedConditionals = 0;
            Assert.That(BoundTreeTraversal.VisitStatements(
                new BoundBlock([branch.Then], default),
                statement =>
                {
                    if (statement is BoundIf)
                    {
                        nestedConditionals++;
                    }
                }), Is.True);
            return nestedConditionals == 1;
        });
        var innerConditional = conditionals.Single(branch =>
            !ReferenceEquals(branch, outerConditional));
        static string[] GetCallNames(BoundStatement statement,
            BoundProgram program)
        {
            var names = new List<string>();
            Assert.That(BoundTreeTraversal.VisitExpressions(
                new BoundBlock([statement], default),
                expression =>
                {
                    if (expression is BoundCall call)
                    {
                        names.Add(program.Functions[call.Function].Name);
                    }
                }), Is.True);
            return [.. names];
        }

        Assert.That(GetCallNames(outerConditional.Then, loaded),
            Is.EquivalentTo(s_selectedRecordCallNames));
        Assert.That(GetCallNames(outerConditional.Else!, loaded),
            Is.EqualTo(["RecordThird"]));
        Assert.That(GetCallNames(innerConditional.Then, loaded),
            Is.EqualTo(["RecordFirst"]));
        Assert.That(GetCallNames(innerConditional.Else!, loaded),
            Is.EqualTo(["RecordSecond"]));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));

        var malformed = RewriteA2EUConditionalRegisterArtifact(artifact, 0,
            14);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity, malformed),
                moduleIdentity, Array.Empty<TrRmStandardArtifactInput>()));
        var malformedTarget = RewriteA2EUConditionalTargetArtifact(artifact,
            RecordIndex, checked((ushort)body.BlockCount));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    malformedTarget),
                moduleIdentity, Array.Empty<TrRmStandardArtifactInput>()));
    }

    [Test]
    public void StandardA2EURejectsMismatchedComparisonPredicateRegister()
    {
        const string moduleIdentity
            = "a2eu-unit-conditional-comparison-malformed";
        var input = CreateStandardA2EUConditionalRuntimeInput(true,
            moduleIdentity, false, "markedError > clearError");
        var malformed = RewriteA2EUComparisonDestinationArtifact(
            input.Artifact.ToArray(), 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(input.SchemaIdentity,
                    malformed),
                moduleIdentity, []));
    }

    [Test]
    public void StandardA2EUScalarFrozenSchemaDoesNotRequireUnusedFeature()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1";
        const string moduleIdentity = "a2eu-scalar-frozen-no-use";
        var input = CreateStandardA2EUConditionalRuntimeInput(true,
            moduleIdentity, false, schemaIdentity: schemaIdentity);
        var artifactIndex
            = TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                    input.Artifact.Span);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                artifactIndex);
        var mainRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                mainRow = row;
                break;
            }
        }

        Assert.That(mainRow, Is.GreaterThan(0));
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(mainRow), tables.Contracts.Count);
        Assert.That(body.ConstantCount, Is.Zero);
        Assert.That(tables.Features.TryGetFeature(
            TrRmFeatureTableReader.TypedFrozenValuesFeatureId, out _),
            Is.False);

        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(input.Artifact.ToArray()));
    }

    [Test]
    public void StandardA2EUScalarFrozenValuesRejectMalformedReferencesAndLegacySelection()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1";
        const string legacySchemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        const string moduleIdentity = "a2eu-scalar-frozen-malformed";
        var input = CreateStandardA2EUConditionalRuntimeInput(true,
            moduleIdentity, false, conditionExpression: "clearError == 0",
            schemaIdentity: schemaIdentity);
        var artifact = input.Artifact.ToArray();
        var artifactIndex = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                artifactIndex);
        var typedFrozenFeatureRow = 0;
        for (var row = 1; row <= tables.Features.Count; row++)
        {
            if (tables.Features.GetFeature(row).FeatureId
                == TrRmFeatureTableReader.TypedFrozenValuesFeatureId)
            {
                typedFrozenFeatureRow = row;
                break;
            }
        }
        Assert.That(typedFrozenFeatureRow, Is.GreaterThan(0));
        var missingTypedFrozenFeature = RewriteA2EUScalarFrozenSection(
            artifact, 0x0004, bytes =>
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(
                    (typedFrozenFeatureRow - 1) * 8),
                    TrRmFeatureTableReader.NativeBoundaryContractsFeatureId));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    missingTypedFrozenFeature),
                moduleIdentity, []));

        var malformedSources = RewriteA2EUScalarFrozenSection(artifact,
            0x0030, static bytes =>
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4),
                    0x00FF_FFFF));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    malformedSources),
                moduleIdentity, []));

        var malformedNodes = RewriteA2EUScalarFrozenSection(artifact,
            0x003F, static bytes =>
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8),
                    0x00FF_FFFF));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    malformedNodes),
                moduleIdentity, []));

        var malformedConstantMap
            = RewriteA2EUScalarConstantSourceMap(artifact, 0x00FF_FFFF);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    malformedConstantMap),
                moduleIdentity, []));

        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(legacySchemaIdentity,
                    artifact),
                moduleIdentity, []));
    }

    [TestCase(true, false, "choose", false)]
    [TestCase(false, false, "choose", false)]
    [TestCase(true, true, "choose", false)]
    [TestCase(true, false,
        "choose || IsProcessorFeaturePresent(markedError / clearError)", false)]
    [TestCase(false, false,
        "choose && IsProcessorFeaturePresent(markedError / clearError)", false)]
    [TestCase(true, false, "!choose", false)]
    [TestCase(true, false, "choose ? false : true", false)]
    [TestCase(true, false, "true", false)]
    [TestCase(false, false, "false", false)]
    [TestCase(true, false,
        "true || IsProcessorFeaturePresent(markedError / clearError)", false)]
    [TestCase(false, false,
        "false && IsProcessorFeaturePresent(markedError / clearError)", false)]
    [TestCase(true, false, "markedError != clearError", false)]
    [TestCase(true, false, "markedError == clearError", false)]
    [TestCase(true, false, "markedError < clearError", false)]
    [TestCase(true, false, "markedError <= clearError", false)]
    [TestCase(true, false, "markedError > clearError", false)]
    [TestCase(true, false, "markedError >= clearError", false)]
    [TestCase(true, false, "clearError == 0", false)]
    [TestCase(true, false, "0 == clearError", false)]
    [TestCase(true, false, "clearError != 0", false)]
    [TestCase(true, false, "0 != clearError", false)]
    [TestCase(true, false, "clearError < 1", false)]
    [TestCase(true, false, "0 <= clearError", false)]
    [TestCase(true, false, "1 > clearError", false)]
    [TestCase(true, false, "clearError >= 0", false)]
    [TestCase(true, false, "markedError == 0", false)]
    [TestCase(true, false, "0 == markedError", false)]
    [TestCase(true, false, "markedError != 0", false)]
    [TestCase(true, false, "0 != markedError", false)]
    [TestCase(true, false, "GetCurrentProcessId() > 0", false)]
    [TestCase(true, false, "0 == GetCurrentProcessId()", false)]
    [TestCase(true, false, "GetCurrentProcessId() == 0", false)]
    [TestCase(true, false, "uint32(0) == uint32(0)", false)]
    [TestCase(true, false, "choose == other", false)]
    [TestCase(false, false, "choose == other", false)]
    [TestCase(true, false, "choose != other", false)]
    [TestCase(false, false, "choose != other", false)]
    [TestCase(true, false, "choose == true", false)]
    [TestCase(false, false, "choose == true", false)]
    [TestCase(true, false, "choose == false", false)]
    [TestCase(false, false, "choose == false", false)]
    [TestCase(true, false, "choose != true", false)]
    [TestCase(false, false, "choose != true", false)]
    [TestCase(true, false, "choose != false", false)]
    [TestCase(false, false, "choose != false", false)]
    [TestCase(true, false, "clearError == GetCurrentProcessId()", false)]
    [TestCase(true, false, "GetCurrentProcessId() > clearError", false)]
    [TestCase(true, false,
        "GetCurrentProcessId() == GetCurrentProcessId()", false)]
    [TestCase(true, false,
        "IsProcessorFeaturePresent(markedError) == IsProcessorFeaturePresent(markedError)", false)]
    [TestCase(true, false,
        "choose || IsProcessorFeaturePresent(markedError / clearError)", true)]
    public void StandardA2EUConditionalJitExecutesOnlyTheSelectedNativeArm(
        bool choose, bool failFalseArm, string conditionExpression,
        bool useIfStatement)
    {
        const string moduleIdentity = "a2eu-unit-conditional-jit";
        AssertA2EUWindowsX64();
        var schemaIdentity = GetA2EUConditionalSchemaIdentity(
            conditionExpression);
        var input = CreateStandardA2EUConditionalRuntimeInput(choose,
            moduleIdentity, failFalseArm, conditionExpression,
            useIfStatement, schemaIdentity);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        if (conditionExpression is
            "choose || IsProcessorFeaturePresent(markedError / clearError)"
            or "choose && IsProcessorFeaturePresent(markedError / clearError)")
        {
            var main = loaded.Functions.Single(static function =>
                function.Name == "Main");
            var conditionalBlocks = new List<BoundIf>();
            Assert.That(BoundTreeTraversal.VisitStatements(main.Body,
                statement =>
                {
                    if (statement is BoundIf conditional)
                    {
                        conditionalBlocks.Add(conditional);
                    }
                }), Is.True);
            Assert.That(conditionalBlocks, Has.Count.GreaterThanOrEqualTo(2));
        }

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, input.SchemaIdentity, 1);
        Assert.That(reemitted, Is.EqualTo(input.Artifact.ToArray()));
        if (conditionExpression == "clearError == GetCurrentProcessId()")
        {
            AssertA2EUComparisonCallSequence(input.Artifact.ToArray(), 2,
                requiresReadSnapshot: true);
        }
        else if (conditionExpression
            == "GetCurrentProcessId() == GetCurrentProcessId()")
        {
            AssertA2EUComparisonCallSequence(input.Artifact.ToArray(), 3,
                requiresReadSnapshot: false);
        }

        var result = NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast);
        var conditionValue = conditionExpression switch {
            "choose" => choose,
            "!choose" or "choose ? false : true" => !choose,
            "true" or "true || IsProcessorFeaturePresent(markedError / clearError)" => true,
            "false" or "false && IsProcessorFeaturePresent(markedError / clearError)" => false,
            "choose || IsProcessorFeaturePresent(markedError / clearError)" => true,
            "choose && IsProcessorFeaturePresent(markedError / clearError)" => false,
            "markedError != clearError" or "markedError > clearError"
                or "markedError >= clearError" => true,
            "markedError == clearError" or "markedError < clearError"
                or "markedError <= clearError" => false,
            "clearError == 0" or "0 == clearError"
                or "clearError < 1" or "0 <= clearError"
                or "1 > clearError" or "clearError >= 0"
                or "uint32(0) == uint32(0)" => true,
            "clearError != 0" or "0 != clearError" => false,
            "markedError == 0" or "0 == markedError"
                or "0 == GetCurrentProcessId()"
                or "GetCurrentProcessId() == 0" => false,
            "markedError != 0" or "0 != markedError"
                or "GetCurrentProcessId() > 0" => true,
            "choose == other" => choose,
            "choose != other" => !choose,
            "choose == true" or "choose != false" => choose,
            "choose == false" or "choose != true" => !choose,
            "clearError == GetCurrentProcessId()" => false,
            "GetCurrentProcessId() > clearError" => true,
            "GetCurrentProcessId() == GetCurrentProcessId()"
                or "IsProcessorFeaturePresent(markedError) == IsProcessorFeaturePresent(markedError)" => true,
            _ => throw new InvalidOperationException(
                "The A2EU test condition has no expected normal result."),
        };
        var expected = conditionValue ? 0 : Environment.ProcessId;
        TestContext.WriteLine(
            $"A2EU source-free JIT: choose={choose}; failFalseArm={failFalseArm}; useIfStatement={useIfStatement}; condition={conditionExpression}; policy=FailFast; expected={expected}; actual={result}.");
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(true, false, "choose", false)]
    [TestCase(false, false, "choose", false)]
    [TestCase(true, true, "choose", false)]
    [TestCase(false, true, "choose", false)]
    [TestCase(true, false,
        "choose || IsProcessorFeaturePresent(markedError / clearError)", false)]
    [TestCase(false, false,
        "choose || IsProcessorFeaturePresent(markedError / clearError)", false)]
    [TestCase(false, false,
        "choose && IsProcessorFeaturePresent(markedError / clearError)", false)]
    [TestCase(true, false,
        "choose && IsProcessorFeaturePresent(markedError / clearError)", false)]
    [TestCase(true, false,
        "true || IsProcessorFeaturePresent(markedError / clearError)", false)]
    [TestCase(false, false,
        "false && IsProcessorFeaturePresent(markedError / clearError)", false)]
    [TestCase(false, false,
        "choose || IsProcessorFeaturePresent(markedError / clearError)", true)]
    [TestCase(true, false, "markedError > clearError", false)]
    [TestCase(true, false, "markedError <= clearError", false)]
    [TestCase(true, false, "choose == other", false)]
    [TestCase(false, false, "choose == other", false)]
    [TestCase(true, false, "choose != other", false)]
    [TestCase(false, false, "choose != other", false)]
    [TestCase(true, false, "choose == true", false)]
    [TestCase(false, false, "choose == true", false)]
    [TestCase(true, false, "choose == false", false)]
    [TestCase(false, false, "choose == false", false)]
    [TestCase(true, false, "choose != true", false)]
    [TestCase(false, false, "choose != true", false)]
    [TestCase(true, false, "choose != false", false)]
    [TestCase(false, false, "choose != false", false)]
    [TestCase(true, false, "clearError == GetCurrentProcessId()", false)]
    [TestCase(true, false, "GetCurrentProcessId() > clearError", false)]
    [TestCase(true, false, "clearError == 0", false)]
    [TestCase(true, false, "0 == clearError", false)]
    [TestCase(true, false, "markedError == 0", true)]
    [TestCase(true, false, "GetCurrentProcessId() > 0", false)]
    [TestCase(true, false, "uint32(0) == uint32(0)", false)]
    [TestCase(true, false,
        "GetCurrentProcessId() == GetCurrentProcessId()", false)]
    [TestCase(true, false,
        "IsProcessorFeaturePresent(markedError) == IsProcessorFeaturePresent(markedError)", false)]
    [TestCase(true, false,
        "IsProcessorFeaturePresent(markedError / clearError) == other", false)]
    public async Task StandardA2EUConditionalAotExecutesOnlyTheSelectedNativeArm(
        bool choose, bool failFalseArm, string conditionExpression,
        bool useIfStatement)
    {
        const string moduleIdentity = "a2eu-unit-conditional-aot";
        AssertA2EUWindowsX64();
        var schemaIdentity = GetA2EUConditionalSchemaIdentity(
            conditionExpression);
        var input = CreateStandardA2EUConditionalRuntimeInput(choose,
            moduleIdentity, failFalseArm, conditionExpression,
            useIfStatement, schemaIdentity);
        var image = NativeModuleCompiler.EmitStandardExecutable(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast);
        var imagePath = Path.Combine(Path.GetTempPath(),
            $"terrum-{moduleIdentity}-{Guid.NewGuid():N}.exe");
        Process? process = null;
        try
        {
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            process = Process.Start(new ProcessStartInfo(imagePath) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException(
                "The A2EU native executable was not started.");
            var processId = process.Id;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(true);
            }
            catch (TimeoutException exception)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                throw new TimeoutException(
                    $"A2EU native executable exceeded its 10-second deadline. {await stdout.ConfigureAwait(true)}{Environment.NewLine}{await stderr.ConfigureAwait(true)}",
                    exception);
            }

            var output = await stdout.ConfigureAwait(true);
            var error = await stderr.ConfigureAwait(true);
            var conditionCallIsSelected
                = conditionExpression switch {
                    "choose || IsProcessorFeaturePresent(markedError / clearError)"
                        => !choose,
                    "choose && IsProcessorFeaturePresent(markedError / clearError)"
                        => choose,
                    "IsProcessorFeaturePresent(markedError / clearError) == other"
                        => true,
                    _ => false,
                };
            var conditionValue = conditionExpression switch {
                "choose" => choose,
                "!choose" or "choose ? false : true" => !choose,
                "true" or "true || IsProcessorFeaturePresent(markedError / clearError)" => true,
                "false" or "false && IsProcessorFeaturePresent(markedError / clearError)" => false,
                "choose || IsProcessorFeaturePresent(markedError / clearError)" => true,
                "choose && IsProcessorFeaturePresent(markedError / clearError)" => false,
                "markedError > clearError" => true,
                "markedError <= clearError" => false,
                "clearError == 0" or "0 == clearError"
                    or "uint32(0) == uint32(0)" => true,
                "markedError == 0" => false,
                "GetCurrentProcessId() > 0" => true,
                "choose == other" => choose,
                "choose != other" => !choose,
                "choose == true" or "choose != false" => choose,
                "choose == false" or "choose != true" => !choose,
                "clearError == GetCurrentProcessId()" => false,
                "GetCurrentProcessId() > clearError" => true,
                "GetCurrentProcessId() == GetCurrentProcessId()"
                    or "IsProcessorFeaturePresent(markedError) == IsProcessorFeaturePresent(markedError)" => true,
                "IsProcessorFeaturePresent(markedError / clearError) == other" => false,
                _ => throw new InvalidOperationException(
                    "The A2EU test condition has no expected normal result."),
            };
            var expected = conditionCallIsSelected
                    || (failFalseArm && !conditionValue)
                ? unchecked((int)0xE0540002)
                : conditionValue ? 0 : processId;
            TestContext.WriteLine(
                $"A2EU source-free AOT: choose={choose}; failFalseArm={failFalseArm}; useIfStatement={useIfStatement}; condition={conditionExpression}; policy=FailFast; pid={processId}; expected={expected}; actual={process.ExitCode}; image-sha256={Convert.ToHexString(SHA256.HashData(image))}; deadline=10s.");
            Assert.That(output, Is.Empty);
            Assert.That(error, Is.Empty);
            Assert.That(process.ExitCode, Is.EqualTo(expected));
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                process.Dispose();
            }

            await TestFileCleanup.DeleteWithRetryAsync(imagePath)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public void StandardA2EUScalarLiteralComparisonTypesAndPredicatesRoundTrip()
    {
        const string moduleIdentity = "a2eu-scalar-literal-matrix";
        var scalarTypes = new (string TypeName, string VariableName,
            string Zero, string One)[] {
            ("int8", "signed8", "0", "1"),
            ("int16", "signed16", "0", "1"),
            ("int32", "signed32", "0", "1"),
            ("int64", "signed64", "0", "1"),
            ("uint8", "unsigned8", "0", "1"),
            ("uint16", "unsigned16", "0", "1"),
            ("uint32", "unsigned32", "0", "1"),
            ("uint64", "unsigned64", "0", "1"),
            ("float32", "single", "0.0", "1.0"),
            ("float64", "double", "0.0", "1.0"),
        };
        var declarations = new StringBuilder();
        var falseComparisons = new List<string>(61);
        foreach (var (TypeName, VariableName, Zero, One) in scalarTypes)
        {
            _ = declarations.Append(TypeName).Append(' ')
                .Append(VariableName).Append(" = ")
                .Append(Zero).AppendLine(";");
            falseComparisons.Add(
                $"{VariableName} == {One}");
            falseComparisons.Add(
                $"{VariableName} != {Zero}");
            falseComparisons.Add(
                $"{VariableName} < {Zero}");
            falseComparisons.Add(
                $"{One} <= {VariableName}");
            falseComparisons.Add(
                $"{Zero} > {VariableName}");
            falseComparisons.Add(
                $"{VariableName} >= {One}");
        }

        falseComparisons.Add("uint32(0) == uint32(1)");
        var additionalStatements = new StringBuilder(
            "SetLastError(markedError);");
        foreach (var comparison in falseComparisons)
        {
            _ = additionalStatements.Append(" if (")
                .Append(comparison)
                .Append(") { SetLastError(clearError); }");
        }
        var input = CreateStandardA2EUConditionalRuntimeInput(true,
            moduleIdentity, false, "choose", schemaIdentity:
                A2EUScalarFrozenValuesSchemaIdentity,
            additionalLocals: declarations.ToString(),
            additionalStatements: additionalStatements.ToString());
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                        input.Artifact.Span));
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                callerRow = row;
                break;
            }
        }

        Assert.That(callerRow, Is.GreaterThan(0));
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        Assert.That(body.ConstantCount, Is.EqualTo(20));
        Assert.That(tables.ConstantSources.Count, Is.EqualTo(20));
        Assert.That(tables.FrozenValueNodes.Count, Is.EqualTo(20));
        Assert.That(TrRmFeatureManifestValidator.Requires(tables.Features,
            tables.Modules, 1,
            TrRmFeatureTableReader.TypedFrozenValuesFeatureId), Is.True);

        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        var comparisons = new List<BoundBinary>();
        var main = loaded.Functions.Single(static function =>
            function.Name == "Main");
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundBinary {
                        Type: PrimitiveType.Bool,
                    } comparison
                    && (comparison.Left is BoundConstant
                        || comparison.Right is BoundConstant))
                {
                    comparisons.Add(comparison);
                }
            }), Is.True);
        Assert.That(comparisons, Has.Count.EqualTo(60));
        foreach (var (TypeName, VariableName, Zero, One) in scalarTypes)
        {
            var boundType = TypeName switch {
                "int8" => PrimitiveType.Int8,
                "int16" => PrimitiveType.Int16,
                "int32" => PrimitiveType.Int32,
                "int64" => PrimitiveType.Int64,
                "uint8" => PrimitiveType.UInt8,
                "uint16" => PrimitiveType.UInt16,
                "uint32" => PrimitiveType.UInt32,
                "uint64" => PrimitiveType.UInt64,
                "float32" => PrimitiveType.Float32,
                "float64" => PrimitiveType.Float64,
                _ => throw new InvalidOperationException(
                    "The A2EU test type has no bound primitive."),
            };
            Assert.That(comparisons.Count(comparison =>
                comparison.Left.Type == boundType),
                Is.EqualTo(6));
        }

        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, A2EUScalarFrozenValuesSchemaIdentity, 1),
            Is.EqualTo(input.Artifact.ToArray()));
        var result = NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast);
        TestContext.WriteLine(
            $"A2EU+SF1 scalar matrix source-free JIT: expected={Environment.ProcessId}; actual={result}; comparisons={comparisons.Count}; constants={body.ConstantCount}; policy=FailFast.");
        Assert.That(result, Is.EqualTo(Environment.ProcessId));
    }

    private static TrRmStandardArtifactInput
        CreateStandardA2EUConditionalRuntimeInput(bool choose,
            string moduleIdentity, bool failFalseArm,
            string conditionExpression = "choose",
            bool useIfStatement = false,
            string? schemaIdentity = null,
            string additionalLocals = "",
            string additionalStatements = "")
    {
        schemaIdentity ??=
            "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var falseAction = failFalseArm
            ? "SetLastError(markedError / clearError)"
            : "SetLastError(markedError)";
        var conditional = useIfStatement
            ? $"if ({conditionExpression}) {{ SetLastError(clearError); }} "
                + $"else {{ {falseAction}; }}"
            : $"({conditionExpression}) ? SetLastError(clearError) : {falseAction};";
        var source = $$"""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern bool IsProcessorFeaturePresent(uint32 feature);

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void SetLastError(uint32 errorCode);

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetLastError",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            // Both selected values fit the test's signed result surface.
            internal extern int32 GetLastError();

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetCurrentProcessId",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 GetCurrentProcessId();

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern int32 IsBadReadPtr(
                ptr<int32> memory,
                uint64 byteCount);

            int32 Main() {
                unsafe {
                    bool choose = {{(choose ? "true" : "false")}};
                    bool other = true;
                    uint32 clearError = 0;
                    uint32 markedError = GetCurrentProcessId();
                    {{additionalLocals}}
                    {{conditional}}
                    {{additionalStatements}}
                    int32 observedError = GetLastError();
                    uint64 byteCount = 0;
                    var callback = &IsBadReadPtr;
                    callback(&observedError, byteCount);
                    return observedError;
                }
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        TestContext.WriteLine(
            $"A2EU source-free runtime input: schema={schemaIdentity}; module={moduleIdentity}; source-sha256={Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)))}; artifact-sha256={Convert.ToHexString(SHA256.HashData(artifact))}; compiler={typeof(TrRmExecutableCompiler).Assembly.Location}; compiler-sha256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(TrRmExecutableCompiler).Assembly.Location)))}.");
        return new TrRmStandardArtifactInput(schemaIdentity, artifact);
    }

    private static bool IsA2EUScalarLiteralComparison(
        string conditionExpression)
        => conditionExpression is "clearError == 0" or "0 == clearError"
            or "clearError != 0" or "0 != clearError"
            or "clearError < 1" or "0 <= clearError"
            or "1 > clearError" or "clearError >= 0"
            or "markedError == 0" or "0 == markedError"
            or "markedError != 0" or "0 != markedError"
            or "GetCurrentProcessId() > 0"
            or "0 == GetCurrentProcessId()"
            or "GetCurrentProcessId() == 0"
            or "uint32(0) == uint32(0)";

    private static string GetA2EUConditionalSchemaIdentity(
        string conditionExpression)
        => IsA2EUScalarLiteralComparison(conditionExpression)
            ? A2EUScalarFrozenValuesSchemaIdentity
            : "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";

    private static void AssertA2EUWindowsX64()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
    }

    private static void AssertA2EUComparisonCallSequence(byte[] artifact,
        int expectedCallCount, bool requiresReadSnapshot)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                artifact);
        _ = index.TryGetSection(0x001B, out _, out var contractCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            _ = flags;
            if (kind != 0x0020)
            {
                continue;
            }

            var bodyTable = new TrRmBodyTableReader(section, recordCount,
                schema);
            var body = new TrRmRegisterBodyReader(bodyTable.GetRow(1),
                contractCount, schema);
            for (var blockIndex = 0; blockIndex < body.BlockCount;
                blockIndex++)
            {
                var code = body.GetBlockCode(blockIndex);
                var callOffsets = new List<int>();
                var copyStoreOffsets = new List<int>();
                var compareOffset = -1;
                for (var position = 0; position < code.Length;)
                {
                    var opcode = code[position];
                    var instructionLength
                        = TrRmRegisterBodyReader.InstructionLength(
                            opcode, schema);
                    if (opcode == 0xE0)
                    {
                        callOffsets.Add(position);
                    }
                    else if (opcode == 0xD6)
                    {
                        copyStoreOffsets.Add(position);
                    }
                    else if (opcode == 0x40)
                    {
                        compareOffset = position;
                    }

                    position += instructionLength;
                }

                if (compareOffset < 0)
                {
                    continue;
                }

                Assert.That(callOffsets, Has.Count.EqualTo(expectedCallCount));
                Assert.That(callOffsets[^1], Is.LessThan(compareOffset));
                if (requiresReadSnapshot)
                {
                    Assert.That(copyStoreOffsets.Any(offset =>
                        offset > callOffsets[0]
                        && offset < callOffsets[1]), Is.True);
                }

                return;
            }
        }

        throw new InvalidOperationException(
            "The A2EU artifact has no scalar comparison call block.");
    }

    [Test]
    public void StandardA2EUConditionalBodySurvivesSelectedModuleBodyRowRemapping()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft;
        const string firstModuleIdentity = "a2eu-conditional-other";
        const string selectedModuleIdentity = "a2eu-conditional-selected";
        const string source = """
            [Terrum.Interop.NativeImport(
                "MODULE",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void RecordFirst();

            [Terrum.Interop.NativeImport(
                "MODULE",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void RecordSecond();

            [Terrum.Interop.NativeImport(
                "MODULE",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 ReadProcessId();

            [Terrum.Interop.NativeImport(
                "MODULE",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            internal extern void Observe(write ptr<int32> output);

            int32 Main() {
                uint32 zero = 0;
                mut int32 output = 0;
                unsafe {
                    uint32 processId = ReadProcessId();
                    processId > zero
                        ? RecordFirst()
                        : RecordSecond();
                    var callback = &Observe;
                    callback(&output);
                }
                return output;
            }
            """;
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var firstCompilation = Compilation.Create(
            SyntaxTree.Parse(source.Replace("MODULE", "other.dll",
                StringComparison.Ordinal)), options);
        var selectedCompilation = Compilation.Create(
            SyntaxTree.Parse(source.Replace("MODULE", "selected.dll",
                StringComparison.Ordinal)), options);
        Assert.That(firstCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, firstCompilation.Diagnostics));
        Assert.That(selectedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                selectedCompilation.Diagnostics));

        var firstArtifact = TrRmExecutableCompiler.EmitStandard(
            firstCompilation.Program!, firstModuleIdentity, schemaIdentity, 1);
        var selectedArtifact = TrRmExecutableCompiler.EmitStandard(
            selectedCompilation.Program!, selectedModuleIdentity,
            schemaIdentity, 1);
        var combinedArtifact = CombinePopulatedNativeAddressModules(
            firstArtifact, selectedArtifact, schema);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                            combinedArtifact));
        var selectedModuleRow = 0;
        for (var moduleRow = 1; moduleRow <= tables.Modules.Count;
            moduleRow++)
        {
            if (tables.Modules.GetIdentity(moduleRow)
                == selectedModuleIdentity)
            {
                selectedModuleRow = moduleRow;
                break;
            }
        }
        Assert.That(selectedModuleRow, Is.EqualTo(2));
        var callerRow = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            if (tables.Methods.GetOwnerModule(methodRow)
                    == selectedModuleRow
                && tables.Methods.GetName(methodRow) == "Main")
            {
                callerRow = methodRow;
                break;
            }
        }
        Assert.That(callerRow, Is.GreaterThan(0));
        Assert.That(tables.Methods.GetBodyRow(callerRow), Is.EqualTo(2));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        Assert.That(body.BlockCount, Is.EqualTo(4));
        var edgeRecordIndex = -1;
        foreach (var blockIndex in Enumerable.Range(0, body.BlockCount))
        {
            var code = body.GetBlockCode(blockIndex);
            for (var position = 0; position < code.Length;)
            {
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        code[position], body.Schema);
                if (code[position] == 0xE4)
                {
                    edgeRecordIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            code[(position + 2)..]);
                }

                position += instructionLength;
            }
        }
        Assert.That(edgeRecordIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(body.GetRecord(edgeRecordIndex)[0], Is.EqualTo(0x06));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, combinedArtifact),
            selectedModuleIdentity, []);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "Observe").DirectNativeImport!.LibraryName,
            Is.EqualTo("selected.dll"));
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var selectedCalls = new List<string>();
        var conditionalCount = 0;
        Assert.That(BoundTreeTraversal.VisitStatements(loadedMain.Body,
            statement =>
            {
                if (statement is BoundIf)
                {
                    conditionalCount++;
                }
            }), Is.True);
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundCall call
                    && loaded.Functions[call.Function].Name
                        is "RecordFirst" or "RecordSecond")
                {
                    selectedCalls.Add(loaded.Functions[call.Function].Name);
                }
            }), Is.True);
        Assert.That(conditionalCount, Is.EqualTo(1));
        Assert.That(selectedCalls,
            Is.EquivalentTo(s_selectedRecordCallNames));
    }

    [Test]
    public void StandardA2EUScalarLiteralProviderBodySurvivesCoLocatedRemappingAndReplacement()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1";
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft;
        const string providerIdentity = "a2eu-conditional-provider";
        const string consumerIdentity = "a2eu-conditional-consumer";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        const string providerSource = """
            [Terrum.Interop.NativeImport(
                "provider.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            public extern void Observe(write ptr<int32> output);

            internal void ProviderBody(write ptr<int32> output) {
                unsafe {
                    var callback = &Observe;
                    callback(output);
                }
            }
            """;
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource), core);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, schemaIdentity, 1);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerIndex = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                providerArtifact);
        var providerTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                providerIndex);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, []);
        var consumerOptions = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "consumer.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void RecordFirst();

            [Terrum.Interop.NativeImport(
                "consumer.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void RecordSecond();

            [Terrum.Interop.NativeImport(
                "consumer.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 GetCode();

            int32 Main() {
                mut int32 output = 0;
                unsafe {
                    GetCode() == 1 ? RecordFirst() : RecordSecond();
                    var callback = &Observe;
                    callback(&output);
                }
                return output;
            }
            """), providerReference, consumerOptions);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerCompilation.Program!, consumerIdentity, schemaIdentity, 1);
        var combinedArtifact = CombinePopulatedNativeAddressModules(
            providerArtifact, consumerArtifact, schema);
        var combinedIndex = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                combinedArtifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                combinedIndex);
        var consumerModuleRow = 0;
        for (var moduleRow = 1; moduleRow <= tables.Modules.Count;
            moduleRow++)
        {
            if (tables.Modules.GetIdentity(moduleRow) == consumerIdentity)
            {
                consumerModuleRow = moduleRow;
                break;
            }
        }
        Assert.That(consumerModuleRow, Is.EqualTo(2));
        var mainRow = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            if (tables.Methods.GetOwnerModule(methodRow) == consumerModuleRow
                && tables.Methods.GetName(methodRow) == "Main")
            {
                mainRow = methodRow;
                break;
            }
        }
        Assert.That(mainRow, Is.GreaterThan(0));
        Assert.That(tables.Methods.GetBodyRow(mainRow), Is.EqualTo(2));
        tables.ValidateRegisterBodyMapsForMethod(mainRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(mainRow), tables.Contracts.Count);
        Assert.That(body.BlockCount, Is.EqualTo(4));
        Assert.That(tables.ConstantSources.Count,
            Is.GreaterThan(providerTables.ConstantSources.Count));
        Assert.That(tables.FrozenValueNodes.Count,
            Is.GreaterThan(providerTables.FrozenValueNodes.Count));
        Assert.That(tables.Blobs.Count, Is.GreaterThan(providerTables.Blobs.Count));
        Assert.That(body.ConstantCount, Is.GreaterThan(0));
        for (var offset = 0; offset < body.ConstantMap.Length;
            offset += 2 * sizeof(uint))
        {
            var sourceRow = BinaryPrimitives.ReadUInt32LittleEndian(
                body.ConstantMap[(offset + sizeof(uint))..]);
            Assert.That(sourceRow,
                Is.GreaterThan((uint)providerTables.ConstantSources.Count));
            var nodeRow = tables.ConstantSources.GetPayloadReference(
                checked((int)sourceRow));
            Assert.That(nodeRow,
                Is.GreaterThan((uint)providerTables.FrozenValueNodes.Count));
            var blobRow = tables.FrozenValueNodes.GetScalarBlobRef(
                checked((int)nodeRow));
            Assert.That(blobRow,
                Is.GreaterThan((uint)providerTables.Blobs.Count));
        }

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, combinedArtifact),
            consumerIdentity, []);
        var target = loaded.Functions.Single(static function =>
            function.Name == "Observe");
        Assert.That(target.ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(target.DirectNativeImport!.LibraryName,
            Is.EqualTo("provider.dll"));
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var conditionalCount = 0;
        var comparisonConstants = new List<BoundConstant>();
        var selectedCallNames = new List<string>();
        Assert.That(BoundTreeTraversal.VisitStatements(loadedMain.Body,
            statement =>
            {
                if (statement is BoundIf)
                {
                    conditionalCount++;
                }
            }), Is.True);
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundBinary {
                        Type: PrimitiveType.Bool,
                    } comparison)
                {
                    if (comparison.Left is BoundConstant left)
                    {
                        comparisonConstants.Add(left);
                    }
                    if (comparison.Right is BoundConstant right)
                    {
                        comparisonConstants.Add(right);
                    }
                }
            }), Is.True);
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundCall call
                    && loaded.Functions[call.Function].Name
                        is "RecordFirst" or "RecordSecond")
                {
                    selectedCallNames.Add(
                        loaded.Functions[call.Function].Name);
                }
            }), Is.True);
        Assert.That(conditionalCount, Is.EqualTo(1));
        Assert.That(comparisonConstants, Has.Count.EqualTo(1));
        Assert.That(comparisonConstants[0].Type, Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(comparisonConstants[0].Bits.Low, Is.EqualTo(1));
        Assert.That(selectedCallNames,
            Is.EquivalentTo(s_selectedRecordCallNames));
        var reencodedConsumerArtifact = TrRmExecutableCompiler.EmitStandard(
            loaded, consumerIdentity, schemaIdentity, 1);
        var reencodedIndex = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                reencodedConsumerArtifact);
        var reencodedTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                reencodedIndex);
        Assert.That(reencodedTables.ConstantSources.Count,
            Is.GreaterThan(0));
        var reencodedLoaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reencodedConsumerArtifact),
            consumerIdentity, [providerInput]);
        var reencodedMain = reencodedLoaded.Functions.Single(
            static function => function.Name == "Main");
        var reencodedComparisonConstants = new List<BoundConstant>();
        Assert.That(BoundTreeTraversal.VisitExpressions(reencodedMain.Body,
            expression =>
            {
                if (expression is BoundBinary {
                        Type: PrimitiveType.Bool,
                    } comparison
                    && comparison.Right is BoundConstant constant)
                {
                    reencodedComparisonConstants.Add(constant);
                }
            }), Is.True);
        Assert.That(reencodedComparisonConstants, Has.Count.EqualTo(1));
        Assert.That(reencodedComparisonConstants[0].Type,
            Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(reencodedComparisonConstants[0].Bits.Low, Is.EqualTo(1));

        const string secondConsumerIdentity
            = "a2eu-conditional-consumer-second";
        var secondConsumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerCompilation.Program!, secondConsumerIdentity,
            schemaIdentity, 1);
        var twiceCombinedArtifact = CombinePopulatedNativeAddressModules(
            combinedArtifact, secondConsumerArtifact, schema);
        var twiceCombinedIndex = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                twiceCombinedArtifact);
        var twiceCombinedTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                twiceCombinedIndex);
        var secondConsumerModuleRow = 0;
        for (var moduleRow = 1;
            moduleRow <= twiceCombinedTables.Modules.Count; moduleRow++)
        {
            if (twiceCombinedTables.Modules.GetIdentity(moduleRow)
                == secondConsumerIdentity)
            {
                secondConsumerModuleRow = moduleRow;
                break;
            }
        }
        Assert.That(secondConsumerModuleRow, Is.GreaterThan(0));
        var secondMainRow = 0;
        for (var methodRow = 1;
            methodRow <= twiceCombinedTables.Methods.Count; methodRow++)
        {
            if (twiceCombinedTables.Methods.GetOwnerModule(methodRow)
                    == secondConsumerModuleRow
                && twiceCombinedTables.Methods.GetName(methodRow) == "Main")
            {
                secondMainRow = methodRow;
                break;
            }
        }
        Assert.That(secondMainRow, Is.GreaterThan(0));
        var secondConsumerBodyRow
            = twiceCombinedTables.Methods.GetBodyRow(secondMainRow);
        Assert.That(secondConsumerBodyRow,
            Is.EqualTo(tables.Bodies.Count + 1));
        var secondConsumerBody = twiceCombinedTables.Bodies
            .GetRegisterBody(secondConsumerBodyRow,
                twiceCombinedTables.Contracts.Count);
        Assert.That(secondConsumerBody.ConstantCount, Is.EqualTo(1));
        var secondSourceRow = BinaryPrimitives.ReadUInt32LittleEndian(
            secondConsumerBody.ConstantMap[sizeof(uint)..]);
        Assert.That(secondSourceRow,
            Is.EqualTo((uint)tables.ConstantSources.Count + 1));
        var secondNodeRow = twiceCombinedTables.ConstantSources
            .GetPayloadReference(checked((int)secondSourceRow));
        Assert.That(secondNodeRow,
            Is.EqualTo((uint)tables.FrozenValueNodes.Count + 1));
        var secondBlobRow = twiceCombinedTables.FrozenValueNodes
            .GetScalarBlobRef(checked((int)secondNodeRow));
        Assert.That(secondBlobRow,
            Is.EqualTo((uint)tables.Blobs.Count + 1));
        Assert.That(twiceCombinedTables.FrozenValueNodes.GetScalarPayload(
            checked((int)secondNodeRow), twiceCombinedTables.Blobs).ToArray(),
            Is.EqualTo(new byte[] { 1, 0, 0, 0 }));

        var compatibleProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource.Replace("ProviderBody",
                "ReplacementBody", StringComparison.Ordinal)), core);
        Assert.That(compatibleProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                compatibleProviderCompilation.Diagnostics));
        var compatibleProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            compatibleProviderCompilation.Program!, providerIdentity,
            schemaIdentity, 1);
        var compatibleCombinedArtifact = CombinePopulatedNativeAddressModules(
            compatibleProviderArtifact, consumerArtifact, schema);
        var compatibleLoaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                compatibleCombinedArtifact),
            consumerIdentity, []);
        Assert.That(compatibleLoaded.Functions.Single(
            static function => function.Name == "Observe")
            .ModuleImport!.ProducerIdentity, Is.EqualTo(providerIdentity));
        var compatibleMain = compatibleLoaded.Functions.Single(
            static function => function.Name == "Main");
        var compatibleComparisonConstants = new List<BoundConstant>();
        Assert.That(BoundTreeTraversal.VisitExpressions(compatibleMain.Body,
            expression =>
            {
                if (expression is BoundBinary {
                        Type: PrimitiveType.Bool,
                    } comparison
                    && comparison.Right is BoundConstant constant)
                {
                    compatibleComparisonConstants.Add(constant);
                }
            }), Is.True);
        Assert.That(compatibleComparisonConstants, Has.Count.EqualTo(1));
        Assert.That(compatibleComparisonConstants[0].Bits.Low, Is.EqualTo(1));

        var incompatibleProviderSource = providerSource
            .Replace("NativeParameter(1, Write, NoEscape, 1)",
                "NativeParameter(1, Read, NoEscape, 1)",
                StringComparison.Ordinal)
            .Replace("Observe(write ptr<int32> output)",
                "Observe(ptr<int32> output)", StringComparison.Ordinal)
            .Replace("ProviderBody(write ptr<int32> output)",
                "ProviderBody(ptr<int32> output)", StringComparison.Ordinal);
        var incompatibleProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(incompatibleProviderSource), core);
        Assert.That(incompatibleProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleProviderCompilation.Diagnostics));
        var incompatibleProviderArtifact
            = TrRmExecutableCompiler.EmitStandard(
                incompatibleProviderCompilation.Program!, providerIdentity,
                schemaIdentity, 1);
        var incompatibleCombinedArtifact
            = CombinePopulatedNativeAddressModules(
                incompatibleProviderArtifact, consumerArtifact, schema);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    incompatibleCombinedArtifact),
                consumerIdentity, []));
    }

    [Test]
    public async Task StandardA2NestedIndexBodySurvivesProviderRemappingAndReplacement()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        const string providerIdentity = "a2-nested-index-provider";
        const string consumerIdentity = "a2-nested-index-consumer";
        const string providerSource = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            public extern void Observe(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            internal int32 ProviderBody() {
                int32 zeroIndex = 0;
                mut [int32; 2] values = [0, -1];
                mut int32 observed = values[zeroIndex];
                uint64 byteCount = 0;
                unsafe {
                    var callback = &Observe;
                    callback(&observed, &values[zeroIndex], byteCount);
                }
                return observed;
            }
            """;
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource), core);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, schemaIdentity, 1);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(
                        providerArtifact));
        Assert.That(providerTables.Bodies.Count, Is.EqualTo(1));
        Assert.That(providerTables.FixedArrayLayouts.Count, Is.EqualTo(1));
        var providerLayoutCount = providerTables.FixedArrayLayouts.Count;
        var providerReference
            = TrRmExecutableCompiler.CreateStandardReference(providerInput,
                providerIdentity, []);

        const string consumerSource = """
            int32 Main() {
                int32 zeroIndex = 0;
                mut int32 sequence = 0;
                mut [[[int32; 2]; 2]; 2] values = [
                    [[0, -1], [-1, 0]],
                    [[-1, 0], [0, -1]]];
                uint64 byteCount = 0;
                unsafe {
                    var callback = &Observe;
                    callback(&sequence,
                        &values[sequence++][zeroIndex][zeroIndex],
                        byteCount);
                }
                return sequence;
            }
            """;
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse(consumerSource), providerReference,
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration
                    = NativeCoreRegistrationTests.CreateRegistration(),
            });
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerCompilation.Program!, consumerIdentity, schemaIdentity, 1);
        Assert.That(GetA2IndexUpdateControls(
            new TrRmStandardArtifactInput(schemaIdentity,
                consumerArtifact)),
            Is.EqualTo(new byte[] { 0x01 }));
        var consumerTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(
                        consumerArtifact));
        Assert.That(consumerTables.Bodies.Count, Is.EqualTo(1));
        Assert.That(consumerTables.FixedArrayLayouts.Count, Is.EqualTo(3));

        var combinedArtifact = CombinePopulatedNativeAddressModules(
            providerArtifact, consumerArtifact, schema);
        var combinedTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(
                        combinedArtifact));
        var consumerModuleRow = 0;
        for (var moduleRow = 1;
            moduleRow <= combinedTables.Modules.Count; moduleRow++)
        {
            if (combinedTables.Modules.GetIdentity(moduleRow)
                == consumerIdentity)
            {
                consumerModuleRow = moduleRow;
                break;
            }
        }
        Assert.That(consumerModuleRow, Is.EqualTo(2));
        var mainRow = 0;
        for (var methodRow = 1;
            methodRow <= combinedTables.Methods.Count; methodRow++)
        {
            if (combinedTables.Methods.GetOwnerModule(methodRow)
                    == consumerModuleRow
                && combinedTables.Methods.GetName(methodRow) == "Main")
            {
                mainRow = methodRow;
                break;
            }
        }
        Assert.That(mainRow, Is.GreaterThan(0));
        var mainBodyRow = combinedTables.Methods.GetBodyRow(mainRow);
        Assert.That(mainBodyRow,
            Is.EqualTo(providerTables.Bodies.Count + 1));
        combinedTables.ValidateRegisterBodyMapsForMethod(mainRow);
        var mainBody = combinedTables.Bodies.GetRegisterBody(mainBodyRow,
            combinedTables.Contracts.Count);
        var selectedLayoutRows = new List<uint>();
        var indexUpdateControls = new List<byte>();
        for (var recordIndex = 0;
            recordIndex < mainBody.RecordCount; recordIndex++)
        {
            var record = mainBody.GetRecord(recordIndex);
            if (record[0] == 0x04 && record[4] == 3 && record[13] == 3)
            {
                selectedLayoutRows.Add(
                    BinaryPrimitives.ReadUInt32LittleEndian(record[16..]));
            }
        }
        for (var blockIndex = 0; blockIndex < mainBody.BlockCount;
            blockIndex++)
        {
            var code = mainBody.GetBlockCode(blockIndex);
            for (var position = 0; position < code.Length;)
            {
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        code[position], mainBody.Schema);
                if (code[position] == 0xC0
                    && code[position + 1] is 0x01 or 0x02 or 0x05 or 0x06)
                {
                    indexUpdateControls.Add(code[position + 1]);
                }

                position += instructionLength;
            }
        }
        Assert.That(selectedLayoutRows, Is.Not.Empty);
        Assert.That(selectedLayoutRows.All(layoutRow =>
            layoutRow > (uint)providerLayoutCount), Is.True);
        Assert.That(indexUpdateControls, Is.EqualTo(new byte[] { 0x01 }));

        void AssertCapturedNestedIndex(BoundProgram program)
        {
            var caller = program.Functions[program.EntryPoint];
            var addresses = new List<BoundNativeStorageAddress>();
            var referenceFormations = new List<BoundReferenceFormation>();
            Assert.That(BoundTreeTraversal.VisitExpressions(
                caller.Body, expression =>
                {
                    if (expression is BoundNativeStorageAddress address
                        && address.Projections.Any(static projection =>
                            projection.Index is BoundUpdate))
                    {
                        addresses.Add(address);
                    }
                    else if (expression is BoundReferenceFormation
                        referenceFormation)
                    {
                        referenceFormations.Add(referenceFormation);
                    }
                }), Is.True);
            Assert.That(addresses, Has.Count.EqualTo(1));
            Assert.That(referenceFormations, Is.Empty);
            var selectedAddress = addresses[0];
            Assert.That(selectedAddress.Projections, Has.Length.EqualTo(3));
            Assert.That(caller.Variables[selectedAddress.Variable].Type,
                Is.EqualTo(selectedAddress.CarrierType));
            Assert.That(selectedAddress.IsReadonly, Is.True);
            var updates = selectedAddress.Projections
                .Select(static projection => projection.Index)
                .OfType<BoundUpdate>().ToArray();
            Assert.That(updates, Has.Length.EqualTo(1));
            Assert.That(updates[0].Operator, Is.EqualTo(TokenKind.PlusPlus));
            Assert.That(updates[0].IsPrefix, Is.False);
            Assert.That(updates[0].Type, Is.EqualTo(PrimitiveType.Int32));
            Assert.That(updates[0].OverflowBehavior,
                Is.EqualTo(BoundIntegerOverflowBehavior.Checked));
            Assert.That(updates[0].Place, Is.TypeOf<BoundRead>());
            var indexRead = (BoundRead)updates[0].Place;
            Assert.That(indexRead.Variable,
                Is.Not.EqualTo(selectedAddress.Variable));
            Assert.That(caller.Variables[indexRead.Variable].Type,
                Is.EqualTo(PrimitiveType.Int32));
            Assert.That(caller.Variables[indexRead.Variable].IsMutable,
                Is.True);
            var parameters = program.Functions.Single(
                static function => function.Name == "Observe")
                .DirectNativeImport!.Parameters;
            Assert.That(parameters, Has.Length.EqualTo(2));
            Assert.That(parameters.Select(static parameter =>
                    parameter.Access),
                Is.EqualTo([
                    NativeParameterAccess.Write,
                    NativeParameterAccess.Read,
                ]));
            Assert.That(parameters.All(static parameter =>
                parameter.Retention == NativeParameterRetention.NoEscape
                && parameter.Extent == 1), Is.True);
        }

        var combinedInput = new TrRmStandardArtifactInput(schemaIdentity,
            combinedArtifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(combinedInput,
            consumerIdentity, []);
        var importedObserve = loaded.Functions.Single(static function =>
            function.Name == "Observe");
        Assert.That(importedObserve.ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(importedObserve.DirectNativeImport!.LibraryName,
            Is.EqualTo("kernel32.dll"));
        AssertCapturedNestedIndex(loaded);
        var reencodedConsumer = TrRmExecutableCompiler.EmitStandard(loaded,
            consumerIdentity, schemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reencodedConsumer),
            consumerIdentity, [providerInput]);
        AssertCapturedNestedIndex(reloaded);
        var standardJitResult = NativeModuleCompiler.ExecuteStandard(
            combinedInput, consumerIdentity, [],
            NativeRuntimePolicy.FailFast);
        TestContext.WriteLine(
            $"A2 nested scalar call initializer standard JIT: policy=FailFast; expected=1; actual={standardJitResult}.");
        Assert.That(standardJitResult, Is.EqualTo(1));
        await AssertStandardA2SourceFreeJitAndAotAsync(consumerIdentity,
            combinedInput, consumerSource, 1,
            "co-located provider Body remap", runJit: false,
            expectedReemitted: consumerArtifact).ConfigureAwait(true);

        var boundsFailureSource = consumerSource.Replace(
            "mut int32 sequence = 0;", "mut int32 sequence = -1;",
            StringComparison.Ordinal);
        var updateOverflowSource = consumerSource
            .Replace("mut int32 sequence = 0;",
                "mut uint8 sequence = 0;\n"
                    + "                mut int32 observed = 0;",
                StringComparison.Ordinal)
            .Replace("callback(&sequence,", "callback(&observed,",
                StringComparison.Ordinal)
            .Replace("sequence++", "sequence--",
                StringComparison.Ordinal)
            .Replace("return sequence;", "return observed;",
                StringComparison.Ordinal);
        var indexFailures = new[] {
            (Source: boundsFailureSource,
                Controls: new byte[] { 0x01 },
                Status: unchecked((int)0xE0540004),
                Scenario: "bounds check before provider entry"),
            (Source: updateOverflowSource,
                Controls: [0x02],
                Status: unchecked((int)0xE0540001),
                Scenario: "checked update before provider entry"),
        };
        foreach (var (Source, Controls, Status, Scenario) in indexFailures)
        {
            var failureCompilation = Compilation.Create(
                SyntaxTree.Parse(Source), providerReference,
                new CompilationOptions {
                    RuntimePolicy = BoundRuntimePolicy.FailFast,
                    NativeCoreRegistration
                        = NativeCoreRegistrationTests.CreateRegistration(),
                });
            Assert.That(failureCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    failureCompilation.Diagnostics));
            var failureArtifact = TrRmExecutableCompiler.EmitStandard(
                failureCompilation.Program!, consumerIdentity,
                schemaIdentity, 1);
            var failureCombinedArtifact
                = CombinePopulatedNativeAddressModules(providerArtifact,
                    failureArtifact, schema);
            var failureInput = new TrRmStandardArtifactInput(schemaIdentity,
                failureCombinedArtifact);
            Assert.That(GetA2IndexUpdateControls(failureInput),
                Is.EqualTo(Controls));
            await AssertStandardA2SourceFreeJitAndAotAsync(
                consumerIdentity, failureInput, Source,
                Status, Scenario, runJit: false,
                expectedReemitted: failureArtifact)
                .ConfigureAwait(true);
        }

        var compatibleProviderSource = providerSource.Replace(
            "uint64 byteCount = 0;",
            "mut [uint32; 2] padding = [0, 0];\n"
                + "                uint64 byteCount = 0;",
            StringComparison.Ordinal);
        var compatibleProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(compatibleProviderSource), core);
        Assert.That(compatibleProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                compatibleProviderCompilation.Diagnostics));
        var compatibleProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            compatibleProviderCompilation.Program!, providerIdentity,
            schemaIdentity, 1);
        Assert.That(compatibleProviderArtifact,
            Is.Not.EqualTo(providerArtifact));
        var compatibleProviderTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(
                        compatibleProviderArtifact));
        Assert.That(compatibleProviderTables.FixedArrayLayouts.Count,
            Is.EqualTo(2));
        var compatibleProviderBodyCount
            = compatibleProviderTables.Bodies.Count;
        var compatibleCombinedArtifact
            = CombinePopulatedNativeAddressModules(
                compatibleProviderArtifact, consumerArtifact, schema);
        var compatibleTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(
                        compatibleCombinedArtifact));
        var compatibleMainRow = 0;
        for (var methodRow = 1;
            methodRow <= compatibleTables.Methods.Count; methodRow++)
        {
            if (compatibleTables.Methods.GetOwnerModule(methodRow)
                    == consumerModuleRow
                && compatibleTables.Methods.GetName(methodRow) == "Main")
            {
                compatibleMainRow = methodRow;
                break;
            }
        }
        Assert.That(compatibleMainRow, Is.GreaterThan(0));
        Assert.That(compatibleTables.Methods.GetBodyRow(compatibleMainRow),
            Is.EqualTo(compatibleProviderBodyCount + 1));
        compatibleTables.ValidateRegisterBodyMapsForMethod(compatibleMainRow);
        var compatibleLoaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                compatibleCombinedArtifact),
            consumerIdentity, []);
        AssertCapturedNestedIndex(compatibleLoaded);

        var incompatibleProviderSource = providerSource
            .Replace("NativeParameter(2, Read, NoEscape, 1)",
                "NativeParameter(2, Write, NoEscape, 1)",
                StringComparison.Ordinal)
            .Replace("ptr<int32> source", "write ptr<int32> source",
                StringComparison.Ordinal);
        var incompatibleProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(incompatibleProviderSource), core);
        Assert.That(incompatibleProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleProviderCompilation.Diagnostics));
        var incompatibleProviderArtifact
            = TrRmExecutableCompiler.EmitStandard(
                incompatibleProviderCompilation.Program!, providerIdentity,
                schemaIdentity, 1);
        var incompatibleCombinedArtifact
            = CombinePopulatedNativeAddressModules(
                incompatibleProviderArtifact, consumerArtifact, schema);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    incompatibleCombinedArtifact),
                consumerIdentity, []));
    }

    [TestCase(1, "uint32", "[0]", PrimitiveType.UInt32, false, 1)]
    [TestCase(3, "uint32", "[0, 0, 0]", PrimitiveType.UInt32, false, 1)]
    [TestCase(2, "int32", "[0, -1]", PrimitiveType.Int32, true, 1)]
    [TestCase(3, "uint32", "[0, 0, 0]", PrimitiveType.UInt32, false, 2)]
    public void StandardA2EmitsAndReconstructsInitializedArrayStorageAddress(
        int elementCount, string elementType, string initializer,
        PrimitiveType expectedElementType, bool hasAllBitsSetElement,
        int nativeParameterExtent)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-array-storage-address";
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(
                1, Read, NoEscape, {{nativeParameterExtent}})]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
            internal extern void Observe(
                ptr<{{elementType}}> value,
                write ptr<int32> output);

            int32 Main() {
                int32 zeroIndex = 0;
                mut [{{elementType}}; {{elementCount}}] values = {{initializer}};
                mut int32 output = 0;
                unsafe {
                    var callback = &Observe;
                    callback(&values[zeroIndex], &output);
                }
                return output;
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(compilation.Program!,
                moduleIdentity,
                "trrm/1-draft-foreign-invocation-storage-addresses-1", 1));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        Assert.That(tables.FixedArrayLayouts.Count, Is.EqualTo(1));
        Assert.That(tables.Features.TryGetFeature(
            TrRmFeatureTableReader.FixedInlineArrayStorageFeatureId,
            out _), Is.True);

        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                callerRow = row;
                break;
            }
        }
        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        AssertHomeFirstUseTypeMap(body);
        var placeSelectionCount = 0;
        var semanticStoreCount = 0;
        var storageAddressCount = 0;
        var arrayRequiredElementCount = 0u;
        var initializerRegisters = new List<ushort>();
        for (var record = 0; record < body.RecordCount; record++)
        {
            var bodyRecord = body.GetRecord(record);
            switch (bodyRecord[0])
            {
                case 0x04:
                    placeSelectionCount++;
                    break;
                case 0x05:
                    semanticStoreCount++;
                    initializerRegisters.Add(
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            bodyRecord[10..]));
                    break;
                case 0x0E:
                    storageAddressCount++;
                    if (bodyRecord[4] == 1)
                    {
                        arrayRequiredElementCount
                            = BinaryPrimitives.ReadUInt32LittleEndian(
                                bodyRecord[12..]);
                    }
                    break;
            }
        }
        Assert.That(placeSelectionCount, Is.EqualTo(elementCount + 1));
        Assert.That(semanticStoreCount, Is.EqualTo(elementCount));
        Assert.That(storageAddressCount, Is.EqualTo(2));
        Assert.That(arrayRequiredElementCount,
            Is.EqualTo((uint)nativeParameterExtent));
        var expectedInitializerRegisters = hasAllBitsSetElement
            ? [14, 15]
            : Enumerable.Repeat((ushort)14, elementCount).ToArray();
        Assert.That(initializerRegisters,
            Is.EqualTo(expectedInitializerRegisters));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var arrayInitializations = new List<BoundNativeFixedArrayInitialization>();
        var storageAddresses = new List<BoundNativeStorageAddress>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression
                    is BoundNativeFixedArrayInitialization initialization)
                {
                    arrayInitializations.Add(initialization);
                }
                else if (expression is BoundNativeStorageAddress address)
                {
                    storageAddresses.Add(address);
                }
            }), Is.True);
        Assert.That(arrayInitializations, Has.Count.EqualTo(1));
        Assert.That(arrayInitializations[0].Elements.Length,
                Is.EqualTo(elementCount));
        Assert.That(storageAddresses, Has.Count.EqualTo(2));
        var arrayAddress = storageAddresses.Single(address =>
            NativeFixedArrayTypes.IsType(address.CarrierType));
        Assert.That(arrayAddress.ElementType, Is.EqualTo(expectedElementType));
        Assert.That(arrayAddress.ElementCount, Is.EqualTo((ulong)elementCount));
        Assert.That(arrayAddress.ElementStride, Is.EqualTo(4));
        Assert.That(arrayAddress.ByteExtent,
            Is.EqualTo((ulong)nativeParameterExtent * 4));
        Assert.That(arrayAddress.Index, Is.TypeOf<BoundRead>());
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
    }

    [TestCase("Read", 0u, false)]
    [TestCase("Read", 1u, false)]
    [TestCase("Read", 2u, false)]
    [TestCase("Write", 2u, false)]
    [TestCase("ReadWrite", 2u, false)]
    [TestCase("Read", 2u, true)]
    public void StandardA2NativeOutputCallRetainsArrayParameterBacking(
        string access, uint nativeParameterExtent, bool allBitsSet)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-native-output-array-backing";
        var pointerType = access == "Read"
            ? "ptr<int32>" : "write ptr<int32>";
        var arrayValues = allBitsSet
            ? "[-1, -1, -1]" : "[0, 0, 0]";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Result {
                public uint32 Value;
            }

            [Terrum.Interop.NativeImport("example.dll", "Observe")]
            [Terrum.Interop.NativeParameter(
                1, {{access}}, NoEscape, {{nativeParameterExtent}})]
            internal extern int32 Observe(
                {{pointerType}} values,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<Result> result);

            internal int32 Probe(int32 index) {
                    int32 zeroIndex = 0;
                    mut [int32; 3] backing = {{arrayValues}};
                init mut Result result;
                unsafe { return Observe(&backing[index], &init result); }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var caller = loaded.Functions.Single(static function =>
            function.Name == "Probe");
        BoundNativeStorageAddress? address = null;
        BoundNativeFixedArrayInitialization? arrayInitialization = null;
        BoundCall? call = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundNativeStorageAddress storageAddress)
                {
                    address = storageAddress;
                }
                else if (expression
                    is BoundNativeFixedArrayInitialization initialization)
                {
                    arrayInitialization = initialization;
                }
                else if (expression is BoundCall candidate
                    && candidate.Arguments.Any(static argument =>
                        argument is BoundNativeOutputReservation))
                {
                    call = candidate;
                }
            }), Is.True);
        Assert.That(address, Is.Not.Null);
        Assert.That(address!.ElementCount, Is.EqualTo(3));
        Assert.That(address.ElementType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(address.ByteExtent,
            Is.EqualTo((ulong)Math.Max(1u, nativeParameterExtent) * 4));
        Assert.That(address.IsReadonly, Is.EqualTo(access == "Read"));
        Assert.That(address.Projections, Has.Length.EqualTo(1));
        Assert.That(address.Index, Is.TypeOf<BoundRead>());
        Assert.That(arrayInitialization, Is.Not.Null);
        Assert.That(arrayInitialization!.Elements, Has.Length.EqualTo(3));
        foreach (var element in arrayInitialization.Elements)
        {
            Assert.That(element, Is.TypeOf<BoundConstant>());
            var constant = (BoundConstant)element;
            Assert.That(constant.Type, Is.EqualTo(PrimitiveType.Int32));
            Assert.That(constant.Bits.Low, Is.EqualTo(allBitsSet
                ? uint.MaxValue : 0));
            Assert.That(constant.Bits.High, Is.Zero);
        }
        Assert.That(call, Is.Not.Null);
        Assert.That(call!.Arguments[1],
            Is.TypeOf<BoundNativeOutputReservation>());
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
                moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(artifact));
    }

    [Test]
    public async Task StandardA2EUEmitsAndReconstructsArrayFieldStorageAddress()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1";
        const string moduleIdentity = "a2eu-array-field-storage-address";
        const string source = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public mut [int32; 2] Values;
                public mut [int32; 1] Sibling;
            }

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            int32 Main() {
                int32 zeroIndex = 0;
                int32 oneIndex = 1;
                [int32; 2] initial = [-1, 0];
                [int32; 1] sibling = [-1];
                mut int32 replacement = 42;
                mut int32 observed = 0;
                uint64 byteCount = 4;
                init mut Holder holder;
                holder.Values = initial;
                holder.Sibling = sibling;
                unsafe {
                    RtlMoveMemory(&holder.Values[oneIndex],
                        &replacement, byteCount);
                    var callback = &RtlMoveMemory;
                    callback(&observed,
                        &holder.Sibling[zeroIndex], byteCount);
                }
                return observed;
            }
            """;
        var options = WithLayoutRegistration(new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        });
        var compilation = Compilation.Create(
            SyntaxTree.Parse(source), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                        artifact));
        Assert.That(tables.TypeDefs.Count, Is.EqualTo(1));
        Assert.That(tables.Fields.Count, Is.EqualTo(2));
        Assert.That(tables.FixedArrayLayouts.Count, Is.EqualTo(2));

        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                callerRow = row;
                break;
            }
        }
        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        Assert.That(body.ConstantCount, Is.EqualTo(3));
        var sawFieldSelection = false;
        var sawElementSelection = false;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] != 0x04)
            {
                continue;
            }

            var payload = record[4..];
            if (payload[0] == 2 && payload[9] == 1
                && BinaryPrimitives.ReadUInt32LittleEndian(payload[12..]) == 1)
            {
                sawFieldSelection = true;
            }
            else if (payload[0] == 3 && payload[9] == 3)
            {
                sawElementSelection = true;
            }
        }
        Assert.That(sawFieldSelection, Is.True);
        Assert.That(sawElementSelection, Is.True);

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var fieldAssignments = new List<BoundFieldAssignment>();
        var storageAddresses = new List<BoundNativeStorageAddress>();
        Assert.That(BoundTreeTraversal.VisitStatements(loadedMain.Body,
            statement =>
            {
                if (statement is BoundFieldAssignment assignment)
                {
                    fieldAssignments.Add(assignment);
                }
            }), Is.True);
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundNativeStorageAddress address)
                {
                    storageAddresses.Add(address);
                }
            }), Is.True);
        Assert.That(fieldAssignments, Has.Count.EqualTo(2));
        Assert.That(fieldAssignments.All(static assignment =>
            NativeFixedArrayTypes.IsType(assignment.Value.Type)
                && assignment.NestedFields.IsEmpty
                && assignment.Value is BoundRead), Is.True);
        var fieldAddresses = storageAddresses.Where(static address =>
            address.FieldPlace is not null).ToArray();
        Assert.That(fieldAddresses, Has.Length.EqualTo(2));
        Assert.That(fieldAddresses.All(static address =>
            address.FieldPlace is BoundFieldRead
                && address.ElementType == PrimitiveType.Int32
                && address.Projections.IsEmpty), Is.True);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
        await AssertStandardA2SourceFreeJitAndAotAsync(moduleIdentity,
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            source, -1, "positive-extent field mutation and sibling preservation")
            .ConfigureAwait(true);
    }

    [Test]
    public async Task StandardA2EUFieldArrayNativeAddressFailsFastOutOfBounds()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1";
        const string moduleIdentity = "a2eu-array-field-storage-bounds";
        const string source = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public mut [int32; 2] Values;
            }

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            int32 Main() {
                int32 zeroIndex = 0;
                mut int32 selectedIndex = 2;
                [int32; 2] initial = [-1, 0];
                mut int32 observed = 0;
                uint64 byteCount = 4;
                init mut Holder holder;
                holder.Values = initial;
                unsafe {
                    var callback = &RtlMoveMemory;
                    callback(&observed,
                        &holder.Values[selectedIndex], byteCount);
                }
                return observed;
            }
            """;
        var options = WithLayoutRegistration(new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        });
        var compilation = Compilation.Create(
            SyntaxTree.Parse(source), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        const int expectedStatus = unchecked((int)0xE0540004);
        await AssertStandardA2SourceFreeJitAndAotAsync(moduleIdentity,
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            source, expectedStatus, "field-array address fast-fail bounds check",
            runJit: false).ConfigureAwait(true);
    }

    [Test]
    public async Task StandardA2EUFieldArrayNativeAccessUsesPositiveDynamicExtent()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1";
        const string moduleIdentity = "a2eu-array-field-positive-native-extent";
        const string source = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public mut [int32; 2] Values;
                public mut [int32; 1] Sibling;
            }

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            int32 Main() {
                int32 zeroIndex = 0;
                mut uint64 index = 0;
                [int32; 2] initial = [-1, 0];
                [int32; 1] sibling = [-1];
                int32 replacement = 0;
                mut int32 observed = 0;
                init mut Holder holder;
                holder.Values = initial;
                holder.Sibling = sibling;
                unsafe {
                    RtlMoveMemory(&holder.Values[++index],
                        &replacement, index);
                    var callback = &RtlMoveMemory;
                    callback(&observed,
                        &holder.Sibling[zeroIndex], index);
                }
                return observed;
            }
            """;
        var options = WithLayoutRegistration(new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        });
        var compilation = Compilation.Create(
            SyntaxTree.Parse(source), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        await AssertStandardA2SourceFreeJitAndAotAsync(moduleIdentity,
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            source, 255, "positive dynamic field extent and sibling preservation")
            .ConfigureAwait(true);
    }

    [Test]
    public async Task StandardA2EUFieldArrayNativeWriteUsesTwoElementSuffix()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1";
        const string moduleIdentity = "a2eu-field-array-native-write-q2";
        const string source = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public mut [int32; 2] Values;
            }

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 2)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            int32 Main() {
                int32 zeroIndex = 0;
                mut uint64 index = 0;
                [int32; 2] initial = [-1, 0];
                int32 replacement = 0;
                init mut Holder holder;
                holder.Values = initial;
                unsafe {
                    var callback = &RtlMoveMemory;
                    callback(&holder.Values[index++],
                        &replacement, index);
                }
                return replacement;
            }
            """;
        var options = WithLayoutRegistration(new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        });
        var compilation = Compilation.Create(
            SyntaxTree.Parse(source), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var input = new TrRmStandardArtifactInput(schemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        var main = loaded.Functions.Single(static function =>
            function.Name == "Main");
        BoundNativeStorageAddress? selectedWriteAddress = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundNativeStorageAddress {
                    FieldPlace: not null,
                    IsReadonly: false,
                    ElementCount: 2,
                    ByteExtent: 8,
                    Index: BoundUpdate,
                } address)
                {
                    selectedWriteAddress = address;
                }
            }), Is.True);
        Assert.That(selectedWriteAddress, Is.Not.Null);
        Assert.That(selectedWriteAddress!.Index.Type,
            Is.EqualTo(PrimitiveType.UInt64));
        await AssertStandardA2SourceFreeJitAndAotAsync(moduleIdentity,
            input, source, 0, "two-element field-array NativeParameter write")
            .ConfigureAwait(true);
    }

    [Test]
    public async Task StandardA2FieldArrayReadUsesPositiveDynamicExtent()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-array-field-positive-native-read";
        const string source = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public mut [int32; 2] Values;
            }

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            int32 Main() {
                int32 zeroIndex = 0;
                mut uint64 index = 0;
                [int32; 2] initial = [-1, -1];
                mut int32 observed = 0;
                init mut Holder holder;
                holder.Values = initial;
                unsafe {
                    var callback = &RtlMoveMemory;
                    callback(&observed,
                        &holder.Values[++index], index);
                }
                return observed;
            }
            """;
        var options = WithLayoutRegistration(new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        });
        var compilation = Compilation.Create(
            SyntaxTree.Parse(source), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        await AssertStandardA2SourceFreeJitAndAotAsync(moduleIdentity,
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            source, 255, "positive dynamic field suffix read")
            .ConfigureAwait(true);
    }

    [Test]
    public void StandardA2EmitsAndReconstructsNestedArrayInitialization()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-nested-array-initialization";
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            internal extern void Observe(write ptr<int32> output);

            int32 Main() {
                int32 zeroIndex = 0;
                mut [[[int32; 2]; 2]; 2] values = [
                    [[0, -1], [-1, 0]],
                    [[-1, 0], [0, -1]]];
                mut int32 output = 0;
                unsafe {
                    var callback = &Observe;
                    callback(&output);
                }
                return output;
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        Assert.That(tables.FixedArrayLayouts.Count, Is.EqualTo(3));

        var mainRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                mainRow = row;
                break;
            }
        }
        Assert.That(mainRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(mainRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(mainRow), tables.Contracts.Count);
        AssertHomeFirstUseTypeMap(body);

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var main = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var initializations
            = new List<BoundNativeFixedArrayInitialization>();
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundNativeFixedArrayInitialization init)
                {
                    initializations.Add(init);
                }
            }), Is.True);
        Assert.That(initializations, Has.Count.EqualTo(7));
        Assert.That(initializations.All(static init =>
            init.Layout.ElementCount == 2
                && (ulong)init.Elements.Length == init.Layout.ElementCount),
            Is.True);
        Assert.That(initializations.Count(static init =>
            NativeFixedArrayTypes.IsType(init.Layout.ElementType)),
            Is.EqualTo(3));
        Assert.That(initializations.Count(static init =>
            init.Layout.ElementType == PrimitiveType.Int32),
            Is.EqualTo(4));
        int GetArrayTypeDepth(PrimitiveType type)
        {
            var depth = 0;
            while (NativeFixedArrayTypes.IsType(type))
            {
                depth++;
                type = loaded.NativeFixedArrayTypes[
                    NativeFixedArrayTypes.Index(type)].ElementType;
            }

            return depth;
        }
        var outerInitialization = initializations.Single(init =>
            GetArrayTypeDepth(init.Type) == 3);
        var sourceOrderedValues = new List<uint>();
        void CollectNestedValues(
            BoundNativeFixedArrayInitialization initialization)
        {
            foreach (var element in initialization.Elements)
            {
                if (element
                    is BoundNativeFixedArrayInitialization nested)
                {
                    CollectNestedValues(nested);
                }
                else
                {
                    Assert.That(element, Is.InstanceOf<BoundConstant>());
                    sourceOrderedValues.Add(unchecked((uint)
                        ((BoundConstant)element).Bits.Low));
                }
            }
        }
        CollectNestedValues(outerInitialization);
        Assert.That(sourceOrderedValues, Is.EqualTo(new uint[] {
            0, uint.MaxValue, uint.MaxValue, 0,
            uint.MaxValue, 0, 0, uint.MaxValue,
        }));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
    }

    [Test]
    public void StandardA2R1RepeatRoundTripsUInt64MaxZeroStrideArrayBody()
    {
        const string moduleIdentity = "a2r1-max-zero-stride-array";
        var options = WithLayoutRegistration(new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        });
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            internal extern void Observe(write ptr<int32> output);

            int32 Main() {
                mut [void; 18446744073709551615] units
                    = [(); 18446744073709551615];
                mut int32 output = 42;
                unsafe {
                    var callback = &Observe;
                    callback(&output);
                }
                return output;
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity,
            A2R1RepeatInitializationSchemaIdentity, 1);
        var index = TrRmArtifactIndex.ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
            artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        Assert.That(tables.FixedArrayLayouts.Count, Is.EqualTo(1));
        Assert.That(tables.FixedArrayLayouts.GetRow(1).ElementStride,
            Is.Zero);
        Assert.That(tables.FixedArrayLayouts.GetRow(1).ByteExtent,
            Is.Zero);
        var mainMethodRow = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            if (tables.Methods.GetName(methodRow) == "Main")
            {
                mainMethodRow = methodRow;
                break;
            }
        }
        Assert.That(mainMethodRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(mainMethodRow);

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(
                A2R1RepeatInitializationSchemaIdentity, artifact),
            moduleIdentity, []);
        var array = loaded.NativeFixedArrayTypes.Single();
        Assert.That((array.ElementType, array.ElementCount,
            array.ElementStride, array.ByteExtent),
            Is.EqualTo((PrimitiveType.Void, ulong.MaxValue, 0UL, 0UL)));
        var main = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var repeatInitializations
            = new List<BoundNativeFixedArrayRepeatInitialization>();
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression
                    is BoundNativeFixedArrayRepeatInitialization repeat)
                {
                    repeatInitializations.Add(repeat);
                }
            }), Is.True);
        Assert.That(repeatInitializations, Has.Count.EqualTo(1));
        Assert.That(repeatInitializations[0].Layout.ElementCount,
            Is.EqualTo(ulong.MaxValue));

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, A2R1RepeatInitializationSchemaIdentity, 1);
        Assert.That(reemitted, Is.EqualTo(artifact));
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(
                A2R1RepeatInitializationSchemaIdentity, reemitted),
            moduleIdentity, []);
        Assert.That(reloaded.NativeFixedArrayTypes.Single().ElementCount,
            Is.EqualTo(ulong.MaxValue));
    }

    [Test]
    public async Task StandardA2NestedScalarCallInitializersPreserveOrderAndFailure()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        const string providerIdentity = "a2-nested-call-value-provider";
        const string consumerIdentity = "a2-nested-call-value-consumer";
        const string providerSource = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            public extern void Observe(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            internal int32 ProviderBody() {
                int32 zeroIndex = 0;
                mut [int32; 2] values = [0, -1];
                mut int32 observed = values[zeroIndex];
                uint64 byteCount = 0;
                unsafe {
                    var callback = &Observe;
                    callback(&observed, &values[zeroIndex], byteCount);
                }
                return observed;
            }
            """;
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource), core);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, schemaIdentity, 1);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, []);

        const string consumerTemplate = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetDriveTypeW",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern int32 GetDriveTypeW(ptr<char16> rootPath);

            int32 Main() {
                unsafe {
                    int32 zeroIndex = 0;
                    mut int32 badIndex = -1;
                    mut [char16; 1] guard = [char16(0)];
                    mut [char16; 1] pathA = [char16(0)];
                    mut [char16; 1] pathB = [char16(0)];
                    int32 selectedIndex
                        = GetDriveTypeW(&pathA[zeroIndex]);
                    mut [[int32; 2]; 2] values = [
                        [GetDriveTypeW(&pathA[zeroIndex]),
                            SECOND_INITIALIZER],
                        [GetDriveTypeW(&pathB[zeroIndex]),
                            GetDriveTypeW(&pathA[zeroIndex])]];
                    mut int32 observed
                        = values[selectedIndex][zeroIndex];
                    uint64 byteCount = 0;
                    var callback = &Observe;
                    callback(&observed, &values[zeroIndex][zeroIndex],
                        byteCount);
                    return observed;
                }
            }
            """;
        var consumerSource = consumerTemplate.Replace("SECOND_INITIALIZER",
            "GetDriveTypeW(&pathB[zeroIndex])", StringComparison.Ordinal);
        var failureSource = consumerTemplate.Replace("SECOND_INITIALIZER",
            "GetDriveTypeW(&guard[badIndex++])",
            StringComparison.Ordinal);

        BoundProgram? consumerProgram = null;
        byte[] EmitConsumer(string source)
        {
            var compilation = Compilation.Create(SyntaxTree.Parse(source),
                providerReference, new CompilationOptions {
                    RuntimePolicy = BoundRuntimePolicy.FailFast,
                    NativeCoreRegistration
                        = NativeCoreRegistrationTests.CreateRegistration(),
                });
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            if (StringComparer.Ordinal.Equals(source, consumerSource))
            {
                consumerProgram = compilation.Program;
            }

            return TrRmExecutableCompiler.EmitStandard(
                compilation.Program!, consumerIdentity, schemaIdentity, 1);
        }

        var consumerArtifact = EmitConsumer(consumerSource);
        var originalProgram = consumerProgram
            ?? throw new InvalidOperationException(
                "The source-bound consumer program was not retained.");
        var originalMain = originalProgram.Functions.Single(
            static function => function.Name == "Main");
        var consumerTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(
                        consumerArtifact));
        var mainRow = 0;
        for (var methodRow = 1;
            methodRow <= consumerTables.Methods.Count; methodRow++)
        {
            if (consumerTables.Methods.GetName(methodRow) == "Main")
            {
                mainRow = methodRow;
                break;
            }
        }
        Assert.That(mainRow, Is.GreaterThan(0));
        consumerTables.ValidateRegisterBodyMapsForMethod(mainRow);
        var mainBody = consumerTables.Bodies.GetRegisterBody(
            consumerTables.Methods.GetBodyRow(mainRow),
            consumerTables.Contracts.Count);
        var directCallTargets = new List<string>();
        for (var blockIndex = 0; blockIndex < mainBody.BlockCount;
            blockIndex++)
        {
            var code = mainBody.GetBlockCode(blockIndex);
            for (var position = 0; position < code.Length;)
            {
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        code[position], mainBody.Schema);
                if (code[position] == 0xE0)
                {
                    var callRecord = mainBody.GetRecord(
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            code[(position + 2)..]));
                    if (callRecord[4] == 1)
                    {
                        var methodMapIndex
                            = BinaryPrimitives.ReadUInt16LittleEndian(
                                callRecord[8..]);
                        var methodToken
                            = BinaryPrimitives.ReadUInt32LittleEndian(
                                mainBody.MethodMap[
                                    (methodMapIndex * sizeof(uint))..]);
                        var methodRow = checked((int)(methodToken
                            & 0x00FF_FFFF));
                        directCallTargets.Add(
                            consumerTables.Methods.GetName(methodRow));
                    }
                }

                position += instructionLength;
            }
        }
        Assert.That(directCallTargets, Is.EqualTo(
            Enumerable.Repeat("GetDriveTypeW", 5).ToArray()));

        var localHomeCount
            = originalMain.Variables.Length - originalMain.ParameterCount;
        var pathAHome
            = Array.FindIndex(originalMain.Variables.ToArray(),
                static variable => variable.Name == "pathA")
                - originalMain.ParameterCount;
        Assert.That(pathAHome, Is.GreaterThanOrEqualTo(0));
        var malformedHomeMap = mainBody.HomeMap.ToArray();
        var wrongResultHome = -1;
        for (var blockIndex = 0; blockIndex < mainBody.BlockCount
            && wrongResultHome < 0; blockIndex++)
        {
            var code = mainBody.GetBlockCode(blockIndex);
            for (var position = 0; position < code.Length;)
            {
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        code[position], mainBody.Schema);
                if (code[position] == 0xE0)
                {
                    var callSite = mainBody.GetRecord(
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            code[(position + 2)..]));
                    if (callSite[4] == 1 && callSite[5] == 2)
                    {
                        var resultHome
                            = BinaryPrimitives.ReadUInt16LittleEndian(
                                callSite[10..]);
                        if (resultHome >= localHomeCount)
                        {
                            wrongResultHome = resultHome;
                            break;
                        }
                    }
                }

                position += instructionLength;
            }
        }
        Assert.That(wrongResultHome, Is.GreaterThanOrEqualTo(0));
        var wrongResultTypeMapIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(
                malformedHomeMap.AsSpan((pathAHome * 8) + 2));
        BinaryPrimitives.WriteUInt16LittleEndian(
            malformedHomeMap.AsSpan((wrongResultHome * 8) + 2),
            wrongResultTypeMapIndex);
        var malformedBlocks = new TrRmRegisterBodyBlock[
            mainBody.BlockCount];
        for (var blockIndex = 0; blockIndex < malformedBlocks.Length;
            blockIndex++)
        {
            var block = mainBody.GetBlock(blockIndex);
            malformedBlocks[blockIndex] = new(
                BinaryPrimitives.ReadUInt32LittleEndian(block),
                BinaryPrimitives.ReadUInt32LittleEndian(block[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(block[8..]));
        }
        var malformedRecords = new ReadOnlyMemory<byte>[
            mainBody.RecordCount];
        for (var recordIndex = 0;
            recordIndex < malformedRecords.Length; recordIndex++)
        {
            malformedRecords[recordIndex]
                = mainBody.GetRecord(recordIndex).ToArray();
        }
        var malformedBody = TrRmRegisterBodyWriter.Encode(
            malformedHomeMap, mainBody.TypeMap.ToArray(),
            mainBody.MethodMap.ToArray(), mainBody.FieldMap.ToArray(),
            mainBody.ConstantMap.ToArray(), malformedBlocks,
            malformedRecords, mainBody.Code.ToArray(),
            consumerTables.Contracts.Count, schema);
        var consumerIndex = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyDraft(
                consumerArtifact);
        Assert.That(consumerIndex.TryGetSection(0x0020, out var bodySection,
            out var bodyCount), Is.True);
        var bodyTable = new TrRmBodyTableReader(bodySection, bodyCount,
            schema);
        var malformedBodyRows = new ReadOnlyMemory<byte>[bodyTable.Count];
        var mainBodyRow
            = consumerTables.Methods.GetBodyRow(mainRow);
        for (var bodyRow = 1; bodyRow <= bodyTable.Count; bodyRow++)
        {
            malformedBodyRows[bodyRow - 1]
                = bodyRow == mainBodyRow
                    ? malformedBody
                    : bodyTable.GetRow(bodyRow).ToArray();
        }
        var malformedBodySection
            = TrRmVariableTableWriter.EncodeBodies(malformedBodyRows);
        var malformedSections
            = new List<TrRmSection>(consumerIndex.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < consumerIndex.SectionCount; sectionIndex++)
        {
            consumerIndex.GetSection(sectionIndex, out var kind,
                out var flags, out var section, out var recordCount);
            malformedSections.Add(new TrRmSection(kind, flags,
                checked((uint)recordCount),
                kind == 0x0020 ? malformedBodySection : section.ToArray()));
        }
        var malformedResultHomeArtifact
            = TrRmArtifactWriter.Encode(malformedSections, schema);
        _ = Assert.Throws<InvalidDataException>(() =>
        {
            var loadedMalformedResultHome
                = TrRmExecutableCompiler.LoadStandard(
                    new TrRmStandardArtifactInput(schemaIdentity,
                        malformedResultHomeArtifact),
                    consumerIdentity, [providerInput]);
            Assert.That(loadedMalformedResultHome, Is.Not.Null);
        });

        var consumerCombinedArtifact
            = CombinePopulatedNativeAddressModules(providerArtifact,
                consumerArtifact, schema);
        var combinedInput = new TrRmStandardArtifactInput(schemaIdentity,
            consumerCombinedArtifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(combinedInput,
            consumerIdentity, []);
        var reencodedConsumer = TrRmExecutableCompiler.EmitStandard(loaded,
            consumerIdentity, schemaIdentity, 1);
        Assert.That(reencodedConsumer, Is.EqualTo(consumerArtifact));
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var initializations
            = new List<BoundNativeFixedArrayInitialization>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression
                    is BoundNativeFixedArrayInitialization initialization)
                {
                    initializations.Add(initialization);
                }
            }), Is.True);
        var initializerCalls = new List<BoundCall>();
        void CollectLoadedCalls(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundBlock block:
                    foreach (var nested in block.Statements)
                    {
                        CollectLoadedCalls(nested);
                    }
                    break;
                case BoundUnsafe unsafeBlock:
                    CollectLoadedCalls(unsafeBlock.Body);
                    break;
                case BoundLocal { Initializer: BoundCall call }:
                    if (loaded.Functions[call.Function].Name == "GetDriveTypeW")
                    {
                        initializerCalls.Add(call);
                    }
                    break;
            }
        }
        CollectLoadedCalls(loadedMain.Body);
        Assert.That(initializerCalls, Has.Count.EqualTo(5));
        var initializerAddressVariables = initializerCalls.Select(call =>
        {
            Assert.That(call.Arguments, Has.Length.EqualTo(1));
            Assert.That(call.Arguments[0],
                Is.TypeOf<BoundNativeStorageAddress>());
            return ((BoundNativeStorageAddress)call.Arguments[0]).Variable;
        }).ToArray();
        var sourceInitializerCalls = new List<BoundCall>();
        void CollectInitializerCalls(BoundNativeFixedArrayInitialization
            initialization)
        {
            foreach (var element in initialization.Elements)
            {
                if (element is BoundCall call
                    && originalProgram.Functions[call.Function].Name
                        == "GetDriveTypeW")
                {
                    sourceInitializerCalls.Add(call);
                }
                else if (element
                    is BoundNativeFixedArrayInitialization nested)
                {
                    CollectInitializerCalls(nested);
                }
            }
        }
        void CollectSourceCalls(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundBlock block:
                    foreach (var nested in block.Statements)
                    {
                        CollectSourceCalls(nested);
                    }
                    break;
                case BoundUnsafe unsafeBlock:
                    CollectSourceCalls(unsafeBlock.Body);
                    break;
                case BoundLocal local when local.Initializer is BoundCall call
                    && originalProgram.Functions[call.Function].Name
                        == "GetDriveTypeW":
                    sourceInitializerCalls.Add(call);
                    break;
                case BoundLocal {
                    Initializer: BoundNativeFixedArrayInitialization
                        initialization,
                }:
                    CollectInitializerCalls(initialization);
                    break;
            }
        }
        CollectSourceCalls(originalMain.Body);
        Assert.That(sourceInitializerCalls, Has.Count.EqualTo(5));
        var pathAVariable = Array.FindIndex(originalMain.Variables.ToArray(),
            static variable => variable.Name == "pathA");
        var pathBVariable = Array.FindIndex(originalMain.Variables.ToArray(),
            static variable => variable.Name == "pathB");
        Assert.That(pathAVariable, Is.GreaterThanOrEqualTo(0));
        Assert.That(pathBVariable, Is.GreaterThanOrEqualTo(0));
        var sourceInitializerAddressVariables = sourceInitializerCalls
            .Select(static call =>
                ((BoundNativeStorageAddress)call.Arguments[0]).Variable)
            .ToArray();
        Assert.That(sourceInitializerAddressVariables, Is.EqualTo([
            pathAVariable,
            pathAVariable,
            pathBVariable,
            pathBVariable,
            pathAVariable,
        ]));
        Assert.That(initializerAddressVariables,
            Is.EqualTo(sourceInitializerAddressVariables));
        var outerInitialization = initializations.Single(
            static initialization => NativeFixedArrayTypes.IsType(
                initialization.Layout.ElementType));
        var scalarResults = new List<BoundRead>();
        void CollectScalarResults(
            BoundNativeFixedArrayInitialization initialization)
        {
            foreach (var element in initialization.Elements)
            {
                if (element
                    is BoundNativeFixedArrayInitialization nested)
                {
                    CollectScalarResults(nested);
                }
                else
                {
                    Assert.That(element, Is.TypeOf<BoundRead>());
                    scalarResults.Add((BoundRead)element);
                }
            }
        }
        CollectScalarResults(outerInitialization);
        Assert.That(scalarResults, Has.Count.EqualTo(4));
        Assert.That(scalarResults.All(static value =>
            value.Type == PrimitiveType.Int32), Is.True);
        Assert.That(TrRmExecutableCompiler.EmitStandard(
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    reencodedConsumer),
                consumerIdentity, [providerInput]),
            consumerIdentity, schemaIdentity, 1),
            Is.EqualTo(consumerArtifact));

        Assert.That(NativeModuleCompiler.ExecuteStandard(combinedInput,
            consumerIdentity, [], NativeRuntimePolicy.FailFast), Is.EqualTo(1));
        await AssertStandardA2SourceFreeJitAndAotAsync(consumerIdentity,
            combinedInput, providerSource + Environment.NewLine
                + consumerSource, 1,
            "nested scalar call initializer results", runJit: false,
            expectedReemitted: consumerArtifact).ConfigureAwait(true);

        var failureArtifact = EmitConsumer(failureSource);
        var failureCombinedArtifact
            = CombinePopulatedNativeAddressModules(providerArtifact,
                failureArtifact, schema);
        var failureInput = new TrRmStandardArtifactInput(schemaIdentity,
            failureCombinedArtifact);
        var failureLoaded = TrRmExecutableCompiler.LoadStandard(failureInput,
            consumerIdentity, []);
        Assert.That(TrRmExecutableCompiler.EmitStandard(failureLoaded,
            consumerIdentity, schemaIdentity, 1),
            Is.EqualTo(failureArtifact));
        await AssertStandardA2SourceFreeJitAndAotAsync(consumerIdentity,
            failureInput, providerSource + Environment.NewLine
                + failureSource,
            unchecked((int)0xE0540004),
            "nested initializer address failure before later calls",
            runJit: false, expectedReemitted: failureArtifact)
            .ConfigureAwait(true);
    }

    [Test]
    public async Task StandardA2NestedPointerArrayValuesReachNativeParameters()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-nested-pointer-values";
        const string source = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            public extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<ptr<int32>> source,
                uint64 byteCount);

            int32 Main() {
                int32 zeroIndex = 0;
                mut [int32; 3] values = [0, -1, 0];
                mut int32 observed = 0;
                uint64 byteCount = 0;
                unsafe {
                    mut [[ptr<int32>; 1]; 1] pointers = [
                        [&values[zeroIndex]]];
                    [[ptr<int32>; 1]; 1] copied = pointers;
                    mut [[ptr<int32>; 1]; 1] assigned = [
                        [&values[zeroIndex]]];
                    assigned = copied;
                    mut [[ptr<ptr<int32>>; 1]; 1] cells = [
                        [&assigned[zeroIndex][zeroIndex]]];
                    ptr<ptr<int32>> selectedCell
                        = cells[zeroIndex][zeroIndex];
                    var callback = &RtlMoveMemory;
                    callback(&observed, selectedCell, byteCount);
                }
                return observed;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration
                    = NativeCoreRegistrationTests.CreateRegistration(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var malformedPointerStore = ReplaceA2PointerStoreType(artifact);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    malformedPointerStore), moduleIdentity, []));
        var input = new TrRmStandardArtifactInput(schemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
        Assert.That(NativeModuleCompiler.ExecuteStandard(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast), Is.Zero);
        await AssertStandardA2SourceFreeJitAndAotAsync(moduleIdentity,
            input, source, 0, "nested pointer array NativeParameter value",
            expectedReemitted: artifact).ConfigureAwait(true);

        var failingSource = source.Replace("&values[zeroIndex]",
            "&values[badIndex]", StringComparison.Ordinal)
            .Replace("int32 zeroIndex = 0;",
                "int32 zeroIndex = 0;\n                int32 badIndex = -1;",
                StringComparison.Ordinal);
        var failingCompilation = Compilation.Create(
            SyntaxTree.Parse(failingSource), new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration
                    = NativeCoreRegistrationTests.CreateRegistration(),
            });
        Assert.That(failingCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                failingCompilation.Diagnostics));
        const string failureModuleIdentity
            = "a2-nested-pointer-values-bounds-failure";
        var failureArtifact = TrRmExecutableCompiler.EmitStandard(
            failingCompilation.Program!, failureModuleIdentity,
            schemaIdentity, 1);
        var failureInput = new TrRmStandardArtifactInput(schemaIdentity,
            failureArtifact);
        await AssertStandardA2SourceFreeJitAndAotAsync(
            failureModuleIdentity, failureInput, failingSource,
            unchecked((int)0xE0540004),
            "nested pointer address failure before NativeParameter call",
            runJit: false,
            expectedReemitted: failureArtifact).ConfigureAwait(true);
    }

    [Test]
    public async Task StandardA2NestedPointerArrayCopySurvivesSourceFreeExecution()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-nested-pointer-array-copy";
        const string source = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            public extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            int32 Main() {
                int32 zeroIndex = 0;
                mut [int32; 3] values = [-1, 0, 0];
                mut int32 observed = 0;
                uint64 byteCount = 0;
                unsafe {
                    [[ptr<int32>; 1]; 1] original = [
                        [&values[zeroIndex]]];
                    [[ptr<int32>; 1]; 1] copied = original;
                    mut [[ptr<int32>; 1]; 1] assigned = [
                        [&values[zeroIndex]]];
                    assigned = copied;
                    ptr<int32> selected
                        = assigned[zeroIndex][zeroIndex];
                    var callback = &RtlMoveMemory;
                    callback(&observed, selected, byteCount);
                }
                return observed;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration
                    = NativeCoreRegistrationTests.CreateRegistration(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var input = new TrRmStandardArtifactInput(schemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
        Assert.That(NativeModuleCompiler.ExecuteStandard(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast), Is.Zero);

        await AssertStandardA2SourceFreeJitAndAotAsync(moduleIdentity,
            input, source, 0, "nested pointer array whole-value copy",
            expectedReemitted: artifact).ConfigureAwait(true);
    }

    [Test]
    public async Task StandardA2NestedPointerCellValuesSurviveProviderRemapping()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        const string providerIdentity = "a2-pointer-cell-provider";
        const string consumerIdentity = "a2-pointer-cell-consumer";
        const string providerSource = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            public extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<ptr<int32>> source,
                uint64 byteCount);

            internal int32 ProviderBody() {
                int32 zeroIndex = 0;
                mut [int32; 1] values = [0];
                mut int32 observed = 0;
                uint64 byteCount = 0;
                unsafe {
                    mut ptr<int32> cell = &values[zeroIndex];
                    var callback = &RtlMoveMemory;
                    callback(&observed, &cell, byteCount);
                }
                return observed;
            }
            """;
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource), core);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, schemaIdentity, 1);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, []);

        const string consumerSource = """
            int32 Main() {
                int32 zeroIndex = 0;
                mut [int32; 3] values = [0, -1, 0];
                mut int32 observed = 0;
                uint64 byteCount = 0;
                unsafe {
                    mut [[ptr<int32>; 1]; 1] pointers = [
                        [&values[zeroIndex]]];
                    [[ptr<int32>; 1]; 1] copied = pointers;
                    mut [[ptr<int32>; 1]; 1] assigned = [
                        [&values[zeroIndex]]];
                    assigned = copied;
                    mut [[ptr<ptr<int32>>; 1]; 1] cells = [
                        [&assigned[zeroIndex][zeroIndex]]];
                    ptr<ptr<int32>> selectedCell
                        = cells[zeroIndex][zeroIndex];
                    var callback = &RtlMoveMemory;
                    callback(&observed, selectedCell, byteCount);
                }
                return observed;
            }
            """;
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse(consumerSource), providerReference,
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration
                    = NativeCoreRegistrationTests.CreateRegistration(),
            });
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerCompilation.Program!, consumerIdentity, schemaIdentity,
            1);
        var remappedProviderArtifact = PermuteA2BodyLocalMaps(
            providerArtifact, "ProviderBody");
        var remappedConsumerArtifact = PermuteA2BodyLocalMaps(
            consumerArtifact, "Main");
        Assert.That(remappedProviderArtifact, Is.Not.EqualTo(providerArtifact));
        Assert.That(remappedConsumerArtifact, Is.Not.EqualTo(consumerArtifact));

        var noCopyConsumerSource = consumerSource.Replace(
            "                    [[ptr<int32>; 1]; 1] copied = pointers;\n"
                + "                    mut [[ptr<int32>; 1]; 1] assigned = [\n"
                + "                        [&values[zeroIndex]]];\n"
                + "                    assigned = copied;\n",
            "", StringComparison.Ordinal)
            .Replace("&assigned[zeroIndex][zeroIndex]",
                "&pointers[zeroIndex][zeroIndex]", StringComparison.Ordinal);
        var noCopyConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse(noCopyConsumerSource), providerReference,
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration
                    = NativeCoreRegistrationTests.CreateRegistration(),
            });
        Assert.That(noCopyConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                noCopyConsumerCompilation.Diagnostics));
        var noCopyConsumerArtifact = TrRmExecutableCompiler.EmitStandard(
            noCopyConsumerCompilation.Program!, consumerIdentity,
            schemaIdentity, 1);
        var remappedNoCopyConsumerArtifact = PermuteA2BodyLocalMaps(
            noCopyConsumerArtifact, "Main");
        var malformedD9Consumer = ReplaceA2PointerStoreType(
            remappedNoCopyConsumerArtifact);
        _ = Assert.Throws<InvalidDataException>(() =>
            CombinePopulatedNativeAddressModules(remappedProviderArtifact,
                malformedD9Consumer, schema));

        var combinedArtifact = CombinePopulatedNativeAddressModules(
            remappedProviderArtifact, remappedConsumerArtifact, schema);
        var input = new TrRmStandardArtifactInput(schemaIdentity,
            combinedArtifact);
        AssertA2ProviderBodyMalformedInputs(schemaIdentity,
            providerIdentity, consumerIdentity, combinedArtifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            consumerIdentity, []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            consumerIdentity, schemaIdentity, 1);
        var reemittedInput = new TrRmStandardArtifactInput(schemaIdentity,
            reemitted);
        var reemittedLoaded = TrRmExecutableCompiler.LoadStandard(
            reemittedInput, consumerIdentity,
            [new TrRmStandardArtifactInput(schemaIdentity,
                remappedProviderArtifact)]);
        Assert.That(TrRmExecutableCompiler.EmitStandard(reemittedLoaded,
            consumerIdentity, schemaIdentity, 1), Is.EqualTo(reemitted));
        Assert.That(NativeModuleCompiler.ExecuteStandard(input,
            consumerIdentity, [], NativeRuntimePolicy.FailFast), Is.Zero);

        await AssertStandardA2SourceFreeJitAndAotAsync(consumerIdentity,
            input, providerSource + Environment.NewLine + consumerSource,
            0, "nested pointer-cell TypeSpec provider remapping",
            runJit: false,
            expectedReemitted: reemitted).ConfigureAwait(true);

        var reversedArtifact = CombinePopulatedNativeAddressModules(
            remappedConsumerArtifact, remappedProviderArtifact, schema);
        var reversedTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(
                        reversedArtifact));
        var reversedConsumerModuleRow = 0;
        for (var moduleRow = 1;
            moduleRow <= reversedTables.Modules.Count; moduleRow++)
        {
            if (reversedTables.Modules.GetIdentity(moduleRow)
                == consumerIdentity)
            {
                reversedConsumerModuleRow = moduleRow;
                break;
            }
        }
        Assert.That(reversedConsumerModuleRow, Is.EqualTo(1));
        var reversedMainRow = 0;
        for (var methodRow = 1;
            methodRow <= reversedTables.Methods.Count; methodRow++)
        {
            if (reversedTables.Methods.GetOwnerModule(methodRow)
                    == reversedConsumerModuleRow
                && reversedTables.Methods.GetName(methodRow) == "Main")
            {
                reversedMainRow = methodRow;
                break;
            }
        }
        Assert.That(reversedMainRow, Is.GreaterThan(0));
        Assert.That(reversedTables.Methods.GetBodyRow(reversedMainRow),
            Is.EqualTo(1));
        var reversedInput = new TrRmStandardArtifactInput(schemaIdentity,
            reversedArtifact);
        var reversedLoaded = TrRmExecutableCompiler.LoadStandard(
            reversedInput, consumerIdentity, []);
        var reversedReemitted = TrRmExecutableCompiler.EmitStandard(
            reversedLoaded, consumerIdentity, schemaIdentity, 1);
        Assert.That(reversedReemitted, Is.EqualTo(reemitted));
        var reversedReemittedInput = new TrRmStandardArtifactInput(
            schemaIdentity, reversedReemitted);
        var reversedReemittedLoaded = TrRmExecutableCompiler.LoadStandard(
            reversedReemittedInput, consumerIdentity,
            [new TrRmStandardArtifactInput(schemaIdentity,
                remappedProviderArtifact)]);
        Assert.That(TrRmExecutableCompiler.EmitStandard(
            reversedReemittedLoaded, consumerIdentity, schemaIdentity, 1),
            Is.EqualTo(reversedReemitted));
        Assert.That(NativeModuleCompiler.ExecuteStandard(reversedInput,
            consumerIdentity, [], NativeRuntimePolicy.FailFast), Is.Zero);
        await AssertStandardA2SourceFreeJitAndAotAsync(consumerIdentity,
            reversedInput,
            providerSource + Environment.NewLine + consumerSource,
            0, "consumer-before-provider Body and TypeMap row remapping",
            runJit: false, expectedReemitted: reversedReemitted)
            .ConfigureAwait(true);

        var compatibleProviderSource = providerSource.Replace(
            "uint64 byteCount = 0;",
            "mut [uint32; 2] padding = [0, 0];\n"
                + "                uint64 byteCount = 0;",
            StringComparison.Ordinal);
        var compatibleProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(compatibleProviderSource), core);
        Assert.That(compatibleProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                compatibleProviderCompilation.Diagnostics));
        var compatibleProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            compatibleProviderCompilation.Program!, providerIdentity,
            schemaIdentity, 1);
        Assert.That(compatibleProviderArtifact,
            Is.Not.EqualTo(providerArtifact));
        var remappedCompatibleProvider = PermuteA2BodyLocalMaps(
            compatibleProviderArtifact, "ProviderBody");
        var compatibleCombinedArtifact = CombinePopulatedNativeAddressModules(
            remappedCompatibleProvider, remappedConsumerArtifact, schema);
        var compatibleInput = new TrRmStandardArtifactInput(schemaIdentity,
            compatibleCombinedArtifact);
        var compatibleLoaded = TrRmExecutableCompiler.LoadStandard(
            compatibleInput, consumerIdentity, []);
        var compatibleReemitted = TrRmExecutableCompiler.EmitStandard(
            compatibleLoaded, consumerIdentity, schemaIdentity, 1);
        Assert.That(compatibleReemitted, Is.EqualTo(reemitted));
        Assert.That(NativeModuleCompiler.ExecuteStandard(compatibleInput,
            consumerIdentity, [], NativeRuntimePolicy.FailFast), Is.Zero);
        await AssertStandardA2SourceFreeJitAndAotAsync(consumerIdentity,
            compatibleInput,
            compatibleProviderSource + Environment.NewLine + consumerSource,
            0, "compatible provider replacement with Body map permutations",
            runJit: false, expectedReemitted: compatibleReemitted)
            .ConfigureAwait(true);

        var incompatibleProviderSource = providerSource
            .Replace("NativeParameter(2, Read, NoEscape, 1)",
                "NativeParameter(2, Write, NoEscape, 1)",
                StringComparison.Ordinal)
            .Replace("ptr<ptr<int32>> source",
                "write ptr<ptr<int32>> source",
                StringComparison.Ordinal);
        var incompatibleProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(incompatibleProviderSource), core);
        Assert.That(incompatibleProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleProviderCompilation.Diagnostics));
        var incompatibleProviderArtifact
            = TrRmExecutableCompiler.EmitStandard(
                incompatibleProviderCompilation.Program!, providerIdentity,
                schemaIdentity, 1);
        Assert.That(incompatibleProviderArtifact,
            Is.Not.EqualTo(providerArtifact));
        var remappedIncompatibleProvider = PermuteA2BodyLocalMaps(
            incompatibleProviderArtifact, "ProviderBody");
        var incompatibleCombinedArtifact
            = CombinePopulatedNativeAddressModules(
                remappedIncompatibleProvider, remappedConsumerArtifact,
                schema);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    incompatibleCombinedArtifact), consumerIdentity, []));
    }

    private static void AssertA2ProviderBodyMalformedInputs(
        string schemaIdentity, string providerIdentity,
        string consumerIdentity, byte[] combinedArtifact)
    {
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyDraft(
                combinedArtifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                index);
        var providerModuleRow = 0;
        var consumerModuleRow = 0;
        for (var moduleRow = 1; moduleRow <= tables.Modules.Count;
            moduleRow++)
        {
            var identity = tables.Modules.GetIdentity(moduleRow);
            if (identity == providerIdentity)
            {
                providerModuleRow = moduleRow;
            }
            else if (identity == consumerIdentity)
            {
                consumerModuleRow = moduleRow;
            }
        }
        Assert.That(providerModuleRow, Is.GreaterThan(0));
        Assert.That(consumerModuleRow, Is.GreaterThan(0));

        var mainRow = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            if (tables.Methods.GetOwnerModule(methodRow) == consumerModuleRow
                && tables.Methods.GetName(methodRow) == "Main")
            {
                mainRow = methodRow;
                break;
            }
        }
        Assert.That(mainRow, Is.GreaterThan(0));
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(mainRow), tables.Contracts.Count);

        var providerLayoutRow = 0;
        var providerArrayTypeSpecRow = 0;
        var consumerArrayTypeSpecRows = new HashSet<int>();
        for (var layoutRow = 1;
            layoutRow <= tables.FixedArrayLayouts.Count; layoutRow++)
        {
            var layout = tables.FixedArrayLayouts.GetRow(layoutRow);
            if (layout.OwnerModuleRow == providerModuleRow
                && providerLayoutRow == 0)
            {
                providerLayoutRow = layoutRow;
                providerArrayTypeSpecRow = layout.ArrayTypeSpecRow;
            }
            else if (layout.OwnerModuleRow == consumerModuleRow)
            {
                _ = consumerArrayTypeSpecRows.Add(layout.ArrayTypeSpecRow);
            }
        }
        Assert.That(providerLayoutRow, Is.GreaterThan(0));
        Assert.That(providerArrayTypeSpecRow, Is.GreaterThan(0));
        Assert.That(consumerArrayTypeSpecRows, Is.Not.Empty);
        Assert.That(consumerArrayTypeSpecRows,
            Does.Not.Contain(providerArrayTypeSpecRow));

        var consumerArrayTypeMapIndex = -1;
        for (var typeMapIndex = 0; typeMapIndex < body.TypeCount;
            typeMapIndex++)
        {
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                body.TypeMap.Slice(typeMapIndex * sizeof(uint)));
            if ((byte)(token >> 24) == 0x11
                && consumerArrayTypeSpecRows.Contains(
                    checked((int)(token & 0x00FF_FFFF))))
            {
                consumerArrayTypeMapIndex = typeMapIndex;
                break;
            }
        }
        Assert.That(consumerArrayTypeMapIndex,
            Is.GreaterThanOrEqualTo(0));

        var malformedTypeMapToken = RewriteA2BodyArtifact(
            combinedArtifact, "Main",
            (_, typeMap, _, _) =>
                BinaryPrimitives.WriteUInt32LittleEndian(typeMap,
                    0xFF00_0000));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    malformedTypeMapToken), consumerIdentity, []));

        var providerArrayTypeToken = 0x1100_0000u
            | checked((uint)providerArrayTypeSpecRow);
        var malformedTypeMapOwner = RewriteA2BodyArtifact(
            combinedArtifact, "Main",
            (_, typeMap, _, _) =>
                BinaryPrimitives.WriteUInt32LittleEndian(
                    typeMap.AsSpan(consumerArrayTypeMapIndex * sizeof(uint)),
                    providerArrayTypeToken));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    malformedTypeMapOwner), consumerIdentity, []));

        var malformedPlaceSelectionOwner = RewriteA2BodyArtifact(
            combinedArtifact, "Main",
            (_, _, records, _) =>
            {
                var placeSelection = -1;
                for (var recordIndex = 0;
                    recordIndex < records.Length; recordIndex++)
                {
                    var record = records[recordIndex];
                    if (record[0] == 0x04 && record.Length == 24
                        && record[4] == 3 && record[13] == 3)
                    {
                        placeSelection = recordIndex;
                        break;
                    }
                }
                Assert.That(placeSelection, Is.GreaterThanOrEqualTo(0));
                BinaryPrimitives.WriteUInt32LittleEndian(
                    records[placeSelection].AsSpan(16),
                    checked((uint)providerLayoutRow));
            });
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    malformedPlaceSelectionOwner), consumerIdentity, []));

        var malformedHomeCopy = ReplaceA2HomeCopyTargetWithIncompatibleType(
            combinedArtifact, "Main");
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    malformedHomeCopy), consumerIdentity, []));
    }

    private static byte[] ReplaceA2PointerStoreType(byte[] artifact)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                index);
        var mainRow = 0;
        for (var methodRow = 1;
            methodRow <= tables.Methods.Count; methodRow++)
        {
            if (tables.Methods.GetName(methodRow) == "Main")
            {
                mainRow = methodRow;
                break;
            }
        }
        Assert.That(mainRow, Is.GreaterThan(0));
        var mainBodyRow = tables.Methods.GetBodyRow(mainRow);
        var body = tables.Bodies.GetRegisterBody(mainBodyRow,
            tables.Contracts.Count);

        static uint GetBodyTypeSpecRef(TrRmRegisterBodyReader body,
            int typeMapIndex)
        {
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                body.TypeMap.Slice(typeMapIndex * sizeof(uint)));
            return (byte)(token >> 24) switch {
                0x11 => token & 0x00FF_FFFF,
                0xF2 or 0xF3 => token,
                _ => throw new InvalidOperationException(
                    "The A2 pointer-store fixture has an invalid TypeMap token."),
            };
        }

        var pointerToPointerStoreD5Offset = -1;
        var wrongPointerValueHome = -1;
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            var codeOffset = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(block));
            var code = body.GetBlockCode(blockIndex);
            for (var position = 0; position < code.Length;)
            {
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        code[position], schema);
                if (code[position] == 0xD9 && position >= 8
                    && code[position - 4] == 0xD5
                    && code[position - 8] == 0xD8)
                {
                    var selectionIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            code[(position - 8 + 2)..]);
                    var selection = body.GetRecord(selectionIndex);
                    var typeMapIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            selection[20..]);
                    var selectedType = tables.TypeSpecs.GetTypeSpecRef(
                        GetBodyTypeSpecRef(body, typeMapIndex));
                    if (selectedType.Form == 0x0C
                        && selectedType.TypeCode == 0x8C)
                    {
                        pointerToPointerStoreD5Offset
                            = codeOffset + position - 4;
                    }
                }

                position += instructionLength;
            }
        }
        Assert.That(pointerToPointerStoreD5Offset,
            Is.GreaterThanOrEqualTo(0));

        for (var homeIndex = 0; homeIndex < body.HomeCount; homeIndex++)
        {
            var home = body.HomeMap.Slice(homeIndex * 8, 8);
            var typeMapIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(home[2..]);
            var type = tables.TypeSpecs.GetTypeSpecRef(
                GetBodyTypeSpecRef(body, typeMapIndex));
            if (home[0] == 3 && type.Form == 0x01
                && type.TypeCode == 0x8C)
            {
                wrongPointerValueHome = homeIndex;
                break;
            }
        }
        Assert.That(wrongPointerValueHome, Is.GreaterThanOrEqualTo(0));

        return RewriteA2BodyArtifact(artifact, "Main",
            (_, _, _, code) =>
                BinaryPrimitives.WriteUInt16LittleEndian(
                    code.AsSpan(pointerToPointerStoreD5Offset + 2),
                    checked((ushort)wrongPointerValueHome)));
    }

    private static byte[] RewriteA2BodyArtifact(byte[] artifact,
        string methodName,
        Action<byte[], byte[], byte[][], byte[]> rewrite,
        bool isRepeatInitialization = false,
        bool preserveInvalidBodyRecords = false)
    {
        var schema = isRepeatInitialization
            ? TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft
            : TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        var index = isRepeatInitialization
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft(
                    artifact)
            : TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyDraft(artifact);
        var tables = isRepeatInitialization
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index)
            : TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    index);
        var methodRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == methodName)
            {
                methodRow = row;
                break;
            }
        }
        Assert.That(methodRow, Is.GreaterThan(0));
        var bodyRow = tables.Methods.GetBodyRow(methodRow);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var homeMap = body.HomeMap.ToArray();
        var typeMap = body.TypeMap.ToArray();
        var records = new byte[body.RecordCount][];
        for (var recordIndex = 0; recordIndex < records.Length;
            recordIndex++)
        {
            records[recordIndex] = body.GetRecord(recordIndex).ToArray();
        }
        var code = body.Code.ToArray();
        rewrite(homeMap, typeMap, records, code);

        if (!index.TryGetSection(0x0020, out var bodySection,
                out var bodyCount))
        {
            throw new InvalidOperationException(
                "The A2 body rewriter has no Body section.");
        }

        var bodyTable = new TrRmBodyTableReader(bodySection, bodyCount,
            schema);
        byte[] rewrittenBody;
        if (preserveInvalidBodyRecords)
        {
            if (!homeMap.AsSpan().SequenceEqual(body.HomeMap)
                || !typeMap.AsSpan().SequenceEqual(body.TypeMap)
                || !code.AsSpan().SequenceEqual(body.Code))
            {
                throw new InvalidOperationException(
                    "Raw A2 body record rewrites cannot change maps or code.");
            }

            rewrittenBody = bodyTable.GetRow(bodyRow).ToArray();
            for (var recordIndex = 0; recordIndex < records.Length;
                recordIndex++)
            {
                var originalRecord = body.GetRecord(recordIndex);
                if (originalRecord.SequenceEqual(records[recordIndex]))
                {
                    continue;
                }

                var recordOffset = -1;
                for (var offset = 0;
                    offset <= rewrittenBody.Length - originalRecord.Length;
                    offset++)
                {
                    if (!rewrittenBody.AsSpan(offset,
                            originalRecord.Length)
                        .SequenceEqual(originalRecord))
                    {
                        continue;
                    }

                    if (recordOffset >= 0)
                    {
                        throw new InvalidOperationException(
                            "A raw A2 body record is not uniquely framed.");
                    }

                    recordOffset = offset;
                }

                if (recordOffset < 0)
                {
                    throw new InvalidOperationException(
                        "A raw A2 body record was not found in its row.");
                }

                records[recordIndex].CopyTo(
                    rewrittenBody.AsSpan(recordOffset));
            }
        }
        else
        {
            var blocks = new TrRmRegisterBodyBlock[body.BlockCount];
            for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
            {
                var block = body.GetBlock(blockIndex);
                blocks[blockIndex] = new(
                    BinaryPrimitives.ReadUInt32LittleEndian(block),
                    BinaryPrimitives.ReadUInt32LittleEndian(block[4..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(block[8..]));
            }
            var encodedRecords = new ReadOnlyMemory<byte>[records.Length];
            for (var recordIndex = 0; recordIndex < records.Length;
                recordIndex++)
            {
                encodedRecords[recordIndex] = records[recordIndex];
            }
            rewrittenBody = TrRmRegisterBodyWriter.Encode(homeMap, typeMap,
                body.MethodMap.ToArray(), body.FieldMap.ToArray(),
                body.ConstantMap.ToArray(), blocks, encodedRecords, code,
                tables.Contracts.Count, schema);
        }

        var bodyRows = new ReadOnlyMemory<byte>[bodyCount];
        for (var row = 1; row <= bodyCount; row++)
        {
            bodyRows[row - 1] = row == bodyRow
                ? rewrittenBody : bodyTable.GetRow(row).ToArray();
        }
        var rewrittenBodySection
            = TrRmVariableTableWriter.EncodeBodies(bodyRows);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            sections.Add(kind == 0x0020
                ? new TrRmSection(kind, flags, checked((uint)bodyCount),
                    rewrittenBodySection)
                : new TrRmSection(kind, flags,
                    checked((uint)recordCount), section.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] ReplaceA2HomeCopyTargetWithIncompatibleType(
        byte[] artifact, string methodName)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                index);
        var methodRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == methodName)
            {
                methodRow = row;
                break;
            }
        }
        Assert.That(methodRow, Is.GreaterThan(0));
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(methodRow), tables.Contracts.Count);
        var homeTypeSpecRefs = new uint[body.HomeCount];
        var isFixedArrayHome = new bool[body.HomeCount];
        for (var homeIndex = 0; homeIndex < body.HomeCount; homeIndex++)
        {
            var home = body.HomeMap.Slice(homeIndex * 8, 8);
            var typeMapIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(home[2..]);
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                body.TypeMap.Slice(typeMapIndex * sizeof(uint)));
            if ((byte)(token >> 24) != 0x11)
            {
                continue;
            }

            var typeSpecRef = token & 0x00FF_FFFF;
            homeTypeSpecRefs[homeIndex] = typeSpecRef;
            isFixedArrayHome[homeIndex]
                = tables.TypeSpecs.GetTypeSpecRef(typeSpecRef).Form == 0x11;
        }

        var copyStoreOffset = -1;
        var incompatibleHome = -1;
        for (var blockIndex = 0; blockIndex < body.BlockCount;
            blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            var codeOffset = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(block));
            var code = body.GetBlockCode(blockIndex);
            for (var position = 0; position < code.Length;)
            {
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        code[position], schema);
                if (code[position] == 0xD5)
                {
                    var sourceRegister = code[position + 1] & 0x0F;
                    var sourceHome
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            code[(position + 2)..]);
                    var nextPosition = position + instructionLength;
                    if (nextPosition < code.Length
                        && code[nextPosition] == 0xD6
                        && (code[nextPosition + 1] & 0x0F)
                            == sourceRegister
                        && isFixedArrayHome[sourceHome])
                    {
                        for (var candidate = 0;
                            candidate < body.HomeCount; candidate++)
                        {
                            var candidateHome = body.HomeMap.Slice(
                                candidate * 8, 8);
                            if (candidate == sourceHome
                                || candidateHome[0] != 3
                                || !isFixedArrayHome[candidate]
                                || tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                                    homeTypeSpecRefs[sourceHome],
                                    homeTypeSpecRefs[candidate]))
                            {
                                continue;
                            }

                            copyStoreOffset = codeOffset + nextPosition + 2;
                            incompatibleHome = candidate;
                            break;
                        }
                    }
                }

                if (copyStoreOffset >= 0)
                {
                    break;
                }
                position += instructionLength;
            }
            if (copyStoreOffset >= 0)
            {
                break;
            }
        }
        Assert.That(copyStoreOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(incompatibleHome, Is.GreaterThanOrEqualTo(0));

        return RewriteA2BodyArtifact(artifact, methodName,
            (_, _, _, code) =>
                BinaryPrimitives.WriteUInt16LittleEndian(
                    code.AsSpan(copyStoreOffset),
                    checked((ushort)incompatibleHome)));
    }

    private static byte[] PermuteA2BodyLocalMaps(byte[] artifact,
        string methodName)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                index);
        var methodRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == methodName)
            {
                methodRow = row;
                break;
            }
        }
        Assert.That(methodRow, Is.GreaterThan(0));
        var bodyRow = tables.Methods.GetBodyRow(methodRow);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        Assert.That(body.HomeCount, Is.GreaterThan(3));
        Assert.That(body.HasExceptionDirectory, Is.False);
        var typeMapCount = body.TypeMap.Length / sizeof(uint);
        Assert.That(typeMapCount, Is.GreaterThan(1));
        var newTypeMapIndexByOld = new ushort[typeMapCount];
        var newHomeIndexByOld = new ushort[body.HomeCount];
        for (var homeIndex = 0; homeIndex < body.HomeCount; homeIndex++)
        {
            newHomeIndexByOld[homeIndex] = checked((ushort)homeIndex);
        }
        newHomeIndexByOld[2] = 3;
        newHomeIndexByOld[3] = 2;
        var typeMap = new byte[body.TypeMap.Length];
        for (var oldIndex = 0; oldIndex < typeMapCount; oldIndex++)
        {
            var newIndex = typeMapCount - oldIndex - 1;
            newTypeMapIndexByOld[oldIndex] = checked((ushort)newIndex);
            body.TypeMap.Slice(oldIndex * sizeof(uint), sizeof(uint))
                .CopyTo(typeMap.AsSpan(newIndex * sizeof(uint)));
        }

        ushort RemapTypeMapIndex(ushort oldIndex)
        {
            if (oldIndex >= newTypeMapIndexByOld.Length)
            {
                throw new InvalidOperationException(
                    "The A2 TypeMap permutation found an invalid local index.");
            }

            return newTypeMapIndexByOld[oldIndex];
        }

        ushort RemapHomeIndex(ushort oldIndex)
        {
            if (oldIndex >= newHomeIndexByOld.Length)
            {
                throw new InvalidOperationException(
                    "The A2 HomeMap permutation found an invalid local index.");
            }

            return newHomeIndexByOld[oldIndex];
        }

        var homeMap = new byte[body.HomeMap.Length];
        for (var homeIndex = 0; homeIndex < body.HomeCount; homeIndex++)
        {
            var newHomeIndex = newHomeIndexByOld[homeIndex];
            body.HomeMap.Slice(homeIndex * 8, 8)
                .CopyTo(homeMap.AsSpan(newHomeIndex * 8));
        }
        for (var homeIndex = 0; homeIndex < body.HomeCount; homeIndex++)
        {
            var offset = (homeIndex * 8) + 2;
            var oldIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                homeMap.AsSpan(offset));
            BinaryPrimitives.WriteUInt16LittleEndian(
                homeMap.AsSpan(offset),
                RemapTypeMapIndex(oldIndex));
        }

        var records = new ReadOnlyMemory<byte>[body.RecordCount];
        for (var recordIndex = 0;
            recordIndex < body.RecordCount; recordIndex++)
        {
            var record = body.GetRecord(recordIndex).ToArray();
            switch (record[0])
            {
                case 0x01:
                {
                    var payload = record.AsSpan(4);
                    var registerCount
                        = BinaryPrimitives.ReadUInt32LittleEndian(payload);
                    for (var row = 0; row < registerCount; row++)
                    {
                        var offset = 4 + checked((int)row * 5);
                        var carrier = payload[offset + 1];
                        if (carrier is 0 or 1)
                        {
                            var oldIndex
                                = BinaryPrimitives.ReadUInt16LittleEndian(
                                    payload[(offset + 2)..]);
                            BinaryPrimitives.WriteUInt16LittleEndian(
                                payload[(offset + 2)..],
                                RemapTypeMapIndex(oldIndex));
                        }
                    }
                    break;
                }

                case 0x04:
                {
                    var placePayload = record.AsSpan(4);
                    if (placePayload[3] == 2)
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(
                            placePayload[6..],
                            RemapHomeIndex(
                                BinaryPrimitives.ReadUInt16LittleEndian(
                                    placePayload[6..])));
                    }
                    if (placePayload[8] == 2)
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(
                            placePayload[10..],
                            RemapHomeIndex(
                                BinaryPrimitives.ReadUInt16LittleEndian(
                                    placePayload[10..])));
                    }
                    if (placePayload[0] == 1
                        && placePayload[9] == 0)
                    {
                        var oldHomeIndex
                            = BinaryPrimitives.ReadUInt32LittleEndian(
                                placePayload[12..]);
                        if (oldHomeIndex > ushort.MaxValue)
                        {
                            throw new InvalidOperationException(
                                "The A2 PlaceSelection has an invalid HomeMap index.");
                        }
                        BinaryPrimitives.WriteUInt32LittleEndian(
                            placePayload[12..],
                            RemapHomeIndex((ushort)oldHomeIndex));
                    }
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        record.AsSpan(20),
                        RemapTypeMapIndex(
                            BinaryPrimitives.ReadUInt16LittleEndian(
                                record.AsSpan(20))));
                    break;
                }

                case 0x02:
                {
                    if (record[5] == 2)
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(
                            record.AsSpan(10),
                            RemapHomeIndex(
                                BinaryPrimitives.ReadUInt16LittleEndian(
                                    record.AsSpan(10))));
                    }
                    var inputCount = BinaryPrimitives.ReadUInt32LittleEndian(
                        record.AsSpan(12));
                    for (var input = 0; input < inputCount; input++)
                    {
                        var inputOffset = 20 + checked((int)input * 4);
                        if (record[inputOffset] == 2)
                        {
                            BinaryPrimitives.WriteUInt16LittleEndian(
                                record.AsSpan(inputOffset + 2),
                                RemapHomeIndex(
                                    BinaryPrimitives.ReadUInt16LittleEndian(
                                        record.AsSpan(inputOffset + 2))));
                        }
                    }
                    break;
                }

                case 0x0D:
                    if (record[4] == 2)
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(
                            record.AsSpan(6),
                            RemapHomeIndex(
                                BinaryPrimitives.ReadUInt16LittleEndian(
                                    record.AsSpan(6))));
                    }
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        record.AsSpan(10),
                        RemapTypeMapIndex(
                            BinaryPrimitives.ReadUInt16LittleEndian(
                                record.AsSpan(10))));
                    break;

                case 0x0E:
                    if (record[4] == 2)
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(
                            record.AsSpan(6),
                            RemapHomeIndex(
                                BinaryPrimitives.ReadUInt16LittleEndian(
                                    record.AsSpan(6))));
                    }
                    if (record[5] == 2)
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(
                            record.AsSpan(8),
                            RemapHomeIndex(
                                BinaryPrimitives.ReadUInt16LittleEndian(
                                    record.AsSpan(8))));
                    }
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        record.AsSpan(10),
                        RemapTypeMapIndex(
                            BinaryPrimitives.ReadUInt16LittleEndian(
                                record.AsSpan(10))));
                    break;
            }

            records[recordIndex] = record;
        }

        var code = body.Code.ToArray();
        for (var blockIndex = 0; blockIndex < body.BlockCount;
            blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            var codeOffset = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(block));
            var blockCode = body.GetBlockCode(blockIndex);
            for (var position = 0; position < blockCode.Length;)
            {
                var opcode = blockCode[position];
                var mapKind = opcode is >= 0xD0 and <= 0xD3
                    ? 1 : opcode == 0xDE
                        ? blockCode[position + 1] >> 4 : 0;
                if (mapKind is 1 or 2 or 5)
                {
                    var mapOffset = codeOffset + position + 2;
                    var oldIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            code.AsSpan(mapOffset));
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        code.AsSpan(mapOffset),
                        RemapTypeMapIndex(oldIndex));
                }

                if (opcode is 0xD5 or 0xD6 or 0xD7)
                {
                    var homeOffset = codeOffset + position + 2;
                    var oldIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            code.AsSpan(homeOffset));
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        code.AsSpan(homeOffset), RemapHomeIndex(oldIndex));
                }
                else if (opcode == 0x14)
                {
                    var operandOffset = codeOffset + position + 1;
                    var operand = code[operandOffset];
                    var oldIndex = (ushort)(operand & 0x0F);
                    code[operandOffset] = (byte)((operand & 0xF0)
                        | RemapHomeIndex(oldIndex));
                }
                else if (opcode is 0x13 or 0x15 or 0x60)
                {
                    var operandOffset = codeOffset + position + 1;
                    var operand = code[operandOffset];
                    var oldIndex = (ushort)(operand >> 4);
                    code[operandOffset] = (byte)((operand & 0x0F)
                        | (RemapHomeIndex(oldIndex) << 4));
                }
                else if (opcode == 0xB8)
                {
                    var operandOffset = codeOffset + position + 1;
                    code[operandOffset] = checked((byte)RemapHomeIndex(
                        code[operandOffset]));
                }

                position += TrRmRegisterBodyReader.InstructionLength(
                    opcode, schema);
            }
        }

        var blocks = new TrRmRegisterBodyBlock[body.BlockCount];
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            blocks[blockIndex] = new(
                BinaryPrimitives.ReadUInt32LittleEndian(block),
                BinaryPrimitives.ReadUInt32LittleEndian(block[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(block[8..]));
        }

        var rewrittenBody = TrRmRegisterBodyWriter.Encode(homeMap,
            typeMap, body.MethodMap.ToArray(), body.FieldMap.ToArray(),
            body.ConstantMap.ToArray(), blocks, records, code,
            tables.Contracts.Count, schema);
        if (!index.TryGetSection(0x0020, out var bodySection,
                out var bodyCount))
        {
            throw new InvalidOperationException(
                "The A2 TypeMap permutation has no Body section.");
        }

        var bodyTable = new TrRmBodyTableReader(bodySection, bodyCount,
            schema);
        var bodyRows = new ReadOnlyMemory<byte>[bodyCount];
        for (var row = 1; row <= bodyCount; row++)
        {
            bodyRows[row - 1] = row == bodyRow
                ? rewrittenBody : bodyTable.GetRow(row).ToArray();
        }

        var rewrittenBodySection
            = TrRmVariableTableWriter.EncodeBodies(bodyRows);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            sections.Add(kind == 0x0020
                ? new TrRmSection(kind, flags, checked((uint)bodyCount),
                    rewrittenBodySection)
                : new TrRmSection(kind, flags,
                    checked((uint)recordCount), section.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections, schema);
    }

    [TestCase(14, 1, false)]
    [TestCase(15, 1, false)]
    [TestCase(65, 1, false)]
    [TestCase(7, 2, false)]
    [TestCase(12, 2, false)]
    [TestCase(65, 2, true)]
    public async Task StandardA2NestedArrayPathsReuseRegisters(
        int dimensionCount, int elementCount, bool singleWideLeaf)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var moduleIdentity
            = $"a2-nested-register-depth-{dimensionCount}-{elementCount}-"
                + (singleWideLeaf ? "leaf" : "uniform");
        var input = CreateA2DeepNestedAddressInput(moduleIdentity,
            dimensionCount, elementCount, singleWideLeaf, out var source);
        await AssertStandardA2SourceFreeJitAndAotAsync(
            moduleIdentity, input, source, -1,
            $"nested path depth {dimensionCount}").ConfigureAwait(true);
    }

    [Test]
    public async Task StandardA2NestedArrayPathDepth126RoundTripsAndExecutes()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string moduleIdentity = "a2-nested-register-depth-126";
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        var source = CreateA2DeepNestedAddressSource(65, 1, false);
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration
                    = NativeCoreRegistrationTests.CreateRegistration(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var (extendedProgram, _)
            = ExtendA2NestedArrayDepth(compilation.Program!, 126);
        var input = new TrRmStandardArtifactInput(schemaIdentity,
            TrRmExecutableCompiler.EmitStandard(extendedProgram,
                moduleIdentity, schemaIdentity, 1));
        TestContext.WriteLine(
            "A2 126-level Body input extends the 65-level source-bound seed before Standard emission.");
        await AssertStandardA2SourceFreeJitAndAotAsync(moduleIdentity,
            input, source, -1, "semantic depth 126").ConfigureAwait(true);
    }

    [Test]
    public void StandardA2NestedArrayPathRejectsOneBeyondSemanticDepth()
    {
        const string moduleIdentity = "a2-nested-register-depth-128";
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        var source = CreateA2DeepNestedAddressSource(65, 1, false);
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration
                    = NativeCoreRegistrationTests.CreateRegistration(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var (extendedProgram, _)
            = ExtendA2NestedArrayDepth(compilation.Program!, 127);
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(extendedProgram,
                moduleIdentity, schemaIdentity, 1));
    }

    [Test]
    public void NativeFixedArrayContractRejectsPathDepth129()
    {
        var source = CreateA2DeepNestedAddressSource(65, 1, false);
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration
                    = NativeCoreRegistrationTests.CreateRegistration(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var (extendedProgram, nestedPath)
            = ExtendA2NestedArrayDepth(compilation.Program!, 128);
        Assert.That(nestedPath.Projections.Length, Is.EqualTo(128));
        Assert.That(NativeFixedArrayContract.MatchesPath(
            extendedProgram.Functions[extendedProgram.EntryPoint]
                .Variables[nestedPath.Variable].Type,
            nestedPath.Projections, extendedProgram.Structures,
            extendedProgram.NativeFixedArrayTypes, out _, out _), Is.True);
        var beyondDepthProjections
            = nestedPath.Projections.Add(nestedPath.Projections[^1]);
        Assert.That(beyondDepthProjections.Length, Is.EqualTo(129));
        Assert.That(NativeFixedArrayContract.MatchesPath(
            extendedProgram.Functions[extendedProgram.EntryPoint]
                .Variables[nestedPath.Variable].Type,
            beyondDepthProjections, extendedProgram.Structures,
            extendedProgram.NativeFixedArrayTypes, out _, out _), Is.False);
    }

    [TestCase(0, 0, 0, 0)]
    [TestCase(0, 0, 1, -1)]
    [TestCase(0, 1, 0, -1)]
    [TestCase(0, 1, 1, 0)]
    [TestCase(1, 0, 0, -1)]
    [TestCase(1, 0, 1, 0)]
    [TestCase(1, 1, 0, 0)]
    [TestCase(1, 1, 1, -1)]
    public void StandardA2NestedArrayJitReadsInitializedRows(
        int outerIndex, int middleIndex, int innerIndex, int expected)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var moduleIdentity
            = $"a2-nested-array-read-{outerIndex}-{middleIndex}-{innerIndex}";
        var input = CreateA2NestedArrayReadbackInput(moduleIdentity,
            outerIndex, middleIndex, innerIndex, out _);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);

        var result = NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast);
        TestContext.WriteLine(
            $"A2 nested source-free JIT row read: module={moduleIdentity}; expected={expected}; actual={result}; policy=FailFast.");
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(0, 0, 0, 42)]
    [TestCase(0, 0, 1, -1)]
    [TestCase(0, 1, 0, -1)]
    [TestCase(0, 1, 1, 0)]
    [TestCase(1, 0, 0, -1)]
    [TestCase(1, 0, 1, 0)]
    [TestCase(1, 1, 0, 0)]
    [TestCase(1, 1, 1, -1)]
    public void NestedArrayNativeParameterJitReadsInitializedRows(
        int outerIndex, int middleIndex, int innerIndex, int expected)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var source = CreateNativeNestedArrayReadbackSource(outerIndex,
            middleIndex, innerIndex);
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration
                    = NativeCoreRegistrationTests.CreateRegistration(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var actual = NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast);
        TestContext.WriteLine(
            $"Nested NativeParameter source JIT: expected={expected}; actual={actual}; source-sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))}.");
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public async Task NestedArrayNativeParameterAotReadsInitializedRow()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var source = CreateNativeNestedArrayReadbackSource(0, 0, 1);
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration
                    = NativeCoreRegistrationTests.CreateRegistration(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var image = NativeEmitter.EmitWithNativeImports(
            compilation.Program!,
            NativeRuntimePolicy.FailFast);
        Assert.That(image, Is.Not.Empty);
        var imagePath = Path.Combine(Path.GetTempPath(),
            $"terrum-nested-native-parameter-{Guid.NewGuid():N}.exe");
        Process? process = null;
        try
        {
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            process = Process.Start(new ProcessStartInfo(imagePath) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException(
                "The nested NativeParameter executable was not started.");
            var processId = process.Id;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(true);
            }
            catch (TimeoutException exception)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                throw new TimeoutException(
                    $"The nested NativeParameter executable exceeded its 10-second deadline. {await stdout.ConfigureAwait(true)}{Environment.NewLine}{await stderr.ConfigureAwait(true)}",
                    exception);
            }

            var output = await stdout.ConfigureAwait(true);
            var error = await stderr.ConfigureAwait(true);
            TestContext.WriteLine(
                $"Nested NativeParameter source AOT: pid={processId}; expected=-1; actual={process.ExitCode}; source-sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))}; image-sha256={Convert.ToHexString(SHA256.HashData(image))}; deadline=10s.");
            Assert.That(output, Is.Empty);
            Assert.That(error, Is.Empty);
            Assert.That(process.ExitCode, Is.EqualTo(-1));
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                process.Dispose();
            }

            await TestFileCleanup.DeleteWithRetryAsync(imagePath)
                .ConfigureAwait(true);
        }
    }

    [TestCase(2, 1)]
    [TestCase(3, 2)]
    public async Task PrivateRevision102AddressOnlyBackingRoundTripsAndExecutes(
        int dimensionCount, int ancestorCount)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var source = CreatePrivateNestedAddressSource(dimensionCount);
        var libraryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(libraryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                libraryCompilation.Diagnostics));

        var producer = ModuleCompiler.CreateProducer(
            libraryCompilation.Program!, ["Exercise"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            producer.AsSpan(sizeof(int), sizeof(int))), Is.EqualTo(106));
        var addressOffset = FindPrivateStorageAddressWithAncestorCount(
            producer, ancestorCount);
        var excessiveAncestorCount = producer.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            excessiveAncestorCount.AsSpan(addressOffset + 50,
                sizeof(int)), 128);
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(excessiveAncestorCount));
        var mismatchedParent = producer.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            mismatchedParent.AsSpan(addressOffset + 54, sizeof(int)),
            (int)PrimitiveType.Int32);
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(mismatchedParent));
        var badLeafCount = producer.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(
            badLeafCount.AsSpan(addressOffset + 25, sizeof(ulong)), 3);
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(badLeafCount));
        var badByteExtent = producer.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(
            badByteExtent.AsSpan(addressOffset + 41, sizeof(ulong)), 12);
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(badByteExtent));
        Assert.That(producer[addressOffset + 58], Is.EqualTo(2));
        var badAncestorIndexType = producer.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            badAncestorIndexType.AsSpan(addressOffset + 59, sizeof(int)),
            (int)PrimitiveType.Float32);
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(badAncestorIndexType));
        var finalIndexOffset = addressOffset + 54
            + (ancestorCount * (sizeof(int) + 1 + (4 * sizeof(int))));
        Assert.That(producer[finalIndexOffset], Is.EqualTo(2));
        var badFinalIndexType = producer.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            badFinalIndexType.AsSpan(finalIndexOffset + 1, sizeof(int)),
            (int)PrimitiveType.Float32);
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(badFinalIndexType));
        if (ancestorCount == 1)
        {
            var firstIndexOffset = addressOffset + 58;
            var oversizedIndex = CreatePrivateBinaryExpressionTree(16);
            var replacementArtifact = new byte[
                producer.Length - 17 + oversizedIndex.Length];
            producer.AsSpan(0, firstIndexOffset)
                .CopyTo(replacementArtifact);
            oversizedIndex.CopyTo(
                replacementArtifact.AsSpan(firstIndexOffset));
            producer.AsSpan(firstIndexOffset + 17)
                .CopyTo(replacementArtifact.AsSpan(
                    firstIndexOffset + oversizedIndex.Length));
            Assert.That(replacementArtifact.Length,
                Is.LessThan(4 * 1024 * 1024));
            _ = Assert.Throws<ModuleFormatException>(() =>
                ModuleCompiler.CreateReference(replacementArtifact));
        }
        var truncatedPath = producer
            .AsSpan(0, addressOffset + 56).ToArray();
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(truncatedPath));
        var maximumTruncatedPath = producer.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            maximumTruncatedPath.AsSpan(addressOffset + 50, sizeof(int)),
            127);
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(maximumTruncatedPath));
        var initializerOffset
            = FindPrivateNestedArrayInitializerOffset(producer);
        var shortInitializer = producer.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            shortInitializer.AsSpan(initializerOffset + 13, sizeof(int)), 1);
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(shortInitializer));
        var excessiveInitializer = producer.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            excessiveInitializer.AsSpan(initializerOffset + 13, sizeof(int)),
            int.MaxValue);
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(excessiveInitializer));
        var mismatchedChild = producer.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            mismatchedChild.AsSpan(initializerOffset + 18, sizeof(int)),
            (int)PrimitiveType.Int32);
        _ = Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(mismatchedChild));

        var reference = ModuleCompiler.CreateReference(producer);
        const string consumerSource = """
            int32 Marker() {
                mut [uint64; 3] values = [9, 1, 2];
                return int32(values[0]);
            }
            int32 Main() { return Exercise() + Marker() - 9; }
            """;
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse(consumerSource), reference,
            BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));

        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        var reemittedProducer = ModuleCompiler.CreateProducer(
            linked with { EntryPoint = -1 }, ["Exercise"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            reemittedProducer.AsSpan(sizeof(int), sizeof(int))),
            Is.EqualTo(106));
        _ = FindPrivateStorageAddressWithAncestorCount(reemittedProducer,
            ancestorCount);

        var reemittedReference
            = ModuleCompiler.CreateReference(reemittedProducer);
        var reemittedConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse(consumerSource), reemittedReference,
            BoundRuntimePolicy.FailFast);
        Assert.That(reemittedConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reemittedConsumerCompilation.Diagnostics));
        var reemittedConsumer = ModuleCompiler.CreateConsumer(
            reemittedConsumerCompilation.Program!, reemittedProducer);
        var reemittedLinked = ModuleCompiler.Link(reemittedConsumer,
            reemittedProducer);

        Assert.That(NativeJit.Execute(linked,
            NativeRuntimePolicy.FailFast), Is.EqualTo(78));
        Assert.That(NativeJit.Execute(reemittedLinked,
            NativeRuntimePolicy.FailFast), Is.EqualTo(78));
        await AssertPrivateNestedBackingAotAsync(linked,
            $"private-nested-address-{dimensionCount}-first", 78)
            .ConfigureAwait(true);
        await AssertPrivateNestedBackingAotAsync(reemittedLinked,
            $"private-nested-address-{dimensionCount}-reemitted", 78)
            .ConfigureAwait(true);
    }

    [Test]
    public void SourceDepthRejectsMaximumNestedAddressPath()
    {
        var source = CreateDeepPrivateNestedAddressSource(128);
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM1004"), Is.True);
    }

    [Test]
    public void SourceDepthRejectsHighNestedAddressPath()
    {
        var source = CreateDeepPrivateNestedAddressSource(127);
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM1004"), Is.True);
    }

    [Test]
    public async Task PrivateRevision102NestedTypeClosureDepth64Executes()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(CreateDeepPrivateNestedAddressSource(64)),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            compilation.Program!, ["Exercise"]);
        _ = FindPrivateStorageAddressWithAncestorCount(producer, 63,
            expectedElementCount: 1);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Exercise(); }"),
            reference, BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        var (ExitCode, Output, Error) = await RunPrivateModuleJitWorkerAsync(consumer,
            producer).ConfigureAwait(true);
        Assert.That(ExitCode, Is.EqualTo(77));
        Assert.That(Output, Is.Empty);
        Assert.That(Error, Is.Empty);

        await AssertPrivateNestedBackingAotAsync(
            ModuleCompiler.Link(consumer, producer),
            "private-nested-depth-64", 77).ConfigureAwait(true);
    }

    [Test]
    public async Task PrivateRevision102NestedTypeClosureDepth65Executes()
    {
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(CreateDeepPrivateNestedAddressSource(65)),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                compilation.Diagnostics));

        var producer = ModuleCompiler.CreateProducer(
            compilation.Program!, ["Exercise"]);
        _ = FindPrivateStorageAddressWithAncestorCount(producer, 64,
            expectedElementCount: 1);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Exercise(); }"),
            reference, BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        var (ExitCode, Output, Error) = await RunPrivateModuleJitWorkerAsync(consumer,
            producer).ConfigureAwait(true);
        Assert.That(ExitCode, Is.EqualTo(77));
        Assert.That(Output, Is.Empty);
        Assert.That(Error, Is.Empty);

        await AssertPrivateNestedBackingAotAsync(
            ModuleCompiler.Link(consumer, producer),
            "private-nested-depth-65", 77).ConfigureAwait(true);
    }

    private static string CreateDeepPrivateNestedAddressSource(int dimensions)
    {
        var arrayType = "int32";
        var initializer = "77";
        for (var depth = 0; depth < dimensions; depth++)
        {
            arrayType = $"[{arrayType}; 1]";
            initializer = $"[{initializer}]";
        }

        var selectedPath = string.Concat(
            Enumerable.Repeat("[0]", dimensions));
        return $$"""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            public int32 Exercise() {
                mut {{arrayType}} values = {{initializer}};
                mut int32 observed = 0;
                uint64 byteCount = 4;
                unsafe {
                    RtlMoveMemory(&observed,
                        &values{{selectedPath}}, byteCount);
                }
                return observed;
            }
            """;
    }

    private static string CreatePrivateNestedAddressSource(int dimensionCount)
        => CreatePrivateNestedAddressSource(dimensionCount, "Exercise",
            "RtlMoveMemory");

    private static string CreatePrivateNestedAddressSource(
        int dimensionCount, string functionName, string importName,
        string importExportName = "RtlMoveMemory")
    {
        var nextValue = 1;
        var arrayType = "int32";
        for (var dimension = 0; dimension < dimensionCount; dimension++)
        {
            arrayType = $"[{arrayType}; 2]";
        }

        var initializer
            = CreatePrivateNestedArrayInitializer(dimensionCount,
                ref nextValue);
        var selectedPath = string.Concat(
            Enumerable.Repeat("[selectedIndex]", dimensionCount));
        var untouchedPath = string.Concat(
            Enumerable.Repeat("[zeroIndex]", dimensionCount));
        return $$"""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "{{importExportName}}")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void {{importName}}(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            public int32 {{functionName}}() {
                mut {{arrayType}} values = {{initializer}};
                int32 selectedIndex = 1;
                int32 zeroIndex = 0;
                mut int32 replacement = 77;
                mut int32 observed = 0;
                mut int32 untouched = 0;
                uint64 byteCount = 4;
                unsafe {
                    {{importName}}(&values{{selectedPath}},
                        &replacement, byteCount);
                    {{importName}}(&observed,
                        &values{{selectedPath}}, byteCount);
                    {{importName}}(&untouched,
                        &values{{untouchedPath}}, byteCount);
                }
                return observed + untouched;
            }
            """;
    }

    private static string CreatePrivateNestedArrayInitializer(
        int dimensionCount, ref int nextValue)
    {
        if (dimensionCount == 0)
        {
            return nextValue++.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }

        var first = CreatePrivateNestedArrayInitializer(
            dimensionCount - 1, ref nextValue);
        var second = CreatePrivateNestedArrayInitializer(
            dimensionCount - 1, ref nextValue);
        return $"[{first}, {second}]";
    }

    private static int FindPrivateStorageAddressWithAncestorCount(
        byte[] artifact, int ancestorCount,
        ulong expectedElementCount = 2)
    {
        for (var offset = sizeof(int) * 2;
            offset <= artifact.Length - 54; offset++)
        {
            if (artifact[offset] == 35
                && BinaryPrimitives.ReadInt32LittleEndian(
                    artifact.AsSpan(offset + 1, sizeof(int)))
                    == (int)PrimitiveType.NativeStorageAddress
                && BinaryPrimitives.ReadUInt64LittleEndian(
                    artifact.AsSpan(offset + 25, sizeof(ulong)))
                    == expectedElementCount
                && BinaryPrimitives.ReadUInt64LittleEndian(
                    artifact.AsSpan(offset + 33, sizeof(ulong)))
                    == sizeof(int)
                && BinaryPrimitives.ReadUInt64LittleEndian(
                    artifact.AsSpan(offset + 41, sizeof(ulong)))
                    == sizeof(int)
                && BinaryPrimitives.ReadInt32LittleEndian(
                    artifact.AsSpan(offset + 50, sizeof(int)))
                    == ancestorCount)
            {
                return offset;
            }
        }

        Assert.Fail(
            $"The private storage-address path with {ancestorCount} ancestors was not encoded.");
        return -1;
    }

    private static int FindPrivateNestedArrayInitializerOffset(
        byte[] artifact)
    {
        for (var offset = sizeof(int) * 2;
            offset <= artifact.Length - 18; offset++)
        {
            if (artifact[offset] == 43
                && NativeFixedArrayTypes.IsType(
                    (PrimitiveType)BinaryPrimitives.ReadInt32LittleEndian(
                        artifact.AsSpan(offset + 1, sizeof(int))))
                && BinaryPrimitives.ReadInt32LittleEndian(
                    artifact.AsSpan(offset + 13, sizeof(int))) == 2
                && artifact[offset + 17] == 43)
            {
                return offset;
            }
        }

        Assert.Fail("The nested fixed-array initializer was not encoded.");
        return -1;
    }

    private static byte[] CreatePrivateBinaryExpressionTree(int depth)
    {
        var leafCount = 1 << depth;
        var nodeCount = (leafCount * 2) - 1;
        using var stream = new MemoryStream(
            (leafCount * 21) + ((leafCount - 1) * 18));
        using (var writer = new BinaryWriter(stream, Encoding.UTF8,
                   leaveOpen: true))
        {
            WriteNode(writer, depth);
        }

        Assert.That(nodeCount, Is.GreaterThan(100_000));
        return stream.ToArray();

        static void WriteNode(BinaryWriter writer, int remainingDepth)
        {
            writer.Write(remainingDepth == 0 ? (byte)1 : (byte)4);
            writer.Write((int)PrimitiveType.Int32);
            writer.Write(0);
            writer.Write(0);
            if (remainingDepth == 0)
            {
                writer.Write(0L);
                return;
            }

            writer.Write((int)TokenKind.Plus);
            writer.Write((byte)0);
            WriteNode(writer, remainingDepth - 1);
            WriteNode(writer, remainingDepth - 1);
        }
    }

    private static async Task AssertPrivateNestedBackingAotAsync(
        BoundProgram program, string filePrefix, int expectedExitCode)
    {
        var image = NativeEmitter.EmitWithNativeImports(program,
            NativeRuntimePolicy.FailFast);
        Assert.That(image, Is.Not.Empty);
        var imagePath = Path.Combine(Path.GetTempPath(),
            $"terrum-{filePrefix}-{Guid.NewGuid():N}.exe");
        Process? process = null;
        try
        {
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            process = Process.Start(new ProcessStartInfo(imagePath) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException(
                "The private nested-address executable was not started.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(true);
            }
            catch (TimeoutException exception)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                throw new TimeoutException(
                    $"The private nested-address executable exceeded its 10-second deadline. {await stdout.ConfigureAwait(true)}{Environment.NewLine}{await stderr.ConfigureAwait(true)}",
                    exception);
            }

            Assert.That(await stdout.ConfigureAwait(true), Is.Empty);
            Assert.That(await stderr.ConfigureAwait(true), Is.Empty);
            Assert.That(process.ExitCode, Is.EqualTo(expectedExitCode));
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                process.Dispose();
            }

            await TestFileCleanup.DeleteWithRetryAsync(imagePath)
                .ConfigureAwait(true);
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)>
        RunPrivateModuleJitWorkerAsync(byte[] consumer, byte[] producer)
    {
        var consumerPath = Path.Combine(Path.GetTempPath(),
            $"terrum-private-consumer-{Guid.NewGuid():N}.trm");
        var producerPath = Path.Combine(Path.GetTempPath(),
            $"terrum-private-producer-{Guid.NewGuid():N}.trm");
        Process? process = null;
        try
        {
            await File.WriteAllBytesAsync(consumerPath, consumer)
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(producerPath, producer)
                .ConfigureAwait(true);
            var framework = new DirectoryInfo(AppContext.BaseDirectory);
            var configuration = framework.Parent?.Name
                ?? throw new InvalidOperationException(
                    "The test output has no configuration directory.");
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", ".."));
            var cliPath = Path.Combine(root, "artifacts", "bin", "sources",
                "terrumc", configuration, framework.Name, "terrumc.dll");
            if (!File.Exists(cliPath))
            {
                throw new FileNotFoundException(
                    "The Terrum CLI was not built.", cliPath);
            }

            var start = new ProcessStartInfo("dotnet") {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add(cliPath);
            start.ArgumentList.Add("run-module");
            start.ArgumentList.Add(consumerPath);
            start.ArgumentList.Add(producerPath);
            start.ArgumentList.Add("--runtime");
            start.ArgumentList.Add("fail-fast");
            process = Process.Start(start) ?? throw new InvalidOperationException(
                "The private module JIT worker was not started.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(true);
            }
            catch (TimeoutException exception)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                throw new TimeoutException(
                    $"The private module JIT worker exceeded its 10-second deadline. {await stdout.ConfigureAwait(true)}{Environment.NewLine}{await stderr.ConfigureAwait(true)}",
                    exception);
            }

            return (process.ExitCode, await stdout.ConfigureAwait(true),
                await stderr.ConfigureAwait(true));
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                process.Dispose();
            }

            await TestFileCleanup.DeleteWithRetryAsync(consumerPath)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(producerPath)
                .ConfigureAwait(true);
        }
    }

    [TestCase("return values[1][1];")]
    [TestCase("values[1][1] = 77; return values[1][1];")]
    public void PrivateRevision102StillRejectsNestedArrayValueOperations(
        string operations)
    {
        var source = $$"""
            public int32 Exercise() {
                mut [[int32; 2]; 2] values = [[1, 2], [3, 4]];
                {{operations}}
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                compilation.Diagnostics));

        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(compilation.Program!,
                ["Exercise"]));
    }

    [Test]
    public void PrivateRevision102DoesNotTreatWriteAsNestedBackingInitialization()
    {
        const string source = """
            [Terrum.Interop.NativeImport("example.dll", "WriteValue")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            internal extern void WriteValue(write ptr<int32> value);

            public void Exercise() {
                init mut [[int32; 2]; 2] values;
                unsafe { WriteValue(&values[1][1]); }
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True);
    }

    [Test]
    public void MayEscapeNativeParameterIsRejectedAtSourceBoundary()
    {
        const string source = """
            [Terrum.Interop.NativeImport("example.dll", "RetainValue")]
            [Terrum.Interop.NativeParameter(1, Write, MayEscape, 1)]
            internal extern void RetainValue(write ptr<int32> value);

            public void Exercise() {
                mut [[int32; 2]; 2] values = [[1, 2], [3, 4]];
                unsafe { RetainValue(&values[1][1]); }
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True);
    }

    [Test]
    public void NestedNativeParameterExtentCannotCrossTheImmediateRow()
    {
        const string source = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 2)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            public int32 Exercise() {
                mut [[int32; 2]; 2] values = [[1, 2], [3, 4]];
                mut int32 observed = 0;
                uint64 byteCount = 8;
                unsafe {
                    RtlMoveMemory(&observed, &values[1][1], byteCount);
                }
                return observed;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True);
    }

    [Test]
    public async Task PrivateRevision102AddressOnlyBackingSurvivesNativePackageSet()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var arrayCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(CreatePrivateNestedAddressSource(3)),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(arrayCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                arrayCompilation.Diagnostics));
        var arrayProvider = NativeModuleCompiler.CreateProducer(
            arrayCompilation.Program!, ["Exercise"], "NestedAddressArray",
            NativeRuntimePolicy.FailFast);

        var biasCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Bias() { return 1; }
            """), BoundRuntimePolicy.FailFast);
        Assert.That(biasCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                biasCompilation.Diagnostics));
        var biasProvider = NativeModuleCompiler.CreateProducer(
            biasCompilation.Program!, ["Bias"], "NestedAddressBias",
            NativeRuntimePolicy.FailFast);

        var providers = new[] { arrayProvider, biasProvider };
        var reference = NativeModuleCompiler.CreateReferenceSet(providers);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                int32 Main() {
                    mut [uint64; 3] marker = [9, 1, 2];
                    return Exercise() + Bias() + int32(marker[0]) - 9;
                }
                """),
            reference, BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumerSet(
            consumerCompilation.Program!, providers,
            NativeRuntimePolicy.FailFast);

        var result = await NativeModuleCompiler.RunConsumerSetAsync(
            consumer, providers, TimeSpan.FromSeconds(10),
            NativeRuntimePolicy.FailFast).ConfigureAwait(true);
        Assert.That(result, Is.EqualTo(79));
    }

    [Test]
    public async Task PrivateRevision102AddressOnlyBackingSurvivesGraphPackage()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var biasCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Bias() { return 1; }
            """), BoundRuntimePolicy.FailFast);
        Assert.That(biasCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                biasCompilation.Diagnostics));
        var biasProvider = NativeModuleCompiler.CreateProducer(
            biasCompilation.Program!, ["Bias"], "GraphAddressBias",
            NativeRuntimePolicy.FailFast);

        var graphReference
            = NativeModuleCompiler.CreateGraphReference([biasProvider]);
        var graphProviderSource = CreatePrivateNestedAddressSource(3)
            .Replace("return observed + untouched;",
                "return observed + untouched + Bias();",
                StringComparison.Ordinal);
        var graphProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(graphProviderSource), graphReference,
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(graphProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                graphProviderCompilation.Diagnostics));
        var graphProvider = NativeModuleCompiler.CreateGraphProducer(
            graphProviderCompilation.Program!, ["Exercise"],
            "GraphAddressArray", [biasProvider],
            NativeRuntimePolicy.FailFast);

        var dependencies = new[] { graphProvider, biasProvider };
        var rootReference
            = NativeModuleCompiler.CreateGraphReference(dependencies);
        var rootCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                int32 Main() {
                    mut [uint64; 3] marker = [9, 1, 2];
                    return Exercise() + Bias() + int32(marker[0]) - 9;
                }
                """),
            rootReference, BoundRuntimePolicy.FailFast);
        Assert.That(rootCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                rootCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            rootCompilation.Program!, dependencies,
            NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.ValidateGraph(graphConsumer, dependencies);

        var result = await NativeModuleCompiler.RunGraphAsync(graphConsumer,
            dependencies, TimeSpan.FromSeconds(10),
            NativeRuntimePolicy.FailFast).ConfigureAwait(true);
        Assert.That(result, Is.EqualTo(80));
    }

    [Test]
    public async Task PrivateRevision102NestedConstructionFailurePreventsAddressUse()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string source = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            int32 Denominator() { return 0; }

            public int32 Exercise() {
                mut [[int32; 2]; 2] values = [
                    [1, 2],
                    [3, 1 / Denominator()]];
                mut int32 observed = 0;
                uint64 byteCount = 4;
                unsafe {
                    RtlMoveMemory(&observed, &values[1][1], byteCount);
                }
                return observed;
            }
            """;
        var libraryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(libraryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                libraryCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            libraryCompilation.Program!, ["Exercise"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Exercise(); }"),
            reference, BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        const int expectedStatus = unchecked((int)0xE0540002);
        var (ExitCode, Output, Error) = await RunPrivateModuleJitWorkerAsync(consumer,
            producer).ConfigureAwait(true);
        Assert.That(ExitCode, Is.EqualTo(expectedStatus));
        Assert.That(Output, Is.Empty);
        Assert.That(Error, Is.Empty);
        var linked = ModuleCompiler.Link(consumer, producer);
        await AssertPrivateNestedBackingAotAsync(linked,
            "private-nested-address-failure", expectedStatus)
            .ConfigureAwait(true);
    }

    [Test]
    public async Task PrivateRevision102NestedBoundsFailurePreventsNativeCall()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string source = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            public int32 Exercise() {
                mut [[int32; 2]; 2] values = [[1, 2], [3, 4]];
                int32 outerIndex = 2;
                mut int32 observed = 0;
                uint64 byteCount = 4;
                unsafe {
                    RtlMoveMemory(&observed,
                        &values[outerIndex][0], byteCount);
                }
                return observed;
            }
            """;
        var libraryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(libraryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                libraryCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            libraryCompilation.Program!, ["Exercise"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Exercise(); }"),
            reference, BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        var expectedStatus = unchecked((int)0xE0540004);
        var (ExitCode, Output, Error) = await RunPrivateModuleJitWorkerAsync(consumer,
            producer).ConfigureAwait(true);
        Assert.That(ExitCode, Is.EqualTo(expectedStatus));
        Assert.That(Output, Is.Empty);
        Assert.That(Error, Is.Empty);

        var linked = ModuleCompiler.Link(consumer, producer);
        await AssertPrivateNestedBackingAotAsync(linked,
            "private-nested-bounds-failure", expectedStatus)
            .ConfigureAwait(true);
    }

    [Test]
    public async Task PrivateRevision102NestedSuffixFailurePreventsNativeCall()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string source = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 2)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 2)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            public int32 Exercise() {
                mut [[int32; 2]; 2] values = [[1, 2], [3, 4]];
                mut [int32; 2] copied = [0, 0];
                int32 selectedIndex = 1;
                uint64 byteCount = 8;
                unsafe {
                    RtlMoveMemory(&copied[0],
                        &values[0][selectedIndex], byteCount);
                }
                return copied[0];
            }
            """;
        var libraryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(libraryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                libraryCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            libraryCompilation.Program!, ["Exercise"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Exercise(); }"),
            reference, BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        const int expectedStatus = unchecked((int)0xE0540004);
        var (ExitCode, Output, Error) = await RunPrivateModuleJitWorkerAsync(consumer,
            producer).ConfigureAwait(true);
        Assert.That(ExitCode, Is.EqualTo(expectedStatus));
        Assert.That(Output, Is.Empty);
        Assert.That(Error, Is.Empty);

        var linked = ModuleCompiler.Link(consumer, producer);
        await AssertPrivateNestedBackingAotAsync(linked,
            "private-nested-suffix-failure", expectedStatus)
            .ConfigureAwait(true);
    }

    [Test]
    public async Task PrivateRevision102EffectfulNestedIndicesExecuteOnceInPathOrder()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string source = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            public int32 Exercise() {
                mut [[[int32; 2]; 2]; 2] values = [
                    [[1, 2], [3, 4]],
                    [[5, 6], [7, 8]]];
                mut int32 index = 0;
                mut int32 observed = 0;
                uint64 byteCount = 4;
                unsafe {
                    RtlMoveMemory(&observed,
                        &values[index++][index++][0], byteCount);
                }
                return observed + index * 100;
            }
            """;
        var libraryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(libraryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                libraryCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            libraryCompilation.Program!, ["Exercise"]);
        _ = FindPrivateStorageAddressWithAncestorCount(producer, 2);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Exercise(); }"),
            reference, BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        var (ExitCode, Output, Error) = await RunPrivateModuleJitWorkerAsync(consumer,
            producer).ConfigureAwait(true);
        Assert.That(ExitCode, Is.EqualTo(203));
        Assert.That(Output, Is.Empty);
        Assert.That(Error, Is.Empty);

        var linked = ModuleCompiler.Link(consumer, producer);
        await AssertPrivateNestedBackingAotAsync(linked,
            "private-nested-index-order", 203).ConfigureAwait(true);
    }

    [Test]
    public async Task PrivateRevision102ReadOnlyNestedBackingExecutes()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string source = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            public int32 Exercise() {
                [[int32; 2]; 2] values = [[1, 2], [3, 4]];
                mut int32 observed = 0;
                uint64 byteCount = 4;
                unsafe {
                    RtlMoveMemory(&observed, &values[1][1], byteCount);
                }
                return observed;
            }
            """;
        var libraryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(libraryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                libraryCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            libraryCompilation.Program!, ["Exercise"]);
        _ = FindPrivateStorageAddressWithAncestorCount(producer, 1);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Exercise(); }"),
            reference, BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        var (ExitCode, Output, Error) = await RunPrivateModuleJitWorkerAsync(consumer,
            producer).ConfigureAwait(true);
        Assert.That(ExitCode, Is.EqualTo(4));
        Assert.That(Output, Is.Empty);
        Assert.That(Error, Is.Empty);

        var linked = ModuleCompiler.Link(consumer, producer);
        await AssertPrivateNestedBackingAotAsync(linked,
            "private-nested-readonly-backing", 4)
            .ConfigureAwait(true);
    }

    [Test]
    public async Task PrivateRevision102NestedInitializerEffectsRunOnceInOrder()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string source = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetLastError")]
            internal extern uint32 GetLastError();

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "SetLastError")]
            internal extern void SetLastError(uint32 value);

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            int32 NextValue() {
                unsafe {
                    uint32 value = GetLastError() + 1;
                    SetLastError(value);
                    return int32(value);
                }
            }

            public int32 Exercise() {
                mut int32 observed = 0;
                mut uint32 counter = 0;
                uint64 byteCount = 4;
                unsafe {
                    SetLastError(0);
                    mut [[int32; 2]; 2] values = [
                        [NextValue(), NextValue()],
                        [NextValue(), NextValue()]];
                    RtlMoveMemory(&observed, &values[1][1], byteCount);
                    counter = GetLastError();
                }
                return observed + int32(counter) * 100;
            }
            """;
        var libraryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(libraryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                libraryCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            libraryCompilation.Program!, ["Exercise"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Exercise(); }"),
            reference, BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        var (ExitCode, Output, Error) = await RunPrivateModuleJitWorkerAsync(consumer,
            producer).ConfigureAwait(true);
        Assert.That(ExitCode, Is.EqualTo(404),
            $"{Output}{Environment.NewLine}{Error}");
        Assert.That(Output, Is.Empty);
        Assert.That(Error, Is.Empty);

        var linked = ModuleCompiler.Link(consumer, producer);
        await AssertPrivateNestedBackingAotAsync(linked,
            "private-nested-initializer-effects", 404)
            .ConfigureAwait(true);
    }

    [Test]
    public async Task PrivateRevision102LaterArgumentFailureSuppressesNativeCall()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string source = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            public int32 Exercise() {
                mut [[int32; 2]; 2] values = [[1, 2], [3, 4]];
                mut int32 observed = 0;
                unsafe {
                    RtlMoveMemory(&values[1][1], &observed,
                        uint64(1 / 0));
                }
                return observed;
            }
            """;
        var libraryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(libraryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                libraryCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            libraryCompilation.Program!, ["Exercise"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Exercise(); }"),
            reference, BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        const int expectedStatus = unchecked((int)0xE0540002);
        var (ExitCode, Output, Error) = await RunPrivateModuleJitWorkerAsync(consumer,
            producer).ConfigureAwait(true);
        Assert.That(ExitCode, Is.EqualTo(expectedStatus));
        Assert.That(Output, Is.Empty);
        Assert.That(Error, Is.Empty);

        var linked = ModuleCompiler.Link(consumer, producer);
        await AssertPrivateNestedBackingAotAsync(linked,
            "private-nested-later-argument-failure", expectedStatus)
            .ConfigureAwait(true);
    }

    [Test]
    public async Task StandardA2NestedArraySourceFreeJitAndAotExecute()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string moduleIdentity = "a2-nested-array-runtime";
        var input = CreateA2NestedArrayReadbackInput(moduleIdentity,
            0, 0, 1, out var source);
        await AssertStandardA2SourceFreeJitAndAotAsync(
            moduleIdentity, input, source, -1, "nested leaf read")
            .ConfigureAwait(true);
    }

    [Test]
    public async Task StandardA2SourceFreeTransfersFromTwoElementNativeParameterSuffix()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string moduleIdentity = "a2-two-element-native-parameter-suffix";
        var input = CreateA2NestedArrayReadbackInput(moduleIdentity,
            0, 0, 1, out var source, effectfulIndices: true,
            nativeParameterExtent: 2,
            schemaIdentityOverride:
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1");
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        var main = loaded.Functions.Single(static function =>
            function.Name == "Main");
        BoundNativeStorageAddress? selectedAddress = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundNativeStorageAddress {
                    Projections.Length: 3,
                } address)
                {
                    selectedAddress = address;
                }
            }), Is.True);
        Assert.That(selectedAddress, Is.Not.Null);
        Assert.That((selectedAddress!.ElementCount,
            selectedAddress.ElementStride, selectedAddress.ByteExtent),
            Is.EqualTo((4UL, 4UL, 8UL)));
        Assert.That(selectedAddress.Projections.All(static projection =>
            projection.Index is BoundUpdate), Is.True);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, input.SchemaIdentity, 1),
            Is.EqualTo(input.Artifact.ToArray()));
        await AssertStandardA2SourceFreeJitAndAotAsync(moduleIdentity,
            input, source, -1, "two-element nested NativeParameter suffix")
            .ConfigureAwait(true);
    }

    [Test]
    public async Task StandardA2SourceFreeTransfersFromThreeElementNativeParameterSuffix()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string moduleIdentity = "a2-three-element-native-parameter-suffix";
        var input = CreateA2NestedArrayReadbackInput(moduleIdentity,
            0, 0, 1, out var source, effectfulIndices: true,
            counterObservedIndexUpdates: true,
            nativeParameterExtent: 3);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        var main = loaded.Functions.Single(static function =>
            function.Name == "Main");
        BoundNativeStorageAddress? selectedAddress = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundNativeStorageAddress {
                    Projections.Length: 3,
                } address)
                {
                    selectedAddress = address;
                }
            }), Is.True);
        Assert.That(selectedAddress, Is.Not.Null);
        Assert.That((selectedAddress!.ElementCount,
            selectedAddress.ElementStride, selectedAddress.ByteExtent),
            Is.EqualTo((4UL, 4UL, 12UL)));
        Assert.That(selectedAddress.Projections.All(static projection =>
            projection.Index is BoundUpdate), Is.True);
        Assert.That(GetA2IndexUpdateControls(input),
            Is.EqualTo(new byte[] { 0x01, 0x01, 0x01 }));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, input.SchemaIdentity, 1),
            Is.EqualTo(input.Artifact.ToArray()));
        await AssertStandardA2SourceFreeJitAndAotAsync(moduleIdentity,
            input, source, 0xFFFF, "three-element dynamic NativeParameter suffix")
            .ConfigureAwait(true);
    }

    [Test]
    public async Task StandardA2SourceFreeRejectsFourElementNativeParameterBeyondSuffix()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string moduleIdentity = "a2-four-element-native-parameter-suffix-failure";
        var input = CreateA2NestedArrayReadbackInput(moduleIdentity,
            0, 0, 1, out var source, effectfulIndices: true,
            counterObservedIndexUpdates: true,
            nativeParameterExtent: 4);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        var main = loaded.Functions.Single(static function =>
            function.Name == "Main");
        BoundNativeStorageAddress? selectedAddress = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundNativeStorageAddress {
                    Projections.Length: 3,
                } address)
                {
                    selectedAddress = address;
                }
            }), Is.True);
        Assert.That(selectedAddress, Is.Not.Null);
        Assert.That((selectedAddress!.ElementCount,
            selectedAddress.ElementStride, selectedAddress.ByteExtent),
            Is.EqualTo((4UL, 4UL, 16UL)));
        Assert.That(selectedAddress.Projections.All(static projection =>
            projection.Index is BoundUpdate), Is.True);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, input.SchemaIdentity, 1),
            Is.EqualTo(input.Artifact.ToArray()));
        await AssertStandardA2SourceFreeJitAndAotAsync(moduleIdentity,
            input, source, 0xFFFF,
            "four-element NativeParameter exceeds selected suffix",
            runJit: false,
            expectedAotExitCode: unchecked((int)0xE0540004))
            .ConfigureAwait(true);
    }

    [Test]
    public async Task StandardA2NestedAddressIndexesExecuteOnceInSourceOrder()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string moduleIdentity = "a2-nested-array-index-updates";
        var input = CreateA2NestedArrayReadbackInput(moduleIdentity,
            0, 0, 1, out var source, effectfulIndices: true,
            returnSequence: true, effectfulPostfix: true);
        Assert.That(GetA2IndexUpdateControls(input),
            Is.EqualTo(new byte[] { 0x01, 0x01, 0x01 }));
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        var main = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var addresses = new List<BoundNativeStorageAddress>();
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundNativeStorageAddress address
                    && address.Projections.Length == 3)
                {
                    addresses.Add(address);
                }
            }), Is.True);
        Assert.That(addresses, Has.Count.EqualTo(1));
        var selectedAddress = addresses[0];
        Assert.That(main.Variables[selectedAddress.Variable].Type,
            Is.EqualTo(selectedAddress.CarrierType));
        Assert.That(selectedAddress.IsReadonly, Is.True);
        var updates = selectedAddress.Projections
            .Select(static projection => projection.Index)
            .OfType<BoundUpdate>().ToArray();
        Assert.That(updates, Has.Length.EqualTo(3));
        var indexVariable = ((BoundRead)updates[0].Place).Variable;
        Assert.That(updates.Select(static update =>
                ((BoundRead)update.Place).Variable),
            Is.EqualTo([
                indexVariable, indexVariable, indexVariable,
            ]));
        Assert.That(indexVariable, Is.Not.EqualTo(selectedAddress.Variable));
        Assert.That(main.Variables[indexVariable].Type,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(main.Variables[indexVariable].IsMutable, Is.True);
        Assert.That(updates.All(static update => !update.IsPrefix
            && update.OverflowBehavior
                == BoundIntegerOverflowBehavior.Checked), Is.True);
        await AssertStandardA2SourceFreeJitAndAotAsync(
            moduleIdentity, input, source, 3, "nested index count/order")
            .ConfigureAwait(true);

        var prefixInput = CreateA2NestedArrayReadbackInput(
            "a2-nested-array-prefix-index", 0, 0, 1, out _,
            effectfulIndices: true, returnSequence: true);
        Assert.That(GetA2IndexUpdateControls(prefixInput),
            Is.EqualTo(new byte[] { 0x01, 0x01, 0x01 }));
        var prefixLoaded = TrRmExecutableCompiler.LoadStandard(prefixInput,
            "a2-nested-array-prefix-index", []);
        Assert.That(NativeJit.Execute(prefixLoaded,
            NativeRuntimePolicy.FailFast), Is.EqualTo(2));
    }

    [Test]
    public async Task StandardA2CheckedUnsignedAddressIndexUpdatesExecuteOnce()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var incrementInput = CreateA2NestedArrayReadbackInput(
            "a2-uint64-postfix-index", 0, 0, 1, out var incrementSource,
            effectfulIndices: true,
            unsignedIndexUpdates: true,
            effectfulPostfix: true);
        Assert.That(GetA2IndexUpdateControls(incrementInput),
            Is.EqualTo(new byte[] { 0x01, 0x01, 0x01 }));
        await AssertStandardA2SourceFreeJitAndAotAsync(
            "a2-uint64-postfix-index", incrementInput, incrementSource,
            0x00FF_FFFF, "checked uint64 postfix increment")
            .ConfigureAwait(true);

        var decrementInput = CreateA2NestedArrayReadbackInput(
            "a2-uint64-postfix-decrement", 0, 0, 1, out var decrementSource,
            effectfulIndices: true,
            unsignedIndexUpdates: true,
            effectfulPostfix: true, effectfulDecrement: true);
        Assert.That(GetA2IndexUpdateControls(decrementInput),
            Is.EqualTo(new byte[] { 0x01, 0x01, 0x02 }));
        await AssertStandardA2SourceFreeJitAndAotAsync(
            "a2-uint64-postfix-decrement", decrementInput, decrementSource,
            0xFF, "checked uint64 postfix decrement")
            .ConfigureAwait(true);

        foreach (var unsignedIndexType in new[] {
            PrimitiveType.UInt8,
            PrimitiveType.UInt16,
            PrimitiveType.UInt32,
            PrimitiveType.UInt64,
            PrimitiveType.UInt128,
        })
        {
            var moduleIdentity
                = $"a2-{unsignedIndexType.ToString().ToUpperInvariant()}-prefix-index";
            var input = CreateA2NestedArrayReadbackInput(moduleIdentity,
                0, 0, 1, out var source, effectfulIndices: true,
                counterObservedIndexUpdates: true,
                unsignedIndexType: unsignedIndexType);
            Assert.That(GetA2IndexUpdateControls(input),
                Is.EqualTo(new byte[] { 0x01, 0x01, 0x01 }));
            await AssertStandardA2SourceFreeJitAndAotAsync(
                moduleIdentity, input, source, 0xFFFF,
                $"checked {unsignedIndexType} prefix increment",
                runJit: false).ConfigureAwait(true);
        }
    }

    [Test]
    public void SourceWideIndexReadAddressExecutesInJit()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string source = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            int32 Main() {
                mut [[[int32; 4]; 3]; 2] values = [
                    [[0, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0]],
                    [[0, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0]]];
                uint128 index = 0;
                mut int32 observed = 0;
                uint64 byteCount = 0;
                unsafe {
                    var callback = &RtlMoveMemory;
                    callback(&observed, &values[index][index][index],
                        byteCount);
                }
                return observed;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration
                    = NativeCoreRegistrationTests.CreateRegistration(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public async Task StandardA2WrappingAddressIndexUpdatesUseAssignedControls()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var incrementInput = CreateA2NestedArrayReadbackInput(
            "a2-wrapping-postfix-index", 0, 0, 1, out var incrementSource,
            effectfulIndices: true, returnSequence: true,
            effectfulPostfix: true,
            arithmeticProfile: SourceArithmeticProfile.Release);
        Assert.That(GetA2IndexUpdateControls(incrementInput),
            Is.EqualTo(new byte[] { 0x05, 0x05, 0x05 }));
        await AssertStandardA2SourceFreeJitAndAotAsync(
            "a2-wrapping-postfix-index", incrementInput, incrementSource,
            3, "wrapping postfix increment")
            .ConfigureAwait(true);

        var decrementInput = CreateA2NestedArrayReadbackInput(
            "a2-wrapping-postfix-decrement", 0, 0, 1, out var decrementSource,
            effectfulIndices: true, returnSequence: true,
            effectfulPostfix: true, effectfulDecrement: true,
            arithmeticProfile: SourceArithmeticProfile.Release);
        Assert.That(GetA2IndexUpdateControls(decrementInput),
            Is.EqualTo(new byte[] { 0x06, 0x05 }));
        await AssertStandardA2SourceFreeJitAndAotAsync(
            "a2-wrapping-postfix-decrement", decrementInput, decrementSource,
            -1, "wrapping postfix decrement")
            .ConfigureAwait(true);
    }

    private static async Task AssertStandardA2SourceFreeJitAndAotAsync(
        string moduleIdentity,
        TrRmStandardArtifactInput input,
        string source,
        int expectedResult,
        string scenario,
        bool runJit = true,
        byte[]? expectedReemitted = null,
        int? expectedAotExitCode = null)
    {
        var schemaIdentity = input.SchemaIdentity;
        var artifact = input.Artifact.ToArray();
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1);
        Assert.That(reemitted, Is.EqualTo(expectedReemitted ?? artifact));

        var compilerPath = typeof(TrRmExecutableCompiler).Assembly.Location;
        var importPath = Path.Combine(Environment.SystemDirectory,
            "kernel32.dll");
        var compilerHash = Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(compilerPath).ConfigureAwait(true)));
        var importHash = Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(importPath).ConfigureAwait(true)));
        TestContext.WriteLine(
            $"A2 nested source-free runtime input: scenario={scenario}; schema={schemaIdentity}; module={moduleIdentity}; source-sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))}; artifact-sha256={Convert.ToHexString(SHA256.HashData(artifact))}; compiler={compilerPath}; compiler-sha256={compilerHash}; native-library={importPath}; native-library-sha256={importHash}.");
        if (runJit)
        {
            var jitResult = NativeJit.Execute(loaded,
                NativeRuntimePolicy.FailFast);
            TestContext.WriteLine(
                $"A2 nested source-free JIT {scenario}: policy=FailFast; expected={expectedResult}; actual={jitResult}.");
            Assert.That(jitResult, Is.EqualTo(expectedResult));
        }

        var image = NativeModuleCompiler.EmitStandardExecutable(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast);
        Assert.That(image, Is.Not.Empty);
        var imagePath = Path.Combine(Path.GetTempPath(),
            $"terrum-{moduleIdentity}-{Guid.NewGuid():N}.exe");
        Process? process = null;
        try
        {
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            process = Process.Start(new ProcessStartInfo(imagePath) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException(
                "The A2 nested-array executable was not started.");
            var processId = process.Id;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(true);
            }
            catch (TimeoutException exception)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                throw new TimeoutException(
                    $"A2 nested-array executable exceeded its 10-second deadline. {await stdout.ConfigureAwait(true)}{Environment.NewLine}{await stderr.ConfigureAwait(true)}",
                    exception);
            }

            var output = await stdout.ConfigureAwait(true);
            var error = await stderr.ConfigureAwait(true);
            TestContext.WriteLine(
                $"A2 nested source-free AOT {scenario}: policy=FailFast; pid={processId}; expected={expectedAotExitCode ?? expectedResult}; actual={process.ExitCode}; image-sha256={Convert.ToHexString(SHA256.HashData(image))}; deadline=10s.");
            Assert.That(output, Is.Empty);
            Assert.That(error, Is.Empty);
            Assert.That(process.ExitCode,
                Is.EqualTo(expectedAotExitCode ?? expectedResult));
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                process.Dispose();
            }

            await TestFileCleanup.DeleteWithRetryAsync(imagePath)
                .ConfigureAwait(true);
        }
    }

    private static byte[] GetA2IndexUpdateControls(
        TrRmStandardArtifactInput input)
    {
        var schema = input.SchemaIdentity switch
        {
            "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2"
                => TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyDraft,
            "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1"
                => TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft,
            _ => throw new ArgumentException(
                "The index-control reader requires an exact A2 or A2EU artifact.",
                nameof(input)),
        };
        var index = schema switch
        {
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                => TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(
                        input.Artifact.Span),
            _ => TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                    input.Artifact.Span),
        };
        var tables = schema switch
        {
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft
                => TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                        index),
            _ => TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                    index),
        };
        var methodRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                methodRow = row;
                break;
            }
        }

        Assert.That(methodRow, Is.GreaterThan(0));
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(methodRow), tables.Contracts.Count);
        var controls = new List<byte>();
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var code = body.GetBlockCode(blockIndex);
            for (var position = 0; position < code.Length;)
            {
                var opcode = code[position];
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        opcode, body.Schema);
                if (opcode == 0xC0
                    && code[position + 1] is 0x01 or 0x02 or 0x05 or 0x06)
                {
                    controls.Add(code[position + 1]);
                }

                position += instructionLength;
            }
        }

        return [.. controls];
    }

    internal static TrRmStandardArtifactInput
        CreateA2NestedArrayReadbackInput(string moduleIdentity,
            int outerIndex, int middleIndex, int innerIndex, out string source,
            bool effectfulIndices = false, bool returnSequence = false,
            bool effectfulPostfix = false, bool unsignedIndexUpdates = false,
            bool effectfulDecrement = false,
            bool counterObservedIndexUpdates = false,
            PrimitiveType unsignedIndexType = PrimitiveType.UInt64,
            SourceArithmeticProfile arithmeticProfile
                = SourceArithmeticProfile.Development,
            uint nativeParameterExtent = 1,
            string? schemaIdentityOverride = null,
            bool isLibrary = false)
    {
        var schemaIdentity = schemaIdentityOverride
            ?? "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
            SourceArithmeticProfile = arithmeticProfile,
        };
        var outerIndexExpression = outerIndex == 0
            ? "0" : "minusOne / minusOne";
        var middleIndexExpression = middleIndex == 0
            ? "0" : "minusOne / minusOne";
        var innerIndexExpression = innerIndex == 0
            ? "0" : "minusOne / minusOne";
        var effectfulCopyByteCount
            = returnSequence || unsignedIndexUpdates
                || counterObservedIndexUpdates ? 0 : 4;
        var effectfulDestination = returnSequence
            ? "&sequence" : "&observed";
        var effectfulByteCount = counterObservedIndexUpdates
            ? "counter"
            : unsignedIndexUpdates ? "sequence" : "copyByteCount";
        var effectfulSequenceType = counterObservedIndexUpdates
            ? unsignedIndexType switch
            {
                PrimitiveType.UInt8 => "uint8",
                PrimitiveType.UInt16 => "uint16",
                PrimitiveType.UInt32 => "uint32",
                PrimitiveType.UInt64 => "uint64",
                PrimitiveType.UInt128 => "uint128",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(unsignedIndexType)),
            }
            : unsignedIndexUpdates ? "uint64" : "int32";
        var usesSingleDecrementArray
            = effectfulDecrement && !unsignedIndexUpdates
                && !counterObservedIndexUpdates;
        var effectfulArrayType = usesSingleDecrementArray
            ? "[[[int32; 1]; 2]; 3]"
            : "[[[int32; 4]; 3]; 2]";
        var effectfulArrayInitializer = usesSingleDecrementArray
            ? "[[[0], [0]], [[0], [0]], [[0], [0]]]"
            : counterObservedIndexUpdates
                ? "[[[0, 0, 0, 0], [0, -1, 0, 0], [0, 0, 0, 0]], "
                    + "[[0, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0]]]"
                : "[[[0, 0, 0, 0], [0, 0, -1, 0], [0, 0, 0, 0]], "
                    + "[[0, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0]]]";
        var effectfulInitialSequence = counterObservedIndexUpdates
            ? "0"
            : effectfulDecrement ? "0" : effectfulPostfix ? "0" : "-1";
        var effectfulIndexExpression = effectfulDecrement
            ? "sequence--"
            : effectfulPostfix ? "sequence++" : "++sequence";
        var effectfulIndexPath = counterObservedIndexUpdates
            ? "[counter++][++sequence][counter++]"
            : effectfulDecrement
            ? unsignedIndexUpdates
                ? "[sequence++][sequence++][sequence--]"
                : $"[{effectfulIndexExpression}][++oneIndex][zeroIndex]"
            : $"[{effectfulIndexExpression}]"
                + $"[{effectfulIndexExpression}]"
                + $"[{effectfulIndexExpression}]";
        source = effectfulIndices ? $$"""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape,
                {{nativeParameterExtent}})]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            {{(isLibrary ? "public " : "")}}int32 Main() {
                int32 zeroIndex = 0;
                mut {{effectfulSequenceType}} oneIndex = 0;
                {{(counterObservedIndexUpdates ? "mut uint64 counter = 0;" : "")}}
                mut {{effectfulArrayType}} values =
                    {{effectfulArrayInitializer}};
                mut {{effectfulSequenceType}} sequence =
                    {{effectfulInitialSequence}};
                mut int32 observed = 0;
                uint64 copyByteCount = {{effectfulCopyByteCount}};
                unsafe {
                    var callback = &RtlMoveMemory;
                    callback({{effectfulDestination}},
                        &values{{effectfulIndexPath}},
                        {{effectfulByteCount}});
                }
                return {{(returnSequence ? "sequence" : "observed")}};
            }
            """ : $$"""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape,
                {{nativeParameterExtent}})]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            {{(isLibrary ? "public " : "")}}int32 Main() {
                int32 zeroIndex = 0;
                int32 minusOne = -1;
                mut [[[int32; 2]; 2]; 2] values = [
                    [[0, -1], [-1, 0]],
                    [[-1, 0], [0, -1]]];
                int32 outer = {{outerIndexExpression}};
                int32 middle = {{middleIndexExpression}};
                int32 inner = {{innerIndexExpression}};
                mut int32 observed = values[outer][middle][inner];
                uint64 copyByteCount = 0;
                unsafe {
                    var callback = &RtlMoveMemory;
                    callback(&observed,
                        &values[outer][middle][inner], copyByteCount);
                }
                return observed;
            }
            """;
        var sourceTree = SyntaxTree.Parse(source);
        var compilation = isLibrary
            ? Compilation.CreateLibrary(sourceTree, options)
            : Compilation.Create(sourceTree, options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        if (effectfulIndices)
        {
            var function = compilation.Program!.Functions.Single(
                static function => function.Name == "Main");
            var addresses = new List<BoundNativeStorageAddress>();
            _ = BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundNativeStorageAddress address
                        && address.Projections.Length == 3)
                    {
                        if (address.Projections.Any(static projection =>
                                projection.Index is BoundUpdate))
                        {
                            addresses.Add(address);
                        }
                    }
                });
            Assert.That(addresses, Has.Count.EqualTo(1));
            var indexUpdates = addresses[0].Projections
                .Select(static projection => projection.Index)
                .OfType<BoundUpdate>().ToArray();
            Assert.That(indexUpdates, Has.Length.EqualTo(
                usesSingleDecrementArray ? 2 : 3));
            var expectedIndexTypes = counterObservedIndexUpdates
                ? [
                    PrimitiveType.UInt64,
                    unsignedIndexType,
                    PrimitiveType.UInt64,
                ]
                : Enumerable.Repeat(
                    unsignedIndexUpdates ? PrimitiveType.UInt64
                        : PrimitiveType.Int32,
                    indexUpdates.Length).ToArray();
            var expectedUpdates = counterObservedIndexUpdates
                ? [
                    (TokenKind.PlusPlus, false),
                    (TokenKind.PlusPlus, true),
                    (TokenKind.PlusPlus, false),
                ]
                : usesSingleDecrementArray
                ? new[] {
                    (TokenKind.MinusMinus, false),
                    (TokenKind.PlusPlus, true),
                }
                : effectfulDecrement
                    ? [
                        (TokenKind.PlusPlus, false),
                        (TokenKind.PlusPlus, false),
                        (TokenKind.MinusMinus, false),
                    ]
                : Enumerable.Repeat(
                    (TokenKind.PlusPlus, !effectfulPostfix), 3).ToArray();
            var expectedOverflowBehavior
                = arithmeticProfile == SourceArithmeticProfile.Release
                    ? BoundIntegerOverflowBehavior.Wrapping
                    : BoundIntegerOverflowBehavior.Checked;
            Assert.That(indexUpdates.Select(static update =>
                (update.Operator, update.IsPrefix)),
                Is.EqualTo(expectedUpdates));
            Assert.That(indexUpdates.Select(static update => update.Type),
                Is.EqualTo(expectedIndexTypes));
            Assert.That(indexUpdates.All(update =>
                update.OverflowBehavior == expectedOverflowBehavior), Is.True);
        }
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        return new TrRmStandardArtifactInput(schemaIdentity, artifact);
    }

    private static TrRmStandardArtifactInput
        CreateA2DeepNestedAddressInput(string moduleIdentity,
            int dimensionCount, int elementCount, bool singleWideLeaf,
            out string source)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        source = CreateA2DeepNestedAddressSource(dimensionCount,
            elementCount, singleWideLeaf);
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration
                    = NativeCoreRegistrationTests.CreateRegistration(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        return new TrRmStandardArtifactInput(schemaIdentity, artifact);
    }

    private static string CreateA2DeepNestedAddressSource(
        int dimensionCount, int elementCount, bool singleWideLeaf)
    {
        var arrayType = "int32";
        var initializer = "-1";
        for (var depth = 0; depth < dimensionCount; depth++)
        {
            var currentElementCount = singleWideLeaf && depth > 0
                ? 1 : elementCount;
            arrayType = $"[{arrayType}; {currentElementCount}]";
            initializer = $"[{string.Join(", ",
                Enumerable.Repeat(initializer, currentElementCount))}]";
        }

        var selectedPath = string.Concat(
            Enumerable.Repeat("[zeroIndex]", dimensionCount));
        return $$"""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            int32 Main() {
                int32 zeroIndex = 0;
                mut {{arrayType}} values = {{initializer}};
                mut int32 observed = values{{selectedPath}};
                uint64 byteCount = 0;
                unsafe {
                    var callback = &RtlMoveMemory;
                    callback(&observed, &values{{selectedPath}}, byteCount);
                }
                return observed;
            }
            """;
    }

    private static (BoundProgram Program,
        BoundNativeFixedArrayPathElement PathElement)
        ExtendA2NestedArrayDepth(
        BoundProgram program, int targetDepth)
    {
        var functionIndex = program.EntryPoint;
        var function = program.Functions[functionIndex];
        var valuesVariable = -1;
        var zeroIndexVariable = -1;
        for (var variableIndex = 0;
            variableIndex < function.Variables.Length;
            variableIndex++)
        {
            var variable = function.Variables[variableIndex];
            if (variable.Name == "values")
            {
                valuesVariable = variableIndex;
            }
            else if (variable.Name == "zeroIndex")
            {
                zeroIndexVariable = variableIndex;
            }
        }

        if (valuesVariable < 0 || zeroIndexVariable < 0
            || !NativeFixedArrayTypes.IsType(
                function.Variables[valuesVariable].Type))
        {
            throw new InvalidOperationException(
                "The deep A2 source fixture has no array or zero-index local.");
        }

        var originalRootType = function.Variables[valuesVariable].Type;
        var originalArrayDepth = 0;
        var elementType = originalRootType;
        while (NativeFixedArrayTypes.IsType(elementType))
        {
            originalArrayDepth++;
            elementType = program.NativeFixedArrayTypes[
                NativeFixedArrayTypes.Index(elementType)].ElementType;
        }

        if (targetDepth < originalArrayDepth)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(
                targetDepth, originalArrayDepth);
        }

        var fixedArrayTypes = program.NativeFixedArrayTypes.ToList();
        var firstAddedTypeIndex = fixedArrayTypes.Count;
        var rootType = originalRootType;
        for (var depth = originalArrayDepth; depth < targetDepth; depth++)
        {
            if (!NativeFixedArrayContract.TryCreateType(rootType, 1,
                    program.Structures, fixedArrayTypes,
                    out var nestedArrayType, out _))
            {
                throw new InvalidOperationException(
                    "The synthetic A2 array type could not be extended.");
            }

            rootType = nestedArrayType;
        }

        var additionalProjections
            = ImmutableArray.CreateBuilder<BoundNativeFixedArrayProjection>(
                fixedArrayTypes.Count - firstAddedTypeIndex);
        for (var typeIndex = fixedArrayTypes.Count - 1;
            typeIndex >= firstAddedTypeIndex; typeIndex--)
        {
            var arrayType = NativeFixedArrayTypes.Type(typeIndex);
            if (!NativeFixedArrayContract.TryGetLayout(arrayType,
                    program.Structures, fixedArrayTypes, out var layout))
            {
                throw new InvalidOperationException(
                    "The synthetic A2 array layout could not be reconstructed.");
            }

            additionalProjections.Add(
                new BoundNativeFixedArrayProjection(layout,
                    new BoundRead(zeroIndexVariable, PrimitiveType.Int32,
                        default)));
        }

        var addedProjections = additionalProjections.MoveToImmutable();
        var variables = function.Variables.ToBuilder();
        variables[valuesVariable] = variables[valuesVariable] with {
            Type = rootType,
        };
        BoundNativeFixedArrayPathElement? extendedPathElement = null;
        var rewrittenFunction = BoundTreeTraversal.RewriteExpressions(
            function with {
                Variables = variables.ToImmutable(),
            },
            expression =>
            {
                switch (expression)
                {
                    case BoundNativeFixedArrayInitialization initialization
                        when initialization.Type == originalRootType:
                    {
                        var expanded
                            = initialization;
                        for (var typeIndex = firstAddedTypeIndex;
                            typeIndex < fixedArrayTypes.Count; typeIndex++)
                        {
                            var arrayType = NativeFixedArrayTypes.Type(
                                typeIndex);
                            if (!NativeFixedArrayContract.TryGetLayout(
                                    arrayType, program.Structures,
                                    fixedArrayTypes, out var layout))
                            {
                                throw new InvalidOperationException(
                                    "The synthetic A2 initializer layout is invalid.");
                            }

                            expanded = new BoundNativeFixedArrayInitialization(
                                layout, [expanded], initialization.Span);
                        }

                        return expanded;
                    }

                    case BoundNativeFixedArrayPathElement pathElement
                        when pathElement.Variable == valuesVariable:
                    {
                        var expandedPathElement = pathElement with {
                            Projections = [
                                .. addedProjections,
                                .. pathElement.Projections,
                            ],
                        };
                        extendedPathElement = expandedPathElement;
                        return expandedPathElement;
                    }

                    case BoundNativeStorageAddress address
                        when address.Variable == valuesVariable:
                        return address with {
                            CarrierType = rootType,
                            Projections = [
                                .. addedProjections,
                                .. address.Projections,
                            ],
                        };

                    default:
                        return expression;
                }
            });
        var functions = program.Functions.ToBuilder();
        functions[functionIndex] = rewrittenFunction;
        var extendedProgram = program with {
            Functions = functions.ToImmutable(),
            NativeFixedArrayTypes = [.. fixedArrayTypes],
        };
        return (extendedProgram, extendedPathElement
            ?? throw new InvalidOperationException(
                "The deep A2 source fixture has no nested array read."));
    }

    private static string CreateNativeNestedArrayReadbackSource(
        int outerIndex, int middleIndex, int innerIndex)
    {
        return $$"""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            int32 Main() {
                mut [[[int32; 2]; 2]; 2] values = [
                    [[42, -1], [-1, 0]],
                    [[-1, 0], [0, -1]]];
                mut int32 observed = 0;
                int32 outer = {{outerIndex}};
                int32 middle = {{middleIndex}};
                int32 inner = {{innerIndex}};
                uint64 copyByteCount = 4;
                unsafe {
                    RtlMoveMemory(&observed,
                        &values[outer][middle][inner], copyByteCount);
                }
                return observed;
            }
            """;
    }

    [Test]
    public void StandardA2DeclarationProducerPublishesCheckedCopyEvidence()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-copy-declarations";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Scalar {
                public int32 Value;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Pair {
                public Scalar Low;
                public Scalar High;
            }
            """), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));

        Assert.That(tables.TypeDefs.Count, Is.EqualTo(2));
        Assert.That(tables.TypeDefs.HasDeclaredCopy(1), Is.True);
        Assert.That(tables.TypeDefs.HasDeclaredCopy(2), Is.True);
        Assert.That(tables.TypeDefs.GetFlags(1) & 0x20, Is.EqualTo(0x20));
        Assert.That(tables.Features.TryGetFeature(
            TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
            out var feature), Is.True);
        Assert.That(feature.RuleRevision, Is.EqualTo(1));
        Assert.That(tables.FixedArrayLayouts.Count, Is.Zero);
    }

    [Test]
    public void StandardA2SourceTypeExportsUseExplicitKeysOnly()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-explicit-type-exports";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct SourceName {
                public int32 Value;
            }
            """), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var noExports = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, schemaIdentity, 1);
        var noExportsWithEmptyMap = TrRmExecutableCompiler.EmitStandard(
            program, moduleIdentity, schemaIdentity, 1, []);
        var noExportTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(noExports));
        var emptyMapTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(
                        noExportsWithEmptyMap));
        Assert.That(noExportTables.Exports.TryGetDefinitionForKey(1,
            "SourceName", out _), Is.False);
        Assert.That(emptyMapTables.Exports.TryGetDefinitionForKey(1,
            "SourceName", out _), Is.False);

        const string exportKey = "library/types/source-name-v1";
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, schemaIdentity, 1,
            [new TrRmSourceTypeExport("", "SourceName", exportKey)]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "unselected", out _), Is.False);
        Assert.That(tables.Exports.TryGetDefinitionForKey(1, exportKey,
            out var definition), Is.True);
        Assert.That(definition, Is.EqualTo(0x1300_0001u));
        Assert.That(tables.Exports.TryGetExportKeyForDefinition(1,
            definition, out var retainedKey), Is.True);
        Assert.That(retainedKey, Is.EqualTo(exportKey));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        Assert.That(loaded.Structures.Single().Name, Is.EqualTo("SourceName"));
    }

    [Test]
    public void StandardA2SourceTypeExportsRequireNominalClosureMappings()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-type-export-closure";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Element {
                public int32 Value;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Container {
                public [Element; 2] Values;
                public ptr<Element> Pointer;
            }
            """), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(program.NativePointerTypes, Has.Length.EqualTo(1));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
                schemaIdentity, 1,
                [new TrRmSourceTypeExport("", "Container", "container")]));

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, schemaIdentity, 1,
        [
            new TrRmSourceTypeExport("", "Container", "container"),
            new TrRmSourceTypeExport("", "Element", "element"),
        ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1, "container",
            out var containerDefinition), Is.True);
        Assert.That(tables.Exports.TryGetDefinitionForKey(1, "element",
            out var elementDefinition), Is.True);
        Assert.That(containerDefinition, Is.EqualTo(0x1300_0002u));
        Assert.That(elementDefinition, Is.EqualTo(0x1300_0001u));
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        Assert.That(loaded.Structures, Has.Length.EqualTo(2));
        Assert.That(NativeFixedArrayContract.MatchesProgram(loaded), Is.True);
        Assert.That(loaded.NativePointerTypes, Is.EqualTo(program.NativePointerTypes));
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            moduleIdentity, []);
        Assert.That(reloaded.NativePointerTypes,
            Is.EqualTo(loaded.NativePointerTypes));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                moduleIdentity, [], ["unused-key"]));
        var stalePointerProgram = loaded with {
            NativePointerTypes = loaded.NativePointerTypes.SetItem(0,
                loaded.NativePointerTypes[0] with {
                    PointeeType = PrimitiveType.Int32,
                }),
        };
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(stalePointerProgram,
                moduleIdentity, schemaIdentity, 1));

        var pointerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Element {
                    public int32 Value;
                }
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct PointerContainer {
                    public ptr<Element> Value;
                }
                """), new CompilationOptions {
                    LayoutCoreRegistration =
                        LayoutCoreRegistrationTestData.Create(),
                });
        Assert.That(pointerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, pointerCompilation.Diagnostics));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(pointerCompilation.Program!,
                "a2-pointer-export-closure", schemaIdentity, 1,
                [new TrRmSourceTypeExport("", "PointerContainer",
                    "pointer-container")]));
        var pointerArtifact = TrRmExecutableCompiler.EmitStandard(
            pointerCompilation.Program!, "a2-pointer-export-closure",
            schemaIdentity, 1,
            [
                new TrRmSourceTypeExport("", "PointerContainer",
                    "pointer-container"),
                new TrRmSourceTypeExport("", "Element", "element"),
            ]);
        var pointerReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, pointerArtifact),
                "a2-pointer-export-closure", [], ["pointer-container"]);
        Assert.That(pointerReference.Structures.Any(static structure
            => structure.Name == "Element"), Is.True);
    }

    [Test]
    public void StandardA2TypeReferenceSelectsRootsAndRetainsLocalOrigins()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-type-reference-provider";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Element {
                public int32 Value;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Container {
                public [Element; 2] Values;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Unused {
                public int32 Value;
            }
            """), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1,
        [
            new TrRmSourceTypeExport("", "Container", "container-key"),
            new TrRmSourceTypeExport("", "Element", "element-key"),
        ]);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, [], ["container-key"]);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            artifact);
        var loadedProvider = TrRmExecutableCompiler.LoadStandard(
            providerInput, moduleIdentity, []);
        var reemittedProvider = TrRmExecutableCompiler.EmitStandard(
            loadedProvider, moduleIdentity, schemaIdentity, 1);
        var reemittedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    reemittedProvider),
                moduleIdentity, [], ["container-key"]);
        Assert.That(reemittedReference.Structures.Any(static structure
            => structure.Name == "Element"), Is.True);
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(loadedProvider,
                moduleIdentity, schemaIdentity, 1,
                [new TrRmSourceTypeExport("", "Container",
                    "replacement-container-key")]));

        Assert.That(reference.Structures, Has.Length.EqualTo(2));
        Assert.That(reference.Structures.Any(static structure
            => structure.Name == "Unused"), Is.False);
        var container = reference.Structures.Single(static structure
            => structure.Name == "Container");
        var containerOrigin = container.StandardTypeOrigin;
        Assert.That(containerOrigin, Is.Not.Null);
        Assert.That(containerOrigin!.IsLookupRoot, Is.True);
        Assert.That(containerOrigin.Provider.SchemaIdentity,
            Is.EqualTo(schemaIdentity));
        Assert.That(containerOrigin.Provider.ModuleIdentity,
            Is.EqualTo(moduleIdentity));
        Assert.That(containerOrigin.Provider.GoverningRuleEdition, Is.EqualTo(1));
        Assert.That(containerOrigin.Provider.TargetRequirement, Is.EqualTo(1));
        Assert.That(containerOrigin.Provider.RuntimePolicy, Is.Zero);
        Assert.That(containerOrigin.Provider.ArtifactSnapshot.AsSpan()
            .SequenceEqual(artifact), Is.True);
        Assert.That(containerOrigin.DefinitionId, Is.EqualTo(0x1300_0002u));
        Assert.That(containerOrigin.ExportKeys, Has.Length.EqualTo(1));
        Assert.That(containerOrigin.ExportKeys[0], Is.EqualTo("container-key"));
        var element = reference.Structures.Single(static structure
            => structure.Name == "Element");
        Assert.That(element.StandardTypeOrigin!.IsLookupRoot, Is.False);
        Assert.That(element.StandardTypeOrigin.ExportKeys, Has.Length.EqualTo(1));
        Assert.That(element.StandardTypeOrigin.ExportKeys[0],
            Is.EqualTo("element-key"));
        Assert.That(reference.NativeFixedArrayTypes, Has.Length.EqualTo(1));
        var multipleRoots = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, moduleIdentity, [],
            ["container-key", "element-key"]);
        Assert.That(multipleRoots.Structures, Has.Length.EqualTo(2));
        Assert.That(multipleRoots.Structures.All(static structure
            => structure.StandardTypeOrigin!.IsLookupRoot), Is.True);
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(providerInput,
                moduleIdentity, [], []));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(providerInput,
                moduleIdentity, [], ["missing-key"]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(providerInput,
                moduleIdentity, [providerInput], ["container-key"]));

        var consumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Consumer {
                public Container Value;
                public [Container; 2] Values;
            }
            """), reference, new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        Assert.That(consumer.Program!.Structures[1].StandardTypeOrigin
            ?.IsLookupRoot, Is.True);
        Assert.That(consumer.Program.NativeFixedArrayTypes, Has.Length.EqualTo(2));

        var unexportedConsumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumer.Program!, "a2-type-reference-consumer", schemaIdentity, 1);
        var loadedUnexportedConsumer = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                unexportedConsumerArtifact),
            "a2-type-reference-consumer", [providerInput]);
        Assert.That(loadedUnexportedConsumer.Structures,
            Has.Length.EqualTo(3));
        Assert.That(loadedUnexportedConsumer.NativeFixedArrayTypes,
            Has.Length.EqualTo(2));

        var staleArrayProgram = consumer.Program! with {
            NativeFixedArrayTypes = consumer.Program.NativeFixedArrayTypes
                .SetItem(0, consumer.Program.NativeFixedArrayTypes[0] with {
                    ElementType = PrimitiveType.Int32,
                }),
        };
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(staleArrayProgram,
                "a2-type-reference-consumer", schemaIdentity, 1,
                [new TrRmSourceTypeExport("", "Consumer", "ConsumerKey")]));

        var closureLookup = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Consumer {
                    public Element Value;
                }
                """), reference, new CompilationOptions {
                    LayoutCoreRegistration =
                        LayoutCoreRegistrationTestData.Create(),
                });
        Assert.That(closureLookup.HasErrors, Is.True);

        var functionConsumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Read() {
                return 0;
            }
            """), reference);
        Assert.That(functionConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, functionConsumer.Diagnostics));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(functionConsumer.Program!,
                ["Read"]));
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.CreateProducer(functionConsumer.Program!,
                ["Read"], "A2TypeReferenceConsumer",
                NativeRuntimePolicy.FailFast));
    }

    [Test]
    public void StandardA2SourceTypeExportsRejectHandBuiltAndDuplicateMappings()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-invalid-type-exports";
        var handBuilt = new BoundProgram([], -1)
        {
            Structures = [
                new BoundStruct("Manual", [
                    new BoundStructField("Value", PrimitiveType.Int32, 0, 4),
                ], 4)
                {
                    IsSequential = true,
                    BlittableTargetRequirement = 1,
                    Packing = 16,
                    Alignment = 4,
                    RequiredAlignment = 1,
                },
            ],
        };
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(handBuilt,
                moduleIdentity, schemaIdentity, 1,
                [new TrRmSourceTypeExport("", "Manual", "manual")]));

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct First {
                public int32 Value;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Second {
                public int32 Value;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct Internal {
                public int32 Value;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public unique struct Unique {
                public int32 Value;
            }
            """), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var changedProgram = program with {
            Structures = program.Structures.SetItem(0,
                program.Structures[0] with { Name = "Changed" }),
        };

        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                moduleIdentity, schemaIdentity, 1,
            [
                new TrRmSourceTypeExport("", "First", "duplicate"),
                new TrRmSourceTypeExport("", "Second", "duplicate"),
            ]));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                moduleIdentity, schemaIdentity, 1,
            [
                new TrRmSourceTypeExport("", "First", "first"),
                new TrRmSourceTypeExport("", "First", "second"),
            ]));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(changedProgram,
                moduleIdentity, schemaIdentity, 1,
                [new TrRmSourceTypeExport("", "First", "changed")]));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                moduleIdentity, schemaIdentity, 1,
                [new TrRmSourceTypeExport("", "Internal", "internal")]));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                moduleIdentity, schemaIdentity, 1,
                [new TrRmSourceTypeExport("", "Unique", "unique")]));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                moduleIdentity, schemaIdentity, 1,
                [new TrRmSourceTypeExport("", "First", "")]));
    }

    [Test]
    public void StandardA2DeclarationProducerDoesNotClaimCopyForUniqueStructs()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-unique-declarations";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public unique struct UniquePayload {
                public int32 Value;
            }
            """), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.Structures.Single().IsUnique,
            Is.True);

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));

        Assert.That(tables.TypeDefs.Count, Is.EqualTo(1));
        Assert.That(tables.TypeDefs.HasDeclaredCopy(1), Is.False);
        Assert.That(tables.TypeDefs.GetFlags(1) & 0x20, Is.Zero);
        Assert.That(tables.Features.TryGetFeature(
            TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
            out _), Is.False);
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                moduleIdentity, []));
    }

    [Test]
    public void StandardA2DeclarationProducerEmitsNestedArrayTypeMetadata()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-array-field-declarations";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct ArrayHolder {
                public [[int32; 2]; 3] Values;
            }
            """), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.NativeFixedArrayTypes,
            Has.Length.EqualTo(2));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));

        Assert.That(tables.FixedArrayLayouts.Count, Is.EqualTo(2));
        var innerLayout = tables.FixedArrayLayouts.GetRow(1);
        var outerLayout = tables.FixedArrayLayouts.GetRow(2);
        Assert.That(tables.TypeSpecs.TryGetFixedInlineArray(
            (uint)innerLayout.ArrayTypeSpecRow, out _, out var innerCount),
            Is.True);
        Assert.That(innerCount, Is.EqualTo(2UL));
        Assert.That(tables.TypeSpecs.TryGetFixedInlineArray(
            (uint)outerLayout.ArrayTypeSpecRow,
            out var outerElementType, out var outerCount), Is.True);
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(outerElementType).Form,
            Is.EqualTo((byte)0x11));
        Assert.That(outerCount, Is.EqualTo(3UL));
        Assert.That(innerLayout.ByteExtent, Is.EqualTo(8UL));
        Assert.That(outerLayout.ByteExtent, Is.EqualTo(24UL));
        Assert.That(tables.TypeDefs.HasDeclaredCopy(1), Is.True);
        Assert.That(tables.Features.TryGetFeature(
            TrRmFeatureTableReader.FixedInlineArrayStorageFeatureId,
            out var arrayFeature), Is.True);
        Assert.That(arrayFeature.RuleRevision, Is.EqualTo(1));
    }

    [Test]
    public void StandardA2TypeLibraryLoaderReconstructsCopyArrayFields()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-array-field-type-library";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Scalar {
                public int32 Value;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct ArrayHolder {
                public Scalar Payload;
                public [[int32; 2]; 3] Values;
            }
            """), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);

        Assert.That(loaded.Functions, Is.Empty);
        Assert.That(loaded.EntryPoint, Is.EqualTo(-1));
        Assert.That(loaded.Structures, Has.Length.EqualTo(2));
        Assert.That(loaded.Structures[0].Name, Is.EqualTo("Scalar"));
        Assert.That(loaded.Structures[1].Name, Is.EqualTo("ArrayHolder"));
        Assert.That(loaded.Structures[1].Fields, Has.Length.EqualTo(2));
        Assert.That(loaded.Structures[1].Fields[0].Type,
            Is.EqualTo((PrimitiveType)100));
        Assert.That(loaded.Structures[1].Fields[1].Type,
            Is.EqualTo(NativeFixedArrayTypes.Type(1)));
        Assert.That(loaded.Structures[1].Size, Is.EqualTo(28));
        Assert.That(loaded.NativeFixedArrayTypes, Has.Length.EqualTo(2));
        Assert.That(loaded.NativeFixedArrayTypes[0].ElementCount,
            Is.EqualTo(2UL));
        Assert.That(loaded.NativeFixedArrayTypes[1].ElementCount,
            Is.EqualTo(3UL));
        Assert.That(NativeFixedArrayContract.MatchesProgram(loaded), Is.True);
    }

    [Test]
    public void StandardA2TypeLibraryLoaderReconstructsLocalNominalArrayElements()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-nominal-array-elements";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Element {
                public [uint32; 2] Values;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public [Element; 3] Items;
            }
            """), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);

        Assert.That(loaded.Structures, Has.Length.EqualTo(2));
        Assert.That(loaded.Structures[0].Fields[0].Type,
            Is.EqualTo(NativeFixedArrayTypes.Type(0)));
        Assert.That(loaded.Structures[0].Size, Is.EqualTo(8));
        Assert.That(loaded.Structures[1].Fields[0].Type,
            Is.EqualTo(NativeFixedArrayTypes.Type(1)));
        Assert.That(loaded.Structures[1].Size, Is.EqualTo(24));
        Assert.That(loaded.NativeFixedArrayTypes, Has.Length.EqualTo(2));
        Assert.That(loaded.NativeFixedArrayTypes[0].ElementType,
            Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(loaded.NativeFixedArrayTypes[0].ElementCount,
            Is.EqualTo(2UL));
        Assert.That(loaded.NativeFixedArrayTypes[1].ElementType,
            Is.EqualTo((PrimitiveType)100));
        Assert.That(loaded.NativeFixedArrayTypes[1].ElementCount,
            Is.EqualTo(3UL));
        Assert.That(loaded.NativeFixedArrayTypes[1].ElementStride,
            Is.EqualTo(8UL));
        Assert.That(loaded.NativeFixedArrayTypes[1].ByteExtent,
            Is.EqualTo(24UL));
        Assert.That(NativeFixedArrayContract.MatchesProgram(loaded), Is.True);
    }

    [Test]
    public void StandardA2LoaderReconstructsNestedAndWideArrayTypes()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "native-address-array-types";
        var artifact = CreateStorageAddressArtifact(moduleIdentity,
            schemaIdentity);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        var firstArrayTypeSpecRow = tables.TypeSpecs.Count + 1;
        var functionPointerTypeSpecRow = 0;
        for (var row = 1; row <= tables.TypeSpecs.Count; row++)
        {
            if (functionPointerTypeSpecRow == 0
                && tables.NativeFunctionPointerAnchors
                    .HasAnchorForTypeSpec(row))
            {
                functionPointerTypeSpecRow = row;
            }
        }
        Assert.That(functionPointerTypeSpecRow, Is.GreaterThan(0));
        var wideCount = 1UL << 32;
        var arrayTypes = new (uint ElementTypeSpecRef, ulong ElementCount,
            ulong ElementStride, ulong ByteExtent, ulong Alignment)[]
        {
            (0xF200_0008, 2, 4, 8, 4),
            (0xF200_0008, wideCount, 4, wideCount * 4, 4),
            ((uint)firstArrayTypeSpecRow, 3, 8, 24, 4),
            (0xF200_0000, ulong.MaxValue, 0, 0, 1),
            (0xF300_8C08, 2, 8, 16, 8),
            ((uint)functionPointerTypeSpecRow, 2, 8, 16, 8),
        };
        artifact = AddA2FixedArrayTypes(artifact, arrayTypes);

        var input = new TrRmStandardArtifactInput(schemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        Assert.That(loaded.NativeFixedArrayTypes, Has.Length.EqualTo(6));
        Assert.That(loaded.NativeFixedArrayTypes[0].ElementType,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(loaded.NativeFixedArrayTypes[0].ElementCount,
            Is.EqualTo(2UL));
        Assert.That(loaded.NativeFixedArrayTypes[1].ElementCount,
            Is.EqualTo(wideCount));
        Assert.That(loaded.NativeFixedArrayTypes[1].ByteExtent,
            Is.EqualTo(wideCount * 4));
        Assert.That(loaded.NativeFixedArrayTypes[2].ElementType,
            Is.EqualTo(NativeFixedArrayTypes.Type(0)));
        Assert.That(loaded.NativeFixedArrayTypes[3].ElementType,
            Is.EqualTo(PrimitiveType.Void));
        Assert.That(loaded.NativeFixedArrayTypes[3].ElementCount,
            Is.EqualTo(ulong.MaxValue));
        Assert.That(loaded.NativeFixedArrayTypes[3].ByteExtent, Is.Zero);
        Assert.That(NativePointerTypes.IsType(
            loaded.NativeFixedArrayTypes[4].ElementType), Is.True);
        Assert.That(NativeFunctionPointerTypes.IsType(
            loaded.NativeFixedArrayTypes[5].ElementType), Is.True);
        Assert.That(NativeFixedArrayContract.MatchesProgram(loaded), Is.True);
    }

    [Test]
    public void StandardA2LoaderRejectsInconsistentFixedArrayGeometry()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "native-address-invalid-array-types";
        var artifact = CreateStorageAddressArtifact(moduleIdentity,
            schemaIdentity);
        artifact = AddA2FixedArrayTypes(artifact,
        [
            (0xF200_0008, 4UL, 4UL, 16UL, 4UL),
        ], corruptFirstExtent: true);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                moduleIdentity, []));
    }

    [Test]
    public void EmitsInitializedLocalStorageAddressForNativeParameter()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-addresses-1";
        const string moduleIdentity = "native-storage-address";
        var artifact = CreateStorageAddressArtifact(moduleIdentity);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageAddressDraft(artifact));
        var caller = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                caller = row;
                break;
            }
        }
        Assert.That(caller, Is.GreaterThan(0));
        var module = tables.Methods.GetOwnerModule(caller);
        Assert.That(tables.Features.Count, Is.EqualTo(2));
        var featureIds = new uint[tables.Features.Count];
        for (var index = 0; index < featureIds.Length; index++)
        {
            featureIds[index] = tables.Features.GetFeature(index + 1).FeatureId;
        }
        Assert.That(featureIds, Is.EquivalentTo(new uint[] { 4, 16 }));
        foreach (var featureId in new uint[] { 1, 2, 3, 4, 16 })
        {
            Assert.That(TrRmFeatureManifestValidator.Requires(
                tables.Features, tables.Modules, module, featureId,
                tables.Schema), Is.True);
        }
        Assert.That(TrRmFeatureManifestValidator.Requires(
            tables.Features, tables.Modules, module,
            TrRmFeatureTableReader.TypedFrozenValuesFeatureId,
            tables.Schema), Is.False);
        tables.ValidateRegisterBodyMapsForMethod(caller);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);

        Assert.That(body.MapMask, Is.EqualTo((byte)0x07));
        Assert.That(body.HomeMap.Length, Is.EqualTo(56));
        AssertHomeFirstUseTypeMap(body);

        var targetHomeTypeMapIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(
                body.HomeMap.Slice((3 * 8) + 2, sizeof(ushort)));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetRecord(1)[10..]), Is.EqualTo(targetHomeTypeMapIndex));
        Assert.That(body.GetRecord(2)[0], Is.EqualTo(0x0E));
        Assert.That(body.GetRecord(2)[4], Is.EqualTo(2));
        Assert.That(body.GetRecord(2)[5], Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetRecord(2)[10..]),
            Is.EqualTo(BinaryPrimitives.ReadUInt16LittleEndian(
                body.HomeMap.Slice((4 * 8) + 2, sizeof(ushort)))));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetRecord(2)[8..]), Is.EqualTo(4));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            body.GetRecord(2)[12..]), Is.EqualTo(1));
        Assert.That(body.GetRecord(3)[0], Is.EqualTo(0x0E));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetRecord(3)[10..]),
            Is.EqualTo(BinaryPrimitives.ReadUInt16LittleEndian(
                body.HomeMap.Slice((5 * 8) + 2, sizeof(ushort)))));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetRecord(3)[8..]), Is.EqualTo(5));
        Assert.That(body.GetRecord(4)[0], Is.EqualTo(0x0E));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetRecord(4)[10..]),
            Is.EqualTo(BinaryPrimitives.ReadUInt16LittleEndian(
                body.HomeMap.Slice((6 * 8) + 2, sizeof(ushort)))));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetRecord(4)[8..]), Is.EqualTo(6));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0x14, 0xE0, 0x14, 0xE1, 0x14, 0xF2,
                0xE9, 0, 1, 0,
                0xEC, 0, 2, 0, 0xEC, 0, 3, 0, 0xEC, 0, 4, 0,
                0xE0, 0, 5, 0,
                0xB8, 2,
            }));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var storageAddresses = new List<BoundNativeStorageAddress>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundNativeStorageAddress storageAddress)
                {
                    storageAddresses.Add(storageAddress);
                }
            }), Is.True);
        Assert.That(storageAddresses, Has.Count.EqualTo(3));
        Assert.That(storageAddresses.Select(address =>
            loadedMain.Variables[address.Variable].Type),
            Is.EquivalentTo(new[] {
                PrimitiveType.UInt32, PrimitiveType.UInt32, PrimitiveType.Int32,
            }));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(
                    "trrm/1-draft-foreign-invocation-contracts-1", artifact),
                moduleIdentity, Array.Empty<TrRmStandardArtifactInput>()));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast),
            Is.EqualTo(1));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
    }

    [TestCase("missing-write-authority")]
    [TestCase("oversized-scalar-extent")]
    public void StorageAddressConsumerRejectsInvalidLocalAuthorityOrExtent(
        string defect)
    {
        var artifact = CreateStorageAddressArtifact("invalid-storage-address");
        var malformed = RewriteStorageAddressArtifact(artifact, defect);

        void Validate()
        {
            var index = TrRmArtifactIndex
                .ForForeignInvocationStorageAddressDraft(malformed);
            var tables = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(
                    index);
            var caller = 0;
            for (var row = 1; row <= tables.Methods.Count; row++)
            {
                if (tables.Methods.GetName(row) == "Main")
                {
                    caller = row;
                    break;
                }
            }
            Assert.That(caller, Is.GreaterThan(0));
            tables.ValidateRegisterBodyMapsForMethod(caller);
        }

        _ = Assert.Throws<InvalidDataException>(Validate);
    }

    [Test]
    public void EmitsParameterizedScalarNativeAddressSignature()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 Observe(uint32 value);

            internal void Form() {
                unsafe { _ = &Observe; }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-parameterized");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var target = 0;
        var caller = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            target = name == "Observe" ? row : target;
            caller = name == "Form" ? row : caller;
        }

        Assert.That(target, Is.GreaterThan(0));
        Assert.That(caller, Is.GreaterThan(0));
        var signature = tables.Methods.GetSignature(target);
        var (parameterStart, parameterCount)
            = tables.Signatures.GetParamRange(signature);
        var resultType = tables.Signatures.GetResultTypeSpec(signature);

        Assert.That(tables.Signatures.ParamCount, Is.EqualTo(1));
        Assert.That(parameterCount, Is.EqualTo(1));
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
            resultType, tables.Parameters.GetTypeSpec(parameterStart)), Is.True);
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(resultType).Form,
            Is.EqualTo(0x01));
        Assert.That(tables.Methods.GetSignature(caller),
            Is.Not.EqualTo(signature));
        tables.ValidateRegisterBodyMapsForMethod(caller);
    }

    [Test]
    public void EmitsNativeParameterOnNativeImportAndSignatureAnchor()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 4294967295)]
            internal extern void Observe(ptr<uint32> value);

            internal void Form() {
                unsafe { _ = &Observe; }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-parameter");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var target = 0;
        var caller = 0;
        var anchor = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            target = name == "Observe" ? row : target;
            caller = name == "Form" ? row : caller;
            anchor = name.StartsWith("__trrm_fnptr_anchor_",
                StringComparison.Ordinal) ? row : anchor;
        }

        Assert.That(target, Is.GreaterThan(0));
        Assert.That(caller, Is.GreaterThan(0));
        Assert.That(anchor, Is.GreaterThan(0));
        var signature = tables.Methods.GetSignature(target);
        var (parameterStart, parameterCount)
            = tables.Signatures.GetParamRange(signature);
        Assert.That(parameterCount, Is.EqualTo(1));
        var parameterTypeSpec = tables.Parameters.GetTypeSpec(parameterStart);
        Assert.That(TrRmNativeImportTypeContract.TryGetNativeParameterPointee(
            tables.TypeSpecs, parameterTypeSpec, 1, tables.Layouts,
            out var pointeeType), Is.True);
        Assert.That(pointeeType, Is.EqualTo(PrimitiveType.UInt32));

        var targetContracts = tables.Contracts.GetNativeParameters(target);
        var anchorContracts = tables.Contracts.GetNativeParameters(anchor);
        Assert.That(targetContracts.Length, Is.EqualTo(1));
        Assert.That(anchorContracts.Length, Is.EqualTo(1));
        Assert.That(targetContracts[0], Is.EqualTo(anchorContracts[0]));
        Assert.That(targetContracts[0].ParameterOrdinal, Is.Zero);
        Assert.That(targetContracts[0].Access, Is.EqualTo(1));
        Assert.That(targetContracts[0].Retention, Is.EqualTo(1));
        Assert.That(targetContracts[0].ExtentKind, Is.EqualTo(1));
        Assert.That(targetContracts[0].Extent, Is.EqualTo(uint.MaxValue));
        Assert.That(TrRmFeatureManifestValidator.Requires(tables.Features,
            tables.Modules, 1,
            TrRmFeatureTableReader.ForeignInvocationContractsCandidateFeatureId,
            TrRmArtifactSchema.ForeignInvocationDraft), Is.True);
        Assert.That(tables.StandardNativeImports.HasImportForMethod(target),
            Is.True);
        tables.ValidateRegisterBodyMapsForMethod(caller);
    }

    [Test]
    public void EmitsWritableNativeParameterPointerQualifier()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 0)]
            internal extern void Observe(write ptr<uint32> value);

            internal void Form() {
                unsafe { _ = &Observe; }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-writable-parameter");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var target = 0;
        var anchor = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            target = name == "Observe" ? row : target;
            anchor = name.StartsWith("__trrm_fnptr_anchor_",
                StringComparison.Ordinal) ? row : anchor;
        }

        Assert.That(target, Is.GreaterThan(0));
        Assert.That(anchor, Is.GreaterThan(0));
        var signature = tables.Methods.GetSignature(target);
        var (parameterStart, parameterCount)
            = tables.Signatures.GetParamRange(signature);
        Assert.That(parameterCount, Is.EqualTo(1));
        var typeSpecRef = tables.Parameters.GetTypeSpec(parameterStart);
        var typeSpec = tables.TypeSpecs.GetTypeSpecBytes(
            typeSpecRef);
        Assert.That(typeSpec.Length, Is.EqualTo(4));
        Assert.That(typeSpec[1], Is.EqualTo(1));
        Assert.That(typeSpec[2], Is.EqualTo(0x8C));
        Assert.That(TrRmNativeImportTypeContract.TryGetNativeParameterPointee(
            tables.TypeSpecs, typeSpecRef, 2, tables.Layouts,
            out var pointeeType), Is.True);
        Assert.That(pointeeType, Is.EqualTo(PrimitiveType.UInt32));
        var targetContracts = tables.Contracts.GetNativeParameters(target);
        var anchorContracts = tables.Contracts.GetNativeParameters(anchor);
        Assert.That(targetContracts.Length, Is.EqualTo(1));
        Assert.That(targetContracts[0], Is.EqualTo(anchorContracts[0]));
        Assert.That(targetContracts[0].Access, Is.EqualTo(2));
        Assert.That(targetContracts[0].ExtentKind, Is.Zero);
        Assert.That(targetContracts[0].Extent, Is.Zero);
    }

    [Test]
    public void EmitsIndirectUnitCallFromNativeAddress()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void Observe();

            internal void Form() {
                unsafe {
                    var callback = &Observe;
                    callback();
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var form = program.Functions.Single(static function =>
            function.Name == "Form");
        var calls = new List<BoundIndirectCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(form.Body, expression =>
        {
            if (expression is BoundIndirectCall indirectCall)
            {
                calls.Add(indirectCall);
            }
        }), Is.True);
        var call = calls.Single();
        Assert.That(call.Target, Is.TypeOf<BoundRead>());

        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                program, "native-address-indirect-call");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var caller = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Form")
            {
                caller = row;
                break;
            }
        }

        Assert.That(caller, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(caller);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);
        Assert.That(body.RecordCount, Is.EqualTo(3));
        Assert.That(body.GetRecord(1)[0], Is.EqualTo(0x0D));
        Assert.That(body.GetRecord(1)[4], Is.EqualTo(2));
        Assert.That(body.GetRecord(1)[6], Is.Zero);
        Assert.That(body.HomeMap.Length, Is.EqualTo(8));
        Assert.That(body.HomeMap[0], Is.EqualTo(3));
        Assert.That(body.GetRecord(2)[0], Is.EqualTo(0x02));
        Assert.That(body.GetRecord(2)[4], Is.EqualTo(3));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE9, 0, 1, 0, 0xE0, 0, 2, 0, 0x18, 0,
            }));
    }

    [Test]
    public void EmitsNativeParameterCallSiteFromInferredAddress()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern void Observe(ptr<uint32> value);

            internal void Form(ptr<uint32> value) {
                unsafe {
                    var callback = &Observe;
                    callback(value);
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                program, "native-address-parameter-call");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var target = 0;
        var caller = 0;
        var anchor = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            target = name == "Observe" ? row : target;
            caller = name == "Form" ? row : caller;
            anchor = name.StartsWith("__trrm_fnptr_anchor_",
                StringComparison.Ordinal) ? row : anchor;
        }

        Assert.That(target, Is.GreaterThan(0));
        Assert.That(caller, Is.GreaterThan(0));
        Assert.That(anchor, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(caller);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);
        var callSite = body.GetRecord(2);
        Assert.That(body.HomeMap.Length, Is.EqualTo(16));
        Assert.That(body.HomeMap[0], Is.EqualTo(3));
        Assert.That(body.HomeMap[8], Is.EqualTo(2));
        Assert.That(callSite[0], Is.EqualTo(0x02));
        Assert.That(callSite[4], Is.EqualTo(3));
        Assert.That(callSite[12], Is.EqualTo(1));
        Assert.That(callSite[20], Is.EqualTo(2));
        Assert.That(callSite[21], Is.EqualTo(1));
        Assert.That(callSite[22], Is.EqualTo(1));
        Assert.That(callSite[23], Is.Zero);
        var targetParameters = tables.Contracts.GetNativeParameters(target);
        var anchorParameters = tables.Contracts.GetNativeParameters(anchor);
        Assert.That(targetParameters.Length, Is.EqualTo(1));
        Assert.That(targetParameters[0], Is.EqualTo(anchorParameters[0]));
        Assert.That(targetParameters[0].Extent, Is.EqualTo(1u));
    }

    [Test]
    public void EmitsWritableNativeParameterCallSiteFromInferredAddress()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 0)]
            internal extern void Observe(write ptr<uint32> value);

            internal void Form(write ptr<uint32> value) {
                unsafe {
                    var callback = &Observe;
                    callback(value);
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-writable-parameter-call");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var target = 0;
        var caller = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            target = name == "Observe" ? row : target;
            caller = name == "Form" ? row : caller;
        }

        Assert.That(target, Is.GreaterThan(0));
        Assert.That(caller, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(caller);
        var targetParameters = tables.Contracts.GetNativeParameters(target);
        Assert.That(targetParameters.Length, Is.EqualTo(1));
        Assert.That(targetParameters[0].Access, Is.EqualTo(2));
        Assert.That(targetParameters[0].ExtentKind, Is.Zero);
        Assert.That(targetParameters[0].Extent, Is.Zero);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);
        Assert.That(body.HomeMap.Length, Is.EqualTo(16));
    }

    [Test]
    public void PreservesReorderedNativeParameterCallSiteInputs()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern void Observe(ptr<uint32> value, uint32 count);

            internal void Form(uint32 count, ptr<uint32> value) {
                unsafe {
                    var callback = &Observe;
                    callback(value, count);
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-reordered-arguments");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var caller = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Form")
            {
                caller = row;
                break;
            }
        }

        Assert.That(caller, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(caller);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);
        var callSite = body.GetRecord(2);
        Assert.That(body.HomeMap.Length, Is.EqualTo(24));
        Assert.That(body.HomeMap[8], Is.EqualTo(2));
        Assert.That(body.HomeMap[16], Is.EqualTo(2));
        Assert.That(callSite[12], Is.EqualTo(2));
        Assert.That(callSite[20], Is.EqualTo(2));
        Assert.That(callSite[21], Is.EqualTo(1));
        Assert.That(callSite[22], Is.EqualTo(2));
        Assert.That(callSite[24], Is.EqualTo(2));
        Assert.That(callSite[25], Is.EqualTo(1));
        Assert.That(callSite[26], Is.EqualTo(1));
    }

    [Test]
    public async Task StandardNativeExportSurvivesSourceFreeLibraryReemissionAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetCurrentProcessId",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 Observe();

            [Terrum.Interop.NativeExport(EntryName: "Form_C")]
            internal uint32 Form() {
                unsafe {
                    var callback = &Observe;
                    return callback();
                }
            }
            """), core);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        const string moduleIdentity = "native-address-export";
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, moduleIdentity);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        Assert.That(tables.NativeExports.Count, Is.EqualTo(1));
        Assert.That(tables.NativeExports.GetEntryName(1),
            Is.EqualTo("Form_C"));

        var image = NativeModuleCompiler.EmitStandardLibrary(
            new TrRmStandardArtifactInput(
                "trrm/1-draft-foreign-invocation-contracts-1", artifact),
            moduleIdentity, [], [], NativeRuntimePolicy.FailFast);
        Assert.That(ReadPeExports(image), Is.EqualTo(["Form_C"]));

        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string providerIdentity = "Observe";
        var rootWithImport = AddRootMethodImports(artifact, [
            ("Observe", providerIdentity, "Observe"),
        ], reuseModuleRefs: false, duplicateSecondMethodName: false);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootWithImport);
        TrRmStandardArtifactInput CreateProvider(
            string libraryName = "kernel32.dll")
        {
            var providerCompilation = Compilation.CreateLibrary(
                SyntaxTree.Parse($$"""
                    [Terrum.Interop.NativeImport(
                        "{{libraryName}}",
                        "GetCurrentProcessId",
                        CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
                    public extern uint32 Observe();

                    internal uint32 ProviderBody() {
                        unsafe {
                            var callback = &Observe;
                            return callback();
                        }
                    }
                    """), core);
            Assert.That(providerCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    providerCompilation.Diagnostics));
            var providerArtifact = TrRmExecutableCompiler
                .EmitForeignInvocationDraftNativeAddressLibrary(
                    providerCompilation.Program!, providerIdentity);
            return new TrRmStandardArtifactInput(schemaIdentity,
                providerArtifact);
        }

        var provider = CreateProvider();
        var replacementProvider = new TrRmStandardArtifactInput(
            schemaIdentity,
            RenameMethodDef(provider.Artifact.ToArray(), "ProviderBody",
                "GetCurrentProcessId"));
        Assert.That(provider.Artifact.Span.SequenceEqual(
            replacementProvider.Artifact.Span), Is.False);
        var incompatibleProvider = CreateProvider("user32.dll");
        var importLibrary = NativeEmitter.System32ImportPath("kernel32.dll");
        foreach (var selectedProvider in new[] {
            provider,
            replacementProvider,
        })
        {
            var providerImage = NativeModuleCompiler.EmitStandardLibrary(
                rootInput, moduleIdentity, [selectedProvider], [],
                NativeRuntimePolicy.FailFast);
            Assert.That(ReadPeExports(providerImage), Is.EqualTo(["Form_C"]));
            Assert.That(ReadPeImports(providerImage, importLibrary),
                Does.Contain("GetCurrentProcessId"));
            Assert.That(await InvokeExport(providerImage).ConfigureAwait(true),
                Is.GreaterThan(0));
        }
        _ = Assert.Throws<InvalidDataException>(() =>
            NativeModuleCompiler.EmitStandardLibrary(rootInput,
                moduleIdentity, [incompatibleProvider], [],
                NativeRuntimePolicy.FailFast));

        static async Task<int> InvokeExport(byte[] image)
        {
            var path = Path.Combine(TestArtifactPaths.DirectoryPath,
                $"native-export-{Guid.NewGuid():N}.dll");
            var handle = IntPtr.Zero;
            try
            {
                await File.WriteAllBytesAsync(path, image)
                    .ConfigureAwait(true);
                handle = NativeLibrary.Load(path);
                var export = Marshal.GetDelegateForFunctionPointer<
                    NativeAddressExport>(NativeLibrary.GetExport(handle,
                        "Form_C"));
                return export();
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    NativeLibrary.Free(handle);
                }

                await TestFileCleanup.DeleteWithRetryAsync(path)
                    .ConfigureAwait(true);
            }
        }
    }

    [Test]
    public async Task StandardNativeExportOnStaticTypeSurvivesSourceFreeAotAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "static-type-native-address-export";
        var nativeRegistration
            = NativeCoreRegistrationTests.CreateRegistration();
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetCurrentProcessId",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 Observe();

            [Blittable]
            [Layout(LayoutKind.Sequential)]
            struct StaticApi {
                public int32 Value;

                [Terrum.Interop.NativeExport(EntryName: "Form_C")]
                static uint32 Form() {
                    unsafe {
                        var callback = &Observe;
                        return callback();
                    }
                }
            }
            """), WithLayoutRegistration(new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration = nativeRegistration,
        }));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, moduleIdentity,
                TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyDraft);
        var artifactIndex = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                artifactIndex);
        var exportedMethodRow = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            if (tables.NativeExports.HasExportForMethod(methodRow))
            {
                exportedMethodRow = methodRow;
                break;
            }
        }
        Assert.That(exportedMethodRow, Is.GreaterThan(0));
        var exportedMethod = tables.NativeExports.GetExportForMethod(
            exportedMethodRow);
        var ownerType = tables.Methods.GetOwnerType(exportedMethod.OwnerRef);
        Assert.That(ownerType, Is.GreaterThan(0));
        Assert.That(tables.TypeDefs.GetName(ownerType),
            Is.EqualTo("StaticApi"));

        var image = NativeModuleCompiler.EmitStandardLibrary(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, [], [], NativeRuntimePolicy.FailFast);
        Assert.That(ReadPeExports(image), Is.EqualTo(["Form_C"]));
        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"static-native-export-{Guid.NewGuid():N}.dll");
        var handle = IntPtr.Zero;
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            handle = NativeLibrary.Load(path);
            var export = Marshal.GetDelegateForFunctionPointer<
                NativeAddressExport>(NativeLibrary.GetExport(handle,
                    "Form_C"));
            Assert.That(export(), Is.GreaterThan(0));
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
            }

            await TestFileCleanup.DeleteWithRetryAsync(path)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public void EmitsIndirectScalarResultFromNativeAddress()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 Transform(uint32 value);

            internal uint32 Form(uint32 value) {
                unsafe {
                    var callback = &Transform;
                    return callback(value);
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-result");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var caller = 0;
        var target = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            caller = name == "Form" ? row : caller;
            target = name == "Transform" ? row : target;
        }

        Assert.That(caller, Is.GreaterThan(0));
        Assert.That(target, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(caller);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);
        var callSite = body.GetRecord(2);
        Assert.That(callSite[4], Is.EqualTo(3));
        Assert.That(callSite[5], Is.EqualTo(1));
        Assert.That(callSite[10], Is.Zero);
        Assert.That(callSite[11], Is.Zero);
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE9, 0, 1, 0, 0xE0, 0, 2, 0, 0x17, 0,
            }));
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
            tables.Signatures.GetResultTypeSpec(
                tables.Methods.GetSignature(caller)),
            tables.Signatures.GetResultTypeSpec(
                tables.Methods.GetSignature(target))), Is.True);
    }

    [Test]
    public void EmitsIndirectScalarResultThroughHomeFromNativeAddress()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 Transform(uint32 value);

            internal uint32 Form(uint32 value) {
                unsafe {
                    var callback = &Transform;
                    var result = callback(value);
                    return result;
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-result-home");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var caller = 0;
        var target = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            caller = name == "Form" ? row : caller;
            target = name == "Transform" ? row : target;
        }

        Assert.That(caller, Is.GreaterThan(0));
        Assert.That(target, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(caller);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);
        var callSite = body.GetRecord(2);
        Assert.That(body.HomeMap.Length, Is.EqualTo(24));
        Assert.That(body.HomeMap[0], Is.EqualTo(3));
        Assert.That(body.HomeMap[8], Is.EqualTo(3));
        Assert.That(body.HomeMap[16], Is.EqualTo(2));
        Assert.That(callSite[4], Is.EqualTo(3));
        Assert.That(callSite[5], Is.EqualTo(2));
        Assert.That(callSite[10], Is.EqualTo(1));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE9, 0, 1, 0, 0xE0, 0, 2, 0, 0xB8, 1,
            }));
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
            tables.Signatures.GetResultTypeSpec(
                tables.Methods.GetSignature(caller)),
            tables.Signatures.GetResultTypeSpec(
                tables.Methods.GetSignature(target))), Is.True);
    }

    [Test]
    public void StagesDirectNativeCallBeforeIndirectCallArguments()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 Transform(uint32 value);

            internal uint32 Form(uint32 value) {
                unsafe {
                    var callback = &Transform;
                    return callback(Transform(value));
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        const string moduleIdentity = "native-address-staged-call";
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Form")
            {
                callerRow = row;
                break;
            }
        }

        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        Assert.That(body.RecordCount, Is.EqualTo(4));
        Assert.That(body.GetRecord(2)[4], Is.EqualTo(1));
        Assert.That(body.GetRecord(2)[5], Is.EqualTo(1));
        Assert.That(body.GetRecord(3)[4], Is.EqualTo(3));
        Assert.That(body.GetRecord(3)[20], Is.EqualTo(1));
        Assert.That(body.GetRecord(3)[21], Is.EqualTo(1));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE9, 0, 1, 0, 0xE0, 0, 2, 0,
                0xE0, 0, 3, 0, 0x17, 0,
            }));

        var loaded = TrRmExecutableCompiler
            .LoadForeignInvocationDraftNativeAddressLibrary(artifact);
        var form = loaded.Functions.Single(static function =>
            function.Name == "Form");
        var indirectCalls = new List<BoundIndirectCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(form.Body, expression =>
        {
            if (expression is BoundIndirectCall indirectCall)
            {
                indirectCalls.Add(indirectCall);
            }
        }), Is.True);
        Assert.That(indirectCalls.Single().Arguments.Single(),
            Is.TypeOf<BoundCall>());
        Assert.That(TrRmExecutableCompiler.EmitStandard(
            loaded, moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
    }

    [Test]
    public void StagesMultipleDirectCallsInIndirectArguments()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 Combine(uint32 left, uint32 right);

            internal uint32 Form(uint32 left, uint32 right) {
                unsafe {
                    var callback = &Combine;
                    var result = callback(
                        Combine(left, right),
                        Combine(right, left));
                    return result;
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        const string moduleIdentity = "native-address-multiple-staged-calls";
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Form")
            {
                callerRow = row;
                break;
            }
        }

        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        Assert.That(body.RecordCount, Is.EqualTo(5));
        Assert.That(body.HomeMap.Length, Is.EqualTo(40));
        Assert.That(body.HomeMap[0], Is.EqualTo(3));
        Assert.That(body.HomeMap[8], Is.EqualTo(3));
        Assert.That(body.HomeMap[16], Is.EqualTo(3));
        Assert.That(body.HomeMap[24], Is.EqualTo(2));
        Assert.That(body.HomeMap[32], Is.EqualTo(2));
        var firstStagedCall = body.GetRecord(2);
        Assert.That(firstStagedCall[4], Is.EqualTo(1));
        Assert.That(firstStagedCall[5], Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            firstStagedCall[10..]), Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            firstStagedCall[22..]), Is.EqualTo(3));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            firstStagedCall[26..]), Is.EqualTo(4));
        var secondStagedCall = body.GetRecord(3);
        Assert.That(secondStagedCall[5], Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            secondStagedCall[22..]), Is.EqualTo(4));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            secondStagedCall[26..]), Is.EqualTo(3));
        var indirectCall = body.GetRecord(4);
        Assert.That(indirectCall[4], Is.EqualTo(3));
        Assert.That(indirectCall[5], Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            indirectCall[10..]), Is.EqualTo(1));
        Assert.That(indirectCall[20], Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            indirectCall[22..]), Is.EqualTo(2));
        Assert.That(indirectCall[24], Is.EqualTo(1));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE9, 0, 1, 0, 0xE0, 0, 2, 0,
                0xE0, 0, 3, 0, 0xE0, 0, 4, 0, 0xB8, 1,
            }));

        var loaded = TrRmExecutableCompiler
            .LoadForeignInvocationDraftNativeAddressLibrary(artifact);
        Assert.That(TrRmExecutableCompiler.EmitStandard(
            loaded, moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
    }

    [Test]
    public void ReconstructsNativeAddressCallFromSelectedForeignInvocationProfile()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern uint32 Transform(ptr<uint32> value, uint32 count);

            internal uint32 Form(uint32 count, ptr<uint32> value) {
                unsafe {
                    var callback = &Transform;
                    var result = callback(value, count);
                    return result;
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        const string moduleIdentity = "native-address-source-free";
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        Assert.That(TrRmArtifactIndex.ForForeignInvocationDraft(artifact).Schema,
            Is.EqualTo(TrRmArtifactSchema.ForeignInvocationDraft));

        var loaded = TrRmExecutableCompiler
            .LoadForeignInvocationDraftNativeAddressLibrary(artifact);
        var target = loaded.Functions.Single(static function =>
            function.Name == "Transform");
        Assert.That(target.DirectNativeImport!.LibraryName,
            Is.EqualTo("example.dll"));
        Assert.That(target.DirectNativeImport.Parameters.Single(),
            Is.EqualTo(new DirectNativeParameter(0, PrimitiveType.UInt32,
                NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 1)));
        var pointerSignature = loaded.NativeFunctionPointerSignatures.Single();
        Assert.That(pointerSignature.NativeParameters.Single(),
            Is.EqualTo(target.DirectNativeImport.Parameters.Single()));

        var caller = loaded.Functions.Single(static function =>
            function.Name == "Form");
        var calls = new List<BoundIndirectCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body, expression =>
        {
            if (expression is BoundIndirectCall call)
            {
                calls.Add(call);
            }
        }), Is.True);
        var call = calls.Single();
        Assert.That(call.Arguments.Select(static argument =>
            ((BoundRead)argument).Variable), Is.EqualTo([1, 0]));
        Assert.That(call.Type, Is.EqualTo(PrimitiveType.UInt32));
        SemanticVerifier.VerifyLibraryWithDirectNativeImports(loaded);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(artifact));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            loaded, moduleIdentity, schemaIdentity, 1);
        Assert.That(reemitted, Is.EqualTo(artifact));
    }

    [Test]
    public void StandardFReferencePreservesImportedNativeCallContracts()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        const string providerSource = """
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 4294967295)]
            public extern uint32 Transform(ptr<uint32> value);

            internal uint32 ProviderBody(ptr<uint32> value) {
                unsafe {
                    var callback = &Transform;
                    return callback(value);
                }
            }
            """;
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource), core);

        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        const string providerIdentity = "native-provider";
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, schemaIdentity, 1);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, providerArtifact),
            providerIdentity, []);
        var consumerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal uint32 Use(ptr<uint32> value) {
                unsafe {
                    var callback = &Transform;
                    return callback(value);
                }
            }
            """), reference);

        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var imported = consumerCompilation.Program!.Functions.Single(
            static function => function.Name == "Transform");
        Assert.That(imported.IsExternal, Is.True);
        Assert.That(imported.IsModuleReference, Is.True);
        Assert.That(imported.ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(imported.DirectNativeImport!.LibraryName,
            Is.EqualTo("example.dll"));
        Assert.That(imported.DirectNativeImport.Parameters.Single().Extent,
            Is.EqualTo(uint.MaxValue));
        Assert.That(consumerCompilation.Program.NativeFunctionPointerSignatures
            .Single().NativeParameters.Single().Extent,
            Is.EqualTo(uint.MaxValue));

        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerCompilation.Program, "native-consumer", schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(consumerArtifact));
        Assert.That(tables.ModuleRefs.Count, Is.EqualTo(1));
        Assert.That(tables.ModuleRefs.GetProducerIdentity(1),
            Is.EqualTo(providerIdentity));
        Assert.That(tables.Imports.Count, Is.EqualTo(1));
        Assert.That(tables.StandardNativeImports.Count, Is.EqualTo(1));
        var targetRow = 0;
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            targetRow = name == "Transform" ? row : targetRow;
            callerRow = name == "Use" ? row : callerRow;
        }

        Assert.That(targetRow, Is.GreaterThan(0));
        Assert.That(callerRow, Is.GreaterThan(0));
        Assert.That(tables.Imports.IsExpectedDefinition(
            0x1600_0000u | (uint)targetRow), Is.True);
        tables.ValidateRegisterBodyMapsForMethod(callerRow);

        var rootInput = new TrRmStandardArtifactInput(
            schemaIdentity, consumerArtifact);
        var providerInput = new TrRmStandardArtifactInput(
            schemaIdentity, providerArtifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            rootInput, "native-consumer", [providerInput]);
        var loadedTarget = loaded.Functions.Single(static function =>
            function.Name == "Transform");
        Assert.That(loadedTarget.DirectNativeImport!.LibraryName,
            Is.EqualTo("example.dll"));
        Assert.That(loadedTarget.DirectNativeImport.Parameters.Single().Extent,
            Is.EqualTo(uint.MaxValue));
        Assert.That(loadedTarget.ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(loaded.NativeFunctionPointerSignatures.Single()
            .NativeParameters.Single().Extent, Is.EqualTo(uint.MaxValue));
        var loadedCaller = loaded.Functions.Single(static function =>
            function.Name == "Use");
        var loadedCalls = new List<BoundIndirectCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedCaller.Body,
            expression =>
            {
                if (expression is BoundIndirectCall call)
                {
                    loadedCalls.Add(call);
                }
            }), Is.True);
        Assert.That(loadedCalls, Has.Count.EqualTo(1));
        SemanticVerifier.VerifyLibraryWithDirectNativeImports(loaded);
        _ = Assert.Throws<NotSupportedException>(() =>
            NativeEmitter.EmitWithNativeImports(loaded,
                NativeRuntimePolicy.FailFast));
        Assert.That(TrRmExecutableCompiler.EmitStandard(
            loaded, "native-consumer", schemaIdentity, 1),
            Is.EqualTo(consumerArtifact));

        var replacementProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource.Replace("ProviderBody",
                "ReplacementBody", StringComparison.Ordinal)), core);
        Assert.That(replacementProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                replacementProviderCompilation.Diagnostics));
        var replacementProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            replacementProviderCompilation.Program!, providerIdentity,
            schemaIdentity, 1);
        Assert.That(replacementProviderArtifact, Is.Not.EqualTo(providerArtifact));
        var replaced = TrRmExecutableCompiler.LoadStandard(rootInput,
            "native-consumer", [
                new TrRmStandardArtifactInput(schemaIdentity,
                    replacementProviderArtifact),
            ]);
        var replacedTarget = replaced.Functions.Single(static function =>
            function.Name == "Transform");
        Assert.That(replacedTarget.ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(replacedTarget.DirectNativeImport!.Parameters.Single()
            .Extent, Is.EqualTo(uint.MaxValue));

        var multiModuleConsumerArtifact = AddLeadingEmptyModule(
            consumerArtifact, "Use");
        var multiModuleProviderArtifact = AddLeadingEmptyModule(
            providerArtifact, "ProviderBody");
        var multiModuleRootInput = new TrRmStandardArtifactInput(
            schemaIdentity, multiModuleConsumerArtifact);
        var multiModuleProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, multiModuleProviderArtifact);
        var multiModuleLoaded = TrRmExecutableCompiler.LoadStandard(
            multiModuleRootInput, "native-consumer",
            [multiModuleProviderInput]);
        Assert.That(multiModuleLoaded.Functions.Single(static function =>
            function.Name == "Transform").ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        var multiModuleReference = TrRmExecutableCompiler
            .CreateStandardReference(multiModuleProviderInput,
                providerIdentity, []);
        Assert.That(multiModuleReference.Functions.Single(
            static function => function.Name == "Transform")
            .DirectNativeImport!.Parameters.Single().Extent,
            Is.EqualTo(uint.MaxValue));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(multiModuleRootInput,
                "Use", [multiModuleProviderInput]));

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                rootInput, "native-consumer", []));

        var incompatibleProvider = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource.Replace("4294967295", "1",
                StringComparison.Ordinal)), core);
        Assert.That(incompatibleProvider.HasErrors, Is.False,
            string.Join(Environment.NewLine, incompatibleProvider.Diagnostics));
        var incompatibleProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            incompatibleProvider.Program!, providerIdentity, schemaIdentity, 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, "native-consumer", [
                new TrRmStandardArtifactInput(schemaIdentity,
                    incompatibleProviderArtifact),
            ]));
    }

    [Test]
    public void StandardFProviderClosureValidatesMixedProviderDependencies()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "Root";
        const string providerIdentity = "Owned";
        const string leafIdentity = "Leaf";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var rootArtifact = CreateRootMethodImportArtifact(rootIdentity,
            "Owned", providerIdentity, "Owned", core);
        var providerArtifact = CreateMixedImportProviderArtifact(
            providerIdentity, "Owned", "GetCurrentProcessId",
            "Leaf", "GetTickCount", leafIdentity, "Leaf", core);
        var leafCompilation = CreateMultipleImportProviderCompilation(
            "Leaf", "GetTickCount", core);
        var leafArtifact = TrRmExecutableCompiler.EmitStandard(
            leafCompilation.Program!, leafIdentity, schemaIdentity, 1);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var leafInput = new TrRmStandardArtifactInput(schemaIdentity,
            leafArtifact);
        var emptyModuleInput = new TrRmStandardArtifactInput(schemaIdentity,
            CreateEmptyFModuleArtifact("EmptyF"));

        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, [providerInput, leafInput, emptyModuleInput]);
        var imported = loaded.Functions.Single(static function =>
            function.Name == "Owned");
        Assert.That(imported.ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(imported.DirectNativeImport!.ExportName,
            Is.EqualTo("GetCurrentProcessId"));

        var nonFirstRootInput = new TrRmStandardArtifactInput(schemaIdentity,
            AddLeadingEmptyModule(rootArtifact, "Main"));
        var nonFirstProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, AddLeadingEmptyModule(providerArtifact,
                "ProviderBody"));
        var nonFirstLoaded = TrRmExecutableCompiler.LoadStandard(
            nonFirstRootInput, rootIdentity,
            [nonFirstProviderInput, leafInput]);
        Assert.That(nonFirstLoaded.Functions.Single(static function =>
            function.Name == "Owned").ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        var nonFirstProviderReference
            = TrRmExecutableCompiler.CreateStandardReference(
                nonFirstProviderInput, providerIdentity, [leafInput]);
        Assert.That(nonFirstProviderReference.Functions.Single(
            static function => function.Name == "Owned").Name,
            Is.EqualTo("Owned"));

        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, [leafInput]);
        Assert.That(providerReference.Functions.Select(
                static function => function.Name),
            Does.Contain("Owned").And.Not.Contain("Leaf"));

        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            rootInput, rootIdentity, [providerInput, leafInput],
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);
        var (loweredProgram, nativeImports) = plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable);
        Assert.That(nativeImports.Select(static binding =>
                binding.FunctionName),
            Is.EquivalentTo(["Owned"]));
        Assert.That(loweredProgram.Functions.Single(static function =>
            function.Name == "Owned").ModuleImport, Is.Null);
        var image = NativeModuleCompiler.EmitStandardExecutable(rootInput,
            rootIdentity, [providerInput, leafInput],
            NativeRuntimePolicy.FailFast);
        Assert.That(image.Take(2), Is.EqualTo([(byte)'M', (byte)'Z']));

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity,
                [providerInput]));

        var incompatibleLeafCompilation
            = CreateMultipleImportProviderCompilation(
                "Leaf", "GetCurrentThreadId", core);
        var incompatibleLeafArtifact
            = TrRmExecutableCompiler.EmitStandard(
                incompatibleLeafCompilation.Program!, leafIdentity,
                schemaIdentity, 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
                providerInput,
                new TrRmStandardArtifactInput(schemaIdentity,
                    incompatibleLeafArtifact),
            ]));

        var disconnectedArtifact = CreateMixedImportProviderArtifact(
            "closure-disconnected", "DisconnectedOwned",
            "GetCurrentThreadId", "Leaf",
            "GetTickCount", leafIdentity, "GetTickCount", core);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
                providerInput,
                leafInput,
                new TrRmStandardArtifactInput(schemaIdentity,
                    disconnectedArtifact),
            ]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
                providerInput,
                leafInput,
                emptyModuleInput,
                new TrRmStandardArtifactInput(schemaIdentity,
                    emptyModuleInput.Artifact),
            ]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
                providerInput,
                leafInput,
                rootInput,
            ]));
    }

    [Test]
    public void StandardFProviderClosureChecksDeepNativeParameterExtent()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "Root";
        const string providerIdentity = "Owned";
        const string leafIdentity = "Transform";
        const string leafSource = """
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 4294967295)]
            public extern uint32 Transform(ptr<uint32> value);

            internal uint32 ProviderBody(ptr<uint32> value) {
                unsafe {
                    var callback = &Transform;
                    return callback(value);
                }
            }
            """;
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var rootArtifact = CreateRootMethodImportArtifact(rootIdentity,
            "Owned", providerIdentity, "Owned", core);
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Terrum.Interop.NativeImport(
                    "example.dll",
                    CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
                [Terrum.Interop.NativeParameter(1, Read, NoEscape, 4294967295)]
                internal extern uint32 Transform(ptr<uint32> value);

                [Terrum.Interop.NativeImport(
                    "kernel32.dll",
                    "GetCurrentProcessId",
                    CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
                public extern int32 Owned();

                internal int32 ProviderBody() {
                    unsafe {
                        var callback = &Owned;
                        return callback();
                    }
                }
                """), core);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, schemaIdentity, 1);
        providerArtifact = AddRootMethodImports(providerArtifact, [
            ("Transform", leafIdentity, "Transform"),
        ], reuseModuleRefs: false, duplicateSecondMethodName: false);
        var leafCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(leafSource), core);
        Assert.That(leafCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, leafCompilation.Diagnostics));
        var leafArtifact = TrRmExecutableCompiler.EmitStandard(
            leafCompilation.Program!, leafIdentity, schemaIdentity, 1);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var leafInput = new TrRmStandardArtifactInput(schemaIdentity,
            leafArtifact);

        var loadedProvider = TrRmExecutableCompiler.LoadStandard(providerInput,
            providerIdentity, [leafInput]);
        var imported = loadedProvider.Functions.Single(static function =>
            function.Name == "Transform");
        Assert.That(imported.ModuleImport!.ProducerIdentity,
            Is.EqualTo(leafIdentity));
        Assert.That(imported.DirectNativeImport!.Parameters.Single().Extent,
            Is.EqualTo(uint.MaxValue));
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, [leafInput]);
        Assert.That(providerReference.Functions.Select(
                static function => function.Name),
            Does.Contain("Owned").And.Not.Contain("Transform"));
        _ = TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
            providerInput,
            leafInput,
        ]);

        var incompatibleLeafCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(leafSource.Replace("4294967295", "2147483648",
                StringComparison.Ordinal)), core);
        Assert.That(incompatibleLeafCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleLeafCompilation.Diagnostics));
        var incompatibleLeafArtifact = TrRmExecutableCompiler.EmitStandard(
            incompatibleLeafCompilation.Program!, leafIdentity,
            schemaIdentity, 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
                providerInput,
                new TrRmStandardArtifactInput(schemaIdentity,
                    incompatibleLeafArtifact),
            ]));
    }

    [Test]
    public void StandardFProviderClosureSupportsDiamondsAndChecksEveryView()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "multi-import-root";
        const string leafIdentity = "Leaf";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var (rootArtifact, _, _, _) =
            CreateMultipleRootMethodImportArtifacts();
        var firstProviderArtifact = CreateMixedImportProviderArtifact(
            "First", "First", "GetCurrentProcessId", "Leaf",
            "GetTickCount", leafIdentity, "Leaf", core);
        var secondProviderArtifact = CreateMixedImportProviderArtifact(
            "Second", "Second", "GetCurrentThreadId", "Leaf",
            "GetTickCount", leafIdentity, "Leaf", core);
        var leafCompilation = CreateMultipleImportProviderCompilation(
            "Leaf", "GetTickCount", core);
        var leafArtifact = TrRmExecutableCompiler.EmitStandard(
            leafCompilation.Program!, leafIdentity, schemaIdentity, 1);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var providers = new[]
        {
            new TrRmStandardArtifactInput(schemaIdentity,
                firstProviderArtifact),
            new TrRmStandardArtifactInput(schemaIdentity,
                secondProviderArtifact),
            new TrRmStandardArtifactInput(schemaIdentity, leafArtifact),
        };

        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, providers);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "First").ModuleImport!.ProducerIdentity,
            Is.EqualTo("First"));
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "Second").ModuleImport!.ProducerIdentity,
            Is.EqualTo("Second"));
        var reorderedLoaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, providers.Reverse().ToArray());
        Assert.That(reorderedLoaded.Functions.Single(static function =>
            function.Name == "First").ModuleImport!.ProducerIdentity,
            Is.EqualTo("First"));
        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            rootInput, rootIdentity, providers,
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);
        Assert.That(plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable).Bindings.Select(
                static binding => binding.FunctionName),
            Is.EquivalentTo(["First", "Second"]));

        var incompatibleSecondArtifact = CreateMixedImportProviderArtifact(
            "Second", "Second", "GetCurrentThreadId", "Leaf",
            "GetCurrentProcessId", leafIdentity, "Leaf", core);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
                providers[0],
                new TrRmStandardArtifactInput(schemaIdentity,
                    incompatibleSecondArtifact),
                providers[2],
            ]));
    }

    [Test]
    public void StandardFProviderClosureValidatesChainsLongerThanTwoEdges()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "Root";
        const string providerIdentity = "Owned";
        const string secondIdentity = "Leaf";
        const string thirdIdentity = "Deep";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var rootArtifact = CreateRootMethodImportArtifact(rootIdentity,
            "Owned", providerIdentity, "Owned", core);
        var providerArtifact = CreateMixedImportProviderArtifact(
            providerIdentity, "Owned", "GetCurrentProcessId",
            "Leaf", "GetTickCount", secondIdentity, "Leaf", core);
        var secondArtifact = CreateMixedImportProviderArtifact(secondIdentity,
            "Leaf", "GetTickCount", "Deep", "GetCurrentThreadId",
            thirdIdentity, "Deep", core);
        var thirdCompilation = CreateMultipleImportProviderCompilation(
            "Deep", "GetCurrentThreadId", core);
        var thirdArtifact = TrRmExecutableCompiler.EmitStandard(
            thirdCompilation.Program!, thirdIdentity, schemaIdentity, 1);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var secondInput = new TrRmStandardArtifactInput(schemaIdentity,
            secondArtifact);
        var thirdInput = new TrRmStandardArtifactInput(schemaIdentity,
            thirdArtifact);
        var compatibleThirdCompilation
            = CreateMultipleImportProviderCompilation(
                "Deep", "GetCurrentThreadId", core,
                providerBodyName: "ReplacementBody");
        var compatibleThirdArtifact = TrRmExecutableCompiler.EmitStandard(
            compatibleThirdCompilation.Program!, thirdIdentity,
            schemaIdentity, 1);
        Assert.That(compatibleThirdArtifact, Is.Not.EqualTo(thirdArtifact));

        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, [providerInput, secondInput, thirdInput]);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "Owned").ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            rootInput, rootIdentity,
            [providerInput, secondInput, thirdInput],
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);
        Assert.That(plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable).Bindings,
            Has.Length.EqualTo(1));
        var compatibleProviderReplacement
            = CreateMixedImportProviderArtifact(providerIdentity, "Owned",
                "GetCurrentProcessId", "Leaf", "GetTickCount",
                secondIdentity, "Leaf", core,
                callerTargetMethodName: "Leaf");
        Assert.That(compatibleProviderReplacement,
            Is.Not.EqualTo(providerArtifact));
        _ = TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
            new TrRmStandardArtifactInput(schemaIdentity,
                compatibleProviderReplacement),
            secondInput,
            thirdInput,
        ]);
        var compatibleReplacement = TrRmExecutableCompiler.LoadStandard(
            rootInput, rootIdentity, [
                providerInput,
                secondInput,
                new TrRmStandardArtifactInput(schemaIdentity,
                    compatibleThirdArtifact),
            ]);
        Assert.That(compatibleReplacement.Functions.Single(
            static function => function.Name == "Owned").ModuleImport!
            .ProducerIdentity, Is.EqualTo(providerIdentity));

        var incompatibleThird = CreateMultipleImportProviderCompilation(
            "Deep", "GetTickCount", core);
        var incompatibleThirdArtifact = TrRmExecutableCompiler.EmitStandard(
            incompatibleThird.Program!, thirdIdentity, schemaIdentity, 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
                providerInput,
                secondInput,
                new TrRmStandardArtifactInput(schemaIdentity,
                    incompatibleThirdArtifact),
            ]));

        var coLocatedProviderArtifact
            = CombinePopulatedNativeAddressModules(providerArtifact,
                secondArtifact);
        var coLocatedProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, coLocatedProviderArtifact);
        var hybridLoaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, [coLocatedProviderInput, thirdInput]);
        Assert.That(hybridLoaded.Functions.Single(static function =>
            function.Name == "Owned").ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        var coLocatedRootProviderArtifact
            = CombinePopulatedNativeAddressModules(rootArtifact,
                providerArtifact);
        var coLocatedRootProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, coLocatedRootProviderArtifact);
        var rootProviderLoaded = TrRmExecutableCompiler.LoadStandard(
            coLocatedRootProviderInput, rootIdentity,
            [secondInput, thirdInput]);
        Assert.That(rootProviderLoaded.Functions.Single(
            static function => function.Name == "Owned").ModuleImport!
            .ProducerIdentity, Is.EqualTo(providerIdentity));

        var coLocatedProviderChain = CombinePopulatedNativeAddressModules(
            CombinePopulatedNativeAddressModules(providerArtifact,
                secondArtifact),
            thirdArtifact);
        var fullyCoLocatedArtifact = CombinePopulatedNativeAddressModules(
            coLocatedProviderChain, rootArtifact);
        var fullyCoLocatedInput = new TrRmStandardArtifactInput(
            schemaIdentity, fullyCoLocatedArtifact);
        var fullyCoLocatedLoaded = TrRmExecutableCompiler.LoadStandard(
            fullyCoLocatedInput, rootIdentity, []);
        Assert.That(fullyCoLocatedLoaded.Functions.Single(
            static function => function.Name == "Owned").ModuleImport!
            .ProducerIdentity, Is.EqualTo(providerIdentity));
        var fullyCoLocatedReference =
            TrRmExecutableCompiler.CreateStandardReference(
                fullyCoLocatedInput, providerIdentity, []);
        Assert.That(fullyCoLocatedReference.Functions.Select(
                static function => function.Name),
            Does.Contain("Owned").And.Not.Contain("Leaf"));
        var fullyCoLocatedPlan =
            TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
                fullyCoLocatedInput, rootIdentity, [],
                TrRmStandardNativeOperation.EmitExecutable,
                NativeRuntimePolicy.FailFast);
        Assert.That(fullyCoLocatedPlan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable).Bindings.Select(
                static binding => binding.FunctionName),
            Is.EqualTo(["Owned"]));

        var reorderedModuleArtifact =
            CombinePopulatedNativeAddressModules(
                CombinePopulatedNativeAddressModules(
                    CombinePopulatedNativeAddressModules(secondArtifact,
                        rootArtifact),
                    thirdArtifact),
                providerArtifact);
        var reorderedModuleInput = new TrRmStandardArtifactInput(
            schemaIdentity, reorderedModuleArtifact);
        var reorderedModuleLoaded = TrRmExecutableCompiler.LoadStandard(
            reorderedModuleInput, rootIdentity, []);
        Assert.That(reorderedModuleLoaded.Functions.Single(
            static function => function.Name == "Owned").ModuleImport!
            .ProducerIdentity, Is.EqualTo(providerIdentity));
        var reorderedModulePlan =
            TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
                reorderedModuleInput, rootIdentity, [],
                TrRmStandardNativeOperation.EmitExecutable,
                NativeRuntimePolicy.FailFast);
        Assert.That(reorderedModulePlan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable).Bindings.Select(
                static binding => binding.FunctionName),
            Is.EqualTo(["Owned"]));
        var missingLeafReferenceArtifact = ReencodeModuleRefIdentity(
            reorderedModuleArtifact, 3, "MissingLeaf", encoding: 2);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    missingLeafReferenceArtifact), rootIdentity, []));
        var mismatchedImportKeyArtifact = ReplaceImportExportKey(
            reorderedModuleArtifact, 3, "Deep");
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    mismatchedImportKeyArtifact), rootIdentity, []));
        var foreignBodyMethodArtifact =
            ReplaceRootBodyMethodWithForeignModuleMethod(
                reorderedModuleArtifact, rootIdentity);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    foreignBodyMethodArtifact), rootIdentity, []));
        var foreignBodyTypeArtifact =
            ReplaceRootBodyFunctionPointerTypeWithForeignModuleType(
                reorderedModuleArtifact, rootIdentity);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    foreignBodyTypeArtifact), rootIdentity, []));
    }

    [Test]
    public void StandardFProviderClosureSupportsMixedMultipleImports()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string providerIdentity = "Provider";
        const string leafIdentity = "LeafA";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Terrum.Interop.NativeImport(
                    "kernel32.dll",
                    "GetCurrentProcessId",
                    CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
                public extern int32 Owned();

                [Terrum.Interop.NativeImport(
                    "kernel32.dll",
                    "GetTickCount",
                    CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
                internal extern int32 LeafA();

                [Terrum.Interop.NativeImport(
                    "kernel32.dll",
                    "GetTickCount",
                    CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
                internal extern int32 LeafB();

                internal int32 ProviderBody() {
                    unsafe {
                        var callback = &Owned;
                        return callback();
                    }
                }
                """), core);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, schemaIdentity, 1);
        providerArtifact = AddRootMethodImports(providerArtifact, [
            ("LeafA", leafIdentity, "GetTickCount"),
            ("LeafB", leafIdentity, "GetTickCount"),
        ], reuseModuleRefs: true, duplicateSecondMethodName: false);
        var leafCompilation = CreateMultipleImportProviderCompilation(
            "GetTickCount", "GetTickCount", core);
        var leafArtifact = TrRmExecutableCompiler.EmitStandard(
            leafCompilation.Program!, leafIdentity, schemaIdentity, 1);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var leafInput = new TrRmStandardArtifactInput(schemaIdentity,
            leafArtifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(providerArtifact));
        Assert.That(tables.Imports.Count, Is.EqualTo(2));
        Assert.That(tables.ModuleRefs.Count, Is.EqualTo(1));

        var loaded = TrRmExecutableCompiler.LoadStandard(providerInput,
            providerIdentity, [leafInput]);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "LeafA").ModuleImport!.ProducerIdentity,
            Is.EqualTo(leafIdentity));
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "LeafB").ModuleImport!.ExportKey,
            Is.EqualTo("GetTickCount"));
        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            providerInput, providerIdentity, [leafInput],
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);
        Assert.That(plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable).Bindings.Select(
                static binding => binding.FunctionName),
            Is.EquivalentTo(["Owned", "LeafA", "LeafB"]));
    }

    [Test]
    public void StandardFProviderClosureRejectsDuplicateMixedMethodNames()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string providerIdentity = "Provider";
        const string leafIdentity = "Leaf";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var providerArtifact = CreateMixedImportProviderArtifact(
            providerIdentity, "Owned", "GetCurrentProcessId",
            "Leaf", "GetTickCount", leafIdentity, "Leaf", core);
        var duplicateNameArtifact = RenameMethodDef(providerArtifact,
            "Leaf", "Owned");
        var leafCompilation = CreateMultipleImportProviderCompilation(
            "Leaf", "GetTickCount", core);
        var leafArtifact = TrRmExecutableCompiler.EmitStandard(
            leafCompilation.Program!, leafIdentity, schemaIdentity, 1);
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    duplicateNameArtifact), providerIdentity, [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        leafArtifact),
                ]));
    }

    [Test]
    public void StandardFProviderClosureSupportsFanInBeyondSixteenModules()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "fan-in-root";
        const int providerCount = 17;
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var declarations = string.Join(Environment.NewLine,
            Enumerable.Range(0, providerCount).Select(index => $$"""
                [Terrum.Interop.NativeImport(
                    "kernel32.dll",
                    "GetCurrentProcessId",
                    CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
                internal extern int32 Provider{{index:D2}}();
                """));
        var rootCompilation = Compilation.Create(SyntaxTree.Parse($$"""
            {{declarations}}

            int32 Main() {
                unsafe {
                    var callback = &Provider00;
                    return callback();
                }
            }
            """), core);
        Assert.That(rootCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var rootArtifact = TrRmExecutableCompiler.EmitStandard(
            rootCompilation.Program!, rootIdentity, schemaIdentity, 1);
        var bindings = new List<(string MethodName, string ProducerIdentity,
            string ExportKey)>(providerCount);
        var providers = new TrRmStandardArtifactInput[providerCount];
        for (var index = 0; index < providerCount; index++)
        {
            var identity = $"Provider{index:D2}";
            bindings.Add((identity, identity, identity));
            var compilation = CreateMultipleImportProviderCompilation(
                identity, "GetCurrentProcessId", core);
            var providerArtifact = TrRmExecutableCompiler.EmitStandard(
                compilation.Program!, identity, schemaIdentity, 1);
            WriteStandardFArtifactEvidence("fan-in-provider", identity,
                providerArtifact);
            providers[index] = new TrRmStandardArtifactInput(schemaIdentity,
                providerArtifact);
        }

        rootArtifact = AddRootMethodImports(rootArtifact, bindings,
            reuseModuleRefs: false, duplicateSecondMethodName: false);
        WriteStandardFArtifactEvidence("fan-in-root", rootIdentity,
            rootArtifact);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, providers);
        Assert.That(loaded.Functions.Count(static function =>
            function.ModuleImport is not null), Is.EqualTo(providerCount));
        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            rootInput, rootIdentity, providers,
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);
        Assert.That(plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable).Bindings,
            Has.Length.EqualTo(providerCount));
        var image = NativeModuleCompiler.EmitStandardExecutable(rootInput,
            rootIdentity, providers, NativeRuntimePolicy.FailFast);
        Assert.That(image.Take(2), Is.EqualTo([(byte)'M', (byte)'Z']));
        var imports = ReadPeImports(image,
            NativeEmitter.System32ImportPath("kernel32.dll"));
        TestContext.WriteLine(
            $"Fan-in PE imports: {string.Join(",", imports)}; image-bytes={image.Length}; sha256={Convert.ToHexString(SHA256.HashData(image))}");
        Assert.That(imports, Does.Contain("GetCurrentProcessId"));
    }

    [Test]
    public void StandardFProviderPlanRetainsOwnedArtifactSnapshots()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "Root";
        const string providerIdentity = "Owned";
        const string leafIdentity = "Leaf";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var rootBytes = CreateRootMethodImportArtifact(rootIdentity,
            "Owned", providerIdentity, "Owned", core);
        var providerBytes = CreateMixedImportProviderArtifact(
            providerIdentity, "Owned", "GetCurrentProcessId",
            "Leaf", "GetCurrentProcessId", leafIdentity, "Leaf", core);
        var leafCompilation = CreateMultipleImportProviderCompilation(
            "Leaf", "GetCurrentProcessId", core);
        var leafBytes = TrRmExecutableCompiler.EmitStandard(
            leafCompilation.Program!, leafIdentity, schemaIdentity, 1);
        var providers = new[]
        {
            new TrRmStandardArtifactInput(schemaIdentity, providerBytes),
            new TrRmStandardArtifactInput(schemaIdentity, leafBytes),
        };
        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            new TrRmStandardArtifactInput(schemaIdentity, rootBytes),
            rootIdentity, providers,
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);

        Array.Fill(rootBytes, (byte)0xCC);
        Array.Fill(providerBytes, (byte)0xCC);
        Array.Fill(leafBytes, (byte)0xCC);
        var (loweredProgram, bindings) = plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable);
        Assert.That(bindings.Select(static binding => binding.FunctionName),
            Is.EquivalentTo(["Owned"]));
        Assert.That(loweredProgram.Functions.Single(static function =>
            function.Name == "Owned").ModuleImport, Is.Null);
    }

    [Test]
    public void StandardFProviderClosureUsesTheSelectedPublicExportKey()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "Root";
        const string providerIdentity = "Owned";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var rootArtifact = CreateRootMethodImportArtifact(rootIdentity,
            "Owned", providerIdentity, "GetCurrentProcessId", core);
        rootArtifact = ReencodeFirstModuleRefIdentity(rootArtifact,
            providerIdentity, encoding: 2);
        var providerCompilation = CreateMultipleImportProviderCompilation(
            "Owned", "GetCurrentProcessId", core);
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, schemaIdentity, 1);
        var aliasProviderArtifact = AddMethodExportAlias(providerArtifact,
            "Owned", "GetCurrentProcessId");
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            aliasProviderArtifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, [providerInput]);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "Owned").DirectNativeImport!.ExportName,
            Is.EqualTo("GetCurrentProcessId"));
        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            rootInput, rootIdentity, [providerInput],
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);
        Assert.That(plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable).Bindings,
            Has.Length.EqualTo(1));

        var caseMismatchRootArtifact = CreateRootMethodImportArtifact(
            "CaseRoot", "owned", "owned", "GetCurrentProcessId", core);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    caseMismatchRootArtifact), "CaseRoot", [providerInput]));

        var normalizationRootArtifact = CreateRootMethodImportArtifact(
            "NormalizationRoot", "Owned", providerIdentity, "Owned", core);
        normalizationRootArtifact = ReencodeFirstModuleRefIdentity(
            normalizationRootArtifact, "e\u0301", encoding: 2);
        var normalizedProviderCompilation
            = CreateMultipleImportProviderCompilation(
                "Owned", "GetCurrentProcessId", core);
        var normalizedProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            normalizedProviderCompilation.Program!, "\u00E9",
            schemaIdentity, 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    normalizationRootArtifact), "NormalizationRoot", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        normalizedProviderArtifact),
                ]));

        const string caseKeyProviderIdentity = "GetCurrentProcessId";
        var caseKeyProviderCompilation
            = CreateMultipleImportProviderCompilation(
                "Owned", "GetCurrentProcessId", core);
        var caseKeyProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            caseKeyProviderCompilation.Program!, caseKeyProviderIdentity,
            schemaIdentity, 1);
        var caseKeyProviderInput = new TrRmStandardArtifactInput(schemaIdentity,
            AddMethodExportAlias(caseKeyProviderArtifact, "Owned",
                "GetCurrentProcessId"));
        var caseMismatchKeyArtifact = CreateRootMethodImportArtifact(
            "CaseKeyRoot", "getcurrentprocessid",
            caseKeyProviderIdentity, "getcurrentprocessid", core);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    caseMismatchKeyArtifact), "CaseKeyRoot",
                [caseKeyProviderInput]));
    }

    [Test]
    public void StandardFProviderClosureUsesExactUnicodeModuleIdentity()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "SupplementaryRoot";
        const string supplementaryProviderIdentity = "Provider-\U0001F680";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var providerCompilation = CreateMultipleImportProviderCompilation(
            "Owned", "GetCurrentProcessId", core);
        var supplementaryProviderArtifact =
            TrRmExecutableCompiler.EmitStandard(
                providerCompilation.Program!, supplementaryProviderIdentity,
                schemaIdentity, 1);
        var supplementaryRootArtifact = CreateRootMethodImportArtifact(
            rootIdentity, "Owned", "Owned", "Owned", core);
        supplementaryRootArtifact = ReencodeFirstModuleRefIdentity(
            supplementaryRootArtifact, supplementaryProviderIdentity,
            encoding: 2);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                supplementaryRootArtifact), rootIdentity, [
                new TrRmStandardArtifactInput(schemaIdentity,
                    supplementaryProviderArtifact),
            ]);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "Owned").ModuleImport!.ProducerIdentity,
            Is.EqualTo(supplementaryProviderIdentity));

        const string ringSymbolIdentity = "\u212B";
        const string composedRingIdentity = "\u00C5";
        var ringRootArtifact = CreateRootMethodImportArtifact(
            "RingRoot", "Owned", "Owned", "Owned", core);
        ringRootArtifact = ReencodeFirstModuleRefIdentity(
            ringRootArtifact, ringSymbolIdentity, encoding: 2);
        var composedRingProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, composedRingIdentity,
            schemaIdentity, 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    ringRootArtifact), "RingRoot", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        composedRingProviderArtifact),
                ]));
    }

    [TestCase(0x0001)]
    [TestCase(0x0010)]
    [TestCase(0x0011)]
    [TestCase(0x0016)]
    [TestCase(0x001C)]
    [TestCase(0x0020)]
    public void StandardFProviderClosureRejectsOverLimitSectionCounts(
        int sectionKind)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "CountLimitRoot";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var artifact = CreateRootMethodImportArtifact(rootIdentity,
            "Owned", "Owned", "Owned", core);
        var overLimitArtifact = ReplaceSectionRecordCount(artifact,
            sectionKind, 0x0100_0000);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    overLimitArtifact), rootIdentity, []));
    }

    [Test]
    public void StandardFProviderClosureSupportsSelfAndMutualDependencies()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var selfArtifact = CreateMixedImportProviderArtifact(
            "SelfDependency", "Owned", "GetCurrentProcessId",
            "SelfDependency", "GetCurrentProcessId", "SelfDependency",
            "Owned", core);
        WriteStandardFArtifactEvidence("self-edge", "SelfDependency",
            selfArtifact);
        var selfInput = new TrRmStandardArtifactInput(schemaIdentity,
            selfArtifact);
        var selfLoaded = TrRmExecutableCompiler.LoadStandard(selfInput,
            "SelfDependency", []);
        Assert.That(selfLoaded.Functions.Single(static function =>
            function.Name == "SelfDependency").ModuleImport!.ProducerIdentity,
            Is.EqualTo("SelfDependency"));
        var selfPlan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            selfInput, "SelfDependency", [],
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);
        var (_, selfImports) = selfPlan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable);
        Assert.That(selfImports.Select(static binding => binding.FunctionName),
            Is.EquivalentTo(["Owned", "SelfDependency"]));
        selfPlan.ValidateLibraryExports(["Owned"]);
        _ = Assert.Throws<ArgumentException>(() =>
            selfPlan.ValidateLibraryExports(["SelfDependency"]));

        var coLocatedProviderArtifact
            = CreateMixedImportProviderArtifact("Owned", "Owned",
                "GetCurrentProcessId", "SelfDependency",
                "GetCurrentProcessId", "Owned", "Owned", core);
        var coLocatedRootArtifact = CreateRootMethodImportArtifact(
            "SelfRoot", "Owned", "Owned", "Owned", core);
        var coLocatedArtifact = CombinePopulatedNativeAddressModules(
            coLocatedProviderArtifact, coLocatedRootArtifact);
        var coLocatedInput = new TrRmStandardArtifactInput(schemaIdentity,
            coLocatedArtifact);
        var coLocatedLoaded = TrRmExecutableCompiler.LoadStandard(
            coLocatedInput, "SelfRoot", []);
        Assert.That(coLocatedLoaded.Functions.Single(static function =>
            function.Name == "Owned").ModuleImport!.ProducerIdentity,
            Is.EqualTo("Owned"));
        var coLocatedPlan
            = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
                coLocatedInput, "SelfRoot", [],
                TrRmStandardNativeOperation.EmitExecutable,
                NativeRuntimePolicy.FailFast);
        Assert.That(coLocatedPlan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable).Bindings,
            Has.Length.EqualTo(1));

        var pArtifact = CreateMixedImportProviderArtifact("CycleP",
            "CycleP", "GetCurrentProcessId", "CycleQ",
            "GetCurrentProcessId", "CycleQ", "CycleQ", core);
        var qArtifact = CreateMixedImportProviderArtifact("CycleQ",
            "CycleQ", "GetCurrentProcessId", "CycleP",
            "GetCurrentProcessId", "CycleP", "CycleP", core);
        var pInput = new TrRmStandardArtifactInput(schemaIdentity, pArtifact);
        var qInput = new TrRmStandardArtifactInput(schemaIdentity, qArtifact);
        var pLoaded = TrRmExecutableCompiler.LoadStandard(pInput,
            "CycleP", [qInput]);
        Assert.That(pLoaded.Functions.Single(static function =>
            function.Name == "CycleQ").ModuleImport!.ProducerIdentity,
            Is.EqualTo("CycleQ"));
        var cyclePlan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            pInput, "CycleP", [qInput],
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);
        Assert.That(cyclePlan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable).Bindings,
            Has.Length.EqualTo(2));
    }

    [Test]
    public async Task StandardFSelfProviderExportThunkRemainsInvocableAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string moduleIdentity = "SelfDependency";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var artifact = CreateMixedImportProviderArtifact(moduleIdentity,
            "Owned", "GetCurrentProcessId", "SelfDependency",
            "GetCurrentProcessId", moduleIdentity, "Owned", core);
        var input = new TrRmStandardArtifactInput(schemaIdentity, artifact);
        var image = NativeModuleCompiler.EmitStandardLibrary(input,
            moduleIdentity, [], ["Owned"], NativeRuntimePolicy.FailFast);
        var exports = ReadPeExports(image);
        TestContext.WriteLine(
            $"Self-edge DLL exports: {string.Join(",", exports)}; image-bytes={image.Length}; sha256={Convert.ToHexString(SHA256.HashData(image))}");
        Assert.That(exports,
            Does.Contain("Owned").And.Not.Contain("SelfDependency"));
        _ = Assert.Throws<ArgumentException>(() =>
            NativeModuleCompiler.EmitStandardLibrary(input, moduleIdentity,
                [], ["SelfDependency"], NativeRuntimePolicy.FailFast));

        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-f-self-export-{Guid.NewGuid():N}.dll");
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            var handle = NativeLibrary.Load(path);
            try
            {
                var function = Marshal.GetDelegateForFunctionPointer<
                    NativeAddressExport>(NativeLibrary.GetExport(handle,
                    "Owned"));
                var result = function();
                TestContext.WriteLine(
                    $"Self-edge DLL Owned export result: {result}");
                Assert.That(result, Is.GreaterThan(0));
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public void StandardFMultipleImportsBindUncalledProviderBeforeLowering()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "multi-import-root";
        var (rootArtifact, firstProviderArtifact, secondProviderArtifact, _)
            = CreateMultipleRootMethodImportArtifacts();
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var firstProviderInput = new TrRmStandardArtifactInput(schemaIdentity,
            firstProviderArtifact);
        var secondProviderInput = new TrRmStandardArtifactInput(schemaIdentity,
            secondProviderArtifact);
        var providers = new[] { firstProviderInput, secondProviderInput };

        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, providers);
        var first = loaded.Functions.Single(static function =>
            function.Name == "First");
        var second = loaded.Functions.Single(static function =>
            function.Name == "Second");
        Assert.That(first.ModuleImport!.ProducerIdentity, Is.EqualTo("First"));
        Assert.That(second.ModuleImport!.ProducerIdentity, Is.EqualTo("Second"));
        Assert.That(first.ModuleImport.ExportKey, Is.EqualTo("First"));
        Assert.That(second.ModuleImport.ExportKey, Is.EqualTo("Second"));
        Assert.That(first.DirectNativeImport!.ExportName,
            Is.EqualTo("GetCurrentProcessId"));
        Assert.That(second.DirectNativeImport!.ExportName,
            Is.EqualTo("GetCurrentThreadId"));

        var caller = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var addresses = new List<BoundNativeFunctionAddress>();
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundNativeFunctionAddress address)
                {
                    addresses.Add(address);
                }
            }), Is.True);
        Assert.That(addresses, Has.Count.EqualTo(1));
        Assert.That(addresses[0].Function,
            Is.EqualTo(loaded.Functions.IndexOf(first)));

        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            rootInput, rootIdentity, providers,
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);
        var (loweredProgram, nativeImports) = plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable);
        Assert.That(nativeImports.Select(static binding =>
                binding.FunctionName),
            Is.EquivalentTo(["First", "Second"]));
        Assert.That(loweredProgram.Functions.Where(static function =>
                function.Name is "First" or "Second")
            .All(static function => function.ModuleImport is null
                && !function.IsModuleReference), Is.True);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity,
                [firstProviderInput]));
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var incompatibleProvider = CreateMultipleImportProviderCompilation(
            "Second", "GetTickCount", core);
        var incompatibleProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity,
            TrRmExecutableCompiler.EmitStandard(
                incompatibleProvider.Program!, "Second", schemaIdentity, 1));
        _ = Assert.Throws<InvalidDataException>(() =>
            NativeModuleCompiler.EmitStandardExecutable(rootInput,
                rootIdentity, [firstProviderInput, incompatibleProviderInput],
                NativeRuntimePolicy.FailFast));

        var image = NativeModuleCompiler.EmitStandardExecutable(rootInput,
            rootIdentity, providers, NativeRuntimePolicy.FailFast);
        Assert.That(image.Length, Is.GreaterThan(2));
        Assert.That(image[0], Is.EqualTo((byte)'M'));
        Assert.That(image[1], Is.EqualTo((byte)'Z'));
    }

    [Test]
    public void StandardFNativeOutputProviderBindingComparesOriginalContract()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string storageAddressSchemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-addresses-1";
        const string rootIdentity = "native-output-provider-root";
        const string providerIdentity = "ReadValue";
        var core = NativeCoreRegistrationTests.CreateRegisteredCompilationOptions();

        byte[] CreateArtifact(string moduleIdentity, string access,
            bool provider, string outputType = "uint32",
            string selectedSchemaIdentity = schemaIdentity,
            string libraryName = "example.dll",
            string nativeEntryName = "ReadValue",
            string methodName = "ReadValue")
        {
            var visibility = provider ? "public" : "internal";
            var caller = provider ? "" : $$"""
                int32 Main() {
                    init mut {{outputType}} value;
                    unsafe { return {{methodName}}(&init value); }
                }
                """;
            var source = $$"""
                [Terrum.Interop.NativeImport(
                    "{{libraryName}}", "{{nativeEntryName}}")]
                {{visibility}} extern int32 {{methodName}}(
                    [Terrum.Interop.NativeOutput(
                        When: Terrum.Interop.NativeOutputCondition.{{access}})]
                    write ptr<{{outputType}}> value);
                {{caller}}
                """;
            var compilation = provider
                ? Compilation.CreateLibrary(SyntaxTree.Parse(source), core)
                : Compilation.Create(SyntaxTree.Parse(source), core);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            return TrRmExecutableCompiler.EmitStandard(
                compilation.Program!, moduleIdentity, selectedSchemaIdentity,
                1);
        }

        var consumerArtifact = AddRootMethodImports(
            CreateArtifact(rootIdentity, "NonZero", provider: false),
            [("ReadValue", providerIdentity, "ReadValue")],
            reuseModuleRefs: false, duplicateSecondMethodName: false);
        var consumerInput = new TrRmStandardArtifactInput(schemaIdentity,
            consumerArtifact);
        var providerArtifact = CreateArtifact(providerIdentity, "NonZero",
            provider: true);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(consumerInput,
            rootIdentity, [providerInput]);
        var imported = loaded.Functions.Single(static function =>
            function.Name == "ReadValue");
        Assert.That(imported.ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(imported.NativeOutputContracts, Has.Length.EqualTo(1));

        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            rootIdentity, schemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemittedArtifact),
            rootIdentity, [providerInput]);
        Assert.That(reloaded.Functions.Single(static function =>
            function.Name == "ReadValue").ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        var image = NativeModuleCompiler.EmitStandardExecutable(consumerInput,
            rootIdentity, [providerInput], NativeRuntimePolicy.FailFast);
        Assert.That(image.Length, Is.GreaterThan(2));
        Assert.That(image[0], Is.EqualTo((byte)'M'));
        Assert.That(image[1], Is.EqualTo((byte)'Z'));

        var changedProvider = new TrRmStandardArtifactInput(schemaIdentity,
            CreateArtifact(providerIdentity, "NonNegative", provider: true));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(consumerInput,
                rootIdentity, [changedProvider]));

        var changedOutputType = new TrRmStandardArtifactInput(schemaIdentity,
            CreateArtifact(providerIdentity, "NonZero", provider: true,
                outputType: "uint64"));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(consumerInput,
                rootIdentity, [changedOutputType]));

        var changedForeignTarget = new TrRmStandardArtifactInput(schemaIdentity,
            CreateArtifact(providerIdentity, "NonZero", provider: true,
                libraryName: "other.dll"));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(consumerInput,
                rootIdentity, [changedForeignTarget]));

        byte[] CreateDependencyArtifact(string moduleIdentity,
            bool provider, bool declaresDependency)
        {
            var visibility = provider ? "public" : "internal";
            var dependencyAttributes = declaresDependency
                ? """
                    [param(value): DependsOn(nameof(source))]
                    [param(value): ForwardsBacking(nameof(source))]
                    """
                : "";
            var caller = provider ? "" : """
                public void Probe(ptr<int32> source) {
                    init mut uint32 value;
                    unsafe { ReadValue(source, &init value); }
                }
                """;
            var source = $$"""
                {{dependencyAttributes}}
                [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
                {{visibility}} extern int32 ReadValue(
                    ptr<int32> source,
                    [Terrum.Interop.NativeOutput(
                        When: Terrum.Interop.NativeOutputCondition.NonZero)]
                    write ptr<uint32> value);
                {{caller}}
                """;
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
                NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            return TrRmExecutableCompiler.EmitStandard(compilation.Program!,
                moduleIdentity, schemaIdentity, 1);
        }

        var dependencyConsumerArtifact = AddRootMethodImports(
            CreateDependencyArtifact(rootIdentity, provider: false,
                declaresDependency: true),
            [("ReadValue", providerIdentity, "ReadValue")],
            reuseModuleRefs: false, duplicateSecondMethodName: false);
        var dependencyConsumerInput = new TrRmStandardArtifactInput(
            schemaIdentity, dependencyConsumerArtifact);
        var matchingDependencyProvider = new TrRmStandardArtifactInput(
            schemaIdentity, CreateDependencyArtifact(providerIdentity,
                provider: true, declaresDependency: true));
        _ = TrRmExecutableCompiler.LoadStandard(dependencyConsumerInput,
            rootIdentity, [matchingDependencyProvider]);
        var reorderedDependencyProvider = new TrRmStandardArtifactInput(
            schemaIdentity, ReverseNativeOutputProviderRows(
                matchingDependencyProvider.Artifact.ToArray()));
        _ = TrRmExecutableCompiler.LoadStandard(dependencyConsumerInput,
            rootIdentity, [reorderedDependencyProvider]);
        var changedDependencyProvider = new TrRmStandardArtifactInput(
            schemaIdentity, CreateDependencyArtifact(providerIdentity,
                provider: true, declaresDependency: false));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(dependencyConsumerInput,
                rootIdentity, [changedDependencyProvider]));

        const string multiRootIdentity = "native-output-multi-provider-root";
        const string secondaryProviderIdentity = "ReadAux";
        var multiSource = """
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            internal extern int32 ReadValue(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<uint32> value);

            [Terrum.Interop.NativeImport("example.dll", "ReadAux")]
            internal extern int32 ReadAux(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<uint32> value);

            int32 Main() {
                init mut uint32 value;
                unsafe { return ReadValue(&init value); }
            }
            """;
        var multiCompilation = Compilation.Create(SyntaxTree.Parse(multiSource),
            core);
        Assert.That(multiCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, multiCompilation.Diagnostics));
        var multiRootArtifact = TrRmExecutableCompiler.EmitStandard(
            multiCompilation.Program!, multiRootIdentity, schemaIdentity, 1);
        var multiRootWithImports = AddRootMethodImports(multiRootArtifact, [
            ("ReadValue", providerIdentity, "ReadValue"),
            ("ReadAux", secondaryProviderIdentity, "ReadAux"),
        ], reuseModuleRefs: false, duplicateSecondMethodName: false);
        var multiRootInput = new TrRmStandardArtifactInput(schemaIdentity,
            multiRootWithImports);
        var primaryProviderInput = new TrRmStandardArtifactInput(schemaIdentity,
            CreateArtifact(providerIdentity, "NonZero", provider: true));
        var secondaryProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, CreateArtifact(secondaryProviderIdentity,
                "NonZero", provider: true, nativeEntryName: "ReadAux",
                methodName: "ReadAux"));
        var multiLoaded = TrRmExecutableCompiler.LoadStandard(multiRootInput,
            multiRootIdentity, [primaryProviderInput, secondaryProviderInput]);
        Assert.That(multiLoaded.Functions.Single(
            static function => function.Name == "ReadAux").ModuleImport!
            .ProducerIdentity, Is.EqualTo(secondaryProviderIdentity));

        var multiReemitted = TrRmExecutableCompiler.EmitStandard(multiLoaded,
            multiRootIdentity, schemaIdentity, 1);
        var multiReloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, multiReemitted),
            multiRootIdentity,
            [primaryProviderInput, secondaryProviderInput]);
        Assert.That(multiReloaded.Functions.Single(
            static function => function.Name == "ReadAux").ModuleImport!
            .ProducerIdentity, Is.EqualTo(secondaryProviderIdentity));
        var multiImage = NativeModuleCompiler.EmitStandardExecutable(
            multiRootInput, multiRootIdentity,
            [primaryProviderInput, secondaryProviderInput],
            NativeRuntimePolicy.FailFast);
        Assert.That(multiImage[0], Is.EqualTo((byte)'M'));
        Assert.That(multiImage[1], Is.EqualTo((byte)'Z'));

        var changedUncalledProvider = new TrRmStandardArtifactInput(
            schemaIdentity, CreateArtifact(secondaryProviderIdentity,
                "NonNegative", provider: true, nativeEntryName: "ReadAux",
                methodName: "ReadAux"));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(multiRootInput,
                multiRootIdentity,
                [primaryProviderInput, changedUncalledProvider]));

        var pointerConsumerArtifact = AddRootMethodImports(
            CreateArtifact(rootIdentity, "NonZero", provider: false,
                outputType: "ptr<void>"),
            [("ReadValue", providerIdentity, "ReadValue")],
            reuseModuleRefs: false, duplicateSecondMethodName: false);
        var pointerProviderInput = new TrRmStandardArtifactInput(schemaIdentity,
            CreateArtifact(providerIdentity, "NonZero", provider: true,
                outputType: "ptr<void>"));
        var pointerLoaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                pointerConsumerArtifact),
            rootIdentity, [pointerProviderInput]);
        Assert.That(pointerLoaded.Functions.Single(static function =>
            function.Name == "ReadValue").NativeOutputContracts.Single()
                .PointeeType,
            Is.EqualTo(PrimitiveType.NativeRawPointer));

        var changedPointerProvider = new TrRmStandardArtifactInput(
            schemaIdentity, CreateArtifact(providerIdentity, "NonZero",
                provider: true, outputType: "ptr<uint32>"));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    pointerConsumerArtifact),
                rootIdentity, [changedPointerProvider]));

        var storageAddressConsumerArtifact = AddRootMethodImports(
            CreateArtifact(rootIdentity, "NonZero", provider: false,
                selectedSchemaIdentity: storageAddressSchemaIdentity),
            [("ReadValue", providerIdentity, "ReadValue")],
            reuseModuleRefs: false, duplicateSecondMethodName: false,
            schema: TrRmArtifactSchema.ForeignInvocationStorageAddressDraft);
        var storageAddressProviderArtifact = CreateArtifact(providerIdentity,
            "NonZero", provider: true,
            selectedSchemaIdentity: storageAddressSchemaIdentity);
        var storageAddressProviderInput = new TrRmStandardArtifactInput(
            storageAddressSchemaIdentity, storageAddressProviderArtifact);
        var storageAddressLoaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(storageAddressSchemaIdentity,
                storageAddressConsumerArtifact),
            rootIdentity, [storageAddressProviderInput]);
        Assert.That(storageAddressLoaded.Functions.Single(
            static function => function.Name == "ReadValue")
            .ModuleImport!.ProducerIdentity, Is.EqualTo(providerIdentity));
        var coLocatedArtifact = CoLocateNativeOutputProvider(
            storageAddressConsumerArtifact, storageAddressProviderArtifact,
            TrRmArtifactSchema.ForeignInvocationStorageAddressDraft);
        var coLocatedInput = new TrRmStandardArtifactInput(
            storageAddressSchemaIdentity, coLocatedArtifact);
        var coLocatedLoaded = TrRmExecutableCompiler.LoadStandard(
            coLocatedInput, rootIdentity, []);
        Assert.That(coLocatedLoaded.Functions.Single(
            static function => function.Name == "ReadValue")
            .ModuleImport!.ProducerIdentity, Is.EqualTo(providerIdentity));
        var coLocatedImage = NativeModuleCompiler.EmitStandardExecutable(
            coLocatedInput, rootIdentity, [], NativeRuntimePolicy.FailFast);
        Assert.That(coLocatedImage[0], Is.EqualTo((byte)'M'));
        Assert.That(coLocatedImage[1], Is.EqualTo((byte)'Z'));

        var mixedProviderInput = new TrRmStandardArtifactInput(schemaIdentity,
            CreateArtifact(providerIdentity, "NonZero", provider: true));
        var mixedLoaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(storageAddressSchemaIdentity,
                storageAddressConsumerArtifact),
            rootIdentity, [mixedProviderInput]);
        Assert.That(mixedLoaded.Functions.Single(
            static function => function.Name == "ReadValue")
            .ModuleImport!.ProducerIdentity, Is.EqualTo(providerIdentity));

        var mixedReemitted = TrRmExecutableCompiler.EmitStandard(mixedLoaded,
            rootIdentity, storageAddressSchemaIdentity, 1);
        var mixedReloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(storageAddressSchemaIdentity,
                mixedReemitted),
            rootIdentity, [mixedProviderInput]);
        Assert.That(mixedReloaded.Functions.Single(
            static function => function.Name == "ReadValue")
            .ModuleImport!.ProducerIdentity, Is.EqualTo(providerIdentity));
        var unrelatedProviderInput = new TrRmStandardArtifactInput(
            storageAddressSchemaIdentity,
            CreateArtifact("unrelated-output-provider", "NonZero",
                provider: true,
                selectedSchemaIdentity: storageAddressSchemaIdentity));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(storageAddressSchemaIdentity,
                    storageAddressConsumerArtifact),
                rootIdentity,
                [storageAddressProviderInput, unrelatedProviderInput]));
    }

    [Test]
    public void StandardFNativeOutputProviderBindingChecksIntermediateEdges()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "native-output-closure-root";
        const string intermediateIdentity = "ReadValue";
        const string secondaryIntermediateIdentity = "ReadOther";
        const string leafIdentity = "example.dll";
        var core = NativeCoreRegistrationTests.CreateRegisteredCompilationOptions();
        var rootCompilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            internal extern int32 ReadValue(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<uint32> value);

            [Terrum.Interop.NativeImport("example.dll", "ReadOther")]
            internal extern int32 ReadOther(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<uint32> value);

            int32 Main() {
                init mut uint32 value;
                unsafe { return ReadValue(&init value); }
            }
            """), core);
        Assert.That(rootCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var rootArtifact = AddRootMethodImports(
            TrRmExecutableCompiler.EmitStandard(rootCompilation.Program!,
                rootIdentity, schemaIdentity, 1),
            [
                ("ReadValue", intermediateIdentity, "ReadValue"),
                ("ReadOther", secondaryIntermediateIdentity, "ReadOther"),
            ],
            reuseModuleRefs: false, duplicateSecondMethodName: false);

        var intermediateCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
                public extern int32 ReadValue(
                    [Terrum.Interop.NativeOutput(
                        When: Terrum.Interop.NativeOutputCondition.NonZero)]
                    write ptr<uint32> value);

                [Terrum.Interop.NativeImport("example.dll", "ReadAux")]
                internal extern int32 ReadAux(
                    [Terrum.Interop.NativeOutput(
                        When: Terrum.Interop.NativeOutputCondition.NonZero)]
                    write ptr<uint32> value);

                internal int32 Bind() {
                    init mut uint32 value;
                    unsafe { return ReadAux(&init value); }
                }
                """), core);
        Assert.That(intermediateCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                intermediateCompilation.Diagnostics));
        var intermediateArtifact = AddRootMethodImports(
            TrRmExecutableCompiler.EmitStandard(
                intermediateCompilation.Program!, intermediateIdentity,
                schemaIdentity, 1),
            [("ReadAux", leafIdentity, "ReadAux")],
            reuseModuleRefs: false, duplicateSecondMethodName: false);

        var secondaryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Terrum.Interop.NativeImport("example.dll", "ReadOther")]
                public extern int32 ReadOther(
                    [Terrum.Interop.NativeOutput(
                        When: Terrum.Interop.NativeOutputCondition.NonZero)]
                    write ptr<uint32> value);

                [Terrum.Interop.NativeImport("example.dll", "ReadAux")]
                internal extern int32 ReadAux(
                    [Terrum.Interop.NativeOutput(
                        When: Terrum.Interop.NativeOutputCondition.NonZero)]
                    write ptr<uint32> value);

                internal int32 BindOther() {
                    init mut uint32 value;
                    unsafe { return ReadAux(&init value); }
                }
                """), core);
        Assert.That(secondaryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                secondaryCompilation.Diagnostics));
        var secondaryArtifact = AddRootMethodImports(
            TrRmExecutableCompiler.EmitStandard(
                secondaryCompilation.Program!,
                secondaryIntermediateIdentity, schemaIdentity, 1),
            [("ReadAux", leafIdentity, "ReadAux")],
            reuseModuleRefs: false, duplicateSecondMethodName: false);
        var coLocatedIntermediateArtifact
            = CombinePopulatedNativeAddressModules(intermediateArtifact,
                secondaryArtifact, requireFunctionPointerAnchors: false);

        byte[] CreateStandaloneOutputProvider(string moduleIdentity,
            string methodName, string access)
        {
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
                [Terrum.Interop.NativeImport(
                    "example.dll", "{{methodName}}")]
                public extern int32 {{methodName}}(
                    [Terrum.Interop.NativeOutput(
                        When: Terrum.Interop.NativeOutputCondition.{{access}})]
                    write ptr<uint32> value);
                """), core);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            return TrRmExecutableCompiler.EmitStandard(compilation.Program!,
                moduleIdentity, schemaIdentity, 1);
        }

        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var intermediateInput = new TrRmStandardArtifactInput(schemaIdentity,
            intermediateArtifact);
        var secondaryInput = new TrRmStandardArtifactInput(schemaIdentity,
            secondaryArtifact);
        var coLocatedIntermediateInput = new TrRmStandardArtifactInput(
            schemaIdentity, coLocatedIntermediateArtifact);
        var leafInput = new TrRmStandardArtifactInput(schemaIdentity,
            CreateStandaloneOutputProvider(leafIdentity, "ReadAux",
                "NonZero"));
        var intermediateLoaded = TrRmExecutableCompiler.LoadStandard(
            intermediateInput, intermediateIdentity, [leafInput]);
        var reemittedIntermediate = TrRmExecutableCompiler.EmitStandard(
            intermediateLoaded, intermediateIdentity, schemaIdentity, 1);
        var reloadedIntermediate = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reemittedIntermediate),
            intermediateIdentity, [leafInput]);
        var uncalledOutput = reloadedIntermediate.Functions.Single(
            static function => function.Name == "ReadValue");
        Assert.That(uncalledOutput.IsPrivate || uncalledOutput.IsInternal,
            Is.False);
        Assert.That(reloadedIntermediate.Functions.Single(
            static function => function.Name == "ReadAux")
            .ModuleImport!.ProducerIdentity, Is.EqualTo(leafIdentity));
        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, [intermediateInput, secondaryInput, leafInput]);
        Assert.That(loaded.Functions.Single(
            static function => function.Name == "ReadValue")
            .ModuleImport!.ProducerIdentity, Is.EqualTo(intermediateIdentity));
        Assert.That(loaded.Functions.Single(
            static function => function.Name == "ReadOther")
            .ModuleImport!.ProducerIdentity,
            Is.EqualTo(secondaryIntermediateIdentity));
        var coLocatedLoaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, [coLocatedIntermediateInput, leafInput]);
        Assert.That(coLocatedLoaded.Functions.Single(
            static function => function.Name == "ReadOther")
            .ModuleImport!.ProducerIdentity,
            Is.EqualTo(secondaryIntermediateIdentity));
        var image = NativeModuleCompiler.EmitStandardExecutable(rootInput,
            rootIdentity, [intermediateInput, secondaryInput, leafInput],
            NativeRuntimePolicy.FailFast);
        Assert.That(image[0], Is.EqualTo((byte)'M'));
        Assert.That(image[1], Is.EqualTo((byte)'Z'));
        var coLocatedImage = NativeModuleCompiler.EmitStandardExecutable(
            rootInput, rootIdentity,
            [coLocatedIntermediateInput, leafInput],
            NativeRuntimePolicy.FailFast);
        Assert.That(coLocatedImage[0], Is.EqualTo((byte)'M'));
        Assert.That(coLocatedImage[1], Is.EqualTo((byte)'Z'));

        var changedLeaf = new TrRmStandardArtifactInput(schemaIdentity,
            CreateStandaloneOutputProvider(leafIdentity, "ReadAux",
                "NonNegative"));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity,
                [intermediateInput, secondaryInput, changedLeaf]));
    }

    [Test]
    public void StandardFSelfProviderImportBindsAndReemitsItsOwnedDeclaration()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string moduleIdentity = "native-output-self-provider";
        var core = NativeCoreRegistrationTests.CreateRegisteredCompilationOptions();
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "GetValue")]
            public extern int32 GetValue(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<uint32> value);

            [Terrum.Interop.NativeImport("example.dll", "GetValue")]
            internal extern int32 ReadValue(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<uint32> value);

            int32 Main() {
                init mut uint32 value;
                unsafe { return ReadValue(&init value); }
            }
            """), core);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = AddRootMethodImports(
            TrRmExecutableCompiler.EmitStandard(compilation.Program!,
                moduleIdentity, schemaIdentity, 1),
            [("ReadValue", moduleIdentity, "GetValue")],
            reuseModuleRefs: false, duplicateSecondMethodName: false);
        var input = new TrRmStandardArtifactInput(schemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        Assert.That(loaded.Functions.Single(
            static function => function.Name == "ReadValue")
            .ModuleImport!.ProducerIdentity, Is.EqualTo(moduleIdentity));

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            moduleIdentity, []);
        Assert.That(reloaded.Functions.Single(
            static function => function.Name == "ReadValue")
            .ModuleImport!.ProducerIdentity, Is.EqualTo(moduleIdentity));
    }

    [Test]
    public void StandardFNativeOutputProviderClosureValidatesAdmittedCycle()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "native-output-cycle-root";
        const string firstIdentity = "ReadValue";
        const string secondIdentity = "ReadAux";
        var core = NativeCoreRegistrationTests.CreateRegisteredCompilationOptions();

        byte[] CreateProvider(string moduleIdentity, string nextProvider)
        {
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
                [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
                public extern int32 ReadValue(
                    [Terrum.Interop.NativeOutput(
                        When: Terrum.Interop.NativeOutputCondition.NonZero)]
                    write ptr<uint32> value);

                [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
                internal extern int32 ReadAux(
                    [Terrum.Interop.NativeOutput(
                        When: Terrum.Interop.NativeOutputCondition.NonZero)]
                    write ptr<uint32> value);

                internal int32 Bind() {
                    init mut uint32 value;
                    unsafe { return ReadAux(&init value); }
                }
                """), core);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            var artifact = TrRmExecutableCompiler.EmitStandard(
                compilation.Program!, moduleIdentity, schemaIdentity, 1);
            return AddRootMethodImports(artifact,
                [("ReadAux", nextProvider, "ReadValue")],
                reuseModuleRefs: false, duplicateSecondMethodName: false);
        }

        var rootCompilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            internal extern int32 ReadValue(
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<uint32> value);

            int32 Main() {
                init mut uint32 value;
                unsafe { return ReadValue(&init value); }
            }
            """), core);
        Assert.That(rootCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var rootArtifact = AddRootMethodImports(
            TrRmExecutableCompiler.EmitStandard(rootCompilation.Program!,
                rootIdentity, schemaIdentity, 1),
            [("ReadValue", firstIdentity, "ReadValue")],
            reuseModuleRefs: false, duplicateSecondMethodName: false);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var firstInput = new TrRmStandardArtifactInput(schemaIdentity,
            CreateProvider(firstIdentity, secondIdentity));
        var secondInput = new TrRmStandardArtifactInput(schemaIdentity,
            CreateProvider(secondIdentity, firstIdentity));

        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, [firstInput, secondInput]);
        Assert.That(loaded.Functions.Single(
            static function => function.Name == "ReadValue")
            .ModuleImport!.ProducerIdentity, Is.EqualTo(firstIdentity));
        var image = NativeModuleCompiler.EmitStandardExecutable(rootInput,
            rootIdentity, [firstInput, secondInput],
            NativeRuntimePolicy.FailFast);
        Assert.That(image[0], Is.EqualTo((byte)'M'));
        Assert.That(image[1], Is.EqualTo((byte)'Z'));
    }

    [TestCase(true, 1)]
    [TestCase(false, 2)]
    public void StandardFMultipleImportsPermitModuleRefReuse(
        bool reuseModuleRef, int expectedModuleRefCount)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "multi-import-root";
        var (rootArtifact, firstProviderArtifact, _, _)
            = CreateMultipleRootMethodImportArtifacts(
                "GetCurrentProcessId", "First", "First", reuseModuleRef);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var firstProviderInput = new TrRmStandardArtifactInput(schemaIdentity,
            firstProviderArtifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(rootArtifact));
        Assert.That(tables.ModuleRefs.Count, Is.EqualTo(expectedModuleRefCount));
        Assert.That(tables.Imports.Count, Is.EqualTo(2));

        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, [firstProviderInput]);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "First").ModuleImport!.ProducerIdentity,
            Is.EqualTo("First"));
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "Second").ModuleImport!.ProducerIdentity,
            Is.EqualTo("First"));
    }

    [Test]
    public void StandardFMultipleImportsRejectSameRootMethodNames()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "multi-import-root";
        var (rootArtifact, firstProviderArtifact, secondProviderArtifact, _)
            = CreateMultipleRootMethodImportArtifacts(
                duplicateSecondMethodName: true);
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity, rootArtifact),
                rootIdentity, [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        firstProviderArtifact),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        secondProviderArtifact),
                ]));
    }

    [Test]
    public void StandardFMultipleImportsSupportThreeProviderBindings()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "multi-import-root";
        var (rootArtifact, firstProviderArtifact, secondProviderArtifact,
            thirdProviderArtifact)
            = CreateMultipleRootMethodImportArtifacts(importCount: 3);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var providers = new[]
        {
            new TrRmStandardArtifactInput(schemaIdentity,
                firstProviderArtifact),
            new TrRmStandardArtifactInput(schemaIdentity,
                secondProviderArtifact),
            new TrRmStandardArtifactInput(schemaIdentity,
                thirdProviderArtifact),
        };
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(rootArtifact));
        Assert.That(tables.Imports.Count, Is.EqualTo(3));
        Assert.That(tables.ModuleRefs.Count, Is.EqualTo(3));

        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, providers);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "Second").ModuleImport!.ProducerIdentity,
            Is.EqualTo("Second"));
        var third = loaded.Functions.Single(static function =>
            function.Name == "Third");
        Assert.That(third.ModuleImport!.ProducerIdentity,
            Is.EqualTo("Third"));
        Assert.That(third.DirectNativeImport!.ExportName,
            Is.EqualTo("GetTickCount"));
        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            rootInput, rootIdentity, providers,
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);
        var (_, nativeImports) = plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable);
        Assert.That(nativeImports.Select(static binding =>
                binding.FunctionName),
            Is.EquivalentTo(["First", "Second", "Third"]));
    }

    [Test]
    public void StandardFMultipleImportsSelectNonFirstAddressTarget()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "multi-import-root";
        var (rootArtifact, firstProviderArtifact, secondProviderArtifact,
            thirdProviderArtifact) = CreateMultipleRootMethodImportArtifacts(
                importCount: 3, targetMethodName: "Third");
        WriteStandardFArtifactEvidence("non-first-target-root", rootIdentity,
            rootArtifact);
        WriteStandardFArtifactEvidence("non-first-target-first-provider",
            "First", firstProviderArtifact);
        WriteStandardFArtifactEvidence("non-first-target-second-provider",
            "Second", secondProviderArtifact);
        WriteStandardFArtifactEvidence("non-first-target-third-provider",
            "Third", thirdProviderArtifact);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var providers = new[]
        {
            new TrRmStandardArtifactInput(schemaIdentity,
                firstProviderArtifact),
            new TrRmStandardArtifactInput(schemaIdentity,
                secondProviderArtifact),
            new TrRmStandardArtifactInput(schemaIdentity,
                thirdProviderArtifact),
        };
        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, providers);
        var caller = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var addresses = new List<BoundNativeFunctionAddress>();
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundNativeFunctionAddress address)
                {
                    addresses.Add(address);
                }
            }), Is.True);
        Assert.That(addresses, Has.Count.EqualTo(1));
        Assert.That(loaded.Functions[addresses[0].Function].Name,
            Is.EqualTo("Third"));

        var image = NativeModuleCompiler.EmitStandardExecutable(rootInput,
            rootIdentity, providers, NativeRuntimePolicy.FailFast);
        Assert.That(ReadPeImports(image,
                NativeEmitter.System32ImportPath("kernel32.dll")),
            Does.Contain("GetTickCount"));
        var jitResult = NativeModuleCompiler.ExecuteStandard(rootInput,
            rootIdentity, providers, NativeRuntimePolicy.FailFast);
        TestContext.WriteLine($"Non-first NativeAddress JIT result: {jitResult}");
        Assert.That(jitResult, Is.GreaterThan(0));
    }

    [Test]
    public void StandardFMultipleImportJitExecutesSelectedProvider()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "multi-import-root";
        var (rootArtifact, firstProviderArtifact, secondProviderArtifact, _)
            = CreateMultipleRootMethodImportArtifacts();
        var result = NativeModuleCompiler.ExecuteStandard(
            new TrRmStandardArtifactInput(schemaIdentity, rootArtifact),
            rootIdentity, [
                new TrRmStandardArtifactInput(schemaIdentity,
                    firstProviderArtifact),
                new TrRmStandardArtifactInput(schemaIdentity,
                    secondProviderArtifact),
            ], NativeRuntimePolicy.FailFast);
        Assert.That(result, Is.GreaterThan(0));
    }

    [Test]
    public void ReconstructsFProfileExecutableEntry()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetCurrentProcessId",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern int32 GetCurrentProcessId();

            int32 Main() {
                unsafe {
                    var callback = &GetCurrentProcessId;
                    return callback();
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        const string moduleIdentity = "native-address-executable";
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        Assert.That(tables.Modules.GetEntryMethod(1), Is.GreaterThan(0));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        Assert.That(loaded.Functions[loaded.EntryPoint].Name,
            Is.EqualTo("Main"));
        Assert.That(TrRmExecutableCompiler.EmitStandard(
            loaded, moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast),
            Is.GreaterThan(0));
    }

    internal static (byte[] RootArtifact, byte[] FirstProviderArtifact,
        byte[] SecondProviderArtifact, byte[] ThirdProviderArtifact)
        CreateMultipleRootMethodImportArtifacts(
        string secondRootImportExport = "GetCurrentThreadId",
        string secondProducerIdentity = "Second",
        string secondExportKey = "Second", bool reuseModuleRef = false,
        bool duplicateSecondMethodName = false, int importCount = 2,
        string targetMethodName = "First")
    {
        if (importCount is < 2 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(importCount));
        }

        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "multi-import-root";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var firstProvider = CreateMultipleImportProviderCompilation(
            "First", "GetCurrentProcessId", core);
        var secondProvider = CreateMultipleImportProviderCompilation(
            "Second", "GetCurrentThreadId", core);
        var thirdProvider = CreateMultipleImportProviderCompilation(
            "Third", "GetTickCount", core);
        var thirdDeclaration = importCount == 3
            ? """
              [Terrum.Interop.NativeImport(
                  "kernel32.dll",
                  "GetTickCount",
                  CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
              internal extern int32 Third();
              """
            : string.Empty;
        var root = Compilation.Create(SyntaxTree.Parse($$"""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetCurrentProcessId",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern int32 First();

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "{{secondRootImportExport}}",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern int32 Second();

            {{thirdDeclaration}}

            int32 Main() {
                unsafe {
                    var callback = &{{targetMethodName}};
                    return callback();
                }
            }
            """), core);
        Assert.That(root.HasErrors, Is.False,
            string.Join(Environment.NewLine, root.Diagnostics));

        var rootArtifact = TrRmExecutableCompiler.EmitStandard(
            root.Program!, rootIdentity, schemaIdentity, 1);
        var importBindings = new List<(string MethodName,
            string ProducerIdentity, string ExportKey)>
        {
            ("First", "First", "First"),
            ("Second", secondProducerIdentity, secondExportKey),
        };
        if (importCount == 3)
        {
            importBindings.Add(("Third", "Third", "Third"));
        }

        rootArtifact = AddRootMethodImports(rootArtifact, importBindings,
            reuseModuleRef, duplicateSecondMethodName);

        return (rootArtifact,
            TrRmExecutableCompiler.EmitStandard(firstProvider.Program!,
                "First", schemaIdentity, 1),
            TrRmExecutableCompiler.EmitStandard(secondProvider.Program!,
                "Second", schemaIdentity, 1),
            TrRmExecutableCompiler.EmitStandard(thirdProvider.Program!,
                "Third", schemaIdentity, 1));
    }

    internal static Compilation CreateMultipleImportProviderCompilation(
        string methodName, string nativeExport,
        CompilationReference core, string providerBodyName = "ProviderBody")
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "{{nativeExport}}",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            public extern int32 {{methodName}}();

            internal int32 {{providerBodyName}}() {
                unsafe {
                    var callback = &{{methodName}};
                    return callback();
                }
            }
            """), core);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return compilation;
    }

    private static byte[] AddRootMethodImports(byte[] artifact,
        List<(string MethodName, string ProducerIdentity,
            string ExportKey)> bindings, bool reuseModuleRefs,
        bool duplicateSecondMethodName,
        TrRmArtifactSchema schema = TrRmArtifactSchema.ForeignInvocationDraft)
    {
        var index = schema
            == TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
                ? TrRmArtifactIndex
                    .ForForeignInvocationStorageAddressDraft(artifact)
                : TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = schema
            == TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
                ? TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(
                        index)
                : TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        Assert.That(tables.Modules.Count, Is.EqualTo(1));
        Assert.That(tables.ModuleRefs.Count, Is.Zero);
        Assert.That(tables.Imports.Count, Is.Zero);
        Assert.That(bindings, Is.Not.Empty);
        Assert.That(index.TryGetSection(0x0001, out var moduleBytes,
            out var moduleCount), Is.True);
        Assert.That(index.TryGetSection(0x0016, out var methodSectionBytes,
            out var methodCount), Is.True);
        Assert.That(index.TryGetSection(0x0002, out _, out var stringCount),
            Is.True);

        var moduleRefCount = reuseModuleRefs
            ? bindings.Select(static binding =>
                binding.ProducerIdentity).Distinct(StringComparer.Ordinal)
                .Count()
            : bindings.Count;
        var modules = new TrRmModuleTableReader(moduleBytes, moduleCount,
            tables.Strings, moduleRefCount, tables.Modules.TypeDefCount,
            tables.Modules.MethodDefCount, tables.Modules.SourceFileCount,
            tables.Features.Count);
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(1);
        var moduleRefRows = new List<TrRmModuleRefRow>();
        var moduleRefRowByIdentity = new Dictionary<string, int>(
            StringComparer.Ordinal);
        var importRows = new List<TrRmImportRow>(bindings.Count);
        foreach (var (MethodName, ProducerIdentity, ExportKey) in bindings)
        {
            var producerStringRow = FindStringRow(tables.Strings,
                stringCount, ProducerIdentity);
            var moduleRefRow = 0;
            if (reuseModuleRefs)
            {
                _ = moduleRefRowByIdentity.TryGetValue(
                    ProducerIdentity, out moduleRefRow);
            }

            if (moduleRefRow == 0)
            {
                moduleRefRows.Add(new TrRmModuleRefRow(producerStringRow,
                    targetRequirement, runtimePolicy));
                moduleRefRow = moduleRefRows.Count;
                if (reuseModuleRefs)
                {
                    moduleRefRowByIdentity.Add(ProducerIdentity,
                        moduleRefRow);
                }
            }

            var expectedMethodRow = 0;
            for (var methodRow = 1; methodRow <= tables.Methods.Count;
                methodRow++)
            {
                if (tables.Methods.GetOwnerModule(methodRow) == 1
                    && tables.Methods.GetName(methodRow)
                        == MethodName
                    && tables.StandardNativeImports
                        .HasImportForMethod(methodRow))
                {
                    if (expectedMethodRow != 0)
                    {
                        throw new InvalidDataException(
                            "The test artifact has duplicate NativeImport MethodDefs.");
                    }

                    expectedMethodRow = methodRow;
                }
            }

            Assert.That(expectedMethodRow, Is.GreaterThan(0));
            var exportKeyStringRow = FindStringRow(tables.Strings,
                stringCount, ExportKey);
            importRows.Add(new TrRmImportRow(moduleRefRow,
                exportKeyStringRow,
                0x1600_0000u | (uint)expectedMethodRow));
        }

        var moduleRefBytes = TrRmModuleRefTableWriter.Encode(moduleRefRows,
            tables.Strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes,
            moduleRefRows.Count, tables.Strings, modules);
        var importBytes = TrRmImportTableWriter.Encode(importRows,
            tables.Strings, moduleRefs, tables.TypeDefs, tables.Methods,
            default, default, default, default, tables.Features, modules,
            schema);
        var methodBytes = methodSectionBytes.ToArray();
        if (duplicateSecondMethodName)
        {
            var firstNameStringRow = FindStringRow(tables.Strings,
                stringCount, "First");
            var secondMethodRow = 0;
            for (var methodRow = 1; methodRow <= tables.Methods.Count;
                methodRow++)
            {
                if (tables.Methods.GetName(methodRow) == "Second"
                    && tables.StandardNativeImports
                        .HasImportForMethod(methodRow))
                {
                    secondMethodRow = methodRow;
                    break;
                }
            }

            Assert.That(secondMethodRow, Is.GreaterThan(0));
            var methodRows = new List<TrRmMethodDefRow>(methodCount);
            for (var methodRow = 1; methodRow <= methodCount; methodRow++)
            {
                var row = methodSectionBytes.Slice((methodRow - 1) * 28, 28);
                var nameStringRow = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(row[4..]));
                if (methodRow == secondMethodRow)
                {
                    nameStringRow = firstNameStringRow;
                }

                methodRows.Add(new TrRmMethodDefRow(
                    checked((int)BinaryPrimitives.ReadUInt32LittleEndian(row)),
                    nameStringRow,
                    checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                        row[8..])),
                    BinaryPrimitives.ReadUInt32LittleEndian(row[12..]),
                    checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                        row[16..])),
                    checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                        row[20..])),
                    checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                        row[24..]))));
            }

            methodBytes = TrRmMethodDefTableWriter.Encode(methodRows,
                tables.Strings, modules, tables.TypeDefs, tables.Signatures,
                tables.Contracts.Count, tables.Bodies.Count);
        }

        var sections = new List<TrRmSection>(index.SectionCount + 2);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            if (kind is 0x0010 or 0x001C)
            {
                Assert.That(recordCount, Is.Zero);
                continue;
            }

            sections.Add(kind == 0x0016 && duplicateSecondMethodName
                ? new TrRmSection(kind, flags, (uint)recordCount,
                    methodBytes)
                : new TrRmSection(kind, flags, (uint)recordCount,
                    bytes.ToArray()));
        }

        sections.Add(new TrRmSection(0x0010, 0,
            (uint)moduleRefRows.Count, moduleRefBytes));
        sections.Add(new TrRmSection(0x001C, 0,
            (uint)importRows.Count, importBytes));
        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var result = TrRmArtifactWriter.Encode(sections,
            schema);
        var resultIndex = schema
            == TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
                ? TrRmArtifactIndex
                    .ForForeignInvocationStorageAddressDraft(result)
                : TrRmArtifactIndex.ForForeignInvocationDraft(result);
        var resultTables = schema
            == TrRmArtifactSchema.ForeignInvocationStorageAddressDraft
                ? TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(
                        resultIndex)
                : TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationDraftAndGetExecutionTables(
                        resultIndex);
        Assert.That(resultTables.ModuleRefs.Count,
            Is.EqualTo(moduleRefRows.Count));
        Assert.That(resultTables.Imports.Count, Is.EqualTo(importRows.Count));
        for (var methodRow = 1; methodRow <= resultTables.Methods.Count;
            methodRow++)
        {
            if (resultTables.Methods.GetBodyRow(methodRow) != 0)
            {
                resultTables.ValidateRegisterBodyMapsForMethod(methodRow);
            }
        }

        return result;
    }

    private static byte[] CoLocateNativeOutputProvider(
        byte[] consumerArtifact, byte[] providerArtifact,
        TrRmArtifactSchema schema)
    {
        Assert.That(schema,
            Is.EqualTo(TrRmArtifactSchema.ForeignInvocationStorageAddressDraft));
        var consumerIndex
            = TrRmArtifactIndex.ForForeignInvocationStorageAddressDraft(
                consumerArtifact);
        var providerIndex
            = TrRmArtifactIndex.ForForeignInvocationStorageAddressDraft(
                providerArtifact);
        var consumer = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(
                consumerIndex);
        var provider = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(
                providerIndex);
        Assert.That(consumer.Modules.Count, Is.EqualTo(1));
        Assert.That(provider.Modules.Count, Is.EqualTo(1));
        Assert.That(provider.ModuleRefs.Count, Is.Zero);
        Assert.That(provider.Imports.Count, Is.Zero);
        Assert.That(provider.Methods.Count, Is.EqualTo(1));
        Assert.That(provider.Bodies.Count, Is.Zero);
        Assert.That(provider.TypeDefs.Count, Is.Zero);
        Assert.That(consumer.Blobs.Count, Is.Zero);
        Assert.That(provider.Blobs.Count, Is.Zero);
        Assert.That(provider.Methods.GetBodyRow(1), Is.Zero);

        var stringRows = new List<(string Text, int Encoding)>();
        static void AppendStrings(TrRmStringTableReader table,
            List<(string Text, int Encoding)> destination)
        {
            for (var row = 1; row <= table.Count; row++)
            {
                destination.Add((table.DecodeNamespace(row), 0));
            }
        }

        AppendStrings(consumer.Strings, stringRows);
        var consumerStringCount = consumer.Strings.Count;
        AppendStrings(provider.Strings, stringRows);
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(stringsBytes, stringRows.Count);
        var rootIdentity = consumer.Modules.GetIdentity(1);
        var providerIdentity = provider.Modules.GetIdentity(1);
        var rootIdentityStringRow = FindStringRow(strings, stringRows.Count,
            rootIdentity);
        var providerIdentityStringRow = consumerStringCount
            + FindStringRow(provider.Strings, provider.Strings.Count,
                providerIdentity);

        var featureRows = new List<TrRmFeatureRow>(
            consumer.Features.Count);
        for (var row = 1; row <= consumer.Features.Count; row++)
        {
            featureRows.Add(consumer.Features.GetFeature(row));
        }

        Assert.That(provider.Features.Count, Is.EqualTo(consumer.Features.Count));
        for (var row = 1; row <= provider.Features.Count; row++)
        {
            Assert.That(provider.Features.GetFeature(row),
                Is.EqualTo(consumer.Features.GetFeature(row)));
        }

        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureRows.Count, schema);

        var moduleRefRows = new List<TrRmModuleRefRow>(
            consumer.ModuleRefs.Count);
        for (var row = 1; row <= consumer.ModuleRefs.Count; row++)
        {
            var producerIdentity = consumer.ModuleRefs.GetProducerIdentity(row);
            var producerStringRow = FindStringRow(consumer.Strings,
                consumer.Strings.Count, producerIdentity);
            var (targetRequirement, runtimePolicy)
                = consumer.ModuleRefs.GetRequirements(row);
            moduleRefRows.Add(new TrRmModuleRefRow(producerStringRow,
                targetRequirement, runtimePolicy));
        }

        var (rootModuleRefStart, _) = consumer.Modules.GetModuleRefRange(1);
        var (rootTypeDefStart, _) = consumer.Modules.GetTypeDefRange(1);
        var (rootMethodStart, _) = consumer.Modules.GetMethodDefRange(1);
        var (rootSourceFileStart, _) = consumer.Modules.GetSourceFileRange(1);
        var (rootFeatureStart, rootFeatureCount)
            = consumer.Modules.GetFeatureRange(1);
        var (providerTargetRequirement, providerRuntimePolicy)
            = provider.Modules.GetRequirements(1);
        var moduleRows = new[] {
            new TrRmModuleRow(rootIdentityStringRow,
                consumer.Modules.GetRequirements(1).TargetRequirement,
                consumer.Modules.GetRequirements(1).RuntimePolicy,
                consumer.Modules.GetEntryMethod(1),
                rootModuleRefStart, rootTypeDefStart, rootMethodStart,
                rootSourceFileStart, rootFeatureStart, rootFeatureCount),
            new TrRmModuleRow(providerIdentityStringRow,
                providerTargetRequirement, providerRuntimePolicy, 0,
                consumer.ModuleRefs.Count + 1,
                consumer.TypeDefs.Count + 1,
                consumer.Methods.Count + 1,
                consumer.Modules.SourceFileCount + 1,
                rootFeatureStart, rootFeatureCount),
        };
        var moduleBytes = TrRmModuleTableWriter.Encode(moduleRows, strings,
            moduleRefRows.Count, 0,
            consumer.Methods.Count + provider.Methods.Count, 0,
            featureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, moduleRows.Length,
            strings, moduleRefRows.Count, 0,
            consumer.Methods.Count + provider.Methods.Count, 0,
            featureRows.Count);
        var moduleRefBytes = TrRmModuleRefTableWriter.Encode(moduleRefRows,
            strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes,
            moduleRefRows.Count, strings, modules);

        var typeSpecRows = new List<ReadOnlyMemory<byte>>(
            consumer.TypeSpecs.Count + provider.TypeSpecs.Count);
        for (var row = 1; row <= consumer.TypeSpecs.Count; row++)
        {
            typeSpecRows.Add(consumer.TypeSpecs
                .GetTypeSpecBytes(checked((uint)row)).ToArray());
        }

        for (var row = 1; row <= provider.TypeSpecs.Count; row++)
        {
            typeSpecRows.Add(provider.TypeSpecs
                .GetTypeSpecBytes(checked((uint)row)).ToArray());
        }

        static uint RemapTypeSpec(uint typeSpecRef, int offset)
        {
            return typeSpecRef != 0 && (typeSpecRef >> 24) == 0
                ? checked(typeSpecRef + (uint)offset) : typeSpecRef;
        }
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            0, moduleRefRows.Count, 0, 0, schema,
            methodDefCount: consumer.Methods.Count + provider.Methods.Count);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, 0, moduleRefRows.Count, 0, 0, schema,
            consumer.Methods.Count + provider.Methods.Count);

        var parameterRows = new List<TrRmParamRow>(
            consumer.Signatures.ParamCount + provider.Signatures.ParamCount);
        var signatureRows = new List<TrRmMethodSigRow>(
            consumer.Signatures.Count + provider.Signatures.Count);
        static void AppendSignatures(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int signatureOffset, int parameterOffset, int typeSpecOffset,
            List<TrRmMethodSigRow> signatureRows,
            List<TrRmParamRow> parameterRows)
        {
            for (var signature = 1;
                signature <= tables.Signatures.Count; signature++)
            {
                var (parameterStart, parameterCount)
                    = tables.Signatures.GetParamRange(signature);
                var (genericStart, genericCount)
                    = tables.Signatures.GetGenericRange(signature);
                var newSignatureRow = signatureOffset + signature;
                signatureRows.Add(new TrRmMethodSigRow(
                    RemapTypeSpec(
                        tables.Signatures.GetResultTypeSpec(signature),
                        typeSpecOffset),
                    RemapTypeSpec(
                        tables.Signatures.GetReceiverTypeSpec(signature),
                        typeSpecOffset),
                    parameterCount == 0 ? 0
                        : parameterOffset + parameterStart,
                    parameterCount,
                    genericCount == 0 ? 0 : genericStart,
                    genericCount));
                for (var ordinal = 0; ordinal < parameterCount; ordinal++)
                {
                    var sourceParameter = parameterStart + ordinal;
                    parameterRows.Add(new TrRmParamRow(newSignatureRow,
                        ordinal,
                        RemapTypeSpec(
                            tables.Parameters.GetTypeSpec(sourceParameter),
                            typeSpecOffset),
                        tables.Parameters.GetMode(sourceParameter),
                        tables.Parameters.GetFlags(sourceParameter)));
                }
            }
        }

        AppendSignatures(consumer, 0, 0, 0, signatureRows, parameterRows);
        AppendSignatures(provider, consumer.Signatures.Count,
            consumer.Signatures.ParamCount, consumer.TypeSpecs.Count,
            signatureRows, parameterRows);
        var signatureBytes = TrRmMethodSigTableWriter.Encode(signatureRows,
            typeSpecs, parameterRows.Count, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            signatureRows.Count, typeSpecs, parameterRows.Count, 0);
        var parameterBytes = TrRmParamTableWriter.Encode(parameterRows,
            signatures, typeSpecs, schema);
        var parameters = new TrRmParamTableReader(parameterBytes,
            parameterRows.Count, signatures, typeSpecs);

        var predicateGraphs = new List<TrRmPredicateGraphRow>(
            consumer.PredicateGraphs.Count + provider.PredicateGraphs.Count);
        var predicateFootprints = new List<TrRmPredicateReadFootprintRow>(
            consumer.PredicateReadFootprints.Count
                + provider.PredicateReadFootprints.Count);
        var predicateRoots = new List<TrRmPredicateReadRootRow>(
            consumer.PredicateReadRoots.Count
                + provider.PredicateReadRoots.Count);
        var predicateNodes = new List<TrRmPredicateNodeRow>(
            consumer.PredicateNodes.Count + provider.PredicateNodes.Count);
        var predicateOperands
            = new List<TrRmPredicateNodeOperandRow>(
                consumer.PredicateNodeOperands.Count
                    + provider.PredicateNodeOperands.Count);
        static void AppendPredicateTables(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            bool isProvider, int footprintOffset, int rootOffset,
            int nodeOffset, int operandOffset, int consumerRootCount,
            List<TrRmPredicateGraphRow> predicateGraphs,
            List<TrRmPredicateReadFootprintRow> predicateFootprints,
            List<TrRmPredicateReadRootRow> predicateRoots,
            List<TrRmPredicateNodeRow> predicateNodes,
            List<TrRmPredicateNodeOperandRow> predicateOperands)
        {
            for (var footprint = 1;
                footprint <= tables.PredicateReadFootprints.Count;
                footprint++)
            {
                var rootStart = tables.PredicateReadFootprints
                    .GetRootFirstRow(footprint);
                var rootCount = tables.PredicateReadFootprints
                    .GetRootRowCount(footprint);
                var dependencyStart = tables.PredicateReadFootprints
                    .GetDependencyFirstRow(footprint);
                var dependencyCount = tables.PredicateReadFootprints
                    .GetDependencyRowCount(footprint);
                predicateFootprints.Add(new TrRmPredicateReadFootprintRow(
                    rootCount == 0 ? 0 : rootOffset + rootStart, rootCount,
                    dependencyCount == 0 ? 0
                        : dependencyStart,
                    dependencyCount));
            }

            for (var root = 1; root <= tables.PredicateReadRoots.Count; root++)
            {
                predicateRoots.Add(new TrRmPredicateReadRootRow(
                    tables.PredicateReadRoots.GetRootKind(root),
                    tables.PredicateReadRoots.GetRootIdentity(root)));
            }

            for (var operand = 1;
                operand <= tables.PredicateNodeOperands.Count; operand++)
            {
                var operandKind = tables.PredicateNodeOperands
                    .GetOperandKind(operand);
                var operandRef = tables.PredicateNodeOperands
                    .GetOperandRef(operand);
                if (isProvider)
                {
                    Assert.That(operandKind, Is.EqualTo((byte)1));
                    operandRef = checked(operandRef
                        + (uint)consumerRootCount);
                }

                predicateOperands.Add(new TrRmPredicateNodeOperandRow(
                    operandKind, operandRef));
            }

            for (var node = 1; node <= tables.PredicateNodes.Count; node++)
            {
                var operandFirst = tables.PredicateNodes
                    .GetOperandFirstRow(node);
                var operandCount = tables.PredicateNodes.GetOperandCount(node);
                predicateNodes.Add(new TrRmPredicateNodeRow(
                    tables.PredicateNodes.GetNodeKind(node),
                    tables.PredicateNodes.GetResultTypeSpecRef(node),
                    operandCount == 0 ? 0
                        : checked(operandFirst + (uint)operandOffset),
                    operandCount));
            }

            for (var graph = 1; graph <= tables.PredicateGraphs.Count; graph++)
            {
                predicateGraphs.Add(new TrRmPredicateGraphRow(
                    tables.PredicateGraphs.GetRootNodeRow(graph) + nodeOffset,
                    tables.PredicateGraphs.GetReadFootprintRow(graph)
                        + footprintOffset));
            }
        }

        AppendPredicateTables(consumer, false, 0, 0, 0, 0, 0,
            predicateGraphs, predicateFootprints, predicateRoots,
            predicateNodes, predicateOperands);
        AppendPredicateTables(provider, true,
            consumer.PredicateReadFootprints.Count,
            consumer.PredicateReadRoots.Count,
            consumer.PredicateNodes.Count,
            consumer.PredicateNodeOperands.Count,
            consumer.PredicateReadRoots.Count,
            predicateGraphs, predicateFootprints, predicateRoots,
            predicateNodes, predicateOperands);
        Assert.That(consumer.PredicateReadDependencies.Count, Is.Zero);
        Assert.That(provider.PredicateReadDependencies.Count, Is.Zero);
        var contractRows = new List<TrRmContractRow>(
            consumer.Contracts.Count + provider.Contracts.Count);
        static void AppendContracts(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int methodOffset, int graphOffset,
            List<TrRmContractRow> contractRows)
        {
            for (var row = 1; row <= tables.Contracts.Count; row++)
            {
                var contract = tables.Contracts.GetRow(row);
                var owner = BinaryPrimitives.ReadUInt32LittleEndian(contract);
                var kind = BinaryPrimitives.ReadUInt16LittleEndian(
                    contract[4..]);
                var payload = contract[8..].ToArray();
                if (methodOffset != 0)
                {
                    Assert.That(owner >> 24, Is.EqualTo(0x16u));
                    owner = (owner & 0xFF00_0000u)
                        | checked((uint)(methodOffset
                            + (int)(owner & 0x00FF_FFFFu)));
                    if (kind == 2)
                    {
                        var graph = BinaryPrimitives.ReadUInt32LittleEndian(
                            payload.AsSpan(4));
                        BinaryPrimitives.WriteUInt32LittleEndian(
                            payload.AsSpan(4),
                            checked(graph + (uint)graphOffset));
                    }
                    else if (kind == 0x0B)
                    {
                        var graph = BinaryPrimitives.ReadUInt32LittleEndian(
                            payload.AsSpan(8));
                        BinaryPrimitives.WriteUInt32LittleEndian(
                            payload.AsSpan(8),
                            checked(graph + (uint)graphOffset));
                    }
                }

                contractRows.Add(new TrRmContractRow(owner, kind, payload));
            }
        }

        AppendContracts(consumer, 0, 0, contractRows);
        AppendContracts(provider, consumer.Methods.Count,
            consumer.PredicateGraphs.Count, contractRows);
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, contractRows.Count, schema);
        var methodRows = new List<TrRmMethodDefRow>(
            consumer.Methods.Count + provider.Methods.Count);
        static void AppendMethods(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int methodOffset, int signatureOffset, int contractOffset,
            int bodyOffset, int stringOffset, int totalStringCount,
            List<TrRmMethodDefRow> methodRows)
        {
            for (var method = 1; method <= tables.Methods.Count; method++)
            {
                var (contractStart, contractCount)
                    = tables.Methods.GetContractRange(method);
                var bodyRow = tables.Methods.GetBodyRow(method);
                methodRows.Add(new TrRmMethodDefRow(
                    tables.Methods.GetOwnerType(method),
                    stringOffset + FindStringRow(tables.Strings,
                        totalStringCount,
                        tables.Methods.GetName(method)),
                    signatureOffset + tables.Methods.GetSignature(method),
                    tables.Methods.GetFlags(method),
                    contractCount == 0 ? 0 : contractOffset + contractStart,
                    contractCount,
                    bodyRow == 0 ? 0 : bodyOffset + bodyRow));
            }
        }

        AppendMethods(consumer, 0, 0, 0, 0, 0, consumer.Strings.Count,
            methodRows);
        AppendMethods(provider, consumer.Methods.Count,
            consumer.Signatures.Count, consumer.Contracts.Count,
            consumer.Bodies.Count, consumerStringCount, provider.Strings.Count,
            methodRows);
        var combinedBodyCount = consumer.Bodies.Count + provider.Bodies.Count;
        var methodBytes = TrRmMethodDefTableWriter.Encode(methodRows,
            strings, modules, typeDefs, signatures, contractRows.Count,
            combinedBodyCount);
        var methods = new TrRmMethodDefTableReader(methodBytes,
            methodRows.Count, strings, modules, typeDefs, signatures,
            contractRows.Count, combinedBodyCount);
        var contractBytes = TrRmContractTableWriter
            .EncodeForeignInvocationDraft(contractRows, typeDefs, methods,
                signatures, parameters, typeSpecs, features, modules,
                predicateGraphs.Count, schema);
        var contracts = new TrRmContractTableReader(contractBytes,
            contractRows.Count, typeDefs, methods, signatures, parameters,
            typeSpecs, 0, features, modules, schema, predicateGraphs.Count);
        var importBinding = consumer.Imports.GetExpectedDefinitionForImportRow(1);
        Assert.That(consumer.Imports.TryGetExpectedBinding(importBinding,
            out var expectedBinding), Is.True);
        var importRows = new[] {
            new TrRmImportRow(
                consumer.Imports.GetModuleRefForImportRow(1),
                FindStringRow(strings, stringRows.Count,
                    expectedBinding.ExportKey),
                importBinding),
        };
        var importBytes = TrRmImportTableWriter.Encode(importRows, strings,
            moduleRefs, typeDefs, methods, default, default, default, default,
            features, modules, schema);
        var imports = new TrRmImportTableReader(importBytes, importRows.Length,
            strings, moduleRefs, typeDefs, methods, default, default, default,
            default, features, modules, schema);

        var nativeImportRows = new List<TrRmNativeImportRow>(
            consumer.StandardNativeImports.Count
                + provider.StandardNativeImports.Count);
        static void AppendNativeImports(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            int methodOffset, int stringOffset,
            List<TrRmNativeImportRow> nativeImportRows)
        {
            for (var method = 1; method <= tables.Methods.Count; method++)
            {
                if (!tables.StandardNativeImports.HasImportForMethod(method))
                {
                    continue;
                }

                var row = tables.StandardNativeImports
                    .GetImportForMethod(method);
                nativeImportRows.Add(new TrRmNativeImportRow(row.OwnerKind,
                    row.ResolutionMode, row.ResolutionPolicy, row.CallConv,
                    methodOffset + method,
                    stringOffset + row.LibraryNameStringRow,
                    row.EntryNameStringRow == 0 ? 0
                        : stringOffset + row.EntryNameStringRow));
            }
        }

        AppendNativeImports(consumer, 0, 0, nativeImportRows);
        AppendNativeImports(provider, consumer.Methods.Count,
            consumerStringCount, nativeImportRows);
        var nativeImportBytes = TrRmNativeImportTableWriter.Encode(
            nativeImportRows, strings, methods, default, typeDefs, imports,
            modules, signatures, parameters, typeSpecs, contracts, default);
        var exportRows = new List<TrRmExportRow>();
        foreach (var (definition, exportKey)
            in provider.Exports.GetExportsForModule(1))
        {
            var methodRow = checked((int)(definition & 0x00FF_FFFF));
            exportRows.Add(new TrRmExportRow(
                (definition & 0xFF00_0000u)
                    | checked((uint)(consumer.Methods.Count + methodRow)),
                consumerStringCount + FindStringRow(provider.Strings,
                    provider.Strings.Count, exportKey)));
        }

        var exportBytes = TrRmExportTableWriter.Encode(exportRows, strings,
            typeDefs, methods, default, imports, default, default, default,
            features, modules, schema);

        var predicateGraphBytes = TrRmPredicateGraphTableWriter.Encode(
            predicateGraphs, predicateNodes.Count, predicateFootprints.Count);
        var predicateFootprintBytes
            = TrRmPredicateReadFootprintTableWriter.Encode(
                predicateFootprints, predicateRoots.Count, 0);
        var predicateRootBytes = TrRmPredicateReadRootTableWriter.Encode(
            predicateRoots, 0, 0, schema);
        var predicateNodeBytes = TrRmPredicateNodeTableWriter.Encode(
            predicateNodes, predicateOperands.Count, typeSpecs, schema);
        var predicateCounts = new TrRmPredicateTableRowCounts(
            predicateRoots.Count, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var predicateOperandBytes
            = TrRmPredicateNodeOperandTableWriter.Encode(predicateOperands,
                predicateCounts, schema);

        Assert.That(consumerIndex.TryGetSection(0x0020,
            out var bodyBytes, out var bodySectionCount), Is.True);
        Assert.That(bodySectionCount, Is.EqualTo(consumer.Bodies.Count));
        var sections = new List<TrRmSection> {
            new(0x0001, 0, (uint)moduleRows.Length, moduleBytes),
            new(0x0002, 0, (uint)stringRows.Count, stringsBytes),
            new(0x0004, 0, (uint)featureRows.Count, featureBytes),
            new(0x0010, 0, (uint)moduleRefRows.Count, moduleRefBytes),
            new(0x0011, 0, (uint)typeSpecRows.Count, typeSpecBytes),
            new(0x0016, 0, (uint)methodRows.Count, methodBytes),
            new(0x0017, 0, (uint)signatureRows.Count, signatureBytes),
            new(0x0018, 0, (uint)parameterRows.Count, parameterBytes),
            new(0x001B, 0, (uint)contractRows.Count, contractBytes),
            new(0x001C, 0, (uint)importRows.Length, importBytes),
            new(0x001D, 0, (uint)exportRows.Count, exportBytes),
            new(0x0020, 0, (uint)bodySectionCount, bodyBytes.ToArray()),
            new(0x0021, 0, (uint)nativeImportRows.Count,
                nativeImportBytes),
            new(0x0037, 0, (uint)predicateGraphs.Count,
                predicateGraphBytes),
            new(0x0038, 0, (uint)predicateFootprints.Count,
                predicateFootprintBytes),
            new(0x0039, 0, (uint)predicateRoots.Count,
                predicateRootBytes),
            new(0x003B, 0, (uint)predicateNodes.Count, predicateNodeBytes),
            new(0x003C, 0, (uint)predicateOperands.Count,
                predicateOperandBytes),
        };

        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var result = TrRmArtifactWriter.Encode(sections, schema);
        _ = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageAddressDraft(result));
        return result;
    }

    private static byte[] ReverseNativeOutputProviderRows(byte[] artifact)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        Assert.That(tables.Modules.Count, Is.EqualTo(1));
        Assert.That(tables.TypeSpecs.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(tables.PredicateGraphs.Count, Is.EqualTo(2));

        static uint RemapTypeSpec(uint typeSpecRef, int typeSpecCount)
        {
            return typeSpecRef != 0 && (typeSpecRef >> 24) == 0
                ? checked((uint)(typeSpecCount + 1)
                    - typeSpecRef) : typeSpecRef;
        }

        var typeSpecRows = new List<ReadOnlyMemory<byte>>(
            tables.TypeSpecs.Count);
        for (var destination = 1; destination <= tables.TypeSpecs.Count;
            destination++)
        {
            var sourceRow = tables.TypeSpecs.Count + 1 - destination;
            var bytes = tables.TypeSpecs
                .GetTypeSpecBytes(checked((uint)sourceRow)).ToArray();
            if (bytes[0] == 0x0C && bytes[2] == 0x8C)
            {
                var pointee = BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.AsSpan(3));
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(3),
                    RemapTypeSpec(pointee, tables.TypeSpecs.Count));
            }

            typeSpecRows.Add(bytes);
        }

        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            tables.TypeDefs.Count, tables.Imports.Count,
            tables.GenericParams.Count,
            tables.NativeFunctionPointerSignatures.Count,
            TrRmArtifactSchema.ForeignInvocationDraft,
            methodDefCount: tables.Methods.Count,
            predicateCaptureCount: tables.PredicateCaptures.Count);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, tables.TypeDefs.Count, tables.Imports.Count,
            tables.GenericParams.Count,
            tables.NativeFunctionPointerSignatures.Count,
            TrRmArtifactSchema.ForeignInvocationDraft,
            tables.Methods.Count, tables.PredicateCaptures.Count);
        var signatureRows = new List<TrRmMethodSigRow>(
            tables.Signatures.Count);
        var parameterRows = new List<TrRmParamRow>(
            tables.Signatures.ParamCount);
        for (var signature = 1;
            signature <= tables.Signatures.Count; signature++)
        {
            var (parameterStart, parameterCount)
                = tables.Signatures.GetParamRange(signature);
            var (genericStart, genericCount)
                = tables.Signatures.GetGenericRange(signature);
            signatureRows.Add(new TrRmMethodSigRow(
                RemapTypeSpec(
                    tables.Signatures.GetResultTypeSpec(signature),
                    tables.TypeSpecs.Count),
                RemapTypeSpec(
                    tables.Signatures.GetReceiverTypeSpec(signature),
                    tables.TypeSpecs.Count),
                parameterStart, parameterCount,
                genericStart, genericCount));
            for (var ordinal = 0; ordinal < parameterCount; ordinal++)
            {
                var parameter = parameterStart + ordinal;
                parameterRows.Add(new TrRmParamRow(signature, ordinal,
                    RemapTypeSpec(
                        tables.Parameters.GetTypeSpec(parameter),
                        tables.TypeSpecs.Count),
                    tables.Parameters.GetMode(parameter),
                    tables.Parameters.GetFlags(parameter)));
            }
        }

        var signatureBytes = TrRmMethodSigTableWriter.Encode(signatureRows,
            typeSpecs, parameterRows.Count, tables.GenericParams.Count);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            signatureRows.Count, typeSpecs, parameterRows.Count,
            tables.GenericParams.Count);
        var parameterBytes = TrRmParamTableWriter.Encode(parameterRows,
            signatures, typeSpecs, TrRmArtifactSchema.ForeignInvocationDraft);
        var parameters = new TrRmParamTableReader(parameterBytes,
            parameterRows.Count, signatures, typeSpecs);
        var contractRows = new List<TrRmContractRow>(tables.Contracts.Count);
        for (var row = 1; row <= tables.Contracts.Count; row++)
        {
            var contract = tables.Contracts.GetRow(row);
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(
                contract[4..]);
            var payload = contract[8..].ToArray();
            if (kind == 2)
            {
                var graph = BinaryPrimitives.ReadUInt32LittleEndian(
                    payload.AsSpan(4));
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4),
                    checked(3u - graph));
            }
            else if (kind == 0x0B)
            {
                var graph = BinaryPrimitives.ReadUInt32LittleEndian(
                    payload.AsSpan(8));
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8),
                    checked(3u - graph));
            }

            contractRows.Add(new TrRmContractRow(
                BinaryPrimitives.ReadUInt32LittleEndian(contract), kind,
                payload));
        }

        var graphRows = new List<TrRmPredicateGraphRow>(2);
        for (var sourceRow = tables.PredicateGraphs.Count;
            sourceRow >= 1; sourceRow--)
        {
            graphRows.Add(new TrRmPredicateGraphRow(
                tables.PredicateGraphs.GetRootNodeRow(sourceRow),
                tables.PredicateGraphs.GetReadFootprintRow(sourceRow)));
        }

        var graphBytes = TrRmPredicateGraphTableWriter.Encode(graphRows,
            tables.PredicateNodes.Count,
            tables.PredicateReadFootprints.Count);
        Assert.That(index.TryGetSection(0x0016, out var methodBytes,
            out var methodCount), Is.True);
        var methods = new TrRmMethodDefTableReader(methodBytes, methodCount,
            tables.Strings, tables.Modules, tables.TypeDefs, signatures,
            contractRows.Count, tables.Bodies.Count);
        var contractBytes = TrRmContractTableWriter
            .EncodeForeignInvocationDraft(contractRows, tables.TypeDefs,
                methods, signatures, parameters, typeSpecs, tables.Features,
                tables.Modules, graphRows.Count,
                TrRmArtifactSchema.ForeignInvocationDraft);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            sections.Add(kind switch
            {
                0x0011 => new TrRmSection(kind, flags,
                    (uint)typeSpecRows.Count, typeSpecBytes),
                0x0017 => new TrRmSection(kind, flags,
                    (uint)signatureRows.Count, signatureBytes),
                0x0018 => new TrRmSection(kind, flags,
                    (uint)parameterRows.Count, parameterBytes),
                0x001B => new TrRmSection(kind, flags,
                    (uint)contractRows.Count, contractBytes),
                0x0037 => new TrRmSection(kind, flags,
                    (uint)graphRows.Count, graphBytes),
                _ => new TrRmSection(kind, flags, (uint)recordCount,
                    bytes.ToArray()),
            });
        }

        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var result = TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.ForeignInvocationDraft);
        _ = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(result));
        return result;
    }

    private static byte[] AddUncalledNativeOutputProviderImport(
        byte[] providerArtifact, string leafIdentity, string methodName)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationDraft;
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(
            providerArtifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        Assert.That(tables.Modules.Count, Is.EqualTo(1));
        Assert.That(tables.Methods.Count, Is.EqualTo(1));
        Assert.That(tables.Methods.GetBodyRow(1), Is.Zero);
        Assert.That(tables.ModuleRefs.Count, Is.Zero);
        Assert.That(tables.Imports.Count, Is.Zero);
        Assert.That(tables.Exports.HasExportForMethod(1), Is.True);
        Assert.That(tables.Contracts.GetOutputStateContracts(1), Is.Not.Empty);
        Assert.That(tables.Contracts.GetNativeParameters(1), Is.Empty);

        var stringRows = new List<(string Text, int Encoding)>(
            tables.Strings.Count + 2);
        for (var row = 1; row <= tables.Strings.Count; row++)
        {
            stringRows.Add((tables.Strings.DecodeNamespace(row), 0));
        }

        int AddString(string text)
        {
            stringRows.Add((text, 0));
            return stringRows.Count;
        }

        var methodNameStringRow = AddString(methodName);
        var leafIdentityStringRow = AddString(leafIdentity);
        var exportKeyStringRow = AddString(methodName);
        var entryNameStringRow = AddString(methodName);
        var stringsBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(stringsBytes, stringRows.Count);
        var moduleIdentityStringRow = FindStringRow(strings, stringRows.Count,
            tables.Modules.GetIdentity(1));
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(1);
        var (moduleRefStart, _) = tables.Modules.GetModuleRefRange(1);
        var (typeDefStart, _) = tables.Modules.GetTypeDefRange(1);
        var (methodStart, _) = tables.Modules.GetMethodDefRange(1);
        var (sourceFileStart, _) = tables.Modules.GetSourceFileRange(1);
        var (featureStart, featureCount)
            = tables.Modules.GetFeatureRange(1);
        var moduleRows = new[] {
            new TrRmModuleRow(moduleIdentityStringRow, targetRequirement,
                runtimePolicy, 0, moduleRefStart, typeDefStart, methodStart,
                sourceFileStart, featureStart, featureCount),
        };
        var moduleBytes = TrRmModuleTableWriter.Encode(moduleRows, strings,
            1, tables.TypeDefs.Count, 2, tables.Modules.SourceFileCount,
            tables.Features.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings, 1,
            tables.TypeDefs.Count, 2, tables.Modules.SourceFileCount,
            tables.Features.Count);
        var moduleRefBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(leafIdentityStringRow,
                targetRequirement, runtimePolicy),
        ], strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes, 1,
            strings, modules);

        var typeSpecRows = new List<ReadOnlyMemory<byte>>(
            tables.TypeSpecs.Count);
        for (var row = 1; row <= tables.TypeSpecs.Count; row++)
        {
            typeSpecRows.Add(tables.TypeSpecs
                .GetTypeSpecBytes(checked((uint)row)).ToArray());
        }

        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            tables.TypeDefs.Count, 1, tables.GenericParams.Count,
            tables.NativeFunctionPointerSignatures.Count, schema,
            methodDefCount: 2,
            predicateCaptureCount: tables.PredicateCaptures.Count);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, tables.TypeDefs.Count, 1,
            tables.GenericParams.Count,
            tables.NativeFunctionPointerSignatures.Count, schema, 2,
            tables.PredicateCaptures.Count);
        Assert.That(index.TryGetSection(0x0017, out var signatureBytes,
            out var signatureCount), Is.True);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            signatureCount, typeSpecs, tables.Signatures.ParamCount,
            tables.GenericParams.Count);
        Assert.That(index.TryGetSection(0x0018, out var parameterBytes,
            out var parameterCount), Is.True);
        var parameters = new TrRmParamTableReader(parameterBytes,
            parameterCount, signatures, typeSpecs);

        var contractRows = new List<TrRmContractRow>(
            tables.Contracts.Count * 2);
        var methodContractCount = 0;
        var (methodContractStart, methodContractRangeCount)
            = tables.Methods.GetContractRange(1);
        for (var contractIndex = 0;
            contractIndex < methodContractRangeCount; contractIndex++)
        {
            var row = tables.Contracts
                .GetRow(methodContractStart + contractIndex).ToArray();
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(row.AsSpan(4));
            var payload = row.AsSpan(8).ToArray();
            contractRows.Add(new TrRmContractRow(
                BinaryPrimitives.ReadUInt32LittleEndian(row), kind, payload));
            var duplicatedPayload = payload.ToArray();
            if (kind == 2)
            {
                var graph = BinaryPrimitives.ReadUInt32LittleEndian(
                    duplicatedPayload.AsSpan(4));
                BinaryPrimitives.WriteUInt32LittleEndian(
                    duplicatedPayload.AsSpan(4),
                    checked(graph + (uint)tables.PredicateGraphs.Count));
            }
            else if (kind == 0x0B)
            {
                var graph = BinaryPrimitives.ReadUInt32LittleEndian(
                    duplicatedPayload.AsSpan(8));
                BinaryPrimitives.WriteUInt32LittleEndian(
                    duplicatedPayload.AsSpan(8),
                    checked(graph + (uint)tables.PredicateGraphs.Count));
            }

            contractRows.Add(new TrRmContractRow(0x1600_0002u, kind,
                duplicatedPayload));
            methodContractCount++;
        }

        var graphRows = new List<TrRmPredicateGraphRow>(
            tables.PredicateGraphs.Count * 2);
        var footprintRows = new List<TrRmPredicateReadFootprintRow>(
            tables.PredicateReadFootprints.Count * 2);
        var rootRows = new List<TrRmPredicateReadRootRow>(
            tables.PredicateReadRoots.Count * 2);
        var nodeRows = new List<TrRmPredicateNodeRow>(
            tables.PredicateNodes.Count * 2);
        var operandRows = new List<TrRmPredicateNodeOperandRow>(
            tables.PredicateNodeOperands.Count * 2);
        for (var row = 1; row <= tables.PredicateReadFootprints.Count; row++)
        {
            var rootStart = tables.PredicateReadFootprints
                .GetRootFirstRow(row);
            var rootCount = tables.PredicateReadFootprints
                .GetRootRowCount(row);
            footprintRows.Add(new TrRmPredicateReadFootprintRow(
                rootStart, rootCount,
                tables.PredicateReadFootprints.GetDependencyFirstRow(row),
                tables.PredicateReadFootprints.GetDependencyRowCount(row)));
            footprintRows.Add(new TrRmPredicateReadFootprintRow(
                rootCount == 0 ? 0
                    : checked(rootStart
                        + tables.PredicateReadRoots.Count),
                rootCount, 0, 0));
        }

        for (var row = 1; row <= tables.PredicateReadRoots.Count; row++)
        {
            var root = new TrRmPredicateReadRootRow(
                tables.PredicateReadRoots.GetRootKind(row),
                tables.PredicateReadRoots.GetRootIdentity(row));
            rootRows.Add(root);
            rootRows.Add(root);
        }

        for (var row = 1; row <= tables.PredicateNodeOperands.Count; row++)
        {
            var operandKind = tables.PredicateNodeOperands.GetOperandKind(row);
            var operandRef = tables.PredicateNodeOperands.GetOperandRef(row);
            operandRows.Add(new TrRmPredicateNodeOperandRow(operandKind,
                operandRef));
            Assert.That(operandKind, Is.EqualTo((byte)1));
            operandRows.Add(new TrRmPredicateNodeOperandRow(operandKind,
                checked(operandRef
                    + (uint)tables.PredicateReadRoots.Count)));
        }

        for (var row = 1; row <= tables.PredicateNodes.Count; row++)
        {
            var node = new TrRmPredicateNodeRow(
                tables.PredicateNodes.GetNodeKind(row),
                tables.PredicateNodes.GetResultTypeSpecRef(row),
                tables.PredicateNodes.GetOperandFirstRow(row),
                tables.PredicateNodes.GetOperandCount(row));
            nodeRows.Add(node);
            nodeRows.Add(new TrRmPredicateNodeRow(node.NodeKind,
                node.ResultTypeSpecRef,
                checked(node.OperandFirstRow
                    + (uint)tables.PredicateNodeOperands.Count),
                node.OperandCount));
        }

        for (var row = 1; row <= tables.PredicateGraphs.Count; row++)
        {
            var graph = new TrRmPredicateGraphRow(
                tables.PredicateGraphs.GetRootNodeRow(row),
                tables.PredicateGraphs.GetReadFootprintRow(row));
            graphRows.Add(graph);
            graphRows.Add(new TrRmPredicateGraphRow(
                checked(graph.RootNodeRow
                    + tables.PredicateNodes.Count),
                checked(graph.ReadFootprintRow
                    + tables.PredicateReadFootprints.Count)));
        }

        Assert.That(tables.PredicateReadDependencies.Count, Is.Zero);
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, contractRows.Count, schema);
        var methodRows = new[] {
            new TrRmMethodDefRow(0,
                FindStringRow(strings, stringRows.Count,
                    tables.Methods.GetName(1)),
                tables.Methods.GetSignature(1),
                tables.Methods.GetFlags(1), 1,
                tables.Methods.GetContractRange(1).Count, 0),
            new TrRmMethodDefRow(0, methodNameStringRow,
                tables.Methods.GetSignature(1), 1,
                tables.Contracts.Count + 1, methodContractCount, 0),
        };
        var methodBytes = TrRmMethodDefTableWriter.Encode(methodRows, strings,
            modules, typeDefs, signatures, contractRows.Count, 0);
        var methods = new TrRmMethodDefTableReader(methodBytes,
            methodRows.Length, strings, modules, typeDefs, signatures,
            contractRows.Count, 0);
        var predicateGraphBytes = TrRmPredicateGraphTableWriter.Encode(
            graphRows, nodeRows.Count, footprintRows.Count);
        var predicateFootprintBytes
            = TrRmPredicateReadFootprintTableWriter.Encode(
                footprintRows, rootRows.Count, 0);
        var predicateRootBytes = TrRmPredicateReadRootTableWriter.Encode(
            rootRows, 0, 0, schema);
        var predicateNodeBytes = TrRmPredicateNodeTableWriter.Encode(
            nodeRows, operandRows.Count, typeSpecs, schema);
        var predicateCounts = new TrRmPredicateTableRowCounts(
            rootRows.Count, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var predicateOperandBytes
            = TrRmPredicateNodeOperandTableWriter.Encode(operandRows,
                predicateCounts, schema);
        var contractBytes = TrRmContractTableWriter
            .EncodeForeignInvocationDraft(contractRows, typeDefs, methods,
                signatures, parameters, typeSpecs, tables.Features, modules,
                graphRows.Count, schema);
        var contracts = new TrRmContractTableReader(contractBytes,
            contractRows.Count, typeDefs, methods, signatures, parameters,
            typeSpecs, 0, tables.Features, modules, schema, graphRows.Count);
        var importBytes = TrRmImportTableWriter.Encode([
            new TrRmImportRow(1, exportKeyStringRow, 0x1600_0002u),
        ], strings, moduleRefs, typeDefs, methods, default, default, default,
            default, tables.Features, modules, schema);
        var imports = new TrRmImportTableReader(importBytes, 1, strings,
            moduleRefs, typeDefs, methods, default, default, default, default,
            tables.Features, modules, schema);
        var firstNativeImport = tables.StandardNativeImports
            .GetImportForMethod(1);
        var nativeImportBytes = TrRmNativeImportTableWriter.Encode([
            firstNativeImport,
            new TrRmNativeImportRow(1,
                firstNativeImport.ResolutionMode,
                firstNativeImport.ResolutionPolicy,
                firstNativeImport.CallConv, 2,
                firstNativeImport.LibraryNameStringRow, entryNameStringRow),
        ], strings, methods, default, typeDefs, imports, modules,
            signatures, parameters, typeSpecs, contracts, default);
        var sections = new List<TrRmSection>(index.SectionCount + 3);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            switch (kind)
            {
                case 0x0001:
                    sections.Add(new TrRmSection(kind, flags, 1,
                        moduleBytes));
                    break;
                case 0x0002:
                    sections.Add(new TrRmSection(kind, flags,
                        (uint)stringRows.Count, stringsBytes));
                    break;
                case 0x0016:
                    sections.Add(new TrRmSection(kind, flags, 2,
                        methodBytes));
                    break;
                case 0x001B:
                    sections.Add(new TrRmSection(kind, flags,
                        (uint)contractRows.Count, contractBytes));
                    break;
                case 0x0021:
                    sections.Add(new TrRmSection(kind, flags, 2,
                        nativeImportBytes));
                    break;
                case 0x0037:
                    sections.Add(new TrRmSection(kind, flags,
                        (uint)graphRows.Count, predicateGraphBytes));
                    break;
                case 0x0038:
                    sections.Add(new TrRmSection(kind, flags,
                        (uint)footprintRows.Count, predicateFootprintBytes));
                    break;
                case 0x0039:
                    sections.Add(new TrRmSection(kind, flags,
                        (uint)rootRows.Count, predicateRootBytes));
                    break;
                case 0x003B:
                    sections.Add(new TrRmSection(kind, flags,
                        (uint)nodeRows.Count, predicateNodeBytes));
                    break;
                case 0x003C:
                    sections.Add(new TrRmSection(kind, flags,
                        (uint)operandRows.Count, predicateOperandBytes));
                    break;
                default:
                    sections.Add(new TrRmSection(kind, flags,
                        (uint)recordCount, bytes.ToArray()));
                    break;
            }
        }

        sections.Add(new TrRmSection(0x0010, 0, 1, moduleRefBytes));
        sections.Add(new TrRmSection(0x001C, 0, 1, importBytes));
        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var result = TrRmArtifactWriter.Encode(sections, schema);
        _ = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(result));
        return result;
    }

    private static int FindStringRow(TrRmStringTableReader strings,
        int stringCount, string text)
    {
        for (var row = 1; row <= stringCount; row++)
        {
            if (StringComparer.Ordinal.Equals(
                    strings.DecodeNamespace(row), text))
            {
                return row;
            }
        }

        throw new InvalidDataException(
            $"The test artifact has no String row for '{text}'.");
    }

    private static byte[] AddLeadingEmptyModule(byte[] artifact,
        string identity)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        var stringTableFound = index.TryGetSection(0x0002,
            out _, out var stringCount);
        var moduleTableFound = index.TryGetSection(0x0001,
            out var moduleBytes, out var moduleCount);
        Assert.That(stringTableFound && moduleTableFound, Is.True);
        Assert.That(moduleCount, Is.EqualTo(1));
        var identityStringRow = 0;
        for (var row = 1; row <= stringCount; row++)
        {
            if (tables.Strings.DecodeNamespace(row).Length != 0
                && tables.Strings.DecodeName(row) == identity)
            {
                identityStringRow = row;
                break;
            }
        }

        Assert.That(identityStringRow, Is.GreaterThan(0));
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(1);
        var featureFirst = (int)BinaryPrimitives.ReadUInt32LittleEndian(
            moduleBytes[24..]);
        var featureCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(
            moduleBytes[28..]);
        var originalIdentityStringRow = checked((int)
            BinaryPrimitives.ReadUInt32LittleEndian(moduleBytes));
        var moduleRows = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(identityStringRow, targetRequirement,
                runtimePolicy, 0, 1, 1, 1, 1, 0, 0,
                tables.Modules.GetGoverningRuleEdition(1)),
            new TrRmModuleRow(originalIdentityStringRow,
                targetRequirement, runtimePolicy,
                tables.Modules.GetEntryMethod(1), 1, 1, 1, 1,
                featureFirst, featureCount,
                tables.Modules.GetGoverningRuleEdition(1)),
        ], tables.Strings, tables.ModuleRefs.Count, tables.TypeDefs.Count,
            tables.Methods.Count, tables.Modules.SourceFileCount,
            tables.Features.Count);

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            sections.Add(kind == 0x0001
                ? new TrRmSection(kind, flags, 2, moduleRows)
                : new TrRmSection(kind, flags, (uint)recordCount,
                    bytes.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections);
    }

    [Test]
    public void LoadsSelectedModuleFromMultiplePopulatedModules()
    {
        var firstCompilation = CreateNativeAddressCompilation("other.dll");
        var secondCompilation = CreateNativeAddressCompilation("selected.dll");
        var firstArtifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                firstCompilation.Program!, "multi-module-other");
        var secondArtifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                secondCompilation.Program!, "multi-module-selected");
        var artifact = CombinePopulatedNativeAddressModules(
            firstArtifact, secondArtifact);

        var input = new TrRmStandardArtifactInput(
            "trrm/1-draft-foreign-invocation-contracts-1", artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            "multi-module-selected", []);

        var imported = loaded.Functions.Single(static function =>
            function.DirectNativeImport is not null);
        Assert.That(imported.DirectNativeImport!.LibraryName,
            Is.EqualTo("selected.dll"));
        Assert.That(loaded.Functions.Any(static function =>
            function.Name == "Form"), Is.True);
    }

    [Test]
    public void BindsProviderModuleFromMultiplePopulatedModules()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string providerIdentity = "multi-module-provider-selected";
        var firstCompilation = CreateNativeAddressCompilation(
            "other.dll", exportObserve: true);
        var secondCompilation = CreateNativeAddressCompilation(
            "selected.dll", exportObserve: true);
        var firstArtifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                firstCompilation.Program!, "multi-module-provider-other");
        var secondArtifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                secondCompilation.Program!, providerIdentity);
        var providerArtifact = CombinePopulatedNativeAddressModules(
            firstArtifact, secondArtifact);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, []);
        Assert.That(providerReference.Functions.Single(
            static function => function.Name == "Observe")
            .DirectNativeImport!.LibraryName, Is.EqualTo("selected.dll"));

        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                internal void Use() {
                    unsafe {
                        var callback = &Observe;
                        callback();
                    }
                }
                """), providerReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var rootArtifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                consumerCompilation.Program!, "multi-module-consumer");
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, rootArtifact),
            "multi-module-consumer", [providerInput]);
        var imported = loaded.Functions.Single(static function =>
            function.Name == "Observe");
        Assert.That(imported.ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(imported.DirectNativeImport!.LibraryName,
            Is.EqualTo("selected.dll"));
    }

    [Test]
    public void LowersProviderModuleCoLocatedWithTheSelectedRoot()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "co-located-native-consumer";
        const string providerIdentity = "co-located-native-provider";
        var providerCompilation = CreateCurrentProcessIdCompilation();
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, schemaIdentity, 1);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, providerArtifact),
            providerIdentity, []);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    var callback = &GetCurrentProcessId;
                    return callback();
                }
            }
            """), providerReference);
        Assert.That(rootCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var rootArtifact = TrRmExecutableCompiler.EmitStandard(
            rootCompilation.Program!, rootIdentity, schemaIdentity, 1);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, []));

        var artifact = CombinePopulatedNativeAddressModules(
            rootArtifact, providerArtifact);
        var input = new TrRmStandardArtifactInput(schemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            rootIdentity, []);
        var imported = loaded.Functions.Single(static function =>
            function.Name == "GetCurrentProcessId");
        Assert.That(imported.ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(imported.DirectNativeImport!.LibraryName,
            Is.EqualTo("kernel32.dll"));
        Assert.That(NativeModuleCompiler.ExecuteStandard(input, rootIdentity,
            [], NativeRuntimePolicy.FailFast), Is.GreaterThan(0));
        Assert.That(NativeModuleCompiler.EmitStandardExecutable(input,
            rootIdentity, [], NativeRuntimePolicy.FailFast), Is.Not.Empty);
        var selfSelectedInput = new TrRmStandardArtifactInput(
            schemaIdentity, RewriteFirstModuleRefToRoot(artifact));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(selfSelectedInput,
                rootIdentity, []));
    }

    private static Compilation CreateNativeAddressCompilation(
        string libraryName, bool exportObserve = false)
    {
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [Terrum.Interop.NativeImport(
                "{{libraryName}}",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            {{(exportObserve ? "public" : "internal")}} extern void Observe();

            internal void Form() {
                unsafe {
                    var callback = &Observe;
                    callback();
                }
            }
            """), core);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return compilation;
    }

    internal static byte[] CreateRootMethodImportArtifact(
        string moduleIdentity, string methodName,
        string producerIdentity, string exportKey, CompilationReference core)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetCurrentProcessId",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern int32 {{methodName}}();

            int32 Main() {
                unsafe {
                    var callback = &{{methodName}};
                    return callback();
                }
            }
            """), core);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        return AddRootMethodImports(artifact, [
            (methodName, producerIdentity, exportKey),
        ], reuseModuleRefs: false, duplicateSecondMethodName: false);
    }

    private static byte[] CreateEmptyFModuleArtifact(string moduleIdentity)
    {
        var stringsBytes
            = TrRmStringTableWriter.EncodeNames([(moduleIdentity, 0)]);
        var strings = new TrRmStringTableReader(stringsBytes, 1);
        var modules = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 0, 0, 0, 0);
        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, modules),
            new TrRmSection(0x0002, 0, 1, stringsBytes),
        ], TrRmArtifactSchema.ForeignInvocationDraft);
    }

    private static byte[] AddMethodExportAlias(byte[] artifact,
        string methodName, string exportKey)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        Assert.That(index.TryGetSection(0x0002, out _, out var stringCount),
            Is.True);
        var methodRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetOwnerModule(row) == 1
                && StringComparer.Ordinal.Equals(
                    tables.Methods.GetName(row), methodName))
            {
                methodRow = row;
                break;
            }
        }

        Assert.That(methodRow, Is.GreaterThan(0));
        var exportRows = new List<TrRmExportRow>();
        foreach (var (definition, existingKey)
            in tables.Exports.GetExportsForModule(1))
        {
            exportRows.Add(new TrRmExportRow(definition,
                FindStringRow(tables.Strings, stringCount, existingKey)));
        }

        exportRows.Add(new TrRmExportRow(
            0x1600_0000u | (uint)methodRow,
            FindStringRow(tables.Strings, stringCount, exportKey)));
        var exports = TrRmExportTableWriter.Encode(exportRows,
            tables.Strings, tables.TypeDefs, tables.Methods,
            default, tables.Imports, tables.Interfaces,
            tables.AssociatedTypeDefs, tables.Fields, tables.Features,
            tables.Modules, TrRmArtifactSchema.ForeignInvocationDraft,
            tables.AssociatedConstDefs, tables.StaticFields,
            tables.StaticFieldContracts, tables.IntrinsicRegistrations);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            sections.Add(kind == 0x001D
                ? new TrRmSection(kind, flags, (uint)exportRows.Count,
                    exports)
                : new TrRmSection(kind, flags, (uint)recordCount,
                    bytes.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.ForeignInvocationDraft);
    }

    private static List<string> ReadPeImports(byte[] image, string library)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        var header = pe.PEHeaders.PEHeader
            ?? throw new InvalidDataException("Missing PE header.");
        var descriptors = pe.GetSectionData(
            header.ImportTableDirectory.RelativeVirtualAddress).GetReader();
        var result = new List<string>();
        while (true)
        {
            var lookup = descriptors.ReadInt32();
            _ = descriptors.ReadInt32();
            _ = descriptors.ReadInt32();
            var name = descriptors.ReadInt32();
            _ = descriptors.ReadInt32();
            if (lookup == 0)
            {
                return result;
            }

            if (!ReadPeName(pe, name).Equals(library,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var entries = pe.GetSectionData(lookup).GetReader();
            for (var entry = entries.ReadUInt64(); entry != 0;
                entry = entries.ReadUInt64())
            {
                Assert.That(entry & 0x8000_0000_0000_0000UL, Is.EqualTo(0UL));
                result.Add(ReadPeName(pe, checked((int)entry + 2)));
            }
        }
    }

    private static string ReadPeName(PEReader pe, int rva)
    {
        var reader = pe.GetSectionData(rva).GetReader();
        var text = new StringBuilder();
        for (var value = reader.ReadByte(); value != 0;
            value = reader.ReadByte())
        {
            _ = text.Append((char)value);
        }

        return text.ToString();
    }

    private static void WriteStandardFArtifactEvidence(string role,
        string moduleIdentity, byte[] artifact)
    {
        TestContext.WriteLine(
            $"Standard F input {role}: module={moduleIdentity}; bytes={artifact.Length}; sha256={Convert.ToHexString(SHA256.HashData(artifact))}");
    }

    private static string[] ReadPeExports(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        var header = pe.PEHeaders.PEHeader
            ?? throw new InvalidDataException("Missing PE header.");
        var exportDirectory = header.ExportTableDirectory;
        if (exportDirectory.RelativeVirtualAddress == 0
            || exportDirectory.Size == 0)
        {
            return [];
        }

        var export = pe.GetSectionData(
            exportDirectory.RelativeVirtualAddress).GetReader();
        _ = export.ReadInt32();
        _ = export.ReadInt32();
        _ = export.ReadUInt16();
        _ = export.ReadUInt16();
        _ = export.ReadInt32();
        _ = export.ReadInt32();
        _ = export.ReadInt32();
        var nameCount = export.ReadInt32();
        _ = export.ReadInt32();
        var namesRva = export.ReadInt32();
        _ = export.ReadInt32();
        var names = pe.GetSectionData(namesRva).GetReader();
        var exports = new List<string>(nameCount);
        for (var index = 0; index < nameCount; index++)
        {
            exports.Add(ReadPeName(pe, names.ReadInt32()));
        }

        return [.. exports];
    }

    private static byte[] ReencodeFirstModuleRefIdentity(byte[] artifact,
        string identity, int encoding)
        => ReencodeModuleRefIdentity(artifact, 1, identity, encoding);

    private static byte[] ReencodeModuleRefIdentity(byte[] artifact,
        int moduleRefRow, string identity, int encoding)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        Assert.That(moduleRefRow, Is.InRange(1, tables.ModuleRefs.Count));
        Assert.That(index.TryGetSection(0x0001, out var moduleBytes,
            out var moduleCount), Is.True);
        Assert.That(index.TryGetSection(0x0002, out _, out var stringCount),
            Is.True);
        Assert.That(index.TryGetSection(0x0010,
            out var moduleRefSectionBytes, out var moduleRefCount), Is.True);

        var stringRows = new (string Text, int Encoding)[stringCount + 1];
        for (var row = 1; row <= stringCount; row++)
        {
            stringRows[row - 1] = (tables.Strings.DecodeNamespace(row), 0);
        }

        stringRows[stringCount] = (identity, encoding);
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(stringBytes,
            stringRows.Length);
        var modules = new TrRmModuleTableReader(moduleBytes, moduleCount,
            strings, tables.ModuleRefs.Count, tables.TypeDefs.Count,
            tables.Methods.Count, tables.Modules.SourceFileCount,
            tables.Features.Count);
        var moduleRefRows = new List<TrRmModuleRefRow>(moduleRefCount);
        for (var row = 1; row <= moduleRefCount; row++)
        {
            var sourceRow = moduleRefSectionBytes.Slice((row - 1) * 12,
                12);
            var identityStringRow = row == moduleRefRow
                ? stringRows.Length
                : checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    sourceRow));
            var (targetRequirement, runtimePolicy)
                = tables.ModuleRefs.GetRequirements(row);
            moduleRefRows.Add(new TrRmModuleRefRow(identityStringRow,
                targetRequirement, runtimePolicy));
        }

        var rewrittenModuleRefBytes = TrRmModuleRefTableWriter.Encode(
            moduleRefRows, strings, modules);

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            sections.Add(kind switch
            {
                0x0002 => new TrRmSection(kind, flags,
                    (uint)stringRows.Length, stringBytes),
                0x0010 => new TrRmSection(kind, flags,
                    (uint)moduleRefCount, rewrittenModuleRefBytes),
                _ => new TrRmSection(kind, flags, (uint)recordCount,
                    bytes.ToArray()),
            });
        }

        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.ForeignInvocationDraft);
    }

    private static byte[] ReplaceImportExportKey(byte[] artifact,
        int importRow, string exportKey)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        Assert.That(importRow, Is.InRange(1, tables.Imports.Count));
        Assert.That(index.TryGetSection(0x001C, out var importSectionBytes,
            out var importCount), Is.True);
        Assert.That(index.TryGetSection(0x0002, out _, out var stringCount),
            Is.True);
        var keyStringRow = FindStringRow(tables.Strings, stringCount,
            exportKey);
        var rewrittenImportBytes = importSectionBytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            rewrittenImportBytes.AsSpan(((importRow - 1) * 12) + 4),
            (uint)keyStringRow);

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            sections.Add(kind == 0x001C
                ? new TrRmSection(kind, flags, (uint)importCount,
                    rewrittenImportBytes)
                : new TrRmSection(kind, flags, (uint)recordCount,
                    bytes.ToArray()));
        }

        var result = TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.ForeignInvocationDraft);
        var resultTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(result));
        for (var methodRow = 1; methodRow <= resultTables.Methods.Count;
            methodRow++)
        {
            if (resultTables.Methods.GetBodyRow(methodRow) != 0)
            {
                resultTables.ValidateRegisterBodyMapsForMethod(methodRow);
            }
        }

        return result;
    }

    private static byte[] ReplaceSectionRecordCount(byte[] artifact,
        int sectionKind, uint recordCount)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var result = (byte[])artifact.Clone();
        var found = false;
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out _, out _, out _);
            if (kind != sectionKind)
            {
                continue;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(
                32 + (section * 16) + 4), recordCount);
            found = true;
            break;
        }

        Assert.That(found, Is.True);
        return result;
    }

    private static byte[] RenameMethodDef(byte[] artifact,
        string currentName, string newName)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        Assert.That(index.TryGetSection(0x0002, out _, out var stringCount),
            Is.True);
        Assert.That(index.TryGetSection(0x0016, out var methodBytes,
            out var methodCount), Is.True);
        var newNameStringRow = FindStringRow(tables.Strings, stringCount,
            newName);
        var rows = new List<TrRmMethodDefRow>(methodCount);
        var renamed = false;
        for (var row = 1; row <= methodCount; row++)
        {
            var data = methodBytes.Slice((row - 1) * 28, 28);
            var rowName = tables.Methods.GetName(row);
            var replaceName = StringComparer.Ordinal.Equals(rowName,
                currentName);
            if (replaceName)
            {
                Assert.That(renamed, Is.False);
                renamed = true;
            }

            rows.Add(new TrRmMethodDefRow(
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data)),
                replaceName ? newNameStringRow : checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(data[4..])),
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    data[8..])),
                BinaryPrimitives.ReadUInt32LittleEndian(data[12..]),
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    data[16..])),
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    data[20..])),
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    data[24..]))));
        }

        Assert.That(renamed, Is.True);
        var renamedMethodBytes = TrRmMethodDefTableWriter.Encode(rows,
            tables.Strings, tables.Modules, tables.TypeDefs,
            tables.Signatures, tables.Contracts.Count, tables.Bodies.Count);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            sections.Add(kind == 0x0016
                ? new TrRmSection(kind, flags, (uint)methodCount,
                    renamedMethodBytes)
                : new TrRmSection(kind, flags, (uint)recordCount,
                    bytes.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.ForeignInvocationDraft);
    }

    internal static byte[] CreateMixedImportProviderArtifact(
        string moduleIdentity, string ownedMethodName,
        string ownedNativeExport, string importedMethodName,
        string importedNativeExport, string producerIdentity,
        string exportKey, CompilationReference core,
        string? callerTargetMethodName = null)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        var targetMethodName = callerTargetMethodName ?? ownedMethodName;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "{{ownedNativeExport}}",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            public extern int32 {{ownedMethodName}}();

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "{{importedNativeExport}}",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern int32 {{importedMethodName}}();

            internal int32 ProviderBody() {
                unsafe {
                    var callback = &{{targetMethodName}};
                    return callback();
                }
            }
            """), core);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        return AddRootMethodImports(artifact, [
            (importedMethodName, producerIdentity, exportKey),
        ], reuseModuleRefs: false, duplicateSecondMethodName: false);
    }

    internal static Compilation CreateCurrentProcessIdCompilation()
    {
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetCurrentProcessId",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            public extern int32 GetCurrentProcessId();

            internal int32 Form() {
                unsafe {
                    var callback = &GetCurrentProcessId;
                    return callback();
                }
            }
            """), core);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return compilation;
    }

    private static byte[] RewriteFirstModuleRefToRoot(byte[] artifact)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        Assert.That(index.TryGetSection(0x0001, out var moduleBytes, out _),
            Is.True);
        Assert.That(index.TryGetSection(0x0010, out var moduleRefBytes,
            out var moduleRefCount), Is.True);
        Assert.That(moduleRefCount, Is.GreaterThan(0));
        var rootIdentityStringRow
            = BinaryPrimitives.ReadUInt32LittleEndian(moduleBytes);
        var rewrittenModuleRefs = moduleRefBytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(rewrittenModuleRefs,
            rootIdentityStringRow);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            sections.Add(kind == 0x0010
                ? new TrRmSection(kind, flags, (uint)recordCount,
                    rewrittenModuleRefs)
                : new TrRmSection(kind, flags, (uint)recordCount,
                    bytes.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.ForeignInvocationDraft);
    }

    internal static byte[] CombinePopulatedNativeAddressModules(
        byte[] firstArtifact, byte[] secondArtifact,
        TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationDraft,
        bool requireFunctionPointerAnchors = true)
    {
        var usesScalarFrozenValues = schema
            == TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft;
        var usesA2EU = usesScalarFrozenValues || schema
            == TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft;
        var usesA2 = schema
            == TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        var firstIndex = usesScalarFrozenValues
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                    firstArtifact)
            : usesA2EU
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                    firstArtifact)
            : usesA2
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyDraft(firstArtifact)
            : TrRmArtifactIndex.ForForeignInvocationDraft(firstArtifact);
        var secondIndex = usesScalarFrozenValues
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                    secondArtifact)
            : usesA2EU
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                    secondArtifact)
            : usesA2
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyDraft(secondArtifact)
            : TrRmArtifactIndex.ForForeignInvocationDraft(secondArtifact);
        var firstTables = usesScalarFrozenValues
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                    firstIndex)
            : usesA2EU
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                    firstIndex)
            : usesA2
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    firstIndex)
            : TrRmStructuralMetadataValidator
                .ValidateForeignInvocationDraftAndGetExecutionTables(
                    firstIndex);
        var secondTables = usesScalarFrozenValues
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                    secondIndex)
            : usesA2EU
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                    secondIndex)
            : usesA2
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    secondIndex)
            : TrRmStructuralMetadataValidator
                .ValidateForeignInvocationDraftAndGetExecutionTables(
                    secondIndex);
        Assert.That(firstTables.Modules.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(secondTables.Modules.Count, Is.EqualTo(1));
        Assert.That(firstTables.Methods.Count,
            Is.GreaterThanOrEqualTo(firstTables.Modules.Count * 3));
        Assert.That(secondTables.Methods.Count, Is.GreaterThanOrEqualTo(3));
        var firstAnchorCount = 0;
        for (var row = 1; row <= firstTables.TypeSpecs.Count; row++)
        {
            if (firstTables.NativeFunctionPointerAnchors
                .HasAnchorForTypeSpec(row))
            {
                firstAnchorCount++;
            }
        }
        var secondAnchorCount = 0;
        for (var row = 1; row <= secondTables.TypeSpecs.Count; row++)
        {
            if (secondTables.NativeFunctionPointerAnchors
                .HasAnchorForTypeSpec(row))
            {
                secondAnchorCount++;
            }
        }

        if (requireFunctionPointerAnchors)
        {
            Assert.That(firstAnchorCount, Is.EqualTo(firstTables.Modules.Count));
            Assert.That(secondAnchorCount, Is.EqualTo(1));
        }
        else
        {
            Assert.That(firstAnchorCount, Is.Zero);
            Assert.That(secondAnchorCount, Is.Zero);
        }
        Assert.That(firstTables.TypeDefs.Count, Is.Zero);
        Assert.That(secondTables.TypeDefs.Count, Is.Zero);
        Assert.That(firstTables.Fields.Count, Is.Zero);
        Assert.That(secondTables.Fields.Count, Is.Zero);
        Assert.That(firstTables.Imports.Count,
            Is.InRange(0, firstTables.Modules.Count));
        Assert.That(secondTables.Imports.Count, Is.InRange(0, 1));
        Assert.That(firstTables.ModuleRefs.Count,
            Is.EqualTo(firstTables.Imports.Count));
        Assert.That(secondTables.ModuleRefs.Count,
            Is.EqualTo(secondTables.Imports.Count));
        Assert.That(firstTables.Bodies.Count,
            Is.EqualTo(firstTables.Modules.Count));
        Assert.That(secondTables.Bodies.Count, Is.EqualTo(1));
        Assert.That(firstIndex.SectionCount, Is.GreaterThanOrEqualTo(9));
        Assert.That(secondIndex.SectionCount, Is.GreaterThanOrEqualTo(9));

        Assert.That(firstIndex.TryGetSection(0x0001, out var firstModuleBytes,
            out var firstModuleCount), Is.True);
        Assert.That(firstIndex.TryGetSection(0x0002, out var firstStringBytes,
            out var firstStringCount), Is.True);
        Assert.That(firstIndex.TryGetSection(0x0004, out var firstFeatureBytes,
            out var firstFeatureCount), Is.True);
        Assert.That(firstIndex.TryGetSection(0x0016, out var firstMethodBytes,
            out var firstMethodCount), Is.True);
        Assert.That(firstIndex.TryGetSection(0x0011, out _, out var firstTypeSpecCount),
            Is.True);
        Assert.That(firstIndex.TryGetSection(0x0017, out _, out var firstSignatureCount),
            Is.True);
        Assert.That(firstIndex.TryGetSection(0x001B, out _, out var firstContractCount),
            Is.True);
        Assert.That(firstIndex.TryGetSection(0x0020, out _, out var firstBodyCount),
            Is.True);
        var firstNativeImportsFound = firstIndex.TryGetSection(0x0021,
            out _, out var firstNativeImportCount);
        var firstModuleRefsFound = firstIndex.TryGetSection(0x0010,
            out var firstModuleRefBytes, out var firstModuleRefCount);
        var firstImportsFound = firstIndex.TryGetSection(0x001C,
            out var firstImportBytes, out var firstImportCount);
        var firstExportsFound = firstIndex.TryGetSection(0x001D,
            out var firstExportBytes, out var firstExportCount);
        Assert.That(secondIndex.TryGetSection(0x0001, out var secondModuleBytes,
            out var secondModuleCount), Is.True);
        Assert.That(secondIndex.TryGetSection(0x0002, out var secondStringBytes,
            out var secondStringCount), Is.True);
        Assert.That(secondIndex.TryGetSection(0x0004, out var secondFeatureBytes,
            out var secondFeatureCount), Is.True);
        Assert.That(secondIndex.TryGetSection(0x0016, out var secondMethodBytes,
            out var secondMethodCount), Is.True);
        Assert.That(secondIndex.TryGetSection(0x0011, out _, out var secondTypeSpecCount),
            Is.True);
        Assert.That(secondIndex.TryGetSection(0x0017, out _, out var secondSignatureCount),
            Is.True);
        Assert.That(secondIndex.TryGetSection(0x001B, out _, out var secondContractCount),
            Is.True);
        Assert.That(secondIndex.TryGetSection(0x0020, out _, out var secondBodyCount),
            Is.True);
        var secondNativeImportsFound = secondIndex.TryGetSection(0x0021,
            out _, out var secondNativeImportCount);
        var secondModuleRefsFound = secondIndex.TryGetSection(0x0010,
            out var secondModuleRefBytes, out var secondModuleRefCount);
        var secondImportsFound = secondIndex.TryGetSection(0x001C,
            out var secondImportBytes, out var secondImportCount);
        var secondExportsFound = secondIndex.TryGetSection(0x001D,
            out var secondExportBytes, out var secondExportCount);
        Assert.That(firstModuleCount, Is.EqualTo(firstTables.Modules.Count));
        Assert.That(secondModuleCount, Is.EqualTo(1));
        Assert.That(firstMethodCount, Is.EqualTo(firstTables.Methods.Count));
        Assert.That(secondMethodCount, Is.EqualTo(secondTables.Methods.Count));
        Assert.That(firstTypeSpecCount, Is.EqualTo(firstTables.TypeSpecs.Count));
        Assert.That(secondTypeSpecCount, Is.EqualTo(secondTables.TypeSpecs.Count));
        var firstTypeDefCount = firstTables.TypeDefs.Count;
        var firstGenericParamCount = firstTables.GenericParams.Count;
        Assert.That(firstSignatureCount, Is.EqualTo(firstTables.Signatures.Count));
        Assert.That(secondSignatureCount, Is.EqualTo(secondTables.Signatures.Count));
        Assert.That(firstContractCount, Is.EqualTo(firstTables.Contracts.Count));
        Assert.That(secondContractCount, Is.EqualTo(secondTables.Contracts.Count));
        Assert.That(firstBodyCount, Is.EqualTo(firstTables.Bodies.Count));
        Assert.That(secondBodyCount, Is.EqualTo(secondTables.Bodies.Count));
        Assert.That(firstNativeImportCount,
            Is.EqualTo(firstTables.StandardNativeImports.Count));
        Assert.That(secondNativeImportCount,
            Is.EqualTo(secondTables.StandardNativeImports.Count));
        Assert.That(firstNativeImportsFound,
            Is.EqualTo(firstNativeImportCount != 0));
        Assert.That(secondNativeImportsFound,
            Is.EqualTo(secondNativeImportCount != 0));
        Assert.That(firstModuleRefCount, Is.EqualTo(firstTables.ModuleRefs.Count));
        Assert.That(firstImportCount, Is.EqualTo(firstTables.Imports.Count));
        Assert.That(secondModuleRefCount,
            Is.EqualTo(secondTables.ModuleRefs.Count));
        Assert.That(secondImportCount,
            Is.EqualTo(secondTables.Imports.Count));
        Assert.That(firstModuleRefsFound, Is.EqualTo(firstModuleRefCount != 0));
        Assert.That(firstImportsFound, Is.EqualTo(firstImportCount != 0));
        Assert.That(secondModuleRefsFound,
            Is.EqualTo(secondModuleRefCount != 0));
        Assert.That(secondImportsFound, Is.EqualTo(secondImportCount != 0));
        Assert.That(firstExportsFound, Is.EqualTo(firstExportCount != 0));
        Assert.That(secondExportsFound, Is.EqualTo(secondExportCount != 0));
        var firstModuleFeatureCount = 0;
        for (var moduleRow = 1; moduleRow <= firstTables.Modules.Count;
            moduleRow++)
        {
            firstModuleFeatureCount += firstTables.Modules
                .GetFeatureRange(moduleRow).Count;
        }
        Assert.That(firstModuleFeatureCount, Is.EqualTo(firstFeatureCount));

        var firstStringIndex = new TrRmVariableTableIndex(
            firstStringBytes, firstStringCount, 8, 4);
        var secondStringIndex = new TrRmVariableTableIndex(
            secondStringBytes, secondStringCount, 8, 4);
        var stringRows = new List<(string Text, int Encoding)>(firstStringCount
            + secondStringCount);
        var stringRowByText = new Dictionary<(string Text, int Encoding), int>();
        for (var row = 1; row <= firstStringCount; row++)
        {
            var value = (firstTables.Strings.DecodeNamespace(row),
                (int)firstStringIndex.GetRow(row)[0]);
            stringRows.Add(value);
            _ = stringRowByText.TryAdd(value, row);
        }

        var secondStringMap = new int[secondStringCount + 1];
        for (var row = 1; row <= secondStringCount; row++)
        {
            var value = (secondTables.Strings.DecodeNamespace(row),
                (int)secondStringIndex.GetRow(row)[0]);
            if (!stringRowByText.TryGetValue(value, out var mappedRow))
            {
                mappedRow = stringRows.Count + 1;
                stringRows.Add(value);
                stringRowByText.Add(value, mappedRow);
            }

            secondStringMap[row] = mappedRow;
        }

        var stringBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(stringBytes,
            stringRows.Count);
        var featureBytes = firstFeatureBytes.ToArray()
            .Concat(secondFeatureBytes.ToArray()).ToArray();
        var featureCount = firstFeatureCount + secondFeatureCount;
        var features = new TrRmFeatureTableReader(featureBytes,
            featureCount, schema);
        var (BlobSection, BlobCount, ConstantSourceSection, ConstantSourceCount, FrozenValueNodeSection, FrozenValueNodeCount) = usesScalarFrozenValues
            ? CombineScalarFrozenValueTables(firstIndex, secondIndex)
            : (BlobSection: [], BlobCount: 0,
                ConstantSourceSection: [],
                ConstantSourceCount: 0,
                FrozenValueNodeSection: [],
                FrozenValueNodeCount: 0);
        var methodCount = firstMethodCount + secondMethodCount;

        var typeSpecRows = new List<ReadOnlyMemory<byte>>(
            firstTypeSpecCount + secondTypeSpecCount);
        for (var row = 1; row <= firstTypeSpecCount; row++)
        {
            typeSpecRows.Add(firstTables.TypeSpecs.GetRow(row).ToArray());
        }

        uint RemapTypeSpecRowReference(uint typeSpecRef)
        {
            return typeSpecRef != 0
                        && (typeSpecRef & 0xFF00_0000u) == 0
                        ? checked(typeSpecRef + (uint)firstTypeSpecCount)
                        : typeSpecRef;
        }

        byte[] RemapTypeSpecRow(ReadOnlySpan<byte> sourceRow)
        {
            var row = sourceRow.ToArray();
            switch (row[0])
            {
                case 0x01:
                    break;

                case 0x02:
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        row.AsSpan(2),
                        checked(BinaryPrimitives.ReadUInt32LittleEndian(
                            sourceRow[2..])
                            + (uint)firstTypeDefCount));
                    break;

                case 0x03:
                    BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(2),
                        checked(BinaryPrimitives.ReadUInt32LittleEndian(
                            sourceRow[2..]) + (uint)firstImportCount));
                    break;

                case >= 0x06 and <= 0x0B:
                    BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(2),
                        RemapTypeSpecRowReference(
                            BinaryPrimitives.ReadUInt32LittleEndian(
                                sourceRow[2..])));
                    break;

                case 0x04:
                    BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(2),
                        checked(BinaryPrimitives.ReadUInt32LittleEndian(
                            sourceRow[2..])
                            + (uint)firstGenericParamCount));
                    break;

                case 0x05:
                    BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(2),
                        RemapTypeSpecRowReference(
                            BinaryPrimitives.ReadUInt32LittleEndian(
                                sourceRow[2..])));
                    for (var offset = 6; offset < row.Length;
                        offset += sizeof(uint))
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(
                            row.AsSpan(offset),
                            RemapTypeSpecRowReference(
                                BinaryPrimitives.ReadUInt32LittleEndian(
                                    sourceRow[offset..])));
                    }
                    break;

                case 0x0C:
                    BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(3),
                        RemapTypeSpecRowReference(
                            BinaryPrimitives.ReadUInt32LittleEndian(
                                sourceRow[3..])));
                    break;

                case 0x11:
                    BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(2),
                        RemapTypeSpecRowReference(
                            BinaryPrimitives.ReadUInt32LittleEndian(
                                sourceRow[2..])));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"The A2 fixture merger does not admit TypeSpec form 0x{row[0]:X2}.");
            }

            return row;
        }

        for (var row = 1; row <= secondTypeSpecCount; row++)
        {
            if (secondTables.NativeFunctionPointerAnchors
                .HasAnchorForTypeSpec(row))
            {
                var anchorMethod = firstMethodCount
                    + secondTables.NativeFunctionPointerAnchors
                        .GetAnchorMethodDefRow(row);
                typeSpecRows.Add(
                    TrRmTypeSpecTableWriter.NativeFunctionPointer(
                        secondTables.NativeFunctionPointerAnchors
                            .GetCallingConvention(row), (uint)anchorMethod));
            }
            else
            {
                typeSpecRows.Add(RemapTypeSpecRow(
                    secondTables.TypeSpecs.GetRow(row)));
            }
        }

        var typeDefCount = firstTables.TypeDefs.Count
            + secondTables.TypeDefs.Count;
        var importCount = firstImportCount + secondImportCount;
        var genericParamCount = firstTables.GenericParams.Count
            + secondTables.GenericParams.Count;
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            typeDefCount, importCount, genericParamCount, 0, schema,
            methodDefCount: methodCount);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, typeDefCount, importCount,
            genericParamCount, 0,
            schema,
            methodDefCount: methodCount);

        var signatureRows = new List<TrRmMethodSigRow>(
            firstSignatureCount + secondSignatureCount);
        var paramRows = new List<TrRmParamRow>(
            firstTables.Signatures.ParamCount
            + secondTables.Signatures.ParamCount);
        for (var artifact = 0; artifact < 2; artifact++)
        {
            var source = artifact == 0 ? firstTables.Signatures
                : secondTables.Signatures;
            var sourceParameters = artifact == 0
                ? firstTables.Parameters : secondTables.Parameters;
            var signatureCount = artifact == 0 ? firstSignatureCount
                : secondSignatureCount;
            var signatureOffset = artifact == 0 ? 0 : firstSignatureCount;
            var typeOffset = artifact == 0 ? 0 : firstTypeSpecCount;
            for (var row = 1; row <= signatureCount; row++)
            {
                var resultType = source.GetResultTypeSpec(row);
                var receiverType = source.GetReceiverTypeSpec(row);
                if (artifact != 0)
                {
                    if (resultType != 0 && (resultType & 0xFF00_0000u) == 0)
                    {
                        resultType = checked(resultType + (uint)typeOffset);
                    }
                    if (receiverType != 0
                        && (receiverType & 0xFF00_0000u) == 0)
                    {
                        receiverType = checked(receiverType + (uint)typeOffset);
                    }
                }

                var (parameterStart, parameterCount)
                    = source.GetParamRange(row);
                var (genericStart, genericCount)
                    = source.GetGenericRange(row);
                Assert.That(genericStart, Is.Zero);
                Assert.That(genericCount, Is.Zero);
                var parameterFirst = 0;
                if (parameterCount != 0)
                {
                    parameterFirst = paramRows.Count + 1;
                    for (var ordinal = 0; ordinal < parameterCount; ordinal++)
                    {
                        var sourceParamRow = parameterStart + ordinal;
                        var typeSpecRef
                            = sourceParameters.GetTypeSpec(sourceParamRow);
                        if (artifact != 0
                            && (typeSpecRef & 0xFF00_0000u) == 0)
                        {
                            typeSpecRef = checked(typeSpecRef
                                + (uint)typeOffset);
                        }

                        paramRows.Add(new TrRmParamRow(
                            signatureOffset + row, ordinal, typeSpecRef,
                            sourceParameters.GetMode(sourceParamRow),
                            sourceParameters.GetFlags(sourceParamRow)));
                    }
                }

                signatureRows.Add(new TrRmMethodSigRow(resultType,
                    receiverType, parameterFirst, parameterCount, 0, 0));
            }
        }

        var signatureBytes = TrRmMethodSigTableWriter.Encode(signatureRows,
            typeSpecs, paramRows.Count, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            signatureRows.Count, typeSpecs, paramRows.Count, 0);
        var parameterBytes = paramRows.Count == 0 ? []
            : TrRmParamTableWriter.Encode(paramRows, signatures, typeSpecs,
                schema);
        var parameters = paramRows.Count == 0
            ? default
            : new TrRmParamTableReader(parameterBytes, paramRows.Count,
                signatures, typeSpecs, schema);
        var moduleRows = new List<TrRmModuleRow>(
            firstModuleCount + secondModuleCount);
        for (var moduleRow = 1; moduleRow <= firstTables.Modules.Count;
            moduleRow++)
        {
            var (targetRequirement, runtimePolicy)
                = firstTables.Modules.GetRequirements(moduleRow);
            var (moduleRefStart, _) = firstTables.Modules
                .GetModuleRefRange(moduleRow);
            var (typeDefStart, _) = firstTables.Modules
                .GetTypeDefRange(moduleRow);
            var (methodDefStart, _) = firstTables.Modules
                .GetMethodDefRange(moduleRow);
            var (featureStart, moduleFeatureCount)
                = firstTables.Modules.GetFeatureRange(moduleRow);
            var identityStringRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(
                    firstModuleBytes.Slice((moduleRow - 1) * 48, 4)));
            moduleRows.Add(new TrRmModuleRow(identityStringRow,
                targetRequirement, runtimePolicy,
                firstTables.Modules.GetEntryMethod(moduleRow),
                moduleRefStart, typeDefStart, methodDefStart, 1,
                featureStart, moduleFeatureCount,
                firstTables.Modules.GetGoverningRuleEdition(moduleRow)));
        }

        var (secondTargetRequirement, secondRuntimePolicy)
            = secondTables.Modules.GetRequirements(1);
        var secondIdentityRow = checked((int)secondStringMap[
            (int)BinaryPrimitives.ReadUInt32LittleEndian(secondModuleBytes)]);
        var (secondModuleRefStart, _) = secondTables.Modules
            .GetModuleRefRange(1);
        var (secondTypeDefStart, _) = secondTables.Modules
            .GetTypeDefRange(1);
        var (secondMethodDefStart, _) = secondTables.Modules
            .GetMethodDefRange(1);
        var (secondFeatureStart, secondModuleFeatureCount)
            = secondTables.Modules.GetFeatureRange(1);
        Assert.That(secondModuleFeatureCount, Is.EqualTo(secondFeatureCount));
        var secondEntryMethod = secondTables.Modules.GetEntryMethod(1);
        if (secondEntryMethod != 0)
        {
            secondEntryMethod += firstMethodCount;
        }

        var moduleRefCount = firstModuleRefCount + secondModuleRefCount;
        moduleRows.Add(new TrRmModuleRow(secondIdentityRow,
            secondTargetRequirement, secondRuntimePolicy, secondEntryMethod,
            secondModuleRefStart + firstModuleRefCount,
            secondTypeDefStart, secondMethodDefStart + firstMethodCount, 1,
            secondFeatureStart + firstFeatureCount,
            secondModuleFeatureCount,
            secondTables.Modules.GetGoverningRuleEdition(1)));
        var moduleBytes = TrRmModuleTableWriter.Encode(moduleRows,
            strings, moduleRefCount, 0, methodCount, 0, featureCount);
        var modules = new TrRmModuleTableReader(moduleBytes, moduleRows.Count, strings,
            moduleRefCount, 0, methodCount, 0, featureCount);
        var moduleRefRows = new List<TrRmModuleRefRow>(moduleRefCount);
        for (var artifact = 0; artifact < 2; artifact++)
        {
            var sourceRows = artifact == 0 ? firstModuleRefBytes
                : secondModuleRefBytes;
            var sourceCount = artifact == 0 ? firstModuleRefCount
                : secondModuleRefCount;
            for (var row = 0; row < sourceCount; row++)
            {
                var sourceRow = sourceRows.Slice(row * 12, 12);
                var identityStringRow = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(sourceRow));
                if (artifact != 0)
                {
                    identityStringRow = secondStringMap[identityStringRow];
                }

                moduleRefRows.Add(new TrRmModuleRefRow(identityStringRow,
                    BinaryPrimitives.ReadUInt32LittleEndian(sourceRow[4..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(sourceRow[8..])));
            }
        }

        var moduleRefBytes = TrRmModuleRefTableWriter.Encode(moduleRefRows,
            strings, modules);
        var moduleRefs = moduleRefRows.Count == 0
            ? default
            : new TrRmModuleRefTableReader(moduleRefBytes,
                moduleRefRows.Count, strings, modules);
        var contractCount = firstContractCount + secondContractCount;
        var bodyCount = firstBodyCount + secondBodyCount;
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, contractCount);

        var methodRows = new List<TrRmMethodDefRow>(methodCount);
        for (var artifact = 0; artifact < 2; artifact++)
        {
            var source = artifact == 0 ? firstTables.Methods
                : secondTables.Methods;
            var sourceRows = artifact == 0 ? firstMethodBytes
                : secondMethodBytes;
            var (methodStart, methodEnd) = artifact == 0
                ? (1, source.Count + 1)
                : secondTables.Modules.GetMethodDefRange(1);
            var signatureOffset = artifact == 0 ? 0 : firstSignatureCount;
            var contractOffset = artifact == 0 ? 0 : firstContractCount;
            var bodyOffset = artifact == 0 ? 0 : firstBodyCount;
            for (var row = methodStart; row < methodEnd; row++)
            {
                var sourceRow = sourceRows.Slice((row - 1) * 28, 28);
                var nameRow = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(sourceRow[4..]));
                if (artifact != 0)
                {
                    nameRow = secondStringMap[nameRow];
                }

                var (contractFirst, methodContracts)
                    = source.GetContractRange(row);
                if (methodContracts != 0)
                {
                    contractFirst += contractOffset;
                }
                var bodyRow = source.GetBodyRow(row);
                if (bodyRow != 0)
                {
                    bodyRow += bodyOffset;
                }

                methodRows.Add(new TrRmMethodDefRow(
                    source.GetOwnerType(row), nameRow,
                    source.GetSignature(row) + signatureOffset,
                    source.GetFlags(row), contractFirst, methodContracts,
                    bodyRow));
            }
        }

        var methodBytes = TrRmMethodDefTableWriter.Encode(methodRows,
            strings, modules, typeDefs, signatures, contractCount, bodyCount);
        var methods = new TrRmMethodDefTableReader(methodBytes,
            methodRows.Count, strings, modules, typeDefs, signatures,
            contractCount, bodyCount);

        var importRows = new List<TrRmImportRow>(
            firstImportCount + secondImportCount);
        for (var artifact = 0; artifact < 2; artifact++)
        {
            var sourceRows = artifact == 0 ? firstImportBytes
                : secondImportBytes;
            var sourceCount = artifact == 0 ? firstImportCount
                : secondImportCount;
            for (var row = 0; row < sourceCount; row++)
            {
                var sourceRow = sourceRows.Slice(row * 12, 12);
                var moduleRefRow = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(sourceRow));
                var keyStringRow = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(sourceRow[4..]));
                var expectedDefinition
                    = BinaryPrimitives.ReadUInt32LittleEndian(sourceRow[8..]);
                if (artifact != 0)
                {
                    moduleRefRow += firstModuleRefCount;
                    keyStringRow = secondStringMap[keyStringRow];
                    expectedDefinition = (expectedDefinition
                        & 0xFF00_0000u)
                        | ((expectedDefinition & 0x00FF_FFFFu)
                            + (uint)firstMethodCount);
                }

                importRows.Add(new TrRmImportRow(moduleRefRow,
                    keyStringRow, expectedDefinition));
            }
        }

        var importBytes = importRows.Count == 0
            ? []
            : TrRmImportTableWriter.Encode(importRows, strings, moduleRefs,
                typeDefs, methods, default, default, default, default,
                features, modules, schema);
        var imports = importRows.Count == 0
            ? default
            : new TrRmImportTableReader(importBytes, importRows.Count,
                strings, moduleRefs, typeDefs, methods, default, default,
                default, default, features, modules, schema);

        var exportRows = new List<TrRmExportRow>(
            firstExportCount + secondExportCount);
        for (var artifact = 0; artifact < 2; artifact++)
        {
            var sourceRows = artifact == 0 ? firstExportBytes
                : secondExportBytes;
            var sourceCount = artifact == 0 ? firstExportCount
                : secondExportCount;
            for (var row = 0; row < sourceCount; row++)
            {
                var sourceRow = sourceRows.Slice(row * 8, 8);
                var definition
                    = BinaryPrimitives.ReadUInt32LittleEndian(sourceRow);
                var keyStringRow = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(sourceRow[4..]));
                if (artifact != 0)
                {
                    definition = (definition & 0xFF00_0000u)
                        | ((definition & 0x00FF_FFFFu)
                            + (uint)firstMethodCount);
                    keyStringRow = secondStringMap[keyStringRow];
                }

                exportRows.Add(new TrRmExportRow(definition, keyStringRow));
            }
        }

        var predicateGraphs = new List<TrRmPredicateGraphRow>(
            firstTables.PredicateGraphs.Count
                + secondTables.PredicateGraphs.Count);
        var predicateFootprints = new List<TrRmPredicateReadFootprintRow>(
            firstTables.PredicateReadFootprints.Count
                + secondTables.PredicateReadFootprints.Count);
        var predicateRoots = new List<TrRmPredicateReadRootRow>(
            firstTables.PredicateReadRoots.Count
                + secondTables.PredicateReadRoots.Count);
        var predicateNodes = new List<TrRmPredicateNodeRow>(
            firstTables.PredicateNodes.Count
                + secondTables.PredicateNodes.Count);
        var predicateOperands
            = new List<TrRmPredicateNodeOperandRow>(
                firstTables.PredicateNodeOperands.Count
                    + secondTables.PredicateNodeOperands.Count);
        var firstGraphCount = firstTables.PredicateGraphs.Count;
        var firstFootprintCount
            = firstTables.PredicateReadFootprints.Count;
        var firstRootCount = firstTables.PredicateReadRoots.Count;
        var firstNodeCount = firstTables.PredicateNodes.Count;
        var firstOperandCount
            = firstTables.PredicateNodeOperands.Count;
        void AppendPredicateTables(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            bool isSecond)
        {
            var footprintOffset = isSecond
                ? firstFootprintCount : 0;
            var rootOffset = isSecond
                ? firstRootCount : 0;
            var nodeOffset = isSecond
                ? firstNodeCount : 0;
            var operandOffset = isSecond
                ? firstOperandCount : 0;
            Assert.That(tables.PredicateReadDependencies.Count, Is.Zero);
            for (var footprint = 1;
                footprint <= tables.PredicateReadFootprints.Count;
                footprint++)
            {
                var rootStart = tables.PredicateReadFootprints
                    .GetRootFirstRow(footprint);
                var rootCount = tables.PredicateReadFootprints
                    .GetRootRowCount(footprint);
                Assert.That(tables.PredicateReadFootprints
                    .GetDependencyRowCount(footprint), Is.Zero);
                predicateFootprints.Add(
                    new TrRmPredicateReadFootprintRow(
                        rootCount == 0 ? 0
                            : checked(rootOffset + rootStart),
                        rootCount, 0, 0));
            }

            for (var root = 1;
                root <= tables.PredicateReadRoots.Count; root++)
            {
                predicateRoots.Add(new TrRmPredicateReadRootRow(
                    tables.PredicateReadRoots.GetRootKind(root),
                    tables.PredicateReadRoots.GetRootIdentity(root)));
            }

            for (var operand = 1;
                operand <= tables.PredicateNodeOperands.Count; operand++)
            {
                var operandKind = tables.PredicateNodeOperands
                    .GetOperandKind(operand);
                Assert.That(operandKind, Is.EqualTo((byte)1));
                var operandRef = tables.PredicateNodeOperands
                    .GetOperandRef(operand);
                if (isSecond)
                {
                    operandRef = checked(operandRef + (uint)rootOffset);
                }

                predicateOperands.Add(new TrRmPredicateNodeOperandRow(
                    operandKind, operandRef));
            }

            for (var node = 1; node <= tables.PredicateNodes.Count; node++)
            {
                var operandFirst = tables.PredicateNodes
                    .GetOperandFirstRow(node);
                var operandCount = tables.PredicateNodes.GetOperandCount(node);
                var resultType = tables.PredicateNodes
                    .GetResultTypeSpecRef(node);
                if (isSecond)
                {
                    resultType = RemapTypeSpecRowReference(resultType);
                }

                predicateNodes.Add(new TrRmPredicateNodeRow(
                    tables.PredicateNodes.GetNodeKind(node), resultType,
                    operandCount == 0 ? 0
                        : checked(operandFirst + (uint)operandOffset),
                    operandCount));
            }

            for (var graph = 1;
                graph <= tables.PredicateGraphs.Count; graph++)
            {
                predicateGraphs.Add(new TrRmPredicateGraphRow(
                    checked(tables.PredicateGraphs.GetRootNodeRow(graph)
                        + nodeOffset),
                    checked(tables.PredicateGraphs
                        .GetReadFootprintRow(graph)
                        + footprintOffset)));
            }
        }

        AppendPredicateTables(firstTables, isSecond: false);
        AppendPredicateTables(secondTables, isSecond: true);

        var contractRows = new List<TrRmContractRow>(contractCount);
        for (var artifact = 0; artifact < 2; artifact++)
        {
            var source = artifact == 0 ? firstTables.Contracts
                : secondTables.Contracts;
            var sourceCount = artifact == 0 ? firstContractCount
                : secondContractCount;
            var methodOffset = artifact == 0 ? 0 : firstMethodCount;
            for (var row = 1; row <= sourceCount; row++)
            {
                var sourceRow = source.GetRow(row);
                var owner = BinaryPrimitives.ReadUInt32LittleEndian(sourceRow);
                var kind = BinaryPrimitives.ReadUInt16LittleEndian(
                    sourceRow[4..]);
                var payload = sourceRow[8..].ToArray();
                if (artifact != 0)
                {
                    Assert.That(owner >> 24, Is.EqualTo(0x16));
                    owner = (owner & 0xFF00_0000u)
                        | ((owner & 0x00FF_FFFFu) + (uint)methodOffset);
                    if (kind == 2)
                    {
                        var graph = BinaryPrimitives.ReadUInt32LittleEndian(
                            payload.AsSpan(4));
                        BinaryPrimitives.WriteUInt32LittleEndian(
                            payload.AsSpan(4),
                            checked(graph
                                + (uint)firstGraphCount));
                    }
                    else if (kind == 0x0B)
                    {
                        var graph = BinaryPrimitives.ReadUInt32LittleEndian(
                            payload.AsSpan(8));
                        BinaryPrimitives.WriteUInt32LittleEndian(
                            payload.AsSpan(8),
                            checked(graph
                                + (uint)firstGraphCount));
                    }
                }

                contractRows.Add(new TrRmContractRow(owner,
                    kind, payload));
            }
        }

        var contractBytes = TrRmContractTableWriter
            .EncodeForeignInvocationDraft(contractRows, typeDefs, methods,
                signatures, parameters, typeSpecs, features, modules,
                predicateGraphCount: predicateGraphs.Count, schema: schema);
        var contracts = new TrRmContractTableReader(contractBytes,
            contractRows.Count, typeDefs, methods, signatures, parameters,
            typeSpecs, 0, features, modules, schema,
            predicateGraphs.Count);

        var bodyRows = new List<ReadOnlyMemory<byte>>(bodyCount);
        for (var artifact = 0; artifact < 2; artifact++)
        {
            var sourceBodies = artifact == 0 ? firstTables.Bodies
                : secondTables.Bodies;
            var sourceContracts = artifact == 0 ? firstContractCount
                : secondContractCount;
            var methodOffset = artifact == 0 ? 0 : firstMethodCount;
            var typeOffset = artifact == 0 ? 0 : firstTypeSpecCount;
            var importOffset = artifact == 0 ? 0 : firstImportCount;
            for (var bodyRow = 1; bodyRow <= sourceBodies.Count; bodyRow++)
            {
                var body = sourceBodies.GetRegisterBody(bodyRow,
                    sourceContracts);
                bodyRows.Add(RemapNativeAddressBody(body, typeOffset,
                    methodOffset, importOffset, contractCount, schema,
                    artifact == 0 ? 0 : firstTables.ConstantSources.Count,
                    artifact == 0 ? 0
                        : firstTables.FixedArrayLayouts.Count));
            }
        }

        var bodyBytes = TrRmVariableTableWriter.EncodeBodies(bodyRows);
        var nativeImportRows = new List<TrRmNativeImportRow>(
            firstNativeImportCount + secondNativeImportCount);
        for (var artifact = 0; artifact < 2; artifact++)
        {
            var source = artifact == 0 ? firstTables.StandardNativeImports
                : secondTables.StandardNativeImports;
            var sourceMethods = artifact == 0 ? firstTables.Methods
                : secondTables.Methods;
            var stringMap = artifact == 0 ? null : secondStringMap;
            var methodOffset = artifact == 0 ? 0 : firstMethodCount;
            for (var method = 1; method <= sourceMethods.Count; method++)
            {
                if (!source.HasImportForMethod(method))
                {
                    continue;
                }

                var import = source.GetImportForMethod(method);
                nativeImportRows.Add(import with
                {
                    OwnerRef = import.OwnerRef + methodOffset,
                    LibraryNameStringRow = stringMap is null
                        ? import.LibraryNameStringRow
                        : stringMap[import.LibraryNameStringRow],
                    EntryNameStringRow = import.EntryNameStringRow == 0
                        ? 0
                        : stringMap is null ? import.EntryNameStringRow
                            : stringMap[import.EntryNameStringRow],
                });
            }
        }

        var targetProfileRows
            = new List<TrRmTargetProfileRow>(
                firstTables.TargetProfiles.Count
                    + secondTables.TargetProfiles.Count);
        var targetProfileRowByIdentity
            = new Dictionary<(uint ProfileId, uint RuleRevision), uint>();
        for (var row = 1; row <= firstTables.TargetProfiles.Count; row++)
        {
            var profile = firstTables.TargetProfiles.GetProfile(row);
            targetProfileRows.Add(profile);
            targetProfileRowByIdentity.Add(
                (profile.ProfileId, profile.RuleRevision),
                checked((uint)targetProfileRows.Count));
        }

        var secondTargetProfileRowMap
            = new uint[secondTables.TargetProfiles.Count + 1];
        for (var row = 1; row <= secondTables.TargetProfiles.Count; row++)
        {
            var profile = secondTables.TargetProfiles.GetProfile(row);
            if (!targetProfileRowByIdentity.TryGetValue(
                    (profile.ProfileId, profile.RuleRevision),
                    out var mappedRow))
            {
                targetProfileRows.Add(profile);
                mappedRow = checked((uint)targetProfileRows.Count);
                targetProfileRowByIdentity.Add(
                    (profile.ProfileId, profile.RuleRevision), mappedRow);
            }

            secondTargetProfileRowMap[row] = mappedRow;
        }

        var targetProfileBytes = targetProfileRows.Count == 0 ? []
            : TrRmTargetProfileTableWriter.Encode(targetProfileRows, schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, targetProfileRows.Count, schema);
        var fixedArrayLayoutRows = new List<TrRmFixedArrayLayoutRow>(
            firstTables.FixedArrayLayouts.Count
                + secondTables.FixedArrayLayouts.Count);
        for (var row = 1; row <= firstTables.FixedArrayLayouts.Count; row++)
        {
            fixedArrayLayoutRows.Add(
                firstTables.FixedArrayLayouts.GetRow(row));
        }

        for (var row = 1; row <= secondTables.FixedArrayLayouts.Count; row++)
        {
            var layout = secondTables.FixedArrayLayouts.GetRow(row);
            fixedArrayLayoutRows.Add(layout with {
                OwnerModuleRow = checked(layout.OwnerModuleRow
                    + firstModuleCount),
                ArrayTypeSpecRow = checked(layout.ArrayTypeSpecRow
                    + firstTypeSpecCount),
                TargetProfileRef = secondTargetProfileRowMap[
                    checked((int)layout.TargetProfileRef)],
            });
        }

        var fixedArrayLayoutBytes = fixedArrayLayoutRows.Count == 0 ? []
            : TrRmFixedArrayLayoutTableWriter.Encode(
                fixedArrayLayoutRows, typeSpecs, targetProfiles, features,
                modules, schema);
        var layouts = new TrRmLayoutTableReader([], 0, typeDefs,
            targetProfiles, 0);
        var nativeImportBytes = nativeImportRows.Count == 0 ? []
            : TrRmNativeImportTableWriter.Encode(nativeImportRows, strings,
                methods, default, typeDefs, imports, modules, signatures,
                parameters, typeSpecs, contracts, layouts);
        var predicateGraphBytes = TrRmPredicateGraphTableWriter.Encode(
            predicateGraphs, predicateNodes.Count,
            predicateFootprints.Count);
        var predicateFootprintBytes
            = TrRmPredicateReadFootprintTableWriter.Encode(
                predicateFootprints, predicateRoots.Count, 0);
        var predicateRootBytes = TrRmPredicateReadRootTableWriter.Encode(
            predicateRoots, 0, 0, schema);
        var predicateNodeBytes = TrRmPredicateNodeTableWriter.Encode(
            predicateNodes, predicateOperands.Count, typeSpecs, schema);
        var predicateCounts = new TrRmPredicateTableRowCounts(
            predicateRoots.Count, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var predicateOperandBytes
            = TrRmPredicateNodeOperandTableWriter.Encode(predicateOperands,
                predicateCounts, schema);
        var sections = new List<TrRmSection>
        {
            new TrRmSection(0x0001, 0, (uint)moduleRows.Count, moduleBytes),
            new TrRmSection(0x0002, 0, (uint)stringRows.Count, stringBytes),
        };
        if (BlobCount != 0)
        {
            sections.Add(new TrRmSection(0x0003, 0,
                (uint)BlobCount,
                BlobSection));
        }
        sections.Add(new TrRmSection(0x0004, 0, (uint)featureCount,
            featureBytes));
        if (moduleRefRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0010, 0,
                (uint)moduleRefRows.Count, moduleRefBytes));
        }

        sections.Add(new TrRmSection(0x0011, 0,
            (uint)typeSpecRows.Count, typeSpecBytes));
        if (fixedArrayLayoutRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0012, 0,
                (uint)fixedArrayLayoutRows.Count, fixedArrayLayoutBytes));
        }
        sections.AddRange([
            new TrRmSection(0x0016, 0, (uint)methodRows.Count, methodBytes),
            new TrRmSection(0x0017, 0, (uint)signatureRows.Count,
                signatureBytes),
        ]);
        if (paramRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0018, 0,
                (uint)paramRows.Count, parameterBytes));
        }
        sections.Add(new TrRmSection(0x001B, 0,
            (uint)contractRows.Count, contractBytes));
        if (importRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001C, 0,
                (uint)importRows.Count, importBytes));
        }

        if (exportRows.Count != 0)
        {
            var exportBytes = TrRmExportTableWriter.Encode(exportRows,
                strings, typeDefs, methods, default, imports, default, default,
                default, features, modules,
                schema);
            sections.Add(new TrRmSection(0x001D, 0,
                (uint)exportRows.Count, exportBytes));
        }

        sections.AddRange([
            new TrRmSection(0x0020, 0, (uint)bodyRows.Count, bodyBytes),
        ]);
        if (nativeImportRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0021, 0,
                (uint)nativeImportRows.Count, nativeImportBytes));
        }
        if (predicateGraphs.Count != 0)
        {
            sections.Add(new TrRmSection(0x0037, 0,
                (uint)predicateGraphs.Count, predicateGraphBytes));
            sections.Add(new TrRmSection(0x0038, 0,
                (uint)predicateFootprints.Count,
                predicateFootprintBytes));
            sections.Add(new TrRmSection(0x0039, 0,
                (uint)predicateRoots.Count, predicateRootBytes));
            sections.Add(new TrRmSection(0x003B, 0,
                (uint)predicateNodes.Count, predicateNodeBytes));
            sections.Add(new TrRmSection(0x003C, 0,
                (uint)predicateOperands.Count, predicateOperandBytes));
        }
        if (ConstantSourceCount != 0)
        {
            sections.Add(new TrRmSection(0x0030, 0,
                (uint)ConstantSourceCount,
                ConstantSourceSection));
        }
        if (FrozenValueNodeCount != 0)
        {
            sections.Add(new TrRmSection(0x003F, 0,
                (uint)FrozenValueNodeCount,
                FrozenValueNodeSection));
        }
        if (targetProfileRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0048, 0,
                (uint)targetProfileRows.Count, targetProfileBytes));
        }
        var combinedArtifact = TrRmArtifactWriter.Encode(sections, schema);
        var combinedIndex = usesScalarFrozenValues
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                    combinedArtifact)
            : usesA2EU
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                    combinedArtifact)
            : usesA2
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyDraft(
                    combinedArtifact)
            : TrRmArtifactIndex.ForForeignInvocationDraft(combinedArtifact);
        var combinedTables = usesScalarFrozenValues
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                    combinedIndex)
            : usesA2EU
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                    combinedIndex)
            : usesA2
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    combinedIndex)
            : TrRmStructuralMetadataValidator
                .ValidateForeignInvocationDraftAndGetExecutionTables(
                    combinedIndex);
        Assert.That(combinedTables.Modules.Count,
            Is.EqualTo(firstModuleCount + secondModuleCount));
        for (var method = 1; method <= combinedTables.Methods.Count; method++)
        {
            if (combinedTables.Methods.GetBodyRow(method) != 0)
            {
                combinedTables.ValidateRegisterBodyMapsForMethod(method);
            }
        }

        return combinedArtifact;
    }

    private static (
        byte[] BlobSection,
        int BlobCount,
        byte[] ConstantSourceSection,
        int ConstantSourceCount,
        byte[] FrozenValueNodeSection,
        int FrozenValueNodeCount) CombineScalarFrozenValueTables(
        TrRmArtifactIndex firstIndex, TrRmArtifactIndex secondIndex)
    {
        var firstHasBlobs = firstIndex.TryGetSection(0x0003,
            out var firstBlobBytes, out var firstBlobCount);
        var secondHasBlobs = secondIndex.TryGetSection(0x0003,
            out var secondBlobBytes, out var secondBlobCount);
        var firstHasSources = firstIndex.TryGetSection(0x0030,
            out var firstSourceBytes, out var firstSourceCount);
        var secondHasSources = secondIndex.TryGetSection(0x0030,
            out var secondSourceBytes, out var secondSourceCount);
        var firstHasNodes = firstIndex.TryGetSection(0x003F,
            out var firstNodeBytes, out var firstNodeCount);
        var secondHasNodes = secondIndex.TryGetSection(0x003F,
            out var secondNodeBytes, out var secondNodeCount);
        Assert.That(firstHasBlobs, Is.EqualTo(firstBlobCount != 0));
        Assert.That(secondHasBlobs, Is.EqualTo(secondBlobCount != 0));
        Assert.That(firstHasSources, Is.EqualTo(firstSourceCount != 0));
        Assert.That(secondHasSources, Is.EqualTo(secondSourceCount != 0));
        Assert.That(firstHasNodes, Is.EqualTo(firstNodeCount != 0));
        Assert.That(secondHasNodes, Is.EqualTo(secondNodeCount != 0));
        Assert.That(firstSourceCount, Is.EqualTo(firstNodeCount));
        Assert.That(secondSourceCount, Is.EqualTo(secondNodeCount));

        var blobs = new List<ReadOnlyMemory<byte>>(
            firstBlobCount + secondBlobCount);
        if (firstBlobCount != 0)
        {
            var reader = new TrRmBlobTableReader(firstBlobBytes,
                firstBlobCount);
            for (var row = 1; row <= reader.Count; row++)
            {
                blobs.Add(reader.GetRow(row).ToArray());
            }
        }
        if (secondBlobCount != 0)
        {
            var reader = new TrRmBlobTableReader(secondBlobBytes,
                secondBlobCount);
            for (var row = 1; row <= reader.Count; row++)
            {
                blobs.Add(reader.GetRow(row).ToArray());
            }
        }
        Assert.That(blobs.Count, Is.LessThanOrEqualTo(0xFF_FFFF));
        var blobSection = blobs.Count == 0
            ? []
            : TrRmVariableTableWriter.EncodeBlobs(blobs);

        var sourceCount = checked(firstSourceCount + secondSourceCount);
        Assert.That(sourceCount, Is.LessThanOrEqualTo(0xFF_FFFF));
        var sourceSection = new byte[checked(sourceCount * 8)];
        firstSourceBytes.CopyTo(sourceSection);
        for (var row = 0; row < secondSourceCount; row++)
        {
            var sourceRow = secondSourceBytes.Slice(row * 8, 8);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(sourceRow),
                Is.EqualTo(1));
            var payloadReference
                = BinaryPrimitives.ReadUInt32LittleEndian(sourceRow[4..]);
            Assert.That(payloadReference, Is.InRange(1u,
                (uint)secondNodeCount));
            var destination = sourceSection.AsSpan(
                (firstSourceCount + row) * 8, 8);
            sourceRow.CopyTo(destination);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[4..],
                checked(payloadReference + (uint)firstNodeCount));
        }

        var nodeCount = checked(firstNodeCount + secondNodeCount);
        Assert.That(nodeCount, Is.LessThanOrEqualTo(0xFF_FFFF));
        var nodeSection = new byte[checked(nodeCount * 24)];
        firstNodeBytes.CopyTo(nodeSection);
        for (var row = 0; row < secondNodeCount; row++)
        {
            var sourceRow = secondNodeBytes.Slice(row * 24, 24);
            var destination = nodeSection.AsSpan(
                (firstNodeCount + row) * 24, 24);
            sourceRow.CopyTo(destination);
            var blobReference = BinaryPrimitives.ReadUInt32LittleEndian(
                destination[8..]);
            if (blobReference != 0)
            {
                Assert.That(blobReference, Is.InRange(1u,
                    (uint)secondBlobCount));
                BinaryPrimitives.WriteUInt32LittleEndian(destination[8..],
                    checked(blobReference + (uint)firstBlobCount));
            }
        }

        for (var row = 0; row < nodeCount; row++)
        {
            var node = nodeSection.AsSpan(row * 24, 24);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(node)
                & 0xFF00_0000u, Is.EqualTo(0xF200_0000u));
            Assert.That(node[4], Is.EqualTo(1));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(node[12..]),
                Is.Zero);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(node[16..]),
                Is.Zero);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(node[20..]),
                Is.Zero);
            var blobReference = BinaryPrimitives.ReadUInt32LittleEndian(
                node[8..]);
            Assert.That(blobReference, Is.InRange(1u, (uint)blobs.Count));
        }
        for (var row = 0; row < sourceCount; row++)
        {
            var source = sourceSection.AsSpan(row * 8, 8);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(source),
                Is.EqualTo(1));
            var payloadReference = BinaryPrimitives.ReadUInt32LittleEndian(
                source[4..]);
            Assert.That(payloadReference, Is.InRange(1u, (uint)nodeCount));
        }

        var sourceSectionBytes = sourceCount == 0 ? [] : sourceSection;
        var nodeSectionBytes = nodeCount == 0 ? [] : nodeSection;
        return (blobSection, blobs.Count, sourceSectionBytes, sourceCount,
            nodeSectionBytes, nodeCount);
    }

    private static byte[] RewriteA2EUScalarFrozenSection(byte[] artifact,
        int sectionKind, Action<byte[]> rewrite)
    {
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                artifact);
        var sections = new List<TrRmSection>(index.SectionCount);
        var found = false;
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            var sectionBytes = bytes.ToArray();
            if (kind == sectionKind)
            {
                rewrite(sectionBytes);
                found = true;
            }
            sections.Add(new TrRmSection(kind, flags, (uint)recordCount,
                sectionBytes));
        }

        Assert.That(found, Is.True);
        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft);
    }

    private static byte[] RewriteA2EUScalarConstantSourceMap(byte[] artifact,
        uint sourceRow)
    {
        var schema = TrRmArtifactSchema
            .ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                index);
        var mainRow = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            if (tables.Methods.GetName(methodRow) == "Main")
            {
                mainRow = methodRow;
                break;
            }
        }
        Assert.That(mainRow, Is.GreaterThan(0));
        var bodyRow = tables.Methods.GetBodyRow(mainRow);
        Assert.That(bodyRow, Is.GreaterThan(0));
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        Assert.That(body.ConstantCount, Is.GreaterThan(0));
        var constantMap = body.ConstantMap.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            constantMap.AsSpan(sizeof(uint)), sourceRow);
        var rewrittenBody = EncodeNativeAddressBody(body,
            body.TypeMap.ToArray(), body.MethodMap.ToArray(), constantMap,
            tables.Contracts.Count, schema);

        Assert.That(index.TryGetSection(0x0020, out var bodySection,
            out var bodyCount), Is.True);
        var bodyIndex = new TrRmVariableTableIndex(bodySection, bodyCount,
            1, 1);
        var bodies = new List<ReadOnlyMemory<byte>>(bodyCount);
        for (var row = 1; row <= bodyCount; row++)
        {
            bodies.Add(row == bodyRow
                ? rewrittenBody
                : bodyIndex.GetRow(row).ToArray());
        }
        var rewrittenBodySection = TrRmVariableTableWriter.EncodeBodies(
            bodies);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            sections.Add(kind == 0x0020
                ? new TrRmSection(kind, flags, (uint)recordCount,
                    rewrittenBodySection)
                : new TrRmSection(kind, flags, (uint)recordCount,
                    bytes.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] ReplaceRootBodyMethodWithForeignModuleMethod(
        byte[] artifact, string moduleIdentity)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        Assert.That(tables.Modules.TryFindModule(moduleIdentity,
            out var moduleRow), Is.True);
        var (methodStart, methodEnd)
            = tables.Modules.GetMethodDefRange(moduleRow);
        var bodyRow = 0;
        for (var methodRow = methodStart; methodRow < methodEnd; methodRow++)
        {
            bodyRow = tables.Methods.GetBodyRow(methodRow);
            if (bodyRow != 0)
            {
                break;
            }
        }
        Assert.That(bodyRow, Is.GreaterThan(0));

        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var methodMap = body.MethodMap.ToArray();
        var expectedToken = 0u;
        var foreignMethodRow = 0;
        for (var offset = 0; offset < methodMap.Length;
            offset += sizeof(uint))
        {
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                methodMap.AsSpan(offset));
            if ((token & 0xFF00_0000u) == 0x1600_0000u
                && tables.Methods.GetOwnerModule(
                    checked((int)(token & 0x00FF_FFFFu))) == moduleRow)
            {
                expectedToken = token;
                break;
            }
        }
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            if (tables.Methods.GetOwnerModule(methodRow) != moduleRow)
            {
                foreignMethodRow = methodRow;
                break;
            }
        }
        Assert.That(expectedToken, Is.Not.Zero);
        Assert.That(foreignMethodRow, Is.GreaterThan(0));
        var foreignToken = 0x1600_0000u | (uint)foreignMethodRow;
        var replaced = false;
        for (var offset = 0; offset < methodMap.Length;
            offset += sizeof(uint))
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(
                    methodMap.AsSpan(offset)) != expectedToken)
            {
                continue;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(
                methodMap.AsSpan(offset), foreignToken);
            replaced = true;
        }
        Assert.That(replaced, Is.True,
            $"Expected MethodMap token {expectedToken:X8}; "
            + $"MethodMap={Convert.ToHexString(methodMap)}.");

        var rewrittenBody = EncodeNativeAddressBody(body,
            body.TypeMap.ToArray(), methodMap, body.ConstantMap.ToArray(),
            tables.Contracts.Count,
            TrRmArtifactSchema.ForeignInvocationDraft);
        return ReplaceRegisterBody(artifact, bodyRow, rewrittenBody);
    }

    private static byte[] ReplaceRootBodyFunctionPointerTypeWithForeignModuleType(
        byte[] artifact, string moduleIdentity)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        Assert.That(tables.Modules.TryFindModule(moduleIdentity,
            out var moduleRow), Is.True);
        var (methodStart, methodEnd)
            = tables.Modules.GetMethodDefRange(moduleRow);
        var bodyRow = 0;
        for (var methodRow = methodStart; methodRow < methodEnd; methodRow++)
        {
            bodyRow = tables.Methods.GetBodyRow(methodRow);
            if (bodyRow != 0)
            {
                break;
            }
        }
        Assert.That(bodyRow, Is.GreaterThan(0));

        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var typeMap = body.TypeMap.ToArray();
        var expectedToken = 0u;
        var foreignTypeSpecRow = 0;
        for (var offset = 0; offset < typeMap.Length;
            offset += sizeof(uint))
        {
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                typeMap.AsSpan(offset));
            if ((token & 0xFF00_0000u) != 0x1100_0000u)
            {
                continue;
            }

            var typeSpecRow = checked((int)(token & 0x00FF_FFFFu));
            if (tables.NativeFunctionPointerAnchors
                    .HasAnchorForTypeSpec(typeSpecRow)
                && tables.Methods.GetOwnerModule(tables
                    .NativeFunctionPointerAnchors
                    .GetAnchorMethodDefRow(typeSpecRow)) == moduleRow)
            {
                expectedToken = token;
                break;
            }
        }
        for (var typeSpecRow = 1; typeSpecRow <= tables.TypeSpecs.Count;
            typeSpecRow++)
        {
            if (tables.NativeFunctionPointerAnchors
                    .HasAnchorForTypeSpec(typeSpecRow)
                && tables.Methods.GetOwnerModule(tables
                    .NativeFunctionPointerAnchors
                    .GetAnchorMethodDefRow(typeSpecRow)) != moduleRow)
            {
                foreignTypeSpecRow = typeSpecRow;
                break;
            }
        }
        Assert.That(expectedToken, Is.Not.Zero);
        Assert.That(foreignTypeSpecRow, Is.GreaterThan(0));
        var foreignToken = 0x1100_0000u | (uint)foreignTypeSpecRow;
        var replaced = false;
        for (var offset = 0; offset < typeMap.Length;
            offset += sizeof(uint))
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(
                    typeMap.AsSpan(offset)) != expectedToken)
            {
                continue;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(
                typeMap.AsSpan(offset), foreignToken);
            replaced = true;
        }
        Assert.That(replaced, Is.True,
            $"Expected TypeMap token {expectedToken:X8}; "
            + $"TypeMap={Convert.ToHexString(typeMap)}.");
        var rewrittenBody = EncodeNativeAddressBody(body, typeMap,
            body.MethodMap.ToArray(), body.ConstantMap.ToArray(),
            tables.Contracts.Count,
            TrRmArtifactSchema.ForeignInvocationDraft);
        return ReplaceRegisterBody(artifact, bodyRow, rewrittenBody);
    }

    private static byte[] ReplaceRegisterBody(byte[] artifact, int bodyRow,
        byte[] rewrittenBody)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        Assert.That(index.TryGetSection(0x0020, out _,
            out var bodyCount), Is.True);
        Assert.That(bodyRow, Is.InRange(1, bodyCount));
        var bodyRows = new List<ReadOnlyMemory<byte>>(bodyCount);
        for (var currentBodyRow = 1; currentBodyRow <= bodyCount;
            currentBodyRow++)
        {
            var currentBody = tables.Bodies.GetRegisterBody(
                currentBodyRow, tables.Contracts.Count);
            bodyRows.Add(currentBodyRow == bodyRow
                ? rewrittenBody
                : RemapNativeAddressBody(currentBody, 0, 0, 0,
                    tables.Contracts.Count,
                    TrRmArtifactSchema.ForeignInvocationDraft));
        }

        var rewrittenBodyBytes = TrRmVariableTableWriter
            .EncodeBodies(bodyRows);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            sections.Add(kind == 0x0020
                ? new TrRmSection(kind, flags, (uint)bodyCount,
                    rewrittenBodyBytes)
                : new TrRmSection(kind, flags, (uint)recordCount,
                    bytes.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.ForeignInvocationDraft);
    }

    private static byte[] RemapNativeAddressBody(
        TrRmRegisterBodyReader body, int typeSpecOffset,
        int methodDefOffset, int importOffset, int contractCount,
        TrRmArtifactSchema schema, int constantSourceOffset = 0,
        int fixedArrayLayoutOffset = 0)
    {
        var typeMap = body.TypeMap.ToArray();
        for (var offset = 0; offset < typeMap.Length; offset += sizeof(uint))
        {
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                typeMap.AsSpan(offset));
            if ((token & 0xFF00_0000u) == 0x1100_0000u)
            {
                token = 0x1100_0000u
                    | ((token & 0x00FF_FFFFu) + (uint)typeSpecOffset);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    typeMap.AsSpan(offset), token);
            }
        }

        var methodMap = body.MethodMap.ToArray();
        for (var offset = 0; offset < methodMap.Length;
            offset += sizeof(uint))
        {
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                methodMap.AsSpan(offset));
            if ((token & 0xFF00_0000u) == 0x1600_0000u)
            {
                token = 0x1600_0000u
                    | ((token & 0x00FF_FFFFu) + (uint)methodDefOffset);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    methodMap.AsSpan(offset), token);
            }
            else if ((token & 0xFF00_0000u) == 0x1C00_0000u)
            {
                token = 0x1C00_0000u
                    | ((token & 0x00FF_FFFFu) + (uint)importOffset);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    methodMap.AsSpan(offset), token);
            }
        }

        var constantMap = body.ConstantMap.ToArray();
        for (var offset = 0; offset < constantMap.Length;
            offset += 2 * sizeof(uint))
        {
            var typeSpecRef = BinaryPrimitives.ReadUInt32LittleEndian(
                constantMap.AsSpan(offset));
            Assert.That(typeSpecRef & 0xFF00_0000u,
                Is.EqualTo(0xF200_0000u));
            var sourceRow = BinaryPrimitives.ReadUInt32LittleEndian(
                constantMap.AsSpan(offset + sizeof(uint)));
            Assert.That(sourceRow, Is.InRange(1u, 0x00FF_FFFFu));
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(offset + sizeof(uint)),
                checked(sourceRow + (uint)constantSourceOffset));
        }

        var records = new List<ReadOnlyMemory<byte>>(body.RecordCount);
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex).ToArray();
            if (fixedArrayLayoutOffset != 0
                && record[0] == 0x04 && record.Length == 24
                && record[4] == 3 && record[13] == 3)
            {
                var layoutRow
                    = BinaryPrimitives.ReadUInt32LittleEndian(
                        record.AsSpan(16));
                if (layoutRow != 0)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        record.AsSpan(16),
                        checked(layoutRow
                            + (uint)fixedArrayLayoutOffset));
                }
            }

            records.Add(record);
        }

        return EncodeNativeAddressBody(body, typeMap, methodMap,
            constantMap, contractCount, schema, records);
    }

    private static byte[] EncodeNativeAddressBody(
        TrRmRegisterBodyReader body, byte[] typeMap, byte[] methodMap,
        byte[] constantMap, int contractCount, TrRmArtifactSchema schema,
        IReadOnlyList<ReadOnlyMemory<byte>>? remappedRecords = null)
    {
        var blocks = new List<TrRmRegisterBodyBlock>(body.BlockCount);
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            blocks.Add(new TrRmRegisterBodyBlock(
                BinaryPrimitives.ReadUInt32LittleEndian(block),
                BinaryPrimitives.ReadUInt32LittleEndian(block[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(block[8..])));
        }

        var records = remappedRecords?.ToList()
            ?? [with(body.RecordCount)];
        if (remappedRecords is null)
        {
            for (var record = 0; record < body.RecordCount; record++)
            {
                records.Add(body.GetRecord(record).ToArray());
            }
        }

        return TrRmRegisterBodyWriter.Encode(body.HomeMap.ToArray(),
            typeMap, methodMap, body.FieldMap.ToArray(),
            constantMap, blocks, records, body.Code.ToArray(),
            contractCount, schema);
    }

    [Test]
    public void NativeParameterIndirectCallBindsThroughAnInferredAddress()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern void Observe(ptr<uint32> value);

            internal void Form(ptr<uint32> value) {
                unsafe {
                    var callback = &Observe;
                    callback(value);
                }
            }
            int32 Main() { return 0; }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var import = program.Functions.Single(static function =>
            function.Name == "Observe").DirectNativeImport!;
        Assert.That(import.Parameters.Single().Extent, Is.EqualTo(1));
        var signature = program.NativeFunctionPointerSignatures.Single();
        Assert.That(signature.NativeParameters.Single().Extent, Is.EqualTo(1));
        var parameterType = signature.ParameterTypes.Single();
        Assert.That(NativePointerTypes.IsType(parameterType), Is.True);
        Assert.That(program.NativePointerTypes[NativePointerTypes.Index(parameterType)],
            Is.EqualTo(new NativePointerTypeDefinition(PrimitiveType.UInt32, false)));
    }

    [Test]
    public void NativeParameterRejectsWriteAccessForReadonlyFormal()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            internal extern void Observe(ptr<uint32> value);
            """), core);

        Assert.That(compilation.HasErrors, Is.True,
            "Write access must require a writable pointer formal.");
    }

    [Test]
    public void NativeParameterReadPreservesWritableFormalQualifier()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern void Observe(write ptr<uint32> value);

            internal void Form(ptr<uint32> value) {
                unsafe {
                    var callback = &Observe;
                    callback(value);
                }
            }
            int32 Main() { return 0; }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var formalType = program.NativeFunctionPointerSignatures.Single()
            .ParameterTypes.Single();
        Assert.That(NativePointerTypes.IsType(formalType), Is.True);
        Assert.That(program.NativePointerTypes[NativePointerTypes.Index(formalType)],
            Is.EqualTo(new NativePointerTypeDefinition(PrimitiveType.UInt32, true)));
        Assert.That(program.NativeFunctionPointerSignatures.Single()
            .NativeParameters.Single().Access, Is.EqualTo(NativeParameterAccess.Read));
        Assert.That(NativeEmitter.EmitWithNativeImports(
            program, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void ReadAccessDoesNotUpgradeWritableFormalDuringDevelopmentRoundTrip()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "Observe")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            extern void Observe(write ptr<uint32> value);

            int32 Main() {
                uint32 value = 42;
                unsafe { Observe(&value); return 0; }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitDevelopmentNativeConventionsDraft(compilation.Program!,
                "native-read-access");
        var loaded = TrRmExecutableCompiler
            .LoadDevelopmentNativeConventionsDraft(artifact);
        var main = loaded.Functions.Single(static function => function.Name == "Main");
        var addresses = new List<BoundNativeStorageAddress>();
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body, expression =>
        {
            if (expression is BoundNativeStorageAddress address)
            {
                addresses.Add(address);
            }
        }), Is.True);
        Assert.That(addresses, Has.Count.EqualTo(1));
        Assert.That(addresses[0].IsReadonly, Is.True);
        Assert.That(main.Variables[addresses[0].Variable].IsMutable, Is.False);
        var target = loaded.Functions.Single(static function => function.Name == "Observe");
        var formalType = target.Variables[0].Type;
        Assert.That(NativePointerTypes.IsType(formalType), Is.True);
        Assert.That(loaded.NativePointerTypes[NativePointerTypes.Index(formalType)],
            Is.EqualTo(new NativePointerTypeDefinition(PrimitiveType.UInt32, true)));
        Assert.That(target.DirectNativeImport!.Parameters.Single().Access,
            Is.EqualTo(NativeParameterAccess.Read));
    }

    [Test]
    public void NativeParameterIndirectCallRejectsDifferentPointeeType()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern void Observe(ptr<uint32> value);

            internal void Form(ptr<uint64> value) {
                unsafe {
                    var callback = &Observe;
                    callback(value);
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.True,
            "The inferred pointer signature must retain the target's pointee type.");
    }

    internal static byte[] CreateStorageAddressArtifact(string moduleIdentity,
        string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-addresses-1")
    {
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetSystemTimeAdjustment",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(3, Write, NoEscape, 1)]
            internal extern int32 GetSystemTimeAdjustment(
                write ptr<uint32> adjustment,
                write ptr<uint32> increment,
                write ptr<int32> disabled);

            int32 Main() {
                mut uint32 adjustment = 0;
                mut uint32 increment = 0;
                mut int32 disabled = -1;
                unsafe {
                    var callback = &GetSystemTimeAdjustment;
                    callback(&adjustment, &increment, &disabled);
                }
                return disabled;
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return TrRmExecutableCompiler.EmitStandard(compilation.Program!,
            moduleIdentity, schemaIdentity, 1);
    }

    private static byte[] AddA2FixedArrayTypes(byte[] artifact,
        (uint ElementTypeSpecRef, ulong ElementCount, ulong ElementStride,
            ulong ByteExtent, ulong Alignment)[] arrays,
        bool corruptFirstExtent = false)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                index);
        if (tables.Modules.Count != 1
            || tables.FixedArrayLayouts.Count != 0
            || tables.Features.TryGetFeature(
                TrRmFeatureTableReader.FixedInlineArrayStorageFeatureId,
                out _))
        {
            throw new InvalidOperationException(
                "The array augmentation requires an unmodified single-module A2 artifact.");
        }

        var (featureFirst, existingFeatureCount)
            = tables.Modules.GetFeatureRange(1);
        if (featureFirst != 1 || existingFeatureCount != tables.Features.Count)
        {
            throw new InvalidOperationException(
                "The source artifact does not own its complete Feature range.");
        }

        var featureRows = new List<TrRmFeatureRow>(
            tables.Features.Count + 1);
        for (var row = 1; row <= tables.Features.Count; row++)
        {
            featureRows.Add(tables.Features.GetFeature(row));
        }
        featureRows.Add(TrRmFeatureRow.FixedInlineArrayStorageCandidate);
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureRows.Count, schema);

        if (!index.TryGetSection(0x0001, out var oldModuleBytes,
                out var moduleCount)
            || moduleCount != 1)
        {
            throw new InvalidOperationException(
                "The source artifact has no single Module row.");
        }
        var moduleBytes = oldModuleBytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(moduleBytes.AsSpan(28),
            checked((uint)featureRows.Count));
        var modules = new TrRmModuleTableReader(moduleBytes, moduleCount,
            tables.Strings, tables.Modules.ModuleRefCount,
            tables.TypeDefs.Count, tables.Methods.Count,
            tables.Modules.SourceFileCount, featureRows.Count);
        if (tables.TargetProfiles.Count != 0)
        {
            throw new InvalidOperationException(
                "The source artifact already contains target profiles.");
        }
        var targetProfileBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader
                    .WindowsX64NativeProfileRuleRevision),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, 1, schema);

        var typeSpecRows = new List<ReadOnlyMemory<byte>>(
            tables.TypeSpecs.Count + arrays.Length);
        for (var row = 1; row <= tables.TypeSpecs.Count; row++)
        {
            typeSpecRows.Add(tables.TypeSpecs.GetRow(row).ToArray());
        }

        foreach (var (ElementTypeSpecRef, ElementCount, ElementStride, ByteExtent, Alignment) in arrays)
        {
            typeSpecRows.Add(TrRmTypeSpecTableWriter.FixedInlineArray(
                ElementTypeSpecRef, ElementCount));
        }
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            tables.TypeDefs.Count, tables.Imports.Count,
            tables.GenericParams.Count,
            tables.NativeFunctionPointerSignatures.Count, schema,
            tables.Methods.Count);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, tables.TypeDefs.Count, tables.Imports.Count,
            tables.GenericParams.Count,
            tables.NativeFunctionPointerSignatures.Count, schema,
            tables.Methods.Count);
        if (!targetProfiles.TryGetProfile(
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader
                    .WindowsX64NativeProfileRuleRevision,
                out var profileRow))
        {
            throw new InvalidOperationException(
                "The source artifact has no Windows x64 target profile.");
        }

        var layoutRows = new TrRmFixedArrayLayoutRow[arrays.Length];
        for (var indexInArray = 0; indexInArray < arrays.Length; indexInArray++)
        {
            var (ElementTypeSpecRef, ElementCount, ElementStride, ByteExtent, Alignment) = arrays[indexInArray];
            layoutRows[indexInArray] = new TrRmFixedArrayLayoutRow(1,
                tables.TypeSpecs.Count + indexInArray + 1,
                (uint)profileRow, ElementStride, ByteExtent,
                Alignment);
        }
        var fixedArrayLayoutBytes = TrRmFixedArrayLayoutTableWriter.Encode(
            layoutRows, typeSpecs, targetProfiles, features, modules,
            schema);
        if (corruptFirstExtent)
        {
            var extent = BinaryPrimitives.ReadUInt64LittleEndian(
                fixedArrayLayoutBytes.AsSpan(24));
            BinaryPrimitives.WriteUInt64LittleEndian(
                fixedArrayLayoutBytes.AsSpan(24), checked(extent + 1));
        }

        var sections = new List<TrRmSection>(index.SectionCount + 1);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            switch (kind)
            {
                case 0x0001:
                    sections.Add(new TrRmSection(kind, flags,
                        checked((uint)moduleCount), moduleBytes));
                    break;
                case 0x0004:
                    sections.Add(new TrRmSection(kind, flags,
                        checked((uint)featureRows.Count), featureBytes));
                    break;
                case 0x0011:
                    sections.Add(new TrRmSection(kind, flags,
                        checked((uint)typeSpecRows.Count), typeSpecBytes));
                    break;
                case 0x0012:
                    throw new InvalidOperationException(
                        "The source artifact already contains FixedArrayLayout rows.");
                case 0x0048:
                    throw new InvalidOperationException(
                        "The source artifact already contains target profiles.");
                default:
                    sections.Add(new TrRmSection(kind, flags,
                        checked((uint)recordCount), section.ToArray()));
                    break;
            }
        }

        sections.Add(new TrRmSection(0x0048, 0, 1, targetProfileBytes));
        sections.Add(new TrRmSection(0x0012, 0,
            checked((uint)layoutRows.Length), fixedArrayLayoutBytes));
        sections.Sort(static (left, right)
            => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] RewriteA2EEmptyInitializationArtifact(
        byte[] artifact, string defect)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft(
                artifact);
        _ = index.TryGetSection(0x001B, out _, out var contractCount);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            if (kind != 0x0020)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)recordCount), section.ToArray()));
                continue;
            }

            var bodyTable = new TrRmBodyTableReader(section, recordCount,
                schema);
            var body = new TrRmRegisterBodyReader(bodyTable.GetRow(1),
                contractCount, schema);
            var bodyBytes = bodyTable.GetRow(1).ToArray();
            var isHomeRoleDefect = defect == "wrong-home-role";
            var modified = isHomeRoleDefect;
            if (isHomeRoleDefect)
            {
                ReadOnlySpan<byte> homeSelection = default;
                for (var recordIndex = 0;
                    recordIndex < body.RecordCount;
                    recordIndex++)
                {
                    var record = body.GetRecord(recordIndex);
                    if (record[0] == 0x04 && record[4] == 1)
                    {
                        homeSelection = record;
                        break;
                    }
                }

                if (homeSelection.IsEmpty)
                {
                    throw new InvalidOperationException(
                        "The A2E Body contains no Home PlaceSelection.");
                }

                var homeIndex = BinaryPrimitives.ReadUInt32LittleEndian(
                    homeSelection[16..]);
                if (homeIndex >= (uint)body.HomeCount)
                {
                    throw new InvalidOperationException(
                        "The A2E Home PlaceSelection has an invalid Home.");
                }

                var homeMap = body.HomeMap.ToArray();
                var homeMapOffset = bodyBytes.AsSpan().IndexOf(homeMap);
                if (homeMapOffset < 0)
                {
                    throw new InvalidOperationException(
                        "The A2E Body has no serialized HomeMap.");
                }

                bodyBytes[homeMapOffset + checked((int)homeIndex * 8)] = 2;
            }
            for (var recordIndex = 0; recordIndex < body.RecordCount;
                recordIndex++)
            {
                var record = body.GetRecord(recordIndex);
                var isHomePlaceDefect = defect is "wrong-home-access"
                    or "wrong-home-type" or "out-of-range-home";
                var isHomeAccessDefect = defect == "wrong-home-access"
                    && record[0] == 0x04 && record[4] == 1;
                var isNullaryStoreDefect = !isHomePlaceDefect
                    && !isHomeRoleDefect
                    && record[0] == 0x05 && record[5] == 0;
                if (!modified
                    && ((isHomePlaceDefect && record[0] == 0x04
                        && record[4] == 1)
                        || isNullaryStoreDefect))
                {
                    var recordOffset = bodyBytes.AsSpan().IndexOf(record);
                    if (recordOffset < 0)
                    {
                        throw new InvalidOperationException(
                            "The A2E nullary store has no serialized record bytes.");
                    }

                    if (isHomeAccessDefect)
                    {
                        bodyBytes[recordOffset + 5] = 2;
                    }
                    else if (defect == "wrong-home-type")
                    {
                        if (body.TypeCount < 2)
                        {
                            throw new InvalidOperationException(
                                "The A2E fixture needs two TypeMap entries for the wrong-type case.");
                        }

                        var currentTypeMapIndex
                            = BinaryPrimitives.ReadUInt16LittleEndian(
                                record[20..]);
                        var wrongTypeMapIndex = checked((ushort)(
                            ((int)currentTypeMapIndex + 1)
                            % body.TypeCount));
                        BinaryPrimitives.WriteUInt16LittleEndian(
                            bodyBytes.AsSpan(recordOffset + 20),
                            wrongTypeMapIndex);
                    }
                    else if (defect == "out-of-range-home")
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(
                            bodyBytes.AsSpan(recordOffset + 16),
                            checked((uint)body.HomeCount));
                    }
                    else
                    {
                        switch (defect)
                        {
                            case "nonzero-source-kind":
                                bodyBytes[recordOffset + 5] = 1;
                                break;
                            case "nonzero-source-index":
                                bodyBytes[recordOffset + 10] = 1;
                                break;
                            case "nonzero-source-use":
                                bodyBytes[recordOffset + 7] = 1;
                                break;
                            case "wrong-transition":
                                bodyBytes[recordOffset + 6] = 2;
                                break;
                            case "nonzero-dependency":
                                bodyBytes[recordOffset + 12] = 1;
                                break;
                            case "formal-present":
                                bodyBytes[recordOffset + 16] = 1;
                                break;
                            case "nonzero-flags":
                                bodyBytes[recordOffset + 17] = 1;
                                break;
                            case "nonzero-formal-ordinal":
                                bodyBytes[recordOffset + 18] = 1;
                                break;
                            case "wrong-place-kind":
                                bodyBytes[recordOffset + 4] = 2;
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(
                                    nameof(defect));
                        }
                    }

                    modified = true;
                }
            }

            if (!modified)
            {
                throw new InvalidOperationException(
                    "The A2E Body contains no nullary SemanticStore.");
            }

            sections.Add(new TrRmSection(kind, flags,
                checked((uint)recordCount),
                TrRmVariableTableWriter.EncodeBodies([bodyBytes])));
        }

        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] DuplicateA2EEmptyHomeInitialization(
        byte[] artifact)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft(
                artifact);
        _ = index.TryGetSection(0x001B, out _, out var contractCount);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            if (kind != 0x0020)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)recordCount), section.ToArray()));
                continue;
            }

            var bodyTable = new TrRmBodyTableReader(section, recordCount,
                schema);
            var body = new TrRmRegisterBodyReader(bodyTable.GetRow(1),
                contractCount, schema);
            var homeSelection = default(ReadOnlyMemory<byte>);
            var nullaryStore = default(ReadOnlyMemory<byte>);
            var records = new List<ReadOnlyMemory<byte>>(body.RecordCount + 2);
            for (var recordIndex = 0; recordIndex < body.RecordCount;
                recordIndex++)
            {
                var record = body.GetRecord(recordIndex);
                records.Add(record.ToArray());
                if (homeSelection.IsEmpty && record[0] == 0x04
                    && record[4] == 1)
                {
                    homeSelection = record.ToArray();
                }
                else if (nullaryStore.IsEmpty && record[0] == 0x05
                    && record[5] == 0)
                {
                    nullaryStore = record.ToArray();
                }
            }

            if (body.BlockCount != 1 || homeSelection.IsEmpty
                || nullaryStore.IsEmpty)
            {
                throw new InvalidOperationException(
                    "The A2E Body cannot be extended with a second Home initialization.");
            }

            var selectionIndex = checked((ushort)records.Count);
            records.Add(homeSelection);
            var storeIndex = checked((ushort)records.Count);
            records.Add(nullaryStore);
            var originalCode = body.Code.ToArray();
            var insertionOffset = -1;
            var position = 0;
            while (position < originalCode.Length)
            {
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        originalCode[position], schema);
                if (position + instructionLength == originalCode.Length)
                {
                    insertionOffset = position;
                    break;
                }

                position += instructionLength;
            }

            if (insertionOffset < 0)
            {
                throw new InvalidOperationException(
                    "The A2E Body has no final instruction for insertion.");
            }

            var code = new byte[originalCode.Length + 8];
            originalCode.AsSpan(0, insertionOffset).CopyTo(code);
            code[insertionOffset] = 0xD8;
            code[insertionOffset + 1] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                code.AsSpan(insertionOffset + 2), selectionIndex);
            code[insertionOffset + 4] = 0xD9;
            code[insertionOffset + 5] = 0;
            BinaryPrimitives.WriteUInt16LittleEndian(
                code.AsSpan(insertionOffset + 6), storeIndex);
            originalCode.AsSpan(insertionOffset).CopyTo(
                code.AsSpan(insertionOffset + 8));

            var originalBlock = body.GetBlock(0);
            TrRmRegisterBodyBlock[] blocks = [
                new TrRmRegisterBodyBlock(0, checked((uint)code.Length),
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        originalBlock[8..])),
            ];
            var rewrittenBody = TrRmRegisterBodyWriter.Encode(
                body.HomeMap.ToArray(), body.TypeMap.ToArray(),
                body.MethodMap.ToArray(), body.FieldMap.ToArray(),
                body.ConstantMap.ToArray(), blocks, records, code,
                contractCount, schema);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)recordCount),
                TrRmVariableTableWriter.EncodeBodies([rewrittenBody])));
        }

        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] RewriteA2EUUnitStoreArtifact(byte[] artifact,
        string defect)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                artifact);
        _ = index.TryGetSection(0x001B, out _, out var contractCount);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            if (kind != 0x0020)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)recordCount), section.ToArray()));
                continue;
            }

            var bodyTable = new TrRmBodyTableReader(section, recordCount,
                schema);
            var body = new TrRmRegisterBodyReader(bodyTable.GetRow(1),
                contractCount, schema);
            var bodyBytes = bodyTable.GetRow(1).ToArray();
            var modified = false;
            for (var recordIndex = 0; recordIndex < body.RecordCount;
                recordIndex++)
            {
                var record = body.GetRecord(recordIndex);
                if (modified || record[0] != 0x05 || record[5] != 1
                    || BinaryPrimitives.ReadUInt16LittleEndian(
                        record[10..]) != 14)
                {
                    continue;
                }

                var recordOffset = bodyBytes.AsSpan().IndexOf(record);
                if (recordOffset < 0)
                {
                    throw new InvalidOperationException(
                        "The A2EU unit store has no serialized record bytes.");
                }

                switch (defect)
                {
                    case "unit-source-r15":
                        bodyBytes[recordOffset + 10] = 15;
                        break;
                    case "unit-store-source-use":
                        bodyBytes[recordOffset + 7] = 2;
                        break;
                    case "unit-store-transition":
                        bodyBytes[recordOffset + 6] = 2;
                        break;
                    case "unit-store-place-kind":
                        bodyBytes[recordOffset + 4] = 2;
                        break;
                    case "unit-store-dependency":
                        bodyBytes[recordOffset + 12] = 1;
                        break;
                    case "unit-store-formal":
                        bodyBytes[recordOffset + 16] = 1;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(defect));
                }

                modified = true;
            }

            if (!modified)
            {
                throw new InvalidOperationException(
                    "The A2EU Body contains no typed Unit SemanticStore.");
            }

            sections.Add(new TrRmSection(kind, flags,
                checked((uint)recordCount),
                TrRmVariableTableWriter.EncodeBodies([bodyBytes])));
        }

        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] RewriteA2EUCheckedArithmeticArtifact(
        byte[] artifact)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                artifact);
        _ = index.TryGetSection(0x001B, out _, out var contractCount);
        var sections = new List<TrRmSection>(index.SectionCount);
        var modified = false;
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            if (kind != 0x0020)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)recordCount), section.ToArray()));
                continue;
            }

            var bodyTable = new TrRmBodyTableReader(section, recordCount,
                schema);
            var body = new TrRmRegisterBodyReader(bodyTable.GetRow(1),
                contractCount, schema);
            var bodyBytes = bodyTable.GetRow(1).ToArray();
            var code = body.GetBlockCode(0);
            var codeOffset = bodyBytes.AsSpan().IndexOf(code);
            if (codeOffset < 0)
            {
                throw new InvalidOperationException(
                    "The A2EU checked arithmetic has no serialized code bytes.");
            }

            for (var position = 0; position < code.Length;)
            {
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        code[position], schema);
                if (code[position] is 0x24 or 0x25)
                {
                    bodyBytes[codeOffset + position + 1] = 0x0E;
                    modified = true;
                    break;
                }

                position += instructionLength;
            }

            if (!modified)
            {
                throw new InvalidOperationException(
                    "The A2EU Body contains no checked division or remainder.");
            }

            sections.Add(new TrRmSection(kind, flags,
                checked((uint)recordCount),
                TrRmVariableTableWriter.EncodeBodies([bodyBytes])));
        }

        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] RewriteA2EUConditionalRegisterArtifact(
        byte[] artifact, int blockIndex, byte conditionRegister)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                artifact);
        _ = index.TryGetSection(0x001B, out _, out var contractCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            _ = flags;
            if (kind != 0x0020)
            {
                continue;
            }

            var bodyTable = new TrRmBodyTableReader(section, recordCount,
                schema);
            var bodyBytes = bodyTable.GetRow(1);
            var body = new TrRmRegisterBodyReader(bodyBytes, contractCount,
                schema);
            var code = body.GetBlockCode(blockIndex);
            var bodyOffset = section.IndexOf(bodyBytes);
            var codeOffset = bodyBytes.IndexOf(code);
            var sectionOffset = artifact.AsSpan().IndexOf(section);
            if (bodyOffset < 0 || codeOffset < 0 || sectionOffset < 0)
            {
                throw new InvalidOperationException(
                    "The A2EU conditional Body has no serialized code range.");
            }

            for (var position = 0; position < code.Length;)
            {
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        code[position], schema);
                if (code[position] == 0xE4)
                {
                    var malformed = (byte[])artifact.Clone();
                    var controlOffset = sectionOffset + bodyOffset
                        + codeOffset + position + 1;
                    malformed[controlOffset] = (byte)(
                        (malformed[controlOffset] & 0xF0)
                        | (conditionRegister & 0x0F));
                    return malformed;
                }

                position += instructionLength;
            }
        }

        throw new InvalidOperationException(
            "The A2EU Body contains no conditional branch.");
    }

    private static byte[] RewriteA2EUConditionalTargetArtifact(
        byte[] artifact, int edgeRecordIndex, ushort targetBlock)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                artifact);
        _ = index.TryGetSection(0x001B, out _, out var contractCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            _ = flags;
            if (kind != 0x0020)
            {
                continue;
            }

            var bodyTable = new TrRmBodyTableReader(section, recordCount,
                schema);
            var bodyBytes = bodyTable.GetRow(1);
            var body = new TrRmRegisterBodyReader(bodyBytes, contractCount,
                schema);
            var edge = body.GetRecord(edgeRecordIndex);
            if (edge[0] != 0x06)
            {
                throw new InvalidOperationException(
                    "The A2EU conditional record is not a ConditionalEdge.");
            }

            var sectionOffset = artifact.AsSpan().IndexOf(section);
            var bodyOffset = section.IndexOf(bodyBytes);
            var edgeOffset = bodyBytes.IndexOf(edge);
            if (sectionOffset < 0 || bodyOffset < 0 || edgeOffset < 0)
            {
                throw new InvalidOperationException(
                    "The A2EU ConditionalEdge has no serialized offset.");
            }

            var malformed = (byte[])artifact.Clone();
            BinaryPrimitives.WriteUInt16LittleEndian(
                malformed.AsSpan(sectionOffset + bodyOffset + edgeOffset + 4),
                targetBlock);
            return malformed;
        }

        throw new InvalidOperationException(
            "The A2EU artifact has no Body table.");
    }

    private static byte[] RewriteA2EUComparisonDestinationArtifact(
        byte[] artifact, byte destinationRegister)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                artifact);
        _ = index.TryGetSection(0x001B, out _, out var contractCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            _ = flags;
            if (kind != 0x0020)
            {
                continue;
            }

            var bodyTable = new TrRmBodyTableReader(section, recordCount,
                schema);
            var bodyBytes = bodyTable.GetRow(1);
            var body = new TrRmRegisterBodyReader(bodyBytes, contractCount,
                schema);
            var sectionOffset = artifact.AsSpan().IndexOf(section);
            var bodyOffset = section.IndexOf(bodyBytes);
            if (sectionOffset < 0 || bodyOffset < 0)
            {
                throw new InvalidOperationException(
                    "The A2EU comparison Body has no serialized range.");
            }

            for (var blockIndex = 0; blockIndex < body.BlockCount;
                blockIndex++)
            {
                var code = body.GetBlockCode(blockIndex);
                var codeOffset = bodyBytes.IndexOf(code);
                if (codeOffset < 0)
                {
                    throw new InvalidOperationException(
                        "The A2EU comparison Body has no serialized code range.");
                }

                for (var position = 0; position < code.Length;)
                {
                    var instructionLength
                        = TrRmRegisterBodyReader.InstructionLength(
                            code[position], schema);
                    if (code[position] == 0x41)
                    {
                        var malformed = (byte[])artifact.Clone();
                        var operandOffset = sectionOffset + bodyOffset
                            + codeOffset + position + 1;
                        malformed[operandOffset] = (byte)(
                            (malformed[operandOffset] & 0xF0)
                            | (destinationRegister & 0x0F));
                        return malformed;
                    }

                    position += instructionLength;
                }
            }
        }

        throw new InvalidOperationException(
            "The A2EU Body contains no comparison predicate.");
    }

    internal static byte[] RewriteStorageAddressArtifact(byte[] artifact,
        string defect)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageAddressDraft;
        var index = TrRmArtifactIndex.ForForeignInvocationStorageAddressDraft(
            artifact);
        _ = index.TryGetSection(0x001B, out _, out var contractCount);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            if (kind != 0x0020)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)recordCount), section.ToArray()));
                continue;
            }

            var bodyTable = new TrRmBodyTableReader(section, recordCount,
                schema);
            var body = new TrRmRegisterBodyReader(bodyTable.GetRow(1),
                contractCount, schema);
            var homeMap = body.HomeMap.ToArray();
            if (defect == "missing-write-authority")
            {
                homeMap[1] = 0;
            }

            var records = new List<byte[]>(body.RecordCount);
            for (var recordIndex = 0; recordIndex < body.RecordCount;
                recordIndex++)
            {
                records.Add(body.GetRecord(recordIndex).ToArray());
            }
            if (defect == "oversized-scalar-extent")
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    records[2].AsSpan(12), 2);
            }

            var blocks = new TrRmRegisterBodyBlock[body.BlockCount];
            for (var blockIndex = 0; blockIndex < body.BlockCount;
                blockIndex++)
            {
                var block = body.GetBlock(blockIndex);
                blocks[blockIndex] = new TrRmRegisterBodyBlock(
                    BinaryPrimitives.ReadUInt32LittleEndian(block),
                    BinaryPrimitives.ReadUInt32LittleEndian(block[4..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(block[8..]));
            }

            var rewrittenBody = TrRmRegisterBodyWriter.Encode(homeMap,
                body.TypeMap.ToArray(), body.MethodMap.ToArray(),
                body.FieldMap.ToArray(), body.ConstantMap.ToArray(), blocks,
                records.Select(static record => (ReadOnlyMemory<byte>)record)
                    .ToArray(),
                body.Code.ToArray(), contractCount, schema);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)recordCount),
                TrRmVariableTableWriter.EncodeBodies([rewrittenBody])));
        }

        return TrRmArtifactWriter.Encode(sections, schema);
    }
}
