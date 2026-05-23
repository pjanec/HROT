using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Spatial;

/// <summary>
/// Computes the layout of attachment pills above a host node.
/// Pure math -- no rendering dependency. The caller supplies measured content widths.
/// </summary>
public static class AttachmentLayoutEngine
{
    // Spec constants (zoom 1.0 values; callers scale by zoom before passing hostWidth).
    public const float PillHeight         = 20f;
    public const float PillMinWidth       = 24f;
    public const float PillPaddingH       = 6f;   // each side
    public const float InterAttachmentGap = 4f;
    public const float InterRowGap        = 3f;
    public const float GapAboveHost       = 6f;

    /// <summary>
    /// Compute the layout for a set of attachments on a single host node.
    /// </summary>
    /// <param name="attachments">All attachments for the host, in any order.</param>
    /// <param name="hostWidth">Width of the host node at current zoom.</param>
    /// <param name="measureContentWidth">
    /// Returns the content width (glyph + gap + label, already at current zoom)
    /// for a single attachment. Must return a value greater than or equal to zero.
    /// </param>
    /// <returns>Computed layout. Returns <see cref="AttachmentLayout.Empty"/> when the list is empty.</returns>
    public static AttachmentLayout Compute(
        IReadOnlyList<IAttachmentModel> attachments,
        float hostWidth,
        Func<IAttachmentModel, float> measureContentWidth)
    {
        if (attachments.Count == 0)
            return AttachmentLayout.Empty;

        // Sort by StackIndex, then by attachment Id value for stable ordering on ties.
        var sorted = attachments
            .OrderBy(a => a.StackIndex)
            .ThenBy(a => a.Id.Value)
            .ToList();

        // Compute pill widths.
        var widths = new float[sorted.Count];
        for (int i = 0; i < sorted.Count; i++)
        {
            float content = measureContentWidth(sorted[i]);
            widths[i] = Math.Max(PillMinWidth, content + PillPaddingH * 2f);
        }

        // Build rows (each row is a list of indices into sorted[]).
        var rows = new List<List<int>>();
        var currentRow = new List<int>();
        float rowUsed = 0f;

        for (int i = 0; i < sorted.Count; i++)
        {
            float needed = (currentRow.Count == 0)
                ? widths[i]
                : rowUsed + InterAttachmentGap + widths[i];

            if (currentRow.Count > 0 && needed > hostWidth)
            {
                rows.Add(currentRow);
                currentRow = new List<int>();
                rowUsed = 0f;
            }

            if (currentRow.Count > 0) rowUsed += InterAttachmentGap;
            currentRow.Add(i);
            rowUsed += widths[i];
        }
        if (currentRow.Count > 0)
            rows.Add(currentRow);

        // Rows are built bottom-up. Row 0 in the list is the bottom-most row
        // (closest to the host header). Compute placements.
        var placements = new Dictionary<AttachmentId, AttachmentPlacement>(sorted.Count);

        // Bottom of bottom row is at Y = -(GapAboveHost + PillHeight).
        // Top of bottom row is at Y = -(GapAboveHost + PillHeight).
        // Y increases downward; attachments are above the host (negative Y).
        // Row top-Y for row index r (0 = bottom-most):
        //   rowTopY(0) = -(GapAboveHost + PillHeight)
        //   rowTopY(r) = rowTopY(0) - r * (PillHeight + InterRowGap)

        float bottomRowTopY = -(GapAboveHost + PillHeight);

        for (int r = 0; r < rows.Count; r++)
        {
            float rowTopY = bottomRowTopY - r * (PillHeight + InterRowGap);
            float x = 0f;

            foreach (int idx in rows[r])
            {
                var attachment = sorted[idx];
                var topLeft = new Vector2(x, rowTopY);
                var size    = new Vector2(widths[idx], PillHeight);
                placements[attachment.Id] = new AttachmentPlacement(attachment.Id, topLeft, size);
                x += widths[idx] + InterAttachmentGap;
            }
        }

        // TotalHeightAboveHost = GapAboveHost + rows.Count * PillHeight + (rows.Count - 1) * InterRowGap.
        float totalHeight = GapAboveHost
            + rows.Count * PillHeight
            + (rows.Count - 1) * InterRowGap;

        return new AttachmentLayout(placements, totalHeight);
    }
}
