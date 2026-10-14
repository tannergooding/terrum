// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class MutableOwningBufferFieldTests
{
    private const string Owner = """
        unique struct Owner {
            public mut OwnedInt32Buffer Words;
            public Owner(OwnedInt32Buffer words) { Words = move words; }
            [NoThrow] [Cleanup] public void Close() {}
        }
        """;
    [TestCase("Owner owner = new Owner(move raw); owner.Words.Clear();")]
    [TestCase("new Owner(move raw).Words.Clear();")]
    [TestCase("mut Owner owner = new Owner(move raw); Owner moved = move owner; owner.Words.Clear();")]
    [TestCase("mut Owner owner = new Owner(move raw); owner.Words.TryAppend(Consume(move owner));")]
    [TestCase("mut Owner owner = new Owner(move raw); owner.Words.TryAppend(ToInt(Replace(out owner)));")]
    [TestCase("mut Owner owner = new Owner(move raw); owner.Words.Set(0, ToInt(Replace(out owner)));")]
    public void MutableBufferFieldRequiresLiveWritableReservedRoot(string operation)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Owner + """
            int32 Consume(Owner value) { return 42; }
            int32 ToInt(bool value) { return 42; }
            bool Replace(out Owner result) {
                if (!OwnedInt32Buffer.TryCreate(1, out var next)) { return false; }
                result = new Owner(move next);
                return true;
            }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
            """ + operation + " return 0; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("owner.Readonly.TryAppend(42);","TRM3023")]
    [TestCase("owner.Hidden.TryAppend(42);","TRM3011")]
    public void MutableBufferFieldRequiresAccessibleMutableField(string operation, string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public OwnedInt32Buffer Readonly;
                private mut OwnedInt32Buffer Hidden;
                public Owner(OwnedInt32Buffer first, OwnedInt32Buffer second) {
                    Readonly = move first;
                    Hidden = move second;
                }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var first);
                require OwnedInt32Buffer.TryCreate(1, out var second);
                mut Owner owner = new Owner(move first, move second);
            """ + operation + " return 0; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == code),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void PartiallyConstructedOwnerCannotMutateInitializedBufferField()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            unique struct Owner {
                public mut OwnedInt32Buffer Words;
                public int32 Tail;
                public Owner(OwnedInt32Buffer words) {
                    Words = move words;
                    Words.Clear();
                    Tail = 0;
                }
                [NoThrow] [Cleanup] public void Close() {}
            }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("ReadonlyInt32BufferView view = owner.Words.Slice(0, 1); owner.Words.Clear();")]
    [TestCase("ReadonlyInt32BufferView view = owner.Words.Slice(0, 1); owner.Words.TryAppend(1);")]
    [TestCase("ReadonlyInt32BufferView view = owner.Words.Slice(0, 1); mut MutableInt32BufferView other = owner.Words.SliceMutable(0, 1);")]
    public void ActiveReadonlyViewBlocksFieldMutationAndExclusiveAcquisition(string operation)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Owner + """
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(2, out var raw);
                mut Owner owner = new Owner(move raw);
                require owner.Words.TryAppend(42);
            """ + operation + " return 0; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void ForgedOwningBufferFieldLoanRejectsMalformedTargetOrPlace(int mutation)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public mut OwnedInt32Buffer Words;
                public Owner(OwnedInt32Buffer words) { Words = move words; }
                public mut void Append() { Words.TryAppend(42); }
                public int32 Read() { return Words.Read(0); }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Main() { return 42; }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var functionIndex = Array.FindIndex(program.Functions.ToArray(),
            static function => function.Name == "Owner.Append");
        var function = program.Functions[functionIndex];
        var statement = ((global::System.Func<BoundExpressionStatement>)(() => { var __nunitActual1 = function.Body.Statements[0]; Assert.That(__nunitActual1, Is.TypeOf<BoundExpressionStatement>()); return (BoundExpressionStatement)__nunitActual1!; }))();
        var call = ((global::System.Func<BoundCall>)(() => { var __nunitActual2 = statement.Expression; Assert.That(__nunitActual2, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual2!; }))();
        var loan = ((global::System.Func<BoundMutableFieldLoan>)(() => { var __nunitActual3 = call.Arguments[0]; Assert.That(__nunitActual3, Is.TypeOf<BoundMutableFieldLoan>()); return (BoundMutableFieldLoan)__nunitActual3!; }))();
        var forgedCall = mutation switch {
            0 => call with {
                Arguments = call.Arguments.SetItem(0, loan with { Place = loan.Place with { Field = 99 } }),
            },
            1 => call with {
                Arguments = call.Arguments.SetItem(0, loan with {
                    Place = loan.Place with { Type = PrimitiveType.Int32 },
                }),
            },
            2 => call with {
                Arguments = call.Arguments.SetItem(0, loan with {
                    Place = loan.Place with {
                        Receiver = new BoundConstant(0, loan.Place.Receiver.Type, loan.Span),
                    },
                }),
            },
            _ => call with {
                Function = Array.FindIndex(program.Functions.ToArray(),
                    static function => function.Intrinsic == BoundIntrinsic.BufferRead),
                Type = PrimitiveType.Int32,
            },
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(functionIndex, function with {
                Body = function.Body with {
                    Statements = function.Body.Statements.SetItem(0, statement with { Expression = forgedCall }),
                },
            }),
        }))!;
    }
}
