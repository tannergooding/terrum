// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ExpressionBodiesSurviveScannerAndPersistedConsumersAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var directory = Path.Combine(root, "artifacts", $"expression-bodies-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var producerSource = Path.Combine(directory, "producer.terrum");
        var consumerSource = Path.Combine(directory, "consumer.terrum");
        var producer = Path.Combine(directory, "producer.trm");
        var consumer = Path.Combine(directory, "consumer.trm");
        var nativeProducer = Path.Combine(directory, "producer.trmn");
        var nativeConsumer = Path.Combine(directory, "consumer.trmn");
        var hostedImage = Path.Combine(directory, "host.exe");
        var scannedImage = Path.Combine(directory, "scanner.exe");
        const string Library = """
            public struct Value {
                public int32 Field;
                public Value(int32 value) => Field = value;
                public int32 Result => Field;
                public static Value Create(int32 value) => new Value(value);
            }
            public int32 Read(Value value) => value.Result;
            """;
        const string Entry = "int32 Main() => Read(Value.Create(42));";
        try
        {
            await File.WriteAllTextAsync(producerSource, Library);
            await File.WriteAllTextAsync(consumerSource, Entry);
            var (result, output, error) = await RunAsync([], "compile-module", "producer",
                producerSource, "Read", "--export", "Value.Create", "--export",
                "Value.#get:Result", "-o", producer, "--runtime", policy, "--json");
            Assert.That(result == 0, Is.True, output + error);
            (result, output, error) = await RunAsync([], "compile-module", "consumer",
                consumerSource, producer, "-o", consumer, "--runtime", policy, "--json");
            Assert.That(result == 0, Is.True, output + error);
            (result, _, _) = await RunAsync([], "run-module", consumer, producer, "--runtime", policy);
            Assert.That(result, Is.EqualTo(42));

            (result, output, error) = await RunAsync([], "compile-native", "producer",
                producerSource, "ExpressionProducer", "--export", "Read", "--export",
                "Value.Create", "--export", "Value.#get:Result", "-o", nativeProducer,
                "--runtime", policy, "--json");
            Assert.That(result == 0, Is.True, output + error);
            (result, output, error) = await RunAsync([], "compile-native", "consumer",
                consumerSource, nativeProducer, "-o", nativeConsumer, "--runtime", policy, "--json");
            Assert.That(result == 0, Is.True, output + error);
            (result, _, _) = await RunAsync([], "run-native", nativeConsumer, nativeProducer,
                "--runtime", policy);
            Assert.That(result, Is.EqualTo(42));

            var bytes = Encoding.UTF8.GetBytes(Entry + Environment.NewLine + Library);
            (result, output, error) = await RunAsync(bytes, "build", "-", "-o", hostedImage,
                "--runtime", policy, "--json");
            Assert.That(result == 0, Is.True, output + error);
            (result, output, error) = await RunAsync(bytes, "build-component",
                Path.Combine(root, "self-host"), "-", "-o", scannedImage,
                "--runtime", policy, "--json");
            Assert.That(result == 0, Is.True, output + error);
            Assert.That(await File.ReadAllBytesAsync(scannedImage), Is.EqualTo(await File.ReadAllBytesAsync(hostedImage)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
