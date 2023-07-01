// Copyright (c) 2023 Robert Adams
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
using System.Threading;
using System.Threading.Tasks;

using org.herbal3d.b.protocol;
using org.herbal3d.transport;
using org.herbal3d.OSAuth;
using org.herbal3d.cs.CommonUtil;

using OMV = OpenMetaverse;

using OpenSim.Framework;

namespace org.herbal3d.Ragu {

    public class SpaceServerOSWSListener: SpaceServerListener {
        private static readonly string _logHeader = "[SpaceServerWSListener]"; 

        BTransportOSWSParams xportParam;

        public SpaceServerOSWSListener(BTransportOSWSParams pParms, 
                    CancellationTokenSource pCanceller,
                    RaguContext pRContext,
                    RaguRegion pRegion) 
                        : base(pParms, pCanceller, pRContext, pRegion) {

            _RContext.log.Debug("{0} SpaceServerOSWSListener: create", _logHeader);
            xportParam = pParms;
            _ = new BTransportOSWSConnectionListener(
                param: pParms,
                logger: _RContext.log,
                connectionProcessor: (pTrans, pCan) => SpaceServerListener.AcceptConnection(pTrans, _RContext, this, pCan),
                cancellerSource: _canceller
            );
        }

        // Return the URL that can be used to connect to this listener
        public override string ExternalURL {
            get {
                // This is the URL the user must connect to.
                // The usual configuration is to have an nginx proxy in front of the web
                //     socket to handle the TLS certificate magic.
                // So the Fleck code below creates a "ws:" connection point but this advertizes
                //     an external "wss:" nginx URL.
                string template = "{0}://{1}:{2}{3}";
                return String.Format(template, 
                                xportParam.isSecure ? "wss" : "ws",
                                xportParam.host,
                                xportParam.port,
                                xportParam.URLaddition
                );
            }
            protected set => throw new NotImplementedException();
        }
    }
}

