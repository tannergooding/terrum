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
    public async Task ScalarFrontendCleanupSwitchTransfersMatchHostedImagesAsync(string policy)
    {
        var cases = new (string Name, string Body, string Equivalent, int Expected)[] {
            ("forward", "switch (1) { 1 => { value = value | 1; continue _; }, _ => { value = value | 2; } }",
                "value = value | 1; value = value | 2;", 3),
            ("forward-literal", "switch (1) { 1 => { continue 2; }, 2 => { value = 42; }, _ => {} }",
                "value = 42;", 42),
            ("selector-once", "switch (Pick(out value)) { 1 => { value = 40; continue _; }, _ => { value = value | 2; } }",
                "Pick(out value); value = 40; value = value | 2;", 42),
            ("backward-chain", "switch (99) { 1 => { value = 42; }, 2 => { continue 1; }, _ => { continue 2; } }",
                "value = 42;", 42),
            ("conditional-exit", "switch (1) { 1 => { if (value == 0) { continue _; } }, _ => { value = 42; } }",
                "if (value == 0) { value = 42; }", 42),
            ("dead-cycle", "switch (1) { 1 => { if (false) { continue 1; } continue _; }, _ => { value = 42; } }",
                "value = 42;", 42),
            ("dead-transfer", "switch (1) { 1 => { continue _; continue 1; }, _ => { value = 42; } }",
                "value = 42;", 42),
            ("nested-loop-exit", "switch (1) { 1 => { while (true) { continue _; } }, _ => { value = 42; } }",
                "value = 42;", 42),
            ("local-loop-label", "switch (1) { 1 => { inner: while (true) { break inner; } continue _; }, _ => { value = 42; } }",
                "inner: while (true) { break inner; } value = 42;", 42),
            ("nonnearest-loop-label", "switch (1) { 1 => { outer: while (true) { while (true) { break outer; } } continue _; }, _ => { value = 42; } }",
                "value = 42;", 42),
            ("nested-finally", "switch (1) { 1 => { try { continue _; } finally { value = 1; } }, _ => { value = value | 2; } }",
                "try {} finally { value = 1; } value = value | 2;", 3),
            ("zero-iteration-loop", "switch (1) { 1 => { while (false) { continue 1; } continue _; }, _ => { value = 42; } }",
                "value = 42;", 42),
            ("post-test-once", "switch (1) { 1 => { do { value = 1; } while (false); continue _; }, _ => { value = value | 2; } }",
                "value = 1; value = value | 2;", 3),
            ("local-continue-label", "switch (1) { 1 => { inner: do { value = 1; continue inner; } while (false); continue _; }, _ => { value = value | 2; } }",
                "value = 1; value = value | 2;", 3),
        };
        var root = RepositoryRoot();
        var directory = Path.Combine(root, "self-host");
        foreach (var testCase in cases)
        {
            var source = $"public int32 Main() {{ mut int32 value = 0; try {{}} finally {{ {testCase.Body} }} return value; }}";
            var equivalent = $"public int32 Main() {{ mut int32 value = 0; try {{}} finally {{ {testCase.Equivalent} }} return value; }}";
            if (testCase.Name == "selector-once")
            {
                const string pick = " [NoThrow] int32 Pick(out int32 result) { { result = 1; } return 1; }";
                source += pick;
                equivalent += pick;
            }
            var native = CreateTestArtifactPath($"cleanup-switch-{Guid.NewGuid():N}.native.exe");
            var hosted = CreateTestArtifactPath($"cleanup-switch-{Guid.NewGuid():N}.hosted.exe");
            try
            {
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", directory, "-", "-o", native, "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{testCase.Name} {policy}: {exit}: {output}{error}");
                var (hostExit, hostOutput, hostError) = await RunAsync(Encoding.UTF8.GetBytes(equivalent),
                    "build", "-", "-o", hosted, "--runtime", policy, "--json");
                Assert.That(hostExit == 0, Is.True, $"{testCase.Name} {policy}: {hostExit}: {hostOutput}{hostError}");
                Assert.That(await ExecuteNativeImageAsync(native), Is.EqualTo(testCase.Expected));
                Assert.That(await ExecuteNativeImageAsync(hosted), Is.EqualTo(testCase.Expected));
            }
            finally
            {
                await TestFileCleanup.DeleteWithRetryAsync(native).ConfigureAwait(true);
                await TestFileCleanup.DeleteWithRetryAsync(hosted).ConfigureAwait(true);
            }
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendCleanupSwitchTransfersComposeNestedDispatchersAsync(string policy)
    {
        const string source = """
            public int32 Main() {
                try {} finally {
                    switch (1) {
                        1 => {
                            switch (2) { 2 => { continue _; }, _ => {} }
                            continue _;
                        },
                        _ => {},
                    }
                }
                return 42;
            }
            """;
        var path = CreateTestArtifactPath($"cleanup-nested-switch-{Guid.NewGuid():N}.exe");
        try
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-build", Path.Combine(RepositoryRoot(), "self-host"), "-",
                "-o", path, "--runtime", policy, "--json");
            Assert.That(exit == 0, Is.True, $"{policy}: {exit}: {output}{error}");
            Assert.That(await ExecuteNativeImageAsync(path), Is.EqualTo(42));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendCleanupSwitchTransfersRejectCyclesAndEscapesAsync(string policy)
    {
        var sources = new[] {
            "public int32 Main() { try {} finally { switch (1) { 1 => { continue 1; }, _ => {} } } return 42; }",
            "public int32 Main() { try {} finally { switch (1) { 1 => { continue _; }, _ => { continue 1; } } } return 42; }",
            "public int32 Main() { mut int32 value = 0; try {} finally { switch (1) { 1 => { if (value == 0) { continue 1; } }, _ => {} } } return 42; }",
            "public int32 Main() { mut int32 value = 1; try {} finally { switch (1) { 1 => { value ^= 1; if (value == 0) { continue 1; } }, _ => {} } } return 42; }",
            "public int32 Main() { try {} finally { switch (1) { 1 => { while (true) {} continue _; }, _ => {} } } return 42; }",
            "public int32 Main() { mut int32 value = 1; try {} finally { switch (1) { 1 => { while (value != 0) { value = 0; } continue _; }, _ => {} } } return 42; }",
            "public int32 Main() { try {} finally { switch (1) { 1 => {}, _ => { continue _; } } } return 42; }",
            "public int32 Main() { try { return 42; } finally { switch (1) { 1 => { continue 1; }, _ => {} } } }",
            "public int32 Main() { try { require false; } finally { switch (1) { 1 => { continue 1; }, _ => {} } } return 42; }",
            "public int32 Main() { switch (1) { 1 => { try {} finally { continue _; } }, _ => {} } return 42; }",
            "public int32 Main() { outer: while (true) { try {} finally { switch (1) { 1 => { continue outer; }, _ => {} } } break; } return 42; }",
            "public int32 Main() { outer: while (true) { try {} finally { switch (1) { 1 => { break outer; }, _ => {} } } break; } return 42; }",
            "public int32 Main() { try {} finally { switch (1) { 1 => { return 42; }, _ => {} } } return 42; }",
            "public int32 Main() { try {} finally { while (true) { if (true) { break; } } } return 42; }",
            "public int32 Main() { try { return 42; } finally { while (true) { if (true) { break; } } } }",
            "public int32 Main() { mut int32 value = 0; try {} finally { switch (1) { 1 => { continue _; }, _ => { value++; } } } return value; }",
            "public int32 Main() { try {} finally { switch (1) { 1 => { Stop(); continue _; }, _ => {} } } return 42; } [NoThrow][DoesNotReturn] void Stop() { while (true) {} }",
            "public int32 Main() { try {} finally { var value = 1 switch { 1 => { continue _; }, _ => 0 }; } return 42; }",
        };
        var path = CreateTestArtifactPath($"cleanup-switch-rejected-{Guid.NewGuid():N}.exe");
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        try
        {
            var (initial, initialOutput, initialError) = await RunAsync(
                Encoding.UTF8.GetBytes("public int32 Main() => 42;"), "frontend-build",
                directory, "-", "-o", path, "--runtime", policy, "--json");
            Assert.That(initial == 0, Is.True, $"{initial}: {initialOutput}{initialError}");
            var image = await File.ReadAllBytesAsync(path);
            for (var index = 0; index < sources.Length; index++)
            {
                var source = sources[index];
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", directory, "-", "-o", path, "--runtime", policy, "--json");
                Assert.That(exit == 1, Is.True, $"{policy}: {source}: {exit}: {output}{error}");
                Assert.That(error, Is.Empty);
                using var document = JsonDocument.Parse(output);
                Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
                if (index < 9)
                {
                    Assert.That(document.RootElement.GetProperty("diagnostics")[0]
                        .GetProperty("code").GetString(), Is.EqualTo("TRM5001"));
                }
                Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(image));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
