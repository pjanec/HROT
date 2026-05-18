using Fdp.Kernel;

namespace Fdp.Examples.Showcase.Components
{
    public struct Projectile
    {
        public Entity Owner;
        public float Damage;
        public float Speed;
        public float Lifetime; // Time until auto-destroy
    }
}
