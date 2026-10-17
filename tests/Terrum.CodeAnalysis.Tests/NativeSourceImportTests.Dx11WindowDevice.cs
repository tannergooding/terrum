using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private static string DirectWindowDeviceSource => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "samples", "design", "win32-dx11-triangle", "window-device-stage.terrum"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static BoundProgram CompileDirectWindowDevice()
    {
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(DirectWindowDeviceSource)],
            runtimePolicy: BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        return ((BoundProgram)__nunitAssertValue0);
    }

    [InteractiveWindowTest]
    [Category("InteractiveWindow")]
    public async Task DirectWindowDeviceStageAcquiresWarpForItsVisibleTerrumWindow()
    {
        var program = CompileDirectWindowDevice();
        Assert.That(NativeDx11WindowAcquisitionContract.MatchesProgram(program), Is.True);
        SemanticVerifier.VerifyReferences(program);
        var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.CheckedExceptions);
        var directory = Directory.CreateTempSubdirectory("terrum-window-device-").FullName;
        var executablePath = Path.Combine(directory, "window-device.exe");
        try
        {
            await File.WriteAllBytesAsync(executablePath, image).ConfigureAwait(false);
            using var process = Process.Start(new ProcessStartInfo(executablePath) {
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = directory,
            }) ?? throw new InvalidOperationException("Direct window-device executable did not start.");
            try
            {
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                var window = IntPtr.Zero;
                while (DateTime.UtcNow < deadline && !process.HasExited)
                {
                    process.Refresh();
                    window = process.MainWindowHandle;
                    if (window != IntPtr.Zero
                        && process.MainWindowTitle == "Terrum DX11 Triangle"
                        && IsWindowVisible(window))
                    {
                        break;
                    }
                    await Task.Delay(50).ConfigureAwait(false);
                }

                Assert.That(process.HasExited, Is.False, "WARP acquisition failed before the visible message pump started.");
                Assert.That(window, Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(process.MainWindowTitle, Is.EqualTo("Terrum DX11 Triangle"));
                Assert.That(IsWindowVisible(window), Is.True, "The WARP swap chain did not target a visible Terrum-owned window.");
                Assert.That(PostMessageW(window, WindowCloseMessage, IntPtr.Zero, IntPtr.Zero), Is.True, "The Terrum-owned WARP window did not accept WM_CLOSE.");
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(process.ExitCode, Is.EqualTo(0));
            }
            catch
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
                throw;
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void DirectWindowDeviceStageRejectsSourceAndBoundHandleEscapes()
    {
        var source = DirectWindowDeviceSource;
        var mutated = source.Replace(
            "trycreatewarpswapchain(Handle,",
            "trycreatewarpswapchain(Terrum.Interop.Windows.HWND.NoOwner,",
            StringComparison.Ordinal);
        Assert.That(mutated, Is.Not.EqualTo(source));
        var rejected = Compilation.CreateWithNativeImports([SyntaxTree.Parse(mutated)],
            runtimePolicy: BoundRuntimePolicy.CheckedExceptions);
        Assert.That(rejected.Diagnostics, Has.Some.Matches(static diagnostic => diagnostic.Code == "TRM3030"));

        var program = CompileDirectWindowDevice();
        Assert.That(NativeDx11WindowAcquisitionContract.MatchesProgram(program), Is.True);
        var runIndex = Enumerable.Range(0, program.Functions.Length).Single(
            index => program.Functions[index].Name == NativeWindowOwnerContract.RunName);
        var run = program.Functions[runIndex];
        var __nunitAssertValue1 = run.Body.Statements[2];
        Assert.That(__nunitAssertValue1, Is.TypeOf<BoundTryFinally>());
        var deviceScope = ((BoundTryFinally)__nunitAssertValue1);
        var __nunitAssertValue2 = deviceScope.Body.Statements[1];
        Assert.That(__nunitAssertValue2, Is.TypeOf<BoundTryFinally>());
        var swapScope = ((BoundTryFinally)__nunitAssertValue2);
        var __nunitAssertValue3 = swapScope.Body.Statements[1];
        Assert.That(__nunitAssertValue3, Is.TypeOf<BoundTryFinally>());
        var contextScope = ((BoundTryFinally)__nunitAssertValue3);
        var __nunitAssertValue4 = contextScope.Body.Statements[0];
        Assert.That(__nunitAssertValue4, Is.TypeOf<BoundIf>());
        var acquisition = ((BoundIf)__nunitAssertValue4);
        var __nunitAssertValue5 = acquisition.Condition;
        Assert.That(__nunitAssertValue5, Is.TypeOf<BoundCall>());
        var call = ((BoundCall)__nunitAssertValue5);
        var forgedCall = call with {
            Arguments = call.Arguments.SetItem(0, new BoundNativeNoOwner(default)),
        };
        var forgedContextScope = contextScope with {
            Body = contextScope.Body with {
                Statements = contextScope.Body.Statements.SetItem(0,
                    acquisition with { Condition = forgedCall }),
            },
        };
        var forgedSwapScope = swapScope with {
            Body = swapScope.Body with { Statements = swapScope.Body.Statements.SetItem(1, forgedContextScope) },
        };
        var forgedDeviceScope = deviceScope with {
            Body = deviceScope.Body with { Statements = deviceScope.Body.Statements.SetItem(1, forgedSwapScope) },
        };
        var forgedRun = run with {
            Body = run.Body with { Statements = run.Body.Statements.SetItem(2, forgedDeviceScope) },
        };
        var forged = program with {
            Functions = program.Functions.SetItem(runIndex, forgedRun),
        };
        Assert.That(NativeDx11WindowAcquisitionContract.MatchesProgram(forged), Is.False);
        Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(forged));
        var __nunitAssertValue6 = contextScope.Body.Statements[0];
        Assert.That(__nunitAssertValue6, Is.TypeOf<BoundIf>());
        var activationAcquisition = ((BoundIf)__nunitAssertValue6);
        var __nunitAssertValue7 = activationAcquisition.Then;
        Assert.That(__nunitAssertValue7, Is.TypeOf<BoundBlock>());
        var pump = ((BoundBlock)__nunitAssertValue7);
        var forgedPump = pump with { Statements = pump.Statements.RemoveAt(0) };
        var forgedActivationContextScope = contextScope with {
            Body = contextScope.Body with {
                Statements = contextScope.Body.Statements.SetItem(
                    0, activationAcquisition with { Then = forgedPump }),
            },
        };
        var forgedActivationSwapScope = swapScope with {
            Body = swapScope.Body with {
                Statements = swapScope.Body.Statements.SetItem(1, forgedActivationContextScope),
            },
        };
        var forgedActivationDeviceScope = deviceScope with {
            Body = deviceScope.Body with {
                Statements = deviceScope.Body.Statements.SetItem(1, forgedActivationSwapScope),
            },
        };
        var forgedActivationRun = run with {
            Body = run.Body with { Statements = run.Body.Statements.SetItem(2, forgedActivationDeviceScope) },
        };
        var forgedActivation = program with {
            Functions = program.Functions.SetItem(runIndex, forgedActivationRun),
        };
        Assert.That(NativeDx11WindowAcquisitionContract.MatchesProgram(forgedActivation), Is.False);
        Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(forgedActivation));
    }
}
