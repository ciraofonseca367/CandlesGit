using System;
using System.Collections.Generic;
using Midas.Core.Common;
using System.Linq;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using Newtonsoft.Json;
using System.Text;
using System.Collections.Concurrent;
using Midas.Sources;
using Midas.Core.Util;

namespace Midas.FeedStream
{
    public delegate void SocketNewCancle(string identification,Candle previous, Candle current);

    public delegate void SocketEvent(string identification,string message, Candle c);

    public delegate void SocketInfo(string identification, string message);
    

    public abstract class LiveAssetFeedStream : AssetFeedStream, IDisposable
    {
        protected BinanceWebSocket _socket;
        protected string _asset;

        protected CandleType _streamCandleType;
        protected CandleType _queryCandleType;

        private Thread _threadRunner;

        private bool _closing;

        protected LiveAssetFeedStream(string asset, CandleType streamCandleType, CandleType queryCandleType)
        {
            _asset = asset;
            _streamCandleType = streamCandleType;
            _queryCandleType = queryCandleType;

            _closing = false;

            _threadRunner = new Thread(new ThreadStart(this.SocketRunner));
            _threadRunner.Start();

            _state = MidasSocketState.Initial;            
        }

        public LiveAssetFeedStream(BinanceWebSocket socket, string asset, CandleType streamCandleType, CandleType queryCandleType)
            : this(asset, streamCandleType, queryCandleType)
        {
            _socket = socket;
        }

        ~LiveAssetFeedStream()
        {
            this.Close(true);
        }

        protected SocketEvent _socketUpdate;        
        protected SocketNewCancle _socketNew;

        protected SocketInfo _socketInfo;

        public void OnUpdate(SocketEvent socketEvent)
        {
            _socketUpdate += socketEvent;
        }
        public void OnNewCandle(SocketNewCancle socketEvent)
        {
            _socketNew += socketEvent;
        }
        public void OnNewInfo(SocketInfo socketEvent)
        {
            _socketInfo += socketEvent;
        }
        public abstract Candle ParseCandle(string buffer);

        public abstract void OpenSocket(string asset);

        private int _retryAttempts = 0;
        protected MidasSocketState _state;

        public MidasSocketState State
        {
            get
            {
                return _state;
            }
        }

        protected virtual void SocketRunner()
        {
            Candle bufferCandle = null;
            Thread.Sleep(10000);

            TraceAndLog.StaticLog("Socket", "Starting socket runner in 2 seconds...");

            Candle lastCandle = null;
            BinanceWebSocket _backupSocket = null;
            bool _starting=true;

            while (!_closing)
            {
                try
                {
                    if(_starting) //We Open and Subscribe to the socket only inside the loop cause the first open sometimes generates a timeout
                    {
                        _starting = false;

                        this._socket.ReOpenAndSubscribe();

                        if (_socketInfo != null)
                            _socketInfo(_asset, "Connected: " + this._socket.SocketStatus);
                    }

                    if (this._socket == null)
                    {
                        _retryAttempts++;

                        this._socket = _backupSocket;
                        this._socket.ReOpenAndSubscribe();

                        if (_socketInfo != null)
                            _socketInfo(_asset, "Socket is null, I will try to create another! - " + this._socket.SocketStatus);
                    }

                    var buffer = this._socket.ReconnetableReceive();

                    if (_socketInfo != null)
                        _socketInfo(_asset, buffer);

                    if (buffer.IndexOf("result") == -1) //Ignore Sometimes when the connection starts binance will return a JSON with result:null
                    {
                        _state = MidasSocketState.Connected;
                        _retryAttempts = 0; //If we are succesfull reset de attempts

                        var tmpCandle = ParseCandle(buffer);
                        if (bufferCandle != null && tmpCandle.OpenTime == bufferCandle.OpenTime)
                            bufferCandle.Replace(tmpCandle);
                        else
                            bufferCandle = tmpCandle;

                        if(_socketUpdate != null)
                            _socketUpdate(_asset, buffer, bufferCandle);

                        //We've just changed candle, thus, we need to add the lastCandle to the internal buffer
                        if (lastCandle == null || bufferCandle.OpenTime > lastCandle.OpenTime)
                        {
                            if(lastCandle == null)
                                lastCandle = bufferCandle;

                            if(_socketNew != null)
                                _socketNew(_asset, lastCandle, bufferCandle);
                        }

                        lastCandle = bufferCandle;
                    }
                }
                catch (Exception err)
                {
                    if(this._socket != null)
                        _backupSocket = this._socket.Clone();

                    string msg = "Error in the socketRunner trying again in 5 seconds - " + err.ToString() + " - " + this._socket.SocketStatus;

                    this._socket = null;
                    if(_socketInfo != null)
                        _socketInfo(_asset, msg);
                    
                    TraceAndLog.StaticLog("Socket", msg);
                    Thread.Sleep(5000);
                }
            }

            _state = MidasSocketState.Closed;
        }

        public virtual void Close(bool fromGC = false)
        {
            _closing = true;

            if(_socket != null)
                _socket.Dispose();

            _threadRunner.Join(5000);

            if (!fromGC)
                GC.SuppressFinalize(this);
        }

        public override void Dispose()
        {
            this.Close(false);
        }
    }

    public class BinanceLiveAssetFeedStream : LiveAssetFeedStream
    {
        /*
{"e":"kline","E":1624298099441,"s":"BTCUSDT","k":{"t":1624297800000,"T":1624298099999,"s":"BTCUSDT","i":"5m","f":922694338,"L":922699903,"o":"32208.66000000","c":"32200.45000000","h":"32285.42000000","l":"32162.66000000","v":"334.90471600","n":5566,"x":false,"q":"10784920.21051327","V":"161.18287900","Q":"5190896.91093688","B":"0"}}
{"e":"kline","E":1624298100120,"s":"BTCUSDT","k":{"t":1624297800000,"T":1624298099999,"s":"BTCUSDT","i":"5m","f":922694338,"L":922699921,"o":"32208.66000000","c":"32206.08000000","h":"32285.42000000","l":"32162.66000000","v":"335.17413900","n":5584,"x":true,"q":"10793596.96728088","V":"161.43417800","Q":"5198989.96471057","B":"0"}}
{"e":"kline","E":1624298102124,"s":"BTCUSDT","k":{"t":1624298100000,"T":1624298399999,"s":"BTCUSDT","i":"5m","f":922699922,"L":922699938,"o":"32205.92000000","c":"32205.91000000","h":"32205.92000000","l":"32205.90000000","v":"0.33833200","n":17,"x":false,"q":"10896.29085731","V":"0.10812800","Q":"3482.36163400","B":"0"}}
{"e":"kline","E":1624298104422,"s":"BTCUSDT","k":{"t":1624298100000,"T":1624298399999,"s":"BTCUSDT","i":"5m","f":922699922,"L":922699962,"o":"32205.92000000","c":"32205.57000000","h":"32205.92000000","l":"32205.57000000","v":"0.57109400","n":41,"x":false,"q":"18392.55645818","V":"0.16241300","Q":"5230.64858287","B":"0"}}
{"e":"kline","E":1624298106566,"s":"BTCUSDT","k":{"t":1624298100000,"T":1624298399999,"s":"BTCUSDT","i":"5m","f":922699922,"L":922700001,"o":"32205.92000000","c":"32194.75000000","h":"32205.92000000","l":"32194.75000000","v":"5.37038500","n":80,"x":false,"q":"172945.52393429","V":"0.29433600","Q":"9479.14643905","B":"0"}}
{"e":"kline","E":1624298108633,"s":"BTCUSDT","k":{"t":1624298100000,"T":1624298399999,"s":"BTCUSDT","i":"5m","f":922699922,"L":922700033,"o":"32205.92000000","c":"32192.24000000","h":"32205.92000000","l":"32190.29000000","v":"5.64832200","n":112,"x":false,"q":"181893.53007654","V":"0.37899500","Q":"12204.74807714","B":"0"}}
{"e":"kline","E":1624298110689,"s":"BTCUSDT","k":{"t":1624298100000,"T":1624298399999,"s":"BTCUSDT","i":"5m","f":922699922,"L":922700053,"o":"32205.92000000","c":"32190.99000000","h":"32205.92000000","l":"32187.46000000","v":"6.14685700","n":132,"x":false,"q":"197940.88791168","V":"0.60325400","Q":"19423.24876400","B":"0"}}
{"e":"kline","E":1624298112807,"s":"BTCUSDT","k":{"t":1624298100000,"T":1624298399999,"s":"BTCUSDT","i":"5m","f":922699922,"L":922700070,"o":"32205.92000000","c":"32188.00000000","h":"32205.92000000","l":"32187.46000000","v":"6.77562300","n":149,"x":false,"q":"218179.65716946","V":"0.98238500","Q":"31626.72497462","B":"0"}}
        */
        public BinanceLiveAssetFeedStream(BinanceWebSocket socket, string asset, CandleType streamCandleType, CandleType queryCandleType) : base(socket, asset, streamCandleType, queryCandleType)
        {
        }

        public override int BufferCount()
        {
            throw new NotImplementedException();
        }

        public override void OpenSocket(string asset)
        {
            throw new NotImplementedException();
        }

        public override Candle ParseCandle(string buffer)
        {
            dynamic stuff = JsonConvert.DeserializeObject(buffer);
            Candle c = new Candle()
            {
                PointInTime_Open = Candle.FromTimeStamp(Convert.ToInt64(stuff.k.t)),
                PointInTime_Close = Candle.FromTimeStamp(Convert.ToInt64(stuff.k.T)),
                LowestValue = Convert.ToDouble(stuff.k.l),
                HighestValue = Convert.ToDouble(stuff.k.h),
                OpenValue = Convert.ToDouble(stuff.k.o),
                CloseValue = Convert.ToDouble(stuff.k.c),
                Volume = Convert.ToDouble(stuff.k.V)
            };

            return c;
        }

        public override Candle Peek()
        {
            throw new NotImplementedException();
        }

        public override Candle[] Read(int periods)
        {
            throw new NotImplementedException();
        }
    }

    public enum MidasSocketState
    {
        Initial,
        Connected,

        Closed
    }
}