using System;
using System.Threading;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;
using Midas.Sources;

namespace BinanceTests
{
    public class Program
    {
        static void Main(string[] args)
        {
            string streamUrl = "wss://stream.binance.com:9443/ws";

            var sockBTCBUSD = new BinanceWebSocket(streamUrl, 100000, "BTCBUSD", "15m");
            var sockBNBBUSD = new BinanceWebSocket(streamUrl, 100000, "BNBBUSD", "15m");

            var btcStream = sockBTCBUSD.OpenAndSubscribe();
            var bnbStream = sockBNBBUSD.OpenAndSubscribe();

            btcStream.OnNewInfo(new Midas.FeedStream.SocketInfo(socketInfo));
            bnbStream.OnNewInfo(new Midas.FeedStream.SocketInfo(socketInfo));

            Console.WriteLine("Ouvindo...");
            Console.Read();

            btcStream.Dispose();
            bnbStream.Dispose();
        }

        private static void socketInfo(string identification, string info)
        {
            Console.WriteLine(identification+": "+info);
        }

        private static void Chat()
        {
        }
    }


}
