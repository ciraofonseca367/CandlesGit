using System;
using System.Collections.Generic;
using System.Linq;

namespace Midas.Core.Trade
{
    public class PurchaseStepManager
    {
        private List<PurchaseStep> _steps;

        public PurchaseStepManager()
        {
            _steps = new List<PurchaseStep>();
        }  

        public PurchaseStepManager(double fund, dynamic config): this()
        {
            double unitValue = Convert.ToDouble(Math.Round(fund/10,6));
            double fundFloat = Convert.ToDouble(fund);

            int i=1;
            double totalUnits=0;
            PurchaseStep step = null;
            int countSteps = config.Count;
            foreach(var rawStep in config)
            {
                step = new PurchaseStep()
                {
                    Units = Convert.ToInt32(rawStep.Units),
                    GainTrigger = Convert.ToDouble(rawStep.GainTrigger),
                    Number = i
                };

                if(i == countSteps)
                    step.TotalUnits = Convert.ToDouble(Math.Round(fundFloat - totalUnits,6));
                else
                {
                    var totalStepUnits = unitValue * step.Units;
                    totalUnits += totalStepUnits;

                    step.TotalUnits = Convert.ToDouble(Math.Round(totalStepUnits,6));
                }

                _steps.Add(step);
                i++;
            }

            var grouped = _steps
            .GroupBy(s => s.GainTrigger)
            .Where(g => g.Count() > 1);
            
            if(grouped.Count() > 0)
                throw new ArgumentException("Purchase steps configuration error - More then one step with the same gain step value");
        }

        public PurchaseStep GetFirstStep()
        {
            var p = _steps.First();
            if(p.Used)
                p = null;

            return p;
        }

        public List<PurchaseStep> GetSteps(double gain)
        {
            var stepsToUse = _steps.Where(s => gain >= s.GainTrigger && !s.Used).ToList();

            return stepsToUse;
        }

    }

    public class PurchaseStep
    {
        public int Units
        {
            get;set;
        }

        public double GainTrigger
        {
            get;set;
        }

        public bool Used
        {
            get;
            private set;
        }

        public void SetUsed()
        {
            Used = true;
        }

        public void SetUnused()
        {
            Used = false;
        }

        public int Number
        {
            get;
            set;
        }
        public double TotalUnits { get; internal set; }
    }
}