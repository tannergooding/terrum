using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TestFileCleanupTests
{
    [Fact]
    public async Task DeleteWithRetryWaitsForFileLockToClearAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), $"test-file-cleanup-{Guid.NewGuid():N}.tmp");
        await File.WriteAllBytesAsync(path, Array.Empty<byte>()).ConfigureAwait(true);
        Task deletion;
        bool waitedForLock;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            deletion = TestFileCleanup.DeleteWithRetryAsync(path);
            await Task.Delay(125).ConfigureAwait(true);
            waitedForLock = !deletion.IsCompleted;
        }

        await deletion.ConfigureAwait(true);
        Assert.True(waitedForLock);
        Assert.False(File.Exists(path));
    }
}
