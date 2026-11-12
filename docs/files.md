# Files

`RewindFile` writes, copies and deletes files so that they follow the outcome of the scope they are changed in: a
rollback puts every file back as it was, and a commit keeps the change. Its methods are named and used like those of
`System.IO.File`.

| Method | What it does |
|---|---|
| `WriteAllText(path, contents)`, `WriteAllText(path, contents, encoding)` | Replaces the file's content with text (UTF-8 without a byte order mark by default) |
| `WriteAllBytes(path, bytes)` | Replaces the file's content with bytes |
| `Write(path, write)` | Replaces the file's content with what `write` writes to a `Stream` — for a serializer |
| `Copy(source, destination)`, `Copy(source, destination, overwrite)` | Copies a file; without `overwrite` the destination must not exist |
| `Delete(path)` | Deletes a file; a missing file is not an error |

## In a scope

The file changes at the call: the rest of the block reads the new content. The previous file is kept beside it, and
put back if the scope — or any scope around it — rolls back. Once the outermost scope commits, the kept file is
deleted.

```csharp
using System.Text.Json;
using ViShap.Rewind;

var path = Path.Join(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
File.WriteAllText(path, """{"Theme":"light"}""");

try
{
    Rewind.Run(() =>
    {
        RewindFile.Write(path, stream => JsonSerializer.Serialize(stream, new { Theme = "dark" }));
        Console.WriteLine(File.ReadAllText(path));   // {"Theme":"dark"}
        throw new InvalidOperationException("Validation failed");
    });
}
catch (InvalidOperationException)
{
}

Console.WriteLine(File.ReadAllText(path));       // {"Theme":"light"}

Rewind.Run(() => RewindFile.WriteAllText(path, """{"Theme":"dark"}"""));
Console.WriteLine(File.ReadAllText(path));       // {"Theme":"dark"}

File.Delete(path);
```

Several operations on one file in one scope are undone newest first, so a rollback returns the file to what it was
when the scope opened. A nested scope that rolls back restores only what it did itself. In a `System.Transactions`
transaction ([transactions.md](transactions.md)) the files follow the transaction like the rest of the scope.

A file operation is an effect, like `scope.Effect`: it does not appear in a `ChangeSet`, and `RewindHistory.Undo`
does not restore the file ([changes-and-history.md](changes-and-history.md)). After an undo, save the file again if it
mirrors the state.

## With no scope

With no scope open — also inside `AfterCommit` and `AfterRollback` effects — an operation is a plain one, and nothing
is kept for a rollback. The file is still written beside the target and renamed over it, so a reader never sees it
half written.

```csharp
using ViShap.Rewind;

var path = Path.Join(Path.GetTempPath(), $"report-{Guid.NewGuid():N}.txt");
var total = new RewindCell<decimal>(0m);

Rewind.Run(scope =>
{
    total.Value = 42m;
    scope.AfterCommit(() => RewindFile.WriteAllText(path, $"Total: {total.Value}"));
});

Console.WriteLine(File.ReadAllText(path));       // Total: 42

RewindFile.Delete(path);
Console.WriteLine(File.Exists(path));            // False
```

## When an operation fails

Arguments are checked first. If the new content cannot be produced — the directory does not exist, the source of
a copy is missing, the callback of `Write` throws — the exception reaches the caller unchanged and the target is
untouched. In a scope, the scope then rolls back as for any exception. If putting a kept file back fails during a
rollback, the failure is collected like any compensation's, in `RewindCompensationException`
([exceptions.md](exceptions.md)).

## Side files

While it works, `RewindFile` uses files next to the target, named after it:

- `.<file name>.<id>.rewind-tmp` — new content being written; it never outlives the call;
- `.<file name>.<id>.rewind-bak` — the previous file, kept until the outermost scope commits or rolls back.

A `.rewind-bak` file stays behind when the process ends while a scope is open, when deleting it after the commit fails
(the commit then throws `RewindEffectException`), and when a transaction the scope joined ends in doubt. It holds the
file as it was before the scope; it is safe to delete once you no longer need it.

The directory must allow creating and renaming files. Rollback does not isolate: other processes see a change from the
call on, and see it go back on rollback.

## Platforms

The guarantees are those of renaming a file within one directory.

- **Windows.** The target is replaced with `ReplaceFile`, which keeps its attributes and access control. A file that
  another program holds open without allowing deletion cannot be replaced: the operation throws `IOException` at the
  call and the file is unchanged. Putting a file back during a rollback can fail the same way.
- **Linux and macOS.** The previous file is kept as a hard link (a copy where the file system has no hard links), and
  the new file is renamed over the target in one step, so the file name never disappears. The new file takes the
  previous file's permissions. A program that already has the file open keeps reading the old content.
