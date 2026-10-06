// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Tests;

internal sealed class NativeSourceGrantTestBindings
{
    private readonly Dictionary<string, BoundExport> _exports = new(StringComparer.Ordinal);

    public NativeSourceGrantTestBindings(nint module,
        ImmutableArray<(string Name, bool SourceDependent, PrimitiveType ReturnType,
            ImmutableArray<PrimitiveType> ParameterTypes)> exports)
    {
        if (module == 0 || exports.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A loaded module and selected exports are required.");
        }

        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("TerrumSourceGrantTestBindings" + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run);
        var moduleBuilder = assembly.DefineDynamicModule("Bindings");
        var index = 0;
        foreach (var export in exports)
        {
            if (export.ReturnType == PrimitiveType.Void)
            {
                throw new NotSupportedException("Generated test bindings require a value-returning export.");
            }
            var parameterTypes = new List<Type>(export.ParameterTypes.Length + (export.SourceDependent ? 2 : 0));
            if (export.SourceDependent)
            {
                parameterTypes.Add(typeof(nint));
                parameterTypes.Add(typeof(uint));
            }
            foreach (var parameterType in export.ParameterTypes)
            {
                parameterTypes.Add(ManagedType(parameterType));
            }

            var delegateType = CreateDelegateType(moduleBuilder, $"Export{index++}",
                ManagedType(export.ReturnType), [.. parameterTypes]);
            var function = Marshal.GetDelegateForFunctionPointer(
                NativeLibrary.GetExport(module, export.Name), delegateType);
            if (!_exports.TryAdd(export.Name, new BoundExport(export, function)))
            {
                throw new ArgumentException($"Duplicate selected export '{export.Name}'.", nameof(exports));
            }
        }
    }

    public T Invoke<T>(string exportName, char[]? source, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!_exports.TryGetValue(exportName, out var export))
        {
            throw new ArgumentException($"'{exportName}' is not a selected export.", nameof(exportName));
        }
        if (arguments.Length != export.Descriptor.ParameterTypes.Length)
        {
            throw new ArgumentException("The authored argument count does not match the selected export.",
                nameof(arguments));
        }
        if (!export.Descriptor.SourceDependent)
        {
            if (source is not null)
            {
                throw new ArgumentException("Source-free exports do not accept a source grant.", nameof(source));
            }
            return Result<T>(export.Function.DynamicInvoke(arguments));
        }
        if (source is { Length: > 16_384 })
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        var callArguments = new object?[arguments.Length + 2];
        callArguments[1] = (uint)(source?.Length ?? 0);
        arguments.CopyTo(callArguments, 2);
        if (source is null or { Length: 0 })
        {
            callArguments[0] = nint.Zero;
            return Result<T>(export.Function.DynamicInvoke(callArguments));
        }

        var pin = GCHandle.Alloc(source, GCHandleType.Pinned);
        try
        {
            var pointer = pin.AddrOfPinnedObject();
            if ((pointer & 1) != 0)
            {
                throw new ArgumentException("The pinned UTF-16 source is not two-byte aligned.", nameof(source));
            }
            callArguments[0] = pointer;
            return Result<T>(export.Function.DynamicInvoke(callArguments));
        }
        finally
        {
            pin.Free();
        }
    }

    private static Type CreateDelegateType(ModuleBuilder module, string name, Type returnType,
        Type[] parameterTypes)
    {
        var builder = module.DefineType(name,
            TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed,
            typeof(MulticastDelegate));
        var constructor = builder.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName,
            CallingConventions.Standard, [typeof(object), typeof(nint)]);
        constructor.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

        var invoke = builder.DefineMethod("Invoke",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot
                | MethodAttributes.Virtual,
            returnType, parameterTypes);
        invoke.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);
        var attributeConstructor = typeof(UnmanagedFunctionPointerAttribute)
            .GetConstructor([typeof(CallingConvention)])
            ?? throw new InvalidOperationException("UnmanagedFunctionPointerAttribute constructor is unavailable.");
        builder.SetCustomAttribute(new CustomAttributeBuilder(attributeConstructor, [CallingConvention.Cdecl]));
        return builder.CreateType()
            ?? throw new InvalidOperationException("Failed to create a test export delegate.");
    }

    private static Type ManagedType(PrimitiveType type) => type switch
    {
        PrimitiveType.Void => typeof(void),
        PrimitiveType.Bool => typeof(byte),
        PrimitiveType.Char16 => typeof(ushort),
        PrimitiveType.Int32 => typeof(int),
        PrimitiveType.UInt32 => typeof(uint),
        PrimitiveType.Int64 => typeof(long),
        PrimitiveType.UInt64 => typeof(ulong),
        _ => throw new NotSupportedException($"'{type}' is not a scalar source-grant test export type."),
    };

    private static T Result<T>(object? result) => result is T value
        ? value : throw new InvalidOperationException("The native export returned an unexpected value.");

    private sealed record BoundExport(
        (string Name, bool SourceDependent, PrimitiveType ReturnType,
            ImmutableArray<PrimitiveType> ParameterTypes) Descriptor,
        Delegate Function);
}
