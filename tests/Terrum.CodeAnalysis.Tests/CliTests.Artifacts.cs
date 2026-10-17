// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    private static string CreateTestArtifactPath(string fileName)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-output");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }
}
