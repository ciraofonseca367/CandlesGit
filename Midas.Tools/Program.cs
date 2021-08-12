using System;
using System.IO;
using Midas.Trading;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Midas.Tools
{
    class Program
    {
        private static string _conString = "mongodb+srv://admin:cI.(00.#ADM@midas.yi35b.mongodb.net/Sentiment?retryWrites=true&w=majority";

        static void Main(string[] args)
        {
            
            string action = args[0];

            switch(action)
            {
                case "Transactions":
                    DumpTransactions(args[1]);
                    break;
            }
        }

        private static void DumpTransactions(string fileName)
        {
            var client = new MongoClient(_conString);
            var database = client.GetDatabase("CandlesFaces");

            var dbCol = database.GetCollection<TradeOperationDto>("TradeOperations");
        var itens = dbCol.Find(new BsonDocument()).ToList();

            using(var output = new StreamWriter(File.OpenWrite(fileName)))
            {
                output.WriteLine("Id;EntryDate;ExitDate;StopLossMarker;PriceEntryDesired;PriceEntryReal;PriceExitDesired;PriceExitReal;Gain");

                foreach(var trade in itens)
                {
                    output.WriteLine(String.Concat(
                        trade._id.ToString()+";",
                        trade.EntryDate.ToString("yyyy-MM-dd hh:mm:ss")+";",
                        trade.ExitDate.ToString("yyyy-MM-dd hh:mm:ss")+";",
                        trade.StopLossMarker.ToString("0.0000")+";",
                        trade.PriceExitDesired.ToString("0.0000")+";",
                        trade.PriceEntryReal.ToString("0.0000")+";",
                        trade.PriceExitDesired.ToString("0.0000")+";",
                        trade.PriceExitReal.ToString("0.0000")+";",
                        trade.Gain.ToString("0.0000")
                    ));
                }

                output.Flush();
            }
        }
    }
}
