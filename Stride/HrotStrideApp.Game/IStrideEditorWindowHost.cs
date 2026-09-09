#nullable enable

namespace HrotStrideApp;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-213</c> / ruling <c>R-S17</c> — the small optional contract that makes the raylib
/// window host USABLE BY BOTH MODES.</b>
/// </summary>
/// <remarks>
/// <para><b>📌 Why this exists.</b> <c>DESIGN_Stride_Node_Modes.md</c> §7.2 described the window host as
/// a generic <i>"raylib window + <c>WindowManager</c> + dockspace + message log"</i> and made
/// <c>CE-214</c>/<c>S6</c> depend on that. 📐 Measured, it was false: the host took an
/// <c>EditorStrideSubsystem</c> outright, so a mode-2 node — which has no editor subsystem at all —
/// could not construct it. 🔒 The user's ruling was <i>"point 1: widen"</i>: <c>CE-213</c> grows from
/// <i>delete + rename</i> to <i>delete + rename + DECOUPLE</i>.</para>
///
/// <para><b>⭐ Why an interface with exactly these three members, and nothing else.</b> The coupling was
/// measured before it was replaced, not guessed at: <b>7 references, 3 members</b> — <c>HostedEditor</c>
/// (×3), <c>ToastMessage</c>, <c>ToastSecondsRemaining</c>. ⛔ Nothing else about the editor subsystem
/// was ever reachable from the window, so widening this contract would invent coupling rather than
/// describe it.</para>
///
/// <para><b>⭐⭐ The whole contract is OPTIONAL, and that is the point.</b> Two of the three uses were
/// ALREADY null-guarded in the window (<c>if (HostedEditor != null)</c> and <c>editor?.DrawUI()</c>)
/// because they are mode 1's <i>panel fill</i> — precisely what §7.2 says mode 2 must not have. ⇒ mode 2
/// passes <see langword="null"/> for the whole host and the window degrades to exactly what that mode
/// wants: the raylib window, the <c>WindowManager</c>, the dockspace and the message log, with whatever
/// panels ITS composition registers.</para>
///
/// <para>⚠ <b>The rename is NOT done here.</b> §7.1/<c>CE-213</c> also calls for
/// <c>StrideInspectorWindow</c> → <c>StrideEditorWindow</c>. ⛔ That is a C# symbol rename, and this
/// machine has no Roslyn — the canon bans renaming a symbol by text search-and-replace, so it is
/// deliberately left for a Roslyn-capable session. ⭐ The decouple is the half <c>S6</c> is gated on;
/// the rename is cosmetic and blocks nothing.</para>
/// </remarks>
public interface IStrideEditorWindowHost
{
    /// <summary>
    /// The hosted editor, or <see langword="null"/> when this host has none (mode 2, and mode 1 before
    /// <c>buildEditorUi</c>). ⭐ The window calls <c>RegisterWindows</c> once at open and
    /// <c>DrawUI</c> per frame, both already null-tolerant.
    /// </summary>
    Hrot.Editor.EditorSubsystem? HostedEditor { get; }

    /// <summary>Seconds left on the transient toast overlay; ⭐ <c>0</c> means "draw nothing".</summary>
    float ToastSecondsRemaining { get; }

    /// <summary>The toast text. ⚠ Only read when <see cref="ToastSecondsRemaining"/> is positive.</summary>
    string ToastMessage { get; }
}
