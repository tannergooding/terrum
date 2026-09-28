// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeMutableBufferViewTests
{
    private const string Owner = """
        int32 Main() {
            require OwnedInt32Buffer.TryCreate(2, out var created);
            mut OwnedInt32Buffer buffer = move created;
            require buffer.TryAppend(40);
            require buffer.TryAppend(1);
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ExclusiveViewWritesAndClearReusesTheOwnerAsync(BoundRuntimePolicy policy)
    {
        const string Source = Owner + """
            mut int32 answer = 0;
            {
                mut MutableInt32BufferView window = buffer.SliceMutable(0, buffer.Count);
                window.Set(1, window.Read(1) + 1);
                answer = window.Read(0) + window.Read(1);
            }
            if (buffer.Read(1) != 2) { return 0; }
            buffer.Clear();
            if (buffer.Count != 0 || buffer.Capacity != 2) { return 0; }
            require buffer.TryAppend(answer);
            return buffer.Read(0);
        }
        """;
        Assert.Equal(42, await RunAsync(Source, policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ExclusiveViewHandlesBranchesLoopsAndReturnCleanupAsync(BoundRuntimePolicy policy)
    {
        const string Source = Owner + """
            mut int32 index = 0;
            mut MutableInt32BufferView window = buffer.SliceMutable(0, 2);
            while (index < window.Length) {
                if (index == 1) { window.Set(index, 42); }
                index = index + 1;
            }
            try { return window.Read(1); }
            finally { int32 length = window.Length; }
        }
        """;
        Assert.Equal(42, await RunAsync(Source, policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ViewSetEvaluatesIndexAndValueOnceInOrderAsync(BoundRuntimePolicy policy)
    {
        const string Source = """
            int32 Observe(bool appended, int32 count) { return count; }
            """ + Owner + """
            require OwnedInt32Buffer.TryCreate(4, out var createdOther);
            mut OwnedInt32Buffer other = move createdOther;
            {
                mut MutableInt32BufferView window = buffer.SliceMutable(0, 2);
                window.Set(Observe(other.TryAppend(1), other.Count),
                    Observe(other.TryAppend(2), other.Count) + 38);
                if (other.Count != 2) { return 0; }
                if (window.Read(0) != 40 || window.Read(1) != 40) { return 0; }
            }
            return buffer.Read(1) + other.Count;
        }
        """;
        Assert.Equal(42, await RunAsync(Source, policy).ConfigureAwait(true));
    }

    [Fact]
    public async Task CheckedWriteArgumentFailurePreservesOwnerAndActiveViewAsync()
    {
        const string Source = """
            int32 Overflow(int32 value) { return value + 1; }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(3, out var created);
                mut OwnedInt32Buffer buffer = move created;
                require buffer.TryAppend(40);
                require buffer.TryAppend(1);
                mut int32 caught = 0;
                unsafe {
                    try { buffer.Set(1, Overflow(int32.MaxValue)); }
                    catch { caught = caught + 1; }
                    try { buffer.Set(Overflow(int32.MaxValue), 99); }
                    catch { caught = caught + 1; }
                    try { buffer.TryAppend(Overflow(int32.MaxValue)); }
                    catch { caught = caught + 1; }
                }
                if (caught != 3 || buffer.Count != 2
                    || buffer.Read(0) != 40 || buffer.Read(1) != 1) { return 0; }
                {
                    mut MutableInt32BufferView window = buffer.SliceMutable(0, 2);
                    unsafe {
                        try { window.Set(1, Overflow(int32.MaxValue)); }
                        catch { caught = caught + 1; }
                        try { window.Set(Overflow(int32.MaxValue), 99); }
                        catch { caught = caught + 1; }
                    }
                    if (caught != 5 || window.Read(0) != 40 || window.Read(1) != 1) { return 0; }
                    window.Set(1, 2);
                }
                return buffer.Read(0) + buffer.Read(1);
            }
            """;
        Assert.Equal(42, await RunAsync(Source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Fact]
    public void ClearOnLiveOwnerCanBeNoThrow()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow] void Reset(OwnedInt32Buffer owner) {
                mut OwnedInt32Buffer value = move owner;
                value.Clear();
            }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var created);
                Reset(move created);
                return 42;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal("native-17", Compilation.GetNativeProfileVersion(
            Assert.IsType<BoundProgram>(compilation.Program)));
    }

    [Fact]
    public void UnusedClearIntrinsicDoesNotPromoteOwnedBufferProgram()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Owner + """
            return buffer.Read(0) + buffer.Read(1);
        }
        """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal("native-15", Compilation.GetNativeProfileVersion(program));
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Size(OwnedInt32Buffer owner) {
                mut OwnedInt32Buffer buffer = move owner;
                return buffer.Count;
            }
            """));
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program), ["Size"]);
        Assert.Equal("native-15", ModuleCompiler.GetNativeProfileVersion(producer, false));
    }

    [Fact]
    public void ClearOnlyProducerRetainsNative17()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Reset(OwnedInt32Buffer owner) {
                mut OwnedInt32Buffer buffer = move owner;
                buffer.Clear();
                return buffer.Count;
            }
            """));
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program), ["Reset"]);
        Assert.Equal("native-17", ModuleCompiler.GetNativeProfileVersion(producer, false));
    }

    [Theory]
    [InlineData("buffer.Read(0);")]
    [InlineData("int32 count = buffer.Count;")]
    [InlineData("buffer.Set(0, 41);")]
    [InlineData("buffer.Clear();")]
    [InlineData("OwnedInt32Buffer moved = move buffer;")]
    [InlineData("OwnedInt32Buffer.TryCreate(1, out buffer);")]
    [InlineData("ReadonlyInt32BufferView other = buffer.Slice(0, 1);")]
    [InlineData("mut MutableInt32BufferView other = buffer.SliceMutable(0, 1);")]
    [InlineData("if (false) { buffer.Clear(); }")]
    [InlineData("try { return window.Read(0); } finally { buffer.Clear(); }")]
    public void ExclusiveViewRejectsOwnerAccessAndOverlappingViews(string operation)
    {
        var source = Owner + " mut MutableInt32BufferView window = buffer.SliceMutable(0, 2); "
            + operation + " return window.Read(0); }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("MutableInt32BufferView window = buffer.SliceMutable(0, 1);")]
    [InlineData("mut MutableInt32BufferView window = buffer.Slice(0, 1);")]
    [InlineData("mut MutableInt32BufferView window = buffer.SliceMutable(0, 1); MutableInt32BufferView copy = window;")]
    public void WritableViewRequiresMutableNamedOwnerAndCannotBeCopied(string declaration)
    {
        var source = Owner + declaration + " return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
    }

    [Fact]
    public void ImmutableOwnerCannotAcquireWritableView()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var buffer);
                mut MutableInt32BufferView window = buffer.SliceMutable(0, 0);
                return 42;
            }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
    }

    [Theory]
    [InlineData("buffer.SliceMutable(-1, 0)")]
    [InlineData("buffer.SliceMutable(2, 1)")]
    [InlineData("buffer.SliceMutable(0, -1)")]
    public async Task MutableSliceRejectsInvalidInitializedPrefixAsync(string range)
    {
        var source = Owner + " mut MutableInt32BufferView window = " + range
            + "; return window.Length; }";
        Assert.Equal(unchecked((int)0xE0540003),
            await RunAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("2")]
    public async Task ViewSetChecksItsOwnRangeAsync(string index)
    {
        var source = Owner + " mut MutableInt32BufferView window = buffer.SliceMutable(0, 2); "
            + "window.Set(" + index + ", 42); return 0; }";
        Assert.Equal(unchecked((int)0xE0540003),
            await RunAsync(source, BoundRuntimePolicy.FailFast).ConfigureAwait(true));
    }

    [Fact]
    public async Task CheckedViewSetFailureUnwindsBeforeOwnerReuseAsync()
    {
        const string Source = Owner + """
            unsafe {
                try {
                    mut MutableInt32BufferView window = buffer.SliceMutable(0, 1);
                    window.Set(1, 42);
                }
                catch {
                    buffer.Clear();
                    require buffer.TryAppend(42);
                    return buffer.Read(0);
                }
            }
            return 0;
        }
        """;
        Assert.Equal(42, await RunAsync(Source, BoundRuntimePolicy.CheckedExceptions).ConfigureAwait(true));
    }

    [Fact]
    public void MutableViewRejectsOutReplacementDuringAcquisitionOrSet()
    {
        const string Helpers = """
            bool Replace(out OwnedInt32Buffer target) { return OwnedInt32Buffer.TryCreate(2, out target); }
            int32 Observe(bool value) { return 0; }
            """;
        foreach (var statement in new[] {
            "mut MutableInt32BufferView window = buffer.SliceMutable(Observe(Replace(out buffer)), 1);",
            "mut MutableInt32BufferView window = buffer.SliceMutable(0, 1); window.Set(Observe(Replace(out buffer)), 42);",
        })
        {
            var compilation = Compilation.Create(SyntaxTree.Parse(Helpers + Owner + statement + " return 42; }"));
            Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3023");
        }
    }

    [Fact]
    public void ForgedIrCannotReadReservedOwnerDuringWritableView()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Owner + """
            mut MutableInt32BufferView window = buffer.SliceMutable(0, 2);
            return window.Read(0);
        }
        """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var entry = program.Functions[program.EntryPoint];
        var owner = Assert.Single(entry.Variables, static variable => variable.Name == "buffer");
        var count = program.Functions.IndexOf(Assert.Single(program.Functions,
            static function => function.Name == "OwnedInt32Buffer.Count"));
        var changed = false;

        BoundStatement Rewrite(BoundStatement statement)
        {
            return statement switch {
                BoundBlock block => block with { Statements = [.. block.Statements.Select(Rewrite)] },
                BoundTryFinally cleanup => cleanup with { Body = (BoundBlock)Rewrite(cleanup.Body) },
                BoundViewScope view => InsertRead(view),
                _ => statement,
            };
        }

        BoundStatement InsertRead(BoundViewScope view)
        {
            changed = true;
            var loan = new BoundReceiverLoan(entry.Variables.IndexOf(owner),
                PrimitiveType.OwnedInt32Buffer, view.Span);
            var read = new BoundExpressionStatement(new BoundCall(count, [loan], PrimitiveType.Int32, view.Span),
                view.Span);
            return view with { Body = view.Body with { Statements = [read, .. view.Body.Statements] } };
        }

        var forged = program with {
            Functions = program.Functions.SetItem(program.EntryPoint,
                entry with { Body = (BoundBlock)Rewrite(entry.Body) }),
        };
        Assert.True(changed);
        var error = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
        Assert.Equal("Ownership", error.Data["Terrum.InvalidEffect"]);
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void ForgedIrCannotSetThroughAnUnrelatedSlot()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Owner + """
            mut MutableInt32BufferView window = buffer.SliceMutable(0, 2);
            window.Set(0, 42);
            return window.Read(0);
        }
        """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var entry = program.Functions[program.EntryPoint];
        var modified = false;

        BoundStatement Rewrite(BoundStatement statement)
        {
            return statement switch {
                BoundBlock block => block with { Statements = [.. block.Statements.Select(Rewrite)] },
                BoundTryFinally cleanup => cleanup with { Body = (BoundBlock)Rewrite(cleanup.Body) },
                BoundViewScope view => view with { Body = (BoundBlock)Rewrite(view.Body) },
                BoundViewSet write => Corrupt(write),
                _ => statement,
            };
        }

        BoundViewSet Corrupt(BoundViewSet write)
        {
            modified = true;
            return write with { Variable = 0 };
        }

        var forged = program with {
            Functions = program.Functions.SetItem(program.EntryPoint,
                entry with { Body = (BoundBlock)Rewrite(entry.Body) }),
        };
        Assert.True(modified);
        var error = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
        Assert.Equal("Ownership", error.Data["Terrum.InvalidEffect"]);
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SemanticModuleRetainsExclusiveScopeAndClearAsync(BoundRuntimePolicy policy)
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Update(OwnedInt32Buffer owner) {
                mut OwnedInt32Buffer buffer = move owner;
                {
                    mut MutableInt32BufferView window = buffer.SliceMutable(0, buffer.Count);
                    window.Set(0, 42);
                }
                int32 result = buffer.Read(0);
                buffer.Clear();
                return result;
            }
            """), policy);
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program), ["Update"]);
        Assert.Equal("native-17", ModuleCompiler.GetNativeProfileVersion(producer, false));
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var created);
                mut OwnedInt32Buffer buffer = move created;
                require buffer.TryAppend(0);
                return Update(move buffer);
            }
            """), ModuleCompiler.CreateReference(producer), runtimePolicy: policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal("native-15", ModuleCompiler.GetNativeProfileVersion(consumer, true));
        Assert.Equal(42, await RunAsync(ModuleCompiler.Link(consumer, producer), policy).ConfigureAwait(true));
    }

    private static async Task<int> RunAsync(string source, BoundRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return await RunAsync(Assert.IsType<BoundProgram>(compilation.Program), policy).ConfigureAwait(true);
    }

    private static async Task<int> RunAsync(BoundProgram program, BoundRuntimePolicy policy)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var nativePolicy = policy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        var path = Path.Combine(Directory.GetCurrentDirectory(), $"terrum-mutable-view-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, nativePolicy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Mutable view test process did not start.");
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
