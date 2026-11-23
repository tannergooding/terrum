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
        = StandaloneMethodImportOutputFramer
            .GeneralPredicateGraphsWitnessProjectionsSchemaIdentity;
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

    [TestCase(BoundRuntimePolicy.FailFast,
        "using api; int32 Main(int32 value) { "
            + "_ = Imported(Imported(value)); return Imported(7); }",
        0, 0, false)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions,
        "using api; int32 Main(int32 value) { "
            + "_ = Imported(Imported(value)); return Imported(7); }",
        0, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { using api; namespace inner.deep { "
            + "int32 Main(int32 value) { _ = Imported(Imported(value)); "
            + "return Imported(7); } } }", 0, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client; using api; int32 Main(int32 value) { "
            + "_ = Imported(Imported(value)); return Imported(7); }",
        0, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(int32 value) { _ = api.Imported("
            + "api.Imported(value)); return api.Imported(7); } }", 0, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { using api; int32 Allowed(int32 value) { "
            + "_ = Imported(value); return value; } } namespace client { "
            + "int32 Denied(int32 value) { Imported(value); return value; } }",
        3, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace outer { int32 Helper() { return 0; } } "
            + "namespace client; int32 Main(int32 value) { "
            + "return value; }", 1, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Helper(int32 value) { return value; } "
            + "int32 Main(int32 value) { return Helper(value); } }",
        0, 1, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Helper(int32 value) { return value; } } "
            + "namespace client { int32 Main(int32 value) { "
            + "return Helper(value); } }", 0, 1, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Helper(int32 value) { return value; } } "
            + "int32 Main(int32 value) { return client.Helper(value); }",
        0, 1, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Helper(int32 value) { return value; } "
            + "namespace inner.deep { int32 Main(int32 value) { "
            + "return Helper(value); } } }", 0, 1, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "using api; namespace client { int32 Main(int32 value) { "
            + "return Imported(7); } }", 0, 4, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { using api; } namespace client.inner { "
            + "int32 Main(int32 value) { return Imported(value); } }",
        3, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace @client { int32 Helper(int32 value) { return value; } } "
            + "namespace client.inner { int32 Main(int32 value) { "
            + "return Helper(value); } }", 0, 1, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { namespace inner.deep { "
            + "int32 Helper(int32 value) { return value; } } } "
            + "namespace client.inner.deep { int32 Main(int32 value) { "
            + "return Helper(value); } }", 0, 1, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Select(int32 left, int32 right) { return left; } "
            + "int32 Select(int32 first, uint32 second) { return first; } "
            + "int32 Main(int32 value) { "
            + "return Select(right: value, left: value); } }",
        0, 2, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Select(int32 first, int32 second) { return first; } "
            + "int32 Select(int32 head, uint32 tail) { return head; } "
            + "int32 Main(int32 value) { "
            + "return Select(first: value, value); } }",
        0, 2, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Select(int32 first, int32 second) { return first; } "
            + "int32 Main(int32 value) { "
            + "return Select(second: value, value); } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Helper(int32 value) { return value; } "
            + "int32 Main(int32 Helper) { return Helper(1); } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "using api; int32 Main(int32 Imported) { return Imported(1); }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Select(int32 value) { return value; } "
            + "uint32 Select(uint32 value) { return value; } "
            + "int32 Main(int32 value) { return Select(value); } }",
        0, 2, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Inner(int32 value) { return value; } "
            + "uint32 Inner(uint32 value) { return value; } "
            + "int32 Outer(int32 value) { return value; } "
            + "uint32 Outer(uint32 value) { return value; } "
            + "int32 Main(int32 value) { "
            + "return Outer(Inner(value)); } }",
        0, 3, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Select(int32 value) { return value; } "
            + "uint32 Select(uint32 value) { return value; } "
            + "int32 Main() { return Select(2147483648); } }",
        4, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "using api; int32 Main(int32 value) { "
            + "return Imported(value: value); }", 2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "using api; int32 Main(int32 value) { "
            + "Imported(value); return value; }", 2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "using api; int32 Main(int32 value) { "
            + "return Imported(value); }", 4, 0, true)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "using api; int32 Main(int32 value) { "
            + "int32 copy = value; return Imported(copy); }",
        0, 15, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "using api; int32 Main(int32 value) { "
            + "int32 copy = value; "
            + "return +Imported((int32(+copy))); }",
        0, 16, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { bool Main(bool value) { return +value; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 First(int32 value) { "
            + "int32 copy = ~value & value; return copy; } "
            + "int32 Second(int32 value) { "
            + "int32 copy = ~value & value; return copy; } }",
        0, 17, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int8 Main(int8 value) { return +value; } }",
        0, 18, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 value) { return int32(value); } }",
        0, 18, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int64 value) { return int32(value); } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int16 Main(int16 value) { return +value; } }",
        0, 18, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 value) { return +value; } }",
        0, 18, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "uint8 Main(uint8 value) { return +value; } }",
        0, 18, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "uint16 Main(uint16 value) { return +value; } }",
        0, 18, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "uint32 Main(uint32 value) { return +value; } }",
        0, 18, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int64 Main(int64 value) { return +value; } }",
        0, 18, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "uint64 Main(uint64 value) { return +value; } }",
        0, 18, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "nint Main(nint value) { return +value; } }",
        0, 18, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "nuint Main(nuint value) { return +value; } }",
        0, 18, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "float32 Main(float32 value) { return +value; } }",
        0, 18, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "float64 Main(float64 value) { return +value; } }",
        0, 18, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { void Main() { (); return (); } }",
        0, 19, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main() { return (); } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 value) { return -value; } }",
        0, 21, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 value) { return -value; } }",
        0, 22, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "float32 Main(float32 value) { return -value; } }",
        0, 24, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "float64 Main(float64 value) { return -value; } }",
        0, 24, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(bool value) { return !value; } }",
        0, 27, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 value) { return ~value; } }",
        0, 28, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 value) { return !value; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(bool value) { return ~value; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(bool left, bool right) { return left & right; } }",
        0, 38, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(bool left, bool right) { return left& right; } }",
        1, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(bool left, bool right) { return left &right; } }",
        1, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(bool left, bool right) { return left | right; } }",
        0, 39, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 left, int32 right) { return left ^ right; } }",
        0, 40, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(bool a, bool b, bool c) { return a | b & c; } }",
        0, 41, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 left, int32 right) { return left & true; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(bool left, bool right) { return left == right; } }",
        0, 42, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(bool left, bool right) { return left== right; } }",
        1, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(bool left, bool right) { return left ==right; } }",
        1, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(bool left, bool right) { return left != right; } }",
        0, 43, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(bool a, bool b, bool c) { return a == b & c; } }",
        0, 44, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(bool left, int32 right) { return left == right; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(int32 left, int32 right) { return left == right; } }",
        0, 45, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(uint32 left, uint32 right) { return left != right; } }",
        0, 46, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(int64 left, int64 right) { return left < right; } }",
        0, 47, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(uint64 left, uint64 right) { return left <= right; } }",
        0, 48, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(float32 left, float32 right) { return left > right; } }",
        0, 49, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(float64 left, float64 right) { return left >= right; } }",
        0, 50, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(char16 left, char16 right) { return left < right; } }",
        0, 51, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(int32 value) { return value < 1; } }",
        0, 52, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(int32 value) { return 1 < value; } }",
        0, 53, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(int32 left, uint32 right) { return left < right; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(int32 left, int32 right) { return 1 < 2; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Helper(int32 value) { return value; } "
            + "bool Main() { return 1 < 2; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(bool left, bool right) { return left < right; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(int32 left, int32 right) { return left <right; } }",
        1, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(int32 left, int32 right) { return left< right; } }",
        1, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(int32 left, int32 right, bool flag) { "
            + "return left < right == flag; } }",
        0, 54, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(float32 left, float32 right) { return left == right; } }",
        0, 55, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(float64 left, float64 right) { return left != right; } }",
        0, 56, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(char16 left, char16 right) { return left == right; } }",
        0, 57, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "void Touch() { return; } "
            + "bool Main() { return Touch() == Touch(); } }",
        0, 58, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main() { return () != (); } }",
        0, 59, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main() { return () < (); } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main() { return () == true; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int8 Main() { return -0x80; } }",
        0, 23, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int8 Main() { return -0b10000000; } }",
        0, 23, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int16 Main() { return -32768; } }",
        0, 23, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main() { return -2147483648; } }",
        0, 23, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "uint32 Main() { return -0; } }",
        0, 23, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "uint8 Main() { return -0x0; } }",
        0, 23, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main() { return -2147483649; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "uint32 Main() { return -1; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int8 Main() { return 0xFF; } }",
        0, 25, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int8 Main() { return 0b11111111; } }",
        0, 25, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int8 Main() { return 0x__0000_FF; } }",
        0, 25, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int16 Main() { return 0xFF; } }",
        0, 25, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { uint8 Main() { return +0xFF; } }",
        0, 25, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int8 Main() { return 0x100; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int8 Main() { return +0x80; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int8 Main() { return + 128; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int8 Main() { return -0xFF; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int8 Main() { return - 128; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int8 Main() { return -(0x80); } }",
        0, 26, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { float32 Main() { return 0.1; } }",
        0, 29, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { float32 Main() { return +0.1; } }",
        0, 29, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { float32 Main() { return -0.0; } }",
        0, 30, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { float64 Main() { return -0x1.fp+2; } }",
        0, 31, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { float32 Main() { return 0xFF; } }",
        0, 32, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { float32 Main() { return +0xFF; } }",
        0, 32, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { float32 Main() { return float32(0xFF); } }",
        0, 32, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { float32 Main() { return -0xFF; } }",
        0, 33, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { float32 Main() { return 0b1010; } }",
        0, 34, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { float32 Main() { return 0x1.fp+2; } }",
        0, 35, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { float32 Main() { return -(0.0); } }",
        0, 36, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { float32 Main() { return 42; } }",
        0, 37, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main() { return 1.0; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { float32 Main() { return - 0.0; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int64 Main() { return -9223372036854775808; } }",
        0, 23, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "nint Main() { return -9223372036854775808; } }",
        0, 23, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(bool value) { return -value; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main() { _ = (); return 0; } }",
        0, 20, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main() { "
            + "return later; int32 later = 1; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main() { "
            + "init int32 result; return result; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main() { "
            + "int32 value = 1; int32 value = 2; return value; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(int32 value) { "
            + "int32 value = 1; return value; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client; namespace inner { "
            + "int32 Main(int32 value) { return value; } }", 1, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 left, int32 right) { "
            + "return left + right; } }", 0, 60, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 left, int32 right) { "
            + "return left + right; } }", 0, 61, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int64 Main(int64 left, int64 right) { "
            + "return left - right; } }", 0, 62, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "uint32 Main(uint32 left, uint32 right) { "
            + "return left * right; } }", 0, 63, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 left, int32 right) { "
            + "return left / right; } }", 0, 64, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "uint64 Main(uint64 left, uint64 right) { "
            + "return left % right; } }", 0, 65, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "float32 Main(float32 left, float32 right) { "
            + "return left + right; } }", 0, 66, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "float64 Main(float64 left, float64 right) { "
            + "return left - right; } }", 0, 67, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "float32 Main(float32 left, float32 right) { "
            + "return left * right; } }", 0, 68, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "float64 Main(float64 left, float64 right) { "
            + "return left / right; } }", 0, 69, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "float32 Main(float32 left, float32 right) { "
            + "return left % right; } }", 0, 70, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main() { return 1 + 2; } }", 0, 71, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 value) { return value + 1; } }",
        0, 72, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 value) { return 1 + value; } }",
        0, 73, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 a, int32 b, int32 c) { "
            + "return a + b * c; } }", 0, 74, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 left, uint32 right) { "
            + "return left + right; } }", 2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 left, float32 right) { "
            + "return left + right; } }", 2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main() { "
            + "mut var value = 1 + 2; return value; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 left, int32 right) { "
            + "return left +right; } }", 1, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 left, int32 right) { "
            + "return left+ right; } }", 1, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "mut int32 value = initial; value = value + 1; "
            + "return value; } }", 0, 75, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "mut int32 value = initial; value += 1; "
            + "return value; } }", 0, 96, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "mut int32 value = initial; value -= 1; "
            + "return value; } }", 0, 97, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "mut int32 value = initial; value *= 2; "
            + "return value; } }", 0, 98, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "mut int32 value = initial; value /= 2; "
            + "return value; } }", 0, 99, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "mut int32 value = initial; value %= 2; "
            + "return value; } }", 0, 100, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "mut int32 value = initial; value &= 1; "
            + "return value; } }", 0, 101, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "mut int32 value = initial; value |= 1; "
            + "return value; } }", 0, 102, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "mut int32 value = initial; value ^= 1; "
            + "return value; } }", 0, 103, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "mut int32 value = initial; return ++value; } }",
        0, 104, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "mut int32 value = initial; return value++; } }",
        0, 105, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "mut int32 value = initial; return --value; } }",
        0, 106, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "mut int32 value = initial; return value--; } }",
        0, 107, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "mut int32 value = initial; return ++value; } }",
        0, 108, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "float32 Main(float32 initial) { "
            + "mut float32 value = initial; return ++value; } }",
        0, 109, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 value, int32 count) { "
            + "return value << count; } }", 0, 111, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 value, int32 count) { "
            + "return value >> count; } }", 0, 112, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 value, uint32 count) { "
            + "return value >>> count; } }", 0, 113, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "uint32 Main(uint32 value, uint32 count) { "
            + "return value >> count; } }", 0, 114, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 value) { "
            + "return value << 1; } }", 0, 115, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int64 Main(int64 value, int32 count) { "
            + "return value >> count; } }", 0, 116, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "uint64 Main(uint64 value, uint32 count) { "
            + "return value >> count; } }", 0, 117, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 value, int64 count) { "
            + "return value << count; } }", 0, 122, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 value, uint64 count) { "
            + "return value >> count; } }", 0, 123, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int64 Main(int64 value, int64 count) { "
            + "return value >>> count; } }", 0, 124, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "uint64 Main(uint64 value, uint64 count) { "
            + "return value >> count; } }", 0, 125, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "float32 Main(float32 value, int32 count) { "
            + "return value << count; } }", 2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 value, float32 count) { "
            + "return value << count; } }", 2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial, int32 count) { "
            + "mut int32 value = initial; value <<= count; "
            + "return value; } }", 0, 118, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial, int32 count) { "
            + "mut int32 value = initial; value >>= count; "
            + "return value; } }", 0, 119, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial, uint32 count) { "
            + "mut int32 value = initial; value >>>= count; "
            + "return value; } }", 0, 120, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "uint32 Main(uint32 initial, uint32 count) { "
            + "mut uint32 value = initial; value >>= count; "
            + "return value; } }", 0, 121, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial, int64 count) { "
            + "mut int32 value = initial; value <<= count; "
            + "return value; } }", 0, 126, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial, uint64 count) { "
            + "mut int32 value = initial; value >>= count; "
            + "return value; } }", 0, 127, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int64 Main(int64 initial, int64 count) { "
            + "mut int64 value = initial; value >>>= count; "
            + "return value; } }", 0, 128, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "uint64 Main(uint64 initial, uint64 count) { "
            + "mut uint64 value = initial; value >>= count; "
            + "return value; } }", 0, 129, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "int32 value = initial; value += 1; "
            + "return value; } }", 2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "int32 value = initial; return ++value; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main() { init mut int32 value; "
            + "return value++; } }", 2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main() { init mut int32 value; "
            + "value += 1; return value; } }", 2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "int32 value = initial; value = 1; "
            + "return value; } }", 2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "mut var value = initial; value = value + 1; "
            + "return value; } }", 0, 77, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main() { var value = 1 + 2; return value; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main() { _ = 1 + 2; return 0; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "mut int32 value = initial; value = true; "
            + "return value; } }", 2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "init int32 value; value = initial; return value; } }",
        0, 81, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "init mut int32 value; value = initial; "
            + "value = value + 1; return value; } }",
        0, 82, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "init mut int32 value; value = initial; "
            + "value += 1; return value; } }",
        0, 82, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main() { init int32 value; return value; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "int32 Main(int32 initial) { "
            + "init int32 value; value = initial; value = initial; "
            + "return value; } }", 2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main() { init int32 value; return value is init; } }",
        0, 90, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(int32 initial) { init int32 value; "
            + "value = initial; return (value) is init; } }",
        0, 91, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main() { init void token; return token is init; } }",
        0, 92, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main() { init void token; token = (); "
            + "return token is init; } }", 0, 93, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(bool initial) { return initial is init; } }",
        0, 94, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { "
            + "bool Main(ref<init int32> destination) { "
            + "return destination is init; } }", 0, 95, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { bool Main(bool condition) { "
            + "if (condition) { return true; } "
            + "else { return false; } } }", 0, 130, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool condition, "
            + "int32 trueValue, int32 falseValue) { "
            + "return condition ? trueValue : falseValue; } }",
        0, 131, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool condition, "
            + "int32 trueValue, uint32 falseValue) { "
            + "return condition ? trueValue : falseValue; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { bool Main(bool left, bool right) { "
            + "return left && right; } }", 0, 132, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { bool Main(bool left, bool right) { "
            + "return left || right; } }", 0, 133, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { bool Main(bool left, bool middle, bool right) { "
            + "return left && middle && right; } }", 0, 136, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { bool Main(bool left, bool middle, bool right) { "
            + "return left || middle || right; } }", 0, 137, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool first, bool second, "
            + "int32 whenBoth, int32 whenFirst, int32 whenNeither) { "
            + "return first ? (second ? whenBoth : whenFirst) "
            + ": whenNeither; } }", 0, 138, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { bool Main(bool left, bool middle, bool right) { "
            + "return left && middle || right; } }", 0, 144, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { bool Main(bool left, bool middle, bool right) { "
            + "return (left || middle) && right; } }", 0, 145, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { bool Main(bool left, bool middle, bool right) { "
            + "bool result = left && middle || right; "
            + "return result; } }", 0, 146, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool first, bool second, "
            + "int32 whenBoth, int32 whenFirst, int32 whenNeither) { "
            + "int32 result = first ? (second ? whenBoth : whenFirst) "
            + ": whenNeither; return result; } }", 0, 147, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { bool Main(bool left, bool middle, bool right) { "
            + "init bool result; "
            + "result = left && middle || right; return result; } }",
        0, 148, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { bool Main(bool left, bool middle, bool right) { "
            + "mut bool result = false; "
            + "result = left && middle || right; return result; } }",
        0, 149, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool first, bool second, "
            + "int32 whenBoth, int32 whenFirst, int32 whenNeither, "
            + "int32 initial) { mut int32 value = initial; "
            + "value = first ? (second ? whenBoth : whenFirst) "
            + ": whenNeither; return value; } }", 0, 150, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool condition, "
            + "int32 whenTrue, int32 whenFalse, int32 offset) { "
            + "return (condition ? whenTrue : whenFalse) + offset; } }",
        0, 151, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { bool Main(bool left, bool middle, bool right, "
            + "bool expected) { "
            + "return (left && middle || right) == expected; } }",
        0, 152, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { bool Main(bool left, bool middle, bool right) { "
            + "if (left && middle || right) { return true; } "
            + "else { return false; } } }", 0, 153, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool left, bool middle, bool right, "
            + "int32 initial, int32 whenTrue, int32 whenFalse) { "
            + "mut int32 value = initial; "
            + "if (left && middle || right) { value = whenTrue; } "
            + "else { value = whenFalse; } return value; } }",
        0, 154, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool left, bool middle, bool right, "
            + "int32 whenTrue, int32 whenFalse) { init int32 value; "
            + "if (left && middle || right) { value = whenTrue; } "
            + "else { value = whenFalse; } return value; } }",
        0, 155, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool left, bool middle, bool right, "
            + "int32 initial, int32 whenTrue) { mut int32 value = initial; "
            + "if (left && middle || right) { value = whenTrue; } "
            + "return value; } }", 0, 156, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool left, bool middle, bool right, "
            + "int32 whenTrue) { init int32 value; "
            + "if (left && middle || right) { value = whenTrue; } "
            + "return value; } }", 2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool condition, int32 value) { "
            + "if (condition) { return Main(false, value); } "
            + "else { return value; } } }", 0, 158, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool condition, int32 value) { "
            + "if (condition) { return Main(false, value); } "
            + "else { return value; } } }", 0, 158, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool first, bool second, "
            + "int32 whenTrue, int32 whenFalse) { "
            + "return (first && second) ? whenTrue : whenFalse; } }",
        0, 159, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool first, bool second, "
            + "int32 whenTrue, int32 whenFalse) { "
            + "return (first || second) ? whenTrue : whenFalse; } }",
        0, 160, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "using api; namespace client { int32 Main(bool condition, "
            + "int32 value) { if (condition) { return Imported(value); } "
            + "else { return value; } } }", 0, 161, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool condition, int32 whenTrue, "
            + "int32 whenFalse) { init int32 value; "
            + "if (condition) { return whenTrue; } "
            + "else { value = whenFalse; } return value; } }",
        0, 162, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool condition, int32 initial, "
            + "int32 whenTrue, int32 whenFalse) { "
            + "mut int32 value = initial; "
            + "if (condition) { return whenTrue; } "
            + "else { value = whenFalse; } return value; } }",
        0, 163, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool condition, "
            + "int32 whenTrue, int32 whenFalse) { init int32 value; "
            + "if (condition) { value = whenFalse; } "
            + "else { return whenTrue; } return value; } }",
        0, 164, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool first, bool second, "
            + "int32 whenTrue, int32 whenFalse) { init int32 value; "
            + "if (first && second) { return whenTrue; } "
            + "else { value = whenFalse; } return value; } }",
        0, 165, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool first, bool second, "
            + "int32 whenTrue, int32 whenFalse) { init int32 value; "
            + "if (first || second) { return whenTrue; } "
            + "else { value = whenFalse; } return value; } }",
        0, 166, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool condition, int32 whenTrue, "
            + "int32 whenFalse) { "
            + "if (condition) { return whenTrue; } "
            + "return whenFalse; } }", 0, 167, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool first, bool second, "
            + "int32 whenTrue, int32 whenFalse) { "
            + "if (first && second) { return whenTrue; } "
            + "return whenFalse; } }", 0, 168, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool condition, "
            + "int32 leftInitial, int32 rightInitial, "
            + "int32 whenTrue, int32 whenFalse) { "
            + "mut int32 left = leftInitial; "
            + "mut int32 right = rightInitial; "
            + "if (condition) { left = whenTrue; } "
            + "else { right = whenFalse; } "
            + "return left + right; } }", 0, 169, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool first, bool second, "
            + "int32 leftInitial, int32 rightInitial, "
            + "int32 whenTrue, int32 whenFalse) { "
            + "mut int32 left = leftInitial; "
            + "mut int32 right = rightInitial; "
            + "if (first && second) { left = whenTrue; } "
            + "else { right = whenFalse; } "
            + "return left + right; } }", 0, 170, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool first, bool second, "
            + "int32 leftInitial, int32 rightInitial, "
            + "int32 whenTrue, int32 whenFalse) { "
            + "mut int32 left = leftInitial; "
            + "mut int32 right = rightInitial; "
            + "if (first || second) { left = whenTrue; } "
            + "else { right = whenFalse; } "
            + "return left + right; } }", 0, 171, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(int32 initial, int32 limit) { "
            + "mut int32 counter = initial; "
            + "while (counter < limit) { "
            + "counter = counter + 1; } "
            + "return counter; } }", 0, 172, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(int32 initial, int32 limit) { "
            + "mut int32 counter = initial; "
            + "while (counter < limit) { "
            + "counter = counter + 1; continue; } "
            + "return counter; } }", 0, 173, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(int32 initial, int32 limit) { "
            + "mut int32 counter = initial; "
            + "while (counter < limit) { "
            + "counter = counter + 1; break; } "
            + "return counter; } }", 0, 174, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main() { break; return 0; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main() { continue; return 0; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool condition, int32 initial, "
            + "int32 whenTrue, int32 whenFalse) { "
            + "mut int32 value = initial; "
            + "if (condition) { value = whenTrue; } "
            + "else { value = whenFalse; } "
            + "return value; } }", 0, 134, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool condition, int32 initial, "
            + "int32 whenTrue, int32 whenFalse) { "
            + "mut int32 value = initial; "
            + "if (condition) { value = whenTrue; value = initial; } "
            + "else { value = whenFalse; value = whenTrue; } "
            + "return value; } }", 0, 135, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool condition, int32 initial, "
            + "int32 whenTrue) { mut int32 value = initial; "
            + "if (condition) { value = whenTrue; } "
            + "return value; } }", 0, 139, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool condition, "
            + "int32 whenTrue, int32 whenFalse) { "
            + "init int32 value; "
            + "if (condition) { value = whenTrue; } "
            + "else { value = whenFalse; } "
            + "return value; } }", 0, 140, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool condition, int32 whenTrue) { "
            + "init int32 value; "
            + "if (condition) { value = whenTrue; } "
            + "return value; } }", 2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool condition, int32 first, "
            + "int32 second, int32 whenFalse) { init int32 value; "
            + "if (condition) { value = first; value = second; } "
            + "else { value = whenFalse; } return value; } }",
        2, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(bool condition, int32 initial, "
            + "int32 whenTrue, int32 whenFalse) { "
            + "mut int32 first = initial; mut int32 second = initial; "
            + "if (condition) { first = whenTrue; } "
            + "else { second = whenFalse; } "
            + "return first; } }", 3, 0, false)]
    [TestCase(BoundRuntimePolicy.FailFast,
        "namespace client { int32 Main(int32 left, int32 right) { "
            + "return left && right; } }", 2, 0, false)]
    public async Task TerrumOwnedSourceBinderValidatesOrderedCallUsesAsync(
        BoundRuntimePolicy policy, string sourceText,
        int expectedSourceStatus, int verificationMode,
        bool includeAmbiguousImportedProvider)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var source = SourceText.From(sourceText);
        var artifacts = new List<TrRmStandardArtifactInput> {
            new(SchemaIdentity,
                CreateMethodProviderArtifact("provider/leaf",
                    "api.Imported", scalarParameter: true,
                    executableBody: verificationMode == 161)),
        };
        var methodRoots = new List<StandaloneMethodRoot> {
            new(0, "provider/leaf",
                "api.Imported"),
        };
        if (includeAmbiguousImportedProvider)
        {
            artifacts.Add(new TrRmStandardArtifactInput(SchemaIdentity,
                CreateMethodProviderArtifact("provider/other",
                    "api.Imported")));
            methodRoots.Add(new StandaloneMethodRoot(1,
                "provider/other", "api.Imported"));
        }
        var input = new StandaloneMethodImportInput(Agreement,
            "consumer/module", artifacts, methodRoots);
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
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/HexDigitValue.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/BasedIntegerFloatLiteral.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/HexFloatLiteral.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/DecimalFloatLiteral.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodImportInput.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneArtifactDirectory.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneStringTable.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneModuleDirectory.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneCallableTypes.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneTypeSpecValidation.terrum",
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
        const string successfulVerificationSource = """
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
                    || Host.StandaloneSourceCallArguments[0] != 2
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
                    || Host.StandaloneSourceStatements[0] != 3
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
                    || Host.StandaloneNormalizedOperations[5] != 3
                    || Host.StandaloneNormalizedOperations[6] != 1
                    || Host.StandaloneNormalizedOperations[10] != 1
                    || Host.StandaloneNormalizedOperations[11] != 0
                    || Host.StandaloneNormalizedOperations[15] != 2
                    || Host.StandaloneNormalizedOperations[16] != 3
                    || Host.StandaloneNormalizedOperations[20] != 3
                    || Host.StandaloneNormalizedOperations[21] != 2
                    || Host.StandaloneNormalizedOperations[25] != 4
                    || Host.StandaloneNormalizedOperations[26] != 4) {
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
        const string localSourceArenaVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 60; }
                int32 status = BindStandaloneOwnedSourceProgram();
                int32 resultTypeCode
                    = Host.StandaloneTypeFacts[
                        Host.StandaloneSourceExpressions[1] * 3 + 1];
                int32 expectedOperationKind
                    = resultTypeCode == 0x14 || resultTypeCode == 0x15
                        ? 8
                        : Host.StandaloneSourceArithmeticProfile[0] == 0
                            ? 6 : 7;
                if (status != 0
                    || Host.StandaloneSourceProgramStatus[0] != 0
                    || Host.StandaloneSourceBodyCount[0] != 1
                    || Host.StandaloneSourceExpressionCount[0] != 3
                    || Host.StandaloneSourceStatementCount[0] != 2
                    || Host.StandaloneSourceLocalCount[0] != 1
                    || Host.StandaloneSourcePlaceCount[0] != 1
                    || Host.StandaloneSourceLexicalScopeCount[0] != 1
                    || Host.StandaloneSourceBodyScopeIds[0] != 0
                    || Host.StandaloneSourceBodyLocalRanges[0] != 0
                    || Host.StandaloneSourceBodyLocalRanges[1] != 1
                    || Host.StandaloneSourceBodyPlaceRanges[0] != 0
                    || Host.StandaloneSourceBodyPlaceRanges[1] != 1
                    || Host.StandaloneSourceLexicalScopes[0] != -1
                    || Host.StandaloneSourceLexicalScopes[1] != 0
                    || Host.StandaloneSourceLexicalScopes[2] != 0
                    || Host.StandaloneSourceLexicalScopes[3] != 1
                    || Host.StandaloneSourceLocals[0] != 0
                    || Host.StandaloneSourceLocals[1] != 0
                    || Host.StandaloneSourceLocals[3] != 0
                    || Host.StandaloneSourceLocalStates[0] != 1
                    || Host.StandaloneSourceLocalDeclarationUseCounts[0]
                        != 1
                    || Host.StandaloneSourcePlaces[0] != 0
                    || Host.StandaloneSourcePlaces[2] != 1
                    || Host.StandaloneSourcePlaces[3] != 0
                    || Host.StandaloneSourcePlaces[4] != -1
                    || Host.StandaloneSourcePlaceUseCounts[0] != 1
                    || Host.StandaloneSourceExpressions[0] != 1
                    || Host.StandaloneSourceExpressions[8] != 5
                    || Host.StandaloneSourceExpressions[16] != 3
                    || Host.StandaloneSourceExpressions[9]
                        != Host.StandaloneSourceLocals[2]
                    || Host.StandaloneSourceExpressions[10] != 0
                    || Host.StandaloneSourceCallArgumentCount[0] != 1
                    || Host.StandaloneSourceCallArguments[0] != 1
                    || Host.StandaloneSourceCallArgumentOwners[0] != 1
                    || Host.StandaloneSourceStatements[0] != 4
                    || Host.StandaloneSourceStatements[1] != 0
                    || Host.StandaloneSourceStatements[2] != 0
                    || Host.StandaloneSourceStatements[3] != 1
                    || Host.StandaloneSourceStatements[4] != 2
                    || Host.StandaloneSourceStatements[5] != -1
                    || Host.StandaloneSourceExpressionUseCounts[0] != 1
                    || Host.StandaloneSourceExpressionUseCounts[1] != 1
                    || Host.StandaloneSourceExpressionUseCounts[2] != 1) {
                    return 61;
                }
                return 0;
            }
            """;
        const string localSourceNormalizationVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 70; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0) { return status; }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedProgramStatus[0] != 0
                    || Host.StandaloneNormalizedBodyCount[0] != 1
                    || Host.StandaloneSourceExpressionCount[0] != 6
                    || Host.StandaloneSourceExpressions[16] != 6
                    || Host.StandaloneSourceExpressions[24] != 8
                    || Host.StandaloneSourceExpressions[32] != 3
                    || Host.StandaloneSourceExpressions[40] != 6
                    || Host.StandaloneSourceStatements[4] != 5
                    || Host.StandaloneNormalizedHomeCount[0] != 2
                    || Host.StandaloneNormalizedSourceLocalHomeIds[0]
                        != 0
                    || Host.StandaloneNormalizedBodies[2] != 2
                    || Host.StandaloneNormalizedBodies[10] != 2
                    || Host.StandaloneInvokeCount[0] != 1
                    || Host.StandaloneInvokeInputCount[0] != 1
                    || Host.StandaloneInvokeInputs[0] != 2
                    || Host.StandaloneInvokeInputs[1] != 1
                    || Host.StandaloneInvokeInputs[2] != 0
                    || Host.StandaloneSourceCallArguments[0] != 3
                    || Host.StandaloneNormalizedOperations[0] != 3
                    || Host.StandaloneNormalizedOperations[5] != 4) {
                    return 71;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceBodyCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteBodyIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id] = id;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0
                    || Host.StandaloneRewrittenSourceLocalCount[0] != 1
                    || Host.StandaloneRewrittenSourcePlaceCount[0] != 1
                    || Host.StandaloneRewrittenSourceStatements[2] != 0
                    || Host.StandaloneRewrittenSourceExpressions[10]
                        != 0
                    || Host.StandaloneRewrittenSourceExpressions[18]
                        != 1
                    || Host.StandaloneRewrittenSourceExpressions[26]
                        != 2
                    || Host.StandaloneRewrittenSourceExpressions[42]
                        != 4
                    || Host.StandaloneRewrittenNormalizedSourceLocalHomeIds[
                        0] != 0) {
                    return 72;
                }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 73;
                }
                return 0;
            }
            """;
        const string localSourcePermutationVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 100; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0) { return status; }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceBodyCount[0] != 2
                    || Host.StandaloneSourceLocalCount[0] != 2
                    || Host.StandaloneSourcePlaceCount[0] != 2) {
                    return 101;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                if (status != 0
                    || Host.StandaloneRewrittenSourceBodyScopeIds[0]
                        != 1
                    || Host.StandaloneRewrittenSourceBodyScopeIds[1]
                        != 0
                    || Host.StandaloneRewrittenSourceBodies[0] != 0
                    || Host.StandaloneRewrittenSourceBodies[12] != 1
                    || Host.StandaloneRewrittenSourceBodyLocalRanges[0]
                        != 1
                    || Host.StandaloneRewrittenSourceBodyLocalRanges[1]
                        != 1
                    || Host.StandaloneRewrittenSourceBodyLocalRanges[2]
                        != 0
                    || Host.StandaloneRewrittenSourceBodyLocalRanges[3]
                        != 1
                    || Host.StandaloneRewrittenSourceLocals[0] != 1
                    || Host.StandaloneRewrittenSourceLocals[10] != 0
                    || Host.StandaloneRewrittenSourcePlaces[0] != 1
                    || Host.StandaloneRewrittenSourcePlaces[5] != 0
                    || Host.StandaloneRewrittenSourceLexicalScopes[1]
                        != 1
                    || Host.StandaloneRewrittenSourceLexicalScopes[6]
                        != 0) {
                    return 102;
                }
                return 0;
            }
            """;
        const string floatUnaryPlusVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 110; }
                int32 status = BindStandaloneOwnedSourceProgram();
                int32 resultTypeCode
                    = Host.StandaloneTypeFacts[
                        Host.StandaloneSourceExpressions[1] * 3 + 1];
                int32 identityExpressionKind
                    = Host.StandaloneSourceExpressions[8];
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 2
                    || Host.StandaloneSourceExpressions[0] != 1
                    || (identityExpressionKind != 6
                        && identityExpressionKind != 8)
                    || Host.StandaloneSourceExpressions[1] !=
                        Host.StandaloneSourceExpressions[9]
                    || (resultTypeCode != 0x06
                        && resultTypeCode != 0x07
                        && resultTypeCode != 0x08
                        && resultTypeCode != 0x0D
                        && resultTypeCode != 0x09
                        && resultTypeCode != 0x0B
                        && resultTypeCode != 0x0C
                        && resultTypeCode != 0x0E
                        && resultTypeCode != 0x10
                        && resultTypeCode != 0x11
                        && resultTypeCode != 0x14
                        && resultTypeCode != 0x15)) {
                    return 111;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedHomeCount[0] != 1
                    || Host.StandaloneNormalizedOperationCount[0] != 1
                    || Host.StandaloneNormalizedOperations[0] != 4
                    || Host.StandaloneNormalizedBodies[2] != 1) {
                    return 112;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceBodyCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteBodyIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id] = id;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 113;
                }
                return 0;
            }
            """;
        const string unaryMinusVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 140; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 2
                    || Host.StandaloneSourceExpressions[0] != 1
                    || Host.StandaloneSourceExpressions[8] != 9) {
                    return 141;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedProgramStatus[0] != 0
                    || Host.StandaloneNormalizedHomeCount[0] != 2
                    || Host.StandaloneNormalizedOperationCount[0] != 2
                    || Host.StandaloneNormalizedOperations[0]
                        != expectedOperationKind
                    || Host.StandaloneNormalizedOperations[1] != 0
                    || Host.StandaloneNormalizedOperations[2] != 1
                    || Host.StandaloneNormalizedOperations[5] != 4
                    || Host.StandaloneNormalizedOperations[6] != 1) {
                    return 142;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                Host.StandaloneRewriteBodyIds[0] = 0;
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id] = id;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 143;
                }
                return 0;
            }
            """;
        const string signedMinimumLiteralVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 150; }
                int32 status = BindStandaloneOwnedSourceProgram();
                int32 resultTypeCode
                    = Host.StandaloneTypeFacts[
                        Host.StandaloneSourceExpressions[1] * 3 + 1];
                int32 expectedLow = resultTypeCode == 0x06 ? 0x80
                    : resultTypeCode == 0x07 ? 0x8000
                    : resultTypeCode == 0x08 ? -2147483648 : 0;
                int32 expectedHigh
                    = resultTypeCode == 0x09 || resultTypeCode == 0x10
                        ? -2147483648 : 0;
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 1
                    || Host.StandaloneSourceExpressions[0] != 2
                    || Host.StandaloneSourceExpressions[1] <= 0
                    || Host.StandaloneTypeFacts[
                        Host.StandaloneSourceExpressions[1] * 3 + 1]
                        != resultTypeCode
                    || (resultTypeCode != 0x06
                        && resultTypeCode != 0x07
                        && resultTypeCode != 0x08
                        && resultTypeCode != 0x09
                        && resultTypeCode != 0x0D
                        && resultTypeCode != 0x0B
                        && resultTypeCode != 0x10)
                    || Host.StandaloneSourceExpressions[4] != expectedLow
                    || Host.StandaloneSourceExpressions[5] != expectedHigh) {
                    return 151;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedConstantCount[0] != 1
                    || Host.StandaloneNormalizedConstants[2]
                        != expectedLow
                    || Host.StandaloneNormalizedConstants[3]
                        != expectedHigh
                    || Host.StandaloneNormalizedOperationCount[0] != 3
                    || Host.StandaloneNormalizedOperations[0] != 1
                    || Host.StandaloneNormalizedOperations[5] != 2
                    || Host.StandaloneNormalizedOperations[10] != 4) {
                    return 152;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                Host.StandaloneRewriteBodyIds[0] = 0;
                Host.StandaloneRewriteExpressionIds[0] = 0;
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 153;
                }
                return 0;
            }
            """;
        const string basedIntegerBitsVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 160; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 1
                    || Host.StandaloneSourceExpressions[0] != 2
                    || (Host.StandaloneTypeFacts[
                            Host.StandaloneSourceExpressions[1] * 3 + 1]
                        != 0x06
                        && Host.StandaloneTypeFacts[
                            Host.StandaloneSourceExpressions[1] * 3 + 1]
                            != 0x07
                        && Host.StandaloneTypeFacts[
                            Host.StandaloneSourceExpressions[1] * 3 + 1]
                            != 0x0B)
                    || Host.StandaloneSourceExpressions[4] != 0xFF
                    || Host.StandaloneSourceExpressions[5] != 0) {
                    return 161;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedConstantCount[0] != 1
                    || Host.StandaloneNormalizedConstants[2] != 0xFF
                    || Host.StandaloneNormalizedConstants[3] != 0
                    || Host.StandaloneNormalizedOperationCount[0] != 3) {
                    return 162;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                Host.StandaloneRewriteBodyIds[0] = 0;
                Host.StandaloneRewriteExpressionIds[0] = 0;
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 163;
                }
                return 0;
            }
            """;
        const string basedTypedNegationVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 170; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 2
                    || Host.StandaloneSourceExpressions[0] != 2
                    || Host.StandaloneSourceExpressions[8] != 9
                    || Host.StandaloneSourceExpressions[4] != 0x80
                    || Host.StandaloneSourceExpressions[5] != 0) {
                    return 171;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedConstantCount[0] != 1
                    || Host.StandaloneNormalizedConstants[2] != 0x80
                    || Host.StandaloneNormalizedHomeCount[0] != 2
                    || Host.StandaloneNormalizedOperationCount[0] != 4
                    || Host.StandaloneNormalizedOperations[0] != 1
                    || Host.StandaloneNormalizedOperations[5] != 2
                    || Host.StandaloneNormalizedOperations[10] != 6
                    || Host.StandaloneNormalizedOperations[11] != 0
                    || Host.StandaloneNormalizedOperations[12] != 1
                    || Host.StandaloneNormalizedOperations[15] != 4
                    || Host.StandaloneNormalizedOperations[16] != 1) {
                    return 172;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                Host.StandaloneRewriteBodyIds[0] = 0;
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id] = id;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 173;
                }
                return 0;
            }
            """;
        const string unaryNotVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 180; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 2) {
                    return 181;
                }
                int32 expressionKind
                    = Host.StandaloneSourceExpressions[8];
                int32 typeId
                    = Host.StandaloneSourceExpressions[9];
                if (typeId <= 0
                    || typeId > Host.StandaloneTypeFactCount[0]) {
                    return 182;
                }
                int32 typeCode
                    = Host.StandaloneTypeFacts[typeId * 3 + 1];
                if (Host.StandaloneSourceExpressions[0] != 1
                    || (expressionKind != 10 && expressionKind != 11)
                    || (expressionKind == 10 && typeCode != 1)
                    || (expressionKind == 11 && typeCode != 0x08)
                    || Host.StandaloneSourceExpressions[10] != 0) {
                    return 183;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedHomeCount[0] != 2
                    || Host.StandaloneNormalizedOperationCount[0] != 2
                    || Host.StandaloneNormalizedOperations[0] != 9
                    || Host.StandaloneNormalizedOperations[1] != 0
                    || Host.StandaloneNormalizedOperations[2] != 1
                    || Host.StandaloneNormalizedOperations[5] != 4
                    || Host.StandaloneNormalizedOperations[6] != 1) {
                    return 184;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                Host.StandaloneRewriteBodyIds[0] = 0;
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id] = id;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 185;
                }
                return 0;
            }
            """;
        const string floatingGroupedNegationVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 190; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 2
                    || Host.StandaloneSourceExpressions[0] != 2
                    || Host.StandaloneSourceExpressions[4] != 0
                    || Host.StandaloneSourceExpressions[5] != 0
                    || Host.StandaloneSourceExpressions[8] != 9) {
                    return 191;
                }
                int32 typeId
                    = Host.StandaloneSourceExpressions[1];
                if (typeId <= 0
                    || typeId > Host.StandaloneTypeFactCount[0]
                    || Host.StandaloneTypeFacts[typeId * 3 + 1]
                        != 0x14) {
                    return 192;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedConstantCount[0] != 1
                    || Host.StandaloneNormalizedConstants[2] != 0
                    || Host.StandaloneNormalizedConstants[3] != 0
                    || Host.StandaloneNormalizedHomeCount[0] != 2
                    || Host.StandaloneNormalizedOperationCount[0] != 4
                    || Host.StandaloneNormalizedOperations[0] != 1
                    || Host.StandaloneNormalizedOperations[5] != 2
                    || Host.StandaloneNormalizedOperations[10] != 8
                    || Host.StandaloneNormalizedOperations[11] != 0
                    || Host.StandaloneNormalizedOperations[12] != 1
                    || Host.StandaloneNormalizedOperations[15] != 4
                    || Host.StandaloneNormalizedOperations[16] != 1) {
                    return 193;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                Host.StandaloneRewriteBodyIds[0] = 0;
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id] = id;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 194;
                }
                return 0;
            }
            """;
        const string bitwisePrecedenceVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 210; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 5
                    || Host.StandaloneSourceExpressions[0] != 1
                    || Host.StandaloneSourceExpressions[8] != 1
                    || Host.StandaloneSourceExpressions[16] != 1
                    || Host.StandaloneSourceExpressions[24] != 12
                    || Host.StandaloneSourceExpressions[26] != 1
                    || Host.StandaloneSourceExpressions[27] != 2
                    || Host.StandaloneSourceExpressions[32] != 13
                    || Host.StandaloneSourceExpressions[34] != 0
                    || Host.StandaloneSourceExpressions[35] != 3) {
                    return 211;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedHomeCount[0] != 5
                    || Host.StandaloneNormalizedOperationCount[0] != 3
                    || Host.StandaloneNormalizedOperations[0] != 10
                    || Host.StandaloneNormalizedOperations[1] != 1
                    || Host.StandaloneNormalizedOperations[2] != 2
                    || Host.StandaloneNormalizedOperations[3] != 3
                    || Host.StandaloneNormalizedOperations[5] != 11
                    || Host.StandaloneNormalizedOperations[6] != 0
                    || Host.StandaloneNormalizedOperations[7] != 3
                    || Host.StandaloneNormalizedOperations[8] != 4
                    || Host.StandaloneNormalizedOperations[10] != 4
                    || Host.StandaloneNormalizedOperations[11] != 4) {
                    return 212;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                Host.StandaloneRewriteBodyIds[0] = 0;
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id] = id;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 213;
                }
                return 0;
            }
            """;
        const string booleanEqualityPrecedenceVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 230; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 5
                    || Host.StandaloneSourceExpressions[0] != 1
                    || Host.StandaloneSourceExpressions[8] != 1
                    || Host.StandaloneSourceExpressions[16] != 15
                    || Host.StandaloneSourceExpressions[18] != 0
                    || Host.StandaloneSourceExpressions[19] != 1
                    || Host.StandaloneSourceExpressions[24] != 1
                    || Host.StandaloneSourceExpressions[32] != 12
                    || Host.StandaloneSourceExpressions[34] != 2
                    || Host.StandaloneSourceExpressions[35] != 3) {
                    return 231;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedHomeCount[0] != 5
                    || Host.StandaloneNormalizedOperationCount[0] != 3
                    || Host.StandaloneNormalizedOperations[0] != 13
                    || Host.StandaloneNormalizedOperations[1] != 0
                    || Host.StandaloneNormalizedOperations[2] != 1
                    || Host.StandaloneNormalizedOperations[3] != 3
                    || Host.StandaloneNormalizedOperations[5] != 10
                    || Host.StandaloneNormalizedOperations[6] != 3
                    || Host.StandaloneNormalizedOperations[7] != 2
                    || Host.StandaloneNormalizedOperations[8] != 4
                    || Host.StandaloneNormalizedOperations[10] != 4
                    || Host.StandaloneNormalizedOperations[11] != 4) {
                    return 232;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                Host.StandaloneRewriteBodyIds[0] = 0;
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id] = id;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 233;
                }
                return 0;
            }
            """;
        const string unitLiteralVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 120; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 2
                    || Host.StandaloneSourceExpressions[0] != 7
                    || Host.StandaloneSourceExpressions[8] != 7
                    || Host.StandaloneSourceStatementCount[0] != 2
                    || Host.StandaloneSourceStatements[0] != 2
                    || Host.StandaloneSourceStatements[1] != 0
                    || Host.StandaloneSourceStatements[3] != 1
                    || Host.StandaloneSourceStatements[4] != 1) {
                    return 121;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedHomeCount[0] != 0
                    || Host.StandaloneNormalizedOperationCount[0] != 1
                    || Host.StandaloneNormalizedOperations[0] != 5
                    || Host.StandaloneNormalizedBodies[2] != 0) {
                    return 122;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                Host.StandaloneRewriteBodyIds[0] = 0;
                Host.StandaloneRewriteExpressionIds[0] = 0;
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 123;
                }
                return 0;
            }
            """;
        const string unitDiscardVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 130; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 2
                    || Host.StandaloneSourceExpressions[0] != 7
                    || Host.StandaloneSourceStatements[0] != 3
                    || Host.StandaloneSourceStatements[1] != 0
                    || Host.StandaloneSourceStatements[3] != 1
                    || Host.StandaloneSourceStatements[4] != 1) {
                    return 131;
                }
                int32 unitTypeId
                    = Host.StandaloneSourceExpressions[1];
                if (Host.StandaloneTypeFacts[unitTypeId * 3] != 0
                    || Host.StandaloneTypeFacts[unitTypeId * 3 + 1] != 0
                    || Host.StandaloneTypeFacts[unitTypeId * 3 + 2] != 0) {
                    return 132;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedProgramStatus[0] != 0
                    || Host.StandaloneNormalizedOperationCount[0] != 3) {
                    return 133;
                }
                return 0;
            }
            """;
        const string localNamespaceCallVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 50; }
                int32 status = BindStandaloneOwnedSourceProgram();
                int32 localCallableCount
                    = Host.StandaloneLocalCallableCount[0];
                int32 targetIndex
                    = Host.StandaloneSourceDirectCalls[1];
                if (status != 0
                    || Host.StandaloneSourceProgramStatus[0] != 0
                    || localCallableCount < 2
                    || Host.StandaloneSourceBodyCount[0]
                        != localCallableCount
                    || Host.StandaloneSourceDirectCallCount[0] != 1
                    || Host.StandaloneSourceCallArgumentCount[0] <= 0
                    || Host.StandaloneSourceDirectCalls[0] != 1
                    || targetIndex < 0
                    || targetIndex >= localCallableCount - 1
                    || Host.StandaloneSourceDirectCalls[2]
                        != Host.StandaloneLocalCallables[
                            targetIndex * 3 + 1]
                    || Host.StandaloneSourceDirectCalls[3]
                        != Host.StandaloneCallableTypeIds[
                            Host.StandaloneLocalCallables[
                                targetIndex * 3 + 1]]
                    || Host.StandaloneSourceDirectCalls[5]
                        != Host.StandaloneSourceCallArgumentCount[0]
                    || Host.StandaloneSourceDirectCallUseCounts[0] != 1) {
                    return 51;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedProgramStatus[0] != 0
                    || Host.StandaloneNormalizedBodyCount[0]
                        != localCallableCount
                    || Host.StandaloneInvokeCount[0] != 1) {
                    return 52;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0) {
                    return 53;
                }
                return 0;
            }
            """;
        const string localOverloadCallVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 60; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneLocalCallableCount[0] != 3
                    || Host.StandaloneSourceBodyCount[0] != 3
                    || Host.StandaloneSourceDirectCallCount[0] != 1
                    || Host.StandaloneSourceCallArgumentCount[0] <= 0
                    || Host.StandaloneSourceDirectCalls[0] != 1
                    || Host.StandaloneSourceDirectCalls[1] != 0
                    || Host.StandaloneSourceDirectCalls[2]
                        != Host.StandaloneLocalCallables[1]
                    || (Host.StandaloneSourceCallArgumentCount[0] == 1
                        && Host.StandaloneSourceCallArguments[1] != 0)
                    || (Host.StandaloneSourceCallArgumentCount[0] == 2
                        && !((Host.StandaloneSourceCallArguments[1] == 1
                                && Host.StandaloneSourceCallArguments[4] == 0)
                            || (Host.StandaloneSourceCallArguments[1] == 0
                                && Host.StandaloneSourceCallArguments[4] == 1)))
                    || (Host.StandaloneSourceCallArgumentCount[0] != 1
                        && Host.StandaloneSourceCallArgumentCount[0] != 2)) {
                    return 61;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedProgramStatus[0] != 0
                    || Host.StandaloneNormalizedBodyCount[0] != 3
                    || Host.StandaloneInvokeCount[0] != 1) {
                    return 62;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0) {
                    return 63;
                }
                return 0;
            }
            """;
        const string nestedLocalOverloadCallVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 70; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneLocalCallableCount[0] != 5
                    || Host.StandaloneSourceDirectCallCount[0] != 2
                    || Host.StandaloneSourceCallArgumentCount[0] != 2
                    || Host.StandaloneSourceDirectCalls[0] != 1
                    || Host.StandaloneSourceDirectCalls[1] != 2
                    || Host.StandaloneSourceDirectCalls[7] != 1
                    || Host.StandaloneSourceDirectCalls[8] != 0
                    || Host.StandaloneSourceCallArguments[1] != 0
                    || Host.StandaloneSourceCallArguments[4] != 0) {
                    return 71;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedProgramStatus[0] != 0
                    || Host.StandaloneNormalizedBodyCount[0] != 5
                    || Host.StandaloneInvokeCount[0] != 2) {
                    return 72;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0) {
                    return 73;
                }
                return 0;
            }
            """;
        const string genericSourceCallVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 90; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0) { return status; }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0) { return status; }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] <= 0) {
                    return 91;
                }
                return 0;
            }
            """;
        var expectedFloatLiteralTypeCode
            = verificationMode == 31 ? 0x15 : 0x14;
        var expectedFloatLiteralBits = verificationMode switch
        {
            29 => 0x3DCCCCCDUL,
            30 => 0x80000000UL,
            31 => 0xC01F000000000000UL,
            32 => 0x437F0000UL,
            33 => 0xC37F0000UL,
            34 => 0x41200000UL,
            35 => 0x40F80000UL,
            37 => 0x42280000UL,
            _ => 0UL,
        };
        var expectedFloatLiteralLow
            = unchecked((int)(uint)expectedFloatLiteralBits);
        var expectedFloatLiteralHigh
            = unchecked((int)(uint)(expectedFloatLiteralBits >> 32));
        var floatingLiteralVerificationSource = $$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 200; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 1
                    || Host.StandaloneSourceExpressions[0] != 2) {
                    return 201;
                }
                int32 typeId
                    = Host.StandaloneSourceExpressions[1];
                if (typeId <= 0
                    || typeId > Host.StandaloneTypeFactCount[0]
                    || Host.StandaloneTypeFacts[typeId * 3 + 1]
                        != {{expectedFloatLiteralTypeCode}}
                    || Host.StandaloneSourceExpressions[4]
                        != {{expectedFloatLiteralLow}}
                    || Host.StandaloneSourceExpressions[5]
                        != {{expectedFloatLiteralHigh}}) {
                    return 202;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedConstantCount[0] != 1
                    || Host.StandaloneNormalizedConstants[2]
                        != {{expectedFloatLiteralLow}}
                    || Host.StandaloneNormalizedConstants[3]
                        != {{expectedFloatLiteralHigh}}
                    || Host.StandaloneNormalizedHomeCount[0] != 1
                    || Host.StandaloneNormalizedOperationCount[0] != 3
                    || Host.StandaloneNormalizedOperations[0] != 1
                    || Host.StandaloneNormalizedOperations[5] != 2
                    || Host.StandaloneNormalizedOperations[10] != 4) {
                    return 203;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                Host.StandaloneRewriteBodyIds[0] = 0;
                Host.StandaloneRewriteExpressionIds[0] = 0;
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 204;
                }
                return 0;
            }
            """;
        var expectedBitwiseExpressionKind = verificationMode switch
        {
            38 => 12,
            39 => 13,
            40 => 14,
            _ => 0,
        };
        var expectedBitwiseOperationKind = verificationMode switch
        {
            38 => 10,
            39 => 11,
            40 => 12,
            _ => 0,
        };
        var expectedBitwiseTypeCode = verificationMode == 40
            ? 0x08 : 1;
        var bitwiseBinaryVerificationSource = $$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 220; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 3
                    || Host.StandaloneSourceExpressions[0] != 1
                    || Host.StandaloneSourceExpressions[8] != 1
                    || Host.StandaloneSourceExpressions[16]
                        != {{expectedBitwiseExpressionKind}}
                    || Host.StandaloneSourceExpressions[18] != 0
                    || Host.StandaloneSourceExpressions[19] != 1) {
                    return 221;
                }
                int32 typeId
                    = Host.StandaloneSourceExpressions[17];
                if (typeId <= 0
                    || typeId > Host.StandaloneTypeFactCount[0]
                    || Host.StandaloneTypeFacts[typeId * 3 + 1]
                        != {{expectedBitwiseTypeCode}}) {
                    return 222;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 1
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 1
                    || Host.StandaloneNormalizedBlocks[0] != 0
                    || Host.StandaloneNormalizedBlocks[1] != 0
                    || Host.StandaloneNormalizedBlocks[2] != 0
                    || Host.StandaloneNormalizedBlocks[3] != 2
                    || Host.StandaloneNormalizedBlocks[4] != 1
                    || Host.StandaloneNormalizedBlocks[5] != 2
                    || Host.StandaloneNormalizedBlocks[6] != 2
                    || Host.StandaloneNormalizedBlocks[7] != typeId
                    || Host.StandaloneNormalizedHomeCount[0] != 3
                    || Host.StandaloneNormalizedOperationCount[0] != 2
                    || Host.StandaloneNormalizedOperations[0]
                        != {{expectedBitwiseOperationKind}}
                    || Host.StandaloneNormalizedOperations[1] != 0
                    || Host.StandaloneNormalizedOperations[2] != 1
                    || Host.StandaloneNormalizedOperations[3] != 2
                    || Host.StandaloneNormalizedOperations[5] != 4
                    || Host.StandaloneNormalizedOperations[6] != 2) {
                    return 223;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                Host.StandaloneRewriteBodyIds[0] = 0;
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id] = id;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 224;
                }
                return 0;
            }
            """;
        var expectedBooleanEqualityExpressionKind
            = verificationMode == 42 ? 15 : 16;
        var expectedBooleanEqualityOperationKind
            = verificationMode == 42 ? 13 : 14;
        var expectedBooleanEqualityControl
            = verificationMode == 42 ? 0x01 : 0x02;
        var booleanEqualityVerificationSource = $$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 240; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 3
                    || Host.StandaloneSourceExpressions[0] != 1
                    || Host.StandaloneSourceExpressions[8] != 1
                    || Host.StandaloneSourceExpressions[16]
                        != {{expectedBooleanEqualityExpressionKind}}
                    || Host.StandaloneSourceExpressions[18] != 0
                    || Host.StandaloneSourceExpressions[19] != 1) {
                    return 241;
                }
                int32 typeId
                    = Host.StandaloneSourceExpressions[17];
                if (typeId <= 0
                    || typeId > Host.StandaloneTypeFactCount[0]
                    || Host.StandaloneTypeFacts[typeId * 3 + 1] != 1) {
                    return 242;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 1
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 1
                    || Host.StandaloneNormalizedBlocks[0] != 0
                    || Host.StandaloneNormalizedBlocks[1] != 0
                    || Host.StandaloneNormalizedBlocks[2] != 0
                    || Host.StandaloneNormalizedBlocks[3] != 2
                    || Host.StandaloneNormalizedBlocks[4] != 1
                    || Host.StandaloneNormalizedBlocks[5] != 2
                    || Host.StandaloneNormalizedBlocks[6] != 2
                    || Host.StandaloneNormalizedBlocks[7] != typeId
                    || Host.StandaloneNormalizedHomeCount[0] != 3
                    || Host.StandaloneNormalizedOperationCount[0] != 2
                    || Host.StandaloneNormalizedOperations[0]
                        != {{expectedBooleanEqualityOperationKind}}
                    || Host.StandaloneNormalizedOperations[1] != 0
                    || Host.StandaloneNormalizedOperations[2] != 1
                    || Host.StandaloneNormalizedOperations[3] != 2
                    || Host.StandaloneNormalizedOperations[5] != 4
                    || Host.StandaloneNormalizedOperations[6] != 2) {
                    return 243;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                Host.StandaloneRewriteBodyIds[0] = 0;
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id] = id;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 244;
                }
                return 0;
            }
            """;
        var expectedComparisonExpressionKind = verificationMode switch
        {
            45 => 15,
            46 => 16,
            47 or 51 or 52 or 53 => 17,
            48 => 18,
            49 => 19,
            50 => 20,
            55 or 57 => 15,
            56 => 16,
            _ => 0,
        };
        var expectedComparisonOperandTypeCode = verificationMode switch
        {
            45 or 52 or 53 => 0x08,
            46 => 0x0D,
            47 => 0x09,
            48 => 0x0E,
            49 => 0x14,
            50 => 0x15,
            51 => 0x03,
            55 => 0x14,
            56 => 0x15,
            57 => 0x03,
            _ => 0,
        };
        var expectedComparisonPredicate = verificationMode switch
        {
            45 => 2,
            46 => 13,
            47 or 51 or 52 or 53 => 1,
            48 => 3,
            49 => 4,
            50 => 6,
            55 or 57 => 2,
            56 => 13,
            _ => 0,
        };
        var comparisonUsesLiteral = verificationMode is 52 or 53;
        var comparisonOperationFirst = comparisonUsesLiteral ? 2 : 0;
        var expectedComparisonOperationCount
            = comparisonUsesLiteral ? 5 : 3;
        var expectedComparisonLeftHome = verificationMode == 53 ? 1 : 0;
        var expectedComparisonRightHome = verificationMode == 53 ? 0 : 1;
        var comparisonOperandRemapping = verificationMode == 51;
        var numericComparisonVerificationSource = $$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 250; }
                int32 status = BindStandaloneOwnedSourceProgram();
                int32 rootExpressionId
                    = Host.StandaloneSourceExpressionCount[0] - 1;
                int32 rootOffset = rootExpressionId * 8;
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 3
                    || Host.StandaloneSourceExpressions[rootOffset]
                        != {{expectedComparisonExpressionKind}}
                    || Host.StandaloneSourceExpressions[
                        rootOffset + 6] != -1) {
                    return 251;
                }
                int32 resultTypeId
                    = Host.StandaloneSourceExpressions[
                        rootOffset + 1];
                int32 leftExpressionId
                    = Host.StandaloneSourceExpressions[
                        rootOffset + 2];
                int32 rightExpressionId
                    = Host.StandaloneSourceExpressions[
                        rootOffset + 3];
                int32 leftTypeId
                    = Host.StandaloneSourceExpressions[
                        leftExpressionId * 8 + 1];
                int32 rightTypeId
                    = Host.StandaloneSourceExpressions[
                        rightExpressionId * 8 + 1];
                if (resultTypeId <= 0
                    || resultTypeId > Host.StandaloneTypeFactCount[0]
                    || leftTypeId <= 0
                    || leftTypeId > Host.StandaloneTypeFactCount[0]
                    || rightTypeId != leftTypeId
                    || Host.StandaloneTypeFacts[
                        resultTypeId * 3 + 1] != 1
                    || Host.StandaloneTypeFacts[
                        leftTypeId * 3 + 1]
                        != {{expectedComparisonOperandTypeCode}}) {
                    return 252;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                int32 relationOperationOffset
                    = {{comparisonOperationFirst}} * 5;
                int32 predicateOperationOffset
                    = ({{comparisonOperationFirst}} + 1) * 5;
                if (status != 0
                    || Host.StandaloneNormalizedHomeCount[0] != 4
                    || Host.StandaloneNormalizedOperationCount[0]
                        != {{expectedComparisonOperationCount}}
                    || Host.StandaloneNormalizedOperations[
                        relationOperationOffset] != 15
                    || Host.StandaloneNormalizedOperations[
                        relationOperationOffset + 1]
                        != {{expectedComparisonLeftHome}}
                    || Host.StandaloneNormalizedOperations[
                        relationOperationOffset + 2]
                        != {{expectedComparisonRightHome}}
                    || Host.StandaloneNormalizedOperations[
                        relationOperationOffset + 3] != 2
                    || Host.StandaloneNormalizedOperations[
                        predicateOperationOffset] != 16
                    || Host.StandaloneNormalizedOperations[
                        predicateOperationOffset + 1] != 2
                    || Host.StandaloneNormalizedOperations[
                        predicateOperationOffset + 2]
                        != {{expectedComparisonPredicate}}
                    || Host.StandaloneNormalizedOperations[
                        predicateOperationOffset + 3] != 3
                    || Host.StandaloneNormalizedOperations[
                        ({{expectedComparisonOperationCount}} - 1) * 5]
                        != 4
                    || Host.StandaloneNormalizedOperations[
                        ({{expectedComparisonOperationCount}} - 1) * 5 + 1]
                        != 3) {
                    return 253;
                }
                int32 relationTypeId
                    = Host.StandaloneNormalizedHomes[2 * 5 + 3];
                if (relationTypeId <= 0
                    || relationTypeId
                        > Host.StandaloneTypeFactCount[0]
                    || Host.StandaloneTypeFacts[
                        relationTypeId * 3 + 1] != 0x0B
                    || Host.StandaloneNormalizedHomes[
                        3 * 5 + 3] != resultTypeId) {
                    return 254;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < callableCount;
                    id = id + 1) {
                    Host.StandaloneRewriteCallableIds[id] = id;
                }
                Host.StandaloneRewriteTypeIds[0] = 0;
                mut int32 boolTypeId = 0;
                mut int32 char16TypeId = 0;
                for (mut int32 id = 1;
                    id <= Host.StandaloneTypeFactCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteTypeIds[id] = id;
                    int32 typeCode
                        = Host.StandaloneTypeFacts[id * 3 + 1];
                    if (Host.StandaloneTypeFacts[id * 3] == 0
                        && typeCode == 1) {
                        boolTypeId = id;
                    }
                    if (Host.StandaloneTypeFacts[id * 3] == 0
                        && typeCode == 0x03) {
                        char16TypeId = id;
                    }
                }
                if ({{comparisonOperandRemapping ? "true" : "false"}}) {
                    if (boolTypeId <= 0 || char16TypeId <= 0) {
                        return 255;
                    }
                    Host.StandaloneRewriteTypeIds[boolTypeId]
                        = char16TypeId;
                    Host.StandaloneRewriteTypeIds[char16TypeId]
                        = boolTypeId;
                }
                Host.StandaloneRewriteBodyIds[0] = 0;
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id]
                        = {{comparisonOperandRemapping ? "true" : "false"}}
                            ? id == 0 ? 1 : id == 1 ? 0 : id
                            : id;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                if ({{comparisonOperandRemapping ? "true" : "false"}}
                    && (Host.StandaloneRewrittenSourceExpressions[
                            (Host.StandaloneRewrittenSourceBodies[3] + 2)
                                * 8 + 2] != 1
                        || Host.StandaloneRewrittenSourceExpressions[
                            (Host.StandaloneRewrittenSourceBodies[3] + 2)
                                * 8 + 3] != 0
                        || Host.StandaloneRewrittenTypeFacts[
                            char16TypeId * 3 + 1] != 1
                        || Host.StandaloneRewrittenTypeFacts[
                            boolTypeId * 3 + 1] != 0x03)) {
                    return 255;
                }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 256;
                }
                return 0;
            }
            """;
        var comparisonPrecedenceVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 260; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 5
                    || Host.StandaloneSourceExpressions[16] != 17
                    || Host.StandaloneSourceExpressions[32] != 15
                    || Host.StandaloneSourceExpressions[34] != 2
                    || Host.StandaloneSourceExpressions[35] != 3) {
                    return 261;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedHomeCount[0] != 6
                    || Host.StandaloneNormalizedOperationCount[0] != 4
                    || Host.StandaloneNormalizedOperations[0] != 15
                    || Host.StandaloneNormalizedOperations[5] != 16
                    || Host.StandaloneNormalizedOperations[6] != 3
                    || Host.StandaloneNormalizedOperations[7] != 1
                    || Host.StandaloneNormalizedOperations[10] != 13
                    || Host.StandaloneNormalizedOperations[15] != 4) {
                    return 262;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                Host.StandaloneRewriteBodyIds[0] = 0;
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id] = id;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 263;
                }
                return 0;
            }
            """;
        var unitEqualityVerificationSource = $$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 270; }
                int32 status = BindStandaloneOwnedSourceProgram();
                int32 bodyCount = {{verificationMode == 58 ? 2 : 1}};
                int32 mainBodyId = {{verificationMode == 58 ? 1 : 0}};
                int32 mainBodyOffset = mainBodyId * 12;
                int32 operationFirst
                    = Host.StandaloneNormalizedBodies[
                        mainBodyOffset + 9];
                int32 operationCount
                    = Host.StandaloneNormalizedBodies[
                        mainBodyOffset + 10];
                int32 expectedCallCount
                    = {{verificationMode == 58 ? 2 : 0}};
                int32 expectedTotalOperationCount
                    = {{verificationMode == 58 ? 6 : 3}};
                int32 expectedBooleanConstant
                    = {{verificationMode == 58 ? 1 : 0}};
                int32 expectedUnitOperandKind
                    = {{verificationMode == 58 ? 3 : 7}};
                int32 expectedUnitEqualityExpressionKind
                    = {{verificationMode == 58 ? 15 : 16}};
                if (status != 0
                    || Host.StandaloneSourceBodyCount[0] != bodyCount
                    || Host.StandaloneSourceExpressionCount[0] != 3
                    || Host.StandaloneSourceExpressions[16]
                        != expectedUnitEqualityExpressionKind
                    || Host.StandaloneSourceExpressions[0]
                        != expectedUnitOperandKind
                    || Host.StandaloneSourceExpressions[8]
                        != expectedUnitOperandKind
                    || Host.StandaloneSourceExpressions[18] != 0
                    || Host.StandaloneSourceExpressions[19] != 1
                    || Host.StandaloneInvokeCount[0]
                        != expectedCallCount
                    || Host.StandaloneNormalizedOperationCount[0]
                        != expectedTotalOperationCount
                    || Host.StandaloneNormalizedConstantCount[0] != 1
                    || Host.StandaloneNormalizedConstants[2]
                        != expectedBooleanConstant
                    || Host.StandaloneNormalizedConstants[3] != 0
                    || operationCount
                        != {{verificationMode == 58 ? 5 : 3}}) {
                    return 271;
                }
                int32 leftExpressionTypeId
                    = Host.StandaloneSourceExpressions[
                        Host.StandaloneSourceExpressions[18] * 8 + 1];
                int32 rightExpressionTypeId
                    = Host.StandaloneSourceExpressions[
                        Host.StandaloneSourceExpressions[19] * 8 + 1];
                if (leftExpressionTypeId <= 0
                    || leftExpressionTypeId
                        > Host.StandaloneTypeFactCount[0]
                    || rightExpressionTypeId != leftExpressionTypeId
                    || Host.StandaloneTypeFacts[
                        leftExpressionTypeId * 3] != 0
                    || Host.StandaloneTypeFacts[
                        leftExpressionTypeId * 3 + 1] != 0) {
                    return 272;
                }
                if ({{verificationMode == 58 ? "true" : "false"}}
                    ? (Host.StandaloneNormalizedOperations[
                            operationFirst * 5] != 3
                        || Host.StandaloneNormalizedOperations[
                            operationFirst * 5 + 1] != 0
                        || Host.StandaloneNormalizedOperations[
                            (operationFirst + 1) * 5] != 3
                        || Host.StandaloneNormalizedOperations[
                            (operationFirst + 1) * 5 + 1] != 1
                        || Host.StandaloneNormalizedOperations[
                            (operationFirst + 2) * 5] != 1
                        || Host.StandaloneNormalizedOperations[
                            (operationFirst + 3) * 5] != 2
                        || Host.StandaloneNormalizedOperations[
                            (operationFirst + 4) * 5] != 4)
                    : (Host.StandaloneNormalizedOperations[
                            operationFirst * 5] != 1
                        || Host.StandaloneNormalizedOperations[
                            (operationFirst + 1) * 5] != 2
                        || Host.StandaloneNormalizedOperations[
                            (operationFirst + 2) * 5] != 4)) {
                    return 273;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                for (mut int32 id = 0;
                    id < bodyCount;
                    id = id + 1) {
                    Host.StandaloneRewriteBodyIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id] = id;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != bodyCount) {
                    return 274;
                }
                return 0;
            }
            """;
        var expectedArithmeticExpressionKind = verificationMode switch
        {
            62 or 67 => 22,
            63 or 68 => 23,
            64 or 69 => 24,
            65 or 70 => 25,
            _ => 21,
        };
        var expectedArithmeticOperationKind = verificationMode switch
        {
            60 or 71 or 72 or 73 => 17,
            61 => 20,
            62 => 21,
            63 => 22,
            64 => 23,
            65 => 24,
            66 => 25,
            67 => 26,
            68 => 27,
            69 => 28,
            70 => 29,
            74 => 19,
            _ => 0,
        };
        var expectedArithmeticTypeCode = verificationMode switch
        {
            62 => 0x09,
            63 => 0x0D,
            65 => 0x0E,
            66 or 68 or 70 => 0x14,
            67 or 69 => 0x15,
            _ => 0x08,
        };
        var expectedArithmeticMappedTypeCode
            = verificationMode == 70 ? 0x15
                : expectedArithmeticTypeCode;
        var arithmeticRemapping = verificationMode == 70;
        var arithmeticSourceProfile
            = verificationMode is 61 or 62 or 63 ? 1 : 0;
        var arithmeticOperationCount = verificationMode switch
        {
            71 => 6,
            72 or 73 => 4,
            74 => 3,
            _ => 2,
        };
        var arithmeticOperationFirst = verificationMode switch
        {
            71 => 4,
            72 or 73 => 2,
            _ => 0,
        };
        var arithmeticConstantCount = verificationMode switch
        {
            71 => 2,
            72 or 73 => 1,
            _ => 0,
        };
        var arithmeticLeftHome = verificationMode switch
        {
            73 => 1,
            74 => 1,
            _ => 0,
        };
        var arithmeticRightHome = verificationMode switch
        {
            73 => 0,
            74 => 2,
            _ => 1,
        };
        var arithmeticResultHome = verificationMode == 74 ? 3 : 2;
        var scalarArithmeticVerificationSource = $$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 280; }
                int32 status = BindStandaloneOwnedSourceProgram();
                int32 expectedExpressionCount
                    = {{verificationMode == 74 ? 5 : 3}};
                int32 rootExpressionId
                    = Host.StandaloneSourceExpressionCount[0] - 1;
                int32 rootOffset = rootExpressionId * 8;
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0]
                        != expectedExpressionCount
                    || Host.StandaloneSourceExpressions[rootOffset]
                        != {{expectedArithmeticExpressionKind}}
                    || Host.StandaloneSourceExpressions[
                        rootOffset + 6] != -1
                    || Host.StandaloneSourceArithmeticProfile[0]
                        != {{arithmeticSourceProfile}}) {
                    return 281;
                }
                int32 rootTypeId
                    = Host.StandaloneSourceExpressions[rootOffset + 1];
                int32 leftExpressionId
                    = Host.StandaloneSourceExpressions[rootOffset + 2];
                int32 rightExpressionId
                    = Host.StandaloneSourceExpressions[rootOffset + 3];
                if (rootTypeId <= 0
                    || rootTypeId > Host.StandaloneTypeFactCount[0]
                    || Host.StandaloneTypeFacts[
                        rootTypeId * 3 + 1]
                        != {{expectedArithmeticTypeCode}}
                    || leftExpressionId < 0
                    || rightExpressionId < 0) {
                    return 282;
                }
                if ({{verificationMode == 74 ? "true" : "false"}}
                    && (Host.StandaloneSourceExpressions[
                            rightExpressionId * 8] != 23
                        || Host.StandaloneSourceExpressions[
                            rightExpressionId * 8 + 2] != 1
                        || Host.StandaloneSourceExpressions[
                            rightExpressionId * 8 + 3] != 2)) {
                    return 283;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                int32 expectedHomeCount
                    = {{verificationMode == 74 ? 5 : 3}};
                if (status != 0
                    || Host.StandaloneNormalizedHomeCount[0]
                        != expectedHomeCount
                    || Host.StandaloneNormalizedOperationCount[0]
                        != {{arithmeticOperationCount}}
                    || Host.StandaloneNormalizedConstantCount[0]
                        != {{arithmeticConstantCount}}) {
                    return 284;
                }
                int32 arithmeticOffset
                    = {{arithmeticOperationFirst}} * 5;
                if (Host.StandaloneNormalizedOperations[
                        arithmeticOffset]
                        != {{expectedArithmeticOperationKind}}
                    || Host.StandaloneNormalizedOperations[
                        arithmeticOffset + 1]
                        != {{arithmeticLeftHome}}
                    || Host.StandaloneNormalizedOperations[
                        arithmeticOffset + 2]
                        != {{arithmeticRightHome}}
                    || Host.StandaloneNormalizedOperations[
                        arithmeticOffset + 3]
                        != {{arithmeticResultHome}}
                    || Host.StandaloneNormalizedOperations[
                        ({{arithmeticOperationCount}} - 1) * 5] != 4
                    || Host.StandaloneNormalizedOperations[
                        ({{arithmeticOperationCount}} - 1) * 5 + 1]
                        != {{verificationMode == 74 ? 4 : 2}}) {
                    return 285;
                }
                if ({{verificationMode == 74 ? "true" : "false"}}
                    && (Host.StandaloneNormalizedOperations[5] != 17
                        || Host.StandaloneNormalizedOperations[6] != 0
                        || Host.StandaloneNormalizedOperations[7] != 3
                        || Host.StandaloneNormalizedOperations[8] != 4)) {
                    return 286;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < callableCount;
                    id = id + 1) {
                    Host.StandaloneRewriteCallableIds[id] = id;
                }
                Host.StandaloneRewriteTypeIds[0] = 0;
                mut int32 float32TypeId = 0;
                mut int32 float64TypeId = 0;
                for (mut int32 id = 1;
                    id <= Host.StandaloneTypeFactCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteTypeIds[id] = id;
                    if (Host.StandaloneTypeFacts[id * 3] == 0
                        && Host.StandaloneTypeFacts[id * 3 + 1] == 0x14) {
                        float32TypeId = id;
                    }
                    if (Host.StandaloneTypeFacts[id * 3] == 0
                        && Host.StandaloneTypeFacts[id * 3 + 1] == 0x15) {
                        float64TypeId = id;
                    }
                }
                if ({{arithmeticRemapping ? "true" : "false"}}) {
                    if (float32TypeId <= 0 || float64TypeId <= 0) {
                        return 287;
                    }
                    Host.StandaloneRewriteTypeIds[float32TypeId]
                        = float64TypeId;
                    Host.StandaloneRewriteTypeIds[float64TypeId]
                        = float32TypeId;
                }
                Host.StandaloneRewriteBodyIds[0] = 0;
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id] = id;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                if ({{arithmeticRemapping ? "true" : "false"}}
                    && Host.StandaloneRewrittenTypeFacts[
                        Host.StandaloneRewrittenNormalizedHomes[
                            2 * 5 + 3] * 3 + 1]
                        != {{expectedArithmeticMappedTypeCode}}) {
                    return 288;
                }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 289;
                }
                return 0;
            }
            """;
        var mutableCompoundExpressionKind = verificationMode switch
        {
            97 => 22,
            98 => 23,
            99 => 24,
            100 => 25,
            101 => 12,
            102 => 13,
            103 => 14,
            _ => 21,
        };
        var mutableCompoundOperationKind = verificationMode switch
        {
            97 => 18,
            98 => 19,
            99 => 23,
            100 => 24,
            101 => 10,
            102 => 11,
            103 => 12,
            _ => 17,
        };
        var mutableLocalVerificationSource = $$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 290; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceLocalCount[0] != 1
                    || Host.StandaloneSourcePlaceCount[0] != 3
                    || Host.StandaloneSourceExpressionCount[0] != 5
                    || Host.StandaloneSourceStatementCount[0] != 3
                    || Host.StandaloneSourceLocals[3] != 1
                    || Host.StandaloneSourceLocalStates[0] != 1
                    || Host.StandaloneSourceStatements[3] != 5
                    || Host.StandaloneSourceStatements[5] != 0
                    || Host.StandaloneSourceExpressions[24]
                        != {{mutableCompoundExpressionKind}}
                    || Host.StandaloneSourceExpressions[32] != 5) {
                    return 291;
                }
                int32 typeId = Host.StandaloneSourceLocals[2];
                if (typeId <= 0
                    || typeId > Host.StandaloneTypeFactCount[0]
                    || Host.StandaloneTypeFacts[typeId * 3 + 1] != 0x08) {
                    return 292;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedHomeCount[0] != 6
                    || Host.StandaloneNormalizedOperationCount[0] != 8
                    || Host.StandaloneNormalizedConstantCount[0] != 1
                    || Host.StandaloneNormalizedSourceLocalHomeIds[0] != 1
                    || Host.StandaloneNormalizedHomes[6] != 3
                    || Host.StandaloneNormalizedHomes[7] != 1
                    || Host.StandaloneNormalizedHomes[8] != typeId) {
                    return 293;
                }
                if (Host.StandaloneNormalizedOperations[0] != 30
                    || Host.StandaloneNormalizedOperations[1] != 0
                    || Host.StandaloneNormalizedOperations[2] != 1
                    || Host.StandaloneNormalizedOperations[3] != 0
                    || Host.StandaloneNormalizedOperations[5] != 31
                    || Host.StandaloneNormalizedOperations[6] != 1
                    || Host.StandaloneNormalizedOperations[7] != 2
                    || Host.StandaloneNormalizedOperations[8] != 0
                    || Host.StandaloneNormalizedOperations[20]
                        != {{mutableCompoundOperationKind}}
                    || Host.StandaloneNormalizedOperations[21] != 2
                    || Host.StandaloneNormalizedOperations[22] != 3
                    || Host.StandaloneNormalizedOperations[23] != 4
                    || Host.StandaloneNormalizedOperations[25] != 30
                    || Host.StandaloneNormalizedOperations[26] != 4
                    || Host.StandaloneNormalizedOperations[27] != 1
                    || Host.StandaloneNormalizedOperations[28] != 0
                    || Host.StandaloneNormalizedOperations[30] != 31
                    || Host.StandaloneNormalizedOperations[31] != 1
                    || Host.StandaloneNormalizedOperations[32] != 5
                    || Host.StandaloneNormalizedOperations[33] != 0
                    || Host.StandaloneNormalizedOperations[35] != 4
                    || Host.StandaloneNormalizedOperations[36] != 5) {
                    return 294;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                Host.StandaloneRewriteBodyIds[0] = 0;
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceLexicalScopeCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteScopeIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceLocalCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteLocalIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < Host.StandaloneSourcePlaceCount[0];
                    id = id + 1) {
                    Host.StandaloneRewritePlaceIds[id]
                        = (id + 1)
                            % Host.StandaloneSourcePlaceCount[0];
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0
                    || Host.StandaloneRewrittenSourceStatements[5] != 1
                    || Host.StandaloneRewrittenSourceExpressions[10] != 2
                    || Host.StandaloneRewrittenSourceExpressions[34] != 0) {
                    return 295;
                }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 296;
                }
                return 0;
            }
            """;
        var updateExpressionKind = verificationMode switch
        {
            104 => 27,
            105 => 29,
            106 => 28,
            108 or 109 => 27,
            _ => 30,
        };
        var updateOperationKind = verificationMode is 106 or 107
            ? 18 : verificationMode == 108 ? 20
                : verificationMode == 109 ? 25 : 17;
        var updateResultHome = verificationMode is 104 or 106 or 108 or 109
            ? 4 : 2;
        var updateTypeCode = verificationMode == 109 ? 0x14 : 0x08;
        var updateConstantLow = verificationMode == 109
            ? 0x3F800000 : 1;
        var updateSourceProfile = verificationMode == 108 ? 1 : 0;
        var updateExpressionVerificationSource = $$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 297; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceLocalCount[0] != 1
                    || Host.StandaloneSourcePlaceCount[0] != 2
                    || Host.StandaloneSourceExpressionCount[0] != 3
                    || Host.StandaloneSourceStatementCount[0] != 2
                    || Host.StandaloneSourceLocals[3] != 1
                    || Host.StandaloneSourceLocalStates[0] != 1
                    || Host.StandaloneSourceExpressions[16]
                        != {{updateExpressionKind}}
                    || Host.StandaloneSourceExpressions[18] != 1
                    || Host.StandaloneSourceExpressions[19] != 1
                    || Host.StandaloneSourceExpressions[8] != 5
                    || Host.StandaloneSourceExpressions[10] != 0
                    || Host.StandaloneSourceArithmeticProfile[0]
                        != {{updateSourceProfile}}
                    || Host.StandaloneSourcePlaces[2] != 1
                    || Host.StandaloneSourcePlaces[7] != 2
                    || Host.StandaloneSourcePlaces[8] != 0) {
                    return 298;
                }
                int32 typeId = Host.StandaloneSourceLocals[2];
                if (typeId <= 0
                    || typeId > Host.StandaloneTypeFactCount[0]
                    || Host.StandaloneTypeFacts[typeId * 3 + 1]
                        != {{updateTypeCode}}) {
                    return 299;
                }
                mut int32 updateTokenIndex = -1;
                for (mut int32 tokenIndex = 0;
                    tokenIndex < Host.Tokens.Length / 3;
                    tokenIndex = tokenIndex + 1) {
                    int32 tokenKind = Host.Tokens[tokenIndex * 3];
                    if (tokenKind == 86 || tokenKind == 87) {
                        if (updateTokenIndex >= 0) { return 303; }
                        updateTokenIndex = tokenIndex;
                    }
                }
                if (updateTokenIndex < 0
                    || !CanBindStandaloneSourceArgument(
                        0,
                        Host.Tokens[updateTokenIndex * 3] == 86
                            ? updateTokenIndex
                            : updateTokenIndex - 1,
                        Host.Tokens[updateTokenIndex * 3] == 86
                            ? updateTokenIndex + 2
                            : updateTokenIndex + 1,
                        typeId, 0)) {
                    return 304;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedHomeCount[0] != 5
                    || Host.StandaloneNormalizedOperationCount[0] != 7
                    || Host.StandaloneNormalizedConstantCount[0] != 1
                    || Host.StandaloneNormalizedSourceLocalHomeIds[0] != 1
                    || Host.StandaloneNormalizedConstants[2]
                        != {{updateConstantLow}}
                    || Host.StandaloneNormalizedConstants[3] != 0
                    || Host.StandaloneNormalizedOperations[20]
                        != {{updateOperationKind}}
                    || Host.StandaloneNormalizedOperations[21] != 2
                    || Host.StandaloneNormalizedOperations[22] != 3
                    || Host.StandaloneNormalizedOperations[23] != 4
                    || Host.StandaloneNormalizedOperations[25] != 30
                    || Host.StandaloneNormalizedOperations[26] != 4
                    || Host.StandaloneNormalizedOperations[27] != 1
                    || Host.StandaloneNormalizedOperations[30] != 4
                    || Host.StandaloneNormalizedOperations[31]
                        != {{updateResultHome}}) {
                    return 300;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                Host.StandaloneRewriteBodyIds[0] = 0;
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceLexicalScopeCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteScopeIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceLocalCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteLocalIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < Host.StandaloneSourcePlaceCount[0];
                    id = id + 1) {
                    Host.StandaloneRewritePlaceIds[id]
                        = (id + 1)
                            % Host.StandaloneSourcePlaceCount[0];
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0
                    || Host.StandaloneRewrittenSourceExpressions[10] != 1
                    || Host.StandaloneRewrittenSourceExpressions[19] != 0) {
                    return 301;
                }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 302;
                }
                return 0;
            }
            """;
        var shiftExpressionKind = verificationMode switch
        {
            111 => 31,
            112 or 114 or 116 or 117 => 32,
            115 => 31,
            122 => 31,
            123 or 125 => 32,
            124 => 33,
            _ => 33,
        };
        var shiftOperationKind = verificationMode switch
        {
            111 => 35,
            112 or 114 or 116 or 117 => 36,
            115 => 35,
            122 => 35,
            123 or 125 => 36,
            124 => 37,
            _ => 37,
        };
        var shiftControl = verificationMode switch
        {
            111 => 0x05,
            112 or 116 => 0x06,
            115 => 0x05,
            122 => 0x05,
            123 => 0x06,
            _ => 0x07,
        };
        var shiftDataTypeCode = verificationMode switch
        {
            114 => 0x0D,
            116 => 0x09,
            117 => 0x0E,
            123 => 0x08,
            124 => 0x09,
            125 => 0x0E,
            _ => 0x08,
        };
        var shiftCountTypeCode = verificationMode is 113 or 114 or 117
            ? 0x0D
            : verificationMode is 122 or 124 ? 0x09
            : verificationMode is 123 or 125 ? 0x0E
            : 0x08;
        var shiftNeedsReinterpret
            = verificationMode is 111 or 112 or 115 or 116;
        var shiftRightExpressionKind
            = verificationMode == 115 ? 2 : 1;
        var shiftOperationFirst = verificationMode == 115 ? 2 : 0;
        var shiftOperationCount = verificationMode == 115 ? 4 : 2;
        var shiftConstantCount = verificationMode == 115 ? 1 : 0;
        var shiftReturnOperationOffset
            = (shiftOperationCount - 1) * 5;
        var shiftVerificationSource = $$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 305; }
                int32 status = BindStandaloneOwnedSourceProgram();
                int32 expressionOffset = 16;
                int32 dataTypeId
                    = Host.StandaloneSourceExpressions[
                        expressionOffset + 1];
                int32 leftExpressionId
                    = Host.StandaloneSourceExpressions[
                        expressionOffset + 2];
                int32 rightExpressionId
                    = Host.StandaloneSourceExpressions[
                        expressionOffset + 3];
                if (status != 0
                    || Host.StandaloneSourceBodyCount[0] != 1
                    || Host.StandaloneSourceLocalCount[0] != 0
                    || Host.StandaloneSourcePlaceCount[0] != 0
                    || Host.StandaloneSourceExpressionCount[0] != 3
                    || Host.StandaloneSourceStatementCount[0] != 1
                    || Host.StandaloneSourceExpressions[
                        expressionOffset] != {{shiftExpressionKind}}
                    || leftExpressionId != 0
                    || rightExpressionId != 1
                    || Host.StandaloneSourceExpressions[0] != 1
                    || Host.StandaloneSourceExpressions[8]
                        != {{shiftRightExpressionKind}}
                    || dataTypeId <= 0
                    || dataTypeId > Host.StandaloneTypeFactCount[0]
                    || Host.StandaloneTypeFacts[
                        dataTypeId * 3 + 1]
                        != {{shiftDataTypeCode}}
                    || Host.StandaloneTypeFacts[
                        Host.StandaloneSourceExpressions[9] * 3 + 1]
                        != {{shiftCountTypeCode}}) {
                    return 306;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 1
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 1
                    || Host.StandaloneNormalizedBlocks[0] != 0
                    || Host.StandaloneNormalizedBlocks[1] != 0
                    || Host.StandaloneNormalizedBlocks[2] != 0
                    || Host.StandaloneNormalizedBlocks[3]
                        != {{shiftOperationCount}}
                    || Host.StandaloneNormalizedBlocks[4] != 1
                    || Host.StandaloneNormalizedBlocks[5] != 2
                    || Host.StandaloneNormalizedBlocks[6] != 2
                    || Host.StandaloneNormalizedBlocks[7] != dataTypeId
                    || Host.StandaloneNormalizedHomeCount[0] != 3
                    || Host.StandaloneNormalizedHomes[8] <= 0
                    || Host.StandaloneTypeFacts[
                        Host.StandaloneNormalizedHomes[8] * 3 + 1]
                        != {{shiftCountTypeCode}}
                    || Host.StandaloneNormalizedOperationCount[0]
                        != {{shiftOperationCount}}
                    || Host.StandaloneNormalizedConstantCount[0]
                        != {{shiftConstantCount}}
                    || Host.StandaloneNormalizedOperations[
                        {{shiftOperationFirst}} * 5]
                        != {{shiftOperationKind}}
                    || Host.StandaloneNormalizedOperations[
                        {{shiftOperationFirst}} * 5 + 1] != 0
                    || Host.StandaloneNormalizedOperations[
                        {{shiftOperationFirst}} * 5 + 2] != 1
                    || Host.StandaloneNormalizedOperations[
                        {{shiftOperationFirst}} * 5 + 3] != 2
                    || Host.StandaloneNormalizedOperations[
                        {{shiftReturnOperationOffset}}] != 4
                    || Host.StandaloneNormalizedOperations[
                        {{shiftReturnOperationOffset + 1}}] != 2) {
                    return 307;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                Host.StandaloneRewriteBodyIds[0] = 0;
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceLexicalScopeCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteScopeIds[id] = id;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 308;
                }
                return 0;
            }
            """;
        var shiftCompoundExpressionKind = verificationMode switch
        {
            118 or 126 => 31,
            119 or 121 or 127 or 129 => 32,
            128 => 33,
            _ => 33,
        };
        var shiftCompoundOperationKind = verificationMode switch
        {
            118 or 126 => 35,
            119 or 121 or 127 or 129 => 36,
            128 => 37,
            _ => 37,
        };
        var shiftCompoundControl = verificationMode switch
        {
            118 or 126 => 0x05,
            119 or 127 => 0x06,
            _ => 0x07,
        };
        var shiftCompoundDataTypeCode = verificationMode switch
        {
            121 => 0x0D,
            128 => 0x09,
            129 => 0x0E,
            _ => 0x08,
        };
        var shiftCompoundCountTypeCode = verificationMode is 120 or 121
            ? 0x0D
            : verificationMode is 126 or 128 ? 0x09
            : verificationMode is 127 or 129 ? 0x0E
            : 0x08;
        var shiftCompoundNeedsReinterpret
            = verificationMode is 118 or 119;
        var shiftCompoundVerificationSource = $$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 309; }
                int32 status = BindStandaloneOwnedSourceProgram();
                int32 expressionOffset = 24;
                int32 dataTypeId
                    = Host.StandaloneSourceExpressions[
                        expressionOffset + 1];
                int32 leftExpressionId
                    = Host.StandaloneSourceExpressions[
                        expressionOffset + 2];
                int32 rightExpressionId
                    = Host.StandaloneSourceExpressions[
                        expressionOffset + 3];
                if (status != 0
                    || Host.StandaloneSourceBodyCount[0] != 1
                    || Host.StandaloneSourceLocalCount[0] != 1
                    || Host.StandaloneSourcePlaceCount[0] != 3
                    || Host.StandaloneSourceExpressionCount[0] != 5
                    || Host.StandaloneSourceStatementCount[0] != 3
                    || Host.StandaloneSourceExpressions[
                        expressionOffset] != {{shiftCompoundExpressionKind}}
                    || leftExpressionId != 1
                    || rightExpressionId != 2
                    || Host.StandaloneSourceExpressions[8] != 5
                    || Host.StandaloneSourceExpressions[10] != 1
                    || Host.StandaloneSourceExpressions[16] != 1
                    || dataTypeId <= 0
                    || dataTypeId > Host.StandaloneTypeFactCount[0]
                    || Host.StandaloneTypeFacts[
                        dataTypeId * 3 + 1]
                        != {{shiftCompoundDataTypeCode}}
                    || Host.StandaloneTypeFacts[
                        Host.StandaloneSourceExpressions[17] * 3 + 1]
                        != {{shiftCompoundCountTypeCode}}) {
                    return 310;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedHomeCount[0] != 6
                    || Host.StandaloneNormalizedOperationCount[0] != 6
                    || Host.StandaloneNormalizedConstantCount[0] != 0
                    || Host.StandaloneNormalizedSourceLocalHomeIds[0] != 2
                    || Host.StandaloneNormalizedOperations[10]
                        != {{shiftCompoundOperationKind}}
                    || Host.StandaloneNormalizedOperations[11] != 3
                    || Host.StandaloneNormalizedOperations[12] != 1
                    || Host.StandaloneNormalizedOperations[13] != 4
                    || Host.StandaloneNormalizedOperations[15] != 30
                    || Host.StandaloneNormalizedOperations[16] != 4
                    || Host.StandaloneNormalizedOperations[17] != 2
                    || Host.StandaloneNormalizedOperations[25] != 4
                    || Host.StandaloneNormalizedOperations[26] != 5) {
                    return 311;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                Host.StandaloneRewriteBodyIds[0] = 0;
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceLexicalScopeCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteScopeIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceLocalCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteLocalIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < Host.StandaloneSourcePlaceCount[0];
                    id = id + 1) {
                    Host.StandaloneRewritePlaceIds[id]
                        = (id + 1)
                            % Host.StandaloneSourcePlaceCount[0];
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0
                    || Host.StandaloneRewrittenSourceStatements[5] != 1
                    || Host.StandaloneRewrittenSourceExpressions[10] != 2) {
                    return 312;
                }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 313;
                }
                return 0;
            }
            """;
        var initializationLocalVerificationSource = $$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 300; }
                int32 status = BindStandaloneOwnedSourceProgram();
                int32 isReplaceable = {{verificationMode == 82 ? 1 : 0}};
                int32 expectedBindingKind
                    = isReplaceable == 1 ? 3 : 2;
                int32 expectedExpressionCount
                    = isReplaceable == 1 ? 5 : 2;
                int32 expectedStatementCount
                    = isReplaceable == 1 ? 4 : 3;
                int32 expectedPlaceCount
                    = isReplaceable == 1 ? 4 : 2;
                if (status != 0
                    || Host.StandaloneSourceLocalCount[0] != 1
                    || Host.StandaloneSourcePlaceCount[0]
                        != expectedPlaceCount
                    || Host.StandaloneSourceExpressionCount[0]
                        != expectedExpressionCount
                    || Host.StandaloneSourceStatementCount[0]
                        != expectedStatementCount
                    || Host.StandaloneSourceLocals[3]
                        != expectedBindingKind
                    || Host.StandaloneSourceLocalStates[0] != 1
                    || Host.StandaloneSourceStatements[0] != 4
                    || Host.StandaloneSourceStatements[3] != 5) {
                    return 301;
                }
                int32 typeId = Host.StandaloneSourceLocals[2];
                if (typeId <= 0
                    || typeId > Host.StandaloneTypeFactCount[0]
                    || Host.StandaloneTypeFacts[typeId * 3 + 1] != 0x08) {
                    return 302;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                int32 expectedHomeCount
                    = isReplaceable == 1 ? 6 : 2;
                int32 expectedOperationCount
                    = isReplaceable == 1 ? 8 : 2;
                int32 expectedConstantCount
                    = isReplaceable == 1 ? 1 : 0;
                if (status != 0
                    || Host.StandaloneNormalizedHomeCount[0]
                        != expectedHomeCount
                    || Host.StandaloneNormalizedOperationCount[0]
                        != expectedOperationCount
                    || Host.StandaloneNormalizedConstantCount[0]
                        != expectedConstantCount
                    || Host.StandaloneNormalizedSourceLocalHomeIds[0]
                        != 1
                    || Host.StandaloneNormalizedHomes[6] != 3
                    || Host.StandaloneNormalizedHomes[7]
                        != (isReplaceable == 1 ? 1 : 0)
                    || Host.StandaloneNormalizedHomes[8] != typeId) {
                    return 303;
                }
                if (Host.StandaloneNormalizedOperations[0] != 30
                    || Host.StandaloneNormalizedOperations[1] != 0
                    || Host.StandaloneNormalizedOperations[2] != 1
                    || Host.StandaloneNormalizedOperations[3] != 0
                    || Host.StandaloneNormalizedOperations[
                        (expectedOperationCount - 1) * 5] != 4
                    || Host.StandaloneNormalizedOperations[
                        (expectedOperationCount - 1) * 5 + 1]
                        != (isReplaceable == 1 ? 5 : 1)) {
                    return 304;
                }
                if (isReplaceable == 1
                    && (Host.StandaloneNormalizedOperations[5] != 31
                        || Host.StandaloneNormalizedOperations[6] != 1
                        || Host.StandaloneNormalizedOperations[7] != 2
                        || Host.StandaloneNormalizedOperations[8] != 0
                        || Host.StandaloneNormalizedOperations[20] != 17
                        || Host.StandaloneNormalizedOperations[21] != 2
                        || Host.StandaloneNormalizedOperations[22] != 3
                        || Host.StandaloneNormalizedOperations[23] != 4
                        || Host.StandaloneNormalizedOperations[25] != 30
                        || Host.StandaloneNormalizedOperations[26] != 4
                        || Host.StandaloneNormalizedOperations[27] != 1
                        || Host.StandaloneNormalizedOperations[28] != 0
                        || Host.StandaloneNormalizedOperations[30] != 31
                        || Host.StandaloneNormalizedOperations[31] != 1
                        || Host.StandaloneNormalizedOperations[32] != 5)) {
                    return 305;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                Host.StandaloneRewriteBodyIds[0] = 0;
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceLexicalScopeCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteScopeIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceLocalCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteLocalIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < Host.StandaloneSourcePlaceCount[0];
                    id = id + 1) {
                    Host.StandaloneRewritePlaceIds[id] = id;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 306;
                }
                return 0;
            }
            """;
        var initTestHasLocal = verificationMode is >= 90 and <= 93;
        var initTestHasParameter = verificationMode is 91 or 94 or 95;
        var initTestHasAssignment = verificationMode is 91 or 93;
        var initTestState = verificationMode is 91 or 93 or 94 or 95 ? 1 : 0;
        var initTestExpectedLocalTypeKind = verificationMode == 95 ? 1 : 0;
        var initTestExpectedLocalTypeCode = verificationMode switch
        {
            90 or 91 or 95 => 0x08,
            94 => 1,
            _ => 0,
        };
        var localInitTestVerificationSource = $$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 310; }
                int32 status = BindStandaloneOwnedSourceProgram();
                int32 expectedLocalCount
                    = {{initTestHasLocal ? 1 : 0}};
                int32 expectedExpressionCount
                    = {{initTestHasAssignment ? 2 : 1}};
                int32 expectedStatementCount
                    = {{initTestHasAssignment ? 3
                        : initTestHasParameter ? 1 : 2}};
                int32 expectedPlaceCount
                    = {{initTestHasAssignment ? 2 : 1}};
                int32 expectedHomeCount
                    = {{verificationMode == 91 ? 3
                        : initTestHasParameter ? 2 : 1}};
                int32 expectedOperationCount
                    = {{verificationMode == 91 ? 4 : 3}};
                int32 testExpressionId
                    = expectedExpressionCount - 1;
                int32 testExpressionOffset = testExpressionId * 8;
                int32 stateAtTest = {{initTestState}};
                int32 expectedBoolean
                    = stateAtTest == 0 ? 1 : 0;
                if (status != 0
                    || Host.StandaloneSourceLocalCount[0]
                        != expectedLocalCount
                    || Host.StandaloneSourceExpressionCount[0]
                        != expectedExpressionCount
                    || Host.StandaloneSourceStatementCount[0]
                        != expectedStatementCount
                    || Host.StandaloneSourcePlaceCount[0]
                        != expectedPlaceCount
                    || Host.StandaloneSourceExpressions[
                        testExpressionOffset] != 26
                    || Host.StandaloneSourceExpressions[
                        testExpressionOffset + 3] != stateAtTest) {
                    return 311;
                }
                int32 testPlaceId
                    = Host.StandaloneSourceExpressions[
                        testExpressionOffset + 2];
                int32 placeOffset = testPlaceId * 5;
                int32 placeTypeId
                    = Host.StandaloneSourcePlaces[placeOffset + 1];
                int32 expectedPlaceKind
                    = {{initTestHasParameter ? 4 : 3}};
                int32 resultTypeId
                    = Host.StandaloneSourceExpressions[
                        testExpressionOffset + 1];
                if (testPlaceId < 0
                    || testPlaceId
                        >= Host.StandaloneSourcePlaceCount[0]
                    || Host.StandaloneSourcePlaces[
                        placeOffset + 2] != expectedPlaceKind
                    || Host.StandaloneSourcePlaces[
                        placeOffset + 3] != 0
                    || placeTypeId <= 0
                    || placeTypeId
                        > Host.StandaloneTypeFactCount[0]
                    || Host.StandaloneTypeFacts[
                        placeTypeId * 3]
                        != {{initTestExpectedLocalTypeKind}}
                    || Host.StandaloneTypeFacts[
                        placeTypeId * 3 + 1]
                        != {{initTestExpectedLocalTypeCode}}
                    || resultTypeId <= 0
                    || resultTypeId
                        > Host.StandaloneTypeFactCount[0]
                    || Host.StandaloneTypeFacts[
                        resultTypeId * 3 + 1] != 1) {
                    return 312;
                }
                if (expectedLocalCount == 1
                    && (Host.StandaloneSourceLocalStates[0]
                            != stateAtTest
                        || Host.StandaloneSourceLocals[2]
                            != placeTypeId)) {
                    return 313;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                int32 expectedLocalHome
                    = {{verificationMode == 91 ? 1
                        : verificationMode == 93 ? 0 : -1}};
                int32 expectedResultHome
                    = {{verificationMode == 91 ? 2
                        : initTestHasParameter ? 1 : 0}};
                if (status != 0
                    || Host.StandaloneNormalizedHomeCount[0]
                        != expectedHomeCount
                    || Host.StandaloneNormalizedOperationCount[0]
                        != expectedOperationCount
                    || Host.StandaloneNormalizedConstantCount[0] != 1
                    || (expectedLocalCount == 1
                        && Host.StandaloneNormalizedSourceLocalHomeIds[0]
                            != expectedLocalHome)
                    || Host.StandaloneNormalizedConstants[2]
                        != expectedBoolean
                    || Host.StandaloneNormalizedConstants[3] != 0
                    || Host.StandaloneNormalizedOperations[
                        (expectedOperationCount - 1) * 5] != 4
                    || Host.StandaloneNormalizedOperations[
                        (expectedOperationCount - 1) * 5 + 1]
                        != expectedResultHome) {
                    return 314;
                }
                if (verificationMode == 91
                    && (Host.StandaloneNormalizedOperations[0] != 30
                        || Host.StandaloneNormalizedOperations[1] != 0
                        || Host.StandaloneNormalizedOperations[2] != 1
                        || Host.StandaloneNormalizedOperations[3] != 0
                        || Host.StandaloneNormalizedOperations[5] != 1
                        || Host.StandaloneNormalizedOperations[10] != 2)) {
                    return 315;
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
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceImportedCallableCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteImportedCallableIds[id] = id;
                }
                for (mut int32 id = 0;
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
                Host.StandaloneRewriteBodyIds[0] = 0;
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceExpressionCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteExpressionIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceLexicalScopeCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteScopeIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < Host.StandaloneSourceLocalCount[0];
                    id = id + 1) {
                    Host.StandaloneRewriteLocalIds[id] = id;
                }
                for (mut int32 id = 0;
                    id < Host.StandaloneSourcePlaceCount[0];
                    id = id + 1) {
                    Host.StandaloneRewritePlaceIds[id] = id;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 316;
                }
                return 0;
            }
            """;
        const string multiBlockControlFlowVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 80; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0) { return status; }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0) { return status; }
                if (Host.StandaloneNormalizedBlockCount[0] != 3
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 3
                    || Host.StandaloneNormalizedBlocks[0] != 0
                    || Host.StandaloneNormalizedBlocks[1] != 0
                    || Host.StandaloneNormalizedBlocks[4] != 4
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9] != 2
                    || Host.StandaloneNormalizedBlocks[13] != 3
                    || Host.StandaloneNormalizedBlocks[15] != 0
                    || Host.StandaloneNormalizedBlocks[16] != 0
                    || Host.StandaloneNormalizedBlocks[17] != 1
                    || Host.StandaloneNormalizedBlocks[20] != 1
                    || Host.StandaloneNormalizedBlocks[32] != 0
                    || Host.StandaloneNormalizedBlocks[33] != 2
                    || Host.StandaloneNormalizedBlocks[36] != 1) {
                    return 81;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 82;
                }
                return 0;
            }
            """;
        const string conditionalValueJoinVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 83; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 4
                    || Host.StandaloneSourceExpressions[24] != 34
                    || Host.StandaloneSourceExpressions[26] != 0
                    || Host.StandaloneSourceExpressions[27] != 1
                    || Host.StandaloneSourceExpressions[28] != 2) {
                    return 84;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 4
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 4
                    || Host.StandaloneNormalizedHomeCount[0] != 4
                    || Host.StandaloneNormalizedOperationCount[0] != 3
                    || Host.StandaloneNormalizedBlocks[4] != 4
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9] != 2
                    || Host.StandaloneNormalizedBlocks[13] != 4
                    || Host.StandaloneNormalizedBlocks[20] != 3
                    || Host.StandaloneNormalizedBlocks[24] != 3
                    || Host.StandaloneNormalizedBlocks[36] != 3
                    || Host.StandaloneNormalizedBlocks[40] != 3
                    || Host.StandaloneNormalizedBlocks[52] != 1
                    || Host.StandaloneNormalizedBlocks[54] != 3
                    || Host.StandaloneNormalizedOperations[0] != 38
                    || Host.StandaloneNormalizedOperations[1] != 1
                    || Host.StandaloneNormalizedOperations[2] != 3
                    || Host.StandaloneNormalizedOperations[5] != 38
                    || Host.StandaloneNormalizedOperations[6] != 2
                    || Host.StandaloneNormalizedOperations[7] != 3
                    || Host.StandaloneNormalizedOperations[10] != 4
                    || Host.StandaloneNormalizedOperations[11] != 3) {
                    return 85;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 86;
                }
                return 0;
            }
            """;
        const string lazyBooleanVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                bool isAnd
                    = Host.StandaloneSourceExpressions[16] == 35;
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 3
                    || (!isAnd
                        && Host.StandaloneSourceExpressions[16] != 36)
                    || Host.StandaloneSourceExpressions[18] != 0
                    || Host.StandaloneSourceExpressions[19] != 1) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 4
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 4
                    || Host.StandaloneNormalizedHomeCount[0] != 3
                    || Host.StandaloneNormalizedOperationCount[0] != 4
                    || Host.StandaloneNormalizedConstantCount[0] != 1
                    || Host.StandaloneNormalizedBlocks[4] != 4
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9] != 2
                    || Host.StandaloneNormalizedBlocks[13] != 4
                    || Host.StandaloneNormalizedBlocks[20] != 3
                    || Host.StandaloneNormalizedBlocks[24] != 3
                    || Host.StandaloneNormalizedBlocks[36] != 3
                    || Host.StandaloneNormalizedBlocks[40] != 3
                    || Host.StandaloneNormalizedBlocks[52] != 1
                    || Host.StandaloneNormalizedBlocks[54] != 2
                    || (isAnd
                        ? Host.StandaloneNormalizedBlocks[19] != 1
                            || Host.StandaloneNormalizedBlocks[35] != 2
                        : Host.StandaloneNormalizedBlocks[19] != 2
                            || Host.StandaloneNormalizedBlocks[35] != 1)
                    || Host.StandaloneNormalizedOperations[15] != 4
                    || Host.StandaloneNormalizedOperations[16] != 2
                    || Host.StandaloneTypeFacts[
                        Host.StandaloneNormalizedConstants[1] * 3 + 1]
                        != 1
                    || Host.StandaloneNormalizedConstants[2]
                        != (isAnd ? 0 : 1)) {
                    return 89;
                }
                if (isAnd
                    ? (Host.StandaloneNormalizedOperations[0] != 38
                        || Host.StandaloneNormalizedOperations[1] != 1
                        || Host.StandaloneNormalizedOperations[2] != 2
                        || Host.StandaloneNormalizedOperations[5] != 1
                        || Host.StandaloneNormalizedOperations[10] != 2
                        || Host.StandaloneNormalizedOperations[11] != 2)
                    : (Host.StandaloneNormalizedOperations[0] != 1
                        || Host.StandaloneNormalizedOperations[5] != 2
                        || Host.StandaloneNormalizedOperations[6] != 2
                        || Host.StandaloneNormalizedOperations[10] != 38
                        || Host.StandaloneNormalizedOperations[11] != 1
                        || Host.StandaloneNormalizedOperations[12] != 2)) {
                    return 90;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 91;
                }
                return 0;
            }
            """;
        const string lazyBooleanChainVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                bool isAnd
                    = Host.StandaloneSourceExpressions[32] == 35;
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 5
                    || (!isAnd
                        && Host.StandaloneSourceExpressions[32] != 36)) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 5
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 5
                    || Host.StandaloneNormalizedHomeCount[0] != 4
                    || Host.StandaloneNormalizedOperationCount[0] != 4
                    || Host.StandaloneNormalizedConstantCount[0] != 1
                    || Host.StandaloneNormalizedBlocks[13] != 5
                    || Host.StandaloneNormalizedBlocks[29] != 6
                    || (isAnd
                        ? (Host.StandaloneNormalizedBlocks[8] != 1
                            || Host.StandaloneNormalizedBlocks[9] != 3
                            || Host.StandaloneNormalizedBlocks[24] != 2
                            || Host.StandaloneNormalizedBlocks[25] != 3
                            || Host.StandaloneNormalizedBlocks[35] != 1
                            || Host.StandaloneNormalizedBlocks[51] != 2)
                        : (Host.StandaloneNormalizedBlocks[8] != 2
                            || Host.StandaloneNormalizedBlocks[9] != 1
                            || Host.StandaloneNormalizedBlocks[24] != 2
                            || Host.StandaloneNormalizedBlocks[25] != 3
                            || Host.StandaloneNormalizedBlocks[35] != 2
                            || Host.StandaloneNormalizedBlocks[51] != 1))) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 90;
                }
                return 0;
            }
            """;
        const string mixedLazyBooleanVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                bool isAndRoot
                    = Host.StandaloneSourceExpressions[32] == 35;
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 5
                    || (isAndRoot
                        ? (Host.StandaloneSourceExpressions[16] != 36
                            || Host.StandaloneSourceExpressions[32] != 35)
                        : (Host.StandaloneSourceExpressions[16] != 35
                            || Host.StandaloneSourceExpressions[32] != 36))) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 6
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 6
                    || Host.StandaloneNormalizedHomeCount[0] != 4
                    || Host.StandaloneNormalizedOperationCount[0] != 5
                    || Host.StandaloneNormalizedConstantCount[0] != 2
                    || (isAndRoot
                        ? (Host.StandaloneNormalizedBlocks[8] != 2
                            || Host.StandaloneNormalizedBlocks[9] != 1
                            || Host.StandaloneNormalizedBlocks[24] != 2
                            || Host.StandaloneNormalizedBlocks[25] != 4
                            || Host.StandaloneNormalizedBlocks[40] != 3
                            || Host.StandaloneNormalizedBlocks[41] != 4
                            || Host.StandaloneNormalizedBlocks[45] != 8)
                        : (Host.StandaloneNormalizedBlocks[8] != 1
                            || Host.StandaloneNormalizedBlocks[9] != 2
                            || Host.StandaloneNormalizedBlocks[24] != 3
                            || Host.StandaloneNormalizedBlocks[25] != 2
                            || Host.StandaloneNormalizedBlocks[40] != 3
                            || Host.StandaloneNormalizedBlocks[41] != 4
                            || Host.StandaloneNormalizedBlocks[45] != 8))
                    || Host.StandaloneNormalizedBlocks[13] != 6
                    || Host.StandaloneNormalizedBlocks[29] != 7) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 90;
                }
                return 0;
            }
            """;
        const string mixedLazyBooleanInitializerVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceStatementCount[0] != 2
                    || Host.StandaloneSourceLocalCount[0] != 1
                    || Host.StandaloneSourceLocalStates[0] != 1
                    || Host.StandaloneSourceExpressionCount[0] != 6
                    || Host.StandaloneSourceExpressions[16] != 35
                    || Host.StandaloneSourceExpressions[32] != 36) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 6
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 6
                    || Host.StandaloneNormalizedHomeCount[0] != 4
                    || Host.StandaloneNormalizedOperationCount[0] != 5
                    || Host.StandaloneNormalizedConstantCount[0] != 2
                    || Host.StandaloneNormalizedSourceLocalHomeIds[0] < 0
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9] != 2
                    || Host.StandaloneNormalizedBlocks[13] != 6
                    || Host.StandaloneNormalizedBlocks[24] != 3
                    || Host.StandaloneNormalizedBlocks[25] != 2
                    || Host.StandaloneNormalizedBlocks[29] != 7
                    || Host.StandaloneNormalizedBlocks[40] != 3
                    || Host.StandaloneNormalizedBlocks[41] != 4
                    || Host.StandaloneNormalizedBlocks[45] != 8) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 90;
                }
                return 0;
            }
            """;
        const string nestedConditionalInitializerVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceStatementCount[0] != 2
                    || Host.StandaloneSourceLocalCount[0] != 1
                    || Host.StandaloneSourceLocalStates[0] != 1
                    || Host.StandaloneSourceExpressionCount[0] != 8
                    || Host.StandaloneSourceExpressions[48] != 34) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 6
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 6
                    || Host.StandaloneNormalizedHomeCount[0] != 6
                    || Host.StandaloneNormalizedOperationCount[0] != 4
                    || Host.StandaloneNormalizedSourceLocalHomeIds[0] < 0
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9] != 4
                    || Host.StandaloneNormalizedBlocks[13] != 6
                    || Host.StandaloneNormalizedBlocks[24] != 2
                    || Host.StandaloneNormalizedBlocks[25] != 3
                    || Host.StandaloneNormalizedBlocks[29] != 7) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 90;
                }
                return 0;
            }
            """;
        const string mixedLazyBooleanAssignmentVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceStatementCount[0] != 3
                    || Host.StandaloneSourceLocalCount[0] != 1
                    || Host.StandaloneSourceLocals[3] != 2
                    || Host.StandaloneSourceLocalStates[0] != 1
                    || Host.StandaloneSourceExpressionCount[0] != 6
                    || Host.StandaloneSourceExpressions[16] != 35
                    || Host.StandaloneSourceExpressions[32] != 36) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 6
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 6
                    || Host.StandaloneNormalizedHomeCount[0] != 5
                    || Host.StandaloneNormalizedOperationCount[0] != 6
                    || Host.StandaloneNormalizedConstantCount[0] != 2
                    || Host.StandaloneNormalizedSourceLocalHomeIds[0] < 0
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9] != 2
                    || Host.StandaloneNormalizedBlocks[13] != 6
                    || Host.StandaloneNormalizedBlocks[24] != 3
                    || Host.StandaloneNormalizedBlocks[25] != 2
                    || Host.StandaloneNormalizedBlocks[29] != 7
                    || Host.StandaloneNormalizedBlocks[40] != 3
                    || Host.StandaloneNormalizedBlocks[41] != 4
                    || Host.StandaloneNormalizedBlocks[45] != 8) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 90;
                }
                return 0;
            }
            """;
        const string mixedLazyBooleanMutableAssignmentVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceStatementCount[0] != 3
                    || Host.StandaloneSourceLocalCount[0] != 1
                    || Host.StandaloneSourceLocals[3] != 1
                    || Host.StandaloneSourceLocalStates[0] != 1
                    || Host.StandaloneSourceExpressionCount[0] != 7
                    || Host.StandaloneSourceExpressions[24] != 35
                    || Host.StandaloneSourceExpressions[40] != 36) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 6
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 6
                    || Host.StandaloneNormalizedHomeCount[0] != 7
                    || Host.StandaloneNormalizedOperationCount[0] != 10
                    || Host.StandaloneNormalizedConstantCount[0] != 2
                    || Host.StandaloneNormalizedSourceLocalHomeIds[0] < 0
                    || Host.StandaloneNormalizedBlocks[2] != 0
                    || Host.StandaloneNormalizedBlocks[3] != 3
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9] != 2
                    || Host.StandaloneNormalizedBlocks[13] != 6
                    || Host.StandaloneNormalizedBlocks[24] != 3
                    || Host.StandaloneNormalizedBlocks[25] != 2
                    || Host.StandaloneNormalizedBlocks[29] != 7
                    || Host.StandaloneNormalizedBlocks[40] != 3
                    || Host.StandaloneNormalizedBlocks[41] != 4
                    || Host.StandaloneNormalizedBlocks[45] != 8) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 90;
                }
                return 0;
            }
            """;
        const string nestedConditionalAssignmentVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceStatementCount[0] != 3
                    || Host.StandaloneSourceLocalCount[0] != 1
                    || Host.StandaloneSourceLocals[3] != 1
                    || Host.StandaloneSourceLocalStates[0] != 1
                    || Host.StandaloneSourceExpressionCount[0] != 9
                    || Host.StandaloneSourceExpressions[56] != 34) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 6
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 6
                    || Host.StandaloneNormalizedHomeCount[0] != 9
                    || Host.StandaloneNormalizedOperationCount[0] != 7
                    || Host.StandaloneNormalizedSourceLocalHomeIds[0] < 0
                    || Host.StandaloneNormalizedBlocks[2] != 0
                    || Host.StandaloneNormalizedBlocks[3] != 1
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9] != 4
                    || Host.StandaloneNormalizedBlocks[13] != 6
                    || Host.StandaloneNormalizedBlocks[24] != 2
                    || Host.StandaloneNormalizedBlocks[25] != 3
                    || Host.StandaloneNormalizedBlocks[29] != 7) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 90;
                }
                return 0;
            }
            """;
        const string conditionalInEagerExpressionVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceStatementCount[0] != 1
                    || Host.StandaloneSourceLocalCount[0] != 0
                    || Host.StandaloneSourceExpressionCount[0] != 6
                    || Host.StandaloneSourceExpressions[24] != 34) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 4
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 4
                    || Host.StandaloneNormalizedHomeCount[0] != 6
                    || Host.StandaloneNormalizedOperationCount[0] != 4
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9] != 2
                    || Host.StandaloneNormalizedBlocks[13] != 4
                    || Host.StandaloneNormalizedBlocks[24] != 3
                    || Host.StandaloneNormalizedBlocks[40] != 3) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 90;
                }
                return 0;
            }
            """;
        const string lazyValueInEagerExpressionVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceStatementCount[0] != 1
                    || Host.StandaloneSourceLocalCount[0] != 0
                    || Host.StandaloneSourceExpressionCount[0] != 7
                    || Host.StandaloneSourceExpressions[32] != 36) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 6
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 6
                    || Host.StandaloneNormalizedHomeCount[0] != 6
                    || Host.StandaloneNormalizedOperationCount[0] != 6
                    || Host.StandaloneNormalizedConstantCount[0] != 2
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9] != 2
                    || Host.StandaloneNormalizedBlocks[13] != 6
                    || Host.StandaloneNormalizedBlocks[24] != 3
                    || Host.StandaloneNormalizedBlocks[25] != 2
                    || Host.StandaloneNormalizedBlocks[29] != 7
                    || Host.StandaloneNormalizedBlocks[40] != 3
                    || Host.StandaloneNormalizedBlocks[41] != 4
                    || Host.StandaloneNormalizedBlocks[45] != 8) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 90;
                }
                return 0;
            }
            """;
        const string lazyConditionConditionalValueVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                bool isAndRoot
                    = Host.StandaloneSourceExpressions[16] == 35;
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 6
                    || (!isAndRoot
                        && Host.StandaloneSourceExpressions[16] != 36)
                    || Host.StandaloneSourceExpressions[40] != 34) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 5
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 5
                    || Host.StandaloneNormalizedBlocks[8]
                        != (isAndRoot ? 1 : 2)
                    || Host.StandaloneNormalizedBlocks[9]
                        != (isAndRoot ? 3 : 1)
                    || Host.StandaloneNormalizedBlocks[24] != 2
                    || Host.StandaloneNormalizedBlocks[25] != 3) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 90;
                }
                return 0;
            }
            """;
        const string lazyBooleanIfReturnVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceStatementCount[0] != 3
                    || Host.StandaloneSourceIfNodeCount[0] != 1
                    || Host.StandaloneSourceIfNodes[2] != 1
                    || Host.StandaloneSourceIfNodes[4] != 1
                    || Host.StandaloneSourceExpressionCount[0] != 7
                    || Host.StandaloneSourceExpressions[32] != 36) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 5
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 5
                    || Host.StandaloneNormalizedHomeCount[0] != 5
                    || Host.StandaloneNormalizedOperationCount[0] != 6
                    || Host.StandaloneNormalizedConstantCount[0] != 2
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9] != 2
                    || Host.StandaloneNormalizedBlocks[13] != 5
                    || Host.StandaloneNormalizedBlocks[24] != 3
                    || Host.StandaloneNormalizedBlocks[25] != 2
                    || Host.StandaloneNormalizedBlocks[29] != 6
                    || Host.StandaloneNormalizedBlocks[40] != 3
                    || Host.StandaloneNormalizedBlocks[41] != 4
                    || Host.StandaloneNormalizedBlocks[45] != 7) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 90;
                }
                return 0;
            }
            """;
        const string localCallInLazyIfReturnVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceStatementCount[0] != 3
                    || Host.StandaloneSourceDirectCallCount[0] != 1
                    || Host.StandaloneSourceCallArgumentCount[0] != 2
                    || Host.StandaloneSourceIfNodeCount[0] != 1
                    || Host.StandaloneSourceIfNodes[2] != 1
                    || Host.StandaloneSourceIfNodes[4] != 1) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 3
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 3
                    || Host.StandaloneInvokeCount[0] != 1
                    || Host.StandaloneInvokeInputCount[0] != 2
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9] != 2
                    || Host.StandaloneNormalizedBlocks[13] != 3) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1
                    || Host.StandaloneWriterMethodMapCount[0] != 1
                    || Host.StandaloneWriterCallSiteRecordIndices[0] != 4) {
                    return 90;
                }
                return 0;
            }
            """;
        const string lazyBooleanIfMutableJoinVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceStatementCount[0] != 5
                    || Host.StandaloneSourceIfNodeCount[0] != 1
                    || Host.StandaloneSourceIfNodes[2] != 1
                    || Host.StandaloneSourceIfNodes[4] != 1
                    || Host.StandaloneSourceLocalCount[0] != 1
                    || Host.StandaloneSourceLocals[3] != 1
                    || Host.StandaloneSourceLocalStates[0] != 1
                    || Host.StandaloneSourceExpressionCount[0] != 9
                    || Host.StandaloneSourceExpressions[24] != 35
                    || Host.StandaloneSourceExpressions[40] != 36) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 6
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 6
                    || Host.StandaloneNormalizedHomeCount[0] != 8
                    || Host.StandaloneNormalizedOperationCount[0] != 5
                    || Host.StandaloneNormalizedBlocks[2] != 0
                    || Host.StandaloneNormalizedBlocks[3] != 1
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9] != 2
                    || Host.StandaloneNormalizedBlocks[13] != 6
                    || Host.StandaloneNormalizedBlocks[24] != 3
                    || Host.StandaloneNormalizedBlocks[25] != 2
                    || Host.StandaloneNormalizedBlocks[29] != 7
                    || Host.StandaloneNormalizedBlocks[40] != 3
                    || Host.StandaloneNormalizedBlocks[41] != 4
                    || Host.StandaloneNormalizedBlocks[45] != 8
                    || Host.StandaloneNormalizedBlocks[56] != 5
                    || Host.StandaloneNormalizedBlocks[72] != 5) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 90;
                }
                return 0;
            }
            """;
        const string lazyBooleanIfInitJoinVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceStatementCount[0] != 5
                    || Host.StandaloneSourceIfNodeCount[0] != 1
                    || Host.StandaloneSourceIfNodes[2] != 1
                    || Host.StandaloneSourceIfNodes[4] != 1
                    || Host.StandaloneSourceLocalCount[0] != 1
                    || Host.StandaloneSourceLocals[3] != 2
                    || Host.StandaloneSourceLocalStates[0] != 1
                    || Host.StandaloneSourceExpressionCount[0] != 8
                    || Host.StandaloneSourceExpressions[16] != 35
                    || Host.StandaloneSourceExpressions[32] != 36) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 6
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 6
                    || Host.StandaloneNormalizedHomeCount[0] != 6
                    || Host.StandaloneNormalizedOperationCount[0] != 3
                    || Host.StandaloneNormalizedBlocks[2] != 0
                    || Host.StandaloneNormalizedBlocks[3] != 0
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9] != 2
                    || Host.StandaloneNormalizedBlocks[13] != 6
                    || Host.StandaloneNormalizedBlocks[24] != 3
                    || Host.StandaloneNormalizedBlocks[25] != 2
                    || Host.StandaloneNormalizedBlocks[29] != 7
                    || Host.StandaloneNormalizedBlocks[40] != 3
                    || Host.StandaloneNormalizedBlocks[41] != 4
                    || Host.StandaloneNormalizedBlocks[45] != 8
                    || Host.StandaloneNormalizedBlocks[56] != 5
                    || Host.StandaloneNormalizedBlocks[72] != 5) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 90;
                }
                return 0;
            }
            """;
        const string lazyBooleanIfNoElseJoinVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceStatementCount[0] != 4
                    || Host.StandaloneSourceIfNodeCount[0] != 1
                    || Host.StandaloneSourceIfNodes[2] != 1
                    || Host.StandaloneSourceIfNodes[4] != 0
                    || Host.StandaloneSourceIfNodes[3]
                        != Host.StandaloneSourceIfChildCount[0]
                    || Host.StandaloneSourceLocalCount[0] != 1
                    || Host.StandaloneSourceLocals[3] != 1
                    || Host.StandaloneSourceLocalStates[0] != 1
                    || Host.StandaloneSourceExpressionCount[0] != 8
                    || Host.StandaloneSourceExpressions[40] != 36) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 6
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 6
                    || Host.StandaloneNormalizedHomeCount[0] != 7
                    || Host.StandaloneNormalizedOperationCount[0] != 4
                    || Host.StandaloneNormalizedBlocks[2] != 0
                    || Host.StandaloneNormalizedBlocks[3] != 1
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9] != 2
                    || Host.StandaloneNormalizedBlocks[13] != 6
                    || Host.StandaloneNormalizedBlocks[24] != 3
                    || Host.StandaloneNormalizedBlocks[25] != 2
                    || Host.StandaloneNormalizedBlocks[29] != 7
                    || Host.StandaloneNormalizedBlocks[40] != 3
                    || Host.StandaloneNormalizedBlocks[41] != 4
                    || Host.StandaloneNormalizedBlocks[45] != 8
                    || Host.StandaloneNormalizedBlocks[56] != 5
                    || Host.StandaloneNormalizedBlocks[67] != 0
                    || Host.StandaloneNormalizedBlocks[72] != 5) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 90;
                }
                return 0;
            }
            """;
        const string localCallInLazyIfReturnVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceStatementCount[0] != 3
                    || Host.StandaloneSourceDirectCallCount[0] != 1
                    || Host.StandaloneSourceCallArgumentCount[0] != 2
                    || Host.StandaloneSourceIfNodeCount[0] != 1
                    || Host.StandaloneSourceIfNodes[2] != 1
                    || Host.StandaloneSourceIfNodes[4] != 1) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 3
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 3
                    || Host.StandaloneNormalizedHomeCount[0] != 4
                    || Host.StandaloneNormalizedOperationCount[0] != 5
                    || Host.StandaloneNormalizedConstantCount[0] != 1
                    || Host.StandaloneInvokeCount[0] != 1
                    || Host.StandaloneInvokeInputCount[0] != 2
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9] != 2
                    || Host.StandaloneNormalizedBlocks[13] != 3) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1
                    || Host.StandaloneWriterMethodMapCount[0] != 1
                    || Host.StandaloneWriterCallSiteRecordIndices[0] != 4) {
                    return 90;
                }
                return 0;
            }
            """;
        const string importedCallInConditionalReturnVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceImportedCallableCount[0] != 1
                    || Host.StandaloneSourceDirectCallCount[0] != 1
                    || Host.StandaloneSourceCallArgumentCount[0] != 1
                    || Host.StandaloneSourceIfNodeCount[0] != 1) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 3
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 3
                    || Host.StandaloneInvokeCount[0] != 1
                    || Host.StandaloneInvokeInputCount[0] != 1
                    || Host.StandaloneInvokes[0] != 2
                    || Host.StandaloneInvokes[1] != 0
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9] != 2
                    || Host.StandaloneNormalizedBlocks[13] != 3) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1
                    || Host.StandaloneWriterMethodMapCount[0] != 1
                    || Host.StandaloneWriterCallSiteRecordIndices[0] != 4) {
                    return 90;
                }
                return 0;
            }
            """;
        const string terminalIfArmJoinVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceLocalCount[0] != 1
                    || Host.StandaloneSourcePlaceCount[0] != 1
                    || Host.StandaloneSourceIfNodeCount[0] != 1
                    || Host.StandaloneSourceIfNodes[2] != 1
                    || Host.StandaloneSourceIfNodes[4] != 1
                    || Host.StandaloneSourceLocalStates[0] != 1) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 4
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 4
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9] != 2
                    || Host.StandaloneNormalizedBlocks[13] != 4) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 90;
                }
                return 0;
            }
            """;
        const string lazyTerminalIfArmJoinVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                bool isAndRoot
                    = Host.StandaloneSourceExpressions[16] == 35;
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 6
                    || (!isAndRoot
                        && Host.StandaloneSourceExpressions[16] != 36)
                    || Host.StandaloneSourceLocalCount[0] != 1
                    || Host.StandaloneSourcePlaceCount[0] != 1
                    || Host.StandaloneSourceIfNodeCount[0] != 1
                    || Host.StandaloneSourceIfNodes[2] != 1
                    || Host.StandaloneSourceIfNodes[4] != 1
                    || Host.StandaloneSourceLocalStates[0] != 1) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 5
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 5
                    || Host.StandaloneNormalizedBlocks[8]
                        != (isAndRoot ? 1 : 2)
                    || Host.StandaloneNormalizedBlocks[9]
                        != (isAndRoot ? 3 : 1)
                    || Host.StandaloneNormalizedBlocks[13] != 5
                    || Host.StandaloneNormalizedBlocks[24] != 2
                    || Host.StandaloneNormalizedBlocks[25] != 3
                    || Host.StandaloneNormalizedBlocks[29] != 6
                    || Host.StandaloneNormalizedBlocks[56] != 4) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 90;
                }
                return 0;
            }
            """;
        const string terminalIfNoElseReturnVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                bool isLazyCondition
                    = Host.StandaloneSourceExpressions[16] == 35
                        || Host.StandaloneSourceExpressions[16] == 36;
                if (status != 0
                    || Host.StandaloneSourceLocalCount[0] != 0
                    || Host.StandaloneSourcePlaceCount[0] != 0
                    || Host.StandaloneSourceIfNodeCount[0] != 1
                    || Host.StandaloneSourceIfNodes[2] != 1
                    || Host.StandaloneSourceIfNodes[4] != 0) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0]
                        != (isLazyCondition ? 5 : 4)
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1]
                        != (isLazyCondition ? 5 : 4)
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9]
                        != (isLazyCondition ? 3 : 2)
                    || Host.StandaloneNormalizedBlocks[13]
                        != (isLazyCondition ? 5 : 4)
                    || (isLazyCondition
                        ? (Host.StandaloneNormalizedBlocks[24] != 2
                            || Host.StandaloneNormalizedBlocks[25] != 3
                            || Host.StandaloneNormalizedBlocks[29] != 6
                            || Host.StandaloneNormalizedBlocks[56] != 4)
                        : Host.StandaloneNormalizedBlocks[40] != 3)) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 90;
                }
                return 0;
            }
            """;
        const string differentPlacesIfJoinVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                int32 conditionExpressionId
                    = Host.StandaloneSourceStatements[7];
                int32 conditionKind
                    = Host.StandaloneSourceExpressions[
                        (Host.StandaloneSourceBodies[3]
                            + conditionExpressionId) * 8];
                bool isAndRoot = conditionKind == 35;
                bool isLazyCondition
                    = conditionKind == 35 || conditionKind == 36;
                if (status != 0
                    || Host.StandaloneSourceLocalCount[0] != 2
                    || Host.StandaloneSourcePlaceCount[0] != 2
                    || Host.StandaloneSourceIfNodeCount[0] != 1
                    || Host.StandaloneSourceIfNodes[2] != 1
                    || Host.StandaloneSourceIfNodes[4] != 1
                    || Host.StandaloneSourceLocalStates[0] != 1
                    || Host.StandaloneSourceLocalStates[1] != 1
                    || (!isLazyCondition
                        && conditionKind == 36)) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0]
                        != (isLazyCondition ? 5 : 4)
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1]
                        != (isLazyCondition ? 5 : 4)
                    || Host.StandaloneNormalizedBlocks[8]
                        != (isLazyCondition
                            ? (isAndRoot ? 1 : 2) : 1)
                    || Host.StandaloneNormalizedBlocks[9]
                        != (isLazyCondition
                            ? (isAndRoot ? 3 : 1) : 2)
                    || Host.StandaloneNormalizedBlocks[13]
                        != (isLazyCondition ? 5 : 4)
                    || (isLazyCondition
                        ? (Host.StandaloneNormalizedBlocks[24] != 2
                            || Host.StandaloneNormalizedBlocks[25] != 3
                            || Host.StandaloneNormalizedBlocks[29] != 6
                            || Host.StandaloneNormalizedBlocks[40] != 4
                            || Host.StandaloneNormalizedBlocks[56] != 4)
                        : (Host.StandaloneNormalizedBlocks[24] != 3
                            || Host.StandaloneNormalizedBlocks[40] != 3))) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 90;
                }
                return 0;
            }
            """;
        const string whileLoopVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                int32 sourceStatementCount
                    = Host.StandaloneSourceStatementCount[0];
                if (status != 0
                    || (sourceStatementCount != 4
                        && sourceStatementCount != 5)
                    || Host.StandaloneSourceStatements[3] != 7
                    || Host.StandaloneSourceLocalCount[0] != 1
                    || Host.StandaloneSourceLocalStates[0] != 1
                    || Host.StandaloneSourceIfNodeCount[0] != 1
                    || Host.StandaloneSourceIfNodes[2]
                        != (sourceStatementCount == 4 ? 1 : 2)
                    || Host.StandaloneSourceIfNodes[4] != 0) {
                    return 88;
                }
                if (sourceStatementCount == 5
                    && ((Host.StandaloneSourceStatements[9] != 8
                            && Host.StandaloneSourceStatements[9] != 9)
                        || Host.StandaloneSourceStatements[11] != 0)) {
                    return 88;
                }
                int32 expectedBodyTarget
                    = sourceStatementCount == 5
                        && Host.StandaloneSourceStatements[9] == 8
                        ? 3 : 1;
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 4
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 4
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[24] != 2
                    || Host.StandaloneNormalizedBlocks[25] != 3
                    || Host.StandaloneNormalizedBlocks[29] != 4
                    || Host.StandaloneNormalizedBlocks[40]
                        != expectedBodyTarget) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 90;
                }
                return 0;
            }
            """;
        const string nestedConditionalValueVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 87; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceExpressionCount[0] != 7
                    || Host.StandaloneSourceExpressions[48] != 34) {
                    return 88;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 6
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 6
                    || Host.StandaloneNormalizedHomeCount[0] != 6
                    || Host.StandaloneNormalizedOperationCount[0] != 4
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9] != 4
                    || Host.StandaloneNormalizedBlocks[13] != 6
                    || Host.StandaloneNormalizedBlocks[24] != 2
                    || Host.StandaloneNormalizedBlocks[25] != 3
                    || Host.StandaloneNormalizedBlocks[29] != 7) {
                    return 89;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 90;
                }
                return 0;
            }
            """;
        const string mutableIfJoinVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 92; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceStatementCount[0] != 5
                    || Host.StandaloneSourceLocalCount[0] != 1
                    || Host.StandaloneSourcePlaceCount[0] != 3
                    || Host.StandaloneSourceStatements[3] != 6
                    || Host.StandaloneSourceStatements[2 * 3] != 5
                    || Host.StandaloneSourceStatements[3 * 3] != 5) {
                    return 93;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 4
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 4
                    || Host.StandaloneNormalizedOperationCount[0] != 5
                    || Host.StandaloneNormalizedBlocks[4] != 4
                    || Host.StandaloneNormalizedBlocks[8] != 1
                    || Host.StandaloneNormalizedBlocks[9] != 2
                    || Host.StandaloneNormalizedBlocks[13] != 4
                    || Host.StandaloneNormalizedBlocks[20] != 3
                    || Host.StandaloneNormalizedBlocks[24] != 3
                    || Host.StandaloneNormalizedBlocks[36] != 3
                    || Host.StandaloneNormalizedBlocks[40] != 3
                    || Host.StandaloneNormalizedBlocks[52] != 1
                    || Host.StandaloneNormalizedBlocks[54] != 5
                    || Host.StandaloneNormalizedOperations[0] != 30
                    || Host.StandaloneNormalizedOperations[5] != 30
                    || Host.StandaloneNormalizedOperations[10] != 30
                    || Host.StandaloneNormalizedOperations[15] != 31
                    || Host.StandaloneNormalizedOperations[20] != 4) {
                    return 94;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 95;
                }
                return 0;
            }
            """;
        const string mutableIfMultipleJoinVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 92; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceStatementCount[0] != 7
                    || Host.StandaloneSourceLocalCount[0] != 1
                    || Host.StandaloneSourceIfNodeCount[0] != 1
                    || Host.StandaloneSourceIfNodes[2] != 2
                    || Host.StandaloneSourceIfNodes[4] != 2) {
                    return 93;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 4
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 4
                    || Host.StandaloneNormalizedOperationCount[0] != 7) {
                    return 94;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 95;
                }
                return 0;
            }
            """;
        const string mutableIfNoElseJoinVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 92; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceStatementCount[0] != 4
                    || Host.StandaloneSourceLocalCount[0] != 1
                    || Host.StandaloneSourceIfNodeCount[0] != 1
                    || Host.StandaloneSourceIfNodes[2] != 1
                    || Host.StandaloneSourceIfNodes[4] != 0
                    || Host.StandaloneSourceIfNodes[3]
                        != Host.StandaloneSourceIfChildCount[0]) {
                    return 93;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 4
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 4
                    || Host.StandaloneNormalizedOperationCount[0] != 4) {
                    return 94;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 95;
                }
                return 0;
            }
            """;
        const string initLocalIfJoinVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 92; }
                int32 status = BindStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneSourceStatementCount[0] != 5
                    || Host.StandaloneSourceLocalCount[0] != 1
                    || Host.StandaloneSourceLocals[3] != 2
                    || Host.StandaloneSourceLocalStates[0] != 1
                    || Host.StandaloneSourceIfNodeCount[0] != 1
                    || Host.StandaloneSourceIfNodes[2] != 1
                    || Host.StandaloneSourceIfNodes[4] != 1) {
                    return 93;
                }
                status = NormalizeStandaloneOwnedSourceProgram();
                if (status != 0
                    || Host.StandaloneNormalizedBlockCount[0] != 4
                    || Host.StandaloneNormalizedBodyBlockRanges[0] != 0
                    || Host.StandaloneNormalizedBodyBlockRanges[1] != 4
                    || Host.StandaloneNormalizedHomeCount[0] != 4
                    || Host.StandaloneNormalizedOperationCount[0] != 3) {
                    return 94;
                }
                status = RewriteStandaloneOwnedCallNodes();
                if (status != 0) { return status; }
                status = WriteStandaloneScalarModule();
                if (status != 0
                    || Host.StandaloneWriterStatus[0] != 0
                    || Host.StandaloneWriterBodyPlanCount[0] != 1) {
                    return 95;
                }
                return 0;
            }
            """;
        var verificationSource = expectedSourceStatus != 0
            ? $$"""
                namespace Terrum.CodeAnalysis;
                int32 Main() {
                    if (Scan() < 1) { return 80; }
                    int32 status = BindStandaloneOwnedSourceProgram();
                    if (status != {{expectedSourceStatus}}
                        || Host.StandaloneSourceProgramStatus[0]
                            != {{expectedSourceStatus}}
                        || Host.StandaloneLocalCallableCount[0] != 0
                        || Host.StandaloneSourceUsingCount[0] != 0
                        || Host.StandaloneSourceNamespaceScopeCount[0] != 0
                        || Host.StandaloneSourceDirectCallCount[0] != 0) {
                        return 81;
                    }
                    return 0;
                }
                """
            : verificationMode == 130
                ? multiBlockControlFlowVerificationSource
            : verificationMode == 131
                ? conditionalValueJoinVerificationSource
            : verificationMode is 132 or 133
                ? lazyBooleanVerificationSource
            : verificationMode is 136 or 137
                ? lazyBooleanChainVerificationSource
            : verificationMode is 144 or 145
                ? mixedLazyBooleanVerificationSource
            : verificationMode == 146
                ? mixedLazyBooleanInitializerVerificationSource
            : verificationMode == 147
                ? nestedConditionalInitializerVerificationSource
            : verificationMode == 148
                ? mixedLazyBooleanAssignmentVerificationSource
            : verificationMode == 149
                ? mixedLazyBooleanMutableAssignmentVerificationSource
            : verificationMode == 150
                ? nestedConditionalAssignmentVerificationSource
            : verificationMode == 151
                ? conditionalInEagerExpressionVerificationSource
            : verificationMode == 152
                ? lazyValueInEagerExpressionVerificationSource
            : verificationMode is 159 or 160
                ? lazyConditionConditionalValueVerificationSource
            : verificationMode == 153
                ? lazyBooleanIfReturnVerificationSource
            : verificationMode == 158
                ? localCallInLazyIfReturnVerificationSource
            : verificationMode == 161
                ? importedCallInConditionalReturnVerificationSource
            : verificationMode is 162 or 163 or 164
                ? terminalIfArmJoinVerificationSource
            : verificationMode is 165 or 166
                ? lazyTerminalIfArmJoinVerificationSource
            : verificationMode is 167 or 168
                ? terminalIfNoElseReturnVerificationSource
            : verificationMode is 169 or 170 or 171
                ? differentPlacesIfJoinVerificationSource
            : verificationMode is >= 172 and <= 174
                ? whileLoopVerificationSource
            : verificationMode == 154
                ? lazyBooleanIfMutableJoinVerificationSource
            : verificationMode == 155
                ? lazyBooleanIfInitJoinVerificationSource
            : verificationMode == 156
                ? lazyBooleanIfNoElseJoinVerificationSource
            : verificationMode == 158
                ? localCallInLazyIfReturnVerificationSource
            : verificationMode == 134
                ? mutableIfJoinVerificationSource
            : verificationMode == 135
                ? mutableIfMultipleJoinVerificationSource
            : verificationMode == 138
                ? nestedConditionalValueVerificationSource
            : verificationMode == 139
                ? mutableIfNoElseJoinVerificationSource
            : verificationMode == 140
                ? initLocalIfJoinVerificationSource
            : verificationMode == 2
                ? localOverloadCallVerificationSource
                : verificationMode == 3
                    ? nestedLocalOverloadCallVerificationSource
                    : verificationMode == 4
                        ? genericSourceCallVerificationSource
                        : verificationMode == 15
                            ? localSourceArenaVerificationSource
                            : verificationMode == 16
                                ? localSourceNormalizationVerificationSource
                                : verificationMode == 17
                                    ? localSourcePermutationVerificationSource
                                    : verificationMode == 21
                                        || verificationMode == 22
                                        ? unaryMinusVerificationSource
                                        : verificationMode == 23
                                            ? signedMinimumLiteralVerificationSource
                                        : verificationMode == 25
                                            ? basedIntegerBitsVerificationSource
                                            : verificationMode == 26
                                                ? basedTypedNegationVerificationSource
                                                : verificationMode == 27
                                                    || verificationMode == 28
                                                    ? unaryNotVerificationSource
                                                : verificationMode == 36
                                                    ? floatingGroupedNegationVerificationSource
                                                : verificationMode == 41
                                                    ? bitwisePrecedenceVerificationSource
                                                : verificationMode == 44
                                                    ? booleanEqualityPrecedenceVerificationSource
                                                : verificationMode == 38
                                                    || verificationMode == 39
                                                    || verificationMode == 40
                                                    ? bitwiseBinaryVerificationSource
                                                : verificationMode == 42
                                                    || verificationMode == 43
                                                    ? booleanEqualityVerificationSource
                                                    : verificationMode >= 45
                                                        && verificationMode <= 53
                                                        ? numericComparisonVerificationSource
                                                    : verificationMode >= 55
                                                        && verificationMode <= 57
                                                        ? numericComparisonVerificationSource
                                                    : verificationMode == 54
                                                        ? comparisonPrecedenceVerificationSource
                                                    : verificationMode is >= 90 and <= 95
                                                        ? localInitTestVerificationSource
                                                    : verificationMode is >= 104 and <= 109
                                                        ? updateExpressionVerificationSource
                                                    : verificationMode is >= 111 and <= 117
                                                        or >= 122 and <= 125
                                                        ? shiftVerificationSource
                                                    : verificationMode is >= 118 and <= 121
                                                        or >= 126 and <= 129
                                                        ? shiftCompoundVerificationSource
                                                    : verificationMode is 81 or 82
                                                        ? initializationLocalVerificationSource
                                                    : verificationMode is 75 or 77
                                                        or >= 96 and <= 103
                                                        ? mutableLocalVerificationSource
                                                    : verificationMode is >= 60 and <= 74
                                                        ? scalarArithmeticVerificationSource
                                                    : verificationMode is 58 or 59
                                                        ? unitEqualityVerificationSource
                                                : verificationMode == 29
                                                    || verificationMode == 30
                                                    || verificationMode == 31
                                                    || verificationMode == 32
                                                    || verificationMode == 33
                                                    || verificationMode == 34
                                                    || verificationMode == 35
                                                    || verificationMode == 37
                                                    ? floatingLiteralVerificationSource
                                                : verificationMode == 18
                                            ? floatUnaryPlusVerificationSource
                                        : verificationMode == 19
                                            ? unitLiteralVerificationSource
                                            : verificationMode == 20
                                                ? unitDiscardVerificationSource
                                                : verificationMode == 1
                                                    ? localNamespaceCallVerificationSource
                                                    : successfulVerificationSource;

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

        var sourceArithmeticProfile = verificationMode is 22 or 61 or 62
            or 63 or 108
            ? SourceArithmeticProfile.Release
            : SourceArithmeticProfile.Development;
        var buffers = ScannerHost.CreateBuffers(source).AddRange(
            input.Capture().CreateSourceCallHostBuffers(
                source, sourceArithmeticProfile));
        var compilation = Compilation.Create(components, buffers, policy);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new AssertionException(
                "Expected a bound standalone source-call validator.");
        var execution = NativeJit.ExecuteWithData(program,
            (NativeRuntimePolicy)policy);
        Assert.That(execution.ExitCode, Is.EqualTo(0));
        if (expectedSourceStatus != 0)
        {
            return;
        }
        if (verificationMode == 15
            || verificationMode == 17 || verificationMode == 20)
        {
            return;
        }
        var artifact = StandaloneMethodImportOutputFramer.Frame(
            execution, buffers);
        if (verificationMode == 0)
        {
            AssertStandaloneWrittenArtifact(artifact);
        }
        else
        {
            var artifactIndex = new TrRmArtifactIndex(artifact);
            Assert.That(artifactIndex.TryGetSection(0x0020,
                    out var bodySection, out var bodyCount),
                Is.True);
            Assert.That(bodyCount, Is.GreaterThan(0));
            var bodyTable = new TrRmBodyTableReader(bodySection, bodyCount);
            for (var bodyId = 1; bodyId <= bodyCount; bodyId++)
            {
                _ = new TrRmRegisterBodyReader(bodyTable.GetRow(bodyId));
            }
        }

        if (verificationMode is 21 or 22 or 24 or 26 or 36)
        {
            var artifactIndex = new TrRmArtifactIndex(artifact);
            Assert.That(artifactIndex.TryGetSection(0x0020,
                    out var bodySection, out var bodyCount),
                Is.True);
            var bodyTable = new TrRmBodyTableReader(bodySection, bodyCount);
            var bodyReader
                = new TrRmRegisterBodyReader(bodyTable.GetRow(1));
            var negateOffset = bodyReader.Code.IndexOf((byte)0xC0);
            Assert.That(negateOffset, Is.GreaterThanOrEqualTo(0));
            Assert.That(bodyReader.Code[negateOffset + 1],
                Is.EqualTo((byte)(verificationMode is 24 or 36 ? 0x25
                    : verificationMode == 22 ? 0x08 : 0x04)));
        }

        if (verificationMode is 4 or 21 or 22 or 23 or 24 or 25 or 26
            or 27 or 28 or 29 or 30 or 31 or 32 or 33 or 34 or 35
            or 36 or 37 or 38 or 39 or 40 or 41 or 42 or 43 or 44
            or 45 or 46 or 47 or 48 or 49 or 50 or 51 or 52 or 53
            or 54 or 55 or 56 or 57 or 58 or 59
            or >= 60 and <= 74 or 75 or 77 or 81 or 82
            or >= 90 and <= 95 or >= 96 and <= 103
            or >= 104 and <= 109 or >= 111 and <= 117
            or >= 122 and <= 125
            or >= 118 and <= 121 or >= 126 and <= 129 or 130)
        {
            var artifactIndex = new TrRmArtifactIndex(artifact);
            Assert.That(artifactIndex.TryGetSection(0x0020,
                    out var bodySection, out var bodyCount),
                Is.True);
            var bodyTable = new TrRmBodyTableReader(bodySection, bodyCount);
            var bodyReader
                = new TrRmRegisterBodyReader(bodyTable.GetRow(
                    verificationMode == 58 ? 2 : 1));
            if (verificationMode == 4)
            {
                Assert.That(bodyReader.Code.IndexOf((byte)0x14),
                    Is.GreaterThanOrEqualTo(0));
            }
            else if (verificationMode is 21 or 22 or 24 or 26 or 36)
            {
                var negateOffset
                    = bodyReader.Code.IndexOf((byte)0xC0);
                Assert.That(negateOffset, Is.GreaterThanOrEqualTo(0));
                Assert.That(bodyReader.Code[negateOffset + 1],
                    Is.EqualTo((byte)(verificationMode is 24 or 36 ? 0x25
                        : verificationMode == 22 ? 0x08 : 0x04)));
            }
            else if (verificationMode is 27 or 28)
            {
                var notOffset = bodyReader.Code.IndexOf((byte)0xC1);
                Assert.That(notOffset, Is.GreaterThanOrEqualTo(0));
                Assert.That(bodyReader.Code[notOffset + 1],
                    Is.EqualTo((byte)0x04));
            }
            else if (verificationMode is 38 or 39 or 40)
            {
                var bitwiseOffset
                    = bodyReader.Code.IndexOf((byte)0xC1);
                Assert.That(bitwiseOffset, Is.GreaterThanOrEqualTo(0));
                var expectedControl = verificationMode == 38 ? 0x01
                    : verificationMode == 39 ? 0x02 : 0x03;
                Assert.That(bodyReader.Code[bitwiseOffset + 1],
                    Is.EqualTo((byte)expectedControl));
            }
            else if (verificationMode == 41)
            {
                var code = bodyReader.Code;
                var andOffset = code.IndexOf((byte)0xC1);
                Assert.That(andOffset, Is.GreaterThanOrEqualTo(0));
                Assert.That(code[andOffset + 1], Is.EqualTo((byte)0x01));
                var orOffset = -1;
                for (var index = andOffset + 4; index < code.Length; index++)
                {
                    if (code[index] == 0xC1)
                    {
                        orOffset = index;
                        break;
                    }
                }
                Assert.That(orOffset, Is.GreaterThan(andOffset));
                Assert.That(code[orOffset + 1], Is.EqualTo((byte)0x02));
            }
            else if (verificationMode is 42 or 43)
            {
                var equalityOffset
                    = bodyReader.Code.IndexOf((byte)0xC2);
                Assert.That(equalityOffset, Is.GreaterThanOrEqualTo(0));
                Assert.That(bodyReader.Code[equalityOffset + 1],
                    Is.EqualTo((byte)expectedBooleanEqualityControl));
            }
            else if (verificationMode == 44)
            {
                var code = bodyReader.Code;
                var equalityOffset = code.IndexOf((byte)0xC2);
                var andOffset = code.IndexOf((byte)0xC1);
                Assert.That(equalityOffset, Is.GreaterThanOrEqualTo(0));
                Assert.That(andOffset, Is.GreaterThan(equalityOffset));
                Assert.That(code[equalityOffset + 1],
                    Is.EqualTo((byte)0x01));
                Assert.That(code[andOffset + 1],
                    Is.EqualTo((byte)0x01));
            }
            else if (verificationMode is >= 45 and <= 53
                or >= 55 and <= 57)
            {
                var code = bodyReader.Code;
                var relationOffset = code.IndexOf((byte)0xC2);
                var setOffset = code.IndexOf((byte)0xC3);
                Assert.That(relationOffset, Is.GreaterThanOrEqualTo(0));
                Assert.That(setOffset, Is.GreaterThan(relationOffset));
                Assert.That(code[relationOffset + 1],
                    Is.EqualTo((byte)0x00));
                Assert.That(code[setOffset + 1],
                    Is.EqualTo((byte)expectedComparisonPredicate));
            }
            else if (verificationMode == 54)
            {
                var code = bodyReader.Code;
                var relationOffset = code.IndexOf((byte)0xC2);
                var setOffset = code.IndexOf((byte)0xC3);
                var equalityOffset = -1;
                for (var index = setOffset + 4;
                    index < code.Length;
                    index++)
                {
                    if (code[index] == 0xC2)
                    {
                        equalityOffset = index;
                        break;
                    }
                }
                Assert.That(relationOffset, Is.GreaterThanOrEqualTo(0));
                Assert.That(setOffset, Is.GreaterThan(relationOffset));
                Assert.That(equalityOffset, Is.GreaterThan(setOffset));
                Assert.That(code[relationOffset + 1],
                    Is.EqualTo((byte)0x00));
                Assert.That(code[setOffset + 1],
                    Is.EqualTo((byte)0x01));
                Assert.That(code[equalityOffset + 1],
                    Is.EqualTo((byte)0x01));
            }
            else if (verificationMode is 58 or 59)
            {
                Assert.That(bodyReader.Code.IndexOf((byte)0xC2),
                    Is.EqualTo(-1));
                Assert.That(bodyReader.Code.IndexOf((byte)0xC3),
                    Is.EqualTo(-1));
            }
            else if (verificationMode is >= 60 and <= 74 or 75 or 77
                or 81 or 82 or >= 96 and <= 100
                or >= 104 and <= 109)
            {
                var code = bodyReader.Code;
                var arithmeticOffset = code.IndexOf((byte)0xC0);
                if (verificationMode == 81)
                {
                    Assert.That(arithmeticOffset, Is.EqualTo(-1));
                    Assert.That(code.IndexOf((byte)0xD5),
                        Is.GreaterThanOrEqualTo(0));
                    Assert.That(code.IndexOf((byte)0xD6),
                        Is.GreaterThanOrEqualTo(0));
                }
                else
                {
                    Assert.That(arithmeticOffset,
                        Is.GreaterThanOrEqualTo(0));
                }
                var expectedControl = verificationMode switch
                {
                    60 or 71 or 72 or 73 or 75 or 77 or 82 => 0x01,
                    96 => 0x01,
                    97 => 0x02,
                    98 => 0x03,
                    99 => 0x0C,
                    100 => 0x0D,
                    104 or 105 => 0x01,
                    106 or 107 => 0x02,
                    108 => 0x05,
                    109 => 0x20,
                    61 => 0x05,
                    62 => 0x06,
                    63 => 0x07,
                    64 => 0x0C,
                    65 => 0x0D,
                    66 => 0x20,
                    67 => 0x21,
                    68 => 0x22,
                    69 => 0x23,
                    70 => 0x24,
                    _ => 0x03,
                };
                if (verificationMode != 81)
                {
                    Assert.That(code[arithmeticOffset + 1],
                        Is.EqualTo((byte)expectedControl));
                }
                if (verificationMode == 74)
                {
                    var addOffset = code.IndexOf((byte)0xC0,
                        arithmeticOffset + 4);
                    Assert.That(addOffset,
                        Is.GreaterThan(arithmeticOffset));
                    Assert.That(code[addOffset + 1],
                        Is.EqualTo((byte)0x01));
                }
                if (verificationMode is 75 or 77 or 82
                    or >= 96 and <= 100
                    or >= 104 and <= 109)
                {
                    Assert.That(code.IndexOf((byte)0xD5),
                        Is.GreaterThanOrEqualTo(0));
                    Assert.That(code.IndexOf((byte)0xD6),
                        Is.GreaterThanOrEqualTo(0));
                }
            }
            else if (verificationMode is >= 101 and <= 103)
            {
                var code = bodyReader.Code;
                var bitwiseOffset = code.IndexOf((byte)0xC1);
                Assert.That(bitwiseOffset, Is.GreaterThanOrEqualTo(0));
                var expectedControl = verificationMode switch
                {
                    101 => 0x01,
                    102 => 0x02,
                    _ => 0x03,
                };
                Assert.That(code[bitwiseOffset + 1],
                    Is.EqualTo((byte)expectedControl));
                Assert.That(code.IndexOf((byte)0xD5),
                    Is.GreaterThanOrEqualTo(0));
                Assert.That(code.IndexOf((byte)0xD6),
                    Is.GreaterThanOrEqualTo(0));
            }
            else if (verificationMode is >= 111 and <= 117
                or >= 122 and <= 125)
            {
                var code = bodyReader.Code;
                var shiftOffset = code.IndexOf((byte)0xC1);
                Assert.That(shiftOffset, Is.GreaterThanOrEqualTo(0));
                Assert.That(code[shiftOffset + 1],
                    Is.EqualTo((byte)shiftControl));
                var reinterpretOffset = code.IndexOf((byte)0xD3);
                if (shiftNeedsReinterpret)
                {
                    Assert.That(reinterpretOffset,
                        Is.GreaterThanOrEqualTo(0));
                }
                else
                {
                    Assert.That(reinterpretOffset, Is.EqualTo(-1));
                }
                Assert.That(code.IndexOf((byte)0xD5),
                    Is.GreaterThanOrEqualTo(0));
                Assert.That(code.IndexOf((byte)0xD6),
                    Is.GreaterThanOrEqualTo(0));
            }
            else if (verificationMode is >= 118 and <= 121
                or >= 126 and <= 129)
            {
                var code = bodyReader.Code;
                var shiftOffset = code.IndexOf((byte)0xC1);
                Assert.That(shiftOffset, Is.GreaterThanOrEqualTo(0));
                Assert.That(code[shiftOffset + 1],
                    Is.EqualTo((byte)shiftCompoundControl));
                var reinterpretOffset = code.IndexOf((byte)0xD3);
                if (shiftCompoundNeedsReinterpret)
                {
                    Assert.That(reinterpretOffset,
                        Is.GreaterThanOrEqualTo(0));
                }
                else
                {
                    Assert.That(reinterpretOffset, Is.EqualTo(-1));
                }
                Assert.That(code.IndexOf((byte)0xD5),
                    Is.GreaterThanOrEqualTo(0));
                Assert.That(code.IndexOf((byte)0xD6),
                    Is.GreaterThanOrEqualTo(0));
            }
            else if ((verificationMode is >= 130 and <= 140)
                || verificationMode is 144 or 145 or 146 or 147
                    or 148 or 149 or 150 or 151 or 152 or 153
                    or 154 or 155 or 156 or 158 or 159 or 160
                    or 161 or 162 or 163 or 164 or 165 or 166
                    or 167 or 168 or 169 or 170 or 171
                    or 172 or 173 or 174)
            {
                var expectedBlockCount
                    = verificationMode is 159 or 160 or 165 or 166
                        or 168 or 170 or 171 ? 5
                        : verificationMode == 169 ? 4
                        : verificationMode is 172 or 173 or 174 ? 4
                        : verificationMode is 130 or 158 or 161 ? 3
                        : verificationMode is 136 or 137 or 153 ? 5
                            : verificationMode is 138 or 144 or 145
                                or 146 or 147 or 148 or 149
                                or 150 or 152 or 154 or 155 or 156 ? 6 : 4;
                var expectedRecordCount
                    = expectedBlockCount
                        + (verificationMode is 136 or 137 or 138 ? 2
                            : verificationMode is 144 or 145 or 146
                                or 148 or 149 or 152 or 153
                                or 154 or 155 or 156 ? 3
                                : verificationMode is 158 or 159 or 160
                                    or 161 or 165 or 166 or 168
                                    or 170 or 171 ? 2
                                : verificationMode is 147 or 150 ? 2 : 1);
                Assert.That(bodyReader.BlockCount,
                    Is.EqualTo(expectedBlockCount));
                Assert.That(bodyReader.RecordCount,
                    Is.EqualTo(expectedRecordCount));
                if (verificationMode is not 172 and not 173 and not 174)
                {
                    Assert.That(bodyReader.GetBlockCode(0)
                        .IndexOf((byte)0xB1),
                        Is.GreaterThanOrEqualTo(0));
                }
                if (verificationMode == 158)
                {
                    var trueBlockCode = bodyReader.GetBlockCode(1);
                    Assert.That(trueBlockCode.IndexOf((byte)0x90),
                        Is.GreaterThanOrEqualTo(0));
                }
                if (verificationMode is 159 or 160)
                {
                    var isAndRoot = verificationMode == 159;
                    for (var blockIndex = 0; blockIndex < 2; blockIndex++)
                    {
                        Assert.That(bodyReader.GetBlockCode(blockIndex)
                            .IndexOf((byte)0xB1),
                            Is.GreaterThanOrEqualTo(0));
                    }
                    for (var blockIndex = 2; blockIndex < 4; blockIndex++)
                    {
                        Assert.That(bodyReader.GetBlockCode(blockIndex)
                            .IndexOf((byte)0xB0),
                            Is.GreaterThanOrEqualTo(0));
                    }
                    var firstEdge = bodyReader.GetRecord(
                        expectedBlockCount);
                    var secondEdge = bodyReader.GetRecord(
                        expectedBlockCount + 1);
                    Assert.That(firstEdge[0], Is.EqualTo((byte)6));
                    Assert.That(secondEdge[0], Is.EqualTo((byte)6));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(firstEdge[4..]),
                        Is.EqualTo((ushort)(isAndRoot ? 1 : 2)));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(firstEdge[6..]),
                        Is.EqualTo((ushort)(isAndRoot ? 3 : 1)));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(secondEdge[4..]),
                        Is.EqualTo((ushort)2));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(secondEdge[6..]),
                        Is.EqualTo((ushort)3));
                }
                if (verificationMode == 161)
                {
                    var trueBlockCode = bodyReader.GetBlockCode(1);
                    Assert.That(trueBlockCode.IndexOf((byte)0x90),
                        Is.GreaterThanOrEqualTo(0));
                    var methodToken
                        = BinaryPrimitives.ReadUInt32LittleEndian(
                            bodyReader.MethodMap);
                    Assert.That(methodToken,
                        Is.EqualTo(0x1C00_0001u));
                    var callSite = bodyReader.GetRecord(
                        bodyReader.RecordCount - 1);
                    Assert.That(callSite[0], Is.EqualTo((byte)0x02));
                    Assert.That(callSite[4], Is.EqualTo((byte)1));
                    Assert.That(callSite[5], Is.EqualTo((byte)2));
                }
                if (verificationMode is 162 or 163 or 164 or 167)
                {
                    var assignmentBlockIndex
                        = verificationMode == 164 ? 1 : 2;
                    var returnBlockIndex
                        = verificationMode == 164 ? 2 : 1;
                    Assert.That(bodyReader.GetBlockCode(
                            assignmentBlockIndex)
                        .IndexOf((byte)0xB0),
                        Is.GreaterThanOrEqualTo(0));
                    Assert.That(bodyReader.GetBlockCode(returnBlockIndex)
                        .IndexOf((byte)0x17),
                        Is.GreaterThanOrEqualTo(0));
                    Assert.That(bodyReader.GetBlockCode(3)
                        .IndexOf((byte)0x17),
                        Is.GreaterThanOrEqualTo(0));
                }
                if (verificationMode is 165 or 166 or 168)
                {
                    var isAndRoot = verificationMode is 165 or 168;
                    for (var blockIndex = 0; blockIndex < 2; blockIndex++)
                    {
                        Assert.That(bodyReader.GetBlockCode(blockIndex)
                            .IndexOf((byte)0xB1),
                            Is.GreaterThanOrEqualTo(0));
                    }
                    Assert.That(bodyReader.GetBlockCode(2)
                        .IndexOf((byte)0x17),
                        Is.GreaterThanOrEqualTo(0));
                    Assert.That(bodyReader.GetBlockCode(3)
                        .IndexOf((byte)0xB0),
                        Is.GreaterThanOrEqualTo(0));
                    Assert.That(bodyReader.GetBlockCode(4)
                        .IndexOf((byte)0x17),
                        Is.GreaterThanOrEqualTo(0));
                    var firstEdge = bodyReader.GetRecord(
                        expectedBlockCount);
                    var secondEdge = bodyReader.GetRecord(
                        expectedBlockCount + 1);
                    Assert.That(firstEdge[0], Is.EqualTo((byte)6));
                    Assert.That(secondEdge[0], Is.EqualTo((byte)6));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(firstEdge[4..]),
                        Is.EqualTo((ushort)(isAndRoot ? 1 : 2)));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(firstEdge[6..]),
                        Is.EqualTo((ushort)(isAndRoot ? 3 : 1)));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(secondEdge[4..]),
                        Is.EqualTo((ushort)2));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(secondEdge[6..]),
                        Is.EqualTo((ushort)3));
                }
                if (verificationMode == 169)
                {
                    for (var blockIndex = 1; blockIndex < 3; blockIndex++)
                    {
                        Assert.That(bodyReader.GetBlockCode(blockIndex)
                            .IndexOf((byte)0xB0),
                            Is.GreaterThanOrEqualTo(0));
                    }
                }
                if (verificationMode is 170 or 171)
                {
                    var isAndRoot = verificationMode == 170;
                    for (var blockIndex = 0; blockIndex < 2; blockIndex++)
                    {
                        Assert.That(bodyReader.GetBlockCode(blockIndex)
                            .IndexOf((byte)0xB1),
                            Is.GreaterThanOrEqualTo(0));
                    }
                    for (var blockIndex = 2; blockIndex < 4; blockIndex++)
                    {
                        Assert.That(bodyReader.GetBlockCode(blockIndex)
                            .IndexOf((byte)0xB0),
                            Is.GreaterThanOrEqualTo(0));
                    }
                    var firstEdge = bodyReader.GetRecord(
                        expectedBlockCount);
                    var secondEdge = bodyReader.GetRecord(
                        expectedBlockCount + 1);
                    Assert.That(firstEdge[0], Is.EqualTo((byte)6));
                    Assert.That(secondEdge[0], Is.EqualTo((byte)6));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(firstEdge[4..]),
                        Is.EqualTo((ushort)(isAndRoot ? 1 : 2)));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(firstEdge[6..]),
                        Is.EqualTo((ushort)(isAndRoot ? 3 : 1)));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(secondEdge[4..]),
                        Is.EqualTo((ushort)2));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(secondEdge[6..]),
                        Is.EqualTo((ushort)3));
                }
                if (verificationMode is 172 or 173 or 174)
                {
                    Assert.That(bodyReader.GetBlockCode(0)
                        .IndexOf((byte)0xB0),
                        Is.GreaterThanOrEqualTo(0));
                    Assert.That(bodyReader.GetBlockCode(1)
                        .IndexOf((byte)0xB1),
                        Is.GreaterThanOrEqualTo(0));
                    Assert.That(bodyReader.GetBlockCode(2)
                        .IndexOf((byte)0xB0),
                        Is.GreaterThanOrEqualTo(0));
                    var loopEdge = bodyReader.GetRecord(
                        expectedBlockCount);
                    Assert.That(loopEdge[0], Is.EqualTo((byte)6));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(loopEdge[4..]),
                        Is.EqualTo((ushort)2));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(loopEdge[6..]),
                        Is.EqualTo((ushort)3));
                }
                if (verificationMode is 136 or 137)
                {
                    Assert.That(bodyReader.GetBlockCode(1)
                        .IndexOf((byte)0xB1),
                        Is.GreaterThanOrEqualTo(0));
                    Assert.That(bodyReader.GetBlockCode(2)
                        .IndexOf((byte)0xB0),
                        Is.GreaterThanOrEqualTo(0));
                    Assert.That(bodyReader.GetBlockCode(3)
                        .IndexOf((byte)0xB0),
                        Is.GreaterThanOrEqualTo(0));
                    var firstEdge = bodyReader.GetRecord(
                        expectedBlockCount);
                    var secondEdge = bodyReader.GetRecord(
                        expectedBlockCount + 1);
                    Assert.That(firstEdge[0], Is.EqualTo((byte)6));
                    Assert.That(secondEdge[0], Is.EqualTo((byte)6));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(firstEdge[4..]),
                        Is.EqualTo((ushort)(verificationMode == 136
                            ? 1 : 2)));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(firstEdge[6..]),
                        Is.EqualTo((ushort)(verificationMode == 136
                            ? 3 : 1)));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(secondEdge[4..]),
                        Is.EqualTo((ushort)2));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(secondEdge[6..]),
                        Is.EqualTo((ushort)3));
                }
                else if (verificationMode is 138 or 147 or 150)
                {
                    Assert.That(bodyReader.GetBlockCode(1)
                        .IndexOf((byte)0xB1),
                        Is.GreaterThanOrEqualTo(0));
                    for (mut int32 blockIndex = 2;
                        blockIndex <= 4;
                        blockIndex = blockIndex + 1)
                    {
                        Assert.That(bodyReader.GetBlockCode(blockIndex)
                            .IndexOf((byte)0xB0),
                            Is.GreaterThanOrEqualTo(0));
                    }
                    var rootEdge = bodyReader.GetRecord(
                        expectedBlockCount);
                    var nestedEdge = bodyReader.GetRecord(
                        expectedBlockCount + 1);
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(rootEdge[4..]),
                        Is.EqualTo((ushort)1));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(rootEdge[6..]),
                        Is.EqualTo((ushort)4));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(nestedEdge[4..]),
                        Is.EqualTo((ushort)2));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(nestedEdge[6..]),
                        Is.EqualTo((ushort)3));
                }
                else if (verificationMode is 144 or 145 or 146 or 148
                    or 149 or 152 or 153 or 154 or 155 or 156)
                {
                    for (var blockIndex = 0;
                        blockIndex < 3;
                        blockIndex++)
                    {
                        Assert.That(bodyReader.GetBlockCode(blockIndex)
                            .IndexOf((byte)0xB1),
                            Is.GreaterThanOrEqualTo(0));
                    }
                    var firstEdge = bodyReader.GetRecord(
                        expectedBlockCount);
                    var secondEdge = bodyReader.GetRecord(
                        expectedBlockCount + 1);
                    var thirdEdge = bodyReader.GetRecord(
                        expectedBlockCount + 2);
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(firstEdge[4..]),
                        Is.EqualTo((ushort)(verificationMode is 144 or 146
                            or 148 or 149 or 152
                            ? 1 : 2)));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(firstEdge[6..]),
                        Is.EqualTo((ushort)(verificationMode is 144 or 146
                            or 148 or 149 or 152
                            ? 2 : 1)));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(secondEdge[4..]),
                        Is.EqualTo((ushort)(verificationMode is 144 or 146
                            or 148 or 149 or 152
                            ? 3 : 2)));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(secondEdge[6..]),
                        Is.EqualTo((ushort)(verificationMode is 144 or 146
                            or 148 or 149 or 152
                            ? 2 : 4)));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(thirdEdge[4..]),
                        Is.EqualTo((ushort)3));
                    Assert.That(BinaryPrimitives
                        .ReadUInt16LittleEndian(thirdEdge[6..]),
                        Is.EqualTo((ushort)4));
                    if (verificationMode is 154 or 155)
                    {
                        Assert.That(bodyReader.GetBlockCode(3)
                            .IndexOf((byte)0xB0),
                            Is.GreaterThanOrEqualTo(0));
                        Assert.That(bodyReader.GetBlockCode(4)
                            .IndexOf((byte)0xB0),
                            Is.GreaterThanOrEqualTo(0));
                    }
                    if (verificationMode == 156)
                    {
                        Assert.That(bodyReader.GetBlockCode(3)
                            .IndexOf((byte)0xB0),
                            Is.GreaterThanOrEqualTo(0));
                        Assert.That(bodyReader.GetBlockCode(4)
                            .IndexOf((byte)0xB0),
                            Is.GreaterThanOrEqualTo(0));
                    }
                    if (verificationMode == 153)
                    {
                        Assert.That(bodyReader.GetBlockCode(3)
                            .IndexOf((byte)0x17),
                            Is.GreaterThanOrEqualTo(0));
                        Assert.That(bodyReader.GetBlockCode(4)
                            .IndexOf((byte)0x17),
                            Is.GreaterThanOrEqualTo(0));
                    }
                }
                else if ((verificationMode >= 131
                        && verificationMode <= 135)
                    || verificationMode is 139 or 140 or 151)
                {
                    Assert.That(bodyReader.GetBlockCode(1)
                        .IndexOf((byte)0xB0),
                        Is.GreaterThanOrEqualTo(0));
                    Assert.That(bodyReader.GetBlockCode(2)
                        .IndexOf((byte)0xB0),
                        Is.GreaterThanOrEqualTo(0));
                }
                if ((verificationMode is < 136 or > 138)
                    && verificationMode is not 145
                        and not 147 and not 150
                        and not 159 and not 160 and not 161
                        and not 165 and not 166 and not 168
                        and not 172 and not 173 and not 174
                        and not 170 and not 171)
                {
                    var edgeRecord
                        = bodyReader.GetRecord(expectedBlockCount);
                    Assert.That(edgeRecord[0], Is.EqualTo((byte)6));
                    Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                        edgeRecord[4..]), Is.EqualTo((ushort)1));
                    Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                        edgeRecord[6..]), Is.EqualTo((ushort)2));
                }
            }
            else if (verificationMode is >= 90 and <= 95)
            {
                Assert.That(bodyReader.Code.IndexOf((byte)0xC0),
                    Is.EqualTo(-1));
                Assert.That(bodyReader.Code.IndexOf((byte)0xD4),
                    Is.GreaterThanOrEqualTo(0));
                Assert.That(bodyReader.Code.IndexOf((byte)0xD6),
                    Is.GreaterThanOrEqualTo(0));
            }
            else
            {
                Assert.That(bodyReader.Code.IndexOf((byte)0x07),
                    Is.GreaterThanOrEqualTo(0));
            }

            var decoderInput = new StandaloneMethodImportArtifactInput(
                Agreement, "consumer/module",
                new TrRmStandardArtifactInput(SchemaIdentity, artifact),
                input.Artifacts);
            var decoderBuffers = decoderInput.Capture()
                .CreateSourceFreeReaderHostBuffers();
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
                "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneTypeSpecValidation.terrum",
                "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodEffects.terrum",
                "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodOutputStates.terrum",
                "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodRoots.terrum",
                "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodImports.terrum",
                "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodImportBindings.terrum",
                "sources/Terrum.CodeAnalysis.Terrum/Semantics/StandaloneOwnedSourceDecoder.terrum",
            };
            var expectedDecoderBodyCount
                = verificationMode == 58 ? 2 : 1;
            var expectedDecoderCallCount = verificationMode is 4 or 158
                    or 161 ? 1
                : verificationMode == 58 ? 2 : 0;
            var expectedDecoderConstantMapCount
                = verificationMode is 4 or 23 or 25 or 26
                    or 29 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37
                    or 52 or 53 or 58 or 59 or 71 or 72 or 73
                    or 75 or 77 or 82 or >= 90 and <= 95
                    or >= 96 and <= 103
                    or >= 104 and <= 109
                    or 115 or 132 or 133 or 136 or 137 or 158
                    or 172 or 173 or 174
                    ? 1 : 0;
            if (verificationMode is 71 or 130 or 144 or 145 or 146
                or 148 or 149 or 152 or 153)
            {
                expectedDecoderConstantMapCount = 2;
            }
            var expectedDecoderBodyShape = verificationMode switch
            {
                130 => "Host.StandaloneDecoderBodies[19] != 3 || "
                    + "Host.StandaloneDecoderBodies[21] != 4",
                131 => "Host.StandaloneDecoderBodies[19] != 4 || "
                    + "Host.StandaloneDecoderBodies[21] != 5",
                132 or 133 => "Host.StandaloneDecoderBodies[19] != 4 || "
                    + "Host.StandaloneDecoderBodies[21] != 5",
                134 => "Host.StandaloneDecoderBodies[19] != 4 || "
                    + "Host.StandaloneDecoderBodies[21] != 5",
                135 => "Host.StandaloneDecoderBodies[19] != 4 || "
                    + "Host.StandaloneDecoderBodies[21] != 5",
                136 or 137 => "Host.StandaloneDecoderBodies[19] != 5 || "
                    + "Host.StandaloneDecoderBodies[21] != 7",
                138 => "Host.StandaloneDecoderBodies[19] != 6 || "
                    + "Host.StandaloneDecoderBodies[21] != 8",
                139 => "Host.StandaloneDecoderBodies[19] != 4 || "
                    + "Host.StandaloneDecoderBodies[21] != 5",
                140 => "Host.StandaloneDecoderBodies[19] != 4 || "
                    + "Host.StandaloneDecoderBodies[21] != 5",
                144 => "Host.StandaloneDecoderBodies[19] != 6 || "
                    + "Host.StandaloneDecoderBodies[21] != 9",
                145 => "Host.StandaloneDecoderBodies[19] != 6 || "
                    + "Host.StandaloneDecoderBodies[21] != 9",
                146 => "Host.StandaloneDecoderBodies[19] != 6 || "
                    + "Host.StandaloneDecoderBodies[21] != 9",
                147 => "Host.StandaloneDecoderBodies[19] != 6 || "
                    + "Host.StandaloneDecoderBodies[21] != 8",
                148 => "Host.StandaloneDecoderBodies[19] != 6 || "
                    + "Host.StandaloneDecoderBodies[21] != 9",
                150 => "Host.StandaloneDecoderBodies[19] != 6 || "
                    + "Host.StandaloneDecoderBodies[21] != 8",
                151 => "Host.StandaloneDecoderBodies[19] != 4 || "
                    + "Host.StandaloneDecoderBodies[21] != 5",
                152 => "Host.StandaloneDecoderBodies[19] != 6 || "
                    + "Host.StandaloneDecoderBodies[21] != 9",
                153 => "Host.StandaloneDecoderBodies[19] != 5 || "
                    + "Host.StandaloneDecoderBodies[21] != 8",
                154 or 155
                    => "Host.StandaloneDecoderBodies[19] != 6 || "
                        + "Host.StandaloneDecoderBodies[21] != 9",
                156 => "Host.StandaloneDecoderBodies[19] != 6 || "
                    + "Host.StandaloneDecoderBodies[21] != 9",
                158 => "Host.StandaloneDecoderBodies[19] != 3 || "
                    + "Host.StandaloneDecoderBodies[21] != 5",
                161 => "Host.StandaloneDecoderBodies[19] != 3 || "
                    + "Host.StandaloneDecoderBodies[21] != 5",
                159 or 160 => "Host.StandaloneDecoderBodies[19] != 5 || "
                    + "Host.StandaloneDecoderBodies[21] != 7",
                162 or 163 or 164
                    => "Host.StandaloneDecoderBodies[19] != 4 || "
                    + "Host.StandaloneDecoderBodies[21] != 5",
                165 or 166
                    => "Host.StandaloneDecoderBodies[19] != 5 || "
                        + "Host.StandaloneDecoderBodies[21] != 7",
                167 => "Host.StandaloneDecoderBodies[19] != 4 || "
                    + "Host.StandaloneDecoderBodies[21] != 5",
                168 => "Host.StandaloneDecoderBodies[19] != 5 || "
                    + "Host.StandaloneDecoderBodies[21] != 7",
                169 => "Host.StandaloneDecoderBodies[19] != 4 || "
                    + "Host.StandaloneDecoderBodies[21] != 5",
                170 or 171
                    => "Host.StandaloneDecoderBodies[19] != 5 || "
                        + "Host.StandaloneDecoderBodies[21] != 7",
                172 => "Host.StandaloneDecoderBodies[19] != 4 || "
                    + "Host.StandaloneDecoderBodies[21] != 5",
                173 or 174
                    => "Host.StandaloneDecoderBodies[19] != 4 || "
                        + "Host.StandaloneDecoderBodies[21] != 5",
                149 => "Host.StandaloneDecoderBodies[19] != 6 || "
                    + "Host.StandaloneDecoderBodies[21] != 9",
                _ => "false",
            };
            var decoderVerificationSource = $$"""
                namespace Terrum.CodeAnalysis;
                int32 Main() {
                    int32 status = DecodeStandaloneModuleMetadata();
                    if (status != 0
                        || Host.StandaloneDecoderStatus[0] != 0
                        || Host.StandaloneDecoderBodyCount[0]
                            != {{expectedDecoderBodyCount}}
                        || Host.StandaloneDecoderCallCount[0]
                            != {{expectedDecoderCallCount}}
                        || Host.StandaloneDecoderConstantMapCount[0]
                            != {{expectedDecoderConstantMapCount}}
                        || ({{expectedDecoderBodyShape}})) {
                        return 80;
                    }
                    return 0;
                }
                """;
            var decoderComponents
                = new List<SyntaxTree>(decoderSourcePaths.Length + 1);
            foreach (var sourcePath in decoderSourcePaths)
            {
                var path = TerrumProjectPaths.RepositoryFile(root,
                    sourcePath);
                var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path)
                    .ConfigureAwait(true), path);
                Assert.That(tree.HasErrors, Is.False,
                    string.Join(Environment.NewLine, tree.Diagnostics));
                decoderComponents.Add(tree);
            }
            decoderComponents.Add(SyntaxTree.Parse(
                decoderVerificationSource,
                "standalone-source-call-decoder-tests.terrum"));
            var decoderCompilation = Compilation.Create(decoderComponents,
                decoderBuffers, policy);
            Assert.That(decoderCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    decoderCompilation.Diagnostics));
            var decoderProgram = decoderCompilation.Program
                ?? throw new AssertionException(
                    "Expected the source-free Body decoder validator.");
            var decoderExecution = NativeJit.ExecuteWithData(
                decoderProgram, (NativeRuntimePolicy)policy);
            Assert.That(decoderExecution.ExitCode, Is.EqualTo(0));
            if (verificationMode == 161)
            {
                decoderComponents[^1] = SyntaxTree.Parse("""
                    namespace Terrum.CodeAnalysis;
                    int32 Main() {
                        int32 status = DecodeStandaloneModuleMetadata();
                        if (status != 0) { return status; }
                        status = BuildStandaloneImportedTargetPlan();
                        if (status != 0) { return status; }
                        status = ValidateStandaloneImportedTargetBodies();
                        if (status != 0) { return status; }
                        if (Host.StandaloneDecoderCallCount[0] != 1
                            || Host.StandaloneDecoderCalls[3] != 2
                            || Host.StandaloneDecoderCalls[14] != 1
                            || Host.StandaloneDecoderCalls[20] <= 0
                            || Host.StandaloneDecoderCalls[21] <= 0
                            || Host.StandaloneDecoderTargetPlanBuilt[0] != 1
                            || Host.StandaloneDecoderTargetBodiesValidated[0]
                                != 1) {
                            return 81;
                        }
                        return 0;
                    }
                    """, "standalone-imported-cfg-target-plan-tests.terrum");
                var targetPlanCompilation = Compilation.Create(
                    decoderComponents, decoderBuffers, policy);
                Assert.That(targetPlanCompilation.HasErrors, Is.False,
                    string.Join(Environment.NewLine,
                        targetPlanCompilation.Diagnostics));
                var targetPlanProgram = targetPlanCompilation.Program
                    ?? throw new AssertionException(
                        "Expected an imported CFG target-plan validator.");
                var targetPlanExecution = NativeJit.ExecuteWithData(
                    targetPlanProgram, (NativeRuntimePolicy)policy);
                Assert.That(targetPlanExecution.ExitCode, Is.EqualTo(0));
            }
            if ((verificationMode is >= 130 and <= 140)
                || verificationMode is 144 or 145 or 146 or 147
                    or 148 or 149 or 150 or 151 or 152 or 153
                    or 154 or 155 or 156 or 158 or 159 or 160
                    or 161 or 162 or 163 or 164 or 165 or 166
                    or 167 or 168 or 169 or 170 or 171
                    or 172 or 173 or 174)
            {
                var artifactIndex = new TrRmArtifactIndex(artifact);
                Assert.That(artifactIndex.TryGetSection(0x0020,
                        out var bodySection, out var bodyCount),
                    Is.True);
                var bodyTable = new TrRmBodyTableReader(
                    bodySection, bodyCount);
                var bodyRow = bodyTable.GetRow(1);
                var bodyReader = new TrRmRegisterBodyReader(bodyRow);
                var sectionCount = checked((int)BinaryPrimitives
                    .ReadUInt32LittleEndian(artifact.AsSpan(12)));
                var bodySectionStart = -1;
                for (var section = 0; section < sectionCount; section++)
                {
                    var entryOffset = checked(32 + section * 16);
                    if (BinaryPrimitives.ReadUInt16LittleEndian(
                            artifact.AsSpan(entryOffset)) == 0x0020)
                    {
                        bodySectionStart = checked((int)BinaryPrimitives
                            .ReadUInt32LittleEndian(
                                artifact.AsSpan(entryOffset + 8)));
                        break;
                    }
                }
                Assert.That(bodySectionStart, Is.GreaterThanOrEqualTo(32));
                var mapCountBytes
                    = ((bodyReader.MapMask & 1) != 0 ? 4 : 0)
                    + ((bodyReader.MapMask & 2) != 0 ? 4 : 0)
                    + ((bodyReader.MapMask & 4) != 0 ? 4 : 0)
                    + ((bodyReader.MapMask & 8) != 0 ? 4 : 0)
                    + ((bodyReader.MapMask & 16) != 0 ? 4 : 0);
                var mapDataBytes = checked(bodyReader.HomeCount * 8
                    + bodyReader.TypeCount * 4
                    + bodyReader.MethodCount * 4
                    + bodyReader.FieldCount * 4
                    + bodyReader.ConstantCount * 8);
                var blockStart = checked(5 + mapCountBytes + 12
                    + mapDataBytes);
                var recordIndexStart = checked(blockStart
                    + bodyReader.BlockCount * 12);
                var recordDataStart = checked(recordIndexStart
                    + (bodyReader.RecordCount + 1) * sizeof(uint));
                var bodyIndexLength = checked((bodyCount + 1)
                    * sizeof(uint));
                var bodyRowOffset = checked((int)BinaryPrimitives
                    .ReadUInt32LittleEndian(artifact.AsSpan(
                        bodySectionStart + sizeof(uint))));
                var bodyRowAbsolute = checked(bodySectionStart
                    + bodyIndexLength + bodyRowOffset);

                void AssertMalformedBodyRejected(byte[] malformedArtifact,
                    string sourcePath)
                {
                    _ = Assert.Throws<InvalidDataException>(() =>
                        TrRmExecutableCompiler.LoadStandard(
                            new TrRmStandardArtifactInput(SchemaIdentity,
                                malformedArtifact),
                            input.UsingModuleIdentity, input.Artifacts));
                    var malformedInput
                        = new StandaloneMethodImportArtifactInput(
                            Agreement, "consumer/module",
                            new TrRmStandardArtifactInput(
                                SchemaIdentity, malformedArtifact),
                            input.Artifacts);
                    var malformedBuffers = malformedInput.Capture()
                        .CreateSourceFreeReaderHostBuffers();
                    decoderComponents[^1] = SyntaxTree.Parse("""
                        namespace Terrum.CodeAnalysis;
                        int32 Main() {
                            int32 status = DecodeStandaloneModuleMetadata();
                            return status == 3
                                && Host.StandaloneDecoderStatus[0] == 3
                                ? 0 : 1;
                        }
                        """, sourcePath);
                    var malformedCompilation = Compilation.Create(
                        decoderComponents, malformedBuffers, policy);
                    Assert.That(malformedCompilation.HasErrors, Is.False,
                        string.Join(Environment.NewLine,
                            malformedCompilation.Diagnostics));
                    var malformedProgram = malformedCompilation.Program
                        ?? throw new AssertionException(
                            "Expected a malformed-body source-free decoder.");
                    var malformedExecution = NativeJit.ExecuteWithData(
                        malformedProgram, (NativeRuntimePolicy)policy);
                    Assert.That(malformedExecution.ExitCode,
                        Is.EqualTo(0));
                }

                var malformedBlockArtifact = artifact.ToArray();
                BinaryPrimitives.WriteUInt16LittleEndian(
                    malformedBlockArtifact.AsSpan(bodyRowAbsolute
                        + blockStart + 12 + 8), 0);
                AssertMalformedBodyRejected(malformedBlockArtifact,
                    "standalone-malformed-block-entry-tests.terrum");

                var malformedEdgeArtifact = artifact.ToArray();
                var edgeRecordOffset = checked((int)BinaryPrimitives
                    .ReadUInt32LittleEndian(bodyRow.Slice(
                        recordIndexStart + (bodyReader.BlockCount
                            * sizeof(uint)))));
                BinaryPrimitives.WriteUInt16LittleEndian(
                    malformedEdgeArtifact.AsSpan(bodyRowAbsolute
                        + recordDataStart + edgeRecordOffset + 4),
                    ushort.MaxValue);
                AssertMalformedBodyRejected(malformedEdgeArtifact,
                    "standalone-malformed-conditional-edge-tests.terrum");

                var loaded = TrRmExecutableCompiler.LoadStandard(
                    new TrRmStandardArtifactInput(SchemaIdentity, artifact),
                    input.UsingModuleIdentity, input.Artifacts);
                var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
                    input.UsingModuleIdentity, SchemaIdentity, 1);
                loaded = TrRmExecutableCompiler.LoadStandard(
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        reemitted), input.UsingModuleIdentity,
                    input.Artifacts);
                if (verificationMode == 161)
                {
                    var importedTarget = loaded.Functions.Single(
                        static function => function.ModuleImport is not null);
                    Assert.That(importedTarget.IsExternal, Is.True);
                    Assert.That(importedTarget.IsModuleReference, Is.True);
                    Assert.That(importedTarget.ModuleImport!.ProducerIdentity,
                        Is.EqualTo("provider/leaf"));
                    Assert.That(importedTarget.ModuleImport.ExportKey,
                        Is.EqualTo("api.Imported"));
                    var reemittedIndex = new TrRmArtifactIndex(reemitted);
                    Assert.That(reemittedIndex.TryGetSection(0x0020,
                            out var reemittedBodySection,
                            out var reemittedBodyCount),
                        Is.True);
                    var reemittedBodies = new TrRmBodyTableReader(
                        reemittedBodySection, reemittedBodyCount);
                    var reemittedBody
                        = new TrRmRegisterBodyReader(
                            reemittedBodies.GetRow(1));
                    Assert.That(BinaryPrimitives
                        .ReadUInt32LittleEndian(reemittedBody.MethodMap),
                        Is.EqualTo(0x1C00_0001u));
                }
                var parameterCount = verificationMode switch {
                    130 => 1,
                    131 => 3,
                    132 or 133 => 2,
                    136 or 137 => 3,
                    138 => 5,
                    139 => 3,
                    140 => 3,
                    144 => 3,
                    145 => 3,
                    146 => 3,
                    147 => 5,
                    148 => 3,
                    150 => 6,
                    151 => 4,
                    152 => 4,
                    153 => 3,
                    154 => 6,
                    155 => 5,
                    156 => 5,
                    158 => 2,
                    161 => 2,
                    162 => 3,
                    149 => 3,
                    163 => 4,
                    164 => 3,
                    165 or 166 => 4,
                    167 => 3,
                    168 => 4,
                    169 => 5,
                    170 or 171 => 6,
                    172 or 173 or 174 => 2,
                    _ => 4,
                };
                var mainIndex = -1;
                for (var functionIndex = 0;
                    functionIndex < loaded.Functions.Length;
                    functionIndex++)
                {
                    var function = loaded.Functions[functionIndex];
                    if (!function.IsExternal
                        && function.ParameterCount == parameterCount
                        && function.Name.EndsWith("Main",
                            StringComparison.Ordinal))
                    {
                        Assert.That(mainIndex, Is.EqualTo(-1),
                            "Expected one emitted source Main function.");
                        mainIndex = functionIndex;
                    }
                }
                Assert.That(mainIndex, Is.GreaterThanOrEqualTo(0));
                var main = loaded.Functions[mainIndex];

                BoundProgram CreateInvocation(params long[] values)
                {
                    Assert.That(values.Length,
                        Is.EqualTo(main.ParameterCount));
                    var arguments
                        = ImmutableArray.CreateBuilder<BoundExpression>(
                            values.Length);
                    for (var parameter = 0;
                        parameter < values.Length; parameter++)
                    {
                        arguments.Add(new BoundConstant(values[parameter],
                            main.Variables[parameter].Type, default));
                    }

                    var call = new BoundCall(mainIndex,
                        arguments.MoveToImmutable(), main.ReturnType,
                        default);
                    BoundExpression result = main.ReturnType
                        == PrimitiveType.Bool
                        ? new BoundConditional(call,
                            new BoundConstant(1, PrimitiveType.Int32, default),
                            new BoundConstant(0, PrimitiveType.Int32, default),
                            PrimitiveType.Int32, default)
                        : call;
                    var entry = new BoundFunction("$runtimeEntry",
                        PrimitiveType.Int32, [], 0,
                        new BoundBlock([
                            new BoundReturn(result, default),
                        ], default), default);
                    return loaded with {
                        Functions = loaded.Functions.Add(entry),
                        EntryPoint = loaded.Functions.Length,
                    };
                }

                int Execute(params long[] values)
                    => NativeJit.Execute(CreateInvocation(values),
                        (NativeRuntimePolicy)policy);

                switch (verificationMode)
                {
                    case 130:
                        Assert.That(Execute(1), Is.EqualTo(1));
                        Assert.That(Execute(0), Is.EqualTo(0));
                        break;
                    case 131:
                        Assert.That(Execute(1, 42, 99), Is.EqualTo(42));
                        Assert.That(Execute(0, 42, 99), Is.EqualTo(99));
                        break;
                    case 132:
                        Assert.That(Execute(1, 1), Is.EqualTo(1));
                        Assert.That(Execute(1, 0), Is.EqualTo(0));
                        Assert.That(Execute(0, 1), Is.EqualTo(0));
                        break;
                    case 133:
                        Assert.That(Execute(0, 0), Is.EqualTo(0));
                        Assert.That(Execute(0, 1), Is.EqualTo(1));
                        Assert.That(Execute(1, 0), Is.EqualTo(1));
                        break;
                    case 134:
                        Assert.That(Execute(1, 7, 11, 13),
                            Is.EqualTo(11));
                        Assert.That(Execute(0, 7, 11, 13),
                            Is.EqualTo(13));
                        break;
                    case 135:
                        Assert.That(Execute(1, 7, 11, 13),
                            Is.EqualTo(7));
                        Assert.That(Execute(0, 7, 11, 13),
                            Is.EqualTo(11));
                        break;
                    case 136:
                        Assert.That(Execute(1, 1, 1), Is.EqualTo(1));
                        Assert.That(Execute(1, 1, 0), Is.EqualTo(0));
                        Assert.That(Execute(1, 0, 1), Is.EqualTo(0));
                        Assert.That(Execute(0, 1, 1), Is.EqualTo(0));
                        break;
                    case 137:
                        Assert.That(Execute(0, 0, 0), Is.EqualTo(0));
                        Assert.That(Execute(0, 1, 0), Is.EqualTo(1));
                        Assert.That(Execute(0, 0, 1), Is.EqualTo(1));
                        Assert.That(Execute(1, 0, 0), Is.EqualTo(1));
                        break;
                    case 138:
                        Assert.That(Execute(1, 1, 17, 19, 23),
                            Is.EqualTo(17));
                        Assert.That(Execute(1, 0, 17, 19, 23),
                            Is.EqualTo(19));
                        Assert.That(Execute(0, 1, 17, 19, 23),
                            Is.EqualTo(23));
                        break;
                    case 139:
                        Assert.That(Execute(1, 7, 11),
                            Is.EqualTo(11));
                        Assert.That(Execute(0, 7, 11),
                            Is.EqualTo(7));
                        break;
                    case 140:
                        Assert.That(Execute(1, 17, 23),
                            Is.EqualTo(17));
                        Assert.That(Execute(0, 17, 23),
                            Is.EqualTo(23));
                        break;
                    case 144:
                        Assert.That(Execute(0, 0, 0),
                            Is.EqualTo(0));
                        Assert.That(Execute(0, 0, 1),
                            Is.EqualTo(1));
                        Assert.That(Execute(1, 0, 0),
                            Is.EqualTo(0));
                        Assert.That(Execute(1, 0, 1),
                            Is.EqualTo(1));
                        Assert.That(Execute(1, 1, 0),
                            Is.EqualTo(1));
                        break;
                    case 145:
                        Assert.That(Execute(0, 0, 1),
                            Is.EqualTo(0));
                        Assert.That(Execute(0, 1, 1),
                            Is.EqualTo(1));
                        Assert.That(Execute(1, 0, 1),
                            Is.EqualTo(1));
                        Assert.That(Execute(0, 1, 0),
                            Is.EqualTo(0));
                        Assert.That(Execute(1, 1, 0),
                            Is.EqualTo(0));
                        break;
                    case 146:
                        Assert.That(Execute(0, 0, 0),
                            Is.EqualTo(0));
                        Assert.That(Execute(0, 0, 1),
                            Is.EqualTo(1));
                        Assert.That(Execute(1, 0, 0),
                            Is.EqualTo(0));
                        Assert.That(Execute(1, 0, 1),
                            Is.EqualTo(1));
                        Assert.That(Execute(1, 1, 0),
                            Is.EqualTo(1));
                        break;
                    case 147:
                        Assert.That(Execute(1, 1, 17, 19, 23),
                            Is.EqualTo(17));
                        Assert.That(Execute(1, 0, 17, 19, 23),
                            Is.EqualTo(19));
                        Assert.That(Execute(0, 1, 17, 19, 23),
                            Is.EqualTo(23));
                        break;
                    case 148:
                        Assert.That(Execute(0, 0, 0),
                            Is.EqualTo(0));
                        Assert.That(Execute(0, 0, 1),
                            Is.EqualTo(1));
                        Assert.That(Execute(1, 0, 0),
                            Is.EqualTo(0));
                        Assert.That(Execute(1, 0, 1),
                            Is.EqualTo(1));
                        Assert.That(Execute(1, 1, 0),
                            Is.EqualTo(1));
                        break;
                    case 149:
                        Assert.That(Execute(0, 0, 0),
                            Is.EqualTo(0));
                        Assert.That(Execute(0, 0, 1),
                            Is.EqualTo(1));
                        Assert.That(Execute(1, 0, 0),
                            Is.EqualTo(0));
                        Assert.That(Execute(1, 0, 1),
                            Is.EqualTo(1));
                        Assert.That(Execute(1, 1, 0),
                            Is.EqualTo(1));
                        break;
                    case 150:
                        Assert.That(Execute(1, 1, 17, 19, 23, 7),
                            Is.EqualTo(17));
                        Assert.That(Execute(1, 0, 17, 19, 23, 7),
                            Is.EqualTo(19));
                        Assert.That(Execute(0, 1, 17, 19, 23, 7),
                            Is.EqualTo(23));
                        break;
                    case 151:
                        Assert.That(Execute(1, 17, 23, 3),
                            Is.EqualTo(20));
                        Assert.That(Execute(0, 17, 23, 3),
                            Is.EqualTo(26));
                        break;
                    case 152:
                        Assert.That(Execute(1, 1, 0, 1),
                            Is.EqualTo(1));
                        Assert.That(Execute(1, 0, 1, 1),
                            Is.EqualTo(1));
                        Assert.That(Execute(0, 0, 0, 0),
                            Is.EqualTo(1));
                        Assert.That(Execute(0, 0, 0, 1),
                            Is.EqualTo(0));
                        break;
                    case 153:
                        Assert.That(Execute(0, 0, 0),
                            Is.EqualTo(0));
                        Assert.That(Execute(0, 0, 1),
                            Is.EqualTo(1));
                        Assert.That(Execute(1, 0, 0),
                            Is.EqualTo(0));
                        Assert.That(Execute(1, 0, 1),
                            Is.EqualTo(1));
                        Assert.That(Execute(1, 1, 0),
                            Is.EqualTo(1));
                        break;
                    case 154:
                        Assert.That(Execute(0, 0, 0, 7, 11, 13),
                            Is.EqualTo(13));
                        Assert.That(Execute(0, 0, 1, 7, 11, 13),
                            Is.EqualTo(11));
                        Assert.That(Execute(1, 0, 0, 7, 11, 13),
                            Is.EqualTo(13));
                        Assert.That(Execute(1, 1, 0, 7, 11, 13),
                            Is.EqualTo(11));
                        break;
                    case 155:
                        Assert.That(Execute(0, 0, 0, 11, 13),
                            Is.EqualTo(13));
                        Assert.That(Execute(0, 0, 1, 11, 13),
                            Is.EqualTo(11));
                        Assert.That(Execute(1, 0, 0, 11, 13),
                            Is.EqualTo(13));
                        Assert.That(Execute(1, 1, 0, 11, 13),
                            Is.EqualTo(11));
                        break;
                    case 156:
                        Assert.That(Execute(0, 0, 0, 7, 11),
                            Is.EqualTo(7));
                        Assert.That(Execute(0, 0, 1, 7, 11),
                            Is.EqualTo(11));
                        Assert.That(Execute(1, 0, 0, 7, 11),
                            Is.EqualTo(7));
                        Assert.That(Execute(1, 1, 0, 7, 11),
                            Is.EqualTo(11));
                        break;
                    case 158:
                        Assert.That(Execute(0, 42), Is.EqualTo(42));
                        Assert.That(Execute(1, 42), Is.EqualTo(42));
                        break;
                    case 159:
                        Assert.That(Execute(0, 0, 7, 11),
                            Is.EqualTo(11));
                        Assert.That(Execute(1, 0, 7, 11),
                            Is.EqualTo(11));
                        Assert.That(Execute(1, 1, 7, 11),
                            Is.EqualTo(7));
                        break;
                    case 160:
                        Assert.That(Execute(0, 0, 7, 11),
                            Is.EqualTo(11));
                        Assert.That(Execute(1, 0, 7, 11),
                            Is.EqualTo(7));
                        Assert.That(Execute(0, 1, 7, 11),
                            Is.EqualTo(7));
                        break;
                    case 162:
                        Assert.That(Execute(1, 17, 23),
                            Is.EqualTo(17));
                        Assert.That(Execute(0, 17, 23),
                            Is.EqualTo(23));
                        break;
                    case 163:
                        Assert.That(Execute(1, 7, 17, 23),
                            Is.EqualTo(17));
                        Assert.That(Execute(0, 7, 17, 23),
                            Is.EqualTo(23));
                        break;
                    case 164:
                        Assert.That(Execute(1, 17, 23),
                            Is.EqualTo(23));
                        Assert.That(Execute(0, 17, 23),
                            Is.EqualTo(17));
                        break;
                    case 165:
                        Assert.That(Execute(1, 1, 7, 11),
                            Is.EqualTo(7));
                        Assert.That(Execute(1, 0, 7, 11),
                            Is.EqualTo(11));
                        Assert.That(Execute(0, 1, 7, 11),
                            Is.EqualTo(11));
                        break;
                    case 166:
                        Assert.That(Execute(0, 0, 7, 11),
                            Is.EqualTo(11));
                        Assert.That(Execute(1, 0, 7, 11),
                            Is.EqualTo(7));
                        Assert.That(Execute(0, 1, 7, 11),
                            Is.EqualTo(7));
                        break;
                    case 167:
                        Assert.That(Execute(1, 17, 23),
                            Is.EqualTo(17));
                        Assert.That(Execute(0, 17, 23),
                            Is.EqualTo(23));
                        break;
                    case 168:
                        Assert.That(Execute(1, 1, 17, 23),
                            Is.EqualTo(17));
                        Assert.That(Execute(1, 0, 17, 23),
                            Is.EqualTo(23));
                        Assert.That(Execute(0, 1, 17, 23),
                            Is.EqualTo(23));
                        break;
                    case 169:
                        Assert.That(Execute(1, 3, 5, 7, 11),
                            Is.EqualTo(12));
                        Assert.That(Execute(0, 3, 5, 7, 11),
                            Is.EqualTo(14));
                        break;
                    case 170:
                        Assert.That(Execute(1, 1, 3, 5, 7, 11),
                            Is.EqualTo(12));
                        Assert.That(Execute(1, 0, 3, 5, 7, 11),
                            Is.EqualTo(14));
                        Assert.That(Execute(0, 1, 3, 5, 7, 11),
                            Is.EqualTo(14));
                        break;
                    case 171:
                        Assert.That(Execute(0, 0, 3, 5, 7, 11),
                            Is.EqualTo(14));
                        Assert.That(Execute(1, 0, 3, 5, 7, 11),
                            Is.EqualTo(12));
                        Assert.That(Execute(0, 1, 3, 5, 7, 11),
                            Is.EqualTo(12));
                        break;
                    case 172:
                        Assert.That(Execute(0, 3), Is.EqualTo(3));
                        Assert.That(Execute(3, 5), Is.EqualTo(5));
                        Assert.That(Execute(5, 2), Is.EqualTo(5));
                        Assert.That(Execute(3, 3), Is.EqualTo(3));
                        break;
                    case 173:
                        Assert.That(Execute(0, 3), Is.EqualTo(3));
                        Assert.That(Execute(3, 5), Is.EqualTo(5));
                        Assert.That(Execute(5, 2), Is.EqualTo(5));
                        break;
                    case 174:
                        Assert.That(Execute(0, 3), Is.EqualTo(1));
                        Assert.That(Execute(3, 5), Is.EqualTo(4));
                        Assert.That(Execute(5, 2), Is.EqualTo(5));
                        break;
                }
            }
            if (verificationMode == 60)
            {
                var malformedArtifact = artifact.ToArray();
                var sectionCount = checked((int)BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        malformedArtifact.AsSpan(12)));
                var bodySectionStart = -1;
                var malformedBodyCount = 0;
                for (var section = 0; section < sectionCount; section++)
                {
                    var entryOffset = checked(32 + (section * 16));
                    if (BinaryPrimitives.ReadUInt16LittleEndian(
                            malformedArtifact.AsSpan(entryOffset)) == 0x0020)
                    {
                        malformedBodyCount = checked((int)BinaryPrimitives
                            .ReadUInt32LittleEndian(
                                malformedArtifact.AsSpan(entryOffset + 4)));
                        bodySectionStart = checked((int)BinaryPrimitives
                            .ReadUInt32LittleEndian(
                                malformedArtifact.AsSpan(entryOffset + 8)));
                        break;
                    }
                }
                Assert.That(bodySectionStart, Is.GreaterThanOrEqualTo(32));
                var bodyIndexLength
                    = checked((malformedBodyCount + 1) * sizeof(uint));
                var bodyDataStart
                    = bodySectionStart + bodyIndexLength;
                var bodyRowOffset = checked((int)BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        malformedArtifact.AsSpan(bodySectionStart + 4)));
                var bodyRowStart = bodyDataStart + bodyRowOffset;
                var bodyRow = bodyTable.GetRow(1);
                var malformedBodyReader
                    = new TrRmRegisterBodyReader(bodyRow);
                var arithmeticCodeOffset
                    = malformedBodyReader.Code.IndexOf((byte)0xC0);
                Assert.That(arithmeticCodeOffset,
                    Is.GreaterThanOrEqualTo(0));
                var codeStart
                    = bodyRow.Length - malformedBodyReader.Code.Length;
                malformedArtifact[bodyRowStart + codeStart
                    + arithmeticCodeOffset + 1] = 0x09;
                var malformedArtifactValues = decoderBuffers
                    .Single(buffer => buffer.Name == "StandaloneArtifacts")
                    .Values.ToArray();
                Assert.That(malformedArtifactValues.Length,
                    Is.GreaterThanOrEqualTo(malformedArtifact.Length));
                for (var index = 0;
                    index < malformedArtifact.Length;
                    index++)
                {
                    malformedArtifactValues[index]
                        = malformedArtifact[index];
                }
                var malformedDecoderBuffers = decoderBuffers.Select(buffer =>
                    buffer.Name == "StandaloneArtifacts"
                        ? buffer with {
                            Values = [.. malformedArtifactValues],
                        }
                        : buffer).ToImmutableArray();
                decoderComponents[^1] = SyntaxTree.Parse("""
                    namespace Terrum.CodeAnalysis;
                    int32 Main() {
                        int32 status = DecodeStandaloneModuleMetadata();
                        if (status != 2
                            || Host.StandaloneDecoderStatus[0] != 2) {
                            return 81;
                        }
                        return 0;
                    }
                    """, "standalone-source-call-decoder-tests.terrum");
                var malformedDecoderCompilation
                    = Compilation.Create(decoderComponents,
                        malformedDecoderBuffers, policy);
                Assert.That(malformedDecoderCompilation.HasErrors, Is.False,
                    string.Join(Environment.NewLine,
                        malformedDecoderCompilation.Diagnostics));
                var malformedDecoderProgram
                    = malformedDecoderCompilation.Program
                        ?? throw new AssertionException(
                            "Expected the malformed Body decoder validator.");
                var malformedDecoderExecution
                    = NativeJit.ExecuteWithData(malformedDecoderProgram,
                        (NativeRuntimePolicy)policy);
                Assert.That(malformedDecoderExecution.ExitCode,
                    Is.EqualTo(0));
            }
        }

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
                    || Host.StandaloneWriterPredicateGraphMap[0] != 0
                    || Host.StandaloneWriterPredicateGraphMap[1] != 1
                    || Host.StandaloneWriterPredicateGraphMap[2] != 2
                    || Host.StandaloneWriterPredicateGraphMap[3] != 2
                    || Host.StandaloneWriterPredicateGraphMap[4] != 1
                    || Host.StandaloneWriterPredicateGraphMap[5] != 1
                    || Host.StandaloneWriterPredicateGraphMap[6] != 3
                    || Host.StandaloneWriterPredicateGraphMap[7] != 0
                    || Host.StandaloneWriterPredicateGraphMap[8] != 5
                    || Host.StandaloneWriterPredicateGraphMap[9] != 0
                    || Host.StandaloneWriterPredicateGraphMap[10] != 1
                    || Host.StandaloneWriterPredicateGraphMap[11] != 1
                    || Host.StandaloneWriterPredicateGraphMap[12] != 3
                    || Host.StandaloneWriterPredicateGraphMap[13] != 1
                    || Host.StandaloneWriterPredicateGraphMap[14] != 2
                    || Host.StandaloneWriterPredicateGraphMap[15] != 1
                    || Host.StandaloneWriterPredicateGraphMap[16] != 3
                    || Host.StandaloneWriterPredicateGraphMap[17] != 0
                    || Host.StandaloneWriterPredicateGraphMap[18] != 5
                    || Host.StandaloneWriterPredicateGraphMap[19] != 0
                    || Host.StandaloneWriterMethodContractCounts[0] != 0
                    || Host.StandaloneWriterMethodContractCounts[1] != 3
                    || Host.StandaloneWriterMethodContractCounts[2] != 3) {
                    return 51;
                }
                init int32 copiedGraphRow;
                if (EnsureStandaloneWriterPredicateGraph(0, 1, 3,
                        3, 0, 2, 1, 5, 0,
                        ref init copiedGraphRow) != 0
                    || copiedGraphRow != 3
                    || FindStandaloneWriterPredicateGraph(0, 1, 2, 2) != 1
                    || FindStandaloneWriterPredicateGraph(0, 1, 3, 2) != 3) {
                    return 52;
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
                .CreateSourceCallHostBuffers(contractSource,
                    SourceArithmeticProfile.Development));
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

        var assertionSource = SourceText.From(
            "using api; bool Main() { return Check(true); }");
        var assertionInput = new StandaloneMethodImportInput(Agreement,
            "consumer/assertion", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreatePhaseOnePredicateArtifact("provider/assertion")),
            ], [
                new StandaloneMethodRoot(0, "provider/assertion",
                    "api/Check"),
            ]);
        const string assertionVerificationSource = """
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
                if (Host.StandaloneWriterPredicateGraphMapCount[0] != 1
                    || Host.StandaloneWriterPredicateGraphMap[0] != 0
                    || Host.StandaloneWriterPredicateGraphMap[1] != 1
                    || Host.StandaloneWriterPredicateGraphMap[2] != 2
                    || Host.StandaloneWriterPredicateGraphMap[3] != 1
                    || Host.StandaloneWriterPredicateGraphMap[4] != 1
                    || Host.StandaloneWriterPredicateGraphMap[5] != 0x11
                    || Host.StandaloneWriterPredicateGraphMap[6] != 1
                    || Host.StandaloneWriterPredicateGraphMap[7] != 0
                    || Host.StandaloneWriterPredicateGraphMap[8] != 2
                    || Host.StandaloneWriterPredicateGraphMap[9] != 0
                    || Host.StandaloneWriterSections[17] != 2) {
                    return 51;
                }
                return 0;
            }
            """;
        var assertionComponents = new List<SyntaxTree>(
            components.GetRange(0, sourcePaths.Length));
        assertionComponents.Add(SyntaxTree.Parse(
            assertionVerificationSource,
            "standalone-owned-source-assertion-writer-tests.terrum"));
        var assertionBuffers = ScannerHost.CreateBuffers(assertionSource)
            .AddRange(assertionInput.Capture()
                .CreateSourceCallHostBuffers(assertionSource,
                    SourceArithmeticProfile.Development));
        var assertionCompilation = Compilation.Create(assertionComponents,
            assertionBuffers, policy);
        Assert.That(assertionCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                assertionCompilation.Diagnostics));
        var assertionProgram = assertionCompilation.Program
            ?? throw new AssertionException(
                "Expected a bound standalone assertion-writer validator.");
        var assertionExecution = NativeJit.ExecuteWithData(assertionProgram,
            (NativeRuntimePolicy)policy);
        Assert.That(assertionExecution.ExitCode, Is.EqualTo(0));
        var assertionArtifact = StandaloneMethodImportOutputFramer.Frame(
            assertionExecution, assertionBuffers);
        var assertionIndex
            = TrRmArtifactIndex
                .ForStandaloneGeneralPredicateGraphsWitnessProjectionsDraft(
                assertionArtifact);
        Assert.That(assertionIndex.TryGetSection(0x0004,
                out var assertionFeatures, out var assertionFeatureCount),
            Is.True);
        Assert.That(assertionFeatureCount, Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                assertionFeatures.Slice(0, sizeof(uint))),
            Is.EqualTo(10));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                assertionFeatures.Slice(sizeof(uint), sizeof(uint))),
            Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                assertionFeatures.Slice(2 * sizeof(uint), sizeof(uint))),
            Is.EqualTo(17));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                assertionFeatures.Slice(3 * sizeof(uint), sizeof(uint))),
            Is.EqualTo(1));

        var malformedTypeSpecSource = SourceText.From(
            "bool Main() { return false; }");
        var malformedTypeSpecInput = new StandaloneMethodImportInput(
            Agreement, "consumer/malformed-typespec", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreatePhaseOnePredicateArtifact("provider/malformed",
                        invalidTypeSpec: true)),
            ], [
                new StandaloneMethodRoot(0, "provider/malformed",
                    "api/Check"),
            ]);
        const string malformedTypeSpecVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 50; }
                int32 status = ValidateStandaloneMethodImportInput();
                if (status != 0) { return status; }
                if (TryValidateStandaloneArtifactDirectory(0)) {
                    return 51;
                }
                return 0;
            }
            """;
        var malformedTypeSpecComponents = new List<SyntaxTree>(
            components.GetRange(0, sourcePaths.Length));
        malformedTypeSpecComponents.Add(SyntaxTree.Parse(
            malformedTypeSpecVerificationSource,
            "standalone-malformed-typespec-preflight-tests.terrum"));
        var malformedTypeSpecBuffers
            = ScannerHost.CreateBuffers(malformedTypeSpecSource).AddRange(
                malformedTypeSpecInput.Capture()
                    .CreateSourceCallHostBuffers(
                        malformedTypeSpecSource,
                        SourceArithmeticProfile.Development));
        var malformedTypeSpecCompilation = Compilation.Create(
            malformedTypeSpecComponents, malformedTypeSpecBuffers, policy);
        Assert.That(malformedTypeSpecCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                malformedTypeSpecCompilation.Diagnostics));
        var malformedTypeSpecProgram = malformedTypeSpecCompilation.Program
            ?? throw new AssertionException(
                "Expected a bound standalone TypeSpec preflight validator.");
        var malformedTypeSpecExecution = NativeJit.ExecuteWithData(
            malformedTypeSpecProgram, (NativeRuntimePolicy)policy);
        Assert.That(malformedTypeSpecExecution.ExitCode, Is.EqualTo(0));

        var witnessProjectionInput = new StandaloneMethodImportInput(
            Agreement, "consumer/witness-projection", [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateWitnessProjectionArtifact("provider/witness")),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateWitnessProjectionArtifact(
                        "provider/witness-invalid",
                        invalidViewOrdinal: true)),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateWitnessProjectionArtifact(
                        "provider/witness-invalid-module-owner",
                        invalidModuleLevelOwner: true)),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateWitnessProjectionArtifact(
                        "provider/witness-generic-argument")),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateWitnessProjectionArtifact(
                        "provider/witness-invalid-generic-argument",
                        invalidConstructedTypeArgument: true)),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateWitnessProjectionArtifact(
                        "provider/witness-missing-member",
                        omitProjectionMember: true)),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateWitnessProjectionArtifact(
                        "provider/witness-duplicate-member",
                        duplicateProjectionMember: true)),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateWitnessProjectionArtifact(
                        "provider/witness-inherited",
                        includeInheritedInterface: true)),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateWitnessProjectionArtifact(
                        "provider/witness-inherited-missing-view",
                        includeInheritedInterface: true,
                        omitInheritedView: true)),
                new TrRmStandardArtifactInput(SchemaIdentity,
                    CreateWitnessProjectionArtifact(
                        "provider/witness-inherited-cycle",
                        includeInheritedInterface: true,
                        invalidInterfaceCycle: true)),
            ], []);
        var witnessProjectionSource = SourceText.From(
            "bool Main() { return false; }");
        const string witnessProjectionVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (Scan() < 1) { return 50; }
                int32 status = ValidateStandaloneMethodImportInput();
                if (status != 0) { return status; }
                if (!TryValidateStandaloneArtifactDirectory(0)) {
                    return 51;
                }
                if (TryValidateStandaloneArtifactDirectory(1)) {
                    return 52;
                }
                if (TryValidateStandaloneArtifactDirectory(2)) {
                    return 53;
                }
                if (!TryValidateStandaloneArtifactDirectory(3)) {
                    return 54;
                }
                if (TryValidateStandaloneArtifactDirectory(4)) {
                    return 55;
                }
                if (TryValidateStandaloneArtifactDirectory(5)) {
                    return 56;
                }
                if (TryValidateStandaloneArtifactDirectory(6)) {
                    return 57;
                }
                if (!TryValidateStandaloneArtifactDirectory(7)) {
                    return 58;
                }
                if (TryValidateStandaloneArtifactDirectory(8)) {
                    return 59;
                }
                if (TryValidateStandaloneArtifactDirectory(9)) {
                    return 60;
                }
                return 0;
            }
            """;
        var witnessProjectionComponents = new List<SyntaxTree>(
            components.GetRange(0, sourcePaths.Length));
        witnessProjectionComponents.Add(SyntaxTree.Parse(
            witnessProjectionVerificationSource,
            "standalone-witness-projection-preflight-tests.terrum"));
        var witnessProjectionBuffers
            = ScannerHost.CreateBuffers(witnessProjectionSource).AddRange(
                witnessProjectionInput.Capture()
                    .CreateSourceCallHostBuffers(
                        witnessProjectionSource,
                        SourceArithmeticProfile.Development));
        var witnessProjectionCompilation = Compilation.Create(
            witnessProjectionComponents, witnessProjectionBuffers, policy);
        Assert.That(witnessProjectionCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                witnessProjectionCompilation.Diagnostics));
        var witnessProjectionProgram = witnessProjectionCompilation.Program
            ?? throw new AssertionException(
                "Expected a bound standalone witness-projection validator.");
        var witnessProjectionExecution = NativeJit.ExecuteWithData(
            witnessProjectionProgram, (NativeRuntimePolicy)policy);
        Assert.That(witnessProjectionExecution.ExitCode, Is.EqualTo(0));

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
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneTypeSpecValidation.terrum",
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

        var assertionDecoderInput
            = new StandaloneMethodImportArtifactInput(Agreement,
                "consumer/assertion-reload",
                new TrRmStandardArtifactInput(SchemaIdentity,
                    assertionArtifact),
                assertionInput.Artifacts);
        var assertionDecoderBuffers = assertionDecoderInput.Capture()
            .CreateSourceFreeReaderHostBuffers();
        const string assertionDecoderVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                int32 status = DecodeStandaloneModuleMetadata();
                if (status != 0) { return status; }
                if (Host.StandaloneDecoderStatus[0] != 0
                    || Host.StandaloneDecoderSelectedModuleId[0] != 0
                    || Host.StandaloneDecoderMethodCount[0] != 2
                    || Host.StandaloneDecoderBodyCount[0] != 1
                    || Host.StandaloneDecoderLocalCallableCount[0] != 1
                    || Host.StandaloneDecoderImportedCallableCount[0] != 1
                    || Host.StandaloneDecoderImportMapCount[0] != 1) {
                    return 71;
                }
                return 0;
            }
            """;
        var assertionDecoderComponents = new List<SyntaxTree>(
            decoderComponents.GetRange(0, decoderSourcePaths.Length));
        assertionDecoderComponents.Add(SyntaxTree.Parse(
            assertionDecoderVerificationSource,
            "standalone-source-free-phase-one-decoder-tests.terrum"));
        var assertionDecoderCompilation = Compilation.Create(
            assertionDecoderComponents, assertionDecoderBuffers, policy);
        Assert.That(assertionDecoderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                assertionDecoderCompilation.Diagnostics));
        var assertionDecoderProgram = assertionDecoderCompilation.Program
            ?? throw new AssertionException(
                "Expected a bound standalone phase-one decoder validator.");
        var assertionDecoderExecution = NativeJit.ExecuteWithData(
            assertionDecoderProgram, (NativeRuntimePolicy)policy);
        Assert.That(assertionDecoderExecution.ExitCode, Is.EqualTo(0));

        var executableProviderArtifact = CreateMethodProviderArtifact(
            "provider/leaf", "api.Imported", scalarParameter: true,
            executableBody: true);
        var targetPlanInput = new StandaloneMethodImportArtifactInput(
            Agreement, "consumer/module",
            new TrRmStandardArtifactInput(SchemaIdentity, artifact), [
                new TrRmStandardArtifactInput(SchemaIdentity,
                    executableProviderArtifact),
            ]);
        var targetPlanBuffers = targetPlanInput.Capture()
            .CreateSourceFreeReaderHostBuffers();
        const string targetPlanVerificationSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                int32 status = DecodeStandaloneModuleMetadata();
                if (status != 0) { return status; }
                status = BuildStandaloneImportedTargetPlan();
                if (status != 0) { return status; }
                status = ValidateStandaloneImportedTargetBodies();
                if (status != 0) { return status; }
                if (Host.StandaloneDecoderTargetPlanBuilt[0] != 1
                    || Host.StandaloneDecoderTargetBodiesValidated[0] != 1
                    || Host.StandaloneDecoderCallCount[0] != 3) {
                    return 72;
                }
                for (mut int32 callId = 0;
                    callId < Host.StandaloneDecoderCallCount[0];
                    callId = callId + 1) {
                    int32 offset = callId * 22;
                    if (Host.StandaloneDecoderCalls[offset + 3] != 2
                        || Host.StandaloneDecoderCalls[offset + 14] != 1
                        || Host.StandaloneDecoderCalls[offset + 16] != 1
                        || Host.StandaloneDecoderCalls[offset + 17] != 1
                        || Host.StandaloneDecoderCalls[offset + 18] != 1
                        || Host.StandaloneDecoderCalls[offset + 19] != 1
                        || Host.StandaloneDecoderCalls[offset + 20] <= 0
                        || Host.StandaloneDecoderCalls[offset + 21] != 48) {
                        return 73;
                    }
                }
                return 0;
            }
            """;
        var targetPlanComponents = new List<SyntaxTree>(
            decoderComponents);
        targetPlanComponents[^1] = SyntaxTree.Parse(
            targetPlanVerificationSource,
            "standalone-import-target-plan-tests.terrum");
        var targetPlanCompilation = Compilation.Create(
            targetPlanComponents, targetPlanBuffers, policy);
        Assert.That(targetPlanCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                targetPlanCompilation.Diagnostics));
        var targetPlanProgram = targetPlanCompilation.Program
            ?? throw new AssertionException(
                "Expected an imported-target plan validator.");
        var targetPlanExecution = NativeJit.ExecuteWithData(
            targetPlanProgram, (NativeRuntimePolicy)policy);
        Assert.That(targetPlanExecution.ExitCode, Is.EqualTo(0));

        var missingBodyTargetInput
            = new StandaloneMethodImportArtifactInput(Agreement,
                "consumer/module",
                new TrRmStandardArtifactInput(SchemaIdentity, artifact),
                input.Artifacts);
        var missingBodyTargetBuffers = missingBodyTargetInput.Capture()
            .CreateSourceFreeReaderHostBuffers();
        var missingBodyTargetCompilation = Compilation.Create(
            targetPlanComponents, missingBodyTargetBuffers, policy);
        Assert.That(missingBodyTargetCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                missingBodyTargetCompilation.Diagnostics));
        var missingBodyTargetProgram = missingBodyTargetCompilation.Program
            ?? throw new AssertionException(
                "Expected a missing-provider-body validator.");
        var missingBodyTargetExecution = NativeJit.ExecuteWithData(
            missingBodyTargetProgram, (NativeRuntimePolicy)policy);
        Assert.That(missingBodyTargetExecution.ExitCode, Is.EqualTo(2));

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

        var malformedCallArtifact = artifact.ToArray();
        var constantCountOffset = bodyCursor;
        var constantMapCount = checked((int)BinaryPrimitives
            .ReadUInt32LittleEndian(
                malformedCallArtifact.AsSpan(constantCountOffset)));
        bodyCursor += 4;
        var blockCount = checked((int)BinaryPrimitives
            .ReadUInt32LittleEndian(malformedCallArtifact.AsSpan(bodyCursor)));
        var recordCount = checked((int)BinaryPrimitives
            .ReadUInt32LittleEndian(
                malformedCallArtifact.AsSpan(bodyCursor + 4)));
        var mapLength = checked((homeMapCount * 8)
            + (typeMapCount * 4) + (methodMapCount * 4)
            + (fieldMapCount * 4) + (constantMapCount * 8));
        var recordIndexStart = checked(bodyCursor + 12 + mapLength
            + (blockCount * 12));
        var recordDataStart = checked(recordIndexStart
            + ((recordCount + 1) * 4));
        var firstCallRecordOffset = checked((int)BinaryPrimitives
            .ReadUInt32LittleEndian(
                malformedCallArtifact.AsSpan(recordIndexStart + 4)));
        var firstCallRecordStart = checked(recordDataStart
            + firstCallRecordOffset);
        Assert.That(malformedCallArtifact[firstCallRecordStart],
            Is.EqualTo(2));
        var firstCallInputStart = firstCallRecordStart + 20;
        malformedCallArtifact[firstCallInputStart] = 1;
        malformedCallArtifact[firstCallInputStart + 1] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(
            malformedCallArtifact.AsSpan(firstCallInputStart + 2), 13);
        var malformedCallInput = new StandaloneMethodImportArtifactInput(
            Agreement, "consumer/module",
            new TrRmStandardArtifactInput(SchemaIdentity,
                malformedCallArtifact), input.Artifacts);
        var malformedCallBuffers = malformedCallInput.Capture()
            .CreateSourceFreeReaderHostBuffers();
        var malformedCallCompilation = Compilation.Create(
            constantDecoderComponents, malformedCallBuffers, policy);
        Assert.That(malformedCallCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                malformedCallCompilation.Diagnostics));
        var malformedCallProgram = malformedCallCompilation.Program
            ?? throw new AssertionException(
                "Expected a register-provenance decoder validator.");
        var malformedCallExecution = NativeJit.ExecuteWithData(
            malformedCallProgram, (NativeRuntimePolicy)policy);
        Assert.That(malformedCallExecution.ExitCode, Is.EqualTo(3));

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
            "using api; int32 Main() { _ = AliasA(); return AliasB(); }");
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
            aliasInput.Capture().CreateSourceCallHostBuffers(aliasSource,
                SourceArithmeticProfile.Development));
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
            remapInput.Capture().CreateSourceCallHostBuffers(remapSource,
                SourceArithmeticProfile.Development));
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
            + "Unit(unitValue); Init(ref init output); "
            + "_ = Unsafe(value); } }");
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
            callVariantInput.Capture()            .CreateSourceCallHostBuffers(callVariantSource,
                SourceArithmeticProfile.Development));
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
            callVariantInput.Capture()            .CreateSourceCallHostBuffers(unauthorizedUnsafeSource,
                SourceArithmeticProfile.Development));
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
            + "+Imported(value); return Imported(7); }");
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
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/HexDigitValue.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/BasedIntegerFloatLiteral.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/HexFloatLiteral.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/DecimalFloatLiteral.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneMethodImportInput.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneArtifactDirectory.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneStringTable.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneModuleDirectory.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneCallableTypes.terrum",
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneTypeSpecValidation.terrum",
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
                    || Host.StandaloneSourceExpressionCount[0] != 5
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
                    || Host.StandaloneSourceCallArguments[3] != 3
                    || Host.StandaloneSourceCallArguments[4] != 0
                    || Host.StandaloneSourceCallArguments[5] != 1
                    || Host.StandaloneSourceExpressions[0] != 1
                    || Host.StandaloneSourceExpressions[8] != 3
                    || Host.StandaloneSourceExpressions[16] != 6
                    || Host.StandaloneSourceExpressions[24] != 2
                    || Host.StandaloneSourceExpressions[32] != 3
                    || Host.StandaloneSourceStatements[0] != 2
                    || Host.StandaloneSourceStatements[1] != 2
                    || Host.StandaloneSourceStatements[3] != 1
                    || Host.StandaloneSourceStatements[4] != 4
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
            captured.CreateSourceCallHostBuffers(
                source, SourceArithmeticProfile.Development));
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
            "sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneTypeSpecValidation.terrum",
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
        Assert.That(bodyReader.Code.IndexOf((byte)0x14),
            Is.GreaterThanOrEqualTo(0));
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
        byte[]? typeSpecSection = null,
        bool executableBody = false)
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
        if (executableBody)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodDefSection.AsSpan(24), 1);
        }
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
        if (executableBody)
        {
            var body = new byte[48];
            body[0] = 1;
            body[1] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(
                body.AsSpan(5), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                body.AsSpan(9), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                body.AsSpan(13), 3);
            BinaryPrimitives.WriteUInt32LittleEndian(
                body.AsSpan(17), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(
                body.AsSpan(21), 3);
            BinaryPrimitives.WriteUInt16LittleEndian(
                body.AsSpan(25), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(
                body.AsSpan(27), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(
                body.AsSpan(29), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(
                body.AsSpan(33), 8);
            body[37] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(
                body.AsSpan(41), 0);
            body[45] = 0x10;
            body[46] = 0xE0;
            body[47] = 0x00;
            sections.Add(new TrRmSection(0x0020, 0, 1,
                TrRmVariableTableWriter.EncodeBlobs([body])));
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

    private static byte[] CreatePhaseOnePredicateArtifact(
        string moduleIdentity, bool invalidTypeSpec = false)
    {
        var typeSpecRows = new ReadOnlyMemory<byte>[] {
            TrRmTypeSpecTableWriter.InitializationReference(
                0xF200_0008, isWritable: true),
        };
        var typeSpecSection = TrRmTypeSpecTableWriter.Encode(
            typeSpecRows, 0, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecSection,
            1, 0, 0, 0);
        var outputTypeSpecSection = typeSpecSection;
        if (invalidTypeSpec)
        {
            outputTypeSpecSection = new byte[14];
            BinaryPrimitives.WriteUInt32LittleEndian(outputTypeSpecSection, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(
                outputTypeSpecSection.AsSpan(4), 6);
            outputTypeSpecSection[8] = 2;
            BinaryPrimitives.WriteUInt32LittleEndian(
                outputTypeSpecSection.AsSpan(10), 1);
        }
        var methodSigSection = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0001, 0, 1, 1, 0, 0),
        ], typeSpecs, 1, 0);
        var signatures = new TrRmMethodSigTableReader(
            methodSigSection, 1, typeSpecs, 1, 0);
        var paramSection = TrRmParamTableWriter.Encode([
            new TrRmParamRow(1, 0, 0xF200_0001, 0),
        ], signatures, typeSpecs);
        var stringSection = TrRmStringTableWriter.EncodeNames([
            (moduleIdentity, 0),
            ("Implementation", 0),
            ("api/Check", 0),
        ]);
        var strings = new TrRmStringTableReader(stringSection, 3);
        var moduleSection = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1, 1, 2),
        ], strings, 0, 0, 1, 0, 2);

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
            methodDefSection.AsSpan(20), 1);

        var predicateAttachment = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(predicateAttachment, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(
            predicateAttachment.AsSpan(4), 0x000B);
        predicateAttachment[8] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(
            predicateAttachment.AsSpan(16), 1);
        var contractSection
            = TrRmVariableTableWriter.EncodeBlobs([predicateAttachment]);

        var predicateGraphSection = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(predicateGraphSection, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            predicateGraphSection.AsSpan(4), 1);
        var readFootprintSection = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(readFootprintSection, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            readFootprintSection.AsSpan(4), 1);
        var readRootSection = new byte[8];
        readRootSection[0] = 2;
        var predicateNodeSection = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(
            predicateNodeSection, 0x11);
        BinaryPrimitives.WriteUInt32LittleEndian(
            predicateNodeSection.AsSpan(4), 0xF200_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(
            predicateNodeSection.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            predicateNodeSection.AsSpan(12), 1);
        var predicateOperandSection = new byte[8];
        predicateOperandSection[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(
            predicateOperandSection.AsSpan(4), 1);

        var featureSection = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(featureSection, 10);
        BinaryPrimitives.WriteUInt32LittleEndian(
            featureSection.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            featureSection.AsSpan(8), 17);
        BinaryPrimitives.WriteUInt32LittleEndian(
            featureSection.AsSpan(12), 1);
        var exportSection = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(
            exportSection, 0x1600_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(
            exportSection.AsSpan(4), 3);
        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleSection),
            new TrRmSection(0x0002, 0, 3, stringSection),
            new TrRmSection(0x0003, 0, 1, outputTypeSpecSection),
            new TrRmSection(0x0004, 0, 2, featureSection),
            new TrRmSection(0x0016, 0, 1, methodDefSection),
            new TrRmSection(0x0017, 0, 1, methodSigSection),
            new TrRmSection(0x0018, 0, 1, paramSection),
            new TrRmSection(0x001B, 0, 1, contractSection),
            new TrRmSection(0x001D, 0, 1, exportSection),
            new TrRmSection(0x0037, 0, 1, predicateGraphSection),
            new TrRmSection(0x0038, 0, 1, readFootprintSection),
            new TrRmSection(0x0039, 0, 1, readRootSection),
            new TrRmSection(0x003B, 0, 1, predicateNodeSection),
            new TrRmSection(0x003C, 0, 1, predicateOperandSection),
        ], TrRmArtifactSchema.GeneralPredicateGraphsDraft,
            standardSchemaIdentity:
                StandaloneMethodImportOutputFramer
                    .GeneralPredicateGraphsWitnessProjectionsSchemaIdentity);
    }

    private static byte[] CreateWitnessProjectionArtifact(
        string moduleIdentity, bool invalidViewOrdinal = false,
        bool invalidModuleLevelOwner = false,
        bool invalidConstructedTypeArgument = false,
        bool omitProjectionMember = false,
        bool duplicateProjectionMember = false,
        bool includeInheritedInterface = false,
        bool omitInheritedView = false,
        bool invalidInterfaceCycle = false)
    {
        if (includeInheritedInterface)
        {
            return CreateInheritedWitnessProjectionArtifact(
                moduleIdentity, omitInheritedView, invalidInterfaceCycle);
        }

        var stringSection = TrRmStringTableWriter.EncodeNames([
            ("", 0),
            (moduleIdentity, 0),
            ("Implementer", 0),
            ("IExample", 0),
            ("Requirement", 0),
            ("Implementation", 0),
            ("Generic", 0),
            ("T", 0),
            ("Requirement2", 0),
            ("Implementation2", 0),
        ]);
        var strings = new TrRmStringTableReader(stringSection, 10);
        var moduleSection = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 0, 1, 1, 0, 1, 2),
        ], strings, 0, 3, 4, 0, 2);
        var modules = new TrRmModuleTableReader(moduleSection, 1, strings,
            0, 3, 4, 0, 2);
        var featureSection = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(featureSection, 10);
        BinaryPrimitives.WriteUInt32LittleEndian(
            featureSection.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            featureSection.AsSpan(8), 17);
        BinaryPrimitives.WriteUInt32LittleEndian(
            featureSection.AsSpan(12), 1);

        var typeSpecRows = new ReadOnlyMemory<byte>[] {
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Reference(0x02, 2),
            TrRmTypeSpecTableWriter.Reference(0x02, 3),
            TrRmTypeSpecTableWriter.Constructed(3, [
                invalidConstructedTypeArgument ? 0x00FF_FFFFu : 1u,
            ]),
        };
        var typeSpecSection = TrRmTypeSpecTableWriter.Encode(
            typeSpecRows, 3, 0, 1);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecSection,
            4, 3, 0, 1);
        var typeDefRows = new[] {
            new TrRmTypeDefRow(1, 3, 6, 0, 0, 0, 0, 1, 1, 0, 0),
            new TrRmTypeDefRow(1, 4, 14, 0, 0, 0, 0, 2, 1, 0, 0),
            new TrRmTypeDefRow(1, 7, 6, 0, 0, 1, 1, 3, 1, 0, 0),
        };
        var typeDefSection = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, 0, 1, 3, 0);
        var typeDefs = new TrRmTypeDefTableReader(typeDefSection, 3,
            strings, modules, 0, 1, 3, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutSection = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(3, 0, 0, 0, 0, 0, 0, 0),
        ], typeDefs, targetProfiles, 0);
        var methodSigSection = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0001, 2, 0, 0, 0, 0),
            new TrRmMethodSigRow(0xF200_0001, 1, 0, 0, 0, 0),
            new TrRmMethodSigRow(0xF200_0001, 2, 0, 0, 0, 0),
            new TrRmMethodSigRow(0xF200_0001, 1, 0, 0, 0, 0),
        ], typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(
            methodSigSection, 4, typeSpecs, 0, 0);
        var methodDefSection = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(2, 5, 1, 2, 0, 0, 0),
            new TrRmMethodDefRow(1, 6, 2, 2, 0, 0, 0),
            new TrRmMethodDefRow(2, 9, 3, 2, 0, 0, 0),
            new TrRmMethodDefRow(1, 10, 4, 2, 0, 0, 0),
        ], strings, modules, typeDefs, signatures, 0, 0);
        var methodDefs = new TrRmMethodDefTableReader(methodDefSection, 4,
            strings, modules, typeDefs, signatures, 0, 0);
        var genericParamSection = TrRmGenericParamTableWriter.Encode([
            new TrRmGenericParamRow(0x1300_0003, 0, 8, 0, 0,
                1, 1, 1, 0, 0),
        ], strings, typeDefs, methodDefs, signatures, typeSpecs, 0);
        var genericParams = new TrRmGenericParamTableReader(
            genericParamSection, 1, strings, typeDefs, methodDefs,
            signatures, typeSpecs, 0);
        var interfaceDefSection = TrRmInterfaceDefTableWriter.Encode([
            new TrRmInterfaceDefRow(2, 0, 0, 0, 1, 2,
                0, 0, 0, 0, 0, 0),
        ], typeDefs, genericParams, 0, 2, 0, 0, 0);
        var interfaceDefs = new TrRmInterfaceDefTableReader(
            interfaceDefSection, 1, typeDefs, genericParams, 0, 2,
            0, 0, 0);
        var interfaceMethodSection = TrRmInterfaceMethodTableWriter.Encode([
            new TrRmInterfaceMethodRow(1, 0, 1),
            new TrRmInterfaceMethodRow(1, 1, 3),
        ], interfaceDefs, methodDefs, signatures);
        var witnessSection = TrRmInterfaceWitnessTableWriter.Encode([
            new TrRmInterfaceWitnessRow(1, 2, 1, 2, 0, 0, 0, 0),
        ], typeSpecs, 2, 0, 0);
        var witnesses = new TrRmInterfaceWitnessTableReader(
            witnessSection, 1, typeSpecs, 2, 0, 0);
        var witnessMemberSection = TrRmWitnessMemberTableWriter.Encode([
            new TrRmWitnessMemberRow(1, 2, 1, 1, 2),
            new TrRmWitnessMemberRow(1, 2, 3, 1, 4),
        ], witnesses, typeSpecs, methodDefs);
        if (invalidModuleLevelOwner)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                witnessMemberSection.AsSpan(12), 0);
        }
        var projectionRow = CreateStandaloneWitnessProjectionRow(
            omitProjectionMember, duplicateProjectionMember);
        if (invalidViewOrdinal)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                projectionRow.AsSpan(40), 1);
        }
        if (invalidModuleLevelOwner)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                projectionRow.AsSpan(44), 0);
        }
        var projectionSection = TrRmVariableTableWriter.EncodeBlobs([
            new ReadOnlyMemory<byte>(projectionRow),
        ]);
        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleSection),
            new TrRmSection(0x0002, 0, 10, stringSection),
            new TrRmSection(0x0003, 0, 4, typeSpecSection),
            new TrRmSection(0x0004, 0, 2, featureSection),
            new TrRmSection(0x0013, 0, 3, typeDefSection),
            new TrRmSection(0x0015, 0, 3, layoutSection),
            new TrRmSection(0x0016, 0, 4, methodDefSection),
            new TrRmSection(0x0017, 0, 4, methodSigSection),
            new TrRmSection(0x0019, 0, 1, genericParamSection),
            new TrRmSection(0x0024, 0, 2, interfaceMethodSection),
            new TrRmSection(0x0025, 0, 1, interfaceDefSection),
            new TrRmSection(0x0028, 0, 1, witnessSection),
            new TrRmSection(0x0029, 0, 2, witnessMemberSection),
            new TrRmSection(0x0068, 0, 1, projectionSection),
        ], TrRmArtifactSchema.GeneralPredicateGraphsDraft,
            standardSchemaIdentity:
                StandaloneMethodImportOutputFramer
                    .GeneralPredicateGraphsWitnessProjectionsSchemaIdentity);
    }

    private static byte[] CreateInheritedWitnessProjectionArtifact(
        string moduleIdentity, bool omitInheritedView,
        bool invalidInterfaceCycle)
    {
        var stringSection = TrRmStringTableWriter.EncodeNames([
            ("", 0),
            (moduleIdentity, 0),
            ("Implementer", 0),
            ("IDerived", 0),
            ("Requirement", 0),
            ("Implementation", 0),
            ("Requirement2", 0),
            ("Implementation2", 0),
            ("IBase", 0),
            ("BaseRequirement", 0),
            ("BaseImplementation", 0),
        ]);
        var strings = new TrRmStringTableReader(stringSection, 11);
        var moduleSection = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 0, 1, 1, 0, 1, 2),
        ], strings, 0, 3, 6, 0, 2);
        var modules = new TrRmModuleTableReader(moduleSection, 1, strings,
            0, 3, 6, 0, 2);
        var featureSection = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(featureSection, 10);
        BinaryPrimitives.WriteUInt32LittleEndian(
            featureSection.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            featureSection.AsSpan(8), 17);
        BinaryPrimitives.WriteUInt32LittleEndian(
            featureSection.AsSpan(12), 1);
        var typeSpecRows = new ReadOnlyMemory<byte>[] {
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            TrRmTypeSpecTableWriter.Reference(0x02, 2),
            TrRmTypeSpecTableWriter.Reference(0x02, 3),
        };
        var typeSpecSection = TrRmTypeSpecTableWriter.Encode(
            typeSpecRows, 3, 0, 0);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecSection,
            3, 3, 0, 0);
        var typeDefRows = new[] {
            new TrRmTypeDefRow(1, 3, 6, 0, 0, 0, 0, 1, 1, 0, 0),
            new TrRmTypeDefRow(1, 4, 14, 0, 0, 0, 0, 2, 1, 0, 0),
            new TrRmTypeDefRow(1, 9, 14, 0, 0, 0, 0, 3, 1, 0, 0),
        };
        var typeDefSection = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, 0, 0, 3, 0);
        var typeDefs = new TrRmTypeDefTableReader(typeDefSection, 3,
            strings, modules, 0, 0, 3, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutSection = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(3, 0, 0, 0, 0, 0, 0, 0),
        ], typeDefs, targetProfiles, 0);
        var methodSigSection = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0001, 2, 0, 0, 0, 0),
            new TrRmMethodSigRow(0xF200_0001, 1, 0, 0, 0, 0),
            new TrRmMethodSigRow(0xF200_0001, 2, 0, 0, 0, 0),
            new TrRmMethodSigRow(0xF200_0001, 1, 0, 0, 0, 0),
            new TrRmMethodSigRow(0xF200_0001, 3, 0, 0, 0, 0),
            new TrRmMethodSigRow(0xF200_0001, 1, 0, 0, 0, 0),
        ], typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(
            methodSigSection, 6, typeSpecs, 0, 0);
        var methodDefSection = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(2, 5, 1, 2, 0, 0, 0),
            new TrRmMethodDefRow(1, 6, 2, 2, 0, 0, 0),
            new TrRmMethodDefRow(2, 7, 3, 2, 0, 0, 0),
            new TrRmMethodDefRow(1, 8, 4, 2, 0, 0, 0),
            new TrRmMethodDefRow(3, 10, 5, 2, 0, 0, 0),
            new TrRmMethodDefRow(1, 11, 6, 2, 0, 0, 0),
        ], strings, modules, typeDefs, signatures, 0, 0);
        var methods = new TrRmMethodDefTableReader(methodDefSection, 6,
            strings, modules, typeDefs, signatures, 0, 0);
        var genericParams = new TrRmGenericParamTableReader([], 0,
            strings, typeDefs, methods, signatures, typeSpecs, 0);
        var interfaceDefSection = TrRmInterfaceDefTableWriter.Encode([
            new TrRmInterfaceDefRow(2, 0, 1, 1, 1, 2,
                0, 0, 0, 0, 0, 0),
            new TrRmInterfaceDefRow(3, 0, 0, 0, 3, 1,
                0, 0, 0, 0, 0, 0),
        ], typeDefs, genericParams, 1, 3, 0, 0, 0);
        var interfaces = new TrRmInterfaceDefTableReader(
            interfaceDefSection, 2, typeDefs, genericParams, 1, 3,
            0, 0, 0);
        var interfaceBaseSection = TrRmInterfaceBaseTableWriter.Encode([
            new TrRmInterfaceBaseRow(1, 0,
                invalidInterfaceCycle ? 2u : 3u),
        ], typeSpecs, interfaces);
        var interfaceMethodSection = TrRmInterfaceMethodTableWriter.Encode([
            new TrRmInterfaceMethodRow(1, 0, 1),
            new TrRmInterfaceMethodRow(1, 1, 3),
            new TrRmInterfaceMethodRow(2, 0, 5),
        ], interfaces, methods, signatures);
        var witnessSection = TrRmInterfaceWitnessTableWriter.Encode([
            new TrRmInterfaceWitnessRow(1, 2, 1, 3, 0, 0, 0, 0),
        ], typeSpecs, 3, 0, 0);
        var witnesses = new TrRmInterfaceWitnessTableReader(
            witnessSection, 1, typeSpecs, 3, 0, 0);
        var witnessMemberSection = TrRmWitnessMemberTableWriter.Encode([
            new TrRmWitnessMemberRow(1, 2, 1, 1, 2),
            new TrRmWitnessMemberRow(1, 2, 3, 1, 4),
            new TrRmWitnessMemberRow(1, 3, 5, 1, 6),
        ], witnesses, typeSpecs, methods);
        var projectionRow = CreateInheritedStandaloneWitnessProjectionRow(
            omitInheritedView);
        var projectionSection = TrRmVariableTableWriter.EncodeBlobs([
            new ReadOnlyMemory<byte>(projectionRow),
        ]);
        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleSection),
            new TrRmSection(0x0002, 0, 11, stringSection),
            new TrRmSection(0x0003, 0, 3, typeSpecSection),
            new TrRmSection(0x0004, 0, 2, featureSection),
            new TrRmSection(0x0013, 0, 3, typeDefSection),
            new TrRmSection(0x0015, 0, 3, layoutSection),
            new TrRmSection(0x0016, 0, 6, methodDefSection),
            new TrRmSection(0x0017, 0, 6, methodSigSection),
            new TrRmSection(0x0023, 0, 1, interfaceBaseSection),
            new TrRmSection(0x0024, 0, 3, interfaceMethodSection),
            new TrRmSection(0x0025, 0, 2, interfaceDefSection),
            new TrRmSection(0x0028, 0, 1, witnessSection),
            new TrRmSection(0x0029, 0, 3, witnessMemberSection),
            new TrRmSection(0x0068, 0, 1, projectionSection),
        ], TrRmArtifactSchema.GeneralPredicateGraphsDraft,
            standardSchemaIdentity:
                StandaloneMethodImportOutputFramer
                    .GeneralPredicateGraphsWitnessProjectionsSchemaIdentity);
    }

    private static byte[] CreateStandaloneWitnessProjectionRow(
        bool omitProjectionMember, bool duplicateProjectionMember)
    {
        var projectionMemberCount = omitProjectionMember ? 0 : 2;
        var row = new byte[36 + (projectionMemberCount * 16)];
        BinaryPrimitives.WriteUInt32LittleEndian(row, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(24),
            (uint)projectionMemberCount);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(28), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(32), 2);
        if (projectionMemberCount > 0)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(36), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(44), 1);
            row[48] = 1;
        }
        if (projectionMemberCount > 1)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                row.AsSpan(52), duplicateProjectionMember ? 1u : 2u);
            BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(60), 1);
            row[64] = 1;
        }
        return row;
    }

    private static byte[] CreateInheritedStandaloneWitnessProjectionRow(
        bool omitInheritedView)
    {
        var viewCount = omitInheritedView ? 1 : 2;
        var memberCount = omitInheritedView ? 2 : 3;
        var row = new byte[28 + (viewCount * 8) + (memberCount * 16)];
        BinaryPrimitives.WriteUInt32LittleEndian(row, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(20), (uint)viewCount);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(24), (uint)memberCount);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(28), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(32), 2);
        if (viewCount == 2)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(36), 3);
            BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(40), 3);
        }

        var memberStart = 28 + (viewCount * 8);
        for (var member = 0; member < memberCount; member++)
        {
            var offset = memberStart + (member * 16);
            BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(offset),
                (uint)(member + 1));
            BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(offset + 4),
                member == 2 ? 1u : 0u);
            BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(offset + 8), 1);
            row[offset + 12] = 1;
        }
        return row;
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

    [Test]
    public void SourceCallHostBuffersPreserveArithmeticProfile()
    {
        var source = SourceText.From("int32 Main() { return 0; }");
        var input = new StandaloneMethodImportInput(
            Agreement, "consumer/module", [], []).Capture();
        var buffers = input.CreateSourceCallHostBuffers(source,
            SourceArithmeticProfile.Release);
        var arithmeticProfile = buffers.Single(buffer =>
            buffer.Name == "StandaloneSourceArithmeticProfile");

        Assert.That(arithmeticProfile.Values,
            Is.EqualTo(new[] { (int)SourceArithmeticProfile.Release }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            input.CreateSourceCallHostBuffers(source,
                (SourceArithmeticProfile)2));
    }
}
