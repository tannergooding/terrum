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
            if (Host.Expected[0] < 0) { return built ? 20 : 0; }
            if (!built || Host.StandaloneModuleCount[0] != Host.Expected[0]) {
                return 21;
            }
            if (Host.Expected[0] == 1) {
                if (Host.StandaloneModuleDirectory[8] != -1) { return 22; }
                return 0;
            }
            if (Host.StandaloneModuleCount[0] != 3
                || Host.StandaloneModuleDirectory[8] != -1
                || Host.StandaloneModuleDirectory[16] != 1
                || Host.StandaloneModuleDirectory[17] != 1
                || Host.StandaloneModuleDirectory[24] != 0
                || Host.StandaloneModuleDirectory[25] != 1) {
                return 22;
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
                "StandaloneModuleDirectory",
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
    public async Task TerrumImportPreflightValidatesRawArtifactsAndModuleIdentitiesAsync(
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
                    "provider/b/Run"),
                new StandaloneMethodRoot(1, "provider/a",
                    "provider/a/Run"),
            ]);
        var sourcePaths = new[] {
            "sources/Terrum.CodeAnalysis.Terrum/Text/Utf16CodeUnits.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodImportInput.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneArtifactDirectory.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneStringTable.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneModuleDirectory.terrum",
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

        var program = Compile(input, 3);
        Assert.That(Run(program, input, 3), Is.EqualTo(0));

        var unsupportedAgreement = input with {
            Agreement = Agreement[..^1] + "2",
        };
        Assert.That(Run(program, unsupportedAgreement, 3), Is.EqualTo(2));

        var unsupportedSchema = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(
                    SchemaIdentity[..^1] + "2",
                    input.Artifacts[0].Artifact),
                input.Artifacts[1],
            ],
        };
        Assert.That(Run(program, unsupportedSchema, 3), Is.EqualTo(3));

        var malformedArtifact = input.Artifacts[0].Artifact.ToArray();
        malformedArtifact[0] = (byte)'X';
        var malformedArtifactInput = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedArtifact),
                input.Artifacts[1],
            ],
        };
        Assert.That(Run(program, malformedArtifactInput, -3),
            Is.EqualTo(0));

        var duplicateRoot = input with {
            MethodRoots = [
                input.MethodRoots[0],
                input.MethodRoots[0],
            ],
        };
        Assert.That(Run(program, duplicateRoot, 3), Is.EqualTo(4));

        var invalidRootOrdinal = input with {
            MethodRoots = [
                input.MethodRoots[0],
                new StandaloneMethodRoot(2, "provider/a",
                    "provider/a/Run"),
            ],
        };
        Assert.That(Run(program, invalidRootOrdinal, 3), Is.EqualTo(1));

        var duplicateModule = input with {
            Artifacts = [
                input.Artifacts[0],
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMinimalArtifact("provider/b")),
            ],
        };
        Assert.That(Run(program, duplicateModule, -1), Is.EqualTo(0));

        var outOfOrderInput = new StandaloneMethodImportInput(Agreement,
            "consumer/module", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateOutOfOrderArtifact()),
            ], []);
        var outOfOrderProgram = Compile(outOfOrderInput, -2);
        Assert.That(Run(outOfOrderProgram, outOfOrderInput, -2),
            Is.EqualTo(0));

        var localOnly = new StandaloneMethodImportInput(Agreement,
            "consumer/module", [], []);
        var localOnlyProgram = Compile(localOnly, 1);
        Assert.That(Run(localOnlyProgram, localOnly, 1), Is.EqualTo(0));

        BoundProgram Compile(StandaloneMethodImportInput request,
            int expectedModuleCount)
        {
            var compilation = Compilation.Create(components,
                Buffers(request, expectedModuleCount), policy);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            return compilation.Program
                ?? throw new AssertionException(
                    "Expected a bound validator.");
        }

        int Run(BoundProgram boundProgram,
            StandaloneMethodImportInput request, int expectedModuleCount)
            => NativeJit.Execute(boundProgram with {
                Buffers = Buffers(request, expectedModuleCount),
            }, (NativeRuntimePolicy)policy);

        ImmutableArray<HostBuffer> Buffers(
            StandaloneMethodImportInput request, int expectedModuleCount)
            => request.Capture().CreateHostBuffers().Add(new HostBuffer(
                "Expected", PrimitiveType.Int32,
                [expectedModuleCount], false));
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..",
            "..", "..", "..", ".."));

    private static byte[] CreateMinimalArtifact(string moduleIdentity)
    {
        var stringSection = TrRmStringTableWriter.EncodeNames([
            (moduleIdentity, 0),
        ]);
        var strings = new TrRmStringTableReader(stringSection, 1);
        var moduleSection = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 0, 0, 0);
        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleSection),
            new TrRmSection(0x0002, 0, 1, stringSection),
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
