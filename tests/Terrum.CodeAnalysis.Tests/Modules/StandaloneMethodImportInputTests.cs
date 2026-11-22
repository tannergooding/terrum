using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Modules;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class StandaloneMethodImportInputTests
{
    private const string Agreement
        = "terrum.experimental.standalone-method-imports/1";
    private const string SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-1";
    private const string VerificationSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            int32 inputStatus = ValidateStandaloneMethodImportInput();
            if (inputStatus != 0) { return inputStatus; }
            if (Host.Expected[0] == -2) {
                return TryValidateStandaloneArtifactDirectories() ? 0 : 10;
            }
            if (Host.Expected[0] == -3) {
                return TryValidateStandaloneArtifactDirectories() ? 10 : 0;
            }
            bool built = TryBuildStandaloneModuleDirectory();
            if (Host.Expected[0] < 0) {
                if (Host.Expected[0] == -1) { return built ? 20 : 0; }
                return built || Host.StandaloneModuleDirectoryStatus[0]
                    != -Host.Expected[0] ? 21 : 0;
            }
            if (!built || Host.StandaloneModuleCount[0] != Host.Expected[0]) {
                return 21;
            }
            if (Host.Expected[0] == 1) {
                if (Host.StandaloneModuleDirectory[8] != -1) { return 22; }
                return 0;
            }
            if (Host.StandaloneModuleCount[0] != 3
                || Host.StandaloneModuleDirectory[8] != -1
                || Host.StandaloneModuleDirectory[16] != 0
                || Host.StandaloneModuleDirectory[17] != 1
                || Host.StandaloneModuleDirectory[24] != 1
                || Host.StandaloneModuleDirectory[25] != 1) {
                return 22;
            }
            if (Host.StandaloneModuleOrder[1] != 1
                || Host.StandaloneModuleOrder[2] != 3
                || Host.StandaloneModuleOrder[3] != 2) {
                return 22;
            }
            int32 rootStatus = BindStandaloneMethodRoots();
            if (rootStatus != Host.Expected[1]) { return 23; }
            if (rootStatus != 0
                && Host.StandaloneResolvedRootCount[0] != 0) {
                return 25;
            }
            if (rootStatus == 0
                && (Host.StandaloneResolvedRootCount[0] != 2
                    || Host.StandaloneResolvedRoots[0] != 1
                    || Host.StandaloneResolvedRoots[1] != 0
                    || Host.StandaloneResolvedRoots[2] != 0
                    || Host.StandaloneResolvedRoots[3] != 1
                    || Host.StandaloneResolvedRoots[4] != 1
                    || Host.StandaloneResolvedRoots[5] != 1
                    || Host.StandaloneResolvedRoots[6] != 2
                    || Host.StandaloneResolvedRoots[7] != 1
                    || Host.StandaloneResolvedRoots[9] != 0
                    || Host.StandaloneResolvedRoots[10] != 0
                    || Host.StandaloneResolvedRoots[11] != 0
                    || Host.StandaloneResolvedRoots[12] != 0
                    || Host.StandaloneResolvedRoots[13] != 0
                    || Host.StandaloneResolvedRoots[14] != 0
                    || Host.StandaloneResolvedRoots[15] != 0
                    || Host.StandaloneResolvedRoots[16] != 0
                    || Host.StandaloneResolvedRoots[17] != 2
                    || Host.StandaloneResolvedRoots[18] != 3
                    || Host.StandaloneResolvedRoots[19] != 0
                    || Host.StandaloneResolvedRoots[20] != 2
                    || Host.StandaloneResolvedRoots[21] != 0
                    || Host.StandaloneResolvedRoots[22] != 1
                    || Host.StandaloneResolvedRoots[23] != 1
                    || Host.StandaloneResolvedRoots[24] != 1
                    || Host.StandaloneResolvedRoots[25] != 1
                    || Host.StandaloneResolvedRoots[26] != 2
                    || Host.StandaloneResolvedRoots[27] != 1
                    || Host.StandaloneResolvedRoots[29] != 0
                    || Host.StandaloneResolvedRoots[30] != 0
                    || Host.StandaloneResolvedRoots[31] != 0
                    || Host.StandaloneResolvedRoots[32] != 0
                    || Host.StandaloneResolvedRoots[33] != 0
                    || Host.StandaloneResolvedRoots[34] != 0
                    || Host.StandaloneResolvedRoots[35] != 0
                    || Host.StandaloneResolvedRoots[36] != 0
                    || Host.StandaloneResolvedRoots[37] != 2
                    || Host.StandaloneResolvedRoots[38] != 3
                    || Host.StandaloneResolvedRoots[39] != 1)) {
                return 24;
            }
            if (!TryValidateStandaloneArtifactDirectory(0)) { return 11; }
            init int32 found;
            init int32 sectionStart;
            init int32 sectionLength;
            init int32 recordCount;
            init int32 sectionFlags;
            if (!TryGetStandaloneArtifactSection(0, 0x0001,
                    ref init found, ref init sectionStart,
                    ref init sectionLength, ref init recordCount,
                    ref init sectionFlags)
                || found != 1 || sectionStart != 64
                || sectionLength != 48 || recordCount != 1
                || sectionFlags != 0) {
                return 12;
            }
            return 0;
        }
        """;

    [Test]
    public void CaptureClonesRawArtifactsAndPreservesOrderedMethodRoots()
    {
        var firstBytes = new byte[] { 1, 2, 3 };
        var secondBytes = new byte[] { 4, 5 };
        var artifacts = new List<TrRmStandardArtifactInput> {
            new("schema/first", firstBytes),
            new("schema/second", secondBytes),
        };
        var roots = new List<StandaloneMethodRoot> {
            new(1, "provider/second", "provider/second/Actual"),
            new(0, "provider/first", "provider/first/Alias"),
        };
        var input = new StandaloneMethodImportInput("agreement/1",
            "consumer/module", artifacts, roots);

        var captured = input.Capture();
        firstBytes[0] = 9;
        secondBytes[0] = 8;
        artifacts[0] = new TrRmStandardArtifactInput("replacement",
            new byte[] { 7 });
        roots[0] = new StandaloneMethodRoot(0, "replacement",
            "replacement/Method");

        Assert.That(captured.Agreement, Is.EqualTo("agreement/1"));
        Assert.That(captured.UsingModuleIdentity,
            Is.EqualTo("consumer/module"));
        Assert.That(captured.Artifacts[0].SchemaIdentity,
            Is.EqualTo("schema/first"));
        Assert.That(captured.Artifacts[0].Artifact.ToArray(),
            Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(captured.Artifacts[1].SchemaIdentity,
            Is.EqualTo("schema/second"));
        Assert.That(captured.Artifacts[1].Artifact.ToArray(),
            Is.EqualTo(new byte[] { 4, 5 }));
        Assert.That(captured.MethodRoots, Is.EqualTo(new[] {
            new StandaloneMethodRoot(1, "provider/second",
                "provider/second/Actual"),
            new StandaloneMethodRoot(0, "provider/first",
                "provider/first/Alias"),
        }));
        var hostBuffers = captured.CreateHostBuffers();
        Assert.That(hostBuffers.Select(static buffer => buffer.Name),
            Is.EqualTo(new[] {
                "StandaloneRequest",
                "StandaloneText",
                "StandaloneArtifacts",
                "StandaloneArtifactInputs",
                "StandaloneMethodRoots",
                "StandaloneSectionCount",
                "StandaloneSectionArtifactOrdinal",
                "StandaloneSectionKinds",
                "StandaloneSectionFlags",
                "StandaloneSectionCounts",
                "StandaloneSectionStarts",
                "StandaloneSectionLengths",
                "StandaloneSectionOrder",
                "StandaloneModuleCount",
                "StandaloneModuleDirectoryStatus",
                "StandaloneModuleDirectory",
                "StandaloneModuleOrder",
                "StandaloneResolvedRootCount",
                "StandaloneResolvedRoots",
            }));
        Assert.That(hostBuffers[2].Values,
            Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
        Assert.That(hostBuffers[3].Values,
            Is.EqualTo(new[] { 26, 12, 0, 3, 38, 13, 3, 2 }));
        Assert.That(hostBuffers[4].Values[0], Is.EqualTo(1));
        Assert.That(hostBuffers[4].Values[5], Is.EqualTo(0));
    }

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task TerrumImportPreflightBindsExplicitMethodRootsAsync(
        BoundRuntimePolicy policy)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var input = new StandaloneMethodImportInput(Agreement,
            "consumer/module", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMinimalArtifact("provider/b")),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMinimalArtifact("provider/a")),
            ], [
                new StandaloneMethodRoot(0, "provider/b",
                    "api/Run"),
                new StandaloneMethodRoot(1, "provider/a",
                    "api/Run"),
            ]);
        var sourcePaths = new[] {
            "sources/Terrum.CodeAnalysis.Terrum/Text/Utf16CodeUnits.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodImportInput.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneArtifactDirectory.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneStringTable.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneModuleDirectory.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodRoots.terrum",
        };
        var root = RepositoryRoot();
        var components = new List<SyntaxTree>(sourcePaths.Length + 1);
        foreach (var sourcePath in sourcePaths)
        {
            var path = TerrumProjectPaths.RepositoryFile(root, sourcePath);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path)
                .ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False,
                string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(VerificationSource,
            "standalone-method-import-input-tests.terrum"));

        var program = Compile(input, 3, 0);
        var boundRootsExecution = NativeJit.ExecuteWithData(program,
            (NativeRuntimePolicy)policy);
        Assert.That(boundRootsExecution.ExitCode, Is.EqualTo(0));
        var resolvedRootsBuffer = Enumerable.Range(0,
                program.Buffers.Length)
            .Single(index =>
                program.Buffers[index].Name == "StandaloneResolvedRoots");
        var resolvedRootsOffset
            = boundRootsExecution.BufferOffsets[resolvedRootsBuffer];
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                boundRootsExecution.Data.AsSpan(
                    resolvedRootsOffset + (8 * sizeof(int)))),
            Is.EqualTo(0xF200_0008u));

        var unsupportedAgreement = input with {
            Agreement = Agreement[..^1] + "2",
        };
        Assert.That(Run(program, unsupportedAgreement, 3, 0),
            Is.EqualTo(2));

        var unsupportedSchema = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(
                    SchemaIdentity[..^1] + "2",
                    input.Artifacts[0].Artifact),
                input.Artifacts[1],
            ],
        };
        Assert.That(Run(program, unsupportedSchema, 3, 0), Is.EqualTo(3));

        var malformedArtifact = input.Artifacts[0].Artifact.ToArray();
        malformedArtifact[0] = (byte)'X';
        var malformedArtifactInput = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedArtifact),
                input.Artifacts[1],
            ],
        };
        Assert.That(Run(program, malformedArtifactInput, -3, 0),
            Is.EqualTo(0));

        var duplicateRoot = input with {
            MethodRoots = [
                input.MethodRoots[0],
                input.MethodRoots[0],
            ],
        };
        Assert.That(Run(program, duplicateRoot, 3, 0), Is.EqualTo(4));

        var invalidRootOrdinal = input with {
            MethodRoots = [
                input.MethodRoots[0],
                new StandaloneMethodRoot(2, "provider/a",
                    "provider/a/Run"),
            ],
        };
        Assert.That(Run(program, invalidRootOrdinal, 3, 0), Is.EqualTo(1));

        var missingExport = input with {
            MethodRoots = [
                new StandaloneMethodRoot(0, "provider/b", "api/Missing"),
                input.MethodRoots[1],
            ],
        };
        var missingExportProgram = Compile(missingExport, 3, 1);
        Assert.That(Run(missingExportProgram, missingExport, 3, 1),
            Is.EqualTo(0));

        var privateMethod = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMinimalArtifact("provider/b", methodFlags: 1)),
                input.Artifacts[1],
            ],
        };
        var privateMethodProgram = Compile(privateMethod, 3, 2);
        Assert.That(Run(privateMethodProgram, privateMethod, 3, 2),
            Is.EqualTo(0));

        var duplicateExportKey = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMinimalArtifact("provider/b",
                        duplicateMethodExport: true)),
                input.Artifacts[1],
            ],
        };
        var duplicateExportProgram = Compile(duplicateExportKey, 3, 3);
        Assert.That(Run(duplicateExportProgram, duplicateExportKey, 3, 3),
            Is.EqualTo(0));

        var wrongDefinitionKind = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateTypeAndMethodArtifact("provider/b")),
                input.Artifacts[1],
            ],
        };
        var wrongDefinitionKindProgram
            = Compile(wrongDefinitionKind, 3, 2);
        Assert.That(Run(wrongDefinitionKindProgram, wrongDefinitionKind,
                3, 2),
            Is.EqualTo(0));

        var supportedFeature = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMinimalArtifact("provider/b", featureId: 10,
                        featureRevision: 1)),
                input.Artifacts[1],
            ],
        };
        var supportedFeatureProgram = Compile(supportedFeature, 3, 0);
        Assert.That(Run(supportedFeatureProgram, supportedFeature, 3, 0),
            Is.EqualTo(0));

        var repeatedFeature = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMinimalArtifact("provider/b", featureId: 10,
                        featureRevision: 1, featureOccurrences: 2)),
                input.Artifacts[1],
            ],
        };
        var repeatedFeatureProgram = Compile(repeatedFeature, 3, 0);
        Assert.That(Run(repeatedFeatureProgram, repeatedFeature, 3, 0),
            Is.EqualTo(0));

        var unsupportedFeature = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMinimalArtifact("provider/b", featureId: 9,
                        featureRevision: 1)),
                input.Artifacts[1],
            ],
        };
        var unsupportedFeatureProgram = Compile(unsupportedFeature, -5, 0);
        Assert.That(Run(unsupportedFeatureProgram, unsupportedFeature, -5, 0),
            Is.EqualTo(0));

        var unsupportedFeatureRevision = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMinimalArtifact("provider/b", featureId: 10,
                        featureRevision: 2)),
                input.Artifacts[1],
            ],
        };
        var unsupportedFeatureRevisionProgram
            = Compile(unsupportedFeatureRevision, -5, 0);
        Assert.That(Run(unsupportedFeatureRevisionProgram,
                unsupportedFeatureRevision, -5, 0),
            Is.EqualTo(0));

        var unsupportedTarget = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMinimalArtifact("provider/b",
                        targetRequirement: 0)),
                input.Artifacts[1],
            ],
        };
        var unsupportedTargetProgram = Compile(unsupportedTarget, -5, 0);
        Assert.That(Run(unsupportedTargetProgram, unsupportedTarget, -5, 0),
            Is.EqualTo(0));

        var unsupportedRuntimePolicy = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMinimalArtifact("provider/b", runtimePolicy: 1)),
                input.Artifacts[1],
            ],
        };
        var unsupportedRuntimeProgram
            = Compile(unsupportedRuntimePolicy, -5, 0);
        Assert.That(Run(unsupportedRuntimeProgram, unsupportedRuntimePolicy,
                -5, 0),
            Is.EqualTo(0));

        var duplicateModule = input with {
            Artifacts = [
                input.Artifacts[0],
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMinimalArtifact("provider/b")),
            ],
        };
        Assert.That(Run(program, duplicateModule, -1, 0), Is.EqualTo(0));

        var outOfOrderInput = new StandaloneMethodImportInput(Agreement,
            "consumer/module", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateOutOfOrderArtifact()),
            ], []);
        var outOfOrderProgram = Compile(outOfOrderInput, -2, 0);
        Assert.That(Run(outOfOrderProgram, outOfOrderInput, -2, 0),
            Is.EqualTo(0));

        var localOnly = new StandaloneMethodImportInput(Agreement,
            "consumer/module", [], []);
        var localOnlyProgram = Compile(localOnly, 1, 0);
        Assert.That(Run(localOnlyProgram, localOnly, 1, 0),
            Is.EqualTo(0));

        BoundProgram Compile(StandaloneMethodImportInput request,
            int expectedModuleCount, int expectedRootStatus)
        {
            var compilation = Compilation.Create(components,
                Buffers(request, expectedModuleCount,
                    expectedRootStatus), policy);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            return compilation.Program
                ?? throw new AssertionException(
                    "Expected a bound validator.");
        }

        int Run(BoundProgram boundProgram,
            StandaloneMethodImportInput request, int expectedModuleCount,
            int expectedRootStatus)
            => NativeJit.Execute(boundProgram with {
                Buffers = Buffers(request, expectedModuleCount,
                    expectedRootStatus),
            }, (NativeRuntimePolicy)policy);

        ImmutableArray<HostBuffer> Buffers(
            StandaloneMethodImportInput request, int expectedModuleCount,
            int expectedRootStatus)
            => request.Capture().CreateHostBuffers().Add(new HostBuffer(
                "Expected", PrimitiveType.Int32,
                [expectedModuleCount, expectedRootStatus], false));
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..",
            "..", "..", "..", ".."));

    private static byte[] CreateMinimalArtifact(string moduleIdentity,
        string exportKey = "api/Run", uint methodFlags = 2,
        bool duplicateMethodExport = false, uint featureId = 0,
        uint featureRevision = 0, int featureOccurrences = 1,
        uint targetRequirement = 1, uint runtimePolicy = 0,
        uint governingRuleEdition = 1)
    {
        var methodCount = duplicateMethodExport ? 2 : 1;
        var featureCount = featureId == 0 ? 0 : featureOccurrences;
        var stringSection = TrRmStringTableWriter.EncodeNames([
            (moduleIdentity, 0),
            ("Implementation", 0),
            (exportKey, 0),
        ]);
        var strings = new TrRmStringTableReader(stringSection, 3);
        var moduleSection = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, targetRequirement, runtimePolicy, 0,
                1, 1, 1, 1, featureCount == 0 ? 0 : 1,
                featureCount, governingRuleEdition),
        ], strings, 0, 0, methodCount, 0, featureCount);
        var methodDefSection = new byte[methodCount * 28];
        var methodSigSection = new byte[methodCount * 24];
        var exportSection = new byte[methodCount * 8];
        var featureSection = new byte[featureCount * 8];
        for (var row = 1; row <= methodCount; row++)
        {
            var methodOffset = (row - 1) * 28;
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodDefSection.AsSpan(methodOffset + 4), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodDefSection.AsSpan(methodOffset + 8),
                (uint)row);
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodDefSection.AsSpan(methodOffset + 12), methodFlags);
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodSigSection.AsSpan((row - 1) * 24),
                0xF200_0008);
            BinaryPrimitives.WriteUInt32LittleEndian(
                exportSection.AsSpan((row - 1) * 8),
                0x1600_0000u | (uint)row);
            BinaryPrimitives.WriteUInt32LittleEndian(
                exportSection.AsSpan(((row - 1) * 8) + 4), 3);
        }
        for (var row = 0; row < featureCount; row++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                featureSection.AsSpan(row * 8), featureId);
            BinaryPrimitives.WriteUInt32LittleEndian(
                featureSection.AsSpan((row * 8) + 4), featureRevision);
        }

        var sections = new List<TrRmSection> {
            new TrRmSection(0x0001, 0, 1, moduleSection),
            new TrRmSection(0x0002, 0, 3, stringSection),
        };
        if (featureCount != 0)
        {
            sections.Add(new TrRmSection(0x0004, 0,
                (uint)featureCount, featureSection));
        }
        sections.Add(new TrRmSection(0x0016, 0,
            (uint)methodCount, methodDefSection));
        sections.Add(new TrRmSection(0x0017, 0,
            (uint)methodCount, methodSigSection));
        sections.Add(new TrRmSection(0x001D, 0,
            (uint)methodCount, exportSection));
        return TrRmArtifactWriter.Encode(sections);
    }

    private static byte[] CreateTypeAndMethodArtifact(string moduleIdentity)
    {
        var stringSection = TrRmStringTableWriter.EncodeNames([
            (moduleIdentity, 0),
            ("Implementation", 0),
            ("api/Run", 0),
            ("api/Method", 0),
            ("PublicType", 0),
        ]);
        var strings = new TrRmStringTableReader(stringSection, 5);
        var moduleSection = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 1, 1, 0);
        var typeDefSection = new byte[44];
        BinaryPrimitives.WriteUInt32LittleEndian(
            typeDefSection.AsSpan(4), 5);
        BinaryPrimitives.WriteUInt32LittleEndian(
            typeDefSection.AsSpan(8), 6);
        var methodDefSection = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(12), 2);
        var methodSigSection = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodSigSection, 0xF200_0008);
        var exportSection = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(exportSection,
            0x1300_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(
            exportSection.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(
            exportSection.AsSpan(8), 0x1600_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(
            exportSection.AsSpan(12), 4);
        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleSection),
            new TrRmSection(0x0002, 0, 5, stringSection),
            new TrRmSection(0x0013, 0, 1, typeDefSection),
            new TrRmSection(0x0016, 0, 1, methodDefSection),
            new TrRmSection(0x0017, 0, 1, methodSigSection),
            new TrRmSection(0x001D, 0, 2, exportSection),
        ]);
    }

    private static byte[] CreateOutOfOrderArtifact()
    {
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, new byte[48]),
            new TrRmSection(0x8001, 1, 1, new byte[] { 0x11 }),
            new TrRmSection(0x8002, 1, 1, new byte[] { 0x22 }),
        ]);
        var firstStart = BinaryPrimitives.ReadUInt32LittleEndian(
            artifact.AsSpan(56));
        var secondStart = BinaryPrimitives.ReadUInt32LittleEndian(
            artifact.AsSpan(72));
        BinaryPrimitives.WriteUInt32LittleEndian(artifact.AsSpan(56),
            secondStart);
        BinaryPrimitives.WriteUInt32LittleEndian(artifact.AsSpan(72),
            firstStart);
        return artifact;
    }
}
