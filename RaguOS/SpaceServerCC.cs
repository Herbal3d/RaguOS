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
using BasilType = org.herbal3d.basil.protocol.BasilType;
using org.herbal3d.OSAuth;

using OMV = OpenMetaverse;

using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;

namespace org.herbal3d.Ragu {

    public class SpaceServerCCListener : SpaceServerLayer {

        // Initial SpaceServerCC invocation with no transport setup.
        // Create a receiving connection and create SpaceServer when Basil connections come in.
        // Note: this canceller is for the overall layer.
        public SpaceServerCCListener(RaguContext pContext, CancellationTokenSource pCanceller)
                        : base(pContext, pCanceller, "SpaceServerCC") {
        }

        // Once someone connects, create a session instance of myself
        protected override SpaceServerLayer InstanceFactory(RaguContext pContext,
                        CancellationTokenSource pCanceller, HTransport.BasilConnection pConnection) {
            return new SpaceServerCC(pContext, pCanceller, this, pConnection);
        }
    }

    public class SpaceServerCC : SpaceServerLayer {
        // Creation of an instance for a specific client.
        // Note: this canceller is for the individual session.
        public SpaceServerCC(RaguContext pContext, CancellationTokenSource pCanceller,
                                        SpaceServerLayer pListener, HTransport.BasilConnection pBasilConnection) 
                        : base(pContext, pCanceller, pListener, "SpaceServerCC", pBasilConnection) {

            // This assignment directs the space server message calls to this ISpaceServer instance.
            _clientConnection.SpaceServiceProcessor.SpaceServerMsgHandler = this;

            // The thing to call to make requests to the Basil server
            _client = new HTransport.BasilClient(pBasilConnection);
        }

        // This one client has disconnected
        public override void Shutdown() {
            _canceller.Cancel();
            if (_client != null) {
                _client = null;
            }
            if (_clientConnection != null) {
                _clientConnection.SpaceServiceProcessor.SpaceServerMsgHandler = null;
                _clientConnection = null;
            }
            base.Shutdown();
        }

        // Request from Basil to open a SpaceServer session
        public override SpaceServer.OpenSessionResp OpenSession(SpaceServer.OpenSessionReq pReq) {
            _context.log.DebugFormat("{0} OpenSession.", _logHeader);

            var ret = new SpaceServer.OpenSessionResp();

            // DEBUG DEBUG
            _context.log.DebugFormat("{0} OpenSession Features:", _logHeader);
            foreach (var kvp in pReq.Features) {
                _context.log.DebugFormat("{0}     {1}: {2}", _logHeader, kvp.Key, kvp.Value);
            };
            // END DEBUG DEBUG

            // Check if this is a test connection. We cannot handle those.
            // Respond with an error message.
            if (CheckIfTestConnection(pReq, ref ret)) {
                ret.Exception = new BasilType.BasilException() {
                    Reason = "Test session not acceptable"
                };
                return ret;
            }

            // Check for an authorized connection
            if (ValidateLoginAuth(pReq.Auth, out OSAuthModule auther, out OSAuthToken userAuth)) {

                // Use common processing routine for all the SpaceServer layers
                ret = base.HandleOpenSession(pReq);

                if (ret.Exception == null) {
                    // Set the processor for the new client go.
                    // This sends the connections for the layers to Basil.
                    Task.Run(async () => {
                        await HandleBasilConnection(auther, userAuth);
                    }, _canceller.Token);
                }
            }
            else {
                ret.Exception = new BasilType.BasilException() {
                    Reason = "Not authorized"
                };
            }
            return ret;
        }

        // Request from Basil to close the SpaceServer session
        public override SpaceServer.CloseSessionResp CloseSession(SpaceServer.CloseSessionReq pReq) {
            throw new NotImplementedException();
        }

        // Request from Basil to move the camera.
        public override SpaceServer.CameraViewResp CameraView(SpaceServer.CameraViewReq pReq) {
            throw new NotImplementedException();
        }

        // Received an OpenSession from a Basil client.
        // Connect it to the other layers.
        private async Task HandleBasilConnection(OSAuthModule pAuther, OSAuthToken pUserAuth) {
            try {

                // Create the Circuit and ScenePresence for the user
                CreateOpenSimPresence(pAuther, pUserAuth);

                _context.log.DebugFormat("{0} HandleBasilConnection", _logHeader);

                // Authorization for this connection back to the other side
                BasilType.AccessAuthorization auth = base.CreateAccessAuthorization(base.ClientAuth);

                if (_context.layerStatic != null) {
                    Dictionary<string, string> props = new Dictionary<string, string>() {
                        { "Service", "SpaceServerClient" },
                        { "TransportURL", _context.layerStatic.RemoteConnectionURL },
                        { "ServiceAuth", _context.layerStatic.ListenerAuth.Token },
                        { "ServiceHint", "static" }
                    };
                    await _client.MakeConnectionAsync(auth, props);
                }

                if (_context.layerDynamic != null) {
                    Dictionary<string, string> props = new Dictionary<string, string>() {
                        { "Service", "SpaceServerClient" },
                        { "TransportURL", _context.layerDynamic.RemoteConnectionURL },
                        { "ServiceAuth", _context.layerDynamic.ListenerAuth.Token },
                        { "ServiceHint", "dynamic" }
                    };
                    await _client.MakeConnectionAsync(auth, props);
                }

                if (_context.layerActors != null) {
                    Dictionary<string, string> props = new Dictionary<string, string>() {
                        { "Service", "SpaceServerClient" },
                        { "TransportURL", _context.layerActors.RemoteConnectionURL },
                        { "ServiceAuth", _context.layerActors.ListenerAuth.Token },
                        { "ServiceHint", "actors" }
                    };
                    await _client.MakeConnectionAsync(auth, props);
                }
            }
            catch (Exception e) {
                _context.log.ErrorFormat("{0} HandleBasilConnection. Exception connecting Basil to layers: {1}", _logHeader, e);
            }
        }

        private bool ValidateLoginAuth(BasilType.AccessAuthorization pAuth, out OSAuthModule pAuther, out OSAuthToken pUserAuth) {
            OSAuthModule auther = null;
            OSAuthToken userAuthToken = null;
            bool isAuthorized = false;
            try {
                auther = _context.scene.RequestModuleInterface<OSAuthModule>();
                if (_context.parms.P<bool>("ShouldEnforceUserAuth")) {
                    if (auther != null && pAuth != null) {
                        if (pAuth.AccessProperties.TryGetValue("Auth", out string userAuth)) {
                            userAuthToken = OSAuthToken.FromString(userAuth);
                            // _context.log.DebugFormat("{0} ValidateLoginAuth: userAuthJSON={1}",
                            //             _logHeader, userAuthToken.TokenJSON);

                            try {
                                string agentId = userAuthToken.GetProperty("AgentId");
                                OMV.UUID agentUUID = OMV.UUID.Parse(agentId);
                                OMV.UUID sessionID = OMV.UUID.Parse(userAuthToken.GetProperty("SessionID"));
                                OMV.UUID secureSessionID = OMV.UUID.Parse(userAuthToken.GetProperty("SSID"));
                                uint circuitCode = UInt32.Parse(userAuthToken.GetProperty("CC"));

                                AgentCircuitData acd = _context.scene.AuthenticateHandler.GetAgentCircuitData(agentUUID);
                                if (acd != null) {
                                    if (acd.circuitcode == circuitCode) {
                                        if (acd.SessionID == sessionID) {
                                            if (acd.SecureSessionID == secureSessionID) {
                                                isAuthorized = true;
                                            }
                                            else {
                                                _context.log.DebugFormat("{0} ValidateUserAuth: Failed secureSessionID test. AgentId={1}",
                                                            _logHeader, agentId);
                                            }
                                        }
                                        else {
                                            _context.log.DebugFormat("{0} ValidateUserAuth: Failed sessionId test. AgentId={1}",
                                                        _logHeader, agentId);
                                        }
                                    }
                                    else {
                                        _context.log.DebugFormat("{0} ValidateUserAuth: Failed circuitCode test. AgentId={1}",
                                                    _logHeader, agentId);
                                    }
                                }
                            }
                            catch (Exception e) {
                                _context.log.ErrorFormat("{0} ValidateUserAuth: exception authorizing: {1}",
                                            _logHeader, e);
                                isAuthorized = false;
                            }
                        }
                    }
                }
                else {
                    isAuthorized = true;
                }
            }
            catch (Exception e) {
                isAuthorized = false;
                _context.log.ErrorFormat("{0} Exception checking login authentication: e={1}", _logHeader, e);
            }
            pAuther = auther;
            pUserAuth = userAuthToken;
            return (auther != null) && isAuthorized;
        }

        // Create  the OpenSimulator ScenePResence and the associated structures
        //    that fake out the simulator so it thinks it is connected to a viewer.
        // Code cribbed from NPC (INPCModule.cs), DSG (http://github.com/intelvwi/DSG),
        //    and LoginService (LLLoginService.cs).
        // The user OSAuthToken contains extra login information needed for creating
        //    the circuit, etc.
        private bool CreateOpenSimPresence(OSAuthModule pAuther, OSAuthToken pUserAuth) {
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
                        _context.log.DebugFormat("{0} Successful login for {1} {2} ({3})",
                                    _logHeader, firstName, lastName, agentId);
                        ret = true;
                    }
                    else {
                        _context.log.ErrorFormat("{0} Failed to create ScenePresence for {1} {2} ({3})",
                                    _logHeader, firstName, lastName, agentId);
                    }
                }
                else {
                    _context.log.ErrorFormat("{0} CreateOpenSimPresence: AgentCircuitData does not match SessionID. AgentID={1}",
                            _logHeader, agentId);
                }
            }
            else {
                _context.log.ErrorFormat("{0} CreateOpenSimPresence: presence was not created. AgentID={1}",
                            _logHeader, agentId);
            }

            return ret;
        }
    }
}
