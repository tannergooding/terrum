// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class MutableBufferFieldViewTests
{
    private const string Owner = """
        unique struct Owner {
            public mut OwnedInt32Buffer Words;
            public mut OwnedInt32Buffer Sibling;
            public Owner(OwnedInt32Buffer first, OwnedInt32Buffer second) {
                Words = move first;
                Sibling = move second;
            }
        }
        """;

    [Theory]
    [InlineData("owner.Words.Read(0);")]
    [InlineData("int32 count = owner.Sibling.Count;")]
    [InlineData("owner.Words.Clear();")]
    [InlineData("owner.Sibling.TryAppend(1);")]
    [InlineData("Owner moved = move owner;")]
    [InlineData("ReadonlyInt32BufferView other = owner.Sibling.Slice(0, 0);")]
    [InlineData("mut MutableInt32BufferView other = owner.Sibling.SliceMutable(0, 0);")]
    public void ActiveFieldViewExcludesAllUsesOfItsUniqueRoot(string operation)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Owner + """
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var first);
                require OwnedInt32Buffer.TryCreate(1, out var second);
                mut Owner owner = new Owner(move first, move second);
                mut MutableInt32BufferView view = owner.Words.SliceMutable(0, 0);
            """ + operation + " return view.Length; }"));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void ActiveFieldViewAlsoExcludesDirectScalarReadsFromRoot()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public mut OwnedInt32Buffer Words;
                public int32 Marker;
                public Owner(OwnedInt32Buffer words) { Words = move words; Marker = 42; }
            }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                mut Owner owner = new Owner(move raw);
                mut MutableInt32BufferView view = owner.Words.SliceMutable(0, 0);
                int32 marker = owner.Marker;
                return view.Length;
            }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("Owner owner = new Owner(move first, move second); mut MutableInt32BufferView view = owner.Words.SliceMutable(0, 0);")]
    [InlineData("mut MutableInt32BufferView view = new Owner(move first, move second).Words.SliceMutable(0, 0);")]
    [InlineData("mut Owner owner = new Owner(move first, move second); mut MutableInt32BufferView view = owner.Words.SliceMutable(ToInt(Replace(out owner)), 0);")]
    [InlineData("mut Owner owner = new Owner(move first, move second); mut MutableInt32BufferView view = owner.Words.SliceMutable(0, ToInt(Replace(out owner)));")]
    [InlineData("mut Owner owner = new Owner(move first, move second); mut MutableInt32BufferView view = owner.Words.SliceMutable(ToInt(owner.Words.TryAppend(1)), 0);")]
    public void FieldViewAcquisitionRequiresWritableLiveRootWithoutArgumentInvalidation(string acquisition)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Owner + """
            int32 ToInt(bool value) { return 0; }
            bool Replace(out Owner result) {
                require OwnedInt32Buffer.TryCreate(1, out var first);
                require OwnedInt32Buffer.TryCreate(1, out var second);
                result = new Owner(move first, move second);
                return true;
            }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var first);
                require OwnedInt32Buffer.TryCreate(1, out var second);
            """ + acquisition + " return 0; }"));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void PartiallyConstructedOwnerCannotAcquireMutableFieldView()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            unique struct Owner {
                public mut OwnedInt32Buffer Words;
                public int32 Tail;
                public Owner(OwnedInt32Buffer words) {
                    Words = move words;
                    mut MutableInt32BufferView view = Words.SliceMutable(0, 0);
                    Tail = 0;
                }
            }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("public OwnedInt32Buffer Words;", "owner.Words")]
    [InlineData("private mut OwnedInt32Buffer Words;", "owner.Words")]
    public void FieldViewRequiresWritableAccessibleField(string declaration, string receiver)
    {
        var source = $$"""
            unique struct Owner {
                {{declaration}}
                public Owner(OwnedInt32Buffer words) { Words = move words; }
            }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                mut Owner owner = new Owner(move raw);
                mut MutableInt32BufferView view = {{receiver}}.SliceMutable(0, 0);
                return view.Length;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ForgedMutableFieldViewCannotBypassPlaceOrLoanChecks(int mutation)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public mut OwnedInt32Buffer Words;
                public Owner(OwnedInt32Buffer words) { Words = move words; }
                public mut void Update() {
                    mut MutableInt32BufferView view = Words.SliceMutable(0, 1);
                    view.Set(0, 42);
                }
            }
            int32 Main() { return 42; }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var index = Array.FindIndex(program.Functions.ToArray(), static function => function.Name == "Owner.Update");
        var function = program.Functions[index];
        var scope = Assert.IsType<BoundViewScope>(function.Body.Statements[0]);
        var loan = Assert.IsType<BoundMutableFieldLoan>(scope.Owner);
        var forgedOwner = mutation switch {
            0 => (BoundExpression)new BoundFieldLoan(loan.Place, loan.Type, loan.Span),
            1 => loan with { Place = loan.Place with { Field = 99 } },
            2 => loan with { Place = loan.Place with { Type = PrimitiveType.Int32 } },
            _ => loan with {
                Place = loan.Place with {
                    Receiver = new BoundRead(99, loan.Place.Receiver.Type, loan.Span),
                },
            },
        };
        var forged = program with {
            Functions = program.Functions.SetItem(index, function with {
                Body = function.Body with { Statements = function.Body.Statements.SetItem(0, scope with { Owner = forgedOwner }) },
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast));
    }
}
