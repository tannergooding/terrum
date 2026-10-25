// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class SourceArithmeticProfileTests
{
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void ReleaseProfileWrapsIntegerOperationsAtTheirDestinationWidth(BoundRuntimePolicy runtimePolicy)
    {
        const string source = """
            int32 Main() {
                mut int32 binaryAdd = int32.MaxValue;
                binaryAdd = binaryAdd + 1;
                mut int32 compoundAdd = int32.MaxValue;
                compoundAdd += 1;
                mut int32 increment = int32.MaxValue;
                increment++;
                mut int32 decrement = int32.MinValue;
                decrement--;
                int32 multiplication = int32.MaxValue * 2;
                int32 subtraction = int32.MinValue - 1;
                int32 negation = -int32.MinValue;
                mut int64 wideAdd = int64.MaxValue;
                wideAdd++;
                mut int64 wideSubtract = int64.MinValue;
                wideSubtract--;
                int64 wideMultiply = int64.MaxValue * 2;
                int64 wideNegation = -int64.MinValue;

                mut uint32 binaryUnsignedAdd = uint32.MaxValue;
                binaryUnsignedAdd = binaryUnsignedAdd + 1;
                mut uint32 compoundUnsignedSubtract = 0;
                compoundUnsignedSubtract -= 1;
                mut uint32 unsignedDecrement = 0;
                unsignedDecrement--;
                uint32 unsignedNegation = -uint32(1);
                uint32 zeroNegation = -uint32(0);
                mut uint64 wideUnsignedAdd = uint64.MaxValue;
                wideUnsignedAdd += 1;
                mut uint64 wideUnsignedDecrement = 0;
                wideUnsignedDecrement--;
                uint64 wideUnsignedNegation = -uint64(1);
                uint64 wideZeroNegation = -uint64(0);

                if (binaryAdd != int32.MinValue || compoundAdd != int32.MinValue
                    || increment != int32.MinValue || decrement != int32.MaxValue
                    || multiplication != -2 || subtraction != int32.MaxValue
                    || negation != int32.MinValue || wideAdd != int64.MinValue
                    || wideSubtract != int64.MaxValue || wideMultiply != -2
                    || wideNegation != int64.MinValue || binaryUnsignedAdd != 0
                    || compoundUnsignedSubtract != uint32.MaxValue
                    || unsignedDecrement != uint32.MaxValue
                    || unsignedNegation != uint32.MaxValue || zeroNegation != 0
                    || wideUnsignedAdd != 0 || wideUnsignedDecrement != uint64.MaxValue
                    || wideUnsignedNegation != uint64.MaxValue || wideZeroNegation != 0) {
                    return 0;
                }
                return 42;
            }
            """;
        var options = new CompilationOptions {
            SourceArithmeticProfile = SourceArithmeticProfile.Release,
            RuntimePolicy = runtimePolicy,
        };
        var compilation = Compilation.Create(SyntaxTree.Parse(source), options);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = GetProgram(compilation, runtimePolicy);
        var nativePolicy = runtimePolicy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        Assert.That(NativeJit.Execute(program, nativePolicy), Is.EqualTo(42));
    }

    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ReleaseProfileWrappingSurvivesAotEmissionAsync(BoundRuntimePolicy runtimePolicy)
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                int32 wrapped = int32.MaxValue + 1;
                mut int64 wide = int64.MaxValue;
                wide++;
                mut uint64 unsigned = 0;
                unsigned--;
                return wrapped == int32.MinValue && wide == int64.MinValue
                    && unsigned == uint64.MaxValue ? 42 : 0;
            }
            """), new CompilationOptions {
            SourceArithmeticProfile = SourceArithmeticProfile.Release,
            RuntimePolicy = runtimePolicy,
        });
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = GetProgram(compilation, runtimePolicy);
        var nativePolicy = runtimePolicy == BoundRuntimePolicy.FailFast
            ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
        var image = NativeEmitter.Emit(program, nativePolicy);
        var executable = Path.Combine(TestArtifactPaths.DirectoryPath, $"arithmetic-profile-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(executable, image).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(executable) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start native worker.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                await process.WaitForExitAsync().ConfigureAwait(true);
                throw;
            }
            Assert.That(process.ExitCode, Is.EqualTo(42));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(executable).ConfigureAwait(true);
        }
    }

    [Test]
    public void DevelopmentProfileKeepsCheckedFailuresIndependentOfFailureDelivery()
    {
        const string source = """
            int32 Main() {
                mut int32 failures = 0;
                uint32 zero = -uint32(0);
                if (zero != 0) { return 1; }
                unsafe {
                    try { mut int32 value = int32.MaxValue; value++; } catch { failures += 1; }
                    try { mut uint32 value = 0; value--; } catch { failures += 1; }
                    try { int64 value = -int64.MinValue; } catch { failures += 1; }
                    try { uint64 value = -1; } catch { failures += 1; }
                }
                return failures == 4 ? 42 : 0;
            }
            """;
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.CheckedExceptions,
        };
        var compilation = Compilation.Create(SyntaxTree.Parse(source), options);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(GetProgram(compilation, BoundRuntimePolicy.CheckedExceptions),
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }

    [Test]
    public void DevelopmentUnsignedNegationChecksNonzeroAndAcceptsZero()
    {
        const string source = """
            int32 Main() {
                uint32 zero = -uint32(0);
                if (zero != 0) { return 1; }
                unsafe {
                    try {
                        uint32 nonzero = -1;
                        if (nonzero != 0) { return 2; }
                    } catch {
                        return 42;
                    }
                }
                return 0;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.CheckedExceptions,
        });
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(GetProgram(compilation, BoundRuntimePolicy.CheckedExceptions),
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }

    [Test]
    public void RequiredConstEvalUsesTheResolvedArithmeticPolicy()
    {
        const string source = """
            [ConstEval] int32 Add(int32 left, int32 right) { return left + right; }
            int32 Main() {
                const int32 result = Add(int32.MaxValue, 1);
                return result == int32.MinValue ? 42 : 0;
            }
            """;
        var release = Compilation.Create(SyntaxTree.Parse(source), new CompilationOptions {
            SourceArithmeticProfile = SourceArithmeticProfile.Release,
        });
        Assert.That(release.HasErrors, Is.False, string.Join(Environment.NewLine, release.Diagnostics));
        Assert.That(NativeJit.Execute(GetProgram(release, BoundRuntimePolicy.FailFast),
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));

        var development = Compilation.Create(SyntaxTree.Parse(source), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.CheckedExceptions,
        });
        Assert.That(global::System.Linq.Enumerable.Any(development.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3024"
                && diagnostic.Message.Contains("checked arithmetic", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void DefaultOptionsSelectDevelopmentAndFailFast()
    {
        var options = default(CompilationOptions);
        Assert.That(options.SourceArithmeticProfile, Is.EqualTo(SourceArithmeticProfile.Development));
        Assert.That(options.RuntimePolicy, Is.EqualTo(BoundRuntimePolicy.FailFast));
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return 0; }"), new CompilationOptions());
        _ = GetProgram(compilation, BoundRuntimePolicy.FailFast);
        var defaultedLibrary = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[NoThrow] int32 Add(int32 left, int32 right) { return left + right; }"));
        Assert.That(defaultedLibrary.HasErrors, Is.True);
        var runtimeOnlyLibrary = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[NoThrow] int32 Add(int32 left, int32 right) { return left + right; }"),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(runtimeOnlyLibrary.HasErrors, Is.True);
    }

    [TestCase(SourceArithmeticProfile.Development, BoundRuntimePolicy.FailFast, true)]
    [TestCase(SourceArithmeticProfile.Development, BoundRuntimePolicy.CheckedExceptions, true)]
    [TestCase(SourceArithmeticProfile.Release, BoundRuntimePolicy.FailFast, false)]
    [TestCase(SourceArithmeticProfile.Release, BoundRuntimePolicy.CheckedExceptions, false)]
    public void NoThrowAnalysisUsesTheResolvedArithmeticPolicy(SourceArithmeticProfile profile,
        BoundRuntimePolicy runtimePolicy, bool expectError)
    {
        var source = SyntaxTree.Parse(
            """
            [NoThrow] int32 Add(int32 left, int32 right) { return left + right; }
            [NoThrow] int64 Multiply(int64 left, int64 right) { return left * right; }
            [NoThrow] uint64 Negate(uint64 value) { return -value; }
            """);
        var compilation = Compilation.CreateLibrary(source, new CompilationOptions {
            SourceArithmeticProfile = profile,
            RuntimePolicy = runtimePolicy,
        });
        Assert.That(compilation.HasErrors, Is.EqualTo(expectError),
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3017"), Is.EqualTo(expectError));
    }

    [Test]
    public void EveryExistingFactoryInputShapeAcceptsCompilationOptions()
    {
        var options = new CompilationOptions {
            SourceArithmeticProfile = SourceArithmeticProfile.Release,
            RuntimePolicy = BoundRuntimePolicy.CheckedExceptions,
        };
        var mainTree = SyntaxTree.Parse("int32 Main() { return 0; }");
        var libraryTree = SyntaxTree.Parse("public int32 Value() { return 1; }");
        var consumerLibraryTree = SyntaxTree.Parse("public int32 Client() { return Value(); }");
        var buffers = ImmutableArray.Create(new HostBuffer("Trace", PrimitiveType.Int32, [0], true));
        var provider = Compilation.CreateLibrary(libraryTree, options);
        var producer = ModuleCompiler.CreateProducer(GetProgram(provider, BoundRuntimePolicy.CheckedExceptions),
            ["Value"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var coreTree = SyntaxTree.Parse("""
            public struct int32 {
                private int32 value;
                public int32 Add(int32 other) { return value + other.value; }
            }
            """);

        var compilations = new[] {
            Compilation.Create(mainTree, options),
            Compilation.Create([mainTree], options),
            Compilation.CreateWithNativeImports(mainTree, options),
            Compilation.CreateWithNativeImports([mainTree], options),
            Compilation.Create(mainTree, reference, options),
            Compilation.Create([mainTree], reference, options),
            Compilation.Create(mainTree, buffers, options),
            Compilation.Create([mainTree], buffers, options),
            Compilation.CreateLibrary(libraryTree, options),
            Compilation.CreateLibrary([libraryTree], options),
            Compilation.CreateLibrary(consumerLibraryTree, reference, options),
            Compilation.CreateLibrary([consumerLibraryTree], reference, options),
            Compilation.CreateLibrary([libraryTree], buffers, options),
            Compilation.CreateCoreLibrary([coreTree], options),
        };

        foreach (var compilation in compilations)
        {
            _ = GetProgram(compilation, BoundRuntimePolicy.CheckedExceptions);
        }
    }

    [Test]
    public void InvalidOptionsAreRejectedBeforeSourceEnumeration()
    {
        var enumerated = false;
        var invalidProfile = new CompilationOptions {
            SourceArithmeticProfile = (SourceArithmeticProfile)byte.MaxValue,
        };
        var profileException = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Compilation.Create(GetTrees(), invalidProfile))!;
        Assert.That(profileException.ParamName, Is.EqualTo("options"));
        Assert.That(enumerated, Is.False);

        var invalidRuntime = new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)byte.MaxValue,
        };
        var runtimeException = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Compilation.Create(GetTrees(), invalidRuntime))!;
        Assert.That(runtimeException.ParamName, Is.EqualTo("options"));
        Assert.That(enumerated, Is.False);

        IEnumerable<SyntaxTree> GetTrees()
        {
            enumerated = true;
            yield return SyntaxTree.Parse("int32 Main() { return 0; }");
        }
    }

    private static BoundProgram GetProgram(Compilation compilation, BoundRuntimePolicy runtimePolicy)
    {
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program;
        Assert.That(program, Is.Not.Null);
        Assert.That(program!.RuntimePolicy, Is.EqualTo(runtimePolicy));
        return program;
    }
}
