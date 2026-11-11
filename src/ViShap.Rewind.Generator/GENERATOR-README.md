# ViShap.Rewind.Generator

The source generator and analyzers of **ViShap.Rewind**. They run in the compiler only; no shipped assembly
references this package. It comes with the meta-package `ViShap.Rewind`, which is the package to install.

The generator writes the tracked implementation of the partial properties of a type marked as rewindable, so a
domain type keeps ordinary property syntax and still has its changes undone on rollback. The analyzers report the
mistakes that silently defeat a rollback: an untracked field changed inside a scope, an external effect called
directly instead of through the scope, a scope opened and never committed.

Each diagnostic has a stable identifier of the form `RWDnnnn`.
