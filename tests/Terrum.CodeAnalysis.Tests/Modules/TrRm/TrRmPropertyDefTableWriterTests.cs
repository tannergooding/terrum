// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmPropertyDefTableWriterTests
{
    [Test]
    public void EmitsGetterIdentityIndependentOfImplementationName()
    {
        var stringBytes = Names();
        var strings = new TrRmStringTableReader(stringBytes, 5);
        var moduleBytes = Modules(strings);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings, 0, 1, 1, 0);
        var typeBytes = Types(strings, modules);
        var types = new TrRmTypeDefTableReader(typeBytes, 1, strings, modules, 0, 0, 1, 0);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutBytes = TrRmLayoutTableWriter.Encode([
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
        ], types, targetProfiles, 0);
        var specBytes = Specs();
        var specs = new TrRmTypeSpecTableReader(specBytes, 2, 1, 0, 0, 0);
        var sigBytes = Signatures(specs);
        var signatures = new TrRmMethodSigTableReader(sigBytes, 1, specs, 0, 0);
        var methodBytes = Methods(strings, modules, types, signatures);
        var methods = new TrRmMethodDefTableReader(methodBytes, 1,
            strings, modules, types, signatures, 0, 0);
        var rows = new[] { new TrRmPropertyDefRow(1, 5, 1, 1, 0) };
        var propertyBytes = TrRmPropertyDefTableWriter.Encode(
            rows, strings, types, specs, methods, signatures);
        var properties = new TrRmPropertyDefTableReader(propertyBytes, 1,
            strings, types, specs, methods, signatures);
        Assert.That(propertyBytes.Length, Is.EqualTo(20));
        Assert.That(properties.GetName(1), Is.EqualTo("Value"));
        Assert.That(methods.GetName(properties.GetGetterMethod(1)), Is.EqualTo("Read"));

        rows[0] = rows[0] with { GetterMethodDefRow = 0 };
        Assert.That(properties.GetGetterMethod(1), Is.EqualTo(1));

        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, moduleBytes),
            new TrRmSection(0x0002, 0, 5, stringBytes),
            new TrRmSection(0x0011, 0, 2, specBytes),
            new TrRmSection(0x0013, 0, 1, typeBytes),
            new TrRmSection(0x0015, 0, 1, layoutBytes),
            new TrRmSection(0x0016, 0, 1, methodBytes),
            new TrRmSection(0x0017, 0, 1, sigBytes),
            new TrRmSection(0x001E, 0, 1, propertyBytes),
        ]);
        TrRmStructuralMetadataValidator.Validate(artifact);
    }

    [Test]
    public void RejectsInvalidOwnerNameTypeGetterAndVisibility()
    {
        var row = new TrRmPropertyDefRow(1, 5, 1, 1, 0);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Encode([]));
        _ = Assert.Throws<InvalidDataException>(() => Encode([row with { OwnerTypeDefRow = 0 }]));
        _ = Assert.Throws<InvalidDataException>(() => Encode([row with { NameStringRow = 0 }]));
        _ = Assert.Throws<InvalidDataException>(() => Encode([row with { TypeSpecRef = 2 }]));
        _ = Assert.Throws<InvalidDataException>(() => Encode([row with { GetterMethodDefRow = 0 }]));
        _ = Assert.Throws<InvalidDataException>(() => Encode([row with { Flags = 3 }]));
    }

    [Test]
    public void MatchesInlineTypeRefsAgainstEquivalentGetterResultTypes()
    {
        var property = new TrRmPropertyDefRow(1, 5, 0xF2000008, 1, 0);
        Assert.That(Encode([property]), Has.Length.EqualTo(20));
        _ = Assert.Throws<InvalidDataException>(() => Encode([
            property with { TypeSpecRef = 0xF2000001 },
        ]));
    }

    [Test]
    public void PI1EmitsTypedBackingAndAccessorRows()
    {
        var property = new TrRmPropertyDefRow(1, 7, 1, 1, 0)
        {
            SetterMethodDefRow = 2,
            BackingFieldDefRow = 1,
        };
        var bytes = EncodePI1(property);

        Assert.That(bytes, Has.Length.EqualTo(32));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20)), Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24)), Is.Zero);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(28)), Is.EqualTo(1));
    }

    [Test]
    public void PI1RejectsUnbackedAccessorsAndUnreferencedBackingFields()
    {
        var unbackedSetter = new TrRmPropertyDefRow(1, 7, 1, 1, 0)
        {
            SetterMethodDefRow = 2,
        };
        var unreferencedBacking = new TrRmPropertyDefRow(1, 7, 1, 1, 0);
        _ = Assert.Throws<InvalidDataException>(() => EncodePI1(unbackedSetter));
        _ = Assert.Throws<InvalidDataException>(() => EncodePI1(unreferencedBacking));
    }

    [TestCase(0u, 0u, false, 0)]
    [TestCase(0u, 1u, false, 1)]
    [TestCase(0u, 1u, true, 1)]
    [TestCase(1u, 0u, false, 1)]
    public void PI1PropertyDefinitionIncludesEveryAccessorBody(
        uint getterBody, uint otherAccessorBody, bool useInitializer,
        int expectedBody)
    {
        Assert.That(ResolvePI1PropertyBody(getterBody, otherAccessorBody,
            useInitializer), Is.EqualTo(expectedBody));
    }

    [Test]
    public void PI1RoleThreeMustNameItsLinkedPropertyInitializer()
    {
        var schema
            = TrRmArtifactSchema.C1M1ConstructionL1R1PropertyInitializedStateDraft;
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Widget", 0), ("Initialize", 0),
            ("InitializeValue", 0), ("ReadValue", 0), ("Value", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 7);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 1, 3, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 1, 3, 0);
        var typeDefBytes = new byte[44];
        Write(typeDefBytes, 0, 1);
        Write(typeDefBytes, 4, 3);
        Write(typeDefBytes, 8, 0x0A);
        Write(typeDefBytes, 12, 1);
        Write(typeDefBytes, 16, 1);
        var types = new TrRmTypeDefTableReader(typeDefBytes, 1,
            strings, modules, 1, 0, 0, 0, schema);
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode([
            TrRmTypeSpecTableWriter.Builtin(8),
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            new byte[] { 0x0C, 0, 0x8A, 2, 0, 0, 0 },
        ], typeDefCount: 1, importCount: 0, genericParamCount: 0,
            nativeFunctionPointerSignatureCount: 0, schema,
            methodDefCount: 3);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 3,
            typeDefCount: 1, importCount: 0, genericParamCount: 0,
            nativeFunctionPointerSignatureCount: 0, schema,
            methodDefCount: 3);
        var fieldBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 0, 1, 0x10),
        ], strings, types, typeSpecs, schema);
        var fields = new TrRmFieldDefTableReader(fieldBytes, 1,
            strings, types, typeSpecs, schema);
        var signaturesBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(0xF200_0000, 3, 0, 0, 0, 0),
            new TrRmMethodSigRow(0xF200_0000, 3, 1, 1, 0, 0),
            new TrRmMethodSigRow(1, 3, 0, 0, 0, 0),
        ], typeSpecs, paramCount: 1, genericParamCount: 0);
        var signatures = new TrRmMethodSigTableReader(signaturesBytes, 3,
            typeSpecs, paramCount: 1, genericParamCount: 0);
        var parameterBytes = TrRmParamTableWriter.Encode([
            new TrRmParamRow(2, 0, 1, 0),
        ], signatures, typeSpecs, schema);
        var parameters = new TrRmParamTableReader(parameterBytes, 1,
            signatures, typeSpecs, schema);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(1, 4, 1, 0, 1, 1, 1),
            new TrRmMethodDefRow(1, 5, 2, 0, 2, 1, 2),
            new TrRmMethodDefRow(1, 6, 3, 0, 0, 0, 0),
        ], strings, modules, types, signatures, 2, 2, schema);
        var methods = new TrRmMethodDefTableReader(methodBytes, 3,
            strings, modules, types, signatures, 2, 2, schema);
        var propertyBytes = TrRmPropertyDefTableWriter.Encode([
            new TrRmPropertyDefRow(1, 7, 1, 3, 0)
            {
                InitMethodDefRow = 2,
                BackingFieldDefRow = 1,
            },
        ], strings, types, typeSpecs, methods, signatures, schema,
            fields, parameters);
        var properties = new TrRmPropertyDefTableReader(propertyBytes, 1,
            strings, types, typeSpecs, methods, signatures,
            deferGetterTypeIdentity: false, resourceLedger: null,
            schema: schema, fields: fields, parameters: parameters);
        var contractRows = new[]
        {
            new TrRmContractRow(0x1600_0001, 3, ConstructionPayload(
                role: 1, 0x1E00_0001)),
            new TrRmContractRow(0x1600_0002, 3, ConstructionPayload(
                role: 3, 1)),
        };
        var features = new TrRmFeatureTableReader([], 0, schema);
        var imports = default(TrRmImportTableReader);
        var contractBytes = TrRmContractTableWriter
            .EncodeConstructionDraftForSchema(contractRows, types, methods,
                signatures, parameters, typeSpecs, fields, properties,
                imports, features, modules, predicateGraphCount: 0, schema);
        var contracts = new TrRmContractTableReader(contractBytes, 2,
            types, methods, signatures, parameters, typeSpecs,
            fieldPathCount: 0, features, modules, schema,
            predicateGraphCount: 0, fields: fields, properties: properties,
            imports: imports);

        var initializer = contracts.GetConstructionClause(2);
        Assert.That(initializer.RoleTag, Is.EqualTo(3));
        Assert.That(initializer.PropertyDefRow, Is.EqualTo(1));
        Assert.That(contracts.GetConstructionClause(1)
            .InitializedMemberDefIds, Is.EqualTo(new uint[] { 0x1E00_0001 }));
    }

    private static byte[] Encode(TrRmPropertyDefRow[] rows)
    {
        var stringBytes = Names();
        var strings = new TrRmStringTableReader(stringBytes, 5);
        var moduleBytes = Modules(strings);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings, 0, 1, 1, 0);
        var typeBytes = Types(strings, modules);
        var types = new TrRmTypeDefTableReader(typeBytes, 1, strings, modules, 0, 0, 1, 0);
        var specBytes = Specs();
        var specs = new TrRmTypeSpecTableReader(specBytes, 2, 1, 0, 0, 0);
        var sigBytes = Signatures(specs);
        var signatures = new TrRmMethodSigTableReader(sigBytes, 1, specs, 0, 0);
        var methodBytes = Methods(strings, modules, types, signatures);
        var methods = new TrRmMethodDefTableReader(methodBytes, 1,
            strings, modules, types, signatures, 0, 0);
        return TrRmPropertyDefTableWriter.Encode(rows, strings, types, specs, methods, signatures);
    }

    private static byte[] EncodePI1(TrRmPropertyDefRow row)
    {
        var schema = TrRmArtifactSchema.C1M1ConstructionL1R1PropertyInitializedStateDraft;
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Widget", 0), ("Read", 0),
            ("Write", 0), ("Initialize", 0), ("Value", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 7);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 1, 3, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings, 0, 1, 3, 0);
        var typeBytes = new byte[44];
        Write(typeBytes, 0, 1);
        Write(typeBytes, 4, 3);
        Write(typeBytes, 8, 8);
        Write(typeBytes, 12, 1);
        Write(typeBytes, 16, 1);
        var types = new TrRmTypeDefTableReader(typeBytes, 1,
            strings, modules, 1, 0, 0, 0);
        var specBytes = Specs();
        var specs = new TrRmTypeSpecTableReader(specBytes, 2, 1, 0, 0, 0);
        var fieldsBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 0, 1, 0x10),
        ], strings, types, specs, schema);
        var fields = new TrRmFieldDefTableReader(fieldsBytes, 1,
            strings, types, specs, schema);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 2, 0, 0, 0, 0),
            new TrRmMethodSigRow(0xF2000000, 2, 1, 1, 0, 0),
            new TrRmMethodSigRow(0xF2000000, 2, 2, 1, 0, 0),
        ], specs, 2, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 3, specs, 2, 0);
        var parameterBytes = TrRmParamTableWriter.Encode([
            new TrRmParamRow(2, 0, 1, 0),
            new TrRmParamRow(3, 0, 1, 0),
        ], signatures, specs, schema);
        var parameters = new TrRmParamTableReader(parameterBytes, 2,
            signatures, specs, schema);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(1, 4, 1, 0, 0, 0, 0),
            new TrRmMethodDefRow(1, 5, 2, 0, 0, 0, 0),
            new TrRmMethodDefRow(1, 6, 3, 0, 0, 0, 0),
        ], strings, modules, types, signatures, 0, 0, schema);
        var methods = new TrRmMethodDefTableReader(methodBytes, 3,
            strings, modules, types, signatures, 0, 0, schema);
        return TrRmPropertyDefTableWriter.Encode([row], strings, types, specs,
            methods, signatures, schema, fields, parameters);
    }

    private static int ResolvePI1PropertyBody(uint getterBody,
        uint otherAccessorBody, bool useInitializer)
    {
        var schema
            = TrRmArtifactSchema.C1M1ConstructionL1R1PropertyInitializedStateDraft;
        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Widget", 0), ("Read", 0),
            ("Write", 0), ("Initialize", 0), ("Value", 0),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 7);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 1, 3, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 1, 3, 0);
        var typeDefBytes = new byte[44];
        Write(typeDefBytes, 0, 1);
        Write(typeDefBytes, 4, 3);
        Write(typeDefBytes, 8, 8);
        Write(typeDefBytes, 12, 1);
        Write(typeDefBytes, 16, 1);
        var types = new TrRmTypeDefTableReader(typeDefBytes, 1, strings,
            modules, 1, 0, 0, 0, schema);
        var typeSpecBytes = Specs();
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes, 2, 1,
            0, 0, 0);
        var fieldBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(1, 0, 1, 0x10),
        ], strings, types, typeSpecs, schema);
        var fields = new TrRmFieldDefTableReader(fieldBytes, 1,
            strings, types, typeSpecs, schema);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 2, 0, 0, 0, 0),
            new TrRmMethodSigRow(0xF2000000, 2, 1, 1, 0, 0),
            new TrRmMethodSigRow(0xF2000000, 2, 2, 1, 0, 0),
        ], typeSpecs, 2, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 3,
            typeSpecs, 2, 0);
        var parameterBytes = TrRmParamTableWriter.Encode([
            new TrRmParamRow(2, 0, 1, 0),
            new TrRmParamRow(3, 0, 1, 0),
        ], signatures, typeSpecs, schema);
        var parameters = new TrRmParamTableReader(parameterBytes, 2,
            signatures, typeSpecs, schema);
        var bodyCount = getterBody == 0 && otherAccessorBody == 0 ? 0 : 1;
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(1, 4, 1, 0, 0, 0, getterBody),
            new TrRmMethodDefRow(1, 5, 2, 0, 0, 0, otherAccessorBody),
            new TrRmMethodDefRow(1, 6, 3, 0, 0, 0, 0),
        ], strings, modules, types, signatures, 0, bodyCount, schema);
        var methods = new TrRmMethodDefTableReader(methodBytes, 3,
            strings, modules, types, signatures, 0, bodyCount, schema);
        var property = new TrRmPropertyDefRow(1, 7, 1, 1, 0) {
            SetterMethodDefRow = useInitializer ? 0 : 2,
            InitMethodDefRow = useInitializer ? 2 : 0,
            BackingFieldDefRow = 1,
        };
        var propertyBytes = TrRmPropertyDefTableWriter.Encode([property],
            strings, types, typeSpecs, methods, signatures, schema, fields,
            parameters);
        var properties = new TrRmPropertyDefTableReader(propertyBytes, 1,
            strings, types, typeSpecs, methods, signatures, false, null,
            schema, fields, parameters);

        return TrRmDefinitionResolver.Resolve(0x1E000001, types, fields,
            methods, properties, default, default, default, schema).Body;
    }

    private static byte[] ConstructionPayload(byte role, uint subject)
    {
        var payload = new byte[role == 1 ? 12 : 8];
        payload[0] = role;
        Write(payload, 4, role == 1 ? 1u : subject);
        if (role == 1)
        {
            Write(payload, 8, subject);
        }

        return payload;
    }

    private static byte[] Names()
        => TrRmStringTableWriter.EncodeTextRows([
            ("", 0), ("Module", 0), ("Widget", 0), ("Read", 0), ("Value", 0),
        ]);

    private static byte[] Modules(TrRmStringTableReader strings)
        => TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 0, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 1, 1, 0);

    private static byte[] Types(TrRmStringTableReader strings, TrRmModuleTableReader modules)
        => TrRmTypeDefTableWriter.Encode([
            new TrRmTypeDefRow(1, 3, 4, 0, 0, 0, 0, 1, 1, 0, 0),
        ], strings, modules, 0, 0, 1, 0);

    private static byte[] Specs()
        => TrRmTypeSpecTableWriter.Encode([
            new byte[] { 1, 0, 8 },
            new byte[] { 2, 0, 1, 0, 0, 0 },
        ], 1, 0, 0, 0);

    private static byte[] Signatures(TrRmTypeSpecTableReader specs)
        => TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(1, 2, 0, 0, 0, 0),
        ], specs, 0, 0);

    private static byte[] Methods(TrRmStringTableReader strings, TrRmModuleTableReader modules,
        TrRmTypeDefTableReader types, TrRmMethodSigTableReader signatures)
        => TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(1, 4, 1, 0, 0, 0, 0),
        ], strings, modules, types, signatures, 0, 0);
}
