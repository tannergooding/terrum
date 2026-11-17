// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Semantics.Generics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class GenericNominalIdentityTests
{
    [Test]
    public void BindsExactSelectedCoreTaskIdentityAndItsDeclaredVoidDefault()
    {
        var reference = TrRmExecutableCompiler.CreateReference(
            CreateGenericTypeProvider("Terrum.Threading.Tasks", "Task", 3, 2, 0xF200_0000));
        var definition = reference.GenericNominalTypeDefinitions.Single(static item =>
            item.Name == "Task");
        var selected = reference.SelectCoreTaskType(definition.Identity);
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 0; }"), selected);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(definition.Formals.Single().Default!.DecodedType,
            Is.EqualTo(PrimitiveType.Void));
        Assert.That(definition.Formals.Single().Default!.Reference,
            Is.EqualTo(0xF200_0000));
        var program = compilation.Program!;
        var defaultType = program.Functions.Single(static function =>
            function.Name == "ReadDefault").ReturnType;
        Assert.That(BoundGenericNominalTypeTypes.IsType(defaultType), Is.True);
        Assert.That(program.CoreTaskType, Is.EqualTo(new CoreTaskTypeIdentity(definition.Identity)));

        var defaultInstance = program.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(defaultType)];
        Assert.That(defaultInstance.Definition, Is.EqualTo(definition.Identity));
        Assert.That(defaultInstance.Arguments.Single().Type, Is.EqualTo(PrimitiveType.Void));
        Assert.That(BoundGenericNominalTypeContract.IsCoreTaskType(defaultType, program), Is.True);
        var noInputCapture = program.Functions.Single(static function =>
            function.Name == "ReadDefault").DependencyContracts.Single();
        Assert.That(noInputCapture.Relation, Is.EqualTo(BoundDependencyRelation.Captures));
        Assert.That(noInputCapture.Destination, Is.EqualTo(BoundDependencyDestination.Operation));
        Assert.That(noInputCapture.Sources, Is.Empty);

        var integerType = program.Functions.Single(static function =>
            function.Name == "ReadInteger").ReturnType;
        var integerInstance = program.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(integerType)];
        Assert.That(integerInstance.Arguments.Single().Type, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(integerType, Is.Not.EqualTo(defaultType));
        Assert.That(BoundGenericNominalTypeContract.IsCoreTaskType(integerType, program), Is.True);
        var inputCapture = program.Functions.Single(static function =>
            function.Name == "ReadWithTaskInput").DependencyContracts.Single();
        Assert.That(inputCapture.Relation, Is.EqualTo(BoundDependencyRelation.Captures));
        Assert.That(inputCapture.Destination, Is.EqualTo(BoundDependencyDestination.Operation));
        Assert.That(inputCapture.Sources, Is.EqualTo([
            new BoundDependencySource(BoundDependencySourceKind.Formal, 0),
        ]));

        var writableType = program.Functions.Single(static function =>
            function.Name == "ReadWritable").ReturnType;
        var writableInstance = program.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(writableType)];
        var writableArgument = writableInstance.Arguments.Single();
        Assert.That(writableArgument.Type, Is.Not.EqualTo(PrimitiveType.NativeRawPointer));
        Assert.That(writableArgument.TypeSpecReference, Is.EqualTo(0xF301_8C_00));
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitWithImports(program, [
            new NativeImportBinding("ReadDefault", "Core.dll", "ReadDefault"),
            new NativeImportBinding("ReadInteger", "Core.dll", "ReadInteger"),
            new NativeImportBinding("ReadWritable", "Core.dll", "ReadWritable"),
        ], NativeRuntimePolicy.FailFast));

        var sourceUse = Compilation.Create(SyntaxTree.Parse("""
            using Terrum.Threading.Tasks;
            Task NoReturn() { while (true) { } }
            Task<write ptr<void>> NoReturnPointer() { while (true) { } }
            int32 Main() { return 0; }
            """), selected);
        Assert.That(sourceUse.Diagnostics.Any(static diagnostic =>
            diagnostic.Message.Contains("requires an explicitly selected exact Core Task",
                StringComparison.Ordinal)), Is.False);
        Assert.That(sourceUse.Diagnostics.Any(static diagnostic =>
            diagnostic.Message.Contains("requires its declared type argument count",
                StringComparison.Ordinal)), Is.False);
        Assert.That(sourceUse.HasErrors, Is.False,
            string.Join(Environment.NewLine, sourceUse.Diagnostics));
        _ = Assert.Throws<NotSupportedException>(() =>
            NativeEmitter.Emit(sourceUse.Program!,
                NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() =>
            NativeEmitter.Emit(sourceUse.Program!,
                NativeRuntimePolicy.FailFast));
    }

    [Test]
    public void CoreTaskSelectionPreservesPrivateEnumOrigins()
    {
        var taskReference = TrRmExecutableCompiler.CreateReference(
            CreateGenericTypeProvider("Terrum.Threading.Tasks", "Task", 3,
                2, 0xF200_0000));
        var taskDefinition = taskReference.GenericNominalTypeDefinitions
            .Single(static definition => definition.Name == "Task");
        var enumType = BoundEnumTypes.Type(
            taskReference.EnumDefinitions.Length);
        var enumOrigin = new BoundPrivateNominalTypeOrigin(enumType,
            BoundPrivateNominalOriginKind.OwnedDefinition,
            IsExported: true, UsingModuleIdentity: "EnumProvider",
            LocalDeclarationKey: "types.color");
        var enumDefinition = BoundEnumDefinition.CreatePrivate(enumOrigin,
            "Color", PrimitiveType.Int32, [
                new BoundPrivateEnumMemberSnapshot("Red", 1, true,
                    "member.red"),
            ], isPublic: true, isPrimary: true, isFlags: false,
            blittableTargetRequirement: 0);
        var reference = new CompilationReference(taskReference.Structures,
            taskReference.Functions, taskReference.RuntimePolicy,
            taskReference.RequiresSourceGrant,
            taskReference.InvocationProfileId,
            taskReference.NativeFunctionPointerSignatures,
            taskReference.NativePointerTypes,
            taskReference.NativeCoreRegistration,
            enumDefinitions: [.. taskReference.EnumDefinitions,
                enumDefinition],
            checkedReferenceTypes: taskReference.CheckedReferenceTypes,
            interfaces: taskReference.Interfaces,
            genericNominalTypeDefinitions:
                taskReference.GenericNominalTypeDefinitions,
            genericNominalTypeInstances:
                taskReference.GenericNominalTypeInstances,
            coreTaskType: taskReference.CoreTaskType,
            nativeFixedArrayTypes: taskReference.NativeFixedArrayTypes,
            coreExceptionIdentitySelections:
                taskReference.CoreExceptionIdentitySelections,
            layoutCoreRegistration: taskReference.LayoutCoreRegistration,
            privateNominalTypeOrigins: [
                .. taskReference.PrivateNominalTypeOrigins, enumOrigin,
            ],
            classes: taskReference.Classes,
            privateClassReferences:
                taskReference.PrivateClassReferences);

        var selected = reference.SelectCoreTaskType(
            taskDefinition.Identity);
        Assert.That(selected.CoreTaskType,
            Is.EqualTo(new CoreTaskTypeIdentity(taskDefinition.Identity)));
        Assert.That(selected.EnumDefinitions.Single().Identity,
            Is.SameAs(enumDefinition.Identity));
        Assert.That(selected.PrivateNominalTypeOrigins,
            Is.EqualTo([enumOrigin]));

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public Color ReadColor() { return Color.Red; }"), selected);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void SameSpelledUnselectedTaskDefinitionHasNoCoreTaskAuthority()
    {
        var reference = TrRmExecutableCompiler.CreateReference(
            CreateGenericTypeProvider("Terrum.Threading.Tasks", "Task", 3, 2, 0xF200_0000));
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Terrum.Threading.Tasks;
            Task<int32> ReadTask() { while (true) { } }
            int32 Main() { return 0; }
            """), reference);

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
            && diagnostic.Message.Contains("exact Core Task definition",
                StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void CoreTaskSelectionRejectsProviderAndModuleIdentityMismatches()
    {
        var reference = TrRmExecutableCompiler.CreateReference(
            CreateGenericTypeProvider("Terrum.Threading.Tasks", "Task", 3, 2, 0xF200_0000));
        var identity = reference.GenericNominalTypeDefinitions.Single(static item =>
            item.Name == "Task").Identity;

        _ = Assert.Throws<ArgumentException>(() => reference.SelectCoreTaskType(
            identity with { ProviderIdentity = "Other.Provider" }));
        _ = Assert.Throws<ArgumentException>(() => reference.SelectCoreTaskType(
            identity with { ModuleIdentity = "Other.Module" }));
    }

    [Test]
    public void CoreTaskSelectionRejectsNonClassNominalDefinitions()
    {
        var reference = TrRmExecutableCompiler.CreateReference(
            CreateGenericTypeProvider("Terrum.Threading.Tasks", "Task", 3, 2,
                0xF200_0000, nominalKind: 1));
        var identity = reference.GenericNominalTypeDefinitions.Single(static item =>
            item.Name == "Task").Identity;

        _ = Assert.Throws<ArgumentException>(() => reference.SelectCoreTaskType(identity));
    }

    [Test]
    public void ExplicitEmptyOperationCaptureNarrowsTheTaskDefault()
    {
        var sourceReference = TrRmExecutableCompiler.CreateReference(
            CreateGenericTypeProvider("Terrum.Threading.Tasks", "Task", 3, 2,
                0xF200_0000));
        var taskDefinition = sourceReference.GenericNominalTypeDefinitions.Single(
            static item => item.Name == "Task");
        var selected = sourceReference.SelectCoreTaskType(taskDefinition.Identity);
        var function = selected.Functions.Single(static item =>
            item.Name == "ReadWithTaskInput");
        var functionIndex = selected.Functions.IndexOf(function);
        var narrowedFunction = function with
        {
            DependencyContracts = [
                new BoundDependencyContract(BoundDependencyRelation.Captures,
                    BoundDependencyDestination.Operation, null, []),
            ],
        };
        var narrowedFunctions = selected.Functions.SetItem(functionIndex,
            narrowedFunction);
        var narrowedReference = new CompilationReference(selected.Structures,
            narrowedFunctions, selected.RuntimePolicy,
            nativeFunctionPointerSignatures: selected.NativeFunctionPointerSignatures,
            nativePointerTypes: selected.NativePointerTypes,
            nativeCoreRegistration: selected.NativeCoreRegistration,
            enumDefinitions: selected.EnumDefinitions,
            checkedReferenceTypes: selected.CheckedReferenceTypes,
            interfaces: selected.Interfaces,
            genericNominalTypeDefinitions: selected.GenericNominalTypeDefinitions,
            genericNominalTypeInstances: selected.GenericNominalTypeInstances,
            coreTaskType: selected.CoreTaskType);
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 0; }"), narrowedReference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var capture = compilation.Program!.Functions.Single(static item =>
            item.Name == "ReadWithTaskInput").DependencyContracts.Single();
        Assert.That(capture.Destination,
            Is.EqualTo(BoundDependencyDestination.Operation));
        Assert.That(capture.Sources, Is.Empty);
    }

    [Test]
    public void RejectsUnsupportedCompleteSignatureTypeSpecsInsteadOfErasingThem()
    {
        var provider = CreateGenericTypeProvider("Terrum.Threading.Tasks",
            "Task", 3, 2, 0xF200_0000, taskInputTypeSpecRef: 0xF300_8B08);

        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.CreateGenericNominalTypeReference(provider));
    }

    [Test]
    public void BindsUnrelatedGenericNominalsThroughTheSameCarrier()
    {
        var reference = TrRmExecutableCompiler.CreateReference(
            CreateGenericTypeProvider("Example", "Box", 3, 2, 0));
        var definition = reference.GenericNominalTypeDefinitions.Single(static item =>
            item.Name == "Box");
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 0; }"), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var returnType = compilation.Program!.Functions.Single(static function =>
            function.Name == "ReadInteger").ReturnType;
        Assert.That(BoundGenericNominalTypeTypes.IsType(returnType), Is.True);
        var instance = compilation.Program.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(returnType)];
        Assert.That(instance.Definition, Is.EqualTo(definition.Identity));
        Assert.That(instance.Arguments.Single().Type, Is.EqualTo(PrimitiveType.Int32));
    }

    [Test]
    public void GenericMethodDefaultsRetainImportedNominalIdentity()
    {
        var reference = TrRmExecutableCompiler.CreateReference(
            CreateGenericTypeProvider("Example", "Box", 3, 2, 0));
        var definition = reference.GenericNominalTypeDefinitions.Single(
            static item => item.Name == "Box");
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            public int32 Accumulate<T = int32, U = Box<T>>(int32 value) {
                return value;
            }
            int32 Main() { return Accumulate<int32>(42); }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var genericMethod = program.GenericMethodDefinitions.Single();
        var importedTerm = genericMethod.Formals[1].TypeDefault!.Term;
        Assert.That(importedTerm.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.ImportedGeneric));
        Assert.That(importedTerm.NominalTypeIdentity,
            Is.EqualTo(definition.Identity));
        Assert.That(importedTerm.TypeArguments.Single().Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Formal));
        Assert.That(importedTerm.TypeArguments.Single().FormalOrdinal,
            Is.Zero);

        var malformedTerm = importedTerm with {
            NominalTypeIdentity = definition.Identity with {
                ProviderIdentity = "different-provider",
            },
        };
        var malformedDefinition = genericMethod with {
            Formals = [
                .. genericMethod.Formals.Select((formal, ordinal) =>
                    ordinal == 1
                        ? formal with {
                            TypeDefault = formal.TypeDefault! with {
                                Term = malformedTerm,
                            },
                        }
                        : formal),
            ],
        };
        var malformedProgram = program with {
            GenericMethodDefinitions = [malformedDefinition],
        };
        Assert.That(SemanticVerifier.TryVerifySource(malformedProgram,
            out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void TypeIdentityPreservesImportedGenericSubstitutionThroughCall()
    {
        var reference = TrRmExecutableCompiler.CreateReference(
            CreateGenericTypeProvider("Example", "Box", 3, 2, 0));
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;

            bool Query<T>(Box<T> value) {
                return typeof(value) == typeof(Box<T>);
            }

            int32 Main() {
                return Query<int32>(ReadInteger()) ? 0 : 1;
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException("The generic program did not bind.");
        var queryDefinition = program.GenericMethodDefinitions.Single(
            static definition => definition.Name.Contains(
                "Query", StringComparison.Ordinal));
        var specialization = program.Functions.Single(function =>
            function.GenericMethodInstantiation?.DefinitionOrdinal
                == queryDefinition.DefinitionOrdinal);
        Assert.That(specialization.GenericMethodInstantiation!
                .TypeArguments,
            Is.EqualTo([PrimitiveType.Int32]));
        var typeIdentityQueries = new List<BoundTypeIdentityQuery>();
        Assert.That(BoundTreeTraversal.VisitExpressions(
            specialization.Body, expression =>
            {
                if (expression is BoundTypeIdentityQuery query)
                {
                    typeIdentityQueries.Add(query);
                }
            }), Is.True);
        Assert.That(typeIdentityQueries, Has.Count.EqualTo(2));
        Assert.That(typeIdentityQueries.Select(static query =>
                query.Subject.Type),
            Is.All.EqualTo(specialization.Variables[0].Type));

        var proofFunction = queryDefinition.ProofProgram.Functions[
            queryDefinition.FunctionIndex];
        var proofQueries = new List<BoundTypeIdentityQuery>();
        Assert.That(BoundTreeTraversal.VisitExpressions(proofFunction.Body,
            expression =>
            {
                if (expression is BoundTypeIdentityQuery query)
                {
                    proofQueries.Add(query);
                }
            }), Is.True);
        Assert.That(proofQueries, Has.Count.EqualTo(2));
        Assert.That(proofQueries.Select(static query =>
                query.Subject.TypeIdentityTerm),
            Has.All.Not.Null);

        var changedQuery = false;
        var malformedSpecialization
            = BoundTreeTraversal.RewriteExpressions(specialization,
                expression =>
                {
                    if (!changedQuery
                        && expression is BoundTypeIdentityQuery {
                            Subject.Kind: BoundQuerySubjectKind.Type,
                        } query)
                    {
                        changedQuery = true;
                        return query with {
                            Subject = query.Subject with {
                                Type = PrimitiveType.Int32,
                                TypeIdentityTerm = null,
                            },
                        };
                    }
                    return expression;
                });
        Assert.That(changedQuery, Is.True);
        var specializationIndex = program.Functions.IndexOf(specialization);
        var malformedProgram = program with {
            Functions = program.Functions.SetItem(specializationIndex,
                malformedSpecialization),
        };
        Assert.That(SemanticVerifier.TryVerifySource(malformedProgram,
            out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void TypeIdentityPreservesImportedConstantFormalSubstitution()
    {
        var reference = TrRmExecutableCompiler.CreateGenericNominalTypeReference(
            CreateGenericTypeProvider("Example", "Box", 3, 2, 0,
                includeConstantFormal: true, constantValue: 42));
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;

            struct Cell<const int32 Seed> { public int32 Value; }

            bool Query<const int32 N, T>(Box<T, N> value) {
                return typeof(value) == typeof(Box<T, N>);
            }

            int32 Main() {
                return Query<42, int32>(ReadConstants()) ? 0 : 1;
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException("The generic program did not bind.");
        var definition = program.GenericMethodDefinitions.Single(
            static item => item.Name.Contains("Query", StringComparison.Ordinal));
        var specialization = program.Functions.Single(function =>
            function.GenericMethodInstantiation?.DefinitionOrdinal
                == definition.DefinitionOrdinal);
        Assert.That(specialization.GenericMethodInstantiation!
                .ConstantArguments.Single().Bits.Low,
            Is.EqualTo(42));

        var proofProgram = definition.ProofProgram;
        var proofNominalInstance = proofProgram.GenericNominalTypeInstances
            .First(instance => instance.Arguments.Any(
                static argument => argument.HasConstantFormalIdentity));
        var proofConstant = proofNominalInstance.Arguments.Single(
            static argument => argument.HasConstantFormalIdentity);
        Assert.That(proofConstant.ConstantFormalOwnerKind,
            Is.EqualTo(BoundGenericTypeFormalOwnerKind.GenericMethod));
        var wrongOwnerInstance = proofNominalInstance with {
            Arguments = [
                .. proofNominalInstance.Arguments.Select(argument =>
                    argument.HasConstantFormalIdentity
                        ? argument with {
                            ConstantFormalOwnerKind =
                                BoundGenericTypeFormalOwnerKind
                                    .SourceGenericType,
                        }
                        : argument),
            ],
        };
        var wrongOwnerProofProgram = proofProgram with {
            GenericNominalTypeInstances = [
                .. proofProgram.GenericNominalTypeInstances.Select(instance =>
                    ReferenceEquals(instance, proofNominalInstance)
                        ? wrongOwnerInstance : instance),
            ],
        };
        var malformedProgram = program with {
            GenericMethodDefinitions = [
                .. program.GenericMethodDefinitions.Select(candidate =>
                    candidate.DefinitionOrdinal == definition.DefinitionOrdinal
                        ? candidate with {
                            ProofProgram = wrongOwnerProofProgram,
                        }
                        : candidate),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(malformedProgram,
            out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void TypeIdentityPreservesSourceGenericSubstitutionThroughCall()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Cell<T, const uint64 Marker> {
                public T Value;
                public Cell(T value) { this.Value = value; }
            }

            bool Query<T>(T value) {
                return typeof(value) == typeof(T);
            }

            int32 Main() {
                return Query<Cell<int32, 42>>(
                    new Cell<int32, 42>(42)) ? 0 : 1;
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The source-generic TypeIdentity program did not bind.");
        var definition = program.GenericMethodDefinitions.Single(
            static item => item.Name.Contains("Query",
                StringComparison.Ordinal));
        var specialization = program.Functions.Single(function =>
            function.GenericMethodInstantiation?.DefinitionOrdinal
                == definition.DefinitionOrdinal);
        var sourceGenericActual = specialization.GenericMethodInstantiation!
            .TypeArguments.Single();
        var sourceGenericInstance = program.SourceGenericTypeInstances
            .Single(instance => (PrimitiveType)(100
                + instance.StructureIndex) == sourceGenericActual);
        Assert.That(sourceGenericInstance.TypeArguments,
            Is.EqualTo([PrimitiveType.Int32]));
        Assert.That(sourceGenericInstance.ConstantArguments.Single().Bits.Low,
            Is.EqualTo(42));
        var queries = new List<BoundTypeIdentityQuery>();
        Assert.That(BoundTreeTraversal.VisitExpressions(specialization.Body,
            expression =>
            {
                if (expression is BoundTypeIdentityQuery query)
                {
                    queries.Add(query);
                }
            }), Is.True);
        Assert.That(queries, Has.Count.EqualTo(2));
    }

    [Test]
    public void PreservesSourceGenericOwnerConstantIdentityInTypeArgument()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Cell<const int32 Marker> {
                public int32 Value;
            }

            struct Wrapper<const int32 Marker> {
                public Cell<Marker> Value;
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The source-generic owner forwarding program did not bind.");
        var cell = program.SourceGenericTypeDefinitions.Single(
            static definition => definition.Name == "Cell");
        var wrapper = program.SourceGenericTypeDefinitions.Single(
            static definition => definition.Name == "Wrapper");
        var instance = program.SourceGenericTypeInstances.Single(candidate =>
            candidate.DefinitionIndex == cell.DefinitionIndex);
        var marker = instance.ConstantArguments.Single();

        Assert.That(marker.Kind,
            Is.EqualTo(BoundGenericMethodConstantArgumentKind.Formal));
        Assert.That(marker.CallerFormalOwnerKind,
            Is.EqualTo(BoundGenericTypeFormalOwnerKind.SourceGenericType));
        Assert.That(marker.CallerDefinitionOrdinal,
            Is.EqualTo(wrapper.DefinitionIndex));
        Assert.That(marker.CallerFormalOrdinal, Is.EqualTo(-1));
        Assert.That(marker.CallerConstantFormalOrdinal, Is.Zero);
        SemanticVerifier.VerifyLibrary(program);
    }

    [Test]
    public void PreservesSourceOwnerFormalInImportedNominalMethodParameter()
    {
        var provider = CreateGenericTypeProvider("Example", "Box", 3, 2,
            0xF200_0008, includeConstantFormal: true,
            genericConstantMethodsDraft: true,
            includeConstantForwardingDefault: true);
        var reference = TrRmExecutableCompiler
            .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                provider);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Example;

            struct Holder<const int32 Seed> {
                public Box<default, Seed> Value;
                public int32 Read<T>(Box<default, Seed> value, T witness) {
                    return Seed;
                }
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The imported nominal source-owner program did not bind.");
        var holder = program.SourceGenericTypeDefinitions.Single(
            static definition => definition.Name == "Holder");
        var method = program.GenericMethodDefinitions.Single(
            static definition => definition.Name.Contains("Read",
                StringComparison.Ordinal));
        var proofProgram = method.ProofProgram;
        var box = reference.GenericNominalTypeDefinitions.Single(
            static definition => definition.Name == "Box");
        var instance = proofProgram.GenericNominalTypeInstances.Single(
            candidate => candidate.Definition == box.Identity
                && candidate.Arguments.Any(
                    static argument => argument.HasConstantFormalIdentity));
        Assert.That(instance.Arguments[0].Type,
            Is.EqualTo(PrimitiveType.Int32));
        var seed = instance.Arguments[1];

        Assert.That(seed.HasConstantFormalIdentity, Is.True);
        Assert.That(seed.ConstantFormalOwnerKind,
            Is.EqualTo(BoundGenericTypeFormalOwnerKind.SourceGenericType));
        Assert.That(seed.ConstantFormalDefinitionOrdinal,
            Is.EqualTo(holder.DefinitionIndex));
        Assert.That(seed.ConstantFormalOrdinal, Is.Zero);
        Assert.That(seed.ConstantFormalDomain,
            Is.EqualTo(new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.Concrete,
                PrimitiveType.Int32, -1, -1, [], false, false, false,
                false)));
        var forwardedSeed = instance.Arguments[2];
        Assert.That(forwardedSeed.HasConstantFormalIdentity, Is.True);
        Assert.That(forwardedSeed.ConstantFormalOwnerKind,
            Is.EqualTo(seed.ConstantFormalOwnerKind));
        Assert.That(forwardedSeed.ConstantFormalDefinitionOrdinal,
            Is.EqualTo(seed.ConstantFormalDefinitionOrdinal));
        Assert.That(forwardedSeed.ConstantFormalOrdinal,
            Is.EqualTo(seed.ConstantFormalOrdinal));
        Assert.That(forwardedSeed.ConstantFormalDomain,
            Is.EqualTo(seed.ConstantFormalDomain));
        SemanticVerifier.VerifyLibrary(program);

        var wrongDomain = forwardedSeed with {
            ConstantFormalDomain = forwardedSeed.ConstantFormalDomain! with {
                TypeArguments = [
                    new BoundGenericMethodTypeTerm(
                        BoundGenericMethodTypeTermKind.Concrete,
                        PrimitiveType.Int32, -1, -1, [], false, false,
                        false, false),
                ],
            },
        };
        var malformedProofProgram = proofProgram with {
            GenericNominalTypeInstances = [
                .. proofProgram.GenericNominalTypeInstances.Select(candidate =>
                    ReferenceEquals(candidate, instance)
                        ? candidate with {
                            Arguments = [
                                instance.Arguments[0],
                                instance.Arguments[1],
                                wrongDomain,
                            ],
                        }
                        : candidate),
            ],
        };
        var malformedProgram = program with {
            GenericMethodDefinitions = [
                .. program.GenericMethodDefinitions.Select(candidate =>
                    candidate.DefinitionOrdinal == method.DefinitionOrdinal
                        ? candidate with {
                            ProofProgram = malformedProofProgram,
                        }
                        : candidate),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(malformedProgram,
            out _, out _, out _, out _), Is.False);

        var wrongDomain = seed with {
            ConstantFormalDomain = new BoundGenericMethodTypeTerm(
                BoundGenericMethodTypeTermKind.Concrete,
                PrimitiveType.Int64, -1, -1, [], false, false, false,
                false),
        };
        var malformedProofProgram = proofProgram with {
            GenericNominalTypeInstances = [
                .. proofProgram.GenericNominalTypeInstances.Select(candidate =>
                    ReferenceEquals(candidate, instance)
                        ? candidate with {
                            Arguments = [wrongDomain],
                        }
                        : candidate),
            ],
        };
        var malformedProgram = program with {
            GenericMethodDefinitions = [
                .. program.GenericMethodDefinitions.Select(candidate =>
                    candidate.DefinitionOrdinal == method.DefinitionOrdinal
                        ? candidate with {
                            ProofProgram = malformedProofProgram,
                        }
                        : candidate),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(malformedProgram,
            out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void PreservesSourceOwnerFormalThroughImportedWrapperDomain()
    {
        var provider = CreateGenericTransparentWrapperProvider(
            genericWrapper: true, includeFeature11: false,
            includeForwardingDefault: true);
        var reference = TrRmExecutableCompiler
            .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                provider);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Example;

            struct Holder<T, const Wrapper<T> Seed> {
                public int32 Read<U>(Box<T, Seed> value,
                    Box<T, Seed, default> explicitValue, U witness) {
                    return 0;
                }
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The source-owner wrapper-domain program did not bind.");
        var holder = program.SourceGenericTypeDefinitions.Single(
            static definition => definition.Name == "Holder");
        var method = program.GenericMethodDefinitions.Single(
            static definition => definition.Name.Contains("Read",
                StringComparison.Ordinal));
        var proofProgram = method.ProofProgram;
        var box = reference.GenericNominalTypeDefinitions.Single(
            static definition => definition.Name == "Box");
        var instance = proofProgram.GenericNominalTypeInstances.Single(
            candidate => candidate.Definition == box.Identity
                && candidate.Arguments.Any(
                    static argument => argument.HasConstantFormalIdentity));
        var seed = instance.Arguments[1];
        var forwardedSeed = instance.Arguments[2];

        Assert.That(seed.HasConstantFormalIdentity, Is.True);
        Assert.That(seed.ConstantFormalOwnerKind,
            Is.EqualTo(BoundGenericTypeFormalOwnerKind.SourceGenericType));
        Assert.That(seed.ConstantFormalDefinitionOrdinal,
            Is.EqualTo(holder.DefinitionIndex));
        Assert.That(seed.ConstantFormalOrdinal, Is.Zero);
        Assert.That(forwardedSeed.HasConstantFormalIdentity, Is.True);
        Assert.That(forwardedSeed.ConstantFormalOwnerKind,
            Is.EqualTo(seed.ConstantFormalOwnerKind));
        Assert.That(forwardedSeed.ConstantFormalDefinitionOrdinal,
            Is.EqualTo(seed.ConstantFormalDefinitionOrdinal));
        Assert.That(forwardedSeed.ConstantFormalOrdinal,
            Is.EqualTo(seed.ConstantFormalOrdinal));
        Assert.That(forwardedSeed.ConstantFormalDomain,
            Is.EqualTo(seed.ConstantFormalDomain));
        SemanticVerifier.VerifyLibrary(program);

        var concreteWrapperDomain = seed.ConstantFormalDomain! with {
            TypeArguments = [
                new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.Concrete,
                    PrimitiveType.Int32, -1, -1, [], false, false,
                    false, false),
            ],
        };
        var wrongDomain = forwardedSeed with {
            ConstantFormalDomain = concreteWrapperDomain,
        };
        var malformedProofProgram = proofProgram with {
            GenericNominalTypeInstances = [
                .. proofProgram.GenericNominalTypeInstances.Select(candidate =>
                    ReferenceEquals(candidate, instance)
                        ? candidate with {
                            Arguments = [
                                .. instance.Arguments.Select((argument,
                                    ordinal) => ordinal == 2
                                    ? wrongDomain : argument),
                            ],
                        }
                        : candidate),
            ],
        };
        var malformedProgram = program with {
            GenericMethodDefinitions = [
                .. program.GenericMethodDefinitions.Select(candidate =>
                    candidate.DefinitionOrdinal == method.DefinitionOrdinal
                        ? candidate with {
                            ProofProgram = malformedProofProgram,
                        }
                        : candidate),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(malformedProgram,
            out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void PreservesSourceGenericConstantFormalForwardingThroughOpenProof()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Cell<T, const int32 Marker> {
                public T Value;
                public Cell(T value) { this.Value = value; }
            }

            bool Inner<U>(U value) {
                return typeof(value) == typeof(U);
            }

            bool Outer<const int32 Marker, T>(Cell<T, Marker> value) {
                return Inner<Cell<T, Marker>>(value)
                    && typeof(value) == typeof(Cell<T, Marker>);
            }

            int32 Main() {
                return Outer<42, int32>(
                    new Cell<int32, 42>(42)) ? 0 : 1;
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The source generic forwarding program did not bind.");
        var inner = program.GenericMethodDefinitions.Single(
            static definition => definition.Name.Contains("Inner",
                StringComparison.Ordinal));
        var outer = program.GenericMethodDefinitions.Single(
            static definition => definition.Name.Contains("Outer",
                StringComparison.Ordinal));
        var outerProof = outer.ProofProgram.Functions[
            outer.FunctionIndex];
        var proofCalls = new List<BoundGenericMethodInstantiation>();
        Assert.That(BoundTreeTraversal.VisitExpressions(outerProof.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation call,
                    })
                {
                    proofCalls.Add(call);
                }
            }), Is.True);
        Assert.That(proofCalls, Has.Count.EqualTo(1));
        Assert.That(proofCalls[0].DefinitionOrdinal,
            Is.EqualTo(inner.DefinitionOrdinal));
        var openTypeActual = proofCalls[0].TypeArguments.Single();
        var openInstance = outer.ProofProgram.SourceGenericTypeInstances
            .Single(instance => (PrimitiveType)(100
                + instance.StructureIndex) == openTypeActual);
        var forwardedConstant = openInstance.ConstantArguments.Single();
        Assert.That(forwardedConstant.Kind,
            Is.EqualTo(BoundGenericMethodConstantArgumentKind.Formal));
        Assert.That(forwardedConstant.CallerDefinitionOrdinal,
            Is.EqualTo(outer.DefinitionOrdinal));
        Assert.That(forwardedConstant.CallerConstantFormalOrdinal,
            Is.Zero);
        Assert.That(NativeJit.Execute(program,
            NativeRuntimePolicy.FailFast), Is.EqualTo(0));
    }

    [Test]
    public void TypeIdentitySubstitutesImportedNominalActualThroughOpenProofCaller()
    {
        var reference
            = TrRmExecutableCompiler.CreateGenericNominalTypeReference(
            CreateGenericTypeProvider("Example", "Box", 3, 2, 0,
                includeConstantFormal: true, constantValue: 42));
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;

            bool Inner<U>(U value) {
                return typeof(value) == typeof(U);
            }

            bool Outer<const int32 N, T>(Box<T, N> value) {
                return Inner<Box<T, N>>(value)
                    && typeof(value) == typeof(Box<T, N>);
            }

            int32 Main() {
                return Outer<42, int32>(ReadConstants()) ? 0 : 1;
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The nested generic TypeIdentity program did not bind.");
        var inner = program.GenericMethodDefinitions.Single(
            static definition => definition.Name.Contains("Inner",
                StringComparison.Ordinal));
        var outer = program.GenericMethodDefinitions.Single(
            static definition => definition.Name.Contains("Outer",
                StringComparison.Ordinal));
        var outerProof = outer.ProofProgram.Functions[
            outer.FunctionIndex];
        var proofCalls = new List<BoundGenericMethodInstantiation>();
        Assert.That(BoundTreeTraversal.VisitExpressions(outerProof.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation call,
                    })
                {
                    proofCalls.Add(call);
                }
            }), Is.True);
        Assert.That(proofCalls, Has.Count.EqualTo(1));
        Assert.That(proofCalls[0].DefinitionOrdinal,
            Is.EqualTo(inner.DefinitionOrdinal));
        var openTypeActual = proofCalls[0].TypeArguments.Single();
        var openNominalActual = outer.ProofProgram
            .GenericNominalTypeInstances[
                BoundGenericNominalTypeTypes.Index(openTypeActual)];
        Assert.That(openNominalActual.Definition,
            Is.EqualTo(reference.GenericNominalTypeDefinitions.Single(
                static definition => definition.Name == "Box").Identity));
        Assert.That(openNominalActual.Arguments[0].Type,
            Is.EqualTo(outerProof.GenericMethodProofTypeParameters.Single()));
        Assert.That(openNominalActual.Arguments[1]
                .HasConstantFormalIdentity,
            Is.True);
        Assert.That(openNominalActual.Arguments[1]
                .ConstantFormalDefinitionOrdinal,
            Is.EqualTo(outer.DefinitionOrdinal));
        Assert.That(openNominalActual.Arguments[1]
                .ConstantFormalOrdinal,
            Is.Zero);

        var innerSpecialization = program.Functions.Single(function =>
        {
            if (function.GenericMethodInstantiation?.DefinitionOrdinal
                != inner.DefinitionOrdinal
                || function.GenericMethodInstantiation.TypeArguments.Length
                    != 1)
            {
                return false;
            }

            var actualType = function.GenericMethodInstantiation
                .TypeArguments[0];
            return BoundGenericNominalTypeTypes.IsType(actualType)
                && program.GenericNominalTypeInstances[
                    BoundGenericNominalTypeTypes.Index(actualType)]
                    .Arguments[1] is {
                        HasDecodedConstant: true,
                        ConstantLow: 42,
                    };
        });
        var importedActual = innerSpecialization
            .GenericMethodInstantiation!.TypeArguments.Single();
        Assert.That(BoundGenericNominalTypeTypes.IsType(importedActual),
            Is.True);
        var importedInstance = program.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(importedActual)];
        Assert.That(importedInstance.Definition,
            Is.EqualTo(reference.GenericNominalTypeDefinitions.Single(
                static definition => definition.Name == "Box").Identity));
        Assert.That(importedInstance.Arguments[0].Type,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(importedInstance.Arguments[1].ConstantLow,
            Is.EqualTo(42));
    }

    [Test]
    public void PreservesOrderedTypeAndConstantSubstitutions()
    {
        var reference = TrRmExecutableCompiler.CreateGenericNominalTypeReference(
            CreateGenericTypeProvider("Example", "Pair", 3, 2, 0,
                includeConstantFormal: true));
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 0; }"), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var returnType = compilation.Program!.Functions.Single(static function =>
            function.Name == "ReadConstants").ReturnType;
        var instance = compilation.Program.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(returnType)];
        Assert.That(instance.Arguments.Length, Is.EqualTo(2));
        Assert.That(instance.Arguments[0].Kind, Is.EqualTo(BoundGenericArgumentKind.Type));
        Assert.That(instance.Arguments[0].Type, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(instance.Arguments[1].Kind, Is.EqualTo(BoundGenericArgumentKind.Constant));
        Assert.That(instance.Arguments[1].ConstantProviderIdentity, Is.EqualTo("Provider"));
        Assert.That(instance.Arguments[1].ConstantSourceRow, Is.EqualTo(1));
    }

    [TestCase(0)]
    [TestCase(42)]
    [TestCase(int.MinValue)]
    [TestCase(int.MaxValue)]
    public void InfersMethodConstantFromPinnedImportedNominalTypeDefault(
        int constantValue)
    {
        var reference = TrRmExecutableCompiler.CreateGenericNominalTypeReference(
            CreateGenericTypeProvider("Example", "Box", 3, 2, 0,
                includeConstantFormal: true,
                constantValue: constantValue));
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            public int32 Read<const int32 N = 99,
                T = Box<int32, N>>(T first, T second) {
                return N;
            }
            int32 Main() {
                return Read<_, default>(
                    ReadConstants(), ReadConstants());
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single(
            static item => item.Name == "Read");
        var typeDefault = definition.Formals.Single().TypeDefault!;
        Assert.That(typeDefault.ConstantFormalDependencies,
            Is.EqualTo([0]));
        Assert.That(typeDefault.Term.ConstantArguments.Single(),
            Is.EqualTo(new BoundGenericMethodTypeConstantArgument(
                BoundGenericMethodTypeConstantArgumentKind.Formal, 0,
                PrimitiveType.Void, PrimitiveType.Void, default) {
                FormalDefinitionOrdinal = definition.DefinitionOrdinal,
            }));

        var main = program.Functions.Single(static function =>
            function.Name == "Main");
        var calls = new List<BoundGenericMethodInstantiation>();
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    calls.Add(instantiation);
                }
            }), Is.True);
        var call = calls.Single();
        var expectedBits = new BoundConstant(constantValue,
            PrimitiveType.Int32, default).Bits;
        Assert.That(call.ConstantArguments.Single().Bits,
            Is.EqualTo(expectedBits));
        var resultType = program.Functions.Single(static function =>
            function.Name == "ReadConstants").ReturnType;
        Assert.That(call.TypeArguments.Single(), Is.EqualTo(resultType));

        var nominalInstance = program.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(resultType)];
        Assert.That(nominalInstance.Arguments[1].HasDecodedConstant,
            Is.True);
        Assert.That(nominalInstance.Arguments[1].ConstantLow,
            Is.EqualTo(expectedBits.Low));
    }

    [Test]
    public void ForwardsConstantFormalsThroughImportedNominalTypeDefaults()
    {
        var reference = TrRmExecutableCompiler.CreateGenericNominalTypeReference(
            CreateGenericTypeProvider("Example", "Box", 3, 2, 0,
                includeConstantFormal: true, constantValue: 42));
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            public int32 Select<const int32 N,
                T = Box<int32, N>>(T value) {
                return N;
            }
            public int32 Forward<T, const int32 Value>(
                T ignored, Box<int32, Value> value) {
                return Select<Value, default>(value);
            }
            Box<int32, 42> ReadBox() { while (true) { } }
            int32 Main() {
                return Forward<int32, 42>(int32(0), ReadBox());
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var forward = program.GenericMethodDefinitions.Single(
            static item => item.Name == "Forward");
        var proofBody = forward.ProofProgram.Functions[
            forward.FunctionIndex];
        BoundGenericMethodInstantiation? forwardedCall = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(proofBody.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    forwardedCall = instantiation;
                }
            }), Is.True);
        Assert.That(forwardedCall, Is.Not.Null);
        Assert.That(forwardedCall!.ConstantArguments.Single().Kind,
            Is.EqualTo(BoundGenericMethodConstantArgumentKind.Formal));
        Assert.That(forwardedCall.ConstantArguments.Single()
            .CallerFormalOrdinal, Is.EqualTo(1));
        Assert.That(forwardedCall.ConstantArguments.Single()
            .CallerConstantFormalOrdinal, Is.Zero);
        Assert.That(forwardedCall.ConstantArguments.Single()
            .CallerDefinitionOrdinal, Is.EqualTo(forward.DefinitionOrdinal));
        Assert.That(forwardedCall.TypeArguments.Single(),
            Is.EqualTo(proofBody.Variables.Single(static variable =>
                variable.Name == "value").Type));

        var main = program.Functions.Single(static function =>
            function.Name == "Main");
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    Assert.That(instantiation.ConstantArguments.Single()
                        .Bits.Low,
                        Is.EqualTo(new BoundConstant(42,
                            PrimitiveType.Int32, default).Bits.Low));
                }
            }), Is.True);
    }

    [Test]
    public void ForwardsConstantsThroughImportedNominalDefaultsWithOpenTypeFormals()
    {
        var reference = TrRmExecutableCompiler.CreateGenericNominalTypeReference(
            CreateGenericTypeProvider("Example", "Box", 3, 2, 0,
                includeConstantFormal: true, constantValue: 42));
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            public int32 Select<T, const int32 N,
                U = Box<T, N>>(T ignored, U value) {
                return N;
            }
            public int32 Forward<T, const int32 Value>(
                T ignored, Box<T, Value> value) {
                return Select<T, Value, default>(ignored, value);
            }
            Box<int32, 42> ReadBox() { while (true) { } }
            int32 Main() {
                return Forward<int32, 42>(int32(0), ReadBox());
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var forward = program.GenericMethodDefinitions.Single(
            static item => item.Name == "Forward");
        var proofBody = forward.ProofProgram.Functions[
            forward.FunctionIndex];
        BoundGenericMethodInstantiation? forwardedCall = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(proofBody.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    forwardedCall = instantiation;
                }
            }), Is.True);

        Assert.That(forwardedCall, Is.Not.Null);
        Assert.That(forwardedCall!.ConstantArguments.Single().Kind,
            Is.EqualTo(BoundGenericMethodConstantArgumentKind.Formal));
        Assert.That(forwardedCall.ConstantArguments.Single()
            .CallerDefinitionOrdinal, Is.EqualTo(forward.DefinitionOrdinal));
        Assert.That(forwardedCall.ConstantArguments.Single()
            .CallerFormalOrdinal, Is.EqualTo(1));
        Assert.That(forwardedCall.ConstantArguments.Single()
            .CallerConstantFormalOrdinal, Is.Zero);
        Assert.That(forwardedCall.TypeArguments[0],
            Is.EqualTo(proofBody.Variables.Single(static variable =>
                variable.Name == "ignored").Type));
        Assert.That(forwardedCall.TypeArguments[1],
            Is.EqualTo(proofBody.Variables.Single(static variable =>
                variable.Name == "value").Type));
    }

    [Test]
    public void ExplicitConstantInferenceDoesNotFallBackToItsDefault()
    {
        var reference = TrRmExecutableCompiler.CreateGenericNominalTypeReference(
            CreateGenericTypeProvider("Example", "Box", 3, 2, 0,
                includeConstantFormal: true));
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            public int32 Read<const int32 N = 99,
                T = Box<int32, N>>(int32 value) {
                return N;
            }
            int32 Main() {
                return Read<_, default>(0);
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3011"), Is.True);
    }

    [Test]
    public void InfersMethodConstantFromPinnedNominalExpectedResult()
    {
        var reference = TrRmExecutableCompiler.CreateGenericNominalTypeReference(
            CreateGenericTypeProvider("Example", "Box", 3, 2, 0,
                includeConstantFormal: true,
                constantValue: 42));
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            public T Build<const int32 N = 99,
                T = Box<int32, N>>(int32 value) {
                while (true) { }
            }
            Box<int32, 42> SelectResult() {
                return Build<_, default>(0);
            }
            int32 Main() { return 0; }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var selectResult = program.Functions.Single(static function =>
            function.Name == "SelectResult");
        var calls = new List<BoundGenericMethodInstantiation>();
        Assert.That(BoundTreeTraversal.VisitExpressions(selectResult.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    calls.Add(instantiation);
                }
            }), Is.True);
        var call = calls.Single();
        Assert.That(call.ConstantArguments.Single().Bits.Low,
            Is.EqualTo(new BoundConstant(42, PrimitiveType.Int32,
                default).Bits.Low));
        Assert.That(call.TypeArguments.Single(),
            Is.EqualTo(selectResult.ReturnType));
    }

    [Test]
    public void BindsClosedImportedNominalConstantTypeArgument()
    {
        var reference = TrRmExecutableCompiler.CreateGenericNominalTypeReference(
            CreateGenericTypeProvider("Example", "Box", 3, 2, 0,
                includeConstantFormal: true));
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            Box<int32, 42> ReadBox() {
                return ReadConstants();
            }
            int32 Main() { return 0; }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(program.Functions.Single(static function =>
            function.Name == "ReadBox").ReturnType,
            Is.EqualTo(program.Functions.Single(static function =>
                function.Name == "ReadConstants").ReturnType));
        var type = program.Functions.Single(static function =>
            function.Name == "ReadBox").ReturnType;
        var instance = program.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(type)];
        Assert.That(instance.Arguments[1].ConstantLow, Is.EqualTo(42));
        Assert.That(instance.Arguments[1].HasDecodedConstant, Is.True);
    }

    [Test]
    public void BindsImportedNominalConstantDefault()
    {
        var reference = TrRmExecutableCompiler.CreateGenericNominalTypeReference(
            CreateGenericTypeProvider("Example", "Box", 3, 2, 0,
                includeConstantFormal: true,
                constantDefaultArgumentRef: 1));
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            Box<int32> ReadBox() {
                return ReadConstants();
            }
            int32 Main() { return 0; }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(program.Functions.Single(static function =>
            function.Name == "ReadBox").ReturnType,
            Is.EqualTo(program.Functions.Single(static function =>
                function.Name == "ReadConstants").ReturnType));
    }

    [Test]
    public void ForwardsEarlierConstantThroughCg1NominalDefault()
    {
        var provider = CreateGenericTypeProvider("Example", "Box", 3, 2, 0,
            includeConstantFormal: true,
            genericConstantMethodsDraft: true,
            includeConstantForwardingDefault: true);
        var reference = TrRmExecutableCompiler
            .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                provider);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Example;

            Box<int32, 42> Identity(Box<int32, 42> value) {
                return value;
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "The CG1 nominal default program did not bind.");
        var box = reference.GenericNominalTypeDefinitions.Single(
            static definition => definition.Name == "Box");
        var type = program.Functions.Single(static function =>
            function.Name == "Identity").ReturnType;
        var instance = program.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(type)];
        Assert.That(instance.Definition, Is.EqualTo(box.Identity));
        Assert.That(instance.Arguments.Length, Is.EqualTo(3));
        Assert.That(instance.Arguments[1].HasDecodedConstant, Is.True);
        Assert.That(instance.Arguments[2].HasDecodedConstant, Is.True);

        Assert.That(instance.Arguments[2].ConstantDomainType,
            Is.EqualTo(instance.Arguments[1].ConstantDomainType));
        Assert.That(instance.Arguments[2].ConstantLow,
            Is.EqualTo(instance.Arguments[1].ConstantLow));
        Assert.That(instance.Arguments[2].ConstantHigh,
            Is.EqualTo(instance.Arguments[1].ConstantHigh));

        var nominalDefinition = program.GenericNominalTypeDefinitions
            .Single(candidate => candidate.Identity == box.Identity);
        var malformedDefinition = nominalDefinition with {
            Formals = [
                .. nominalDefinition.Formals.Select(formal =>
                    formal.Ordinal == 2
                        ? formal with {
                            Default = formal.Default! with {
                                EarlierFormalOrdinal = 2,
                            },
                        }
                        : formal),
            ],
        };
        var malformedProgram = program with {
            GenericNominalTypeDefinitions = [
                .. program.GenericNominalTypeDefinitions.Select(candidate =>
                    candidate.Identity == box.Identity
                        ? malformedDefinition : candidate),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(malformedProgram,
            out _, out _, out _, out _), Is.False);

        var selfReference = provider.ToArray();
        var constantSourceOffset = GetSectionOffset(selfReference, 0x0030);
        BinaryPrimitives.WriteUInt32LittleEndian(
            selfReference.AsSpan(constantSourceOffset + 8 + 4), 3);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    selfReference));

        var typeFormalReference = provider.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            typeFormalReference.AsSpan(constantSourceOffset + 8 + 4), 1);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    typeFormalReference));

        var mismatchedDefaultDomain = provider.ToArray();
        var genericParamOffset = GetSectionOffset(
            mismatchedDefaultDomain, 0x0019);
        BinaryPrimitives.WriteUInt32LittleEndian(
            mismatchedDefaultDomain.AsSpan(genericParamOffset + 64 + 24),
            GetBuiltinTypeSpecReference(PrimitiveType.Int64));
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    mismatchedDefaultDomain));

        var unscopedTypeSpec = CreateGenericTypeProvider("Example", "Box",
            3, 2, 0, includeConstantFormal: true,
            genericConstantMethodsDraft: true,
            includeConstantForwardingDefault: true,
            forwardConstructedSignature: true);
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    unscopedTypeSpec));
    }

    [Test]
    public void BindsEnumConstantDomainThroughMixedGenericNominalTypeDefs()
    {
        var provider = CreateGenericTypeProvider("Example", "Box", 3, 2, 0,
            includeConstantFormal: true,
            constantDefaultArgumentRef: 1,
            includeEnumConstantDomain: true);
        var reference =
            TrRmExecutableCompiler.CreateNominalEnumsDraftGenericNominalTypeReference(
                provider);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateGenericNominalTypeReference(provider));
        var missingEnumFeature = CreateGenericTypeProvider("Example", "Box",
            3, 2, 0, includeConstantFormal: true,
            constantDefaultArgumentRef: 1,
            includeEnumConstantDomain: true,
            includeNominalEnumsFeature: false);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateNominalEnumsDraftGenericNominalTypeReference(
                missingEnumFeature));
        var definition = reference.GenericNominalTypeDefinitions.Single(
            static item => item.Name == "Box");
        var formal = definition.Formals.Single(static item =>
            item.Kind == BoundGenericArgumentKind.Constant);
        Assert.That(reference.EnumDefinitions, Has.Length.EqualTo(1));
        Assert.That(formal.ConstantDomainType, Is.EqualTo(BoundEnumTypes.Type(0)));
        Assert.That(formal.ConstantCarrierType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(formal.Default!.DecodedConstantArgument!.ConstantDomainType,
            Is.EqualTo(BoundEnumTypes.Type(0)));

        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            Box<int32, Color.Red> ReadBox() { return ReadConstants(); }
            Box<int32> ReadDefaultBox() { return ReadConstants(); }
            int32 Main() { return 0; }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var explicitType = program.Functions.Single(static function =>
            function.Name == "ReadBox").ReturnType;
        var defaultType = program.Functions.Single(static function =>
            function.Name == "ReadDefaultBox").ReturnType;
        Assert.That(explicitType, Is.EqualTo(defaultType));
        var instance = program.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(explicitType)];
        Assert.That(instance.Arguments[1].ConstantDomainType,
            Is.EqualTo(BoundEnumTypes.Type(0)));
        Assert.That(instance.Arguments[1].ConstantCarrierType,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(instance.Arguments[1].ConstantLow,
            Is.EqualTo(new BoundConstant(42, PrimitiveType.Int32,
                default).Bits.Low));

        var inferred = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            public int32 Read<const Color N, T = Box<int32, N>>(T value) {
                return 0;
            }
            int32 Main() {
                return Read<_, default>(ReadConstants());
            }
            """), reference);
        Assert.That(inferred.HasErrors, Is.False,
            string.Join(Environment.NewLine, inferred.Diagnostics));
        var inferredProgram = inferred.Program!;
        var main = inferredProgram.Functions.Single(static function =>
            function.Name == "Main");
        var calls = new List<BoundGenericMethodInstantiation>();
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    calls.Add(instantiation);
                }
            }), Is.True);
        var inferredArgument = calls.Single().ConstantArguments.Single();
        Assert.That(inferredArgument.DomainType,
            Is.EqualTo(BoundEnumTypes.Type(0)));
        Assert.That(inferredArgument.CarrierType,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(inferredArgument.Bits.Low,
            Is.EqualTo(new BoundConstant(42, PrimitiveType.Int32,
                default).Bits.Low));

        var inferredFromResult = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            public T Build<const Color N, T = Box<int32, N>>(int32 value) {
                while (true) { }
            }
            Box<int32, Color.Red> SelectResult() {
                return Build<_, default>(0);
            }
            int32 Main() { return 0; }
            """), reference);
        Assert.That(inferredFromResult.HasErrors, Is.False,
            string.Join(Environment.NewLine, inferredFromResult.Diagnostics));
        var resultFunction = inferredFromResult.Program!.Functions.Single(
            static function => function.Name == "SelectResult");
        var resultCalls = new List<BoundGenericMethodInstantiation>();
        Assert.That(BoundTreeTraversal.VisitExpressions(resultFunction.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    resultCalls.Add(instantiation);
                }
            }), Is.True);
        Assert.That(resultCalls.Single().ConstantArguments.Single().DomainType,
            Is.EqualTo(BoundEnumTypes.Type(0)));

        var wrongDomain = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            Box<int32, 42> ReadBox() { return ReadConstants(); }
            int32 Main() { return 0; }
            """), reference);
        Assert.That(wrongDomain.HasErrors, Is.True);
    }

    [Test]
    public void BindsCg1EnumValuesThroughTypedConstantSources()
    {
        var provider = CreateGenericTypeProvider("Example", "Box", 3, 2, 0,
            includeConstantFormal: true,
            constantDefaultArgumentRef: 2,
            includeEnumConstantDomain: true,
            genericConstantMethodsDraft: true,
            enumUnderlyingType: PrimitiveType.Int8,
            genericEnumMemberValue: new BigInteger(-7),
            genericEnumValue: new BigInteger(5),
            includeEnumValueAlias: true);
        var reference =
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    provider);

        Assert.That(reference.EnumDefinitions, Has.Length.EqualTo(1));
        var enumDefinition = reference.EnumDefinitions[0];
        Assert.That(enumDefinition.UnderlyingType,
            Is.EqualTo(PrimitiveType.Int8));
        Assert.That(enumDefinition.Members.Select(static member =>
            member.Value).ToArray(), Is.EqualTo([
                new BigInteger(-7), new BigInteger(-7),
            ]));
        var formal = reference.GenericNominalTypeDefinitions.Single(
            static item => item.Name == "Box").Formals.Single(
            static item => item.Kind == BoundGenericArgumentKind.Constant);
        Assert.That(formal.ConstantDomainType,
            Is.EqualTo(BoundEnumTypes.Type(0)));
        Assert.That(formal.ConstantCarrierType,
            Is.EqualTo(PrimitiveType.Int8));
        Assert.That(formal.Default!.DecodedConstantArgument!
            .ConstantDomainType, Is.EqualTo(BoundEnumTypes.Type(0)));
        Assert.That(formal.Default.DecodedConstantArgument!.ConstantLow,
            Is.EqualTo(5));

        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            Box<int32, Color.Red> ReadBox() { return ReadConstants(); }
            int32 Main() { return 0; }
            """), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateGenericNominalTypeReference(provider));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateNominalEnumsDraftGenericNominalTypeReference(provider));
    }

    [Test]
    public void ReadsAllCg1FixedIntegerEnumEndpoints()
    {
        var endpoints = new (PrimitiveType Type, string Value)[]
        {
            (PrimitiveType.Int8, "-128"), (PrimitiveType.Int8, "127"),
            (PrimitiveType.Int16, "-32768"), (PrimitiveType.Int16, "32767"),
            (PrimitiveType.Int32, "-2147483648"),
            (PrimitiveType.Int32, "2147483647"),
            (PrimitiveType.Int64, "-9223372036854775808"),
            (PrimitiveType.Int64, "9223372036854775807"),
            (PrimitiveType.Int128,
                "-170141183460469231731687303715884105728"),
            (PrimitiveType.Int128,
                "170141183460469231731687303715884105727"),
            (PrimitiveType.UInt8, "0"), (PrimitiveType.UInt8, "255"),
            (PrimitiveType.UInt16, "0"), (PrimitiveType.UInt16, "65535"),
            (PrimitiveType.UInt32, "0"), (PrimitiveType.UInt32, "4294967295"),
            (PrimitiveType.UInt64, "0"),
            (PrimitiveType.UInt64, "18446744073709551615"),
            (PrimitiveType.UInt128, "0"),
            (PrimitiveType.UInt128,
                "340282366920938463463374607431768211455"),
        };
        foreach (var (type, text) in endpoints)
        {
            var value = BigInteger.Parse(text, CultureInfo.InvariantCulture);
            var provider = CreateGenericTypeProvider("Example", "Box", 3, 2,
                0, includeConstantFormal: true,
                constantDefaultArgumentRef: 1,
                includeEnumConstantDomain: true,
                genericConstantMethodsDraft: true,
                enumUnderlyingType: type,
                genericEnumValue: value,
                genericEnumMemberValue: value);
            var reference = TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    provider);

            Assert.That(reference.EnumDefinitions.Single().UnderlyingType,
                Is.EqualTo(type), text);
            Assert.That(reference.EnumDefinitions.Single().Members.Single()
                .Value, Is.EqualTo(value), $"{type}: {text}");
            var compilation = Compilation.Create(SyntaxTree.Parse("""
                using Example;
                Box<int32, Color.Red> ReadBox() { return ReadConstants(); }
                int32 Main() { return 0; }
                """), reference);
            Assert.That(compilation.HasErrors, Is.False,
                $"{type}: {text}{Environment.NewLine}"
                    + string.Join(Environment.NewLine,
                        compilation.Diagnostics));
        }
    }

    [Test]
    public void RejectsMalformedCg1EnumMemberValueSources()
    {
        var missingEnumFeature = CreateGenericTypeProvider("Example", "Box",
            3, 2, 0, includeConstantFormal: true,
            constantDefaultArgumentRef: 1,
            includeEnumConstantDomain: true,
            genericConstantMethodsDraft: true,
            includeNominalEnumsFeature: false);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    missingEnumFeature));

        var missingGenericConstantFeature = CreateGenericTypeProvider(
            "Example", "Box", 3, 2, 0,
            includeConstantFormal: true,
            constantDefaultArgumentRef: 1,
            includeEnumConstantDomain: true,
            genericConstantMethodsDraft: true,
            includeGenericConstantFeature: false);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    missingGenericConstantFeature));

        var wrongDomain = CreateGenericTypeProvider("Example", "Box", 3, 2, 0,
            includeConstantFormal: true,
            constantDefaultArgumentRef: 1,
            includeEnumConstantDomain: true,
            genericConstantMethodsDraft: true,
            enumUnderlyingType: PrimitiveType.Int8);
        BinaryPrimitives.WriteUInt32LittleEndian(
            wrongDomain.AsSpan(GetSectionOffset(wrongDomain, 0x003F)),
            6);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    wrongDomain));

        var wrongSourceKind = CreateGenericTypeProvider("Example", "Box", 3,
            2, 0, includeConstantFormal: true,
            constantDefaultArgumentRef: 1,
            includeEnumConstantDomain: true,
            genericConstantMethodsDraft: true);
        var sourceOffset = GetSectionOffset(wrongSourceKind, 0x0030);
        BinaryPrimitives.WriteUInt32LittleEndian(
            wrongSourceKind.AsSpan(sourceOffset), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(
            wrongSourceKind.AsSpan(sourceOffset + 4), 2);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    wrongSourceKind));

        var wrongCarrier = CreateGenericTypeProvider("Example", "Box", 3, 2,
            0, includeConstantFormal: true,
            constantDefaultArgumentRef: 1,
            includeEnumConstantDomain: true,
            genericConstantMethodsDraft: true,
            enumUnderlyingType: PrimitiveType.Int8);
        BinaryPrimitives.WriteUInt32LittleEndian(
            wrongCarrier.AsSpan(GetSectionOffset(wrongCarrier, 0x0053) + 4),
            GetBuiltinTypeSpecReference(PrimitiveType.Int32));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    wrongCarrier));

        foreach (var nonInteger in new[]
            {
                PrimitiveType.Bool, PrimitiveType.Char8,
                PrimitiveType.Float16,
            })
        {
            var invalidBase = CreateGenericTypeProvider("Example", "Box",
                3, 2, 0, includeConstantFormal: true,
                constantDefaultArgumentRef: 1,
                includeEnumConstantDomain: true,
                genericConstantMethodsDraft: true,
                enumUnderlyingType: PrimitiveType.Int8);
            BinaryPrimitives.WriteUInt32LittleEndian(
                invalidBase.AsSpan(GetSectionOffset(invalidBase, 0x0053) + 4),
                GetBuiltinTypeSpecReference(nonInteger));
            _ = Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler
                    .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                        invalidBase));
        }
    }

    [Test]
    public void BindsCg1TransparentWrapperFrozenValuesAsNominalDomains()
    {
        var provider = CreateGenericTransparentWrapperProvider();
        var reference = TrRmExecutableCompiler
            .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                provider);

        Assert.That(reference.Structures, Has.Length.EqualTo(1));
        var wrapper = reference.Structures.Single();
        Assert.That(wrapper.IsTransparent, Is.True);
        Assert.That(wrapper.Fields.Single().Type,
            Is.EqualTo(PrimitiveType.Int8));
        Assert.That(wrapper.StandardTypeOrigin?.DefinitionId,
            Is.EqualTo(0x1300_0002));
        Assert.That(wrapper.StandardTypeOrigin?.IsLookupRoot, Is.True);

        var definition = reference.GenericNominalTypeDefinitions.Single(
            static item => item.Name == "Box");
        var formal = definition.Formals.Single(
            static item => item.Kind == BoundGenericArgumentKind.Constant);
        Assert.That(formal.ConstantDomainType,
            Is.EqualTo((PrimitiveType)100));
        Assert.That(formal.ConstantCarrierType,
            Is.EqualTo(PrimitiveType.Int8));
        Assert.That(formal.Default!.DecodedConstantArgument!
            .ConstantDomainType, Is.EqualTo((PrimitiveType)100));
        Assert.That(formal.Default.DecodedConstantArgument!.ConstantLow,
            Is.EqualTo(0xF9));

        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            Wrapper GetWrapper() { while (true) { } }
            Box<int32> ReadBox() { while (true) { } }
            int32 Main() { return 0; }
            """), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(
                TrRmArtifactIndex.ForGenericConstantMethodsDraft(provider));
        var materialized = TrRmConstantSourceValueValidator
            .MaterializeFrozenScalar(1, 1, 1,
                FixedLayoutStorage.WindowsX64TargetRequirement,
                tables.ConstantSources, tables.FrozenValueNodes,
                tables.Blobs, tables.TypeSpecs, tables.Features,
                tables.Modules, tables.TypeDefs, tables.EnumDefs,
                tables.Imports, tables.A3LayoutResourceLedger, tables.Fields,
                tables.TransparentConstantDomains, reference.Structures);
        Assert.That(materialized.Type, Is.EqualTo((PrimitiveType)100));
        Assert.That(materialized.RepresentationType,
            Is.EqualTo(PrimitiveType.Int8));
        Assert.That(materialized.Bits.Low, Is.EqualTo(0xF9));
    }

    [Test]
    public void ForwardsOpenFormalsThroughImportedGenericWrapperDomains()
    {
        var provider = CreateGenericTransparentWrapperProvider(
            genericWrapper: true);
        var reference = TrRmExecutableCompiler
            .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                provider);

        Assert.That(reference.Structures, Is.Empty);
        var wrapper = reference.GenericNominalTypeDefinitions.Single(
            static item => item.Name == "Wrapper");
        Assert.That(wrapper.TransparentConstantCarrierType,
            Is.EqualTo(PrimitiveType.Int8));
        var box = reference.GenericNominalTypeDefinitions.Single(
            static item => item.Name == "Box");
        var valueFormal = box.Formals.Single(
            static formal => formal.Kind
                == BoundGenericArgumentKind.Constant);
        Assert.That(valueFormal.ConstantDomainType, Is.Null);
        Assert.That(valueFormal.ConstantCarrierType,
            Is.EqualTo(PrimitiveType.Int8));
        Assert.That(valueFormal.ConstantDomainTypeTerm!.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.ImportedGeneric));
        Assert.That(valueFormal.ConstantDomainTypeTerm.NominalTypeIdentity,
            Is.EqualTo(wrapper.Identity));
        Assert.That(valueFormal.ConstantDomainTypeTerm.TypeArguments.Single()
                .FormalOrdinal,
            Is.Zero);

        var domainCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Example;
                public int32 Domain<T, const Wrapper<T> Value>(
                    int32 input) {
                    return 0;
                }
                """), reference);
        Assert.That(domainCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                domainCompilation.Diagnostics));

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Example;
            public int32 Read<T, const Wrapper<T> Value>(
                Box<T, Value> value) {
                return 0;
            }
            """), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        var proofProgram = definition.ProofProgram;
        var nominalInstance = proofProgram.GenericNominalTypeInstances.Single(
            instance => instance.Definition == box.Identity);
        Assert.That(nominalInstance.Arguments[1]
                .HasConstantFormalIdentity,
            Is.True);
        Assert.That(nominalInstance.Arguments[1].ConstantFormalDomain,
            Is.EqualTo(definition.ConstantFormals.Single().Domain));

        var nominalDefinition = proofProgram.GenericNominalTypeDefinitions
            .Single(candidate => candidate.Identity == box.Identity);
        var constantFormal = nominalDefinition.Formals.Single(
            static formal => formal.Kind
                == BoundGenericArgumentKind.Constant);
        var mismatchedDomain = constantFormal.ConstantDomainTypeTerm! with {
            TypeArguments = [
                new BoundGenericMethodTypeTerm(
                    BoundGenericMethodTypeTermKind.Concrete,
                    PrimitiveType.Int32, -1, -1, [],
                    false, false, false, false),
            ],
        };
        var malformedProofProgram = proofProgram with {
            GenericNominalTypeDefinitions = [
                .. proofProgram.GenericNominalTypeDefinitions.Select(
                    candidate => candidate.Identity == box.Identity
                        ? candidate with {
                            Formals = [
                                .. candidate.Formals.Select(formal =>
                                    formal.Ordinal == constantFormal.Ordinal
                                        ? formal with {
                                            ConstantDomainTypeTerm =
                                                mismatchedDomain,
                                        }
                                        : formal),
                            ],
                        }
                        : candidate),
            ],
        };
        var malformedProgram = program with {
            GenericMethodDefinitions = [
                .. program.GenericMethodDefinitions.Select(candidate =>
                    candidate.DefinitionOrdinal
                        == definition.DefinitionOrdinal
                        ? candidate with {
                            ProofProgram = malformedProofProgram,
                        }
                        : candidate),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(malformedProgram,
            out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void DecodesClosedGenericWrapperValuesAfterTypeFormalSubstitution()
    {
        var provider = CreateGenericTransparentWrapperProvider(
            genericWrapper: true, includeGenericWrapperValue: true);
        var reference = TrRmExecutableCompiler
            .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                provider);

        var box = reference.GenericNominalTypeDefinitions.Single(
            static item => item.Name == "Box");
        var boxInstance = reference.GenericNominalTypeInstances.Single(
            instance => instance.Definition == box.Identity);
        var value = boxInstance.Arguments[1];
        Assert.That(value.HasDecodedConstant, Is.True);
        Assert.That(value.ConstantCarrierType,
            Is.EqualTo(PrimitiveType.Int8));
        Assert.That(value.ConstantLow, Is.EqualTo(0xF9));

        var wrapper = reference.GenericNominalTypeDefinitions.Single(
            static item => item.Name == "Wrapper");
        var wrapperInstance = reference.GenericNominalTypeInstances.Single(
            instance => instance.Definition == wrapper.Identity);
        Assert.That(wrapperInstance.Arguments.Single().Type,
            Is.EqualTo(PrimitiveType.Int8));

        var referenceProgram = new BoundProgram([], -1) {
            GenericNominalTypeDefinitions =
                reference.GenericNominalTypeDefinitions,
            GenericNominalTypeInstances =
                reference.GenericNominalTypeInstances,
        };
        Assert.That(BoundGenericNominalTypeContract
            .IsValidArgumentTypes(referenceProgram), Is.True);
        Assert.That(BoundGenericNominalTypeContract.IsValidInstanceTable(
            reference.GenericNominalTypeInstances,
            reference.GenericNominalTypeDefinitions), Is.True);
        Assert.That(SemanticVerifier
            .IsValidGenericNominalConstantFormalBindings(referenceProgram),
            Is.True);

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Example;
            int32 Main() { return 0; }
            """), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void RejectsClosedGenericWrapperValueOutsideSubstitutedDomain()
    {
        var provider = CreateGenericTransparentWrapperProvider(
            genericWrapper: true, mismatchGenericWrapperValue: true,
            includeGenericWrapperValue: true);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    provider));
    }

    [Test]
    public void InfersGenericWrapperConstantFromImportedNominalInput()
    {
        var provider = CreateGenericTransparentWrapperProvider(
            genericWrapper: true, includeGenericWrapperValue: true);
        var reference = TrRmExecutableCompiler
            .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                provider);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            int32 Read<T, const Wrapper<T> Value>(
                Box<T, Value> value) {
                return 0;
            }
            int32 Main() { return Read(ReadBox(int32(0))); }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single(
            static item => item.Name == "Read");
        BoundGenericMethodInstantiation? call = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(
            program.Functions.Single(static function =>
                function.Name == "Main").Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    call = instantiation;
                }
            }), Is.True);

        Assert.That(call, Is.Not.Null);
        Assert.That(call!.DefinitionOrdinal,
            Is.EqualTo(definition.DefinitionOrdinal));
        Assert.That(call.TypeArguments, Is.EqualTo([PrimitiveType.Int8]));
        var actual = call.ConstantArguments.Single();
        Assert.That(actual.Kind,
            Is.EqualTo(BoundGenericMethodConstantArgumentKind.Value));
        Assert.That(actual.CarrierType, Is.EqualTo(PrimitiveType.Int8));
        Assert.That(actual.Bits.Low, Is.EqualTo(0xF9));
    }

    [Test]
    public void RejectsImportedWrapperInferenceForDifferentTypeActual()
    {
        var provider = CreateGenericTransparentWrapperProvider(
            genericWrapper: true, includeGenericWrapperValue: true);
        var reference = TrRmExecutableCompiler
            .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                provider);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            int32 Read<T, const Wrapper<T> Value>(
                Box<T, Value> value) {
                return 0;
            }
            int32 Main() {
                return Read<int32, _>(ReadBox(int32(0)));
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Select(static diagnostic =>
            diagnostic.Code), Does.Contain("TRM3007"));
    }

    [Test]
    public void RejectsConflictingImportedWrapperInferenceEvidence()
    {
        var reference = TrRmExecutableCompiler
            .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                CreateGenericTransparentWrapperProvider(
                    genericWrapper: true));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Example;
            Box<T, Value> Select<T, const Wrapper<T> Value>(
                Box<T, Value> value) {
                while (true) { }
            }
            Box<T, Other> Forward<T, const Wrapper<T> Value,
                const Wrapper<T> Other>(Box<T, Value> value) {
                return Select(value);
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Select(static diagnostic =>
            diagnostic.Code), Does.Contain("TRM3007"));
    }

    [Test]
    public void ForwardsGenericWrapperFormalFromExpectedImportedResult()
    {
        var provider = CreateGenericTransparentWrapperProvider(
            genericWrapper: true);
        var reference = TrRmExecutableCompiler
            .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                provider);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Example;
            Box<T, Value> Select<T, const Wrapper<T> Value>(T input) {
                while (true) { }
            }
            Box<T, Value> Forward<T, const Wrapper<T> Value>(T input) {
                return Select(input);
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var forward = program.GenericMethodDefinitions.Single(
            static item => item.Name == "Forward");
        var proofBody = forward.ProofProgram.Functions[
            forward.FunctionIndex];
        BoundGenericMethodInstantiation? call = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(proofBody.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    call = instantiation;
                }
            }), Is.True);

        Assert.That(call, Is.Not.Null);
        var forwarded = call!.ConstantArguments.Single();
        Assert.That(forwarded.Kind,
            Is.EqualTo(BoundGenericMethodConstantArgumentKind.Formal));
        Assert.That(forwarded.CallerDefinitionOrdinal,
            Is.EqualTo(forward.DefinitionOrdinal));
        Assert.That(forwarded.CallerConstantFormalOrdinal, Is.Zero);
        Assert.That(forwarded.Domain.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.ImportedGeneric));
        Assert.That(forwarded.Domain.TypeArguments.Single().Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Formal));
    }

    [Test]
    public void ForwardsImportedGenericWrapperDomainThroughInterfaceConstraint()
    {
        var reference = TrRmExecutableCompiler
            .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                CreateGenericTransparentWrapperProvider(
                    genericWrapper: true));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Example;
            internal interface I<T, const Wrapper<T> Value> {
                int32 Read(T value);
            }
            int32 ReadVia<T, const Wrapper<T> Value>(T value)
                where T : I<T, Value> {
                return value.via<I<T, Value>>.Read(value);
            }
            int32 Forward<T, const Wrapper<T> Value>(T value)
                where T : I<T, Value> {
                return ReadVia<T, Value>(value);
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions
            .Single(static item => item.Name == "ReadVia");
        var proofBody = definition.ProofProgram!.Functions[
            definition.FunctionIndex];
        BoundCall? call = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(proofBody.Body,
            expression =>
            {
                if (expression is BoundCall {
                        InterfaceCallIdentity.IsConstraintCall: true,
                    } candidate)
                {
                    call = candidate;
                }
            }), Is.True);
        Assert.That(call, Is.Not.Null);
        Assert.That(call!.InterfaceCallIdentity!.RequestedInterface
            .TypeArguments[1].ConstantParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Member));
        var domain = call.InterfaceCallIdentity.RequestedInterface
            .TypeArguments[1].ConstantType!;
        Assert.That(domain.Kind,
            Is.EqualTo(BoundInterfaceTypeKind.GenericNominal));
        Assert.That(domain.IsGenericNominalConstantDomain, Is.True);
        Assert.That(domain.TypeArguments.Single().Type!
            .TypeParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Member));

        var forward = program.GenericMethodDefinitions.Single(
            static item => item.Name == "Forward");
        var forwardBody = forward.ProofProgram!.Functions[
            forward.FunctionIndex];
        BoundGenericMethodInstantiation? forwardedCall = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(forwardBody.Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    forwardedCall = instantiation;
                }
            }), Is.True);
        Assert.That(forwardedCall, Is.Not.Null);
        var forwardedConstant = forwardedCall!.ConstantArguments.Single();
        Assert.That(forwardedConstant.Kind,
            Is.EqualTo(BoundGenericMethodConstantArgumentKind.Formal));
        Assert.That(forwardedConstant.CallerDefinitionOrdinal,
            Is.EqualTo(forward.DefinitionOrdinal));
        Assert.That(forwardedConstant.CallerFormalOrdinal, Is.EqualTo(1));
        Assert.That(forwardedConstant.CallerConstantFormalOrdinal, Is.Zero);

        var interfaceDefinition = program.Interfaces.Single(
            static item => item.QualifiedName == "I");
        var interfaceDomain = interfaceDefinition.Formals[1].ConstantType!;
        var malformedDomain = interfaceDomain with {
            TypeArguments = [
                interfaceDomain.TypeArguments[0] with {
                    Type = interfaceDomain.TypeArguments[0].Type! with {
                        TypeParameterOrdinal = int.MaxValue,
                    },
                },
            ],
        };
        var malformedInterface = interfaceDefinition with {
            Formals = [
                .. interfaceDefinition.Formals.Select((formal, ordinal) =>
                    ordinal == 1
                        ? formal with { ConstantType = malformedDomain }
                        : formal),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            Interfaces = [
                .. program.Interfaces.Select(candidate =>
                    candidate.QualifiedName == "I"
                        ? malformedInterface : candidate),
            ],
        }, out _, out _, out _, out _), Is.False);

        var box = program.GenericNominalTypeDefinitions.Single(
            static item => item.Name == "Box");
        var malformedIdentityDomain = interfaceDomain with {
            NominalName = box.Namespace + "." + box.Name,
            GenericNominalTypeIdentity = box.Identity,
        };
        var malformedIdentityInterface = interfaceDefinition with {
            Formals = [
                .. interfaceDefinition.Formals.Select((formal, ordinal) =>
                    ordinal == 1
                        ? formal with {
                            ConstantType = malformedIdentityDomain,
                        }
                        : formal),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            Interfaces = [
                .. program.Interfaces.Select(candidate =>
                    candidate.QualifiedName == "I"
                        ? malformedIdentityInterface : candidate),
            ],
        }, out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void BindsImportedGenericWrapperDomainInInterfaceConstraint()
    {
        var reference = TrRmExecutableCompiler
            .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                CreateGenericTransparentWrapperProvider(
                    genericWrapper: true));
        var interfaceCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Example;
                internal interface I<T, const Wrapper<T> Value> {
                    int32 Read(T value);
                }
                """), reference);
        Assert.That(interfaceCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                interfaceCompilation.Diagnostics));

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Example;
            internal interface I<T, const Wrapper<T> Value> {
                int32 Read(T value);
            }
            int32 Needs<T, const Wrapper<T> Value>(T value)
                where T : I<T, Value> {
                return 0;
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single(
            static item => item.Name == "Needs");
        var constraint = definition.Formals.Single()
            .InterfaceConstraints.Single();
        var constant = constraint.TypeArguments[1];
        Assert.That(constant.ConstantType!.Kind,
            Is.EqualTo(BoundInterfaceTypeKind.GenericNominal));
        Assert.That(constant.ConstantType.GenericNominalTypeIdentity,
            Is.EqualTo(program.GenericNominalTypeDefinitions.Single(
                static item => item.Name == "Wrapper").Identity));
        Assert.That(constant.ConstantType.IsGenericNominalConstantDomain,
            Is.True);
        Assert.That(constant.ConstantType.TypeArguments.Single().Type!
            .TypeParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Member));
    }

    [Test]
    public void BindsImportedNestedConstantTypeArgumentsInInterfaceConstraint()
    {
        var provider = CreateGenericTransparentWrapperProvider(
            genericWrapper: true, includeNestedConstantType: true);
        var reference = TrRmExecutableCompiler
            .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                provider);
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Example;
            internal interface I<T, const Wrapper<T> Value> {
                int32 Read(T value);
            }
            internal interface J<
                const Wrapper<Cell<int32, 7>> Marker> {
                int32 Read(int32 value);
            }
            int32 Needs<T, const int32 Seed,
                const Wrapper<Cell<int32, Seed>> Marker>(T value)
                where T : I<Cell<int32, Seed>, Marker> {
                return 0;
            }
            int32 Forward<T, const int32 Seed,
                const Wrapper<Cell<int32, Seed>> Marker>(T value)
                where T : I<Cell<int32, Seed>, Marker> {
                return Needs<T, Seed, Marker>(value);
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var cell = program.GenericNominalTypeDefinitions.Single(
            static item => item.Name == "Cell");
        var needs = program.GenericMethodDefinitions.Single(
            static item => item.Name == "Needs");
        var constraint = needs.Formals.Single()
            .InterfaceConstraints.Single();
        var nestedType = constraint.TypeArguments[0].Type!;
        Assert.That(nestedType.GenericNominalTypeIdentity,
            Is.EqualTo(cell.Identity));
        Assert.That(nestedType.TypeArguments[1].ConstantParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Member));
        Assert.That(nestedType.TypeArguments[1].ConstantParameterOrdinal,
            Is.EqualTo(1));
        Assert.That(constraint.TypeArguments[1].ConstantParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Member));
        Assert.That(constraint.TypeArguments[1].ConstantParameterOrdinal,
            Is.EqualTo(2));
        var wrapperDomain = needs.ConstantFormals[1].Domain;
        var nestedFormal = wrapperDomain.TypeArguments.Single()
            .ConstantArguments.Single();
        Assert.That(nestedFormal.Kind,
            Is.EqualTo(BoundGenericMethodTypeConstantArgumentKind.Formal));
        Assert.That(nestedFormal.FormalDefinitionOrdinal,
            Is.EqualTo(needs.DefinitionOrdinal));
        Assert.That(nestedFormal.FormalOrdinal, Is.Zero);
        var nestedInterface = program.Interfaces.Single(
            static item => item.QualifiedName == "J");
        var interfaceDomain = nestedInterface.Formals.Single()
            .ConstantType!;
        var interfaceCell = interfaceDomain.TypeArguments.Single().Type!;
        Assert.That(interfaceCell.GenericNominalTypeIdentity,
            Is.EqualTo(cell.Identity));
        Assert.That(interfaceCell.TypeArguments[1].ConstantValue!.Bits.Low,
            Is.EqualTo(7));

        var forward = program.GenericMethodDefinitions.Single(
            static item => item.Name == "Forward");
        BoundGenericMethodInstantiation? forwardedCall = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(
            forward.ProofProgram!.Functions[forward.FunctionIndex].Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    forwardedCall = instantiation;
                }
            }), Is.True);
        Assert.That(forwardedCall, Is.Not.Null);
        var forwardedSeed = forwardedCall!.ConstantArguments[0];
        Assert.That(forwardedSeed.Kind,
            Is.EqualTo(BoundGenericMethodConstantArgumentKind.Formal));
        Assert.That(forwardedSeed.CallerDefinitionOrdinal,
            Is.EqualTo(forward.DefinitionOrdinal));
        Assert.That(forwardedSeed.CallerConstantFormalOrdinal,
            Is.Zero);
        var forwardedMarker = forwardedCall.ConstantArguments[1];
        Assert.That(forwardedMarker.Kind,
            Is.EqualTo(BoundGenericMethodConstantArgumentKind.Formal));
        Assert.That(forwardedMarker.CallerDefinitionOrdinal,
            Is.EqualTo(forward.DefinitionOrdinal));
        Assert.That(forwardedMarker.CallerConstantFormalOrdinal,
            Is.EqualTo(1));

        var box = program.GenericNominalTypeDefinitions.Single(
            static item => item.Name == "Box");
        var malformedCell = interfaceCell with {
            NominalName = box.Namespace + "." + box.Name,
            GenericNominalTypeIdentity = box.Identity,
        };
        var malformedDomain = interfaceDomain with {
            TypeArguments = [
                BoundInterfaceTypeArgument.ForType(malformedCell),
            ],
        };
        var malformedInterface = nestedInterface with {
            Formals = [
                .. nestedInterface.Formals.Select(formal =>
                    formal with { ConstantType = malformedDomain }),
            ],
        };
        var malformedProgram = program with {
            Interfaces = [
                .. program.Interfaces.Select(candidate =>
                    candidate.QualifiedName == "J"
                        ? malformedInterface : candidate),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(malformedProgram,
            out _, out _, out _, out _), Is.False);

        var mismatch = Compilation.CreateLibrary(SyntaxTree.Parse("""
            using Example;
            internal interface I<T, const Wrapper<T> Value> {
                int32 Read(T value);
            }
            int32 Needs<T, const Wrapper<Cell<int32, 7>> Marker>(T value)
                where T : I<Cell<int32, 8>, Marker> {
                return 0;
            }
            """), reference);
        Assert.That(mismatch.HasErrors, Is.True,
            "A caller constant domain must match the imported nested actual.");

        var constrainedProvider = CreateGenericTransparentWrapperProvider(
            genericWrapper: true, includeNestedConstantType: true,
            constrainNestedType: true);
        var constrainedReference = TrRmExecutableCompiler
            .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                constrainedProvider);
        var unsupportedConstraint = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                using Example;
                internal interface I<T, const Wrapper<T> Value> {
                    int32 Read(T value);
                }
                int32 Needs<T, const Wrapper<Cell<int32, 7>> Marker>(T value)
                    where T : I<Cell<int32, 7>, Marker> {
                    return 0;
                }
                """), constrainedReference);
        Assert.That(unsupportedConstraint.HasErrors, Is.True,
            "Imported generic TypeDef constraints are not inferred from a use.");
    }

    [Test]
    public void BindsSourceGenericWrapperDomainInInterfaceConstraint()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct Tagged<T> { public int32 Value; }
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct OtherTagged<T> { public int32 Value; }
            struct Cell<const int32 Seed> { public int32 Value; }
            interface I<T, const Tagged<T> Marker> {
                int32 Read(T value);
            }
            int32 ReadVia<T, const Tagged<T> Marker>(T value)
                where T : I<T, Marker> {
                return value.via<I<T, Marker>>.Read(value);
            }
            int32 Forward<T, const Tagged<T> Marker>(T value)
                where T : I<T, Marker> {
                return ReadVia<T, Marker>(value);
            }
            interface Nested<T, const Tagged<Cell<7>> Marker> {
                int32 Read(T value);
            }
            int32 ReadNested<T, const Tagged<Cell<7>> Marker>(T value)
                where T : Nested<T, Marker> {
                return value.via<Nested<T, Marker>>.Read(value);
            }
            int32 ForwardNested<T, const Tagged<Cell<7>> Marker>(T value)
                where T : Nested<T, Marker> {
                return ReadNested<T, Marker>(value);
            }
            """), new CompilationOptions {
            LayoutCoreRegistration =
                LayoutCoreRegistrationTestData.Create(),
        });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var definition = program.SourceGenericTypeDefinitions.Single(
            static item => item.Name == "Tagged");
        var interfaceDefinition = program.Interfaces.Single(
            static item => item.QualifiedName == "I");
        var interfaceDomain = interfaceDefinition.Formals[1].ConstantType!;
        Assert.That(interfaceDomain.Kind,
            Is.EqualTo(BoundInterfaceTypeKind.GenericNominal));
        Assert.That(interfaceDomain.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(definition.DefinitionIndex));
        Assert.That(interfaceDomain.IsGenericNominalConstantDomain, Is.True);
        Assert.That(interfaceDomain.TypeArguments.Single().Type!
            .TypeParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Interface));

        var readVia = program.GenericMethodDefinitions.Single(
            static item => item.Name == "ReadVia");
        var constraintDomain = readVia.Formals.Single()
            .InterfaceConstraints.Single().TypeArguments[1].ConstantType!;
        Assert.That(constraintDomain.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(definition.DefinitionIndex));
        Assert.That(constraintDomain.TypeArguments.Single().Type!
            .TypeParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Member));

        var forward = program.GenericMethodDefinitions.Single(
            static item => item.Name == "Forward");
        BoundGenericMethodInstantiation? forwardedCall = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(
            forward.ProofProgram!.Functions[forward.FunctionIndex].Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    forwardedCall = instantiation;
                }
            }), Is.True);
        Assert.That(forwardedCall, Is.Not.Null);
        var forwardedConstant = forwardedCall!.ConstantArguments.Single();
        Assert.That(forwardedConstant.Kind,
            Is.EqualTo(BoundGenericMethodConstantArgumentKind.Formal));
        Assert.That(forwardedConstant.CallerDefinitionOrdinal,
            Is.EqualTo(forward.DefinitionOrdinal));
        Assert.That(forwardedConstant.CallerConstantFormalOrdinal,
            Is.Zero);

        var readNested = program.GenericMethodDefinitions.Single(
            static item => item.Name == "ReadNested");
        var nestedDomain = readNested.Formals.Single()
            .InterfaceConstraints.Single().TypeArguments[1].ConstantType!;
        var nestedType = nestedDomain.TypeArguments.Single().Type!;
        Assert.That(nestedType.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(program.SourceGenericTypeDefinitions.Single(
                static item => item.Name == "Cell").DefinitionIndex));
        Assert.That(nestedType.TypeArguments.Single().ConstantValue!
            .Bits.Low, Is.EqualTo(7));

        var forwardNested = program.GenericMethodDefinitions.Single(
            static item => item.Name == "ForwardNested");
        BoundGenericMethodInstantiation? forwardedNestedCall = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(
            forwardNested.ProofProgram!.Functions[
                forwardNested.FunctionIndex].Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    forwardedNestedCall = instantiation;
                }
            }), Is.True);
        Assert.That(forwardedNestedCall, Is.Not.Null);
        Assert.That(forwardedNestedCall!.ConstantArguments.Single()
            .CallerDefinitionOrdinal,
            Is.EqualTo(forwardNested.DefinitionOrdinal));

        var malformedIdentity = interfaceDomain with {
            SourceGenericTypeDefinitionIndex =
                program.SourceGenericTypeDefinitions.Single(
                    static item => item.Name == "OtherTagged")
                    .DefinitionIndex,
        };
        var malformedInterface = interfaceDefinition with {
            Formals = [
                .. interfaceDefinition.Formals.Select((formal, ordinal) =>
                    ordinal == 1
                        ? formal with { ConstantType = malformedIdentity }
                        : formal),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            Interfaces = [
                .. program.Interfaces.Select(candidate =>
                    candidate.QualifiedName == "I"
                        ? malformedInterface : candidate),
            ],
        }, out _, out _, out _, out _), Is.False);

        var nestedInterface = program.Interfaces.Single(
            static item => item.QualifiedName == "Nested");
        var nestedInterfaceDomain =
            nestedInterface.Formals[1].ConstantType!;
        var nestedCellType = nestedInterfaceDomain.TypeArguments.Single()
            .Type!;
        var changedCellArgument = nestedCellType.TypeArguments.Single() with {
            ConstantValue = new BoundConstant(new ScalarBits(8, 0),
                PrimitiveType.Int32, default),
        };
        var changedNestedDomain = nestedInterfaceDomain with {
            TypeArguments = [
                nestedInterfaceDomain.TypeArguments.Single() with {
                    Type = nestedCellType with {
                        TypeArguments = [changedCellArgument],
                    },
                },
            ],
        };
        var changedNestedInterface = nestedInterface with {
            Formals = [
                .. nestedInterface.Formals.Select((formal, ordinal) =>
                    ordinal == 1
                        ? formal with {
                            ConstantType = changedNestedDomain,
                        }
                        : formal),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            Interfaces = [
                .. program.Interfaces.Select(candidate =>
                    candidate.QualifiedName == "Nested"
                        ? changedNestedInterface : candidate),
            ],
        }, out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void ForwardsEarlierConstantThroughSourceGenericWrapperDomain()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct Tagged<T, const int32 Tag> {
                public int32 Value;
            }
            interface I<T, const int32 Tag,
                const Tagged<T, Tag> Marker> {
                int32 Read(T value);
            }
            int32 ReadVia<T, const int32 Tag,
                const Tagged<T, Tag> Marker>(T value)
                where T : I<T, Tag, Marker> {
                return value.via<I<T, Tag, Marker>>.Read(value);
            }
            int32 Forward<T, const int32 Tag,
                const Tagged<T, Tag> Marker>(T value)
                where T : I<T, Tag, Marker> {
                return ReadVia<T, Tag, Marker>(value);
            }
            """), new CompilationOptions {
            LayoutCoreRegistration =
                LayoutCoreRegistrationTestData.Create(),
        });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var tagged = program.SourceGenericTypeDefinitions.Single(
            static item => item.Name == "Tagged");
        var interfaceDefinition = program.Interfaces.Single(
            static item => item.QualifiedName == "I");
        var interfaceDomain = interfaceDefinition.Formals[2].ConstantType!;
        Assert.That(interfaceDomain.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(tagged.DefinitionIndex));
        Assert.That(interfaceDomain.TypeArguments[1]
            .ConstantParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Interface));
        Assert.That(interfaceDomain.TypeArguments[1]
            .ConstantParameterOrdinal, Is.EqualTo(1));

        var readVia = program.GenericMethodDefinitions.Single(
            static item => item.Name == "ReadVia");
        var constraintDomain = readVia.Formals.Single()
            .InterfaceConstraints.Single().TypeArguments[2]
            .ConstantType!;
        Assert.That(constraintDomain.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(tagged.DefinitionIndex));
        Assert.That(constraintDomain.TypeArguments[1]
            .ConstantParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Member));
        Assert.That(constraintDomain.TypeArguments[1]
            .ConstantParameterOrdinal, Is.EqualTo(1));

    }

    [Test]
    public void ForwardsEarlierSourceConstantThroughWrapperDomain()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct Tagged<const int32 Seed> { public int32 Value; }
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct Outer<const int32 Seed,
                const Tagged<Seed> Marker, const int32 Later> {
                public int32 Value;
            }
            interface I<T, const int32 Seed,
                const Tagged<Seed> Tag,
                const int32 Later,
                const Outer<Seed, Tag, Later> Marker> {
                int32 Read(T value);
            }
            int32 ReadVia<T, const int32 Seed,
                const Tagged<Seed> Tag,
                const int32 Later,
                const Outer<Seed, Tag, Later> Marker>(T value)
                where T : I<T, Seed, Tag, Later, Marker> {
                return value.via<I<T, Seed, Tag, Later, Marker>>.Read(value);
            }
            int32 Forward<T, const int32 Seed,
                const Tagged<Seed> Tag,
                const int32 Later,
                const Outer<Seed, Tag, Later> Marker>(T value)
                where T : I<T, Seed, Tag, Later, Marker> {
                return ReadVia<T, Seed, Tag, Later, Marker>(value);
            }
            int32 ReadOuter<const int32 Seed,
                const Tagged<Seed> Tag, const int32 Later>(
                Outer<Seed, Tag, Later> value) {
                return value.Value;
            }
            """), new CompilationOptions {
            LayoutCoreRegistration =
                LayoutCoreRegistrationTestData.Create(),
        });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var definitions = program.SourceGenericTypeDefinitions;
        var tagged = definitions.Single(static item =>
            item.Name == "Tagged");
        var outer = definitions.Single(static item =>
            item.Name == "Outer");
        var dependentDomain = outer.ConstantFormals[1].Domain;
        Assert.That(dependentDomain.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.SourceGeneric));
        Assert.That(dependentDomain.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(tagged.DefinitionIndex));
        var sourceFormal = dependentDomain.ConstantArguments.Single();
        Assert.That(sourceFormal.FormalOwnerKind,
            Is.EqualTo(BoundGenericTypeFormalOwnerKind.SourceGenericType));
        Assert.That(sourceFormal.FormalDefinitionOrdinal,
            Is.EqualTo(outer.DefinitionIndex));
        Assert.That(sourceFormal.FormalOrdinal, Is.Zero);
        Assert.That(BoundTreeTraversal.TryRewriteGenericMethodTypeTerm(
            dependentDomain, static type => type,
            static ordinal => ordinal,
            static definition => definition + 10,
            static identity => identity,
            out var rewrittenDomain), Is.True);
        Assert.That(rewrittenDomain.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(tagged.DefinitionIndex + 10));
        Assert.That(rewrittenDomain.ConstantArguments.Single()
            .FormalDefinitionOrdinal, Is.EqualTo(outer.DefinitionIndex + 10));

        var interfaceDefinition = program.Interfaces.Single(
            static item => item.QualifiedName == "I");
        var interfaceDomain =
            interfaceDefinition.Formals[4].ConstantType!;
        Assert.That(interfaceDomain.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(outer.DefinitionIndex));
        Assert.That(interfaceDomain.TypeArguments.Select(
                static argument => argument.ConstantParameterOrdinal),
            Is.EqualTo([1, 2, 3]));
        Assert.That(interfaceDomain.TypeArguments.All(
                static argument => argument.ConstantParameterScope
                    == BoundInterfaceTypeParameterScope.Interface),
            Is.True);

        var readVia = program.GenericMethodDefinitions.Single(
            static item => item.Name == "ReadVia");
        var constraintDomain = readVia.Formals.Single()
            .InterfaceConstraints.Single().TypeArguments[4].ConstantType!;
        Assert.That(constraintDomain.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(outer.DefinitionIndex));
        Assert.That(constraintDomain.TypeArguments.Select(
                static argument => argument.ConstantParameterOrdinal),
            Is.EqualTo([1, 2, 3]));
        Assert.That(constraintDomain.TypeArguments.All(
                static argument => argument.ConstantParameterScope
                    == BoundInterfaceTypeParameterScope.Member),
            Is.True);

        var forward = program.GenericMethodDefinitions.Single(
            static item => item.Name == "Forward");
        BoundGenericMethodInstantiation? forwardedCall = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(
            forward.ProofProgram!.Functions[forward.FunctionIndex].Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    forwardedCall = instantiation;
                }
            }), Is.True);
        Assert.That(forwardedCall, Is.Not.Null);
        var forwardedConstants = forwardedCall!.ConstantArguments;
        Assert.That(forwardedConstants.Length, Is.EqualTo(4));
        for (var ordinal = 0; ordinal < forwardedConstants.Length; ordinal++)
        {
            Assert.That(forwardedConstants[ordinal].Kind,
                Is.EqualTo(BoundGenericMethodConstantArgumentKind.Formal));
            Assert.That(forwardedConstants[ordinal]
                .CallerDefinitionOrdinal,
                Is.EqualTo(forward.DefinitionOrdinal));
            Assert.That(forwardedConstants[ordinal]
                .CallerConstantFormalOrdinal,
                Is.EqualTo(ordinal));
        }
        var forwardedTagDomain = forwardedConstants[1].Domain;
        Assert.That(forwardedTagDomain.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(tagged.DefinitionIndex));
        Assert.That(forwardedTagDomain.ConstantArguments.Single()
            .FormalDefinitionOrdinal,
            Is.EqualTo(forward.DefinitionOrdinal));
        var forwardedOuterDomain = forwardedConstants[3].Domain;
        Assert.That(forwardedOuterDomain.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(outer.DefinitionIndex));
        Assert.That(forwardedOuterDomain.ConstantArguments.Select(
                static argument => argument.FormalOrdinal),
            Is.EqualTo([0, 1, 2]));
        Assert.That(forwardedOuterDomain.ConstantArguments.All(
                argument => argument.FormalDefinitionOrdinal
                    == forward.DefinitionOrdinal
                    && argument.FormalOwnerKind
                        == BoundGenericTypeFormalOwnerKind.GenericMethod),
            Is.True);
        foreach (var invalidReferenceOrdinal in new[] { 1, 2 })
        {
            var invalidDomain = dependentDomain with {
                ConstantArguments = [
                    sourceFormal with {
                        FormalOrdinal = invalidReferenceOrdinal,
                    },
                ],
            };
            var invalidOuter = outer with {
                Formals = [
                    .. outer.Formals.Select((formal, ordinal) =>
                        ordinal == 1
                            ? formal with {
                                ConstantDomain = invalidDomain,
                            }
                            : formal),
                ],
                ConstantFormals = [
                    .. outer.ConstantFormals.Select((formal, ordinal) =>
                        ordinal == 1
                            ? formal with { Domain = invalidDomain }
                            : formal),
                ],
            };
            Assert.That(SemanticVerifier.TryVerifySource(program with {
                SourceGenericTypeDefinitions = [
                    .. definitions.Select(definition =>
                        definition.DefinitionIndex == outer.DefinitionIndex
                            ? invalidOuter : definition),
                ],
            }, out _, out _, out _, out _), Is.False);
        }

        var mismatchedCaller = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Transparent)]
                struct Tagged<const int32 Seed> { public int32 Value; }
                [Blittable] [Layout(LayoutKind.Transparent)]
                struct OtherTagged<const int32 Seed> {
                    public int32 Value;
                }
                [Blittable] [Layout(LayoutKind.Transparent)]
                struct Outer<const int32 Seed,
                    const Tagged<Seed> Marker, const int32 Later> {
                    public int32 Value;
                }
                int32 Read<const int32 Seed,
                    const OtherTagged<Seed> Tag, const int32 Later,
                    const Outer<Seed, Tag, Later> Marker>(
                    Outer<Seed, Tag, Later> value) {
                    return value.Value;
                }
                """), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(mismatchedCaller.HasErrors, Is.True);
        Assert.That(mismatchedCaller.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3005"), Is.True);

        var closedInstantiation = Compilation.Create(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Transparent)]
                struct Tagged<const int32 Seed> {
                    public int32 Value;
                    [ConstEval]
                    public Tagged(int32 value) { Value = value; }
                }
                [Blittable] [Layout(LayoutKind.Transparent)]
                struct Outer<const int32 Seed,
                    const Tagged<Seed> Marker, const int32 Later> {
                    public int32 Value;
                    public Outer(int32 value) { Value = value; }
                }
                int32 Main() {
                    Outer<7, (new Tagged<7>(9)), 3> value
                        = new Outer<7, (new Tagged<7>(9)), 3>(42);
                    return value.Value;
                }
                """), CoreEvaluationFormTestHelpers.WithConstEval(
                new CompilationOptions {
                    LayoutCoreRegistration =
                        LayoutCoreRegistrationTestData.Create(),
                }));
        Assert.That(closedInstantiation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                closedInstantiation.Diagnostics));
        Assert.That(NativeJit.Execute(closedInstantiation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void BindsSourceWrapperDomainDependingOnEarlierTypeFormal()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct Tagged<T> { public int32 Value; }
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct OtherTagged<T> { public int32 Value; }
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct Outer<T, const Tagged<T> Marker> {
                public int32 Value;
            }
            interface I<T, const Tagged<T> Tag,
                const Outer<T, Tag> Marker> {
                int32 Read(T value);
            }
            int32 ReadVia<T, const Tagged<T> Tag,
                const Outer<T, Tag> Marker>(T value)
                where T : I<T, Tag, Marker> {
                return value.via<I<T, Tag, Marker>>.Read(value);
            }
            int32 Forward<T, const Tagged<T> Tag,
                const Outer<T, Tag> Marker>(T value)
                where T : I<T, Tag, Marker> {
                return ReadVia<T, Tag, Marker>(value);
            }
            """), new CompilationOptions {
            LayoutCoreRegistration =
                LayoutCoreRegistrationTestData.Create(),
        });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var definitions = program.SourceGenericTypeDefinitions;
        var tagged = definitions.Single(static item =>
            item.Name == "Tagged");
        var outer = definitions.Single(static item => item.Name == "Outer");
        Assert.That(outer.ConstantFormals.Single().Domain.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.SourceGeneric));
        Assert.That(tagged.TransparentConstantCarrierType,
            Is.EqualTo(PrimitiveType.Int32));
        var interfaceDefinition = program.Interfaces.Single(
            static item => item.QualifiedName == "I");
        var interfaceOuter = interfaceDefinition.Formals[2].ConstantType!;
        Assert.That(interfaceOuter.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(outer.DefinitionIndex));
        Assert.That(interfaceOuter.TypeArguments[1].ConstantType!
            .SourceGenericTypeDefinitionIndex,
            Is.EqualTo(tagged.DefinitionIndex));
        var readVia = program.GenericMethodDefinitions.Single(
            static item => item.Name == "ReadVia");
        var methodOuter = readVia.Formals.Single()
            .InterfaceConstraints.Single().TypeArguments[2]
            .ConstantType!;
        Assert.That(methodOuter.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(outer.DefinitionIndex));
        Assert.That(methodOuter.TypeArguments[1].ConstantType!
            .SourceGenericTypeDefinitionIndex,
            Is.EqualTo(tagged.DefinitionIndex));

        var forward = program.GenericMethodDefinitions.Single(
            static item => item.Name == "Forward");
        BoundGenericMethodInstantiation? forwardedCall = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(
            forward.ProofProgram!.Functions[forward.FunctionIndex].Body,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    forwardedCall = instantiation;
                }
            }), Is.True);
        Assert.That(forwardedCall, Is.Not.Null);
        Assert.That(forwardedCall!.ConstantArguments[1]
            .CallerDefinitionOrdinal,
            Is.EqualTo(forward.DefinitionOrdinal));
        Assert.That(forwardedCall.ConstantArguments[1]
            .CallerConstantFormalOrdinal,
            Is.EqualTo(1));

        var wrongDomain = outer.Formals[1].ConstantDomain! with {
            SourceGenericTypeDefinitionIndex =
                definitions.Single(static item =>
                    item.Name == "OtherTagged").DefinitionIndex,
        };
        var malformedOuter = outer with {
            Formals = [
                .. outer.Formals.Select((formal, ordinal) =>
                    ordinal == 1
                        ? formal with { ConstantDomain = wrongDomain }
                        : formal),
            ],
            ConstantFormals = [
                .. outer.ConstantFormals.Select(formal =>
                    formal with { Domain = wrongDomain }),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            SourceGenericTypeDefinitions = [
                .. definitions.Select(definition =>
                    definition.DefinitionIndex == outer.DefinitionIndex
                        ? malformedOuter : definition),
            ],
        }, out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void BindsSourceWrapperDefaultsInInterfaceConstantDomains()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct Tagged<T, U = T> { public int32 Value; }
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct OtherTagged<T, U = T> { public int32 Value; }
            interface I<T, const Tagged<T> Marker> {
                int32 Read(T value);
            }
            interface J<T, const Tagged<T, default> Marker> {
                int32 Read(T value);
            }
            int32 ReadVia<T, const Tagged<T> Marker>(T value)
                where T : I<T, Marker> {
                return value.via<I<T, Marker>>.Read(value);
            }
            int32 Forward<T, const Tagged<T> Marker>(T value)
                where T : I<T, Marker> {
                return ReadVia<T, Marker>(value);
            }
            """), new CompilationOptions {
            LayoutCoreRegistration =
                LayoutCoreRegistrationTestData.Create(),
        });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var tagged = program.SourceGenericTypeDefinitions.Single(
            static item => item.Name == "Tagged");
        var definitions = program.SourceGenericTypeDefinitions;
        var interfaceDefinition = program.Interfaces.Single(
            static item => item.QualifiedName == "I");
        var omittedDomain = interfaceDefinition.Formals[1].ConstantType!;
        var explicitlyDefaultedDomain = program.Interfaces.Single(
            static item => item.QualifiedName == "J")
            .Formals[1].ConstantType!;

        foreach (var domain in new[] { omittedDomain, explicitlyDefaultedDomain })
        {
            Assert.That(domain.Kind,
                Is.EqualTo(BoundInterfaceTypeKind.GenericNominal));
            Assert.That(domain.SourceGenericTypeDefinitionIndex,
                Is.EqualTo(tagged.DefinitionIndex));
            Assert.That(domain.IsGenericNominalConstantDomain, Is.True);
            Assert.That(domain.TypeArguments, Has.Length.EqualTo(2));
            Assert.That(domain.TypeArguments[0].Kind,
                Is.EqualTo(BoundInterfaceTypeArgumentKind.Type));
            Assert.That(domain.TypeArguments[1].Kind,
                Is.EqualTo(BoundInterfaceTypeArgumentKind.Type));
            Assert.That(domain.TypeArguments[0].Type!.Kind,
                Is.EqualTo(BoundInterfaceTypeKind.TypeParameter));
            Assert.That(domain.TypeArguments[1].Type!.Kind,
                Is.EqualTo(BoundInterfaceTypeKind.TypeParameter));
            Assert.That(domain.TypeArguments[0].Type!.TypeParameterScope,
                Is.EqualTo(domain.TypeArguments[1].Type!.TypeParameterScope));
            Assert.That(domain.TypeArguments[0].Type!.TypeParameterOrdinal,
                Is.EqualTo(domain.TypeArguments[1].Type!.TypeParameterOrdinal));
            Assert.That(domain.IsWritable || domain.IsOptional
                || domain.IsGarbageCollected || domain.IsInitialization,
                Is.False);
        }

        var readVia = program.GenericMethodDefinitions.Single(
            static item => item.Name == "ReadVia");
        var methodDomain = readVia.ConstantFormals.Single().Domain;
        Assert.That(methodDomain.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.SourceGeneric));
        Assert.That(methodDomain.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(tagged.DefinitionIndex));
        Assert.That(methodDomain.TypeArguments, Has.Length.EqualTo(2));
        Assert.That(methodDomain.TypeArguments[0].Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Formal));
        Assert.That(methodDomain.TypeArguments[1].Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Formal));
        Assert.That(methodDomain.TypeArguments[0].FormalOrdinal,
            Is.EqualTo(methodDomain.TypeArguments[1].FormalOrdinal));

        var forward = program.GenericMethodDefinitions.Single(
            static item => item.Name == "Forward");
        var proofBody = forward.ProofProgram!.Functions[
            forward.FunctionIndex].Body;
        BoundGenericMethodInstantiation? forwardedCall = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(proofBody,
            expression =>
            {
                if (expression is BoundCall {
                        GenericMethodCall:
                            BoundGenericMethodInstantiation instantiation,
                    })
                {
                    forwardedCall = instantiation;
                }
            }), Is.True);
        Assert.That(forwardedCall, Is.Not.Null);
        var forwarded = forwardedCall!.ConstantArguments.Single();
        Assert.That(forwarded.Kind,
            Is.EqualTo(BoundGenericMethodConstantArgumentKind.Formal));
        Assert.That(forwarded.CallerDefinitionOrdinal,
            Is.EqualTo(forward.DefinitionOrdinal));
        Assert.That(forwarded.CallerConstantFormalOrdinal, Is.Zero);
        Assert.That(forwarded.Domain.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(tagged.DefinitionIndex));
        Assert.That(forwarded.Domain.TypeArguments, Has.Length.EqualTo(2));
        Assert.That(forwarded.Domain.TypeArguments[0].Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Formal));
        Assert.That(forwarded.Domain.TypeArguments[1].Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Formal));
        Assert.That(forwarded.Domain.TypeArguments[0].FormalOrdinal,
            Is.EqualTo(forwarded.Domain.TypeArguments[1].FormalOrdinal));
        SemanticVerifier.VerifyLibrary(program);

        var other = definitions.Single(static item =>
            item.Name == "OtherTagged");
        var wrongIdentity = interfaceDefinition with {
            Formals = [
                .. interfaceDefinition.Formals.Select((formal, ordinal) =>
                    ordinal == 1
                        ? formal with {
                            ConstantType = omittedDomain with {
                                SourceGenericTypeDefinitionIndex =
                                    other.DefinitionIndex,
                            },
                        }
                        : formal),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            Interfaces = [
                .. program.Interfaces.Select(definition =>
                    definition.QualifiedName == interfaceDefinition.QualifiedName
                        ? wrongIdentity : definition),
            ],
        }, out _, out _, out _, out _), Is.False);

        var writableDomain = interfaceDefinition with {
            Formals = [
                .. interfaceDefinition.Formals.Select((formal, ordinal) =>
                    ordinal == 1
                        ? formal with {
                            ConstantType = omittedDomain with {
                                IsWritable = true,
                            },
                        }
                        : formal),
            ],
        };
        Assert.That(SemanticVerifier.TryVerifySource(program with {
            Interfaces = [
                .. program.Interfaces.Select(definition =>
                    definition.QualifiedName == interfaceDefinition.QualifiedName
                        ? writableDomain : definition),
            ],
        }, out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void BindsSourceWrapperDefaultUsingEarlierConstantFormalInInterfaceDomain()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Cell<const int32 Count> { public int32 Value; }
            [Blittable] [Layout(LayoutKind.Transparent)]
            struct CountTag<const int32 Count, T = Cell<Count>> {
                public int32 Value;
            }
            interface I<const int32 Count,
                const CountTag<Count> Marker> { }
            """), new CompilationOptions {
            LayoutCoreRegistration =
                LayoutCoreRegistrationTestData.Create(),
        });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var cell = program.SourceGenericTypeDefinitions.Single(
            static item => item.Name == "Cell");
        var countTag = program.SourceGenericTypeDefinitions.Single(
            static item => item.Name == "CountTag");
        var domain = program.Interfaces.Single(
            static item => item.QualifiedName == "I")
            .Formals[1].ConstantType!;

        Assert.That(domain.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(countTag.DefinitionIndex));
        Assert.That(domain.IsGenericNominalConstantDomain, Is.True);
        Assert.That(domain.TypeArguments, Has.Length.EqualTo(2));
        var countArgument = domain.TypeArguments[0];
        Assert.That(countArgument.Kind,
            Is.EqualTo(BoundInterfaceTypeArgumentKind.Constant));
        Assert.That(countArgument.ConstantType!.PrimitiveType,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(countArgument.ConstantParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Interface));
        Assert.That(countArgument.ConstantParameterOrdinal, Is.Zero);

        var defaultType = domain.TypeArguments[1].Type!;
        Assert.That(defaultType.Kind,
            Is.EqualTo(BoundInterfaceTypeKind.GenericNominal));
        Assert.That(defaultType.SourceGenericTypeDefinitionIndex,
            Is.EqualTo(cell.DefinitionIndex));
        Assert.That(defaultType.TypeArguments, Has.Length.EqualTo(1));
        var forwardedCount = defaultType.TypeArguments.Single();
        Assert.That(forwardedCount.Kind,
            Is.EqualTo(BoundInterfaceTypeArgumentKind.Constant));
        Assert.That(forwardedCount.ConstantType!.PrimitiveType,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(forwardedCount.ConstantParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Interface));
        Assert.That(forwardedCount.ConstantParameterOrdinal, Is.Zero);
        SemanticVerifier.VerifyLibrary(program);
    }

    [TestCase("""
        [Blittable] [Layout(LayoutKind.Transparent)]
        struct Tagged<T, U> { public int32 Value; }
        interface I<T, const Tagged<T> Marker> { }
        """)]
    [TestCase("""
        [Blittable] [Layout(LayoutKind.Transparent)]
        struct Tagged<T, U = T> { public int32 Value; }
        interface I<T, const Tagged<T> Marker> { }
        int32 Conflicting<T, const Tagged<T, int32> Marker>(T value)
            where T : I<T, Marker> { return 0; }
        """)]
    public void RejectsUnavailableOrConflictingSourceWrapperDefaultsInInterfaceDomains(
        string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });

        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void BindsExpectedCg1WrapperToItsExactProvider()
    {
        var provider = CreateGenericTransparentWrapperProvider();
        var consumer = CreateGenericTransparentWrapperProvider(
            importWrapper: true);
        var reference = TrRmExecutableCompiler
            .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                consumer, [provider]);

        var wrapper = reference.Structures.Single();
        var origin = wrapper.StandardTypeOrigin!;
        Assert.That(origin.Provider.ModuleIdentity, Is.EqualTo("Provider"));
        Assert.That(origin.DefinitionId, Is.EqualTo(0x1300_0002));
        Assert.That(origin.IsLookupRoot, Is.True);
        Assert.That(origin.Expectations, Has.Length.EqualTo(1));
        Assert.That(origin.Expectations[0].UsingModule.ModuleIdentity,
            Is.EqualTo("Consumer"));
        Assert.That(origin.Expectations[0].ExpectedDefinitionId,
            Is.EqualTo(0x1300_0002));
        Assert.That(origin.Expectations[0].ProviderDefinitionId,
            Is.EqualTo(0x1300_0002));

        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            Wrapper GetWrapper() { while (true) { } }
            Box<int32> ReadBox() { while (true) { } }
            int32 Main() { return 0; }
            """), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    consumer, []));

        var mismatchedProvider = (byte[])provider.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            mismatchedProvider.AsSpan(
                GetSectionOffset(mismatchedProvider, 0x0013) + 44 + 4), 9);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    consumer, [mismatchedProvider]));

        var mismatchedRequirements = (byte[])provider.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            mismatchedRequirements.AsSpan(
                GetSectionOffset(mismatchedRequirements, 0x0001) + 8), 0);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    consumer, [mismatchedRequirements]));

        var mismatchedCarrierField = (byte[])provider.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            mismatchedCarrierField.AsSpan(
                GetSectionOffset(mismatchedCarrierField, 0x0014) + 4), 8);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    consumer, [mismatchedCarrierField]));

        var mismatchedLayout = (byte[])provider.Clone();
        mismatchedLayout[GetSectionOffset(mismatchedLayout, 0x0015)
            + (2 * 28) + 9] = 0;
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    consumer, [mismatchedLayout]));

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    consumer, [provider, provider]));
    }

    [Test]
    public void BindsExpectedCg1GenericWrapperToItsExactProvider()
    {
        var provider = CreateGenericTransparentWrapperProvider(
            genericWrapper: true);
        var consumer = CreateGenericTransparentWrapperProvider(
            importWrapper: true, genericWrapper: true);
        var reference = TrRmExecutableCompiler
            .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                consumer, [provider]);

        var wrapper = reference.GenericNominalTypeDefinitions.Single(
            static definition => definition.Name == "Wrapper");
        Assert.That(wrapper.Identity.ProviderIdentity, Is.EqualTo("Provider"));
        Assert.That(wrapper.Identity.ModuleIdentity, Is.EqualTo("Provider"));
        Assert.That(wrapper.Identity.DefinitionId, Is.EqualTo(0x1300_0002));
        Assert.That(wrapper.TransparentConstantCarrierType,
            Is.EqualTo(PrimitiveType.Int8));
        Assert.That(wrapper.ProviderExpectations, Has.Length.EqualTo(1));
        Assert.That(wrapper.ProviderExpectations[0].ExpectedDefinitionId,
            Is.EqualTo(0x1300_0002));
        Assert.That(wrapper.ProviderExpectations[0].ProviderDefinitionId,
            Is.EqualTo(wrapper.Identity.DefinitionId));
        Assert.That(wrapper.ProviderExpectations[0].UsingModule.ModuleIdentity,
            Is.EqualTo("Consumer"));

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    consumer, []));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    consumer, [provider, provider]));

        var incompatibleProvider = (byte[])provider.Clone();
        incompatibleProvider[GetSectionOffset(incompatibleProvider, 0x0019)
            + (2 * 32) + 21] = 1;
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    consumer, [incompatibleProvider]));

        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            int32 Read<const Wrapper<int32> Value>(int32 input) {
                return input;
            }
            int32 Main() { return 0; }
            """), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var boundWrapper = compilation.Program!.GenericNominalTypeDefinitions
            .Single(definition => definition.Identity
                == wrapper.Identity);
        Assert.That(boundWrapper.ProviderExpectations,
            Is.EqualTo(wrapper.ProviderExpectations));

        var inferenceCompilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            int32 Select<const Wrapper<int32> Value>(
                Box<int32, Value> value) {
                return 0;
            }
            int32 Forward<const Wrapper<int32> Value>(
                Box<int32, Value> value) {
                return Select<_>(value);
            }
            Box<int32, Value> SelectResult<
                const Wrapper<int32> Value>(
                int32 input) {
                while (true) { }
            }
            Box<int32, Value> ForwardResult<
                const Wrapper<int32> Value>(int32 input) {
                return SelectResult<_>(input);
            }
            int32 Main() { return 0; }
            """), reference);
        Assert.That(inferenceCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                inferenceCompilation.Diagnostics));
        var inferenceProgram = inferenceCompilation.Program!;
        foreach (var methodName in new[] { "Forward", "ForwardResult" })
        {
            var definition = inferenceProgram.GenericMethodDefinitions.Single(
                candidate => candidate.Name == methodName);
            BoundGenericMethodInstantiation? call = null;
            Assert.That(BoundTreeTraversal.VisitExpressions(
                definition.ProofProgram!.Functions[
                    definition.FunctionIndex].Body,
                expression =>
                {
                    if (expression is BoundCall {
                            GenericMethodCall:
                                BoundGenericMethodInstantiation instantiation,
                        })
                    {
                        call = instantiation;
                    }
                }), Is.True);
            Assert.That(call, Is.Not.Null);
            var inferred = call!.ConstantArguments.Single();
            Assert.That(inferred.Kind,
                Is.EqualTo(BoundGenericMethodConstantArgumentKind.Formal));
            Assert.That(inferred.CallerDefinitionOrdinal,
                Is.EqualTo(definition.DefinitionOrdinal));
            Assert.That(inferred.CallerConstantFormalOrdinal, Is.Zero);
            Assert.That(inferred.Domain.NominalTypeIdentity,
                Is.EqualTo(wrapper.Identity));
        }
    }

    [Test]
    public void InfersCg1WrapperConstantsFromTypedInputAndExpectedResult()
    {
        var reference = TrRmExecutableCompiler
            .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                CreateGenericTransparentWrapperProvider());
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            public int32 SelectInput<const Wrapper N,
                T = Box<int32, N>>(T value) {
                while (true) { }
            }
            public T SelectResult<const Wrapper N,
                T = Box<int32, N>>(int32 value) {
                while (true) { }
            }
            public T SelectBoth<const Wrapper N,
                T = Box<int32, N>>(T value) {
                while (true) { }
            }
            Box<int32> ReadBox() { while (true) { } }
            int32 FromInput() {
                return SelectInput<_, default>(ReadBox());
            }
            Box<int32> FromResult() {
                return SelectResult<_, default>(0);
            }
            Box<int32> FromBoth() {
                return SelectBoth<_, default>(ReadBox());
            }
            int32 Main() { return 0; }
            """), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        foreach (var functionName in new[] { "FromInput", "FromResult",
            "FromBoth" })
        {
            var function = compilation.Program!.Functions.Single(
                candidate => candidate.Name == functionName);
            var calls = new List<BoundGenericMethodInstantiation>();
            Assert.That(BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall {
                            GenericMethodCall:
                                BoundGenericMethodInstantiation instantiation,
                        })
                    {
                        calls.Add(instantiation);
                    }
                }), Is.True);
            var inferred = calls.Single().ConstantArguments.Single();
            Assert.That(inferred.DomainType,
                Is.EqualTo((PrimitiveType)100));
            Assert.That(inferred.CarrierType,
                Is.EqualTo(PrimitiveType.Int8));
            Assert.That(inferred.Bits.Low, Is.EqualTo(0xF9));
        }
    }

    [Test]
    public void RejectsMalformedCg1TransparentWrapperFrozenValues()
    {
        var missingTypedValues = CreateGenericTransparentWrapperProvider(
            includeFeature11: false);
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    missingTypedValues));

        var missingConstantFeature = CreateGenericTransparentWrapperProvider(
            includeFeature24: false);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    missingConstantFeature));

        var missingBlittableFeature = CreateGenericTransparentWrapperProvider(
            includeFeature1: false);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    missingBlittableFeature));

        var wrongDomain = CreateGenericTransparentWrapperProvider();
        BinaryPrimitives.WriteUInt32LittleEndian(
            wrongDomain.AsSpan(GetSectionOffset(wrongDomain, 0x003F)), 2);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    wrongDomain));

        var wrongDomainOwner = CreateGenericTransparentWrapperProvider();
        BinaryPrimitives.WriteUInt32LittleEndian(
            wrongDomainOwner.AsSpan(GetSectionOffset(wrongDomainOwner, 0x0065)),
            1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    wrongDomainOwner));

        var wrongLayout = CreateGenericTransparentWrapperProvider();
        wrongLayout[GetSectionOffset(wrongLayout, 0x0015) + 28 + 8] = 0;
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    wrongLayout));

        var wrongSourceKind = CreateGenericTransparentWrapperProvider();
        var sourceOffset = GetSectionOffset(wrongSourceKind, 0x0030);
        BinaryPrimitives.WriteUInt32LittleEndian(
            wrongSourceKind.AsSpan(sourceOffset), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(
            wrongSourceKind.AsSpan(sourceOffset + 4), 2);
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    wrongSourceKind));

        var wrongCarrier = CreateGenericTransparentWrapperProvider();
        var int16TypeSpecRef = GetBuiltinTypeSpecReference(
            PrimitiveType.Int16);
        BinaryPrimitives.WriteUInt32LittleEndian(
            wrongCarrier.AsSpan(GetSectionOffset(wrongCarrier, 0x0014) + 8),
            int16TypeSpecRef);
        BinaryPrimitives.WriteUInt32LittleEndian(
            wrongCarrier.AsSpan(GetSectionOffset(wrongCarrier, 0x0065) + 4),
            int16TypeSpecRef);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler
                .CreateGenericConstantMethodsDraftGenericNominalTypeReference(
                    wrongCarrier));

    }

    [TestCase("Box<int32, true>")]
    [TestCase("Box<42, 42>")]
    [TestCase("Box<int32>")]
    public void RejectsInvalidImportedNominalConstantTypeArguments(
        string typeName)
    {
        var reference = TrRmExecutableCompiler.CreateGenericNominalTypeReference(
            CreateGenericTypeProvider("Example", "Box", 3, 2, 0,
                includeConstantFormal: true));
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            using Example;
            {{typeName}} ReadBox() { while (true) { } }
            int32 Main() { return 0; }
            """), reference);

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True);
    }

    [Test]
    public void ConflictingNominalConstantEvidenceRejectsInference()
    {
        var reference =
            TrRmExecutableCompiler.CreateGenericNominalTypeReference(
                CreateGenericTypeProvider("Example", "Box", 3, 2, 0,
                    includeConstantFormal: true));
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            using Example;
            public int32 Select<const int32 N = 99,
                T = Box<int32, N>>(T value) {
                return N;
            }
            int32 Main() {
                return Select<43, default>(ReadConstants());
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3007"), Is.True);
    }

    [Test]
    public void RejectsMalformedGenericNominalDefaultsBeforeReferenceBinding()
    {
        var malformed = CreateGenericTypeProvider(
            "Terrum.Threading.Tasks", "Task", 3, 2, 0xF200_0000);
        var genericParamOffset = GetSectionOffset(malformed, 0x0019);
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformed.AsSpan(genericParamOffset + 28), 6);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateGenericNominalTypeReference(malformed));
    }

    private static byte[] CreateGenericTypeProvider(string @namespace, string name,
        byte ownershipDomain, byte nullabilityDomain, uint defaultArgument,
        bool includeConstantFormal = false, byte nominalKind = 2,
        uint? taskInputTypeSpecRef = null,
        uint constantDefaultArgumentRef = 0, int constantValue = 42,
        bool includeEnumConstantDomain = false,
        bool includeNominalEnumsFeature = true,
        bool genericConstantMethodsDraft = false,
        PrimitiveType enumUnderlyingType = PrimitiveType.Int32,
        bool includeGenericConstantFeature = true,
        BigInteger? genericEnumValue = null,
        BigInteger? genericEnumMemberValue = null,
        bool includeEnumValueAlias = false,
        bool includeConstantForwardingDefault = false,
        bool forwardConstructedSignature = false)
    {
        if (includeEnumConstantDomain && !includeConstantFormal)
        {
            throw new ArgumentException(
                "An enum constant domain requires a constant formal.",
                nameof(includeEnumConstantDomain));
        }
        if (genericConstantMethodsDraft
            && !includeConstantFormal)
        {
            throw new ArgumentException(
                "The CG1 provider fixture requires a constant formal.",
                nameof(genericConstantMethodsDraft));
        }
        if (includeConstantForwardingDefault
            && !genericConstantMethodsDraft)
        {
            throw new ArgumentException(
                "A constant-formal default requires the CG1 profile.",
                nameof(includeConstantForwardingDefault));
        }
        if (forwardConstructedSignature
            && !includeConstantForwardingDefault)
        {
            throw new ArgumentException(
                "A constructed TypeSpec forwarding fixture requires the CG1 formal source.",
                nameof(forwardConstructedSignature));
        }

        var stringNames = new List<(string Text, int Encoding)>
        {
            ("Provider", 0), (@namespace, 0), (name, 0), ("T", 0),
            ("ReadDefault", 0), ("ReadInteger", 0), ("ReadWritable", 0),
            ("NominalType", 0), ("ReadConstants", 0), ("N", 0),
            ("ReadWithTaskInput", 0), ("BoxType", 0),
        };
        if (includeEnumConstantDomain)
        {
            stringNames.Add(("Color", 0));
            stringNames.Add(("Red", 0));
            stringNames.Add(("color.red", 0));
            if (includeEnumValueAlias)
            {
                stringNames.Add(("AlsoRed", 0));
                stringNames.Add(("color.also-red", 0));
            }
        }
        if (includeConstantForwardingDefault)
        {
            stringNames.Add(("M", 0));
        }
        var stringsBytes = TrRmStringTableWriter.EncodeNames(stringNames);
        var stringCount = stringNames.Count;
        var strings = new TrRmStringTableReader(stringsBytes, stringCount);
        var methodCount = includeConstantFormal ? 5 : 4;
        var formalCount = includeConstantFormal
            ? includeConstantForwardingDefault ? 3 : 2
            : 1;
        var typeDefCount = includeEnumConstantDomain ? 3 : 2;
        var layoutCount = typeDefCount;
        var featureFirst = includeConstantFormal ? 1 : 0;
        var featureCount = includeConstantFormal ? 1 : 0;
        if (includeEnumConstantDomain && includeNominalEnumsFeature)
        {
            featureCount++;
        }
        if (genericConstantMethodsDraft && includeGenericConstantFeature)
        {
            featureCount++;
        }
        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 0, 0, 0, 1, 1, 1, 1,
                featureFirst, featureCount),
        ], strings, moduleRefCount: 0, typeDefCount: typeDefCount,
            methodDefCount: methodCount, sourceFileCount: 0,
            featureCount: featureCount);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            moduleRefCount: 0, typeDefCount: typeDefCount,
            methodDefCount: methodCount,
            sourceFileCount: 0, featureCount: featureCount);
        var typeDefRows = new List<TrRmTypeDefRow>
        {
            new TrRmTypeDefRow(2, 3, (uint)(2 | (nominalKind << 2)),
                0, 0, 1, formalCount, 1, 1, 0, 0),
            new TrRmTypeDefRow(2, 8, 10, 0, 0, 0, 0, 2, 1, 0, 0),
        };
        if (includeEnumConstantDomain)
        {
            typeDefRows.Add(new TrRmTypeDefRow(2, 13, 0x12, 0, 0, 0, 0,
                3, 1, 0, 0));
        }
        var typeDefSchema = genericConstantMethodsDraft
            ? TrRmArtifactSchema.GenericConstantMethodsDraft
            : includeEnumConstantDomain
                ? TrRmArtifactSchema.NominalEnumsDraft
                : TrRmArtifactSchema.Standard;
        var typeDefBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, fieldCount: 0, genericParamCount: formalCount,
            layoutCount: layoutCount, contractCount: 0,
            schema: typeDefSchema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes, typeDefCount,
            strings, modules, fieldCount: 0, genericParamCount: formalCount,
            layoutCount: layoutCount, contractCount: 0,
            schema: typeDefSchema);
        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layoutRows = new List<TrRmLayoutRow>
        {
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(2, 0, 0, 0, 0, 0, 0, 0),
        };
        if (includeEnumConstantDomain)
        {
            layoutRows.Add(new TrRmLayoutRow(3, 0, 0, 0, 0, 0, 0, 0));
        }
        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows, typeDefs,
            targetProfiles, layoutFieldCount: 0);
        var layouts = new TrRmLayoutTableReader(layoutBytes, layoutCount,
            typeDefs, targetProfiles, layoutFieldCount: 0);
        var typeSpecRows = new List<ReadOnlyMemory<byte>>
        {
            TrRmTypeSpecTableWriter.Reference(0x02, 1),
            ConstructedType(0xF200_0000),
            ConstructedType(0xF200_0008),
            ConstructedType(0xF301_8C_00),
        };
        if (includeConstantFormal)
        {
            typeSpecRows.Add(ConstructedType(0xF200_0008));
        }
        var boxTypeSpecRef = (uint)(typeSpecRows.Count + 1);
        typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x02, 2));
        var enumTypeSpecRef = 0u;
        if (includeEnumConstantDomain)
        {
            enumTypeSpecRef = (uint)(typeSpecRows.Count + 1);
            typeSpecRows.Add(TrRmTypeSpecTableWriter.Reference(0x02, 3));
        }
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            typeDefCount: typeDefCount, importCount: 0,
            genericParamCount: formalCount,
            nativeFunctionPointerSignatureCount: 0,
            schema: typeDefSchema);
        var typeSpecTable = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, typeDefCount: typeDefCount, importCount: 0,
            genericParamCount: formalCount,
            nativeFunctionPointerSignatureCount: 0,
            schema: typeDefSchema);
        var enumDefBytes = Array.Empty<byte>();
        var enumMemberBytes = Array.Empty<byte>();
        var enumDefs = default(TrRmEnumDefTableReader);
        var enumMemberCount = 0;
        var valueBuilder = new TrRmFrozenScalarValueBuilder();
        BoundConstant? enumConstant = null;
        var enumMemberSourceRef = 0u;
        if (genericConstantMethodsDraft && includeEnumConstantDomain)
        {
            var memberValue
                = genericEnumMemberValue ?? new BigInteger(constantValue);
            if (!ScalarBits.TryFromIntegerValue(memberValue,
                    enumUnderlyingType, out var enumBits))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(genericEnumMemberValue));
            }

            enumConstant = new BoundConstant(enumBits,
                BoundEnumTypes.Type(0), enumUnderlyingType, default);
            enumMemberSourceRef = valueBuilder.AddGenericNominalEnum(
                enumConstant, enumTypeSpecRef, enumUnderlyingType)
                .ConstantSourceRow;
        }
        if (includeEnumConstantDomain)
        {
            TrRmEnumMemberDefTableReader memberTable;
            if (genericConstantMethodsDraft)
            {
                var memberRows = new List<
                    TrRmTypedEnumMemberDefRow>
                {
                    new(3, 0, 15, 14, enumMemberSourceRef),
                };
                if (includeEnumValueAlias)
                {
                    memberRows.Add(new TrRmTypedEnumMemberDefRow(
                        3, 1, 17, 16, enumMemberSourceRef));
                }

                enumMemberCount = memberRows.Count;
                enumMemberBytes
                    = TrRmEnumTablesWriter
                        .EncodeTypedEnumMemberValues(
                            memberRows, strings, typeDefs,
                            checked((int)enumMemberSourceRef));
                memberTable = new TrRmEnumMemberDefTableReader(
                    enumMemberBytes, memberRows.Count, strings, typeDefs,
                    checked((int)enumMemberSourceRef));
            }
            else
            {
                var memberRows = new[] {
                    new TrRmEnumMemberDefRow(3, 0, 15, 14, constantValue),
                };
                enumMemberCount = memberRows.Length;
                enumMemberBytes = TrRmEnumTablesWriter.EncodeMembers(
                    memberRows, strings, typeDefs);
                memberTable = new TrRmEnumMemberDefTableReader(
                    enumMemberBytes, memberRows.Length, strings, typeDefs);
            }

            var enumRows = new[] {
                new TrRmEnumDefRow(3,
                    genericConstantMethodsDraft
                        ? GetBuiltinTypeSpecReference(enumUnderlyingType)
                        : 0xF200_0008,
                    0, 1, enumMemberCount),
            };
            enumDefBytes = TrRmEnumTablesWriter.Encode(enumRows, typeDefs,
                typeSpecTable, layouts, targetProfiles, memberTable);
            enumDefs = new TrRmEnumDefTableReader(enumDefBytes,
                enumRows.Length, typeDefs, typeSpecTable, layouts,
                targetProfiles, memberTable);
        }
        var signatureRows = new List<TrRmMethodSigRow>
        {
            new(2, 0, 0, 0, 0, 0),
            new(3, 0, 0, 0, 0, 0),
            new(4, 0, 0, 0, 0, 0),
        };
        if (includeConstantFormal)
        {
            signatureRows.Add(new TrRmMethodSigRow(5, 0, 0, 0, 0, 0));
        }
        var taskInputSignatureRow = signatureRows.Count + 1;
        signatureRows.Add(new TrRmMethodSigRow(2, 0, 1, 1, 0, 0));
        var signatureBytes = TrRmMethodSigTableWriter.Encode(signatureRows,
            typeSpecTable, paramCount: 1, genericParamCount: formalCount);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            signatureRows.Count, typeSpecTable, paramCount: 1,
            genericParamCount: formalCount);
        var parameterBytes = TrRmParamTableWriter.Encode([
            new TrRmParamRow(taskInputSignatureRow, 0,
                taskInputTypeSpecRef ?? boxTypeSpecRef, 0),
        ], signatures, typeSpecTable);
        var methodRows = new List<TrRmMethodDefRow>
        {
            new(0, 5, 1, 2, 0, 0, 0),
            new(0, 6, 2, 2, 0, 0, 0),
            new(0, 7, 3, 2, 0, 0, 0),
        };
        if (includeConstantFormal)
        {
            methodRows.Add(new TrRmMethodDefRow(0, 9, 4, 2, 0, 0, 0));
        }
        methodRows.Add(new TrRmMethodDefRow(0, 11, taskInputSignatureRow, 2, 0, 0, 0));
        var methodsBytes = TrRmMethodDefTableWriter.Encode(methodRows,
            strings, modules, typeDefs, signatures,
            contractCount: 0, bodyCount: 0);
        var methods = new TrRmMethodDefTableReader(methodsBytes, methodCount,
            strings, modules, typeDefs, signatures, contractCount: 0, bodyCount: 0);
        var exportRows = new List<TrRmExportRow>
        {
            new(0x1300_0001, 8),
            new(0x1300_0002, 12),
            new(0x1600_0001, 5),
            new(0x1600_0002, 6),
            new(0x1600_0003, 7),
        };
        if (includeConstantFormal)
        {
            exportRows.Add(new TrRmExportRow(0x1600_0004, 9));
        }
        exportRows.Add(new TrRmExportRow(0x1600_0000u | (uint)methodCount, 11));
        if (includeEnumConstantDomain)
        {
            exportRows.Add(new TrRmExportRow(0x1300_0003, 13));
        }
        var exportsBytes = TrRmExportTableWriter.Encode(exportRows,
            strings, typeDefs, methods, default, default);
        var genericParamRows = new List<TrRmGenericParamRow>
        {
            new(0x1300_0001, 0, 4, 0, 0,
                1, ownershipDomain, nullabilityDomain, 0, defaultArgument),
        };
        if (includeConstantFormal)
        {
            genericParamRows.Add(new TrRmGenericParamRow(0x1300_0001, 1, 10,
                0, 0, 2, 0, 0,
                includeEnumConstantDomain ? enumTypeSpecRef : 0xF200_0008,
                constantDefaultArgumentRef));
        }
        if (includeConstantForwardingDefault)
        {
            var forwardingFormalNameRow = checked((uint)(
                stringNames.FindIndex(static entry => entry.Text == "M") + 1));
            genericParamRows.Add(new TrRmGenericParamRow(0x1300_0001, 2,
                forwardingFormalNameRow, 0, 0, 2, 0, 0, 0xF200_0008, 2));
        }
        var valueTables = default(TrRmFrozenScalarValueTables);
        if (includeConstantFormal)
        {
            BoundConstant constant;
            TrRmFrozenScalarValueReference valueReference;
            if (genericConstantMethodsDraft
                && includeEnumConstantDomain)
            {
                var memberValue = genericEnumMemberValue
                    ?? new BigInteger(constantValue);
                var defaultValue = genericEnumValue ?? memberValue;
                if (defaultValue == memberValue)
                {
                    constant = enumConstant
                        ?? throw new InvalidOperationException(
                            "The CG1 enum provider has no member constant.");
                }
                else if (ScalarBits.TryFromIntegerValue(defaultValue,
                    enumUnderlyingType, out var defaultBits))
                {
                    constant = new BoundConstant(defaultBits,
                        BoundEnumTypes.Type(0), enumUnderlyingType, default);
                }
                else
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(genericEnumValue));
                }

                valueReference = valueBuilder.AddGenericNominalEnum(
                    constant, enumTypeSpecRef, enumUnderlyingType);
            }
            else
            {
                var bits = new BoundConstant(constantValue,
                    PrimitiveType.Int32, default).Bits;
                constant = new BoundConstant(bits,
                    includeEnumConstantDomain
                        ? BoundEnumTypes.Type(0) : PrimitiveType.Int32,
                    default);
                valueReference = includeEnumConstantDomain
                    ? valueBuilder.AddNominalEnum(constant, enumTypeSpecRef)
                    : valueBuilder.Add(constant);
            }

            var expectedSourceRow = genericConstantMethodsDraft
                && includeEnumConstantDomain
                && genericEnumValue is BigInteger selectedValue
                && selectedValue
                    != (genericEnumMemberValue
                        ?? new BigInteger(constantValue))
                    ? 2u : 1u;
            if (valueReference.ConstantSourceRow != expectedSourceRow)
            {
                throw new InvalidOperationException(
                    "The generic nominal constant source row is not canonical.");
            }

            valueTables = genericConstantMethodsDraft
                ? valueBuilder.EncodeGenericConstantMethodsDraft(
                    typeSpecTable, typeDefs, enumDefs, default, default,
                    default)
                : includeEnumConstantDomain
                    ? valueBuilder.EncodeNominalEnumsDraft(typeSpecTable,
                        typeDefs, enumDefs, default)
                    : valueBuilder.Encode(typeSpecTable);
        }
        if (includeConstantForwardingDefault)
        {
            var existingSources = new TrRmConstantSourceTableReader(
                valueTables.ConstantSourceSection,
                valueTables.ConstantSourceCount,
                valueTables.FrozenValueNodeCount,
                constantExecutionCount: 0,
                targetValueBindingCount: 0,
                genericParamCount: formalCount,
                constantTemplateCount: 0,
                schema: typeDefSchema);
            var sourceRows = new TrRmConstantSourceRow[
                valueTables.ConstantSourceCount + 1];
            for (var row = 1; row <= existingSources.Count; row++)
            {
                sourceRows[row - 1] = new TrRmConstantSourceRow(
                    existingSources.GetKind(row),
                    existingSources.GetPayloadReference(row));
            }
            sourceRows[^1] = new TrRmConstantSourceRow(4, 2);
            valueTables = valueTables with {
                ConstantSourceSection =
                    TrRmConstantSourceTableWriter.Encode(sourceRows,
                        valueTables.FrozenValueNodeCount,
                        constantExecutionCount: 0,
                        targetValueBindingCount: 0,
                        genericParamCount: formalCount,
                        constantTemplateCount: 0,
                        schema: typeDefSchema),
                ConstantSourceCount = sourceRows.Length,
            };
        }
        var genericParamsBytes = TrRmGenericParamTableWriter.Encode(genericParamRows,
            strings, typeDefs, methods, signatures, typeSpecTable,
            constraintCount: 0,
            constantSourceCount: valueTables.ConstantSourceCount);
        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, (uint)stringCount, stringsBytes),
            new(0x0011, 0, (uint)typeSpecRows.Count, typeSpecBytes),
            new(0x0013, 0, (uint)typeDefCount, typeDefBytes),
            new(0x0015, 0, (uint)layoutCount, layoutBytes),
            new(0x0016, 0, (uint)methodCount, methodsBytes),
            new(0x0017, 0, (uint)signatureRows.Count, signatureBytes),
            new(0x0018, 0, 1, parameterBytes),
            new(0x0019, 0, (uint)formalCount, genericParamsBytes),
            new(0x001D, 0, (uint)exportRows.Count, exportsBytes),
        };
        if (includeConstantFormal)
        {
            sections.Add(new TrRmSection(0x0003, 0,
                (uint)valueTables.BlobCount, valueTables.BlobSection));
            var featureRows = new List<TrRmFeatureRow>
            {
                TrRmFeatureRow.TypedFrozenValues,
            };
            if (includeEnumConstantDomain && includeNominalEnumsFeature)
            {
                featureRows.Add(new TrRmFeatureRow(18, 1));
            }
            if (genericConstantMethodsDraft && includeGenericConstantFeature)
            {
                featureRows.Add(
                    TrRmFeatureRow.ConstantGenericTermsCandidate);
            }
            sections.Add(new TrRmSection(0x0004, 0,
                (uint)featureRows.Count,
                TrRmFeatureTableWriter.Encode(featureRows, typeDefSchema)));
            sections.Add(new TrRmSection(0x0030, 0,
                (uint)valueTables.ConstantSourceCount,
                valueTables.ConstantSourceSection));
            sections.Add(new TrRmSection(0x003F, 0,
                (uint)valueTables.FrozenValueNodeCount,
                valueTables.FrozenValueNodeSection));
        }
        if (includeEnumConstantDomain)
        {
            sections.Add(new TrRmSection(0x0053, 0, 1, enumDefBytes));
            sections.Add(new TrRmSection(0x0054, 0,
                (uint)enumMemberCount, enumMemberBytes));
        }
        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, typeDefSchema);

        ReadOnlyMemory<byte> ConstructedType(uint typeArgument)
        {
            if (includeConstantForwardingDefault)
            {
                return TrRmTypeSpecTableWriter.Constructed(1,
                    [typeArgument, 1,
                        forwardConstructedSignature ? 2u : 1u]);
            }
            return includeConstantFormal
                ? TrRmTypeSpecTableWriter.Constructed(1, [typeArgument, 1])
                : TrRmTypeSpecTableWriter.Constructed(1, [typeArgument]);
        }
    }

    private static byte[] CreateGenericTransparentWrapperProvider(
        bool includeFeature1 = true, bool includeFeature11 = true,
        bool includeFeature24 = true, bool importWrapper = false,
        bool genericWrapper = false, bool mismatchGenericWrapperValue = false,
        bool includeGenericWrapperValue = false,
        bool includeNestedConstantType = false,
        bool constrainNestedType = false,
        bool includeForwardingDefault = false)
    {
        if ((mismatchGenericWrapperValue
                && !includeGenericWrapperValue)
            || (includeGenericWrapperValue
                && (!genericWrapper || importWrapper))
            || (includeNestedConstantType
                && (!genericWrapper || importWrapper))
            || (constrainNestedType && !includeNestedConstantType)
            || (includeForwardingDefault
                && (!genericWrapper || importWrapper
                    || includeGenericWrapperValue
                    || includeNestedConstantType)))
        {
            throw new ArgumentException(
                "The requested generic wrapper fixture shape is unsupported.");
        }

        var schema = TrRmArtifactSchema.GenericConstantMethodsDraft;
        var stringNames = new List<(string Text, int Encoding)>
        {
            ("Provider", 0), ("Example", 0), ("Box", 0), ("T", 0),
            ("N", 0), ("Wrapper", 0), ("value", 0), ("BoxType", 0),
            ("WrapperType", 0), ("Ignored", 0), ("Consumer", 0),
            ("ReadBox", 0), ("Cell", 0), ("Seed", 0),
        };
        if (includeForwardingDefault)
        {
            stringNames.Add(("M", 0));
        }
        var typeDefCount = includeNestedConstantType ? 3 : 2;
        var stringsBytes = TrRmStringTableWriter.EncodeNames(stringNames);
        var strings = new TrRmStringTableReader(stringsBytes,
            stringNames.Count);
        var featureRows = new List<TrRmFeatureRow>();
        if (includeFeature1)
        {
            featureRows.Add(TrRmFeatureRow.NativeBoundaryContracts);
        }
        if (includeFeature11)
        {
            featureRows.Add(TrRmFeatureRow.TypedFrozenValues);
        }
        if (includeFeature24)
        {
            featureRows.Add(TrRmFeatureRow.ConstantGenericTermsCandidate);
        }
        if (constrainNestedType)
        {
            featureRows.Add(TrRmFeatureRow.LayoutQueries);
        }
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureRows.Count, schema);

        var moduleBytes = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(importWrapper ? 11 : 1,
                FixedLayoutStorage.WindowsX64TargetRequirement,
                0, 0, 1, 1, 1, 1, 1,
                featureRows.Count),
        ], strings, moduleRefCount: importWrapper ? 1 : 0,
            typeDefCount: typeDefCount,
            methodDefCount: 1, sourceFileCount: 0,
            featureCount: featureRows.Count);
        var modules = new TrRmModuleTableReader(moduleBytes, 1, strings,
            moduleRefCount: importWrapper ? 1 : 0,
            typeDefCount: typeDefCount,
            methodDefCount: 1, sourceFileCount: 0,
            featureCount: featureRows.Count);
        var moduleRefBytes = importWrapper
            ? TrRmModuleRefTableWriter.Encode([
                new TrRmModuleRefRow(1,
                    FixedLayoutStorage.WindowsX64TargetRequirement, 0),
            ], strings, modules)
            : [];
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes,
            importWrapper ? 1 : 0, strings, modules);
        var genericParamCount = (genericWrapper ? 3 : 2)
            + (includeForwardingDefault ? 1 : 0)
            + (includeNestedConstantType ? 2 : 0);
        var typeDefRows = new List<TrRmTypeDefRow>
        {
            new TrRmTypeDefRow(2, 3, 10, 0, 0, 1,
                includeForwardingDefault ? 3 : 2, 1, 1, 0, 0),
            new TrRmTypeDefRow(2, 6, 6, 1, 1,
                genericWrapper
                    ? (includeForwardingDefault ? 4 : 3) : 0,
                genericWrapper ? 1 : 0,
                2, 2, 0, 0),
        };
        if (includeNestedConstantType)
        {
            typeDefRows.Add(new TrRmTypeDefRow(2, 13, 10, 0, 0,
                4, 2, 4, 1, 0, 0));
        }
        var typeDefBytes = TrRmTypeDefTableWriter.Encode(typeDefRows,
            strings, modules, fieldCount: 1,
            genericParamCount: genericParamCount,
            layoutCount: includeNestedConstantType ? 4 : 3,
            contractCount: 0, schema);
        var typeDefs = new TrRmTypeDefTableReader(typeDefBytes,
            typeDefCount, strings,
            modules, fieldCount: 1,
            genericParamCount: genericParamCount,
            layoutCount: includeNestedConstantType ? 4 : 3,
            contractCount: 0, schema);
        var typeSpecRows = genericWrapper
            ? includeGenericWrapperValue
                ? new ReadOnlyMemory<byte>[] {
                    TrRmTypeSpecTableWriter.Reference(0x02, 2),
                    TrRmTypeSpecTableWriter.Reference(0x02, 1),
                    TrRmTypeSpecTableWriter.Reference(0x04, 1),
                    TrRmTypeSpecTableWriter.Constructed(1, [3]),
                    TrRmTypeSpecTableWriter.Constructed(1,
                        [0xF200_0006]),
                    TrRmTypeSpecTableWriter.Constructed(2,
                        [mismatchGenericWrapperValue
                            ? GetBuiltinTypeSpecReference(
                                PrimitiveType.Int32)
                            : 0xF200_0006, 1]),
                }
                : [
                TrRmTypeSpecTableWriter.Reference(
                    importWrapper ? (byte)0x03 : (byte)0x02,
                    importWrapper ? 1u : 2u),
                TrRmTypeSpecTableWriter.Reference(0x02, 1),
                TrRmTypeSpecTableWriter.Reference(0x04, 1),
                TrRmTypeSpecTableWriter.Constructed(1, [3]),
            ]
            : [
                TrRmTypeSpecTableWriter.Reference(
                    importWrapper ? (byte)0x03 : (byte)0x02,
                    importWrapper ? 1u : 2u),
                TrRmTypeSpecTableWriter.Reference(0x02, 1),
            ];
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            typeDefCount: typeDefCount,
            importCount: importWrapper ? 1 : 0,
            genericParamCount: genericParamCount,
            nativeFunctionPointerSignatureCount: 0, schema);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Length,
            typeDefCount: typeDefCount,
            importCount: importWrapper ? 1 : 0,
            genericParamCount: genericParamCount,
            nativeFunctionPointerSignatureCount: 0, schema);
        var targetProfileBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader.WindowsX64NativeProfileRuleRevision),
        ]);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, 1, schema);
        var layoutRows = new List<TrRmLayoutRow>
        {
            new TrRmLayoutRow(1, 0, 0, 0, 0, 0, 0, 0),
            new TrRmLayoutRow(2, 0, 3, 0, 0, 0, 1, 1),
            new TrRmLayoutRow(2, 1, 3, 1, 0, 0, 2, 1),
        };
        if (includeNestedConstantType)
        {
            layoutRows.Add(new TrRmLayoutRow(3, 0, 1, 0, 4, 1, 0, 0));
        }
        var layoutBytes = TrRmLayoutTableWriter.Encode(layoutRows,
            typeDefs, targetProfiles, layoutFieldCount: 2);
        var layouts = new TrRmLayoutTableReader(layoutBytes,
            layoutRows.Count, typeDefs,
            targetProfiles, layoutFieldCount: 2);
        var fieldBytes = TrRmFieldDefTableWriter.Encode([
            new TrRmFieldDefRow(2, 7, 0xF200_0006, 0),
        ], strings, typeDefs, typeSpecs);
        var fields = new TrRmFieldDefTableReader(fieldBytes, 1, strings,
            typeDefs, typeSpecs);
        var layoutFieldBytes = TrRmLayoutFieldTableWriter.Encode([
            new TrRmLayoutFieldRow(2, 0, 1),
            new TrRmLayoutFieldRow(3, 0, 1),
        ], layouts, typeDefs, fields);
        var layoutFields = new TrRmLayoutFieldTableReader(layoutFieldBytes, 2,
            layouts, typeDefs, fields);
        var transparentDomainBytes
            = TrRmTransparentConstantDomainTableWriter.Encode([
                new TrRmTransparentConstantDomainRow(2, 0xF200_0006, 1),
            ], typeDefs, fields, typeSpecs);
        var transparentDomains
            = new TrRmTransparentConstantDomainTableReader(
                transparentDomainBytes, 1, typeDefs, fields, typeSpecs);

        var signatureRows = new[]
        {
            new TrRmMethodSigRow(includeGenericWrapperValue ? 6u
                : 0xF200_0000, 0, 1, 1, 0, 0),
        };
        var signatureBytes = TrRmMethodSigTableWriter.Encode(signatureRows,
            typeSpecs, paramCount: 1,
            genericParamCount: genericParamCount);
        var signatures = new TrRmMethodSigTableReader(signatureBytes, 1,
            typeSpecs, paramCount: 1,
            genericParamCount: genericParamCount);
        var parameterBytes = TrRmParamTableWriter.Encode([
            new TrRmParamRow(1, 0, 0xF200_0008, 0),
        ], signatures, typeSpecs);
        var methodRows = new[]
        {
            new TrRmMethodDefRow(0,
                includeGenericWrapperValue ? 12 : 10, 1, 2, 0, 0, 0),
        };
        var methodsBytes = TrRmMethodDefTableWriter.Encode(methodRows, strings,
            modules, typeDefs, signatures, contractCount: 0, bodyCount: 0);
        var methods = new TrRmMethodDefTableReader(methodsBytes, 1, strings,
            modules, typeDefs, signatures, contractCount: 0, bodyCount: 0);
        var importBytes = importWrapper
            ? TrRmImportTableWriter.Encode([
                new TrRmImportRow(1, 9, 0x1300_0002),
            ], strings, moduleRefs, typeDefs, methods, default, default,
                default, fields, features, modules, schema)
            : [];
        var imports = new TrRmImportTableReader(importBytes,
            importWrapper ? 1 : 0, strings, moduleRefs, typeDefs, methods,
            default, default, default, fields, features, modules, schema);
        var exportRows = importWrapper
            ? new[] { new TrRmExportRow(0x1300_0001, 8) }
            : includeGenericWrapperValue
            ? [
                new TrRmExportRow(0x1300_0001, 8),
                new TrRmExportRow(0x1300_0002, 9),
                new TrRmExportRow(0x1600_0001, 12),
            ]
            : [
                new TrRmExportRow(0x1300_0001, 8),
                new TrRmExportRow(0x1300_0002, 9),
            ];
        if (includeNestedConstantType)
        {
            exportRows = [.. exportRows,
                new TrRmExportRow(0x1300_0003, 13)];
        }
        var exportBytes = TrRmExportTableWriter.Encode(exportRows, strings,
            typeDefs, methods, default, imports);
        var genericParamRows = new List<TrRmGenericParamRow>
        {
            new(0x1300_0001, 0, 4, 0, 0, 1, 3, 2, 0, 0),
            new(0x1300_0001, 1, 5, 0, 0, 2, 0, 0,
                genericWrapper ? 4u : 1u,
                genericWrapper ? 0u : 1u),
        };
        if (includeForwardingDefault)
        {
            genericParamRows.Add(new TrRmGenericParamRow(
                0x1300_0001, 2, 15, 0, 0, 2, 0, 0, 4, 1));
        }
        if (genericWrapper)
        {
            genericParamRows.Add(new TrRmGenericParamRow(
                0x1300_0002, 0, 4, 0, 0, 1, 3, 2, 0, 0));
        }
        if (includeNestedConstantType)
        {
            genericParamRows.Add(new TrRmGenericParamRow(
                0x1300_0003, 0, 4,
                constrainNestedType ? 1 : 0,
                constrainNestedType ? 1 : 0, 1, 3, 2, 0, 0));
            genericParamRows.Add(new TrRmGenericParamRow(
                0x1300_0003, 1, 14, 0, 0, 2, 0, 0,
                GetBuiltinTypeSpecReference(PrimitiveType.Int32), 0));
        }
        TrRmConstraintRow[] constraintRows = constrainNestedType
            ? [new TrRmConstraintRow(4, 2, 3)] : [];
        var genericParamBytes = TrRmGenericParamTableWriter.Encode(
            genericParamRows, strings, typeDefs, methods, signatures,
            typeSpecs, constraintCount: constraintRows.Length,
            constantSourceCount: genericWrapper
                ? (includeGenericWrapperValue || includeForwardingDefault
                    ? 1 : 0) : 1);
        var genericParams = new TrRmGenericParamTableReader(
            genericParamBytes, genericParamRows.Count, strings, typeDefs,
            methods, signatures, typeSpecs,
            constraintCount: constraintRows.Length,
            constantSourceCount: genericWrapper
                ? (includeGenericWrapperValue || includeForwardingDefault
                    ? 1 : 0) : 1);
        var constraintBytes = constraintRows.Length == 0 ? []
            : TrRmConstraintTableWriter.Encode(constraintRows,
                genericParams, typeSpecs, default);
        var valueBuilder = new TrRmFrozenScalarValueBuilder();
        if (!genericWrapper || includeGenericWrapperValue)
        {
            if (!ScalarBits.TryFromIntegerValue(new BigInteger(-7),
                    PrimitiveType.Int8, out var bits))
            {
                throw new InvalidOperationException(
                    "The wrapper fixture value does not fit its carrier.");
            }

            var wrapper = new BoundStruct("Example.Wrapper",
                [new BoundStructField("value", PrimitiveType.Int8, 0, 1)],
                1) {
                IsTransparent = true,
                Alignment = 1,
                BlittableTargetRequirement
                    = FixedLayoutStorage.WindowsX64TargetRequirement,
            };
            _ = valueBuilder.AddGenericNominalWrapper(
                new BoundConstant(bits,
                    includeGenericWrapperValue
                        ? BoundGenericNominalTypeTypes.Type(0)
                        : (PrimitiveType)100,
                    PrimitiveType.Int8, default),
                includeGenericWrapperValue ? 5u : 1u, wrapper);
        }

        var valueTables = valueBuilder.EncodeGenericConstantMethodsDraft(
            typeSpecs, typeDefs, default, fields, transparentDomains, imports);
        if (includeForwardingDefault)
        {
            valueTables = valueTables with {
                ConstantSourceSection =
                    TrRmConstantSourceTableWriter.Encode(
                        [new TrRmConstantSourceRow(4, 2)],
                        valueTables.FrozenValueNodeCount,
                        constantExecutionCount: 0,
                        targetValueBindingCount: 0,
                        genericParamCount: genericParamCount,
                        constantTemplateCount: 0,
                        schema: schema),
                ConstantSourceCount = 1,
            };
        }
        var sections = new List<TrRmSection>
        {
            new(0x0001, 0, 1, moduleBytes),
            new(0x0002, 0, (uint)stringNames.Count, stringsBytes),
            new(0x0004, 0, (uint)featureRows.Count,
                featureBytes),
            new(0x0011, 0, (uint)typeSpecRows.Length, typeSpecBytes),
            new(0x0013, 0, (uint)typeDefCount, typeDefBytes),
            new(0x0014, 0, 1, fieldBytes),
            new(0x0015, 0, (uint)layoutRows.Count, layoutBytes),
            new(0x0016, 0, 1, methodsBytes),
            new(0x0017, 0, 1, signatureBytes),
            new(0x0018, 0, 1, parameterBytes),
            new(0x0019, 0, (uint)genericParamRows.Count,
                genericParamBytes),
            new(0x0023, 0, 2, layoutFieldBytes),
            new(0x001D, 0, (uint)exportRows.Length, exportBytes),
            new(0x0048, 0, 1, targetProfileBytes),
            new(0x0065, 0, 1, transparentDomainBytes),
        };
        if (constraintRows.Length != 0)
        {
            sections.Add(new TrRmSection(0x001A, 0,
                (uint)constraintRows.Length, constraintBytes));
        }
        if (valueTables.BlobCount > 0)
        {
            sections.Add(new TrRmSection(0x0003, 0,
                (uint)valueTables.BlobCount, valueTables.BlobSection));
        }
        if (valueTables.ConstantSourceCount > 0)
        {
            sections.Add(new TrRmSection(0x0030, 0,
                (uint)valueTables.ConstantSourceCount,
                valueTables.ConstantSourceSection));
        }
        if (valueTables.FrozenValueNodeCount > 0)
        {
            sections.Add(new TrRmSection(0x003F, 0,
                (uint)valueTables.FrozenValueNodeCount,
                valueTables.FrozenValueNodeSection));
        }
        if (importWrapper)
        {
            sections.Add(new TrRmSection(0x0010, 0, 1, moduleRefBytes));
            sections.Add(new TrRmSection(0x001C, 0, 1, importBytes));
        }
        sections.Sort(static (left, right) => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static int GetSectionOffset(byte[] artifact, ushort sectionKind)
    {
        var entryCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            artifact.AsSpan(12)));
        for (var entry = 0; entry < entryCount; entry++)
        {
            var entryOffset = 32 + (entry * 16);
            if (BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(entryOffset))
                == sectionKind)
            {
                return checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    artifact.AsSpan(entryOffset + 8)));
            }
        }

        throw new InvalidDataException($"Missing TRRM section 0x{sectionKind:X4}.");
    }

    private static uint GetBuiltinTypeSpecReference(PrimitiveType type)
    {
        for (var typeCode = 0; typeCode <= 0x19; typeCode++)
        {
            if (TrRmConstantSourceValueValidator.TryGetBoundScalarType(
                    (byte)typeCode, out var primitiveType)
                && primitiveType == type)
            {
                return 0xF200_0000u | (uint)typeCode;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(type));
    }
}
