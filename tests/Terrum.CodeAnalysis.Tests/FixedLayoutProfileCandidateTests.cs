// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class FixedLayoutProfileCandidateTests
{
    private const string Profile = "windows-x64-natural-sequential-v1";
    private const int Target = 1;
    private const int MaximumDepth = 128;

    private const string Declarations = """
        public struct Inner { public int32 Signed; public char16 Unit; }
        public struct Outer { public char16 Prefix; public Inner Value; public uint32 Tail; }
        public struct Padded { public char16 Prefix; public uint32 Value; public char16 Suffix; }
        public struct Over16 { public int64 Value; }
        public struct BooleanField { public bool Value; }
        public struct Float32Field { public float32 Value; }
        public struct FloatingField { public float64 Value; }
        unique struct UniqueValue { public int32 Value; }
        public struct MissingNested { public Inner Value; }
        """;

    [Fact]
    public async Task CandidateLayoutsMatchIndependentWindowsX64StorageAsync()
    {
        var program = Compile(Declarations);
        var claims = Claims("Inner", "Outer", "Padded", "Over16", "BooleanField", "FloatingField",
            "Float32Field", "UniqueValue", "MissingNested");

        var inner = Assert.IsType<CandidateDescriptor>(Describe(program, "Inner", claims));
        Assert.Equal((8, 4), (inner.Layout.Size, inner.Layout.Alignment));
        Assert.Collection(inner.Layout.Fields,
            field => Assert.Equal((0, 4), (field.Offset, field.Size)),
            field => Assert.Equal((4, 2), (field.Offset, field.Size)));

        var outer = Assert.IsType<CandidateDescriptor>(Describe(program, "Outer", claims));
        Assert.Equal((16, 4), (outer.Layout.Size, outer.Layout.Alignment));
        Assert.Collection(outer.Layout.Fields,
            field => Assert.Equal((0, 2), (field.Offset, field.Size)),
            field => Assert.Equal((4, 8), (field.Offset, field.Size)),
            field => Assert.Equal((12, 4), (field.Offset, field.Size)));

        var padded = Assert.IsType<CandidateDescriptor>(Describe(program, "Padded", claims));
        Assert.Equal((12, 4), (padded.Layout.Size, padded.Layout.Alignment));
        Assert.Collection(padded.Layout.Fields,
            field => Assert.Equal((0, 2), (field.Offset, field.Size)),
            field => Assert.Equal((4, 4), (field.Offset, field.Size)),
            field => Assert.Equal((8, 2), (field.Offset, field.Size)));

        await RunNativeStorageProbeAsync().ConfigureAwait(true);
    }

    [Fact]
    public void IndependentCandidateDescriptorsUseNominalNamesAndRejectChangedContracts()
    {
        var producer = Compile("""
            public struct Leaf { public int32 Value; }
            public struct Pair { public Leaf First; public char16 Last; }
            int32 Change() { return 1; }
            """);
        var replacement = Compile("""
            public struct ReorderedIndex { public uint64 Value; }
            public struct Leaf { public int32 Value; }
            public struct Pair { public Leaf First; public char16 Last; }
            int32 Change() { return 2; }
            """);
        Assert.NotEqual((int)Find(producer, "Pair").Fields[0].Type,
            (int)Find(replacement, "Pair").Fields[0].Type);

        var producerDescriptor = Describe(producer, "Pair", Claims("Leaf", "Pair"));
        var replacementDescriptor = Describe(replacement, "Pair", Claims("Leaf", "Pair"));
        Assert.True(AreCompatible(producerDescriptor, replacementDescriptor));

        var changedType = Compile("""
            public struct Leaf { public uint32 Value; }
            public struct Pair { public Leaf First; public char16 Last; }
            int32 Change() { return 2; }
            """);
        Assert.False(AreCompatible(producerDescriptor,
            Describe(changedType, "Pair", Claims("Leaf", "Pair"))));

        var changedNominal = Compile("""
            public struct Leaf { public int32 Value; }
            public struct PairReplacement { public Leaf First; public char16 Last; }
            int32 Change() { return 2; }
            """);
        Assert.False(AreCompatible(producerDescriptor,
            Describe(changedNominal, "PairReplacement", Claims("Leaf", "PairReplacement"))));

        var changedFieldName = Compile("""
            public struct Leaf { public int32 Data; }
            public struct Pair { public Leaf First; public char16 Last; }
            int32 Change() { return 2; }
            """);
        Assert.False(AreCompatible(producerDescriptor,
            Describe(changedFieldName, "Pair", Claims("Leaf", "Pair"))));

        var changedOrder = Compile("""
            public struct Leaf { public int32 Value; }
            public struct Pair { public char16 Last; public Leaf First; }
            int32 Change() { return 2; }
            """);
        Assert.False(AreCompatible(producerDescriptor,
            Describe(changedOrder, "Pair", Claims("Leaf", "Pair"))));

        var changedAccess = Compile("""
            public struct Leaf { int32 Value; }
            public struct Pair { public Leaf First; public char16 Last; }
            int32 Change() { return 2; }
            """);
        Assert.False(AreCompatible(producerDescriptor,
            Describe(changedAccess, "Pair", Claims("Leaf", "Pair"))));

        var changedMutation = Compile("""
            public struct Leaf { public mut int32 Value; }
            public struct Pair { public Leaf First; public char16 Last; }
            int32 Change() { return 2; }
            """);
        Assert.False(AreCompatible(producerDescriptor,
            Describe(changedMutation, "Pair", Claims("Leaf", "Pair"))));

        var unsafeField = Find(producer, "Leaf").Fields[0] with { RequiresUnsafeCaller = true };
        var forgedUnsafe = producer with {
            Structures = producer.Structures.SetItem(0,
                Find(producer, "Leaf") with { Fields = [unsafeField] }),
        };
        Assert.False(AreCompatible(producerDescriptor,
            Describe(forgedUnsafe, "Pair", Claims("Leaf", "Pair"))));

        Assert.False(AreCompatible(producerDescriptor, Describe(producer, "Leaf",
            WithClaim(Claims("Leaf", "Pair"), "Leaf",
                new CandidateClaim(false, "Sequential", Profile, Target)))));
        Assert.False(AreCompatible(producerDescriptor, Describe(producer, "Pair",
            WithClaim(Claims("Leaf", "Pair"), "Pair",
                new CandidateClaim(true, "Sequential", Profile, 2)))));
    }

    [Fact]
    public void CandidateEvaluatorRejectsUnsupportedMembershipAndForgedLayouts()
    {
        var program = Compile(Declarations);
        var claims = Claims("Inner", "Outer", "Padded", "Over16", "BooleanField", "FloatingField",
            "Float32Field", "UniqueValue", "MissingNested");

        Assert.Null(Describe(program, "Outer", Claims("Outer")));
        Assert.Null(Describe(program, "MissingNested", Claims("MissingNested")));
        Assert.Null(Describe(program, "BooleanField", claims));
        Assert.Null(Describe(program, "Float32Field", claims));
        Assert.Null(Describe(program, "FloatingField", claims));
        Assert.Null(Describe(program, "UniqueValue", claims));
        Assert.Null(Describe(program, "Outer", WithClaim(Claims("Inner", "Outer"), "Outer",
            new CandidateClaim(true, "Sequential", Profile, Target, 8))));
        Assert.Null(Describe(program, "Outer", WithClaim(Claims("Inner", "Outer"), "Outer",
            new CandidateClaim(true, "Sequential", "other-profile", Target))));
        Assert.Null(Describe(program, "Outer", WithClaim(Claims("Inner", "Outer"), "Outer",
            new CandidateClaim(true, "Automatic", Profile, Target))));
        Assert.Null(Describe(program, "Outer", WithClaim(Claims("Inner", "Outer"), "Outer",
            new CandidateClaim(true, "Sequential", Profile, Target, 4)), target: 2));
        Assert.Null(Describe(program, "Outer", WithClaim(Claims("Inner", "Outer"), "Outer",
            new CandidateClaim(true, "Sequential", Profile, 2)), target: 2));

        var over16 = Find(program, "Over16") with {
            Fields = [
                new BoundStructField("Prefix", PrimitiveType.Char16, 0, 2),
                new BoundStructField("Value", PrimitiveType.Int64, 8, 8),
                new BoundStructField("Suffix", PrimitiveType.Char16, 16, 2),
            ],
            Size = 24,
        };
        Assert.Null(Describe(program with {
            Structures = program.Structures.SetItem(IndexOf(program, "Over16"), over16),
        }, "Over16", claims));

        var managed = Find(program, "Over16") with {
            Fields = [new BoundStructField("Value", PrimitiveType.ManagedNode, 0, 4)],
            Size = 4,
        };
        Assert.Null(Describe(program with {
            Structures = program.Structures.SetItem(IndexOf(program, "Over16"), managed),
        }, "Over16", claims));

        var badOffset = Find(program, "Outer").Fields[1] with { Offset = 5 };
        var forgedOffset = program with {
            Structures = program.Structures.SetItem(IndexOf(program, "Outer"),
                Find(program, "Outer") with {
                    Fields = Find(program, "Outer").Fields.SetItem(1, badOffset),
                }),
        };
        Assert.Null(Describe(forgedOffset, "Outer", claims));

        var badWidth = Find(program, "Inner").Fields[0] with { Size = 8 };
        var forgedWidth = program with {
            Structures = program.Structures.SetItem(IndexOf(program, "Inner"),
                Find(program, "Inner") with {
                    Fields = Find(program, "Inner").Fields.SetItem(0, badWidth),
                }),
        };
        Assert.Null(Describe(forgedWidth, "Outer", claims));

        var badSize = Find(program, "Inner") with { Size = 16 };
        var forgedSize = program with {
            Structures = program.Structures.SetItem(IndexOf(program, "Inner"), badSize),
        };
        Assert.Null(Describe(forgedSize, "Outer", claims));

        Assert.Null(Describe(CreateCyclicProgram(program), "Inner", claims));
        Assert.Null(Describe(CreateTooDeepProgram(), "D0", Claims(
            Enumerable.Range(0, MaximumDepth + 2).Select(static index => $"D{index}").ToArray())));
    }

    private static Dictionary<string, CandidateClaim> Claims(params string[] names) =>
        names.ToDictionary(static name => name,
            static _ => new CandidateClaim(true, "Sequential", Profile, Target), StringComparer.Ordinal);

    private static Dictionary<string, CandidateClaim> WithClaim(
        Dictionary<string, CandidateClaim> claims, string name, CandidateClaim claim)
    {
        claims[name] = claim;
        return claims;
    }

    private static CandidateDescriptor? Describe(BoundProgram program, string nominalName,
        IReadOnlyDictionary<string, CandidateClaim> claims, int target = Target)
    {
        if (!claims.TryGetValue(nominalName, out var claim)
            || !claim.IsBlittable
            || claim.Layout != "Sequential"
            || claim.Profile != Profile
            || claim.Target != target
            || target != Target)
        {
            return null;
        }

        var structure = Find(program, nominalName);
        if (structure.IsPrivate || structure.IsInternal || structure.IsUnique
            || structure.IsTransparent || structure.CleanupMethod is not null)
        {
            return null;
        }

        if (!TryCompute(program, structure, claims, claim, [], 0, out var layout))
        {
            return null;
        }

        return new CandidateDescriptor(structure.Name, claim.Profile, claim.Target, layout);
    }

    private static bool TryCompute(BoundProgram program, BoundStruct structure,
        IReadOnlyDictionary<string, CandidateClaim> claims, CandidateClaim rootClaim,
        HashSet<string> active, int depth, out CandidateLayout layout)
    {
        layout = null!;
        if (depth >= MaximumDepth || !active.Add(structure.Name))
        {
            return false;
        }

        if (!claims.TryGetValue(structure.Name, out var claim)
            || !claim.IsBlittable
            || claim.Layout != "Sequential"
            || claim.Profile != rootClaim.Profile
            || claim.Target != rootClaim.Target
            || structure.IsPrivate || structure.IsInternal || structure.IsUnique
            || structure.IsTransparent || structure.CleanupMethod is not null)
        {
            _ = active.Remove(structure.Name);
            return false;
        }

        try
        {
            var size = 0;
            var alignment = 1;
            var fields = new List<CandidateField>(structure.Fields.Length);
            var nestedLayouts = new List<CandidateLayout>();
            foreach (var field in structure.Fields)
            {
                int fieldSize;
                int fieldAlignment;
                CandidateLayout? nestedLayout = null;
                var typeIndex = (int)field.Type - 100;
                if (typeIndex is >= 0 and < 924)
                {
                    if (typeIndex >= program.Structures.Length)
                    {
                        return false;
                    }
                    var nested = program.Structures[typeIndex];
                    if (!claims.ContainsKey(nested.Name)
                        || !TryCompute(program, nested, claims, rootClaim, active, depth + 1, out nestedLayout))
                    {
                        return false;
                    }
                    fieldSize = nestedLayout.Size;
                    fieldAlignment = nestedLayout.Alignment;
                    nestedLayouts.Add(nestedLayout);
                }
                else
                {
                    (fieldSize, fieldAlignment) = field.Type switch {
                        PrimitiveType.Int32 or PrimitiveType.UInt32 => (4, 4),
                        PrimitiveType.Int64 or PrimitiveType.UInt64 => (8, 8),
                        PrimitiveType.Char16 => (2, 2),
                        _ => (0, 0),
                    };
                    if (fieldSize == 0)
                    {
                        return false;
                    }
                }

                var expectedOffset = Align(size, fieldAlignment);
                if (field.Offset != expectedOffset || field.Size != fieldSize)
                {
                    return false;
                }
                size = expectedOffset + fieldSize;
                alignment = Math.Max(alignment, fieldAlignment);
                fields.Add(new CandidateField(field.Name, TypeName(program, field.Type), field.Offset,
                    field.Size, field.IsPrivate, field.IsInternal, field.IsMutable, field.RequiresUnsafeCaller));
            }

            size = Align(size, alignment);
            if (size is < 1 or > 16 || structure.Size != size
                || (claim.AggregateAlignment is int claimedAlignment && claimedAlignment != alignment))
            {
                return false;
            }

            layout = new CandidateLayout(structure.Name, size, alignment, fields.ToArray(),
                nestedLayouts.ToArray());
            return true;
        }
        finally
        {
            _ = active.Remove(structure.Name);
        }
    }

    private static bool AreCompatible(CandidateDescriptor? producer, CandidateDescriptor? consumer) =>
        producer is not null
        && consumer is not null
        && producer.NominalName == consumer.NominalName
        && producer.Profile == consumer.Profile
        && producer.Target == consumer.Target
        && LayoutsCompatible(producer.Layout, consumer.Layout);

    private static bool LayoutsCompatible(CandidateLayout producer, CandidateLayout consumer) =>
        producer.NominalName == consumer.NominalName
        && producer.Size == consumer.Size
        && producer.Alignment == consumer.Alignment
        && producer.Fields.SequenceEqual(consumer.Fields)
        && producer.NestedLayouts.Length == consumer.NestedLayouts.Length
        && producer.NestedLayouts.Zip(consumer.NestedLayouts)
            .All(static pair => LayoutsCompatible(pair.First, pair.Second));

    private static int Align(int value, int alignment) => (value + alignment - 1) & -alignment;

    private static string TypeName(BoundProgram program, PrimitiveType type)
    {
        var typeIndex = (int)type - 100;
        return typeIndex is >= 0 and < 924 && typeIndex < program.Structures.Length
            ? program.Structures[typeIndex].Name
            : type.ToString();
    }

    private static BoundProgram CreateCyclicProgram(BoundProgram program)
    {
        var index = IndexOf(program, "Inner");
        var structure = Find(program, "Inner");
        return program with {
            Structures = program.Structures.SetItem(index, structure with {
                Fields = [structure.Fields[0] with { Type = (PrimitiveType)(100 + index), Size = structure.Size }],
            }),
        };
    }

    private static BoundProgram CreateTooDeepProgram()
    {
        var source = string.Concat(Enumerable.Range(0, MaximumDepth + 2)
            .Select(static index => $"public struct D{index} {{ public int32 Value; }} "));
        var program = Compile(source);
        var structures = program.Structures;
        for (var index = 0; index < structures.Length - 1; index++)
        {
            structures = structures.SetItem(index, structures[index] with {
                Fields = [new BoundStructField("Value", (PrimitiveType)(101 + index), 0, 1)],
                Size = 1,
            });
        }
        return program with { Structures = structures };
    }

    private static BoundStruct Find(BoundProgram program, string name) =>
        program.Structures.First(structure => structure.Name == name);

    private static int IndexOf(BoundProgram program, string name)
    {
        for (var index = 0; index < program.Structures.Length; index++)
        {
            if (program.Structures[index].Name == name)
            {
                return index;
            }
        }
        return -1;
    }

    private static BoundProgram Compile(string declarations)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(declarations + " int32 Main() { return 0; }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static async Task RunNativeStorageProbeAsync()
    {
        Assert.True(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "The fixed-layout storage fixture requires Windows x64.");

        var root = FindRepositoryRoot();
        var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        Assert.True(File.Exists(vswhere), "Visual Studio discovery is required to build the C storage fixture.");

        var discover = new ProcessStartInfo(vswhere) { WorkingDirectory = root };
        foreach (var argument in new[] {
            "-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
            "-property", "installationPath",
        })
        {
            discover.ArgumentList.Add(argument);
        }
        var installation = (await RunAsync(discover, TimeSpan.FromSeconds(10)).ConfigureAwait(false)).Trim();
        var environment = Path.Combine(installation, "VC", "Auxiliary", "Build", "vcvars64.bat");
        Assert.True(File.Exists(environment), "Visual Studio x64 C build tools are required for the C storage fixture.");

        var directory = Directory.CreateTempSubdirectory("terrum-fixed-layout-").FullName;
        var executable = Path.Combine(directory, "fixed-layout-storage-probe.exe");
        var objectFile = Path.Combine(directory, "fixed-layout-storage-probe.obj");
        var source = Path.Combine(root, "tests", "native", "fixed-layout-storage-probe.c");
        try
        {
            var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")
                ?? throw new InvalidOperationException("ComSpec is unset.")) {
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /O2 /MT /TC /Fo\"{objectFile}\" /Fe\"{executable}\" \"{source}\" /link /incremental:no\"",
                WorkingDirectory = root,
            };
            _ = await RunAsync(build, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            _ = await RunAsync(new ProcessStartInfo(executable), TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tests", "native", "fixed-layout-storage-probe.c")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static async Task<string> RunAsync(ProcessStartInfo start, TimeSpan timeout)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start native storage probe.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(false);
            throw;
        }
        var standardOutput = await output.ConfigureAwait(false);
        var standardError = await error.ConfigureAwait(false);
        Assert.True(process.ExitCode == 0,
            $"Process exited with {process.ExitCode}.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        return standardOutput;
    }

    private sealed record CandidateClaim(bool IsBlittable, string Layout, string Profile, int Target,
        int? AggregateAlignment = null);

    private sealed record CandidateField(string Name, string TypeName, int Offset, int Size,
        bool IsPrivate, bool IsInternal, bool IsMutable, bool RequiresUnsafeCaller);

    private sealed record CandidateLayout(string NominalName, int Size, int Alignment,
        CandidateField[] Fields, CandidateLayout[] NestedLayouts);

    private sealed record CandidateDescriptor(string NominalName, string Profile, int Target, CandidateLayout Layout);
}
