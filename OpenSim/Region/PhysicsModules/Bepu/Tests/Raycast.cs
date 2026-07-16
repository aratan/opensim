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
    public class BepuRaycastTests : OpenSimTestCase
    {
        private BepuScene _physicsScene;
        private const uint _targetLocalID = 123;

        // A 10x10x10 sphere at <100, 100, 50>
        private static readonly Vector3 TargetPos = new Vector3(100f, 100f, 50f);
        private static readonly Vector3 TargetSize = new Vector3(10f, 10f, 10f);

        [OneTimeSetUp]
        public void Init()
        {
            _physicsScene = BepuTestsUtil.CreateBasicPhysicsEngine(null);

            PrimitiveBaseShape pbs = PrimitiveBaseShape.CreateSphere();
            pbs.Scale = TargetSize;

            _physicsScene.AddPrimShape("TargetSphere", pbs, TargetPos, TargetSize,
                                       Quaternion.Identity, false, _targetLocalID);
            // Ensure the static body is registered in the broad phase
            _physicsScene.Simulate(0.089f);
        }

        [TestCase(100f, 50f, 50f, 100f, 150f, 50f, true, "Pass through sphere from front")]
        [TestCase(50f, 100f, 50f, 150f, 100f, 50f, true, "Pass through sphere from side")]
        [TestCase(50f, 50f, 50f, 150f, 150f, 50f, true, "Pass through sphere diagonally")]
        [TestCase(100f, 100f, 100f, 100f, 100f, 20f, true, "Pass through sphere from above")]
        [TestCase(20f, 20f, 50f, 80f, 80f, 50f, false, "Not reach sphere")]
        [TestCase(50f, 50f, 65f, 150f, 150f, 65f, false, "Passed over sphere")]
        public void RaycastAroundObject(float fromX, float fromY, float fromZ,
                                        float toX, float toY, float toZ,
                                        bool expected, string msg)
        {
            Vector3 fromPos = new Vector3(fromX, fromY, fromZ);
            Vector3 toPos = new Vector3(toX, toY, toZ);
            Vector3 direction = toPos - fromPos;
            float len = Vector3.Distance(fromPos, toPos);

            List<ContactResult> results = _physicsScene.RaycastWorld(fromPos, direction, len, 1);

            if (expected)
            {
                Assert.That(results.Count, Is.GreaterThan(0), msg + ": Did not return a hit but expected to.");
                // Verify hit position is near the target sphere (within radius + margin)
                Vector3 hitPos = results[0].Pos;
                float distToTarget = Vector3.Distance(hitPos, TargetPos);
                Assert.That(distToTarget, Is.LessThan(15f),
                    msg + ": Hit position too far from target sphere center");
            }
            else
            {
                Assert.That(results.Count, Is.EqualTo(0), msg + ": Returned a hit but expected none");
            }
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
