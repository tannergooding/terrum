// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

namespace Terrum.CodeAnalysis.Semantics;

internal static class NativeCallbackParameterContract
{
    internal static bool IsValid(NativeImportDefinition import)
    {
        if (import.CallbackParameters.IsDefault)
        {
            return false;
        }

        var hasCallbackContract = !import.ParameterContracts.IsDefaultOrEmpty
            && import.ParameterContracts.Contains(NativeImportParameterContract.NativeCallback);
        if (import.CallbackParameters.IsEmpty && !hasCallbackContract)
        {
            return true;
        }

        if (import.ParameterTypes.IsDefault || import.ParameterContracts.IsDefault
            || import.ParameterContracts.Length != import.ParameterTypes.Length)
        {
            return false;
        }

        var hasCallbackParameter = new bool[import.ParameterTypes.Length];
        foreach (var parameter in import.CallbackParameters)
        {
            if (parameter is null || parameter.ParameterIndex < 0
                || parameter.ParameterIndex >= import.ParameterTypes.Length
                || hasCallbackParameter[parameter.ParameterIndex]
                || !IsValidSourceTypeName(parameter.SourceTypeName)
                || parameter.Signature is null
                || import.ParameterTypes[parameter.ParameterIndex] != parameter.Signature.CallbackType
                || import.ParameterContracts[parameter.ParameterIndex] != NativeImportParameterContract.NativeCallback
                || !NativeCallbackContract.IsValid(parameter.Signature)
                || parameter.Lifetime is not (NativeCallbackLifetime.SynchronousNoEscape
                    or NativeCallbackLifetime.RetainedUntilCallReturns))
            {
                return false;
            }

            hasCallbackParameter[parameter.ParameterIndex] = true;
        }

        for (var index = 0; index < hasCallbackParameter.Length; index++)
        {
            var declaresCallback = import.ParameterContracts[index] == NativeImportParameterContract.NativeCallback;
            if (declaresCallback != hasCallbackParameter[index]
                || import.ParameterTypes[index] == PrimitiveType.NativeCallbackAddress
                    != hasCallbackParameter[index])
            {
                return false;
            }
        }

        return true;
    }

    internal static bool TryGetParameter(NativeImportDefinition import, int parameterIndex,
        out NativeCallbackParameterDefinition? parameter)
    {
        if (import.CallbackParameters.IsDefault)
        {
            parameter = null;
            return false;
        }

        foreach (var candidate in import.CallbackParameters)
        {
            if (candidate?.ParameterIndex == parameterIndex)
            {
                parameter = candidate;
                return true;
            }
        }

        parameter = null;
        return false;
    }

    private static bool IsValidSourceTypeName(string sourceTypeName)
    {
        if (string.IsNullOrWhiteSpace(sourceTypeName))
        {
            return false;
        }

        var segmentHasInitial = false;
        var hasSeparator = false;
        foreach (var character in sourceTypeName)
        {
            if (character == '.')
            {
                if (!segmentHasInitial)
                {
                    return false;
                }

                segmentHasInitial = false;
                hasSeparator = true;
            }
            else if ((!segmentHasInitial && (char.IsLetter(character) || character == '_'))
                || (segmentHasInitial && (char.IsLetterOrDigit(character) || character == '_')))
            {
                segmentHasInitial = true;
            }
            else
            {
                return false;
            }
        }

        return hasSeparator && segmentHasInitial;
    }
}

internal sealed record NativeCallbackParameterDefinition(int ParameterIndex, string SourceTypeName,
    NativeCallbackSignatureDefinition Signature, NativeCallbackLifetime Lifetime);

internal enum NativeCallbackLifetime
{
    SynchronousNoEscape,
    RetainedUntilCallReturns,
}
