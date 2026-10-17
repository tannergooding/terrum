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
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendCleanupCountedForExecutesFinitePathsAsync(string policy)
    {
        var cases = new (string Name, string Loop, int Expected)[] {
            ("ascending", "for (mut int32 i = 0; i < 4; i = i + 1) { value = value | i; }", 3),
            ("descending", "for (mut int32 i = 3; i > 0; i = i - 1) { value = value | i; }", 3),
            ("assigned-initializer", "mut int32 i = 99; for (i = 0; i < 4; i = i + 1) { value = i; }", 3),
            ("negative", "for (mut int32 i = -3; i < 0; i = i + 1) { value = i; }", -1),
            ("equal-zero", "for (mut int32 i = 4; i < 4; i = i + 1) { value = 99; continue 1; }", 0),
            ("descending-equal-zero", "for (mut int32 i = 4; i > 4; i = i - 1) { value = 99; }", 0),
            ("ascending-zero", "for (mut int32 i = 2147483647; i < -2147483648; i = i + 1) { value = 99; }", 0),
            ("descending-zero", "for (mut int32 i = -2147483648; i > 2147483647; i = i - 1) { value = 99; }", 0),
            ("ascending-extreme", "for (mut int32 i = 2147483646; i < 2147483647; i = i + 1) { value = i; }", 2147483646),
            ("descending-extreme", "for (mut int32 i = -2147483647; i > -2147483648; i = i - 1) { value = i; }", -2147483647),
            ("local-continue", "for (mut int32 i = 0; i < 4; i = i + 1) { value = value | i; continue; }", 3),
            ("local-label-continue", "inner: for (mut int32 i = 3; i > 0; i = i - 1) { value = value | i; continue inner; }", 3),
            ("local-break", "for (mut int32 i = 0; i < 4; i = i + 1) { value = 8; break; }", 8),
            ("arm-transfer", "for (mut int32 i = 0; i < 4; i = i + 1) { value = 8; continue _; }", 8),
            ("body-copy", "for (mut int32 i = 0; i < 4; i = i + 1) { int32 copy = i; value = value | copy; }", 3),
            ("by-value-call", "for (mut int32 i = 0; i < 4; i = i + 1) { Observe(i); value = value | i; }", 3),
        };
        var root = RepositoryRoot();
        // A void cleanup helper keeps each function inside the frontend's
        // existing 12-statement budget, including the local-continue shape.
        foreach (var testCase in cases)
        {
            var path = CreateTestArtifactPath($"cleanup-counted-for-{Guid.NewGuid():N}.exe");
            try
            {
                var source = "public int32 Main() { mut int32 value = 0; Clean(out value); return value; } "
                    + $"void Clean(out int32 value) {{ value = 0; try {{}} finally {{ switch (1) {{ "
                    + $"1 => {{ {testCase.Loop} continue _; }}, _ => {{}} }} }} }}";
                if (testCase.Name == "by-value-call")
                {
                    source += " [NoThrow] void Observe(int32 i) {}";
                }
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", SelfHostPaths.SourceDirectory(root), "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{testCase.Name} {policy}: {exit}: {output}{error}");
                Assert.That(await ExecuteNativeImageAsync(path), Is.EqualTo(testCase.Expected));
            }
            finally
            {
                if (File.Exists(path))
                {
                    await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
                }
            }
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendCleanupCountedForRejectsUnsupportedProofsAsync(string policy)
    {
        var loops = new[] {
            "for (mut int32 i = value; i < 4; i = i + 1) {}",
            "mut int32 i = 0; for (; i < 4; i = i + 1) {}",
            "for (value = 0; value < 4; value = value + 1) {}",
            "for (mut int32 i = 0; i < value; i = i + 1) {}",
            "for (mut int32 i = 0; i < 4; i = i + 2) {}",
            "for (mut int32 i = 4; i > 0; i = i - 2) {}",
            "for (mut int32 i = 0; i <= 4; i = i + 1) {}",
            "for (mut int32 i = 4; i >= 0; i = i - 1) {}",
            "for (mut int32 i = 0; i < 4; i = i - 1) {}",
            "for (mut int32 i = 4; i > 0; i = i + 1) {}",
            "for (mut int32 i = 4; i < 0; i = i - 1) {}",
            "for (mut int32 i = 0; i < 4; i = i + value) {}",
            "for (mut int32 i = 0; i < 4; i = 1 + i) {}",
            "for (mut int32 i = 0; i < 4; i += 1) {}",
            "for (mut int32 i = 0; i < 4; i++) {}",
            "for (mut int32 i = 0; 4 > i; i = i + 1) {}",
            "for (mut int32 i = 0; i < 4; value = value | 1) {}",
            "for (mut int32 i = 0; i < 4; i = i + 1) { i = 0; }",
            "for (mut int32 i = 0; i < 4; i = i + 1) { if (value == 0) { i = 0; } }",
            "for (mut int32 i = 0; i < 4; i = i + 1) { i |= 1; }",
            "for (mut int32 i = 0; i < 4; i = i + 1) { Overwrite(out i); }",
            "for (mut int32 i = 0; i < 4; i = i + 1) { while (value != 0) { value = 0; } }",
            "for (mut int32 i = 0; i < 4; i = i + 1) { value = value + 1; }",
            "for (mut int64 i = 0; i < 4; i = i + 1) {}",
            "for (mut uint32 i = 0; i < 4; i = i + 1) {}",
            "for (mut int32 i = 0; i < 4; i = i + 1) { continue 1; }",
            "for (mut int32 i = 0; i < 4; i = i + 1) { return; }",
            "while (value != 0) { value = 0; }",
            "{ mut int32 i = 0; while (i < 4) { {} i = i + 1; } }",
        };
        var root = RepositoryRoot();
        var directory = SelfHostPaths.SourceDirectory(root);
        var path = CreateTestArtifactPath($"cleanup-counted-for-rejected-{Guid.NewGuid():N}.exe");
        try
        {
            var (initial, initialOutput, initialError) = await RunAsync(
                Encoding.UTF8.GetBytes("public int32 Main() => 42;"), "frontend-build",
                directory, "-", "-o", path, "--runtime", policy, "--json");
            Assert.That(initial == 0, Is.True, $"{initial}: {initialOutput}{initialError}");
            var image = await File.ReadAllBytesAsync(path);
            foreach (var loop in loops)
            {
                var source = "public int32 Main() { mut int32 value = 1; Clean(out value); return 42; } "
                    + $"void Clean(out int32 value) {{ value = 1; try {{}} finally {{ switch (1) {{ "
                    + $"1 => {{ {loop} continue _; }}, _ => {{}} }} }} }}"
                    + " [NoThrow] int32 Overwrite(out int32 value) { { value = 0; } return 0; }";
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", directory, "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(exit == 1, Is.True, $"{policy}: {loop}: {exit}: {output}{error}");
                Assert.That(error, Is.Empty);
                using var document = JsonDocument.Parse(output);
                Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
                Assert.That(document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
                Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(image));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendCleanupCountedForRetainsOrdinaryLoopBoundaryAsync(string policy)
    {
        var cases = new (string Source, bool Accepted)[] {
            ("public int32 Main() { mut int32 value = 0; for (mut int32 i = 0; i < 4; i = i + 1) { value = value | i; } return value; }", true),
            ("public int32 Main() { try {} finally { for (mut int32 i = 0; i < 4; i = i + 1) {} } return 3; }", false),
            ("public int32 Main() { try {} finally { switch (1) { 1 => { for (mut int32 i = 0; i < 4; i = i + 1) {} }, _ => {} } } return 3; }", false),
            ("[NoThrow] public int32 Main() { for (mut int32 i = 0; i < 4; i = i + 1) {} return 3; }", false),
        };
        var root = RepositoryRoot();
        var path = CreateTestArtifactPath($"cleanup-counted-for-ordinary-{Guid.NewGuid():N}.exe");
        try
        {
            var image = Array.Empty<byte>();
            foreach (var testCase in cases)
            {
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(testCase.Source),
                    "frontend-build", SelfHostPaths.SourceDirectory(root), "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(exit == (testCase.Accepted ? 0 : 1), Is.True, $"{policy}: {testCase.Source}: {exit}: {output}{error}");
                Assert.That(error, Is.Empty);
                if (testCase.Accepted)
                {
                    Assert.That(await ExecuteNativeImageAsync(path), Is.EqualTo(3));
                    image = await File.ReadAllBytesAsync(path);
                }
                else
                {
                    using var document = JsonDocument.Parse(output);
                    Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
                    Assert.That(document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
                    Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(image));
                }
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
}
