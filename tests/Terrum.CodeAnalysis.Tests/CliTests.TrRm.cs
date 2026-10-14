// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Test]
    public async Task ValidateTrRmStructureAcceptsMinimalArtifactAndRejectsCorruptionAsync()
    {
        var module = new byte[44];
        foreach (var offset in new[] { 0, 4, 28, 32, 36, 40 })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(module.AsSpan(offset), 1);
        }
        var strings = TrRmStringTableWriter.EncodeNames([("A", 0)]);
        var artifact = TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, module),
            new TrRmSection(0x0002, 0, 1, strings),
        ]);
        var path = Path.Combine(Path.GetTempPath(), $"terrum-trrm-structure-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(path, artifact);
            var (exit, output, error) = await RunAsync([], "validate-trrm-structure", path, "--json");
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(error, Is.Empty);
            using (var result = JsonDocument.Parse(output))
            {
                Assert.That(result.RootElement.GetProperty("success").GetBoolean(), Is.True);
                Assert.That(result.RootElement.GetProperty("scope").GetString(), Is.EqualTo("framing-and-structural-metadata"));
            }

            var stringStart = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(48 + 8)));
            artifact[stringStart + strings.Length - 1] = 0xFF;
            await File.WriteAllBytesAsync(path, artifact);
            var (invalidExit, invalidOutput, invalidError) = await RunAsync([],
                "validate-trrm-structure", path, "--json");
            Assert.That(invalidExit, Is.EqualTo(1));
            Assert.That(invalidError, Is.Empty);
            using var invalid = JsonDocument.Parse(invalidOutput);
            Assert.That(invalid.RootElement.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(invalid.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ValidateTrRmStructureReportsMissingInputAndUsageAsync()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"terrum-trrm-missing-{Guid.NewGuid():N}.trrm");
        var (missingExit, missingOutput, _) = await RunAsync([],
            "validate-trrm-structure", missing, "--json");
        Assert.That(missingExit, Is.EqualTo(2));
        using (var result = JsonDocument.Parse(missingOutput))
        {
            Assert.That(result.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString(), Is.EqualTo("TRM2001"));
        }
        var (usageExit, usageOutput, _) = await RunAsync([],
            "validate-trrm-structure", "-", "--json");
        Assert.That(usageExit, Is.EqualTo(2));
        using var usage = JsonDocument.Parse(usageOutput);
        Assert.That(usage.RootElement.GetProperty("diagnostics")[0]
            .GetProperty("code").GetString(), Is.EqualTo("TRM2000"));
    }

    [Test]
    public async Task ValidateTrRmCompactLinearChecksBodiesWithoutChangingStructuralValidationAsync()
    {
        var module = new byte[44];
        foreach (var offset in new[] { 0, 4, 28, 32, 36, 40 })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(module.AsSpan(offset), 1);
        }

        var strings = TrRmStringTableWriter.EncodeNames(
            [("Module", 0), ("Run", 0), ("src/file", 0)]);
        byte[] typeSpec = [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];
        var signature = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(signature, 1);
        var method = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(24), 1);
        byte[] body =
        [
            0, 0, 0, 0, 13, 0, 0, 0,
            0x04, 1, 1, 0, 0, 0, 1, 1, 1, 1, 42, 0x83, 1,
        ];
        var sourceFile = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(sourceFile, 3);
        var sourceMap = new byte[52];
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap, 2);
        BinaryPrimitives.WriteUInt16LittleEndian(sourceMap.AsSpan(4), 0x0020);
        BinaryPrimitives.WriteUInt16LittleEndian(sourceMap.AsSpan(6), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(sourceMap.AsSpan(20), 0x001F);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(36), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(40), 2);
        byte[] Encode() => TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, module),
            new TrRmSection(0x0002, 0, 3, strings),
            new TrRmSection(0x0011, 0, 1, typeSpec),
            new TrRmSection(0x0016, 0, 1, method),
            new TrRmSection(0x0017, 0, 1, signature),
            new TrRmSection(0x001F, 0, 1, sourceFile),
            new TrRmSection(0x0020, 0, 1, body),
            new TrRmSection(0x7F00, 2, 1, sourceMap),
        ]);

        var path = Path.Combine(Path.GetTempPath(), $"terrum-trrm-compact-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(path, Encode());
            var (exit, output, error) = await RunAsync([],
                "validate-trrm-compact-linear", path, "--json");
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(error, Is.Empty);
            using (var result = JsonDocument.Parse(output))
            {
                Assert.That(result.RootElement.GetProperty("success").GetBoolean(), Is.True);
                Assert.That(result.RootElement.GetProperty("scope").GetString(), Is.EqualTo("framing-structural-metadata-and-compact-linear-shapes"));
            }

            BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(16), 0);
            await File.WriteAllBytesAsync(path, Encode());
            var (operationExit, _, _) = await RunAsync([],
                "validate-trrm-compact-linear", path, "--json");
            Assert.That(operationExit, Is.EqualTo(0));

            BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(16), 2);
            await File.WriteAllBytesAsync(path, Encode());
            var (structuralMapExit, _, _) = await RunAsync([],
                "validate-trrm-structure", path, "--json");
            Assert.That(structuralMapExit, Is.EqualTo(0));
            var (invalidMapExit, _, _) = await RunAsync([],
                "validate-trrm-compact-linear", path, "--json");
            Assert.That(invalidMapExit, Is.EqualTo(1));

            BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(16), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(12), 2);
            await File.WriteAllBytesAsync(path, Encode());
            var (invalidBlockExit, _, _) = await RunAsync([],
                "validate-trrm-compact-linear", path, "--json");
            Assert.That(invalidBlockExit, Is.EqualTo(1));

            BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(12), 1);
            body[8] = 0x08;
            await File.WriteAllBytesAsync(path, Encode());
            var (structuralExit, _, _) = await RunAsync([],
                "validate-trrm-structure", path, "--json");
            Assert.That(structuralExit, Is.EqualTo(0));
            var (invalidExit, invalidOutput, invalidError) = await RunAsync([],
                "validate-trrm-compact-linear", path, "--json");
            Assert.That(invalidExit, Is.EqualTo(1));
            Assert.That(invalidError, Is.Empty);
            using var invalid = JsonDocument.Parse(invalidOutput);
            Assert.That(invalid.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ValidateTrRmCompactLinearChecksPersistedLocalCallAsync()
    {
        var module = new byte[44];
        foreach (var offset in new[] { 0, 4, 28, 32, 36, 40 })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(module.AsSpan(offset), 1);
        }

        var strings = TrRmStringTableWriter.EncodeTextRows(
            [("Module", 0), ("Call", 0), ("Target", 0), ("", 0)]);
        byte[] typeSpec = [0, 0, 0, 0, 3, 0, 0, 0, 1, 0, 8];
        var signature = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(signature, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(signature.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(signature.AsSpan(12), 1);
        var parameter = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(parameter, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(parameter.AsSpan(8), 1);
        var methods = new byte[56];
        foreach (var start in new[] { 0, 28 })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(methods.AsSpan(start + 4),
                (uint)(start / 28 + 2));
            BinaryPrimitives.WriteUInt32LittleEndian(methods.AsSpan(start + 8), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(methods.AsSpan(start + 12), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(methods.AsSpan(start + 24),
                (uint)(start / 28 + 1));
        }

        byte[] caller = [0x04, 1, 1, 0, 0, 0, 1, 0x12, 2, 2, 1, 1, 0x83, 2];
        byte[] callee = [0x04, 1, 1, 0, 0, 0, 0, 0x83, 1];
        var moduleRef = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(moduleRef, 3);
        var import = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(import, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(import.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(import.AsSpan(8), 0x1600_0002);
        var owner = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(owner, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(owner.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(owner.AsSpan(8), 6);
        byte[] Artifact(byte[] moduleBytes, uint moduleCount, byte[] callerBody,
            bool imported = false, bool typeOwned = false)
        {
            var sections = new List<TrRmSection>
            {
                new(0x0001, 0, moduleCount, moduleBytes),
                new(0x0002, 0, 4, strings),
            };
            if (imported)
            {
                sections.Add(new(0x0010, 0, 1, moduleRef));
            }

            sections.Add(new(0x0011, 0, 1, typeSpec));
            if (typeOwned)
            {
                sections.Add(new(0x0013, 0, 1, owner));
            }

            sections.Add(new(0x0016, 0, 2, methods));
            sections.Add(new(0x0017, 0, 1, signature));
            sections.Add(new(0x0018, 0, 1, parameter));
            if (imported)
            {
                sections.Add(new(0x001C, 0, 1, import));
            }

            sections.Add(new(0x0020, 0, imported ? 1u : 2u,
                imported ? TrRmVariableTableWriter.EncodeBodies([callerBody])
                    : TrRmVariableTableWriter.EncodeBodies([callerBody, callee])));
            return TrRmArtifactWriter.Encode(sections);
        }

        var path = Path.Combine(AppContext.BaseDirectory,
            $"terrum-trrm-linear-call-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(path, Artifact(module, 1, caller));
            foreach (var command in new[] { "validate-trrm-structure",
                "validate-trrm-compact-linear", "validate-trrm-compact" })
            {
                var (valid, output, error) = await RunAsync([], command, path, "--json");
                Assert.That(valid == 0, Is.True, output + error);
            }

            caller[9] = 0;
            await File.WriteAllBytesAsync(path, Artifact(module, 1, caller));
            var (structural, _, _) = await RunAsync([],
                "validate-trrm-structure", path, "--json");
            Assert.That(structural, Is.EqualTo(0));
            var (invalid, _, _) = await RunAsync([],
                "validate-trrm-compact-linear", path, "--json");
            Assert.That(invalid, Is.EqualTo(1));

            var packedModule = new byte[88];
            module.CopyTo(packedModule, 0);
            foreach (var offset in new[] { 72, 76, 84 })
            {
                BinaryPrimitives.WriteUInt32LittleEndian(packedModule.AsSpan(offset), 1);
            }

            BinaryPrimitives.WriteUInt32LittleEndian(packedModule.AsSpan(44), 3);
            BinaryPrimitives.WriteUInt32LittleEndian(packedModule.AsSpan(80), 2);
            byte[] packedCaller =
            [
                0x04, 1, 1, 0, 0, 0, 1,
                0x12, 2, 0, 2, 0, 0, 0x16, 1, 1, 0x83, 2,
            ];
            await File.WriteAllBytesAsync(path, Artifact(packedModule, 2, packedCaller));
            var (packedValid, packedOutput, packedError) = await RunAsync([],
                "validate-trrm-compact-linear", path, "--json");
            Assert.That(packedValid == 0, Is.True, packedOutput + packedError);
            methods[40] = 0;
            await File.WriteAllBytesAsync(path, Artifact(packedModule, 2, packedCaller));
            var (packedStructural, _, _) = await RunAsync([],
                "validate-trrm-structure", path, "--json");
            Assert.That(packedStructural, Is.EqualTo(0));
            var (packedInvalid, _, _) = await RunAsync([],
                "validate-trrm-compact-linear", path, "--json");
            Assert.That(packedInvalid, Is.EqualTo(1));

            methods[40] = 2;
            BinaryPrimitives.WriteUInt32LittleEndian(methods.AsSpan(52), 0);
            byte[] importedCaller = [0x04, 1, 1, 0, 0, 0, 1, 0x12, 2, 2, 1, 1, 0x83, 2];
            await File.WriteAllBytesAsync(path, Artifact(module, 1, importedCaller,
                imported: true));
            var (importedValid, importedOutput, importedError) = await RunAsync([],
                "validate-trrm-compact-linear", path, "--json");
            Assert.That(importedValid == 0, Is.True, importedOutput + importedError);

            BinaryPrimitives.WriteUInt32LittleEndian(moduleRef.AsSpan(8), 1);
            await File.WriteAllBytesAsync(path, Artifact(module, 1, importedCaller,
                imported: true));
            var (importedStructural, _, _) = await RunAsync([],
                "validate-trrm-structure", path, "--json");
            Assert.That(importedStructural, Is.EqualTo(0));
            var (importedInvalid, _, _) = await RunAsync([],
                "validate-trrm-compact-linear", path, "--json");
            Assert.That(importedInvalid, Is.EqualTo(1));

            BinaryPrimitives.WriteUInt32LittleEndian(methods.AsSpan(28), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(moduleRef.AsSpan(8), 0);
            foreach (var (moduleBytes, moduleCount, callBody, imported) in new[]
            {
                (packedModule, 2u, packedCaller, false),
                (module, 1u, importedCaller, true),
            })
            {
                BinaryPrimitives.WriteUInt32LittleEndian(methods.AsSpan(52),
                    imported ? 0u : 2u);
                await File.WriteAllBytesAsync(path, Artifact(moduleBytes, moduleCount,
                    callBody, imported, typeOwned: true));
                foreach (var command in new[] { "validate-trrm-structure",
                    "validate-trrm-compact-linear", "validate-trrm-compact" })
                {
                    var (valid, output, error) = await RunAsync([], command, path, "--json");
                    Assert.That(valid == 0, Is.True, output + error);
                }

                BinaryPrimitives.WriteUInt32LittleEndian(owner.AsSpan(8), 4);
                await File.WriteAllBytesAsync(path, Artifact(moduleBytes, moduleCount,
                    callBody, imported, typeOwned: true));
                var (ownerStructural, _, _) = await RunAsync([],
                    "validate-trrm-structure", path, "--json");
                Assert.That(ownerStructural, Is.EqualTo(0));
                var (ownerInvalid, _, _) = await RunAsync([],
                    "validate-trrm-compact-linear", path, "--json");
                Assert.That(ownerInvalid, Is.EqualTo(1));
                BinaryPrimitives.WriteUInt32LittleEndian(owner.AsSpan(8), 6);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ValidateTrRmCompactIndexedChecksBodiesAndSourceLocationsAsync()
    {
        var module = new byte[44];
        foreach (var offset in new[] { 0, 4, 28, 32, 36, 40 })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(module.AsSpan(offset), 1);
        }

        var strings = TrRmStringTableWriter.EncodeNames(
            [("Module", 0), ("Run", 0), ("src/file", 0)]);
        byte[] typeSpec =
        [
            0, 0, 0, 0, 3, 0, 0, 0, 6, 0, 0, 0,
            1, 0, 1, 1, 0, 8,
        ];
        var signature = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(signature, 2);
        BinaryPrimitives.WriteUInt32LittleEndian(signature.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(signature.AsSpan(12), 3);
        var parameters = new byte[48];
        for (var index = 0; index < 3; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(parameters.AsSpan(index * 16), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(parameters.AsSpan(index * 16 + 4), (uint)index);
            BinaryPrimitives.WriteUInt32LittleEndian(parameters.AsSpan(index * 16 + 8),
                index == 0 ? 1u : 2u);
        }

        var method = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(24), 1);
        var body = TrRmBodyExampleTests.ReadExample("#### Compact indexed conditional sum");
        var sourceFile = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(sourceFile, 3);
        var sourceMap = new byte[52];
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap, 2);
        BinaryPrimitives.WriteUInt16LittleEndian(sourceMap.AsSpan(4), 0x0020);
        BinaryPrimitives.WriteUInt16LittleEndian(sourceMap.AsSpan(6), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(sourceMap.AsSpan(20), 0x001F);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(36), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(40), 2);
        byte[] Encode(byte[]? methodRows = null, int methodCount = 1,
            byte[]? bodyRows = null, int bodyCount = 1,
            byte[]? mapRows = null, int mapCount = 1) => TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, module),
            new TrRmSection(0x0002, 0, 3, strings),
            new TrRmSection(0x0011, 0, 2, typeSpec),
            new TrRmSection(0x0016, 0, (uint)methodCount, methodRows ?? method),
            new TrRmSection(0x0017, 0, 1, signature),
            new TrRmSection(0x0018, 0, 3, parameters),
            new TrRmSection(0x001F, 0, 1, sourceFile),
            new TrRmSection(0x0020, 0, (uint)bodyCount, bodyRows ?? body),
            new TrRmSection(0x7F00, 2, (uint)mapCount, mapRows ?? sourceMap),
        ]);

        var path = Path.Combine(Path.GetTempPath(), $"terrum-trrm-indexed-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(path, Encode());
            var (exit, output, error) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(error, Is.Empty);
            using (var result = JsonDocument.Parse(output))
            {
                Assert.That(result.RootElement.GetProperty("success").GetBoolean(), Is.True);
                Assert.That(result.RootElement.GetProperty("scope").GetString(), Is.EqualTo("framing-structural-metadata-and-compact-indexed-shapes"));
            }

            BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(16), 2);
            await File.WriteAllBytesAsync(path, Encode());
            var (structuralExit, _, _) = await RunAsync([],
                "validate-trrm-structure", path, "--json");
            Assert.That(structuralExit, Is.EqualTo(0));
            var (invalidMapExit, _, _) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            Assert.That(invalidMapExit, Is.EqualTo(1));

            BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(16), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(12), 4);
            await File.WriteAllBytesAsync(path, Encode());
            var (invalidBlockExit, _, _) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            Assert.That(invalidBlockExit, Is.EqualTo(1));

            BinaryPrimitives.WriteUInt32LittleEndian(sourceMap.AsSpan(12), 2);
            body[77] = 4;
            await File.WriteAllBytesAsync(path, Encode());
            var (opaqueExit, _, _) = await RunAsync([],
                "validate-trrm-structure", path, "--json");
            Assert.That(opaqueExit, Is.EqualTo(0));
            var (invalidBodyExit, invalidOutput, _) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            Assert.That(invalidBodyExit, Is.EqualTo(1));
            using var invalid = JsonDocument.Parse(invalidOutput);
            Assert.That(invalid.RootElement.GetProperty("diagnostics")[0]
                .GetProperty("code").GetString(), Is.EqualTo("TRM5001"));

            body[77] = 2;
            var methodRows = new byte[56];
            method.CopyTo(methodRows, 0);
            method.CopyTo(methodRows, 28);
            BinaryPrimitives.WriteUInt32LittleEndian(methodRows.AsSpan(52), 2);
            byte[] linearRow =
            [
                0x04, 2, 1, 0, 0, 0, 2, 0, 0, 0, 0, 0x83, 2,
            ];
            var bodyRows = new byte[12 + body.Length - 8 + linearRow.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(bodyRows.AsSpan(4), (uint)(body.Length - 8));
            BinaryPrimitives.WriteUInt32LittleEndian(bodyRows.AsSpan(8),
                (uint)(body.Length - 8 + linearRow.Length));
            body.AsSpan(8).CopyTo(bodyRows.AsSpan(12));
            linearRow.CopyTo(bodyRows, 12 + body.Length - 8);
            var mixedMap = new byte[84];
            BinaryPrimitives.WriteUInt32LittleEndian(mixedMap, 3);
            sourceMap.AsSpan(4, 32).CopyTo(mixedMap.AsSpan(4));
            BinaryPrimitives.WriteUInt16LittleEndian(mixedMap.AsSpan(36), 0x0020);
            BinaryPrimitives.WriteUInt16LittleEndian(mixedMap.AsSpan(38), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(mixedMap.AsSpan(40), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(mixedMap.AsSpan(44), 1);
            sourceMap.AsSpan(36, 16).CopyTo(mixedMap.AsSpan(52));
            BinaryPrimitives.WriteUInt32LittleEndian(mixedMap.AsSpan(68), 3);
            BinaryPrimitives.WriteUInt32LittleEndian(mixedMap.AsSpan(72), 2);
            await File.WriteAllBytesAsync(path, Encode(methodRows, 2, bodyRows, 2, mixedMap, 2));
            var (mixedExit, mixedOutput, _) = await RunAsync([],
                "validate-trrm-compact", path, "--json");
            Assert.That(mixedExit, Is.EqualTo(0));
            using (var result = JsonDocument.Parse(mixedOutput))
            {
                Assert.That(result.RootElement.GetProperty("scope").GetString(), Is.EqualTo("framing-structural-metadata-and-compact-shapes"));
            }

            var (indexedOnlyExit, _, _) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            var (linearOnlyExit, _, _) = await RunAsync([],
                "validate-trrm-compact-linear", path, "--json");
            Assert.That(indexedOnlyExit, Is.EqualTo(1));
            Assert.That(linearOnlyExit, Is.EqualTo(1));

            BinaryPrimitives.WriteUInt32LittleEndian(mixedMap.AsSpan(48), 1);
            await File.WriteAllBytesAsync(path, Encode(methodRows, 2, bodyRows, 2, mixedMap, 2));
            var (invalidLinearLocationExit, _, _) = await RunAsync([],
                "validate-trrm-compact", path, "--json");
            Assert.That(invalidLinearLocationExit, Is.EqualTo(1));

            BinaryPrimitives.WriteUInt32LittleEndian(mixedMap.AsSpan(48), 0);
            bodyRows[12 + body.Length - 8] = 0;
            await File.WriteAllBytesAsync(path, Encode(methodRows, 2, bodyRows, 2, mixedMap, 2));
            var (unsupportedExit, _, _) = await RunAsync([],
                "validate-trrm-compact", path, "--json");
            Assert.That(unsupportedExit, Is.EqualTo(1));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ValidateTrRmCompactIndexedChecksPersistedCallMethodRowsAsync()
    {
        var module = new byte[44];
        foreach (var offset in new[] { 0, 4, 28, 32, 36, 40 })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(module.AsSpan(offset), 1);
        }

        var strings = TrRmStringTableWriter.EncodeNames([("Module", 0), ("Run", 0)]);
        byte[] typeSpec =
        [
            0, 0, 0, 0, 3, 0, 0, 0, 6, 0, 0, 0,
            1, 0, 1, 1, 0, 8,
        ];
        var signature = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(signature, 2);
        var method = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(24), 1);
        var body = TrRmBodyExampleTests.IndexedScalarOperation([0x12, 1, 1, 0], [], 2);
        byte[] Artifact(bool withImport = false, uint importTarget = 0, uint importPolicy = 0)
        {
            var sections = new List<TrRmSection>
            {
                new(0x0001, 0, 1, module),
                new(0x0002, 0, withImport ? 3u : 2u,
                    withImport ? TrRmStringTableWriter.EncodeNames(
                        [("Module", 0), ("Run", 0), ("Producer", 0)]) : strings),
            };
            if (withImport)
            {
                var moduleRef = new byte[12];
                BinaryPrimitives.WriteUInt32LittleEndian(moduleRef, 3);
                BinaryPrimitives.WriteUInt32LittleEndian(moduleRef.AsSpan(4), importTarget);
                BinaryPrimitives.WriteUInt32LittleEndian(moduleRef.AsSpan(8), importPolicy);
                sections.Add(new TrRmSection(0x0010, 0, 1, moduleRef));
            }

            sections.Add(new TrRmSection(0x0011, 0, 2, typeSpec));
            if (withImport)
            {
                var methods = new byte[56];
                method.CopyTo(methods, 0);
                BinaryPrimitives.WriteUInt32LittleEndian(methods.AsSpan(32), 2);
                BinaryPrimitives.WriteUInt32LittleEndian(methods.AsSpan(36), 1);
                BinaryPrimitives.WriteUInt32LittleEndian(methods.AsSpan(40), 2);
                sections.Add(new TrRmSection(0x0016, 0, 2, methods));
            }
            else
            {
                sections.Add(new TrRmSection(0x0016, 0, 1, method));
            }

            sections.Add(new TrRmSection(0x0017, 0, 1, signature));
            if (withImport)
            {
                var import = new byte[12];
                BinaryPrimitives.WriteUInt32LittleEndian(import, 1);
                BinaryPrimitives.WriteUInt32LittleEndian(import.AsSpan(4), 2);
                BinaryPrimitives.WriteUInt32LittleEndian(import.AsSpan(8), 0x1600_0002);
                sections.Add(new TrRmSection(0x001C, 0, 1, import));
            }

            sections.Add(new TrRmSection(0x0020, 0, 1, body));
            return TrRmArtifactWriter.Encode([.. sections]);
        }

        var path = Path.Combine(Path.GetTempPath(), $"terrum-trrm-call-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(path, Artifact());
            var (valid, output, error) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            Assert.That(valid, Is.EqualTo(0));
            Assert.That(error, Is.Empty);
            using (var result = JsonDocument.Parse(output))
            {
                Assert.That(result.RootElement.GetProperty("success").GetBoolean(), Is.True);
            }

            byte[] operationBytes = [0x12, 1, 1, 0, 0x83, 1];
            var operation = body.AsSpan().IndexOf(operationBytes);
            Assert.That(operation >= 0, Is.True);
            body[operation + 2] = 2;
            await File.WriteAllBytesAsync(path, Artifact());
            var (structural, _, _) = await RunAsync([],
                "validate-trrm-structure", path, "--json");
            Assert.That(structural, Is.EqualTo(0));
            var (invalid, _, _) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            Assert.That(invalid, Is.EqualTo(1));

            await File.WriteAllBytesAsync(path, Artifact(withImport: true));
            var (imported, importedOutput, importedError) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            Assert.That(imported, Is.EqualTo(0));
            Assert.That(importedError, Is.Empty);
            using (var result = JsonDocument.Parse(importedOutput))
            {
                Assert.That(result.RootElement.GetProperty("success").GetBoolean(), Is.True);
            }

            BinaryPrimitives.WriteUInt32LittleEndian(module.AsSpan(4), 0);
            foreach (var (target, policy) in new (uint Target, uint Policy)[]
                { (1, 0), (0, 1) })
            {
                await File.WriteAllBytesAsync(path,
                    Artifact(withImport: true, importTarget: target, importPolicy: policy));
                var (importStructure, _, _) = await RunAsync([],
                    "validate-trrm-structure", path, "--json");
                Assert.That(importStructure, Is.EqualTo(0));
                var (incompatibleImport, _, _) = await RunAsync([],
                    "validate-trrm-compact-indexed", path, "--json");
                Assert.That(incompatibleImport, Is.EqualTo(1));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ValidateTrRmCompactIndexedChecksPersistedChar16RangeConvertAsync()
    {
        var module = new byte[44];
        foreach (var offset in new[] { 0, 4, 28, 32, 36, 40 })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(module.AsSpan(offset), 1);
        }

        var strings = TrRmStringTableWriter.EncodeNames([("Module", 0), ("Convert", 0)]);
        byte[] typeSpec =
        [
            0, 0, 0, 0, 3, 0, 0, 0, 6, 0, 0, 0,
            1, 0, 3, 1, 0, 8,
        ];
        var signature = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(signature, 2);
        BinaryPrimitives.WriteUInt32LittleEndian(signature.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(signature.AsSpan(12), 1);
        var parameter = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(parameter, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(parameter.AsSpan(8), 1);
        var method = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(24), 1);

        byte[] Artifact(byte[] operation, byte sourceType, byte resultType)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(signature, resultType);
            BinaryPrimitives.WriteUInt32LittleEndian(parameter.AsSpan(8), sourceType);
            var body = TrRmBodyExampleTests.IndexedScalarOperation(
                operation, [sourceType], resultType);
            return TrRmArtifactWriter.Encode([
                new TrRmSection(0x0001, 0, 1, module),
                new TrRmSection(0x0002, 0, 2, strings),
                new TrRmSection(0x0011, 0, 2, typeSpec),
                new TrRmSection(0x0016, 0, 1, method),
                new TrRmSection(0x0017, 0, 1, signature),
                new TrRmSection(0x0018, 0, 1, parameter),
                new TrRmSection(0x0020, 0, 1, body),
            ]);
        }

        var path = Path.Combine(AppContext.BaseDirectory,
            $"terrum-trrm-char16-convert-{Guid.NewGuid():N}.trrm");
        try
        {
            foreach (var (source, destination) in new[] { ((byte)1, (byte)2),
                ((byte)2, (byte)1) })
            {
                await File.WriteAllBytesAsync(path, Artifact(
                    [0x16, 2, destination, 1], source, destination));
                foreach (var command in new[] { "validate-trrm-structure",
                    "validate-trrm-compact-indexed", "validate-trrm-compact" })
                {
                    var (valid, output, error) = await RunAsync([], command, path, "--json");
                    Assert.That(valid == 0, Is.True, output + error);
                }
            }

            await File.WriteAllBytesAsync(path, Artifact([6, 2, 2, 1, 1], 1, 2));
            var (structural, _, _) = await RunAsync([],
                "validate-trrm-structure", path, "--json");
            Assert.That(structural, Is.EqualTo(0));
            var (invalid, _, _) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            Assert.That(invalid, Is.EqualTo(1));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ValidateTrRmCompactIndexedChecksPackedCallDefIdsAsync()
    {
        var modules = new byte[88];
        foreach (var offset in new[] { 0, 4, 28, 32, 36, 40, 48, 72, 76, 84 })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(modules.AsSpan(offset), 1);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(modules.AsSpan(44), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(modules.AsSpan(80), 2);
        var strings = TrRmStringTableWriter.EncodeTextRows(
            [("Module", 0), ("Producer", 0), ("", 0)]);
        byte[] typeSpec =
        [
            0, 0, 0, 0, 3, 0, 0, 0, 6, 0, 0, 0,
            1, 0, 1, 1, 0, 8,
        ];
        var signature = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(signature, 2);
        BinaryPrimitives.WriteUInt32LittleEndian(signature.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(signature.AsSpan(12), 1);
        var parameter = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(parameter, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(parameter.AsSpan(8), 2);
        var methods = new byte[56];
        foreach (var offset in new[] { 4, 32 })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(methods.AsSpan(offset), 2);
        }
        foreach (var offset in new[] { 8, 24, 36 })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(methods.AsSpan(offset), 1);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(methods.AsSpan(40), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(methods.AsSpan(52), 2);
        var owner = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(owner, 3);
        BinaryPrimitives.WriteUInt32LittleEndian(owner.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(owner.AsSpan(8), 6);
        var formal = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(formal, 0x1300_0001);
        BinaryPrimitives.WriteUInt32LittleEndian(formal.AsSpan(8), 2);
        var caller = TrRmBodyExampleTests.IndexedScalarOperation(
            [0x12, 2, 0, 2, 0, 0, 0x16, 1, 1], [2], 2);
        var producer = TrRmBodyExampleTests.IndexedScalarOperation([3, 2, 1], [2], 2);
        var callerLength = caller.Length - 8;
        var body = new byte[12 + callerLength + producer.Length - 8];
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), (uint)callerLength);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8), (uint)(body.Length - 12));
        caller.AsSpan(8).CopyTo(body.AsSpan(12));
        producer.AsSpan(8).CopyTo(body.AsSpan(12 + callerLength));
        byte[] Artifact(bool typeOwned = false, bool genericOwner = false)
        {
            var sections = new List<TrRmSection>
            {
                new(0x0001, 0, 2, modules),
                new(0x0002, 0, 3, strings),
                new(0x0011, 0, 2, typeSpec),
            };
            if (typeOwned)
            {
                sections.Add(new(0x0013, 0, 1, owner));
            }

            sections.Add(new(0x0016, 0, 2, methods));
            sections.Add(new(0x0017, 0, 1, signature));
            sections.Add(new(0x0018, 0, 1, parameter));
            if (genericOwner)
            {
                sections.Add(new(0x0019, 0, 1, formal));
            }

            sections.Add(new(0x0020, 0, 2, body));
            return TrRmArtifactWriter.Encode(sections);
        }

        var path = Path.Combine(AppContext.BaseDirectory,
            $"terrum-trrm-packed-call-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(path, Artifact());
            var (valid, output, error) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            Assert.That(valid, Is.EqualTo(0));
            Assert.That(error, Is.Empty);
            using (var result = JsonDocument.Parse(output))
            {
                Assert.That(result.RootElement.GetProperty("success").GetBoolean(), Is.True);
            }

            BinaryPrimitives.WriteUInt32LittleEndian(methods.AsSpan(40), 0);
            await File.WriteAllBytesAsync(path, Artifact());
            var (privateStructural, _, _) = await RunAsync([],
                "validate-trrm-structure", path, "--json");
            Assert.That(privateStructural, Is.EqualTo(0));
            var (privateCall, _, _) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            Assert.That(privateCall, Is.EqualTo(1));
            BinaryPrimitives.WriteUInt32LittleEndian(methods.AsSpan(40), 2);

            BinaryPrimitives.WriteUInt32LittleEndian(modules.AsSpan(4), 0);
            await File.WriteAllBytesAsync(path, Artifact());
            var (targetStructural, _, _) = await RunAsync([],
                "validate-trrm-structure", path, "--json");
            Assert.That(targetStructural, Is.EqualTo(0));
            var (targetMismatch, _, _) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            Assert.That(targetMismatch, Is.EqualTo(1));
            BinaryPrimitives.WriteUInt32LittleEndian(modules.AsSpan(4), 1);
            await File.WriteAllBytesAsync(path, Artifact());
            var (compatibleTarget, _, _) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            Assert.That(compatibleTarget, Is.EqualTo(0));

            BinaryPrimitives.WriteUInt32LittleEndian(methods.AsSpan(28), 1);
            await File.WriteAllBytesAsync(path, Artifact(typeOwned: true));
            var (ownedValid, ownedOutput, ownedError) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            Assert.That(ownedValid == 0, Is.True, ownedOutput + ownedError);

            BinaryPrimitives.WriteUInt32LittleEndian(owner.AsSpan(20), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(owner.AsSpan(24), 1);
            await File.WriteAllBytesAsync(path, Artifact(typeOwned: true, genericOwner: true));
            var (genericStructural, _, _) = await RunAsync([],
                "validate-trrm-structure", path, "--json");
            Assert.That(genericStructural, Is.EqualTo(0));
            var (genericInvalid, _, _) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            Assert.That(genericInvalid, Is.EqualTo(1));
            BinaryPrimitives.WriteUInt32LittleEndian(methods.AsSpan(28), 0);

            byte[] encodedCall = [0x12, 2, 0, 2, 0, 0, 0x16, 1, 1];
            var call = body.AsSpan().IndexOf(encodedCall);
            Assert.That(call >= 0, Is.True);
            body[call + 3] = 1;
            await File.WriteAllBytesAsync(path, Artifact());
            var (structural, _, _) = await RunAsync([],
                "validate-trrm-structure", path, "--json");
            Assert.That(structural, Is.EqualTo(0));
            var (invalid, _, _) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            Assert.That(invalid, Is.EqualTo(1));
        }
        finally
        {
            File.Delete(path);
        }
    }
    [TestCase(false,false)]
    [TestCase(true,false)]
    [TestCase(false,true)]
    public async Task ValidateTrRmCompactIndexedChecksPersistedLoopRegionAsync(
        bool siblingLoop, bool nestedLoop)
    {
        var module = new byte[44];
        foreach (var offset in new[] { 0, 4, 28, 32, 36, 40 })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(module.AsSpan(offset), 1);
        }

        var strings = TrRmStringTableWriter.EncodeNames([("Module", 0), ("Loop", 0)]);
        byte[] typeSpec =
        [
            0, 0, 0, 0, 3, 0, 0, 0, 6, 0, 0, 0,
            1, 0, 1, 1, 0, 8,
        ];
        var method = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(method.AsSpan(24), 1);
        var signature = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(signature, nestedLoop ? 1u : 2u);
        BinaryPrimitives.WriteUInt32LittleEndian(signature.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(signature.AsSpan(12), 1);
        var parameter = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(parameter, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(parameter.AsSpan(8), nestedLoop ? 1u : 2u);
        var body = nestedLoop ? TrRmBodyExampleTests.IndexedNestedLoop()
            : TrRmBodyExampleTests.IndexedScalarLoop(siblingLoop: siblingLoop);
        byte[] Artifact() => TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, module),
            new TrRmSection(0x0002, 0, 2, strings),
            new TrRmSection(0x0011, 0, 2, typeSpec),
            new TrRmSection(0x0016, 0, 1, method),
            new TrRmSection(0x0017, 0, 1, signature),
            new TrRmSection(0x0018, 0, 1, parameter),
            new TrRmSection(0x0020, 0, 1, body),
        ]);

        var path = Path.Combine(Path.GetTempPath(), $"terrum-trrm-loop-{Guid.NewGuid():N}.trrm");
        try
        {
            await File.WriteAllBytesAsync(path, Artifact());
            foreach (var command in new[] { "validate-trrm-structure",
                "validate-trrm-compact-indexed", "validate-trrm-compact" })
            {
                var (valid, output, error) = await RunAsync([], command, path, "--json");
                Assert.That(valid == 0, Is.True, output + error);
            }

            var region = body.AsSpan().IndexOf(nestedLoop
                ? new byte[] { 1, 1, 2, 4, 0 } : [1, 0, 2, 4, 0]);
            Assert.That(region >= 0, Is.True);
            body[region + 2] = 3;
            await File.WriteAllBytesAsync(path, Artifact());
            var (structural, _, _) = await RunAsync([],
                "validate-trrm-structure", path, "--json");
            Assert.That(structural, Is.EqualTo(0));
            var (invalid, _, _) = await RunAsync([],
                "validate-trrm-compact-indexed", path, "--json");
            Assert.That(invalid, Is.EqualTo(1));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
