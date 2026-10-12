// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class ManagedScannerComponentTests
{
    [Fact]
    public async Task ScalarFrontendSwitchUsesV86AndReaderRejectsTamperedRecordsAsync()
    {
        const string text = "public int32 Main() { int32 value = 1; switch (value) { 1 => { return 11; }, _ => { return 33; } } }";
        var source = SourceText.From(text);
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
            Assert.Equal(88, BinaryPrimitives.ReadInt32LittleEndian(programBytes));

            var read = ScalarFrontendHost.ReadProgram(source, bytes, programBytes,
                (BoundRuntimePolicy)policy);
            Assert.True(read.Projection.IsValid);
            var function = Assert.Single(read.Projection.Functions);
            Assert.Contains(function.Statements, static statement => statement.Kind == 38);
            Assert.Contains(function.Statements, static statement => statement.Kind == 39);
            Assert.Single(function.Locals, static local => local.IsSwitchSelector);
            const string switchText = "switch (value) { 1 => { return 11; }, _ => { return 33; } }";
            var switchStatement = Assert.Single(function.Statements,
                static statement => statement.Kind == 38);
            Assert.Equal(new TextSpan(text.IndexOf(switchText, StringComparison.Ordinal),
                switchText.Length), switchStatement.Span);
            var arms = function.Statements.Where(static statement => statement.Kind == 39).ToArray();
            Assert.Equal(2, arms.Length);
            const string caseArmText = "1 => { return 11; }";
            const string defaultArmText = "_ => { return 33; }";
            const string caseBodyText = "{ return 11; }";
            const string defaultBodyText = "{ return 33; }";
            Assert.Equal(new TextSpan(text.IndexOf(caseArmText, StringComparison.Ordinal),
                caseArmText.Length), arms[0].Span);
            Assert.Equal(new TextSpan(text.IndexOf(defaultArmText, StringComparison.Ordinal),
                defaultArmText.Length), arms[1].Span);
            Assert.Equal(new TextSpan(text.IndexOf(caseBodyText, StringComparison.Ordinal),
                caseBodyText.Length), arms[0].BodySpan);
            Assert.Equal(new TextSpan(text.IndexOf(defaultBodyText, StringComparison.Ordinal),
                defaultBodyText.Length), arms[1].BodySpan);
            Assert.Equal(new TextSpan(text.IndexOf("1 =>", StringComparison.Ordinal), 1),
                function.Nodes[arms[0].Root].Span);
            Assert.Equal(-1, arms[1].Root);

            var functionIndex = Array.FindIndex(read.Projection.Functions.ToArray(),
                static projected => projected.Statements.Any(statement => statement.Kind == 38));
            Assert.InRange(functionIndex, 0, read.Projection.Functions.Length - 1);
            var functionPayloadOffset = 4 + functionIndex * ScalarFrontendHost.ProgramFrameWordCount + 4;
            var selectorLocalIndex = Array.FindIndex(function.Locals.ToArray(),
                static local => local.IsSwitchSelector);
            Assert.InRange(selectorLocalIndex, 0, function.Locals.Length - 1);
            var selectorFlagsOffset = (functionPayloadOffset + 300
                + selectorLocalIndex * 4 + 1) * sizeof(int);
            var tamperedLocal = (byte[])programBytes.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(tamperedLocal.AsSpan(selectorFlagsOffset), 0);
            Assert.Throws<ArgumentException>(() => {
                ScalarFrontendHost.ReadProgram(source, bytes, tamperedLocal, (BoundRuntimePolicy)policy);
            });

            var selectorTypeOffset = (functionPayloadOffset + 300
                + selectorLocalIndex * 4) * sizeof(int);
            var tamperedSelectorType = (byte[])programBytes.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(tamperedSelectorType.AsSpan(selectorTypeOffset), 7);
            Assert.Throws<ArgumentException>(() => {
                ScalarFrontendHost.ReadProgram(source, bytes, tamperedSelectorType, (BoundRuntimePolicy)policy);
            });

            var switchIndex = Array.FindIndex(function.Statements.ToArray(),
                static statement => statement.Kind == 38);
            Assert.InRange(switchIndex, 0, function.Statements.Length - 1);
            var switchKindOffset = (functionPayloadOffset + 332
                + switchIndex * 5) * sizeof(int);
            var tamperedStatement = (byte[])programBytes.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(tamperedStatement.AsSpan(switchKindOffset), 40);
            Assert.Throws<ArgumentException>(() => {
                ScalarFrontendHost.ReadProgram(source, bytes, tamperedStatement, (BoundRuntimePolicy)policy);
            });
        }
    }

    [Fact]
    public async Task ScalarFrontendReaderRejectsEditedMalformedSyntaxWithUnchangedSwitchBuffersAsync()
    {
        const string sourceText = "public int32 Main() { switch (1) { 1 => {}, _ => {} } return Broken(); } int32 Broken() => 1 + 2;";
        var malformedSource = SourceText.From(sourceText.Replace("1 + 2", "1+  2", StringComparison.Ordinal));
        Assert.Equal(sourceText.Length, malformedSource.Length);

        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var (bytes, programBytes, _) = await ProduceScalarProgramAsync(sourceText, policy);
            Assert.Throws<ArgumentException>(() => {
                ScalarFrontendHost.ReadProgram(malformedSource, bytes, programBytes, (BoundRuntimePolicy)policy);
            });
        }
    }
}
