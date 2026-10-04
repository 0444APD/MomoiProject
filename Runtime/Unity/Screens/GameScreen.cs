using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using UnityEngine.UIElements;

namespace GBible
{
    // 화면 하나의 코드(DB › 화면 항목에 [BindTo]로 잇는다). 열 때 OnOpen, 플레이어 데이터·언어가 바뀌면 Draw(프레임 끝에 한 번), 닫을 때 OnClose.
    //   [BindTo(GameIds.Screen.BattleHud)]
    //   public class BattleHud : GameScreen
    //   {
    //       ElementList<string> _hand;
    //       protected override void OnOpen()
    //       {
    //           OnClick("end-turn", () => new EndTurn());                         // 버튼 → 행동(연출·선택 대기 중에는 무시, 플레이어용 실패는 알림)
    //           OnClick("menu", () => GameScreens.Open(GameScreens.Pause));       // 버튼 → 화면 일
    //           _hand = List<string>("hand", cardItem, () => Game.Data().Run.battle.hand, (e, id) => e.Q<Label>("name").text = …);
    //       }
    //       protected override void Draw() => Q<Label>("floor").text = …;      // binding-path로 안 되는 것만
    //   }
    // 값을 보이기만 하는 칸은 코드 없이 UXML의 binding-path로(ScreenBinding): <ui:Label binding-path="run:gold@hud.gold" />.
    // 화면 코드는 게임이 켜질 때 한 번 만들어 두고 열 때마다 다시 쓴다(필드는 OnOpen에서 새로 채운다).
    public abstract class GameScreen
    {
        public string Id { get; internal set; }
        // 이 화면의 트리(UXML을 만든 것). 열려 있을 때만.
        public VisualElement Root { get; internal set; }
        // GameScreens.Open(id, argument)로 넘긴 값.
        public object Argument { get; internal set; }
        public bool IsOpen { get; internal set; }
        // DB › 화면 항목(기본 화면이면 null).
        public ScreenData Data { get; internal set; }

        protected GameApi Game => GBibleRuntime.Game;

        // 다시 그릴 플레이어 데이터 키. null이면 binding-path에 쓴 키들(없으면 어느 값이 바뀌어도), 빈 배열이면 어느 값이 바뀌어도.
        protected internal virtual string[] Watch => null;
        // 연출 대기열이 끝난 뒤에 그린다(피해 숫자가 다 뜬 뒤에 체력이 바뀐다).
        protected internal virtual bool WaitForPresentation => true;

        protected internal virtual void OnOpen()
        {
        }

        protected internal virtual void Draw()
        {
        }

        protected internal virtual void OnClose()
        {
        }

        // 열려 있는 동안 매 프레임(실제 시간 초, 게임 일시정지와 상관없이). 남은 쿨다운 원·타이머처럼 데이터가 아니라 시간에 따라 바뀌는 칸.
        // 여기서 데이터를 바꾸지 않는다(바꾸려면 행동).
        protected internal virtual void Tick(float deltaTime)
        {
        }

        public void Close() => GameScreens.Close(this);

        // 이름으로 찾는다. 없으면 어느 화면의 어느 이름인지 알리는 예외(UXML과 코드의 이름이 어긋났을 때).
        protected T Q<T>(string name) where T : VisualElement =>
            Root?.Q<T>(name) ?? throw new InvalidOperationException($"[MomoiProject] 화면 '{Id}'의 UXML에 이름(name)이 '{name}'인 {typeof(T).Name}이(가) 없습니다.");

        protected VisualElement Q(string name) => Q<VisualElement>(name);

        // 목록 한 칸의 UXML: DB › 화면의 '목록 칸 UXML'이나 화면 UXML의 <ui:Template name="…">에서 이름(파일 이름)으로 찾는다.
        protected VisualTreeAsset Item(string name)
        {
            var found = Data?.items.FirstOrDefault(i => i != null && i.name == name)
                        ?? Data?.layout?.templateDependencies.FirstOrDefault(t => t != null && t.name == name);
            return found ?? throw new InvalidOperationException($"[MomoiProject] 화면 '{Id}'에 목록 칸 UXML '{name}'이 없습니다(DB › 화면 › 목록 칸 UXML).");
        }

        // 버튼 → 행동. 연출·선택 대기 중(GameView.Busy)에는 무시하고, 플레이어용 실패는 알림(DB › 화면 설정 '실패를 알림으로')으로 보인다.
        protected void OnClick(string button, Func<GameAction> action) => OnClick(Q<Button>(button), action);
        protected void OnClick(Button button, Func<GameAction> action) => Hook(button, () => GameScreens.Do(action));

        // 버튼 → 화면 일(다른 화면 열기·닫기 …). 행동이 아니므로 Busy여도 돈다.
        protected void OnClick(string button, Action handler) => OnClick(Q<Button>(button), handler);
        protected void OnClick(Button button, Action handler) => Hook(button, handler);

        // 데이터 목록 ↔ 요소 목록(항목 id마다 item UXML 하나). 화면을 닫으면 저절로 정리된다.
        protected ElementList<TItem> List<TItem>(string container, VisualTreeAsset item, Func<IEnumerable<TItem>> items, Action<VisualElement, TItem> bind, params string[] keys) =>
            Own(new ElementList<TItem>(Q(container), item, items, bind, keys));

        // 닫을 때 정리할 것(구독·목록 …).
        protected T Own<T>(T disposable) where T : IDisposable
        {
            if (disposable != null) Owned.Add(disposable);
            return disposable;
        }

        internal readonly List<IDisposable> Owned = new List<IDisposable>();
        internal readonly List<(Button button, Action handler)> Clicks = new List<(Button, Action)>();

        void Hook(Button button, Action handler)
        {
            if (button == null) throw new ArgumentNullException(nameof(button));
            button.clicked += handler;
            Clicks.Add((button, handler));
        }
    }

    // 코드를 잇지 않은 화면(binding-path만 쓰는 HUD 등).
    public sealed class PlainScreen : GameScreen
    {
    }
}
