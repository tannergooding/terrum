using System;

namespace Terrum.CodeAnalysis.Tests;

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
