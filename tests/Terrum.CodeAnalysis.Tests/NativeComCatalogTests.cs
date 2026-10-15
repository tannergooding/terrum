// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeComCatalogTests
{
    [Test]
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

        Assert.That(catalog.TryGetComInterface("IFixture", out var selectedInterface), Is.True);
        Assert.That(selectedInterface, Is.SameAs(interfaceDefinition));
        Assert.That(catalog.TryGetComMethod(methodDefinition.Identity, out var selectedMethod), Is.True);
        Assert.That(selectedMethod, Is.SameAs(methodDefinition));
        Assert.That(NativeFixedArrayContract.MatchesCatalog(catalog), Is.False);
    }

    [Test]
    public void CheckedFixedArrayStorageUsesCatalogLayoutRatherThanApiSourceShape()
    {
        var layout = new NativeFixedArrayStorageLayoutDefinition(
            nameof(PrimitiveType.UInt32), 2, sizeof(uint), sizeof(uint), 2 * sizeof(uint)) {
            AllowsMutableLocal = true,
            AllowsElementWrites = true,
            AllowsCheckedRuntimeAccess = true,
        };
        var catalog = NativeImportCatalog.CreateForTesting(
            "test.generic-checked-fixed-array.v1", [], fixedArrayStorageLayouts: [layout]);
        var profile = new NativeSourceProfileBuilder().Select([], catalog,
            BoundRuntimePolicy.CheckedExceptions);
        var program = new BoundProgram([], -1) {
            NativeImportCatalog = catalog,
            RuntimePolicy = BoundRuntimePolicy.CheckedExceptions,
        };

        Assert.That(profile.Has(NativeSourceFeature.NativeFixedArrayStorage), Is.True);
        Assert.That(NativeFixedArrayContract.MatchesProgram(program), Is.True);
    }

    [Test]
    public void CheckedFixedArrayArithmeticUsesCatalogCapabilitiesWithoutWindowsImports()
    {
        var layout = new NativeFixedArrayStorageLayoutDefinition(
            nameof(PrimitiveType.UInt32), 3, sizeof(uint), sizeof(uint), 3 * sizeof(uint)) {
            AllowsMutableLocal = true,
            AllowsElementWrites = true,
            AllowsUInt32Indices = true,
            AllowsCheckedRuntimeAccess = true,
        };
        var catalog = NativeImportCatalog.CreateForTesting(
            "test.generic-checked-array-arithmetic.v1", [], fixedArrayStorageLayouts: [layout]);
        var source = """
            int32 Main() {
                mut InlineArray<uint32, 3> values = [4, 5, 6];
                values[1] = values[0] + 1;
                return int32(values[2]);
            }
            """;
        var compilation = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(source)], catalog, BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");

        _ = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.CheckedExceptions);
    }

    [Test]
    public void CataloguedAggregateLayoutsAdmitNestedStructsBeyondTheValueProfile()
    {
        const string importKey = "test.fixture.read-descriptor";
        var import = new NativeImportDefinition(importKey, "fixture.dll", "ReadDescriptor",
            [PrimitiveType.NativeStorageAddress], PrimitiveType.Int32,
            IsNoThrow: true, RequiresUnsafeCaller: true, DoesNotReturn: false,
            NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [NativeImportParameterContract.ReadonlyNativeStorageNoEscape],
            AggregateStorageParameters = [
                new(0, PrimitiveType.NativeStorageAddress, 40, 8, IsReadonly: true,
                    SourceType: "FixtureDescriptor"),
            ],
        };
        var catalog = NativeImportCatalog.CreateForTesting("test.generic-aggregate-storage.v1", [import],
            aggregateStorageLayouts: [
                new("FixtureHeader", 3 * sizeof(ulong), sizeof(ulong)) {
                    Fields = [
                        new("First", nameof(PrimitiveType.UInt64), 0, sizeof(ulong)),
                        new("Second", nameof(PrimitiveType.UInt64), sizeof(ulong), sizeof(ulong)),
                        new("Third", nameof(PrimitiveType.UInt64), 2 * sizeof(ulong), sizeof(ulong)),
                    ],
                },
                new("FixtureDescriptor", 40, 8) {
                    Fields = [
                        new("Header", "FixtureHeader", 0, 3 * sizeof(ulong)),
                        new("OutputHandle", nameof(PrimitiveType.UInt64),
                            3 * sizeof(ulong), sizeof(ulong)),
                        new("Count", nameof(PrimitiveType.UInt32), 4 * sizeof(ulong), sizeof(uint)),
                        new("Flags", nameof(PrimitiveType.UInt32), 4 * sizeof(ulong) + sizeof(uint),
                            sizeof(uint)),
                    ],
                },
            ]);
        var source = $$"""
            [Terrum.Interop.NativeImport("{{importKey}}")]
            [NoThrow]
            extern int32 ReadDescriptor(Terrum.Interop.NativeStorageAddress descriptor);

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

            int32 Main() {
                FixtureDescriptor descriptor = new FixtureDescriptor(
                    new FixtureHeader(1, 2, 3), 4, 5, 6);
                return unsafe(ReadDescriptor(&descriptor));
            }
            """;

        var compilation = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(source)], catalog, BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");
        var main = program.Functions[program.EntryPoint];
        var returnStatement = main.Body.Statements.OfType<BoundReturn>().Single();
        var call = returnStatement.Value switch {
            BoundUnsafeExpression { Expression: BoundCall nativeCall } => nativeCall,
            BoundCall nativeCall => nativeCall,
            _ => throw new AssertionException("Expected the aggregate import call."),
        };
        var address = call.Arguments[0] as BoundNativeStorageAddress
            ?? throw new AssertionException("Expected a catalogued aggregate storage address.");
        Assert.That(address.ByteExtent, Is.EqualTo(40));
        Assert.That(address.IsReadonly, Is.True);
    }

    [Test]
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
            IsNoThrow: true, RequiresUnsafeCaller: true, DoesNotReturn: false,
            NativeImportDeploymentPolicy.ApplicationDirectory);

        var catalog = NativeImportCatalog.CreateForTesting(
            "test.generic-com.v1", [import], [interfaceDefinition], [], [methodDefinition], []);

        Assert.That(catalog.TryGetComInterface("IFixture", out var selectedInterface), Is.True);
        Assert.That(selectedInterface, Is.SameAs(interfaceDefinition));
        Assert.That(catalog.TryGetComMethod("fixture.iface.run", out var selectedMethod), Is.True);
        Assert.That(selectedMethod, Is.SameAs(methodDefinition));
        var unrelatedInterface = new NativeComInterfaceDefinition(
            "IUnrelated", Guid.Parse("0B1846F5-71B2-4517-89E8-EE29BCF7A41B"), null,
            ["IUnrelated::Run"], ["IUnrelated::Run"]);
        Assert.That(NativeComCatalog.IsValid([interfaceDefinition, unrelatedInterface], [], [
            methodDefinition with {
                Signature = "uint32(IUnrelated*)",
            },
        ], [], [import]), Is.False);
        Assert.That(NativeComCatalog.IsValid([interfaceDefinition], [], [
            methodDefinition with {
                Signature = "void(IFixture*,IFixture*)",
                Abi = "windows-x64-rcx-edx-void",
            },
        ], [], [import]), Is.False);
        Assert.That(NativeComCatalog.IsValid([interfaceDefinition], [], [
            methodDefinition with {
                Signature = "double(IFixture*,double)",
                Abi = "windows-x64-rcx-rdx-eax",
            },
        ], [], [import]), Is.False);
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

    [Test]
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

        Assert.That(catalog.TryGetComInterface("IFixture", out var selectedInterface), Is.True);
        Assert.That(selectedInterface, Is.SameAs(interfaceDefinition));

        var outputMethod = new NativeComMethodDefinition(
            "fixture.iface.create", "IFixture", "IFixture", 0,
            "HRESULT(IFixture*,IFixtureAlias**)", "windows-x64-rcx-rdx-eax",
            NativeComEffects.SignedHResult | NativeComEffects.AcquiresReference,
            IsCallable: true, OutputParameterIndex: 1, OutputInterfaceIdentity: "IFixture") {
            VTableMethodIdentity = "IFixture::Run",
        };
        Assert.That(NativeComCatalog.IsValid(
            [interfaceDefinition], [], [outputMethod], [], [CreateImport()], [], out _), Is.True);
        var otherInterface = new NativeComInterfaceDefinition(
            "IOther", Guid.Parse("8DE87BA7-BC92-49E3-B31D-8E3EA62F4B92"), null,
            ["IOther::Run"], ["IOther::Run"]);
        Assert.That(NativeComCatalog.IsValid(
            [interfaceDefinition, otherInterface], [], [outputMethod with {
                OutputInterfaceIdentity = "IOther",
            }], [], [CreateImport()], [], out _), Is.False);
    }

    [Test]
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

        Assert.That(NativeComCatalog.IsValid(
            [interfaceDefinition], [], [method], [], [CreateImport()]), Is.True);
        Assert.That(NativeComCatalog.IsValid(
            [interfaceDefinition], [], [method with {
                Abi = "windows-x64-rcx-rdx-r8-xmm0",
            }], [], [CreateImport()]), Is.False);
        Assert.That(NativeComCatalog.IsValid(
            [interfaceDefinition], [], [method with {
                Abi = "windows-x64-rcx-xmm1-xmm2-eax",
            }], [], [CreateImport()]), Is.False);
    }

    [Test]
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

        Assert.That(NativeComCatalog.IsValid([baseInterface, derivedInterface], [], [method], [],
            [CreateImport()]), Is.True);
        Assert.That(NativeComCatalog.IsValid([baseInterface, derivedInterface], [], [
            method with { Slot = 1 },
        ], [], [CreateImport()]), Is.False);
        Assert.That(NativeComCatalog.IsValid([baseInterface, derivedInterface with {
            FlattenedMethods = ["IDerived::Close", "IBase::Run"],
        }], [], [method], [], [CreateImport()]), Is.False);
        Assert.That(NativeComCatalog.IsValid([baseInterface, derivedInterface], [], [
            method with { DeclaringInterfaceIdentity = "IDerived" },
        ], [], [CreateImport()]), Is.False);
    }

    [Test]
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

        Assert.That(NativeComCatalog.IsValid(
            [baseInterface, derivedInterface], [], [baseMethod, derivedMethod], [], [], [signatureType], out _), Is.True);
        Assert.That(NativeComCatalog.IsValid(
            [baseInterface, derivedInterface], [], [
                baseMethod,
                derivedMethod with { Signature = "uint32(IDerivedAlias*,double)" },
            ], [], [], [signatureType], out _), Is.False);
    }

    [Test]
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

        Assert.That(NativeComCatalog.IsValid(
            [root, baseInterface, derivedInterface], [owner], [method], [("IDerived", "IBase")], [import]), Is.True);
        Assert.That(NativeComCatalog.IsValid(
            [root, baseInterface, derivedInterface], [owner], [
                method with {
                    ParameterAdaptations = [
                        new(1, "IDerived*", "IRoot*",
                            NativeComParameterAdaptationKind.PointerPreservingBaseInterface),
                    ],
                },
            ], [("IDerived", "IBase"), ("IBase", "IRoot")], [import]), Is.False);
        Assert.That(NativeComCatalog.IsValid(
            [root, baseInterface, derivedInterface], [owner], [
                method with { BorrowedOwnerParameters = [] },
            ], [("IDerived", "IBase")], [import]), Is.False);
    }

    [Test]
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

        Assert.That(catalog.TryGetComOwner("fixture.owner", out var selectedOwner), Is.True);
        Assert.That(selectedOwner, Is.SameAs(owner));
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

    [Test]
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
            IsNoThrow: true, RequiresUnsafeCaller: true, DoesNotReturn: false,
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

        Assert.That(NativeComCallContract.IsValidOwnerCloseFunction(close, program), Is.True);
        Assert.That(NativeComCallContract.IsValidOwnerCloseFunction(close with {
            NativeComMethod = release with { Effects = NativeComEffects.None },
        }, program), Is.False);
        Assert.That(NativeComCallContract.IsValidOwnerCloseFunction(close with {
            Name = "fixture.other.Close",
        }, program), Is.False);
        Assert.That(NativeComCallContract.IsValidOwnerCloseFunction(close with {
            Intrinsic = BoundIntrinsic.NativeComVTableCall,
        }, program), Is.False);
        Assert.That(NativeComCatalog.IsValid(
            [interfaceDefinition], [owner with { ReleaseMethodIdentity = "fixture.missing.release" }],
            [release], [], [import]), Is.False);
    }

    [Test]
    public void GenericComImportOutputBindsSourceOwnerAndCleanup()
    {
        var interfaceDefinition = new NativeComInterfaceDefinition(
            "IFixture", Guid.Parse("62D20A28-2466-4B88-882D-E7F27A61E4BE"), null,
            ["IFixture::Release", "IFixture::Run"], ["IFixture::Release", "IFixture::Run"]);
        var release = new NativeComMethodDefinition(
            "fixture.iface.release", "IFixture", "IFixture", 0,
            "ULONG(IFixture*)", "windows-x64-rcx-eax",
            NativeComEffects.ReleasesReference, IsCallable: true) {
            VTableMethodIdentity = "IFixture::Release",
        };
        var run = new NativeComMethodDefinition(
            "fixture.iface.run", "IFixture", "IFixture", 1,
            "uint32(IFixture*)", "windows-x64-rcx-eax",
            NativeComEffects.None, IsCallable: true) {
            VTableMethodIdentity = "IFixture::Run",
        };
        var import = new NativeImportDefinition(
            "test.fixture.acquire-owner", "fixture.dll", "Acquire",
            [PrimitiveType.NativeNullPointer, PrimitiveType.NativeComInterfaceOutputAddress], PrimitiveType.Int32,
            IsNoThrow: true, RequiresUnsafeCaller: true, DoesNotReturn: false,
            NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [NativeImportParameterContract.NullPointerOnly,
                NativeImportParameterContract.OwnedNativeComInterfaceOutput],
            ComOutputs = [new(1, "IFixture", NativeComEffects.SignedHResult
                | NativeComEffects.AcquiresReference
                | NativeComEffects.OutputMayBeWrittenOnFailure)],
        };
        var owner = new NativeComOwnerDefinition(
            "FixtureOwner", "IFixture", NativeComOwnerAcquisitionKind.NativeImportOutput,
            import.Key, 1, true, 8, 8) {
            ReleaseMethodIdentity = release.Identity,
        };
        var catalog = NativeImportCatalog.CreateForTesting(
            "test.generic-com-source-acquisition.v1", [import], [interfaceDefinition], [owner], [release, run], []);
        var source = """
            [Terrum.Interop.NativeImport("test.fixture.acquire-owner")]
            [NoThrow]
            extern int32 Acquire(Terrum.Interop.Windows.NativeNullPointer optional, out FixtureOwner owner);

            [Terrum.Interop.NativeComAcquisition(0)]
            bool ForwardAcquire(out FixtureOwner owner) {
                return unsafe(Acquire(null, out owner) >= 0);
            }

            int32 Main() {
                if (!ForwardAcquire(out var owner)) {
                    return 1;
                }
                unsafe {
                    return int32(owner.Run());
                }
            }
            """;

        var compilation = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(source)], catalog, BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        var unguardedCall = source.Replace("unsafe {", "{", StringComparison.Ordinal);
        var unguardedCompilation = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(unguardedCall)], catalog, BoundRuntimePolicy.FailFast);
        Assert.That(unguardedCompilation.Program, Is.Null);
        Assert.That(unguardedCompilation.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3028"), Is.True);

        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var ownerStructure = global::System.Linq.Enumerable.Single(program.Structures, structure => structure.Name == owner.BoundOwnerName);
        Assert.That(ownerStructure.IsUnique, Is.True);
        Assert.That(ownerStructure.Size, Is.EqualTo(owner.CellSize));
        Assert.That(ownerStructure.CleanupMethod, Is.EqualTo(owner.BoundOwnerName + ".Close"));
        var close = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == owner.BoundOwnerName + ".Close");
        Assert.That(NativeComCallContract.IsValidOwnerCloseFunction(close, program), Is.True);
        var forward = global::System.Linq.Enumerable.Single(program.Functions,
            static function => function.Name == "ForwardAcquire");
        Assert.That(forward.NativeComAcquisitionOutputParameterIndexes, Is.EqualTo(new[] { 0 }));
        var main = global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == "Main");
        Assert.That(NativeComCallContract.Acquisitions(main, program)!, Has.Exactly(1).Items);

        _ = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);

        var branchedSource = source.Replace(
            "return unsafe(Acquire(null, out owner) >= 0);",
            "if (unsafe(0 <= Acquire(null, out owner))) {\n    return true;\n}\nreturn false;",
            StringComparison.Ordinal);
        var branchedCompilation = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(branchedSource)], catalog, BoundRuntimePolicy.FailFast);
        Assert.That(branchedCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, branchedCompilation.Diagnostics));

        var failureAsSuccess = source.Replace(
            "return unsafe(Acquire(null, out owner) >= 0);",
            "return unsafe(Acquire(null, out owner) < 0);", StringComparison.Ordinal);
        var failureAsSuccessCompilation = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(failureAsSuccess)], catalog, BoundRuntimePolicy.FailFast);
        Assert.That(failureAsSuccessCompilation.Program, Is.Null);

        var missingAcquisition = source.Replace(
            "[Terrum.Interop.NativeComAcquisition(0)]\n", "", StringComparison.Ordinal);
        var missingAcquisitionCompilation = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(missingAcquisition)], catalog, BoundRuntimePolicy.FailFast);
        Assert.That(missingAcquisitionCompilation.Program, Is.Null);

        var wrongOrdinal = source.Replace(
            "[Terrum.Interop.NativeComAcquisition(0)]",
            "[Terrum.Interop.NativeComAcquisition(1)]", StringComparison.Ordinal);
        var wrongOrdinalCompilation = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(wrongOrdinal)], catalog, BoundRuntimePolicy.FailFast);
        Assert.That(wrongOrdinalCompilation.Program, Is.Null);
        Assert.That(wrongOrdinalCompilation.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3030"), Is.True);
    }

    [Test]
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

            Assert.That(NativeComCallContract.IsValidOwnerCloseFunction(close, program), Is.True, owner.BoundOwnerName);
        }
    }

    [Test]
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

        Assert.That(catalog.TryGetComMethod(method.Identity, out var selectedMethod), Is.True);
        Assert.That(selectedMethod, Is.SameAs(method));
        var program = new BoundProgram([], -1) {
            Structures = [
                new BoundStruct("fixture.owner", [], 8) {
                    IsUnique = true,
                    CleanupMethod = "fixture.owner.Close",
                },
            ],
            NativeImportCatalog = catalog,
        };
        Assert.That(NativeComCallContract.LoanInterface(method, 1, owner, program), Is.SameAs(interfaceDefinition));
        Assert.Throws<ArgumentException>(() => NativeComCallContract.LoanInterface(
            method with { BorrowedOwnerParameters = [] }, 1, owner, program));
        Assert.That(NativeComCatalog.IsValid([interfaceDefinition, otherInterface], [owner], [
            method with { BorrowedOwnerParameters = [2] },
        ], [], [import]), Is.False);
        Assert.That(NativeComCatalog.IsValid([interfaceDefinition, otherInterface], [owner], [
            method with {
                Signature = "void(IFixture*,IUnknown*)",
                BorrowedOwnerParameters = [1],
            },
        ], [], [import]), Is.False);
        Assert.That(NativeComCatalog.IsValid([interfaceDefinition, otherInterface], [owner], [
            method with { Signature = "void(IFixture*,IFixture**)" },
        ], [], [import]), Is.False);
    }

    [Test]
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
            RequiresUnsafeCaller = true,
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
            RequiresUnsafeCaller = true,
            Intrinsic = BoundIntrinsic.NativeComVTableCall,
            NativeComMethod = stackMethod,
        };
        var float32Function = new BoundFunction("fixture.owner.GetFloat32", PrimitiveType.Float32, [
            new("this", ownerType, false, default),
        ], 1, new BoundBlock([], default), default) {
            DeclaringStruct = "fixture.owner",
            IsInternal = true,
            IsNoThrow = true,
            RequiresUnsafeCaller = true,
            Intrinsic = BoundIntrinsic.NativeComVTableCall,
            NativeComMethod = float32Method,
        };
        var float64Function = new BoundFunction("fixture.owner.GetFloat64", PrimitiveType.Float64, [
            new("this", ownerType, false, default),
        ], 1, new BoundBlock([], default), default) {
            DeclaringStruct = "fixture.owner",
            IsInternal = true,
            IsNoThrow = true,
            RequiresUnsafeCaller = true,
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
            RequiresUnsafeCaller = true,
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
            RequiresUnsafeCaller = true,
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
            RequiresUnsafeCaller = true,
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

        Assert.That(NativeComCallContract.IsValidVTableCallFunction(registerFunction, program), Is.True);
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(stackFunction, program), Is.True);
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(float32Function, program), Is.True);
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(float64Function, program), Is.True);
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(adaptedFunction, program), Is.True);
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(aggregateFunction, program), Is.True);
        var aggregateOutputFunctions = NativeComCallContract.DirectMethodFunctions(catalog,
            program.Structures, new HashSet<string>(StringComparer.Ordinal) { "RunAggregateOutput" });
        var aggregateOutputFunction = global::System.Linq.Enumerable.Single(aggregateOutputFunctions);
        Assert.That(aggregateOutputFunction.Variables[1].Type, Is.EqualTo(PrimitiveType.NativeStorageAddress));
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(aggregateOutputFunction, program), Is.True);
        var valueOutputFunctions = NativeComCallContract.DirectMethodFunctions(catalog,
            program.Structures, new HashSet<string>(StringComparer.Ordinal) { "RunValueOutputAddress" });
        var valueOutputFunction = global::System.Linq.Enumerable.Single(valueOutputFunctions);
        Assert.That(valueOutputFunction.Variables[1].Type, Is.EqualTo(PrimitiveType.NativeStorageAddress));
        Assert.That(valueOutputFunction.Variables[1].IsOut, Is.False);
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(valueOutputFunction, program), Is.True);
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(valueOutputFunction with {
            Variables = valueOutputFunction.Variables.SetItem(1,
                valueOutputFunction.Variables[1] with { IsOut = true }),
        }, program), Is.False);
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(valueOutputFunction with {
            Variables = valueOutputFunction.Variables.SetItem(1,
                valueOutputFunction.Variables[1] with { Type = PrimitiveType.UInt32 }),
        }, program), Is.False);
        var aggregateStorageAddress = new BoundNativeStorageAddress(0,
            new BoundConstant(0, PrimitiveType.Int32, default), aggregateType, aggregateType,
            PrimitiveType.NativeStorageAddress, 1, 12, 12, IsReadonly: false, default);
        Assert.That(NativeStorageParameterContract.SupportsAggregateType(catalog, aggregateType,
            program.Structures, isReadonly: false), Is.True);
        Assert.That(NativeStorageParameterContract.MatchesAggregateAddress(aggregateStorageAddress,
            aggregateOutputMethod.ParameterAdaptations[0], program), Is.True);
        Assert.That(NativeStorageParameterContract.MatchesAggregateAddress(
            aggregateStorageAddress with { ByteExtent = 8 }, aggregateOutputMethod.ParameterAdaptations[0],
            program), Is.False);
        Assert.That(NativeStorageParameterContract.MatchesAggregateAddress(
            aggregateStorageAddress with { IsReadonly = true }, aggregateOutputMethod.ParameterAdaptations[0],
            program), Is.False);
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(aggregateFunction, program with {
            Structures = program.Structures.SetItem(1, program.Structures[1] with { Size = 8 }),
        }), Is.False);
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(aggregateFunction, program with {
            Structures = program.Structures.SetItem(1, program.Structures[1] with { IsUnique = true }),
        }), Is.False);
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(adaptedFunction with {
            Variables = adaptedFunction.Variables.SetItem(2,
                adaptedFunction.Variables[2] with { Type = PrimitiveType.Int32 }),
        }, program), Is.False);
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(adaptedFunction with {
            Variables = adaptedFunction.Variables.SetItem(3,
                adaptedFunction.Variables[3] with { Type = PrimitiveType.Float64 }),
        }, program), Is.False);
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(adaptedFunction with {
            NativeComParameterMappings = [
                new(4, 4),
                new(5, 4),
                new(6, 4),
            ],
        }, program), Is.False);
        Assert.That(ValueLayout.Alignment(ownerType, program.Structures, program.NativeImportCatalog), Is.EqualTo(8));
        SemanticVerifier.VerifyLibraryReferences(program);
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(stackFunction with {
            Variables = stackFunction.Variables.SetItem(4,
                new BoundVariable("floatingStackArgument", PrimitiveType.Float64, false, default)),
        }, program), Is.False);
        var outputProgram = program with {
            Functions = program.Functions.Add(aggregateOutputFunction).Add(valueOutputFunction),
        };
        SemanticVerifier.VerifyLibraryReferences(outputProgram);
        var code = new PeCompiler(outputProgram, default, managedNodes: false, library: true).Compile();
        var registerRange = global::System.Linq.Enumerable.Single(code.Functions, range => range.Start == code.SourceFunctionOffsets[0]);
        var stackRange = global::System.Linq.Enumerable.Single(code.Functions, range => range.Start == code.SourceFunctionOffsets[1]);
        Assert.That(stackRange.StackSize, Is.EqualTo(registerRange.StackSize + 32));
        Assert.That(code.Code.AsSpan(registerRange.Start, registerRange.End - registerRange.Start)
            .IndexOf(new byte[] { 0x66, 0x0F, 0x6E, 0xC8 }) >= 0, Is.True);
        Assert.That(code.Code.AsSpan(registerRange.Start, registerRange.End - registerRange.Start)
            .IndexOf(new byte[] { 0x66, 0x48, 0x0F, 0x6E, 0xD8 }) >= 0, Is.True);
        Assert.That(code.Code.AsSpan(stackRange.Start, stackRange.End - stackRange.Start)
            .IndexOf(new byte[] { 0x48, 0x89, 0x84, 0x24, 0x20, 0, 0, 0 }) >= 0, Is.True);
        var adaptedRange = global::System.Linq.Enumerable.Single(code.Functions, range => range.Start == code.SourceFunctionOffsets[4]);
        var adaptedBytes = code.Code.AsSpan(adaptedRange.Start, adaptedRange.End - adaptedRange.Start);
        Assert.That(adaptedBytes.IndexOf(new byte[] { 0x48, 0x89, 0xC2 }) >= 0, Is.True);
        var aggregateOutputRange = global::System.Linq.Enumerable.Single(code.Functions, range => range.Start == code.SourceFunctionOffsets[^2]);
        var aggregateOutputBytes = code.Code.AsSpan(aggregateOutputRange.Start,
            aggregateOutputRange.End - aggregateOutputRange.Start);
        Assert.That(aggregateOutputBytes.IndexOf(
            new byte[] { 0x48, 0x8B, 0x01, 0xFF, 0x50, 48 }) >= 0, Is.True);
        var valueOutputRange = global::System.Linq.Enumerable.Single(code.Functions, range => range.Start == code.SourceFunctionOffsets[^1]);
        var valueOutputBytes = code.Code.AsSpan(valueOutputRange.Start,
            valueOutputRange.End - valueOutputRange.Start);
        Assert.That(valueOutputBytes.IndexOf(
            new byte[] { 0x48, 0x8B, 0x01, 0xFF, 0x50, 56 }) >= 0, Is.True);
        Assert.That(valueOutputBytes.IndexOf(new byte[] { 0x48, 0x8B, 0x85 }) >= 0, Is.True);
        Assert.That(valueOutputBytes.IndexOf(new byte[] { 0x48, 0x8D, 0x85 }), Is.EqualTo(-1));
        Assert.That(adaptedBytes.IndexOf(new byte[] { 0x49, 0x89, 0xC0 }) >= 0, Is.True);
        Assert.That(adaptedBytes.IndexOf(new byte[] { 0x49, 0x89, 0xC1 }) >= 0, Is.True);
        Assert.That(adaptedBytes.IndexOf(new byte[] { 0xFF, 0x50, 0x20 }) >= 0, Is.True);
        Assert.That(adaptedBytes.IndexOf(new byte[] { 0x48, 0x8D, 0x85 }) >= 0, Is.True);
        var aggregateRange = global::System.Linq.Enumerable.Single(code.Functions, range => range.Start == code.SourceFunctionOffsets[5]);
        var aggregateBytes = code.Code.AsSpan(aggregateRange.Start, aggregateRange.End - aggregateRange.Start);
        Assert.That(aggregateBytes.IndexOf(new byte[] { 0x48, 0x8D, 0x85 }) >= 0, Is.True);
        AssertFloatingReturn(code, functionIndex: 2, vtableSlot: 2, wide: false);
        AssertFloatingReturn(code, functionIndex: 3, vtableSlot: 3, wide: true);
    }

    [Test]
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

        Assert.That(NativeComCatalog.IsValid(
            [interfaceDefinition], [], [method], [], [CreateImport()]), Is.True);
        Assert.That(NativeComCatalog.IsValid(
            [interfaceDefinition], [], [method with { OutputParameterIndex = 2 }], [], [CreateImport()]), Is.False);
        Assert.That(NativeComCatalog.IsValid(
            [interfaceDefinition], [], [method with { Abi = "windows-x64-rcx-eax" }], [], [CreateImport()]), Is.False);
        Assert.That(NativeComCatalog.IsValid(
            [interfaceDefinition], [], [method with { Abi = "windows-x64-rdx-rcx-eax" }], [], [CreateImport()]), Is.False);

        var owner = new NativeComOwnerDefinition(
            "fixture.method-owner", "IFixture", NativeComOwnerAcquisitionKind.ComMethodOutput,
            method.Identity, 1, true, 8, 8);
        var otherInterface = new NativeComInterfaceDefinition(
            "IOther", Guid.Parse("E8FD8082-EE73-4D76-A06D-20F6C0E93A76"), null, ["IOther::Run"], ["IOther::Run"]);
        Assert.That(NativeComCatalog.IsValid(
            [interfaceDefinition, otherInterface], [owner], [method], [], [CreateImport()]), Is.True);
        Assert.That(NativeComCatalog.IsValid(
            [interfaceDefinition, otherInterface], [owner with { OutputParameterIndex = 0 }], [method], [], [CreateImport()]), Is.False);
        Assert.That(NativeComCatalog.IsValid(
            [interfaceDefinition, otherInterface], [owner with { InterfaceIdentity = "IOther" }], [method], [], [CreateImport()]), Is.False);
        Assert.That(NativeComCatalog.IsValid(
            [interfaceDefinition, otherInterface], [owner], [method with {
                Effects = NativeComEffects.SignedHResult,
            }], [], [CreateImport()]), Is.False);
    }

    [Test]
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

        var close = global::System.Linq.Enumerable.Single(functions, static candidate => candidate.IsCleanup);
        Assert.That(NativeComCallContract.IsValidOwnerCloseFunction(close,
            new BoundProgram(functions, -1) {
                Structures = [ownerStructure],
                NativeImportCatalogId = catalog.Identity,
                NativeImportCatalog = catalog,
            }), Is.True);
        var function = global::System.Linq.Enumerable.Single(functions, static candidate => !candidate.IsCleanup);
        Assert.That(function.Name, Is.EqualTo(owner.BoundOwnerName + ".Resize"));
        Assert.That(function.ReturnType, Is.EqualTo(PrimitiveType.Bool));
        Assert.That(function.Variables[1].Type, Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(function.Variables[2].Type, Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(function,
            new BoundProgram(functions, -1) {
                Structures = [ownerStructure],
                NativeImportCatalogId = catalog.Identity,
                NativeImportCatalog = catalog,
            }), Is.True);
    }

    [Test]
    public void GenericComCatalogForwardsBufferContentsAndSizeFromOneBorrowedOwner()
    {
        var catalog = NativeImportCatalog.WindowsX64;
        var structures = catalog.ComOwners.Select(static owner =>
            new BoundStruct(owner.BoundOwnerName, [], owner.CellSize) {
                IsUnique = true,
                CleanupMethod = owner.BoundOwnerName + ".Close",
            }).ToImmutableArray();
        var program = new BoundProgram([], -1) {
            Structures = structures,
            NativeImportCatalogId = catalog.Identity,
            NativeImportCatalog = catalog,
        };
        var functions = NativeComCallContract.DirectMethodFunctions(catalog, structures,
            new HashSet<string>(StringComparer.Ordinal) { "CreateVertexShader" });
        var function = functions.SingleOrDefault(static candidate =>
            candidate.Name == NativeDx11ComCatalog.DeviceOwnerName + ".CreateVertexShader");

        Assert.That(function, Is.Not.Null);
        TestAssertions.NotNull(function);
        Assert.That(function.NativeComParameterMappings, Is.EqualTo([
            new NativeComBoundParameterMapping(1, 1),
            new NativeComBoundParameterMapping(1, 2),
            new NativeComBoundParameterMapping(2, 4),
        ]));
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(function, program), Is.True);
        var code = new PeCompiler(program with { Functions = functions }, default,
            managedNodes: false, library: true).Compile();
        var pointerGetterCall = new byte[] {
            0x48, 0x8B, 0x80, 0, 0, 0, 0, 0x48, 0x89, 0xC1, 0x48, 0x8B, 0x01, 0xFF, 0x50, 0x18,
        };
        var sizeGetterCall = new byte[] {
            0x48, 0x8B, 0x80, 0, 0, 0, 0, 0x48, 0x89, 0xC1, 0x48, 0x8B, 0x01, 0xFF, 0x50, 0x20,
        };
        Assert.That(code.Code.AsSpan().IndexOf(pointerGetterCall) >= 0, Is.True);
        Assert.That(code.Code.AsSpan().IndexOf(sizeGetterCall) >= 0, Is.True);
    }

    [Test]
    public void D3DCompileCatalogUsesNullForOptionalDiagnosticsOutput()
    {
        var catalog = NativeImportCatalog.WindowsX64;
        Assert.That(catalog.TryGetImport(NativeDx11ComCatalog.CompileShaderFromFileKey,
            out var definition), Is.True);
        TestAssertions.NotNull(definition);
        Assert.That(definition.ParameterTypes[8], Is.EqualTo(PrimitiveType.NativeNullPointer));
        Assert.That(definition.ParameterContracts[8], Is.EqualTo(NativeImportParameterContract.NullPointerOnly));
        Assert.That(definition.ComOutputs.Select(static output => output.ParameterIndex), Is.EqualTo([7]));
        var codeOwner = catalog.ComOwners.Single(owner =>
            owner.BoundOwnerName == NativeDx11ComCatalog.ShaderCodeOwnerName);
        Assert.That(codeOwner.AdditionalOutputParameterIndexes, Is.Empty);
    }

    [Test]
    public void D3DCreateBufferUsesGenericStorageRecordAndComOutputContracts()
    {
        var catalog = NativeImportCatalog.WindowsX64;
        var structures = catalog.ComOwners.Select(static owner =>
            new BoundStruct(owner.BoundOwnerName, [], owner.CellSize) {
                IsUnique = true,
                CleanupMethod = owner.BoundOwnerName + ".Close",
            }).ToImmutableArray();
        var program = new BoundProgram([], -1) {
            Structures = structures,
            NativeImportCatalogId = catalog.Identity,
            NativeImportCatalog = catalog,
        };
        var functions = NativeComCallContract.DirectMethodFunctions(catalog, structures,
            new HashSet<string>(StringComparer.Ordinal) { "CreateBuffer" });
        var function = functions.SingleOrDefault(static candidate =>
            candidate.Name == NativeDx11ComCatalog.DeviceOwnerName + ".CreateBuffer");

        Assert.That(function, Is.Not.Null);
        TestAssertions.NotNull(function);
        Assert.That(function.Variables[1].Type, Is.EqualTo(PrimitiveType.NativeStorageAddress));
        Assert.That(function.Variables[2].Type, Is.EqualTo(PrimitiveType.NativeStorageAddress));
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(function, program), Is.True);
    }

    [Test]
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

        var observeFunction = global::System.Linq.Enumerable.Single(NativeComCallContract.DirectMethodFunctions(
            catalog, [ownerStructure]), static function => !function.IsCleanup);
        Assert.That(observeFunction.Variables[1].Type, Is.EqualTo(PrimitiveType.NativeStorageAddress));
        Assert.That(observeFunction.Variables[2].Type, Is.EqualTo(PrimitiveType.NativeStorageAddress));
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(observeFunction, program), Is.True);
        Assert.That(NativeStorageRecordContract.TryGetParameter(observeMethod, 1,
            out var firstStorageRecordParameter), Is.True);
        Assert.That(NativeStorageRecordContract.TryGetParameter(observeMethod, 2,
            out var secondStorageRecordParameter), Is.True);
        TestAssertions.NotNull(firstStorageRecordParameter);
        TestAssertions.NotNull(secondStorageRecordParameter);
        Assert.That(firstStorageRecordParameter.LoanGroup, Is.EqualTo(loanGroup));
        Assert.That(secondStorageRecordParameter.LoanGroup, Is.EqualTo(loanGroup));
        Assert.That(NativeStorageRecordContract.IsValidComParameter(firstStorageRecordParameter,
            observeMethod, catalog.StorageRecordLayouts), Is.True);
        Assert.That(NativeStorageRecordContract.IsValidComParameter(secondStorageRecordParameter,
            observeMethod, catalog.StorageRecordLayouts), Is.True);
        var recordAddress = new BoundNativeStorageAddress(0,
            new BoundConstant(0, PrimitiveType.Int32, default), PrimitiveType.NativeStorageRecord,
            PrimitiveType.NativeStorageRecord, PrimitiveType.NativeStorageAddress, 1,
            layout.ByteExtent, layout.ByteExtent, IsReadonly: true, Span: default);
        Assert.That(NativeStorageRecordContract.MatchesAddress(
            recordAddress, firstStorageRecordParameter, catalog), Is.True);
        Assert.That(NativeStorageRecordContract.MatchesAddress(
            recordAddress with { ByteExtent = layout.ByteExtent - sizeof(uint) },
            firstStorageRecordParameter, catalog), Is.False);
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

    [Test]
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
            RequiresUnsafeCaller = true,
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

        Assert.That(NativeComCallContract.IsValidOwnerCloneFunction(cloneFunction, program), Is.True);
        Assert.That(NativeComCallContract.IsValidOwnerCloneFunction(cloneFunction with {
            NativeComMethod = releaseMethod,
        }, program), Is.False);
        Assert.That(NativeComCallContract.IsValidOwnerCloneFunction(cloneFunction with {
            Variables = cloneFunction.Variables.SetItem(1,
                cloneFunction.Variables[1] with { IsOut = false }),
        }, program), Is.False);
        var unapprovedCatalog = NativeImportCatalog.CreateForTesting(
            "test.generic-com-owner-no-clone.v1", [acquisitionImport, verificationImport],
            [interfaceDefinition], [owner with { AdditionalAcquisitionMethodIdentities = [] }],
            [addRefMethod, releaseMethod], []);
        var unapprovedProgram = program with {
            NativeImportCatalog = unapprovedCatalog,
            NativeImportCatalogId = unapprovedCatalog.Identity,
        };
        Assert.That(NativeComCallContract.IsValidOwnerCloneFunction(cloneFunction, unapprovedProgram), Is.False);
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
        TestAssertions.NotNull(acquisitions);
        Assert.That(acquisitions[1], Is.EqualTo(0));
        var unapprovedAcquisitions = NativeComCallContract.Acquisitions(caller, callerProgram with {
            NativeImportCatalog = unapprovedCatalog,
            NativeImportCatalogId = unapprovedCatalog.Identity,
        });
        TestAssertions.NotNull(unapprovedAcquisitions);
        Assert.That(unapprovedAcquisitions, Is.Empty);

        var code = new PeCompiler(program, default, managedNodes: false, library: true).Compile();
        var range = global::System.Linq.Enumerable.Single(code.Functions, candidate => candidate.Start == code.SourceFunctionOffsets[0]);
        Assert.That(code.Code.AsSpan(range.Start, range.End - range.Start)
            .IndexOf(new byte[] { 0xFF, 0x50, 0x00 }) >= 0, Is.True);
    }

    [Test]
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
            RequiresUnsafeCaller = true,
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
            RequiresUnsafeCaller = true,
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

        Assert.That(NativeComCallContract.IsValidVTableCallFunction(createFunction, program), Is.True);
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(
            createFunction with { ReturnType = PrimitiveType.Int32 }, program), Is.False);
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(createFunction with {
            NativeComParameterMappings = [new(1, 4), new(2, 4)],
        }, program), Is.False);
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(createFunction with {
            NativeComHiddenParameters = [
                new(1, NativeComHiddenParameterKind.NullPointer),
                new(2, NativeComHiddenParameterKind.InterfaceIdAddress, InterfaceIdentity: "IFixture"),
                new(3, NativeComHiddenParameterKind.NullPointer),
            ],
        }, program), Is.False);
        Assert.That(NativeComCallContract.IsValidVTableCallFunction(createFunction with {
            Variables = createFunction.Variables.SetItem(2, createFunction.Variables[2] with { IsOut = false }),
        }, program), Is.False);
        var outputCall = new BoundCall(0, [
            new BoundReceiverLoan(0, ownerType, default),
            new BoundConstant(1, PrimitiveType.UInt32, default),
            new BoundOutArgument(1, true, ownerType, default),
        ], PrimitiveType.Bool, default) {
            IsUnsafeAuthorized = true,
        };
        var outputCaller = new BoundFunction("fixture.Caller", PrimitiveType.Bool, [
            new("this", ownerType, false, default),
            new("created", ownerType, true, default),
        ], 1, new BoundBlock([new BoundReturn(outputCall, default)], default), default);
        var outputProgram = program with {
            Functions = [createFunction, outputCaller, closeFunction, verificationImportFunction],
        };
        var acquisitions = NativeComCallContract.Acquisitions(outputCaller, outputProgram);
        TestAssertions.NotNull(acquisitions);
        Assert.That(acquisitions[1], Is.EqualTo(0));
        SemanticVerifier.VerifyLibraryReferences(program);

        var code = new PeCompiler(program, default, managedNodes: false, library: true).Compile();
        var range = global::System.Linq.Enumerable.Single(code.Functions, candidate => candidate.Start == code.SourceFunctionOffsets[0]);
        var bytes = code.Code.AsSpan(range.Start, range.End - range.Start);
        Assert.That(bytes.IndexOf(new byte[] { 0x48, 0x8D, 0x85 }) >= 0
            || bytes.IndexOf(new byte[] { 0x48, 0x8D, 0x84, 0x24 }) >= 0, Is.True);
        Assert.That(bytes.IndexOf(new byte[] { 0x48, 0x89, 0x84, 0x24, 0x28, 0, 0, 0 }) >= 0, Is.True);
        Assert.That(range.StackSize >= 160, Is.True);
        Assert.That(bytes.IndexOf(new byte[] { 0xFF, 0x50, 0x00 }) >= 0, Is.True);
        Assert.That(bytes.IndexOf(new byte[] { 0xFF, 0x50, 0x08 }) >= 0, Is.True);
    }

    [Test]
    public void NativeImportCatalogRejectsImportsWithoutCallerUnsafety()
    {
        var safeImport = CreateImport() with { RequiresUnsafeCaller = false };

        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test.native-import.safe-caller.v1", [safeImport]))!;
    }

    private static NativeImportDefinition CreateImport() =>
        new("test.fixture.call", "fixture.dll", "Call", [PrimitiveType.UInt32], PrimitiveType.UInt32,
            IsNoThrow: true, RequiresUnsafeCaller: true, DoesNotReturn: false,
            NativeImportDeploymentPolicy.ApplicationDirectory);

    private static void AssertFloatingReturn(NativeCode code, int functionIndex, int vtableSlot, bool wide)
    {
        var range = global::System.Linq.Enumerable.Single(code.Functions, candidate => candidate.Start == code.SourceFunctionOffsets[functionIndex]);
        var bytes = code.Code.AsSpan(range.Start, range.End - range.Start);
        var call = bytes.IndexOf(new byte[] {
            0xFF, 0x50, checked((byte)(vtableSlot * NativeTargetProfile.PointerSize)),
        });
        var resultTransfer = wide
            ? bytes.IndexOf(new byte[] { 0x66, 0x48, 0x0F, 0x7E, 0xC0 })
            : bytes.IndexOf(new byte[] { 0x66, 0x0F, 0x7E, 0xC0 });
        Assert.That(call >= 0 && resultTransfer > call, Is.True);
    }
}
