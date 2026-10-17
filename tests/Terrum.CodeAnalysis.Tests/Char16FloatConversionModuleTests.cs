// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.
using System;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class Char16FloatConversionModuleTests
{
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
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
            var path = SelfHostPaths.ResolveFile(root, dependencyNames[index] + ".terrum");
            trees[index] = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(trees[index].HasErrors, Is.False, string.Join(Environment.NewLine, trees[index].Diagnostics));
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
        Assert.That(entry.HasErrors, Is.False, string.Join(Environment.NewLine, entry.Diagnostics));
        var compilation = Compilation.Create([.. trees, entry], policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(NativeJit.Execute(program, (NativeRuntimePolicy)policy), Is.EqualTo(0));
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
