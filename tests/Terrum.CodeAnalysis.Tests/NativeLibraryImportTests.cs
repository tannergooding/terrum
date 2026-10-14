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
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeLibraryImportTests
{
    private const string Leaf = "public int32 Leaf() { return 40; }";
    private const string Middle = "public int32 Middle() { return Leaf() + 1; }";
    private const string Main = "int32 Main() { return Middle() + 1; }";
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ExecutableImportsDependentLibraryAndLeafLibraryAsync(BoundRuntimePolicy policy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var leaf = Compilation.CreateLibrary(SyntaxTree.Parse(Leaf), policy);
        Assert.That(leaf.HasErrors, Is.False, string.Join(Environment.NewLine, leaf.Diagnostics));
        var leafProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = leaf.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var leafContract = ModuleCompiler.CreateProducer(leafProgram, ["Leaf"]);
        var leafReference = ModuleCompiler.CreateReference(leafContract);
        var middle = Compilation.CreateLibrary([SyntaxTree.Parse(
            "public int32 Forward() { return Leaf(); }"), SyntaxTree.Parse(
            "public int32 Middle() { return Forward() + 1; }")], leafReference, policy);
        Assert.That(middle.HasErrors, Is.False, string.Join(Environment.NewLine, middle.Diagnostics));
        var middleProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = middle.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        Assert.That(middleProgram.EntryPoint, Is.EqualTo(-1));
        var nativePolicy = (NativeRuntimePolicy)policy;
        var binding = new NativeImportBinding("Leaf", "bridge-leaf.dll", "Leaf");
        var middleImage = NativeEmitter.EmitLibraryWithImports(middleProgram, ["Middle"], [binding], nativePolicy);
        Assert.That(NativeEmitter.EmitLibraryWithImports(middleProgram, ["Middle"], [binding], nativePolicy), Is.EqualTo(middleImage));

        var application = Compilation.Create([SyntaxTree.Parse(Middle), SyntaxTree.Parse(Main)],
            leafReference, policy);
        Assert.That(application.HasErrors, Is.False, string.Join(Environment.NewLine, application.Diagnostics));
        var applicationProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = application.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        var main = global::System.Linq.Enumerable.Single(applicationProgram.Functions, static function => function.Name == "Main");
        var published = global::System.Linq.Enumerable.Single(applicationProgram.Functions, static function => function.Name == "Middle");
        var result = ((global::System.Func<BoundReturn>)(() => { var __nunitActual3 = global::System.Linq.Enumerable.Single(main.Body.Statements); Assert.That(__nunitActual3, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual3!; }))();
        var sum = ((global::System.Func<BoundBinary>)(() => { var __nunitActual4 = result.Value; Assert.That(__nunitActual4, Is.TypeOf<BoundBinary>()); return (BoundBinary)__nunitActual4!; }))();
        var call = ((global::System.Func<BoundCall>)(() => { var __nunitActual5 = sum.Left; Assert.That(__nunitActual5, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual5!; }))();
        Assert.That(applicationProgram.Functions[call.Function].Name, Is.EqualTo(published.Name));
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
            Assert.That(process.ExitCode, Is.EqualTo(42));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(executablePath).ConfigureAwait(true);
            File.Delete(middlePath);
            File.Delete(leafPath);
            Directory.Delete(directory);
        }
    }

    [Test]
    public void LibraryReferencesRespectSourceSetsAndRuntimePolicy()
    {
        var leaf = Compilation.CreateLibrary(SyntaxTree.Parse(Leaf), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(leaf.HasErrors, Is.False);
        var reference = ModuleCompiler.CreateReference(ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = leaf.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))(), ["Leaf"]));
        var single = Compilation.CreateLibrary(SyntaxTree.Parse(Middle), reference,
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(single.HasErrors, Is.False, string.Join(Environment.NewLine, single.Diagnostics));
        var multiple = Compilation.CreateLibrary([SyntaxTree.Parse(Middle),
            SyntaxTree.Parse("public int32 Relay() { return Middle(); }")],
            reference, BoundRuntimePolicy.CheckedExceptions);
        Assert.That(multiple.HasErrors, Is.False, string.Join(Environment.NewLine, multiple.Diagnostics));
        Assert.That(((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = multiple.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))().EntryPoint, Is.EqualTo(-1));
        Assert.That(global::System.Linq.Enumerable.Any(((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = multiple.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))().Functions,static function => function.Name == "Leaf" && function.IsExternal),Is.True);
        var mismatch = Compilation.CreateLibrary(SyntaxTree.Parse(Middle), reference);
        Assert.That(global::System.Linq.Enumerable.Any(mismatch.Diagnostics,static diagnostic => diagnostic.Code == "TRM3022"),Is.True);
        Assert.That(mismatch.Program, Is.Null);
        var multipleMismatch = Compilation.CreateLibrary([SyntaxTree.Parse(Middle)], reference,
            BoundRuntimePolicy.FailFast);
        Assert.That(global::System.Linq.Enumerable.Any(multipleMismatch.Diagnostics,static diagnostic => diagnostic.Code == "TRM3022"),Is.True);
        Assert.That(multipleMismatch.Program, Is.Null);

        var fastLeaf = Compilation.CreateLibrary(SyntaxTree.Parse(Leaf));
        Assert.That(fastLeaf.HasErrors, Is.False);
        var fastReference = ModuleCompiler.CreateReference(ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = fastLeaf.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))(), ["Leaf"]));
        var defaultSingle = Compilation.CreateLibrary(SyntaxTree.Parse(Middle), fastReference);
        var defaultMultiple = Compilation.CreateLibrary([SyntaxTree.Parse(Middle)], fastReference);
        Assert.That(defaultSingle.HasErrors, Is.False, string.Join(Environment.NewLine, defaultSingle.Diagnostics));
        Assert.That(defaultMultiple.HasErrors, Is.False, string.Join(Environment.NewLine, defaultMultiple.Diagnostics));
    }

    [Test]
    public void DependentLibraryRejectsMalformedIrExportsAndImportBindings()
    {
        var leaf = Compilation.CreateLibrary(SyntaxTree.Parse(Leaf));
        Assert.That(leaf.HasErrors, Is.False);
        var reference = ModuleCompiler.CreateReference(ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = leaf.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))(), ["Leaf"]));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(Middle), reference);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = compilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))();
        var binding = new NativeImportBinding("Leaf", "bridge-leaf.dll", "Leaf");
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(program, ["Middle"],
            NativeRuntimePolicy.FailFast))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibraryWithImports(program, ["Leaf"],
            [binding], NativeRuntimePolicy.FailFast))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibraryWithImports(program, ["Missing"],
            [binding], NativeRuntimePolicy.FailFast))!;
        foreach (var bindings in new[] {
            Array.Empty<NativeImportBinding>(),
            [binding, binding],
            [new NativeImportBinding("Middle", "bridge-leaf.dll", "Leaf")],
            [new NativeImportBinding("Leaf", "..\\bridge-leaf.dll", "Leaf")],
        })
        {
            _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibraryWithImports(program,
                ["Middle"], bindings, NativeRuntimePolicy.FailFast))!;
        }
        var external = Array.FindIndex(program.Functions.ToArray(), static function => function.IsExternal);
        var invalid = program with {
            Functions = program.Functions.SetItem(external, program.Functions[external] with {
                Body = new BoundBlock([new BoundReturn(new BoundConstant(0, PrimitiveType.Int32, default), default)],
                    default),
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibraryWithImports(invalid,
            ["Middle"], [binding], NativeRuntimePolicy.FailFast))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibraryWithImports(program with { EntryPoint = 0 },
            ["Middle"], [binding], NativeRuntimePolicy.FailFast))!;
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibraryWithImports(program,
            ["Middle"], [binding], (NativeRuntimePolicy)123))!;
    }
}
