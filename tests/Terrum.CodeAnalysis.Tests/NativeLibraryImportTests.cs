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

public sealed class NativeLibraryImportTests
{
    private const string Leaf = "public int32 Leaf() { return 40; }";
    private const string Middle = "public int32 Middle() { return Leaf() + 1; }";
    private const string Main = "int32 Main() { return Middle() + 1; }";

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ExecutableImportsDependentLibraryAndLeafLibraryAsync(BoundRuntimePolicy policy)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var leaf = Compilation.CreateLibrary(SyntaxTree.Parse(Leaf), policy);
        Assert.False(leaf.HasErrors, string.Join(Environment.NewLine, leaf.Diagnostics));
        var leafProgram = Assert.IsType<BoundProgram>(leaf.Program);
        var leafContract = ModuleCompiler.CreateProducer(leafProgram, ["Leaf"]);
        var leafReference = ModuleCompiler.CreateReference(leafContract);
        var middle = Compilation.CreateLibrary([SyntaxTree.Parse(
            "public int32 Forward() { return Leaf(); }"), SyntaxTree.Parse(
            "public int32 Middle() { return Forward() + 1; }")], leafReference, policy);
        Assert.False(middle.HasErrors, string.Join(Environment.NewLine, middle.Diagnostics));
        var middleProgram = Assert.IsType<BoundProgram>(middle.Program);
        Assert.Equal(-1, middleProgram.EntryPoint);
        var nativePolicy = (NativeRuntimePolicy)policy;
        var binding = new NativeImportBinding("Leaf", "bridge-leaf.dll", "Leaf");
        var middleImage = NativeEmitter.EmitLibraryWithImports(middleProgram, ["Middle"], [binding], nativePolicy);
        Assert.Equal(middleImage, NativeEmitter.EmitLibraryWithImports(middleProgram, ["Middle"], [binding], nativePolicy));

        var application = Compilation.Create([SyntaxTree.Parse(Middle), SyntaxTree.Parse(Main)],
            leafReference, policy);
        Assert.False(application.HasErrors, string.Join(Environment.NewLine, application.Diagnostics));
        var applicationProgram = Assert.IsType<BoundProgram>(application.Program);
        var main = Assert.Single(applicationProgram.Functions, static function => function.Name == "Main");
        var published = Assert.Single(applicationProgram.Functions, static function => function.Name == "Middle");
        var result = Assert.IsType<BoundReturn>(Assert.Single(main.Body.Statements));
        var sum = Assert.IsType<BoundBinary>(result.Value);
        var call = Assert.IsType<BoundCall>(sum.Left);
        Assert.Equal(published.Name, applicationProgram.Functions[call.Function].Name);
        var imported = published with { IsExternal = true, Body = new BoundBlock([], published.Body.Span) };
        var entry = main with {
            Body = main.Body with {
                Statements = [result with { Value = sum with { Left = call with { Function = 1 } } }],
            },
        };
        var executable = applicationProgram with { Functions = [entry, imported], EntryPoint = 0 };
        var executableImage = NativeEmitter.EmitWithImports(executable,
            [new NativeImportBinding("Middle", "bridge-middle.dll", "Middle")], nativePolicy);

        var directory = Directory.CreateTempSubdirectory("terrum-library-imports-").FullName;
        var leafPath = Path.Combine(directory, "bridge-leaf.dll");
        var middlePath = Path.Combine(directory, "bridge-middle.dll");
        var executablePath = Path.Combine(directory, "bridge.exe");
        try
        {
            await File.WriteAllBytesAsync(leafPath,
                NativeEmitter.EmitLibrary(leafProgram, ["Leaf"], nativePolicy)).ConfigureAwait(true);
            await File.WriteAllBytesAsync(middlePath, middleImage).ConfigureAwait(true);
            await File.WriteAllBytesAsync(executablePath, executableImage).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(executablePath) {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory,
            }) ?? throw new InvalidOperationException("Native executable did not start.");
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
            Assert.Equal(42, process.ExitCode);
        }
        finally
        {
            File.Delete(executablePath);
            File.Delete(middlePath);
            File.Delete(leafPath);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void LibraryReferencesRespectSourceSetsAndRuntimePolicy()
    {
        var leaf = Compilation.CreateLibrary(SyntaxTree.Parse(Leaf), BoundRuntimePolicy.CheckedExceptions);
        Assert.False(leaf.HasErrors);
        var reference = ModuleCompiler.CreateReference(ModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(leaf.Program), ["Leaf"]));
        var single = Compilation.CreateLibrary(SyntaxTree.Parse(Middle), reference,
            BoundRuntimePolicy.CheckedExceptions);
        Assert.False(single.HasErrors, string.Join(Environment.NewLine, single.Diagnostics));
        var multiple = Compilation.CreateLibrary([SyntaxTree.Parse(Middle),
            SyntaxTree.Parse("public int32 Relay() { return Middle(); }")],
            reference, BoundRuntimePolicy.CheckedExceptions);
        Assert.False(multiple.HasErrors, string.Join(Environment.NewLine, multiple.Diagnostics));
        Assert.Equal(-1, Assert.IsType<BoundProgram>(multiple.Program).EntryPoint);
        Assert.Contains(Assert.IsType<BoundProgram>(multiple.Program).Functions,
            static function => function.Name == "Leaf" && function.IsExternal);
        var mismatch = Compilation.CreateLibrary(SyntaxTree.Parse(Middle), reference);
        Assert.Contains(mismatch.Diagnostics, static diagnostic => diagnostic.Code == "TRM3022");
        Assert.Null(mismatch.Program);
        var multipleMismatch = Compilation.CreateLibrary([SyntaxTree.Parse(Middle)], reference,
            BoundRuntimePolicy.FailFast);
        Assert.Contains(multipleMismatch.Diagnostics, static diagnostic => diagnostic.Code == "TRM3022");
        Assert.Null(multipleMismatch.Program);

        var fastLeaf = Compilation.CreateLibrary(SyntaxTree.Parse(Leaf));
        Assert.False(fastLeaf.HasErrors);
        var fastReference = ModuleCompiler.CreateReference(ModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(fastLeaf.Program), ["Leaf"]));
        var defaultSingle = Compilation.CreateLibrary(SyntaxTree.Parse(Middle), fastReference);
        var defaultMultiple = Compilation.CreateLibrary([SyntaxTree.Parse(Middle)], fastReference);
        Assert.False(defaultSingle.HasErrors, string.Join(Environment.NewLine, defaultSingle.Diagnostics));
        Assert.False(defaultMultiple.HasErrors, string.Join(Environment.NewLine, defaultMultiple.Diagnostics));
    }

    [Fact]
    public void DependentLibraryRejectsMalformedIrExportsAndImportBindings()
    {
        var leaf = Compilation.CreateLibrary(SyntaxTree.Parse(Leaf));
        Assert.False(leaf.HasErrors);
        var reference = ModuleCompiler.CreateReference(ModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(leaf.Program), ["Leaf"]));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(Middle), reference);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var binding = new NativeImportBinding("Leaf", "bridge-leaf.dll", "Leaf");
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(program, ["Middle"],
            NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibraryWithImports(program, ["Leaf"],
            [binding], NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibraryWithImports(program, ["Missing"],
            [binding], NativeRuntimePolicy.FailFast));
        foreach (var bindings in new[] {
            Array.Empty<NativeImportBinding>(),
            [binding, binding],
            [new NativeImportBinding("Middle", "bridge-leaf.dll", "Leaf")],
            [new NativeImportBinding("Leaf", "..\\bridge-leaf.dll", "Leaf")],
        })
        {
            _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibraryWithImports(program,
                ["Middle"], bindings, NativeRuntimePolicy.FailFast));
        }
        var external = Array.FindIndex(program.Functions.ToArray(), static function => function.IsExternal);
        var invalid = program with {
            Functions = program.Functions.SetItem(external, program.Functions[external] with {
                Body = new BoundBlock([new BoundReturn(new BoundConstant(0, PrimitiveType.Int32, default), default)],
                    default),
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibraryWithImports(invalid,
            ["Middle"], [binding], NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibraryWithImports(program with { EntryPoint = 0 },
            ["Middle"], [binding], NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibraryWithImports(program,
            ["Middle"], [binding], (NativeRuntimePolicy)123));
    }
}
