using System;
using System.Linq;
using System.Reflection;
using Xunit;
using Hrot.MuscleCharacter.Animation.Contracts;

namespace Hrot.Stride.Animation.Tests;

/// <summary>
/// Verifies that <see cref="StrideAnimationBackend"/> fully satisfies the
/// <see cref="IAnimationBackend"/> contract at runtime (BATCH-01 STR-P0-T2;
/// kept green after the real implementation landed in STR-P4-T1). These assert
/// interface membership, runtime assignability, and complete method surface;
/// behavioral assertions live in StrideAnimationBackendBehaviorTests.
/// </summary>
public class StrideAnimationBackendContractTests
{
    [Fact]
    public void StrideAnimationBackend_IsAssignableToIAnimationBackend()
    {
        // Construct the backend and assign to IAnimationBackend variable.
        // This proves runtime assignability (not just compile-time).
        IAnimationBackend backend = new StrideAnimationBackend();
        Assert.NotNull(backend);
        // Verify the concrete runtime type is correct.
        Assert.IsType<StrideAnimationBackend>(backend);
    }

    [Fact]
    public void StrideAnimationBackend_ImplementsEveryInterfaceMethod()
    {
        // Get all non-static public methods declared on IAnimationBackend.
        var interfaceMethods = typeof(IAnimationBackend)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToArray();

        // Get all public instance methods on the concrete implementation.
        var implType = typeof(StrideAnimationBackend);
        var implMethods = implType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToHashSet();

        // Every interface method must have a matching implementation.
        var missing = interfaceMethods.Where(n => !implMethods.Contains(n)).ToArray();

        Assert.True(
            missing.Length == 0,
            $"StrideAnimationBackend is missing implementations for: {string.Join(", ", missing)}");
    }

    [Fact]
    public void StrideAnimationBackend_InterfaceMethodCount_MatchesExpected()
    {
        // Cross-check: IAnimationBackend has exactly 16 methods as verified in source.
        // If the interface grows, this test will catch it so we can update the stub.
        var count = typeof(IAnimationBackend)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Length;

        Assert.Equal(17, count);
    }

    [Fact]
    public void StrideAnimationBackend_InterfaceMapIsComplete()
    {
        // Verify the type's explicit interface map covers all IAnimationBackend methods.
        var implType = typeof(StrideAnimationBackend);
        var interfaceType = typeof(IAnimationBackend);

        // InterfaceMapping will throw if the interface is not implemented — no need to assert.
        var map = implType.GetInterfaceMap(interfaceType);

        // Every target method must be non-null and have a declaring type of StrideAnimationBackend.
        Assert.All(map.TargetMethods, m => Assert.Equal(implType, m.DeclaringType));

        // The map length must equal the interface method count.
        Assert.Equal(map.InterfaceMethods.Length, map.TargetMethods.Length);
    }
}
