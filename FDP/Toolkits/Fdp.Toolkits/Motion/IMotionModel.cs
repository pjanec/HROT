using System;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Motion
{
    /// <summary>
    /// ⭐ CE-2168 (docs/designs/motion-models/DESIGN_Motion_Domains.md MD-C) — ONE body's motion for one frame: what a motion model
    /// reads and writes. The model owns the body's position, velocity and Z (MD-G); its own state and parameters it reads through
    /// <see cref="World"/> by <see cref="Self"/>.
    /// </summary>
    public ref struct MotionFrame
    {
        public Entity Self;
        public EntityRepository World;
        public ref SimTransform Transform;
        public ref SimVelocity Velocity;
        /// <summary>⭐ Air Ops slice 2 — the terrain the body flies over (null ⇒ flat ground at 0): heights for a route, and the
        /// ground a landing sets down on.</summary>
        public Fdp.Toolkit.World.IWorldQuery? Terrain;

        /// <summary>The ground height under (x, y).</summary>
        public readonly float Ground(float x, float y) => Terrain?.GroundHeightAt(x, y) ?? 0f;

        public MotionFrame(EntityRepository world, Entity self, ref SimTransform transform, ref SimVelocity velocity)
        {
            World = world;
            Self = self;
            Transform = ref transform;
            Velocity = ref velocity;
            Terrain = null;
        }
    }

    /// <summary>
    /// ⭐ CE-2168 (MD-C, MD-D) — how one <see cref="LocomotionClass"/> moves, under the SAME two commands every mover takes: the
    /// behaviour's <see cref="NavigationIntent"/> (autonomous) and a possessing operator's <see cref="ManualDrive"/> (manual).
    /// A new mover is a new model + a <see cref="MotionModelTable"/> row, never a branch in someone else's system.
    /// </summary>
    public interface IMotionModel
    {
        LocomotionClass Class { get; }

        /// <summary>Follow the behaviour's intent (a 3-D point, a speed); report arrival on the body's <c>NavigationStatus</c>.</summary>
        void Autonomous(ref MotionFrame frame, in NavigationIntent intent, float dt);

        /// <summary>Follow the operator's axes (<see cref="ManualDrive"/>).</summary>
        void Manual(ref MotionFrame frame, in ManualDrive drive, float dt);
    }

    /// <summary>
    /// ⭐ CE-2168 (MD-C, MD-H) — the class → model table. A domain's module only SCHEDULES its classes' models through it.
    /// v1 holds the air row (<see cref="LocomotionClass.Rotorcraft"/>); the ground classes still run <c>CarKinematicsSystem</c> (their
    /// two-model refactor is its own slice) and answer null; <see cref="LocomotionClass.FixedWing"/>, <see cref="LocomotionClass.Surface"/>
    /// and <see cref="LocomotionClass.Amphibious"/> FAIL LOUDLY until built.
    /// </summary>
    public static class MotionModelTable
    {
        private static readonly IMotionModel Rotorcraft = new RotorcraftModel();

        /// <summary>The model of <paramref name="c"/>; null for a ground class (CarKinematics moves it in v1).</summary>
        /// <exception cref="NotSupportedException">a class whose model is not built yet.</exception>
        public static IMotionModel? Model(LocomotionClass c) => c switch
        {
            LocomotionClass.Rotorcraft => Rotorcraft,
            LocomotionClass.Pedestrian or LocomotionClass.Wheeled or LocomotionClass.Tracked or LocomotionClass.Unknown => null,
            _ => throw new NotSupportedException(
                $"no motion model for {c} yet (DESIGN_Motion_Domains.md MD-H: FixedWing / Surface / Amphibious are table rows until built)"),
        };
    }
}
