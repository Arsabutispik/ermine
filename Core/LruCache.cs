using System.Collections.Generic;

public class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _maxSize;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map = new();
    private readonly LinkedList<(TKey Key, TValue Value)> _list = new();
    private readonly object _lock = new();

    public LruCache(int maxSize) => _maxSize = maxSize;

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
            if (_map.TryGetValue(key, out var existing))
            {
                _list.Remove(existing);
                _map.Remove(key);
            }
            var node = _list.AddFirst((key, value));
            _map[key] = node;

            while (_list.Count > _maxSize)
            {
                var last = _list.Last!;
                _map.Remove(last.Value.Key);
                _list.RemoveLast();
            }
        }
    }
}