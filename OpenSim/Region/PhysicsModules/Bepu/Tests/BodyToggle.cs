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

using NUnit.Framework;
using OpenSim.Framework;
using OpenSim.Region.PhysicsModules.SharedBase;
using OpenSim.Tests.Common;
using OpenMetaverse;

namespace OpenSim.Region.PhysicsModule.Bepu.Tests
{
    [TestFixture]
    public class BodyToggleTests : OpenSimTestCase
    {
        private BepuScene _physicsScene;
        private const uint _localID = 42;
        private PhysicsActor _prim;

        [OneTimeSetUp]
        public void Init()
        {
            _physicsScene = BepuTestsUtil.CreateBasicPhysicsEngine(null);

            PrimitiveBaseShape pbs = PrimitiveBaseShape.CreateBox();
            Vector3 pos = new Vector3(128f, 128f, 30f);
            Vector3 size = new Vector3(2f, 2f, 2f);

            _prim = _physicsScene.AddPrimShape("TestBox", pbs, pos, size,
                                               Quaternion.Identity, false, _localID);
        }

        [Test]
        public void T001_InitialState()
        {
            Assert.That(_prim, Is.Not.Null);
            Assert.That(_prim.IsPhysical, Is.False, "Prim should start non-physical");
            // IsSelected not available on PhysicsActor base class
        }

        [Test]
        public void T002_SimulateStep()
        {
            float result = _physicsScene.Simulate(0.089f);
            Assert.That(result, Is.EqualTo(1.0f), "Simulate should complete without error");
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (_physicsScene != null)
            {
                if (_prim != null)
                    _physicsScene.RemovePrim(_prim);

                _physicsScene.Dispose();
                _physicsScene = null;
            }
        }
    }
}
