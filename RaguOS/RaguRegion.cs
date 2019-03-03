// Copyright (c) 2019 Robert Adams
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//     http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using OpenSim.Region.Framework.Scenes;

using org.herbal3d.transport;

namespace org.herbal3d.Ragu {
    public class RaguRegion {
        private static readonly String _logHeader = "[RaguRegion]";

        private readonly Scene _scene;
        private readonly RaguContext _context;
        private bool _running;  // 'true' if should be doing stuff
        private CancellationTokenSource _canceller;

        private BasilClient _client;
        private ISpaceServer _spaceServer;

        // Given a scene, do the LOD ("level of detail") conversion
        public RaguRegion(Scene pScene, RaguContext pContext) {
            _scene = pScene;
            _context = pContext;
            _running = false;
            _canceller = new CancellationTokenSource();
        }

        public void Start() {
            _running = true;
            // Wait for the region to have all its content before scanning
            _scene.EventManager.OnPrimsLoaded += Event_OnPrimsLoaded;
        }

        public void Stop() {
            if (_canceller != null) {
                _canceller.Cancel();
                _client = null;
                _spaceServer = null;
            }
        }

        // All prims have been loaded into the region.
        // Verify they have been converted into the LOD'ed versions.
        private void Event_OnPrimsLoaded(Scene pScene) {
            // Loading is going to take a while. Start up a Task.
            HerbalTransport transport = new HerbalTransport(HaveNewClient, ReturnSpaceServer, _context.parms, _context.log);
            transport.Start(_canceller);
        }

        // When a Basil server connects, we get a handle to call the server
        private void HaveNewClient(BasilClient pClient) {
            _client = pClient;
        }

        // When someone needs a SpaceServer to do SpaceServer operations, this is called
        private ISpaceServer ReturnSpaceServer(BasilConnection pConnection) {
            return null;
        }
    }
}
