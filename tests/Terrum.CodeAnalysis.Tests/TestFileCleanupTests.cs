using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class TestFileCleanupTests
{
    [Test]
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
        Assert.That(waitedForLock, Is.True);
        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public async Task DeleteDirectoryWithRetryWaitsForFileLockToClearAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), $"test-directory-cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "locked.tmp");
        await File.WriteAllBytesAsync(path, Array.Empty<byte>()).ConfigureAwait(true);
        Task deletion;
        bool waitedForLock;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            deletion = TestFileCleanup.DeleteDirectoryWithRetryAsync(directory);
            await Task.Delay(125).ConfigureAwait(true);
            waitedForLock = !deletion.IsCompleted;
        }

        await deletion.ConfigureAwait(true);
        Assert.That(waitedForLock, Is.True);
        Assert.That(Directory.Exists(directory), Is.False);
    }
}
