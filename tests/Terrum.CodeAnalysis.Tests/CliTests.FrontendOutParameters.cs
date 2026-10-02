// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Fact]
    public async Task ScalarFrontendProjectsOutParametersThroughHostedVerificationAsync()
    {
        const string accepted = "public bool Try(out int32 value) { value = 41; value++; return true; }";
        const string unassigned = "public bool Try(out int32 value) { return true; }";
        var frontend = Path.Combine(RepositoryRoot(), "self-host");
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(accepted),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.True(exit == 0, $"{policy}: {exit}: {output}{error}");
            using (var document = JsonDocument.Parse(output))
            {
                Assert.Equal(69, document.RootElement.GetProperty("result")
                    .GetProperty("projectionVersion").GetInt32());
            }

            var (invalidExit, invalidOutput, invalidError) = await RunAsync(Encoding.UTF8.GetBytes(unassigned),
                "frontend-component", frontend, "-", "--runtime", policy, "--json");
            Assert.True(invalidExit == 1, $"{policy}: {invalidExit}: {invalidOutput}{invalidError}");
        }
    }
}
