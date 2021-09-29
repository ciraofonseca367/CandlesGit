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

namespace BinanceTests
{
    public class Program
    {
        static void Main(string[] args)
        {
            // string streamUrl = "wss://stream.binance.com:9443/ws";

            // var sockBTCBUSD = new BinanceWebSocket(streamUrl, 100000, "BTCBUSD", Midas.Core.Common.CandleType.MIN15);
            // var sockBNBBUSD = new BinanceWebSocket(streamUrl, 100000, "BNBBUSD", Midas.Core.Common.CandleType.MIN15);

            // var btcStream = sockBTCBUSD.OpenAndSubscribe();
            // var bnbStream = sockBNBBUSD.OpenAndSubscribe();

            // btcStream.OnNewInfo(new Midas.FeedStream.SocketInfo(socketInfo));
            // bnbStream.OnNewInfo(new Midas.FeedStream.SocketInfo(socketInfo));

            // Console.WriteLine("Ouvindo...");
            // Console.Read();

            // btcStream.Dispose();
            // bnbStream.Dispose();

            DownloadCoinFiles();
        }

        private static void socketInfo(string identification, string info)
        {
            Console.WriteLine(identification + ": " + info);
        }

        private static void Chat()
        {
        }

        private static void DownloadCoinFiles()
        {
            string remoteUriMask = "https://data.binance.vision/data/spot/monthly/klines/{0}/15m/{0}-15m-{1}-{2}.zip";
            string folderMask = "/Users/cironola/Documents/CandlesFace Projects/Storage/{0}-MIN15/";
            string fileNameMask = "/Users/cironola/Documents/CandlesFace Projects/Storage/{0}-MIN15/{0}-15m-{1}-{2}.zip";

            string[] pairs = new string[] { "DOTUSDT", "XRPUSDT", "ADAUSDT"};

            List<Task> downloads = new List<Task>();

            foreach (var pair in pairs)
            {
                DirectoryInfo dir = new DirectoryInfo(String.Format(folderMask, pair));
                dir.Create();

                Task t = Task.Run(() =>
                {

                    DateTime current = new DateTime(2018, 1, 1);
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
