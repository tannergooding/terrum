// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.Tools;

internal static partial class Program
{
    // This is the worker entry point. Its caller must impose a subprocess deadline: fail-fast
    // machine code can terminate the worker via ExitProcess, and user code can loop indefinitely.
    private static int RunJit(string[] args, bool json)
    {
        if (args.Length is not (4 or 5) || args[0] != "jit" || args[1] == "--json"
            || args[2] != "--runtime" || args[3] != "fail-fast"
            || (args.Length == 5 && args[4] != "--json"))
        {
            WriteInputError(json, "TRM2000", "Usage: terrumc jit <path|-> --runtime fail-fast [--json]");
            return 2;
        }

        string text;
        try
        {
            text = ReadSource(args[1]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException or ArgumentException or NotSupportedException)
        {
            WriteInputError(json, "TRM2001", exception.Message);
            return 2;
        }

        var tree = SyntaxTree.Parse(text, args[1] == "-" ? "<stdin>" : args[1]);
        var compilation = Compilation.Create(tree);
        var diagnostics = compilation.Diagnostics;
        if (diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            if (json)
            {
                WriteCompilationJson("jit", tree, diagnostics, false, null, null);
            }
            else
            {
                foreach (var diagnostic in diagnostics)
                {
                    var position = tree.Source.GetLinePosition(diagnostic.Span.Start);
                    Console.Error.WriteLine(FormattableString.Invariant($"{tree.Source.Path}({position.Line + 1},{position.Character + 1}): {GetSeverityText(diagnostic.Severity)} {diagnostic.Code}: {diagnostic.Message}"));
                }
            }
            return 1;
        }

        if (compilation.Program is not BoundProgram program)
        {
            throw new InvalidOperationException("A successful compilation must contain a bound program.");
        }

        // --json describes compiler errors only; success returns Main's raw process exit status.
        // In particular, it cannot promise a JSON success envelope after ExitProcess.
        try
        {
            return NativeJit.Execute(program, NativeRuntimePolicy.FailFast);
        }
        catch (NotSupportedException exception)
        {
            diagnostics = diagnostics.Add(new Diagnostic("TRM4001", DiagnosticSeverity.Error, exception.Message, tree.Root.Span));
            if (json)
            {
                WriteCompilationJson("jit", tree, diagnostics, false, null, null);
            }
            else
            {
                var position = tree.Source.GetLinePosition(tree.Root.Span.Start);
                Console.Error.WriteLine(FormattableString.Invariant($"{tree.Source.Path}({position.Line + 1},{position.Character + 1}): error TRM4001: {exception.Message}"));
            }
            return 1;
        }
    }
}
