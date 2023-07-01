// Copyright (c) 2023 Robert Adams
//
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using OpenSim.Framework;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;

using org.herbal3d.cs.CommonUtil;

namespace org.herbal3d.transport {

    // Parameters used to listen for WebSocket connections
    public class BTransportOSWSParams : BTransportParams {
        // Usually run insecure with an nginx proxy in the front to eliminate all the SSL complexity
        public bool isSecure = false;
        // String added to the URL to get to the WebSocket server
        public string URLaddition = "";
        public string externalURLTemplate = "ws://{0}:{1}/{2}";
        public bool disableNaglesAlgorithm = false;
        public readonly string defaultProtocolPrefix = "ws:";
        public readonly string secureProtocolPrefix = "wss:";

        public BTransportOSWSParams(): base() {
            transport = BTransportOSWS.ID;
            protocol = "Basil-JSON";
            port = 11440;
            URLaddition = "/Ragu/";
        }
    }

    // A version of BTransport that uses the OpenSimulator's HTTP server to
    //    create WebSocket connections.
    public class BTransportOSWS : BTransport {

        private static readonly string _logHeader = "[BTransportOSWS]";

        // Named used to identify transport
        public static string ID = "OSWS";

        private readonly CancellationToken _overallCancellation;

        private WebSocketHttpServerHandler _handler;

        private Task _inputQueueTask;
        private Task _outputQueueTask;

        /**
         * Transport for receiving and sending via WebSockets.
         * Receives a text or binary blob and passes it up the a BProtocol for translation.
         */
        public BTransportOSWS(
                            WebSocketHttpServerHandler pHandler,
                            CancellationToken pCanceller,
                            BLogger pLogger): base(BTransportOSWS.ID, pLogger) {

            _handler = pHandler;
            _overallCancellation = pCanceller;
            ConnectionName = pHandler.Path + ":" + pHandler.GetRemoteIPEndpoint().ToString();

            SubscribeToEvents();

            if (_overallCancellation == null) {
                throw new Exception("BTransportWS.constructor: OverallCancellation parameter null");
            }
            _log.Debug("{0} Connection created {1}", _logHeader, ConnectionName); // DEBUG DEBUG
        }

        public override void Start() {
            base.Start();
            StartInputAndOutputQueueTasks();
        }

        public override void Close() {
            base.OnClosed();
            if (_handler != null) {
                _handler.Close("BTransportOSWS.Close");
                _handler = null;
            }
        }

        private void StartInputAndOutputQueueTasks() {
            // Tasks to push and pull from the input and output queues.
            BTransport hostingTransport = this;
            _inputQueueTask = Task.Run(() => {
                while (!_overallCancellation.IsCancellationRequested) {
                    byte[] msg = _receiveQueue.Take();
                    try {
                        if (_receptionCallback != null) {
                            // _log.Debug("{0} sending message to processor", _logHeader); // DEBUG DEBUG
                            // _log.Debug("{0}     xportT={1}, contextT={2}", _logHeader, hostingTransport.TransportType, _receptionCallbackContext.GetType().FullName); // DEBUG DEBUG
                            _receptionCallback(hostingTransport, msg, _receptionCallbackContext);
                        }
                        else {
                            _log.Debug("{0} message received with no processor", _logHeader);   // DEBUG DEBUG
                        }
                    }
                    catch (Exception ee) {
                        _log.Debug("{0} inputQueue: Exception: {1}", _logHeader, ee);
                    }
                }
            }, _overallCancellation);
            _outputQueueTask = Task.Run(() => {
                while (!_overallCancellation.IsCancellationRequested) {
                    byte[] msg = _sendQueue.Take();
                    _handler.SendData(msg);
                }
            }, _overallCancellation);
        }
        public void SubscribeToEvents()
        {
            if (_handler == null) {
                throw new Exception("BTransportWS.SubscribeToEvents: Handler parameter null");
            }
            _handler.OnClose += Connection_OnClose;
            _handler.OnText += Connection_OnMessage;
            _handler.OnUpgradeCompleted += Connection_OnOpen;
            _handler.OnData += Connection_OnBinary;
            _handler.OnPong += Connection_OnPong;
        }
        public void UnSubscribeToEvents()
        {
            _handler.OnClose -= Connection_OnClose;
            _handler.OnText -= Connection_OnMessage;
            _handler.OnUpgradeCompleted -= Connection_OnOpen;
            _handler.OnData -= Connection_OnBinary;
            _handler.OnPong -= Connection_OnPong;
        }

        // A WebSocket connection has been made.
        // Initialized the message processors.
        private void Connection_OnOpen(object pSender, UpgradeCompletedEventArgs pEventArgs) {
            _log.Debug("{0} Connection_OnOpen: connection state to OPEN", _logHeader);
            base.OnOpened();
        }

        // The WebSocket connection is closed. Any application state is out-of-luck
        private void Connection_OnClose(object pSender, CloseEventArgs pCloseEventArgs) {
            // _log.Debug("{0} Connection_OnClose: connection state to CLOSED", _logHeader);
            base.OnClosed();
        }

        private void Connection_OnMessage(object sender, WebsocketTextEventArgs pTextEventArgs) {
            if (IsConnected()) {
                // _log.Debug("{0} Connection_OnMessage: cn={1}", _logHeader, ConnectionName); // DEBUG DEBUG
                _receiveQueue.Add(Encoding.ASCII.GetBytes(pTextEventArgs.Data));
            }
        }

        private void Connection_OnBinary(object sender, WebsocketDataEventArgs pDataEventArgs) {
            if (IsConnected()) {
                // _log.Debug("{0} Connection_OnBinary: cn={1}", _logHeader, ConnectionName);  // DEBUG DEBUG
                _receiveQueue.Add(pDataEventArgs.Data);
            }
        }

        private void Connection_OnError(Exception pExcept) {
            base.OnErrored();
            _log.Error("{0} OnError event on {1}: {2}", _logHeader, ConnectionName, pExcept);
        }

        private void Connection_OnPong(object sender, PongEventArgs pPongEventArgs) {
            _log.Debug("{0} Connection_OnPong: cn={1}", _logHeader, ConnectionName);  // DEBUG DEBUG
        }
    }

    // Since we can't create a WebSocket processor until we have the socket, this
    //    creates a listener that will create a processor for each connection.
    public class BTransportOSWSConnectionListener {

        public static string _logHeader = "[BTransportWSConnectionListener]";

        private BLogger _logger;

        public BTransportOSWSConnectionListener(
                        BTransportOSWSParams param,
                        BTransportConnectionAcceptedProcessor connectionProcessor,
                        CancellationTokenSource cancellerSource,
                        BLogger logger
                        ) {

            BTransportOSWSParams _params = param;
            BTransportConnectionAcceptedProcessor _connectionProcessor = connectionProcessor;
            _logger = logger;

            string handlerPath = _params.URLaddition ?? "/Ragu/SS";

            logger.Debug("{0} Creating Websocket handler for path '{1}'", _logHeader, handlerPath);

            // Add a listener for a WebSocket connection
            MainServer.Instance.AddWebSocketHandler(handlerPath, (path, handler) => {
                _logger.Debug("{0} Received WebSocket connection for path '{1}'", _logHeader, path);
                handler.SetChunksize(8192);
                handler.NoDelay_TCP_Nagle = _params.disableNaglesAlgorithm;

                // Once we have the connection, create a BTransport to handle it
                var transporter = new BTransportOSWS(handler, cancellerSource.Token, logger);
                _connectionProcessor(transporter, cancellerSource);

                handler.HandshakeAndUpgrade();
            });
        }

    }
}

