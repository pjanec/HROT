using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Core.Threading;
using Fdp.Toolkit.Vis3D;
using Hrot.Map.Common.Components;

namespace Hrot.UI.Common.Map3D;

/// <summary>⭐ CE-11006 (RR-6) — a camera driver says what it is doing (source, mode, bound unit, distance, side); the pose is the camera's own.</summary>
public interface ICameraSuggests
{
    void Describe(EntityRepository world, ref CameraSuggestion s);
}

/// <summary>
/// ⭐⭐ CE-11006 (RR-6, R-11000; docs/designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md D-1) — the ONE writer of the world's
/// <see cref="CameraSuggestion"/>: called once per frame by <see cref="MapViewModeLayer"/> (every map host has it), it composes the driver's description
/// (<see cref="ICameraSuggests"/>; none = the operator's free camera) with the 3-D camera's pose. 0 B (a struct, set by value).
/// </summary>
public static class CameraSuggestions
{
    public static void Write(EntityRepository world, MapCamera3D camera)
    {
        var s = new CameraSuggestion { Source = CameraSource.Operator, Mode = CameraMode.Free, Distance = camera.Distance };
        if (camera.Driver is ICameraSuggests d) d.Describe(world, ref s);
        s.Position = camera.Eye;
        s.LookAt = camera.LookAt;
        s.Orientation = OrientationOf(camera.Forward, camera.Up);
        s.FovyDegrees = camera.FovyDegrees;
        world.SetSingletonUnmanaged(s);
        if (SimJobs.LinkOf(world) is { } link) Uplink(world, link, in s);
    }

    // ⭐⭐ CE-11066 (unreal-ig, the director test H-1) — on a THREADED node the map draws world A (the presentation's) while every node extension (the
    //   gateway) runs on world S: the suggestion written only on A never reached S, so the gateway sent no View at all (measured: 0 samples on Unreal
    //   through a whole live take). The newest suggestion — and the caption text when it changes — is copied to S by ONE pending job per link: a UI
    //   frame updates the latest value and posts only when no copy is waiting, so the sim drains at most one copy per tick. 0 B steady (a cached job,
    //   a struct copy under a lock; the caption is the same immutable record object A holds).
    private sealed class Link
    {
        public readonly object Gate = new();
        public CameraSuggestion Latest;
        public FilmCaption? Caption;
        public bool Pending;
        public FilmCaption? LastCaption;
        public readonly SimJobQueue.SimJob Job;

        public Link() => Job = Copy;

        private Action<EntityRepository>? Copy(EntityRepository sim)
        {
            CameraSuggestion s; FilmCaption? caption;
            lock (Gate) { s = Latest; caption = Caption; Caption = null; Pending = false; }
            sim.SetSingletonUnmanaged(s);
            if (caption != null) sim.SetSingletonManaged(caption);
            return null;
        }
    }

    private static readonly ConditionalWeakTable<SimLink, Link> Links = new();

    private static void Uplink(EntityRepository world, SimLink link, in CameraSuggestion s)
    {
        var l = Links.GetValue(link, static _ => new Link());
        FilmCaption? caption = world.HasSingletonManaged<FilmCaption>() ? world.GetSingletonManaged<FilmCaption>() : null;
        bool post;
        lock (l.Gate)
        {
            l.Latest = s;
            if (caption != null && !ReferenceEquals(caption, l.LastCaption)) { l.Caption = caption; l.LastCaption = caption; }
            post = !l.Pending;
            l.Pending = true;
        }
        if (post) link.Jobs.Post(l.Job, "camera suggestion");
    }

    /// <summary>The rotation carrying +X onto <paramref name="forward"/> and +Z onto <paramref name="up"/> (+Y = their left).</summary>
    public static Quaternion OrientationOf(Vector3 forward, Vector3 up)
    {
        if (forward.LengthSquared() < 1e-8f) return Quaternion.Identity;
        var f = Vector3.Normalize(forward);
        var left = Vector3.Cross(up, f);
        if (left.LengthSquared() < 1e-8f) left = Vector3.Cross(Vector3.UnitZ, f);
        if (left.LengthSquared() < 1e-8f) left = Vector3.UnitY;
        left = Vector3.Normalize(left);
        var u = Vector3.Cross(f, left);
        var m = new Matrix4x4(f.X, f.Y, f.Z, 0f, left.X, left.Y, left.Z, 0f, u.X, u.Y, u.Z, 0f, 0f, 0f, 0f, 1f);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }
}
