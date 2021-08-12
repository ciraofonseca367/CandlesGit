using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using Midas.Core;
using Midas.Core.Common;
using Midas.FeedStream;

namespace Midas.Sources
{
    public class LiveAssetSource : AssetSource
    {
        public override LiveAssetFeedStream GetFeedStream(
            string asset,
            DateRange range,
            CandleType type
        )
        {
            LiveAssetFeedStream feedStream;

            if(RunParameters.GetInstance().ScoreThreshold < -1)
                feedStream = new TestLiveAssetFeedStream(
                    null,
                    asset,
                    CandleType.MIN5,
                    type
                );
            else
            {
                string streamUrl = "wss://stream.binance.com:9443/ws";
                var binanceSock = new BinanceWebSocket(streamUrl, 10000, asset, "5m");

                feedStream = binanceSock.OpenAndSubscribe();
            }

            return feedStream;
        }

        // private BinanceClWebSocket OpenSocket(string asset)
        // {
        //     string streamUrl = "wss://stream.binance.com:9443/ws/btcusdt@kline_5m";
        //     ClientWebSocket sock = new ClientWebSocket();
        //     Uri serverUri = new Uri(streamUrl);
        //     var taskConnect = sock.ConnectAsync(serverUri, CancellationToken.None);
        //     if(!taskConnect.Wait(10000))
        //         throw new IOException("Connection timeout to: "+streamUrl);
        //     else
        //     {
        //         if(sock.State != WebSocketState.Open)
        //             throw new ApplicationException("It was no possible to open the socket");
        //         else
        //         {
        //             string subscribeCommand = "{";
        //             subscribeCommand += "\"method\": \"SUBSCRIBE\",";
        //             subscribeCommand += "   \"params\": [";
        //             //subscribeCommand += "   \"btcusdt@trade\"";
        //             subscribeCommand += "   \""+asset+"@kline_5m\"";
        //             subscribeCommand += "   ],";
        //             subscribeCommand += "   \"id\": 2";
        //             subscribeCommand += "}";

        //             ArraySegment<byte> bytesToSend = new ArraySegment<byte>(Encoding.UTF8.GetBytes(subscribeCommand));

        //             var send = sock.SendAsync(bytesToSend, WebSocketMessageType.Text, true, CancellationToken.None);
        //             if(!send.Wait(5000))
        //                 throw new IOException("Subscription Timeout");
        //         }
        //     }

        //     return sock;
        // }        
    }

}
