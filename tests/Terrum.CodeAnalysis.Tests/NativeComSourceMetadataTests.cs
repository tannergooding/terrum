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
            Is.EqualTo(new NativeComParameterDefinition(1, NativeComParameterAccess.Read,
                NativeComParameterRetention.NoEscape, 1)));

        var reference = TrRmExecutableCompiler.CreateReference(artifact);
        var consumer = Compilation.Create(SyntaxTree.Parse("""
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
                if (TryCreateClock(out var clock)) {
                    unsafe { return clock.Read() ? 0 : -1; }
                }
                return -1;
            }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        Assert.That(consumer.Program!.NativeImportCatalog!.ComInterfaces.Single().NominalIdentity,
            Is.EqualTo("Example.Interop.IClock"));
        var image = NativeEmitter.EmitWithNativeImports(consumer.Program,
            NativeRuntimePolicy.FailFast);
        Assert.That(System.Text.Encoding.ASCII.GetString(image), Does.Contain("CreateClock"));

        var malformed = CorruptNativeComMethodSlot(artifact);
        Assert.Throws<InvalidDataException>(() => TrRmExecutableCompiler.Load(malformed));
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

    private static byte[] CorruptNativeComMethodSlot(byte[] artifact)
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
                        malformed.AsSpan(dataOffset + 8, sizeof(uint)), 5);
                    return malformed;
                }
            }
            break;
        }
        throw new InvalidOperationException("The emitted TRRM artifact has no zero-slot COM method clause.");
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
            new NativeComParameterDefinition(1, NativeComParameterAccess.Read,
                NativeComParameterRetention.NoEscape, 1),
        ]));
        var acquire = catalog.ComMethods.Single(static method => method.BoundMethodName == "Acquire");
        Assert.That(acquire.OutputParameterIndex, Is.EqualTo(1));
        Assert.That(acquire.OutputInterfaceIdentity, Is.EqualTo("Example.Interop.IClock"));
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
    public void SourceAuthoredComMethodRejectsSlotsOutsideDeclarationOrder()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeComInterface("76e1b75f-286c-4571-a9cd-43aa5b64a381")]
            interface IClock {
                [Terrum.Interop.NativeComMethod(1)]
                unsafe int32 Read();
            }

            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3030"), Is.True);
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
