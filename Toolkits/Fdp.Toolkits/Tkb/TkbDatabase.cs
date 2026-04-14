using System;
using System.Collections.Generic;
using Fdp.Interfaces;

namespace Fdp.Toolkit.Tkb
{
    /// <summary>
    /// Manages entity templates (Transient Knowledge Base).
    /// Used to spawn pre-configured entities.
    /// </summary>
    public class TkbDatabase : ITkbDatabase
    {
        private readonly Dictionary<string, TkbTemplate> _byName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<long, TkbTemplate> _byType = new();

        /// <summary>
        /// Registers a new template.
        /// </summary>
        public void Register(TkbTemplate template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            
            if (_byName.ContainsKey(template.Name))
                throw new InvalidOperationException($"Template with name '{template.Name}' already exists.");
                
            if (_byType.ContainsKey(template.TkbType))
                throw new InvalidOperationException($"Template with TkbType '{template.TkbType}' already exists.");
                
            _byName[template.Name] = template;
            _byType[template.TkbType] = template;
        }
        
        public TkbTemplate GetByType(long tkbType)
        {
            if (!_byType.TryGetValue(tkbType, out var template))
                throw new KeyNotFoundException($"Template with TkbType {tkbType} not found.");
            return template;
        }

        public bool TryGetByType(long tkbType, out TkbTemplate template)
        {
            return _byType.TryGetValue(tkbType, out template);
        }
        
        public TkbTemplate GetByName(string name)
        {
             if (!_byName.TryGetValue(name, out var template))
                throw new KeyNotFoundException($"Template with Name {name} not found.");
            return template;
        }

        public bool TryGetByName(string name, out TkbTemplate template)
        {
            return _byName.TryGetValue(name, out template);
        }
        
        public IEnumerable<TkbTemplate> GetAll()
        {
            return _byType.Values;
        }
    }
}
