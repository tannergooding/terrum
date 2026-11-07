// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Native.Interop;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class NativeFunctionPointerTests
{
    private const string NativeCallbackProvider = """
        [Terrum.Interop.NativeCallable]
        int32 Answer(int32 value) { return value; }


        public unsafe [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> GetAnswer() {
            unsafe { return &Answer; }
        }
        """;

    [Test]
    public void VerifierRejectsFunctionAddressAfterRemovingItsUnsafeScope()
    {
        var compilation = Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            int32 Answer() { return 42; }

            int32 Main() {
                unsafe {
                    [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32> answer = &Answer;
                    return 0;
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var forged = RemoveFunctionUnsafeScope(program, "Main");

        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
    }

    [Test]
    public void VerifierRejectsIndirectCallAfterRemovingItsUnsafeScope()
    {
        var compilation = Create(SyntaxTree.Parse("""
            unsafe int32 Invoke([Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> callback, int32 value) {
                unsafe { return callback(value); }
            }

            int32 Main() { return 0; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var forged = RemoveFunctionUnsafeScope(program, "Invoke");

        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
    }

    [Test]
    public void DirectEmissionRejectsCallerUnsafeCallOutsideLexicalUnsafeScope()
    {
        var compilation = Create(SyntaxTree.Parse("""
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

        _ = Assert.Throws<ArgumentException>(() =>
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
        var compilation = Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            float64 Sum(float64 first, int32 ignored1, float64 second, int32 ignored2, float64 third) {
                return first + second + third;
            }

            int32 Main() {
                unsafe {
                    [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<float64, int32, float64, int32, float64, float64> sum = &Sum;
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
    public void NativeCallableDoesNotRequireNoThrowAndProvesTargetBody()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            int32 Identity(int32 value) { return value; }

            [Terrum.Interop.NativeCallable]
            int32 Forward(int32 value) {
                unsafe { return Identity(value); }
            }

            [Terrum.Interop.NativeCallable]
            int32 Invoke([Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> callback, int32 value) {
                unsafe { return callback(value); }
            }

            int32 Main() {
                unsafe {
                    [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> identity = &Identity;
                    return Forward(42) == 42 && Invoke(identity, 42) == 42 ? 0 : -1;
                }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        foreach (var function in compilation.Program!.Functions.Where(
            static function => function.NativeFunctionPointerType is not null))
        {
            Assert.That(function.NoFailureProof, Is.False, function.Name);
        }
        Assert.That(NativeJit.Execute(compilation.Program, NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [TestCase("""
        [Terrum.Interop.NativeCallable]
        int32 Fails(int32 value) { return 42 / value; }
        int32 Main() { return 0; }
        """)]
    [TestCase("""
        int32 Helper(int32 value) { return value; }
        [Terrum.Interop.NativeCallable]
        int32 Forwards(int32 value) { return Helper(value); }
        int32 Main() { return 0; }
        """)]
    public void NativeCallableRequiresNoFailureEvidence(string source)
    {
        var compilation = Create(SyntaxTree.Parse(source));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030" && diagnostic.Message.Contains("NativeCallable", StringComparison.Ordinal)),
            Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NativeCallableMayCallNativeImportWithoutGeneralNoThrow()
    {
        var compilation = CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("synthetic.dll", "ReadValue")]
            extern unsafe int32 ReadValue();

            [Terrum.Interop.NativeVTableMethod(3)]
            extern unsafe int32 Dispatch(ptr<void> receiver);

            [Terrum.Interop.NativeCallable]
            public int32 Callback() {
                unsafe { return ReadValue(); }
            }

            [Terrum.Interop.NativeCallable]
            public int32 VTableCallback(ptr<void> receiver) {
                unsafe { return Dispatch(receiver); }
            }
            """), BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(program.Functions.Select(static function => function.Name),
            Is.EqualTo(["ReadValue", "Dispatch", "Callback", "VTableCallback"]));
        var callback = program.Functions.Single(static function => function.Name == "Callback");
        var imported = program.Functions.Single(static function => function.Name == "ReadValue");
        Assert.That(callback.IsExternal, Is.False);
        Assert.That(callback.Body.Statements, Is.Not.Empty);
        Assert.That(imported.IsExternal, Is.True);
        Assert.That(imported.DirectNativeImport, Is.Not.Null);
        Assert.That(callback.NoFailureProof, Is.False);
        Assert.That(callback.NativeFunctionPointerType, Is.Not.Null);
        var vtableCallback = program.Functions.Single(static function => function.Name == "VTableCallback");
        Assert.That(vtableCallback.NoFailureProof, Is.False);
        Assert.That(vtableCallback.NativeFunctionPointerType, Is.Not.Null);

        var privateProducer = ModuleCompiler.CreateProducer(program, ["Callback", "VTableCallback"]);
        var privateReference = ModuleCompiler.CreateReference(privateProducer);
        var privateCallback = privateReference.Functions.Single(static function => function.Name == "Callback");
        Assert.That(privateCallback.NoFailureProof, Is.False);
        Assert.That(privateCallback.NativeFunctionPointerType, Is.Not.Null);
        var privateVTableCallback = privateReference.Functions
            .Single(static function => function.Name == "VTableCallback");
        Assert.That(privateVTableCallback.NoFailureProof, Is.False);
        Assert.That(privateVTableCallback.NativeFunctionPointerType, Is.Not.Null);
        var privateConsumerCompilation = Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                unsafe {
                    if (Callback() != 0) { return 1; }
                    return VTableCallback(null);
                }
            }
            """), privateReference, BoundRuntimePolicy.FailFast);
        Assert.That(privateConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, privateConsumerCompilation.Diagnostics));
        var privateConsumer = ModuleCompiler.CreateConsumer(
            privateConsumerCompilation.Program!, privateProducer);
        var nullPointerType = PrimitiveType.NativeRawPointer;
        var nullPointerPayloadOffset = -1;
        for (var offset = 0;
            offset <= privateConsumer.Length - 21; offset++)
        {
            if (privateConsumer[offset] == 1
                && BinaryPrimitives.ReadInt32LittleEndian(
                    privateConsumer.AsSpan(offset + 1, sizeof(int)))
                    == (int)nullPointerType
                && BinaryPrimitives.ReadInt64LittleEndian(
                    privateConsumer.AsSpan(offset + 13, sizeof(long))) == 0)
            {
                nullPointerPayloadOffset = offset + 13;
                break;
            }
        }
        Assert.That(nullPointerPayloadOffset, Is.GreaterThanOrEqualTo(0));
        var nonNullPointerConsumer = privateConsumer.ToArray();
        nonNullPointerConsumer[nullPointerPayloadOffset] = 1;
        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.Link(nonNullPointerConsumer, privateProducer));
        var linkedCallback = ModuleCompiler.Link(privateConsumer, privateProducer);
        foreach (var name in new[] { "Callback", "VTableCallback" })
        {
            var linkedFunction = linkedCallback.Functions.Single(function => function.Name == name);
            Assert.That(linkedFunction.NoFailureProof, Is.False, name);
            Assert.That(linkedFunction.NativeFunctionPointerType, Is.Not.Null, name);
        }

        var loaded = TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(
            TrRmExecutableCompiler.EmitDevelopmentNativeConventionsDraft(
                program, "native-callable-import"));
        var loadedCallback = loaded.Functions.Single(static function => function.Name == "Callback");
        Assert.That(loadedCallback.NoFailureProof, Is.False);
        Assert.That(loadedCallback.NativeFunctionPointerType, Is.Not.Null);
    }

    [Test]
    public void NativeImportCanBeAddressedAndIndirectlyInvoked()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("synthetic.dll", "ReadValue")]
            extern unsafe int32 ReadValue(int32 input);

            int32 Invoke(int32 value) {
                unsafe {
                    var readValue = &ReadValue;
                    return readValue(value);
                }
            }
            """), reference, BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var imported = program.Functions.Single(static function =>
            function.Name == "ReadValue");
        var invoke = program.Functions.Single(static function =>
            function.Name == "Invoke");
        var addresses = new List<BoundNativeFunctionAddress>();
        var calls = new List<BoundIndirectCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(invoke.Body, expression =>
        {
            if (expression is BoundNativeFunctionAddress address)
            {
                addresses.Add(address);
            }
            if (expression is BoundIndirectCall call)
            {
                calls.Add(call);
            }
        }), Is.True);

        var address = addresses.Single();
        var call = calls.Single();
        Assert.That(address.Function, Is.EqualTo(program.Functions.IndexOf(imported)));
        Assert.That(call.Target.Type, Is.EqualTo(address.Type));
        Assert.That(call.Arguments, Has.Length.EqualTo(1));
        var signature = program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(address.Type)];
        Assert.That(signature.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Unmanaged));
        Assert.That(signature.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(signature.ParameterTypes,
            Is.EqualTo([PrimitiveType.Int32]));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void NativeJitExecutesIndirectNativeOutputUnderItsResultCondition(
        NativeRuntimePolicy runtimePolicy)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var boundRuntimePolicy = runtimePolicy == NativeRuntimePolicy.CheckedExceptions
            ? BoundRuntimePolicy.CheckedExceptions : BoundRuntimePolicy.FailFast;
        var reference = new CompilationReference([], [], boundRuntimePolicy,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Create(SyntaxTree.Parse("""
            [param(counter): Terrum.Interop.NativeOutput(
                When: Terrum.Interop.NativeOutputCondition.NonZero)]
            [Terrum.Interop.NativeImport("kernel32.dll", "QueryPerformanceCounter")]
            extern unsafe int32 QueryPerformanceCounter(write ptr<int64> counter);

            int32 Main() {
                init int64 counter;
                unsafe {
                    var query = &QueryPerformanceCounter;
                    int32 status = query(&init counter);
                    if (status != 0) { return counter > 0 ? 0 : -1; }
                }
                return -2;
            }
            """), reference, boundRuntimePolicy);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var image = NativeEmitter.EmitWithNativeImports(
            compilation.Program!, runtimePolicy);
        Assert.That(Encoding.ASCII.GetString(image),
            Does.Contain("QueryPerformanceCounter").And.Contain("KERNEL32.dll"));
        Assert.That(NativeJit.Execute(compilation.Program!, runtimePolicy), Is.Zero);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void NativeJitExecutesAValidatedSystem32IndirectPointerInput(
        NativeRuntimePolicy runtimePolicy)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var boundRuntimePolicy = runtimePolicy == NativeRuntimePolicy.CheckedExceptions
            ? BoundRuntimePolicy.CheckedExceptions : BoundRuntimePolicy.FailFast;
        var reference = new CompilationReference([], [], boundRuntimePolicy,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("kernel32.dll", "CompareStringOrdinal")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(3, Read, NoEscape, 1)]
            extern unsafe int32 CompareStringOrdinal(ptr<char16> first, int32 firstLength,
                ptr<char16> second, int32 secondLength, int32 ignoreCase);

            int32 Main() {
                char16 first = 'A';
                char16 second = 'A';
                unsafe {
                    var compare = &CompareStringOrdinal;
                    return compare(&first, 1, &second, 1, 0) - 2;
                }
            }
            """), reference, boundRuntimePolicy);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var image = NativeEmitter.EmitWithNativeImports(
            compilation.Program!, runtimePolicy);
        Assert.That(Encoding.ASCII.GetString(image),
            Does.Contain("CompareStringOrdinal").And.Contain("KERNEL32.dll"));
        Assert.That(NativeJit.Execute(compilation.Program!, runtimePolicy), Is.Zero);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void NativeJitExecutesIndirectNativeOutputComplementWithStackArgument(
        NativeRuntimePolicy runtimePolicy)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var boundRuntimePolicy = runtimePolicy == NativeRuntimePolicy.CheckedExceptions
            ? BoundRuntimePolicy.CheckedExceptions : BoundRuntimePolicy.FailFast;
        var reference = new CompilationReference([], [], boundRuntimePolicy,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeParameter(5, Read, NoEscape, 0)]
            [param(bytesRead): Terrum.Interop.NativeOutput(
                When: Terrum.Interop.NativeOutputCondition.NonZero)]
            [Terrum.Interop.NativeImport("kernel32.dll", "ReadFile")]
            extern unsafe int32 ReadFile(
                ptr<void> file, ptr<void> buffer, uint32 bytesToRead,
                write ptr<uint32> bytesRead, ptr<void> overlapped);

            int32 Main() {
                init uint32 bytesRead;
                unsafe {
                    var readFile = &ReadFile;
                    int32 status = readFile(null, null, 0, &init bytesRead, null);
                    if (status == 0) { return 0; }
                }
                return int32(bytesRead);
            }
            """), reference, boundRuntimePolicy);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var image = NativeEmitter.EmitWithNativeImports(
            compilation.Program!, runtimePolicy);
        Assert.That(Encoding.ASCII.GetString(image),
            Does.Contain("ReadFile").And.Contain("KERNEL32.dll"));
        Assert.That(NativeJit.Execute(compilation.Program!, runtimePolicy), Is.Zero);
    }

    [Test]
    public void IndirectNativeOutputCarriesDependencyToItsSourceFormal()
    {
        var nativeRegistration = NativeCoreRegistrationTests.CreateRegistration();
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: nativeRegistration,
            layoutCoreRegistration: NativeCoreRegistrationTests.CreateLayoutRegistration(
                nativeRegistration));
        var compilation = CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct Packet { public ptr<void> Value; }

            [param(packet): DependsOn(nameof(source))]
            [param(packet): ForwardsBacking(nameof(source))]
            [param(packet): Terrum.Interop.NativeOutput(
                When: Terrum.Interop.NativeOutputCondition.NonZero)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            [Terrum.Interop.NativeImport("synthetic.dll", "ReadPacket")]
            extern unsafe int32 ReadPacket(ptr<void> source, write ptr<Packet> packet);

            int32 Probe(ptr<void> source) {
                init Packet packet;
                unsafe {
                    var readPacket = &ReadPacket;
                    int32 status = readPacket(source, &init packet);
                    if (status != 0 && packet.Value == null) { return 0; }
                }
                return -1;
            }
            """), reference, BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var imported = program.Functions.Single(static function =>
            function.Name == "ReadPacket");
        var probe = program.Functions.Single(static function => function.Name == "Probe");
        var indirectCall = new List<BoundIndirectCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(probe.Body, expression =>
        {
            if (expression is BoundIndirectCall call)
            {
                indirectCall.Add(call);
            }
        }), Is.True);

        var call = indirectCall.Single();
        Assert.That(call.Arguments[1], Is.TypeOf<BoundNativeOutputReservation>());
        var signature = program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(call.Target.Type)];
        Assert.That(signature.Dependencies, Is.EqualTo(
        [
            new NativeFunctionPointerDependency(2, 1, 2, 1, 1, 0),
        ]));
        Assert.That(signature.NativeParameters,
            Is.EqualTo(
            [
                new DirectNativeParameter(0, PrimitiveType.Void,
                    NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 0),
            ]));
        Assert.That(NativeFunctionPointerContract.MatchesAddressableFunction(
            imported, call.Target.Type, program.NativeFunctionPointerSignatures), Is.True);
    }

    [Test]
    public void IndirectNativeOutputRejectsReadingOnItsComplementResult()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = CreateLibrary(SyntaxTree.Parse("""
            [param(counter): Terrum.Interop.NativeOutput(
                When: Terrum.Interop.NativeOutputCondition.NonZero)]
            [Terrum.Interop.NativeImport("kernel32.dll", "QueryPerformanceCounter")]
            extern unsafe int32 QueryPerformanceCounter(write ptr<int64> counter);

            int32 Probe() {
                init int64 counter;
                unsafe {
                    var query = &QueryPerformanceCounter;
                    int32 status = query(&init counter);
                    if (status == 0) { return int32(counter); }
                }
                return 0;
            }
            """), reference, BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void IndirectNativeOutputRejectsReadingAfterDiscardedStatus()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = CreateLibrary(SyntaxTree.Parse("""
            [param(counter): Terrum.Interop.NativeOutput(
                When: Terrum.Interop.NativeOutputCondition.NonZero)]
            [Terrum.Interop.NativeImport("kernel32.dll", "QueryPerformanceCounter")]
            extern unsafe int32 QueryPerformanceCounter(write ptr<int64> counter);

            int32 Probe() {
                init int64 counter;
                unsafe {
                    var query = &QueryPerformanceCounter;
                    query(&init counter);
                }
                return int32(counter);
            }
            """), reference, BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void IndirectNativeOutputRejectsDuplicateReservationTargets()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = CreateLibrary(SyntaxTree.Parse("""
            [param(first): Terrum.Interop.NativeOutput(
                When: Terrum.Interop.NativeOutputCondition.NonZero)]
            [param(second): Terrum.Interop.NativeOutput(
                When: Terrum.Interop.NativeOutputCondition.NonZero)]
            [Terrum.Interop.NativeImport("synthetic.dll", "ReadTwoValues")]
            extern unsafe int32 ReadTwoValues(
                write ptr<int32> first, write ptr<int32> second);

            int32 Probe() {
                init int32 value;
                unsafe {
                    var readTwoValues = &ReadTwoValues;
                    return readTwoValues(&init value, &init value);
                }
            }
            """), reference, BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void IndirectNativeOutputRejectsReentrantReservationOfSameCell()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = CreateLibrary(SyntaxTree.Parse("""
            [param(value): Terrum.Interop.NativeOutput(
                When: Terrum.Interop.NativeOutputCondition.NonZero)]
            [Terrum.Interop.NativeImport("synthetic.dll", "ReadInner")]
            extern unsafe int32 ReadInner(write ptr<int32> value);

            [param(value): Terrum.Interop.NativeOutput(
                When: Terrum.Interop.NativeOutputCondition.NonZero)]
            [Terrum.Interop.NativeImport("synthetic.dll", "ReadOuter")]
            extern unsafe int32 ReadOuter(write ptr<int32> value, int32 status);

            int32 Probe() {
                init int32 value;
                unsafe {
                    var inner = &ReadInner;
                    var outer = &ReadOuter;
                    return outer(&init value, inner(&init value));
                }
            }
            """), reference, BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void IndirectNativeOutputCancelsReservationWhenLaterArgumentDoesNotComplete()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.CheckedExceptions,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = CreateLibrary(SyntaxTree.Parse("""
            [param(value): Terrum.Interop.NativeOutput(
                When: Terrum.Interop.NativeOutputCondition.NonZero)]
            [Terrum.Interop.NativeImport("synthetic.dll", "ReadValue")]
            extern unsafe int32 ReadValue(
                write ptr<int32> value, int32 marker);

            [param(value): Terrum.Interop.NativeOutput(
                When: Terrum.Interop.NativeOutputCondition.NonZero)]
            [Terrum.Interop.NativeImport("synthetic.dll", "ReadAgain")]
            extern unsafe int32 ReadAgain(write ptr<int32> value);

            [DoesNotReturn] int32 Stop() { require false; }

            int32 Probe() {
                init int32 value;
                unsafe {
                    var readValue = &ReadValue;
                    var readAgain = &ReadAgain;
                    try {
                        readValue(&init value, Stop());
                    } catch {
                        readAgain(&init value);
                    }
                }
                return 0;
            }
            """), reference, BoundRuntimePolicy.CheckedExceptions);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void IndirectNativeParameterCallRejectsWrongPointeeType()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("kernel32.dll", "QueryPerformanceCounter")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            extern unsafe int32 QueryPerformanceCounter(write ptr<int64> counter);

            int32 Main() {
                mut int32 counter = 0;
                unsafe {
                    var query = &QueryPerformanceCounter;
                    return query(&counter);
                }
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase(0, true)]
    [TestCase(1, false)]
    public void IndirectNativeParameterNullValueMustSatisfyExtent(
        int extent, bool shouldCompile)
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var source = $$"""
            [Terrum.Interop.NativeImport("fixture.dll", "Observe")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, {{extent}})]
            extern unsafe int32 Observe(ptr<uint32> value);

            int32 Main() {
                unsafe {
                    var observe = &Observe;
                    return observe(null);
                }
            }
            """;
        var compilation = Create(SyntaxTree.Parse(source), reference);

        Assert.That(compilation.HasErrors, Is.EqualTo(!shouldCompile),
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase(0, true)]
    [TestCase(3, true)]
    [TestCase(4, true)]
    [TestCase(5, false)]
    public void IndirectNativeUtf16LiteralPointerMustMeetNativeParameterExtent(
        int extent, bool shouldCompile)
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var source = $$"""
            [Terrum.Interop.NativeImport("fixture.dll", "Observe")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, {{extent}})]
            extern unsafe int32 Observe(ptr<char16> value);

            int32 Main() {
                unsafe {
                    var observe = &Observe;
                    return observe(utf16z("abc"));
                }
            }
            """;
        var compilation = Create(SyntaxTree.Parse(source), reference);

        Assert.That(compilation.HasErrors, Is.EqualTo(!shouldCompile),
            string.Join(Environment.NewLine, compilation.Diagnostics));
        if (!shouldCompile)
        {
            Assert.That(compilation.Program, Is.Null);
            Assert.That(compilation.Diagnostics, Is.Not.Empty);
            return;
        }

        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");
        Assert.That(Encoding.ASCII.GetString(
            NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast)),
            Does.Contain("Observe"));
    }

    [TestCase(0, 3, true)]
    [TestCase(1, 2, true)]
    [TestCase(1, 3, false)]
    [TestCase(0, 4, false)]
    public void IndirectNativeParameterExtentTracksCopiedArrayPointers(
        int index, int extent, bool shouldCompile)
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var source = $$"""
            [Terrum.Interop.NativeImport("fixture.dll", "ObserveValues")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, {{extent}})]
            extern unsafe int32 ObserveValues(ptr<uint32> values);

            struct PointerHolder {
                public mut ptr<uint32> Value;
                public unsafe PointerHolder(ptr<uint32> value) { Value = value; }
            }

            int32 Main() {
                [uint32; 3] values = [1, 2, 3];
                unsafe {
                    var observe = &ObserveValues;
                    mut PointerHolder holder = new PointerHolder(null);
                    holder.Value = &values[{{index}}];
                    PointerHolder copy = holder;
                    return observe(copy.Value);
                }
            }
            """;
        var compilation = Create(SyntaxTree.Parse(source), reference);

        Assert.That(compilation.HasErrors, Is.EqualTo(!shouldCompile),
            string.Join(Environment.NewLine, compilation.Diagnostics));
        if (!shouldCompile)
        {
            Assert.That(compilation.Program, Is.Null);
            Assert.That(compilation.Diagnostics, Is.Not.Empty);
            return;
        }

        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");
        Assert.That(Encoding.ASCII.GetString(
            NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast)),
            Does.Contain("ObserveValues"));
    }

    [Test]
    public void IndirectNativeParameterExtentUsesStagedPointerBeforeLaterWriteArgument()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("fixture.dll", "Overwrite")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            extern unsafe int32 Overwrite(write ptr<ptr<uint32>> cell);

            [Terrum.Interop.NativeImport("fixture.dll", "Observe")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 2)]
            extern unsafe int32 Observe(ptr<uint32> value, int32 status);

            int32 Main() {
                [uint32; 2] values = [42, 42];
                mut ptr<uint32> pointer = null;
                unsafe {
                    var observe = &Observe;
                    pointer = &values[0];
                    return observe(pointer, Overwrite(&pointer));
                }
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(Encoding.ASCII.GetString(NativeEmitter.EmitWithNativeImports(
            compilation.Program!, NativeRuntimePolicy.FailFast)),
            Does.Contain("Overwrite").And.Contain("Observe"));
    }

    [TestCase("if (overwrite(&pointer) != 0) { status = 1; }", "", false)]
    [TestCase("if (overwrite(&pointer) != 0) { status = 1; }",
        "pointer = &values[0];", true)]
    [TestCase("if (overwrite(&pointer) != 0 && true) { status = 1; }", "", false)]
    [TestCase("status = (overwrite(&pointer) != 0) ? 1 : 0;", "", false)]
    public void IndirectNativeParameterWriteInConditionInvalidatesPointerExtentUntilReassigned(
        string condition, string reassignment, bool shouldCompile)
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var source = $$"""
            [Terrum.Interop.NativeImport("fixture.dll", "Overwrite")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            extern unsafe int32 Overwrite(write ptr<ptr<uint32>> cell);

            [Terrum.Interop.NativeImport("fixture.dll", "Observe")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 2)]
            extern unsafe int32 Observe(ptr<uint32> value);

            int32 Main() {
                [uint32; 2] values = [42, 42];
                unsafe {
                    var overwrite = &Overwrite;
                    mut ptr<uint32> pointer = &values[0];
                    mut int32 status = 0;
                    {{condition}}
                    {{reassignment}}
                    return Observe(pointer) + status;
                }
            }
            """;
        var compilation = Create(SyntaxTree.Parse(source), reference);

        Assert.That(compilation.HasErrors, Is.EqualTo(!shouldCompile),
            string.Join(Environment.NewLine, compilation.Diagnostics));
        if (!shouldCompile)
        {
            Assert.That(compilation.Program, Is.Null);
            Assert.That(compilation.Diagnostics.Any(static diagnostic =>
                diagnostic.Code == "TRM3030"), Is.True,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            return;
        }

        Assert.That(NativeEmitter.EmitWithNativeImports(
            compilation.Program!, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void SemanticVerifierRejectsTypedNullForPositiveIndirectNativeParameterExtent()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("fixture.dll", "Observe")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            extern unsafe int32 Observe(ptr<uint32> value);

            int32 Main() {
                unsafe {
                    var observe = &Observe;
                    return observe(null);
                }
            }
            """), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var importedIndex = program.Functions.IndexOf(program.Functions.Single(
            static function => function.Name == "Observe"));
        var importedFunction = program.Functions[importedIndex];
        var import = importedFunction.DirectNativeImport!;
        var invalidImport = import with {
            Parameters = import.Parameters.SetItem(0,
                import.Parameters[0] with { Extent = 1 }),
        };
        var signature = program.NativeFunctionPointerSignatures.Single();
        var signatureIndex = program.NativeFunctionPointerSignatures.IndexOf(signature);
        var invalidSignature = signature with {
            NativeParameters = signature.NativeParameters.SetItem(0,
                signature.NativeParameters[0] with { Extent = 1 }),
        };
        var invalidProgram = program with {
            Functions = program.Functions.SetItem(importedIndex,
                importedFunction with { DirectNativeImport = invalidImport }),
            NativeFunctionPointerSignatures =
                program.NativeFunctionPointerSignatures.SetItem(signatureIndex, invalidSignature),
        };

        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(invalidProgram));
    }

    [Test]
    public void NativeImportPointerIdentityRetainsOutputAndParameterContracts()
    {
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("synthetic.dll", "ReadAndProbe")]
            [param(result): DependsOn(nameof(source))]
            [param(result): ForwardsBacking(nameof(source))]
            [Terrum.Interop.NativeParameter(3, Read, NoEscape, 4)]
            extern unsafe int32 ReadAndProbe(
                ptr<int32> source,
                [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<ptr<int32>> result,
                ptr<uint32> values);

            int32 Address() {
                unsafe {
                    var readAndProbe = &ReadAndProbe;
                    return 0;
                }
            }
            """), reference, BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var imported = program.Functions.Single(static function =>
            function.Name == "ReadAndProbe");
        var addressFunction = program.Functions.Single(static function =>
            function.Name == "Address");
        var address = new List<BoundNativeFunctionAddress>();
        Assert.That(BoundTreeTraversal.VisitExpressions(addressFunction.Body,
            expression =>
            {
                if (expression is BoundNativeFunctionAddress functionAddress)
                {
                    address.Add(functionAddress);
                }
            }), Is.True);

        var functionPointer = address.Single();
        var signature = program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(functionPointer.Type)];
        Assert.That(functionPointer.Function,
            Is.EqualTo(program.Functions.IndexOf(imported)));
        Assert.That(signature.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Unmanaged));
        Assert.That(signature.Selectors, Is.EqualTo(
        [
            new NativeFunctionPointerSelector(3, 0,
                (byte)NativeOutputSelector.NonZero),
            new NativeFunctionPointerSelector(3, 1,
                (byte)NativeOutputSelector.Zero),
        ]));
        Assert.That(signature.OutputStates, Is.EqualTo(
        [
            new NativeFunctionPointerOutputState(1, 0,
                (byte)NativeOutputState.Live),
            new NativeFunctionPointerOutputState(1, 1,
                (byte)NativeOutputState.Vacant),
        ]));
        Assert.That(signature.Dependencies, Is.EqualTo(
        [
            new NativeFunctionPointerDependency(2, 1, 2, 1, 1, 0),
        ]));
        Assert.That(signature.NativeParameters,
            Is.EqualTo(
            [
                new DirectNativeParameter(2, PrimitiveType.UInt32,
                    NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 4),
            ]));
        Assert.That(NativeFunctionPointerContract.TryGetNativeOutputContract(
            signature, 1, program.NativePointerTypes, out var output), Is.True);
        var outputContract = output ?? throw new InvalidOperationException();
        Assert.That(outputContract.FormalOrdinal, Is.EqualTo(1));
        Assert.That(outputContract.PointeeType,
            Is.EqualTo(imported.NativeOutputContracts.Single().PointeeType));
        Assert.That(outputContract.SuccessSelector, Is.EqualTo(NativeOutputSelector.NonZero));
        Assert.That(outputContract.SuccessState, Is.EqualTo(NativeOutputState.Live));
        Assert.That(outputContract.ComplementSelector, Is.EqualTo(NativeOutputSelector.Zero));
        Assert.That(outputContract.ComplementState, Is.EqualTo(NativeOutputState.Vacant));
        Assert.That(outputContract.Dependencies,
            Is.EqualTo(imported.NativeOutputContracts.Single().Dependencies));
        Assert.That(NativeFunctionPointerContract.MatchesAddressableFunction(
            imported, functionPointer.Type, program.NativeFunctionPointerSignatures), Is.True);
        Assert.That(NativeFunctionPointerContract.CanPersistRevision92(signature), Is.False);
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitDevelopmentFunctionPointerEffectsDraft(
                program, "unsupported-native-output-signature"));
    }

    [Test]
    public void NativeImportPointerTypeComparisonPreservesTypedContracts()
    {
        var readParameter = new DirectNativeParameter(1, PrimitiveType.UInt32,
            NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 4);
        var signature = new NativeFunctionPointerSignatureDefinition(
            NativeCallingConvention.Unmanaged, PrimitiveType.Int32,
            [NativePointerTypes.Type(0), NativePointerTypes.Type(1)]) {
            Selectors =
            [
                new NativeFunctionPointerSelector(3, 0,
                    (byte)NativeOutputSelector.NonZero),
                new NativeFunctionPointerSelector(3, 1,
                    (byte)NativeOutputSelector.Zero),
            ],
            OutputStates =
            [
                new NativeFunctionPointerOutputState(0, 0,
                    (byte)NativeOutputState.Live),
                new NativeFunctionPointerOutputState(0, 1,
                    (byte)NativeOutputState.Vacant),
            ],
            Dependencies = [new NativeFunctionPointerDependency(2, 0, 0, 1, 0, 0)],
            NativeParameters = [readParameter],
        };
        var noOutput = signature with {
            Selectors = [],
            OutputStates = [],
            Dependencies = [],
        };
        var differentOutput = signature with {
            Selectors =
            [
                new NativeFunctionPointerSelector(3, 0,
                    (byte)NativeOutputSelector.NonNegative),
                new NativeFunctionPointerSelector(3, 1,
                    (byte)NativeOutputSelector.Negative),
            ],
        };
        var writableParameter = signature with {
            NativeParameters = [readParameter with {
                Access = NativeParameterAccess.ReadWrite,
            }],
        };

        Assert.That(NativeFunctionPointerContract.IsValidSignature(signature, [signature]),
            Is.True);
        Assert.That(NativeFunctionPointerContract.IsValidSignature(signature with {
            OutputStates = [signature.OutputStates[0]],
        }, [signature]), Is.False);
        Assert.That(NativeFunctionPointerContract.IsValidSignature(signature with {
            Dependencies = [],
        }, [signature]), Is.False);
        Assert.That(NativeFunctionPointerContract.IsValidSignature(signature with {
            Dependencies = [new NativeFunctionPointerDependency(2, 1, 1, 1, 0, 1)],
        }, [signature]), Is.False);
        Assert.That(NativeFunctionPointerContract.AreEquivalent(signature, noOutput),
            Is.False);
        Assert.That(NativeFunctionPointerContract.AreEquivalent(signature, differentOutput),
            Is.False);
        Assert.That(NativeFunctionPointerContract.AreEquivalent(signature, writableParameter),
            Is.False);
        Assert.That(NativeFunctionPointerContract.CanProject(
            NativeFunctionPointerTypes.Type(0), NativeFunctionPointerTypes.Type(1),
            [signature, noOutput]), Is.False);
        Assert.That(NativeFunctionPointerContract.CanPersistRevision92(signature), Is.False);
    }

    [Test]
    public void NativeFunctionAddressSurvivesPrivateModuleProducerConsumerRoundTrip()
    {
        var providerCompilation = CreateLibrary(SyntaxTree.Parse(NativeCallbackProvider));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(providerCompilation.Program!, ["GetAnswer"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> callback = GetAnswer();
                    return callback(42) == 42 ? 0 : -1;
                }
            }
            """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(consumerCompilation.Program!, producer);

        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), NativeRuntimePolicy.FailFast),
            Is.Zero);

        var incompatibleCompilation = CreateLibrary(SyntaxTree.Parse("""

            [Terrum.Interop.NativeCallable]
            int32 Answer() { return 42; }


            public unsafe [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32> GetAnswer() {
                unsafe { return &Answer; }
            }
            """));
        Assert.That(incompatibleCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, incompatibleCompilation.Diagnostics));
        var incompatibleProducer = ModuleCompiler.CreateProducer(incompatibleCompilation.Program!, ["GetAnswer"]);
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer, incompatibleProducer));
    }

    [Test]
    public async Task NativeCallableExportsSurvivePrivateAndNativeModuleConsumersAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string providerSource = """
            [Terrum.Interop.NativeCallable]
            public int32 Identity(int32 value) { return value; }
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
                    [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> identity = &Identity;
                    return identity(42) == 42 ? 0 : -1;
                }
            }
            """;

        var providerCompilation = CreateLibrary(SyntaxTree.Parse(providerSource));
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
        var privateConsumerCompilation = Create(
            SyntaxTree.Parse(consumerSource), privateReference);
        Assert.That(privateConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, privateConsumerCompilation.Diagnostics));
        var privateConsumer = ModuleCompiler.CreateConsumer(
            privateConsumerCompilation.Program!, privateProducer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(privateConsumer, privateProducer),
            NativeRuntimePolicy.FailFast), Is.Zero);
        var addressConsumerCompilation = Create(
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
            Is.EqualTo(["Identity"]));
        var secondCompilation = CreateLibrary(SyntaxTree.Parse(
            "public int32 Other() { return 0; }"));
        Assert.That(secondCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, secondCompilation.Diagnostics));
        var secondProducer = NativeModuleCompiler.CreateProducer(secondCompilation.Program!,
            ["Other"], "NativeCallableSecond", NativeRuntimePolicy.FailFast);
        var nativeProducers = new[] { nativeProducer, secondProducer };
        var nativeReference = NativeModuleCompiler.CreateReferenceSet(nativeProducers);
        var nativeConsumerCompilation = Create(
            SyntaxTree.Parse(consumerSource), nativeReference);
        Assert.That(nativeConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, nativeConsumerCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateConsumerSet(
            nativeConsumerCompilation.Program!, nativeProducers, NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.ValidateConsumerSet(nativeConsumer, nativeProducers);
        Assert.That(await NativeModuleCompiler.RunConsumerSetAsync(nativeConsumer, nativeProducers,
            TimeSpan.FromSeconds(10), NativeRuntimePolicy.FailFast).ConfigureAwait(true), Is.Zero);
        var nativeAddressConsumerCompilation = Create(
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

        var directory = TestArtifactPaths.CreateDirectory("terrum-native-narrow-return-");
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

            var compilation = Create(SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                struct TestVTable {
                    public [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<ptr<TestObject>, bool> Method;
                    public TestVTable([Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<ptr<TestObject>, bool> method) {
                        Method = method;
                    }
                }

                [Blittable] [Layout(LayoutKind.Sequential)]
                struct TestObject {
                    public ptr<TestVTable> VTable;
                    public TestObject(ptr<TestVTable> vtable) { VTable = vtable; }
                }

                [Terrum.Interop.NativeImport("native-narrow-return.dll", "ReturnFalseDirty")]

                extern unsafe bool ReturnFalseDirty();

                [Terrum.Interop.NativeImport("native-narrow-return.dll", "GetFalseDirty")]

                extern unsafe [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<bool> GetFalseDirty();

                [Terrum.Interop.NativeImport("native-narrow-return.dll", "GetFalseDirtyWithReceiver")]

                extern unsafe [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<ptr<TestObject>, bool> GetFalseDirtyWithReceiver();

                [Terrum.Interop.NativeImport("native-narrow-return.dll", "ReturnChar16Dirty")]

                extern unsafe char16 ReturnChar16Dirty();

                [Terrum.Interop.NativeVTableMethod(0)]
                extern unsafe bool Invoke(ptr<TestObject> receiver);

                int32 Main() {
                    unsafe {
                        if (ReturnFalseDirty()) { return 1; }
                        [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<bool> callback = GetFalseDirty();
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
            public unsafe int32 AcceptZero([Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32> callback) { return 0; }
            public unsafe int32 AcceptUnary([Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> callback) { return 0; }
            """, ["AcceptZero", "AcceptUnary"]);
        var secondProducer = CreateProducer("""
            public unsafe int32 AcceptUnarySecond([Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> callback) { return 0; }
            public unsafe int32 AcceptZeroSecond([Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32> callback) { return 0; }
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
        var compilation = Create(SyntaxTree.Parse("""
            struct CallbackBox {
                public [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> Callback;
                public CallbackBox([Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> callback) {
                    Callback = callback;
                }
            }


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
                public [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<ptr<void>, uint32, uint64, int64, int64> Callback;
                public CallbackBox(
                    [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<ptr<void>, uint32, uint64, int64, int64> callback) {
                    Callback = callback;
                }
            }
            """);
        var implementation = SyntaxTree.Parse("""
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
        var compilation = Create([declarations, implementation]);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public void BlittableFunctionPointerFieldsUseNativePointerLayout()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct CallbackBox {
                public [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> Callback;
                public CallbackBox([Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> callback) {
                    Callback = callback;
                }
            }


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
        var compilation = Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestVTable {
                public [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<ptr<void>, uint32> GetValue;
                public TestVTable([Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<ptr<void>, uint32> getValue) {
                    GetValue = getValue;
                }
            }


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
        var compilation = Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestVTable {
                public [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<ptr<TestObject>, uint32> GetValue;
                public TestVTable([Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<ptr<TestObject>, uint32> getValue) {
                    GetValue = getValue;
                }
            }

            [Blittable] [Layout(LayoutKind.Sequential)]
            struct TestObject {
                public ptr<TestVTable> VTable;
                public TestObject(ptr<TestVTable> vtable) { VTable = vtable; }
            }


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
            public unsafe int32 AcceptZero([Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32> callback) { return 0; }
            public unsafe int32 AcceptUnary([Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> callback) { return 0; }
            """, ["AcceptZero", "AcceptUnary"], "CallbackTypesFirst");
        var second = CreateNativeProducer("""
            public unsafe int32 AcceptUnarySecond([Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> callback) { return 0; }
            public unsafe int32 AcceptZeroSecond([Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32> callback) { return 0; }
            """, ["AcceptUnarySecond", "AcceptZeroSecond"], "CallbackTypesSecond");
        var reference = NativeModuleCompiler.CreateReferenceSet([first, second]);

        AssertMergedFunctionPointerTypes(reference);
        var consumerCompilation = Create(SyntaxTree.Parse("int32 Main() { return 0; }"), reference);
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
        var providerCompilation = CreateLibrary(SyntaxTree.Parse(NativeCallbackProvider));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(providerCompilation.Program!,
            ["GetAnswer"], "CallbackProvider", NativeRuntimePolicy.FailFast);
        var reference = NativeModuleCompiler.CreateGraphReference([provider]);
        var consumerCompilation = Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> callback = GetAnswer();
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
        var parameterTypeOffset = FindPrivate106SignatureParameterTypeOffset(
            malformed, expectedParameterCount: 1, parameterOrdinal: 0);
        BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(parameterTypeOffset),
            (int)PrimitiveType.Void);

        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed));
    }

    [Test]
    public void StandardTrRmRoundTripsAndExecutesNativeFunctionPointerBodyOperations()
    {
        var compilation = Create(SyntaxTree.Parse(NativeCallbackProvider + """
            int32 Main() {
                unsafe {
                    [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> callback = GetAnswer();
                    return callback(42) == 42 ? 0 : -1;
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitDevelopmentNativeConventionsDraft(
            compilation.Program!,
            "native-function-pointer-body");
        var index = TrRmArtifactIndex.ForDevelopmentNativeConventionsDraft(artifact);
        Assert.That(index.TryGetSection(0x0004, out var featureBytes, out var featureCount), Is.True);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount);
        Assert.That(features.TryGetFeature(
            0x0102, out _), Is.True);
        Assert.That(features.TryGetFeature(
            0x0103, out _), Is.True);

        var loaded = TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(artifact);
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
        var compilation = Create(SyntaxTree.Parse(NativeCallbackProvider + """

            [Terrum.Interop.NativeCallable]
            int64 WrongSignature() { return 0; }

            int32 Main() {
                unsafe {
                    [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> callback = GetAnswer();
                    return callback(42);
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitDevelopmentNativeConventionsDraft(
            compilation.Program!,
            "malformed-native-function-address");
        var loaded = TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(artifact);
        var wrongSignatureMethodRow = loaded.Functions
            .Select((function, index) => (function, index))
            .Single(static item => item.function.Name == "WrongSignature").index + 1;
        var addressTargetOffset = FindIndexedOperationTargetOffset(artifact, 0x31);

        var mismatchedTarget = (byte[])artifact.Clone();
        mismatchedTarget[addressTargetOffset] = checked((byte)wrongSignatureMethodRow);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(mismatchedTarget));

        var missingUnsafeRegion = (byte[])artifact.Clone();
        RemoveIndexedUnsafeAuthorization(missingUnsafeRegion);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(missingUnsafeRegion));
    }

    [Test]
    public void StandardTrRmRoundTripsNestedNativeFunctionPointerSignatures()
    {
        var compilation = CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            public int32 Invoke([Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32> callback, int32 value) {
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

        var artifact = TrRmExecutableCompiler.EmitDevelopmentNativeConventionsDraft(
            program, "nested-native-callable");
        var artifactIndex = TrRmArtifactIndex.ForDevelopmentNativeConventionsDraft(artifact);
        Assert.That(artifactIndex.TryGetSection(0x0033, out _, out var signatureCount), Is.True);
        Assert.That(signatureCount, Is.EqualTo(program.NativeFunctionPointerSignatures.Length));
        Assert.That(artifactIndex.TryGetSection(0x0011, out var typeSpecBytes,
            out var typeSpecCount), Is.True);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, typeSpecCount,
            0, 0, 0, signatureCount,
            TrRmArtifactSchema.DevelopmentNativeConventionsDraft);
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
            0x0100, out _), Is.True);
        Assert.That(features.TryGetFeature(
            0x0101, out _), Is.True);

        var loaded = TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(artifact);
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
        Assert.That(TrRmExecutableCompiler.EmitDevelopmentNativeConventionsDraft(
                loaded, "nested-native-callable"),
            Is.EqualTo(artifact));

        var reference = TrRmExecutableCompiler
            .CreateDevelopmentNativeConventionsDraftReference(artifact);
        Assert.That(Export(reference, "Invoke").Variables[0].Type, Is.EqualTo(callbackType));
        Assert.That(reference.NativeFunctionPointerSignatures, Has.Length.EqualTo(
            program.NativeFunctionPointerSignatures.Length));

        var malformed = artifact.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformed.AsSpan(FindSectionOffset(malformed, 0x0033)), 0x00FF_FFFF);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(malformed));

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
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(duplicateSignature));
    }

    [Test]
    public void NativeFunctionPointerOutputRetainsItsNestedSignature()
    {
        var compilation = CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "AcquireCallback")]
            extern int32 AcquireCallback(
                [Terrum.Interop.NativeOutput(When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<[Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<[Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32>, int32>> callback);
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var import = program.Functions.Single(static function => function.Name == "AcquireCallback");
        var outputType = import.NativeOutputContracts.Single().PointeeType;
        Assert.That(NativeFunctionPointerTypes.IsType(outputType), Is.True);
        var signature = program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(outputType)];
        Assert.That(signature.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(signature.ParameterTypes, Has.Length.EqualTo(1));
        var nestedSignature = program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(signature.ParameterTypes[0])];
        Assert.That(nestedSignature.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(nestedSignature.ParameterTypes, Is.Empty);
    }

    [Test]
    public void DevelopmentTrRmRequiresFunctionPointerFeaturesOnEveryUsingModule()
    {
        var compilation = CreateLibrary(SyntaxTree.Parse("""

            [Terrum.Interop.NativeCallable]
            public int32 Invoke([Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32> callback) {
                return 0;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitDevelopmentNativeConventionsDraft(
            compilation.Program!, "module-feature-closure");

        ValidateDevelopmentNativeConventionsDraft(
            SplitSignatureAnchorModule(artifact, includeRootlessFeature: true));
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateDevelopmentNativeConventionsDraft(
                SplitSignatureAnchorModule(artifact, includeRootlessFeature: false)));
    }

    [Test]
    public void StandardTrRmRoundTripsScalarNativeCallableContracts()
    {
        var compilation = CreateLibrary(SyntaxTree.Parse("""
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
        var artifact = TrRmExecutableCompiler.EmitDevelopmentNativeConventionsDraft(
            program, "native-callable");
        var loaded = TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(artifact);
        var callable = loaded.Functions.Single(static function => function.Name == "Sum");

        Assert.That(callable.NativeFunctionPointerType, Is.EqualTo(NativeFunctionPointerTypes.Type(0)));
        Assert.That(callable.NativeCallableEntryName, Is.EqualTo("Sum|Native"));
        Assert.That(loaded.NativeFunctionPointerSignatures, Has.Length.EqualTo(1));
        Assert.That(loaded.NativeFunctionPointerSignatures[0].ReturnType, Is.EqualTo(PrimitiveType.Float64));
        Assert.That(loaded.NativeFunctionPointerSignatures[0].ParameterTypes,
            Is.EqualTo([PrimitiveType.Float64, PrimitiveType.Int32, PrimitiveType.Float64]));
        Assert.That(callable.NoFailureProof, Is.False);
        Assert.That(callable.RequiresUnsafeCaller, Is.True);
        Assert.That(TrRmExecutableCompiler.EmitDevelopmentNativeConventionsDraft(
            loaded, "native-callable"), Is.EqualTo(artifact));

        var privateProducer = ModuleCompiler.CreateProducer(program, ["Sum"]);
        var privateReference = ModuleCompiler.CreateReference(privateProducer);
        Assert.That(Export(privateReference, "Sum").NativeCallableEntryName, Is.EqualTo("Sum|Native"));
    }

    [Test]
    public void StandardTrRmRoundTripsTypedPointerOnlyContracts()
    {
        var compilation = CreateLibrary(SyntaxTree.Parse("""
            public unsafe int32 Ignore(ptr<int32> value) { return 42; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitDevelopmentNativeConventionsDraft(
            compilation.Program!, "typed-pointer-only");
        var artifactIndex = TrRmArtifactIndex.ForDevelopmentNativeConventionsDraft(artifact);
        Assert.That(artifactIndex.TryGetSection(0x0004, out var featureBytes,
            out var featureCount), Is.True);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount);
        Assert.That(features.TryGetFeature(
            0x0100, out _), Is.True);
        Assert.That(features.TryGetFeature(
            0x0101, out _), Is.True);

        var loaded = TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(artifact);
        var ignored = loaded.Functions.Single(static function => function.Name == "Ignore");
        Assert.That(ignored.RequiresUnsafeCaller, Is.True);
        Assert.That(NativePointerTypes.IsType(ignored.Variables[0].Type), Is.True);
        Assert.That(TrRmExecutableCompiler.EmitDevelopmentNativeConventionsDraft(
            loaded, "typed-pointer-only"), Is.EqualTo(artifact));
    }

    [Test]
    public void RejectsOversizedNestedFunctionPointerTypeIdentityBeforeExpansion()
    {
        var source = new StringBuilder();
        var previousAlias = "A";
        _ = source.Append("alias ").Append(previousAlias)
            .AppendLine(" = [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32>;");
        for (var depth = 1; depth <= 3; depth++)
        {
            var alias = ((char)('A' + depth)).ToString();
            var parametersAndReturn = string.Join(", ", Enumerable.Repeat(previousAlias, 9));
            _ = source.Append("alias ").Append(alias).Append(" = [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<")
                .Append(parametersAndReturn).AppendLine(">;");
            previousAlias = alias;
        }
        _ = source.Append("public ").Append(previousAlias).Append(" Identity(")
            .Append(previousAlias).AppendLine(" callback) { return callback; }");

        Assert.That(source.Length, Is.LessThan(1024));
        var compilation = CreateLibrary(SyntaxTree.Parse(source.ToString()));
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
        _ = source.Append("alias ").Append(previousAlias)
            .AppendLine(" = [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32>;");
        for (var depth = 1; depth <= 10; depth++)
        {
            var alias = ((char)('A' + depth)).ToString();
            var parametersAndReturn = string.Join(", ", Enumerable.Repeat(previousAlias, 9));
            _ = source.Append("alias ").Append(alias).Append(" = [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<")
                .Append(parametersAndReturn).AppendLine(">;");
            previousAlias = alias;
        }
        _ = source.Append("public ").Append(previousAlias)
            .AppendLine(" Reject() { return true; }");

        var compilation = CreateLibrary(SyntaxTree.Parse(source.ToString()));

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Max(static diagnostic => diagnostic.Message.Length),
            Is.LessThanOrEqualTo(512));
    }

    [Test]
    public void StandardTrRmNativeCallableReferenceBindsUnsafeCalls()
    {
        var providerCompilation = CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            public int32 GetAnswer(int32 value) { return value; }
            """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitDevelopmentNativeConventionsDraft(
            providerCompilation.Program!, "native-callable-provider");
        var reference = TrRmExecutableCompiler
            .CreateDevelopmentNativeConventionsDraftReference(artifact);

        var consumerCompilation = Create(SyntaxTree.Parse("""
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
        var providerCompilation = CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            public int32 GetAnswer(int32 value) { return value; }
            """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitDevelopmentNativeConventionsDraft(
            providerCompilation.Program!, "native-callable-provider");
        var reference = TrRmExecutableCompiler
            .CreateDevelopmentNativeConventionsDraftReference(providerArtifact);
        var imported = reference.Functions.Single(static function => function.Name == "GetAnswer");
        Assert.That(imported.ModuleImport?.ProducerIdentity, Is.EqualTo("native-callable-provider"));
        Assert.That(imported.ModuleImport?.ExportKey, Is.EqualTo("GetAnswer"));

        var consumerCompilation = Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> callback = &GetAnswer;
                    return callback(42);
                }
            }
            """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitDevelopmentNativeConventionsDraft(
            consumerCompilation.Program!, "native-callable-consumer");
        var artifactIndex = TrRmArtifactIndex.ForDevelopmentNativeConventionsDraft(artifact);
        Assert.That(artifactIndex.TryGetSection(0x10, out _, out var moduleRefCount), Is.True);
        Assert.That(moduleRefCount, Is.EqualTo(1));
        Assert.That(artifactIndex.TryGetSection(0x1C, out _, out var importCount), Is.True);
        Assert.That(importCount, Is.EqualTo(1));
        Assert.That(artifactIndex.TryGetSection(0x31, out _, out _), Is.False);

        var loaded = TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(artifact);
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
        var compilation = Create(SyntaxTree.Parse("""

            int32 Unused(int32 value) { return 10; }

            int32 Chosen(int32 value) { return 20; }


            [Terrum.Interop.NativeCallable]
            int32 CallbackChosen(int32 value) { return 20; }


            [Terrum.Interop.NativeCallable]
            int32 CallbackDecoy(int32 value) { return 30; }

            int32 Fill(init ref<int32> output) {
                output = 0;
                return Chosen(1);
            }

            int32 Main() {
                unsafe {
                    [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> callback = &CallbackChosen;
                    int32 result = callback(3);
                    return Chosen(result);
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitDevelopmentNativeConventionsDraft(
            compilation.Program!,
            "method-row-function-pointer");
        var reordered = MoveNativeFunctionPointerSignatureAnchorFirst(artifact);
        var loaded = TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(reordered);

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

        var providerCompilation = CreateLibrary(SyntaxTree.Parse("""

            [Terrum.Interop.NativeCallable]
            public int32 GetAnswer(int32 value) { return value; }
            """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitDevelopmentNativeConventionsDraft(
            providerCompilation.Program!, "NativeCallableProvider");
        var reference = TrRmExecutableCompiler
            .CreateDevelopmentNativeConventionsDraftReference(providerArtifact);
        var consumerCompilation = Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32, int32> callback = &GetAnswer;
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
        var secondCompilation = CreateLibrary(SyntaxTree.Parse("""
            public int32 Other() { return 0; }
            """));
        Assert.That(secondCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, secondCompilation.Diagnostics));
        var secondProvider = NativeModuleCompiler.CreateProducer(secondCompilation.Program!,
            ["Other"], "NativeCallableSecond", NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, wrongProvider, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateConsumerSet(
            consumerCompilation.Program!, [wrongProvider, secondProvider], NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<ModuleContractException>(() => NativeModuleCompiler.CreateGraphConsumer(
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
    public async Task NativeCallableExportMatchesIndependentDotNetAbiCallerAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var directory = TestArtifactPaths.CreateDirectory("terrum-native-callback-");
        var libraryPath = Path.Combine(directory, "native-callback.dll");
        var handle = IntPtr.Zero;
        try
        {
            var compilation = CreateLibrary(SyntaxTree.Parse("""
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

            handle = NativeLibrary.Load(libraryPath);
            unsafe
            {
                var sum = (delegate* unmanaged<double, int, double, int, double, double>)
                    NativeLibrary.GetExport(handle, "Sum");
                Assert.That(sum(1.5, 0, 3.0, 0, 5.0), Is.EqualTo(9.5));
            }
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
            }
            await TestFileCleanup.DeleteWithRetryAsync(libraryPath).ConfigureAwait(true);
            Directory.Delete(directory);
        }
    }

    [TestCase("""
        [Terrum.Interop.NativeCallable("invalid")]
        int32 Answer() { return 42; }
        int32 Main() { return 0; }
        """, "TRM3030")]
    [TestCase("""
        [Terrum.Interop.NativeCallable]
        int32 Answer() { return 42; }
        int32 Invoke([Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32> callback) { return callback(); }
        int32 Main() { return 0; }
        """, "TRM3028")]
    public void NativeCallableContractsRejectMalformedDeclarationsAndUnsafeCalls(
        string source, string diagnosticCode)
    {
        var compilation = Create(SyntaxTree.Parse(source));

        Assert.That(compilation.Diagnostics.Any(diagnostic => diagnostic.Code == diagnosticCode), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NativeCallableFunctionPointerTypesRetainDeclaredEffectsSeparatelyFromRoleUnsafety()
    {
        var compilation = CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            int32 Returns(int32 value) { return value; }

            [DoesNotReturn]
            [Terrum.Interop.NativeCallable]
            int32 Stops(int32 value) { while (true) { } }

            [Terrum.Interop.NativeCallable]
            unsafe int32 ExplicitlyUnsafe(int32 value) { return value; }

            [DoesNotReturn]
            [Terrum.Interop.NativeCallable]
            unsafe int32 StopsAndRequiresUnsafe(int32 value) { while (true) { } }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var returns = program.Functions.Single(static function => function.Name == "Returns");
        var stops = program.Functions.Single(static function => function.Name == "Stops");
        var explicitlyUnsafe = program.Functions.Single(
            static function => function.Name == "ExplicitlyUnsafe");
        var stopsAndRequiresUnsafe = program.Functions.Single(
            static function => function.Name == "StopsAndRequiresUnsafe");
        Assert.That(returns.RequiresUnsafeCaller, Is.True);
        Assert.That(returns.DeclaredRequiresUnsafeCaller, Is.False);
        Assert.That(stops.DoesNotReturn, Is.True);
        Assert.That(stops.NativeFunctionPointerType, Is.Not.Null);
        Assert.That(explicitlyUnsafe.RequiresUnsafeCaller, Is.True);
        Assert.That(explicitlyUnsafe.DeclaredRequiresUnsafeCaller, Is.True);
        Assert.That(explicitlyUnsafe.NativeFunctionPointerType, Is.Not.Null);
        Assert.That(stopsAndRequiresUnsafe.NativeFunctionPointerType, Is.Not.Null);
        Assert.That(stops.NativeFunctionPointerType,
            Is.Not.EqualTo(returns.NativeFunctionPointerType));
        Assert.That(explicitlyUnsafe.NativeFunctionPointerType,
            Is.Not.EqualTo(returns.NativeFunctionPointerType));

        var signatures = program.NativeFunctionPointerSignatures;
        Assert.That(signatures[NativeFunctionPointerTypes.Index(
            returns.NativeFunctionPointerType!.Value)].EffectsMask, Is.Zero);
        Assert.That(signatures[NativeFunctionPointerTypes.Index(
            stops.NativeFunctionPointerType!.Value)].EffectsMask, Is.EqualTo(2));
        Assert.That(signatures[NativeFunctionPointerTypes.Index(
            explicitlyUnsafe.NativeFunctionPointerType!.Value)].EffectsMask, Is.EqualTo(4));
        Assert.That(signatures[NativeFunctionPointerTypes.Index(
            stopsAndRequiresUnsafe.NativeFunctionPointerType!.Value)].EffectsMask, Is.EqualTo(6));
        Assert.That(new[]
        {
            returns.NativeFunctionPointerType.Value,
            stops.NativeFunctionPointerType.Value,
            explicitlyUnsafe.NativeFunctionPointerType.Value,
            stopsAndRequiresUnsafe.NativeFunctionPointerType.Value,
        }.Distinct().Count(), Is.EqualTo(4));
        Assert.That(signatures.Length, Is.EqualTo(4));
        Assert.That(NativeFunctionPointerContract.MatchesFunction(returns,
            returns.NativeFunctionPointerType.Value, signatures), Is.True);
        Assert.That(NativeFunctionPointerContract.MatchesFunction(stops,
            stops.NativeFunctionPointerType.Value, signatures), Is.True);
        Assert.That(NativeFunctionPointerContract.MatchesFunction(explicitlyUnsafe,
            explicitlyUnsafe.NativeFunctionPointerType.Value, signatures), Is.True);
        Assert.That(NativeFunctionPointerContract.MatchesFunction(stopsAndRequiresUnsafe,
            stopsAndRequiresUnsafe.NativeFunctionPointerType.Value, signatures), Is.True);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(program, ["Stops"]));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(program, "native-callable-effects"));

        var candidateArtifact = TrRmExecutableCompiler
            .EmitDevelopmentNativeConventionsDraft(program, "native-callable-effects");
        var loaded = TrRmExecutableCompiler
            .LoadDevelopmentNativeConventionsDraft(candidateArtifact);
        foreach (var (name, expectedEffects, expectedDoesNotReturn,
            expectedDeclaredUnsafe) in new[]
        {
            ("Returns", 0u, false, false),
            ("Stops", 2u, true, false),
            ("ExplicitlyUnsafe", 4u, false, true),
            ("StopsAndRequiresUnsafe", 6u, true, true),
        })
        {
            var function = loaded.Functions.Single(candidate => candidate.Name == name);
            Assert.That(function.NativeFunctionPointerType, Is.Not.Null);
            Assert.That(function.DoesNotReturn, Is.EqualTo(expectedDoesNotReturn));
            Assert.That(function.RequiresUnsafeCaller, Is.True);
            Assert.That(function.DeclaredRequiresUnsafeCaller,
                Is.EqualTo(expectedDeclaredUnsafe));
            var signature = loaded.NativeFunctionPointerSignatures[
                NativeFunctionPointerTypes.Index(function.NativeFunctionPointerType!.Value)];
            Assert.That(signature.EffectsMask, Is.EqualTo(expectedEffects));
        }

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(candidateArtifact));

        var index = TrRmArtifactIndex
            .ForDevelopmentNativeConventionsDraft(candidateArtifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateDevelopmentNativeConventionsDraftAndGetExecutionTables(index);
        var stopsMethod = 0;
        for (var method = 1; method <= tables.Methods.Count; method++)
        {
            if (tables.Methods.GetName(method) == "Stops")
            {
                stopsMethod = method;
                break;
            }
        }
        Assert.That(stopsMethod, Is.GreaterThan(0));
        var (firstContract, contractCount) = tables.Methods.GetContractRange(stopsMethod);
        var effectsRow = 0;
        for (var indexInRange = 0; indexInRange < contractCount; indexInRange++)
        {
            var row = firstContract + indexInRange;
            if (BinaryPrimitives.ReadUInt16LittleEndian(
                tables.Contracts.GetRow(row).Slice(4, sizeof(ushort))) == 1)
            {
                effectsRow = row;
                break;
            }
        }
        Assert.That(effectsRow, Is.GreaterThan(0));
        Assert.That(index.TryGetSection(0x001B, out _, out var contractRowCount), Is.True);
        var contractSectionOffset = FindSectionOffset(candidateArtifact, 0x001B);
        var contractIndexByteCount = checked((contractRowCount + 1) * sizeof(uint));
        var effectsRowOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            candidateArtifact.AsSpan(
                contractSectionOffset + ((effectsRow - 1) * sizeof(uint)))));
        var malformed = (byte[])candidateArtifact.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(
            contractSectionOffset + contractIndexByteCount + effectsRowOffset + 8), 8);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(malformed));
    }

    [Test]
    public void DevelopmentNativeConventionsProfileSeparatesNativeImportRoleUnsafety()
    {
        var compilation = CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "ObserveDefault")]
            public extern void ObserveDefault();

            [Terrum.Interop.NativeImport("example.dll", "ObserveExplicit")]
            public unsafe extern void ObserveExplicit();
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitDevelopmentNativeConventionsDraft(
            compilation.Program!, "native-import-effects");
        var index = TrRmArtifactIndex.ForDevelopmentNativeConventionsDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateDevelopmentNativeConventionsDraftAndGetExecutionTables(index);
        var loaded = TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(artifact);
        foreach (var (name, declaredUnsafe) in new[]
        {
            ("ObserveDefault", false),
            ("ObserveExplicit", true),
        })
        {
            var function = loaded.Functions.Single(candidate => candidate.Name == name);
            Assert.That(function.RequiresUnsafeCaller, Is.True);
            Assert.That(function.DeclaredRequiresUnsafeCaller, Is.EqualTo(declaredUnsafe));
            Assert.That(function.DirectNativeImport, Is.Not.Null);
        }
        var defaultMethod = 0;
        var explicitMethod = 0;
        for (var method = 1; method <= tables.Methods.Count; method++)
        {
            var name = tables.Methods.GetName(method);
            defaultMethod = name == "ObserveDefault" ? method : defaultMethod;
            explicitMethod = name == "ObserveExplicit" ? method : explicitMethod;
        }
        Assert.That(defaultMethod, Is.GreaterThan(0));
        Assert.That(explicitMethod, Is.GreaterThan(0));
        Assert.That(tables.Methods.GetContractRange(defaultMethod).Count, Is.EqualTo(1));
        Assert.That(tables.Methods.GetContractRange(explicitMethod).Count, Is.EqualTo(2));
        Assert.That(tables.Contracts.GetEffectsMask(defaultMethod), Is.Zero);
        Assert.That(tables.Contracts.GetEffectsMask(explicitMethod), Is.EqualTo(4u));

        _ = Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(artifact));
    }

    [Test]
    public void DevelopmentNativeConventionsProfileRetainsBodylessNativeCallableExpectations()
    {
        var provider = CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            public int32 ReadValue(int32 value) { return value; }

            [Terrum.Interop.NativeCallable]
            public unsafe int32 ReadUnsafe(int32 value) { return value; }
            """));
        Assert.That(provider.HasErrors, Is.False,
            string.Join(Environment.NewLine, provider.Diagnostics));

        var providerArtifact = TrRmExecutableCompiler
            .EmitDevelopmentNativeConventionsDraft(provider.Program!, "callable-effects");
        var reference = TrRmExecutableCompiler
            .CreateDevelopmentNativeConventionsDraftReference(providerArtifact);
        var consumer = Create(SyntaxTree.Parse(
            "int32 Main() { return 0; }"), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        Assert.That(consumer.Program!.Functions.Count(static function =>
            function.ModuleImport is not null), Is.EqualTo(2));

        var consumerArtifact = TrRmExecutableCompiler
            .EmitDevelopmentNativeConventionsDraft(consumer.Program!, "callable-consumer");
        var loadedConsumer = TrRmExecutableCompiler
            .LoadDevelopmentNativeConventionsDraft(consumerArtifact);
        foreach (var (name, declaredUnsafe) in new[]
        {
            ("ReadValue", false),
            ("ReadUnsafe", true),
        })
        {
            var imported = loadedConsumer.Functions.Single(function =>
                function.Name == name);
            Assert.That(imported.ModuleImport, Is.Not.Null);
            Assert.That(imported.RequiresUnsafeCaller, Is.True);
            Assert.That(imported.DeclaredRequiresUnsafeCaller,
                Is.EqualTo(declaredUnsafe));
        }
    }

    [Test]
    public void DevelopmentNativeConventionsProfilePreservesRoleAndResolutionContracts()
    {
        var compilation = CreateLibrary(SyntaxTree.Parse("""
            using Terrum.Interop;

            [NativeImport("example.dll", "ImportUnmanaged",
                CallConv: NativeCallingConvention.Unmanaged)]
            public extern unsafe int32 ImportUnmanaged();

            [NativeImport("example.dll", "ImportCdecl",
                CallConv: NativeCallingConvention.Cdecl,
                ResolutionMode: NativeImportResolutionMode.Dynamic)]
            public extern unsafe int32 ImportCdecl();

            [NativeImport("example.dll", "ImportStdcall",
                CallConv: NativeCallingConvention.Stdcall,
                ResolutionMode: NativeImportResolutionMode.Dynamic,
                ResolutionPolicy: NativeImportResolutionPolicy.CompatibilitySearch)]
            public extern unsafe int32 ImportStdcall();

            [NativeImport("example.dll", "ImportVectorcall",
                CallConv: NativeCallingConvention.Vectorcall)]
            public extern unsafe int32 ImportVectorcall();

            [NativeCallable(CallConv: NativeCallingConvention.Unmanaged)]
            public int32 CallableUnmanaged(int32 value) { return value; }

            [NativeCallable(CallConv: NativeCallingConvention.Cdecl)]
            public int32 CallableCdecl(int32 value, int32 ignored) { return value; }

            [NativeCallable(CallConv: NativeCallingConvention.Stdcall)]
            public int32 CallableStdcall(int32 value) { return value; }

            [NativeCallable(CallConv: NativeCallingConvention.Vectorcall)]
            public int32 CallableVectorcall(int32 value) { return value; }

            internal unsafe int32 AcceptNative(fnptr<int32, int32> callback, int32 value) {
                return 0;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitDevelopmentNativeConventionsDraft(
            compilation.Program!, "native-conventions-provider");
        var index = TrRmArtifactIndex.ForDevelopmentNativeConventionsDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateDevelopmentNativeConventionsDraftAndGetExecutionTables(index);
        var loaded = TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(artifact);
        var nativeAbisByName = new Dictionary<string, TrRmNativeAbiContract>(
            StringComparer.Ordinal);
        var nativeAbiPayloadOffsetsByName = new Dictionary<string, int>(
            StringComparer.Ordinal);
        var role3Conventions = new List<byte>();
        string? firstAnchorName = null;
        var contractSectionOffset = FindSectionOffset(artifact, 0x001B);
        var contractIndexByteCount = checked((tables.Contracts.Count + 1) * 4);
        for (var method = 1; method <= tables.Methods.Count; method++)
        {
            if (!tables.Contracts.HasNativeAbi(method))
            {
                continue;
            }

            var name = tables.Methods.GetName(method);
            var abi = tables.Contracts.GetNativeAbi(method);
            nativeAbisByName.Add(name, abi);
            if (abi.Role == 3)
            {
                role3Conventions.Add(abi.ConventionOrReserved);
                firstAnchorName ??= name;
            }

            var (first, count) = tables.Methods.GetContractRange(method);
            for (var indexInRange = 0; indexInRange < count; indexInRange++)
            {
                var row = first + indexInRange;
                var contract = tables.Contracts.GetRow(row);
                if (BinaryPrimitives.ReadUInt16LittleEndian(
                    contract.Slice(4, sizeof(ushort))) != 0x10)
                {
                    continue;
                }

                var rowOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    artifact.AsSpan(contractSectionOffset + ((row - 1) * 4))));
                nativeAbiPayloadOffsetsByName.Add(name,
                    contractSectionOffset + contractIndexByteCount + rowOffset + 8);
            }
        }

        foreach (var (name, convention, mode, policy, encodedConvention) in new[]
        {
            ("ImportUnmanaged", NativeCallingConvention.Unmanaged,
                NativeImportResolutionMode.Link, NativeImportResolutionPolicy.NotApplicable, (byte)1),
            ("ImportCdecl", NativeCallingConvention.Cdecl,
                NativeImportResolutionMode.Dynamic, NativeImportResolutionPolicy.Default, (byte)2),
            ("ImportStdcall", NativeCallingConvention.Stdcall,
                NativeImportResolutionMode.Dynamic, NativeImportResolutionPolicy.CompatibilitySearch, (byte)3),
            ("ImportVectorcall", NativeCallingConvention.Vectorcall,
                NativeImportResolutionMode.Link, NativeImportResolutionPolicy.NotApplicable, (byte)4),
        })
        {
            var imported = loaded.Functions.Single(function => function.Name == name);
            Assert.That(imported.DirectNativeImport!.CallingConvention,
                Is.EqualTo(convention), name);
            Assert.That(imported.DirectNativeImport.ResolutionMode, Is.EqualTo(mode), name);
            Assert.That(imported.DirectNativeImport.ResolutionPolicy, Is.EqualTo(policy), name);
            Assert.That(nativeAbisByName[name].ConventionOrReserved,
                Is.EqualTo(encodedConvention), name);
        }

        foreach (var (name, convention, encodedConvention) in new[]
        {
            ("CallableUnmanaged", NativeCallingConvention.Unmanaged, (byte)1),
            ("CallableCdecl", NativeCallingConvention.Cdecl, (byte)2),
            ("CallableStdcall", NativeCallingConvention.Stdcall, (byte)3),
            ("CallableVectorcall", NativeCallingConvention.Vectorcall, (byte)4),
        })
        {
            var callable = loaded.Functions.Single(function => function.Name == name);
            Assert.That(callable.NativeCallableConvention, Is.EqualTo(convention), name);
            Assert.That(nativeAbisByName[name].ConventionOrReserved,
                Is.EqualTo(encodedConvention), name);
        }

        var anchorConventions = role3Conventions
            .Distinct()
            .Order()
            .ToArray();
        Assert.That(anchorConventions, Is.EqualTo(new byte[] { 0, 1, 2, 3, 4 }));
        Assert.That(loaded.NativeFunctionPointerSignatures
            .Select(static signature => signature.CallingConvention)
            .Distinct()
            .Order()
            .ToArray(), Is.EqualTo(new[] {
                NativeCallingConvention.Native,
                NativeCallingConvention.Unmanaged,
                NativeCallingConvention.Cdecl,
                NativeCallingConvention.Stdcall,
                NativeCallingConvention.Vectorcall,
            }.Order().ToArray()));

        var reemitted = TrRmExecutableCompiler.EmitDevelopmentNativeConventionsDraft(
            loaded, "native-conventions-reemitted");
        var reloaded = TrRmExecutableCompiler
            .LoadDevelopmentNativeConventionsDraft(reemitted);
        Assert.That(reloaded.Functions.Single(function =>
            function.Name == "ImportStdcall").DirectNativeImport!.ResolutionPolicy,
            Is.EqualTo(NativeImportResolutionPolicy.CompatibilitySearch));
        Assert.That(reloaded.Functions.Single(function =>
            function.Name == "CallableVectorcall").NativeCallableConvention,
            Is.EqualTo(NativeCallingConvention.Vectorcall));
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibrary(
            loaded, ["CallableVectorcall"], NativeRuntimePolicy.FailFast));

        var reference = TrRmExecutableCompiler
            .CreateDevelopmentNativeConventionsDraftReference(artifact);
        var consumer = Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    return ImportStdcall() + CallableCdecl(7, 0);
                }
            }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        var importedFromReference = consumer.Program!.Functions.Single(function =>
            function.Name == "ImportStdcall");
        Assert.That(importedFromReference.DirectNativeImport!.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Stdcall));
        Assert.That(importedFromReference.DirectNativeImport.ResolutionMode,
            Is.EqualTo(NativeImportResolutionMode.Dynamic));
        Assert.That(importedFromReference.DirectNativeImport.ResolutionPolicy,
            Is.EqualTo(NativeImportResolutionPolicy.CompatibilitySearch));
        var callableFromReference = consumer.Program.Functions.Single(function =>
            function.Name == "CallableCdecl");
        Assert.That(callableFromReference.NativeCallableConvention,
            Is.EqualTo(NativeCallingConvention.Cdecl));
        var importConsumer = Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe { return ImportStdcall(); }
            }
            """), reference);
        Assert.That(importConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, importConsumer.Diagnostics));
        _ = Assert.Throws<NotSupportedException>(() =>
            NativeEmitter.EmitWithNativeImports(importConsumer.Program!,
                NativeRuntimePolicy.FailFast));
        var consumerArtifact = TrRmExecutableCompiler.EmitDevelopmentNativeConventionsDraft(
            importConsumer.Program!, "native-conventions-consumer");
        var loadedConsumer = TrRmExecutableCompiler
            .LoadDevelopmentNativeConventionsDraft(consumerArtifact);
        Assert.That(loadedConsumer.Functions.Single(function =>
            function.Name == "ImportStdcall").DirectNativeImport!.ResolutionPolicy,
            Is.EqualTo(NativeImportResolutionPolicy.CompatibilitySearch));

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(artifact));

        var importSectionOffset = FindSectionOffset(artifact, 0x0030);
        var malformedResolution = (byte[])artifact.Clone();
        malformedResolution[importSectionOffset + 12] = 1;
        malformedResolution[importSectionOffset + 13] = 1;
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(
                malformedResolution));
        var malformedReserved = (byte[])artifact.Clone();
        malformedReserved[importSectionOffset + 14] = 1;
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(
                malformedReserved));

        var importAbiOffset = nativeAbiPayloadOffsetsByName["ImportUnmanaged"];
        var malformedImportConvention = (byte[])artifact.Clone();
        malformedImportConvention[importAbiOffset + 6] = 0;
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(
                malformedImportConvention));
        var callableAbiOffset = nativeAbiPayloadOffsetsByName["CallableUnmanaged"];
        var malformedCallableConvention = (byte[])artifact.Clone();
        malformedCallableConvention[callableAbiOffset + 6] = 0;
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(
                malformedCallableConvention));
        var mismatchedCallableConvention = (byte[])artifact.Clone();
        mismatchedCallableConvention[nativeAbiPayloadOffsetsByName["CallableCdecl"] + 6] = 3;
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(
                mismatchedCallableConvention));
        var anchorAbiOffset = nativeAbiPayloadOffsetsByName[firstAnchorName!];
        var malformedAnchorConvention = (byte[])artifact.Clone();
        malformedAnchorConvention[anchorAbiOffset + 6] = 5;
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadDevelopmentNativeConventionsDraft(
                malformedAnchorConvention));
    }

    [Test]
    public void NativeAddressMayProjectNoReturnTargetToDefaultReturningView()
    {
        var compilation = Create(SyntaxTree.Parse("""
            [DoesNotReturn]
            [Terrum.Interop.NativeCallable]
            int32 Stops() { while (true) { } }

            int32 Main() {
                unsafe {
                    [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32> callback = &Stops;
                    return callback();
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var target = program.Functions.Single(static function => function.Name == "Stops");
        var callbackType = PrimitiveType.Void;
        var indirectCallType = PrimitiveType.Void;
        var main = program.Functions.Single(static function => function.Name == "Main");
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body, expression =>
        {
            if (expression is BoundNativeFunctionAddress address)
            {
                callbackType = address.Type;
            }
            else if (expression is BoundIndirectCall call)
            {
                indirectCallType = call.Target.Type;
            }
        }), Is.True);
        var targetType = target.NativeFunctionPointerType!.Value;
        Assert.That(program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(targetType)].EffectsMask, Is.EqualTo(2));
        Assert.That(program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(callbackType)].EffectsMask, Is.Zero);
        Assert.That(NativeFunctionPointerContract.CanProject(
            targetType, callbackType, program.NativeFunctionPointerSignatures), Is.True);
        Assert.That(indirectCallType, Is.EqualTo(callbackType));
    }

    [Test]
    public void NativeAddressWithoutExpectedTypeRetainsTargetContract()
    {
        var compilation = Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            int32 Answer() { return 42; }

            int32 Main() {
                unsafe {
                    var callback = &Answer;
                    return callback() == 42 ? 0 : -1;
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var target = program.Functions.Single(static function => function.Name == "Answer");
        var main = program.Functions.Single(static function => function.Name == "Main");
        var addressType = PrimitiveType.Void;
        var callType = PrimitiveType.Void;
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body, expression =>
        {
            if (expression is BoundNativeFunctionAddress address)
            {
                addressType = address.Type;
            }
            else if (expression is BoundIndirectCall call)
            {
                callType = call.Target.Type;
            }
        }), Is.True);
        Assert.That(addressType, Is.EqualTo(target.NativeFunctionPointerType));
        Assert.That(callType, Is.EqualTo(addressType));
        Assert.That(NativeFunctionPointerContract.MatchesFunction(
            target, addressType, program.NativeFunctionPointerSignatures), Is.True);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public void NativeAddressInfersOrdinaryTerrumFunctionContract()
    {
        var compilation = Create(SyntaxTree.Parse("""
            int32 Increment(int32 value) { return value + 1; }

            int32 Main() {
                unsafe {
                    fnptr<int32, int32> typed = &Increment;
                    var inferred = &Increment;
                    return typed(20) == inferred(21) - 1 ? 0 : -1;
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var target = program.Functions.Single(static function => function.Name == "Increment");
        var main = program.Functions.Single(static function => function.Name == "Main");
        var addressTypes = new List<PrimitiveType>();
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body, expression =>
        {
            if (expression is BoundNativeFunctionAddress address)
            {
                addressTypes.Add(address.Type);
            }
        }), Is.True);
        Assert.That(addressTypes.Count, Is.EqualTo(2));
        Assert.That(addressTypes[0], Is.EqualTo(addressTypes[1]));
        Assert.That(NativeFunctionPointerContract.MatchesAddressableFunction(
            target, addressTypes[0], program.NativeFunctionPointerSignatures), Is.True);
        Assert.That(program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(addressTypes[0])].CallingConvention,
            Is.EqualTo(NativeCallingConvention.Native));
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public void NativeAddressRejectsOrdinaryInstanceMethods()
    {
        var compilation = Create(SyntaxTree.Parse("""
            struct Counter {
                int32 Get() { return 42; }
            }

            int32 Main() {
                unsafe {
                    var callback = &Counter.Get;
                    return 0;
                }
            }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"),
            Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void OrdinaryTerrumFunctionAddressSurvivesPrivateModuleRoundTrip()
    {
        var providerCompilation = CreateLibrary(SyntaxTree.Parse("""
            int32 Increment(int32 value) { return value + 1; }

            public int32 Invoke(int32 value) {
                unsafe {
                    var callback = &Increment;
                    return callback(value);
                }
            }
            """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(providerCompilation.Program!, ["Invoke"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Create(SyntaxTree.Parse(
            "int32 Main() { return Invoke(41) == 42 ? 0 : -1; }"), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(consumerCompilation.Program!, producer);

        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer),
            NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public void IndirectNoReturnContractSuppressesNormalFlowAndEmitsNoReturnTrap()
    {
        var compilation = Create(SyntaxTree.Parse("""
            [DoesNotReturn]
            [Terrum.Interop.NativeCallable]
            int32 Stops() { while (true) { } }

            int32 Invoke([Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32> callback) {
                unsafe { return callback(); }
            }

            int32 Main() { return 0; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var stop = program.Functions.Single(static function => function.Name == "Stops");
        var invokeIndex = Enumerable.Range(0, program.Functions.Length)
            .Single(index => program.Functions[index].Name == "Invoke");
        var invoke = program.Functions[invokeIndex];
        var returningType = invoke.Variables[0].Type;
        var noReturnType = stop.NativeFunctionPointerType!.Value;
        Assert.That(NativeFunctionPointerContract.CanProject(
            noReturnType, returningType, program.NativeFunctionPointerSignatures), Is.True);
        Assert.That(NativeFunctionPointerContract.CanProject(
            returningType, noReturnType, program.NativeFunctionPointerSignatures), Is.False);

        var noReturnInvoke = BoundTreeTraversal.RewriteTypes(invoke,
            type => type == returningType ? noReturnType : type) with {
            DoesNotReturn = true,
        };
        var forged = program with {
            Functions = program.Functions.SetItem(invokeIndex, noReturnInvoke),
        };
        SemanticVerifier.Verify(forged);

        var code = NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast);
        Assert.That(code.Code.AsSpan().IndexOf(new byte[] { 0x0F, 0x0B }),
            Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void FunctionPointerIdentityRetainsDependencyContracts()
    {
        var plain = new NativeFunctionPointerSignatureDefinition(
            NativeCallingConvention.Native, PrimitiveType.NativeRawPointer,
            [PrimitiveType.NativeRawPointer]);
        var borrowing = plain with {
            Dependencies = [
                new NativeFunctionPointerDependency(1, 1, 3, 1, 0, 0),
            ],
        };
        var backingOnly = plain with {
            Dependencies = [
                new NativeFunctionPointerDependency(1, 1, 2, 1, 0, 0),
            ],
        };
        Assert.That(NativeFunctionPointerContract.IsValidSignature(
            plain, [plain]), Is.True);
        Assert.That(NativeFunctionPointerContract.IsValidSignature(
            borrowing, [borrowing]), Is.True);
        Assert.That(NativeFunctionPointerContract.AreEquivalent(
            plain, borrowing), Is.False);
        Assert.That(NativeFunctionPointerContract.AreEquivalent(
            borrowing, backingOnly), Is.False);
        Assert.That(NativeFunctionPointerContract.CanProject(
            NativeFunctionPointerTypes.Type(0), NativeFunctionPointerTypes.Type(0),
            [borrowing]), Is.True);
        Assert.That(NativeFunctionPointerContract.CanPersistRevision92(plain), Is.True);
        Assert.That(NativeFunctionPointerContract.CanPersistRevision92(borrowing), Is.False);

        var multipleSources = plain with {
            ParameterTypes = [PrimitiveType.NativeRawPointer, PrimitiveType.NativeRawPointer],
            Dependencies = [
                new NativeFunctionPointerDependency(1, 1, 3, 1, 0, 0),
                new NativeFunctionPointerDependency(1, 1, 3, 1, 0, 1),
            ],
        };
        var reorderedSources = multipleSources with {
            Dependencies = [
                new NativeFunctionPointerDependency(1, 1, 3, 1, 0, 1),
                new NativeFunctionPointerDependency(1, 1, 3, 1, 0, 0),
            ],
        };
        Assert.That(NativeFunctionPointerContract.IsValidSignature(
            multipleSources, [multipleSources]), Is.True);
        Assert.That(NativeFunctionPointerContract.AreEquivalent(
            multipleSources, reorderedSources), Is.True);
        var backingSubset = multipleSources with {
            Dependencies = [
                new NativeFunctionPointerDependency(1, 1, 2, 1, 0, 0),
            ],
        };
        var projectionSignatures = ImmutableArray.Create(multipleSources, backingSubset);
        Assert.That(NativeFunctionPointerContract.CanProject(
            NativeFunctionPointerTypes.Type(1), NativeFunctionPointerTypes.Type(0),
            projectionSignatures), Is.True);
        Assert.That(NativeFunctionPointerContract.CanProject(
            NativeFunctionPointerTypes.Type(0), NativeFunctionPointerTypes.Type(1),
            projectionSignatures), Is.False);

        var explicitEmpty = plain with {
            Dependencies = [
                new NativeFunctionPointerDependency(1, 0, 0, 1, 0, 0),
            ],
        };
        var broadSingleSource = plain with {
            Dependencies = [
                new NativeFunctionPointerDependency(1, 1, 3, 1, 0, 0),
            ],
        };
        var emptyProjectionSignatures = ImmutableArray.Create(
            explicitEmpty, broadSingleSource, plain);
        Assert.That(NativeFunctionPointerContract.AreEquivalent(
            explicitEmpty, plain), Is.False);
        Assert.That(NativeFunctionPointerContract.CanProject(
            NativeFunctionPointerTypes.Type(0), NativeFunctionPointerTypes.Type(1),
            emptyProjectionSignatures), Is.True);
        Assert.That(NativeFunctionPointerContract.CanProject(
            NativeFunctionPointerTypes.Type(2), NativeFunctionPointerTypes.Type(1),
            emptyProjectionSignatures), Is.False);

        var malformedEmptySource = borrowing with {
            Dependencies = [
                new NativeFunctionPointerDependency(1, 0, 1, 1, 0, 0),
            ],
        };
        Assert.That(NativeFunctionPointerContract.IsValidSignature(
            malformedEmptySource, [malformedEmptySource]), Is.False);
        var unsupportedCheckedOutput = plain with {
            OutputStates = [new NativeFunctionPointerOutputState(0, 0, 1)],
        };
        Assert.That(NativeFunctionPointerContract.IsValidSignature(
            unsupportedCheckedOutput, [unsupportedCheckedOutput]), Is.False);

        var sourceDependency = new BoundDependencyContract(
            BoundDependencyRelation.DependsOn,
            BoundDependencyDestination.Result, null,
            [new BoundDependencySource(BoundDependencySourceKind.Formal, 0)]);
        Assert.That(NativeFunctionPointerContract.TryCreateDependencies(
            [sourceDependency], out var mappedDependencies), Is.True);
        Assert.That(mappedDependencies, Is.EqualTo(
        [
            new NativeFunctionPointerDependency(1, 1, 3, 1, 0, 0),
        ]));

        var capture = new BoundDependencyContract(
            BoundDependencyRelation.Captures,
            BoundDependencyDestination.RetainedFormal, 1,
            [new BoundDependencySource(BoundDependencySourceKind.Formal, 0)]);
        Assert.That(NativeFunctionPointerContract.TryCreateDependencies(
            [capture], out mappedDependencies), Is.True);
        Assert.That(mappedDependencies, Is.EqualTo(
        [
            new NativeFunctionPointerDependency(3, 1, 3, 2, 1, 0),
        ]));

        var forwardsBacking = sourceDependency with {
            Relation = BoundDependencyRelation.ForwardsBacking,
        };
        Assert.That(NativeFunctionPointerContract.TryCreateDependencies(
            [sourceDependency, forwardsBacking], out mappedDependencies), Is.True);
        Assert.That(mappedDependencies, Is.EqualTo(
        [
            new NativeFunctionPointerDependency(1, 1, 2, 1, 0, 0),
        ]));
    }

    [Test]
    public void NativeCallableFunctionPointerCarriesDeclaredResultDependency()
    {
        var compilation = CreateLibrary(SyntaxTree.Parse("""
            [return: DependsOn(nameof(source), nameof(other))]
            [Terrum.Interop.NativeCallable]
            ptr<int32> Broad(ptr<int32> source, ptr<int32> other) {
                return source;
            }

            [return: DependsOn(nameof(source), nameof(other))]
            [return: ForwardsBacking(nameof(source))]
            [Terrum.Interop.NativeCallable]
            ptr<int32> Forward(ptr<int32> source, ptr<int32> other) { return source; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var broad = program.Functions.Single(static candidate => candidate.Name == "Broad");
        var function = program.Functions.Single(static candidate => candidate.Name == "Forward");
        var type = function.NativeFunctionPointerType!.Value;
        var signature = program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(type)];
        var broadSignature = program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(broad.NativeFunctionPointerType!.Value)];
        Assert.That(broad.NativeFunctionPointerType,
            Is.Not.EqualTo(function.NativeFunctionPointerType));
        Assert.That(NativeFunctionPointerContract.AreEquivalent(
            broadSignature, signature), Is.False);
        Assert.That(signature.Dependencies, Is.EqualTo(
        [
            new NativeFunctionPointerDependency(1, 1, 2, 1, 0, 0),
            new NativeFunctionPointerDependency(1, 1, 3, 1, 0, 1),
        ]));
        Assert.That(NativeFunctionPointerContract.MatchesFunction(
            function, type, program.NativeFunctionPointerSignatures), Is.True);
        Assert.That(NativeFunctionPointerContract.MatchesFunction(
            broad, broad.NativeFunctionPointerType.Value,
            program.NativeFunctionPointerSignatures), Is.True);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(program, ["Forward"]));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(program, "native-callable-dependencies"));
    }

    [Test]
    public void NativeFunctionAddressRequiresExactSignature()
    {
        var compilation = Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            int32 Answer() { return 42; }

            int32 Main() {
                unsafe {
                    [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int64> callback = &Answer;
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
        var compilation = Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            int32 Answer() { return 42; }

            int32 Main() {
                [Terrum.Interop.NativeCallConv(Terrum.Interop.NativeCallingConvention.Unmanaged)] fnptr<int32> callback = &Answer;
                return 0;
            }
            """));

        Assert.That(compilation.Diagnostics.Any(diagnostic => diagnostic.Code == "TRM3028"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [TestCase("""
        [Terrum.Interop.NativeCallable]
        int32 ReadSourceLength() { return Host.Source.Length; }
        """, "TRM3016")]
    [TestCase("""

        int32 SourceLength() { return Host.Source.Length; }
        [Terrum.Interop.NativeCallable]
        int32 ReadSourceLength() { return SourceLength(); }
        """, "TRM3030")]
    public void NativeCallableCannotDependOnInvocationScopedSource(string source, string expectedCode)
    {
        var compilation = CreateLibrary([SyntaxTree.Parse(source)],
            [new HostBuffer("Source", PrimitiveType.Char16, [], false) { IsInvocationScoped = true }],
            BoundRuntimePolicy.FailFast);

        Assert.That(compilation.Diagnostics.Any(diagnostic => diagnostic.Code == expectedCode),
            Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void NativeCallableCannotDependOnNonEmptyInvocationScopedSource()
    {
        var compilation = CreateLibrary([SyntaxTree.Parse("""
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
        var compilation = CreateLibrary([SyntaxTree.Parse("""
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
        var compilation = CreateLibrary([SyntaxTree.Parse("""
            [Terrum.Interop.NativeCallable]
            int32 Answer() { return 42; }
            """)], [new HostBuffer("Source", PrimitiveType.Char16, [65], false) { IsInvocationScoped = true }],
            BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
    }

    private static Compilation Create(SyntaxTree tree,
        BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast) =>
        Compilation.Create(tree, CreateLayoutOptions(runtimePolicy));

    private static Compilation Create(SyntaxTree tree,
        CompilationReference reference,
        BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast) =>
        Compilation.Create(tree, reference,
            CreateReferenceOptions(reference, runtimePolicy));

    private static Compilation Create(IEnumerable<SyntaxTree> trees,
        BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast) =>
        Compilation.Create(trees, CreateLayoutOptions(runtimePolicy));

    private static Compilation CreateLibrary(SyntaxTree tree,
        BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast) =>
        Compilation.CreateLibrary(tree, CreateLayoutOptions(runtimePolicy));

    private static Compilation CreateLibrary(SyntaxTree tree,
        CompilationReference reference,
        BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast) =>
        Compilation.CreateLibrary(tree, reference,
            CreateReferenceOptions(reference, runtimePolicy));

    private static Compilation CreateLibrary(IEnumerable<SyntaxTree> trees,
        BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast) =>
        Compilation.CreateLibrary(trees, CreateLayoutOptions(runtimePolicy));

    private static Compilation CreateLibrary(IEnumerable<SyntaxTree> trees,
        ImmutableArray<HostBuffer> buffers,
        BoundRuntimePolicy runtimePolicy = BoundRuntimePolicy.FailFast) =>
        Compilation.CreateLibrary(trees, buffers,
            CreateLayoutOptions(runtimePolicy));

    private static CompilationOptions CreateLayoutOptions(
        BoundRuntimePolicy runtimePolicy)
        => NativeCoreRegistrationTests.CreateRegisteredCompilationOptions(runtimePolicy);

    private static CompilationOptions CreateReferenceOptions(
        CompilationReference reference, BoundRuntimePolicy runtimePolicy)
    {
        if (reference.NativeCoreRegistration is not null)
        {
            return new CompilationOptions {
                RuntimePolicy = runtimePolicy,
            };
        }

        if (reference.LayoutCoreRegistration is not null)
        {
            return new CompilationOptions {
                RuntimePolicy = runtimePolicy,
            };
        }

        return NativeCoreRegistrationTests.CreateRegisteredCompilationOptions(
            runtimePolicy);
    }

    private static byte[] CreateProducer(string source, string[] exports)
    {
        var compilation = CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return ModuleCompiler.CreateProducer(compilation.Program!, exports);
    }

    private static byte[] CreateNativeProducer(string source, string[] exports, string identity)
    {
        var compilation = CreateLibrary(SyntaxTree.Parse(source));
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
        Assert.That(reader.ReadInt32(), Is.EqualTo(106));
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

    private static int FindPrivate106SignatureParameterTypeOffset(
        byte[] module, int expectedParameterCount, int parameterOrdinal)
    {
        var tableOffset = FindSignatureTableOffset(module);
        using var stream = new MemoryStream(module, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8,
            leaveOpen: true);
        stream.Position = tableOffset;
        var signatureCount = reader.ReadInt32();
        Assert.That(signatureCount, Is.GreaterThan(0));
        for (var index = 0; index < signatureCount; index++)
        {
            _ = reader.ReadByte();
            _ = reader.ReadInt32();
            var parameterCount = reader.ReadInt32();
            var parameterTypeOffset = checked((int)stream.Position);
            if (parameterCount == expectedParameterCount)
            {
                Assert.That(parameterOrdinal, Is.InRange(0,
                    parameterCount - 1));
                return checked(parameterTypeOffset
                    + (parameterOrdinal * sizeof(int)));
            }

            stream.Position = checked(stream.Position
                + ((long)parameterCount * sizeof(int)));
            _ = reader.ReadUInt32();
            SkipPrivate106Rows(reader, rowSize: 12);
            SkipPrivate106Rows(reader, rowSize: 12);
            SkipPrivate106Rows(reader, rowSize: 12);
            SkipPrivate106Rows(reader, rowSize: 12);
        }

        throw new InvalidDataException(
            "The private 105 signature table has no requested parameter count.");
    }

    private static void SkipPrivate106Rows(BinaryReader reader, int rowSize)
    {
        var count = reader.ReadInt32();
        if (count < 0)
        {
            throw new InvalidDataException(
                "A private 105 signature vector count is negative.");
        }
        reader.BaseStream.Position = checked(reader.BaseStream.Position
            + ((long)count * rowSize));
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
        var index = TrRmArtifactIndex.ForDevelopmentNativeConventionsDraft(artifact);
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
                        bytes.AsSpan((row * 48) + 16));
                    if (entryPoint != 0)
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((row * 48) + 16),
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
                    bytes = [.. rows.SelectMany(static row => row)];
                }
            }
            sections.Add(new TrRmSection(kind, flags, (uint)recordCount, bytes));
        }

        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.DevelopmentNativeConventionsDraft);
    }

    private static void ValidateDevelopmentNativeConventionsDraft(byte[] artifact)
    {
        var index = TrRmArtifactIndex.ForDevelopmentNativeConventionsDraft(artifact);
        _ = TrRmStructuralMetadataValidator
            .ValidateDevelopmentNativeConventionsDraftAndGetExecutionTables(index);
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
        var index = TrRmArtifactIndex.ForDevelopmentNativeConventionsDraft(artifact);
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
        var index = TrRmArtifactIndex.ForDevelopmentNativeConventionsDraft(artifact);
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
        var index = TrRmArtifactIndex.ForDevelopmentNativeConventionsDraft(artifact);
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
                var featureCount = index.TryGetSection(0x0004, out _, out var count)
                    ? count : throw new InvalidOperationException("Missing Feature section.");
                var modules = new byte[bytes.Length + 48];
                bytes.CopyTo(modules, 0);
                bytes.CopyTo(modules, bytes.Length);
                var firstModule = modules.AsSpan(0, 48);
                var secondModule = modules.AsSpan(48, 48);
                BinaryPrimitives.WriteUInt32LittleEndian(firstModule[24..], 1);
                BinaryPrimitives.WriteUInt32LittleEndian(firstModule[28..],
                    includeRootlessFeature ? (uint)featureCount : 1);
                BinaryPrimitives.WriteUInt32LittleEndian(secondModule, methodNameRow);
                BinaryPrimitives.WriteUInt32LittleEndian(secondModule[4..], 1);
                BinaryPrimitives.WriteUInt32LittleEndian(secondModule[16..], 0);
                BinaryPrimitives.WriteUInt32LittleEndian(secondModule[24..], 1);
                BinaryPrimitives.WriteUInt32LittleEndian(secondModule[28..],
                    includeRootlessFeature ? (uint)featureCount : 1);
                BinaryPrimitives.WriteUInt32LittleEndian(secondModule[40..], 2);
                bytes = modules;
                recordCount++;
            }
            sections.Add(new TrRmSection(kind, flags, (uint)recordCount, bytes));
        }

        return TrRmArtifactWriter.EncodeDevelopment(sections);
    }

    private static byte[] ReverseNativeFunctionPointerSignatureRows(byte[] artifact)
    {
        var index = TrRmArtifactIndex.ForDevelopment(artifact);
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

        return TrRmArtifactWriter.EncodeDevelopment(sections);
    }

    private static void SkipString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        _ = reader.ReadBytes(length);
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
