// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.IO;
using System.Linq;
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
        var buildLibrary = args[0] == "frontend-build-library";
        var buildExecutable = args[0] == "frontend-build";
        var runExecutable = args[0] == "frontend-run";
        var build = buildLibrary || buildExecutable;
        var count = build ? 7 : 5;
        var runtime = build ? 5 : 3;
        if ((args.Length != count && args.Length != count + 1) || args[1] == "-"
            || (build && args[3] != "-o") || args[runtime] != "--runtime"
            || args[runtime + 1] is not ("fail-fast" or "checked-exceptions")
            || (args.Length == count + 1 && args[count] != "--json"))
        {
            WriteInputError(json, "TRM2000", Usage);
            return 2;
        }

        SourceText source;
        string? outputPath = null;
        List<SyntaxTree> trees = new();
        string[] names = ["SourceRange", "Token", "ManagedTokenChain", "Scanner",
            "ManagedScanner", "TokenSnapshot", "TokenTriviaSnapshot",
            "TokenIntegerSnapshot", "TokenDiagnosticSnapshot", "ScalarParserTypes",
            "ScalarFrontend", "ScalarFrontendProjection"];
        try
        {
            if (build)
            {
                outputPath = Path.GetFullPath(args[4]);
                var extension = buildExecutable ? ".exe" : ".dll";
                if (!string.Equals(Path.GetExtension(outputPath), extension, StringComparison.OrdinalIgnoreCase)
                    || (args[2] != "-" && string.Equals(outputPath, Path.GetFullPath(args[2]),
                        StringComparison.OrdinalIgnoreCase)))
                {
                    WriteInputError(json, "TRM2002", $"Output must be a distinct {extension} path, not the input source.");
                    return 2;
                }
            }
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
        var policy = args[runtime + 1] == "fail-fast"
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        var compilation = Compilation.Create(trees, ScalarFrontendHost.CreateBuffers(source),
            (BoundRuntimePolicy)policy);
        if (compilation.HasErrors)
        {
            WriteCompilationResult(args[0], compilation, compilation.SourceDiagnostics,
                false, null, null, json, policy);
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
                WriteFrontendOutput(args[0], json, false, null, diagnostics, outputPath);
                return 1;
            }
            var bytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.BufferIndex],
                ScalarFrontendHost.WordCount * sizeof(int));
            var result = ScalarFrontendHost.ReadResult(source, bytes);
            if ((build || runExecutable) && result.IsValid)
            {
                if (buildLibrary && !result.IsPublic)
                {
                    WriteInputError(json, "TRM4002", "A native frontend library export must be declared public.");
                    return 1;
                }
                if ((buildExecutable || runExecutable) && (result.ParameterCount != 0 || result.ReturnType is not (0 or 1)
                    || !source.Text.AsSpan(result.Name.Start, result.Name.Length).SequenceEqual("Main")))
                {
                    WriteInputError(json, "TRM4002", "A native frontend entry requires int32 or void Main() without parameters.");
                    return 1;
                }
                var library = ScalarFrontendHost.BindLibrary(source, bytes, (BoundRuntimePolicy)policy);
                if (runExecutable)
                {
                    try
                    {
                        return NativeJit.Execute(library with { EntryPoint = 0 }, policy,
                            GraphJitRegistrarAssemblyPath());
                    }
                    catch (Exception exception) when (exception is NotSupportedException or IOException
                        or Win32Exception or InvalidOperationException or TimeoutException)
                    {
                        WriteInputError(json, "TRM4001", exception.Message);
                        return 1;
                    }
                }
                byte[] image;
                try
                {
                    image = buildExecutable
                        ? NativeEmitter.Emit(library with { EntryPoint = 0 }, policy)
                        : NativeEmitter.EmitLibrary(library,
                            [IdentifierName.GetName(source.Text.Substring(result.Name.Start, result.Name.Length))], policy);
                }
                catch (NotSupportedException exception)
                {
                    WriteInputError(json, "TRM4001", exception.Message);
                    return 1;
                }
                catch (ArgumentException exception)
                {
                    WriteInputError(json, "TRM4002", exception.Message);
                    return 1;
                }
                try
                {
                    WriteArtifact(outputPath ?? throw new InvalidOperationException("Missing frontend output path."), image);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                    or ArgumentException or NotSupportedException)
                {
                    WriteInputError(json, "TRM2002", exception.Message);
                    return 2;
                }
            }
            WriteFrontendOutput(args[0], json, result.IsValid, result, [], build ? outputPath : null);
            return result.IsValid ? 0 : 1;
        }
        catch (ArgumentException exception)
        {
            WriteInputError(json, "TRM5001", exception.Message);
            return 1;
        }
    }

    private static void WriteFrontendOutput(string command, bool json, bool success,
        ScalarFrontendResult? result, ImmutableArray<ScannerDiagnostic> diagnostics,
        string? outputPath = null)
    {
        if (json)
        {
            using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(),
                new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            writer.WriteNumber("schema", 1);
            writer.WriteString("command", command);
            writer.WriteBoolean("success", success);
            if (outputPath is not null && success)
            {
                writer.WriteString("output", outputPath);
            }
            if (result is ScalarFrontendResult frontend)
            {
                writer.WriteStartObject("result");
                writer.WriteBoolean("valid", frontend.IsValid);
                writer.WriteBoolean("public", frontend.IsPublic);
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
                    if (frontend.HasFallthrough)
                    {
                        writer.WriteBoolean("hasFallthrough", true);
                    }
                    if (frontend.HasCondition)
                    {
                        WriteFrontendExpression(writer, "condition", frontend.Condition);
                        WriteFrontendExpression(writer, "whenFalse", frontend.WhenFalse);
                    }
                    WriteFrontendExpression(writer, "whenTrue", frontend.WhenTrue);
                    if (!frontend.Nodes.IsDefault)
                    {
                        var projectionVersion = frontend.Statements.IsDefaultOrEmpty ? 20 : 21;
                        if (frontend.Nodes.Any(static node => node.Operation == 83))
                        {
                            projectionVersion += 2;
                        }
                        if (frontend.Nodes.Any(static node => node.Operation == 84))
                        {
                            projectionVersion += 4;
                        }
                        if (frontend.Statements.Any(static statement => statement.Kind == 3))
                        {
                            projectionVersion = 28
                                + (frontend.Nodes.Any(static node => node.Operation == 83) ? 1 : 0)
                                + (frontend.Nodes.Any(static node => node.Operation == 84) ? 2 : 0);
                        }
                        if (frontend.Statements.Any(static statement => statement.Kind == 4))
                        {
                            projectionVersion = (frontend.Statements.Any(static statement => statement.Kind == 3)
                                    ? 36 : 32)
                                + (frontend.Nodes.Any(static node => node.Operation == 83) ? 1 : 0)
                                + (frontend.Nodes.Any(static node => node.Operation == 84) ? 2 : 0);
                        }
                        if (frontend.Statements.Any(static statement => statement.Kind is 6 or 7))
                        {
                            projectionVersion = (frontend.Statements.Any(static statement => statement.Kind == 4)
                                    ? 44 : 40)
                                + (frontend.Nodes.Any(static node => node.Operation == 83) ? 1 : 0)
                                + (frontend.Nodes.Any(static node => node.Operation == 84) ? 2 : 0);
                        }
                        if (frontend.Statements.Any(static statement => statement.Kind == 8))
                        {
                            projectionVersion = 48;
                        }
                        if (frontend.Nodes.Any(static node => node.Operation is >= 86 and <= 101))
                        {
                            projectionVersion = 49;
                        }
                        for (var index = 0; index < frontend.Statements.Length; index++)
                        {
                            if (frontend.Statements[index].Kind != 1)
                            {
                                continue;
                            }
                            for (var header = 0; header < index; header++)
                            {
                                if (frontend.Statements[header].Kind is 3 or 4 or 5
                                    && frontend.Statements[header].Slot > index)
                                {
                                    projectionVersion = 50;
                                    break;
                                }
                            }
                        }
                        if (frontend.Statements.Any(static statement => statement.Kind is 11 or 12))
                        {
                            projectionVersion = 51;
                        }
                        if (frontend.Statements.Any(static statement => statement.Kind == 14))
                        {
                            projectionVersion = 52;
                        }
                        if (frontend.Statements.Any(static statement => statement.Kind == 15))
                        {
                            projectionVersion = 53;
                        }
                        if (frontend.Statements.Any(static statement => statement.Kind is >= 16 and <= 20))
                        {
                            projectionVersion = 54;
                        }
                        if (frontend.Statements.Any(static statement => statement.Kind == 21))
                        {
                            projectionVersion = 55;
                        }
                        if (frontend.ReturnType == 0)
                        {
                            projectionVersion = frontend.IsExpressionBody ? 57 : 56;
                        }
                        writer.WriteNumber("projectionVersion", projectionVersion);
                        writer.WriteBoolean("expressionBody", frontend.IsExpressionBody);
                        writer.WriteNumber("conditionRoot", frontend.ConditionRoot);
                        writer.WriteNumber("trueRoot", frontend.TrueRoot);
                        writer.WriteNumber("falseRoot", frontend.FalseRoot);
                        writer.WriteStartArray("nodes");
                        foreach (var node in frontend.Nodes)
                        {
                            writer.WriteStartObject();
                            writer.WriteNumber("operation", node.Operation);
                            writer.WriteNumber("type", node.Type);
                            WriteFrontendSpan(writer, "span", node.Span);
                            writer.WriteNumber("left", node.Left);
                            writer.WriteNumber("right", node.Right);
                            writer.WriteNumber("low", node.Low);
                            writer.WriteNumber("high", node.High);
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                        if (!frontend.Statements.IsDefaultOrEmpty)
                        {
                            writer.WriteStartArray("locals");
                            foreach (var local in frontend.Locals)
                            {
                                writer.WriteStartObject();
                                writer.WriteNumber("type", local.Type);
                                writer.WriteBoolean("mutable", local.IsMutable);
                                WriteFrontendSpan(writer, "name", local.Name);
                                writer.WriteEndObject();
                            }
                            writer.WriteEndArray();
                            writer.WriteStartArray("statements");
                            foreach (var statement in frontend.Statements)
                            {
                                writer.WriteStartObject();
                                writer.WriteNumber("kind", statement.Kind);
                                writer.WriteNumber("slot", statement.Slot);
                                writer.WriteNumber("root", statement.Root);
                                WriteFrontendSpan(writer, "span", statement.Span);
                                writer.WriteEndObject();
                            }
                            writer.WriteEndArray();
                        }
                    }
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
                Console.Out.WriteLine(outputPath is null
                    ? $"Scalar function at [{frontend.Name.Start}..{frontend.Name.End}), {frontend.ParameterCount} parameters."
                    : $"{outputPath}: native scalar {(command == "frontend-build" ? "executable" : "library")}");
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
