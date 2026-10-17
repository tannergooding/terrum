// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativePointerModuleTests
{
    [Test]
    public void PrivateReferenceSetsExcludePointeeTypesUsedOnlyByUnexportedFunctions()
    {
        const string firstSource = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct State {
                int32 Value;
                public State(int32 value) { Value = value; }
            }
            unsafe int32 Hidden(ptr<State> state) { return 1; }
            public int32 First() {
                State state = new State(1);
                unsafe { return Hidden(&state); }
            }
            """;
        const string secondSource = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct State {
                int32 Value;
                public State(int32 value) { Value = value; }
            }
            unsafe int32 Hidden(ptr<State> state) { return 2; }
            public int32 Second() {
                State state = new State(2);
                unsafe { return Hidden(&state); }
            }
            """;
        var firstCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(firstSource));
        var secondCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(secondSource));
        Assert.That(firstCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, firstCompilation.Diagnostics));
        Assert.That(secondCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, secondCompilation.Diagnostics));
        var firstProducer = ModuleCompiler.CreateProducer(firstCompilation.Program!, ["First"]);
        var secondProducer = ModuleCompiler.CreateProducer(secondCompilation.Program!, ["Second"]);

        var reference = ModuleCompiler.CreateReferenceSet([firstProducer, secondProducer]);
        Assert.That(reference.Structures.Any(static structure => structure.Name == "State"), Is.False);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() { return First() + Second() - 3; }
            """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
    }

    [Test]
    public void NativeGraphReferencesExcludePointeeTypesUsedOnlyByUnexportedFunctions()
    {
        const string firstSource = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct State {
                int32 Value;
                public State(int32 value) { Value = value; }
            }
            unsafe int32 Hidden(ptr<State> state) { return 1; }
            public int32 First() {
                State state = new State(1);
                unsafe { return Hidden(&state); }
            }
            """;
        const string secondSource = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct State {
                int64 Value;
                public State(int64 value) { Value = value; }
            }
            unsafe int32 Hidden(ptr<State> state) { return 2; }
            public int32 Second() {
                State state = new State(2);
                unsafe { return Hidden(&state); }
            }
            """;
        var firstCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(firstSource));
        var secondCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(secondSource));
        Assert.That(firstCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, firstCompilation.Diagnostics));
        Assert.That(secondCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, secondCompilation.Diagnostics));
        var firstProducer = NativeModuleCompiler.CreateProducer(firstCompilation.Program!,
            ["First"], "GraphPrivateFirst", NativeRuntimePolicy.FailFast);
        var secondProducer = NativeModuleCompiler.CreateProducer(secondCompilation.Program!,
            ["Second"], "GraphPrivateSecond", NativeRuntimePolicy.FailFast);

        var reference = NativeModuleCompiler.CreateGraphReference([firstProducer, secondProducer]);
        Assert.That(reference.Structures.Any(static structure => structure.Name == "State"), Is.False);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() { return First() + Second() - 3; }
            """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
    }

    [Test]
    public void TypedPointerIdentitiesMergeAcrossPrivateModuleReferences()
    {
        var firstCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Payload { int32 Value; }

            public unsafe ptr<Payload> First(ptr<Payload> value) { return value; }
            """));
        var secondCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Decoy { int32 Value; }

            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Other { int32 Value; }

            public unsafe ptr<Other> Second(ptr<Other> value) { return value; }
            public unsafe ptr<Decoy> DecoyPointer(ptr<Decoy> value) { return value; }
            """));

        Assert.That(firstCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, firstCompilation.Diagnostics));
        Assert.That(secondCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, secondCompilation.Diagnostics));
        var firstProducer = ModuleCompiler.CreateProducer(firstCompilation.Program!, ["First"]);
        var secondProducer = ModuleCompiler.CreateProducer(secondCompilation.Program!,
            ["Second", "DecoyPointer"]);
        var reference = ModuleCompiler.CreateReferenceSet([firstProducer, secondProducer]);

        var firstPointer = reference.Functions.Single(static function => function.Name == "First").Variables[0].Type;
        var secondPointer = reference.Functions.Single(static function => function.Name == "Second").Variables[0].Type;
        Assert.That(secondPointer, Is.Not.EqualTo(firstPointer));

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    First(null);
                    Second(null);
                    DecoyPointer(null);
                }
                return 0;
            }
            """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NativePointerAndFunctionPointerFieldsRemainValidAfterProjection(bool moduleSet)
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct PrivatePointerPrefix {
                ptr<int32> Address;
                fnptr<NativeAbi, int32, int32> Callback;
            }

            unsafe int32 Hidden(ptr<int32> privatePointer,
                fnptr<NativeAbi, int32, int32> privateCallback) { return 0; }

            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct ExportedPointers {
                public ptr<uint32> Address;
                public fnptr<NativeAbi, int64> Callback;
            }

            public unsafe int32 Accept(ExportedPointers value) { return 0; }
            """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var sourceStructure = providerCompilation.Program!.Structures.Single(
            static structure => structure.Name == "ExportedPointers");
        Assert.That(NativePointerTypes.Index(
            sourceStructure.Fields.Single(static field => field.Name == "Address").Type), Is.GreaterThan(0));
        Assert.That(NativeFunctionPointerTypes.Index(
            sourceStructure.Fields.Single(static field => field.Name == "Callback").Type), Is.GreaterThan(0));
        var provider = ModuleCompiler.CreateProducer(providerCompilation.Program!, ["Accept"]);
        var reference = moduleSet
            ? ModuleCompiler.CreateReferenceSet([
                provider,
                ModuleCompiler.CreateProducer(
                    Compilation.CreateLibrary(SyntaxTree.Parse("public int32 Extra() { return 0; }")).Program!,
                    ["Extra"]),
            ])
            : ModuleCompiler.CreateReference(provider);

        Assert.That(reference.Structures.Any(static structure => structure.Name == "PrivatePointerPrefix"), Is.False);
        var structure = reference.Structures.Single(static structure => structure.Name == "ExportedPointers");
        Assert.That(NativePointerTypes.Index(structure.Fields.Single(static field => field.Name == "Address").Type),
            Is.EqualTo(0));
        Assert.That(NativeFunctionPointerTypes.Index(
            structure.Fields.Single(static field => field.Name == "Callback").Type), Is.EqualTo(0));
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            unsafe int32 ReadFields(ExportedPointers value) {
                unsafe {
                    ptr<uint32> address = value.Address;
                    fnptr<NativeAbi, int64> callback = value.Callback;
                    return 0;
                }
            }
            int32 Main() { return 0; }
            """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
    }

    [TestCase("""
        unsafe ptr<int32> Escape() {
            int32 value = 1;
            unsafe { return &value; }
        }
        int32 Main() { return 0; }
        """)]
    [TestCase("""
        unsafe ptr<int32> Escape() {
            int32 value = 1;
            unsafe {
                ptr<int32> pointer = &value;
                ptr<int32> forwarded = pointer;
                return forwarded;
            }
        }
        int32 Main() { return 0; }
        """)]
    [TestCase("""
        unsafe ptr<int32> Forward(ptr<int32> pointer) { return pointer; }
        unsafe ptr<int32> Escape() {
            int32 value = 1;
            unsafe { return Forward(&value); }
        }
        int32 Main() { return 0; }
        """)]
    [TestCase("""
        unsafe ptr<int32> Escape() {
            int32 value = 1;
            mut ptr<int32> pointer = null;
            unsafe {
                write ptr<ptr<int32>> cell = &pointer;
                *cell = &value;
                return *cell;
            }
        }
        int32 Main() { return 0; }
        """)]
    [TestCase("""
        [Blittable] [Layout(LayoutKind.Sequential)]
        struct PointerHolder {
            public ptr<int32> Value;
            public unsafe PointerHolder(ptr<int32> value) { Value = value; }
        }
        unsafe ptr<int32> Escape() {
            int32 value = 1;
            unsafe {
                PointerHolder holder = new PointerHolder(&value);
                return holder.Value;
            }
        }
        int32 Main() { return 0; }
        """)]
    public void NativePointerCannotReturnAddressBackedByFunctionLocal(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"
            && diagnostic.Message.Contains("NativePointer", StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void VerifierTracksPointerBackingWhenWriteIsWrappedInUnsafeExpression()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unsafe ptr<int32> Escape() {
                int32 value = 1;
                mut ptr<int32> pointer = null;
                unsafe {
                    write ptr<ptr<int32>> cell = &pointer;
                    *cell = &value;
                    ptr<int32> observed = *cell;
                    return null;
                }
            }
            int32 Main() { return 0; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var functionIndex = Enumerable.Range(0, program.Functions.Length)
            .Single(index => program.Functions[index].Name == "Escape");
        var function = program.Functions[functionIndex];
        var unsafeScope = function.Body.Statements.OfType<BoundUnsafe>().Single();
        var scopeStatements = unsafeScope.Body.Statements;
        var writeIndex = Enumerable.Range(0, scopeStatements.Length)
            .Single(index => scopeStatements[index] is BoundExpressionStatement {
                Expression: BoundNativePointerWrite,
            });
        var writeStatement = (BoundExpressionStatement)scopeStatements[writeIndex];
        var write = (BoundNativePointerWrite)writeStatement.Expression;
        var observed = scopeStatements.OfType<BoundLocal>().Single(local =>
            function.Variables[local.Variable].Name == "observed");
        var returnIndex = Enumerable.Range(0, scopeStatements.Length)
            .Single(index => scopeStatements[index] is BoundReturn);
        var returnStatement = (BoundReturn)scopeStatements[returnIndex];
        var forgedScope = unsafeScope with {
            Body = unsafeScope.Body with {
                Statements = scopeStatements
                    .SetItem(writeIndex, writeStatement with {
                        Expression = new BoundUnsafeExpression(write, write.Span),
                    })
                    .SetItem(returnIndex, returnStatement with {
                        Value = new BoundRead(observed.Variable, observed.Initializer.Type,
                            returnStatement.Span),
                    }),
            },
        };
        var scopeIndex = function.Body.Statements.IndexOf(unsafeScope);
        var forgedFunction = function with {
            Body = function.Body with {
                Statements = function.Body.Statements.SetItem(scopeIndex, forgedScope),
            },
        };
        var forgedProgram = program with {
            Functions = program.Functions.SetItem(functionIndex, forgedFunction),
        };

        Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forgedProgram));
    }

    [Test]
    public void NativePointerCannotEscapeThroughIndirectIdentityCallback()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow] [Terrum.Interop.NativeCallable]
            unsafe ptr<int32> Identity(ptr<int32> pointer) { return pointer; }
            unsafe ptr<int32> Escape() {
                int32 value = 1;
                unsafe {
                    fnptr<NativeAbi, ptr<int32>, ptr<int32>> identity = &Identity;
                    return identity(&value);
                }
            }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"
            && diagnostic.Message.Contains("NativePointer", StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NativePointerCannotEscapeThroughExternalIdentityImport()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "Identity")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            extern ptr<int32> Identity(ptr<int32> pointer);
            unsafe ptr<int32> Escape() {
                int32 value = 1;
                unsafe { return Identity(&value); }
            }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"
            && diagnostic.Message.Contains("NativePointer", StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NativePointerCannotEscapeThroughExternalIdentityImportFromLocalPointer()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "Identity")]
            extern ptr<int32> Identity(ptr<int32> pointer);
            unsafe ptr<int32> Escape() {
                int32 value = 1;
                unsafe {
                    ptr<int32> pointer = &value;
                    return Identity(pointer);
                }
            }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"
            && diagnostic.Message.Contains("NativePointer", StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void ExternalIdentityImportPreservesLiveLocalBacking()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "Identity")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            extern ptr<int32> Identity(ptr<int32> pointer);
            int32 Main() {
                int32 value = 42;
                unsafe {
                    ptr<int32> result = Identity(&value);
                    return *result;
                }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void OpaqueNativePointerCannotEscapeThroughExternalIdentityImport()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "Identity")]
            extern ptr<void> Identity(ptr<int32> pointer);
            unsafe ptr<void> Escape() {
                int32 value = 1;
                unsafe { return Identity(&value); }
            }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"
            && diagnostic.Message.Contains("NativePointer", StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void OpaqueNativePointerImportPreservesLiveLocalBacking()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "Identity")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            extern ptr<void> Identity(ptr<int32> pointer);
            int32 Main() {
                int32 value = 42;
                unsafe {
                    ptr<void> result = Identity(&value);
                    return result == null ? -1 : 0;
                }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void OpaqueNativePointerCannotEscapeThroughIndirectIdentityCallback()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow] [Terrum.Interop.NativeCallable]
            unsafe ptr<void> Identity(ptr<int32> pointer) { return null; }
            unsafe ptr<void> Escape() {
                int32 value = 1;
                unsafe {
                    fnptr<NativeAbi, ptr<int32>, ptr<void>> identity = &Identity;
                    return identity(&value);
                }
            }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"
            && diagnostic.Message.Contains("NativePointer", StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void OpaqueNativePointerCannotEscapeThroughVTableIdentityMethod()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestVTable {
                public fnptr<NativeAbi, ptr<TestObject>, ptr<int32>, ptr<void>> Method;
                public TestVTable(fnptr<NativeAbi, ptr<TestObject>, ptr<int32>, ptr<void>> method) {
                    Method = method;
                }
            }

            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestObject {
                public ptr<TestVTable> VTable;
                public TestObject(ptr<TestVTable> vtable) { VTable = vtable; }
            }

            [NoThrow] [Terrum.Interop.NativeCallable]
            unsafe ptr<void> Identity(ptr<TestObject> receiver, ptr<int32> pointer) { return null; }

            [Terrum.Interop.NativeVTableMethod(0)]
            extern unsafe ptr<void> Invoke(ptr<TestObject> receiver, ptr<int32> pointer);

            unsafe ptr<void> Escape() {
                int32 value = 1;
                unsafe {
                    TestVTable vtable = new TestVTable(&Identity);
                    TestObject instance = new TestObject(&vtable);
                    return Invoke(&instance, &value);
                }
            }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"
            && diagnostic.Message.Contains("NativePointer", StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void VerifierFindsUnsafePointerInsideRewrittenCallArguments()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Identity(ptr<int32> pointer) { return 0; }
            int32 Main() {
                int32 value = 1;
                unsafe {
                    int32 result = Identity(&value);
                }
                return 0;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var main = program.Functions[program.EntryPoint];
        var unsafeBlock = main.Body.Statements.OfType<BoundUnsafe>().Single();
        var result = (BoundLocal)unsafeBlock.Body.Statements.Single();
        var call = (BoundCall)result.Initializer;
        var unsafeIndex = main.Body.Statements.IndexOf(unsafeBlock);
        var movedCall = main.Body with {
            Statements = main.Body.Statements.SetItem(unsafeIndex,
                new BoundExpressionStatement(call, call.Span)),
        };
        var forged = program with {
            Functions = program.Functions.SetItem(program.EntryPoint,
                main with { Body = movedCall }),
        };

        var exception = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
        Assert.That(exception!.Data.Values.Cast<object>(), Does.Contain("NativePointer"));
    }

    [Test]
    public void VerifierRejectsForgedAddressOfNonBlittableLocal()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct OpaquePayload {
                public int32 Value;
                public OpaquePayload(int32 value) { Value = value; }
            }
            unsafe int32 AcceptOpaque(ptr<OpaquePayload> value) { return 0; }
            int32 Main() {
                int32 scalar = 1;
                OpaquePayload opaque = new OpaquePayload(2);
                unsafe {
                    ptr<int32> scalarAddress = &scalar;
                    ptr<OpaquePayload> opaqueAddress = null;
                }
                return 0;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var main = program.Functions[program.EntryPoint];
        var unsafeBlock = main.Body.Statements.OfType<BoundUnsafe>().Single();
        var scalarAddress = (BoundNativePointerAddress)unsafeBlock.Body.Statements
            .OfType<BoundLocal>()
            .Single(local => main.Variables[local.Variable].Name == "scalarAddress")
            .Initializer;
        var opaqueLocal = main.Body.Statements.OfType<BoundLocal>()
            .Single(local => main.Variables[local.Variable].Name == "opaque");
        var opaquePointerLocal = unsafeBlock.Body.Statements.OfType<BoundLocal>()
            .Single(local => main.Variables[local.Variable].Name == "opaqueAddress");
        var forgedAddress = scalarAddress with {
            Variable = opaqueLocal.Variable,
            Type = main.Variables[opaquePointerLocal.Variable].Type,
        };
        var unsafeStatements = unsafeBlock.Body.Statements.SetItem(
            unsafeBlock.Body.Statements.IndexOf(opaquePointerLocal),
            opaquePointerLocal with { Initializer = forgedAddress });
        var forgedUnsafeBlock = unsafeBlock with {
            Body = unsafeBlock.Body with { Statements = unsafeStatements },
        };
        var forged = program with {
            Functions = program.Functions.SetItem(program.EntryPoint,
                main with {
                    Body = main.Body with {
                        Statements = main.Body.Statements.SetItem(
                            main.Body.Statements.IndexOf(unsafeBlock), forgedUnsafeBlock),
                    },
                }),
        };

        var exception = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
        Assert.That(exception!.Data.Values.Cast<object>(), Does.Contain("NativePointer"));
    }

    [Test]
    public void IndirectIdentityCallbackPreservesLiveLocalBacking()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow] [Terrum.Interop.NativeCallable]
            unsafe ptr<int32> Identity(ptr<int32> pointer) { return pointer; }
            int32 Main() {
                int32 value = 42;
                unsafe {
                    fnptr<NativeAbi, ptr<int32>, ptr<int32>> identity = &Identity;
                    ptr<int32> result = identity(&value);
                    return *result;
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void NativePointerCannotPublishFunctionLocalBackingThroughInitParameter()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct PointerHolder {
                public mut ptr<int32> Value;
                public unsafe PointerHolder(ptr<int32> value) { Value = value; }
            }
            unsafe bool Publish(init ref<PointerHolder> output) {
                int32 value = 1;
                unsafe { output = new PointerHolder(&value); }
                return true;
            }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"
            && diagnostic.Message.Contains("NativePointer", StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NativePointerCannotPublishFunctionLocalBackingThroughUnknownPointerCell()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unsafe int32 Publish(write ptr<ptr<int32>> destination) {
                int32 value = 1;
                unsafe { *destination = &value; }
                return 0;
            }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"
            && diagnostic.Message.Contains("NativePointer", StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NativePointerCannotPublishThroughConditionallyUnknownPointerCell()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unsafe int32 Publish(bool chooseLocal, write ptr<ptr<int32>> destination) {
                int32 value = 1;
                mut ptr<int32> localPointer = null;
                write ptr<ptr<int32>> localCell = unsafe(&localPointer);
                write ptr<ptr<int32>> cell =
                    chooseLocal ? localCell : destination;
                unsafe { *cell = &value; }
                return 0;
            }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"
            && diagnostic.Message.Contains("NativePointer", StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NativePointerCannotPublishKnownBackingThroughAliasAfterUnknownStore()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            alias WritablePointerCell = write ptr<ptr<int32>>;
            unsafe int32 Publish(write ptr<ptr<int32>> destination) {
                int32 value = 1;
                mut ptr<int32> localPointer = null;
                mut write ptr<ptr<int32>> holderPointer = unsafe(&localPointer);
                unsafe {
                    write ptr<WritablePointerCell> holderPointerCell = &holderPointer;
                    *holderPointerCell = destination;
                    *holderPointer = &value;
                }
                return 0;
            }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"
            && diagnostic.Message.Contains("NativePointer", StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NativePointerCannotOutliveExplicitBackingLifetimeEnd()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                int32 value = 1;
                mut ptr<int32> pointer = null;
                unsafe { pointer = &value; }
                value = _;
                unsafe { return *pointer; }
            }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"
            && diagnostic.Message.Contains("NativePointer", StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NativePointerCannotFormAddressAfterBackingLifetimeEnd()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                int32 value = 1;
                value = _;
                unsafe {
                    ptr<int32> pointer = &value;
                    return 0;
                }
            }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"
            && diagnostic.Message.Contains("NativePointer", StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NativePointerLifetimeTracksPointerStoredThroughInternalHelper()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unsafe void StorePointer(write ptr<ptr<int32>> destination, ptr<int32> value) {
                unsafe { *destination = value; }
            }
            int32 Main() {
                int32 value = 1;
                mut ptr<int32> pointer = null;
                unsafe { StorePointer(&pointer, &value); }
                value = _;
                return 0;
            }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"
            && diagnostic.Message.Contains("NativePointer", StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NativePointerLifetimeTracksPointerStoredThroughAliasedHelperParameters()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unsafe void StorePointer(write ptr<ptr<int32>> destination, ptr<int32> value) {
                write ptr<ptr<int32>> destinationAlias = destination;
                mut ptr<int32> valueAlias = null;
                valueAlias = value;
                unsafe { *destinationAlias = valueAlias; }
            }
            unsafe ptr<int32> ReturnLocalPointer() {
                int32 value = 1;
                mut ptr<int32> pointer = null;
                unsafe {
                    StorePointer(&pointer, &value);
                    return pointer;
                }
            }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"
            && diagnostic.Message.Contains("NativePointer", StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void RecursiveNativePointerWriteSummariesDoNotMutateDuringEnumeration()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unsafe void StorePointer(
                write ptr<ptr<int32>> first,
                write ptr<ptr<int32>> second,
                ptr<int32> value,
                bool recurse) {
                unsafe { *first = value; }
                if (recurse) { unsafe { StorePointer(second, first, value, false); } }
            }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void NativePointerCannotOutliveNestedLocalBacking()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut ptr<int32> pointer = null;
                {
                    int32 value = 1;
                    unsafe { pointer = &value; }
                }
                unsafe { return *pointer; }
            }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3016"
            && diagnostic.Message.Contains("NativePointer", StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NativePointerParameterPassthroughPreservesCallerBacking()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unsafe ptr<int32> Identity(ptr<int32> pointer) { return pointer; }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void PrivateModuleReaderRejectsCyclicNativePointerTypes()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public unsafe int32 AcceptScalar(ptr<int32> value) { return 0; }
            public unsafe int32 AcceptNested(ptr<ptr<int32>> value) { return 0; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var producer = ModuleCompiler.CreateProducer(compilation.Program!,
            ["AcceptScalar", "AcceptNested"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var scalarPointerType = (int)reference.Functions.Single(
            static function => function.Name == "AcceptScalar").Variables[0].Type;
        var nestedPointerType = (int)reference.Functions.Single(
            static function => function.Name == "AcceptNested").Variables[0].Type;
        var nestedPointerIndex = nestedPointerType - Math.Min(scalarPointerType, nestedPointerType);
        var tableOffset = FindPointerTableOffset(producer);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(tableOffset)), Is.EqualTo(2));
        var malformed = producer.ToArray();
        var nestedPointeeOffset = tableOffset + sizeof(int)
            + nestedPointerIndex * (sizeof(int) + sizeof(byte));
        BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(nestedPointeeOffset), nestedPointerType);

        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed));
    }

    [Test]
    public void TypedPointerAccessRequiresUnsafeAndWritablePermission()
    {
        var unscopedRead = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                ptr<int32> pointer = null;
                return *pointer;
            }
            """));
        Assert.That(unscopedRead.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3028"), Is.True,
            string.Join(Environment.NewLine, unscopedRead.Diagnostics));

        var readonlyWrite = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    ptr<int32> pointer = null;
                    *pointer = 42;
                }
                return 0;
            }
            """));
        Assert.That(readonlyWrite.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, readonlyWrite.Diagnostics));
    }

    [Test]
    public void PrivateModulesRejectNativeFixedArrayPointerAddresses()
    {
        var compilation = Compilation.Create([SyntaxTree.Parse("""
            int32 Export() {
                InlineArray<uint32, 2> values = [4, 5];
                unsafe {
                    ptr<uint32> first = &values[0];
                    return 0;
                }
            }
            int32 Main() { return 0; }
            """)], BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var exception = Assert.Throws<NotSupportedException>(() =>
            ModuleCompiler.CreateProducer(compilation.Program!, ["Export"]));
        Assert.That(exception!.Message,
            Does.Contain("Source native imports are not supported by semantic modules."));
    }

    [Test]
    public async Task NativeGraphConsumerPreservesTypedPointerContractsAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public unsafe int32 Read(ptr<int32> value) {
                unsafe { return *value; }
            }
            """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(providerCompilation.Program!,
            ["Read"], "PointerProvider", NativeRuntimePolicy.FailFast);
        var reference = NativeModuleCompiler.CreateGraphReference([provider]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init mut int32 value;
                unsafe {
                    write ptr<int32> writable = &value;
                    *writable = 42;
                    ptr<int32> readable = &value;
                    return Read(readable);
                }
            }
            """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateGraphConsumer(
            consumerCompilation.Program!, [provider], NativeRuntimePolicy.FailFast);

        NativeModuleCompiler.ValidateGraph(consumer, [provider]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(consumer, [provider],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast).ConfigureAwait(true), Is.EqualTo(42));
    }

    private static int FindPointerTableOffset(byte[] module)
    {
        using var stream = new MemoryStream(module, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        _ = reader.ReadInt32();
        Assert.That(reader.ReadInt32(), Is.EqualTo(83));
        SkipString(reader);
        _ = reader.ReadBoolean();
        _ = reader.ReadByte();
        _ = reader.ReadBoolean();
        SkipString(reader);
        Assert.That(reader.ReadInt32(), Is.Zero, "The fixture has no invocation buffers.");
        _ = reader.ReadInt32();
        Assert.That(reader.ReadInt32(), Is.Zero, "The fixture has no structures.");
        Assert.That(reader.ReadInt32(), Is.Zero, "The fixture has no classes.");
        Assert.That(reader.ReadInt32(), Is.Zero, "The fixture has no imported structures.");
        Assert.That(reader.ReadInt32(), Is.Zero, "The fixture has no function-pointer signatures.");
        return checked((int)stream.Position);
    }

    private static void SkipString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (reader.ReadBytes(length).Length != length)
        {
            throw new EndOfStreamException();
        }
    }
}
