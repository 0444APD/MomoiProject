using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.Data
{
    // 화면이 뜨는 층. 위의 층이 아래 층을 덮는다.
    public enum ScreenLayer
    {
        [InspectorName("바탕(HUD)")] Base,
        [InspectorName("창(메뉴)")] Window,
        [InspectorName("팝업(위에 뜨는 창)")] Popup,
        [InspectorName("알림(입력을 막지 않음)")] Toast,
    }

    // DB › 화면: UI Toolkit 화면 하나 — UXML, 뜨는 층, 아래 입력을 막나, 뒤로(Esc)로 닫히나, 어느 씬에서 저절로 열리나.
    // 코드는 [BindTo(GameIds.Screen.X)] class X : GameScreen. 값을 보이기만 하는 칸은 코드 없이 UXML의 binding-path로(ScreenBinding).
    // 여는 법: GameScreens.Open(GameIds.Screen.X) · 열 씬 · 게임이 켜지면 열기.
    [Database("화면", Group = "DB", Order = 50, IdPrefix = "screen")]
    [CreateAssetMenu(menuName = "MomoiProject/화면", fileName = "NewScreen")]
    public sealed class ScreenData : GameData
    {
        [FieldLabel("UXML"), Tooltip("이 화면의 UXML(Assets/UI/Screens/…)")] public VisualTreeAsset layout;
        [FieldLabel("더할 USS"), Tooltip("이 화면에만 붙는 USS. 모든 화면에 붙는 것은 DB › 화면 설정의 테마")] public List<StyleSheet> styles = new List<StyleSheet>();
        [FieldLabel("목록 칸 UXML"), Tooltip("목록 한 칸의 UXML(Assets/UI/Items/…). 코드는 Item(\"파일 이름\")으로 찾는다. 화면 UXML에 <ui:Template>로 적은 것도 찾는다")] public List<VisualTreeAsset> items = new List<VisualTreeAsset>();
        [FieldLabel("층")] public ScreenLayer layer = ScreenLayer.Window;
        [FieldLabel("아래 입력 막기"), Tooltip("창: 켜면 열린 동안 아래 층 화면을 누를 수 없습니다. 팝업은 늘 막고, 바탕·알림은 막지 않습니다")] public bool blockInput = true;
        [FieldLabel("뒤로(Esc)로 닫기")] public bool closeOnBack = true;
        [FieldLabel("열린 동안 게임 시간 멈춤")] public bool pauseTime;
        [FieldLabel("열 씬"), Tooltip("이 씬이 열리면 저절로 열고, 다른 씬이 열리면 닫습니다. 비우면 코드(GameScreens.Open)나 '게임이 켜지면 열기'로")] public List<string> scenes = new List<string>();
        [FieldLabel("게임이 켜지면 열기")] public bool openOnStart;

        public override void ValidateData(IValidationContext context)
        {
            if (layout == null) context.Error("UXML이 없습니다.");
            if (openOnStart && scenes.Count > 0) context.Warning("'게임이 켜지면 열기'와 '열 씬'을 함께 켰습니다. 열 씬이 아닌 씬에 가면 닫힙니다.");
        }
    }
}
