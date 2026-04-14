using Xunit;

/// <summary>
/// xUnit collection definition that disables parallel execution for all DDS-using
/// test classes. Prevents CycloneDDS runtime crashes caused by concurrent participant
/// creation/destruction across test assemblies.
/// </summary>
[CollectionDefinition("DDS", DisableParallelization = true)]
public class DdsTestCollection { }
