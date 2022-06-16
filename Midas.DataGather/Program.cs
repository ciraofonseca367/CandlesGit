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
using System.Net.Http;

namespace Midas.DataGather
{
    public class DataGather
    {
        private Task _runner;
        private bool _running;
        private RunParameters _params;

        private MongoClient _mongoClient;

        public DataGather(RunParameters parans)
        {
            _params = parans;

            _mongoClient = new MongoClient(parans.DbConString);
        }

        public async Task Start()
        {
            await TelegramBot.SendMessage("Iniciando DataGather...");

            _running = true;

            _runner = Task.Run(this.Runner);
        }

        public void Stop()
        {
            _running = false;
            if (!_runner.Wait(1000))
                throw new ApplicationException("Timeout waiting for the runner to stop");
        }

        public async Task Runner()
        {
            var runParams = _params;

            var client = new MongoClient(_params.DbConString);
            var database = client.GetDatabase("Sentiment");

            var lastNewsCheck = DateTime.MinValue;
            var state = LoadLastState();

            while (_running)
            {
                try
                {
                    /* Code to read the news every 5 minutes */
                    if ((DateTime.Now - lastNewsCheck).TotalMinutes > 120)
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

                        Console.WriteLine("{0:yyyy/MM/dd hh:mm:ss} - Waiting 120 minutes", DateTime.Now);

                        lastNewsCheck = DateTime.Now;
                    }

                    Thread.Sleep(5000);
                }
                catch (Exception err)
                {
                    await TelegramBot.SendMessage("Data Gather - Thread Error: " + err.ToString());
                }
            }
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
            string url = "https://www.googleapis.com/customsearch/v1?key={0}&cx=eb92c0399a22b5aa7&q={1}&dateRestrict=d1&num=10&start={2}";
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
                string newsUrl = string.Format(url, _params.CustomSearchKey, searchTerm, (i * 10 + 1).ToString());
                Console.WriteLine("Fetching from: "+newsUrl);

                HttpClient client = new HttpClient();

                string response = "";
                var responseTask = client.GetStringAsync(newsUrl);
                if(responseTask.Wait(10000))
                    response = responseTask.Result;
                else
                    throw new TimeoutException("Timeout while getting news");


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

                                    entries.Add(new
                                    {
                                        Title = title,
                                        Link = link,
                                        Description = String.IsNullOrEmpty(description) ? "" : description.Substring(0, Math.Min(description.Length - 1, 200)),
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
                    foreach (var item in entries)
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
        static async Task Main(string[] args)
        {
            RunParameters runParams = RunParameters.CreateInstace(args);
            _gather = new DataGather(runParams);

            System.Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", "/Users/cironola/Downloads/candlesfaces-fdbef15d7ab2.json");

            runParams.WriteToConsole();

            await _gather.Start();

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

