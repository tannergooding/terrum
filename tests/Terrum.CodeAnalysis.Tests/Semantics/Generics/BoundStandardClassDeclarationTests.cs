// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics.Generics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class BoundStandardClassDeclarationTests
{
    private const string C1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1";
    private const string C1M1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1";

    [Test]
    public void MetadataClassCarrierKeepsC1MutabilityUnknownAndChecksC1M1Exactly()
    {
        var (c1Declaration, c1Types) = CreateDeclaration(
            C1SchemaIdentity, 0x0A, expectedClassFlags: 0x0A);
        Assert.That(BoundStandardClassDeclarationContract.IsValidTable(
            [c1Declaration], c1Types), Is.True);
        Assert.That(c1Declaration.Declaration.TryGetDeclaredMutability(
            out _), Is.False);
        var metadataReference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            genericNominalTypeDefinitions: c1Types,
            standardClassDeclarations: [c1Declaration]);
        Assert.That(metadataReference.Classes, Is.Empty);
        Assert.That(metadataReference.StandardClassDeclarations,
            Has.Length.EqualTo(1));
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 0; }"), metadataReference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        Assert.That(program.Classes, Is.Empty);
        Assert.That(program.StandardClassDeclarations,
            Has.Length.EqualTo(1));

        var (mutableDeclaration, mutableTypes) = CreateDeclaration(
            C1M1SchemaIdentity, 0x8A, expectedClassFlags: 0x8A);
        Assert.That(BoundStandardClassDeclarationContract.IsValidTable(
            [mutableDeclaration], mutableTypes), Is.True);
        Assert.That(mutableDeclaration.Declaration
            .TryGetDeclaredMutability(out var isMutable), Is.True);
        Assert.That(isMutable, Is.True);

        var (mismatchedDeclaration, mismatchedTypes) = CreateDeclaration(
            C1M1SchemaIdentity, 0x8A, expectedClassFlags: 0x0A);
        Assert.That(BoundStandardClassDeclarationContract.IsValidTable(
            [mismatchedDeclaration], mismatchedTypes), Is.False);
    }

    [Test]
    public void StandardClassDeclarationTraversalVisitsAndRewritesExpectedViews()
    {
        var (declaration, _) = CreateDeclaration(C1M1SchemaIdentity,
            0x8A, expectedClassFlags: 0x8A);
        var visited = new List<string>();
        Assert.That(BoundTreeTraversal
            .VisitStandardClassDeclarationTypeSpecs([declaration],
                (provider, _) => visited.Add(provider.ModuleIdentity)), Is.True);
        Assert.That(visited, Is.EqualTo(["Provider", "Consumer"]));

        var rewritten = BoundTreeTraversal.RewriteStandardClassDeclarations(
            [declaration],
            static view => view with {
                Fields = [.. view.Fields.Select(field => field with {
                    TypeSpecReference = 0xF200_000D,
                })],
            },
            static expectation => expectation);
        Assert.That(rewritten[0].Declaration.Fields.Single()
            .TypeSpecReference, Is.EqualTo(0xF200_000D));
        Assert.That(rewritten[0].Expectations.Single()
            .ExpectedDeclaration.Fields.Single().TypeSpecReference,
            Is.EqualTo(0xF200_000D));
    }

    private static (BoundStandardClassDeclaration Declaration,
        ImmutableArray<BoundGenericNominalTypeDefinition> Types)
        CreateDeclaration(string schemaIdentity, uint actualClassFlags,
            uint expectedClassFlags)
    {
        var actualIdentity = new BoundGenericNominalTypeIdentity(
            "ManifestProvider", "Provider", 0x1300_0001);
        var expectedIdentity = new BoundGenericNominalTypeIdentity(
            "ManifestConsumer", "Consumer", 0x1300_0001);
        var actualProvider = Snapshot(schemaIdentity, "Provider", 1);
        var expectedProvider = Snapshot(schemaIdentity, "Consumer", 2);
        var actualView = View(actualIdentity, actualClassFlags,
            actualProvider);
        var expectedView = View(expectedIdentity, expectedClassFlags,
            expectedProvider);
        var binding = new BoundSourceTypeExpectation(expectedProvider, 1,
            expectedIdentity.DefinitionId, "ManifestProvider", "ClassKey",
            1, 0, actualProvider, actualIdentity.DefinitionId);
        var declaration = new BoundStandardClassDeclaration(actualView,
            ["ClassKey"], [
                new BoundStandardClassExpectation(binding, expectedView),
            ]);
        var definitions = ImmutableArray.Create(
            new BoundGenericNominalTypeDefinition(actualIdentity, "Demo",
                "Widget",
                2, [], 0, 0, 0, 0),
            new BoundGenericNominalTypeDefinition(expectedIdentity, "Demo",
                "Widget",
                2, [], 0, 0, 0, 0));
        return (declaration, definitions);
    }

    private static BoundSourceTypeProviderSnapshot Snapshot(
        string schemaIdentity, string moduleIdentity, byte marker)
        => new(schemaIdentity, [marker], 1, moduleIdentity, 1, 1, 0, [], []);

    private static BoundStandardClassTypeView View(
        BoundGenericNominalTypeIdentity identity, uint flags,
        BoundSourceTypeProviderSnapshot provider)
        => new(identity, "Demo", "Widget", flags, [
            new BoundStandardClassFieldDeclaration(0x1400_0001,
                identity.DefinitionId, "Value", 0, 0xF200_0008),
        ], [
            new BoundStandardClassLayoutVariant(1,
                identity.DefinitionId, 0, 0, 0, 1, 0, 0, 1,
                [0x1400_0001]),
        ], provider);
}
