// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StaticProtocolTests
{
    private static readonly string s_sample = File.ReadAllText(
        Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..")),
            "samples", "native", "static-protocol-quadratic.terrum"));

    [Fact]
    public async Task SourceDefinedNominalProtocolsSpecializeTwoConcreteWrappersAsync()
    {
        var tree = SyntaxTree.Parse(s_sample);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(s_sample, tree.Root.ToFullString());
        Assert.Contains(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.InterfaceDeclaration);
        Assert.Contains(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.GenericNameExpression);
        var compilation = Compilation.Create(tree);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(2, program.Functions.Count(function => function.Name.StartsWith("EvaluateQuadratic<",
            StringComparison.Ordinal)));
        Assert.DoesNotContain(program.Functions, function => function.Name == "EvaluateQuadratic");
        Assert.Equal(46, await RunAsync(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
    public async Task ConcreteWrapperPublishesWithoutGenericWitnessMetadataAsync()
    {
        var source = s_sample[..s_sample.IndexOf("int32 Main()", StringComparison.Ordinal)]
            + "public int32 Closed(int32 x) { return EvaluateQuadratic<Meters>("
            + "new Meters(x), new Meters(3), new Meters(4), new Meters(5)).Value; }";
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program), ["Closed"]);
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.Equal("Closed", Assert.Single(reference.Functions).Name);
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Closed(2); }"), reference);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(25, await RunAsync(NativeEmitter.Emit(
            ModuleCompiler.Link(consumer, producer), NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(": Addition<Meters, Meters, Meters>, Multiplication<Meters, Meters, Meters>", "", "TRM3007")]
    [InlineData("public static Meters operator *(Meters left, Meters right)", "private static Meters operator *(Meters left, Meters right)", "TRM3007")]
    [InlineData("operator *(Meters left, Meters right)", "NotMultiply(Meters left, Meters right)", "TRM3007")]
    [InlineData("operator *(Meters left, Meters right)", "operator *(Meters left, int32 right)", "TRM3007")]
    [InlineData("struct Meters", "unique struct Meters", "TRM3007")]
    [InlineData("Addition<T, T, T>, Multiplication<T, T, T>, [Copy]", "Addition<T, T, T>, [Copy]", "TRM3007")]
    [InlineData("T EvaluateQuadratic<T>", "public T EvaluateQuadratic<T>", "TRM3007")]
    [InlineData("Addition<Meters, Meters, Meters>", "Addition<Meters, Seconds, Meters>", "TRM3007")]
    [InlineData("Addition<Meters, Meters, Meters>", "Addition.NotAddition<Meters, Meters, Meters>", "TRM3007")]
    [InlineData("Addition<Meters, Meters, Meters>", "Addition<Meters, Meters, Meters>?", "TRM3007")]
    [InlineData("where T : Addition<T, T, T>", "where T : Unknown<T, T, T>", "TRM3007")]
    [InlineData("where T : Addition<T, T, T>", "where T : Addition.NotAddition<T, T, T>", "TRM3007")]
    [InlineData("where T : Addition<T, T, T>", "where T : Addition<T, T, T>?", "TRM3007")]
    [InlineData("T EvaluateQuadratic<T>", "[NoThrow] T EvaluateQuadratic<T>", "TRM3007")]
    [InlineData("T EvaluateQuadratic<T>", "T EvaluateQuadratic<T, T>", "TRM3007")]
    [InlineData("operator +(Meters left, Meters right)", "op_Addition(Meters left, Meters right)", "TRM3007")]
    [InlineData("static TResult operator +(TSelf left, TOther right)",
        "[NoThrow] [NoThrow] static TResult operator +(TSelf left, TOther right)", "TRM3001")]
    [InlineData("static TResult operator +(TSelf left, TOther right);",
        "static TResult operator +(TSelf left, TOther right); static TResult operator -(TSelf left, TOther right);",
        "TRM3001")]
    [InlineData("static TResult operator +(TSelf left, TOther right)", "static TOther operator +(TSelf left, TOther right)", "TRM3001")]
    [InlineData("=> (a * x + b) * x + c;", "=> EvaluateQuadratic<Meters>(x, a, b, c);", "TRM3007")]
    public void RejectsMissingOrFalseProtocolPromises(string before, string after, string expected)
    {
        Assert.True(s_sample.Contains(before, StringComparison.Ordinal));
        var compilation = Compilation.Create(SyntaxTree.Parse(s_sample.Replace(before, after, StringComparison.Ordinal)));
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == expected);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void TypedArgumentsInferTheSameClosedProtocolSpecialization()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(s_sample.Replace(
            "EvaluateQuadratic<Meters>(", "EvaluateQuadratic(", StringComparison.Ordinal)));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Contains(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            function => function.Name == "EvaluateQuadratic<Meters>");
    }

    [Fact]
    public void AmbiguousImportedTypeArgumentsAreRejected()
    {
        var prefix = s_sample[..s_sample.IndexOf("int32 Main()", StringComparison.Ordinal)];
        var compilation = Compilation.Create([
            SyntaxTree.Parse("namespace One; struct Value { public int32 Number; }"),
            SyntaxTree.Parse("namespace Two; struct Value { public int32 Number; }"),
            SyntaxTree.Parse("using One; using Two; " + prefix
                + "int32 Main() { EvaluateQuadratic<Value>(); return 0; }"),
        ]);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3011");
    }

    [Fact]
    public async Task QualifiedProtocolIdentitiesAndLongQualifiedTypeArgumentExecuteAsync()
    {
        const string Namespace = "A.B.C.D.E.F.G.H.I.J.K.L.M.N.O";
        var source = "namespace " + Namespace + ";\n"
            + s_sample.Replace(": Addition<Meters", ": " + Namespace + ".Addition<Meters",
                StringComparison.Ordinal)
                .Replace(": Addition<Seconds", ": " + Namespace + ".Addition<Seconds",
                    StringComparison.Ordinal)
                .Replace("where T : Addition<T", "where T : " + Namespace + ".Addition<T",
                    StringComparison.Ordinal)
                .Replace("EvaluateQuadratic<Meters>(", "EvaluateQuadratic<" + Namespace + ".Meters>(",
                    StringComparison.Ordinal)
                .Replace("EvaluateQuadratic<Seconds>(", "EvaluateQuadratic<" + Namespace + ".Seconds>(",
                    StringComparison.Ordinal);
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(source, tree.Root.ToFullString());
        var callStart = source.IndexOf("EvaluateQuadratic<" + Namespace + ".Meters>(", StringComparison.Ordinal)
            + "EvaluateQuadratic".Length;
        var callEnd = source.IndexOf('>', callStart, StringComparison.Ordinal) + 1;
        Assert.Equal(33, tree.Tokens.Count(token =>
            token.Span.Start >= callStart && token.Span.End <= callEnd));
        var compilation = Compilation.Create(tree);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(46, await RunAsync(NativeEmitter.Emit(
            Assert.IsType<BoundProgram>(compilation.Program), NativeRuntimePolicy.FailFast)).ConfigureAwait(true));
    }

    [Fact]
    public void AProtocolEffectPromiseRequiresAProvenImplementation()
    {
        var source = s_sample.Replace("static TResult operator +(TSelf left, TOther right)",
            "[NoThrow] static TResult operator +(TSelf left, TOther right)", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
    }

    [Fact]
    public void AFalseOperatorNoThrowPromiseIsRejectedByConcreteVerification()
    {
        var source = s_sample.Replace("public static Meters operator +(Meters left, Meters right)",
            "[NoThrow] public static Meters operator +(Meters left, Meters right)", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3017");
    }

    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    public void ClosedSpecializationsHaveAnExplicitBound(int instantiations)
    {
        var extra = new System.Text.StringBuilder();
        for (var i = 0; i < instantiations; i++)
        {
            var name = "Measure" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _ = extra.Append("struct ").Append(name).Append(" : Addition<").Append(name).Append(", ")
                .Append(name).Append(", ").Append(name).Append("> { public int32 Value; public ")
                .Append(name).Append("(int32 value) { Value = value; } public static ").Append(name)
                .Append(" operator +(").Append(name).Append(" left, ").Append(name)
                .Append(" right) { return new ").Append(name).Append("(left.Value + right.Value); } }");
        }
        _ = extra.Append("T Double<T>(T x) where T : Addition<T, T, T>, [Copy] { return x + x; } int32 Main() {");
        for (var i = 0; i < instantiations; i++)
        {
            var name = "Measure" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _ = extra.Append("Double<").Append(name).Append(">(new ").Append(name).Append("(1));");
        }
        _ = extra.Append("return 0; }");
        var source = s_sample[..s_sample.IndexOf("struct Meters", StringComparison.Ordinal)] + extra;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        if (instantiations == 32)
        {
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3016");
        }
    }

    [Theory]
    [InlineData("Example<int32>(2;")]
    [InlineData("Example<int32(2);")]
    [InlineData("Example<int32> (2;")]
    public void SyntaxRecoversAndPreservesMalformedStaticProtocolSource(string invocation)
    {
        var source = "interface Example<T, U, V> { static V operator +(T a, U b); } "
            + "int32 Main() { return " + invocation + " }";
        var tree = SyntaxTree.Parse(source);
        Assert.Equal(source, tree.Root.ToFullString());
        Assert.True(tree.HasErrors || Compilation.Create(tree).HasErrors);
    }

    [Fact]
    public void RepeatedUnclosedTypeArgumentsStayWithinParserNestingAndRetainSource()
    {
        var source = "int32 Main() { return " + string.Concat(Enumerable.Repeat("a<", 512)) + "a; }";
        var tree = SyntaxTree.Parse(source);
        Assert.Equal(source, tree.Root.ToFullString());
        Assert.True(tree.HasErrors || Compilation.Create(tree).HasErrors);
    }

    [Fact]
    public void ImpossibleGenericPunctuationDoesNotBecomeAnInvocation()
    {
        const string Source = "int32 Main() { return Call<,Meters>(value); }";
        var tree = SyntaxTree.Parse(Source);
        Assert.True(tree.HasErrors);
        Assert.Equal(Source, tree.Root.ToFullString());
        Assert.DoesNotContain(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.GenericNameExpression);
    }

    [Fact]
    public void RejectedTemplateReportsItsOwningSourceSnapshot()
    {
        var source = s_sample.Replace("=> (a * x + b) * x + c;", "=> a / x;",
            StringComparison.Ordinal);
        var tree = SyntaxTree.Parse(source, "quadratic.terrum");
        var compilation = Compilation.Create(tree);
        var diagnostic = Assert.Single(compilation.SourceDiagnostics,
            item => item.Diagnostic.Code == "TRM3007"
                && item.Diagnostic.Message.StartsWith("Generic function requires", StringComparison.Ordinal));
        Assert.Same(tree, diagnostic.SyntaxTree);
        Assert.Equal("T EvaluateQuadratic", source.AsSpan(diagnostic.Diagnostic.Span.Start,
            "T EvaluateQuadratic".Length).ToString());
    }

    internal static async Task<int> RunAsync(byte[] image)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var path = Path.Combine(Directory.GetCurrentDirectory(), $"terrum-protocol-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Native process did not start.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }
                throw;
            }
            return process.ExitCode;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
