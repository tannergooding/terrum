// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrum.CodeAnalysis.Semantics;

public static partial class SemanticVerifier
{
    private static bool IsNativeComImportOutputParameter(BoundFunction function, int parameter,
        BoundProgram program)
    {
        if (function.IsExternal && program.NativeImportCatalog is NativeImportCatalog catalog
            && function.NativeImportKey is string key
            && catalog.TryGetImport(key, out var definition) && definition is not null
            && parameter >= 0 && parameter < definition.ParameterTypes.Length
            && definition.ParameterTypes[parameter] == PrimitiveType.NativeComInterfaceOutputAddress
            && catalog.MatchesFunction(function, definition, program.Structures))
        {
            return true;
        }
        return parameter >= 0 && parameter < function.ParameterCount
            && parameter < function.Variables.Length && function.Variables[parameter].IsOut
            && NativeComCallContract.Owner(function.Variables[parameter].Type, program) is not null
            && NativeComAcquisitions(function, program)?.ContainsKey(parameter) == true;
    }

    private static NativeComOwnerDefinition? VerifiedNativeComOwner(PrimitiveType type, BoundProgram program)
    {
        try
        {
            return NativeComCallContract.Owner(type, program);
        }
        catch (ArgumentException exception)
        {
            throw InvalidEffect(default, exception.Message);
        }
    }

    private static Dictionary<int, int>? NativeComAcquisitions(BoundFunction function, BoundProgram program)
    {
        try
        {
            return NativeComCallContract.Acquisitions(function, program);
        }
        catch (ArgumentException exception)
        {
            throw InvalidEffect(function.Span, exception.Message);
        }
    }

    private static int VerifiedNativeComParameterIndex(BoundFunction function, int parameter,
        NativeComMethodDefinition method)
    {
        try
        {
            return NativeComCallContract.NativeParameterIndex(function, parameter, method);
        }
        catch (ArgumentException exception)
        {
            throw InvalidEffect(function.Span, exception.Message);
        }
    }

    private static void VerifyNativeComCall(BoundCall call, BoundFunction target, BoundProgram program)
    {
        if (target.NativeComMethod is not NativeComMethodDefinition method)
        {
            return;
        }
        if (program.NativeImportCatalog is not NativeImportCatalog catalog
            || !catalog.TryGetComMethod(method.Identity, out var selected)
            || !ReferenceEquals(method, selected) || !method.IsCallable || call.Arguments.IsDefaultOrEmpty)
        {
            throw InvalidEffect(call.Span, "NativeImport");
        }

        var enforceGenericOwnerLoans = target.Intrinsic == BoundIntrinsic.NativeComVTableCall;
        if (!enforceGenericOwnerLoans)
        {
            if (call.NativeComCall is not null
                || call.Arguments[0] is not BoundReceiverLoan receiverLoan
                || VerifiedNativeComOwner(receiverLoan.Type, program) is null)
            {
                throw InvalidEffect(call.Span, "NativeImport");
            }
            for (var argumentIndex = 0; argumentIndex < call.Arguments.Length; argumentIndex++)
            {
                var argument = call.Arguments[argumentIndex];
                if (argument is not BoundReceiverLoan loan
                    || VerifiedNativeComOwner(loan.Type, program) is not NativeComOwnerDefinition owner)
                {
                    continue;
                }
                var parameter = call.ParameterIndex(argumentIndex);
                var nativeParameter = VerifiedNativeComParameterIndex(target, parameter, method);
                try
                {
                    _ = NativeComCallContract.LoanInterface(method, nativeParameter, owner, program);
                }
                catch (ArgumentException)
                {
                    throw InvalidEffect(loan.Span, "NativeImport");
                }
            }
            return;
        }

        if (call.NativeComCall is not NativeComCallFacts facts
            || !ReferenceEquals(facts.Method, selected) || facts.Loans.IsDefaultOrEmpty
            || !call.ArgumentParameterIndices.IsDefault
            || facts.Loans.Length != call.Arguments.Count(argument =>
                argument is BoundReceiverLoan loan && VerifiedNativeComOwner(loan.Type, program) is not null)
            || call.Arguments[0] is not BoundReceiverLoan
            || facts.Loans[0] is not NativeComOwnerLoanFacts receiver || receiver.ParameterIndex != 0)
        {
            throw InvalidEffect(call.Span, "NativeImport");
        }
        var index = 0;
        for (var argumentIndex = 0; argumentIndex < call.Arguments.Length; argumentIndex++)
        {
            var boundParameter = call.ParameterIndex(argumentIndex);
            if (boundParameter < 0 || boundParameter >= target.ParameterCount
                || boundParameter >= target.Variables.Length)
            {
                throw InvalidEffect(call.Span, "NativeImport");
            }
            var argument = call.Arguments[argumentIndex];
            if (argument is BoundReceiverLoan nonComLoan
                && VerifiedNativeComOwner(nonComLoan.Type, program) is null)
            {
                continue;
            }
            var nativeParameter = VerifiedNativeComParameterIndex(target, boundParameter, method);
            if (nativeParameter == method.OutputParameterIndex)
            {
                if (argument is not BoundOutArgument)
                {
                    throw InvalidEffect(argument.Span, "NativeImport");
                }
                continue;
            }
            var isOwnerParameter = VerifiedNativeComOwner(
                target.Variables[boundParameter].Type, program) is not null;
            var isOwnerArgument = VerifiedNativeComOwner(argument.Type, program) is not null;
            if ((nativeParameter == 0 || isOwnerParameter || isOwnerArgument
                    || method.BorrowedOwnerParameters.Contains(nativeParameter))
                && argument is not BoundReceiverLoan)
            {
                throw InvalidEffect(argument.Span, "NativeImport");
            }
            if (argument is not BoundReceiverLoan loan
                || VerifiedNativeComOwner(loan.Type, program) is not NativeComOwnerDefinition owner)
            {
                continue;
            }
            var retained = facts.Loans[index++];
            try
            {
                if (retained is null || retained.ParameterIndex != boundParameter
                    || retained.Variable != loan.Variable || !ReferenceEquals(retained.Owner, owner)
                    || !ReferenceEquals(retained.Interface,
                        NativeComCallContract.LoanInterface(method, nativeParameter, owner, program)))
                {
                    throw InvalidEffect(loan.Span, "NativeImport");
                }
            }
            catch (ArgumentException)
            {
                throw InvalidEffect(loan.Span, "NativeImport");
            }
        }
    }

    private static void VerifyNativeComParameterAddress(BoundNativeStorageAddress address,
        NativeComMethodDefinition method, int nativeParameterIndex, InitializationState initialized,
        uint[] fieldMasks, HashSet<int> declarations, BoundFunction function, BoundProgram program,
        ref int remaining, int depth, FailureEdges? failures)
    {
        Count(ref remaining, depth);
        if (!NativeStorageParameterContract.MatchesComParameterAddress(
                address, method, nativeParameterIndex, function, program)
            || (uint)address.Variable >= (uint)function.Variables.Length
            || !declarations.Contains(address.Variable) || !initialized[address.Variable])
        {
            throw InvalidEffect(address.Span, "NativeImport");
        }
        Expression(address.Index, initialized, fieldMasks, declarations, function, program,
            ref remaining, depth + 1, failures);
    }

    private static void VerifyNativeComLoan(BoundCall call, int parameter, BoundReceiverLoan loan,
        InitializationState initialized, BoundFunction function, BoundProgram program)
    {
        if (NativeComCallContract.Owner(loan.Type, program) is not NativeComOwnerDefinition owner)
        {
            return;
        }
        if (call.NativeComCall is NativeComCallFacts facts)
        {
            var retained = facts.Loans.Single(fact => fact.ParameterIndex == parameter);
            if (retained.ParameterIndex != parameter || retained.Variable != loan.Variable
                || !ReferenceEquals(retained.Owner, owner))
            {
                throw InvalidEffect(loan.Span, "NativeImport");
            }
        }
        else if ((uint)call.Function >= (uint)program.Functions.Length
            || program.Functions[call.Function].NativeComMethod is not NativeComMethodDefinition method)
        {
            throw InvalidEffect(loan.Span, "NativeImport");
        }
        else
        {
            var target = program.Functions[call.Function];
            var nativeParameter = VerifiedNativeComParameterIndex(target, parameter, method);
            try
            {
                _ = NativeComCallContract.LoanInterface(method, nativeParameter, owner, program);
            }
            catch (ArgumentException)
            {
                throw InvalidEffect(loan.Span, "NativeImport");
            }
        }
        if (loan.Variable < 0 || loan.Variable >= initialized.Length || !initialized[loan.Variable]
            || loan.Type != function.Variables[loan.Variable].Type
            || initialized.ComOwnerAcquisitions is null
            || !initialized.ComOwnerAcquisitions.TryGetValue(loan.Variable, out var acquisition)
            || (call.NativeComCall is NativeComCallFacts retainedCall
                && retainedCall.Loans.Single(fact => fact.ParameterIndex == parameter).AcquisitionFunction
                    != acquisition)
            || !ReferenceEquals(owner, NativeComCallContract.Owner(function.Variables[loan.Variable].Type, program)))
        {
            throw InvalidEffect(loan.Span, "NativeImport");
        }
    }
}
