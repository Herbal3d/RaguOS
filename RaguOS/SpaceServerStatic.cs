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

using BT = org.herbal3d.basil.protocol.BasilType;
using HT = org.herbal3d.transport;

using org.herbal3d.cs.CommonEntitiesUtil;
using org.herbal3d.Tiles;

using org.herbal3d.Loden;
using org.herbal3d.OSAuth;

using OMV = OpenMetaverse;

namespace org.herbal3d.Ragu {

    public class SpaceServerStatic : HT.SpaceServerBase {

        private static readonly string _logHeader = "[SpaceServerStatic]";
        private readonly RaguContext _rContext;

        public SpaceServerStatic(RaguContext pContext,
                                    CancellationTokenSource pCanceller,
                                    HT.BasilConnection pConnection,
                                    OSAuthToken pOpenSessionAuth) 
                        : base(pCanceller, pConnection, "Static") {
            _rContext = pContext;
            SessionAuth = pOpenSessionAuth;
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
            return pUserToken.Matches(SessionAuth);
        }

        protected override void DoOpenSessionWork(HT.BasilConnection pConnection, HT.BasilComm pClient, BT.Props pParms) {
            Task.Run(() => {
                HandleBasilConnection(pConnection, pClient, pParms);
            });
        }

        // I don't have anything to do for a CloseSession
        protected override void DoCloseSessionWork() {
            _rContext.log.DebugFormat("{0} DoCloseSessionWork: ", _logHeader);
            return;
        }

        // Received an OpenSession from a Basil client.
        // Send the fellow some content.
        private void HandleBasilConnection(HT.BasilConnection pConnection, HT.BasilComm pClient, BT.Props pParms) {
            try {
                // _context.log.DebugFormat("{0} HandleBasilConnection", _logHeader);

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
                    _rContext.log.ErrorFormat("{0} HandleBasilConnection: Failure reading region spec '{1}': {2}",
                                    _logHeader, regionSpecURL, e);
                    // There is nothing more we can do
                    return;
                }
                if (regionSpec == null) {
                    _rContext.log.ErrorFormat("{0} HandleBasilConnection: Could not read regionSpec", _logHeader);
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

                    _rContext.log.DebugFormat("{0} HandleBasilConnection: Created displayable {1} and instance {2}",
                                    _logHeader, displayableId, instanceId);
                });
            }
            catch (Exception e) {
                _rContext.log.ErrorFormat("{0} HandleBasilConnection. Exception connecting Basil to layers: {1}", _logHeader, e);
            }
        }
    }
}
