using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.Text;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Net;

using MongoDB.Driver;
using MongoDB.Bson;
using Newtonsoft.Json;

using Midas.Core.Chart;
using Midas.Core.Encoder;
using Midas.Core.Indicators;
using Midas.Util;
using Midas.Trading;
using Midas.Core.Telegram;
using Midas.Core;
using Midas.Core.Common;
using Midas.FeedStream;
using Midas.Sources;

namespace Midas.DataGather
{
    public class DataGather
    {
        private Thread _runner;
        private bool _running;
        private RunParameters _params;

        private MongoClient _mongoClient;

        public DataGather(RunParameters parans)
        {
            _params = parans;
            _runner = new Thread(new ThreadStart(this.Runner));

            _mongoClient = new MongoClient(parans.DbConString);
        }

        public void Start()
        {
            TelegramBot.SendMessage("Iniciando DataGather...");

            _running = true;

            this._runner.Start();
        }

        public void Stop()
        {
            _running = false;
            if (!_runner.Join(1000))
                throw new ApplicationException("Timeout waiting for the runner to stop");
        }

        private void OnNewCandle(string asset, Candle p, Candle c)
        {
            Console.WriteLine($"=== New Candle {asset} - {p.ToString()} ===");
            p.SaveOrUpdate(_params.DbConString,String.Format("Klines_{0}_{1}", asset.ToUpper(), _params.CandleType.ToString()));
        }

        public void Runner()
        {
            var runParams = _params;

            var client = new MongoClient(_params.DbConString);
            var database = client.GetDatabase("Sentiment");

            var lastNewsCheck = DateTime.MinValue;
            var state = LoadLastState();
            
            string streamUrl = "wss://stream.binance.com:9443/ws";
            var sockBTCBUSD = new BinanceWebSocket(streamUrl, 120000, "BTCBUSD", Midas.Core.Common.CandleType.MIN15);
            var sockBNBBUSD = new BinanceWebSocket(streamUrl, 120000, "BNBBUSD", Midas.Core.Common.CandleType.MIN15);
            var sockADABUSD = new BinanceWebSocket(streamUrl, 120000, "ADABUSD", Midas.Core.Common.CandleType.MIN15);
            var sockETHBUSD = new BinanceWebSocket(streamUrl, 120000, "ETHBUSD", Midas.Core.Common.CandleType.MIN15);

            var btcTBtream = sockBTCBUSD.OpenAndSubscribe();
            var bnbStream = sockBNBBUSD.OpenAndSubscribe();
            var adaStream = sockADABUSD.OpenAndSubscribe();
            var ethStream = sockETHBUSD.OpenAndSubscribe();

            btcTBtream.OnNewCandle(new SocketNewCancle(this.OnNewCandle));
            bnbStream.OnNewCandle(new SocketNewCancle(this.OnNewCandle));
            adaStream.OnNewCandle(new SocketNewCancle(this.OnNewCandle));
            ethStream.OnNewCandle(new SocketNewCancle(this.OnNewCandle));

            while (_running)
            {
                try
                {

                    /* Code to read the news every 5 minutes */
                    if ((DateTime.Now - lastNewsCheck).TotalMinutes > 5)
                    {
                        Console.WriteLine("{0:yyyy/MM/dd hh:mm:ss} - Resuming...", DateTime.Now);

                        FetchResult res = null;
                        try
                        {
                            res = FetchCurrentState(state);
                        }
                        catch (Exception err)
                        {
                            Console.WriteLine("Error when fetching results - " + err.Message);
                        }


                        if (res != null)
                        {
                            if (res.State.Count > 0)
                            {
                                try
                                {
                                    var btcState = database.GetCollection<BsonDocument>("BTCLastState");
                                    foreach (var entry in res.State)
                                    {
                                        state.Add(entry.Key, entry.Value);

                                        btcState.InsertOne(new BsonDocument {
                                        { "Title", entry.Key}
                                    });
                                    }

                                    var col = database.GetCollection<BsonDocument>("BTCTail");
                                    col.InsertOne(res.Result);
                                }
                                catch (Exception err)
                                {
                                    Console.WriteLine("Error when updated DB with state info - " + err.Message);
                                }

                            }
                        }

                        Console.WriteLine("{0:yyyy/MM/dd hh:mm:ss} - Waiting...", DateTime.Now);

                        lastNewsCheck = DateTime.Now;
                    }
                }
                catch (Exception err)
                {
                    TelegramBot.SendMessage("Data Gather - Thread Error: " + err.ToString());
                }
            }


            btcTBtream.Dispose();            
            bnbStream.Dispose();            

        }
        public Dictionary<string, string> LoadLastState()
        {
            var client = new MongoClient(_params.DbConString);
            var database = client.GetDatabase("Sentiment");

            var lastState = database.GetCollection<BsonDocument>("BTCLastState");
            var itens = lastState.Find(new BsonDocument()).ToList();
            Dictionary<string, string> ret = new Dictionary<string, string>(121);
            if (itens.Count > 0)
            {
                foreach (var item in itens)
                    ret[item.GetValue(1).ToString()] = "não importa o texto"; //Pega o valor do segundo campo do documento, que é sempre o title
            }

            return ret;
        }


        private FetchResult FetchCurrentState(Dictionary<string, string> lastState)
        {
            //AIzaSyC_nFQ07WunNDNRCY7rLoz-OK092bcfl20
            string url = "https://www.googleapis.com/customsearch/v1?key=AIzaSyC_nFQ07WunNDNRCY7rLoz-OK092bcfl20&cx=eb92c0399a22b5aa7&q={0}&dateRestrict=d1&num=10&start={1}";
            string searchTerm = "bitcoin";

            int totalNumber = 0;
            float totalSentiment = 0;

            var uniqueItems = new Dictionary<string, string>(23);

            var searchResult = new BsonDocument();
            BsonArray bItems = new BsonArray();

            StringBuilder email = new StringBuilder();

            var entries = new List<dynamic>();

            for (int i = 0; i <= 3; i++)
            {
                string newsUrl = string.Format(url, searchTerm, (i * 10 + 1).ToString());

                //Console.WriteLine(newUrl);
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(newsUrl);
                request.Method = "GET";

                WebResponse webResponse = request.GetResponse();
                Stream webStream = webResponse.GetResponseStream();
                StreamReader responseReader = new StreamReader(webStream);
                string response = responseReader.ReadToEnd();
                responseReader.Close();

                dynamic stuff = JsonConvert.DeserializeObject(response);
                foreach (var item in stuff.items)
                {
                    if (item.pagemap != null)
                    {
                        var tags = item.pagemap.metatags;
                        if (tags != null && tags[0] != null)
                        {
                            string articleAuthor = (tags[0]["article:author"] == null ? String.Empty : tags[0]["article:author"]);
                            string articlePublisher = (tags[0]["article:publisher"] == null ? String.Empty : tags[0]["article:publisher"]);
                            string articleTier = (tags[0]["article:content_tier"] == null ? String.Empty : tags[0]["article:content_tier"]);
                            string articleOpinion = (tags[0]["article:opinion"] == null ? String.Empty : tags[0]["article:opinion"]);
                            string articlePublishedDate = (tags[0]["article:published_time"] == null ? String.Empty : tags[0]["article:published_time"]);

                            string description = (tags[0]["og:description"] == null ? String.Empty : tags[0]["og:description"]);
                            string title = (tags[0]["og:title"] == null ? String.Empty : tags[0]["og:title"]);
                            string link = (item["link"] == null ? String.Empty : item["link"]);


                            //Pequena lógica para ser considerado algo relevante
                            //Títulos pequenos são de páginas genéricas, não de uma notífica.
                            if (title.Length > 20 && (articleAuthor != String.Empty || articlePublisher != String.Empty))
                            {
                                if (!lastState.ContainsKey(title))
                                {
                                    Console.WriteLine("TITLE: " + title);
                                    Console.WriteLine("DESCRIPTION: " + description);
                                    Console.WriteLine("Published Date:" + articlePublishedDate);
                                    //Console.WriteLine("Opinion:" + articleOpinion);
                                    //Console.WriteLine("Tier:" + articleTier);
                                    Console.WriteLine("");

                                    entries.Add(new {
                                        Title = title,
                                        Link = link,
                                        Description = String.IsNullOrEmpty(description) ? "" : description.Substring(0, Math.Min(description.Length-1, 200)),
                                        PublishedDate = articlePublishedDate,
                                        DateHit = DateTime.Now
                                    });

                                    var bDocItem = new BsonDocument() {
                                        { "Title",  title },
                                        { "Description",  description },
                                        { "PublishedDate",  articlePublishedDate },
                                        { "IsOpinion",  articleOpinion },
                                        { "ArticleTier",  articleTier },
                                    };
                                    bItems.Add(bDocItem);

                                    Console.WriteLine("");

                                    totalNumber++;

                                    uniqueItems[title] = title;
                                }
                            }

                        }
                        else
                        {
                            Console.WriteLine("ODD ITEM:" + item.title);
                            Console.WriteLine("");
                        }
                    }
                    else
                    {
                        Console.WriteLine("ODD ITEM:" + item.title);
                        Console.WriteLine("");
                    }
                }
            }

            float sentimentAverage = 0;
            if (totalNumber > 0)
                sentimentAverage = totalSentiment / totalNumber;

            string emailSubject = String.Concat("BTC Upt - Hits:", totalNumber);

            searchResult.Add("SentimentAverage", -1);
            searchResult.Add("Created", DateTime.Now);
            searchResult.Add("Hits", totalNumber);
            searchResult.Add("Items", bItems);

            var res = new FetchResult();
            res.Result = searchResult;
            res.State = uniqueItems;

            if (totalNumber > 0)
            {
                try
                {

                    foreach(var item in entries)
                    {
                        TelegramBot.SendMessage(item.Link);

                        Thread.Sleep(100);
                    }

                    
                }
                catch (Exception e)
                {
                    Console.WriteLine("Oppppss, erro ao enviar e-mail: " + e.Message);
                }
            }

            return res;
        }
    }
    public class FetchResult
    {
        public BsonDocument Result;
        public Dictionary<string, string> State;
    }

    class Program
    {
        private static DataGather _gather;
        static void Main(string[] args)
        {
            RunParameters runParams = RunParameters.CreateInstace(args);
            _gather = new DataGather(runParams);

            System.Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", "/Users/cironola/Downloads/candlesfaces-fdbef15d7ab2.json");

            runParams.WriteToConsole();

            _gather.Start();

            while (true)
            {
                string line = Console.ReadLine();
                if (line == "Exit")
                {
                    Console.WriteLine("Stopping...");
                    _gather.Stop();
                    Console.WriteLine("Stopped!");
                }
            }
        }
    }
}

