# ViShap.Rewind.Testing

Test tools for code that uses **ViShap.Rewind**. Add it to a test project only.

An operation that is meant to be atomic has as many failure points as it has writes. This package runs the
operation once to count them, then runs it again failing at each one in turn, and checks after every run that
tracked state is exactly as it was and that no deferred effect ran. It finds the write that went to a plain field
instead of tracked state and the effect that was called directly instead of through the scope.

It works with any test framework: a failed check is an exception.
