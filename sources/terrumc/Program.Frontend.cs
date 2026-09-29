// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Text.Json;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.Tools;

internal static partial class Program
{
    private static int RunFrontendComponent(string[] args, bool json)
    {
        if (args.Length is not (5 or 6) || args[1] == "-" || args[3] != "--runtime"
            || args[4] is not ("fail-fast" or "checked-exceptions")
            || (args.Length == 6 && args[5] != "--json"))
        {
            WriteInputError(json, "TRM2000", Usage);
            return 2;
        }

        SourceText source;
        List<SyntaxTree> trees = new();
        string[] names = ["SourceRange", "Token", "ManagedTokenChain", "Scanner",
            "ManagedScanner", "TokenSnapshot", "TokenTriviaSnapshot",
            "TokenIntegerSnapshot", "TokenDiagnosticSnapshot", "ScalarParserTypes",
            "ScalarFrontend", "ScalarFrontendProjection"];
        try
        {
            source = SourceText.From(ReadSource(args[2]), args[2] == "-" ? "<stdin>" : args[2]);
            if (source.Length > ScannerHost.MaximumSourceLength)
            {
                WriteInputError(json, "TRM2001", "The frontend input exceeds its bounded source limit.");
                return 2;
            }
            foreach (var name in names)
            {
                var path = Path.Combine(args[1], name + ".terrum");
                trees.Add(SyntaxTree.Parse(ReadSource(path), path));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or DecoderFallbackException or ArgumentException or NotSupportedException)
        {
            WriteInputError(json, "TRM2001", exception.Message);
            return 2;
        }

        trees.Add(SyntaxTree.Parse(
            "using Terrum.CodeAnalysis; int32 Main() { if (!TryPublishScalarFrontend()) { return 1; } return 0; }",
            "<frontend-component>"));
        var policy = args[4] == "fail-fast"
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        var compilation = Compilation.Create(trees, ScalarFrontendHost.CreateBuffers(source),
            (BoundRuntimePolicy)policy);
        if (compilation.HasErrors)
        {
            if (json)
            {
                WriteCompilationJson(args[0], trees[0], compilation.Diagnostics, false,
                    null, null, policy, compilation.NativeProfileVersion ?? Profile(policy));
            }
            else
            {
                foreach (var diagnostic in compilation.Diagnostics)
                {
                    Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
                }
            }
            return 1;
        }

        if (compilation.Program is not BoundProgram program)
        {
            throw new InvalidOperationException("A successful frontend compilation must contain a bound program.");
        }

        NativeExecutionResult native;
        try
        {
            native = NativeJit.ExecuteWithData(program, policy, GraphJitRegistrarAssemblyPath());
        }
        catch (NotSupportedException exception)
        {
            WriteInputError(json, "TRM4001", exception.Message);
            return 1;
        }

        try
        {
            if (native.ExitCode != 0)
            {
                var diagnosticBytes = native.Data.AsSpan(native.BufferOffsets[4],
                    program.Buffers[4].Values.Length * sizeof(int));
                var diagnostics = ScannerHost.ReadDiagnostics(source, diagnosticBytes);
                if (native.ExitCode != 1 || diagnostics.IsEmpty)
                {
                    throw new ArgumentException("The frontend failed without lexical diagnostics.");
                }
                WriteFrontendOutput(json, false, null, diagnostics);
                return 1;
            }
            var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
                ScalarFrontendHost.WordCount * sizeof(int));
            var result = ScalarFrontendHost.ReadResult(source, bytes);
            WriteFrontendOutput(json, result.IsValid, result, []);
            return result.IsValid ? 0 : 1;
        }
        catch (ArgumentException exception)
        {
            WriteInputError(json, "TRM5001", exception.Message);
            return 1;
        }
    }

    private static void WriteFrontendOutput(bool json, bool success,
        ScalarFrontendResult? result, ImmutableArray<ScannerDiagnostic> diagnostics)
    {
        if (json)
        {
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(),
                new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            writer.WriteNumber("schema", 1);
            writer.WriteString("command", "frontend-component");
            writer.WriteBoolean("success", success);
            if (result is ScalarFrontendResult frontend)
            {
                writer.WriteStartObject("result");
                writer.WriteBoolean("valid", frontend.IsValid);
                writer.WriteNumber("errorCode", frontend.ErrorCode);
                WriteFrontendSpan(writer, "errorSpan", frontend.ErrorSpan);
                if (frontend.IsValid)
                {
                    writer.WriteNumber("returnType", frontend.ReturnType);
                    writer.WriteNumber("parameterCount", frontend.ParameterCount);
                    writer.WriteNumber("firstType", frontend.FirstType);
                    writer.WriteNumber("secondType", frontend.SecondType);
                    WriteFrontendSpan(writer, "name", frontend.Name);
                    WriteFrontendSpan(writer, "firstName", frontend.FirstName);
                    WriteFrontendSpan(writer, "secondName", frontend.SecondName);
                    writer.WriteBoolean("hasCondition", frontend.HasCondition);
                    if (frontend.HasCondition)
                    {
                        WriteFrontendExpression(writer, "condition", frontend.Condition);
                        WriteFrontendExpression(writer, "whenFalse", frontend.WhenFalse);
                    }
                    WriteFrontendExpression(writer, "whenTrue", frontend.WhenTrue);
                }
                writer.WriteEndObject();
            }
            writer.WriteStartArray("diagnostics");
            foreach (var diagnostic in diagnostics)
            {
                writer.WriteStartObject();
                writer.WriteString("code", diagnostic.Code);
                WriteFrontendSpan(writer, "span", diagnostic.Span);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        else if (result is ScalarFrontendResult frontend)
        {
            if (frontend.IsValid)
            {
                Console.Out.WriteLine($"Scalar function at [{frontend.Name.Start}..{frontend.Name.End}), {frontend.ParameterCount} parameters.");
            }
            else
            {
                Console.Error.WriteLine($"Frontend {frontend.ErrorCode} at [{frontend.ErrorSpan.Start}..{frontend.ErrorSpan.End}).");
            }
        }
        else
        {
            foreach (var diagnostic in diagnostics)
            {
                Console.Error.WriteLine($"{diagnostic.Code} at [{diagnostic.Span.Start}..{diagnostic.Span.End}).");
            }
        }
    }

    private static void WriteFrontendSpan(Utf8JsonWriter writer, string name, TextSpan span)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("start", span.Start);
        writer.WriteNumber("length", span.Length);
        writer.WriteEndObject();
    }

    private static void WriteFrontendExpression(Utf8JsonWriter writer, string name,
        ScalarFrontendExpression expression)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("type", expression.Type);
        writer.WriteNumber("operation", expression.Operation);
        writer.WriteNumber("leftParameter", expression.LeftParameter);
        writer.WriteNumber("rightParameter", expression.RightParameter);
        WriteFrontendSpan(writer, "span", expression.Span);
        writer.WriteEndObject();
    }
}
