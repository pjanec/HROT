using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.ReplayBrowser
{
    /// <summary>
    /// Maintains a back/forward navigation stack of entity selections.
    /// While GoBack/GoForward is executing, PushSelection calls are suppressed
    /// to prevent history pollution.
    /// </summary>
    public sealed class EntitySelectionHistory
    {
        private readonly List<Entity> _history = new();
        private int _currentIndex = -1;

        // Mutex flag to prevent history pollution during Back/Forward operations
        private bool _isNavigating;

        /// <summary>
        /// Event fired when the selection changes.
        /// The UI must bind to this to update the active InspectorState.
        /// </summary>
        public event Action<Entity>? OnSelectionChanged;

        public bool CanGoBack => _currentIndex > 0;
        public bool CanGoForward => _currentIndex < _history.Count - 1;

        /// <summary>
        /// Invoked by the UI panels when an entity deep link is explicitly clicked by the user.
        /// </summary>
        public void PushSelection(Entity entity)
        {
            if (_isNavigating) return;

            // Prevent pushing a duplicate if the user clicks the currently selected entity
            if (_currentIndex >= 0 && _history[_currentIndex] == entity) return;

            // If we navigated back and then clicked a new link, truncate the forward history
            if (_currentIndex < _history.Count - 1)
            {
                _history.RemoveRange(_currentIndex + 1, _history.Count - (_currentIndex + 1));
            }

            _history.Add(entity);
            _currentIndex++;

            OnSelectionChanged?.Invoke(entity);
        }

        public void GoBack()
        {
            if (!CanGoBack) return;

            _isNavigating = true;
            _currentIndex--;
            OnSelectionChanged?.Invoke(_history[_currentIndex]);
            _isNavigating = false;
        }

        public void GoForward()
        {
            if (!CanGoForward) return;

            _isNavigating = true;
            _currentIndex++;
            OnSelectionChanged?.Invoke(_history[_currentIndex]);
            _isNavigating = false;
        }
    }
}
