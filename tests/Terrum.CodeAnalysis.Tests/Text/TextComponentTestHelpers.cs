using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Tests.Text;

internal static class TextComponentTestHelpers
{
    internal static PrivateModuleIdentityInputs CreateIdentityInputs(
        BoundProgram program, string moduleIdentity)
    {
        var definitions = new PrivateNominalDefinition[
            program.Structures.Length + program.Classes.Length];
        for (var index = 0; index < program.Structures.Length; index++)
        {
            var structure = program.Structures[index];
            definitions[index] = new PrivateNominalDefinition(
                (PrimitiveType)(100 + index), $"types.{structure.Name}",
                !structure.IsPrivate && !structure.IsInternal);
        }
        for (var index = 0; index < program.Classes.Length; index++)
        {
            var @class = program.Classes[index];
            definitions[program.Structures.Length + index]
                = new PrivateNominalDefinition(
                    (PrimitiveType)(ManagedClassTypes.First + index),
                    $"classes.{@class.Name}", !@class.IsInternal);
        }

        return PrivateModuleIdentityInputs.Create(program, moduleIdentity,
            definitions, []);
    }
}
