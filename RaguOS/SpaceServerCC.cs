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
    class ProcessCCOpenConnection : IncomingMessageProcessor {
        SpaceServerCC _ssContext;
        public ProcessCCOpenConnection(SpaceServerCC pContext) : base(pContext) {
            _ssContext = pContext;
        }
        public override void Process(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            if (pMsg.Op == (uint)BMessageOps.OpenSessionReq) {
                _ssContext.ProcessOpenSessionReq(pMsg, pConnection, pProtocol);
            }
            else {
                BMessage resp = BasilConnection.MakeResponse(pMsg);
                resp.Exception = "Session is not open. AA";
                pProtocol.Send(resp);
            }
        }
    }

    public class SpaceServerCC : SpaceServerBase {
        private static readonly string _logHeader = "[SpaceServerCC]";

        public static readonly string StaticLayerType = "CC";

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
            _protocol = new BProtocolJSON(null, _transport);

            // Expect BMessages and set up messsage processor to handle initial OpenSession
            _connection = new BasilConnection(_protocol, _context.log);
            _connection.SetOpProcessor(new ProcessCCOpenConnection(this));
        }

        // Called when an OpenConnection is received
        public void ProcessOpenSessionReq(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            string errorReason = "";
            // Get the login information from the OpenConnection
            if (pMsg.IProps.TryGetValue("clientAuth", out string clientAuthToken)) {
                if (pMsg.IProps.TryGetValue("Auth", out string serviceAuth)) {
                    // have the info to try and log the user in
                    OSAuthToken loginInfo = OSAuthToken.FromString(serviceAuth);
                    if (ValidateLoginAuth(loginInfo)) {
                        // The user checks out so construct the success response
                        OSAuthToken incomingAuth = new OSAuthToken();
                        OSAuthToken outgoingAuth = OSAuthToken.FromString(clientAuthToken);
                        pConnection.SetAuthorizations(incomingAuth, outgoingAuth);

                        BMessage resp = BasilConnection.MakeResponse(pMsg);
                        resp.IProps.Add("ServerVersion", _context.ServerVersion);
                        resp.IProps.Add("ServerAuth", incomingAuth.Token);
                        pConnection.Send(resp);

                        // Connect the user to the various other layers in the background
                        Task.Run(() => {
                            StartConnection(pConnection, loginInfo);
                        });
                    }
                    else {
                        errorReason = "Login credentials not valid";
                    }
                }
                else {
                    errorReason = "Login credentials not supplied (serviceAuth)";
                }
            }
            else {
                errorReason = "Connection auth not supplied (clientAuth)";
            }

            // If an error happened, return error response
            if (errorReason.Length > 0) {
                BMessage resp = BasilConnection.MakeResponse(pMsg);
                resp.Exception = errorReason;
                pConnection.Send(resp);
            }
        }

        // Received an OpenSession from a Basil client.
        // Connect it to the other layers.
        private async void StartConnection(BasilConnection pConnection, OSAuthToken pServiceAuth) {
            try {
                // Create the Circuit and ScenePresence for the user
                CreateOpenSimPresence(pServiceAuth);

                _context.log.Debug("{0} HandleBasilConnection", _logHeader);

                // Invite the client to connect to the interesting layers.
                string[] inviteLayers = { SpaceServerStatic.StaticLayerType,
                        SpaceServerActors.StaticLayerType,
                        SpaceServerDynamic.StaticLayerType };
                foreach (string layerName in inviteLayers ) {
                    // The MakeConnection sends an auth that this remembers so it can be checked
                    //     when the OpenConnection is eventually received.
                    WaitingInfo waiting = new WaitingInfo();
                    this._context.waitingForMakeConnection.Add(waiting.incomingAuth.Token, waiting);

                    SpaceServerListener layer = _context.LayerListeners[layerName];
                    ParamBlock pp = layer.ParamsForMakeConnection(waiting.incomingAuth);
                    await pConnection.MakeConnection(pp);
                }
            }
            catch (Exception e) {
                _context.log.Error("{0} HandleBasilConnection. Exception connecting Basil to layers: {1}", _logHeader, e);
            }
        }

        bool ValidateLoginAuth(OSAuthToken pUserAuth) {
            bool isAuthorized = false;
            if (_context.parms.ShouldEnforceUserAuth) {
                try {
                    string agentId = pUserAuth.GetProperty("AgentId");
                    OMV.UUID agentUUID = OMV.UUID.Parse(agentId);
                    OMV.UUID sessionID = OMV.UUID.Parse(pUserAuth.GetProperty("SessionID"));
                    OMV.UUID secureSessionID = OMV.UUID.Parse(pUserAuth.GetProperty("SSID"));
                    uint circuitCode = UInt32.Parse(pUserAuth.GetProperty("CC"));
                    // _context.log.DebugFormat("{0} ValidateLoginAuth: agentUUID={1}, sessionID={2}, secureSessionID={3}, circuitCode={4}",
                    //             _logHeader, agentUUID, sessionID, secureSessionID, circuitCode);

                    AgentCircuitData acd = _context.scene.AuthenticateHandler.GetAgentCircuitData(agentUUID);
                    if (acd != null) {
                        if (acd.circuitcode == circuitCode) {
                            if (acd.SessionID == sessionID) {
                                if (acd.SecureSessionID == secureSessionID) {
                                    isAuthorized = true;
                                }
                                else {
                                    _context.log.Debug("{0} ValidateUserAuth: Failed secureSessionID test. AgentId={1}",
                                                _logHeader, agentId);
                                }
                            }
                            else {
                                _context.log.Debug("{0} ValidateUserAuth: Failed sessionId test. AgentId={1}",
                                            _logHeader, agentId);
                            }
                        }
                        else {
                            _context.log.Debug("{0} ValidateUserAuth: Failed circuitCode test. AgentId={1}",
                                        _logHeader, agentId);
                        }
                    }
                }
                catch (Exception e) {
                    _context.log.Error("{0} ValidateUserAuth: exception authorizing: {1}",
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
            AgentCircuitData acd = _context.scene.AuthenticateHandler.GetAgentCircuitData(agentUUID);

            if (acd != null) {
                if (acd.SessionID == sessionUUID) {
                    IClientAPI thisPerson = new RaguAvatar(firstName, lastName,
                                                    agentUUID,
                                                    acd.startpos,   /* initial position */
                                                    OMV.UUID.Zero,  /* owner */
                                                    true,           /* senseAsAgent */
                                                    _context.scene,
                                                    acd.circuitcode);
                    // Start the client by adding it to the scene and doing event subscriptions
                    thisPerson.Start();

                    // Get the ScenePresence just to make sure we can
                    if (_context.scene.TryGetScenePresence(agentUUID, out ScenePresence sp)) {
                        _context.log.Debug("{0} Successful login for {1} {2} ({3})",
                                    _logHeader, firstName, lastName, agentId);
                        ret = true;
                    }
                    else {
                        _context.log.Error("{0} Failed to create ScenePresence for {1} {2} ({3})",
                                    _logHeader, firstName, lastName, agentId);
                    }
                }
                else {
                    _context.log.Error("{0} CreateOpenSimPresence: AgentCircuitData does not match SessionID. AgentID={1}",
                            _logHeader, agentId);
                }
            }
            else {
                _context.log.Error("{0} CreateOpenSimPresence: presence was not created. AgentID={1}",
                            _logHeader, agentId);
            }

            return ret;
        }
    }
}
