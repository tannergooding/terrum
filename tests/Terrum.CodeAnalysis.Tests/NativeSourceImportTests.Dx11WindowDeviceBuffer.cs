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
    private static string DirectWindowDeviceBufferSource => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "samples", "design", "win32-dx11-triangle", "window-device-buffer-stage.terrum"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static BoundProgram CompileDirectWindowDeviceBuffer()
    {
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(DirectWindowDeviceBufferSource)],
            runtimePolicy: BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        return ((BoundProgram)__nunitAssertValue0);
    }

    [InteractiveWindowTest]
    [Category("InteractiveWindow")]
    public async Task DirectWindowDeviceBufferStageKeepsItsRealWarpBufferThroughVisiblePump()
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var program = CompileDirectWindowDeviceBuffer();
        Assert.That(NativeDx11WindowBufferIntegrationContract.MatchesProgram(program), Is.True);
        Assert.That(NativeDx11WindowAcquisitionContract.MatchesProgram(program), Is.False);
        Assert.That(NativeDx11DirectBufferContract.MatchesProgram(program), Is.False);
        SemanticVerifier.VerifyReferences(program);

        var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.CheckedExceptions);
        Assert.That(image.AsSpan().IndexOf("D3D11CreateDeviceAndSwapChain\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("D3D11CreateDeviceAndSwapChain"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("NativeFfi"u8) < 0, Is.True);

        var directory = Directory.CreateTempSubdirectory("terrum-window-device-buffer-").FullName;
        var executablePath = Path.Combine(directory, "window-device-buffer.exe");
        try
        {
            await File.WriteAllBytesAsync(executablePath, image).ConfigureAwait(false);
            using var process = Process.Start(new ProcessStartInfo(executablePath) {
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = directory,
            }) ?? throw new InvalidOperationException("Direct window-device-buffer executable did not start.");
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

                Assert.That(process.HasExited, Is.False, "WARP or immutable-buffer creation failed before the visible message pump.");
                Assert.That(window, Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(process.MainWindowTitle, Is.EqualTo("Terrum DX11 Triangle"));
                Assert.That(IsWindowVisible(window), Is.True, "The WARP-backed Terrum window was not visible.");
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

    [TestCase("uint32 byteWidth = 36;", "uint32 byteWidth = 35;")]
    [TestCase("ShowWindow(Handle, 5);", "ShowWindow(Handle, 0);")]
    [TestCase("while (result > 0)", "while (result >= 0)")]
    [TestCase("private Terrum.Interop.Windows.HWND Handle;", "public Terrum.Interop.Windows.HWND Handle;")]
    public void DirectWindowDeviceBufferStageRejectsSourceMutation(string original, string replacement)
    {
        var source = DirectWindowDeviceBufferSource;
        Assert.That(source.Contains(original, StringComparison.Ordinal), Is.True);
        var mutated = source.Replace(original, replacement, StringComparison.Ordinal);
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(mutated)],
            runtimePolicy: BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Has.Some.Matches(static diagnostic => diagnostic.Code == "TRM3030"));
    }

    [Test]
    public void DirectWindowDeviceBufferStageAcceptsUnusedOutputLocalRenames()
    {
        var source = DirectWindowDeviceBufferSource.Replace(
            "out var device, out var swapchain, out var context",
            "out var device, out var context, out var swapchain", StringComparison.Ordinal);
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)],
            runtimePolicy: BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue1 = compilation.Program;
        Assert.That(__nunitAssertValue1, Is.TypeOf<BoundProgram>());
        Assert.That(NativeDx11WindowBufferIntegrationContract.MatchesProgram(
                    ((BoundProgram)__nunitAssertValue1)), Is.True);
    }

    [Test]
    public void DirectWindowDeviceBufferStageRejectsForgedBufferCallAndCleanupOrder()
    {
        var program = CompileDirectWindowDeviceBuffer();
        var runIndex = Enumerable.Range(0, program.Functions.Length).Single(
            index => program.Functions[index].Name == NativeWindowOwnerContract.RunName);
        var run = program.Functions[runIndex];
        var __nunitAssertValue2 = run.Body.Statements[2];
        Assert.That(__nunitAssertValue2, Is.TypeOf<BoundTryFinally>());
        var deviceScope = ((BoundTryFinally)__nunitAssertValue2);
        var __nunitAssertValue3 = deviceScope.Body.Statements[1];
        Assert.That(__nunitAssertValue3, Is.TypeOf<BoundTryFinally>());
        var swapScope = ((BoundTryFinally)__nunitAssertValue3);
        var __nunitAssertValue4 = swapScope.Body.Statements[1];
        Assert.That(__nunitAssertValue4, Is.TypeOf<BoundTryFinally>());
        var contextScope = ((BoundTryFinally)__nunitAssertValue4);
        var __nunitAssertValue5 = contextScope.Body.Statements[0];
        Assert.That(__nunitAssertValue5, Is.TypeOf<BoundIf>());
        var warpBranch = ((BoundIf)__nunitAssertValue5);
        var __nunitAssertValue6 = warpBranch.Then;
        Assert.That(__nunitAssertValue6, Is.TypeOf<BoundBlock>());
        var setup = ((BoundBlock)__nunitAssertValue6);
        var __nunitAssertValue7 = setup.Statements[12];
        Assert.That(__nunitAssertValue7, Is.TypeOf<BoundTryFinally>());
        var bufferScope = ((BoundTryFinally)__nunitAssertValue7);
        var __nunitAssertValue8 = bufferScope.Body.Statements[0];
        Assert.That(__nunitAssertValue8, Is.TypeOf<BoundIf>());
        var bufferBranch = ((BoundIf)__nunitAssertValue8);
        var __nunitAssertValue9 = bufferBranch.Condition;
        Assert.That(__nunitAssertValue9, Is.TypeOf<BoundCall>());
        var bufferCall = ((BoundCall)__nunitAssertValue9);

        var forgedCall = bufferCall with {
            Arguments = bufferCall.Arguments.SetItem(1, new BoundRead(5, (PrimitiveType)104, default)),
        };
        var forgedBufferBranch = bufferBranch with { Condition = forgedCall };
        var forgedCallScope = bufferScope with {
            Body = bufferScope.Body with {
                Statements = bufferScope.Body.Statements.SetItem(0, forgedBufferBranch),
            },
        };
        var forgedCallSetup = setup with {
            Statements = setup.Statements.SetItem(12, forgedCallScope),
        };
        var forgedCallWarpBranch = warpBranch with { Then = forgedCallSetup };
        var forgedCallContextScope = contextScope with {
            Body = contextScope.Body with {
                Statements = contextScope.Body.Statements.SetItem(0, forgedCallWarpBranch),
            },
        };
        var forgedCallSwapScope = swapScope with {
            Body = swapScope.Body with { Statements = swapScope.Body.Statements.SetItem(1, forgedCallContextScope) },
        };
        var forgedCallDeviceScope = deviceScope with {
            Body = deviceScope.Body with { Statements = deviceScope.Body.Statements.SetItem(1, forgedCallSwapScope) },
        };
        var forgedCallRun = run with {
            Body = run.Body with { Statements = run.Body.Statements.SetItem(2, forgedCallDeviceScope) },
        };
        var forgedCallProgram = program with {
            Functions = program.Functions.SetItem(runIndex, forgedCallRun),
        };
        Assert.That(NativeDx11WindowBufferIntegrationContract.MatchesProgram(forgedCallProgram), Is.False);
        Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(forgedCallProgram));
        var __nunitAssertValue10 = bufferScope.Finally.Statements[0];
        Assert.That(__nunitAssertValue10, Is.TypeOf<BoundDrop>());
        var bufferDrop = ((BoundDrop)__nunitAssertValue10);
        var forgedLifetimeScope = bufferScope with {
            Finally = bufferScope.Finally with {
                Statements = bufferScope.Finally.Statements.SetItem(0, bufferDrop with { CleanupFunction = 3 }),
            },
        };
        var forgedLifetimeSetup = setup with {
            Statements = setup.Statements.SetItem(12, forgedLifetimeScope),
        };
        var forgedLifetimeWarpBranch = warpBranch with { Then = forgedLifetimeSetup };
        var forgedLifetimeContextScope = contextScope with {
            Body = contextScope.Body with {
                Statements = contextScope.Body.Statements.SetItem(0, forgedLifetimeWarpBranch),
            },
        };
        var forgedLifetimeSwapScope = swapScope with {
            Body = swapScope.Body with {
                Statements = swapScope.Body.Statements.SetItem(1, forgedLifetimeContextScope),
            },
        };
        var forgedLifetimeDeviceScope = deviceScope with {
            Body = deviceScope.Body with {
                Statements = deviceScope.Body.Statements.SetItem(1, forgedLifetimeSwapScope),
            },
        };
        var forgedLifetimeRun = run with {
            Body = run.Body with { Statements = run.Body.Statements.SetItem(2, forgedLifetimeDeviceScope) },
        };
        var forgedLifetimeProgram = program with {
            Functions = program.Functions.SetItem(runIndex, forgedLifetimeRun),
        };
        Assert.That(NativeDx11WindowBufferIntegrationContract.MatchesProgram(forgedLifetimeProgram), Is.False);
        Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(forgedLifetimeProgram));
    }
}
