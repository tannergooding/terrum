// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class CliTests
{
    [Theory]
    [InlineData("check")]
    [InlineData("tree")]
    public async Task RepeatedOptionalReportsPreciseStructuredDiagnosticAsync(string command)
    {
        const string Source = "void F(T? /* gap */\n? value) {}";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), command, "-", "--json");
        Assert.Equal(1, exit);
        Assert.Equal("", error);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("syntax-1", root.GetProperty("languageVersion").GetString());
        var diagnostic = Assert.Single(root.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("TRM1005", diagnostic.GetProperty("code").GetString());
        Assert.Equal(Source.LastIndexOf('?', StringComparison.Ordinal), diagnostic.GetProperty("span").GetProperty("start").GetInt32());
        Assert.Equal(1, diagnostic.GetProperty("span").GetProperty("length").GetInt32());

        if (command == "tree")
        {
            var reconstructed = string.Concat(root.GetProperty("tokens").EnumerateArray().Select(token =>
                string.Concat(token.GetProperty("leadingTrivia").EnumerateArray().Select(trivia => trivia.GetProperty("text").GetString())) + token.GetProperty("text").GetString()));
            Assert.Equal(Source, reconstructed);
        }
    }

    [Theory]
    [InlineData("name")]
    [InlineData("\"quote\\\" and \\\\ slash\"")]
    [InlineData("\"<tag> \u03B1 \U0001F600\"")]
    public async Task HumanTokensPreserveJsonEscapingAsync(string source)
    {
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), "tokens", "-");
        Assert.Equal(0, exit);
        Assert.Equal("", error);
        Assert.Contains(JsonSerializer.Serialize(source), output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("check", "void Main() { return; }", 0)]
    [InlineData("tree", "void Main() { return 1 }", 1)]
    [InlineData("tokens", " \r\n\u03B1 + 12i32 // end", 0)]
    [InlineData("tokens", "1 \u0661", 1)]
    [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "The xUnit data rows supply non-null source strings.")]
    public async Task EmitsStructuredResultsWithExpectedExitAsync(string command, string source, int exitCode)
    {
        (var exit, var output, var error) = await RunAsync(Encoding.UTF8.GetBytes(source), command, "-", "--json");
        Assert.Equal(exitCode, exit);
        Assert.Equal("", error);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("utf-16", root.GetProperty("coordinateSystem").GetString());
        Assert.Equal(exitCode == 0, root.GetProperty("success").GetBoolean());
        Assert.Equal(source.Length, root.GetProperty("sourceLength").GetInt32());

        if (command != "check")
        {
            var reconstructed = string.Concat(root.GetProperty("tokens").EnumerateArray().Select(token =>
                string.Concat(token.GetProperty("leadingTrivia").EnumerateArray().Select(trivia => trivia.GetProperty("text").GetString())) + token.GetProperty("text").GetString()));
            Assert.Equal(source, reconstructed);
        }

        if (command == "tree")
        {
            Assert.Equal("CompilationUnit", root.GetProperty("nodes")[0].GetProperty("kind").GetString());
            Assert.Equal(64, Assert.IsType<string>(root.GetProperty("contentId").GetString()).Length);
        }
    }

    [Theory]
    [InlineData("void Main() {\nreturn 1\n}", "<stdin>(3,1): error TRM1001")]
    [InlineData("void F(T? /* gap */\n? value) {}", "<stdin>(2,1): error TRM1005")]
    public async Task HumanErrorsUseOneBasedLocationsAndStderrAsync(string source, string diagnostic)
    {
        (var exit, var output, var error) = await RunAsync(Encoding.UTF8.GetBytes(source), "check", "-");
        Assert.Equal(1, exit);
        Assert.Equal("", output);
        Assert.Contains(diagnostic, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InputFailuresRemainStructuredAndDoNotClaimToParseAsync()
    {
        foreach (var arguments in new[] { new[] { "bad", "-", "--json" }, ["check", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".terrum"), "--json"] })
        {
            (var exit, var output, var error) = await RunAsync([], arguments);
            Assert.Equal(2, exit);
            Assert.Equal("", error);
            using var document = JsonDocument.Parse(output);
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            Assert.StartsWith("TRM200", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task StrictUtf8RejectsInvalidBytesAndAcceptsAnOptionalBomAsync()
    {
        (var exit, var output, _) = await RunAsync([0xFF, 0xFE, 0x00, 0xD8], "check", "-", "--json");
        Assert.Equal(2, exit);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("TRM2001", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        (var validExit, _, _) = await RunAsync([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("void Main() {}")], "check", "-");
        Assert.Equal(0, validExit);
    }

    [Fact]
    public async Task FlatTreeProtocolPreservesReferencesAndDeepTreesAsync()
    {
        var source = "void F() { return " + string.Join("+", Enumerable.Repeat("1", 500)) + "; }";
        (var exit, var output, _) = await RunAsync(Encoding.UTF8.GetBytes(source), "tree", "-", "--json");
        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output);
        var nodes = document.RootElement.GetProperty("nodes");
        var tokens = document.RootElement.GetProperty("tokens");
        var nodeReferences = new int[nodes.GetArrayLength()];
        var tokenReferences = new int[tokens.GetArrayLength()];

        foreach (var node in nodes.EnumerateArray())
        {
            foreach (var child in node.GetProperty("children").EnumerateArray())
            {
                if (child.TryGetProperty("node", out var nodeId))
                {
                    nodeReferences[nodeId.GetInt32()]++;
                }
                else
                {
                    tokenReferences[child.GetProperty("token").GetInt32()]++;
                }
            }
        }

        Assert.Equal(0, nodeReferences[0]);
        Assert.All(nodeReferences.Skip(1), count => Assert.Equal(1, count));
        Assert.All(tokenReferences, count => Assert.Equal(1, count));
        (var secondExit, var secondOutput, _) = await RunAsync(Encoding.UTF8.GetBytes(source), "tree", "-", "--json");
        Assert.Equal(exit, secondExit);
        Assert.Equal(output, secondOutput);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(byte[] input, params string[] arguments)
    {
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name ?? throw new InvalidOperationException("Missing configuration directory.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var cli = Path.Combine(root, "artifacts", "bin", "sources", "terrumc", configuration, frameworkDirectory.Name, "terrumc.dll");
        Assert.True(File.Exists(cli), $"CLI was not built: {cli}");
        var start = new ProcessStartInfo("dotnet") {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(cli);

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start CLI.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.BaseStream.WriteAsync(input).ConfigureAwait(true);
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(true);
            }

            throw;
        }

        return (process.ExitCode, await output.ConfigureAwait(true), await error.ConfigureAwait(true));
    }
}
