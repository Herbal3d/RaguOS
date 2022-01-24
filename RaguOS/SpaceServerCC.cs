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

using org.herbal3d.OSAuth;
using org.herbal3d.transport;
using org.herbal3d.b.protocol;
using org.herbal3d.cs.CommonUtil;

using OMV = OpenMetaverse;

using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;

namespace org.herbal3d.Ragu {

    

    // Processor of incoming messages when we're waiting for the OpenSession.
    class ProcessCCIncomingMessages : IncomingMessageProcessor {
        SpaceServerCC _ssContext;
        public ProcessCCIncomingMessages(SpaceServerCC pContext) : base(pContext) {
            _ssContext = pContext;
        }
        public override void Process(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            switch (pMsg.Op) {
                case (uint)BMessageOps.OpenSessionReq:
                    _ssContext.ProcessOpenSessionReq(pMsg, pConnection, pProtocol);
                    break;
                case (uint)BMessageOps.MakeConnectionResp:
                    // We will get responses from our MakeConnections
                    break;
                default:
                    BMessage resp = BasilConnection.MakeResponse(pMsg);
                    resp.Exception = "Session is not open. AA";
                    pProtocol.Send(resp);
                    break;
            }
        }
    }

    public class SpaceServerCC : SpaceServerBase {
        private static readonly string _logHeader = "[SpaceServerCC]";

        public static readonly string StaticLayerType = "CC";

        // Function called to start up the service listener.
        // THis starts listening for network connections and creates instances of the SpaceServer
        //     for each of the incoming connections
        public static SpaceServerListener SpaceServerCCService(RaguContext pRContext, CancellationTokenSource pCanceller) {
            return new SpaceServerListener(
                transportParams: new BTransportParams[] {
                    new BTransportWSParams() {
                        isSecure        = pRContext.parms.SpaceServerCC_IsSecure,
                        port            = pRContext.parms.SpaceServerCC_WSConnectionPort,
                        certificate     = pRContext.parms.SpaceServerCC_WSCertificate,
                        disableNaglesAlgorithm = pRContext.parms.SpaceServerCC_DisableNaglesAlgorithm
                    }
                },
                layer:                  SpaceServerCC.StaticLayerType,
                canceller:              pCanceller,
                logger:                 pRContext.log,
                // This method is called when the listener receives a connection but before any
                //     messsages have been exchanged.
                processor:              (pTransport, pCancellerP) => {
                                            return new SpaceServerCC(pRContext, pCancellerP, pTransport);
                                        }
            );
        }

        // Creation of an instance for a specific client.
        // This instance is created when someone connects to the  transport so we're passed the
        //     initial transport. This code needs to set up the transport stack to receive
        //    the initial OpenSession message. If that is successful, we'll set up the
        //    whole message processing stack and thereafter be the instance for the client.
        // Note: this canceller is for the individual session.
        public SpaceServerCC(RaguContext pContext, CancellationTokenSource pCanceller, BTransport pTransport) 
                        : base(pContext, pCanceller, pTransport) {
            LayerType = StaticLayerType;

            // The protocol for the initial OpenSession is always JSON
            _protocol = new BProtocolJSON(null, _transport, RContext.log);

            // Expect BMessages and set up messsage processor to handle initial OpenSession
            _connection = new BasilConnection(_protocol, RContext.log);
            _connection.SetOpProcessor(new ProcessMessagesOpenConnection(this));
        }

        // Received an OpenSession from a Basil client.
        // Connect it to the other layers.
        protected override void OpenSessionProcessing(BasilConnection pConnection, OSAuthToken pServiceAuth) {
            Task.Run(async () => {
                try {
                    // Create the Circuit and ScenePresence for the user
                    CreateOpenSimPresence(pServiceAuth);

                    RContext.log.Debug("{0} HandleBasilConnection", _logHeader);

                    // Invite the client to connect to the interesting layers.
                    // Static
                    WaitingInfo waiting = new WaitingInfo();
                    RContext.waitingForMakeConnection.Add(waiting.incomingAuth.Token, waiting);
                    ParamBlock pp = RContext.SpaceServerStaticService.ParamsForMakeConnection(
                                        RContext.HostnameForExternalAccess, waiting.incomingAuth);
                    await pConnection.MakeConnection(pp);
                    // Actors
                    waiting = new WaitingInfo();
                    RContext.waitingForMakeConnection.Add(waiting.incomingAuth.Token, waiting);
                    pp = RContext.SpaceServerActorsService.ParamsForMakeConnection(
                                        RContext.HostnameForExternalAccess, waiting.incomingAuth);
                    await pConnection.MakeConnection(pp);
                    // Dynamic
                    waiting = new WaitingInfo();
                    RContext.waitingForMakeConnection.Add(waiting.incomingAuth.Token, waiting);
                    pp = RContext.SpaceServerDynamicService.ParamsForMakeConnection(
                                        RContext.HostnameForExternalAccess, waiting.incomingAuth);
                    await pConnection.MakeConnection(pp);

                }
                catch (Exception e) {
                    RContext.log.Error("{0} HandleBasilConnection. Exception connecting Basil to layers: {1}", _logHeader, e);
                }
            });
        }

        // The OpenSession request to the command/control SpaceServer has an authoriztion for the hosting
        // region. The OSAuthToken should contain a bunch of information from the user login.
        protected override bool ValidateLoginAuth(OSAuthToken pUserAuth) {
            bool isAuthorized = false;
            if (RContext.parms.ShouldEnforceUserAuth) {
                try {
                    string agentId = pUserAuth.GetProperty("AgentId");
                    OMV.UUID agentUUID = OMV.UUID.Parse(agentId);
                    OMV.UUID sessionID = OMV.UUID.Parse(pUserAuth.GetProperty("SessionID"));
                    OMV.UUID secureSessionID = OMV.UUID.Parse(pUserAuth.GetProperty("SSID"));
                    uint circuitCode = UInt32.Parse(pUserAuth.GetProperty("CC"));
                    // RContext.log.DebugFormat("{0} ValidateLoginAuth: agentUUID={1}, sessionID={2}, secureSessionID={3}, circuitCode={4}",
                    //             _logHeader, agentUUID, sessionID, secureSessionID, circuitCode);

                    AgentCircuitData acd = RContext.scene.AuthenticateHandler.GetAgentCircuitData(agentUUID);
                    if (acd != null) {
                        if (acd.circuitcode == circuitCode) {
                            if (acd.SessionID == sessionID) {
                                if (acd.SecureSessionID == secureSessionID) {
                                    isAuthorized = true;
                                }
                                else {
                                    RContext.log.Debug("{0} ValidateUserAuth: Failed secureSessionID test. AgentId={1}",
                                                _logHeader, agentId);
                                }
                            }
                            else {
                                RContext.log.Debug("{0} ValidateUserAuth: Failed sessionId test. AgentId={1}",
                                            _logHeader, agentId);
                            }
                        }
                        else {
                            RContext.log.Debug("{0} ValidateUserAuth: Failed circuitCode test. AgentId={1}",
                                        _logHeader, agentId);
                        }
                    }
                }
                catch (Exception e) {
                    RContext.log.Error("{0} ValidateUserAuth: exception authorizing: {1}",
                                _logHeader, e);
                    isAuthorized = false;
                }
            }
            else {
                isAuthorized = true;
            };
            return isAuthorized;
        }

        // Create  the OpenSimulator ScenePResence and the associated structures
        //    that fake out the simulator so it thinks it is connected to a viewer.
        // Code cribbed from NPC (INPCModule.cs), DSG (http://github.com/intelvwi/DSG),
        //    and LoginService (LLLoginService.cs).
        // The user OSAuthToken contains extra login information needed for creating
        //    the circuit, etc.
        private bool CreateOpenSimPresence(OSAuthToken pUserAuth) {
            bool ret = false;

            // Get identifying info from the auth
            string agentId = pUserAuth.GetProperty("AgentId");
            OMV.UUID agentUUID = OMV.UUID.Parse(agentId);
            OMV.UUID sessionUUID = OMV.UUID.Parse(pUserAuth.GetProperty("SessionID"));
            string firstName =  pUserAuth.GetProperty("FN");
            string lastName =  pUserAuth.GetProperty("LN");
            uint circuitCode = UInt32.Parse(pUserAuth.GetProperty("CC"));

            // The login operation created the initial circuit
            AgentCircuitData acd = RContext.scene.AuthenticateHandler.GetAgentCircuitData(agentUUID);

            if (acd != null) {
                if (acd.SessionID == sessionUUID) {
                    IClientAPI thisPerson = new RaguAvatar(firstName, lastName,
                                                    agentUUID,
                                                    acd.startpos,   /* initial position */
                                                    OMV.UUID.Zero,  /* owner */
                                                    true,           /* senseAsAgent */
                                                    RContext.scene,
                                                    acd.circuitcode);
                    // Start the client by adding it to the scene and doing event subscriptions
                    thisPerson.Start();

                    // Get the ScenePresence just to make sure we can
                    if (RContext.scene.TryGetScenePresence(agentUUID, out ScenePresence sp)) {
                        RContext.log.Debug("{0} Successful login for {1} {2} ({3})",
                                    _logHeader, firstName, lastName, agentId);
                        ret = true;
                    }
                    else {
                        RContext.log.Error("{0} Failed to create ScenePresence for {1} {2} ({3})",
                                    _logHeader, firstName, lastName, agentId);
                    }
                }
                else {
                    RContext.log.Error("{0} CreateOpenSimPresence: AgentCircuitData does not match SessionID. AgentID={1}",
                            _logHeader, agentId);
                }
            }
            else {
                RContext.log.Error("{0} CreateOpenSimPresence: presence was not created. AgentID={1}",
                            _logHeader, agentId);
            }

            return ret;
        }
    }
}
