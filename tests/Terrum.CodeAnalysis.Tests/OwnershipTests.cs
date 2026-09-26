// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class OwnershipTests
{
    private const string Owner = """
        unique struct Owner {
            public int32 Id;
            public Owner(int32 id) { Id = id; }
            [NoThrow] [Cleanup] public void Close() {
                Host.Trace[0] = Host.Trace[0] ^ Id;
                Host.Trace[1] = Id;
            }
            [NoThrow] public int32 Peek() { return Id; }
        }
        """;

    [Theory]
    [InlineData("Owner copy = value;", "TRM3023")]
    [InlineData("Owner copy = move value; Owner duplicate = move value;", "TRM3023")]
    [InlineData("Owner copy = move value; return value.Id;", "TRM3023")]
    [InlineData("if (flag) { Owner gone = move value; } return value.Id;", "TRM3023")]
    [InlineData("value.Close();", "TRM3023")]
    [InlineData("return new Owner(42).Id;", "TRM3023")]
    public void UniqueOwnersCannotBeCopiedReadVacantOrManuallyCleaned(string body, string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Owner
            + " int32 Main() { bool flag = true; Owner value = new Owner(42); " + body + " return 0; }"),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0, 0], true)]);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("struct Copy { public int32 Id; public Copy() { Id = 1; } [NoThrow] [Cleanup] public void Close() {} }", "TRM3023")]
    [InlineData("unique struct U { public int32 Id; public U() { Id = 1; } [Cleanup] public void Close() {} }", "TRM3023")]
    [InlineData("unique struct U { public int32 Id; public U() { Id = 1; } } struct Copy { public U Value; public Copy() { Value = new U(); } }", "TRM3023")]
    [InlineData("unique struct U { public int32 Id; public U() { Id = 1; } } void Take(U value) {} ", "TRM3023")]
    [InlineData("unique struct U { public int32 Id; public U() { Id = 1; U duplicate = this; } }", "TRM3023")]
    public void UnsupportedOwnershipContractsAreDiagnosed(string declarations, string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(declarations + " int32 Main() { return 0; }"));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Theory]
    [InlineData("Owner first = new Owner(1); Owner second = new Owner(2);", 3, 1)]
    [InlineData("Owner first = new Owner(1); Owner second = move first;", 1, 1)]
    [InlineData("mut Owner first = new Owner(1); first = new Owner(2);", 3, 2)]
    public async Task NormalCleanupRunsOnceInReverseLexicalOrderAsync(string body, int mask, int last)
    {
        var source = Owner + " int32 Main() { { " + body
            + " } if (Host.Trace[0] == " + mask + " && Host.Trace[1] == " + last + ") { return 42; } return 0; }";
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true));
    }

    [Fact]
    public async Task ReturningScalarSnapshotsBeforeDroppingOwnerAsync()
    {
        var source = Owner + """
            int32 Take() {
                Owner value = new Owner(1);
                return value.Peek();
            }
            int32 Main() {
                int32 result = Take();
                if (result == 1 && Host.Trace[0] == 1 && Host.Trace[1] == 1) {
                    return 42;
                }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true));
    }

    [Fact]
    public async Task ConstructorDropsLocalOwnerBeforeImplicitReturnAsync()
    {
        var source = Owner + """
            struct Result {
                public int32 Id;
                public Result() {
                    Owner local = new Owner(1);
                    Id = 42;
                }
            }
            int32 Main() {
                Result result = new Result();
                if (result.Id == 42 && Host.Trace[0] == 1) { return 42; }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true));
    }

    [Fact]
    public async Task IndirectOwnerReturnTransfersCleanupToCallerAsync()
    {
        var source = """
            unique struct Token {
                public int32 First;
                public int32 Second;
                public int32 Third;
                public Token(int32 value) {
                    First = value;
                    Second = value + 1;
                    Third = value + 2;
                }
                [NoThrow] [Cleanup] public void Close() {
                    Host.Trace[0] = Host.Trace[0] ^ First;
                    Host.Trace[1] = Third;
                }
            }
            Token Make() {
                Token value = new Token(40);
                return move value;
            }
            int32 Main() {
                {
                    Token value = Make();
                    if (value.Third != 42 || Host.Trace[0] != 0) { return 0; }
                }
                if (Host.Trace[0] == 40 && Host.Trace[1] == 42) { return 42; }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BranchMoveRetainsExactlyOneCleanupObligationAsync(bool move)
    {
        var source = Owner + " int32 Main() { bool choose = " + (move ? "true" : "false") + ";"
            + " { Owner value = new Owner(1); if (choose) { Owner destination = move value; } }"
            + " if (Host.Trace[0] == 1 && Host.Trace[1] == 1) { return 42; } return 0; }";
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Fact]
    public async Task LoopInitializerOwnerDropsAfterLastIterationAsync()
    {
        var source = Owner + """
            int32 Main() {
                for (Owner value = new Owner(1); false;) {
                    require false;
                }
                if (Host.Trace[0] == 1) { return 42; }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true));
    }

    [Fact]
    public async Task CheckedUnwindDropsInnerThenOuterBeforeRecoveryAsync()
    {
        var source = Owner + """
            int32 Main() {
                unsafe {
                    try {
                        Owner outer = new Owner(1);
                        Owner inner = new Owner(2);
                        require inner.Peek() == 0;
                    } catch {
                        if (Host.Trace[0] == 3 && Host.Trace[1] == 1) { return 42; }
                        return 0;
                    }
                }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Fact]
    public async Task CheckedCrossFunctionUnwindDropsOwnersBeforeCallerRecoveryAsync()
    {
        var source = Owner + """
            void Fail() {
                Owner outer = new Owner(1);
                Owner inner = new Owner(2);
                require false;
            }
            int32 Main() {
                unsafe {
                    try { Fail(); } catch {
                        if (Host.Trace[0] == 3 && Host.Trace[1] == 1) { return 42; }
                        return 0;
                    }
                }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Fact]
    public async Task FailedReplacementLeavesOriginalOwnerForExceptionalCleanupAsync()
    {
        var source = """
            unique struct Token {
                public int32 Id;
                public Token(int32 id) {
                    require id != 2;
                    Id = id;
                }
                [NoThrow] [Cleanup] public void Close() {
                    Host.Trace[0] = Host.Trace[0] ^ Id;
                    Host.Trace[1] = Id;
                }
            }
            int32 Main() {
                unsafe {
                    try {
                        mut Token value = new Token(1);
                        value = new Token(2);
                    } catch {
                        if (Host.Trace[0] == 1 && Host.Trace[1] == 1) { return 42; }
                    }
                }
                return 0;
            }
            """;
        Assert.Equal(42, await RunNativeAsync(source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Fact]
    public void PublicIrCannotOmitLexicalDropOrForgeOwnershipLoan()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public int32 Id;
                public Owner(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Main() { Owner owner = new Owner(42); return owner.Id; }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var entry = program.Functions[program.EntryPoint];
        var truncated = entry with { Body = entry.Body with { Statements = entry.Body.Statements.RemoveAt(1) } };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, truncated),
        }));
        var earlyDrop = entry with {
            Body = entry.Body with {
                Statements = entry.Body.Statements.Add(new BoundAssignment(0,
                new BoundCall(0, [new BoundConstant(1, PrimitiveType.Int32, default)],
                    (PrimitiveType)100, default), default)),
            }
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, earlyDrop),
        }));
        var forged = entry with {
            Body = entry.Body with {
                Statements = entry.Body.Statements.SetItem(0, new BoundLocal(0,
                new BoundReceiverLoan(0, (PrimitiveType)100, default), default)),
            }
        };
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, forged),
        }, NativeRuntimePolicy.FailFast));
        var unboundCleanup = program with {
            Structures = program.Structures.SetItem(0, program.Structures[0] with { CleanupMethod = "Owner.Missing" }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(unboundCleanup));
    }

    [Theory]
    [InlineData("then")]
    [InlineData("else")]
    [InlineData("while")]
    public void PublicIrRequiresCleanupForBareUniqueDeclarations(string placement)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public int32 Id;
                public Owner(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Main() { Owner owner = new Owner(42); return owner.Id; }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var entry = program.Functions[program.EntryPoint];
        var local = Assert.IsType<BoundLocal>(entry.Body.Statements[0]);
        var region = Assert.IsType<BoundTryFinally>(entry.Body.Statements[1]);
        var result = Assert.Single(Assert.IsType<BoundBlock>(region.Body).Statements);
        BoundStatement bare = placement switch {
            "then" => new BoundIf(new BoundConstant(1, PrimitiveType.Bool, default), local, null, default),
            "else" => new BoundIf(new BoundConstant(0, PrimitiveType.Bool, default),
                new BoundReturn(new BoundConstant(0, PrimitiveType.Int32, default), default), local, default),
            "while" => new BoundWhile(new BoundConstant(1, PrimitiveType.Bool, default), local, default),
            _ => throw new InvalidOperationException(),
        };
        var body = entry.Body with { Statements = placement == "while" ? [bare] : [bare, result] };
        var forged = program with {
            Functions = program.Functions.SetItem(program.EntryPoint, entry with { Body = body }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicIrRejectsWritableOrOutUniqueReceiver(bool isOut)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public mut int32 Id;
                public Owner(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
                public int32 Peek() { return Id; }
                public bool Ready() { return false; }
            }
            int32 Main() { Owner owner = new Owner(42); return owner.Peek(); }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var index = -1;
        for (var i = 0; i < program.Functions.Length; i++)
        {
            if (program.Functions[i].Name == (isOut ? "Owner.Ready" : "Owner.Peek"))
            {
                index = i;
                break;
            }
        }
        Assert.True(index >= 0);
        var function = program.Functions[index];
        var receiver = function.Variables[0] with { IsMutable = !isOut, IsOut = isOut };
        var body = isOut ? function.Body : function.Body with {
            Statements = [new BoundFieldAssignment(0, 0,
                new BoundConstant(1, PrimitiveType.Int32, default), default), .. function.Body.Statements],
        };
        var forged = program with {
            Functions = program.Functions.SetItem(index, function with {
                Variables = function.Variables.SetItem(0, receiver),
                Body = body,
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public async Task UniqueCleanupContractSurvivesSeparateNativeModulesAsync()
    {
        const string ProviderSource = """
            public unique struct Token {
                public int32 Id;
                public Token(int32 id) { Id = id; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            public Token Make(int32 id) { return new Token(id); }
            """;
        var producerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(ProviderSource));
        Assert.False(producerCompilation.HasErrors, string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producerCompilation.Program),
            ["Make", "Token.Close"], "unique_owner", NativeRuntimePolicy.FailFast);
        var reference = NativeModuleCompiler.CreateReference(producer);
        var client = Compilation.Create(SyntaxTree.Parse("int32 Main() { Token token = Make(42); return token.Id; }"), reference);
        Assert.False(client.HasErrors, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(client.Program), producer,
            NativeRuntimePolicy.FailFast);
        Assert.Equal(42, await NativeModuleCompiler.RunAsync(consumer, producer, TimeSpan.FromSeconds(10),
            NativeRuntimePolicy.FailFast).ConfigureAwait(true));
        var replacementCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            ProviderSource.Replace("new Token(id);", "new Token(id + 1);", StringComparison.Ordinal)));
        Assert.False(replacementCompilation.HasErrors);
        var replacement = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(replacementCompilation.Program),
            ["Make", "Token.Close"], "unique_owner", NativeRuntimePolicy.FailFast);
        Assert.Equal(43, await NativeModuleCompiler.RunAsync(consumer, replacement, TimeSpan.FromSeconds(10),
            NativeRuntimePolicy.FailFast).ConfigureAwait(true));

        var incompatibleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            ProviderSource.Replace("Close()", "Release()", StringComparison.Ordinal)));
        Assert.False(incompatibleCompilation.HasErrors);
        var incompatible = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(incompatibleCompilation.Program),
            ["Make", "Token.Release"], "unique_owner", NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.Validate(consumer, incompatible));
    }

    private static async Task<int> RunNativeAsync(string source, BoundRuntimePolicy policy)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0, 0], true)], policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        var image = NativeEmitter.Emit(Assert.IsType<BoundProgram>(compilation.Program), nativePolicy);
        var path = Path.Combine(Directory.GetCurrentDirectory(), $"terrum-owner-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Native process did not start.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }
                throw;
            }
            return process.ExitCode;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
