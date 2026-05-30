using System;
using ImGuiNET;
using Fdp.Core;
using Fdp.Presentation.WindowManager;
using Hrot.MuscleCharacter.Animation.Fake.Components;

namespace Hrot.MuscleCharacter.Animation.Fake.Windows;

/// <summary>
/// ANC-P1-09: ImGui diagnostic window for FakeAnimationBackend inspection.
/// Registered via SimHostSubsystem.RegisterWindows in non-headless mode (DD-Fake §7.3,
/// retargeted from the non-existent MuscleCharacterHostSubsystem to SimHostSubsystem).
/// Renders FakeAnimBackendState (DD-Fake §2/§7) per humanoid entity: slots, aim, stance,
/// locomotion inputs, and pending notify ring. JSON snapshot button per DD-Fake §8.
/// </summary>
public sealed class FakeAnimBackendInspectorWindow : ManagedWindow
{
    private EntityRepository? _repo;
    private int _selectedEntityIndex = -1;

    public FakeAnimBackendInspectorWindow()
        : base("anim_backend_inspector", "Animation Backend Inspector", "Authoring", WindowScope.PerspectiveBound)
    {
    }

    public void SetBackend(EntityRepository repo)
    {
        _repo = repo;
    }

    protected override void DrawClientArea()
    {
        var repo = _repo;
        if (repo == null)
        {
            ImGui.TextDisabled("No world available.");
            return;
        }

        var query = repo.Query().With<FakeAnimBackendState>().Build();

        // Header — entity count + JSON snapshot button.
        int count = 0;
        foreach (var _ in query) count++;
        ImGui.TextDisabled($"Registered humanoid entities: {count}");
        ImGui.Separator();

        if (count == 0)
        {
            ImGui.TextDisabled("(no entities — backend has nothing to inspect)");
            return;
        }

        // Two-column layout: list on the left, detail on the right.
        if (ImGui.BeginTable("anim_inspector", 2,
                ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Entities", ImGuiTableColumnFlags.WidthFixed, 220f);
            ImGui.TableSetupColumn("Detail",   ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            DrawEntityList(repo, query);

            ImGui.TableSetColumnIndex(1);
            DrawSelectedEntityDetail(repo);

            ImGui.EndTable();
        }
    }

    private void DrawEntityList(EntityRepository repo, EntityQuery query)
    {
        ImGui.BeginChild("entity_list", new System.Numerics.Vector2(0, 0), ImGuiChildFlags.None);

        foreach (var entity in query)
        {
            ref readonly var state = ref repo.GetComponentRO<FakeAnimBackendState>(entity);
            int activeSlots = CountActiveSlots(in state);
            string label = $"Entity {entity.Index}  ({activeSlots}/8 slots)";
            if (ImGui.Selectable(label, _selectedEntityIndex == entity.Index))
            {
                _selectedEntityIndex = entity.Index;
            }
        }

        ImGui.EndChild();
    }

    private void DrawSelectedEntityDetail(EntityRepository repo)
    {
        if (_selectedEntityIndex < 0)
        {
            ImGui.TextDisabled("Select an entity from the list.");
            return;
        }

        // Re-resolve the entity (the index is stable for a session).
        var query = repo.Query().With<FakeAnimBackendState>().Build();
        bool found = false;
        Entity sel = default;
        foreach (var e in query)
        {
            if (e.Index == _selectedEntityIndex) { sel = e; found = true; break; }
        }
        if (!found)
        {
            ImGui.TextDisabled($"Entity {_selectedEntityIndex} no longer present.");
            return;
        }

        ref readonly var state = ref repo.GetComponentRO<FakeAnimBackendState>(sel);

        ImGui.Text($"Entity {sel.Index}");
        ImGui.SameLine();
        if (ImGui.SmallButton("Copy JSON Snapshot"))
        {
            ImGui.SetClipboardText(BuildJsonSnapshot(sel, in state));
        }
        ImGui.TextDisabled($"Generation: {state.Generation}   TotalTicks: {state.TotalTicks}");
        ImGui.Separator();

        if (ImGui.BeginTabBar("anim_detail_tabs"))
        {
            if (ImGui.BeginTabItem("Slots"))      { DrawSlotsTab(in state);      ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Aim"))        { DrawAimTab(in state);        ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Stance"))     { DrawStanceTab(in state);     ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Locomotion")) { DrawLocomotionTab(in state); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Notifies"))   { DrawNotifiesTab(in state);   ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
    }

    private static void DrawSlotsTab(in FakeAnimBackendState state)
    {
        if (!ImGui.BeginTable("slots", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            return;
        ImGui.TableSetupColumn("Slot");
        ImGui.TableSetupColumn("Montage");
        ImGui.TableSetupColumn("Elapsed");
        ImGui.TableSetupColumn("BlendW");
        ImGui.TableSetupColumn("BlendOut?");
        ImGui.TableSetupColumn("Notifies");
        ImGui.TableHeadersRow();

        for (int i = 0; i < 8; i++)
        {
            ref readonly var s = ref state.Slots[i];
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.Text(i.ToString());
            ImGui.TableNextColumn();
            if (s.IsActive == 0) { ImGui.TextDisabled("—"); }
            else                 { ImGui.Text($"#{s.ActiveMontage.Hash:X8}  sec={s.CurrentSectionIndex}"); }
            ImGui.TableNextColumn();
            ImGui.Text(s.IsActive == 0 ? "" : $"{s.ElapsedSeconds:F2}/{s.TotalDurationSeconds:F2}");
            ImGui.TableNextColumn();
            ImGui.Text(s.IsActive == 0 ? "" : $"{s.BlendWeight:F2}");
            ImGui.TableNextColumn();
            ImGui.Text(s.InBlendOutWindow != 0 ? "yes" : (s.IsActive == 0 ? "" : "no"));
            ImGui.TableNextColumn();
            ImGui.Text(s.IsActive == 0 ? "" : $"0x{s.FiredNotifyMask:X16}");
        }
        ImGui.EndTable();
    }

    private static void DrawAimTab(in FakeAnimBackendState state)
    {
        var a = state.Aim;
        ImGui.Text($"IsActive: {a.IsActive != 0}");
        ImGui.Text($"IsReleasing: {a.IsReleasing != 0}");
        ImGui.Text($"Priority: {a.Priority}");
        ImGui.Text($"BlendWeight: {a.BlendWeight:F2}");
        ImGui.Text($"BlendIn / BlendOut: {a.BlendInTime:F2} / {a.BlendOutTime:F2}");
        ImGui.Text($"Target point: ({a.TargetWorldAimPoint.X:F1}, {a.TargetWorldAimPoint.Y:F1}, {a.TargetWorldAimPoint.Z:F1})");
        ImGui.Text($"Current point: ({a.WorldAimPoint.X:F1}, {a.WorldAimPoint.Y:F1}, {a.WorldAimPoint.Z:F1})");
    }

    private static void DrawStanceTab(in FakeAnimBackendState state)
    {
        var st = state.Stance;
        ImGui.Text($"Current: {st.CurrentStance}");
        ImGui.Text($"Target:  {st.TargetStance}");
        ImGui.Text($"Transitioning: {st.IsTransitioning != 0}");
        if (st.IsTransitioning != 0)
            ImGui.ProgressBar(st.TransitionProgress, new System.Numerics.Vector2(200, 0),
                $"{st.TransitionProgress * 100f:F0}% over {st.TransitionTotalSeconds:F2}s");
    }

    private static void DrawLocomotionTab(in FakeAnimBackendState state)
    {
        ImGui.Text($"HorizontalSpeed: {state.HorizontalSpeed:F2} m/s");
        ImGui.Text($"LocalHorizontalVelocity: ({state.LocalHorizontalVelocity.X:F2}, {state.LocalHorizontalVelocity.Y:F2})");
        ImGui.Text($"VerticalVelocity: {state.VerticalVelocity:F2} m/s");
        ImGui.Text($"IsGrounded: {state.IsGrounded != 0}");
        ImGui.Separator();
        ImGui.Text($"DistanceSinceLastFootstep: {state.DistanceSinceLastFootstep:F2} m");
        ImGui.Text($"NextFootIndex: {(state.NextFootIndex == 0 ? "left" : "right")}");
    }

    private static void DrawNotifiesTab(in FakeAnimBackendState state)
    {
        ImGui.Text($"PendingNotifyCount: {state.PendingNotifyCount} / 16");
        ImGui.TextDisabled("(should usually be 0 — drained each tick by NotifyEventEmitterSystem)");
        ImGui.Separator();
        if (state.PendingNotifyCount == 0)
        {
            ImGui.TextDisabled("(no pending notifies)");
            return;
        }
        if (!ImGui.BeginTable("notifies", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            return;
        ImGui.TableSetupColumn("#");
        ImGui.TableSetupColumn("Kind");
        ImGui.TableSetupColumn("MarkerHash");
        ImGui.TableSetupColumn("Time");
        ImGui.TableSetupColumn("Payload");
        ImGui.TableHeadersRow();
        for (int i = 0; i < state.PendingNotifyCount; i++)
        {
            ref readonly var n = ref state.PendingNotifies[i];
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.Text(i.ToString());
            ImGui.TableNextColumn(); ImGui.Text(n.Kind.ToString());
            ImGui.TableNextColumn(); ImGui.Text($"0x{n.MarkerHash:X8}");
            ImGui.TableNextColumn(); ImGui.Text($"{n.TimeSeconds:F2}s");
            ImGui.TableNextColumn(); ImGui.Text($"f={n.PayloadFloat:F2} u={n.PayloadUint}");
        }
        ImGui.EndTable();
    }

    private static int CountActiveSlots(in FakeAnimBackendState state)
    {
        int c = 0;
        for (int i = 0; i < 8; i++)
            if (state.Slots[i].IsActive != 0) c++;
        return c;
    }

    /// <summary>
    /// Minimal JSON-shaped snapshot (DD-Fake §8). Full schema with TKB name
    /// resolution is deferred to a follow-up; this version produces a
    /// pasteable diagnostic blob from the raw component state.
    /// </summary>
    private static string BuildJsonSnapshot(Entity entity, in FakeAnimBackendState s)
    {
        var sb = new System.Text.StringBuilder(1024);
        sb.Append("{\"entity\":").Append(entity.Index)
          .Append(",\"generation\":").Append(s.Generation)
          .Append(",\"total_ticks\":").Append(s.TotalTicks)
          .Append(",\"slots\":[");
        bool firstSlot = true;
        for (int i = 0; i < 8; i++)
        {
            ref readonly var slot = ref s.Slots[i];
            if (slot.IsActive == 0) continue;
            if (!firstSlot) sb.Append(',');
            firstSlot = false;
            sb.Append("{\"index\":").Append(i)
              .Append(",\"montage_id\":").Append(slot.ActiveMontage.Hash)
              .Append(",\"elapsed\":").Append(slot.ElapsedSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture))
              .Append(",\"duration\":").Append(slot.TotalDurationSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture))
              .Append(",\"section\":").Append(slot.CurrentSectionIndex)
              .Append(",\"blend_weight\":").Append(slot.BlendWeight.ToString("F3", System.Globalization.CultureInfo.InvariantCulture))
              .Append(",\"in_blend_out\":").Append(slot.InBlendOutWindow != 0 ? "true" : "false")
              .Append(",\"fired_mask\":").Append(slot.FiredNotifyMask)
              .Append('}');
        }
        sb.Append("],\"aim\":{\"active\":").Append(s.Aim.IsActive != 0 ? "true" : "false")
          .Append(",\"releasing\":").Append(s.Aim.IsReleasing != 0 ? "true" : "false")
          .Append(",\"weight\":").Append(s.Aim.BlendWeight.ToString("F3", System.Globalization.CultureInfo.InvariantCulture))
          .Append(",\"target\":[").Append(s.Aim.TargetWorldAimPoint.X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
          .Append(',').Append(s.Aim.TargetWorldAimPoint.Y.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
          .Append(',').Append(s.Aim.TargetWorldAimPoint.Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
          .Append("]},\"stance\":{\"current\":").Append(s.Stance.CurrentStance)
          .Append(",\"target\":").Append(s.Stance.TargetStance)
          .Append(",\"transitioning\":").Append(s.Stance.IsTransitioning != 0 ? "true" : "false")
          .Append(",\"progress\":").Append(s.Stance.TransitionProgress.ToString("F3", System.Globalization.CultureInfo.InvariantCulture))
          .Append("},\"locomotion\":{\"speed\":").Append(s.HorizontalSpeed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
          .Append(",\"vertical\":").Append(s.VerticalVelocity.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
          .Append(",\"grounded\":").Append(s.IsGrounded != 0 ? "true" : "false")
          .Append(",\"foot\":").Append(s.NextFootIndex)
          .Append("},\"pending_notify_count\":").Append(s.PendingNotifyCount)
          .Append('}');
        return sb.ToString();
    }
}
