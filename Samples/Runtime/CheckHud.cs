using UnityEngine.UIElements;

namespace GBible.Samples
{
    // 확인용 HUD의 코드(DB › 화면 checkHud). AI가 화면을 만드는 방식 그대로: UXML(Samples/Screens) + DB 항목 + [BindTo]로 이은 GameScreen.
    // 값 칸(턴·에너지·가진 전리품)은 UXML의 binding-path가, 버튼·유닛 목록은 여기서 잇는다.
    [BindTo(CheckIds.Hud)]
    public sealed class CheckHud : GameScreen
    {
        protected override void OnOpen()
        {
            OnClick("hit", () => new CheckHit { target = CheckRules.Run(Game).Foe });
            OnClick("pay-hit", () => new CheckPayHit { target = CheckRules.Run(Game).Foe });   // 에너지가 모자라면 알림
            OnClick("end-turn", () => new CheckEndTurn());
            OnClick("ask", () => new CheckAsk());                                              // 기본 고르기 창이 뜬다
            OnClick("settings", () => GameScreens.Open(GameScreens.Settings));
            OnClick("close", Close);
            List<CheckUnit>("units", Item("CheckUnitItem"), () => CheckRules.Run(Game).units, (element, unit) =>
            {
                element.Q<Label>("name").text = unit.name;
                var hp = element.Q<ProgressBar>("hp");
                hp.highValue = unit.maxHp;
                hp.value = unit.hp;
                hp.title = $"{unit.hp} / {unit.maxHp}";
            }, CheckIds.Run);
        }
    }
}
