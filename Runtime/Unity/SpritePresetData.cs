namespace GBible.Data
{
    // 예전 형식(2026-10-07 전): 크기마다 파일 하나였던 스프라이트 규격. 지금은 틀(SpriteFrameData) 하나에 표로 적는다.
    // 남은 파일은 에디터를 열 때 그 그룹(지금의 틀)의 크기 줄로 옮기고 파일은 MomoiProjectBackups로 옮긴다(SpriteSpecEditor.MigrateLegacy).
    // 그 일을 하려고 형식만 남긴다(만들기 메뉴·DB 종류가 아니다 — DB 탭에 보이지 않게 HideInDatabase).
    [HideInDatabase]
    public sealed class SpritePresetData : GameData
    {
        public string group = "";
        public int width = 256;
        public int height = 256;
    }
}
