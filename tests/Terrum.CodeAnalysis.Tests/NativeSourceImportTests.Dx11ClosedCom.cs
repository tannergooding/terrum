// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    [Test]
    public async Task Dx11ClosedComCatalogMatchesWindowsSdkAndInheritanceAsync()
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var root = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(root).ConfigureAwait(false);
        var directory = TestArtifactPaths.CreateDirectory("terrum-dx11-closed-com-sdk-");
        var native = Path.Combine(root, "tests", "native");
        var comspec = Environment.GetEnvironmentVariable("ComSpec")
            ?? throw new InvalidOperationException("ComSpec is unset.");
        try
        {
            var executable = Path.Combine(directory, "dx11-closed-com-sdk.exe");
            var sdkProbe = Path.Combine(native, "dx11-closed-com-sdk-probe.c");
            var inheritanceProbe = Path.Combine(native, "dx11-closed-com-inheritance-probe.cpp");
            var inheritanceObject = Path.Combine(directory, "dx11-closed-com-inheritance.obj");
            _ = await RunAsync(new ProcessStartInfo(comspec) {
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /wd4201 /Od /TC /Fo\"{directory}\\sdk.obj\" /Fe\"{executable}\" \"{sdkProbe}\" /link /incremental:no dxguid.lib uuid.lib && cl /nologo /W4 /WX /wd4201 /std:c++20 /Od /TP /c /Fo\"{inheritanceObject}\" \"{inheritanceProbe}\"\"",
                WorkingDirectory = directory,
            }, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            Assert.That(File.Exists(inheritanceObject), Is.True);
            _ = await RunAsync(new ProcessStartInfo(executable) {
                WorkingDirectory = directory,
            }, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
