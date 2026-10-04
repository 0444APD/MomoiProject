using System;
using UnityEngine;
using UnityEngine.Events;

namespace GBible
{
    // 월드 상태 ↔ 씬 오브젝트(코드 없이): 월드 상태 값에 따라 오브젝트를 켜고 끄고, 값을 이벤트로 보낸다.
    //   열린 상자: 이름 "forest.chest3", 값 "opened" → 켤 것 = 열린 모습, 끌 것 = 닫힌 모습
    //   쓰러뜨린 보스: 이름 "forest.boss", 값 비움(참이면) → 끌 것 = 보스 오브젝트
    // 값은 행동 안에서 Game.SetWorld로 바꾼다. 씬을 다시 열거나 판을 불러와도 그 상태로 보인다.
    [AddComponentMenu("MomoiProject/월드 상태 연결")]
    public sealed class WorldStateBinding : MonoBehaviour
    {
        [Tooltip("월드 상태 이름(씬 이름.오브젝트 이름 권장)")] public string id = "";
        [Tooltip("이 값일 때 '맞음'. 비우면 값이 있고 거짓이 아니면 맞음")] public string value = "";
        [Tooltip("맞을 때 켜고, 아닐 때 끈다")] public GameObject[] showWhenMatched = Array.Empty<GameObject>();
        [Tooltip("맞을 때 끄고, 아닐 때 켠다")] public GameObject[] hideWhenMatched = Array.Empty<GameObject>();
        public UnityEvent<bool> onMatched = new UnityEvent<bool>();
        public UnityEvent<string> onValue = new UnityEvent<string>();

        IDisposable _watch;

        void OnEnable() => _watch = GameView.Redraw(Draw, WorldStateSystem.SessionKey, WorldStateSystem.MetaKey);

        void OnDisable()
        {
            _watch?.Dispose();
            _watch = null;
        }

        void Draw()
        {
            var game = GBibleRuntime.Game;
            if (game == null || string.IsNullOrEmpty(id)) return;
            var current = game.World(id);
            var matched = string.IsNullOrEmpty(value) ? WorldStateSystem.IsTrue(current) : current == value;
            foreach (var target in showWhenMatched) if (target != null && target != gameObject) target.SetActive(matched);
            foreach (var target in hideWhenMatched) if (target != null && target != gameObject) target.SetActive(!matched);
            onMatched.Invoke(matched);
            onValue.Invoke(current ?? "");
        }
    }
}
