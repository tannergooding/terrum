// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.
using System;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class Char16FloatConversionModuleTests
{
    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task Char16FloatConversionMatchesUnsignedCodeUnitBitsAsync(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var trees = new SyntaxTree[4];
        var dependencyNames = new[]
        {
            "WideIntegerFacts",
            "IntegerFloatConversion",
            "Char16FloatConversion",
            "Char16FloatConversionTests",
        };
        for (var index = 0; index < dependencyNames.Length; index++)
        {
            var path = Path.Combine(root, "self-host", dependencyNames[index] + ".terrum");
            trees[index] = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.False(trees[index].HasErrors, string.Join(Environment.NewLine, trees[index].Diagnostics));
        }

        var entry = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis.Tests;
            int32 Main() {
                if (!CheckChar16FloatConversion()) {
                    return 1;
                }
                return 0;
            }
            """);
        Assert.False(entry.HasErrors, string.Join(Environment.NewLine, entry.Diagnostics));
        var compilation = Compilation.Create([.. trees, entry], policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(0, NativeJit.Execute(program, (NativeRuntimePolicy)policy));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Terrum.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate Terrum.slnx.");
    }
}
