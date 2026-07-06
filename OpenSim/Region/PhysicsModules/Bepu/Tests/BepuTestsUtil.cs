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

using System;
using System.IO;
using System.Collections.Generic;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Region.PhysicsModules.SharedBase;
using OpenMetaverse;

namespace OpenSim.Region.PhysicsModule.Bepu.Tests
{
    public static class BepuTestsUtil
    {
        /// <summary>
        /// Create a basic Bepu physics engine ready for testing.
        /// </summary>
        /// <param name="paramOverrides">Optional config overrides for the BepuPhysics section.</param>
        /// <returns>An initialized BepuScene.</returns>
        public static BepuScene CreateBasicPhysicsEngine(Dictionary<string, string> paramOverrides)
        {
            IConfigSource openSimINI = new IniConfigSource();
            IConfig startupConfig = openSimINI.AddConfig("Startup");
            startupConfig.Set("physics", "BepuPhysics");
            startupConfig.Set("meshing", "Meshmerizer");
            startupConfig.Set("cacheSculptMaps", "false");

            IConfig bepuConfig = openSimINI.AddConfig("BepuPhysics");
            bepuConfig.Set("MeshSculptedPrim", "false");
            bepuConfig.Set("ForceSimplePrimMeshing", "true");
            if (paramOverrides != null)
            {
                foreach (KeyValuePair<string, string> kvp in paramOverrides)
                    bepuConfig.Set(kvp.Key, kvp.Value);
            }

            if (Directory.Exists("physlogs"))
            {
                bepuConfig.Set("PhysicsLoggingDir", "./physlogs");
                bepuConfig.Set("PhysicsLoggingEnabled", "True");
                bepuConfig.Set("PhysicsLoggingDoFlush", "True");
            }

            BepuScene pScene = new BepuScene();
            pScene.Init();

            return pScene;
        }
    }
}
