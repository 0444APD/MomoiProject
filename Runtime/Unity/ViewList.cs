using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GBible
{
    // 화면 연결 — 데이터 목록 ↔ 화면 오브젝트 목록: 항목(id)마다 프리팹 하나.
    // 항목이 생기면 만들고, 사라지면 없애고, 순서를 맞추고, 값이 바뀌면 bind로 다시 그린다(GameView.Redraw 위에서 돈다).
    //   _hand = new ViewList<DeckCard, CardView>(cardPrefab, handRoot, () => Game.Data().Run.hand, (view, card) => view.Show(card), GameIds.PlayerData.Run);
    //   void OnDestroy() => _hand.Dispose();
    // 만든 오브젝트는 GameView.Register(id)로 등록된다 → 연출이 GameView.Find<CardView>(id)로 찾는다.
    // 사라질 때 연출: OnRemove = (view, done) => view.PlayDeath(done) — 연출 대기열에 들어가고, done 뒤에 없앤다.
    //   차례가 왔을 때 목록이 이미 정리됐거나(Dispose) 오브젝트가 없어졌으면 연출 없이 바로 없애고 넘어간다.
    //   연출 대기열을 버리면(GameView.ClearPresentation) 차례를 기다리던 것은 그 자리에서 없앤다.
    // 항목은 id가 있는 객체(또는 글자 자체가 id). 같은 id가 여러 번 있으면 "id#2"처럼 따로 만든다.
    // id가 없는 항목(정수 목록·id 없는 클래스)은 자리 번호("$0" …)로 잇는다(화면 오브젝트 등록은 하지 않는다).
    public sealed class ViewList<TItem, TView> : IDisposable where TView : Component
    {
        readonly TView _prefab;
        readonly Transform _parent;
        readonly Func<IEnumerable<TItem>> _items;
        readonly Action<TView, TItem> _bind;
        readonly Dictionary<string, TView> _views = new Dictionary<string, TView>(StringComparer.Ordinal);
        readonly IDisposable _watch;
        // 사라지는 중인 오브젝트(연출 차례를 기다리거나 도는 중).
        readonly List<Removal> _removals = new List<Removal>();
        bool _disposed;

        // 사라지는 오브젝트 하나. Finish는 한 번만 돈다(연출의 done·Dispose 중 먼저 온 쪽).
        sealed class Removal
        {
            public TView View;
            // 연출 대기열의 done(차례가 와서 OnRemove를 부른 뒤에만 있다).
            public Action Done;
            public bool Finished;

            public void Finish()
            {
                if (Finished) return;
                Finished = true;
                if (View != null) UnityEngine.Object.Destroy(View.gameObject);
                Done?.Invoke();
            }
        }

        // 사라지는 항목의 연출(끝나면 done). 비우면 바로 없앤다.
        public Action<TView, Action> OnRemove;

        // 새로 만든 직후(처음 bind 전) — 클릭 연결 등.
        public Action<TView, TItem> OnCreate;

        public IReadOnlyDictionary<string, TView> Views => _views;

        public ViewList(TView prefab, Transform parent, Func<IEnumerable<TItem>> items, Action<TView, TItem> bind, params string[] keys)
            : this(prefab, parent, items, bind, true, keys)
        {
        }

        public ViewList(TView prefab, Transform parent, Func<IEnumerable<TItem>> items, Action<TView, TItem> bind, bool waitForPresentation, params string[] keys)
        {
            _prefab = prefab != null ? prefab : throw new ArgumentNullException(nameof(prefab));
            _parent = parent;
            _items = items ?? throw new ArgumentNullException(nameof(items));
            _bind = bind;
            _watch = GameView.Redraw(Draw, waitForPresentation, keys);
            GameView.Cleared += DropQueued;
        }

        public TView ViewOf(string id) => id != null && _views.TryGetValue(id, out var view) ? view : null;
        public TView ViewOf(TItem item) => item == null ? null : ViewOf(KeyOf(item));

        // 다음 프레임 끝에 이 목록만 다시 그린다(시험용 — 게임 코드는 값이 바뀌면 저절로, 플레이어 데이터가 아닌 것은 GameView.RedrawAll).
        internal void Refresh() => GameView.MarkDirty(_watch);

        void Draw()
        {
            var items = _items()?.Where(i => i != null).ToList() ?? new List<TItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                // id가 없는 항목(정수 목록·id 없는 클래스)은 자리 번호로 잇는다 — 하나 때문에 목록 전체가 그려지지 않는 일이 없게.
                // 자리 번호는 이 목록 안에서만 뜻이 있으므로 화면 오브젝트로 등록하지 않는다(다른 목록의 "$0"과 겹친다).
                var id = KeyOf(item);
                var positional = id == null;
                if (positional) id = "$" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                counts[id] = counts.TryGetValue(id, out var n) ? n + 1 : 1;
                var key = counts[id] == 1 ? id : $"{id}#{counts[id]}";
                if (!_views.TryGetValue(key, out var view) || view == null)
                {
                    view = UnityEngine.Object.Instantiate(_prefab, _parent);
                    _views[key] = view;
                    if (counts[id] == 1 && !positional) GameView.Register(id, view.gameObject);
                    // OnCreate가 예외를 내도 이 오브젝트 그리기·나머지 항목·사라진 항목 정리는 계속한다.
                    try { OnCreate?.Invoke(view, item); }
                    catch (Exception e) { Debug.LogException(e); }
                }
                view.transform.SetSiblingIndex(i);
                seen.Add(key);
                try { _bind?.Invoke(view, item); }
                catch (Exception e) { Debug.LogException(e); }
            }
            foreach (var key in _views.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                var view = _views[key];
                _views.Remove(key);
                if (view == null) continue;
                GameView.Unregister(key, view.gameObject);
                if (OnRemove == null) UnityEngine.Object.Destroy(view.gameObject);
                else
                {
                    // 사라지는 연출이 done을 부르지 않아도(연출 시간 초과) 오브젝트가 영영 남지 않게 잠시 뒤 없앤다.
                    UnityEngine.Object.Destroy(view.gameObject, 31f);
                    var removal = new Removal { View = view };
                    _removals.Add(removal);
                    GameView.Present(done => Remove(removal, done));
                }
            }
        }

        // 사라지는 연출의 차례. 목록이 이미 정리됐거나(Dispose — 씬을 떠남) 오브젝트가 없어졌으면 연출 없이 바로 넘긴다:
        // 없어진 오브젝트의 연출은 done을 부르지 못해 대기열이 제한 시간(30초)까지 멈춘다(그동안 Busy).
        void Remove(Removal removal, Action done)
        {
            removal.Done = done;
            if (removal.Finished)
            {
                done();
                return;
            }
            var onRemove = OnRemove;
            if (_disposed || removal.View == null || onRemove == null)
            {
                Finish(removal);
                return;
            }
            try { onRemove(removal.View, () => Finish(removal)); }
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

        // 연출 대기열을 버렸을 때(GameView.ClearPresentation): 차례를 기다리던 사라짐은 영영 차례가 오지 않으니 지금 없앤다
        // (31초 안전장치까지 남으면 Undo로 되살아난 새 오브젝트와 겹쳐 보인다). 도는 중인 것(Done이 있음)은 그 연출의 done에 없어진다.
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
            foreach (var pair in _views)
            {
                GameView.Unregister(pair.Key, pair.Value != null ? pair.Value.gameObject : null);
                if (pair.Value != null) UnityEngine.Object.Destroy(pair.Value.gameObject);
            }
            _views.Clear();
            // 사라지는 중인 오브젝트도 없앤다. 차례를 기다리는 것은 차례가 오면 바로 넘어가고, 도는 중인 연출은 done을 기다리지 않는다
            // (오브젝트가 씬과 함께 없어지면 연출이 done을 부르지 못한다).
            foreach (var removal in _removals.ToArray()) removal.Finish();
            _removals.Clear();
        }
    }
}
