using System;

namespace Fhsm.Compiler.Graph
{
    public class RegionNode
    {
        public string Name { get; set; }
        public StateNode InitialState { get; set; }
        
        public RegionNode(string name, StateNode initial)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            InitialState = initial ?? throw new ArgumentNullException(nameof(initial));
        }
    }
}