using System.Globalization;
using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Expression;

namespace NodeEditor.UI.MiniEditors;

/// <summary>
/// Drop-in replacement for <c>ImGui.DragFloat</c> / <c>ImGui.DragInt</c> that,
/// on double-click, switches to an <c>InputText</c> widget where the user may type
/// an arithmetic expression (<c>pi*2</c>, <c>sin(45 deg)</c>, etc.).
/// On commit (Enter or focus-out), the expression is evaluated via
/// <see cref="ExpressionEvaluator"/>; on failure the previous value is restored
/// and an error tooltip is shown for 2 seconds.
/// </summary>
public static class DragFloatWithExpression
{
    // Per-widget transient state keyed by ImGui widget ID.
    private static readonly Dictionary<uint, ExprState> s_states = [];

    private sealed class ExprState
    {
        public bool IsEditing;
        public string TextBuffer = "";
        public float PrevFloatValue;
        public int PrevIntValue;
        public string? ErrorMsg;
        public float ErrorTimer;
        public Vector2 ClickPos;
        public double ClickTime;
        public bool FocusPending;
    }

    /// <summary>Render a drag-float widget with expression-aware text editing.</summary>
    /// <param name="label">ImGui widget label (## prefix to suppress display).</param>
    /// <param name="value">Current value — modified when the user changes it.</param>
    /// <param name="speed">Drag speed per pixel.</param>
    /// <param name="format">Printf-style display format.</param>
    /// <returns>True when the value changed this frame.</returns>
    public static bool Render(string label, ref float value, float speed = 0.1f, string format = "%.3f")
    {
        uint id = ImGui.GetID(label);
        if (!s_states.TryGetValue(id, out var st))
        {
            st = new ExprState();
            s_states[id] = st;
        }

        bool changed = false;

        if (st.IsEditing)
        {
            bool escPressed = ImGui.IsKeyPressed(ImGuiKey.Escape);
            if (st.FocusPending)
            {
                ImGui.SetKeyboardFocusHere();
                st.FocusPending = false;
            }

            bool entered = ImGui.InputText(label, ref st.TextBuffer, 256,
                ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
            bool deactivated = ImGui.IsItemDeactivated();

            if (escPressed)
            {
                value = st.PrevFloatValue;
                st.IsEditing = false;
            }
            else if (entered || deactivated)
            {
                CommitFloat(st, ref value, ref changed);
            }

            ShowErrorTooltip(st);
        }
        else
        {
            if (ImGui.DragFloat(label, ref value, speed, 0f, 0f, format))
                changed = true;

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                st.ClickPos = ImGui.GetMousePos();
                st.ClickTime = ImGui.GetTime();
            }

            if (ImGui.IsItemDeactivated())
            {
                double timeDelta = ImGui.GetTime() - st.ClickTime;
                float dragDist = (ImGui.GetMousePos() - st.ClickPos).Length();
                if (timeDelta <= 0.15 && dragDist <= 3f)
                {
                    st.IsEditing = true;
                    st.TextBuffer = value.ToString("G6", CultureInfo.InvariantCulture);
                    st.PrevFloatValue = value;
                    st.FocusPending = true;
                }
            }
        }

        return changed;
    }

    /// <summary>Render a drag-int widget with expression-aware text editing.</summary>
    public static bool Render(string label, ref int value, float speed = 1.0f)
    {
        uint id = ImGui.GetID(label);
        if (!s_states.TryGetValue(id, out var st))
        {
            st = new ExprState();
            s_states[id] = st;
        }

        bool changed = false;

        if (st.IsEditing)
        {
            bool escPressed = ImGui.IsKeyPressed(ImGuiKey.Escape);
            if (st.FocusPending)
            {
                ImGui.SetKeyboardFocusHere();
                st.FocusPending = false;
            }

            bool entered = ImGui.InputText(label, ref st.TextBuffer, 256,
                ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
            bool deactivated = ImGui.IsItemDeactivated();

            if (escPressed)
            {
                value = st.PrevIntValue;
                st.IsEditing = false;
            }
            else if (entered || deactivated)
            {
                CommitInt(st, ref value, ref changed);
            }

            ShowErrorTooltip(st);
        }
        else
        {
            if (ImGui.DragInt(label, ref value, speed))
                changed = true;

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                st.ClickPos = ImGui.GetMousePos();
                st.ClickTime = ImGui.GetTime();
            }

            if (ImGui.IsItemDeactivated())
            {
                double timeDelta = ImGui.GetTime() - st.ClickTime;
                float dragDist = (ImGui.GetMousePos() - st.ClickPos).Length();
                if (timeDelta <= 0.15 && dragDist <= 3f)
                {
                    st.IsEditing = true;
                    st.TextBuffer = value.ToString(CultureInfo.InvariantCulture);
                    st.PrevIntValue = value;
                    st.FocusPending = true;
                }
            }
        }

        return changed;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static void CommitFloat(ExprState st, ref float value, ref bool changed)
    {
        var result = ExpressionEvaluator.Evaluate(st.TextBuffer);
        if (result.Success)
        {
            value = (float)result.Value;
            changed = true;
            st.ErrorMsg = null;
        }
        else
        {
            value = st.PrevFloatValue;
            st.ErrorMsg = result.Error;
            st.ErrorTimer = 2f;
        }
        st.IsEditing = false;
    }

    private static void CommitInt(ExprState st, ref int value, ref bool changed)
    {
        var result = ExpressionEvaluator.Evaluate(st.TextBuffer);
        if (result.Success)
        {
            value = (int)Math.Round(result.Value);
            changed = true;
            st.ErrorMsg = null;
        }
        else
        {
            value = st.PrevIntValue;
            st.ErrorMsg = result.Error;
            st.ErrorTimer = 2f;
        }
        st.IsEditing = false;
    }

    private static void ShowErrorTooltip(ExprState st)
    {
        if (st.ErrorMsg == null) return;
        st.ErrorTimer -= ImGui.GetIO().DeltaTime;
        if (st.ErrorTimer <= 0f) { st.ErrorMsg = null; return; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(st.ErrorMsg);
    }
}
