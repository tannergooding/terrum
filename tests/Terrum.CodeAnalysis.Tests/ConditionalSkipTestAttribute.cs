using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace Terrum.CodeAnalysis.Tests;

internal abstract class ConditionalSkipTestAttribute : TestAttribute, IApplyToTest
{
    protected abstract string? GetSkipReason();

    public new void ApplyToTest(Test test)
    {
        if (test.RunState != RunState.Runnable)
        {
            return;
        }
        var skipReason = GetSkipReason();
        if (skipReason is null)
        {
            return;
        }
        test.RunState = RunState.Ignored;
        test.Properties.Set(PropertyNames.SkipReason, skipReason);
    }
}
