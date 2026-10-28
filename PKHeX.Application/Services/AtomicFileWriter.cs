namespace PKHeX.Application.Services;

/// <summary>
/// Writes a file so a crash, power loss or full disk can never leave it half-written: the bytes go to a
/// temporary file in the same folder, are flushed to disk and read back to verify them, and only then
/// replace the target with a rename. On any failure the original file is left untouched.
/// If the folder does not allow creating files but the target itself is writable, it falls back to an
/// in-place write that is still read back and verified, so saving keeps working where it did before.
/// </summary>
public static class AtomicFileWriter
{
    private const string TempSuffix = ".pkhex-tmp";

    public static void WriteAllBytes(string path, ReadOnlySpan<byte> data)
    {
        // Replacing a symlink with a rename would swap the link for a plain file; write through to its target.
        var target = ResolveTarget(path);
        var folder = Path.GetDirectoryName(Path.GetFullPath(target)) ?? ".";
        var tempPath = Path.Combine(folder, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}{TempSuffix}");

        FileStream tempStream;
        try
        {
            tempStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (UnauthorizedAccessException) when (File.Exists(target))
        {
            WriteInPlace(target, data);
            return;
        }

        try
        {
            using (tempStream)
            {
                tempStream.Write(data);
                tempStream.Flush(flushToDisk: true);
            }

            Verify(tempPath, target, data);

            // The rename replaces the file itself, so carry over the original's permissions.
            if (!OperatingSystem.IsWindows() && File.Exists(target))
                File.SetUnixFileMode(tempPath, File.GetUnixFileMode(target));

            File.Move(tempPath, target, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); }
            catch { /* the original error matters more than a leftover temp file */ }
            throw;
        }
    }

    private static void WriteInPlace(string target, ReadOnlySpan<byte> data)
    {
        using (var stream = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(data);
            stream.Flush(flushToDisk: true);
        }
        Verify(target, target, data);
    }

    private static void Verify(string writtenPath, string target, ReadOnlySpan<byte> data)
    {
        if (!File.ReadAllBytes(writtenPath).AsSpan().SequenceEqual(data))
            throw new IOException($"Verification failed: the bytes written to '{target}' did not read back identically.");
    }

    private static string ResolveTarget(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is null)
                return path;
            return info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        }
        catch
        {
            return path;
        }
    }
}
