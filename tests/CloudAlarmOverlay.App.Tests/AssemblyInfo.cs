// WPF pack-resource caches and application state are shared across STA test threads.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
