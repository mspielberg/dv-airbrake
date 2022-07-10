using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
            get
            {
                if (cache.TryGetValue(key, out TEntry entry))
                    return entry;
                entry = generator(key);
                cache[key] = entry;
                return entry;
            }
        }
    }

    public static class UnityExtensions
    {
        public static string GetPath(this Component c)
        {
            return string.Join("/", c.GetComponentsInParent<Transform>(true).Reverse().Select(c => c.name));
        }

        public static string DumpHierarchy(this GameObject gameObject)
        {
            return string.Join("\n", gameObject.GetComponentsInChildren<Component>().Select(c => $"{GetPath(c)} {c.GetType()}"));
        }
    }
}
