using System;
using System.Collections.Generic;
using Friflo.Engine.ECS;
using OpenSim.Region.ECS.Components;
using OpenSim.Region.ECS.World;
using OpenSim.Region.Framework.Scenes;
using OpenMetaverse;

namespace OpenSim.Region.ECS.Systems
{
    /// <summary>
    /// Bidirectional synchronisation adapter between the legacy
    /// OpenSim SceneGraph (SceneObjectGroup / SceneObjectPart)
    /// and the new ECS world (Friflo).
    ///
    /// This runs as a System subscribed to EventManager.OnFrame.
    /// It reads current ECS transform state and feeds it into
    /// SceneObjectPart.OffsetPosition/RotationOffset for the
    /// existing rendering pipeline.
    ///
    /// Phase 1: only syncs SceneGraph → ECS (read-only mirror).
    /// Phase 4+ takes over scene ownership.
    /// </summary>
    public class LegacySyncSystem
    {
        private readonly EcsWorld _ecs;
        private readonly Scene _scene;

        /// <summary>
        /// Maps SceneObjectPart.UUID → entity id in the ECS world.
        /// Used for fast lookup during sync.
        /// </summary>
        private readonly Dictionary<UUID, int> _partToEntity = new();
        private readonly Dictionary<int, UUID> _entityToPart = new();

        /// <summary>
        /// If true, ECS writes back transforms to SceneGraph.
        /// Enabled in Phase 4 when ECS + Bepu become authoritative.
        /// </summary>
        public bool WriteBackEnabled { get; set; }

        public LegacySyncSystem(EcsWorld ecs, Scene scene)
        {
            _ecs = ecs ?? throw new ArgumentNullException(nameof(ecs));
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            WriteBackEnabled = false;
        }

        /// <summary>
        /// Main sync method. Called once per heartbeat frame.
        /// </summary>
        public void OnFrame()
        {
            var frame = _ecs.Tick();

            // Phase 1: SceneGraph → ECS (mirror)
            // Iterate over all scene objects and update ECS entities.
            _scene.SceneGraph.ForEachSOG(sog =>
            {
                foreach (var part in sog.Parts)
                {
                    SyncPartToEcs(part);
                }
            });

            // Phase 4+: ECS → SceneGraph (write-back)
            if (WriteBackEnabled)
                SyncEcsToSceneGraph();
        }

        /// <summary>
        /// Push a single SceneObjectPart's transform into the ECS world.
        /// If the entity does not exist yet, it is created.
        /// </summary>
        private void SyncPartToEcs(SceneObjectPart part)
        {
            if (_partToEntity.TryGetValue(part.UUID, out int entityId))
            {
                // Update existing entity
                if (_ecs.Store.TryGetEntityById(entityId, out var entity))
                {
                    ref var transform = ref entity.GetComponent<EcsTransform>();
                    var worldPos = part.GetWorldPosition();
                    transform.Position = worldPos;
                    transform.Rotation = part.RotationOffset;

                    ref var primShape = ref entity.GetComponent<EcsPrimShape>();
                    primShape.Scale = part.Scale;
                }
            }
            else
            {
                // Create new entity
                var id = _ecs.CreatePrimEntity(
                    part.UUID, part.LocalId, part.Shape,
                    part.GetWorldPosition(), part.RotationOffset, part.Scale
                );
                _partToEntity[part.UUID] = id;
                _entityToPart[id] = part.UUID;
            }
        }

        /// <summary>
        /// Push ECS transform changes back into SceneGraph.
        /// Only called when WriteBackEnabled is true (Phase 4+).
        /// </summary>
        private void SyncEcsToSceneGraph()
        {
            foreach (var (entityId, uuid) in _entityToPart)
            {
                if (!_ecs.Store.TryGetEntityById(entityId, out var entity))
                {
                    _entityToPart.Remove(entityId);
                    _partToEntity.Remove(uuid);
                    continue;
                }

                var part = _scene.GetSceneObjectPart(uuid);
                if (part == null)
                    continue;

                ref var transform = ref entity.GetComponent<EcsTransform>();
                part.OffsetPosition = transform.Position;
                part.RotationOffset = transform.Rotation;
            }
        }

        /// <summary>
        /// Remove an entity mapping.
        /// </summary>
        public void RemovePart(UUID partUuid)
        {
            if (_partToEntity.TryGetValue(partUuid, out int entityId))
            {
                _ecs.RemoveEntity(entityId);
                _entityToPart.Remove(entityId);
                _partToEntity.Remove(partUuid);
            }
        }
    }
}
