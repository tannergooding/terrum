// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmDevelopmentNativeConventions
{
    internal static bool TryEncodeConvention(NativeCallingConvention convention,
        byte role, out byte value)
    {
        value = role switch {
            1 or 2 => convention switch {
                NativeCallingConvention.Unmanaged => 1,
                NativeCallingConvention.Cdecl => 2,
                NativeCallingConvention.Stdcall => 3,
                NativeCallingConvention.Vectorcall => 4,
                _ => byte.MaxValue,
            },
            3 => convention switch {
                NativeCallingConvention.Native => 0,
                NativeCallingConvention.Unmanaged => 1,
                NativeCallingConvention.Cdecl => 2,
                NativeCallingConvention.Stdcall => 3,
                NativeCallingConvention.Vectorcall => 4,
                _ => byte.MaxValue,
            },
            _ => byte.MaxValue,
        };
        return value != byte.MaxValue;
    }

    internal static bool TryDecodeConvention(byte value, byte role,
        out NativeCallingConvention convention)
    {
        convention = role switch {
            1 or 2 => value switch {
                1 => NativeCallingConvention.Unmanaged,
                2 => NativeCallingConvention.Cdecl,
                3 => NativeCallingConvention.Stdcall,
                4 => NativeCallingConvention.Vectorcall,
                _ => NativeCallingConvention.Native,
            },
            3 => value switch {
                0 => NativeCallingConvention.Native,
                1 => NativeCallingConvention.Unmanaged,
                2 => NativeCallingConvention.Cdecl,
                3 => NativeCallingConvention.Stdcall,
                4 => NativeCallingConvention.Vectorcall,
                _ => NativeCallingConvention.Native,
            },
            _ => NativeCallingConvention.Native,
        };
        return (role is 1 or 2 && value is >= 1 and <= 4)
            || (role == 3 && value <= 4);
    }

    internal static NativeCallingConvention DecodeConvention(byte value, byte role)
        => TryDecodeConvention(value, role, out var convention)
            ? convention : throw InvalidNativeConvention();

    internal static bool IsValidNativeAbi(TrRmNativeAbiContract abi,
        TrRmArtifactSchema schema)
    {
        if (abi.AbiId != 1 || abi.ExceptionMode != 0)
        {
            return false;
        }

        if (!schema.UsesDevelopmentNativeConventions())
        {
            return abi.ConventionOrReserved == 0;
        }

        return abi.Role == 4
            ? abi.ConventionOrReserved == 0
            : TryDecodeConvention(abi.ConventionOrReserved, abi.Role, out _);
    }

    internal static bool TryEncodeResolution(NativeImportResolutionMode mode,
        NativeImportResolutionPolicy policy, out byte modeValue, out byte policyValue)
    {
        (modeValue, policyValue) = (mode, policy) switch {
            (NativeImportResolutionMode.Link, NativeImportResolutionPolicy.NotApplicable)
                => ((byte)1, (byte)0),
            (NativeImportResolutionMode.Dynamic, NativeImportResolutionPolicy.Default)
                => ((byte)2, (byte)1),
            (NativeImportResolutionMode.Dynamic,
                NativeImportResolutionPolicy.CompatibilitySearch) => ((byte)2, (byte)2),
            _ => ((byte)0, (byte)0),
        };
        return modeValue != 0;
    }

    internal static bool TryDecodeResolution(byte modeValue, byte policyValue,
        out NativeImportResolutionMode mode, out NativeImportResolutionPolicy policy)
    {
        (mode, policy) = (modeValue, policyValue) switch {
            (1, 0) => (NativeImportResolutionMode.Link,
                NativeImportResolutionPolicy.NotApplicable),
            (2, 1) => (NativeImportResolutionMode.Dynamic,
                NativeImportResolutionPolicy.Default),
            (2, 2) => (NativeImportResolutionMode.Dynamic,
                NativeImportResolutionPolicy.CompatibilitySearch),
            _ => (NativeImportResolutionMode.Link,
                NativeImportResolutionPolicy.NotApplicable),
        };
        return modeValue is 1 or 2
            && (modeValue != 1 || policyValue == 0)
            && (modeValue != 2 || policyValue is 1 or 2);
    }

    internal static NativeImportResolutionMode DecodeResolutionMode(byte modeValue,
        byte policyValue)
        => TryDecodeResolution(modeValue, policyValue, out var mode, out _)
            ? mode : throw InvalidNativeConvention();

    internal static NativeImportResolutionPolicy DecodeResolutionPolicy(byte modeValue,
        byte policyValue)
        => TryDecodeResolution(modeValue, policyValue, out _, out var policy)
            ? policy : throw InvalidNativeConvention();

    private static InvalidDataException InvalidNativeConvention()
        => new("Invalid Development TRRM native convention or resolution contract.");
}
