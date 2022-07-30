// Copyright (c) 2012 Robert Adams
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

    // Processor of incoming messages after we're connected up
    class ProcessEnvironIncomingMessages : IncomingMessageProcessor {
        SpaceServerEnviron _ssContext;
        public ProcessEnvironIncomingMessages(SpaceServerEnviron pContext) : base(pContext) {
            _ssContext = pContext;
        }
        public override void Process(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            switch (pMsg.Op) {
                case (uint)BMessageOps.UpdatePropertiesReq:
                    // TODO:
                    break;
                default:
                    BMessage resp = BasilConnection.MakeResponse(pMsg);
                    resp.Exception = "Unsupported operation on SpaceServer" + _ssContext.LayerType;
                    pConnection.Send(resp);
                    break;
            }
        }
    }

    public class SpaceServerEnviron : SpaceServerBase {
        private static readonly string _logHeader = "[SpaceServerEnviron]";

        public static readonly string StaticLayerType = "Environ";

        // Function called to start up the service listener.
        // THis starts listening for network connections and creates instances of the SpaceServer
        //     for each of the incoming connections
        public static SpaceServerListener SpaceServerEnvironService(RaguContext pRContext, CancellationTokenSource pCanceller) {
            return new SpaceServerListener(
                transportParams: new BTransportParams[] {
                    new BTransportWSParams() {
                        preferred       = true,
                        isSecure        = pRContext.parms.GetConnectionParam<bool>(pRContext, SpaceServerEnviron.StaticLayerType, "WSIsSecure"),
                        port            = pRContext.parms.GetConnectionParam<int>(pRContext, SpaceServerEnviron.StaticLayerType, "WSPort"),
                        certificate     = pRContext.parms.GetConnectionParam<string>(pRContext, SpaceServerEnviron.StaticLayerType, "WSCertificate"),
                        disableNaglesAlgorithm = pRContext.parms.GetConnectionParam<bool>(pRContext, SpaceServerEnviron.StaticLayerType, "DisableNaglesAlgorithm")
                    }
                },
                layer:                  SpaceServerActors.StaticLayerType,
                canceller:              pCanceller,
                logger:                 pRContext.log,
                // This method is called when the listener receives a connection but before any
                //     messsages have been exchanged.
                processor:              (pTransport, pCancellerP) => {
                                            return new SpaceServerEnviron(pRContext, pCancellerP, pTransport);
                                        }
            );
        }

        public SpaceServerEnviron(RaguContext pContext, CancellationTokenSource pCanceller, BTransport pTransport) 
                        : base(pContext, pCanceller, pTransport) {
            LayerType = StaticLayerType;

            // The protocol for the initial OpenSession is always JSON
            _protocol = new BProtocolJSON(null, _transport, RContext.log);

            // Expect BMessages and set up messsage processor to handle initial OpenSession
            _connection = new BasilConnection(_protocol, RContext.log);
            _connection.SetOpProcessor(new ProcessMessagesOpenConnection(this), ProcessConnectionStateChange);
            _connection.Start();
        }

        protected override void OpenSessionProcessing(BasilConnection pConnection, OSAuthToken pServiceAuth, WaitingInfo pWaitingInfo) {
            // We also have a full command processor
            pConnection.SetOpProcessor(new ProcessEnvironIncomingMessages(this), ProcessConnectionStateChange);

            // Set up the UI
            Task.Run(async () => {
                await StartEnviron(pConnection);
                await StartUI(pConnection);
            });
            return;
        }
        private async Task StartEnviron(BasilConnection pConn) {
        }
        private async Task StartUI(BasilConnection pConn) {
            // Create the first top menu
            AbilityList abilProps = new AbilityList();
            abilProps.Add(new AbDialog() {
                DialogName = "topMenu",
                DialogUrl = "./Dialogs/topMenu.html",
                DialogPlacement = "menu"
            });
            // They get to see statistics
            await pConn.CreateItem(abilProps);
            abilProps = new AbilityList();
            abilProps.Add(new AbDialog() {
                DialogUrl = "./Dialogs/status.html",
                DialogName = "Status",
                DialogPlacement = "bottom right"
            });
            await pConn.CreateItem(abilProps);
        }


    }
}