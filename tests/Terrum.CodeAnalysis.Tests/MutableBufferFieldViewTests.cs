// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

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
    [TestCase("owner.Words.Read(0);")]
    [TestCase("int32 count = owner.Sibling.Count;")]
    [TestCase("owner.Words.Clear();")]
    [TestCase("owner.Sibling.TryAppend(1);")]
    [TestCase("Owner moved = move owner;")]
    [TestCase("ReadonlyInt32BufferView other = owner.Sibling.Slice(0, 0);")]
    [TestCase("mut MutableInt32BufferView other = owner.Sibling.SliceMutable(0, 0);")]
    public void ActiveFieldViewExcludesAllUsesOfItsUniqueRoot(string operation)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Owner + """
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var first);
                require OwnedInt32Buffer.TryCreate(1, out var second);
                mut Owner owner = new Owner(move first, move second);
                mut MutableInt32BufferView view = owner.Words.SliceMutable(0, 0);
            """ + operation + " return view.Length; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("Owner owner = new Owner(move first, move second); mut MutableInt32BufferView view = owner.Words.SliceMutable(0, 0);")]
    [TestCase("mut MutableInt32BufferView view = new Owner(move first, move second).Words.SliceMutable(0, 0);")]
    [TestCase("mut Owner owner = new Owner(move first, move second); mut MutableInt32BufferView view = owner.Words.SliceMutable(ToInt(Replace(out owner)), 0);")]
    [TestCase("mut Owner owner = new Owner(move first, move second); mut MutableInt32BufferView view = owner.Words.SliceMutable(0, ToInt(Replace(out owner)));")]
    [TestCase("mut Owner owner = new Owner(move first, move second); mut MutableInt32BufferView view = owner.Words.SliceMutable(ToInt(owner.Words.TryAppend(1)), 0);")]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3023"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("public OwnedInt32Buffer Words;","owner.Words")]
    [TestCase("private mut OwnedInt32Buffer Words;","owner.Words")]
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
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var index = Array.FindIndex(program.Functions.ToArray(), static function => function.Name == "Owner.Update");
        var function = program.Functions[index];
        var scope = ((global::System.Func<BoundViewScope>)(() => { var __nunitActual1 = function.Body.Statements[0]; Assert.That(__nunitActual1, Is.TypeOf<BoundViewScope>()); return (BoundViewScope)__nunitActual1!; }))();
        var loan = ((global::System.Func<BoundMutableFieldLoan>)(() => { var __nunitActual2 = scope.Owner; Assert.That(__nunitActual2, Is.TypeOf<BoundMutableFieldLoan>()); return (BoundMutableFieldLoan)__nunitActual2!; }))();
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
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast))!;
    }
}
