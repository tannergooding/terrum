// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class TrRmExecutableCompiler
{
    internal static byte[] EmitStandardFloatingScalarLibrary(
        BoundProgram program, string moduleIdentity)
        => EmitStandardScalarOperationLibrary(program, moduleIdentity, floating: true);

    internal static byte[] EmitStandardIntegerScalarLibrary(
        BoundProgram program, string moduleIdentity)
        => EmitStandardScalarOperationLibrary(program, moduleIdentity, floating: false);

    private static byte[] EmitStandardScalarOperationLibrary(
        BoundProgram program, string moduleIdentity, bool floating)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrEmpty(moduleIdentity);
        SemanticVerifier.VerifyLibrary(program);

        if (program.EntryPoint != -1 || program.Functions.Length != 1
            || !program.Structures.IsEmpty || !program.Classes.IsEmpty
            || !program.Buffers.IsEmpty || !program.NativeUtf16ZLiterals.IsEmpty
            || !program.NativeUtf8ZLiterals.IsEmpty || program.RequiresSourceGrant
            || program.InvocationProfileId is not null || program.IsInvocationSourceBound
            || !program.NativeFunctionPointerSignatures.IsEmpty
            || !program.NativePointerTypes.IsEmpty
            || program.RuntimePolicy != BoundRuntimePolicy.FailFast)
        {
            throw Unsupported("Standard scalar-operation Body emission requires one self-contained fail-fast library method.");
        }

        var function = program.Functions[0];
        if (!IsStandardScalarOperationFunction(function)
            || function.ParameterCount != 2 || function.Variables.Length != 2
            || function.IsPrivate && function.IsInternal)
        {
            throw Unsupported("Standard scalar-operation Body emission supports one top-level two-parameter scalar function.");
        }

        var typeCode = floating
            ? function.ReturnType switch
            {
                PrimitiveType.Float32 => (byte)0x14,
                PrimitiveType.Float64 => (byte)0x15,
                _ => throw Unsupported("Standard floating Body emission requires float32 or float64."),
            }
            : StandardIntegerScalarTypeCode(function.ReturnType);
        var typeSpecRef = 0xF200_0000u | typeCode;
        var visibility = function.IsPrivate ? 0u : function.IsInternal ? 1u : 2u;
        var body = EncodeStandardScalarOperationBody(function, floating);

        var stringBytes = TrRmStringTableWriter.EncodeTextRows([
            ("", 0), (moduleIdentity, 1), (function.Name, 1),
        ]);
        var strings = new TrRmStringTableReader(stringBytes, 3);
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(2, 1, 0, 0, 1, 1, 1, 1, 0, 0, 1),
        ], strings, 0, 0, 1, 0, 0);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            0, 0, 1, 0, 0);
        var types = default(TrRmTypeSpecTableReader);
        var signatureBytes = TrRmMethodSigTableWriter.Encode([
            new TrRmMethodSigRow(typeSpecRef, 0, 1, 2, 0, 0),
        ], types, 2, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1, types, 2, 0);
        var parameterBytes = TrRmParamTableWriter.Encode([
            new TrRmParamRow(1, 0, typeSpecRef, 0),
            new TrRmParamRow(1, 1, typeSpecRef, 0),
        ], signatures, types);
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, 0);
        var methodBytes = TrRmMethodDefTableWriter.Encode([
            new TrRmMethodDefRow(0, 3, 1, visibility, 0, 0, 1),
        ], strings, modules, typeDefs, signatures, 0, 1);
        var methods = new TrRmMethodDefTableReader(methodBytes, 1, strings, modules,
            typeDefs, signatures, 0, 1);
        var exports = visibility == 2
            ? TrRmExportTableWriter.Encode([
                new TrRmExportRow(0x1600_0001u, 3),
            ], strings, typeDefs, methods, default, default)
            : [];
        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, 3, stringBytes),
            new(0x0016, 0, 1, methodBytes),
            new(0x0017, 0, 1, signatureBytes),
            new(0x0018, 0, 2, parameterBytes),
        };
        if (visibility == 2)
        {
            sections.Add(new TrRmSection(0x001D, 0, 1, exports));
        }
        sections.Add(new TrRmSection(0x0020, 0, 1,
            TrRmVariableTableWriter.EncodeBodies([body])));
        var artifact = TrRmArtifactWriter.Encode(sections);
        TrRmStructuralMetadataValidator.Validate(artifact);
        if (floating)
        {
            _ = LoadStandardFloatingScalarFunction(artifact);
        }
        else
        {
            _ = LoadStandardIntegerScalarFunction(artifact);
        }
        return artifact;
    }

    private static byte StandardIntegerScalarTypeCode(PrimitiveType type)
    {
        var index = Array.IndexOf(s_types, type);
        if (!PrimitiveTypeFacts.IsIntegerOrWide(type)
            || index <= 0 || index >= s_codes.Length)
        {
            throw Unsupported($"Standard integer Body emission does not support {type}.");
        }

        return s_codes[index];
    }

    private static bool IsStandardScalarOperationFunction(BoundFunction function)
    {
        if (function.IsExternal || function.DirectNativeImport is not null
            || function.NativeOutputRole is not null
            || !function.NativeOutputContracts.IsEmpty
            || function.ModuleImport is not null || function.IsModuleReference
            || function.NativeFunctionPointerType is not null
            || function.NativeCallableEntryName is not null
            || function.NativeVTableMethod is not null || function.IndexedBody is not null
            || function.PropertyName is not null || function.IsCleanup || function.IsNoThrow
            || function.RequiresUnsafeCaller || function.DoesNotReturn || function.IsConstructor
            || function.DeclaringStruct is not null || function.IsStatic || function.IsOperator
            || function.Intrinsic != BoundIntrinsic.None || function.IsClassConstructor
            || function.ClassConstructor != -1 || !function.CapturedParameters.IsEmpty
            || function.ReturnsForwardedBacking || !function.ResultDependencyParameters.IsEmpty
            || function.ResultDependencyIncludesReceiver || !function.DependencyContracts.IsEmpty
            || !function.Body.RetiredManagedLocals.IsEmpty
            || function.Body.Statements.Length != 1
            || function.Body.Statements[0] is not BoundReturn { Value: not null })
        {
            return false;
        }

        foreach (var variable in function.Variables)
        {
            if (variable.Type != function.ReturnType || variable.IsMutable
                || variable.IsOut || variable.IsInit || variable.IsImplicitRef
                || variable.IsImplicitReferenceTemporary || variable.IsInitializationLocal
                || variable.NativeFixedArrayLayout is not null)
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] EncodeStandardScalarOperationBody(
        BoundFunction function, bool floating)
    {
        var returned = ((BoundReturn)function.Body.Statements[0]).Value!;
        var code = new byte[8];
        code[0] = 0x11;
        code[1] = 0;
        code[2] = 0x11;
        code[3] = 0x11;

        byte resultRegister;
        if (returned is BoundUnary
            {
                Operator: TokenKind.Minus,
                Operand: BoundRead operand,
            } unary && unary.Type == function.ReturnType
            && (floating
                ? IsFloat(unary.Type)
                    && unary.OverflowBehavior == BoundIntegerOverflowBehavior.Checked
                : PrimitiveTypeFacts.IsIntegerOrWide(unary.Type)
                    && unary.OverflowBehavior == BoundIntegerOverflowBehavior.Wrapping))
        {
            resultRegister = GetParameterRegister(operand, function);
            code[4] = floating ? (byte)0x26 : (byte)0x20;
            code[5] = (byte)((resultRegister << 4) | resultRegister);
        }
        else if (returned is BoundBinary binary
            && binary.Type == function.ReturnType
            && (floating
                ? IsFloat(binary.Type)
                    && binary.OverflowBehavior == BoundIntegerOverflowBehavior.Checked
                    && binary.Operator is TokenKind.Plus or TokenKind.Minus
                        or TokenKind.Star or TokenKind.Slash or TokenKind.Percent
                : PrimitiveTypeFacts.IsIntegerOrWide(binary.Type)
                    && binary.Operator is TokenKind.Plus or TokenKind.Minus
                        or TokenKind.Star or TokenKind.Slash or TokenKind.Percent
                    && (binary.Operator is TokenKind.Plus or TokenKind.Minus or TokenKind.Star
                        ? binary.OverflowBehavior == BoundIntegerOverflowBehavior.Wrapping
                        : binary.OverflowBehavior == BoundIntegerOverflowBehavior.Checked)))
        {
            var left = GetParameterRegister(binary.Left, function);
            var right = GetParameterRegister(binary.Right, function);
            resultRegister = left;
            code[4] = floating ? binary.Operator switch
            {
                TokenKind.Plus => (byte)0x27,
                TokenKind.Minus => (byte)0x28,
                TokenKind.Star => (byte)0x29,
                TokenKind.Slash => (byte)0x2A,
                _ => (byte)0x2B,
            } : binary.Operator switch
            {
                TokenKind.Plus => (byte)0x21,
                TokenKind.Minus => (byte)0x22,
                TokenKind.Star => (byte)0x23,
                TokenKind.Slash => (byte)0x24,
                _ => (byte)0x25,
            };
            code[5] = (byte)((right << 4) | left);
        }
        else
        {
            throw Unsupported("Standard scalar-operation Body emission supports one direct, type-correct unary or binary operation.");
        }

        code[6] = 0x17;
        code[7] = resultRegister;
        var body = new byte[53];
        body[0] = 0x01;
        body[1] = 0x01;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(5), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(9), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(13), (uint)code.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(21), (uint)code.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(33), 8);
        body[37] = 0x01;
        code.CopyTo(body.AsSpan(45));
        _ = new TrRmRegisterBodyReader(body);
        return body;
    }

    private static byte GetParameterRegister(BoundExpression expression, BoundFunction function)
    {
        if (expression is not BoundRead read || read.Type != function.ReturnType
            || (uint)read.Variable >= (uint)function.ParameterCount)
        {
            throw Unsupported("Standard scalar-operation Body emission requires parameter-only operands of the result type.");
        }

        return checked((byte)read.Variable);
    }
}
