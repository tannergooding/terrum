using System;
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

public sealed class NativeVTableCallTests
{
    private static string CompilerApphost()
    {
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        return Path.Combine(root, "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
    }

    [Test]
    public void NativeVTableSlotForwardsExplicitReceiverAndWindowsX64ArgumentPositions()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestVTable {
                public fnptr<NativeAbi, ptr<TestObject>, uint32, float64, uint32, float64, float64, uint32, uint32> Method;
                public TestVTable(fnptr<NativeAbi, ptr<TestObject>, uint32, float64, uint32, float64, float64, uint32, uint32> method) {
                    Method = method;
                }
            }

            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestObject {
                public ptr<TestVTable> VTable;
                public uint32 Marker;
                public TestObject(ptr<TestVTable> vtable, uint32 marker) {
                    VTable = vtable;
                    Marker = marker;
                }
            }

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            uint32 GetValue(ptr<TestObject> receiver, uint32 first, float64 second,
                uint32 third, float64 fourth, float64 fifth, uint32 sixth) {
                unsafe {
                    return (*receiver).Marker == 7 && first == 10 && second == 1.25
                        && third == 20 && fourth == 2.5 && fifth == 3.75 && sixth == 30 ? 42 : 0;
                }
            }

            [Terrum.Interop.NativeVTableMethod(0)]
            extern unsafe uint32 Invoke(ptr<TestObject> receiver, uint32 first, float64 second,
                uint32 third, float64 fourth, float64 fifth, uint32 sixth);

            int32 Main() {
                unsafe {
                    TestVTable vtable = new TestVTable(&GetValue);
                    TestObject instance = new TestObject(&vtable, 7);
                    ptr<TestObject> receiver = &instance;
                    return Invoke(receiver, 10, 1.25, 20, 2.5, 3.75, 30) == 42 ? 0 : -1;
                }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var method = compilation.Program!.Functions
            .Single(static function => function.Name == "Invoke");
        Assert.That(method.Intrinsic, Is.EqualTo(BoundIntrinsic.NativeVTableCall));
        Assert.That(method.IsExternal, Is.False);
        Assert.That(method.RequiresUnsafeCaller, Is.True);

        Assert.That(NativeJit.Execute(compilation.Program, NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public void NativeVTableMethodRequiresAReceiverPointerAndUnsafeCallers()
    {
        var invalidReceiver = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeVTableMethod(0)]
            extern unsafe int32 Invoke(int32 receiver);
            int32 Main() { return 0; }
            """));
        Assert.That(invalidReceiver.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True);

        var unsafeCallerRequired = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeVTableMethod(0)]
            extern unsafe int32 Invoke(ptr<void> receiver);
            int32 Main() { return Invoke(null); }
            """));
        Assert.That(unsafeCallerRequired.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3028"), Is.True);
    }

    [Test]
    public void NativeVTableSlotReturnsFloatingValueAndCommitsOutputArgument()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestVTable {
                public fnptr<NativeAbi, ptr<TestObject>, write ptr<uint32>, float64> Method;
                public TestVTable(fnptr<NativeAbi, ptr<TestObject>, write ptr<uint32>, float64> method) {
                    Method = method;
                }
            }

            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestObject {
                public ptr<TestVTable> VTable;
                public TestObject(ptr<TestVTable> vtable) { VTable = vtable; }
            }

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            float64 GetValue(ptr<TestObject> receiver, write ptr<uint32> output) {
                unsafe {
                    *output = 42;
                    return 10.5;
                }
            }

            [Terrum.Interop.NativeVTableMethod(0)]
            extern unsafe float64 Invoke(ptr<TestObject> receiver, out uint32 output);

            int32 Main() {
                unsafe {
                    TestVTable vtable = new TestVTable(&GetValue);
                    TestObject instance = new TestObject(&vtable);
                    ptr<TestObject> receiver = &instance;
                    mut uint32 output = 0;
                    float64 result = Invoke(receiver, out output);
                    return output == 42 && result == 10.5 ? 0 : -1;
                }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public void NativeVTableMethodReturnsRawPointer()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestVTable {
                public fnptr<NativeAbi, ptr<TestObject>, ptr<void>> Method;
                public TestVTable(fnptr<NativeAbi, ptr<TestObject>, ptr<void>> method) {
                    Method = method;
                }
            }

            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestObject {
                public ptr<TestVTable> VTable;
                public TestObject(ptr<TestVTable> vtable) { VTable = vtable; }
            }

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            ptr<void> GetValue(ptr<TestObject> receiver) { return null; }

            [Terrum.Interop.NativeVTableMethod(0)]
            extern unsafe ptr<void> Invoke(ptr<TestObject> receiver);

            unsafe ptr<void> Forward(ptr<void> pointer) { return pointer; }

            int32 Main() {
                unsafe {
                    TestVTable vtable = new TestVTable(&GetValue);
                    TestObject instance = new TestObject(&vtable);
                    ptr<TestObject> receiver = &instance;
                    return Forward(Invoke(receiver)) == null ? 0 : -1;
                }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var method = compilation.Program!.Functions
            .Single(static function => function.Name == "Invoke");
        Assert.That(method.ReturnType, Is.EqualTo(PrimitiveType.NativeRawPointer));

        Assert.That(NativeJit.Execute(compilation.Program, NativeRuntimePolicy.FailFast), Is.Zero);

        var safeReturn = Compilation.Create(SyntaxTree.Parse("""
            ptr<void> GetPointer() { return null; }
            int32 Main() { return 0; }
            """));
        Assert.That(safeReturn.HasErrors, Is.True);
    }

    [Test]
    public void NativeVTableMethodUsesAnnotatedPointerArgumentAddress()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestVTable {
                public fnptr<NativeAbi, ptr<TestObject>, ptr<uint32>, uint32> Method;
                public TestVTable(fnptr<NativeAbi, ptr<TestObject>, ptr<uint32>, uint32> method) {
                    Method = method;
                }
            }

            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestObject {
                public ptr<TestVTable> VTable;
                public uint32 Marker;
                public TestObject(ptr<TestVTable> vtable, uint32 marker) {
                    VTable = vtable;
                    Marker = marker;
                }
            }

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            uint32 GetValue(ptr<TestObject> receiver, ptr<uint32> value) { return 74; }

            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            [Terrum.Interop.NativeVTableMethod(0)]
            extern unsafe uint32 Invoke(ptr<TestObject> receiver, ptr<uint32> value);

            int32 Main() {
                unsafe {
                    TestVTable vtable = new TestVTable(&GetValue);
                    TestObject instance = new TestObject(&vtable, 42);
                    ptr<TestObject> receiver = &instance;
                    uint32 value = 32;
                    return Invoke(receiver, &value) == 74 ? 0 : -1;
                }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public async Task NativeVTableMethodSurvivesPrivateAndNativeModuleDependenciesAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string providerSource = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct TestVTable {
                public fnptr<NativeAbi, ptr<TestObject>, ptr<TestPayload>, int32> First;
                public fnptr<NativeAbi, ptr<TestObject>, ptr<TestPayload>, int32> Second;
                public TestVTable(fnptr<NativeAbi, ptr<TestObject>, ptr<TestPayload>, int32> first,
                    fnptr<NativeAbi, ptr<TestObject>, ptr<TestPayload>, int32> second) {
                    First = first;
                    Second = second;
                }
            }

            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct TestObject {
                public ptr<TestVTable> VTable;
                public TestObject(ptr<TestVTable> vtable) { VTable = vtable; }
            }

            [Blittable] [Layout(LayoutKind.Sequential)]
            struct UnusedStructure { int32 Value; }

            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct TestPayload {
                public int32 Value;
                public TestPayload(int32 value) { Value = value; }
            }

            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            [Terrum.Interop.NativeVTableMethod(0)]
            public extern unsafe int32 Invoke(ptr<TestObject> receiver, ptr<TestPayload> value);
            """;
        const string consumerSource = """
            [NoThrow]
            [Terrum.Interop.NativeCallable]
            int32 GetFirst(ptr<TestObject> receiver, ptr<TestPayload> value) { return 42; }

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            int32 GetSecond(ptr<TestObject> receiver, ptr<TestPayload> value) { return 43; }

            int32 Main() {
                unsafe {
                    TestVTable vtable = new TestVTable(&GetFirst, &GetSecond);
                    TestObject instance = new TestObject(&vtable);
                    ptr<TestObject> receiver = &instance;
                    TestPayload value = new TestPayload(42);
                    return Invoke(receiver, &value) == 42 ? 0 : -1;
                }
            }
            """;
        string[] exports = ["Invoke", "TestVTable.#ctor", "TestObject.#ctor", "TestPayload.#ctor"];
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(providerSource));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program!;
        var producer = ModuleCompiler.CreateProducer(providerProgram, exports);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(consumerCompilation.Program!, producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer),
            NativeRuntimePolicy.FailFast), Is.Zero);

        var slotOneSource = providerSource.Replace("NativeVTableMethod(0)", "NativeVTableMethod(1)",
            StringComparison.Ordinal);
        var slotOneCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(slotOneSource));
        Assert.That(slotOneCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, slotOneCompilation.Diagnostics));
        var slotOneProducer = ModuleCompiler.CreateProducer(slotOneCompilation.Program!, exports);
        var slotOneConsumer = ModuleCompiler.Link(consumer, slotOneProducer);
        Assert.That(NativeJit.Execute(slotOneConsumer, NativeRuntimePolicy.FailFast), Is.EqualTo(-1));

        var incompatibleSource = providerSource.Replace("extern unsafe int32 Invoke",
            "extern unsafe int64 Invoke", StringComparison.Ordinal);
        var incompatibleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(incompatibleSource));
        Assert.That(incompatibleCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, incompatibleCompilation.Diagnostics));
        var incompatibleProducer = ModuleCompiler.CreateProducer(incompatibleCompilation.Program!, exports);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer, incompatibleProducer));

        var nativeProducer = NativeModuleCompiler.CreateProducer(providerProgram, exports, "native_vtable",
            NativeRuntimePolicy.FailFast);
        var nativeReference = NativeModuleCompiler.CreateReference(nativeProducer);
        var nativeConsumerCompilation = Compilation.Create(SyntaxTree.Parse(consumerSource), nativeReference);
        Assert.That(nativeConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, nativeConsumerCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(nativeConsumerCompilation.Program!,
            nativeProducer, NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.Validate(nativeConsumer, nativeProducer);
        Assert.That(await NativeModuleCompiler.RunAsync(nativeConsumer, nativeProducer,
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast), Is.Zero);

        var extraProviderCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Extra() { return 43; }"));
        Assert.That(extraProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, extraProviderCompilation.Diagnostics));
        var extraNativeProducer = NativeModuleCompiler.CreateProducer(
            extraProviderCompilation.Program!, ["Extra"], "native_extra", NativeRuntimePolicy.FailFast);
        var nativeProviders = new[] { nativeProducer, extraNativeProducer };
        var multiProviderConsumerSource = consumerSource.Replace(
            "return Invoke(receiver, &value) == 42 ? 0 : -1;",
            "if (Invoke(receiver, &value) != 42) { return -1; }\n"
                + "                    return Extra() == 43 ? 0 : -2;",
            StringComparison.Ordinal);

        var setReference = NativeModuleCompiler.CreateReferenceSet(nativeProviders);
        var setCompilation = Compilation.Create(
            SyntaxTree.Parse(multiProviderConsumerSource), setReference);
        Assert.That(setCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var nativeSet = NativeModuleCompiler.CreateConsumerSet(setCompilation.Program!,
            nativeProviders, NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.ValidateConsumerSet(nativeSet, nativeProviders);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(nativeSet, nativeProviders,
            TimeSpan.FromSeconds(15), NativeRuntimePolicy.FailFast), Is.Zero);

        var graphReference = NativeModuleCompiler.CreateGraphReference(nativeProviders);
        var graphCompilation = Compilation.Create(
            SyntaxTree.Parse(multiProviderConsumerSource), graphReference);
        Assert.That(graphCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(graphCompilation.Program!,
            nativeProviders, NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.ValidateGraph(graphConsumer, nativeProviders);
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(graphConsumer, nativeProviders,
            CompilerApphost(), TimeSpan.FromSeconds(15), NativeRuntimePolicy.FailFast), Is.Zero);

        var incompatibleNativeProducer = NativeModuleCompiler.CreateProducer(
            incompatibleCompilation.Program!, exports, "native_vtable", NativeRuntimePolicy.FailFast);
        Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateConsumerSet(nativeSet,
                [incompatibleNativeProducer, extraNativeProducer]));
        Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateGraph(graphConsumer,
                [incompatibleNativeProducer, extraNativeProducer]));

        var directCompilation = Compilation.Create(SyntaxTree.Parse(providerSource + consumerSource));
        Assert.That(directCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, directCompilation.Diagnostics));
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(directCompilation.Program!, "native_vtable"));
    }
}
