// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class BoundTreeTraversalTests
{
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ModuleRemappingVisitsUnsafeAndViewScopeCallsAsync(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            int32 Unused() { return -1; }
            int32 Start() { return 0; }
            int32 Length() { return 1; }
            int32 Adjust(int32 value) { return value + 2; }
            public int32 Evaluate() {
                require OwnedInt32Buffer.TryCreate(1, out var created);
                mut OwnedInt32Buffer buffer = move created;
                require buffer.TryAppend(40);
                ReadonlyInt32BufferView view = buffer.Slice(Start(), Length());
                unsafe { return Adjust(view.Read(Start())); }
            }
            """), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected the library.");
        var producer = ModuleCompiler.CreateProducer(program, ["Evaluate"]);
        var consumer = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Evaluate(); }"),
            ModuleCompiler.CreateReference(producer), (BoundRuntimePolicy)policy);
        Assert.That(consumer.HasErrors, Is.False, string.Join(Environment.NewLine, consumer.Diagnostics));
        var persisted = ModuleCompiler.CreateConsumer(consumer.Program!, producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(persisted, producer), policy), Is.EqualTo(42));

        var nativeProducer = NativeModuleCompiler.CreateProducer(program, ["Evaluate"], "Traversal", policy);
        var nativeConsumer = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Evaluate(); }"),
            NativeModuleCompiler.CreateReference(nativeProducer), (BoundRuntimePolicy)policy);
        Assert.That(nativeConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, nativeConsumer.Diagnostics));
        var nativePersisted = NativeModuleCompiler.CreateConsumer(
            nativeConsumer.Program!, nativeProducer, policy);
        Assert.That(await NativeModuleCompiler.RunAsync(nativePersisted, nativeProducer,
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(false), Is.EqualTo(42));
    }

    private static BoundProgram CompileFixedArray()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            uint32 Element() { return 42; }
            int32 Index() { return 1; }
            int32 Main() {
                InlineArray<uint32, 2> values = [7, Element()];
                return int32(values[Index()]);
            }
            """), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return compilation.Program ?? throw new AssertionException("Expected the fixed-array program.");
    }

    [Test]
    public async Task NativeEmissionVisitsFixedArrayInitializerAndIndexCallsAsync()
    {
        var image = NativeEmitter.EmitWithNativeImports(
            CompileFixedArray(), NativeRuntimePolicy.CheckedExceptions);
        var directory = TestArtifactPaths.CreateDirectory("terrum-array-traversal-");
        try
        {
            var path = Path.Combine(directory, "array-traversal.exe");
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(false);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory,
            }) ?? throw new InvalidOperationException("Could not start the fixed-array program.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(process.ExitCode, Is.EqualTo(42));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory).ConfigureAwait(false);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void NativeEmissionRejectsMalformedFixedArrayChildren(bool defaultElements)
    {
        var program = CompileFixedArray();
        var function = program.Functions[program.EntryPoint];
        var local = function.Body.Statements.OfType<BoundLocal>()
            .Single(static statement => statement.Initializer is BoundNativeFixedArrayInitialization);
        var array = (BoundNativeFixedArrayInitialization)local.Initializer;
        var malformed = local with {
            Initializer = array with {
                Elements = defaultElements ? default
                    : array.Elements.SetItem(1, new BoundCall(0, default, PrimitiveType.UInt32, default)),
            },
        };
        var changedFunction = function with {
            Body = function.Body with {
                Statements = function.Body.Statements.Replace(local, malformed),
            },
        };
        var changed = program with {
            Functions = program.Functions.SetItem(program.EntryPoint, changedFunction),
        };
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImports(changed, NativeRuntimePolicy.CheckedExceptions));
    }
}
