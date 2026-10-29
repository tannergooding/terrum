// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Native;

/// <summary>Behavior on a checked runtime failure in a native executable.</summary>
public enum NativeRuntimePolicy
{
    /// <summary>Terminate with 0xE0540001 for overflow, 0xE0540002 for zero division, 0xE0540003 for failed requirements, or 0xE0540004 for buffer bounds.</summary>
    FailFast,
    /// <summary>Use the bounded, versioned native checked-failure exception contract.</summary>
    CheckedExceptions,
}

/// <summary>Maps one verified external function declaration to a named Windows DLL export.</summary>
public sealed record NativeImportBinding(string FunctionName, string DllName, string ExportName);

/// <summary>Emits owned, deterministic Windows x64 PE32+ artifacts from verified structured IR.</summary>
public static class NativeEmitter
{
    /// <summary>Emits a native consumer executable with explicit bindings for every external declaration.</summary>
    /// <remarks>Bindings specify ABI identities, not verified provider contracts; callers must validate provider compatibility.</remarks>
    public static byte[] EmitWithImports(BoundProgram program, IReadOnlyList<NativeImportBinding> bindings,
        NativeRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default) =>
        EmitWithImportUsage(program, bindings, runtimePolicy,
            cancellationToken: cancellationToken).Image;

    /// <summary>Emits a native executable using verified direct native import declarations.</summary>
    public static byte[] EmitWithNativeImports(BoundProgram program, NativeRuntimePolicy runtimePolicy,
        CancellationToken cancellationToken = default) =>
        EmitWithNativeImportsCore(program, runtimePolicy, allowApplicationDirectory: false, cancellationToken);

    internal static byte[] EmitWithNativeImportsForTesting(BoundProgram program,
        NativeRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default) =>
        EmitWithNativeImportsCore(program, runtimePolicy, allowApplicationDirectory: true, cancellationToken);

    private static byte[] EmitWithNativeImportsCore(BoundProgram program, NativeRuntimePolicy runtimePolicy,
        bool allowApplicationDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(program);

        var bindings = ImmutableArray.CreateBuilder<NativeImportBinding>();
        for (var functionIndex = 0; functionIndex < program.Functions.Length; functionIndex++)
        {
            var function = program.Functions[functionIndex];
            if (!function.IsExternal)
            {
                continue;
            }
            if (function.DirectNativeImport is DirectNativeImport directImport)
            {
                if (!DirectNativeImportContract.IsValid(function, program.Structures,
                    program.NativeFunctionPointerSignatures, program.NativePointerTypes))
                {
                    throw new ArgumentException("A direct native import does not retain its verified contract.",
                        nameof(program));
                }
                bindings.Add(new NativeImportBinding(function.Name,
                    directImport.LibraryName, directImport.ExportName));
                continue;
            }
            if (function.NativeVTableMethod is not null)
            {
                continue;
            }
            throw new ArgumentException("Every external source function must be a direct native import.",
                nameof(program));
        }

        if (bindings.Count == 0 && !HasNativeFixedArrayStorage(program))
        {
            throw new ArgumentException("The program contains no supported native imports.",
                nameof(program));
        }
        var code = CompileWithImports(program, bindings.ToImmutable(), runtimePolicy, cancellationToken,
            allowNativeImports: true).Code;
        return PeImage.Build(allowApplicationDirectory
            ? code
            : QualifyDirectNativeImports(program, code));
    }

    internal static string System32ImportPath(string dllName)
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(systemDirectory)
            || systemDirectory.Any(static character => character is < ' ' or > '~'))
        {
            throw new NotSupportedException("System32 native imports require an ASCII Windows system directory.");
        }
        return Path.Combine(systemDirectory, dllName);
    }

    internal static (byte[] Image, ImmutableArray<NativeImportBinding> Imports)
        EmitWithImportUsage(BoundProgram program, IReadOnlyList<NativeImportBinding> bindings,
            NativeRuntimePolicy runtimePolicy, bool allowNativeImports = false,
            bool allowDirectNativeImports = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(bindings);
        if (allowDirectNativeImports)
        {
            ValidateDirectNativeBindings(program, bindings);
        }
        var (code, selected) = CompileWithImports(program, bindings, runtimePolicy, cancellationToken,
            allowNativeImports || allowDirectNativeImports);
        var used = UsedImports(code, selected);
        if (allowDirectNativeImports)
        {
            code = QualifyDirectNativeImports(program, code);
        }
        return (PeImage.Build(code), used);
    }

    private static NativeCode QualifyDirectNativeImports(BoundProgram program, NativeCode code)
    {
        var system32ImportPaths = new Dictionary<int, string>();
        for (var index = 0; index < program.Functions.Length; index++)
        {
            if (program.Functions[index] is { DirectNativeImport: DirectNativeImport directImport,
                ModuleImport: null })
            {
                system32ImportPaths.Add(index, System32ImportPath(directImport.LibraryName));
            }
        }
        return code with {
            NativeImports = [
                .. code.NativeImports.Select(import =>
                    import.DeclarationIndex >= 0
                        && system32ImportPaths.TryGetValue(import.DeclarationIndex, out var path)
                            ? import with { DllName = path } : import),
            ],
        };
    }

    /// <summary>Emits callable native code with verified external declaration bindings for graph JIT callers.</summary>
    internal static NativeCode EmitCodeWithImports(BoundProgram program, IReadOnlyList<NativeImportBinding> bindings,
        NativeRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default) =>
        EmitCodeWithImportUsage(program, bindings, runtimePolicy,
            cancellationToken: cancellationToken).Code;

    internal static (NativeCode Code, ImmutableArray<NativeImportBinding> Imports)
        EmitCodeWithImportUsage(BoundProgram program, IReadOnlyList<NativeImportBinding> bindings,
            NativeRuntimePolicy runtimePolicy, bool allowDirectNativeImports = false,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(bindings);
        if (allowDirectNativeImports)
        {
            ValidateDirectNativeBindings(program, bindings);
        }
        var (code, selected) = CompileWithImports(program, bindings, runtimePolicy, cancellationToken,
            allowNativeImports: allowDirectNativeImports);
        return (code, UsedImports(code, selected));
    }

    internal static void EnsureNativeFunctionPointerConventionSupported(PrimitiveType type,
        ImmutableArray<NativeFunctionPointerSignatureDefinition> signatures)
    {
        if (!NativeFunctionPointerContract.IsValidType(type, signatures))
        {
            throw new ArgumentException("A native function-pointer use has no valid signature.", nameof(type));
        }

        var convention = signatures[NativeFunctionPointerTypes.Index(type)].CallingConvention;
        EnsureNativeCallingConventionSupported(convention);
    }

    internal static void EnsureNativeCallingConventionSupported(NativeCallingConvention convention)
    {
        if (!NativeFunctionPointerContract.IsKnownConvention(convention))
        {
            throw new ArgumentException("A native call has an unknown calling convention.", nameof(convention));
        }
        if (!NativeFunctionPointerContract.IsSupportedWindowsX64Convention(convention))
        {
            throw new NotSupportedException(
                $"The Windows x64 native emitter does not implement {convention} calls.");
        }
    }

    internal static void EnsureNativeImportResolutionSupported(DirectNativeImport import)
    {
        if (!DirectNativeImportContract.IsValidResolution(import.ResolutionMode,
            import.ResolutionPolicy))
        {
            throw new ArgumentException("A direct native import has invalid resolution settings.",
                nameof(import));
        }
        if (import.ResolutionMode != NativeImportResolutionMode.Link)
        {
            throw new NotSupportedException(
                "The Windows x64 native emitter does not implement Dynamic native imports.");
        }
    }

    internal static (NativeCode Code, ImmutableArray<NativeImportBinding> Imports)
        EmitCodeWithDirectNativeImports(BoundProgram program, NativeRuntimePolicy runtimePolicy,
                CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(program);

        var bindings = ImmutableArray.CreateBuilder<NativeImportBinding>();
        var libraryNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var function in program.Functions)
        {
            if (!function.IsExternal)
            {
                if (function.DirectNativeImport is not null)
                {
                    throw new ArgumentException(
                        "Only external declarations can carry direct native imports.", nameof(program));
                }
                continue;
            }
            if (function.IsModuleReference || function.ModuleImport is not null)
            {
                throw new NotSupportedException(
                    "Provider-bound module imports require an explicit native module binding.");
            }
            if (function.DirectNativeImport is not DirectNativeImport import
                || !DirectNativeImportContract.IsValid(function, program.Structures,
                    program.NativeFunctionPointerSignatures, program.NativePointerTypes))
            {
                throw new NotSupportedException(
                    "Direct-native JIT requires every external declaration to retain its direct import contract.");
            }
            if (!libraryNames.TryGetValue(import.LibraryName, out var libraryName))
            {
                libraryName = import.LibraryName;
                libraryNames.Add(libraryName, libraryName);
            }
            bindings.Add(new NativeImportBinding(function.Name, libraryName, import.ExportName));
        }
        if (bindings.Count == 0)
        {
            throw new ArgumentException("The program contains no direct native imports.", nameof(program));
        }
        var directBindings = bindings.ToImmutable();
        return EmitCodeWithImportUsage(program, directBindings, runtimePolicy,
            allowDirectNativeImports: true, cancellationToken: cancellationToken);
    }

    private static (NativeCode Code, Dictionary<int, NativeImportBinding> Selected)
        CompileWithImports(BoundProgram program, IReadOnlyList<NativeImportBinding> bindings,
            NativeRuntimePolicy runtimePolicy, CancellationToken cancellationToken,
            bool allowNativeImports = false)
    {
        if (runtimePolicy is not (NativeRuntimePolicy.FailFast or NativeRuntimePolicy.CheckedExceptions))
        {
            throw new NotSupportedException("Unsupported native runtime policy.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(bindings);
        if (program.RequiresSourceGrant)
        {
            throw new NotSupportedException("Native modules do not support Host.Source requirements.");
        }
        if (program.Functions.Any(static function => function.DirectNativeImport is not null
                && function.ModuleImport is null)
            && !allowNativeImports)
        {
            throw new NotSupportedException("JIT and low-level import binding do not support source native imports.");
        }
        SemanticVerifier.VerifyReferences(program);
        if (program.RuntimePolicy != (BoundRuntimePolicy)runtimePolicy)
        {
            throw new ArgumentException("Native and verified checked-failure policies differ.", nameof(program));
        }
        var selected = SelectImports(program, bindings, cancellationToken);
        var managedNodes = UsesManagedNodes(program);
        var compiler = new PeCompiler(program, managedNodes, imports: selected,
            cancellationToken: cancellationToken);
        return (compiler.Compile(), selected);
    }

    private static Dictionary<int, NativeImportBinding> SelectImports(BoundProgram program,
        IReadOnlyList<NativeImportBinding> bindings, CancellationToken cancellationToken)
    {
        if (bindings.Count > 1024 || (bindings.Count == 0 && !HasNativeFixedArrayStorage(program)))
        {
            throw new ArgumentException(
                "Native consumers require 1 to 1024 explicit import bindings unless the program uses fixed-array storage.",
                nameof(bindings));
        }
        var externals = new Dictionary<string, int>(bindings.Count, StringComparer.Ordinal);
        for (var index = 0; index < program.Functions.Length; index++)
        {
            if (program.Functions[index].IsExternal)
            {
                externals.Add(program.Functions[index].Name, index);
            }
        }
        if (bindings.Count != externals.Count)
        {
            throw new ArgumentException("Every external function needs exactly one native import binding.", nameof(bindings));
        }
        var selected = new Dictionary<int, NativeImportBinding>(bindings.Count);
        foreach (var binding in bindings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (binding is null || string.IsNullOrEmpty(binding.FunctionName)
                || !externals.TryGetValue(binding.FunctionName, out var index)
                || !ValidDllName(binding.DllName) || !ValidImportName(binding.ExportName))
            {
                throw new ArgumentException("Import bindings must identify valid external declarations and DLL exports.", nameof(bindings));
            }
            if (!selected.TryAdd(index, binding))
            {
                throw new ArgumentException("An external function has multiple native import bindings.", nameof(bindings));
            }
        }
        return selected;
    }

    private static bool HasNativeFixedArrayStorage(BoundProgram program) =>
        program.Functions.Any(static function =>
            function.Variables.Any(static variable => variable.NativeFixedArrayLayout is not null));

    private static ImmutableArray<NativeImportBinding> UsedImports(NativeCode code,
        Dictionary<int, NativeImportBinding>? bindings)
    {
        if (bindings is null)
        {
            return [];
        }
        var used = new HashSet<int>();
        foreach (var fixup in code.NativeImports)
        {
            if (fixup.DeclarationIndex < 0)
            {
                continue;
            }
            if (!bindings.TryGetValue(fixup.DeclarationIndex, out var binding)
                || !fixup.DllName.Equals(binding.DllName, StringComparison.OrdinalIgnoreCase)
                || fixup.ExportName != binding.ExportName)
            {
                throw new InvalidOperationException("Emitted native import has no verified declaration binding.");
            }
            _ = used.Add(fixup.DeclarationIndex);
        }
        return [.. used.Select(index => bindings[index]).OrderBy(static binding => binding.FunctionName, StringComparer.Ordinal)];
    }

    private static bool ValidImportName(string? name) => name is { Length: >= 1 and <= 256 }
        && name.All(static character => character is >= '!' and <= '~');

    private static bool ValidDllName(string? name) => name is { Length: >= 5 and <= 256 }
        && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        && name.All(static character => character is (>= '0' and <= '9') or (>= 'A' and <= 'Z')
            or (>= 'a' and <= 'z') or '_' or '-' or '.');

    /// <summary>Verifies a library and emits a Windows x64 DLL with explicitly named public exports.</summary>
    /// <exception cref="ArgumentException">The library, export names, or public value layouts are invalid.</exception>
    /// <exception cref="NotSupportedException">The runtime policy or a native resource limit is unsupported.</exception>
    public static byte[] EmitLibrary(BoundProgram program, IReadOnlyList<string> exports,
        NativeRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default) =>
        EmitLibraryCore(program, exports, null, runtimePolicy, cancellationToken).Image;

    /// <summary>Test-only prototype: emits a library with invocation-scoped source context and export descriptors.</summary>
    /// <remarks>The source-grant ABI is not a selected compiler boundary or a stable native contract.</remarks>
    public static NativeSourceGrantLibrary EmitLibraryWithSourceGrant(BoundProgram program,
        IReadOnlyList<string> exports, NativeRuntimePolicy runtimePolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(program);
        var dependent = NativeSourceGrantClassifier.Classify(program);
        var descriptors = ImmutableArray.CreateBuilder<NativeLibraryExportDescriptor>();
        var (image, _) = EmitLibraryCore(program, exports, null, runtimePolicy, cancellationToken, dependent,
            captureSelectedExports: selectedExports =>
            {
                foreach (var (name, index) in selectedExports)
                {
                    var function = program.Functions[index];
                    if (function.IsExternal || function.IsConstructor || function.DeclaringStruct is not null
                        || function.ReturnType == PrimitiveType.Void
                        || !NativeSourceGrantClassifier.IsScalar(function.ReturnType))
                    {
                        throw new NotSupportedException("Source-grant export descriptors support only scalar value functions.");
                    }
                    var parameterTypes = ImmutableArray.CreateBuilder<PrimitiveType>(function.ParameterCount);
                    for (var parameter = 0; parameter < function.ParameterCount; parameter++)
                    {
                        var variable = function.Variables[parameter];
                        if (variable.IsOut || variable.IsInit || !NativeSourceGrantClassifier.IsScalar(variable.Type))
                        {
                            throw new NotSupportedException(
                                "Source-grant export descriptors do not support output, loan, or aggregate parameters.");
                        }
                        parameterTypes.Add(variable.Type);
                    }
                    descriptors.Add(new NativeLibraryExportDescriptor(name, dependent[index],
                        function.ReturnType, parameterTypes.MoveToImmutable()));
                }
            });

        return new NativeSourceGrantLibrary(image, descriptors.ToImmutable());
    }

    internal static (byte[] Image, NativeCode Code,
        ImmutableArray<(string Name, bool SourceDependent, PrimitiveType ReturnType,
            ImmutableArray<PrimitiveType> ParameterTypes)> Exports) EmitSourceGrantLibraryForTest(
        BoundProgram program, IReadOnlyList<string> exports, NativeRuntimePolicy runtimePolicy)
    {
        ArgumentNullException.ThrowIfNull(program);
        SemanticVerifier.VerifyLibrary(program);
        var dependent = NativeSourceGrantExperiment.Classify(program);
        NativeCode? emitted = null;
        var exportBindings = ImmutableArray.CreateBuilder<(string Name, bool SourceDependent,
            PrimitiveType ReturnType, ImmutableArray<PrimitiveType> ParameterTypes)>();
        var result = EmitLibraryCore(program, exports, null, runtimePolicy, default, dependent,
            code => emitted = code, selectedExports =>
            {
                foreach (var (name, index) in selectedExports)
                {
                    var function = program.Functions[index];
                    var parameterOffset = function.IsConstructor ? 1 : 0;
                    var parameterTypes = ImmutableArray.CreateBuilder<PrimitiveType>(function.ParameterCount);
                    for (var parameter = 0; parameter < function.ParameterCount; parameter++)
                    {
                        parameterTypes.Add(function.Variables[parameter + parameterOffset].Type);
                    }
                    exportBindings.Add((name, dependent[index], function.ReturnType,
                        parameterTypes.MoveToImmutable()));
                }
            });

        return (result.Image, emitted ?? throw new InvalidOperationException("No experiment code was emitted."),
            exportBindings.ToImmutable());
    }

    /// <summary>Emits a library whose verified external declarations have explicit DLL import bindings.</summary>
    /// <remarks>Bindings specify ABI identities, not provider compatibility; callers must validate dependency contracts.</remarks>
    public static byte[] EmitLibraryWithImports(BoundProgram program, IReadOnlyList<string> exports,
        IReadOnlyList<NativeImportBinding> bindings, NativeRuntimePolicy runtimePolicy,
        CancellationToken cancellationToken = default) =>
        EmitLibraryWithImportUsage(program, exports, bindings, runtimePolicy,
            cancellationToken: cancellationToken).Image;

    internal static (byte[] Image, ImmutableArray<NativeImportBinding> Imports)
        EmitLibraryWithImportUsage(BoundProgram program, IReadOnlyList<string> exports,
            IReadOnlyList<NativeImportBinding> bindings, NativeRuntimePolicy runtimePolicy,
            bool allowDirectNativeImports = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(bindings);
        if (allowDirectNativeImports)
        {
            ValidateDirectNativeBindings(program, bindings);
        }
        return EmitLibraryCore(program, exports, bindings, runtimePolicy, cancellationToken,
            allowDirectNativeImports: allowDirectNativeImports);
    }

    internal static byte[] EmitLibraryWithNativeImports(BoundProgram program,
        IReadOnlyList<string> exports, NativeRuntimePolicy runtimePolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(program);
        var bindings = ImmutableArray.CreateBuilder<NativeImportBinding>();
        foreach (var function in program.Functions)
        {
            if (!function.IsExternal)
            {
                if (function.DirectNativeImport is not null)
                {
                    throw new ArgumentException("Only external declarations can carry direct native imports.",
                        nameof(program));
                }
                continue;
            }
            if (function.DirectNativeImport is not DirectNativeImport import
                || !DirectNativeImportContract.IsValid(function, program.Structures,
                    program.NativeFunctionPointerSignatures, program.NativePointerTypes))
            {
                throw new NotSupportedException(
                    "Native library packages currently require direct imports for every external declaration.");
            }
            bindings.Add(new NativeImportBinding(function.Name, import.LibraryName, import.ExportName));
        }
        if (bindings.Count == 0)
        {
            throw new ArgumentException("The program contains no direct native imports.", nameof(program));
        }
        return EmitLibraryCore(program, exports, bindings.ToImmutable(), runtimePolicy, cancellationToken,
            allowDirectNativeImports: true).Image;
    }

    private static void ValidateDirectNativeBindings(BoundProgram program,
        IReadOnlyList<NativeImportBinding> bindings)
    {
        var directFunctions = program.Functions.Where(static function => function.IsExternal
            && function.DirectNativeImport is not null && function.ModuleImport is null)
            .ToDictionary(static function => function.Name,
                StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (!directFunctions.TryGetValue(binding.FunctionName, out var function)
                || function.DirectNativeImport is not DirectNativeImport import
                || !DirectNativeImportContract.IsValid(function, program.Structures,
                    program.NativeFunctionPointerSignatures, program.NativePointerTypes)
                || !StringComparer.OrdinalIgnoreCase.Equals(import.LibraryName, binding.DllName)
                || import.ExportName != binding.ExportName)
            {
                continue;
            }
            _ = directFunctions.Remove(binding.FunctionName);
        }
        if (directFunctions.Count != 0)
        {
            throw new ArgumentException("Every direct native import needs its declared DLL and export binding.",
                nameof(bindings));
        }
    }

    private static (byte[] Image, ImmutableArray<NativeImportBinding> Imports)
        EmitLibraryCore(BoundProgram program, IReadOnlyList<string> exports,
        IReadOnlyList<NativeImportBinding>? bindings, NativeRuntimePolicy runtimePolicy, CancellationToken cancellationToken,
        bool[]? sourceDependent = null, Action<NativeCode>? captureCode = null,
        Action<ImmutableArray<(string Name, int Index)>>? captureSelectedExports = null,
        bool allowDirectNativeImports = false)
    {
        if (runtimePolicy is not (NativeRuntimePolicy.FailFast or NativeRuntimePolicy.CheckedExceptions))
        {
            throw new NotSupportedException("Unsupported native runtime policy.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(program);
        SemanticVerifier.RejectSourceNativeImports(program, "PE library emission",
            allowDirectNativeImports);
        if (program.RequiresSourceGrant && !program.IsInvocationSourceBound && sourceDependent is null)
        {
            throw new NotSupportedException("An invocation-scoped Host.Source grant must be bound through NativeJit.");
        }
        if (bindings is null)
        {
            SemanticVerifier.VerifyLibrary(program);
        }
        else
        {
            SemanticVerifier.VerifyLibraryReferences(program);
        }
        if (program.RuntimePolicy != (BoundRuntimePolicy)runtimePolicy)
        {
            throw new ArgumentException("Native and verified checked-failure policies differ.", nameof(program));
        }
        ArgumentNullException.ThrowIfNull(exports);
        if (!program.Buffers.IsEmpty && sourceDependent is null)
        {
            throw new NotSupportedException("Native DLLs do not expose host buffer capabilities.");
        }
        if (exports.Count is < 1 or > 1024)
        {
            throw new ArgumentException("A native library requires 1 to 1024 explicit exports.", nameof(exports));
        }
        var selected = new List<(string Name, int Index)>(exports.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var imageNames = new HashSet<string>(StringComparer.Ordinal);
        var nativeCallableEntryNames = new HashSet<string>(StringComparer.Ordinal);
        var managedExports = UsesManagedNodes(program);
        var functionIndices = new Dictionary<string, int>(program.Functions.Length, StringComparer.Ordinal);
        for (var index = 0; index < program.Functions.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            functionIndices.Add(program.Functions[index].Name, index);
        }
        var structureVisibility = new Dictionary<string, bool>(program.Structures.Length,
            StringComparer.Ordinal);
        var publicLayouts = new Dictionary<PrimitiveType, bool>(program.Structures.Length);
        foreach (var structure in program.Structures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            structureVisibility.Add(structure.Name, !structure.IsPrivate && !structure.IsInternal);
        }
        foreach (var name in exports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(name) || !names.Add(name)
                || name.Length > 256 || name.Any(static character => character is < '!' or > '~'))
            {
                throw new ArgumentException("Export names must be unique printable ASCII names of at most 256 bytes.", nameof(exports));
            }
            if (!functionIndices.TryGetValue(name, out var index)
                || !Exportable(program, program.Functions[index], managedExports,
                    structureVisibility, publicLayouts))
            {
                throw new ArgumentException($"'{name}' is not an accessible native library export.", nameof(exports));
            }
            if (!imageNames.Add(name))
            {
                throw new ArgumentException("Native library export names must be unique.", nameof(exports));
            }
            selected.Add((name, index));
        }
        for (var index = 0; index < program.Functions.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var function = program.Functions[index];
            if (function.NativeCallableEntryName is not string entryName)
            {
                continue;
            }
            if (function.NativeFunctionPointerType is null
                || !DirectNativeImportContract.IsValidExportName(entryName)
                || !nativeCallableEntryNames.Add(entryName))
            {
                throw new ArgumentException(
                    $"Function '{function.Name}' has an invalid or duplicate native callable entry name.",
                    nameof(program));
            }
            EnsureNativeFunctionPointerConventionSupported(function.NativeFunctionPointerType.Value,
                program.NativeFunctionPointerSignatures);
            if (!function.IsExternal && !imageNames.Add(entryName))
            {
                throw new ArgumentException("Native library export names must be unique.", nameof(exports));
            }
            if (!function.IsExternal)
            {
                selected.Add((entryName, index));
            }
        }
        if (selected.Count > 1024)
        {
            throw new ArgumentException("A native library supports at most 1024 public exports.", nameof(exports));
        }
        selected.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        captureSelectedExports?.Invoke([.. selected]);
        var imports = bindings is null ? null : SelectImports(program, bindings, cancellationToken);
        var compiler = new PeCompiler(program, managedExports, library: true, imports: imports,
            sourceDependent: sourceDependent, cancellationToken: cancellationToken);
        var code = compiler.Compile();
        var usedImports = UsedImports(code, imports);
        if (allowDirectNativeImports)
        {
            code = QualifyDirectNativeImports(program, code);
        }
        var exportThunks = ImmutableArray.CreateBuilder<NativeExportThunk>();
        foreach (var (exportName, functionIndex) in selected)
        {
            var function = program.Functions[functionIndex];
            if (function.DirectNativeImport is not DirectNativeImport directImport)
            {
                continue;
            }
            if (!allowDirectNativeImports || function.IsModuleReference || function.ModuleImport is not null
                || !DirectNativeImportContract.IsValid(function, program.Structures,
                    program.NativeFunctionPointerSignatures, program.NativePointerTypes)
                || imports is null || !imports.TryGetValue(functionIndex, out var import)
                || !StringComparer.OrdinalIgnoreCase.Equals(import.DllName, directImport.LibraryName)
                || import.ExportName != directImport.ExportName)
            {
                throw new ArgumentException(
                    $"Direct native export '{exportName}' lacks a valid owned import binding.", nameof(exports));
            }
            EnsureNativeCallingConventionSupported(directImport.CallingConvention);
            EnsureNativeImportResolutionSupported(directImport);
            exportThunks.Add(new NativeExportThunk(functionIndex, exportName,
                import with { DllName = System32ImportPath(import.DllName) }));
        }
        if (exportThunks.Count != 0)
        {
            code = code with { NativeExportThunks = exportThunks.ToImmutable() };
        }
        captureCode?.Invoke(code);
        return (PeImage.BuildLibrary(code, [.. selected]), usedImports);
    }

    internal static bool UsesManagedNodes(BoundProgram program) => !program.Classes.IsEmpty
        || program.Functions.Any(static function =>
        function.Intrinsic is >= BoundIntrinsic.NodeTryCreate and <= BoundIntrinsic.NodeCollect
        || ManagedClassTypes.IsManaged(function.ReturnType)
        || function.Variables.Any(static variable => ManagedClassTypes.IsManaged(variable.Type)));

    private static bool Exportable(BoundProgram program, BoundFunction function, bool managedExports,
        IReadOnlyDictionary<string, bool> structureVisibility, Dictionary<PrimitiveType, bool> publicLayouts)
    {
        var isOwnedDirectNativeImport = function.IsExternal
            && function.DirectNativeImport is not null
            && !function.IsModuleReference && function.ModuleImport is null;
        if (function.IsPrivate || function.IsInternal
            || (function.IsExternal && !isOwnedDirectNativeImport)
            || (isOwnedDirectNativeImport
                && !DirectNativeImportContract.IsValid(function, program.Structures,
                    program.NativeFunctionPointerSignatures, program.NativePointerTypes)))
        {
            return false;
        }
        if (function.DeclaringStruct is string owner)
        {
            if (structureVisibility.TryGetValue(owner, out var isPublic))
            {
                if (!isPublic)
                {
                    return false;
                }
            }
            else if (!Binder.TryPrimitiveType(owner, out _))
            {
                return false;
            }
        }
        if (!PublicLayout(function.ReturnType)
            || (managedExports && !ManagedExportShape.Supported(function, program.Structures)))
        {
            return false;
        }
        for (var i = 0; i < function.ParameterCount; i++)
        {
            var parameter = function.Variables[i + (function.IsConstructor ? 1 : 0)];
            if (!PublicLayout(parameter.Type))
            {
                return false;
            }
        }
        return true;

        bool PublicLayout(PrimitiveType type)
        {
            if (publicLayouts.TryGetValue(type, out var isPublic))
            {
                return isPublic;
            }
            if (ManagedClassTypes.IsManaged(type) || TokenSpanContract.IsDependent(type, program.Structures))
            {
                isPublic = false;
            }
            else if (NativePointerTypes.IsType(type) || NativeFunctionPointerTypes.IsType(type))
            {
                isPublic = true;
            }
            else if (BoundEnumTypes.IsType(type))
            {
                isPublic = BoundEnumTypes.IsValidType(
                    type, program.EnumDefinitions);
            }
            else if ((int)type < 100)
            {
                isPublic = true;
            }
            else
            {
                var structure = program.Structures[(int)type - 100];
                isPublic = !structure.IsPrivate && !structure.IsInternal;
                var fields = structure.Fields;
                for (var index = 0; isPublic && index < fields.Length; index++)
                {
                    var field = fields[index];
                    isPublic = !field.IsPrivate && !field.IsInternal && PublicLayout(field.Type);
                }
            }
            publicLayouts.Add(type, isPublic);
            return isPublic;
        }
    }

    /// <summary>Verifies and emits a complete executable image without accessing the filesystem.</summary>
    /// <exception cref="ArgumentException">The IR is invalid.</exception>
    /// <exception cref="NotSupportedException">The runtime policy or an IR, frame, or image resource limit is unsupported.</exception>
    public static byte[] Emit(BoundProgram program, NativeRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default)
    {
        var code = EmitCode(program, runtimePolicy, cancellationToken);
        return PeImage.Build(code);
    }

    /// <summary>Verifies and emits relocatable code with a callable int32(void) entry point.</summary>
    public static NativeCode EmitCode(BoundProgram program, NativeRuntimePolicy runtimePolicy, CancellationToken cancellationToken = default)
    {
        if (runtimePolicy is not (NativeRuntimePolicy.FailFast or NativeRuntimePolicy.CheckedExceptions))
        {
            throw new NotSupportedException("Unsupported native runtime policy.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(program);
        if (program.Functions.Any(static function => function.DirectNativeImport is not null))
        {
            throw new NotSupportedException("Source native imports require the explicit EmitWithNativeImports path.");
        }
        if (program.RequiresSourceGrant && !program.IsInvocationSourceBound)
        {
            throw new NotSupportedException("An invocation-scoped Host.Source grant must be bound through NativeJit.");
        }
        SemanticVerifier.Verify(program);
        if (program.RuntimePolicy != (BoundRuntimePolicy)runtimePolicy)
        {
            throw new ArgumentException("Native and verified checked-failure policies differ.", nameof(program));
        }
        var managedNodes = UsesManagedNodes(program);
        return new PeCompiler(program, managedNodes, cancellationToken: cancellationToken).Compile();
    }
}

internal sealed partial class PeCompiler
{
    // A sub-4 KiB frame cannot cross more than one guard page in its single allocation.
    // 32 bytes of outgoing home space and all outgoing stack arguments stay at the bottom of each frame.
    private const int MaximumFrame = 0xF00;
    private const int MaximumText = 8 * 1024 * 1024;
    private sealed record Binary128Runtime(NativeCode Code,
        ImmutableDictionary<string, int> FunctionOffsets);
    private sealed record BidDecimalRuntime(NativeCode Code,
        ImmutableDictionary<string, int> FunctionOffsets);
    private static readonly Lazy<Binary128Runtime> s_binary128Runtime = new(
        CreateBinary128Runtime, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<BidDecimalRuntime> s_bidDecimalRuntime = new(
        CreateBidDecimalRuntime, LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly BoundProgram _program;
    private readonly bool _library;
    private readonly IReadOnlyDictionary<int, NativeImportBinding>? _nativeImports;
    private readonly CancellationToken _cancellationToken;
    private readonly MachineCode _code = new();
    private readonly int[] _starts;
    private readonly int[] _ends;
    private readonly int[] _frames;
    private readonly int[][] _slots;
    private readonly int[] _spillBases;
    private readonly int[][] _outSlots;
    private readonly int[] _returnSlots;
    private readonly int[][] _edgeCopySlots;
    private readonly int[][] _liveSlots;
    private readonly int[][] _initializationFlags;
    private readonly int[][] _constructedFieldSlots;
    private readonly int[] _replacementSlots;
    private readonly int[] _cleanupArgumentSlots;
    private readonly List<(int Offset, int Target)> _calls = new();
    private readonly List<(int Offset, string Function)> _binary128Calls = [];
    private readonly List<(int Offset, string Function)> _bidDecimalCalls = [];
    private readonly List<(int Offset, int Target)> _functionAddressFixups = [];
    private readonly HashSet<int> _externalFunctionAddresses = [];
    private readonly Dictionary<int, int> _externalFunctionAddressStarts = [];
    private readonly List<(int Offset, int Target)> _runtimeCalls = new();
    private readonly List<(int Offset, int Target)> _cleanupCalls = [];
    private readonly List<(BoundBlock Body, int Function, int Start, int End)> _funclets = [];
    private readonly Dictionary<int, List<(int Value, int Live, PrimitiveType Type)>> _stagedOwners = [];
    private readonly Dictionary<int, List<int>> _managedStagedRoots = [];
    private readonly HashSet<int> _unwindRootFrames = [];
    private readonly List<int> _imports = new();
    private readonly List<NativeImportFixup> _nativeImportFixups = [];
    private readonly List<NativeDataFixup> _dataFixups = [];
    private readonly int[] _bufferOffsets;
    private readonly int[] _nativeUtf16ZOffsets;
    private readonly int[] _nativeUtf8ZOffsets;
    private readonly bool[]? _sourceDependent;
    private readonly int[] _sourceSlots;
    private readonly byte[] _data;
    private readonly bool _managedNodes;
    private readonly int _managedRuntimeOffset;
    private readonly int _hostDataLength;
    private readonly bool _checkedExceptions;
    private readonly List<NativeExceptionScope>[] _scopes;
    private readonly List<NativeExceptionScope> _entryScopes = [];
    private readonly int _readOnlyDataLength;
    private readonly int[] _failureStarts = new int[4];
    private readonly int[] _failureEnds = new int[4];
    private int _entryStart;
    private int _entryEnd;
    private int _exceptionHandler;
    private int _catchFilter;
    private int _entryFilter;
    private int _frame;
    private int _spillBase;
    private int _returnSlot;
    private int _currentFunction;
    private bool _emittingFunclet;
    private readonly List<int> _cleanups = [];
    private readonly List<NormalExit> _normalExits = [];
    private readonly List<LoopTarget> _loopTargets = [];

    private sealed record NormalExit(int Cleanup, ImmutableArray<int> RetiredRoots);

    private sealed class LoopTarget(int exitDepth)
    {
        internal int ExitDepth { get; } = exitDepth;
        internal List<int> Breaks { get; } = [];
        internal List<int> Continues { get; } = [];
    }

    internal PeCompiler(BoundProgram program, bool managedNodes, bool library = false,
        IReadOnlyDictionary<int, NativeImportBinding>? imports = null, bool[]? sourceDependent = null,
        CancellationToken cancellationToken = default)
    {
        _program = program;
        _sourceDependent = sourceDependent;
        _sourceSlots = sourceDependent is null ? [] : new int[program.Functions.Length];
        _checkedExceptions = program.RuntimePolicy == BoundRuntimePolicy.CheckedExceptions;
        _library = library;
        _nativeImports = imports;
        _cancellationToken = cancellationToken;
        _starts = new int[program.Functions.Length];
        _scopes = new List<NativeExceptionScope>[program.Functions.Length];
        for (var index = 0; index < _scopes.Length; index++)
        {
            _scopes[index] = [];
        }
        _ends = new int[program.Functions.Length];
        _frames = new int[program.Functions.Length];
        _slots = new int[program.Functions.Length][];
        _spillBases = new int[program.Functions.Length];
        _outSlots = new int[program.Functions.Length][];
        _returnSlots = new int[program.Functions.Length];
        _edgeCopySlots = new int[program.Functions.Length][];
        for (var index = 0; index < program.Functions.Length; index++)
        {
            _outSlots[index] = [];
            _edgeCopySlots[index] = [];
        }
        _liveSlots = new int[program.Functions.Length][];
        _initializationFlags = new int[program.Functions.Length][];
        _managedRootFrames = new int[program.Functions.Length];
        _managedRootFlags = new Dictionary<int, int>[program.Functions.Length];
        for (var index = 0; index < _managedRootFlags.Length; index++)
        {
            _managedRootFlags[index] = [];
        }
        _constructedFieldSlots = new int[program.Functions.Length][];
        _replacementSlots = new int[program.Functions.Length];
        _cleanupArgumentSlots = new int[program.Functions.Length];
        _bufferOffsets = sourceDependent is null ? new int[program.Buffers.Length] : [];
        if (sourceDependent is not null)
        {
            if (!program.NativeUtf16ZLiterals.IsEmpty || !program.NativeUtf8ZLiterals.IsEmpty)
            {
                throw new NotSupportedException("Native UTF-NUL image literals require AOT native-import emission.");
            }
            // Invocation storage has no image data, address fixups, or retained runtime state.
            _data = [];
            _nativeUtf16ZOffsets = [];
            _nativeUtf8ZOffsets = [];
            _managedRuntimeOffset = -1;
            return;
        }
        if (!NativeStringZPoolContract.IsValid(program.NativeUtf16ZLiterals, program.NativeUtf8ZLiterals))
        {
            throw new InvalidOperationException("The verified native UTF-NUL literal pools are invalid.");
        }
        var literalBytes = 0;
        _nativeUtf16ZOffsets = new int[program.NativeUtf16ZLiterals.Length];
        for (var index = 0; index < program.NativeUtf16ZLiterals.Length; index++)
        {
            _nativeUtf16ZOffsets[index] = literalBytes;
            literalBytes = checked(literalBytes
                + ((program.NativeUtf16ZLiterals[index].Length + 1) * sizeof(char)));
        }
        _nativeUtf8ZOffsets = new int[program.NativeUtf8ZLiterals.Length];
        for (var index = 0; index < program.NativeUtf8ZLiterals.Length; index++)
        {
            _nativeUtf8ZOffsets[index] = literalBytes;
            literalBytes = checked(literalBytes + program.NativeUtf8ZLiterals[index].Length + 1);
        }
        var literalPrefixBytes = checked((literalBytes + 3) & ~3);
        var readOnlyBufferBytes = 0;
        foreach (var buffer in program.Buffers)
        {
            if (!buffer.IsWritable)
            {
                readOnlyBufferBytes = checked(readOnlyBufferBytes + (buffer.Values.Length * 4));
            }
        }
        _readOnlyDataLength = checked(literalPrefixBytes + readOnlyBufferBytes);
        var readOnlyAt = literalPrefixBytes;
        var writableAt = _readOnlyDataLength;
        for (var index = 0; index < program.Buffers.Length; index++)
        {
            var buffer = program.Buffers[index];
            _bufferOffsets[index] = buffer.IsWritable ? writableAt : readOnlyAt;
            if (buffer.IsWritable)
            {
                writableAt = checked(writableAt + (buffer.Values.Length * 4));
            }
            else
            {
                readOnlyAt = checked(readOnlyAt + (buffer.Values.Length * 4));
            }
        }
        _hostDataLength = checked(writableAt - literalPrefixBytes);
        _managedNodes = managedNodes;
        _managedRuntimeOffset = _managedNodes ? (writableAt + 7) & ~7 : -1;
        _data = new byte[_managedNodes ? _managedRuntimeOffset + 24 : writableAt];
        for (var index = 0; index < program.NativeUtf16ZLiterals.Length; index++)
        {
            var literal = program.NativeUtf16ZLiterals[index];
            for (var codeUnit = 0; codeUnit < literal.Length; codeUnit++)
            {
                var offset = _nativeUtf16ZOffsets[index] + (codeUnit * sizeof(char));
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                    _data.AsSpan(offset, sizeof(char)), literal[codeUnit]);
            }
            var terminatorOffset = _nativeUtf16ZOffsets[index] + (literal.Length * sizeof(char));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                _data.AsSpan(terminatorOffset, sizeof(char)), 0);
        }
        for (var index = 0; index < program.NativeUtf8ZLiterals.Length; index++)
        {
            var literal = program.NativeUtf8ZLiterals[index];
            var offset = _nativeUtf8ZOffsets[index];
            for (var character = 0; character < literal.Length; character++)
            {
                _data[offset + character] = checked((byte)literal[character]);
            }
            _data[offset + literal.Length] = 0;
        }
        for (var index = 0; index < program.Buffers.Length; index++)
        {
            var buffer = program.Buffers[index];
            for (var element = 0; element < buffer.Values.Length; element++)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                    _data.AsSpan(_bufferOffsets[index] + (element * 4), 4), buffer.Values[element]);
            }
        }
    }
    internal NativeCode Compile()
    {
        PrepareFrames();
        var (processEntry, processEnd) = EmitEntryAndFailureHelpers();
        EmitSourceFunctions();
        EmitCleanupFunclets();
        EmitManagedCollector();
        EmitManagedDescriptors();

        var binary128FunctionRanges = ImmutableArray<NativeFunctionRange>.Empty;
        if (_binary128Calls.Count != 0)
        {
            var runtime = s_binary128Runtime.Value;
            var runtimeStart = _code.Position;
            _code.Bytes(runtime.Code.Code);
            foreach (var (offset, function) in _binary128Calls)
            {
                _code.PatchRelative(offset, runtimeStart + runtime.FunctionOffsets[function]);
            }
            foreach (var offset in runtime.Code.ExitProcessFixups)
            {
                _imports.Add(runtimeStart + offset);
            }
            foreach (var import in runtime.Code.NativeImports)
            {
                _nativeImportFixups.Add(import with {
                    DisplacementOffset = runtimeStart + import.DisplacementOffset,
                });
            }
            foreach (var fixup in runtime.Code.DataFixups)
            {
                _dataFixups.Add(fixup with {
                    DisplacementOffset = runtimeStart + fixup.DisplacementOffset,
                });
            }
            binary128FunctionRanges = [
                .. runtime.Code.Functions.Select(function => function with {
                    Start = runtimeStart + function.Start,
                    End = runtimeStart + function.End,
                    ExceptionHandler = function.ExceptionHandler == 0
                        ? 0 : runtimeStart + function.ExceptionHandler,
                    ExceptionScopes = [
                        .. function.ExceptionScopes.Select(scope => scope with {
                            Begin = runtimeStart + scope.Begin,
                            End = runtimeStart + scope.End,
                            Handler = scope.Handler == 0 ? 0 : runtimeStart + scope.Handler,
                        }),
                    ],
                }),
            ];
        }

        var bidDecimalFunctionRanges = ImmutableArray<NativeFunctionRange>.Empty;
        if (_bidDecimalCalls.Count != 0)
        {
            var runtime = s_bidDecimalRuntime.Value;
            var runtimeStart = _code.Position;
            _code.Bytes(runtime.Code.Code);
            foreach (var (offset, function) in _bidDecimalCalls)
            {
                _code.PatchRelative(offset, runtimeStart + runtime.FunctionOffsets[function]);
            }
            foreach (var offset in runtime.Code.ExitProcessFixups)
            {
                _imports.Add(runtimeStart + offset);
            }
            foreach (var import in runtime.Code.NativeImports)
            {
                _nativeImportFixups.Add(import with {
                    DisplacementOffset = runtimeStart + import.DisplacementOffset,
                });
            }
            foreach (var fixup in runtime.Code.DataFixups)
            {
                _dataFixups.Add(fixup with {
                    DisplacementOffset = runtimeStart + fixup.DisplacementOffset,
                });
            }
            bidDecimalFunctionRanges = [
                .. runtime.Code.Functions.Select(function => function with {
                    Start = runtimeStart + function.Start,
                    End = runtimeStart + function.End,
                    ExceptionHandler = function.ExceptionHandler == 0
                        ? 0 : runtimeStart + function.ExceptionHandler,
                    ExceptionScopes = [
                        .. function.ExceptionScopes.Select(scope => scope with {
                            Begin = runtimeStart + scope.Begin,
                            End = runtimeStart + scope.End,
                            Handler = scope.Handler == 0 ? 0 : runtimeStart + scope.Handler,
                        }),
                    ],
                }),
            ];
        }

        foreach (var (offset, target) in _calls)
        {
            _code.PatchRelative(offset, _starts[target]);
        }
        foreach (var (offset, target) in _runtimeCalls)
        {
            _code.PatchRelative(offset, _failureStarts[target]);
        }
        foreach (var (offset, target) in _cleanupCalls)
        {
            _code.PatchRelative(offset, _funclets[target].Start);
        }
        if (_checkedExceptions)
        {
            _exceptionHandler = _code.Position;
            _nativeImportFixups.Add(new NativeImportFixup(_code.ImportJump(), "NTDLL.dll", "__C_specific_handler"));
            _catchFilter = _code.Position;
            EmitExceptionFilter(storeReason: false);
            _entryFilter = _code.Position;
            EmitExceptionFilter(storeReason: true);
        }
        EmitExternalFunctionAddressThunks();
        foreach (var (offset, target) in _functionAddressFixups)
        {
            var targetStart = _program.Functions[target].IsExternal
                ? _externalFunctionAddressStarts[target]
                : _starts[target];
            _code.PatchRelative(offset, targetStart);
        }
        if (_code.Position > MaximumText)
        {
            throw new NotSupportedException("Native text exceeds the 8 MiB image ceiling.");
        }
        var functionCount = checked(binary128FunctionRanges.Length + bidDecimalFunctionRanges.Length
            + (_library ? 0 : 2) + _failureStarts.Length + _funclets.Count
            + (_managedNodes ? 1 : 0));
        var sourceFunctionOffsets = ImmutableArray.CreateBuilder<int>(_program.Functions.Length);
        for (var i = 0; i < _program.Functions.Length; i++)
        {
            var function = _program.Functions[i];
            sourceFunctionOffsets.Add(function.IsExternal ? -1 : _starts[i]);
            if (!function.IsExternal)
            {
                functionCount = checked(functionCount + 1);
            }
        }
        var functions = ImmutableArray.CreateBuilder<NativeFunctionRange>(functionCount);
        if (!_library)
        {
            functions.Add(new NativeFunctionRange(_entryStart, _entryEnd, 40) {
                ExceptionHandler = _checkedExceptions ? _exceptionHandler : 0,
                ExceptionScopes = [.. _entryScopes.Select(scope => scope with { Handler = _entryFilter })],
            });
            functions.Add(new NativeFunctionRange(processEntry, processEnd, 40));
        }
        for (var i = 0; i < _failureStarts.Length; i++)
        {
            functions.Add(new NativeFunctionRange(_failureStarts[i], _failureEnds[i], _checkedExceptions ? 72 : 40));
        }
        for (var i = 0; i < _starts.Length; i++)
        {
            if (!_program.Functions[i].IsExternal)
            {
                functions.Add(new NativeFunctionRange(_starts[i], _ends[i], _frames[i]) {
                    UsesFramePointer = true,
                    ExceptionHandler = _checkedExceptions ? _exceptionHandler : 0,
                    ExceptionScopes = [.. _scopes[i].Select(scope => scope with {
                        Handler = scope.Target != 0 ? _catchFilter : _funclets[-scope.Handler - 1].Start,
                    })],
                });
            }
        }
        foreach (var (_, function, start, end) in _funclets)
        {
            functions.Add(new NativeFunctionRange(start, end, _frames[function]) {
                UsesFramePointer = true,
                UsesParentFrame = true,
            });
        }
        if (_managedNodes)
        {
            functions.Add(new NativeFunctionRange(_managedCollectorStart, _managedCollectorEnd, 120));
        }
        functions.AddRange(binary128FunctionRanges);
        functions.AddRange(bidDecimalFunctionRanges);

        return new NativeCode(_code.ToArray(), _entryStart,
            functions.MoveToImmutable(),
            [.. _imports]) {
            AotEntryPoint = processEntry,
            Data = _data,
            ReadOnlyDataLength = _readOnlyDataLength,
            BufferOffsets = [.. _bufferOffsets],
            SourceFunctionOffsets = sourceFunctionOffsets.MoveToImmutable(),
            DataFixups = [.. _dataFixups],
            NativeImports = [.. _nativeImportFixups],
            ManagedRuntimeOffset = _managedRuntimeOffset,
            HostDataLength = _hostDataLength,
        };
    }

    private void EmitExternalFunctionAddressThunks()
    {
        foreach (var function in _externalFunctionAddresses.OrderBy(static function => function))
        {
            if (_nativeImports is null || !_nativeImports.TryGetValue(function, out var import))
            {
                throw new InvalidOperationException(
                    "External native function address has no verified native import binding.");
            }
            _externalFunctionAddressStarts.Add(function, _code.Position);
            _nativeImportFixups.Add(new NativeImportFixup(
                _code.ImportJump(), import.DllName, import.ExportName, function));
        }
    }

    private void PrepareFrames()
    {
        var maximumIndirectCleanupSize = 0;
        for (var type = 0; type < _program.Structures.Length; type++)
        {
            var structureType = (PrimitiveType)(100 + type);
            if (_program.Structures[type].CleanupMethod is not null && IsIndirect(structureType))
            {
                maximumIndirectCleanupSize = Math.Max(maximumIndirectCleanupSize, ScratchSize(structureType));
            }
        }
        for (var i = 0; i < _program.Functions.Length; i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var function = _program.Functions[i];
            if (function.IsExternal)
            {
                continue;
            }
            var maximumArguments = 0;
            var needsReturnSnapshot = false;
            var spillSlots = StatementSpills(function.Body, ref maximumArguments, ref needsReturnSnapshot,
                _checkedExceptions && _managedNodes);
            if (function.IndexedBody is TrRmScalarGraph indexedBody)
            {
                foreach (var block in indexedBody.Blocks)
                {
                    foreach (var operation in block.Operations)
                    {
                        spillSlots = Math.Max(spillSlots, StatementSpills(operation, ref maximumArguments));
                    }
                }
            }
            if (function.Intrinsic == BoundIntrinsic.ClassTryCreate)
            {
                maximumArguments = Math.Max(maximumArguments, function.ParameterCount);
            }
            if (function.Intrinsic == BoundIntrinsic.NativeVTableCall)
            {
                maximumArguments = Math.Max(maximumArguments, function.ParameterCount);
            }
            var outgoingSize = 32 + (8 * Math.Max(0, maximumArguments - 4));
            var at = (outgoingSize + 15) & ~15;
            if (_sourceDependent?[i] == true)
            {
                _sourceSlots[i] = at;
                at += 16;
            }
            var slots = new int[function.Variables.Length];
            for (var variable = 0; variable < slots.Length; variable++)
            {
                if ((IsIndirect(function.Variables[variable].Type)
                    || function.Variables[variable].Type is PrimitiveType.ReadonlyInt32BufferView
                        or PrimitiveType.MutableInt32BufferView)
                    && !function.Variables[variable].IsOut)
                {
                    at = (at + 15) & ~15;
                }
                slots[variable] = at;
                at += function.Variables[variable].NativeFixedArrayLayout?.ByteExtent
                    ?? ((IsIndirect(function.Variables[variable].Type)
                            && IsStruct(function.Variables[variable].Type))
                    ? Size(function.Variables[variable].Type) : (IsIndirect(function.Variables[variable].Type)
                    || function.Variables[variable].Type is PrimitiveType.ReadonlyInt32BufferView
                        or PrimitiveType.MutableInt32BufferView)
                    && !function.Variables[variable].IsOut ? 16 : 8);
                at = (at + 7) & ~7;
            }
            _slots[i] = slots;
            _spillBases[i] = AlignOffset(at, 16);
            at = checked(_spillBases[i] + (16 * spillSlots));
            var outputSlots = new int[function.Variables.Length];
            Array.Fill(outputSlots, -1);
            for (var variable = 0; variable < function.Variables.Length; variable++)
            {
                if (!function.Variables[variable].IsOut)
                {
                    continue;
                }
                var type = function.Variables[variable].Type;
                at = AlignOffset(at, ScratchAlignment(type));
                outputSlots[variable] = at;
                at = checked(at + ScratchSize(type));
            }
            _outSlots[i] = outputSlots;
            at = AlignOffset(at, 16);
            var edgeParameters = 0;
            if (function.IndexedBody is TrRmScalarGraph indexed)
            {
                foreach (var block in indexed.Blocks)
                {
                    edgeParameters = Math.Max(edgeParameters, block.Parameters.Length);
                }
            }
            var edgeCopySizes = new int[edgeParameters];
            var edgeCopyAlignments = new int[edgeParameters];
            Array.Fill(edgeCopyAlignments, 8);
            if (function.IndexedBody is TrRmScalarGraph edgeGraph)
            {
                foreach (var block in edgeGraph.Blocks)
                {
                    for (var parameter = 0; parameter < block.Parameters.Length; parameter++)
                    {
                        var type = edgeGraph.Types[block.Parameters[parameter] - 1];
                        edgeCopySizes[parameter] = Math.Max(edgeCopySizes[parameter], ScratchSize(type));
                        edgeCopyAlignments[parameter] = Math.Max(edgeCopyAlignments[parameter],
                            ScratchAlignment(type));
                    }
                }
            }
            var edgeCopySlots = new int[edgeParameters];
            for (var parameter = 0; parameter < edgeParameters; parameter++)
            {
                at = AlignOffset(at, edgeCopyAlignments[parameter]);
                edgeCopySlots[parameter] = at;
                at = checked(at + edgeCopySizes[parameter]);
            }
            _edgeCopySlots[i] = edgeCopySlots;
            at = AlignOffset(at, 16);
            _returnSlots[i] = at;
            var hasOutputParameters = false;
            var parameterOffset = function.IsConstructor ? 1 : 0;
            for (var parameter = parameterOffset;
                parameter < function.ParameterCount + parameterOffset; parameter++)
            {
                hasOutputParameters |= function.Variables[parameter].IsOut;
            }
            var snapshotsOutputReturn = hasOutputParameters
                && function.ReturnType is not (PrimitiveType.Bool or PrimitiveType.Void);
            // Publishing outputs uses the return registers, so preserve non-Boolean results first.
            var required = _returnSlots[i]
                + (IsIndirectResult(function.ReturnType) || snapshotsOutputReturn ? 8L : 0L);
            if (needsReturnSnapshot || function.ReturnType == PrimitiveType.Float128)
            {
                var returnSnapshot = AlignOffset(_returnSlots[i]
                    + (IsIndirectResult(function.ReturnType) ? 8 : 0), 16);
                required = Math.Max(required, returnSnapshot + (long)ScratchSize(function.ReturnType));
            }
            var live = new int[function.Variables.Length];
            Array.Fill(live, -1);
            var hasUnique = false;
            var replacementSize = 8;
            for (var variable = 0; variable < function.Variables.Length; variable++)
            {
                var type = function.Variables[variable].Type;
                if (IsUnique(type))
                {
                    hasUnique = true;
                    replacementSize = Math.Max(replacementSize, ScratchSize(type));
                    if (!(variable == 0 && function.DeclaringStruct is not null && !function.IsStatic))
                    {
                        live[variable] = checked((int)required);
                        required += 8;
                    }
                }
            }
            _liveSlots[i] = live;
            var initializationTests = InitializationTestVariables(function);
            var initializationFlags = new int[function.Variables.Length];
            Array.Fill(initializationFlags, -1);
            for (var variable = 0; variable < function.Variables.Length; variable++)
            {
                var slot = function.Variables[variable];
                if (initializationTests.Contains(variable)
                    && !ManagedClassTypes.IsManaged(slot.Type) && !IsUnique(slot.Type))
                {
                    initializationFlags[variable] = checked((int)required);
                    required += 4;
                }
            }
            _initializationFlags[i] = initializationFlags;
            var constructed = function.IsConstructor
                ? new int[_program.Structures[(int)function.ReturnType - 100].Fields.Length] : [];
            Array.Fill(constructed, -1);
            for (var field = 0; field < constructed.Length; field++)
            {
                if (IsUnique(_program.Structures[(int)function.ReturnType - 100].Fields[field].Type))
                {
                    constructed[field] = checked((int)required);
                    required += 8;
                }
            }
            _constructedFieldSlots[i] = constructed;
            _replacementSlots[i] = checked((int)((required + 15) & ~15L));
            if (hasUnique)
            {
                required = _replacementSlots[i] + (long)replacementSize;
            }
            _cleanupArgumentSlots[i] = -1;
            if (maximumIndirectCleanupSize != 0)
            {
                _cleanupArgumentSlots[i] = checked((int)((required + 15) & ~15L));
                required = _cleanupArgumentSlots[i] + (long)maximumIndirectCleanupSize;
            }
            required = PrepareManagedRoots(i, function, spillSlots, needsReturnSnapshot, required);
            required += 8; // Saved RBP lives above the addressable frame slots.
            if (required > MaximumFrame)
            {
                throw new NotSupportedException("Native frame exceeds the 3840-byte page-safe ceiling.");
            }
            var frame = AlignFrame((int)required);
            if (frame > MaximumFrame)
            {
                throw new NotSupportedException("Native frame exceeds the 3840-byte page-safe ceiling.");
            }
            _frames[i] = frame;
        }
    }

    private void EmitSourceFunctions()
    {
        for (var i = 0; i < _program.Functions.Length; i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var function = _program.Functions[i];
            if (function.IsExternal)
            {
                continue;
            }
            _starts[i] = _code.Position;
            _currentFunction = i;
            _frame = _frames[i];
            _spillBase = _spillBases[i];
            _returnSlot = _returnSlots[i];
            _cleanups.Clear();
            _normalExits.Clear();
            _loopTargets.Clear();
            _code.Byte(0x55); // push rbp
            Prolog(_frame - 8);
            _code.Bytes(0x48, 0x89, 0xE5); // mov rbp,rsp
            _code.UseFramePointer = true;
            var sourceHidden = _sourceDependent?[i] == true ? 2 : 0;
            if (sourceHidden != 0 && function.NativeFunctionPointerType is not null)
            {
                throw new NotSupportedException(
                    "A native-callable function cannot require an invocation-scoped source grant.");
            }
            if (sourceHidden != 0)
            {
                // RCX/RDX are volatile; preserve the complete context before emitting any calls.
                _code.StoreRegister(0, _sourceSlots[i], true);
                _code.StoreRegister(1, _sourceSlots[i] + 8, false);
            }
            foreach (var live in _liveSlots[i])
            {
                if (live >= 0)
                {
                    _code.Clear(live);
                }
            }
            foreach (var flag in _initializationFlags[i])
            {
                if (flag >= 0)
                {
                    _code.Clear(flag);
                }
            }
            foreach (var field in _constructedFieldSlots[i])
            {
                if (field >= 0)
                {
                    _code.Clear(field);
                }
            }
            var hiddenReturn = IsIndirectResult(function.ReturnType) ? 1 : 0;
            var hidden = hiddenReturn + sourceHidden;
            if (hiddenReturn != 0)
            {
                _code.StoreRegister(0, _returnSlot, true);
            }
            if (function.IsConstructor)
            {
                if (IsIndirectResult(function.ReturnType))
                {
                    ZeroStruct(Slot(0), Size(function.ReturnType));
                }
                else
                {
                    _code.Bytes(0x31, 0xC0);
                    _code.Store64(Slot(0));
                }
            }
            for (var argument = 0; argument < function.ParameterCount; argument++)
            {
                var slot = argument + (function.IsConstructor ? 1 : 0);
                var abiArgument = argument + hidden;
                var type = function.Variables[slot].Type;
                var isAddress = function.Variables[slot].IsOut || function.Variables[slot].IsImplicitRef;
                if (abiArgument < 4)
                {
                    if (!isAddress && IsFloatingCallCarrier(type))
                    {
                        _code.StoreFloatingRegister(abiArgument, Slot(slot),
                            type == PrimitiveType.Float64);
                        if (type is PrimitiveType.Float16 or PrimitiveType.BFloat16)
                        {
                            _code.LoadUnsigned(Slot(slot), sizeof(ushort));
                            _code.Store(Slot(slot));
                        }
                    }
                    else
                    {
                        _code.StoreRegister(abiArgument, Slot(slot),
                            IsStruct(type) || IsWide(type) || IsIndirect(type) || function.Variables[slot].IsOut
                                || function.Variables[slot].IsImplicitRef);
                    }
                }
                else
                {
                    // At entry [rsp] is the return address, [rsp+8..39] is caller home space.
                    _code.Load64(_frame + 40 + (8 * (abiArgument - 4)));
                    _code.Store64(Slot(slot));
                }
            }
            for (var argument = 0; argument < function.ParameterCount; argument++)
            {
                var slot = argument + (function.IsConstructor ? 1 : 0);
                var type = function.Variables[slot].Type;
                if (IsIndirect(type) && !function.Variables[slot].IsOut
                    && !function.Variables[slot].IsImplicitRef
                    && !(slot == 0 && IsBorrowedReceiver()))
                {
                    _code.Load64(Slot(slot));
                    CopyToFrame(Slot(slot), Size(type));
                }
                if (!function.Variables[slot].IsOut && !function.Variables[slot].IsImplicitRef
                    && function.Variables[slot].Type is PrimitiveType.Bool or PrimitiveType.Char16)
                {
                    _code.Load(Slot(slot));
                    _code.Bytes(0x0F, function.Variables[slot].Type == PrimitiveType.Bool ? (byte)0xB6 : (byte)0xB7, 0xC0);
                    _code.Store(Slot(slot));
                }
            }
            for (var argument = 0; argument < function.ParameterCount; argument++)
            {
                var slot = argument + (function.IsConstructor ? 1 : 0);
                if (_liveSlots[i][slot] >= 0 && !function.Variables[slot].IsOut)
                {
                    _code.Set(_liveSlots[i][slot]);
                }
                var initializationFlag = _initializationFlags[i][slot];
                if (initializationFlag >= 0 && !function.Variables[slot].IsOut)
                {
                    _code.Set(initializationFlag);
                }
            }
            if (function.Intrinsic is BoundIntrinsic.None or BoundIntrinsic.ClassTryCreate)
            {
                RegisterManagedRoots(i);
            }
            var rootsBegin = _code.Position;
            if (function.Intrinsic != BoundIntrinsic.None)
            {
                if (function.Intrinsic == BoundIntrinsic.ClassTryCreate)
                {
                    EmitManagedClassFactory(function);
                }
                else if (function.Intrinsic == BoundIntrinsic.NativeVTableCall)
                {
                    EmitNativeVTableMethodFunction(function);
                }
                else if (function.Intrinsic >= BoundIntrinsic.NodeTryCreate)
                {
                    EmitManagedIntrinsic(function.Intrinsic);
                }
                else
                {
                    EmitBufferIntrinsic(function.Intrinsic);
                }
                if (function.Intrinsic == BoundIntrinsic.ClassTryCreate)
                {
                    UnregisterManagedRoots(i);
                }
                Epilog();
            }
            else
            {
                if (function.IndexedBody is TrRmScalarGraph graph)
                {
                    EmitIndexedBody(graph);
                }
                else
                {
                    Statement(function.Body);
                }
                if (function.ReturnType == PrimitiveType.Void)
                {
                    UnregisterManagedRoots(i);
                    Epilog();
                }
            }
            if (_checkedExceptions)
            {
                // Keep a checked-helper call continuation inside its function range for unwind lookup.
                _code.Byte(0x90);
            }
            if (_checkedExceptions && _managedRootFlags[i].Count != 0)
            {
                var unwind = _funclets.Count;
                _funclets.Add((new BoundBlock([], function.Span), i, 0, 0));
                _ = _unwindRootFrames.Add(unwind);
                _scopes[i].Add(new NativeExceptionScope(rootsBegin, _code.Position, -unwind - 1, 0));
            }
            _ends[i] = _code.Position;
            if (_code.Position > MaximumText)
            {
                throw new NotSupportedException("Native text exceeds the 8 MiB image ceiling.");
            }
        }
    }

    private void EmitCleanupFunclets()
    {
        for (var index = 0; index < _funclets.Count; index++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var (body, function, _, _) = _funclets[index];
            _currentFunction = function;
            _frame = _frames[function];
            _spillBase = _spillBases[function];
            _returnSlot = _returnSlots[function];
            var start = _code.Position;
            _code.Byte(0x55); // push rbp
            Prolog(_frame - 8);
            _code.Bytes(0x48, 0x89, 0xD5); // mov rbp,rdx: the source function's frame base
            _emittingFunclet = true;
            _normalExits.Clear();
            _loopTargets.Clear();
            Statement(body);
            if (_stagedOwners.TryGetValue(index, out var owners))
            {
                for (var owner = owners.Count - 1; owner >= 0; owner--)
                {
                    var (value, live, type) = owners[owner];
                    _code.Load(live);
                    _code.Bytes(0x85, 0xC0);
                    var vacant = _code.JumpIf(0x84);
                    _code.Clear(live);
                    EmitDropValue(value, type);
                    _code.PatchRelative(vacant, _code.Position);
                }
            }
            if (_managedStagedRoots.TryGetValue(index, out var roots))
            {
                foreach (var slot in roots)
                {
                    SetManagedRoot(slot, active: false);
                }
            }
            if (_managedAbandonedFactories.TryGetValue(index, out var provisional))
            {
                SetManagedRoot(provisional, active: false);
                EmitManagedAbandon(provisional);
            }
            if (_unwindRootFrames.Contains(index))
            {
                UnregisterManagedRoots(function);
            }
            _emittingFunclet = false;
            Epilog();
            _funclets[index] = (body, function, start, _code.Position);
            if (_code.Position > MaximumText)
            {
                throw new NotSupportedException("Native text exceeds the 8 MiB image ceiling.");
            }
        }
    }

    private (int ProcessEntry, int ProcessEnd) EmitEntryAndFailureHelpers()
    {
        if (!_library)
        {
            _entryStart = _code.Position;
            Prolog(40);
            var protectedStart = _code.Position;
            _calls.Add((_code.RelativeCall(), _program.EntryPoint));
            if (_managedNodes)
            {
                _code.Bytes(0x89, 0x44, 0x24, 0x20);
                _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _managedRuntimeOffset));
                _code.Bytes(0x48, 0x89, 0xD1);
                _managedCollectorCalls.Add(_code.RelativeCall());
                _code.Bytes(0x8B, 0x44, 0x24, 0x20);
            }
            if (_checkedExceptions)
            {
                _code.Byte(0x90); // keep the protected call continuation outside the epilog
            }
            var protectedEnd = _code.Position;
            if (_program.Functions[_program.EntryPoint].ReturnType == PrimitiveType.Void)
            {
                _code.Bytes(0x31, 0xC0); // xor eax,eax
            }
            _frame = 40;
            Epilog();
            if (_checkedExceptions)
            {
                var handled = _code.Position;
                _code.Bytes(0x8B, 0x4C, 0x24, 0x20); // recorded failure reason
                _code.Bytes(0x81, 0xC9, 0x00, 0x00, 0x54, 0xE0); // 0xE0540000 | reason
                _imports.Add(_code.ImportCall());
                _code.Byte(0xCC);
                _entryScopes.Add(new NativeExceptionScope(protectedStart, protectedEnd, 0, handled));
            }
            _entryEnd = _code.Position;
        }

        var processEntry = _code.Position;
        if (!_library)
        {
            Prolog(40);
            _code.PatchRelative(_code.RelativeCall(), _entryStart);
            _code.Bytes(0x89, 0xC1); // mov ecx,eax
            _imports.Add(_code.ImportCall());
            _code.Byte(0xCC);
        }
        var processEnd = _code.Position;

        for (var failure = 0; failure < _failureStarts.Length; failure++)
        {
            _failureStarts[failure] = _code.Position;
            if (_checkedExceptions)
            {
                Prolog(72);
                _code.Bytes(0x48, 0xB8);
                _code.Int64(0x54455252554D0001);
                _code.Bytes(0x48, 0x89, 0x44, 0x24, 0x20);
                _code.Bytes(0x48, 0xC7, 0x44, 0x24, 0x28, 0x01, 0x00, 0x00, 0x00);
                _code.Bytes(0x48, 0xC7, 0x44, 0x24, 0x30);
                _code.Int32(failure + 1);
                _code.Byte(0xB9);
                _code.Int32(unchecked((int)0xE0540092));
                _code.Bytes(0xBA, 0x01, 0x00, 0x00, 0x00); // EXCEPTION_NONCONTINUABLE
                _code.Bytes(0x41, 0xB8, 0x03, 0x00, 0x00, 0x00);
                _code.Bytes(0x4C, 0x8D, 0x4C, 0x24, 0x20);
                _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "RaiseException"));
            }
            else
            {
                Prolog(40);
                _code.Byte(0xB9);
                _code.Int32(unchecked((int)0xE0540001) + failure);
                _imports.Add(_code.ImportCall());
            }
            _code.Byte(0xCC);
            _failureEnds[failure] = _code.Position;
        }
        return (processEntry, processEnd);
    }

    private static int AlignFrame(int bytes) => ((bytes + 8 + 15) & ~15) - 8;

    private int PendingReturnSlot() => (_returnSlot
        + (IsIndirectResult(_program.Functions[_currentFunction].ReturnType) ? 8 : 0) + 15) & ~15;

    private int StatementSpills(BoundStatement statement, ref int maximumArguments)
    {
        var ignoredCleanup = false;
        return StatementSpills(statement, ref maximumArguments, ref ignoredCleanup, managedScopes: false);
    }

    private int StatementSpills(BoundStatement statement, ref int maximumArguments,
        ref bool containsCleanup, bool managedScopes)
    {
        switch (statement)
        {
            case BoundBlock block:
                containsCleanup |= managedScopes && !block.RetiredManagedLocals.IsEmpty;
                var maximum = 0;
                foreach (var child in block.Statements)
                {
                    maximum = Math.Max(maximum,
                        StatementSpills(child, ref maximumArguments, ref containsCleanup, managedScopes));
                }
                return maximum;
            case BoundLocal local:
                return ExpressionSpills(local.Initializer, ref maximumArguments);
            case BoundViewScope view:
                return Math.Max(2, Math.Max(ExpressionSpills(view.Start, ref maximumArguments),
                    Math.Max(1 + ExpressionSpills(view.RangeEnd ?? view.Length, ref maximumArguments),
                        StatementSpills(view.Body, ref maximumArguments, ref containsCleanup, managedScopes))));
            case BoundViewSet write:
                return Math.Max(2, Math.Max(ExpressionSpills(write.Index, ref maximumArguments),
                    1 + ExpressionSpills(write.Value, ref maximumArguments)));
            case BoundAssignment assignment:
                return Math.Max(ExpressionSpills(assignment.Value, ref maximumArguments),
                    HasDeclaredCleanup(assignment.Value.Type)
                        ? CleanupSpills(ref maximumArguments) : 0);
            case BoundOptionalAssignment optionalAssignment:
                return optionalAssignment.Place is BoundFieldRead field
                    ? Math.Max(ExpressionSpills(field.Receiver, ref maximumArguments),
                        1 + ExpressionSpills(optionalAssignment.Value, ref maximumArguments))
                    : ExpressionSpills(optionalAssignment.Value, ref maximumArguments);
            case BoundDrop drop:
                return drop.CleanupFunction >= 0 ? CleanupSpills(ref maximumArguments) : 0;
            case BoundLifetimeEnd end:
                return end.CleanupFunction >= 0 ? CleanupSpills(ref maximumArguments) : 0;
            case BoundFieldAssignment assignment:
                return Math.Max(2, 1 + ExpressionSpills(assignment.Value, ref maximumArguments));
            case BoundBufferAssignment assignment:
                return Math.Max(ExpressionSpills(assignment.Index, ref maximumArguments),
                    1 + ExpressionSpills(assignment.Value, ref maximumArguments));
            case BoundExpressionStatement expression:
                return ExpressionSpills(expression.Expression, ref maximumArguments);
            case BoundIf conditional:
                return Math.Max(ExpressionSpills(conditional.Condition, ref maximumArguments),
                    Math.Max(StatementSpills(conditional.Then, ref maximumArguments,
                            ref containsCleanup, managedScopes),
                        conditional.Else is null ? 0 : StatementSpills(conditional.Else, ref maximumArguments,
                            ref containsCleanup, managedScopes)));
            case BoundWhile loop:
                return Math.Max(ExpressionSpills(loop.Condition, ref maximumArguments),
                    Math.Max(StatementSpills(loop.Body, ref maximumArguments, ref containsCleanup, managedScopes),
                        loop.Iterator is null ? 0 : StatementSpills(loop.Iterator, ref maximumArguments,
                            ref containsCleanup, managedScopes)));
            case BoundTryFinally cleanup:
                containsCleanup = true;
                return Math.Max(StatementSpills(cleanup.Body, ref maximumArguments,
                        ref containsCleanup, managedScopes),
                    StatementSpills(cleanup.Finally, ref maximumArguments, ref containsCleanup, managedScopes));
            case BoundUnsafe unsafeBlock:
                return StatementSpills(unsafeBlock.Body, ref maximumArguments, ref containsCleanup, managedScopes);
            case BoundTryCatch recovery:
                containsCleanup |= recovery.Finally is not null;
                return Math.Max(StatementSpills(recovery.Body, ref maximumArguments,
                        ref containsCleanup, managedScopes),
                    Math.Max(StatementSpills(recovery.Catch, ref maximumArguments, ref containsCleanup, managedScopes),
                        recovery.Finally is null ? 0 : StatementSpills(recovery.Finally, ref maximumArguments,
                            ref containsCleanup, managedScopes)));
            case BoundRequire requirement:
                return ExpressionSpills(requirement.Condition, ref maximumArguments);
            case BoundReturn result when result.Value is BoundExpression value:
                return ExpressionSpills(value, ref maximumArguments);
            default:
                return 0;
        }
    }

    private int ExpressionSpills(BoundExpression expression, ref int maximumArguments)
    {
        switch (expression)
        {
            case BoundNativeFixedArrayInitialization fixedArray:
                var fixedArraySpills = 0;
                foreach (var element in fixedArray.Elements)
                {
                    fixedArraySpills = Math.Max(fixedArraySpills,
                        ExpressionSpills(element, ref maximumArguments));
                }
                return fixedArraySpills;
            case BoundNativeFixedArrayElement fixedArrayElement:
                return ExpressionSpills(fixedArrayElement.Index, ref maximumArguments);
            case BoundNativeFixedArrayPointerAddress pointerAddress:
                return ExpressionSpills(pointerAddress.Index, ref maximumArguments);
            case BoundNativeFixedArrayWrite fixedArrayWrite:
                return Math.Max(2, Math.Max(1 + ExpressionSpills(fixedArrayWrite.Index, ref maximumArguments),
                    1 + ExpressionSpills(fixedArrayWrite.Value, ref maximumArguments)));
            case BoundNativeStorageAddress address:
                return ExpressionSpills(address.Index, ref maximumArguments);
            case BoundNativeOutputReservation:
                return 0;
            case BoundNativePointerAddress:
                return 0;
            case BoundNativePointerRead pointerRead:
                return ExpressionSpills(pointerRead.Address, ref maximumArguments);
            case BoundReferenceFormation formation:
                return ExpressionSpills(formation.Place, ref maximumArguments);
            case BoundReferenceReborrow reborrow:
                return ExpressionSpills(reborrow.Descriptor, ref maximumArguments);
            case BoundReferenceRead referenceRead:
                return ExpressionSpills(referenceRead.Descriptor, ref maximumArguments);
            case BoundReferenceWrite referenceWrite:
                return Math.Max(2, Math.Max(1 + ExpressionSpills(referenceWrite.Descriptor, ref maximumArguments),
                    1 + ExpressionSpills(referenceWrite.Value, ref maximumArguments)));
            case BoundNativePointerWrite pointerWrite:
                return Math.Max(2, Math.Max(1 + ExpressionSpills(pointerWrite.Address, ref maximumArguments),
                    1 + ExpressionSpills(pointerWrite.Value, ref maximumArguments)));
            case BoundConstant constant:
                return IsIndirect(constant.Type) ? Math.Max(1, (Size(constant.Type) + 15) / 16) : 0;
            case BoundUnary unary:
                if (IsBidDecimal(unary.Type))
                {
                    return Math.Max(3, 1 + ExpressionSpills(unary.Operand, ref maximumArguments));
                }
                return Math.Max(PrimitiveTypeFacts.IsWideInteger(unary.Type) ? 2 : 0,
                    ExpressionSpills(unary.Operand, ref maximumArguments));
            case BoundUpdate update:
                var requiredUpdateSpills = PrimitiveTypeFacts.IsWideInteger(update.Type) ? 5
                    : update.Type is PrimitiveType.Float16 or PrimitiveType.BFloat16 ? 18
                    : PrimitiveTypeFacts.IsFloating(update.Type) ? 5 : 3;
                return Math.Max(requiredUpdateSpills, update.Place switch {
                    BoundFieldRead field => ExpressionSpills(field.Receiver, ref maximumArguments),
                    BoundBufferRead buffer => ExpressionSpills(buffer.Index, ref maximumArguments),
                    _ => 0,
                });
            case BoundCompoundAssignment compound:
                var requiredCompoundSpills = PrimitiveTypeFacts.IsWideInteger(compound.Type)
                    ? CompoundOperators.Binary(compound.Operator) switch {
                        TokenKind.Star => 7,
                        TokenKind.Slash or TokenKind.Percent => 9,
                        _ => 3,
                    }
                    : compound.Type is PrimitiveType.Float16 or PrimitiveType.BFloat16 ? 18
                    : PrimitiveTypeFacts.IsFloating(compound.Type)
                        ? compound.Type == PrimitiveType.Float128 ? 5
                            : CompoundOperators.Binary(compound.Operator) == TokenKind.Percent ? 6 : 5
                        : 3;
                return Math.Max(requiredCompoundSpills,
                    Math.Max(4 + ExpressionSpills(compound.Right, ref maximumArguments),
                        compound.Place switch {
                        BoundFieldRead field => ExpressionSpills(field.Receiver, ref maximumArguments),
                        BoundBufferRead buffer => ExpressionSpills(buffer.Index, ref maximumArguments),
                        _ => 0,
                    }));
            case BoundNominalUpdate update:
                maximumArguments = Math.Max(maximumArguments, IsIndirect(update.Type) ? 2 : 1);
                return Math.Max(4, update.Place is BoundFieldRead nominalField
                    ? ExpressionSpills(nominalField.Receiver, ref maximumArguments) : 0);
            case BoundCast cast:
                return Math.Max(IsIndirect(cast.Type) ? Math.Max(1, (Size(cast.Type) + 15) / 16) : 0,
                    Math.Max(RequiresFloatingEnvironment(cast.Operand.Type, cast.Type) ? 3 : 0,
                        ExpressionSpills(cast.Operand, ref maximumArguments)));
            case TrRmScalarConvert conversion:
                return ExpressionSpills(conversion.Operand, ref maximumArguments);
            case TrRmScalarOptionalLift lift:
                return 1 + ExpressionSpills(lift.Value, ref maximumArguments);
            case TrRmScalarOptionalNone none when IsIndirect(none.Type):
                return 1;
            case TrRmScalarOptionalPresence presence:
                return ExpressionSpills(presence.Optional, ref maximumArguments);
            case TrRmScalarOptionalEquality:
                return 2;
            case BoundOptionalLift lift:
                return ExpressionSpills(lift.Value, ref maximumArguments);
            case BoundCoalesce coalesce:
                return Math.Max(ExpressionSpills(coalesce.Optional, ref maximumArguments),
                    ExpressionSpills(coalesce.Fallback, ref maximumArguments));
            case BoundReadonlyProjection projection:
                return ExpressionSpills(projection.Value, ref maximumArguments);
            case BoundIsNull test:
                return ExpressionSpills(test.Value, ref maximumArguments);
            case BoundIsSome pattern:
                return ExpressionSpills(pattern.Value, ref maximumArguments);
            case BoundIsVariablePattern pattern:
                return ExpressionSpills(pattern.Value, ref maximumArguments);
            case BoundIsInitialized:
                return 0;
            case BoundFieldRead field:
                return ExpressionSpills(field.Receiver, ref maximumArguments);
            case BoundBufferRead buffer:
                return ExpressionSpills(buffer.Index, ref maximumArguments);
            case BoundViewRead view:
                return ExpressionSpills(view.Index, ref maximumArguments);
            case BoundSnapshotSpan:
                return 4;
            case BoundSpanLength length:
                return ExpressionSpills(length.View, ref maximumArguments);
            case BoundSpanRead read:
                return Math.Max(2, Math.Max(ExpressionSpills(read.View, ref maximumArguments),
                    1 + ExpressionSpills(read.Index, ref maximumArguments)));
            case BoundSpanAdvance advance:
                return Math.Max(1, ExpressionSpills(advance.View, ref maximumArguments));
            case BoundBinary binary:
                if (IsBidDecimal(binary.Left.Type))
                {
                    return Math.Max(3, Math.Max(1 + ExpressionSpills(binary.Left, ref maximumArguments),
                        2 + ExpressionSpills(binary.Right, ref maximumArguments)));
                }
                var requiredBinarySpills = PrimitiveTypeFacts.IsWideInteger(binary.Left.Type)
                    ? binary.Operator switch {
                        TokenKind.Star => 5,
                        TokenKind.Slash or TokenKind.Percent => 7,
                        _ => 3,
                    }
                    : binary.Left.Type is PrimitiveType.Float16 or PrimitiveType.BFloat16
                        ? IsFloatingComparison(binary.Operator) ? 6
                            : binary.Operator is TokenKind.Plus or TokenKind.Minus or TokenKind.Star
                                or TokenKind.Slash or TokenKind.Percent ? 16 : 3
                        : binary.Left.Type == PrimitiveType.Float128 ? 3
                        : binary.Operator == TokenKind.Percent
                            && PrimitiveTypeFacts.IsFloating(binary.Left.Type) ? 4 : 3;
                return Math.Max(requiredBinarySpills,
                    Math.Max(ExpressionSpills(binary.Left, ref maximumArguments),
                        1 + ExpressionSpills(binary.Right, ref maximumArguments)));
            case BoundConditional conditional:
                return Math.Max(ExpressionSpills(conditional.Condition, ref maximumArguments),
                    Math.Max(ExpressionSpills(conditional.WhenTrue, ref maximumArguments),
                        ExpressionSpills(conditional.WhenFalse, ref maximumArguments)));
            case BoundUnsafeExpression unsafeExpression:
                return ExpressionSpills(unsafeExpression.Expression, ref maximumArguments);
            case BoundCall call:
                maximumArguments = Math.Max(maximumArguments,
                    call.Arguments.Length + (IsIndirectResult(call.Type) ? 1 : 0)
                        + (_sourceDependent?[call.Function] == true ? 2 : 0));
                var resultSpills = CallResultSpills(call);
                var argumentSpills = CallArgumentSpills(call);
                var expressionSpillBase = resultSpills + argumentSpills;
                var maximum = expressionSpillBase;
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    if (call.Arguments[i] is BoundOutArgument output && HasDeclaredCleanup(output.Type))
                    {
                        maximum = Math.Max(maximum, expressionSpillBase + CleanupSpills(ref maximumArguments));
                    }
                    else if (call.Arguments[i] is not BoundOutArgument)
                    {
                        maximum = Math.Max(maximum, expressionSpillBase
                            + ExpressionSpills(call.Arguments[i], ref maximumArguments));
                    }
                }
                return maximum;
            case BoundIndirectCall call:
                maximumArguments = Math.Max(maximumArguments, call.Arguments.Length);
                var indirectCallSpills = call.Arguments.Length + 1;
                var maximumIndirectCallSpills = indirectCallSpills;
                maximumIndirectCallSpills = Math.Max(maximumIndirectCallSpills,
                    indirectCallSpills + ExpressionSpills(call.Target, ref maximumArguments));
                foreach (var argument in call.Arguments)
                {
                    maximumIndirectCallSpills = Math.Max(maximumIndirectCallSpills,
                        indirectCallSpills + ExpressionSpills(argument, ref maximumArguments));
                }
                return maximumIndirectCallSpills;
            default:
                return 0;
        }
    }

    private static int CleanupSpills(ref int maximumArguments)
    {
        maximumArguments = Math.Max(maximumArguments, 1);
        return 3;
    }

    // Spill slots are 16 bytes; indirect aggregate values can occupy multiple slots.
    private int CallArgumentValueSpills(BoundCall call, int index)
    {
        var argument = call.Arguments[index];
        var isAddress = IsImplicitReferenceArgument(call, index)
            || argument is BoundOutArgument or BoundReceiverLoan or BoundMutableReceiverLoan
                or BoundFieldLoan or BoundMutableFieldLoan;
        return isAddress ? 1 : Math.Max(1, (Size(argument.Type) + 15) / 16);
    }

    private int CallArgumentAuxiliarySpills(BoundExpression argument) =>
        argument is BoundOutArgument output ? Math.Max(1, (Size(output.Type) + 15) / 16) : 1;

    private int CallResultSpills(BoundCall call) =>
        IsIndirect(call.Type) ? Math.Max(1, (Size(call.Type) + 15) / 16) : 1;

    private int CallArgumentSpills(BoundCall call)
    {
        var total = 0;
        for (var i = 0; i < call.Arguments.Length; i++)
        {
            total += CallArgumentValueSpills(call, i) + CallArgumentAuxiliarySpills(call.Arguments[i]);
        }
        return total;
    }

    private static HashSet<int> InitializationTestVariables(BoundFunction function)
    {
        var variables = new HashSet<int>();
        VisitStatement(function.Body);
        return variables;

        void VisitStatement(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundBlock block:
                    foreach (var child in block.Statements)
                    {
                        VisitStatement(child);
                    }
                    break;
                case BoundLocal local:
                    VisitExpression(local.Initializer);
                    break;
                case BoundAssignment assignment:
                    VisitExpression(assignment.Value);
                    break;
                case BoundOptionalAssignment assignment:
                    VisitExpression(assignment.Place);
                    VisitExpression(assignment.Value);
                    break;
                case BoundFieldAssignment assignment:
                    VisitExpression(assignment.Value);
                    break;
                case BoundBufferAssignment assignment:
                    VisitExpression(assignment.Index);
                    VisitExpression(assignment.Value);
                    break;
                case BoundExpressionStatement expressionStatement:
                    VisitExpression(expressionStatement.Expression);
                    break;
                case BoundIf conditional:
                    VisitExpression(conditional.Condition);
                    VisitStatement(conditional.Then);
                    if (conditional.Else is not null)
                    {
                        VisitStatement(conditional.Else);
                    }
                    break;
                case BoundWhile loop:
                    VisitExpression(loop.Condition);
                    VisitStatement(loop.Body);
                    if (loop.Iterator is not null)
                    {
                        VisitStatement(loop.Iterator);
                    }
                    break;
                case BoundTryFinally cleanup:
                    VisitStatement(cleanup.Body);
                    VisitStatement(cleanup.Finally);
                    break;
                case BoundViewScope view:
                    VisitExpression(view.Owner);
                    VisitExpression(view.Start);
                    VisitExpression(view.RangeEnd ?? view.Length);
                    VisitStatement(view.Body);
                    break;
                case BoundViewSet write:
                    VisitExpression(write.Index);
                    VisitExpression(write.Value);
                    break;
                case BoundUnsafe unsafeBlock:
                    VisitStatement(unsafeBlock.Body);
                    break;
                case BoundTryCatch recovery:
                    VisitStatement(recovery.Body);
                    VisitStatement(recovery.Catch);
                    if (recovery.Finally is not null)
                    {
                        VisitStatement(recovery.Finally);
                    }
                    break;
                case BoundRequire requirement:
                    VisitExpression(requirement.Condition);
                    break;
                case BoundReturn { Value: BoundExpression value }:
                    VisitExpression(value);
                    break;
            }
        }

        void VisitExpression(BoundExpression expression)
        {
            switch (expression)
            {
                case BoundIsInitialized initializationTest:
                    _ = variables.Add(initializationTest.Variable);
                    break;
                case BoundUnary unary:
                    VisitExpression(unary.Operand);
                    break;
                case BoundUpdate update:
                    VisitExpression(update.Place);
                    break;
                case BoundCompoundAssignment compound:
                    VisitExpression(compound.Place);
                    VisitExpression(compound.Right);
                    break;
                case BoundNominalUpdate update:
                    VisitExpression(update.Place);
                    break;
                case BoundCast cast:
                    VisitExpression(cast.Operand);
                    break;
                case TrRmScalarConvert conversion:
                    VisitExpression(conversion.Operand);
                    break;
                case BoundOptionalLift lift:
                    VisitExpression(lift.Value);
                    break;
                case BoundReadonlyProjection projection:
                    VisitExpression(projection.Value);
                    break;
                case BoundIsNull test:
                    VisitExpression(test.Value);
                    break;
                case BoundIsSome pattern:
                    VisitExpression(pattern.Value);
                    break;
                case BoundIsVariablePattern pattern:
                    VisitExpression(pattern.Value);
                    break;
                case BoundFieldRead field:
                    VisitExpression(field.Receiver);
                    break;
                case BoundFieldLoan loan:
                    VisitExpression(loan.Place);
                    break;
                case BoundMutableFieldLoan loan:
                    VisitExpression(loan.Place);
                    break;
                case BoundViewRead view:
                    VisitExpression(view.Index);
                    break;
                case BoundSpanLength length:
                    VisitExpression(length.View);
                    break;
                case BoundSpanRead read:
                    VisitExpression(read.View);
                    VisitExpression(read.Index);
                    break;
                case BoundSpanAdvance advance:
                    VisitExpression(advance.View);
                    break;
                case BoundBufferRead buffer:
                    VisitExpression(buffer.Index);
                    break;
                case BoundBinary binary:
                    VisitExpression(binary.Left);
                    VisitExpression(binary.Right);
                    break;
                case BoundNativeFixedArrayInitialization fixedArray:
                    foreach (var element in fixedArray.Elements)
                    {
                        VisitExpression(element);
                    }
                    break;
                case BoundNativeFixedArrayElement fixedArrayElement:
                    VisitExpression(fixedArrayElement.Index);
                    break;
                case BoundNativeFixedArrayPointerAddress pointerAddress:
                    _ = variables.Add(pointerAddress.Variable);
                    VisitExpression(pointerAddress.Index);
                    break;
                case BoundNativeFixedArrayWrite fixedArrayWrite:
                    VisitExpression(fixedArrayWrite.Index);
                    VisitExpression(fixedArrayWrite.Value);
                    break;
                case BoundNativeStorageAddress address:
                    VisitExpression(address.Index);
                    break;
                case BoundNativeOutputReservation reservation:
                    _ = variables.Add(reservation.Variable);
                    break;
                case BoundNativePointerAddress address:
                    _ = variables.Add(address.Variable);
                    break;
                case BoundNativePointerRead pointerRead:
                    VisitExpression(pointerRead.Address);
                    break;
                case BoundNativePointerWrite pointerWrite:
                    VisitExpression(pointerWrite.Address);
                    VisitExpression(pointerWrite.Value);
                    break;
                case BoundConditional conditional:
                    VisitExpression(conditional.Condition);
                    VisitExpression(conditional.WhenTrue);
                    VisitExpression(conditional.WhenFalse);
                    break;
                case BoundCoalesce coalesce:
                    VisitExpression(coalesce.Optional);
                    VisitExpression(coalesce.Fallback);
                    break;
                case BoundUnsafeExpression unsafeExpression:
                    VisitExpression(unsafeExpression.Expression);
                    break;
                case BoundCall call:
                    foreach (var argument in call.Arguments)
                    {
                        VisitExpression(argument);
                    }
                    break;
            }
        }
    }

    private int Slot(int index) => _slots[_currentFunction][index];
    private int Spill(int index) => _spillBase + (index * 16);
    private int OutSlot(int index)
    {
        var slots = _outSlots[_currentFunction];
        if ((uint)index >= (uint)slots.Length || slots[index] < 0)
        {
            throw new InvalidOperationException("Native output parameter has no staging slot.");
        }
        return slots[index];
    }

    private bool IsImplicitReferenceArgument(BoundCall call, int argumentIndex)
    {
        var target = _program.Functions[call.Function];
        var parameterSlot = call.ParameterIndex(argumentIndex) + (target.IsConstructor ? 1 : 0);
        return (uint)parameterSlot < (uint)target.Variables.Length
            && target.Variables[parameterSlot].IsImplicitRef;
    }

    private void EmitImplicitReferenceAddress(BoundCall call, int argumentIndex, int spill)
    {
        var temporary = call.ImplicitReferenceTemporary(argumentIndex);
        if (temporary >= 0)
        {
            var argument = call.Arguments[argumentIndex];
            Expression(argument, spill);
            StoreValue(Slot(temporary), argument.Type);
            _code.Address(Slot(temporary));
            return;
        }
        if (call.Arguments[argumentIndex] is not BoundRead read)
        {
            if (call.Arguments[argumentIndex] is BoundFieldRead field)
            {
                if (UpdateAddress(field, spill))
                {
                    throw new InvalidOperationException("A managed field cannot be borrowed by an implicit reference.");
                }
                return;
            }
            if (call.Arguments[argumentIndex] is BoundBufferRead or BoundViewRead)
            {
                if (UpdateAddress(call.Arguments[argumentIndex], spill))
                {
                    throw new InvalidOperationException("A host-buffer or lexical-view element cannot be borrowed by an implicit reference.");
                }
                return;
            }
            throw new InvalidOperationException("An implicit reference argument must be an existing place or materialized value.");
        }
        if (_program.Functions[_currentFunction].Variables[read.Variable].IsImplicitRef)
        {
            _code.Load64(Slot(read.Variable));
        }
        else
        {
            _code.Address(OwnerPlace(read.Variable));
        }
    }

    private int OwnerPlace(int index) =>
        _program.Functions[_currentFunction].Variables[index].IsOut ? OutSlot(index) : Slot(index);
    private void SetInitializationFlag(int variable, bool initialized)
    {
        var flag = _initializationFlags[_currentFunction][variable];
        if (flag < 0)
        {
            return;
        }
        if (initialized)
        {
            _code.Set(flag);
        }
        else
        {
            _code.Clear(flag);
        }
    }

    private void EmitInitializationTest(int variable)
    {
        var type = _program.Functions[_currentFunction].Variables[variable].Type;
        if (ManagedClassTypes.IsManaged(type))
        {
            var sourceSlot = OwnerPlace(variable);
            if (!_managedRootFlags[_currentFunction].TryGetValue(sourceSlot, out var flag))
            {
                throw new InvalidOperationException("Managed initialization test has no runtime state flag.");
            }
            _code.Load(flag);
        }
        else if (IsUnique(type))
        {
            var flag = _liveSlots[_currentFunction][variable];
            if (flag >= 0)
            {
                _code.Load(flag);
            }
            else
            {
                _code.Bytes(0xB8, 0x01, 0x00, 0x00, 0x00);
            }
        }
        else
        {
            var flag = _initializationFlags[_currentFunction][variable];
            if (flag >= 0)
            {
                _code.Load(flag);
            }
            else
            {
                _code.Bytes(0xB8, 0x01, 0x00, 0x00, 0x00);
            }
        }
        _code.Bytes(0x85, 0xC0, 0x0F, 0x95, 0xC0, 0x0F, 0xB6, 0xC0);
    }

    private static bool IsStruct(PrimitiveType type) =>
        (int)type is >= 100 and < BoundEnumTypes.First;
    private static bool RequiresFloatingEnvironment(PrimitiveType source, PrimitiveType target) =>
        source is PrimitiveType.Float32 or PrimitiveType.Float64
            || target is PrimitiveType.Float32 or PrimitiveType.Float64;
    private static bool IsWide(PrimitiveType type) =>
        type is PrimitiveType.Int64 or PrimitiveType.UInt64 or PrimitiveType.Float64 or PrimitiveType.Decimal64
            or PrimitiveType.OwnedInt32Buffer
            or PrimitiveType.TokenSpan or PrimitiveType.NativeUtf16ZPointer or PrimitiveType.NativeUtf8ZPointer
            or PrimitiveType.NativeRawPointer
            or PrimitiveType.NativeStorageAddress
            || NativePointerTypes.IsType(type)
            || NativeFunctionPointerTypes.IsType(type)
            || CheckedReferenceTypes.IsType(type)
            || ManagedClassTypes.IsManaged(type);
    private int Size(PrimitiveType type) =>
        ValueLayout.Size(type, _program.Structures, _program.CheckedReferenceTypes);
    private static bool IsNarrowInteger(PrimitiveType type) =>
        PrimitiveTypeFacts.IntegerWidth(type) is 8 or 16;

    private void LoadTypedPointerField(int offset, PrimitiveType type)
    {
        _code.LoadPointerField(offset, Size(type));
        NormalizeNarrowInteger(type, checkRange: false);
    }

    private int UpdateStoreWidth(BoundExpression place, PrimitiveType type)
    {
        if (place is BoundRead read)
        {
            var variable = _program.Functions[_currentFunction].Variables[read.Variable];
            if (!variable.IsImplicitRef && !variable.IsOut)
            {
                return Math.Max(4, Size(type));
            }
        }
        return Size(type);
    }

    private int ScratchSize(PrimitiveType type) => Math.Max(8, Size(type));
    private int ScratchAlignment(PrimitiveType type) =>
        Math.Max(8, ValueLayout.Alignment(type, _program.Structures, _program.CheckedReferenceTypes));
    private static int AlignOffset(int offset, int alignment) =>
        checked((offset + alignment - 1) & ~(alignment - 1));

    private static Binary128Runtime CreateBinary128Runtime()
    {
        const string source = """
            uint128 F128Zero(bool negative) {
                if (negative) {
                    return uint128(1) << 127;
                }
                return uint128(0);
            }
            uint128 F128Infinity(bool negative) {
                mut uint128 value = uint128(32767) << 112;
                if (negative) {
                    value = value | (uint128(1) << 127);
                }
                return value;
            }
            uint128 F128NaN() {
                return (uint128(32767) << 112) | (uint128(1) << 111);
            }
            bool F128Sign(uint128 bits) {
                return ((bits >> 127) & uint128(1)) != 0;
            }
            uint128 F128Fraction(uint128 bits) {
                return bits & ((uint128(1) << 112) - uint128(1));
            }
            uint128 F128Class(uint128 bits) {
                uint128 encodedExponent = (bits >> 112) & uint128(32767);
                uint128 fraction = F128Fraction(bits);
                if (encodedExponent == uint128(32767)) {
                    if (fraction == 0) {
                        return uint128(1);
                    }
                    return uint128(2);
                }
                if (encodedExponent == 0 && fraction == 0) {
                    return uint128(0);
                }
                return uint128(3);
            }
            uint128 F128Significand(uint128 bits) {
                uint128 encodedExponent = (bits >> 112) & uint128(32767);
                mut uint128 significand = F128Fraction(bits);
                uint128 hiddenBit = uint128(1) << 112;
                if (encodedExponent == 0) {
                    while (significand != 0 && significand < hiddenBit) {
                        significand = significand << 1;
                    }
                    return significand;
                }
                return significand | hiddenBit;
            }
            uint128 F128Exponent(uint128 bits) {
                uint128 encodedExponent = (bits >> 112) & uint128(32767);
                if (encodedExponent != 0) {
                    return encodedExponent + uint128(16607);
                }
                mut uint128 significand = F128Fraction(bits);
                mut uint128 exponent = uint128(16608);
                uint128 hiddenBit = uint128(1) << 112;
                while (significand != 0 && significand < hiddenBit) {
                    significand = significand << 1;
                    exponent = exponent - uint128(1);
                }
                return exponent;
            }
            uint128 F128ShiftRightJam(uint128 value, uint128 distance) {
                if (distance == 0) {
                    return value;
                }
                if (distance >= uint128(128)) {
                    if (value == 0) {
                        return 0;
                    }
                    return 1;
                }
                uint128 discardedMask = (uint128(1) << distance) - uint128(1);
                bool sticky = (value & discardedMask) != 0;
                mut uint128 shifted = value >> distance;
                if (sticky) {
                    shifted = shifted | uint128(1);
                }
                return shifted;
            }
            uint128 F128Pack(bool negative, uint128 exponent, uint128 extended) {
                if (extended == 0) {
                    return F128Zero(negative);
                }
                mut uint128 resultExtended = extended;
                mut uint128 resultExponent = exponent;
                uint128 hiddenBit = uint128(1) << 112;
                uint128 extendedHiddenBit = hiddenBit << 3;
                while (resultExtended >= (extendedHiddenBit << 1)) {
                    resultExtended = F128ShiftRightJam(resultExtended, uint128(1));
                    resultExponent = resultExponent + uint128(1);
                }
                while (resultExtended < extendedHiddenBit && resultExponent > uint128(16496)) {
                    resultExtended = resultExtended << 1;
                    resultExponent = resultExponent - uint128(1);
                }
                if (resultExponent < uint128(16608)) {
                    resultExtended = F128ShiftRightJam(
                        resultExtended, uint128(16608) - resultExponent);
                    resultExponent = uint128(16608);
                }
                mut uint128 significand = resultExtended >> 3;
                uint128 rounding = resultExtended & uint128(7);
                if (rounding > uint128(4)
                    || (rounding == uint128(4) && (significand & uint128(1)) != 0)) {
                    significand = significand + uint128(1);
                }
                if (significand >= (uint128(1) << 113)) {
                    significand = significand >> 1;
                    resultExponent = resultExponent + uint128(1);
                }
                if (resultExponent > uint128(49373)) {
                    return F128Infinity(negative);
                }
                mut uint128 exponentField = uint128(0);
                if (significand >= hiddenBit) {
                    exponentField = resultExponent - uint128(16607);
                }
                mut uint128 bits = (exponentField << 112)
                    | (significand & (hiddenBit - uint128(1)));
                if (negative) {
                    bits = bits | (uint128(1) << 127);
                }
                return bits;
            }
            uint128 F128AddCore(uint128 left, uint128 right, bool subtract) {
                mut uint128 adjustedRight = right;
                if (subtract) {
                    adjustedRight = adjustedRight ^ (uint128(1) << 127);
                }
                mut bool signA = F128Sign(left);
                mut bool signB = F128Sign(adjustedRight);
                uint128 classA = F128Class(left);
                uint128 classB = F128Class(adjustedRight);
                if (classA == uint128(2) || classB == uint128(2)) {
                    return F128NaN();
                }
                if (classA == uint128(1) && classB == uint128(1)) {
                    if (signA == signB) {
                        return F128Infinity(signA);
                    }
                    return F128NaN();
                }
                if (classA == uint128(1)) {
                    return F128Infinity(signA);
                }
                if (classB == uint128(1)) {
                    return F128Infinity(signB);
                }
                if (classA == uint128(0) && classB == uint128(0)) {
                    if (signA == signB) {
                        return F128Zero(signA);
                    }
                    return F128Zero(false);
                }
                if (classA == uint128(0)) {
                    return adjustedRight;
                }
                if (classB == uint128(0)) {
                    return left;
                }
                mut uint128 exponentA = F128Exponent(left);
                mut uint128 exponentB = F128Exponent(adjustedRight);
                mut uint128 significandA = F128Significand(left);
                mut uint128 significandB = F128Significand(adjustedRight);
                if (exponentA < exponentB
                    || (exponentA == exponentB && significandA < significandB)) {
                    mut uint128 temporarySignificand = significandA;
                    significandA = significandB;
                    significandB = temporarySignificand;
                    mut uint128 temporaryExponent = exponentA;
                    exponentA = exponentB;
                    exponentB = temporaryExponent;
                    mut bool temporarySign = signA;
                    signA = signB;
                    signB = temporarySign;
                }
                uint128 extendedA = significandA << 3;
                uint128 extendedB = F128ShiftRightJam(
                    significandB << 3, exponentA - exponentB);
                if (signA == signB) {
                    mut uint128 sum = extendedA + extendedB;
                    mut uint128 resultExponent = exponentA;
                    if (sum >= (uint128(1) << 116)) {
                        sum = F128ShiftRightJam(sum, uint128(1));
                        resultExponent = resultExponent + uint128(1);
                    }
                    return F128Pack(signA, resultExponent, sum);
                }
                mut uint128 difference = extendedA - extendedB;
                if (difference == 0) {
                    return F128Zero(false);
                }
                mut uint128 differenceExponent = exponentA;
                uint128 normalizedBit = uint128(1) << 115;
                while (difference < normalizedBit && differenceExponent > uint128(16496)) {
                    difference = difference << 1;
                    differenceExponent = differenceExponent - uint128(1);
                }
                return F128Pack(signA, differenceExponent, difference);
            }
            uint128 F128Add(uint128 left, uint128 right) {
                return F128AddCore(left, right, false);
            }
            uint128 F128Subtract(uint128 left, uint128 right) {
                return F128AddCore(left, right, true);
            }
            uint128 F128MultiplyLow(uint128 left, uint128 right) {
                uint128 mask = (uint128(1) << 64) - uint128(1);
                uint128 leftLow = left & mask;
                uint128 leftHigh = left >> 64;
                uint128 rightLow = right & mask;
                uint128 rightHigh = right >> 64;
                uint128 lowLow = leftLow * rightLow;
                uint128 lowHigh = leftLow * rightHigh;
                uint128 highLow = leftHigh * rightLow;
                return lowLow + ((lowHigh & mask) << 64)
                    + ((highLow & mask) << 64);
            }
            uint128 F128MultiplyHigh(uint128 left, uint128 right) {
                uint128 mask = (uint128(1) << 64) - uint128(1);
                uint128 leftLow = left & mask;
                uint128 leftHigh = left >> 64;
                uint128 rightLow = right & mask;
                uint128 rightHigh = right >> 64;
                uint128 lowLow = leftLow * rightLow;
                uint128 lowHigh = leftLow * rightHigh;
                uint128 highLow = leftHigh * rightLow;
                uint128 highHigh = leftHigh * rightHigh;
                uint128 firstCross = (lowHigh & mask) << 64;
                uint128 secondCross = (highLow & mask) << 64;
                mut uint128 firstSum = lowLow + firstCross;
                bool carryFirst = firstSum < lowLow;
                mut uint128 lowResult = firstSum + secondCross;
                bool carrySecond = lowResult < firstSum;
                mut uint128 highResult = (lowHigh >> 64) + (highLow >> 64) + highHigh;
                if (carryFirst) {
                    highResult = highResult + uint128(1);
                }
                if (carrySecond) {
                    highResult = highResult + uint128(1);
                }
                return highResult;
            }
            uint128 F128Multiply(uint128 left, uint128 right) {
                bool negative = F128Sign(left) != F128Sign(right);
                uint128 classA = F128Class(left);
                uint128 classB = F128Class(right);
                if (classA == uint128(2) || classB == uint128(2)
                    || (classA == uint128(1) && classB == uint128(0))
                    || (classA == uint128(0) && classB == uint128(1))) {
                    return F128NaN();
                }
                if (classA == uint128(1) || classB == uint128(1)) {
                    return F128Infinity(negative);
                }
                if (classA == uint128(0) || classB == uint128(0)) {
                    return F128Zero(negative);
                }
                uint128 leftSignificand = F128Significand(left);
                uint128 rightSignificand = F128Significand(right);
                uint128 low = F128MultiplyLow(leftSignificand, rightSignificand);
                uint128 high = F128MultiplyHigh(leftSignificand, rightSignificand);
                bool highProduct = (high & (uint128(1) << 97)) != 0;
                uint128 shift = highProduct ? uint128(110) : uint128(109);
                mut uint128 extended = (high << (uint128(128) - shift)) | (low >> shift);
                uint128 discardedMask = (uint128(1) << shift) - uint128(1);
                if ((low & discardedMask) != 0) {
                    extended = extended | uint128(1);
                }
                mut uint128 exponent = F128Exponent(left) + F128Exponent(right)
                    - uint128(32990);
                if (highProduct) {
                    exponent = exponent + uint128(1);
                }
                return F128Pack(negative, exponent, extended);
            }
            uint128 F128Divide(uint128 left, uint128 right) {
                bool negative = F128Sign(left) != F128Sign(right);
                uint128 classA = F128Class(left);
                uint128 classB = F128Class(right);
                if (classA == uint128(2) || classB == uint128(2)
                    || (classA == uint128(0) && classB == uint128(0))
                    || (classA == uint128(1) && classB == uint128(1))) {
                    return F128NaN();
                }
                if (classA == uint128(1) || classB == uint128(0)) {
                    return F128Infinity(negative);
                }
                if (classA == uint128(0) || classB == uint128(1)) {
                    return F128Zero(negative);
                }
                mut uint128 numerator = F128Significand(left);
                uint128 denominator = F128Significand(right);
                mut uint128 exponent = F128Exponent(left) + uint128(32990)
                    - F128Exponent(right);
                if (numerator < denominator) {
                    numerator = numerator << 1;
                    exponent = exponent - uint128(1);
                }
                mut uint128 remainder = numerator - denominator;
                mut uint128 quotient = uint128(1);
                for (mut int32 index = 0; index < 115; index++) {
                    remainder = remainder << 1;
                    quotient = quotient << 1;
                    if (remainder >= denominator) {
                        remainder = remainder - denominator;
                        quotient = quotient | uint128(1);
                    }
                }
                if (remainder != 0) {
                    quotient = quotient | uint128(1);
                }
                return F128Pack(negative, exponent, quotient);
            }
            uint128 F128Remainder(uint128 left, uint128 right) {
                bool negative = F128Sign(left);
                uint128 classA = F128Class(left);
                uint128 classB = F128Class(right);
                if (classA == uint128(2) || classB == uint128(2)
                    || classA == uint128(1) || classB == uint128(0)) {
                    return F128NaN();
                }
                if (classB == uint128(1) || classA == uint128(0)) {
                    return left;
                }
                mut uint128 exponentA = F128Exponent(left);
                uint128 exponentB = F128Exponent(right);
                if (exponentA < exponentB) {
                    return left;
                }
                uint128 denominator = F128Significand(right);
                mut uint128 remainder = F128Significand(left);
                if (remainder >= denominator) {
                    remainder = remainder - denominator;
                }
                mut uint128 exponentGap = exponentA - exponentB;
                while (exponentGap > uint128(0)) {
                    remainder = remainder << 1;
                    if (remainder >= denominator) {
                        remainder = remainder - denominator;
                    }
                    exponentGap = exponentGap - uint128(1);
                }
                if (remainder == 0) {
                    return F128Zero(negative);
                }
                return F128Pack(negative, exponentB, remainder << 3);
            }
            int32 F128Compare(uint128 left, uint128 right) {
                uint128 classA = F128Class(left);
                uint128 classB = F128Class(right);
                if (classA == uint128(2) || classB == uint128(2)) {
                    return 2;
                }
                if (classA == uint128(0) && classB == uint128(0)) {
                    return 0;
                }
                bool negativeA = F128Sign(left);
                bool negativeB = F128Sign(right);
                if (negativeA != negativeB) {
                    if (negativeA) {
                        return -1;
                    }
                    return 1;
                }
                uint128 magnitudeMask = (uint128(1) << 127) - uint128(1);
                uint128 leftMagnitude = left & magnitudeMask;
                uint128 rightMagnitude = right & magnitudeMask;
                if (leftMagnitude == rightMagnitude) {
                    return 0;
                }
                if (leftMagnitude < rightMagnitude) {
                    if (negativeA) {
                        return 1;
                    }
                    return -1;
                }
                if (negativeA) {
                    return -1;
                }
                return 1;
            }
            bool F128Equal(uint128 left, uint128 right) {
                return F128Compare(left, right) == 0;
            }
            bool F128NotEqual(uint128 left, uint128 right) {
                return F128Compare(left, right) != 0;
            }
            bool F128Less(uint128 left, uint128 right) {
                return F128Compare(left, right) == -1;
            }
            bool F128LessOrEqual(uint128 left, uint128 right) {
                int32 comparison = F128Compare(left, right);
                return comparison == -1 || comparison == 0;
            }
            bool F128Greater(uint128 left, uint128 right) {
                return F128Compare(left, right) == 1;
            }
            bool F128GreaterOrEqual(uint128 left, uint128 right) {
                int32 comparison = F128Compare(left, right);
                return comparison == 1 || comparison == 0;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                SourceArithmeticProfile = SourceArithmeticProfile.Release,
            });
        if (compilation.HasErrors || compilation.Program is not BoundProgram program)
        {
            throw new InvalidOperationException(
                "The compiler-owned binary128 integer helper failed semantic verification: "
                + string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        var code = new PeCompiler(program, managedNodes: false, library: true).Compile();
        if (code.Data.Length != 0 || !code.DataFixups.IsEmpty)
        {
            throw new InvalidOperationException(
                "The compiler-owned binary128 integer helper must not depend on image data.");
        }
        var functionOffsets = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            if (!function.IsExternal)
            {
                functionOffsets.Add(function.Name, code.SourceFunctionOffsets[index]);
            }
        }
        return new Binary128Runtime(code, functionOffsets.ToImmutable());
    }

    private static BidDecimalRuntime CreateBidDecimalRuntime()
    {
        const string source = """
            [Blittable] [Layout(LayoutKind.Sequential)] struct D256 {
                public uint128 Low;
                public uint128 High;
                public uint128 Remainder;
                public D256(uint128 low, uint128 high, uint128 remainder) {
                    this.Low = low;
                    this.High = high;
                    this.Remainder = remainder;
                }
            }
            uint128 DPower10(int32 exponent) {
                mut uint128 value = uint128(1);
                for (mut int32 index = 0; index < exponent; index++) {
                    value = value * uint128(10);
                }
                return value;
            }
            int32 DDecimalDigits(uint128 value) {
                if (value == uint128(0)) {
                    return 1;
                }
                mut int32 digits = 0;
                mut uint128 remaining = value;
                while (remaining != uint128(0)) {
                    remaining = remaining / uint128(10);
                    digits++;
                }
                return digits;
            }
            uint128 D128Scale10(uint128 value, int32 exponent) {
                mut uint128 result = value;
                for (mut int32 index = 0; index < exponent; index++) {
                    result = result * uint128(10);
                }
                return result;
            }
            D256 D256Divide10(D256 value) {
                uint128 mask = (uint128(1) << 64) - uint128(1);
                mut uint128 quotient3 = value.High >> 64;
                mut uint128 remainder = quotient3 % uint128(10);
                quotient3 = quotient3 / uint128(10);
                mut uint128 current = (remainder << 64) | (value.High & mask);
                uint128 quotient2 = current / uint128(10);
                remainder = current % uint128(10);
                current = (remainder << 64) | (value.Low >> 64);
                uint128 quotient1 = current / uint128(10);
                remainder = current % uint128(10);
                current = (remainder << 64) | (value.Low & mask);
                uint128 quotient0 = current / uint128(10);
                remainder = current % uint128(10);
                return new D256((quotient1 << 64) | quotient0,
                    (quotient3 << 64) | quotient2, remainder);
            }
            D256 D256Multiply10(D256 value) {
                uint128 mask = (uint128(1) << 64) - uint128(1);
                uint128 lowProduct = (value.Low & mask) * uint128(10);
                uint128 middleProduct = (value.Low >> 64) * uint128(10)
                    + (lowProduct >> 64);
                uint128 low = ((middleProduct & mask) << 64) | (lowProduct & mask);
                uint128 high = value.High * uint128(10) + (middleProduct >> 64);
                return new D256(low, high, uint128(0));
            }
            D256 D256Scale10(D256 value, int32 exponent) {
                mut D256 result = value;
                for (mut int32 index = 0; index < exponent; index++) {
                    result = D256Multiply10(result);
                }
                return result;
            }
            D256 D256Add(D256 left, D256 right) {
                mut uint128 low = left.Low + right.Low;
                mut uint128 high = left.High + right.High;
                if (low < left.Low) {
                    high = high + uint128(1);
                }
                return new D256(low, high, uint128(0));
            }
            D256 D256Subtract(D256 left, D256 right) {
                mut uint128 low = left.Low - right.Low;
                mut uint128 high = left.High - right.High;
                if (left.Low < right.Low) {
                    high = high - uint128(1);
                }
                return new D256(low, high, uint128(0));
            }
            int32 D256Compare(D256 left, D256 right) {
                if (left.High < right.High) {
                    return -1;
                }
                if (left.High > right.High) {
                    return 1;
                }
                if (left.Low < right.Low) {
                    return -1;
                }
                if (left.Low > right.Low) {
                    return 1;
                }
                return 0;
            }
            bool D256IsZero(D256 value) {
                return value.Low == uint128(0) && value.High == uint128(0);
            }
            int32 D256Digits(D256 value) {
                if (D256IsZero(value)) {
                    return 0;
                }
                mut int32 digits = 0;
                mut D256 remaining = value;
                while (!D256IsZero(remaining)) {
                    remaining = D256Divide10(remaining);
                    digits++;
                }
                return digits;
            }
            D256 D256Multiply128(uint128 left, uint128 right) {
                uint128 mask = (uint128(1) << 64) - uint128(1);
                uint128 leftLow = left & mask;
                uint128 leftHigh = left >> 64;
                uint128 rightLow = right & mask;
                uint128 rightHigh = right >> 64;
                uint128 lowLow = leftLow * rightLow;
                uint128 lowHigh = leftLow * rightHigh;
                uint128 highLow = leftHigh * rightLow;
                uint128 highHigh = leftHigh * rightHigh;
                uint128 firstCross = (lowHigh & mask) << 64;
                uint128 secondCross = (highLow & mask) << 64;
                mut uint128 low = lowLow + firstCross;
                bool carryFirst = low < lowLow;
                mut uint128 lowResult = low + secondCross;
                bool carrySecond = lowResult < low;
                mut uint128 highResult = (lowHigh >> 64) + (highLow >> 64) + highHigh;
                if (carryFirst) {
                    highResult = highResult + uint128(1);
                }
                if (carrySecond) {
                    highResult = highResult + uint128(1);
                }
                return new D256(lowResult, highResult, uint128(0));
            }
            D256 D256RoundDrop(D256 value, int32 droppedDigits) {
                if (droppedDigits <= 0) {
                    return D256Scale10(value, -droppedDigits);
                }
                if (droppedDigits > D256Digits(value)) {
                    return new D256(uint128(0), uint128(0), uint128(0));
                }
                mut D256 quotient = value;
                mut uint128 guard = uint128(0);
                mut bool sticky = false;
                for (mut int32 index = 0; index < droppedDigits; index++) {
                    quotient = D256Divide10(quotient);
                    guard = quotient.Remainder;
                    if (index + 1 < droppedDigits && guard != uint128(0)) {
                        sticky = true;
                    }
                }
                if (guard > uint128(5)
                    || (guard == uint128(5)
                        && (sticky || (quotient.Low & uint128(1)) != uint128(0)))) {
                    quotient = D256Add(quotient, new D256(uint128(1), uint128(0), uint128(0)));
                }
                return quotient;
            }
            uint128 DInfinity(bool negative, int32 width) {
                mut uint128 value = uint128(30) << uint128(width - 6);
                if (negative) {
                    value = value | (uint128(1) << uint128(width - 1));
                }
                return value;
            }
            uint128 DNaN(int32 width) {
                return uint128(31) << uint128(width - 6);
            }
            uint128 DDecodeCoefficient(uint128 bits, int32 width) {
                mut int32 precision = 0;
                mut int32 coefficientBits = 0;
                if (width == 32) {
                    precision = 7;
                    coefficientBits = 23;
                } else if (width == 64) {
                    precision = 16;
                    coefficientBits = 53;
                } else {
                    precision = 34;
                    coefficientBits = 113;
                }
                uint128 special = (bits >> uint128(width - 6)) & uint128(31);
                if (special == uint128(30) || special == uint128(31)) {
                    return uint128(0);
                }
                bool steering = ((bits >> uint128(width - 3)) & uint128(3)) == uint128(3);
                mut uint128 coefficient = uint128(0);
                if (steering) {
                    coefficient = (uint128(1) << uint128(coefficientBits))
                        | (bits & ((uint128(1) << uint128(coefficientBits - 2)) - uint128(1)));
                } else {
                    coefficient = bits & ((uint128(1) << uint128(coefficientBits)) - uint128(1));
                }
                if (coefficient >= DPower10(precision)) {
                    coefficient = uint128(0);
                }
                return coefficient;
            }
            uint128 D32Coefficient(uint128 bits) {
                uint128 special = (bits >> uint128(26)) & uint128(31);
                if (special == uint128(30) || special == uint128(31)) {
                    return uint128(0);
                }
                bool steering = ((bits >> uint128(29)) & uint128(3)) == uint128(3);
                mut uint128 coefficient = uint128(0);
                if (steering) {
                    coefficient = (uint128(1) << uint128(23))
                        | (bits & ((uint128(1) << uint128(21)) - uint128(1)));
                } else {
                    coefficient = bits & ((uint128(1) << uint128(23)) - uint128(1));
                }
                mut uint128 limit = uint128(1);
                for (mut int32 index = 0; index < 7; index++) {
                    limit = limit * uint128(10);
                }
                return coefficient >= limit ? uint128(0) : coefficient;
            }
            uint128 D64Coefficient(uint128 bits) {
                uint128 special = (bits >> uint128(58)) & uint128(31);
                if (special == uint128(30) || special == uint128(31)) {
                    return uint128(0);
                }
                bool steering = ((bits >> uint128(61)) & uint128(3)) == uint128(3);
                mut uint128 coefficient = uint128(0);
                if (steering) {
                    coefficient = (uint128(1) << uint128(53))
                        | (bits & ((uint128(1) << uint128(51)) - uint128(1)));
                } else {
                    coefficient = bits & ((uint128(1) << uint128(53)) - uint128(1));
                }
                mut uint128 limit = uint128(1);
                for (mut int32 index = 0; index < 16; index++) {
                    limit = limit * uint128(10);
                }
                return coefficient >= limit ? uint128(0) : coefficient;
            }
            uint128 D128Coefficient(uint128 bits) {
                uint128 special = (bits >> uint128(122)) & uint128(31);
                if (special == uint128(30) || special == uint128(31)) {
                    return uint128(0);
                }
                bool steering = ((bits >> uint128(125)) & uint128(3)) == uint128(3);
                mut uint128 coefficient = uint128(0);
                if (steering) {
                    coefficient = (uint128(1) << uint128(113))
                        | (bits & ((uint128(1) << uint128(111)) - uint128(1)));
                } else {
                    coefficient = bits & ((uint128(1) << uint128(113)) - uint128(1));
                }
                mut uint128 limit = uint128(1);
                for (mut int32 index = 0; index < 34; index++) {
                    limit = limit * uint128(10);
                }
                return coefficient >= limit ? uint128(0) : coefficient;
            }
            uint128 DClass(uint128 bits, int32 width) {
                mut int32 precision = 0;
                mut int32 coefficientBits = 0;
                if (width == 32) {
                    precision = 7;
                    coefficientBits = 23;
                } else if (width == 64) {
                    precision = 16;
                    coefficientBits = 53;
                } else {
                    precision = 34;
                    coefficientBits = 113;
                }
                uint128 special = (bits >> uint128(width - 6)) & uint128(31);
                if (special == uint128(30)) {
                    return uint128(2);
                }
                if (special == uint128(31)) {
                    return uint128(3);
                }
                bool steering = ((bits >> uint128(width - 3)) & uint128(3)) == uint128(3);
                mut uint128 coefficient = uint128(0);
                if (steering) {
                    coefficient = (uint128(1) << uint128(coefficientBits))
                        | (bits & ((uint128(1) << uint128(coefficientBits - 2)) - uint128(1)));
                } else {
                    coefficient = bits & ((uint128(1) << uint128(coefficientBits)) - uint128(1));
                }
                mut uint128 precisionLimit = uint128(1);
                for (mut int32 index = 0; index < precision; index++) {
                    precisionLimit = precisionLimit * uint128(10);
                }
                return coefficient == uint128(0) || coefficient >= precisionLimit
                    ? uint128(0) : uint128(1);
            }
            bool DNegative(uint128 bits, int32 width) {
                return (bits & (uint128(1) << uint128(width - 1))) != uint128(0);
            }
            int32 DExponent(uint128 bits, int32 width) {
                mut int32 coefficientBits = 0;
                mut int32 exponentBits = 0;
                mut int32 bias = 0;
                if (width == 32) {
                    coefficientBits = 23;
                    exponentBits = 8;
                    bias = 101;
                } else if (width == 64) {
                    coefficientBits = 53;
                    exponentBits = 10;
                    bias = 398;
                } else {
                    coefficientBits = 113;
                    exponentBits = 14;
                    bias = 6176;
                }
                bool steering = ((bits >> uint128(width - 3)) & uint128(3)) == uint128(3);
                uint128 exponentMask = (uint128(1) << uint128(exponentBits)) - uint128(1);
                uint128 encodedExponent = steering
                    ? (bits >> uint128(coefficientBits - 2)) & exponentMask
                    : (bits >> uint128(coefficientBits)) & exponentMask;
                mut int32 decodedExponent = 0;
                mut int32 exponentPower = 1;
                for (mut int32 bit = 0; bit < exponentBits; bit++) {
                    if ((encodedExponent & uint128(exponentPower)) != uint128(0)) {
                        decodedExponent = decodedExponent + exponentPower;
                    }
                    exponentPower = exponentPower * 2;
                }
                return decodedExponent - bias;
            }
            int32 DCompareMagnitude(uint128 leftCoefficient, int32 leftExponent,
                uint128 rightCoefficient, int32 rightExponent) {
                if (leftCoefficient == uint128(0)) {
                    return rightCoefficient == uint128(0) ? 0 : -1;
                }
                if (rightCoefficient == uint128(0)) {
                    return 1;
                }
                mut uint128 leftRemaining = leftCoefficient;
                mut uint128 rightRemaining = rightCoefficient;
                mut int32 leftDigits = 0;
                mut int32 rightDigits = 0;
                while (leftRemaining != uint128(0)) {
                    leftRemaining = leftRemaining / uint128(10);
                    leftDigits++;
                }
                while (rightRemaining != uint128(0)) {
                    rightRemaining = rightRemaining / uint128(10);
                    rightDigits++;
                }
                int32 leftAdjusted = leftExponent + leftDigits - 1;
                int32 rightAdjusted = rightExponent + rightDigits - 1;
                if (leftAdjusted < rightAdjusted) {
                    return -1;
                }
                if (leftAdjusted > rightAdjusted) {
                    return 1;
                }
                int32 commonExponent = leftExponent < rightExponent
                    ? leftExponent : rightExponent;
                mut uint128 scaledLeft = leftCoefficient;
                mut uint128 scaledRight = rightCoefficient;
                for (mut int32 index = 0; index < leftExponent - commonExponent; index++) {
                    scaledLeft = scaledLeft * uint128(10);
                }
                for (mut int32 index = 0; index < rightExponent - commonExponent; index++) {
                    scaledRight = scaledRight * uint128(10);
                }
                if (scaledLeft < scaledRight) {
                    return -1;
                }
                if (scaledLeft > scaledRight) {
                    return 1;
                }
                return 0;
            }
            int32 DCompare(uint128 leftBits, uint128 rightBits, int32 width) {
                mut uint128 leftCoefficient = uint128(0);
                mut uint128 rightCoefficient = uint128(0);
                if (width == 32) {
                    leftCoefficient = D32Coefficient(leftBits);
                    rightCoefficient = D32Coefficient(rightBits);
                } else if (width == 64) {
                    leftCoefficient = D64Coefficient(leftBits);
                    rightCoefficient = D64Coefficient(rightBits);
                } else {
                    leftCoefficient = D128Coefficient(leftBits);
                    rightCoefficient = D128Coefficient(rightBits);
                }
                uint128 leftClass = DClass(leftBits, width);
                uint128 rightClass = DClass(rightBits, width);
                bool leftNegative = DNegative(leftBits, width);
                bool rightNegative = DNegative(rightBits, width);
                if (leftClass == uint128(3) || rightClass == uint128(3)) {
                    return 2;
                }
                if (leftClass == uint128(2) && rightClass == uint128(2)) {
                    return leftNegative == rightNegative ? 0
                        : leftNegative ? -1 : 1;
                }
                if (leftClass == uint128(2)) {
                    return leftNegative ? -1 : 1;
                }
                if (rightClass == uint128(2)) {
                    return rightNegative ? 1 : -1;
                }
                if (leftClass == uint128(0) && rightClass == uint128(0)) {
                    return 0;
                }
                if (leftNegative != rightNegative) {
                    return leftNegative ? -1 : 1;
                }
                int32 leftExponent = DExponent(leftBits, width);
                int32 rightExponent = DExponent(rightBits, width);
                if (leftCoefficient == uint128(0)) {
                    return rightCoefficient == uint128(0) ? 0
                        : leftNegative ? 1 : -1;
                }
                if (rightCoefficient == uint128(0)) {
                    return leftNegative ? -1 : 1;
                }
                mut uint128 leftRemaining = leftCoefficient;
                mut uint128 rightRemaining = rightCoefficient;
                mut int32 leftDigits = 0;
                mut int32 rightDigits = 0;
                while (leftRemaining != uint128(0)) {
                    leftRemaining = leftRemaining / uint128(10);
                    leftDigits++;
                }
                while (rightRemaining != uint128(0)) {
                    rightRemaining = rightRemaining / uint128(10);
                    rightDigits++;
                }
                int32 leftAdjusted = leftExponent + leftDigits - 1;
                int32 rightAdjusted = rightExponent + rightDigits - 1;
                if (leftAdjusted < rightAdjusted) {
                    return leftNegative ? 1 : -1;
                }
                if (leftAdjusted > rightAdjusted) {
                    return leftNegative ? -1 : 1;
                }
                int32 commonExponent = leftExponent < rightExponent
                    ? leftExponent : rightExponent;
                mut uint128 scaledLeft = leftCoefficient;
                mut uint128 scaledRight = rightCoefficient;
                for (mut int32 index = 0; index < leftExponent - commonExponent; index++) {
                    scaledLeft = scaledLeft * uint128(10);
                }
                for (mut int32 index = 0; index < rightExponent - commonExponent; index++) {
                    scaledRight = scaledRight * uint128(10);
                }
                mut int32 magnitude = 0;
                if (scaledLeft < scaledRight) {
                    magnitude = -1;
                } else if (scaledLeft > scaledRight) {
                    magnitude = 1;
                }
                return leftNegative ? -magnitude : magnitude;
            }
            uint128 DEncodeRaw(uint128 coefficient, int32 exponent, bool negative,
                int32 width, int32 coefficientBits, int32 bias) {
                uint128 sign = negative
                    ? uint128(1) << uint128(width - 1) : uint128(0);
                uint128 encodedExponent = uint128(exponent + bias);
                uint128 shortLimit = uint128(1) << uint128(coefficientBits);
                if (coefficient < shortLimit) {
                    return sign | (encodedExponent << uint128(coefficientBits)) | coefficient;
                }
                uint128 steering = uint128(3) << uint128(width - 3);
                return sign | steering
                    | (encodedExponent << uint128(coefficientBits - 2))
                    | (coefficient - shortLimit);
            }
            uint128 DEncode(D256 coefficient, int32 exponent, int32 preferredExponent,
                bool negative, int32 width, int32 precision, int32 coefficientBits,
                int32 bias, int32 minimumQuantum, int32 maximumQuantum,
                int32 maximumAdjusted) {
                if (D256IsZero(coefficient)) {
                    mut int32 zeroExponent = preferredExponent;
                    if (zeroExponent < minimumQuantum) {
                        zeroExponent = minimumQuantum;
                    } else if (zeroExponent > maximumQuantum) {
                        zeroExponent = maximumQuantum;
                    }
                    return DEncodeRaw(uint128(0), zeroExponent, negative,
                        width, coefficientBits, bias);
                }
                int32 adjustedExponent = exponent + D256Digits(coefficient) - 1;
                if (adjustedExponent > maximumAdjusted) {
                    return DInfinity(negative, width);
                }
                if (adjustedExponent < minimumQuantum - 1) {
                    return DEncodeRaw(uint128(0), minimumQuantum, negative,
                        width, coefficientBits, bias);
                }
                mut int32 outputExponent = adjustedExponent - precision + 1;
                if (preferredExponent > outputExponent) {
                    outputExponent = preferredExponent;
                }
                if (outputExponent < minimumQuantum) {
                    outputExponent = minimumQuantum;
                } else if (outputExponent > maximumQuantum) {
                    outputExponent = maximumQuantum;
                }
                int32 shift = exponent - outputExponent;
                mut D256 rounded = D256RoundDrop(coefficient, -shift);
                uint128 precisionLimit = DPower10(precision);
                if (rounded.Low >= precisionLimit) {
                    rounded = D256Divide10(rounded);
                    outputExponent++;
                    if (outputExponent > maximumQuantum) {
                        return DInfinity(negative, width);
                    }
                }
                return DEncodeRaw(rounded.Low, outputExponent, negative,
                    width, coefficientBits, bias);
            }
            int32 DFloorLog10Rational(uint128 numerator, uint128 denominator) {
                mut uint128 numeratorRemaining = numerator;
                mut uint128 denominatorRemaining = denominator;
                mut int32 numeratorDigits = 0;
                mut int32 denominatorDigits = 0;
                while (numeratorRemaining != uint128(0)) {
                    numeratorRemaining = numeratorRemaining / uint128(10);
                    numeratorDigits++;
                }
                while (denominatorRemaining != uint128(0)) {
                    denominatorRemaining = denominatorRemaining / uint128(10);
                    denominatorDigits++;
                }
                mut int32 exponent = numeratorDigits - denominatorDigits;
                if (exponent >= 0) {
                    mut uint128 scaledDenominator = denominator;
                    for (mut int32 index = 0; index < exponent; index++) {
                        scaledDenominator = scaledDenominator * uint128(10);
                    }
                    if (numerator < scaledDenominator) {
                        exponent--;
                    }
                } else {
                    mut uint128 scaledNumerator = numerator;
                    for (mut int32 index = 0; index < -exponent; index++) {
                        scaledNumerator = scaledNumerator * uint128(10);
                    }
                    if (scaledNumerator < denominator) {
                        exponent--;
                    }
                }
                return exponent;
            }
            uint128 D128Gcd(uint128 left, uint128 right) {
                mut uint128 a = left;
                mut uint128 b = right;
                while (b != uint128(0)) {
                    mut uint128 remainder = a % b;
                    a = b;
                    b = remainder;
                }
                return a;
            }
            uint128 DRoundRationalPositive(uint128 numerator, uint128 denominator,
                uint128 shift) {
                mut uint128 quotient = numerator / denominator;
                mut uint128 remainder = numerator % denominator;
                for (mut uint128 index = uint128(0); index < shift; index++) {
                    remainder = remainder * uint128(10);
                    uint128 digit = remainder / denominator;
                    remainder = remainder % denominator;
                    quotient = quotient * uint128(10) + digit;
                }
                if (remainder * uint128(2) > denominator
                    || (remainder * uint128(2) == denominator
                        && (quotient & uint128(1)) != uint128(0))) {
                    quotient = quotient + uint128(1);
                }
                return quotient;
            }
            uint128 DRoundRationalNegative(uint128 numerator, uint128 denominator,
                uint128 droppedDigits) {
                mut uint128 quotient = numerator / denominator;
                mut uint128 remainder = numerator % denominator;
                if (droppedDigits > uint128(DDecimalDigits(quotient))) {
                    return uint128(0);
                }
                mut uint128 guard = uint128(0);
                mut bool sticky = false;
                for (mut uint128 index = uint128(0); index < droppedDigits; index++) {
                    guard = quotient % uint128(10);
                    quotient = quotient / uint128(10);
                    if (index + uint128(1) < droppedDigits && guard != uint128(0)) {
                        sticky = true;
                    }
                }
                sticky = sticky || remainder != uint128(0);
                if (guard > uint128(5)
                    || (guard == uint128(5)
                        && (sticky || (quotient & uint128(1)) != uint128(0)))) {
                    quotient = quotient + uint128(1);
                }
                return quotient;
            }
            uint128 DEncodeRational(uint128 numerator, uint128 denominator,
                uint128 metadata) {
                uint128 encodedExponent = metadata & uint128(32767);
                mut int32 exponent = 0;
                mut int32 exponentPower = 1;
                for (mut int32 bit = 0; bit < 15; bit++) {
                    if ((encodedExponent & uint128(exponentPower)) != uint128(0)) {
                        exponent = exponent + exponentPower;
                    }
                    exponentPower = exponentPower * 2;
                }
                exponent = exponent - 16384;
                bool negative = (metadata & (uint128(1) << uint128(15))) != uint128(0);
                uint128 format = (metadata >> uint128(16)) & uint128(3);
                mut int32 width = 32;
                mut int32 precision = 7;
                mut int32 coefficientBits = 23;
                mut int32 bias = 101;
                mut int32 minimumQuantum = -101;
                mut int32 maximumQuantum = 90;
                mut int32 maximumAdjusted = 96;
                if (format == uint128(1)) {
                    width = 64;
                    precision = 16;
                    coefficientBits = 53;
                    bias = 398;
                    minimumQuantum = -398;
                    maximumQuantum = 369;
                    maximumAdjusted = 384;
                } else if (format == uint128(2)) {
                    width = 128;
                    precision = 34;
                    coefficientBits = 113;
                    bias = 6176;
                    minimumQuantum = -6176;
                    maximumQuantum = 6111;
                    maximumAdjusted = 6144;
                }
                mut uint128 numeratorDigitsValue = numerator;
                mut uint128 denominatorDigitsValue = denominator;
                mut int32 numeratorDigits = 0;
                mut int32 denominatorDigits = 0;
                while (numeratorDigitsValue != uint128(0)) {
                    numeratorDigitsValue = numeratorDigitsValue / uint128(10);
                    numeratorDigits++;
                }
                while (denominatorDigitsValue != uint128(0)) {
                    denominatorDigitsValue = denominatorDigitsValue / uint128(10);
                    denominatorDigits++;
                }
                mut int32 adjustedExponent = numeratorDigits - denominatorDigits;
                if (adjustedExponent >= 0) {
                    mut uint128 scaledDenominator = denominator;
                    for (mut int32 index = 0; index < adjustedExponent; index++) {
                        scaledDenominator = scaledDenominator * uint128(10);
                    }
                    if (numerator < scaledDenominator) {
                        adjustedExponent--;
                    }
                } else {
                    mut uint128 scaledNumerator = numerator;
                    for (mut int32 index = 0; index < -adjustedExponent; index++) {
                        scaledNumerator = scaledNumerator * uint128(10);
                    }
                    if (scaledNumerator < denominator) {
                        adjustedExponent--;
                    }
                }
                adjustedExponent = adjustedExponent + exponent;
                if (adjustedExponent > maximumAdjusted) {
                    return DInfinity(negative, width);
                }
                if (adjustedExponent < minimumQuantum - 1) {
                    return DEncodeRaw(uint128(0), minimumQuantum, negative,
                        width, coefficientBits, bias);
                }
                uint128 precisionLimit = DPower10(precision);
                mut int32 outputExponent = adjustedExponent - precision + 1;
                if (outputExponent < minimumQuantum) {
                    outputExponent = minimumQuantum;
                } else if (outputExponent > maximumQuantum) {
                    outputExponent = maximumQuantum;
                }
                mut uint128 rounded = numerator / denominator;
                mut uint128 remainder = numerator - rounded * denominator;
                int32 outputShift = exponent - outputExponent;
                if (outputShift >= 0) {
                    for (mut int32 index = 0; index < outputShift; index++) {
                        remainder = remainder * uint128(10);
                        uint128 digit = remainder / denominator;
                        remainder = remainder - digit * denominator;
                        rounded = rounded * uint128(10) + digit;
                    }
                    if (remainder * uint128(2) > denominator
                        || (remainder * uint128(2) == denominator
                            && (rounded & uint128(1)) != uint128(0))) {
                        rounded = rounded + uint128(1);
                    }
                } else {
                    int32 droppedDigits = -outputShift;
                    mut uint128 valueDigits = rounded;
                    mut int32 digitCount = 0;
                    while (valueDigits != uint128(0)) {
                        valueDigits = valueDigits / uint128(10);
                        digitCount++;
                    }
                    if (droppedDigits > digitCount) {
                        rounded = uint128(0);
                    } else {
                        mut uint128 guard = uint128(0);
                        mut bool sticky = false;
                        for (mut int32 index = 0; index < droppedDigits; index++) {
                            uint128 quotient = rounded / uint128(10);
                            guard = rounded - quotient * uint128(10);
                            rounded = quotient;
                            if (index + 1 < droppedDigits && guard != uint128(0)) {
                                sticky = true;
                            }
                        }
                        sticky = sticky || remainder != uint128(0);
                        if (guard > uint128(5)
                            || (guard == uint128(5)
                                && (sticky || (rounded & uint128(1)) != uint128(0)))) {
                            rounded = rounded + uint128(1);
                        }
                    }
                }
                if (rounded >= precisionLimit) {
                    rounded = rounded / uint128(10);
                    outputExponent++;
                    if (outputExponent > maximumQuantum) {
                        return DInfinity(negative, width);
                    }
                }
                mut uint128 encoded = negative
                    ? uint128(1) << uint128(width - 1) : uint128(0);
                while (outputExponent < exponent && rounded != uint128(0)
                    && (rounded / uint128(10)) * uint128(10) == rounded) {
                    rounded = rounded / uint128(10);
                    outputExponent++;
                }
                uint128 resultExponentField = uint128(outputExponent + bias);
                uint128 shortCoefficientLimit = uint128(1) << uint128(coefficientBits);
                if (rounded < shortCoefficientLimit) {
                    return encoded
                        | (resultExponentField << uint128(coefficientBits))
                        | rounded;
                }
                return encoded | (uint128(3) << uint128(width - 3))
                    | (resultExponentField << uint128(coefficientBits - 2))
                    | (rounded - shortCoefficientLimit);
            }
            uint128 DAddCore(uint128 leftBits, uint128 rightBits, bool subtract,
                int32 width, int32 precision, int32 coefficientBits,
                int32 exponentBits, int32 bias, int32 minimumQuantum,
                int32 maximumQuantum, int32 maximumAdjusted) {
                uint128 leftCoefficient = DDecodeCoefficient(leftBits, width);
                uint128 rightCoefficient = DDecodeCoefficient(rightBits, width);
                uint128 leftClass = DClass(leftBits, width);
                uint128 rightClass = DClass(rightBits, width);
                bool leftNegative = DNegative(leftBits, width);
                bool rightNegative = DNegative(rightBits, width) != subtract;
                int32 leftExponent = DExponent(leftBits, width);
                int32 rightExponent = DExponent(rightBits, width);
                if (leftClass == uint128(3) || rightClass == uint128(3)) {
                    return DNaN(width);
                }
                if (leftClass == uint128(2) && rightClass == uint128(2)) {
                    return leftNegative == rightNegative
                        ? DInfinity(leftNegative, width) : DNaN(width);
                }
                if (leftClass == uint128(2)) {
                    return DInfinity(leftNegative, width);
                }
                if (rightClass == uint128(2)) {
                    return DInfinity(rightNegative, width);
                }
                int32 preferredExponent = leftExponent < rightExponent
                    ? leftExponent : rightExponent;
                if (leftClass == uint128(0) && rightClass == uint128(0)) {
                    bool negativeZero = leftNegative && rightNegative;
                    return DEncode(new D256(uint128(0), uint128(0), uint128(0)),
                        preferredExponent, preferredExponent, negativeZero, width,
                        precision, coefficientBits, bias, minimumQuantum,
                        maximumQuantum, maximumAdjusted);
                }
                if (leftClass == uint128(0)) {
                    return DEncode(new D256(rightCoefficient, uint128(0), uint128(0)),
                        rightExponent, preferredExponent, rightNegative, width,
                        precision, coefficientBits, bias, minimumQuantum,
                        maximumQuantum, maximumAdjusted);
                }
                if (rightClass == uint128(0)) {
                    return DEncode(new D256(leftCoefficient, uint128(0), uint128(0)),
                        leftExponent, preferredExponent, leftNegative, width,
                        precision, coefficientBits, bias, minimumQuantum,
                        maximumQuantum, maximumAdjusted);
                }
                int32 leftAdjusted = leftExponent + DDecimalDigits(leftCoefficient) - 1;
                int32 rightAdjusted = rightExponent + DDecimalDigits(rightCoefficient) - 1;
                int32 adjustedGap = leftAdjusted > rightAdjusted
                    ? leftAdjusted - rightAdjusted : rightAdjusted - leftAdjusted;
                if (adjustedGap >= precision + 2) {
                    if (leftAdjusted > rightAdjusted) {
                        return DEncode(new D256(leftCoefficient, uint128(0), uint128(0)),
                            leftExponent, preferredExponent, leftNegative, width,
                            precision, coefficientBits, bias, minimumQuantum,
                            maximumQuantum, maximumAdjusted);
                    }
                    return DEncode(new D256(rightCoefficient, uint128(0), uint128(0)),
                        rightExponent, preferredExponent, rightNegative, width,
                        precision, coefficientBits, bias, minimumQuantum,
                        maximumQuantum, maximumAdjusted);
                }
                int32 commonExponent = preferredExponent;
                D256 leftScaledCoefficient = D256Scale10(
                    new D256(leftCoefficient, uint128(0), uint128(0)),
                    leftExponent - commonExponent);
                D256 rightScaledCoefficient = D256Scale10(
                    new D256(rightCoefficient, uint128(0), uint128(0)),
                    rightExponent - commonExponent);
                mut D256 result = new D256(uint128(0), uint128(0), uint128(0));
                mut bool negativeResult = false;
                if (leftNegative == rightNegative) {
                    result = D256Add(leftScaledCoefficient, rightScaledCoefficient);
                    negativeResult = leftNegative;
                } else {
                    int32 magnitude = D256Compare(leftScaledCoefficient, rightScaledCoefficient);
                    if (magnitude == 0) {
                        return DEncode(new D256(uint128(0), uint128(0), uint128(0)),
                            commonExponent, preferredExponent, false, width,
                            precision, coefficientBits, bias, minimumQuantum,
                            maximumQuantum, maximumAdjusted);
                    }
                    if (magnitude > 0) {
                        result = D256Subtract(leftScaledCoefficient, rightScaledCoefficient);
                        negativeResult = leftNegative;
                    } else {
                        result = D256Subtract(rightScaledCoefficient, leftScaledCoefficient);
                        negativeResult = rightNegative;
                    }
                }
                return DEncode(result, commonExponent, preferredExponent,
                    negativeResult, width, precision, coefficientBits, bias,
                    minimumQuantum, maximumQuantum, maximumAdjusted);
            }
            uint128 DMultiplyCore(uint128 leftBits, uint128 rightBits,
                int32 width, int32 precision, int32 coefficientBits,
                int32 exponentBits, int32 bias, int32 minimumQuantum,
                int32 maximumQuantum, int32 maximumAdjusted) {
                uint128 leftCoefficient = DDecodeCoefficient(leftBits, width);
                uint128 rightCoefficient = DDecodeCoefficient(rightBits, width);
                uint128 leftClass = DClass(leftBits, width);
                uint128 rightClass = DClass(rightBits, width);
                bool negative = DNegative(leftBits, width) != DNegative(rightBits, width);
                int32 exponent = DExponent(leftBits, width) + DExponent(rightBits, width);
                if (leftClass == uint128(3) || rightClass == uint128(3)
                    || (leftClass == uint128(2) && rightClass == uint128(0))
                    || (rightClass == uint128(2) && leftClass == uint128(0))) {
                    return DNaN(width);
                }
                if (leftClass == uint128(2) || rightClass == uint128(2)) {
                    return DInfinity(negative, width);
                }
                if (leftClass == uint128(0) || rightClass == uint128(0)) {
                    return DEncode(new D256(uint128(0), uint128(0), uint128(0)),
                        exponent, exponent, negative, width, precision,
                        coefficientBits, bias, minimumQuantum,
                        maximumQuantum, maximumAdjusted);
                }
                D256 product = D256Multiply128(leftCoefficient, rightCoefficient);
                return DEncode(product, exponent, exponent, negative,
                    width, precision, coefficientBits, bias, minimumQuantum,
                    maximumQuantum, maximumAdjusted);
            }
            uint128 D32DivideCore(uint128 leftBits, uint128 rightBits) {
                uint128 leftCoefficient = D32Coefficient(leftBits);
                uint128 rightCoefficient = D32Coefficient(rightBits);
                uint128 leftClass = DClass(leftBits, 32);
                uint128 rightClass = DClass(rightBits, 32);
                bool negative = DNegative(leftBits, 32) != DNegative(rightBits, 32);
                int32 leftExponent = DExponent(leftBits, 32);
                int32 rightExponent = DExponent(rightBits, 32);
                if (leftClass == uint128(3) || rightClass == uint128(3)
                    || (leftClass == uint128(2) && rightClass == uint128(2))
                    || (leftClass == uint128(0) && rightClass == uint128(0))) {
                    return DNaN(32);
                }
                if (leftClass == uint128(2)) {
                    return DInfinity(negative, 32);
                }
                if (rightClass == uint128(2)) {
                    return DEncode(new D256(uint128(0), uint128(0), uint128(0)),
                        -101, -101, negative, 32, 7, 23, 101, -101, 90, 96);
                }
                if (rightClass == uint128(0)) {
                    return DInfinity(negative, 32);
                }
                int32 exponent = leftExponent - rightExponent;
                if (leftClass == uint128(0)) {
                    return DEncode(new D256(uint128(0), uint128(0), uint128(0)),
                        exponent, exponent, negative, 32, 7, 23, 101, -101, 90, 96);
                }
                mut uint128 metadata = uint128(exponent + 16384);
                if (negative) {
                    metadata = metadata | (uint128(1) << uint128(15));
                }
                return DEncodeRational(leftCoefficient, rightCoefficient, metadata);
            }
            uint128 D64DivideCore(uint128 leftBits, uint128 rightBits) {
                uint128 leftCoefficient = D64Coefficient(leftBits);
                uint128 rightCoefficient = D64Coefficient(rightBits);
                uint128 leftClass = DClass(leftBits, 64);
                uint128 rightClass = DClass(rightBits, 64);
                bool negative = DNegative(leftBits, 64) != DNegative(rightBits, 64);
                int32 leftExponent = DExponent(leftBits, 64);
                int32 rightExponent = DExponent(rightBits, 64);
                if (leftClass == uint128(3) || rightClass == uint128(3)
                    || (leftClass == uint128(2) && rightClass == uint128(2))
                    || (leftClass == uint128(0) && rightClass == uint128(0))) {
                    return DNaN(64);
                }
                if (leftClass == uint128(2)) {
                    return DInfinity(negative, 64);
                }
                if (rightClass == uint128(2)) {
                    return DEncode(new D256(uint128(0), uint128(0), uint128(0)),
                        -398, -398, negative, 64, 16, 53, 398, -398, 369, 384);
                }
                if (rightClass == uint128(0)) {
                    return DInfinity(negative, 64);
                }
                int32 exponent = leftExponent - rightExponent;
                if (leftClass == uint128(0)) {
                    return DEncode(new D256(uint128(0), uint128(0), uint128(0)),
                        exponent, exponent, negative, 64, 16, 53, 398, -398, 369, 384);
                }
                mut uint128 metadata = uint128(exponent + 16384)
                    | (uint128(1) << uint128(16));
                if (negative) {
                    metadata = metadata | (uint128(1) << uint128(15));
                }
                return DEncodeRational(leftCoefficient, rightCoefficient, metadata);
            }
            uint128 D128DivideCore(uint128 leftBits, uint128 rightBits) {
                uint128 leftCoefficient = D128Coefficient(leftBits);
                uint128 rightCoefficient = D128Coefficient(rightBits);
                uint128 leftClass = DClass(leftBits, 128);
                uint128 rightClass = DClass(rightBits, 128);
                bool negative = DNegative(leftBits, 128) != DNegative(rightBits, 128);
                int32 leftExponent = DExponent(leftBits, 128);
                int32 rightExponent = DExponent(rightBits, 128);
                if (leftClass == uint128(3) || rightClass == uint128(3)
                    || (leftClass == uint128(2) && rightClass == uint128(2))
                    || (leftClass == uint128(0) && rightClass == uint128(0))) {
                    return DNaN(128);
                }
                if (leftClass == uint128(2)) {
                    return DInfinity(negative, 128);
                }
                if (rightClass == uint128(2)) {
                    return DEncode(new D256(uint128(0), uint128(0), uint128(0)),
                        -6176, -6176, negative, 128, 34, 113, 6176, -6176, 6111, 6144);
                }
                if (rightClass == uint128(0)) {
                    return DInfinity(negative, 128);
                }
                int32 exponent = leftExponent - rightExponent;
                if (leftClass == uint128(0)) {
                    return DEncode(new D256(uint128(0), uint128(0), uint128(0)),
                        exponent, exponent, negative, 128, 34, 113, 6176, -6176, 6111, 6144);
                }
                mut uint128 metadata = uint128(exponent + 16384)
                    | (uint128(2) << uint128(16));
                if (negative) {
                    metadata = metadata | (uint128(1) << uint128(15));
                }
                return DEncodeRational(leftCoefficient, rightCoefficient, metadata);
            }
            uint128 DRemainderCore(uint128 leftBits, uint128 rightBits,
                int32 width, int32 precision, int32 coefficientBits,
                int32 exponentBits, int32 bias, int32 minimumQuantum,
                int32 maximumQuantum, int32 maximumAdjusted) {
                uint128 leftCoefficient = DDecodeCoefficient(leftBits, width);
                uint128 rightCoefficient = DDecodeCoefficient(rightBits, width);
                uint128 leftClass = DClass(leftBits, width);
                uint128 rightClass = DClass(rightBits, width);
                int32 leftExponent = DExponent(leftBits, width);
                int32 rightExponent = DExponent(rightBits, width);
                bool leftNegative = DNegative(leftBits, width);
                if (leftClass == uint128(3) || rightClass == uint128(3)
                    || leftClass == uint128(2) || rightClass == uint128(0)) {
                    return DNaN(width);
                }
                if (rightClass == uint128(2)) {
                    return leftBits;
                }
                int32 preferredExponent = leftExponent < rightExponent
                    ? leftExponent : rightExponent;
                if (leftClass == uint128(0)) {
                    return DEncode(new D256(uint128(0), uint128(0), uint128(0)),
                        preferredExponent, preferredExponent, leftNegative,
                        width, precision, coefficientBits, bias, minimumQuantum,
                        maximumQuantum, maximumAdjusted);
                }
                mut uint128 remainder = uint128(0);
                if (leftExponent >= rightExponent) {
                    remainder = leftCoefficient % rightCoefficient;
                    int32 exponentGap = leftExponent - rightExponent;
                    for (mut int32 index = 0; index < exponentGap; index++) {
                        remainder = (remainder * uint128(10)) % rightCoefficient;
                    }
                } else {
                    if (DCompareMagnitude(leftCoefficient, leftExponent,
                        rightCoefficient, rightExponent) < 0) {
                        return DEncode(new D256(leftCoefficient, uint128(0), uint128(0)),
                            leftExponent, preferredExponent, leftNegative,
                            width, precision, coefficientBits, bias, minimumQuantum,
                            maximumQuantum, maximumAdjusted);
                    }
                    uint128 scaledDivisor = D128Scale10(rightCoefficient,
                        rightExponent - leftExponent);
                    remainder = leftCoefficient % scaledDivisor;
                }
                return DEncode(new D256(remainder, uint128(0), uint128(0)),
                    preferredExponent, preferredExponent, leftNegative, width,
                    precision, coefficientBits, bias, minimumQuantum,
                    maximumQuantum, maximumAdjusted);
            }
            uint128 DNegate(uint128 bits, int32 width) {
                return bits ^ (uint128(1) << uint128(width - 1));
            }
            uint128 D32Add(uint128 left, uint128 right) {
                return DAddCore(left, right, false, 32, 7, 23, 8, 101, -101, 90, 96);
            }
            uint128 D32Subtract(uint128 left, uint128 right) {
                return DAddCore(left, right, true, 32, 7, 23, 8, 101, -101, 90, 96);
            }
            uint128 D32Multiply(uint128 left, uint128 right) {
                return DMultiplyCore(left, right, 32, 7, 23, 8, 101, -101, 90, 96);
            }
            uint128 D32Divide(uint128 left, uint128 right) {
                return D32DivideCore(left, right);
            }
            uint128 D32Remainder(uint128 left, uint128 right) {
                return DRemainderCore(left, right, 32, 7, 23, 8, 101, -101, 90, 96);
            }
            int32 D32Compare(uint128 left, uint128 right) {
                return DCompare(left, right, 32);
            }
            uint128 D32Negate(uint128 bits) { return DNegate(bits, 32); }
            uint128 D64Add(uint128 left, uint128 right) {
                return DAddCore(left, right, false, 64, 16, 53, 10, 398, -398, 369, 384);
            }
            uint128 D64Subtract(uint128 left, uint128 right) {
                return DAddCore(left, right, true, 64, 16, 53, 10, 398, -398, 369, 384);
            }
            uint128 D64Multiply(uint128 left, uint128 right) {
                return DMultiplyCore(left, right, 64, 16, 53, 10, 398, -398, 369, 384);
            }
            uint128 D64Divide(uint128 left, uint128 right) {
                return D64DivideCore(left, right);
            }
            uint128 D64Remainder(uint128 left, uint128 right) {
                return DRemainderCore(left, right, 64, 16, 53, 10, 398, -398, 369, 384);
            }
            int32 D64Compare(uint128 left, uint128 right) {
                return DCompare(left, right, 64);
            }
            uint128 D64Negate(uint128 bits) { return DNegate(bits, 64); }
            uint128 D128Add(uint128 left, uint128 right) {
                return DAddCore(left, right, false, 128, 34, 113, 14, 6176, -6176, 6111, 6144);
            }
            uint128 D128Subtract(uint128 left, uint128 right) {
                return DAddCore(left, right, true, 128, 34, 113, 14, 6176, -6176, 6111, 6144);
            }
            uint128 D128Multiply(uint128 left, uint128 right) {
                return DMultiplyCore(left, right, 128, 34, 113, 14, 6176, -6176, 6111, 6144);
            }
            uint128 D128Divide(uint128 left, uint128 right) {
                return D128DivideCore(left, right);
            }
            uint128 D128Remainder(uint128 left, uint128 right) {
                return DRemainderCore(left, right, 128, 34, 113, 14, 6176, -6176, 6111, 6144);
            }
            int32 D128Compare(uint128 left, uint128 right) {
                return DCompare(left, right, 128);
            }
            uint128 D128Negate(uint128 bits) { return DNegate(bits, 128); }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                SourceArithmeticProfile = SourceArithmeticProfile.Release,
            });
        if (compilation.HasErrors || compilation.Program is not BoundProgram program)
        {
            throw new InvalidOperationException(
                "The compiler-owned BID decimal integer helper failed semantic verification: "
                + string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        var code = new PeCompiler(program, managedNodes: false, library: true).Compile();
        if (code.Data.Length != 0 || !code.DataFixups.IsEmpty)
        {
            throw new InvalidOperationException(
                "The compiler-owned BID decimal integer helper must not depend on image data.");
        }
        var functionOffsets = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < program.Functions.Length; index++)
        {
            var function = program.Functions[index];
            if (!function.IsExternal)
            {
                functionOffsets.Add(function.Name, code.SourceFunctionOffsets[index]);
            }
        }
        return new BidDecimalRuntime(code, functionOffsets.ToImmutable());
    }

    private void EmitBinary128Call(string function, int left, int right, int result)
    {
        _code.Address(result);
        _code.MoveArgument(0, true);
        _code.Address(left);
        _code.MoveArgument(1, true);
        _code.Address(right);
        _code.MoveArgument(2, true);
        _binary128Calls.Add((_code.RelativeCall(), function));
        _code.Address(result);
    }

    private void EmitBinary128Comparison(TokenKind operation, int left, int right)
    {
        var function = operation switch {
            TokenKind.EqualsEquals => "F128Equal",
            TokenKind.BangEquals => "F128NotEqual",
            TokenKind.Less => "F128Less",
            TokenKind.LessEquals => "F128LessOrEqual",
            TokenKind.Greater => "F128Greater",
            TokenKind.GreaterEquals => "F128GreaterOrEqual",
            _ => throw new InvalidOperationException("The verifier admitted an unsupported binary128 comparison."),
        };
        _code.Address(left);
        _code.MoveArgument(0, true);
        _code.Address(right);
        _code.MoveArgument(1, true);
        _binary128Calls.Add((_code.RelativeCall(), function));
    }

    private void EmitBinary128Arithmetic(TokenKind operation, int left, int right, int result)
    {
        var function = operation switch {
            TokenKind.Plus => "F128Add",
            TokenKind.Minus => "F128Subtract",
            TokenKind.Star => "F128Multiply",
            TokenKind.Slash => "F128Divide",
            TokenKind.Percent => "F128Remainder",
            _ => throw new InvalidOperationException("The verifier admitted an unsupported binary128 operator."),
        };
        EmitBinary128Call(function, left, right, result);
    }

    private void EmitBidDecimalArithmetic(PrimitiveType type, TokenKind operation,
        int left, int right, int result)
    {
        var prefix = type switch {
            PrimitiveType.Decimal32 => "D32",
            PrimitiveType.Decimal64 => "D64",
            PrimitiveType.Decimal128 => "D128",
            _ => throw new InvalidOperationException("The verifier admitted a non-BID decimal type."),
        };
        var suffix = operation switch {
            TokenKind.Plus => "Add",
            TokenKind.Minus => "Subtract",
            TokenKind.Star => "Multiply",
            TokenKind.Slash => "Divide",
            TokenKind.Percent => "Remainder",
            _ => throw new InvalidOperationException("The verifier admitted an unsupported BID decimal operator."),
        };
        EmitBidDecimalCall(prefix + suffix, left, right, result);
    }

    private void EmitBidDecimalCall(string function, int left, int right, int result)
    {
        _code.Address(result);
        _code.MoveArgument(0, true);
        _code.Address(left);
        _code.MoveArgument(1, true);
        _code.Address(right);
        _code.MoveArgument(2, true);
        _bidDecimalCalls.Add((_code.RelativeCall(), function));
        _code.Address(result);
    }

    private void EmitBidDecimalComparison(TokenKind operation, PrimitiveType type,
        int left, int right)
    {
        var function = type switch {
            PrimitiveType.Decimal32 => "D32Compare",
            PrimitiveType.Decimal64 => "D64Compare",
            PrimitiveType.Decimal128 => "D128Compare",
            _ => throw new InvalidOperationException("The verifier admitted a non-BID decimal comparison."),
        };
        _code.Address(left);
        _code.MoveArgument(0, true);
        _code.Address(right);
        _code.MoveArgument(1, true);
        _bidDecimalCalls.Add((_code.RelativeCall(), function));
        _code.Bytes(0x83, 0xF8, 0x02);
        var ordered = _code.JumpIf(0x85);
        _code.Bytes(0xB8);
        _code.Int32(operation == TokenKind.BangEquals ? 1 : 0);
        var unordered = _code.Jump();
        _code.PatchRelative(ordered, _code.Position);
        _code.Bytes(0x83, 0xF8, 0x00);
        var condition = operation switch {
            TokenKind.EqualsEquals => (byte)0x94,
            TokenKind.BangEquals => (byte)0x95,
            TokenKind.Less => (byte)0x9C,
            TokenKind.LessEquals => (byte)0x9E,
            TokenKind.Greater => (byte)0x9F,
            TokenKind.GreaterEquals => (byte)0x9D,
            _ => throw new InvalidOperationException("The verifier admitted an unsupported BID comparison."),
        };
        _code.Bytes(0x0F, condition, 0xC0, 0x0F, 0xB6, 0xC0);
        _code.PatchRelative(unordered, _code.Position);
    }

    private void EmitBidDecimalNegate(PrimitiveType type, int value, int result)
    {
        var function = type switch {
            PrimitiveType.Decimal32 => "D32Negate",
            PrimitiveType.Decimal64 => "D64Negate",
            PrimitiveType.Decimal128 => "D128Negate",
            _ => throw new InvalidOperationException("The verifier admitted a non-BID decimal sign operation."),
        };
        _code.Address(result);
        _code.MoveArgument(0, true);
        _code.Address(value);
        _code.MoveArgument(1, true);
        _bidDecimalCalls.Add((_code.RelativeCall(), function));
    }

    private void EmitBidDecimalOperand(PrimitiveType type, int destination) =>
        StoreValue(Spill(destination), type);

    private void EmitBidDecimalResult(PrimitiveType type, int result)
    {
        if (type == PrimitiveType.Decimal128)
        {
            _code.Address(Spill(result));
        }
        else
        {
            LoadValue(Spill(result), type);
        }
    }

    private static (ulong Low, ulong High) BidDecimalOne(PrimitiveType type) => type switch {
        PrimitiveType.Decimal32 => (0x3280_0001, 0),
        PrimitiveType.Decimal64 => (0x31C0_0000_0000_0001, 0),
        PrimitiveType.Decimal128 => (1, 0x3040_0000_0000_0000),
        _ => throw new InvalidOperationException("A BID decimal one requires a decimal format."),
    };

    private static void EnsureSupportedNativeFloatingOperator(PrimitiveType type)
    {
        if (PrimitiveTypeFacts.IsFloating(type)
            && type is not (PrimitiveType.Float16 or PrimitiveType.BFloat16
                or PrimitiveType.Float32 or PrimitiveType.Float64 or PrimitiveType.Float128
                or PrimitiveType.Decimal32 or PrimitiveType.Decimal64 or PrimitiveType.Decimal128))
        {
            throw new NotSupportedException(
                "Native floating operator lowering does not support this floating format.");
        }
    }
    private static void EnsureSupportedNativeFloatingUnary(PrimitiveType type, TokenKind operation)
    {
        if (PrimitiveTypeFacts.IsFloating(type)
            && (type is not (PrimitiveType.Float16 or PrimitiveType.BFloat16
                or PrimitiveType.Float32 or PrimitiveType.Float64 or PrimitiveType.Float128
                or PrimitiveType.Decimal32 or PrimitiveType.Decimal64 or PrimitiveType.Decimal128)
                || operation is not (TokenKind.Plus or TokenKind.Minus)))
        {
            throw new NotSupportedException(
                "Native floating unary lowering does not support this format or operation.");
        }
    }
    private static bool IsFloatingComparison(TokenKind operation) =>
        operation is TokenKind.EqualsEquals or TokenKind.BangEquals or TokenKind.Less
            or TokenKind.LessEquals or TokenKind.Greater or TokenKind.GreaterEquals;
    private static bool IsBidDecimal(PrimitiveType type) =>
        type is PrimitiveType.Decimal32 or PrimitiveType.Decimal64 or PrimitiveType.Decimal128;

    private static void EnsureSupportedNativeFloatingBinary(
        PrimitiveType type, TokenKind operation)
    {
        var narrowComparison = type is PrimitiveType.Float16 or PrimitiveType.BFloat16
            && IsFloatingComparison(operation);
        var narrowAddOrSubtract = type is PrimitiveType.Float16 or PrimitiveType.BFloat16
            && operation is TokenKind.Plus or TokenKind.Minus;
        var narrowMultiply = type is PrimitiveType.Float16 or PrimitiveType.BFloat16
            && operation == TokenKind.Star;
        var narrowDivide = type is PrimitiveType.Float16 or PrimitiveType.BFloat16
            && operation == TokenKind.Slash;
        var narrowRemainder = type is PrimitiveType.Float16 or PrimitiveType.BFloat16
            && operation == TokenKind.Percent;
        var binary128Operator = type == PrimitiveType.Float128
            && operation is TokenKind.Plus or TokenKind.Minus or TokenKind.Star
                or TokenKind.Slash or TokenKind.Percent;
        var binary128Comparison = type == PrimitiveType.Float128
            && IsFloatingComparison(operation);
        var bidDecimalOperator = type is (PrimitiveType.Decimal32 or PrimitiveType.Decimal64
                or PrimitiveType.Decimal128)
            && operation is (TokenKind.Plus or TokenKind.Minus or TokenKind.Star
                or TokenKind.Slash or TokenKind.Percent);
        var bidDecimalComparison = type is (PrimitiveType.Decimal32 or PrimitiveType.Decimal64
                or PrimitiveType.Decimal128)
            && IsFloatingComparison(operation);
        if (PrimitiveTypeFacts.IsFloating(type)
            && type is not (PrimitiveType.Float32 or PrimitiveType.Float64)
            && !narrowComparison && !narrowAddOrSubtract && !narrowMultiply
            && !narrowDivide && !narrowRemainder
            && !binary128Operator && !binary128Comparison
            && !bidDecimalOperator && !bidDecimalComparison)
        {
            throw new NotSupportedException(
                "Native floating binary lowering does not support this format or operation.");
        }
    }
    private bool IsIndirect(PrimitiveType type) => type == PrimitiveType.TokenSpan
        || type is PrimitiveType.Int128 or PrimitiveType.UInt128 or PrimitiveType.Float128
            or PrimitiveType.Decimal128
        || IsStruct(type) && Size(type) is not (1 or 2 or 4 or 8);
    private bool IsIndirectResult(PrimitiveType type) =>
        type != PrimitiveType.Float128 && IsIndirect(type);
    private static bool IsFloatingCallCarrier(PrimitiveType type) =>
        type is PrimitiveType.Float16 or PrimitiveType.BFloat16
            or PrimitiveType.Float32 or PrimitiveType.Float64;
    private bool IsUnique(PrimitiveType type) =>
        type == PrimitiveType.OwnedInt32Buffer
            || (IsStruct(type) && _program.Structures[(int)type - 100].IsUnique);
    private bool HasDeclaredCleanup(PrimitiveType type) =>
        IsStruct(type) && _program.Structures[(int)type - 100].CleanupMethod is not null;
    private bool IsBorrowedReceiver()
    {
        var function = _program.Functions[_currentFunction];
        return function.DeclaringStruct is not null && !function.IsConstructor && !function.IsCleanup
            && !function.IsStatic && (IsUnique(function.Variables[0].Type)
                || (IsStruct(function.Variables[0].Type) && function.Variables[0].IsMutable));
    }

    private void EmitUniqueFieldPlace(BoundExpression expression)
    {
        if (expression is BoundRead root)
        {
            if (root.Variable == 0 && IsBorrowedReceiver())
            {
                _code.Load64(Slot(0));
            }
            else
            {
                _code.Address(OwnerPlace(root.Variable));
            }
            return;
        }
        var fieldRead = (BoundFieldRead)expression;
        EmitUniqueFieldPlace(fieldRead.Receiver);
        var field = _program.Structures[(int)fieldRead.Receiver.Type - 100].Fields[fieldRead.Field];
        _code.PointerOffset(field.Offset);
    }

    private bool UpdateAddress(BoundExpression place, int spill)
    {
        switch (place)
        {
            case BoundRead read:
                if (read.Variable == 0 && IsBorrowedReceiver())
                {
                    _code.Load64(Slot(0));
                }
                else
                {
                    if (_program.Functions[_currentFunction].Variables[read.Variable].IsImplicitRef)
                    {
                        _code.Load64(Slot(read.Variable));
                    }
                    else
                    {
                        _code.Address(OwnerPlace(read.Variable));
                    }
                }
                return false;
            case BoundBufferRead buffer:
                EmitBufferIndex(buffer.Buffer, buffer.Index, buffer.IsEndRelativeOffset, spill);
                _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _bufferOffsets[buffer.Buffer]));
                _code.Bytes(0x48, 0x8D, 0x04, 0x82); // lea rax,[rdx+rax*4]
                return false;
            case BoundViewRead view:
                EmitViewElementAddress(view, spill);
                return false;
            case BoundFieldRead field:
                bool rooted;
                int offset;
                if (ManagedClassTypes.IsClass(field.Receiver.Type))
                {
                    Expression(field.Receiver, spill);
                    _code.Store64(Spill(spill));
                    SetManagedRoot(Spill(spill), active: true);
                    offset = 32 + _program.Classes[ManagedClassTypes.ClassIndex(field.Receiver.Type)]
                        .Fields[field.Field].Offset;
                    rooted = true;
                }
                else
                {
                    rooted = UpdateAddress(field.Receiver, spill);
                    offset = _program.Structures[(int)field.Receiver.Type - 100].Fields[field.Field].Offset;
                }
                _code.PointerOffset(offset);
                return rooted;
            default:
                throw new InvalidOperationException("Verified update requires a writable place.");
        }
    }

    private void EmitViewElementAddress(BoundViewRead view, int spill)
    {
        Expression(view.Index, spill);
        _code.Bytes(0x89, 0xC1); // ecx = index
        _code.Load(Slot(view.Variable) + 8);
        _code.Bytes(0x39, 0xC1); // unsigned index >= length also rejects negative indexes
        RuntimeBranch(0x83, 2);
        _code.Load64(Slot(view.Variable), destinationRegister: true);
        _code.Bytes(0x48, 0x8D, 0x04, 0x8A); // lea rax,[rdx+rcx*4]
    }

    private void DropOwner(int variable)
    {
        var live = _liveSlots[_currentFunction][variable];
        _code.Load(live);
        _code.Bytes(0x85, 0xC0);
        var vacant = _code.JumpIf(0x84);
        var type = _program.Functions[_currentFunction].Variables[variable].Type;
        _code.Clear(live);
        EmitDropValue(OwnerPlace(variable), type);
        _code.PatchRelative(vacant, _code.Position);
    }

    private void EmitBufferIntrinsic(BoundIntrinsic intrinsic)
    {
        switch (intrinsic)
        {
            case BoundIntrinsic.BufferTryCreate:
                _code.Load(Slot(0));
                _code.Bytes(0x85, 0xC0);
                RuntimeBranch(0x88, 0);
                _code.Byte(0x3D);
                _code.Int32(NativeBufferIntrinsics.MaximumCapacity);
                RuntimeBranch(0x8F, 0);
                _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "GetProcessHeap"));
                _code.MoveArgument(0, true);
                _code.Bytes(0x31, 0xD2); // HeapAlloc flags = 0
                _code.Load(Slot(0));
                _code.Bytes(0x4C, 0x8D, 0x04, 0x85);
                _code.Int32(8); // r8 = sizeof(header) + capacity * sizeof(int32)
                _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "HeapAlloc"));
                _code.Bytes(0x48, 0x85, 0xC0);
                var allocationFailed = _code.JumpIf(0x84);
                _code.Bytes(0x48, 0x89, 0xC1); // rcx = allocated header
                _code.Bytes(0xC7, 0x01);
                _code.Int32(0); // initialized prefix is empty
                _code.Load(Slot(0));
                _code.Bytes(0x89, 0x41, 0x04);
                _code.Load64(Slot(1));
                _code.Bytes(0x48, 0x89, 0x08); // publish owner only after header initialization
                _code.Bytes(0xB8);
                _code.Int32(1);
                var created = _code.Jump();
                _code.PatchRelative(allocationFailed, _code.Position);
                _code.Bytes(0x31, 0xC0);
                _code.PatchRelative(created, _code.Position);
                break;
            case BoundIntrinsic.BufferCount:
            case BoundIntrinsic.BufferCapacity:
                _code.Load64(Slot(0));
                _code.Bytes(0x48, 0x8B, 0x00); // receiver place -> opaque owner handle
                _code.Bytes(0x8B, intrinsic == BoundIntrinsic.BufferCount ? (byte)0x00 : (byte)0x40);
                if (intrinsic == BoundIntrinsic.BufferCapacity)
                {
                    _code.Byte(0x04);
                }
                break;
            case BoundIntrinsic.BufferRead:
            case BoundIntrinsic.BufferSet:
                _code.Load64(Slot(0));
                _code.Bytes(0x48, 0x8B, 0x00, 0x48, 0x89, 0xC2); // rdx = owner handle
                _code.Load(Slot(1));
                _code.Bytes(0x3B, 0x02); // unsigned index >= count includes negative indexes
                RuntimeBranch(0x83, 2);
                if (intrinsic == BoundIntrinsic.BufferRead)
                {
                    _code.Bytes(0x8B, 0x44, 0x82, 0x08);
                }
                else
                {
                    _code.Bytes(0x89, 0xC1); // rcx = checked index
                    _code.Load(Slot(2));
                    _code.Bytes(0x89, 0x44, 0x8A, 0x08);
                }
                break;
            case BoundIntrinsic.BufferTryAppend:
                _code.Load64(Slot(0));
                _code.Bytes(0x48, 0x8B, 0x00, 0x48, 0x89, 0xC2);
                _code.Bytes(0x8B, 0x02, 0x3B, 0x42, 0x04); // count < capacity
                var full = _code.JumpIf(0x83);
                _code.Bytes(0x89, 0xC1);
                _code.Load(Slot(1));
                _code.Bytes(0x89, 0x44, 0x8A, 0x08); // initialize element before publishing count
                _code.Bytes(0xFF, 0x02, 0xB8);
                _code.Int32(1);
                var appended = _code.Jump();
                _code.PatchRelative(full, _code.Position);
                _code.Bytes(0x31, 0xC0);
                _code.PatchRelative(appended, _code.Position);
                break;
            case BoundIntrinsic.BufferClear:
                _code.Load64(Slot(0));
                _code.Bytes(0x48, 0x8B, 0x00, 0xC7, 0x00);
                _code.Int32(0);
                break;
            case BoundIntrinsic.None or BoundIntrinsic.NodeTryCreate or BoundIntrinsic.NodeValue
                or BoundIntrinsic.NodeSetValue or BoundIntrinsic.NodeSetNext or BoundIntrinsic.NodeClearNext
                or BoundIntrinsic.NodeTryGetNext or BoundIntrinsic.NodeCollect or BoundIntrinsic.ClassTryCreate:
            default:
                throw new InvalidOperationException("Unknown verified native buffer intrinsic.");
        }
    }

    private void EmitDropValue(int place, PrimitiveType type)
    {
        if (type == PrimitiveType.OwnedInt32Buffer)
        {
            _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "GetProcessHeap"));
            _code.MoveArgument(0, true);
            _code.Bytes(0x31, 0xD2); // HeapFree flags = 0
            _code.Load64(place);
            _code.Bytes(0x49, 0x89, 0xC0); // r8 = owner handle
            _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "HeapFree"));
            _code.Bytes(0x85, 0xC0);
            var released = _code.JumpIf(0x85);
            _code.Bytes(0xB9);
            _code.Int32(unchecked((int)0xE0540005));
            _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), "KERNEL32.dll", "ExitProcess"));
            _code.Bytes(0x0F, 0x0B); // ExitProcess cannot return
            _code.PatchRelative(released, _code.Position);
            return;
        }
        var structure = _program.Structures[(int)type - 100];
        var name = structure.CleanupMethod;
        if (name is not null)
        {
            var method = -1;
            for (var index = 0; index < _program.Functions.Length; index++)
            {
                if (_program.Functions[index].Name == name)
                {
                    method = index;
                    break;
                }
            }
            if (method < 0)
            {
                throw new InvalidOperationException("Verified unique cleanup method is missing.");
            }
            if (IsIndirect(type))
            {
                var scratch = _cleanupArgumentSlots[_currentFunction];
                if (scratch < 0)
                {
                    throw new InvalidOperationException("Indirect cleanup requires dedicated argument storage.");
                }
                _code.Address(place);
                CopyToFrame(scratch, Size(type));
                _code.Address(scratch);
            }
            else
            {
                LoadValue(place, type);
            }
            _code.MoveArgument(0, true);
            EmitTargetCall(method);
        }
        for (var field = structure.Fields.Length - 1; field >= 0; field--)
        {
            if (IsUnique(structure.Fields[field].Type))
            {
                EmitDropValue(place + structure.Fields[field].Offset, structure.Fields[field].Type);
            }
        }
    }
    private void StoreValue(int offset, PrimitiveType type)
    {
        if (IsIndirect(type))
        {
            CopyToFrame(offset, Size(type));
        }
        else if (IsStruct(type) || IsWide(type))
        {
            _code.Store64(offset);
        }
        else
        {
            _code.Store(offset);
        }
    }
    private void LoadValue(int offset, PrimitiveType type)
    {
        if (IsIndirect(type))
        {
            _code.Address(offset);
        }
        else if (IsStruct(type) || IsWide(type))
        {
            _code.Load64(offset);
        }
        else
        {
            _code.Load(offset);
        }
    }

    private void EmitOptionalPresenceInAccumulator(PrimitiveType optionalType)
    {
        var payloadSize = Size(TrRmOptionalScalar.PayloadType(optionalType));
        if (IsIndirect(optionalType))
        {
            _code.Bytes(0x0F, 0xB6, 0x80);
            _code.Int32(payloadSize);
        }
        else
        {
            _code.Bytes(0x48, 0xC1, 0xE8, (byte)(payloadSize * 8), 0x83, 0xE0, 0x01);
        }
    }

    private void LoadOptionalPresence(int offset, PrimitiveType optionalType)
    {
        if (IsIndirect(optionalType))
        {
            _code.Address(offset);
        }
        else
        {
            _code.Load64(offset);
        }
        EmitOptionalPresenceInAccumulator(optionalType);
    }

    private void EmitOptionalEquality(TrRmScalarOptionalEquality equality, int spill)
    {
        var type = equality.Left.Type;
        var left = Spill(spill);
        var right = Spill(spill + 1);
        Expression(equality.Left, spill + 2);
        StoreValue(left, type);
        Expression(equality.Right, spill + 2);
        StoreValue(right, type);

        LoadOptionalPresence(left, type);
        _code.Bytes(0x85, 0xC0);
        var leftAbsent = _code.JumpIf(0x84);
        LoadOptionalPresence(right, type);
        _code.Bytes(0x85, 0xC0);
        var rightAbsent = _code.JumpIf(0x84);
        LoadValue(left, equality.PayloadType);
        if (IsWide(equality.PayloadType))
        {
            _code.Bytes(0x48, 0x89, 0xC1);
        }
        else
        {
            _code.Bytes(0x89, 0xC1);
        }
        LoadValue(right, equality.PayloadType);
        if (IsWide(equality.PayloadType))
        {
            _code.Bytes(0x48, 0x39, 0xC8);
        }
        else
        {
            _code.Bytes(0x39, 0xC8);
        }
        _code.Bytes(0x0F, equality.Negated ? (byte)0x95 : (byte)0x94, 0xC0);
        _code.Bytes(0x0F, 0xB6, 0xC0);
        var compared = _code.Jump();

        _code.PatchRelative(leftAbsent, _code.Position);
        LoadOptionalPresence(right, type);
        _code.Bytes(0x85, 0xC0);
        var leftAbsentRightPresent = _code.JumpIf(0x85);
        _code.Byte(0xB8);
        _code.Int32(equality.Negated ? 0 : 1);
        var bothAbsent = _code.Jump();

        _code.PatchRelative(rightAbsent, _code.Position);
        _code.Byte(0xB8);
        _code.Int32(equality.Negated ? 1 : 0);
        var mismatchedPresence = _code.Jump();

        _code.PatchRelative(leftAbsentRightPresent, _code.Position);
        _code.Byte(0xB8);
        _code.Int32(equality.Negated ? 1 : 0);

        _code.PatchRelative(bothAbsent, _code.Position);
        _code.PatchRelative(mismatchedPresence, _code.Position);
        _code.PatchRelative(compared, _code.Position);
    }

    private void CopyToFrame(int destination, int size)
    {
        _code.AddressToRdx(destination);
        _code.CopyMemory(size);
    }

    private void ZeroStruct(int destination, int size)
    {
        _code.Bytes(0x31, 0xC0);
        var offset = 0;
        while (offset < size)
        {
            var width = Math.Min(4, size - offset);
            if (width == 3)
            {
                width = 2;
            }
            _code.StoreField(destination + offset, width);
            offset += width;
        }
    }

    private void Prolog(int frame)
    {
        // Entry rsp is 8 mod 16. Subtracting 8 mod 16 aligns rsp at each call site.
        _code.Bytes(0x48, 0x81, 0xEC);
        _code.Int32(frame);
    }

    private void Epilog()
    {
        _code.Bytes(0x48, 0x81, 0xC4);
        _code.Int32(_frame - (_code.UseFramePointer ? 8 : 0));
        if (_code.UseFramePointer)
        {
            _code.Byte(0x5D); // pop rbp
        }
        _code.Byte(0xC3);
    }

    private void RuntimeBranch(byte condition, int target)
    {
        // Transfer through a call, not a jump: the helper needs the ordinary
        // Windows entry alignment (rsp == 8 mod 16) and a valid unwind chain.
        var skip = _code.JumpIf((byte)(condition ^ 1));
        _runtimeCalls.Add((_code.RelativeCall(), target));
        _code.PatchRelative(skip, _code.Position);
    }

    private void FloatingRuntimeBranch(byte condition, int target, int savedMxcsr, int controlledMxcsr)
    {
        var skip = _code.JumpIf((byte)(condition ^ 1));
        _code.LoadMxcsr(savedMxcsr);
        _runtimeCalls.Add((_code.RelativeCall(), target));
        _code.LoadMxcsr(controlledMxcsr);
        _code.PatchRelative(skip, _code.Position);
    }

    private void CallCleanup(int index)
    {
        _code.Bytes(0x48, 0x89, 0xEA); // mov rdx,rbp: pass the owning function's frame base
        _code.Bytes(0x31, 0xC9); // normal cleanup has no exception record
        _cleanupCalls.Add((_code.RelativeCall(), index));
    }

    private void EmitTargetCall(int function)
    {
        if (_program.Functions[function].IsExternal)
        {
            if (_nativeImports is null || !_nativeImports.TryGetValue(function, out var import))
            {
                throw new InvalidOperationException("External call has no verified native import binding.");
            }
            _nativeImportFixups.Add(new NativeImportFixup(_code.ImportCall(), import.DllName, import.ExportName, function));
        }
        else
        {
            _calls.Add((_code.RelativeCall(), function));
        }
    }

    private void NormalizeNativeNarrowReturn(PrimitiveType type)
    {
        if (type == PrimitiveType.Bool)
        {
            _code.Bytes(0x0F, 0xB6, 0xC0);
        }
        else if (type == PrimitiveType.Char16)
        {
            _code.Bytes(0x0F, 0xB7, 0xC0);
        }
    }

    private void EmitExceptionFilter(bool storeReason)
    {
        // A leaf filter rejects everything except the private noncontinuable, versioned
        // four-reason record. The entry filter additionally records the reason in its frame.
        _code.Bytes(0x48, 0x8B, 0x01); // mov rax,[rcx]: ExceptionRecord
        _code.Bytes(0x81, 0x38);
        _code.Int32(unchecked((int)0xE0540092));
        var reject = new List<int> { _code.JumpIf(0x85) };
        _code.Bytes(0xF6, 0x40, 0x04, 0x01);
        reject.Add(_code.JumpIf(0x84));
        _code.Bytes(0x83, 0x78, 0x18, 0x03);
        reject.Add(_code.JumpIf(0x85));
        _code.Bytes(0x49, 0xB8);
        _code.Int64(0x54455252554D0001);
        _code.Byte(0x4C);
        _code.Byte(0x39);
        _code.Byte(0x40);
        _code.Byte(0x20);
        reject.Add(_code.JumpIf(0x85));
        _code.Bytes(0x48, 0x83, 0x78, 0x28, 0x01);
        reject.Add(_code.JumpIf(0x85));
        _code.Bytes(0x4C, 0x8B, 0x40, 0x30); // reason
        _code.Bytes(0x49, 0x83, 0xF8, 0x01);
        reject.Add(_code.JumpIf(0x82));
        _code.Bytes(0x49, 0x83, 0xF8, 0x04);
        reject.Add(_code.JumpIf(0x87));
        if (storeReason)
        {
            _code.Bytes(0x44, 0x89, 0x42, 0x20); // store reason in entry frame, outside home space
        }
        _code.Bytes(0xB8, 0x01, 0x00, 0x00, 0x00, 0xC3);
        foreach (var offset in reject)
        {
            _code.PatchRelative(offset, _code.Position);
        }
        _code.Bytes(0x31, 0xC0, 0xC3);
    }

    private void EmitIndexedBody(TrRmScalarGraph graph)
    {
        var positions = new int[graph.Blocks.Length + 1];
        var fixups = new List<(int Offset, int Target)>();

        void EmitEdge(TrRmScalarEdge edge)
        {
            var parameters = graph.Blocks[edge.Target - 1].Parameters;
            // Backedges can exchange parameter values; no destination may overwrite a later source.
            for (var i = 0; i < parameters.Length; i++)
            {
                var type = graph.Types[edge.Arguments[i] - 1];
                var source = Slot(edge.Arguments[i] - 1);
                var staged = _edgeCopySlots[_currentFunction][i];
                if (IsIndirect(type))
                {
                    _code.Address(source);
                    CopyToFrame(staged, Size(type));
                }
                else
                {
                    LoadValue(source, type);
                    StoreValue(staged, type);
                }
            }
            for (var i = 0; i < parameters.Length; i++)
            {
                var type = graph.Types[parameters[i] - 1];
                var destination = Slot(parameters[i] - 1);
                var staged = _edgeCopySlots[_currentFunction][i];
                if (IsIndirect(type))
                {
                    _code.Address(staged);
                    CopyToFrame(destination, Size(type));
                }
                else
                {
                    LoadValue(staged, type);
                    StoreValue(destination, type);
                }
            }
            fixups.Add((_code.Jump(), edge.Target));
        }

        for (var id = 1; id <= graph.Blocks.Length; id++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            positions[id] = _code.Position;
            var block = graph.Blocks[id - 1];
            foreach (var operation in block.Operations)
            {
                Statement(operation);
            }
            if (block.FailureReason != 0)
            {
                // TRRM reason IDs differ from the native fail-fast status order.
                var target = block.FailureReason switch
                {
                    1 => 2,
                    2 => 0,
                    3 => 1,
                    4 => 0,
                    _ => throw new InvalidOperationException("Indexed failure reason was not verified."),
                };
                _runtimeCalls.Add((_code.RelativeCall(), target));
                _code.Byte(0xCC);
            }
            else if (block.SwitchValue != 0)
            {
                var selectorType = graph.Types[block.SwitchValue - 1];
                LoadValue(Slot(block.SwitchValue - 1), selectorType);
                if (IsWide(selectorType))
                {
                    _code.Bytes(0x49, 0x89, 0xC0);
                }
                else
                {
                    _code.Bytes(0x41, 0x89, 0xC0);
                }
                foreach (var switchCase in block.SwitchCases)
                {
                    if (IsWide(selectorType))
                    {
                        _code.Bytes(0x48, 0xB8);
                        _code.Int64(unchecked((long)switchCase.Bits));
                        _code.Bytes(0x49, 0x39, 0xC0);
                    }
                    else
                    {
                        _code.Byte(0xB8);
                        _code.Int32(unchecked((int)switchCase.Bits));
                        _code.Bytes(0x41, 0x39, 0xC0);
                    }
                    var nextCase = _code.JumpIf(0x85);
                    EmitEdge(switchCase.Edge);
                    _code.PatchRelative(nextCase, _code.Position);
                }
                EmitEdge(block.SwitchDefault
                    ?? throw new InvalidOperationException("Indexed switch lacks a default edge."));
            }
            else if (block.Condition != 0)
            {
                _code.Load(Slot(block.Condition - 1));
                _code.Bytes(0x85, 0xC0);
                var falseJump = _code.JumpIf(0x84);
                EmitEdge(block.True ?? throw new InvalidOperationException("Indexed branch lacks true edge."));
                _code.PatchRelative(falseJump, _code.Position);
                EmitEdge(block.False ?? throw new InvalidOperationException("Indexed branch lacks false edge."));
            }
            else if (block.True is not null)
            {
                EmitEdge(block.True);
            }
            else
            {
                Statement(new BoundReturn(block.ReturnValue == 0 ? null
                    : new BoundRead(block.ReturnValue - 1, graph.Types[block.ReturnValue - 1], default),
                    default));
            }
            if (_code.Position > MaximumText)
            {
                throw new NotSupportedException("Native text exceeds the 8 MiB image ceiling.");
            }
        }
        foreach (var (offset, target) in fixups)
        {
            _code.PatchRelative(offset, positions[target]);
        }
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "The verifier excludes other structured statement variants.")]
    private void Statement(BoundStatement statement)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (_code.Position > MaximumText)
        {
            throw new NotSupportedException("Native text exceeds the 8 MiB image ceiling.");
        }
        switch (statement)
        {
            case BoundDropSpan span:
                SetInitializationFlag(span.Variable, initialized: false);
                break;
            case BoundRequire requirement:
                Expression(requirement.Condition, 0);
                _code.Bytes(0x85, 0xC0);
                RuntimeBranch(0x84, 2);
                break;
            case BoundBlock block:
                var rootCleanup = -1;
                if (_checkedExceptions && !_emittingFunclet && !block.RetiredManagedLocals.IsEmpty)
                {
                    rootCleanup = _funclets.Count;
                    _funclets.Add((new BoundBlock([], block.Span) {
                        RetiredManagedLocals = block.RetiredManagedLocals,
                    }, _currentFunction, 0, 0));
                    _cleanups.Add(rootCleanup);
                }
                if (!block.RetiredManagedLocals.IsEmpty)
                {
                    _normalExits.Add(new NormalExit(rootCleanup, block.RetiredManagedLocals));
                }
                var rootScopeBegin = _code.Position;
                foreach (var child in block.Statements)
                {
                    Statement(child);
                }
                if (rootCleanup >= 0)
                {
                    _code.Byte(0x90);
                    _cleanups.RemoveAt(_cleanups.Count - 1);
                    _scopes[_currentFunction].Add(new NativeExceptionScope(rootScopeBegin, _code.Position,
                        -rootCleanup - 1, 0));
                    CallCleanup(rootCleanup);
                }
                else
                {
                    foreach (var retired in block.RetiredManagedLocals)
                    {
                        SetManagedRoot(OwnerPlace(retired), active: false);
                    }
                }
                if (!block.RetiredManagedLocals.IsEmpty)
                {
                    _normalExits.RemoveAt(_normalExits.Count - 1);
                }
                break;
            case TrRmScalarDivRem division:
                Expression(division.Dividend, 0);
                if (IsWide(division.Type))
                {
                    _code.Bytes(0x49, 0x89, 0xC0); // Preserve the dividend in volatile r8.
                }
                else
                {
                    _code.Bytes(0x41, 0x89, 0xC0);
                }
                Expression(division.Divisor, 1);
                if (IsWide(division.Type))
                {
                    _code.Bytes(0x48, 0x89, 0xC1, 0x4C, 0x89, 0xC0);
                }
                else
                {
                    _code.Bytes(0x89, 0xC1, 0x44, 0x89, 0xC0);
                }
                CheckedDivRem(division.Type);
                StoreValue(Slot(division.QuotientVariable), division.Type);
                SetInitializationFlag(division.QuotientVariable, initialized: true);
                if (IsWide(division.Type))
                {
                    _code.Bytes(0x48, 0x89, 0xD0);
                }
                else
                {
                    _code.Bytes(0x89, 0xD0);
                }
                StoreValue(Slot(division.RemainderVariable), division.Type);
                SetInitializationFlag(division.RemainderVariable, initialized: true);
                break;
            case BoundLocal local:
                if (local.Initializer is BoundNativeFixedArrayInitialization fixedArray)
                {
                    var stride = fixedArray.Layout.ElementStride;
                    if (fixedArray.Elements.IsEmpty)
                    {
                        _code.Bytes(0x31, 0xC0);
                        for (var offset = 0; offset < fixedArray.Layout.ByteExtent;)
                        {
                            var remaining = fixedArray.Layout.ByteExtent - offset;
                            var width = remaining >= sizeof(ulong) ? sizeof(ulong)
                                : remaining >= sizeof(uint) ? sizeof(uint)
                                : remaining >= sizeof(ushort) ? sizeof(ushort) : sizeof(byte);
                            _code.StoreField(Slot(local.Variable) + offset, width);
                            offset += width;
                        }
                        break;
                    }
                    for (var index = 0; index < fixedArray.Elements.Length; index++)
                    {
                        var element = fixedArray.Elements[index];
                        var fixedArrayDestination = Slot(local.Variable) + (index * stride);
                        Expression(element, 0);
                        if (IsIndirect(element.Type))
                        {
                            CopyToFrame(fixedArrayDestination, stride);
                        }
                        else if (element.Type is PrimitiveType.Bool or PrimitiveType.Char16)
                        {
                            _code.StoreField(fixedArrayDestination, Size(element.Type));
                        }
                        else
                        {
                            StoreValue(fixedArrayDestination, element.Type);
                        }
                    }
                }
                else
                {
                    Expression(local.Initializer, 0);
                    StoreValue(Slot(local.Variable), local.Initializer.Type);
                }
                SetInitializationFlag(local.Variable, initialized: true);
                if (ManagedClassTypes.IsManaged(local.Initializer.Type))
                {
                    SetManagedRoot(Slot(local.Variable), active: true);
                }
                if (IsUnique(local.Initializer.Type))
                {
                    _code.Set(_liveSlots[_currentFunction][local.Variable]);
                }
                break;
            case BoundViewScope view:
                Expression(view.Start, 0);
                _code.Store(Spill(0));
                Expression(view.RangeEnd ?? view.Length, 1);
                _code.Store(Spill(1));
                Expression(view.Owner, 2);
                _code.Bytes(0x48, 0x8B, 0x10); // rdx = heap header through the reserved owner place
                _code.Load(Spill(0));
                _code.Bytes(0x85, 0xC0);
                RuntimeBranch(0x88, 2);
                _code.Bytes(0x3B, 0x02); // start <= initialized count
                RuntimeBranch(0x87, 2);
                if (view.RangeEnd is null)
                {
                    _code.Bytes(0x89, 0xC1); // rcx = checked start
                    _code.Load(Spill(1));
                    _code.Bytes(0x85, 0xC0);
                    RuntimeBranch(0x88, 2);
                    _code.Bytes(0x44, 0x8B, 0x02, 0x41, 0x29, 0xC8); // r8d = count - start
                    _code.Bytes(0x44, 0x39, 0xC0); // length <= remaining initialized elements
                    RuntimeBranch(0x87, 2);
                }
                else
                {
                    if (view.IsStartEndRelative)
                    {
                        _code.Bytes(0x8B, 0x0A, 0x29, 0xC1, 0x89, 0xC8); // start = count - checked offset
                        _code.Store(Spill(0));
                    }
                    _code.Load(Spill(1));
                    _code.Bytes(0x85, 0xC0);
                    RuntimeBranch(0x88, 2);
                    _code.Bytes(0x3B, 0x02); // end <= initialized count
                    RuntimeBranch(0x87, 2);
                    if (view.IsRangeEndRelative)
                    {
                        _code.Bytes(0x8B, 0x0A, 0x29, 0xC1, 0x89, 0xC8); // end = count - checked offset
                        _code.Store(Spill(1));
                    }
                    _code.Load(Spill(0));
                    _code.Bytes(0x89, 0xC1); // rcx = start
                    _code.Load(Spill(1));
                    _code.Bytes(0x39, 0xC1); // start <= end
                    RuntimeBranch(0x8F, 2);
                    _code.Bytes(0x29, 0xC8); // length = end - start after both are checked
                    _code.Store(Spill(1));
                }
                _code.Bytes(0x48, 0x8D, 0x44, 0x8A, 0x08); // pointer to first element of the slice
                _code.Store64(Slot(view.Variable));
                _code.Load(Spill(1));
                _code.Store(Slot(view.Variable) + 8);
                Statement(view.Body);
                break;
            case BoundViewSet write:
                Expression(write.Index, 0);
                _code.Store(Spill(0));
                Expression(write.Value, 1);
                _code.Store(Spill(1));
                _code.Load(Spill(0));
                _code.Bytes(0x89, 0xC1);
                _code.Load(Slot(write.Variable) + 8);
                _code.Bytes(0x39, 0xC1);
                RuntimeBranch(0x83, 2);
                _code.Load64(Slot(write.Variable), destinationRegister: true);
                _code.Load(Spill(1));
                _code.Bytes(0x89, 0x04, 0x8A);
                break;
            case BoundOwnerDeclaration or BoundVacantLocal:
                break;
            case BoundLifetimeEnd end:
                var endedType = _program.Functions[_currentFunction].Variables[end.Variable].Type;
                if (IsUnique(endedType))
                {
                    DropOwner(end.Variable);
                }
                else if (ManagedClassTypes.IsManaged(endedType))
                {
                    SetManagedRoot(Slot(end.Variable), active: false);
                }
                SetInitializationFlag(end.Variable, initialized: false);
                break;
            case BoundAssignment assignment:
                Expression(assignment.Value, 0);
                if (ManagedClassTypes.IsManaged(assignment.Value.Type))
                {
                    var managedDestination = OwnerPlace(assignment.Variable);
                    StoreValue(managedDestination, assignment.Value.Type);
                    SetManagedRoot(managedDestination, active: true);
                    break;
                }
                if (IsUnique(assignment.Value.Type))
                {
                    StoreValue(_replacementSlots[_currentFunction], assignment.Value.Type);
                    DropOwner(assignment.Variable);
                    LoadValue(_replacementSlots[_currentFunction], assignment.Value.Type);
                    StoreValue(OwnerPlace(assignment.Variable), assignment.Value.Type);
                    _code.Set(_liveSlots[_currentFunction][assignment.Variable]);
                    break;
                }
                if (_program.Functions[_currentFunction].Variables[assignment.Variable].IsOut)
                {
                    if (IsIndirect(assignment.Value.Type))
                    {
                        CopyToFrame(OutSlot(assignment.Variable), Size(assignment.Value.Type));
                    }
                    else
                    {
                        _code.Store64(OutSlot(assignment.Variable));
                    }
                }
                else
                {
                    StoreValue(Slot(assignment.Variable), assignment.Value.Type);
                }
                SetInitializationFlag(assignment.Variable, initialized: true);
                break;
            case BoundOptionalAssignment optionalAssignment:
                if (optionalAssignment.Place is BoundRead optionalLocal)
                {
                    var optionalDestination = OwnerPlace(optionalLocal.Variable);
                    _code.Load64(optionalDestination);
                    _code.Bytes(0x48, 0x85, 0xC0);
                    var localPresent = _code.JumpIf(0x85);
                    Expression(optionalAssignment.Value, 0);
                    StoreValue(optionalDestination, optionalLocal.Type);
                    SetManagedRoot(optionalDestination, active: true);
                    _code.PatchRelative(localPresent, _code.Position);
                    break;
                }
                var optionalField = (BoundFieldRead)optionalAssignment.Place;
                Expression(optionalField.Receiver, 0);
                _code.Store64(Spill(0));
                SetManagedRoot(Spill(0), active: true);
                var fieldDeclaration = _program.Classes[ManagedClassTypes.ClassIndex(optionalField.Receiver.Type)]
                    .Fields[optionalField.Field];
                _code.LoadPointerField(32 + fieldDeclaration.Offset, 8);
                _code.Bytes(0x48, 0x85, 0xC0);
                var fieldPresent = _code.JumpIf(0x85);
                var fallbackBegin = _code.Position;
                var fallbackFunclet = -1;
                if (_checkedExceptions && !_emittingFunclet)
                {
                    fallbackFunclet = _funclets.Count;
                    _funclets.Add((new BoundBlock([], optionalAssignment.Span), _currentFunction, 0, 0));
                    _managedStagedRoots.Add(fallbackFunclet, [Spill(0)]);
                }
                Expression(optionalAssignment.Value, 1);
                _code.Bytes(0x48, 0x89, 0xC1);
                _code.Load64(Spill(0));
                _code.PointerOffset(32 + fieldDeclaration.Offset);
                _code.Bytes(0x48, 0x89, 0xC2, 0x48, 0x89, 0xC8);
                _code.Bytes(0x48, 0x89, 0x02);
                if (fallbackFunclet >= 0)
                {
                    _code.Byte(0x90);
                    _scopes[_currentFunction].Add(new NativeExceptionScope(fallbackBegin, _code.Position,
                        -fallbackFunclet - 1, 0));
                }
                _code.PatchRelative(fieldPresent, _code.Position);
                SetManagedRoot(Spill(0), active: false);
                break;
            case BoundDrop drop:
                if (drop.IsOutput)
                {
                    _code.Bytes(0x48, 0x85, 0xC9); // the unwind funclet receives an exception record
                    var unwind = _code.JumpIf(0x85);
                    if (_program.Functions[_currentFunction].ReturnType == PrimitiveType.Bool)
                    {
                        _code.Load(PendingReturnSlot());
                        _code.Bytes(0x85, 0xC0);
                        var transferred = _code.JumpIf(0x85);
                        _code.PatchRelative(unwind, _code.Position);
                        DropOwner(drop.Variable);
                        _code.PatchRelative(transferred, _code.Position);
                    }
                    else
                    {
                        var transferred = _code.JumpIf(0x84);
                        _code.PatchRelative(unwind, _code.Position);
                        DropOwner(drop.Variable);
                        _code.PatchRelative(transferred, _code.Position);
                    }
                }
                else
                {
                    DropOwner(drop.Variable);
                }
                break;
            case BoundDropConstructedFields partial:
                _code.Bytes(0x48, 0x85, 0xC9); // only an exceptional unwind owns unfinished fields
                var completed = _code.JumpIf(0x84);
                var fields = _program.Structures[(int)_program.Functions[_currentFunction].ReturnType - 100].Fields;
                for (var index = partial.AcquisitionOrder.Length - 1; index >= 0; index--)
                {
                    var fieldIndex = partial.AcquisitionOrder[index];
                    var live = _constructedFieldSlots[_currentFunction][fieldIndex];
                    _code.Load(live);
                    _code.Bytes(0x85, 0xC0);
                    var vacant = _code.JumpIf(0x84);
                    _code.Clear(live);
                    EmitDropValue(Slot(0) + fields[fieldIndex].Offset, fields[fieldIndex].Type);
                    _code.PatchRelative(vacant, _code.Position);
                }
                _code.PatchRelative(completed, _code.Position);
                break;
            case BoundFieldAssignment fieldAssignment:
                var variable = _program.Functions[_currentFunction].Variables[fieldAssignment.Variable];
                var fieldType = variable.Type;
                var classField = ManagedClassTypes.IsClass(fieldType);
                var field = classField ? _program.Classes[ManagedClassTypes.ClassIndex(fieldType)]
                    .Fields[fieldAssignment.Field] : _program.Structures[(int)fieldType - 100].Fields[fieldAssignment.Field];
                var destination = (variable.IsOut ? OutSlot(fieldAssignment.Variable) : Slot(fieldAssignment.Variable))
                    + field.Offset;
                if (classField)
                {
                    _code.Load64(OwnerPlace(fieldAssignment.Variable));
                    var inlineOffset = 0;
                    foreach (var nestedField in fieldAssignment.NestedFields)
                    {
                        var containingType = field.Type;
                        if (ManagedClassTypes.IsClass(containingType))
                        {
                            _code.PointerOffset(32 + inlineOffset + field.Offset);
                            _code.Bytes(0x48, 0x8B, 0x00);
                            field = _program.Classes[ManagedClassTypes.ClassIndex(containingType)]
                                .Fields[nestedField];
                            inlineOffset = 0;
                        }
                        else
                        {
                            inlineOffset += field.Offset;
                            field = _program.Structures[(int)containingType - 100].Fields[nestedField];
                        }
                    }
                    _code.Store64(Spill(0));
                    SetManagedRoot(Spill(0), active: true);
                    var stagedBegin = _code.Position;
                    var stagedFunclet = -1;
                    if (_checkedExceptions && !_emittingFunclet)
                    {
                        stagedFunclet = _funclets.Count;
                        _funclets.Add((new BoundBlock([], fieldAssignment.Span), _currentFunction, 0, 0));
                        _managedStagedRoots.Add(stagedFunclet, [Spill(0)]);
                    }
                    Expression(fieldAssignment.Value, 1);
                    if (IsIndirect(field.Type))
                    {
                        _code.Bytes(0x48, 0x89, 0xC1); // preserve the source pointer without overwriting its spill
                    }
                    else
                    {
                        _code.Store64(Spill(1));
                    }
                    _code.Load64(Spill(0));
                    _code.PointerOffset(32 + inlineOffset + field.Offset);
                    _code.Bytes(0x48, 0x89, 0xC2);
                    if (IsIndirect(field.Type))
                    {
                        _code.Bytes(0x48, 0x89, 0xC8);
                        _code.CopyMemory(field.Size);
                    }
                    else
                    {
                        _code.Load64(Spill(1));
                        switch (field.Size)
                        {
                            case 1:
                                _code.Bytes(0x88, 0x02);
                                break;
                            case 2:
                                _code.Bytes(0x66, 0x89, 0x02);
                                break;
                            case 4:
                                _code.Bytes(0x89, 0x02);
                                break;
                            default:
                                _code.Bytes(0x48, 0x89, 0x02);
                                break;
                        }
                    }
                    SetManagedRoot(Spill(0), active: false);
                    if (stagedFunclet >= 0)
                    {
                        _code.Byte(0x90);
                        _scopes[_currentFunction].Add(new NativeExceptionScope(stagedBegin, _code.Position,
                            -stagedFunclet - 1, 0));
                    }
                }
                else
                {
                    foreach (var nestedField in fieldAssignment.NestedFields)
                    {
                        field = _program.Structures[(int)field.Type - 100].Fields[nestedField];
                        destination += field.Offset;
                    }
                    Expression(fieldAssignment.Value, 0);
                    if (fieldAssignment.Variable == 0 && IsBorrowedReceiver())
                    {
                        StoreValue(Spill(0), field.Type);
                        _code.Load64(Slot(0));
                        _code.PointerOffset(destination - Slot(0));
                        _code.Bytes(0x48, 0x89, 0xC2);
                        LoadValue(Spill(0), field.Type);
                        if (IsIndirect(field.Type))
                        {
                            _code.CopyMemory(field.Size);
                        }
                        else
                        {
                            switch (field.Size)
                            {
                                case 1:
                                    _code.Bytes(0x88, 0x02);
                                    break;
                                case 2:
                                    _code.Bytes(0x66, 0x89, 0x02);
                                    break;
                                case 4:
                                    _code.Bytes(0x89, 0x02);
                                    break;
                                default:
                                    _code.Bytes(0x48, 0x89, 0x02);
                                    break;
                            }
                        }
                    }
                    else if (IsIndirect(field.Type))
                    {
                        CopyToFrame(destination, field.Size);
                    }
                    else
                    {
                        _code.StoreField(destination, field.Size);
                    }
                }
                if (fieldAssignment.Variable == 0 && fieldAssignment.NestedFields.IsEmpty
                    && IsUnique(field.Type)
                    && _constructedFieldSlots[_currentFunction].Length > fieldAssignment.Field
                    && _constructedFieldSlots[_currentFunction][fieldAssignment.Field] >= 0)
                {
                    _code.Set(_constructedFieldSlots[_currentFunction][fieldAssignment.Field]);
                }
                break;
            case BoundBufferAssignment bufferWrite:
                EmitBufferIndex(bufferWrite.Buffer, bufferWrite.Index, bufferWrite.IsEndRelativeOffset, 0);
                _code.Store(Spill(0));
                Expression(bufferWrite.Value, 1);
                _code.Bytes(0x89, 0xC1); // mov ecx,eax
                _code.Load(Spill(0));
                _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _bufferOffsets[bufferWrite.Buffer]));
                _code.Bytes(0x89, 0x0C, 0x82); // mov [rdx+rax*4],ecx
                break;
            case BoundExpressionStatement expression:
                Expression(expression.Expression, 0);
                break;
            case BoundReturn result:
                if (result.Value is not null)
                {
                    Expression(result.Value, 0);
                    if (_cleanups.Count != 0 || result.Value.Type == PrimitiveType.Float128)
                    {
                        StoreValue(PendingReturnSlot(), result.Value.Type);
                        if (ManagedClassTypes.IsManaged(result.Value.Type))
                        {
                            SetManagedRoot(PendingReturnSlot(), active: true);
                        }
                    }
                }
                for (var index = _cleanups.Count - 1; index >= 0; index--)
                {
                    CallCleanup(_cleanups[index]);
                }
                if (result.Value is not null)
                {
                    if (_cleanups.Count != 0)
                    {
                        LoadValue(PendingReturnSlot(), result.Value.Type);
                    }
                    if (IsIndirectResult(result.Value.Type))
                    {
                        _code.Load64(_returnSlot, destinationRegister: true);
                        _code.CopyMemory(Size(result.Value.Type));
                        _code.Load64(_returnSlot);
                    }
                }
                var hasOutParameter = false;
                var currentFunction = _program.Functions[_currentFunction];
                var parameterOffset = currentFunction.IsConstructor ? 1 : 0;
                for (var i = parameterOffset;
                    i < currentFunction.ParameterCount + parameterOffset; i++)
                {
                    hasOutParameter |= currentFunction.Variables[i].IsOut;
                }
                if (currentFunction.ReturnType == PrimitiveType.Bool || hasOutParameter)
                {
                    var returnType = currentFunction.ReturnType;
                    if (returnType != PrimitiveType.Bool && returnType != PrimitiveType.Void
                        && !IsIndirectResult(returnType))
                    {
                        StoreValue(PendingReturnSlot(), returnType);
                    }
                    CommitOutParameters();
                    if (returnType is not (PrimitiveType.Bool or PrimitiveType.Void))
                    {
                        if (IsIndirectResult(returnType))
                        {
                            _code.Load64(_returnSlot);
                        }
                        else
                        {
                            LoadValue(PendingReturnSlot(), returnType);
                        }
                    }
                }
                if (_checkedExceptions)
                {
                    _code.Byte(0x90); // call-site continuation must not start at an unwind epilog
                }
                UnregisterManagedRoots(_currentFunction);
                if (IsFloatingCallCarrier(currentFunction.ReturnType))
                {
                    _code.MoveFloatingReturnFromAccumulator(currentFunction.ReturnType == PrimitiveType.Float64);
                }
                else if (currentFunction.ReturnType == PrimitiveType.Float128)
                {
                    _code.Address(PendingReturnSlot());
                    _code.LoadFloating128FromPointer();
                }
                Epilog();
                break;
            case BoundTryFinally cleanup:
                var funclet = _funclets.Count;
                _funclets.Add((cleanup.Finally, _currentFunction, 0, 0));
                _cleanups.Add(funclet);
                _normalExits.Add(new NormalExit(funclet, []));
                var protectedBegin = _code.Position;
                Statement(cleanup.Body);
                if (_checkedExceptions && !_emittingFunclet)
                {
                    _code.Byte(0x90);
                }
                var protectedEnd = _code.Position;
                _cleanups.RemoveAt(_cleanups.Count - 1);
                _normalExits.RemoveAt(_normalExits.Count - 1);
                if (_checkedExceptions && !_emittingFunclet && protectedBegin != protectedEnd)
                {
                    _scopes[_currentFunction].Add(new NativeExceptionScope(protectedBegin, protectedEnd, -funclet - 1, 0));
                }
                CallCleanup(funclet);
                break;
            case BoundUnsafe unsafeBlock:
                Statement(unsafeBlock.Body);
                break;
            case BoundTryCatch recovery:
                var cleanupIndex = -1;
                if (recovery.Finally is not null)
                {
                    cleanupIndex = _funclets.Count;
                    _funclets.Add((recovery.Finally, _currentFunction, 0, 0));
                    _cleanups.Add(cleanupIndex);
                    _normalExits.Add(new NormalExit(cleanupIndex, []));
                }
                var bodyBegin = _code.Position;
                Statement(recovery.Body);
                _code.Byte(0x90);
                var bodyEnd = _code.Position;
                var skipCatch = _code.Jump();
                var landing = _code.Position;
                Statement(recovery.Catch);
                if (cleanupIndex >= 0)
                {
                    _code.Byte(0x90);
                }
                var catchEnd = _code.Position;
                _code.PatchRelative(skipCatch, catchEnd);
                if (bodyBegin != bodyEnd)
                {
                    _scopes[_currentFunction].Add(new NativeExceptionScope(bodyBegin, bodyEnd, 0, landing));
                }
                if (cleanupIndex >= 0)
                {
                    _cleanups.RemoveAt(_cleanups.Count - 1);
                    _normalExits.RemoveAt(_normalExits.Count - 1);
                    _scopes[_currentFunction].Add(new NativeExceptionScope(bodyBegin, catchEnd, -cleanupIndex - 1, 0));
                    CallCleanup(cleanupIndex);
                }
                break;
            case BoundIf conditional:
                Expression(conditional.Condition, 0);
                _code.Bytes(0x85, 0xC0);
                var falseBranch = _code.JumpIf(0x84);
                Statement(conditional.Then);
                if (conditional.Else is not null)
                {
                    var end = _code.Jump();
                    _code.PatchRelative(falseBranch, _code.Position);
                    Statement(conditional.Else);
                    _code.PatchRelative(end, _code.Position);
                }

                else
                {
                    _code.PatchRelative(falseBranch, _code.Position);
                }
                break;
            case BoundWhile loop:
                var start = _code.Position;
                var exit = -1;
                if (!loop.IsPostTested)
                {
                    Expression(loop.Condition, 0);
                    _code.Bytes(0x85, 0xC0);
                    exit = _code.JumpIf(0x84);
                }
                var loopTarget = new LoopTarget(_normalExits.Count);
                _loopTargets.Add(loopTarget);
                Statement(loop.Body);
                var iteratorStart = _code.Position;
                if (loop.Iterator is not null)
                {
                    Statement(loop.Iterator);
                }
                var conditionStart = _code.Position;
                if (loop.IsPostTested)
                {
                    Expression(loop.Condition, 0);
                    _code.Bytes(0x85, 0xC0);
                    exit = _code.JumpIf(0x84);
                }
                foreach (var jump in loopTarget.Continues)
                {
                    _code.PatchRelative(jump, loop.IsPostTested ? conditionStart
                        : loop.Iterator is null ? start : iteratorStart);
                }
                _loopTargets.RemoveAt(_loopTargets.Count - 1);
                _code.PatchRelative(_code.Jump(), start);
                _code.PatchRelative(exit, _code.Position);
                foreach (var jump in loopTarget.Breaks)
                {
                    _code.PatchRelative(jump, _code.Position);
                }
                break;
            case BoundBreak or BoundContinue:
                if (_loopTargets.Count == 0)
                {
                    throw new ArgumentException("Loop transfer has no verified target.", nameof(statement));
                }
                var targetDepth = statement is BoundBreak exitLoop ? exitLoop.TargetDepth
                    : ((BoundContinue)statement).TargetDepth;
                if (targetDepth <= 0 || targetDepth > _loopTargets.Count)
                {
                    throw new ArgumentException("Loop transfer has no verified target.", nameof(statement));
                }
                var target = _loopTargets[^targetDepth];
                for (var index = _normalExits.Count - 1; index >= target.ExitDepth; index--)
                {
                    var normalExit = _normalExits[index];
                    if (normalExit.Cleanup >= 0)
                    {
                        CallCleanup(normalExit.Cleanup);
                    }
                    else
                    {
                        foreach (var retired in normalExit.RetiredRoots)
                        {
                            SetManagedRoot(OwnerPlace(retired), active: false);
                        }
                    }
                }
                var transfer = _code.Jump();
                (statement is BoundBreak ? target.Breaks : target.Continues).Add(transfer);
                break;
            default:
                throw new ArgumentException("Unknown native statement kind.", nameof(statement));
        }
    }

    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "The verified scalar layouts and eight-byte fallback cover all output slots.")]
    private void CommitOutParameters()
    {
        var function = _program.Functions[_currentFunction];
        var variables = function.Variables;
        var hasOut = false;
        foreach (var variable in variables)
        {
            hasOut |= variable.IsOut;
        }

        if (!hasOut)
        {
            return;
        }
        var conditional = _program.Functions[_currentFunction].ReturnType == PrimitiveType.Bool;
        var skip = -1;
        if (conditional)
        {
            _code.Bytes(0x85, 0xC0);
            skip = _code.JumpIf(0x84);
        }
        for (var i = 0; i < variables.Length; i++)
        {
            if (!variables[i].IsOut
                || NativeVTableCallContract.OutputForParameter(function, i) is not null)
            {
                continue;
            }
            var size = Size(variables[i].Type);
            if (IsIndirect(variables[i].Type))
            {
                _code.Address(OutSlot(i));
                _code.Load64(Slot(i), destinationRegister: true);
                _code.CopyMemory(size);
                continue;
            }
            _code.Load64(Slot(i));
            _code.Bytes(0x48, 0x89, 0xC2); // mov rdx,rax
            _code.Load64(OutSlot(i));
            if (size == 1)
            {
                _code.Bytes(0x88, 0x02);
            }
            else if (size == 2)
            {
                _code.Bytes(0x66, 0x89, 0x02);
            }
            else if (size == 4)
            {
                _code.Bytes(0x89, 0x02);
            }
            else
            {
                _code.Bytes(0x48, 0x89, 0x02);
            }
        }
        if (conditional)
        {
            _code.Bytes(0xB8, 0x01, 0x00, 0x00, 0x00);
            _code.PatchRelative(skip, _code.Position);
        }
    }

    private void CheckBufferIndex(int buffer)
    {
        if (_sourceDependent is not null)
        {
            _code.Bytes(0x3B, 0x85); // cmp eax,[rbp+sourceCount]
            _code.Int32(_sourceSlots[_currentFunction] + 8);
            RuntimeBranch(0x83, 3);
            return;
        }
        _code.Byte(0x3D);
        _code.Int32(_program.Buffers[buffer].Values.Length);
        RuntimeBranch(0x83, 3); // unsigned >= length rejects negative indexes too
    }

    private void EmitBufferIndex(int buffer, BoundExpression index, bool isEndRelative, int spill)
    {
        Expression(index, spill);
        if (isEndRelative)
        {
            var invocationScoped = _sourceDependent is not null && buffer == 0;
            if (invocationScoped)
            {
                _code.Bytes(0x8B, 0x8D); // mov ecx,[rbp+sourceCount]
                _code.Int32(_sourceSlots[_currentFunction] + 8);
            }
            _code.Bytes(0x85, 0xC0); // test eax,eax
            var negativeOffset = _code.JumpIf(0x88);
            if (invocationScoped)
            {
                _code.Bytes(0x3B, 0xC1); // cmp eax,ecx
            }
            else
            {
                _code.Byte(0x3D); // cmp eax,length
                _code.Int32(_program.Buffers[buffer].Values.Length);
            }
            var outOfRange = _code.JumpIf(0x87);
            if (!invocationScoped)
            {
                _code.Byte(0xB9); // mov ecx,length
                _code.Int32(_program.Buffers[buffer].Values.Length);
            }
            _code.Bytes(0x29, 0xC1, 0x89, 0xC8); // ecx = length - eax; eax = ecx
            var converted = _code.Jump();
            _code.PatchRelative(negativeOffset, _code.Position);
            _code.PatchRelative(outOfRange, _code.Position);
            _code.Bytes(0xB8); // mov eax,-1
            _code.Int32(-1);
            _code.PatchRelative(converted, _code.Position);
        }
        CheckBufferIndex(buffer);
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "The verifier excludes unsupported expression variants and unary operators.")]
    private void Expression(BoundExpression expression, int spill)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        switch (expression)
        {
            case BoundNativeFixedArrayElement fixedArrayElement:
                EmitNativeFixedArrayElement(fixedArrayElement, spill);
                break;
            case BoundNativeFixedArrayPointerAddress pointerAddress:
                EmitNativeFixedArrayPointerAddress(pointerAddress, spill);
                break;
            case BoundNativeFixedArrayWrite fixedArrayWrite:
                EmitNativeFixedArrayWrite(fixedArrayWrite, spill);
                break;
            case BoundNativeStorageAddress address:
                EmitNativeStorageAddress(address, spill);
                break;
            case BoundNativeOutputReservation reservation:
                EmitNativeOutputReservation(reservation);
                break;
            case BoundNativePointerAddress address:
                EmitNativePointerAddress(address);
                break;
            case BoundNativePointerRead pointerRead:
                EmitNativePointerRead(pointerRead, spill);
                break;
            case BoundNativePointerWrite pointerWrite:
                EmitNativePointerWrite(pointerWrite, spill);
                break;
            case BoundReferenceFormation formation:
                EmitCheckedReferenceFormation(formation, spill);
                break;
            case BoundReferenceReborrow reborrow:
                EmitCheckedReferenceReborrow(reborrow, spill);
                break;
            case BoundReferenceRead referenceRead:
                EmitCheckedReferenceRead(referenceRead, spill);
                break;
            case BoundReferenceWrite referenceWrite:
                EmitCheckedReferenceWrite(referenceWrite, spill);
                break;
            case BoundNativeFunctionAddress address:
                NativeEmitter.EnsureNativeFunctionPointerConventionSupported(address.Type,
                    _program.NativeFunctionPointerSignatures);
                if (_sourceDependent?[address.Function] == true)
                {
                    throw new NotSupportedException(
                        "A function pointer cannot target an entry with hidden invocation-scoped inputs.");
                }
                _code.Bytes(0x48, 0x8D, 0x05); // lea rax,[rip+function]
                var functionAddress = _code.Position;
                _code.Int32(0);
                _functionAddressFixups.Add((functionAddress, address.Function));
                if (_program.Functions[address.Function].IsExternal)
                {
                    _ = _externalFunctionAddresses.Add(address.Function);
                }
                break;
            case BoundConstant constant:
                if (constant.Type == PrimitiveType.NativeUtf16ZPointer)
                {
                    if (constant.Value < 0 || constant.Value >= _nativeUtf16ZOffsets.Length)
                    {
                        throw new InvalidOperationException("The verified UTF-16-NUL literal index is invalid.");
                    }
                    _dataFixups.Add(new NativeDataFixup(_code.DataAddress(),
                        _nativeUtf16ZOffsets[checked((int)constant.Value)]));
                    _code.Bytes(0x48, 0x89, 0xD0); // mov rax,rdx
                }
                else if (constant.Type == PrimitiveType.NativeUtf8ZPointer)
                {
                    if (constant.Value < 0 || constant.Value >= _nativeUtf8ZOffsets.Length)
                    {
                        throw new InvalidOperationException("The verified UTF-8-NUL literal index is invalid.");
                    }
                    _dataFixups.Add(new NativeDataFixup(_code.DataAddress(),
                        _nativeUtf8ZOffsets[checked((int)constant.Value)]));
                    _code.Bytes(0x48, 0x89, 0xD0); // mov rax,rdx
                }
                else if (constant.Type is PrimitiveType.Int128 or PrimitiveType.UInt128
                    or PrimitiveType.Float128 or PrimitiveType.Decimal128)
                {
                    var storage = Spill(spill);
                    _code.Bytes(0x48, 0xB8);
                    _code.Int64(unchecked((long)constant.Bits.Low));
                    _code.Store64(storage);
                    _code.Bytes(0x48, 0xB8);
                    _code.Int64(unchecked((long)constant.Bits.High));
                    _code.Store64(storage + sizeof(ulong));
                    _code.Address(storage);
                }
                else if (IsWide(constant.Type))
                {
                    _code.Bytes(0x48, 0xB8);
                    _code.Int64(constant.Value);
                }
                else
                {
                    _code.Byte(0xB8);
                    _code.Int32(constant.Type is PrimitiveType.Float32 or PrimitiveType.UInt32
                        or PrimitiveType.Decimal32
                        ? unchecked((int)constant.Value) : checked((int)constant.Value));
                }
                break;
            case BoundNull:
                _code.Bytes(0x31, 0xC0);
                break;
            case BoundOptionalLift lift:
                Expression(lift.Value, spill);
                break;
            case BoundCoalesce coalesce:
                Expression(coalesce.Optional, spill);
                _code.Bytes(0x48, 0x85, 0xC0);
                var present = _code.JumpIf(0x85);
                Expression(coalesce.Fallback, spill);
                _code.PatchRelative(present, _code.Position);
                break;
            case BoundReadonlyProjection projection:
                Expression(projection.Value, spill);
                break;
            case BoundIsNull test:
                Expression(test.Value, spill);
                _code.Bytes(0x48, 0x85, 0xC0, 0x0F, 0x94, 0xC0, 0x0F, 0xB6, 0xC0);
                break;
            case BoundIsSome pattern:
                Expression(pattern.Value, spill);
                _code.Bytes(0x48, 0x85, 0xC0);
                var empty = _code.JumpIf(0x84);
                _code.Store64(Slot(pattern.Variable));
                SetManagedRoot(Slot(pattern.Variable), active: true);
                _code.Bytes(0xB8, 0x01, 0x00, 0x00, 0x00);
                var patternDone = _code.Jump();
                _code.PatchRelative(empty, _code.Position);
                SetManagedRoot(Slot(pattern.Variable), active: false);
                _code.Bytes(0x31, 0xC0);
                _code.PatchRelative(patternDone, _code.Position);
                break;
            case BoundIsVariablePattern pattern:
                if (!ManagedClassTypes.IsOptional(pattern.Value.Type))
                {
                    Expression(pattern.Value, spill);
                    StoreValue(Slot(pattern.Variable), pattern.Value.Type);
                    _code.Bytes(0xB8, 0x01, 0x00, 0x00, 0x00);
                    break;
                }
                if (pattern.Value is not BoundRead source)
                {
                    Expression(pattern.Value, spill);
                    _code.Store64(Slot(pattern.Variable));
                    SetManagedRoot(Slot(pattern.Variable), active: true);
                    _code.Bytes(0xB8, 0x01, 0x00, 0x00, 0x00);
                    break;
                }

                var sourceSlot = OwnerPlace(source.Variable);
                if (!_managedRootFlags[_currentFunction].TryGetValue(sourceSlot, out var initializedFlag))
                {
                    throw new InvalidOperationException("Managed pattern source has no runtime initialization flag.");
                }

                // The active root bit tracks publication separately from pointer bits, including initialized null.
                _code.Load(initializedFlag);
                _code.Bytes(0x85, 0xC0);
                var vacant = _code.JumpIf(0x84);
                _code.Load64(sourceSlot);
                var absent = -1;
                if (!pattern.IncludeNull)
                {
                    _code.Bytes(0x48, 0x85, 0xC0);
                    absent = _code.JumpIf(0x84);
                }
                _code.Store64(Slot(pattern.Variable));
                SetManagedRoot(Slot(pattern.Variable), active: true);
                _code.Bytes(0xB8, 0x01, 0x00, 0x00, 0x00);
                var variablePatternDone = _code.Jump();
                _code.PatchRelative(vacant, _code.Position);
                if (absent >= 0)
                {
                    _code.PatchRelative(absent, _code.Position);
                }
                SetManagedRoot(Slot(pattern.Variable), active: false);
                _code.Bytes(0x31, 0xC0);
                _code.PatchRelative(variablePatternDone, _code.Position);
                break;
            case BoundIsInitialized initializationTest:
                EmitInitializationTest(initializationTest.Variable);
                break;
            case BoundRead read:
                var readVariable = _program.Functions[_currentFunction].Variables[read.Variable];
                if (readVariable.IsImplicitRef)
                {
                    _code.Load64(Slot(read.Variable));
                    if (!IsIndirect(read.Type))
                    {
                        LoadTypedPointerField(0, read.Type);
                    }
                }
                else if (read.Variable == 0 && IsBorrowedReceiver() && !IsUnique(read.Type))
                {
                    _code.Load64(Slot(0));
                    if (!IsIndirect(read.Type))
                    {
                        LoadTypedPointerField(0, read.Type);
                    }
                }
                else
                {
                    LoadValue(_program.Functions[_currentFunction].Variables[read.Variable].IsOut
                        ? OutSlot(read.Variable) : Slot(read.Variable), read.Type);
                }
                break;
            case BoundViewLength length:
                _code.Load(Slot(length.Variable) + 8);
                break;
            case BoundBufferLength length:
                if (_sourceDependent is not null)
                {
                    _code.Load(_sourceSlots[_currentFunction] + 8);
                }
                else
                {
                    _code.Byte(0xB8);
                    _code.Int32(_program.Buffers[length.Buffer].Values.Length);
                }
                break;
            case BoundSnapshotSpan snapshot:
                EmitSnapshotSpan(snapshot, spill);
                break;
            case BoundSpanLength length:
                Expression(length.View, spill);
                _code.LoadPointerField(TokenSpanContract.CountOffset, NativeTargetProfile.PointerSize);
                break;
            case BoundSpanRead read:
                EmitSpanRead(read, spill);
                break;
            case BoundSpanAdvance advance:
                Expression(advance.View, spill);
                CopyToFrame(Spill(spill), TokenSpanContract.DescriptorSize);
                _code.Load64(Spill(spill) + TokenSpanContract.CountOffset);
                _code.Bytes(0x48, 0x85, 0xC0);
                RuntimeBranch(0x84, 2);
                _code.Bytes(0x48, 0x83, 0xE8, 0x01);
                _code.Store64(Spill(spill) + TokenSpanContract.CountOffset);
                _code.Load64(Spill(spill));
                _code.Bytes(0x48, 0x83, 0xC0, TokenSpanContract.Stride);
                RuntimeBranch(0x82, 0);
                _code.Store64(Spill(spill));
                _code.Address(Spill(spill));
                break;
            case BoundViewRead viewRead:
                EmitViewElementAddress(viewRead, spill);
                _code.Bytes(0x8B, 0x00); // eax = [rax]
                break;
            case BoundMove move:
                LoadValue(OwnerPlace(move.Variable), move.Type);
                _code.Clear(_liveSlots[_currentFunction][move.Variable]);
                break;
            case BoundReceiverLoan loan:
                if (loan.Variable == 0 && IsBorrowedReceiver())
                {
                    _code.Load64(Slot(0));
                }
                else
                {
                    _code.Address(OwnerPlace(loan.Variable));
                }
                break;
            case BoundFieldLoan fieldLoan:
                EmitUniqueFieldPlace(fieldLoan.Place);
                break;
            case BoundMutableFieldLoan mutableField:
                EmitUniqueFieldPlace(mutableField.Place);
                break;
            case BoundMutableReceiverLoan mutableLoan:
                if (mutableLoan.Variable == 0 && IsBorrowedReceiver())
                {
                    _code.Load64(Slot(0));
                }
                else
                {
                    _code.Address(OwnerPlace(mutableLoan.Variable));
                }
                break;
            case BoundFieldRead fieldRead:
                if (ManagedClassTypes.IsClass(fieldRead.Receiver.Type))
                {
                    Expression(fieldRead.Receiver, spill);
                    var classField = _program.Classes[ManagedClassTypes.ClassIndex(
                        fieldRead.Receiver.Type)].Fields[fieldRead.Field];
                    if (IsIndirect(classField.Type))
                    {
                        _code.PointerOffset(32 + classField.Offset);
                    }
                    else
                    {
                        LoadTypedPointerField(32 + classField.Offset, fieldRead.Type);
                    }
                    break;
                }
                if (IsUnique(fieldRead.Receiver.Type))
                {
                    EmitUniqueFieldPlace(fieldRead.Receiver);
                }
                else
                {
                    Expression(fieldRead.Receiver, spill);
                }
                var field = _program.Structures[(int)fieldRead.Receiver.Type - 100].Fields[fieldRead.Field];
                if (IsIndirect(fieldRead.Receiver.Type) || IsUnique(fieldRead.Receiver.Type))
                {
                    if (IsIndirect(field.Type))
                    {
                        _code.PointerOffset(field.Offset);
                    }
                    else
                    {
                        LoadTypedPointerField(field.Offset, field.Type);
                    }
                    break;
                }
                if (field.Offset != 0)
                {
                    _code.Bytes(0x48, 0xC1, 0xE8, checked((byte)(field.Offset * 8)));
                }
                if (field.Size == 1)
                {
                    _code.Bytes(0x0F, 0xB6, 0xC0);
                }
                else if (field.Size == 2)
                {
                    _code.Bytes(0x0F, 0xB7, 0xC0);
                }
                else if (field.Size == 4)
                {
                    _code.Bytes(0x89, 0xC0); // clear adjacent aggregate bits above the field
                }
                NormalizeNarrowInteger(field.Type, checkRange: false);
                break;
            case BoundBufferRead bufferRead:
                EmitBufferIndex(bufferRead.Buffer, bufferRead.Index, bufferRead.IsEndRelativeOffset, spill);
                if (_sourceDependent is not null)
                {
                    _code.Bytes(0x48, 0x8B, 0x95); // mov rdx,[rbp+sourcePointer]
                    _code.Int32(_sourceSlots[_currentFunction]);
                    _code.Bytes(0x0F, 0xB7, 0x04, 0x42); // movzx eax,word ptr [rdx+rax*2]
                }
                else
                {
                    _dataFixups.Add(new NativeDataFixup(_code.DataAddress(), _bufferOffsets[bufferRead.Buffer]));
                    _code.Bytes(0x8B, 0x04, 0x82); // mov eax,[rdx+rax*4]
                }
                break;
            case BoundUnary unary:
                EnsureSupportedNativeFloatingUnary(unary.Type, unary.Operator);
                if (unary.Type == PrimitiveType.Float128)
                {
                    Expression(unary.Operand, spill);
                    if (unary.Operator == TokenKind.Minus)
                    {
                        StoreValue(Spill(spill), unary.Type);
                        _code.Load64(Spill(spill) + sizeof(ulong));
                        _code.Bytes(0x48, 0x0F, 0xBA, 0xF8, 0x3F); // btc rax,63
                        _code.Store64(Spill(spill) + sizeof(ulong));
                        _code.Address(Spill(spill));
                    }
                    break;
                }
                if (unary.Type is PrimitiveType.Decimal32 or PrimitiveType.Decimal64
                    or PrimitiveType.Decimal128)
                {
                    if (unary.Operator == TokenKind.Minus)
                    {
                        ZeroStruct(Spill(spill), 2 * sizeof(ulong));
                        Expression(unary.Operand, spill + 1);
                        EmitBidDecimalOperand(unary.Type, spill);
                        EmitBidDecimalNegate(unary.Type, Spill(spill), Spill(spill + 2));
                        EmitBidDecimalResult(unary.Type, spill + 2);
                    }
                    else
                    {
                        Expression(unary.Operand, spill);
                    }
                    break;
                }
                Expression(unary.Operand, spill);
                if (PrimitiveTypeFacts.IsWideInteger(unary.Type))
                {
                    if (unary.Operator != TokenKind.Plus)
                    {
                        StoreValue(Spill(spill), unary.Type);
                        EmitWideIntegerUnary(unary.Operator, unary.Type,
                            unary.OverflowBehavior, Spill(spill));
                        _code.Address(Spill(spill));
                    }
                    break;
                }
                switch (unary.Operator)
                {
                    case TokenKind.Minus:
                        if (unary.Type is PrimitiveType.Float16 or PrimitiveType.BFloat16)
                        {
                            _code.Byte(0x35);
                            _code.Int32(0x8000);
                        }
                        else if (unary.Type is PrimitiveType.Float32 or PrimitiveType.Float64)
                        {
                            if (unary.Type == PrimitiveType.Float64)
                            {
                                _code.Byte(0x48);
                            }
                            _code.Bytes(0x0F, 0xBA, 0xF8, unary.Type == PrimitiveType.Float32 ? (byte)31 : (byte)63);
                        }
                        else
                        {
                            if (IsWide(unary.Type))
                            {
                                _code.Byte(0x48);
                            }
                            _code.Bytes(0xF7, 0xD8);
                            if (IsNarrowInteger(unary.Type))
                            {
                                NormalizeNarrowInteger(unary.Type,
                                    checkRange: unary.OverflowBehavior == BoundIntegerOverflowBehavior.Checked);
                            }
                            else if (unary.OverflowBehavior == BoundIntegerOverflowBehavior.Checked)
                            {
                                RuntimeBranch(unary.Type is PrimitiveType.UInt32 or PrimitiveType.UInt64
                                    ? (byte)0x82 : (byte)0x80, 0);
                            }
                        }
                        break;
                    case TokenKind.Tilde:
                        if (IsWide(unary.Type))
                        {
                            _code.Byte(0x48);
                        }
                        _code.Bytes(0xF7, 0xD0);
                        NormalizeNarrowInteger(unary.Type, checkRange: false);
                        break;
                    case TokenKind.Bang:
                        _code.Bytes(0x83, 0xF0, 0x01);
                        break;
                }
                break;
            case BoundUpdate update:
                EnsureSupportedNativeFloatingOperator(update.Type);
                var rootedUpdate = UpdateAddress(update.Place, spill);
                _code.Store64(Spill(spill + 1));
                if (PrimitiveTypeFacts.IsWideInteger(update.Type)
                    || update.Type == PrimitiveType.Float128)
                {
                    _code.Load64(Spill(spill + 1));
                    _code.AddressToRdx(Spill(spill + 2));
                    _code.CopyMemory(Size(update.Type));
                }
                else if (update.Type is PrimitiveType.Decimal32 or PrimitiveType.Decimal64
                    or PrimitiveType.Decimal128)
                {
                    ZeroStruct(Spill(spill + 2), 2 * sizeof(ulong));
                    _code.Load64(Spill(spill + 1));
                    _code.AddressToRdx(Spill(spill + 2));
                    _code.CopyMemory(Size(update.Type));
                }
                else
                {
                    _code.LoadPointerField(0, Size(update.Type));
                    NormalizeNarrowInteger(update.Type, checkRange: false);
                    StoreValue(Spill(spill + 2), update.Type);
                }
                var updateBegin = _code.Position;
                var updateFunclet = -1;
                if (rootedUpdate && _checkedExceptions && !_emittingFunclet)
                {
                    updateFunclet = _funclets.Count;
                    _funclets.Add((new BoundBlock([], update.Span), _currentFunction, 0, 0));
                    _managedStagedRoots.Add(updateFunclet, [Spill(spill)]);
                }
                if (PrimitiveTypeFacts.IsWideInteger(update.Type))
                {
                    _code.Bytes(0x48, 0xB8);
                    _code.Int64(1);
                    _code.Store64(Spill(spill + 3));
                    _code.Bytes(0x31, 0xC0);
                    _code.Store64(Spill(spill + 3 + sizeof(ulong)));
                    EmitWideIntegerBinary(update.Operator == TokenKind.PlusPlus
                        ? TokenKind.Plus : TokenKind.Minus, update.Type,
                        update.OverflowBehavior, Spill(spill + 2), Spill(spill + 3),
                        update.Type, Spill(spill + 4));
                    _code.Load64(Spill(spill + 1), destinationRegister: true);
                    _code.Address(Spill(spill + 4));
                    _code.CopyMemory(Size(update.Type));
                }
                else if (update.Type is PrimitiveType.Float16 or PrimitiveType.BFloat16)
                {
                    _code.Byte(0xB8);
                    _code.Int32(update.Type == PrimitiveType.Float16 ? 0x3C00 : 0x3F80);
                    FloatingNarrowBinary(update.Operator == TokenKind.PlusPlus
                        ? TokenKind.Plus : TokenKind.Minus, update.Type, spill + 2);
                    _code.Load64(Spill(spill + 1), destinationRegister: true);
                    _code.StorePointerField(UpdateStoreWidth(update.Place, update.Type));
                }
                else if (update.Type is PrimitiveType.Float32 or PrimitiveType.Float64)
                {
                    if (update.Type == PrimitiveType.Float32)
                    {
                        _code.Bytes(0xB8, 0x00, 0x00, 0x80, 0x3F);
                    }
                    else
                    {
                        _code.Bytes(0x48, 0xB8);
                        _code.Int64(0x3FF0_0000_0000_0000);
                    }
                    FloatingBinary(update.Operator == TokenKind.PlusPlus
                        ? TokenKind.Plus : TokenKind.Minus, update.Type, spill + 2);
                    _code.Load64(Spill(spill + 1), destinationRegister: true);
                    _code.StorePointerField(UpdateStoreWidth(update.Place, update.Type));
                }
                else if (update.Type == PrimitiveType.Float128)
                {
                    _code.Bytes(0x31, 0xC0);
                    _code.Store64(Spill(spill + 3));
                    _code.Bytes(0x48, 0xB8);
                    _code.Int64(0x3FFF_0000_0000_0000);
                    _code.Store64(Spill(spill + 3) + sizeof(ulong));
                    EmitBinary128Arithmetic(update.Operator == TokenKind.PlusPlus
                            ? TokenKind.Plus : TokenKind.Minus,
                        Spill(spill + 2), Spill(spill + 3), Spill(spill + 4));
                    _code.Load64(Spill(spill + 1), destinationRegister: true);
                    _code.Address(Spill(spill + 4));
                    _code.CopyMemory(Size(update.Type));
                }
                else if (update.Type is PrimitiveType.Decimal32 or PrimitiveType.Decimal64
                    or PrimitiveType.Decimal128)
                {
                    ZeroStruct(Spill(spill + 3), 2 * sizeof(ulong));
                    var one = BidDecimalOne(update.Type);
                    _code.Bytes(0x48, 0xB8);
                    _code.Int64(unchecked((long)one.Low));
                    _code.Store64(Spill(spill + 3));
                    _code.Bytes(0x48, 0xB8);
                    _code.Int64(unchecked((long)one.High));
                    _code.Store64(Spill(spill + 3) + sizeof(ulong));
                    EmitBidDecimalArithmetic(update.Type,
                        update.Operator == TokenKind.PlusPlus ? TokenKind.Plus : TokenKind.Minus,
                        Spill(spill + 2), Spill(spill + 3), Spill(spill + 4));
                    _code.Load64(Spill(spill + 1), destinationRegister: true);
                    if (update.Type == PrimitiveType.Decimal128)
                    {
                        _code.Address(Spill(spill + 4));
                        _code.CopyMemory(Size(update.Type));
                    }
                    else
                    {
                        LoadValue(Spill(spill + 4), update.Type);
                        _code.StorePointerField(UpdateStoreWidth(update.Place, update.Type));
                    }
                }
                else if (IsWide(update.Type))
                {
                    _code.Byte(0x48);
                }
                if (!PrimitiveTypeFacts.IsWideInteger(update.Type)
                    && update.Type is not (PrimitiveType.Float16 or PrimitiveType.BFloat16
                        or PrimitiveType.Float32 or PrimitiveType.Float64 or PrimitiveType.Float128
                        or PrimitiveType.Decimal32 or PrimitiveType.Decimal64 or PrimitiveType.Decimal128))
                {
                    _code.Bytes(0x83, update.Operator == TokenKind.PlusPlus ? (byte)0xC0 : (byte)0xE8, 0x01);
                    if (IsNarrowInteger(update.Type))
                    {
                        NormalizeNarrowInteger(update.Type,
                            checkRange: update.OverflowBehavior == BoundIntegerOverflowBehavior.Checked);
                    }
                    else if (update.OverflowBehavior == BoundIntegerOverflowBehavior.Checked)
                    {
                        RuntimeBranch(update.Type is PrimitiveType.UInt32 or PrimitiveType.UInt64
                            ? (byte)0x82 : (byte)0x80, 0);
                    }
                    _code.Load64(Spill(spill + 1), destinationRegister: true);
                    _code.StorePointerField(UpdateStoreWidth(update.Place, update.Type));
                }
                if (updateFunclet >= 0)
                {
                    _code.Byte(0x90);
                    _scopes[_currentFunction].Add(new NativeExceptionScope(updateBegin, _code.Position,
                        -updateFunclet - 1, 0));
                }
                if (rootedUpdate)
                {
                    SetManagedRoot(Spill(spill), active: false);
                }
                if (!update.IsPrefix)
                {
                    if (PrimitiveTypeFacts.IsWideInteger(update.Type))
                    {
                        _code.Address(Spill(spill + 2));
                    }
                    else
                    {
                        LoadValue(Spill(spill + 2), update.Type);
                    }
                }
                else if (update.Type == PrimitiveType.Float128)
                {
                    _code.Address(Spill(spill + 4));
                }
                else if (PrimitiveTypeFacts.IsWideInteger(update.Type))
                {
                    _code.Address(Spill(spill + 4));
                }
                else if (update.Type is PrimitiveType.Decimal32 or PrimitiveType.Decimal64
                    or PrimitiveType.Decimal128)
                {
                    EmitBidDecimalResult(update.Type, spill + 4);
                }
                break;
            case BoundCompoundAssignment compound:
            {
                EnsureSupportedNativeFloatingOperator(compound.Type);
                var rooted = UpdateAddress(compound.Place, spill);
                _code.Store64(Spill(spill + 1));
                if (PrimitiveTypeFacts.IsWideInteger(compound.Type)
                    || compound.Type == PrimitiveType.Float128)
                {
                    _code.Load64(Spill(spill + 1));
                    _code.AddressToRdx(Spill(spill + 2));
                    _code.CopyMemory(Size(compound.Type));
                }
                else if (compound.Type is PrimitiveType.Decimal32 or PrimitiveType.Decimal64
                    or PrimitiveType.Decimal128)
                {
                    ZeroStruct(Spill(spill + 2), 2 * sizeof(ulong));
                    _code.Load64(Spill(spill + 1));
                    _code.AddressToRdx(Spill(spill + 2));
                    _code.CopyMemory(Size(compound.Type));
                }
                else
                {
                    _code.LoadPointerField(0, Size(compound.Type));
                    NormalizeNarrowInteger(compound.Type, checkRange: false);
                    StoreValue(Spill(spill + 2), compound.Type);
                }
                var begin = _code.Position;
                var funclet = -1;
                if (rooted && _checkedExceptions && !_emittingFunclet)
                {
                    funclet = _funclets.Count;
                    _funclets.Add((new BoundBlock([], compound.Span), _currentFunction, 0, 0));
                    _managedStagedRoots.Add(funclet, [Spill(spill)]);
                }
                if (compound.Type is PrimitiveType.Decimal32 or PrimitiveType.Decimal64
                    or PrimitiveType.Decimal128)
                {
                    ZeroStruct(Spill(spill), 2 * sizeof(ulong));
                }
                Expression(compound.Right, spill + 4);
                if (PrimitiveTypeFacts.IsWideInteger(compound.Type))
                {
                    StoreValue(Spill(spill + 4), compound.Right.Type);
                    EmitWideIntegerBinary(CompoundOperators.Binary(compound.Operator),
                        compound.Type, compound.OverflowBehavior, Spill(spill + 2),
                        Spill(spill + 4), compound.Right.Type, Spill(spill + 2));
                    _code.Load64(Spill(spill + 1), destinationRegister: true);
                    _code.Address(Spill(spill + 2));
                    _code.CopyMemory(Size(compound.Type));
                }
                else if (compound.Type is PrimitiveType.Float16 or PrimitiveType.BFloat16)
                {
                    FloatingNarrowBinary(CompoundOperators.Binary(compound.Operator),
                        compound.Type, spill + 2);
                    _code.Load64(Spill(spill + 1), destinationRegister: true);
                    _code.StorePointerField(UpdateStoreWidth(compound.Place, compound.Type));
                }
                else if (compound.Type is PrimitiveType.Float32 or PrimitiveType.Float64)
                {
                    FloatingBinary(CompoundOperators.Binary(compound.Operator),
                        compound.Type, spill + 2);
                    _code.Load64(Spill(spill + 1), destinationRegister: true);
                    _code.StorePointerField(UpdateStoreWidth(compound.Place, compound.Type));
                }
                else if (compound.Type == PrimitiveType.Float128)
                {
                    EmitBinary128Arithmetic(CompoundOperators.Binary(compound.Operator),
                        Spill(spill + 2), Spill(spill + 4), Spill(spill + 2));
                    _code.Load64(Spill(spill + 1), destinationRegister: true);
                    _code.Address(Spill(spill + 2));
                    _code.CopyMemory(Size(compound.Type));
                }
                else if (compound.Type is PrimitiveType.Decimal32 or PrimitiveType.Decimal64
                    or PrimitiveType.Decimal128)
                {
                    EmitBidDecimalOperand(compound.Type, spill);
                    EmitBidDecimalArithmetic(compound.Type, CompoundOperators.Binary(compound.Operator),
                        Spill(spill + 2), Spill(spill), Spill(spill + 4));
                    _code.Load64(Spill(spill + 1), destinationRegister: true);
                    if (compound.Type == PrimitiveType.Decimal128)
                    {
                        _code.Address(Spill(spill + 4));
                        _code.CopyMemory(Size(compound.Type));
                    }
                    else
                    {
                        LoadValue(Spill(spill + 4), compound.Type);
                        _code.StorePointerField(UpdateStoreWidth(compound.Place, compound.Type));
                    }
                    EmitBidDecimalResult(compound.Type, spill + 4);
                }
                else
                {
                    if (IsWide(compound.Right.Type) || CompoundOperators.IsShift(compound.Operator))
                    {
                        _code.Bytes(0x48, 0x89, 0xC1);
                    }
                    else
                    {
                        _code.Bytes(0x89, 0xC1);
                    }
                    LoadValue(Spill(spill + 2), compound.Type);
                    Binary(CompoundOperators.Binary(compound.Operator), compound.Type, compound.OverflowBehavior);
                    _code.Load64(Spill(spill + 1), destinationRegister: true);
                    _code.StorePointerField(UpdateStoreWidth(compound.Place, compound.Type));
                }
                if (funclet >= 0)
                {
                    _code.Byte(0x90);
                    _scopes[_currentFunction].Add(new NativeExceptionScope(begin, _code.Position,
                        -funclet - 1, 0));
                }
                if (rooted)
                {
                    SetManagedRoot(Spill(spill), active: false);
                }
                break;
            }
            case BoundNominalUpdate update:
            {
                var rooted = UpdateAddress(update.Place, spill);
                _code.Store64(Spill(spill + 1));
                _code.AddressToRdx(Spill(spill + 2));
                _code.CopyMemory(Size(update.Type));
                var callBegin = _code.Position;
                var rootFunclet = -1;
                if (rooted && _checkedExceptions && !_emittingFunclet)
                {
                    rootFunclet = _funclets.Count;
                    _funclets.Add((new BoundBlock([], update.Span), _currentFunction, 0, 0));
                    _managedStagedRoots.Add(rootFunclet, [Spill(spill)]);
                }
                LoadValue(Spill(spill + 2), update.Type);
                _code.MoveArgument(IsIndirect(update.Type) ? 1 : 0, true);
                if (IsIndirect(update.Type))
                {
                    _code.Address(Spill(spill + 3));
                    _code.MoveArgument(0, true);
                }
                EmitTargetCall(update.Function);
                if (!IsIndirect(update.Type))
                {
                    StoreValue(Spill(spill + 3), update.Type);
                }
                if (rootFunclet >= 0)
                {
                    _code.Byte(0x90);
                    _scopes[_currentFunction].Add(new NativeExceptionScope(callBegin, _code.Position,
                        -rootFunclet - 1, 0));
                }
                _code.Address(Spill(spill + 3));
                _code.Load64(Spill(spill + 1), destinationRegister: true);
                _code.CopyMemory(Size(update.Type));
                if (rooted)
                {
                    SetManagedRoot(Spill(spill), active: false);
                }
                LoadValue(Spill(spill + (update.IsPrefix ? 3 : 2)), update.Type);
                break;
            }
            case BoundCast cast:
                Expression(cast.Operand, spill);
                Cast(cast.Operand.Type, cast.Type, spill);
                break;
            case TrRmScalarConvert conversion:
                Expression(conversion.Operand, spill);
                ConvertInteger(conversion.Operand.Type, conversion.Type, conversion.Policy);
                break;
            case TrRmScalarReinterpret reinterpret:
                Expression(reinterpret.Operand, spill);
                break;
            case TrRmScalarOptionalNone none:
                if (IsIndirect(none.Type))
                {
                    ZeroStruct(Spill(spill), Size(none.Type));
                    _code.Address(Spill(spill));
                }
                else
                {
                    _code.Bytes(0x31, 0xC0);
                }
                break;
            case TrRmScalarOptionalLift lift:
            {
                var storage = Spill(spill);
                ZeroStruct(storage, Size(lift.Type));
                Expression(lift.Value, spill + 1);
                StoreValue(storage, lift.Value.Type);
                _code.Byte(0xB8);
                _code.Int32(1);
                _code.StoreField(storage + Size(lift.Value.Type), 1);
                if (IsIndirect(lift.Type))
                {
                    _code.Address(storage);
                }
                else
                {
                    _code.Load64(storage);
                }
                break;
            }
            case TrRmScalarOptionalPresence presence:
                Expression(presence.Optional, spill);
                EmitOptionalPresenceInAccumulator(presence.Optional.Type);
                break;
            case TrRmScalarOptionalEquality equality:
                EmitOptionalEquality(equality, spill);
                break;
            case BoundBinary binary:
                EnsureSupportedNativeFloatingBinary(binary.Left.Type, binary.Operator);
                if (binary.Operator is TokenKind.AmpersandAmpersand or TokenKind.PipePipe)
                {
                    Expression(binary.Left, spill);
                    _code.Bytes(0x85, 0xC0);
                    var skip = _code.JumpIf(binary.Operator == TokenKind.AmpersandAmpersand ? (byte)0x84 : (byte)0x85);
                    Expression(binary.Right, spill);
                    _code.PatchRelative(skip, _code.Position);
                    return;
                }
                if (PrimitiveTypeFacts.IsWideInteger(binary.Left.Type))
                {
                    Expression(binary.Left, spill);
                    StoreValue(Spill(spill), binary.Left.Type);
                    Expression(binary.Right, spill + 1);
                    StoreValue(Spill(spill + 1), binary.Right.Type);
                    EmitWideIntegerBinary(binary.Operator, binary.Left.Type,
                        binary.OverflowBehavior, Spill(spill), Spill(spill + 1),
                        binary.Right.Type, Spill(spill + 2));
                    break;
                }
                if (IsBidDecimal(binary.Left.Type))
                {
                    ZeroStruct(Spill(spill), 2 * sizeof(ulong));
                    Expression(binary.Left, spill + 1);
                    EmitBidDecimalOperand(binary.Left.Type, spill);
                    ZeroStruct(Spill(spill + 1), 2 * sizeof(ulong));
                    Expression(binary.Right, spill + 2);
                    EmitBidDecimalOperand(binary.Right.Type, spill + 1);
                    if (IsFloatingComparison(binary.Operator))
                    {
                        EmitBidDecimalComparison(binary.Operator, binary.Left.Type,
                            Spill(spill), Spill(spill + 1));
                    }
                    else
                    {
                        EmitBidDecimalArithmetic(binary.Left.Type, binary.Operator,
                            Spill(spill), Spill(spill + 1), Spill(spill + 2));
                        EmitBidDecimalResult(binary.Left.Type, spill + 2);
                    }
                    break;
                }
                var floating = binary.Left.Type is PrimitiveType.Float32 or PrimitiveType.Float64;
                var narrowFloating = binary.Left.Type is PrimitiveType.Float16 or PrimitiveType.BFloat16;
                var binary128Floating = binary.Left.Type == PrimitiveType.Float128;
                Expression(binary.Left, spill);
                if (!floating && (TryEmitUnsignedPowerOfTwoBinary(binary) || TryEmitSignedPowerOfTwoBinary(binary)))
                {
                    break;
                }
                StoreValue(Spill(spill), binary.Left.Type);
                Expression(binary.Right, spill + 1);
                if (floating)
                {
                    FloatingBinary(binary.Operator, binary.Left.Type, spill);
                }
                else if (narrowFloating)
                {
                    FloatingNarrowBinary(binary.Operator, binary.Left.Type, spill);
                }
                else if (binary128Floating)
                {
                    StoreValue(Spill(spill + 1), binary.Right.Type);
                    if (IsFloatingComparison(binary.Operator))
                    {
                        EmitBinary128Comparison(binary.Operator, Spill(spill), Spill(spill + 1));
                    }
                    else
                    {
                        EmitBinary128Arithmetic(binary.Operator,
                            Spill(spill), Spill(spill + 1), Spill(spill + 2));
                    }
                }
                else
                {
                    if (IsWide(binary.Right.Type)
                        || binary.Operator is TokenKind.ShiftLeft or TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned)
                    {
                        _code.Bytes(0x48, 0x89, 0xC1); // mov rcx,rax
                    }
                    else
                    {
                        _code.Bytes(0x89, 0xC1); // mov ecx,eax
                    }
                    LoadValue(Spill(spill), binary.Left.Type);
                    Binary(binary.Operator, binary.Left.Type, binary.OverflowBehavior);
                }
                break;
            case BoundConditional conditional:
            {
                Expression(conditional.Condition, spill);
                _code.Bytes(0x85, 0xC0);
                var whenFalse = _code.JumpIf(0x84);
                Expression(conditional.WhenTrue, spill);
                var done = _code.Jump();
                _code.PatchRelative(whenFalse, _code.Position);
                Expression(conditional.WhenFalse, spill);
                _code.PatchRelative(done, _code.Position);
                break;
            }
            case BoundUnsafeExpression unsafeExpression:
                Expression(unsafeExpression.Expression, spill);
                break;
            case BoundIndirectCall call:
                NativeEmitter.EnsureNativeFunctionPointerConventionSupported(call.Target.Type,
                    _program.NativeFunctionPointerSignatures);
                var argumentBase = spill + 1;
                var argumentExpressionSpill = argumentBase + call.Arguments.Length;
                Expression(call.Target, argumentExpressionSpill);
                StoreValue(Spill(spill), call.Target.Type);
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    var argument = call.Arguments[i];
                    Expression(argument, argumentExpressionSpill);
                    if (argument is BoundNativeOutputReservation)
                    {
                        _code.Store64(Spill(argumentBase + i));
                    }
                    else
                    {
                        StoreValue(Spill(argumentBase + i), argument.Type);
                    }
                }
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    var argument = call.Arguments[i];
                    var isNativeOutput = argument is BoundNativeOutputReservation;
                    var argumentType = argument.Type;
                    if (isNativeOutput)
                    {
                        _code.Load64(Spill(argumentBase + i));
                    }
                    else
                    {
                        LoadValue(Spill(argumentBase + i), argumentType);
                    }
                    if (i < 4)
                    {
                        if (isNativeOutput)
                        {
                            _code.MoveArgument(i, true);
                        }
                        else if (argumentType is PrimitiveType.Float32 or PrimitiveType.Float64)
                        {
                            _code.MoveFloatingArgument(i, argumentType == PrimitiveType.Float64);
                        }
                        else
                        {
                            _code.MoveArgument(i, IsWide(argumentType));
                        }
                    }
                    else
                    {
                        _code.StoreOutgoing64(32 + (8 * (i - 4)));
                    }
                }
                _code.Load64(Spill(spill));
                _code.Bytes(0x49, 0x89, 0xC3); // mov r11,rax
                _code.Bytes(0x41, 0xFF, 0xD3); // call r11
                var callSignature = _program.NativeFunctionPointerSignatures[
                    NativeFunctionPointerTypes.Index(call.Target.Type)];
                if ((callSignature.EffectsMask & 2) != 0)
                {
                    _code.Bytes(0x0F, 0x0B); // no-return promises must not publish a result
                }
                else if (call.Type is PrimitiveType.Float32 or PrimitiveType.Float64)
                {
                    _code.MoveFloatingReturnToAccumulator(call.Type == PrimitiveType.Float64);
                }
                else
                {
                    NormalizeNativeNarrowReturn(call.Type);
                }
                break;
            case BoundCall call:
                var argumentsBase = spill + CallResultSpills(call);
                var argumentSpills = new int[call.Arguments.Length];
                var auxiliarySpills = new int[call.Arguments.Length];
                var nextSpill = argumentsBase;
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    argumentSpills[i] = nextSpill;
                    nextSpill += CallArgumentValueSpills(call, i);
                }
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    auxiliarySpills[i] = nextSpill;
                    nextSpill += CallArgumentAuxiliarySpills(call.Arguments[i]);
                }
                var argumentExpressionSpillBase = nextSpill;
                List<(int Value, int Live, PrimitiveType Type)>? staged = null;
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    if (!IsUnique(call.Arguments[i].Type)
                        || call.Arguments[i] is BoundReceiverLoan or BoundMutableReceiverLoan
                            or BoundFieldLoan or BoundMutableFieldLoan or BoundOutArgument)
                    {
                        continue;
                    }
                    staged ??= [];
                    var live = Spill(auxiliarySpills[i]);
                    staged.Add((Spill(argumentSpills[i]), live, call.Arguments[i].Type));
                    _code.Clear(live);
                }
                var stagedBegin = _code.Position;
                var stagedFunclet = -1;
                var managedFunclet = -1;
                if (_checkedExceptions && _managedNodes && !_emittingFunclet)
                {
                    List<int>? managedSlots = null;
                    for (var i = 0; i < call.Arguments.Length; i++)
                    {
                        if (ManagedClassTypes.IsManaged(call.Arguments[i].Type))
                        {
                            managedSlots ??= [];
                            managedSlots.Add(Spill(argumentSpills[i]));
                            if (call.Arguments[i] is BoundOutArgument)
                            {
                                managedSlots.Add(Spill(auxiliarySpills[i]));
                            }
                        }
                    }
                    if (managedSlots is not null)
                    {
                        managedFunclet = _funclets.Count;
                        _funclets.Add((new BoundBlock([], call.Span), _currentFunction, 0, 0));
                        _managedStagedRoots.Add(managedFunclet, managedSlots);
                    }
                }
                if (_checkedExceptions && staged is not null && !_emittingFunclet)
                {
                    stagedFunclet = _funclets.Count;
                    _funclets.Add((new BoundBlock([], call.Span), _currentFunction, 0, 0));
                    _stagedOwners.Add(stagedFunclet, staged);
                }
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    if (call.Arguments[i] is BoundOutArgument)
                    {
                        var scratch = Spill(auxiliarySpills[i]);
                        ZeroStruct(scratch, IsIndirect(call.Arguments[i].Type) ? Size(call.Arguments[i].Type) : 8);
                        if (ManagedClassTypes.IsManaged(call.Arguments[i].Type))
                        {
                            SetManagedRoot(scratch, active: true);
                        }
                        _code.Address(scratch);
                        _code.Store64(Spill(argumentSpills[i]));
                    }
                    else if (call.Arguments[i] is BoundNativeOutputReservation reservation)
                    {
                        Expression(reservation, argumentExpressionSpillBase);
                        _code.Store64(Spill(argumentSpills[i]));
                    }
                    else
                    {
                        if (IsImplicitReferenceArgument(call, i))
                        {
                            EmitImplicitReferenceAddress(call, i,
                                argumentExpressionSpillBase);
                        }
                        else
                        {
                            Expression(call.Arguments[i], argumentExpressionSpillBase);
                        }
                        if (IsImplicitReferenceArgument(call, i)
                            || call.Arguments[i] is BoundReceiverLoan or BoundMutableReceiverLoan
                            or BoundFieldLoan or BoundMutableFieldLoan)
                        {
                            _code.Store64(Spill(argumentSpills[i]));
                        }
                        else
                        {
                            StoreValue(Spill(argumentSpills[i]), call.Arguments[i].Type);
                            if (ManagedClassTypes.IsManaged(call.Arguments[i].Type))
                            {
                                SetManagedRoot(Spill(argumentSpills[i]), active: true);
                            }
                        }
                        if (staged is not null && IsUnique(call.Arguments[i].Type)
                            && call.Arguments[i] is not (BoundReceiverLoan or BoundMutableReceiverLoan
                                or BoundFieldLoan or BoundMutableFieldLoan))
                        {
                            _code.Set(Spill(auxiliarySpills[i]));
                        }
                    }
                }
                if (stagedFunclet >= 0)
                {
                    _code.Byte(0x90);
                    _scopes[_currentFunction].Add(new NativeExceptionScope(stagedBegin, _code.Position,
                        -stagedFunclet - 1, 0));
                }
                if (staged is not null)
                {
                    foreach (var stagedOwner in staged)
                    {
                        _code.Clear(stagedOwner.Live);
                    }
                }
                foreach (var argument in call.Arguments)
                {
                    if (argument is BoundOutArgument output && IsUnique(output.Type))
                    {
                        DropOwner(output.Variable);
                    }
                    if (argument is BoundOutArgument managedOutput
                        && ManagedClassTypes.IsManaged(managedOutput.Type))
                    {
                        SetManagedRoot(OwnerPlace(managedOutput.Variable), active: false);
                    }
                }
                var hiddenReturn = IsIndirectResult(call.Type) ? 1 : 0;
                var sourceHidden = _sourceDependent?[call.Function] == true ? 2 : 0;
                var hidden = hiddenReturn + sourceHidden;
                var targetFunction = _program.Functions[call.Function];
                if (targetFunction.DirectNativeImport is DirectNativeImport directImport)
                {
                    NativeEmitter.EnsureNativeCallingConventionSupported(directImport.CallingConvention);
                    NativeEmitter.EnsureNativeImportResolutionSupported(directImport);
                }
                if (targetFunction.NativeFunctionPointerType is PrimitiveType nativeFunctionPointerType)
                {
                    NativeEmitter.EnsureNativeFunctionPointerConventionSupported(nativeFunctionPointerType,
                        _program.NativeFunctionPointerSignatures);
                }
                var nativeAbi = targetFunction.DirectNativeImport is not null
                    || targetFunction.NativeFunctionPointerType is not null;
                for (var i = Math.Max(0, 4 - hidden); i < call.Arguments.Length; i++)
                {
                    var argumentIndex = call.ArgumentIndex(i);
                    if (IsImplicitReferenceArgument(call, argumentIndex)
                        || call.Arguments[argumentIndex] is BoundOutArgument or BoundReceiverLoan or BoundMutableReceiverLoan
                        or BoundFieldLoan or BoundMutableFieldLoan or BoundNativeOutputReservation)
                    {
                        _code.Load64(Spill(argumentSpills[argumentIndex]));
                    }
                    else
                    {
                        LoadValue(Spill(argumentSpills[argumentIndex]), call.Arguments[argumentIndex].Type);
                    }
                    _code.StoreOutgoing64(32 + (8 * (i + hidden - 4)));
                }
                for (var i = 0; i < Math.Min(4 - hidden, call.Arguments.Length); i++)
                {
                    var argumentIndex = call.ArgumentIndex(i);
                    if (IsImplicitReferenceArgument(call, argumentIndex)
                        || call.Arguments[argumentIndex] is BoundOutArgument or BoundReceiverLoan or BoundMutableReceiverLoan
                        or BoundFieldLoan or BoundMutableFieldLoan or BoundNativeOutputReservation)
                    {
                        _code.Load64(Spill(argumentSpills[argumentIndex]));
                    }
                    else
                    {
                        LoadValue(Spill(argumentSpills[argumentIndex]), call.Arguments[argumentIndex].Type);
                    }
                    var argumentType = call.Arguments[argumentIndex].Type;
                    var isAddress = IsImplicitReferenceArgument(call, argumentIndex)
                        || call.Arguments[argumentIndex] is BoundOutArgument or BoundReceiverLoan
                            or BoundMutableReceiverLoan or BoundFieldLoan or BoundMutableFieldLoan
                            or BoundNativeOutputReservation;
                    if (!isAddress && IsFloatingCallCarrier(argumentType))
                    {
                        _code.MoveFloatingArgument(i + hidden, argumentType == PrimitiveType.Float64);
                    }
                    else
                    {
                        _code.MoveArgument(i + hidden,
                            isAddress || IsStruct(argumentType) || IsWide(argumentType) || IsIndirect(argumentType));
                    }
                }
                if (sourceHidden != 0)
                {
                    _code.Load64(_sourceSlots[_currentFunction]);
                    _code.MoveArgument(0, true);
                    _code.Load(_sourceSlots[_currentFunction] + 8);
                    _code.MoveArgument(1, false);
                }
                if (hiddenReturn != 0)
                {
                    _code.Address(Spill(spill));
                    _code.MoveArgument(0, true);
                }
                EmitTargetCall(call.Function);
                if (IsFloatingCallCarrier(call.Type))
                {
                    _code.MoveFloatingReturnToAccumulator(call.Type == PrimitiveType.Float64);
                    if (call.Type is PrimitiveType.Float16 or PrimitiveType.BFloat16)
                    {
                        _code.Bytes(0x0F, 0xB7, 0xC0); // retain only the typed sixteen-bit payload
                    }
                }
                else if (call.Type == PrimitiveType.Float128)
                {
                    _code.StoreFloating128Return(Spill(spill));
                    _code.Address(Spill(spill));
                }
                else if (nativeAbi)
                {
                    NormalizeNativeNarrowReturn(call.Type);
                }
                if (hiddenReturn != 0)
                {
                    _code.Address(Spill(spill));
                }
                var hasOut = false;
                foreach (var argument in call.Arguments)
                {
                    hasOut |= argument is BoundOutArgument;
                }
                if (hasOut)
                {
                    var conditionalOutputs = call.Type == PrimitiveType.Bool;
                    var skip = -1;
                    if (conditionalOutputs)
                    {
                        _code.Bytes(0x85, 0xC0);
                        skip = _code.JumpIf(0x84);
                    }
                    else if (call.Type != PrimitiveType.Void && !IsIndirect(call.Type))
                    {
                        StoreValue(Spill(spill), call.Type);
                    }
                    for (var i = 0; i < call.Arguments.Length; i++)
                    {
                        if (call.Arguments[i] is BoundOutArgument output)
                        {
                            var scratch = Spill(auxiliarySpills[i]);
                            var destination = _program.Functions[_currentFunction].Variables[output.Variable].IsOut
                                ? OutSlot(output.Variable) : Slot(output.Variable);
                            if (IsIndirect(output.Type))
                            {
                                _code.Address(scratch);
                                CopyToFrame(destination, Size(output.Type));
                                if (IsUnique(output.Type))
                                {
                                    _code.Set(_liveSlots[_currentFunction][output.Variable]);
                                }
                                SetInitializationFlag(output.Variable, initialized: true);
                                continue;
                            }
                            if (output.Type is PrimitiveType.Bool or PrimitiveType.Char16)
                            {
                                _code.LoadUnsigned(scratch, output.Type == PrimitiveType.Bool ? 1 : 2);
                            }
                            else
                            {
                                LoadValue(scratch, output.Type);
                            }
                            StoreValue(destination, output.Type);
                            if (ManagedClassTypes.IsManaged(output.Type))
                            {
                                SetManagedRoot(destination, active: true);
                            }
                            if (IsUnique(output.Type))
                            {
                                _code.Set(_liveSlots[_currentFunction][output.Variable]);
                            }
                            SetInitializationFlag(output.Variable, initialized: true);
                        }
                    }
                    if (conditionalOutputs)
                    {
                        _code.Bytes([0xB8, 0x01, 0x00, 0x00, 0x00]);
                        var done = _code.Jump();
                        _code.PatchRelative(skip, _code.Position);
                        for (var i = 0; i < call.Arguments.Length; i++)
                        {
                            if (call.Arguments[i] is BoundOutArgument output)
                            {
                                if (IsUnique(output.Type))
                                {
                                    _code.Clear(_liveSlots[_currentFunction][output.Variable]);
                                }
                                SetInitializationFlag(output.Variable, initialized: false);
                            }
                        }
                        _code.PatchRelative(done, _code.Position);
                    }
                    else if (call.Type != PrimitiveType.Void)
                    {
                        if (IsIndirect(call.Type))
                        {
                            _code.Address(Spill(spill));
                        }
                        else
                        {
                            LoadValue(Spill(spill), call.Type);
                        }
                    }
                }
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    if (ManagedClassTypes.IsManaged(call.Arguments[i].Type))
                    {
                        SetManagedRoot(Spill(argumentSpills[i]), active: false);
                        if (call.Arguments[i] is BoundOutArgument)
                        {
                            SetManagedRoot(Spill(auxiliarySpills[i]), active: false);
                        }
                    }
                }
                if (managedFunclet >= 0)
                {
                    _code.Byte(0x90);
                    _scopes[_currentFunction].Add(new NativeExceptionScope(stagedBegin, _code.Position,
                        -managedFunclet - 1, 0));
                }
                break;
        }
    }

    private void Cast(PrimitiveType source, PrimitiveType target, int spill)
    {
        if (!RequiresFloatingEnvironment(source, target))
        {
            CastPrimitive(source, target, 0, 0, spill);
            return;
        }

        var operand = Spill(spill + 2);
        // MXCSR setup uses EAX, so preserve the conversion payload before changing the control state.
        StoreValue(operand, source);
        var savedMxcsr = Spill(spill);
        var controlledMxcsr = Spill(spill + 1);
        BeginFloatingEnvironment(savedMxcsr, controlledMxcsr);
        LoadValue(operand, source);
        CastPrimitive(source, target, savedMxcsr, controlledMxcsr, spill);
        _code.LoadMxcsr(savedMxcsr);
    }

    private void FloatingNarrowComparison(TokenKind operation, PrimitiveType type, int spill)
    {
        var fractionBits = type == PrimitiveType.Float16 ? 10 : 7;
        var exponentBits = type == PrimitiveType.Float16 ? 5 : 8;
        var fractionMask = (1 << fractionBits) - 1;
        var exponentMask = ((1 << exponentBits) - 1) << fractionBits;
        var leftRaw = Spill(spill);
        var rightRaw = Spill(spill + 1);
        var leftMagnitude = Spill(spill + 2);
        var rightMagnitude = Spill(spill + 3);
        var leftSign = Spill(spill + 4);
        var rightSign = Spill(spill + 5);
        StoreValue(rightRaw, type);
        var leftNaN = EmitNarrowNaNBranch(leftRaw, fractionMask, exponentMask);
        var rightNaN = EmitNarrowNaNBranch(rightRaw, fractionMask, exponentMask);

        _code.Load(leftRaw);
        _code.Byte(0x25);
        _code.Int32(0x7FFF);
        _code.Store(leftMagnitude);
        _code.Load(rightRaw);
        _code.Byte(0x25);
        _code.Int32(0x7FFF);
        _code.Store(rightMagnitude);
        _code.Load(leftRaw);
        _code.Bytes(0xC1, 0xE8, 0x0F, 0x83, 0xE0, 0x01);
        _code.Store(leftSign);
        _code.Load(rightRaw);
        _code.Bytes(0xC1, 0xE8, 0x0F, 0x83, 0xE0, 0x01);
        _code.Store(rightSign);

        _code.Load(leftMagnitude);
        _code.Bytes(0x85, 0xC0);
        var leftNonzero = _code.JumpIf(0x85);
        _code.Load(rightMagnitude);
        _code.Bytes(0x85, 0xC0);
        var rightNonzero = _code.JumpIf(0x85);
        _code.Bytes(0x31, 0xC0, 0x39, 0xC0);
        EmitNarrowComparisonCondition(operation);
        var equalExit = _code.Jump();

        var compareNonzero = _code.Position;
        _code.PatchRelative(leftNonzero, compareNonzero);
        _code.PatchRelative(rightNonzero, compareNonzero);
        _code.Load(leftSign);
        _code.Bytes(0x89, 0xC1);
        _code.Load(rightSign);
        _code.Bytes(0x31, 0xC8, 0x85, 0xC0);
        var sameSign = _code.JumpIf(0x84);

        if (operation is TokenKind.EqualsEquals or TokenKind.BangEquals)
        {
            _code.Bytes(0x31, 0xC0);
            if (operation == TokenKind.BangEquals)
            {
                _code.Bytes(0xB8);
                _code.Int32(1);
            }
        }
        else
        {
            _code.Load(leftSign);
            _code.Bytes(0x85, 0xC0);
            var leftIsPositive = _code.JumpIf(0x84);
            var negativeResult = operation is TokenKind.Less or TokenKind.LessEquals;
            _code.Bytes(0xB8);
            _code.Int32(negativeResult ? 1 : 0);
            var signDifferenceExit = _code.Jump();
            _code.PatchRelative(leftIsPositive, _code.Position);
            _code.Bytes(0xB8);
            _code.Int32(negativeResult ? 0 : 1);
            _code.PatchRelative(signDifferenceExit, _code.Position);
        }
        var signDifferenceExitToDone = _code.Jump();

        var sameSignLabel = _code.Position;
        _code.PatchRelative(sameSign, sameSignLabel);
        _code.Load(leftSign);
        _code.Bytes(0x85, 0xC0);
        var nonnegative = _code.JumpIf(0x84);
        _code.Load(leftMagnitude);
        _code.Bytes(0x89, 0xC1);
        _code.Load(rightMagnitude);
        _code.Bytes(0x39, 0xC8);
        EmitNarrowComparisonCondition(operation);
        var negativeComparisonExit = _code.Jump();
        _code.PatchRelative(nonnegative, _code.Position);
        _code.Load(rightMagnitude);
        _code.Bytes(0x89, 0xC1);
        _code.Load(leftMagnitude);
        _code.Bytes(0x39, 0xC8);
        EmitNarrowComparisonCondition(operation);
        var positiveComparisonExit = _code.Jump();

        var unorderedLabel = _code.Position;
        _code.PatchRelative(leftNaN, unorderedLabel);
        _code.PatchRelative(rightNaN, unorderedLabel);
        _code.Bytes(0x31, 0xC0);
        if (operation == TokenKind.BangEquals)
        {
            _code.Bytes(0xB8);
            _code.Int32(1);
        }
        var unorderedExit = _code.Jump();

        var done = _code.Position;
        _code.PatchRelative(equalExit, done);
        _code.PatchRelative(signDifferenceExitToDone, done);
        _code.PatchRelative(negativeComparisonExit, done);
        _code.PatchRelative(positiveComparisonExit, done);
        _code.PatchRelative(unorderedExit, done);

        int EmitNarrowNaNBranch(int raw, int mask, int exponent)
        {
            _code.Load(raw);
            _code.Bytes(0x89, 0xC1);
            _code.Byte(0x25);
            _code.Int32(exponent);
            _code.Byte(0x3D);
            _code.Int32(exponent);
            var notSpecial = _code.JumpIf(0x85);
            _code.Bytes(0x89, 0xC8, 0x25);
            _code.Int32(mask);
            _code.Bytes(0x85, 0xC0);
            var isNaN = _code.JumpIf(0x85);
            _code.PatchRelative(notSpecial, _code.Position);
            return isNaN;
        }

        void EmitNarrowComparisonCondition(TokenKind comparison)
        {
            var condition = comparison switch {
                TokenKind.EqualsEquals => (byte)0x94,
                TokenKind.BangEquals => (byte)0x95,
                TokenKind.Less => (byte)0x92,
                TokenKind.LessEquals => (byte)0x96,
                TokenKind.Greater => (byte)0x97,
                TokenKind.GreaterEquals => (byte)0x93,
                _ => throw new InvalidOperationException(),
            };
            _code.Bytes(0x0F, condition, 0xC0, 0x0F, 0xB6, 0xC0);
        }
    }

    private void FloatingNarrowBinary(TokenKind operation, PrimitiveType type, int spill)
    {
        if (IsFloatingComparison(operation))
        {
            FloatingNarrowComparison(operation, type, spill);
            return;
        }
        if (operation is TokenKind.Plus or TokenKind.Minus)
        {
            FloatingNarrowAddOrSubtract(operation, type, spill);
            return;
        }
        if (operation == TokenKind.Star)
        {
            FloatingNarrowMultiply(type, spill);
            return;
        }
        if (operation == TokenKind.Slash)
        {
            FloatingNarrowDivide(type, spill);
            return;
        }
        if (operation == TokenKind.Percent)
        {
            FloatingNarrowRemainder(type, spill);
            return;
        }
        throw new InvalidOperationException("The verifier admitted an unsupported narrow floating operator.");
    }

    private void FloatingNarrowAddOrSubtract(TokenKind operation, PrimitiveType type, int spill)
    {
        var fractionBits = type == PrimitiveType.Float16 ? 10 : 7;
        var exponentBits = type == PrimitiveType.Float16 ? 5 : 8;
        var exponentMask = (1 << exponentBits) - 1;
        var signMask = 1 << (fractionBits + exponentBits);
        var bias = (1 << (exponentBits - 1)) - 1;
        var minimumExponent = 1 - bias;
        var maximumExponent = bias;
        var signA = Spill(spill + 2);
        var exponentA = Spill(spill + 3);
        var significandA = Spill(spill + 4);
        var classA = Spill(spill + 5);
        var signB = Spill(spill + 6);
        var exponentB = Spill(spill + 7);
        var significandB = Spill(spill + 8);
        var classB = Spill(spill + 9);
        var signResult = Spill(spill + 10);
        var exponentResult = Spill(spill + 11);
        var extendedA = Spill(spill + 12);
        var extendedB = Spill(spill + 13);
        var temporary = Spill(spill + 14);
        var result = Spill(spill + 15);

        var rightRaw = Spill(spill + 1);
        StoreValue(rightRaw, type);
        EmitNarrowDecode(type, Spill(spill), signA, exponentA, significandA, classA);
        EmitNarrowDecode(type, rightRaw, signB, exponentB, significandB, classB);
        if (operation == TokenKind.Minus)
        {
            _code.Load(signB);
            _code.Byte(0x35);
            _code.Int32(1);
            _code.Store(signB);
        }

        _code.Load(classA);
        _code.Bytes(0x83, 0xF8, 0x02);
        var leftNaN = _code.JumpIf(0x84);
        _code.Load(classB);
        _code.Bytes(0x83, 0xF8, 0x02);
        var rightNaN = _code.JumpIf(0x84);

        _code.Load(classA);
        _code.Bytes(0x83, 0xF8, 0x01);
        var leftFinite = _code.JumpIf(0x85);
        _code.Load(classB);
        _code.Bytes(0x83, 0xF8, 0x01);
        var rightFinite = _code.JumpIf(0x85);
        _code.Load(signA);
        _code.Bytes(0x89, 0xC1);
        _code.Load(signB);
        _code.Bytes(0x39, 0xC8);
        var sameInfinitySign = _code.JumpIf(0x84);
        EmitNarrowQuietNaN(result, fractionBits, exponentMask);
        var oppositeInfinityExit = _code.Jump();
        _code.PatchRelative(sameInfinitySign, _code.Position);
        EmitNarrowInfinity(result, signA, exponentMask, fractionBits);
        var bothInfinityExit = _code.Jump();

        _code.PatchRelative(rightFinite, _code.Position);
        EmitNarrowInfinity(result, signA, exponentMask, fractionBits);
        var leftInfinityExit = _code.Jump();

        _code.PatchRelative(leftFinite, _code.Position);
        _code.Load(classB);
        _code.Bytes(0x83, 0xF8, 0x01);
        var bothFinite = _code.JumpIf(0x85);
        EmitNarrowInfinity(result, signB, exponentMask, fractionBits);
        var rightInfinityExit = _code.Jump();

        _code.PatchRelative(bothFinite, _code.Position);
        _code.Load(significandA);
        _code.Bytes(0x85, 0xC0);
        var leftNonzero = _code.JumpIf(0x85);
        _code.Load(significandB);
        _code.Bytes(0x85, 0xC0);
        var rightNonzero = _code.JumpIf(0x85);
        _code.Load(signA);
        _code.Bytes(0x89, 0xC1);
        _code.Load(signB);
        _code.Bytes(0x21, 0xC8, 0xC1, 0xE0, 0x0F);
        _code.Store(result);
        var bothZeroExit = _code.Jump();

        var onlyRightNonzero = _code.Position;
        _code.PatchRelative(rightNonzero, onlyRightNonzero);
        _code.Load(rightRaw);
        if (operation == TokenKind.Minus)
        {
            _code.Byte(0x35);
            _code.Int32(signMask);
        }
        _code.Store(result);
        var onlyRightExit = _code.Jump();

        var checkRightZero = _code.Position;
        _code.PatchRelative(leftNonzero, checkRightZero);
        _code.Load(significandB);
        _code.Bytes(0x85, 0xC0);
        var bothNonzero = _code.JumpIf(0x85);
        _code.Load(Spill(spill));
        _code.Store(result);
        var onlyLeftExit = _code.Jump();

        _code.PatchRelative(bothNonzero, _code.Position);
        EmitNarrowOrderOperands(exponentA, significandA, signA,
            exponentB, significandB, signB, temporary);

        _code.Load(exponentA);
        _code.Bytes(0x89, 0xC1);
        _code.Load(exponentB);
        _code.Bytes(0x29, 0xC1, 0x89, 0xC8);
        _code.Store(exponentResult);

        _code.Load(significandA);
        _code.Bytes(0xC1, 0xE0, 0x03);
        _code.Store(extendedA);
        _code.Load(significandB);
        _code.Bytes(0xC1, 0xE0, 0x03);
        _code.Store(temporary);
        EmitNarrowShiftRightJam(temporary, exponentResult, extendedB);
        _code.Load(exponentA);
        _code.Store(exponentResult);

        _code.Load(signA);
        _code.Bytes(0x89, 0xC1);
        _code.Load(signB);
        _code.Bytes(0x39, 0xC8);
        var subtractMagnitudes = _code.JumpIf(0x85);
        _code.Load(extendedA);
        _code.Bytes(0x89, 0xC1);
        _code.Load(extendedB);
        _code.Bytes(0x01, 0xC8);
        _code.Store(extendedA);
        var carryThreshold = 1 << (fractionBits + 4);
        _code.Byte(0x3D);
        _code.Int32(carryThreshold);
        var noCarry = _code.JumpIf(0x82);
        _code.Load(extendedA);
        _code.Bytes(0x89, 0xC1);
        _code.Bytes(0x83, 0xE1, 0x01, 0xD1, 0xE8, 0x09, 0xC8);
        _code.Store(extendedA);
        _code.Load(exponentA);
        _code.Bytes(0x83, 0xC0, 0x01);
        _code.Store(exponentResult);
        var additionReady = _code.Position;
        _code.PatchRelative(noCarry, additionReady);
        _code.Load(signA);
        _code.Store(signResult);
        EmitNarrowRoundPack(type, signResult, exponentResult, extendedA, temporary,
            result, fractionBits, bias, minimumExponent, maximumExponent);
        var additionExit = _code.Jump();

        _code.PatchRelative(subtractMagnitudes, _code.Position);
        _code.Load(extendedB);
        _code.Bytes(0x89, 0xC1);
        _code.Load(extendedA);
        _code.Bytes(0x29, 0xC8);
        _code.Store(extendedA);
        _code.Bytes(0x85, 0xC0);
        var nonzeroDifference = _code.JumpIf(0x85);
        _code.Bytes(0x31, 0xC0);
        _code.Store(result);
        var zeroDifferenceExit = _code.Jump();

        _code.PatchRelative(nonzeroDifference, _code.Position);
        _code.Load(signA);
        _code.Store(signResult);
        _code.Load(exponentA);
        _code.Store(exponentResult);
        var normalize = _code.Position;
        _code.Load(extendedA);
        _code.Byte(0x3D);
        _code.Int32(1 << (fractionBits + 3));
        var normalized = _code.JumpIf(0x83);
        _code.Load(exponentResult);
        _code.Bytes(0x83, 0xF8, unchecked((byte)minimumExponent));
        var atMinimum = _code.JumpIf(0x8E);
        _code.Load(extendedA);
        _code.Bytes(0xD1, 0xE0);
        _code.Store(extendedA);
        _code.Load(exponentResult);
        _code.Bytes(0x83, 0xE8, 0x01);
        _code.Store(exponentResult);
        var normalizeAgain = _code.Jump();
        _code.PatchRelative(normalizeAgain, normalize);
        var packDifference = _code.Position;
        _code.PatchRelative(normalized, packDifference);
        _code.PatchRelative(atMinimum, packDifference);
        EmitNarrowRoundPack(type, signResult, exponentResult, extendedA, temporary,
            result, fractionBits, bias, minimumExponent, maximumExponent);
        var subtractionExit = _code.Jump();

        var nanLabel = _code.Position;
        _code.PatchRelative(leftNaN, nanLabel);
        _code.PatchRelative(rightNaN, nanLabel);
        EmitNarrowQuietNaN(result, fractionBits, exponentMask);
        var nanExit = _code.Jump();

        var done = _code.Position;
        _code.PatchRelative(oppositeInfinityExit, done);
        _code.PatchRelative(bothInfinityExit, done);
        _code.PatchRelative(leftInfinityExit, done);
        _code.PatchRelative(rightInfinityExit, done);
        _code.PatchRelative(bothZeroExit, done);
        _code.PatchRelative(onlyRightExit, done);
        _code.PatchRelative(onlyLeftExit, done);
        _code.PatchRelative(additionExit, done);
        _code.PatchRelative(zeroDifferenceExit, done);
        _code.PatchRelative(subtractionExit, done);
        _code.PatchRelative(nanExit, done);
        _code.Load(result);
    }

    private void FloatingNarrowRemainder(PrimitiveType type, int spill)
    {
        var fractionBits = type == PrimitiveType.Float16 ? 10 : 7;
        var exponentBits = type == PrimitiveType.Float16 ? 5 : 8;
        var exponentMask = (1 << exponentBits) - 1;
        var bias = (1 << (exponentBits - 1)) - 1;
        var minimumExponent = 1 - bias;
        var signA = Spill(spill + 2);
        var exponentA = Spill(spill + 3);
        var significandA = Spill(spill + 4);
        var classA = Spill(spill + 5);
        var signB = Spill(spill + 6);
        var exponentB = Spill(spill + 7);
        var significandB = Spill(spill + 8);
        var classB = Spill(spill + 9);
        var signResult = Spill(spill + 10);
        var exponentResult = Spill(spill + 11);
        var extendedA = Spill(spill + 12);
        var temporary = Spill(spill + 14);
        var result = Spill(spill + 15);

        var rightRaw = Spill(spill + 1);
        StoreValue(rightRaw, type);
        EmitNarrowDecode(type, Spill(spill), signA, exponentA, significandA, classA);
        EmitNarrowDecode(type, rightRaw, signB, exponentB, significandB, classB);

        _code.Load(classA);
        _code.Bytes(0x83, 0xF8, 0x02);
        var leftNaN = _code.JumpIf(0x84);
        _code.Load(classB);
        _code.Bytes(0x83, 0xF8, 0x02);
        var rightNaN = _code.JumpIf(0x84);
        _code.Load(classA);
        _code.Bytes(0x83, 0xF8, 0x01);
        var leftFinite = _code.JumpIf(0x85);
        EmitNarrowQuietNaN(result, fractionBits, exponentMask);
        var infiniteDividendExit = _code.Jump();

        _code.PatchRelative(leftFinite, _code.Position);
        _code.Load(classB);
        _code.Bytes(0x83, 0xF8, 0x01);
        var rightFinite = _code.JumpIf(0x85);
        _code.Load(Spill(spill));
        _code.Store(result);
        var infiniteDivisorExit = _code.Jump();

        _code.PatchRelative(rightFinite, _code.Position);
        _code.Load(significandB);
        _code.Bytes(0x85, 0xC0);
        var nonzeroDivisor = _code.JumpIf(0x85);
        EmitNarrowQuietNaN(result, fractionBits, exponentMask);
        var zeroDivisorExit = _code.Jump();

        _code.PatchRelative(nonzeroDivisor, _code.Position);
        _code.Load(significandA);
        _code.Bytes(0x85, 0xC0);
        var nonzeroDividend = _code.JumpIf(0x85);
        _code.Load(Spill(spill));
        _code.Store(result);
        var zeroDividendExit = _code.Jump();

        _code.PatchRelative(nonzeroDividend, _code.Position);
        _code.Load(exponentA);
        _code.Bytes(0x89, 0xC1);
        _code.Load(exponentB);
        _code.Bytes(0x39, 0xC1);
        var exponentLess = _code.JumpIf(0x8C);
        var exponentGreater = _code.JumpIf(0x8F);
        _code.Load(significandA);
        _code.Bytes(0x89, 0xC1);
        _code.Load(significandB);
        _code.Bytes(0x39, 0xC1);
        var significandLess = _code.JumpIf(0x82);

        var computeRemainder = _code.Position;
        _code.Load(exponentA);
        _code.Bytes(0x89, 0xC1);
        _code.Load(exponentB);
        _code.Bytes(0x29, 0xC1, 0x89, 0xC8);
        _code.Store(exponentResult);
        _code.Load(significandB);
        _code.Bytes(0x89, 0xC1);
        _code.Load(significandA);
        _code.Bytes(0x31, 0xD2, 0xF7, 0xF1, 0x89, 0xD0);
        _code.Store(extendedA);
        var reduce = _code.Position;
        _code.Load(exponentResult);
        _code.Bytes(0x85, 0xC0);
        var reductionComplete = _code.JumpIf(0x84);
        _code.Load(extendedA);
        _code.Bytes(0xD1, 0xE0);
        _code.Store(temporary);
        _code.Load(significandB);
        _code.Bytes(0x89, 0xC1);
        _code.Load(temporary);
        _code.Bytes(0x31, 0xD2, 0xF7, 0xF1, 0x89, 0xD0);
        _code.Store(extendedA);
        _code.Load(exponentResult);
        _code.Bytes(0x83, 0xE8, 0x01);
        _code.Store(exponentResult);
        var nextReduction = _code.Jump();
        _code.PatchRelative(nextReduction, reduce);
        var finishReduction = _code.Position;
        _code.PatchRelative(reductionComplete, finishReduction);

        _code.Load(exponentB);
        _code.Store(exponentResult);
        _code.Load(signA);
        _code.Store(signResult);
        _code.Load(extendedA);
        _code.Bytes(0x85, 0xC0);
        var nonzeroRemainder = _code.JumpIf(0x85);
        _code.Load(signResult);
        _code.Bytes(0xC1, 0xE0, 0x0F);
        _code.Store(result);
        var zeroRemainderExit = _code.Jump();

        _code.PatchRelative(nonzeroRemainder, _code.Position);
        _code.Load(extendedA);
        _code.Bytes(0xC1, 0xE0, 0x03);
        _code.Store(extendedA);
        var normalize = _code.Position;
        _code.Load(extendedA);
        _code.Byte(0x3D);
        _code.Int32(1 << (fractionBits + 3));
        var normalized = _code.JumpIf(0x83);
        _code.Load(exponentResult);
        _code.Bytes(0x83, 0xF8, unchecked((byte)minimumExponent));
        var atMinimum = _code.JumpIf(0x8E);
        _code.Load(extendedA);
        _code.Bytes(0xD1, 0xE0);
        _code.Store(extendedA);
        _code.Load(exponentResult);
        _code.Bytes(0x83, 0xE8, 0x01);
        _code.Store(exponentResult);
        var normalizeAgain = _code.Jump();
        _code.PatchRelative(normalizeAgain, normalize);
        var pack = _code.Position;
        _code.PatchRelative(normalized, pack);
        _code.PatchRelative(atMinimum, pack);
        EmitNarrowRoundPack(type, signResult, exponentResult, extendedA, temporary,
            result, fractionBits, bias, minimumExponent,
            type == PrimitiveType.Float16 ? 15 : 127);
        var finiteRemainderExit = _code.Jump();

        var copyDividend = _code.Position;
        _code.PatchRelative(exponentLess, copyDividend);
        _code.PatchRelative(significandLess, copyDividend);
        _code.Load(Spill(spill));
        _code.Store(result);
        var dividendExit = _code.Jump();

        var nanLabel = _code.Position;
        _code.PatchRelative(leftNaN, nanLabel);
        _code.PatchRelative(rightNaN, nanLabel);
        EmitNarrowQuietNaN(result, fractionBits, exponentMask);
        var nanExit = _code.Jump();

        var done = _code.Position;
        _code.PatchRelative(exponentGreater, computeRemainder);
        _code.PatchRelative(infiniteDividendExit, done);
        _code.PatchRelative(infiniteDivisorExit, done);
        _code.PatchRelative(zeroDivisorExit, done);
        _code.PatchRelative(zeroDividendExit, done);
        _code.PatchRelative(zeroRemainderExit, done);
        _code.PatchRelative(finiteRemainderExit, done);
        _code.PatchRelative(dividendExit, done);
        _code.PatchRelative(nanExit, done);
        _code.Load(result);
    }

    private void FloatingNarrowMultiply(PrimitiveType type, int spill)
    {
        var fractionBits = type == PrimitiveType.Float16 ? 10 : 7;
        var exponentBits = type == PrimitiveType.Float16 ? 5 : 8;
        var exponentMask = (1 << exponentBits) - 1;
        var bias = (1 << (exponentBits - 1)) - 1;
        var minimumExponent = 1 - bias;
        var maximumExponent = bias;
        var signA = Spill(spill + 2);
        var exponentA = Spill(spill + 3);
        var significandA = Spill(spill + 4);
        var classA = Spill(spill + 5);
        var signB = Spill(spill + 6);
        var exponentB = Spill(spill + 7);
        var significandB = Spill(spill + 8);
        var classB = Spill(spill + 9);
        var signResult = Spill(spill + 10);
        var exponentResult = Spill(spill + 11);
        var extendedA = Spill(spill + 12);
        var extendedB = Spill(spill + 13);
        var temporary = Spill(spill + 14);
        var result = Spill(spill + 15);

        var rightRaw = Spill(spill + 1);
        StoreValue(rightRaw, type);
        EmitNarrowDecode(type, Spill(spill), signA, exponentA, significandA, classA);
        EmitNarrowDecode(type, rightRaw, signB, exponentB, significandB, classB);
        _code.Load(signA);
        _code.Bytes(0x89, 0xC1);
        _code.Load(signB);
        _code.Bytes(0x31, 0xC8);
        _code.Store(signResult);

        _code.Load(classA);
        _code.Bytes(0x83, 0xF8, 0x02);
        var leftNaN = _code.JumpIf(0x84);
        _code.Load(classB);
        _code.Bytes(0x83, 0xF8, 0x02);
        var rightNaN = _code.JumpIf(0x84);

        _code.Load(classA);
        _code.Bytes(0x83, 0xF8, 0x01);
        var leftFinite = _code.JumpIf(0x85);
        _code.Load(classB);
        _code.Bytes(0x83, 0xF8, 0x01);
        var rightFinite = _code.JumpIf(0x85);
        EmitNarrowInfinity(result, signResult, exponentMask, fractionBits);
        var bothInfinityExit = _code.Jump();

        _code.PatchRelative(rightFinite, _code.Position);
        _code.Load(significandB);
        _code.Bytes(0x85, 0xC0);
        var leftInfinityNonzero = _code.JumpIf(0x85);
        EmitNarrowQuietNaN(result, fractionBits, exponentMask);
        var leftInfinityZeroExit = _code.Jump();
        _code.PatchRelative(leftInfinityNonzero, _code.Position);
        EmitNarrowInfinity(result, signResult, exponentMask, fractionBits);
        var leftInfinityExit = _code.Jump();

        _code.PatchRelative(leftFinite, _code.Position);
        _code.Load(classB);
        _code.Bytes(0x83, 0xF8, 0x01);
        var bothFinite = _code.JumpIf(0x85);
        _code.Load(significandA);
        _code.Bytes(0x85, 0xC0);
        var rightInfinityNonzero = _code.JumpIf(0x85);
        EmitNarrowQuietNaN(result, fractionBits, exponentMask);
        var rightInfinityZeroExit = _code.Jump();
        _code.PatchRelative(rightInfinityNonzero, _code.Position);
        EmitNarrowInfinity(result, signResult, exponentMask, fractionBits);
        var rightInfinityExit = _code.Jump();

        _code.PatchRelative(bothFinite, _code.Position);
        _code.Load(significandA);
        _code.Bytes(0x85, 0xC0);
        var leftZero = _code.JumpIf(0x84);
        _code.Load(significandB);
        _code.Bytes(0x85, 0xC0);
        var rightZero = _code.JumpIf(0x84);

        _code.Load(exponentA);
        _code.Bytes(0x89, 0xC1);
        _code.Load(exponentB);
        _code.Bytes(0x01, 0xC8);
        _code.Store(exponentResult);
        _code.Load(significandA);
        _code.Bytes(0x89, 0xC1);
        _code.Load(significandB);
        _code.Bytes(0x0F, 0xAF, 0xC1);
        _code.Store(temporary);
        var highProductThreshold = 1 << ((2 * fractionBits) + 1);
        _code.Byte(0x3D);
        _code.Int32(highProductThreshold);
        var lowProduct = _code.JumpIf(0x82);
        _code.Byte(0xB8);
        _code.Int32(fractionBits - 2);
        _code.Store(extendedB);
        _code.Load(exponentResult);
        _code.Bytes(0x83, 0xC0, 0x01);
        _code.Store(exponentResult);
        var productShiftReady = _code.Jump();
        _code.PatchRelative(lowProduct, _code.Position);
        _code.Byte(0xB8);
        _code.Int32(fractionBits - 3);
        _code.Store(extendedB);
        _code.PatchRelative(productShiftReady, _code.Position);
        EmitNarrowShiftRightJam(temporary, extendedB, extendedA);
        EmitNarrowRoundPack(type, signResult, exponentResult, extendedA, temporary,
            result, fractionBits, bias, minimumExponent, maximumExponent);
        var multiplyExit = _code.Jump();

        var zeroLabel = _code.Position;
        _code.PatchRelative(leftZero, zeroLabel);
        _code.PatchRelative(rightZero, zeroLabel);
        _code.Load(signResult);
        _code.Bytes(0xC1, 0xE0, 0x0F);
        _code.Store(result);
        var zeroExit = _code.Jump();

        var nanLabel = _code.Position;
        _code.PatchRelative(leftNaN, nanLabel);
        _code.PatchRelative(rightNaN, nanLabel);
        EmitNarrowQuietNaN(result, fractionBits, exponentMask);
        var nanExit = _code.Jump();

        var done = _code.Position;
        _code.PatchRelative(bothInfinityExit, done);
        _code.PatchRelative(leftInfinityZeroExit, done);
        _code.PatchRelative(leftInfinityExit, done);
        _code.PatchRelative(rightInfinityZeroExit, done);
        _code.PatchRelative(rightInfinityExit, done);
        _code.PatchRelative(multiplyExit, done);
        _code.PatchRelative(zeroExit, done);
        _code.PatchRelative(nanExit, done);
        _code.Load(result);
    }

    private void FloatingNarrowDivide(PrimitiveType type, int spill)
    {
        var fractionBits = type == PrimitiveType.Float16 ? 10 : 7;
        var exponentBits = type == PrimitiveType.Float16 ? 5 : 8;
        var exponentMask = (1 << exponentBits) - 1;
        var bias = (1 << (exponentBits - 1)) - 1;
        var minimumExponent = 1 - bias;
        var maximumExponent = bias;
        var signA = Spill(spill + 2);
        var exponentA = Spill(spill + 3);
        var significandA = Spill(spill + 4);
        var classA = Spill(spill + 5);
        var signB = Spill(spill + 6);
        var exponentB = Spill(spill + 7);
        var significandB = Spill(spill + 8);
        var classB = Spill(spill + 9);
        var signResult = Spill(spill + 10);
        var exponentResult = Spill(spill + 11);
        var extendedA = Spill(spill + 12);
        var shift = Spill(spill + 13);
        var temporary = Spill(spill + 14);
        var result = Spill(spill + 15);

        var rightRaw = Spill(spill + 1);
        StoreValue(rightRaw, type);
        EmitNarrowDecode(type, Spill(spill), signA, exponentA, significandA, classA);
        EmitNarrowDecode(type, rightRaw, signB, exponentB, significandB, classB);
        _code.Load(signA);
        _code.Bytes(0x89, 0xC1);
        _code.Load(signB);
        _code.Bytes(0x31, 0xC8);
        _code.Store(signResult);

        _code.Load(classA);
        _code.Bytes(0x83, 0xF8, 0x02);
        var leftNaN = _code.JumpIf(0x84);
        _code.Load(classB);
        _code.Bytes(0x83, 0xF8, 0x02);
        var rightNaN = _code.JumpIf(0x84);

        _code.Load(classA);
        _code.Bytes(0x83, 0xF8, 0x01);
        var leftFinite = _code.JumpIf(0x85);
        _code.Load(classB);
        _code.Bytes(0x83, 0xF8, 0x01);
        var bothInfinity = _code.JumpIf(0x84);
        EmitNarrowInfinity(result, signResult, exponentMask, fractionBits);
        var infiniteNumeratorExit = _code.Jump();

        _code.PatchRelative(bothInfinity, _code.Position);
        EmitNarrowQuietNaN(result, fractionBits, exponentMask);
        var infinityPairExit = _code.Jump();

        _code.PatchRelative(leftFinite, _code.Position);
        _code.Load(classB);
        _code.Bytes(0x83, 0xF8, 0x01);
        var bothFinite = _code.JumpIf(0x85);
        _code.Load(signResult);
        _code.Bytes(0xC1, 0xE0, 0x0F);
        _code.Store(result);
        var infiniteDenominatorExit = _code.Jump();

        _code.PatchRelative(bothFinite, _code.Position);
        _code.Load(significandB);
        _code.Bytes(0x85, 0xC0);
        var divisorNonzero = _code.JumpIf(0x85);
        _code.Load(significandA);
        _code.Bytes(0x85, 0xC0);
        var zeroNumerator = _code.JumpIf(0x84);
        EmitNarrowInfinity(result, signResult, exponentMask, fractionBits);
        var zeroDivisorExit = _code.Jump();

        _code.PatchRelative(zeroNumerator, _code.Position);
        EmitNarrowQuietNaN(result, fractionBits, exponentMask);
        var zeroOverZeroExit = _code.Jump();

        _code.PatchRelative(divisorNonzero, _code.Position);
        _code.Load(significandA);
        _code.Bytes(0x85, 0xC0);
        var nonzeroNumerator = _code.JumpIf(0x85);
        _code.Load(signResult);
        _code.Bytes(0xC1, 0xE0, 0x0F);
        _code.Store(result);
        var zeroQuotientExit = _code.Jump();

        _code.PatchRelative(nonzeroNumerator, _code.Position);
        _code.Load(significandA);
        _code.Bytes(0x89, 0xC1);
        _code.Load(significandB);
        _code.Bytes(0x39, 0xC1);
        var numeratorBelowOne = _code.JumpIf(0x82);

        _code.Byte(0xB8);
        _code.Int32(fractionBits + 3);
        _code.Store(shift);
        _code.Load(exponentA);
        _code.Bytes(0x89, 0xC1);
        _code.Load(exponentB);
        _code.Bytes(0x29, 0xC1, 0x89, 0xC8);
        _code.Store(exponentResult);
        var normalizedRatio = _code.Jump();

        _code.PatchRelative(numeratorBelowOne, _code.Position);
        _code.Byte(0xB8);
        _code.Int32(fractionBits + 4);
        _code.Store(shift);
        _code.Load(exponentA);
        _code.Bytes(0x89, 0xC1);
        _code.Load(exponentB);
        _code.Bytes(0x29, 0xC1, 0x83, 0xE9, 0x01, 0x89, 0xC8);
        _code.Store(exponentResult);
        _code.PatchRelative(normalizedRatio, _code.Position);

        _code.Load(shift);
        _code.Bytes(0x89, 0xC1);
        _code.Load(significandA);
        _code.Bytes(0xD3, 0xE0);
        _code.Store(temporary);
        _code.Load(significandB);
        _code.Bytes(0x89, 0xC1);
        _code.Load(temporary);
        _code.Bytes(0x31, 0xD2, 0xF7, 0xF1);
        _code.Bytes(0x85, 0xD2);
        var exactQuotient = _code.JumpIf(0x84);
        _code.Bytes(0x83, 0xC8, 0x01);
        var quotientReady = _code.Position;
        _code.PatchRelative(exactQuotient, quotientReady);
        _code.Store(extendedA);
        EmitNarrowRoundPack(type, signResult, exponentResult, extendedA, temporary,
            result, fractionBits, bias, minimumExponent, maximumExponent);
        var divisionExit = _code.Jump();

        var nanLabel = _code.Position;
        _code.PatchRelative(leftNaN, nanLabel);
        _code.PatchRelative(rightNaN, nanLabel);
        EmitNarrowQuietNaN(result, fractionBits, exponentMask);
        var nanExit = _code.Jump();

        var done = _code.Position;
        _code.PatchRelative(infiniteNumeratorExit, done);
        _code.PatchRelative(infinityPairExit, done);
        _code.PatchRelative(infiniteDenominatorExit, done);
        _code.PatchRelative(zeroDivisorExit, done);
        _code.PatchRelative(zeroOverZeroExit, done);
        _code.PatchRelative(zeroQuotientExit, done);
        _code.PatchRelative(divisionExit, done);
        _code.PatchRelative(nanExit, done);
        _code.Load(result);
    }

    private void EmitNarrowDecode(PrimitiveType type, int raw, int sign,
        int exponent, int significand, int valueClass)
    {
        var fractionBits = type == PrimitiveType.Float16 ? 10 : 7;
        var exponentBits = type == PrimitiveType.Float16 ? 5 : 8;
        var exponentMask = (1 << exponentBits) - 1;
        var fractionMask = (1 << fractionBits) - 1;
        var bias = (1 << (exponentBits - 1)) - 1;
        var minimumExponent = 1 - bias;

        _code.Load(raw);
        _code.Bytes(0xC1, 0xE8, 0x0F, 0x83, 0xE0, 0x01);
        _code.Store(sign);
        _code.Load(raw);
        _code.Bytes(0xC1, 0xE8, checked((byte)fractionBits));
        _code.Byte(0x25);
        _code.Int32(exponentMask);
        _code.Store(exponent);
        _code.Load(raw);
        _code.Byte(0x25);
        _code.Int32(fractionMask);
        _code.Store(significand);
        _code.Bytes(0x31, 0xC0);
        _code.Store(valueClass);

        _code.Load(exponent);
        _code.Bytes(0x85, 0xC0);
        var nonzeroExponent = _code.JumpIf(0x85);
        _code.Byte(0xB8);
        _code.Int32(minimumExponent);
        _code.Store(exponent);
        var subnormalDone = _code.Jump();

        _code.PatchRelative(nonzeroExponent, _code.Position);
        _code.Load(exponent);
        _code.Byte(0x3D);
        _code.Int32(exponentMask);
        var normal = _code.JumpIf(0x85);
        _code.Load(significand);
        _code.Bytes(0x85, 0xC0);
        var zeroFraction = _code.JumpIf(0x84);
        _code.Byte(0xB8);
        _code.Int32(2);
        _code.Store(valueClass);
        var nanDone = _code.Jump();

        _code.PatchRelative(zeroFraction, _code.Position);
        _code.Byte(0xB8);
        _code.Int32(1);
        _code.Store(valueClass);
        var infinityDone = _code.Jump();

        _code.PatchRelative(normal, _code.Position);
        _code.Load(exponent);
        _code.Byte(0x2D);
        _code.Int32(bias);
        _code.Store(exponent);
        _code.Load(significand);
        _code.Byte(0x0D);
        _code.Int32(1 << fractionBits);
        _code.Store(significand);

        var done = _code.Position;
        _code.PatchRelative(subnormalDone, done);
        _code.PatchRelative(nanDone, done);
        _code.PatchRelative(infinityDone, done);
    }

    private void EmitNarrowOrderOperands(int exponentA, int significandA, int signA,
        int exponentB, int significandB, int signB, int temporary)
    {
        _code.Load(exponentA);
        _code.Bytes(0x89, 0xC1);
        _code.Load(exponentB);
        _code.Bytes(0x39, 0xC1);
        var exponentLess = _code.JumpIf(0x8C);
        var exponentGreater = _code.JumpIf(0x8F);
        _code.Load(significandA);
        _code.Bytes(0x89, 0xC1);
        _code.Load(significandB);
        _code.Bytes(0x39, 0xC1);
        var significandLess = _code.JumpIf(0x82);
        var alreadyOrdered = _code.Jump();

        var swap = _code.Position;
        Swap(signA, signB);
        Swap(exponentA, exponentB);
        Swap(significandA, significandB);
        var done = _code.Position;
        _code.PatchRelative(exponentLess, swap);
        _code.PatchRelative(significandLess, swap);
        _code.PatchRelative(exponentGreater, done);
        _code.PatchRelative(alreadyOrdered, done);

        void Swap(int first, int second)
        {
            _code.Load(first);
            _code.Store(temporary);
            _code.Load(second);
            _code.Store(first);
            _code.Load(temporary);
            _code.Store(second);
        }
    }

    private void EmitNarrowShiftRightJam(int value, int shift, int result)
    {
        _code.Load(shift);
        _code.Bytes(0x85, 0xC0);
        var nonzeroShift = _code.JumpIf(0x85);
        _code.Load(value);
        _code.Store(result);
        var zeroShiftDone = _code.Jump();

        _code.PatchRelative(nonzeroShift, _code.Position);
        _code.Load(shift);
        _code.Bytes(0x83, 0xF8, 0x20);
        var smallShift = _code.JumpIf(0x8C);
        _code.Load(value);
        _code.Bytes(0x85, 0xC0);
        var zeroValue = _code.JumpIf(0x84);
        _code.Byte(0xB8);
        _code.Int32(1);
        _code.Store(result);
        var largeShiftDone = _code.Jump();
        _code.PatchRelative(zeroValue, _code.Position);
        _code.Bytes(0x31, 0xC0);
        _code.Store(result);
        var largeZeroDone = _code.Jump();

        _code.PatchRelative(smallShift, _code.Position);
        _code.Load(shift);
        _code.Bytes(0x89, 0xC1);
        _code.Load(value);
        _code.Bytes(0xD3, 0xE8);
        _code.Store(result);
        _code.Load(shift);
        _code.Bytes(0x89, 0xC1, 0xB8);
        _code.Int32(32);
        _code.Bytes(0x29, 0xC8, 0x89, 0xC1);
        _code.Load(value);
        _code.Bytes(0xD3, 0xE0, 0x85, 0xC0);
        var noSticky = _code.JumpIf(0x84);
        _code.Load(result);
        _code.Bytes(0x83, 0xC8, 0x01);
        _code.Store(result);
        var stickyDone = _code.Position;
        _code.PatchRelative(noSticky, stickyDone);

        var done = _code.Position;
        _code.PatchRelative(zeroShiftDone, done);
        _code.PatchRelative(largeShiftDone, done);
        _code.PatchRelative(largeZeroDone, done);
    }

    private void EmitNarrowRoundPack(PrimitiveType type, int sign, int exponent,
        int extended, int temporary, int result, int fractionBits, int bias,
        int minimumExponent, int maximumExponent)
    {
        var hidden = 1 << fractionBits;
        var overflowSignificand = hidden << 1;
        var fractionMask = hidden - 1;
        var exponentMask = type == PrimitiveType.Float16 ? 0x1F : 0xFF;

        _code.Load(exponent);
        _code.Bytes(0x83, 0xF8, unchecked((byte)minimumExponent));
        var noUnderflow = _code.JumpIf(0x8D);
        _code.Load(exponent);
        _code.Bytes(0x89, 0xC1);
        _code.Byte(0xB8);
        _code.Int32(minimumExponent);
        _code.Bytes(0x29, 0xC8);
        _code.Store(temporary);
        EmitNarrowShiftRightJam(extended, temporary, result);
        _code.Load(result);
        _code.Store(extended);
        _code.Byte(0xB8);
        _code.Int32(minimumExponent);
        _code.Store(exponent);
        _code.PatchRelative(noUnderflow, _code.Position);

        _code.Load(extended);
        _code.Bytes(0x83, 0xE0, 0x07);
        _code.Store(temporary);
        _code.Load(extended);
        _code.Bytes(0xC1, 0xE8, 0x03);
        _code.Store(result);

        _code.Load(temporary);
        _code.Bytes(0x83, 0xF8, 0x04);
        var roundGreater = _code.JumpIf(0x8F);
        var roundLess = _code.JumpIf(0x8C);
        _code.Load(result);
        _code.Bytes(0xA9, 0x01, 0x00, 0x00, 0x00);
        var roundEven = _code.JumpIf(0x84);
        var increment = _code.Position;
        _code.PatchRelative(roundGreater, increment);
        _code.Load(result);
        _code.Bytes(0x83, 0xC0, 0x01);
        _code.Store(result);
        var incrementDone = _code.Jump();
        var noIncrement = _code.Position;
        _code.PatchRelative(roundLess, noIncrement);
        _code.PatchRelative(roundEven, noIncrement);
        _code.PatchRelative(incrementDone, _code.Position);

        _code.Load(result);
        _code.Byte(0x3D);
        _code.Int32(overflowSignificand);
        var noSignificandCarry = _code.JumpIf(0x82);
        _code.Load(result);
        _code.Bytes(0xD1, 0xE8);
        _code.Store(result);
        _code.Load(exponent);
        _code.Bytes(0x83, 0xC0, 0x01);
        _code.Store(exponent);
        var carryDone = _code.Jump();
        _code.PatchRelative(noSignificandCarry, _code.Position);
        _code.PatchRelative(carryDone, _code.Position);
        _code.Load(result);
        _code.Store(temporary);

        _code.Load(exponent);
        _code.Bytes(0x83, 0xF8, checked((byte)maximumExponent));
        var finite = _code.JumpIf(0x8E);
        EmitNarrowInfinity(result, sign, exponentMask, fractionBits);
        var overflowDone = _code.Jump();

        _code.PatchRelative(finite, _code.Position);
        _code.Load(temporary);
        _code.Bytes(0x85, 0xC0);
        var nonzero = _code.JumpIf(0x85);
        _code.Load(sign);
        _code.Bytes(0xC1, 0xE0, 0x0F);
        _code.Store(result);
        var zeroDone = _code.Jump();

        _code.PatchRelative(nonzero, _code.Position);
        _code.Load(exponent);
        _code.Bytes(0x83, 0xF8, unchecked((byte)minimumExponent));
        var aboveMinimum = _code.JumpIf(0x8F);
        _code.Load(result);
        _code.Byte(0x3D);
        _code.Int32(hidden);
        var normalAtMinimum = _code.JumpIf(0x83);

        _code.Load(sign);
        _code.Bytes(0xC1, 0xE0, 0x0F);
        _code.Store(result);
        _code.Load(temporary);
        _code.Byte(0x25);
        _code.Int32(fractionMask);
        _code.Bytes(0x89, 0xC1);
        _code.Load(result);
        _code.Bytes(0x09, 0xC8);
        _code.Store(result);
        var subnormalDone = _code.Jump();

        var normal = _code.Position;
        _code.PatchRelative(aboveMinimum, normal);
        _code.PatchRelative(normalAtMinimum, normal);
        _code.Load(sign);
        _code.Bytes(0xC1, 0xE0, 0x0F);
        _code.Store(result);
        _code.Load(exponent);
        _code.Byte(0x05);
        _code.Int32(bias);
        _code.Bytes(0xC1, 0xE0, checked((byte)fractionBits), 0x89, 0xC1);
        _code.Load(result);
        _code.Bytes(0x09, 0xC8);
        _code.Store(result);
        _code.Load(temporary);
        _code.Byte(0x25);
        _code.Int32(fractionMask);
        _code.Bytes(0x89, 0xC1);
        _code.Load(result);
        _code.Bytes(0x09, 0xC8);
        _code.Store(result);

        var done = _code.Position;
        _code.PatchRelative(overflowDone, done);
        _code.PatchRelative(zeroDone, done);
        _code.PatchRelative(subnormalDone, done);
    }

    private void EmitNarrowQuietNaN(int result, int fractionBits, int exponentMask)
    {
        _code.Byte(0xB8);
        _code.Int32((exponentMask << fractionBits) | (1 << (fractionBits - 1)));
        _code.Store(result);
    }

    private void EmitNarrowInfinity(int result, int sign, int exponentMask, int fractionBits)
    {
        _code.Load(sign);
        _code.Bytes(0xC1, 0xE0, 0x0F);
        _code.Byte(0x0D);
        _code.Int32(exponentMask << fractionBits);
        _code.Store(result);
    }

    private void CastPrimitive(PrimitiveType source, PrimitiveType target, int savedMxcsr,
        int controlledMxcsr, int resultSpill)
    {
        if (source is PrimitiveType.Float32 or PrimitiveType.Float64
            && target is PrimitiveType.Int32 or PrimitiveType.UInt32 or PrimitiveType.Int64 or PrimitiveType.UInt64)
        {
            EmitFloatingToInteger(source, target, savedMxcsr, controlledMxcsr);
            return;
        }
        if (target is PrimitiveType.Float32 or PrimitiveType.Float64
            && (PrimitiveTypeFacts.IsInteger(source) || source == PrimitiveType.Char16))
        {
            if (source == PrimitiveType.UInt64)
            {
                EmitUInt64ToFloating(target);
            }
            else
            {
                if (source == PrimitiveType.Char16)
                {
                    _code.Bytes(0x0F, 0xB7, 0xC0); // movzx eax,ax
                }
                EmitIntegerToFloating(source, target);
            }
            return;
        }
        if ((source is PrimitiveType.Char16 or PrimitiveType.Int8 or PrimitiveType.Int16
                or PrimitiveType.Int32 or PrimitiveType.Int64 or PrimitiveType.UInt8
                or PrimitiveType.UInt16 or PrimitiveType.UInt32 or PrimitiveType.UInt64)
            && (target is PrimitiveType.Int128 or PrimitiveType.UInt128))
        {
            EmitIntegerTo128(source, target, resultSpill);
            return;
        }
        if ((PrimitiveTypeFacts.IsInteger(source) || source == PrimitiveType.Char16)
            && (PrimitiveTypeFacts.IsInteger(target) || target == PrimitiveType.Char16))
        {
            EmitCheckedIntegerConversion(source, target);
            return;
        }
        if (source == target)
        {
            return;
        }
        if (source == PrimitiveType.Float32 && target == PrimitiveType.Float64)
        {
            _code.Bytes(0x66, 0x0F, 0x6E, 0xC0); // movd xmm0,eax
            _code.Bytes(0xF3, 0x0F, 0x5A, 0xC0); // cvtss2sd xmm0,xmm0
            _code.Bytes(0x66, 0x48, 0x0F, 0x7E, 0xC0); // movq rax,xmm0
            return;
        }
        if (source == PrimitiveType.Float64 && target == PrimitiveType.Float32)
        {
            _code.Bytes(0x66, 0x48, 0x0F, 0x6E, 0xC0); // movq xmm0,rax
            _code.Bytes(0xF2, 0x0F, 0x5A, 0xC0); // cvtsd2ss xmm0,xmm0
            _code.Bytes(0x66, 0x0F, 0x7E, 0xC0); // movd eax,xmm0
            return;
        }
        throw new InvalidOperationException("The verifier admitted an unsupported conversion.");
    }

    private void EmitIntegerTo128(PrimitiveType source, PrimitiveType target, int resultSpill)
    {
        var sourceSigned = PrimitiveTypeFacts.IsSignedInteger(source);
        if (source == PrimitiveType.Char16)
        {
            _code.Bytes(0x0F, 0xB7, 0xC0); // zero-extend the raw code unit
        }
        else if (sourceSigned && source != PrimitiveType.Int64)
        {
            _code.Bytes(0x48, 0x63, 0xC0); // sign-extend the source value to RAX
        }
        if (target == PrimitiveType.UInt128 && sourceSigned)
        {
            _code.Bytes(0x48, 0x85, 0xC0);
            RuntimeBranch(0x8C, 0);
        }

        if (sourceSigned && target == PrimitiveType.Int128)
        {
            _code.Bytes(0x48, 0x89, 0xC2, 0x48, 0xC1, 0xFA, 0x3F); // high word is sign extension
        }
        else
        {
            _code.Bytes(0x31, 0xD2); // unsigned inputs and successful uint128 inputs zero-extend
        }
        _code.Store64(Spill(resultSpill));
        _code.Bytes(0x48, 0x89, 0xD0);
        _code.Store64(Spill(resultSpill) + sizeof(ulong));
        _code.Address(Spill(resultSpill));
    }

    private void EmitCheckedIntegerConversion(PrimitiveType source, PrimitiveType target)
    {
        var sourceWidth = source == PrimitiveType.Char16 ? 16 : PrimitiveTypeFacts.IntegerWidth(source);
        var targetWidth = target == PrimitiveType.Char16 ? 16 : PrimitiveTypeFacts.IntegerWidth(target);
        var sourceSigned = PrimitiveTypeFacts.IsSignedInteger(source);
        var targetSigned = target != PrimitiveType.Char16 && PrimitiveTypeFacts.IsSignedInteger(target);

        if (sourceSigned && sourceWidth < 64)
        {
            _code.Bytes(0x48, 0x63, 0xC0); // sign-extend the source value to RAX
        }
        else if (!sourceSigned && sourceWidth < 32)
        {
            _code.Bytes(0x89, 0xC0); // clear the upper half after loading a narrow unsigned value
        }

        var minimum = 0L;
        if (targetSigned)
        {
            minimum = targetWidth == 64 ? long.MinValue : -(1L << (targetWidth - 1));
        }
        var maximum = targetSigned
            ? (1UL << (targetWidth - 1)) - 1
            : targetWidth == 64 ? ulong.MaxValue : (1UL << targetWidth) - 1;

        if (sourceSigned && !targetSigned)
        {
            CompareRaxToBound(0);
            RuntimeBranch(0x8C, 0);
        }
        else if (sourceSigned && targetSigned)
        {
            CompareRaxToBound(unchecked((ulong)minimum));
            RuntimeBranch(0x8C, 0);
        }

        CompareRaxToBound(maximum);
        RuntimeBranch(sourceSigned && targetSigned ? (byte)0x8F : (byte)0x87, 0);

        if (targetWidth == 8 && targetSigned)
        {
            _code.Bytes(0x0F, 0xBE, 0xC0);
        }
        else if (targetWidth == 16)
        {
            _code.Bytes(0x0F, targetSigned ? (byte)0xBF : (byte)0xB7, 0xC0);
        }
        else if (targetWidth <= 32)
        {
            _code.Bytes(0x89, 0xC0); // canonicalize the 32-bit destination carrier
        }
    }

    private void CompareRaxToBound(ulong value)
    {
        _code.Bytes(0x48, 0xBA);
        _code.Int64(unchecked((long)value));
        _code.Bytes(0x48, 0x39, 0xD0);
    }

    private void EmitFloatingToInteger(PrimitiveType source, PrimitiveType target, int savedMxcsr, int controlledMxcsr)
    {
        if (source == PrimitiveType.Float32)
        {
            _code.Bytes(0x66, 0x0F, 0x6E, 0xC0); // movd xmm0,eax
            _code.Bytes(0xF3, 0x0F, 0x5A, 0xC0); // cvtss2sd xmm0,xmm0
        }
        else
        {
            _code.Bytes(0x66, 0x48, 0x0F, 0x6E, 0xC0); // movq xmm0,rax
        }

        _code.Bytes(0x66, 0x0F, 0x2E, 0xC0); // ucomisd xmm0,xmm0 rejects NaN
        FloatingRuntimeBranch(0x8A, 0, savedMxcsr, controlledMxcsr);
        switch (target)
        {
            case PrimitiveType.Int32:
                CompareFloat64(-2147483649.0);
                FloatingRuntimeBranch(0x86, 0, savedMxcsr, controlledMxcsr);
                CompareFloat64(2147483648.0);
                FloatingRuntimeBranch(0x83, 0, savedMxcsr, controlledMxcsr);
                break;
            case PrimitiveType.UInt32:
                CompareFloat64(-1.0);
                FloatingRuntimeBranch(0x86, 0, savedMxcsr, controlledMxcsr);
                CompareFloat64(4294967296.0);
                FloatingRuntimeBranch(0x83, 0, savedMxcsr, controlledMxcsr);
                break;
            case PrimitiveType.Int64:
                CompareFloat64(-9223372036854775808.0);
                FloatingRuntimeBranch(0x82, 0, savedMxcsr, controlledMxcsr);
                CompareFloat64(9223372036854775808.0);
                FloatingRuntimeBranch(0x83, 0, savedMxcsr, controlledMxcsr);
                break;
            case PrimitiveType.UInt64:
                CompareFloat64(-1.0);
                FloatingRuntimeBranch(0x86, 0, savedMxcsr, controlledMxcsr);
                CompareFloat64(18446744073709551616.0);
                FloatingRuntimeBranch(0x83, 0, savedMxcsr, controlledMxcsr);
                CompareFloat64(9223372036854775808.0);
                var belowSignedLimit = _code.JumpIf(0x82);
                _code.Bytes(0xF2, 0x0F, 0x5C, 0xC1); // subtract 2^63 before the signed conversion
                _code.Bytes(0xF2, 0x48, 0x0F, 0x2C, 0xC0);
                _code.Bytes(0x48, 0xBA);
                _code.Int64(long.MinValue);
                _code.Bytes(0x48, 0x09, 0xD0); // restore the unsigned high bit
                var completedUnsigned = _code.Jump();
                _code.PatchRelative(belowSignedLimit, _code.Position);
                _code.Bytes(0xF2, 0x48, 0x0F, 0x2C, 0xC0);
                _code.PatchRelative(completedUnsigned, _code.Position);
                break;
            default:
                throw new InvalidOperationException("Floating-to-integer target was not verified.");
        }
        if (target != PrimitiveType.UInt64)
        {
            _code.Bytes(0xF2, 0x48, 0x0F, 0x2C, 0xC0); // cvttsd2si rax,xmm0
        }
    }

    private void CompareFloat64(double value)
    {
        _code.Bytes(0x48, 0xB8);
        _code.Int64(BitConverter.DoubleToInt64Bits(value));
        _code.Bytes(0x66, 0x48, 0x0F, 0x6E, 0xC8); // movq xmm1,rax
        _code.Bytes(0x66, 0x0F, 0x2E, 0xC1); // ucomisd xmm0,xmm1
    }

    private void EmitIntegerToFloating(PrimitiveType source, PrimitiveType target)
    {
        _code.Byte(target == PrimitiveType.Float32 ? (byte)0xF3 : (byte)0xF2);
        if (source is PrimitiveType.UInt32 or PrimitiveType.Int64)
        {
            _code.Byte(0x48);
        }
        _code.Bytes(0x0F, 0x2A, 0xC0);
        EmitFloatingResult(target);
    }

    private void EmitUInt64ToFloating(PrimitiveType target)
    {
        // CVTSI2S treats a 64-bit source as signed, so round the high unsigned half explicitly.
        _code.Bytes(0x48, 0x85, 0xC0);
        var highUnsigned = _code.JumpIf(0x88);
        EmitIntegerToFloating(PrimitiveType.Int64, target);
        var complete = _code.Jump();
        _code.PatchRelative(highUnsigned, _code.Position);

        var precision = target == PrimitiveType.Float32 ? 24 : 53;
        var shift = 64 - precision;
        _code.Bytes(0x48, 0x89, 0xC1); // mov rcx,rax
        _code.Bytes(0x48, 0xC1, 0xE8, (byte)shift); // q = value >> shift

        _code.Bytes(0x48, 0x89, 0xCA); // mov rdx,rcx
        _code.Bytes(0x48, 0xC1, 0xEA, (byte)(shift - 1));
        _code.Bytes(0x83, 0xE2, 0x01); // guard bit
        _code.Bytes(0x85, 0xD2);
        var noGuardBit = _code.JumpIf(0x84);

        _code.Bytes(0x48, 0x89, 0xCA);
        _code.Bytes(0x48, 0xC1, 0xE2, (byte)(64 - (shift - 1)));
        _code.Bytes(0x48, 0x85, 0xD2); // sticky bits below the guard bit
        var tie = _code.JumpIf(0x84);
        _code.Bytes(0x48, 0x83, 0xC0, 0x01);
        var roundedUp = _code.Jump();
        _code.PatchRelative(tie, _code.Position);
        _code.Bytes(0x48, 0xA9, 0x01, 0x00, 0x00, 0x00); // ties round to an even significand
        var evenSignificand = _code.JumpIf(0x84);
        _code.Bytes(0x48, 0x83, 0xC0, 0x01);
        _code.PatchRelative(evenSignificand, _code.Position);
        _code.PatchRelative(roundedUp, _code.Position);
        _code.PatchRelative(noGuardBit, _code.Position);

        _code.Bytes(0x48, 0x0F, 0xBA, 0xE0, (byte)precision); // test for significand carry
        var carry = _code.JumpIf(0x82);
        EmitUInt64FloatingBits(target, 63);
        var packed = _code.Jump();
        _code.PatchRelative(carry, _code.Position);
        _code.Bytes(0x48, 0xC1, 0xE8, 0x01);
        EmitUInt64FloatingBits(target, 64);
        _code.PatchRelative(packed, _code.Position);
        _code.PatchRelative(complete, _code.Position);
    }

    private void EmitUInt64FloatingBits(PrimitiveType target, int exponent)
    {
        var precision = target == PrimitiveType.Float32 ? 24 : 53;
        var fractionMask = (1UL << (precision - 1)) - 1;
        var exponentBias = target == PrimitiveType.Float32 ? 127 : 1023;
        var exponentBits = (ulong)(exponent + exponentBias) << (precision - 1);
        _code.Bytes(0x48, 0xBA);
        _code.Int64(unchecked((long)fractionMask));
        _code.Bytes(0x48, 0x21, 0xD0); // retain the fraction bits
        _code.Bytes(0x48, 0xBA);
        _code.Int64(unchecked((long)exponentBits));
        _code.Bytes(0x48, 0x09, 0xD0); // add the biased exponent
    }

    private void EmitFloatingResult(PrimitiveType target)
    {
        if (target == PrimitiveType.Float32)
        {
            _code.Bytes(0x66, 0x0F, 0x7E, 0xC0); // movd eax,xmm0
        }
        else
        {
            _code.Bytes(0x66, 0x48, 0x0F, 0x7E, 0xC0); // movq rax,xmm0
        }
    }

    private void CheckedDivRem(PrimitiveType type)
    {
        var isWide = IsWide(type);
        var isUnsigned = type is PrimitiveType.UInt32 or PrimitiveType.UInt64;
        if (isWide)
        {
            _code.Bytes(0x48, 0x85, 0xC9);
            RuntimeBranch(0x84, 1);
            if (isUnsigned)
            {
                _code.Bytes(0x31, 0xD2, 0x48, 0xF7, 0xF1);
                return;
            }
            _code.Bytes(0x48, 0xBA);
            _code.Int64(long.MinValue);
            _code.Bytes(0x48, 0x39, 0xD0);
            var normal = _code.JumpIf(0x85);
            _code.Bytes(0x48, 0x83, 0xF9, 0xFF);
            var normalAgain = _code.JumpIf(0x85);
            _runtimeCalls.Add((_code.RelativeCall(), 0));
            _code.PatchRelative(normal, _code.Position);
            _code.PatchRelative(normalAgain, _code.Position);
            _code.Bytes(0x48, 0x99, 0x48, 0xF7, 0xF9);
            return;
        }

        _code.Bytes(0x85, 0xC9);
        RuntimeBranch(0x84, 1);
        if (isUnsigned)
        {
            _code.Bytes(0x31, 0xD2, 0xF7, 0xF1);
            return;
        }
        _code.Byte(0x3D);
        _code.Int32(int.MinValue);
        var normal32 = _code.JumpIf(0x85);
        _code.Bytes(0x83, 0xF9, 0xFF);
        var normalAgain32 = _code.JumpIf(0x85);
        _runtimeCalls.Add((_code.RelativeCall(), 0));
        _code.PatchRelative(normal32, _code.Position);
        _code.PatchRelative(normalAgain32, _code.Position);
        _code.Bytes(0x99, 0xF7, 0xF9);
    }

    private void ConvertInteger(PrimitiveType source, PrimitiveType target, byte policy)
    {
        if (policy == 1)
        {
            if (source == PrimitiveType.Int32 && IsWide(target))
            {
                _code.Bytes(0x48, 0x63, 0xC0); // Sign-extend the source value before widening.
            }
            return;
        }
        if (policy != 2)
        {
            throw new InvalidOperationException("TRRM integer conversion policy was not verified.");
        }
        if (source == target)
        {
            return;
        }
        if (source == PrimitiveType.Int32)
        {
            if (target == PrimitiveType.Int64)
            {
                _code.Bytes(0x48, 0x63, 0xC0);
            }
            else if (target is PrimitiveType.UInt32 or PrimitiveType.UInt64)
            {
                _code.Bytes(0x85, 0xC0);
                var nonnegative = _code.JumpIf(0x89);
                _code.Bytes(0x31, 0xC0);
                _code.PatchRelative(nonnegative, _code.Position);
            }
            return;
        }
        if (source == PrimitiveType.UInt32)
        {
            if (target == PrimitiveType.Int32)
            {
                _code.Bytes(0x85, 0xC0);
                var inRange = _code.JumpIf(0x89);
                _code.Byte(0xB8);
                _code.Int32(int.MaxValue);
                _code.PatchRelative(inRange, _code.Position);
            }
            return;
        }
        if (source == PrimitiveType.Int64)
        {
            if (target == PrimitiveType.UInt64)
            {
                _code.Bytes(0x48, 0x85, 0xC0);
                var nonnegative = _code.JumpIf(0x89);
                _code.Bytes(0x31, 0xC0);
                _code.PatchRelative(nonnegative, _code.Position);
            }
            else if (target == PrimitiveType.Int32)
            {
                _code.Bytes(0x48, 0x3D);
                _code.Int32(int.MinValue);
                var aboveMinimum = _code.JumpIf(0x8D);
                _code.Byte(0xB8);
                _code.Int32(int.MinValue);
                var belowRange = _code.Jump();
                _code.PatchRelative(aboveMinimum, _code.Position);
                _code.Bytes(0x48, 0x3D);
                _code.Int32(int.MaxValue);
                var belowMaximum = _code.JumpIf(0x8E);
                _code.Byte(0xB8);
                _code.Int32(int.MaxValue);
                _code.PatchRelative(belowMaximum, _code.Position);
                _code.PatchRelative(belowRange, _code.Position);
            }
            else if (target == PrimitiveType.UInt32)
            {
                _code.Bytes(0x48, 0x85, 0xC0);
                var nonnegative = _code.JumpIf(0x89);
                _code.Bytes(0x31, 0xC0);
                var negative = _code.Jump();
                _code.PatchRelative(nonnegative, _code.Position);
                _code.Byte(0xB9);
                _code.Int32(-1);
                _code.Bytes(0x48, 0x39, 0xC8);
                var inRange = _code.JumpIf(0x86);
                _code.Byte(0xB8);
                _code.Int32(-1);
                _code.PatchRelative(inRange, _code.Position);
                _code.PatchRelative(negative, _code.Position);
            }
            return;
        }
        if (source == PrimitiveType.UInt64)
        {
            if (target == PrimitiveType.Int64)
            {
                _code.Bytes(0x48, 0x85, 0xC0);
                var inRange = _code.JumpIf(0x89);
                _code.Bytes(0x48, 0xB8);
                _code.Int64(long.MaxValue);
                _code.PatchRelative(inRange, _code.Position);
            }
            else if (target == PrimitiveType.Int32)
            {
                _code.Byte(0xB9);
                _code.Int32(int.MaxValue);
                _code.Bytes(0x48, 0x39, 0xC8);
                var inRange = _code.JumpIf(0x86);
                _code.Byte(0xB8);
                _code.Int32(int.MaxValue);
                _code.PatchRelative(inRange, _code.Position);
            }
            else if (target == PrimitiveType.UInt32)
            {
                _code.Byte(0xB9);
                _code.Int32(-1);
                _code.Bytes(0x48, 0x39, 0xC8);
                var inRange = _code.JumpIf(0x86);
                _code.Byte(0xB8);
                _code.Int32(-1);
                _code.PatchRelative(inRange, _code.Position);
            }
            return;
        }
        throw new InvalidOperationException("TRRM integer conversion types were not verified.");
    }

    private bool TryEmitUnsignedPowerOfTwoBinary(BoundBinary binary)
    {
        if (binary.Operator is not (TokenKind.Slash or TokenKind.Percent)
            || binary.Left.Type is not (PrimitiveType.UInt32 or PrimitiveType.UInt64)
            || binary.Right is not BoundConstant constant)
        {
            return false;
        }

        var divisor = binary.Left.Type == PrimitiveType.UInt32
            ? unchecked((uint)constant.Value)
            : unchecked((ulong)constant.Value);
        if (divisor == 0 || (divisor & (divisor - 1)) != 0)
        {
            return false;
        }

        if (binary.Operator == TokenKind.Slash)
        {
            if (divisor != 1)
            {
                if (binary.Left.Type == PrimitiveType.UInt64)
                {
                    _code.Byte(0x48);
                }
                _code.Bytes(0xC1, 0xE8, (byte)BitOperations.TrailingZeroCount(divisor));
            }
        }
        else if (divisor == 1)
        {
            _code.Bytes(0x31, 0xC0);
        }
        else
        {
            var mask = divisor - 1;
            if (binary.Left.Type == PrimitiveType.UInt32 || mask <= uint.MaxValue)
            {
                _code.Byte(0x25); // and eax,imm32; zero-extends for uint64
                _code.Int32(unchecked((int)mask));
            }
            else
            {
                _code.Bytes(0x48, 0xB9);
                _code.Int64(unchecked((long)mask));
                _code.Bytes(0x48, 0x21, 0xC8);
            }
        }

        return true;
    }

    private bool TryEmitSignedPowerOfTwoBinary(BoundBinary binary)
    {
        if (binary.Operator is not (TokenKind.Slash or TokenKind.Percent)
            || binary.Left.Type is not (PrimitiveType.Int32 or PrimitiveType.Int64)
            || binary.Right is not BoundConstant constant)
        {
            return false;
        }

        var divisor = constant.Value;
        if (divisor <= 0 || (divisor & (divisor - 1)) != 0)
        {
            return false;
        }

        if (divisor == 1)
        {
            if (binary.Operator == TokenKind.Percent)
            {
                _code.Bytes(0x31, 0xC0);
            }
            return true;
        }

        var wide = binary.Left.Type == PrimitiveType.Int64;
        var width = wide ? 64 : 32;
        var shift = BitOperations.TrailingZeroCount((ulong)divisor);
        if (binary.Operator == TokenKind.Percent)
        {
            if (wide)
            {
                _code.Byte(0x48);
            }
            _code.Bytes(0x89, 0xC2); // preserve the numerator in rdx
        }

        // Only negative numerators receive a bias of divisor - 1. Its addition cannot
        // overflow: a negative n plus at most divisor - 1 is at most divisor - 2.
        if (wide)
        {
            _code.Byte(0x48);
        }
        _code.Bytes(0x89, 0xC1); // mov rcx,rax
        if (wide)
        {
            _code.Byte(0x48);
        }
        _code.Bytes(0xC1, 0xF9, (byte)(width - 1)); // sar rcx,width-1
        if (wide)
        {
            _code.Byte(0x48);
        }
        _code.Bytes(0xC1, 0xE9, (byte)(width - shift)); // logical shift yields 0 or divisor - 1
        if (wide)
        {
            _code.Byte(0x48);
        }
        _code.Bytes(0x01, 0xC8); // add rax,rcx
        if (wide)
        {
            _code.Byte(0x48);
        }
        _code.Bytes(0xC1, 0xF8, (byte)shift); // arithmetic quotient truncated toward zero

        if (binary.Operator == TokenKind.Percent)
        {
            if (wide)
            {
                _code.Byte(0x48);
            }
            _code.Bytes(0xC1, 0xE0, (byte)shift); // quotient * divisor is within the numerator's range
            if (wide)
            {
                _code.Byte(0x48);
            }
            _code.Bytes(0x29, 0xC2); // sub rdx,rax
            if (wide)
            {
                _code.Byte(0x48);
            }
            _code.Bytes(0x89, 0xD0);
        }

        return true;
    }

    private void BeginFloatingEnvironment(int savedMxcsr, int controlledMxcsr)
    {
        _code.StoreMxcsr(savedMxcsr);
        _code.Load(savedMxcsr);
        _code.Byte(0x25); // and eax,~(RC[14:13] | DAZ[6] | FTZ[15])
        _code.Int32(unchecked((int)0xFFFF1FBF));
        _code.Byte(0x0D); // or eax,all six SSE exception masks
        _code.Int32(0x1F80);
        _code.Store(controlledMxcsr);
        _code.LoadMxcsr(controlledMxcsr);
    }

    private void FloatingBinary(TokenKind op, PrimitiveType type, int spill)
    {
        if (op == TokenKind.Percent)
        {
            FloatingRemainder(type, spill);
            return;
        }

        var isSingle = type == PrimitiveType.Float32;
        if (isSingle)
        {
            _code.Bytes(0x66, 0x0F, 0x6E, 0xC8); // movd xmm1,eax
        }
        else
        {
            _code.Bytes(0x66, 0x48, 0x0F, 0x6E, 0xC8); // movq xmm1,rax
        }

        var savedMxcsr = Spill(spill + 1);
        var controlledMxcsr = Spill(spill + 2);
        BeginFloatingEnvironment(savedMxcsr, controlledMxcsr);

        LoadValue(Spill(spill), type);
        if (isSingle)
        {
            _code.Bytes(0x66, 0x0F, 0x6E, 0xC0); // movd xmm0,eax
        }
        else
        {
            _code.Bytes(0x66, 0x48, 0x0F, 0x6E, 0xC0); // movq xmm0,rax
        }

        if (op is TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash)
        {
            _code.Bytes(isSingle ? (byte)0xF3 : (byte)0xF2, 0x0F,
                op switch {
                    TokenKind.Plus => (byte)0x58,
                    TokenKind.Minus => (byte)0x5C,
                    TokenKind.Star => (byte)0x59,
                    TokenKind.Slash => (byte)0x5E,
                    _ => throw new InvalidOperationException(),
                }, 0xC1);
            if (isSingle)
            {
                _code.Bytes(0x66, 0x0F, 0x7E, 0xC0); // movd eax,xmm0
            }
            else
            {
                _code.Bytes(0x66, 0x48, 0x0F, 0x7E, 0xC0); // movq rax,xmm0
            }
        }
        else
        {
            if (isSingle)
            {
                _code.Bytes(0x0F, 0x2E, 0xC1); // ucomiss xmm0,xmm1
            }
            else
            {
                _code.Bytes(0x66, 0x0F, 0x2E, 0xC1); // ucomisd xmm0,xmm1
            }
            switch (op)
            {
                case TokenKind.EqualsEquals:
                    _code.Bytes(0x0F, 0x94, 0xC0, 0x0F, 0x9B, 0xC1, 0x20, 0xC8);
                    break;
                case TokenKind.BangEquals:
                    _code.Bytes(0x0F, 0x95, 0xC0, 0x0F, 0x9A, 0xC1, 0x08, 0xC8);
                    break;
                case TokenKind.Less:
                    _code.Bytes(0x0F, 0x92, 0xC0, 0x0F, 0x9B, 0xC1, 0x20, 0xC8);
                    break;
                case TokenKind.LessEquals:
                    _code.Bytes(0x0F, 0x96, 0xC0, 0x0F, 0x9B, 0xC1, 0x20, 0xC8);
                    break;
                case TokenKind.Greater:
                    _code.Bytes(0x0F, 0x97, 0xC0);
                    break;
                case TokenKind.GreaterEquals:
                    _code.Bytes(0x0F, 0x93, 0xC0);
                    break;
                default:
                    throw new InvalidOperationException();
            }
            _code.Bytes(0x0F, 0xB6, 0xC0); // movzx eax,al
        }
        _code.LoadMxcsr(savedMxcsr);
    }

    private void FloatingRemainder(PrimitiveType type, int spill)
    {
        var isSingle = type == PrimitiveType.Float32;
        var right = Spill(spill + 3);
        var savedControlWord = Spill(spill + 1);
        var controlledControlWord = Spill(spill + 2);
        StoreValue(right, type);

        _code.StoreX87ControlWord(savedControlWord);
        _code.Load(savedControlWord);
        _code.Byte(0x25);
        _code.Int32(~0x0F3F);
        _code.Byte(0x0D);
        _code.Int32(0x033F);
        _code.Store(controlledControlWord);
        _code.LoadX87ControlWord(controlledControlWord);

        _code.LoadX87Value(right, isSingle);
        _code.LoadX87Value(Spill(spill), isSingle);
        var repeat = _code.Position;
        _code.Bytes(0xD9, 0xF8); // fprem
        _code.Bytes(0xDF, 0xE0); // fnstsw ax
        _code.Bytes(0xF6, 0xC4, 0x04); // test ah, C2
        var incomplete = _code.JumpIf(0x85);
        _code.PatchRelative(incomplete, repeat);
        _code.StoreX87Value(Spill(spill), isSingle);
        _code.Bytes(0xDD, 0xD8); // fstp st0
        _code.LoadX87ControlWord(savedControlWord);
        LoadValue(Spill(spill), type);
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "The verifier restricts binary operators to the supported native profile.")]
    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "The verifier excludes other token kinds before reaching the comparison encoder.")]
    private void Binary(TokenKind op, PrimitiveType type,
        BoundIntegerOverflowBehavior overflowBehavior = BoundIntegerOverflowBehavior.Checked)
    {
        if (PrimitiveTypeFacts.IsWideInteger(type))
        {
            throw new InvalidOperationException("Wide integer operations require their 128-bit operands.");
        }
        if (IsWide(type))
        {
            BinaryWide(op, type == PrimitiveType.UInt64, overflowBehavior);
            return;
        }
        var width = PrimitiveTypeFacts.IntegerWidth(type);
        var narrow = IsNarrowInteger(type);
        var unsigned = type is PrimitiveType.UInt8 or PrimitiveType.UInt16 or PrimitiveType.UInt32
            or PrimitiveType.Char8 or PrimitiveType.Char16 or PrimitiveType.Char32 or PrimitiveType.Rune;
        switch (op)
        {
            case TokenKind.ShiftLeft:
            case TokenKind.ShiftRight:
            case TokenKind.ShiftRightUnsigned:
                _code.Bytes(0x48, 0x83, 0xF9, checked((byte)width));
                RuntimeBranch(0x83, 0);
                if (op == TokenKind.ShiftRightUnsigned && narrow
                    && PrimitiveTypeFacts.IsSignedInteger(type))
                {
                    _code.Byte(0x25);
                    _code.Int32((1 << width) - 1);
                }
                _code.Bytes(0xD3, op == TokenKind.ShiftLeft ? (byte)0xE0
                    : op == TokenKind.ShiftRight && !unsigned ? (byte)0xF8 : (byte)0xE8);
                break;
            case TokenKind.Plus:
                _code.Bytes(0x01, 0xC8);
                if (overflowBehavior == BoundIntegerOverflowBehavior.Checked)
                {
                    RuntimeBranch(unsigned ? (byte)0x82 : (byte)0x80, 0);
                }
                break;
            case TokenKind.Minus:
                _code.Bytes(0x29, 0xC8);
                if (overflowBehavior == BoundIntegerOverflowBehavior.Checked)
                {
                    RuntimeBranch(unsigned ? (byte)0x82 : (byte)0x80, 0);
                }
                break;
            case TokenKind.Star:
                if (unsigned)
                {
                    _code.Bytes(0xF7, 0xE1); // mul ecx; high half in edx
                    if (overflowBehavior == BoundIntegerOverflowBehavior.Checked)
                    {
                        _code.Bytes(0x85, 0xD2);
                        RuntimeBranch(0x85, 0);
                    }
                }
                else
                {
                    _code.Bytes(0x0F, 0xAF, 0xC1);
                    if (overflowBehavior == BoundIntegerOverflowBehavior.Checked)
                    {
                        RuntimeBranch(0x80, 0);
                    }
                }
                break;
            case TokenKind.Slash:
            case TokenKind.Percent:
                _code.Bytes(0x85, 0xC9); // test ecx,ecx
                RuntimeBranch(0x84, 1);
                if (unsigned)
                {
                    _code.Bytes(0x31, 0xD2, 0xF7, 0xF1); // xor edx,edx; div ecx
                    if (op == TokenKind.Percent)
                    {
                        _code.Bytes(0x89, 0xD0);
                    }
                    break;
                }
                _code.Byte(0x3D);
                _code.Int32(width switch {
                    8 => sbyte.MinValue,
                    16 => short.MinValue,
                    _ => int.MinValue,
                });
                var normal = _code.JumpIf(0x85);
                _code.Bytes(0x83, 0xF9, 0xFF); // cmp ecx,-1
                var normalAgain = _code.JumpIf(0x85);
                if (op == TokenKind.Slash)
                {
                    _runtimeCalls.Add((_code.RelativeCall(), 0));
                }
                else
                {
                    _code.Bytes(0x31, 0xC0); // int.MinValue % -1 is zero
                    var done = _code.Jump();
                    _code.PatchRelative(normal, _code.Position);
                    _code.PatchRelative(normalAgain, _code.Position);
                    _code.Bytes(0x99, 0xF7, 0xF9); // cdq; idiv ecx
                    _code.Bytes(0x89, 0xD0);
                    _code.PatchRelative(done, _code.Position);
                    break;
                }
                _code.PatchRelative(normal, _code.Position);
                _code.PatchRelative(normalAgain, _code.Position);
                _code.Bytes(0x99, 0xF7, 0xF9);
                break;
            case TokenKind.Ampersand:
                _code.Bytes(0x21, 0xC8);
                break;
            case TokenKind.Pipe:
                _code.Bytes(0x09, 0xC8);
                break;
            case TokenKind.Caret:
                _code.Bytes(0x31, 0xC8);
                break;
            default:
                _code.Bytes(0x39, 0xC8); // cmp eax,ecx
                _code.Bytes(0x0F, (byte)(op switch {
                    TokenKind.EqualsEquals => 0x94,
                    TokenKind.BangEquals => 0x95,
                    TokenKind.Less => unsigned ? 0x92 : 0x9C,
                    TokenKind.LessEquals => unsigned ? 0x96 : 0x9E,
                    TokenKind.Greater => unsigned ? 0x97 : 0x9F,
                    TokenKind.GreaterEquals => unsigned ? 0x93 : 0x9D,
                    _ => throw new InvalidOperationException(),
                }), 0xC0);
                _code.Bytes(0x0F, 0xB6, 0xC0); // movzx eax,al
                break;
        }
        NormalizeNarrowInteger(type, checkRange: narrow
            && overflowBehavior == BoundIntegerOverflowBehavior.Checked
            && op is TokenKind.Plus or TokenKind.Minus or TokenKind.Star);
    }

    private void NormalizeNarrowInteger(PrimitiveType type, bool checkRange)
    {
        var width = PrimitiveTypeFacts.IntegerWidth(type);
        if (width is not (8 or 16))
        {
            return;
        }
        if (PrimitiveTypeFacts.IsSignedInteger(type))
        {
            if (checkRange)
            {
                _code.Bytes(0x89, 0xC2); // preserve the full result for exact-width range checking
            }
            _code.Bytes(0x0F, width == 8 ? (byte)0xBE : (byte)0xBF, 0xC0);
            if (checkRange)
            {
                _code.Bytes(0x39, 0xD0);
                RuntimeBranch(0x85, 0);
            }
        }
        else
        {
            if (checkRange)
            {
                _code.Byte(0x3D);
                _code.Int32((1 << width) - 1);
                RuntimeBranch(0x87, 0);
            }
            _code.Bytes(0x0F, width == 8 ? (byte)0xB6 : (byte)0xB7, 0xC0);
        }
    }

    private void EmitWideIntegerUnary(TokenKind operation, PrimitiveType type,
        BoundIntegerOverflowBehavior overflowBehavior, int valueOffset)
    {
        if (operation == TokenKind.Minus && overflowBehavior == BoundIntegerOverflowBehavior.Checked)
        {
            if (type == PrimitiveType.Int128)
            {
                _code.Load64ToRegister(valueOffset + sizeof(ulong), 0);
                _code.Bytes(0x48, 0xB9);
                _code.Int64(long.MinValue);
                _code.Bytes(0x48, 0x39, 0xC8);
                var notMinimumHigh = _code.JumpIf(0x85);
                _code.Load64(valueOffset);
                _code.Bytes(0x48, 0x85, 0xC0);
                RuntimeBranch(0x84, 0);
                _code.PatchRelative(notMinimumHigh, _code.Position);
            }
            else
            {
                _code.Load64(valueOffset);
                _code.Bytes(0x48, 0x85, 0xC0);
                RuntimeBranch(0x85, 0);
                _code.Load64(valueOffset + sizeof(ulong));
                _code.Bytes(0x48, 0x85, 0xC0);
                RuntimeBranch(0x85, 0);
            }
        }

        switch (operation)
        {
            case TokenKind.Plus:
                return;
            case TokenKind.Minus:
                NegateWideIntegerAt(valueOffset);
                return;
            case TokenKind.Tilde:
                _code.Load64(valueOffset);
                _code.Bytes(0x48, 0xF7, 0xD0);
                _code.Store64(valueOffset);
                _code.Load64(valueOffset + sizeof(ulong), destinationRegister: true);
                _code.Bytes(0x48, 0xF7, 0xD2);
                _code.Store64FromRegister(valueOffset + sizeof(ulong), 2);
                return;
            default:
                throw new InvalidOperationException("The verifier admitted an unsupported 128-bit unary operator.");
        }
    }

    private void EmitWideIntegerBinary(TokenKind operation, PrimitiveType type,
        BoundIntegerOverflowBehavior overflowBehavior, int leftOffset, int rightOffset,
        PrimitiveType rightType, int resultOffset)
    {
        if (operation is TokenKind.EqualsEquals or TokenKind.BangEquals
            or TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater or TokenKind.GreaterEquals)
        {
            EmitWideIntegerComparison(operation, type, leftOffset, rightOffset);
            return;
        }

        if (operation is TokenKind.ShiftLeft or TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned)
        {
            EmitWideIntegerShift(operation, type, leftOffset, rightOffset, rightType, resultOffset);
            return;
        }

        if (operation == TokenKind.Star)
        {
            EmitWideIntegerMultiply(type, overflowBehavior, leftOffset, rightOffset, resultOffset);
            return;
        }
        if (operation is TokenKind.Slash or TokenKind.Percent)
        {
            EmitWideIntegerDivide(operation, type, leftOffset, rightOffset, resultOffset);
            return;
        }

        var unsigned = type == PrimitiveType.UInt128;
        switch (operation)
        {
            case TokenKind.Plus:
            case TokenKind.Minus:
                _code.Load64ToRegister(leftOffset, 0);
                _code.Load64ToRegister(rightOffset, 1);
                _code.Bytes(0x48, operation == TokenKind.Plus ? (byte)0x01 : (byte)0x29, 0xC8);
                _code.Load64ToRegister(leftOffset + sizeof(ulong), 2);
                _code.Load64ToRegister(rightOffset + sizeof(ulong), 1);
                _code.Bytes(0x48, operation == TokenKind.Plus ? (byte)0x11 : (byte)0x19, 0xCA);
                if (overflowBehavior == BoundIntegerOverflowBehavior.Checked)
                {
                    RuntimeBranch(unsigned ? (byte)0x82 : (byte)0x80, 0);
                }
                break;
            case TokenKind.Ampersand:
            case TokenKind.Pipe:
            case TokenKind.Caret:
                var bitwiseOpcode = operation switch {
                    TokenKind.Ampersand => (byte)0x21,
                    TokenKind.Pipe => (byte)0x09,
                    _ => (byte)0x31,
                };
                _code.Load64ToRegister(leftOffset, 0);
                _code.Load64ToRegister(rightOffset, 1);
                _code.Bytes(0x48, bitwiseOpcode, 0xC8);
                _code.Store64(resultOffset);
                _code.Load64ToRegister(leftOffset + sizeof(ulong), 2);
                _code.Load64ToRegister(rightOffset + sizeof(ulong), 1);
                _code.Bytes(0x48, bitwiseOpcode, 0xCA);
                _code.Store64FromRegister(resultOffset + sizeof(ulong), 2);
                _code.Address(resultOffset);
                return;
            default:
                throw new InvalidOperationException("The verifier admitted an unsupported 128-bit operator.");
        }

        _code.Store64(resultOffset);
        _code.Store64FromRegister(resultOffset + sizeof(ulong), 2);
        _code.Address(resultOffset);
    }

    private void EmitWideIntegerMultiply(PrimitiveType type,
        BoundIntegerOverflowBehavior overflowBehavior, int leftOffset, int rightOffset, int resultOffset)
    {
        // Magnitudes use shift/add so checked overflow observes every discarded product bit.
        var scratchA = Math.Max(Math.Max(leftOffset, rightOffset), resultOffset) + 16;
        var scratchB = scratchA + 16;
        var isSigned = type == PrimitiveType.Int128;

        if (isSigned)
        {
            _code.Load64(leftOffset + sizeof(ulong));
            _code.Bytes(0x48, 0xC1, 0xE8, 0x3F, 0x41, 0x89, 0xC2);
            _code.Load64(rightOffset + sizeof(ulong));
            _code.Bytes(0x48, 0xC1, 0xE8, 0x3F, 0x41, 0x89, 0xC3);
        }

        _code.Load64(leftOffset);
        _code.Store64(scratchA);
        _code.Load64(leftOffset + sizeof(ulong));
        _code.Store64(scratchA + sizeof(ulong));
        _code.Load64(rightOffset);
        _code.Store64(scratchB);
        _code.Load64(rightOffset + sizeof(ulong));
        _code.Store64(scratchB + sizeof(ulong));

        if (isSigned)
        {
            _code.Bytes(0x45, 0x85, 0xD2);
            var leftNonnegative = _code.JumpIf(0x84);
            NegateWideIntegerAt(scratchA);
            _code.PatchRelative(leftNonnegative, _code.Position);

            _code.Bytes(0x45, 0x85, 0xDB);
            var rightNonnegative = _code.JumpIf(0x84);
            NegateWideIntegerAt(scratchB);
            _code.PatchRelative(rightNonnegative, _code.Position);

            _code.Bytes(0x45, 0x31, 0xDA);
        }

        _code.Bytes(0x31, 0xC0);
        _code.Store64(resultOffset);
        _code.Store64(resultOffset + sizeof(ulong));
        _code.Bytes(0x41, 0xB9);
        _code.Int32(128);
        var loop = _code.Position;

        _code.Load64(scratchB);
        _code.Bytes(0x48, 0x83, 0xE0, 0x01, 0x48, 0x85, 0xC0);
        var skipAdd = _code.JumpIf(0x84);
        _code.Load64(resultOffset);
        _code.Load64ToRegister(scratchA, 1);
        _code.Bytes(0x48, 0x01, 0xC8);
        _code.Store64(resultOffset);
        _code.Load64(resultOffset + sizeof(ulong), destinationRegister: true);
        _code.Load64ToRegister(scratchA + sizeof(ulong), 1);
        _code.Bytes(0x48, 0x11, 0xCA);
        if (overflowBehavior == BoundIntegerOverflowBehavior.Checked)
        {
            RuntimeBranch(0x82, 0);
        }
        _code.Store64FromRegister(resultOffset + sizeof(ulong), 2);
        _code.PatchRelative(skipAdd, _code.Position);

        _code.Bytes(0x41, 0x83, 0xF9, 0x01);
        var lastMultiplierBit = _code.JumpIf(0x84);
        _code.Load64(scratchB + sizeof(ulong), destinationRegister: true);
        _code.Bytes(0x48, 0xD1, 0xEA);
        _code.Store64FromRegister(scratchB + sizeof(ulong), 2);
        _code.Load64(scratchB);
        _code.Bytes(0x48, 0xD1, 0xD8);
        _code.Store64(scratchB);

        _code.Load64(scratchA);
        _code.Bytes(0x48, 0xD1, 0xE0);
        _code.Store64(scratchA);
        _code.Load64(scratchA + sizeof(ulong), destinationRegister: true);
        _code.Bytes(0x48, 0xD1, 0xD2, 0x41, 0x0F, 0x92, 0xC0);
        _code.Store64FromRegister(scratchA + sizeof(ulong), 2);
        if (overflowBehavior == BoundIntegerOverflowBehavior.Checked)
        {
            _code.Load64(scratchB);
            _code.Load64ToRegister(scratchB + sizeof(ulong), 1);
            _code.Bytes(0x48, 0x09, 0xC8, 0x48, 0x85, 0xC0);
            var noMultiplierBitsRemain = _code.JumpIf(0x84);
            _code.Bytes(0x41, 0xF6, 0xC0, 0x01);
            RuntimeBranch(0x85, 0);
            _code.PatchRelative(noMultiplierBitsRemain, _code.Position);
        }
        _code.Bytes(0x41, 0xFF, 0xC9);
        var repeat = _code.JumpIf(0x85);
        _code.PatchRelative(repeat, loop);
        _code.PatchRelative(lastMultiplierBit, _code.Position);

        if (isSigned && overflowBehavior == BoundIntegerOverflowBehavior.Checked)
        {
            _code.Bytes(0x45, 0x85, 0xD2);
            var positiveResult = _code.JumpIf(0x84);

            _code.Load64(resultOffset + sizeof(ulong));
            _code.Bytes(0x48, 0xB9);
            _code.Int64(long.MinValue);
            _code.Bytes(0x48, 0x39, 0xC8);
            RuntimeBranch(0x87, 0);
            var magnitudeBelowMinimum = _code.JumpIf(0x82);
            _code.Load64(resultOffset);
            _code.Bytes(0x48, 0x85, 0xC0);
            RuntimeBranch(0x85, 0);
            _code.PatchRelative(magnitudeBelowMinimum, _code.Position);
            var negativeChecked = _code.Jump();

            _code.PatchRelative(positiveResult, _code.Position);
            _code.Load64(resultOffset + sizeof(ulong));
            _code.Bytes(0x48, 0xB9);
            _code.Int64(long.MinValue);
            _code.Bytes(0x48, 0x39, 0xC8);
            RuntimeBranch(0x83, 0);
            _code.PatchRelative(negativeChecked, _code.Position);
        }

        if (isSigned)
        {
            _code.Bytes(0x45, 0x85, 0xD2);
            var nonnegativeResult = _code.JumpIf(0x84);
            NegateWideIntegerAt(resultOffset);
            _code.PatchRelative(nonnegativeResult, _code.Position);
        }

        _code.Address(resultOffset);
    }

    private void EmitWideIntegerDivide(TokenKind operation, PrimitiveType type,
        int leftOffset, int rightOffset, int resultOffset)
    {
        // Restoring division keeps the transient 129th remainder bit separate from the two-word value.
        var scratchNumerator = Math.Max(Math.Max(leftOffset, rightOffset), resultOffset) + 16;
        var scratchDivisor = scratchNumerator + 16;
        var scratchRemainder = scratchDivisor + 16;
        var scratchSigns = scratchRemainder + 16;
        var isSigned = type == PrimitiveType.Int128;
        var skipSpecialCases = new List<int>();
        var specialRemainder = -1;

        _code.Load64(rightOffset);
        _code.Load64ToRegister(rightOffset + sizeof(ulong), 1);
        _code.Bytes(0x48, 0x09, 0xC8, 0x48, 0x85, 0xC0);
        RuntimeBranch(0x84, 1);

        if (isSigned)
        {
            _code.Load64ToRegister(leftOffset + sizeof(ulong), 0);
            _code.Bytes(0x48, 0xB9);
            _code.Int64(long.MinValue);
            _code.Bytes(0x48, 0x39, 0xC8);
            skipSpecialCases.Add(_code.JumpIf(0x85));
            _code.Load64(leftOffset);
            _code.Bytes(0x48, 0x85, 0xC0);
            skipSpecialCases.Add(_code.JumpIf(0x85));
            _code.Load64ToRegister(rightOffset + sizeof(ulong), 0);
            _code.Bytes(0x48, 0x83, 0xF8, 0xFF);
            skipSpecialCases.Add(_code.JumpIf(0x85));
            _code.Load64(rightOffset);
            _code.Bytes(0x48, 0x83, 0xF8, 0xFF);
            skipSpecialCases.Add(_code.JumpIf(0x85));

            if (operation == TokenKind.Slash)
            {
                _runtimeCalls.Add((_code.RelativeCall(), 0));
                _code.Byte(0xCC);
            }
            else
            {
                _code.Bytes(0x31, 0xC0);
                _code.Store64(resultOffset);
                _code.Store64(resultOffset + sizeof(ulong));
                _code.Address(resultOffset);
                specialRemainder = _code.Jump();
            }
        }

        var normalDivision = _code.Position;
        foreach (var offset in skipSpecialCases)
        {
            _code.PatchRelative(offset, normalDivision);
        }

        _code.Load64(leftOffset);
        _code.Store64(scratchNumerator);
        _code.Load64(leftOffset + sizeof(ulong));
        _code.Store64(scratchNumerator + sizeof(ulong));
        _code.Load64(rightOffset);
        _code.Store64(scratchDivisor);
        _code.Load64(rightOffset + sizeof(ulong));
        _code.Store64(scratchDivisor + sizeof(ulong));

        if (isSigned)
        {
            _code.Load64(leftOffset + sizeof(ulong));
            _code.Bytes(0x48, 0xC1, 0xE8, 0x3F);
            _code.Store(scratchSigns);
            _code.Load64(rightOffset + sizeof(ulong));
            _code.Bytes(0x48, 0xC1, 0xE8, 0x3F);
            _code.Store(scratchSigns + sizeof(int));

            _code.Load(scratchSigns);
            _code.Bytes(0x85, 0xC0);
            var numeratorNonnegative = _code.JumpIf(0x84);
            NegateWideIntegerAt(scratchNumerator);
            _code.PatchRelative(numeratorNonnegative, _code.Position);

            _code.Load(scratchSigns + sizeof(int));
            _code.Bytes(0x85, 0xC0);
            var divisorNonnegative = _code.JumpIf(0x84);
            NegateWideIntegerAt(scratchDivisor);
            _code.PatchRelative(divisorNonnegative, _code.Position);
        }
        else
        {
            _code.Bytes(0x31, 0xC0);
            _code.Store(scratchSigns);
            _code.Store(scratchSigns + sizeof(int));
        }

        _code.Bytes(0x31, 0xC0);
        _code.Store64(resultOffset);
        _code.Store64(resultOffset + sizeof(ulong));
        _code.Store64(scratchRemainder);
        _code.Store64(scratchRemainder + sizeof(ulong));
        _code.Bytes(0x41, 0xB9);
        _code.Int32(128);
        var loop = _code.Position;

        _code.Load64(scratchNumerator + sizeof(ulong));
        _code.Bytes(0x48, 0xC1, 0xE8, 0x3F, 0x41, 0x89, 0xC3);
        _code.Load64(scratchNumerator);
        _code.Load64(scratchNumerator + sizeof(ulong), destinationRegister: true);
        _code.Bytes(0x48, 0x0F, 0xA4, 0xC2, 0x01, 0x48, 0xD1, 0xE0);
        _code.Store64(scratchNumerator);
        _code.Store64FromRegister(scratchNumerator + sizeof(ulong), 2);

        _code.Load64(scratchRemainder + sizeof(ulong));
        _code.Bytes(0x48, 0xC1, 0xE8, 0x3F, 0x41, 0x89, 0xC0);
        _code.Load64(scratchRemainder);
        _code.Load64(scratchRemainder + sizeof(ulong), destinationRegister: true);
        _code.Bytes(0x48, 0x0F, 0xA4, 0xC2, 0x01, 0x48, 0xD1, 0xE0, 0x4C, 0x09, 0xD8);
        _code.Store64(scratchRemainder);
        _code.Store64FromRegister(scratchRemainder + sizeof(ulong), 2);

        _code.Bytes(0x45, 0x85, 0xC0);
        var remainderCarry = _code.JumpIf(0x85);
        _code.Load64ToRegister(scratchRemainder + sizeof(ulong), 0);
        _code.Load64ToRegister(scratchDivisor + sizeof(ulong), 1);
        _code.Bytes(0x48, 0x39, 0xC8);
        var highGreater = _code.JumpIf(0x87);
        var highLess = _code.JumpIf(0x82);
        _code.Load64ToRegister(scratchRemainder, 0);
        _code.Load64ToRegister(scratchDivisor, 1);
        _code.Bytes(0x48, 0x39, 0xC8);
        var lowAtLeast = _code.JumpIf(0x83);
        var lowLess = _code.JumpIf(0x82);

        var noSubtract = _code.Position;
        _code.Bytes(0x45, 0x31, 0xDB);
        var noSubtractDone = _code.Jump();

        var subtract = _code.Position;
        _code.PatchRelative(remainderCarry, subtract);
        _code.PatchRelative(highGreater, subtract);
        _code.PatchRelative(lowAtLeast, subtract);
        _code.Load64(scratchRemainder);
        _code.Load64ToRegister(scratchDivisor, 1);
        _code.Bytes(0x48, 0x29, 0xC8);
        _code.Store64(scratchRemainder);
        _code.Load64(scratchRemainder + sizeof(ulong), destinationRegister: true);
        _code.Load64ToRegister(scratchDivisor + sizeof(ulong), 1);
        _code.Bytes(0x48, 0x19, 0xCA);
        _code.Store64FromRegister(scratchRemainder + sizeof(ulong), 2);
        _code.Bytes(0x41, 0xBB, 0x01, 0x00, 0x00, 0x00);
        var subtractDone = _code.Jump();

        var quotientBit = _code.Position;
        _code.PatchRelative(highLess, noSubtract);
        _code.PatchRelative(lowLess, noSubtract);
        _code.PatchRelative(noSubtractDone, quotientBit);
        _code.PatchRelative(subtractDone, quotientBit);
        _code.Load64(resultOffset);
        _code.Load64(resultOffset + sizeof(ulong), destinationRegister: true);
        _code.Bytes(0x48, 0x0F, 0xA4, 0xC2, 0x01, 0x48, 0xD1, 0xE0, 0x4C, 0x09, 0xD8);
        _code.Store64(resultOffset);
        _code.Store64FromRegister(resultOffset + sizeof(ulong), 2);

        _code.Bytes(0x41, 0xFF, 0xC9);
        var repeat = _code.JumpIf(0x85);
        _code.PatchRelative(repeat, loop);

        if (isSigned)
        {
            if (operation == TokenKind.Slash)
            {
                _code.Load(scratchSigns);
                _code.Bytes(0x41, 0x89, 0xC2);
                _code.Load(scratchSigns + sizeof(int));
                _code.Bytes(0x41, 0x31, 0xC2, 0x45, 0x85, 0xD2);
                var nonnegativeQuotient = _code.JumpIf(0x84);
                NegateWideIntegerAt(resultOffset);
                _code.PatchRelative(nonnegativeQuotient, _code.Position);
            }
            else
            {
                _code.Load(scratchSigns);
                _code.Bytes(0x85, 0xC0);
                var nonnegativeRemainder = _code.JumpIf(0x84);
                NegateWideIntegerAt(scratchRemainder);
                _code.PatchRelative(nonnegativeRemainder, _code.Position);
                _code.AddressToRdx(resultOffset);
                _code.Address(scratchRemainder);
                _code.CopyMemory(2 * sizeof(ulong));
            }
        }
        else if (operation == TokenKind.Percent)
        {
            _code.AddressToRdx(resultOffset);
            _code.Address(scratchRemainder);
            _code.CopyMemory(2 * sizeof(ulong));
        }

        _code.Address(resultOffset);
        if (specialRemainder >= 0)
        {
            _code.PatchRelative(specialRemainder, _code.Position);
        }
    }

    private void NegateWideIntegerAt(int offset)
    {
        _code.Load64(offset);
        _code.Bytes(0x48, 0xF7, 0xD0, 0x48, 0x83, 0xC0, 0x01);
        _code.Store64(offset);
        _code.Load64(offset + sizeof(ulong), destinationRegister: true);
        _code.Bytes(0x48, 0xF7, 0xD2, 0x48, 0x83, 0xD2, 0x00);
        _code.Store64FromRegister(offset + sizeof(ulong), 2);
    }

    private void EmitWideIntegerShift(TokenKind operation, PrimitiveType type, int valueOffset,
        int countOffset, PrimitiveType countType, int resultOffset)
    {
        if (PrimitiveTypeFacts.IsWideInteger(countType))
        {
            _code.Load64(countOffset + sizeof(ulong));
            _code.Bytes(0x48, 0x85, 0xC0);
            RuntimeBranch(0x85, 2);
            _code.Load64(countOffset);
        }
        else
        {
            LoadValue(countOffset, countType);
        }
        _code.Bytes(0x48, 0x3D);
        _code.Int32(128);
        RuntimeBranch(0x83, 2);
        _code.Bytes(0x48, 0x89, 0xC1);
        _code.Bytes(0x48, 0x83, 0xF9, 0x40);

        var atLeast64 = _code.JumpIf(0x83);
        _code.Load64(valueOffset);
        _code.Load64(valueOffset + sizeof(ulong), destinationRegister: true);
        if (operation == TokenKind.ShiftLeft)
        {
            _code.Bytes(0x48, 0x0F, 0xA5, 0xC2, 0x48, 0xD3, 0xE0);
        }
        else
        {
            _code.Bytes(0x48, 0x0F, 0xAD, 0xD0);
            _code.Bytes(0x48, 0xD3, operation == TokenKind.ShiftRight
                && type == PrimitiveType.Int128 ? (byte)0xFA : (byte)0xEA);
        }
        var done = _code.Jump();
        _code.PatchRelative(atLeast64, _code.Position);
        _code.Bytes(0x48, 0x83, 0xE9, 0x40);
        if (operation == TokenKind.ShiftLeft)
        {
            _code.Load64(valueOffset);
            _code.Bytes(0x48, 0x89, 0xC2, 0x48, 0xD3, 0xE2, 0x31, 0xC0);
        }
        else
        {
            _code.Load64(valueOffset + sizeof(ulong));
            _code.Bytes(0x48, 0x89, 0xC2);
            _code.Bytes(0x48, 0xD3, operation == TokenKind.ShiftRight
                && type == PrimitiveType.Int128 ? (byte)0xFA : (byte)0xEA);
            _code.Bytes(0x48, 0x89, 0xD0);
            if (operation == TokenKind.ShiftRight && type == PrimitiveType.Int128)
            {
                _code.Bytes(0x48, 0xC1, 0xFA, 0x3F);
            }
            else
            {
                _code.Bytes(0x31, 0xD2);
            }
        }
        _code.PatchRelative(done, _code.Position);
        _code.Store64(resultOffset);
        _code.Store64FromRegister(resultOffset + sizeof(ulong), 2);
        _code.Address(resultOffset);
    }

    private void EmitWideIntegerComparison(TokenKind operation, PrimitiveType type,
        int leftOffset, int rightOffset)
    {
        var unsigned = type == PrimitiveType.UInt128;
        _code.Load64ToRegister(leftOffset + sizeof(ulong), 0);
        _code.Load64ToRegister(rightOffset + sizeof(ulong), 1);
        _code.Bytes(0x48, 0x39, 0xC8);
        var highLess = _code.JumpIf(unsigned ? (byte)0x82 : (byte)0x8C);
        var highGreater = _code.JumpIf(unsigned ? (byte)0x87 : (byte)0x8F);

        _code.Load64ToRegister(leftOffset, 0);
        _code.Load64ToRegister(rightOffset, 1);
        _code.Bytes(0x48, 0x39, 0xC8);
        var lowCondition = operation switch {
            TokenKind.EqualsEquals => (byte)0x94,
            TokenKind.BangEquals => (byte)0x95,
            TokenKind.Less => (byte)0x92,
            TokenKind.LessEquals => (byte)0x96,
            TokenKind.Greater => (byte)0x97,
            TokenKind.GreaterEquals => (byte)0x93,
            _ => throw new InvalidOperationException("The verifier admitted an unsupported 128-bit comparison."),
        };
        _code.Bytes(0x0F, lowCondition, 0xC0, 0x0F, 0xB6, 0xC0);
        var lowDone = _code.Jump();

        _code.PatchRelative(highLess, _code.Position);
        _code.Bytes(0xB8);
        _code.Int32(operation is TokenKind.Less or TokenKind.LessEquals or TokenKind.BangEquals ? 1 : 0);
        var lessDone = _code.Jump();

        _code.PatchRelative(highGreater, _code.Position);
        _code.Bytes(0xB8);
        _code.Int32(operation is TokenKind.Greater or TokenKind.GreaterEquals or TokenKind.BangEquals ? 1 : 0);
        var greaterDone = _code.Jump();

        var end = _code.Position;
        _code.PatchRelative(lowDone, end);
        _code.PatchRelative(lessDone, end);
        _code.PatchRelative(greaterDone, end);
    }

    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "The verifier restricts binary operators to the supported native profile.")]
    [SuppressMessage("Style", "IDE0072:Populate switch", Justification = "The verifier excludes unsupported token kinds before reaching this encoder.")]
    private void BinaryWide(TokenKind op, bool unsigned, BoundIntegerOverflowBehavior overflowBehavior)
    {
        switch (op)
        {
            case TokenKind.ShiftLeft:
            case TokenKind.ShiftRight:
            case TokenKind.ShiftRightUnsigned:
                _code.Bytes(0x48, 0x83, 0xF9, 0x40); // cmp rcx,64; unsigned comparison rejects negative or oversized counts
                RuntimeBranch(0x83, 0);
                _code.Bytes(0x48, 0xD3, op == TokenKind.ShiftLeft ? (byte)0xE0
                    : op == TokenKind.ShiftRight && !unsigned ? (byte)0xF8 : (byte)0xE8);
                break;
            case TokenKind.Plus:
            case TokenKind.Minus:
                _code.Bytes(0x48, op == TokenKind.Plus ? (byte)0x01 : (byte)0x29, 0xC8);
                if (overflowBehavior == BoundIntegerOverflowBehavior.Checked)
                {
                    RuntimeBranch(unsigned ? (byte)0x82 : (byte)0x80, 0);
                }
                break;
            case TokenKind.Star:
                if (unsigned)
                {
                    _code.Bytes(0x48, 0xF7, 0xE1); // mul rcx; the upper product must be zero
                    if (overflowBehavior == BoundIntegerOverflowBehavior.Checked)
                    {
                        _code.Bytes(0x48, 0x85, 0xD2);
                        RuntimeBranch(0x85, 0);
                    }
                }
                else
                {
                    _code.Bytes(0x48, 0x0F, 0xAF, 0xC1);
                    if (overflowBehavior == BoundIntegerOverflowBehavior.Checked)
                    {
                        RuntimeBranch(0x80, 0);
                    }
                }
                break;
            case TokenKind.Slash:
            case TokenKind.Percent:
                _code.Bytes(0x48, 0x85, 0xC9);
                RuntimeBranch(0x84, 1);
                if (unsigned)
                {
                    _code.Bytes(0x31, 0xD2, 0x48, 0xF7, 0xF1); // xor edx,edx; div rcx
                }
                else
                {
                    _code.Bytes(0x48, 0xBA);
                    _code.Int64(long.MinValue);
                    _code.Bytes(0x48, 0x39, 0xD0); // cmp rax,rdx
                    var normal = _code.JumpIf(0x85);
                    _code.Bytes(0x48, 0x83, 0xF9, 0xFF); // cmp rcx,-1
                    var normalAgain = _code.JumpIf(0x85);
                    if (op == TokenKind.Slash)
                    {
                        _runtimeCalls.Add((_code.RelativeCall(), 0));
                    }
                    else
                    {
                        _code.Bytes(0x31, 0xC0); // min % -1 is zero
                        var done = _code.Jump();
                        _code.PatchRelative(normal, _code.Position);
                        _code.PatchRelative(normalAgain, _code.Position);
                        _code.Bytes(0x48, 0x99, 0x48, 0xF7, 0xF9); // cqo; idiv rcx
                        _code.Bytes(0x48, 0x89, 0xD0);
                        _code.PatchRelative(done, _code.Position);
                        break;
                    }
                    _code.PatchRelative(normal, _code.Position);
                    _code.PatchRelative(normalAgain, _code.Position);
                    _code.Bytes(0x48, 0x99, 0x48, 0xF7, 0xF9);
                }
                if (op == TokenKind.Percent)
                {
                    _code.Bytes(0x48, 0x89, 0xD0); // mov rax,rdx
                }
                break;
            case TokenKind.Ampersand:
            case TokenKind.Pipe:
            case TokenKind.Caret:
                byte opcode = op switch {
                    TokenKind.Ampersand => 0x21,
                    TokenKind.Pipe => 0x09,
                    _ => 0x31,
                };
                _code.Bytes(0x48, opcode, 0xC8);
                break;
            default:
                _code.Bytes(0x48, 0x39, 0xC8);
                _code.Bytes(0x0F, (byte)(op switch {
                    TokenKind.EqualsEquals => 0x94,
                    TokenKind.BangEquals => 0x95,
                    TokenKind.Less => unsigned ? 0x92 : 0x9C,
                    TokenKind.LessEquals => unsigned ? 0x96 : 0x9E,
                    TokenKind.Greater => unsigned ? 0x97 : 0x9F,
                    TokenKind.GreaterEquals => unsigned ? 0x93 : 0x9D,
                    _ => throw new InvalidOperationException(),
                }), 0xC0);
                _code.Bytes(0x0F, 0xB6, 0xC0);
                break;
        }
    }
}

internal sealed class MachineCode
{
    private readonly List<byte> _bytes = new();
    internal bool UseFramePointer { get; set; }
    internal int Position => _bytes.Count;
    internal void Byte(byte value) => _bytes.Add(value);
    internal void Bytes(params ReadOnlySpan<byte> values) => _bytes.AddRange(values);
    internal void Int32(int value)
    {
        for (var i = 0; i < 4; i++)
        {
            Byte(unchecked((byte)(value >> (8 * i))));
        }
    }
    internal void Int64(long value)
    {
        for (var i = 0; i < 8; i++)
        {
            Byte(unchecked((byte)(value >> (8 * i))));
        }
    }
    internal void PatchInt32(int offset, int value)
    {
        for (var i = 0; i < 4; i++)
        {
            _bytes[offset + i] = unchecked((byte)(value >> (8 * i)));
        }
    }
    internal void PatchRelative(int offset, int target) => PatchInt32(offset, checked(target - offset - 4));
    internal int Jump()
    {
        Byte(0xE9);
        var offset = Position;
        Int32(0);
        return offset;
    }
    internal int JumpIf(byte condition)
    {
        Bytes(0x0F, condition);
        var offset = Position;
        Int32(0);
        return offset;
    }
    internal int RelativeCall()
    {
        Byte(0xE8);
        var offset = Position;
        Int32(0);
        return offset;
    }
    internal int ImportCall()
    {
        Bytes(0xFF, 0x15); // call qword ptr [rip+disp32]
        var offset = Position;
        Int32(0);
        return offset;
    }
    internal int ImportJump()
    {
        Bytes(0xFF, 0x25); // jmp qword ptr [rip+disp32]
        var offset = Position;
        Int32(0);
        return offset;
    }
    internal int DataAddress()
    {
        Bytes(0x48, 0x8D, 0x15); // lea rdx,[rip+disp32]
        var offset = Position;
        Int32(0);
        return offset;
    }
    internal void Load(int offset)
    {
        if (UseFramePointer)
        {
            Bytes(0x8B, 0x85);
        }
        else
        {
            Bytes(0x8B, 0x84, 0x24);
        }
        Int32(offset);
    }
    internal void StoreMxcsr(int offset)
    {
        Bytes(0x0F, 0xAE, UseFramePointer ? (byte)0x9D : (byte)0x9C);
        if (!UseFramePointer)
        {
            Byte(0x24);
        }
        Int32(offset);
    }
    internal void LoadMxcsr(int offset)
    {
        Bytes(0x0F, 0xAE, UseFramePointer ? (byte)0x95 : (byte)0x94);
        if (!UseFramePointer)
        {
            Byte(0x24);
        }
        Int32(offset);
    }
    internal void StoreX87ControlWord(int offset) => X87Memory(0xD9, 0xBD, 0xBC, offset);
    internal void LoadX87ControlWord(int offset) => X87Memory(0xD9, 0xAD, 0xAC, offset);
    internal void LoadX87Value(int offset, bool single) =>
        X87Memory(single ? (byte)0xD9 : (byte)0xDD, 0x85, 0x84, offset);
    internal void StoreX87Value(int offset, bool single) =>
        X87Memory(single ? (byte)0xD9 : (byte)0xDD, 0x9D, 0x9C, offset);
    private void X87Memory(byte opcode, byte framePointerModRm, byte stackPointerModRm, int offset)
    {
        Byte(opcode);
        Byte(UseFramePointer ? framePointerModRm : stackPointerModRm);
        if (!UseFramePointer)
        {
            Byte(0x24);
        }
        Int32(offset);
    }
    internal void Load64(int offset, bool destinationRegister = false)
    {
        if (destinationRegister)
        {
            if (UseFramePointer)
            {
                Bytes(0x48, 0x8B, 0x95);
            }
            else
            {
                Bytes(0x48, 0x8B, 0x94, 0x24);
            }
            Int32(offset);
        }
        else
        {
            Byte(0x48);
            Load(offset);
        }
    }
    internal void Load64ToRegister(int offset, byte register)
    {
        Byte((byte)(0x48 | (((register >> 3) & 1) << 2)));
        Byte(0x8B);
        if (UseFramePointer)
        {
            Byte((byte)(0x85 | ((register & 7) << 3)));
        }
        else
        {
            Bytes((byte)(0x84 | ((register & 7) << 3)), 0x24);
        }
        Int32(offset);
    }
    internal void LoadUnsigned(int offset, int width)
    {
        Bytes(0x0F, width == 1 ? (byte)0xB6 : (byte)0xB7, UseFramePointer ? (byte)0x85 : (byte)0x84);
        if (!UseFramePointer)
        {
            Byte(0x24);
        }
        Int32(offset);
    }
    internal void Address(int offset)
    {
        Bytes(0x48, 0x8D, UseFramePointer ? (byte)0x85 : (byte)0x84);
        if (!UseFramePointer)
        {
            Byte(0x24);
        }
        Int32(offset);
    }
    internal void AddressToRdx(int offset)
    {
        Bytes(0x48, 0x8D, UseFramePointer ? (byte)0x95 : (byte)0x94);
        if (!UseFramePointer)
        {
            Byte(0x24);
        }
        Int32(offset);
    }
    internal void PointerOffset(int offset)
    {
        Bytes(0x48, 0x8D, 0x80);
        Int32(offset);
    }
    internal void LoadPointerField(int offset, int width)
    {
        if (width == 8)
        {
            Byte(0x48);
        }
        if (width is 1 or 2)
        {
            Bytes(0x0F, width == 1 ? (byte)0xB6 : (byte)0xB7);
        }
        else
        {
            Byte(0x8B);
        }
        Byte(0x80);
        Int32(offset);
    }
    internal void CopyMemory(int size)
    {
        var at = 0;
        while (at < size)
        {
            var remaining = size - at;
            var width = remaining >= 8 ? 8 : remaining >= 4 ? 4 : remaining >= 2 ? 2 : 1;
            if (width == 8)
            {
                Byte(0x48);
            }
            else if (width == 2)
            {
                Byte(0x66);
            }
            if (width == 1)
            {
                Bytes(0x0F, 0xB6, 0x88);
            }
            else
            {
                Bytes(0x8B, 0x88);
            }
            Int32(at);
            if (width == 8)
            {
                Byte(0x48);
            }
            else if (width == 2)
            {
                Byte(0x66);
            }
            Bytes(width == 1 ? (byte)0x88 : (byte)0x89, 0x8A);
            Int32(at);
            at += width;
        }
    }
    internal void Store(int offset)
    {
        if (UseFramePointer)
        {
            Bytes(0x89, 0x85);
        }
        else
        {
            Bytes(0x89, 0x84, 0x24);
        }
        Int32(offset);
    }
    internal void Clear(int offset)
    {
        Bytes(0xC7, UseFramePointer ? (byte)0x85 : (byte)0x84);
        if (!UseFramePointer)
        {
            Byte(0x24);
        }
        Int32(offset);
        Int32(0);
    }
    internal void Set(int offset)
    {
        Bytes(0xC7, UseFramePointer ? (byte)0x85 : (byte)0x84);
        if (!UseFramePointer)
        {
            Byte(0x24);
        }
        Int32(offset);
        Int32(1);
    }
    internal void StoreOutgoing64(int offset)
    {
        Bytes(0x48, 0x89, 0x84, 0x24);
        Int32(offset);
    }
    internal void Store64(int offset)
    {
        Byte(0x48);
        Store(offset);
    }
    internal void Store64FromRegister(int offset, byte register)
    {
        Byte((byte)(0x48 | (((register >> 3) & 1) << 2)));
        Byte(0x89);
        if (UseFramePointer)
        {
            Byte((byte)(0x85 | ((register & 7) << 3)));
        }
        else
        {
            Bytes((byte)(0x84 | ((register & 7) << 3)), 0x24);
        }
        Int32(offset);
    }
    internal void StoreField(int offset, int width)
    {
        if (width == 8)
        {
            Byte(0x48);
        }
        if (width == 2)
        {
            Byte(0x66);
        }
        if (width == 1)
        {
            Bytes(0x88, UseFramePointer ? (byte)0x85 : (byte)0x84);
        }
        else
        {
            Bytes(0x89, UseFramePointer ? (byte)0x85 : (byte)0x84);
        }
        if (!UseFramePointer)
        {
            Byte(0x24);
        }
        Int32(offset);
    }
    internal void StorePointerField(int width)
    {
        if (width == 8)
        {
            Bytes(0x48, 0x89, 0x02);
        }
        else if (width == 4)
        {
            Bytes(0x89, 0x02);
        }
        else if (width == 2)
        {
            Bytes(0x66, 0x89, 0x02);
        }
        else if (width == 1)
        {
            Bytes(0x88, 0x02);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
    }
    internal void StoreRegister(int argument, int offset, bool wide)
    {
        if (argument >= 2)
        {
            Byte(wide ? (byte)0x4C : (byte)0x44);
        }
        else if (wide)
        {
            Byte(0x48);
        }
        var register = argument switch {
            0 or 3 => 0x8C,
            1 => 0x94,
            _ => 0x84,
        };
        Bytes(0x89, (byte)(register + (UseFramePointer ? 1 : 0)));
        if (!UseFramePointer)
        {
            Byte(0x24);
        }
        Int32(offset);
    }
    internal void StoreFloatingRegister(int argument, int offset, bool wide)
    {
        if (argument is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(argument));
        }
        Byte(0x66);
        if (wide)
        {
            Byte(0x48);
        }
        Bytes(0x0F, wide ? (byte)0xD6 : (byte)0x7E,
            (byte)((UseFramePointer ? 0x85 : 0x84) + (argument << 3)));
        if (!UseFramePointer)
        {
            Byte(0x24);
        }
        Int32(offset);
    }
    [SuppressMessage("Style", "IDE0010:Populate switch", Justification = "Stack arguments are handled by the caller; only the first four use registers.")]
    internal void MoveArgument(int argument, bool wide)
    {
        switch (argument)
        {
            case 0:
                if (wide)
                {
                    Byte(0x48);
                }
                Bytes(0x89, 0xC1);
                break;
            case 1:
                if (wide)
                {
                    Byte(0x48);
                }
                Bytes(0x89, 0xC2);
                break;
            case 2:
                Bytes(wide ? (byte)0x49 : (byte)0x41, 0x89, 0xC0);
                break;
            case 3:
                Bytes(wide ? (byte)0x49 : (byte)0x41, 0x89, 0xC1);
                break;
        }
    }

    internal void MoveFloatingArgument(int argument, bool wide)
    {
        if (argument is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(argument));
        }
        Byte(0x66);
        if (wide)
        {
            Byte(0x48);
        }
        Bytes(0x0F, 0x6E, (byte)(0xC0 + (argument << 3)));
    }
    internal void MoveFloatingReturnToAccumulator(bool wide)
    {
        Byte(0x66);
        if (wide)
        {
            Byte(0x48);
        }
        Bytes(0x0F, 0x7E, 0xC0);
    }
    internal void MoveFloatingReturnFromAccumulator(bool wide)
    {
        Byte(0x66);
        if (wide)
        {
            Byte(0x48);
        }
        Bytes(0x0F, 0x6E, 0xC0);
    }
    internal void StoreFloating128Return(int offset)
    {
        Bytes(0xF3, 0x0F, 0x7F, UseFramePointer ? (byte)0x85 : (byte)0x84);
        if (!UseFramePointer)
        {
            Byte(0x24);
        }
        Int32(offset);
    }
    internal void LoadFloating128FromPointer() => Bytes(0xF3, 0x0F, 0x6F, 0x00);
    internal byte[] ToArray() => [.. _bytes];
}
