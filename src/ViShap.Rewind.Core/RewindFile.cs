using System.Text;

namespace ViShap.Rewind;

/// <summary>
/// File operations bound to the outcome of the current scope. Inside a scope an operation changes the file at the call
/// and keeps the previous file aside in the same directory: a rollback of the scope, or of a scope enclosing it, puts
/// the previous file back; the commit of the outermost scope deletes the kept file. With no current scope an operation
/// is a plain one.
/// </summary>
/// <remarks>
/// <para>
/// New content is written to a side file in the target's directory and then renamed over the target, so the target
/// never holds partly written content. Side files are named <c>.&lt;file name&gt;.&lt;id&gt;.rewind-tmp</c> (content
/// being written) and <c>.&lt;file name&gt;.&lt;id&gt;.rewind-bak</c> (a previous file kept for rollback). A kept file
/// outlives the scope when the process ends while the scope is open, when its deletion after the commit fails, and
/// when an ambient transaction the scope joined ends in doubt.
/// </para>
/// <para>
/// A change is visible to other processes from the call on. A file operation is an effect, not tracked state: it does
/// not appear in a <see cref="ChangeSet"/>, and <see cref="RewindHistory"/> neither undoes nor redoes it.
/// </para>
/// </remarks>
public static class RewindFile
{
    private const int MaxNameInSideName = 128;

    private static readonly UTF8Encoding s_utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Replaces the content of a file with <paramref name="bytes"/>, creating the file if it does not exist.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="bytes">The new content.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="bytes"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or not a valid path.</exception>
    /// <exception cref="IOException">The file could not be written or replaced; the target is unchanged.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller has no permission to write the file or its directory; the target is unchanged.</exception>
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(bytes);
        Install(path, bytes, static (stream, bytes) => stream.Write(bytes));
    }

    /// <summary>
    /// Replaces the content of a file with <paramref name="contents"/> encoded as UTF-8 without a byte order mark,
    /// creating the file if it does not exist.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="contents">The new content; <see langword="null"/> writes an empty file.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or not a valid path.</exception>
    /// <exception cref="IOException">The file could not be written or replaced; the target is unchanged.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller has no permission to write the file or its directory; the target is unchanged.</exception>
    public static void WriteAllText(string path, string? contents) => WriteAllText(path, contents, s_utf8);

    /// <summary>
    /// Replaces the content of a file with <paramref name="contents"/> encoded with <paramref name="encoding"/>, preceded
    /// by its preamble, creating the file if it does not exist.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="contents">The new content; <see langword="null"/> writes only the preamble.</param>
    /// <param name="encoding">The encoding of the content.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="encoding"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or not a valid path.</exception>
    /// <exception cref="IOException">The file could not be written or replaced; the target is unchanged.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller has no permission to write the file or its directory; the target is unchanged.</exception>
    public static void WriteAllText(string path, string? contents, Encoding encoding)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(encoding);
        Install(path, (contents, encoding), static (stream, state) =>
        {
            using var writer = new StreamWriter(stream, state.encoding, leaveOpen: true);
            writer.Write(state.contents);
        });
    }

    /// <summary>
    /// Replaces the content of a file with what <paramref name="write"/> writes to the stream it receives, creating the
    /// file if it does not exist. The target changes only once <paramref name="write"/> has returned.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="write">Writes the new content to the stream, which is positioned at its start and is empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="write"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or not a valid path.</exception>
    /// <exception cref="IOException">The file could not be written or replaced; the target is unchanged.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller has no permission to write the file or its directory; the target is unchanged.</exception>
    /// <remarks>An exception thrown by <paramref name="write"/> reaches the caller unchanged, and the target is unchanged.</remarks>
    public static void Write(string path, Action<Stream> write)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(write);
        Install(path, write, static (stream, write) => write(stream));
    }

    /// <summary>Copies a file to a destination that must not exist.</summary>
    /// <param name="sourceFileName">The file to copy.</param>
    /// <param name="destFileName">The file to create.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sourceFileName"/> or <paramref name="destFileName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A path is empty or not a valid path.</exception>
    /// <exception cref="FileNotFoundException"><paramref name="sourceFileName"/> does not exist.</exception>
    /// <exception cref="IOException"><paramref name="destFileName"/> exists, or the file could not be copied; the destination is unchanged.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller has no permission to read the source or write the destination; the destination is unchanged.</exception>
    public static void Copy(string sourceFileName, string destFileName) => Copy(sourceFileName, destFileName, overwrite: false);

    /// <summary>Copies a file to a destination, replacing the destination when <paramref name="overwrite"/> allows it.</summary>
    /// <param name="sourceFileName">The file to copy.</param>
    /// <param name="destFileName">The file to create or replace.</param>
    /// <param name="overwrite"><see langword="true"/> to replace an existing destination; <see langword="false"/> to refuse it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sourceFileName"/> or <paramref name="destFileName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A path is empty or not a valid path.</exception>
    /// <exception cref="FileNotFoundException"><paramref name="sourceFileName"/> does not exist.</exception>
    /// <exception cref="IOException">
    /// <paramref name="destFileName"/> exists and <paramref name="overwrite"/> is <see langword="false"/>, or the file could
    /// not be copied; the destination is unchanged.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">The caller has no permission to read the source or write the destination; the destination is unchanged.</exception>
    public static void Copy(string sourceFileName, string destFileName, bool overwrite)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceFileName);
        ArgumentException.ThrowIfNullOrEmpty(destFileName);
        var target = Path.GetFullPath(destFileName);
        var temp = SideName(target, "tmp");
        try
        {
            File.Copy(sourceFileName, temp);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        Replace(temp, target, overwrite, keepMode: false);
    }

    /// <summary>Deletes a file. A file that does not exist is not an error.</summary>
    /// <param name="path">The file to delete.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or not a valid path.</exception>
    /// <exception cref="IOException">The file is in use or could not be moved aside; it is unchanged.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller has no permission to delete the file, or the path is a directory; it is unchanged.</exception>
    public static void Delete(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var target = Path.GetFullPath(path);
        var scope = ScopeContext.Current;
        if (scope is null || !File.Exists(target))
        {
            File.Delete(target);
            return;
        }

        var backup = SideName(target, "bak");
        File.Move(target, backup);
        Keep(scope, target, backup);
    }

    private static void Install<TState>(string path, TState state, Action<Stream, TState> write)
    {
        var target = Path.GetFullPath(path);
        var temp = SideName(target, "tmp");
        try
        {
            using var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            write(stream, state);
            if (stream.CanWrite)
            {
                stream.Flush(flushToDisk: true);
            }
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        Replace(temp, target, overwrite: true, keepMode: true);
    }

    /// <summary>
    /// Renames <paramref name="temp"/> over <paramref name="target"/>. In a scope the previous target is kept aside and
    /// put back on rollback, and a target that did not exist is deleted on rollback. <paramref name="temp"/> never
    /// outlives the call.
    /// </summary>
    private static void Replace(string temp, string target, bool overwrite, bool keepMode)
    {
        var scope = ScopeContext.Current;
        try
        {
            if (!File.Exists(target))
            {
                File.Move(temp, target, overwrite: false);
                scope?.Journal.Add(new CompensationEntry(() => File.Delete(target)));
                return;
            }

            if (!overwrite)
            {
                throw new IOException($"The file '{target}' already exists.");
            }

            if (keepMode && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temp, File.GetUnixFileMode(target));
            }

            if (scope is null)
            {
                File.Replace(temp, target, null);
                return;
            }

            var backup = SideName(target, "bak");
            ReplaceKeeping(temp, target, backup);
            Keep(scope, target, backup);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>
    /// Replaces <paramref name="target"/> with <paramref name="temp"/>, the previous target becoming
    /// <paramref name="backup"/>. When the replacement fails after the target was moved aside, the target is moved back
    /// and the replacement's exception is the one thrown.
    /// </summary>
    private static void ReplaceKeeping(string temp, string target, string backup)
    {
        try
        {
            File.Replace(temp, target, backup);
        }
        catch
        {
            if (!File.Exists(target) && File.Exists(backup))
            {
                try
                {
                    File.Move(backup, target);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }

            throw;
        }
    }

    /// <summary>Records how to put <paramref name="backup"/> back over <paramref name="target"/>, and deletes it after the outermost commit.</summary>
    private static void Keep(RewindScope scope, string target, string backup)
    {
        scope.Journal.Add(new CompensationEntry(() => File.Move(backup, target, overwrite: true)));
        scope.AfterCommit(() => File.Delete(backup));
    }

    private static string SideName(string target, string kind)
    {
        var name = Path.GetFileName(target);
        if (name.Length > MaxNameInSideName)
        {
            name = name[..MaxNameInSideName];
        }

        return Path.Join(Path.GetDirectoryName(target), $".{name}.{Guid.NewGuid():N}.rewind-{kind}");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
