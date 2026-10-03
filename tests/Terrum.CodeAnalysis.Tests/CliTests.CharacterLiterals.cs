// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Fact]
    public async Task CharacterLiteralsExecuteThroughHostedCompilerAndJitAsync()
    {
        const string Source = """
            int32 Main() {
                char16 direct = 'A';
                char16 escaped = '\u0041';
                if (direct == escaped) { return int32(direct) - 23; }
                return 0;
            }
            """;
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
            "jit", "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(42, exit);
        Assert.Empty(output);
        Assert.Empty(error);
    }
}
