// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

internal sealed partial class CliTests
{
    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win")]
    public async Task CompilerPrimitiveTargetsKeepModuleConfigurationIndependentAsync(
        bool sourceGrant)
    {
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-primitive-target-config-");
        var root = RepositoryRoot();
        var contexts = new[] {
            (
                Target: "BuildNativeCompilerPrimitives",
                LayoutProperty: "LayoutCoreTestManifest",
                EvaluationProperty: "EvaluationCoreTestManifest",
                LayoutFile: "build-layout-core-manifest.json",
                EvaluationFile: "build-evaluation-core-manifest.json",
                Artifact: "terrum-compiler-primitives.dll",
                State: "compiler-primitive-evaluation-selection.txt"
            ),
            (
                Target: "BuildNativeCompilerPrimitivesSourceGrant",
                LayoutProperty: "SourceGrantLayoutCoreTestManifest",
                EvaluationProperty: (string?)null,
                LayoutFile: "build-source-grant-layout-core-manifest.json",
                EvaluationFile: (string?)null,
                Artifact: "terrum-compiler-primitives-source-grant.dll",
                State: "compiler-primitive-source-grant-layout-selection.txt"
            ),
        };
        var (target, layoutProperty, evaluationProperty, layoutFile,
            evaluationFile, artifactName, stateFile)
            = contexts[sourceGrant ? 1 : 0];
        var (_, otherLayoutProperty, otherEvaluationProperty, otherLayoutFile,
            _, otherArtifactName, otherStateFile)
            = contexts[sourceGrant ? 0 : 1];
        var configuration = new DirectoryInfo(AppContext.BaseDirectory)
            .Parent!.Name;
        var outputDirectory = Path.Combine(directory, "artifacts", "bin",
            "sources", "Terrum.CodeAnalysis.Terrum", configuration);
        var artifact = Path.Combine(outputDirectory, artifactName);
        var otherArtifact = Path.Combine(outputDirectory, otherArtifactName);
        var state = Path.Combine(directory, "obj", stateFile);
        var otherState = Path.Combine(directory, "obj", otherStateFile);
        var missing = Path.Combine(directory, "missing.json");
        var otherMissing = new Dictionary<string, string>(StringComparer.Ordinal) {
            [otherLayoutProperty] = missing,
        };
        if (otherEvaluationProperty is string otherEvaluation)
        {
            otherMissing[otherEvaluation] = missing;
        }
        var bothTargets = contexts[0].Target + ";" + contexts[1].Target;
        try
        {
            var (exit, result, error) = await RunCompilerPrimitiveTargetsAsync(
                directory, target, otherMissing).ConfigureAwait(true);
            Assert.That(exit, Is.Zero, result + error);
            Assert.That(new FileInfo(artifact).Length, Is.GreaterThan(0));
            Assert.That(File.Exists(state), Is.True);
            Assert.That(File.Exists(otherArtifact), Is.False);
            Assert.That(File.Exists(otherState), Is.False);
            var published = await File.ReadAllBytesAsync(artifact)
                .ConfigureAwait(true);

            var requiredProperties = new List<string> { layoutProperty };
            if (evaluationProperty is string selectedEvaluationProperty)
            {
                requiredProperties.Add(selectedEvaluationProperty);
            }
            foreach (var property in requiredProperties)
            {
                foreach (var invalid in new[] { "", missing })
                {
                    var invalidProperties = new Dictionary<string, string>(
                        otherMissing, StringComparer.Ordinal) {
                        [property] = invalid,
                    };
                    var (missingExit, missingResult, missingError)
                        = await RunCompilerPrimitiveTargetsAsync(directory,
                            target, invalidProperties)
                            .ConfigureAwait(true);
                    Assert.That(missingExit, Is.Not.Zero,
                        missingResult + missingError);
                    Assert.That(missingResult,
                        Does.Contain("requires an explicit existing " + property));
                    Assert.That(await File.ReadAllBytesAsync(artifact)
                        .ConfigureAwait(true), Is.EqualTo(published));
                    Assert.That(File.Exists(otherState), Is.False);
                }
            }

            if (evaluationProperty is not null)
            {
                var otherLayout = Path.Combine(root, "tests", "Fixtures",
                    "LayoutCore", otherLayoutFile);
                var (mismatchExit, mismatchResult, mismatchError)
                    = await RunCompilerPrimitiveTargetsAsync(directory,
                        target, new Dictionary<string, string>(
                            StringComparer.Ordinal) {
                            [layoutProperty] = otherLayout,
                        }).ConfigureAwait(true);
                Assert.That(mismatchExit, Is.Not.Zero,
                    mismatchResult + mismatchError);
                Assert.That(mismatchResult,
                    Does.Contain("authority and usingModuleIdentity must match exactly"));
                Assert.That(await File.ReadAllBytesAsync(artifact)
                    .ConfigureAwait(true), Is.EqualTo(published));
            }

            var (baselineExit, baselineResult, baselineError)
                = await RunCompilerPrimitiveTargetsAsync(directory,
                    bothTargets).ConfigureAwait(true);
            Assert.That(baselineExit, Is.Zero,
                baselineResult + baselineError);
            Assert.That(File.Exists(otherArtifact), Is.True);
            Assert.That(File.Exists(otherState), Is.True);

            var (unchangedExit, unchangedResult, unchangedError)
                = await RunCompilerPrimitiveTargetsAsync(directory,
                    bothTargets).ConfigureAwait(true);
            Assert.That(unchangedExit, Is.Zero,
                unchangedResult + unchangedError);
            Assert.That(unchangedResult,
                Does.Not.Contain(artifact + ": Windows x64"));
            Assert.That(unchangedResult,
                Does.Not.Contain(otherArtifact + ": Windows x64"));

            var selections = new List<(string Property, string Family,
                string File)> {
                (layoutProperty, "LayoutCore", layoutFile),
            };
            if (evaluationProperty is string evaluationSelectionProperty
                && evaluationFile is string evaluationSelectionFile)
            {
                selections.Add((evaluationSelectionProperty,
                    "EvaluationCore", evaluationSelectionFile));
            }
            foreach (var (property, family, file) in selections)
            {
                var copy = Path.Combine(directory, property + ".json");
                File.Copy(Path.Combine(root, "tests", "Fixtures", family,
                    file), copy);
                File.SetLastWriteTimeUtc(copy, new DateTime(2000, 1, 1,
                    0, 0, 0, DateTimeKind.Utc));
                var selection = new Dictionary<string, string>(
                    StringComparer.Ordinal) {
                    [property] = copy,
                };
                foreach (var changeContents in new[] { false, true })
                {
                    if (changeContents)
                    {
                        await File.AppendAllTextAsync(copy, " ")
                            .ConfigureAwait(true);
                        File.SetLastWriteTimeUtc(copy, new DateTime(2000, 1, 1,
                            0, 0, 0, DateTimeKind.Utc));
                    }
                    var previousState = await File.ReadAllTextAsync(state)
                        .ConfigureAwait(true);
                    var previousOtherState = await File.ReadAllTextAsync(
                        otherState).ConfigureAwait(true);
                    var otherStateTime = File.GetLastWriteTimeUtc(otherState);
                    var otherArtifactTime = File.GetLastWriteTimeUtc(
                        otherArtifact);
                    var (changedExit, changedResult, changedError)
                        = await RunCompilerPrimitiveTargetsAsync(directory,
                            bothTargets, selection).ConfigureAwait(true);
                    Assert.That(changedExit, Is.Zero,
                        changedResult + changedError);
                    Assert.That(changedResult,
                        Does.Contain(artifact + ": Windows x64"));
                    Assert.That(changedResult,
                        Does.Not.Contain(otherArtifact + ": Windows x64"));
                    Assert.That(await File.ReadAllTextAsync(state)
                        .ConfigureAwait(true), Is.Not.EqualTo(previousState));
                    Assert.That(await File.ReadAllTextAsync(otherState)
                        .ConfigureAwait(true), Is.EqualTo(previousOtherState));
                    Assert.That(File.GetLastWriteTimeUtc(otherState),
                        Is.EqualTo(otherStateTime));
                    Assert.That(File.GetLastWriteTimeUtc(otherArtifact),
                        Is.EqualTo(otherArtifactTime));
                }

                var (restoredExit, restoredResult, restoredError)
                    = await RunCompilerPrimitiveTargetsAsync(directory,
                        target).ConfigureAwait(true);
                Assert.That(restoredExit, Is.Zero,
                    restoredResult + restoredError);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)>
        RunCompilerPrimitiveTargetsAsync(string directory, string targets,
            Dictionary<string, string>? properties = null)
    {
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent!.Name;
        var root = RepositoryRoot();
        var cliDirectory = Path.Combine(root, "artifacts", "bin", "sources",
            "terrumc", configuration, frameworkDirectory.Name);
        var start = new ProcessStartInfo("dotnet") {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        string[] arguments = [
            "msbuild", Path.Combine(root, "sources", "terrumc", "terrumc.csproj"),
            "-nologo", "-v:minimal", "-t:" + targets,
            "-p:Configuration=" + configuration,
            "-p:BaseArtifactsPath=" + Path.Combine(directory, "artifacts")
                + Path.DirectorySeparatorChar,
            "-p:IntermediateOutputPath=" + Path.Combine(directory, "obj")
                + Path.DirectorySeparatorChar,
            "-p:TargetPath=" + Path.Combine(cliDirectory, "terrumc.dll"),
            "-p:TargetDir=" + cliDirectory + Path.DirectorySeparatorChar,
        ];
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        if (properties is not null)
        {
            foreach (var (name, value) in properties)
            {
                start.ArgumentList.Add("-p:" + name + "=" + value);
            }
        }
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start MSBuild.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync()
                .WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(true);
            }
            throw;
        }

        var result = await output.ConfigureAwait(true);
        var errors = await error.ConfigureAwait(true);
        TestContext.WriteLine($"MSBuild {targets}: exit {process.ExitCode}");
        TestContext.WriteLine(result + errors);
        return (process.ExitCode, result, errors);
    }
}
