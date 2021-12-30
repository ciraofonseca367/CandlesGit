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

            Console.WriteLine("Iniciando serviço");

            RunParameters runParams = RunParameters.CreateInstace(args);
            _investor = new InvestorService(runParams);

            Console.WriteLine("RunParameters loaded");

            runParams.WriteToConsole();

            _investor.Start();

           while(_investor.Running)
                Thread.Sleep(1000);
        }
    }


}
