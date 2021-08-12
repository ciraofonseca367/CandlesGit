using System;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Linq;
using System.Collections.Generic;

namespace Midas.Core.Trade
{
    
    public class FundsManager
    {
        private string _dbConString;

        public FundsManager(string dbConString)
        {
            _dbConString = dbConString;
        }

        public Fund GetFunds(string accountName)
        {
            var dbClient = new MongoClient(_dbConString);
            var database = dbClient.GetDatabase("CandlesFaces");

            var dbCol = database.GetCollection<Fund>("Funds");

            var result = dbCol.Find(new BsonDocument() {});

            var res = result.ToList().Where(r => r.Name == accountName);
            if(res.Count() == 0)
                throw new ArgumentException("No funds configured for the name - "+accountName);

            return res.FirstOrDefault();
        }
    }

    public class Fund
    {
        public object _id
        {
            get;
            set;
        }
        
        public string Name
        {
            get;
            set;
        }

        public double Amount
        {
            get;
            set;
        }
    }

}