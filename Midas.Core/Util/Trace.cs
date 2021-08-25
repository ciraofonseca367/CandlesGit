using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using System.Text;

namespace Midas.Core.Util
{
    public class TraceAndLog
    {
        private static TraceAndLog _singleTrace;
        private static object _lockSinc = new object();
        private StreamWriter _traceFile;
        private StreamWriter _logFile;

        private RunParameters _parans;

        public static TraceAndLog GetInstance()
        {
            var parans = RunParameters.GetInstance();

            if (parans != null)
            {
                if (_singleTrace == null)
                {
                    lock (_lockSinc)
                    {
                        if (_singleTrace == null)
                            _singleTrace = new TraceAndLog(parans);
                    }
                }
            }

            return _singleTrace;
        }

        public TraceAndLog(RunParameters parans)
        {
            _parans = parans;
            string filePrefix = String.Format("{0:yyyyMMdd}", DateTime.Now);

            _logFile = new StreamWriter(File.Open(Path.Combine(parans.OutputDirectory, filePrefix + ".log"), FileMode.Append, FileAccess.Write, FileShare.Read));
            _traceFile = new StreamWriter(File.Open(Path.Combine(parans.OutputDirectory, filePrefix + ".trace"), FileMode.Append, FileAccess.Write, FileShare.Read));
        }

        public static void StaticLog(string module, string description)
        {
            var logger = GetInstance();
            if (logger != null)
                logger.Log(module, description);
        }

        public void Log(string module, string description)
        {
            string entry = String.Format("{0:yyyy-MM-dd hh:mm:ss} - {1,30}:{2}", DateTime.Now, module, description);
            Console.WriteLine(entry);
            lock (_logFile)
            {
                _logFile.WriteLine(entry);
                _logFile.Flush();
            }
        }

        public void LogTrace(string module, string title, string description)
        {
            string entry = String.Format("{0:yyyy-MM-dd hh:mm:ss} - {1} - {2}", DateTime.Now, module, title);
            lock (_traceFile)
            {
                _traceFile.WriteLine(entry);
                _traceFile.WriteLine("");
                _traceFile.WriteLine(description);
                _traceFile.Flush();
            }
        }

        public void LogTraceHttpAction(string module, string action, HttpRequestHeaders headers, HttpResponseHeaders respHeaders, string completeUrl, string body)
        {
            StringBuilder allText = new StringBuilder(500);
            allText.AppendLine(String.Empty);
            allText.AppendLine("Request headers:");
            foreach (var entry in headers)
            {
                string[] values = (string[])entry.Value;
                allText.AppendLine(entry.Key + " : " + String.Join(',', values));
            }

            allText.AppendLine(String.Empty);
            allText.AppendLine("Response headers:");
            foreach (var entry in respHeaders)
            {
                string[] values = (string[])entry.Value;
                allText.AppendLine(entry.Key + " : " + String.Join(',', values));
            }

            allText.AppendLine(String.Empty);
            allText.AppendLine(body);

            LogTrace(module, action + " Request to: " + completeUrl, allText.ToString());
        }

        public void Dispose()
        {
            if (_logFile != null)
            {
                _logFile.Flush();
                _logFile.Dispose();
                _traceFile.Dispose();
            }
        }

    }


    public class TraceEntry
    {
        public DateTime Entry { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
    }
}