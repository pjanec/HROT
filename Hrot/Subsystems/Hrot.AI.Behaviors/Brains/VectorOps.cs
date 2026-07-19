using System.Numerics;
using Hrot.Editor.AiShared;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>Curated, reflection-free vector construction for blueprints (no vector-literal node exists).</summary>
    public static class VectorOps
    {
        /// <summary>Constructs a Vector3 from three floats.</summary>
        [BlueprintCallable("Vector", DisplayName = "Make Vector3")]
        public static Vector3 Vec3(float x, float y, float z) => new Vector3(x, y, z);

        /// <summary>Constructs a Vector3 in the XY plane (Z = 0) from two floats.</summary>
        [BlueprintCallable("Vector", DisplayName = "Make Vector2 (XY)")]
        public static Vector3 Vec2(float x, float y)          => new Vector3(x, y, 0f);
    }
}
