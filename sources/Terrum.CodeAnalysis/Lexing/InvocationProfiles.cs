using System.Collections.Immutable;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Lexing;

internal static class InvocationProfiles
{
    internal static bool IsBufferSet(ImmutableArray<HostBuffer> buffers, string? identity) =>
        ScannerHost.IsInvocationProfileBufferSet(buffers, identity)
        || ScalarFrontendHost.IsInvocationProfileBufferSet(buffers, identity);
}
