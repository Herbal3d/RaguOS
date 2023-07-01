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
using System.Threading;

using org.herbal3d.transport;

namespace org.herbal3d.Ragu {

    public class SpaceServerWSListener: SpaceServerListener {
        private static readonly string _logHeader = "[SpaceServerWSListener]"; 

        BTransportWSParams xportParam;

        public SpaceServerWSListener(BTransportWSParams pParms, 
                    CancellationTokenSource pCanceller,
                    RaguContext pRContext,
                    RaguRegion pRegion) 
                        : base(pParms, pCanceller, pRContext, pRegion) {


            _ = new BTransportWSConnectionListener(
                param: TransportParams as BTransportWSParams,
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
                return String.Format(xportParam.externalURLTemplate, _RContext.HostnameForExternalAccess, xportParam.port);
            }
            protected set => throw new NotImplementedException();
        }

    }
}
