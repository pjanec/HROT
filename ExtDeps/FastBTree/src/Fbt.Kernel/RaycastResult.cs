using System.Numerics;

namespace Fbt
{
    public struct RaycastResult
    {
        public bool IsReady;
        public bool Hit;
        public Vector3 HitPoint;
        public Vector3 HitNormal;
        public float Distance;
    }
}
