using System;
using System.Collections.Generic;
using UnityEngine;

namespace GBible
{
    // 게임 시간 조절: 일시정지, 배속·슬로우, 프레임 단위 넘기기(개발용).
    // Unity의 Time.timeScale을 바꾸므로 게임 안의 모든 요소가 함께 영향을 받는다:
    // Update의 Time.deltaTime, 물리(FixedUpdate), 애니메이터(Normal 모드), 파티클, 코루틴의 WaitForSeconds,
    // 트윈(기본 설정), 게임 시계(session.Clock)와 스냅샷 간격.
    //
    // 영향을 받지 않는 것(일부러 그렇게 만든 것): Time.unscaledDeltaTime을 쓰는 코드, 애니메이터 Unscaled Time 모드,
    // WaitForSecondsRealtime, 오디오(피치), UI 연출. 메뉴·일시정지 화면처럼 멈추면 안 되는 것만 이쪽을 쓴다.
    //
    // 일시정지는 이유별로 겹칠 수 있다(예: 메뉴와 다이얼로그 창이 동시에 멈춤 → 둘 다 풀려야 재개).
    //
    // 고정 틱: 세션은 배속을 모르고 deltaTime만 받으므로, 한 프레임에 따라잡는 틱 한도(GameSession.HostSpeed)를 배속에 맞춰 준다
    // (×16이면 보통 프레임에도 틱이 16배 들어온다 — 한도가 그대로면 규칙 시간만 ×8로 늦어진다).
    public static class GameTime
    {
        public const float MinSpeed = 0.05f;
        public const float MaxSpeed = 16f;

        static readonly HashSet<string> PauseReasons = new HashSet<string>(StringComparer.Ordinal);
        static float _speed = 1f;
        static float _baseFixedDelta = -1f;
        static int _stepFrames;
        static int _stepRequestedFrame = -1;

        // 상태가 바뀌었을 때(일시정지·배속·프레임 넘기기).
        public static event Action Changed;

        public static bool IsPaused => PauseReasons.Count > 0;
        public static IEnumerable<string> PausedBy => PauseReasons;
        public static float Speed => _speed;
        public static bool IsStepping => _stepFrames > 0;

        public static void Pause(string reason = "게임")
        {
            if (PauseReasons.Add(reason ?? "게임")) Apply();
        }

        public static void Resume(string reason = "게임")
        {
            if (PauseReasons.Remove(reason ?? "게임")) Apply();
        }

        // 모든 이유의 일시정지를 푼다(개발 도구·긴급 복구용).
        public static void ResumeAll()
        {
            if (PauseReasons.Count == 0) return;
            PauseReasons.Clear();
            Apply();
        }

        public static void TogglePause(string reason = "게임")
        {
            if (PauseReasons.Contains(reason ?? "게임")) Resume(reason);
            else Pause(reason);
        }

        // 배속. 1 = 보통, 0.5 = 슬로우, 2 = 두 배. 일시정지 중에 바꾸면 재개할 때 적용된다.
        public static void SetSpeed(float speed)
        {
            speed = Mathf.Clamp(speed, MinSpeed, MaxSpeed);
            if (Mathf.Approximately(speed, _speed)) return;
            _speed = speed;
            Apply();
        }

        // 개발용: 일시정지 상태에서 지정한 프레임 수만큼만 진행한다(현재 배속 적용).
        public static void StepFrame(int frames = 1)
        {
            if (!IsPaused || frames <= 0) return;
            _stepFrames += frames;
            // 요청한 그 프레임은 이미 시작됐으므로 다음 프레임부터 센다.
            _stepRequestedFrame = Time.frameCount;
            Apply();
        }

        // 호스트가 매 프레임 끝에 호출한다. 넘기기 프레임을 하나씩 소모하고, 고정 틱 한도를 배속에 맞춘다(새로 만든 세션도 다음 프레임부터).
        internal static void OnFrameEnd()
        {
            ShareSpeed();
            if (_stepFrames <= 0 || Time.frameCount == _stepRequestedFrame) return;
            _stepFrames--;
            if (_stepFrames == 0) Apply();
        }

        static void Apply()
        {
            if (_baseFixedDelta < 0) _baseFixedDelta = Time.fixedDeltaTime;
            var scale = IsPaused && _stepFrames == 0 ? 0f : _speed;
            Time.timeScale = scale;
            // 슬로우에서도 물리가 뚝뚝 끊기지 않게 물리 간격을 배속에 맞춘다(빠르게 할 때는 기본 간격 유지).
            Time.fixedDeltaTime = scale > 0f && scale < 1f ? _baseFixedDelta * scale : _baseFixedDelta;
            ShareSpeed();
            Changed?.Invoke();
        }

        // 이 프로그램이 시간을 흘리는 세션 모두에 배속을 알린다: 이 화면의 세션 · 시험 모드·방장의 서버 세션 · 매칭 서버의 방.
        // 바뀐 배속은 다음 프레임의 deltaTime부터 들어오므로 프레임 끝에 맞춰 두면 된다.
        static void ShareSpeed()
        {
            var main = GBibleRuntime.Session;
            if (main != null) main.HostSpeed = _speed;
            var server = GameNet.Server?.Session;
            if (server != null) server.HostSpeed = _speed;
            var rooms = GameNet.Hub?.Rooms;
            if (rooms == null) return;
            for (var i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i]?.Session;
                if (room != null) room.HostSpeed = _speed;
            }
        }

        // 플레이 시작·종료 시 기본값으로(에디터에서 도메인 리로드를 꺼도 이전 플레이 상태가 남지 않게).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Reset()
        {
            PauseReasons.Clear();
            _speed = 1f;
            _stepFrames = 0;
            if (_baseFixedDelta > 0) Time.fixedDeltaTime = _baseFixedDelta;
            _baseFixedDelta = -1f;
            Time.timeScale = 1f;
            Changed = null;
        }
    }
}
