using System;
using System.Threading;
using Midas.Core;
using Midas.Core.Services;


namespace Midas.InVestor
{
    class Program
    {
        private static InvestorService _investor;
        static void Main(string[] args)
        {
            ThreadPool.SetMaxThreads(100, 100);
            ThreadPool.SetMinThreads(6, 6);

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
