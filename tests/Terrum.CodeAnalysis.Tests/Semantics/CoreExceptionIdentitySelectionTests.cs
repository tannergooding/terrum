// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using NUnit.Framework;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class CoreExceptionIdentitySelectionTests
{
    [Test]
    public void CompilationReferencePreservesCoreExceptionIdentitySelections()
    {
        var selection = new CoreExceptionIdentitySelection(
            "consumer", "core", "exception-interface",
            "core", "unreachable-payload");
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            coreExceptionIdentitySelections: [selection]);

        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public int32 Value() { return 1; }"),
            reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
        Assert.That(compilation.Program!.CoreExceptionIdentitySelections,
            Is.EqualTo([selection]));
    }

    [Test]
    public void CompilationOptionsCarryCoreExceptionIdentitySelections()
    {
        var selection = new CoreExceptionIdentitySelection(
            "consumer", "core", "exception-interface",
            "core", "unreachable-payload");
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public int32 Value() { return 1; }"),
            new CompilationOptions {
                CoreExceptionIdentitySelections = [selection],
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
        Assert.That(compilation.Program!.CoreExceptionIdentitySelections,
            Is.EqualTo([selection]));
    }

    [Test]
    public void CompilationRejectsConflictingReferenceAndOptionSelections()
    {
        var referenceSelection = new CoreExceptionIdentitySelection(
            "consumer", "core", "exception-interface",
            "core", "unreachable-payload");
        var optionSelection = referenceSelection with {
            UnreachableExportKey = "other-unreachable",
        };
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            coreExceptionIdentitySelections: [referenceSelection]);

        _ = Assert.Throws<ArgumentException>(() =>
            Compilation.CreateLibrary(
                SyntaxTree.Parse("public int32 Value() { return 1; }"),
                reference,
                new CompilationOptions {
                    CoreExceptionIdentitySelections = [optionSelection],
                }));
    }

    [Test]
    public void CompilationReferenceRejectsDuplicateUsingModuleSelections()
    {
        var selection = new CoreExceptionIdentitySelection(
            "consumer", "core", "exception-interface",
            "core", "unreachable-payload");

        _ = Assert.Throws<ArgumentException>(() =>
            new CompilationReference([], [], BoundRuntimePolicy.FailFast,
                coreExceptionIdentitySelections: [selection, selection]));
    }

    [Test]
    public void CompilationReferenceRejectsMalformedCoreExceptionIdentityText()
    {
        var selection = new CoreExceptionIdentitySelection(
            "consumer", "core", "exception-interface\uD800",
            "core", "unreachable-payload");

        _ = Assert.Throws<ArgumentException>(() =>
            new CompilationReference([], [], BoundRuntimePolicy.FailFast,
                coreExceptionIdentitySelections: [selection]));
    }
}
