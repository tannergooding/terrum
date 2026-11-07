using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class NativeLoweringAgreementTests
{
    private const string Source = "int32 Main() { return 42; }";

    [Test]
    public void ExactAgreementIsBoundAndRequiresExplicitVerification()
    {
        var agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source),
            new CompilationOptions { NativeLoweringAgreement = agreement });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
        Assert.That(compilation.NativeProfileVersion, Is.Null);
        var program = compilation.Program!;

        Assert.Throws<System.ArgumentException>(() => SemanticVerifier.Verify(program));
        Assert.DoesNotThrow(() => SemanticVerifier.Verify(program, agreement));
        Assert.Throws<System.ArgumentException>(() =>
            SemanticVerifier.Verify(program, agreement.ToUpperInvariant()));
        var code = NativeEmitter.EmitCode(program,
            NativeRuntimePolicy.FailFast, nativeLoweringAgreement: agreement);
        Assert.That(code.ManagedRuntimeOffset, Is.EqualTo(-1));
        Assert.That(code.NormalBackingRuntimeOffset, Is.Zero);
        Assert.That(code.Data.Length, Is.EqualTo(32));
        Assert.Throws<System.NotSupportedException>(() =>
            Compilation.GetNativeProfileVersion(program));
        Assert.Throws<System.ArgumentException>(() =>
            NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast));
    }

    [Test]
    public void NormalBackingAgreementChangesCheckedReferenceLayout()
    {
        const string Source = """
            using Terrum.CodeAnalysis;
            struct Holder {
                public mut ref<int32> Value;
                public int32 Tail;
                [this: Captures(nameof(value))]
                public Holder(ref<int32> value, int32 tail) {
                    Value = value;
                    Tail = tail;
                }
            }
            """;
        var syntax = SyntaxTree.Parse(Source);
        Assert.That(syntax.HasErrors, Is.False,
            string.Join(Environment.NewLine, syntax.Diagnostics));

        var defaultCompilation = Compilation.CreateLibrary(syntax);
        Assert.That(defaultCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, defaultCompilation.Diagnostics));
        var defaultProgram = defaultCompilation.Program!;
        Assert.That(defaultProgram.Structures.Single().Size, Is.EqualTo(16));
        Assert.That(defaultProgram.Structures.Single().Fields[1].Offset, Is.EqualTo(8));

        var agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var selectedCompilation = Compilation.CreateLibrary(syntax,
            new CompilationOptions { NativeLoweringAgreement = agreement });
        Assert.That(selectedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, selectedCompilation.Diagnostics));
        var selectedProgram = selectedCompilation.Program!;
        Assert.That(selectedProgram.Structures.Single().Size, Is.EqualTo(24));
        Assert.That(selectedProgram.Structures.Single().Fields[1].Offset, Is.EqualTo(16));
        Assert.DoesNotThrow(() => SemanticVerifier.VerifyLibrary(
            selectedProgram, agreement));
        Assert.Throws<System.ArgumentException>(() =>
            SemanticVerifier.VerifyLibrary(selectedProgram));

        var executable = Compilation.Create(SyntaxTree.Parse(
            Source + "\nint32 Main() { return 42; }\n"),
            new CompilationOptions { NativeLoweringAgreement = agreement });
        Assert.That(executable.HasErrors, Is.False,
            string.Join(Environment.NewLine, executable.Diagnostics));
        var executableCode = NativeEmitter.EmitCode(
            executable.Program!, NativeRuntimePolicy.FailFast, agreement);
        Assert.That(executableCode.NormalBackingRuntimeOffset, Is.Zero);
        Assert.That(executableCode.Data.Length, Is.EqualTo(32));
        Assert.That(NativeJit.ExecuteWithData(executable.Program!,
            NativeRuntimePolicy.FailFast, nativeLoweringAgreement: agreement).ExitCode,
            Is.EqualTo(42));
    }

    [Test]
    public void RootlessReferenceAgreementForwardsScalarNativeImportsThroughJit()
    {
        Assume.That(OperatingSystem.IsWindows()
            && System.Runtime.InteropServices.RuntimeInformation
                .ProcessArchitecture
                == System.Runtime.InteropServices.Architecture.X64);
        const string agreement
            = CompilationOptions.WindowsX64RootlessReferencesAgreement;
        var options = NativeCoreRegistrationTests
            .CreateRegisteredCompilationOptions() with {
                NativeLoweringAgreement = agreement,
            };
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("kernel32.dll", "GetCurrentThreadId")]
            extern unsafe uint32 CurrentThreadId();
            int32 Main() {
                unsafe {
                    if (CurrentThreadId() == uint32(0)) { return 0; }
                    return 42;
                }
            }
            """), options);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        Assert.That(NativeJit.Execute(program,
            NativeRuntimePolicy.FailFast,
            nativeLoweringAgreement: agreement), Is.EqualTo(42));
    }

    [Test]
    public void RootlessReferenceAgreementExecutesOptionalDescriptorValues()
    {
        const string agreement
            = CompilationOptions.WindowsX64RootlessReferencesAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int32 value = 41;
                mut ref<int32>? selected = ref value;
                if (selected is some present) { return *present; }
                return 0;
            }
            """), new CompilationOptions {
                NativeLoweringAgreement = agreement,
            });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        Assert.That(NativeJit.Execute(program,
            NativeRuntimePolicy.FailFast,
            nativeLoweringAgreement: agreement), Is.EqualTo(41));
    }

    [TestCase("")]
    [TestCase("terrum.experimental.windows-x64.Normal-backing")]
    [TestCase("terrum.experimental.windows-x64.Rootless-references")]
    [TestCase(" terrum.experimental.windows-x64.rootless-references")]
    [TestCase("terrum.experimental.windows-x64.other")]
    public void RejectsUnrecognizedAgreements(string agreement)
    {
        _ = Assert.Throws<System.ArgumentException>(() => Compilation.Create(
            SyntaxTree.Parse(Source),
            new CompilationOptions { NativeLoweringAgreement = agreement }));
    }
}
