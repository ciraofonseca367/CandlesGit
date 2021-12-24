using System;
using System.Threading;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;
using Midas.Sources;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Midas.Core.Binance;
using System.Linq;
using Midas.Core.Trade;
using Midas.Core.Common;

namespace BinanceTests
{
    public class Program
    {
        private static BookView _lastView;
        private static string mainUri = "wss://stream.binance.com:9443/ws";
        static void Main(string[] args)
        {
            // var tradeSocket = new TradeStreamWebSocket(mainUri,"BTCUSDT", 10000);

            // tradeSocket.OnNewTrade((TradeStreamItem item) => {
            //     Console.WriteLine(item.ToString());
            // });

            // tradeSocket.Open();

            // Console.WriteLine("Ouvindo...");
            // Console.Read();            

            // tradeSocket.Dispose();

            DownloadCoinFiles();
        }

        private static void BookSocketTest()
        {
            var bookSocket = new BinanceBookWebSocket(mainUri, 10000, "BTCBUSD", 10);

            var sockBTCBUSD = new BinanceWebSocket(mainUri, 100000, "BTCBUSD", Midas.Core.Common.CandleType.MIN5);

            var btcStream = sockBTCBUSD.OpenAndSubscribe();

            bookSocket.Open();

            //bookSocket.OnNewInfo(socketInfo);
            bookSocket.OnNewBookView((bookView) => {
                // Console.WriteLine($"Best Bid: {bookView.Bids.Last().Qty} - {bookView.Bids.Last().Price}");
                // Console.WriteLine($"Best Ask: {bookView.Asks.Last().Qty} - {bookView.Asks.Last().Price}");

                _lastView = bookView;
            });

            btcStream.OnUpdate((info, info2, candle) => {
                Console.WriteLine("PASSEI AQUI!");

                var localView = _lastView;
                var priceBuy = MatchMaker.GetPrice(_lastView, 0.024, OfferType.Ask);
                var priceSell = MatchMaker.GetPrice(_lastView, 0.024, OfferType.Bid);

                var diffBuy = ((priceBuy - candle.CloseValue) / candle.CloseValue) * 100;
                var diffSell = ((priceSell - candle.CloseValue) / candle.CloseValue) * 100;

                Console.WriteLine($"Price ${candle.CloseValue:0.000}  - Candidate: ${priceBuy:0.000} - {diffBuy:0.0000}%");
                Console.WriteLine($"Price ${candle.CloseValue:0.000}  - Candidate: ${priceSell:0.000} - {diffSell:0.0000}%");
            });

            Console.WriteLine("Ouvindo...");
            Console.Read();

            bookSocket.Dispose();
            btcStream.Dispose();            
        }

        private static void socketInfo(string identification, string info)
        {
            Console.WriteLine(identification + ": " + info);
            Console.WriteLine();
        }

        private static void Chat()
        {
        }

        private static void DownloadCoinFiles()
        {
            string remoteUriMask = "https://data.binance.vision/data/spot/monthly/klines/{0}/5m/{0}-5m-{1}-{2}.zip";
            string folderMask = "/Users/cironola/Documents/CandlesFace Projects/Storage/{0}-MIN5/";
            string fileNameMask = "/Users/cironola/Documents/CandlesFace Projects/Storage/{0}-MIN5/{0}-5m-{1}-{2}.zip";

            string[] pairs = new string[] { "ETHUSDT"};

            List<Task> downloads = new List<Task>();

            foreach (var pair in pairs)
            {
                DirectoryInfo dir = new DirectoryInfo(String.Format(folderMask, pair));
                dir.Create();

                Task t = Task.Run(() =>
                {

                    DateTime current = new DateTime(2021, 1, 1);
                    while (current < DateTime.Now)
                    {
                        var remoteUri = String.Format(remoteUriMask, pair, current.Year, current.Month.ToString("00"));
                        var fileName = String.Format(fileNameMask, pair, current.Year, current.Month.ToString("00"));
                        try
                        {
                            WebClient myWebClient = new WebClient();
                            myWebClient.DownloadFile(remoteUri, fileName);
                            Console.WriteLine("Successfully Downloaded File \"{0}\" from \"{1}\"", fileName, remoteUri);
                        }
                        catch (Exception err)
                        {
                            Console.WriteLine(err.Message);
                        }

                        current = current.AddMonths(1);
                    }
                });

                downloads.Add(t);
            }

            Task.WaitAll(downloads.ToArray());
        }
    }


}
