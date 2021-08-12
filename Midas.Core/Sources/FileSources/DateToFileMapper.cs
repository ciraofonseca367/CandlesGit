using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Midas.Core.Common;
using Newtonsoft.Json;
using System.IO;

namespace Midas.Sources
{
    public class DateToFileMapper
    {
        private List<DateToFileMap> _mappings;
        private CandleType _groupType;

        public DateToFileMapper(string config)
        {
            _mappings = new List<DateToFileMap>();

            dynamic stuff = JsonConvert.DeserializeObject(config);
            if (stuff.GroupLevel == null)
                throw new ArgumentException("Bad format, were expecting 'GroupLevel' field in config");

            object parsed = null;
            Enum.TryParse(typeof(CandleType), stuff.GroupLevel.ToString(), true, out parsed);
            if (parsed == null)
                throw new ArgumentException("Invalid group type - " + stuff.GroupType);
            else
            {
                _groupType = (CandleType)parsed;
                foreach (var map in stuff.FileMapping)
                {
                    _mappings.Add(new DateToFileMap()
                    {
                        FilePath = map.File,
                        Range = new DateRange(
                            Convert.ToDateTime(map.DateStart),
                            Convert.ToDateTime(map.DateEnd)
                        ),
                        Mask = Convert.ToString(map.Mask)
                    });
                }
            }

            processMaskedEntries();
        }

        private void processMaskedEntries()
        {
            var masked = _mappings.Where(m => !String.IsNullOrEmpty(m.Mask)).ToList();

            var expandedMappings = new Dictionary<string, DateToFileMap>();
            foreach (var mask in masked)
            {
                var span = mask.Range.GetSpan();

                DateTime current = mask.Range.Start;
                for (int i = 0; i < Math.Round(span.TotalDays) - 1; i++)
                {
                    string fileName = current.ToString(mask.Mask);
                    if (!expandedMappings.ContainsKey(fileName))
                    {
                        var newMap = new DateToFileMap();
                        newMap.Range = new DateRange(
                            new DateTime(current.Year, current.Month, current.Day),
                            new DateTime(current.Year, current.Month, current.Day, 23, 59, 59)
                        );
                        newMap.FilePath = Path.Combine(mask.FilePath, fileName);
                        newMap.Mask = null;

                        expandedMappings.Add(fileName, newMap);
                    }

                    current = current.AddDays(1);
                }

                _mappings.Remove(mask);
            }            

            _mappings.AddRange(expandedMappings.Values.ToList());
        }

        public CandleType MinGroup
        {
            get
            {
                return _groupType;
            }
        }

        public string[] GetFiles(DateRange range)
        {
            var overlaps = _mappings
            .Where(m => m.Range.IsOverlap(range))
            .OrderBy(m => m.Range.Start).ToList();

            var files = overlaps.Select(o => o.FilePath).ToArray();

            return files;
        }
    }

    public class DateToFileMap
    {
        public string FilePath
        {
            get;
            set;
        }

        public DateRange Range
        {
            get;
            set;
        }

        public string Mask
        {
            get;
            set;
        }
    }
}