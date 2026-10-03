using System;
using System.Collections.Generic;

namespace GBible
{
    // 화면 갱신 도우미: 플레이어 데이터나 언어가 바뀌면 다음 프레임 끝에 한 번만 다시 그린다.
    // 값이 바뀌었다는 알림은 한 행동에도 여러 번 온다(중간 Sync, 실패한 행동의 되돌림, Undo). 알림마다 그리면 같은 화면을
    // 한 프레임에 여러 번 계산하게 되므로, 표시만 남겼다가 프레임 끝(행동 밖)에서 모아 한 번 그린다. 언어가 바뀌어도 같은 프레임에 다시 그린다.
    //   void OnEnable()  => _view = GameView.Redraw(Redraw, GameIds.PlayerData.Run);   // 키를 비우면 어느 값이 바뀌어도
    //   void OnDisable() => _view.Dispose();
    // 처음 한 번은 등록한 프레임 끝에 바로 그린다.
    public static class GameView
    {
        sealed class Watch : IDisposable
        {
            public Action Draw;
            public HashSet<string> Keys;
            public bool Dirty = true;
            public bool Disposed;

            public void Dispose()
            {
                Disposed = true;
                Watches.Remove(this);
            }
        }

        static readonly List<Watch> Watches = new List<Watch>();
        static GameSession _session;

        public static IDisposable Redraw(Action draw, params string[] keys)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            Hook();
            var watch = new Watch { Draw = draw, Keys = keys == null || keys.Length == 0 ? null : new HashSet<string>(keys, StringComparer.Ordinal) };
            Watches.Add(watch);
            return watch;
        }

        static void Hook()
        {
            if (_session == GBibleRuntime.Session) return;
            if (_session != null) _session.Data.Changed -= OnChanged;
            _session = GBibleRuntime.Session;
            if (_session != null) _session.Data.Changed += OnChanged;
            GameLanguage.Changed -= OnLanguage;
            GameLanguage.Changed += OnLanguage;
        }

        static void OnChanged(string key)
        {
            foreach (var watch in Watches)
                if (watch.Keys == null || watch.Keys.Contains(key)) watch.Dirty = true;
        }

        static void OnLanguage()
        {
            foreach (var watch in Watches) watch.Dirty = true;
        }

        // 프레임 끝(GBibleHost.LateUpdate)에서 부른다. 행동 중이면 다음 프레임으로 미룬다.
        internal static void FrameEnd()
        {
            if (Watches.Count == 0) return;
            if (_session != null && _session.Data.InTransaction) return;
            foreach (var watch in Watches.ToArray())
            {
                if (!watch.Dirty || watch.Disposed) continue;
                watch.Dirty = false;
                try { watch.Draw(); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
            }
        }

        // 플레이를 다시 시작할 때(도메인 리로드를 끈 설정) 이전 등록을 지운다.
        internal static void Reset()
        {
            if (_session != null) _session.Data.Changed -= OnChanged;
            _session = null;
            Watches.Clear();
            GameLanguage.Changed -= OnLanguage;
        }
    }
}
