using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class IntegerParsingRadixModuleTests
{
    private const int MaximumSourceCodeUnits = 16_384;
    private const string SourceIntegerParsingNamespace = "Terrum.CodeAnalysis.Numerics64.";

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task IntegerParsingRadixModuleMatchesHostOracleAcrossFreshSourceGrantsAsync(
        BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentNames = new[] { "WideIntegerFacts", "IntegerParsing" };
        var trees = new SyntaxTree[componentNames.Length + 1];
        for (var index = 0; index < componentNames.Length; index++)
        {
            var path = Path.Combine(root, "self-host", componentNames[index] + ".terrum");
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            trees[index] = SyntaxTree.Parse(text, path);
            Assert.False(trees[index].HasErrors, string.Join(Environment.NewLine, trees[index].Diagnostics));
        }

        trees[^1] = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis.Numerics64;
            int32 Main() { return 0; }

            bool MatchesUInt64(uint64 value, int32 expectedStart) {
                return int32(value & uint64(65535)) == int32(Host.Source[expectedStart])
                    && int32((value >> 16) & uint64(65535)) == int32(Host.Source[expectedStart + 1])
                    && int32((value >> 32) & uint64(65535)) == int32(Host.Source[expectedStart + 2])
                    && int32((value >> 48) & uint64(65535)) == int32(Host.Source[expectedStart + 3]);
            }
            bool MatchesInt64(int64 value, int32 expectedStart) {
                return int32(value & int64(65535)) == int32(Host.Source[expectedStart])
                    && int32((value >> 16) & int64(65535)) == int32(Host.Source[expectedStart + 1])
                    && int32((value >> 32) & int64(65535)) == int32(Host.Source[expectedStart + 2])
                    && int32((value >> 48) & int64(65535)) == int32(Host.Source[expectedStart + 3]);
            }
            bool MatchesUInt32(uint32 value, int32 expectedStart) {
                return int32(value & uint32(65535)) == int32(Host.Source[expectedStart])
                    && int32((value >> 16) & uint32(65535)) == int32(Host.Source[expectedStart + 1])
                    && Host.Source[expectedStart + 2] == char16(0)
                    && Host.Source[expectedStart + 3] == char16(0);
            }
            bool MatchesInt32(int32 value, int32 expectedStart) {
                return (value & int32(65535)) == int32(Host.Source[expectedStart])
                    && ((value >> 16) & int32(65535)) == int32(Host.Source[expectedStart + 1])
                    && Host.Source[expectedStart + 2] == char16(0)
                    && Host.Source[expectedStart + 3] == char16(0);
            }
            bool MatchesChar16(char16 value, int32 expectedStart) {
                return int32(value) == int32(Host.Source[expectedStart])
                    && Host.Source[expectedStart + 1] == char16(0)
                    && Host.Source[expectedStart + 2] == char16(0)
                    && Host.Source[expectedStart + 3] == char16(0);
            }

            public int32 VerifyBatch() {
                mut int32 position = 0;
                mut int32 caseIndex = 0;
                while (position < Host.Source.Length) {
                    if (Host.Source.Length - position < 8) { return -1; }
                    int32 api = int32(Host.Source[position]);
                    uint32 radix = uint32(Host.Source[position + 1]);
                    int32 textLength = int32(Host.Source[position + 2]);
                    int32 expectedSuccess = int32(Host.Source[position + 3]);
                    int32 textStart = position + 8;
                    if (textLength > Host.Source.Length - textStart) { return -2; }
                    if (api == 0) {
                        if (TryParseUInt64Base(textStart, textLength, radix, init var parsed)) {
                            if (expectedSuccess != 1 || !MatchesUInt64(parsed, position + 4)) { return caseIndex + 1; }
                        } else if (expectedSuccess != 0) { return caseIndex + 1; }
                    } else if (api == 1) {
                        if (TryParseInt64Base(textStart, textLength, radix, init var parsed)) {
                            if (expectedSuccess != 1 || !MatchesInt64(parsed, position + 4)) { return caseIndex + 1; }
                        } else if (expectedSuccess != 0) { return caseIndex + 1; }
                    } else if (api == 2) {
                        if (TryParseUInt32Base(textStart, textLength, radix, init var parsed)) {
                            if (expectedSuccess != 1 || !MatchesUInt32(parsed, position + 4)) { return caseIndex + 1; }
                        } else if (expectedSuccess != 0) { return caseIndex + 1; }
                    } else if (api == 3) {
                        if (TryParseInt32Base(textStart, textLength, radix, init var parsed)) {
                            if (expectedSuccess != 1 || !MatchesInt32(parsed, position + 4)) { return caseIndex + 1; }
                        } else if (expectedSuccess != 0) { return caseIndex + 1; }
                    } else if (api == 4) {
                        if (TryParseChar16Base(textStart, textLength, radix, init var parsed)) {
                            if (expectedSuccess != 1 || !MatchesChar16(parsed, position + 4)) { return caseIndex + 1; }
                        } else if (expectedSuccess != 0) { return caseIndex + 1; }
                    } else {
                        return -3;
                    }
                    position = textStart + textLength;
                    caseIndex = caseIndex + 1;
                }
                return 0;
            }
            """, "integer-parsing-radix-module.terrum");

        var producerCompilation = Compilation.Create(trees,
        [
            new HostBuffer("Source", PrimitiveType.Char16, [0], false) {
                IsInvocationScoped = true,
            },
        ], policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producerCompilation.Program),
            ["Terrum.CodeAnalysis.Numerics64.VerifyBatch"]);

        var consumerTree = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis.Numerics64;
            int32 Main() { return VerifyBatch(); }
            """, "integer-parsing-radix-consumer.terrum");
        var consumerCompilation = Compilation.Create(consumerTree, ModuleCompiler.CreateReference(producer), policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);

        foreach (var radix in Enumerable.Range(2, 35))
        {
            var fixtures = FixturesForRadix(radix).ToArray();
            ExecuteBatch(fixtures, linked, policy, $"radix {radix}");
        }

        var invalidRadixFixtures = (
            from radix in new[] { 0, 1, 37 }
            from api in Enum.GetValues<ParserApi>()
            select new ParseFixture(api, radix, "1")).ToArray();
        ExecuteBatch(invalidRadixFixtures, linked, policy, "invalid radices");

        var maximumLengthZeros = new string('0', MaximumSourceCodeUnits - 8);
        var maximumLengthNines = new string('9', MaximumSourceCodeUnits - 8);
        Assert.Equal(MaximumSourceCodeUnits, 8 + maximumLengthZeros.Length);
        foreach (var api in Enum.GetValues<ParserApi>())
        {
            ExecuteBatch([new ParseFixture(api, 10, maximumLengthZeros)],
                linked, policy, $"maximum-length zero digits for {api}");
            ExecuteBatch([new ParseFixture(api, 10, maximumLengthNines)],
                linked, policy, $"maximum-length overflowing digits for {api}");
        }
        foreach (var radix in Enumerable.Range(2, 35))
        {
            var maximumDigit = Digit(radix - 1);
            ExecuteBatch([new ParseFixture(ParserApi.UInt64, radix, maximumLengthZeros)],
                linked, policy, $"maximum-length zero digits in radix {radix}");
            ExecuteBatch([new ParseFixture(ParserApi.UInt64, radix,
                    new string(maximumDigit, MaximumSourceCodeUnits - 8))],
                linked, policy, $"maximum-length overflow digits in radix {radix}");
        }
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SourceGrantIntegerParsingScalarExportsMatchHostOracleAsync(
        BoundRuntimePolicy policy)
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var root = RepositoryRoot();
        var componentNames = new[] { "WideIntegerFacts", "SourceIntegerParsing" };
        var trees = new SyntaxTree[componentNames.Length];
        for (var index = 0; index < componentNames.Length; index++)
        {
            var componentPath = Path.Combine(root, "self-host", componentNames[index] + ".terrum");
            var text = await File.ReadAllTextAsync(componentPath).ConfigureAwait(true);
            trees[index] = SyntaxTree.Parse(text, componentPath);
            Assert.False(trees[index].HasErrors, string.Join(Environment.NewLine, trees[index].Diagnostics));
        }

        var compilation = Compilation.CreateLibrary(trees,
        [
            new HostBuffer("Source", PrimitiveType.Char16, [], false) {
                IsInvocationScoped = true,
            },
        ], policy);
        Assert.False(compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = NativeEmitter.EmitLibraryWithSourceGrant(
            Assert.IsType<BoundProgram>(compilation.Program),
            Enum.GetValues<ParserApi>().SelectMany(static api => new[] {
                SourceIntegerParsingNamespace + "CanParse" + api + "Base",
                SourceIntegerParsingNamespace + "Parse" + api + "Base",
            }).ToArray(), (NativeRuntimePolicy)policy);
        Assert.Equal(Enum.GetValues<ParserApi>().Length * 2, artifact.Exports.Length);
        Assert.All(artifact.Exports, static export => {
            Assert.True(export.SourceDependent);
            Assert.Equal([PrimitiveType.Int32, PrimitiveType.Int32, PrimitiveType.UInt32],
                export.ParameterTypes.ToArray());
            var isStatus = export.Name.StartsWith(
                SourceIntegerParsingNamespace + "CanParse", StringComparison.Ordinal);
            var parserName = export.Name[(SourceIntegerParsingNamespace.Length
                + (isStatus ? "CanParse" : "Parse").Length)..];
            var api = Enum.GetValues<ParserApi>().Single(value => parserName == value + "Base");
            Assert.Equal(isStatus ? PrimitiveType.Bool : ResultType(api),
                export.ReturnType);
        });

        var path = Path.Combine(Path.GetTempPath(), $"terrum-source-integer-parsing-{Guid.NewGuid():N}.dll");
        try
        {
            await File.WriteAllBytesAsync(path, artifact.Image).ConfigureAwait(true);
            var module = NativeLibrary.Load(path);
            try
            {
                var bindings = new NativeSourceGrantTestBindings(module,
                    [.. artifact.Exports.Select(static export => (export.Name, export.SourceDependent,
                        export.ReturnType, export.ParameterTypes))]);
                foreach (var radix in Enumerable.Range(2, 35))
                {
                    foreach (var fixture in FixturesForRadix(radix))
                    {
                        AssertSourceGrantFixture(bindings, fixture);
                    }
                }

                foreach (var api in Enum.GetValues<ParserApi>())
                {
                    foreach (var text in new[] { "?", "+1", " 1", "0x1", "\0", "\u0661", "\uD800", "-" })
                    {
                        AssertSourceGrantFixture(bindings, new ParseFixture(api, 10, text));
                    }

                    var source = "1".ToCharArray();
                    Assert.False(InvokeCanParse(bindings, api, source, -1, 1, 10));
                    Assert.False(InvokeCanParse(bindings, api, source, 0, 0, 10));
                    Assert.False(InvokeCanParse(bindings, api, source, 0, -1, 10));
                    Assert.False(InvokeCanParse(bindings, api, source, 1, 1, 10));
                    Assert.False(InvokeCanParse(bindings, api, source, int.MaxValue, 1, 10));
                    Assert.False(InvokeCanParse(bindings, api, source, 0, int.MaxValue, 10));
                    Assert.False(InvokeCanParse(bindings, api, source, 0, 1, 0));
                    Assert.False(InvokeCanParse(bindings, api, source, 0, 1, 1));
                    Assert.False(InvokeCanParse(bindings, api, source, 0, 1, 37));
                    Assert.False(InvokeCanParse(bindings, api, source, 0, 1, uint.MaxValue));
                    Assert.False(InvokeCanParse(bindings, api, [], 0, 1, 10));

                    if (policy == BoundRuntimePolicy.CheckedExceptions)
                    {
                        Assert.Throws<TargetInvocationException>(() =>
                            InvokeParsedValue(bindings, api, ['?'], 0, 1, 10));
                        Assert.Throws<TargetInvocationException>(() =>
                            InvokeParsedValue(bindings, api, source, -1, 1, 10));
                        Assert.Throws<TargetInvocationException>(() =>
                            InvokeParsedValue(bindings, api, source, 0, 1, 1));
                    }
                }

                var maximumZeros = new string('0', MaximumSourceCodeUnits).ToCharArray();
                var maximumNines = new string('9', MaximumSourceCodeUnits).ToCharArray();
                foreach (var api in Enum.GetValues<ParserApi>())
                {
                    Assert.True(InvokeCanParse(bindings, api, maximumZeros, 0,
                        MaximumSourceCodeUnits, 10));
                    Assert.Equal(BigInteger.Zero, InvokeParsedValue(bindings, api, maximumZeros, 0,
                        MaximumSourceCodeUnits, 10));
                    Assert.False(InvokeCanParse(bindings, api, maximumNines, 0,
                        MaximumSourceCodeUnits, 10));
                }

                var maximumZerosForRadix = new string('0', MaximumSourceCodeUnits).ToCharArray();
                foreach (var radix in Enumerable.Range(2, 35))
                {
                    var maximumDigitsForRadix = new string(Digit(radix - 1), MaximumSourceCodeUnits).ToCharArray();
                    Assert.True(InvokeCanParse(bindings, ParserApi.UInt64, maximumZerosForRadix, 0,
                        MaximumSourceCodeUnits, (uint)radix));
                    Assert.Equal(BigInteger.Zero, InvokeParsedValue(bindings, ParserApi.UInt64,
                        maximumZerosForRadix, 0, MaximumSourceCodeUnits, (uint)radix));
                    Assert.False(InvokeCanParse(bindings, ParserApi.UInt64, maximumDigitsForRadix, 0,
                        MaximumSourceCodeUnits, (uint)radix));
                }

                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    InvokeCanParse(bindings, ParserApi.UInt64, new char[MaximumSourceCodeUnits + 1],
                        0, 1, 10));
            }
            finally
            {
                NativeLibrary.Free(module);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("ParseUInt64Base(0, 1, 10)")]
    [InlineData("ParseInt32Base(0, 1, 10)")]
    public async Task InvalidSourceGrantValueQueryFailsFastInSubprocessAsync(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var root = RepositoryRoot();
        var componentNames = new[] { "WideIntegerFacts", "SourceIntegerParsing" };
        var trees = new SyntaxTree[componentNames.Length + 1];
        for (var index = 0; index < componentNames.Length; index++)
        {
            var path = Path.Combine(root, "self-host", componentNames[index] + ".terrum");
            trees[index] = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
        }
        trees[^1] = SyntaxTree.Parse(
            $"namespace Terrum.CodeAnalysis.Numerics64; int32 Main() {{ return int32({expression}); }}",
            "source-integer-parsing-fail-fast.terrum");

        var compilation = Compilation.Create(trees,
            [new HostBuffer("Source", PrimitiveType.Char16, [(int)'?'], false)],
            BoundRuntimePolicy.FailFast);
        Assert.False(compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);

        var pathToExecutable = Path.Combine(Path.GetTempPath(),
            $"terrum-source-integer-parsing-fail-fast-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(pathToExecutable,
                NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(pathToExecutable) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Could not start the compiled source parser.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
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
            Assert.NotEqual(0, process.ExitCode);
        }
        finally
        {
            File.Delete(pathToExecutable);
        }
    }

    private static void AssertSourceGrantFixture(NativeSourceGrantTestBindings bindings, ParseFixture fixture)
    {
        var source = ("!" + fixture.Text + "!").ToCharArray();
        var expected = ParseOracle(fixture);
        var actualIsValid = InvokeCanParse(bindings, fixture.Api, source, 1, fixture.Text.Length,
            (uint)fixture.Radix);
        Assert.Equal(expected.HasValue, actualIsValid);
        if (expected.HasValue)
        {
            Assert.Equal(expected.Value, InvokeParsedValue(bindings, fixture.Api, source, 1,
                fixture.Text.Length, (uint)fixture.Radix));
        }
    }

    private static bool InvokeCanParse(NativeSourceGrantTestBindings bindings, ParserApi api,
        char[] source, int start, int length, uint radix)
    {
        return bindings.Invoke<byte>(SourceIntegerParsingNamespace + "CanParse" + api + "Base",
            source, start, length, radix) != 0;
    }

    private static BigInteger InvokeParsedValue(NativeSourceGrantTestBindings bindings, ParserApi api,
        char[] source, int start, int length, uint radix) => api switch {
        ParserApi.UInt64 => new BigInteger(bindings.Invoke<ulong>(
            SourceIntegerParsingNamespace + "ParseUInt64Base", source, start, length, radix)),
        ParserApi.Int64 => new BigInteger(bindings.Invoke<long>(
            SourceIntegerParsingNamespace + "ParseInt64Base", source, start, length, radix)),
        ParserApi.UInt32 => new BigInteger(bindings.Invoke<uint>(
            SourceIntegerParsingNamespace + "ParseUInt32Base", source, start, length, radix)),
        ParserApi.Int32 => new BigInteger(bindings.Invoke<int>(
            SourceIntegerParsingNamespace + "ParseInt32Base", source, start, length, radix)),
        ParserApi.Char16 => new BigInteger(bindings.Invoke<ushort>(
            SourceIntegerParsingNamespace + "ParseChar16Base", source, start, length, radix)),
        _ => throw new ArgumentOutOfRangeException(nameof(api)),
    };

    private static PrimitiveType ResultType(ParserApi api) => api switch {
        ParserApi.UInt64 => PrimitiveType.UInt64,
        ParserApi.Int64 => PrimitiveType.Int64,
        ParserApi.UInt32 => PrimitiveType.UInt32,
        ParserApi.Int32 => PrimitiveType.Int32,
        ParserApi.Char16 => PrimitiveType.Char16,
        _ => throw new ArgumentOutOfRangeException(nameof(api)),
    };

    private static void ExecuteBatch(ParseFixture[] fixtures, BoundProgram linked,
        BoundRuntimePolicy policy, string description)
    {
        var values = new List<int>(fixtures.Sum(static fixture => 8 + fixture.Text.Length));
        for (var index = 0; index < fixtures.Length; index++)
        {
            var fixture = fixtures[index];
            var expected = ParseOracle(fixture);
            values.Add((int)fixture.Api);
            values.Add(fixture.Radix);
            values.Add(fixture.Text.Length);
            values.Add(expected.HasValue ? 1 : 0);

            var bits = expected.GetValueOrDefault();
            if (bits.Sign < 0)
            {
                bits += BigInteger.One << BitWidth(fixture.Api);
            }
            for (var part = 0; part < 4; part++)
            {
                values.Add((int)((bits >> (part * 16)) & ushort.MaxValue));
            }
            values.AddRange(fixture.Text.Select(static codeUnit => (int)codeUnit));
        }

        Assert.InRange(values.Count, 1, MaximumSourceCodeUnits);
        var grant = new HostBuffer("Source", PrimitiveType.Char16, [.. values], false) {
            IsInvocationScoped = true,
        };
        var actual = NativeJit.ExecuteWithSourceGrant(linked, [grant], (NativeRuntimePolicy)policy);
        var failure = actual > 0 && actual <= fixtures.Length
            ? $"case {actual - 1}: {fixtures[actual - 1]}"
            : $"unexpected module result {actual}";
        Assert.True(actual == 0, $"{description}: {failure}.");
    }

    private static IEnumerable<ParseFixture> FixturesForRadix(int radix)
    {
        foreach (var api in Enum.GetValues<ParserApi>())
        {
            yield return new(api, radix, "0");
            yield return new(api, radix, "?");
            yield return new(api, radix, "\u0661");
            yield return new(api, radix, "\uFF11");
            yield return new(api, radix, "\U0001D7D9");
            yield return new(api, radix, "1_0");
            yield return new(api, radix, "1__0");
            yield return new(api, radix, "1_");
            yield return new(api, radix, "");
            yield return new(api, radix, "-1");
            if (radix > 10)
            {
                yield return new(api, radix, Digit(radix - 1).ToString().ToUpperInvariant());
            }
            if (radix < 36)
            {
                yield return new(api, radix, Digit(radix).ToString());
            }
        }

        yield return new(ParserApi.UInt64, radix, ToBase(ulong.MaxValue, radix));
        yield return new(ParserApi.UInt64, radix, ToBase((BigInteger)ulong.MaxValue + 1, radix));
        yield return new(ParserApi.Int64, radix, ToBase(long.MaxValue, radix));
        yield return new(ParserApi.Int64, radix, "-" + ToBase(BigInteger.One << 63, radix));
        yield return new(ParserApi.Int64, radix, ToBase((BigInteger)long.MaxValue + 1, radix));
        yield return new(ParserApi.Int64, radix, "-" + ToBase((BigInteger.One << 63) + 1, radix));
        yield return new(ParserApi.UInt32, radix, ToBase(uint.MaxValue, radix));
        yield return new(ParserApi.UInt32, radix, ToBase((BigInteger)uint.MaxValue + 1, radix));
        yield return new(ParserApi.Int32, radix, ToBase(int.MaxValue, radix));
        yield return new(ParserApi.Int32, radix, ToBase((BigInteger)int.MaxValue + 1, radix));
        yield return new(ParserApi.Int32, radix, "-" + ToBase((BigInteger)int.MaxValue + 1, radix));
        yield return new(ParserApi.Int32, radix, "-" + ToBase(int.MaxValue, radix));
        yield return new(ParserApi.Int32, radix, "-" + ToBase((BigInteger)int.MaxValue + 2, radix));
        yield return new(ParserApi.Int32, radix, "-1");
        yield return new(ParserApi.Char16, radix, ToBase((int)char.MaxValue, radix));
        yield return new(ParserApi.Char16, radix, ToBase((BigInteger)(int)char.MaxValue + 1, radix));
        yield return new(ParserApi.Char16, radix, ToBase(55296, radix));

        yield return new(ParserApi.Int64, radix, "-0");
        yield return new(ParserApi.Int32, radix, "-0");
        yield return new(ParserApi.UInt64, radix, "-0");
        yield return new(ParserApi.UInt32, radix, "-0");
        yield return new(ParserApi.Char16, radix, "-0");

        yield return new(ParserApi.UInt64, radix, "_1");
        yield return new(ParserApi.Int64, radix, "_1");
        yield return new(ParserApi.UInt32, radix, "_1");
        yield return new(ParserApi.Int32, radix, "_1");
        yield return new(ParserApi.Char16, radix, "_1");
    }

    private static BigInteger? ParseOracle(ParseFixture fixture)
    {
        if (fixture.Radix is < 2 or > 36)
        {
            return null;
        }

        var text = fixture.Text;
        var start = 0;
        var negative = text.StartsWith("-", StringComparison.Ordinal);
        if (negative)
        {
            if (fixture.Api is not (ParserApi.Int64 or ParserApi.Int32))
            {
                return null;
            }
            start = 1;
        }
        if (start == text.Length)
        {
            return null;
        }

        var value = BigInteger.Zero;
        var hasDigit = false;
        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '_')
            {
                if (fixture.Radix is not (2 or 10 or 16)
                    || (fixture.Radix == 10 && index == start))
                {
                    return null;
                }
                var next = index + 1;
                while (next < text.Length && text[next] == '_')
                {
                    next++;
                }
                if (next == text.Length)
                {
                    return null;
                }
                index = next - 1;
                continue;
            }

            var digit = character switch {
                >= '0' and <= '9' => character - '0',
                >= 'A' and <= 'Z' => character - 'A' + 10,
                >= 'a' and <= 'z' => character - 'a' + 10,
                _ => -1,
            };
            if (digit < 0 || digit >= fixture.Radix)
            {
                return null;
            }
            hasDigit = true;
            value = value * fixture.Radix + digit;
        }

        if (!hasDigit)
        {
            return null;
        }
        if (negative)
        {
            value = -value;
        }

        var (minimum, maximum) = fixture.Api switch {
            ParserApi.UInt64 => (BigInteger.Zero, (BigInteger)ulong.MaxValue),
            ParserApi.Int64 => ((BigInteger)long.MinValue, (BigInteger)long.MaxValue),
            ParserApi.UInt32 => (BigInteger.Zero, (BigInteger)uint.MaxValue),
            ParserApi.Int32 => ((BigInteger)int.MinValue, (BigInteger)int.MaxValue),
            ParserApi.Char16 => (BigInteger.Zero, (BigInteger)(int)char.MaxValue),
            _ => throw new ArgumentOutOfRangeException(nameof(fixture)),
        };
        return value < minimum || value > maximum ? null : value;
    }

    private static int BitWidth(ParserApi api) => api switch {
        ParserApi.UInt64 or ParserApi.Int64 => 64,
        ParserApi.UInt32 or ParserApi.Int32 => 32,
        ParserApi.Char16 => 16,
        _ => throw new ArgumentOutOfRangeException(nameof(api)),
    };

    private static char Digit(int value) => "0123456789abcdefghijklmnopqrstuvwxyz"[value];

    private static string ToBase(BigInteger value, int radix)
    {
        var characters = new char[128];
        var start = characters.Length;
        do
        {
            value = BigInteger.DivRem(value, radix, out var remainder);
            characters[--start] = Digit((int)remainder);
        }
        while (value > BigInteger.Zero);
        return new string(characters, start, characters.Length - start);
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private enum ParserApi
    {
        UInt64,
        Int64,
        UInt32,
        Int32,
        Char16,
    }

    private sealed record ParseFixture(ParserApi Api, int Radix, string Text);
}
