// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

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

    [Theory]
    [InlineData("Owner owner = new Owner(move raw); owner.Words.Clear();")]
    [InlineData("new Owner(move raw).Words.Clear();")]
    [InlineData("mut Owner owner = new Owner(move raw); Owner moved = move owner; owner.Words.Clear();")]
    [InlineData("mut Owner owner = new Owner(move raw); owner.Words.TryAppend(Consume(move owner));")]
    [InlineData("mut Owner owner = new Owner(move raw); owner.Words.TryAppend(ToInt(Replace(out owner)));")]
    [InlineData("mut Owner owner = new Owner(move raw); owner.Words.Set(0, ToInt(Replace(out owner)));")]
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
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("owner.Readonly.TryAppend(42);", "TRM3023")]
    [InlineData("owner.Hidden.TryAppend(42);", "TRM3011")]
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
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Null(compilation.Program);
    }

    [Fact]
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
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("ReadonlyInt32BufferView view = owner.Words.Slice(0, 1); owner.Words.Clear();")]
    [InlineData("ReadonlyInt32BufferView view = owner.Words.Slice(0, 1); owner.Words.TryAppend(1);")]
    [InlineData("mut MutableInt32BufferView view = owner.Words.SliceMutable(0, 1);")]
    public void ActiveViewOrFieldSliceMutableIsNotAWritableFieldReceiver(string operation)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Owner + """
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(2, out var raw);
                mut Owner owner = new Owner(move raw);
                require owner.Words.TryAppend(42);
            """ + operation + " return 0; }"));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var functionIndex = Array.FindIndex(program.Functions.ToArray(),
            static function => function.Name == "Owner.Append");
        var function = program.Functions[functionIndex];
        var statement = Assert.IsType<BoundExpressionStatement>(function.Body.Statements[0]);
        var call = Assert.IsType<BoundCall>(statement.Expression);
        var loan = Assert.IsType<BoundMutableFieldLoan>(call.Arguments[0]);
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
        }));
    }
}
