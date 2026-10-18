// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Storage;

public sealed class NativeManagedClassCopyFieldTests
{
    private const string Valid = """
        struct Small {
            public int32 First;
            public int32 Second;
            public int32 Third;
            public Small(int32 first) { First = first; Second = 2; Third = 3; }
        }
        struct Wide {
            public int64 First;
            public int64 Second;
            public Wide(int64 first) { First = first; Second = 2; }
        }
        class Box {
            public bool Ready;
            public Small Value;
            public Wide Other;
            public Box(int32 first) {
                Ready = true;
                Value = new Small(first);
                Other = new Wide(40);
            }
        }
        int32 Main() {
            require Box.TryCreate(42, out var box);
            return box.Value.First;
        }
        """;

    [Test]
    public void CopyFieldsUseNaturalAlignmentAndRetainTheExistingFactoryAbi()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Valid));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var fields = program.Classes[0].Fields;
        Assert.That((fields[0].Offset, fields[0].Size), Is.EqualTo((0, 1)));
        Assert.That((fields[1].Offset, fields[1].Size), Is.EqualTo((4, 12)));
        Assert.That((fields[2].Offset, fields[2].Size), Is.EqualTo((16, 16)));
        Assert.That(program.Classes[0].Size, Is.EqualTo(32));
        SemanticVerifier.Verify(program);
    }
    [TestCase("public Small Value;","box.Value.First = 42;","TRM3008")]
    [TestCase("public mut Small Value;","box.Value.Second = 42;","TRM3008")]
    [TestCase("public OwnedInt32Buffer Value;","","TRM3023")]
    [TestCase("public ReadonlyInt32BufferView Value;","","TRM3023")]
    [TestCase("public Owner Value;","","TRM3023")]
    public void RejectsReadonlyNestedWritesAndOwningOrViewFields(string field, string assignment, string code)
    {
        ArgumentNullException.ThrowIfNull(field);
        var initializer = field.Contains("Small", StringComparison.Ordinal) ? "Value = new Small(1);" : "";
        var source = """
            struct Small {
                public mut int32 First;
                public int32 Second;
                public Small(int32 first) { First = first; Second = 2; }
            }
            unique struct Owner {
                public int32 Value;
                public Owner(int32 value) { Value = value; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            class Box {
            """ + field + """

                public Box() {
            """ + initializer + """

                }
            }
            int32 Main() {
                require Box.TryCreate(out var box);
            """ + assignment + """

                return 42;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == code),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void ForgedOwningAndMisalignedCopyFieldsNeverReachNativeEmission()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Valid));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        var @class = program.Classes[0];
        var fields = @class.Fields;
        var owning = program with {
            Classes = [@class with {
                Fields = fields.SetItem(1, fields[1] with { Type = PrimitiveType.OwnedInt32Buffer, Size = 8 }),
            }],
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(owning))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(owning, NativeRuntimePolicy.FailFast))!;
        var misaligned = program with {
            Classes = [@class with {
                Fields = fields.SetItem(1, fields[1] with { Offset = 8 }),
            }],
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(misaligned))!;
        var unique = program with {
            Structures = program.Structures.SetItem(0, program.Structures[0] with { IsUnique = true }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(unique))!;
    }

    [Test]
    public void ForgedReadonlyClassCopyFieldCannotBeMutatedThroughItsNestedFields()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Small {
                public mut int32 Value;
                public Small(int32 value) { Value = value; }
            }
            mut class Box {
                public mut Small Data;
                public Box() { Data = new Small(1); }
            }
            int32 Main() {
                require Box.TryCreate(out var box);
                box.Data.Value = 42;
                return box.Data.Value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        var @class = program.Classes[0];
        var readonlyField = @class.Fields[0] with { IsMutable = false };
        var forged = program with { Classes = [@class with { Fields = [readonlyField] }] };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(forged, NativeRuntimePolicy.FailFast))!;
    }

    [Test]
    public void ConstructorCannotWriteIntoAnUninitializedCopyField()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Small {
                public mut int32 Value;
                public Small(int32 value) { Value = value; }
            }
            mut class Box {
                public mut Small Data;
                public Box() {
                    Data.Value = 42;
                    Data = new Small(42);
                }
            }
            int32 Main() { return 42; }
            """));
        Assert.That(compilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3016"),Is.True);
    }
    [TestCase(8,true)]
    [TestCase(9,false)]
    public void CompactCopyFieldsRespectThe128ByteClassPayload(int count, bool fits)
    {
        var fields = string.Join(" ", Enumerable.Range(0, count).Select(
            static index => $"public Wide Field{index};"));
        var initializers = string.Join(" ", Enumerable.Range(0, count).Select(
            static index => $"Field{index} = new Wide(42);"));
        var source = $$"""
            struct Wide {
                public int64 First;
                public int64 Second;
                public Wide(int64 first) { First = first; Second = 2; }
            }
            class Box {
                {{fields}}
                public Box() { {{initializers}} }
            }
            int32 Main() {
                require Box.TryCreate(out var box);
                return int32(box.Field0.First);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        if (fits)
        {
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            Assert.That(((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))().Classes[0].Size, Is.EqualTo(128));
        }
        else
        {
            Assert.That(compilation.Program, Is.Null);
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3005"),Is.True);
        }
    }
}
