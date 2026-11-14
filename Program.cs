using System.Collections;
using CustomCollections;

// ───────────────────────── Demo ─────────────────────────

// 1. Basics: a collection expression works because the class is IEnumerable and has Add().
CustomCollection<string> songs = ["Intro", "Verse", "Chorus"];
Console.WriteLine($"Count = {songs.Count}, Capacity = {songs.Capacity}");   // 3, 4

// 2. Growth: capacity doubles (4 -> 8) when the backing array is full.
songs.Add("Bridge");
songs.Add("Outro");
Console.WriteLine($"After 2 more adds: Count = {songs.Count}, Capacity = {songs.Capacity}");   // 5, 8

// 3. Insert / Remove / RemoveAt shift the items in the backing array.
songs.Insert(1, "Pre-verse");
songs.Remove("Bridge");
songs.RemoveAt(0);
Console.WriteLine($"Items: {string.Join(", ", songs)}");   // Pre-verse, Verse, Chorus, Outro
Console.WriteLine($"Contains \"Verse\": {songs.Contains("Verse")}, IndexOf(\"Chorus\"): {songs.IndexOf("Chorus")}");

// 4. Indexer: only 0..Count-1 is valid, even though the array is larger.
songs[0] = "Verse 0";
Console.WriteLine($"songs[0] = {songs[0]}");
try
{
    Console.WriteLine(songs[songs.Count]);   // inside the array (capacity 8), but past Count
}
catch (IndexOutOfRangeException)
{
    Console.WriteLine($"songs[{songs.Count}] -> IndexOutOfRangeException (Count is {songs.Count})");
}

// 5. IEnumerable<T> vs IEnumerator<T>.
//    foreach asks the sequence for an enumerator and calls MoveNext()/Current/Dispose() for us.
foreach (string song in songs)
{
    Console.WriteLine($"foreach:  {song}");
}

//    The same walk by hand. An enumerator is only a cursor: `foreach (var s in e)` would not compile.
using (IEnumerator<string> e = songs.GetEnumerator())
{
    while (e.MoveNext())
    {
        Console.WriteLine($"MoveNext: {e.Current}");
    }
    Console.WriteLine($"MoveNext after the end: {e.MoveNext()}");   // False: a cursor is single-pass
}

//    One sequence, many independent cursors.
using IEnumerator<string> first = songs.GetEnumerator();
using IEnumerator<string> second = songs.GetEnumerator();
first.MoveNext();
first.MoveNext();
second.MoveNext();
Console.WriteLine($"first is on {first.Current}, second is on {second.Current}");

//    Iterator-generated enumerators (yield return) don't support Reset().
try
{
    first.Reset();
}
catch (NotSupportedException)
{
    Console.WriteLine("Reset() -> NotSupportedException: call GetEnumerator() again instead.");
}

// 6. Copying out and clearing.
string[] copy = [.. songs];
Console.WriteLine($"ToArray: [{string.Join(", ", copy)}]");
songs.Clear();
Console.WriteLine($"After Clear: Count = {songs.Count}, Capacity = {songs.Capacity}");   // 0, 8 (capacity is kept)

// 7. Argument checks.
try
{
    songs.RemoveAt(0);
}
catch (ArgumentOutOfRangeException ex)
{
    Console.WriteLine($"RemoveAt(0) on an empty collection -> {ex.GetType().Name} ({ex.ParamName})");
}

// ─────────────────────── The collection ───────────────────────

namespace CustomCollections
{
    /// <summary>
    /// A minimal dynamic array, like a simplified List&lt;T&gt;: items live in a backing array
    /// that doubles in size when it is full. Only the first <see cref="Count"/> slots are in use.
    /// </summary>
    public class CustomCollection<T> : IEnumerable<T>
    {
        private const int DefaultCapacity = 4;
        private static readonly T[] s_emptyArray = [];

        private T[] _items;
        private int _count;

        public CustomCollection()
        {
            _items = s_emptyArray;
        }

        public int Count => _count;

        public int Capacity
        {
            get => _items.Length;
            set
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(value, _count);

                T[] newItems = new T[value];
                Array.Copy(_items, newItems, _count);
                _items = newItems;
            }
        }

        // The span covers only the used part of the array, so the runtime itself throws
        // IndexOutOfRangeException for any index outside 0..Count-1.
        public T this[int index]
        {
            get => _items.AsSpan(0, _count)[index];
            set => _items.AsSpan(0, _count)[index] = value;
        }

        public void Add(T item)
        {
            EnsureCapacity(_count + 1);
            _items[_count++] = item;
        }

        public void Insert(int index, T item)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _count);

            EnsureCapacity(_count + 1);
            if (index < _count)
            {
                Array.Copy(_items, index, _items, index + 1, _count - index);
            }
            _items[index] = item;
            _count++;
        }

        public bool Remove(T item)
        {
            int index = IndexOf(item);
            if (index < 0)
            {
                return false;
            }

            RemoveAt(index);
            return true;
        }

        public void RemoveAt(int index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);

            _count--;
            if (index < _count)
            {
                Array.Copy(_items, index + 1, _items, index, _count - index);
            }
            _items[_count] = default!;   // drop the reference so the GC can collect it
        }

        public void Clear()
        {
            Array.Clear(_items, 0, _count);
            _count = 0;
        }

        public bool Contains(T item) => IndexOf(item) >= 0;

        public int IndexOf(T item) => Array.IndexOf(_items, item, 0, _count);

        public void EnsureCapacity(int min)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(min);
            if (_items.Length < min)
            {
                Capacity = GetNewCapacity(min);
            }
        }

        public T[] ToArray()
        {
            if (_count == 0)
            {
                return s_emptyArray;
            }

            T[] array = new T[_count];
            Array.Copy(_items, array, _count);
            return array;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            Array.Copy(_items, 0, array, arrayIndex, _count);
        }

        // An iterator: the compiler generates the IEnumerator<T> class (MoveNext, Current, Dispose).
        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _count; i++)
            {
                yield return _items[i];
            }
        }

        // The non-generic version is required by IEnumerable; it just reuses the generic one.
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private int GetNewCapacity(int min)
        {
            int newCapacity = _items.Length == 0 ? DefaultCapacity : _items.Length * 2;
            return Math.Max(newCapacity, min);
        }
    }
}
