using System;
using UnityEngine;

namespace GBible
{
    // 테스트용 현실 시각 옮기기(개발 빌드 · 에디터에서만 Game.Now에 얹힌다 — GBibleRuntime이 세션의 RealClock 위에 더한다).
    // 오프라인 보상 · 일일 초기화 · 날짜 넘김을 실제로 기다리지 않고 시험한다: 개발 명령 '시간 › 현실 시각 +1시간 / +1일 / 지금으로'.
    // 테스트 상황을 불러오면 저장한 순간의 현실 시각으로 맞춘다(TestScenarios) — 며칠 뒤 열어도 꺼 둔 시간 · 날짜가 같다.
    // 플레이를 시작할 때마다 0으로 돌아간다. 규칙은 이것을 직접 읽지 않는다(Game.Now · Game.Day만).
    public static class TestClock
    {
        public static TimeSpan Offset { get; private set; }

        // 지금 Game.Now가 가리키는 현실 시각(UTC — 옮긴 시간 포함).
        public static DateTime Now => DateTime.UtcNow + Offset;

        public static void Shift(TimeSpan by) => Offset += by;

        public static void SetNow(DateTime utc) => Offset = utc.ToUniversalTime() - DateTime.UtcNow;

        public static void Reset() => Offset = TimeSpan.Zero;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Offset = TimeSpan.Zero;
    }
}
