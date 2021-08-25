using System;
using System.Collections.Generic;

namespace Midas.Util
{
    public class FixedSizedQueue<T>
    {
        readonly Queue<T> queue = new Queue<T>();

        public int Size { get; private set; }

        public FixedSizedQueue(int size)
        {
            Size = size;
        }

        public T[] GetList() {
            return queue.ToArray();
        }

        public void Enqueue(T obj)
        {
            queue.Enqueue(obj);

            while (queue.Count > Size)
            {
                T outObj;
                queue.TryDequeue(out outObj);
            }
        }

    }
}