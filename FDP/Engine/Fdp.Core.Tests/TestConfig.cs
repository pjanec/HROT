using Xunit;

// Disable parallel test execution completely to avoid race conditions with static ComponentTypeRegistry.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
