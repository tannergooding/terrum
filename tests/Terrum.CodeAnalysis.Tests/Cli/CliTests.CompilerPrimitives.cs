// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    private static readonly Lazy<string> s_compilerPrimitivesTestLibrary = new(CreateCompilerPrimitivesTestLibrary);

    private static string CreateCompilerPrimitivesTestLibrary()
    {
        var root = RepositoryRoot();
        var sourcePaths = new[] {
            Path.Combine("Lexing", "CharacterFacts.terrum"),
            Path.Combine("Utilities", "Numerics", "BitOperations.terrum"),
            Path.Combine("Utilities", "Numerics", "Float32MathSelection.terrum"),
            Path.Combine("Utilities", "Numerics", "Float64MathSelection.terrum"),
            Path.Combine("Utilities", "Numerics", "IntegerMath.terrum"),
            Path.Combine("Utilities", "Numerics", "IntegerFormatting.terrum"),
            Path.Combine("Utilities", "Numerics", "IntegerCheckedArithmetic.terrum"),
            Path.Combine("Utilities", "Numerics", "IntegerCheckedConversions.terrum"),
            Path.Combine("Utilities", "Numerics", "IntegerFloatConversion.terrum"),
            Path.Combine("Utilities", "Numerics", "Char16FloatConversion.terrum"),
            Path.Combine("Utilities", "Numerics", "FloatingFormatConversion.terrum"),
            Path.Combine("Utilities", "Numerics", "FloatingNegation.terrum"),
            Path.Combine("Utilities", "Numerics", "FloatingComparison.terrum"),
            Path.Combine("Utilities", "Numerics", "FloatingArithmetic.terrum"),
            Path.Combine("Utilities", "Numerics", "FloatingIntegerConversion.terrum"),
            Path.Combine("Utilities", "Numerics", "IntegerSaturation.terrum"),
            Path.Combine("Utilities", "Numerics", "IntegerWrapping.terrum"),
            Path.Combine("Text", "SourceRange.terrum"),
            Path.Combine("Lexing", "Token.terrum"),
            Path.Combine("Text", "Utf16CodeUnits.terrum"),
            Path.Combine("Utilities", "Numerics", "WideIntegerFacts.terrum"),
            Path.Combine("Utilities", "Numerics", "IntegerFacts.terrum"),
            Path.Combine("Utilities", "CopyAlgorithms.terrum"),
        };
        var adapterNames = new[] { "integer-formatting-abi", "token-abi", "integer-facts-abi", "copy-algorithms-abi" };
        var trees = sourcePaths
            .Select(path => SyntaxTree.Parse(File.ReadAllText(TerrumProjectPaths.SourceFile(root, path))))
            .Concat(adapterNames
                .Select(name => SyntaxTree.Parse(File.ReadAllText(TerrumProjectPaths.TestFile(root,
                    Path.Combine("TestHelpers", "Abi", name + ".terrum"))))));
        var compilation = Compilation.CreateLibrary([.. trees], BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected the test primitives library.");
        var exports = program.Functions.Select(static function => function.Name)
            .Where(static name => name.EndsWith("ForAbiTest", StringComparison.Ordinal)
                || name is "Terrum.CodeAnalysis.Token.#ctor" or "Terrum.CodeAnalysis.SourceRange.TryCreate")
            .ToArray();
        var image = NativeEmitter.EmitLibrary(program, exports, NativeRuntimePolicy.FailFast);
        var directory = TestArtifactPaths.CreateDirectory("terrum-compiler-primitives-tests-");
        var path = Path.Combine(directory, "compiler-primitives-tests.dll");
        File.WriteAllBytes(path, image);
        return path;
    }

    [OneTimeTearDown]
    public static async Task DeleteCompilerPrimitivesTestLibraryAsync()
    {
        if (s_compilerPrimitivesTestLibrary.IsValueCreated)
        {
            var directory = Path.GetDirectoryName(s_compilerPrimitivesTestLibrary.Value)
                ?? throw new InvalidOperationException("Missing test-library directory.");
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory).ConfigureAwait(false);
        }
    }
}
