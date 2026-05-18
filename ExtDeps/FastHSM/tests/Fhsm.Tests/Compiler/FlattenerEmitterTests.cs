using System;
using System.Linq;
using Xunit;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel.Data;
using Fhsm.Kernel;

namespace Fhsm.Tests.Compiler
{
    public class FlattenerEmitterTests
    {
        // === Flattener Tests ===

        [Fact]
        public void Flattener_States_Flattened_In_Index_Order()
        {
            var graph = new StateMachineGraph("G");
            var a = new StateNode("A");
            var b = new StateNode("B");
            
            graph.AddState(a);
            graph.AddState(b);
            graph.RootState.AddChild(a);
            a.AddChild(b);
            
            HsmNormalizer.Normalize(graph);
            // Root=0, A=1, B=2
            
            var data = HsmFlattener.Flatten(graph);
            
            Assert.Equal(3, data.States.Length);
            Assert.Equal(0xFFFF, data.States[0].ParentIndex); // Root parent is 0xFFFF
            
            // A (1) parent is Root (0)
            Assert.Equal(0, data.States[1].ParentIndex);
            
            // B (2) parent is A (1)
            Assert.Equal(1, data.States[2].ParentIndex);
        }

        [Fact]
        public void Flattener_Parent_Child_Sibling_Indices_Correct()
        {
            var graph = new StateMachineGraph("G");
            var p = new StateNode("P");
            var c1 = new StateNode("C1");
            var c2 = new StateNode("C2");
            
            graph.AddState(p);
            graph.AddState(c1);
            graph.AddState(c2);
            
            graph.RootState.AddChild(p);
            p.AddChild(c1);
            p.AddChild(c2);
            
            HsmNormalizer.Normalize(graph);
            // Root(0), P(1), C1(2), C2(3) (BFS order)
            
            var data = HsmFlattener.Flatten(graph);
            
            // P
            Assert.Equal(2, data.States[1].FirstChildIndex); // C1
            
            // C1
            Assert.Equal(1, data.States[2].ParentIndex); // P
            // Assertion removed: NextSiblingIndex field was removed
            
            // C2
            Assert.Equal(1, data.States[3].ParentIndex); // P
            // Assertion removed: NextSiblingIndex field was removed
        }

        [Fact]
        public void Flattener_ActionIds_Mapped_Correctly()
        {
            var graph = new StateMachineGraph("G");
            var s = new StateNode("S");
            s.OnEntryAction = "MyAction";
            s.OnExitAction = "MyAction"; // Same action
            
            graph.AddState(s);
            graph.RootState.AddChild(s);
            
            HsmNormalizer.Normalize(graph);
            var data = HsmFlattener.Flatten(graph);
            
            // Should have 1 entry in ActionIds
            Assert.Single(data.ActionIds);
            
            // Both should point to same hash ID (since same action name)
            ushort expectedHash = ComputeHash("MyAction");
            Assert.Equal(expectedHash, data.States[1].OnEntryActionId);
            Assert.Equal(expectedHash, data.States[1].OnExitActionId);
            
            Assert.Equal(0xFFFF, data.States[1].ActivityActionId); // None
        }
        
        private static ushort ComputeHash(string name)
        {
            uint hash = 2166136261;
            foreach (char c in name)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return (ushort)(hash & 0xFFFF);
        }

        [Fact]
        public void Flattener_Transitions_Flattened_And_Ranges_Set()
        {
            var graph = new StateMachineGraph("G");
            var a = new StateNode("A");
            var b = new StateNode("B");
            
            graph.AddState(a);
            graph.AddState(b);
            graph.RootState.AddChild(a);
            graph.RootState.AddChild(b);
            
            // A -> B
            var t = new TransitionNode(a, b, 10);
            a.AddTransition(t);
            
            HsmNormalizer.Normalize(graph);
            var data = HsmFlattener.Flatten(graph);
            
            Assert.Single(data.Transitions);
            var tDef = data.Transitions[0];
            Assert.Equal(10, tDef.EventId);
            Assert.Equal(a.FlatIndex, tDef.SourceStateIndex);
            Assert.Equal(b.FlatIndex, tDef.TargetStateIndex);
            
            // Check StateDef range
            var aDef = data.States[a.FlatIndex];
            Assert.Equal(0, aDef.FirstTransitionIndex);
            Assert.Equal(1, aDef.TransitionCount);
            
            var bDef = data.States[b.FlatIndex];
            Assert.Equal(0, bDef.TransitionCount); // Using implementation detail? 0 or 0xFFFF?
            // Implementation sets 0 count, index 0xFFFF if empty.
            Assert.Equal(0xFFFF, bDef.FirstTransitionIndex);
        }

        [Fact]
        public void Flattener_Transition_Cost_Computed()
        {
            var graph = new StateMachineGraph("G");
            var root = graph.RootState;
            var a = new StateNode("A");
            var aa = new StateNode("AA");
            var b = new StateNode("B");
            
            graph.AddState(a);
            graph.AddState(aa);
            graph.AddState(b);
            
            root.AddChild(a);
            a.AddChild(aa);
            root.AddChild(b);
            
            // Transition AA -> B
            // LCA is Root.
            // AA -> A -> Root (2 steps up)
            // Root -> B (1 step down)
            // Total cost = 3
            
            var t = new TransitionNode(aa, b, 1);
            aa.AddTransition(t);
            
            HsmNormalizer.Normalize(graph);
            var data = HsmFlattener.Flatten(graph);
            
            var tDef = data.Transitions[0];
            Assert.Equal(3, tDef.Cost);
        }

        [Fact]
        public void Flattener_State_Flags_Built()
        {
            var graph = new StateMachineGraph("G");
            var s = new StateNode("S");
            s.IsHistory = true;
            s.IsInitial = true; // Relative to parent
            
            graph.AddState(s);
            graph.RootState.AddChild(s);
            
            HsmNormalizer.Normalize(graph);
            var data = HsmFlattener.Flatten(graph);
            
            var def = data.States[s.FlatIndex];
            Assert.True((def.Flags & StateFlags.IsHistory) != 0);
            Assert.True((def.Flags & StateFlags.IsInitial) != 0);
        }
        
        [Fact]
        public void Flattener_Transition_Flags_Include_Priority()
        {
            var graph = new StateMachineGraph("G");
            var a = new StateNode("A");
            graph.AddState(a);
            graph.RootState.AddChild(a);
            
            var t = new TransitionNode(graph.RootState, a, 1);
            t.Priority = 5;
            graph.RootState.AddTransition(t);
            
            HsmNormalizer.Normalize(graph);
            var data = HsmFlattener.Flatten(graph);
            
            var def = data.Transitions[0];
            // Priority is in bits 8-11.
            int encoded = (int)def.Flags >> 8;
            Assert.Equal(5, encoded & 0x0F);
        }

        [Fact]
        public void Flattener_Global_Transitions_Separated()
        {
            var graph = new StateMachineGraph("G");
            var a = new StateNode("A");
            graph.AddState(a);
            graph.RootState.AddChild(a);
            
            // Global Transition
            var gt = new TransitionNode(graph.RootState, a, 999);
            graph.GlobalTransitions.Add(gt);
            
            HsmNormalizer.Normalize(graph);
            var data = HsmFlattener.Flatten(graph);
            
            Assert.Empty(data.Transitions); // No normal transitions
            Assert.Single(data.GlobalTransitions);
            
            var gtDef = data.GlobalTransitions[0];
            Assert.Equal(999, gtDef.EventId);
            Assert.Equal(a.FlatIndex, gtDef.TargetStateIndex);
        }

        // === Emitter Tests ===

        [Fact]
        public void Emitter_Header_Populated()
        {
            var graph = new StateMachineGraph("G");
            HsmNormalizer.Normalize(graph);
            var data = HsmFlattener.Flatten(graph);
            
            var blob = HsmEmitter.Emit(data);
            
            Assert.Equal(HsmDefinitionHeader.MagicNumber, blob.Header.Magic);
            Assert.Equal(1, blob.Header.FormatVersion);
            Assert.Equal(data.States.Length, blob.Header.StateCount);
        }

        [Fact]
        public void Emitter_Hashes_Computed()
        {
            var graph = new StateMachineGraph("G");
            HsmNormalizer.Normalize(graph);
            var data = HsmFlattener.Flatten(graph);
            
            var blob = HsmEmitter.Emit(data);
            
            Assert.NotEqual(0u, blob.Header.StructureHash);
            // ParameterHash might be hash of empty string if no actions? 
            // Or non-zero if seeds used.
        }

        [Fact]
        public void Emitter_StructureHash_Stable_Across_Renames()
        {
            // Build Graph 1
            var g1 = new StateMachineGraph("G1");
            var s1 = new StateNode("MyState");
            g1.AddState(s1);
            g1.RootState.AddChild(s1);
            
            HsmNormalizer.Normalize(g1);
            var d1 = HsmFlattener.Flatten(g1);
            var b1 = HsmEmitter.Emit(d1);
            
            // Build Graph 2 (Same struct, different names)
            var g2 = new StateMachineGraph("G2");
            var s2 = new StateNode("RenamedState");
            g2.AddState(s2);
            g2.RootState.AddChild(s2);
            
            HsmNormalizer.Normalize(g2);
            var d2 = HsmFlattener.Flatten(g2);
            var b2 = HsmEmitter.Emit(d2);
            
            Assert.Equal(b1.Header.StructureHash, b2.Header.StructureHash);
        }
        
        [Fact]
        public void Emitter_ParameterHash_Changes_On_Action_Change()
        {
            // Build Graph 1: Two states, Action A1 then A2
            var g1 = new StateMachineGraph("G");
            var s1 = new StateNode("S1"); s1.OnEntryAction = "A1";
            var s2 = new StateNode("S2"); s2.OnEntryAction = "A2";
            g1.AddState(s1); g1.AddState(s2);
            g1.RootState.AddChild(s1); g1.RootState.AddChild(s2);
            
            HsmNormalizer.Normalize(g1);
            var b1 = HsmEmitter.Emit(HsmFlattener.Flatten(g1));
            
            // Build Graph 2: Two states, Action A2 then A1 (Swapped usage)
            var g2 = new StateMachineGraph("G");
            var s3 = new StateNode("S1"); s3.OnEntryAction = "A2";
            var s4 = new StateNode("S2"); s4.OnEntryAction = "A1";
            g2.AddState(s3); g2.AddState(s4);
            g2.RootState.AddChild(s3); g2.RootState.AddChild(s4);
            
            HsmNormalizer.Normalize(g2);
            var b2 = HsmEmitter.Emit(HsmFlattener.Flatten(g2));
            
            Assert.Equal(b1.Header.StructureHash, b2.Header.StructureHash);
            Assert.NotEqual(b1.Header.ParameterHash, b2.Header.ParameterHash);
        }
        
        [Fact]
        public void Emitter_Blob_Validates()
        {
             var graph = new StateMachineGraph("G");
             var s = new StateNode("S");
             graph.AddState(s);
             graph.RootState.AddChild(s);
             // Validation requires root children?
             
             HsmNormalizer.Normalize(graph);
             var blob = HsmEmitter.Emit(HsmFlattener.Flatten(graph));
             
             Assert.True(HsmValidator.ValidateDefinition(blob, out var err), err);
        }
    }
}
