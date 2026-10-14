// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TrRmScopedLocalAuditTests
{
    // Each arm defines its own value; only the selected value reaches the join parameter.
    // Copy-local source scopes require no lexical region or mutable place in this Body.
    private static byte[] ArmLocalBody(bool chooseFirst) =>
    [
        8, 4, 4, 0, 2, 1, 0, 0, 0, 2, 0, 0, 0,
        2, 1, 1, 1, 1, 1, 1, 1,
        0, 1, 2, 3, 4,
        5, 7, 7, 255,
        0, 15, 30, 45, 54,
        9, 11, 11, 7,
        0x10, 1, 1, 1, 1, 1, 1, 2, chooseFirst ? (byte)1 : (byte)0,
        0x81, 1, 2, 0, 3, 0,
        0x18, 1, 2, 1, 2, 1, 1, 1, 2, 1, 20, 0x80, 4, 1, 2,
        0x18, 1, 3, 1, 3, 1, 1, 1, 3, 1, 42, 0x80, 4, 1, 3,
        0x12, 1, 4, 1, 4, 0, 0, 0x83, 4,
    ];
    [TestCase(true,null,20)]
    [TestCase(false,null,42)]
    [TestCase(true,1u,20)]
    [TestCase(false,1u,42)]
    public void HandwrittenArmDefinitionsExecuteWithoutSourceScopes(bool chooseFirst, uint? effect, int expected)
    {
        var artifact = TrRmExecutionAuditTests.Artifact(ArmLocalBody(chooseFirst), effect: effect);
        TrRmStructuralMetadataValidator.ValidateCompactIndexed(artifact);
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.That(loaded.Functions[0].Variables.Length, Is.EqualTo(4));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }
    [TestCase(15,(byte)(2))] // Arm result type.
    [TestCase(16,(byte)(2))] // Exact use inventory.
    [TestCase(23,(byte)(1))] // Definition ownership.
    [TestCase(27,(byte)(8))] // Definition offset.
    [TestCase(29,(byte)(0))] // Block parameter sentinel.
    [TestCase(32,(byte)(31))] // Block extent.
    [TestCase(36,(byte)(10))] // Terminator boundary.
    [TestCase(49,(byte)(2))] // Branch condition type/dominance.
    [TestCase(56,(byte)(3))] // Arm live-out inventory.
    [TestCase(59,(byte)(0))] // Last-use ordinal.
    [TestCase(62,(byte)(3))] // Result identity.
    [TestCase(67,(byte)(0))] // Missing join argument.
    [TestCase(68,(byte)(3))] // Sibling definition on the true edge.
    [TestCase(83,(byte)(2))] // Sibling definition on the false edge.
    [TestCase(86,(byte)(2))] // Reused parameter identity.
    public void ForgedScopedValueFlowDoesNotBypassAuditing(int offset, byte value)
    {
        var body = ArmLocalBody(true);
        body[offset] = value;
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body)));
    }

    [Test]
    public void MatchingUseAndLiveInventoriesCannotMakeASiblingDefinitionDominate()
    {
        // The true arm incorrectly forwards the false arm's v3. Inventories include
        // that use and propagate it to entry, but no definition can dominate entry.
        byte[] body =
        [
            8, 4, 4, 0, 2, 1, 0, 0, 0, 2, 0, 0, 0,
            2, 1, 1, 0, 1, 2, 1, 1,
            0, 1, 2, 3, 4,
            9, 9, 7, 255,
            0, 19, 36, 51, 60,
            13, 13, 11, 7,
            0x1C, 1, 3, 1, 3, 1, 1, 1, 1, 1, 1, 2, 1,
            0x81, 1, 2, 0, 3, 0,
            0x1C, 1, 3, 1, 3, 1, 3, 1, 1, 1, 2, 1, 20, 0x80, 4, 1, 3,
            0x18, 1, 3, 1, 3, 1, 1, 1, 3, 1, 42, 0x80, 4, 1, 3,
            0x12, 1, 4, 1, 4, 0, 0, 0x83, 4,
        ];
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body)));
    }

    [Test]
    public void SourceScopeAdoptionDoesNotEnableLexicalRegionExecution()
    {
        var body = TrRmIndexedExecutionAuditTests.LoopBody(3);
        body[13] = 2;
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(body)));
    }

    [Test]
    public void EveryTruncationOfTheIndependentScopedValueBodyRejects()
    {
        var body = ArmLocalBody(true);
        for (var length = 1; length < body.Length; length++)
        {
            var truncated = body[..length];
            Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.Load(TrRmExecutionAuditTests.Artifact(truncated)));
        }
    }
}
