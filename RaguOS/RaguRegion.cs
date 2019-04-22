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

        private readonly RaguContext _context;
        private readonly Scene _scene;
        private readonly CancellationTokenSource _canceller;

        private ISpaceServer _spaceServerCC;
        private RaguAssetService _assetService;

        // Given a scene, do the LOD ("level of detail") conversion
        public RaguRegion(Scene pScene, RaguContext pContext) {
            _scene = pScene;
            _context = pContext;
            _canceller = new CancellationTokenSource();
        }

        public void Start() {
            // Wait for the region to have all its content before scanning
            _scene.EventManager.OnPrimsLoaded += Event_OnPrimsLoaded;

            _assetService = new RaguAssetService(_context);

        }

        public void Stop() {
            if (_assetService != null) {
                _assetService.Stop();
                _assetService = null;
            }
            if (_canceller != null) {
                _canceller.Cancel();
            }
        }

        // All prims have been loaded into the region.
        // Start the 'command and control' SpaceServer.
        private void Event_OnPrimsLoaded(Scene pScene) {
            _context.log.DebugFormat("{0} Prims loaded. Starting command-and-control SpaceServer", _logHeader);
            try {
                _context.layerStatic = new SpaceServerStatic(_context, _canceller);
                _context.layerCC = new SpaceServerCC(_context, _canceller);
            }
            catch (Exception e) {
                _context.log.ErrorFormat("{0} Failed creation of SpaceServerCC: {1}", _logHeader, e);
            }
        }
    }
}
