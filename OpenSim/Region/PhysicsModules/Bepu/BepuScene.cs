using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.PhysicsModules.SharedBase;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuPhysics.Trees;
using BepuUtilities;
using BepuUtilities.Memory;
using log4net;
using Nini.Config;
using Mono.Addins;
using Quaternion = OpenMetaverse.Quaternion;
using Vector3 = OpenMetaverse.Vector3;

namespace OpenSim.Region.PhysicsModule.Bepu
{
    [Mono.Addins.Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "BepuPhysicsScene")]
    public sealed class BepuScene : PhysicsScene, INonSharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private const string LogHeader = "[BEPU SCENE]";

        private bool _initialized;
        private bool _enabled;
        private string _regionName;
        private IConfigSource _config;

        // Bepu core types
        private BufferPool _bufferPool;
        private Simulation _simulation;
        private ThreadDispatcher _threadDispatcher;

        // Actor tracking
        private readonly Dictionary<uint, BepuActor> _actors = new();
        private readonly Dictionary<BodyHandle, BepuActor> _bodyHandleToActor = new();
        private readonly object _actorsLock = new();

        // Deferred body actions (queued by BepuActor, applied before Simulate)
        private readonly ConcurrentQueue<BodyAction> _bodyActions = new();

        // Scheduled updates (queued by BepuActor property setters)
        private readonly ConcurrentQueue<Action> _scheduledUpdates = new();

        // Collision tracking
        private readonly Dictionary<uint, Dictionary<uint, ContactPoint>> _activeCollisions = new();
        private readonly List<BufferedContact> _contactBuffer = new();

        // PID / MoveTo tracking (Phase 5)
        private readonly Dictionary<uint, PidState> _pidStates = new();

        // Terrain
        private float[] _terrainHeightMap;
        private float _waterLevel;
        private StaticHandle _terrainStaticHandle;
        private TypedIndex _terrainShapeIndex;
        private bool _hasTerrain;

        // World constants
        private const float DefaultGravityZ = -9.81f;
        private const int MaxBodies = 4096;
        private const int MaxConstraints = 1024;
        private const int DefaultAllocatorPoolSize = 128;

        // Time dilation tracking
        private float _timeDilation = 1.0f;

        #region Constructor

        public BepuScene()
        {
        }

        /// <summary>
        /// Initialises the Bepu physics engine. Must be called before any other operation.
        /// </summary>
        public void Init()
        {
            if (_initialized) return;
            InitializePhysics(Vector3.Zero);
        }

        private void InitializePhysics(Vector3 regionExtent)
        {
            _bufferPool = new BufferPool(DefaultAllocatorPoolSize);

            var pose = new RigidPose(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity);

            var narrowPhase = new BepuNarrowPhaseCallbacks();
            narrowPhase.Contacts = _contactBuffer;
            narrowPhase.MaterialResolver = ResolveMaterialProperties;

            _simulation = Simulation.Create(
                _bufferPool,
                narrowPhase,
                new BepuPoseIntegratorCallbacks(new System.Numerics.Vector3(0, 0, DefaultGravityZ)),
                new SolveDescription(velocityIterationCount: 8, substepCount: 1)
            );

            _threadDispatcher = new ThreadDispatcher(Environment.ProcessorCount > 1 ? Environment.ProcessorCount - 1 : 1);

            _initialized = true;
            m_log.InfoFormat("{0} BepuPhysics initialized. Gravity={1}, Threads={2}",
                LogHeader, DefaultGravityZ, _threadDispatcher.ThreadCount);
        }

        #endregion

        #region INonSharedRegionModule

        /// <summary>
        /// Name used by the module loader. Must match the "physics" setting in OpenSim.ini
        /// under the [Startup] section (physics = BepuPhysics).
        /// </summary>
        public string Name
        {
            get { return "BepuPhysics"; }
        }

        /// <summary>
        /// If this module can be replaced by another module implementing the same interface,
        /// return that interface type. Null means this module is not replaceable.
        /// </summary>
        public Type ReplaceableInterface
        {
            get { return null; }
        }

        /// <summary>
        /// Called by the module loader after creating the region instance.
        /// Checks whether this physics engine is the selected one and caches
        /// the config source for later initialization.
        /// </summary>
        public void Initialise(IConfigSource source)
        {
            IConfig startupConfig = source.Configs["Startup"];
            if (startupConfig != null)
            {
                string physics = startupConfig.GetString("physics", string.Empty);
                if (physics == Name)
                {
                    _config = source;
                    _enabled = true;
                }
            }
        }

        /// <summary>
        /// Called when the module is being shut down.
        /// </summary>
        public void Close()
        {
            if (_initialized)
                Dispose();

            _enabled = false;
            _config = null;
        }

        /// <summary>
        /// Called when a region is added to this module instance.
        /// For non-shared modules this happens exactly once, after Initialise().
        /// Initializes the physics engine, registers the PhysicsScene interface,
        /// and sets up terrain and water from the scene.
        /// </summary>
        public void AddRegion(Scene scene)
        {
            if (!_enabled)
                return;

            _regionName = scene.RegionInfo.RegionName;
            PhysicsSceneName = Name + "/" + _regionName;

            // Register this PhysicsScene so other modules can find it
            scene.RegisterModuleInterface<PhysicsScene>(this);

            // Determine region extent
            var extent = new OpenMetaverse.Vector3(
                scene.RegionInfo.RegionSizeX,
                scene.RegionInfo.RegionSizeY,
                scene.RegionInfo.RegionSizeZ);

            // Initialize the Bepu physics engine
            InitializePhysics(extent);

            // Set up terrain and water through the base class helper
            float[] terrain = scene.Heightmap?.GetFloatsSerialised()
                ?? new float[scene.RegionInfo.RegionSizeX * scene.RegionInfo.RegionSizeY];
            float waterHeight = (float)(scene.RegionInfo.RegionSettings?.WaterHeight ?? 20.0);

            base.Initialise(scene.PhysicsRequestAsset, terrain, waterHeight);

            EngineName = Name;
            EngineType = Name;
        }

        /// <summary>
        /// Called when the region is removed from this module instance.
        /// </summary>
        public void RemoveRegion(Scene scene)
        {
            if (!_enabled)
                return;

            if (_initialized)
                Dispose();
        }

        /// <summary>
        /// Called after all modules have had AddRegion called for this scene.
        /// Enables physics on the scene and performs any final setup that
        /// requires other modules to be present.
        /// </summary>
        public void RegionLoaded(Scene scene)
        {
            if (!_enabled)
                return;

            scene.PhysicsEnabled = true;
        }

        #endregion

        #region PhysicsScene abstract overrides

        public override PhysicsActor AddAvatar(
            string avName, Vector3 position, Vector3 velocity, Vector3 size, bool isFlying)
        {
            if (!_initialized) return null;

            var actor = new BepuActor(this, 0, avName, position, velocity, size,
                                       Quaternion.Identity, 70f, true, true)
            {
                Flying = isFlying
            };

            lock (_actorsLock)
                _actors[0] = actor; // LocalID assigned by caller via overload

            return actor;
        }

        public override PhysicsActor AddAvatar(
            uint localID, string avName, Vector3 position, Vector3 velocity, Vector3 size, bool isFlying)
        {
            if (!_initialized) return null;

            var actor = new BepuActor(this, localID, avName, position, velocity, size,
                                       Quaternion.Identity, 70f, true, true)
            {
                Flying = isFlying
            };

            lock (_actorsLock)
                _actors[localID] = actor;

            return actor;
        }

        public override void RemoveAvatar(PhysicsActor actor)
        {
            if (!_initialized || actor == null) return;

            if (actor is BepuActor bepuActor)
            {
                lock (_actorsLock)
                    _actors.Remove(bepuActor.LocalID);

                if (bepuActor.HasBody)
                {
                    _bodyHandleToActor.Remove(bepuActor.BodyHandle);
                    _simulation.Bodies.Remove(bepuActor.BodyHandle);
                    bepuActor.RemoveBody();
                }
            }
        }

        public override void RemovePrim(PhysicsActor prim)
        {
            if (!_initialized || prim == null) return;

            if (prim is BepuActor bepuPrim)
            {
                lock (_actorsLock)
                    _actors.Remove(bepuPrim.LocalID);

                if (bepuPrim.HasBody)
                {
                    _bodyHandleToActor.Remove(bepuPrim.BodyHandle);
                    _simulation.Bodies.Remove(bepuPrim.BodyHandle);
                    bepuPrim.RemoveBody();
                }
            }
        }

        public override PhysicsActor AddPrimShape(
            string primName, PrimitiveBaseShape pbs, Vector3 position,
            Vector3 size, Quaternion rotation, bool isPhysical, uint localID)
        {
            if (!_initialized) return null;

            var actor = new BepuActor(this, localID, primName, position, Vector3.Zero,
                                       size, rotation, CalculateMass(size, isPhysical),
                                       isPhysical, false)
            {
                Shape = pbs
            };

            // Create the Bepu body for this prim
            var snPosition = BepuUtil.ToSN(position);
            var snRotation = BepuUtil.ToSN(rotation);

            BodyHandle handle;
            TypedIndex shapeIndex = default;
            try
            {
                shapeIndex = CreateBepuShape(pbs, size, actor.Mass, out var inertia);

                var bodyDescription = CreateBodyDescription(
                    ref snPosition, ref snRotation, shapeIndex,
                    inertia, isPhysical);

                handle = _simulation.Bodies.Add(bodyDescription);
                actor.SetBody(handle, bodyDescription.Collidable.Shape, actor.Mass);
            }
            catch (Exception)
            {
                if (shapeIndex.Exists)
                    _simulation.Shapes.Remove(shapeIndex);
                throw;
            }

            lock (_actorsLock)
            {
                _actors[localID] = actor;
                _bodyHandleToActor[handle] = actor;
            }

            return actor;
        }

        public override float Simulate(float timeStep)
        {
            if (!_initialized) return 5.0f;

            // Apply all queued body actions and scheduled updates
            ProcessScheduledUpdates();

            // Run Bepu simulation
            _simulation.Timestep(timeStep, _threadDispatcher);

            // Collect results and push to actors
            PushSimulationResults();

            // Process collisions
            ProcessCollisions();

            // Process per-actor control systems (Phase 5)
            ProcessPidControls();
            ProcessBuoyancy();

            // Return simulated frames (always 1 for Bepu)
            return 1.0f;
        }

        public override void SetTerrain(float[] heightMap)
        {
            _terrainHeightMap = heightMap;

            if (!_initialized) return;

            // Remove previous terrain static and shape
            if (_hasTerrain)
            {
                _simulation.Statics.Remove(_terrainStaticHandle);
                _simulation.Shapes.Remove(_terrainShapeIndex);
                _hasTerrain = false;
            }

            if (heightMap == null || heightMap.Length == 0)
                return;

            int sizeX = (int)Math.Sqrt(heightMap.Length);
            int sizeY = sizeX; // OpenSim regions are square

            // Build mesh triangles from heightmap
            // Standard grid triangulation: each cell becomes two triangles
            int triCount = (sizeX - 1) * (sizeY - 1) * 2;
            _bufferPool.TakeAtLeast(triCount, out Buffer<Triangle> triangles);

            int triIdx = 0;
            for (int y = 0; y < sizeY - 1; y++)
            {
                for (int x = 0; x < sizeX - 1; x++)
                {
                    float h00 = heightMap[y * sizeY + x];
                    float h10 = heightMap[y * sizeY + x + 1];
                    float h01 = heightMap[(y + 1) * sizeY + x];
                    float h11 = heightMap[(y + 1) * sizeY + x + 1];

                    triangles[triIdx++] = new Triangle
                    {
                        A = new System.Numerics.Vector3(x, y, h00),
                        B = new System.Numerics.Vector3(x + 1, y, h10),
                        C = new System.Numerics.Vector3(x, y + 1, h01)
                    };
                    triangles[triIdx++] = new Triangle
                    {
                        A = new System.Numerics.Vector3(x + 1, y, h10),
                        B = new System.Numerics.Vector3(x + 1, y + 1, h11),
                        C = new System.Numerics.Vector3(x, y + 1, h01)
                    };
                }
            }

            // Mesh constructor takes ownership of the triangle buffer.
            // Shapes.Add copies the Mesh struct (sharing the buffer reference).
            // Shapes.Remove later will call Mesh.Dispose to return the buffer.
            var mesh = new Mesh(triangles, System.Numerics.Vector3.One, _bufferPool);
            _terrainShapeIndex = _simulation.Shapes.Add(mesh);

            // Center the mesh terrain at half the region size.
            // Vertices use exact world coordinates (0..sizeX-1, 0..sizeY-1),
            // so the static body is positioned at the center.
            float centerX = (sizeX - 1) * 0.5f;
            float centerY = (sizeY - 1) * 0.5f;

            var staticPose = new RigidPose(
                new System.Numerics.Vector3(centerX, centerY, 0f),
                System.Numerics.Quaternion.Identity);

            _terrainStaticHandle = _simulation.Statics.Add(new StaticDescription(staticPose, _terrainShapeIndex));
            _hasTerrain = true;
        }

        public override void SetWaterLevel(float baseheight)
        {
            _waterLevel = baseheight;
        }

        public override void DeleteTerrain()
        {
            if (_hasTerrain && _initialized)
            {
                _simulation.Statics.Remove(_terrainStaticHandle);
                _simulation.Shapes.Remove(_terrainShapeIndex);
                _hasTerrain = false;
            }
            _terrainHeightMap = null;
        }

        /// <summary>
        /// Get the terrain height at the given world position.
        /// Bilinear interpolation over the heightmap grid.
        /// Returns 0 if no terrain is loaded.
        /// </summary>
        public float GetTerrainHeightAtXYZ(Vector3 pos)
        {
            if (_terrainHeightMap == null || _terrainHeightMap.Length == 0)
                return 0f;

            int maxX = (int)Math.Sqrt(_terrainHeightMap.Length);
            int maxY = maxX;

            int baseX = (int)pos.X;
            int baseY = (int)pos.Y;

            // Clamp to valid range
            if (baseX < 0 || baseX >= maxX - 1 || baseY < 0 || baseY >= maxY - 1)
                return 0f;

            float diffX = pos.X - baseX;
            float diffY = pos.Y - baseY;

            float h00 = _terrainHeightMap[baseY * maxY + baseX];
            float h10 = _terrainHeightMap[baseY * maxY + Math.Min(baseX + 1, maxX - 1)];
            float h01 = _terrainHeightMap[Math.Min(baseY + 1, maxY - 1) * maxY + baseX];
            float h11 = _terrainHeightMap[Math.Min(baseY + 1, maxY - 1) * maxY + Math.Min(baseX + 1, maxX - 1)];

            // Bilinear interpolation
            float xLerp = h00 + (h10 - h00) * diffX;
            float yLerp = h01 + (h11 - h01) * diffX;
            return xLerp + (yLerp - xLerp) * diffY;
        }

        public override void Dispose()
        {
            if (!_initialized) return;

            lock (_actorsLock)
            {
                foreach (var actor in _actors.Values)
                {
                    if (actor.HasBody)
                    {
                        _simulation.Bodies.Remove(actor.BodyHandle);
                        actor.RemoveBody();
                    }
                }
                _actors.Clear();
            }

            _simulation.Dispose();
            _bufferPool.Clear();
            _threadDispatcher.Dispose();
            _initialized = false;
        }

        public override Dictionary<uint, float> GetTopColliders()
        {
            var topColliders = new List<KeyValuePair<uint, float>>();
            lock (_actorsLock)
            {
                foreach (var kvp in _actors)
                {
                    float score = kvp.Value.CollisionScore;
                    if (score > 0f)
                        topColliders.Add(new KeyValuePair<uint, float>(kvp.Key, score));
                }
            }

            topColliders.Sort((a, b) => b.Value.CompareTo(a.Value));

            var result = new Dictionary<uint, float>();
            int count = Math.Min(topColliders.Count, 25);
            for (int i = 0; i < count; i++)
                result[topColliders[i].Key] = topColliders[i].Value;

            return result;
        }

        #endregion

        #region Bepu-specific API (called by BepuActor)

        internal void EnqueueBodyAction(BodyHandle handle, ref System.Numerics.Vector3 value, BodyActionType type)
        {
            _bodyActions.Enqueue(new BodyAction { Handle = handle, Value = value, Type = type });
        }

        internal void SchedulePositionUpdate(BepuActor actor, Vector3 position)
        {
            var snPos = BepuUtil.ToSN(position);
            _scheduledUpdates.Enqueue(() =>
            {
                if (actor.HasBody)
                {
                    var body = _simulation.Bodies.GetBodyReference(actor.BodyHandle);
                    var pose = body.Pose;
                    pose.Position = snPos;
                    body.Pose = pose;
                }
            });
        }

        internal void ScheduleOrientationUpdate(BepuActor actor, OpenMetaverse.Quaternion orientation)
        {
            var snRot = BepuUtil.ToSN(orientation);
            _scheduledUpdates.Enqueue(() =>
            {
                if (actor.HasBody)
                {
                    var body = _simulation.Bodies.GetBodyReference(actor.BodyHandle);
                    var pose = body.Pose;
                    pose.Orientation = snRot;
                    body.Pose = pose;
                }
            });
        }

        internal void ScheduleVelocityUpdate(BepuActor actor, Vector3 velocity)
        {
            var snVel = BepuUtil.ToSN(velocity);
            _scheduledUpdates.Enqueue(() =>
            {
                if (actor.HasBody)
                {
                    var body = _simulation.Bodies.GetBodyReference(actor.BodyHandle);
                    body.Velocity.Linear = snVel;
                }
            });
        }

        internal void ScheduleAngularVelocityUpdate(BepuActor actor, Vector3 angularVelocity)
        {
            var snAngVel = BepuUtil.ToSN(angularVelocity);
            _scheduledUpdates.Enqueue(() =>
            {
                if (actor.HasBody)
                {
                    var body = _simulation.Bodies.GetBodyReference(actor.BodyHandle);
                    body.Velocity.Angular = snAngVel;
                }
            });
        }

        internal void SchedulePhysicalFlagUpdate(BepuActor actor, bool isPhysical)
        {
            _scheduledUpdates.Enqueue(() =>
            {
                if (actor.HasBody)
                {
                    var body = _simulation.Bodies.GetBodyReference(actor.BodyHandle);
                    if (isPhysical)
                    {
                        // Restore cached inertia or compute from mass/size
                        if (actor.CachedInertia.InverseMass > 0 || actor.CachedInertia.InverseInertiaTensor.XX > 0)
                        {
                            body.SetLocalInertia(actor.CachedInertia);
                        }
                        else
                        {
                            // Cache not populated (e.g. first-time toggle to physical) — compute from mass/size
                            var snSize = BepuUtil.ToSN(actor.Size);
                            var box = new Box(snSize.X, snSize.Y, snSize.Z);
                            var inertia = box.ComputeInertia(actor.Mass);
                            body.SetLocalInertia(inertia);
                        }
                        body.Activity.SleepThreshold = 0.01f;
                    }
                    else
                    {
                        // Cache current inertia for later restoration
                        actor.CachedInertia = body.LocalInertia;
                        // Kinematic body for non-physical
                        body.SetLocalInertia(default);
                    }
                }
            });
        }

        internal void ScheduleKinematicUpdate(BepuActor actor, bool kinematic)
        {
            _scheduledUpdates.Enqueue(() =>
            {
                if (actor.HasBody)
                {
                    var body = _simulation.Bodies.GetBodyReference(actor.BodyHandle);
                    if (kinematic)
                    {
                        // Cache current inertia before zeroing
                        actor.CachedInertia = body.LocalInertia;
                        body.SetLocalInertia(default);
                    }
                    else
                    {
                        // Restore dynamic inertia from cache or compute from mass/size
                        if (actor.CachedInertia.InverseMass > 0 || actor.CachedInertia.InverseInertiaTensor.XX > 0)
                        {
                            body.SetLocalInertia(actor.CachedInertia);
                        }
                        else
                        {
                            // Compute inertia from mass and size
                            var snSize = BepuUtil.ToSN(actor.Size);
                            var box = new Box(snSize.X, snSize.Y, snSize.Z);
                            var inertia = box.ComputeInertia(actor.Mass);
                            body.SetLocalInertia(inertia);
                        }
                    }
                }
            });
        }

        internal void ScheduleShapeUpdate(BepuActor actor)
        {
            _scheduledUpdates.Enqueue(() =>
            {
                if (!actor.HasBody) return;

                var body = _simulation.Bodies.GetBodyReference(actor.BodyHandle);

                // Remove old shape and recreate based on the actor's current shape/size.
                _simulation.Shapes.Remove(body.Collidable.Shape);
                var newShapeIndex = CreateBepuShape(actor.PrimitiveBaseShape, actor.Size, actor.Mass, out var newInertia);
                body.Collidable.Shape = newShapeIndex;

                // Update inertia to match the new geometry.
                if (actor.IsPhysical)
                    body.SetLocalInertia(newInertia);
            });
        }

        internal void ScheduleLinksetUpdate(BepuActor actor)
        {
            // Linkset constraints will be implemented in Phase 4
        }

        /// <summary>
        /// Subscribe an actor to collision events at the given throttle interval.
        /// </summary>
        internal void SubscribeCollisionEvents(uint localID, int ms)
        {
            if (_actors.TryGetValue(localID, out var actor))
                actor.SubscribeCollisionEvents(ms);
        }

        #endregion

        #region Internal simulation plumbing

        /// <summary>
        /// Create a Bepu collider shape from an OpenSim PrimitiveBaseShape.
        /// Maps OpenSim profile/path combinations to native Bepu shapes.
        /// Returns the shape index and the inertia tensor for the given mass.
        /// </summary>
        private TypedIndex CreateBepuShape(PrimitiveBaseShape pbs, Vector3 size, float mass, out BodyInertia inertia)
        {
            // Default to a box if no shape information is provided.
            if (pbs == null)
            {
                var box = new Box(size.X, size.Y, size.Z);
                inertia = box.ComputeInertia(mass);
                return _simulation.Shapes.Add(box);
            }

            var profile = pbs.ProfileShape;
            var path = (Extrusion)pbs.PathCurve;

            // Sphere: half-circle profile revolved (e.g. CreateSphere)
            if (profile == ProfileShape.HalfCircle && path == Extrusion.Curve1)
            {
                float radius = Math.Max(size.X, Math.Max(size.Y, size.Z)) * 0.5f;
                var sphere = new Sphere(radius);
                inertia = sphere.ComputeInertia(mass);
                return _simulation.Shapes.Add(sphere);
            }

            // Cylinder: circle profile extruded straight (e.g. CreateCylinder)
            // NOTE: Bepu v2 Cylinder is Y-aligned by default, while OpenSim prims are
            // Z-aligned. For full correctness this should be wrapped in a Compound with a 90°
            // X rotation; that is left for a follow-up to avoid scope creep.
            if (profile == ProfileShape.Circle && path == Extrusion.Straight)
            {
                float radius = Math.Max(size.X, size.Y) * 0.5f;
                float length = size.Z;
                var cylinder = new Cylinder(radius, length);
                inertia = cylinder.ComputeInertia(mass);
                return _simulation.Shapes.Add(cylinder);
            }

            // Capsule: half-circle profile extruded straight
            // NOTE: Bepu v2 Capsule is Y-aligned by default, while OpenSim prims are
            // Z-aligned. For full correctness this should be wrapped in a Compound with a 90°
            // X rotation; that is left for a follow-up to avoid scope creep.
            if (profile == ProfileShape.HalfCircle && path == Extrusion.Straight)
            {
                float radius = Math.Max(size.X, size.Y) * 0.5f;
                float length = Math.Max(size.Z - 2f * radius, 0f);
                var capsule = new Capsule(radius, length);
                inertia = capsule.ComputeInertia(mass);
                return _simulation.Shapes.Add(capsule);
            }

            // Box: square profile extruded straight (default)
            {
                var box = new Box(size.X, size.Y, size.Z);
                inertia = box.ComputeInertia(mass);
                return _simulation.Shapes.Add(box);
            }
        }

        private void ProcessScheduledUpdates()
        {
            // Process body actions (forces)
            while (_bodyActions.TryDequeue(out var action))
            {
                if (!_simulation.Bodies.BodyExists(action.Handle)) continue;
                var bodyRef = _simulation.Bodies.GetBodyReference(action.Handle);

                switch (action.Type)
                {
                    case BodyActionType.AddForce:
                        bodyRef.ApplyImpulse(action.Value, System.Numerics.Vector3.Zero);
                        break;
                    case BodyActionType.AddAngularForce:
                        bodyRef.ApplyAngularImpulse(action.Value);
                        break;
                    case BodyActionType.SetMomentum:
                        // Velocity change instead of momentum
                        bodyRef.Velocity.Linear = action.Value;
                        break;
                }
            }

            // Process scheduled updates
            while (_scheduledUpdates.TryDequeue(out var update))
            {
                try { update(); }
                catch (Exception ex)
                {
                    m_log.WarnFormat("{0} Scheduled update failed: {1}", LogHeader, ex.Message);
                }
            }
        }

        private void PushSimulationResults()
        {
            lock (_actorsLock)
            {
                foreach (var kvp in _actors)
                {
                    var actor = kvp.Value;
                    if (!actor.HasBody) continue;

                    var body = _simulation.Bodies.GetBodyReference(actor.BodyHandle);

                    var pos = BepuUtil.ToOM(body.Pose.Position);
                    var rot = BepuUtil.ToOM(body.Pose.Orientation);
                    var vel = BepuUtil.ToOM(body.Velocity.Linear);
                    var angVel = BepuUtil.ToOM(body.Velocity.Angular);

                    actor.ApplySimulationResult(pos, rot, vel, angVel);
                }
            }
        }

        private void ProcessCollisions()
        {
            if (_contactBuffer.Count == 0)
                return;

            // Build this frame's collision map: actorLocalID → { colliderLocalID → ContactPoint }
            var thisFrameCollisions = new Dictionary<uint, Dictionary<uint, ContactPoint>>();

            // Track collision scores per actor this frame
            var collisionScores = new Dictionary<uint, float>();

            foreach (var contact in _contactBuffer)
            {
                if (!TryGetLocalID(contact.CollidableA, out var localA) ||
                    !TryGetLocalID(contact.CollidableB, out var localB))
                    continue;

                // Skip terrain collisions
                if (IsTerrainHandle(localA) || IsTerrainHandle(localB))
                    continue;

                // Compute world contact position — the Offset is from CollidableA's position
                var worldPos = GetContactWorldPosition(contact);
                var omPos = BepuUtil.ToOM(worldPos);
                var omNormal = BepuUtil.ToOM(contact.Normal);
                var cp = new ContactPoint(omPos, omNormal, contact.Depth);

                // Add contact from A's perspective (A collided with B)
                AddCollisionEntry(thisFrameCollisions, localA, localB, cp);

                // Add contact from B's perspective (B collided with A) with inverted normal
                var cpInverted = new ContactPoint(omPos, -omNormal, contact.Depth);
                AddCollisionEntry(thisFrameCollisions, localB, localA, cpInverted);

                // Accumulate collision scores
                float absDepth = Math.Abs(contact.Depth);
                collisionScores[localA] = collisionScores.GetValueOrDefault(localA) + absDepth;
                collisionScores[localB] = collisionScores.GetValueOrDefault(localB) + absDepth;
            }

            // Send collision events for actors with collisions this frame
            foreach (var kvp in thisFrameCollisions)
            {
                uint actorLocalID = kvp.Key;
                var colliderMap = kvp.Value;

                if (_actors.TryGetValue(actorLocalID, out var actor))
                {
                    // Set collision score for this actor
                    if (collisionScores.TryGetValue(actorLocalID, out float score))
                        actor.CollisionScore = score;

                    // Set collision state flags
                    actor.IsColliding = true;
                    actor.CollidingObj = true;

                    // Fire collision events (with throttle)
                    actor.FireCollisionEvents(colliderMap);
                }
            }

            // Detect collision END: actors that had collisions last frame but not this frame
            foreach (var kvp in _activeCollisions)
            {
                uint actorLocalID = kvp.Key;
                if (!thisFrameCollisions.ContainsKey(actorLocalID))
                {
                    // Send empty CollisionEventUpdate to signal collision end
                    if (_actors.TryGetValue(actorLocalID, out var actor))
                    {
                        actor.IsColliding = false;
                        actor.CollidingObj = false;
                        actor.FireCollisionEvents(new Dictionary<uint, ContactPoint>());
                    }
                }
            }

            // Update active collisions for next frame
            _activeCollisions.Clear();
            foreach (var kvp in thisFrameCollisions)
                _activeCollisions[kvp.Key] = kvp.Value;

            // Clear the contact buffer for the next step
            _contactBuffer.Clear();
        }

        /// <summary>
        /// Map a CollidableReference to an actor's local ID.
        /// </summary>
        private bool TryGetLocalID(CollidableReference collidable, out uint localID)
        {
            if (collidable.Mobility == CollidableMobility.Static)
            {
                // Static handle — could be terrain or other static
                localID = uint.MaxValue - 1; // Sentinel for terrain
                return true;
            }

            // For dynamic/kinematic bodies, look up by BodyHandle
            if (_bodyHandleToActor.TryGetValue(collidable.BodyHandle, out var actor))
            {
                localID = actor.LocalID;
                return true;
            }

            localID = 0;
            return false;
        }

        /// <summary>
        /// Check if a localID is one of the terrain sentinel values.
        /// </summary>
        private static bool IsTerrainHandle(uint localID)
        {
            return localID == uint.MaxValue - 1;
        }

        /// <summary>
        /// Compute the world-space contact position from a BufferedContact.
        /// The Offset is relative to CollidableA's position.
        /// </summary>
        private System.Numerics.Vector3 GetContactWorldPosition(in BufferedContact contact)
        {
            if (contact.CollidableA.Mobility == CollidableMobility.Static)
            {
                var staticRef = _simulation.Statics.GetStaticReference(
                    contact.CollidableA.StaticHandle);
                return staticRef.Pose.Position + contact.Offset;
            }
            else
            {
                var bodyRef = _simulation.Bodies.GetBodyReference(
                    contact.CollidableA.BodyHandle);
                return bodyRef.Pose.Position + contact.Offset;
            }
        }

        /// <summary>
        /// Add a collision entry to the per-frame collision map.
        /// </summary>
        private static void AddCollisionEntry(
            Dictionary<uint, Dictionary<uint, ContactPoint>> frameCollisions,
            uint actorLocalID, uint colliderLocalID, ContactPoint cp)
        {
            if (!frameCollisions.TryGetValue(actorLocalID, out var colliderMap))
            {
                colliderMap = new Dictionary<uint, ContactPoint>();
                frameCollisions[actorLocalID] = colliderMap;
            }

            // Keep the deepest penetration entry for each collider pair
            if (!colliderMap.TryGetValue(colliderLocalID, out var existing) ||
                Math.Abs(cp.PenetrationDepth) > Math.Abs(existing.PenetrationDepth))
            {
                colliderMap[colliderLocalID] = cp;
            }
        }

        private BodyDescription CreateBodyDescription(
            ref System.Numerics.Vector3 position,
            ref System.Numerics.Quaternion rotation,
            TypedIndex shapeIndex,
            BodyInertia inertia,
            bool isPhysical)
        {
            var pose = new RigidPose(position, rotation);
            var collidableDesc = new CollidableDescription(shapeIndex, 0.01f);

            if (isPhysical && inertia.InverseMass > 0)
            {
                var activityDesc = new BodyActivityDescription { SleepThreshold = 0.01f, MinimumTimestepCountUnderThreshold = 0 };
                return BodyDescription.CreateDynamic(pose, inertia, collidableDesc, activityDesc);
            }
            else
            {
                var activityDesc = new BodyActivityDescription { SleepThreshold = 0.01f, MinimumTimestepCountUnderThreshold = 0 };
                return BodyDescription.CreateKinematic(pose, collidableDesc, activityDesc);
            }
        }

        private static float CalculateMass(Vector3 size, bool isPhysical)
        {
            if (!isPhysical) return 0f;
            float volume = size.X * size.Y * size.Z;
            return Math.Max(volume * 10f, 1f); // density ~10
        }

        #endregion

        #region Phase 5: Per-actor material, PID, buoyancy, angular lock

        /// <summary>
        /// Resolve combined pair material properties from the two collidables.
        /// Called from the narrow phase worker threads via MaterialResolver delegate.
        /// </summary>
        private PairMaterialProperties ResolveMaterialProperties(CollidableReference a, CollidableReference b)
        {
            float frictionA = 0.5f, restitutionA = 0.1f;
            float frictionB = 0.5f, restitutionB = 0.1f;

            lock (_actorsLock)
            {
                ResolveCollidableMaterial(a, ref frictionA, ref restitutionA);
                ResolveCollidableMaterial(b, ref frictionB, ref restitutionB);
            }

            return new PairMaterialProperties
            {
                // Use the maximum friction and restitution of the pair
                FrictionCoefficient = Math.Max(frictionA, frictionB),
                MaximumRecoveryVelocity = 2f,
                SpringSettings = new SpringSettings(30f, 1f)
            };
        }

        private void ResolveCollidableMaterial(CollidableReference collidable, ref float friction, ref float restitution)
        {
            if (collidable.Mobility == CollidableMobility.Dynamic || collidable.Mobility == CollidableMobility.Kinematic)
            {
                if (_bodyHandleToActor.TryGetValue(collidable.BodyHandle, out var actor))
                    (friction, restitution) = actor.GetMaterialProperties();
            }
            // Statics keep the default values passed in
        }

        /// <summary>
        /// Register or unregister a PID (MoveTo) state for an actor.
        /// Called from BepuActor property setters.
        /// </summary>
        internal void SyncPidState(uint localId, PidState state)
        {
            lock (_actorsLock)
            {
                if (state.Active)
                    _pidStates[localId] = state;
                else
                    _pidStates.Remove(localId);
            }
        }

        /// <summary>
        /// Process all active PID (MoveTo) controllers.
        /// Drives physical bodies toward their PID targets using a simple spring.
        /// </summary>
        private void ProcessPidControls()
        {
            if (_pidStates.Count == 0)
                return;

            lock (_actorsLock)
            {
                foreach (var kvp in _pidStates)
                {
                    if (!_actors.TryGetValue(kvp.Key, out var actor) || !actor.HasBody)
                        continue;

                    var pid = kvp.Value;
                    if (!pid.Active) continue;

                    var body = _simulation.Bodies.GetBodyReference(actor.BodyHandle);
                    var currentPos = body.Pose.Position;
                    var targetPos = BepuUtil.ToSN(pid.Target);

                    // Simple proportional controller: error / tau = velocity
                    var error = targetPos - currentPos;
                    var force = error / Math.Max(pid.Tau, 0.001f);
                    body.Velocity.Linear = force;
                }
            }
        }

        /// <summary>
        /// Apply buoyancy forces to actors that are in water.
        /// </summary>
        private void ProcessBuoyancy()
        {
            float waterLevel = _waterLevel;
            if (waterLevel <= 0) return;

            lock (_actorsLock)
            {
                foreach (var kvp in _actors)
                {
                    var actor = kvp.Value;
                    if (!actor.HasBody || !actor.FloatOnWater || actor.Buoyancy <= 0)
                        continue;

                    var body = _simulation.Bodies.GetBodyReference(actor.BodyHandle);
                    float submergedDepth = waterLevel - body.Pose.Position.Z;
                    if (submergedDepth <= 0) continue;

                    // Simple buoyancy: upward force proportional to submerged depth
                    float buoyantForce = submergedDepth * actor.Buoyancy * 9.81f;
                    body.Velocity.Linear.Z += buoyantForce * 0.01f;
                }
            }
        }

        /// <summary>
        /// Zero angular velocity components based on the axis lock bitmask.
        /// Bitmask: 1=X, 2=Y, 4=Z. Applied each frame via scheduled update.
        /// </summary>
        internal void ScheduleAngularLockUpdate(BepuActor actor, int axislocks)
        {
            _scheduledUpdates.Enqueue(() =>
            {
                if (!actor.HasBody) return;
                var body = _simulation.Bodies.GetBodyReference(actor.BodyHandle);
                var angularVelocity = body.Velocity.Angular;
                if ((axislocks & 1) != 0) angularVelocity.X = 0;
                if ((axislocks & 2) != 0) angularVelocity.Y = 0;
                if ((axislocks & 4) != 0) angularVelocity.Z = 0;
                body.Velocity.Angular = angularVelocity;
            });
        }

        #endregion

        #region Raycasting

        public override bool SupportsRayCast() => true;

        public override void RaycastWorld(Vector3 position, Vector3 direction, float length, RaycastCallback retMethod)
        {
            if (!_initialized)
            {
                retMethod?.Invoke(false, Vector3.Zero, 0, 999999f, Vector3.Zero);
                return;
            }

            var rayOrigin = BepuUtil.ToSN(position);
            var rayDir = BepuUtil.ToSN(direction);
            if (rayDir.LengthSquared() <= 0)
            {
                retMethod?.Invoke(false, Vector3.Zero, 0, 999999f, Vector3.Zero);
                return;
            }
            rayDir = System.Numerics.Vector3.Normalize(rayDir);

            var hitHandler = new RayHitHandler();
            _simulation.RayCast(rayOrigin, rayDir, length, ref hitHandler, 0);

            if (retMethod != null)
            {
                if (hitHandler.Hit)
                {
                    var hitPoint = BepuUtil.ToOM(rayOrigin + rayDir * hitHandler.T);
                    var hitNormal = BepuUtil.ToOM(hitHandler.Normal);
                    retMethod(true, hitPoint, 0, hitHandler.T, hitNormal);
                }
                else
                {
                    retMethod(false, Vector3.Zero, 0, 999999f, Vector3.Zero);
                }
            }
        }

        public override void RaycastWorld(Vector3 position, Vector3 direction, float length, int count, RayCallback retMethod)
        {
            if (!_initialized)
            {
                retMethod?.Invoke(new List<ContactResult>());
                return;
            }

            var results = CollectRayHits(position, direction, length, count, RayFilterFlags.All);
            retMethod?.Invoke(results);
        }

        public override List<ContactResult> RaycastWorld(Vector3 position, Vector3 direction, float length, int count)
        {
            if (!_initialized)
                return new List<ContactResult>();

            return CollectRayHits(position, direction, length, count, RayFilterFlags.All);
        }

        public override object RaycastWorld(Vector3 position, Vector3 direction, float length, int count, RayFilterFlags filter)
        {
            if (!_initialized)
                return new List<ContactResult>();

            return CollectRayHits(position, direction, length, count, filter);
        }

        private List<ContactResult> CollectRayHits(Vector3 position, Vector3 direction, float length, int count, RayFilterFlags filter)
        {
            var rayOrigin = BepuUtil.ToSN(position);
            var rayDir = BepuUtil.ToSN(direction);
            if (rayDir.LengthSquared() <= 0)
                return new List<ContactResult>();
            rayDir = System.Numerics.Vector3.Normalize(rayDir);

            var hitHandler = new MultiHitHandler
            {
                Results = new List<ContactResult>(),
                MaxHits = count,
                Origin = rayOrigin,
                Direction = rayDir,
                Filter = filter
            };

            _simulation.RayCast(rayOrigin, rayDir, length, ref hitHandler, 0);
            return hitHandler.Results;
        }

        #endregion

        #region Probes

        public override List<ContactResult> SphereProbe(Vector3 position, float radius, int count, RayFilterFlags flags)
        {
            // Implement in Phase 4 with Bepu's sweep tests
            return new List<ContactResult>();
        }

        public override List<ContactResult> BoxProbe(Vector3 position, Vector3 size, Quaternion orientation, int count, RayFilterFlags flags)
        {
            return new List<ContactResult>();
        }

        #endregion

        #region Time and stats

        public override float TimeDilation
        {
            get => _timeDilation;
        }

        public override Dictionary<string, float> GetStats()
        {
            if (!_initialized)
                return new Dictionary<string, float>();

            int totalBodies = 0;
            for (int i = 0; i < _simulation.Bodies.Sets.Length; i++)
            {
                if (_simulation.Bodies.Sets[i].Allocated)
                    totalBodies += _simulation.Bodies.Sets[i].Count;
            }

            var stats = new Dictionary<string, float>
            {
                ["BepuBodyCount"] = totalBodies,
                ["BepuActiveBodyCount"] = _simulation.Bodies.ActiveSet.Count,
                ["BepuStaticCount"] = _simulation.Statics.Count,
                ["BepuConstraintCount"] = _simulation.Solver.CountConstraints(),
            };
            return stats;
        }

        #endregion
    }

    #region Phase 5 types

    /// <summary>
    /// Per-actor PID (MoveTo) state tracked by BepuScene.
    /// Updated from BepuActor property setters via SyncPidState.
    /// </summary>
    internal struct PidState
    {
        public Vector3 Target; // OM Vector3 (from PIDTarget setter)
        public float Tau;
        public bool Active;
        public float HoverHeight;
        public PIDHoverType HoverType;
        public float HoverTau;
    }

    #endregion

    #region Bepu callbacks

    /// <summary>
    /// A single collision contact captured from Bepu's narrow phase.
    /// </summary>
    internal struct BufferedContact
    {
        public CollidableReference CollidableA;
        public CollidableReference CollidableB;
        public System.Numerics.Vector3 Offset; // Offset from CollidableA's position to contact
        public System.Numerics.Vector3 Normal;
        public float Depth;
    }

    /// <summary>
    /// Narrow phase callbacks that collect contacts for the collision pipeline.
    /// Contacts are extracted from manifolds and buffered for batch processing
    /// after the simulation step.
    /// </summary>
    internal struct BepuNarrowPhaseCallbacks : INarrowPhaseCallbacks
    {
        /// <summary>
        /// Contact buffer populated during the narrow phase.
        /// Set before passing this struct to Simulation.Create.
        /// Shared by reference across struct copies since List is a reference type.
        /// </summary>
        public List<BufferedContact> Contacts;

        /// <summary>
        /// Optional delegate to resolve per-actor pair material properties (friction, restitution).
        /// Set by BepuScene during initialization.
        /// Called from worker threads during the narrow phase — must be thread-safe.
        /// </summary>
        public Func<CollidableReference, CollidableReference, PairMaterialProperties> MaterialResolver;

        public void Initialize(Simulation simulation) { }

        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
            => true;

        public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB)
            => true;

        public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial)
            where TManifold : unmanaged, IContactManifold<TManifold>
        {
            if (MaterialResolver != null)
                pairMaterial = MaterialResolver(pair.A, pair.B);
            else
            {
                pairMaterial = new PairMaterialProperties
                {
                    FrictionCoefficient = 0.5f,
                    MaximumRecoveryVelocity = 2f,
                    SpringSettings = new SpringSettings(30f, 1f)
                };
            }

            if (Contacts == null)
                return true;

            int count = manifold.Count;
            if (count <= 0)
                return true;

            // Runtime type dispatch to access concrete manifold's GetContact method.
            // Both ConvexContactManifold and NonconvexContactManifold share the same
            // contact access pattern. Handle the common convex case here.
            if (typeof(TManifold) == typeof(ConvexContactManifold))
            {
                ref var convex = ref Unsafe.As<TManifold, ConvexContactManifold>(ref manifold);
                ReadContactsFromConvex(pair, ref convex);
            }

            return true;
        }

        public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold)
        {
            if (Contacts == null)
                return true;

            ReadContactsFromConvex(pair, ref manifold);
            return true;
        }

        public void Dispose() { }

        private void ReadContactsFromConvex(CollidablePair pair, ref ConvexContactManifold manifold)
        {
            for (int i = 0; i < manifold.Count; i++)
            {
                manifold.GetContact(i, out var offset, out var normal, out var depth, out _);
                Contacts.Add(new BufferedContact
                {
                    CollidableA = pair.A,
                    CollidableB = pair.B,
                    Offset = offset,
                    Normal = normal,
                    Depth = depth
                });
            }
        }
    }

    internal struct BepuPoseIntegratorCallbacks : IPoseIntegratorCallbacks
    {
        private Vector3Wide _gravityWide;

        /// <summary>
        /// Current gravity vector. BepuScene can modify this between timesteps.
        /// Re-broadcast to SIMD wide on each PrepareForIntegration call.
        /// </summary>
        public System.Numerics.Vector3 Gravity;

        public AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;

        public bool AllowSubstepsForUnconstrainedBodies => true;

        public bool IntegrateVelocityForKinematics => true;

        public BepuPoseIntegratorCallbacks(System.Numerics.Vector3 gravity) : this()
        {
            Gravity = gravity;
            Vector3Wide.Broadcast(gravity, out _gravityWide);
        }

        public void Initialize(Simulation simulation) { }

        public void PrepareForIntegration(float dt)
        {
            // Re-broadcast gravity each frame so external changes take effect
            Vector3Wide.Broadcast(Gravity, out _gravityWide);
        }

        public void IntegrateVelocity(
            Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation, BodyInertiaWide localInertia,
            Vector<int> integrationMask, int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
        {
            velocity.Linear += _gravityWide * dt;
        }
    }

    internal struct RayHitHandler : IRayHitHandler
    {
        public bool Hit;
        public float T;
        public System.Numerics.Vector3 Normal;
        public CollidableReference HitCollidable;

        public bool AllowTest(CollidableReference collidable)
        {
            return true;
        }

        public bool AllowTest(CollidableReference collidable, int childIndex)
        {
            return true;
        }

        public void OnRayHit(in RayData ray, ref float maximumT, float t, in System.Numerics.Vector3 normal, CollidableReference collidable, int childIndex)
        {
            Hit = true;
            T = t;
            Normal = normal;
            HitCollidable = collidable;
            // Narrow search to only find closer hits (we want the closest one)
            maximumT = t;
        }
    }

    internal struct MultiHitHandler : IRayHitHandler
    {
        public List<ContactResult> Results;
        public int MaxHits;
        public System.Numerics.Vector3 Origin;
        public System.Numerics.Vector3 Direction;
        public RayFilterFlags Filter;

        public bool AllowTest(CollidableReference collidable)
        {
            switch (collidable.Mobility)
            {
                case CollidableMobility.Static:
                    return (Filter & RayFilterFlags.land) != 0;
                case CollidableMobility.Dynamic:
                    return (Filter & (RayFilterFlags.agent | RayFilterFlags.physical)) != 0;
                case CollidableMobility.Kinematic:
                    return (Filter & RayFilterFlags.nonphysical) != 0;
                default:
                    return true;
            }
        }

        public bool AllowTest(CollidableReference collidable, int childIndex)
        {
            return true;
        }

        public void OnRayHit(in RayData ray, ref float maximumT, float t, in System.Numerics.Vector3 normal, CollidableReference collidable, int childIndex)
        {
            if (Results.Count >= MaxHits) return;

            var hitPoint = Origin + Direction * t;
            Results.Add(new ContactResult
            {
                Pos = new Vector3(hitPoint.X, hitPoint.Y, hitPoint.Z),
                Normal = new Vector3(normal.X, normal.Y, normal.Z),
                Depth = 0,
                ConsumerID = 0
            });
        }
    }

    #endregion
}
