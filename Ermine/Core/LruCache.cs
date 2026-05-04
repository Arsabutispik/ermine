using System;
using System.Collections.Generic;

namespace Ermine.Core;

public class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly long _maxBytes;
    private long _currentBytes;
    private readonly Func<TValue, long> _getSize;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value, long Size)>> _map = new();
    private readonly LinkedList<(TKey Key, TValue Value, long Size)> _list = new();
    private readonly object _lock = new();

    public LruCache(long maxBytes, Func<TValue, long> getSize) 
    {
        _maxBytes = maxBytes;
        _getSize = getSize;
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _list.Remove(node);
                _list.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = default!;
            return false;
        }
    }

    public void Add(TKey key, TValue value)
    {
        lock (_lock)
        {
            var itemSize = _getSize(value);

            if (_map.TryGetValue(key, out var existing))
            {
                _currentBytes -= existing.Value.Size;
                DisposeValue(existing.Value.Value, existing.Value.Size);
                _list.Remove(existing);
                _map.Remove(key);
            }

            var node = _list.AddFirst((key, value, itemSize));
            _map[key] = node;
            _currentBytes += itemSize;

            GC.AddMemoryPressure(itemSize);
            
            while (_currentBytes > _maxBytes && _list.Count > 0)
            {
                var last = _list.Last!;
                _map.Remove(last.Value.Key);
                _currentBytes -= last.Value.Size;
                DisposeValue(last.Value.Value,  last.Value.Size);
                _list.RemoveLast();
            }
        }
    }

    private static void DisposeValue(TValue value, long size)
    {
        if (value is IDisposable disposable)
        {
            disposable.Dispose();
            GC.RemoveMemoryPressure(size);
        }
    }
}