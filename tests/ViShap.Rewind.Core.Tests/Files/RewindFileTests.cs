using System.Text;
using System.Transactions;
using ViShap.Rewind.Tests.Fixtures;

namespace ViShap.Rewind.Tests.Files;

public sealed class RewindFileTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void WriteAllText_InScope_ReplacesAtOnce()
    {
        var path = Existing("settings.json", "old");
        string? seen = null;

        Rewind.Run(() =>
        {
            RewindFile.WriteAllText(path, "new");
            seen = File.ReadAllText(path);
        });

        Assert.Equal("new", seen);
    }

    [Fact]
    public void WriteAllText_InScopeRolledBack_RestoresPreviousContent()
    {
        var path = Existing("settings.json", "old");

        RollBack(() => RewindFile.WriteAllText(path, "new"));

        Assert.Equal("old", File.ReadAllText(path));
        Assert.Empty(_directory.SideFiles());
    }

    [Fact]
    public void WriteAllText_InScopeCommitted_KeepsContentAndLeavesNoSideFile()
    {
        var path = Existing("settings.json", "old");

        Rewind.Run(() => RewindFile.WriteAllText(path, "new"));

        Assert.Equal("new", File.ReadAllText(path));
        Assert.Equal(new[] { "settings.json" }, _directory.Files());
    }

    [Fact]
    public void WriteAllBytes_CreatesFileInScopeRolledBack_RemovesIt()
    {
        var path = _directory.File("data.bin");

        RollBack(() =>
        {
            RewindFile.WriteAllBytes(path, [1, 2, 3]);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        });

        Assert.Empty(_directory.Files());
    }

    [Fact]
    public void Delete_InScope_RemovesAtOnceRollbackRestores()
    {
        var path = Existing("report.txt", "content");

        RollBack(() =>
        {
            RewindFile.Delete(path);
            Assert.False(File.Exists(path));
        });

        Assert.Equal("content", File.ReadAllText(path));
        Assert.Equal(new[] { "report.txt" }, _directory.Files());
    }

    [Fact]
    public void Delete_InScopeCommitted_LeavesNoSideFile()
    {
        var path = Existing("report.txt", "content");

        Rewind.Run(() => RewindFile.Delete(path));

        Assert.Empty(_directory.Files());
    }

    [Fact]
    public void Delete_MissingFile_DoesNothing()
    {
        var path = _directory.File("missing.txt");

        Rewind.Run(() => RewindFile.Delete(path));
        RollBack(() => RewindFile.Delete(path));
        RewindFile.Delete(path);

        Assert.Empty(_directory.Files());
    }

    [Fact]
    public void Copy_InScopeRolledBack_RestoresDestination()
    {
        var source = Existing("source.txt", "source");
        var existing = Existing("existing.txt", "old");
        var created = _directory.File("created.txt");

        RollBack(() =>
        {
            RewindFile.Copy(source, existing, overwrite: true);
            RewindFile.Copy(source, created);
            Assert.Equal("source", File.ReadAllText(existing));
            Assert.Equal("source", File.ReadAllText(created));
        });

        Assert.Equal("old", File.ReadAllText(existing));
        Assert.Equal(new[] { "existing.txt", "source.txt" }, _directory.Files());
    }

    [Fact]
    public void Copy_WithoutOverwriteOntoExistingFile_ThrowsAndChangesNothing()
    {
        var source = Existing("source.txt", "source");
        var existing = Existing("existing.txt", "old");

        Assert.Throws<IOException>(() => RewindFile.Copy(source, existing));
        Assert.Throws<IOException>(() => Rewind.Run(() => RewindFile.Copy(source, existing, overwrite: false)));

        Assert.Equal("old", File.ReadAllText(existing));
        Assert.Equal(new[] { "existing.txt", "source.txt" }, _directory.Files());
    }

    [Fact]
    public void SeveralWrites_RolledBack_RestoreStartNewestFirst()
    {
        var path = Existing("state.txt", "a");

        RollBack(() =>
        {
            RewindFile.WriteAllText(path, "b");
            RewindFile.WriteAllText(path, "c");
            RewindFile.Delete(path);
            RewindFile.WriteAllText(path, "d");
            Assert.Equal("d", File.ReadAllText(path));
        });

        Assert.Equal("a", File.ReadAllText(path));
        Assert.Equal(new[] { "state.txt" }, _directory.Files());
    }

    [Fact]
    public void NestedScope_RolledBack_RestoresOnlyItsOwnWrite()
    {
        var path = Existing("state.txt", "a");
        string? afterChild = null;

        Rewind.Run(() =>
        {
            RewindFile.WriteAllText(path, "b");
            RollBack(() => RewindFile.WriteAllText(path, "c"));
            afterChild = File.ReadAllText(path);
        });

        Assert.Equal("b", afterChild);
        Assert.Equal("b", File.ReadAllText(path));
        Assert.Equal(new[] { "state.txt" }, _directory.Files());
    }

    [Fact]
    public void NestedScopeCommitted_OuterRolledBack_RestoresStart()
    {
        var path = Existing("state.txt", "a");

        RollBack(() =>
        {
            RewindFile.WriteAllText(path, "b");
            Rewind.Run(() => RewindFile.WriteAllText(path, "c"));
            Assert.Equal("c", File.ReadAllText(path));
        });

        Assert.Equal("a", File.ReadAllText(path));
        Assert.Equal(new[] { "state.txt" }, _directory.Files());
    }

    [Fact]
    public void Write_CallbackThrows_TargetUnchangedAndExceptionReachesCaller()
    {
        var path = Existing("state.txt", "a");
        var thrown = new InvalidOperationException();

        var withScope = Record.Exception(() => Rewind.Run(() => RewindFile.Write(path, stream =>
        {
            stream.WriteByte(1);
            throw thrown;
        })));
        var withoutScope = Record.Exception(() => RewindFile.Write(path, _ => throw thrown));

        Assert.Same(thrown, withScope);
        Assert.Same(thrown, withoutScope);
        Assert.Equal("a", File.ReadAllText(path));
        Assert.Equal(new[] { "state.txt" }, _directory.Files());
    }

    [Fact]
    public void WriteMethods_ProduceSameBytesAsFile()
    {
        var expected = _directory.File("expected");
        var actual = Existing("actual", "previous content that is longer than every one written below");
        byte[] bytes = [0, 1, 2, 254, 255];

        AssertSameBytes(() => File.WriteAllText(expected, "héllo"), () => RewindFile.WriteAllText(actual, "héllo"));
        AssertSameBytes(() => File.WriteAllText(expected, "héllo", Encoding.UTF8), () => RewindFile.WriteAllText(actual, "héllo", Encoding.UTF8));
        AssertSameBytes(() => File.WriteAllText(expected, "héllo", Encoding.Unicode), () => Rewind.Run(() => RewindFile.WriteAllText(actual, "héllo", Encoding.Unicode)));
        AssertSameBytes(() => File.WriteAllText(expected, null), () => RewindFile.WriteAllText(actual, null));
        AssertSameBytes(() => File.WriteAllBytes(expected, bytes), () => Rewind.Run(() => RewindFile.WriteAllBytes(actual, bytes)));
        AssertSameBytes(() => File.WriteAllBytes(expected, bytes), () => RewindFile.Write(actual, stream => stream.Write(bytes)));

        void AssertSameBytes(Action file, Action rewindFile)
        {
            file();
            rewindFile();
            Assert.Equal(File.ReadAllBytes(expected), File.ReadAllBytes(actual));
        }
    }

    [Fact]
    public void Operations_WithNoScope_ArePlainAndLeaveNoSideFile()
    {
        var path = Existing("state.txt", "a");
        var created = _directory.File("created.txt");
        var copy = _directory.File("copy.txt");

        RewindFile.WriteAllText(path, "b");
        RewindFile.WriteAllBytes(created, [1]);
        RewindFile.Copy(path, copy);
        RewindFile.WriteAllText(path, "c");
        RewindFile.Copy(path, copy, overwrite: true);
        RewindFile.Delete(created);

        Assert.Null(Rewind.Current);
        Assert.Equal("c", File.ReadAllText(path));
        Assert.Equal("c", File.ReadAllText(copy));
        Assert.Equal(new[] { "copy.txt", "state.txt" }, _directory.Files());
    }

    [Fact]
    public void WriteAllText_InAfterCommitEffect_IsPlainWrite()
    {
        var path = Existing("state.txt", "a");

        Rewind.Run(scope => scope.AfterCommit(() => RewindFile.WriteAllText(path, "after commit")));

        Assert.Equal("after commit", File.ReadAllText(path));
        Assert.Equal(new[] { "state.txt" }, _directory.Files());
    }

    [Fact]
    public void WriteAllText_DirectoryMissing_ThrowsAndLeavesNothing()
    {
        var path = Path.Join(_directory.Path, "missing", "state.txt");

        Assert.Throws<DirectoryNotFoundException>(() => Rewind.Run(() => RewindFile.WriteAllText(path, "a")));
        Assert.Throws<DirectoryNotFoundException>(() => RewindFile.WriteAllText(path, "a"));

        Assert.Empty(Directory.GetFileSystemEntries(_directory.Path));
    }

    [Fact]
    public void Operations_InvalidArguments_ThrowBeforeTouchingDisk()
    {
        var path = _directory.File("state.txt");

        Rewind.Run(() =>
        {
            Assert.Throws<ArgumentNullException>(() => RewindFile.WriteAllText(null!, "a"));
            Assert.Throws<ArgumentException>(() => RewindFile.WriteAllText("", "a"));
            Assert.Throws<ArgumentException>(() => RewindFile.WriteAllText(path + "\0", "a"));
            Assert.Throws<ArgumentNullException>(() => RewindFile.WriteAllText(path, "a", null!));
            Assert.Throws<ArgumentNullException>(() => RewindFile.WriteAllBytes(path, null!));
            Assert.Throws<ArgumentNullException>(() => RewindFile.Write(path, null!));
            Assert.Throws<ArgumentNullException>(() => RewindFile.Copy(null!, path));
            Assert.Throws<ArgumentException>(() => RewindFile.Copy(path, ""));
            Assert.Throws<ArgumentNullException>(() => RewindFile.Delete(null!));
            Assert.Throws<FileNotFoundException>(() => RewindFile.Copy(_directory.File("missing.txt"), path));
        });

        Assert.Empty(_directory.Files());
    }

    [Fact]
    public void WriteAllText_InAmbientTransaction_FollowsItsOutcome()
    {
        var path = Existing("state.txt", "a");

        using (new TransactionScope())
        {
            Rewind.Run(() => RewindFile.WriteAllText(path, "b"));
            Assert.Equal("b", File.ReadAllText(path));
        }

        Assert.Equal("a", File.ReadAllText(path));
        Assert.Equal(new[] { "state.txt" }, _directory.Files());

        using (var transaction = new TransactionScope())
        {
            Rewind.Run(() => RewindFile.WriteAllText(path, "c"));
            Assert.Single(_directory.SideFiles());
            transaction.Complete();
        }

        Assert.Equal("c", File.ReadAllText(path));
        Assert.Equal(new[] { "state.txt" }, _directory.Files());
    }

    [Fact]
    public void WriteAllText_IsNotInChangeSetNorUndoneByHistory()
    {
        var path = Existing("state.txt", "a");
        var cell = new RewindCell<int>(0);
        var history = new RewindHistory();
        ChangeSet? changes = null;

        Rewind.Run(scope =>
        {
            scope.OnCommitted(observed => changes = observed);
            RewindFile.WriteAllText(path, "b");
        });
        history.Run(() =>
        {
            cell.Value = 1;
            RewindFile.WriteAllText(path, "c");
        });
        history.Undo();

        Assert.NotNull(changes);
        Assert.True(changes.IsEmpty);
        Assert.Equal(0, cell.Value);
        Assert.Equal("c", File.ReadAllText(path));
        Assert.Equal(new[] { "state.txt" }, _directory.Files());
    }

    [Fact]
    public void Rollback_RestorationFails_CollectedAndOthersStillRun()
    {
        var path = Existing("state.txt", "a");
        var thrown = new InvalidOperationException();
        var compensated = false;

        var exception = Assert.Throws<RewindCompensationException>(() => Rewind.Run(scope =>
        {
            scope.Effect(() => { }, () => compensated = true);
            RewindFile.WriteAllText(path, "b");
#pragma warning disable RWD1002 // The test removes the kept file to make its restoration fail.
            File.Delete(_directory.File(Assert.Single(_directory.SideFiles())));
#pragma warning restore RWD1002
            throw thrown;
        }));

        Assert.Same(thrown, exception.OriginalException);
        Assert.IsType<FileNotFoundException>(Assert.Single(exception.Failures));
        Assert.True(compensated);
        Assert.Equal("b", File.ReadAllText(path));
    }

    [Fact]
    public void WriteAllText_TargetHeldOpen_FollowsPlatform()
    {
        var path = Existing("state.txt", "a");

        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var exception = Record.Exception(() => Rewind.Run(() => RewindFile.WriteAllText(path, "b")));

            if (OperatingSystem.IsWindows())
            {
                Assert.IsAssignableFrom<IOException>(exception);
            }
            else
            {
                Assert.Null(exception);
                Assert.Equal((byte)'a', reader.ReadByte());
            }
        }

        Assert.Equal(OperatingSystem.IsWindows() ? "a" : "b", File.ReadAllText(path));
        Assert.Equal(new[] { "state.txt" }, _directory.Files());
    }

    [Fact]
    public void WriteAllText_OnUnix_KeepsFileMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var path = Existing("state.txt", "a");
        File.SetUnixFileMode(path, mode);

        Rewind.Run(() => RewindFile.WriteAllText(path, "b"));
        Assert.Equal(mode, File.GetUnixFileMode(path));

        RewindFile.WriteAllText(path, "c");
        Assert.Equal(mode, File.GetUnixFileMode(path));
    }

    [Fact]
    public void WriteAllText_ThroughSymbolicLink_WritesTheLinkedFileAndKeepsTheLink()
    {
        var real = Existing("real.json", "old");
        if (Link("link.json", real) is not { } link)
        {
            return;
        }

        Rewind.Run(() => RewindFile.WriteAllText(link, "new"));

        Assert.Equal("new", File.ReadAllText(real));
        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Empty(_directory.SideFiles());
    }

    [Fact]
    public void WriteAllText_ThroughSymbolicLinkRolledBack_LinkedFileRestoredLinkKept()
    {
        var real = Existing("real.json", "old");
        if (Link("link.json", real) is not { } link)
        {
            return;
        }

        RollBack(() => RewindFile.WriteAllText(link, "new"));

        Assert.Equal("old", File.ReadAllText(real));
        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Empty(_directory.SideFiles());
    }

    [Fact]
    public void WriteAllText_ThroughSymbolicLinkWithNoScope_WritesTheLinkedFile()
    {
        var real = Existing("real.json", "old");
        if (Link("link.json", real) is not { } link)
        {
            return;
        }

        RewindFile.WriteAllText(link, "new");

        Assert.Equal("new", File.ReadAllText(real));
        Assert.NotNull(new FileInfo(link).LinkTarget);
    }

    [Fact]
    public void Copy_OverSymbolicLink_ReplacesTheLinkedFile()
    {
        var real = Existing("real.json", "old");
        var source = Existing("source.json", "copied");
        if (Link("link.json", real) is not { } link)
        {
            return;
        }

        Rewind.Run(() => RewindFile.Copy(source, link, overwrite: true));

        Assert.Equal("copied", File.ReadAllText(real));
        Assert.NotNull(new FileInfo(link).LinkTarget);
    }

    [Fact]
    public void Delete_SymbolicLinkRolledBack_TheLinkComesBackAndTheLinkedFileIsUntouched()
    {
        var real = Existing("real.json", "old");
        if (Link("link.json", real) is not { } link)
        {
            return;
        }

        RollBack(() => RewindFile.Delete(link));

        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Equal("old", File.ReadAllText(real));
    }

    /// <summary>A symbolic link to <paramref name="target"/>, or <see langword="null"/> where the system does not let this user create one.</summary>
    private string? Link(string name, string target)
    {
        var link = _directory.File(name);
        try
        {
            File.CreateSymbolicLink(link, target);
            return link;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string Existing(string name, string content)
    {
        var path = _directory.File(name);
        File.WriteAllText(path, content);
        return path;
    }

    private static void RollBack(Action block) =>
        Assert.Throws<RolledBack>(() => Rewind.Run(() =>
        {
            block();
            throw new RolledBack();
        }));

    private sealed class RolledBack : Exception;
}
