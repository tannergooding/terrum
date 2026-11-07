using System.Threading.Tasks;

namespace Terrum.CodeAnalysis.Tests.Native.Jit;

internal sealed partial class JitTests
{
    internal static Task RunFailFastAsync(string source, int expected) =>
        AssertFailFastExecutionAsync(source, expected);

    internal static Task RunCheckedAsync(string source, int expected) =>
        AssertCheckedExecutionAsync(source, expected);
}
