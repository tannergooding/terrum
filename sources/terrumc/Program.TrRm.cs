// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text.Json;
using Terrum.CodeAnalysis.Modules;

namespace Terrum.Tools;

internal static partial class Program
{
    private static int RunTrRmStructureCommand(string[] args, bool json)
    {
        if (args.Length is not (2 or 3) || args[1] == "-"
            || (args.Length == 3 && args[2] != "--json"))
        {
            WriteInputError(json, "TRM2000", Usage);
            return 2;
        }

        byte[] artifact;
        try
        {
            artifact = File.ReadAllBytes(args[1]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            WriteInputError(json, "TRM2001", exception.Message);
            return 2;
        }

        try
        {
            TrRmStructuralMetadataValidator.Validate(artifact);
        }
        catch (InvalidDataException exception)
        {
            WriteInputError(json, "TRM5001", exception.Message);
            return 1;
        }

        if (json)
        {
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(),
                new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            writer.WriteNumber("schema", 1);
            writer.WriteString("command", args[0]);
            writer.WriteBoolean("success", true);
            writer.WriteString("scope", "framing-and-structural-metadata");
            writer.WriteEndObject();
        }
        else
        {
            Console.Out.WriteLine($"{args[1]}: TRRM framing and structural metadata valid (Body not verified)");
        }
        return 0;
    }
}
