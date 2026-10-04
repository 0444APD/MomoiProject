using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible
{
    // 화면 연결(UI Toolkit) — 데이터 목록 ↔ 요소 목록: 항목(id)마다 item UXML 하나. ViewList의 UI Toolkit판.
    // 항목이 생기면 만들고, 사라지면 없애고, 순서를 맞추고, 값이 바뀌면 bind로 다시 그린다(GameView.Redraw 위에서 돈다).
    //   _hand = List<DeckCard>("hand", cardItem, () => Game.Data().Run.deck, (e, card) => e.Q<Label>("name").text = …, GameIds.PlayerData.Run.Id);
    // 만든 요소는 GameView.RegisterElement(id)로 등록된다 → 연출이 GameView.FindElement(id)로 찾는다.
    // 요소 안의 binding-path는 쓰지 않는다(항목마다 값이 다르므로 bind에서 채운다).
    // 사라질 때 연출: OnRemove = (element, done) => …(끝나면 done) — 연출 대기열에 들어가고, done 뒤에 없앤다.
    public sealed class ElementList<TItem> : IDisposable
    {
        readonly VisualElement _container;
        readonly VisualTreeAsset _item;
        readonly Func<IEnumerable<TItem>> _items;
        readonly Action<VisualElement, TItem> _bind;
        readonly Dictionary<string, VisualElement> _elements = new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        readonly IDisposable _watch;

        // 새로 만든 직후(처음 bind 전) — 클릭 연결 등.
        public Action<VisualElement, TItem> OnCreate;
        // 사라지는 항목의 연출(끝나면 done). 비우면 바로 없앤다.
        public Action<VisualElement, Action> OnRemove;

        public IReadOnlyDictionary<string, VisualElement> Elements => _elements;

        public ElementList(VisualElement container, VisualTreeAsset item, Func<IEnumerable<TItem>> items, Action<VisualElement, TItem> bind, params string[] keys)
            : this(container, item, items, bind, true, keys)
        {
        }

        public ElementList(VisualElement container, VisualTreeAsset item, Func<IEnumerable<TItem>> items, Action<VisualElement, TItem> bind, bool waitForPresentation, params string[] keys)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _item = item;
            _items = items ?? throw new ArgumentNullException(nameof(items));
            _bind = bind;
            _watch = GameView.Redraw(Draw, waitForPresentation, keys);
        }

        public VisualElement Find(string id) => id != null && _elements.TryGetValue(id, out var element) ? element : null;
        public VisualElement Find(TItem item) => item == null ? null : Find(KeyOf(item));

        void Draw()
        {
            var items = _items()?.Where(i => i != null).ToList() ?? new List<TItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var ordered = new List<VisualElement>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var id = KeyOf(item);
                counts[id] = counts.TryGetValue(id, out var n) ? n + 1 : 1;
                var key = counts[id] == 1 ? id : $"{id}#{counts[id]}";
                if (!_elements.TryGetValue(key, out var element))
                {
                    element = _item != null ? _item.Instantiate() : new VisualElement();
                    element.AddToClassList("momoi-item");
                    _elements[key] = element;
                    _container.Add(element);
                    if (counts[id] == 1) GameView.RegisterElement(id, element);
                    OnCreate?.Invoke(element, item);
                }
                ordered.Add(element);
                seen.Add(key);
                try { _bind?.Invoke(element, item); }
                catch (Exception e) { Debug.LogException(e); }
            }
            // 데이터 순서대로 맨 앞부터 놓는다(사라지는 중인 요소는 뒤로 밀린다).
            for (int i = 0; i < ordered.Count; i++)
            {
                if (_container.IndexOf(ordered[i]) == i) continue;
                ordered[i].RemoveFromHierarchy();
                _container.Insert(Math.Min(i, _container.childCount), ordered[i]);
            }
            foreach (var key in _elements.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                var element = _elements[key];
                _elements.Remove(key);
                GameView.UnregisterElement(key, element);
                if (OnRemove == null) element.RemoveFromHierarchy();
                else GameView.Present(done => OnRemove(element, () =>
                {
                    element.RemoveFromHierarchy();
                    done();
                }));
            }
        }

        static string KeyOf(TItem item) => item is string text ? text : GameApi.IdOf(item);

        public void Dispose()
        {
            _watch.Dispose();
            foreach (var pair in _elements)
            {
                GameView.UnregisterElement(pair.Key, pair.Value);
                pair.Value.RemoveFromHierarchy();
            }
            _elements.Clear();
        }
    }
}
