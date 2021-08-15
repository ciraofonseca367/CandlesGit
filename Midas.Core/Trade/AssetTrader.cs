using Midas.Core;
using Midas.Sources;
using Midas.FeedStream;
using Midas.Core.Common;
using System.Threading;

namespace Midas.Core.Trade
{
    public class AssetTrader
    {
        private static string WEB_SOCKETURI = "wss://stream.binance.com:9443";
        private string _asset;
        private CandleType _candleType;
        private LiveAssetFeedStream _stream;
        private RunParameters _params;
        private int _timeOut;
        private Thread _runner;

        private bool _stopping;

        public AssetTrader(string asset, CandleType candleType, RunParameters @params, int timeOut)
        {
            _asset = asset;
            _candleType = candleType;
            _params = @params;
            _timeOut = timeOut;

            //_runner = new Thread(new ThreadStart(this.Runner));

            _stopping = false;

            //TODO: Codigo do CandleUpdate e NewCandle conforme já anotado
            //TODO: Alterar o código do stream para fazer o pedido de abertura inicial, já que muitas vezes da timeout

        }

        public void Start()
        {
            //Start the thread
            _stream = GetLiveStream();

            _stream.OnNewCandle(new SocketNewCancle(this.OnNewCandle));
            _stream.OnUpdate(new SocketEvent(this.OnCandleUpdate));
        }

        public void Stop()
        {

        }

        //Vamos precisar de uma Thread?
        // private void Runner()
        // {
        //     while(!_stopping)
        //     {

        //     }
        // }

        private void OnNewCandle(string id, Candle previous, Candle current)
        {

        }

        private void OnCandleUpdate(string id, string message, Candle cc)
        {

        }



        private LiveAssetFeedStream GetLiveStream()
        {
            BinanceWebSocket sock = new BinanceWebSocket(
                WEB_SOCKETURI, _timeOut, _asset, CandleTypeConverter.Convert(_candleType)
            );

            return sock.OpenAndSubscribe();
        }
    }
}