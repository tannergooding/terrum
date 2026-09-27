// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class MutableStructReceiverTests
{
    [Theory]
    [InlineData("Counter value = new Counter(40); value.Increment();")]
    [InlineData("new Counter(40).Increment();")]
    [InlineData("Counter.Create().Increment();")]
    public void MutationRequiresNamedMutableStorage(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Counter {
                public mut int32 Value;
                public Counter(int32 value) { Value = value; }
                public static Counter Create() { return new Counter(40); }
                public mut void Increment() { Value = Value + 1; }
            }
            int32 Main() {
            """ + body + " return 0; }"));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("public void Readonly() { Increment(); }")]
    [InlineData("public mut void Replace() { this = new Counter(42); }")]
    public void ReadonlyMethodsAndReceiverRebindingCannotMutateStorage(string method)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Counter {
                public mut int32 Value;
                public Counter(int32 value) { Value = value; }
                public mut void Increment() { Value = Value + 1; }
            """ + method + " }"));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void UniqueStructMutationDoesNotClaimAnExclusiveLoanContract()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            unique struct Owner {
                public int32 Value;
                public Owner(int32 value) { Value = value; }
                public mut void Increment() { Value = Value + 1; }
            }
            """));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void MutableReceiverHasDistinctVerifiedPlaceContract()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Counter {
                public mut int32 Value;
                public Counter(int32 value) { Value = value; }
                public mut void Increment() { Value = Value + 1; }
            }
            int32 Main() {
                mut Counter value = new Counter(40);
                value.Increment();
                return value.Value;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        SemanticVerifier.Verify(program);
        var method = Assert.Single(program.Functions,
            function => function.Name == "Counter.Increment");
        Assert.True(method.Variables[0].IsMutable);
    }

    [Fact]
    public void ForgedLoanFromReadonlyLocalIsRejected()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Counter {
                public mut int32 Value;
                public Counter(int32 value) { Value = value; }
                public mut void Increment() { Value = Value + 1; }
            }
            int32 Main() {
                Counter readonlyValue = new Counter(1);
                mut Counter writable = readonlyValue;
                writable.Increment();
                return writable.Value;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var main = program.Functions[program.EntryPoint];
        var readonlyIndex = Array.FindIndex(main.Variables.ToArray(),
            static variable => variable.Name == "readonlyValue");
        var call = Assert.IsType<BoundCall>(Assert.Single(main.Body.Statements
            .OfType<BoundExpressionStatement>()).Expression);
        var loan = Assert.IsType<BoundMutableReceiverLoan>(Assert.Single(call.Arguments));
        var forgedCall = call with { Arguments = [loan with { Variable = readonlyIndex }] };
        var index = Array.FindIndex(main.Body.Statements.ToArray(),
            statement => statement is BoundExpressionStatement expression && expression.Expression == call);
        var forged = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(index,
                    new BoundExpressionStatement(forgedCall, call.Span)),
            },
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, forged),
        }));
    }

    [Theory]
    [InlineData("int32", "40", "")]
    [InlineData("int64", "40i64", "public int64 Second;")]
    public void MutableReceiverRejectsForgedValueArgumentBeforeEmission(
        string type, string initial, string additionalField)
    {
        ArgumentNullException.ThrowIfNull(additionalField);
        var initializeExtra = additionalField.Length == 0 ? "" : "Second = 2i64;";
        var source = $$"""
            struct Counter {
                public mut {{type}} Value;
                {{additionalField}}
                public Counter() { Value = {{initial}}; {{initializeExtra}} }
                public mut void Increment() { Value = Value + 1; }
            }
            int32 Main() {
                mut Counter value = new Counter();
                value.Increment();
                return (int32)value.Value;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var main = program.Functions[program.EntryPoint];
        var index = Array.FindIndex(main.Body.Statements.ToArray(),
            static statement => statement is BoundExpressionStatement);
        var statement = Assert.IsType<BoundExpressionStatement>(main.Body.Statements[index]);
        var call = Assert.IsType<BoundCall>(statement.Expression);
        var loan = Assert.IsType<BoundMutableReceiverLoan>(Assert.Single(call.Arguments));
        var forged = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(index, statement with {
                    Expression = call with {
                        Arguments = [new BoundRead(loan.Variable, loan.Type, loan.Span)],
                    },
                }),
            },
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, forged),
        }));
    }

    [Fact]
    public void ForgedOutReceiverHeaderIsRejected()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Counter {
                public mut int32 Value;
                public Counter() { Value = 0; }
                public mut void Touch() { }
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var index = Array.FindIndex(program.Functions.ToArray(),
            static function => function.Name == "Counter.Touch");
        var method = program.Functions[index];
        var forged = method with {
            Variables = method.Variables.SetItem(0, method.Variables[0] with { IsOut = true }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(program with {
            Functions = program.Functions.SetItem(index, forged),
        }));
    }

    [Fact]
    public void ForgedMutableLoanToReadonlyMethodIsRejected()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Counter {
                public int32 Value;
                public Counter(int32 value) { Value = value; }
                public int32 Read() { return Value; }
            }
            int32 Main() {
                mut Counter value = new Counter(42);
                return value.Read();
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var main = program.Functions[program.EntryPoint];
        var returned = Assert.IsType<BoundReturn>(main.Body.Statements[^1]);
        var call = Assert.IsType<BoundCall>(returned.Value);
        var read = Assert.IsType<BoundRead>(Assert.Single(call.Arguments));
        var forged = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(main.Body.Statements.Length - 1,
                    returned with {
                        Value = call with {
                            Arguments = [new BoundMutableReceiverLoan(read.Variable, read.Type, read.Span)],
                        },
                    }),
            },
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, forged),
        }));
    }
}
