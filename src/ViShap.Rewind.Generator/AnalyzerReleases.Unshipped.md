; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
RWD0001 | ViShap.Rewind | Error | Rewindable type must be partial
RWD0002 | ViShap.Rewind | Warning | Writable auto-property is not rolled back
RWD0003 | ViShap.Rewind | Error | Partial property has an init accessor or a ref return
RWD0004 | ViShap.Rewind | Warning | Only the reference of a mutable reference type is tracked
RWD0005 | ViShap.Rewind | Error | Partial property cannot be tracked: static, indexer, or no set accessor
RWD1001 | ViShap.Rewind | Warning | Scope returned by Rewind.Begin() is never disposed
RWD1002 | ViShap.Rewind | Warning | External effect called directly inside a scope
RWD1003 | ViShap.Rewind | Error | Asynchronous block passed to Rewind.Run
RWD1004 | ViShap.Rewind | Warning | RewindCell<T> of a mutable reference type
RWD1005 | ViShap.Rewind | Warning | Untracked field written inside a scope
