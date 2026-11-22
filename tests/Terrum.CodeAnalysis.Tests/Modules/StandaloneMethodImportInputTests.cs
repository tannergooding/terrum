using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Tests.Modules;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class StandaloneMethodImportInputTests
{
    private const string Agreement
        = "terrum.experimental.standalone-method-imports/1";
    private const string SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-1";
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
            if (Host.Expected[2] < 4) {
                if (Host.Expected[0] == 1) {
                    if (Host.StandaloneModuleDirectory[8] != -1) {
                        return 22;
                    }
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
            }
            int32 rootStatus = BindStandaloneMethodRoots();
            if (rootStatus != Host.Expected[1]) { return 23; }
            if (rootStatus != 0
                && (Host.StandaloneResolvedRootCount[0] != 0
                    || Host.StandaloneTypeFactCount[0] != 0
                    || (Host.StandaloneCallableTypeIds.Length != 0
                        && Host.StandaloneCallableTypeIds[0] != 0)
                    || (Host.StandaloneParameterTypeIds.Length > 1
                        && Host.StandaloneParameterTypeIds[1] != 0))) {
                return 25;
            }
            if (Host.Expected[2] == 4) {
                if (rootStatus != 0
                    || Host.StandaloneResolvedRootCount[0] != 1
                    || Host.StandaloneResolvedImportCount[0] != 2
                    || Host.StandaloneResolvedExpectedImportCount[0] != 2
                    || Host.StandaloneResolvedProviderGroupCount[0] != 1
                    || Host.StandaloneResolvedImportOrder[1] != 1
                    || Host.StandaloneResolvedImportOrder[2] != 2
                    || Host.StandaloneResolvedExpectedImportOrder[1] != 1
                    || Host.StandaloneResolvedExpectedImportOrder[2] != 2
                    || Host.StandaloneResolvedImports[0] != 0
                    || Host.StandaloneResolvedImports[1] != 1
                    || Host.StandaloneResolvedImports[2] != 1
                    || Host.StandaloneResolvedImports[3] != 1
                    || Host.StandaloneResolvedImports[4]
                        != Host.StandaloneResolvedImports[36]
                    || Host.StandaloneResolvedImports[12] != 3
                    || Host.StandaloneResolvedImports[13] != 1
                    || Host.StandaloneResolvedImports[19] != 1
                    || Host.StandaloneResolvedImports[20] != 1
                    || Host.StandaloneResolvedImports[32] != 1
                    || Host.StandaloneResolvedImports[33] != 1
                    || Host.StandaloneResolvedImports[34] != 1
                    || Host.StandaloneResolvedImports[35] != 1
                    || Host.StandaloneResolvedImports[44] != 3
                    || Host.StandaloneResolvedImports[45] != 2
                    || Host.StandaloneResolvedImports[51] != 1
                    || Host.StandaloneResolvedImports[52] != 2
                    || Host.StandaloneResolvedImports[53] != 1
                    || Host.StandaloneResolvedImports[54] != 1
                    || Host.StandaloneResolvedImports[60] != 0
                    || Host.StandaloneCallableTypeIds[1] != 1
                    || Host.StandaloneCallableTypeIds[2] != 1
                    || Host.StandaloneCallableEffects[1] != 0
                    || Host.StandaloneCallableEffects[2] != 4) {
                    return 29;
                }
                return 0;
            }
            if (Host.Expected[2] == 10) {
                if (rootStatus != 0
                    || Host.StandaloneResolvedRootCount[0] != 1
                    || Host.StandaloneResolvedImportCount[0] != 2
                    || Host.StandaloneResolvedExpectedImportCount[0] != 1
                    || Host.StandaloneResolvedProviderGroupCount[0] != 2
                    || Host.StandaloneResolvedImports[19] != 1
                    || Host.StandaloneResolvedImports[20] != 1
                    || Host.StandaloneResolvedImports[51] != 2
                    || Host.StandaloneResolvedImports[52] != 1
                    || Host.StandaloneResolvedImports[53] != 1
                    || Host.StandaloneCallableTypeIds[1] != 1
                    || Host.StandaloneCallableTypeIds[2] != 1) {
                    return 35;
                }
                return 0;
            }
            if (Host.Expected[2] == 5) {
                if (rootStatus != 1
                    || Host.StandaloneResolvedRootCount[0] != 0
                    || Host.StandaloneResolvedImportCount[0] != 0
                    || Host.StandaloneResolvedExpectedImportCount[0] != 0
                    || Host.StandaloneResolvedProviderGroupCount[0] != 0) {
                    return 30;
                }
                return 0;
            }
            if (Host.Expected[2] == 6) {
                if (rootStatus != 2
                    || Host.StandaloneResolvedRootCount[0] != 0
                    || Host.StandaloneResolvedImportCount[0] != 0
                    || Host.StandaloneResolvedExpectedImportCount[0] != 0
                    || Host.StandaloneResolvedProviderGroupCount[0] != 0) {
                    return 31;
                }
                return 0;
            }
            if (Host.Expected[2] == 7) {
                if (rootStatus != 3
                    || Host.StandaloneResolvedRootCount[0] != 0
                    || Host.StandaloneResolvedImportCount[0] != 0
                    || Host.StandaloneResolvedProviderGroupCount[0] != 0) {
                    return 32;
                }
                return 0;
            }
            if (Host.Expected[2] == 8) {
                if (rootStatus != 2
                    || Host.StandaloneResolvedRootCount[0] != 0
                    || Host.StandaloneResolvedImportCount[0] != 0
                    || Host.StandaloneResolvedProviderGroupCount[0] != 0) {
                    return 33;
                }
                return 0;
            }
            if (Host.Expected[2] == 9) {
                if (rootStatus != 3
                    || Host.StandaloneResolvedRootCount[0] != 0
                    || Host.StandaloneResolvedImportCount[0] != 0
                    || Host.StandaloneResolvedProviderGroupCount[0] != 0) {
                    return 34;
                }
                return 0;
            }
            if (Host.Expected[2] == 11
                || Host.Expected[2] == 12
                || Host.Expected[2] == 13
                || Host.Expected[2] == 14) {
                if (rootStatus != 1
                    || Host.StandaloneResolvedRootCount[0] != 0
                    || Host.StandaloneResolvedImportCount[0] != 0
                    || Host.StandaloneResolvedExpectedImportCount[0] != 0
                    || Host.StandaloneResolvedProviderGroupCount[0] != 0) {
                    return 36;
                }
                return 0;
            }
            if (Host.Expected[2] == 1) {
                if (rootStatus != 0
                    || Host.StandaloneResolvedRootCount[0] != 2
                    || Host.StandaloneCallableEffects[0] != 0
                    || Host.StandaloneCallableEffects[1] != 0
                    || Host.StandaloneCallableTypeIds[0] != 1
                    || Host.StandaloneCallableTypeIds[1] != 1
                    || Host.StandaloneTypeFactCount[0] != 3
                    || Host.StandaloneTypeFacts[3] != 0
                    || Host.StandaloneTypeFacts[4] != 8
                    || Host.StandaloneTypeFacts[5] != 0
                    || Host.StandaloneTypeFacts[6] != 0
                    || Host.StandaloneTypeFacts[7] != 0
                    || Host.StandaloneTypeFacts[8] != 0
                    || Host.StandaloneTypeFacts[9] != 1
                    || Host.StandaloneTypeFacts[10] != 8
                    || Host.StandaloneTypeFacts[11] != 1
                    || Host.StandaloneArtifactParameterStarts[0] != 0
                    || Host.StandaloneArtifactParameterStarts[1] != 1
                    || Host.StandaloneArtifactParameterStarts[2] != 2
                    || Host.StandaloneParameterTypeIds[1] != 2
                    || Host.StandaloneParameterTypeIds[2] != 3
                    || Host.StandaloneResolvedRoots[10] != 1
                    || Host.StandaloneResolvedRoots[11] != 1
                    || Host.StandaloneResolvedRoots[30] != 1
                    || Host.StandaloneResolvedRoots[31] != 1) {
                    return 26;
                }
                return 0;
            }
            if (Host.Expected[2] == 2) {
                if (rootStatus != 0
                    || Host.StandaloneCallableEffects[0]
                        != Host.Expected[3]
                    || Host.StandaloneCallableEffects[1] != 0) {
                    return 27;
                }
                return 0;
            }
            if (Host.Expected[2] == 3) {
                if (rootStatus != 0
                    || Host.StandaloneResolvedRootCount[0] != 2
                    || Host.StandaloneContractFactCount[0] != 3
                    || Host.StandaloneContractFacts[7] != 11
                    || Host.StandaloneContractFacts[8] != 3
                    || Host.StandaloneContractFacts[9] != 0
                    || Host.StandaloneContractFacts[10] != 1
                    || Host.StandaloneContractFacts[11]
                        != Host.Expected[4]
                    || Host.StandaloneContractFacts[12] != 1
                    || Host.StandaloneContractFacts[14] != 2
                    || Host.StandaloneContractFacts[15] != 0
                    || Host.StandaloneContractFacts[16] != 1
                    || Host.StandaloneContractFacts[17] != 1
                    || Host.StandaloneContractFacts[18] != 0
                    || Host.StandaloneContractFacts[19] != 0
                    || Host.StandaloneContractFacts[20] != 0
                    || Host.StandaloneContractFacts[21] != 4
                    || Host.StandaloneContractFacts[22] != 2
                    || Host.StandaloneContractFacts[23] != 0
                    || Host.StandaloneContractFacts[24] != 0
                    || Host.StandaloneContractFacts[25] != 1
                    || Host.StandaloneContractFacts[26] != 0
                    || Host.StandaloneContractFacts[27] != 0) {
                    return 28;
                }
                return 0;
            }
            if (rootStatus == 0
                && (Host.StandaloneResolvedRootCount[0] != 2
                    || Host.StandaloneCallableEffects[0] != 0
                    || Host.StandaloneCallableEffects[1] != 0
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
                "StandaloneArtifactParameterStarts",
                "StandaloneParameterTypeIds",
                "StandaloneModuleDirectory",
                "StandaloneModuleOrder",
                "StandaloneTypeFactCount",
                "StandaloneTypeFacts",
                "StandaloneContractFactCount",
                "StandaloneContractFacts",
                "StandaloneContractFactOrder",
                "StandaloneContractComparisonFactCount",
                "StandaloneContractComparisonFacts",
                "StandaloneContractComparisonFactOrder",
                "StandaloneResolvedImportCount",
                "StandaloneResolvedImports",
                "StandaloneResolvedImportOrder",
                "StandaloneResolvedExpectedImportCount",
                "StandaloneResolvedProviderGroupCount",
                "StandaloneResolvedExpectedImportOrder",
                "StandaloneResolvedRootCount",
                "StandaloneResolvedRoots",
                "StandaloneCallableTypeIds",
                "StandaloneCallableEffects",
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
    public async Task TerrumOwnedSourceBinderValidatesOrderedCallUsesAsync(
        BoundRuntimePolicy policy)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var source = SourceText.From(
            "using api; int32 Main(int32 value) { "
            + "Imported(Imported(value)); return Imported(7); }");
        var input = new StandaloneMethodImportInput(Agreement,
            "consumer/module", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodProviderArtifact("provider/leaf",
                        "api.Imported", scalarParameter: true)),
            ], [
                new StandaloneMethodRoot(0, "provider/leaf",
                    "api.Imported"),
            ]);
        var sourcePaths = new[] {
            "sources/Terrum.CodeAnalysis.Terrum/Text/SourceRange.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/CharacterFacts.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Text/Utf16CodeUnits.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Text/Utf16Scalar.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/CharacterLiteral.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/StringEscape.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/Token.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/ManagedTokenChain.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/Scanner.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/ManagedScanner.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/TokenSnapshot.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/TokenTriviaSnapshot.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/WideIntegerFacts.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/IntegerParsing.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/TokenIntegerSnapshot.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/TokenDiagnosticSnapshot.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodImportInput.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneArtifactDirectory.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneStringTable.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneModuleDirectory.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneCallableTypes.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodEffects.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodOutputStates.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodRoots.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodImports.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodImportBindings.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Semantics/StandaloneOwnedSourceProgram.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Semantics/StandaloneOwnedSourceNormalization.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Semantics/StandaloneOwnedSourceRemapping.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Semantics/StandaloneOwnedSourceWriter.terrum",
        };
        const string verificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 40; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0) { return status; }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0) { return status; }
                if (Host.StandaloneSourceProgramStatus[0] != 0
                    || Host.StandaloneNormalizedProgramStatus[0] != 0
                    || Host.StandaloneResolvedRootCount[0] != 1
                    || Host.StandaloneSourceImportedCallableCount[0] != 1
                    || Host.StandaloneLocalCallableCount[0] != 1
                    || Host.StandaloneSourceBodyCount[0] != 1
                    || Host.StandaloneSourceExpressionCount[0] != 5
                    || Host.StandaloneSourceStatementCount[0] != 2
                    || Host.StandaloneSourceDirectCallCount[0] != 3
                    || Host.StandaloneSourceCallArgumentCount[0] != 3
                    || Host.StandaloneSourceDirectCalls[0] != 2
                    || Host.StandaloneSourceDirectCalls[1] != 0
                    || Host.StandaloneSourceDirectCalls[2] != 0
                    || Host.StandaloneSourceDirectCalls[3]
                        != Host.StandaloneCallableTypeIds[0]
                    || Host.StandaloneSourceDirectCalls[4] != 0
                    || Host.StandaloneSourceDirectCalls[5] != 1
                    || Host.StandaloneSourceDirectCalls[6] != -1
                    || Host.StandaloneSourceDirectCalls[7] != 2
                    || Host.StandaloneSourceDirectCalls[8] != 0
                    || Host.StandaloneSourceDirectCalls[9] != 0
                    || Host.StandaloneSourceDirectCalls[10]
                        != Host.StandaloneCallableTypeIds[0]
                    || Host.StandaloneSourceDirectCalls[11] != 1
                    || Host.StandaloneSourceDirectCalls[12] != 1
                    || Host.StandaloneSourceDirectCalls[13] != -1
                    || Host.StandaloneSourceDirectCalls[14] != 2
                    || Host.StandaloneSourceDirectCalls[15] != 0
                    || Host.StandaloneSourceDirectCalls[16] != 0
                    || Host.StandaloneSourceDirectCalls[17]
                        != Host.StandaloneCallableTypeIds[0]
                    || Host.StandaloneSourceDirectCalls[18] != 2
                    || Host.StandaloneSourceDirectCalls[19] != 1
                    || Host.StandaloneSourceDirectCalls[20] != -1
                    || Host.StandaloneSourceCallArguments[0] != 1
                    || Host.StandaloneSourceCallArguments[1] != 0
                    || Host.StandaloneSourceCallArguments[2] != 1
                    || Host.StandaloneSourceCallArguments[3] != 0
                    || Host.StandaloneSourceCallArguments[4] != 0
                    || Host.StandaloneSourceCallArguments[5] != 1
                    || Host.StandaloneSourceCallArguments[6] != 3
                    || Host.StandaloneSourceCallArguments[7] != 0
                    || Host.StandaloneSourceCallArguments[8] != 1
                    || Host.StandaloneSourceExpressions[0] != 1
                    || Host.StandaloneSourceExpressions[8] != 3
                    || Host.StandaloneSourceExpressions[16] != 3
                    || Host.StandaloneSourceExpressions[24] != 2
                    || Host.StandaloneSourceExpressions[32] != 3
                    || Host.StandaloneSourceExpressionUseCounts[0] != 1
                    || Host.StandaloneSourceExpressionUseCounts[1] != 1
                    || Host.StandaloneSourceExpressionUseCounts[2] != 1
                    || Host.StandaloneSourceExpressionUseCounts[3] != 1
                    || Host.StandaloneSourceExpressionUseCounts[4] != 1
                    || Host.StandaloneSourceDirectCallUseCounts[0] != 1
                    || Host.StandaloneSourceDirectCallUseCounts[1] != 1
                    || Host.StandaloneSourceDirectCallUseCounts[2] != 1
                    || Host.StandaloneSourceCallArgumentOwners[0] != 1
                    || Host.StandaloneSourceCallArgumentOwners[1] != 2
                    || Host.StandaloneSourceCallArgumentOwners[2] != 3
                    || Host.StandaloneSourceStatements[0] != 2
                    || Host.StandaloneSourceStatements[1] != 2
                    || Host.StandaloneSourceStatements[3] != 1
                    || Host.StandaloneSourceStatements[4] != 4
                    || Host.StandaloneNormalizedBodyCount[0] != 1
                    || Host.StandaloneNormalizedHomeCount[0] != 5
                    || Host.StandaloneNormalizedConstantCount[0] != 1
                    || Host.StandaloneInvokeCount[0] != 3
                    || Host.StandaloneInvokeInputCount[0] != 3
                    || Host.StandaloneNormalizedOperationCount[0] != 6
                    || Host.StandaloneInvokeInputs[0] != 2
                    || Host.StandaloneInvokeInputs[1] != 1
                    || Host.StandaloneInvokeInputs[2] != 0
                    || Host.StandaloneInvokeInputs[3] != 2
                    || Host.StandaloneInvokeInputs[4] != 1
                    || Host.StandaloneInvokeInputs[5] != 1
                    || Host.StandaloneInvokeInputs[6] != 2
                    || Host.StandaloneInvokeInputs[7] != 1
                    || Host.StandaloneInvokeInputs[8] != 3
                    || Host.StandaloneInvokes[0] != 2
                    || Host.StandaloneInvokes[1] != 0
                    || Host.StandaloneInvokes[2] != 0
                    || Host.StandaloneInvokes[3] != 0
                    || Host.StandaloneInvokes[4] != 1
                    || Host.StandaloneInvokes[5] != 2
                    || Host.StandaloneInvokes[6] != 1
                    || Host.StandaloneInvokes[9] != -1
                    || Host.StandaloneInvokes[10] != 2
                    || Host.StandaloneInvokes[11] != 0
                    || Host.StandaloneInvokes[12] != 0
                    || Host.StandaloneInvokes[13] != 1
                    || Host.StandaloneInvokes[14] != 1
                    || Host.StandaloneInvokes[15] != 2
                    || Host.StandaloneInvokes[16] != 2
                    || Host.StandaloneInvokes[19] != -1
                    || Host.StandaloneInvokes[20] != 2
                    || Host.StandaloneInvokes[21] != 0
                    || Host.StandaloneInvokes[22] != 0
                    || Host.StandaloneInvokes[23] != 2
                    || Host.StandaloneInvokes[24] != 1
                    || Host.StandaloneInvokes[25] != 2
                    || Host.StandaloneInvokes[26] != 4
                    || Host.StandaloneInvokes[29] != -1
                    || Host.StandaloneDirectCallInvokeMap[0] != 1
                    || Host.StandaloneDirectCallInvokeMap[1] != 0
                    || Host.StandaloneDirectCallInvokeMap[2] != 2
                    || Host.StandaloneDirectCallTraversalCount[0] != 3
                    || Host.StandaloneDirectCallTraversal[0] != 0
                    || Host.StandaloneDirectCallTraversal[1] != 1
                    || Host.StandaloneDirectCallTraversal[2] != 2
                    || Host.StandaloneNormalizedOperations[0] != 3
                    || Host.StandaloneNormalizedOperations[1] != 0
                    || Host.StandaloneNormalizedOperations[4] != 3
                    || Host.StandaloneNormalizedOperations[5] != 1
                    || Host.StandaloneNormalizedOperations[8] != 1
                    || Host.StandaloneNormalizedOperations[9] != 0
                    || Host.StandaloneNormalizedOperations[12] != 2
                    || Host.StandaloneNormalizedOperations[13] != 3
                    || Host.StandaloneNormalizedOperations[16] != 3
                    || Host.StandaloneNormalizedOperations[17] != 2
                    || Host.StandaloneNormalizedOperations[20] != 4
                    || Host.StandaloneNormalizedOperations[21] != 4) {
                    return 41;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0) { return status; }
                if (Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterSectionCount[0] < 5
                    || Host.StandaloneWriterSections[0] != 1
                    || Host.StandaloneWriterSections[5] != 2
                    || Host.StandaloneWriterSections[10] != 3
                    || Host.StandaloneWriterSections[15] != 4) {
                    return 42;
                }
                return 0;
            }
            """;

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
        components.Add(SyntaxTree.Parse(verificationSource,
            "standalone-owned-source-program-tests.terrum"));

        var buffers = ScannerHost.CreateBuffers(source).AddRange(
            input.Capture().CreateSourceCallHostBuffers(source));
        var compilation = Compilation.Create(components, buffers, policy);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException(
                "Expected a bound standalone source-call validator.");
        var execution = NativeJit.ExecuteWithData(program,
            (NativeRuntimePolicy)policy);
        Assert.That(execution.ExitCode, Is.EqualTo(0));
        var artifact = StandaloneMethodImportOutputFramer.Frame(
            execution, buffers);
        AssertStandaloneWrittenArtifact(artifact);

        var contractSource = SourceText.From(
            "using api; bool Main(ref<init int32> output, "
            + "ref<init int32> source) { "
            + "return Run(ref init output, ref init source); }");
        var contractInput = new StandaloneMethodImportInput(Agreement,
            "consumer/contracts", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateOutputStateArtifact("provider/contracts",
                        includeDependency: true, sourcedDependency: true,
                        predicateGraphRow: 2)),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateOutputStateArtifact("provider/other",
                        includeDependency: true, exportKey: "api/Other")),
            ], [
                new StandaloneMethodRoot(0, "provider/contracts",
                    "api/Run"),
                new StandaloneMethodRoot(1, "provider/other",
                    "api/Other"),
            ]);
        const string contractVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 50; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0) { return status; }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0) { return status; }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0) { return status; }
                if (Host.StandaloneWriterPredicateGraphMapCount[0] != 2
                    || Host.StandaloneWriterPredicateGraphMap[1] != 2
                    || Host.StandaloneWriterPredicateGraphMap[2] != 1
                    || Host.StandaloneWriterPredicateGraphMap[3] != 1
                    || Host.StandaloneWriterPredicateGraphMap[4] != 1
                    || Host.StandaloneWriterPredicateGraphMap[5] != 1
                    || Host.StandaloneWriterPredicateGraphMap[6] != 2
                    || Host.StandaloneWriterPredicateGraphMap[7] != 1
                    || Host.StandaloneWriterMethodContractCounts[0] != 0
                    || Host.StandaloneWriterMethodContractCounts[1] != 3
                    || Host.StandaloneWriterMethodContractCounts[2] != 3) {
                    return 51;
                }
                return 0;
            }
            """;
        var contractComponents = new List<SyntaxTree>(
            components.GetRange(0, sourcePaths.Length));
        contractComponents.Add(SyntaxTree.Parse(
            contractVerificationSource,
            "standalone-owned-source-contract-writer-tests.terrum"));
        var contractBuffers = ScannerHost.CreateBuffers(contractSource)
            .AddRange(contractInput.Capture()
                .CreateSourceCallHostBuffers(contractSource));
        var contractCompilation = Compilation.Create(contractComponents,
            contractBuffers, policy);
        Assert.That(contractCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                contractCompilation.Diagnostics));
        var contractProgram = contractCompilation.Program
            ?? throw new AssertionException(
                "Expected a bound standalone contract-writer validator.");
        var contractExecution = NativeJit.ExecuteWithData(contractProgram,
            (NativeRuntimePolicy)policy);
        Assert.That(contractExecution.ExitCode, Is.EqualTo(0));
        var contractArtifact = StandaloneMethodImportOutputFramer.Frame(
            contractExecution, contractBuffers);
        AssertStandaloneOutputStateContractArtifact(contractArtifact);

        var decoderInput = new StandaloneMethodImportArtifactInput(
            Agreement, "consumer/contracts",
            new TrRmStandardArtifactInput(SchemaIdentity, contractArtifact),
            contractInput.Artifacts);
        var decoderBuffers
            = decoderInput.Capture().CreateSourceFreeReaderHostBuffers();
        Assert.That(decoderBuffers.Any(buffer => buffer.Name == "Source"
                || buffer.Name == "Tokens"),
            Is.False);
        var decoderSourcePaths = new[] {
            "sources/Terrum.CodeAnalysis.Terrum/Text/Utf16CodeUnits.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Text/Utf16Scalar.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodImportInput.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneArtifactDirectory.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneStringTable.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneModuleDirectory.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneCallableTypes.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodEffects.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodOutputStates.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodRoots.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodImports.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodImportBindings.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Semantics/StandaloneOwnedSourceDecoder.terrum",
        };
        const string decoderVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                int32 status = DecodeStandaloneModuleMetadata();
                if (status != 0) { return status; }
                if (Host.StandaloneDecoderStatus[0] != 0
                    || Host.StandaloneDecoderSelectedModuleId[0] != 0
                    || Host.StandaloneDecoderMethodCount[0] != 3
                    || Host.StandaloneDecoderBodyCount[0] != 1
                    || Host.StandaloneDecoderLocalCallableCount[0] != 1
                    || Host.StandaloneDecoderImportedCallableCount[0] != 2
                    || Host.StandaloneDecoderImportMapCount[0] != 2
                    || Host.StandaloneDecoderBodies[0] != 0
                    || Host.StandaloneDecoderBodies[2] != 1
                    || Host.StandaloneDecoderBodies[3] != 1
                    || Host.StandaloneDecoderBodies[4] != 1
                    || Host.StandaloneDecoderBodyOwners[1] != 1
                    || Host.StandaloneDecoderBodies[7] != 7
                    || Host.StandaloneDecoderBodies[8] < 2
                    || Host.StandaloneDecoderHomeCount[0] < 2
                    || Host.StandaloneDecoderTypeMapCount[0] <= 0
                    || Host.StandaloneDecoderTypeMapTypeIds[0] <= 0
                    || Host.StandaloneDecoderMethodMapCount[0] != 1
                    || Host.StandaloneDecoderMethodMapTargets[0] != 2
                    || Host.StandaloneDecoderMethodMapTargets[1] != 0
                    || Host.StandaloneDecoderMethodMapTargets[2] != 0
                    || Host.StandaloneDecoderMethodMapTargets[3] != 1
                    || Host.StandaloneDecoderConstantMapCount[0] != 0
                    || Host.StandaloneDecoderCallCount[0] != 1
                    || Host.StandaloneDecoderCallInputCount[0] != 2
                    || Host.StandaloneDecoderBodies[32] != 0
                    || Host.StandaloneDecoderBodies[33] != 1
                    || Host.StandaloneDecoderCalls[1] != 1
                    || Host.StandaloneDecoderCalls[6] != 1
                    || Host.StandaloneDecoderCalls[10] != 2
                    || Host.StandaloneDecoderBodies[19] != 1
                    || Host.StandaloneDecoderBodies[21] != 2
                    || Host.StandaloneDecoderBodies[26] <= 0
                    || Host.StandaloneDecoderMethods[0] != 0
                    || Host.StandaloneDecoderMethods[3] != 1
                    || Host.StandaloneDecoderMethods[10] != 1
                    || Host.StandaloneDecoderMethods[13] != 1
                    || Host.StandaloneDecoderMethods[16 + 3] != 2
                    || Host.StandaloneDecoderMethods[16 + 13] != 2
                    || Host.StandaloneDecoderMethods[16 + 14] != 1
                    || Host.StandaloneDecoderMethods[32 + 3] != 3
                    || Host.StandaloneDecoderMethods[32 + 13] != 2
                    || Host.StandaloneDecoderMethods[32 + 14] != 2
                    || Host.StandaloneDecoderExpectedGroupCallableIds[1] != 0
                    || Host.StandaloneDecoderExpectedGroupCallableIds[2] != 1
                    || Host.StandaloneDecoderExpectedGroupRepresentativeImportIds[
                        1] != 0
                    || Host.StandaloneDecoderExpectedGroupRepresentativeImportIds[
                        2] != 1
                    || Host.StandaloneDecoderImportedCallables[6] != 1
                    || Host.StandaloneDecoderImportedCallables[16 + 6] != 2) {
                    return 70;
                }
                return 0;
            }
            """;
        var decoderComponents = new List<SyntaxTree>(
            decoderSourcePaths.Length + 1);
        foreach (var sourcePath in decoderSourcePaths)
        {
            var path = TerrumProjectPaths.RepositoryFile(root, sourcePath);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path)
                .ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False,
                string.Join(Environment.NewLine, tree.Diagnostics));
            decoderComponents.Add(tree);
        }
        decoderComponents.Add(SyntaxTree.Parse(decoderVerificationSource,
            "standalone-source-free-module-decoder-tests.terrum"));
        var decoderCompilation = Compilation.Create(decoderComponents,
            decoderBuffers, policy);
        Assert.That(decoderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                decoderCompilation.Diagnostics));
        var decoderProgram = decoderCompilation.Program
            ?? throw new AssertionException(
                "Expected a bound standalone module-decoder validator.");
        var decoderExecution = NativeJit.ExecuteWithData(decoderProgram,
            (NativeRuntimePolicy)policy);
        Assert.That(decoderExecution.ExitCode, Is.EqualTo(0));

        var constantDecoderInput = new StandaloneMethodImportArtifactInput(
            Agreement, "consumer/module",
            new TrRmStandardArtifactInput(SchemaIdentity, artifact),
            input.Artifacts);
        var constantDecoderBuffers = constantDecoderInput.Capture()
            .CreateSourceFreeReaderHostBuffers();
        const string constantDecoderVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                int32 status = DecodeStandaloneModuleMetadata();
                if (status != 0) { return status; }
                if (Host.StandaloneDecoderBodyCount[0] != 1
                    || Host.StandaloneDecoderCallCount[0] != 3
                    || Host.StandaloneDecoderCallInputCount[0] != 3
                    || Host.StandaloneDecoderMethodMapCount[0] != 1
                    || Host.StandaloneDecoderConstantMapCount[0] != 1
                    || Host.StandaloneDecoderConstantMapEntries[0] <= 0
                    || Host.StandaloneDecoderConstantMapEntries[1] <= 0
                    || Host.StandaloneDecoderConstantMapEntries[2] <= 0) {
                    return 71;
                }
                return 0;
            }
            """;
        var constantDecoderComponents
            = new List<SyntaxTree>(decoderComponents);
        constantDecoderComponents[^1] = SyntaxTree.Parse(
            constantDecoderVerificationSource,
            "standalone-source-free-constant-map-tests.terrum");
        var constantDecoderCompilation = Compilation.Create(
            constantDecoderComponents, constantDecoderBuffers, policy);
        Assert.That(constantDecoderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                constantDecoderCompilation.Diagnostics));
        var constantDecoderProgram = constantDecoderCompilation.Program
            ?? throw new AssertionException(
                "Expected a constant-map decoder validator.");
        var constantDecoderExecution = NativeJit.ExecuteWithData(
            constantDecoderProgram, (NativeRuntimePolicy)policy);
        Assert.That(constantDecoderExecution.ExitCode, Is.EqualTo(0));

        var malformedConstantArtifact = artifact.ToArray();
        var constantSectionCount = checked((int)BinaryPrimitives
            .ReadUInt32LittleEndian(malformedConstantArtifact.AsSpan(12)));
        var constantBodySectionStart = -1;
        var bodyTableCount = 0;
        for (var section = 0; section < constantSectionCount; section++)
        {
            var entryOffset = checked(32 + (section * 16));
            if (BinaryPrimitives.ReadUInt16LittleEndian(
                    malformedConstantArtifact.AsSpan(entryOffset)) == 0x0020)
            {
                bodyTableCount = checked((int)BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        malformedConstantArtifact.AsSpan(entryOffset + 4)));
                constantBodySectionStart = checked((int)BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        malformedConstantArtifact.AsSpan(entryOffset + 8)));
                break;
            }
        }
        Assert.That(bodyTableCount, Is.GreaterThan(0));
        var constantBodyIndexLength = checked((bodyTableCount + 1) * 4);
        var constantBodyDataOffset = checked((int)BinaryPrimitives
            .ReadUInt32LittleEndian(
                malformedConstantArtifact.AsSpan(
                    constantBodySectionStart)));
        var constantBodyStart = checked(constantBodySectionStart
            + constantBodyIndexLength + constantBodyDataOffset);
        var mapMask = malformedConstantArtifact[constantBodyStart + 2];
        Assert.That(mapMask & 16, Is.Not.EqualTo(0));
        var bodyCursor = constantBodyStart + 5;
        var homeMapCount = 0;
        var typeMapCount = 0;
        var methodMapCount = 0;
        var fieldMapCount = 0;
        if ((mapMask & 1) != 0)
        {
            homeMapCount = checked((int)BinaryPrimitives
                .ReadUInt32LittleEndian(
                    malformedConstantArtifact.AsSpan(bodyCursor)));
            bodyCursor += 4;
        }
        if ((mapMask & 2) != 0)
        {
            typeMapCount = checked((int)BinaryPrimitives
                .ReadUInt32LittleEndian(
                    malformedConstantArtifact.AsSpan(bodyCursor)));
            bodyCursor += 4;
        }
        if ((mapMask & 4) != 0)
        {
            methodMapCount = checked((int)BinaryPrimitives
                .ReadUInt32LittleEndian(
                    malformedConstantArtifact.AsSpan(bodyCursor)));
            bodyCursor += 4;
        }
        if ((mapMask & 8) != 0)
        {
            fieldMapCount = checked((int)BinaryPrimitives
                .ReadUInt32LittleEndian(
                    malformedConstantArtifact.AsSpan(bodyCursor)));
            bodyCursor += 4;
        }
        var constantMapTypeOffset = checked(bodyCursor + 12
            + (homeMapCount * 8) + (typeMapCount * 4)
            + (methodMapCount * 4) + (fieldMapCount * 4));
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedConstantArtifact.AsSpan(constantMapTypeOffset),
            0xF200_0001);
        var malformedConstantInput
            = new StandaloneMethodImportArtifactInput(Agreement,
                "consumer/module",
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedConstantArtifact),
                input.Artifacts);
        var malformedConstantBuffers = malformedConstantInput.Capture()
            .CreateSourceFreeReaderHostBuffers();
        var malformedConstantCompilation = Compilation.Create(
            constantDecoderComponents, malformedConstantBuffers, policy);
        Assert.That(malformedConstantCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                malformedConstantCompilation.Diagnostics));
        var malformedConstantProgram = malformedConstantCompilation.Program
            ?? throw new AssertionException(
                "Expected a mismatched-constant decoder validator.");
        var malformedConstantExecution = NativeJit.ExecuteWithData(
            malformedConstantProgram, (NativeRuntimePolicy)policy);
        Assert.That(malformedConstantExecution.ExitCode, Is.EqualTo(3));

        var malformedBodyArtifact = contractArtifact.ToArray();
        var sectionCount = checked((int)BinaryPrimitives
            .ReadUInt32LittleEndian(malformedBodyArtifact.AsSpan(12)));
        var bodySectionStart = -1;
        var bodySectionCount = 0;
        for (var section = 0; section < sectionCount; section++)
        {
            var entryOffset = checked(32 + (section * 16));
            if (BinaryPrimitives.ReadUInt16LittleEndian(
                    malformedBodyArtifact.AsSpan(entryOffset)) == 0x0020)
            {
                bodySectionCount = checked((int)BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        malformedBodyArtifact.AsSpan(entryOffset + 4)));
                bodySectionStart = checked((int)BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        malformedBodyArtifact.AsSpan(entryOffset + 8)));
                break;
            }
        }
        Assert.That(bodySectionCount, Is.GreaterThan(0));
        Assert.That(bodySectionStart, Is.GreaterThanOrEqualTo(32));
        var bodyIndexLength = checked((bodySectionCount + 1) * 4);
        var bodyDataOffset = checked((int)BinaryPrimitives
            .ReadUInt32LittleEndian(
                malformedBodyArtifact.AsSpan(bodySectionStart)));
        var bodyStart = checked(bodySectionStart + bodyIndexLength
            + bodyDataOffset);
        malformedBodyArtifact[bodyStart + 3] = 1;
        var malformedDecoderInput = new StandaloneMethodImportArtifactInput(
            Agreement, "consumer/contracts",
            new TrRmStandardArtifactInput(SchemaIdentity,
                malformedBodyArtifact), contractInput.Artifacts);
        var malformedDecoderBuffers
            = malformedDecoderInput.Capture()
                .CreateSourceFreeReaderHostBuffers();
        var malformedDecoderCompilation = Compilation.Create(
            decoderComponents, malformedDecoderBuffers, policy);
        Assert.That(malformedDecoderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                malformedDecoderCompilation.Diagnostics));
        var malformedDecoderProgram = malformedDecoderCompilation.Program
            ?? throw new AssertionException(
                "Expected a malformed-body decoder validator.");
        var malformedDecoderExecution = NativeJit.ExecuteWithData(
            malformedDecoderProgram, (NativeRuntimePolicy)policy);
        Assert.That(malformedDecoderExecution.ExitCode, Is.EqualTo(3));

        var aliasSource = SourceText.From(
            "using api; int32 Main() { AliasA(); return AliasB(); }");
        var aliasInput = new StandaloneMethodImportInput(Agreement,
            "consumer/alias", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateAliasedProviderArtifact("provider/alias",
                        "api.AliasA", "api.AliasB")),
            ], [
                new StandaloneMethodRoot(0, "provider/alias",
                    "api.AliasA"),
                new StandaloneMethodRoot(0, "provider/alias",
                    "api.AliasB"),
            ]);
        const string aliasVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 50; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0) { return status; }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0) { return status; }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                return WriteStandaloneScalarModule();
            }
            """;
        var aliasComponents = new List<SyntaxTree>(
            components.GetRange(0, sourcePaths.Length));
        aliasComponents.Add(SyntaxTree.Parse(aliasVerificationSource,
            "standalone-owned-source-alias-writer-tests.terrum"));
        var aliasBuffers = ScannerHost.CreateBuffers(aliasSource).AddRange(
            aliasInput.Capture().CreateSourceCallHostBuffers(aliasSource));
        var aliasCompilation = Compilation.Create(aliasComponents,
            aliasBuffers, policy);
        Assert.That(aliasCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                aliasCompilation.Diagnostics));
        var aliasProgram = aliasCompilation.Program
            ?? throw new AssertionException(
                "Expected a bound standalone alias-writer validator.");
        var aliasExecution = NativeJit.ExecuteWithData(aliasProgram,
            (NativeRuntimePolicy)policy);
        Assert.That(aliasExecution.ExitCode, Is.EqualTo(0));
        var aliasArtifact = StandaloneMethodImportOutputFramer.Frame(
            aliasExecution, aliasBuffers);
        AssertStandaloneAliasesShareExpectedMethod(aliasArtifact);

        var remapSource = SourceText.From(
            "using api; int32 Local(int32 first, int32 second) { "
            + "return first; } int32 Main(int32 value) { return "
            + "Imported(Local(second: Imported(value), first: value)); }");
        var remapInput = new StandaloneMethodImportInput(Agreement,
            "consumer/module", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodProviderArtifact("provider/leaf",
                        "api.Imported", scalarParameter: true)),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodProviderArtifact("provider/other",
                        "other.Imported", scalarParameter: true)),
            ], [
                new StandaloneMethodRoot(0, "provider/leaf",
                    "api.Imported"),
                new StandaloneMethodRoot(1, "provider/other",
                    "other.Imported"),
            ]);
        const string remapVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 50; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0) { return status; }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0) { return status; }
                if (Host.StandaloneModuleCount[0] != 3
                    || Host.StandaloneLocalCallableCount[0] != 2
                    || Host.StandaloneSourceImportedCallableCount[0] != 2
                    || Host.StandaloneSourceBodyCount[0] != 2
                    || Host.StandaloneSourceExpressionCount[0] != 6
                    || Host.StandaloneSourceDirectCallCount[0] != 3
                    || Host.StandaloneSourceCallArgumentCount[0] != 4
                    || Host.StandaloneInvokeCount[0] != 3
                    || Host.StandaloneTypeFactCount[0] != 1) {
                    return 51;
                }
                int32 callableBase
                    = Host.StandaloneResolvedRootCount[0]
                        + Host.StandaloneResolvedImports.Length / 32
                        + Host.StandaloneResolvedProviderGroupCount[0];
                int32 callableCount
                    = callableBase + Host.StandaloneLocalCallableCount[0];
                Host.StandaloneRewriteCallableCount[0] = callableCount;
                for (mut int32 id = 0;
                    id < Host.StandaloneModuleCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteModuleIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < Host.StandaloneLocalCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteLocalCallableIds[id] = id;
                }
                Host.StandaloneRewriteImportedCallableIds[0] = 1;
                Host.StandaloneRewriteImportedCallableIds[1] = 0;
                Host.StandaloneRewriteCallableIds[0] = 1;
                Host.StandaloneRewriteCallableIds[1] = 0;
                for (mut int32 id = 2;
                    id < callableCount;
                    id = id + 1) {
                    Host.StandaloneRewriteCallableIds[id] = id;
                }
                Host.StandaloneRewriteTypeIds[0] = 0;
                for (mut int32 id = 1;
                    id <= Host.StandaloneTypeFactCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteTypeIds[id] = id;
                }
                Host.StandaloneRewriteBodyIds[0] = 1;
                Host.StandaloneRewriteBodyIds[1] = 0;
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id] = id;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                if (Host.StandaloneRewriteStatus[0] != 0
                    || Host.StandaloneRewrittenModuleCount[0] != 3
                    || Host.StandaloneRewrittenLocalCallableCount[0] != 2
                    || Host.StandaloneRewrittenImportedCallableCount[0] != 2
                    || Host.StandaloneRewrittenCallableCount[0]
                        != callableCount
                    || Host.StandaloneRewrittenSourceBodyCount[0] != 2
                    || Host.StandaloneRewrittenNormalizedBodyCount[0] != 2
                    || Host.StandaloneRewrittenLocalCallables[1]
                        != callableBase
                    || Host.StandaloneRewrittenLocalCallables[2] != 1
                    || Host.StandaloneRewrittenLocalCallables[4]
                        != callableBase + 1
                    || Host.StandaloneRewrittenLocalCallables[5] != 0
                    || Host.StandaloneRewrittenImportedCallables[4] != 0
                    || Host.StandaloneRewrittenImportedCallables[12] != 1
                    || Host.StandaloneRewrittenImportedCallables[8]
                        != Host.StandaloneSourceImportedCallables[0]
                    || Host.StandaloneRewrittenImportedCallables[9]
                        != Host.StandaloneSourceImportedCallables[1]
                    || Host.StandaloneRewrittenSourceBodies[0] != 0
                    || Host.StandaloneRewrittenSourceBodies[12] != 1
                    || Host.StandaloneRewrittenNormalizedBodies[0] != 0
                    || Host.StandaloneRewrittenNormalizedBodies[12] != 1
                    || Host.StandaloneRewrittenSourceExpressions[7] != 1
                    || Host.StandaloneRewrittenSourceExpressions[15] != 0
                    || Host.StandaloneRewrittenSourceDirectCalls[0] != 2
                    || Host.StandaloneRewrittenSourceDirectCalls[1] != 1
                    || Host.StandaloneRewrittenSourceDirectCalls[2] != 1
                    || Host.StandaloneRewrittenSourceDirectCalls[7] != 1
                    || Host.StandaloneRewrittenSourceDirectCalls[8] != 0
                    || Host.StandaloneRewrittenSourceDirectCalls[9]
                        != callableBase
                    || Host.StandaloneRewrittenSourceDirectCalls[11] != 1
                    || Host.StandaloneRewrittenSourceDirectCalls[12] != 2
                    || Host.StandaloneRewrittenSourceDirectCalls[14] != 2
                    || Host.StandaloneRewrittenSourceDirectCalls[15] != 1
                    || Host.StandaloneRewrittenSourceDirectCalls[16] != 1
                    || Host.StandaloneRewrittenSourceDirectCalls[18] != 3
                    || Host.StandaloneRewrittenSourceDirectCalls[19] != 1
                    || Host.StandaloneRewrittenSourceCallArguments[4] != 1
                    || Host.StandaloneRewrittenSourceCallArguments[7] != 0
                    || Host.StandaloneRewrittenInvokes[0] != 1
                    || Host.StandaloneRewrittenInvokes[1] != 1
                    || Host.StandaloneRewrittenInvokes[2] != 1
                    || Host.StandaloneRewrittenInvokes[3] != 0
                    || Host.StandaloneRewrittenInvokes[4] != 1
                    || Host.StandaloneRewrittenInvokes[10] != 1
                    || Host.StandaloneRewrittenInvokes[11] != 0
                    || Host.StandaloneRewrittenInvokes[12]
                        != callableBase
                    || Host.StandaloneRewrittenInvokes[13] != 1
                    || Host.StandaloneRewrittenInvokes[14] != 2
                    || Host.StandaloneRewrittenInvokes[20] != 2
                    || Host.StandaloneRewrittenInvokes[21] != 1
                    || Host.StandaloneRewrittenInvokes[22] != 1
                    || Host.StandaloneRewrittenInvokes[23] != 3
                    || Host.StandaloneRewrittenInvokes[24] != 1
                    || Host.StandaloneRewrittenDirectCallInvokeMap[0] != 2
                    || Host.StandaloneRewrittenDirectCallInvokeMap[1] != 1
                    || Host.StandaloneRewrittenDirectCallInvokeMap[2] != 0
                    || Host.StandaloneRewrittenInvokeInputs[0] != 2
                    || Host.StandaloneRewrittenInvokeInputs[2] != 0
                    || Host.StandaloneRewrittenInvokeInputs[3] != 2
                    || Host.StandaloneRewrittenInvokeInputs[5] != 0
                    || Host.StandaloneRewrittenInvokeInputs[6] != 2
                    || Host.StandaloneRewrittenInvokeInputs[8] != 1
                    || Host.StandaloneRewrittenInvokeInputs[9] != 2
                    || Host.StandaloneRewrittenInvokeInputs[11] != 2) {
                    return 52;
                }
                return 0;
            }
            """;
        var remapComponents = new List<SyntaxTree>(sourcePaths.Length + 1);
        foreach (var sourcePath in sourcePaths)
        {
            var path = TerrumProjectPaths.RepositoryFile(root, sourcePath);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path)
                .ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False,
                string.Join(Environment.NewLine, tree.Diagnostics));
            remapComponents.Add(tree);
        }
        remapComponents.Add(SyntaxTree.Parse(remapVerificationSource,
            "standalone-owned-source-remapping-tests.terrum"));

        var remapBuffers = ScannerHost.CreateBuffers(remapSource).AddRange(
            remapInput.Capture().CreateSourceCallHostBuffers(remapSource));
        var remapCompilation = Compilation.Create(remapComponents,
            remapBuffers, policy);
        Assert.That(remapCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                remapCompilation.Diagnostics));
        var remapProgram = remapCompilation.Program
            ?? throw new AssertionException(
                "Expected a bound standalone call-remapping validator.");
        Assert.That(NativeJit.Execute(remapProgram,
                (NativeRuntimePolicy)policy),
            Is.EqualTo(0));

        var callVariantSource = SourceText.From(
            "using api; void Main(void unitValue, "
            + "ref<init int32> output, int32 value) { unsafe { "
            + "Unit(unitValue); Init(ref init output); Unsafe(value); } }");
        var callVariantInput = new StandaloneMethodImportInput(Agreement,
            "consumer/module", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodProviderArtifact("provider/unit",
                        "api.Unit", resultTypeSpecRef: 0xF200_0000,
                        scalarParameter: true,
                        parameterTypeSpecRef: 0xF200_0000)),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodProviderArtifact("provider/init",
                        "api.Init", resultTypeSpecRef: 0xF200_0000,
                        scalarParameter: true,
                        parameterTypeSpecRef: 0x1100_0001,
                        typeSpecSection:
                            TrRmTypeSpecTableWriter.InitializationReference(
                                0xF200_0008, isWritable: true))),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodProviderArtifact("provider/unsafe",
                        "api.Unsafe", effectsMask: 4,
                        scalarParameter: true)),
            ], [
                new StandaloneMethodRoot(0, "provider/unit", "api.Unit"),
                new StandaloneMethodRoot(1, "provider/init", "api.Init"),
                new StandaloneMethodRoot(2, "provider/unsafe",
                    "api.Unsafe"),
            ]);
        const string callVariantVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 60; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0) { return status; }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0) { return status; }
                if (Host.StandaloneSourceProgramStatus[0] != 0
                    || Host.StandaloneNormalizedProgramStatus[0] != 0
                    || Host.StandaloneSourceParameterCount[0] != 3
                    || Host.StandaloneSourceDirectCallCount[0] != 3
                    || Host.StandaloneSourceCallArgumentCount[0] != 3
                    || Host.StandaloneSourceStatements[0] != 2
                    || Host.StandaloneSourceStatements[3] != 2
                    || Host.StandaloneSourceStatements[6] != 2
                    || Host.StandaloneSourceBodies[11] != 1
                    || Host.StandaloneNormalizedHomeCount[0] != 3
                    || Host.StandaloneInvokeCount[0] != 3
                    || Host.StandaloneInvokeInputCount[0] != 3
                    || Host.StandaloneNormalizedOperationCount[0] != 3
                    || Host.StandaloneSourceCallArguments[2] != 0
                    || Host.StandaloneSourceCallArguments[5] != 5
                    || Host.StandaloneSourceCallArguments[8] != 1
                    || Host.StandaloneInvokeInputs[0] != 0
                    || Host.StandaloneInvokeInputs[1] != 0
                    || Host.StandaloneInvokeInputs[2] != 0
                    || Host.StandaloneInvokeInputs[3] != 2
                    || Host.StandaloneInvokeInputs[4] != 5
                    || Host.StandaloneInvokeInputs[5] != 0
                    || Host.StandaloneInvokeInputs[6] != 2
                    || Host.StandaloneInvokeInputs[7] != 1
                    || Host.StandaloneInvokeInputs[8] != 1
                    || Host.StandaloneInvokes[5] != 0
                    || Host.StandaloneInvokes[15] != 0
                    || Host.StandaloneInvokes[25] != 2
                    || Host.StandaloneInvokes[26] != 2) {
                    return 61;
                }
                return 0;
            }
            """;
        var callVariantComponents
            = new List<SyntaxTree>(sourcePaths.Length + 1);
        foreach (var sourcePath in sourcePaths)
        {
            var path = TerrumProjectPaths.RepositoryFile(root, sourcePath);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path)
                .ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False,
                string.Join(Environment.NewLine, tree.Diagnostics));
            callVariantComponents.Add(tree);
        }
        callVariantComponents.Add(SyntaxTree.Parse(
            callVariantVerificationSource,
            "standalone-owned-call-variants-tests.terrum"));

        var callVariantBuffers = ScannerHost.CreateBuffers(
            callVariantSource).AddRange(
            callVariantInput.Capture().CreateSourceCallHostBuffers(
                callVariantSource));
        var callVariantCompilation = Compilation.Create(
            callVariantComponents, callVariantBuffers, policy);
        Assert.That(callVariantCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                callVariantCompilation.Diagnostics));
        var callVariantProgram = callVariantCompilation.Program
            ?? throw new AssertionException(
                "Expected a bound standalone call-variant validator.");
        Assert.That(NativeJit.Execute(callVariantProgram,
                (NativeRuntimePolicy)policy),
            Is.EqualTo(0));

        var unauthorizedUnsafeSource = SourceText.From(
            "using api; int32 Main(int32 value) { "
            + "return Unsafe(value); }");
        const string unauthorizedUnsafeVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 70; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 2
                    || Host.StandaloneSourceProgramStatus[0] != 2
                    || Host.StandaloneResolvedRootCount[0] != 0
                    || Host.StandaloneLocalCallableCount[0] != 0
                    || Host.StandaloneSourceDirectCallCount[0] != 0) {
                    return 71;
                }
                return 0;
            }
            """;
        var unauthorizedUnsafeComponents
            = new List<SyntaxTree>(sourcePaths.Length + 1);
        foreach (var sourcePath in sourcePaths)
        {
            var path = TerrumProjectPaths.RepositoryFile(root, sourcePath);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path)
                .ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False,
                string.Join(Environment.NewLine, tree.Diagnostics));
            unauthorizedUnsafeComponents.Add(tree);
        }
        unauthorizedUnsafeComponents.Add(SyntaxTree.Parse(
            unauthorizedUnsafeVerificationSource,
            "standalone-owned-unsafe-rejection-tests.terrum"));

        var unauthorizedUnsafeBuffers = ScannerHost.CreateBuffers(
            unauthorizedUnsafeSource).AddRange(
            callVariantInput.Capture().CreateSourceCallHostBuffers(
                unauthorizedUnsafeSource));
        var unauthorizedUnsafeCompilation = Compilation.Create(
            unauthorizedUnsafeComponents, unauthorizedUnsafeBuffers,
            policy);
        Assert.That(unauthorizedUnsafeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                unauthorizedUnsafeCompilation.Diagnostics));
        var unauthorizedUnsafeProgram
            = unauthorizedUnsafeCompilation.Program
                ?? throw new AssertionException(
                    "Expected a standalone unsafe-call rejection validator.");
        Assert.That(NativeJit.Execute(unauthorizedUnsafeProgram,
                (NativeRuntimePolicy)policy),
            Is.EqualTo(0));
    }

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task TerrumOwnedSourceNormalizerTerminatesAtDoesNotReturnAsync(
        BoundRuntimePolicy policy)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var source = SourceText.From(
            "using api; int32 Main(int32 value) { "
            + "Imported(value); return Imported(7); }");
        var input = new StandaloneMethodImportInput(Agreement,
            "consumer/module", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodProviderArtifact("provider/leaf",
                        "api.Imported", effectsMask: 2,
                        scalarParameter: true)),
            ], [
                new StandaloneMethodRoot(0, "provider/leaf",
                    "api.Imported"),
            ]);
        var scannerSources = new[] {
            "sources/Terrum.CodeAnalysis.Terrum/Text/SourceRange.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/CharacterFacts.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Text/Utf16CodeUnits.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Text/Utf16Scalar.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/CharacterLiteral.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/StringEscape.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/Token.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/ManagedTokenChain.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/Scanner.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/ManagedScanner.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/TokenSnapshot.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/TokenTriviaSnapshot.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/WideIntegerFacts.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/IntegerParsing.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/TokenIntegerSnapshot.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/TokenDiagnosticSnapshot.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodImportInput.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneArtifactDirectory.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneStringTable.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneModuleDirectory.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneCallableTypes.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodEffects.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodOutputStates.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodRoots.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodImports.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodImportBindings.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Semantics/StandaloneOwnedSourceProgram.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Semantics/StandaloneOwnedSourceNormalization.terrum",
        };
        const string verificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 40; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0) { return status; }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0) { return status; }
                if (Host.StandaloneSourceProgramStatus[0] != 0
                    || Host.StandaloneNormalizedProgramStatus[0] != 0
                    || Host.StandaloneResolvedRootCount[0] != 1
                    || Host.StandaloneSourceImportedCallableCount[0] != 1
                    || Host.StandaloneLocalCallableCount[0] != 1
                    || Host.StandaloneSourceBodyCount[0] != 1
                    || Host.StandaloneSourceExpressionCount[0] != 4
                    || Host.StandaloneSourceStatementCount[0] != 2
                    || Host.StandaloneSourceDirectCallCount[0] != 2
                    || Host.StandaloneSourceCallArgumentCount[0] != 2
                    || Host.StandaloneSourceDirectCalls[0] != 2
                    || Host.StandaloneSourceDirectCalls[1] != 0
                    || Host.StandaloneSourceDirectCalls[2] != 0
                    || Host.StandaloneSourceDirectCalls[3]
                        != Host.StandaloneCallableTypeIds[0]
                    || Host.StandaloneSourceDirectCalls[4] != 0
                    || Host.StandaloneSourceDirectCalls[5] != 1
                    || Host.StandaloneSourceDirectCalls[6] != -1
                    || Host.StandaloneSourceDirectCalls[7] != 2
                    || Host.StandaloneSourceDirectCalls[8] != 0
                    || Host.StandaloneSourceDirectCalls[9] != 0
                    || Host.StandaloneSourceDirectCalls[10]
                        != Host.StandaloneCallableTypeIds[0]
                    || Host.StandaloneSourceDirectCalls[11] != 1
                    || Host.StandaloneSourceDirectCalls[12] != 1
                    || Host.StandaloneSourceDirectCalls[13] != -1
                    || Host.StandaloneSourceCallArguments[0] != 0
                    || Host.StandaloneSourceCallArguments[1] != 0
                    || Host.StandaloneSourceCallArguments[2] != 1
                    || Host.StandaloneSourceCallArguments[3] != 2
                    || Host.StandaloneSourceCallArguments[4] != 0
                    || Host.StandaloneSourceCallArguments[5] != 1
                    || Host.StandaloneSourceExpressions[0] != 1
                    || Host.StandaloneSourceExpressions[8] != 3
                    || Host.StandaloneSourceExpressions[16] != 2
                    || Host.StandaloneSourceExpressions[24] != 3
                    || Host.StandaloneSourceStatements[0] != 2
                    || Host.StandaloneSourceStatements[1] != 1
                    || Host.StandaloneSourceStatements[3] != 1
                    || Host.StandaloneSourceStatements[4] != 3
                    || Host.StandaloneNormalizedBodyCount[0] != 1
                    || Host.StandaloneNormalizedHomeCount[0] != 1
                    || Host.StandaloneNormalizedConstantCount[0] != 0
                    || Host.StandaloneInvokeCount[0] != 1
                    || Host.StandaloneInvokeInputCount[0] != 1
                    || Host.StandaloneNormalizedOperationCount[0] != 1
                    || Host.StandaloneNormalizedBodies[11] != 1
                    || Host.StandaloneDirectCallInvokeMap[0] != 0
                    || Host.StandaloneDirectCallInvokeMap[1] != -1
                    || Host.StandaloneDirectCallTraversalCount[0] != 2
                    || Host.StandaloneDirectCallTraversal[0] != 0
                    || Host.StandaloneDirectCallTraversal[1] != 1
                    || Host.StandaloneInvokeInputs[0] != 2
                    || Host.StandaloneInvokeInputs[1] != 1
                    || Host.StandaloneInvokeInputs[2] != 0
                    || Host.StandaloneInvokes[5] != 0
                    || Host.StandaloneInvokes[6] != 0
                    || Host.StandaloneNormalizedOperations[0] != 3) {
                    return 41;
                }
                return 0;
            }
            """;

        var root = RepositoryRoot();
        var components = new List<SyntaxTree>(scannerSources.Length + 1);
        foreach (var sourcePath in scannerSources)
        {
            var path = TerrumProjectPaths.RepositoryFile(root, sourcePath);
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path)
                .ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False,
                string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(verificationSource,
            "standalone-owned-source-program-tests.terrum"));

        var captured = input.Capture();
        var buffers = ScannerHost.CreateBuffers(source).AddRange(
            captured.CreateSourceCallHostBuffers(source));
        var compilation = Compilation.Create(components, buffers, policy);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException(
                "Expected a bound standalone source-call validator.");
        Assert.That(NativeJit.Execute(program,
                (NativeRuntimePolicy)policy),
            Is.EqualTo(0));
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
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneCallableTypes.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodEffects.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodOutputStates.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodRoots.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodImports.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodImportBindings.terrum",
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

        var supersededBPlusISchema = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(
                    "trrm/1-draft-init-reference-bodies-1",
                    input.Artifacts[0].Artifact),
                input.Artifacts[1],
            ],
        };
        Assert.That(Run(program, supersededBPlusISchema, 3, 0),
            Is.EqualTo(0));

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

        var typedParameters = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateParameterizedArtifact("provider/b",
                        1, TrRmTypeSpecTableWriter.Builtin(0))),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateParameterizedArtifact("provider/a", 1,
                        TrRmTypeSpecTableWriter.InitializationReference(
                            0xF200_0008, isWritable: true))),
            ],
        };
        var typedParametersProgram
            = Compile(typedParameters, 3, 0, verificationMode: 1);
        Assert.That(Run(typedParametersProgram, typedParameters, 3, 0,
                verificationMode: 1),
            Is.EqualTo(0));

        var voidInitializationReference = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateParameterizedArtifact("provider/b", 1,
                        TrRmTypeSpecTableWriter.InitializationReference(
                            0xF200_0000, isWritable: true))),
                input.Artifacts[1],
            ],
        };
        var voidInitializationReferenceProgram
            = Compile(voidInitializationReference, 3, 2);
        Assert.That(Run(voidInitializationReferenceProgram,
                voidInitializationReference, 3, 2),
            Is.EqualTo(0));

        var pointerParameter = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateParameterizedArtifact("provider/b", 1,
                        TrRmTypeSpecTableWriter.Wrapped(0x08,
                            0xF200_0008))),
                input.Artifacts[1],
            ],
        };
        var pointerParameterProgram = Compile(pointerParameter, 3, 2);
        Assert.That(Run(pointerParameterProgram, pointerParameter, 3, 2),
            Is.EqualTo(0));

        var invalidParameterMode = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateParameterizedArtifact("provider/b",
                        0xF200_0008, parameterMode: 1)),
                input.Artifacts[1],
            ],
        };
        var invalidParameterModeProgram
            = Compile(invalidParameterMode, 3, 3);
        Assert.That(Run(invalidParameterModeProgram, invalidParameterMode,
                3, 3),
            Is.EqualTo(0));

        var callerUnsafeAndNoReturn = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateEffectArtifact("provider/b", 6)),
                input.Artifacts[1],
            ],
        };
        var effectsProgram = Compile(callerUnsafeAndNoReturn, 3, 0,
            verificationMode: 2, expectedEffectMask: 6);
        Assert.That(Run(effectsProgram, callerUnsafeAndNoReturn, 3, 0,
                verificationMode: 2, expectedEffectMask: 6),
            Is.EqualTo(0));

        var explicitZeroEffects = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateEffectArtifact("provider/b", 0)),
                input.Artifacts[1],
            ],
        };
        var explicitZeroEffectsProgram
            = Compile(explicitZeroEffects, 3, 0, verificationMode: 2);
        Assert.That(Run(explicitZeroEffectsProgram, explicitZeroEffects,
                3, 0, verificationMode: 2),
            Is.EqualTo(0));

        var publishedOutputState = input with {
            Artifacts = [
                input.Artifacts[0],
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateOutputStateArtifact("provider/a",
                        includeDependency: true)),
            ],
        };
        var publishedOutputStateProgram
            = Compile(publishedOutputState, 3, 0, verificationMode: 3);
        Assert.That(Run(publishedOutputStateProgram, publishedOutputState,
                3, 0, verificationMode: 3),
            Is.EqualTo(0));

        var integerOutputState = input with {
            Artifacts = [
                input.Artifacts[0],
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateOutputStateArtifact("provider/a",
                        includeDependency: true, nodeKind: 3,
                        methodResultTypeSpecRef: 0xF200_0008)),
            ],
        };
        var integerOutputStateProgram
            = Compile(integerOutputState, 3, 0, verificationMode: 3,
                expectedPredicateNodeKind: 3);
        Assert.That(Run(integerOutputStateProgram, integerOutputState,
                3, 0, verificationMode: 3,
                expectedPredicateNodeKind: 3),
            Is.EqualTo(0));

        var duplicateDependency = input with {
            Artifacts = [
                input.Artifacts[0],
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateOutputStateArtifact("provider/a",
                        includeDependency: true,
                        duplicateDependency: true)),
            ],
        };
        var duplicateDependencyProgram
            = Compile(duplicateDependency, 3, 3);
        Assert.That(Run(duplicateDependencyProgram, duplicateDependency,
                3, 3),
            Is.EqualTo(0));

        var conflictingDependencySources = input with {
            Artifacts = [
                input.Artifacts[0],
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateOutputStateArtifact("provider/a",
                        includeDependency: true,
                        conflictEmptyAndSourcedDependency: true)),
            ],
        };
        var conflictingDependencyProgram
            = Compile(conflictingDependencySources, 3, 3);
        Assert.That(Run(conflictingDependencyProgram,
                conflictingDependencySources, 3, 3),
            Is.EqualTo(0));

        var outputStateWithoutFeature = input with {
            Artifacts = [
                input.Artifacts[0],
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateOutputStateArtifact("provider/a",
                        featureId: 0)),
            ],
        };
        var outputStateWithoutFeatureProgram
            = Compile(outputStateWithoutFeature, 3, 3);
        Assert.That(Run(outputStateWithoutFeatureProgram,
                outputStateWithoutFeature, 3, 3),
            Is.EqualTo(0));

        var outputStateWithoutPredicate = input with {
            Artifacts = [
                input.Artifacts[0],
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateOutputStateArtifact("provider/a",
                        includeOutputState: false)),
            ],
        };
        var outputStateWithoutPredicateProgram
            = Compile(outputStateWithoutPredicate, 3, 3);
        Assert.That(Run(outputStateWithoutPredicateProgram,
                outputStateWithoutPredicate, 3, 3),
            Is.EqualTo(0));

        var invalidEffectsMask = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateEffectArtifact("provider/b", 1)),
                input.Artifacts[1],
            ],
        };
        var invalidEffectsProgram = Compile(invalidEffectsMask, 3, 3);
        Assert.That(Run(invalidEffectsProgram, invalidEffectsMask, 3, 3),
            Is.EqualTo(0));

        var duplicateEffects = input with {
            Artifacts = [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateEffectArtifact("provider/b", 2,
                        effectRowCount: 2)),
                input.Artifacts[1],
            ],
        };
        var duplicateEffectsProgram = Compile(duplicateEffects, 3, 3);
        Assert.That(Run(duplicateEffectsProgram, duplicateEffects, 3, 3),
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

        var imports = new StandaloneMethodImportInput(Agreement,
            "consumer/module", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodImporterArtifact(
                        "provider/consumer-a", "provider/leaf")),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodImporterArtifact(
                        "provider/consumer-b", "provider/leaf",
                        effectsMask: 4)),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMinimalArtifact("provider/leaf")),
            ], [
                new StandaloneMethodRoot(2, "provider/leaf", "api/Run"),
            ]);
        var importsProgram = Compile(imports, 4, 0, verificationMode: 4);
        Assert.That(Run(importsProgram, imports, 4, 0,
                verificationMode: 4),
            Is.EqualTo(0));

        var sharedExpectedAliases = new StandaloneMethodImportInput(
            Agreement, "consumer/module", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodImporterArtifact(
                        "provider/consumer", "provider/leaf",
                        sharedExpectedAliases: true)),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateAliasedProviderArtifact("provider/leaf",
                        "api/Run", "api/RunAlias")),
            ], [
                new StandaloneMethodRoot(1, "provider/leaf", "api/Run"),
            ]);
        var sharedAliasesProgram
            = Compile(sharedExpectedAliases, 3, 0,
                verificationMode: 10);
        Assert.That(Run(sharedAliasesProgram, sharedExpectedAliases, 3, 0,
                verificationMode: 10),
            Is.EqualTo(0));

        var missingProvider = new StandaloneMethodImportInput(Agreement,
            "consumer/module", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodImporterArtifact(
                        "provider/consumer", "provider/missing")),
            ], []);
        var missingProviderProgram
            = Compile(missingProvider, 2, 1, verificationMode: 5);
        Assert.That(Run(missingProviderProgram, missingProvider, 2, 1,
                verificationMode: 5),
            Is.EqualTo(0));

        var unsupportedImport = new StandaloneMethodImportInput(Agreement,
            "consumer/module", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodImporterArtifact(
                        "provider/consumer", "provider/leaf",
                        methodFlags: 0)),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMinimalArtifact("provider/leaf")),
            ], []);
        var unsupportedImportProgram
            = Compile(unsupportedImport, 3, 2, verificationMode: 6);
        Assert.That(Run(unsupportedImportProgram, unsupportedImport, 3, 2,
                verificationMode: 6),
            Is.EqualTo(0));

        var bodyfulImport = new StandaloneMethodImportInput(Agreement,
            "consumer/module", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodImporterArtifact(
                        "provider/consumer", "provider/leaf",
                        bodyRow: 1)),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMinimalArtifact("provider/leaf")),
            ], []);
        var bodyfulImportProgram
            = Compile(bodyfulImport, 3, 3, verificationMode: 7);
        Assert.That(Run(bodyfulImportProgram, bodyfulImport, 3, 3,
                verificationMode: 7),
            Is.EqualTo(0));

        var unsupportedRequirement = new StandaloneMethodImportInput(
            Agreement, "consumer/module", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodImporterArtifact(
                        "provider/consumer", "provider/leaf",
                        moduleRefTargetRequirement: 0)),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMinimalArtifact("provider/leaf")),
            ], []);
        var unsupportedRequirementProgram
            = Compile(unsupportedRequirement, 3, 2, verificationMode: 8);
        Assert.That(Run(unsupportedRequirementProgram,
                unsupportedRequirement, 3, 2, verificationMode: 8),
            Is.EqualTo(0));

        var mismatchedOwner = new StandaloneMethodImportInput(Agreement,
            "consumer/module", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodImporterArtifact(
                        "provider/consumer", "provider/leaf",
                        methodOwnedBySecondModule: true)),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMinimalArtifact("provider/leaf")),
            ], []);
        var mismatchedOwnerProgram
            = Compile(mismatchedOwner, 4, 3, verificationMode: 9);
        Assert.That(Run(mismatchedOwnerProgram, mismatchedOwner, 4, 3,
                verificationMode: 9),
            Is.EqualTo(0));

        var incompatibleSignature = new StandaloneMethodImportInput(
            Agreement, "consumer/module", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodImporterArtifact(
                        "provider/consumer", "provider/leaf")),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodProviderArtifact("provider/leaf",
                        "api/Run", resultTypeSpecRef: 0xF200_0001)),
            ], []);
        var incompatibleSignatureProgram
            = Compile(incompatibleSignature, 3, 1, verificationMode: 11);
        Assert.That(Run(incompatibleSignatureProgram,
                incompatibleSignature, 3, 1, verificationMode: 11),
            Is.EqualTo(0));

        var unsafeReplacement = new StandaloneMethodImportInput(Agreement,
            "consumer/module", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodImporterArtifact(
                        "provider/consumer", "provider/leaf")),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodProviderArtifact("provider/leaf",
                        "api/Run", effectsMask: 4)),
            ], []);
        var unsafeReplacementProgram
            = Compile(unsafeReplacement, 3, 1, verificationMode: 12);
        Assert.That(Run(unsafeReplacementProgram, unsafeReplacement, 3, 1,
                verificationMode: 12),
            Is.EqualTo(0));

        var conflictingAliases = new StandaloneMethodImportInput(
            Agreement, "consumer/module", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodImporterArtifact(
                        "provider/consumer", "provider/leaf",
                        sharedExpectedAliases: true)),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateConflictingAliasedProviderArtifact(
                        "provider/leaf")),
            ], [
                new StandaloneMethodRoot(1, "provider/leaf", "api/Run"),
            ]);
        var conflictingAliasesProgram
            = Compile(conflictingAliases, 3, 1, verificationMode: 13);
        Assert.That(Run(conflictingAliasesProgram, conflictingAliases, 3, 1,
                verificationMode: 13),
            Is.EqualTo(0));

        var missingExport = new StandaloneMethodImportInput(Agreement,
            "consumer/module", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMethodImporterArtifact(
                        "provider/consumer", "provider/leaf",
                        exportKey: "api/Missing")),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateMinimalArtifact("provider/leaf")),
            ], []);
        var missingExportProgram
            = Compile(missingExport, 3, 1, verificationMode: 14);
        Assert.That(Run(missingExportProgram, missingExport, 3, 1,
                verificationMode: 14),
            Is.EqualTo(0));

        BoundProgram Compile(StandaloneMethodImportInput request,
            int expectedModuleCount, int expectedRootStatus,
            int verificationMode = 0, int expectedEffectMask = 0,
            int expectedPredicateNodeKind = 1)
        {
            var compilation = Compilation.Create(components,
                Buffers(request, expectedModuleCount,
                    expectedRootStatus, verificationMode,
                    expectedEffectMask, expectedPredicateNodeKind), policy);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            return compilation.Program
                ?? throw new AssertionException(
                    "Expected a bound validator.");
        }

        int Run(BoundProgram boundProgram,
            StandaloneMethodImportInput request, int expectedModuleCount,
            int expectedRootStatus, int verificationMode = 0,
            int expectedEffectMask = 0, int expectedPredicateNodeKind = 1)
            => NativeJit.Execute(boundProgram with {
                Buffers = Buffers(request, expectedModuleCount,
                    expectedRootStatus, verificationMode,
                    expectedEffectMask, expectedPredicateNodeKind),
            }, (NativeRuntimePolicy)policy);

        ImmutableArray<HostBuffer> Buffers(
            StandaloneMethodImportInput request, int expectedModuleCount,
            int expectedRootStatus, int verificationMode,
            int expectedEffectMask, int expectedPredicateNodeKind)
            => request.Capture().CreateHostBuffers().Add(new HostBuffer(
                "Expected", PrimitiveType.Int32,
                [expectedModuleCount, expectedRootStatus,
                    verificationMode, expectedEffectMask,
                    expectedPredicateNodeKind], false));
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..",
            "..", "..", "..", ".."));

    private static void AssertStandaloneWrittenArtifact(byte[] artifact)
    {
        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x0003, out var blobs,
                out var blobCount), Is.True);
        Assert.That(blobCount, Is.EqualTo(1));
        Assert.That(index.TryGetSection(0x0004, out var features,
                out var featureCount), Is.True);
        Assert.That(featureCount, Is.EqualTo(1));
        Assert.That(index.TryGetSection(0x001C, out _, out var importCount),
            Is.True);
        Assert.That(importCount, Is.EqualTo(1));
        Assert.That(index.TryGetSection(0x0020, out var bodySection,
                out var bodyCount), Is.True);
        Assert.That(bodyCount, Is.EqualTo(1));
        Assert.That(index.TryGetSection(0x0030, out _,
                out var constantSourceCount), Is.True);
        Assert.That(constantSourceCount, Is.EqualTo(1));
        Assert.That(index.TryGetSection(0x003F, out _,
                out var frozenValueNodeCount), Is.True);
        Assert.That(frozenValueNodeCount, Is.EqualTo(1));

        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                features.Slice(0, sizeof(uint))), Is.EqualTo(11));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                features.Slice(sizeof(uint), sizeof(uint))), Is.EqualTo(1));

        var bodies = new TrRmBodyTableReader(bodySection, bodyCount);
        var bodyReader = new TrRmRegisterBodyReader(bodies.GetRow(1));
        Assert.That(bodyReader.Code[0], Is.EqualTo(0x05));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                bodyReader.MethodMap), Is.EqualTo(0x1C00_0001));
        Assert.That(bodyReader.RecordCount, Is.EqualTo(4));
        var firstCall = bodyReader.GetRecord(1);
        Assert.That(firstCall[0], Is.EqualTo(0x02));
        Assert.That(firstCall[4], Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                firstCall.Slice(4 + 4, sizeof(ushort))),
            Is.EqualTo(0));
        Assert.That(blobs.Length, Is.GreaterThan(0));
    }

    private static void AssertStandaloneOutputStateContractArtifact(
        byte[] artifact)
    {
        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x0001, out var modules,
                out var moduleCount), Is.True);
        Assert.That(moduleCount, Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                modules.Slice(24, sizeof(uint))), Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                modules.Slice(28, sizeof(uint))), Is.EqualTo(1));

        Assert.That(index.TryGetSection(0x0004, out var features,
                out var featureCount), Is.True);
        Assert.That(featureCount, Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                features.Slice(0, sizeof(uint))), Is.EqualTo(10));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                features.Slice(sizeof(uint), sizeof(uint))), Is.EqualTo(1));

        Assert.That(index.TryGetSection(0x0016, out var methodDefs,
                out var methodCount), Is.True);
        Assert.That(methodCount, Is.EqualTo(3));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                methodDefs.Slice(28 + 16, sizeof(uint))), Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                methodDefs.Slice(28 + 20, sizeof(uint))), Is.EqualTo(3));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                methodDefs.Slice(56 + 16, sizeof(uint))), Is.EqualTo(4));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                methodDefs.Slice(56 + 20, sizeof(uint))), Is.EqualTo(3));
        Assert.That(index.TryGetSection(0x0018, out _, out var paramCount),
            Is.True);
        Assert.That(paramCount, Is.EqualTo(5));

        Assert.That(index.TryGetSection(0x001B, out var contracts,
                out var contractCount), Is.True);
        Assert.That(contractCount, Is.EqualTo(6));
        Assert.That(contracts.Length, Is.EqualTo(148));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                contracts.Slice(0, sizeof(uint))), Is.EqualTo(0));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                contracts.Slice(4, sizeof(uint))), Is.EqualTo(20));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                contracts.Slice(8, sizeof(uint))), Is.EqualTo(40));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                contracts.Slice(12, sizeof(uint))), Is.EqualTo(60));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                contracts.Slice(16, sizeof(uint))), Is.EqualTo(80));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                contracts.Slice(20, sizeof(uint))), Is.EqualTo(100));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                contracts.Slice(24, sizeof(uint))), Is.EqualTo(120));

        var predicateClause = contracts.Slice(28, 20);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(predicateClause),
            Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                predicateClause.Slice(4, sizeof(ushort))),
            Is.EqualTo(0x000B));
        Assert.That(predicateClause[8], Is.EqualTo(3));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                predicateClause.Slice(12, sizeof(uint))), Is.EqualTo(0));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                predicateClause.Slice(16, sizeof(uint))), Is.EqualTo(1));

        var outputStateClause = contracts.Slice(48, 20);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(outputStateClause),
            Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                outputStateClause.Slice(4, sizeof(ushort))),
            Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                outputStateClause.Slice(8, sizeof(uint))), Is.EqualTo(0));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                outputStateClause.Slice(12, sizeof(uint))), Is.EqualTo(1));
        Assert.That(outputStateClause[16], Is.EqualTo(1));

        var dependencyClause = contracts.Slice(68, 20);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(dependencyClause),
            Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                dependencyClause.Slice(4, sizeof(ushort))),
            Is.EqualTo(4));
        Assert.That(dependencyClause[8], Is.EqualTo(2));
        Assert.That(dependencyClause[9], Is.EqualTo(1));
        Assert.That(dependencyClause[10], Is.EqualTo(1));
        Assert.That(dependencyClause[11], Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                dependencyClause.Slice(12, sizeof(uint))), Is.EqualTo(0));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                dependencyClause.Slice(16, sizeof(uint))), Is.EqualTo(1));
        var secondPredicateClause = contracts.Slice(88, 20);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                secondPredicateClause), Is.EqualTo(3));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                secondPredicateClause.Slice(16, sizeof(uint))),
            Is.EqualTo(2));
        var secondOutputStateClause = contracts.Slice(108, 20);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                secondOutputStateClause), Is.EqualTo(3));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                secondOutputStateClause.Slice(12, sizeof(uint))),
            Is.EqualTo(2));
        var secondDependencyClause = contracts.Slice(128, 20);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                secondDependencyClause), Is.EqualTo(3));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                secondDependencyClause.Slice(4, sizeof(ushort))),
            Is.EqualTo(4));

        Assert.That(index.TryGetSection(0x0037, out var graphs,
                out var graphCount), Is.True);
        Assert.That(graphCount, Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                graphs.Slice(0, sizeof(uint))), Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                graphs.Slice(4, sizeof(uint))), Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                graphs.Slice(8, sizeof(uint))), Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                graphs.Slice(12, sizeof(uint))), Is.EqualTo(2));
        Assert.That(index.TryGetSection(0x0038, out var footprints,
                out var footprintCount), Is.True);
        Assert.That(footprintCount, Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                footprints.Slice(0, sizeof(uint))), Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                footprints.Slice(4, sizeof(uint))), Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                footprints.Slice(8, sizeof(uint))), Is.EqualTo(0));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                footprints.Slice(12, sizeof(uint))), Is.EqualTo(0));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                footprints.Slice(16, sizeof(uint))), Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                footprints.Slice(20, sizeof(uint))), Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                footprints.Slice(24, sizeof(uint))), Is.EqualTo(0));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                footprints.Slice(28, sizeof(uint))), Is.EqualTo(0));
        Assert.That(index.TryGetSection(0x0039, out var roots,
                out var rootCount), Is.True);
        Assert.That(rootCount, Is.EqualTo(2));
        Assert.That(roots[0], Is.EqualTo(5));
        Assert.That(roots[1], Is.EqualTo(0));
        Assert.That(roots[8], Is.EqualTo(5));
        Assert.That(roots[9], Is.EqualTo(0));
        Assert.That(index.TryGetSection(0x003B, out var nodes,
                out var nodeCount), Is.True);
        Assert.That(nodeCount, Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                nodes.Slice(0, sizeof(uint))), Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                nodes.Slice(4, sizeof(uint))), Is.EqualTo(0xF200_0001));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                nodes.Slice(8, sizeof(uint))), Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                nodes.Slice(12, sizeof(uint))), Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                nodes.Slice(16, sizeof(uint))), Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                nodes.Slice(20, sizeof(uint))), Is.EqualTo(0xF200_0001));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                nodes.Slice(24, sizeof(uint))), Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                nodes.Slice(28, sizeof(uint))), Is.EqualTo(1));
        Assert.That(index.TryGetSection(0x003C, out var operands,
                out var operandCount), Is.True);
        Assert.That(operandCount, Is.EqualTo(2));
        Assert.That(operands[0], Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                operands.Slice(4, sizeof(uint))), Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                operands.Slice(12, sizeof(uint))), Is.EqualTo(2));
    }

    private static void AssertStandaloneAliasesShareExpectedMethod(
        byte[] artifact)
    {
        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x0016, out var methodDefs,
                out var methodCount), Is.True);
        Assert.That(methodCount, Is.EqualTo(2));
        Assert.That(index.TryGetSection(0x001C, out var imports,
                out var importCount), Is.True);
        Assert.That(importCount, Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                imports.Slice(8, sizeof(uint))),
            Is.EqualTo(0x1600_0002));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                imports.Slice(12 + 8, sizeof(uint))),
            Is.EqualTo(0x1600_0002));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                imports.Slice(4, sizeof(uint))),
            Is.Not.EqualTo(BinaryPrimitives.ReadUInt32LittleEndian(
                imports.Slice(12 + 4, sizeof(uint)))));

        Assert.That(index.TryGetSection(0x0020, out var bodySection,
                out var bodyCount), Is.True);
        Assert.That(bodyCount, Is.EqualTo(1));
        var bodyTable = new TrRmBodyTableReader(bodySection, bodyCount);
        var body = new TrRmRegisterBodyReader(bodyTable.GetRow(1));
        Assert.That(body.MethodMap.Length, Is.EqualTo(2 * sizeof(uint)));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                body.MethodMap), Is.EqualTo(0x1C00_0001));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                body.MethodMap.Slice(sizeof(uint))), Is.EqualTo(0x1C00_0002));
        Assert.That(body.RecordCount, Is.EqualTo(3));
        Assert.That(methodDefs.Length, Is.EqualTo(2 * 28));
    }

    private static byte[] CreateAliasedProviderArtifact(
        string moduleIdentity, string firstExportKey,
        string secondExportKey)
    {
        var stringSection = TrRmStringTableWriter.EncodeNames([
            (moduleIdentity, 0),
            ("Implementation", 0),
            (firstExportKey, 0),
            (secondExportKey, 0),
        ]);
        var strings = new TrRmStringTableReader(stringSection, 4);
        var moduleSection = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 0, 1, 0, 0);
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
        BinaryPrimitives.WriteUInt32LittleEndian(
            exportSection, 0x1600_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(
            exportSection.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(
            exportSection.AsSpan(8), 0x1600_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(
            exportSection.AsSpan(12), 4);
        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleSection),
            new TrRmSection(0x0002, 0, 4, stringSection),
            new TrRmSection(0x0016, 0, 1, methodDefSection),
            new TrRmSection(0x0017, 0, 1, methodSigSection),
            new TrRmSection(0x001D, 0, 2, exportSection),
        ]);
    }

    private static byte[] CreateConflictingAliasedProviderArtifact(
        string moduleIdentity)
    {
        var stringSection = TrRmStringTableWriter.EncodeNames([
            (moduleIdentity, 0),
            ("Implementation1", 0),
            ("api/Run", 0),
            ("api/RunAlias", 0),
            ("Implementation2", 0),
        ]);
        var strings = new TrRmStringTableReader(stringSection, 5);
        var moduleSection = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 0, 2, 0, 0);
        var methodDefSection = new byte[56];
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(32), 5);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(36), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(40), 2);
        var methodSigSection = new byte[48];
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodSigSection, 0xF200_0008);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodSigSection.AsSpan(24), 0xF200_0008);
        var exportSection = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(
            exportSection, 0x1600_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(
            exportSection.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(
            exportSection.AsSpan(8), 0x1600_0002);
        BinaryPrimitives.WriteUInt32LittleEndian(
            exportSection.AsSpan(12), 4);
        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleSection),
            new TrRmSection(0x0002, 0, 5, stringSection),
            new TrRmSection(0x0016, 0, 2, methodDefSection),
            new TrRmSection(0x0017, 0, 2, methodSigSection),
            new TrRmSection(0x001D, 0, 2, exportSection),
        ]);
    }

    private static byte[] CreateMethodProviderArtifact(
        string moduleIdentity, string exportKey,
        uint resultTypeSpecRef = 0xF200_0008, uint effectsMask = 0,
        bool scalarParameter = false,
        uint parameterTypeSpecRef = 0xF200_0008,
        byte[]? typeSpecSection = null)
    {
        var stringSection = TrRmStringTableWriter.EncodeNames([
            (moduleIdentity, 0),
            ("Implementation", 0),
            (exportKey, 0),
        ]);
        var strings = new TrRmStringTableReader(stringSection, 3);
        var moduleSection = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 0, 1, 0, 0);
        var methodDefSection = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(12), 2);
        if (effectsMask != 0)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodDefSection.AsSpan(16), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodDefSection.AsSpan(20), 1);
        }
        var methodSigSection = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodSigSection, resultTypeSpecRef);
        byte[]? paramSection = null;
        if (scalarParameter)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodSigSection.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodSigSection.AsSpan(12), 1);
            paramSection = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(
                paramSection, 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                paramSection.AsSpan(4), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(
                paramSection.AsSpan(8), parameterTypeSpecRef);
        }
        var exportSection = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(
            exportSection, 0x1600_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(
            exportSection.AsSpan(4), 3);
        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, moduleSection),
            new(0x0002, 0, 3, stringSection),
            new(0x0016, 0, 1, methodDefSection),
            new(0x0017, 0, 1, methodSigSection),
            new(0x001D, 0, 1, exportSection),
        };
        if (paramSection is not null)
        {
            sections.Add(new TrRmSection(0x0018, 0, 1,
                paramSection));
        }
        if (typeSpecSection is not null)
        {
            sections.Add(new TrRmSection(0x0011, 0, 1,
                typeSpecSection));
        }
        if (effectsMask != 0)
        {
            var effectsRow = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(
                effectsRow, 1);
            BinaryPrimitives.WriteUInt16LittleEndian(
                effectsRow.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                effectsRow.AsSpan(8), effectsMask);
            sections.Add(new TrRmSection(0x001B, 0, 1,
                TrRmVariableTableWriter.EncodeBlobs([effectsRow])));
        }
        sections.Sort(static (left, right)
            => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections);
    }

    private static byte[] CreateMethodImporterArtifact(
        string moduleIdentity, string producerIdentity,
        string exportKey = "api/Run", uint methodFlags = 2,
        uint bodyRow = 0, uint moduleRefTargetRequirement = 1,
        bool methodOwnedBySecondModule = false, uint effectsMask = 0,
        bool sharedExpectedAliases = false,
        bool scalarParameter = false)
    {
        var stringSection = TrRmStringTableWriter.EncodeNames([
            (moduleIdentity, 0),
            ("Expected", 0),
            (exportKey, 0),
            (producerIdentity, 0),
            (moduleIdentity + "/other", 0),
            ("api/RunAlias", 0),
        ]);
        var strings = new TrRmStringTableReader(stringSection, 6);
        var moduleRefCount = sharedExpectedAliases ? 2 : 1;
        var moduleRows = methodOwnedBySecondModule
            ? new[] {
                new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1),
                new TrRmModuleRow(5, 1, 0, 0,
                    moduleRefCount + 1, 1, 1, 1),
            }
            : new[] {
                new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1),
            };
        var moduleSection = TrRmModuleTableWriter.Encode(moduleRows,
            strings, moduleRefCount, 0, 1, 0, 0);
        var modules = new TrRmModuleTableReader(moduleSection,
            moduleRows.Length,
            strings, moduleRefCount, 0, 1, 0, 0);
        var moduleRefRows = sharedExpectedAliases
            ? new[] {
                new TrRmModuleRefRow(4, moduleRefTargetRequirement, 0),
                new TrRmModuleRefRow(4, moduleRefTargetRequirement, 0),
            }
            : new[] {
                new TrRmModuleRefRow(4, moduleRefTargetRequirement, 0),
            };
        var moduleRefSection = TrRmModuleRefTableWriter.Encode(
            moduleRefRows, strings, modules);

        var methodDefSection = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(12), methodFlags);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(24), bodyRow);
        if (effectsMask != 0)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodDefSection.AsSpan(16), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodDefSection.AsSpan(20), 1);
        }
        var methodSigSection = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodSigSection, 0xF200_0008);
        byte[]? paramSection = null;
        if (scalarParameter)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodSigSection.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodSigSection.AsSpan(12), 1);
            paramSection = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(
                paramSection, 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                paramSection.AsSpan(4), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(
                paramSection.AsSpan(8), 0xF200_0008);
        }
        var importCount = sharedExpectedAliases ? 2 : 1;
        var importSection = new byte[importCount * 12];
        for (var index = 0; index < importCount; index++)
        {
            var offset = index * 12;
            BinaryPrimitives.WriteUInt32LittleEndian(
                importSection.AsSpan(offset), (uint)(index + 1));
            BinaryPrimitives.WriteUInt32LittleEndian(
                importSection.AsSpan(offset + 4),
                sharedExpectedAliases && index == 1 ? 6u : 3u);
            BinaryPrimitives.WriteUInt32LittleEndian(
                importSection.AsSpan(offset + 8), 0x1600_0001);
        }

        var sections = new List<TrRmSection> {
            new(0x0001, 0, (uint)moduleRows.Length, moduleSection),
            new(0x0002, 0, 6, stringSection),
            new(0x0010, 0, (uint)moduleRefCount, moduleRefSection),
            new TrRmSection(0x0016, 0, 1, methodDefSection),
            new TrRmSection(0x0017, 0, 1, methodSigSection),
            new(0x001C, 0, (uint)importCount, importSection),
        };
        if (paramSection is not null)
        {
            sections.Add(new TrRmSection(0x0018, 0, 1,
                paramSection));
        }
        if (effectsMask != 0)
        {
            var effectsRow = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(
                effectsRow, 1);
            BinaryPrimitives.WriteUInt16LittleEndian(
                effectsRow.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                effectsRow.AsSpan(8), effectsMask);
            sections.Add(new TrRmSection(0x001B, 0, 1,
                TrRmVariableTableWriter.EncodeBlobs([effectsRow])));
        }
        return TrRmArtifactWriter.Encode(sections);
    }

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

    private static byte[] CreateParameterizedArtifact(string moduleIdentity,
        uint parameterTypeSpecRef, byte[]? typeSpecRow = null,
        byte parameterMode = 0)
    {
        ReadOnlyMemory<byte>[] typeSpecRows = typeSpecRow is null
            ? []
            : [new ReadOnlyMemory<byte>(typeSpecRow)];
        byte[] typeSpecSection = typeSpecRows.Length == 0
            ? Array.Empty<byte>()
            : TrRmTypeSpecTableWriter.Encode(typeSpecRows, 0, 0, 0);
        var stringSection = TrRmStringTableWriter.EncodeNames([
            (moduleIdentity, 0),
            ("Implementation", 0),
            ("api/Run", 0),
        ]);
        var strings = new TrRmStringTableReader(stringSection, 3);
        var moduleSection = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 0, 1, 0);
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
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodSigSection.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodSigSection.AsSpan(12), 1);
        var paramSection = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(paramSection, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            paramSection.AsSpan(8), parameterTypeSpecRef);
        paramSection[12] = parameterMode;
        var exportSection = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(exportSection,
            0x1600_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(
            exportSection.AsSpan(4), 3);

        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, moduleSection),
            new(0x0002, 0, 3, stringSection),
        };
        if (typeSpecRows.Length != 0)
        {
            sections.Add(new TrRmSection(0x0003, 0, 1,
                typeSpecSection));
        }
        sections.Add(new TrRmSection(0x0016, 0, 1,
            methodDefSection));
        sections.Add(new TrRmSection(0x0017, 0, 1,
            methodSigSection));
        sections.Add(new TrRmSection(0x0018, 0, 1, paramSection));
        sections.Add(new TrRmSection(0x001D, 0, 1, exportSection));
        return TrRmArtifactWriter.Encode(sections);
    }

    private static byte[] CreateEffectArtifact(string moduleIdentity,
        uint effectsMask, int effectRowCount = 1)
    {
        var stringSection = TrRmStringTableWriter.EncodeNames([
            (moduleIdentity, 0),
            ("Implementation", 0),
            ("api/Run", 0),
        ]);
        var strings = new TrRmStringTableReader(stringSection, 3);
        var moduleSection = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 0, 1, 0);
        var methodDefSection = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(20), (uint)effectRowCount);
        var methodSigSection = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodSigSection, 0xF200_0008);
        var contractRows = new ReadOnlyMemory<byte>[effectRowCount];
        for (var index = 0; index < effectRowCount; index++)
        {
            var contractRow = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(contractRow, 1);
            BinaryPrimitives.WriteUInt16LittleEndian(
                contractRow.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                contractRow.AsSpan(8), effectsMask);
            contractRows[index] = contractRow;
        }
        var contractSection
            = TrRmVariableTableWriter.EncodeBlobs(contractRows);
        var exportSection = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(exportSection,
            0x1600_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(
            exportSection.AsSpan(4), 3);
        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleSection),
            new TrRmSection(0x0002, 0, 3, stringSection),
            new TrRmSection(0x0016, 0, 1, methodDefSection),
            new TrRmSection(0x0017, 0, 1, methodSigSection),
            new TrRmSection(0x001B, 0, (uint)effectRowCount,
                contractSection),
            new TrRmSection(0x001D, 0, 1, exportSection),
        ]);
    }

    private static byte[] CreateOutputStateArtifact(string moduleIdentity,
        uint featureId = 10, bool includeOutputState = true,
        byte outputState = 1, bool includeDependency = false,
        bool duplicateDependency = false,
        bool conflictEmptyAndSourcedDependency = false,
        bool sourcedDependency = false,
        uint nodeKind = 1, uint methodResultTypeSpecRef = 0xF200_0001,
        uint predicateGraphRow = 1, string exportKey = "api/Run")
    {
        var typeSpecRows = new ReadOnlyMemory<byte>[] {
            TrRmTypeSpecTableWriter.InitializationReference(
                0xF200_0008, isWritable: true),
        };
        var typeSpecSection = TrRmTypeSpecTableWriter.Encode(
            typeSpecRows, 0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecSection,
            1, 0, 0, 0);
        var methodParameterCount = sourcedDependency ? 2 : 1;
        var methodSigSection = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(methodResultTypeSpecRef, 0, 1,
                (uint)methodParameterCount, 0, 0),
        ], typeSpecs, 1, 0);
        var signatures = new TrRmMethodSigTableReader(
            methodSigSection, 1, typeSpecs, methodParameterCount, 0);
        var parameterRows
            = new TrRmParamRow[methodParameterCount];
        for (var ordinal = 0; ordinal < methodParameterCount; ordinal++)
        {
            parameterRows[ordinal]
                = new TrRmParamRow(1, (uint)ordinal, 1, 0);
        }
        var paramSection = TrRmParamTableWriter.Encode(
            parameterRows, signatures, typeSpecs);
        var stringSection = TrRmStringTableWriter.EncodeNames([
            (moduleIdentity, 0),
            ("Implementation", 0),
            (exportKey, 0),
        ]);
        var strings = new TrRmStringTableReader(stringSection, 3);
        var featureIds = featureId == 10
            ? new uint[] { 10 }
            : featureId == 0 ? Array.Empty<uint>()
            : new uint[] { featureId };
        var featureCount = featureIds.Length;
        var moduleSection = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1,
                featureCount == 0 ? 0 : 1, featureCount),
        ], strings, 0, 0, 1, 0, featureCount);

        var methodDefSection = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(16), 1);
        var dependencyRowCount = !includeDependency ? 0
            : duplicateDependency || conflictEmptyAndSourcedDependency ? 2 : 1;
        var contractRowCount = (includeOutputState ? 2 : 1)
            + dependencyRowCount;
        BinaryPrimitives.WriteUInt32LittleEndian(
            methodDefSection.AsSpan(20), (uint)contractRowCount);

        var predicateGraphCount = checked((int)predicateGraphRow);
        var predicateGraphSection
            = new byte[predicateGraphCount * 8];
        var readFootprintSection
            = new byte[predicateGraphCount * 16];
        var readRootSection = new byte[predicateGraphCount * 8];
        var predicateNodeSection
            = new byte[predicateGraphCount * 16];
        var predicateOperandSection
            = new byte[predicateGraphCount * 8];
        for (var index = 0; index < predicateGraphCount; index++)
        {
            var row = checked((uint)index + 1);
            var graphOffset = index * 8;
            BinaryPrimitives.WriteUInt32LittleEndian(
                predicateGraphSection.AsSpan(graphOffset), row);
            BinaryPrimitives.WriteUInt32LittleEndian(
                predicateGraphSection.AsSpan(graphOffset + 4), row);

            var footprintOffset = index * 16;
            BinaryPrimitives.WriteUInt32LittleEndian(
                readFootprintSection.AsSpan(footprintOffset), row);
            BinaryPrimitives.WriteUInt32LittleEndian(
                readFootprintSection.AsSpan(footprintOffset + 4), 1);

            var rootOffset = index * 8;
            readRootSection[rootOffset] = 5;

            var nodeOffset = index * 16;
            BinaryPrimitives.WriteUInt32LittleEndian(
                predicateNodeSection.AsSpan(nodeOffset),
                row == predicateGraphRow ? nodeKind : 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                predicateNodeSection.AsSpan(nodeOffset + 4),
                0xF200_0001);
            BinaryPrimitives.WriteUInt32LittleEndian(
                predicateNodeSection.AsSpan(nodeOffset + 8), row);
            BinaryPrimitives.WriteUInt32LittleEndian(
                predicateNodeSection.AsSpan(nodeOffset + 12), 1);

            var operandOffset = index * 8;
            predicateOperandSection[operandOffset] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(
                predicateOperandSection.AsSpan(operandOffset + 4), row);
        }
        const uint methodDefinition = 0x1600_0001;
        const uint contractOwner = 1;
        var contractRows = new ReadOnlyMemory<byte>[contractRowCount];
        var predicateAttachment = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(
            predicateAttachment, contractOwner);
        BinaryPrimitives.WriteUInt16LittleEndian(
            predicateAttachment.AsSpan(4), 0x000B);
        predicateAttachment[8] = 3;
        BinaryPrimitives.WriteUInt32LittleEndian(
            predicateAttachment.AsSpan(16), predicateGraphRow);
        contractRows[0] = predicateAttachment;
        var nextContractRow = 1;
        if (includeOutputState)
        {
            var outputStateRow = new byte[20];
            BinaryPrimitives.WriteUInt32LittleEndian(
                outputStateRow, contractOwner);
            BinaryPrimitives.WriteUInt16LittleEndian(
                outputStateRow.AsSpan(4), 0x0002);
            BinaryPrimitives.WriteUInt32LittleEndian(
                outputStateRow.AsSpan(12), predicateGraphRow);
            outputStateRow[16] = outputState;
            contractRows[nextContractRow] = outputStateRow;
            nextContractRow = nextContractRow + 1;
        }
        if (includeDependency)
        {
            for (var dependencyIndex = 0;
                dependencyIndex < dependencyRowCount;
                dependencyIndex++)
            {
                var dependencyRow = new byte[20];
                BinaryPrimitives.WriteUInt32LittleEndian(
                    dependencyRow, contractOwner);
                BinaryPrimitives.WriteUInt16LittleEndian(
                    dependencyRow.AsSpan(4), 0x0004);
                dependencyRow[8] = 2;
                dependencyRow[9]
                    = (sourcedDependency && dependencyIndex == 0)
                        || (conflictEmptyAndSourcedDependency
                            && dependencyIndex == 1)
                        ? (byte)1 : (byte)0;
                dependencyRow[10] = dependencyRow[9];
                dependencyRow[11] = 1;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    dependencyRow.AsSpan(12), 0);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    dependencyRow.AsSpan(16),
                    sourcedDependency ? 1u : 0u);
                contractRows[nextContractRow] = dependencyRow;
                nextContractRow = nextContractRow + 1;
            }
        }
        var contractSection
            = TrRmVariableTableWriter.EncodeBlobs(contractRows);
        var exportSection = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(
            exportSection, methodDefinition);
        BinaryPrimitives.WriteUInt32LittleEndian(
            exportSection.AsSpan(4), 3);
        var sections = new List<TrRmSection> {
            new(0x0001, 0, 1, moduleSection),
            new(0x0002, 0, 3, stringSection),
            new(0x0003, 0, 1, typeSpecSection),
        };
        if (featureCount != 0)
        {
            var featureSection = new byte[featureCount * 8];
            for (var index = 0; index < featureCount; index++)
            {
                var rowOffset = index * 8;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    featureSection.AsSpan(rowOffset), featureIds[index]);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    featureSection.AsSpan(rowOffset + 4), 1);
            }
            sections.Add(new TrRmSection(0x0004, 0,
                (uint)featureCount, featureSection));
        }
        sections.Add(new TrRmSection(0x0016, 0, 1, methodDefSection));
        sections.Add(new TrRmSection(0x0017, 0, 1, methodSigSection));
        sections.Add(new TrRmSection(0x0018, 0, 1, paramSection));
        sections.Add(new TrRmSection(0x001B, 0,
            (uint)contractRowCount, contractSection));
        sections.Add(new TrRmSection(0x001D, 0, 1, exportSection));
        sections.Add(new TrRmSection(0x0037, 0,
            (uint)predicateGraphCount,
            predicateGraphSection));
        sections.Add(new TrRmSection(0x0038, 0,
            (uint)predicateGraphCount,
            readFootprintSection));
        sections.Add(new TrRmSection(0x0039, 0,
            (uint)predicateGraphCount, readRootSection));
        sections.Add(new TrRmSection(0x003B, 0,
            (uint)predicateGraphCount,
            predicateNodeSection));
        sections.Add(new TrRmSection(0x003C, 0,
            (uint)predicateGraphCount,
            predicateOperandSection));
        return TrRmArtifactWriter.Encode(sections);
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
