// Copyright (c) Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private const string WindowLifetimeFixtureKey = NativeWindowLifetimeContract.ImportKey;
    private static readonly string WindowLifetimeDeclarations = $$"""
        [Terrum.Interop.NativeImport("{{WindowLifetimeFixtureKey}}")]
        [NoThrow]
        extern int32 NativeFfiWindowLifetime(Terrum.Interop.Windows.WNDPROC callback);

        [Terrum.Interop.NativeImport("{{ImportKey}}")]
        [NoThrow]
        extern uint32 CurrentThreadId();
        """;
    private const string WindowLifetimeCallback = """
        struct Host {
            int32 Value;

            [NoThrow]
            public static nint WindowProcedure(Terrum.Interop.Windows.HWND window, uint32 message,
                nuint wParam, nint lParam) {
                if (window == Terrum.Interop.Windows.HWND.NoOwner || wParam != 0) {
                    return -1;
                }
                if (message == 129) {
                    return lParam != 0 ? 1 : -1;
                }
                if (message == 1) {
                    return lParam != 0 ? 0 : -1;
                }
                if (message == 15) {
                    return lParam == 0 ? (4294967296 | int64(CurrentThreadId())) : -1;
                }
                if (message == 2) {
                    return lParam == 0 ? (8589934592 | int64(CurrentThreadId())) : -1;
                }
                if (message == 130) {
                    return lParam == 0 ? (12884901888 | int64(CurrentThreadId())) : -1;
                }
                return 0;
            }
        }
        """;

    private static NativeImportDefinition CreateWindowLifetimeImportDefinition() =>
        new(WindowLifetimeFixtureKey, NativeWindowLifetimeContract.DllName, NativeWindowLifetimeContract.ExportName,
            [PrimitiveType.NativeCallbackAddress], PrimitiveType.Int32, true, true, false,
            NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [NativeImportParameterContract.NativeCallback],
            CallbackParameters = [
                new(0, NativeWindowProcedureContract.TypeName,
                    NativeWindowProcedureContract.CallbackSignature,
                    NativeCallbackLifetime.RetainedUntilCallReturns),
            ],
        };

    private static NativeImportCatalog CreateWindowLifetimeCatalog() =>
        NativeImportCatalog.CreateForTesting("test-native-window-lifetime-v2", [
            CreateWindowLifetimeImportDefinition(),
            new NativeImportDefinition(ImportKey, "kernel32.dll", "GetCurrentThreadId", [],
                PrimitiveType.UInt32, true, true, false, NativeImportDeploymentPolicy.System32),
        ]);

    private static Compilation CompileWindowLifetime(string? callback = null, string? main = null,
        string? declarations = null, BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast) =>
        Compilation.CreateWithNativeImports([SyntaxTree.Parse((declarations ?? WindowLifetimeDeclarations)
            + "\n" + (callback ?? WindowLifetimeCallback) + "\nint32 Main() {"
            + (main ?? "return NativeFfiWindowLifetime(callback: &Host.WindowProcedure);") + "}")],
            CreateWindowLifetimeCatalog(), policy);

    [Test]
    public void WindowLifetimeUsesOnlyTheExactStaticNoThrowCallbackArgument()
    {
        var compilation = CompileWindowLifetime();
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue1 = global::System.Linq.Enumerable.ToArray(main.Body.Statements);
        Assert.That(__nunitAssertValue1, Has.Exactly(1).Items);
        var __nunitAssertValue2 = __nunitAssertValue1[0];
        Assert.That(__nunitAssertValue2, Is.TypeOf<BoundReturn>());
        var __nunitAssertValue3 = (((BoundReturn)__nunitAssertValue2)).Value;
        Assert.That(__nunitAssertValue3, Is.TypeOf<BoundCall>());
        var call = ((BoundCall)__nunitAssertValue3);
        var __nunitAssertValue4 = global::System.Linq.Enumerable.ToArray(call.Arguments);
        Assert.That(__nunitAssertValue4, Has.Exactly(1).Items);
        var __nunitAssertValue5 = __nunitAssertValue4[0];
        Assert.That(__nunitAssertValue5, Is.TypeOf<BoundNativeCallbackAddress>());
        var address = ((BoundNativeCallbackAddress)__nunitAssertValue5);
        var callback = program.Functions[address.Function];
        Assert.That(callback.Name, Is.EqualTo("Host.WindowProcedure"));
        Assert.That(callback.IsStatic && callback.IsNoThrow && !callback.IsExternal, Is.True);
        Assert.That(callback.ReturnType, Is.EqualTo(PrimitiveType.Int64));
        Assert.That(callback.Variables.Take(callback.ParameterCount).Select(static variable => variable.Type), Is.EqualTo(new[] {
            PrimitiveType.NativeWindowHandle, PrimitiveType.UInt32, PrimitiveType.UInt64, PrimitiveType.Int64,
        }));
        Assert.That(program.Functions[call.Function].NativeImportKey, Is.EqualTo(WindowLifetimeFixtureKey));
        var __nunitAssertValue6 = global::System.Linq.Enumerable.ToArray(CreateWindowLifetimeImportDefinition().ParameterContracts);
        Assert.That(__nunitAssertValue6, Has.Exactly(1).Items);
        Assert.That(__nunitAssertValue6[0], Is.EqualTo(NativeImportParameterContract.NativeCallback));
        var image = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
        Assert.That(image.AsSpan().IndexOf("NativeFfiWindowLifetime\0"u8) >= 0, Is.True);
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast));
        Assert.That(Compilation.Create(SyntaxTree.Parse(WindowLifetimeDeclarations + WindowLifetimeCallback
            + "int32 Main() { return NativeFfiWindowLifetime(&Host.WindowProcedure); }")).Program, Is.Null);
        Assert.That(Compilation.CreateWithNativeImports(SyntaxTree.Parse(WindowLifetimeDeclarations
            + WindowLifetimeCallback + "int32 Main() { return NativeFfiWindowLifetime(&Host.WindowProcedure); }")).Program, Is.Null);
        Assert.That(CompileWindowLifetime(policy: BoundRuntimePolicy.CheckedExceptions).Program, Is.Null);
    }

    [TestCase("public static nint", "public static int32")]
    [TestCase("public static nint", "public nint")]
    [TestCase("[NoThrow]", "")]
    [TestCase("Terrum.Interop.Windows.HWND window", "nint window")]
    [TestCase("uint32 message", "int32 message")]
    [TestCase("nuint wParam", "uint32 wParam")]
    [TestCase("nint lParam", "nuint lParam")]
    [TestCase("nuint wParam", "mut nuint wParam")]
    [TestCase("nint lParam", "out nint lParam")]
    [TestCase("return 0;", "return 1 / lParam;")]
    public void WindowLifetimeRejectsInexactInstanceOrUnprovedNoThrowTargets(string before, string after)
    {
        Assert.That(WindowLifetimeCallback.Contains(before, StringComparison.Ordinal), Is.True);
        var compilation = CompileWindowLifetime(WindowLifetimeCallback.Replace(before, after, StringComparison.Ordinal));
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("Terrum.Interop.Windows.WNDPROC saved = &Host.WindowProcedure; return 0;")]
    [TestCase("var saved = &Host.WindowProcedure; return 0;")]
    [TestCase("&Host.WindowProcedure; return 0;")]
    [TestCase("return &Host.WindowProcedure;")]
    [TestCase("return NativeFfiWindowLifetime(Host.WindowProcedure);")]
    [TestCase("return NativeFfiWindowLifetime(null);")]
    [TestCase("return NativeFfiWindowLifetime(0);")]
    [TestCase("return NativeFfiWindowLifetime(true ? &Host.WindowProcedure : &Host.WindowProcedure);")]
    [TestCase("return Forward(&Host.WindowProcedure);")]
    public void WindowLifetimeRejectsAddressStorageEscapeAndForwarding(string main)
    {
        var declarations = WindowLifetimeDeclarations
            + "\nint32 Forward(Terrum.Interop.Windows.WNDPROC callback) { return NativeFfiWindowLifetime(callback); }";
        var compilation = CompileWindowLifetime(main: main, declarations:
            main.Contains("Forward", StringComparison.Ordinal) ? declarations : WindowLifetimeDeclarations);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("extern int32", "extern int64")]
    [TestCase("WNDPROC callback", "WNDPROC callback, uint32 retained")]
    [TestCase("Terrum.Interop.Windows.WNDPROC callback", "mut Terrum.Interop.Windows.WNDPROC callback")]
    [TestCase("Terrum.Interop.Windows.WNDPROC callback", "out Terrum.Interop.Windows.WNDPROC callback")]
    [TestCase("Terrum.Interop.Windows.WNDPROC callback", "init Terrum.Interop.Windows.WNDPROC callback")]
    [TestCase("[NoThrow]", "")]
    public void WindowLifetimeImportRequiresTheExactCataloguedDeclaration(string before, string after)
    {
        Assert.That(WindowLifetimeDeclarations.Contains(before, StringComparison.Ordinal), Is.True);
        var compilation = CompileWindowLifetime(declarations:
            WindowLifetimeDeclarations.Replace(before, after, StringComparison.Ordinal));
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void WindowLifetimeCatalogRequiresItsDistinctTestOnlyContract()
    {
        var definition = CreateWindowLifetimeImportDefinition();
        Assert.That(NativeCallbackParameterContract.IsValid(definition), Is.True);
        Assert.That(NativeWindowLifetimeContract.MatchesImport(definition), Is.True);
        Assert.That(NativeCallbackParameterContract.TryGetParameter(definition, -1, out _), Is.False);
        Assert.That(NativeCallbackParameterContract.TryGetParameter(definition, 1, out _), Is.False);
        foreach (var invalid in new[] {
            definition with { ParameterContracts = [] },
            definition with { ParameterContracts = [NativeImportParameterContract.Value] },
            definition with { ParameterContracts = [NativeImportParameterContract.NoOwnerWindowHandle] },
            definition with {
                CallbackParameters = [
                    new(0, NativeWindowProcedureContract.TypeName,
                        NativeWindowProcedureContract.CallbackSignature,
                        NativeCallbackLifetime.SynchronousNoEscape),
                ],
            },
        })
        {
            _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
                "test-invalid-window-lifetime", [invalid]));
        }
        _ = Assert.Throws<ArgumentException>(() => new NativeImportCatalog("not-test-only-window-lifetime",
            [definition]));
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test-no-retention-cannot-use-lifetime-contract", [CreateWindowProcedureImportDefinition() with {
                CallbackParameters = [
                    new(0, NativeWindowProcedureContract.TypeName,
                        NativeWindowProcedureContract.CallbackSignature,
                        NativeCallbackLifetime.RetainedUntilCallReturns),
                ],
            }]));
        var __nunitAssertValue7 = global::System.Linq.Enumerable.ToArray(CreateWindowProcedureImportDefinition().ParameterContracts);
        Assert.That(__nunitAssertValue7, Has.Exactly(1).Items);
        Assert.That(__nunitAssertValue7[0], Is.EqualTo(NativeImportParameterContract.NativeCallback));
        Assert.That(NativeImportCatalog.WindowsX64.TryGetImport(CallWindowProcImportKey, out var systemImport), Is.True);
        var __nunitAssertValue8 = systemImport;
        Assert.That(__nunitAssertValue8, Is.TypeOf<NativeImportDefinition>());
        Assert.That(((NativeImportDefinition)__nunitAssertValue8).ParameterContracts[0], Is.EqualTo(NativeImportParameterContract.NativeCallback));
    }

    [TestCase(NativeWindowLifetimeContract.DllName)]
    [TestCase("NATIVE-WINDOW-LIFETIME-PROBE.DLL")]
    public void WindowLifetimeCatalogRejectsAliasedIdentityWithTheOldNoRetentionContract(string dllName)
    {
        var alias = CreateWindowLifetimeImportDefinition() with {
            Key = "test.aliased-window-lifetime",
            DllName = dllName,
            CallbackParameters = [
                new(0, NativeWindowProcedureContract.TypeName,
                    NativeWindowProcedureContract.CallbackSignature,
                    NativeCallbackLifetime.SynchronousNoEscape),
            ],
        };
        Assert.That(NativeWindowLifetimeContract.MatchesIdentity(alias), Is.True);
        Assert.That(NativeWindowLifetimeContract.MatchesImport(alias), Is.False);
        Assert.That(NativeCallbackParameterContract.IsValid(alias), Is.True);
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test-aliased-window-lifetime", [alias]));
        _ = Assert.Throws<ArgumentException>(() => new NativeImportCatalog(
            "production-aliased-window-lifetime", [alias]));
    }

    [TestCase("key")]
    [TestCase("dll")]
    [TestCase("export")]
    [TestCase("return")]
    [TestCase("parameter")]
    [TestCase("parameter-extra")]
    [TestCase("parameters-default")]
    [TestCase("contracts-default")]
    [TestCase("contracts-extra")]
    [TestCase("throwing")]
    [TestCase("unsafe")]
    [TestCase("does-not-return")]
    [TestCase("deployment")]
    public void WindowLifetimeCatalogRejectsAlteredLifecycleEntries(string variant)
    {
        var definition = CreateWindowLifetimeImportDefinition();
        var invalid = variant switch {
            "key" => definition with { Key = "test.unreviewed-lifecycle" },
            "dll" => definition with { DllName = "other.dll" },
            "export" => definition with { ExportName = "OtherLifecycle" },
            "return" => definition with { ReturnType = PrimitiveType.Int64 },
            "parameter" => definition with { ParameterTypes = [PrimitiveType.NativeWindowHandle] },
            "parameter-extra" => definition with {
                ParameterTypes = [PrimitiveType.NativeCallbackAddress, PrimitiveType.UInt32],
                ParameterContracts = [
                    NativeImportParameterContract.NativeCallback, NativeImportParameterContract.Value,
                ],
            },
            "parameters-default" => definition with { ParameterTypes = default },
            "contracts-default" => definition with { ParameterContracts = default },
            "contracts-extra" => definition with {
                ParameterContracts = [
                NativeImportParameterContract.NativeCallback, NativeImportParameterContract.Value,
            ]
            },
            "throwing" => definition with { IsNoThrow = false },
            "unsafe" => definition with { RequiresUnsafeCaller = true },
            "does-not-return" => definition with { DoesNotReturn = true },
            "deployment" => definition with { DeploymentPolicy = NativeImportDeploymentPolicy.System32 },
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test-invalid-lifecycle-entry", [invalid]));
        Assert.That(NativeWindowLifetimeContract.MatchesImport(invalid), Is.False);
    }

    [TestCase("callback-negative")]
    [TestCase("callback-outside-program")]
    [TestCase("callback-import")]
    [TestCase("callback-not-static")]
    [TestCase("callback-not-nothrow")]
    [TestCase("callback-unsafe")]
    [TestCase("callback-hwnd-type")]
    [TestCase("callback-parameter-count")]
    [TestCase("callback-return-type")]
    [TestCase("callback-message-type")]
    [TestCase("callback-wparam-type")]
    [TestCase("callback-lparam-type")]
    [TestCase("callback-mutable-parameter")]
    [TestCase("callback-out-parameter")]
    [TestCase("callback-init-parameter")]
    [TestCase("callback-unproved-nothrow")]
    [TestCase("callback-address-type")]
    [TestCase("callback-constant")]
    [TestCase("callback-outside-call")]
    [TestCase("callback-local")]
    [TestCase("callback-return")]
    [TestCase("callback-conditional")]
    [TestCase("callback-forwarded")]
    [TestCase("import-return-type")]
    [TestCase("import-without-key")]
    [TestCase("catalog-removed")]
    [TestCase("catalog-production")]
    [TestCase("checked-policy")]
    public void WindowLifetimeMandatoryVerificationRejectsForgedCallbacksAndEscape(string variant)
    {
        var compilation = CompileWindowLifetime();
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue9 = compilation.Program;
        Assert.That(__nunitAssertValue9, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue9);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue10 = global::System.Linq.Enumerable.ToArray(main.Body.Statements);
        Assert.That(__nunitAssertValue10, Has.Exactly(1).Items);
        var __nunitAssertValue11 = __nunitAssertValue10[0];
        Assert.That(__nunitAssertValue11, Is.TypeOf<BoundReturn>());
        var result = ((BoundReturn)__nunitAssertValue11);
        var __nunitAssertValue12 = result.Value;
        Assert.That(__nunitAssertValue12, Is.TypeOf<BoundCall>());
        var call = ((BoundCall)__nunitAssertValue12);
        var __nunitAssertValue13 = global::System.Linq.Enumerable.ToArray(call.Arguments);
        Assert.That(__nunitAssertValue13, Has.Exactly(1).Items);
        var __nunitAssertValue14 = __nunitAssertValue13[0];
        Assert.That(__nunitAssertValue14, Is.TypeOf<BoundNativeCallbackAddress>());
        var address = ((BoundNativeCallbackAddress)__nunitAssertValue14);
        var callback = program.Functions[address.Function];
        BoundExpression argument = address;
        switch (variant)
        {
            case "callback-negative" or "callback-outside-program" or "callback-import":
                argument = address with {
                    Function = variant switch {
                        "callback-negative" => -1,
                        "callback-outside-program" => program.Functions.Length,
                        _ => call.Function,
                    }
                };
                break;
            case "callback-not-static":
                callback = callback with { IsStatic = false };
                break;
            case "callback-not-nothrow":
                callback = callback with { IsNoThrow = false };
                break;
            case "callback-unsafe":
                callback = callback with { RequiresUnsafeCaller = true };
                break;
            case "callback-hwnd-type":
                callback = callback with {
                    Variables = callback.Variables.SetItem(0,
                    callback.Variables[0] with { Type = PrimitiveType.Int64 })
                };
                break;
            case "callback-parameter-count":
                callback = callback with { ParameterCount = 3 };
                break;
            case "callback-return-type":
                callback = callback with { ReturnType = PrimitiveType.Int32 };
                break;
            case "callback-message-type" or "callback-wparam-type" or "callback-lparam-type":
                var parameter = variant == "callback-message-type" ? 1 : variant == "callback-wparam-type" ? 2 : 3;
                callback = callback with {
                    Variables = callback.Variables.SetItem(parameter,
                    callback.Variables[parameter] with { Type = PrimitiveType.Int32 })
                };
                break;
            case "callback-mutable-parameter" or "callback-out-parameter" or "callback-init-parameter":
                callback = callback with {
                    Variables = callback.Variables.SetItem(2,
                    callback.Variables[2] with {
                        IsMutable = variant == "callback-mutable-parameter",
                        IsOut = variant == "callback-out-parameter",
                        IsInit = variant == "callback-init-parameter",
                    })
                };
                break;
            case "callback-unproved-nothrow":
                callback = callback with {
                    Body = callback.Body with {
                        Statements = [
                    new BoundRequire(new BoundConstant(0, PrimitiveType.Bool, callback.Span), callback.Span),
                    new BoundReturn(new BoundConstant(0, PrimitiveType.Int64, callback.Span), callback.Span),
                ]
                    }
                };
                break;
            case "callback-address-type":
                argument = address with { Type = PrimitiveType.Int64 };
                break;
            case "callback-constant":
                argument = new BoundConstant(0, PrimitiveType.NativeCallbackAddress, address.Span);
                break;
            case "callback-outside-call":
                main = main with {
                    Body = main.Body with {
                        Statements = [
                    new BoundExpressionStatement(address, address.Span), result,
                ]
                    }
                };
                break;
            case "callback-local":
                main = main with {
                    Variables = [new BoundVariable("saved", PrimitiveType.NativeCallbackAddress, false, address.Span)],
                    Body = main.Body with {
                        Statements = [
                        new BoundLocal(0, address, address.Span), result,
                    ]
                    },
                };
                break;
            case "callback-return":
                main = main with {
                    ReturnType = PrimitiveType.NativeCallbackAddress,
                    Body = main.Body with { Statements = [result with { Value = address }] },
                };
                break;
            case "callback-conditional":
                argument = new BoundConditional(new BoundConstant(1, PrimitiveType.Bool, address.Span),
                    address, address, PrimitiveType.NativeCallbackAddress, address.Span);
                break;
            case "callback-forwarded":
                var forwarded = program.Functions[call.Function] with {
                    IsExternal = false,
                    NativeImportKey = null,
                    IsNoThrow = false,
                    Body = new BoundBlock([new BoundReturn(new BoundConstant(0, PrimitiveType.Int32,
                        call.Span), call.Span)], call.Span),
                };
                program = program with { Functions = program.Functions.SetItem(call.Function, forwarded) };
                break;
            case "import-return-type":
                program = program with {
                    Functions = program.Functions.SetItem(call.Function,
                    program.Functions[call.Function] with { ReturnType = PrimitiveType.Int64 })
                };
                break;
            case "import-without-key":
                program = program with {
                    Functions = program.Functions.SetItem(call.Function,
                    program.Functions[call.Function] with { NativeImportKey = null })
                };
                break;
            case "catalog-removed":
                program = program with { NativeImportCatalog = null };
                break;
            case "catalog-production":
                program = program with { NativeImportCatalog = NativeImportCatalog.WindowsX64 };
                break;
            case "checked-policy":
                program = program with { RuntimePolicy = BoundRuntimePolicy.CheckedExceptions };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(variant));
        }
        if (variant is not ("callback-outside-call" or "callback-local" or "callback-return"))
        {
            main = main with {
                Body = main.Body with {
                    Statements = [
                result with { Value = call with { Arguments = [argument] } },
            ]
                }
            };
        }
        program = program with {
            Functions = program.Functions
            .SetItem(address.Function, callback).SetItem(program.EntryPoint, main)
        };
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast));
    }

    [Test]
    public async Task WindowLifetimeRunsIndependentCAndTerrumCallbacksWithoutRetainedWindowStateAsync()
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var repositoryRoot = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(repositoryRoot).ConfigureAwait(false);
        var directory = TestArtifactPaths.CreateDirectory("terrum-native-window-lifetime-");
        var sourcePath = Path.Combine(repositoryRoot, "tests", "native", "native-window-lifetime-probe.c");
        var libraryPath = Path.Combine(directory, "native-window-lifetime-probe.dll");
        var selfTestPath = Path.Combine(directory, "native-window-lifetime-self-test.exe");
        try
        {
            var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")
                ?? throw new InvalidOperationException("ComSpec is unset.")) {
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /O2 /MT /TC /LD /Fo\"{Path.Combine(directory, "fixture.obj")}\" /Fe\"{libraryPath}\" \"{sourcePath}\" /link /incremental:no user32.lib && cl /nologo /W4 /WX /O2 /MT /TC /DNATIVE_FFI_WINDOW_LIFETIME_SELF_TEST /Fo\"{Path.Combine(directory, "self-test.obj")}\" /Fe\"{selfTestPath}\" \"{sourcePath}\" /link /incremental:no user32.lib\"",
                WorkingDirectory = directory,
            };
            _ = await RunAsync(build, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            var selfTestOutput = await RunAsync(new ProcessStartInfo(selfTestPath) {
                WorkingDirectory = directory,
            }, TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            Assert.That(selfTestOutput.Contains("window-lifetime C self-test: 11 cases passed", StringComparison.Ordinal), Is.True);

            var cases = new[] {
                (Before: "", After: "", Expected: 0),
                (Before: "lParam != 0 ? 1 : -1", After: "0", Expected: 31),
                (Before: "lParam != 0 ? 0 : -1", After: "1", Expected: 32),
                (Before: "4294967296 | int64(CurrentThreadId())", After: "0", Expected: 33),
                (Before: "8589934592 | int64(CurrentThreadId())", After: "0", Expected: 34),
                (Before: "12884901888 | int64(CurrentThreadId())", After: "0", Expected: 35),
                (Before: "4294967296 | int64(CurrentThreadId())", After: "4294967296 | (int64(CurrentThreadId()) ^ 1)", Expected: 33),
                (Before: "", After: "", Expected: 0),
            };
            for (var index = 0; index < cases.Length; index++)
            {
                var test = cases[index];
                var callback = test.Before.Length == 0 ? WindowLifetimeCallback
                    : WindowLifetimeCallback.Replace(test.Before, test.After, StringComparison.Ordinal);
                // A second call in the same image must be able to reuse the already-unregistered class.
                var main = $"int32 result = NativeFfiWindowLifetime(&Host.WindowProcedure);"
                    + $"if (result != {test.Expected}) {{ return 100 + result; }}"
                    + "return NativeFfiWindowLifetime(&Host.WindowProcedure);";
                var compilation = CompileWindowLifetime(callback, main);
                Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
                var __nunitAssertValue15 = compilation.Program;
                Assert.That(__nunitAssertValue15, Is.TypeOf<BoundProgram>());
                var program = ((BoundProgram)__nunitAssertValue15);
                var image = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
                var executablePath = Path.Combine(directory, $"window-lifetime-{index}.exe");
                await File.WriteAllBytesAsync(executablePath, image).ConfigureAwait(false);
                using var process = Process.Start(new ProcessStartInfo(executablePath) {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = directory,
                }) ?? throw new InvalidOperationException("Native window-lifetime executable did not start.");
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync().ConfigureAwait(false);
                    }
                    throw;
                }
                Assert.That(process.ExitCode, Is.EqualTo(test.Expected));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
