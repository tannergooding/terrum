// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    [Test]
    public async Task ScalarFrontendSwitchProjectsAndMatchesHostedIfChainAsync()
    {
        const string source = """
            public int32 Main() {
                mut int32 calls = 1;
                switch (calls++) {
                    0 => { return 0; },
                    1 => { return calls; },
                    _ => { return 0; },
                }
            }
            """;
        const string hostedEquivalent = """
            public int32 Main() {
                mut int32 calls = 1;
                int32 choice = calls++;
                if (choice == 0) {
                    return 0;
                } else if (choice == 1) {
                    return calls;
                } else {
                    return 0;
                }
            }
            """;
        var root = RepositoryRoot();
        var directory = TerrumProjectPaths.SourceDirectory(root);
        var input = Encoding.UTF8.GetBytes(source);
        var equivalentInput = Encoding.UTF8.GetBytes(hostedEquivalent);
        var nativePath = CreateTestArtifactPath($"switch-{Guid.NewGuid():N}.native.exe");
        var hostedPath = CreateTestArtifactPath($"switch-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var (componentExit, componentOutput, componentError) = await RunAsync(input,
                    "frontend-component", directory, "-", "--runtime", policy, "--json");
                Assert.That(componentExit == 0, Is.True, $"{componentExit}: {componentOutput}{componentError}");
                using (var component = JsonDocument.Parse(componentOutput))
                {
                    Assert.That(component.RootElement.GetProperty("result")
                        .GetProperty("projectionVersion").GetInt32(), Is.EqualTo(88));
                }

                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build", directory, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(equivalentInput,
                    "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"{nativeExit}: {nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"{hostedExit}: {hostedOutput}{hostedError}");
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                Assert.That(await ExecuteNativeImageAsync(nativePath), Is.EqualTo(2));
                Assert.That(await ExecuteNativeImageAsync(hostedPath), Is.EqualTo(2));

                var (runExit, runOutput, runError) = await RunAsync(input,
                    "frontend-run", directory, "-", "--runtime", policy, "--json");
                Assert.That(runExit, Is.EqualTo(2));
                Assert.That(runOutput, Is.Empty);
                Assert.That(runError, Is.Empty);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(hostedPath).ConfigureAwait(true);
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendSwitchCompletionAndLoopTransfersMatchHostedAsync(string policy)
    {
        var cases = new (string Name, string Source, string HostedEquivalent, int ExpectedResult)[] {
            ("arm-completion", """
                public int32 Main() {
                    mut int32 selector = 1;
                    mut int32 effects = 0;
                    switch (selector++) {
                        0 => { effects = effects + 1; },
                        1 => { effects = effects + 10; },
                        _ => { effects = effects + 100; },
                    }
                    return effects * 10 + selector;
                }
                """, """
                public int32 Main() {
                    mut int32 selector = 1;
                    mut int32 effects = 0;
                    int32 choice = selector++;
                    if (choice == 0) {
                        effects = effects + 1;
                    } else if (choice == 1) {
                        effects = effects + 10;
                    } else {
                        effects = effects + 100;
                    }
                    return effects * 10 + selector;
                }
                """, 102),
            ("continue-enclosing-loop", """
                public int32 Main() {
                    mut int32 total = 0;
                    for (mut int32 index = 0; index < 4; index = index + 1) {
                        switch (index) {
                            0 => { continue; },
                            1 => { total = total + 10; },
                            _ => { total = total + 1; },
                        }
                    }
                    return total;
                }
                """, """
                public int32 Main() {
                    mut int32 total = 0;
                    for (mut int32 index = 0; index < 4; index = index + 1) {
                        int32 choice = index;
                        if (choice == 0) {
                            continue;
                        } else if (choice == 1) {
                            total = total + 10;
                        } else {
                            total = total + 1;
                        }
                    }
                    return total;
                }
                """, 12),
        };
        var root = RepositoryRoot();
        var directory = TerrumProjectPaths.SourceDirectory(root);
        foreach (var testCase in cases)
        {
            var nativePath = Path.Combine(TestArtifactPaths.DirectoryPath,
                $"switch-{testCase.Name}-{policy}-{Guid.NewGuid():N}.native.exe");
            var hostedPath = Path.Combine(TestArtifactPaths.DirectoryPath,
                $"switch-{testCase.Name}-{policy}-{Guid.NewGuid():N}.hosted.exe");
            try
            {
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(
                    Encoding.UTF8.GetBytes(testCase.Source), "frontend-build", directory, "-",
                    "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(
                    Encoding.UTF8.GetBytes(testCase.HostedEquivalent), "build", "-",
                    "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"{testCase.Name} {policy}: {nativeExit}: {nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"{testCase.Name} {policy}: {hostedExit}: {hostedOutput}{hostedError}");
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                Assert.That(await ExecuteNativeImageAsync(nativePath), Is.EqualTo(testCase.ExpectedResult));
                Assert.That(await ExecuteNativeImageAsync(hostedPath), Is.EqualTo(testCase.ExpectedResult));
            }
            finally
            {
                await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
                await TestFileCleanup.DeleteWithRetryAsync(hostedPath).ConfigureAwait(true);
            }
        }
    }

    [Test]
    public async Task ScalarFrontendSwitchOutputLocalsAndConstantLabelsMatchHostedAsync()
    {
        var cases = new (string Name, string Source, string HostedEquivalent)[] {
            ("out-existing", """
                int32 Pick(out int32 value) { value = 41; return 1; }
                public int32 Main() {
                    mut int32 value = 0;
                    switch (Pick(out value)) {
                        1 => { return value + 1; },
                        _ => { return 0; },
                    }
                }
                """, """
                int32 Pick(out int32 value) { value = 41; return 1; }
                public int32 Main() {
                    mut int32 value = 0;
                    int32 selector = Pick(out value);
                    if (selector == 1) { return value + 1; } else { return 0; }
                }
                """),
            ("out-var", """
                int32 Pick(out int32 value) { value = 41; return 1; }
                public int32 Main() {
                    switch (Pick(out var value)) {
                        1 => { return value + 1; },
                        _ => { return 0; },
                    }
                }
                """, """
                int32 Pick(out int32 value) { value = 41; return 1; }
                public int32 Main() {
                    int32 selector = Pick(out var value);
                    if (selector == 1) { return value + 1; } else { return 0; }
                }
                """),
            ("init-var", """
                int32 Pick(init ref<int32> value) { value = 41; return 1; }
                public int32 Main() {
                    switch (Pick(init var value)) {
                        1 => { return value + 1; },
                        _ => { return 0; },
                    }
                }
                """, """
                int32 Pick(init ref<int32> value) { value = 41; return 1; }
                public int32 Main() {
                    int32 selector = Pick(init var value);
                    if (selector == 1) { return value + 1; } else { return 0; }
                }
                """),
            ("const-label", """
                public int32 Main() {
                    const int32 N = 1;
                    switch (1) {
                        N => { return 42; },
                        _ => { return 0; },
                    }
                }
                """, """
                public int32 Main() {
                    const int32 N = 1;
                    if (1 == N) { return 42; } else { return 0; }
                }
                """),
        };
        var root = RepositoryRoot();
        var directory = TerrumProjectPaths.SourceDirectory(root);
        foreach (var testCase in cases)
        {
            var input = Encoding.UTF8.GetBytes(testCase.Source);
            var equivalentInput = Encoding.UTF8.GetBytes(testCase.HostedEquivalent);
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var nativePath = Path.Combine(TestArtifactPaths.DirectoryPath,
                    $"switch-{testCase.Name}-{policy}-{Guid.NewGuid():N}.native.exe");
                var hostedPath = Path.Combine(TestArtifactPaths.DirectoryPath,
                    $"switch-{testCase.Name}-{policy}-{Guid.NewGuid():N}.hosted.exe");
                try
                {
                    var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                        "frontend-build", directory, "-", "-o", nativePath, "--runtime", policy, "--json");
                    var (hostedExit, hostedOutput, hostedError) = await RunAsync(equivalentInput,
                        "build", "-", "-o", hostedPath, "--runtime", policy, "--json");
                    Assert.That(nativeExit == 0, Is.True, $"{testCase.Name} {policy}: {nativeExit}: {nativeOutput}{nativeError}");
                    Assert.That(hostedExit == 0, Is.True, $"{testCase.Name} {policy}: {hostedExit}: {hostedOutput}{hostedError}");
                    Assert.That(await ExecuteNativeImageAsync(nativePath), Is.EqualTo(42));
                    Assert.That(await ExecuteNativeImageAsync(hostedPath), Is.EqualTo(42));
                }
                finally
                {
                    await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
                    await TestFileCleanup.DeleteWithRetryAsync(hostedPath).ConfigureAwait(true);
                }
            }
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendSwitchMatchesHostedSelectorsAcrossAllScalarWidthsAsync(string policy)
    {
        foreach (var (type, value) in new[]
        {
            ("int32", "-2147483648"),
            ("int32", "2147483647"),
            ("uint32", "4294967295"),
            ("int64", "-9223372036854775808"),
            ("int64", "9223372036854775807"),
            ("uint64", "18446744073709551615"),
            ("bool", "false"),
            ("bool", "true"),
            ("char16", "char16(55296)"),
            ("char16", "char16(65535)"),
        })
        {
            string source = $$"""
                public int32 Main() {
                    {{type}} value = {{value}};
                    switch (value) {
                        {{value}} => { return 42; },
                        _ => { return 0; },
                    }
                }
                """;
            string hostedEquivalent = $$"""
                public int32 Main() {
                    {{type}} value = {{value}};
                    if (value == {{value}}) { return 42; } else { return 0; }
                }
                """;
            var root = RepositoryRoot();
            var directory = TerrumProjectPaths.SourceDirectory(root);
            var nativePath = CreateTestArtifactPath($"switch-widths-{Guid.NewGuid():N}.native.exe");
            var hostedPath = CreateTestArtifactPath($"switch-widths-{Guid.NewGuid():N}.hosted.exe");
            try
            {
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", directory, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(
                    Encoding.UTF8.GetBytes(hostedEquivalent), "build", "-", "-o", hostedPath,
                    "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"{type} {policy}: {nativeExit}: {nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"{type} {policy}: {hostedExit}: {hostedOutput}{hostedError}");
                Assert.That(await ExecuteNativeImageAsync(nativePath), Is.EqualTo(42));
                Assert.That(await ExecuteNativeImageAsync(hostedPath), Is.EqualTo(42));
            }
            finally
            {
                await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
                await TestFileCleanup.DeleteWithRetryAsync(hostedPath).ConfigureAwait(true);
            }
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendSwitchSelectsDefaultAcrossAllScalarTypesAsync(string policy)
    {
        foreach (var (type, value, label) in new[]
        {
            ("int32", "2147483647", "0"),
            ("uint32", "4294967295", "0"),
            ("int64", "9223372036854775807", "0"),
            ("uint64", "18446744073709551615", "0"),
            ("bool", "false", "true"),
            ("char16", "char16(55296)", "char16(0)"),
        })
        {
            string source = $$"""
                public int32 Main() {
                    {{type}} value = {{value}};
                    switch (value) {
                        {{label}} => { return 0; },
                        _ => { return 42; },
                    }
                }
                """;
            string hostedEquivalent = $$"""
                public int32 Main() {
                    {{type}} value = {{value}};
                    if (value == {{label}}) { return 0; } else { return 42; }
                }
                """;
            var root = RepositoryRoot();
            var directory = TerrumProjectPaths.SourceDirectory(root);
            var nativePath = CreateTestArtifactPath($"switch-default-{Guid.NewGuid():N}.native.exe");
            var hostedPath = CreateTestArtifactPath($"switch-default-{Guid.NewGuid():N}.hosted.exe");
            try
            {
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", directory, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(
                    Encoding.UTF8.GetBytes(hostedEquivalent), "build", "-", "-o", hostedPath,
                    "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"{type} {policy}: {nativeExit}: {nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"{type} {policy}: {hostedExit}: {hostedOutput}{hostedError}");
                Assert.That(await ExecuteNativeImageAsync(nativePath), Is.EqualTo(42));
                Assert.That(await ExecuteNativeImageAsync(hostedPath), Is.EqualTo(42));
            }
            finally
            {
                await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
                await TestFileCleanup.DeleteWithRetryAsync(hostedPath).ConfigureAwait(true);
            }
        }
    }
    [TestCase("int32 Main() { int32 x = 1; switch (x) { 1 when true => {}, _ => {} } return 0; }")]
    [TestCase("int32 Main() { int32 x = 1; switch (x) { 1 or 2 => {}, _ => {} } return 0; }")]
    [TestCase("int32 Main() { int32 x = 1; switch (x) { x => {}, _ => {} } return 0; }")]
    [TestCase("int32 Main() { int32 x = 1; switch (x) { 1 => {}, 1 + 0 => {}, _ => {} } return 0; }")]
    [TestCase("int32 Main() { int32 x = 2147483647; switch (x) { 2147483647 => {}, 2147483647 => {}, _ => {} } return 0; }")]
    [TestCase("int32 Main() { uint32 x = 4294967295; switch (x) { 4294967295 => {}, 4294967295 => {}, _ => {} } return 0; }")]
    [TestCase("int32 Main() { int64 x = 9223372036854775807; switch (x) { 9223372036854775807 => {}, 9223372036854775807 => {}, _ => {} } return 0; }")]
    [TestCase("int32 Main() { uint64 x = 18446744073709551615; switch (x) { 18446744073709551615 => {}, 18446744073709551615 => {}, _ => {} } return 0; }")]
    [TestCase("int32 Main() { bool x = true; switch (x) { true => {}, true => {}, _ => {} } return 0; }")]
    [TestCase("int32 Main() { char16 x = char16(55296); switch (x) { char16(55296) => {}, char16(55296) => {}, _ => {} } return 0; }")]
    [TestCase("int32 Main() { int32 x = 1; switch (x) { 1 => {}, _ => {}, 2 => {} } return 0; }")]
    [TestCase("int32 Main() { int32 x = 1; switch (x) { 1 => {} } return 0; }")]
    [TestCase("int32 Main() { int32 x = 1; switch (x) { case 1 => {}, _ => {} } return 0; }")]
    [TestCase("int32 Main() { int32 x = 1; switch (x) { 1: return 1; _ => {} } return 0; }")]
    [TestCase("int32 Main() { switch (1) { 1 => {} _ => {} } return 0; }")]
    [TestCase("int32 Main() { float32 x = 1.0; switch (x) { 1.0 => {}, _ => {} } return 0; }")]
    [TestCase("int32 Main() { float64 x = 1.0; switch (x) { 1.0 => {}, _ => {} } return 0; }")]
    [TestCase("int32 Main() { switch (1) { 1 => {}, _ => {} } return 0; } int32 Broken() { int32 value = ; return 0; }")]
    public async Task ScalarFrontendSwitchRejectsUnsupportedFormsAsync(string source)
    {
        var directory = TerrumProjectPaths.SourceDirectory(RepositoryRoot());
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendSwitchSeparatorDiagnosticRetainsTheOffendingSpanAsync(string policy)
    {
        const string source = "void Main() { switch (0) { 0 => {} _ => {} } }";
        var directory = TerrumProjectPaths.SourceDirectory(RepositoryRoot());
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
        var result = document.RootElement.GetProperty("result");
        Assert.That(result.GetProperty("errorCode").GetInt32(), Is.EqualTo(1));
        var errorSpan = result.GetProperty("errorSpan");
        Assert.That(errorSpan.GetProperty("start").GetInt32(), Is.EqualTo(source.IndexOf("_ =>", StringComparison.Ordinal)));
        Assert.That(errorSpan.GetProperty("length").GetInt32(), Is.EqualTo(1));
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendSwitchRetainsTheTwelveStatementBudgetAsync(string policy)
    {
        static string SourceWithCases(int count)
        {
            var cases = string.Join(", ", Enumerable.Range(0, count)
                .Select(static value => $"{value} => {{}}"));
            return $"void Main() {{ switch (0) {{ {cases}, _ => {{}} }} }}";
        }

        var directory = TerrumProjectPaths.SourceDirectory(RepositoryRoot());
        var (atLimitExit, atLimitOutput, atLimitError) = await RunAsync(
            Encoding.UTF8.GetBytes(SourceWithCases(10)),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.That(atLimitExit == 0, Is.True, $"{atLimitExit}: {atLimitOutput}{atLimitError}");
        using (var atLimit = JsonDocument.Parse(atLimitOutput))
        {
            Assert.That(atLimit.RootElement.GetProperty("success").GetBoolean(), Is.True);
        }

        var (overLimitExit, overLimitOutput, overLimitError) = await RunAsync(
            Encoding.UTF8.GetBytes(SourceWithCases(11)),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.That(overLimitExit, Is.EqualTo(1));
        Assert.That(overLimitError, Is.Empty);
        using var overLimit = JsonDocument.Parse(overLimitOutput);
        Assert.That(overLimit.RootElement.GetProperty("success").GetBoolean(), Is.False);
    }
}
