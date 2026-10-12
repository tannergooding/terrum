using System;
using System.IO;
using System.Threading.Tasks;

namespace Terrum.CodeAnalysis.Tests;

internal static class TestFileCleanup
{
    private const int MaximumRetries = 20;
    private const int RetryDelayMilliseconds = 50;

    public static async Task DeleteWithRetryAsync(string path)
    {
        // Allow Windows up to one second to release an executed image before surfacing cleanup failures.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) when (attempt < MaximumRetries)
            {
            }
            catch (UnauthorizedAccessException) when (attempt < MaximumRetries)
            {
            }

            await Task.Delay(RetryDelayMilliseconds).ConfigureAwait(false);
        }
    }
}
