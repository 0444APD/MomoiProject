using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.Data
{
    // DB › 화면 설정(항목 하나): 모든 화면이 함께 쓰는 Panel Settings·테마 USS·글꼴, 기본 화면을 저절로 쓸지.
    // 메뉴 MomoiProject › 화면 › 화면 준비가 Panel Settings·테마 파일과 이 항목을 만든다.
    [Database("화면 설정", Group = "선언", Order = 7, IdPrefix = "screens")]
    [CreateAssetMenu(menuName = "MomoiProject/화면 설정", fileName = "ScreenSettings")]
    public sealed class ScreenSettingsData : GameData
    {
        [FieldLabel("Panel Settings"), Tooltip("UI Toolkit 패널 설정(화면 크기 맞춤·기본 테마). 화면 준비 메뉴가 Assets/UI/PanelSettings.asset을 만든다")] public PanelSettings panelSettings;
        [FieldLabel("테마 USS"), Tooltip("모든 화면에 붙는 USS(색·글자 크기·버튼 모양 변수). 기본 화면도 이 변수를 쓴다")] public List<StyleSheet> theme = new List<StyleSheet>();
        [FieldLabel("글꼴"), Tooltip("모든 화면의 글꼴. 한글을 보이려면 한글이 든 글꼴(TTF·OTF)을 넣는다")] public Font font;
        [FieldLabel("고르기 창 저절로"), Tooltip("선택 대기(Game.Ask·선택 요구·가중치 표에서 고르기)가 생기면 기본 고르기 창을 엽니다. 프로젝트가 고르기 화면을 따로 만들면 끈다")] public bool choiceWindow = true;
        [FieldLabel("고르는 동안 게임 시간 멈춤"), Tooltip("기본 고르기 창이 떠 있는 동안 게임 시간(Time.timeScale — 예약·초 지속·고정 틱·연출)을 멈춥니다. 실시간 게임의 레벨업 선택에서 켠다. 턴제는 끈다(연출이 멈추지 않게)")] public bool pauseWhileChoosing;
        [FieldLabel("실패를 알림으로"), Tooltip("화면 버튼이 보낸 행동이 플레이어용 실패(Game.Fail·비용 모자람 …)로 끝나면 그 글을 알림으로 띄웁니다")] public bool failureToast = true;
        [FieldLabel("Esc로 일시정지 메뉴"), Tooltip("닫을 화면이 없을 때 뒤로(Esc)를 누르면 기본 일시정지 메뉴를 엽니다")] public bool pauseMenu;

        public override void ValidateData(IValidationContext context)
        {
            if (panelSettings == null) context.Warning("Panel Settings가 없습니다. 게임이 켜질 때 테마 없는 임시 설정을 만듭니다(메뉴 MomoiProject › 화면 › 화면 준비).");
            if (theme.Contains(null)) context.Error("테마 USS에 빈 칸이 있습니다.");
        }
    }
}
