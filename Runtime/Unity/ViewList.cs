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
    // 항목은 id가 있는 객체(또는 글자 자체가 id). 같은 id가 여러 번 있으면 "id#2"처럼 따로 만든다.
    public sealed class ViewList<TItem, TView> : IDisposable where TView : Component
    {
        readonly TView _prefab;
        readonly Transform _parent;
        readonly Func<IEnumerable<TItem>> _items;
        readonly Action<TView, TItem> _bind;
        readonly Dictionary<string, TView> _views = new Dictionary<string, TView>(StringComparer.Ordinal);
        readonly IDisposable _watch;

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
        }

        public TView ViewOf(string id) => id != null && _views.TryGetValue(id, out var view) ? view : null;
        public TView ViewOf(TItem item) => item == null ? null : ViewOf(KeyOf(item));

        void Draw()
        {
            var items = _items()?.Where(i => i != null).ToList() ?? new List<TItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                // id가 없는 항목(단순 클래스 목록)은 자리 번호로 잇는다 — 하나 때문에 목록 전체가 그려지지 않는 일이 없게.
                var id = KeyOf(item) ?? "$" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                counts[id] = counts.TryGetValue(id, out var n) ? n + 1 : 1;
                var key = counts[id] == 1 ? id : $"{id}#{counts[id]}";
                if (!_views.TryGetValue(key, out var view) || view == null)
                {
                    view = UnityEngine.Object.Instantiate(_prefab, _parent);
                    _views[key] = view;
                    if (counts[id] == 1) GameView.Register(id, view.gameObject);
                    OnCreate?.Invoke(view, item);
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
                    GameView.Present(done => OnRemove(view, () =>
                    {
                        if (view != null) UnityEngine.Object.Destroy(view.gameObject);
                        done();
                    }));
                }
            }
        }

        static string KeyOf(TItem item) => item is string text ? text : GameApi.IdOf(item);

        public void Dispose()
        {
            _watch.Dispose();
            foreach (var pair in _views)
            {
                GameView.Unregister(pair.Key, pair.Value != null ? pair.Value.gameObject : null);
                if (pair.Value != null) UnityEngine.Object.Destroy(pair.Value.gameObject);
            }
            _views.Clear();
        }
    }
}
