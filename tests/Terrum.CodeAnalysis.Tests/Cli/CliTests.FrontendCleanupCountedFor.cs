// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

internal sealed partial class CliTests
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
        foreach (var (Name, Loop, Expected) in cases)
        {
            var path = CreateTestArtifactPath($"cleanup-counted-for-{Guid.NewGuid():N}.exe");
            try
            {
                var source = "public int32 Main() { mut int32 value = 0; Clean(out value); return value; } "
                    + $"void Clean(out int32 value) {{ value = 0; try {{}} finally {{ switch (int32(1)) {{ "
                    + $"1 => {{ {Loop} continue _; }}, _ => {{}} }} }} }}";
                if (Name == "by-value-call")
                {
                    source += "  void Observe(int32 i) {}";
                }
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", TerrumProjectPaths.SourceDirectory(root), "-", "-o", path, "--runtime", policy, "--json").ConfigureAwait(false);
                Assert.That(exit == 0, Is.True, $"{Name} {policy}: {exit}: {output}{error}");
                Assert.That(await ExecuteNativeImageAsync(path).ConfigureAwait(false), Is.EqualTo(Expected));
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
    public async Task ScalarFrontendCleanupCountedForSupportsGeneralLoopShapesAsync(string policy)
    {
        var loops = new (string Loop, bool Completes)[] {
            ("for (mut int32 i = value; i < 4; i = i + 1) {}", true),
            ("mut int32 i = 0; for (; i < 4; i = i + 1) {}", true),
            ("for (value = 0; value < 4; value = value + 1) {}", true),
            ("for (mut int32 i = 0; i < value; i = i + 1) {}", true),
            ("for (mut int32 i = 0; i < 4; i = i + 2) {}", true),
            ("for (mut int32 i = 4; i > 0; i = i - 2) {}", true),
            ("for (mut int32 i = 0; i <= 4; i = i + 1) {}", true),
            ("for (mut int32 i = 4; i >= 0; i = i - 1) {}", true),
            ("for (mut int32 i = 0; i < 4; i = i - 1) {}", false),
            ("for (mut int32 i = 4; i > 0; i = i + 1) {}", false),
            ("for (mut int32 i = 4; i < 0; i = i - 1) {}", true),
            ("for (mut int32 i = 0; i < 4; i = i + value) {}", true),
            ("for (mut int32 i = 0; i < 4; i = 1 + i) {}", true),
            ("for (mut int32 i = 0; i < 4; i += 1) {}", true),
            ("for (mut int32 i = 0; i < 4; i++) {}", true),
            ("for (mut int32 i = 0; 4 > i; i = i + 1) {}", true),
            ("for (mut int32 i = 0; i < 4; value = value | 1) {}", false),
            ("for (mut int32 i = 0; i < 4; i = i + 1) { i = 0; }", false),
            ("for (mut int32 i = 0; i < 4; i = i + 1) { if (value == 0) { i = 0; } }", true),
            ("for (mut int32 i = 0; i < 4; i = i + 1) { i |= 1; }", true),
            ("for (mut int32 i = 0; i < 4; i = i + 1) { Overwrite(out i); }", false),
            ("for (mut int32 i = 0; i < 4; i = i + 1) { while (value != 0) { value = 0; } }", true),
            ("for (mut int32 i = 0; i < 4; i = i + 1) { value = value + 1; }", true),
            ("for (mut int64 i = 0; i < 4; i = i + 1) {}", true),
            ("for (mut uint32 i = 0; i < 4; i = i + 1) {}", true),
            ("for (mut int32 i = 0; i < 4; i = i + 1) { continue 1; }", false),
            ("while (value != 0) { value = 0; }", true),
            ("{ mut int32 i = 0; while (i < 4) { {} i = i + 1; } }", true),
        };
        var root = RepositoryRoot();
        var directory = TerrumProjectPaths.SourceDirectory(root);
        foreach (var (loop, completes) in loops)
        {
            var source = "public int32 Main() { mut int32 value = 1; Clean(out value); return 42; } "
                + $"void Clean(out int32 value) {{ value = 1; try {{}} finally {{ switch (int32(1)) {{ "
                + $"1 => {{ {loop} continue _; }}, _ => {{}} }} }} }}"
                + "  int32 Overwrite(out int32 value) { { value = 0; } return 0; }";
            var path = CreateTestArtifactPath($"cleanup-counted-for-general-{Guid.NewGuid():N}.exe");
            try
            {
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", directory, "-", "-o", path, "--runtime", policy, "--json").ConfigureAwait(false);
                Assert.That(exit, Is.Zero, $"{policy}: {loop}: {exit}: {output}{error}");
                Assert.That(error, Is.Empty);
                using var document = JsonDocument.Parse(output);
                Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.True);
                if (completes)
                {
                    Assert.That(await ExecuteNativeImageAsync(path).ConfigureAwait(false), Is.EqualTo(42));
                }
            }
            finally
            {
                await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
            }
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendCleanupCountedForSupportsOrdinaryLoopBoundaryAsync(string policy)
    {
        var cases = new (string Source, int Expected)[] {
            ("public int32 Main() { mut int32 value = 0; for (mut int32 i = 0; i < 4; i = i + 1) { value = value | i; } return value; }", 3),
            ("public int32 Main() { try {} finally { for (mut int32 i = 0; i < 4; i = i + 1) {} } return 3; }", 3),
            ("public int32 Main() { try {} finally { switch (int32(1)) { 1 => { for (mut int32 i = 0; i < 4; i = i + 1) {} }, _ => {} } } return 3; }", 3),
            (" public int32 Main() { for (mut int32 i = 0; i < 4; i = i + 1) {} return 3; }", 3),
        };
        var root = RepositoryRoot();
        var path = CreateTestArtifactPath($"cleanup-counted-for-ordinary-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (var (Source, Expected) in cases)
            {
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
                    "frontend-build", TerrumProjectPaths.SourceDirectory(root), "-", "-o", path, "--runtime", policy, "--json").ConfigureAwait(false);
                Assert.That(exit, Is.Zero, $"{policy}: {Source}: {exit}: {output}{error}");
                Assert.That(error, Is.Empty);
                using var document = JsonDocument.Parse(output);
                Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.True);
                Assert.That(await ExecuteNativeImageAsync(path).ConfigureAwait(false), Is.EqualTo(Expected));
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
}
