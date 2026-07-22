using System;
using System.Collections.Generic;
using System.Linq;

namespace Thesis.Orchestration.Debugging
{
    /// <summary>
    /// Keeps a short in-memory history of orchestration events
    /// for debugging and local UI visualization.
    /// </summary>
    public class QueueEventHistory
    {
        private readonly Queue<string> events = new();
        private readonly int maxEntries;

        public QueueEventHistory(int maxEntries = 20)
        {
            this.maxEntries = Math.Max(1, maxEntries);
        }

        public void Add(string message)
        {
            string formatted = $"[{DateTime.Now:HH:mm:ss}] {message}";
            events.Enqueue(formatted);

            while (events.Count > maxEntries)
            {
                events.Dequeue();
            }
        }

        public IReadOnlyList<string> GetAll()
        {
            return events.ToList();
        }

        public void Clear()
        {
            events.Clear();
        }
    }
}