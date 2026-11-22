using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct StandaloneMethodRoot(
    int ArtifactInputOrdinal, string ModuleIdentity, string ExportKey);

internal readonly record struct StandaloneMethodImportInput(
    string Agreement, string UsingModuleIdentity,
    IReadOnlyList<TrRmStandardArtifactInput> Artifacts,
    IReadOnlyList<StandaloneMethodRoot> MethodRoots)
{
    /// <summary>Copies the raw request values before any artifact decoding.</summary>
    /// <remarks>
    /// The caller must not mutate either list or artifact backing storage during
    /// capture. Artifact bytes are copied; schema and identity strings are immutable.
    /// </remarks>
    internal CapturedStandaloneMethodImportInput Capture()
    {
        ArgumentNullException.ThrowIfNull(Agreement);
        ArgumentNullException.ThrowIfNull(UsingModuleIdentity);
        ArgumentNullException.ThrowIfNull(Artifacts);
        ArgumentNullException.ThrowIfNull(MethodRoots);

        var artifactCount = Artifacts.Count;
        var artifactBuilder
            = ImmutableArray.CreateBuilder<TrRmStandardArtifactInput>(
                artifactCount);
        for (var index = 0; index < artifactCount; index++)
        {
            var artifact = Artifacts[index]
                ?? throw new ArgumentException(
                    "The standalone artifact list cannot contain null entries.",
                    nameof(Artifacts));
            ArgumentNullException.ThrowIfNull(artifact.SchemaIdentity);
            artifactBuilder.Add(new TrRmStandardArtifactInput(
                artifact.SchemaIdentity, artifact.Artifact.ToArray()));
        }
        if (Artifacts.Count != artifactCount)
        {
            throw new InvalidOperationException(
                "The standalone artifact list changed during capture.");
        }

        var methodRootCount = MethodRoots.Count;
        var methodRootBuilder
            = ImmutableArray.CreateBuilder<StandaloneMethodRoot>(
                methodRootCount);
        for (var index = 0; index < methodRootCount; index++)
        {
            var methodRoot = MethodRoots[index];
            ArgumentNullException.ThrowIfNull(methodRoot.ModuleIdentity);
            ArgumentNullException.ThrowIfNull(methodRoot.ExportKey);
            methodRootBuilder.Add(methodRoot);
        }
        if (MethodRoots.Count != methodRootCount)
        {
            throw new InvalidOperationException(
                "The standalone method-root list changed during capture.");
        }

        return new CapturedStandaloneMethodImportInput(Agreement,
            UsingModuleIdentity, artifactBuilder.MoveToImmutable(),
            methodRootBuilder.MoveToImmutable());
    }
}

internal readonly record struct CapturedStandaloneMethodImportInput(
    string Agreement, string UsingModuleIdentity,
    ImmutableArray<TrRmStandardArtifactInput> Artifacts,
    ImmutableArray<StandaloneMethodRoot> MethodRoots)
{
    /// <summary>
    /// Exposes only captured request values and raw artifact bytes to one
    /// in-process Terrum invocation.
    /// </summary>
    internal ImmutableArray<HostBuffer> CreateHostBuffers()
    {
        ArgumentNullException.ThrowIfNull(Agreement);
        ArgumentNullException.ThrowIfNull(UsingModuleIdentity);
        if (Artifacts.IsDefault || MethodRoots.IsDefault)
        {
            throw new InvalidOperationException(
                "Standalone import inputs must be captured before execution.");
        }

        var textLength = checked(Agreement.Length
            + UsingModuleIdentity.Length);
        var artifactByteLength = 0;
        foreach (var artifact in Artifacts)
        {
            ArgumentNullException.ThrowIfNull(artifact);
            ArgumentNullException.ThrowIfNull(artifact.SchemaIdentity);
            textLength = checked(textLength + artifact.SchemaIdentity.Length);
            artifactByteLength = checked(artifactByteLength
                + artifact.Artifact.Length);
        }

        foreach (var root in MethodRoots)
        {
            ArgumentNullException.ThrowIfNull(root.ModuleIdentity);
            ArgumentNullException.ThrowIfNull(root.ExportKey);
            textLength = checked(textLength + root.ModuleIdentity.Length
                + root.ExportKey.Length);
        }

        var text = ImmutableArray.CreateBuilder<int>(textLength);
        var artifactBytes
            = ImmutableArray.CreateBuilder<int>(artifactByteLength);
        var artifactInputs
            = ImmutableArray.CreateBuilder<int>(
                checked(Artifacts.Length * 4));
        var methodRoots = ImmutableArray.CreateBuilder<int>(
            checked(MethodRoots.Length * 5));
        var resolvedRootsCapacity = checked(MethodRoots.Length * 20);
        var sectionCapacity = checked((artifactByteLength / 16) + 1);
        var moduleCapacity = checked((artifactByteLength / 48) + 2);
        var moduleDirectoryCapacity = checked(moduleCapacity * 8);
        var parameterTypeCapacity = checked((artifactByteLength / 16) + 1);
        var contractFactCapacity = checked((artifactByteLength / 20) + 1);
        var contractFactStorage = checked(contractFactCapacity * 7);
        var importCapacity = checked((artifactByteLength / 12) + 1);
        var importFactStorage = checked(importCapacity * 32);
        var callableCapacity
            = checked(MethodRoots.Length + (importCapacity * 2));
        var artifactParameterStartCapacity
            = checked(Artifacts.Length + 1);

        var agreementStart = AppendText(Agreement);
        var usingModuleStart = AppendText(UsingModuleIdentity);
        foreach (var artifact in Artifacts)
        {
            var schemaStart = AppendText(artifact.SchemaIdentity);
            var artifactStart = artifactBytes.Count;
            foreach (var value in artifact.Artifact.Span)
            {
                artifactBytes.Add(value);
            }

            artifactInputs.Add(schemaStart);
            artifactInputs.Add(artifact.SchemaIdentity.Length);
            artifactInputs.Add(artifactStart);
            artifactInputs.Add(artifact.Artifact.Length);
        }

        foreach (var root in MethodRoots)
        {
            var moduleStart = AppendText(root.ModuleIdentity);
            var exportKeyStart = AppendText(root.ExportKey);
            methodRoots.Add(root.ArtifactInputOrdinal);
            methodRoots.Add(moduleStart);
            methodRoots.Add(root.ModuleIdentity.Length);
            methodRoots.Add(exportKeyStart);
            methodRoots.Add(root.ExportKey.Length);
        }

        var request = ImmutableArray.Create(
            agreementStart, Agreement.Length, usingModuleStart,
            UsingModuleIdentity.Length, Artifacts.Length, MethodRoots.Length);
        return [
            new HostBuffer("StandaloneRequest", PrimitiveType.Int32,
                request, false),
            new HostBuffer("StandaloneText", PrimitiveType.Char16,
                text.MoveToImmutable(), false),
            new HostBuffer("StandaloneArtifacts", PrimitiveType.Int32,
                artifactBytes.MoveToImmutable(), false),
            new HostBuffer("StandaloneArtifactInputs",
                PrimitiveType.Int32, artifactInputs.MoveToImmutable(), false),
            new HostBuffer("StandaloneMethodRoots", PrimitiveType.Int32,
                methodRoots.MoveToImmutable(), false),
            new HostBuffer("StandaloneSectionCount", PrimitiveType.Int32,
                [0], true),
            new HostBuffer("StandaloneSectionArtifactOrdinal",
                PrimitiveType.Int32, [-1], true),
            new HostBuffer("StandaloneSectionKinds", PrimitiveType.Int32,
                [.. new int[sectionCapacity]], true),
            new HostBuffer("StandaloneSectionFlags", PrimitiveType.Int32,
                [.. new int[sectionCapacity]], true),
            new HostBuffer("StandaloneSectionCounts", PrimitiveType.Int32,
                [.. new int[sectionCapacity]], true),
            new HostBuffer("StandaloneSectionStarts", PrimitiveType.Int32,
                [.. new int[sectionCapacity]], true),
            new HostBuffer("StandaloneSectionLengths", PrimitiveType.Int32,
                [.. new int[sectionCapacity]], true),
            new HostBuffer("StandaloneSectionOrder", PrimitiveType.Int32,
                [.. new int[sectionCapacity]], true),
            new HostBuffer("StandaloneModuleCount", PrimitiveType.Int32,
                [0], true),
            new HostBuffer("StandaloneModuleDirectoryStatus",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneArtifactParameterStarts",
                PrimitiveType.Int32,
                [.. new int[artifactParameterStartCapacity]], true),
            new HostBuffer("StandaloneParameterTypeIds",
                PrimitiveType.Int32,
                [.. new int[parameterTypeCapacity]], true),
            new HostBuffer("StandaloneModuleDirectory",
                PrimitiveType.Int32,
                [.. new int[moduleDirectoryCapacity]], true),
            new HostBuffer("StandaloneModuleOrder", PrimitiveType.Int32,
                [.. new int[moduleCapacity]], true),
            new HostBuffer("StandaloneTypeFactCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneTypeFacts", PrimitiveType.Int32,
                [.. new int[53 * 3]], true),
            new HostBuffer("StandaloneContractFactCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneContractFacts",
                PrimitiveType.Int32,
                [.. new int[contractFactStorage]], true),
            new HostBuffer("StandaloneContractFactOrder",
                PrimitiveType.Int32,
                [.. new int[contractFactCapacity]], true),
            new HostBuffer("StandaloneContractComparisonFactCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneContractComparisonFacts",
                PrimitiveType.Int32,
                [.. new int[contractFactStorage]], true),
            new HostBuffer("StandaloneContractComparisonFactOrder",
                PrimitiveType.Int32,
                [.. new int[contractFactCapacity]], true),
            new HostBuffer("StandaloneResolvedImportCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneResolvedImports",
                PrimitiveType.Int32,
                [.. new int[importFactStorage]], true),
            new HostBuffer("StandaloneResolvedImportOrder",
                PrimitiveType.Int32,
                [.. new int[importCapacity]], true),
            new HostBuffer("StandaloneResolvedExpectedImportCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneResolvedProviderGroupCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneResolvedExpectedImportOrder",
                PrimitiveType.Int32,
                [.. new int[importCapacity]], true),
            new HostBuffer("StandaloneResolvedRootCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneResolvedRoots",
                PrimitiveType.UInt32,
                [.. new int[resolvedRootsCapacity]], true),
            new HostBuffer("StandaloneCallableTypeIds",
                PrimitiveType.Int32,
                [.. new int[callableCapacity]], true),
            new HostBuffer("StandaloneCallableEffects",
                PrimitiveType.Int32,
                [.. new int[callableCapacity]], true),
        ];

        int AppendText(string value)
        {
            var start = text.Count;
            foreach (var codeUnit in value)
            {
                text.Add(codeUnit);
            }

            return start;
        }
    }
}
