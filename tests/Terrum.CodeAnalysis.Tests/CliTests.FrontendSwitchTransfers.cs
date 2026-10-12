// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task ScalarFrontendSwitchCrossArmTransfersMatchHostedAndExecuteAsync(string policy)
    {
        var cases = new (string Name, string Source, string Equivalent, int Expected)[] {
            ("forward-once", """
                public int32 Main() {
                    mut int32 calls = 0;
                    mut int32 total = 0;
                    switch (calls++) {
                        0 => { total = 10; continue 2; total = 0; },
                        2 => { total = total + 20; },
                        _ => { total = 100; },
                    }
                    return total * 10 + calls;
                }
                """, """
                public int32 Main() {
                    mut int32 calls = 0;
                    mut int32 total = 0;
                    int32 choice = calls++;
                    mut int32 arm = choice == 0 ? 0 : choice == 2 ? 1 : 2;
                    while (true) {
                        if (arm == 0) { total = 10; arm = 1; continue; total = 0; }
                        else if (arm == 1) { total = total + 20; break; }
                        else { total = 100; break; }
                    }
                    return total * 10 + calls;
                }
                """, 301),
            ("backward-cycle", """
                public int32 Main() {
                    mut int32 hits = 0;
                    switch (2) {
                        0 => { hits++; if (hits < 3) { continue 2; } },
                        2 => { continue 0; },
                        _ => { return 0; },
                    }
                    return hits;
                }
                """, """
                public int32 Main() {
                    mut int32 hits = 0;
                    mut int32 arm = 1;
                    while (true) {
                        if (arm == 0) {
                            hits++;
                            if (hits < 3) { arm = 1; continue; }
                            break;
                        } else { arm = 0; continue; }
                    }
                    return hits;
                }
                """, 3),
            ("nested-cleanup", """
                public int32 Main() {
                    mut int32 effects = 0;
                    switch (1) {
                        1 => {
                            try {
                                try { continue _; }
                                finally { effects = effects | 1; }
                            } finally { effects = effects == 1 ? 18 : 0; }
                        },
                        _ => { return effects; },
                    }
                }
                """, """
                public int32 Main() {
                    mut int32 effects = 0;
                    try {
                        try {} finally { effects = effects | 1; }
                    } finally { effects = effects == 1 ? 18 : 0; }
                    return effects;
                }
                """, 18),
            ("retained-outer-cleanup", """
                public int32 Main() {
                    mut int32 effects = 0;
                    try {
                        switch (1) {
                            1 => { continue _; },
                            _ => { effects = effects << 1; },
                        }
                    } finally { effects = effects | 1; }
                    return effects;
                }
                """, """
                public int32 Main() {
                    mut int32 effects = 0;
                    try { effects = effects << 1; }
                    finally { effects = effects | 1; }
                    return effects;
                }
                """, 1),
            ("continue-for", """
                public int32 Main() {
                    mut int32 total = 0;
                    for (mut int32 i = 0; i < 3; i++) {
                        switch (i) {
                            0 => { continue _; },
                            _ => { total++; continue; },
                        }
                    }
                    return total;
                }
                """, """
                public int32 Main() {
                    mut int32 total = 0;
                    for (mut int32 i = 0; i < 3; i++) { total++; continue; }
                    return total;
                }
                """, 3),
            ("label-precedence", """
                public int32 Main() {
                    const int32 N = 1;
                    N: for (mut int32 i = 0; i < 2; i++) {
                        switch (i) {
                            0 => { continue (N); },
                            N => { continue N; },
                            _ => {},
                        }
                    }
                    return 42;
                }
                """, """
                public int32 Main() {
                    N: for (mut int32 i = 0; i < 2; i++) { continue N; }
                    return 42;
                }
                """, 42),
            ("exit-nested-loop", """
                public int32 Main() {
                    switch (1) {
                        1 => { while (true) { continue _; } },
                        _ => { return 42; },
                    }
                }
                """, "public int32 Main() => 42;", 42),
            ("backward-from-wildcard", """
                public int32 Main() {
                    switch (7) {
                        0 => { return 42; },
                        _ => { continue 0; },
                    }
                }
                """, "public int32 Main() => 42;", 42),
            ("arm-local-reentry", """
                public int32 Main() {
                    mut int32 count = 0;
                    switch (0) {
                        0 => {
                            int32 saved = count;
                            count++;
                            if (saved < 2) { continue 0; }
                            return count;
                        },
                        _ => { return 0; },
                    }
                }
                """, """
                public int32 Main() {
                    mut int32 count = 0;
                    while (true) {
                        int32 saved = count;
                        count++;
                        if (saved < 2) { continue; }
                        return count;
                    }
                }
                """, 3),
            ("nearest-switch", """
                public int32 Main() {
                    switch (1) {
                        1 => {
                            switch (2) {
                                2 => { continue _; },
                                _ => {},
                            }
                            continue _;
                        },
                        _ => { return 42; },
                    }
                }
                """, "public int32 Main() => 42;", 42),
            ("continue-loop-cleanup", """
                public int32 Main() {
                    mut int32 i = 0;
                    while (i < 3) {
                        i++;
                        switch (i) {
                            1 => { continue _; },
                            _ => {
                                try { continue; }
                                finally { i = i | 4; }
                            },
                        }
                    }
                    return i;
                }
                """, """
                public int32 Main() {
                    mut int32 i = 0;
                    while (i < 3) {
                        i++;
                        try { continue; } finally { i = i | 4; }
                    }
                    return i;
                }
                """, 5),
            ("target-checked-failure", policy == "checked-exceptions" ? """
                public int32 Main() {
                    unsafe {
                        try {
                            switch (1) {
                                1 => { try { continue _; } finally {} },
                                _ => { require false; },
                            }
                        } catch { return 42; }
                    }
                }
                """ : """
                public int32 Main() {
                    switch (1) {
                        1 => { try { continue _; } finally {} },
                        _ => { require false; },
                    }
                }
                """, policy == "checked-exceptions" ? """
                public int32 Main() {
                    unsafe {
                        try {
                            try {} finally {}
                            require false;
                        } catch { return 42; }
                    }
                }
                """ : "public int32 Main() { try {} finally {} require false; }",
                policy == "checked-exceptions" ? 42 : unchecked((int)0xE0540003)),
        };
        var root = RepositoryRoot();
        var directory = Path.Combine(root, "self-host");
        foreach (var testCase in cases)
        {
            var native = Path.Combine(root, $"switch-transfer-{Guid.NewGuid():N}.native.exe");
            var hosted = Path.Combine(root, $"switch-transfer-{Guid.NewGuid():N}.hosted.exe");
            try
            {
                var input = Encoding.UTF8.GetBytes(testCase.Source);
                var (exit, output, error) = await RunAsync(input, "frontend-build", directory,
                    "-", "-o", native, "--runtime", policy, "--json");
                Assert.True(exit == 0, $"{testCase.Name} {policy}: {exit}: {output}{error}");
                var (hostExit, hostOutput, hostError) = await RunAsync(
                    Encoding.UTF8.GetBytes(testCase.Equivalent), "build", "-", "-o", hosted,
                    "--runtime", policy, "--json");
                Assert.True(hostExit == 0, $"{testCase.Name} {policy}: {hostExit}: {hostOutput}{hostError}");
                Assert.Equal(testCase.Expected, await ExecuteNativeImageAsync(native));
                Assert.Equal(testCase.Expected, await ExecuteNativeImageAsync(hosted));
                var (runExit, runOutput, runError) = await RunAsync(input, "frontend-run", directory,
                    "-", "--runtime", policy, "--json");
                Assert.True(runExit == testCase.Expected,
                    $"{testCase.Name} {policy}: {runExit}: {runOutput}{runError}");
            }
            finally
            {
                File.Delete(native);
                File.Delete(hosted);
            }
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task ScalarFrontendSwitchTransferTargetsRetainScalarWidthsAsync(string policy)
    {
        foreach (var (type, initial, target) in new[] {
            ("int32", "0", "-2147483648"),
            ("int32", "0", "+0x7fffffff"),
            ("uint32", "0", "4294967295"),
            ("int64", "0", "-9223372036854775808"),
            ("uint64", "0", "0xffffffffffffffff"),
            ("bool", "false", "(true)"),
            ("char16", "char16(0)", "(char16(65535))"),
            ("int32", "0", "(1 + 1)"),
        })
        {
            var source = $$"""
                public int32 Main() {
                    {{type}} choice = {{initial}};
                    switch (choice) {
                        {{initial}} => { continue {{target}}; },
                        {{target}} => { return 42; },
                        _ => { return 0; },
                    }
                }
                """;
            var path = Path.Combine(RepositoryRoot(), $"switch-target-{Guid.NewGuid():N}.exe");
            try
            {
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", Path.Combine(RepositoryRoot(), "self-host"), "-",
                    "-o", path, "--runtime", policy, "--json");
                Assert.True(exit == 0, $"{type} {target} {policy}: {exit}: {output}{error}");
                Assert.Equal(42, await ExecuteNativeImageAsync(path));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task ScalarFrontendSwitchTransferDiagnosticsRejectInvalidTargetsAndRetainImageAsync(string policy)
    {
        var cases = new[] {
            "int32 Main() { switch (1) { 1 => { continue 2; }, _ => { return 0; } } }",
            "int32 Main() { switch (1) { 1 => { continue 2; }, 2 when true => {}, _ => {} } return 0; }",
            "int32 Main() { mut int32 x = 1; switch (1) { 1 => { continue (x); }, _ => {} } return 0; }",
            "int32 Main() { switch (1) { 1 => { continue 1 + 0; }, _ => {} } return 0; }",
            "int32 Main() { switch (1) { 1 => { continue true; }, _ => {} } return 0; }",
            "int32 Main() { switch (1) { 1 => { break _; }, _ => {} } return 0; }",
            "int32 Main() { switch (1) { 1 => { continue; }, _ => {} } return 0; }",
            "int32 Main() { switch (1) { 1 => { continue (uint64(1)); }, _ => {} } return 0; }",
            "int32 Main() { continue _; return 0; }",
            "int32 Main() { continue (1); return 0; }",
            "int32 Main() { switch (1) { 1 => { try {} finally { continue _; } }, _ => {} } return 0; }",
            "int32 Main() { switch (1) { 1 => { return 0; }, _ => { try {} finally { continue 1; } } } return 0; }",
            "int32 Main() { var x = 1 switch { 1 => { continue _; }, _ => 0 }; return x; }",
            "int32 Main() { switch (1) { 1 => { int32 x = 42; continue _; }, _ => { return x; } } }",
            "int32 Main() { init int32 x; switch (1) { 1 => { continue _; }, _ => {} } return x; }",
            "int32 Main() { switch (1) { 1 => { continue _; }, _ => {} } }",
        };
        var path = Path.Combine(RepositoryRoot(), $"switch-invalid-{Guid.NewGuid():N}.exe");
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        try
        {
            var (initial, initialOutput, initialError) = await RunAsync(
                Encoding.UTF8.GetBytes("public int32 Main() => 42;"), "frontend-build",
                directory, "-", "-o", path, "--runtime", policy, "--json");
            Assert.True(initial == 0, $"{initial}: {initialOutput}{initialError}");
            var bytes = await File.ReadAllBytesAsync(path);
            foreach (var source in cases)
            {
                var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                    "frontend-build", directory, "-", "-o", path, "--runtime", policy, "--json");
                Assert.True(exit == 1, $"{source}: {exit}: {output}{error}");
                Assert.Empty(error);
                using var document = JsonDocument.Parse(output);
                Assert.False(document.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
                Assert.Equal(42, await ExecuteNativeImageAsync(path));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task ScalarFrontendSwitchTransferKeepsLabelDiagnosticAndCapacityAsync(string policy)
    {
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        const string missingLabel = "void Main() { const int32 N = 1; while (false) { switch (1) { 1 => { continue N; }, _ => {} } } }";
        var (labelExit, labelOutput, labelError) = await RunAsync(Encoding.UTF8.GetBytes(missingLabel),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.Equal(1, labelExit);
        Assert.Empty(labelError);
        using (var document = JsonDocument.Parse(labelOutput))
        {
            var result = document.RootElement.GetProperty("result");
            Assert.Equal(3, result.GetProperty("errorCode").GetInt32());
            Assert.Equal(missingLabel.IndexOf("N;", StringComparison.Ordinal),
                result.GetProperty("errorSpan").GetProperty("start").GetInt32());
            Assert.Equal(1, result.GetProperty("errorSpan").GetProperty("length").GetInt32());
        }
        var locals = string.Concat(Enumerable.Range(0, 7).Select(static i => $"int32 v{i} = 0; "));
        var maximum = $"void Main() {{ {locals} switch (0) {{ 0 => {{ continue _; }}, _ => {{}} }} return; }}";
        var (accepted, acceptedOutput, acceptedError) = await RunAsync(Encoding.UTF8.GetBytes(maximum),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.True(accepted == 0, $"{accepted}: {acceptedOutput}{acceptedError}");
        using (var document = JsonDocument.Parse(acceptedOutput))
        {
            var function = document.RootElement.GetProperty("result").GetProperty("functions")[0];
            Assert.Equal(12, function.GetProperty("statements").GetArrayLength());
            Assert.Equal(8, function.GetProperty("locals").GetArrayLength());
        }
        foreach (var source in new[] {
            maximum.Replace("return;", "return; return;", StringComparison.Ordinal),
            maximum.Replace("switch (0)", "int32 extra = 0; switch (0)", StringComparison.Ordinal),
        })
        {
            var (rejected, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.True(rejected == 1, $"{rejected}: {output}{error}");
            Assert.Empty(error);
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task ScalarFrontendSwitchTransferCleanupAcyclicGraphCompletesAsync(string policy)
    {
        const string source = """
            public int32 Main() {
                mut int32 value = 0;
                try {} finally {
                    switch (1) {
                        1 => { continue _; },
                        _ => { value = 42; },
                    }
                }
                return value;
            }
            """;
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", Path.Combine(RepositoryRoot(), "self-host"), "-",
            "--runtime", policy, "--json");
        Assert.True(exit == 0, $"{exit}: {output}{error}");
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        var (runExit, runOutput, runError) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-run", Path.Combine(RepositoryRoot(), "self-host"), "-",
            "--runtime", policy, "--json");
        Assert.True(runExit == 42, $"{runExit}: {runOutput}{runError}");
    }
}
