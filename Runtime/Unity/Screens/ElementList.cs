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
    //   차례가 왔을 때 목록이 이미 정리됐거나(Dispose — 화면을 닫음) 칸이 화면에서 떨어졌으면 연출 없이 바로 없애고 넘어간다.
    //   연출 대기열을 버리면(GameView.ClearPresentation) 차례를 기다리던 것은 그 자리에서 치운다.
    // id가 없는 항목(정수 목록·id 없는 클래스)은 자리 번호("$0" …)로 잇는다(GameView.RegisterElement 등록은 하지 않는다).
    public sealed class ElementList<TItem> : IDisposable
    {
        readonly VisualElement _container;
        readonly VisualTreeAsset _item;
        readonly Func<IEnumerable<TItem>> _items;
        readonly Action<VisualElement, TItem> _bind;
        readonly Dictionary<string, VisualElement> _elements = new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        readonly IDisposable _watch;
        // 사라지는 중인 칸(연출 차례를 기다리거나 도는 중).
        readonly List<Removal> _removals = new List<Removal>();
        bool _disposed;

        // 사라지는 칸 하나. Finish는 한 번만 돈다(연출의 done·Dispose 중 먼저 온 쪽).
        sealed class Removal
        {
            public VisualElement Element;
            // 연출 대기열의 done(차례가 와서 OnRemove를 부른 뒤에만 있다).
            public Action Done;
            public bool Finished;

            public void Finish()
            {
                if (Finished) return;
                Finished = true;
                Element.RemoveFromHierarchy();
                Done?.Invoke();
            }
        }

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
            GameView.Cleared += DropQueued;
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
                // id가 없는 항목(정수 목록·id 없는 클래스)은 자리 번호로 잇는다 — 하나 때문에 목록 전체가 그려지지 않는 일이 없게.
                // 자리 번호는 이 목록 안에서만 뜻이 있으므로 화면 전체(FindElement)에 등록하지 않는다(다른 목록의 "$0"과 겹친다).
                var id = KeyOf(item);
                var positional = id == null;
                if (positional) id = "$" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                counts[id] = counts.TryGetValue(id, out var n) ? n + 1 : 1;
                var key = counts[id] == 1 ? id : $"{id}#{counts[id]}";
                if (!_elements.TryGetValue(key, out var element))
                {
                    element = _item != null ? _item.Instantiate() : new VisualElement();
                    element.AddToClassList("momoi-item");
                    _elements[key] = element;
                    _container.Add(element);
                    if (counts[id] == 1 && !positional) GameView.RegisterElement(id, element);
                    // OnCreate가 예외를 내도 이 칸 그리기·나머지 항목·사라진 항목 정리는 계속한다.
                    try { OnCreate?.Invoke(element, item); }
                    catch (Exception e) { Debug.LogException(e); }
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
                else
                {
                    // 사라지는 연출이 done을 부르지 않아도(연출 시간 초과) 요소가 영영 남지 않게 잠시 뒤 치운다.
                    element.schedule.Execute(element.RemoveFromHierarchy).StartingIn(31000);
                    var removal = new Removal { Element = element };
                    _removals.Add(removal);
                    GameView.Present(done => Remove(removal, done));
                }
            }
        }

        // 사라지는 연출의 차례. 목록이 이미 정리됐거나(Dispose — 화면을 닫음) 칸이 화면에서 떨어졌으면(panel 없음) 연출 없이 바로 넘긴다:
        // 떨어진 칸에서는 schedule·전환이 돌지 않아 연출이 done을 부르지 못하고, 대기열이 제한 시간(30초)까지 멈춘다(그동안 Busy).
        void Remove(Removal removal, Action done)
        {
            removal.Done = done;
            if (removal.Finished)
            {
                done();
                return;
            }
            var onRemove = OnRemove;
            if (_disposed || removal.Element.panel == null || onRemove == null)
            {
                Finish(removal);
                return;
            }
            try { onRemove(removal.Element, () => Finish(removal)); }
            catch (Exception e)
            {
                Debug.LogException(e);
                Finish(removal);
            }
        }

        void Finish(Removal removal)
        {
            _removals.Remove(removal);
            removal.Finish();
        }

        // 연출 대기열을 버렸을 때(GameView.ClearPresentation): 차례를 기다리던 사라짐은 영영 차례가 오지 않으니 지금 치운다
        // (31초 안전장치까지 남으면 Undo로 되살아난 새 칸과 겹쳐 보인다). 도는 중인 것(Done이 있음)은 그 연출의 done에 치워진다.
        void DropQueued()
        {
            foreach (var removal in _removals.Where(r => r.Done == null).ToArray()) Finish(removal);
        }

        // 열쇠: 글자면 그 글자, id가 있는 객체면 그 id, 없으면 null(→ 자리 번호).
        static string KeyOf(TItem item) => item is string text ? text : item != null && GameApi.HasId(item.GetType()) ? GameApi.IdOf(item) : null;

        public void Dispose()
        {
            _disposed = true;
            _watch.Dispose();
            GameView.Cleared -= DropQueued;
            foreach (var pair in _elements)
            {
                GameView.UnregisterElement(pair.Key, pair.Value);
                pair.Value.RemoveFromHierarchy();
            }
            _elements.Clear();
            // 사라지는 중인 칸도 치운다. 차례를 기다리는 것은 차례가 오면 바로 넘어가고, 도는 중인 연출은 done을 기다리지 않는다
            // (닫힌 화면의 칸에서는 연출이 돌지 않는다).
            foreach (var removal in _removals.ToArray()) removal.Finish();
            _removals.Clear();
        }
    }
}
