// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class VacantAssignmentTests
{
    [TestCase("init int32 value; value = 42; return value;")]
    [TestCase("init var value; value = int32(42); return value;")]
    [TestCase("init uint64 value; value = 18446744073709551615; return int32(value >> 63) + 41;")]
    [TestCase("init int32 value; if (Pick()) { value = 42; } else { value = 42; } return value;")]
    [TestCase("init int32 value; if (Pick()) { value = 1; return 1; } value = 42; return value;")]
    [TestCase("mut int32 sum = 0; for (mut int32 i = 0; i < 2; i++) { init int32 value; value = 21; sum += value; } return sum;")]
    public void AssignmentInfersOrUsesTheFixedTypeAndPublishesOnEverySuccessfulPath(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            $"bool Pick() {{ return false; }} int32 Main() {{ {body} }}"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        SemanticVerifier.Verify(program);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }
    [TestCase("init int32 value; return value;")]
    [TestCase("init int32 value; value = 1; value = 42; return value;")]
    [TestCase("init int32 value; if (Pick()) { value = 1; } value = 42; return value;")]
    [TestCase("init int32 value; while (Pick()) { value = 42; } return 42;")]
    [TestCase("init int32 value; Fill(init value); value = 42; return value;")]
    [TestCase("init int32 value; if (Pick()) { require Fill(init value); } require Fill(init value); return value;")]
    [TestCase("init var value; value = int32(1); value = int64(42); return 42;")]
    [TestCase("init bool value; value = FillBool(init value); return 42;")]
    [TestCase("int32 value = 1; value = 42; return value;")]
    public void AssignmentAndInitLoansRequireDefiniteVacancyNotJustAbsenceOfDefiniteInitialization(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            bool Pick() { return false; }
            bool Fill(init ref<int32> value) { value = 42; return true; }
            bool FillBool(init ref<bool> value) { value = true; return true; }
            int32 Main() { {{body}} }
            """));
        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void OrdinaryOwnerCannotGainInitializationAuthorityAfterMovingItsValue()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public int32 Value;
                public Owner() { Value = 42; }
                [NoThrow] [Cleanup] public void Close() { }
            }
            int32 Main() {
                Owner owner = new Owner();
                Owner moved = move owner;
                owner = new Owner();
                return moved.Value;
            }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3008"),Is.True);
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task InitializationAssignmentRoundTripsWithoutChangingTheNativeAbiAsync(bool checkedFailures)
    {
        var policy = checkedFailures ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Read() { init var value; value = int32(42); return value; }"),
            runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        var producer = ModuleCompiler.CreateProducer(program, ["Read"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(76));
        foreach (var version in new[] { 40, 41, 42, 43, 44, 45 })
        {
            var historical = (byte[])producer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(historical.AsSpan(4, 4), version);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(historical));
        }
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Read(); }"),
            ModuleCompiler.CreateReference(producer), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = consumerCompilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(NativeJit.Execute(linked, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(linked, policy), Is.Not.Empty);

        var nativeProducer = NativeModuleCompiler.CreateProducer(program, ["Read"], "VacantAssignment", policy);
        var nativeConsumerCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Read(); }"),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.That(nativeConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, nativeConsumerCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = nativeConsumerCompilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))(), [nativeProducer], policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public void VerifierRejectsForgedInitializationAuthorityAndOrdinaryReadonlyAssignment()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { init int32 value; value = 42; return value; }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
        var function = program.Functions[0];
        var slot = global::System.Linq.Enumerable.Single(function.Variables.Select((variable, index) => (variable, index))).index;
        var vacant = ((global::System.Func<BoundVacantLocal>)(() => { var __nunitActual5 = function.Body.Statements[0]; Assert.That(__nunitActual5, Is.TypeOf<BoundVacantLocal>()); return (BoundVacantLocal)__nunitActual5!; }))();
        var forged = function with {
            Body = function.Body with {
                Statements = function.Body.Statements.SetItem(0,
                    new BoundLocal(slot, new BoundConstant(0, PrimitiveType.Int32, vacant.Span), vacant.Span)),
            },
        };
        Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(0, forged),
        }));
        var noAuthority = function with {
            Variables = function.Variables.SetItem(slot, function.Variables[slot] with {
                IsInitializationLocal = false,
            }),
        };
        Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(0, noAuthority),
        }));
    }

    [Test]
    public void InlineOutputDeclarationCannotForgeStandaloneInitializationAuthority()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            bool Fill(init ref<int32> value) { value = 42; return true; }
            int32 Main() { if (Fill(init var value)) { return value; } return 0; }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = compilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))();
        var functionIndex = program.Functions.Select((function, index) => (function, index))
            .Single(item => item.function.Name == "Main").index;
        var function = program.Functions[functionIndex];
        var slot = global::System.Linq.Enumerable.Single(function.Variables.Select((variable, index) => (variable, index))).index;
        var forged = function with {
            Variables = function.Variables.SetItem(slot, function.Variables[slot] with {
                IsInitializationLocal = true,
            }),
        };
        Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(functionIndex, forged),
        }));
    }
}
