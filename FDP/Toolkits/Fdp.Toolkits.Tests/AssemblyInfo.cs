using Xunit;

// Disable parallel test execution across all test classes in this assembly.
// Required to prevent FdpAutoSerializerFixedBufferTests (which temporarily registers
// EntityInlineComp ID 228 in the static ComponentTypeRegistry) from racing with
// RecordingExportServiceTests.EX_T* tests that call FdpAutoSerializer.Build()
// and unexpectedly encounter EntityInlineComp.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
