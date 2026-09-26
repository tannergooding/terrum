// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeGraphJitTests
{
    [Fact]
    public async Task RejectsUnboundedOrNullInputsBeforeSnapshotAllocationAsync()
    {
        const int MaximumArtifactBytes = 16 * 1024 * 1024;
        const NativeRuntimePolicy Policy = NativeRuntimePolicy.FailFast;
        var timeout = TimeSpan.FromSeconds(10);
        var root = new byte[51];
        var provider = new byte[1];
        _ = await Assert.ThrowsAsync<ModuleContractException>(() =>
            NativeModuleCompiler.RunGraphJitAsync(root, [], "missing.dll", timeout, Policy));
        _ = await Assert.ThrowsAsync<ArgumentException>(() =>
            NativeModuleCompiler.RunGraphJitAsync(root, new byte[1][], "missing.dll", timeout, Policy));
        _ = await Assert.ThrowsAsync<ModuleFormatException>(() =>
            NativeModuleCompiler.RunGraphJitAsync(new byte[MaximumArtifactBytes + 1],
                [provider], "missing.dll", timeout, Policy));
        _ = await Assert.ThrowsAsync<ModuleFormatException>(() =>
            NativeModuleCompiler.RunGraphJitAsync(root,
                [new byte[MaximumArtifactBytes + 1]], "missing.dll", timeout, Policy));
    }
}
