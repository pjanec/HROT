using System;

namespace Fdp.Core
{
    public interface IEventBus
    {
        void Publish<T>(T evt) where T : unmanaged;
        void PublishManaged<T>(T evt); // No class constraint — allows managed structs (value types containing references)
    }
}
