// Copyright (c) 2022 Robert Adams
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
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using org.herbal3d.OSAuth;
using org.herbal3d.transport;
using org.herbal3d.b.protocol;

using OpenSim.Region.Framework.Scenes;
using org.herbal3d.cs.CommonEntities;

using OMV = OpenMetaverse;

namespace org.herbal3d.Ragu {

    class ProcessActorsOpenConnection : IncomingMessageProcessor {
        SpaceServerActors _ssContext;
        public ProcessActorsOpenConnection(SpaceServerActors pContext) : base(pContext) {
            _ssContext = pContext;
        }
        public override void Process(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            if (pMsg.Op == (uint)BMessageOps.OpenSessionReq) {
                _ssContext.ProcessOpenSessionReq(pMsg, pConnection, pProtocol);
            }
            else {
                BMessage resp = BasilConnection.MakeResponse(pMsg);
                resp.Exception = "Session is not open. Actors";
                pProtocol.Send(resp);
            }
        }
    }
    class ProcessActorsIncomingMessages : IncomingMessageProcessor {
        SpaceServerActors _ssContext;
        public ProcessActorsIncomingMessages(SpaceServerActors pContext) : base(pContext) {
            _ssContext = pContext;
        }
        public override void Process(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            if (pMsg.Op == (uint)BMessageOps.OpenSessionReq) {
                _ssContext.ProcessOpenSessionReq(pMsg, pConnection, pProtocol);
            }
            else {
                BMessage resp = BasilConnection.MakeResponse(pMsg);
                resp.Exception = "Session is not open. ActorsIC";
                pProtocol.Send(resp);
            }
        }
    }
    public class SpaceServerActors : SpaceServerBase {
        private static readonly string _logHeader = "[SpaceServerActors]";

        public static readonly string StaticLayerType = "Actors";

        // Function called to start up the service listener.
        // THis starts listening for network connections and creates instances of the SpaceServer
        //     for each of the incoming connections
        public static SpaceServerListener SpaceServerActorsService(RaguContext pRContext, CancellationTokenSource pCanceller) {
            return new SpaceServerListener(
                connectionURL:          pRContext.parms.SpaceServerActors_ConnectionURL,
                layer:                  SpaceServerActors.StaticLayerType,
                isSecure:               pRContext.parms.SpaceServerActors_IsSecure,
                secureConnectionURL:    pRContext.parms.SpaceServerActors_SecureConnectionURL,
                certificate:            pRContext.parms.SpaceServerActors_Certificate,
                disableNaglesAlgorithm: pRContext.parms.SpaceServerActors_DisableNaglesAlgorithm,
                canceller:              pCanceller,
                logger:                 pRContext.log,
                // This method is called when the listener receives a connection but before any
                //     messsages have been exchanged.
                processor:              (pTransport, pCancellerP) => {
                                            return new SpaceServerActors(pRContext, pCancellerP, pTransport);
                                        }
            );
        }

        private readonly RaguContext _rContext;

        public SpaceServerActors(RaguContext pContext, CancellationTokenSource pCanceller, BTransport pTransport) 
                        : base(pContext, pCanceller, pTransport) {
            _rContext = pContext;
            LayerType = StaticLayerType;

            // The protocol for the initial OpenSession is always JSON
            _protocol = new BProtocolJSON(null, _transport);

            // Expect BMessages and set up messsage processor to handle initial OpenSession
            _connection = new BasilConnection(_protocol, _context.log);
            _connection.SetOpProcessor(new ProcessActorsOpenConnection(this));
        }

        public void ProcessOpenSessionReq(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            string errorReason = "";
            // Get the login information from the OpenConnection
            if (pMsg.IProps.TryGetValue("clientAuth", out string clientAuthToken)) {
                if (pMsg.IProps.TryGetValue("Auth", out string serviceAuth)) {
                    // have the info to try and log the user in
                    OSAuthToken loginAuth = OSAuthToken.FromString(serviceAuth);
                    if (ValidateLoginAuth(loginAuth)) {
                        // The user checks out so construct the success response
                        OSAuthToken incomingAuth = new OSAuthToken();
                        OSAuthToken outgoingAuth = OSAuthToken.FromString(clientAuthToken);
                        pConnection.SetAuthorizations(incomingAuth, outgoingAuth);

                        // We also have a full command processor
                        pConnection.SetOpProcessor(new ProcessActorsIncomingMessages(this));

                        BMessage resp = BasilConnection.MakeResponse(pMsg);
                        resp.IProps.Add("ServerVersion", _context.ServerVersion);
                        resp.IProps.Add("ServerAuth", incomingAuth.Token);
                        pConnection.Send(resp);

                        // Talk about the avatars
                        Task.Run(() => {
                            StartConnection(pConnection, loginAuth);
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

        private bool ValidateLoginAuth(OSAuthToken pUserAuth) {
            bool ret = false;
            string auth = pUserAuth.Token;
            lock (_context.waitingForMakeConnection) {
                if (_context.waitingForMakeConnection.TryGetValue(auth, out WaitingInfo waitingInfo)) {
                    _context.log.Debug("{0}: login auth successful. Waited {1} seconds", _logHeader,
                        (DateTime.Now - waitingInfo.whenCreated).TotalSeconds);
                    _context.waitingForMakeConnection.Remove(auth);
                    ret = true;
                }
                else {
                    _context.log.Debug("{0}: login auth unsuccessful. Token: {1}", _logHeader, auth);
                }
            }
            return ret;
        }

        private void StartConnection(BasilConnection pConnection, OSAuthToken pServiceAuth) {
            AddEventSubscriptions();
            AddExistingPresences();
        }

        private void AddEventSubscriptions() {
            _context.scene.EventManager.OnNewPresence               += Event_OnNewPresence;
            _context.scene.EventManager.OnRemovePresence            += Event_OnRemovePresence;
            // update to client position (either this or 'significant')
            _context.scene.EventManager.OnClientMovement            += Event_OnClientMovement;
            // "significant" update to client position
            _context.scene.EventManager.OnSignificantClientMovement += Event_OnSignificantClientMovement;
            // Gets called for most position/camera/action updates. Seems to be once a second.
            // _context.scene.EventManager.OnScenePresenceUpdated      += Event_OnScenePresenceUpdated;
        }
        private void RemoveEventSubscriptions() {
            _context.scene.EventManager.OnNewPresence               -= Event_OnNewPresence;
            _context.scene.EventManager.OnRemovePresence            -= Event_OnRemovePresence;
            // update to client position (either this or 'significant')
            _context.scene.EventManager.OnClientMovement            -= Event_OnClientMovement;
            // "significant" update to client position
            _context.scene.EventManager.OnSignificantClientMovement -= Event_OnSignificantClientMovement;
            // Gets called for most position/camera/action updates
            // _context.scene.EventManager.OnScenePresenceUpdated      -= Event_OnScenePresenceUpdated;
        }
        private void AddExistingPresences() {
            _context.scene.GetScenePresences().ForEach(pres => {
                PresenceInfo pi = new PresenceInfo(pres, this, _context);
                AddPresence(pi);
                pi.AddAppearanceInstance();
            });
        }

        private void Event_OnNewPresence(ScenePresence pPresence) {
            // _context.log.DebugFormat("{0} Event_OnNewPresence", _logHeader);
            PresenceInfo pi;
            if (FindPresence(pPresence.UUID, out pi)) {
                _context.log.Error("{0} Event_OnNewPresence: two events for the same presence", _logHeader);
            }
            else {
                pi = new PresenceInfo(pPresence, this, _context);
                AddPresence(pi);
                pi.AddAppearanceInstance();
            }
        }
        private void Event_OnRemovePresence(OMV.UUID pPresenceUUID) {
            // _context.log.DebugFormat("{0} Event_OnRemovePresence", _logHeader);
            if (FindPresence(pPresenceUUID, out PresenceInfo pi)) {
                pi.RemoveAppearanceInstance();
                RemovePresence(pi);
            }
        }
        private void Event_OnClientMovement(ScenePresence pPresence) {
            // _context.log.DebugFormat("{0} Event_OnClientMovement", _logHeader);
            if (FindPresence(pPresence, out PresenceInfo pi)) {
                pi.UpdatePosition();
            }
        }
        private void Event_OnSignificantClientMovement(ScenePresence pPresence) {
            // _context.log.DebugFormat("{0} Event_OnSignificantClientMovement", _logHeader);
            if (FindPresence(pPresence, out PresenceInfo pi)) {
                pi.UpdatePosition();
            }
        }
        private void Event_OnScenePresenceUpdated(ScenePresence pPresence) {
            // _context.log.DebugFormat("{0} Event_OnScenePresenceUpdated", _logHeader);
            if (FindPresence(pPresence, out PresenceInfo pi)) {
                pi.UpdatePosition();
            }
        }

        private List<PresenceInfo> _presences = new List<PresenceInfo>();
        // Find a presence based on it's ScenePresence instance
        private bool FindPresence(ScenePresence pScenePresence, out PresenceInfo pFound) {
            bool ret = false;
            lock (_presences) {
                try {
                    PresenceInfo found = _presences.Where(p => p.presence == pScenePresence).First();
                    pFound = found;
                    ret = true;
                }
                catch {
                    pFound = null;
                }
            }
            return ret;
        }
        // Find a presence using it's UUID
        private bool FindPresence(OMV.UUID pPresenceId, out PresenceInfo pFound) {
            bool ret = false;
            lock (_presences) {
                try {
                    PresenceInfo found = _presences.Where(p => p.presence.UUID == pPresenceId).First();
                    pFound = found;
                    ret = true;
                }
                catch {
                    pFound = null;
                }
            }
            return ret;
        }
        private void AddPresence(PresenceInfo pPI) {
            lock (_presences) {
                _presences.Add(pPI);
            }
        }
        private void RemovePresence(PresenceInfo pPI) {
            lock (_presences) {
                _presences.Remove(pPI);
            }
        }
        // The connection is going down or something. Remove all presences from the scene.
        private void DeleteAllPresences() {
            lock (_presences) {
                while (_presences.Count > 0) {
                    PresenceInfo pres = _presences.First();
                    _presences.Remove(pres);
                    pres.RemoveAppearanceInstance();
                }
            }
        }


        // Local class for a presence and the operations we do on the Basil display.
        private class PresenceInfo {
            private static readonly string _logHeader = "[PresenceInfo]";

            public ScenePresence presence;
            private RaguContext _context;
            private SpaceServerActors _spaceServer;
            // private BT.ItemId _instanceId;
            private string _instanceId;

            public PresenceInfo(ScenePresence pPresence, SpaceServerActors pSpaceServer, RaguContext pContext) {
                presence = pPresence;
                _context = pContext;
                _spaceServer = pSpaceServer;
            }
            public async void UpdatePosition() {
                /*
                if (_instanceId != null) {
                    BT.Props props = new BT.Props();
                    PackageInstancePosition(ref props);
                    await _spaceServer.Client.UpdatePropertiesAsync(_instanceId, props);
                    // _context.log.DebugFormat("{0} UpdatePosition: p={1}, r={2}",
                    //             _logHeader, presence.AbsolutePosition, presence.Rotation);
                }
                */
            }
            public void UpdateAppearance() {
                if (_instanceId != null) {
                }
            }
            public void AddAppearanceInstance() {
            /*
                Task.Run(async () => {
                    try {
                        // TODO: use avatar appearance baking code to build GLTF version of avatar
                        // For the moment, use a canned, static mesh
                        string tempAppearanceURL = "https://files.misterblue.com/BasilTest/gltf/Duck/glTF/Duck.gltf";

                        // Create the displayable for the actor
                        BT.Props displayableProps = new BT.Props();
                        BT.AbilityList displayableAbilities = new BT.AbilityList() {
                            new BT.AbilityDisplayable() {
                                DisplayableUrl = tempAppearanceURL,
                                AssetServiceType = "HTTP",
                                DisplayableType = "meshset",
                                LoaderType = "GLTF"
                            }
                        };
                        BT.Props displayableResp = await _spaceServer.Client.CreateItemAsync(displayableProps, displayableAbilities);
                        _instanceId = new BT.ItemId(displayableResp["ItemId"]);
                        // _context.log.DebugFormat("{0} AddAppearanceInstance: created displayable: {1}",
                        //                 _logHeader, _instanceId);

                        // Add the instance to that displayable
                        BT.Props instanceProps = new BT.Props() {
                            {  "displayableItemId", _instanceId.Id }
                        };
                        PackageInstancePosition(ref instanceProps);
                        // _context.log.DebugFormat("{0} AddAppearanceInstance: instance props={1}",
                        //             _logHeader, instanceProps.ToString());
                        BT.AbilityList instanceAbilities = new AbilityList() {
                            new BT.AbilityInstance(instanceProps)
                        };
                        BT.Props instanceResp = await _spaceServer.Client.AddAbilityAsync(_instanceId, instanceAbilities);
                    }
                    catch (Exception e) {
                        _context.log.DebugFormat("{0} AddAppearanceInstance: exception adding appearance: {1}",
                                        _logHeader, e);
                    };
                });
            */
            }

            public void RemoveAppearanceInstance() {
                Task.Run( async () => {
                    if (_instanceId != null) {
                        // await _spaceServer.Client.DeleteItemAsync(_instanceId);
                        _instanceId = null;
                    };
                });
            }
            /*
            // Return an InstancePositionInfo with the presence's current position
            public void PackageInstancePosition(ref BT.Props pProps) {
                // Convert coordinates from OpenSim Zup to GLTF Yup
                OMV.Vector3 thePos = CoordAxis.ConvertZupToYup(presence.AbsolutePosition);
                OMV.Quaternion theRot = CoordAxis.ConvertZupToYup(presence.Rotation);
                pProps.Add("Pos", String.Format("[{0},{1},{2}]", thePos.X, thePos.Y, thePos.Z));
                pProps.Add("Rot", String.Format("[{0},{1},{2},{3}]", theRot.X, theRot.Y, theRot.Z, theRot.W));
            }
            */
        }
    }
}
