namespace UrlShortener.Infrastructure.Caching;

/// <summary>
/// Fixed-capacity Least Recently Used cache (US-16, same idea as LeetCode 146).
/// Dictionary gives O(1) lookup of the list node; the doubly linked list keeps usage order
/// (front = most recently used, back = least), so moving a node and evicting the back are O(1).
/// One lock keeps it simple and correct; every operation is a few pointer moves, so contention is low.
/// </summary>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map;
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new();
    private readonly Lock _lock = new();

    public LruCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>(capacity);
    }

    public int Count
    {
        get { lock (_lock) { return _map.Count; } }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                // A read counts as a use: move to the front.
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }

            value = default!;
            return false;
        }
    }

    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                existing.Value = (key, value);
                _order.Remove(existing);
                _order.AddFirst(existing);
                return;
            }

            if (_map.Count >= _capacity)
            {
                var lru = _order.Last!;
                _order.RemoveLast();
                _map.Remove(lru.Value.Key);
            }

            _map[key] = _order.AddFirst((key, value));
        }
    }

    public bool Remove(TKey key)
    {
        lock (_lock)
        {
            if (!_map.Remove(key, out var node))
            {
                return false;
            }

            _order.Remove(node);
            return true;
        }
    }
}
