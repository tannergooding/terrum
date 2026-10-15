// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Modules;

public static partial class ModuleCompiler
{
    // Provisional bounded semantic IR only; canonical TRRM (1, 0) reserved bits stay reserved.
    private const int FixedLayoutVersion = 78;
    private const int PrivateSpanFixedLayoutVersion = 81;

    private static Module SelectFixedLayoutRevision(Module module)
    {
        if (!module.Structures.Any(static structure => structure.BlittableTargetRequirement != 0))
        {
            return module;
        }
        if (module.FormatVersion == PrivateSpanVersion)
        {
            return module with { FormatVersion = PrivateSpanFixedLayoutVersion };
        }
        if (module.FormatVersion >= DirectNativeImportVersion)
        {
            return module;
        }
        return module with { FormatVersion = FixedLayoutVersion };
    }

    private static void ValidateFixedLayoutModule(Module module)
    {
        var hasCommitments = module.Structures.Any(static structure => structure.BlittableTargetRequirement != 0);
        if (hasCommitments && module.FormatVersion < FixedLayoutVersion
            || !hasCommitments && module.FormatVersion == FixedLayoutVersion)
        {
            throw new ModuleFormatException("Explicit fixed-storage commitments require semantic-module revision 78.");
        }
        if (!hasCommitments)
        {
            return;
        }
        if (module.Structures.Any(static structure => structure.IsSequential
            && structure.BlittableTargetRequirement == 0
            && (!structure.IsInternal || structure.Fields.Any(static field => !field.IsPrivate))))
        {
            throw new ModuleFormatException("Public Sequential metadata requires an explicit fixed-storage commitment.");
        }
        try
        {
            _ = FixedLayoutStorage.Validate(module.Structures);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new ModuleFormatException("Module fixed-storage facts do not match their explicit commitment.", exception);
        }
    }
}
