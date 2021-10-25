using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using Midas.Core.Common;
using Midas.Core.Util;
using Newtonsoft.Json;

namespace Midas.Core.Binance
{
    public abstract class GenericWebSocket : IDisposable
    {
        private ClientWebSocket _socket;
        private int _timeout;

        private string _binanceUri;
        private string _uriParams;
        private bool _active;

        private Thread _threadRunner;

        public GenericWebSocket(string binanceUri, string uriParams, int timeout)
        {
            _timeout = timeout;
            _binanceUri = binanceUri;
            _uriParams = uriParams;
            _active = true;

            _threadRunner = new Thread(new ThreadStart(this.SocketRunner));
        }

        ~GenericWebSocket()
        {
            this.Close(true);
        }

        public WebSocketState State
        {
            get
            {
                return _socket.State;
            }
        }

        private string GetCompleteUri()
        {
            return $"{_binanceUri}/{_uriParams}";
        }

        public virtual void ReOpen()
        {
            _socket = new ClientWebSocket();
            _socket.Options.KeepAliveInterval = new TimeSpan(0,0,60);

            Uri serverUri = new Uri(GetCompleteUri());
            TraceAndLog.StaticLog("Socket","URI: "+serverUri);

            var taskConnect = _socket.ConnectAsync(serverUri, CancellationToken.None);
            if (!taskConnect.Wait(_timeout))
                throw new IOException("Open connection timeout to: " + _binanceUri);
            else
            {
                if(_socket.State != WebSocketState.Open)
                    throw new ApplicationException("It was not possible to open the socket");
                else
                {
                    Console.WriteLine("Connect info: "+taskConnect.Status);
                }
            }
        }        

        public virtual void Open()
        {
            ReOpen();

            _threadRunner.Start();          
        }     

        public void Close()
        {
            var closeRes = _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Just closing...", CancellationToken.None);
            closeRes.Wait(1000);
        }

        public void ReconnetableSend(string data)
        {
            ReconnetableSend(Encoding.UTF8.GetBytes(data));
        }
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

                ReOpen();
            }
            catch(Exception err)
            {
                throw new Exception("Error restabilishing connection", err);
            }
        }

        public void Dispose()
        {
            this.Close(false);
        }        

        public abstract void OnData(string data);

        public abstract void OnInfo(string info);



        protected virtual void SocketRunner()
        {
            TraceAndLog.StaticLog("Socket", "Starting socket runner in 2 seconds...");

            while (_active)
            {
                try
                {
                    if (this._socket == null)
                    {
                        TraceAndLog.StaticLog("Socket","Trying to connect again..");
                        
                        this.ReOpen();

                        OnInfo($"Socket is null, I will try to create another! {this.SocketStatus}");
                    }

                    var buffer = this.ReconnetableReceive();

                    OnData(buffer);


                }
                catch (Exception err)
                {
                    string msg = "Error in the socketRunner trying again in 5 seconds - " + err.ToString() + " - " + this.SocketStatus;

                    this._socket = null;

                    OnInfo(msg);
                    
                    Thread.Sleep(20000);
                }
            }
        }    

        public virtual void Close(bool fromGC = false)
        {
            _active = false;
            _threadRunner.Join(5000);

            if(_socket != null)
                _socket.Dispose();

            if (!fromGC)
                GC.SuppressFinalize(this);
        }         
    }
}