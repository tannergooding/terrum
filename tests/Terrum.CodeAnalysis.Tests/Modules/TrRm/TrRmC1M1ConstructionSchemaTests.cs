// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmC1M1ConstructionSchemaTests
{
    private const string SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1";
    private const string C1M1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1";
    private const string Construction2SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2";

    [Test]
    public void EmitsAndStructurallyValidatesOnlyUnderTheExactSuccessorIdentity()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Identity(int32 value) { return value; }"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var artifact = TrRmExecutableCompiler.EmitStandard(program, "Module",
            SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], []);

        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                new TrRmArtifactIndex(artifact,
                    TrRmArtifactSchema.C1M1ConstructionDraft));
        Assert.That(tables.Contracts.Schema,
            Is.EqualTo(TrRmArtifactSchema.C1M1ConstructionDraft));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, "Module",
                SchemaIdentity,
                TrRmModuleTableReader
                    .WindowsX64NativeTargetRequirement));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, "Module",
                C1M1SchemaIdentity,
                TrRmModuleTableReader
                    .WindowsX64NativeTargetRequirement,
                [], [], []));
    }

    [Test]
    public void Construction2HasAnExactSchemaAndFeatureSelection()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Identity(int32 value) { return value; }"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var artifact = TrRmExecutableCompiler.EmitStandard(program, "Module",
            Construction2SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement, []);

        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                new TrRmArtifactIndex(artifact,
                    TrRmArtifactSchema.C1M1Construction2Draft));
        Assert.That(tables.Contracts.Schema,
            Is.EqualTo(TrRmArtifactSchema.C1M1Construction2Draft));
        Assert.That(TrRmFeatureTableReader.TryGetRuleRevision(25,
            TrRmArtifactSchema.C1M1Construction2Draft, out var revision),
            Is.True);
        Assert.That(revision, Is.EqualTo(1));
        Assert.That(TrRmFeatureTableReader.TryGetRuleRevision(25,
            TrRmArtifactSchema.C1M1ConstructionDraft, out _), Is.False);
        var featureBytes = TrRmFeatureTableWriter.Encode(
            [TrRmFeatureRow.ClassConstructionBodiesCandidate],
            TrRmArtifactSchema.C1M1Construction2Draft);
        var features = new TrRmFeatureTableReader(featureBytes, 1,
            TrRmArtifactSchema.C1M1Construction2Draft);
        Assert.That(features.GetFeature(1),
            Is.EqualTo(TrRmFeatureRow.ClassConstructionBodiesCandidate));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(Construction2SchemaIdentity,
                artifact), "Module", []);
        Assert.That(loaded.Functions.Single().Name, Is.EqualTo("Identity"));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(Construction2SchemaIdentity,
                    artifact), "Module",
                [new TrRmStandardArtifactInput(C1M1SchemaIdentity,
                    ReadOnlyMemory<byte>.Empty)]));
    }
}
