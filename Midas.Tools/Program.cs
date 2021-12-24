using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Text;
using Midas.Core.Forecast;
using Midas.Trading;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;

namespace Midas.Tools
{
    class Program
    {
        private static string _conString = "mongodb+srv://admin:cI.(00.#ADM@midas.yi35b.mongodb.net/Sentiment?retryWrites=true&w=majority";

        static void Main(string[] args)
        {

            // string action = args[0];

            // switch(action)
            // {
            //     case "Transactions":
            //         DumpTransactions(args[1]);
            //         break;
            // }

            Classify(args[0], args[1]);
        }

        private static void Classify(string folderWithLabels, string rootPath)
        {
            var mapper = new LabelMapper(folderWithLabels);
            var map = mapper.FileMappings();

            var assets = new List<string>();
            assets.Add("BTCUSDT");
            assets.Add("ETHUSDT");


            foreach (var asset in assets)
            {
                var assetMappings = map[asset];

                foreach (var pair in assetMappings)
                {
                    DirectoryInfo dirInfo = new DirectoryInfo(Path.Join(rootPath, pair.Value));
                    dirInfo.Create();

                    DateTime plus15Periods = pair.Key;

                    FileInfo file = new FileInfo(Path.Join(rootPath, asset, plus15Periods.ToString("200 dd_MMM_yyyy HH_mm")) + ".gif");

                    if (file.Exists)
                    {
                        file.CopyTo(Path.Join(dirInfo.FullName, asset+"_"+file.Name), true);
                        Console.Write(".");
                    }
                    else
                    {
                        Console.WriteLine($"\nCouldn't find {file.Name}");
                    }
                }

            }


        }

        private static string TransformImageToBase64(Bitmap image)
        {
            MemoryStream ms = new MemoryStream();
            image.Save(ms, System.Drawing.Imaging.ImageFormat.Gif);
            ms.Flush();
            ms.Position = 0;

            return Convert.ToBase64String(ms.ToArray());
        }

        private static Bitmap FromBase64(string base64Image)
        {
            var imageBytes = Convert.FromBase64String(base64Image);

            MemoryStream ms = new MemoryStream();
            ms.Write(imageBytes, 0, imageBytes.Length);
            ms.Flush();
            ms.Position = 0;

            Bitmap b = new Bitmap(ms);

            return b;
        }

        public static Bitmap ResizeImage(Image image, int width, int height)
        {
            var destRect = new Rectangle(0, 0, width, height);
            var destImage = new Bitmap(width, height);

            destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (var wrapMode = new ImageAttributes())
                {
                    wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
                }
            }

            return destImage;
        }

        private static void EchoImage(string imgPath)
        {
            FileInfo fi = new FileInfo(imgPath);
            string payload;
            using (FileStream f = fi.OpenRead())
            {
                Bitmap img = new Bitmap(f);
                Bitmap newImg = ResizeImage(img, 250, 250);

                newImg.Save("Resized.gif");

                payload = TransformImageToBase64(newImg);

                File.WriteAllText("base64image.txt", payload);
            }

            var httpClient = new HttpClient();

            try
            {
                httpClient.BaseAddress = new Uri("http://10.0.0.188");

                string url = "/predict";
                var res = httpClient.PostAsync(url, new StringContent(payload));
                if (res.Wait(5000))
                {

                    var predictions = res.Result.Content.ReadAsStringAsync();
                    if (predictions.Wait(5000))
                    {
                        Console.WriteLine(predictions.Result);
                    }
                    else
                        Console.WriteLine("Timeout...");
                }
                else
                    Console.WriteLine("Time the fuck out!");

            }
            catch (Exception err)
            {
                Console.WriteLine(err.ToString());
            }

        }

        private static void TestModel(string imgDirectoryPath)
        {
            IForecast forecaster = ForecastFactory.GetForecaster(
                "ThirtyPeriodForecast", "http://10.0.0.188");

            StringBuilder html = new StringBuilder();

            StringBuilder csv = new StringBuilder();

            html.Append("<html>");


            DirectoryInfo dirInfo = new DirectoryInfo(imgDirectoryPath);
            int i = 1;
            var files = dirInfo.GetFiles("*.gif");
            foreach (FileInfo fi in files)
            {
                using (FileStream f = fi.OpenRead())
                {
                    Bitmap img = new Bitmap(f);

                    Console.WriteLine($"{fi.Name}");
                    Console.WriteLine($"File {i} de {files.Length}");

                    var prediction = forecaster.GetPrediction(img, "teste", 100000, DateTime.Now);

                    html.Append($"<div><img src='file:///{fi.FullName}' width=250 height=250 /></br>");
                    html.Append($"<b>Predictions:</b></br>");

                    int good = 0;
                    if (prediction.RankLong > prediction.RankShort)
                        good = 1;

                    csv.Append($"{fi.Name},{good},{prediction.ScoreLong:0.00},{prediction.ScoreZero:0.00},{prediction.ScoreShort:0.00}\n");

                    foreach (var p in prediction.AllPredictions)
                    {
                        html.Append($"{p.Tag} - {p.Score:0.000}</br>");
                    }

                    html.Append("</div>>");
                }

                i++;
            }

            html.Append("</html>");

            File.WriteAllText("result.html", html.ToString());
            File.WriteAllText("result.csv", csv.ToString());

        }

        private static void DumpTransactions(string fileName)
        {
            var client = new MongoClient(_conString);
            var database = client.GetDatabase("CandlesFaces");

            var dbCol = database.GetCollection<TradeOperationDto>("TradeOperations");
            var itens = dbCol.Find(new BsonDocument()).ToList();

            using (var output = new StreamWriter(File.OpenWrite(fileName)))
            {
                output.WriteLine("Id;EntryDate;ExitDate;StopLossMarker;PriceEntryDesired;PriceEntryReal;PriceExitDesired;PriceExitReal;Gain");

                foreach (var trade in itens)
                {
                    output.WriteLine(String.Concat(
                        trade._id.ToString() + ";",
                        trade.EntryDate.ToString("yyyy-MM-dd hh:mm:ss") + ";",
                        trade.ExitDate.ToString("yyyy-MM-dd hh:mm:ss") + ";",
                        trade.StopLossMarker.ToString("0.0000") + ";",
                        trade.PriceExitDesired.ToString("0.0000") + ";",
                        trade.PriceEntryReal.ToString("0.0000") + ";",
                        trade.PriceExitDesired.ToString("0.0000") + ";",
                        trade.PriceExitReal.ToString("0.0000") + ";",
                        trade.Gain.ToString("0.0000")
                    ));
                }

                output.Flush();
            }
        }
    }
}
