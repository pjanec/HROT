namespace GizmoMap.Network
{
    /// <summary>
    /// Which node a <b>canvas</b> (entity-less) interaction is addressed to — the declared purpose of
    /// <see cref="GizmoInteractionBatch.PickStreamId"/>, a "publisher stream discriminator".
    ///
    /// <para>An ENTITY pick scopes itself: the receiver resolves <see cref="GizmoInteractionBatch.PickAnchorId"/>
    /// in its own world. A canvas pick (anchor <see cref="CanvasAnchorId"/>) hits no entity, so nothing on the
    /// record says whose map it was — and a broadcast DDS topic reaches every node. A viewer that mirrors one
    /// node's map names that node here; a receiver accepts a canvas pick only when it is named.</para>
    ///
    /// <para>⚠ <c>0</c> means "no stream" — it is what every entity pick carries and what an un-migrated
    /// sender sends, and a canvas pick carrying it is dropped exactly as before this existed.</para>
    /// </summary>
    public static class GizmoPickScope
    {
        /// <summary>The anchor id of a canvas (empty-space) pick: the default token.</summary>
        public const long CanvasAnchorId = 0;

        /// <summary>
        /// What a sender puts in <see cref="GizmoInteractionBatch.PickStreamId"/>: the node whose map it mirrors
        /// for a canvas pick, <c>0</c> for an entity pick.
        /// </summary>
        public static uint StreamIdFor(long pickAnchorId, byte targetNodeId)
            => pickAnchorId == CanvasAnchorId ? targetNodeId : 0u;

        /// <summary>
        /// True when a received record is a canvas pick addressed to <paramref name="localNodeId"/>.
        /// </summary>
        public static bool IsCanvasPickFor(long pickAnchorId, uint pickStreamId, long localNodeId)
            => pickAnchorId == CanvasAnchorId && pickStreamId != 0 && pickStreamId == localNodeId;
    }
}
