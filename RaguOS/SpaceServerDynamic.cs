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

using org.herbal3d.transport;
using org.herbal3d.b.protocol;
using org.herbal3d.OSAuth;

using OMV = OpenMetaverse;

namespace org.herbal3d.Ragu {

    class ProcessDynamicIncomingMessages : IncomingMessageProcessor {
        SpaceServerDynamic _ssContext;
        public ProcessDynamicIncomingMessages(SpaceServerDynamic pContext) : base(pContext) {
            _ssContext = pContext;
        }
        public override void Process(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            if (pMsg.Op == (uint)BMessageOps.OpenSessionReq) {
                _ssContext.ProcessOpenSessionReq(pMsg, pConnection, pProtocol);
            }
            else {
                BMessage resp = BasilConnection.MakeResponse(pMsg);
                resp.Exception = "Session is not open. Static";
                pProtocol.Send(resp);
            }
        }
    }

    public class SpaceServerDynamic : SpaceServerBase {
        private static readonly string _logHeader = "[SpaceServerDynamic]";

        public static readonly string StaticLayerType = "Dynamic";

        // Function called to start up the service listener.
        // THis starts listening for network connections and creates instances of the SpaceServer
        //     for each of the incoming connections
        public static SpaceServerListener SpaceServerDynamicService(RaguContext pRContext, CancellationTokenSource pCanceller) {
            return new SpaceServerListener(
                transportParams: new BTransportParams[] {
                    new BTransportWSParams() {
                        preferred       = true,
                        isSecure        = pRContext.parms.SpaceServerDynamic_IsSecure,
                        port            = pRContext.parms.SpaceServerDynamic_WSConnectionPort,
                        certificate     = pRContext.parms.SpaceServerDynamic_WSCertificate,
                        disableNaglesAlgorithm = pRContext.parms.SpaceServerDynamic_DisableNaglesAlgorithm
                    }
                },
                layer:                  SpaceServerActors.StaticLayerType,
                canceller:              pCanceller,
                logger:                 pRContext.log,
                // This method is called when the listener receives a connection but before any
                //     messsages have been exchanged.
                processor:              (pTransport, pCancellerP) => {
                                            return new SpaceServerDynamic(pRContext, pCancellerP, pTransport);
                                        }
            );
        }

        private RaguContext _rContext;

        public SpaceServerDynamic(RaguContext pContext, CancellationTokenSource pCanceller, BTransport pTransport) 
                        : base(pContext, pCanceller, pTransport) {
            LayerType = StaticLayerType;

            // The protocol for the initial OpenSession is always JSON
            _protocol = new BProtocolJSON(null, _transport, _rContext.log);

            // Expect BMessages and set up messsage processor to handle initial OpenSession
            _connection = new BasilConnection(_protocol, RContext.log);
            _connection.SetOpProcessor(new ProcessMessagesOpenConnection(this));
        }

        protected override void OpenSessionProcessing(BasilConnection pConnection, OSAuthToken pServiceAuth) {
            // We also have a full command processor
            pConnection.SetOpProcessor(new ProcessDynamicIncomingMessages(this));
            return;
        }


    }
}
