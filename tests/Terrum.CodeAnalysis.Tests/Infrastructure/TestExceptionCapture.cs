using System;

namespace Terrum.CodeAnalysis.Tests.Infrastructure;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability",
    "CA1031:Do not catch general exception types",
    Justification = "Tests capture arbitrary thrown values for exact assertions.")]
internal static class TestExceptionCapture
{
    public static Exception? Capture(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
