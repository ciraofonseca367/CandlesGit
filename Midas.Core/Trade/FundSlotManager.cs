using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Midas.Core.Trade
{
    public class FundSlotManager
    {
        private double _totalFund;

        private List<FundSlot> _slots;

        public FundSlotManager(double totalFund, int numberOfSlots)
        {
            _totalFund = totalFund;

            _slots = new List<FundSlot>(numberOfSlots);

            var slotAmount = totalFund / Convert.ToDouble(numberOfSlots);

            for (int i = 0; i < numberOfSlots; i++)
            {
                _slots.Add(new FundSlot()
                {
                    Id = i,
                    SlotAmount = slotAmount,
                    InUse = false,
                    LastReturned = DateTime.MinValue,
                    LastTaken = DateTime.MinValue
                });
            }

            Dump();
        }

        public int GetNumberOfSlotsAvailable()
        {
            return _slots.Count(s => s.InUse == false);
        }

        public FundSlot TryGetSlot()
        {
            FundSlot fundSlot;
            lock (_slots)
            {
                fundSlot = _slots.FirstOrDefault(s => s.InUse == false);
                if (fundSlot != null)
                {
                    Console.WriteLine($"Peguei o SLOT: {fundSlot.Id}");
                    fundSlot.InUse = true;
                    fundSlot.LastTaken = DateTime.Now;
                }
            }

            return fundSlot;
        }

        public void ReturnSlot(int slotId)
        {
            Console.WriteLine("Returning slot (OUTSIDE): "+slotId);
            lock (_slots)
            {
                var slot = _slots[slotId];
                if (slot.InUse)
                {
                    Console.WriteLine("Returning slot: "+slotId);
                    slot.InUse = false;
                    slot.LastReturned = DateTime.Now;
                }
                else
                    Console.WriteLine("Trying to return a slot that is not in use...");
            }
        }

        internal void Dump()
        {
            _slots.ForEach(s => {
                Console.WriteLine(s.ToString());
            });
        }

        public string DumpState(double assetPrice)
        {
            StringBuilder sb = new StringBuilder();
            _slots.ForEach(s => {
                sb.Append(s.ToString());
                sb.AppendLine();
            });

            return sb.ToString();

        }
    }

    public class FundSlot
    {
        public int Id
        {
            get;
            set;
        }

        public double SlotAmount
        {
            get;
            set;
        }

        public string Identifier
        {
            get;
            set;
        }

        public bool InUse
        {
            get;
            set;
        }

        public DateTime LastTaken
        {
            get;
            set;
        }

        public DateTime LastReturned
        {
            get;
            set;
        }

        public override string ToString()
        {
            return $"{Identifier} - {Id} - {SlotAmount:0.00000} - {(InUse ? "Em uso" : "Livre")} - {LastTaken:HH:mm:ss} - {LastReturned:HH:mm:ss}";
        }
    }
}