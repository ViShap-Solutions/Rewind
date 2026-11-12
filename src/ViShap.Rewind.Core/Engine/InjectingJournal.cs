namespace ViShap.Rewind.Engine;

/// <summary>The journal of a synchronous outermost scope opened in a flow armed by the Testing package.</summary>
internal sealed class InjectingJournal(IFaultInjector injector) : Journal(intercepted: true)
{
    public override IFaultInjector? Injector => injector;

    private protected override void Intercept(JournalEntry entry)
    {
        AddCore(entry);
        injector.AfterRecording(entry);
    }
}
