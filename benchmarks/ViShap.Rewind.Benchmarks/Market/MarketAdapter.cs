namespace ViShap.Rewind.Benchmarks.Market;

/// <summary>
/// What a library may or may not do for the T1 job — undo in-memory changes on one thread when a block fails. A
/// probe decides each one for each adapter on the day of the run; the adapter's declaration is checked against it.
/// </summary>
internal enum Capability
{
    /// <summary>A block that fails leaves every value it wrote as it was before the block.</summary>
    Rollback,

    /// <summary>A value can be written with no transaction open, as a plain write.</summary>
    WriteOutsideTransaction,

    /// <summary>
    /// A nested block that fails is undone while its parent goes on and commits; a parent that fails undoes the
    /// nested blocks that completed inside it.
    /// </summary>
    NestedRollback,

    /// <summary>A list changed by a block that fails holds what it held before the block.</summary>
    CollectionRollback,

    /// <summary>An action registered in a block runs after the block commits, and never when it fails.</summary>
    CommitEffect,
}

/// <summary>The failure every adapter's failing block throws: one instance, so no adapter pays for constructing it.</summary>
internal sealed class MarketFailure : Exception
{
    internal static readonly MarketFailure Instance = new();

    private MarketFailure()
        : base("The block failed on purpose.")
    {
    }
}

/// <summary>
/// One library in its own documented best mode, doing the work of every Track B workload. The arguments of a block
/// are passed through fields and the block is a delegate built once, so no adapter pays for a closure another one is
/// spared.
/// </summary>
/// <remarks>
/// An operation the library has no mechanism for throws <see cref="NotSupportedException"/> with the reason; an
/// operation it has a mechanism for does what that mechanism does, and the probes say whether that is the job.
/// </remarks>
internal abstract class MarketAdapter
{
    /// <summary>The short name a table row and a benchmark parameter carry.</summary>
    internal abstract string Name { get; }

    /// <summary>The package, or the technique for a row with no package.</summary>
    internal abstract string Library { get; }

    /// <summary>The API used and over what state.</summary>
    internal abstract string Configuration { get; }

    /// <summary>The version measured, as the process loaded it.</summary>
    internal abstract string Version { get; }

    /// <summary>Where the configuration is documented.</summary>
    internal abstract string Documentation { get; }

    /// <summary>Why this is the library's best documented mode for the job, and what it does not do.</summary>
    internal abstract string Rationale { get; }

    /// <summary>What the adapter declares it can do; the probes check the declaration.</summary>
    internal abstract IReadOnlySet<Capability> Declared { get; }

    /// <summary>Whether isolation between threads is part of the library, which T1 does not measure.</summary>
    internal abstract bool Isolates { get; }

    /// <summary>True for the floor: plain state, no rollback — the cost of doing nothing.</summary>
    internal virtual bool IsFloor => false;

    /// <summary>The number of deferred actions that have run.</summary>
    internal int EffectsRun;

    /// <summary>Creates <paramref name="cells"/> integer cells holding zero.</summary>
    internal abstract void Prepare(int cells);

    /// <summary>Reads a cell with no transaction, for verification.</summary>
    internal abstract int Get(int index);

    /// <summary>In one transaction, writes <paramref name="value"/> to the first <paramref name="writes"/> cells; commits.</summary>
    internal abstract void Commit(int writes, int value);

    /// <summary>In one transaction, writes as <see cref="Commit"/> does, then throws <see cref="MarketFailure"/>.</summary>
    internal abstract void Fail(int writes, int value);

    /// <summary>In one transaction, reads the first <paramref name="reads"/> cells and returns their sum.</summary>
    internal abstract int Read(int reads);

    /// <summary>Writes the first cell with no transaction open.</summary>
    internal virtual void WriteOutside(int value) =>
        throw new NotSupportedException("The library has no write outside a transaction.");

    /// <summary>
    /// Opens <paramref name="depth"/> nested transactions; level i writes <paramref name="value"/> to cell i. The level
    /// <paramref name="failLevel"/> throws <see cref="MarketFailure"/> after its children ran; a parent catches the
    /// failure of its innermost child, and the failure of level 0 reaches the caller.
    /// </summary>
    internal virtual void Nested(int depth, int failLevel, int value) =>
        throw new NotSupportedException("The library has no nested transaction.");

    /// <summary>Creates a list holding 0 … <paramref name="items"/> − 1.</summary>
    internal virtual void PrepareList(int items) =>
        throw new NotSupportedException("The library has no transactional list.");

    /// <summary>The number of items in the list, read with no transaction.</summary>
    internal virtual int ListCount() => throw new NotSupportedException("The library has no transactional list.");

    /// <summary>An item of the list, read with no transaction.</summary>
    internal virtual int ListItem(int index) => throw new NotSupportedException("The library has no transactional list.");

    /// <summary>In one transaction, clears the list and appends <paramref name="items"/> values from <paramref name="first"/>; commits.</summary>
    internal virtual void ListReplace(int items, int first) =>
        throw new NotSupportedException("The library has no transactional list.");

    /// <summary>In one transaction, appends <paramref name="items"/> values, then throws <see cref="MarketFailure"/>.</summary>
    internal virtual void ListAppendFail(int items) =>
        throw new NotSupportedException("The library has no transactional list.");

    /// <summary>
    /// In one transaction, writes the first cell and registers <paramref name="effects"/> actions that each count into
    /// <see cref="EffectsRun"/>; commits, or throws <see cref="MarketFailure"/> when <paramref name="fail"/> is set.
    /// </summary>
    internal virtual void WithEffects(int effects, int value, bool fail) =>
        throw new NotSupportedException("The library has no action deferred to the commit.");

    /// <summary>Whether a block whose library supports nothing more is leaving state behind it, checked after every probe.</summary>
    internal virtual string? Leak() => null;
}
