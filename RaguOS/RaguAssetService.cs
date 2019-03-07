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

        public RaguAssetService(RaguContext pContext) {
            _context = pContext;

            RaguGETStreamHandler getHandler = new RaguGETStreamHandler(_context);

            MainServer.Instance.AddStreamHandler(getHandler);
        }

    }

    public class RaguGETStreamHandler : BaseStreamHandler {

        private RaguContext _context;
        private BAssetStorage _assetStorage;

        public RaguGETStreamHandler(RaguContext pContext)
                        : base("GET", "/Ragu/Assets" , "RaguGET" , "Ragu asset fetcher") {
            _context = pContext;


        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            byte[] ret = null;
            NameValueCollection headers = httpRequest.Headers;
            string authValue = headers.GetOne("Authorization");
            if (authValue != null) {
                // Check authorization

                // The thing to get is in the last field of the URL
                string[] segments = httpRequest.Url.Segments;
                string filename = segments.Last();
                Stream strm = _assetStorage.GetStream(filename).Result;


                var continuation = _assetStorage.GetStream(filename).ContinueWith(strm => {
                    if (strm.Result != null) {
                        strm.CopyTo(httpResponse.OutputStream);
                    }
                    else {
                        httpResponse.StatusCode = (int)System.Net.HttpStatusCode.NotFound;
                        ret = null;
                    }
                });
                continuation.Wait();
            }
            return ret;

            else {
            }
            // Try to parse the texture ID from the request URL
            NameValueCollection query = HttpUtility.ParseQueryString(httpRequest.Url.Query);
            string textureStr = query.GetOne("texture_id");
            string format = query.GetOne("format");

            //m_log.DebugFormat("[GETTEXTURE]: called {0}", textureStr);

            if (m_assetService == null)
            {
                m_log.Error("[GETTEXTURE]: Cannot fetch texture " + textureStr + " without an asset service");
                httpResponse.StatusCode = (int)System.Net.HttpStatusCode.NotFound;
                return null;
            }

            UUID textureID;
            if (!String.IsNullOrEmpty(textureStr) && UUID.TryParse(textureStr, out textureID))
            {
//                m_log.DebugFormat("[GETTEXTURE]: Received request for texture id {0}", textureID);

                string[] formats;
                if (!string.IsNullOrEmpty(format))
                {
                    formats = new string[1] { format.ToLower() };
                }
                else
                {
                    formats = WebUtil.GetPreferredImageTypes(httpRequest.Headers.Get("Accept"));
                    if (formats.Length == 0)
                        formats = new string[1] { DefaultFormat }; // default

                }
                // OK, we have an array with preferred formats, possibly with only one entry

                httpResponse.StatusCode = (int)System.Net.HttpStatusCode.NotFound;
                foreach (string f in formats)
                {
                    if (FetchTexture(httpRequest, httpResponse, textureID, f))
                        break;
                }
            }
            else
            {
                m_log.Warn("[GETTEXTURE]: Failed to parse a texture_id from GetTexture request: " + httpRequest.Url);
            }

//            m_log.DebugFormat(
//                "[GETTEXTURE]: For texture {0} sending back response {1}, data length {2}",
//                textureID, httpResponse.StatusCode, httpResponse.ContentLength);

            return null;
        }

    }
}
