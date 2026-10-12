using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class BootstrapInt32MainReturnComponentTests
{
    private static readonly string[] s_frontendComponents = [
        "SourceRange", "CharacterFacts", "Utf16CodeUnits", "Utf16Scalar", "CharacterLiteral",
        "StringEscape", "Token", "ManagedTokenChain", "Scanner", "ManagedScanner",
        "TokenSnapshot", "TokenTriviaSnapshot", "WideIntegerFacts", "IntegerParsing",
        "TokenIntegerSnapshot", "TokenDiagnosticSnapshot", "HexDigitValue",
        "BasedIntegerFloatLiteral", "HexFloatLiteral", "DecimalFloatLiteral",
        "IntegerFloatConversion", "Char16FloatConversion", "ScalarParserTypes",
        "ScalarFrontend", "ScalarFrontendProgram", "ScalarFrontendProjection",
        "ScalarFrontendProgramReader",
    ];

    private static readonly string[] s_bootstrapSourceComponents = [
        "SourceRange", "CharacterFacts", "Utf16CodeUnits", "Utf16Scalar", "CharacterLiteral",
        "StringEscape", "Token", "ManagedTokenChain", "Scanner", "ManagedScanner",
        "TokenSnapshot", "TokenTriviaSnapshot", "WideIntegerFacts", "IntegerParsing",
        "TokenIntegerSnapshot", "TokenDiagnosticSnapshot", "HexDigitValue",
        "BasedIntegerFloatLiteral", "HexFloatLiteral", "DecimalFloatLiteral",
        "IntegerFloatConversion", "Char16FloatConversion",
    ];

    private const string PublishFrontendSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            if (!TryPublishScalarFrontend()) { return 1; }
            return 0;
        }
        """;

    private const string VerifyEmitterSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            bool expected = Host.Expected[0] == 1;
            bool emitted = TryEmitBootstrapInt32MainReturn();
            if (emitted != expected) { return 1; }
            if (!emitted) {
                if (Host.BootstrapCodeLength[0] != 0) { return 2; }
                return 0;
            }
            if (Host.BootstrapCodeLength[0] != Host.Expected[1]) { return 3; }
            for (mut int32 index = 0; index < 16; index = index + 1) {
                if (Host.BootstrapCodeBytes[index] != Host.Expected[index + 2]) {
                    return 4;
                }
                Host.BootstrapCodeSnapshot[index] = Host.BootstrapCodeBytes[index];
            }
            Host.FrontendProgram[2] = 0;
            if (TryEmitBootstrapInt32MainReturn()
                || Host.BootstrapCodeLength[0] != 0) {
                return 5;
            }
            Host.FrontendProgram[2] = 2;
            Host.FrontendProgram[8 + 39] = int32.MaxValue;
            if (TryEmitBootstrapInt32MainReturn()
                || Host.BootstrapCodeLength[0] != 0) {
                return 6;
            }
            return 0;
        }
        """;

    private const string EndToEndBootstrapSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            if (!TryPublishScalarFrontend()) { return -1; }
            if (!TryEmitBootstrapInt32MainReturn()) { return -2; }
            if (!TryWriteBootstrapInt32MainPeImage()) { return -3; }
            return Host.BootstrapImageLength[0];
        }
        """;

    private const string VerifyBootstrapSourceEmitter = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            bool expected = Host.Expected[0] == 1;
            bool emitted = TryEmitBootstrapInt32MainSource();
            if (emitted != expected) { return 1; }
            if (!emitted) {
                if (Host.BootstrapCodeLength[0] != 0) { return 2; }
                return 0;
            }
            if (Host.BootstrapCodeLength[0] != Host.Expected[1]) { return 3; }
            for (mut int32 index = 0; index < Host.Expected.Length - 2; index = index + 1) {
                if (Host.BootstrapCodeBytes[index] != Host.Expected[index + 2]) {
                    return 4;
                }
            }
            return 0;
        }
        """;

    private const string EndToEndBootstrapSourceOnly = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            if (!TryEmitBootstrapInt32MainSource()) { return -1; }
            if (!TryWriteBootstrapInt32MainPeImage()) { return -2; }
            if (TryGetBootstrapInt32MainParameterlessCallChainCountForLength(
                    Host.BootstrapCodeLength[0], init var parameterlessForwardingCount)
                && !VerifyBootstrapParameterlessCallChainPeRejections()) { return -62; }
            if (TryGetBootstrapInt32MainCallChainCountForLength(
                    Host.BootstrapCodeLength[0], init var chainArgumentCount,
                    init var chainMultiplication,
                    init var chainForwardingCount)
                && !VerifyBootstrapCallChainPeRejections()) { return -61; }
            if (TryGetBootstrapInt32MainStackArgumentCountForLength(
                    Host.BootstrapCodeLength[0], init var argumentCount)
                && !VerifyBootstrapStackArgumentPeRejections()) { return -60; }
            if (!TryWriteBootstrapInt32MainPeImage()) { return -6; }
            return Host.BootstrapImageLength[0];
        }

        bool VerifyBootstrapParameterlessCallChainPeRejections() {
            int32 codeLength = Host.BootstrapCodeLength[0];
            if (!TryGetBootstrapInt32MainParameterlessCallChainCountForLength(
                    codeLength, init var forwardingCount)) { return false; }
            int32 helperOffset = (forwardingCount + 1) * 14;
            for (mut int32 offset = 0; offset < helperOffset; offset = offset + 1) {
                if (!BootstrapCallChainMutationRejected(offset)) { return false; }
            }
            if (!BootstrapCallChainMutationRejected(helperOffset)
                || !BootstrapCallChainMutationRejected(codeLength - 1)) { return false; }
            if (codeLength - helperOffset == 11) {
                if (!BootstrapCallChainMutationRejected(helperOffset + 5)) { return false; }
                int32 originalLeft = ReadBootstrapPeInt32(helperOffset + 1);
                int32 originalRight = ReadBootstrapPeInt32(helperOffset + 6);
                int32 originalOpcode = Host.BootstrapCodeBytes[helperOffset + 5];
                Host.BootstrapCodeBytes[helperOffset + 5] = 0x05;
                WriteBootstrapInt32Immediate(helperOffset + 1, int32.MaxValue);
                WriteBootstrapInt32Immediate(helperOffset + 6, 1);
                if (!BootstrapStackArgumentMutationRejected()) { return false; }
                WriteBootstrapInt32Immediate(helperOffset + 1, -1);
                if (!BootstrapStackArgumentMutationRejected()) { return false; }
                WriteBootstrapInt32Immediate(helperOffset + 1, originalLeft);
                WriteBootstrapInt32Immediate(helperOffset + 6, originalRight);
                Host.BootstrapCodeBytes[helperOffset + 5] = originalOpcode;
            }
            int32 originalByte = Host.BootstrapCodeBytes[helperOffset];
            Host.BootstrapCodeBytes[helperOffset] = 0x1B8;
            if (!BootstrapStackArgumentMutationRejected()) { return false; }
            Host.BootstrapCodeBytes[helperOffset] = originalByte;
            Host.BootstrapCodeLength[0] = codeLength - 1;
            if (!BootstrapStackArgumentMutationRejected()) { return false; }
            Host.BootstrapCodeLength[0] = codeLength + 1;
            if (!BootstrapStackArgumentMutationRejected()) { return false; }
            Host.BootstrapCodeLength[0] = int32.MaxValue;
            if (!BootstrapStackArgumentMutationRejected()) { return false; }
            Host.BootstrapCodeLength[0] = codeLength;
            return true;
        }

        bool VerifyBootstrapStackArgumentPeRejections() {
            int32 codeLength = Host.BootstrapCodeLength[0];
            if (!TryGetBootstrapInt32MainStackArgumentCountForLength(
                    codeLength, init var argumentCount)) { return false; }
            mut int32 frameBaseSize = 0x20;
            if (argumentCount > 4) {
                frameBaseSize = frameBaseSize + (argumentCount - 4) * 8;
            }
            int32 stackFrameSize = (frameBaseSize + 23) / 16 * 16 - 8;
            bool largeFrame = stackFrameSize > 0x7F;
            bool probeFrame = stackFrameSize >= 0x1000;
            mut int32 callerOffset = probeFrame ? 0x32 : largeFrame ? 7 : 4;
            mut int32 lastCallerStackStore = -1;
            mut int32 lastArgumentImmediate = -1;
            for (mut int32 argumentIndex = 0; argumentIndex < argumentCount;
                argumentIndex = argumentIndex + 1) {
                mut int32 instructionLength = 5;
                mut int32 immediateOffset = callerOffset + 1;
                if (argumentIndex < 2) {
                    instructionLength = 5;
                    immediateOffset = callerOffset + 1;
                } else if (argumentIndex < 4) {
                    instructionLength = 6;
                    immediateOffset = callerOffset + 2;
                } else {
                    int32 callerStackOffset = 0x20 + (argumentIndex - 4) * 8;
                    instructionLength = callerStackOffset > 0x7F ? 11 : 8;
                    immediateOffset = callerOffset
                        + (callerStackOffset > 0x7F ? 7 : 4);
                    lastCallerStackStore = callerOffset + 3;
                }
                if (argumentIndex + 1 == argumentCount) {
                    lastArgumentImmediate = immediateOffset;
                }
                callerOffset = callerOffset + instructionLength;
            }
            int32 callOffset = callerOffset;
            int32 helperOffset = callOffset + 5 + (largeFrame ? 8 : 5);
            bool helperSubtraction = argumentCount > 1
                && Host.BootstrapCodeBytes[helperOffset + 2] == 0x29;
            bool helperMultiplication = argumentCount > 1
                && Host.BootstrapCodeBytes[helperOffset + 2] == 0x0F;
            mut int32 argumentOffset = probeFrame ? 0x32 : largeFrame ? 7 : 4;
            mut int32 prefixResult = 0;
            for (mut int32 argumentIndex = 0; argumentIndex < argumentCount;
                argumentIndex = argumentIndex + 1) {
                mut int32 instructionLength = 5;
                mut int32 immediateOffset = argumentOffset + 1;
                if (argumentIndex < 2) {
                    instructionLength = 5;
                } else if (argumentIndex < 4) {
                    instructionLength = 6;
                    immediateOffset = argumentOffset + 2;
                } else {
                    int32 callerStackOffset = 0x20 + (argumentIndex - 4) * 8;
                    instructionLength = callerStackOffset > 0x7F ? 11 : 8;
                    immediateOffset = argumentOffset
                        + (callerStackOffset > 0x7F ? 7 : 4);
                }
                if (argumentIndex + 1 < argumentCount) {
                    int32 argument = ReadBootstrapPeInt32(immediateOffset);
                    if (argumentIndex == 0) {
                        prefixResult = argument;
                    } else if (helperSubtraction) {
                        prefixResult = prefixResult - argument;
                    } else if (helperMultiplication) {
                        prefixResult = prefixResult * argument;
                    } else {
                        prefixResult = prefixResult + argument;
                    }
                }
                argumentOffset = argumentOffset + instructionLength;
            }
            mut int32 stackReadOffset = helperOffset + 2;
            mut int32 lastHelperStackLoad = -1;
            for (mut int32 argumentIndex = 1; argumentIndex < argumentCount;
                argumentIndex = argumentIndex + 1) {
                if (helperMultiplication && argumentIndex == 1) {
                    stackReadOffset = stackReadOffset + 3;
                } else if (helperMultiplication && argumentIndex < 4) {
                    stackReadOffset = stackReadOffset + 4;
                } else if (helperMultiplication) {
                    int32 helperStackOffset = 0x28 + (argumentIndex - 4) * 8;
                    lastHelperStackLoad = stackReadOffset + 4;
                    stackReadOffset = stackReadOffset
                        + (helperStackOffset > 0x7F ? 8 : 5);
                } else if (argumentIndex == 1) {
                    stackReadOffset = stackReadOffset + 2;
                } else if (argumentIndex < 4) {
                    stackReadOffset = stackReadOffset + 3;
                } else {
                    int32 helperStackOffset = 0x28 + (argumentIndex - 4) * 8;
                    lastHelperStackLoad = stackReadOffset + 3;
                    stackReadOffset = stackReadOffset
                        + (helperStackOffset > 0x7F ? 7 : 4);
                }
            }

            mut int32 original = Host.BootstrapCodeBytes[0];
            Host.BootstrapCodeBytes[0] = original ^ 1;
            if (!BootstrapStackArgumentMutationRejected()) { return false; }
            Host.BootstrapCodeBytes[0] = original;

            original = Host.BootstrapCodeBytes[3];
            Host.BootstrapCodeBytes[3] = original ^ 1;
            if (!BootstrapStackArgumentMutationRejected()) { return false; }
            Host.BootstrapCodeBytes[3] = original;

            original = Host.BootstrapCodeBytes[callOffset + 1];
            Host.BootstrapCodeBytes[callOffset + 1] = original ^ 1;
            if (!BootstrapStackArgumentMutationRejected()) { return false; }
            Host.BootstrapCodeBytes[callOffset + 1] = original;

            if (lastCallerStackStore >= 0) {
                original = Host.BootstrapCodeBytes[lastCallerStackStore];
                Host.BootstrapCodeBytes[lastCallerStackStore] = original ^ 1;
                if (!BootstrapStackArgumentMutationRejected()) { return false; }
                Host.BootstrapCodeBytes[lastCallerStackStore] = original;
                original = Host.BootstrapCodeBytes[lastHelperStackLoad];
                Host.BootstrapCodeBytes[lastHelperStackLoad] = original ^ 1;
                if (!BootstrapStackArgumentMutationRejected()) { return false; }
                Host.BootstrapCodeBytes[lastHelperStackLoad] = original;
            }

            if (helperMultiplication) {
                for (mut int32 multiplyByte = helperOffset + 2;
                    multiplyByte < helperOffset + 5; multiplyByte = multiplyByte + 1) {
                    original = Host.BootstrapCodeBytes[multiplyByte];
                    Host.BootstrapCodeBytes[multiplyByte] = original ^ 1;
                    if (!BootstrapStackArgumentMutationRejected()) { return false; }
                    Host.BootstrapCodeBytes[multiplyByte] = original;
                }
            }

            original = Host.BootstrapCodeBytes[codeLength - 1];
            Host.BootstrapCodeBytes[codeLength - 1] = original ^ 1;
            if (!BootstrapStackArgumentMutationRejected()) { return false; }
            Host.BootstrapCodeBytes[codeLength - 1] = original;

            bool canForceArithmeticOverflow = helperMultiplication
                ? prefixResult > 1 || prefixResult < -1
                : helperSubtraction
                ? prefixResult > 0 || prefixResult < -1
                : prefixResult > 0 || prefixResult < 0;
            if (canForceArithmeticOverflow) {
                int32 originalArgument = ReadBootstrapPeInt32(lastArgumentImmediate);
                int32 overflowArgument = helperMultiplication
                    ? prefixResult > 0 ? int32.MaxValue : int32.MinValue
                    : helperSubtraction
                        ? prefixResult > 0 ? int32.MinValue : int32.MaxValue
                        : prefixResult > 0 ? int32.MaxValue : int32.MinValue;
                WriteBootstrapInt32Immediate(lastArgumentImmediate,
                    overflowArgument);
                if (!BootstrapStackArgumentMutationRejected()) { return false; }
                WriteBootstrapInt32Immediate(lastArgumentImmediate, originalArgument);
            }

            for (mut int32 length = codeLength - 1;
                length <= codeLength + 1; length = length + 1) {
                if (length != codeLength) {
                    Host.BootstrapCodeLength[0] = length;
                    if (!BootstrapStackArgumentMutationRejected()) { return false; }
                }
            }
            Host.BootstrapCodeLength[0] = codeLength;
            return true;
        }

        bool BootstrapStackArgumentMutationRejected() {
            return !TryWriteBootstrapInt32MainPeImage()
                && Host.BootstrapImageLength[0] == 0;
        }

        bool VerifyBootstrapCallChainPeRejections() {
            int32 codeLength = Host.BootstrapCodeLength[0];
            if (!TryGetBootstrapInt32MainCallChainCountForLength(
                    codeLength, init var argumentCount,
                    init var multiplication, init var forwardingCount)
                || !TryGetBootstrapInt32MainCallChainLayoutForDepth(
                    argumentCount, multiplication, forwardingCount,
                    init var mainFunctionEnd,
                    init var forwardingFunctionEnd, init var expectedCodeLength)
                || expectedCodeLength != codeLength) {
                return false;
            }
            mut int32 frameBaseSize = 0x20;
            if (argumentCount > 4) {
                frameBaseSize = frameBaseSize + (argumentCount - 4) * 8;
            }
            int32 stackFrameSize = (frameBaseSize + 23) / 16 * 16 - 8;
            int32 epilogueLength = stackFrameSize > 0x7F ? 8 : 5;
            int32 prologLength = stackFrameSize >= 0x1000 ? 0x32
                : stackFrameSize > 0x7F ? 7 : 4;
            int32 mainCallOffset = mainFunctionEnd - 5 - epilogueLength;
            int32 forwardingLength = forwardingFunctionEnd - mainFunctionEnd;

            if (!BootstrapCallChainMutationRejected(0)
                || !BootstrapCallChainMutationRejected(mainCallOffset + 1)
                || !BootstrapCallChainMutationRejected(prologLength)) {
                return false;
            }
            for (mut int32 forwardingIndex = 0;
                forwardingIndex < forwardingCount;
                forwardingIndex = forwardingIndex + 1) {
                int32 forwardingStart = mainFunctionEnd
                    + forwardingIndex * forwardingLength;
                int32 forwardingCallOffset = forwardingStart
                    + forwardingLength - 5 - epilogueLength;
                if (!BootstrapCallChainMutationRejected(forwardingStart)
                    || !BootstrapCallChainMutationRejected(
                        forwardingCallOffset + 1)
                    || !BootstrapCallChainMutationRejected(
                        forwardingStart + forwardingLength - 1)) {
                    return false;
                }
                if (argumentCount > 4
                    && !BootstrapCallChainMutationRejected(
                        forwardingStart + prologLength)) {
                    return false;
                }
            }
            for (mut int32 length = codeLength - 1;
                length <= codeLength + 1; length = length + 1) {
                if (length != codeLength) {
                    Host.BootstrapCodeLength[0] = length;
                    if (!BootstrapStackArgumentMutationRejected()) { return false; }
                }
            }
            Host.BootstrapCodeLength[0] = codeLength;
            return true;
        }

        bool BootstrapCallChainMutationRejected(int32 codeOffset) {
            if (codeOffset < 0
                || codeOffset >= Host.BootstrapCodeLength[0]) {
                return false;
            }
            mut int32 original = Host.BootstrapCodeBytes[codeOffset];
            Host.BootstrapCodeBytes[codeOffset] = original ^ 1;
            bool rejected = !TryWriteBootstrapInt32MainPeImage()
                && Host.BootstrapImageLength[0] == 0;
            Host.BootstrapCodeBytes[codeOffset] = original;
            return rejected;
        }
        """;

    private const string RejectBootstrapSourcePublication = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            if (TryEmitBootstrapInt32MainSource()
                || Host.BootstrapCodeLength[0] != 0) { return 1; }
            if (TryWriteBootstrapInt32MainPeImage()
                || Host.BootstrapImageLength[0] != 0) { return 2; }
            return 0;
        }
        """;

    private const string RejectOverCapacityBootstrapPeSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            if (!TryEmitBootstrapInt32MainSource()
                || Host.BootstrapCodeLength[0] < 1) { return 1; }
            Host.BootstrapImageBytes[0] = 123;
            if (TryWriteBootstrapInt32MainPeImage()
                || Host.BootstrapImageLength[0] != 0
                || Host.BootstrapImageBytes[0] != 0) { return 2; }
            return 0;
        }
        """;

    private const string VerifyBootstrapStackProbeFrameLimitsSource = """
        namespace Terrum.CodeAnalysis;
        int32 Main() {
            if (TryGetBootstrapInt32MainSourceStackArgumentCodeLength(
                    65536, false, init var unsupportedSourceLength)) { return 1; }
            if (TryGetBootstrapInt32MainPeStackArgumentCodeLength(
                    65536, false, init var unsupportedPeLength)) { return 2; }
            return 0;
        }
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 0; void Helper() { }", true, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 0; void Helper() { }", true, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 0x12345678; void Helper() { }", true, 0x12345678)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 0x12345678; void Helper() { }", true, 0x12345678)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 2147483647; void Helper() { }", true, int.MaxValue)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 2147483647; void Helper() { }", true, int.MaxValue)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int64 Main() => 42; void Helper() { }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int64 Main() => 42; void Helper() { }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main(int32 value) => value; void Helper() { }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main(int32 value) => value; void Helper() { }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => -1; void Helper() { }", true, -1)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => -1; void Helper() { }", true, -1)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => -0x12345678; void Helper() { }", true, -0x12345678)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => -0x12345678; void Helper() { }", true, -0x12345678)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => -2147483648; void Helper() { }", true, int.MinValue)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => -2147483648; void Helper() { }", true, int.MinValue)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 40 + 2; void Helper() { }", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 40 + 2; void Helper() { }", true, 42)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 40 - 2; void Helper() { }", true, 38)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 40 - 2; void Helper() { }", true, 38)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 6 * 7; void Helper() { }", true, 42, 6, 7)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 6 * 7; void Helper() { }", true, 42, 6, 7)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 0 * 0; void Helper() { }", true, 0, 0, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 0 * 0; void Helper() { }", true, 0, 0, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 50000 * 50000; void Helper() { }", false, 0, 50000, 50000)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 50000 * 50000; void Helper() { }", false, 0, 50000, 50000)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 40 / 2; void Helper() { }", true, 20)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 40 / 2; void Helper() { }", true, 20)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 40 % 3; void Helper() { }", true, 1)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 40 % 3; void Helper() { }", true, 1)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 40 / 0; void Helper() { }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 40 / 0; void Helper() { }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 2147483647 + 1; void Helper() { }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 2147483647 + 1; void Helper() { }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { return 42; } void Helper() { }", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { return 42; } void Helper() { }", true, 42)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { return -2147483648; } void Helper() { }", true, int.MinValue)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { return -2147483648; } void Helper() { }", true, int.MinValue)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { return 40 + 2; } void Helper() { }", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { return 40 + 2; } void Helper() { }", true, 42)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 42; return value; } void Helper() { }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 42; return value; } void Helper() { }", false, 0)]
    public async Task EmitsOnlySupportedMainLiteralLeafBytesAsync(
        BoundRuntimePolicy policy, string sourceText, bool shouldEmit, int value,
        int firstOperand = 0, int secondOperand = 0)
    {
        var source = SourceText.From(sourceText);
        var publication = await PublishAndAuditFrontendAsync(source, policy).ConfigureAwait(true);
        var expected = CreateExpectedBytes(shouldEmit, value,
            sourceText.Contains(" + ", StringComparison.Ordinal),
            sourceText.Contains(" - ", StringComparison.Ordinal),
            sourceText.Contains(" * ", StringComparison.Ordinal),
            sourceText.Contains(" / ", StringComparison.Ordinal),
            sourceText.Contains(" % ", StringComparison.Ordinal),
            firstOperand, secondOperand);
        var buffers = new[]
        {
            new HostBuffer("Source", PrimitiveType.Char16, [.. source.Text], false),
            // The harness corrupts this audited copy after success to check failure publication.
            new HostBuffer("FrontendProgram", PrimitiveType.Int32,
                [.. ReadWords(publication)], true),
            new HostBuffer("Expected", PrimitiveType.Int32, [.. expected], false),
            new HostBuffer("BootstrapCodeBytes", PrimitiveType.Int32,
                [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], true),
            new HostBuffer("BootstrapCodeLength", PrimitiveType.Int32, [0], true),
            new HostBuffer("BootstrapCodeSnapshot", PrimitiveType.Int32,
                [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], true),
        };
        var trees = await ReadComponentsAsync(
            ["BootstrapInt32MainLeaf", "BootstrapInt32MainReturn"], VerifyEmitterSource)
            .ConfigureAwait(true);
        var compilation = Compilation.Create(trees, buffers.ToImmutableArray(), policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);

        var jit = NativeJit.ExecuteWithData(program, (NativeRuntimePolicy)policy);
        Assert.Equal(0, jit.ExitCode);
        Assert.Equal(0, await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true));

        var lengthOffset = jit.BufferOffsets[4];
        Assert.Equal(0, ReadInt32(jit.Data, lengthOffset));
        if (shouldEmit)
        {
            var snapshotOffset = jit.BufferOffsets[5];
            var actual = Enumerable.Range(0, 16)
                .Select(index => ReadInt32(jit.Data, snapshotOffset + index * sizeof(int)));
            Assert.Equal(expected.Skip(2), actual);
        }
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 42; void Helper() { }", 42, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 42; void Helper() { }", 42, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 40 + 2; void Helper() { }", 42, 1)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 40 + 2; void Helper() { }", 42, 1)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 40 - 2; void Helper() { }", 38, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 40 - 2; void Helper() { }", 38, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 6 * 7; void Helper() { }", 42, 3, 6, 7)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 6 * 7; void Helper() { }", 42, 3, 6, 7)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 0 * 0; void Helper() { }", 0, 3, 0, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 0 * 0; void Helper() { }", 0, 3, 0, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 40 / 2; void Helper() { }", 20, 4)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 40 / 2; void Helper() { }", 20, 4)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 40 % 3; void Helper() { }", 1, 5)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 40 % 3; void Helper() { }", 1, 5)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { return 42; } void Helper() { }", 42, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { return 42; } void Helper() { }", 42, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { return 40 + 2; } void Helper() { }", 42, 1)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { return 40 + 2; } void Helper() { }", 42, 1)]
    public async Task TerrumFrontendEmitsAndRunsBootstrapPeImageAsync(
        BoundRuntimePolicy policy, string sourceText, int value, int operation,
        int firstOperand = 0, int secondOperand = 0)
    {
        var source = SourceText.From(sourceText);
        var components = s_frontendComponents
            .Where(static name => name != "ScalarFrontendProgramReader")
            .Append("BootstrapInt32MainLeaf")
            .Append("BootstrapInt32MainReturn")
            .Append("BootstrapInt32MainPeImage");
        var trees = await ReadComponentsAsync(components, EndToEndBootstrapSource)
            .ConfigureAwait(true);
        var buffers = ScalarFrontendHost.CreateBuffers(source)
            .Add(new HostBuffer("BootstrapCodeBytes", PrimitiveType.Int32,
                [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], true))
            .Add(new HostBuffer("BootstrapCodeLength", PrimitiveType.Int32, [0], true))
            .Add(new HostBuffer("BootstrapImageBytes", PrimitiveType.Int32,
                [.. new int[1536]], true))
            .Add(new HostBuffer("BootstrapImageLength", PrimitiveType.Int32, [0], true));
        var compilation = Compilation.Create(trees, buffers, policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);

        var jit = NativeJit.ExecuteWithData(program, (NativeRuntimePolicy)policy);
        const int expectedImageLength = 0x600;
        var expected = CreateExpectedBytes(true, value,
            operation == 1, operation == 2, operation == 3,
            operation == 4, operation == 5, firstOperand, secondOperand);
        Assert.Equal(expectedImageLength, jit.ExitCode);
        AssertPublishedFrontendAudits(source, jit, policy);
        AssertOutputCode(jit, buffers.Length - 4, buffers.Length - 3, expected);
        var image = ReadBootstrapPeImage(
            jit, buffers.Length - 2, buffers.Length - 1, expected);
        Assert.Equal(value, await ExecuteImageAsync(image).ConfigureAwait(true));
        Assert.Equal(expectedImageLength, await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 42;", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 42;", true, 42)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 42; public int32 Main() => Answer();", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 42; public int32 Main() => Answer();", true, 42)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => -2147483648; public int32 Main() => Answer();", true, int.MinValue)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => -2147483648; public int32 Main() => Answer();", true, int.MinValue)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 40 + 2; public int32 Main() => Answer();", true, 42, 40, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 40 + 2; public int32 Main() => Answer();", true, 42, 40, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 40 - 2; public int32 Main() => Answer();", true, 38, 40, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 40 - 2; public int32 Main() => Answer();", true, 38, 40, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer(int32 value) => value; public int32 Main() => Answer(42);", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer(int32 value) => value; public int32 Main() => Answer(42);", true, 42)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer(int32 value) => value; public int32 Main() { int32 result = 40 + 2; return Answer(result); }", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer(int32 value) => value; public int32 Main() { int32 result = 40 + 2; return Answer(result); }", true, 42)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Add(int32 left, int32 right) => left + right; public int32 Main() => Add(40, 2);", true, 42, 40, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Add(int32 left, int32 right) => left + right; public int32 Main() => Add(40, 2);", true, 42, 40, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Add(int32 left, int32 right) => right + left; public int32 Main() { int32 first = 20; int32 second = 22; return Add(first, second); }", true, 42, 20, 22)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Add(int32 left, int32 right) => right + left; public int32 Main() { int32 first = 20; int32 second = 22; return Add(first, second); }", true, 42, 20, 22)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() => Sum(20, 20, 2);", true, 42, 20, 20, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() => Sum(20, 20, 2);", true, 42, 20, 20, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() => Sum(-2, -3, -2);", true, -7, -2, -3, -2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() => Sum(-2, -3, -2);", true, -7, -2, -3, -2)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum4(int32 first, int32 second, int32 third, int32 fourth) => first + second + third + fourth; public int32 Main() => Sum4(1, 2, 4, 8);", true, 15, 1, 2, 4, 8)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum4(int32 first, int32 second, int32 third, int32 fourth) => first + second + third + fourth; public int32 Main() => Sum4(1, 2, 4, 8);", true, 15, 1, 2, 4, 8)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum5(int32 first, int32 second, int32 third, int32 fourth, int32 fifth) => first + second + third + fourth + fifth; public int32 Main() => Sum5(1, 2, 4, 8, 16);", true, 31, 1, 2, 4, 8, 16)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum5(int32 first, int32 second, int32 third, int32 fourth, int32 fifth) => first + second + third + fourth + fifth; public int32 Main() => Sum5(-2, -3, -5, -7, -11);", true, -28, -2, -3, -5, -7, -11)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum5(int32 first, int32 second, int32 third, int32 fourth, int32 fifth) => first + second + third + fourth + fifth; public int32 Main() { int32 firstValue = 1; int32 fifthValue = 16; return Sum5(firstValue, 2, 4, 8, fifthValue); }", true, 31, 1, 2, 4, 8, 16)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum6(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth) => first + second + third + fourth + fifth + sixth; public int32 Main() => Sum6(1, 2, 4, 8, 16, 32);", true, 63, 1, 2, 4, 8, 16, 32)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum6(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth) => first + second + third + fourth + fifth + sixth; public int32 Main() => Sum6(-2, -3, -5, -7, -11, -13);", true, -41, -2, -3, -5, -7, -11, -13)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum6(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth) => first + second + third + fourth + fifth + sixth; public int32 Main() { int32 firstValue = 1; int32 sixthValue = 32; return Sum6(firstValue, 2, 4, 8, 16, sixthValue); }", true, 63, 1, 2, 4, 8, 16, 32)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum6(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth) => first + second + third + fourth + fifth + sixth; public int32 Main() { int32 fifthValue = 16; int32 sixthValue = 32; return Sum6(1, 2, 4, 8, fifthValue, sixthValue); }", true, 63, 1, 2, 4, 8, 16, 32)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum7(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh) => first + second + third + fourth + fifth + sixth + seventh; public int32 Main() => Sum7(1, 2, 4, 8, 16, 32, 64);", true, 127, 1, 2, 4, 8, 16, 32, 64)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum7(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh) => first + second + third + fourth + fifth + sixth + seventh; public int32 Main() => Sum7(-2, -3, -5, -7, -11, -13, -17);", true, -58, -2, -3, -5, -7, -11, -13, -17)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum7(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh) => first + second + third + fourth + fifth + sixth + seventh; public int32 Main() { int32 fifthValue = 16; int32 seventhValue = 64; return Sum7(1, 2, 4, 8, fifthValue, 32, seventhValue); }", true, 127, 1, 2, 4, 8, 16, 32, 64)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum8(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth) => first + second + third + fourth + fifth + sixth + seventh + eighth; public int32 Main() => Sum8(1, 2, 4, 8, 16, 32, 64, 128);", true, 255, 1, 2, 4, 8, 16, 32, 64, 128)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum8(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth) => first + second + third + fourth + fifth + sixth + seventh + eighth; public int32 Main() => Sum8(-2, -3, -5, -7, -11, -13, -17, -19);", true, -77, -2, -3, -5, -7, -11, -13, -17, -19)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum8(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth) => first + second + third + fourth + fifth + sixth + seventh + eighth; public int32 Main() { int32 fifthValue = 16; int32 eighthValue = 128; return Sum8(1, 2, 4, 8, fifthValue, 32, 64, eighthValue); }", true, 255, 1, 2, 4, 8, 16, 32, 64, 128)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum9(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth; public int32 Main() => Sum9(1, 2, 4, 8, 16, 32, 64, 128, 256);", true, 511, 1, 2, 4, 8, 16, 32, 64, 128, 256)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum9(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth; public int32 Main() => Sum9(1, 2, 3, 4, 5, 6, 7, 8, 9);", true, 45, 1, 2, 3, 4, 5, 6, 7, 8, 9)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum9(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth; public int32 Main() => Sum9(-2, -3, -5, -7, -11, -13, -17, -19, -23);", true, -103, -2, -3, -5, -7, -11, -13, -17, -19, -23)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum9(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth; public int32 Main() { int32 fifthValue = 16; int32 ninthValue = 256; return Sum9(1, 2, 4, 8, fifthValue, 32, 64, 128, ninthValue); }", true, 511, 1, 2, 4, 8, 16, 32, 64, 128, 256)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum10(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth; public int32 Main() => Sum10(1, 2, 4, 8, 16, 32, 64, 128, 256, 512);", true, 1023, 1, 2, 4, 8, 16, 32, 64, 128, 256, 512)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum10(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth; public int32 Main() => Sum10(-2, -3, -5, -7, -11, -13, -17, -19, -23, -29);", true, -129, -2, -3, -5, -7, -11, -13, -17, -19, -23, -29)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum10(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth; public int32 Main() { int32 fifthValue = 16; int32 tenthValue = 512; return Sum10(1, 2, 4, 8, fifthValue, 32, 64, 128, 256, tenthValue); }", true, 1023, 1, 2, 4, 8, 16, 32, 64, 128, 256, 512)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum11(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh; public int32 Main() => Sum11(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);", true, 66, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum11(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh; public int32 Main() => Sum11(-2, -3, -5, -7, -11, -13, -17, -19, -23, -29, -31);", true, -160, -2, -3, -5, -7, -11, -13, -17, -19, -23, -29, -31)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum11(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh; public int32 Main() { int32 eleventhValue = 11; return Sum11(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, eleventhValue); }", true, 66, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum12(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth; public int32 Main() => Sum12(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);", true, 78, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum12(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth; public int32 Main() => Sum12(-2, -3, -5, -7, -11, -13, -17, -19, -23, -29, -31, -37);", true, -197, -2, -3, -5, -7, -11, -13, -17, -19, -23, -29, -31, -37)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum12(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth; public int32 Main() { int32 twelfthValue = 12; return Sum12(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, twelfthValue); }", true, 78, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum13(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth; public int32 Main() => Sum13(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13);", true, 91, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum13(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth; public int32 Main() => Sum13(-2, -3, -5, -7, -11, -13, -17, -19, -23, -29, -31, -37, -41);", true, -238, -2, -3, -5, -7, -11, -13, -17, -19, -23, -29, -31, -37, -41)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum13(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth; public int32 Main() { int32 thirteenthValue = 13; return Sum13(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, thirteenthValue); }", true, 91, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum14(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth; public int32 Main() => Sum14(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14);", true, 105, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum14(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth; public int32 Main() => Sum14(-2, -3, -5, -7, -11, -13, -17, -19, -23, -29, -31, -37, -41, -43);", true, -281, -2, -3, -5, -7, -11, -13, -17, -19, -23, -29, -31, -37, -41, -43)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum14(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth; public int32 Main() { int32 fourteenthValue = 14; return Sum14(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, fourteenthValue); }", true, 105, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum15(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth + fifteenth; public int32 Main() => Sum15(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);", true, 120, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum15(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth + fifteenth; public int32 Main() => Sum15(-2, -3, -5, -7, -11, -13, -17, -19, -23, -29, -31, -37, -41, -43, -47);", true, -328, -2, -3, -5, -7, -11, -13, -17, -19, -23, -29, -31, -37, -41, -43, -47)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum15(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth + fifteenth; public int32 Main() { int32 fifteenthValue = 15; return Sum15(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, fifteenthValue); }", true, 120, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum16(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth, int32 sixteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth + fifteenth + sixteenth; public int32 Main() => Sum16(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16);", true, 136, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum17(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth, int32 sixteenth, int32 seventeenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth + fifteenth + sixteenth + seventeenth; public int32 Main() => Sum17(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17);", true, 153, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum16(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth, int32 sixteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth + fifteenth + sixteenth; public int32 Main() => Sum16(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2147483647);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum17(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth, int32 sixteenth, int32 seventeenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth + fifteenth + sixteenth + seventeenth; public int32 Main() => Sum17(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2147483647);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum18(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth, int32 sixteenth, int32 seventeenth, int32 eighteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth + fifteenth + sixteenth + seventeenth + eighteenth; public int32 Main() => Sum18(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18);", true, 171, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum18(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth, int32 sixteenth, int32 seventeenth, int32 eighteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth + fifteenth + sixteenth + seventeenth + eighteenth; public int32 Main() => Sum18(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2147483647);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum18(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth, int32 sixteenth, int32 seventeenth, int32 eighteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth + fifteenth + sixteenth + seventeenth + eighteenth; public int32 Main() => Sum18(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, -2147483648);", true, -2147483631, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, int.MinValue)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() { int32 one = 20; int32 two = 20; return Sum(one, two, 2); }", true, 42, 20, 20, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() { int32 one = 20; int32 two = 20; return Sum(one, two, 2); }", true, 42, 20, 20, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer(int32 value) => value; public int32 Main() { int32 first = 20; int32 result = first + 22; return Answer(result); }", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer(int32 value) => value; public int32 Main() { int32 first = 20; int32 result = first + 22; return Answer(result); }", true, 42)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer(int32 value) => value; public int32 Main() { int32 result = 42; return Answer(-result); }", true, -42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer(int32 value) => value; public int32 Main() { int32 result = 42; return Answer(-result); }", true, -42)]
    [InlineData(BoundRuntimePolicy.FailFast, "public /* header */ int32 Main() => 42; // trailing", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public /* header */ int32 Main() => 42; // trailing", true, 42)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { return 40 + 2; }", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { return 40 + 2; }", true, 42)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 42; return value; }", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 42; return value; }", true, 42)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 answer = 40 + 2; return answer; }", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 answer = 40 + 2; return answer; }", true, 42)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 40; return value + 2; }", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 40; return value + 2; }", true, 42)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 2; return 40 + value; }", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 2; return 40 + value; }", true, 42)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => -2147483648;", true, int.MinValue)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => -2147483648;", true, int.MinValue)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => -0x12345678;", true, -0x12345678)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => -0x12345678;", true, -0x12345678)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 40 - 2;", true, 38)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 40 - 2;", true, 38)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 40 / 2;", true, 20)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 40 / 2;", true, 20)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 40 % 3;", true, 1)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 40 % 3;", true, 1)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 40 / 0;", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 40 / 0;", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 0x2A;", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 0x2A;", true, 42)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 2147483648;", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 2147483648;", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => -2147483649;", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => -2147483649;", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 42; void Helper() { }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 42; void Helper() { }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main(int32 value) => value;", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main(int32 value) => value;", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { mut int32 value = 42; return value; }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { mut int32 value = 42; return value; }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 42; return other; }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 42; return other; }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 42; return -value; }", true, -42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 42; return -value; }", true, -42)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = -2147483648; return -value; }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = -2147483648; return -value; }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 40; return 42; }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 40; return 42; }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 42; value = 41; return value; }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 42; value = 41; return value; }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 40; int32 value = 2; return value; }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 40; int32 value = 2; return value; }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 left = 40; int32 right = 2; int32 extra = 0; return left + right; }", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 left = 40; int32 right = 2; int32 extra = 0; return left + right; }", true, 42)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 left = 2147483647; int32 right = left + 1; return right; }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 left = 2147483647; int32 right = left + 1; return right; }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 left = 40; int32 right = 0; return left / right; }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 left = 40; int32 right = 0; return left / right; }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 2147483647 + 1; public int32 Main() => Answer();", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 2147483647 + 1; public int32 Main() => Answer();", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 42; public int32 Main() => Other();", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 42; public int32 Main() => Other();", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 40 + 2; public int32 Main() { return Answer(); }", true, 42, 40, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 40 + 2; public int32 Main() { return Answer(); }", true, 42, 40, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 40 + 2; public int32 Main() { int32 value = 7; return Answer(); }", true, 42, 40, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 40 + 2; public int32 Main() { int32 value = 7; return Answer(); }", true, 42, 40, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 40 + 2; public int32 Main() { int32 first = 7; int32 second = first + 1; return Answer(); }", true, 42, 40, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 40 + 2; public int32 Main() { int32 first = 7; int32 second = first + 1; return Answer(); }", true, 42, 40, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 42; public int32 Main() { int32 first = 1; int32 second = first; int32 third = 3; return Answer(); }", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 42; public int32 Main() { int32 first = 1; int32 second = first; int32 third = 3; return Answer(); }", true, 42)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 42; public int32 Main() { int32 first = 2147483647 + 1; return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 42; public int32 Main() { int32 first = 2147483647 + 1; return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 42; public int32 Main() { int32 first = 1 / 0; return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 42; public int32 Main() { int32 first = 1 / 0; return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 42; public int32 Main() { int32 value = 7; value = 8; return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 42; public int32 Main() { int32 value = 7; value = 8; return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 42; public int32 Main() { return Other(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 42; public int32 Main() { return Other(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 42; public int32 Main() { int32 value = 2147483647 + 1; return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 42; public int32 Main() { int32 value = 2147483647 + 1; return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 42; public int32 Main() { int32 value = 1 / 0; return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 42; public int32 Main() { int32 value = 1 / 0; return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 42; public int32 Main() { int32 Answer = 1; return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 42; public int32 Main() { int32 Answer = 1; return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 42; public int32 Main() { mut int32 value = 1; return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 42; public int32 Main() { mut int32 value = 1; return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 42; public int32 Main() { int32 value = 1; int32 value = 2; return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 42; public int32 Main() { int32 value = 1; int32 value = 2; return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 42; public int32 Main() { int32 value = Other(); return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 42; public int32 Main() { int32 value = Other(); return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 42; public int32 Main(int32 input) { return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 42; public int32 Main(int32 input) { return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer(int32 input) => 42; public int32 Main() { return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer(int32 input) => 42; public int32 Main() { return Answer(); }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer(int32 value) => value; public int32 Main() => Answer();", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer(int32 value) => value; public int32 Main() => Answer();", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer(int32 value) => value; public int32 Main() => Answer(1, 2);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer(int32 value) => value; public int32 Main() => Answer(1, 2);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer(int32 value) => value; public int32 Main() => Answer(40 + 2);", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer(int32 value) => value; public int32 Main() => Answer(40 + 2);", true, 42)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Add(int32 left, int32 right) => left + right; public int32 Main() => Add(1);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Add(int32 left, int32 right) => left + right; public int32 Main() => Add(1);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Add(int32 left, int32 right) => left + right; public int32 Main() => Add(1, 2, 3);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Add(int32 left, int32 right) => left + right; public int32 Main() => Add(1, 2, 3);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Add(int32 left, int32 right) => left + right; public int32 Main() => Add(2147483647, 1);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Add(int32 left, int32 right) => left + right; public int32 Main() => Add(2147483647, 1);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Add(int32 left, int32 right) => left + right; public int32 Main() => Add(-2147483648, -1);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Add(int32 left, int32 right) => left + right; public int32 Main() => Add(-2147483648, -1);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Add(int32 left, int32 right) => left - right; public int32 Main() => Add(40, 2);", true, 38, 40, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Add(int32 left, int32 right) => left - right; public int32 Main() => Add(40, 2);", true, 38, 40, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() => Sum(1, 2);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() => Sum(1, 2);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() => Sum(1, 2, 3, 4);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() => Sum(1, 2, 3, 4);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum(int32 first, int32 second, int32 third) => first - second + third; public int32 Main() => Sum(1, 2, 3);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum(int32 first, int32 second, int32 third) => first - second + third; public int32 Main() => Sum(1, 2, 3);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() => Sum(2147483647, 1, -1);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() => Sum(2147483647, 1, -1);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() => Sum(2147483646, 0, 2);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() => Sum(2147483646, 0, 2);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() => Sum(-2147483647, 0, -2);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() => Sum(-2147483647, 0, -2);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum4(int32 first, int32 second, int32 third, int32 fourth) => first + second + third + fourth; public int32 Main() => Sum4(2147483646, 0, 0, 2);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum4(int32 first, int32 second, int32 third, int32 fourth) => first + second + third + fourth; public int32 Main() => Sum4(2147483646, 0, 0, 2);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum5(int32 first, int32 second, int32 third, int32 fourth, int32 fifth) => first + second + third + fourth + fifth; public int32 Main() => Sum5(-2147483647, 0, 0, 0, -2);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum5(int32 first, int32 second, int32 third, int32 fourth, int32 fifth) => first + second + third + fourth + fifth; public int32 Main() => Sum5(-2147483647, 0, 0, 0, -2);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum6(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth) => first + second + third + fourth + fifth + sixth; public int32 Main() => Sum6(2147483647, 1, -1, 0, 0, 0);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum6(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth) => first + second + third + fourth + fifth + sixth; public int32 Main() => Sum6(2147483647, 0, 0, 0, 0, 1);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum6(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth) => first + second + third + fourth + fifth + sixth; public int32 Main() => Sum6(-2147483648, 0, 0, 0, 0, -1);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum6(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth) => first + second + third + fourth + fifth + sixth; public int32 Main() => Sum6(1, 2, 3, 4, 5);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum6(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth) => first + second + third + fourth + fifth + sixth; public int32 Main() => Sum6(1, 2, 3, 4, 5, 6, 7);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum7(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh) => first + second + third + fourth + fifth + sixth + seventh; public int32 Main() => Sum7(2147483647, 0, 0, 0, 0, 0, 1);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum7(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh) => first + second + third + fourth + fifth + sixth + seventh; public int32 Main() => Sum7(-2147483648, 0, 0, 0, 0, 0, -1);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum8(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth) => first + second + third + fourth + fifth + sixth + seventh + eighth; public int32 Main() => Sum8(2147483647, 0, 0, 0, 0, 0, 0, 1);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum8(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth) => first + second + third + fourth + fifth + sixth + seventh + eighth; public int32 Main() => Sum8(-2147483648, 0, 0, 0, 0, 0, 0, -1);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum9(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth; public int32 Main() => Sum9(2147483393, 2, 4, 8, 16, 32, 64, 128, 256);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum9(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth; public int32 Main() => Sum9(-2147483393, -2, -4, -8, -16, -32, -64, -128, -256);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum9(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth; public int32 Main() => Sum9(2147483647, 0, 0, 0, 0, 0, 0, 0, 1);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum9(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth; public int32 Main() => Sum9(-2147483648, 0, 0, 0, 0, 0, 0, 0, -1);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum10(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth; public int32 Main() => Sum10(2147483640, 0, 0, 0, 0, 0, 0, 0, 0, 8);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum10(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth; public int32 Main() => Sum10(-2147483641, 0, 0, 0, 0, 0, 0, 0, 0, -8);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum11(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh; public int32 Main() => Sum11(2147483640, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum11(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh; public int32 Main() => Sum11(-2147483641, 0, 0, 0, 0, 0, 0, 0, 0, 0, -8);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum12(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth; public int32 Main() => Sum12(2147483640, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum12(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth; public int32 Main() => Sum12(-2147483641, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -8);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum13(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth; public int32 Main() => Sum13(2147483640, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum13(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth; public int32 Main() => Sum13(-2147483641, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -8);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum14(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth; public int32 Main() => Sum14(2147483640, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum14(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth; public int32 Main() => Sum14(-2147483641, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -8);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum15(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth + fifteenth; public int32 Main() => Sum15(2147483640, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum15(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth + fifteenth; public int32 Main() => Sum15(-2147483641, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -8);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum7(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh) => first + second + third + fourth + fifth + sixth + seventh; public int32 Main() => Sum7(1, 2, 3, 4, 5, 6);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum7(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh) => first + second + third + fourth + fifth + sixth + seventh; public int32 Main() => Sum7(1, 2, 3, 4, 5, 6, 7, 8);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum8(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth) => first + second + third + fourth + fifth + sixth + seventh + eighth; public int32 Main() => Sum8(1, 2, 3, 4, 5, 6, 7);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum8(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth) => first + second + third + fourth + fifth + sixth + seventh + eighth; public int32 Main() => Sum8(1, 2, 3, 4, 5, 6, 7, 8, 9);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum8(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth) => first + second + third + fourth + fifth + sixth + eighth + seventh; public int32 Main() => Sum8(1, 2, 3, 4, 5, 6, 7, 8);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum9(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth; public int32 Main() => Sum9(1, 2, 3, 4, 5, 6, 7, 8);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum9(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth; public int32 Main() => Sum9(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum10(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth; public int32 Main() => Sum10(1, 2, 3, 4, 5, 6, 7, 8, 9);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum10(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth; public int32 Main() => Sum10(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum11(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh; public int32 Main() => Sum11(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum11(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh; public int32 Main() => Sum11(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum12(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth; public int32 Main() => Sum12(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum12(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth; public int32 Main() => Sum12(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum13(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth; public int32 Main() => Sum13(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum13(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth; public int32 Main() => Sum13(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum14(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth; public int32 Main() => Sum14(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum14(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth; public int32 Main() => Sum14(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum15(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth + fifteenth; public int32 Main() => Sum15(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum15(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth + fifteenth; public int32 Main() => Sum15(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum5(int32 first, int32 second, int32 third, int32 fourth, int32 fifth) => first + second + third + fifth + fourth; public int32 Main() => Sum5(1, 2, 3, 4, 5);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum5(int32 first, int32 second, int32 third, int32 fourth, int32 fifth) => first + second + third + fifth + fourth; public int32 Main() => Sum5(1, 2, 3, 4, 5);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum5(int32 first, int32 second, int32 third, int32 fourth, int32 fifth) => first + second + third + fourth + fifth; public int32 Main() => Sum5(1, 2, 3, 4);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum5(int32 first, int32 second, int32 third, int32 fourth, int32 fifth) => first + second + third + fourth + fifth; public int32 Main() => Sum5(1, 2, 3, 4);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum5(int32 first, int32 second, int32 third, int32 fourth, int32 fifth) => first + second + third + fourth + fifth; public int32 Main() => Sum5(1, 2, 3, 4, 5, 6);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum5(int32 first, int32 second, int32 third, int32 fourth, int32 fifth) => first + second + third + fourth + fifth; public int32 Main() => Sum5(1, 2, 3, 4, 5, 6);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer(int32 value) => 42; public int32 Main() => Answer(42);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer(int32 value) => 42; public int32 Main() => Answer(42);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer(int32 first, int32 second) => first; public int32 Main() => Answer(42);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer(int32 first, int32 second) => first; public int32 Main() => Answer(42);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 Answer() => 42; public int32 Main() => Answer();", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 Answer() => 42; public int32 Main() => Answer();", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Answer() => 42; public int32 Main() => Answer();", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Answer() => 42; public int32 Main() => Answer();", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int64 Answer() => 42; public int32 Main() => Answer();", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int64 Answer() => 42; public int32 Main() => Answer();", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 42; /*", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 42; /*", false, 0)]
    public async Task EmitsOnlyTheDirectSourceSubsetAsync(
        BoundRuntimePolicy policy, string sourceText, bool shouldEmit, int value,
        int firstOperand = 0, int secondOperand = 0, int thirdOperand = 0,
        int fourthOperand = 0, int fifthOperand = 0, int sixthOperand = 0,
        int seventhOperand = 0, int eighthOperand = 0, int ninthOperand = 0,
        int tenthOperand = 0, int eleventhOperand = 0, int twelfthOperand = 0,
        int thirteenthOperand = 0, int fourteenthOperand = 0, int fifteenthOperand = 0,
        int sixteenthOperand = 0, int seventeenthOperand = 0, int eighteenthOperand = 0,
        int nineteenthOperand = 0, int twentiethOperand = 0, int twentyFirstOperand = 0,
        int twentySecondOperand = 0, int twentyThirdOperand = 0, int twentyFourthOperand = 0,
        int twentyFifthOperand = 0, int twentySixthOperand = 0,
        int[]? helperArguments = null, bool foldMainExpression = false,
        int bootstrapMainLocalNameCapacity = -1, int bootstrapMainLocalValueCapacity = -1)
    {
        var source = SourceText.From(sourceText);
        var codeCapacity = helperArguments is not null
            ? GetStackArgumentCodeLength(helperArguments.Length)
            : 240;
        var mainDeclarationStart = sourceText.IndexOf(
            "public int32 Main", StringComparison.Ordinal);
        var mainBodyStart = mainDeclarationStart >= 0
            ? sourceText.IndexOf('{', mainDeclarationStart)
            : -1;
        var mainReturnStart = mainBodyStart >= 0
            ? sourceText.IndexOf("return ", mainBodyStart, StringComparison.Ordinal)
            : -1;
        var returnExpression = mainReturnStart >= 0
            ? sourceText[(mainReturnStart + "return ".Length)..].TrimStart()
            : string.Empty;
        var returnOpenParen = returnExpression.IndexOf('(');
        var returnOperator = returnExpression.IndexOfAny(['+', '-', '*', '/', '%', ';']);
        var returnsCall = returnExpression.Length > 0
            && (char.IsLetter(returnExpression[0]) || returnExpression[0] == '_')
            && returnOpenParen > 0
            && (returnOperator < 0 || returnOpenParen < returnOperator);
        var sourceSizedLeaf = shouldEmit && !returnsCall
            && mainReturnStart > mainBodyStart
            && sourceText[mainBodyStart..mainReturnStart]
                .Split("int32 ", StringSplitOptions.None).Length >= 4;
        var helperSubtraction = mainDeclarationStart >= 0
            && sourceText[..mainDeclarationStart]
                .Contains(" - ", StringComparison.Ordinal);
        var preserveDirectOperation = !sourceSizedLeaf && !foldMainExpression;
        var expected = CreateExpectedBytes(shouldEmit, value,
            preserveDirectOperation && sourceText.Contains(" + ", StringComparison.Ordinal),
            preserveDirectOperation && sourceText.Contains(" - ", StringComparison.Ordinal),
            preserveDirectOperation && sourceText.Contains(" * ", StringComparison.Ordinal),
            preserveDirectOperation && sourceText.Contains(" / ", StringComparison.Ordinal),
            preserveDirectOperation && sourceText.Contains(" % ", StringComparison.Ordinal),
            firstOperand, secondOperand, thirdOperand, fourthOperand, fifthOperand,
            sixthOperand: sixthOperand,
            helperCall: helperArguments is not null
                || sourceText.Contains("Main() => Answer(", StringComparison.Ordinal)
                || sourceText.Contains("return Answer(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Add(", StringComparison.Ordinal)
                || sourceText.Contains("return Add(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum4(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum4(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum5(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum5(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum6(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum6(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum7(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum7(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum8(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum8(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum9(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum9(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum10(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum10(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum11(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum11(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum12(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum12(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum13(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum13(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum14(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum14(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum15(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum15(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum16(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum16(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum17(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum17(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum18(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum18(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum19(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum19(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum20(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum20(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum21(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum21(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum22(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum22(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum23(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum23(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum24(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum24(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum25(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum25(", StringComparison.Ordinal)
                || sourceText.Contains("Main() => Sum26(", StringComparison.Ordinal)
                || sourceText.Contains("return Sum26(", StringComparison.Ordinal),
            helperArgument: sourceText.Contains("Answer(int32 ", StringComparison.Ordinal),
            helperTwoArguments: sourceText.Contains("Add(int32 left, int32 right)", StringComparison.Ordinal),
            helperThreeArguments: sourceText.Contains(
                "Sum(int32 first, int32 second, int32 third)", StringComparison.Ordinal),
            helperFourArguments: sourceText.Contains(
                "Sum4(int32 first, int32 second, int32 third, int32 fourth)",
                StringComparison.Ordinal),
            helperFiveArguments: sourceText.Contains(
                "Sum5(int32 first, int32 second, int32 third, int32 fourth, int32 fifth)",
                StringComparison.Ordinal),
            helperSixArguments: sourceText.Contains(
                "Sum6(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth)",
                StringComparison.Ordinal),
            helperSevenArguments: sourceText.Contains(
                "Sum7(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh)",
                StringComparison.Ordinal),
            helperEightArguments: sourceText.Contains(
                "Sum8(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth)",
                StringComparison.Ordinal),
            helperNineArguments: sourceText.Contains(
                "Sum9(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth)",
                StringComparison.Ordinal),
            helperTenArguments: sourceText.Contains(
                "Sum10(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth)",
                StringComparison.Ordinal),
            helperElevenArguments: sourceText.Contains(
                "Sum11(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh)",
                StringComparison.Ordinal),
            helperTwelveArguments: sourceText.Contains(
                "Sum12(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth)",
                StringComparison.Ordinal),
            helperThirteenArguments: sourceText.Contains(
                "Sum13(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth)",
                StringComparison.Ordinal),
            helperFourteenArguments: sourceText.Contains(
                "Sum14(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth)",
                StringComparison.Ordinal),
            helperFifteenArguments: sourceText.Contains(
                "Sum15(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth)",
                StringComparison.Ordinal),
            helperSixteenArguments: sourceText.Contains(
                "Sum16(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth, int32 sixteenth)",
                StringComparison.Ordinal),
            helperSeventeenArguments: sourceText.Contains(
                "Sum17(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth, int32 sixteenth, int32 seventeenth)",
                StringComparison.Ordinal),
            helperEighteenArguments: sourceText.Contains(
                "Sum18(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth, int32 sixteenth, int32 seventeenth, int32 eighteenth)",
                StringComparison.Ordinal),
            helperNineteenArguments: sourceText.Contains("Sum19(", StringComparison.Ordinal),
            helperTwentyArguments: sourceText.Contains("Sum20(", StringComparison.Ordinal),
            helperTwentyOneArguments: sourceText.Contains("Sum21(", StringComparison.Ordinal),
            helperTwentyTwoArguments: sourceText.Contains("Sum22(", StringComparison.Ordinal),
            helperTwentyThreeArguments: sourceText.Contains("Sum23(", StringComparison.Ordinal),
            helperTwentyFourArguments: sourceText.Contains("Sum24(", StringComparison.Ordinal),
            helperTwentyFiveArguments: sourceText.Contains("Sum25(", StringComparison.Ordinal),
            helperTwentySixArguments: sourceText.Contains("Sum26(", StringComparison.Ordinal),
            seventhOperand: seventhOperand,
            eighthOperand: eighthOperand,
            ninthOperand: ninthOperand,
            tenthOperand: tenthOperand,
            eleventhOperand: eleventhOperand,
            twelfthOperand: twelfthOperand,
            thirteenthOperand: thirteenthOperand,
            fourteenthOperand: fourteenthOperand,
            fifteenthOperand: fifteenthOperand,
            sixteenthOperand: sixteenthOperand,
            seventeenthOperand: seventeenthOperand,
            eighteenthOperand: eighteenthOperand,
            nineteenthOperand: nineteenthOperand,
            twentiethOperand: twentiethOperand,
            twentyFirstOperand: twentyFirstOperand,
            twentySecondOperand: twentySecondOperand,
            twentyThirdOperand: twentyThirdOperand,
            twentyFourthOperand: twentyFourthOperand,
            twentyFifthOperand: twentyFifthOperand,
            twentySixthOperand: twentySixthOperand,
            codeCapacity: codeCapacity,
            helperArgumentCount: helperArguments?.Length ?? 0,
            helperArguments: helperArguments,
            helperSubtraction: helperSubtraction);
        var sourceBuffers = CreateBootstrapSourceBuffers(source);
        if (bootstrapMainLocalNameCapacity >= 0 || bootstrapMainLocalValueCapacity >= 0)
        {
            var localNameCapacity = bootstrapMainLocalNameCapacity >= 0
                ? bootstrapMainLocalNameCapacity
                : source.Length;
            var localValueCapacity = bootstrapMainLocalValueCapacity >= 0
                ? bootstrapMainLocalValueCapacity
                : source.Length;
            sourceBuffers = sourceBuffers
                .Where(static buffer => buffer.Name != "BootstrapMainLocalNames"
                    && buffer.Name != "BootstrapMainLocalValues")
                .ToImmutableArray()
                .Add(new HostBuffer("BootstrapMainLocalNames", PrimitiveType.Int32,
                    [.. new int[localNameCapacity]], true))
                .Add(new HostBuffer("BootstrapMainLocalValues", PrimitiveType.Int32,
                    [.. new int[localValueCapacity]], true));
        }
        var buffers = sourceBuffers
            .Add(new HostBuffer("Expected", PrimitiveType.Int32, [.. expected], false))
            .Add(new HostBuffer("BootstrapCodeBytes", PrimitiveType.Int32,
                [.. new int[codeCapacity]], true))
            .Add(new HostBuffer("BootstrapCodeLength", PrimitiveType.Int32, [0], true));
        var components = s_bootstrapSourceComponents
            .Append("BootstrapInt32MainSource")
            .Append("BootstrapInt32MainLeaf");
        var trees = await ReadComponentsAsync(components, VerifyBootstrapSourceEmitter)
            .ConfigureAwait(true);
        var compilation = Compilation.Create(trees, buffers, policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);

        var jit = NativeJit.ExecuteWithData(program, (NativeRuntimePolicy)policy);
        Assert.Equal(0, jit.ExitCode);
        Assert.Equal(0, await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 40 + 2 * 3 - 2;", true, 44)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 40 + 2 * 3 - 2;", true, 44)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => (40 + 2) * 3;", true, 126)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => (40 + 2) * 3;", true, 126)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 40 + -(2 * 3);", true, 34)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 40 + -(2 * 3);", true, 34)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => -1 + 2;", true, 1)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => -1 + 2;", true, 1)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 40 % 7 + 2 * 3;", true, 11)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 40 % 7 + 2 * 3;", true, 11)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { return 40 + 2 * 3; }", true, 46)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { return 40 + 2 * 3; }", true, 46)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 2147483647 + 1 - 1;", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 2147483647 + 1 - 1;", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 40 / (2 - 2);", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 40 / (2 - 2);", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => (-2147483648) / -1;", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => (-2147483648) / -1;", false, 0)]
    public Task BootstrapDirectMainFoldsBoundedExpressionsAsync(
        BoundRuntimePolicy policy, string sourceText, bool shouldEmit, int value)
    {
        return EmitsOnlyTheDirectSourceSubsetAsync(
            policy, sourceText, shouldEmit, value,
            foldMainExpression: true);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 40 + 2 * 3; return value; }", true, 46)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 40 + 2 * 3; return value; }", true, 46)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = (40 + 2) * 3; return value; }", true, 126)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = (40 + 2) * 3; return value; }", true, 126)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 first = 20; int32 second = (first + 2) * 2; return second; }", true, 44)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 first = 20; int32 second = (first + 2) * 2; return second; }", true, 44)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 40; return value + 2 * 3; }", true, 46)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 40; return value + 2 * 3; }", true, 46)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 40; int32 next = value + 1; return next + value + 2; }", true, 83)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 40; int32 next = value + 1; return next + value + 2; }", true, 83)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = -1; return value + 2; }", true, 1)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = -1; return value + 2; }", true, 1)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 first = 20; int32 second = 22; return (first + second) * 3; }", true, 126)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 first = 20; int32 second = 22; return (first + second) * 3; }", true, 126)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 40; return value + -(2 * 3); }", true, 34)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 40; return value + -(2 * 3); }", true, 34)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 2147483647 + 1 - 1; return value; }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 2147483647 + 1 - 1; return value; }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 first = 1; int32 second = 1 / (first - first); return second; }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 first = 1; int32 second = 1 / (first - first); return second; }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 40; return 40 + 2 * 3; }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 40; return 40 + 2 * 3; }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 40; return missing + value; }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 40; return missing + value; }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 first = second; int32 second = 2; return second; }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 first = second; int32 second = 2; return second; }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 first = 1; int32 second = second + first; return second; }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 first = 1; int32 second = second + first; return second; }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 first = 1; int32 first = 2; return first + 1; }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 first = 1; int32 first = 2; return first + 1; }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 first = 1; return first + 2147483647 + 1; }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 first = 1; return first + 2147483647 + 1; }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 first = 1; return first + 1 / (first - first); }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 first = 1; return first + 1 / (first - first); }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = -2147483648; return -value; }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = -2147483648; return -value; }", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = -2147483648; return value / -1; }", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = -2147483648; return value / -1; }", false, 0)]
    public Task BootstrapDirectMainLocalsFoldBoundedExpressionsAsync(
        BoundRuntimePolicy policy, string sourceText, bool shouldEmit, int value)
    {
        return EmitsOnlyTheDirectSourceSubsetAsync(
            policy, sourceText, shouldEmit, value, foldMainExpression: true);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task BootstrapDirectMainLocalExpressionsScaleGroupingToScannerLimitAsync(
        BoundRuntimePolicy policy)
    {
        const string prefix = "public int32 Main() { int32 value = 40; return -(";
        const string innerExpression = "value + 2";
        const string suffix = "); }";
        var groupingDepth = (ScannerHost.MaximumSourceLength
            - prefix.Length - innerExpression.Length - suffix.Length) / 2;
        var sourceText = prefix
            + new string('(', groupingDepth)
            + innerExpression
            + new string(')', groupingDepth)
            + suffix;
        Assert.InRange(sourceText.Length, 1, ScannerHost.MaximumSourceLength);
        var excessiveSource = prefix
            + new string('(', groupingDepth + 1)
            + innerExpression
            + new string(')', groupingDepth + 1)
            + suffix;
        Assert.True(excessiveSource.Length > ScannerHost.MaximumSourceLength);
        return EmitsOnlyTheDirectSourceSubsetAsync(
            policy, sourceText, shouldEmit: true, value: -42, foldMainExpression: true);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task BootstrapDirectMainLocalExpressionsRespectScannerSourceLimitAsync(
        BoundRuntimePolicy policy)
    {
        const string body =
            "public int32 Main() { int32 value = 40; return value + 2 * 3;";
        const string suffix = " }";
        const bool shouldEmit = true;
        var sourceLength = ScannerHost.MaximumSourceLength;
        var paddingLength = sourceLength - body.Length - suffix.Length;
        Assert.True(paddingLength >= 0);
        var sourceText = body + new string(' ', paddingLength) + suffix;
        Assert.Equal(sourceLength, sourceText.Length);
        return EmitsOnlyTheDirectSourceSubsetAsync(
            policy, sourceText, shouldEmit, shouldEmit ? 46 : 0,
            foldMainExpression: true);
    }

    [Fact]
    public void BootstrapSourceBufferFactoryRejectsBeyondScannerSourceLimit()
    {
        var source = SourceText.From(new string(' ', ScannerHost.MaximumSourceLength + 1));
        _ = Assert.Throws<ArgumentException>(() => CreateBootstrapSourceBuffers(source));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, true, false)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true, false)]
    [InlineData(BoundRuntimePolicy.FailFast, false, false)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false, false)]
    [InlineData(BoundRuntimePolicy.FailFast, true, true)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true, true)]
    [InlineData(BoundRuntimePolicy.FailFast, false, true)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false, true)]
    public Task BootstrapDirectMainLocalExpressionRejectsInsufficientTableCapacityAsync(
        BoundRuntimePolicy policy, bool shortNameTable, bool twoLocals)
    {
        string sourceText = twoLocals
            ? "public int32 Main() { int32 value = 40; int32 next = value + 1; return next + 2 * 3; }"
            : "public int32 Main() { int32 value = 40; return value + 2 * 3; }";
        var source = SourceText.From(sourceText);
        return EmitsOnlyTheDirectSourceSubsetAsync(
            policy, sourceText, shouldEmit: false, value: 0,
            foldMainExpression: true,
            bootstrapMainLocalNameCapacity: shortNameTable ? 0 : source.Length,
            bootstrapMainLocalValueCapacity: shortNameTable ? source.Length : 0);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task BootstrapParameterlessHelperSupportsSourceSizedMainLocalsAsync(
        BoundRuntimePolicy policy)
    {
        const int localCount = 512;
        const int nestedGroupCount = 166;
        var nestedExpression = string.Join(" ", Enumerable.Repeat("(", nestedGroupCount))
            + " local1 + 5 "
            + string.Join(" ", Enumerable.Repeat(")", nestedGroupCount))
            + " + 0";
        var declarations = string.Join(" ", Enumerable.Range(0, localCount)
            .Select(index => index switch
            {
                0 => "int32 local0 = 1;",
                2 => $"int32 local2 = {nestedExpression};",
                _ => $"int32 local{index} = local{index - 1} + 1;",
            }));
        var sourceText =
            $"internal int32 Answer() => 40 + 2; public int32 Main() {{ {declarations} "
            + "return Answer(); }";
        Assert.Equal(16_384, sourceText.Length);
        return EmitsOnlyTheDirectSourceSubsetAsync(
            policy, sourceText, shouldEmit: true, value: 42, firstOperand: 40, secondOperand: 2);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "40 * 2", true, 80)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "40 * 2", true, 80)]
    [InlineData(BoundRuntimePolicy.FailFast, "40 + 2 * 3 - 2", true, 44)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "40 + 2 * 3 - 2", true, 44)]
    [InlineData(BoundRuntimePolicy.FailFast, "(40 + 2) * 3", true, 126)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "(40 + 2) * 3", true, 126)]
    [InlineData(BoundRuntimePolicy.FailFast, "40 + -(2 * 3)", true, 34)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "40 + -(2 * 3)", true, 34)]
    [InlineData(BoundRuntimePolicy.FailFast, "40 / (2 + 3) + 7 % 4", true, 11)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "40 / (2 + 3) + 7 % 4", true, 11)]
    [InlineData(BoundRuntimePolicy.FailFast, "2147483647 + 1 - 1", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "2147483647 + 1 - 1", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "40 / (2 - 2)", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "40 / (2 - 2)", false, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "(-2147483648) / -1", false, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "(-2147483648) / -1", false, 0)]
    public Task BootstrapParameterlessHelperFoldsBoundedExpressionsAsync(
        BoundRuntimePolicy policy, string helperExpression, bool shouldEmit, int value)
    {
        var sourceText =
            $"internal int32 Answer() => {helperExpression}; public int32 Main() => Answer();";
        return EmitsOnlyTheDirectSourceSubsetAsync(
            policy, sourceText, shouldEmit, value, foldMainExpression: true);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task BootstrapParameterlessHelperFoldsMaximumSourceBoundedGroupingAsync(
        BoundRuntimePolicy policy)
    {
        const string prefix = "internal int32 Answer() => ";
        const string suffix = "; public int32 Main() => Answer();";
        var remaining = ScannerHost.MaximumSourceLength - prefix.Length - suffix.Length - 2;
        var nestingDepth = remaining / 2;
        var paddingLength = remaining - (nestingDepth * 2);
        var expression = new string('(', nestingDepth) + "40" + new string(')', nestingDepth);
        var sourceText = prefix + expression + new string(' ', paddingLength) + suffix;
        Assert.Equal(ScannerHost.MaximumSourceLength, sourceText.Length);
        var beyondSource = prefix + new string('(', nestingDepth + 1) + "40"
            + new string(')', nestingDepth + 1) + new string(' ', paddingLength) + suffix;
        Assert.Equal(ScannerHost.MaximumSourceLength + 2, beyondSource.Length);
        Assert.Throws<ArgumentException>(() => CreateBootstrapSourceBuffers(SourceText.From(beyondSource)));
        return EmitsOnlyTheDirectSourceSubsetAsync(
            policy, sourceText, shouldEmit: true, value: 40, foldMainExpression: true);
    }

    public static IEnumerable<object?[]> BootstrapParameterlessCallChainCases()
    {
        foreach (var policy in new[] {
            BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions,
        })
        {
            foreach (var depth in new[] { 1, 3 })
            {
                yield return [policy, depth, "42", 42, null, 0, false];
                yield return [policy, depth, "40 + 2", 42, 40, 2, false];
                yield return [policy, depth, "2 - 40", -38, 2, 40, true];
                yield return [policy, depth, "(40 + 2) * 3 - -(7 % 4)", 129, null, 0, false];
                yield return [policy, depth, "(-2147483648) + 0", int.MinValue, null, 0, false];
            }
        }
    }

    [Theory]
    [MemberData(nameof(BootstrapParameterlessCallChainCases))]
    public Task BootstrapParameterlessCallChainsExecuteAndUnwindAsync(
        BoundRuntimePolicy policy, int forwardingCount, string expression,
        int value, int? leftOperand, int rightOperand, bool subtraction)
    {
        var sourceText = CreateBootstrapParameterlessCallChainSource(
            expression, forwardingCount, blockMain: forwardingCount == 3);
        if (forwardingCount == 3)
        {
            sourceText = "namespace Bootstrap.Example; " + sourceText;
        }
        return AssertBootstrapParameterlessCallChainAsync(
            policy, sourceText, forwardingCount, value, leftOperand, rightOperand, subtraction);
    }

    public static IEnumerable<object?[]> BootstrapParameterlessHelperBlockLocalCases()
    {
        foreach (var policy in new[] {
            BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions,
        })
        {
            yield return [policy, 1, "int32 result = 40 + 2; return result;", 42];
            yield return [policy, 3,
                "int32 first = 6; int32 second = first * 7; "
                + "int32 result = second - 0; return result;", 42];
            yield return [policy, 1,
                "int32 first = (40 + 2) * 3; int32 result = first / 3; return result;", 42];
        }
    }

    [Theory]
    [MemberData(nameof(BootstrapParameterlessHelperBlockLocalCases))]
    public Task BootstrapParameterlessHelperBlockLocalsExecuteAndUnwindAsync(
        BoundRuntimePolicy policy, int forwardingCount, string helperBody, int value)
    {
        var sourceText = CreateBootstrapParameterlessCallChainSource(
            "42", forwardingCount, blockMain: forwardingCount == 3,
            leafDeclaration: $"internal int32 Leaf() {{ {helperBody} }} ");
        if (forwardingCount == 3)
        {
            sourceText = "namespace Bootstrap.Example; " + sourceText;
        }
        return AssertBootstrapParameterlessCallChainAsync(
            policy, sourceText, forwardingCount, value);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task BootstrapParameterlessCallChainsScaleToSourceLimitAsync(
        BoundRuntimePolicy policy)
    {
        var forwardingCount = 1;
        while (CreateBootstrapParameterlessCallChainSource("42", forwardingCount + 1).Length
            <= ScannerHost.MaximumSourceLength)
        {
            forwardingCount++;
        }
        var sourceText = CreateBootstrapParameterlessCallChainSource("42", forwardingCount)
            .PadRight(ScannerHost.MaximumSourceLength);
        Assert.Equal(ScannerHost.MaximumSourceLength, sourceText.Length);
        Assert.True(forwardingCount > 256);
        var beyondSource = CreateBootstrapParameterlessCallChainSource("42", forwardingCount + 1);
        Assert.True(beyondSource.Length > ScannerHost.MaximumSourceLength);
        Assert.Throws<ArgumentException>(() => CreateBootstrapSourceBuffers(SourceText.From(beyondSource)));
        await AssertBootstrapParameterlessCallChainAsync(
            policy, sourceText, forwardingCount, 42).ConfigureAwait(true);
        await AssertBootstrapParameterlessCallChainAsync(
            policy, sourceText, forwardingCount, 42,
            shortCode: true).ConfigureAwait(true);
        await AssertBootstrapParameterlessCallChainAsync(
            policy, sourceText, forwardingCount, 42,
            shortImage: true).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, true)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true)]
    [InlineData(BoundRuntimePolicy.FailFast, false)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false)]
    public Task BootstrapParameterlessCallChainsRejectInsufficientCapacityAsync(
        BoundRuntimePolicy policy, bool shortCode)
        => AssertBootstrapParameterlessCallChainAsync(
            policy, CreateBootstrapParameterlessCallChainSource("40 + 2", 3), 3, 42, 40, 2,
            shortCode: shortCode, shortImage: !shortCode);

    public static IEnumerable<object[]> UnsupportedBootstrapParameterlessCallChainCases()
    {
        const string leaf = "internal int32 Leaf() => 42; ";
        const string hop = "internal int32 Hop() => Leaf(); ";
        const string main = "public int32 Main() => Hop();";
        string[] sources = [
            leaf + "internal int32 Hop() => Missing(); " + main,
            leaf + "internal int32 Hop() => Hop(); " + main,
            leaf + "internal int32 Hop() => Later(); internal int32 Later() => Hop(); " + main,
            leaf + hop + "internal int32 Outer() => Leaf(); public int32 Main() => Outer();",
            leaf + hop + "internal int32 Hop() => Hop(); " + main,
            leaf + "internal int32 Leaf() => Leaf(); public int32 Main() => Leaf();",
            leaf + "internal int32 Main() => Leaf(); public int32 Main() => Main();",
            leaf + "public int32 Hop() => Leaf(); " + main,
            leaf + "internal int64 Hop() => Leaf(); " + main,
            leaf + "internal int32 Hop(int32 value) => Leaf(); " + main,
            leaf + "internal int32 Hop() => Leaf(1); " + main,
            leaf + "internal int32 Hop() => Leaf() + 1; " + main,
            leaf + "internal int32 Hop() { return Leaf(); } " + main,
            leaf + hop + "public int32 Main(int32 value) => Hop();",
            leaf + hop + "public int32 Main() => Leaf();",
            leaf + hop + main + " internal int32 Extra() => 1;",
            leaf + hop + "public int32 Main() { int32 Hop = 1; return Hop(); }",
            leaf + hop + "internal int32 Outer() => Hop(); public int32 Main() { "
                + "int32 Hop = 1; return Outer(); }",
            "internal int32 Leaf() => 2147483647 + 1 - 1; " + hop + main,
            "internal int32 Leaf() => 50000 * 50000 * 0; " + hop + main,
            "internal int32 Leaf() => (-2147483648) / -1; " + hop + main,
            "internal int32 Leaf() => (-2147483648) % -1; " + hop + main,
            "internal int32 Leaf() => -(-2147483648); " + hop + main,
            "internal int32 Leaf() => 40 % (2 - 2); " + hop + main,
            "internal int32 Leaf() => (40 + 2; " + hop + main,
        ];
        foreach (var policy in new[] {
            BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions,
        })
        {
            foreach (var source in sources)
            {
                yield return [policy, source];
            }
        }
    }

    [Theory]
    [MemberData(nameof(UnsupportedBootstrapParameterlessCallChainCases))]
    public Task BootstrapParameterlessCallChainsRejectMalformedOrOverflowingSourceAsync(
        BoundRuntimePolicy policy, string sourceText)
        => AssertParameterizedArithmeticRejectsBeforePublicationAsync(policy, sourceText);

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "BootstrapCallArgumentValues", 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "BootstrapCallArgumentValues", 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "BootstrapCallArgumentOperators", 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "BootstrapCallArgumentOperators", 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "BootstrapMainLocalNames", 1)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "BootstrapMainLocalNames", 1)]
    [InlineData(BoundRuntimePolicy.FailFast, "BootstrapMainLocalValues", 1)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "BootstrapMainLocalValues", 1)]
    public Task BootstrapParameterlessCallChainsRejectInsufficientWorkspaceAsync(
        BoundRuntimePolicy policy, string workspaceName, int capacity)
        => AssertParameterizedArithmeticRejectsBeforePublicationAsync(
            policy, CreateBootstrapParameterlessCallChainSource("(40 + 2) * 3", 3, blockMain: true),
            workspaceName, capacity);

    public static IEnumerable<object[]> UnsupportedBootstrapParameterlessHelperBlockLocalCases()
    {
        string[] bodies = [
            "return 42;",
            "int32 value = 1; return 42;",
            "int32 value = later; int32 later = 1; return value;",
            "int32 value = value; return value;",
            "int32 value = 1; int32 value = 2; return value;",
            "int32 Leaf = 1; return Leaf;",
            "int32 value = 2147483647 + 1; return value;",
            "int32 value = 1; return value / 0;",
            "int32 value = (40 + 2; return value;",
            "int32 value = 1; return value; int32 later = 2;",
            "int32 value = 1; return value; return 0;",
            "int32 value = 1; return value",
        ];
        foreach (var policy in new[] {
            BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions,
        })
        {
            foreach (var body in bodies)
            {
                yield return [policy, body];
            }
        }
    }

    [Theory]
    [MemberData(nameof(UnsupportedBootstrapParameterlessHelperBlockLocalCases))]
    public Task BootstrapParameterlessHelperBlockLocalsRejectMalformedSourcesAsync(
        BoundRuntimePolicy policy, string helperBody)
        => AssertParameterizedArithmeticRejectsBeforePublicationAsync(
            policy, CreateBootstrapParameterlessCallChainSource(
                "42", 1, leafDeclaration: $"internal int32 Leaf() {{ {helperBody} }} "));

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "BootstrapMainLocalNames", 1)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "BootstrapMainLocalNames", 1)]
    [InlineData(BoundRuntimePolicy.FailFast, "BootstrapMainLocalValues", 1)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "BootstrapMainLocalValues", 1)]
    [InlineData(BoundRuntimePolicy.FailFast, "BootstrapCallArgumentValues", 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "BootstrapCallArgumentValues", 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "BootstrapCallArgumentOperators", 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "BootstrapCallArgumentOperators", 0)]
    public Task BootstrapParameterlessHelperBlockLocalsRejectInsufficientWorkspaceAsync(
        BoundRuntimePolicy policy, string workspaceName, int capacity)
        => AssertParameterizedArithmeticRejectsBeforePublicationAsync(
            policy, CreateBootstrapParameterlessCallChainSource(
                "42", 1, leafDeclaration:
                    "internal int32 Leaf() { int32 first = 40 + 2; "
                    + "int32 second = first; return second; } "),
            workspaceName, capacity);

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task BootstrapParameterlessHelperBlockLocalsScaleToSourceLimitAsync(
        BoundRuntimePolicy policy)
    {
        var localCount = 1;
        while (CreateBootstrapParameterlessHelperBlockChainSource(localCount + 1).Length
            <= ScannerHost.MaximumSourceLength)
        {
            localCount++;
        }
        var sourceText = CreateBootstrapParameterlessHelperBlockChainSource(localCount)
            .PadRight(ScannerHost.MaximumSourceLength);
        Assert.Equal(ScannerHost.MaximumSourceLength, sourceText.Length);
        Assert.True(localCount > 256);
        var beyondSource = CreateBootstrapParameterlessHelperBlockChainSource(localCount + 1);
        Assert.True(beyondSource.Length > ScannerHost.MaximumSourceLength);
        Assert.Throws<ArgumentException>(
            () => CreateBootstrapSourceBuffers(SourceText.From(beyondSource)));
        await AssertBootstrapParameterlessCallChainAsync(
            policy, sourceText, 1, 42).ConfigureAwait(true);
        await AssertBootstrapParameterlessCallChainAsync(
            policy, sourceText, 1, 42, shortCode: true).ConfigureAwait(true);
        await AssertBootstrapParameterlessCallChainAsync(
            policy, sourceText, 1, 42, shortImage: true).ConfigureAwait(true);
    }

    private static string CreateBootstrapParameterlessCallChainSource(
        string expression, int forwardingCount, bool blockMain = false,
        string? leafDeclaration = null)
    {
        var source = leafDeclaration ?? $"internal int32 Leaf() => {expression}; ";
        var target = "Leaf";
        for (var index = 0; index < forwardingCount; index++)
        {
            var name = GetBootstrapCallChainForwardingName(index);
            source += $"internal int32 {name}() => {target}(); ";
            target = name;
        }
        source += blockMain
            ? $"public int32 Main() {{ int32 first = (2 + 3) * 4; "
                + $"int32 second = first + 2; return {target}(); }}"
            : $"public int32 Main() => {target}();";
        return source;
    }

    private static string CreateBootstrapParameterlessHelperBlockChainSource(int localCount)
    {
        var source = new System.Text.StringBuilder("internal int32 Leaf() { ");
        for (var index = 0; index < localCount; index++)
        {
            source.Append("int32 value").Append(index).Append(" = ");
            if (index == 0)
            {
                source.Append("42");
            }
            else
            {
                source.Append("value").Append(index - 1).Append(" + 0");
            }
            source.Append("; ");
        }
        source.Append("return value").Append(localCount - 1)
            .Append("; } internal int32 Hop() => Leaf(); public int32 Main() => Hop();");
        return source.ToString();
    }

    private async Task AssertBootstrapParameterlessCallChainAsync(
        BoundRuntimePolicy policy, string sourceText, int forwardingCount, int value,
        int? leftOperand = null, int rightOperand = 0, bool subtraction = false,
        bool shortCode = false, bool shortImage = false)
    {
        byte[] leaf = leftOperand is int left
            ? [0xB8, .. BitConverter.GetBytes(left), subtraction ? (byte)0x2D : (byte)0x05,
                .. BitConverter.GetBytes(rightOperand), 0xC3]
            : [0xB8, .. BitConverter.GetBytes(value), 0xC3];
        var functionCount = forwardingCount + 1;
        var helperOffset = functionCount * 14;
        var codeLength = helperOffset + leaf.Length;
        var metadataOffset = (codeLength + 3) & ~3;
        var virtualSize = metadataOffset + functionCount * 20;
        var imageLength = 0x400 + ((virtualSize + 0x1FF) & ~0x1FF);
        var buffers = CreateBootstrapSourceBuffers(SourceText.From(sourceText))
            .Add(new HostBuffer("BootstrapCodeBytes", PrimitiveType.Int32,
                [.. new int[codeLength - (shortCode ? 1 : 0)]], true))
            .Add(new HostBuffer("BootstrapCodeLength", PrimitiveType.Int32, [0], true))
            .Add(new HostBuffer("BootstrapImageBytes", PrimitiveType.Int32,
                [.. new int[imageLength - (shortImage ? 1 : 0)]], true))
            .Add(new HostBuffer("BootstrapImageLength", PrimitiveType.Int32, [0], true));
        var components = s_bootstrapSourceComponents
            .Append("BootstrapInt32MainSource")
            .Append("BootstrapInt32MainLeaf")
            .Append("BootstrapInt32MainPeImage");
        var trees = await ReadComponentsAsync(components, EndToEndBootstrapSourceOnly)
            .ConfigureAwait(true);
        var compilation = Compilation.Create(trees, buffers, policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var execution = NativeJit.ExecuteWithData(program, (NativeRuntimePolicy)policy);
        var expectedExit = shortCode ? -1 : shortImage ? -2 : imageLength;
        Assert.Equal(expectedExit, execution.ExitCode);
        Assert.Equal(shortCode ? 0 : codeLength,
            ReadInt32(execution.Data, execution.BufferOffsets[buffers.Length - 3]));
        Assert.Equal(shortCode || shortImage ? 0 : imageLength,
            ReadInt32(execution.Data, execution.BufferOffsets[buffers.Length - 1]));
        Assert.Equal(expectedExit,
            await ExecuteAotAsync(program, (NativeRuntimePolicy)policy).ConfigureAwait(true));
        if (shortCode || shortImage)
        {
            return;
        }

        var code = new byte[codeLength];
        var image = new byte[imageLength];
        for (var index = 0; index < code.Length; index++)
        {
            var emitted = ReadInt32(execution.Data,
                execution.BufferOffsets[buffers.Length - 4] + index * sizeof(int));
            Assert.InRange(emitted, 0, byte.MaxValue);
            code[index] = (byte)emitted;
        }
        for (var index = 0; index < image.Length; index++)
        {
            var emitted = ReadInt32(execution.Data,
                execution.BufferOffsets[buffers.Length - 2] + index * sizeof(int));
            Assert.InRange(emitted, 0, byte.MaxValue);
            image[index] = (byte)emitted;
        }
        Assert.Equal(leaf, code.AsSpan(helperOffset).ToArray());
        Assert.Equal(0x1000 + metadataOffset, ReadInt32(image, 0x120));
        Assert.Equal(functionCount * 12, ReadInt32(image, 0x124));
        Assert.Equal(virtualSize, ReadInt32(image, 0x190));
        Assert.Equal(imageLength - 0x400, ReadInt32(image, 0x198));
        Assert.Equal(0x1000 + ((virtualSize + 0xFFF) & ~0xFFF), ReadInt32(image, 0xD0));
        byte[] frame = [0x48, 0x83, 0xEC, 0x28, 0xE8, 5, 0, 0, 0,
            0x48, 0x83, 0xC4, 0x28, 0xC3];
        for (var index = 0; index < functionCount; index++)
        {
            Assert.Equal(frame, code.AsSpan(index * 14, 14).ToArray());
            var recordOffset = 0x400 + metadataOffset + index * 12;
            Assert.Equal(0x1000 + index * 14, ReadInt32(image, recordOffset));
            Assert.Equal(0x1000 + (index + 1) * 14, ReadInt32(image, recordOffset + 4));
            var unwindOffset = metadataOffset + functionCount * 12 + index * 8;
            Assert.Equal(0x1000 + unwindOffset, ReadInt32(image, recordOffset + 8));
            AssertBootstrapCallChainUnwindInfo(image, 0x400 + unwindOffset, 0x28, 4);
        }
        Assert.Equal(code, image.AsSpan(0x400, codeLength).ToArray());
        Assert.All(image.AsSpan(0x400 + virtualSize).ToArray(),
            static emitted => Assert.Equal(0, emitted));
        Assert.Equal(value, await ExecuteImageAsync(image).ConfigureAwait(true));
        foreach (var probeForwarder in new[] { true, false })
        {
            var probe = CreateBootstrapCallChainAlignmentProbeImage(
                image, 0, probeForwarder, forwardingCount);
            Assert.Equal(8, await ExecuteImageAsync(probe).ConfigureAwait(true));
        }
        await AssertBootstrapAllocationUnwindsAsync(
            image, 0x28, value, callChain: true, forwardingCount: forwardingCount)
            .ConfigureAwait(true);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, true)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true)]
    [InlineData(BoundRuntimePolicy.FailFast, false)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false)]
    public async Task BootstrapParameterlessHelperRejectsInsufficientLocalCapacityAsync(
        BoundRuntimePolicy policy, bool shortNameTable)
    {
        const string sourceText =
            "internal int32 Answer() => 42; public int32 Main() { "
            + "int32 first = 1; int32 second = first + 1; int32 third = second + 1; "
            + "return Answer(); }";
        const int codeCapacity = 240;
        var source = SourceText.From(sourceText);
        var expected = CreateExpectedBytes(false, 0, false, codeCapacity: codeCapacity);
        var localNameCapacity = shortNameTable ? 2 : source.Length;
        var localValueCapacity = shortNameTable ? source.Length : 2;
        var buffers = CreateBootstrapSourceBuffers(source)
            .Where(static buffer => buffer.Name != "BootstrapMainLocalNames"
                && buffer.Name != "BootstrapMainLocalValues")
            .ToImmutableArray()
            .Add(new HostBuffer("BootstrapMainLocalNames", PrimitiveType.Int32,
                [.. new int[localNameCapacity]], true))
            .Add(new HostBuffer("BootstrapMainLocalValues", PrimitiveType.Int32,
                [.. new int[localValueCapacity]], true))
            .Add(new HostBuffer("Expected", PrimitiveType.Int32, [.. expected], false))
            .Add(new HostBuffer("BootstrapCodeBytes", PrimitiveType.Int32,
                [.. new int[codeCapacity]], true))
            .Add(new HostBuffer("BootstrapCodeLength", PrimitiveType.Int32, [0], true))
            .Add(new HostBuffer("BootstrapImageLength", PrimitiveType.Int32, [0], true));
        var components = s_bootstrapSourceComponents
            .Append("BootstrapInt32MainSource")
            .Append("BootstrapInt32MainLeaf");
        var trees = await ReadComponentsAsync(components, VerifyBootstrapSourceEmitter)
            .ConfigureAwait(true);
        var compilation = Compilation.Create(trees, buffers, policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);

        var jit = NativeJit.ExecuteWithData(program, (NativeRuntimePolicy)policy);
        Assert.Equal(0, jit.ExitCode);
        Assert.Equal(0, ReadInt32(jit.Data, jit.BufferOffsets[buffers.Length - 1]));
        Assert.Equal(0, await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() => Sum(20, 20, 2);", 32)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() => Sum(20, 20, 2);", 32)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum4(int32 first, int32 second, int32 third, int32 fourth) => first + second + third + fourth; public int32 Main() => Sum4(1, 2, 4, 8);", 40)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum4(int32 first, int32 second, int32 third, int32 fourth) => first + second + third + fourth; public int32 Main() => Sum4(1, 2, 4, 8);", 40)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum5(int32 first, int32 second, int32 third, int32 fourth, int32 fifth) => first + second + third + fourth + fifth; public int32 Main() => Sum5(1, 2, 4, 8, 16);", 40)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum5(int32 first, int32 second, int32 third, int32 fourth, int32 fifth) => first + second + third + fourth + fifth; public int32 Main() => Sum5(1, 2, 4, 8, 16);", 40)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum6(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth) => first + second + third + fourth + fifth + sixth; public int32 Main() => Sum6(1, 2, 4, 8, 16, 32);", 60)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum7(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh) => first + second + third + fourth + fifth + sixth + seventh; public int32 Main() => Sum7(1, 2, 4, 8, 16, 32, 64);", 80)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum8(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth) => first + second + third + fourth + fifth + sixth + seventh + eighth; public int32 Main() => Sum8(1, 2, 4, 8, 16, 32, 64, 128);", 80)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum9(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth; public int32 Main() => Sum9(1, 2, 4, 8, 16, 32, 64, 128, 256);", 106)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum10(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth; public int32 Main() => Sum10(1, 2, 4, 8, 16, 32, 64, 128, 256, 512);", 118)]
    public async Task HelperCallsRejectInsufficientCodeBuffersAsync(
        BoundRuntimePolicy policy, string sourceText, int codeCapacity)
    {
        var source = SourceText.From(sourceText);
        var expected = CreateExpectedBytes(false, 0, false, codeCapacity: codeCapacity);
        var buffers = CreateBootstrapSourceBuffers(source)
            .Add(new HostBuffer("Expected", PrimitiveType.Int32, [.. expected], false))
            .Add(new HostBuffer("BootstrapCodeBytes", PrimitiveType.Int32,
                [.. new int[codeCapacity]], true))
            .Add(new HostBuffer("BootstrapCodeLength", PrimitiveType.Int32, [0], true));
        var components = s_bootstrapSourceComponents
            .Append("BootstrapInt32MainSource")
            .Append("BootstrapInt32MainLeaf");
        var trees = await ReadComponentsAsync(components, VerifyBootstrapSourceEmitter)
            .ConfigureAwait(true);
        var compilation = Compilation.Create(trees, buffers, policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);

        var jit = NativeJit.ExecuteWithData(program, (NativeRuntimePolicy)policy);
        Assert.Equal(0, jit.ExitCode);
        Assert.Equal(0, await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 42;", 42, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 42;", 42, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => -2147483648; public int32 Main() => Answer();", int.MinValue, 6)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => -2147483648; public int32 Main() => Answer();", int.MinValue, 6)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 40 + 2; public int32 Main() => Answer();", 42, 6, 40, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 40 + 2; public int32 Main() => Answer();", 42, 6, 40, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 40 - 2; public int32 Main() => Answer();", 38, 6, 40, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 40 - 2; public int32 Main() => Answer();", 38, 6, 40, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer(int32 value) => value; public int32 Main() => Answer(-42);", -42, 7)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer(int32 value) => value; public int32 Main() => Answer(-42);", -42, 7)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer(int32 value) => value; public int32 Main() { int32 result = 20 + 22; return Answer(result); }", 42, 7)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer(int32 value) => value; public int32 Main() { int32 result = 20 + 22; return Answer(result); }", 42, 7)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Add(int32 left, int32 right) => left + right; public int32 Main() => Add(40, 2);", 42, 8, 40, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Add(int32 left, int32 right) => left + right; public int32 Main() => Add(40, 2);", 42, 8, 40, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Add(int32 left, int32 right) => left + right; public int32 Main() { int32 left = 20; int32 right = 22; return Add(left, right); }", 42, 8, 20, 22)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Add(int32 left, int32 right) => left + right; public int32 Main() { int32 left = 20; int32 right = 22; return Add(left, right); }", 42, 8, 20, 22)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() => Sum(20, 20, 2);", 42, 9, 20, 20, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() => Sum(20, 20, 2);", 42, 9, 20, 20, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() => Sum(-2, -3, -2);", -7, 9, -2, -3, -2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() => Sum(-2, -3, -2);", -7, 9, -2, -3, -2)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum4(int32 first, int32 second, int32 third, int32 fourth) => first + second + third + fourth; public int32 Main() => Sum4(1, 2, 4, 8);", 15, 10, 1, 2, 4, 8)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum4(int32 first, int32 second, int32 third, int32 fourth) => first + second + third + fourth; public int32 Main() => Sum4(1, 2, 4, 8);", 15, 10, 1, 2, 4, 8)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum5(int32 first, int32 second, int32 third, int32 fourth, int32 fifth) => first + second + third + fourth + fifth; public int32 Main() => Sum5(1, 2, 4, 8, 16);", 31, 11, 1, 2, 4, 8, 16)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum5(int32 first, int32 second, int32 third, int32 fourth, int32 fifth) => first + second + third + fourth + fifth; public int32 Main() => Sum5(-2, -3, -5, -7, -11);", -28, 11, -2, -3, -5, -7, -11)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum5(int32 first, int32 second, int32 third, int32 fourth, int32 fifth) => first + second + third + fourth + fifth; public int32 Main() { int32 firstValue = 1; int32 fifthValue = 16; return Sum5(firstValue, 2, 4, 8, fifthValue); }", 31, 11, 1, 2, 4, 8, 16)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum6(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth) => first + second + third + fourth + fifth + sixth; public int32 Main() => Sum6(1, 2, 4, 8, 16, 32);", 63, 12, 1, 2, 4, 8, 16, 32)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum6(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth) => first + second + third + fourth + fifth + sixth; public int32 Main() => Sum6(-2, -3, -5, -7, -11, -13);", -41, 12, -2, -3, -5, -7, -11, -13)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum6(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth) => first + second + third + fourth + fifth + sixth; public int32 Main() { int32 firstValue = 1; int32 sixthValue = 32; return Sum6(firstValue, 2, 4, 8, 16, sixthValue); }", 63, 12, 1, 2, 4, 8, 16, 32)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum6(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth) => first + second + third + fourth + fifth + sixth; public int32 Main() { int32 fifthValue = 16; int32 sixthValue = 32; return Sum6(1, 2, 4, 8, fifthValue, sixthValue); }", 63, 12, 1, 2, 4, 8, 16, 32)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum7(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh) => first + second + third + fourth + fifth + sixth + seventh; public int32 Main() => Sum7(1, 2, 4, 8, 16, 32, 64);", 127, 13, 1, 2, 4, 8, 16, 32, 64)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum7(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh) => first + second + third + fourth + fifth + sixth + seventh; public int32 Main() => Sum7(-2, -3, -5, -7, -11, -13, -17);", -58, 13, -2, -3, -5, -7, -11, -13, -17)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum7(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh) => first + second + third + fourth + fifth + sixth + seventh; public int32 Main() { int32 fifthValue = 16; int32 seventhValue = 64; return Sum7(1, 2, 4, 8, fifthValue, 32, seventhValue); }", 127, 13, 1, 2, 4, 8, 16, 32, 64)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum8(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth) => first + second + third + fourth + fifth + sixth + seventh + eighth; public int32 Main() => Sum8(1, 2, 4, 8, 16, 32, 64, 128);", 255, 14, 1, 2, 4, 8, 16, 32, 64, 128)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum8(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth) => first + second + third + fourth + fifth + sixth + seventh + eighth; public int32 Main() => Sum8(-2, -3, -5, -7, -11, -13, -17, -19);", -77, 14, -2, -3, -5, -7, -11, -13, -17, -19)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum8(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth) => first + second + third + fourth + fifth + sixth + seventh + eighth; public int32 Main() { int32 fifthValue = 16; int32 eighthValue = 128; return Sum8(1, 2, 4, 8, fifthValue, 32, 64, eighthValue); }", 255, 14, 1, 2, 4, 8, 16, 32, 64, 128)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum9(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth; public int32 Main() => Sum9(1, 2, 4, 8, 16, 32, 64, 128, 256);", 511, 15, 1, 2, 4, 8, 16, 32, 64, 128, 256)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum9(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth; public int32 Main() { int32 fifthValue = 16; int32 ninthValue = 256; return Sum9(1, 2, 4, 8, fifthValue, 32, 64, 128, ninthValue); }", 511, 15, 1, 2, 4, 8, 16, 32, 64, 128, 256)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum10(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth; public int32 Main() => Sum10(1, 2, 4, 8, 16, 32, 64, 128, 256, 512);", 1023, 16, 1, 2, 4, 8, 16, 32, 64, 128, 256, 512)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum10(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth; public int32 Main() => Sum10(-2, -3, -5, -7, -11, -13, -17, -19, -23, -29);", -129, 16, -2, -3, -5, -7, -11, -13, -17, -19, -23, -29)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum10(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth; public int32 Main() { int32 fifthValue = 16; int32 tenthValue = 512; return Sum10(1, 2, 4, 8, fifthValue, 32, 64, 128, 256, tenthValue); }", 1023, 16, 1, 2, 4, 8, 16, 32, 64, 128, 256, 512)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum11(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh; public int32 Main() => Sum11(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);", 66, 17, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum11(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh; public int32 Main() => Sum11(-2, -3, -5, -7, -11, -13, -17, -19, -23, -29, -31);", -160, 17, -2, -3, -5, -7, -11, -13, -17, -19, -23, -29, -31)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum11(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh; public int32 Main() { int32 eleventhValue = 11; return Sum11(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, eleventhValue); }", 66, 17, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum12(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth; public int32 Main() => Sum12(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);", 78, 18, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum12(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth; public int32 Main() => Sum12(-2, -3, -5, -7, -11, -13, -17, -19, -23, -29, -31, -37);", -197, 18, -2, -3, -5, -7, -11, -13, -17, -19, -23, -29, -31, -37)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum12(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth; public int32 Main() { int32 twelfthValue = 12; return Sum12(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, twelfthValue); }", 78, 18, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum13(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth; public int32 Main() => Sum13(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13);", 91, 19, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum13(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth; public int32 Main() => Sum13(-2, -3, -5, -7, -11, -13, -17, -19, -23, -29, -31, -37, -41);", -238, 19, -2, -3, -5, -7, -11, -13, -17, -19, -23, -29, -31, -37, -41)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum13(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth; public int32 Main() { int32 thirteenthValue = 13; return Sum13(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, thirteenthValue); }", 91, 19, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() { int32 one = 20; int32 two = 20; return Sum(one, two, 2); }", 42, 9, 20, 20, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sum(int32 first, int32 second, int32 third) => first + second + third; public int32 Main() { int32 one = 20; int32 two = 20; return Sum(one, two, 2); }", 42, 9, 20, 20, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer(int32 value) => value; public int32 Main() { int32 first = 20; int32 result = first + 22; return Answer(result); }", 42, 7)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer(int32 value) => value; public int32 Main() { int32 first = 20; int32 result = first + 22; return Answer(result); }", 42, 7)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer(int32 value) => value; public int32 Main() { int32 result = 42; return Answer(-result); }", -42, 7)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer(int32 value) => value; public int32 Main() { int32 result = 42; return Answer(-result); }", -42, 7)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 40 + 2; public int32 Main() { int32 value = 7; return Answer(); }", 42, 6, 40, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 40 + 2; public int32 Main() { int32 value = 7; return Answer(); }", 42, 6, 40, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 40 + 2; public int32 Main() { return Answer(); }", 42, 6, 40, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 40 + 2; public int32 Main() { return Answer(); }", 42, 6, 40, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Answer() => 40 + 2; public int32 Main() { int32 first = 7; int32 second = first + 1; return Answer(); }", 42, 6, 40, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Answer() => 40 + 2; public int32 Main() { int32 first = 7; int32 second = first + 1; return Answer(); }", 42, 6, 40, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { return 40 + 2; }", 42, 1)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { return 40 + 2; }", 42, 1)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 42; return value; }", 42, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 42; return value; }", 42, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 42; return -value; }", -42, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 42; return -value; }", -42, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 answer = 40 + 2; return answer; }", 42, 1)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 answer = 40 + 2; return answer; }", 42, 1)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 answer = 40 + 2; return answer + 1; }", 43, 1, 42, 1)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 answer = 40 + 2; return answer + 1; }", 43, 1, 42, 1)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 40; return value + 2; }", 42, 1)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 40; return value + 2; }", 42, 1)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 40; return value - 2; }", 38, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 40; return value - 2; }", 38, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 6; return value * 7; }", 42, 3, 6, 7)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 6; return value * 7; }", 42, 3, 6, 7)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 2; return 40 / value; }", 20, 4)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 2; return 40 / value; }", 20, 4)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 3; return 40 % value; }", 1, 5)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 3; return 40 % value; }", 1, 5)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 value = 2; return 40 + value; }", 42, 1)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 value = 2; return 40 + value; }", 42, 1)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 left = 40; int32 right = 2; return left + right; }", 42, 1, 40, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 left = 40; int32 right = 2; return left + right; }", 42, 1, 40, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 left = 40; int32 right = left + 2; return right; }", 42, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 left = 40; int32 right = left + 2; return right; }", 42, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 left = 40; int32 right = left / 2; return right; }", 20, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 left = 40; int32 right = left / 2; return right; }", 20, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() { int32 left = 42; int32 right = -left; return right; }", -42, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() { int32 left = 42; int32 right = -left; return right; }", -42, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 6 * 7;", 42, 3, 6, 7)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 6 * 7;", 42, 3, 6, 7)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 40 - 2;", 38, 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 40 - 2;", 38, 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 40 / 2;", 20, 4)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 40 / 2;", 20, 4)]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 40 % 3;", 1, 5)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 40 % 3;", 1, 5)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum14(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth; public int32 Main() => Sum14(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14);", 105, 20, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum15(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth + fifteenth; public int32 Main() => Sum15(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);", 120, 21, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum16(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth, int32 sixteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth + fifteenth + sixteenth; public int32 Main() => Sum16(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16);", 136, 22, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum17(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth, int32 sixteenth, int32 seventeenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth + fifteenth + sixteenth + seventeenth; public int32 Main() => Sum17(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17);", 153, 23, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum17(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth, int32 sixteenth, int32 seventeenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth + fifteenth + sixteenth + seventeenth; public int32 Main() => Sum17(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, -2147483648);", -2147483632, 23, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, int.MinValue)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum18(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth, int32 sixteenth, int32 seventeenth, int32 eighteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth + fifteenth + sixteenth + seventeenth + eighteenth; public int32 Main() => Sum18(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18);", 171, 24, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18)]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sum18(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth, int32 seventh, int32 eighth, int32 ninth, int32 tenth, int32 eleventh, int32 twelfth, int32 thirteenth, int32 fourteenth, int32 fifteenth, int32 sixteenth, int32 seventeenth, int32 eighteenth) => first + second + third + fourth + fifth + sixth + seventh + eighth + ninth + tenth + eleventh + twelfth + thirteenth + fourteenth + fifteenth + sixteenth + seventeenth + eighteenth; public int32 Main() => Sum18(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, -2147483648);", -2147483631, 24, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, int.MinValue)]

    public async Task TerrumScannerAndSourceEmitterProduceBootstrapPeImageAsync(
        BoundRuntimePolicy policy, string sourceText, int value, int operation,
        int firstOperand = 0, int secondOperand = 0, int thirdOperand = 0,
        int fourthOperand = 0, int fifthOperand = 0, int sixthOperand = 0,
        int seventhOperand = 0, int eighthOperand = 0, int ninthOperand = 0,
        int tenthOperand = 0, int eleventhOperand = 0, int twelfthOperand = 0,
        int thirteenthOperand = 0, int fourteenthOperand = 0, int fifteenthOperand = 0,
        int sixteenthOperand = 0, int seventeenthOperand = 0, int eighteenthOperand = 0,
        int nineteenthOperand = 0, int twentiethOperand = 0, int twentyFirstOperand = 0,
        int twentySecondOperand = 0, int twentyThirdOperand = 0, int twentyFourthOperand = 0,
        int twentyFifthOperand = 0, int twentySixthOperand = 0,
        int[]? helperArguments = null, int imageCapacity = 1536,
        bool forceNativeProbe = false, bool helperSubtraction = false,
        bool helperMultiplication = false)
    {
        var source = SourceText.From(sourceText);
        var helperArgumentCount = helperArguments?.Length ?? (operation >= 7 ? operation - 6 : 0);
        var callAlignmentProbeArgumentCount = helperArguments?.Length
            ?? (operation >= 7 ? operation - 6 : 0);
        var shouldProbeCallFrame = forceNativeProbe
            || helperArgumentCount >= 1
                && (helperArguments is null
                    || helperArguments.SequenceEqual(Enumerable.Range(1, helperArgumentCount))
                        && !sourceText.Contains("firstValue", StringComparison.Ordinal));
        var requiredCodeLength = helperArgumentCount >= 1
            ? GetStackArgumentCodeLength(helperArgumentCount, helperMultiplication)
            : 0;
        var codeCapacity = helperArguments is not null
            ? requiredCodeLength
            : 240;
        var components = s_bootstrapSourceComponents
            .Append("BootstrapInt32MainSource")
            .Append("BootstrapInt32MainLeaf")
            .Append("BootstrapInt32MainPeImage");
        var trees = await ReadComponentsAsync(components, EndToEndBootstrapSourceOnly)
            .ConfigureAwait(true);
        var buffers = CreateBootstrapSourceBuffers(source)
            .Add(new HostBuffer("BootstrapCodeBytes", PrimitiveType.Int32,
                [.. new int[codeCapacity]], true))
            .Add(new HostBuffer("BootstrapCodeLength", PrimitiveType.Int32, [0], true))
            .Add(new HostBuffer("BootstrapImageBytes", PrimitiveType.Int32,
                [.. new int[imageCapacity]], true))
            .Add(new HostBuffer("BootstrapImageLength", PrimitiveType.Int32, [0], true));
        var compilation = Compilation.Create(trees, buffers, policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);

        var jit = NativeJit.ExecuteWithData(program, (NativeRuntimePolicy)policy);
        var expectedImageLength = helperArgumentCount > 0
            ? GetStackArgumentImageLength(helperArgumentCount, helperMultiplication)
            : 0x600;
        var addition = operation == 1
            || operation == 6 && sourceText.Contains(" + ", StringComparison.Ordinal);
        var subtraction = operation == 2
            || operation == 6 && sourceText.Contains(" - ", StringComparison.Ordinal);
        var expected = CreateExpectedBytes(true, value,
            addition, subtraction, operation == 3,
            operation == 4, operation == 5, firstOperand, secondOperand,
            thirdOperand, fourthOperand, fifthOperand, sixthOperand,
            seventhOperand, eighthOperand, ninthOperand, tenthOperand, eleventhOperand,
            twelfthOperand, thirteenthOperand, fourteenthOperand, fifteenthOperand,
            sixteenthOperand, seventeenthOperand, eighteenthOperand, nineteenthOperand, twentiethOperand,
            twentyFirstOperand, twentySecondOperand, twentyThirdOperand, twentyFourthOperand,
            twentyFifthOperand, twentySixthOperand,
            helperCall: operation >= 6,
            helperArgument: operation == 7,
            helperTwoArguments: operation == 8,
            helperThreeArguments: operation == 9,
            helperFourArguments: operation == 10,
            helperFiveArguments: operation == 11,
            helperSixArguments: operation == 12,
            helperSevenArguments: operation == 13,
            helperEightArguments: operation == 14,
            helperNineArguments: operation == 15,
            helperTenArguments: operation == 16,
            helperElevenArguments: operation == 17,
            helperTwelveArguments: operation == 18,
            helperThirteenArguments: operation == 19,
            helperFourteenArguments: operation == 20,
            helperFifteenArguments: operation == 21,
            helperSixteenArguments: operation == 22,
            helperSeventeenArguments: operation == 23,
            helperEighteenArguments: operation == 24,
            helperNineteenArguments: operation == 25,
            helperTwentyArguments: operation == 26,
            helperTwentyOneArguments: operation == 27,
            helperTwentyTwoArguments: operation == 28,
            helperTwentyThreeArguments: operation == 29,
            helperTwentyFourArguments: operation == 30,
            helperTwentyFiveArguments: operation == 31,
            helperTwentySixArguments: operation == 32,
            codeCapacity: codeCapacity,
            helperArgumentCount: helperArgumentCount,
            helperArguments: helperArguments,
            helperSubtraction: helperSubtraction,
            helperMultiplication: helperMultiplication);
        Assert.True(jit.ExitCode == expectedImageLength,
            $"PE emission returned {jit.ExitCode}; codeLength="
                + $"{ReadInt32(jit.Data, jit.BufferOffsets[buffers.Length - 3])}, imageLength="
                + $"{ReadInt32(jit.Data, jit.BufferOffsets[buffers.Length - 1])}.");
        AssertOutputCode(jit, buffers.Length - 4, buffers.Length - 3, expected);
        var image = ReadBootstrapPeImage(
            jit, buffers.Length - 2, buffers.Length - 1, expected,
            helperArgumentCount);
        Assert.Equal(value, await ExecuteImageAsync(image).ConfigureAwait(true));
        if (callAlignmentProbeArgumentCount >= 1 && shouldProbeCallFrame)
        {
            var alignmentProbeImage = CreateBootstrapHelperEntryAlignmentProbeImage(
                image, callAlignmentProbeArgumentCount);
            var helperEntryRspAlignment = await ExecuteImageAsync(alignmentProbeImage)
                .ConfigureAwait(true);
            Assert.True(helperEntryRspAlignment == 8,
                $"The {callAlignmentProbeArgumentCount}-argument helper had RSP mod 16 = {helperEntryRspAlignment} at entry.");
        }
        if (helperArguments is not null && shouldProbeCallFrame
            && (helperArgumentCount is 1 or 6 or 8 or 10 or 12
                or >= 14))
        {
            var expectedAllocation = GetStackArgumentFrameSize(helperArgumentCount);
            await AssertBootstrapAllocationUnwindsAsync(image, expectedAllocation, value)
                .ConfigureAwait(true);
        }
        Assert.Equal(expectedImageLength, await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sub(int32 first, int32 second) => first - second; public int32 Main() => Sub(40, 2);", 38, new int[] { 40, 2 })]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sub(int32 first, int32 second) => first - second; public int32 Main() => Sub(40, 2);", 38, new int[] { 40, 2 })]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sub(int32 first, int32 second, int32 third, int32 fourth) => first - second - third - fourth; public int32 Main() => Sub(40, 2, 3, 4);", 31, new int[] { 40, 2, 3, 4 })]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sub(int32 first, int32 second, int32 third, int32 fourth) => first - second - third - fourth; public int32 Main() => Sub(40, 2, 3, 4);", 31, new int[] { 40, 2, 3, 4 })]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sub(int32 first, int32 second, int32 third, int32 fourth, int32 fifth) => first - second - third - fourth - fifth; public int32 Main() => Sub(100, 1, 2, 3, 4);", 90, new int[] { 100, 1, 2, 3, 4 })]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sub(int32 first, int32 second, int32 third, int32 fourth, int32 fifth) => first - second - third - fourth - fifth; public int32 Main() => Sub(100, 1, 2, 3, 4);", 90, new int[] { 100, 1, 2, 3, 4 })]
    public Task ParameterizedSubtractionExecutesFromGeneratedPeAsync(
        BoundRuntimePolicy policy, string sourceText, int value, int[] arguments)
        => TerrumScannerAndSourceEmitterProduceBootstrapPeImageAsync(
            policy, sourceText, value, arguments.Length + 6,
            helperArguments: arguments, helperSubtraction: true);

    public static IEnumerable<object[]> BootstrapHelperCallChainCases()
    {
        foreach (var policy in new[]
            { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            yield return [policy, new[] { 17 }, "", 17, false, 1];
            yield return [policy, new[] { 100, 2, 30, 4 }, "+", 136, false, 1];
            yield return [policy, new[] { 1, 20, 300, 4000, 50000 }, "+", 54321, true, 1];
            yield return [policy, new[] { 100000, 2000, 300, 40, 5, 1 }, "-", 97654, false, 1];
            yield return [policy, new[] { 2, 3, 5, 7, 11 }, "*", 2310, false, 1];
            yield return [policy, Enumerable.Range(1, 18).ToArray(), "+", 171, false, 1];
            yield return [policy, new[] { 1, 2, 3, 4, 5, 6 }, "+", 21, false, 3];
            yield return [policy, new[] { 7, 11 }, "*", 77, false, 6];
            yield return [policy, new[] { 17 }, "", 17, false, 32];
        }
    }

    [Theory]
    [MemberData(nameof(BootstrapHelperCallChainCases))]
    public Task BootstrapHelperCallChainsExecuteAndValidateAsync(
        BoundRuntimePolicy policy, int[] arguments, string operation,
        int expectedResult, bool useMainLocal, int forwardingCount)
        => AssertBootstrapHelperCallChainAsync(
            policy, arguments, operation, expectedResult, useMainLocal,
            forwardingCount: forwardingCount);

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task BootstrapHelperCallChainsProbeAndUnwindNestedFramesAsync(
        BoundRuntimePolicy policy)
    {
        var arguments = Enumerable.Range(1, 512).ToArray();
        return AssertBootstrapHelperCallChainAsync(
            policy, arguments, "+", 131328, false, verifyNestedUnwind: true);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task BootstrapHelperCallChainsProbeAndUnwindMultipleForwardersAsync(
        BoundRuntimePolicy policy)
    {
        var arguments = Enumerable.Range(1, 256).ToArray();
        return AssertBootstrapHelperCallChainAsync(
            policy, arguments, "+", 32896, false, verifyNestedUnwind: true,
            forwardingCount: 3);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, 32)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, 32)]
    [InlineData(BoundRuntimePolicy.FailFast, 128)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, 128)]
    [InlineData(BoundRuntimePolicy.FailFast, 256)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, 256)]
    public Task BootstrapHelperCallChainsProbeAndUnwindDeepChainAsync(
        BoundRuntimePolicy policy, int forwardingCount)
        => AssertBootstrapHelperCallChainAsync(
            policy, [17], "", 17, false, verifyNestedUnwind: true,
            forwardingCount: forwardingCount);

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task BootstrapHelperCallChainDepthScalesToSourceLimitAsync(
        BoundRuntimePolicy policy)
    {
        static string CreateSource(int forwardingCount)
            => CreateBootstrapHelperCallChainSource(
                [17], "", useMainLocal: false, forwardingCount: forwardingCount);

        var minimumForwardingCount = 1;
        var maximumForwardingCount = ScannerHost.MaximumSourceLength;
        while (minimumForwardingCount < maximumForwardingCount)
        {
            var forwardingCount = minimumForwardingCount
                + (maximumForwardingCount - minimumForwardingCount + 1) / 2;
            if (CreateSource(forwardingCount).Length <= ScannerHost.MaximumSourceLength)
            {
                minimumForwardingCount = forwardingCount;
            }
            else
            {
                maximumForwardingCount = forwardingCount - 1;
            }
        }

        var sourceText = CreateSource(minimumForwardingCount);
        Assert.InRange(sourceText.Length, 1, ScannerHost.MaximumSourceLength);
        var excessiveSource = CreateSource(minimumForwardingCount + 1);
        Assert.True(excessiveSource.Length > ScannerHost.MaximumSourceLength);

        var imageCapacity = GetBootstrapCallChainImageLength(
            1, multiplication: false, forwardingCount: minimumForwardingCount);
        await AssertBootstrapHelperCallChainAsync(
            policy, [17], "", 17, false, imageCapacity: imageCapacity,
            forwardingCount: minimumForwardingCount).ConfigureAwait(true);
        await AssertBootstrapHelperCallChainAsync(
            policy, [17], "", 17, false, imageCapacity: imageCapacity - 1,
            expectImageRejection: true,
            forwardingCount: minimumForwardingCount).ConfigureAwait(true);
        await AssertBootstrapHelperCallChainAsync(
            policy, [17], "", 17, false,
            forwardingCount: minimumForwardingCount,
            codeCapacity: GetBootstrapCallChainCodeLength(
                1, multiplication: false,
                forwardingCount: minimumForwardingCount) - 1,
            expectCodeRejection: true).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task BootstrapHelperCallChainArgumentCountScalesToSourceLimitAsync(
        BoundRuntimePolicy policy)
    {
        const int forwardingCount = 2;

        static (string SourceText, int[] Arguments) CreateCase(
            int argumentCount, int forwardingCount)
        {
            var arguments = Enumerable.Repeat(1, argumentCount).ToArray();
            return (
                CreateBootstrapHelperCallChainSource(
                    arguments, "+", useMainLocal: false,
                    forwardingCount: forwardingCount),
                arguments);
        }

        var minimumArgumentCount = 1;
        var maximumArgumentCount = ScannerHost.MaximumSourceLength;
        while (minimumArgumentCount < maximumArgumentCount)
        {
            var argumentCount = minimumArgumentCount
                + (maximumArgumentCount - minimumArgumentCount + 1) / 2;
            if (CreateCase(argumentCount, forwardingCount).SourceText.Length
                <= ScannerHost.MaximumSourceLength)
            {
                minimumArgumentCount = argumentCount;
            }
            else
            {
                maximumArgumentCount = argumentCount - 1;
            }
        }

        var (sourceText, arguments) = CreateCase(minimumArgumentCount, forwardingCount);
        Assert.InRange(sourceText.Length, 1, ScannerHost.MaximumSourceLength);
        Assert.True(CreateCase(minimumArgumentCount + 1, forwardingCount).SourceText.Length
            > ScannerHost.MaximumSourceLength);

        var codeCapacity = GetBootstrapCallChainCodeLength(
            arguments.Length, multiplication: false,
            forwardingCount: forwardingCount);
        var imageCapacity = GetBootstrapCallChainImageLength(
            arguments.Length, multiplication: false,
            forwardingCount: forwardingCount);
        await AssertBootstrapHelperCallChainAsync(
            policy, arguments, "+", arguments.Length, false,
            imageCapacity: imageCapacity, forwardingCount: forwardingCount)
            .ConfigureAwait(true);
        await AssertBootstrapHelperCallChainAsync(
            policy, arguments, "+", arguments.Length, false,
            imageCapacity: imageCapacity - 1, expectImageRejection: true,
            forwardingCount: forwardingCount).ConfigureAwait(true);
        await AssertBootstrapHelperCallChainAsync(
            policy, arguments, "+", arguments.Length, false,
            forwardingCount: forwardingCount, codeCapacity: codeCapacity - 1,
            expectCodeRejection: true).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task BootstrapHelperCallChainsForwardManyStackArgumentsThroughDeepChainAsync(
        BoundRuntimePolicy policy)
    {
        var arguments = Enumerable.Range(1, 32).ToArray();
        return AssertBootstrapHelperCallChainAsync(
            policy, arguments, "+", 528, false, verifyNestedUnwind: true,
            imageCapacity: GetBootstrapCallChainImageLength(
                arguments.Length, multiplication: false, forwardingCount: 32),
            forwardingCount: 32);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "namespace Demo; public int32 Main() => 42;", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "namespace Demo; public int32 Main() => 42;", true, 42)]
    [InlineData(BoundRuntimePolicy.FailFast, "namespace Demo.Core; public int32 Main() => 42;", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "namespace Demo.Core; public int32 Main() => 42;", true, 42)]
    [InlineData(BoundRuntimePolicy.FailFast, "namespace Demo.Core; internal int32 Answer() => 40 + 2; public int32 Main() => Answer();", true, 42)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "namespace Demo.Core; internal int32 Answer() => 40 + 2; public int32 Main() => Answer();", true, 42)]
    public Task BootstrapFileScopedNamespacePreservesMainAndHelperAsync(
        BoundRuntimePolicy policy, string sourceText, bool shouldEmit, int expectedValue)
        => EmitsOnlyTheDirectSourceSubsetAsync(
            policy, sourceText, shouldEmit, expectedValue);

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task BootstrapStandaloneLeafSupportsSourceSizedLocalsAsync(
        BoundRuntimePolicy policy)
    {
        static string CreateDeclarations(int localCount)
        {
            return string.Join(" ", Enumerable.Range(0, localCount)
                .Select(static index => index == 0
                    ? "int32 local0 = 1;"
                    : $"int32 local{index} = local{index - 1} + 1;"));
        }

        static string CreateSource(int localCount)
        {
            var declarations = CreateDeclarations(localCount);
            return $"namespace Demo.Core; public int32 Main() {{ {declarations} "
                + $"return -(local{localCount - 1} + 2 * 3); }}";
        }

        var minimumLocalCount = 1;
        var maximumLocalCount = ScannerHost.MaximumSourceLength;
        while (minimumLocalCount < maximumLocalCount)
        {
            var localCount = minimumLocalCount
                + (maximumLocalCount - minimumLocalCount + 1) / 2;
            if (CreateSource(localCount).Length <= ScannerHost.MaximumSourceLength)
            {
                minimumLocalCount = localCount;
            }
            else
            {
                maximumLocalCount = localCount - 1;
            }
        }

        var sourceText = CreateSource(minimumLocalCount);
        Assert.InRange(sourceText.Length, 1, ScannerHost.MaximumSourceLength);
        Assert.True(CreateSource(minimumLocalCount + 1).Length
            > ScannerHost.MaximumSourceLength);
        return TerrumScannerAndSourceEmitterProduceBootstrapPeImageAsync(
            policy, sourceText, -(minimumLocalCount + 6), 0);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 first = 1; int32 second = 2; int32 third = 3; return first + second * third;", 7)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 first = 1; int32 second = 2; int32 third = 3; return first + second * third;", 7)]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 first = 4; int32 second = first * 3 + 2; int32 third = (second - 1) / 3; return -(third + first);", -8)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 first = 4; int32 second = first * 3 + 2; int32 third = (second - 1) / 3; return -(third + first);", -8)]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 first = -1; int32 second = 2; int32 third = 3; return first + second;", 1)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 first = -1; int32 second = 2; int32 third = 3; return first + second;", 1)]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 first = 1; int32 second = 2; int32 third = 3; return first + second + third;", 6)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 first = 1; int32 second = 2; int32 third = 3; return first + second + third;", 6)]
    public Task BootstrapStandaloneLeafFoldsCallArgumentExpressionsAsync(
        BoundRuntimePolicy policy, string body, int expectedValue)
        => TerrumScannerAndSourceEmitterProduceBootstrapPeImageAsync(
            policy, $"public int32 Main() {{ {body} }}", expectedValue, 0);

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 first = second; int32 second = 2; int32 third = 3; return second;")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 first = second; int32 second = 2; int32 third = 3; return second;")]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 first = 1; int32 first = 2; int32 third = 3; return first;")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 first = 1; int32 first = 2; int32 third = 3; return first;")]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 first = 1; int32 second = 2; int32 third = 3; return 1;")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 first = 1; int32 second = 2; int32 third = 3; return 1;")]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 first = 2147483647 + 1; int32 second = 2; int32 third = 3; return third;")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 first = 2147483647 + 1; int32 second = 2; int32 third = 3; return third;")]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 first = 1 / 0; int32 second = 2; int32 third = 3; return third;")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 first = 1 / 0; int32 second = 2; int32 third = 3; return third;")]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 first = -2147483648; int32 second = -first; int32 third = 3; return third;")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 first = -2147483648; int32 second = -first; int32 third = 3; return third;")]
    public Task BootstrapStandaloneLeafRejectsInvalidSourceSizedLocalsAsync(
        BoundRuntimePolicy policy, string body)
        => EmitsOnlyTheDirectSourceSubsetAsync(
            policy, $"public int32 Main() {{ {body} }}", false, 0);

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, true)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true)]
    [InlineData(BoundRuntimePolicy.FailFast, false)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false)]
    public async Task BootstrapStandaloneLeafRejectsInsufficientLocalCapacityAsync(
        BoundRuntimePolicy policy, bool shortNameTable)
    {
        const string sourceText =
            "public int32 Main() { int32 first = 1; int32 second = first + 1; "
            + "int32 third = second + 1; return third; }";
        var source = SourceText.From(sourceText);
        var rejectSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (!TryEmitBootstrapInt32MainSource()) { return 0; }
                if (Host.BootstrapCodeLength[0] != 0) { return 1; }
                if (Host.BootstrapImageLength[0] != 0) { return 2; }
                return 3;
            }
            """;
        var buffers = CreateBootstrapSourceBuffers(source)
            .Where(static buffer => buffer.Name != "BootstrapMainLocalNames"
                && buffer.Name != "BootstrapMainLocalValues")
            .ToImmutableArray()
            .Add(new HostBuffer("BootstrapMainLocalNames", PrimitiveType.Int32,
                [.. new int[shortNameTable ? 2 : source.Length]], true))
            .Add(new HostBuffer("BootstrapMainLocalValues", PrimitiveType.Int32,
                [.. new int[shortNameTable ? source.Length : 2]], true))
            .Add(new HostBuffer("BootstrapCodeBytes", PrimitiveType.Int32,
                [.. new int[16]], true))
            .Add(new HostBuffer("BootstrapCodeLength", PrimitiveType.Int32, [0], true))
            .Add(new HostBuffer("BootstrapImageBytes", PrimitiveType.Int32,
                [.. new int[1536]], true))
            .Add(new HostBuffer("BootstrapImageLength", PrimitiveType.Int32, [0], true));
        var components = s_bootstrapSourceComponents
            .Append("BootstrapInt32MainSource")
            .Append("BootstrapInt32MainLeaf")
            .Append("BootstrapInt32MainPeImage");
        var trees = await ReadComponentsAsync(components, rejectSource)
            .ConfigureAwait(true);
        var compilation = Compilation.Create(trees, buffers, policy);
        Assert.False(compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var execution = NativeJit.ExecuteWithData(program, (NativeRuntimePolicy)policy);
        Assert.Equal(0, execution.ExitCode);
        Assert.Equal(0, ReadInt32(execution.Data,
            execution.BufferOffsets[buffers.Length - 3]));
        Assert.Equal(0, ReadInt32(execution.Data,
            execution.BufferOffsets[buffers.Length - 1]));
        Assert.Equal(0, await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "namespace ; public int32 Main() => 42;")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "namespace ; public int32 Main() => 42;")]
    [InlineData(BoundRuntimePolicy.FailFast, "namespace Demo.; public int32 Main() => 42;")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "namespace Demo.; public int32 Main() => 42;")]
    [InlineData(BoundRuntimePolicy.FailFast, "namespace Demo..Core; public int32 Main() => 42;")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "namespace Demo..Core; public int32 Main() => 42;")]
    [InlineData(BoundRuntimePolicy.FailFast, "namespace Demo public int32 Main() => 42;")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "namespace Demo public int32 Main() => 42;")]
    [InlineData(BoundRuntimePolicy.FailFast, "namespace Demo; namespace Other; public int32 Main() => 42;")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "namespace Demo; namespace Other; public int32 Main() => 42;")]
    [InlineData(BoundRuntimePolicy.FailFast, "namespace Demo { public int32 Main() => 42; }")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "namespace Demo { public int32 Main() => 42; }")]
    [InlineData(BoundRuntimePolicy.FailFast, "public int32 Main() => 42; namespace Demo;")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "public int32 Main() => 42; namespace Demo;")]
    [InlineData(BoundRuntimePolicy.FailFast, "namespace Demo; internal int32 Answer() => 1; internal int32 Answer() => 2; public int32 Main() => Answer();")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "namespace Demo; internal int32 Answer() => 1; internal int32 Answer() => 2; public int32 Main() => Answer();")]
    public Task BootstrapFileScopedNamespaceRejectsMalformedOrDuplicateInputAsync(
        BoundRuntimePolicy policy, string sourceText)
        => EmitsOnlyTheDirectSourceSubsetAsync(policy, sourceText, false, 0);

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task BootstrapFileScopedNamespacePreservesForwardingChainAsync(
        BoundRuntimePolicy policy)
        => AssertBootstrapHelperCallChainAsync(
            policy, [20, 22], "+", 42, false, verifyNestedUnwind: true,
            forwardingCount: 3, fileScopedNamespace: "Demo.Core");

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, false)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false)]
    [InlineData(BoundRuntimePolicy.FailFast, true)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true)]
    public Task BootstrapHelperCallArgumentsFoldCheckedExpressionsAsync(
        BoundRuntimePolicy policy, bool forwardingChain)
    {
        if (forwardingChain)
        {
            return AssertBootstrapHelperCallChainAsync(
                policy, [3, 4, 9, 5, 1, 1], "+", 25, true,
                mainArgumentExpressions: [
                    "- ( (firstValue + 2) * 3 ) + 20", "8/2", "3*3", "7-2", "10%3", "6/4"]);
        }

        const string sumDeclaration =
            "internal int32 Sum6(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth) => first + second + third + fourth + fifth + sixth;";
        const string sourceText =
            $"{sumDeclaration} public int32 Main() {{ int32 firstValue = 1; int32 secondValue = 2; return Sum6(- ( (firstValue + secondValue) * 3 ) + 4, 8/2, 3*3, 7-2, 10%3, 6/4); }}";

        return EmitsOnlyTheDirectSourceSubsetAsync(
            policy, sourceText, true, 15, helperArguments: [-5, 4, 9, 5, 1, 1]);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task BootstrapHelperCallMinimumLiteralRemainsRepresentableAsync(
        BoundRuntimePolicy policy)
        => AssertBootstrapHelperCallChainAsync(
            policy, [int.MinValue], "", int.MinValue, false,
            mainArgumentExpressions: ["-2147483648"]);

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "(seed + 2) * 3", "first / 2 + 7 % 4", true, 66, 106)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "(seed + 2) * 3", "first / 2 + 7 % 4", true, 66, 106)]
    [InlineData(BoundRuntimePolicy.FailFast, "seed - 55", "-(first - 5) + 18 % 5", true, 23, 63)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "seed - 55", "-(first - 5) + 18 % 5", true, 23, 63)]
    [InlineData(BoundRuntimePolicy.FailFast, "2147483647 + 1", "first", false, 0, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "2147483647 + 1", "first", false, 0, 0)]
    [InlineData(BoundRuntimePolicy.FailFast, "seed / (40 - 40)", "first", false, 0, 0)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "seed / (40 - 40)", "first", false, 0, 0)]
    public Task BootstrapParameterizedHelperCallFoldsBoundedMainLocalExpressionsAsync(
        BoundRuntimePolicy policy, string firstInitializer, string secondInitializer,
        bool shouldEmit, int secondValue, int value)
    {
        const string helper =
            "internal int32 Sum2(int32 first, int32 second) => first + second;";
        var sourceText =
            $"{helper} public int32 Main() {{ int32 seed = 40; "
            + $"int32 first = {firstInitializer}; int32 second = {secondInitializer}; "
            + "return Sum2(second, seed); }";
        return EmitsOnlyTheDirectSourceSubsetAsync(
            policy, sourceText, shouldEmit, value,
            helperArguments: [shouldEmit ? secondValue : 0, 40]);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task BootstrapParameterizedHelperCallSupportsSourceSizedMainLocalsAsync(
        BoundRuntimePolicy policy)
    {
        const string helper =
            "internal int32 Sum2(int32 first, int32 second) => first + second;";

        string CreateSource(int localCount)
        {
            var declarations = string.Join(" ", Enumerable.Range(0, localCount)
                .Select(index => index == 0
                    ? "int32 local0 = 1;"
                    : $"int32 local{index} = local{index - 1} + 1;"));
            return $"{helper} public int32 Main() {{ {declarations} "
                + $"return Sum2(local{localCount - 1}, local{localCount - 2}); }}";
        }

        var minimumLocalCount = 0;
        var maximumLocalCount = ScannerHost.MaximumSourceLength;
        while (minimumLocalCount < maximumLocalCount)
        {
            var localCount = minimumLocalCount
                + (maximumLocalCount - minimumLocalCount + 1) / 2;
            if (CreateSource(localCount).Length <= ScannerHost.MaximumSourceLength)
            {
                minimumLocalCount = localCount;
            }
            else
            {
                maximumLocalCount = localCount - 1;
            }
        }

        var sourceText = CreateSource(minimumLocalCount);
        Assert.True(minimumLocalCount > 2);
        Assert.InRange(sourceText.Length, 1, ScannerHost.MaximumSourceLength);
        Assert.True(CreateSource(minimumLocalCount + 1).Length > ScannerHost.MaximumSourceLength);
        return EmitsOnlyTheDirectSourceSubsetAsync(
            policy, sourceText, true, minimumLocalCount * 2 - 1,
            helperArguments: [minimumLocalCount, minimumLocalCount - 1]);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, true)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true)]
    [InlineData(BoundRuntimePolicy.FailFast, false)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false)]
    public Task BootstrapParameterizedHelperCallRejectsInsufficientMainLocalTablesAsync(
        BoundRuntimePolicy policy, bool shortNameTable)
    {
        const string sourceText =
            "internal int32 Sum2(int32 first, int32 second) => first + second; "
            + "public int32 Main() { int32 first = 1; int32 second = first + 1; "
            + "int32 third = second + first; return Sum2(third, second); }";
        return EmitsOnlyTheDirectSourceSubsetAsync(
            policy, sourceText, false, 0, helperArguments: [3, 2],
            bootstrapMainLocalNameCapacity: shortNameTable ? 0 : sourceText.Length,
            bootstrapMainLocalValueCapacity: shortNameTable ? sourceText.Length : 0);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, false)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false)]
    [InlineData(BoundRuntimePolicy.FailFast, true)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true)]
    public Task BootstrapHelperCallLargeStackArgumentExpressionsFoldCheckedAsync(
        BoundRuntimePolicy policy, bool forwardingChain)
    {
        var arguments = Enumerable.Range(1, 32).ToArray();
        var mainArguments = arguments.Select(static argument =>
            argument.ToString(CultureInfo.InvariantCulture)).ToArray();
        mainArguments[0] = "- ( (1 + 2) * 3 ) + 2";
        mainArguments[3] = "20 / (5 + 5)";
        mainArguments[4] = "10 - ( -3 - 2 )";
        mainArguments[15] = "32 / (4 * 2)";
        mainArguments[31] = "18 % (5 + 1)";
        arguments[0] = -7;
        arguments[3] = 2;
        arguments[4] = 15;
        arguments[15] = 4;
        arguments[31] = 0;
        if (forwardingChain)
        {
            return AssertBootstrapHelperCallChainAsync(
                policy, arguments, "+", 484, false, forwardingCount: 2,
                mainArgumentExpressions: mainArguments);
        }

        var parameterNames = Enumerable.Range(0, arguments.Length)
            .Select(static index => $"value{index}").ToArray();
        var parameters = string.Join(", ",
            parameterNames.Select(static name => $"int32 {name}"));
        var sum = string.Join(" + ", parameterNames);
        var sourceText =
            $"internal int32 Sum32({parameters}) => {sum}; public int32 Main() => Sum32({string.Join(", ", mainArguments)});";
        return EmitsOnlyTheDirectSourceSubsetAsync(
            policy, sourceText, true, 484, helperArguments: arguments);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task BootstrapHelperCallArgumentCountScalesBeyondRegisterArgumentsAsync(
        BoundRuntimePolicy policy)
    {
        const int argumentCount = 256;
        var arguments = Enumerable.Range(1, argumentCount).ToArray();
        var parameterNames = Enumerable.Range(0, argumentCount)
            .Select(static index => $"value{index}").ToArray();
        var parameters = string.Join(", ",
            parameterNames.Select(static name => $"int32 {name}"));
        var sum = string.Join(" + ", parameterNames);
        var sourceText =
            $"internal int32 Sum({parameters}) => {sum}; public int32 Main() "
            + $"=> Sum({string.Join(", ", arguments)});";
        return TerrumScannerAndSourceEmitterProduceBootstrapPeImageAsync(
            policy, sourceText, arguments.Sum(), 6,
            helperArguments: arguments,
            imageCapacity: GetStackArgumentImageLength(argumentCount));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task BootstrapHelperCallArgumentCountScalesToSourceLimitAsync(
        BoundRuntimePolicy policy)
    {
        var (sourceText, arguments) = CreateMaximumSourceBoundedStackArgumentCase();
        return TerrumScannerAndSourceEmitterProduceBootstrapPeImageAsync(
            policy, sourceText, arguments.Sum(), 6,
            helperArguments: arguments,
            imageCapacity: GetStackArgumentImageLength(arguments.Length));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task BootstrapHelperCallSourceLimitRejectsInsufficientCodeCapacityAsync(
        BoundRuntimePolicy policy)
    {
        var (sourceText, arguments) = CreateMaximumSourceBoundedStackArgumentCase();
        var codeCapacity = GetStackArgumentCodeLength(arguments.Length) - 1;
        return HelperCallsRejectInsufficientCodeBuffersAsync(
            policy, sourceText, codeCapacity);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task BootstrapHelperCallArgumentGroupingScalesToScannerLimitAsync(
        BoundRuntimePolicy policy)
    {
        var minimumGroupingDepth = 0;
        var maximumGroupingDepth = ScannerHost.MaximumSourceLength;
        while (minimumGroupingDepth < maximumGroupingDepth)
        {
            var groupingDepth = minimumGroupingDepth
                + (maximumGroupingDepth - minimumGroupingDepth + 1) / 2;
            var candidateExpression = "-("
                + new string('(', groupingDepth)
                + "-(1+2)"
                + new string(')', groupingDepth)
                + ")";
            var source = CreateBootstrapHelperCallChainSource(
                [3], "", useMainLocal: false, forwardingCount: 1,
                mainArgumentExpressions: [candidateExpression]);
            if (source.Length <= ScannerHost.MaximumSourceLength)
            {
                minimumGroupingDepth = groupingDepth;
            }
            else
            {
                maximumGroupingDepth = groupingDepth - 1;
            }
        }

        var nestedExpression = "-("
            + new string('(', minimumGroupingDepth)
            + "-(1+2)"
            + new string(')', minimumGroupingDepth)
            + ")";
        var sourceText = CreateBootstrapHelperCallChainSource(
            [3], "", useMainLocal: false, forwardingCount: 1,
            mainArgumentExpressions: [nestedExpression]);
        Assert.InRange(sourceText.Length, 1, ScannerHost.MaximumSourceLength);
        var excessiveExpression = "-("
            + new string('(', minimumGroupingDepth + 1)
            + "-(1+2)"
            + new string(')', minimumGroupingDepth + 1)
            + ")";
        Assert.True(CreateBootstrapHelperCallChainSource(
                [3], "", useMainLocal: false, forwardingCount: 1,
                mainArgumentExpressions: [excessiveExpression]).Length
            > ScannerHost.MaximumSourceLength);
        return AssertBootstrapHelperCallChainAsync(
            policy, [3], "", 3, false,
            mainArgumentExpressions: [nestedExpression]);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task BootstrapHelperCallChainLocalInitializersScaleGroupingToScannerLimitAsync(
        BoundRuntimePolicy policy)
    {
        static string CreateInitializer(int groupingDepth)
        {
            return new string('(', groupingDepth)
                + "1+2"
                + new string(')', groupingDepth);
        }

        static string CreateSource(int groupingDepth)
        {
            return CreateBootstrapHelperCallChainSource(
                [3], "", useMainLocal: true, forwardingCount: 1,
                mainArgumentExpressions: ["value"],
                mainLocalDeclarations: $"int32 value={CreateInitializer(groupingDepth)};");
        }

        var minimumGroupingDepth = 0;
        var maximumGroupingDepth = ScannerHost.MaximumSourceLength;
        while (minimumGroupingDepth < maximumGroupingDepth)
        {
            var groupingDepth = minimumGroupingDepth
                + (maximumGroupingDepth - minimumGroupingDepth + 1) / 2;
            if (CreateSource(groupingDepth).Length <= ScannerHost.MaximumSourceLength)
            {
                minimumGroupingDepth = groupingDepth;
            }
            else
            {
                maximumGroupingDepth = groupingDepth - 1;
            }
        }

        Assert.InRange(CreateSource(minimumGroupingDepth).Length,
            1, ScannerHost.MaximumSourceLength);
        Assert.True(CreateSource(minimumGroupingDepth + 1).Length
            > ScannerHost.MaximumSourceLength);
        return AssertBootstrapHelperCallChainAsync(
            policy, [3], "", 3, true,
            mainArgumentExpressions: ["value"],
            mainLocalDeclarations: $"int32 value={CreateInitializer(minimumGroupingDepth)};");
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task BootstrapHelperCallChainLocalInitializersScaleNestedUnaryGroupingToScannerLimitAsync(
        BoundRuntimePolicy policy)
    {
        static string CreateInitializer(int groupingDepth)
        {
            return "-("
                + new string('(', groupingDepth)
                + "-(1+2)"
                + new string(')', groupingDepth)
                + ")";
        }

        static string CreateSource(int groupingDepth)
        {
            return CreateBootstrapHelperCallChainSource(
                [3], "", useMainLocal: true, forwardingCount: 1,
                mainArgumentExpressions: ["value"],
                mainLocalDeclarations: $"int32 value = {CreateInitializer(groupingDepth)};");
        }

        var minimumGroupingDepth = 0;
        var maximumGroupingDepth = ScannerHost.MaximumSourceLength;
        while (minimumGroupingDepth < maximumGroupingDepth)
        {
            var groupingDepth = minimumGroupingDepth
                + (maximumGroupingDepth - minimumGroupingDepth + 1) / 2;
            if (CreateSource(groupingDepth).Length <= ScannerHost.MaximumSourceLength)
            {
                minimumGroupingDepth = groupingDepth;
            }
            else
            {
                maximumGroupingDepth = groupingDepth - 1;
            }
        }

        Assert.InRange(CreateSource(minimumGroupingDepth).Length,
            1, ScannerHost.MaximumSourceLength);
        Assert.True(CreateSource(minimumGroupingDepth + 1).Length
            > ScannerHost.MaximumSourceLength);
        return AssertBootstrapHelperCallChainAsync(
            policy, [3], "", 3, true,
            mainArgumentExpressions: ["value"],
            mainLocalDeclarations: $"int32 value = {CreateInitializer(minimumGroupingDepth)};");
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task BootstrapHelperCallChainScalesMainLocalsToScannerLimitAsync(
        BoundRuntimePolicy policy)
    {
        static string CreateDeclarations(int localCount)
        {
            return string.Join(" ", Enumerable.Range(0, localCount)
                .Select(static index => index == 0
                    ? "int32 local0 = 1;"
                    : $"int32 local{index} = local{index - 1} + 1;"));
        }

        static string CreateSource(int localCount)
        {
            var source = CreateBootstrapHelperCallChainSource(
                [localCount], "", useMainLocal: true, forwardingCount: 1,
                mainArgumentExpressions: [$"local{localCount - 1}"],
                mainLocalDeclarations: CreateDeclarations(localCount));
            return $"namespace Demo.Core; {source}";
        }

        var minimumLocalCount = 1;
        var maximumLocalCount = ScannerHost.MaximumSourceLength;
        while (minimumLocalCount < maximumLocalCount)
        {
            var localCount = minimumLocalCount
                + (maximumLocalCount - minimumLocalCount + 1) / 2;
            if (CreateSource(localCount).Length <= ScannerHost.MaximumSourceLength)
            {
                minimumLocalCount = localCount;
            }
            else
            {
                maximumLocalCount = localCount - 1;
            }
        }

        Assert.InRange(CreateSource(minimumLocalCount).Length,
            1, ScannerHost.MaximumSourceLength);
        Assert.True(CreateSource(minimumLocalCount + 1).Length
            > ScannerHost.MaximumSourceLength);
        var declarations = CreateDeclarations(minimumLocalCount);
        return AssertBootstrapHelperCallChainAsync(
            policy, [minimumLocalCount], "", minimumLocalCount, true,
            mainArgumentExpressions: [$"local{minimumLocalCount - 1}"],
            mainLocalDeclarations: declarations,
            fileScopedNamespace: "Demo.Core");
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, true)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true)]
    [InlineData(BoundRuntimePolicy.FailFast, false)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false)]
    public async Task BootstrapHelperCallChainsRejectInsufficientMainLocalCapacityAsync(
        BoundRuntimePolicy policy, bool shortNameTable)
    {
        const string sourceText =
            "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); "
            + "public int32 Main() { int32 first = 1; int32 second = first + 1; "
            + "return A(second); }";
        var source = SourceText.From(sourceText);
        var rejectSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (TryEmitBootstrapInt32MainSource()) { return 1; }
                if (Host.BootstrapCodeLength[0] != 0) { return 2; }
                if (Host.BootstrapImageLength[0] != 0) { return 3; }
                return 0;
            }
            """;
        var buffers = CreateBootstrapSourceBuffers(source)
            .Where(static buffer => buffer.Name != "BootstrapMainLocalNames"
                && buffer.Name != "BootstrapMainLocalValues")
            .ToImmutableArray()
            .Add(new HostBuffer("BootstrapMainLocalNames", PrimitiveType.Int32,
                [.. new int[shortNameTable ? 1 : source.Length]], true))
            .Add(new HostBuffer("BootstrapMainLocalValues", PrimitiveType.Int32,
                [.. new int[shortNameTable ? source.Length : 1]], true))
            .Add(new HostBuffer("BootstrapCodeBytes", PrimitiveType.Int32,
                [.. new int[GetBootstrapCallChainCodeLength(1, false)]], true))
            .Add(new HostBuffer("BootstrapCodeLength", PrimitiveType.Int32, [0], true))
            .Add(new HostBuffer("BootstrapImageBytes", PrimitiveType.Int32,
                [.. new int[65536]], true))
            .Add(new HostBuffer("BootstrapImageLength", PrimitiveType.Int32, [0], true));
        var components = s_bootstrapSourceComponents
            .Append("BootstrapInt32MainSource")
            .Append("BootstrapInt32MainLeaf")
            .Append("BootstrapInt32MainPeImage");
        var trees = await ReadComponentsAsync(components, rejectSource)
            .ConfigureAwait(true);
        var compilation = Compilation.Create(trees, buffers, policy);
        Assert.False(compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var execution = NativeJit.ExecuteWithData(program, (NativeRuntimePolicy)policy);
        Assert.Equal(0, execution.ExitCode);
        Assert.Equal(0, ReadInt32(execution.Data,
            execution.BufferOffsets[buffers.Length - 3]));
        Assert.Equal(0, ReadInt32(execution.Data,
            execution.BufferOffsets[buffers.Length - 1]));
        Assert.Equal(0, await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 first = 7; int32 result = first - 2;", "result", 5)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 first = 7; int32 result = first - 2;", "result", 5)]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 first = 3; int32 result = first * 4;", "result", 12)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 first = 3; int32 result = first * 4;", "result", 12)]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 first = 24; int32 result = first / 3;", "result", 8)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 first = 24; int32 result = first / 3;", "result", 8)]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 first = 17; int32 result = first % 5;", "result", 2)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 first = 17; int32 result = first % 5;", "result", 2)]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 first = 2 + 3 * 4; int32 result = -(first - 20) + 0 * 7;", "result", 6)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 first = 2 + 3 * 4; int32 result = -(first - 20) + 0 * 7;", "result", 6)]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 result = -7;", "result", -7)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 result = -7;", "result", -7)]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 result = -2147483648;", "result", int.MinValue)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 result = -2147483648;", "result", int.MinValue)]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 first = 1; int32 result = first + 2 + 3;", "result", 6)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 first = 1; int32 result = first + 2 + 3;", "result", 6)]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 first = -3; int32 result = 7 + first;", "result", 4)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 first = -3; int32 result = 7 + first;", "result", 4)]
    [InlineData(BoundRuntimePolicy.FailFast, "int32 result = -((-(1 + 2)));", "result", 3)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "int32 result = -((-(1 + 2)));", "result", 3)]
    public Task BootstrapHelperCallChainMainLocalsFoldBoundedArithmeticAsync(
        BoundRuntimePolicy policy, string localDeclarations, string resultLocal,
        int expectedResult)
        => AssertBootstrapHelperCallChainAsync(
            policy, [expectedResult], "", expectedResult, true,
            mainArgumentExpressions: [resultLocal],
            mainLocalDeclarations: localDeclarations);

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, false, "2147483647 + 1")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false, "2147483647 + 1")]
    [InlineData(BoundRuntimePolicy.FailFast, true, "2147483647 + 1")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true, "2147483647 + 1")]
    [InlineData(BoundRuntimePolicy.FailFast, false, "50000 * 50000")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false, "50000 * 50000")]
    [InlineData(BoundRuntimePolicy.FailFast, true, "50000 * 50000")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true, "50000 * 50000")]
    [InlineData(BoundRuntimePolicy.FailFast, false, "1 / 0")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false, "1 / 0")]
    [InlineData(BoundRuntimePolicy.FailFast, true, "1 / 0")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true, "1 / 0")]
    [InlineData(BoundRuntimePolicy.FailFast, false, "(1 / 0)")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false, "(1 / 0)")]
    [InlineData(BoundRuntimePolicy.FailFast, true, "(1 / 0)")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true, "(1 / 0)")]
    [InlineData(BoundRuntimePolicy.FailFast, false, "1 % 0")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false, "1 % 0")]
    [InlineData(BoundRuntimePolicy.FailFast, true, "1 % 0")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true, "1 % 0")]
    [InlineData(BoundRuntimePolicy.FailFast, false, "2147483647+1*1")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false, "2147483647+1*1")]
    [InlineData(BoundRuntimePolicy.FailFast, true, "2147483647+1*1")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true, "2147483647+1*1")]
    [InlineData(BoundRuntimePolicy.FailFast, false, "50000*50000/50000")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false, "50000*50000/50000")]
    [InlineData(BoundRuntimePolicy.FailFast, true, "50000*50000/50000")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true, "50000*50000/50000")]
    [InlineData(BoundRuntimePolicy.FailFast, false, "(2147483647 + 1)")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false, "(2147483647 + 1)")]
    [InlineData(BoundRuntimePolicy.FailFast, true, "(2147483647 + 1)")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true, "(2147483647 + 1)")]
    [InlineData(BoundRuntimePolicy.FailFast, false, "1 + ( )")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false, "1 + ( )")]
    [InlineData(BoundRuntimePolicy.FailFast, true, "1 + ( )")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true, "1 + ( )")]
    [InlineData(BoundRuntimePolicy.FailFast, false, "(1 + 2")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false, "(1 + 2")]
    [InlineData(BoundRuntimePolicy.FailFast, true, "(1 + 2")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true, "(1 + 2")]
    [InlineData(BoundRuntimePolicy.FailFast, false, "1 + (2 + 3) )")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false, "1 + (2 + 3) )")]
    [InlineData(BoundRuntimePolicy.FailFast, true, "1 + (2 + 3) )")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true, "1 + (2 + 3) )")]
    [InlineData(BoundRuntimePolicy.FailFast, false, "- - (1 + 2)")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false, "- - (1 + 2)")]
    [InlineData(BoundRuntimePolicy.FailFast, true, "- - (1 + 2)")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true, "- - (1 + 2)")]
    [InlineData(BoundRuntimePolicy.FailFast, false, "- ( -2147483648 )")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false, "- ( -2147483648 )")]
    [InlineData(BoundRuntimePolicy.FailFast, true, "- ( -2147483648 )")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true, "- ( -2147483648 )")]
    [InlineData(BoundRuntimePolicy.FailFast, false, "-2147483648-1")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false, "-2147483648-1")]
    [InlineData(BoundRuntimePolicy.FailFast, true, "-2147483648-1")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true, "-2147483648-1")]
    [InlineData(BoundRuntimePolicy.FailFast, false, "-2147483648 / -1")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false, "-2147483648 / -1")]
    [InlineData(BoundRuntimePolicy.FailFast, true, "-2147483648 / -1")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true, "-2147483648 / -1")]
    [InlineData(BoundRuntimePolicy.FailFast, false, "-2147483648 % -1")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false, "-2147483648 % -1")]
    [InlineData(BoundRuntimePolicy.FailFast, true, "-2147483648 % -1")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true, "-2147483648 % -1")]
    [InlineData(BoundRuntimePolicy.FailFast, false, "-50000*50000")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, false, "-50000*50000")]
    [InlineData(BoundRuntimePolicy.FailFast, true, "-50000*50000")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, true, "-50000*50000")]
    public Task InvalidBootstrapCallArgumentExpressionsRejectBeforePublicationAsync(
        BoundRuntimePolicy policy, bool forwardingChain, string firstArgument)
    {
        const string sumDeclaration =
            "internal int32 Sum6(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth) => first + second + third + fourth + fifth + sixth;";
        var sourceText = sumDeclaration;
        var target = "Sum6";
        if (forwardingChain)
        {
            sourceText =
                "internal int32 B(int32 b0, int32 b1, int32 b2, int32 b3, int32 b4, int32 b5) => b0 + b1 + b2 + b3 + b4 + b5;"
                + " internal int32 A(int32 b0, int32 b1, int32 b2, int32 b3, int32 b4, int32 b5) => B(b0, b1, b2, b3, b4, b5);";
            target = "A";
        }
        sourceText +=
            $" public int32 Main() => {target}({firstArgument}, 2, 3, 4, 5, 6);";

        return EmitsOnlyTheDirectSourceSubsetAsync(policy, sourceText, false, 0);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task NegatingMinimumLocalInBootstrapCallArgumentRejectsBeforePublicationAsync(
        BoundRuntimePolicy policy)
    {
        const string sourceText =
            "internal int32 Sum6(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth) => first + second + third + fourth + fifth + sixth;"
            + " public int32 Main() { int32 minimum = -2147483648; return Sum6(-minimum, 2, 3, 4, 5, 6); }";
        return EmitsOnlyTheDirectSourceSubsetAsync(policy, sourceText, false, 0);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 B(int32 b0, int32 b1) => b0 + b1; internal int32 A(int32 a0) => B(a0); public int32 Main() => A(1);")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 B(int32 b0, int32 b1) => b0 + b1; internal int32 A(int32 a0) => B(a0); public int32 Main() => A(1);")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 B(int32 b0, int32 b1) => b0 + b1; internal int32 A(int32 a0, int32 a1) => B(a1, a0); public int32 Main() => A(1, 2);")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 B(int32 b0, int32 b1) => b0 + b1; internal int32 A(int32 a0, int32 a1) => B(a1, a0); public int32 Main() => A(1, 2);")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 B(int32 b0, int32 b1) => b0 + b1; internal int32 A(int32 a0, int32 a1) => B(a0, a0); public int32 Main() => A(1, 2);")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 B(int32 b0, int32 b1) => b0 + b1; internal int32 A(int32 a0, int32 a1) => B(a0, a0); public int32 Main() => A(1, 2);")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 B(int32 b0, int32 b1) => b0 + b1; internal int32 A(int32 a0, int32 a1) => B(a0 + 1, a1); public int32 Main() => A(1, 2);")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 B(int32 b0, int32 b1) => b0 + b1; internal int32 A(int32 a0, int32 a1) => B(a0 + 1, a1); public int32 Main() => A(1, 2);")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 B(int32 b0, int32 b1) => b0 + b1; internal int32 B(int32 a0, int32 a1) => B(a0, a1); public int32 Main() => B(1, 2);")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 B(int32 b0, int32 b1) => b0 + b1; internal int32 B(int32 a0, int32 a1) => B(a0, a1); public int32 Main() => B(1, 2);")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 B(int32 b0, int32 b1) => b0 + b1; internal int32 A(int32 a0, int32 a1) => B(a0, a1); public int32 Main() => A(1, 2); internal int32 C() => 3;")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 B(int32 b0, int32 b1) => b0 + b1; internal int32 A(int32 a0, int32 a1) => B(a0, a1); public int32 Main() => A(1, 2); internal int32 C() => 3;")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); public int32 Main() { int32 first = 1; int32 first = 2; return A(first); }")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); public int32 Main() { int32 first = 1; int32 first = 2; return A(first); }")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); public int32 Main() { int32 A = 1; return A(A); }")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); public int32 Main() { int32 A = 1; return A(A); }")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); public int32 Main() { int32 B = 1; return A(B); }")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); public int32 Main() { int32 B = 1; return A(B); }")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); public int32 Main() { int32 Main = 1; return A(Main); }")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); public int32 Main() { int32 Main = 1; return A(Main); }")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); public int32 Main() { int32 first = second; int32 second = 2; return A(first); }")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); public int32 Main() { int32 first = second; int32 second = 2; return A(first); }")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); public int32 Main() { int32 first = 2147483647 + 1; return A(first); }")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); public int32 Main() { int32 first = 2147483647 + 1; return A(first); }")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); public int32 Main() { int32 first = 50000 * 50000; return A(first); }")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); public int32 Main() { int32 first = 50000 * 50000; return A(first); }")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); public int32 Main() { int32 first = 1 / 0; return A(first); }")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); public int32 Main() { int32 first = 1 / 0; return A(first); }")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); public int32 Main() { int32 first = 1 % 0; return A(first); }")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); public int32 Main() { int32 first = 1 % 0; return A(first); }")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); public int32 Main() { int32 minimum = -2147483648; int32 result = -minimum; return A(result); }")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 B(int32 b0) => b0; internal int32 A(int32 a0) => B(a0); public int32 Main() { int32 minimum = -2147483648; int32 result = -minimum; return A(result); }")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 B(int32 b0, int32 b1) => b0 + b1; internal int32 A(int32 a0, int32 a1) => B(a0, a1); public int32 Main() => A(2147483647, 1);")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 B(int32 b0, int32 b1) => b0 + b1; internal int32 A(int32 a0, int32 a1) => B(a0, a1); public int32 Main() => A(2147483647, 1);")]
    public Task UnsupportedBootstrapHelperCallChainsRejectBeforePublicationAsync(
        BoundRuntimePolicy policy, string sourceText)
        => EmitsOnlyTheDirectSourceSubsetAsync(policy, sourceText, false, 0);

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task BootstrapHelperCallChainsRejectInsufficientCodeCapacityAsync(
        BoundRuntimePolicy policy)
    {
        var arguments = new[] { 1, 2, 3, 4, 5, 6 };
        var source = SourceText.From(CreateBootstrapHelperCallChainSource(
            arguments, "+", useMainLocal: false, forwardingCount: 3));
        var codeCapacity = GetBootstrapCallChainCodeLength(
            arguments.Length, multiplication: false, forwardingCount: 3) - 1;
        const string rejectSource = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (TryEmitBootstrapInt32MainSource()
                    || Host.BootstrapCodeLength[0] != 0) { return 1; }
                return 0;
            }
            """;
        var buffers = CreateBootstrapSourceBuffers(source)
            .Add(new HostBuffer("BootstrapCodeBytes", PrimitiveType.Int32,
                [.. new int[codeCapacity]], true))
            .Add(new HostBuffer("BootstrapCodeLength", PrimitiveType.Int32, [0], true));
        var components = s_bootstrapSourceComponents
            .Append("BootstrapInt32MainSource")
            .Append("BootstrapInt32MainLeaf");
        var trees = await ReadComponentsAsync(components, rejectSource)
            .ConfigureAwait(true);
        var compilation = Compilation.Create(trees, buffers, policy);
        Assert.False(compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(0, NativeJit.ExecuteWithData(
            program, (NativeRuntimePolicy)policy).ExitCode);
        Assert.Equal(0, await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task BootstrapHelperCallChainsRejectInsufficientImageCapacityAsync(
        BoundRuntimePolicy policy)
    {
        var arguments = Enumerable.Range(1, 18).ToArray();
        return AssertBootstrapHelperCallChainAsync(
            policy, arguments, "+", 171, false,
            imageCapacity: GetBootstrapCallChainImageLength(
                arguments.Length, multiplication: false, forwardingCount: 3) - 1,
            forwardingCount: 3,
            expectImageRejection: true);
    }

    private async Task AssertBootstrapHelperCallChainAsync(
        BoundRuntimePolicy policy, int[] arguments, string operation,
        int expectedResult, bool useMainLocal, bool verifyNestedUnwind = false,
        int imageCapacity = 65536, bool expectImageRejection = false,
        int forwardingCount = 1, string? fileScopedNamespace = null,
        string[]? mainArgumentExpressions = null,
        string? mainLocalDeclarations = null, int? codeCapacity = null,
        bool expectCodeRejection = false)
    {
        var sourceText = CreateBootstrapHelperCallChainSource(
            arguments, operation, useMainLocal, forwardingCount,
            mainArgumentExpressions, mainLocalDeclarations);
        if (fileScopedNamespace is not null)
        {
            sourceText = $"namespace {fileScopedNamespace}; {sourceText}";
        }
        var source = SourceText.From(sourceText);
        var multiplication = operation == "*";
        var requiredCodeCapacity = GetBootstrapCallChainCodeLength(
            arguments.Length, multiplication, forwardingCount);
        codeCapacity ??= requiredCodeCapacity;
        var components = s_bootstrapSourceComponents
            .Append("BootstrapInt32MainSource")
            .Append("BootstrapInt32MainLeaf")
            .Append("BootstrapInt32MainPeImage");
        var trees = await ReadComponentsAsync(components, EndToEndBootstrapSourceOnly)
            .ConfigureAwait(true);
        var buffers = CreateBootstrapSourceBuffers(source)
            .Add(new HostBuffer("BootstrapCodeBytes", PrimitiveType.Int32,
                [.. new int[codeCapacity.Value]], true))
            .Add(new HostBuffer("BootstrapCodeLength", PrimitiveType.Int32, [0], true))
            .Add(new HostBuffer("BootstrapImageBytes", PrimitiveType.Int32,
                [.. new int[imageCapacity]], true))
            .Add(new HostBuffer("BootstrapImageLength", PrimitiveType.Int32, [0], true));
        var compilation = Compilation.Create(trees, buffers, policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var execution = NativeJit.ExecuteWithData(program, (NativeRuntimePolicy)policy);

        var codeLength = ReadInt32(execution.Data,
            execution.BufferOffsets[buffers.Length - 3]);
        var imageLength = ReadInt32(execution.Data,
            execution.BufferOffsets[buffers.Length - 1]);
        var expectedImageLength = GetBootstrapCallChainImageLength(
            arguments.Length, multiplication, forwardingCount);
        var expectedExitCode = expectCodeRejection ? -1
            : expectImageRejection ? -2 : expectedImageLength;
        Assert.True(execution.ExitCode == expectedExitCode,
            $"Expected exit code {expectedExitCode}, got {execution.ExitCode}; "
                + $"codeLength={codeLength}, imageLength={imageLength}.");
        if (expectCodeRejection)
        {
            Assert.Equal(0, codeLength);
            Assert.Equal(0, imageLength);
            return;
        }
        Assert.Equal(requiredCodeCapacity, codeLength);
        if (expectImageRejection)
        {
            Assert.Equal(0, ReadInt32(
                execution.Data, execution.BufferOffsets[buffers.Length - 1]));
            return;
        }
        var code = new byte[codeLength];
        for (var index = 0; index < code.Length; index++)
        {
            var value = ReadInt32(execution.Data,
                execution.BufferOffsets[buffers.Length - 4] + index * sizeof(int));
            Assert.InRange(value, 0, byte.MaxValue);
            code[index] = (byte)value;
        }

        var frameSize = GetStackArgumentFrameSize(arguments.Length);
        var prologLength = frameSize >= 0x1000 ? 0x32 : frameSize > 0x7F ? 7 : 4;
        var epilogueLength = frameSize > 0x7F ? 8 : 5;
        var mainCallOffset = GetStackArgumentCallerEndOffset(arguments.Length);
        var (mainFunctionEnd, forwardingFunctionEnd, expectedCodeLength) =
            GetBootstrapCallChainLayout(
                arguments.Length, multiplication, forwardingCount);
        Assert.Equal(codeLength, expectedCodeLength);
        AssertBootstrapCallChainFrameProlog(code, 0, frameSize);
        Assert.Equal(0xE8, code[mainCallOffset]);
        Assert.Equal(epilogueLength, ReadInt32(code, mainCallOffset + 1));
        Assert.Equal(mainFunctionEnd, mainCallOffset + 5
            + ReadInt32(code, mainCallOffset + 1));

        var forwardingLength = forwardingFunctionEnd - mainFunctionEnd;
        for (var forwardingIndex = 0; forwardingIndex < forwardingCount; forwardingIndex++)
        {
            var forwardingStart = mainFunctionEnd + forwardingIndex * forwardingLength;
            AssertBootstrapCallChainFrameProlog(code, forwardingStart, frameSize);
            var copyOffset = forwardingStart + prologLength;
            for (var argumentIndex = 4; argumentIndex < arguments.Length; argumentIndex++)
            {
                var incomingOffset = frameSize + 0x28 + (argumentIndex - 4) * 8;
                var outgoingOffset = 0x20 + (argumentIndex - 4) * 8;
                copyOffset = AssertBootstrapForwardedStackArgument(
                    code, copyOffset, incomingOffset, outgoingOffset);
            }
            var forwardingCallOffset = forwardingStart + forwardingLength
                - 5 - epilogueLength;
            Assert.Equal(forwardingCallOffset, copyOffset);
            Assert.Equal(0xE8, code[forwardingCallOffset]);
            Assert.Equal(epilogueLength, ReadInt32(code, forwardingCallOffset + 1));
            Assert.Equal(forwardingStart + forwardingLength,
                forwardingCallOffset + 5 + ReadInt32(code, forwardingCallOffset + 1));
        }

        var metadataOffset = (codeLength + 3) & ~3;
        var functionCount = forwardingCount + 1;
        var functionTableSize = functionCount * 12;
        var sectionVirtualSize = metadataOffset + functionCount * 20;
        var functionTableRva = 0x1000 + metadataOffset;
        var functionTableOffset = 0x400 + metadataOffset;
        var image = new byte[expectedImageLength];
        var imageBytesOffset = execution.BufferOffsets[buffers.Length - 2];
        for (var index = 0; index < image.Length; index++)
        {
            var value = ReadInt32(execution.Data, imageBytesOffset + index * sizeof(int));
            Assert.InRange(value, 0, byte.MaxValue);
            image[index] = (byte)value;
        }
        Assert.Equal(functionTableRva, ReadInt32(image, 0x120));
        Assert.Equal(functionTableSize, ReadInt32(image, 0x124));
        for (var functionIndex = 0; functionIndex < functionCount; functionIndex++)
        {
            var functionStart = functionIndex == 0 ? 0
                : mainFunctionEnd + (functionIndex - 1) * forwardingLength;
            var functionEnd = functionIndex == 0 ? mainFunctionEnd
                : functionStart + forwardingLength;
            var functionOffset = functionTableOffset + functionIndex * 12;
            Assert.Equal(0x1000 + functionStart,
                ReadInt32(image, functionOffset));
            Assert.Equal(0x1000 + functionEnd,
                ReadInt32(image, functionOffset + 4));
            Assert.Equal(functionTableRva + functionTableSize + functionIndex * 8,
                ReadInt32(image, functionOffset + 8));
            AssertBootstrapCallChainUnwindInfo(
                image, functionTableOffset + functionTableSize + functionIndex * 8,
                frameSize, prologLength);
        }
        Assert.Equal(sectionVirtualSize, ReadInt32(image, 0x190));
        Assert.Equal(code.Select(static value => (int)value),
            image.AsSpan(0x400, codeLength).ToArray().Select(static value => (int)value));
        Assert.All(image.AsSpan(0x400 + sectionVirtualSize).ToArray(),
            static value => Assert.Equal(0, value));

        Assert.Equal(expectedResult, await ExecuteImageAsync(image).ConfigureAwait(true));
        Assert.Equal(expectedImageLength,
            await ExecuteAotAsync(program, (NativeRuntimePolicy)policy).ConfigureAwait(true));
        if (verifyNestedUnwind)
        {
            var forwardingAlignmentImage = CreateBootstrapCallChainAlignmentProbeImage(
                image, arguments.Length, probeForwardingHelper: true);
            var leafAlignmentImage = CreateBootstrapCallChainAlignmentProbeImage(
                image, arguments.Length, probeForwardingHelper: false);
            Assert.Equal(8, await ExecuteImageAsync(forwardingAlignmentImage)
                .ConfigureAwait(true));
            Assert.Equal(8, await ExecuteImageAsync(leafAlignmentImage)
                .ConfigureAwait(true));
            await AssertBootstrapAllocationUnwindsAsync(
                image, frameSize, expectedResult, callChain: true,
                forwardingCount: forwardingCount).ConfigureAwait(true);
        }
    }

    private static string CreateBootstrapHelperCallChainSource(
        int[] arguments, string operation, bool useMainLocal, int forwardingCount = 1,
        string[]? mainArgumentExpressions = null,
        string? mainLocalDeclarations = null)
    {
        var leafNames = Enumerable.Range(0, arguments.Length)
            .Select(index => arguments.Length <= 32 ? $"b{index}"
                : string.Concat("p", (char)('a' + index / 26),
                    (char)('a' + index % 26)))
            .ToArray();
        var forwardingNames = leafNames;
        var leafParameters = string.Join(",", leafNames.Select(static name => $"int32 {name}"));
        var forwardingParameters = string.Join(",", forwardingNames.Select(static name => $"int32 {name}"));
        var leafBody = arguments.Length == 1
            ? leafNames[0]
            : string.Join(operation, leafNames);
        var forwardingArguments = string.Join(",", forwardingNames);
        var mainArguments = mainArgumentExpressions ?? arguments.Select(static argument =>
            argument.ToString(CultureInfo.InvariantCulture)).ToArray();
        if (useMainLocal && mainArgumentExpressions is null)
        {
            mainArguments[0] = "firstValue";
        }
        var mainCall = $"{GetBootstrapCallChainForwardingName(forwardingCount - 1)}"
            + $"({string.Join(",", mainArguments)})";
        var mainLocalSource = mainLocalDeclarations
            ?? $"int32 firstValue={arguments[0].ToString(CultureInfo.InvariantCulture)};";
        var mainBody = useMainLocal
            ? $"{{ {mainLocalSource}return {mainCall}; }}"
            : $"=> {mainCall}";
        var source = $"internal int32 B({leafParameters})=>{leafBody};";
        for (var forwardingIndex = 0; forwardingIndex < forwardingCount; forwardingIndex++)
        {
            var targetName = forwardingIndex == 0
                ? "B"
                : GetBootstrapCallChainForwardingName(forwardingIndex - 1);
            source += $"internal int32 {GetBootstrapCallChainForwardingName(forwardingIndex)}"
                + $"({forwardingParameters})=>{targetName}({forwardingArguments});";
        }
        return source + $"public int32 Main(){mainBody}{(useMainLocal ? "" : ";")}";
    }

    private static string GetBootstrapCallChainForwardingName(int forwardingIndex)
        => forwardingIndex == 0 ? "A" : $"A{forwardingIndex + 1}";

    private static int GetBootstrapCallChainCodeLength(
        int argumentCount, bool multiplication, int forwardingCount = 1)
        => GetBootstrapCallChainLayout(
            argumentCount, multiplication, forwardingCount).CodeLength;

    private static int GetBootstrapCallChainImageLength(
        int argumentCount, bool multiplication, int forwardingCount = 1)
    {
        var codeLength = GetBootstrapCallChainCodeLength(
            argumentCount, multiplication, forwardingCount);
        var sectionVirtualSize = ((codeLength + 3) & ~3)
            + (forwardingCount + 1) * 20;
        var sectionRawSize = (sectionVirtualSize + 0x1FF) & ~0x1FF;
        return 0x400 + sectionRawSize;
    }

    private static (int MainFunctionEnd, int ForwardingFunctionEnd, int CodeLength)
        GetBootstrapCallChainLayout(
            int argumentCount, bool multiplication, int forwardingCount = 1)
    {
        var frameSize = GetStackArgumentFrameSize(argumentCount);
        var epilogueLength = frameSize > 0x7F ? 8 : 5;
        var mainFunctionEnd = GetStackArgumentCallerEndOffset(argumentCount)
            + 5 + epilogueLength;
        var forwardingLength = (frameSize >= 0x1000 ? 0x32
                : frameSize > 0x7F ? 7 : 4)
            + 5 + epilogueLength;
        for (var argumentIndex = 4; argumentIndex < argumentCount; argumentIndex++)
        {
            var incomingOffset = frameSize + 0x28 + (argumentIndex - 4) * 8;
            var outgoingOffset = 0x20 + (argumentIndex - 4) * 8;
            forwardingLength += (incomingOffset > 0x7F ? 7 : 4)
                + (outgoingOffset > 0x7F ? 7 : 4);
        }
        var baseCodeLength = GetStackArgumentCodeLength(argumentCount, multiplication);
        return (mainFunctionEnd, mainFunctionEnd + forwardingLength,
            baseCodeLength + forwardingCount * forwardingLength);
    }

    private static int AssertBootstrapForwardedStackArgument(
        byte[] code, int offset, int incomingOffset, int outgoingOffset)
    {
        if (incomingOffset > 0x7F)
        {
            Assert.Equal(0x8B, code[offset]);
            Assert.Equal(0x84, code[offset + 1]);
            Assert.Equal(0x24, code[offset + 2]);
            Assert.Equal(incomingOffset, ReadInt32(code, offset + 3));
            offset += 7;
        }
        else
        {
            Assert.Equal(0x8B, code[offset]);
            Assert.Equal(0x44, code[offset + 1]);
            Assert.Equal(0x24, code[offset + 2]);
            Assert.Equal(incomingOffset, code[offset + 3]);
            offset += 4;
        }
        if (outgoingOffset > 0x7F)
        {
            Assert.Equal(0x89, code[offset]);
            Assert.Equal(0x84, code[offset + 1]);
            Assert.Equal(0x24, code[offset + 2]);
            Assert.Equal(outgoingOffset, ReadInt32(code, offset + 3));
            return offset + 7;
        }
        Assert.Equal(0x89, code[offset]);
        Assert.Equal(0x44, code[offset + 1]);
        Assert.Equal(0x24, code[offset + 2]);
        Assert.Equal(outgoingOffset, code[offset + 3]);
        return offset + 4;
    }

    private static byte[] CreateBootstrapCallChainAlignmentProbeImage(
        byte[] image, int argumentCount, bool probeForwardingHelper, int forwardingCount = 1)
    {
        var (mainFunctionEnd, forwardingFunctionEnd, _) = GetBootstrapCallChainLayout(
            argumentCount, multiplication: false, forwardingCount: forwardingCount);
        var forwardingLength = forwardingFunctionEnd - mainFunctionEnd;
        var leafStart = mainFunctionEnd + forwardingCount * forwardingLength;
        var codeOffset = 0x400
            + (probeForwardingHelper ? mainFunctionEnd : leafStart);
        var probeImage = (byte[])image.Clone();
        new byte[] { 0x89, 0xE0, 0x83, 0xE0, 0x0F, 0xC3 }
            .CopyTo(probeImage, codeOffset);
        return probeImage;
    }

    private static void AssertBootstrapCallChainFrameProlog(
        byte[] code, int offset, int frameSize)
    {
        if (frameSize >= 0x1000)
        {
            Assert.Equal(0x49, code[offset]);
            Assert.Equal(0x89, code[offset + 1]);
            Assert.Equal(0xE3, code[offset + 2]);
            Assert.Equal(frameSize, ReadInt32(code, offset + 5));
            Assert.Equal(0x48, code[offset + 43]);
            Assert.Equal(0x81, code[offset + 44]);
            Assert.Equal(0xEC, code[offset + 45]);
            Assert.Equal(frameSize, ReadInt32(code, offset + 46));
        }
        else if (frameSize > 0x7F)
        {
            Assert.Equal(0x48, code[offset]);
            Assert.Equal(0x81, code[offset + 1]);
            Assert.Equal(0xEC, code[offset + 2]);
            Assert.Equal(frameSize, ReadInt32(code, offset + 3));
        }
        else
        {
            Assert.Equal(0x48, code[offset]);
            Assert.Equal(0x83, code[offset + 1]);
            Assert.Equal(0xEC, code[offset + 2]);
            Assert.Equal(frameSize, code[offset + 3]);
        }
    }

    private static void AssertBootstrapCallChainUnwindInfo(
        byte[] image, int offset, int frameSize, int prologLength)
    {
        var largeFrame = frameSize > 0x7F;
        Assert.Equal(1, image[offset]);
        Assert.Equal(prologLength, image[offset + 1]);
        Assert.Equal(largeFrame ? 2 : 1, image[offset + 2]);
        Assert.Equal(0, image[offset + 3]);
        Assert.Equal(prologLength, image[offset + 4]);
        Assert.Equal(largeFrame ? 0x01 : ((frameSize / 8 - 1) << 4) | 0x02,
            image[offset + 5]);
        Assert.Equal(largeFrame ? frameSize / 8 & 0xFF : 0, image[offset + 6]);
        Assert.Equal(largeFrame ? frameSize / 8 >> 8 & 0xFF : 0, image[offset + 7]);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Mul(int32 first, int32 second) => first * second; public int32 Main() => Mul(-7, 6);", -42, new int[] { -7, 6 })]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Mul(int32 first, int32 second) => first * second; public int32 Main() => Mul(-7, 6);", -42, new int[] { -7, 6 })]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Mul4(int32 first, int32 second, int32 third, int32 fourth) => first * second * third * fourth; public int32 Main() => Mul4(-2, -3, 2, -1);", -12, new int[] { -2, -3, 2, -1 })]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Mul4(int32 first, int32 second, int32 third, int32 fourth) => first * second * third * fourth; public int32 Main() => Mul4(-2, -3, 2, -1);", -12, new int[] { -2, -3, 2, -1 })]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Mul5(int32 first, int32 second, int32 third, int32 fourth, int32 fifth) => first * second * third * fourth * fifth; public int32 Main() => Mul5(2, 3, 4, 5, 6);", 720, new int[] { 2, 3, 4, 5, 6 })]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Mul5(int32 first, int32 second, int32 third, int32 fourth, int32 fifth) => first * second * third * fourth * fifth; public int32 Main() => Mul5(2, 3, 4, 5, 6);", 720, new int[] { 2, 3, 4, 5, 6 })]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Mul6(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth) => first * second * third * fourth * fifth * sixth; public int32 Main() => Mul6(-2, 3, 4, 0, -5, 7);", 0, new int[] { -2, 3, 4, 0, -5, 7 })]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Mul6(int32 first, int32 second, int32 third, int32 fourth, int32 fifth, int32 sixth) => first * second * third * fourth * fifth * sixth; public int32 Main() => Mul6(-2, 3, 4, 0, -5, 7);", 0, new int[] { -2, 3, 4, 0, -5, 7 })]
    public Task ParameterizedProductExecutesFromGeneratedPeAsync(
        BoundRuntimePolicy policy, string sourceText, int value, int[] arguments)
        => TerrumScannerAndSourceEmitterProduceBootstrapPeImageAsync(
            policy, sourceText, value, arguments.Length + 6,
            helperArguments: arguments, helperMultiplication: true);

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task ParameterizedProductExecutesWithWideStackDisplacementsAsync(
        BoundRuntimePolicy policy)
    {
        const int argumentCount = 18;
        var parameterNames = Enumerable.Range(1, argumentCount)
            .Select(static index => $"factor{index}")
            .ToArray();
        var arguments = Enumerable.Repeat(1, argumentCount).ToArray();
        arguments[0] = -2;
        arguments[^1] = 3;
        var helperName = $"Multiply{argumentCount}";
        var sourceText = $"internal int32 {helperName}("
            + string.Join(", ", parameterNames.Select(static name => "int32 " + name))
            + ") => " + string.Join(" * ", parameterNames)
            + $"; public int32 Main() => {helperName}("
            + string.Join(", ", arguments) + ");";
        return TerrumScannerAndSourceEmitterProduceBootstrapPeImageAsync(
            policy, sourceText, -6, argumentCount + 6,
            helperArguments: arguments, helperMultiplication: true);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Mul(int32 first, int32 second) => first * second; public int32 Main() => Mul(50000, 50000);")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Mul(int32 first, int32 second) => first * second; public int32 Main() => Mul(50000, 50000);")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Mul(int32 first, int32 second) => first * second; public int32 Main() => Mul(-2147483648, -1);")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Mul(int32 first, int32 second) => first * second; public int32 Main() => Mul(-2147483648, -1);")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Mul3(int32 first, int32 second, int32 third) => first * second * third; public int32 Main() => Mul3(50000, 50000, 0);")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Mul3(int32 first, int32 second, int32 third) => first * second * third; public int32 Main() => Mul3(50000, 50000, 0);")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Mul(int32 first, int32 second, int32 third) => first * second + third; public int32 Main() => Mul(2, 3, 4);")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Mul(int32 first, int32 second, int32 third) => first * second + third; public int32 Main() => Mul(2, 3, 4);")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Mul(int32 first, int32 second, int32 third) => first * second - third; public int32 Main() => Mul(2, 3, 4);")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Mul(int32 first, int32 second, int32 third) => first * second - third; public int32 Main() => Mul(2, 3, 4);")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Mul(int32 first, int32 second, int32 third) => first * third * second; public int32 Main() => Mul(2, 3, 4);")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Mul(int32 first, int32 second, int32 third) => first * third * second; public int32 Main() => Mul(2, 3, 4);")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Mul(int32 first, int32 second) => second * first; public int32 Main() => Mul(2, 3);")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Mul(int32 first, int32 second) => second * first; public int32 Main() => Mul(2, 3);")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Mul(int32 first, int32 second) => first / second; public int32 Main() => Mul(6, 3);")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Mul(int32 first, int32 second) => first / second; public int32 Main() => Mul(6, 3);")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Mul(int32 first, int32 second) => first * ; public int32 Main() => Mul(6, 3);")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Mul(int32 first, int32 second) => first * ; public int32 Main() => Mul(6, 3);")]
    public Task OverflowingOrUnsupportedParameterizedProductRejectsBeforePublicationAsync(
        BoundRuntimePolicy policy, string sourceText)
        => AssertParameterizedArithmeticRejectsBeforePublicationAsync(policy, sourceText);

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sub(int32 first, int32 second, int32 third) => first - second - third; public int32 Main() => Sub(-2147483648, 1, -2147483648);")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sub(int32 first, int32 second, int32 third) => first - second - third; public int32 Main() => Sub(-2147483648, 1, -2147483648);")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sub(int32 first, int32 second) => first - second; public int32 Main() => Sub(2147483647, -1);")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sub(int32 first, int32 second) => first - second; public int32 Main() => Sub(2147483647, -1);")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sub(int32 first, int32 second) => second - first; public int32 Main() => Sub(40, 2);")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sub(int32 first, int32 second) => second - first; public int32 Main() => Sub(40, 2);")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sub(int32 first, int32 second, int32 third) => first - second + third; public int32 Main() => Sub(40, 2, 3);")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sub(int32 first, int32 second, int32 third) => first - second + third; public int32 Main() => Sub(40, 2, 3);")]
    [InlineData(BoundRuntimePolicy.FailFast, "internal int32 Sub(int32 first, int32 second, int32 third) => first - third - second; public int32 Main() => Sub(40, 2, 3);")]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, "internal int32 Sub(int32 first, int32 second, int32 third) => first - third - second; public int32 Main() => Sub(40, 2, 3);")]
    public async Task OverflowingOrUnsupportedParameterizedSubtractionRejectsBeforePublicationAsync(
        BoundRuntimePolicy policy, string sourceText)
        => await AssertParameterizedArithmeticRejectsBeforePublicationAsync(
            policy, sourceText).ConfigureAwait(true);

    private async Task AssertParameterizedArithmeticRejectsBeforePublicationAsync(
        BoundRuntimePolicy policy, string sourceText,
        string? shortWorkspace = null, int workspaceCapacity = 0)
    {
        var source = SourceText.From(sourceText);
        var sourceBuffers = CreateBootstrapSourceBuffers(source);
        if (shortWorkspace is not null)
        {
            sourceBuffers = sourceBuffers
                .Where(buffer => buffer.Name != shortWorkspace)
                .ToImmutableArray()
                .Add(new HostBuffer(shortWorkspace, PrimitiveType.Int32,
                    [.. new int[workspaceCapacity]], true));
        }
        var buffers = sourceBuffers
            .Add(new HostBuffer("BootstrapCodeBytes", PrimitiveType.Int32,
                [.. new int[240]], true))
            .Add(new HostBuffer("BootstrapCodeLength", PrimitiveType.Int32, [0], true))
            .Add(new HostBuffer("BootstrapImageBytes", PrimitiveType.Int32,
                [.. new int[1536]], true))
            .Add(new HostBuffer("BootstrapImageLength", PrimitiveType.Int32, [0], true));
        var components = s_bootstrapSourceComponents
            .Append("BootstrapInt32MainSource")
            .Append("BootstrapInt32MainLeaf")
            .Append("BootstrapInt32MainPeImage");
        var trees = await ReadComponentsAsync(components, RejectBootstrapSourcePublication)
            .ConfigureAwait(true);
        var compilation = Compilation.Create(trees, buffers, policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);

        var jit = NativeJit.ExecuteWithData(program, (NativeRuntimePolicy)policy);
        Assert.Equal(0, jit.ExitCode);
        Assert.Equal(0, ReadInt32(jit.Data, jit.BufferOffsets[buffers.Length - 3]));
        Assert.Equal(0, ReadInt32(jit.Data, jit.BufferOffsets[buffers.Length - 1]));
        Assert.Equal(0, await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true));
    }

    public static IEnumerable<object[]> StackArgumentSourceCases()
        => Enumerable.Range(19, GetFixedSectionMaximumArgumentCount() - 18)
            .SelectMany(CreateExtendedStackArgumentSourceCases);

    public static IEnumerable<object[]> StackArgumentAlignmentSourceCases()
    {
        foreach (var argumentCount in Enumerable.Range(1, 18))
        {
            var arguments = Enumerable.Range(1, argumentCount).ToArray();
            var source = CreateStackArgumentSource(argumentCount, arguments);
            foreach (var policy in new[] {
                BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions,
            })
            {
                yield return [policy, source, arguments];
            }
        }
    }

    private static int GetMaximumStackArgumentCount()
    {
        const int textSectionRawSize = 0x200;
        for (var argumentCount = 1; ; argumentCount++)
        {
            var codeLength = GetStackArgumentCodeLength(argumentCount);
            var metadataEnd = ((codeLength + 3) & ~3) + 20;
            if (metadataEnd > textSectionRawSize)
            {
                return argumentCount - 1;
            }
        }
    }

    private static int GetFixedSectionMaximumArgumentCount()
        => GetMaximumStackArgumentCount();

    private static int GetStackArgumentImageLength(
        int argumentCount, bool multiplication = false)
    {
        var codeLength = GetStackArgumentCodeLength(argumentCount, multiplication);
        var virtualSize = ((codeLength + 3) & ~3) + 20;
        var rawSize = (virtualSize + 0x1FF) & ~0x1FF;
        return 0x400 + rawSize;
    }

    private static int GetStackArgumentCodeLength(
        int argumentCount, bool multiplication = false)
    {
        var frameSize = GetStackArgumentFrameSize(argumentCount);
        var largeFrame = frameSize > 0x7F;
        var probeFrame = frameSize >= 0x1000;
        var codeLength = probeFrame ? 0x32 : largeFrame ? 7 : 4;
        for (var argumentIndex = 0; argumentIndex < argumentCount; argumentIndex++)
        {
            codeLength += argumentIndex < 2 ? 5
                : argumentIndex < 4 ? 6
                : 0x20 + (argumentIndex - 4) * 8 > 0x7F ? 11 : 8;
        }
        codeLength += largeFrame ? 13 : 10;
        codeLength += 3;
        for (var argumentIndex = 1; argumentIndex < argumentCount; argumentIndex++)
        {
            codeLength += multiplication
                ? argumentIndex == 1 ? 3
                    : argumentIndex < 4 ? 4
                    : 0x28 + (argumentIndex - 4) * 8 > 0x7F ? 8 : 5
                : argumentIndex == 1 ? 2
                    : argumentIndex < 4 ? 3
                    : 0x28 + (argumentIndex - 4) * 8 > 0x7F ? 7 : 4;
        }
        return codeLength;
    }

    private static int GetStackArgumentFrameSize(int argumentCount)
        => ((0x20 + Math.Max(argumentCount - 4, 0) * 8 + 8 + 15) & ~15) - 8;

    private static int GetStackArgumentCountForCodeLength(int codeLength)
    {
        for (var argumentCount = 1; argumentCount <= codeLength / 4; argumentCount++)
        {
            if (GetStackArgumentCodeLength(argumentCount) == codeLength)
            {
                return argumentCount;
            }
        }
        return 0;
    }

    private static int GetStackArgumentCallerEndOffset(int argumentCount)
    {
        var frameSize = GetStackArgumentFrameSize(argumentCount);
        var callerEndOffset = frameSize >= 0x1000 ? 0x32
            : frameSize > 0x7F ? 7 : 4;
        for (var argumentIndex = 0; argumentIndex < argumentCount; argumentIndex++)
        {
            callerEndOffset += argumentIndex < 2 ? 5
                : argumentIndex < 4 ? 6
                : 0x20 + (argumentIndex - 4) * 8 > 0x7F ? 11 : 8;
        }
        return callerEndOffset;
    }

    private static (string SourceText, int[] Arguments) CreateMaximumSourceBoundedStackArgumentCase()
    {
        var minimumArgumentCount = 1;
        var maximumArgumentCount = ScannerHost.MaximumSourceLength;
        while (minimumArgumentCount < maximumArgumentCount)
        {
            var argumentCount = minimumArgumentCount
                + (maximumArgumentCount - minimumArgumentCount + 1) / 2;
            var source = CreateStackArgumentSource(
                argumentCount, new int[argumentCount],
                compactNames: true, compactSyntax: true);
            if (source.Length <= ScannerHost.MaximumSourceLength)
            {
                minimumArgumentCount = argumentCount;
            }
            else
            {
                maximumArgumentCount = argumentCount - 1;
            }
        }

        var arguments = Enumerable.Range(0, minimumArgumentCount)
            .Select(static index => index % 9 + 1)
            .ToArray();
        var sourceText = CreateStackArgumentSource(
            minimumArgumentCount, arguments, compactNames: true, compactSyntax: true);
        Assert.InRange(sourceText.Length, 1, ScannerHost.MaximumSourceLength);
        Assert.True(CreateStackArgumentSource(
                minimumArgumentCount + 1, new int[minimumArgumentCount + 1],
                compactNames: true, compactSyntax: true).Length
            > ScannerHost.MaximumSourceLength);
        return (sourceText, arguments);
    }

    private static string CreateStackArgumentSource(
        int argumentCount, int[] arguments, bool compactNames = false,
        bool compactSyntax = false)
    {
        var names = Enumerable.Range(1, argumentCount)
            .Select(index => compactSyntax ? ((char)(0x4DFF + index)).ToString()
                : compactNames ? $"p{index:X}" : $"argument{index}")
            .ToArray();
        var helperName = $"Sum{argumentCount}";
        var separator = compactSyntax ? "," : ", ";
        var addition = compactSyntax ? "+" : " + ";
        var helperBody = argumentCount == 1 ? names[0] : string.Join(addition, names);
        return $"internal int32 {helperName}("
            + string.Join(separator, names.Select(static name => "int32 " + name))
            + $") => {helperBody}; public int32 Main() => {helperName}("
            + string.Join(separator, arguments) + ");";
    }

    public static IEnumerable<object[]> StackArgumentCapacityBoundaryCases()
    {
        var maximumArgumentCount = GetFixedSectionMaximumArgumentCount();
        foreach (var argumentCount in Enumerable.Range(1, maximumArgumentCount))
        {
            var arguments = Enumerable.Range(1, argumentCount).ToArray();
            var source = CreateStackArgumentSource(argumentCount, arguments);
            var requiredCodeLength = GetStackArgumentCodeLength(argumentCount);
            foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
            {
                yield return [policy, source, requiredCodeLength - 1];
            }
        }
        foreach (var argumentCount in new[] { 33, 231, 232, 511 })
        {
            var arguments = new int[argumentCount];
            var source = CreateStackArgumentSource(
                argumentCount, arguments, compactNames: true);
            var requiredCodeLength = GetStackArgumentCodeLength(argumentCount);
            foreach (var policy in new[] {
                BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions,
            })
            {
                yield return [policy, source, requiredCodeLength - 1];
            }
        }

        foreach (var argumentCount in new[] { 512, 513, 514, 684, 685 })
        {
            var probeSource = CreateStackArgumentSource(
                argumentCount, new int[argumentCount], compactNames: true);
            var requiredCodeLength = GetStackArgumentCodeLength(argumentCount);
            foreach (var policy in new[] {
                BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions,
            })
            {
                yield return [policy, probeSource, requiredCodeLength - 1];
            }
        }
    }

    private static IEnumerable<object[]> CreateExtendedStackArgumentSourceCases(int argumentCount)
    {
        var parameterNames = Enumerable.Range(1, GetFixedSectionMaximumArgumentCount())
            .Select(static index => $"argument{index}")
            .ToArray();
        var names = parameterNames[..argumentCount];
        var helperName = $"Sum{argumentCount}";
        var declaration = $"internal int32 {helperName}("
            + string.Join(", ", names.Select(static name => "int32 " + name))
            + ") => " + string.Join(" + ", names) + "; ";
        var positive = Enumerable.Range(1, argumentCount).ToArray();
        var negative = positive.Select(static value => -value).ToArray();
        var cases = new List<(string Source, int[] Arguments, bool ShouldEmit)>();
        foreach (var arguments in new[] {
            positive, negative,
            [.. Enumerable.Repeat(1, argumentCount - 1), int.MinValue],
            [.. Enumerable.Repeat(-1, argumentCount - 1), int.MaxValue],
            [.. Enumerable.Repeat(1, argumentCount - 1), int.MaxValue - (argumentCount - 1)],
            [.. Enumerable.Repeat(-1, argumentCount - 1), int.MinValue + (argumentCount - 1)],
            new int[argumentCount],
        })
        {
            cases.Add((declaration + $"public int32 Main() => {helperName}("
                + string.Join(", ", arguments) + ");", arguments, true));
        }
        if (argumentCount >= 21)
        {
            foreach (var lastArgument in new[] { 0x12345678, int.MinValue })
            {
                var arguments = new int[argumentCount];
                arguments[^1] = lastArgument;
                cases.Add((declaration + $"public int32 Main() => {helperName}("
                    + string.Join(", ", arguments) + ");", arguments, true));
            }
        }
        cases.Add((declaration
            + $"public int32 Main() {{ int32 firstValue = 1; int32 lastValue = {argumentCount / 2} + {argumentCount - argumentCount / 2}; "
            + $"return {helperName}(firstValue, "
            + string.Join(", ", positive[1..^1]) + ", lastValue); }",
            positive, true));
        foreach (var arguments in new[] {
            [.. Enumerable.Repeat(1, argumentCount - 1), int.MaxValue],
            [.. Enumerable.Repeat(-1, argumentCount - 1), int.MinValue],
            new[] { int.MaxValue, 1, -1 }.Concat(Enumerable.Repeat(0, argumentCount - 4)).Append(-1).ToArray(),
            [.. Enumerable.Repeat(1, argumentCount - 2), int.MaxValue, -1],
        })
        {
            cases.Add((declaration + $"public int32 Main() => {helperName}("
                + string.Join(", ", arguments) + ");", arguments, false));
        }
        var valid = declaration + $"public int32 Main() => {helperName}("
            + string.Join(", ", positive) + ");";
        var argumentTail = $", {argumentCount});";
        var parameterTail = $"int32 {names[^1]}";
        var sumTail = $"+ {names[^1]};";
        foreach (var source in new[] {
            valid.Replace(argumentTail, ");", StringComparison.Ordinal),
            valid.Replace(argumentTail, $", {argumentCount}, {argumentCount + 1});", StringComparison.Ordinal),
            valid.Replace(argumentTail, ", missing);", StringComparison.Ordinal),
            valid.Replace(argumentTail, ", -2147483649);", StringComparison.Ordinal),
            valid.Replace(argumentTail, ", 2147483648);", StringComparison.Ordinal),
            valid.Replace($"{argumentCount - 1}, {argumentCount});", $"{argumentCount - 1} {argumentCount});", StringComparison.Ordinal),
            valid.Replace(parameterTail, $"int64 {names[^1]}", StringComparison.Ordinal),
            valid.Replace(parameterTail, "int32 first", StringComparison.Ordinal),
            valid.Replace(parameterTail, $"int32 {names[^2]}", StringComparison.Ordinal),
            valid.Replace(parameterTail, $"int32 {helperName}", StringComparison.Ordinal),
            valid.Replace(parameterTail, "int32 Main", StringComparison.Ordinal),
            valid.Replace(sumTail, $"+ {names[^2]};", StringComparison.Ordinal),
            valid.Replace(sumTail, $"- {names[^1]};", StringComparison.Ordinal),
        })
        {
            cases.Add((source, positive, false));
        }
        cases.Add((valid.Replace(argumentTail,
            $", {argumentCount - 1} + 1);", StringComparison.Ordinal), positive, true));
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            foreach (var (source, arguments, shouldEmit) in cases)
            {
                yield return [policy, source, arguments, shouldEmit];
            }
        }
    }

    [Theory]
    [MemberData(nameof(StackArgumentSourceCases))]
    public Task StackArgumentSourceAndPeAsync(
        BoundRuntimePolicy policy, string sourceText, int[] arguments, bool shouldEmit)
        => AssertExtendedStackArgumentSourceAndPeAsync(policy, sourceText, arguments, shouldEmit);

    [Theory]
    [MemberData(nameof(StackArgumentAlignmentSourceCases))]
    public Task StackArgumentCallAlignmentAsync(
        BoundRuntimePolicy policy, string sourceText, int[] arguments)
        => AssertExtendedStackArgumentSourceAndPeAsync(policy, sourceText, arguments, true);

    [Theory]
    [MemberData(nameof(StackArgumentCapacityBoundaryCases))]
    public Task StackArgumentCapacityBoundariesAsync(
        BoundRuntimePolicy policy, string sourceText, int codeCapacity)
        => HelperCallsRejectInsufficientCodeBuffersAsync(policy, sourceText, codeCapacity);

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, 33, 2048)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, 33, 2048)]
    [InlineData(BoundRuntimePolicy.FailFast, 231, 5120)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, 231, 5120)]
    [InlineData(BoundRuntimePolicy.FailFast, 232, 5632)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, 232, 5632)]
    [InlineData(BoundRuntimePolicy.FailFast, 511, 10240)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, 511, 10240)]
    [InlineData(BoundRuntimePolicy.FailFast, 512, 10240)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, 512, 10240)]
    [InlineData(BoundRuntimePolicy.FailFast, 513, 10240)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, 513, 10240)]
    [InlineData(BoundRuntimePolicy.FailFast, 514, 10752)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, 514, 10752)]
    [InlineData(BoundRuntimePolicy.FailFast, 684, 13312)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, 684, 13312)]
    [InlineData(BoundRuntimePolicy.FailFast, 685, 13824)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, 685, 13824)]
    public Task StackArgumentCallsScalePeSectionsAndProbeStacksAsync(
        BoundRuntimePolicy policy, int argumentCount, int imageCapacity)
    {
        var arguments = Enumerable.Range(1, argumentCount).ToArray();
        var source = CreateStackArgumentSource(argumentCount, arguments, compactNames: true);
        return AssertExtendedStackArgumentSourceAndPeAsync(
            policy, source, arguments, true, imageCapacity);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task StackArgumentProbeLoopsAcrossPagesAsync(BoundRuntimePolicy policy)
    {
        var arguments = new int[1025];
        arguments[^1] = int.MinValue;
        var source = CreateStackArgumentSource(
            arguments.Length, arguments, compactNames: true, compactSyntax: true);
        return AssertExtendedStackArgumentSourceAndPeAsync(
            policy, source, arguments, true, 19456, forceNativeProbe: true);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task StackArgumentSourceEmitsPageProbePrologAsync(BoundRuntimePolicy policy)
    {
        var arguments = Enumerable.Range(1, 512).ToArray();
        return EmitsOnlyTheDirectSourceSubsetAsync(
            policy,
            CreateStackArgumentSource(arguments.Length, arguments, compactNames: true),
            true,
            arguments.Sum(),
            helperArguments: arguments);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task StackProbeFrameEncodingLimitsAsync(BoundRuntimePolicy policy)
    {
        var source = SourceText.From(VerifyBootstrapStackProbeFrameLimitsSource);
        var components = s_bootstrapSourceComponents
            .Append("BootstrapInt32MainSource")
            .Append("BootstrapInt32MainLeaf")
            .Append("BootstrapInt32MainPeImage");
        var trees = await ReadComponentsAsync(
            components, VerifyBootstrapStackProbeFrameLimitsSource).ConfigureAwait(true);
        var buffers = CreateBootstrapSourceBuffers(source)
            .Add(new HostBuffer("BootstrapCodeBytes", PrimitiveType.Int32,
                [.. new int[0x10000 * 4]], true))
            .Add(new HostBuffer("BootstrapCodeLength", PrimitiveType.Int32, [0], true))
            .Add(new HostBuffer("BootstrapImageBytes", PrimitiveType.Int32,
                [.. new int[1536]], true))
            .Add(new HostBuffer("BootstrapImageLength", PrimitiveType.Int32, [0], true));
        var compilation = Compilation.Create(trees, buffers, policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);

        Assert.Equal(0, NativeJit.ExecuteWithData(program, (NativeRuntimePolicy)policy).ExitCode);
        Assert.Equal(0, await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast, 33, 2047)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, 33, 2047)]
    [InlineData(BoundRuntimePolicy.FailFast, 512, 10239)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, 512, 10239)]
    [InlineData(BoundRuntimePolicy.FailFast, 685, 13823)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, 685, 13823)]
    [InlineData(BoundRuntimePolicy.FailFast, 1025, 19455)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions, 1025, 19455)]
    public Task PeImageRejectsWhenOutputBufferIsInsufficientAsync(
        BoundRuntimePolicy policy, int argumentCount, int imageCapacity)
    {
        var sourceText = CreateStackArgumentSource(
            argumentCount, new int[argumentCount], compactNames: true,
            compactSyntax: argumentCount >= 1024);
        return RejectOverCapacityBootstrapPeImageAsync(
            policy, sourceText, argumentCount, imageCapacity);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public Task PeImageRejectsAtSourceLimitArgumentCountWhenOutputIsInsufficientAsync(
        BoundRuntimePolicy policy)
    {
        var (sourceText, arguments) = CreateMaximumSourceBoundedStackArgumentCase();
        return RejectOverCapacityBootstrapPeImageAsync(
            policy, sourceText, arguments.Length,
            GetStackArgumentImageLength(arguments.Length) - 1);
    }

    private async Task RejectOverCapacityBootstrapPeImageAsync(
        BoundRuntimePolicy policy, string sourceText,
        int argumentCount, int imageCapacity)
    {
        var source = SourceText.From(sourceText);
        var components = s_bootstrapSourceComponents
            .Append("BootstrapInt32MainSource")
            .Append("BootstrapInt32MainLeaf")
            .Append("BootstrapInt32MainPeImage");
        var trees = await ReadComponentsAsync(components, RejectOverCapacityBootstrapPeSource)
            .ConfigureAwait(true);
        var buffers = CreateBootstrapSourceBuffers(source)
            .Add(new HostBuffer("BootstrapCodeBytes", PrimitiveType.Int32,
                [.. new int[GetStackArgumentCodeLength(argumentCount)]], true))
            .Add(new HostBuffer("BootstrapCodeLength", PrimitiveType.Int32, [0], true))
            .Add(new HostBuffer("BootstrapImageBytes", PrimitiveType.Int32,
                [.. new int[imageCapacity]], true))
            .Add(new HostBuffer("BootstrapImageLength", PrimitiveType.Int32, [1], true));
        var compilation = Compilation.Create(trees, buffers, policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);

        Assert.Equal(0, NativeJit.ExecuteWithData(program, (NativeRuntimePolicy)policy).ExitCode);
        Assert.Equal(0, await ExecuteAotAsync(program, (NativeRuntimePolicy)policy)
            .ConfigureAwait(true));
    }

    private async Task AssertExtendedStackArgumentSourceAndPeAsync(
        BoundRuntimePolicy policy, string sourceText, int[] arguments, bool shouldEmit,
        int imageCapacity = 1536, bool forceNativeProbe = false)
    {
        var usesDistinctArguments = arguments.SequenceEqual(Enumerable.Range(1, arguments.Length))
            && !sourceText.Contains("firstValue", StringComparison.Ordinal);
        var usesHighStackArgument = arguments.Length >= 21
            && arguments[..^1].All(static argument => argument == 0)
            && arguments[^1] is 0x12345678 or int.MinValue;
        var value = shouldEmit ? checked((int)arguments.Sum(static argument => (long)argument)) : 0;
        await EmitsOnlyTheDirectSourceSubsetAsync(
            policy, sourceText, shouldEmit, value, helperArguments: arguments)
            .ConfigureAwait(true);
        if (shouldEmit && (usesDistinctArguments || usesHighStackArgument))
        {
            await TerrumScannerAndSourceEmitterProduceBootstrapPeImageAsync(
                policy, sourceText, value, arguments.Length + 6,
                helperArguments: arguments, imageCapacity: imageCapacity,
                forceNativeProbe: forceNativeProbe)
                .ConfigureAwait(true);
        }
    }

    private static async Task AssertBootstrapAllocationUnwindsAsync(
        byte[] image, int expectedAllocation, int expectedValue,
        bool callChain = false, int forwardingCount = 1)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "The generated-image unwind probe requires Windows x64.");

        var root = RepositoryRoot();
        var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        Assert.True(File.Exists(vswhere), "Visual Studio discovery is required to build the unwind probe.");

        var discover = new ProcessStartInfo(vswhere) { WorkingDirectory = root };
        foreach (var argument in new[] {
            "-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
            "-property", "installationPath",
        })
        {
            discover.ArgumentList.Add(argument);
        }
        var installation = (await RunProbeProcessAsync(discover, TimeSpan.FromSeconds(10))
            .ConfigureAwait(true)).Trim();
        var environment = Path.Combine(installation, "VC", "Auxiliary", "Build", "vcvars64.bat");
        Assert.True(File.Exists(environment), "Visual Studio x64 C build tools are required for the unwind probe.");

        var directory = Directory.CreateTempSubdirectory("terrum-bootstrap-unwind-").FullName;
        var imagePath = Path.Combine(directory, "bootstrap.exe");
        var executablePath = Path.Combine(directory, "bootstrap-unwind-probe.exe");
        var objectPath = Path.Combine(directory, "bootstrap-unwind-probe.obj");
        var sourcePath = Path.Combine(root, "tests", "native", "bootstrap-unwind-probe.c");
        try
        {
            await File.WriteAllBytesAsync(imagePath, image).ConfigureAwait(true);
            var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")
                ?? throw new InvalidOperationException("ComSpec is unset.")) {
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /O2 /MT /TC /Fo\"{objectPath}\" /Fe\"{executablePath}\" \"{sourcePath}\" /link /incremental:no\"",
                WorkingDirectory = root,
            };
            _ = await RunProbeProcessAsync(build, TimeSpan.FromMinutes(2)).ConfigureAwait(true);

            var run = new ProcessStartInfo(executablePath) { WorkingDirectory = root };
            run.ArgumentList.Add(imagePath);
            var output = await RunProbeProcessAsync(run, TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            var expectedUnwindStateCount = expectedAllocation >= 0x1000 ? 13 : 4;
            var functionCount = forwardingCount + 1;
            Assert.Contains(
                $"unwind-states={expectedUnwindStateCount} allocation={expectedAllocation}",
                output, StringComparison.Ordinal);
            Assert.Contains(
                "probe-execution-result="
                    + unchecked((uint)expectedValue).ToString(CultureInfo.InvariantCulture),
                output, StringComparison.Ordinal);
            if (callChain)
            {
                Assert.Contains(
                    $"nested-unwind-states={forwardingCount * 4} allocation={expectedAllocation}",
                    output, StringComparison.Ordinal);
                Assert.Contains(
                    $"leaf-chain-unwind-states={functionCount + 1} allocation={expectedAllocation}",
                    output, StringComparison.Ordinal);
            }
            if ((long)expectedAllocation * (callChain ? functionCount : 1) >= 0x2000)
            {
                var growthLine = output.Split(
                    ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Single(static line => line.StartsWith(
                        "probe-stack-growth-bytes=", StringComparison.Ordinal));
                Assert.True(ulong.TryParse(growthLine["probe-stack-growth-bytes=".Length..],
                    NumberStyles.None, CultureInfo.InvariantCulture, out var growthBytes)
                    && growthBytes >= 0x1000,
                    $"Expected at least one page of measured stack growth, got '{growthLine}'.");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<string> RunProbeProcessAsync(ProcessStartInfo start, TimeSpan timeout)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start the bootstrap unwind probe process.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(timeout).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(true);
            }

            throw;
        }
        var standardOutput = await output.ConfigureAwait(true);
        var standardError = await error.ConfigureAwait(true);
        Assert.True(process.ExitCode == 0,
            $"Process exited with {process.ExitCode}.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        return standardOutput;
    }

    private static async Task<byte[]> PublishAndAuditFrontendAsync(
        SourceText source, BoundRuntimePolicy policy)
    {
        var trees = await ReadComponentsAsync(s_frontendComponents, PublishFrontendSource)
            .ConfigureAwait(true);
        var compilation = Compilation.Create(trees, ScalarFrontendHost.CreateBuffers(source), policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var execution = NativeJit.ExecuteWithData(
            Assert.IsType<BoundProgram>(compilation.Program), (NativeRuntimePolicy)policy);
        Assert.Equal(0, execution.ExitCode);

        var summary = execution.Data.AsSpan(
            execution.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        var publication = execution.Data.AsSpan(
            execution.BufferOffsets[ScalarFrontendHost.ProgramBufferIndex],
            ScalarFrontendHost.ProgramWordCount * sizeof(int)).ToArray();
        var audit = ScalarFrontendHost.ReadProgram(source, summary, publication, policy);
        Assert.True(audit.Projection.IsValid);
        Assert.NotNull(audit.Program);
        return publication;
    }

    private static ImmutableArray<HostBuffer> CreateBootstrapSourceBuffers(SourceText source)
    {
        var buffers = ScalarFrontendHost.CreateBuffers(source)
            .Where(static buffer => buffer.Name != "Frontend"
                && buffer.Name != "FrontendProgram")
            .ToImmutableArray();
        return buffers
            .Add(new HostBuffer("BootstrapCallArgumentValues", PrimitiveType.Int32,
                [.. new int[source.Length]], true))
            .Add(new HostBuffer("BootstrapCallArgumentOperators", PrimitiveType.Int32,
                [.. new int[source.Length]], true))
            .Add(new HostBuffer("BootstrapMainLocalNames", PrimitiveType.Int32,
                [.. new int[source.Length]], true))
            .Add(new HostBuffer("BootstrapMainLocalValues", PrimitiveType.Int32,
                [.. new int[source.Length]], true));
    }

    private static void AssertPublishedFrontendAudits(
        SourceText source, NativeExecutionResult execution, BoundRuntimePolicy policy)
    {
        var summary = execution.Data.AsSpan(
            execution.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int));
        var publication = execution.Data.AsSpan(
            execution.BufferOffsets[ScalarFrontendHost.ProgramBufferIndex],
            ScalarFrontendHost.ProgramWordCount * sizeof(int));
        var audit = ScalarFrontendHost.ReadProgram(source, summary, publication, policy);
        Assert.True(audit.Projection.IsValid);
        Assert.NotNull(audit.Program);
    }

    private static void AssertOutputCode(
        NativeExecutionResult execution, int codeBytesBuffer, int codeLengthBuffer,
        int[] expected)
    {
        Assert.Equal(expected[1], ReadInt32(execution.Data,
            execution.BufferOffsets[codeLengthBuffer]));
        for (var index = 0; index < expected[1]; index++)
        {
            Assert.Equal(expected[index + 2], ReadInt32(execution.Data,
                execution.BufferOffsets[codeBytesBuffer] + index * sizeof(int)));
        }
    }

    private static byte[] ReadBootstrapPeImage(
        NativeExecutionResult execution, int imageBytesBuffer, int imageLengthBuffer,
        int[] expected, int helperArgumentCount = 0)
    {
        var imageLength = ReadInt32(execution.Data,
            execution.BufferOffsets[imageLengthBuffer]);
        var hasUnwindInfo = expected[2] is 0x48 or 0x49;
        var metadataOffset = (expected[1] + 3) & ~3;
        var sectionVirtualSize = hasUnwindInfo ? metadataOffset + 20 : expected[1];
        var sectionRawSize = (sectionVirtualSize + 0x1FF) & ~0x1FF;
        var imageVirtualSize = (sectionVirtualSize + 0xFFF) & ~0xFFF;
        if (helperArgumentCount == 0)
        {
            helperArgumentCount = GetStackArgumentCountForCodeLength(expected[1]);
        }
        var stackFrameSize = helperArgumentCount > 0
            ? GetStackArgumentFrameSize(helperArgumentCount)
            : 0x28;
        var prologSize = stackFrameSize >= 0x1000 ? 0x32
            : stackFrameSize > 0x7F ? 7 : 4;
        var expectedFunctionEndRva = helperArgumentCount > 0
            ? 0x1000 + GetStackArgumentCallerEndOffset(helperArgumentCount)
                + 5 + (stackFrameSize > 0x7F ? 8 : 5)
            : 0x100E;
        var usesLargeAllocation = stackFrameSize > 0x7F;
        var image = new byte[imageLength];
        var imageBytesOffset = execution.BufferOffsets[imageBytesBuffer];
        for (var index = 0; index < image.Length; index++)
        {
            var value = ReadInt32(execution.Data,
                imageBytesOffset + index * sizeof(int));
            Assert.InRange(value, 0, byte.MaxValue);
            image[index] = (byte)value;
        }

        Assert.Equal(imageLength, ReadInt32(execution.Data,
            execution.BufferOffsets[imageLengthBuffer]));
        Assert.Equal(0x5A4D,
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(image));
        Assert.Equal(0x80, ReadInt32(image, 0x3C));
        Assert.Equal(0x00004550, ReadInt32(image, 0x80));
        Assert.Equal(0x8664,
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(0x84)));
        Assert.Equal(1,
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(0x86)));
        Assert.Equal(0x0022,
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(0x96)));
        Assert.Equal(0x1000, ReadInt32(image, 0xA8));
        Assert.Equal(0x1000 + imageVirtualSize, ReadInt32(image, 0xD0));
        Assert.Equal(0x400, ReadInt32(image, 0xD4));
        Assert.Equal(sectionRawSize, ReadInt32(image, 0x9C));
        Assert.Equal(16, ReadInt32(image, 0x104));
        Assert.Equal(0, ReadInt32(image, 0x108));
        Assert.Equal(0, ReadInt32(image, 0x10C));
        if (hasUnwindInfo)
        {
            var functionTableRva = 0x1000 + metadataOffset;
            var functionTableOffset = 0x400 + metadataOffset;
            var unwindInfoOffset = functionTableOffset + 12;
            Assert.Equal(functionTableRva, ReadInt32(image, 0x120));
            Assert.Equal(12, ReadInt32(image, 0x124));
            Assert.Equal(0x1000, ReadInt32(image, functionTableOffset));
            Assert.Equal(expectedFunctionEndRva,
                ReadInt32(image, functionTableOffset + 4));
            Assert.Equal(functionTableRva + 12, ReadInt32(image, functionTableOffset + 8));
            Assert.Equal(1, image[unwindInfoOffset]);
            Assert.Equal(prologSize, image[unwindInfoOffset + 1]);
            Assert.Equal(usesLargeAllocation ? 2 : 1, image[unwindInfoOffset + 2]);
            Assert.Equal(0, image[unwindInfoOffset + 3]);
            Assert.Equal(prologSize, image[unwindInfoOffset + 4]);
            Assert.Equal(usesLargeAllocation
                    ? 0x01
                    : ((stackFrameSize / 8 - 1) << 4) | 0x02,
                image[unwindInfoOffset + 5]);
            Assert.Equal(usesLargeAllocation ? stackFrameSize / 8 & 0xFF : 0,
                image[unwindInfoOffset + 6]);
            Assert.Equal(usesLargeAllocation ? stackFrameSize / 8 >> 8 & 0xFF : 0,
                image[unwindInfoOffset + 7]);
            Assert.All(image.AsSpan(0x400 + expected[1],
                metadataOffset - expected[1]).ToArray(),
                static value => Assert.Equal(0, value));
        }
        else
        {
            Assert.Equal(0, ReadInt32(image, 0x120));
            Assert.Equal(0, ReadInt32(image, 0x124));
        }
        Assert.Equal(".text", System.Text.Encoding.ASCII.GetString(image, 0x188, 5));
        Assert.Equal(sectionVirtualSize, ReadInt32(image, 0x190));
        Assert.Equal(0x1000, ReadInt32(image, 0x194));
        Assert.Equal(sectionRawSize, ReadInt32(image, 0x198));
        Assert.Equal(0x400, ReadInt32(image, 0x19C));
        Assert.Equal(0x60000020, ReadInt32(image, 0x1AC));
        Assert.Equal(expected.Skip(2).Take(expected[1]).Select(static value => (byte)value),
            image.AsSpan(0x400, expected[1]).ToArray());
        var usedTextSize = hasUnwindInfo ? sectionVirtualSize : expected[1];
        Assert.All(image.AsSpan(0x400 + usedTextSize).ToArray(),
            static value => Assert.Equal(0, value));
        return image;
    }

    private static async Task<int> ExecuteImageAsync(byte[] image)
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"bootstrap-int32-main-image-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Failed to start bootstrap PE image.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15))
                .ConfigureAwait(true);
            return process.ExitCode;
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }

    private static byte[] CreateBootstrapHelperEntryAlignmentProbeImage(
        byte[] image, int argumentCount)
    {
        var frameSize = GetStackArgumentFrameSize(argumentCount);
        var callOffset = GetStackArgumentCallerEndOffset(argumentCount);
        var helperOffset = callOffset + 5 + (frameSize > 0x7F ? 8 : 5);
        var probeImage = (byte[])image.Clone();
        Assert.Equal(0xE8, probeImage[0x400 + callOffset]);

        // Replace only the leaf helper to report its actual entry RSP alignment.
        new byte[] { 0x89, 0xE0, 0x83, 0xE0, 0x0F, 0xC3 }
            .CopyTo(probeImage, 0x400 + helperOffset);
        return probeImage;
    }

    private static int[] ReadWords(byte[] bytes)
    {
        var words = new int[bytes.Length / sizeof(int)];
        for (var index = 0; index < words.Length; index++)
        {
            words[index] = ReadInt32(bytes, index * sizeof(int));
        }
        return words;
    }

    private static void WriteExpectedInt32Immediate(
        int[] expected, int codeOffset, int value)
    {
        for (var byteIndex = 0; byteIndex < sizeof(int); byteIndex++)
        {
            expected[codeOffset + byteIndex + 2] =
                (value >> (byteIndex * 8)) & 0xFF;
        }
    }

    private static int[] CreateExpectedBytes(
        bool shouldEmit, int value, bool addition, bool subtraction = false,
        bool multiplication = false, bool division = false, bool remainder = false,
        int firstOperand = 0, int secondOperand = 0, int thirdOperand = 0,
        int fourthOperand = 0, int fifthOperand = 0, int sixthOperand = 0,
        int seventhOperand = 0, int eighthOperand = 0, int ninthOperand = 0,
        int tenthOperand = 0, int eleventhOperand = 0, int twelfthOperand = 0,
        int thirteenthOperand = 0, int fourteenthOperand = 0, int fifteenthOperand = 0,
        int sixteenthOperand = 0, int seventeenthOperand = 0, int eighteenthOperand = 0,
        int nineteenthOperand = 0, int twentiethOperand = 0, int twentyFirstOperand = 0,
        int twentySecondOperand = 0, int twentyThirdOperand = 0, int twentyFourthOperand = 0,
        int twentyFifthOperand = 0, int twentySixthOperand = 0,
        bool helperCall = false, bool helperArgument = false,
        bool helperTwoArguments = false, bool helperThreeArguments = false,
        bool helperFourArguments = false, bool helperFiveArguments = false,
        bool helperSixArguments = false, bool helperSevenArguments = false,
        bool helperEightArguments = false, bool helperNineArguments = false,
        bool helperTenArguments = false, bool helperElevenArguments = false,
        bool helperTwelveArguments = false, bool helperThirteenArguments = false,
        bool helperFourteenArguments = false,
        bool helperFifteenArguments = false,
        bool helperSixteenArguments = false, bool helperSeventeenArguments = false,
        bool helperEighteenArguments = false, bool helperNineteenArguments = false,
        bool helperTwentyArguments = false, bool helperTwentyOneArguments = false,
        bool helperTwentyTwoArguments = false, bool helperTwentyThreeArguments = false,
        bool helperTwentyFourArguments = false,
        bool helperTwentyFiveArguments = false,
        bool helperTwentySixArguments = false,
        int codeCapacity = 16, int helperArgumentCount = 0, int[]? helperArguments = null,
        bool helperSubtraction = false, bool helperMultiplication = false)
    {
        var expected = new int[codeCapacity + 2];
        if (!shouldEmit)
        {
            return expected;
        }

        expected[0] = 1;
        if (helperCall)
        {
            if (helperNineArguments || helperTenArguments || helperElevenArguments
                || helperTwelveArguments || helperThirteenArguments
                || helperFourteenArguments || helperFifteenArguments
                || helperSixteenArguments || helperSeventeenArguments
                || helperEighteenArguments || helperNineteenArguments || helperTwentyArguments
                || helperTwentyOneArguments || helperTwentyTwoArguments || helperTwentyThreeArguments
                || helperTwentyFourArguments || helperTwentyFiveArguments || helperTwentySixArguments
                || helperArgumentCount >= 1)
            {
                var argumentCount = helperArgumentCount > 0 ? helperArgumentCount
                    : helperTwentySixArguments ? 26
                    : helperTwentyFiveArguments ? 25
                    : helperTwentyFourArguments ? 24
                    : helperTwentyThreeArguments ? 23
                    : helperTwentyTwoArguments ? 22
                    : helperTwentyOneArguments ? 21
                    : helperTwentyArguments ? 20
                    : helperNineteenArguments ? 19
                    : helperEighteenArguments ? 18
                    : helperSeventeenArguments ? 17
                    : helperSixteenArguments ? 16 : helperFifteenArguments ? 15
                    : helperFourteenArguments ? 14
                    : helperThirteenArguments ? 13 : helperTwelveArguments ? 12
                    : helperElevenArguments ? 11 : helperTenArguments ? 10 : 9;
                expected[1] = GetStackArgumentCodeLength(
                    argumentCount, helperMultiplication);
                var stackFrameSize = GetStackArgumentFrameSize(argumentCount);
                var longFrame = stackFrameSize > 0x7F;
                var probeFrame = stackFrameSize >= 0x1000;
                if (probeFrame)
                {
                    expected[2] = 0x49;
                    expected[3] = 0x89;
                    expected[4] = 0xE3;
                    expected[5] = 0x41;
                    expected[6] = 0xBA;
                    WriteExpectedInt32Immediate(expected, 5, stackFrameSize);
                    expected[11] = 0x49;
                    expected[12] = 0x81;
                    expected[13] = 0xEB;
                    WriteExpectedInt32Immediate(expected, 12, 0x1000);
                    expected[18] = 0x41;
                    expected[19] = 0xF6;
                    expected[20] = 0x03;
                    expected[21] = 0;
                    expected[22] = 0x41;
                    expected[23] = 0x81;
                    expected[24] = 0xEA;
                    WriteExpectedInt32Immediate(expected, 23, 0x1000);
                    expected[29] = 0x41;
                    expected[30] = 0x81;
                    expected[31] = 0xFA;
                    WriteExpectedInt32Immediate(expected, 30, 0x1000);
                    expected[36] = 0x73;
                    expected[37] = 0xE5;
                    expected[38] = 0x4D;
                    expected[39] = 0x29;
                    expected[40] = 0xD3;
                    expected[41] = 0x41;
                    expected[42] = 0xF6;
                    expected[43] = 0x03;
                    expected[44] = 0;
                    expected[45] = 0x48;
                    expected[46] = 0x81;
                    expected[47] = 0xEC;
                    WriteExpectedInt32Immediate(expected, 46, stackFrameSize);
                }
                else if (longFrame)
                {
                    expected[2] = 0x48;
                    expected[3] = 0x81;
                    expected[4] = 0xEC;
                    for (var byteIndex = 0; byteIndex < sizeof(int); byteIndex++)
                    {
                        expected[byteIndex + 5] = (stackFrameSize >> (byteIndex * 8)) & 0xFF;
                    }
                    expected[9] = 0xB9;
                }
                else
                {
                    expected[2] = 0x48;
                    expected[3] = 0x83;
                    expected[4] = 0xEC;
                    expected[5] = stackFrameSize;
                }
                var arguments = helperArguments ?? new[]
                {
                    argumentCount == 1 ? value : firstOperand,
                    secondOperand, thirdOperand, fourthOperand,
                    fifthOperand, sixthOperand, seventhOperand, eighthOperand,
                    ninthOperand, tenthOperand, eleventhOperand, twelfthOperand,
                    thirteenthOperand, fourteenthOperand, fifteenthOperand,
                    sixteenthOperand, seventeenthOperand, eighteenthOperand, nineteenthOperand, twentiethOperand,
                    twentyFirstOperand, twentySecondOperand, twentyThirdOperand, twentyFourthOperand,
                    twentyFifthOperand, twentySixthOperand,
                };
                var setupOffset = probeFrame ? 0x32 : longFrame ? 7 : 4;
                for (var argumentIndex = 0; argumentIndex < argumentCount; argumentIndex++)
                {
                    var immediateOffset = setupOffset + 1;
                    var instructionLength = 5;
                    if (argumentIndex == 0)
                    {
                        expected[setupOffset + 2] = 0xB9;
                    }
                    else if (argumentIndex == 1)
                    {
                        expected[setupOffset + 2] = 0xBA;
                    }
                    else if (argumentIndex == 2)
                    {
                        expected[setupOffset + 2] = 0x41;
                        expected[setupOffset + 3] = 0xB8;
                        immediateOffset = setupOffset + 2;
                        instructionLength = 6;
                    }
                    else if (argumentIndex == 3)
                    {
                        expected[setupOffset + 2] = 0x41;
                        expected[setupOffset + 3] = 0xB9;
                        immediateOffset = setupOffset + 2;
                        instructionLength = 6;
                    }
                    else
                    {
                        expected[setupOffset + 2] = 0xC7;
                        var stackOffset = 0x20 + (argumentIndex - 4) * 8;
                        if (stackOffset > 0x7F)
                        {
                            expected[setupOffset + 3] = 0x84;
                            expected[setupOffset + 4] = 0x24;
                            for (var byteIndex = 0; byteIndex < sizeof(int); byteIndex++)
                            {
                                expected[setupOffset + byteIndex + 5] =
                                    (stackOffset >> (byteIndex * 8)) & 0xFF;
                            }
                            immediateOffset = setupOffset + 7;
                            instructionLength = 11;
                        }
                        else
                        {
                            expected[setupOffset + 3] = 0x44;
                            expected[setupOffset + 4] = 0x24;
                            expected[setupOffset + 5] = 0x20 + (argumentIndex - 4) * 8;
                            immediateOffset = setupOffset + 4;
                            instructionLength = 8;
                        }
                    }
                    for (var byteIndex = 0; byteIndex < sizeof(int); byteIndex++)
                    {
                        expected[immediateOffset + byteIndex + 2] =
                            (arguments[argumentIndex] >> (byteIndex * 8)) & 0xFF;
                    }
                    setupOffset += instructionLength;
                }

                expected[setupOffset + 2] = 0xE8;
                expected[setupOffset + 3] = longFrame ? 8 : 5;
                if (longFrame)
                {
                    expected[setupOffset + 7] = 0x48;
                    expected[setupOffset + 8] = 0x81;
                    expected[setupOffset + 9] = 0xC4;
                    for (var byteIndex = 0; byteIndex < sizeof(int); byteIndex++)
                    {
                        expected[setupOffset + byteIndex + 10] =
                            (stackFrameSize >> (byteIndex * 8)) & 0xFF;
                    }
                    expected[setupOffset + 14] = 0xC3;
                }
                else
                {
                    expected[setupOffset + 7] = 0x48;
                    expected[setupOffset + 8] = 0x83;
                    expected[setupOffset + 9] = 0xC4;
                    expected[setupOffset + 10] = stackFrameSize;
                    expected[setupOffset + 11] = 0xC3;
                }
                var helperOffset = setupOffset + (longFrame ? 15 : 12);
                expected[helperOffset] = 0x89;
                expected[helperOffset + 1] = 0xC8;
                var arithmeticOpcode = helperSubtraction ? 0x29 : 0x01;
                var stackReadOffset = helperOffset + 2;
                for (var argumentIndex = 1; argumentIndex < argumentCount; argumentIndex++)
                {
                    if (helperMultiplication && argumentIndex == 1)
                    {
                        expected[stackReadOffset] = 0x0F;
                        expected[stackReadOffset + 1] = 0xAF;
                        expected[stackReadOffset + 2] = 0xC2;
                        stackReadOffset += 3;
                    }
                    else if (helperMultiplication && argumentIndex == 2)
                    {
                        expected[stackReadOffset] = 0x41;
                        expected[stackReadOffset + 1] = 0x0F;
                        expected[stackReadOffset + 2] = 0xAF;
                        expected[stackReadOffset + 3] = 0xC0;
                        stackReadOffset += 4;
                    }
                    else if (helperMultiplication && argumentIndex == 3)
                    {
                        expected[stackReadOffset] = 0x41;
                        expected[stackReadOffset + 1] = 0x0F;
                        expected[stackReadOffset + 2] = 0xAF;
                        expected[stackReadOffset + 3] = 0xC1;
                        stackReadOffset += 4;
                    }
                    else if (helperMultiplication)
                    {
                        var stackOffset = 0x28 + (argumentIndex - 4) * 8;
                        expected[stackReadOffset] = 0x0F;
                        expected[stackReadOffset + 1] = 0xAF;
                        if (stackOffset > 0x7F)
                        {
                            expected[stackReadOffset + 2] = 0x84;
                            expected[stackReadOffset + 3] = 0x24;
                            for (var byteIndex = 0; byteIndex < sizeof(int); byteIndex++)
                            {
                                expected[stackReadOffset + byteIndex + 4] =
                                    (stackOffset >> (byteIndex * 8)) & 0xFF;
                            }
                            stackReadOffset += 8;
                        }
                        else
                        {
                            expected[stackReadOffset + 2] = 0x44;
                            expected[stackReadOffset + 3] = 0x24;
                            expected[stackReadOffset + 4] = stackOffset;
                            stackReadOffset += 5;
                        }
                    }
                    else if (argumentIndex == 1)
                    {
                        expected[stackReadOffset] = arithmeticOpcode;
                        expected[stackReadOffset + 1] = 0xD0;
                        stackReadOffset += 2;
                    }
                    else if (argumentIndex == 2)
                    {
                        expected[stackReadOffset] = 0x44;
                        expected[stackReadOffset + 1] = arithmeticOpcode;
                        expected[stackReadOffset + 2] = 0xC0;
                        stackReadOffset += 3;
                    }
                    else if (argumentIndex == 3)
                    {
                        expected[stackReadOffset] = 0x44;
                        expected[stackReadOffset + 1] = arithmeticOpcode;
                        expected[stackReadOffset + 2] = 0xC8;
                        stackReadOffset += 3;
                    }
                    else
                    {
                        expected[stackReadOffset] = helperSubtraction ? 0x2B : 0x03;
                        var stackOffset = 0x28 + (argumentIndex - 4) * 8;
                        if (stackOffset > 0x7F)
                        {
                            expected[stackReadOffset + 1] = 0x84;
                            expected[stackReadOffset + 2] = 0x24;
                            for (var byteIndex = 0; byteIndex < sizeof(int); byteIndex++)
                            {
                                expected[stackReadOffset + byteIndex + 3] =
                                    (stackOffset >> (byteIndex * 8)) & 0xFF;
                            }
                            stackReadOffset += 7;
                        }
                        else
                        {
                            expected[stackReadOffset + 1] = 0x44;
                            expected[stackReadOffset + 2] = 0x24;
                            expected[stackReadOffset + 3] = stackOffset;
                            stackReadOffset += 4;
                        }
                    }
                }
                expected[stackReadOffset] = 0xC3;
                return expected;
            }

            if (helperEightArguments)
            {
                expected[1] = 95;
                expected[2] = 0x48;
                expected[3] = 0x83;
                expected[4] = 0xEC;
                expected[5] = 0x48;
                var arguments = new[]
                {
                    firstOperand, secondOperand, thirdOperand, fourthOperand,
                    fifthOperand, sixthOperand, seventhOperand, eighthOperand,
                };
                var setupOffset = 4;
                for (var argumentIndex = 0; argumentIndex < arguments.Length; argumentIndex++)
                {
                    var immediateOffset = setupOffset + 1;
                    var instructionLength = 5;
                    if (argumentIndex == 0)
                    {
                        expected[setupOffset + 2] = 0xB9;
                    }
                    else if (argumentIndex == 1)
                    {
                        expected[setupOffset + 2] = 0xBA;
                    }
                    else if (argumentIndex == 2)
                    {
                        expected[setupOffset + 2] = 0x41;
                        expected[setupOffset + 3] = 0xB8;
                        immediateOffset = setupOffset + 2;
                        instructionLength = 6;
                    }
                    else if (argumentIndex == 3)
                    {
                        expected[setupOffset + 2] = 0x41;
                        expected[setupOffset + 3] = 0xB9;
                        immediateOffset = setupOffset + 2;
                        instructionLength = 6;
                    }
                    else
                    {
                        expected[setupOffset + 2] = 0xC7;
                        expected[setupOffset + 3] = 0x44;
                        expected[setupOffset + 4] = 0x24;
                        expected[setupOffset + 5] = 0x20 + (argumentIndex - 4) * 8;
                        immediateOffset = setupOffset + 4;
                        instructionLength = 8;
                    }
                    for (var byteIndex = 0; byteIndex < sizeof(int); byteIndex++)
                    {
                        expected[immediateOffset + byteIndex + 2] =
                            (arguments[argumentIndex] >> (byteIndex * 8)) & 0xFF;
                    }
                    setupOffset += instructionLength;
                }

                expected[setupOffset + 2] = 0xE8;
                expected[setupOffset + 3] = 5;
                expected[setupOffset + 7] = 0x48;
                expected[setupOffset + 8] = 0x83;
                expected[setupOffset + 9] = 0xC4;
                expected[setupOffset + 10] = 0x48;
                expected[setupOffset + 11] = 0xC3;
                var helperOffset = setupOffset + 12;
                expected[helperOffset] = 0x89;
                expected[helperOffset + 1] = 0xC8;
                expected[helperOffset + 2] = 0x01;
                expected[helperOffset + 3] = 0xD0;
                expected[helperOffset + 4] = 0x44;
                expected[helperOffset + 5] = 0x01;
                expected[helperOffset + 6] = 0xC0;
                expected[helperOffset + 7] = 0x44;
                expected[helperOffset + 8] = 0x01;
                expected[helperOffset + 9] = 0xC8;
                var stackReadOffset = helperOffset + 10;
                for (var argumentIndex = 4; argumentIndex < arguments.Length; argumentIndex++)
                {
                    expected[stackReadOffset] = 0x03;
                    expected[stackReadOffset + 1] = 0x44;
                    expected[stackReadOffset + 2] = 0x24;
                    expected[stackReadOffset + 3] = 0x28 + (argumentIndex - 4) * 8;
                    stackReadOffset += 4;
                }
                expected[stackReadOffset] = 0xC3;
                return expected;
            }

            if (helperSevenArguments)
            {
                expected[1] = 83;
                expected[2] = 0x48;
                expected[3] = 0x83;
                expected[4] = 0xEC;
                expected[5] = 0x38;
                expected[6] = 0xB9;
                expected[7] = firstOperand & 0xFF;
                expected[8] = (firstOperand >> 8) & 0xFF;
                expected[9] = (firstOperand >> 16) & 0xFF;
                expected[10] = (firstOperand >> 24) & 0xFF;
                expected[11] = 0xBA;
                expected[12] = secondOperand & 0xFF;
                expected[13] = (secondOperand >> 8) & 0xFF;
                expected[14] = (secondOperand >> 16) & 0xFF;
                expected[15] = (secondOperand >> 24) & 0xFF;
                expected[16] = 0x41;
                expected[17] = 0xB8;
                expected[18] = thirdOperand & 0xFF;
                expected[19] = (thirdOperand >> 8) & 0xFF;
                expected[20] = (thirdOperand >> 16) & 0xFF;
                expected[21] = (thirdOperand >> 24) & 0xFF;
                expected[22] = 0x41;
                expected[23] = 0xB9;
                expected[24] = fourthOperand & 0xFF;
                expected[25] = (fourthOperand >> 8) & 0xFF;
                expected[26] = (fourthOperand >> 16) & 0xFF;
                expected[27] = (fourthOperand >> 24) & 0xFF;
                expected[28] = 0xC7;
                expected[29] = 0x44;
                expected[30] = 0x24;
                expected[31] = 0x20;
                expected[32] = fifthOperand & 0xFF;
                expected[33] = (fifthOperand >> 8) & 0xFF;
                expected[34] = (fifthOperand >> 16) & 0xFF;
                expected[35] = (fifthOperand >> 24) & 0xFF;
                expected[36] = 0xC7;
                expected[37] = 0x44;
                expected[38] = 0x24;
                expected[39] = 0x28;
                expected[40] = sixthOperand & 0xFF;
                expected[41] = (sixthOperand >> 8) & 0xFF;
                expected[42] = (sixthOperand >> 16) & 0xFF;
                expected[43] = (sixthOperand >> 24) & 0xFF;
                expected[44] = 0xC7;
                expected[45] = 0x44;
                expected[46] = 0x24;
                expected[47] = 0x30;
                expected[48] = seventhOperand & 0xFF;
                expected[49] = (seventhOperand >> 8) & 0xFF;
                expected[50] = (seventhOperand >> 16) & 0xFF;
                expected[51] = (seventhOperand >> 24) & 0xFF;
                expected[52] = 0xE8;
                expected[53] = 5;
                expected[57] = 0x48;
                expected[58] = 0x83;
                expected[59] = 0xC4;
                expected[60] = 0x38;
                expected[61] = 0xC3;
                expected[62] = 0x89;
                expected[63] = 0xC8;
                expected[64] = 0x01;
                expected[65] = 0xD0;
                expected[66] = 0x44;
                expected[67] = 0x01;
                expected[68] = 0xC0;
                expected[69] = 0x44;
                expected[70] = 0x01;
                expected[71] = 0xC8;
                expected[72] = 0x03;
                expected[73] = 0x44;
                expected[74] = 0x24;
                expected[75] = 0x28;
                expected[76] = 0x03;
                expected[77] = 0x44;
                expected[78] = 0x24;
                expected[79] = 0x30;
                expected[80] = 0x03;
                expected[81] = 0x44;
                expected[82] = 0x24;
                expected[83] = 0x38;
                expected[84] = 0xC3;
                return expected;
            }

            if (helperSixArguments)
            {
                expected[1] = 71;
                expected[2] = 0x48;
                expected[3] = 0x83;
                expected[4] = 0xEC;
                expected[5] = 0x38;
                expected[6] = 0xB9;
                expected[7] = firstOperand & 0xFF;
                expected[8] = (firstOperand >> 8) & 0xFF;
                expected[9] = (firstOperand >> 16) & 0xFF;
                expected[10] = (firstOperand >> 24) & 0xFF;
                expected[11] = 0xBA;
                expected[12] = secondOperand & 0xFF;
                expected[13] = (secondOperand >> 8) & 0xFF;
                expected[14] = (secondOperand >> 16) & 0xFF;
                expected[15] = (secondOperand >> 24) & 0xFF;
                expected[16] = 0x41;
                expected[17] = 0xB8;
                expected[18] = thirdOperand & 0xFF;
                expected[19] = (thirdOperand >> 8) & 0xFF;
                expected[20] = (thirdOperand >> 16) & 0xFF;
                expected[21] = (thirdOperand >> 24) & 0xFF;
                expected[22] = 0x41;
                expected[23] = 0xB9;
                expected[24] = fourthOperand & 0xFF;
                expected[25] = (fourthOperand >> 8) & 0xFF;
                expected[26] = (fourthOperand >> 16) & 0xFF;
                expected[27] = (fourthOperand >> 24) & 0xFF;
                expected[28] = 0xC7;
                expected[29] = 0x44;
                expected[30] = 0x24;
                expected[31] = 0x20;
                expected[32] = fifthOperand & 0xFF;
                expected[33] = (fifthOperand >> 8) & 0xFF;
                expected[34] = (fifthOperand >> 16) & 0xFF;
                expected[35] = (fifthOperand >> 24) & 0xFF;
                expected[36] = 0xC7;
                expected[37] = 0x44;
                expected[38] = 0x24;
                expected[39] = 0x28;
                expected[40] = sixthOperand & 0xFF;
                expected[41] = (sixthOperand >> 8) & 0xFF;
                expected[42] = (sixthOperand >> 16) & 0xFF;
                expected[43] = (sixthOperand >> 24) & 0xFF;
                expected[44] = 0xE8;
                expected[45] = 5;
                expected[49] = 0x48;
                expected[50] = 0x83;
                expected[51] = 0xC4;
                expected[52] = 0x38;
                expected[53] = 0xC3;
                expected[54] = 0x89;
                expected[55] = 0xC8;
                expected[56] = 0x01;
                expected[57] = 0xD0;
                expected[58] = 0x44;
                expected[59] = 0x01;
                expected[60] = 0xC0;
                expected[61] = 0x44;
                expected[62] = 0x01;
                expected[63] = 0xC8;
                expected[64] = 0x03;
                expected[65] = 0x44;
                expected[66] = 0x24;
                expected[67] = 0x28;
                expected[68] = 0x03;
                expected[69] = 0x44;
                expected[70] = 0x24;
                expected[71] = 0x30;
                expected[72] = 0xC3;
                return expected;
            }

            if (helperFiveArguments)
            {
                expected[1] = 59;
                expected[2] = 0x48;
                expected[3] = 0x83;
                expected[4] = 0xEC;
                expected[5] = 0x28;
                expected[6] = 0xB9;
                expected[7] = firstOperand & 0xFF;
                expected[8] = (firstOperand >> 8) & 0xFF;
                expected[9] = (firstOperand >> 16) & 0xFF;
                expected[10] = (firstOperand >> 24) & 0xFF;
                expected[11] = 0xBA;
                expected[12] = secondOperand & 0xFF;
                expected[13] = (secondOperand >> 8) & 0xFF;
                expected[14] = (secondOperand >> 16) & 0xFF;
                expected[15] = (secondOperand >> 24) & 0xFF;
                expected[16] = 0x41;
                expected[17] = 0xB8;
                expected[18] = thirdOperand & 0xFF;
                expected[19] = (thirdOperand >> 8) & 0xFF;
                expected[20] = (thirdOperand >> 16) & 0xFF;
                expected[21] = (thirdOperand >> 24) & 0xFF;
                expected[22] = 0x41;
                expected[23] = 0xB9;
                expected[24] = fourthOperand & 0xFF;
                expected[25] = (fourthOperand >> 8) & 0xFF;
                expected[26] = (fourthOperand >> 16) & 0xFF;
                expected[27] = (fourthOperand >> 24) & 0xFF;
                expected[28] = 0xC7;
                expected[29] = 0x44;
                expected[30] = 0x24;
                expected[31] = 0x20;
                expected[32] = fifthOperand & 0xFF;
                expected[33] = (fifthOperand >> 8) & 0xFF;
                expected[34] = (fifthOperand >> 16) & 0xFF;
                expected[35] = (fifthOperand >> 24) & 0xFF;
                expected[36] = 0xE8;
                expected[37] = 5;
                expected[41] = 0x48;
                expected[42] = 0x83;
                expected[43] = 0xC4;
                expected[44] = 0x28;
                expected[45] = 0xC3;
                expected[46] = 0x89;
                expected[47] = 0xC8;
                expected[48] = 0x01;
                expected[49] = 0xD0;
                expected[50] = 0x44;
                expected[51] = 0x01;
                expected[52] = 0xC0;
                expected[53] = 0x44;
                expected[54] = 0x01;
                expected[55] = 0xC8;
                expected[56] = 0x03;
                expected[57] = 0x44;
                expected[58] = 0x24;
                expected[59] = 0x28;
                expected[60] = 0xC3;
                return expected;
            }

            if (helperFourArguments)
            {
                expected[1] = 47;
                expected[2] = 0x48;
                expected[3] = 0x83;
                expected[4] = 0xEC;
                expected[5] = 0x28;
                expected[6] = 0xB9;
                expected[7] = firstOperand & 0xFF;
                expected[8] = (firstOperand >> 8) & 0xFF;
                expected[9] = (firstOperand >> 16) & 0xFF;
                expected[10] = (firstOperand >> 24) & 0xFF;
                expected[11] = 0xBA;
                expected[12] = secondOperand & 0xFF;
                expected[13] = (secondOperand >> 8) & 0xFF;
                expected[14] = (secondOperand >> 16) & 0xFF;
                expected[15] = (secondOperand >> 24) & 0xFF;
                expected[16] = 0x41;
                expected[17] = 0xB8;
                expected[18] = thirdOperand & 0xFF;
                expected[19] = (thirdOperand >> 8) & 0xFF;
                expected[20] = (thirdOperand >> 16) & 0xFF;
                expected[21] = (thirdOperand >> 24) & 0xFF;
                expected[22] = 0x41;
                expected[23] = 0xB9;
                expected[24] = fourthOperand & 0xFF;
                expected[25] = (fourthOperand >> 8) & 0xFF;
                expected[26] = (fourthOperand >> 16) & 0xFF;
                expected[27] = (fourthOperand >> 24) & 0xFF;
                expected[28] = 0xE8;
                expected[29] = 5;
                expected[33] = 0x48;
                expected[34] = 0x83;
                expected[35] = 0xC4;
                expected[36] = 0x28;
                expected[37] = 0xC3;
                expected[38] = 0x89;
                expected[39] = 0xC8;
                expected[40] = 0x01;
                expected[41] = 0xD0;
                expected[42] = 0x44;
                expected[43] = 0x01;
                expected[44] = 0xC0;
                expected[45] = 0x44;
                expected[46] = 0x01;
                expected[47] = 0xC8;
                expected[48] = 0xC3;
                return expected;
            }

            if (helperThreeArguments)
            {
                expected[1] = 38;
                expected[2] = 0x48;
                expected[3] = 0x83;
                expected[4] = 0xEC;
                expected[5] = 0x28;
                expected[6] = 0xB9;
                expected[7] = firstOperand & 0xFF;
                expected[8] = (firstOperand >> 8) & 0xFF;
                expected[9] = (firstOperand >> 16) & 0xFF;
                expected[10] = (firstOperand >> 24) & 0xFF;
                expected[11] = 0xBA;
                expected[12] = secondOperand & 0xFF;
                expected[13] = (secondOperand >> 8) & 0xFF;
                expected[14] = (secondOperand >> 16) & 0xFF;
                expected[15] = (secondOperand >> 24) & 0xFF;
                expected[16] = 0x41;
                expected[17] = 0xB8;
                expected[18] = thirdOperand & 0xFF;
                expected[19] = (thirdOperand >> 8) & 0xFF;
                expected[20] = (thirdOperand >> 16) & 0xFF;
                expected[21] = (thirdOperand >> 24) & 0xFF;
                expected[22] = 0xE8;
                expected[23] = 5;
                expected[27] = 0x48;
                expected[28] = 0x83;
                expected[29] = 0xC4;
                expected[30] = 0x28;
                expected[31] = 0xC3;
                expected[32] = 0x89;
                expected[33] = 0xC8;
                expected[34] = 0x01;
                expected[35] = 0xD0;
                expected[36] = 0x44;
                expected[37] = 0x01;
                expected[38] = 0xC0;
                expected[39] = 0xC3;
                return expected;
            }

            if (helperTwoArguments)
            {
                expected[1] = 29;
                expected[2] = 0x48;
                expected[3] = 0x83;
                expected[4] = 0xEC;
                expected[5] = 0x28;
                expected[6] = 0xB9;
                expected[7] = firstOperand & 0xFF;
                expected[8] = (firstOperand >> 8) & 0xFF;
                expected[9] = (firstOperand >> 16) & 0xFF;
                expected[10] = (firstOperand >> 24) & 0xFF;
                expected[11] = 0xBA;
                expected[12] = secondOperand & 0xFF;
                expected[13] = (secondOperand >> 8) & 0xFF;
                expected[14] = (secondOperand >> 16) & 0xFF;
                expected[15] = (secondOperand >> 24) & 0xFF;
                expected[16] = 0xE8;
                expected[17] = 5;
                expected[21] = 0x48;
                expected[22] = 0x83;
                expected[23] = 0xC4;
                expected[24] = 0x28;
                expected[25] = 0xC3;
                expected[26] = 0x89;
                expected[27] = 0xC8;
                expected[28] = helperSubtraction ? 0x29 : 0x01;
                expected[29] = 0xD0;
                expected[30] = 0xC3;
                return expected;
            }

            if (helperArgument)
            {
                expected[1] = 22;
                expected[2] = 0x48;
                expected[3] = 0x83;
                expected[4] = 0xEC;
                expected[5] = 0x28;
                expected[6] = 0xB9;
                expected[7] = value & 0xFF;
                expected[8] = (value >> 8) & 0xFF;
                expected[9] = (value >> 16) & 0xFF;
                expected[10] = (value >> 24) & 0xFF;
                expected[11] = 0xE8;
                expected[12] = 5;
                expected[16] = 0x48;
                expected[17] = 0x83;
                expected[18] = 0xC4;
                expected[19] = 0x28;
                expected[20] = 0xC3;
                expected[21] = 0x89;
                expected[22] = 0xC8;
                expected[23] = 0xC3;
                return expected;
            }

            var helper = CreateExpectedBytes(true, value, addition, subtraction,
                multiplication, division, remainder, firstOperand, secondOperand);
            expected[1] = helper[1] + 14;
            expected[2] = 0x48;
            expected[3] = 0x83;
            expected[4] = 0xEC;
            expected[5] = 0x28;
            expected[6] = 0xE8;
            expected[7] = 5;
            expected[11] = 0x48;
            expected[12] = 0x83;
            expected[13] = 0xC4;
            expected[14] = 0x28;
            expected[15] = 0xC3;
            for (var index = 0; index < helper[1]; index++)
            {
                expected[index + 16] = helper[index + 2];
            }
            return expected;
        }
        expected[1] = remainder ? 16 : division ? 14
            : multiplication ? 12 : addition || subtraction ? 11 : 6;
        expected[2] = 0xB8;
        var firstValue = multiplication ? firstOperand : division || remainder ? 40
            : addition || subtraction ? firstOperand == 0 && secondOperand == 0 ? 40 : firstOperand
            : value;
        expected[3] = firstValue & 0xFF;
        expected[4] = (firstValue >> 8) & 0xFF;
        expected[5] = (firstValue >> 16) & 0xFF;
        expected[6] = (firstValue >> 24) & 0xFF;
        if (division || remainder)
        {
            expected[7] = 0x99;
            expected[8] = 0xB9;
            expected[9] = division ? 2 : 3;
            expected[13] = 0xF7;
            expected[14] = 0xF9;
            if (remainder)
            {
                expected[15] = 0x89;
                expected[16] = 0xD0;
                expected[17] = 0xC3;
            }
            else
            {
                expected[15] = 0xC3;
            }
        }
        else if (multiplication)
        {
            expected[7] = 0x69;
            expected[8] = 0xC0;
            expected[9] = secondOperand & 0xFF;
            expected[10] = (secondOperand >> 8) & 0xFF;
            expected[11] = (secondOperand >> 16) & 0xFF;
            expected[12] = (secondOperand >> 24) & 0xFF;
            expected[13] = 0xC3;
        }
        else if (addition || subtraction)
        {
            expected[7] = addition ? 0x05 : 0x2D;
            var rightValue = firstOperand == 0 && secondOperand == 0 ? 2 : secondOperand;
            expected[8] = rightValue & 0xFF;
            expected[9] = (rightValue >> 8) & 0xFF;
            expected[10] = (rightValue >> 16) & 0xFF;
            expected[11] = (rightValue >> 24) & 0xFF;
            expected[12] = 0xC3;
        }
        else
        {
            expected[7] = 0xC3;
        }
        return expected;
    }

    private static int ReadInt32(byte[] bytes, int offset)
        => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)));

    private static async Task<ImmutableArray<SyntaxTree>> ReadComponentsAsync(
        IEnumerable<string> componentNames, string verificationSource)
    {
        var root = RepositoryRoot();
        var components = new List<SyntaxTree>();
        foreach (var name in componentNames)
        {
            var path = Path.Combine(root, "self-host", name + ".terrum");
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
            components.Add(tree);
        }
        components.Add(SyntaxTree.Parse(verificationSource, "bootstrap-int32-main-return.terrum"));
        return components.ToImmutableArray();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Terrum.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate Terrum.slnx.");
    }

    private static async Task<int> ExecuteAotAsync(
        BoundProgram program, NativeRuntimePolicy policy)
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"bootstrap-int32-main-return-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy))
                .ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Failed to start native executable.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15))
                .ConfigureAwait(true);
            return process.ExitCode;
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path).ConfigureAwait(true);
        }
    }
}
