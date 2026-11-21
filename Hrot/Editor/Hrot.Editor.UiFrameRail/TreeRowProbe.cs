using System;
using System.Collections.Generic;
using Raylib_cs;

namespace Hrot.Editor.UiFrameRail;

/// <summary>
/// ⭐ U-4 (docs/UX/DESIGN_UI_Conventions.md §2, C2) — the pixel probe the tree rails share: where the CHEVRON, the ICON and the LABEL of a tree row are on the screen, read off a screenshot (not off the drawer's own
/// arithmetic). Icons are SOLID colours the rail supplies, so a row's icon is found exactly; the chevron is the ink left of it and the label the ink right of it.
/// </summary>
public static class TreeRowProbe
{
    /// <summary>One tree row as seen on the screen: the y band, the icon's x extent, the chevron's right edge (-1 = no arrow) and the label's left edge.</summary>
    public readonly record struct Row(int Y0, int Y1, int IconMin, int IconMax, int ChevronMax, int LabelMin);

    public static bool Is(Color c, Color want, int tol = 6) => Math.Abs(c.R - want.R) <= tol && Math.Abs(c.G - want.G) <= tol && Math.Abs(c.B - want.B) <= tol;

    /// <summary>Every row whose icon is <paramref name="icon"/>'s colour inside the window area, top to bottom. <paramref name="bg"/> is the window's background colour; each row's own background (it may be lit) is read 6 px inside <paramref name="x1"/> (so x1 must lie inside the row's width, right of everything drawn).</summary>
    public static List<Row> Rows(Image shot, int x0, int y0, int x1, int y1, Color icon, Color bg)
    {
        var rows = new List<Row>();
        int bandStart = -1;
        for (int y = y0; y <= y1; y++)
        {
            bool any = false;
            for (int x = x0; x < x1 && !any; x++) any = Is(Raylib.GetImageColor(shot, x, y), icon);
            if (any && bandStart < 0) bandStart = y;
            if ((!any || y == y1) && bandStart >= 0)
            {
                int bandEnd = any ? y : y - 1;
                rows.Add(Measure(shot, x0, x1, bandStart, bandEnd, icon, bg));
                bandStart = -1;
            }
        }
        return rows;
    }

    private static Row Measure(Image shot, int x0, int x1, int ya, int yb, Color icon, Color bg)
    {
        int iconMin = -1, iconMax = -1;
        for (int x = x0; x < x1; x++)
            for (int y = ya; y <= yb; y++)
                if (Is(Raylib.GetImageColor(shot, x, y), icon)) { if (iconMin < 0) iconMin = x; iconMax = x; break; }
        var rowBg = Raylib.GetImageColor(shot, x1 - 6, (ya + yb) / 2);   // the row's own background, read at its far right (a selected / focused row is lit across its whole width)
        static int Diff(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
        // ink differs from BOTH the row's background and the window's (a lit row starts at its indent: the pixels left of it are the window's)
        bool Ink(Color c) => Diff(c, rowBg) > 120 && Diff(c, bg) > 120 && !Is(c, icon);
        int chevronMax = -1, labelMin = -1;
        for (int x = x0; x < x1; x++)
        {
            bool ink = false;
            for (int y = ya; y <= yb && !ink; y++) ink = Ink(Raylib.GetImageColor(shot, x, y));
            if (!ink) continue;
            if (x < iconMin) chevronMax = x;
            else if (x > iconMax && labelMin < 0) labelMin = x;
        }
        return new Row(ya, yb, iconMin, iconMax, chevronMax, labelMin);
    }
}
