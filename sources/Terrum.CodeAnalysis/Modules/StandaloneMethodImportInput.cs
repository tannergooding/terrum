using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Modules;

internal readonly record struct StandaloneMethodRoot(
    int ArtifactInputOrdinal, string ModuleIdentity, string ExportKey);

internal readonly record struct StandaloneMethodImportArtifactInput(
    string Agreement, string ModuleIdentity, TrRmStandardArtifactInput Input,
    IReadOnlyList<TrRmStandardArtifactInput> Providers)
{
    internal CapturedStandaloneMethodImportInput Capture()
    {
        ArgumentNullException.ThrowIfNull(Agreement);
        ArgumentNullException.ThrowIfNull(ModuleIdentity);
        ArgumentNullException.ThrowIfNull(Input);
        ArgumentNullException.ThrowIfNull(Input.SchemaIdentity);
        ArgumentNullException.ThrowIfNull(Providers);

        var providerCount = Providers.Count;
        var artifacts
            = ImmutableArray.CreateBuilder<TrRmStandardArtifactInput>(
                checked(providerCount + 1));
        artifacts.Add(new TrRmStandardArtifactInput(Input.SchemaIdentity,
            Input.Artifact.ToArray()));
        for (var index = 0; index < providerCount; index++)
        {
            var provider = Providers[index]
                ?? throw new ArgumentException(
                    "The provider list cannot contain null entries.",
                    nameof(Providers));
            ArgumentNullException.ThrowIfNull(provider.SchemaIdentity);
            artifacts.Add(new TrRmStandardArtifactInput(
                provider.SchemaIdentity, provider.Artifact.ToArray()));
        }
        if (Providers.Count != providerCount)
        {
            throw new InvalidOperationException(
                "The provider list changed during capture.");
        }

        return new CapturedStandaloneMethodImportInput(Agreement,
            ModuleIdentity, artifacts.MoveToImmutable(),
            ImmutableArray<StandaloneMethodRoot>.Empty);
    }
}

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
        var projectionInterfaceCapacity = checked((artifactByteLength / 44) + 1);
        var projectionMemberSeenCapacity = checked((artifactByteLength / 16) + 1);
        var projectionInterfaceStackCapacity
            = checked((artifactByteLength / 24) + 2);
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
            new HostBuffer("StandaloneProjectionInterfaceByTypeDef",
                PrimitiveType.Int32,
                [.. new int[projectionInterfaceCapacity]], true),
            new HostBuffer("StandaloneProjectionMemberSeen",
                PrimitiveType.Int32,
                [.. new int[projectionMemberSeenCapacity]], true),
            new HostBuffer("StandaloneProjectionViewSeen",
                PrimitiveType.Int32,
                [.. new int[projectionMemberSeenCapacity]], true),
            new HostBuffer("StandaloneProjectionInterfaceStack",
                PrimitiveType.Int32,
                [.. new int[projectionInterfaceStackCapacity]], true),
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

    internal ImmutableArray<HostBuffer> CreateSourceFreeReaderHostBuffers()
    {
        if (Artifacts.IsDefault || MethodRoots.IsDefault
            || Artifacts.IsEmpty || !MethodRoots.IsEmpty)
        {
            throw new InvalidOperationException(
                "Source-free decoding requires captured artifacts and no source MethodRoots.");
        }

        var artifactByteLength = 0;
        foreach (var artifact in Artifacts)
        {
            artifactByteLength = checked(artifactByteLength
                + artifact.Artifact.Length);
        }

        var methodCapacity = checked((artifactByteLength / 28) + 1);
        var bodyOwnerCapacity = checked(methodCapacity + 1);
        var importCapacity = checked((artifactByteLength / 12) + 1);
        var moduleCapacity = checked((artifactByteLength / 48) + 2);
        var homeMapCapacity = checked((artifactByteLength / 8) + 1);
        var typeMapCapacity = checked((artifactByteLength / 4) + 1);
        var methodMapCapacity = checked((artifactByteLength / 4) + 1);
        var constantMapCapacity = checked((artifactByteLength / 8) + 1);
        var formalCapacity = checked((artifactByteLength / 16) + 1);
        var callSiteCapacity = checked((artifactByteLength / 20) + 1);
        var callInputCapacity = checked((artifactByteLength / 4) + 1);
        return CreateHostBuffers().AddRange([
            new HostBuffer("StandaloneModuleDirectoryMode",
                PrimitiveType.Int32, [1], false),
            new HostBuffer("StandaloneDecoderStatus",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneDecoderSelectedModuleId",
                PrimitiveType.Int32, [-1], true),
            new HostBuffer("StandaloneDecoderMethodCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneDecoderMethods",
                PrimitiveType.Int32,
                [.. new int[checked(methodCapacity * 16)]], true),
            new HostBuffer("StandaloneDecoderBodyCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneDecoderBodies",
                PrimitiveType.Int32,
                [.. new int[checked(methodCapacity * 34)]], true),
            new HostBuffer("StandaloneDecoderBodyOwners",
                PrimitiveType.Int32,
                [.. new int[bodyOwnerCapacity]], true),
            new HostBuffer("StandaloneDecoderBodyOwnershipValidated",
                PrimitiveType.Int32,
                [.. new int[Artifacts.Length]], true),
            new HostBuffer("StandaloneDecoderHomeCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneDecoderHomes",
                PrimitiveType.Int32,
                [.. new int[checked(homeMapCapacity * 3)]], true),
            new HostBuffer("StandaloneDecoderFormalHomeBodies",
                PrimitiveType.Int32,
                [.. new int[formalCapacity]], true),
            new HostBuffer("StandaloneDecoderTypeMapCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneDecoderTypeMapTypeIds",
                PrimitiveType.Int32, [.. new int[typeMapCapacity]], true),
            new HostBuffer("StandaloneDecoderMethodMapCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneDecoderMethodMapTargets",
                PrimitiveType.Int32,
                [.. new int[checked(methodMapCapacity * 4)]], true),
            new HostBuffer("StandaloneDecoderConstantMapCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneDecoderConstantMapEntries",
                PrimitiveType.Int32,
                [.. new int[checked(constantMapCapacity * 3)]], true),
            new HostBuffer("StandaloneDecoderCallCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneDecoderTargetPlanBuilt",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneDecoderTargetBodiesValidated",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneDecoderCalls",
                PrimitiveType.Int32,
                [.. new int[checked(callSiteCapacity * 22)]], true),
            new HostBuffer("StandaloneDecoderCallInputCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneDecoderCallInputs",
                PrimitiveType.Int32,
                [.. new int[checked(callInputCapacity * 4)]], true),
            new HostBuffer("StandaloneDecoderRegisterTypeIds",
                PrimitiveType.Int32,
                [.. new int[checked(methodCapacity * 14)]], true),
            new HostBuffer("StandaloneDecoderLocalCallableCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneDecoderLocalCallables",
                PrimitiveType.Int32,
                [.. new int[checked(methodCapacity * 4)]], true),
            new HostBuffer("StandaloneDecoderImportedCallableCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneDecoderImportedCallables",
                PrimitiveType.Int32,
                [.. new int[checked(importCapacity * 16)]], true),
            new HostBuffer("StandaloneDecoderImportMapCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneDecoderImportMap",
                PrimitiveType.Int32,
                [.. new int[checked(importCapacity * 4)]], true),
            new HostBuffer("StandaloneDecoderExpectedGroupCallableIds",
                PrimitiveType.Int32,
                [.. new int[checked(importCapacity + 1)]], true),
            new HostBuffer(
                "StandaloneDecoderExpectedGroupRepresentativeImportIds",
                PrimitiveType.Int32,
                [.. new int[checked(importCapacity + 1)]], true),
            new HostBuffer("StandaloneDecoderMethodLookupSlots",
                PrimitiveType.Int32,
                [.. new int[checked(methodCapacity * 2 + 1)]], true),
            new HostBuffer("StandaloneDecoderImportLookupSlots",
                PrimitiveType.Int32,
                [.. new int[checked(importCapacity * 2 + 1)]], true),
            new HostBuffer("StandaloneDecoderModuleLookupSlots",
                PrimitiveType.Int32,
                [.. new int[checked(moduleCapacity * 2 + 1)]], true),
        ]);
    }

    internal ImmutableArray<HostBuffer> CreateSourceCallHostBuffers(
        SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > ScannerHost.MaximumSourceLength)
        {
            throw new ArgumentException(
                "The standalone source exceeds the scanner limit.",
                nameof(source));
        }

        var tokenCapacity = checked(source.Length + 1);
        var hostBuffers = CreateHostBuffers();
        var moduleCapacity = 0;
        var callableCapacity = 0;
        var typeCapacity = 0;
        var parameterTypeCapacity = 0;
        var callableEffectsCapacity = 0;
        for (var index = 0; index < hostBuffers.Length; index++)
        {
            var buffer = hostBuffers[index];
            if (buffer.Name == "StandaloneModuleDirectory")
            {
                moduleCapacity = buffer.Values.Length / 8;
            }
            else if (buffer.Name == "StandaloneCallableTypeIds")
            {
                callableCapacity = buffer.Values.Length;
            }
            else if (buffer.Name == "StandaloneCallableEffects")
            {
                callableEffectsCapacity = buffer.Values.Length;
            }
            else if (buffer.Name == "StandaloneTypeFacts")
            {
                typeCapacity = buffer.Values.Length / 3;
            }
            else if (buffer.Name == "StandaloneParameterTypeIds")
            {
                parameterTypeCapacity = buffer.Values.Length;
            }
        }

        if (callableEffectsCapacity != callableCapacity)
        {
            throw new InvalidOperationException(
                "Standalone callable buffers are missing.");
        }

        var sourceCallableCapacity = checked(callableCapacity
            + tokenCapacity);
        var expandedBuffers = hostBuffers.ToBuilder();
        for (var index = 0; index < hostBuffers.Length; index++)
        {
            if (hostBuffers[index].Name == "StandaloneCallableTypeIds")
            {
                expandedBuffers[index] = new HostBuffer(
                    "StandaloneCallableTypeIds", PrimitiveType.Int32,
                    [.. new int[sourceCallableCapacity]], true);
            }
            else if (hostBuffers[index].Name == "StandaloneCallableEffects")
            {
                expandedBuffers[index] = new HostBuffer(
                    "StandaloneCallableEffects", PrimitiveType.Int32,
                    [.. new int[sourceCallableCapacity]], true);
            }
        }
        hostBuffers = expandedBuffers.ToImmutable();

        var rewriteCallableCapacity
            = checked(callableCapacity + tokenCapacity);
        var rewriteMapCapacity = Math.Max(checked(tokenCapacity * 2),
            Math.Max(moduleCapacity,
                Math.Max(rewriteCallableCapacity,
                    checked(typeCapacity + 1))));
        var artifactByteLength = 0;
        foreach (var artifact in Artifacts)
        {
            artifactByteLength = checked(artifactByteLength
                + artifact.Artifact.Length);
        }

        var standaloneTextLength = 0;
        foreach (var buffer in hostBuffers)
        {
            if (buffer.Name == "StandaloneText")
            {
                standaloneTextLength = buffer.Values.Length;
                break;
            }
        }

        var writerOutputByteCapacity = checked(
            ((long)artifactByteLength * 4)
            + ((long)standaloneTextLength * 8)
            + ((long)source.Length * 128)
            + 65_536);
        var writerWordCapacity = checked((int)(
            (writerOutputByteCapacity + 3) / 4));
        var writerSectionCapacity = checked((int)Math.Min(32_790L,
            (artifactByteLength / 16L) + 32));
        var writerStringCapacity = checked(tokenCapacity
            + (MethodRoots.Length * 3) + 1);
        var writerStringHashCapacity = checked(
            (writerStringCapacity * 2) + 1);
        var writerConstantHashCapacity = checked((tokenCapacity * 2) + 1);
        var writerRootGroupHashCapacity = checked(
            (MethodRoots.Length * 2) + 1);
        var writerMethodCapacity = checked(
            tokenCapacity + MethodRoots.Length);
        var writerContractFactCapacity = checked(
            (artifactByteLength / 20) + 1);
        var writerTextCapacity = checked(artifactByteLength
            + standaloneTextLength + source.Length);
        return hostBuffers.AddRange([
            new HostBuffer("StandaloneSourceProgramStatus",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneLocalCallableCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneLocalCallables",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 3)]], true),
            new HostBuffer("StandaloneSourceCallableInfo",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 12)]], true),
            new HostBuffer("StandaloneSourceParameterCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneSourceParameters",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 4)]], true),
            new HostBuffer("StandaloneSourceBodyCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneSourceBodies",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 12)]], true),
            new HostBuffer("StandaloneSourceExpressionCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneSourceExpressions",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 8)]], true),
            new HostBuffer("StandaloneSourceExpressionUseCounts",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneSourceDirectCallUseCounts",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneSourceStatementCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneSourceStatements",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 3)]], true),
            new HostBuffer("StandaloneSourceImportedCallableCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneSourceImportedCallables",
                PrimitiveType.Int32,
                [.. new int[checked(MethodRoots.Length * 8)]], true),
            new HostBuffer("StandaloneSourceDirectCallCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneSourceDirectCalls",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 7)]], true),
            new HostBuffer("StandaloneSourceCallArgumentCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneSourceCallArguments",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 3)]], true),
            new HostBuffer("StandaloneSourceCallArgumentOwners",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneSourceCallFormalSeen",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneSourceUsingCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneSourceUsings",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 2)]], true),
            new HostBuffer("StandaloneNormalizedProgramStatus",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneNormalizedBodyCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneNormalizedBodies",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 12)]], true),
            new HostBuffer("StandaloneNormalizedHomeCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneNormalizedHomes",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 10)]], true),
            new HostBuffer("StandaloneNormalizedParameterHomeIds",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneNormalizedConstantCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneNormalizedConstants",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 4)]], true),
            new HostBuffer("StandaloneInvokeCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneInvokes",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 10)]], true),
            new HostBuffer("StandaloneInvokeUseCounts",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneInvokeInputCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneInvokeInputs",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 3)]], true),
            new HostBuffer("StandaloneInvokeInputUseCounts",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneNormalizedInputScratch",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 3)]], true),
            new HostBuffer("StandaloneNormalizedOperationCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneNormalizedOperations",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 12)]], true),
            new HostBuffer("StandaloneDirectCallInvokeMap",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneDirectCallTraversalCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneDirectCallTraversal",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneDirectCallTraversalSeen",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneDirectCallTraversalExpressionStates",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneRewriteStatus",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneRewriteCallableCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneRewriteModuleIds",
                PrimitiveType.Int32,
                [.. new int[moduleCapacity]], true),
            new HostBuffer("StandaloneRewriteLocalCallableIds",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneRewriteImportedCallableIds",
                PrimitiveType.Int32,
                [.. new int[MethodRoots.Length]], true),
            new HostBuffer("StandaloneRewriteCallableIds",
                PrimitiveType.Int32,
                [.. new int[rewriteCallableCapacity]], true),
            new HostBuffer("StandaloneRewriteTypeIds",
                PrimitiveType.Int32,
                [.. new int[checked(typeCapacity + 1)]], true),
            new HostBuffer("StandaloneRewriteBodyIds",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneRewriteExpressionIds",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneRewriteMapSeen",
                PrimitiveType.Int32,
                [.. new int[rewriteMapCapacity]], true),
            new HostBuffer("StandaloneRewrittenModuleDirectory",
                PrimitiveType.Int32,
                [.. new int[checked(moduleCapacity * 8)]], true),
            new HostBuffer("StandaloneRewrittenLocalCallables",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 3)]], true),
            new HostBuffer("StandaloneRewrittenImportedCallables",
                PrimitiveType.Int32,
                [.. new int[checked(MethodRoots.Length * 8)]], true),
            new HostBuffer("StandaloneRewrittenCallableTypeIds",
                PrimitiveType.Int32,
                [.. new int[rewriteCallableCapacity]], true),
            new HostBuffer("StandaloneRewrittenCallableEffects",
                PrimitiveType.Int32,
                [.. new int[rewriteCallableCapacity]], true),
            new HostBuffer("StandaloneRewrittenCallableParameterCounts",
                PrimitiveType.Int32,
                [.. new int[rewriteCallableCapacity]], true),
            new HostBuffer("StandaloneRewrittenTypeFacts",
                PrimitiveType.Int32,
                [.. new int[checked(typeCapacity * 3)]], true),
            new HostBuffer("StandaloneRewrittenParameterTypeIds",
                PrimitiveType.Int32,
                [.. new int[parameterTypeCapacity]], true),
            new HostBuffer("StandaloneRewrittenSourceCallableInfo",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 12)]], true),
            new HostBuffer("StandaloneRewrittenSourceParameters",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 4)]], true),
            new HostBuffer("StandaloneRewrittenSourceBodies",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 12)]], true),
            new HostBuffer("StandaloneRewrittenSourceExpressions",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 8)]], true),
            new HostBuffer("StandaloneRewrittenSourceStatements",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 3)]], true),
            new HostBuffer("StandaloneRewrittenSourceDirectCalls",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 7)]], true),
            new HostBuffer("StandaloneRewrittenSourceCallArguments",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 3)]], true),
            new HostBuffer("StandaloneRewrittenDirectCallInvokeMap",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneRewrittenDirectCallBodyIds",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneRewrittenDirectCallTraversalCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneRewrittenDirectCallTraversal",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneRewrittenNormalizedBodies",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 12)]], true),
            new HostBuffer("StandaloneRewrittenNormalizedHomes",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 10)]], true),
            new HostBuffer("StandaloneRewrittenNormalizedConstants",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 4)]], true),
            new HostBuffer("StandaloneRewrittenInvokes",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 10)]], true),
            new HostBuffer("StandaloneRewrittenInvokeInputs",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 3)]], true),
            new HostBuffer("StandaloneRewrittenNormalizedOperations",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 12)]], true),
            new HostBuffer("StandaloneRewrittenSourceBodyCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneRewrittenNormalizedBodyCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneRewrittenModuleCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneRewrittenLocalCallableCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneRewrittenImportedCallableCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneRewrittenCallableCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneRewrittenTypeFactCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneRewrittenParameterTypeCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneRewrittenSourceParameterCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneRewrittenSourceExpressionCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneRewrittenSourceStatementCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneRewrittenSourceDirectCallCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneRewrittenSourceCallArgumentCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneRewrittenNormalizedHomeCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneRewrittenNormalizedConstantCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneRewrittenInvokeCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneRewrittenInvokeInputCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneRewrittenNormalizedOperationCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneWriterStatus",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneWriterSectionCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneWriterSections",
                PrimitiveType.Int32,
                [.. new int[checked(writerSectionCapacity * 5)]], true),
            new HostBuffer("StandaloneWriterOutputByteCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneWriterOutputWords",
                PrimitiveType.Int32, [.. new int[writerWordCapacity]], true),
            new HostBuffer("StandaloneWriterStringCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneWriterStrings",
                PrimitiveType.Int32,
                [.. new int[checked(writerStringCapacity * 3)]], true),
            new HostBuffer("StandaloneWriterStringHashSlots",
                PrimitiveType.Int32,
                [.. new int[writerStringHashCapacity]], true),
            new HostBuffer("StandaloneWriterStringHashes",
                PrimitiveType.UInt32,
                [.. new int[writerStringCapacity]], true),
            new HostBuffer("StandaloneWriterLocalNameStringIds",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneWriterRootNameStringIds",
                PrimitiveType.Int32,
                [.. new int[MethodRoots.Length]], true),
            new HostBuffer("StandaloneWriterRootKeyStringIds",
                PrimitiveType.Int32,
                [.. new int[MethodRoots.Length]], true),
            new HostBuffer("StandaloneWriterRootModuleStringIds",
                PrimitiveType.Int32,
                [.. new int[MethodRoots.Length]], true),
            new HostBuffer("StandaloneWriterRootOrdinals",
                PrimitiveType.Int32,
                [.. new int[MethodRoots.Length]], true),
            new HostBuffer("StandaloneWriterRootExpectedGroupByImport",
                PrimitiveType.Int32,
                [.. new int[MethodRoots.Length]], true),
            new HostBuffer("StandaloneWriterRootRepresentativeImportByGroup",
                PrimitiveType.Int32,
                [.. new int[MethodRoots.Length]], true),
            new HostBuffer("StandaloneWriterRootExpectedMethodRowsByImport",
                PrimitiveType.Int32,
                [.. new int[MethodRoots.Length]], true),
            new HostBuffer("StandaloneWriterRootExpectedGroupCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneWriterRootGroupHashSlots",
                PrimitiveType.Int32,
                [.. new int[writerRootGroupHashCapacity]], true),
            new HostBuffer("StandaloneWriterMethodContractCounts",
                PrimitiveType.Int32,
                [.. new int[writerMethodCapacity]], true),
            new HostBuffer("StandaloneWriterPredicateGraphMapCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneWriterPredicateGraphMap",
                PrimitiveType.Int32,
                [.. new int[checked(writerContractFactCapacity * 10)]],
                true),
            new HostBuffer("StandaloneWriterTextCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneWriterText",
                PrimitiveType.Char16,
                [.. new int[writerTextCapacity]], true),

            new HostBuffer("StandaloneWriterModuleRefCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneWriterModuleRefByModuleId",
                PrimitiveType.Int32,
                [.. new int[moduleCapacity]], true),
            new HostBuffer("StandaloneWriterModuleRefStringIds",
                PrimitiveType.Int32,
                [.. new int[moduleCapacity]], true),
            new HostBuffer("StandaloneWriterModuleRefModuleIds",
                PrimitiveType.Int32,
                [.. new int[moduleCapacity]], true),
            new HostBuffer("StandaloneWriterModuleIdentityStringId",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneWriterTypeSpecCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneWriterTypeSpecOrder",
                PrimitiveType.Int32, [.. new int[typeCapacity]], true),
            new HostBuffer("StandaloneWriterTypeSpecRowByTypeId",
                PrimitiveType.Int32,
                [.. new int[checked(typeCapacity + 1)]], true),
            new HostBuffer("StandaloneWriterBodyPlans",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 20)]], true),
            new HostBuffer("StandaloneWriterBodyPlanCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneWriterTypeMapCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneWriterTypeMapTokens",
                PrimitiveType.UInt32,
                [.. new int[checked(tokenCapacity * 2)]], true),
            new HostBuffer("StandaloneWriterHomeTypeMapIndices",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 2)]], true),
            new HostBuffer("StandaloneWriterTypeMapIndexByTypeId",
                PrimitiveType.Int32,
                [.. new int[checked(typeCapacity + 1)]], true),
            new HostBuffer("StandaloneWriterMethodMapCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneWriterMethodMapTokens",
                PrimitiveType.UInt32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneWriterLocalMethodMapIndices",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneWriterImportedMethodMapIndices",
                PrimitiveType.Int32,
                [.. new int[MethodRoots.Length]], true),
            new HostBuffer("StandaloneWriterInvokeMethodMapIndices",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneWriterCallSiteRecordIndices",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneWriterConstantMapCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneWriterConstantMapEntries",
                PrimitiveType.UInt32,
                [.. new int[checked(tokenCapacity * 2)]], true),
            new HostBuffer("StandaloneWriterConstantMapIndices",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneWriterConstantMapIndexBySourceRow",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity + 1)]], true),
            new HostBuffer("StandaloneWriterConstantSourceRowByConstant",
                PrimitiveType.Int32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneWriterUniqueConstantCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneWriterUniqueConstants",
                PrimitiveType.Int32,
                [.. new int[checked(tokenCapacity * 5)]], true),
            new HostBuffer("StandaloneWriterUniqueConstantTypeSpecRefs",
                PrimitiveType.UInt32,
                [.. new int[tokenCapacity]], true),
            new HostBuffer("StandaloneWriterConstantHashSlots",
                PrimitiveType.Int32,
                [.. new int[writerConstantHashCapacity]], true),
            new HostBuffer("StandaloneWriterBlobCount",
                PrimitiveType.Int32, [0], true),
            new HostBuffer("StandaloneWriterBlobs",
                PrimitiveType.UInt32,
                [.. new int[checked(tokenCapacity * 4)]], true),
            new HostBuffer("StandaloneWriterBlobHashSlots",
                PrimitiveType.Int32,
                [.. new int[writerConstantHashCapacity]], true),
        ]);
    }
}

internal static class StandaloneMethodImportOutputFramer
{
    internal const string
        GeneralPredicateGraphsWitnessProjectionsSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-witness-projections-1";

    internal static bool
        IsSelectedGeneralPredicateGraphsWitnessProjectionsSchema(
            string? schemaIdentity)
        => StringComparer.Ordinal.Equals(schemaIdentity,
            GeneralPredicateGraphsWitnessProjectionsSchemaIdentity);

    internal static byte[] Frame(
        NativeExecutionResult execution,
        ImmutableArray<HostBuffer> buffers)
    {
        ArgumentNullException.ThrowIfNull(execution);
        if (execution.ExitCode != 0
            || execution.BufferOffsets.Length != buffers.Length)
        {
            throw new InvalidOperationException(
                "Standalone output framing requires a successful execution snapshot.");
        }

        var sectionCountBuffer = FindBuffer(buffers,
            "StandaloneWriterSectionCount");
        var writerStatusBuffer = FindBuffer(buffers,
            "StandaloneWriterStatus");
        var sectionRowsBuffer = FindBuffer(buffers,
            "StandaloneWriterSections");
        var byteCountBuffer = FindBuffer(buffers,
            "StandaloneWriterOutputByteCount");
        var outputWordsBuffer = FindBuffer(buffers,
            "StandaloneWriterOutputWords");
        if (buffers[sectionRowsBuffer].Values.Length % 5 != 0)
        {
            throw new InvalidOperationException(
                "The standalone output section inventory has an invalid shape.");
        }

        if (ReadBufferInt32(execution, writerStatusBuffer, 0) != 0)
        {
            throw new InvalidOperationException(
                "The standalone TRRM writer did not complete successfully.");
        }

        var sectionCount = ReadBufferInt32(execution, sectionCountBuffer, 0);
        var sectionCapacity = buffers[sectionRowsBuffer].Values.Length / 5;
        var outputByteCount = ReadBufferInt32(execution, byteCountBuffer, 0);
        var outputWordCapacity = buffers[outputWordsBuffer].Values.Length;
        if (sectionCount <= 0 || sectionCount > sectionCapacity
            || outputByteCount < 0
            || outputByteCount > (long)outputWordCapacity * sizeof(int))
        {
            throw new InvalidOperationException(
                "The standalone output section inventory exceeds its captured buffers.");
        }

        var sections = new TrRmSection[sectionCount];
        var previousKind = 0;
        var previousByteEnd = 0;
        for (var index = 0; index < sectionCount; index++)
        {
            var row = index * 5;
            var kind = ReadBufferInt32(execution, sectionRowsBuffer, row);
            var flags = ReadBufferInt32(execution, sectionRowsBuffer, row + 1);
            var recordCount = ReadBufferInt32(execution, sectionRowsBuffer,
                row + 2);
            var byteOffset = ReadBufferInt32(execution, sectionRowsBuffer,
                row + 3);
            var byteLength = ReadBufferInt32(execution, sectionRowsBuffer,
                row + 4);
            if (kind <= previousKind || kind > ushort.MaxValue
                || flags is < 0 or > ushort.MaxValue
                || recordCount <= 0 || recordCount > 0xFF_FFFF
                || byteOffset < 0 || byteLength <= 0
                || byteOffset != previousByteEnd
                || byteOffset > outputByteCount
                || byteLength > outputByteCount - byteOffset)
            {
                throw new InvalidOperationException(
                    "The standalone output contains an invalid section extent.");
            }

            var payload = new byte[byteLength];
            for (var byteIndex = 0; byteIndex < payload.Length; byteIndex++)
            {
                var outputByteIndex = byteOffset + byteIndex;
                var packedWord = ReadBufferInt32(execution,
                    outputWordsBuffer, outputByteIndex / sizeof(int));
                var shift = (outputByteIndex % sizeof(int)) * 8;
                payload[byteIndex] = unchecked((byte)(
                    (uint)packedWord >> shift));
            }

            sections[index] = new TrRmSection((ushort)kind,
                (ushort)flags, (uint)recordCount, payload);
            previousKind = kind;
            previousByteEnd = byteOffset + byteLength;
        }
        if (previousByteEnd != outputByteCount)
        {
            throw new InvalidOperationException(
                "The standalone output contains unclaimed section bytes.");
        }

        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.GeneralPredicateGraphsDraft,
            standardSchemaIdentity:
                GeneralPredicateGraphsWitnessProjectionsSchemaIdentity);
    }

    private static int FindBuffer(ImmutableArray<HostBuffer> buffers,
        string name)
    {
        var found = -1;
        for (var index = 0; index < buffers.Length; index++)
        {
            if (!StringComparer.Ordinal.Equals(buffers[index].Name, name))
            {
                continue;
            }
            if (found >= 0)
            {
                throw new InvalidOperationException(
                    $"The standalone output buffer '{name}' is duplicated.");
            }

            found = index;
        }

        return found >= 0 ? found
            : throw new InvalidOperationException(
                $"The standalone output buffer '{name}' is missing.");
    }

    private static int ReadBufferInt32(NativeExecutionResult execution,
        int bufferIndex, int elementIndex)
    {
        var offset = execution.BufferOffsets[bufferIndex];
        var byteOffset = checked(offset + (elementIndex * sizeof(int)));
        if (offset < 0 || byteOffset < offset
            || byteOffset > execution.Data.Length - sizeof(int))
        {
            throw new InvalidOperationException(
                "The standalone output buffer exceeds the execution snapshot.");
        }

        return System.Buffers.Binary.BinaryPrimitives
            .ReadInt32LittleEndian(execution.Data.AsSpan(byteOffset,
                sizeof(int)));
    }
}
