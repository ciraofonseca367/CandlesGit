using System;
using System.Threading;
using Midas.Core;
using Midas.Core.Services;

namespace Midas.InVestor
{
    public class Program
    {
        private static InvestorService _investor;
        static void Main(string[] args)
        {
            ThreadPool.SetMaxThreads(500, 500);
            ThreadPool.SetMinThreads(50, 50);

            RunParameters runParams = RunParameters.CreateInstace(args);
            _investor = new InvestorService(runParams);

            System.Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", "/Users/cironola/Downloads/candlesfaces-fdbef15d7ab2.json");

            runParams.WriteToConsole();

            _investor.Start();

           while(_investor.Running)
                Thread.Sleep(1000);
        }
    }


}
