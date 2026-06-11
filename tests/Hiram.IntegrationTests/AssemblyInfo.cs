// Integration tests each spin up their own containers; run them serially to avoid resource contention
// and to keep process-wide configuration overrides isolated between test classes.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
