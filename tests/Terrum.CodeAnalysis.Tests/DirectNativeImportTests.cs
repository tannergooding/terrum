// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class DirectNativeImportTests
{
    private const string ImportDeclaration = """
        [Terrum.Interop.NativeImport("kernel32.dll", "QueryPerformanceCounter")]
        [Terrum.Interop.NativeOutput(0)]
        extern int32 QueryPerformanceCounter(write ptr<int64> counter);
        """;
    private const string PublicImportDeclaration = """
        [Terrum.Interop.NativeImport("kernel32.dll", "QueryPerformanceCounter")]
        [Terrum.Interop.NativeOutput(0)]
        public extern int32 QueryPerformanceCounter(write ptr<int64> counter);
        """;
    private const string OpaquePointerImportDeclaration = """
        [Terrum.Interop.NativeImport("example.dll", "AcquireInterface")]
        [Terrum.Interop.NativeOutput(0)]
        public extern int32 AcquireInterface(write ptr<ptr<void>> result);
        """;

    [Test]
    public void SourceAuthoredImportEmitsTheNamedWindowsBinding()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ImportDeclaration + """
            int32 Main() {
                mut int64 counter = 0;
                unsafe {
                    if (QueryPerformanceCounter(&counter) != 0) { return 0; }
                }
                return -1;
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var image = NativeEmitter.EmitWithNativeImports(
            compilation.Program!, NativeRuntimePolicy.FailFast);

        Assert.That(Encoding.ASCII.GetString(image),
            Does.Contain("QueryPerformanceCounter"));
        Assert.That(Encoding.ASCII.GetString(image),
            Does.Contain("KERNEL32.dll"));
    }

    [Test]
    public void DirectNativeImportBindsOpaqueContextAndWritableBlittableRecord()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct FixtureRectangle {
                int32 Left;
                int32 Top;
                int32 Right;
                int32 Bottom;

                public FixtureRectangle(int32 left, int32 top, int32 right, int32 bottom) {
                    Left = left;
                    Top = top;
                    Right = right;
                    Bottom = bottom;
                }
            }

            [Terrum.Interop.NativeImport("fixture.dll", "ReadRectangle")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
            extern int32 ReadRectangle(ptr<void> context, ptr<FixtureRectangle> rectangle);

            int32 Main() {
                mut FixtureRectangle rectangle = new FixtureRectangle(0, 0, 0, 0);
                unsafe { return ReadRectangle(null, &rectangle); }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");
        var import = program.Functions.Single(static function =>
            function.Name == "ReadRectangle");
        Assert.That(import.Variables.Select(static variable => variable.Type),
            Is.EqualTo([PrimitiveType.NativeRawPointer, PrimitiveType.NativeRawPointer]));
        Assert.That(import.DirectNativeImport!.Parameters, Is.EqualTo([
            new DirectNativeParameter(0, PrimitiveType.Void,
                NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 0),
            new DirectNativeParameter(1, (PrimitiveType)100,
                NativeParameterAccess.Write, NativeParameterRetention.NoEscape, 1),
        ]));
        Assert.That(Encoding.ASCII.GetString(
            NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast)),
            Does.Contain("ReadRectangle"));
    }

    [Test]
    public void DirectNativeImportAcceptsNestedBlittableStructThroughTypedPointer()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct FixtureHeader {
                uint64 First;
                uint64 Second;
                uint64 Third;

                public FixtureHeader(uint64 first, uint64 second, uint64 third) {
                    First = first;
                    Second = second;
                    Third = third;
                }
            }

            [Blittable] [Layout(LayoutKind.Sequential)]
            struct FixtureDescriptor {
                FixtureHeader Header;
                uint64 OutputHandle;
                uint32 Count;
                uint32 Flags;

                public FixtureDescriptor(FixtureHeader header, uint64 outputHandle,
                    uint32 count, uint32 flags) {
                    Header = header;
                    OutputHandle = outputHandle;
                    Count = count;
                    Flags = flags;
                }
            }

            [Terrum.Interop.NativeImport("fixture.dll", "ReadDescriptor")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            extern int32 ReadDescriptor(ptr<FixtureDescriptor> descriptor);

            int32 Main() {
                FixtureDescriptor descriptor = new FixtureDescriptor(
                    new FixtureHeader(1, 2, 3), 4, 5, 6);
                unsafe { return ReadDescriptor(&descriptor); }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");
        var descriptor = program.Structures.Single(static structure =>
            structure.Name == "FixtureDescriptor");
        Assert.That(descriptor.Size, Is.EqualTo(40));
        Assert.That(descriptor.Fields.Select(static field => field.Offset), Is.EqualTo([0, 24, 32, 36]));
        Assert.That(Encoding.ASCII.GetString(
            NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast)),
            Does.Contain("ReadDescriptor"));
    }

    [Test]
    public void DirectNativeImportAcceptsGenericFunctionPointerParameters()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("fixture.dll", "InvokeCallback")]
            public unsafe extern int32 InvokeCallback(
                fnptr<NativeAbi, int32, int32> callback, int32 value);

            [NoThrow]
            [Terrum.Interop.NativeCallable]
            int32 Echo(int32 value) { return value; }

            int32 Main() {
                unsafe { return InvokeCallback(&Echo, 42); }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");
        var import = program.Functions.Single(static function =>
            function.Name == "InvokeCallback");
        Assert.That(import.Variables[0].Type, Is.Not.EqualTo(PrimitiveType.NativeRawPointer));
        Assert.That(import.DirectNativeImport, Is.Not.Null);
        Assert.That(Encoding.ASCII.GetString(NativeEmitter.EmitWithNativeImports(
            program, NativeRuntimePolicy.FailFast)), Does.Contain("InvokeCallback"));
    }

    [Test]
    public void RetiredNativeStorageRecordOrdinalIsRejectedAsAValueType()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Identity(int32 value) { return value; }
            int32 Main() { return Identity(42); }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var identity = program.Functions.Single(static function => function.Name == "Identity");
        var identityIndex = program.Functions.IndexOf(identity);
        var variables = identity.Variables.SetItem(0,
            identity.Variables[0] with { Type = PrimitiveType.LegacyNativeStorageRecord });
        var forgedProgram = program with {
            Functions = program.Functions.SetItem(identityIndex, identity with { Variables = variables }),
        };

        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImports(forgedProgram, NativeRuntimePolicy.FailFast))!;
    }

    [Test]
    public void NativeStorageAddressIsNotAnExposedSourceType()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("fixture.dll", "ConsumeStorage")]
            extern int32 ConsumeStorage(Terrum.Interop.NativeStorageAddress value);

            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void RawPointerNullAndEqualityUseOrdinaryPointerValues()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    ptr<void> first = null;
                    ptr<void> second = null;
                    return first == null && first == second ? 0 : -1;
                }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast), Is.Zero);
    }

    [Test]
    public void BlittableRecordsCarryOpaquePointerFieldsThroughDirectImports()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct FixturePointerRecord {
                public ptr<void> Context;
                public uint64 Value;

                public FixturePointerRecord(ptr<void> context, uint64 value) {
                    Context = context;
                    Value = value;
                }
            }

            [Terrum.Interop.NativeImport("fixture.dll", "ObserveRecord")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            extern int32 ObserveRecord(ptr<FixturePointerRecord> value);

            int32 Main() {
                FixturePointerRecord record = new FixturePointerRecord(null, 42);
                unsafe { return ObserveRecord(&record); }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var recordType = compilation.Program!.Structures.Single(static structure =>
            structure.Name == "FixturePointerRecord");
        Assert.That(recordType.Size, Is.EqualTo(16));
        Assert.That(recordType.Fields.Select(static field => field.Offset), Is.EqualTo([0, 8]));
        Assert.That(recordType.Fields[0].Type, Is.EqualTo(PrimitiveType.NativeRawPointer));
        Assert.That(Encoding.ASCII.GetString(NativeEmitter.EmitWithNativeImports(
            compilation.Program, NativeRuntimePolicy.FailFast)), Does.Contain("ObserveRecord"));
    }

    [Test]
    public void DirectNativeImportSupportsOpaquePointerAndFloatingResultsThroughTrRm()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("fixture.dll", "AcquireContext")]
            public extern ptr<void> AcquireContext();

            [Terrum.Interop.NativeImport("fixture.dll", "ReadTemperature")]
            public extern float64 ReadTemperature();
            """));
        Assert.That(provider.HasErrors, Is.False,
            string.Join(Environment.NewLine, provider.Diagnostics));

        var artifact = TrRmExecutableCompiler.Emit(provider.Program!, "pointer-results");
        var reference = TrRmExecutableCompiler.CreateReference(artifact);
        var consumer = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    ptr<void> context = AcquireContext();
                    _ = ReadTemperature();
                }
                return 0;
            }
            """), reference);

        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        var imports = TrRmExecutableCompiler.Load(artifact).Functions
            .Where(static function => function.IsExternal).ToArray();
        Assert.That(imports.Select(static function => function.ReturnType),
            Is.EqualTo([PrimitiveType.NativeRawPointer, PrimitiveType.Float64]));
        Assert.That(Encoding.ASCII.GetString(
            NativeEmitter.EmitWithNativeImports(consumer.Program!, NativeRuntimePolicy.FailFast)),
            Does.Contain("AcquireContext"));

        var privateArtifact = ModuleCompiler.CreateProducer(provider.Program!,
            ["AcquireContext", "ReadTemperature"]);
        var privateReference = ModuleCompiler.CreateReference(privateArtifact);
        Assert.That(privateReference.Functions.Select(static function => function.ReturnType),
            Is.EqualTo([PrimitiveType.NativeRawPointer, PrimitiveType.Float64]));
    }

    [Test]
    public void DirectNativeImportPreservesTypedPointerResults()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("fixture.dll", "AcquireValue")]
            public extern ptr<int32> AcquireValue();
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");
        var function = program.Functions.Single(static candidate => candidate.Name == "AcquireValue");
        Assert.That(function.RequiresUnsafeCaller, Is.True);
        Assert.That(program.NativePointerTypes, Has.Length.EqualTo(1));
        Assert.That(program.NativePointerTypes[0].PointeeType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(function.ReturnType, Is.EqualTo(NativePointerTypes.Type(0)));
    }

    [Test]
    public void DirectNativeImportDoesNotTreatWindowHandleTypesAsGenericAbiValues()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("fixture.dll", "ReadHandle")]
            public extern int32 ReadHandle(Terrum.Interop.Windows.HWND value);
            """));

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void TrRmNativeImportRejectsWritablePointerResultType()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("fixture.dll", "AcquireContext")]
            public extern ptr<void> AcquireContext();
            """));
        Assert.That(provider.HasErrors, Is.False,
            string.Join(Environment.NewLine, provider.Diagnostics));

        var artifact = TrRmExecutableCompiler.Emit(provider.Program!, "pointer-result");
        var resultTypeRow = FindVariableRow(artifact, 0x0011, [1, 0, 0x8C, 0]);
        var malformed = MutateVariableRowByte(artifact, 0x0011, resultTypeRow, 1, 1);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    [Test]
    public void NativeJitExecutesAValidatedSystem32DirectImport()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("kernel32.dll", "GetCurrentThreadId")]
            [NoThrow]
            extern unsafe uint32 GetCurrentThreadId();

            int32 Main() {
                unsafe {
                    if (GetCurrentThreadId() != 0) { return 0; }
                }
                return -1;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast),
            Is.EqualTo(0));
    }

    [Test]
    public void NativeJitExecutesAValidatedSystem32DirectPointerInput()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("kernel32.dll", "CompareStringOrdinal")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(3, Read, NoEscape, 1)]
            extern int32 CompareStringOrdinal(ptr<char16> first, int32 firstLength,
                ptr<char16> second, int32 secondLength, int32 ignoreCase);

            int32 Main() {
                char16 first = 'A';
                char16 second = 'A';
                unsafe { return CompareStringOrdinal(&first, 1, &second, 1, 0) - 2; }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast),
            Is.EqualTo(0));
    }

    [Test]
    public void NativeJitExecutesABlittableAggregatePointerInput()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct SystemTime {
                char16 Year;
                char16 Month;
                char16 DayOfWeek;
                char16 Day;
                char16 Hour;
                char16 Minute;
                char16 Second;
                char16 Millisecond;

                public SystemTime(char16 year, char16 month, char16 dayOfWeek, char16 day,
                    char16 hour, char16 minute, char16 second, char16 millisecond) {
                    Year = year;
                    Month = month;
                    DayOfWeek = dayOfWeek;
                    Day = day;
                    Hour = hour;
                    Minute = minute;
                    Second = second;
                    Millisecond = millisecond;
                }
            }

            [Terrum.Interop.NativeImport("kernel32.dll", "SystemTimeToFileTime")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            [Terrum.Interop.NativeOutput(1)]
            extern int32 SystemTimeToFileTime(ptr<SystemTime> value, write ptr<int64> fileTime);

            int32 Main() {
                SystemTime value = new SystemTime(char16(2024), char16(1), char16(0), char16(2),
                    char16(3), char16(4), char16(5), char16(6));
                mut int64 fileTime = 0;
                unsafe {
                    if (SystemTimeToFileTime(&value, &fileTime) == 0) { return -1; }
                }
                if (fileTime <= 0) { return -2; }
                return 0;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        Assert.That(NativeJit.Execute(compilation.Program!, NativeRuntimePolicy.FailFast),
            Is.EqualTo(0));
    }

    [Test]
    public void DirectNativeImportAcceptsOpaquePointersAndWritableBlittableAggregates()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct NativeRectangle {
                public int32 Left;
                public int32 Top;
                public int32 Right;
                public int32 Bottom;

                public NativeRectangle(int32 left, int32 top, int32 right, int32 bottom) {
                    Left = left;
                    Top = top;
                    Right = right;
                    Bottom = bottom;
                }
            }

            [Terrum.Interop.NativeImport("example.dll", "UpdateRectangle")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(3, Write, NoEscape, 1)]
            [NoThrow]
            extern int32 UpdateRectangle(ptr<void> window,
                ptr<void> module, ptr<NativeRectangle> rectangle);

            unsafe int32 Invoke(ptr<void> window, ptr<void> module) {
                mut NativeRectangle rectangle = new NativeRectangle(1, 2, 3, 4);
                unsafe { return UpdateRectangle(window, module, &rectangle); }
            }

            int32 Main() { return 0; }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = compilation.Program!.Functions.Single(static candidate =>
            candidate.Name == "UpdateRectangle");
        Assert.That(function.RequiresUnsafeCaller, Is.True);
        Assert.That(function.Variables.Take(2).Select(static variable => variable.Type),
            Is.EqualTo([PrimitiveType.NativeRawPointer, PrimitiveType.NativeRawPointer]));
        Assert.That(function.DirectNativeImport!.Parameters, Is.EqualTo([
            new DirectNativeParameter(0, PrimitiveType.Void,
                NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 0),
            new DirectNativeParameter(1, PrimitiveType.Void,
                NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 0),
            new DirectNativeParameter(2, (PrimitiveType)100,
                NativeParameterAccess.Write, NativeParameterRetention.NoEscape, 1),
        ]));
        Assert.That(Encoding.ASCII.GetString(
            NativeEmitter.EmitWithNativeImports(compilation.Program, NativeRuntimePolicy.FailFast)),
            Does.Contain("UpdateRectangle"));
    }

    [Test]
    public void DirectNativeImportAcceptsLargeBlittableAggregateInput()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct NativePacket {
                public uint64 First;
                public uint64 Second;
                public uint64 Third;
                public uint64 Fourth;
                public uint64 Fifth;

                public NativePacket(uint64 first, uint64 second, uint64 third,
                    uint64 fourth, uint64 fifth) {
                    First = first;
                    Second = second;
                    Third = third;
                    Fourth = fourth;
                    Fifth = fifth;
                }
            }

            [Terrum.Interop.NativeImport("example.dll", "ConsumePacket")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            extern int32 ConsumePacket(ptr<NativePacket> packet);

            int32 Main() {
                NativePacket packet = new NativePacket(1, 2, 3, 4, 5);
                unsafe { return ConsumePacket(&packet); }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var structure = compilation.Program!.Structures.Single(static candidate =>
            candidate.Name == "NativePacket");
        Assert.That(structure.Size, Is.EqualTo(40));
        Assert.That(structure.Fields[^1].Type, Is.EqualTo(PrimitiveType.UInt64));
        Assert.That(structure.Fields[^1].Offset, Is.EqualTo(32));
        Assert.That(Encoding.ASCII.GetString(
            NativeEmitter.EmitWithNativeImports(compilation.Program, NativeRuntimePolicy.FailFast)),
            Does.Contain("ConsumePacket"));
    }

    [Test]
    public void DirectNativeImportTransportsOpaquePointerBitsButRejectsNonBlittableLocalAddress()
    {
        const string declarations = """
            struct OpaquePayload {
                public int32 Value;
                public OpaquePayload(int32 value) { Value = value; }
            }
            [Terrum.Interop.NativeImport("example.dll", "ConsumeOpaque")]
            extern int32 ConsumeOpaque(ptr<OpaquePayload> value);
            """;
        var transported = Compilation.Create(SyntaxTree.Parse(declarations + """
            unsafe int32 ForwardOpaque(ptr<OpaquePayload> value) {
                unsafe { return ConsumeOpaque(value); }
            }
            int32 Main() { return 0; }
            """));

        Assert.That(transported.HasErrors, Is.False,
            string.Join(Environment.NewLine, transported.Diagnostics));

        var exposed = Compilation.Create(SyntaxTree.Parse(declarations + """
            unsafe int32 ExposeOpaque() {
                OpaquePayload value = new OpaquePayload(1);
                unsafe { return ConsumeOpaque(&value); }
            }
            int32 Main() { return 0; }
            """));
        Assert.That(exposed.Program, Is.Null);
        Assert.That(exposed.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, exposed.Diagnostics));
    }

    [Test]
    public void UnsafeSourceFunctionsCanRelayOpaquePointersWithExplicitAuthorization()
    {
        var accepted = Compilation.CreateLibrary(SyntaxTree.Parse("""
            unsafe int32 Consume(ptr<void> window) { return 42; }
            unsafe int32 Forward(ptr<void> window) {
                unsafe { return Consume(window); }
            }
            """));
        Assert.That(accepted.HasErrors, Is.False,
            string.Join(Environment.NewLine, accepted.Diagnostics));

        var rejected = Compilation.CreateLibrary(SyntaxTree.Parse("""
            unsafe int32 Consume(ptr<void> window) { return 42; }
            int32 Forward() { return Consume(null); }
            """));
        Assert.That(rejected.HasErrors, Is.True);
        Assert.That(rejected.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3028"), Is.True);
    }

    [Test]
    public void DirectNativeWritableAggregateRequiresBlittabilityAndMutableInitializedBacking()
    {
        const string Import = """
            [Terrum.Interop.NativeImport("example.dll", "UpdateRectangle")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            extern int32 UpdateRectangle(ptr<NativeRectangle> rectangle);
            """;
        const string NonBlittable = """
            struct NativeRectangle { public int32 Left; }
            """;
        var unsupportedLayout = Compilation.CreateLibrary(SyntaxTree.Parse(
            NonBlittable + Import));
        Assert.That(unsupportedLayout.Program, Is.Null);
        Assert.That(unsupportedLayout.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True);

        var readonlyBacking = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct NativeRectangle {
                public int32 Left;
                public NativeRectangle(int32 left) { Left = left; }
            }
            [Terrum.Interop.NativeImport("example.dll", "UpdateRectangle")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            extern int32 UpdateRectangle(ptr<NativeRectangle> rectangle);
            int32 Main() {
                NativeRectangle rectangle = new NativeRectangle(1);
                unsafe { return UpdateRectangle(&rectangle); }
            }
            """));
        Assert.That(readonlyBacking.Program, Is.Null);
        Assert.That(readonlyBacking.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True);
    }

    [Test]
    public void OpaquePointerAndWritableAggregateImportSurvivePrivateModulesButRemainUnencodedInTrRm()
    {
        const string Source = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct NativeRectangle {
                public int32 Left;
                public int32 Top;
                public int32 Right;
                public int32 Bottom;
            }

            [Terrum.Interop.NativeImport("example.dll", "UpdateRectangle")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(3, Write, NoEscape, 1)]
            public extern int32 UpdateRectangle(ptr<void> window,
                ptr<void> module, ptr<NativeRectangle> rectangle);
            """;

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(Source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var artifact = ModuleCompiler.CreateProducer(program, ["UpdateRectangle"]);
        var reference = ModuleCompiler.CreateReference(artifact);
        var function = reference.Functions.Single(static candidate =>
            candidate.Name == "UpdateRectangle");
        Assert.That(function.Variables.Take(2).Select(static variable => variable.Type),
            Is.EqualTo([PrimitiveType.NativeRawPointer, PrimitiveType.NativeRawPointer]));
        Assert.That(function.DirectNativeImport!.Parameters, Is.EqualTo([
            new DirectNativeParameter(0, PrimitiveType.Void,
                NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 0),
            new DirectNativeParameter(1, PrimitiveType.Void,
                NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 0),
            new DirectNativeParameter(2, (PrimitiveType)100,
                NativeParameterAccess.Write, NativeParameterRetention.NoEscape, 1),
        ]));

        var executable = Compilation.Create(SyntaxTree.Parse(Source + "\nint32 Main() { return 0; }"));
        Assert.That(executable.HasErrors, Is.False,
            string.Join(Environment.NewLine, executable.Diagnostics));
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(executable.Program!, "unapproved-native-handle-write"));
    }

    [Test]
    public void SourceAuthoredImportRequiresUnsafeCallers()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ImportDeclaration + """
            int32 Main() {
                mut int64 counter = 0;
                return QueryPerformanceCounter(&counter);
            }
            """));

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3028"), Is.True);
    }

    [Test]
    public void NativeImportRejectsLegacyCatalogKeySyntax()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("test.catalog.key")]
            [NoThrow]
            extern unsafe uint32 ReadCounter();

            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void SourceAuthoredOutputRequiresMatchingWritableLocal()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ImportDeclaration + """
            int32 Main() {
                mut int32 counter = 0;
                unsafe { return QueryPerformanceCounter(&counter); }
            }
            """));

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True);
    }

    [Test]
    public void DirectNativePointerInputUsesOptionalNativeParameterMetadata()
    {
        const string Source = """
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            extern int32 ReadValue(ptr<int32> value);

            int32 Main() {
                int32 value = 42;
                unsafe { return ReadValue(&value); }
            }
            """;
        var declarationOnly = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            public extern int32 ReadValue(ptr<int32> value);
            """));
        Assert.That(declarationOnly.HasErrors, Is.False,
            string.Join(Environment.NewLine, declarationOnly.Diagnostics));
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var import = compilation.Program!.Functions.Single(static function =>
            function.Name == "ReadValue").DirectNativeImport!;
        Assert.That(import.Parameters, Has.Length.EqualTo(1));
        Assert.That(import.Parameters.Single(),
            Is.EqualTo(new DirectNativeParameter(0, PrimitiveType.Int32,
                NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 1)));
        Assert.That(Encoding.ASCII.GetString(
            NativeEmitter.EmitWithNativeImports(compilation.Program, NativeRuntimePolicy.FailFast)),
            Does.Contain("ReadValue"));

        var missingMetadata = Compilation.Create(SyntaxTree.Parse(Source.Replace(
            "[Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]",
            "", StringComparison.Ordinal)));
        Assert.That(missingMetadata.HasErrors, Is.False,
            string.Join(Environment.NewLine, missingMetadata.Diagnostics));
        var rawImport = missingMetadata.Program!.Functions.Single(static function =>
            function.Name == "ReadValue").DirectNativeImport!;
        Assert.That(rawImport.Parameters, Is.Empty);
        var unauthorizedWrite = Compilation.Create(SyntaxTree.Parse(Source.Replace(
            "Read, NoEscape", "Write, NoEscape", StringComparison.Ordinal)));
        Assert.That(unauthorizedWrite.Program, Is.Null);
        Assert.That(unauthorizedWrite.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True);
        var safeCaller = Compilation.Create(SyntaxTree.Parse(Source.Replace(
            "unsafe { return ReadValue(&value); }", "return ReadValue(&value);",
            StringComparison.Ordinal)));
        Assert.That(safeCaller.Program, Is.Null);
        Assert.That(safeCaller.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3028"), Is.True);
    }

    [Test]
    public void DirectNativeImportAcceptsUnboundedPointerInputsWithExplicitUnsafeBacking()
    {
        const string Source = """
            [Terrum.Interop.NativeImport("fixture.dll", "ReadSourceFixture")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(3, Read, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(4, Read, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(5, Read, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(9, Read, NoEscape, 0)]
            [Terrum.Interop.NativeOutput(7, NonNegative)]
            extern int32 ReadSourceFixture(
                ptr<char16> sourceFile,
                ptr<void> defines,
                ptr<void> include,
                ptr<void> entryPoint,
                ptr<void> target,
                uint32 flags1,
                uint32 flags2,
                write ptr<ptr<void>> code,
                ptr<void> diagnostics);

            int32 Main() {
                mut ptr<void> code = null;
                unsafe {
                    return ReadSourceFixture(utf16z("fixture.terrum"), null, null,
                        utf8z("VSMain"), utf8z("vs_5_0"), 0, 0, &code, null);
                }
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var import = compilation.Program!.Functions.Single(static function =>
            function.Name == "ReadSourceFixture").DirectNativeImport!;
        Assert.That(import.Parameters, Is.EqualTo([
            new DirectNativeParameter(0, PrimitiveType.Char16,
                NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 0),
            new DirectNativeParameter(1, PrimitiveType.Void,
                NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 0),
            new DirectNativeParameter(2, PrimitiveType.Void,
                NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 0),
            new DirectNativeParameter(3, PrimitiveType.Void,
                NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 0),
            new DirectNativeParameter(4, PrimitiveType.Void,
                NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 0),
            new DirectNativeParameter(8, PrimitiveType.Void,
                NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 0),
        ]));
        Assert.That(Encoding.ASCII.GetString(NativeEmitter.EmitWithNativeImports(
            compilation.Program, NativeRuntimePolicy.FailFast)), Does.Contain("ReadSourceFixture"));

        var wrongElementType = Compilation.Create(SyntaxTree.Parse(Source.Replace(
            "ptr<char16> sourceFile", "ptr<uint32> sourceFile", StringComparison.Ordinal)));
        Assert.That(wrongElementType.Program, Is.Null);
        Assert.That(wrongElementType.HasErrors, Is.True);

        var safeCaller = Compilation.Create(SyntaxTree.Parse(Source.Replace(
            "unsafe {", "{", StringComparison.Ordinal)));
        Assert.That(safeCaller.Program, Is.Null);
        Assert.That(safeCaller.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3028"), Is.True);
    }

    [Test]
    public void TrRmNativeImportRoundTripsUnboundedCharacterPointerInput()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("fixture.dll", "ReadUtf16Path")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            public extern int32 ReadUtf16Path(ptr<char16> path);
            """));
        Assert.That(provider.HasErrors, Is.False,
            string.Join(Environment.NewLine, provider.Diagnostics));

        var artifact = TrRmExecutableCompiler.Emit(provider.Program!, "character-pointer-input");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        var import = loaded.Functions.Single(static function =>
            function.Name == "ReadUtf16Path").DirectNativeImport!;
        Assert.That(import.Parameters, Is.EqualTo([
            new DirectNativeParameter(0, PrimitiveType.Char16,
                NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 0),
        ]));
    }

    [Test]
    public void UnsafeWrapperCanCallDirectImportWithStringLiteralPointer()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("fixture.dll", "ReadUtf16Path")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            extern int32 ReadUtf16Path(ptr<char16> path);

            unsafe int32 ReadThroughWrapper() {
                unsafe { return ReadUtf16Path(utf16z("fixture.terrum")); }
            }

            int32 Main() {
                unsafe { return ReadThroughWrapper(); }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(Encoding.ASCII.GetString(NativeEmitter.EmitWithNativeImports(
            compilation.Program!, NativeRuntimePolicy.FailFast)), Does.Contain("ReadUtf16Path"));
    }

    [Test]
    public void DirectNativePointerInputSurvivesPrivateSemanticMetadataAndIsRevalidated()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 3)]
            public extern int32 ReadValue(ptr<int32> value);
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = ModuleCompiler.CreateProducer(compilation.Program!, ["ReadValue"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            artifact.AsSpan(sizeof(int), sizeof(int))), Is.EqualTo(80));
        var reference = ModuleCompiler.CreateReference(artifact);
        var import = reference.Functions.Single(static function =>
            function.Name == "ReadValue").DirectNativeImport!;
        Assert.That(import.Parameters, Is.EqualTo([
            new DirectNativeParameter(0, PrimitiveType.Int32,
                NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 3),
        ]));

        var malformed = artifact.ToArray();
        var library = Encoding.UTF8.GetBytes("example.dll");
        var libraryOffset = IndexOf(malformed, library);
        Assert.That(libraryOffset, Is.GreaterThanOrEqualTo(sizeof(int)));
        var exportLengthOffset = libraryOffset + library.Length;
        var exportLength = BinaryPrimitives.ReadInt32LittleEndian(
            malformed.AsSpan(exportLengthOffset, sizeof(int)));
        var outputCountOffset = exportLengthOffset + sizeof(int) + exportLength;
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            malformed.AsSpan(outputCountOffset, sizeof(int))), Is.Zero);
        var parameterCountOffset = outputCountOffset + sizeof(int);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            malformed.AsSpan(parameterCountOffset, sizeof(int))), Is.EqualTo(1));
        var parameterOrdinalOffset = parameterCountOffset + sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(
            malformed.AsSpan(parameterOrdinalOffset, sizeof(int)), 1);

        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed));
    }

    [TestCase(2, false)]
    [TestCase(3, true)]
    [TestCase(5, true)]
    public void DirectNativeParameterExtentRequiresAtLeastTheDeclaredArrayCapacity(
        int backingCount, bool shouldCompile)
    {
        var values = string.Join(", ", Enumerable.Range(1, backingCount));
        var source = $$"""
            [Terrum.Interop.NativeImport("fixture.dll", "ObserveValues")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 3)]
            extern int32 ObserveValues(ptr<uint32> values);

            int32 Main() {
                InlineArray<uint32, {{backingCount}}> values = [{{values}}];
                unsafe { return ObserveValues(&values[0]); }
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.EqualTo(!shouldCompile),
            string.Join(Environment.NewLine, compilation.Diagnostics));
        if (!shouldCompile)
        {
            Assert.That(compilation.Program, Is.Null);
            Assert.That(compilation.Diagnostics, Is.Not.Empty);
            return;
        }

        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");
        var import = program.Functions.Single(static function =>
            function.Name == "ObserveValues").DirectNativeImport!;
        Assert.That(import.Parameters.Single().Extent, Is.EqualTo(3));
        Assert.That(Encoding.ASCII.GetString(
            NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast)),
            Does.Contain("ObserveValues"));
    }

    [Test]
    public void BlittableAggregateNativeParameterSurvivesTypeRemappingAndIsRevalidated()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct UnusedPrefix { public int32 Value; }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct ClockFields {
                public char16 Year;
                public char16 Month;
                public char16 DayOfWeek;
                public char16 Day;
                public char16 Hour;
                public char16 Minute;
                public char16 Second;
                public char16 Millisecond;
            }

            [Terrum.Interop.NativeImport("example.dll", "ReadClock")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            public extern int32 ReadClock(ptr<ClockFields> value);
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = ModuleCompiler.CreateProducer(compilation.Program!, ["ReadClock"]);
        var reference = ModuleCompiler.CreateReference(artifact);
        Assert.That(reference.Structures.Select(static structure => structure.Name),
            Is.EqualTo(["ClockFields"]));
        var import = reference.Functions.Single(static function =>
            function.Name == "ReadClock").DirectNativeImport!;
        Assert.That(import.Parameters, Is.EqualTo([
            new DirectNativeParameter(0, (PrimitiveType)100,
                NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 1),
        ]));

        var malformed = artifact.ToArray();
        var library = Encoding.UTF8.GetBytes("example.dll");
        var libraryOffset = IndexOf(malformed, library);
        var exportLengthOffset = libraryOffset + library.Length;
        var exportLength = BinaryPrimitives.ReadInt32LittleEndian(
            malformed.AsSpan(exportLengthOffset, sizeof(int)));
        var outputCountOffset = exportLengthOffset + sizeof(int) + exportLength;
        var parameterCountOffset = outputCountOffset + sizeof(int);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            malformed.AsSpan(parameterCountOffset, sizeof(int))), Is.EqualTo(1));
        var parameterTypeOffset = parameterCountOffset + (2 * sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(
            malformed.AsSpan(parameterTypeOffset, sizeof(int)), int.MaxValue);

        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed));
    }

    [Test]
    public void TrRmNativeImportRoundTripsNativeParameterPointerInput()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            public extern int32 ReadValue(ptr<int32> value);
            """));
        Assert.That(provider.HasErrors, Is.False,
            string.Join(Environment.NewLine, provider.Diagnostics));

        var artifact = TrRmExecutableCompiler.Emit(provider.Program!, "pointer-input");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        var import = loaded.Functions.Single(static function =>
            function.Name == "ReadValue").DirectNativeImport!;
        Assert.That(import.Parameters,
            Is.EqualTo(new[]
            {
                new DirectNativeParameter(0, PrimitiveType.Int32,
                    NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 1),
            }));

        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x0004, out var featureBytes, out var featureCount), Is.True);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount);
        Assert.That(featureCount, Is.EqualTo(1));
        Assert.That(features.GetFeature(1).FeatureId,
            Is.EqualTo(TrRmFeatureTableReader.WindowsNamedNativeImportsFeatureId));

        var reference = TrRmExecutableCompiler.CreateReference(artifact);
        var consumer = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int32 value = 42;
                unsafe { return ReadValue(&value); }
            }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        var image = NativeEmitter.EmitWithNativeImports(
            consumer.Program!, NativeRuntimePolicy.FailFast);
        Assert.That(Encoding.ASCII.GetString(image), Does.Contain("ReadValue"));
        Assert.That(Encoding.ASCII.GetString(image), Does.Contain("example.dll"));
    }

    [Test]
    public void TrRmNativeImportRoundTripsBlittableAggregatePointerDependency()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct TransportIdentity {
                public uint32 Source;
                public uint32 Destination;
            }

            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct TransportHeader {
                public TransportIdentity Identity;
                public uint64 Sequence;
            }

            [Terrum.Interop.NativeImport("fixture.dll", "InspectHeader")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            public extern int32 InspectHeader(ptr<TransportHeader> header);
            """));
        Assert.That(provider.HasErrors, Is.False,
            string.Join(Environment.NewLine, provider.Diagnostics));

        var artifact = TrRmExecutableCompiler.Emit(provider.Program!, "transport-header");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        var loadedStructure = loaded.Structures.Single(static structure =>
            structure.Name == "TransportHeader");
        var loadedStructureIndex = loaded.Structures.IndexOf(loadedStructure);
        Assert.That(loadedStructure.Fields.Select(static field => field.Name),
            Is.EqualTo(["Identity", "Sequence"]));
        Assert.That(loadedStructure.Fields.Select(static field => field.Offset),
            Is.EqualTo([0, 8]));
        Assert.That(loaded.Structures.Single(static structure =>
            structure.Name == "TransportIdentity").Fields.Select(static field => field.Offset),
            Is.EqualTo([0, 4]));
        var loadedImport = loaded.Functions.Single(static function =>
            function.Name == "InspectHeader").DirectNativeImport!;
        Assert.That(loadedImport.Parameters.Single().PointeeType,
            Is.EqualTo((PrimitiveType)(100 + loadedStructureIndex)));

        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x0004, out var featureBytes, out var featureCount), Is.True);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount);
        Assert.That(features.TryGetFeature(TrRmFeatureTableReader.NativeBlittableLayoutsFeatureId,
            out var blittableFeature), Is.True);
        Assert.That(blittableFeature.MinimumRuleRevision,
            Is.EqualTo(TrRmFeatureTableReader.NativeBlittableLayoutsRuleRevision));

        var reference = TrRmExecutableCompiler.CreateReference(artifact);
        Assert.That(reference.Structures.Select(static structure => structure.Name),
            Is.EqualTo(["TransportIdentity", "TransportHeader"]));
        var consumer = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe { return InspectHeader(null); }
            }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        var image = NativeEmitter.EmitWithNativeImports(
            consumer.Program!, NativeRuntimePolicy.FailFast);
        Assert.That(Encoding.ASCII.GetString(image), Does.Contain("InspectHeader"));
        Assert.That(Encoding.ASCII.GetString(image), Does.Contain("fixture.dll"));

        var wrongTarget = MutateVariableRowByte(artifact, 0x001B, 1, 8, 2);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(wrongTarget));
        var missingFeature = MutateFixedSectionByte(artifact, 0x0004, 8, 3);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(missingFeature));
        var wrongAlignment = MutateFixedRowByte(artifact, 0x0015, 2, 24, 12, 4);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(wrongAlignment));
    }

    [Test]
    public void TrRmNativeImportRoundTripsAndBoundsLargeNativeParameterExtents()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "ObserveValues")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 3)]
            public extern int32 ObserveValues(ptr<uint32> values);
            """));
        Assert.That(provider.HasErrors, Is.False,
            string.Join(Environment.NewLine, provider.Diagnostics));

        var artifact = TrRmExecutableCompiler.Emit(provider.Program!, "native-parameter-extent");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        var import = loaded.Functions.Single(static function =>
            function.Name == "ObserveValues").DirectNativeImport!;
        Assert.That(import.Parameters.Single().Extent, Is.EqualTo(3));

        var malformed = MutateVariableRowByte(artifact, 0x001B, 2, 19, 0x80);
        Assert.Throws<InvalidDataException>(() => TrRmStructuralMetadataValidator.Validate(malformed));
    }

    [Test]
    public void TrRmNativeImportRoundTripsNestedNativeParameterPointerInput()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "WritePointer")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            public extern int32 WritePointer(ptr<ptr<void>> value);
            """));
        Assert.That(provider.HasErrors, Is.False,
            string.Join(Environment.NewLine, provider.Diagnostics));

        var artifact = TrRmExecutableCompiler.Emit(provider.Program!, "nested-pointer-input");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        var import = loaded.Functions.Single(static function =>
            function.Name == "WritePointer").DirectNativeImport!;
        Assert.That(import.Parameters,
            Is.EqualTo(new[]
            {
                new DirectNativeParameter(0, PrimitiveType.NativeRawPointer,
                    NativeParameterAccess.Write, NativeParameterRetention.NoEscape, 1),
            }));

        var reference = TrRmExecutableCompiler.CreateReference(artifact);
        var consumer = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut ptr<void> result = null;
                unsafe { return WritePointer(&result); }
            }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        var image = NativeEmitter.EmitWithNativeImports(
            consumer.Program!, NativeRuntimePolicy.FailFast);
        Assert.That(Encoding.ASCII.GetString(image), Does.Contain("WritePointer"));
    }

    [Test]
    public void TrRmNativeImportRoundTripsVoidPointeeNativeParameter()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "InspectPointer")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            public extern int32 InspectPointer(ptr<void> value);
            """));
        Assert.That(provider.HasErrors, Is.False,
            string.Join(Environment.NewLine, provider.Diagnostics));

        var artifact = TrRmExecutableCompiler.Emit(provider.Program!, "void-pointer-input");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        var import = loaded.Functions.Single(static function =>
            function.Name == "InspectPointer").DirectNativeImport!;
        Assert.That(import.Parameters,
            Is.EqualTo(new[]
            {
                new DirectNativeParameter(0, PrimitiveType.Void,
                    NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 0),
            }));
    }

    [Test]
    public void TrRmNativeImportRejectsMalformedNativeParameterContract()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            public extern int32 ReadValue(ptr<int32> value);
            """));
        Assert.That(provider.HasErrors, Is.False,
            string.Join(Environment.NewLine, provider.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(provider.Program!, "pointer-input");
        var malformed = MutateVariableRowByte(artifact, 0x001B, 2, 12, 0);

        Assert.Throws<InvalidDataException>(() => TrRmStructuralMetadataValidator.Validate(malformed));
    }

    [Test]
    public void SignedNonnegativeNativeOutputConditionRoundTripsThroughTrRm()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "AcquireInterface")]
            [Terrum.Interop.NativeOutput(0, NonNegative)]
            public extern int32 AcquireInterface(write ptr<ptr<void>> result);
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.Emit(compilation.Program!, "signed-native-output");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        var output = loaded.Functions.Single(static function =>
            function.Name == "AcquireInterface").DirectNativeImport!.Outputs.Single();

        Assert.That(output.ResultCondition, Is.EqualTo(3u));

        var invalid = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "AcquireInterface")]
            [Terrum.Interop.NativeOutput(0, NonNegative)]
            public extern uint32 AcquireInterface(write ptr<ptr<void>> result);
            """));
        Assert.That(invalid.Program, Is.Null);
        Assert.That(invalid.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True);
    }

    [Test]
    public void SignedNonnegativeNativeOutputSurvivesPrivateNativeDependencies()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "AcquireInterface")]
            [Terrum.Interop.NativeOutput(0, NonNegative)]
            extern int32 AcquireInterface(write ptr<ptr<void>> result);

            public int32 AcquireAndReturn() {
                mut ptr<void> result = null;
                mut int32 status = 0;
                unsafe { status = AcquireInterface(&result); }
                if (status < 0) { return -1; }
                return 0;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var semanticProducer = ModuleCompiler.CreateProducer(program, ["AcquireAndReturn"]);
        var reference = ModuleCompiler.CreateReference(semanticProducer);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return AcquireAndReturn(); }"), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var nativeProducer = NativeModuleCompiler.CreateProducer(program,
            ["AcquireAndReturn"], "SignedOutput", NativeRuntimePolicy.FailFast);
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(consumerCompilation.Program!,
            nativeProducer, NativeRuntimePolicy.FailFast);

        NativeModuleCompiler.Validate(nativeConsumer, nativeProducer);
    }

    [Test]
    public void WritableNativeOutputSurvivesPrivateSemanticAndNativeModuleDependencies()
    {
        var source = """
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            [Terrum.Interop.NativeOutput(0)]
            extern int32 ReadValue(write ptr<int32> value);
            public int32 ReadAndReturn() {
                mut int32 value = 0;
                unsafe {
                    if (ReadValue(&value) == 0) { return -1; }
                }
                return value;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var semanticProducer = ModuleCompiler.CreateProducer(program, ["ReadAndReturn"]);
        var reference = ModuleCompiler.CreateReference(semanticProducer);
        Assert.That(reference.Functions.Select(static function => function.Name),
            Is.EqualTo(["ReadAndReturn"]));

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() { return ReadAndReturn(); }
            """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var nativeProducer = NativeModuleCompiler.CreateProducer(program, ["ReadAndReturn"],
            "DirectOutput", NativeRuntimePolicy.FailFast);
        var info = NativeModuleCompiler.Inspect(nativeProducer);
        Assert.That(info.IsConsumer, Is.False);
        var packageText = Encoding.ASCII.GetString(nativeProducer);
        Assert.That(packageText, Does.Contain("example.dll"));
        Assert.That(packageText, Does.Contain("ReadValue"));
        var nativeConsumer = NativeModuleCompiler.CreateConsumer(consumerCompilation.Program!,
            nativeProducer, NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.Validate(nativeConsumer, nativeProducer);
    }

    [Test]
    public void DirectNativeOutputCanBeCombinedWithOneNativeProducerDependency()
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 ProviderValue() { return 40; }"));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(providerCompilation.Program!,
            ["ProviderValue"], "ProviderValue", NativeRuntimePolicy.FailFast);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            [Terrum.Interop.NativeOutput(0)]
            public extern int32 ReadValue(write ptr<int32> value);

            int32 Main() {
                mut int32 value = 0;
                unsafe {
                    if (ReadValue(&value) == 0) { return ProviderValue() + value; }
                }
                return -1;
            }
            """), NativeModuleCompiler.CreateReference(provider));
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));

        var consumer = NativeModuleCompiler.CreateConsumer(consumerCompilation.Program!, provider,
            NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.Validate(consumer, provider);

        var packageText = Encoding.ASCII.GetString(consumer);
        Assert.That(packageText, Does.Contain("example.dll"));
        Assert.That(packageText, Does.Contain("ProviderValue.dll"));
        Assert.That(packageText, Does.Contain("ReadValue"));
    }

    [Test]
    public void DirectNativePointerInputCanBeCombinedWithOneNativeProducerDependency()
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 ProviderValue() { return 40; }"));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = NativeModuleCompiler.CreateProducer(providerCompilation.Program!,
            ["ProviderValue"], "ProviderValue", NativeRuntimePolicy.FailFast);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            public extern int32 ReadValue(ptr<int32> value);

            int32 Main() {
                mut int32 value = 1;
                mut int32 status = 0;
                unsafe { status = ReadValue(&value); }
                if (status == 0) { return -1; }
                return ProviderValue() + value;
            }
            """), NativeModuleCompiler.CreateReference(provider));
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));

        var consumer = NativeModuleCompiler.CreateConsumer(consumerCompilation.Program!, provider,
            NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.Validate(consumer, provider);

        var packageText = Encoding.ASCII.GetString(consumer);
        Assert.That(packageText, Does.Contain("example.dll"));
        Assert.That(packageText, Does.Contain("ProviderValue.dll"));
        Assert.That(packageText, Does.Contain("ReadValue"));
    }

    [Test]
    public void PrivateSemanticModuleRevalidatesNativeOutputMetadata()
    {
        var source = """
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            [Terrum.Interop.NativeOutput(0)]
            extern int32 ReadValue(write ptr<int32> value);
            public int32 ReadAndReturn() {
                mut int32 value = 0;
                unsafe {
                    if (ReadValue(&value) == 0) { return -1; }
                }
                return value;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = ModuleCompiler.CreateProducer(compilation.Program!, ["ReadAndReturn"]);
        var malformed = artifact.ToArray();
        var library = Encoding.UTF8.GetBytes("example.dll");
        var libraryOffset = IndexOf(malformed, library);
        Assert.That(libraryOffset, Is.GreaterThanOrEqualTo(sizeof(int)));
        var exportLengthOffset = libraryOffset + library.Length;
        var exportLength = BinaryPrimitives.ReadInt32LittleEndian(
            malformed.AsSpan(exportLengthOffset, sizeof(int)));
        var outputCountOffset = exportLengthOffset + sizeof(int) + exportLength;
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            malformed.AsSpan(outputCountOffset, sizeof(int))), Is.EqualTo(1));
        var resultConditionOffset = outputCountOffset + sizeof(int) + sizeof(int) + sizeof(int);
        BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(resultConditionOffset, sizeof(uint)), 2);

        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(malformed));
    }

    [Test]
    public void DirectWritablePointerParameterDoesNotInventOutputValidity()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            extern int32 ReadValue(write ptr<int32> value);
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");
        var function = program.Functions.Single(static candidate => candidate.Name == "ReadValue");
        Assert.That(function.RequiresUnsafeCaller, Is.True);
        Assert.That(function.DirectNativeImport!.Outputs, Is.Empty);
        Assert.That(program.NativePointerTypes, Has.Length.EqualTo(1));
        Assert.That(program.NativePointerTypes[0].PointeeType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(program.NativePointerTypes[0].IsWritable, Is.True);
    }

    [Test]
    public void SourceAuthoredNestedPointerOutputUsesAnOpaquePointerValue()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(OpaquePointerImportDeclaration + """
            int32 Main() {
                mut ptr<void> result = null;
                unsafe {
                    if (AcquireInterface(&result) == 0) { return -1; }
                }
                return 0;
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var image = NativeEmitter.EmitWithNativeImports(
            compilation.Program!, NativeRuntimePolicy.FailFast);
        Assert.That(Encoding.ASCII.GetString(image), Does.Contain("AcquireInterface"));
    }

    [Test]
    public void TrRmNativeImportRoundTripsNestedPointerOutput()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse(OpaquePointerImportDeclaration));
        Assert.That(provider.HasErrors, Is.False,
            string.Join(Environment.NewLine, provider.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(provider.Program!, "opaque-interface-import");

        var reference = TrRmExecutableCompiler.CreateReference(artifact);
        var consumer = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut ptr<void> result = null;
                unsafe {
                    if (AcquireInterface(&result) == 0) { return -1; }
                }
                return 0;
            }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        var image = NativeEmitter.EmitWithNativeImports(
            consumer.Program!, NativeRuntimePolicy.FailFast);
        Assert.That(Encoding.ASCII.GetString(image), Does.Contain("AcquireInterface"));
    }

    [Test]
    public void TrRmNativeImportRoundTripsIntoAnUnsafeSourceConsumer()
    {
        var artifact = EmitPublicImportArtifact();
        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x0004, out var featureBytes, out var featureCount), Is.True);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount);
        Assert.That(features.GetFeature(1),
            Is.EqualTo(new TrRmFeatureRow(
                TrRmFeatureTableReader.WindowsNamedNativeImportsFeatureId,
                TrRmFeatureTableReader.WindowsNamedNativeImportsRuleRevision)));
        Assert.That(index.TryGetSection(0x0021, out _, out var nativeImportCount), Is.True);
        Assert.That(nativeImportCount, Is.EqualTo(1));

        var reference = TrRmExecutableCompiler.CreateReference(artifact);
        var consumer = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int64 counter = 0;
                unsafe {
                    if (QueryPerformanceCounter(&counter) == 0) { return 1; }
                }
                return 0;
            }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        var image = NativeEmitter.EmitWithNativeImports(
            consumer.Program!, NativeRuntimePolicy.FailFast);
        Assert.That(Encoding.ASCII.GetString(image), Does.Contain("QueryPerformanceCounter"));
        Assert.That(Encoding.ASCII.GetString(image), Does.Contain("KERNEL32.dll"));
    }

    [Test]
    public void TrRmNativeImportRejectsMissingUnsafeEffect()
    {
        var artifact = EmitPublicImportArtifact();
        var malformed = MutateVariableRowByte(artifact, 0x001B, 1, 8, 0);

        Assert.Throws<InvalidDataException>(() => TrRmStructuralMetadataValidator.Validate(malformed));
    }

    [Test]
    public void TrRmNativeImportRejectsReadonlyOutputPointer()
    {
        var artifact = EmitPublicImportArtifact();
        var malformed = RemoveNativeOutputWriteQualifier(artifact);

        Assert.Throws<InvalidDataException>(() => TrRmStructuralMetadataValidator.Validate(malformed));
    }

    [Test]
    public void TrRmNativeImportRejectsMissingRequiredFeature()
    {
        var artifact = EmitPublicImportArtifact();
        var malformed = MutateFixedSectionByte(artifact, 0x0004, 0, 1);

        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
    }

    private static byte[] EmitPublicImportArtifact()
    {
        var provider = Compilation.CreateLibrary(SyntaxTree.Parse(PublicImportDeclaration));
        Assert.That(provider.HasErrors, Is.False,
            string.Join(Environment.NewLine, provider.Diagnostics));
        return TrRmExecutableCompiler.Emit(provider.Program!, "windows-clock");
    }

    private static int IndexOf(byte[] source, byte[] value)
    {
        for (var index = 0; index <= source.Length - value.Length; index++)
        {
            if (source.AsSpan(index, value.Length).SequenceEqual(value))
            {
                return index;
            }
        }
        return -1;
    }

    private static byte[] MutateFixedSectionByte(byte[] artifact, ushort sectionKind,
        int byteOffset, byte value)
    {
        var (sectionOffset, count) = FindSection(artifact, sectionKind);
        var result = artifact.ToArray();
        if ((uint)byteOffset >= (uint)count * 8)
        {
            throw new ArgumentOutOfRangeException(nameof(byteOffset));
        }
        result[sectionOffset + byteOffset] = value;
        return result;
    }

    private static byte[] MutateFixedRowByte(byte[] artifact, ushort sectionKind,
        int row, int rowSize, int byteOffset, byte value)
    {
        var (sectionOffset, count) = FindSection(artifact, sectionKind);
        if (row <= 0 || row > count || rowSize <= 0
            || byteOffset < 0 || byteOffset >= rowSize)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        var result = artifact.ToArray();
        result[sectionOffset + ((row - 1) * rowSize) + byteOffset] = value;
        return result;
    }

    private static byte[] MutateVariableRowByte(byte[] artifact, ushort sectionKind,
        int row, int byteOffset, byte value)
    {
        var (sectionOffset, count) = FindSection(artifact, sectionKind);
        if (row <= 0 || row > count)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        var offsetTableBytes = checked((count + 1) * sizeof(uint));
        var first = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            artifact.AsSpan(sectionOffset + (row - 1) * sizeof(uint), sizeof(uint))));
        var last = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            artifact.AsSpan(sectionOffset + row * sizeof(uint), sizeof(uint))));
        if ((uint)byteOffset >= (uint)(last - first))
        {
            throw new ArgumentOutOfRangeException(nameof(byteOffset));
        }

        var result = artifact.ToArray();
        result[sectionOffset + offsetTableBytes + first + byteOffset] = value;
        return result;
    }

    private static int FindVariableRow(byte[] artifact, ushort sectionKind, byte[] expected)
    {
        var (sectionOffset, count) = FindSection(artifact, sectionKind);
        var offsetTableBytes = checked((count + 1) * sizeof(uint));
        for (var row = 1; row <= count; row++)
        {
            var first = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                artifact.AsSpan(sectionOffset + (row - 1) * sizeof(uint), sizeof(uint))));
            var last = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                artifact.AsSpan(sectionOffset + row * sizeof(uint), sizeof(uint))));
            var rowBytes = artifact.AsSpan(sectionOffset + offsetTableBytes + first, last - first);
            if (rowBytes.SequenceEqual(expected))
            {
                return row;
            }
        }

        throw new InvalidOperationException("The emitted artifact has no matching metadata row.");
    }

    private static byte[] RemoveNativeOutputWriteQualifier(byte[] artifact)
    {
        var (sectionOffset, count) = FindSection(artifact, 0x0011);
        var offsetTableBytes = checked((count + 1) * sizeof(uint));
        for (var row = 1; row <= count; row++)
        {
            var first = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                artifact.AsSpan(sectionOffset + (row - 1) * sizeof(uint), sizeof(uint))));
            var last = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                artifact.AsSpan(sectionOffset + row * sizeof(uint), sizeof(uint))));
            var typeOffset = sectionOffset + offsetTableBytes + first;
            if (last - first == 4 && artifact[typeOffset] == 1
                && artifact[typeOffset + 1] == 1 && artifact[typeOffset + 2] == 0x8C)
            {
                var result = artifact.ToArray();
                result[typeOffset + 1] = 0;
                return result;
            }
        }

        throw new InvalidOperationException("The emitted artifact has no writable native output pointer.");
    }

    private static (int Offset, int Count) FindSection(byte[] artifact, ushort sectionKind)
    {
        var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(12, sizeof(uint))));
        for (var entry = 0; entry < count; entry++)
        {
            var directoryOffset = 32 + entry * 16;
            if (BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(directoryOffset, sizeof(ushort)))
                == sectionKind)
            {
                return (
                    checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                        artifact.AsSpan(directoryOffset + 8, sizeof(uint)))),
                    checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                        artifact.AsSpan(directoryOffset + 4, sizeof(uint)))));
            }
        }

        throw new InvalidOperationException($"The emitted artifact has no section 0x{sectionKind:X4}.");
    }
}
