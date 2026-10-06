using System;
using System.Linq;
using Xunit;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Fhsm.Tests.Kernel
{
    /// <summary>
    /// ⭐ CE-1003 (docs/blueprints/Architect_Question_84 §6, lean A0) — a parallel state's DECLARED regions.
    /// The FastHSM design (§2.2–§2.4) has named regions, each a sub-state machine with its own initial state. The
    /// flattener used to make EVERY child of a parallel state a region of its own, so a region holding a sequence
    /// (Worker → Done) ran Worker AND Done at once. Children that declare a region (<c>InRegion(n)</c>) are now
    /// grouped; children that declare none keep the old one-region-each rule.
    /// </summary>
    public unsafe class DeclaredRegionTests
    {
        private static HsmDefinitionBlob Compile(HsmBuilder builder)
        {
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            var errors = HsmGraphValidator.Validate(graph).Where(e => e.Severity == HsmGraphValidator.ErrorSeverity.Error).ToList();
            Assert.True(errors.Count == 0, string.Join("; ", errors));
            var blob = HsmEmitter.Emit(HsmFlattener.Flatten(graph));
            blob.Metadata = HsmEmitter.BuildMachineMetadata(graph);
            return blob;
        }

        private static ushort IndexOf(HsmDefinitionBlob blob, string name)
            => blob.Metadata.StateNames.First(kv => kv.Value == name).Key;

        /// <summary>P (parallel) → region 0: Worker (initial) → Done on event 1 · region 1: Other.</summary>
        private static HsmDefinitionBlob BuildWorkerDoneMachine(bool declareRegions)
        {
            var builder = new HsmBuilder("Declared");
            builder.Event("Finish", 1);
            var p = builder.State("P").Initial().Parallel();
            StateBuilder worker = null!;
            p.Child("Worker", c => { worker = c; c.Initial(); if (declareRegions) c.InRegion(0); });
            p.Child("Done",   c => { if (declareRegions) c.InRegion(0); });
            p.Child("Other",  c => { c.Initial(); if (declareRegions) c.InRegion(1); });
            worker.On(1).GoTo("Done");
            return Compile(builder);
        }

        private static ushort[] Leaves(ref HsmInstance128 inst, int slots)
        {
            var r = new ushort[slots];
            for (int i = 0; i < slots; i++) r[i] = inst.ActiveLeafIds[i];
            return r;
        }

        [Fact]
        public void CE1003_ADeclaredRegion_StartsOnlyItsInitialState()
        {
            var blob = BuildWorkerDoneMachine(declareRegions: true);
            Assert.Equal(3, blob.Header.RegionCount);              // root + two DECLARED regions (not three children)

            var inst = new HsmInstance128();
            HsmInstanceManager.Initialize(&inst, blob);
            for (int i = 0; i < 4; i++) HsmKernel.Update(blob, ref inst, 0, 0.016f);

            var leaves = Leaves(ref inst, 3);
            Assert.Contains(IndexOf(blob, "Worker"), leaves);
            Assert.Contains(IndexOf(blob, "Other"), leaves);
            Assert.DoesNotContain(IndexOf(blob, "Done"), leaves);
        }

        [Fact]
        public void CE1003_ATransitionInsideADeclaredRegion_MovesThatRegion_AndLeavesTheOtherAlone()
        {
            var blob = BuildWorkerDoneMachine(declareRegions: true);
            var inst = new HsmInstance128();
            HsmInstanceManager.Initialize(&inst, blob);
            for (int i = 0; i < 4; i++) HsmKernel.Update(blob, ref inst, 0, 0.016f);

            HsmEventQueue.TryEnqueue(&inst, 128, new HsmEvent { EventId = 1 });
            for (int i = 0; i < 4; i++) HsmKernel.Update(blob, ref inst, 0, 0.016f);

            var leaves = Leaves(ref inst, 3);
            Assert.Contains(IndexOf(blob, "Done"), leaves);
            Assert.Contains(IndexOf(blob, "Other"), leaves);
            Assert.DoesNotContain(IndexOf(blob, "Worker"), leaves);
        }

        [Fact]
        public void CE1003_UndeclaredChildren_KeepTheOneRegionPerChildRule()
        {
            // Hand-written machines that never call InRegion compile exactly as before: three children, three regions.
            var blob = BuildWorkerDoneMachine(declareRegions: false);
            Assert.Equal(4, blob.Header.RegionCount);
        }

        [Fact]
        public void CE1003_TwoInitialStatesInOneDeclaredRegion_IsAValidationError()
        {
            var builder = new HsmBuilder("TwoInitials");
            var p = builder.State("P").Initial().Parallel();
            p.Child("A", c => c.Initial().InRegion(0));
            p.Child("B", c => c.Initial().InRegion(0));
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            var errors = HsmGraphValidator.Validate(graph);
            Assert.Contains(errors, e => e.Message.Contains("Region 0 has 2 initial states"));
        }
    }
}
