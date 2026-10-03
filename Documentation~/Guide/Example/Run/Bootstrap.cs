// 카드 던전 — 시작 설정. 세션이 만들어지기 전(Configuring)과 만들어진 뒤(Started)에 할 일.
using System.Linq;
using GBible;
using GBible.Ids;
using UnityEngine;

namespace CardDungeon
{
    public static class Bootstrap
    {
        // GBibleRuntime이 첫 씬을 읽기 전에 세션을 만들므로, 그보다 먼저(AfterAssembliesLoaded) 구독한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Hook()
        {
            GBibleRuntime.Configuring += Configure;
            GBibleRuntime.Started += Started;
        }

        static void Configure(SessionOptions options)
        {
            options.History = HistoryMode.Actions;   // 턴제: 행동 하나씩 Undo
            options.HistoryLimit = 200;              // 오래 플레이해도 Undo 기록이 끝없이 늘지 않게
            if (Debug.isDebugBuild) options.Seed = 20261003;   // 개발 빌드는 시드 고정(버그 재현)
        }

        static void Started(GameSession session)
        {
            // "run"·"profile"은 도감 › 플레이어 데이터에서 만든다. 도감에 없을 때만 코드로 정의(테스트·서버용).
            if (!session.Data.IsDefined(GameIds.PlayerData.Run.Id)) session.Data.Define(GameIds.PlayerData.Run.Id, DataZone.Session, new RunState(), "판");
            if (!session.Data.IsDefined(GameIds.PlayerData.Profile.Id)) session.Data.Define(GameIds.PlayerData.Profile.Id, DataZone.Meta, new Profile(), "프로필");

            // 개발용 명령(모니터 › 명령 탭의 버튼).
            if (Debug.isDebugBuild)
            {
                session.Debug.Register("카드 던전", "골드 +100", () => session.Do(new DeveloperEditAction { label = "골드 +100", Edit = () => session.Game.Data().Run.gold += 100 }));
                session.Debug.Register("카드 던전", "적 모두 처치", () => session.Do(new DeveloperEditAction
                {
                    label = "적 모두 처치",
                    Edit = () =>
                    {
                        foreach (var enemy in Rules.LivingEnemies(session.Game).ToArray()) Rules.Damage(session.Game, session.Game.Data().Run.hero, enemy, 9999);
                    },
                }));
            }
        }
    }
}
