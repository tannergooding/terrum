// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class OptionalManagedCoreTests
{
    private const string Source = """
        mut class Link {
            public int32 Value;
            public mut write Link? Next;
            public Link(int32 value) {
                Value = value;
                Next = null;
                Next = this;
            }
        }
        int32 Main() {
            require Link.TryCreate(42, out var link);
            Link? optional = link;
            if (optional is some snapshot && snapshot.Value == 42) {
                if (link.Next is null) { return 1; }
                return snapshot.Value;
            }
            return 0;
        }
        """;

    [Fact]
    public void ContextualPatternTokensPreserveIdentifierKinds()
    {
        var syntax = SyntaxTree.Parse(Source);
        Assert.False(syntax.HasErrors);
        Assert.Equal("syntax-40", SyntaxTree.LanguageVersion);
        Assert.Contains(syntax.Tokens, static token => token is { Kind: TokenKind.Identifier, Text: "is" });
        Assert.Contains(syntax.Tokens, static token => token is { Kind: TokenKind.Identifier, Text: "some" });
        var identifiers = SyntaxTree.Parse("int32 Main() { int32 is = 20; int32 some = 22; return is + some; }");
        Assert.False(identifiers.HasErrors);
    }

    [Fact]
    public void OptionalClassNeedsTypedAbsenceAndTracksManagedRoots()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal("native-20", compilation.NativeProfileVersion);
        Assert.True(ManagedClassTypes.IsOptional(program.Classes[0].Fields[1].Type));
        Assert.NotEmpty(NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast).Code);
    }

    [Fact]
    public void PublishesPatternSnapshotsOnlyOnTheirProvenSuccessPaths()
    {
        var source = """
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            bool Take(Leaf? candidate, out Leaf? result) {
                if (candidate is null) { result = null; return true; }
                require candidate is some value;
                result = value;
                return true;
            }
            int32 Main() {
                require Leaf.TryCreate(42, out var leaf);
                require Take(leaf, out var accepted);
                mut Leaf? optional = accepted;
                mut int32 visits = 0;
                while (optional is some current && visits < 1) {
                    visits = visits + 1;
                    optional = null;
                    require current.Value == 42;
                }
                if (optional is null) { return visits + 41; }
                return 0;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal("native-20", compilation.NativeProfileVersion);
        Assert.NotEmpty(NativeEmitter.EmitCode(
            Assert.IsType<BoundProgram>(compilation.Program), NativeRuntimePolicy.FailFast).Code);
    }

    [Fact]
    public void NegatedGuardPublishesSnapshotAfterReturningAbsentBranch()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            int32 Main() {
                require Leaf.TryCreate(42, out var leaf);
                Leaf? candidate = leaf;
                if (!(candidate is some present)) { return 0; }
                return present.Value;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.NotEmpty(NativeEmitter.EmitCode(
            Assert.IsType<BoundProgram>(compilation.Program), NativeRuntimePolicy.FailFast).Code);
    }

    [Fact]
    public void RejectsOptionalPrivateNodeFromSemanticModules()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Proof() {
                ManagedNode? node = null;
                if (node is null) { return 42; }
                return 0;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal("native-20", compilation.NativeProfileVersion);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateProducer(program, ["Proof"]));
    }

    [Theory]
    [InlineData("int32 Main() { var missing = null; return 0; }")]
    [InlineData("int32 Main() { int32? invalid = null; return 0; }")]
    [InlineData("class C { public int32 Value; public C(int32 n) { Value = n; } } int32 Main() { C? value = null; return value.Value; }")]
    [InlineData("class C { public int32 Value; public C(int32 n) { Value = n; } } int32 Main() { C value = null; return value.Value; }")]
    [InlineData("class C { public int32 Value; public C(int32 n) { Value = n; } } int32 Main() { C? value = null; if (value is some present) { return present.Value; } return present.Value; }")]
    [InlineData("class C { public int32 Value; public C(int32 n) { Value = n; } } int32 Main() { C? value = null; if (value is some present || true) { return present.Value; } return 0; }")]
    public void RejectsMissingContextScalarOptionsAndUnprovenReads(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Null(compilation.Program);
        Assert.True(compilation.HasErrors);
    }

    [Fact]
    public void RejectsForgedOptionalProofAndNonNullZero()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Link {
                public int32 Value;
                public Link(int32 value) { Value = value; }
            }
            int32 Main() {
                require Link.TryCreate(42, out var link);
                Link? optional = link;
                if (optional is some snapshot) { return snapshot.Value; }
                return 0;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var main = program.Functions[program.EntryPoint];
        var local = Assert.IsType<BoundLocal>(main.Body.Statements[1]);
        var invalid = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(1,
                    local with { Initializer = new BoundConstant(0, local.Initializer.Type, local.Span) }),
            },
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, invalid),
        }));
        invalid = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(1,
                    local with {
                        Initializer = new BoundOptionalLift(
                        new BoundNull(local.Initializer.Type, local.Span),
                        local.Initializer.Type, local.Span)
                    }),
            },
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, invalid),
        }));
        invalid = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(1,
                    local with {
                        Initializer = new BoundOptionalLift(
                            new BoundConstant(0, ManagedClassTypes.Required(local.Initializer.Type), local.Span),
                            local.Initializer.Type, local.Span)
                    }),
            },
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, invalid),
        }));
        var conditional = Assert.IsType<BoundIf>(main.Body.Statements[2]);
        var pattern = Assert.IsType<BoundIsSome>(conditional.Condition);
        invalid = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(2, conditional with {
                    Condition = pattern with { Variable = 0 },
                }),
            },
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, invalid),
        }));
        invalid = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(2, new BoundReturn(
                    new BoundFieldRead(new BoundRead(local.Variable, local.Initializer.Type, local.Span),
                        0, PrimitiveType.Int32, local.Span), local.Span)),
            },
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, invalid),
        }));
    }
}
