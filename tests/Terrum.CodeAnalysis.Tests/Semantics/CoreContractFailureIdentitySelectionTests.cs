using System;
using System.Collections.Immutable;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class CoreContractFailureIdentitySelectionTests
{
    [Test]
    public void SelectionAcceptsExactModuleScopedRoles()
    {
        var selection = CreateSelection();

        Assert.That(CoreContractFailureIdentitySelectionContract.IsValidTable(
            [selection]), Is.True);
    }

    [Test]
    public void SelectionAllowsOnlyRolesNeededByTheModule()
    {
        var requirementOnly = CreateSelection() with {
            PostconditionExceptionClass = null,
            RequireExceptionSchema = null,
        };
        var schemaOnly = CreateSelection() with {
            RequirementExceptionClass = null,
            PostconditionExceptionClass = null,
        };

        Assert.That(CoreContractFailureIdentitySelectionContract.IsValidTable(
            [requirementOnly, schemaOnly with {
                UsingModuleIdentity = "sample.other",
            }]), Is.True);
    }

    [Test]
    public void SelectionRejectsEmptyDuplicateOrAliasedRoles()
    {
        var empty = new CoreContractFailureIdentitySelection(
            "sample.consumer", null, null, null);
        var duplicateModule = CreateSelection() with {
            RequirementExceptionClass = null,
            PostconditionExceptionClass = null,
        };
        var aliasedClasses = CreateSelection() with {
            PostconditionExceptionClass
                = CreateSelection().RequirementExceptionClass,
        };
        var repeatedExport = CreateSelection() with {
            RequireExceptionSchema
                = CreateSelection().RequirementExceptionClass,
        };
        var malformed = CreateSelection() with {
            RequirementExceptionClass =
                new CoreContractFailureDeclarationIdentity(
                    "core\u0000provider", "requirement"),
        };

        Assert.That(CoreContractFailureIdentitySelectionContract.IsValidTable(
            [empty]), Is.False);
        Assert.That(CoreContractFailureIdentitySelectionContract.IsValidTable(
            [CreateSelection(), duplicateModule]), Is.False);
        Assert.That(CoreContractFailureIdentitySelectionContract.IsValidTable(
            [aliasedClasses]), Is.False);
        Assert.That(CoreContractFailureIdentitySelectionContract.IsValidTable(
            [repeatedExport]), Is.False);
        Assert.That(CoreContractFailureIdentitySelectionContract.IsValidTable(
            [malformed]), Is.False);
    }

    [Test]
    public void CompilationOptionsAndReferencesCarrySelections()
    {
        var selection = CreateSelection();
        var options = new CompilationOptions {
            CoreContractFailureIdentitySelection = selection,
        };
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Identity(int32 value) { return value; }"), options);

        Assert.That(library.HasErrors, Is.False,
            string.Join(Environment.NewLine, library.Diagnostics));
        Assert.That(library.Program!.CoreContractFailureIdentitySelections,
            Is.EqualTo([selection]));
        Assert.That(library.Program.CoreContractFailureIdentitySelection,
            Is.EqualTo(selection));

        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            coreContractFailureIdentitySelections: [selection]);
        var consumer = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 0; }"), reference);

        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        Assert.That(consumer.Program!.CoreContractFailureIdentitySelections,
            Is.EqualTo([selection]));
        Assert.That(consumer.Program.CoreContractFailureIdentitySelection,
            Is.Null);
    }

    [Test]
    public void CompilationRejectsDuplicateReferenceAndLocalSelection()
    {
        var selection = CreateSelection();
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            coreContractFailureIdentitySelections: [selection]);

        Assert.That(() => Compilation.CreateLibrary(SyntaxTree.Parse(
                "public int32 Identity(int32 value) { return value; }"),
            reference, new CompilationOptions {
                CoreContractFailureIdentitySelection = selection,
            }), Throws.ArgumentException);
    }

    [Test]
    public void DeclarationClausesBindOrderedPredicatesAndFailureSelections()
    {
        const string source = """
            public int32 Identity(int32 value)
                require<Failure> value >= int32(0)
                ensure return == value
            {
                return value;
            }
            public class Failure {
                public int32 Value;
                public Failure() { Value = 0; }
            }
            """;
        var selection = CreateSelection();
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection = selection,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = compilation.Program!.Functions.Single(
            static function => function.Name == "Identity");
        Assert.That(function.DeclarationContractClauses, Has.Length.EqualTo(2));
        Assert.That(function.DeclarationContractClauses.Select(static clause =>
                clause.Phase),
            Is.EqualTo([
                BoundDeclarationContractPhase.Precondition,
                BoundDeclarationContractPhase.NormalPostcondition,
            ]));
        Assert.That(function.DeclarationContractClauses.Select(
                static clause => clause.PhaseLocalOrdinal),
            Is.EqualTo([0u, 0u]));
        Assert.That(function.DeclarationContractClauses[0].ExplicitFailureType,
            Is.Not.Null);
        Assert.That(function.DeclarationContractClauses[0].SelectedFailureClass,
            Is.Null);
        Assert.That(function.DeclarationContractClauses[1].SelectedFailureClass,
            Is.EqualTo(selection.PostconditionExceptionClass));
        var hasSavedReturn = false;
        Assert.That(BoundTreeTraversal.VisitExpressionTree(
            function.DeclarationContractClauses[1].Predicate,
            expression => hasSavedReturn |=
                expression is BoundContractReturnValue), Is.True);
        Assert.That(hasSavedReturn, Is.True);
        var remapped = BoundTreeTraversal.RewriteTypes(function,
            static type => type);
        Assert.That(remapped.DeclarationContractClauses,
            Is.EqualTo(function.DeclarationContractClauses));
        Assert.That(() => NativeEmitter.EmitLibrary(compilation.Program,
                ["Identity"], NativeRuntimePolicy.FailFast),
            Throws.TypeOf<NotSupportedException>());
        var moduleException = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(compilation.Program, ["Identity"]));
        Assert.That(moduleException!.Message,
            Does.Contain("declaration contract clauses"));
        var trrmException = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(compilation.Program,
                "sample.contracts", "trrm/1-draft-layout-queries-1", 1));
        Assert.That(trrmException!.Message,
            Does.Contain("declaration contract clauses"));
    }

    [Test]
    public void DefaultRequirementUsesOnlyTheSelectedCoreFailureIdentity()
    {
        const string source = """
            public void Validate() require true {}
            """;
        var selection = CreateSelection() with {
            PostconditionExceptionClass = null,
            RequireExceptionSchema = null,
        };
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                CoreContractFailureIdentitySelection = selection,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var clause = compilation.Program!.Functions.Single(
            static function => function.Name == "Validate")
            .DeclarationContractClauses.Single();
        Assert.That(clause.SelectedFailureClass,
            Is.EqualTo(selection.RequirementExceptionClass));
        Assert.That(clause.UsingModuleIdentity,
            Is.EqualTo(selection.UsingModuleIdentity));
    }

    [Test]
    public void DefaultRequirementRejectsMissingCoreIdentitySelection()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public void Validate() require true {}
            """));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Message.Contains(
                "selected Core RequirementException identity",
                StringComparison.Ordinal)), Is.True);
    }

    private static CoreContractFailureIdentitySelection CreateSelection()
        => new("sample.consumer",
            new CoreContractFailureDeclarationIdentity("core.contracts",
                "requirement-exception"),
            new CoreContractFailureDeclarationIdentity("core.contracts",
                "postcondition-exception"),
            new CoreContractFailureDeclarationIdentity("core.contracts",
                "require-exception-schema"));
}
