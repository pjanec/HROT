namespace Fdp.Core
{
    [ComponentId(GlobalComponentIds.EntityInfo)]
    public struct EntityInfo
    {
        public FixedString64 Name;
        public ForceId ForceId;
        public int CommanderId;
    }
}
