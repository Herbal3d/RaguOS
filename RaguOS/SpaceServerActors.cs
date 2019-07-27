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

using OpenSim.Region.Framework.Scenes;

using SpaceServer = org.herbal3d.basil.protocol.SpaceServer;
using HTransport = org.herbal3d.transport;
using BasilType = org.herbal3d.basil.protocol.BasilType;
using org.herbal3d.cs.CommonEntities;
using org.herbal3d.OSAuth;

using Google.Protobuf;

using OMV = OpenMetaverse;

namespace org.herbal3d.Ragu {

    public class SpaceServerActorsLayer : SpaceServerLayer {

        // Initial SpaceServerActors invocation with no transport setup.
        // Create a receiving connection and create SpaceServer when Basil connections come in.
        // Note: this canceller is for the overall layer.
        public SpaceServerActorsLayer(RaguContext pContext, CancellationTokenSource pCanceller)
                        : base(pContext, pCanceller, "SpaceServerActors") {
        }

        // Return an instance of me
        protected override SpaceServerLayer InstanceFactory(RaguContext pContext,
                        CancellationTokenSource pCanceller, HTransport.BasilConnection pConnection) {
            return new SpaceServerActors(pContext, pCanceller, pConnection);
        }
    }

    public class SpaceServerActors : SpaceServerLayer {
        // Creation of an instance for a specific client.
        // Note: this canceller is for the individual session.
        public SpaceServerActors(RaguContext pContext, CancellationTokenSource pCanceller,
                                        HTransport.BasilConnection pBasilConnection) 
                        : base(pContext, pCanceller, "SpaceServerActors", pBasilConnection) {

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

            if (base.ValidateUserAuth(pReq.Auth, out OSAuthModule auther, out bool authorized)) {

                // Use common processing routine for all the SpaceServer layers
                ret = base.HandleOpenSession(pReq, auther, out string sessionKey, out string connectionKey);

                // Start sending stuff to our new Basil friend.
                HandleBasilConnection();
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

        private void HandleBasilConnection() {
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
            if (FindPresence(pPresence.UUID, out PresenceInfo aPresence)) {
                _context.log.ErrorFormat("{0} Event_OnNewPresence: two events for the same presence", _logHeader);
            }
            else {
                PresenceInfo pi = new PresenceInfo(pPresence, this, _context);
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

        // Local class for a presence and the operations we do on the Basil display.
        private class PresenceInfo {
            private static readonly string _logHeader = "[PresenceInfo]";

            public ScenePresence presence;
            private RaguContext _context;
            private SpaceServerActors _spaceServer;
            private BasilType.ObjectIdentifier _objectId;
            private BasilType.InstanceIdentifier _instanceId;

            public PresenceInfo(ScenePresence pPresence, SpaceServerActors pSpaceServer, RaguContext pContext) {
                presence = pPresence;
                _context = pContext;
                _spaceServer = pSpaceServer;
            }
            public void UpdatePosition() {
                if (_instanceId != null) {
                    BasilType.AccessAuthorization auth = null;
                    _spaceServer.Client.UpdateInstancePosition(auth, _instanceId, PackageInstancePosition());
                    // _context.log.DebugFormat("{0} UpdatePosition: p={1}, r={2}",
                    //             _logHeader, presence.AbsolutePosition, presence.Rotation);
                }
            }
            public void UpdateAppearance() {
                if (_instanceId != null) {
                }
            }
            public void AddAppearanceInstance() {
                BasilType.AccessAuthorization auth = _spaceServer.CreatBasilAccessAuth(_spaceServer.ClientAuth.Token);

                BasilType.AaBoundingBox aabb = null;
                Task.Run( async () => {
                    BasilType.AssetInformation asset = new BasilType.AssetInformation() {
                        DisplayInfo = new BasilType.DisplayableInfo() {
                            DisplayableType = "meshset",
                        }
                    };
                    // TODO: use avatar appearance baking code to build GLTF version of avatar
                    // For the moment, use a canned, static mesh
                    string tempAppearanceURL = "http://files.misterblue.com/BasilTest/gltf/Duck/glTF/Duck.gltf";
                    asset.DisplayInfo.Asset.Add("url", tempAppearanceURL);
                    asset.DisplayInfo.Asset.Add("loaderType", "GLTF");

                    var resp = await _spaceServer.Client.IdentifyDisplayableObjectAsync(auth, asset, aabb);
                    if (resp.Exception == null) {
                        _objectId = resp.ObjectId;

                        var resp2 = await _spaceServer.Client.CreateObjectInstanceAsync(auth, _objectId, PackageInstancePosition());
                        if (resp2.Exception == null) {
                            _instanceId = resp2.InstanceId;
                        }
                        else {
                            _context.log.ErrorFormat("{0} Error creating presence instance: {1}",
                                            _logHeader, resp2.Exception.Reason);
                            _instanceId = null;
                        }
                    }
                    else {
                        _context.log.ErrorFormat("{0} Error creating presence displayable: {1}",
                                        _logHeader, resp.Exception.Reason);
                        _objectId = null;
                    }
                });
            }
            public void RemoveAppearanceInstance() {
                BasilType.AccessAuthorization auth = null;
                Task.Run( async () => {
                    if (_instanceId != null) {
                        await _spaceServer.Client.DeleteObjectInstanceAsync(auth, _instanceId);
                        _instanceId = null;
                    }
                    if (_objectId != null) {
                        await _spaceServer.Client.ForgetDisplayableObjectAsync(auth, _objectId);
                        _objectId = null;
                    }
                });
            }
            // Return an InstancePositionInfo with the presence's current position
            public BasilType.InstancePositionInfo PackageInstancePosition() {
                // Convert coordinates from OpenSim Zup to GLTF Yup
                OMV.Vector3 thePos = CoordAxis.ConvertZupToYup(presence.AbsolutePosition);
                OMV.Quaternion theRot = CoordAxis.ConvertZupToYup(presence.Rotation);
                BasilType.InstancePositionInfo pos = new BasilType.InstancePositionInfo() {
                    Pos = new BasilType.CoordPosition() {
                        PosRef = BasilType.CoordSystem.Wgs86,
                        Pos = new BasilType.Vector3() {
                            X = thePos.X,
                            Y = thePos.Y,
                            Z = thePos.Z
                        },
                        RotRef = BasilType.RotationSystem.Worldr,
                        Rot = new BasilType.Quaternion() {
                            X = theRot.X,
                            Y = theRot.Y,
                            Z = theRot.Z,
                            W = theRot.W
                        }
                    }
                };
                return pos;
            }
        }
    }
}
