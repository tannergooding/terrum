// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class VacantAssignmentTests
{
    [Theory]
    [InlineData("init int32 value; value = 42; return value;")]
    [InlineData("init var value; value = int32(42); return value;")]
    [InlineData("init uint64 value; value = 18446744073709551615; return int32(value >> 63) + 41;")]
    [InlineData("init int32 value; if (Pick()) { value = 42; } else { value = 42; } return value;")]
    [InlineData("init int32 value; if (Pick()) { value = 1; return 1; } value = 42; return value;")]
    [InlineData("mut int32 sum = 0; for (mut int32 i = 0; i < 2; i++) { init int32 value; value = 21; sum += value; } return sum;")]
    public void AssignmentInfersOrUsesTheFixedTypeAndPublishesOnEverySuccessfulPath(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            $"bool Pick() {{ return false; }} int32 Main() {{ {body} }}"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        SemanticVerifier.Verify(program);
        Assert.Equal(42, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData("init int32 value; return value;")]
    [InlineData("init int32 value; value = 1; value = 42; return value;")]
    [InlineData("init int32 value; if (Pick()) { value = 1; } value = 42; return value;")]
    [InlineData("init int32 value; while (Pick()) { value = 42; } return 42;")]
    [InlineData("init int32 value; Fill(init value); value = 42; return value;")]
    [InlineData("init int32 value; if (Pick()) { require Fill(init value); } require Fill(init value); return value;")]
    [InlineData("init var value; value = int32(1); value = int64(42); return 42;")]
    [InlineData("init bool value; value = FillBool(init value); return 42;")]
    [InlineData("int32 value = 1; value = 42; return value;")]
    public void AssignmentAndInitLoansRequireDefiniteVacancyNotJustAbsenceOfDefiniteInitialization(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            bool Pick() { return false; }
            bool Fill(init ref<int32> value) { value = 42; return true; }
            bool FillBool(init ref<bool> value) { value = true; return true; }
            int32 Main() { {{body}} }
            """));
        Assert.True(compilation.HasErrors);
    }

    [Fact]
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
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3008");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitializationAssignmentRoundTripsWithoutChangingTheNativeAbiAsync(bool checkedFailures)
    {
        var policy = checkedFailures ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Read() { init var value; value = int32(42); return value; }"),
            runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var producer = ModuleCompiler.CreateProducer(program, ["Read"]);
        Assert.Equal(72,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        foreach (var version in new[] { 40, 41, 42, 43, 44, 45 })
        {
            var historical = (byte[])producer.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(historical.AsSpan(4, 4), version);
            Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(historical));
        }
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Read(); }"),
            ModuleCompiler.CreateReference(producer), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.False(consumerCompilation.HasErrors, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(42, NativeJit.Execute(linked, policy));
        Assert.NotEmpty(NativeEmitter.Emit(linked, policy));

        var nativeProducer = NativeModuleCompiler.CreateProducer(program, ["Read"], "VacantAssignment", policy);
        var nativeConsumerCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Read(); }"),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.False(nativeConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, nativeConsumerCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(nativeConsumerCompilation.Program), [nativeProducer], policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
    }

    [Fact]
    public void VerifierRejectsForgedInitializationAuthorityAndOrdinaryReadonlyAssignment()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { init int32 value; value = 42; return value; }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var function = program.Functions[0];
        var slot = Assert.Single(function.Variables.Select((variable, index) => (variable, index))).index;
        var vacant = Assert.IsType<BoundVacantLocal>(function.Body.Statements[0]);
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

    [Fact]
    public void InlineOutputDeclarationCannotForgeStandaloneInitializationAuthority()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            bool Fill(init ref<int32> value) { value = 42; return true; }
            int32 Main() { if (Fill(init var value)) { return value; } return 0; }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var functionIndex = program.Functions.Select((function, index) => (function, index))
            .Single(item => item.function.Name == "Main").index;
        var function = program.Functions[functionIndex];
        var slot = Assert.Single(function.Variables.Select((variable, index) => (variable, index))).index;
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
