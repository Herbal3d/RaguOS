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

        public static readonly string SpaceServerType = "Environ";

        public SpaceServerEnviron(RaguContext pContext,
                                CancellationTokenSource pCanceller,
                                WaitingInfo pWaitingInfo,
                                BasilConnection pConnection,
                                BMessage pMsg) 
                        : base(pContext, pCanceller, pConnection) {
            LayerType = SpaceServerType;

            pConnection.SetOpProcessor(new ProcessEnvironIncomingMessages(this), ProcessConnectionStateChange);
        }

        public override void Start() {
            // Set up the UI
            Task.Run(async () => {
                await StartEnviron(_connection);
                await StartUI(_connection);
            });
        }

        // Send a MakeConnection for connecting to a SpaceServer of this type.
        public static void MakeConnectionToSpaceServer(BasilConnection pConn,
                                                    OMV.UUID pAgentUUID,
                                                    RaguContext pRContext) {

            // The authentication token that the client will send with the OpenSession
            OSAuthToken incomingAuth = new OSAuthToken();

            // Information that will be used to process the incoming OpenSession
            var wInfo = new WaitingInfo() {
                agentUUID = pAgentUUID,
                incomingAuth = incomingAuth,
                spaceServerType = SpaceServerEnviron.SpaceServerType,
                createSpaceServer = (pC, pW, pConn, pMsgX, pCan) => {
                    return new SpaceServerEnviron(pC, pCan, pW, pConn, pMsgX);
                }
            };
            pRContext.RememberWaitingForOpenSession(wInfo);

            // Create the MakeConnection and send it
            var pBlock = pRContext.Listener.ParamsForMakeConnection(pRContext.HostnameForExternalAccess, incomingAuth);
            _ = pConn.MakeConnection(pBlock);
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
