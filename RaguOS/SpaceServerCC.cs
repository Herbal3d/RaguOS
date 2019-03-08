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

using SpaceServer = org.herbal3d.basil.protocol.SpaceServer;
using HTransport = org.herbal3d.transport;

using Google.Protobuf;

using OMV = OpenMetaverse;

namespace org.herbal3d.Ragu {

    public class SpaceServerCC : HTransport.ISpaceServer {
        private readonly static string _logHeader = "[SpaceServerCC]";

        private readonly CancellationTokenSource _canceller;
        private readonly RaguContext _context;
        private HTransport.BasilClient _client;

        public SpaceServerCC(RaguContext pContext, CancellationTokenSource pCanceller) {
            _context = pContext;
            _canceller = pCanceller;

            HTransport.HerbalTransport transport =
                            new HTransport.HerbalTransport(this, _context.parms, _context.log);
            transport.Start(_canceller);
        }

        // Handle to the client.
        // This call says things are initialized and ready to go.
        public void SetClientConnection(HTransport.BasilClient pClient) {
            _context.log.DebugFormat("{0} Client handler set", _logHeader);
            _client = pClient;
        }

        public SpaceServer.OpenSessionResp OpenSession(SpaceServer.OpenSessionReq pReq) {
            _context.log.DebugFormat("{0} OpenSession.", _logHeader);
            if (pReq.Features != null) {
            }
            Dictionary<string, string> props = new Dictionary<string, string>() {
                { "SessionKey", _context.sessionKey },
                // For the moment, fake an asset access key
                { "AssetKey", _context.assetKey },
                { "AssetKeyExpiration", DateTime.UtcNow.AddHours(2).ToString("O") },
                { "AssetBase", RaguAssetService.Instance.AssetServiceURL }
            };


            var ret = new SpaceServer.OpenSessionResp() {
            };
            ret.Properties.Add(props);
            return ret;
        }

        public SpaceServer.CloseSessionResp CloseSession(SpaceServer.CloseSessionReq pReq) {
            throw new NotImplementedException();
        }

        public SpaceServer.CameraViewResp CameraView(SpaceServer.CameraViewReq pReq) {
            throw new NotImplementedException();
        }
    }
}
