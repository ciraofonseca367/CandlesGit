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
    public delegate void RawSocketInfo(string identification, string message);

    public delegate void NewBookView(BookView view);

    public class BinanceBookWebSocket : IDisposable
    {
        private ClientWebSocket _socket;
        private int _timeout;

        private string _asset;

        private int _depthSize;

        private string _binanceUri;

        private bool _active;

        private Thread _threadRunner;

        public BinanceBookWebSocket(string binanceUri, int timeout, string asset, int depthSize)
        {
            _timeout = timeout;
            _asset = asset.ToLower();
            _binanceUri = binanceUri;
            _depthSize = depthSize;

            _active = true;

            _threadRunner = new Thread(new ThreadStart(this.SocketRunner));
        }

        ~BinanceBookWebSocket()
        {
            this.Close(true);
        }

        private RawSocketInfo _socketInfo;
        public void OnNewInfo(RawSocketInfo socketEvent)
        {
            _socketInfo += socketEvent;
        }

        private NewBookView _bookView;
        public void OnNewBookView(NewBookView bookViewEvent)
        {
            _bookView += bookViewEvent;
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
            return $"{_binanceUri}/{_asset}@depth{_depthSize}@100ms";
        }

        public void ReOpen()
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
                    throw new ApplicationException("It was no possible to open the socket");
                else
                {
                    Console.WriteLine("Connect info: "+taskConnect.Status);
                }
            }
        }        

        public void Open()
        {
            ReOpen();

            if (_socketInfo != null)
                _socketInfo(_asset, "Connected: " + this.SocketStatus);

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

                        if (_socketInfo != null)
                            _socketInfo(_asset, "Socket is null, I will try to create another! - " + this.SocketStatus);
                    }

                    var buffer = this.ReconnetableReceive();

                    if (_socketInfo != null)
                        _socketInfo(_asset, buffer);

                    BookView view = null;
                    try
                    {
                        view = TryParse(buffer);

                        if(_bookView != null)
                            _bookView(view);
                    }
                    catch(Exception err)
                    {
                        Console.WriteLine(err.Message);
                        TraceAndLog.StaticLog("Socket Parse Error", err.Message);
                    }

                }
                catch (Exception err)
                {
                    string msg = "Error in the socketRunner trying again in 5 seconds - " + err.ToString() + " - " + this.SocketStatus;

                    this._socket = null;
                    if(_socketInfo != null)
                        _socketInfo(_asset, msg);
                    
                    TraceAndLog.StaticLog("Socket", msg);
                    Thread.Sleep(20000);
                }
            }
        }    

        private BookView TryParse(string buffer)
        {
            buffer = buffer.Replace(_asset+": ","");

            dynamic stuff = JsonConvert.DeserializeObject(buffer);

            var view = new BookView();
            view.LastUpdateId = Convert.ToString(stuff.lastUpdateId);
            foreach(var rawBid in stuff.bids)
            {
                view.Bids.Add(new BookOffer()
                {
                    OfferType = OfferType.Bid,
                    Price = rawBid[0],
                    Qty = rawBid[1]
                });
            }

            foreach(var rawAsk in stuff.asks)
            {
                view.Asks.Add(new BookOffer()
                {
                    OfferType = OfferType.Ask,
                    Price = rawAsk[0],
                    Qty = rawAsk[1]
                });
            }
            
            return view;
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