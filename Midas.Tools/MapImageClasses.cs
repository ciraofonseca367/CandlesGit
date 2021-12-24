using System;
using System.Collections.Generic;
using System.IO;

namespace Midas.Tools
{
    public class LabelMapper
    {
        private string _rootPath;
        public LabelMapper(string rootPath)
        {
            _rootPath = rootPath;
        }

        public Dictionary<string, Dictionary<DateTime,string>> FileMappings()
        {
            DirectoryInfo dirInfo = new DirectoryInfo(_rootPath);
            var maps = new Dictionary<string,Dictionary<DateTime,string>>();

            foreach(var dir in dirInfo.GetDirectories())
            {
                string label = dir.Name;

                foreach(var file in dir.GetFiles("*.gif"))
                {
                    var parts = file.Name.Split(" ");
                    string asset;
                    int indexDate;

                    if(parts.Length == 6)
                    {
                        asset = "BTCUSDT";
                        indexDate = 2;
                    }
                    else
                    {
                        asset = parts[2];
                        indexDate = 3;
                    }

                    Dictionary<DateTime,string> classMappings = null;
                    maps.TryGetValue(asset, out classMappings);
                    if(classMappings == null)
                    {
                        classMappings = new Dictionary<DateTime, string>();
                        maps.Add(asset, classMappings);
                    }

                    var strDateTime = parts[indexDate]+" "+parts[indexDate+1];
                    DateTime imageDateEnd = DateTime.ParseExact(strDateTime,"dd_MMM_yyyy HH_mm",null);
                    DateTime imageDateStart = imageDateEnd.AddMinutes(5*30*-1);

                    classMappings[imageDateEnd]  = label;
                }
            }

            return maps;
        }
    }
}