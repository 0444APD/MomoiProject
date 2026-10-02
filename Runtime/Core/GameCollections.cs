using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace GBible
{
    // 플레이어 데이터 클래스 안의 리스트. List<T>와 같이 쓰고, 넣기·빼기·바꾸기가 그 순간 기록된다(Undo·Journal·조건 알림).
    // List<T>의 Add·Remove는 필드 쓰기가 아니라서 위빙으로 잡을 수 없어 이 형식을 쓴다.
    [Serializable]
    public sealed class GameList<T> : IList<T>, IReadOnlyList<T>, IList, IGameCollection
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        List<T> items = new List<T>();

        public GameList()
        {
        }

        public GameList(IEnumerable<T> source)
        {
            items.AddRange(source);
        }

        public int Count => items.Count;
        public bool IsReadOnly => false;

        public T this[int index]
        {
            get => items[index];
            set
            {
                var old = items[index];
                if (EqualityComparer<T>.Default.Equals(old, value)) return;
                PlayerDataTracker.ListChanged(this, DataWriteKind.SetItem, index, old, value);
                items[index] = value;
            }
        }

        public void Add(T item) => Insert(items.Count, item);

        public void AddRange(IEnumerable<T> source)
        {
            foreach (var item in source.ToList()) Add(item);
        }

        public void Insert(int index, T item)
        {
            if (index < 0 || index > items.Count) throw new ArgumentOutOfRangeException(nameof(index));
            PlayerDataTracker.ListChanged(this, DataWriteKind.Insert, index, null, item);
            items.Insert(index, item);
        }

        public bool Remove(T item)
        {
            var index = items.IndexOf(item);
            if (index < 0) return false;
            RemoveAt(index);
            return true;
        }

        public void RemoveAt(int index)
        {
            var old = items[index];
            PlayerDataTracker.ListChanged(this, DataWriteKind.Remove, index, old, null);
            items.RemoveAt(index);
        }

        public int RemoveAll(Predicate<T> match)
        {
            var removed = 0;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (!match(items[i])) continue;
                RemoveAt(i);
                removed++;
            }
            return removed;
        }

        public void Clear()
        {
            if (items.Count == 0) return;
            PlayerDataTracker.CollectionCleared(this, items.Cast<object>().ToList());
            items.Clear();
        }

        public int IndexOf(T item) => items.IndexOf(item);
        public bool Contains(T item) => items.Contains(item);
        public void CopyTo(T[] array, int arrayIndex) => items.CopyTo(array, arrayIndex);
        public List<T>.Enumerator GetEnumerator() => items.GetEnumerator();
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();
        public T Find(Predicate<T> match) => items.Find(match);
        public int FindIndex(Predicate<T> match) => items.FindIndex(match);
        public bool Exists(Predicate<T> match) => items.Exists(match);
        public T[] ToArray() => items.ToArray();

        // ── IList(저장소 도구가 형식을 모르고 다룰 때) ─────────
        bool IList.IsFixedSize => false;
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => this;
        object IList.this[int index] { get => items[index]; set => this[index] = (T)value; }
        int IList.Add(object value)
        {
            Add((T)value);
            return items.Count - 1;
        }
        bool IList.Contains(object value) => value is T t && items.Contains(t);
        int IList.IndexOf(object value) => value is T t ? items.IndexOf(t) : -1;
        void IList.Insert(int index, object value) => Insert(index, (T)value);
        void IList.Remove(object value)
        {
            if (value is T t) Remove(t);
        }
        void ICollection.CopyTo(Array array, int index) => ((ICollection)items).CopyTo(array, index);

        // ── 기록 없이(되돌리기) ────────────────────────────────
        IEnumerable IGameCollection.Items => items;
        void IGameCollection.RawInsert(int index, object key, object value) => items.Insert(index, (T)value);
        void IGameCollection.RawRemove(int index, object key) => items.RemoveAt(index);
        void IGameCollection.RawSet(int index, object key, object value) => items[index] = (T)value;
        void IGameCollection.RawRestore(IList source)
        {
            items.Clear();
            foreach (var item in source) items.Add((T)item);
        }
    }

    // 플레이어 데이터 클래스 안의 딕셔너리. Dictionary<K,V>와 같이 쓰고, 넣기·빼기·바꾸기가 그 순간 기록된다.
    // Unity 직렬화를 위해 키·값을 두 리스트로 저장한다. 키는 숫자·글자·enum.
    [Serializable]
    public sealed class GameDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>, IDictionary, IGameCollection
#if UNITY_2020_1_OR_NEWER
        , UnityEngine.ISerializationCallbackReceiver
#endif
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        List<TKey> keys = new List<TKey>();
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        List<TValue> values = new List<TValue>();

        [NonSerialized] Dictionary<TKey, TValue> _map = new Dictionary<TKey, TValue>();

        Dictionary<TKey, TValue> Map
        {
            get
            {
                if (_map == null)
                {
                    _map = new Dictionary<TKey, TValue>();
                    for (int i = 0; i < Math.Min(keys.Count, values.Count); i++) _map[keys[i]] = values[i];
                }
                return _map;
            }
        }

        public int Count => Map.Count;
        public bool IsReadOnly => false;
        public ICollection<TKey> Keys => Map.Keys;
        public ICollection<TValue> Values => Map.Values;
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Map.Keys;
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Map.Values;

        public TValue this[TKey key]
        {
            get => Map[key];
            set
            {
                var existed = Map.TryGetValue(key, out var old);
                if (existed && EqualityComparer<TValue>.Default.Equals(old, value)) return;
                PlayerDataTracker.DictionaryChanged(this, existed ? DataWriteKind.SetItem : DataWriteKind.Insert, key, existed ? (object)old : null, value);
                Map[key] = value;
            }
        }

        public void Add(TKey key, TValue value)
        {
            if (Map.ContainsKey(key)) throw new ArgumentException($"'{key}' 키가 이미 있습니다.");
            this[key] = value;
        }

        public bool Remove(TKey key)
        {
            if (!Map.TryGetValue(key, out var old)) return false;
            PlayerDataTracker.DictionaryChanged(this, DataWriteKind.Remove, key, old, null);
            Map.Remove(key);
            return true;
        }

        public void Clear()
        {
            if (Map.Count == 0) return;
            PlayerDataTracker.CollectionCleared(this, Map.Select(p => (object)new KeyValuePair<TKey, TValue>(p.Key, p.Value)).ToList());
            Map.Clear();
        }

        public bool ContainsKey(TKey key) => Map.ContainsKey(key);
        public bool TryGetValue(TKey key, out TValue value) => Map.TryGetValue(key, out value);
        public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);
        public bool Contains(KeyValuePair<TKey, TValue> item) => Map.TryGetValue(item.Key, out var v) && EqualityComparer<TValue>.Default.Equals(v, item.Value);
        public bool Remove(KeyValuePair<TKey, TValue> item) => Contains(item) && Remove(item.Key);
        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) => ((ICollection<KeyValuePair<TKey, TValue>>)Map).CopyTo(array, arrayIndex);
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => Map.GetEnumerator();
        // IDictionary로 돌 때는 DictionaryEntry를 낸다(제네릭으로 돌면 KeyValuePair).
        IEnumerator IEnumerable.GetEnumerator() => ((IDictionary)Map).GetEnumerator();

        // ── IDictionary(저장소 도구가 형식을 모르고 다룰 때) ───
        bool IDictionary.IsFixedSize => false;
        ICollection IDictionary.Keys => Map.Keys;
        ICollection IDictionary.Values => Map.Values;
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => this;
        object IDictionary.this[object key] { get => Map.TryGetValue((TKey)key, out var v) ? v : default; set => this[(TKey)key] = (TValue)value; }
        void IDictionary.Add(object key, object value) => Add((TKey)key, (TValue)value);
        bool IDictionary.Contains(object key) => key is TKey k && Map.ContainsKey(k);
        IDictionaryEnumerator IDictionary.GetEnumerator() => ((IDictionary)Map).GetEnumerator();
        void IDictionary.Remove(object key)
        {
            if (key is TKey k) Remove(k);
        }
        void ICollection.CopyTo(Array array, int index) => ((ICollection)Map).CopyTo(array, index);

        // ── 기록 없이(되돌리기) ────────────────────────────────
        IEnumerable IGameCollection.Items => Map.Values;
        void IGameCollection.RawInsert(int index, object key, object value) => Map[(TKey)key] = (TValue)value;
        void IGameCollection.RawRemove(int index, object key) => Map.Remove((TKey)key);
        void IGameCollection.RawSet(int index, object key, object value) => Map[(TKey)key] = (TValue)value;
        void IGameCollection.RawRestore(IList source)
        {
            Map.Clear();
            foreach (KeyValuePair<TKey, TValue> pair in source) Map[pair.Key] = pair.Value;
        }

#if UNITY_2020_1_OR_NEWER
        void UnityEngine.ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            keys.Clear();
            values.Clear();
            foreach (var pair in Map)
            {
                keys.Add(pair.Key);
                values.Add(pair.Value);
            }
        }

        void UnityEngine.ISerializationCallbackReceiver.OnAfterDeserialize() => _map = null;
#endif
    }
}
