// Copyright 2019 Robert Adams
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
//     Unless required by applicable law or agreed to in writing, software
//     distributed under the License is distributed on an "AS IS" BASIS,
//     WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//     See the License for the specific language governing permissions and
//     limitations under the License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web;

using OpenSim.Framework;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;

using org.herbal3d.cs.CommonEntities;
using org.herbal3d.OSAuth;

using BasilType = org.herbal3d.basil.protocol.BasilType;

namespace org.herbal3d.Ragu {
    public class RaguAssetService {

        private readonly string _logHeader = "[RaguAssetService]";

        // TODO: Someday make this into a separate service.


        public string HandlerPath;
        public string AssetServiceURL;

        // Name used to identify this service in the authorization system
        // There is one asset server for this simulator. Make unique in case there are
        //     multiple simulators.
        public string ServiceName = "Assets-" + Guid.NewGuid().ToString();
        public readonly OSAuthToken AccessToken;

        private readonly RaguContext _context;
        private RaguGETStreamHandler _getHandler;

        // There is only one asset service per sinulator
        public static RaguAssetService Instance;
        private static Object InstanceLock = new object();

        // Create the single instance.
        // The lock is because there could be several regions starting up at the same time.
        public static void CreateInstance(RaguContext pContext) {
            lock (RaguAssetService.InstanceLock) {
                if (RaguAssetService.Instance == null) {
                    RaguAssetService.Instance = new RaguAssetService(pContext);
                }
            }
        }

        public RaguAssetService(RaguContext pContext) {
            _context = pContext;
            HandlerPath = "/Ragu/Assets";

            string hostAddress = RaguRegion.HostnameForExternalAccess;
            if (MainServer.Instance.UseSSL) {
                AssetServiceURL = new UriBuilder("https", hostAddress, (int)MainServer.Instance.Port, HandlerPath).Uri.ToString();
            }
            else {
                AssetServiceURL = new UriBuilder("http", hostAddress, (int)MainServer.Instance.Port, HandlerPath).Uri.ToString();
            }

            // If there is not already an asset server, start one.
            // TODO: someday move this asset server into its own shared region module.
            var handlerKeys = MainServer.Instance.GetHTTPHandlerKeys();
            string thisHandler = "GET:" + HandlerPath;
            if (!handlerKeys.Contains(thisHandler)) {
                _context.log.DebugFormat("{0} Creating GET handler for path '{1}' at '{2}",
                                    _logHeader, HandlerPath, AssetServiceURL);
                BAssetStorage storage = new BAssetStorage(_context.log, _context.parms);
                _getHandler = new RaguGETStreamHandler(_context, HandlerPath, storage);

                MainServer.Instance.AddStreamHandler(_getHandler);
            }
            else {
                _context.log.DebugFormat("{0} GET handler already exists. Not creating.", _logHeader);
            }

            // People need to supply a key to access us
            OSAuthModule authModule = pContext.scene.RequestModuleInterface<OSAuthModule>();
            if (authModule != null) {
                _context.log.DebugFormat("{0} Created authToken for service 'Assets'", _logHeader);
                AccessToken = new OSAuthToken() {
                    Srv = "RaguAssetService",
                    Sid = new Guid().ToString()
                };
                _getHandler.AccessToken = AccessToken;
            }
            else {
                _context.log.ErrorFormat("{0} No auth module available. Could not create authToken for service 'Assets'", _logHeader);
            }
        }

        public void Stop() {
            if (_getHandler != null) {
                MainServer.Instance.RemoveStreamHandler("GET", HandlerPath);
                _getHandler = null;
            }
        }

        // Given an asset identifying string, create an BasilType.AssetInformation
        //    structure that would allow external access to the asset.
        public BasilType.AssetInformation CreateAssetInformation(string pUri) {
            var assetInfo = new BasilType.AssetInformation() {
                DisplayInfo = new BasilType.DisplayableInfo()
            };

            // For the moment, assume everything is a meshset
            assetInfo.DisplayInfo.DisplayableType = "meshset";

            string regionAssetURL = CreateAccessURL(pUri);

            assetInfo.DisplayInfo.Asset.Add("url", regionAssetURL);
            assetInfo.DisplayInfo.Asset.Add("loaderType", "GLTF");
            assetInfo.DisplayInfo.Asset.Add("auth", _getHandler.AccessToken.Token);

            return assetInfo;
        }

        // Create an URL for accessing the passed uri
        public string CreateAccessURL(string uri) {
            string assetURL = AssetServiceURL + "/" + uri;
            assetURL = assetURL.Replace("/./", "/");
            return assetURL;
        }
    }

    public class RaguGETStreamHandler : BaseStreamHandler {
        private readonly string _logHeader = "[RaguGetStreamHandler]";

        private readonly RaguContext _context;
        private readonly BAssetStorage _assetStorage;

        public OSAuthToken AccessToken;

        private readonly Dictionary<string, string> MimeCodes = new Dictionary<string, string>() {
            {".jpg", "image/jpeg" },
            {".jpeg", "image/jpeg" },
            {".png", "image/png" },
            {".bmp", "image/bmp" },
            {".gif", "image/gif" },
            {".buf", "application/octet-stream" },
            {".json", "application/json" },
            {".meta", "application/json" },
            {".txt", "application/text" },
            {".gltf", "model/gltf+json" },
            {".glb", "model/gltf-binary" }
        };

        public RaguGETStreamHandler(RaguContext pContext, string pPath, BAssetStorage pStorage)
                        : base("GET", pPath, "RaguGET" , "Ragu asset fetcher") {
            _context = pContext;
            _assetStorage = pStorage;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            string[] segments = httpRequest.Url.Segments;

            bool authorized = false;
            if (!_context.parms.P<bool>("ShouldEnforceAssetAccessAuthorization")) {
                // Check for 'Authorization' in the request header
                NameValueCollection headers = httpRequest.Headers;
                string authValue = headers.GetOne("Authorization");
                if (authValue != null) {
                    // _context.log.DebugFormat("{0} Checking Authorization header", _logHeader);
                    if (AccessToken != null && authValue == AccessToken.Token) {
                        // _context.log.DebugFormat("{0} Matched Authorization header. Auth={1}", _logHeader, authValue);
                        authorized = true;
                    }
                    // else {
                    //     _context.log.DebugFormat("{0} Failed Authorization header. Auth={1}", _logHeader, authValue);
                    // }
                }
                else {
                    // if no 'Authorization', see if an access token was embedded in the URL.
                    // Tokens will be for the form ".../bearer-token/..." where "bearer-" is the
                    //     flag for the token, and "token" is the actual token string.
                    foreach (string segment in segments.Reverse()) {
                        if (segment.StartsWith("bearer-")) {
                            authValue = segment.Substring(7);
                            if (authValue.EndsWith("/")) {
                                authValue = authValue.Substring(0, authValue.Length - 1);
                            }
                            if (authValue == AccessToken.Token) {
                                authorized = true;
                            }
                            break;
                        }
                    }
                    if (!authorized) {
                        _context.log.DebugFormat("{0} Failed bearer- URL field. Auth={1}, url={2}",
                                    _logHeader, authValue, httpRequest.Url.ToString());
                    }
                }
            }
            else {
                authorized = true;
            }

            if (authorized) {
                // The thing to get is in the last field of the URL
                string filename = segments.Last();
                string extension = System.IO.Path.GetExtension(filename);
                string mimeType = "application/text";
                if (MimeCodes.ContainsKey(extension)) {
                    mimeType = MimeCodes[extension];
                }

                byte[] asset = _assetStorage.Fetch(filename).Result;
                int assetLength = asset.Length;
                if (asset.Length > 0) {
                    httpResponse.StatusCode = (int)System.Net.HttpStatusCode.OK;
                    httpResponse.ContentLength = assetLength;
                    httpResponse.ContentType = mimeType;
                    httpResponse.Body.Write(asset, 0, assetLength);
                }
                else {
                    httpResponse.StatusCode = (int)System.Net.HttpStatusCode.NotFound;
                }
                // Cross-Origin Resource Sharing with simple requests
                httpResponse.AddHeader("Access-Control-Allow-Origin", "*");
            }
            else {
                httpResponse.StatusCode = (int)System.Net.HttpStatusCode.Unauthorized;
            }

            return null;
        }

    }
}
