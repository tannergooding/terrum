// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class MutableStructReceiverTests
{
    [TestCase("Counter value = new Counter(40); value.Increment();")]
    [TestCase("new Counter(40).Increment();")]
    [TestCase("Counter.Create().Increment();")]
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
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("public void Readonly() { Increment(); }")]
    [TestCase("public mut void Replace() { this = new Counter(42); }")]
    public void ReadonlyMethodsAndReceiverRebindingCannotMutateStorage(string method)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Counter {
                public mut int32 Value;
                public Counter(int32 value) { Value = value; }
                public mut void Increment() { Value = Value + 1; }
            """ + method + " }"));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("Owner owner = new Owner(41); owner.Increment();")]
    [TestCase("new Owner(41).Increment();")]
    [TestCase("mut Owner owner = new Owner(41); Owner moved = move owner; owner.Increment();")]
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
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var main = program.Functions[program.EntryPoint];
        var region = ((global::System.Func<BoundTryFinally>)(() => { var __nunitActual1 = main.Body.Statements[1]; Assert.That(__nunitActual1, Is.TypeOf<BoundTryFinally>()); return (BoundTryFinally)__nunitActual1!; }))();
        var statement = ((global::System.Func<BoundExpressionStatement>)(() => { var __nunitActual2 = region.Body.Statements[0]; Assert.That(__nunitActual2, Is.TypeOf<BoundExpressionStatement>()); return (BoundExpressionStatement)__nunitActual2!; }))();
        var call = ((global::System.Func<BoundCall>)(() => { var __nunitActual3 = statement.Expression; Assert.That(__nunitActual3, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual3!; }))();
        var loan = ((global::System.Func<BoundMutableReceiverLoan>)(() => { var __nunitActual4 = global::System.Linq.Enumerable.Single(call.Arguments); Assert.That(__nunitActual4, Is.TypeOf<BoundMutableReceiverLoan>()); return (BoundMutableReceiverLoan)__nunitActual4!; }))();
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
        }))!;
    }
    [TestCase("Outer owner = new Outer(1); owner.Nested.Child.Increment(1);","TRM3023")]
    [TestCase("mut ReadonlyOuter owner = new ReadonlyOuter(1); owner.Nested.Child.Increment(1);","TRM3023")]
    [TestCase("mut Outer owner = new Outer(1); owner.Nested.Readonly.Increment(1);","TRM3023")]
    [TestCase("new Outer(1).Nested.Child.Increment(1);","TRM3023")]
    [TestCase("mut UniqueOuter owner = new UniqueOuter(1); owner.Nested.Child.Increment(1);","TRM3023")]
    [TestCase("mut Outer owner = new Outer(1); owner.Nested.Hidden.Increment(1);","TRM3011")]
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
                public Middle Nested;
                public UniqueOuter(int32 value) { Nested = new Middle(value); }
            }
            int32 Main() {
            """ + body + " return 0; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == code),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("Owner owner = new Owner(41); owner.Child.Increment(true);")]
    [TestCase("mut Owner owner = new Owner(41); owner.Readonly.Increment(true);")]
    [TestCase("new Owner(41).Child.Increment(true);")]
    [TestCase("mut Owner owner = new Owner(41); Owner moved = move owner; owner.Child.Increment(true);")]
    [TestCase("mut Owner owner = new Owner(41); owner.Child.Increment(Replace(out owner));")]
    public void UniqueFieldReceiverRequiresInitializedWritableRootAndFields(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Child {
                public mut int32 Value;
                public Child(int32 value) { Value = value; }
                public mut void Increment(bool enabled) { if (enabled) { Value = Value + 1; } }
                [NoThrow] [Cleanup] public void Close() {}
            }
            unique struct Owner {
                public mut Child Child;
                public Child Readonly;
                public Owner(int32 value) { Child = new Child(value); Readonly = new Child(value); }
                [NoThrow] [Cleanup] public void Close() {}
            }
            bool Replace(out Owner result) { result = new Owner(1); return true; }
            int32 Main() {
            """ + body + " return 0; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void PartialUniqueConstructorCannotLoanInitializedChildBeforeWholeOwner()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            unique struct Child {
                public mut int32 Value;
                public Child(int32 value) { Value = value; }
                public mut void Increment() { Value = Value + 1; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            unique struct Owner {
                public mut Child Nested;
                public int32 Guard;
                public Owner() {
                    Nested = new Child(41);
                    Nested.Increment();
                    Guard = 0;
                }
                [NoThrow] [Cleanup] public void Close() {}
            }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public void ForgedMutableFieldLoanThroughUniqueOwnerIsRejected(int mutation)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Leaf {
                public mut int32 Value;
                public Leaf(int32 value) { Value = value; }
                public mut void Increment() { Value = Value + 1; }
            }
            unique struct Child {
                public mut Leaf Data;
                public Child(int32 value) { Data = new Leaf(value); }
                [NoThrow] [Cleanup] public void Close() {}
            }
            unique struct Owner {
                public mut Child Active;
                public Child Readonly;
                public Owner(int32 value) {
                    Active = new Child(value);
                    Readonly = new Child(value);
                }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Main() {
                mut Owner owner = new Owner(41);
                owner.Active.Data.Increment();
                return owner.Active.Data.Value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = compilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))();
        var main = program.Functions[program.EntryPoint];
        var region = ((global::System.Func<BoundTryFinally>)(() => { var __nunitActual6 = main.Body.Statements[1]; Assert.That(__nunitActual6, Is.TypeOf<BoundTryFinally>()); return (BoundTryFinally)__nunitActual6!; }))();
        var statement = ((global::System.Func<BoundExpressionStatement>)(() => { var __nunitActual7 = region.Body.Statements[0]; Assert.That(__nunitActual7, Is.TypeOf<BoundExpressionStatement>()); return (BoundExpressionStatement)__nunitActual7!; }))();
        var call = ((global::System.Func<BoundCall>)(() => { var __nunitActual8 = statement.Expression; Assert.That(__nunitActual8, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual8!; }))();
        var loan = ((global::System.Func<BoundMutableFieldLoan>)(() => { var __nunitActual9 = global::System.Linq.Enumerable.Single(call.Arguments); Assert.That(__nunitActual9, Is.TypeOf<BoundMutableFieldLoan>()); return (BoundMutableFieldLoan)__nunitActual9!; }))();
        var parent = ((global::System.Func<BoundFieldRead>)(() => { var __nunitActual10 = loan.Place.Receiver; Assert.That(__nunitActual10, Is.TypeOf<BoundFieldRead>()); return (BoundFieldRead)__nunitActual10!; }))();
        var forgedPlace = mutation switch {
            0 => loan.Place with { Field = 8 },
            1 => loan.Place with { Type = PrimitiveType.Int32 },
            2 => loan.Place with { Receiver = parent with { Field = 1 } },
            3 => loan.Place with { Receiver = parent with { Type = PrimitiveType.Int32 } },
            4 => loan.Place with {
                Receiver = parent with { Receiver = new BoundRead(99, parent.Receiver.Type, parent.Span) },
            },
            _ => loan.Place with {
                Receiver = parent with { Receiver = new BoundConstant(0, parent.Receiver.Type, parent.Span) },
            },
        };
        var forged = region with {
            Body = region.Body with {
                Statements = region.Body.Statements.SetItem(0, statement with {
                    Expression = call with { Arguments = [loan with { Place = forgedPlace }] },
                }),
            },
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, main with {
                Body = main.Body with { Statements = main.Body.Statements.SetItem(1, forged) },
            }),
        }))!;
    }

    [Test]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = compilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))();
        SemanticVerifier.Verify(program);
        var method = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Counter.Increment");
        Assert.That(method.Variables[0].IsMutable, Is.True);
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual12 = compilation.Program; Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual12!; }))();
        var main = program.Functions[program.EntryPoint];
        var readonlyIndex = Array.FindIndex(main.Variables.ToArray(),
            static variable => variable.Name == "readonlyValue");
        var call = ((global::System.Func<BoundCall>)(() => {
            var __nunitActual13 = global::System.Linq.Enumerable.Single(main.Body.Statements
            .OfType<BoundExpressionStatement>()).Expression;
            Assert.That(__nunitActual13, Is.TypeOf<BoundCall>());
            return (BoundCall)__nunitActual13!;
        }))();
        var loan = ((global::System.Func<BoundMutableReceiverLoan>)(() => { var __nunitActual14 = global::System.Linq.Enumerable.Single(call.Arguments); Assert.That(__nunitActual14, Is.TypeOf<BoundMutableReceiverLoan>()); return (BoundMutableReceiverLoan)__nunitActual14!; }))();
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
        }))!;
    }
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual15 = compilation.Program; Assert.That(__nunitActual15, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual15!; }))();
        var main = program.Functions[program.EntryPoint];
        var immutable = Array.FindIndex(main.Variables.ToArray(), variable => variable.Name == "immutable");
        var index = Array.FindIndex(main.Body.Statements.ToArray(),
            statement => statement is BoundExpressionStatement);
        var statement = ((global::System.Func<BoundExpressionStatement>)(() => { var __nunitActual16 = main.Body.Statements[index]; Assert.That(__nunitActual16, Is.TypeOf<BoundExpressionStatement>()); return (BoundExpressionStatement)__nunitActual16!; }))();
        var call = ((global::System.Func<BoundCall>)(() => { var __nunitActual17 = statement.Expression; Assert.That(__nunitActual17, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual17!; }))();
        var loan = ((global::System.Func<BoundMutableFieldLoan>)(() => { var __nunitActual18 = call.Arguments[0]; Assert.That(__nunitActual18, Is.TypeOf<BoundMutableFieldLoan>()); return (BoundMutableFieldLoan)__nunitActual18!; }))();
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
        }))!;
    }
    [TestCase("int32","40","")]
    [TestCase("int64","40","public int64 Second;")]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual19 = compilation.Program; Assert.That(__nunitActual19, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual19!; }))();
        var main = program.Functions[program.EntryPoint];
        var index = Array.FindIndex(main.Body.Statements.ToArray(),
            static statement => statement is BoundExpressionStatement);
        var statement = ((global::System.Func<BoundExpressionStatement>)(() => { var __nunitActual20 = main.Body.Statements[index]; Assert.That(__nunitActual20, Is.TypeOf<BoundExpressionStatement>()); return (BoundExpressionStatement)__nunitActual20!; }))();
        var call = ((global::System.Func<BoundCall>)(() => { var __nunitActual21 = statement.Expression; Assert.That(__nunitActual21, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual21!; }))();
        var loan = ((global::System.Func<BoundMutableReceiverLoan>)(() => { var __nunitActual22 = global::System.Linq.Enumerable.Single(call.Arguments); Assert.That(__nunitActual22, Is.TypeOf<BoundMutableReceiverLoan>()); return (BoundMutableReceiverLoan)__nunitActual22!; }))();
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
        }))!;
    }

    [Test]
    public void ForgedOutReceiverHeaderIsRejected()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Counter {
                public mut int32 Value;
                public Counter() { Value = 0; }
                public mut void Touch() { }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual23 = compilation.Program; Assert.That(__nunitActual23, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual23!; }))();
        var index = Array.FindIndex(program.Functions.ToArray(),
            static function => function.Name == "Counter.Touch");
        var method = program.Functions[index];
        var forged = method with {
            Variables = method.Variables.SetItem(0, method.Variables[0] with { IsOut = true }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(program with {
            Functions = program.Functions.SetItem(index, forged),
        }))!;
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual24 = compilation.Program; Assert.That(__nunitActual24, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual24!; }))();
        var main = program.Functions[program.EntryPoint];
        var returned = ((global::System.Func<BoundReturn>)(() => { var __nunitActual25 = main.Body.Statements[^1]; Assert.That(__nunitActual25, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual25!; }))();
        var call = ((global::System.Func<BoundCall>)(() => { var __nunitActual26 = returned.Value; Assert.That(__nunitActual26, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual26!; }))();
        var read = ((global::System.Func<BoundRead>)(() => { var __nunitActual27 = global::System.Linq.Enumerable.Single(call.Arguments); Assert.That(__nunitActual27, Is.TypeOf<BoundRead>()); return (BoundRead)__nunitActual27!; }))();
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
        }))!;
    }
}
