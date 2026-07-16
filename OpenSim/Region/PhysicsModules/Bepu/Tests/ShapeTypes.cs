/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System.Collections.Generic;
using NUnit.Framework;
using OpenSim.Framework;
using OpenSim.Region.PhysicsModules.SharedBase;
using OpenSim.Tests.Common;
using OpenMetaverse;

namespace OpenSim.Region.PhysicsModule.Bepu.Tests
{
    [TestFixture]
    public class ShapeTypesTests : OpenSimTestCase
    {
        private BepuScene _physicsScene;

        [OneTimeSetUp]
        public void Init()
        {
            _physicsScene = BepuTestsUtil.CreateBasicPhysicsEngine(null);
        }

        [Test]
        public void T001_CreateSphere()
        {
            PrimitiveBaseShape pbs = PrimitiveBaseShape.CreateSphere();
            PhysicsActor prim = _physicsScene.AddPrimShape("Sphere", pbs,
                new Vector3(128f, 128f, 30f),
                new Vector3(2f, 2f, 2f),
                Quaternion.Identity, false, 1001);

            Assert.That(prim, Is.Not.Null, "Sphere prim should be created");
            Assert.That(prim.Size, Is.EqualTo(new Vector3(2f, 2f, 2f)));
        }

        [Test]
        public void T002_CreateCylinder()
        {
            PrimitiveBaseShape pbs = PrimitiveBaseShape.CreateCylinder();
            PhysicsActor prim = _physicsScene.AddPrimShape("Cylinder", pbs,
                new Vector3(128f, 128f, 30f),
                new Vector3(2f, 2f, 4f),
                Quaternion.Identity, false, 1002);

            Assert.That(prim, Is.Not.Null, "Cylinder prim should be created");
            Assert.That(prim.Size, Is.EqualTo(new Vector3(2f, 2f, 4f)));
        }

        [Test]
        public void T003_CreateCapsule()
        {
            PrimitiveBaseShape pbs = PrimitiveBaseShape.CreateSphere();
            pbs.ProfileShape = ProfileShape.HalfCircle;
            pbs.PathCurve = (byte)Extrusion.Straight;

            PhysicsActor prim = _physicsScene.AddPrimShape("Capsule", pbs,
                new Vector3(128f, 128f, 30f),
                new Vector3(2f, 2f, 4f),
                Quaternion.Identity, false, 1003);

            Assert.That(prim, Is.Not.Null, "Capsule prim should be created");
            Assert.That(prim.Size, Is.EqualTo(new Vector3(2f, 2f, 4f)));
        }

        [Test]
        public void T004_SphereRaycastHitsWhenExpected()
        {
            PrimitiveBaseShape pbs = PrimitiveBaseShape.CreateSphere();
            Vector3 targetPos = new Vector3(100f, 100f, 50f);
            Vector3 targetSize = new Vector3(10f, 10f, 10f);

            _physicsScene.AddPrimShape("TargetSphere", pbs, targetPos, targetSize,
                Quaternion.Identity, false, 123);
            _physicsScene.Simulate(0.089f);

            // Ray that should pass through the sphere
            Vector3 fromPos = new Vector3(100f, 50f, 50f);
            Vector3 toPos = new Vector3(100f, 150f, 50f);
            List<ContactResult> results = _physicsScene.RaycastWorld(fromPos, toPos - fromPos,
                Vector3.Distance(fromPos, toPos), 1);

            Assert.That(results.Count, Is.GreaterThan(0), "Ray through sphere center should hit");
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (_physicsScene != null)
            {
                _physicsScene.Dispose();
                _physicsScene = null;
            }
        }
    }
}
