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

using BT = org.herbal3d.basil.protocol.BasilType;
using HT = org.herbal3d.transport;

using OMV = OpenMetaverse;

using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;

namespace org.herbal3d.Ragu {

    public class SpaceServerCC : HT.SpaceServerBase {
        private static readonly string _logHeader = "[SpaceServerCC]";

        private readonly RaguContext _context;
        private OSAuthToken _userLoginAuth;

        // Creation of an instance for a specific client.
        // Note: this canceller is for the individual session.
        public SpaceServerCC(RaguContext pContext, CancellationTokenSource pCanceller,
                                        HT.BasilConnection pBasilConnection,
                                        OSAuthToken pOpenSessionAuth) 
                        : base(pCanceller, pBasilConnection, "CC") {
            _context = pContext;
        }

        protected override void DoShutdownWork() {
            return;
        }

        /// <summary>
        ///  The client does an OpenSession with 'login' information. Authorize the
        ///  logged in user.
        /// </summary>
        /// <param name="pUserToken">UserAuth token sent from the client making the OpenSession
        ///     which authenticates the access.</param>
        /// <returns>"true" if the user is authorized</returns>
        protected override bool VerifyClientAuthentication(OSAuthToken pUserToken) {
            // Remember the login auth token because it has a bunch of user context info
            _userLoginAuth = pUserToken;
            // Verify this is good login info bafore accepting login
            return ValidateLoginAuth(pUserToken);
        }

        protected override void DoOpenSessionWork(HT.BasilConnection pConnection, HT.BasilComm pClient, BT.Props pParms) {
            Task.Run(async () => {
                await HandleBasilConnection(pConnection, pClient, pParms);
            });
        }

        // I don't have anything to do for a CloseSession
        protected override void DoCloseSessionWork() {
            _context.log.DebugFormat("{0} DoCloseSessionWork: ", _logHeader);
            return;
        }

        // Received an OpenSession from a Basil client.
        // Connect it to the other layers.
        private async Task HandleBasilConnection(HT.BasilConnection pConnection,
                        HT.BasilComm pClient, BT.Props pParms) {
            try {

                // Create the Circuit and ScenePresence for the user
                CreateOpenSimPresence(_userLoginAuth);

                _context.log.DebugFormat("{0} HandleBasilConnection", _logHeader);

                // Loop through all the layers there are listeners for and tell the
                //    client to connect to the interesting ones.
                foreach (var kvp in _context.LayerListeners) {
                    string layerName = kvp.Key;
                    HT.SpaceServerListener listener = kvp.Value;
                    switch (layerName) {
                        case "Static":
                        case "Dynamic":
                        case "Actors":
                            BT.Props props = new BT.Props() {
                                { "Service", "BasilComm" },
                                { "TransportURL", listener.RemoteConnectionURL },
                                { "ServiceAuth", listener.ListenerParams.P<string>("OpenSessionAuthentication") },
                                { "ServiceHint", layerName }
                            };
                            await pClient.MakeConnectionAsync(props);
                            break;
                        default:
                            // For any others, don't do anything (this happens for "CC")
                            break;
                    }
                }
            }
            catch (Exception e) {
                _context.log.ErrorFormat("{0} HandleBasilConnection. Exception connecting Basil to layers: {1}", _logHeader, e);
            }
        }

        private bool ValidateLoginAuth(OSAuthToken pUserAuth) {
            bool isAuthorized = false;
            if (_context.parms.P<bool>("ShouldEnforceUserAuth")) {
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
