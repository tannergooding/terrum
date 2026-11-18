// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmSourceClassExportTests
{
    private const string C1M1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1";

    [Test]
    public void CapturesExactPublicClassMembersAndConstructorProjectionKeys()
    {
        var (program, _, declaration, members) = CreateProgram();
        var classExport = new TrRmSourceClassExport(program, 0,
            declaration.Span, "BoxKey", [
                new TrRmSourceClassMemberExport(0,
                    Member(members, SyntaxKind.FieldDeclaration).Span,
                    "Box.Value"),
                new TrRmSourceClassMemberExport(0,
                    Member(members, SyntaxKind.ConstructorDeclaration).Span,
                    "Box.Initialize") {
                    ConstructionProjectionExportKey = "Box.Create",
                },
                new TrRmSourceClassMemberExport(0,
                    Member(members, SyntaxKind.FunctionDeclaration).Span,
                    "Box.Read"),
            ]);

        var captured = TrRmExecutableCompiler.SourceClassExports.Capture(
            program, "Module", [], [], [classExport]);

        Assert.That(captured.Exports, Has.Length.EqualTo(1));
        Assert.That(captured.Exports[0].Declaration.Span,
            Is.EqualTo(declaration.Span));
        Assert.That(captured.Exports[0].ExportKey, Is.EqualTo("BoxKey"));
        Assert.That(captured.Exports[0].MemberExports.Select(
                static member => member.Kind),
            Is.EqualTo([
                TrRmExecutableCompiler.SourceClassMemberKind.Field,
                TrRmExecutableCompiler.SourceClassMemberKind.Constructor,
                TrRmExecutableCompiler.SourceClassMemberKind.Method,
            ]));
        Assert.That(captured.Exports[0].MemberExports[1]
            .ConstructionProjectionExportKey, Is.EqualTo("Box.Create"));
    }

    [Test]
    public void C1M1RefusesConstructorAndMethodExportKeys()
    {
        var (program, _, declaration, members) = CreateProgram();
        var classExport = new TrRmSourceClassExport(program, 0,
            declaration.Span, "BoxKey", [
                new TrRmSourceClassMemberExport(0,
                    Member(members, SyntaxKind.FieldDeclaration).Span,
                    "Box.Value"),
                new TrRmSourceClassMemberExport(0,
                    Member(members, SyntaxKind.ConstructorDeclaration).Span,
                    "Box.Initialize"),
                new TrRmSourceClassMemberExport(0,
                    Member(members, SyntaxKind.FunctionDeclaration).Span,
                    "Box.Read"),
            ]);

        var exception = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, "Module",
                C1M1SchemaIdentity, 1, [], [], [classExport]));

        Assert.That(exception!.Message,
            Does.Contain("exact construction successor"));
    }

    [Test]
    public void RejectsCopiedProgramsAndUnmappedPublicMembers()
    {
        var (program, _, declaration, members) = CreateProgram();
        var constructor = Member(members,
            SyntaxKind.ConstructorDeclaration);
        var copiedProgram = program with {
            Functions = [.. program.Functions],
        };
        var copiedClassExport = new TrRmSourceClassExport(copiedProgram, 0,
            declaration.Span, "BoxKey", [
                new TrRmSourceClassMemberExport(0, constructor.Span,
                    "Box.Initialize"),
            ]);
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.SourceClassExports.Capture(
                copiedProgram, "Module", [], [], [copiedClassExport]));

        var incompleteClassExport = new TrRmSourceClassExport(program, 0,
            declaration.Span, "BoxKey", [
                new TrRmSourceClassMemberExport(0, constructor.Span,
                    "Box.Initialize"),
            ]);
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.SourceClassExports.Capture(
                program, "Module", [], [], [incompleteClassExport],
                requirePublicCallableKeys: true));
    }

    [Test]
    public void CapturesGetterOnlyPropertyThroughItsExactPropertyDeclaration()
    {
        var tree = SyntaxTree.Parse("""
            public class Box {
                private int32 _value;
                public Box(int32 value) { _value = value; }
                public int32 Value { get { return _value; } }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var declaration = tree.Root.Children.OfType<SyntaxNode>().Single(
            static node => node.Kind == SyntaxKind.ClassDeclaration);
        var members = declaration.Children.OfType<SyntaxNode>().ToImmutableArray();
        var classExport = new TrRmSourceClassExport(program, 0,
            declaration.Span, "BoxKey", [
                new TrRmSourceClassMemberExport(0,
                    Member(members,
                        SyntaxKind.ConstructorDeclaration).Span,
                    "Box.Initialize"),
                new TrRmSourceClassMemberExport(0,
                    Member(members, SyntaxKind.PropertyDeclaration).Span,
                    "Box.Value"),
            ]);

        var captured = TrRmExecutableCompiler.SourceClassExports.Capture(
            program, "Module", [], [], [classExport]);

        Assert.That(captured.Exports[0].MemberExports[1].Kind,
            Is.EqualTo(
                TrRmExecutableCompiler.SourceClassMemberKind.Property));
    }

    [Test]
    public void PartialConstructorMapsItsPrimaryAndRetainsItsImplementation()
    {
        var tree = SyntaxTree.Parse("""
                public class Box {
                    public int32 Value;
                    public partial Box(int32 value);
                    partial Box(int32 value) { Value = value; }
                }
                """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var declaration = tree.Root.Children.OfType<SyntaxNode>().Single(
                static node => node.Kind == SyntaxKind.ClassDeclaration);
        var members = declaration.Children.OfType<SyntaxNode>().ToImmutableArray();
        var constructors = members.Where(member =>
                member.Kind == SyntaxKind.ConstructorDeclaration).ToArray();
        var primary = constructors.Single(member =>
                member.Children.OfType<SyntaxNode>().Any(
                    static child => child.Kind == SyntaxKind.ExternalBody));
        var secondary = members.Single(member =>
                member.Kind == SyntaxKind.ConstructorDeclaration
                && member.Span != primary.Span);
        var initializer = program.Functions.Single(function =>
                function.IsClassConstructor);
        Assert.That(initializer.IsPartialConstructor, Is.True);
        Assert.That(initializer.SourceInputOrdinal, Is.EqualTo(0));
        Assert.That(initializer.SourceDeclarationInputOrdinal,
                Is.EqualTo(0));
        Assert.That(initializer.SourceDeclarationSpan, Is.EqualTo(primary.Span));
        Assert.That(initializer.Span, Is.EqualTo(secondary.Span));

        var mappedPrimary = new TrRmSourceClassExport(program, 0,
                declaration.Span, "BoxKey", [
                    new TrRmSourceClassMemberExport(0, primary.Span,
                        "Box.Initialize"),
                    new TrRmSourceClassMemberExport(0,
                        Member(members, SyntaxKind.FieldDeclaration).Span,
                        "Box.Value"),
                ]);
        var captured = TrRmExecutableCompiler.SourceClassExports.Capture(
                program, "Module", [], [], [mappedPrimary]);
        Assert.That(captured.Exports[0].MemberExports, Has.Length.EqualTo(2));

        var mappedSecondary = mappedPrimary with {
                MemberExports = [
                    new TrRmSourceClassMemberExport(0, secondary.Span,
                        "Box.Initialize"),
                    mappedPrimary.MemberExports[1],
                ],
        };
        _ = Assert.Throws<ArgumentException>(() =>
                TrRmExecutableCompiler.SourceClassExports.Capture(
                    program, "Module", [], [], [mappedSecondary]));
    }

    [Test]
    public void PartialConstructorRejectsMissingOrMismatchedImplementation()
    {
        var declarations = new[] {
            "public partial Box(int32 value);",
            "public partial Box(int32 value); partial Box(int32 value);",
            "public partial Box(int32 value); public partial Box(int32 value) { Value = value; }",
            "public partial Box(int32 value); partial Box(uint32 value) { Value = 0; }",
        };
        foreach (var constructors in declarations)
        {
            var source = $"public class Box {{ public int32 Value; {constructors} }}";
            var compilation = Compilation.CreateLibrary(
                    SyntaxTree.Parse(source));
            Assert.That(compilation.HasErrors, Is.True, source);
            Assert.That(compilation.Diagnostics.Any(
                        static diagnostic => diagnostic.Code == "TRM3011"),
                    Is.True, source);
        }
    }

    [TestCase("""
        public partial class Box {
            public int32 Value;
            public Box(int32 value) { Value = value; }
        }
        partial class Box {
            public int32 Read() { return Value; }
        }
        """)]
    [TestCase("""
        partial class Box {
            public int32 Read() { return Value; }
        }
        public partial class Box {
            public int32 Value;
            public Box(int32 value) { Value = value; }
        }
        """)]
    public void CapturesPartialClassMembersWithVisibilitySelectedPrimary(
        string source)
    {
        var tree = SyntaxTree.Parse(source);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var parts = tree.Root.Children.OfType<SyntaxNode>()
            .Where(static node => node.Kind == SyntaxKind.ClassDeclaration)
            .ToArray();
        var primary = parts.Single(part =>
            part.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Kind == TokenKind.PublicKeyword));
        var memberExports = parts
            .SelectMany(part => part.Children.OfType<SyntaxNode>())
            .Where(static node => node.Kind is SyntaxKind.FieldDeclaration
                or SyntaxKind.ConstructorDeclaration
                or SyntaxKind.FunctionDeclaration)
            .Select(node => new TrRmSourceClassMemberExport(0, node.Span,
                node.Kind switch {
                    SyntaxKind.FieldDeclaration => "Box.Value",
                    SyntaxKind.ConstructorDeclaration => "Box.Initialize",
                    _ => "Box.Read",
                }))
            .ToImmutableArray();
        var classExport = new TrRmSourceClassExport(program, 0,
            primary.Span, "BoxKey", memberExports);

        var captured = TrRmExecutableCompiler.SourceClassExports.Capture(
            program, "Module", [], [], [classExport]);

        Assert.That(captured.Exports[0].Declaration.Declaration,
            Is.SameAs(primary));
        Assert.That(captured.Exports[0].MemberExports, Has.Length.EqualTo(3));
    }

    [Test]
    public void CapturesMembersFromOtherSourceInputsInThePartialClass()
    {
        var secondary = SyntaxTree.Parse("""
            partial class Box {
                public int32 Read() { return Value; }
            }
            """);
        var primary = SyntaxTree.Parse("""
            public partial class Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
            }
            """);
        var compilation = Compilation.CreateLibrary(
            [secondary, primary]);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var primaryDeclaration = primary.Root.Children
            .OfType<SyntaxNode>().Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var field = Member(primaryDeclaration.Children
            .OfType<SyntaxNode>().ToImmutableArray(),
            SyntaxKind.FieldDeclaration);
        var constructor = Member(primaryDeclaration.Children
            .OfType<SyntaxNode>().ToImmutableArray(),
            SyntaxKind.ConstructorDeclaration);
        var secondaryDeclaration = secondary.Root.Children
            .OfType<SyntaxNode>().Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var method = Member(secondaryDeclaration.Children
            .OfType<SyntaxNode>().ToImmutableArray(),
            SyntaxKind.FunctionDeclaration);
        var classExport = new TrRmSourceClassExport(program, 1,
            primaryDeclaration.Span, "BoxKey", [
                new TrRmSourceClassMemberExport(1, field.Span, "Box.Value"),
                new TrRmSourceClassMemberExport(1, constructor.Span,
                    "Box.Initialize"),
                new TrRmSourceClassMemberExport(0, method.Span, "Box.Read"),
            ]);

        var captured = TrRmExecutableCompiler.SourceClassExports.Capture(
            program, "Module", [], [], [classExport]);

        Assert.That(captured.Exports[0].Declaration.SourceInputOrdinal,
            Is.EqualTo(1));
        Assert.That(captured.Exports[0].MemberExports[2].Declaration.Span,
            Is.EqualTo(method.Span));
    }

    [Test]
    public void PartialClassRequiresOnePrimaryAndMatchingRepeatedModifiers()
    {
        var partialParts = new[] {
            """
            public partial class Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
            }
            public partial class Box {
                public int32 Read() { return Value; }
            }
            """,
            """
            public class Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
            }
            partial class Box {
                public int32 Read() { return Value; }
            }
            """,
            """
            partial class Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
            }
            partial class Box {
                public int32 Read() { return Value; }
            }
            """,
            """
            public partial class Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
            }
            partial mut class Box {
                public int32 Read() { return Value; }
            }
            """,
        };
        foreach (var source in partialParts)
        {
            var compilation = Compilation.CreateLibrary(
                SyntaxTree.Parse(source));
            Assert.That(compilation.HasErrors, Is.True, source);
            Assert.That(compilation.Diagnostics.Any(
                    static diagnostic => diagnostic.Code == "TRM3003"),
                Is.True, source);
        }
    }

    [Test]
    public void RejectsPrivateMemberAndModuleWideDuplicateKeys()
    {
        const string source = """
            public class Box {
                private int32 _value;
                public Box(int32 value) { _value = value; }
            }
            """;
        var tree = SyntaxTree.Parse(source);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var declaration = tree.Root.Children.OfType<SyntaxNode>().Single(
            static node => node.Kind == SyntaxKind.ClassDeclaration);
        var field = Member(declaration.Children.OfType<SyntaxNode>().ToImmutableArray(),
            SyntaxKind.FieldDeclaration);
        var constructor = Member(
            declaration.Children.OfType<SyntaxNode>().ToImmutableArray(),
            SyntaxKind.ConstructorDeclaration);

        var privateFieldClassExport = new TrRmSourceClassExport(program, 0,
            declaration.Span, "BoxKey", [
                new TrRmSourceClassMemberExport(0, field.Span, "Box.Value"),
                new TrRmSourceClassMemberExport(0, constructor.Span,
                    "Box.Initialize"),
            ]);
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.SourceClassExports.Capture(
                program, "Module", [], [], [privateFieldClassExport]));

        var duplicateKeyClassExport = new TrRmSourceClassExport(program, 0,
            declaration.Span, "BoxKey", [
                new TrRmSourceClassMemberExport(0, constructor.Span,
                    "BoxKey"),
            ]);
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.SourceClassExports.Capture(
                program, "Module", [], [], [duplicateKeyClassExport]));
    }

    private static (BoundProgram Program, SyntaxTree Tree,
        SyntaxNode Declaration, ImmutableArray<SyntaxNode> Members)
        CreateProgram()
    {
        var tree = SyntaxTree.Parse("""
            public class Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
                public int32 Read() { return Value; }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var declaration = tree.Root.Children.OfType<SyntaxNode>().Single(
            static node => node.Kind == SyntaxKind.ClassDeclaration);
        return ((BoundProgram)compilation.Program!, tree, declaration,
            [.. declaration.Children.OfType<SyntaxNode>()]);
    }

    private static SyntaxNode Member(ImmutableArray<SyntaxNode> members,
        SyntaxKind kind)
        => members.Single(member => member.Kind == kind);
}
