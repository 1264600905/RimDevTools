using System;
using System.Collections.Generic;

namespace LogSyncMod.AutoTest
{
    public sealed class AutoTestBoundedBuffer<T>
    {
        private readonly int capacity;
        private readonly List<T> items = new List<T>();
        private readonly object gate = new object();

        public AutoTestBoundedBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException("capacity");
            this.capacity = capacity;
        }

        public void Add(T item)
        {
            lock (gate)
            {
                items.Add(item);
                if (items.Count > capacity) items.RemoveAt(0);
            }
        }

        public List<T> Snapshot()
        {
            lock (gate) return new List<T>(items);
        }
    }
}
