using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Layout;

// Simple statechart grid layout for new HsmAsset instances.
// Runs only when opening an asset with no existing layout data.
// Lays out top-level states left-to-right with children arranged inside composites.
public static class HsmAutoLayout
{
    private const float TopLevelSpacingX = 200f;
    private const float TopLevelStartX   = 100f;
    private const float TopLevelY        = 100f;
    private const float ChildSpacingX    = 160f;
    private const float ChildSpacingY    =  80f;
    private const float ChildStartX      =  20f;
    private const float ChildStartY      =  40f;
    private const float DefaultWidth     = 160f;
    private const float DefaultHeight    =  80f;
    private const float CompositeWidth   = 400f;
    private const float CompositeHeight  = 200f;

    // Runs auto-layout on the given asset, writing positions to each StateNode.
    // Only positions nodes with default (zero) positions.
    public static void Layout(HsmAsset asset)
    {
        float x = TopLevelStartX;
        foreach (var state in asset.RootState.Children)
        {
            state.Position = new Vector2(x, TopLevelY);
            if (state.Children.Count > 0 || state.IsParallel)
            {
                state.SizeOverride = new Vector2(CompositeWidth, CompositeHeight);
                LayoutChildren(state, x, TopLevelY);
                x += CompositeWidth + TopLevelSpacingX;
            }
            else
            {
                x += DefaultWidth + TopLevelSpacingX;
            }
        }
    }

    private static void LayoutChildren(StateNode parent, float parentX, float parentY)
    {
        float cx = parentX + ChildStartX;
        float cy = parentY + ChildStartY;
        int col = 0;
        int maxCols = 3;
        foreach (var child in parent.Children)
        {
            child.Position = new Vector2(cx, cy);
            col++;
            if (col >= maxCols)
            {
                col = 0;
                cx = parentX + ChildStartX;
                cy += DefaultHeight + ChildSpacingY;
            }
            else
            {
                cx += DefaultWidth + ChildSpacingX;
            }
            if (child.Children.Count > 0 || child.IsParallel)
            {
                child.SizeOverride = new Vector2(CompositeWidth, CompositeHeight);
                LayoutChildren(child, child.Position.X, child.Position.Y);
            }
        }
    }
}
