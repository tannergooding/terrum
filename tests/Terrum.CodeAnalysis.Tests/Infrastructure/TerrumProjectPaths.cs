using System.IO;

namespace Terrum.CodeAnalysis.Tests.Infrastructure;

internal static class TerrumProjectPaths
{
    public static string SourceDirectory(string repositoryRoot) =>
        Path.Combine(repositoryRoot, "sources", "Terrum.CodeAnalysis.Terrum");

    public static string SourceFile(string repositoryRoot, string relativePath) =>
        Path.Combine(SourceDirectory(repositoryRoot), relativePath);

    public static string TestFile(string repositoryRoot, string relativePath) =>
        Path.Combine(repositoryRoot, "tests", "Terrum.CodeAnalysis.Terrum.Tests", relativePath);

    public static string RepositoryFile(string repositoryRoot, string relativePath) =>
        Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
