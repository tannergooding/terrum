// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

public sealed partial class CliTests
{
    private static string CreateTestArtifactPath(string fileName) =>
        Path.Combine(TestArtifactPaths.DirectoryPath, fileName);
}
