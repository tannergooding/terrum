# Cross-module recovery

`checked-provider.terrum` supplies `Divide`; `checked-consumer.terrum` calls
the provider and recovers from the checked failure. The consumer returns 42
for the covered recovery case.

These files form a provider/consumer composition, not two standalone
applications. Use the `checked-exceptions` recipe in the
[semantic modules guide](../../docs/implementation/modules.md). The
[sample catalog](../../docs/samples.md) distinguishes module execution from
the separate design and test-fixture examples.
