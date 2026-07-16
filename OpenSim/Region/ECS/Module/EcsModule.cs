using System;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Region.ECS.Systems;
using OpenSim.Region.ECS.World;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Region.ECS.Module
{
    /// <summary>
    /// INonSharedRegionModule that boots the ECS world and wires
    /// the LegacySyncSystem into the Scene heartbeat (OnFrame).
    ///
    /// Activated when opensim.ini has:
    ///   [Modules]
    ///   ECSModule = OpenSim.Region.ECS.Module.ECSModule
    /// </summary>
    public class ECSModule : INonSharedRegionModule
    {
        private EcsWorld _ecsWorld;
        private LegacySyncSystem _syncSystem;
        private Scene _scene;
        private bool _enabled = true;

        public string Name => "ECSModule";

        public Type ReplaceableInterface => null;

        public void Initialise(IConfigSource configSource)
        {
            var config = configSource.Configs["ECS"];
            if (config != null)
                _enabled = config.GetBoolean("Enabled", true);

            if (!_enabled)
                return;

            _ecsWorld = new EcsWorld();
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        public void AddRegion(Scene scene)
        {
            if (!_enabled)
                return;

            _scene = scene;
            _syncSystem = new LegacySyncSystem(_ecsWorld, scene);

            // Subscribe to the heartbeat frame event
            scene.EventManager.OnFrame += OnFrame;
        }

        public void RegionLoaded(Scene scene)
        {
            // Everything is wired up in AddRegion — no extra work needed.
        }

        public void RemoveRegion(Scene scene)
        {
            if (_syncSystem != null)
            {
                scene.EventManager.OnFrame -= OnFrame;
                _syncSystem = null;
            }
        }

        private void OnFrame()
        {
            _syncSystem?.OnFrame();
        }

        // Exposed for diagnostic / future use
        public EcsWorld World => _ecsWorld;
        public LegacySyncSystem SyncSystem => _syncSystem;
        public bool Enabled => _enabled;
    }
}
