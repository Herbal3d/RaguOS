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

namespace org.herbal3d.Ragu {
    public class RaguAssetService {

        private RaguContext _context;
        private RaguGETStreamHandler _getHandler;
        private string _handlerPath = "/Ragu/Assets";



        public RaguAssetService(RaguContext pContext) {
            _context = pContext;

            BAssetStorage storage = new BAssetStorage(_context.log, _context.parms);
            _getHandler = new RaguGETStreamHandler(_context, _handlerPath, storage);

            MainServer.Instance.AddStreamHandler(_getHandler);
        }

        public void Stop() {
            if (_getHandler != null) {
                MainServer.Instance.RemoveStreamHandler("GET", _handlerPath);
                _getHandler = null;
            }
        }

    }

    public class RaguGETStreamHandler : BaseStreamHandler {
        private readonly string _logHeader = "[RaguGetStreamHandler]";

        private RaguContext _context;
        private BAssetStorage _assetStorage;

        private Dictionary<string, string> MimeCodes = new Dictionary<string, string>() {
            {".jpg", "image/jpeg" },
            {".jpeg", "image/jpeg" },
            {".png", "image/png" },
            {".bmp", "image/bmp" },
            {".gif", "image/gif" },
            {".buf", "application/octet-stream" },
            {".json", "application/json" },
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
            NameValueCollection headers = httpRequest.Headers;
            string authValue = headers.GetOne("Authorization");
            // if (authValue != null) {
                // _context.log.DebugFormat("{0} ProcessRequest: Authorization={1}", _logHeader, authValue);
                // Check authorization

                // The thing to get is in the last field of the URL
                string[] segments = httpRequest.Url.Segments;
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
            // }
            return null;
        }

    }
}
