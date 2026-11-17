// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

internal sealed partial class CliTests
{
    private const string EvaluationCoreCallable = """
        using Evaluation.Contracts;
        alias CheckedEvaluation = ConstantSource;
        [CheckedEvaluation]
        public int32 Add(int32 left, int32 right) { return left + right; }
        """;
    private const string EvaluationCoreEntry = """
        int32 Main() {
            const int32 result = Add(20, 22);
            return result;
        }
        """;
    private const string EvaluationCoreNativeEntry = """
        [Terrum.Interop.NativeImport("kernel32.dll", "GetCurrentThreadId",
            CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
        extern unsafe int32 GetCurrentThreadId();
        int32 Main() {
            const int32 result = Add(20, 22);
            unsafe {
                if (GetCurrentThreadId() == 0) { return 1; }
            }
            return result;
        }
        """;

    [TestCase("analyze", "ordinary")]
    [TestCase("analyze", "corelib")]
    [TestCase("build", "ordinary")]
    [TestCase("build-native-import", "ordinary")]
    [TestCase("build-library", "ordinary")]
    [TestCase("build-library", "corelib")]
    [TestCase("build-library", "source-grant")]
    [TestCase("jit", "ordinary")]
    public async Task EvaluationCoreManifestReachesSourceFactoriesAsync(
        string command, string profile)
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-evaluation-core-factories-");
        var manifest = Path.Combine(directory, "selected.json");
        var output = Path.Combine(directory,
            command == "build-library" ? "output.dll" : "output.exe");
        var abiOutput = Path.Combine(directory, "output.abi.json");
        try
        {
            await File.WriteAllBytesAsync(manifest,
                [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(
                    CreateEvaluationCoreManifest(pure: true))])
                .ConfigureAwait(true);
            var arguments = new List<string> {
                command, "-", "--evaluation-core-manifest", manifest,
                "--runtime", "fail-fast",
            };
            if (command == "build-native-import")
            {
                var nativeManifest = Path.Combine(directory, "native.json");
                await File.WriteAllTextAsync(nativeManifest,
                    CreateNativeCoreManifest()).ConfigureAwait(true);
                arguments.AddRange(["--native-core-manifest",
                    nativeManifest]);
            }
            if (command is "build" or "build-native-import" or "build-library")
            {
                arguments.AddRange(["-o", output]);
            }
            if (command == "build-library")
            {
                arguments.AddRange(["--export", "Add"]);
            }
            if (profile == "corelib")
            {
                arguments.Add("--corelib");
            }
            else if (profile == "source-grant")
            {
                arguments.AddRange(["--source-grant", "--abi-output",
                    abiOutput]);
            }
            arguments.AddRange(["--arithmetic-profile", "release"]);
            arguments.Add("--json");

            var callable = EvaluationCoreCallable.Replace(
                "[CheckedEvaluation]",
                "[PureSource] [CheckedEvaluation]",
                StringComparison.Ordinal);
            var (exit, result, error) = await RunAsync(
                Encoding.UTF8.GetBytes(
                    callable + "\n"
                        + (command == "build-native-import"
                            ? EvaluationCoreNativeEntry
                            : EvaluationCoreEntry)),
                [.. arguments]).ConfigureAwait(true);
            Assert.That(exit, Is.EqualTo(command == "jit" ? 42 : 0),
                result + error);
            Assert.That(error, Is.Empty);
            if (command is "build" or "build-native-import" or "build-library")
            {
                Assert.That(new FileInfo(output).Length, Is.GreaterThan(0));
                if (command != "build-library")
                {
                    Assert.That(await ExecuteNativeImageAsync(output)
                        .ConfigureAwait(true), Is.EqualTo(42));
                }
            }
            if (profile == "source-grant")
            {
                Assert.That(File.Exists(abiOutput), Is.True);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EvaluationCoreManifestAssociatesAllSourceTreesAsync(
        bool entryOnStdin)
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-evaluation-core-source-unit-");
        var manifest = Path.Combine(directory, "selected.json");
        var otherSource = Path.Combine(directory, "unrelated-name.terrum");
        try
        {
            await File.WriteAllTextAsync(manifest,
                CreateEvaluationCoreManifest()).ConfigureAwait(true);
            await File.WriteAllTextAsync(otherSource,
                entryOnStdin ? EvaluationCoreCallable : EvaluationCoreEntry)
                .ConfigureAwait(true);
            var (exit, result, error) = await RunAsync(
                Encoding.UTF8.GetBytes(entryOnStdin
                    ? EvaluationCoreEntry : EvaluationCoreCallable),
                "analyze", "-", "--source", otherSource, "--source-name",
                "not-the-logical-module.terrum", "--evaluation-core-manifest",
                manifest, "--arithmetic-profile", "release", "--json")
                .ConfigureAwait(true);
            Assert.That(exit, Is.Zero, result + error);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task EvaluationCoreManifestAcceptsIndependentRolesAndOrdinalTextAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-evaluation-core-ordinal-");
        var manifest = Path.Combine(directory, "selected.json");
        try
        {
            foreach (var pureOnly in new[] { false, true })
            {
                var root = JsonNode.Parse(CreateEvaluationCoreManifest(
                    pure: pureOnly, constEval: !pureOnly,
                    usingModuleIdentity: " ",
                    coreAuthorityIdentity: "\t",
                    exportKey: " \t "))!.AsObject();
                var catalog = root["localSchemaDeclarations"]!;
                catalog["origin"]!["revisionIdentity"] = " ";
                catalog["origin"]!["schema"]!["identity"] = "\t";
                catalog["exports"]!.AsArray().Add(new JsonObject {
                    ["exportKey"] = "ordinal-alias",
                    ["declarationIndex"] = 0,
                });
                root[pureOnly ? "pureSchema" : "constEvalSchema"]![
                    "exportKey"] = "ordinal-alias";
                await File.WriteAllTextAsync(manifest,
                    root.ToJsonString()).ConfigureAwait(true);
                var source = pureOnly
                    ? """
                      using Evaluation.Contracts;
                      [PureSource] int32 Identity(int32 value) {
                          return value;
                      }
                      int32 Main() {
                          return Identity(42) == 42 ? 0 : 1;
                      }
                      """
                    : EvaluationCoreCallable + "\n" + EvaluationCoreEntry;
                var (exit, result, error) = await RunAsync(
                    Encoding.UTF8.GetBytes(source), "analyze", "-",
                    "--evaluation-core-manifest", manifest,
                    "--arithmetic-profile", "release", "--json")
                    .ConfigureAwait(true);
                Assert.That(exit, Is.Zero, result + error);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [TestCase("role/key", "Role/key")]
    [TestCase(" role/key ", "role/key")]
    [TestCase("\u00E9", "e\u0301")]
    public async Task EvaluationCoreManifestDoesNotNormalizeExportKeysAsync(
        string exportedKey, string selectedKey)
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-evaluation-core-distinct-key-");
        var manifest = Path.Combine(directory, "selected.json");
        try
        {
            var root = JsonNode.Parse(CreateEvaluationCoreManifest(
                exportKey: exportedKey))!.AsObject();
            root["constEvalSchema"]!["exportKey"] = selectedKey;
            await File.WriteAllTextAsync(manifest,
                root.ToJsonString()).ConfigureAwait(true);
            var (exit, result, error) = await RunAsync([0xFF],
                "analyze", "-", "--evaluation-core-manifest", manifest,
                "--json").ConfigureAwait(true);
            Assert.That(exit, Is.EqualTo(2), result + error);
            AssertInputDiagnosticCode(result, "TRM2001");
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task EvaluationCoreManifestOmissionAndDecoysGrantNoRoleAsync()
    {
        var source = Encoding.UTF8.GetBytes(
            EvaluationCoreCallable + "\n" + EvaluationCoreEntry);
        var (plainExit, plainOutput, plainError) = await RunAsync(
            Encoding.UTF8.GetBytes("int32 Main() { return 0; }"),
            "analyze", "-", "--json").ConfigureAwait(true);
        Assert.That(plainExit, Is.Zero, plainOutput + plainError);
        foreach (var corelib in new[] { false, true })
        {
            string[] arguments = corelib
                ? ["analyze", "-", "--corelib", "--json"]
                : ["analyze", "-", "--json"];
            var (exit, result, error) = await RunAsync(source, arguments)
                .ConfigureAwait(true);
            Assert.That(exit, Is.EqualTo(1), result + error);
        }

        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-evaluation-core-decoy-");
        var manifest = Path.Combine(directory, "selected.json");
        try
        {
            await File.WriteAllTextAsync(manifest,
                CreateEvaluationCoreManifest()).ConfigureAwait(true);
            foreach (var decoy in new[] {
                "[ConstEval] int32 Add() { return 1; }",
                """
                using Evaluation.Contracts;
                alias ConstantSource = int32;
                [ConstantSource] int32 Add() { return 1; }
                """,
            })
            {
                var (exit, result, error) = await RunAsync(
                    Encoding.UTF8.GetBytes(decoy), "analyze", "-",
                    "--corelib", "--evaluation-core-manifest", manifest,
                    "--json").ConfigureAwait(true);
                Assert.That(exit, Is.EqualTo(1), result + error);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [TestCaseSource(nameof(InvalidEvaluationCoreManifests))]
    public async Task EvaluationCoreManifestRejectsInvalidInputAsync(byte[] contents)
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-evaluation-core-invalid-");
        var manifest = Path.Combine(directory, "invalid.json");
        var output = Path.Combine(directory, "previous.exe");
        byte[] previous = [0x4D, 0x5A, 0x42];
        try
        {
            await File.WriteAllBytesAsync(manifest, contents)
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(output, previous)
                .ConfigureAwait(true);
            var (exit, result, error) = await RunAsync([0xFF],
                "build", "-", "-o", output, "--runtime", "fail-fast",
                "--evaluation-core-manifest", manifest, "--json")
                .ConfigureAwait(true);
            Assert.That(exit, Is.EqualTo(2), result + error);
            AssertInputDiagnosticCode(result, "TRM2001");
            Assert.That(result, Does.Contain("invalid.json"));
            Assert.That(await File.ReadAllBytesAsync(output)
                .ConfigureAwait(true), Is.EqualTo(previous));
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EvaluationCoreManifestComposesWithOtherFamiliesAsync(
        bool evaluationFirst)
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-evaluation-core-composition-");
        var evaluation = Path.Combine(directory, "evaluation.json");
        var layout = Path.Combine(directory, "layout.json");
        var native = Path.Combine(directory, "native.json");
        var nativeOutput = Path.Combine(directory, "native-output.json");
        try
        {
            await File.WriteAllTextAsync(evaluation,
                CreateEvaluationCoreManifest()).ConfigureAwait(true);
            await File.WriteAllTextAsync(layout, CreateLayoutCoreManifest())
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(native, CreateNativeCoreManifest())
                .ConfigureAwait(true);
            await File.WriteAllTextAsync(nativeOutput,
                CreateNativeOutputCoreManifest()).ConfigureAwait(true);
            var arguments = new List<string> { "analyze", "-" };
            if (evaluationFirst)
            {
                arguments.AddRange(["--evaluation-core-manifest", evaluation]);
            }
            arguments.AddRange(["--layout-core-manifest", layout,
                "--native-core-manifest", native,
                "--native-output-core-manifest", nativeOutput]);
            if (!evaluationFirst)
            {
                arguments.AddRange(["--evaluation-core-manifest", evaluation]);
            }
            arguments.AddRange(["--arithmetic-profile", "release"]);
            arguments.Add("--json");

            var source = Encoding.UTF8.GetBytes(
                EvaluationCoreCallable + "\n" + EvaluationCoreEntry);
            var (exit, result, error) = await RunAsync(source,
                [.. arguments]).ConfigureAwait(true);
            Assert.That(exit, Is.Zero, result + error);

            foreach (var configuration in new[] {
                CreateEvaluationCoreManifest(
                    usingModuleIdentity: "other.consumer"),
                CreateEvaluationCoreManifest(
                    coreAuthorityIdentity: "other.authority"),
            })
            {
                await File.WriteAllTextAsync(evaluation, configuration)
                    .ConfigureAwait(true);
                var (conflictExit, conflictResult, conflictError)
                    = await RunAsync([0xFF], [.. arguments])
                        .ConfigureAwait(true);
                Assert.That(conflictExit, Is.EqualTo(2),
                    conflictResult + conflictError);
                AssertInputDiagnosticCode(conflictResult, "TRM2001");
            }

            var external = JsonNode.Parse(
                CreateEvaluationCoreManifest())!.AsObject();
            external["constEvalSchema"]!["providerIdentity"]
                = "sample.native-core";
            external["localSchemaDeclarations"] = null;
            await File.WriteAllTextAsync(evaluation,
                external.ToJsonString()).ConfigureAwait(true);
            var (externalExit, externalResult, externalError) = await RunAsync(
                [0xFF], [.. arguments]).ConfigureAwait(true);
            Assert.That(externalExit, Is.EqualTo(2),
                externalResult + externalError);
            AssertInputDiagnosticCode(externalResult, "TRM2001");
            Assert.That(externalResult, Does.Contain("checked ordinary reference"));
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EvaluationCoreManifestCompileTrRmChecksIdentityAndContextAsync(
        bool library)
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-evaluation-core-trrm-");
        var manifest = Path.Combine(directory, "evaluation.json");
        var output = Path.Combine(directory, "output.trrm");
        try
        {
            await File.WriteAllTextAsync(manifest,
                CreateEvaluationCoreManifest()).ConfigureAwait(true);
            var arguments = new List<string> {
                "compile-trrm", "-", "--identity", "sample.consumer",
                "-o", output, "--runtime", "fail-fast",
                "--arithmetic-profile", "release",
            };
            if (library)
            {
                arguments.Add("--library");
            }
            var (plainExit, plainResult, plainError) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { return 0; }"),
                [.. arguments, "--json"]).ConfigureAwait(true);
            Assert.That(plainExit, Is.Zero, plainResult + plainError);
            var previous = await File.ReadAllBytesAsync(output)
                .ConfigureAwait(true);
            arguments.AddRange(["--evaluation-core-manifest", manifest,
                "--json"]);
            arguments[3] = "different.consumer";
            var (identityExit, identityResult, identityError) = await RunAsync(
                [0xFF], [.. arguments]).ConfigureAwait(true);
            Assert.That(identityExit, Is.EqualTo(2),
                identityResult + identityError);
            AssertInputDiagnosticCode(identityResult, "TRM2000");
            arguments[3] = "sample.consumer";
            var (contextExit, contextResult, contextError) = await RunAsync(
                Encoding.UTF8.GetBytes(
                    EvaluationCoreCallable + "\n" + EvaluationCoreEntry),
                [.. arguments]).ConfigureAwait(true);
            Assert.That(contextExit, Is.EqualTo(1),
                contextResult + contextError);
            AssertInputDiagnosticCode(contextResult, "TRM5001");
            Assert.That(contextResult,
                Does.Contain("evaluation-form schema context"));
            Assert.That(await File.ReadAllBytesAsync(output)
                .ConfigureAwait(true), Is.EqualTo(previous));
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EvaluationCoreManifestReadsInOptionOrderAsync(
        bool evaluationFirst)
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-evaluation-core-input-order-");
        var evaluation = Path.Combine(directory, "bad-evaluation.json");
        var native = Path.Combine(directory, "bad-native.json");
        try
        {
            await File.WriteAllTextAsync(evaluation, "{}").ConfigureAwait(true);
            await File.WriteAllTextAsync(native, "{}").ConfigureAwait(true);
            string[] selections = evaluationFirst
                ? ["--evaluation-core-manifest", evaluation,
                    "--native-core-manifest", native]
                : ["--native-core-manifest", native,
                    "--evaluation-core-manifest", evaluation];
            var (exit, result, error) = await RunAsync([0xFF],
                ["analyze", "-", .. selections, "--json"])
                .ConfigureAwait(true);
            Assert.That(exit, Is.EqualTo(2), result + error);
            AssertInputDiagnosticCode(result, "TRM2001");
            Assert.That(result, Does.Contain(Path.GetFileName(
                evaluationFirst ? evaluation : native)));
            Assert.That(result, Does.Not.Contain(Path.GetFileName(
                evaluationFirst ? native : evaluation)));
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task EvaluationCoreManifestRejectsPathAliasesAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-evaluation-core-path-");
        var manifest = Path.Combine(directory, "evaluation.json");
        var output = Path.Combine(directory, "output.dll");
        try
        {
            var contents = Encoding.UTF8.GetBytes(
                CreateEvaluationCoreManifest());
            await File.WriteAllBytesAsync(manifest, contents)
                .ConfigureAwait(true);
            string[] selection = ["--evaluation-core-manifest", manifest];
            string[][] aliases = [
                ["analyze", manifest, .. selection, "--json"],
                ["analyze", "-", .. selection,
                    "--layout-core-manifest", manifest, "--json"],
                ["build-library", "-", "--export", "Add", "-o", output,
                    "--runtime", "fail-fast", "--source-grant",
                    "--abi-output", manifest, .. selection, "--json"],
                ["compile-trrm", "-", "--identity", "sample.consumer",
                    "-o", Path.Combine(directory, "output.trrm"),
                    "--runtime", "fail-fast", "--trrm-schema",
                    "trrm/1-draft-nominal-enums-1",
                    "--trrm-source-enum-exports", manifest,
                    .. selection, "--json"],
                ["compile-trrm", "-", "--identity", "sample.consumer",
                    "-o", Path.Combine(directory, "output.trrm"),
                    "--runtime", "fail-fast", "--trrm-schema",
                    "trrm/1-draft-generic-method-calls-parameter-labels-1",
                    "--trrm-source-generic-method-exports", manifest,
                    .. selection, "--json"],
            ];
            foreach (var arguments in aliases)
            {
                var (exit, result, error) = await RunAsync([0xFF], arguments)
                    .ConfigureAwait(true);
                Assert.That(exit, Is.EqualTo(2), result + error);
                AssertInputDiagnosticCode(result, "TRM2002");
            }
            if (OperatingSystem.IsWindows())
            {
                foreach (var command in new[] { "build", "compile-trrm" })
                {
                    var alias = Path.Combine(directory,
                        command == "build" ? "alias.exe" : "alias.trrm");
                    Assert.That(CreateHardLink(alias, manifest, IntPtr.Zero),
                        Is.True);
                    var arguments = new List<string> {
                        command, "-", "-o", alias, "--runtime", "fail-fast",
                    };
                    arguments.AddRange(selection);
                    if (command == "compile-trrm")
                    {
                        arguments.AddRange(["--identity", "sample.consumer"]);
                    }
                    arguments.Add("--json");
                    var (exit, result, error) = await RunAsync([0xFF],
                        [.. arguments]).ConfigureAwait(true);
                    Assert.That(exit, Is.EqualTo(2), result + error);
                    AssertInputDiagnosticCode(result, "TRM2002");
                }
            }
            Assert.That(await File.ReadAllBytesAsync(manifest)
                .ConfigureAwait(true), Is.EqualTo(contents));
            Assert.That(File.Exists(output), Is.False);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task EvaluationCoreManifestRejectsMalformedOptionsAsync()
    {
        const string option = "--evaluation-core-manifest";
        string[][] invalidArguments = [
            ["analyze", "-", option, "--json"],
            ["analyze", "-", option, "", "--json"],
            ["analyze", "-", option, "-", "--json"],
            ["analyze", "-", option, "--not-a-path", "--json"],
            ["analyze", "-", option + "=missing.json", "--json"],
            ["analyze", "-", "--Evaluation-core-manifest", "missing.json",
                "--json"],
            ["analyze", "-", option, "missing.json", option, "missing.json",
                "--json"],
            ["analyze", "-", option, "missing.json",
                "--machine", "terrum.cli/1", "--json"],
            ["build", "-", "-o", "missing.exe", "--runtime", "fail-fast",
                "--source-name", "subject.terrum", "--frontend-command",
                "frontend-build", option, "missing.json", "--json"],
        ];
        foreach (var arguments in invalidArguments)
        {
            var (exit, result, error) = await RunAsync([0xFF], arguments)
                .ConfigureAwait(true);
            Assert.That(exit, Is.EqualTo(2),
                string.Join(' ', arguments) + result + error);
            AssertInputDiagnosticCode(result, "TRM2000");
        }
    }

    private static IEnumerable<TestCaseData> InvalidEvaluationCoreManifests()
    {
        (string Name, Action<JsonObject> Change)[] cases = [
            ("schema", root => root["schema"] = "unknown"),
            ("missing-field", root => root.Remove("usingModuleIdentity")),
            ("unknown-field", root => root["providers"] = new JsonArray()),
            ("empty-text", root => root["usingModuleIdentity"] = ""),
            ("nul-text", root => root["coreAuthorityIdentity"] = "bad\0text"),
            ("no-role", root => root["constEvalSchema"] = null),
            ("no-catalog", root => root["localSchemaDeclarations"] = null),
            ("external", root => {
                root["constEvalSchema"]!["providerIdentity"] = "external.core";
                root["localSchemaDeclarations"] = null;
            }),
            ("owner", root => Catalog(root)["origin"]!["moduleIdentity"]
                = "other.module"),
            ("revision", root => Catalog(root)["origin"]!["schema"]!["revision"]
                = 0),
            ("edition", root => Catalog(root)["origin"]![
                "governingSemanticEdition"] = 2),
            ("target", root => Catalog(root)["origin"]!["targetRequirement"]
                = 2),
            ("runtime-domain", root => Catalog(root)["origin"]!["runtimePolicy"]
                = 2),
            ("runtime-context", root => Catalog(root)["origin"]!["runtimePolicy"]
                = 1),
            ("name", root => Declaration(root)["qualifiedName"] = " "),
            ("arity", root => Declaration(root)["genericArity"] = 1),
            ("arguments", root => Declaration(root)["argumentCount"] = 1),
            ("visibility", root => Declaration(root)["isPublic"] = false),
            ("primary", root => Declaration(root)["isPrimary"] = false),
            ("primary-target", root => Declaration(root)["isPrimaryTargetOnly"]
                = false),
            ("repeatable", root => Declaration(root)["isRepeatable"] = true),
            ("unknown-target", root => Declaration(root)["targets"]![0]
                = "whole-property"),
            ("duplicate-target", root => Declaration(root)["targets"]![1]
                = "ordinary-named-callable"),
            ("wrong-type", root => Declaration(root)["isPrimary"] = "true"),
            ("missing-target", root => Declaration(root)["targets"]!
                .AsArray().RemoveAt(0)),
            ("duplicate-export", root => Catalog(root)["exports"]!.AsArray()
                .Add(Catalog(root)["exports"]![0]!.DeepClone())),
            ("dangling-export", root => Catalog(root)["exports"]![0]![
                "declarationIndex"] = 1),
            ("unexported", root => Catalog(root)["exports"] = new JsonArray()),
            ("unused-invalid-declaration", root => {
                var unused = Declaration(root).DeepClone();
                unused["qualifiedName"] = "Other.Unused";
                unused["argumentCount"] = 1;
                Catalog(root)["declarations"]!.AsArray().Add(unused);
                Catalog(root)["exports"]!.AsArray().Add(new JsonObject {
                    ["exportKey"] = "unused", ["declarationIndex"] = 1,
                });
            }),
            ("role-alias-collision", root => {
                root["pureSchema"] = root["constEvalSchema"]!.DeepClone();
                root["pureSchema"]!["exportKey"] = "pure-alias";
                Catalog(root)["exports"]!.AsArray().Add(new JsonObject {
                    ["exportKey"] = "pure-alias", ["declarationIndex"] = 0,
                });
            }),
        ];
        foreach (var (name, change) in cases)
        {
            var root = JsonNode.Parse(
                CreateEvaluationCoreManifest())!.AsObject();
            change(root);
            yield return Case(name, Encoding.UTF8.GetBytes(
                root.ToJsonString()));
        }

        var valid = CreateEvaluationCoreManifest();
        foreach (var token in new[] { "-0", "0.0", "0e0", "4294967296", "\"0\"" })
        {
            yield return Case("index-token-" + token,
                Encoding.UTF8.GetBytes(valid.Replace(
                    "\"declarationIndex\":0", "\"declarationIndex\":" + token,
                    StringComparison.Ordinal)));
        }
        yield return Case("invalid-utf8", [0xFF]);
        yield return Case("repeated-signature",
            [0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF,
                .. Encoding.UTF8.GetBytes(valid)]);
        yield return Case("comments",
            Encoding.UTF8.GetBytes("/*comment*/" + valid));
        yield return Case("trailing-json",
            Encoding.UTF8.GetBytes(valid + "{}"));
        yield return Case("trailing-comma",
            Encoding.UTF8.GetBytes(valid[..^1] + ",}"));
        yield return Case("duplicate-decoded-property",
            Encoding.UTF8.GetBytes("{\"sch\\u0065ma\":null," + valid[1..]));
        yield return Case("nested-duplicate-property",
            Encoding.UTF8.GetBytes(valid.Replace(
                "\"genericArity\":0",
                "\"genericArity\":0,\"generic\\u0041rity\":0",
                StringComparison.Ordinal)));
        yield return Case("unpaired-surrogate",
            Encoding.UTF8.GetBytes(valid.Replace(
                "\"usingModuleIdentity\":\"sample.consumer\"",
                "\"usingModuleIdentity\":\"\\uD800\"",
                StringComparison.Ordinal)));

        static JsonNode Catalog(JsonObject root)
        {
            return root["localSchemaDeclarations"]!;
        }

        static JsonNode Declaration(JsonObject root)
        {
            return Catalog(root)["declarations"]![0]!;
        }

        static TestCaseData Case(string name, byte[] contents)
        {
            return new TestCaseData((object)contents)
                .SetName($"EvaluationCoreManifestRejectsInvalidInput({name})");
        }
    }

    private static string CreateEvaluationCoreManifest(
        bool pure = false, bool constEval = true,
        string usingModuleIdentity = "sample.consumer",
        string coreAuthorityIdentity = "test-layout-authority",
        string exportKey = "evaluation/constant-source")
    {
        var declarations = new List<object>();
        var exports = new List<object>();
        object? pureSchema = null;
        object? constEvalSchema = null;
        if (pure)
        {
            var key = constEval ? "evaluation/pure-source" : exportKey;
            pureSchema = new { providerIdentity = usingModuleIdentity,
                exportKey = key };
            exports.Add(new { exportKey = key,
                declarationIndex = declarations.Count });
            declarations.Add(Declaration("Evaluation.Contracts.PureSource"));
        }
        if (constEval)
        {
            constEvalSchema = new {
                providerIdentity = usingModuleIdentity, exportKey,
            };
            exports.Add(new { exportKey,
                declarationIndex = declarations.Count });
            declarations.Add(Declaration(
                "Evaluation.Contracts.ConstantSource"));
        }
        return JsonSerializer.Serialize(new {
            schema = "terrumc.evaluation-core-manifest/1",
            coreAuthorityIdentity,
            usingModuleIdentity,
            pureSchema,
            constEvalSchema,
            localSchemaDeclarations = new {
                origin = new {
                    moduleIdentity = usingModuleIdentity,
                    revisionIdentity = "evaluation-revision-1",
                    schema = new {
                        identity = "evaluation-declarations", revision = 1,
                    },
                    governingSemanticEdition = 1,
                    targetRequirement = 0,
                    runtimePolicy = 0,
                },
                declarations,
                exports,
            },
        });

        static object Declaration(string name)
        {
            return new {
                qualifiedName = name,
                genericArity = 0,
                argumentCount = 0,
                targets = new[] {
                    "ordinary-named-callable", "individual-accessor",
                    "eligible-initialization-constructor",
                },
                isPublic = true,
                isPrimary = true,
                isPrimaryTargetOnly = true,
                isRepeatable = false,
            };
        }
    }
}
