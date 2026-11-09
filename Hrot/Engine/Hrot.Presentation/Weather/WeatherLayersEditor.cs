using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Weather;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Hrot.UI.Common.Weather;

/// <summary>
/// ⭐⭐ CE-4346 (📄 docs/UX/DESIGN_Details_Forms.md §3 Scenario, Part 2 as-built; backend NOTE "Scenario form weather layers") — <b>the wind levels and cloud
/// layers of a weather</b> (the World Environment's global weather, or a weather region's), each a table with its own editors: wind — altitude, from, speed,
/// gust; cloud — base, thickness, cover (octas by its aviation name), kind; a row's ✕ removes it, <i>Add</i> appends one (up to 8 / 4). ⭐ A list travels WHOLE
/// (forms Q8): every edit copies the <see cref="WeatherConditions"/>, changes it with its own list ops and writes it through <see cref="WeatherEdit.Set"/> (the
/// owner writes, any other node asks it). ⭐ HOT PATH: constant ids, no strings built per frame; a value is written only when its field is committed.
/// </summary>
public static class WeatherLayersEditor
{
    private static readonly string[] CoverNames = { "Clear (0/8)", "Few (1-2/8)", "Scattered (3-4/8)", "Broken (5-7/8)", "Overcast (8/8)" };
    private static readonly byte[] CoverOctas = { 0, 2, 4, 6, 8 };
    private static readonly string[] KindNames = { "Stratus", "Cumulus", "Stratocumulus", "Cirrus", "Cumulonimbus" };
    private static readonly Vector4 Muted = new(0.62f, 0.62f, 0.66f, 1f);

    /// <summary>For rails: the wind / cloud tables' rows on the last draw (−1 = not drawn).</summary>
    public static int DrawnWindRows { get; private set; } = -1;
    /// <summary>For rails: the cloud table's rows on the last draw.</summary>
    public static int DrawnCloudRows { get; private set; } = -1;

    /// <summary>Draws both tables for <paramref name="e"/>'s weather (nothing when it has none); <paramref name="editable"/> false ⇒ read-only.</summary>
    public static void Draw(EntityRepository world, Entity e, bool editable)
    {
        DrawnWindRows = DrawnCloudRows = -1;
        if (!WeatherEdit.TryRead(world, e, out var w)) return;
        ImGui.PushID("wx.layers");
        DrawWind(world, e, ref w, editable);
        DrawClouds(world, e, ref w, editable);
        ImGui.PopID();
    }

    private static unsafe void DrawWind(EntityRepository world, Entity e, ref WeatherConditions w, bool editable)
    {
        SectionCaption("Wind levels");
        int n = w.WindCount;
        DrawnWindRows = n;
        if (n == 0) ImGui.TextColored(Muted, "no wind levels — calm");
        else if (ImGui.BeginTable("##wind", 5, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerH))
        {
            ImGui.TableSetupColumn("Altitude (m)");
            ImGui.TableSetupColumn("From (°)");
            ImGui.TableSetupColumn("Speed (m/s)");
            ImGui.TableSetupColumn("Gust (m/s)");
            ImGui.TableSetupColumn("##rm", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
            ImGui.TableHeadersRow();
            int remove = -1;
            for (int i = 0; i < n; i++)
            {
                ImGui.PushID(i);
                ImGui.TableNextRow();
                float alt = w.WindAltitudeM[i], from = w.WindFromDeg[i], speed = w.WindSpeedMps[i], gust = w.WindGustMps[i];
                bool changed = false;
                changed |= Cell(0, "##alt", ref alt, -500f, 20000f);
                changed |= Cell(1, "##from", ref from, 0f, 360f);
                changed |= Cell(2, "##speed", ref speed, 0f, 120f);
                changed |= Cell(3, "##gust", ref gust, 0f, 150f);
                ImGui.TableSetColumnIndex(4);
                if (editable && RemoveButton("##rmw")) remove = i;
                if (changed && editable) { var c = w; c.SetWind(i, alt, from % 360f, speed, Math.Max(gust, 0f)); if (WeatherEdit.Set(world, e, c)) w = c; }
                ImGui.PopID();
            }
            ImGui.EndTable();
            if (remove >= 0) { var c = w; if (c.RemoveWind(remove) && WeatherEdit.Set(world, e, c)) w = c; }
        }
        if (editable)
        {
            bool full = w.WindCount >= WeatherConditions.MaxWindLevels;
            ImGui.BeginDisabled(full);
            if (ImGui.SmallButton("+ Add wind level"))
            {
                var c = w;
                float above = c.WindCount > 0 ? c.WindAltitudeM[c.WindCount - 1] + 500f : 10f;
                float from = c.WindCount > 0 ? c.WindFromDeg[c.WindCount - 1] : 270f;
                float speed = c.WindCount > 0 ? c.WindSpeedMps[c.WindCount - 1] : 3f;
                if (c.AddWind(above, from, speed, 0f) && WeatherEdit.Set(world, e, c)) w = c;
            }
            ImGui.EndDisabled();
            if (full && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("at most 8 wind levels");
        }
    }

    private static unsafe void DrawClouds(EntityRepository world, Entity e, ref WeatherConditions w, bool editable)
    {
        SectionCaption("Cloud layers");
        int n = w.CloudCount;
        DrawnCloudRows = n;
        if (n == 0) ImGui.TextColored(Muted, "no cloud layers — clear sky");
        else if (ImGui.BeginTable("##cloud", 5, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerH))
        {
            ImGui.TableSetupColumn("Base (m)");
            ImGui.TableSetupColumn("Thickness (m)");
            ImGui.TableSetupColumn("Cover");
            ImGui.TableSetupColumn("Kind");
            ImGui.TableSetupColumn("##rm", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
            ImGui.TableHeadersRow();
            int remove = -1;
            for (int i = 0; i < n; i++)
            {
                ImGui.PushID(i);
                ImGui.TableNextRow();
                float baseM = w.CloudBaseM[i], thick = w.CloudThicknessM[i];
                int cover = CoverIndex(w.CloudOctas[i]);
                int kind = Math.Clamp((int)w.CloudKind[i], 0, KindNames.Length - 1);
                bool changed = false;
                changed |= Cell(0, "##base", ref baseM, 0f, 20000f);
                changed |= Cell(1, "##thick", ref thick, 0f, 15000f);
                ImGui.TableSetColumnIndex(2); ImGui.SetNextItemWidth(-1f);
                changed |= ImGui.Combo("##cover", ref cover, CoverNames, CoverNames.Length);
                ImGui.TableSetColumnIndex(3); ImGui.SetNextItemWidth(-1f);
                changed |= ImGui.Combo("##kind", ref kind, KindNames, KindNames.Length);
                ImGui.TableSetColumnIndex(4);
                if (editable && RemoveButton("##rmc")) remove = i;
                if (changed && editable) { var c = w; c.SetCloud(i, baseM, thick, CoverOctas[cover], (CloudKind)kind); if (WeatherEdit.Set(world, e, c)) w = c; }
                ImGui.PopID();
            }
            ImGui.EndTable();
            if (remove >= 0) { var c = w; if (c.RemoveCloud(remove) && WeatherEdit.Set(world, e, c)) w = c; }
        }
        if (editable)
        {
            bool full = w.CloudCount >= WeatherConditions.MaxCloudLayers;
            ImGui.BeginDisabled(full);
            if (ImGui.SmallButton("+ Add cloud layer"))
            {
                var c = w;
                float baseM = c.CloudCount > 0 ? c.CloudBaseM[c.CloudCount - 1] + c.CloudThicknessM[c.CloudCount - 1] + 500f : 1500f;
                if (c.AddCloud(baseM, 600f, 4, CloudKind.Cumulus) && WeatherEdit.Set(world, e, c)) w = c;
            }
            ImGui.EndDisabled();
            if (full && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("at most 4 cloud layers");
        }
    }

    /// <summary>The cover choice an octa count falls in (the stored value stays the octas).</summary>
    internal static int CoverIndex(byte octas) => octas switch { 0 => 0, <= 2 => 1, <= 4 => 2, <= 7 => 3, _ => 4 };

    private static bool Cell(int column, string id, ref float value, float min, float max)
    {
        ImGui.TableSetColumnIndex(column);
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputFloat(id, ref value, 0f, 0f, "%.0f");
        bool committed = ImGui.IsItemDeactivatedAfterEdit();   // written once, when the field is left — not per keystroke
        value = Math.Clamp(value, min, max);
        return committed;
    }

    private static bool RemoveButton(string id)
    {
        var g = Icons.Get("storage/cancel");
        return GlyphWidgets.GlyphButton(id, g, "Remove this row");
    }

    private static void SectionCaption(string text) => Fdp.Presentation.Forms.FormView.Caption(text);   // the form's ONE caption style
}
