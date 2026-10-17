// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Test]
    public async Task ClosedGenericStructSpecializationExecutesAsOrdinaryNativeTypesAsync()
    {
        const string Source = """
            struct Box<T>
            {
                T value;
                public Box(T value) { this.value = value; }
                public T Get() { return this.value; }
            }
            struct Nested<T>
            {
                Box<T> value;
                public Nested(Box<T> value) { this.value = value; }
                public T Get() { return this.value.Get(); }
            }
            int32 Main()
            {
                Nested<int32> value = new Nested<int32>(new Box<int32>(42));
                return value.Get();
            }
            """;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var directory = Path.Combine(root, "artifacts", $"generic-nominal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var executable = Path.Combine(directory, "generic-nominal.exe");
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
                "build", "-", "-o", executable, "--runtime", "fail-fast", "--json");
            Assert.That(exit, Is.Zero, output + error);

            using var process = Process.Start(new ProcessStartInfo(executable) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException("Could not start generic nominal executable.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw;
            }
            Assert.That(process.ExitCode, Is.EqualTo(42), await outputTask + await errorTask);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory);
        }
    }
}
