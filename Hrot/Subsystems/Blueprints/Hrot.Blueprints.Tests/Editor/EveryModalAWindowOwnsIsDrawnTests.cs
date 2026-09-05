using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Presentation.WindowManager;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>Batch 100 (<c>100d</c>) — THE CLASS, not the instance: a modal a window OWNS must be
/// DRAWN by it.</b>
///
/// <para>⛔⛔⛔ <b>THIS DEFECT HAS NOW HAPPENED THREE TIMES.</b> Batch 87 shipped <i>"the modal
/// draws"</i> · Batch 89 fixed <i>"<c>Draw</c> had no caller"</i> · Batch 99 built the Properties form
/// with a field, a constructor call, an <c>Open</c> and a test accessor — and ⛔ <b>no line calling
/// <c>Draw()</c>.</b> ⚠⚠ <b>Each time, the batch's own rails were green</b>, because <c>IsOpen</c> and
/// the commit path were both genuinely correct. 📌 <c>BP-327</c>.</para>
///
/// <para>⭐⭐⭐ <b>Why a rail on the specific window would not have been enough</b> — 📌 the handoff:
/// <i>"and the CLASS, not just the instance."</i> A rail that names one window
/// catches the instance we already know about; ⛔ <b>it does nothing for the fourth occurrence</b>,
/// which will be in a window nobody has written yet.</para>
///
/// <para>⭐⭐ <b>HOW IT WORKS, and why it is not a grep.</b> It reads the <b>IL</b> of the window's draw
/// methods and resolves every <c>call</c>/<c>callvirt</c> token, then asks whether the owned modal's
/// <c>Draw</c> is among them. ⇒ ⭐ it sees through helper methods, and ⛔ a commented-out call or a
/// call in a doc example cannot fool it — both of which a text scan would accept.</para>
///
/// <para>⚠ <b>WHAT IT CANNOT SEE</b> *(📌 <c>M-29</c>, stated rather than implied)*: <b>reachability</b>.
/// A <c>Draw()</c> call sitting <b>after an early <c>return</c></b> is present in the IL and this rail
/// passes. ⇒ ⭐ the frame rail <c>ThePropertiesFormIsVisibleWhenOpenedTests</c> exists alongside this
/// one: ⭐⭐ <b>this rail proves the call exists; that one proves the dialog appears.</b></para>
///
/// <para>⚠⚠ <b>AND A SCOPE LOSS <c>S1</c> CREATED, named rather than left to be discovered.</b>
/// 📐 This rail only sees a modal held in a FIELD OF A <c>ManagedWindow</c>. §7.3 ① retired
/// <c>BlueprintDetailsWindow</c>, and the Properties form now lives in
/// <c>BlueprintDetailsContribution</c> as a frame OVERLAY — ⛔ so it is <b>out of this rail's
/// scope</b>. ⭐ It is not uncovered: the frame rail above renders <c>WindowManager.FrameOverlays</c>
/// and reddens if the installer never registered it, which is the same question one floor up. ⚠ The
/// rail below still guards every OTHER window-owned modal, which is what it was written for.</para>
/// </summary>
public sealed class EveryModalAWindowOwnsIsDrawnTests
{
    /// <summary>⭐ The production assemblies that define editor windows. ⛔ Not "all loaded assemblies":
    /// that would sweep in test doubles and make the rail's own scope drift.</summary>
    private static IEnumerable<Assembly> EditorAssemblies() => new[]
    {
        typeof(Hrot.Blueprints.Editor.Windows.BlueprintNodeDetailsView).Assembly,
        typeof(Hrot.Editor.AiShared.Variables.VariableEditModal).Assembly,
        typeof(Hrot.Hsm.Editor.Windows.HsmEventsWindow).Assembly,
    }.Distinct();

    /// <summary>
    /// ⭐⭐ <b>What counts as "a modal this window owns".</b> An instance field whose type declares a
    /// parameterless <c>Draw()</c> and whose name ends in <c>Modal</c>.
    ///
    /// <para>⚠ <b>The name test is deliberate and it is the rail's main limitation.</b> ⛔ Dropping it
    /// and taking <i>any</i> field with a <c>Draw()</c> sweeps in panels, sections and controls that
    /// are legitimately drawn by something else — the rail would flag a dozen correct designs and be
    /// switched off within a batch. 📌 That exact failure is on record *(the optional-parameter sweep,
    /// <c>2026-08-16</c>)*. ⭐ A modal is the case where "who draws it?" has exactly one right answer:
    /// <b>its owner</b>.</para>
    /// </summary>
    private static bool IsOwnedModal(FieldInfo f)
        => f.FieldType.Name.EndsWith("Modal", StringComparison.Ordinal)
        && DrawMethodOf(f.FieldType) is not null;

    private static MethodInfo? DrawMethodOf(Type t)
        => t.GetMethod("Draw",
               BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
               binder: null, types: Type.EmptyTypes, modifiers: null);

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL.</b> For every <c>ManagedWindow</c> in the editor assemblies, every modal it
    /// owns must be reached from its own drawing code.
    /// </summary>
    [Fact]
    public void EveryWindowOwnedModalIsReachedFromThatWindowsDraw()
    {
        var offenders = new List<string>();
        int windowsWithModals = 0, modalsChecked = 0;

        foreach (var window in EditorAssemblies()
                     .SelectMany(a => a.GetTypes())
                     .Where(t => !t.IsAbstract && typeof(ManagedWindow).IsAssignableFrom(t)))
        {
            var modals = window
                .GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                .Where(IsOwnedModal)
                .ToList();

            if (modals.Count == 0) continue;
            windowsWithModals++;

            var called = CalledMethods(window);

            foreach (var modal in modals)
            {
                modalsChecked++;
                var draw = DrawMethodOf(modal.FieldType)!;
                if (!called.Contains(draw))
                    offenders.Add($"{window.Name} owns {modal.FieldType.Name} " +
                                  $"({modal.Name}) and never calls its Draw()");
            }
        }

        // ⭐⭐ ANTI-VACUITY, and it is not decoration: if the reflection filter silently stopped
        //    matching — a rename, a moved assembly — this rail would pass by finding nothing, which is
        //    exactly how the defect it guards survived three batches of green.
        Assert.True(windowsWithModals > 0, "found no window owning a modal — the filter has rotted");
        Assert.True(modalsChecked      > 0, "found no owned modal — the filter has rotted");

        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    /// <summary>
    /// ⭐⭐ Every method reachable from the window's own drawing entry points, by walking IL call
    /// tokens.
    ///
    /// <para>⭐ Starts at <c>DrawClientArea</c> and <c>Draw</c> — the two entry points
    /// <c>ManagedWindow</c> defines — and follows calls into the window's OWN methods, so a modal
    /// drawn from a private helper still counts. ⛔ It does not follow calls out of the type: that
    /// would walk most of the editor and turn every window into a false pass.</para>
    /// </summary>
    private static HashSet<MethodBase> CalledMethods(Type window)
    {
        var visited = new HashSet<MethodBase>();
        var result  = new HashSet<MethodBase>();
        var queue   = new Queue<MethodBase>();

        foreach (var name in new[] { "DrawClientArea", "Draw" })
        {
            var m = window.GetMethod(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null, types: Type.EmptyTypes, modifiers: null);
            if (m != null) queue.Enqueue(m);
        }

        while (queue.Count > 0)
        {
            var method = queue.Dequeue();
            if (!visited.Add(method)) continue;

            foreach (var callee in ResolveCalls(method))
            {
                result.Add(callee);
                // ⭐ Recurse only into the window's own code — see the remark above.
                if (callee.DeclaringType == window && callee is MethodInfo mi) queue.Enqueue(mi);
            }
        }

        return result;
    }

    /// <summary>
    /// ⭐ Reads <c>call</c> (<c>0x28</c>) and <c>callvirt</c> (<c>0x6F</c>) tokens out of a method body.
    /// ⚠ <b>A deliberately small IL reader</b>: it scans for the two opcodes rather than decoding every
    /// instruction, so an operand byte that happens to equal <c>0x28</c> can produce a spurious token —
    /// ⭐ which <c>ResolveMethod</c> then rejects, and the <c>catch</c> discards. ⛔ It can therefore
    /// over-report calls, never under-report them; ⚠ and over-reporting is the direction that makes
    /// this rail WEAKER, not wrong, so it is stated rather than engineered away.
    /// </summary>
    private static IEnumerable<MethodBase> ResolveCalls(MethodBase method)
    {
        byte[]? il;
        try { il = method.GetMethodBody()?.GetILAsByteArray(); }
        catch { yield break; }
        if (il is null) yield break;

        var module   = method.Module;
        var typeArgs = method.DeclaringType?.IsGenericType == true
            ? method.DeclaringType.GetGenericArguments() : null;

        for (int i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] != 0x28 && il[i] != 0x6F) continue;

            int token = BitConverter.ToInt32(il, i + 1);
            MethodBase? callee = null;
            try { callee = module.ResolveMethod(token, typeArgs, null); }
            catch { /* ⭐ not a method token — see the remark */ }

            if (callee != null) yield return callee;
        }
    }
}
