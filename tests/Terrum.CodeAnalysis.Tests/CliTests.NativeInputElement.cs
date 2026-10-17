// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase("build", "fail-fast")]
    [TestCase("build-native-import", "fail-fast")]
    [TestCase("build-native-import", "checked-exceptions")]
    public async Task CliRejectsTestOnlyInputElementDescriptorsAsync(string command, string runtimePolicy)
    {
        var path = Path.Combine(Path.GetTempPath(), $"terrum-test-input-element-{Guid.NewGuid():N}.exe");
        try
        {
            var (exit, output, error) = await RunAsync(
                Encoding.UTF8.GetBytes(NativeSourceImportTests.InputElementSource), command, "-",
                "-o", path, "--runtime", runtimePolicy, "--json");
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            Assert.That(File.Exists(path), Is.False);
            using var result = JsonDocument.Parse(output);
            Assert.That(result.RootElement.GetProperty("command").GetString(), Is.EqualTo(command));
            Assert.That(result.RootElement.GetProperty("success").GetBoolean(), Is.False);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
