// Copyright (c) 2021 Robert Adams
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
using org.herbal3d.cs.CommonUtil;

namespace org.herbal3d.Ragu {

    // Information saved while waiting for an OpenConnection.
    public struct WaitingForOpenConnection {
        OSAuthToken clientAuth;
        DateTime whenCreated;
    }

    public abstract class SpaceServerBase {
        private static readonly string _logHeader = "[SpaceServerBase]";

        public string LayerType = "XX";

        protected readonly RaguContext _context;
        protected CancellationTokenSource _cancellerSource;
        protected BTransport _transport;
        protected BProtocol _protocol;
        protected BasilConnection _connection;


        // The connections this SpaceServer is waiting for
        protected List<WaitingForOpenConnection> _waitingForOpenSession;

        public SpaceServerBase(RaguContext pContext, CancellationTokenSource pCanceller, BTransport pTransport) {
            _context = pContext;
            _cancellerSource = pCanceller;
            _transport = pTransport;

            _waitingForOpenSession = new List<WaitingForOpenConnection>();
        }

    }
}
