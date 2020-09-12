using System;
using System.Collections.Generic;

namespace DvMod.AirBrake
{
    public class Cache<TKey, TEntry>
    {
        private readonly Func<TKey, TEntry> generator;
        private readonly Dictionary<TKey, TEntry> cache = new Dictionary<TKey, TEntry>();

        public Cache(Func<TKey, TEntry> generator)
        {
            this.generator = generator;
        }

        public TEntry this[TKey key]
        {
            get {
                if (cache.TryGetValue(key, out TEntry entry))
                    return entry;
                entry = generator(key);
                cache[key] = entry;
                return entry;
            }
        }
    }
}