// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeComCatalogTests
{
    [Fact]
    public void NativeComCatalogCanBeTestedWithoutASeparateImport()
    {
        var interfaceDefinition = new NativeComInterfaceDefinition(
            "IFixture", Guid.Parse("A3BFA1D9-0169-4B6C-9334-33D17A7CE9E7"), null,
            ["IFixture::Run"], ["IFixture::Run"]);
        var methodDefinition = new NativeComMethodDefinition(
            "fixture.iface.run", "IFixture", "IFixture", 0,
            "uint32(IFixture*)", "windows-x64-rcx-eax", NativeComEffects.None, IsCallable: true) {
            VTableMethodIdentity = "IFixture::Run",
        };

        var catalog = NativeImportCatalog.CreateForTesting(
            "test.generic-com.no-import.v1", [], [interfaceDefinition], [], [methodDefinition], []);

        Assert.True(catalog.TryGetComInterface("IFixture", out var selectedInterface));
        Assert.Same(interfaceDefinition, selectedInterface);
        Assert.True(catalog.TryGetComMethod(methodDefinition.Identity, out var selectedMethod));
        Assert.Same(methodDefinition, selectedMethod);
        Assert.False(NativeFixedArrayContract.MatchesCatalog(catalog));
    }

    [Fact]
    public void NativeComCatalogAcceptsAnUnrelatedInterfaceAndMethod()
    {
        var interfaceDefinition = new NativeComInterfaceDefinition(
            "IFixture", Guid.Parse("8C61A6B0-7A2C-4E44-A2C2-27A32687A1F1"), null,
            ["IFixture::Run"], ["IFixture::Run"]);
        var methodDefinition = new NativeComMethodDefinition(
            "fixture.iface.run", "IFixture", "IFixture", 0,
            "uint32(IFixture*)", "windows-x64-rcx-eax", NativeComEffects.None, IsCallable: true) {
            VTableMethodIdentity = "IFixture::Run",
        };
        var import = new NativeImportDefinition(
            "test.fixture.call", "fixture.dll", "Call", [PrimitiveType.UInt32], PrimitiveType.UInt32,
            IsNoThrow: true, RequiresUnsafeCaller: false, DoesNotReturn: false,
            NativeImportDeploymentPolicy.ApplicationDirectory);

        var catalog = NativeImportCatalog.CreateForTesting(
            "test.generic-com.v1", [import], [interfaceDefinition], [], [methodDefinition], []);

        Assert.True(catalog.TryGetComInterface("IFixture", out var selectedInterface));
        Assert.Same(interfaceDefinition, selectedInterface);
        Assert.True(catalog.TryGetComMethod("fixture.iface.run", out var selectedMethod));
        Assert.Same(methodDefinition, selectedMethod);
        var unrelatedInterface = new NativeComInterfaceDefinition(
            "IUnrelated", Guid.Parse("0B1846F5-71B2-4517-89E8-EE29BCF7A41B"), null,
            ["IUnrelated::Run"], ["IUnrelated::Run"]);
        Assert.False(NativeComCatalog.IsValid([interfaceDefinition, unrelatedInterface], [], [
            methodDefinition with {
                Signature = "uint32(IUnrelated*)",
            },
        ], [], [import]));
        Assert.False(NativeComCatalog.IsValid([interfaceDefinition], [], [
            methodDefinition with {
                Signature = "void(IFixture*,IFixture*)",
                Abi = "windows-x64-rcx-edx-void",
            },
        ], [], [import]));
        Assert.False(NativeComCatalog.IsValid([interfaceDefinition], [], [
            methodDefinition with {
                Signature = "double(IFixture*,double)",
                Abi = "windows-x64-rcx-rdx-eax",
            },
        ], [], [import]));
        Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test.com-output-address-without-metadata.v1", [import with {
                ParameterTypes = [PrimitiveType.NativeComInterfaceOutputAddress],
            }]));
        Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test.com-output-without-metadata.v1", [import with {
                ComOutputs = [new(99, "IUnknown", NativeComEffects.AcquiresReference)],
            }]));
        Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test.uninitialized-import-parameters.v1", [import with {
                ParameterTypes = default,
            }]));
    }

    [Fact]
    public void NativeComCatalogRequiresExplicitSignatureAndInterfaceAliases()
    {
        var interfaceDefinition = new NativeComInterfaceDefinition(
            "IFixture", Guid.Parse("28F945BD-58ED-44B3-BD71-3582117A0695"), null,
            ["IFixture::Run"], ["IFixture::Run"]) {
            SignatureAliases = ["IFixtureAlias"],
        };
        var method = new NativeComMethodDefinition(
            "fixture.iface.run", "IFixture", "IFixture", 0,
            "FixtureCode(IFixtureAlias*)", "windows-x64-rcx-eax",
            NativeComEffects.None, IsCallable: true) {
            VTableMethodIdentity = "IFixture::Run",
        };
        var signatureType = new NativeComSignatureTypeDefinition("FixtureCode", NativeComAbiValueKind.Unsigned32);
        var catalog = NativeImportCatalog.CreateForTesting(
            "test.generic-com-aliases.v1", [CreateImport()], [interfaceDefinition], [], [method], [],
            [signatureType]);

        Assert.True(catalog.TryGetComInterface("IFixture", out var selectedInterface));
        Assert.Same(interfaceDefinition, selectedInterface);

        var outputMethod = new NativeComMethodDefinition(
            "fixture.iface.create", "IFixture", "IFixture", 0,
            "HRESULT(IFixture*,IFixtureAlias**)", "windows-x64-rcx-rdx-eax",
            NativeComEffects.SignedHResult | NativeComEffects.AcquiresReference,
            IsCallable: true, OutputParameterIndex: 1, OutputInterfaceIdentity: "IFixture") {
            VTableMethodIdentity = "IFixture::Run",
        };
        Assert.True(NativeComCatalog.IsValid(
            [interfaceDefinition], [], [outputMethod], [], [CreateImport()], [], out _));
        var otherInterface = new NativeComInterfaceDefinition(
            "IOther", Guid.Parse("8DE87BA7-BC92-49E3-B31D-8E3EA62F4B92"), null,
            ["IOther::Run"], ["IOther::Run"]);
        Assert.False(NativeComCatalog.IsValid(
            [interfaceDefinition, otherInterface], [], [outputMethod with {
                OutputInterfaceIdentity = "IOther",
            }], [], [CreateImport()], [], out _));
    }

    [Fact]
    public void NativeComCatalogClassifiesFloatingPointArgumentsAndReturns()
    {
        var interfaceDefinition = new NativeComInterfaceDefinition(
            "IFixture", Guid.Parse("FB6418D8-DF3A-430E-AC71-D93317EE34D1"), null,
            ["IFixture::Run"], ["IFixture::Run"]);
        var method = new NativeComMethodDefinition(
            "fixture.iface.run", "IFixture", "IFixture", 0,
            "float(IFixture*,float,double)", "windows-x64-rcx-xmm1-xmm2-xmm0",
            NativeComEffects.None, IsCallable: true) {
            VTableMethodIdentity = "IFixture::Run",
        };

        Assert.True(NativeComCatalog.IsValid(
            [interfaceDefinition], [], [method], [], [CreateImport()]));
        Assert.False(NativeComCatalog.IsValid(
            [interfaceDefinition], [], [method with {
                Abi = "windows-x64-rcx-rdx-r8-xmm0",
            }], [], [CreateImport()]));
        Assert.False(NativeComCatalog.IsValid(
            [interfaceDefinition], [], [method with {
                Abi = "windows-x64-rcx-xmm1-xmm2-eax",
            }], [], [CreateImport()]));
    }

    [Fact]
    public void NativeComCatalogRejectsInconsistentSlotsAndInterfaceInheritance()
    {
        var baseInterface = new NativeComInterfaceDefinition(
            "IBase", Guid.Parse("E3DD92D9-6F95-4C98-A7C4-27FDD3D25F05"), null,
            ["IBase::Run"], ["IBase::Run"]);
        var derivedInterface = new NativeComInterfaceDefinition(
            "IDerived", Guid.Parse("A19BA8AD-8AC5-4AB3-ACB0-680821D8435F"), "IBase",
            ["IBase::Run", "IDerived::Close"], ["IDerived::Close"]);
        var method = new NativeComMethodDefinition(
            "fixture.iface.run", "IDerived", "IBase", 0,
            "uint32(IBase*)", "windows-x64-rcx-eax", NativeComEffects.None, IsCallable: true) {
            VTableMethodIdentity = "IBase::Run",
        };

        Assert.True(NativeComCatalog.IsValid([baseInterface, derivedInterface], [], [method], [],
            [CreateImport()]));
        Assert.False(NativeComCatalog.IsValid([baseInterface, derivedInterface], [], [
            method with { Slot = 1 },
        ], [], [CreateImport()]));
        Assert.False(NativeComCatalog.IsValid([baseInterface, derivedInterface with {
            FlattenedMethods = ["IDerived::Close", "IBase::Run"],
        }], [], [method], [], [CreateImport()]));
        Assert.False(NativeComCatalog.IsValid([baseInterface, derivedInterface], [], [
            method with { DeclaringInterfaceIdentity = "IDerived" },
        ], [], [CreateImport()]));
    }

    [Fact]
    public void NativeComCatalogRejectsConflictingSignaturesForOneVTableMethod()
    {
        var baseInterface = new NativeComInterfaceDefinition(
            "IBase", Guid.Parse("AF1D6EB7-1BA4-4DE3-8656-0A2C437711E1"), null,
            ["IBase::Run"], ["IBase::Run"]) {
            SignatureAliases = ["IBaseAlias"],
        };
        var derivedInterface = new NativeComInterfaceDefinition(
            "IDerived", Guid.Parse("DE6D2BFC-963B-4FB2-BB2D-4D93D1F3139C"), "IBase",
            ["IBase::Run"], []) {
            SignatureAliases = ["IDerivedAlias"],
        };
        var baseMethod = new NativeComMethodDefinition(
            "fixture.base.run", "IBase", "IBase", 0,
            "FixtureCode(IBaseAlias*,float)", "windows-x64-rcx-xmm1-eax",
            NativeComEffects.None, IsCallable: true) {
            VTableMethodIdentity = "IBase::Run",
        };
        var derivedMethod = new NativeComMethodDefinition(
            "fixture.derived.run", "IDerived", "IBase", 0,
            "uint32(IDerivedAlias*,float)", "windows-x64-rcx-xmm1-eax",
            NativeComEffects.None, IsCallable: true) {
            VTableMethodIdentity = "IBase::Run",
        };
        var signatureType = new NativeComSignatureTypeDefinition(
            "FixtureCode", NativeComAbiValueKind.Unsigned32);

        Assert.True(NativeComCatalog.IsValid(
            [baseInterface, derivedInterface], [], [baseMethod, derivedMethod], [], [], [signatureType], out _));
        Assert.False(NativeComCatalog.IsValid(
            [baseInterface, derivedInterface], [], [
                baseMethod,
                derivedMethod with { Signature = "uint32(IDerivedAlias*,double)" },
            ], [], [], [signatureType], out _));
    }

    [Fact]
    public void NativeComCatalogRequiresDirectlyPermittedPointerAdaptations()
    {
        var root = new NativeComInterfaceDefinition(
            "IRoot", Guid.Parse("B9EAC9D0-2742-49C1-9A39-201A938759B1"), null,
            ["IRoot::Run"], ["IRoot::Run"]);
        var baseInterface = new NativeComInterfaceDefinition(
            "IBase", Guid.Parse("64B8CE9F-9ABA-407C-97D0-1201BBE03295"), "IRoot",
            ["IRoot::Run", "IBase::Close"], ["IBase::Close"]);
        var derivedInterface = new NativeComInterfaceDefinition(
            "IDerived", Guid.Parse("A06D1E34-EA75-4E29-B987-8D9242F9ED23"), "IBase",
            ["IRoot::Run", "IBase::Close", "IDerived::Read"], ["IDerived::Read"]);
        var import = CreateImport() with {
            Key = "test.fixture.create-derived",
            ParameterTypes = [PrimitiveType.NativeComInterfaceOutputAddress],
            ParameterContracts = [NativeImportParameterContract.OwnedNativeComInterfaceOutput],
            ComOutputs = [new(0, "IDerived", NativeComEffects.AcquiresReference)],
        };
        var owner = new NativeComOwnerDefinition(
            "fixture.derived-owner", "IDerived", NativeComOwnerAcquisitionKind.NativeImportOutput,
            import.Key, 0, true, 8, 8);
        var method = new NativeComMethodDefinition(
            "fixture.iface.read", "IDerived", "IDerived", 2,
            "void(IDerived*,IBase*)", "windows-x64-rcx-rdx-void", NativeComEffects.None,
            IsCallable: true, ParameterAdaptations: [
                new(1, "IDerived*", "IBase*", NativeComParameterAdaptationKind.PointerPreservingBaseInterface),
            ]) {
            VTableMethodIdentity = "IDerived::Read",
            BorrowedOwnerParameters = [1],
        };

        Assert.True(NativeComCatalog.IsValid(
            [root, baseInterface, derivedInterface], [owner], [method], [("IDerived", "IBase")], [import]));
        Assert.False(NativeComCatalog.IsValid(
            [root, baseInterface, derivedInterface], [owner], [
                method with {
                    ParameterAdaptations = [
                        new(1, "IDerived*", "IRoot*",
                            NativeComParameterAdaptationKind.PointerPreservingBaseInterface),
                    ],
                },
            ], [("IDerived", "IBase"), ("IBase", "IRoot")], [import]));
        Assert.False(NativeComCatalog.IsValid(
            [root, baseInterface, derivedInterface], [owner], [
                method with { BorrowedOwnerParameters = [] },
            ], [("IDerived", "IBase")], [import]));
    }

    [Fact]
    public void NativeComCatalogValidatesOutputOwnersAgainstTheirProducer()
    {
        var interfaceDefinition = new NativeComInterfaceDefinition(
            "IFixture", Guid.Parse("F28160A1-9D82-4E7B-A8B0-31F4B98E3770"), null,
            ["IFixture::Run"], ["IFixture::Run"]);
        var otherInterface = new NativeComInterfaceDefinition(
            "IUnknown", Guid.Parse("5C983839-6317-4DC1-8773-A38F8204971E"), null,
            ["IUnknown::AddRef"], ["IUnknown::AddRef"]);
        var import = CreateImport() with {
            ParameterTypes = [PrimitiveType.NativeComInterfaceOutputAddress],
            ParameterContracts = [NativeImportParameterContract.OwnedNativeComInterfaceOutput],
            ComOutputs = [
                new(0, "IFixture", NativeComEffects.AcquiresReference),
            ],
        };
        var owner = new NativeComOwnerDefinition(
            "fixture.owner", "IFixture", NativeComOwnerAcquisitionKind.NativeImportOutput,
            import.Key, 0, true, 8, 8);

        var catalog = NativeImportCatalog.CreateForTesting(
            "test.generic-com-owner.v1", [import], [interfaceDefinition, otherInterface], [owner], [], []);

        Assert.True(catalog.TryGetComOwner("fixture.owner", out var selectedOwner));
        Assert.Same(owner, selectedOwner);
        Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test.generic-com-owner-bad-index.v1", [import], [interfaceDefinition], [
                owner with { OutputParameterIndex = 1 },
            ], [], []));
        Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test.generic-com-owner-ambiguous-interface.v1", [import], [interfaceDefinition], [
                owner,
                owner with { BoundOwnerName = "fixture.alternate-owner" },
            ], [], []));
        Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test.generic-com-owner-scalar-output.v1", [import with {
                ParameterTypes = [PrimitiveType.UInt32],
            }], [interfaceDefinition], [owner], [], []));
        Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test.generic-com-owner-bad-contract.v1", [import with {
                ParameterContracts = [NativeImportParameterContract.Value],
            }], [interfaceDefinition], [owner], [], []));
        Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test.generic-com-owner-bad-interface.v1", [import with {
                ComOutputs = [new(0, "IUnknown", NativeComEffects.AcquiresReference)],
            }], [interfaceDefinition, otherInterface], [owner], [], []));
        Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test.generic-com-owner-no-acquire.v1", [import with {
                ComOutputs = [new(0, "IFixture", NativeComEffects.None)],
            }], [interfaceDefinition], [owner], [], []));
    }

    [Fact]
    public void NativeComOwnerCleanupUsesCataloguedReleaseMetadata()
    {
        var interfaceDefinition = new NativeComInterfaceDefinition(
            "IFixture", Guid.Parse("FEA74457-2669-4519-A99C-E7351BC5F36C"), null,
            ["IFixture::Release"], ["IFixture::Release"]);
        var release = new NativeComMethodDefinition(
            "fixture.iface.release", "IFixture", "IFixture", 0,
            "ULONG(IFixture*)", "windows-x64-rcx-eax",
            NativeComEffects.ReleasesReference, IsCallable: true) {
            VTableMethodIdentity = "IFixture::Release",
        };
        var import = new NativeImportDefinition(
            "test.fixture.acquire", "fixture.dll", "Acquire",
            [PrimitiveType.NativeComInterfaceOutputAddress], PrimitiveType.Int32,
            IsNoThrow: true, RequiresUnsafeCaller: false, DoesNotReturn: false,
            NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [NativeImportParameterContract.OwnedNativeComInterfaceOutput],
            ComOutputs = [new(0, "IFixture", NativeComEffects.SignedHResult
                | NativeComEffects.AcquiresReference)],
        };
        var owner = new NativeComOwnerDefinition(
            "fixture.owner", "IFixture", NativeComOwnerAcquisitionKind.NativeImportOutput,
            import.Key, 0, true, 8, 8) {
            ReleaseMethodIdentity = release.Identity,
        };
        var catalog = NativeImportCatalog.CreateForTesting(
            "test.generic-com-owner-cleanup.v1", [import], [interfaceDefinition], [owner], [release], []);
        var program = new BoundProgram([], -1) {
            Structures = [
                new BoundStruct(owner.BoundOwnerName, [], owner.CellSize) {
                    IsUnique = true,
                    CleanupMethod = owner.BoundOwnerName + ".Close",
                },
            ],
            NativeImportCatalog = catalog,
        };
        var close = new BoundFunction(owner.BoundOwnerName + ".Close", PrimitiveType.Void, [
            new("this", (PrimitiveType)100, false, default),
        ], 1, new BoundBlock([], default), default) {
            DeclaringStruct = owner.BoundOwnerName,
            IsNoThrow = true,
            IsCleanup = true,
            Intrinsic = BoundIntrinsic.NativeComOwnerClose,
            NativeComMethod = release,
        };

        Assert.True(NativeComCallContract.IsValidOwnerCloseFunction(close, program));
        Assert.False(NativeComCallContract.IsValidOwnerCloseFunction(close with {
            NativeComMethod = release with { Effects = NativeComEffects.None },
        }, program));
        Assert.False(NativeComCallContract.IsValidOwnerCloseFunction(close with {
            Name = "fixture.other.Close",
        }, program));
        Assert.False(NativeComCallContract.IsValidOwnerCloseFunction(close with {
            Intrinsic = BoundIntrinsic.NativeComVTableCall,
        }, program));
        Assert.False(NativeComCatalog.IsValid(
            [interfaceDefinition], [owner with { ReleaseMethodIdentity = "fixture.missing.release" }],
            [release], [], [import]));
    }

    [Fact]
    public void GenericComImportOutputBindsSourceOwnerAndCleanup()
    {
        var interfaceDefinition = new NativeComInterfaceDefinition(
            "IFixture", Guid.Parse("62D20A28-2466-4B88-882D-E7F27A61E4BE"), null,
            ["IFixture::Release"], ["IFixture::Release"]);
        var release = new NativeComMethodDefinition(
            "fixture.iface.release", "IFixture", "IFixture", 0,
            "ULONG(IFixture*)", "windows-x64-rcx-eax",
            NativeComEffects.ReleasesReference, IsCallable: true) {
            VTableMethodIdentity = "IFixture::Release",
        };
        var import = new NativeImportDefinition(
            "test.fixture.acquire-owner", "fixture.dll", "Acquire",
            [PrimitiveType.NativeNullPointer, PrimitiveType.NativeComInterfaceOutputAddress], PrimitiveType.Int32,
            IsNoThrow: true, RequiresUnsafeCaller: false, DoesNotReturn: false,
            NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [NativeImportParameterContract.NullPointerOnly,
                NativeImportParameterContract.OwnedNativeComInterfaceOutput],
            ComOutputs = [new(1, "IFixture", NativeComEffects.SignedHResult
                | NativeComEffects.AcquiresReference | NativeComEffects.OutputMayBeWrittenOnFailure)],
        };
        var owner = new NativeComOwnerDefinition(
            "FixtureOwner", "IFixture", NativeComOwnerAcquisitionKind.NativeImportOutput,
            import.Key, 1, true, 8, 8) {
            ReleaseMethodIdentity = release.Identity,
        };
        var catalog = NativeImportCatalog.CreateForTesting(
            "test.generic-com-source-acquisition.v1", [import], [interfaceDefinition], [owner], [release], []);
        var source = """
            [Terrum.Interop.NativeImport("test.fixture.acquire-owner")]
            [NoThrow]
            extern int32 Acquire(Terrum.Interop.Windows.NativeNullPointer optional, out FixtureOwner owner);

            int32 Main() {
                if (Acquire(null, out var owner) < 0) {
                    return 1;
                }
                return 0;
            }
            """;

        var compilation = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(source)], catalog, BoundRuntimePolicy.FailFast);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var ownerStructure = Assert.Single(program.Structures,
            structure => structure.Name == owner.BoundOwnerName);
        Assert.True(ownerStructure.IsUnique);
        Assert.Equal(owner.CellSize, ownerStructure.Size);
        Assert.Equal(owner.BoundOwnerName + ".Close", ownerStructure.CleanupMethod);
        var close = Assert.Single(program.Functions,
            function => function.Name == owner.BoundOwnerName + ".Close");
        Assert.True(NativeComCallContract.IsValidOwnerCloseFunction(close, program));
        var main = Assert.Single(program.Functions, static function => function.Name == "Main");
        Assert.Single(NativeComCallContract.Acquisitions(main, program)!);

        _ = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
    }

    [Fact]
    public void NativeComOwnerCleanupAcceptsEverySelectedWindowsOwner()
    {
        var owners = NativeImportCatalog.WindowsX64.ComOwners;
        var structures = ImmutableArray.CreateBuilder<BoundStruct>(owners.Length);
        for (var index = 0; index < owners.Length; index++)
        {
            var owner = owners[index];
            structures.Add(new(owner.BoundOwnerName, [], owner.CellSize) {
                IsUnique = owner.IsUnique,
                CleanupMethod = owner.BoundOwnerName + ".Close",
            });
        }
        var program = new BoundProgram([], -1) {
            Structures = structures.ToImmutable(),
            NativeImportCatalog = NativeImportCatalog.WindowsX64,
        };

        for (var index = 0; index < owners.Length; index++)
        {
            var owner = owners[index];
            var close = new BoundFunction(owner.BoundOwnerName + ".Close", PrimitiveType.Void, [
                new("this", (PrimitiveType)(100 + index), false, default),
            ], 1, new BoundBlock([], default), default) {
                DeclaringStruct = owner.BoundOwnerName,
                IsNoThrow = true,
                IsCleanup = true,
                Intrinsic = BoundIntrinsic.NativeComOwnerClose,
                NativeComMethod = NativeImportCatalog.WindowsX64.ComMethods.Single(
                    method => method.Identity == owner.ReleaseMethodIdentity),
            };

            Assert.True(NativeComCallContract.IsValidOwnerCloseFunction(close, program),
                owner.BoundOwnerName);
        }
    }

    [Fact]
    public void NativeComCatalogValidatesBorrowedOwnerParameters()
    {
        var interfaceDefinition = new NativeComInterfaceDefinition(
            "IFixture", Guid.Parse("D0E38921-3D09-4F22-AB53-617FC3653F89"), null,
            ["IFixture::Use"], ["IFixture::Use"]);
        var otherInterface = new NativeComInterfaceDefinition(
            "IUnknown", Guid.Parse("5C983839-6317-4DC1-8773-A38F8204971E"), null,
            ["IUnknown::AddRef"], ["IUnknown::AddRef"]);
        var import = CreateImport() with {
            ParameterTypes = [PrimitiveType.NativeComInterfaceOutputAddress],
            ParameterContracts = [NativeImportParameterContract.OwnedNativeComInterfaceOutput],
            ComOutputs = [
                new(0, "IFixture", NativeComEffects.AcquiresReference),
            ],
        };
        var owner = new NativeComOwnerDefinition(
            "fixture.owner", "IFixture", NativeComOwnerAcquisitionKind.NativeImportOutput,
            import.Key, 0, true, 8, 8);
        var method = new NativeComMethodDefinition(
            "fixture.iface.use", "IFixture", "IFixture", 0,
            "void(IFixture*,IFixture*)", "windows-x64-rcx-rdx-void",
            NativeComEffects.None, IsCallable: true) {
            VTableMethodIdentity = "IFixture::Use",
            BorrowedOwnerParameters = [1],
        };

        var catalog = NativeImportCatalog.CreateForTesting(
            "test.generic-com-borrowed-owner.v1", [import], [interfaceDefinition, otherInterface],
            [owner], [method], []);

        Assert.True(catalog.TryGetComMethod(method.Identity, out var selectedMethod));
        Assert.Same(method, selectedMethod);
        var program = new BoundProgram([], -1) {
            Structures = [
                new BoundStruct("fixture.owner", [], 8) {
                    IsUnique = true,
                    CleanupMethod = "fixture.owner.Close",
                },
            ],
            NativeImportCatalog = catalog,
        };
        Assert.Same(interfaceDefinition, NativeComCallContract.LoanInterface(method, 1, owner, program));
        Assert.Throws<ArgumentException>(() => NativeComCallContract.LoanInterface(
            method with { BorrowedOwnerParameters = [] }, 1, owner, program));
        Assert.False(NativeComCatalog.IsValid([interfaceDefinition, otherInterface], [owner], [
            method with { BorrowedOwnerParameters = [2] },
        ], [], [import]));
        Assert.False(NativeComCatalog.IsValid([interfaceDefinition, otherInterface], [owner], [
            method with {
                Signature = "void(IFixture*,IUnknown*)",
                BorrowedOwnerParameters = [1],
            },
        ], [], [import]));
        Assert.False(NativeComCatalog.IsValid([interfaceDefinition, otherInterface], [owner], [
            method with { Signature = "void(IFixture*,IFixture**)" },
        ], [], [import]));
    }

    [Fact]
    public void NativeComCallContractMapsUnrelatedCataloguedMethodSignatures()
    {
        var interfaceDefinition = new NativeComInterfaceDefinition(
            "IFixture", Guid.Parse("9D07CB1F-D3F8-47E2-AF4E-5777D1FAAF97"), null,
            ["IFixture::RunRegisters", "IFixture::RunStack", "IFixture::GetFloat32", "IFixture::GetFloat64",
                "IFixture::RunAdapted", "IFixture::RunAggregate", "IFixture::RunAggregateOutput",
                "IFixture::RunValueOutputAddress"],
            ["IFixture::RunRegisters", "IFixture::RunStack", "IFixture::GetFloat32", "IFixture::GetFloat64",
                "IFixture::RunAdapted", "IFixture::RunAggregate", "IFixture::RunAggregateOutput",
                "IFixture::RunValueOutputAddress"]);
        var import = CreateImport() with {
            ParameterTypes = [PrimitiveType.NativeComInterfaceOutputAddress],
            ParameterContracts = [NativeImportParameterContract.OwnedNativeComInterfaceOutput],
            ComOutputs = [
                new(0, "IFixture", NativeComEffects.AcquiresReference),
            ],
        };
        var verificationImport = CreateImport() with {
            Key = "test.fixture.verification",
        };
        var owner = new NativeComOwnerDefinition(
            "fixture.owner", "IFixture", NativeComOwnerAcquisitionKind.NativeImportOutput,
            import.Key, 0, true, 8, 8);
        var registerMethod = new NativeComMethodDefinition(
            "fixture.iface.run-registers", "IFixture", "IFixture", 0,
            "uint32(IFixture*,float,uint32,double)",
            "windows-x64-rcx-xmm1-r8d-xmm3-eax", NativeComEffects.None, IsCallable: true) {
            VTableMethodIdentity = "IFixture::RunRegisters",
        };
        var stackMethod = new NativeComMethodDefinition(
            "fixture.iface.run-stack", "IFixture", "IFixture", 1,
            "uint32(IFixture*,float,uint32,double,float)",
            "windows-x64-rcx-xmm1-r8d-xmm3-stack32-eax", NativeComEffects.None, IsCallable: true) {
            VTableMethodIdentity = "IFixture::RunStack",
        };
        var float32Method = new NativeComMethodDefinition(
            "fixture.iface.get-float32", "IFixture", "IFixture", 2,
            "float(IFixture*)", "windows-x64-rcx-xmm0", NativeComEffects.None, IsCallable: true) {
            VTableMethodIdentity = "IFixture::GetFloat32",
        };
        var float64Method = new NativeComMethodDefinition(
            "fixture.iface.get-float64", "IFixture", "IFixture", 3,
            "double(IFixture*)", "windows-x64-rcx-xmm0", NativeComEffects.None, IsCallable: true) {
            VTableMethodIdentity = "IFixture::GetFloat64",
        };
        var adaptedMethod = new NativeComMethodDefinition(
            "fixture.iface.run-adapted", "IFixture", "IFixture", 4,
            "void(IFixture*,IFixture*const*,const UINT*,const float*,const float[4])",
            "windows-x64-rcx-rdx-r8-r9-stack32-void",
            NativeComEffects.None, IsCallable: true, ParameterAdaptations: [
                new(1, "IFixture*", "IFixture*const*",
                    NativeComParameterAdaptationKind.SingleElementOwnerPointerArray),
                new(2, "uint32", "const UINT*", NativeComParameterAdaptationKind.ValueAddress),
                new(3, "float", "const float*", NativeComParameterAdaptationKind.ValueAddress),
                new(4, "float", "const float[4]", NativeComParameterAdaptationKind.ValueArrayAddress),
            ]) {
            VTableMethodIdentity = "IFixture::RunAdapted",
            BorrowedOwnerParameters = [1],
        };
        var aggregateMethod = new NativeComMethodDefinition(
            "fixture.iface.run-aggregate", "IFixture", "IFixture", 5,
            "void(IFixture*,const fixture.aggregate*)",
            "windows-x64-rcx-rdx-void", NativeComEffects.None, IsCallable: true,
            ParameterAdaptations: [
                new(1, "fixture.aggregate", "const fixture.aggregate*",
                    NativeComParameterAdaptationKind.AggregateAddress, 12, 4),
            ]) {
            VTableMethodIdentity = "IFixture::RunAggregate",
        };
        var aggregateOutputMethod = new NativeComMethodDefinition(
            "fixture.iface.run-aggregate-output", "IFixture", "IFixture", 6,
            "void(IFixture*,fixture.aggregate*)",
            "windows-x64-rcx-rdx-void", NativeComEffects.None, IsCallable: true,
            ParameterAdaptations: [
                new(1, "fixture.aggregate", "fixture.aggregate*",
                    NativeComParameterAdaptationKind.AggregateStorageAddress, 12, 4, NoEscape: true),
            ]) {
            VTableMethodIdentity = "IFixture::RunAggregateOutput",
        };
        var valueOutputAddressMethod = new NativeComMethodDefinition(
            "fixture.iface.run-value-output-address", "IFixture", "IFixture", 7,
            "void(IFixture*,UINT*)", "windows-x64-rcx-rdx-void", NativeComEffects.None,
            IsCallable: true, ParameterAdaptations: [
                new(1, "uint32", "UINT*", NativeComParameterAdaptationKind.ValueOutputAddress,
                    NoEscape: true),
            ]) {
            VTableMethodIdentity = "IFixture::RunValueOutputAddress",
        };
        Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test.generic-com-call.invalid-aggregate-layout.v1", [import, verificationImport],
            [interfaceDefinition], [owner], [
                registerMethod, stackMethod, float32Method, float64Method, adaptedMethod,
                aggregateMethod with {
                    ParameterAdaptations = [
                        new(1, "fixture.aggregate", "const fixture.aggregate*",
                            NativeComParameterAdaptationKind.AggregateAddress, 12, 3),
                    ],
                },
            ], []));
        Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test.generic-com-call.invalid-aggregate-storage-noescape.v1", [import, verificationImport],
            [interfaceDefinition], [owner], [
                registerMethod, stackMethod, float32Method, float64Method, adaptedMethod, aggregateMethod,
                aggregateOutputMethod with {
                    ParameterAdaptations = [
                        new(1, "fixture.aggregate", "fixture.aggregate*",
                            NativeComParameterAdaptationKind.AggregateStorageAddress, 12, 4),
                    ],
                },
            ], []));
        Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test.generic-com-call.invalid-value-output-address-noescape.v1",
            [import, verificationImport], [interfaceDefinition], [owner], [
                registerMethod, stackMethod, float32Method, float64Method, adaptedMethod,
                aggregateMethod, aggregateOutputMethod,
                valueOutputAddressMethod with {
                    ParameterAdaptations = [
                        new(1, "uint32", "UINT*", NativeComParameterAdaptationKind.ValueOutputAddress),
                    ],
                },
            ], []));
        Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test.generic-com-call.invalid-value-output-address.v1",
            [import, verificationImport], [interfaceDefinition], [owner], [
                registerMethod, stackMethod, float32Method, float64Method, adaptedMethod,
                aggregateMethod, aggregateOutputMethod,
                valueOutputAddressMethod with {
                    ParameterAdaptations = [
                        new(1, "uint32", "const UINT*", NativeComParameterAdaptationKind.ValueOutputAddress,
                            NoEscape: true),
                    ],
                },
            ], []));
        var catalog = NativeImportCatalog.CreateForTesting(
            "test.generic-com-call.v1", [import, verificationImport], [interfaceDefinition], [owner],
            [registerMethod, stackMethod, float32Method, float64Method, adaptedMethod,
                aggregateMethod, aggregateOutputMethod, valueOutputAddressMethod], []);
        Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test.generic-com-call.invalid-value-address.v1", [import, verificationImport],
            [interfaceDefinition], [owner], [
                registerMethod, stackMethod, float32Method, float64Method,
                adaptedMethod with {
                    ParameterAdaptations = adaptedMethod.ParameterAdaptations.SetItem(2,
                        new(2, "uint32", "const float*", NativeComParameterAdaptationKind.ValueAddress)),
                },
            ], []));
        var ownerType = (PrimitiveType)100;
        var registerFunction = new BoundFunction("fixture.owner.RunRegisters", PrimitiveType.UInt32, [
            new("this", ownerType, false, default),
            new("floatingRegisterArgument", PrimitiveType.Float32, false, default),
            new("integerRegisterArgument", PrimitiveType.UInt32, false, default),
            new("wideFloatingRegisterArgument", PrimitiveType.Float64, false, default),
        ], 4, new BoundBlock([], default), default) {
            DeclaringStruct = "fixture.owner",
            IsInternal = true,
            IsNoThrow = true,
            Intrinsic = BoundIntrinsic.NativeComVTableCall,
            NativeComMethod = registerMethod,
        };
        var stackFunction = new BoundFunction("fixture.owner.RunStack", PrimitiveType.UInt32, [
            new("this", ownerType, false, default),
            new("floatingRegisterArgument", PrimitiveType.Float32, false, default),
            new("integerRegisterArgument", PrimitiveType.UInt32, false, default),
            new("wideFloatingRegisterArgument", PrimitiveType.Float64, false, default),
            new("floatingStackArgument", PrimitiveType.Float32, false, default),
        ], 5, new BoundBlock([], default), default) {
            DeclaringStruct = "fixture.owner",
            IsInternal = true,
            IsNoThrow = true,
            Intrinsic = BoundIntrinsic.NativeComVTableCall,
            NativeComMethod = stackMethod,
        };
        var float32Function = new BoundFunction("fixture.owner.GetFloat32", PrimitiveType.Float32, [
            new("this", ownerType, false, default),
        ], 1, new BoundBlock([], default), default) {
            DeclaringStruct = "fixture.owner",
            IsInternal = true,
            IsNoThrow = true,
            Intrinsic = BoundIntrinsic.NativeComVTableCall,
            NativeComMethod = float32Method,
        };
        var float64Function = new BoundFunction("fixture.owner.GetFloat64", PrimitiveType.Float64, [
            new("this", ownerType, false, default),
        ], 1, new BoundBlock([], default), default) {
            DeclaringStruct = "fixture.owner",
            IsInternal = true,
            IsNoThrow = true,
            Intrinsic = BoundIntrinsic.NativeComVTableCall,
            NativeComMethod = float64Method,
        };
        var adaptedFunction = new BoundFunction("fixture.owner.RunAdapted", PrimitiveType.Void, [
            new("this", ownerType, false, default),
            new("element", ownerType, false, default),
            new("value", PrimitiveType.UInt32, false, default),
            new("ratio", PrimitiveType.Float32, false, default),
            new("red", PrimitiveType.Float32, false, default),
            new("green", PrimitiveType.Float32, false, default),
            new("blue", PrimitiveType.Float32, false, default),
            new("alpha", PrimitiveType.Float32, false, default),
        ], 8, new BoundBlock([], default), default) {
            DeclaringStruct = "fixture.owner",
            IsInternal = true,
            IsNoThrow = true,
            Intrinsic = BoundIntrinsic.NativeComVTableCall,
            NativeComMethod = adaptedMethod,
            NativeComParameterMappings = [
                new(4, 4),
                new(5, 4),
                new(6, 4),
                new(7, 4),
            ],
        };
        var aggregateType = (PrimitiveType)101;
        var aggregateFunction = new BoundFunction("fixture.owner.RunAggregate", PrimitiveType.Void, [
            new("this", ownerType, false, default),
            new("value", aggregateType, false, default),
        ], 2, new BoundBlock([], default), default) {
            DeclaringStruct = "fixture.owner",
            IsInternal = true,
            IsNoThrow = true,
            Intrinsic = BoundIntrinsic.NativeComVTableCall,
            NativeComMethod = aggregateMethod,
        };
        var closeFunction = new BoundFunction("fixture.owner.Close", PrimitiveType.Void, [
            new("this", ownerType, false, default),
        ], 1, new BoundBlock([], default), default) {
            DeclaringStruct = "fixture.owner",
            IsCleanup = true,
            IsNoThrow = true,
        };
        var verificationImportFunction = new BoundFunction("fixture.Verify", PrimitiveType.UInt32, [
            new("value", PrimitiveType.UInt32, false, default),
        ], 1, new BoundBlock([], default), default) {
            IsExternal = true,
            IsNoThrow = true,
            NativeImportKey = verificationImport.Key,
        };
        var program = new BoundProgram([
            registerFunction, stackFunction, float32Function, float64Function, adaptedFunction,
            aggregateFunction, verificationImportFunction, closeFunction,
        ], -1) {
            Structures = [
                new BoundStruct("fixture.owner", [], 8) {
                    IsUnique = true,
                    CleanupMethod = "fixture.owner.Close",
                },
                new("fixture.aggregate", [
                    new("X", PrimitiveType.Float32, 0, 4),
                    new("Y", PrimitiveType.Float32, 4, 4),
                    new("Z", PrimitiveType.Float32, 8, 4),
                ], 12),
            ],
            NativeImportCatalog = catalog,
            NativeImportCatalogId = catalog.Identity,
        };

        Assert.True(NativeComCallContract.IsValidVTableCallFunction(registerFunction, program));
        Assert.True(NativeComCallContract.IsValidVTableCallFunction(stackFunction, program));
        Assert.True(NativeComCallContract.IsValidVTableCallFunction(float32Function, program));
        Assert.True(NativeComCallContract.IsValidVTableCallFunction(float64Function, program));
        Assert.True(NativeComCallContract.IsValidVTableCallFunction(adaptedFunction, program));
        Assert.True(NativeComCallContract.IsValidVTableCallFunction(aggregateFunction, program));
        var aggregateOutputFunctions = NativeComCallContract.DirectMethodFunctions(catalog,
            program.Structures, new HashSet<string>(StringComparer.Ordinal) { "RunAggregateOutput" });
        var aggregateOutputFunction = Assert.Single(aggregateOutputFunctions);
        Assert.Equal(PrimitiveType.NativeStorageAddress,
            aggregateOutputFunction.Variables[1].Type);
        Assert.True(NativeComCallContract.IsValidVTableCallFunction(aggregateOutputFunction, program));
        var valueOutputFunctions = NativeComCallContract.DirectMethodFunctions(catalog,
            program.Structures, new HashSet<string>(StringComparer.Ordinal) { "RunValueOutputAddress" });
        var valueOutputFunction = Assert.Single(valueOutputFunctions);
        Assert.Equal(PrimitiveType.NativeStorageAddress, valueOutputFunction.Variables[1].Type);
        Assert.False(valueOutputFunction.Variables[1].IsOut);
        Assert.True(NativeComCallContract.IsValidVTableCallFunction(valueOutputFunction, program));
        Assert.False(NativeComCallContract.IsValidVTableCallFunction(valueOutputFunction with {
            Variables = valueOutputFunction.Variables.SetItem(1,
                valueOutputFunction.Variables[1] with { IsOut = true }),
        }, program));
        Assert.False(NativeComCallContract.IsValidVTableCallFunction(valueOutputFunction with {
            Variables = valueOutputFunction.Variables.SetItem(1,
                valueOutputFunction.Variables[1] with { Type = PrimitiveType.UInt32 }),
        }, program));
        var aggregateStorageAddress = new BoundNativeStorageAddress(0,
            new BoundConstant(0, PrimitiveType.Int32, default), aggregateType, aggregateType,
            PrimitiveType.NativeStorageAddress, 1, 12, 12, IsReadonly: false, default);
        Assert.True(NativeStorageParameterContract.SupportsAggregateType(catalog, aggregateType,
            program.Structures, isReadonly: false));
        Assert.True(NativeStorageParameterContract.MatchesAggregateAddress(aggregateStorageAddress,
            aggregateOutputMethod.ParameterAdaptations[0], program));
        Assert.False(NativeStorageParameterContract.MatchesAggregateAddress(
            aggregateStorageAddress with { ByteExtent = 8 }, aggregateOutputMethod.ParameterAdaptations[0],
            program));
        Assert.False(NativeStorageParameterContract.MatchesAggregateAddress(
            aggregateStorageAddress with { IsReadonly = true }, aggregateOutputMethod.ParameterAdaptations[0],
            program));
        Assert.False(NativeComCallContract.IsValidVTableCallFunction(aggregateFunction, program with {
            Structures = program.Structures.SetItem(1, program.Structures[1] with { Size = 8 }),
        }));
        Assert.False(NativeComCallContract.IsValidVTableCallFunction(aggregateFunction, program with {
            Structures = program.Structures.SetItem(1, program.Structures[1] with { IsUnique = true }),
        }));
        Assert.False(NativeComCallContract.IsValidVTableCallFunction(adaptedFunction with {
            Variables = adaptedFunction.Variables.SetItem(2,
                adaptedFunction.Variables[2] with { Type = PrimitiveType.Int32 }),
        }, program));
        Assert.False(NativeComCallContract.IsValidVTableCallFunction(adaptedFunction with {
            Variables = adaptedFunction.Variables.SetItem(3,
                adaptedFunction.Variables[3] with { Type = PrimitiveType.Float64 }),
        }, program));
        Assert.False(NativeComCallContract.IsValidVTableCallFunction(adaptedFunction with {
            NativeComParameterMappings = [
                new(4, 4),
                new(5, 4),
                new(6, 4),
            ],
        }, program));
        Assert.Equal(8, ValueLayout.Alignment(ownerType, program.Structures, program.NativeImportCatalog));
        SemanticVerifier.VerifyLibraryReferences(program);
        Assert.False(NativeComCallContract.IsValidVTableCallFunction(stackFunction with {
            Variables = stackFunction.Variables.SetItem(4,
                new BoundVariable("floatingStackArgument", PrimitiveType.Float64, false, default)),
        }, program));
        var outputProgram = program with {
            Functions = program.Functions.Add(aggregateOutputFunction).Add(valueOutputFunction),
        };
        SemanticVerifier.VerifyLibraryReferences(outputProgram);
        var code = new PeCompiler(outputProgram, default, managedNodes: false, library: true).Compile();
        var registerRange = Assert.Single(code.Functions,
            range => range.Start == code.SourceFunctionOffsets[0]);
        var stackRange = Assert.Single(code.Functions,
            range => range.Start == code.SourceFunctionOffsets[1]);
        Assert.Equal(registerRange.StackSize + 32, stackRange.StackSize);
        Assert.True(code.Code.AsSpan(registerRange.Start, registerRange.End - registerRange.Start)
            .IndexOf(new byte[] { 0x66, 0x0F, 0x6E, 0xC8 }) >= 0);
        Assert.True(code.Code.AsSpan(registerRange.Start, registerRange.End - registerRange.Start)
            .IndexOf(new byte[] { 0x66, 0x48, 0x0F, 0x6E, 0xD8 }) >= 0);
        Assert.True(code.Code.AsSpan(stackRange.Start, stackRange.End - stackRange.Start)
            .IndexOf(new byte[] { 0x48, 0x89, 0x84, 0x24, 0x20, 0, 0, 0 }) >= 0);
        var adaptedRange = Assert.Single(code.Functions,
            range => range.Start == code.SourceFunctionOffsets[4]);
        var adaptedBytes = code.Code.AsSpan(adaptedRange.Start, adaptedRange.End - adaptedRange.Start);
        Assert.True(adaptedBytes.IndexOf(new byte[] { 0x48, 0x89, 0xC2 }) >= 0);
        var aggregateOutputRange = Assert.Single(code.Functions,
            range => range.Start == code.SourceFunctionOffsets[^2]);
        var aggregateOutputBytes = code.Code.AsSpan(aggregateOutputRange.Start,
            aggregateOutputRange.End - aggregateOutputRange.Start);
        Assert.True(aggregateOutputBytes.IndexOf(
            new byte[] { 0x48, 0x8B, 0x01, 0xFF, 0x50, 48 }) >= 0);
        var valueOutputRange = Assert.Single(code.Functions,
            range => range.Start == code.SourceFunctionOffsets[^1]);
        var valueOutputBytes = code.Code.AsSpan(valueOutputRange.Start,
            valueOutputRange.End - valueOutputRange.Start);
        Assert.True(valueOutputBytes.IndexOf(
            new byte[] { 0x48, 0x8B, 0x01, 0xFF, 0x50, 56 }) >= 0);
        Assert.True(valueOutputBytes.IndexOf(new byte[] { 0x48, 0x8B, 0x85 }) >= 0);
        Assert.Equal(-1, valueOutputBytes.IndexOf(new byte[] { 0x48, 0x8D, 0x85 }));
        Assert.True(adaptedBytes.IndexOf(new byte[] { 0x49, 0x89, 0xC0 }) >= 0);
        Assert.True(adaptedBytes.IndexOf(new byte[] { 0x49, 0x89, 0xC1 }) >= 0);
        Assert.True(adaptedBytes.IndexOf(new byte[] { 0xFF, 0x50, 0x20 }) >= 0);
        Assert.True(adaptedBytes.IndexOf(new byte[] { 0x48, 0x8D, 0x85 }) >= 0);
        var aggregateRange = Assert.Single(code.Functions,
            range => range.Start == code.SourceFunctionOffsets[5]);
        var aggregateBytes = code.Code.AsSpan(aggregateRange.Start, aggregateRange.End - aggregateRange.Start);
        Assert.True(aggregateBytes.IndexOf(new byte[] { 0x48, 0x8D, 0x85 }) >= 0);
        AssertFloatingReturn(code, functionIndex: 2, vtableSlot: 2, wide: false);
        AssertFloatingReturn(code, functionIndex: 3, vtableSlot: 3, wide: true);
    }

    [Fact]
    public void NativeComCatalogBoundsOutputAndAdaptationMetadataBySignature()
    {
        var interfaceDefinition = new NativeComInterfaceDefinition(
            "IFixture", Guid.Parse("5C983839-6317-4DC1-8773-A38F8204971E"), null,
            ["IFixture::Create"], ["IFixture::Create"]);
        var method = new NativeComMethodDefinition(
            "fixture.iface.create", "IFixture", "IFixture", 0,
            "HRESULT(IFixture*,IFixture**)", "windows-x64-rcx-rdx-eax",
            NativeComEffects.SignedHResult | NativeComEffects.AcquiresReference,
            IsCallable: true, OutputParameterIndex: 1, OutputInterfaceIdentity: "IFixture") {
            VTableMethodIdentity = "IFixture::Create",
        };

        Assert.True(NativeComCatalog.IsValid(
            [interfaceDefinition], [], [method], [], [CreateImport()]));
        Assert.False(NativeComCatalog.IsValid(
            [interfaceDefinition], [], [method with { OutputParameterIndex = 2 }], [], [CreateImport()]));
        Assert.False(NativeComCatalog.IsValid(
            [interfaceDefinition], [], [method with { Abi = "windows-x64-rcx-eax" }], [], [CreateImport()]));
        Assert.False(NativeComCatalog.IsValid(
            [interfaceDefinition], [], [method with { Abi = "windows-x64-rdx-rcx-eax" }], [], [CreateImport()]));

        var owner = new NativeComOwnerDefinition(
            "fixture.method-owner", "IFixture", NativeComOwnerAcquisitionKind.ComMethodOutput,
            method.Identity, 1, true, 8, 8);
        var otherInterface = new NativeComInterfaceDefinition(
            "IOther", Guid.Parse("E8FD8082-EE73-4D76-A06D-20F6C0E93A76"), null, ["IOther::Run"], ["IOther::Run"]);
        Assert.True(NativeComCatalog.IsValid(
            [interfaceDefinition, otherInterface], [owner], [method], [], [CreateImport()]));
        Assert.False(NativeComCatalog.IsValid(
            [interfaceDefinition, otherInterface], [owner with { OutputParameterIndex = 0 }], [method], [], [CreateImport()]));
        Assert.False(NativeComCatalog.IsValid(
            [interfaceDefinition, otherInterface], [owner with { InterfaceIdentity = "IOther" }], [method], [], [CreateImport()]));
        Assert.False(NativeComCatalog.IsValid(
            [interfaceDefinition, otherInterface], [owner], [method with {
                Effects = NativeComEffects.SignedHResult,
            }], [], [CreateImport()]));
    }

    [Fact]
    public void GenericComCatalogGeneratesDirectScalarMethodFunctions()
    {
        var interfaceDefinition = new NativeComInterfaceDefinition(
            "IFixture", Guid.Parse("0C85C73F-8C35-4428-B0E0-50B68222375C"), null,
            ["IFixture::Release", "IFixture::Resize"],
            ["IFixture::Release", "IFixture::Resize"]);
        var acquisitionImport = CreateImport() with {
            Key = "test.fixture.direct-method-acquisition",
            ParameterTypes = [PrimitiveType.NativeComInterfaceOutputAddress],
            ParameterContracts = [NativeImportParameterContract.OwnedNativeComInterfaceOutput],
            ComOutputs = [new(0, "IFixture", NativeComEffects.AcquiresReference)],
        };
        var releaseMethod = new NativeComMethodDefinition(
            "fixture.iface.release", "IFixture", "IFixture", 0,
            "ULONG(IFixture*)", "windows-x64-rcx-eax",
            NativeComEffects.ReleasesReference, IsCallable: true) {
            VTableMethodIdentity = "IFixture::Release",
        };
        var resizeMethod = new NativeComMethodDefinition(
            "fixture.iface.resize", "IFixture", "IFixture", 1,
            "HRESULT(IFixture*,UINT,UINT)", "windows-x64-rcx-edx-r8d-eax",
            NativeComEffects.SignedHResult, IsCallable: true) {
            VTableMethodIdentity = "IFixture::Resize",
        };
        var owner = new NativeComOwnerDefinition(
            "fixture.owner", "IFixture", NativeComOwnerAcquisitionKind.NativeImportOutput,
            acquisitionImport.Key, 0, true, 8, 8) {
            ReleaseMethodIdentity = releaseMethod.Identity,
        };
        var catalog = NativeImportCatalog.CreateForTesting(
            "test.generic-com-direct-method.v1", [acquisitionImport], [interfaceDefinition], [owner],
            [releaseMethod, resizeMethod], []);
        var ownerStructure = new BoundStruct(owner.BoundOwnerName, [], 8) {
            IsUnique = true,
            CleanupMethod = owner.BoundOwnerName + ".Close",
        };
        var functions = NativeComCallContract.DirectMethodFunctions(catalog, [ownerStructure]);

        var close = Assert.Single(functions, static candidate => candidate.IsCleanup);
        Assert.True(NativeComCallContract.IsValidOwnerCloseFunction(close,
            new BoundProgram(functions, -1) {
                Structures = [ownerStructure],
                NativeImportCatalogId = catalog.Identity,
                NativeImportCatalog = catalog,
            }));
        var function = Assert.Single(functions, static candidate => !candidate.IsCleanup);
        Assert.Equal(owner.BoundOwnerName + ".Resize", function.Name);
        Assert.Equal(PrimitiveType.Bool, function.ReturnType);
        Assert.Equal(PrimitiveType.UInt32, function.Variables[1].Type);
        Assert.Equal(PrimitiveType.UInt32, function.Variables[2].Type);
        Assert.True(NativeComCallContract.IsValidVTableCallFunction(function,
            new BoundProgram(functions, -1) {
                Structures = [ownerStructure],
                NativeImportCatalogId = catalog.Identity,
                NativeImportCatalog = catalog,
            }));
    }

    [Fact]
    public void GenericComCatalogMapsCataloguedStorageRecordLoans()
    {
            const string recordType = "fixture.storage.Record";
            const string loanGroup = "record-backing";
            var interfaceDefinition = new NativeComInterfaceDefinition(
                "IFixture", Guid.Parse("740BFE34-D3E8-4E81-BE60-BC3E30A6C2D1"), null,
                ["IFixture::Release", "IFixture::Observe"], ["IFixture::Release", "IFixture::Observe"]);
            var acquisitionImport = CreateImport() with {
                Key = "test.fixture.storage-record-acquisition",
                ParameterTypes = [PrimitiveType.NativeComInterfaceOutputAddress],
                ParameterContracts = [NativeImportParameterContract.OwnedNativeComInterfaceOutput],
                ComOutputs = [new(0, "IFixture", NativeComEffects.AcquiresReference)],
            };
            var owner = new NativeComOwnerDefinition(
                "fixture.owner", "IFixture", NativeComOwnerAcquisitionKind.NativeImportOutput,
                acquisitionImport.Key, 0, true, 8, 8) {
                ReleaseMethodIdentity = "fixture.iface.release",
            };
            var releaseMethod = new NativeComMethodDefinition(
                "fixture.iface.release", "IFixture", "IFixture", 0,
                "ULONG(IFixture*)", "windows-x64-rcx-eax",
                NativeComEffects.ReleasesReference, IsCallable: true) {
                VTableMethodIdentity = "IFixture::Release",
            };
            var observeMethod = new NativeComMethodDefinition(
                "fixture.iface.observe", "IFixture", "IFixture", 1,
                "void(IFixture*,const FIXTURE_RECORD*,const FIXTURE_RECORD*)",
                "windows-x64-rcx-rdx-r8-void", NativeComEffects.None, IsCallable: true) {
                VTableMethodIdentity = "IFixture::Observe",
                StorageRecordParameters = [
                    new(1, recordType, true, loanGroup, NoEscape: true,
                        SignatureTypeName: "FIXTURE_RECORD"),
                    new(2, recordType, true, loanGroup, NoEscape: true,
                        SignatureTypeName: "FIXTURE_RECORD"),
                ],
            };
            var layout = new NativeStorageRecordLayoutDefinition(
                recordType, "fixture.record", 16, 8, [
                    new("Data", PrimitiveType.NativeStorageAddress, 0, 8),
                    new("ByteExtent", PrimitiveType.UInt32, 8, 4, MinimumValue: 1, MaximumValue: 16),
                ], BackingPointerFieldIndex: 0, BackingExtentFieldIndex: 1);
            var catalog = NativeImportCatalog.CreateForTesting(
                "test.generic-com-storage-record.v1", [acquisitionImport], [interfaceDefinition], [owner],
                [releaseMethod, observeMethod], [], storageRecordLayouts: [layout]);
            var ownerStructure = new BoundStruct(owner.BoundOwnerName, [], 8) {
                IsUnique = true,
                CleanupMethod = owner.BoundOwnerName + ".Close",
            };
            var program = new BoundProgram([], -1) {
                Structures = [ownerStructure],
                NativeImportCatalogId = catalog.Identity,
                NativeImportCatalog = catalog,
            };

            var observeFunction = Assert.Single(NativeComCallContract.DirectMethodFunctions(
                catalog, [ownerStructure]), static function => !function.IsCleanup);
            Assert.Equal(PrimitiveType.NativeStorageAddress, observeFunction.Variables[1].Type);
            Assert.Equal(PrimitiveType.NativeStorageAddress, observeFunction.Variables[2].Type);
            Assert.True(NativeComCallContract.IsValidVTableCallFunction(observeFunction, program));
            Assert.True(NativeStorageRecordContract.TryGetParameter(observeMethod, 1,
                out var firstStorageRecordParameter));
            Assert.True(NativeStorageRecordContract.TryGetParameter(observeMethod, 2,
                out var secondStorageRecordParameter));
            Assert.NotNull(firstStorageRecordParameter);
            Assert.NotNull(secondStorageRecordParameter);
            Assert.Equal(loanGroup, firstStorageRecordParameter.LoanGroup);
            Assert.Equal(loanGroup, secondStorageRecordParameter.LoanGroup);
            Assert.True(NativeStorageRecordContract.IsValidComParameter(firstStorageRecordParameter,
                observeMethod, catalog.StorageRecordLayouts));
            Assert.True(NativeStorageRecordContract.IsValidComParameter(secondStorageRecordParameter,
                observeMethod, catalog.StorageRecordLayouts));
            var recordAddress = new BoundNativeStorageAddress(0,
                new BoundConstant(0, PrimitiveType.Int32, default), PrimitiveType.NativeStorageRecord,
                PrimitiveType.NativeStorageRecord, PrimitiveType.NativeStorageAddress, 1,
                layout.ByteExtent, layout.ByteExtent, IsReadonly: true, Span: default);
            Assert.True(NativeStorageRecordContract.MatchesAddress(
                recordAddress, firstStorageRecordParameter, catalog));
            Assert.False(NativeStorageRecordContract.MatchesAddress(
                recordAddress with { ByteExtent = layout.ByteExtent - sizeof(uint) },
                firstStorageRecordParameter, catalog));
            Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
                "test.generic-com-storage-record.invalid-signature.v1", [acquisitionImport],
                [interfaceDefinition], [owner], [
                    releaseMethod,
                    observeMethod with {
                        StorageRecordParameters = observeMethod.StorageRecordParameters.SetItem(0,
                            observeMethod.StorageRecordParameters[0] with {
                                SignatureTypeName = "OTHER_RECORD",
                            }),
                    },
                ], [], storageRecordLayouts: [layout]));
        Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
                "test.generic-com-storage-record.invalid-noescape.v1", [acquisitionImport],
                [interfaceDefinition], [owner], [
                    releaseMethod,
                    observeMethod with {
                        StorageRecordParameters = observeMethod.StorageRecordParameters.SetItem(0,
                            observeMethod.StorageRecordParameters[0] with { NoEscape = false }),
                    },
                ], [], storageRecordLayouts: [layout]));
    }

    [Fact]
    public void NativeComOwnerTryCloneUsesCataloguedReferenceAcquisition()
    {
        var interfaceDefinition = new NativeComInterfaceDefinition(
            "IFixture", Guid.Parse("1510346B-1DFB-471F-A567-8F192345A1DF"), null,
            ["IFixture::AddRef", "IFixture::Release"],
            ["IFixture::AddRef", "IFixture::Release"]);
        var acquisitionImport = CreateImport() with {
            Key = "test.fixture.owner-acquisition",
            ParameterTypes = [PrimitiveType.NativeComInterfaceOutputAddress],
            ParameterContracts = [NativeImportParameterContract.OwnedNativeComInterfaceOutput],
            ComOutputs = [new(0, "IFixture", NativeComEffects.AcquiresReference)],
        };
        var verificationImport = CreateImport() with {
            Key = "test.fixture.owner-clone-verification",
        };
        var addRefMethod = new NativeComMethodDefinition(
            "fixture.iface.add-ref", "IFixture", "IFixture", 0,
            "ULONG(IFixture*)", "windows-x64-rcx-eax",
            NativeComEffects.AcquiresReference, IsCallable: true) {
            VTableMethodIdentity = "IFixture::AddRef",
        };
        var releaseMethod = new NativeComMethodDefinition(
            "fixture.iface.release", "IFixture", "IFixture", 1,
            "ULONG(IFixture*)", "windows-x64-rcx-eax",
            NativeComEffects.ReleasesReference, IsCallable: true) {
            VTableMethodIdentity = "IFixture::Release",
        };
        var owner = new NativeComOwnerDefinition(
            "fixture.owner", "IFixture", NativeComOwnerAcquisitionKind.NativeImportOutput,
            acquisitionImport.Key, 0, true, 8, 8) {
            AdditionalAcquisitionMethodIdentities = [addRefMethod.Identity],
            ReleaseMethodIdentity = releaseMethod.Identity,
        };
        var catalog = NativeImportCatalog.CreateForTesting(
            "test.generic-com-owner-clone.v1", [acquisitionImport, verificationImport], [interfaceDefinition], [owner],
            [addRefMethod, releaseMethod], []);
        var ownerType = (PrimitiveType)100;
        var cloneFunction = new BoundFunction("fixture.owner.TryClone", PrimitiveType.Bool, [
            new("this", ownerType, false, default),
            new("clone", ownerType, true, default) { IsOut = true },
        ], 2, new BoundBlock([], default), default) {
            DeclaringStruct = owner.BoundOwnerName,
            IsInternal = true,
            IsNoThrow = true,
            Intrinsic = BoundIntrinsic.NativeComOwnerTryClone,
            NativeComMethod = addRefMethod,
        };
        var closeFunction = new BoundFunction(owner.BoundOwnerName + ".Close", PrimitiveType.Void, [
            new("this", ownerType, false, default),
        ], 1, new BoundBlock([], default), default) {
            DeclaringStruct = owner.BoundOwnerName,
            IsNoThrow = true,
            IsCleanup = true,
            Intrinsic = BoundIntrinsic.NativeComOwnerClose,
            NativeComMethod = releaseMethod,
        };
        var verificationFunction = new BoundFunction("fixture.Verify", PrimitiveType.UInt32, [
            new("value", PrimitiveType.UInt32, false, default),
        ], 1, new BoundBlock([], default), default) {
            IsExternal = true,
            IsNoThrow = true,
            NativeImportKey = verificationImport.Key,
        };
        var program = new BoundProgram([cloneFunction, closeFunction, verificationFunction], -1) {
            Structures = [
                new BoundStruct(owner.BoundOwnerName, [], owner.CellSize) {
                    IsUnique = true,
                    CleanupMethod = owner.BoundOwnerName + ".Close",
                },
            ],
            NativeImportCatalog = catalog,
            NativeImportCatalogId = catalog.Identity,
        };

        Assert.True(NativeComCallContract.IsValidOwnerCloneFunction(cloneFunction, program));
        Assert.False(NativeComCallContract.IsValidOwnerCloneFunction(cloneFunction with {
            NativeComMethod = releaseMethod,
        }, program));
        Assert.False(NativeComCallContract.IsValidOwnerCloneFunction(cloneFunction with {
            Variables = cloneFunction.Variables.SetItem(1,
                cloneFunction.Variables[1] with { IsOut = false }),
        }, program));
        var unapprovedCatalog = NativeImportCatalog.CreateForTesting(
            "test.generic-com-owner-no-clone.v1", [acquisitionImport, verificationImport],
            [interfaceDefinition], [owner with { AdditionalAcquisitionMethodIdentities = [] }],
            [addRefMethod, releaseMethod], []);
        var unapprovedProgram = program with {
            NativeImportCatalog = unapprovedCatalog,
            NativeImportCatalogId = unapprovedCatalog.Identity,
        };
        Assert.False(NativeComCallContract.IsValidOwnerCloneFunction(cloneFunction, unapprovedProgram));
        SemanticVerifier.VerifyLibraryReferences(program);

        var cloneCall = new BoundCall(0, [
            new BoundReceiverLoan(0, ownerType, default),
            new BoundOutArgument(1, true, ownerType, default),
        ], PrimitiveType.Bool, default);
        var caller = new BoundFunction("fixture.CloneCaller", PrimitiveType.Bool, [
            new("this", ownerType, false, default),
            new("clone", ownerType, true, default),
        ], 1, new BoundBlock([new BoundReturn(cloneCall, default)], default), default);
        var callerProgram = program with {
            Functions = [cloneFunction, caller, closeFunction, verificationFunction],
        };
        var acquisitions = NativeComCallContract.Acquisitions(caller, callerProgram);
        Assert.NotNull(acquisitions);
        Assert.Equal(0, acquisitions[1]);
        var unapprovedAcquisitions = NativeComCallContract.Acquisitions(caller, callerProgram with {
            NativeImportCatalog = unapprovedCatalog,
            NativeImportCatalogId = unapprovedCatalog.Identity,
        });
        Assert.NotNull(unapprovedAcquisitions);
        Assert.Empty(unapprovedAcquisitions);

        var code = new PeCompiler(program, default, managedNodes: false, library: true).Compile();
        var range = Assert.Single(code.Functions,
            candidate => candidate.Start == code.SourceFunctionOffsets[0]);
        Assert.True(code.Code.AsSpan(range.Start, range.End - range.Start)
            .IndexOf(new byte[] { 0xFF, 0x50, 0x00 }) >= 0);
    }

    [Fact]
    public void NativeComVTableCallStagesAndValidatesOwnedOutputParameters()
    {
        var interfaceDefinition = new NativeComInterfaceDefinition(
            "IFixture", Guid.Parse("CCEB2E5D-147F-4E73-A8ED-1AE5E9CE5A47"), null,
            ["IFixture::Create", "IFixture::Release"], ["IFixture::Create", "IFixture::Release"]);
        var createMethod = new NativeComMethodDefinition(
            "fixture.iface.create", "IFixture", "IFixture", 0,
            "HRESULT(IFixture*,uint32,REFIID,LPVOID,uint32,IFixture**)",
            "windows-x64-rcx-edx-r8-r9-stack32-stack40-eax",
            NativeComEffects.SignedHResult | NativeComEffects.AcquiresReference
                | NativeComEffects.OutputMayBeWrittenOnFailure,
            IsCallable: true, OutputParameterIndex: 5, OutputInterfaceIdentity: "IFixture") {
            VTableMethodIdentity = "IFixture::Create",
        };
        var releaseMethod = new NativeComMethodDefinition(
            "fixture.iface.release", "IFixture", "IFixture", 1,
            "ULONG(IFixture*)", "windows-x64-rcx-eax",
            NativeComEffects.ReleasesReference, IsCallable: true) {
            VTableMethodIdentity = "IFixture::Release",
        };
        var owner = new NativeComOwnerDefinition(
            "fixture.owner", "IFixture", NativeComOwnerAcquisitionKind.ComMethodOutput,
            createMethod.Identity, 5, true, 8, 8) {
            ReleaseMethodIdentity = releaseMethod.Identity,
        };
        var verificationImport = CreateImport() with {
            Key = "test.generic-com-output-call.verify",
        };
        var catalog = NativeImportCatalog.CreateForTesting(
            "test.generic-com-output-call.v1", [verificationImport], [interfaceDefinition], [owner],
            [createMethod, releaseMethod], []);
        var ownerType = (PrimitiveType)100;
        var createFunction = new BoundFunction("fixture.owner.Create", PrimitiveType.Bool, [
            new("this", ownerType, false, default),
            new("third", PrimitiveType.UInt32, false, default),
            new("created", ownerType, true, default) { IsOut = true },
        ], 3, new BoundBlock([], default), default) {
            DeclaringStruct = owner.BoundOwnerName,
            IsInternal = true,
            IsNoThrow = true,
            Intrinsic = BoundIntrinsic.NativeComVTableCall,
            NativeComMethod = createMethod,
            NativeComParameterMappings = [new(1, 4), new(2, 5)],
            NativeComHiddenParameters = [
                new(1, NativeComHiddenParameterKind.UInt32),
                new(2, NativeComHiddenParameterKind.InterfaceIdAddress, InterfaceIdentity: "IFixture"),
                new(3, NativeComHiddenParameterKind.NullPointer),
            ],
        };
        var closeFunction = new BoundFunction(owner.BoundOwnerName + ".Close", PrimitiveType.Void, [
            new("this", ownerType, false, default),
        ], 1, new BoundBlock([], default), default) {
            DeclaringStruct = owner.BoundOwnerName,
            IsNoThrow = true,
            IsCleanup = true,
            Intrinsic = BoundIntrinsic.NativeComOwnerClose,
            NativeComMethod = releaseMethod,
        };
        var verificationImportFunction = new BoundFunction("fixture.Verify", PrimitiveType.UInt32, [
            new("value", PrimitiveType.UInt32, false, default),
        ], 1, new BoundBlock([], default), default) {
            IsExternal = true,
            IsNoThrow = true,
            NativeImportKey = verificationImport.Key,
        };
        var program = new BoundProgram([createFunction, closeFunction, verificationImportFunction], -1) {
            Structures = [
                new BoundStruct(owner.BoundOwnerName, [], owner.CellSize) {
                    IsUnique = owner.IsUnique,
                    CleanupMethod = owner.BoundOwnerName + ".Close",
                },
            ],
            NativeImportCatalog = catalog,
            NativeImportCatalogId = catalog.Identity,
        };

        Assert.True(NativeComCallContract.IsValidVTableCallFunction(createFunction, program));
        Assert.False(NativeComCallContract.IsValidVTableCallFunction(
            createFunction with { ReturnType = PrimitiveType.Int32 }, program));
        Assert.False(NativeComCallContract.IsValidVTableCallFunction(createFunction with {
            NativeComParameterMappings = [new(1, 4), new(2, 4)],
        }, program));
        Assert.False(NativeComCallContract.IsValidVTableCallFunction(createFunction with {
            NativeComHiddenParameters = [
                new(1, NativeComHiddenParameterKind.NullPointer),
                new(2, NativeComHiddenParameterKind.InterfaceIdAddress, InterfaceIdentity: "IFixture"),
                new(3, NativeComHiddenParameterKind.NullPointer),
            ],
        }, program));
        Assert.False(NativeComCallContract.IsValidVTableCallFunction(createFunction with {
            Variables = createFunction.Variables.SetItem(2, createFunction.Variables[2] with { IsOut = false }),
        }, program));
        var outputCall = new BoundCall(0, [
            new BoundReceiverLoan(0, ownerType, default),
            new BoundConstant(1, PrimitiveType.UInt32, default),
            new BoundOutArgument(1, true, ownerType, default),
        ], PrimitiveType.Bool, default);
        var outputCaller = new BoundFunction("fixture.Caller", PrimitiveType.Bool, [
            new("this", ownerType, false, default),
            new("created", ownerType, true, default),
        ], 1, new BoundBlock([new BoundReturn(outputCall, default)], default), default);
        var outputProgram = program with {
            Functions = [createFunction, outputCaller, closeFunction, verificationImportFunction],
        };
        var acquisitions = NativeComCallContract.Acquisitions(outputCaller, outputProgram);
        Assert.NotNull(acquisitions);
        Assert.Equal(0, acquisitions[1]);
        SemanticVerifier.VerifyLibraryReferences(program);

        var code = new PeCompiler(program, default, managedNodes: false, library: true).Compile();
        var range = Assert.Single(code.Functions,
            candidate => candidate.Start == code.SourceFunctionOffsets[0]);
        var bytes = code.Code.AsSpan(range.Start, range.End - range.Start);
        Assert.True(bytes.IndexOf(new byte[] { 0x48, 0x8D, 0x85 }) >= 0
            || bytes.IndexOf(new byte[] { 0x48, 0x8D, 0x84, 0x24 }) >= 0);
        Assert.True(bytes.IndexOf(new byte[] { 0x48, 0x89, 0x84, 0x24, 0x28, 0, 0, 0 }) >= 0);
        Assert.True(range.StackSize >= 160);
        Assert.True(bytes.IndexOf(new byte[] { 0xFF, 0x50, 0x00 }) >= 0);
        Assert.True(bytes.IndexOf(new byte[] { 0xFF, 0x50, 0x08 }) >= 0);
    }

    private static NativeImportDefinition CreateImport() =>
        new("test.fixture.call", "fixture.dll", "Call", [PrimitiveType.UInt32], PrimitiveType.UInt32,
            IsNoThrow: true, RequiresUnsafeCaller: false, DoesNotReturn: false,
            NativeImportDeploymentPolicy.ApplicationDirectory);

    private static void AssertFloatingReturn(NativeCode code, int functionIndex, int vtableSlot, bool wide)
    {
        var range = Assert.Single(code.Functions,
            candidate => candidate.Start == code.SourceFunctionOffsets[functionIndex]);
        var bytes = code.Code.AsSpan(range.Start, range.End - range.Start);
        var call = bytes.IndexOf(new byte[] {
            0xFF, 0x50, checked((byte)(vtableSlot * NativeTargetProfile.PointerSize)),
        });
        var resultTransfer = wide
            ? bytes.IndexOf(new byte[] { 0x66, 0x48, 0x0F, 0x7E, 0xC0 })
            : bytes.IndexOf(new byte[] { 0x66, 0x0F, 0x7E, 0xC0 });
        Assert.True(call >= 0 && resultTransfer > call);
    }
}
