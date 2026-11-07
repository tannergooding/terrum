// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class NativeModuleCompiler
{
    /// <summary>Emits an executable from an explicitly selected Standard root and provider set.</summary>
    /// <param name="input">The complete root artifact and its exact schema selection.</param>
    /// <param name="moduleIdentity">The exact executable root Module identity.</param>
    /// <param name="providers">The complete explicitly selected provider artifacts.</param>
    /// <param name="runtimePolicy">The selected fail-fast runtime policy.</param>
    /// <param name="cancellationToken">A token that cancels native image emission.</param>
    /// <returns>The complete Windows x64 PE32+ executable image.</returns>
    /// <exception cref="InvalidDataException">A selected artifact or root Module is invalid.</exception>
    /// <exception cref="NotSupportedException">The selected schema, operation or runtime target is unsupported.</exception>
    /// <exception cref="OverflowException">A selected A3 target-layout calculation exceeds its representable range.</exception>
    /// <exception cref="TrRmResourceLimitException">The selected A3 request exceeds deterministic layout capacity.</exception>
    public static byte[] EmitStandardExecutable(
        TrRmStandardArtifactInput input, string moduleIdentity,
        IReadOnlyList<TrRmStandardArtifactInput> providers,
        NativeRuntimePolicy runtimePolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(providers);
        cancellationToken.ThrowIfCancellationRequested();
        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            input, moduleIdentity, providers,
            TrRmStandardNativeOperation.EmitExecutable, runtimePolicy);
        if (plan.RootProgram.EntryPoint < 0)
        {
            var exception = new InvalidDataException(
                "The selected Standard root Module has no executable entry.");
            plan.A3AdmissionScope?.Mark(exception,
                TrRmA3AdmissionFailureKind.InvalidArtifact);
            throw exception;
        }

        var (program, bindings) = plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable);
        if (plan.IsLocalAggregateLayoutPlan)
        {
            return NativeEmitter.Emit(program, runtimePolicy,
                cancellationToken);
        }

        return NativeEmitter.EmitWithImportUsage(program, bindings,
            runtimePolicy, allowDirectNativeImports: true,
            cancellationToken: cancellationToken).Image;
    }

    /// <summary>Emits a library from an explicitly selected Standard root and provider set.</summary>
    /// <param name="input">The complete root artifact and its exact schema selection.</param>
    /// <param name="moduleIdentity">The exact no-entry library Module identity.</param>
    /// <param name="providers">The complete explicitly selected provider artifacts.</param>
    /// <param name="exports">The exact public root functions to export.</param>
    /// <param name="runtimePolicy">The selected fail-fast runtime policy.</param>
    /// <param name="cancellationToken">A token that cancels native image emission.</param>
    /// <returns>The complete Windows x64 PE32+ DLL image.</returns>
    /// <exception cref="InvalidDataException">A selected artifact or root Module is invalid.</exception>
    /// <exception cref="NotSupportedException">The selected schema, operation or runtime target is unsupported.</exception>
    /// <exception cref="OverflowException">A selected A3 target-layout calculation exceeds its representable range.</exception>
    /// <exception cref="TrRmResourceLimitException">The selected A3 request exceeds deterministic layout capacity.</exception>
    public static byte[] EmitStandardLibrary(
        TrRmStandardArtifactInput input, string moduleIdentity,
        IReadOnlyList<TrRmStandardArtifactInput> providers,
        IReadOnlyList<string> exports, NativeRuntimePolicy runtimePolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(exports);
        var exportSnapshot = new string[exports.Count];
        for (var index = 0; index < exports.Count; index++)
        {
            exportSnapshot[index] = exports[index]
                ?? throw new ArgumentNullException($"{nameof(exports)}[{index}]");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            input, moduleIdentity, providers,
            TrRmStandardNativeOperation.EmitLibrary, runtimePolicy);
        if (plan.RootProgram.EntryPoint >= 0)
        {
            var exception = new InvalidDataException(
                "The selected Standard root Module has an executable entry.");
            plan.A3AdmissionScope?.Mark(exception,
                TrRmA3AdmissionFailureKind.InvalidArtifact);
            throw exception;
        }

        plan.ValidateLibraryExports(exportSnapshot);
        var (program, bindings) = plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitLibrary);
        if (plan.IsLocalAggregateLayoutPlan)
        {
            return NativeEmitter.EmitLibraryWithVerifiedFixedArrayBodies(
                program, exportSnapshot, runtimePolicy, cancellationToken);
        }

        return NativeEmitter.EmitLibraryWithImportUsage(program,
            exportSnapshot, bindings, runtimePolicy,
            allowDirectNativeImports: true,
            cancellationToken: cancellationToken).Image;
    }

    /// <summary>Executes an explicitly selected Standard root and provider set in the current process.</summary>
    /// <param name="input">The complete root artifact and its exact schema selection.</param>
    /// <param name="moduleIdentity">The exact executable root Module identity.</param>
    /// <param name="providers">The complete explicitly selected provider artifacts.</param>
    /// <param name="runtimePolicy">The selected fail-fast runtime policy.</param>
    /// <param name="registrarAssembly">The trusted registrar used by the existing JIT contract.</param>
    /// <returns>The root entry's raw int32 result.</returns>
    /// <exception cref="InvalidDataException">A selected artifact or root Module is invalid.</exception>
    /// <exception cref="NotSupportedException">The selected schema, operation or runtime target is unsupported.</exception>
    /// <exception cref="OverflowException">A selected A3 target-layout calculation exceeds its representable range.</exception>
    /// <exception cref="TrRmResourceLimitException">The selected A3 request exceeds deterministic layout capacity.</exception>
    public static int ExecuteStandard(
        TrRmStandardArtifactInput input, string moduleIdentity,
        IReadOnlyList<TrRmStandardArtifactInput> providers,
        NativeRuntimePolicy runtimePolicy, string? registrarAssembly = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(providers);
        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            input, moduleIdentity, providers,
            TrRmStandardNativeOperation.Execute, runtimePolicy);
        if (plan.RootProgram.EntryPoint < 0)
        {
            var exception = new InvalidDataException(
                "The selected Standard root Module has no executable entry.");
            plan.A3AdmissionScope?.Mark(exception,
                TrRmA3AdmissionFailureKind.InvalidArtifact);
            throw exception;
        }

        return NativeJit.ExecuteStandard(plan, runtimePolicy,
            registrarAssembly);
    }
}
