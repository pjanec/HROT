namespace Fhsm.Compiler.Graph
{
    public class EventDefinition
    {
        public string Name { get; set; }
        public ushort Id { get; set; }
        public int PayloadSize { get; set; }
        public bool IsIndirect { get; set; }
        public bool IsDeferred { get; set; }

        public EventDefinition(string name, ushort id)
        {
            Name = name;
            Id = id;
        }
    }
}
