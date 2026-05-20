using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.Blueprints.Tests.Mocks;

// Shared structs used across MockSimulationView and MockEntityCommandBuffer contract tests.
// Component IDs 252-254 are reserved for Blueprint test-only types.

/// <summary>
/// Minimal unmanaged component for ECB and view tests.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[ComponentId(252)]
internal struct TestComponent
{
    public int Value;
}

/// <summary>
/// Unmanaged event for bus tests. EventId reserved in range 90000-90099 for test types.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[EventId(90001)]
internal struct TestEvent
{
    public int Value;
}

/// <summary>
/// Large unmanaged struct for AddEmptyComponent zero-init tests.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[ComponentId(253)]
internal unsafe struct LargeTestStruct
{
    public fixed byte Data[256];
}

/// <summary>
/// Second test component to verify multiple component types co-exist.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[ComponentId(254)]
internal struct AnotherTestComponent
{
    public float X;
    public float Y;
}

/// <summary>
/// Registers all test-only component types in a given EntityRepository.
/// Call this at the start of any test that performs AddComponent / GetComponentRO
/// operations with the test types above.
/// </summary>
internal static class MockTestComponents
{
    internal static void Register(EntityRepository repo)
    {
        repo.RegisterComponent<TestComponent>();
        repo.RegisterComponent<LargeTestStruct>();
        repo.RegisterComponent<AnotherTestComponent>();
    }
}
