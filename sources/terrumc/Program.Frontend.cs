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
            "CharacterLiteral", "ScalarFrontend", "ScalarFrontendProgram", "ScalarFrontendProjection"];
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
            var programBytes = native.Data.AsSpan(native.BufferOffsets[ScalarFrontendHost.ProgramBufferIndex],
                ScalarFrontendHost.ProgramWordCount * sizeof(int));
            var read = ScalarFrontendHost.ReadProgram(source, bytes, programBytes, (BoundRuntimePolicy)policy);
            var result = read.Projection;
            if ((build || runExecutable) && result.IsValid)
            {
                var functions = result.Functions.IsDefault ? ImmutableArray.Create(result) : result.Functions;
                var exports = functions.Where(static function => function.IsPublic)
                    .Select(function => IdentifierName.GetName(source.Text.Substring(function.Name.Start, function.Name.Length)))
                    .ToImmutableArray();
                var entryIndex = -1;
                for (var i = 0; i < functions.Length; i++)
                {
                    if (IdentifierName.GetName(source.Text.Substring(functions[i].Name.Start,
                        functions[i].Name.Length)) == "Main")
                    {
                        entryIndex = i;
                    }
                }
                if (buildLibrary && exports.IsEmpty)
                {
                    WriteInputError(json, "TRM4002", "A native frontend library export must be declared public.");
                    return 1;
                }
                if ((buildExecutable || runExecutable) && (entryIndex < 0
                    || functions[entryIndex].ParameterCount != 0 || functions[entryIndex].ReturnType is not (0 or 1)))
                {
                    WriteInputError(json, "TRM4002", "A native frontend entry requires int32 or void Main() without parameters.");
                    return 1;
                }
                var library = read.Program ?? ScalarFrontendHost.BindLibrary(source, bytes, programBytes,
                    (BoundRuntimePolicy)policy);
                if (runExecutable)
                {
                    try
                    {
                        return NativeJit.Execute(library with { EntryPoint = entryIndex }, policy,
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
                        ? NativeEmitter.Emit(library with { EntryPoint = entryIndex }, policy)
                        : NativeEmitter.EmitLibrary(library, exports, policy);
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
                writer.WriteBoolean("noThrow", frontend.IsNoThrow);
                writer.WriteBoolean("doesNotReturn", frontend.DoesNotReturn);
                writer.WriteNumber("errorCode", frontend.ErrorCode);
                WriteFrontendSpan(writer, "errorSpan", frontend.ErrorSpan);
                if (!frontend.Functions.IsDefault)
                {
                    writer.WriteStartArray("functions");
                    for (var functionIndex = 0; functionIndex < frontend.Functions.Length; functionIndex++)
                    {
                        var function = frontend.Functions[functionIndex];
                        writer.WriteStartObject();
                        writer.WriteNumber("index", functionIndex);
                        writer.WriteBoolean("public", function.IsPublic);
                        writer.WriteBoolean("noThrow", function.IsNoThrow);
                        writer.WriteNumber("returnType", function.ReturnType);
                        writer.WriteNumber("parameterCount", function.ParameterCount);
                        writer.WriteNumber("outParameterMask", function.OutParameterMask);
                        writer.WriteNumber("initRefParameterMask", function.InitRefParameterMask);
                        writer.WriteNumber("firstType", function.FirstType);
                        writer.WriteNumber("secondType", function.SecondType);
                        writer.WriteNumber("thirdType", function.ThirdType);
                        WriteFrontendSpan(writer, "name", function.Name);
                        WriteFrontendSpan(writer, "firstName", function.FirstName);
                        WriteFrontendSpan(writer, "secondName", function.SecondName);
                        WriteFrontendSpan(writer, "thirdName", function.ThirdName);
                        writer.WriteBoolean("expressionBody", function.IsExpressionBody);
                        writer.WriteNumber("conditionRoot", function.ConditionRoot);
                        writer.WriteNumber("trueRoot", function.TrueRoot);
                        writer.WriteNumber("falseRoot", function.FalseRoot);
                        writer.WriteStartArray("nodes");
                        for (var nodeIndex = 0; nodeIndex < function.Nodes.Length; nodeIndex++)
                        {
                            var node = function.Nodes[nodeIndex];
                            writer.WriteStartObject();
                            writer.WriteNumber("operation", node.Operation);
                            writer.WriteNumber("type", node.Type);
                            WriteFrontendSpan(writer, "span", node.Span);
                            writer.WriteNumber("left", node.Left);
                            writer.WriteNumber("right", node.Right);
                            writer.WriteNumber("low", node.Low);
                            writer.WriteNumber("high", node.High);
                            if (node.Operation == 105)
                            {
                                writer.WriteNumber("function", function.CallTargets[nodeIndex]);
                            }
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                        writer.WriteStartArray("locals");
                        foreach (var local in function.Locals)
                        {
                            writer.WriteStartObject();
                            writer.WriteNumber("type", local.Type);
                            writer.WriteBoolean("mutable", local.IsMutable);
                            writer.WriteBoolean("initialization", local.IsInitialization);
                            writer.WriteBoolean("constant", local.IsConstant);
                            WriteFrontendSpan(writer, "name", local.Name);
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                        writer.WriteStartArray("statements");
                        foreach (var statement in function.Statements)
                        {
                            writer.WriteStartObject();
                            writer.WriteNumber("kind", statement.Kind);
                            writer.WriteNumber("slot", statement.Slot);
                            writer.WriteNumber("root", statement.Root);
                            WriteFrontendSpan(writer, "span", statement.Span);
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
                if (frontend.IsValid)
                {
                    if (frontend.Functions.IsDefault)
                    {
                        writer.WriteNumber("outParameterMask", frontend.OutParameterMask);
                        writer.WriteNumber("initRefParameterMask", frontend.InitRefParameterMask);
                    }
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
                        writer.WriteNumber("projectionVersion", 85);
                        writer.WriteBoolean("expressionBody", frontend.IsExpressionBody);
                        writer.WriteNumber("conditionRoot", frontend.ConditionRoot);
                        writer.WriteNumber("trueRoot", frontend.TrueRoot);
                        writer.WriteNumber("falseRoot", frontend.FalseRoot);
                        writer.WriteStartArray("nodes");
                        for (var nodeIndex = 0; nodeIndex < frontend.Nodes.Length; nodeIndex++)
                        {
                            var node = frontend.Nodes[nodeIndex];
                            writer.WriteStartObject();
                            writer.WriteNumber("operation", node.Operation);
                            writer.WriteNumber("type", node.Type);
                            WriteFrontendSpan(writer, "span", node.Span);
                            writer.WriteNumber("left", node.Left);
                            writer.WriteNumber("right", node.Right);
                            writer.WriteNumber("low", node.Low);
                            writer.WriteNumber("high", node.High);
                            if (node.Operation == 105)
                            {
                                writer.WriteNumber("function", frontend.CallTargets[nodeIndex]);
                            }
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
                    ? frontend.Functions.IsDefault
                        ? $"Scalar function at [{frontend.Name.Start}..{frontend.Name.End}), {frontend.ParameterCount} parameters."
                        : $"Scalar program with {frontend.Functions.Length} functions (projection 74)."
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
