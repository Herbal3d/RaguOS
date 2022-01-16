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
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using org.herbal3d.OSAuth;
using org.herbal3d.transport;
using org.herbal3d.b.protocol;
using org.herbal3d.Tiles;

using org.herbal3d.Loden;

namespace org.herbal3d.Ragu {

    // Processor of incoming messages when we're waiting for the OpenSession.
    class ProcessStaticOpenConnection : IncomingMessageProcessor {
        SpaceServerStatic _ssContext;
        public ProcessStaticOpenConnection(SpaceServerStatic pContext) : base(pContext) {
            _ssContext = pContext;
        }
        public override void Process(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            if (pMsg.Op == (uint)BMessageOps.OpenSessionReq) {
                _ssContext.ProcessOpenSessionReq(pMsg, pConnection, pProtocol);
            }
            else {
                BMessage resp = BasilConnection.MakeResponse(pMsg);
                resp.Exception = "Session is not open. Static";
                pProtocol.Send(resp);
            }
        }
    }
    class ProcessStaticIncomingMessages : IncomingMessageProcessor {
        SpaceServerStatic _ssContext;
        public ProcessStaticIncomingMessages(SpaceServerStatic pContext) : base(pContext) {
            _ssContext = pContext;
        }
        public override void Process(BMessage pMsg, BasilConnection pConnection, BProtocol pProtocol) {
            if (pMsg.Op == (uint)BMessageOps.OpenSessionReq) {
                _ssContext.ProcessOpenSessionReq(pMsg, pConnection, pProtocol);
            }
            else {
                BMessage resp = BasilConnection.MakeResponse(pMsg);
                resp.Exception = "Session is not open. Static";
                pProtocol.Send(resp);
            }
        }
    }

    public class SpaceServerStatic : SpaceServerBase {

        private static readonly string _logHeader = "[SpaceServerStatic]";

        public static readonly string StaticLayerType = "Static";

        private RaguContext _rContext;

        public SpaceServerStatic(RaguContext pContext, CancellationTokenSource pCanceller, BTransport pTransport) 
                        : base(pContext, pCanceller, pTransport) {
            _rContext = pContext;
            LayerType = StaticLayerType;

            // The protocol for the initial OpenSession is always JSON
            _protocol = new BProtocolJSON(null, _transport);

            // Expect BMessages and set up messsage processor to handle initial OpenSession
            _connection = new BasilConnection(_protocol, _context.log);
            _connection.SetOpProcessor(new ProcessStaticOpenConnection(this));
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
                        pConnection.SetOpProcessor(new ProcessStaticIncomingMessages(this));

                        BMessage resp = BasilConnection.MakeResponse(pMsg);
                        resp.IProps.Add("ServerVersion", _context.ServerVersion);
                        resp.IProps.Add("ServerAuth", incomingAuth.Token);
                        pConnection.Send(resp);

                        // Send the static region information to the user
                        Task.Run(async () => {
                            await StartConnection(pConnection, loginAuth);
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

        // Received an OpenSession from a Basil client.
        // Send the fellow some content.
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private async Task StartConnection(BasilConnection pConnection, OSAuthToken pUserAuth) {
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
            try {

                /* TODO:
                // Get region tile definition
                LodenRegion lodenRegion = _rContext.scene.RequestModuleInterface<LodenRegion>();
                string regionSpecURL = RaguAssetService.Instance.CreateAccessURL(lodenRegion.RegionTopLevelSpecURL);

                // Get the top level description of the region
                TileSet regionSpec;
                try {
                    using (WebClient wClient = new WebClient()) {
                        wClient.UseDefaultCredentials = false;
                        wClient.Headers.Add("Authorization", RaguAssetService.Instance.AccessToken.Token);
                        string specString = wClient.DownloadString(regionSpecURL);
                        regionSpec = TileSet.FromString(specString);
                    }
                }
                catch (Exception e) {
                    _rContext.log.Error("{0} HandleBasilConnection: Failure reading region spec '{1}': {2}",
                                    _logHeader, regionSpecURL, e);
                    // There is nothing more we can do
                    return;
                }
                if (regionSpec == null) {
                    _rContext.log.Error("{0} HandleBasilConnection: Could not read regionSpec", _logHeader);
                    return;
                }

                // Gather all the URIs for the region's contents
                List<string> regionURIs = new List<string>();
                if (regionSpec.root.content.uris != null) {
                    regionURIs.AddRange(regionSpec.root.content.uris.Select(uri => {
                        return RaguAssetService.Instance.CreateAccessURL(uri);
                    }));
                };
                if (!string.IsNullOrEmpty(regionSpec.root.content.uri)) {
                    regionURIs.Add(RaguAssetService.Instance.CreateAccessURL(regionSpec.root.content.uri));
                };

                // Tell the Basil server to load all of the region's contents
                regionURIs.ForEach(async uri => {
                    BT.Props props = new BT.Props();
                    BT.AbilityList abilities = new BT.AbilityList {
                        new BT.AbilityDisplayable() {
                            DisplayableUrl = uri,
                            AssetServiceType = "RAGU",
                            DisplayableAuth = RaguAssetService.Instance.AccessToken.Token,
                            DisplayableType = "meshset",
                            LoaderType = "GLTF"
                        }
                    };
                    BT.Props resp = await Client.CreateItemAsync(props, abilities);
                    BT.ItemId displayableId = new BT.ItemId(resp["ItemId"]);

                    props = new BT.Props();
                    abilities = new BT.AbilityList {
                        new BT.AbilityInstance() {
                            DisplayableItemId = displayableId,
                            Pos = new double[] { 0, 0, 0 }
                        }
                    };
                    resp = await Client.CreateItemAsync(props, abilities);
                    BT.ItemId instanceId = new BT.ItemId(resp["ItemId"]);

                    // _rContext.log.DebugFormat("{0} HandleBasilConnection: Created displayable {1} and instance {2}",
                    //                 _logHeader, displayableId, instanceId);
                });
                */
            }
            catch (Exception e) {
                _rContext.log.Error("{0} HandleBasilConnection. Exception connecting Basil to layers: {1}", _logHeader, e);
            }
        }
    }
}
