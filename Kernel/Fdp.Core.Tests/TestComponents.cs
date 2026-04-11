using Fdp.Kernel;

namespace Fdp.Tests
{
    // Shared test component types used across multiple test files

    /// <summary>Wrapper allowing <c>int</c> to be used as an ECS component in tests.</summary>
    [ComponentId(164)]
    public struct IntComponent { public int Value; }

    /// <summary>Wrapper allowing <c>string</c> to be used as a managed ECS component in benchmarks.</summary>
    [ComponentId(165)]
    public record StringComponent(string Value);

    /// <summary>Wrapper allowing <c>float</c> to be used as an ECS component in tests.</summary>
    [ComponentId(246)]
    public struct FloatComponent { public float Value; }

    [ComponentId(166)]
    public struct Position
    {
        public float X, Y, Z;
    }

    [ComponentId(167)]
    public struct Velocity
    {
        public float X, Y, Z;
    }

    [ComponentId(168)]
    public struct Health
    {
        public int Value;
    }
}
