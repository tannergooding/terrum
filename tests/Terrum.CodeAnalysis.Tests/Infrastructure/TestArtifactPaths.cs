// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;

namespace Terrum.CodeAnalysis.Tests.Infrastructure;

internal static class TestArtifactPaths
{
    public static string DirectoryPath
    {
        get
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "tmp");
            _ = Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public static string CreateDirectory(string prefix)
    {
        var directory = Path.Combine(DirectoryPath, $"{prefix}-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(directory);
        return directory;
    }
}
