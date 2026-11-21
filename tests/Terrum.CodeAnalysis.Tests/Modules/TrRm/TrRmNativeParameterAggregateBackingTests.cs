// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

internal sealed class TrRmNativeParameterAggregateBackingTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePair
    {
        public int First;
        public int Second;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeArrayHolder
    {
        public int First;
        public int Second;
        public int Third;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeNestedArrayHolder
    {
        public int First;
        public int Second;
        public int Third;
        public int Fourth;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ProbeDelegate(nint process, NativePair value,
        nuint bytesToRead, nint bytesRead);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ArrayProbeDelegate(nint process,
        nuint bytesToRead, nint bytesRead);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int WideIndexProbeDelegate(nint process, ulong index,
        nuint bytesToRead, nint bytesRead);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ComputedIndexProbeDelegate(nint process,
        int firstIndex, int secondIndex, nuint bytesToRead, nint bytesRead);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ArrayUpdateProbeDelegate(nint process,
        int initialIndex, nuint bytesToRead, nint bytesRead);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int NativeParameterArrayProbeDelegate(ulong byteCount);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int FieldArrayProbeDelegate(nint process,
        NativeArrayHolder value, nuint bytesToRead, nint bytesRead);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int NestedFieldArrayProbeDelegate(nint process,
        NativeNestedArrayHolder value, int outerIndex, int innerIndex,
        nuint bytesToRead, nint bytesRead);

    [Test]
    public async Task StandardNativeOutputUsesAggregateNativeParameterBackingAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-addresses-1";
        const string moduleIdentity = "native-output-aggregate-backing";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Pair {
                public int32 First;
                public int32 Second;
            }
            [param(buffer): DependsOn(nameof(baseAddress))]
            [Terrum.Interop.NativeImport("kernel32.dll",
                "ReadProcessMemory")]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(5, Write, NoEscape, 1)]
            extern unsafe int32 ReadProcessMemory(
                ptr<void> process, ptr<Pair> baseAddress,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<Pair> buffer, nuint bytesToRead,
                write ptr<nuint> bytesRead);

            public unsafe int32 Probe(ptr<void> process, Pair value,
                nuint bytesToRead, write ptr<nuint> bytesRead) {
                mut Pair backing = value;
                init mut Pair buffer;
                unsafe {
                    return ReadProcessMemory(process, &backing,
                        &init buffer, bytesToRead, bytesRead);
                }
            }
            """), NativeOutputCoreRegistrationTests
                .CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity,
            moduleTargetRequirement: 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity + "-reemitted", schemaIdentity,
            moduleTargetRequirement: 1);
        var reemittedIdentity = moduleIdentity + "-reemitted";
        var reemittedInput = new TrRmStandardArtifactInput(
            schemaIdentity, reemitted);
        _ = TrRmExecutableCompiler.LoadStandard(reemittedInput,
            reemittedIdentity, []);
        var image = NativeModuleCompiler.EmitStandardLibrary(
            reemittedInput, reemittedIdentity, [], ["Probe"],
            NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-nativeparameter-aggregate-backing-");
        try
        {
            var imagePath = Path.Combine(directory, "library.dll");
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            var library = NativeLibrary.Load(imagePath);
            nint bytesRead = 0;
            try
            {
                bytesRead = Marshal.AllocHGlobal(sizeof(long));
                Marshal.WriteInt64(bytesRead, 0);
                var function = Marshal.GetDelegateForFunctionPointer<
                    ProbeDelegate>(NativeLibrary.GetExport(library, "Probe"));
                Assert.That(function(new IntPtr(-1), new NativePair {
                    First = 11,
                    Second = 22,
                }, (nuint)Marshal.SizeOf<NativePair>(), bytesRead),
                    Is.EqualTo(1));
                Assert.That(Marshal.ReadInt64(bytesRead), Is.EqualTo(8));
            }
            finally
            {
                if (bytesRead != 0)
                {
                    Marshal.FreeHGlobal(bytesRead);
                }
                NativeLibrary.Free(library);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public void StandardNativeOutputReemits16ByteAggregateNativeParameterBacking()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-addresses-1";
        const string moduleIdentity = "native-output-aggregate-backing-16";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Quad {
                public int32 First;
                public int32 Second;
                public int32 Third;
                public int32 Fourth;
            }
            [param(buffer): DependsOn(nameof(baseAddress))]
            [Terrum.Interop.NativeImport("kernel32.dll",
                "ReadProcessMemory")]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(5, Write, NoEscape, 1)]
            extern unsafe int32 ReadProcessMemory(
                ptr<void> process, ptr<Quad> baseAddress,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<Quad> buffer, nuint bytesToRead,
                write ptr<nuint> bytesRead);

            public unsafe int32 Probe(ptr<void> process, Quad value,
                nuint bytesToRead, write ptr<nuint> bytesRead) {
                mut Quad backing = value;
                init mut Quad buffer;
                unsafe {
                    return ReadProcessMemory(process, &backing,
                        &init buffer, bytesToRead, bytesRead);
                }
            }
            """), NativeOutputCoreRegistrationTests
                .CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(program.Structures.Single(
            static structure => structure.Name == "Quad").Size,
            Is.EqualTo(16));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            program, moduleIdentity, schemaIdentity,
            moduleTargetRequirement: 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var caller = loaded.Functions.Single(static function =>
            function.Name == "Probe");
        BoundNativeStorageAddress? aggregateBacking = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundNativeStorageAddress address)
                {
                    aggregateBacking = address;
                }
            }), Is.True);
        Assert.That(aggregateBacking, Is.Not.Null);
        Assert.That(aggregateBacking!.ElementCount, Is.EqualTo(1UL));
        Assert.That(aggregateBacking.ElementStride, Is.EqualTo(16UL));
        Assert.That(aggregateBacking.ByteExtent, Is.EqualTo(16UL));
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, moduleTargetRequirement: 1);
        Assert.That(reemitted, Is.EqualTo(artifact));
        var reemittedInput = new TrRmStandardArtifactInput(schemaIdentity,
            reemitted);
        _ = TrRmExecutableCompiler.LoadStandard(reemittedInput,
            moduleIdentity, []);
        Assert.That(NativeModuleCompiler.EmitStandardLibrary(
                reemittedInput, moduleIdentity, [], ["Probe"],
                NativeRuntimePolicy.FailFast),
            Is.Not.Empty);
    }

    [Test]
    [TestCase(3, "mut [int32; 3] source = [0, 0, 0];", "&source[0]", 0, 3UL)]
    [TestCase(2, "mut [[int32; 2]; 2] source = [[0, 0], [0, 0]];",
        "&source[1][0]", 2, 2UL)]
    public async Task StandardNativeOutputUsesMultiElementFixedArrayBackingAsync(
        int nativeParameterExtent, string sourceInitialization,
        string sourceAddress, int expectedProjectionCount,
        ulong expectedElementCount)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "native-output-array-backing";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Pair {
                public int32 First;
                public int32 Second;
            }
            [param(buffer): DependsOn(nameof(baseAddress))]
            [Terrum.Interop.NativeImport("kernel32.dll",
                "ReadProcessMemory")]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape,
                {{nativeParameterExtent}})]
            [Terrum.Interop.NativeParameter(5, Write, NoEscape, 1)]
            extern unsafe int32 ReadProcessMemory(
                ptr<void> process, ptr<int32> baseAddress,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<Pair> buffer, nuint bytesToRead,
                write ptr<nuint> bytesRead);

            public unsafe int32 Probe(ptr<void> process,
                nuint bytesToRead, write ptr<nuint> bytesRead) {
                {{sourceInitialization}}
                init mut Pair buffer;
                unsafe {
                    return ReadProcessMemory(process, {{sourceAddress}},
                        &init buffer, bytesToRead, bytesRead);
                }
            }
            """), NativeOutputCoreRegistrationTests
                .CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity,
            moduleTargetRequirement: 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var caller = loaded.Functions.Single(static function =>
            function.Name == "Probe");
        BoundNativeStorageAddress? arrayBacking = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundNativeStorageAddress address)
                {
                    arrayBacking = address;
                }
            }), Is.True);
        Assert.That(arrayBacking, Is.Not.Null);
        var storageAddress = arrayBacking!;
        Assert.That(NativeFixedArrayTypes.IsType(
            storageAddress.CarrierType), Is.True);
        Assert.That(storageAddress.Projections.Length,
            Is.EqualTo(expectedProjectionCount));
        Assert.That(storageAddress.ElementCount,
            Is.EqualTo(expectedElementCount));
        Assert.That(storageAddress.ElementStride, Is.EqualTo(4UL));
        Assert.That(storageAddress.ByteExtent,
            Is.EqualTo(checked(expectedElementCount * 4UL)));

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, moduleTargetRequirement: 1);
        Assert.That(reemitted, Is.EqualTo(artifact));
        var reemittedInput = new TrRmStandardArtifactInput(schemaIdentity,
            reemitted);
        _ = TrRmExecutableCompiler.LoadStandard(reemittedInput,
            moduleIdentity, []);
        var image = NativeModuleCompiler.EmitStandardLibrary(
            reemittedInput, moduleIdentity, [], ["Probe"],
            NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-nativeparameter-array-backing-");
        try
        {
            var imagePath = Path.Combine(directory, "library.dll");
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            var library = NativeLibrary.Load(imagePath);
            nint bytesRead = 0;
            try
            {
                bytesRead = Marshal.AllocHGlobal(sizeof(long));
                Marshal.WriteInt64(bytesRead, 0);
                var function = Marshal.GetDelegateForFunctionPointer<
                    ArrayProbeDelegate>(NativeLibrary.GetExport(library,
                    "Probe"));
                Assert.That(function(new IntPtr(-1),
                    (nuint)Marshal.SizeOf<NativePair>(), bytesRead),
                    Is.EqualTo(1));
                Assert.That(Marshal.ReadInt64(bytesRead), Is.EqualTo(8));
            }
            finally
            {
                if (bytesRead != 0)
                {
                    Marshal.FreeHGlobal(bytesRead);
                }
                NativeLibrary.Free(library);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task StandardA2NativeParameterLowersSingleProjectionArrayAddressesAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-nativeparameter-array-address";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("kernel32.dll", "RtlMoveMemory")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 3)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 3)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            public unsafe int32 Probe(uint64 byteCount) {
                mut [int32; 3] source = [-1, -1, -1];
                mut [int32; 3] destination = [0, 0, 0];
                unsafe {
                    RtlMoveMemory(&destination[0], &source[0],
                        byteCount);
                }
                return destination[0] + destination[1]
                    + destination[2];
            }
            """), NativeOutputCoreRegistrationTests
                .CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity,
            moduleTargetRequirement: 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var caller = loaded.Functions.Single(static function =>
            function.Name == "Probe");
        var arrayAddresses = new List<BoundNativeStorageAddress>();
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundNativeStorageAddress address
                    && NativeFixedArrayTypes.IsType(address.CarrierType))
                {
                    arrayAddresses.Add(address);
                }
            }), Is.True);
        Assert.That(arrayAddresses, Has.Count.EqualTo(2));
        foreach (var address in arrayAddresses)
        {
            Assert.That(address.Projections.Length, Is.EqualTo(1));
            Assert.That(address.ElementCount, Is.EqualTo(3UL));
            Assert.That(address.ElementStride, Is.EqualTo(4UL));
            Assert.That(address.ByteExtent, Is.EqualTo(12UL));
        }

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, moduleTargetRequirement: 1);
        Assert.That(reemitted, Is.EqualTo(artifact));
        var reemittedInput = new TrRmStandardArtifactInput(schemaIdentity,
            reemitted);
        _ = TrRmExecutableCompiler.LoadStandard(reemittedInput,
            moduleIdentity, []);
        var image = NativeModuleCompiler.EmitStandardLibrary(
            reemittedInput, moduleIdentity, [], ["Probe"],
            NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-a2-nativeparameter-array-address-");
        try
        {
            var imagePath = Path.Combine(directory, "library.dll");
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            var library = NativeLibrary.Load(imagePath);
            try
            {
                var function = Marshal.GetDelegateForFunctionPointer<
                    NativeParameterArrayProbeDelegate>(
                    NativeLibrary.GetExport(library, "Probe"));
                Assert.That(function(12), Is.EqualTo(-3));
            }
            finally
            {
                NativeLibrary.Free(library);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    [TestCase(false, "&backing.Values[0]", 1)]
    [TestCase(true, "&backing.Data.Values[0]", 2)]
    public async Task StandardA2NativeOutputUsesCopyStructFieldArrayBackingAsync(
        bool nestedField, string fieldArrayAddress, int expectedFieldDepth)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-native-output-field-array-backing";
        var holderDeclaration = nestedField
            ? """
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Nested {
                    public mut [int32; 3] Values;
                }
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Holder {
                    public mut Nested Data;
                }
                """
            : """
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Holder {
                    public mut [int32; 3] Values;
                }
                """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Pair {
                public int32 First;
                public int32 Second;
            }
            {{holderDeclaration}}
            [param(buffer): DependsOn(nameof(baseAddress))]
            [Terrum.Interop.NativeImport("kernel32.dll",
                "ReadProcessMemory")]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 3)]
            [Terrum.Interop.NativeParameter(5, Write, NoEscape, 1)]
            extern unsafe int32 ReadProcessMemory(
                ptr<void> process, ptr<int32> baseAddress,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<Pair> buffer, nuint bytesToRead,
                write ptr<nuint> bytesRead);

            public unsafe int32 Probe(ptr<void> process, Holder value,
                nuint bytesToRead, write ptr<nuint> bytesRead) {
                mut Holder backing = value;
                init mut Pair buffer;
                unsafe {
                    return ReadProcessMemory(process,
                        {{fieldArrayAddress}}, &init buffer,
                        bytesToRead, bytesRead);
                }
            }
            """), NativeOutputCoreRegistrationTests
                .CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity,
            moduleTargetRequirement: 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var caller = loaded.Functions.Single(static function =>
            function.Name == "Probe");
        BoundNativeStorageAddress? fieldBacking = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundNativeStorageAddress address
                    && address.FieldPlace is not null)
                {
                    fieldBacking = address;
                }
            }), Is.True);
        Assert.That(fieldBacking, Is.Not.Null);
        var storageAddress = fieldBacking!;
        Assert.That(NativeFixedArrayTypes.IsType(
            storageAddress.CarrierType), Is.True);
        Assert.That(storageAddress.FieldPlace!.Type,
            Is.EqualTo(storageAddress.CarrierType));
        var fieldDepth = 0;
        var fieldPlace = storageAddress.FieldPlace;
        while (fieldPlace is BoundFieldRead fieldRead)
        {
            fieldDepth++;
            fieldPlace = fieldRead.Receiver;
        }
        Assert.That(fieldDepth, Is.EqualTo(expectedFieldDepth));
        Assert.That(storageAddress.ElementCount, Is.EqualTo(3UL));
        Assert.That(storageAddress.ElementStride, Is.EqualTo(4UL));
        Assert.That(storageAddress.ByteExtent, Is.EqualTo(12UL));

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, moduleTargetRequirement: 1);
        Assert.That(reemitted, Is.EqualTo(artifact));
        var reemittedInput = new TrRmStandardArtifactInput(schemaIdentity,
            reemitted);
        _ = TrRmExecutableCompiler.LoadStandard(reemittedInput,
            moduleIdentity, []);
        var image = NativeModuleCompiler.EmitStandardLibrary(
            reemittedInput, moduleIdentity, [], ["Probe"],
            NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-a2-native-output-field-array-");
        try
        {
            var imagePath = Path.Combine(directory, "library.dll");
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            var library = NativeLibrary.Load(imagePath);
            nint bytesRead = 0;
            try
            {
                bytesRead = Marshal.AllocHGlobal(sizeof(long));
                Marshal.WriteInt64(bytesRead, 0);
                var function = Marshal.GetDelegateForFunctionPointer<
                    FieldArrayProbeDelegate>(NativeLibrary.GetExport(
                    library, "Probe"));
                Assert.That(function(new IntPtr(-1), new NativeArrayHolder {
                    First = 11,
                    Second = 22,
                    Third = 33,
                }, 8, bytesRead), Is.EqualTo(1));
                Assert.That(Marshal.ReadInt64(bytesRead), Is.EqualTo(8));
            }
            finally
            {
                if (bytesRead != 0)
                {
                    Marshal.FreeHGlobal(bytesRead);
                }
                NativeLibrary.Free(library);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task StandardA2NativeOutputUsesNestedFieldAndArrayBackingAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-native-output-nested-field-array";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Pair {
                public int32 First;
                public int32 Second;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Nested {
                public mut [[int32; 2]; 2] Values;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public mut Nested Data;
            }
            [param(buffer): DependsOn(nameof(baseAddress))]
            [Terrum.Interop.NativeImport("kernel32.dll",
                "ReadProcessMemory")]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 2)]
            [Terrum.Interop.NativeParameter(5, Write, NoEscape, 1)]
            extern unsafe int32 ReadProcessMemory(
                ptr<void> process, ptr<int32> baseAddress,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<Pair> buffer, nuint bytesToRead,
                write ptr<nuint> bytesRead);

            public unsafe int32 Probe(ptr<void> process, Holder value,
                int32 outerIndex, int32 innerIndex, nuint bytesToRead,
                write ptr<nuint> bytesRead) {
                mut Holder backing = value;
                init mut Pair buffer;
                unsafe {
                    return ReadProcessMemory(process,
                        &backing.Data.Values[outerIndex][innerIndex],
                        &init buffer,
                        bytesToRead, bytesRead);
                }
            }
            """), NativeOutputCoreRegistrationTests
                .CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity,
            moduleTargetRequirement: 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var caller = loaded.Functions.Single(static function =>
            function.Name == "Probe");
        BoundNativeStorageAddress? fieldBacking = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundNativeStorageAddress address
                    && address.FieldPlace is not null)
                {
                    fieldBacking = address;
                }
            }), Is.True);
        Assert.That(fieldBacking, Is.Not.Null);
        var storageAddress = fieldBacking!;
        Assert.That(storageAddress.Projections.Length, Is.EqualTo(2));
        Assert.That(storageAddress.ElementCount, Is.EqualTo(2UL));
        Assert.That(storageAddress.ElementStride, Is.EqualTo(4UL));
        Assert.That(storageAddress.ByteExtent, Is.EqualTo(8UL));
        var fieldDepth = 0;
        var fieldPlace = storageAddress.FieldPlace;
        while (fieldPlace is BoundFieldRead fieldRead)
        {
            fieldDepth++;
            fieldPlace = fieldRead.Receiver;
        }
        Assert.That(fieldDepth, Is.EqualTo(2));

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, moduleTargetRequirement: 1);
        Assert.That(reemitted, Is.EqualTo(artifact));
        var reemittedInput = new TrRmStandardArtifactInput(schemaIdentity,
            reemitted);
        _ = TrRmExecutableCompiler.LoadStandard(reemittedInput,
            moduleIdentity, []);
        var image = NativeModuleCompiler.EmitStandardLibrary(
            reemittedInput, moduleIdentity, [], ["Probe"],
            NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-a2-native-output-nested-field-array-");
        try
        {
            var imagePath = Path.Combine(directory, "library.dll");
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            var library = NativeLibrary.Load(imagePath);
            nint bytesRead = 0;
            try
            {
                bytesRead = Marshal.AllocHGlobal(sizeof(long));
                Marshal.WriteInt64(bytesRead, 0);
                var function = Marshal.GetDelegateForFunctionPointer<
                    NestedFieldArrayProbeDelegate>(
                    NativeLibrary.GetExport(library, "Probe"));
                Assert.That(function(new IntPtr(-1),
                    new NativeNestedArrayHolder {
                        First = 11,
                        Second = 22,
                        Third = 33,
                        Fourth = 44,
                    }, 1, 0, 8, bytesRead), Is.EqualTo(1));
                Assert.That(Marshal.ReadInt64(bytesRead), Is.EqualTo(8));
            }
            finally
            {
                if (bytesRead != 0)
                {
                    Marshal.FreeHGlobal(bytesRead);
                }
                NativeLibrary.Free(library);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    [TestCase("int64", PrimitiveType.Int64)]
    [TestCase("uint64", PrimitiveType.UInt64)]
    public async Task StandardA2NativeOutputPreservesWideArrayIndexAsync(
        string indexTypeName, PrimitiveType expectedIndexType)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-native-output-wide-array-index";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [param(buffer): DependsOn(nameof(baseAddress))]
            [Terrum.Interop.NativeImport("kernel32.dll",
                "ReadProcessMemory")]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(5, Write, NoEscape, 1)]
            extern unsafe int32 ReadProcessMemory(
                ptr<void> process, ptr<int32> baseAddress,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<int32> buffer, nuint bytesToRead,
                write ptr<nuint> bytesRead);

            public unsafe int32 Probe(ptr<void> process,
                {{indexTypeName}} index, nuint bytesToRead,
                write ptr<nuint> bytesRead) {
                mut [int32; 3] source = [0, 0, 0];
                init mut int32 buffer;
                unsafe {
                    return ReadProcessMemory(process,
                        &source[index], &init buffer, bytesToRead,
                        bytesRead);
                }
            }
            """), NativeOutputCoreRegistrationTests
                .CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity,
            moduleTargetRequirement: 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var caller = loaded.Functions.Single(static function =>
            function.Name == "Probe");
        BoundNativeStorageAddress? arrayBacking = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundNativeStorageAddress address
                    && NativeFixedArrayTypes.IsType(address.CarrierType))
                {
                    arrayBacking = address;
                }
            }), Is.True);
        Assert.That(arrayBacking, Is.Not.Null);
        Assert.That(arrayBacking!.Index.Type, Is.EqualTo(expectedIndexType));

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, moduleTargetRequirement: 1);
        Assert.That(reemitted, Is.EqualTo(artifact));
        var reemittedInput = new TrRmStandardArtifactInput(schemaIdentity,
            reemitted);
        _ = TrRmExecutableCompiler.LoadStandard(reemittedInput,
            moduleIdentity, []);
        var image = NativeModuleCompiler.EmitStandardLibrary(
            reemittedInput, moduleIdentity, [], ["Probe"],
            NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-a2-native-output-wide-array-index-");
        try
        {
            var imagePath = Path.Combine(directory, "library.dll");
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            var library = NativeLibrary.Load(imagePath);
            nint bytesRead = 0;
            try
            {
                bytesRead = Marshal.AllocHGlobal(sizeof(long));
                Marshal.WriteInt64(bytesRead, 0);
                var function = Marshal.GetDelegateForFunctionPointer<
                    WideIndexProbeDelegate>(NativeLibrary.GetExport(library,
                    "Probe"));
                Assert.That(function(new IntPtr(-1), 1, 4, bytesRead),
                    Is.EqualTo(1));
                Assert.That(Marshal.ReadInt64(bytesRead), Is.EqualTo(4));
            }
            finally
            {
                if (bytesRead != 0)
                {
                    Marshal.FreeHGlobal(bytesRead);
                }
                NativeLibrary.Free(library);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    [TestCase("index++", false)]
    [TestCase("++index", true)]
    public async Task StandardA2NativeOutputCapturesArrayIndexUpdateAsync(
        string indexExpression, bool expectedPrefix)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-native-output-postfix-array-index";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [param(buffer): DependsOn(nameof(baseAddress))]
            [Terrum.Interop.NativeImport("kernel32.dll",
                "ReadProcessMemory")]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(5, Write, NoEscape, 1)]
            extern unsafe int32 ReadProcessMemory(
                ptr<void> process, ptr<int32> baseAddress,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<int32> buffer, nuint bytesToRead,
                write ptr<nuint> bytesRead);

            public unsafe int32 Probe(ptr<void> process, int32 initialIndex,
                nuint bytesToRead, write ptr<nuint> bytesRead) {
                mut [int32; 3] source = [0, 0, 0];
                mut int32 index = initialIndex;
                init mut int32 buffer;
                unsafe {
                    return ReadProcessMemory(process,
                        &source[{{indexExpression}}], &init buffer,
                        bytesToRead, bytesRead);
                }
            }
            """), NativeOutputCoreRegistrationTests
                .CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity,
            moduleTargetRequirement: 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var caller = loaded.Functions.Single(static function =>
            function.Name == "Probe");
        BoundNativeStorageAddress? arrayBacking = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundNativeStorageAddress address
                    && NativeFixedArrayTypes.IsType(address.CarrierType))
                {
                    arrayBacking = address;
                }
            }), Is.True);
        Assert.That(arrayBacking, Is.Not.Null);
        Assert.That(arrayBacking!.Index, Is.TypeOf<BoundUpdate>());
        Assert.That(((BoundUpdate)arrayBacking.Index).IsPrefix,
            Is.EqualTo(expectedPrefix));

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, moduleTargetRequirement: 1);
        Assert.That(reemitted, Is.EqualTo(artifact));
        var reemittedInput = new TrRmStandardArtifactInput(schemaIdentity,
            reemitted);
        _ = TrRmExecutableCompiler.LoadStandard(reemittedInput,
            moduleIdentity, []);
        var image = NativeModuleCompiler.EmitStandardLibrary(
            reemittedInput, moduleIdentity, [], ["Probe"],
            NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-a2-native-output-postfix-index-");
        try
        {
            var imagePath = Path.Combine(directory, "library.dll");
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            var library = NativeLibrary.Load(imagePath);
            nint bytesRead = 0;
            try
            {
                bytesRead = Marshal.AllocHGlobal(sizeof(long));
                Marshal.WriteInt64(bytesRead, 0);
                var function = Marshal.GetDelegateForFunctionPointer<
                    ArrayUpdateProbeDelegate>(NativeLibrary.GetExport(
                    library, "Probe"));
                Assert.That(function(new IntPtr(-1), 1, 4, bytesRead),
                    Is.EqualTo(1));
                Assert.That(Marshal.ReadInt64(bytesRead), Is.EqualTo(4));
            }
            finally
            {
                if (bytesRead != 0)
                {
                    Marshal.FreeHGlobal(bytesRead);
                }
                NativeLibrary.Free(library);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public async Task StandardA2NativeOutputUsesComputedArrayIndexAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);

        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-native-output-computed-array-index";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [param(buffer): DependsOn(nameof(baseAddress))]
            [Terrum.Interop.NativeImport("kernel32.dll",
                "ReadProcessMemory")]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(5, Write, NoEscape, 1)]
            extern unsafe int32 ReadProcessMemory(
                ptr<void> process, ptr<int32> baseAddress,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<int32> buffer, nuint bytesToRead,
                write ptr<nuint> bytesRead);

            public unsafe int32 Probe(ptr<void> process,
                int32 firstIndex, int32 secondIndex, nuint bytesToRead,
                write ptr<nuint> bytesRead) {
                mut [int32; 3] source = [0, 0, 0];
                int32 selectedIndex = firstIndex + secondIndex;
                init mut int32 buffer;
                unsafe {
                    return ReadProcessMemory(process,
                        &source[selectedIndex], &init buffer,
                        bytesToRead, bytesRead);
                }
            }
            """), NativeOutputCoreRegistrationTests
                .CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity,
            moduleTargetRequirement: 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var caller = loaded.Functions.Single(static function =>
            function.Name == "Probe");
        BoundNativeStorageAddress? arrayBacking = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundNativeStorageAddress address
                    && NativeFixedArrayTypes.IsType(address.CarrierType))
                {
                    arrayBacking = address;
                }
            }), Is.True);
        Assert.That(arrayBacking, Is.Not.Null);
        var indexRead = arrayBacking!.Index as BoundRead;
        Assert.That(indexRead, Is.Not.Null);
        Assert.That(caller.Body.Statements.OfType<BoundLocal>().Single(
            local => local.Variable == indexRead!.Variable).Initializer,
            Is.TypeOf<BoundBinary>());

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, moduleTargetRequirement: 1);
        Assert.That(reemitted, Is.EqualTo(artifact));
        var reemittedInput = new TrRmStandardArtifactInput(schemaIdentity,
            reemitted);
        _ = TrRmExecutableCompiler.LoadStandard(reemittedInput,
            moduleIdentity, []);
        var image = NativeModuleCompiler.EmitStandardLibrary(
            reemittedInput, moduleIdentity, [], ["Probe"],
            NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory(
            "terrum-a2-native-output-computed-index-");
        try
        {
            var imagePath = Path.Combine(directory, "library.dll");
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            var library = NativeLibrary.Load(imagePath);
            nint bytesRead = 0;
            try
            {
                bytesRead = Marshal.AllocHGlobal(sizeof(long));
                Marshal.WriteInt64(bytesRead, 0);
                var function = Marshal.GetDelegateForFunctionPointer<
                    ComputedIndexProbeDelegate>(
                    NativeLibrary.GetExport(library, "Probe"));
                Assert.That(function(new IntPtr(-1), 1, 0, 4, bytesRead),
                    Is.EqualTo(1));
                Assert.That(Marshal.ReadInt64(bytesRead), Is.EqualTo(4));
            }
            finally
            {
                if (bytesRead != 0)
                {
                    Marshal.FreeHGlobal(bytesRead);
                }
                NativeLibrary.Free(library);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory)
                .ConfigureAwait(true);
        }
    }
}
