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

    // Processor of incoming messages after we're connected up
    class ProcessActorsIncomingMessages : IncomingMessageProcessor {
        SpaceServerActors _ssContext;
        public ProcessActorsIncomingMessages(SpaceServerActors pContext) : base(pContext) {
            _ssContext = pContext;
        }
        public override void Process(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            switch (pMsg.Op) {
                case (uint)BMessageOps.UpdatePropertiesReq:
                    // TODO:
                    break;
                default:
                    BMessage resp = BasilConnection.MakeResponse(pMsg);
                    resp.Exception = "Unknown operation: " + _ssContext.LayerType;
                    pProtocol.Send(resp);
                    break;
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
                transportParams: new BTransportParams[] {
                    new BTransportWSParams() {
                        preferred       = true,
                        isSecure        = pRContext.parms.GetConnectionParam<bool>(pRContext, SpaceServerActors.StaticLayerType, "WSIsSecure"),
                        port            = pRContext.parms.GetConnectionParam<int>(pRContext, SpaceServerActors.StaticLayerType, "WSPort"),
                        certificate     = pRContext.parms.GetConnectionParam<string>(pRContext, SpaceServerActors.StaticLayerType, "WSCertificate"),
                        disableNaglesAlgorithm = pRContext.parms.GetConnectionParam<bool>(pRContext, SpaceServerActors.StaticLayerType, "DisableNaglesAlgorithm")
                    }
                },
                layer: SpaceServerActors.StaticLayerType,
                canceller: pCanceller,
                logger: pRContext.log,
                // This method is called when the listener receives a connection but before any
                //     messsages have been exchanged.
                processor: (pTransport, pCancellerP) => {
                    return new SpaceServerActors(pRContext, pCancellerP, pTransport);
                }
            );
        }

        public SpaceServerActors(RaguContext pContext, CancellationTokenSource pCanceller, BTransport pTransport) 
                        : base(pContext, pCanceller, pTransport) {
            LayerType = StaticLayerType;

            // The protocol for the initial OpenSession is always JSON
            _protocol = new BProtocolJSON(null, _transport, RContext.log);

            // Expect BMessages and set up messsage processor to handle initial OpenSession
            _connection = new BasilConnection(_protocol, RContext.log);
            _connection.SetOpProcessor(new ProcessMessagesOpenConnection(this));
            _connection.Start();
        }

        protected override void OpenSessionProcessing(BasilConnection pConnection, OSAuthToken pServiceAuth) {
            // We also have a full command processor
            pConnection.SetOpProcessor(new ProcessMessagesOpenConnection(this));

            AddEventSubscriptions();
            AddExistingPresences();
        }

        private void AddEventSubscriptions() {
            RContext.scene.EventManager.OnNewPresence               += Event_OnNewPresence;
            RContext.scene.EventManager.OnRemovePresence            += Event_OnRemovePresence;
            // update to client position (either this or 'significant')
            RContext.scene.EventManager.OnClientMovement            += Event_OnClientMovement;
            // "significant" update to client position
            RContext.scene.EventManager.OnSignificantClientMovement += Event_OnSignificantClientMovement;
            // Gets called for most position/camera/action updates. Seems to be once a second.
            // RContext.scene.EventManager.OnScenePresenceUpdated      += Event_OnScenePresenceUpdated;
        }
        private void RemoveEventSubscriptions() {
            RContext.scene.EventManager.OnNewPresence               -= Event_OnNewPresence;
            RContext.scene.EventManager.OnRemovePresence            -= Event_OnRemovePresence;
            // update to client position (either this or 'significant')
            RContext.scene.EventManager.OnClientMovement            -= Event_OnClientMovement;
            // "significant" update to client position
            RContext.scene.EventManager.OnSignificantClientMovement -= Event_OnSignificantClientMovement;
            // Gets called for most position/camera/action updates
            // RContext.scene.EventManager.OnScenePresenceUpdated      -= Event_OnScenePresenceUpdated;
        }
        private void AddExistingPresences() {
            RContext.scene.GetScenePresences().ForEach(pres => {
                PresenceInfo pi = new PresenceInfo(pres, _connection, this, RContext);
                AddPresence(pi);
                pi.AddAppearanceInstance();
            });
        }

        private void Event_OnNewPresence(ScenePresence pPresence) {
            // RContext.log.Debug("{0} Event_OnNewPresence", _logHeader);
            PresenceInfo pi;
            if (TryFindPresence(pPresence.UUID, out pi)) {
                RContext.log.Error("{0} Event_OnNewPresence: two events for the same presence", _logHeader);
            }
            else {
                pi = new PresenceInfo(pPresence, _connection, this, RContext);
                AddPresence(pi);
                pi.AddAppearanceInstance();
            }
        }
        private void Event_OnRemovePresence(OMV.UUID pPresenceUUID) {
            // RContext.log.Debug("{0} Event_OnRemovePresence", _logHeader);
            if (TryFindPresence(pPresenceUUID, out PresenceInfo pi)) {
                pi.RemoveAppearanceInstance();
                RemovePresence(pi);
            }
        }
        private void Event_OnClientMovement(ScenePresence pPresence) {
            // RContext.log.Debug("{0} Event_OnClientMovement", _logHeader);
            if (TryFindPresence(pPresence, out PresenceInfo pi)) {
                pi.UpdatePosition();
            }
            else {
                RContext.log.Error("{0} Event_OnClientMovement: did not find presence", _logHeader);
            }
        }
        private void Event_OnSignificantClientMovement(ScenePresence pPresence) {
            // RContext.log.Debug("{0} Event_OnSignificantClientMovement", _logHeader);
            if (TryFindPresence(pPresence, out PresenceInfo pi)) {
                pi.UpdatePosition();
            }
            else {
                RContext.log.Error("{0} Event_OnSignificantClientMovement. Did not find presence", _logHeader);
            }
        }
        private void Event_OnScenePresenceUpdated(ScenePresence pPresence) {
            // RContext.log.Debug("{0} Event_OnScenePresenceUpdated", _logHeader);
            if (TryFindPresence(pPresence, out PresenceInfo pi)) {
                pi.UpdatePosition();
            }
        }

        private List<PresenceInfo> _presences = new List<PresenceInfo>();
        // Find a presence based on it's ScenePresence instance
        private bool TryFindPresence(ScenePresence pScenePresence, out PresenceInfo pFound) {
            bool ret = false;
            lock (_presences) {
                try {
                    pFound = _presences.Where(p => p.presence == pScenePresence).First();
                    ret = true;
                }
                catch {
                    pFound = null;
                }
            }
            return ret;
        }
        // Find a presence using it's UUID
        private bool TryFindPresence(OMV.UUID pPresenceId, out PresenceInfo pFound) {
            bool ret = false;
            lock (_presences) {
                try {
                    pFound = _presences.Where(p => p.presence.UUID == pPresenceId).First();
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
            private BasilConnection _connection;
            private SpaceServerActors _spaceServer;
            // private BT.ItemId _instanceId;
            private string _instanceId;

            public PresenceInfo(ScenePresence pPresence, BasilConnection pConnection, SpaceServerActors pSpaceServer, RaguContext pContext) {
                presence = pPresence;
                _context = pContext;
                _connection = pConnection;
                _spaceServer = pSpaceServer;
            }
            public async void UpdatePosition() {
                if (_instanceId != null) {
                    var coordParams = new AbPlacement() {
                        WorldPos = GetWorldPosition(),
                        WorldRot = GetWorldRotation()
                    };
                    await _connection.UpdateProperties(_instanceId, coordParams);
                    // _context.log.Debug("{0} UpdatePosition: p={1}, r={2}",
                    //             _logHeader, presence.AbsolutePosition, presence.Rotation);
                }
            }
            public void UpdateAppearance() {
                if (_instanceId != null) {
                }
            }
            public void AddAppearanceInstance() {
                Task.Run(async () => {
                    try {
                        // TODO: use avatar appearance baking code to build GLTF version of avatar
                        // For the moment, use a canned, static mesh
                        string tempAppearanceURL = "https://files.misterblue.com/BasilTest/gltf/Duck/glTF/Duck.gltf";

                        AbilityList props = new AbilityList();
                        props.Add(
                            new AbAssembly() {
                                AssetURL = tempAppearanceURL,
                                AssetAuth = RaguAssetService.Instance.AccessToken.Token,
                            }
                        );
                        props.Add(new AbPlacement() {
                                WorldPos = GetWorldPosition(),
                                WorldRot = GetWorldRotation()
                            }
                        );
                        BMessage resp = await _connection.CreateItem(props);
                        _instanceId = AbBItem.GetId(resp);
                    }
                    catch (Exception e) {
                        _context.log.Error("{0} AddAppearanceInstance: exception adding appearance: {1}",
                                        _logHeader, e);
                    };
                });
            }

            public void RemoveAppearanceInstance() {
                Task.Run( async () => {
                    if (_instanceId != null) {
                        await _connection.DeleteItem(_instanceId);
                        _instanceId = null;
                    };
                });
            }
            // Return the Instance's position converted from OpenSim Zup to GLTF Yup
            public double[] GetWorldPosition() {
                OMV.Vector3 thePos = CoordAxis.ConvertZupToYup(presence.AbsolutePosition);
                // OMV.Vector3 thePos = presence.AbsolutePosition;
                return new double[] { thePos.X, thePos.Y, thePos.Z };
            }
            // Return the Instance's rotation converted from OpenSim Zup to GLTF Yup
            public double[] GetWorldRotation() {
                OMV.Quaternion theRot = presence.Rotation;
                // OMV.Quaternion theRot = CoordAxis.ConvertZupToYup(presence.Rotation);
                return new double[] { theRot.X, theRot.Y, theRot.Z, theRot.W };
            }
        }
    }
}
