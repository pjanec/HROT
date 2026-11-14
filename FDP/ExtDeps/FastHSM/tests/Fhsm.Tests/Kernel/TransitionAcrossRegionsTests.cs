using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Fhsm.Compiler;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Fhsm.Tests.Kernel
{
    /// <summary>
    /// ⭐ CE-1005 (docs/projects/FDP/ExtDeps/FastHSM/Fhsm.Kernel.md "Transition Execution Across Regions") — a transition exits and
    /// enters by REGION SLOT, not only along the source→target path. Measured before: entering a parallel state started one region
    /// (D1), a transition to a non-initial child from outside its parent landed on the parent's initial child (D2), and a transition
    /// declared on a composite skipped its active child's exit action (D3).
    /// </summary>
    [Collection("HsmActionDispatcher")]
    public unsafe class TransitionAcrossRegionsTests
    {
        private static readonly List<string> Log = new();
        private static Dictionary<ushort, string> _names = new();


        private static HsmDefinitionBlob Compile(HsmBuilder b)
        {
            var g = b.Build(); HsmNormalizer.Normalize(g);
            var errors = HsmGraphValidator.Validate(g).Where(e => e.Severity == HsmGraphValidator.ErrorSeverity.Error).ToList();
            Assert.True(errors.Count == 0, string.Join("; ", errors));
            var blob = HsmEmitter.Emit(HsmFlattener.Flatten(g)); blob.Metadata = HsmEmitter.BuildMachineMetadata(g); return blob;
        }

        private static ushort Id(HsmDefinitionBlob blob, string name) => blob.Metadata.StateNames.First(kv => kv.Value == name).Key;

        private static string[] Leaves(HsmDefinitionBlob blob, ref HsmInstance128 inst)
        {
            var r = new string[blob.Header.RegionCount];
            for (int i = 0; i < r.Length; i++) { ushort l = inst.ActiveLeafIds[i]; r[i] = l == 0xFFFF ? "-" : blob.Metadata.StateNames[l]; }
            return r;
        }

        private static void Pump(HsmDefinitionBlob blob, ref HsmInstance128 inst, ushort? ev = null)
        {
            fixed (HsmInstance128* p = &inst) { if (ev is { } e) HsmEventQueue.TryEnqueue(p, 128, new HsmEvent { EventId = e }); }
            for (int i = 0; i < 6; i++) HsmKernel.Update(blob, ref inst, 0, 0.016f);
        }

        // Actions are told apart by the stamped occurrence (the state that owns the call) — one shared function per kind.
        private static void OnEntry(void* i, void* c, HsmCommandWriter* w) => Log.Add("enter " + _names[w->OccurrenceStateId]);
        private static void OnExit(void* i, void* c, HsmCommandWriter* w) => Log.Add("exit " + _names[w->OccurrenceStateId]);

        private static void Instrument(HsmDefinitionBlob blob)
        {
            HsmActionDispatcher.ClearAll();
            Log.Clear();
            _names = blob.Metadata.StateNames.ToDictionary(kv => kv.Key, kv => kv.Value);
            for (ushort s = 0; s < blob.Header.StateCount; s++)
            {
                ref readonly var d = ref blob.GetState(s);
                if (d.OnEntryActionId is not 0 and not 0xFFFF)
                    HsmActionDispatcher.RegisterAction(d.OnEntryActionId, (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&OnEntry);
                if (d.OnExitActionId is not 0 and not 0xFFFF)
                    HsmActionDispatcher.RegisterAction(d.OnExitActionId, (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&OnExit);
            }
        }

        private static StateBuilder Traced(StateBuilder s, string name) => s.OnEntry("T.Enter" + name).OnExit("T.Exit" + name);

        /// <summary>Idle (initial) --Go(1)--> P (parallel: A | B) --Leave(3)--> Idle · Idle --Second(2)--> C.Second · Idle --ToC(4)--> C
        /// (C: First initial, Second; C --Leave(3)--> Idle).</summary>
        private static HsmDefinitionBlob Machine()
        {
            var b = new HsmBuilder("AcrossRegions");
            b.Event("Go", 1); b.Event("Second", 2); b.Event("Leave", 3); b.Event("ToC", 4);
            var idle = Traced(b.State("Idle").Initial(), "Idle");
            var c = Traced(b.State("C"), "C");
            c.Child("First", x => Traced(x.Initial(), "First"));
            c.Child("Second", x => Traced(x, "Second"));
            var p = Traced(b.State("P").Parallel(), "P");
            p.Child("A", x => Traced(x.Initial(), "A"));
            p.Child("B", x => Traced(x.Initial(), "B"));
            idle.On(1).GoTo("P");
            idle.On(2).GoTo("Second");
            idle.On(4).GoTo("C");
            p.On(3).GoTo("Idle");
            c.On(3).GoTo("Idle");
            foreach (var n in new[] { "Idle", "C", "First", "Second", "P", "A", "B" })
            {
                b.RegisterAction("T.Enter" + n);
                b.RegisterAction("T.Exit" + n);
            }
            return Compile(b);
        }

        [Fact]
        public void CE1005_D1_EnteringAParallelState_StartsEveryRegion()
        {
            var blob = Machine();
            var inst = new HsmInstance128(); HsmInstanceManager.Initialize(&inst, blob);
            Pump(blob, ref inst);
            Instrument(blob);
            Pump(blob, ref inst, 1);

            Assert.Equal(new[] { "P", "A", "B" }, Leaves(blob, ref inst));
            Assert.Equal(new[] { "exit Idle", "enter P", "enter A", "enter B" }, Log);
            HsmActionDispatcher.ClearAll();
        }

        [Fact]
        public void CE1005_D1_LeavingAParallelState_ExitsEveryRegion_InnermostFirst()
        {
            var blob = Machine();
            var inst = new HsmInstance128(); HsmInstanceManager.Initialize(&inst, blob);
            Pump(blob, ref inst);
            Pump(blob, ref inst, 1);
            Instrument(blob);
            Pump(blob, ref inst, 3);

            Assert.Equal(new[] { "Idle", "-", "-" }, Leaves(blob, ref inst));
            Assert.Equal(4, Log.Count);
            Assert.Equal(new[] { "exit A", "exit B" }, Log.Take(2).OrderBy(x => x));   // both regions, before their parent
            Assert.Equal("exit P", Log[2]);
            Assert.Equal("enter Idle", Log[3]);
            HsmActionDispatcher.ClearAll();
        }

        [Fact]
        public void CE1005_D2_ATransitionToANonInitialChild_LandsOnIt()
        {
            var blob = Machine();
            var inst = new HsmInstance128(); HsmInstanceManager.Initialize(&inst, blob);
            Pump(blob, ref inst);
            Instrument(blob);
            Pump(blob, ref inst, 2);

            Assert.Equal("Second", Leaves(blob, ref inst)[0]);
            Assert.Equal(new[] { "exit Idle", "enter C", "enter Second" }, Log);   // never the initial First
            HsmActionDispatcher.ClearAll();
        }

        [Fact]
        public void CE1005_ACompositeTarget_DrillsToItsInitialLeaf_RunningItsEntry()
        {
            var blob = Machine();
            var inst = new HsmInstance128(); HsmInstanceManager.Initialize(&inst, blob);
            Pump(blob, ref inst);
            Instrument(blob);
            Pump(blob, ref inst, 4);

            Assert.Equal("First", Leaves(blob, ref inst)[0]);
            Assert.Equal(new[] { "exit Idle", "enter C", "enter First" }, Log);   // the initial child is ENTERED, not just pointed at
            HsmActionDispatcher.ClearAll();
        }

        [Fact]
        public void CE1005_D3_ATransitionOnAComposite_ExitsItsActiveChildFirst()
        {
            var blob = Machine();
            var inst = new HsmInstance128(); HsmInstanceManager.Initialize(&inst, blob);
            Pump(blob, ref inst);
            Pump(blob, ref inst, 2);           // into C.Second
            Instrument(blob);
            Pump(blob, ref inst, 3);           // C --Leave--> Idle, declared on C

            Assert.Equal("Idle", Leaves(blob, ref inst)[0]);
            Assert.Equal(new[] { "exit Second", "exit C", "enter Idle" }, Log);
            HsmActionDispatcher.ClearAll();
        }

        [Fact]
        public void CE1005_TheFlattener_RecordsEachStatesRegionSlot()
        {
            var b = new HsmBuilder("Slots");
            var p = b.State("P").Initial().Parallel();
            p.Child("Worker", c => { c.Initial(); c.InRegion(0); });
            p.Child("Done",   c => c.InRegion(0));
            p.Child("Other",  c => { c.Initial(); c.InRegion(1); });
            var blob = Compile(b);

            Assert.Equal(0, blob.GetState(Id(blob, "P")).RegionSlot);
            Assert.Equal(1, blob.GetState(Id(blob, "Worker")).RegionSlot);
            Assert.Equal(1, blob.GetState(Id(blob, "Done")).RegionSlot);    // a declared region's members share its slot
            Assert.Equal(2, blob.GetState(Id(blob, "Other")).RegionSlot);
        }
    }
}
