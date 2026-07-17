using System.Numerics;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>Curated, reflection-free vector construction for blueprints (no vector-literal node exists).</summary>
    public static class VectorOps
    {
        public static Vector3 Vec3(float x, float y, float z) => new Vector3(x, y, z);
        public static Vector3 Vec2(float x, float y)          => new Vector3(x, y, 0f);
    }
}
