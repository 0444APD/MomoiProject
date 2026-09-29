using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GameSystems.Tests
{
    // 실제 플레이 모드: 런타임이 스스로 시작해 데이터베이스를 읽고, 샘플 행동이 도전과제까지 이어지는지.
    // (테스트 프로젝트의 샘플 데이터·GAME_SYSTEMS_SAMPLES 심볼이 있을 때만 컴파일된다)
    public class RuntimeBootstrapTests
    {
        [UnityTest]
        public IEnumerator 플레이_시작_시_세션이_준비된다()
        {
            yield return null;
            var session = GameSystemsRuntime.Session;
            Assert.IsNotNull(session, "런타임이 자동으로 시작되어야 한다");
            Assert.IsNotEmpty(session.Stats.Definitions, "데이터베이스의 수치가 정의되어야 한다");
            Assert.IsNotEmpty(session.Achievements.Definitions, "데이터베이스의 도전과제가 정의되어야 한다");
            Assert.AreEqual(HistoryMode.Actions, session.Options.History, "샘플은 행동 단위 무르기를 쓴다");
        }

        [UnityTest]
        public IEnumerator 샘플_행동이_도전과제로_이어진다()
        {
            yield return null;
            var session = GameSystemsRuntime.Session;
            var attack = session.Debug.All.First(c => c.Group == "샘플" && c.Name == "오크 공격 ×10");
            var first = session.Achievements.Definitions.First(a => a.DisplayName == "첫 사냥");
            session.Achievements.ResetProgress(first.Id);

            for (int i = 0; i < 5 && session.Stats.Get(KillsTotal(session)) < 1; i++)
            {
                attack.Execute();
                yield return null;
            }

            Assert.GreaterOrEqual(session.Stats.Get(KillsTotal(session)), 1);
            Assert.IsTrue(session.Achievements.IsUnlocked(first.Id));
            Assert.IsTrue(session.Journal.Entries.Any(e => e.Kind == JournalKind.Reward));
        }

        [UnityTest]
        public IEnumerator 무르기는_이번_판_수치를_되돌린다()
        {
            yield return null;
            var session = GameSystemsRuntime.Session;
            var runKills = session.Stats.Definitions.First(s => s.Scope == StatScope.Run).Id;
            var before = session.Stats.Get(runKills);
            var totalBefore = session.State.Get(StateKeys.Stat(KillsTotal(session)));
            var historyBefore = session.UndoCount;

            session.Debug.All.First(c => c.Name == "오크 공격 ×10").Execute();
            Assert.AreEqual(historyBefore + 10, session.UndoCount);
            for (int i = 0; i < 10; i++) session.Undo();

            Assert.AreEqual(before, session.Stats.Get(runKills));
            Assert.AreEqual(totalBefore, session.State.Get(StateKeys.Stat(KillsTotal(session))), "무른 행동은 누적에 남지 않는다");
        }

        static string KillsTotal(GameSession session) =>
            session.Stats.Definitions.First(s => s.DisplayName == "처치 수 (누적)").Id;
    }
}
