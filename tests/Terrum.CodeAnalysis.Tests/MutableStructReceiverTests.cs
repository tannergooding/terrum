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

    [Theory]
    [InlineData("Owner owner = new Owner(41); owner.Increment();")]
    [InlineData("new Owner(41).Increment();")]
    [InlineData("mut Owner owner = new Owner(41); Owner moved = move owner; owner.Increment();")]
    public void UniqueStructMutationRequiresAnInitializedWritablePlace(string body)
    {
        const string Source = """
            unique struct Owner {
                public mut int32 Value;
                public Owner(int32 value) { Value = value; }
                public mut void Increment() { Value = Value + 1; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source
            + "int32 Main() { " + body + " return 0; }"));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void UniqueMutableReceiverReservesRootBeforeArgumentMoves()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public mut int32 Value;
                public Owner(int32 value) { Value = value; }
                public mut void Accept(Owner other) { Value = other.Value; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Main() {
                mut Owner owner = new Owner(42);
                owner.Accept(move owner);
                return 0;
            }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void ForgedReadonlyLoanCannotCallUniqueMutableMethod()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public mut int32 Value;
                public Owner(int32 value) { Value = value; }
                public mut void Increment() { Value = Value + 1; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Main() {
                mut Owner owner = new Owner(41);
                owner.Increment();
                return owner.Value;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var main = program.Functions[program.EntryPoint];
        var region = Assert.IsType<BoundTryFinally>(main.Body.Statements[1]);
        var statement = Assert.IsType<BoundExpressionStatement>(region.Body.Statements[0]);
        var call = Assert.IsType<BoundCall>(statement.Expression);
        var loan = Assert.IsType<BoundMutableReceiverLoan>(Assert.Single(call.Arguments));
        var forged = region with {
            Body = region.Body with {
                Statements = region.Body.Statements.SetItem(0, statement with {
                    Expression = call with {
                        Arguments = [new BoundReceiverLoan(loan.Variable, loan.Type, loan.Span)],
                    },
                }),
            },
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, main with {
                Body = main.Body with { Statements = main.Body.Statements.SetItem(1, forged) },
            }),
        }));
    }

    [Theory]
    [InlineData("Outer owner = new Outer(1); owner.Nested.Child.Increment(1);", "TRM3023")]
    [InlineData("mut ReadonlyOuter owner = new ReadonlyOuter(1); owner.Nested.Child.Increment(1);", "TRM3023")]
    [InlineData("mut Outer owner = new Outer(1); owner.Nested.Readonly.Increment(1);", "TRM3023")]
    [InlineData("new Outer(1).Nested.Child.Increment(1);", "TRM3023")]
    [InlineData("mut UniqueOuter owner = new UniqueOuter(1); owner.Nested.Child.Increment(1);", "TRM3023")]
    [InlineData("mut Outer owner = new Outer(1); owner.Nested.Hidden.Increment(1);", "TRM3011")]
    public void NestedReceiverRequiresWritableAccessibleNonOwningPlace(string body, string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Inner {
                public mut int32 Value;
                public Inner(int32 value) { Value = value; }
                public mut void Increment(int32 amount) { Value = Value + amount; }
            }
            struct Middle {
                public mut Inner Child;
                public Inner Readonly;
                private mut Inner Hidden;
                public Middle(int32 value) {
                    Child = new Inner(value);
                    Readonly = new Inner(value);
                    Hidden = new Inner(value);
                }
            }
            struct Outer {
                public mut Middle Nested;
                public Outer(int32 value) { Nested = new Middle(value); }
            }
            struct ReadonlyOuter {
                public Middle Nested;
                public ReadonlyOuter(int32 value) { Nested = new Middle(value); }
            }
            unique struct UniqueOuter {
                public mut Middle Nested;
                public UniqueOuter(int32 value) { Nested = new Middle(value); }
            }
            int32 Main() {
            """ + body + " return 0; }"));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void NestedReceiverReservesRootBeforeEvaluatingArguments()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Inner {
                public mut int32 Value;
                public Inner(int32 value) { Value = value; }
                public mut void Add(bool replaced) { Value = Value + 1; }
            }
            struct Outer {
                public mut Inner Nested;
                public Outer(int32 value) { Nested = new Inner(value); }
            }
            bool Replace(out Outer result) { result = new Outer(1); return true; }
            int32 Main() {
                mut Outer owner = new Outer(41);
                owner.Nested.Add(Replace(out owner));
                return owner.Nested.Value;
            }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void ReadonlyThisCannotLoanItsMutableField()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Inner {
                public mut int32 Value;
                public Inner(int32 value) { Value = value; }
                public mut void Increment() { Value = Value + 1; }
            }
            struct Outer {
                public mut Inner Nested;
                public Outer(int32 value) { Nested = new Inner(value); }
                public void Change() { Nested.Increment(); }
            }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void ManagedClassFieldCannotBecomeMutableStructReceiver()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Inner {
                public mut int32 Value;
                public Inner(int32 value) { Value = value; }
                public mut void Increment() { Value = Value + 1; }
            }
            class Holder {
                public mut Inner Nested;
                public Holder(int32 value) { Nested = new Inner(value); }
            }
            int32 Main() {
                require Holder.TryCreate(41, out var holder);
                holder.Nested.Increment();
                return 0;
            }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
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
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ForgedNestedMutablePlacesAreRejected(int mutation)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Inner {
                public mut int32 Value;
                public Inner(int32 value) { Value = value; }
                public mut void Increment(int32 amount) { Value = Value + amount; }
            }
            struct Outer {
                public mut Inner Active;
                public Inner Readonly;
                public Outer(int32 value) {
                    Active = new Inner(value);
                    Readonly = new Inner(value);
                }
            }
            int32 Main() {
                mut Outer owner = new Outer(41);
                Outer immutable = owner;
                owner.Active.Increment(1);
                return owner.Active.Value;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var main = program.Functions[program.EntryPoint];
        var immutable = Array.FindIndex(main.Variables.ToArray(), variable => variable.Name == "immutable");
        var index = Array.FindIndex(main.Body.Statements.ToArray(),
            statement => statement is BoundExpressionStatement);
        var statement = Assert.IsType<BoundExpressionStatement>(main.Body.Statements[index]);
        var call = Assert.IsType<BoundCall>(statement.Expression);
        var loan = Assert.IsType<BoundMutableFieldLoan>(call.Arguments[0]);
        var forgedPlace = mutation switch {
            0 => loan.Place with { Field = 9 },
            1 => loan.Place with { Field = 1 },
            2 => loan.Place with { Type = PrimitiveType.Int32 },
            3 => loan.Place with {
                Receiver = new BoundRead(immutable, loan.Place.Receiver.Type, loan.Place.Span),
            },
            _ => loan.Place with {
                Receiver = new BoundConstant(0, loan.Place.Receiver.Type, loan.Place.Span),
            },
        };
        var forged = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(index, statement with {
                    Expression = call with { Arguments = call.Arguments.SetItem(0, loan with { Place = forgedPlace }) },
                }),
            },
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, forged),
        }));
    }

    [Theory]
    [InlineData("int32", "40", "")]
    [InlineData("int64", "40", "public int64 Second;")]
    public void MutableReceiverRejectsForgedValueArgumentBeforeEmission(
        string type, string initial, string additionalField)
    {
        ArgumentNullException.ThrowIfNull(additionalField);
        var initializeExtra = additionalField.Length == 0 ? "" : "Second = 2;";
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
                return int32(value.Value);
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
