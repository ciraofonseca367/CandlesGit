using System;
using System.Collections.Generic;
using System.Linq;
using Midas.Core.Chart;

namespace Midas.Core.Util
{
    public class MLUtil
    {
        public static Dictionary<DateTime, List<FeatureCoordinate>> MergePeriodsFeatures(
            Dictionary<DateTime, List<FeatureCoordinate>> featureMap1,
            Dictionary<DateTime, List<FeatureCoordinate>> featureMap2
        )
        {
            var keys1 = featureMap1.Keys.ToList();
            var keys2 = featureMap2.Keys.ToList();

            var combinedKeys = keys1.Union(keys2);

            var res = new Dictionary<DateTime, List<FeatureCoordinate>>();

            foreach(var key in combinedKeys)
            {
                List<FeatureCoordinate> list1 = null;
                List<FeatureCoordinate> list2 = null;
                featureMap1.TryGetValue(key, out list1);
                featureMap2.TryGetValue(key, out list2);

                List<FeatureCoordinate> combined = new List<FeatureCoordinate>();
                if(list1 != null)
                    combined.AddRange(list1);
                
                if(list2 != null)
                    combined.AddRange(list2);

                res.Add(key, combined);
            }

            return res;
        }

        public static Dictionary<int,List<double>> GetFeaturesByPeriod(Dictionary<DateTime, List<FeatureCoordinate>> featureMap)
        {
            var keys = featureMap.Keys.ToList();
            keys = keys.OrderBy(k => k).ToList();

            var orderedMap = new Dictionary<int,List<double>>();

            int order = 0;
            foreach(var key in keys)
            {
                var features = new List<double>();

                var featuresInCoordinates = featureMap[key];
                featuresInCoordinates.ForEach(f => {
                    features.Add(f.x);
                    features.Add(f.y);
                });

                orderedMap.Add(order,features);

                order++;
            }

            return orderedMap;
        }
    }
}