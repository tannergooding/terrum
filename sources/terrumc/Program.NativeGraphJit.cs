// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;

namespace Terrum.Tools;

internal static partial class Program
{
    private const string NativeGraphJitUsage = "Usage: terrumc run-native-graph-jit <consumer.trng> "
        + "--provider <selected.trmn|selected.trng> [--provider <selected>] "
        + "--runtime <fail-fast|checked-exceptions> [--json]";

    private static int RunNativeGraphJitCommand(string[] args, bool json)
    {
        var worker = args[0] == "--native-graph-jit-worker";
        var length = args.Length - (args.Length != 0 && args[^1] == "--json" ? 1 : 0);
        if (length < 6 || (length & 1) != 0 || (json && (worker || length == args.Length
            || Array.IndexOf(args, "--json") != args.Length - 1)))
        {
            WriteInputError(json, "TRM2000", NativeGraphJitUsage);
            return 2;
        }
        var providers = new List<string>();
        NativeRuntimePolicy? policy = null;
        string? registrationInput = null;
        string? registrationOutput = null;
        for (var index = 2; index < length; index += 2)
        {
            switch (args[index])
            {
                case "--provider" when providers.Count < 16:
                    providers.Add(args[index + 1]);
                    break;
                case "--runtime" when policy is null && args[index + 1] is "fail-fast" or "checked-exceptions":
                    policy = args[index + 1] == "fail-fast"
                        ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions;
                    break;
                case "--registrar-input" when worker && registrationInput is null:
                    registrationInput = args[index + 1];
                    break;
                case "--registrar-output" when worker && registrationOutput is null:
                    registrationOutput = args[index + 1];
                    break;
                default:
                    WriteInputError(json, "TRM2000", NativeGraphJitUsage);
                    return 2;
            }
        }
        if (providers.Count == 0 || policy is null
            || (worker && (registrationInput is null || registrationOutput is null)))
        {
            WriteInputError(json, "TRM2000", NativeGraphJitUsage);
            return 2;
        }
        try
        {
            if (worker)
            {
                return NativeModuleCompiler.ExecuteGraphJitWorker(args[1], providers, policy.Value,
                    registrarInputPipe: registrationInput, registrarOutputPipe: registrationOutput);
            }
            var root = ReadModuleFile(args[1], 16 * 1024 * 1024);
            var packages = providers.Select(path => ReadModuleFile(path, 16 * 1024 * 1024)).ToArray();
            return NativeModuleCompiler.RunGraphJitAsync(root, packages, GraphJitWorkerApphostPath(),
                TimeSpan.FromSeconds(30), policy.Value).GetAwaiter().GetResult();
        }
        catch (ModuleFormatException exception)
        {
            WriteInputError(json, "TRM5001", exception.Message);
            return 1;
        }
        catch (ModuleContractException exception)
        {
            WriteInputError(json, "TRM5002", exception.Message);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or TimeoutException or Win32Exception
            or DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            WriteInputError(json, "TRM5003", exception.Message);
            return 2;
        }
    }

    private static string GraphJitRegistrarAssemblyPath()
    {
        var assembly = Path.Combine(AppContext.BaseDirectory, "terrumc.dll");
        if (!File.Exists(assembly))
        {
            throw new NotSupportedException("Native graph JIT requires terrumc.dll alongside the CLI.");
        }
        return assembly;
    }

    private static string GraphJitWorkerApphostPath()
    {
        var apphost = Path.Combine(AppContext.BaseDirectory, "terrumc.exe");
        if (!File.Exists(apphost))
        {
            throw new NotSupportedException("Native graph JIT requires terrumc.exe alongside the CLI.");
        }
        return apphost;
    }
}
