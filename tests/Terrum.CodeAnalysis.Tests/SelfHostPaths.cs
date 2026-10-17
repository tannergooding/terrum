using System;
using System.IO;

namespace Terrum.CodeAnalysis.Tests;

internal static class SelfHostPaths
{
    public static string SourceDirectory(string repositoryRoot) =>
        Path.Combine(repositoryRoot, "sources", "self-host");

    public static string ResolveFile(string repositoryRoot, string fileName)
    {
        var sourcePath = Path.Combine(SourceDirectory(repositoryRoot), fileName);
        var testPath = Path.Combine(repositoryRoot, "tests", "self-host", fileName);
        var sourceExists = File.Exists(sourcePath);
        var testExists = File.Exists(testPath);
        if (sourceExists && testExists)
        {
            throw new InvalidOperationException($"Self-host file '{fileName}' exists in both source and test trees.");
        }
        if (sourceExists)
        {
            return sourcePath;
        }
        if (testExists)
        {
            return testPath;
        }
        throw new FileNotFoundException($"Self-host file '{fileName}' was not found in either tree.", fileName);
    }
}
