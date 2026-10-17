// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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
    public void VerifierRejectsFunctionAddressAfterRemovingItsUnsafeScope()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow]
            [Terrum.Interop.NativeCallable]
            int32 Answer() { return 42; }

            int32 Main() {
                unsafe {
                    fnptr<NativeAbi, int32> answer = &Answer;
                    return 0;
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var forged = RemoveFunctionUnsafeScope(program, "Main");

        Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
    }

    [Test]
    public void VerifierRejectsIndirectCallAfterRemovingItsUnsafeScope()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unsafe int32 Invoke(fnptr<NativeAbi, int32, int32> callback, int32 value) {
                unsafe { return callback(value); }
            }

            int32 Main() { return 0; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var forged = RemoveFunctionUnsafeScope(program, "Invoke");

        Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
    }

    [Test]
    public void DirectEmissionRejectsCallerUnsafeCallOutsideLexicalUnsafeScope()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unsafe int32 Danger() { return 42; }
            int32 Main() { return unsafe(Danger()); }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var functionIndex = Enumerable.Range(0, program.Functions.Length)
            .Single(index => program.Functions[index].Name == "Main");
        var function = program.Functions[functionIndex];
        var result = (BoundReturn)function.Body.Statements.Single();
        var authorized = (BoundUnsafeExpression)result.Value!;
        var forged = program with {
            Functions = program.Functions.SetItem(functionIndex, function with {
                Body = function.Body with {
                    Statements = [result with { Value = authorized.Expression }],
                },
            }),
        };

        Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast));
    }

    private static BoundProgram RemoveFunctionUnsafeScope(BoundProgram program, string functionName)
    {
        var functionIndex = Enumerable.Range(0, program.Functions.Length)
            .Single(index => program.Functions[index].Name == functionName);
        var function = program.Functions[functionIndex];
        var unsafeScope = function.Body.Statements.OfType<BoundUnsafe>().Single();
        return program with {
            Functions = program.Functions.SetItem(functionIndex, function with { Body = unsafeScope.Body }),
        };
    }

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
    public async Task NativeCallableExportsSurvivePrivateAndNativeModuleConsumersAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string providerSource = """
            [NoThrow]
            [Terrum.Interop.NativeCallable]
            public int32 Identity(int32 value) { return value; }
            [NoThrow]
            [Terrum.Interop.NativeCallable]
            public int32 NamedOnly(int32 value) { return value; }
            """;
        const string consumerSource = """
            int32 Main() {
                unsafe { return Identity(42) == 42 ? 0 : -1; }
            }
            """;
        const string addressConsumerSource = """
            int32 Main() {
                unsafe {
                    fnptr<NativeAbi, int32, int32> identity = &Identity;
                    return identity(42) == 42 ? 0 : -1;
                }
            }
            """;

        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(providerSource));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var nativeProviderProgram = providerCompilation.Program!;
        var namedOnlyIndex = nativeProviderProgram.Functions.IndexOf(
            nativeProviderProgram.Functions.Single(static function => function.Name == "NamedOnly"));
        nativeProviderProgram = nativeProviderProgram with
        {
            Functions = nativeProviderProgram.Functions.SetItem(namedOnlyIndex,
                nativeProviderProgram.Functions[namedOnlyIndex] with {
                    NativeCallableEntryName = "NamedOnly|Native",
                }),
        };

        var privateProducer = ModuleCompiler.CreateProducer(providerCompilation.Program!, ["Identity"]);
        var privateReference = ModuleCompiler.CreateReference(privateProducer);
        var privateConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse(consumerSource), privateReference);
        Assert.That(privateConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, privateConsumerCompilation.Diagnostics));
        var privateConsumer = ModuleCompiler.CreateConsumer(
            privateConsumerCompilation.Program!, privateProducer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(privateConsumer, privateProducer),
            NativeRuntimePolicy.FailFast), Is.Zero);
        var addressConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse(addressConsumerSource), privateReference);
        Assert.That(addressConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, addressConsumerCompilation.Diagnostics));
        var addressConsumer = ModuleCompiler.CreateConsumer(addressConsumerCompilation.Program!,
            privateProducer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(addressConsumer, privateProducer),
            NativeRuntimePolicy.FailFast), Is.Zero);

        var nativeProducer = NativeModuleCompiler.CreateProducer(nativeProviderProgram,
            ["Identity"], "NativeCallableProvider", NativeRuntimePolicy.FailFast);
        var nativeProducerReference = NativeModuleCompiler.CreateReference(nativeProducer);
        Assert.That(nativeProducerReference.Functions.Select(static function => function.Name),
            Is.EqualTo(new[] { "Identity" }));
        var secondCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Other() { return 0; }"));
        Assert.That(secondCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, secondCompilation.Diagnostics));
        var secondProducer = NativeModuleCompiler.CreateProducer(secondCompilation.Program!,
            ["Other"], "NativeCallableSecond", NativeRuntimePolicy.FailFast);
        var nativeProducers = new[] { nativeProducer, secondProducer };
        var nativeReference = NativeModuleCompiler.CreateReferenceSet(nativeProducers);
        var nativeConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse(consumerSource), nativeReference);
        Assert.That(nativeConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, nativeConsumerCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateConsumerSet(
            nativeConsumerCompilation.Program!, nativeProducers, NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.ValidateConsumerSet(nativeConsumer, nativeProducers);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(nativeConsumer, nativeProducers,
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast).ConfigureAwait(true), Is.Zero);
        var nativeAddressConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse(addressConsumerSource), nativeReference);
        Assert.That(nativeAddressConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, nativeAddressConsumerCompilation.Diagnostics));
        var nativeAddressConsumer = NativeModuleCompiler.CreateConsumerSet(
            nativeAddressConsumerCompilation.Program!, nativeProducers, NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.ValidateConsumerSet(nativeAddressConsumer, nativeProducers);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(nativeAddressConsumer, nativeProducers,
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast).ConfigureAwait(true), Is.Zero);
    }

    [Test]
    public async Task NativeCallsNormalizeNarrowReturnsAcrossImportPointerAndVTableAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        Assert.That(File.Exists(vswhere), Is.True,
            "Visual Studio discovery is required to build the independent native return fixture.");
        var discover = new ProcessStartInfo(vswhere);
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
            "Visual Studio x64 build tools are required to build the independent native return fixture.");

        var directory = Directory.CreateTempSubdirectory("terrum-native-narrow-return-").FullName;
        var assemblyPath = Path.Combine(directory, "native-narrow-return.asm");
        var objectPath = Path.Combine(directory, "native-narrow-return.obj");
        var libraryPath = Path.Combine(directory, "native-narrow-return.dll");
        var exportPath = Path.Combine(directory, "native-narrow-return.exp");
        var importLibraryPath = Path.Combine(directory, "native-narrow-return.lib");
        var executablePath = Path.Combine(directory, "native-narrow-return.exe");
        try
        {
            await File.WriteAllTextAsync(assemblyPath, """
                option casemap:none
                .code
                ReturnFalseDirty PROC
                    mov eax, 0FFFFFF00h
                    ret
                ReturnFalseDirty ENDP

                GetFalseDirty PROC
                    lea rax, ReturnFalseDirty
                    ret
                GetFalseDirty ENDP

                ReturnFalseDirtyWithReceiver PROC
                    mov eax, 0FFFFFF00h
                    ret
                ReturnFalseDirtyWithReceiver ENDP

                GetFalseDirtyWithReceiver PROC
                    lea rax, ReturnFalseDirtyWithReceiver
                    ret
                GetFalseDirtyWithReceiver ENDP

                ReturnChar16Dirty PROC
                    mov eax, 0DEAD0001h
                    ret
                ReturnChar16Dirty ENDP
                END
                """).ConfigureAwait(true);

            var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")
                ?? throw new InvalidOperationException("ComSpec is unset.")) {
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && ml64 /nologo /c /Fo\"{objectPath}\" \"{assemblyPath}\" && link /nologo /dll /noentry /incremental:no /out:\"{libraryPath}\" /export:ReturnFalseDirty /export:GetFalseDirty /export:ReturnFalseDirtyWithReceiver /export:GetFalseDirtyWithReceiver /export:ReturnChar16Dirty \"{objectPath}\"\"",
                WorkingDirectory = directory,
            };
            _ = await RunProcessAsync(build, TimeSpan.FromMinutes(2)).ConfigureAwait(true);

            var compilation = Compilation.Create(SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                struct TestVTable {
                    public fnptr<NativeAbi, ptr<TestObject>, bool> Method;
                    public TestVTable(fnptr<NativeAbi, ptr<TestObject>, bool> method) {
                        Method = method;
                    }
                }

                [Blittable] [Layout(LayoutKind.Sequential)]
                struct TestObject {
                    public ptr<TestVTable> VTable;
                    public TestObject(ptr<TestVTable> vtable) { VTable = vtable; }
                }

                [Terrum.Interop.NativeImport("native-narrow-return.dll", "ReturnFalseDirty")]
                [NoThrow]
                extern unsafe bool ReturnFalseDirty();

                [Terrum.Interop.NativeImport("native-narrow-return.dll", "GetFalseDirty")]
                [NoThrow]
                extern unsafe fnptr<NativeAbi, bool> GetFalseDirty();

                [Terrum.Interop.NativeImport("native-narrow-return.dll", "GetFalseDirtyWithReceiver")]
                [NoThrow]
                extern unsafe fnptr<NativeAbi, ptr<TestObject>, bool> GetFalseDirtyWithReceiver();

                [Terrum.Interop.NativeImport("native-narrow-return.dll", "ReturnChar16Dirty")]
                [NoThrow]
                extern unsafe char16 ReturnChar16Dirty();

                [Terrum.Interop.NativeVTableMethod(0)]
                extern unsafe bool Invoke(ptr<TestObject> receiver);

                int32 Main() {
                    unsafe {
                        if (ReturnFalseDirty()) { return 1; }
                        fnptr<NativeAbi, bool> callback = GetFalseDirty();
                        if (callback()) { return 2; }
                        char16 value = ReturnChar16Dirty();
                        if (value != char16(1)) { return 3; }
                        TestVTable table = new TestVTable(GetFalseDirtyWithReceiver());
                        TestObject instance = new TestObject(&table);
                        return Invoke(&instance) ? 4 : 0;
                    }
                }
                """));
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            await File.WriteAllBytesAsync(executablePath, NativeEmitter.EmitWithNativeImportsForTesting(
                compilation.Program!, NativeRuntimePolicy.FailFast)).ConfigureAwait(true);
            _ = await RunProcessAsync(new ProcessStartInfo(executablePath) {
                WorkingDirectory = directory,
            }, TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(executablePath).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(libraryPath).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(exportPath).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(importLibraryPath).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(objectPath).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(assemblyPath).ConfigureAwait(true);
            Directory.Delete(directory);
        }
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
    public void StandardTrRmRoundTripsAndExecutesNativeFunctionPointerBodyOperations()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(NativeCallbackProvider + """
            int32 Main() {
                unsafe {
                    fnptr<NativeAbi, int32, int32> callback = GetAnswer();
                    return callback(42) == 42 ? 0 : -1;
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.Emit(compilation.Program!,
            "native-function-pointer-body");
        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x0004, out var featureBytes, out var featureCount), Is.True);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount);
        Assert.That(features.TryGetFeature(
            TrRmFeatureTableReader.DirectNativeCallsFeatureId, out _), Is.True);
        Assert.That(features.TryGetFeature(
            TrRmFeatureTableReader.NativeCallableEntriesFeatureId, out _), Is.True);

        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.That(loaded.Functions.Single(static function => function.Name == "GetAnswer")
            .IndexedBody, Is.Not.Null);
        Assert.That(loaded.Functions.Single(static function => function.Name == "Main")
            .IndexedBody, Is.Not.Null);
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public void StandardTrRmRejectsMalformedNativeFunctionAddressOperations()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(NativeCallbackProvider + """
            [NoThrow]
            [Terrum.Interop.NativeCallable]
            int64 WrongSignature() { return 0; }

            int32 Main() {
                unsafe {
                    fnptr<NativeAbi, int32, int32> callback = GetAnswer();
                    return callback(42);
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.Emit(compilation.Program!,
            "malformed-native-function-address");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        var wrongSignatureMethodRow = loaded.Functions
            .Select((function, index) => (function, index))
            .Single(static item => item.function.Name == "WrongSignature").index + 1;
        var addressTargetOffset = FindIndexedOperationTargetOffset(artifact, 0x31);

        var mismatchedTarget = (byte[])artifact.Clone();
        mismatchedTarget[addressTargetOffset] = checked((byte)wrongSignatureMethodRow);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(mismatchedTarget));

        var missingUnsafeRegion = (byte[])artifact.Clone();
        RemoveIndexedUnsafeAuthorization(missingUnsafeRegion);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(missingUnsafeRegion));
    }

    [Test]
    public void StandardTrRmRoundTripsNestedNativeFunctionPointerSignatures()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [NoThrow]
            [Terrum.Interop.NativeCallable]
            public int32 Invoke(fnptr<NativeAbi, int32> callback, int32 value) {
                return value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var function = program.Functions.Single(static candidate => candidate.Name == "Invoke");
        Assert.That(function.NativeFunctionPointerType, Is.Not.Null);
        var callableSignature = program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(function.NativeFunctionPointerType!.Value)];
        Assert.That(callableSignature.ParameterTypes, Has.Length.EqualTo(2));
        var callbackType = callableSignature.ParameterTypes[0];
        Assert.That(NativeFunctionPointerTypes.IsType(callbackType), Is.True);
        var callbackSignature = program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(callbackType)];
        Assert.That(callbackSignature.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(callbackSignature.ParameterTypes, Is.Empty);

        var artifact = TrRmExecutableCompiler.Emit(program, "nested-native-callable");
        var artifactIndex = new TrRmArtifactIndex(artifact);
        Assert.That(artifactIndex.TryGetSection(0x0033, out _, out var signatureCount), Is.True);
        Assert.That(signatureCount, Is.EqualTo(program.NativeFunctionPointerSignatures.Length));
        Assert.That(artifactIndex.TryGetSection(0x0011, out var typeSpecBytes,
            out var typeSpecCount), Is.True);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, typeSpecCount,
            0, 0, 0, 0, signatureCount);
        var hasFunctionPointerTypeSpec = false;
        for (var row = 1; row <= typeSpecCount; row++)
        {
            hasFunctionPointerTypeSpec |= typeSpecs.GetRow(row)[0] == 0x0D;
        }
        Assert.That(hasFunctionPointerTypeSpec, Is.True);
        Assert.That(artifactIndex.TryGetSection(0x0004, out var featureBytes,
            out var featureCount), Is.True);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount);
        Assert.That(features.TryGetFeature(
            TrRmFeatureTableReader.NativeBoundaryContractsFeatureId, out _), Is.True);
        Assert.That(features.TryGetFeature(
            TrRmFeatureTableReader.RootlessNativeValuesFeatureId, out _), Is.True);

        var loaded = TrRmExecutableCompiler.Load(artifact);
        var loadedFunction = loaded.Functions.Single(static candidate => candidate.Name == "Invoke");
        Assert.That(loadedFunction.Variables[0].Type, Is.EqualTo(callbackType));
        Assert.That(loaded.NativeFunctionPointerSignatures, Has.Length.EqualTo(
            program.NativeFunctionPointerSignatures.Length));
        for (var index = 0; index < program.NativeFunctionPointerSignatures.Length; index++)
        {
            var expected = program.NativeFunctionPointerSignatures[index];
            var actual = loaded.NativeFunctionPointerSignatures[index];
            Assert.That(actual.ReturnType, Is.EqualTo(expected.ReturnType));
            Assert.That(actual.ParameterTypes.ToArray(), Is.EqualTo(expected.ParameterTypes.ToArray()));
        }
        Assert.That(TrRmExecutableCompiler.Emit(loaded, "nested-native-callable"),
            Is.EqualTo(artifact));

        var reference = TrRmExecutableCompiler.CreateReference(artifact);
        Assert.That(Export(reference, "Invoke").Variables[0].Type, Is.EqualTo(callbackType));
        Assert.That(reference.NativeFunctionPointerSignatures, Has.Length.EqualTo(
            program.NativeFunctionPointerSignatures.Length));

        var malformed = artifact.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformed.AsSpan(FindSectionOffset(malformed, 0x0033)), 0x00FF_FFFF);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));

        Assert.That(signatureCount, Is.EqualTo(2));
        var duplicateSignature = artifact.ToArray();
        var signatureOffset = FindSectionOffset(duplicateSignature, 0x0033);
        var methodOffset = FindSectionOffset(duplicateSignature, 0x0016);
        var firstAnchor = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            duplicateSignature.AsSpan(signatureOffset)));
        var secondAnchor = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            duplicateSignature.AsSpan(signatureOffset + 8)));
        var firstMethodSignature = BinaryPrimitives.ReadUInt32LittleEndian(
            duplicateSignature.AsSpan(methodOffset + ((firstAnchor - 1) * 28) + 8));
        BinaryPrimitives.WriteUInt32LittleEndian(
            duplicateSignature.AsSpan(methodOffset + ((secondAnchor - 1) * 28) + 8),
            firstMethodSignature);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(duplicateSignature));
    }

    [Test]
    public void ReorderedTrRmNestedSignaturesPreserveOutputStorageInPrivateModules()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [NoThrow]
            [Terrum.Interop.NativeCallable]
            public int32 Invoke(fnptr<NativeAbi, int32> callback) {
                return 0;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.Emit(compilation.Program!, "forward-native-signature");
        var reordered = ReverseNativeFunctionPointerSignatureRows(artifact);
        var loaded = TrRmExecutableCompiler.Load(reordered);
        var loadedFunction = loaded.Functions.Single(static candidate => candidate.Name == "Invoke");
        var callableSignatureIndex =
            NativeFunctionPointerTypes.Index(loadedFunction.NativeFunctionPointerType!.Value);
        Assert.That(callableSignatureIndex, Is.EqualTo(0));
        var callableSignature = loaded.NativeFunctionPointerSignatures[callableSignatureIndex];
        Assert.That(NativeFunctionPointerTypes.Index(callableSignature.ParameterTypes[0]), Is.EqualTo(1));

        var producer = ModuleCompiler.CreateProducer(loaded, ["Invoke"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "AcquireCallback")]
            [Terrum.Interop.NativeOutput(0, NonZero)]
            extern int32 AcquireCallback(
                write ptr<fnptr<NativeAbi, fnptr<NativeAbi, int32>, int32>> callback);

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            int32 Placeholder(fnptr<NativeAbi, int32> callback) { return 0; }

            int32 Main() {
                unsafe {
                    mut fnptr<NativeAbi, fnptr<NativeAbi, int32>, int32> callback = &Placeholder;
                    int32 status = AcquireCallback(&callback);
                    if (status != 0) { return 0; }
                    return -1;
                }
            }
            """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program!;
        var mainFunction = consumerProgram.Functions.Single(static function =>
            function.Name == "Main");
        var importCall = mainFunction.Body.Statements.OfType<BoundUnsafe>().Single().Body
            .Statements.OfType<BoundLocal>()
            .Select(static local => local.Initializer)
            .OfType<BoundCall>().Single();
        var outputAddress = (BoundNativeStorageAddress)importCall.Arguments[0];
        var outputPointee = consumerProgram.Functions.Single(static function =>
            function.Name == "AcquireCallback").DirectNativeImport!.Outputs.Single().PointeeType;
        Assert.That(outputAddress.CarrierType,
            Is.EqualTo(mainFunction.Variables[outputAddress.Variable].Type));
        Assert.That(outputAddress.CarrierType, Is.EqualTo(outputPointee));

        var consumer = ModuleCompiler.CreateConsumer(consumerCompilation.Program!, producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.DoesNotThrow(() => SemanticVerifier.VerifyWithDirectNativeImports(linked));
    }

    [Test]
    public void StandardTrRmRequiresFunctionPointerFeaturesOnEveryUsingModule()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [NoThrow]
            [Terrum.Interop.NativeCallable]
            public int32 Invoke(fnptr<NativeAbi, int32> callback) {
                return 0;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(compilation.Program!, "module-feature-closure");

        Assert.DoesNotThrow(() => TrRmStructuralMetadataValidator.Validate(
            SplitSignatureAnchorModule(artifact, includeRootlessFeature: true)));
        Assert.Throws<InvalidDataException>(() => TrRmStructuralMetadataValidator.Validate(
            SplitSignatureAnchorModule(artifact, includeRootlessFeature: false)));
    }

    [Test]
    public void StandardTrRmRoundTripsScalarNativeCallableContracts()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [NoThrow]
            [Terrum.Interop.NativeCallable]
            public float64 Sum(float64 first, int32 ignored, float64 second) {
                return first + second;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var function = program.Functions.Single(static candidate => candidate.Name == "Sum");
        program = program with
        {
            Functions = program.Functions.SetItem(0, function with { NativeCallableEntryName = "Sum|Native" }),
        };
        var artifact = TrRmExecutableCompiler.Emit(program, "native-callable");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        var callable = loaded.Functions.Single(static function => function.Name == "Sum");

        Assert.That(callable.NativeFunctionPointerType, Is.EqualTo(NativeFunctionPointerTypes.Type(0)));
        Assert.That(callable.NativeCallableEntryName, Is.EqualTo("Sum|Native"));
        Assert.That(loaded.NativeFunctionPointerSignatures, Has.Length.EqualTo(1));
        Assert.That(loaded.NativeFunctionPointerSignatures[0].ReturnType, Is.EqualTo(PrimitiveType.Float64));
        Assert.That(loaded.NativeFunctionPointerSignatures[0].ParameterTypes,
            Is.EqualTo(new[] { PrimitiveType.Float64, PrimitiveType.Int32, PrimitiveType.Float64 }));
        Assert.That(callable.IsNoThrow, Is.True);
        Assert.That(callable.RequiresUnsafeCaller, Is.True);
        Assert.That(TrRmExecutableCompiler.Emit(loaded, "native-callable"), Is.EqualTo(artifact));

        var privateProducer = ModuleCompiler.CreateProducer(program, ["Sum"]);
        var privateReference = ModuleCompiler.CreateReference(privateProducer);
        Assert.That(Export(privateReference, "Sum").NativeCallableEntryName, Is.EqualTo("Sum|Native"));
    }

    [Test]
    public void StandardTrRmRoundTripsTypedPointerOnlyContracts()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public unsafe int32 Ignore(ptr<int32> value) { return 42; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.Emit(compilation.Program!, "typed-pointer-only");
        var artifactIndex = new TrRmArtifactIndex(artifact);
        Assert.That(artifactIndex.TryGetSection(0x0004, out var featureBytes,
            out var featureCount), Is.True);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount);
        Assert.That(features.TryGetFeature(
            TrRmFeatureTableReader.NativeBoundaryContractsFeatureId, out _), Is.True);
        Assert.That(features.TryGetFeature(
            TrRmFeatureTableReader.RootlessNativeValuesFeatureId, out _), Is.True);

        var loaded = TrRmExecutableCompiler.Load(artifact);
        var ignored = loaded.Functions.Single(static function => function.Name == "Ignore");
        Assert.That(ignored.RequiresUnsafeCaller, Is.True);
        Assert.That(NativePointerTypes.IsType(ignored.Variables[0].Type), Is.True);
        Assert.That(TrRmExecutableCompiler.Emit(loaded, "typed-pointer-only"), Is.EqualTo(artifact));
    }

    [Test]
    public void RejectsOversizedNestedFunctionPointerTypeIdentityBeforeExpansion()
    {
        var source = new StringBuilder();
        var previousAlias = "A";
        source.Append("alias ").Append(previousAlias)
            .AppendLine(" = fnptr<NativeAbi, int32>;");
        for (var depth = 1; depth <= 10; depth++)
        {
            var alias = ((char)('A' + depth)).ToString();
            var parametersAndReturn = string.Join(", ", Enumerable.Repeat(previousAlias, 9));
            source.Append("alias ").Append(alias).Append(" = fnptr<NativeAbi, ")
                .Append(parametersAndReturn).AppendLine(">;");
            previousAlias = alias;
        }
        source.Append("public ").Append(previousAlias).Append(" Identity(")
            .Append(previousAlias).AppendLine(" callback) { return callback; }");

        Assert.That(source.Length, Is.LessThan(1024));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source.ToString()));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var error = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(compilation.Program!, ["Identity"]));
        Assert.That(error!.Message, Does.Contain("type identity exceeds the string limit"));
    }

    [Test]
    public void FunctionPointerTypeDiagnosticsBoundRecursiveFormatting()
    {
        var source = new StringBuilder();
        var previousAlias = "A";
        source.Append("alias ").Append(previousAlias)
            .AppendLine(" = fnptr<NativeAbi, int32>;");
        for (var depth = 1; depth <= 10; depth++)
        {
            var alias = ((char)('A' + depth)).ToString();
            var parametersAndReturn = string.Join(", ", Enumerable.Repeat(previousAlias, 9));
            source.Append("alias ").Append(alias).Append(" = fnptr<NativeAbi, ")
                .Append(parametersAndReturn).AppendLine(">;");
            previousAlias = alias;
        }
        source.Append("public ").Append(previousAlias)
            .AppendLine(" Reject() { return true; }");

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source.ToString()));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Max(static diagnostic => diagnostic.Message.Length),
            Is.LessThanOrEqualTo(512));
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Message.Contains("...", StringComparison.Ordinal)),
            Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void StandardTrRmNativeCallableReferenceBindsUnsafeCalls()
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [NoThrow]
            [Terrum.Interop.NativeCallable]
            public int32 GetAnswer(int32 value) { return value; }
            """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(providerCompilation.Program!, "native-callable-provider");
        var reference = TrRmExecutableCompiler.CreateReference(artifact);

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe { return GetAnswer(42); }
            }
            """), reference);

        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var imported = reference.Functions.Single(static function => function.Name == "GetAnswer");
        Assert.That(imported.IsExternal, Is.True);
        Assert.That(imported.NativeFunctionPointerType, Is.Not.Null);
        Assert.That(reference.NativeFunctionPointerSignatures, Has.Length.EqualTo(1));
    }

    [Test]
    public void StandardTrRmRoundTripsImportedNativeCallableAddressAndIndirectCall()
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [NoThrow]
            [Terrum.Interop.NativeCallable]
            public int32 GetAnswer(int32 value) { return value; }
            """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.Emit(
            providerCompilation.Program!, "native-callable-provider");
        var reference = TrRmExecutableCompiler.CreateReference(providerArtifact);
        var imported = reference.Functions.Single(static function => function.Name == "GetAnswer");
        Assert.That(imported.ModuleImport?.ProducerIdentity, Is.EqualTo("native-callable-provider"));
        Assert.That(imported.ModuleImport?.ExportKey, Is.EqualTo("GetAnswer"));

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    fnptr<NativeAbi, int32, int32> callback = &GetAnswer;
                    return callback(42);
                }
            }
            """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.Emit(
            consumerCompilation.Program!, "native-callable-consumer");
        var artifactIndex = new TrRmArtifactIndex(artifact);
        Assert.That(artifactIndex.TryGetSection(0x10, out _, out var moduleRefCount), Is.True);
        Assert.That(moduleRefCount, Is.EqualTo(1));
        Assert.That(artifactIndex.TryGetSection(0x1C, out _, out var importCount), Is.True);
        Assert.That(importCount, Is.EqualTo(1));
        Assert.That(artifactIndex.TryGetSection(0x31, out _, out _), Is.False);

        var loaded = TrRmExecutableCompiler.Load(artifact);
        var loadedImported = loaded.Functions.Single(static function => function.Name == "GetAnswer");
        Assert.That(loadedImported.IsExternal, Is.True);
        Assert.That(loadedImported.ModuleImport?.ProducerIdentity, Is.EqualTo("native-callable-provider"));
        Assert.That(loadedImported.ModuleImport?.ExportKey, Is.EqualTo("GetAnswer"));

        var main = loaded.Functions.Single(static function => function.Name == "Main");
        var operations = main.IndexedBody!.Blocks.SelectMany(static block => block.Operations).ToArray();
        var address = operations.OfType<BoundLocal>()
            .Select(static local => local.Initializer)
            .OfType<BoundNativeFunctionAddress>()
            .Single();
        Assert.That(loaded.Functions[address.Function].ModuleImport,
            Is.EqualTo(loadedImported.ModuleImport));
        Assert.That(operations.OfType<BoundLocal>()
            .Any(static local => local.Initializer is BoundIndirectCall), Is.True);
    }

    [Test]
    public void StandardTrRmMapsMethodDefRowsPastSignatureAnchors()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow]
            int32 Unused(int32 value) { return 10; }

            int32 Chosen(int32 value) { return 20; }

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            int32 CallbackChosen(int32 value) { return 20; }

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            int32 CallbackDecoy(int32 value) { return 30; }

            int32 Fill(init ref<int32> output) {
                output = 0;
                return Chosen(1);
            }

            int32 Main() {
                unsafe {
                    fnptr<NativeAbi, int32, int32> callback = &CallbackChosen;
                    int32 result = callback(3);
                    return Chosen(result);
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.Emit(compilation.Program!,
            "method-row-function-pointer");
        var reordered = MoveNativeFunctionPointerSignatureAnchorFirst(artifact);
        var loaded = TrRmExecutableCompiler.Load(reordered);

        var callbackIndex = Enumerable.Range(0, loaded.Functions.Length)
            .Single(index => loaded.Functions[index].Name == "CallbackChosen");
        var chosenIndex = Enumerable.Range(0, loaded.Functions.Length)
            .Single(index => loaded.Functions[index].Name == "Chosen");
        var main = loaded.Functions.Single(static function => function.Name == "Main");
        var mainOperations = main.IndexedBody!.Blocks.SelectMany(static block => block.Operations)
            .ToArray();
        var address = mainOperations
            .OfType<BoundLocal>()
            .Select(static local => local.Initializer)
            .OfType<BoundNativeFunctionAddress>()
            .Single();
        Assert.That(address.Function, Is.EqualTo(callbackIndex));
        Assert.That(mainOperations.OfType<BoundLocal>()
            .Select(static local => local.Initializer)
            .OfType<BoundCall>()
            .Single().Function, Is.EqualTo(chosenIndex));

        var fill = loaded.Functions.Single(static function => function.Name == "Fill");
        var outputCall = fill.Body.Statements.OfType<BoundLocal>()
            .Select(static local => local.Initializer)
            .OfType<BoundCall>()
            .Single();
        Assert.That(outputCall.Function, Is.EqualTo(chosenIndex));
    }

    [Test]
    public async Task StandardTrRmCallableImportBindsAndRunsOnlyWithItsSelectedProviderAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [NoThrow]
            [Terrum.Interop.NativeCallable]
            public int32 GetAnswer(int32 value) { return value; }
            """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.Emit(
            providerCompilation.Program!, "NativeCallableProvider");
        var reference = TrRmExecutableCompiler.CreateReference(providerArtifact);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    fnptr<NativeAbi, int32, int32> callback = &GetAnswer;
                    return callback(42);
                }
            }
            """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));

        var provider = NativeModuleCompiler.CreateProducer(providerCompilation.Program!,
            ["GetAnswer"], "NativeCallableProvider", NativeRuntimePolicy.FailFast);
        var wrongProvider = NativeModuleCompiler.CreateProducer(providerCompilation.Program!,
            ["GetAnswer"], "WrongNativeCallableProvider", NativeRuntimePolicy.FailFast);
        var secondCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Other() { return 0; }
            """));
        Assert.That(secondCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, secondCompilation.Diagnostics));
        var secondProvider = NativeModuleCompiler.CreateProducer(secondCompilation.Program!,
            ["Other"], "NativeCallableSecond", NativeRuntimePolicy.FailFast);
        Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, wrongProvider, NativeRuntimePolicy.FailFast));
        Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateConsumerSet(
            consumerCompilation.Program!, [wrongProvider, secondProvider], NativeRuntimePolicy.FailFast));
        Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateGraphConsumer(
            consumerCompilation.Program!, [wrongProvider], NativeRuntimePolicy.FailFast));

        var consumer = NativeModuleCompiler.CreateConsumer(consumerCompilation.Program!,
            provider, NativeRuntimePolicy.FailFast);
        Assert.That(await NativeModuleCompiler.RunAsync(consumer, provider,
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast).ConfigureAwait(true),
            Is.EqualTo(42));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(consumerCompilation.Program!,
            [provider], NativeRuntimePolicy.FailFast);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(graphConsumer, [provider],
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast).ConfigureAwait(true),
            Is.EqualTo(42));
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

    private static int FindSectionOffset(byte[] artifact, ushort sectionKind)
    {
        var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(12, 4)));
        for (var index = 0; index < count; index++)
        {
            var entry = artifact.AsSpan(32 + checked((int)index * 16), 16);
            if (BinaryPrimitives.ReadUInt16LittleEndian(entry) == sectionKind)
            {
                return checked((int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]));
            }
        }

        throw new InvalidDataException($"TRRM section 0x{sectionKind:X4} was not found.");
    }

    private static byte[] MoveNativeFunctionPointerSignatureAnchorFirst(byte[] artifact)
    {
        var index = new TrRmArtifactIndex(artifact);
        if (!index.TryGetSection(0x0033, out var signatureBytes, out var signatureCount)
            || signatureCount != 1
            || !index.TryGetSection(0x0016, out var methodBytes, out var methodCount))
        {
            throw new InvalidDataException("Expected one native function-pointer signature anchor.");
        }

        var anchorRow = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(signatureBytes));
        if (anchorRow != methodCount || methodCount < 3)
        {
            throw new InvalidDataException("The signature anchor is not the final MethodDef row.");
        }

        var methodRowMap = new int[methodCount + 1];
        methodRowMap[anchorRow] = 1;
        methodRowMap[1] = methodCount;
        for (var row = 2; row < anchorRow; row++)
        {
            methodRowMap[row] = row;
        }

        var reorderedMethods = new byte[methodBytes.Length];
        for (var oldRow = 1; oldRow <= methodCount; oldRow++)
        {
            methodBytes.Slice((oldRow - 1) * 28, 28).CopyTo(
                reorderedMethods.AsSpan((methodRowMap[oldRow] - 1) * 28, 28));
        }

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var entry = 0; entry < index.SectionCount; entry++)
        {
            index.GetSection(entry, out var kind, out var flags, out var sectionBytes,
                out var recordCount);
            var bytes = sectionBytes.ToArray();
            if (kind == 0x0016)
            {
                bytes = reorderedMethods;
            }
            else if (kind == 0x0001)
            {
                for (var row = 0; row < recordCount; row++)
                {
                    var entryPoint = BinaryPrimitives.ReadUInt32LittleEndian(
                        bytes.AsSpan((row * 44) + 12));
                    if (entryPoint != 0)
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((row * 44) + 12),
                            (uint)RemapMethodRow(entryPoint, methodRowMap));
                    }
                }
            }
            else if (kind == 0x001B)
            {
                var dataOffset = (recordCount + 1) * sizeof(uint);
                for (var row = 0; row < recordCount; row++)
                {
                    var start = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                        bytes.AsSpan(row * sizeof(uint))));
                    var owner = BinaryPrimitives.ReadUInt32LittleEndian(
                        bytes.AsSpan(dataOffset + start));
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(dataOffset + start),
                        RemapMethodDefinitionId(owner, methodRowMap));
                }
            }
            else if (kind is 0x001C or 0x001D)
            {
                var stride = kind == 0x001C ? 12 : 8;
                var definitionOffset = kind == 0x001C ? 8 : 0;
                for (var row = 0; row < recordCount; row++)
                {
                    var offset = (row * stride) + definitionOffset;
                    var definition = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset),
                        RemapMethodDefinitionId(definition, methodRowMap));
                }
            }
            else if (kind is 0x0030 or 0x0031 or 0x0032 or 0x0033)
            {
                var stride = kind is 0x0030 or 0x0032 ? 16 : 8;
                for (var row = 0; row < recordCount; row++)
                {
                    var offset = row * stride;
                    var methodRow = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset),
                        (uint)RemapMethodRow(methodRow, methodRowMap));
                }
                if (kind != 0x0033)
                {
                    var rows = new List<byte[]>(recordCount);
                    for (var row = 0; row < recordCount; row++)
                    {
                        rows.Add(bytes.AsSpan(row * stride, stride).ToArray());
                    }
                    rows.Sort(static (left, right) =>
                        BinaryPrimitives.ReadUInt32LittleEndian(left)
                            .CompareTo(BinaryPrimitives.ReadUInt32LittleEndian(right)));
                    bytes = rows.SelectMany(static row => row).ToArray();
                }
            }
            sections.Add(new TrRmSection(kind, flags, (uint)recordCount, bytes));
        }

        return TrRmArtifactWriter.Encode(sections);
    }

    private static uint RemapMethodDefinitionId(uint definition, int[] methodRowMap)
    {
        if (definition >> 24 != 0x16)
        {
            return definition;
        }

        var row = (int)(definition & 0x00FF_FFFF);
        return 0x1600_0000u | (uint)RemapMethodRow((uint)row, methodRowMap);
    }

    private static int RemapMethodRow(uint row, int[] methodRowMap)
    {
        var methodRow = checked((int)row);
        if (methodRow <= 0 || methodRow >= methodRowMap.Length)
        {
            throw new InvalidDataException("A MethodDef reference is outside the fixture map.");
        }

        return methodRowMap[methodRow];
    }

    private static int FindIndexedOperationTargetOffset(byte[] artifact, byte opcode)
    {
        var index = new TrRmArtifactIndex(artifact);
        if (!index.TryGetSection(0x0020, out var bodyBytes, out var bodyCount))
        {
            throw new InvalidDataException("TRRM fixture is missing its Body section.");
        }

        var bodies = new TrRmBodyTableReader(bodyBytes, bodyCount);
        var bodySectionOffset = FindSectionOffset(artifact, 0x0020);
        var bodyDataOffset = bodySectionOffset + ((bodyCount + 1) * sizeof(uint));
        var matches = new List<int>();
        for (var bodyRow = 1; bodyRow <= bodyCount; bodyRow++)
        {
            var body = bodies.GetRow(bodyRow);
            if (body.IsEmpty || (body[0] & 0xFC) != 0x08 || (body[0] & 3) == 3)
            {
                continue;
            }

            var bodyStart = bodyDataOffset + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                artifact.AsSpan(bodySectionOffset + ((bodyRow - 1) * sizeof(uint)))));
            var operation = body.IndexOf(opcode);
            if (operation >= 0)
            {
                matches.Add(bodyStart + operation + 2);
            }
        }

        if (matches.Count != 1)
        {
            throw new InvalidDataException("TRRM fixture does not contain one function-address operation.");
        }

        return matches[0];
    }

    private static void RemoveIndexedUnsafeAuthorization(byte[] artifact)
    {
        var index = new TrRmArtifactIndex(artifact);
        if (!index.TryGetSection(0x0020, out var bodyBytes, out var bodyCount))
        {
            throw new InvalidDataException("TRRM fixture is missing its Body section.");
        }

        var bodies = new TrRmBodyTableReader(bodyBytes, bodyCount);
        var bodySectionOffset = FindSectionOffset(artifact, 0x0020);
        var bodyDataOffset = bodySectionOffset + ((bodyCount + 1) * sizeof(uint));
        var changedRegions = 0;
        for (var bodyRow = 1; bodyRow <= bodyCount; bodyRow++)
        {
            var body = bodies.GetRow(bodyRow);
            if (body.IsEmpty || (body[0] & 0xFC) != 0x08 || (body[0] & 3) == 3)
            {
                continue;
            }

            var width = 1 << (body[0] & 3);
            var position = 1;
            _ = ReadIndexedValue(body, ref position, width);
            _ = ReadIndexedValue(body, ref position, width);
            var regionCount = ReadIndexedValue(body, ref position, width);
            var typeCount = ReadIndexedValue(body, ref position, width);
            position += typeCount * sizeof(uint);
            var bodyStart = bodyDataOffset + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                artifact.AsSpan(bodySectionOffset + ((bodyRow - 1) * sizeof(uint)))));
            for (var region = 0; region < regionCount; region++)
            {
                if (body[position] == 6)
                {
                    artifact[bodyStart + position] = 1;
                    changedRegions++;
                }
                position += 1 + (4 * width);
            }
        }

        if (changedRegions == 0)
        {
            throw new InvalidDataException("TRRM fixture does not contain an unsafe indexed region.");
        }
    }

    private static int ReadIndexedValue(ReadOnlySpan<byte> bytes, ref int position, int width)
    {
        var value = width switch
        {
            1 => bytes[position],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes[position..]),
            4 => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[position..])),
            _ => throw new InvalidDataException("Invalid indexed Body width."),
        };
        position += width;
        return value;
    }

    private static byte[] SplitSignatureAnchorModule(byte[] artifact, bool includeRootlessFeature)
    {
        var index = new TrRmArtifactIndex(artifact);
        var sections = new List<TrRmSection>(index.SectionCount);
        if (!index.TryGetSection(0x0016, out var methodBytes, out _))
        {
            throw new InvalidOperationException("Missing MethodDef section.");
        }
        var methodNameRow = BinaryPrimitives.ReadUInt32LittleEndian(methodBytes[4..]);

        for (var entry = 0; entry < index.SectionCount; entry++)
        {
            index.GetSection(entry, out var kind, out var flags, out var sectionBytes,
                out var recordCount);
            var bytes = sectionBytes.ToArray();
            if (kind == 0x0001)
            {
                var modules = new byte[bytes.Length + 44];
                bytes.CopyTo(modules, 0);
                bytes.CopyTo(modules, bytes.Length);
                var firstModule = modules.AsSpan(0, 44);
                var secondModule = modules.AsSpan(44, 44);
                BinaryPrimitives.WriteUInt32LittleEndian(firstModule[20..],
                    includeRootlessFeature ? 4u : 1u);
                BinaryPrimitives.WriteUInt32LittleEndian(secondModule, methodNameRow);
                BinaryPrimitives.WriteUInt32LittleEndian(secondModule[12..], 0);
                BinaryPrimitives.WriteUInt32LittleEndian(secondModule[16..], 1);
                BinaryPrimitives.WriteUInt32LittleEndian(secondModule[20..], 2);
                BinaryPrimitives.WriteUInt32LittleEndian(secondModule[36..], 2);
                bytes = modules;
                recordCount++;
            }
            sections.Add(new TrRmSection(kind, flags, (uint)recordCount, bytes));
        }

        return TrRmArtifactWriter.Encode(sections);
    }

    private static byte[] ReverseNativeFunctionPointerSignatureRows(byte[] artifact)
    {
        var index = new TrRmArtifactIndex(artifact);
        if (!index.TryGetSection(0x0033, out var signatureBytes, out var signatureCount)
            || signatureCount < 2)
        {
            throw new InvalidOperationException("TRRM fixture does not contain nested function-pointer signatures.");
        }

        var signatureMapping = new int[signatureCount + 1];
        for (var row = 1; row <= signatureCount; row++)
        {
            signatureMapping[row] = signatureCount + 1 - row;
        }
        var reorderedSignatures = new byte[signatureBytes.Length];
        for (var row = 1; row <= signatureCount; row++)
        {
            signatureBytes.Slice((row - 1) * 8, 8).CopyTo(
                reorderedSignatures.AsSpan((signatureMapping[row] - 1) * 8, 8));
        }

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var entry = 0; entry < index.SectionCount; entry++)
        {
            index.GetSection(entry, out var kind, out var flags, out var sectionBytes, out var recordCount);
            var bytes = sectionBytes.ToArray();
            if (kind == 0x0033)
            {
                bytes = reorderedSignatures;
            }
            else if (kind == 0x0011)
            {
                var dataOffset = checked(((int)recordCount + 1) * sizeof(uint));
                for (var row = 1; row <= recordCount; row++)
                {
                    var start = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                        bytes.AsSpan((row - 1) * sizeof(uint))));
                    var end = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                        bytes.AsSpan(row * sizeof(uint))));
                    var type = bytes.AsSpan(dataOffset + start, end - start);
                    if (type.Length == 6 && type[0] == 0x0D && type[1] == 0)
                    {
                        var signatureRow = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(type[2..]));
                        BinaryPrimitives.WriteUInt32LittleEndian(type[2..],
                            checked((uint)signatureMapping[signatureRow]));
                    }
                }
            }
            sections.Add(new TrRmSection(kind, flags, (uint)recordCount, bytes));
        }

        return TrRmArtifactWriter.Encode(sections);
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
