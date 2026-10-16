// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
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

public sealed class NativeFunctionPointerTests
{
    private const string NativeCallbackProvider = """
        [NoThrow]
        [Terrum.Interop.NativeCallable]
        int32 Answer(int32 value) { return value; }

        [NoThrow]
        public unsafe fnptr<NativeAbi, int32, int32> GetAnswer() {
            unsafe { return &Answer; }
        }
        """;

    [Test]
    public void NativeCallableAndIndirectFloatCallsUseWindowsX64Abi()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow]
            [Terrum.Interop.NativeCallable]
            float64 Sum(float64 first, int32 ignored1, float64 second, int32 ignored2, float64 third) {
                return first + second + third;
            }

            int32 Main() {
                unsafe {
                    fnptr<NativeAbi, float64, int32, float64, int32, float64, float64> sum = &Sum;
                    float64 expected = 9.5;
                    if (Sum(1.5, 0, 3.0, 0, 5.0) != sum(1.5, 0, 3.0, 0, 5.0)) { return -1; }
                    return sum(1.5, 0, 3.0, 0, 5.0) == expected ? 0 : -1;
                }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public void NativeFunctionAddressSurvivesPrivateModuleProducerConsumerRoundTrip()
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(NativeCallbackProvider));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(providerCompilation.Program!, ["GetAnswer"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    fnptr<NativeAbi, int32, int32> callback = GetAnswer();
                    return callback(42) == 42 ? 0 : -1;
                }
            }
            """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(consumerCompilation.Program!, producer);

        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), NativeRuntimePolicy.FailFast),
            Is.Zero);

        var incompatibleCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [NoThrow]
            [Terrum.Interop.NativeCallable]
            int32 Answer() { return 42; }

            [NoThrow]
            public unsafe fnptr<NativeAbi, int32> GetAnswer() {
                unsafe { return &Answer; }
            }
            """));
        Assert.That(incompatibleCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, incompatibleCompilation.Diagnostics));
        var incompatibleProducer = ModuleCompiler.CreateProducer(incompatibleCompilation.Program!, ["GetAnswer"]);
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer, incompatibleProducer));
    }

    [Test]
    public void FunctionPointerSignaturesMergeAcrossPrivateModuleReferences()
    {
        var firstProducer = CreateProducer("""
            public unsafe int32 AcceptZero(fnptr<NativeAbi, int32> callback) { return 0; }
            public unsafe int32 AcceptUnary(fnptr<NativeAbi, int32, int32> callback) { return 0; }
            """, ["AcceptZero", "AcceptUnary"]);
        var secondProducer = CreateProducer("""
            public unsafe int32 AcceptUnarySecond(fnptr<NativeAbi, int32, int32> callback) { return 0; }
            public unsafe int32 AcceptZeroSecond(fnptr<NativeAbi, int32> callback) { return 0; }
            """, ["AcceptUnarySecond", "AcceptZeroSecond"]);

        var reference = ModuleCompiler.CreateReferenceSet([firstProducer, secondProducer]);
        var zeroType = Export(reference, "AcceptZero").Variables[0].Type;
        var unaryType = Export(reference, "AcceptUnary").Variables[0].Type;

        Assert.That(Export(reference, "AcceptZeroSecond").Variables[0].Type, Is.EqualTo(zeroType));
        Assert.That(Export(reference, "AcceptUnarySecond").Variables[0].Type, Is.EqualTo(unaryType));
        Assert.That(unaryType, Is.Not.EqualTo(zeroType));
    }

    [Test]
    public void FunctionPointerFieldsTransportCallableAddresses()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct CallbackBox {
                public fnptr<NativeAbi, int32, int32> Callback;
                public CallbackBox(fnptr<NativeAbi, int32, int32> callback) {
                    Callback = callback;
                }
            }

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            int32 Echo(int32 value) { return value; }

            int32 Main() {
                unsafe {
                    CallbackBox box = new CallbackBox(&Echo);
                    return box.Callback(42) == 42 ? 0 : -1;
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public void FunctionPointerFieldAcceptsNativeCallableDeclaredInAnotherSourceFile()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var declarations = SyntaxTree.Parse("""
            struct CallbackBox {
                public fnptr<NativeAbi, ptr<void>, uint32, uint64, int64, int64> Callback;
                public CallbackBox(
                    fnptr<NativeAbi, ptr<void>, uint32, uint64, int64, int64> callback) {
                    Callback = callback;
                }
            }
            """);
        var implementation = SyntaxTree.Parse("""
            [NoThrow]
            [Terrum.Interop.NativeCallable]
            int64 WindowProcedure(ptr<void> window, uint32 message, uint64 wParam, int64 lParam) {
                return lParam;
            }

            int32 Main() {
                unsafe {
                    CallbackBox box = new CallbackBox(&WindowProcedure);
                    return box.Callback(null, 0, 0, 42) == 42 ? 0 : -1;
                }
            }
            """);
        var compilation = Compilation.Create([declarations, implementation]);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public void BlittableFunctionPointerFieldsUseNativePointerLayout()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct CallbackBox {
                public fnptr<NativeAbi, int32, int32> Callback;
                public CallbackBox(fnptr<NativeAbi, int32, int32> callback) {
                    Callback = callback;
                }
            }

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            int32 Echo(int32 value) { return value; }

            int32 Main() {
                unsafe {
                    CallbackBox box = new CallbackBox(&Echo);
                    return box.Callback(42) == 42 ? 0 : -1;
                }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(FixedLayoutStorage.Validate(program.Structures), Is.EqualTo([8]));
        Assert.That(program.Structures[0].Fields.Single().Offset, Is.Zero);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public void AuthoredVTableUsesOrdinaryFunctionPointerFields()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestVTable {
                public fnptr<NativeAbi, ptr<void>, uint32> GetValue;
                public TestVTable(fnptr<NativeAbi, ptr<void>, uint32> getValue) {
                    GetValue = getValue;
                }
            }

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            uint32 GetValue(ptr<void> receiver) { return 42; }

            int32 Main() {
                unsafe {
                    TestVTable vtable = new TestVTable(&GetValue);
                    ptr<void> receiver = null;
                    return vtable.GetValue(receiver) == 42 ? 0 : -1;
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public void AuthoredVTableUsesPointerReceiverAndOrdinaryFunctionPointerFields()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestVTable {
                public fnptr<NativeAbi, ptr<TestObject>, uint32> GetValue;
                public TestVTable(fnptr<NativeAbi, ptr<TestObject>, uint32> getValue) {
                    GetValue = getValue;
                }
            }

            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestObject {
                public ptr<TestVTable> VTable;
                public TestObject(ptr<TestVTable> vtable) { VTable = vtable; }
            }

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            uint32 GetValue(ptr<TestObject> receiver) { return 42; }

            int32 Main() {
                unsafe {
                    TestVTable vtable = new TestVTable(&GetValue);
                    TestObject instance = new TestObject(&vtable);
                    ptr<TestObject> receiver = &instance;
                    return (*((*receiver).VTable)).GetValue(receiver) == 42 ? 0 : -1;
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public void FunctionPointerSignaturesMergeAcrossNativeModuleSets()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var first = CreateNativeProducer("""
            public unsafe int32 AcceptZero(fnptr<NativeAbi, int32> callback) { return 0; }
            public unsafe int32 AcceptUnary(fnptr<NativeAbi, int32, int32> callback) { return 0; }
            """, ["AcceptZero", "AcceptUnary"], "CallbackTypesFirst");
        var second = CreateNativeProducer("""
            public unsafe int32 AcceptUnarySecond(fnptr<NativeAbi, int32, int32> callback) { return 0; }
            public unsafe int32 AcceptZeroSecond(fnptr<NativeAbi, int32> callback) { return 0; }
            """, ["AcceptUnarySecond", "AcceptZeroSecond"], "CallbackTypesSecond");
        var reference = NativeModuleCompiler.CreateReferenceSet([first, second]);

        AssertMergedFunctionPointerTypes(reference);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return 0; }"), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumerSet(
            consumerCompilation.Program!, [first, second], NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.ValidateConsumerSet(consumer, [second, first]);
    }

    [Test]
    public async Task NativeGraphConsumerPreservesProviderFunctionPointersAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(NativeCallbackProvider));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(providerCompilation.Program!,
            ["GetAnswer"], "CallbackProvider", NativeRuntimePolicy.FailFast);
        var reference = NativeModuleCompiler.CreateGraphReference([provider]);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    fnptr<NativeAbi, int32, int32> callback = GetAnswer();
                    return callback(42) == 42 ? 0 : -1;
                }
            }
            """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateGraphConsumer(
            consumerCompilation.Program!, [provider], NativeRuntimePolicy.FailFast);

        Assert.That(await NativeModuleCompiler.RunGraphAsync(consumer, [provider],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast).ConfigureAwait(true), Is.Zero);
    }

    [Test]
    public void PrivateModuleReaderRejectsMalformedFunctionPointerSignatures()
    {
        var producer = CreateProducer(NativeCallbackProvider, ["GetAnswer"]);
        var malformed = producer.ToArray();
        var tableOffset = FindSignatureTableOffset(malformed);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(malformed.AsSpan(tableOffset)), Is.EqualTo(1));
        var parameterCountOffset = tableOffset + sizeof(int) + sizeof(byte) + sizeof(int);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(malformed.AsSpan(parameterCountOffset)), Is.EqualTo(1));
        BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(parameterCountOffset + sizeof(int)),
            (int)PrimitiveType.Void);

        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed));
    }

    [Test]
    public void StandardTrRmRejectsNativeFunctionPointerSignatures()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(NativeCallbackProvider + """
            int32 Main() { return 0; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(compilation.Program!, "unapproved-native-function-pointer"));
    }

    [Test]
    public async Task NativeCallableExportMatchesIndependentCAbiCallerAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var root = FindRepositoryRoot();
        var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        Assert.That(File.Exists(vswhere), Is.True,
            "Visual Studio discovery is required to build the independent C caller.");
        var discover = new ProcessStartInfo(vswhere) { WorkingDirectory = root };
        foreach (var argument in new[] {
            "-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
            "-property", "installationPath",
        })
        {
            discover.ArgumentList.Add(argument);
        }
        var installation = (await RunProcessAsync(discover, TimeSpan.FromSeconds(10)).ConfigureAwait(true)).Trim();
        var environment = Path.Combine(installation, "VC", "Auxiliary", "Build", "vcvars64.bat");
        Assert.That(File.Exists(environment), Is.True,
            "Visual Studio x64 C build tools are required for the independent C caller.");

        var directory = Directory.CreateTempSubdirectory("terrum-native-callback-").FullName;
        var libraryPath = Path.Combine(directory, "native-callback.dll");
        var objectPath = Path.Combine(directory, "native-callback-caller.obj");
        var executablePath = Path.Combine(directory, "native-callback-caller.exe");
        var sourcePath = Path.Combine(root, "tests", "native", "native-function-pointer-caller.c");
        try
        {
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
                [NoThrow]
                [Terrum.Interop.NativeCallable]
                public float64 Sum(float64 first, int32 ignored1, float64 second, int32 ignored2,
                    float64 third) {
                    return first + second + third;
                }
                """));
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            await File.WriteAllBytesAsync(libraryPath,
                NativeEmitter.EmitLibrary(compilation.Program!, ["Sum"], NativeRuntimePolicy.FailFast))
                .ConfigureAwait(true);

            var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")
                ?? throw new InvalidOperationException("ComSpec is unset.")) {
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /O2 /MT /TC /Fo\"{objectPath}\" /Fe\"{executablePath}\" \"{sourcePath}\" /link /incremental:no\"",
                WorkingDirectory = root,
            };
            _ = await RunProcessAsync(build, TimeSpan.FromMinutes(2)).ConfigureAwait(true);
            var run = new ProcessStartInfo(executablePath);
            run.ArgumentList.Add(libraryPath);
            _ = await RunProcessAsync(run, TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(executablePath).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(libraryPath).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(objectPath).ConfigureAwait(true);
            Directory.Delete(directory);
        }
    }

    [TestCase("""
        [Terrum.Interop.NativeCallable]
        int32 Answer() { return 42; }
        int32 Main() { return 0; }
        """, "TRM3030")]
    [TestCase("""
        [NoThrow]
        [Terrum.Interop.NativeCallable("invalid")]
        int32 Answer() { return 42; }
        int32 Main() { return 0; }
        """, "TRM3030")]
    [TestCase("""
        [NoThrow]
        [Terrum.Interop.NativeCallable]
        int32 Answer() { return 42; }
        int32 Invoke(fnptr<NativeAbi, int32> callback) { return callback(); }
        int32 Main() { return 0; }
        """, "TRM3028")]
    public void NativeCallableContractsRejectMissingUnsafeAndMalformedDeclarations(
        string source, string diagnosticCode)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.Diagnostics.Any(diagnostic => diagnostic.Code == diagnosticCode), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NativeFunctionAddressRequiresExactSignature()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow]
            [Terrum.Interop.NativeCallable]
            int32 Answer() { return 42; }

            int32 Main() {
                unsafe {
                    fnptr<NativeAbi, int64> callback = &Answer;
                    return 0;
                }
            }
            """));

        Assert.That(compilation.Diagnostics.Any(diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NativeFunctionAddressRequiresUnsafeContext()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow]
            [Terrum.Interop.NativeCallable]
            int32 Answer() { return 42; }

            int32 Main() {
                fnptr<NativeAbi, int32> callback = &Answer;
                return 0;
            }
            """));

        Assert.That(compilation.Diagnostics.Any(diagnostic => diagnostic.Code == "TRM3028"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [TestCase("""
        [NoThrow]
        [Terrum.Interop.NativeCallable]
        int32 ReadSourceLength() { return Host.Source.Length; }
        """)]
    [TestCase("""
        [NoThrow]
        int32 SourceLength() { return Host.Source.Length; }
        [NoThrow]
        [Terrum.Interop.NativeCallable]
        int32 ReadSourceLength() { return SourceLength(); }
        """)]
    public void NativeCallableCannotDependOnInvocationScopedSource(string source)
    {
        var compilation = Compilation.CreateLibrary([SyntaxTree.Parse(source)],
            [new HostBuffer("Source", PrimitiveType.Char16, [], false) { IsInvocationScoped = true }],
            BoundRuntimePolicy.FailFast);

        Assert.That(compilation.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "TRM3016" && diagnostic.Message.Contains("NativeFunctionPointer", StringComparison.Ordinal)),
            Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NativeCallableCannotDependOnNonEmptyInvocationScopedSource()
    {
        var compilation = Compilation.CreateLibrary([SyntaxTree.Parse("""
                [NoThrow]
                [Terrum.Interop.NativeCallable]
                int32 ReadSourceLength() { return Host.Source.Length; }
                """)], [new HostBuffer("Source", PrimitiveType.Char16, [65], false) { IsInvocationScoped = true }],
            BoundRuntimePolicy.FailFast);

        Assert.That(compilation.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "TRM3016" && diagnostic.Message.Contains("NativeFunctionPointer", StringComparison.Ordinal)),
            Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void SourceIndependentNativeCallableRemainsValidWithInvocationScopedSource()
    {
        var compilation = Compilation.CreateLibrary([SyntaxTree.Parse("""
                [NoThrow]
                [Terrum.Interop.NativeCallable]
                int32 Answer() { return 42; }
                """)], [new HostBuffer("Source", PrimitiveType.Char16, [], false) { IsInvocationScoped = true }],
            BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    [Test]
    public void SourceIndependentNativeCallableWithNonEmptyInvocationSourceRemainsValid()
    {
        var compilation = Compilation.CreateLibrary([SyntaxTree.Parse("""
                [NoThrow]
                [Terrum.Interop.NativeCallable]
                int32 Answer() { return 42; }
                """)], [new HostBuffer("Source", PrimitiveType.Char16, [65], false) { IsInvocationScoped = true }],
            BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    private static byte[] CreateProducer(string source, string[] exports)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return ModuleCompiler.CreateProducer(compilation.Program!, exports);
    }

    private static byte[] CreateNativeProducer(string source, string[] exports, string identity)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return NativeModuleCompiler.CreateProducer(compilation.Program!, exports, identity,
            NativeRuntimePolicy.FailFast);
    }

    private static BoundFunction Export(CompilationReference reference, string name) =>
        reference.Functions.Single(function => function.Name == name);

    private static void AssertMergedFunctionPointerTypes(CompilationReference reference)
    {
        var zeroType = Export(reference, "AcceptZero").Variables[0].Type;
        var unaryType = Export(reference, "AcceptUnary").Variables[0].Type;

        Assert.That(Export(reference, "AcceptZeroSecond").Variables[0].Type, Is.EqualTo(zeroType));
        Assert.That(Export(reference, "AcceptUnarySecond").Variables[0].Type, Is.EqualTo(unaryType));
        Assert.That(unaryType, Is.Not.EqualTo(zeroType));
    }

    private static int FindSignatureTableOffset(byte[] module)
    {
        using var stream = new MemoryStream(module, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        _ = reader.ReadInt32();
        Assert.That(reader.ReadInt32(), Is.EqualTo(82));
        SkipString(reader);
        _ = reader.ReadBoolean();
        _ = reader.ReadByte();
        _ = reader.ReadBoolean();
        SkipString(reader);
        var bufferCount = reader.ReadInt32();
        Assert.That(bufferCount, Is.Zero);
        _ = reader.ReadInt32();
        Assert.That(reader.ReadInt32(), Is.Zero, "The fixture has no structures.");
        Assert.That(reader.ReadInt32(), Is.Zero, "The fixture has no classes.");
        Assert.That(reader.ReadInt32(), Is.Zero, "The fixture has no imported structures.");
        return checked((int)stream.Position);
    }

    private static void SkipString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        _ = reader.ReadBytes(length);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tests", "native", "native-function-pointer-caller.c")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static async Task<string> RunProcessAsync(ProcessStartInfo start, TimeSpan timeout)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start native function-pointer process.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(timeout).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(true);
            throw;
        }

        var standardOutput = await output.ConfigureAwait(true);
        var standardError = await error.ConfigureAwait(true);
        Assert.That(process.ExitCode, Is.Zero,
            $"Process failed with exit code {process.ExitCode}.{Environment.NewLine}{standardOutput}{standardError}");
        return standardOutput;
    }
}
