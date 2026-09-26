// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.Tools;

internal static partial class Program
{
    private const string NativeGraphCatalogUsage = "Usage: terrumc resolve-native-graph <consumer.trng> "
        + "--candidate <local.trmn|local.trng> [--candidate <local.trmn|local.trng>] [--json]";

    private static int RunNativeGraphCatalogCommand(string[] args, bool json)
    {
        var count = args.Length - (args.Length != 0 && args[^1] == "--json" ? 1 : 0);
        if (count < 4 || (count & 1) != 0
            || (json && (count == args.Length || Array.IndexOf(args, "--json") != args.Length - 1)))
        {
            WriteInputError(json, "TRM2000", NativeGraphCatalogUsage);
            return 2;
        }
        var paths = new List<string>();
        for (var index = 2; index < count; index += 2)
        {
            if (args[index] != "--candidate" || paths.Count == 32)
            {
                WriteInputError(json, "TRM2000", NativeGraphCatalogUsage);
                return 2;
            }
            paths.Add(args[index + 1]);
        }
        try
        {
            var rootPath = Path.GetFullPath(args[1]);
            var candidatePaths = paths.Select(Path.GetFullPath).ToArray();
            var totalBytes = new FileInfo(rootPath).Length;
            foreach (var path in candidatePaths)
            {
                totalBytes += new FileInfo(path).Length;
                if (totalBytes > 64 * 1024 * 1024)
                {
                    throw new ModuleContractException("Native graph catalog exceeds the 64 MiB aggregate input limit.");
                }
            }
            var root = ReadModuleFile(rootPath, 16 * 1024 * 1024);
            var candidates = candidatePaths.Select(path => ReadModuleFile(path, 16 * 1024 * 1024)).ToArray();
            var selection = NativeModuleCompiler.ResolveGraphCatalog(root, candidates);
            var rootSnapshot = selection.Consumer.ToArray();
            var info = NativeModuleCompiler.InspectGraphArtifact(rootSnapshot);
            var selected = selection.Producers.Select((bytes, index) => {
                var snapshot = bytes.ToArray();
                return (Bytes: snapshot, Path: candidatePaths[selection.CandidateIndexes[index]],
                    Info: NativeModuleCompiler.InspectGraphArtifact(snapshot));
            }).ToArray();
            if (!json)
            {
                Console.WriteLine($"{rootPath}: resolved {selected.Length} native graph producers.");
                foreach (var item in selected)
                {
                    Console.WriteLine($"{item.Info.Identity}: {item.Path} {Convert.ToHexString(SHA256.HashData(item.Bytes))}");
                }
                return 0;
            }
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(),
                new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("command", args[0]);
            writer.WriteString("profile", info.NativeProfileVersion);
            writer.WriteString("runtimePolicy", PolicyName(info.RuntimePolicy));
            writer.WriteBoolean("success", true);
            writer.WriteStartArray("diagnostics");
            writer.WriteEndArray();
            writer.WriteStartObject("artifact");
            writer.WriteString("path", rootPath);
            writer.WriteString("kind", "native-graph-consumer");
            writer.WriteNumber("length", rootSnapshot.Length);
            writer.WriteString("sha256", Convert.ToHexString(SHA256.HashData(rootSnapshot)));
            writer.WriteString("imageSha256", info.ImageSha256);
            writer.WriteStartArray("dependencies");
            foreach (var dependency in info.Dependencies)
            {
                writer.WriteStringValue(dependency);
            }
            writer.WriteEndArray();
            writer.WriteStartArray("exports");
            writer.WriteEndArray();
            writer.WriteStartArray("selectedProviders");
            foreach (var item in selected)
            {
                writer.WriteStartObject();
                writer.WriteString("identity", item.Info.Identity);
                writer.WriteString("path", item.Path);
                writer.WriteString("sha256", Convert.ToHexString(SHA256.HashData(item.Bytes)));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
            return 0;
        }
        catch (ModuleFormatException exception)
        {
            WriteInputError(json, "TRM5001", exception.Message);
            return 1;
        }
        catch (ModuleContractException exception)
        {
            WriteInputError(json, "TRM5002", exception.Message);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            WriteInputError(json, "TRM5003", exception.Message);
            return 2;
        }
    }
}
