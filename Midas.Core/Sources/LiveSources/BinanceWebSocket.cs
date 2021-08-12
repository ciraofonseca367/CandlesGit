using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Midas.Core.Common;
using Midas.Core.Util;
using Midas.FeedStream;

namespace Midas.Sources
{
    public class BinanceWebSocket : IDisposable
    {
        private ClientWebSocket _socket;
        private int _timeout;

        private string _asset;

        private string _candleType;

        private string _binanceUri;

        public BinanceWebSocket(string binanceUri, int timeout, string asset, string candleType)
        {
            _socket = new ClientWebSocket();
            _socket.Options.KeepAliveInterval = new TimeSpan(0,0,60);
            _timeout = timeout;
            _asset = asset.ToLower();
            _candleType = candleType;
            _binanceUri = binanceUri;

            _lastPong = DateTime.Now;
        }

        public BinanceWebSocket Clone()
        {
            return new BinanceWebSocket(
                _binanceUri,
                _timeout,
                _asset,
                _candleType);
        }

        public WebSocketState State
        {
            get
            {
                return _socket.State;
            }
        }

        public void OpenAndSubscribe()
        {
            Uri serverUri = new Uri(_binanceUri+"/"+_asset+"@kline_"+_candleType);
            TraceAndLog.StaticLog("Socket","URI: "+serverUri);

            var taskConnect = _socket.ConnectAsync(serverUri, CancellationToken.None);
            if (!taskConnect.Wait(_timeout))
                throw new IOException("Open connection timeout to: " + _binanceUri);
            else
            {
                if(_socket.State != WebSocketState.Open)
                    throw new ApplicationException("It was no possible to open the socket");
                else
                {
                    Console.WriteLine("Connect info: "+taskConnect.Status);
                    SubscribeToAsset();
                }
            }
        }

        public void Close()
        {
            string unsubscribeCommand = "{";
            unsubscribeCommand += "\"method\": \"UNSUBSCRIBE\",";
            unsubscribeCommand += "   \"params\": [";
            unsubscribeCommand += "   \"" + _asset + "@kline_"+_candleType+"\"";
            unsubscribeCommand += "   ],";
            unsubscribeCommand += "   \"id\": 2";
            unsubscribeCommand += "}";

            var send = _socket.SendAsync(Encoding.UTF8.GetBytes(unsubscribeCommand), WebSocketMessageType.Text, true, CancellationToken.None);
            send.Wait(1000);

            var closeRes = _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Just closing...", CancellationToken.None);
            closeRes.Wait(1000);
        }

        public void ReconnetableSend(string data)
        {
            ReconnetableSend(Encoding.UTF8.GetBytes(data));
        }

        private DateTime _lastPong;
        public void ReconnetableSend(byte[] data, bool retry = true)
        {
            var res = _socket.SendAsync(
                new ArraySegment<byte>(data),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None
            );

            if (!res.Wait(_timeout))
            {
                if (retry)
                {
                    RestabilishConnection();
                    ReconnetableSend(data, false);
                }
                else
                    throw new IOException("Send timeout AFTER retring");
            }
        }

        private void TrySendPong()
        {
            var diff = (DateTime.Now - _lastPong);
            if(diff.TotalMinutes > 10)
            {
                _lastPong = DateTime.Now;

                try
                {
                    var res = _socket.SendAsync(
                        new ArraySegment<byte>(Encoding.UTF8.GetBytes("{ \"method\": \"ping\" }")),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None
                    );
                    res.Wait(500);
                }
                catch{}
            }
        }

        private DateTime _lastRenew = DateTime.Now;
        private void RenewConnectionIfNeeded()
        {
            var diff = (DateTime.Now - _lastRenew);
            if(diff.TotalMinutes > 60)
            {
                _lastRenew = DateTime.Now;

                RestabilishConnection();
            }
        }

        public string ReconnetableReceive(bool retry = true)
        {
            //RenewConnectionIfNeeded();

            string result = null;
            ArraySegment<byte> bytesReceived = new ArraySegment<byte>(new byte[1024]);
            var taskReceive = this._socket.ReceiveAsync(bytesReceived, CancellationToken.None);
            if (!taskReceive.Wait(_timeout))
            {
                if (retry)
                {
                    RestabilishConnection();
                    result = ReconnetableReceive(false);
                }
                else
                    throw new IOException("Receive timeout AFTER retring");
            }
            else
            {
                result = Encoding.UTF8.GetString(bytesReceived.Array, 0, bytesReceived.Count);
            }

            return result;
        }


        private void SubscribeToAsset()
        {
            string subscribeCommand = "{";
            subscribeCommand += "\"method\": \"SUBSCRIBE\",";
            subscribeCommand += "   \"params\": [";
            //subscribeCommand += "   \"btcusdt@trade\"";
            subscribeCommand += "   \"" + _asset + "@kline_" + _candleType + "\"";
            subscribeCommand += "   ],";
            subscribeCommand += "   \"id\": 2";
            subscribeCommand += "}";

            ArraySegment<byte> bytesToSend = new ArraySegment<byte>(Encoding.UTF8.GetBytes(subscribeCommand));

            var send = _socket.SendAsync(bytesToSend, WebSocketMessageType.Text, true, CancellationToken.None);
            if (!send.Wait(_timeout))
                throw new IOException("Subscription Timeout");
        }

        public string SocketStatus
        {
            get
            {
                string info = String.Empty;
                if(_socket != null)
                {
                    if(_socket.CloseStatus != null)
                        info = String.Format("{0} - {1} - {2}", _socket.State, _socket.CloseStatus.ToString(), _socket.CloseStatusDescription.ToString());
                    else
                        info = String.Format("{0}", _socket.State);
                }
            
                return info;
            }
        }

        private void RestabilishConnection()
        {
            try
            {
                Thread.Sleep(300);
                try
                {
                    var closeRes = _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Just closing...", CancellationToken.None);
                    closeRes.Wait(2000);
                }
                catch(Exception closeErr)
                {
                    Console.WriteLine("Error closing socket, on restabilish - "+closeErr.Message);
                }

                Thread.Sleep(500);

                _socket  = new ClientWebSocket();

                OpenAndSubscribe();
            }
            catch(Exception err)
            {
                throw new Exception("Error restabilishing connection", err);
            }
        }

        public void Dispose()
        {
            this.Close();
        }
    }
}
