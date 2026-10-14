using System;
using System.IO;
using System.Threading.Tasks;

namespace Terrum.CodeAnalysis.Tests;

internal static class TestFileCleanup
{
    private const int MaximumRetries = 20;
    private const int RetryDelayMilliseconds = 50;

    public static Task DeleteWithRetryAsync(string path)
        => DeleteWithRetryAsync(() => File.Delete(path));

    public static Task DeleteDirectoryWithRetryAsync(string path)
        => DeleteWithRetryAsync(() => Directory.Delete(path, recursive: true));

    private static async Task DeleteWithRetryAsync(Action delete)
    {
        // Allow Windows up to one second to release an executed image before surfacing cleanup failures.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                delete();
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
