using UnityEngine;

namespace GameSystems
{
    // 소리 채널. 음량은 채널마다 따로, 실제 음량 = 전체 × 채널 × 소리 하나의 음량.
    //  · 브금·목소리: 한 번에 하나(직렬). 바뀌면 이전 소리가 페이드 아웃, 새 소리가 페이드 인.
    //  · 효과음·UI: 튼 만큼 모두 동시에(병렬).
    public enum AudioChannel
    {
        [InspectorName("전체")] Master,
        [InspectorName("브금")] Bgm,
        [InspectorName("목소리")] Voice,
        [InspectorName("효과음")] Sfx,
        [InspectorName("UI")] Ui,
    }
}
