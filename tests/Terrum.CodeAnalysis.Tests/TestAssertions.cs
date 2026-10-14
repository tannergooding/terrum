using System.Diagnostics.CodeAnalysis;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

internal static class TestAssertions
{
    public static void NotNull([NotNull] object? actual)
    {
        if (actual is null)
        {
            throw new AssertionException("Expected a non-null value.");
        }
    }
}
