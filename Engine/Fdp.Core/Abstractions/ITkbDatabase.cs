using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Interfaces
{
    [ComponentId(GlobalComponentIds.ITkbDatabase)]
    public interface ITkbDatabase
    {
        // Template registration
        void Register(TkbTemplate template);
        
        // Lookup by TkbType (primary key)
        TkbTemplate GetByType(long tkbType);

		// Convenience method for primary key lookup, for backward compatibility
		TkbTemplate GetTemplate(long tkbType) => GetByType(tkbType);

        bool TryGetByType(long tkbType, out TkbTemplate template);
        
        // Lookup by name (secondary key)  
        TkbTemplate GetByName(string name);
        bool TryGetByName(string name, out TkbTemplate template);
        
        // Enumeration
        IEnumerable<TkbTemplate> GetAll();
    }
}
