// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeComSourceMetadataTests
{
    [Test]
    public void SourceOwnerResolvesAnUnqualifiedCatalogInterfaceIdentity()
    {
        var interfaceDefinition = new NativeComInterfaceDefinition(
            "IFixture", Guid.Parse("A3BFA1D9-0169-4B6C-9334-33D17A7CE9E7"), null,
            ["IFixture::Release"], ["IFixture::Release"]);
        var methodDefinition = new NativeComMethodDefinition(
            "fixture.iface.release", "IFixture", "IFixture", 0,
            "uint32(IFixture*)", "windows-x64-rcx-eax", NativeComEffects.ReleasesReference,
            IsCallable: true) {
            VTableMethodIdentity = "IFixture::Release",
            IsNoThrow = true,
        };
        var catalogImport = new NativeImportDefinition(
            "test.fixture.create", "fixture.dll", "CreateFixture",
            [PrimitiveType.NativeComInterfaceOutputAddress], PrimitiveType.Int32,
            IsNoThrow: true, RequiresUnsafeCaller: true, DoesNotReturn: false,
            NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [NativeImportParameterContract.OwnedNativeComInterfaceOutput],
            ComOutputs = [new(0, "IFixture", NativeComEffects.SignedHResult
                | NativeComEffects.AcquiresReference | NativeComEffects.OutputMayBeWrittenOnFailure)],
        };
        var catalogOwner = new NativeComOwnerDefinition(
            "Example.Interop.ProvisionalFixtureOwner", "IFixture",
            NativeComOwnerAcquisitionKind.NativeImportOutput, "test.fixture.create", 0,
            IsUnique: true, CellSize: sizeof(long), CellAlignment: sizeof(long)) {
            ReleaseMethodIdentity = methodDefinition.Identity,
            OutputMayBeWrittenOnFailure = true,
        };
        var catalog = NativeImportCatalog.CreateForTesting(
            "test.generic-com.source-owner.v1", [catalogImport], [interfaceDefinition],
            [catalogOwner], [methodDefinition], []);
        const string source = """
            namespace Example.Interop;

            [Terrum.Interop.NativeComOwner("IFixture")]
            public unique struct FixtureOwner {
                private ptr<void> Pointer;
                public unsafe FixtureOwner(ptr<void> pointer) {
                    unsafe { Pointer = pointer; }
                }
            }

            [Terrum.Interop.NativeImport("fixture.dll", "AcquireFixture")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 0)]
            [Terrum.Interop.NativeOutput(2)]
            extern unsafe int32 AcquireFixture(
                ptr<char16> path, ptr<void> kind, write ptr<ptr<void>> output);

            [Terrum.Interop.NativeComAcquisition(
                0, "FixtureOwner", "AcquireFixture", 2, OutputMayBeWrittenOnFailure)]
            bool TryAcquireFixture(out FixtureOwner owner) {
                mut ptr<void> raw = null;
                mut int32 result = 0;
                unsafe { result = AcquireFixture(
                    utf16z("fixture.bin"), utf8z("fixture"), &raw); }
                owner = unsafe(new FixtureOwner(raw));
                return result >= 0;
            }

            int32 Main() { return 0; }
            """;

        var compilation = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(source)], catalog, BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var owners = compilation.Program!.NativeImportCatalog!.ComOwners;
        Assert.That(owners, Has.Length.EqualTo(1));
        Assert.That(owners[0].BoundOwnerName, Is.EqualTo("Example.Interop.FixtureOwner"));
        Assert.That(owners[0].InterfaceIdentity, Is.EqualTo("IFixture"));
        var import = compilation.Program.Functions.Single(static function =>
            function.Name == "Example.Interop.AcquireFixture").DirectNativeImport!;
        Assert.That(import.Parameters, Is.EqualTo([
            new DirectNativeParameter(0, PrimitiveType.Char16,
                NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 0),
            new DirectNativeParameter(1, PrimitiveType.Void,
                NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 0),
        ]));
        Assert.That(import.Outputs.Single().ParameterIndex, Is.EqualTo(2));
        Assert.That(NativeComCallContract.Acquisitions(
            compilation.Program.Functions.Single(static function =>
                function.Name == "Example.Interop.TryAcquireFixture"), compilation.Program), Is.Not.Null);

        var unknownInterface = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(source.Replace(
                "[Terrum.Interop.NativeComOwner(\"IFixture\")]",
                "[Terrum.Interop.NativeComOwner(\"IMissing\")]", StringComparison.Ordinal))],
            catalog, BoundRuntimePolicy.FailFast);
        Assert.That(unknownInterface.Program, Is.Null);
    }

    private const string SourceOwnerAcquisition = """
        namespace Example.Interop;

        [Terrum.Interop.NativeComInterface("9df7f7b6-1cbb-4ad5-8c10-743c19e4319f")]
        interface IThing {
            [Terrum.Interop.NativeComMethod(0, ReleasesReference)]
            [NoThrow]
            unsafe uint32 Release();

            [Terrum.Interop.NativeComMethod(1)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            unsafe void SetData(ptr<void> data);
        }

        [Terrum.Interop.NativeComOwner("IThing")]
        public unique struct ThingOwner {
            private ptr<void> Pointer;

            public unsafe ThingOwner(ptr<void> pointer) {
                unsafe {
                    Pointer = pointer;
                }
            }
        }

        [Terrum.Interop.NativeImport("example.dll", "AcquireThing")]
        [Terrum.Interop.NativeOutput(0)]
        extern unsafe int32 AcquireThing(write ptr<ptr<void>> output);

        [Terrum.Interop.NativeComAcquisition(
            0, "ThingOwner", "AcquireThing", 0, OutputMayBeWrittenOnFailure)]
        bool TryAcquireThing(out ThingOwner owner) {
            mut ptr<void> raw = null;
            mut int32 result = 0;
            unsafe {
                result = AcquireThing(&raw);
            }
            owner = unsafe(new ThingOwner(raw));
            return result >= 0;
        }

        int32 Main() {
            if (TryAcquireThing(out var owner)) {
                return 0;
            }
            return 0;
        }
        """;

    private const string SourceComAggregateParameter = """
        namespace Example.Interop;

        [Blittable] [Layout(Sequential)]
        struct RecordIdentity {
            uint32 High;
            uint32 Low;

            public RecordIdentity(uint32 high, uint32 low) {
                High = high;
                Low = low;
            }
        }

        [Terrum.Interop.NativeComInterface("a07cd987-6ee5-48e6-80cb-44dd59cb3d65")]
        interface IRecordReader {
            [Terrum.Interop.NativeComMethod(0, ReleasesReference)]
            [NoThrow]
            unsafe uint32 Release();

            [Terrum.Interop.NativeComMethod(1)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            unsafe int32 IsCurrent(ptr<RecordIdentity> identity);
        }

        [Terrum.Interop.NativeComOwner("IRecordReader")]
        public unique struct RecordReaderOwner {
            private ptr<void> Pointer;

            public unsafe RecordReaderOwner(ptr<void> pointer) {
                unsafe { Pointer = pointer; }
            }
        }

        [Terrum.Interop.NativeImport("example.dll", "CreateRecordReader")]
        [Terrum.Interop.NativeOutput(0)]
        extern unsafe int32 CreateRecordReader(write ptr<ptr<void>> output);

        [Terrum.Interop.NativeComAcquisition(
            0, "RecordReaderOwner", "CreateRecordReader", 0, OutputMayBeWrittenOnFailure)]
        bool TryCreateRecordReader(out RecordReaderOwner owner) {
            mut ptr<void> raw = null;
            mut int32 result = 0;
            unsafe { result = CreateRecordReader(&raw); }
            owner = unsafe(new RecordReaderOwner(raw));
            return result >= 0;
        }

        int32 Main() {
            RecordIdentity identity = new RecordIdentity(1, 2);
            if (TryCreateRecordReader(out var reader)) {
                mut int32 result = 0;
                unsafe { result = reader.IsCurrent(&identity); }
                return result;
            }
            return 0;
        }
        """;

    private const string SourceComFixedInterfaceAcquisition = """
        namespace Example.Interop;

        [Terrum.Interop.NativeComInterface("d6c3f6ac-39d7-4b9c-9a8d-a2d927605808")]
        interface IThing {
            [Terrum.Interop.NativeComMethod(0, ReleasesReference)]
            [NoThrow]
            unsafe uint32 Release();
        }

        [Terrum.Interop.NativeComInterface("0d35e0b0-f419-4d8f-a1c3-c3969c35b9ed")]
        interface IFactory {
            [Terrum.Interop.NativeComMethod(0, ReleasesReference)]
            [NoThrow]
            unsafe uint32 Release();

            [Terrum.Interop.NativeComMethod(
                1, SignedHResult, AcquiresReference, OutputMayBeWrittenOnFailure)]
            [Terrum.Interop.NativeComHiddenParameter(1, UInt32, 0)]
            [Terrum.Interop.NativeComHiddenParameter(2, NullPointer)]
            [Terrum.Interop.NativeComHiddenParameter(3, InterfaceIdAddress, "IThing")]
            [Terrum.Interop.NativeParameter(4, Write, NoEscape, 1)]
            [Terrum.Interop.NativeOutput(4)]
            unsafe HRESULT GetThing(uint32 reserved, LPVOID outer, REFIID interfaceId,
                write ptr<ptr<IThing>> output);
        }

        [Terrum.Interop.NativeComOwner("IThing")]
        public unique struct ThingOwner {
            private ptr<void> Pointer;

            public unsafe ThingOwner(ptr<void> pointer) {
                unsafe { Pointer = pointer; }
            }
        }

        [Terrum.Interop.NativeComOwner("IFactory")]
        public unique struct FactoryOwner {
            private ptr<void> Pointer;

            public unsafe FactoryOwner(ptr<void> pointer) {
                unsafe { Pointer = pointer; }
            }
        }

        [Terrum.Interop.NativeImport("example.dll", "CreateFactory")]
        [Terrum.Interop.NativeOutput(0)]
        extern unsafe int32 CreateFactory(write ptr<ptr<void>> output);

        [Terrum.Interop.NativeComAcquisition(
            0, "FactoryOwner", "CreateFactory", 0, OutputMayBeWrittenOnFailure)]
        bool TryCreateFactory(out FactoryOwner owner) {
            mut ptr<void> raw = null;
            mut int32 result = 0;
            unsafe { result = CreateFactory(&raw); }
            owner = unsafe(new FactoryOwner(raw));
            return result >= 0;
        }

        [Terrum.Interop.NativeComAcquisition(
            0, "ThingOwner", "IFactory::GetThing", 4, OutputMayBeWrittenOnFailure)]
        bool TryGetThing(out ThingOwner owner) {
            if (!TryCreateFactory(out var factory)) {
                owner = unsafe(new ThingOwner(null));
                return false;
            }
            unsafe { return factory.GetThing(out owner); }
        }

        int32 Main() {
            if (TryGetThing(out var thing)) {
                return 0;
            }
            return 1;
        }
        """;

    private const string SourceComOwnerAcquisition = """
        namespace Example.Interop;

        [Terrum.Interop.NativeComInterface("f6218053-95b2-44c2-a9ec-d8f8a26056d8")]
        interface IThing {
            [Terrum.Interop.NativeComMethod(0, ReleasesReference)]
            [NoThrow]
            unsafe uint32 Release();
        }

        [Terrum.Interop.NativeComInterface("cd290ce4-9e12-40bb-90f4-73c41f1a417f")]
        interface IFactory {
            [Terrum.Interop.NativeComMethod(
                0, SignedHResult, AcquiresReference, OutputMayBeWrittenOnFailure)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeOutput(1)]
            unsafe HRESULT Acquire(write ptr<ptr<IThing>> output);

            [Terrum.Interop.NativeComMethod(1, ReleasesReference)]
            [NoThrow]
            unsafe uint32 Release();
        }

        [Terrum.Interop.NativeComOwner("IThing")]
        public unique struct ThingOwner {
            private ptr<void> Pointer;
            public unsafe ThingOwner(ptr<void> pointer) {
                unsafe { Pointer = pointer; }
            }
        }

        [Terrum.Interop.NativeComOwner("IFactory")]
        public unique struct FactoryOwner {
            private ptr<void> Pointer;
            public unsafe FactoryOwner(ptr<void> pointer) {
                unsafe { Pointer = pointer; }
            }
        }

        [Terrum.Interop.NativeImport("example.dll", "CreateFactory")]
        [Terrum.Interop.NativeOutput(0)]
        extern unsafe int32 CreateFactory(write ptr<ptr<void>> output);

        [Terrum.Interop.NativeComAcquisition(
            0, "FactoryOwner", "CreateFactory", 0, OutputMayBeWrittenOnFailure)]
        bool TryCreateFactory(out FactoryOwner owner) {
            mut ptr<void> raw = null;
            mut int32 result = 0;
            unsafe {
                result = CreateFactory(&raw);
            }
            owner = unsafe(new FactoryOwner(raw));
            return result >= 0;
        }

        [Terrum.Interop.NativeComAcquisition(
            0, "ThingOwner", "IFactory::Acquire", 1, OutputMayBeWrittenOnFailure)]
        bool TryAcquireThing(out ThingOwner owner) {
            if (!TryCreateFactory(out var factory)) {
                owner = unsafe(new ThingOwner(null));
                return false;
            }
            unsafe {
                return factory.Acquire(out owner);
            }
        }

        int32 Main() {
            return 0;
        }
        """;

    private const string SparseSourceComInterfaces = """
        namespace Example.Interop;

        [Terrum.Interop.NativeComInterface("e35301d3-7071-4482-a6d6-3f74f04090f0")]
        interface IClock {
            [Terrum.Interop.NativeComMethod(5)]
            unsafe int32 Finish();

            [Terrum.Interop.NativeComMethod(2)]
            unsafe int32 Begin();
        }

        [Terrum.Interop.NativeComInterface("f56cb04d-1b0c-4990-8fa5-2e08d7125d13")]
        interface IPreciseClock : IClock {
            [Terrum.Interop.NativeComMethod(8)]
            unsafe int32 Pause();

            [Terrum.Interop.NativeComMethod(6)]
            unsafe int32 Reset();
        }

        [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
        [Terrum.Interop.NativeOutput(0)]
        public extern int32 ReadValue(write ptr<int32> value);
        """;

    [Test]
    public void SourceAuthoredComInterfaceRoundTripsThroughProvisionalTrRm()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace Example.Interop;

            [Terrum.Interop.NativeComInterface("76e1b75f-286c-4571-a9cd-43aa5b64a381")]
            interface IClock {
                [Terrum.Interop.NativeComMethod(0, SignedHResult)]
                [NoThrow]
                unsafe HRESULT Read();

                [Terrum.Interop.NativeComMethod(1)]
                [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
                unsafe int32 Scale(ptr<int32> factor);

                [Terrum.Interop.NativeComMethod(2, ReleasesReference)]
                [NoThrow]
                unsafe uint32 Release();
            }
            [Terrum.Interop.NativeImport("example.dll", "ReadValue")]
            [Terrum.Interop.NativeOutput(0)]
            public extern int32 ReadValue(write ptr<int32> value);
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.Emit(compilation.Program!, "com-marker");
        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x13, out _, out var typeCount), Is.True);
        Assert.That(typeCount, Is.EqualTo(1));
        Assert.That(index.TryGetSection(0x1B, out _, out var contractCount), Is.True);
        Assert.That(contractCount, Is.EqualTo(10));
        Assert.That(index.TryGetSection(0x0004, out var featureBytes, out var featureCount), Is.True);
        var features = new TrRmFeatureTableReader(featureBytes, featureCount);
        Assert.That(features.TryGetFeature(TrRmFeatureTableReader.NativeComContractsFeatureId,
            out var comFeature), Is.True);
        Assert.That(comFeature, Is.EqualTo(TrRmFeatureRow.NativeComContracts));

        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.That(loaded.NativeImportCatalog!.ComInterfaces.Single().NominalIdentity,
            Is.EqualTo("Example.Interop.IClock"));
        var loadedMethods = loaded.NativeImportCatalog.ComMethods;
        Assert.That(loadedMethods.Select(static method => method.VTableMethodIdentity),
            Is.EqualTo([
                "Example.Interop.IClock::Read",
                "Example.Interop.IClock::Scale",
                "Example.Interop.IClock::Release",
            ]));
        Assert.That(loadedMethods[1].NativeParameters, Has.Length.EqualTo(1));
        Assert.That(loadedMethods[1].NativeParameters.Single(),
            Is.EqualTo(new NativeParameterDefinition(1, NativeParameterAccess.Read,
                NativeParameterRetention.NoEscape, 1)));

        var reference = TrRmExecutableCompiler.CreateReference(artifact);
        var consumerSource = """
            using Example.Interop;

            [Terrum.Interop.NativeComOwner("IClock")]
            public unique struct ClockOwner {
                private ptr<void> Pointer;
                public unsafe ClockOwner(ptr<void> pointer) {
                    unsafe { Pointer = pointer; }
                }
            }

            [Terrum.Interop.NativeImport("example.dll", "CreateClock")]
            [Terrum.Interop.NativeOutput(0)]
            public extern int32 CreateClock(write ptr<ptr<void>> output);

            [Terrum.Interop.NativeComAcquisition(
                0, "ClockOwner", "CreateClock", 0, OutputMayBeWrittenOnFailure)]
            bool TryCreateClock(out ClockOwner owner) {
                mut ptr<void> raw = null;
                mut int32 result = 0;
                unsafe { result = CreateClock(&raw); }
                owner = unsafe(new ClockOwner(raw));
                return result >= 0;
            }

            int32 Main() {
                mut int32 factor = 1;
                if (TryCreateClock(out var clock)) {
                    unsafe { return clock.Scale(&factor); }
                }
                return -1;
            }
            """;
        var consumer = Compilation.Create(SyntaxTree.Parse(consumerSource), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        Assert.That(consumer.Program!.NativeImportCatalog!.ComInterfaces.Single().NominalIdentity,
            Is.EqualTo("Example.Interop.IClock"));
        var image = NativeEmitter.EmitWithNativeImports(consumer.Program,
            NativeRuntimePolicy.FailFast);
        Assert.That(System.Text.Encoding.ASCII.GetString(image), Does.Contain("CreateClock"));

        var mismatchedPointee = Compilation.Create(
            SyntaxTree.Parse(consumerSource.Replace("mut int32 factor = 1;",
                "mut int64 factor = 1;", StringComparison.Ordinal)), reference);
        Assert.That(mismatchedPointee.Program, Is.Null);
        Assert.That(mismatchedPointee.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, mismatchedPointee.Diagnostics));

        var duplicateSlot = CorruptNativeComMethodSlot(artifact, 1);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(duplicateSlot));
        var unrepresentableSlot = CorruptNativeComMethodSlot(artifact, uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(unrepresentableSlot));
    }

    [Test]
    public void SourceAuthoredOwnerAndNativeImportAcquisitionAreGeneric()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(SourceOwnerAcquisition));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var owner = program.NativeImportCatalog!.ComOwners.Single();
        Assert.That(owner.BoundOwnerName, Is.EqualTo("Example.Interop.ThingOwner"));
        Assert.That(owner.InterfaceIdentity, Is.EqualTo("Example.Interop.IThing"));
        Assert.That(owner.AcquisitionKind,
            Is.EqualTo(NativeComOwnerAcquisitionKind.DirectNativeImportOutput));
        Assert.That(owner.AcquisitionIdentity, Is.EqualTo("Example.Interop.AcquireThing"));
        Assert.That(owner.OutputParameterIndex, Is.Zero);
        Assert.That(owner.OutputMayBeWrittenOnFailure, Is.True);
        var structure = program.Structures.Single(static structure =>
            structure.Name == "Example.Interop.ThingOwner");
        Assert.That(structure.Fields, Has.Length.EqualTo(1));
        Assert.That(structure.CleanupMethod, Is.EqualTo("Example.Interop.ThingOwner.Close"));
        var forward = program.Functions.Single(static function =>
            function.Name == "Example.Interop.TryAcquireThing");
        Assert.That(forward.NativeComAcquisitionSources, Has.Length.EqualTo(1));
        Assert.That(NativeComCallContract.Acquisitions(forward, program), Is.Not.Null);
        _ = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast);
    }

    [Test]
    public void SourceComMethodsBorrowExactNoEscapeOwnerParameters()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            SourceComBorrowedOwnerCall("Read", "NoEscape")));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var useMethod = program.NativeImportCatalog!.ComMethods.Single(static method =>
            method.Identity == "Example.Interop.IThing::UseThing");
        Assert.That(useMethod.BorrowedOwnerParameters, Is.EqualTo([1]));
        var structure = program.Structures.Single(static structure =>
            structure.Name == "Example.Interop.ThingOwner");
        var ownerType = (PrimitiveType)(100 + program.Structures.IndexOf(structure));
        var useFunction = program.Functions.Single(static function =>
            function.Name == "Example.Interop.ThingOwner.UseThing");
        Assert.That(useFunction.Variables[1].Type, Is.EqualTo(ownerType));
        var useFunctionIndex = program.Functions.IndexOf(useFunction);
        var exercise = program.Functions.Single(static function =>
            function.Name == "Example.Interop.ExerciseBorrowedOwner");
        BoundCall? useCall = null;
        _ = BoundTreeTraversal.RewriteCalls(exercise, call => {
            if (call.Function == useFunctionIndex)
            {
                useCall = call;
            }
            return call;
        });
        Assert.That(useCall, Is.Not.Null);
        Assert.That(useCall!.Arguments[1], Is.TypeOf<BoundReceiverLoan>());
        Assert.That(useCall.NativeComCall!.Loans.Single(static loan => loan.ParameterIndex == 1)
            .Owner.BoundOwnerName,
            Is.EqualTo("Example.Interop.ThingOwner"));
        _ = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast);
    }

    [TestCase("Read", "MayEscape")]
    [TestCase("Write", "NoEscape")]
    public void SourceComMethodDoesNotInferAnEscapingOrWritableOwnerLoan(
        string access, string retention)
    {
        var source = SourceOwnerAcquisition
            .Replace("NativeParameter(1, Read, NoEscape, 1)",
                $"NativeParameter(1, {access}, {retention}, 1)", StringComparison.Ordinal)
            .Replace("SetData(ptr<void> data);", "UseThing(ptr<IThing> other);", StringComparison.Ordinal);
        var metadataCompilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(metadataCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, metadataCompilation.Diagnostics));
        Assert.That(metadataCompilation.Program!.NativeImportCatalog!.ComMethods.Single(static method =>
            method.Identity == "Example.Interop.IThing::UseThing").BorrowedOwnerParameters, Is.Empty);

        var compilation = Compilation.Create(SyntaxTree.Parse(
            SourceComBorrowedOwnerCall(access, retention)));

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3011"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    private static string SourceComBorrowedOwnerCall(string access, string retention) =>
        SourceOwnerAcquisition
            .Replace("NativeParameter(1, Read, NoEscape, 1)",
                $"NativeParameter(1, {access}, {retention}, 1)", StringComparison.Ordinal)
            .Replace("SetData(ptr<void> data);", "UseThing(ptr<IThing> other);", StringComparison.Ordinal)
            .Replace("int32 Main() {",
                "int32 ExerciseBorrowedOwner() {\n"
                + "    if (TryAcquireThing(out var first)) {\n"
                + "        unsafe { first.UseThing(first); }\n"
                + "    }\n"
                + "    return 0;\n"
                + "}\n\n"
                + "int32 Main() {", StringComparison.Ordinal);

    [Test]
    public void SignedNonnegativeNativeOutputSupportsFixedOwnerAcquisition()
    {
        var source = SourceOwnerAcquisition.Replace("[Terrum.Interop.NativeOutput(0)]",
            "[Terrum.Interop.NativeOutput(0, NonNegative)]", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var acquisition = compilation.Program!.Functions.Single(static function =>
            function.Name == "Example.Interop.AcquireThing");
        Assert.That(acquisition.DirectNativeImport!.Outputs.Single().ResultCondition, Is.EqualTo(3u));
        Assert.That(NativeComCallContract.Acquisitions(
            compilation.Program.Functions.Single(static function =>
                function.Name == "Example.Interop.TryAcquireThing"), compilation.Program), Is.Not.Null);
    }

    [Test]
    public void SourceAuthoredOwnerAcquisitionRejectsWrongFailureDisposition()
    {
        var source = SourceOwnerAcquisition.Replace(
            "OutputMayBeWrittenOnFailure", "OutputNotWrittenOnFailure", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void SourceAuthoredOwnerRequiresPrivatePointerCell()
    {
        var source = SourceOwnerAcquisition.Replace(
            "private ptr<void> Pointer;", "public ptr<void> Pointer;", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void SourceAuthoredComOutputAcquisitionPreservesFixedInterfaceOwnership()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(SourceComOwnerAcquisition));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var thingOwner = program.NativeImportCatalog!.ComOwners.Single(static owner =>
            owner.BoundOwnerName == "Example.Interop.ThingOwner");
        Assert.That(thingOwner.AcquisitionKind,
            Is.EqualTo(NativeComOwnerAcquisitionKind.ComMethodOutput));
        Assert.That(thingOwner.AcquisitionIdentity, Is.EqualTo("Example.Interop.IFactory::Acquire"));
        Assert.That(thingOwner.InterfaceIdentity, Is.EqualTo("Example.Interop.IThing"));
        var acquire = program.NativeImportCatalog.ComMethods.Single(static method =>
            method.Identity == "Example.Interop.IFactory::Acquire");
        Assert.That(acquire.OutputInterfaceIdentity, Is.EqualTo(thingOwner.InterfaceIdentity));
        Assert.That(NativeComCallContract.Acquisitions(
            program.Functions.Single(static function => function.Name == "Example.Interop.TryAcquireThing"),
            program), Is.Not.Null);
        _ = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast);
    }

    [Test]
    public void SourceAuthoredComAcquisitionRejectsOwnerForDifferentOutputInterface()
    {
        var source = SourceComOwnerAcquisition.Replace(
            "write ptr<ptr<IThing>> output", "write ptr<ptr<IFactory>> output",
            StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void SourceAuthoredComAcquisitionRejectsUnmatchedSourceContract()
    {
        foreach (var source in new[] {
            SourceComOwnerAcquisition.Replace(
                "\"IFactory::Acquire\", 1, OutputMayBeWrittenOnFailure)",
                "\"IFactory::Acquire\", 0, OutputMayBeWrittenOnFailure)", StringComparison.Ordinal),
            SourceComOwnerAcquisition.Replace(
                "\"IFactory::Acquire\", 1, OutputMayBeWrittenOnFailure)",
                "\"IFactory::Missing\", 1, OutputMayBeWrittenOnFailure)", StringComparison.Ordinal),
            SourceComOwnerAcquisition.Replace(
                "\"IFactory::Acquire\", 1, OutputMayBeWrittenOnFailure)",
                "\"IFactory::Acquire\", 1, OutputNotWrittenOnFailure)", StringComparison.Ordinal),
        })
        {
            var compilation = Compilation.Create(SyntaxTree.Parse(source));

            Assert.That(compilation.Program, Is.Null);
            Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
                string.Join(Environment.NewLine, compilation.Diagnostics));
        }
    }

    private static byte[] CorruptNativeComMethodSlot(byte[] artifact, uint replacementSlot)
    {
        var sectionCount = BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(12, sizeof(uint)));
        for (var entry = 0; entry < sectionCount; entry++)
        {
            var directoryOffset = 32 + entry * 16;
            if (BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(directoryOffset, sizeof(ushort)))
                != 0x001B)
            {
                continue;
            }
            var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                artifact.AsSpan(directoryOffset + 4, sizeof(uint))));
            var sectionOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                artifact.AsSpan(directoryOffset + 8, sizeof(uint))));
            var offsetBytes = checked((count + 1) * sizeof(uint));
            for (var row = 0; row < count; row++)
            {
                var first = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    artifact.AsSpan(sectionOffset + row * sizeof(uint), sizeof(uint))));
                var last = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    artifact.AsSpan(sectionOffset + (row + 1) * sizeof(uint), sizeof(uint))));
                var dataOffset = sectionOffset + offsetBytes + first;
                if (last - first == 16
                    && BinaryPrimitives.ReadUInt16LittleEndian(
                        artifact.AsSpan(dataOffset + 4, sizeof(ushort))) == 8
                    && BinaryPrimitives.ReadUInt32LittleEndian(
                        artifact.AsSpan(dataOffset + 8, sizeof(uint))) == 0)
                {
                    var malformed = artifact.ToArray();
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        malformed.AsSpan(dataOffset + 8, sizeof(uint)), replacementSlot);
                    return malformed;
                }
            }
            break;
        }
        throw new InvalidOperationException("The emitted TRRM artifact has no zero-slot COM method clause to corrupt.");
    }

    [Test]
    public void SourceAuthoredInterfaceAndMethodPopulateGenericCatalog()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Example.Interop;

            [Terrum.Interop.NativeComInterface("76e1b75f-286c-4571-a9cd-43aa5b64a381")]
            interface IClock {
                [Terrum.Interop.NativeComMethod(0, SignedHResult)]
                [NoThrow]
                unsafe HRESULT Read();

                [Terrum.Interop.NativeComMethod(1)]
                [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
                unsafe int32 Scale(ptr<int32> factor);

                [Terrum.Interop.NativeComMethod(2, SignedHResult, AcquiresReference)]
                [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
                [Terrum.Interop.NativeOutput(1)]
                unsafe HRESULT Acquire(write ptr<ptr<IClock>> output);
            }

            int32 Main() { return 0; }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var catalog = compilation.Program!.NativeImportCatalog;
        Assert.That(catalog, Is.Not.Null);
        var definition = catalog!.ComInterfaces.Single();
        Assert.That(definition.NominalIdentity, Is.EqualTo("Example.Interop.IClock"));
        Assert.That(definition.SdkIid, Is.EqualTo(
            Guid.Parse("76e1b75f-286c-4571-a9cd-43aa5b64a381")));
        var method = catalog.ComMethods.Single(static method => method.BoundMethodName == "Read");
        Assert.That(method.Identity, Is.EqualTo("Example.Interop.IClock::Read"));
        Assert.That(method.Slot, Is.Zero);
        Assert.That(method.Signature, Is.EqualTo("HRESULT(Example.Interop.IClock*)"));
        Assert.That(method.Abi, Is.EqualTo("windows-x64-rcx-eax"));
        Assert.That(method.Effects, Is.EqualTo(NativeComEffects.SignedHResult));
        Assert.That(method.IsNoThrow, Is.True);
        var scale = catalog.ComMethods.Single(static method => method.BoundMethodName == "Scale");
        Assert.That(scale.Slot, Is.EqualTo(1));
        Assert.That(scale.Signature, Is.EqualTo("int32(Example.Interop.IClock*,int32*)"));
        Assert.That(scale.Abi, Is.EqualTo("windows-x64-rcx-rdx-eax"));
        Assert.That(scale.NativeParameters, Is.EqualTo([
            new NativeParameterDefinition(1, NativeParameterAccess.Read,
                NativeParameterRetention.NoEscape, 1),
        ]));
        var acquire = catalog.ComMethods.Single(static method => method.BoundMethodName == "Acquire");
        Assert.That(acquire.OutputParameterIndex, Is.EqualTo(1));
        Assert.That(acquire.OutputInterfaceIdentity, Is.EqualTo("Example.Interop.IClock"));
    }

    [Test]
    public void SourceAuthoredComMethodAcceptsBlittableAggregatePointer()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(SourceComAggregateParameter));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var method = compilation.Program!.Functions.Single(static function =>
            function.Name == "Example.Interop.RecordReaderOwner.IsCurrent");
        Assert.That(method.Variables[1].Type, Is.EqualTo(PrimitiveType.NativeStorageAddress));
    }

    [Test]
    public void SourceAuthoredComMethodRejectsNonBlittableAggregatePointer()
    {
        var source = SourceComAggregateParameter.Replace("[Blittable] ", "", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void SourceAuthoredComMethodRejectsUninitializedAggregatePointer()
    {
        var source = SourceComAggregateParameter.Replace(
            "RecordIdentity identity = new RecordIdentity(1, 2);",
            "mut RecordIdentity identity;", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void SourceAuthoredComAcquisitionCanFixItsInterfaceId()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(SourceComFixedInterfaceAcquisition));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var method = compilation.Program!.Functions.Single(static function =>
            function.Name == "Example.Interop.FactoryOwner.GetThing");
        Assert.That(method.Variables, Has.Length.EqualTo(2));
        Assert.That(method.NativeComHiddenParameters, Is.EqualTo([
            new NativeComHiddenParameter(1, NativeComHiddenParameterKind.UInt32),
            new NativeComHiddenParameter(2, NativeComHiddenParameterKind.NullPointer),
            new NativeComHiddenParameter(3, NativeComHiddenParameterKind.InterfaceIdAddress,
                InterfaceIdentity: "Example.Interop.IThing"),
        ]));
        Assert.That(NativeComCallContract.Acquisitions(
            compilation.Program.Functions.Single(static function =>
                function.Name == "Example.Interop.TryGetThing"),
            compilation.Program), Is.Not.Null);
        _ = NativeEmitter.EmitWithNativeImports(compilation.Program, NativeRuntimePolicy.FailFast);
    }

    [Test]
    public void SourceAuthoredComAcquisitionRejectsMismatchedFixedInterfaceId()
    {
        var source = SourceComFixedInterfaceAcquisition.Replace(
            "NativeComHiddenParameter(3, InterfaceIdAddress, \"IThing\")",
            "NativeComHiddenParameter(3, InterfaceIdAddress, \"IFactory\")",
            StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void SourceAuthoredComMethodRequiresUnsafeDeclaration()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeComInterface("76e1b75f-286c-4571-a9cd-43aa5b64a381")]
            interface IClock {
                [Terrum.Interop.NativeComMethod(0)]
                int32 Read();
            }

            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True);
    }

    [Test]
    public void SourceAuthoredComInterfacesSupportSparseSlotsAndInheritanceThroughProvisionalTrRm()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(SparseSourceComInterfaces));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var sourceInterfaces = compilation.Program!.NativeImportCatalog!.ComInterfaces;
        var clock = sourceInterfaces.Single(static definition =>
            definition.NominalIdentity == "Example.Interop.IClock");
        Assert.That(clock.FlattenedMethods, Is.EqualTo([
            "Example.Interop.IClock::Begin",
            "Example.Interop.IClock::Finish",
        ]));
        Assert.That(clock.FlattenedMethodSlots, Is.EqualTo([2, 5]));
        Assert.That(clock.EffectiveVTableSlotCount, Is.EqualTo(6));
        var preciseClock = sourceInterfaces.Single(static definition =>
            definition.NominalIdentity == "Example.Interop.IPreciseClock");
        Assert.That(preciseClock.FlattenedMethods, Is.EqualTo([
            "Example.Interop.IClock::Begin",
            "Example.Interop.IClock::Finish",
            "Example.Interop.IPreciseClock::Reset",
            "Example.Interop.IPreciseClock::Pause",
        ]));
        Assert.That(preciseClock.FlattenedMethodSlots, Is.EqualTo([2, 5, 6, 8]));
        Assert.That(preciseClock.EffectiveVTableSlotCount, Is.EqualTo(9));

        var artifact = TrRmExecutableCompiler.Emit(compilation.Program, "sparse-com-marker");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        var loadedClock = loaded.NativeImportCatalog!.ComInterfaces.Single(static definition =>
            definition.NominalIdentity == "Example.Interop.IClock");
        Assert.That(loadedClock.FlattenedMethodSlots, Is.EqualTo([2, 5]));
        var loadedPreciseClock = loaded.NativeImportCatalog.ComInterfaces.Single(static definition =>
            definition.NominalIdentity == "Example.Interop.IPreciseClock");
        Assert.That(loadedPreciseClock.FlattenedMethodSlots, Is.EqualTo([2, 5, 6, 8]));
        Assert.That(loaded.NativeImportCatalog.ComMethods.Single(static method =>
            method.Identity == "Example.Interop.IPreciseClock::Pause").Slot, Is.EqualTo(8));
    }

    [Test]
    public void SourceAuthoredComMethodRejectsDuplicateSparseSlots()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(SparseSourceComInterfaces.Replace(
            "[Terrum.Interop.NativeComMethod(6)]",
            "[Terrum.Interop.NativeComMethod(8)]", StringComparison.Ordinal)));

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void SourceAuthoredComMethodRejectsSlotsInsideInheritedVTableExtent()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(SparseSourceComInterfaces.Replace(
            "[Terrum.Interop.NativeComMethod(6)]",
            "[Terrum.Interop.NativeComMethod(5)]", StringComparison.Ordinal)));

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void SourceAuthoredComMethodRejectsUnrepresentableVTableOffsets()
    {
        var invalidSlotAttribute =
            $"[Terrum.Interop.NativeComMethod({int.MaxValue / NativeTargetProfile.PointerSize + 1})]";
        var compilation = Compilation.Create(SyntaxTree.Parse(SparseSourceComInterfaces.Replace(
            "[Terrum.Interop.NativeComMethod(6)]",
            invalidSlotAttribute, StringComparison.Ordinal)));

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void SourceAuthoredComMethodRequiresPointerAccessAndRetentionMetadata()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeComInterface("76e1b75f-286c-4571-a9cd-43aa5b64a381")]
            interface IClock {
                [Terrum.Interop.NativeComMethod(0)]
                unsafe int32 Read(ptr<int32> value);
            }

            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True);
    }
}
