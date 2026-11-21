namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Builds an authored <see cref="Fdp.Toolkit.Replication.PointLink"/> from local coordinates, for a baked blueprint
    /// <c>FunctionCall</c> that has two floats and must fill a params DTO's point (S0 item 5: <c>MoveToLocation.Location</c>
    /// is ONE point now, not an X / Y pair). ⚠ Deliberately NOT <c>[BlueprintCallable]</c> — the palette offers nothing new;
    /// the compiler never reads the attribute, so a baked call still compiles (as <see cref="NetworkEntityMapOps.ResolveTarget"/>).
    /// </summary>
    public static class AuthoredPointOps
    {
        /// <summary>The point at local <paramref name="x"/>, <paramref name="y"/> (z = 0), with no reference form.</summary>
        public static Fdp.Toolkit.Replication.PointLink At(float x, float y)
            => new(new Fdp.Core.WorldPoint(x, y, 0f));
    }
}
