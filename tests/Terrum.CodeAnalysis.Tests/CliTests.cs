// Copyright (c) Tanner Gooding and Contributors. Licensed under the MIT License.

using System;
using System.Diagnostics;
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
    [InlineData("check", "void Main() { return; }", 0)]
    [InlineData("tree", "void Main() { return 1 }", 1)]
    [InlineData("tokens", " \r\n\u03B1 + 12i32 // end", 0)]
    [InlineData("tokens", "1 \u0661", 1)]
    public async Task EmitsStructuredResultsWithExpectedExit(string command, string source, int exitCode)
    {
        (int exit, string output, string error) = await RunAsync(Encoding.UTF8.GetBytes(source), command, "-", "--json");
        Assert.Equal(exitCode, exit);
        Assert.Equal("", error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("utf-16", root.GetProperty("coordinateSystem").GetString());
        Assert.Equal(exitCode == 0, root.GetProperty("success").GetBoolean());
        Assert.Equal(source.Length, root.GetProperty("sourceLength").GetInt32());

        if (command != "check")
        {
            string reconstructed = string.Concat(root.GetProperty("tokens").EnumerateArray().Select(token =>
                string.Concat(token.GetProperty("leadingTrivia").EnumerateArray().Select(trivia => trivia.GetProperty("text").GetString())) + token.GetProperty("text").GetString()));
            Assert.Equal(source, reconstructed);
        }

        if (command == "tree")
        {
            Assert.Equal("CompilationUnit", root.GetProperty("nodes")[0].GetProperty("kind").GetString());
            Assert.Equal(64, Assert.IsType<string>(root.GetProperty("contentId").GetString()).Length);
        }
    }

    [Fact]
    public async Task HumanErrorsUseOneBasedLocationsAndStderr()
    {
        (int exit, string output, string error) = await RunAsync(Encoding.UTF8.GetBytes("void Main() {\nreturn 1\n}"), "check", "-");
        Assert.Equal(1, exit);
        Assert.Equal("", output);
        Assert.Contains("<stdin>(3,1): error TRM1001", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InputFailuresRemainStructuredAndDoNotClaimToParse()
    {
        foreach (string[] arguments in new[] { new[] { "bad", "-", "--json" }, new[] { "check", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".terrum"), "--json" } })
        {
            (int exit, string output, string error) = await RunAsync([], arguments);
            Assert.Equal(2, exit);
            Assert.Equal("", error);
            using JsonDocument document = JsonDocument.Parse(output);
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            Assert.StartsWith("TRM200", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task StrictUtf8RejectsInvalidBytesAndAcceptsAnOptionalBom()
    {
        (int exit, string output, _) = await RunAsync([0xFF, 0xFE, 0x00, 0xD8], "check", "-", "--json");
        Assert.Equal(2, exit);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal("TRM2001", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        (int validExit, _, _) = await RunAsync([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("void Main() {}")], "check", "-");
        Assert.Equal(0, validExit);
    }

    [Fact]
    public async Task FlatTreeProtocolPreservesReferencesAndDeepTrees()
    {
        string source = "void F() { return " + string.Join("+", Enumerable.Repeat("1", 500)) + "; }";
        (int exit, string output, _) = await RunAsync(Encoding.UTF8.GetBytes(source), "tree", "-", "--json");
        Assert.Equal(0, exit);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement nodes = document.RootElement.GetProperty("nodes");
        JsonElement tokens = document.RootElement.GetProperty("tokens");
        int[] nodeReferences = new int[nodes.GetArrayLength()];
        int[] tokenReferences = new int[tokens.GetArrayLength()];

        foreach (JsonElement node in nodes.EnumerateArray())
        {
            foreach (JsonElement child in node.GetProperty("children").EnumerateArray())
            {
                if (child.TryGetProperty("node", out JsonElement nodeId))
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
        (int secondExit, string secondOutput, _) = await RunAsync(Encoding.UTF8.GetBytes(source), "tree", "-", "--json");
        Assert.Equal(exit, secondExit);
        Assert.Equal(output, secondOutput);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(byte[] input, params string[] arguments)
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Name;
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string cli = Path.Combine(root, "artifacts", "bin", "terrumc", configuration, "terrumc.dll");
        Assert.True(File.Exists(cli), $"CLI was not built: {cli}");
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(cli);

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start CLI.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.BaseStream.WriteAsync(input);
        process.StandardInput.Close();
        Task completed = await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(20)));

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("CLI did not complete within 20 seconds.");
        }

        await completed;

        return (process.ExitCode, await output, await error);
    }
}
