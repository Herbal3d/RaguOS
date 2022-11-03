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
using org.herbal3d.cs.CommonUtil;

using OMV = OpenMetaverse;

namespace org.herbal3d.Ragu {

    // Processor of incoming messages after we're connected up
    class ProcessDynamicIncomingMessages : IncomingMessageProcessor {
        SpaceServerDynamic _ssContext;
        public ProcessDynamicIncomingMessages(SpaceServerDynamic pContext) : base(pContext) {
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

    public class SpaceServerDynamic : SpaceServerBase {
        private static readonly string _logHeader = "[SpaceServerDynamic]";

        public static readonly string SpaceServerType = "Dynamic";

        public SpaceServerDynamic(RaguContext pContext,
                                CancellationTokenSource pCanceller,
                                WaitingInfo pWaitingInfo,
                                BasilConnection pConnection,
                                BMessage pMsg) 
                        : base(pContext, pCanceller, pConnection) {
            LayerType = SpaceServerType;

            pConnection.SetOpProcessor(new ProcessDynamicIncomingMessages(this), ProcessConnectionStateChange);
        }

        public override void Start() {
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
                spaceServerType = SpaceServerDynamic.SpaceServerType,
                createSpaceServer = (pC, pW, pConn, pMsgX, pCan) => {
                    return new SpaceServerDynamic(pC, pCan, pW, pConn, pMsgX);
                }
            };
            pRContext.RememberWaitingForOpenSession(wInfo);

            // Create the MakeConnection and send it
            var pBlock = pRContext.Listener.ParamsForMakeConnection(pRContext.HostnameForExternalAccess, incomingAuth);
            _ = pConn.MakeConnection(pBlock);
        }


    }
}
