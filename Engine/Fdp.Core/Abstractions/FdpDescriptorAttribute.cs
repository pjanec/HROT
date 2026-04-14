using System;

namespace FDP.Interfaces.Abstractions
{
    /// <summary>
    /// Attribute to mark types for automatic translator generation (zero boilerplate networking).
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class FdpDescriptorAttribute : Attribute
    {
        public int Ordinal { get; }
        public string TopicName { get; }
        public bool IsMandatory { get; set; } = false;

        public FdpDescriptorAttribute(int ordinal, string topicName)
        {
            Ordinal = ordinal;
            TopicName = topicName;
        }
    }
}
