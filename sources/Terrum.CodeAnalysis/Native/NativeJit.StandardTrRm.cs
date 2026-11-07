// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Native;

public static unsafe partial class NativeJit
{
    internal static int ExecuteStandard(
        TrRmStandardNativeBindingPlan plan,
        NativeRuntimePolicy runtimePolicy, string? registrarAssembly)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var admissionScope = plan.A3AdmissionScope;
        try
        {
        if (runtimePolicy != NativeRuntimePolicy.FailFast
            || plan.RuntimePolicy != runtimePolicy)
        {
            throw new NotSupportedException(
                "The bounded Standard TRRM JIT route supports fail-fast only.");
        }
        if (!OperatingSystem.IsWindows()
            || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException(
                "Standard TRRM native execution requires Windows x64.");
        }

        var (program, bindings) = plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.Execute);
        if (plan.IsLocalAggregateLayoutPlan)
        {
            return Execute(program, runtimePolicy, registrarAssembly);
        }

        if (program.EntryPoint < 0)
        {
            throw new ArgumentException(
                "The selected Standard root Module has no executable entry.",
                nameof(plan));
        }
        if (program.RequiresSourceGrant)
        {
            throw new ArgumentException(
                "Standard TRRM execution does not accept a source grant.",
                nameof(plan));
        }
        if (program.RuntimePolicy != (BoundRuntimePolicy)runtimePolicy)
        {
            throw new ArgumentException(
                "Native and verified checked-failure policies differ.",
                nameof(plan));
        }

        var (code, imports) = NativeEmitter.EmitCodeWithImportUsage(program,
            bindings, runtimePolicy, allowDirectNativeImports: true);
        ValidateBufferMetadata(program, code);
        var (modules, selectedImports) = CreateSystem32ImportModules(imports);
        if (admissionScope is null)
        {
            return ExecuteCode(code, modules, selectedImports,
                registrarAssembly, null, null, 0, false, out _);
        }

        return admissionScope.ExecuteAfterHandoff(() => ExecuteCode(code,
            modules, selectedImports, registrarAssembly,
            null, null, 0, false, out _));
        }
        catch (Exception exception)
        {
            if (admissionScope is not null
                && !admissionScope.IsBeforeExecutionHandoff)
            {
                TrRmA3AdmissionFailureMarkers.Remove(exception);
            }

            throw;
        }
    }
}
