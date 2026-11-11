using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using global::CarKinem.Systems;
using Xunit;

namespace Fdp.Toolkit.Tests.CarKinemRails
{
    /// <summary>
    /// ⭐ CE-1480 G4 — the paused ground clamp reads first and takes the write reference only when the height differs: a body already on the surface
    /// stamps no <see cref="SimTransform"/> chunk (it used to, every paused frame, which kept every change gate downstream — map culling, the
    /// recorder, the view copy — permanently "changed"); a body off the surface is still put back on it.
    /// </summary>
    public sealed class GroundClampWhilePausedTests
    {
        [Fact]
        public void ABodyOnTheSurface_StampsNoChunk_AndABodyOffItIsClamped()
        {
            using var w = new EntityRepository();
            w.RegisterComponent<SimTransform>(); w.RegisterComponent<SimVelocity>(); w.RegisterComponent<VehicleState>();
            w.RegisterComponent<VehicleParams>(); w.RegisterComponent<NavState>();
            w.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            w.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(
                "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-100,-100,600,100],\"groundZ\":0},\"features\":[]}"));

            Entity Body(float z)
            {
                var e = w.CreateEntity();
                w.AddComponent(e, new SimTransform { Position = new Vector3(10, 10, z), Rotation = Quaternion.Identity });
                w.AddComponent(e, new SimVelocity()); w.AddComponent(e, new VehicleState());
                w.AddComponent(e, new VehicleParams()); w.AddComponent(e, new NavState());
                w.SetAuthority<SimTransform>(e, true);
                return e;
            }
            var onGround = Body(0f);
            var system = new GroundClampWhilePausedSystem();

            w.Tick(); system.Execute(w, 0f);
            uint v = w.GlobalVersion;
            for (int i = 0; i < 5; i++) { w.Tick(); system.Execute(w, 0f); }
            Assert.False(w.HasComponentChanged(typeof(SimTransform), v), "a body already on the surface must not stamp its chunk");

            var floating = Body(5f);
            w.Tick(); system.Execute(w, 0f);
            Assert.Equal(0f, w.GetComponentRO<SimTransform>(floating).Position.Z);
            Assert.Equal(0f, w.GetComponentRO<SimTransform>(onGround).Position.Z);
        }
    }
}
