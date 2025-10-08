using System;
using System.Collections.Generic;

namespace IngameScript
{
    partial class Program
    {
        static class Memo
        {
            private class CacheValue
            {
                public object Value { get; }
                public int Age { get; private set; }
                public int DepHash { get; }

                public CacheValue(int depHash, object value, int age = 0)
                {
                    DepHash = depHash;
                    Value = value;
                    Age = age;
                }

                public bool Decay()
                {
                    if (Age-- > 0) return true;
                    return false;
                }
            }

            private static readonly Dictionary<string, CacheValue> _dependencyCache = new Dictionary<string, CacheValue>();
            private static readonly Queue<string> _cacheOrder = new Queue<string>();
            private const int MaxCacheSize = 1000;

            private static int GetDepHash(object dep)
            {
                if (dep is int) return (int)dep;
                if (dep is object[])
                {
                    var arr = (object[])dep;
                    unchecked
                    {
                        int hash = 17;
                        foreach (var d in arr)
                            hash = hash * 31 + (d?.GetHashCode() ?? 0);
                        return hash;
                    }
                }
                return dep?.GetHashCode() ?? 0;
            }

            private static object IntOf(Func<object, object> f, string context, object dep)
            {
                if (_dependencyCache.Count > MaxCacheSize)
                {
                    EvictOldestCacheItem();
                }

                int depHash = GetDepHash(dep);
                string cacheKey = context;// + ":" + depHash;

                CacheValue value;
                if (_dependencyCache.TryGetValue(cacheKey, out value))
                {
                    bool isNotStale = dep is int ? value.Decay() : value.DepHash == depHash;
                    if (isNotStale) return value.Value;
                }

                var result = f(value?.Value);
                _dependencyCache[cacheKey] = new CacheValue(depHash, result, dep is int ? (int)dep : 0);
                _cacheOrder.Enqueue(cacheKey);
                return result;
            }

            public static R Of<R, T>(string context, T dep, Func<T, R> f) => (R)IntOf(d => f(d != null ? (T)d : default(T)), context, dep);
            public static R Of<R>(string context, object dep, Func<R> f) => (R)IntOf(_ => f(), context, dep);

            public static void Of<T>(string context, T dep, Action<T> f) => IntOf(d => { f(d != null ? (T)d : default(T)); return null; }, context, dep);
            public static void Of(string context, object dep, Action f) => IntOf(_ => { f(); return null; }, context, dep);

            private static void EvictOldestCacheItem()
            {
                if (_cacheOrder.Count > 0)
                {
                    var oldestKey = _cacheOrder.Dequeue();
                    _dependencyCache.Remove(oldestKey);
                }
            }
        }
    }
}
