using System;
using Raylib_cs;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;
using Fdp.Examples.Showcase.Components;

namespace Fdp.Examples.Showcase.Core
{
    public class ShowcaseInput
    {
        private readonly ShowcaseGame _game;
        private readonly KeyInputManager _keyManager;

        public ShowcaseInput(ShowcaseGame game)
        {
            _game = game;
            _keyManager = new KeyInputManager();
            RegisterKeys();
        }
        
        private void RegisterKeys()
        {
            // General controls
            _keyManager.RegisterKey(KeyboardKey.Escape, HandleExit);
            _keyManager.RegisterKey(KeyboardKey.R, HandleToggleRecording);
            _keyManager.RegisterKey(KeyboardKey.I, HandleToggleInspector);
            _keyManager.RegisterKey(KeyboardKey.Space, HandleTogglePause);
            _keyManager.RegisterKey(KeyboardKey.P, HandleTogglePlayback);
            
            // Playback seeking with auto-repeat
            _keyManager.RegisterKey(KeyboardKey.Left, () => SeekBackward(1));
            _keyManager.RegisterKey(KeyboardKey.Right, () => SeekForward(1));
            _keyManager.RegisterKey(KeyboardKey.Home, HandleRewind);
            _keyManager.RegisterKey(KeyboardKey.End, HandleSeekToEnd);
            
            // Spawn units with auto-repeat
            _keyManager.RegisterKey(KeyboardKey.One, () => _game.SpawnRandomUnit(UnitType.Tank));
            _keyManager.RegisterKey(KeyboardKey.Two, () => _game.SpawnRandomUnit(UnitType.Aircraft));
            _keyManager.RegisterKey(KeyboardKey.Three, () => _game.SpawnRandomUnit(UnitType.Infantry));
            
            // Delete unit with auto-repeat
            _keyManager.RegisterKey(KeyboardKey.Delete, () => _game.RemoveRandomUnit());
            _keyManager.RegisterKey(KeyboardKey.Backspace, () => _game.RemoveRandomUnit());
        }
        
        private void HandleExit()
        {
            _game.IsRunning = false;
        }
        
        private void HandleToggleRecording()
        {
            _game.IsRecording = !_game.IsRecording;
        }
        
        private void HandleToggleInspector()
        {
            _game.ShowInspector = !_game.ShowInspector;
        }
        
        private void HandleTogglePause()
        {
            _game.IsPaused = !_game.IsPaused;
        }
        
        private void HandleTogglePlayback()
        {
            if (!_game.IsReplaying)
            {
                _game.IsRecording = false;
                _game.DiskRecorder?.Dispose();
                _game.DiskRecorder = null;
                
                try
                {
                    _game.PlaybackController = new PlaybackController(_game.RecordingFilePath);
                    _game.PlaybackController.EventBus = _game.EventBus;
                    _game.IsReplaying = true;
                    
                    if (_game.PlaybackController.TotalFrames > 0)
                    {
                        _game.PlaybackController.Rewind(_game.Repo);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load recording: {ex.Message}");
                }
            }
            else
            {
                _game.IsReplaying = false;
                _game.PlaybackController?.Dispose();
                _game.PlaybackController = null;
            }
        }
        
        private void HandleRewind()
        {
            if (_game.IsReplaying && _game.PlaybackController != null)
            {
                _game.PlaybackController.Rewind(_game.Repo);
            }
        }
        
        private void HandleSeekToEnd()
        {
            if (_game.IsReplaying && _game.PlaybackController != null)
            {
                _game.PlaybackController.SeekToFrame(_game.Repo, _game.PlaybackController.TotalFrames - 1);
            }
        }
        
        private void SeekBackward(int steps)
        {
            if (!_game.IsReplaying || _game.PlaybackController == null)
                return;
            
            bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
            bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
            
            int stepSize = ctrl ? 100 : (shift ? 10 : 1);
            int targetFrame = Math.Max(0, _game.PlaybackController.CurrentFrame - (stepSize * steps));
            _game.PlaybackController.SeekToFrame(_game.Repo, targetFrame);
        }
        
        private void SeekForward(int steps)
        {
            if (_game.IsReplaying && _game.PlaybackController != null)
            {
                bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
                bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
                
                int stepSize = ctrl ? 100 : (shift ? 10 : 1);
                int targetFrame = Math.Min(_game.PlaybackController.TotalFrames - 1, _game.PlaybackController.CurrentFrame + (stepSize * steps));
                _game.PlaybackController.SeekToFrame(_game.Repo, targetFrame);
            }
            else if (_game.IsPaused && !_game.IsReplaying)
            {
                _game.SingleStep = true;
            }
        }

        public void HandleRaylibInput()
        {
            // Get delta time for key manager
            ref var time = ref _game.Repo.GetSingletonUnmanaged<GlobalTime>();
            float dt = time.DeltaTime;
            
            // Update key manager for auto-repeat
            _keyManager.Update(dt);
            
            // Process all key actions
            _keyManager.ProcessActions();
        }
    }
}
