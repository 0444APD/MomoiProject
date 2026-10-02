using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GBible.Tests
{
    // 실제 플레이 모드: 런타임이 스스로 시작해 데이터베이스를 읽고, 샘플 행동이 도전과제까지 이어지는지.
    // (테스트 프로젝트의 샘플 데이터·GBIBLE_SAMPLES 심볼이 있을 때만 컴파일된다)
    public class RuntimeBootstrapTests
    {
        [UnityTest]
        public IEnumerator 플레이_시작_시_세션이_준비된다()
        {
            yield return null;
            var session = GBibleRuntime.Session;
            Assert.IsNotNull(session, "런타임이 자동으로 시작되어야 한다");
            RequireSampleData(session);
            Assert.IsNotEmpty(session.Stats.Definitions, "데이터베이스의 수치가 정의되어야 한다");
            Assert.IsNotEmpty(session.Achievements.Definitions, "데이터베이스의 도전과제가 정의되어야 한다");
            Assert.AreEqual(HistoryMode.Actions, session.Options.History, "샘플은 행동 단위 Undo를 쓴다");
        }

        [UnityTest]
        public IEnumerator 샘플_행동이_도전과제로_이어진다()
        {
            yield return null;
            var session = GBibleRuntime.Session;
            RequireSampleData(session);
            var attack = session.Debug.All.First(c => c.Group == "샘플" && c.Name == "오크 공격 ×10");
            var first = session.Achievements.Definitions.First(a => a.Id == "firstHunt");
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
        public IEnumerator Undo는_이번_판_수치를_되돌린다()
        {
            yield return null;
            var session = GBibleRuntime.Session;
            RequireSampleData(session);
            var runKills = session.Stats.Definitions.First(s => s.Scope == StatScope.Run).Id;
            var before = session.Stats.Get(runKills);
            var totalBefore = session.Data.Get(PlayerDataKeys.Stat(KillsTotal(session)));
            var historyBefore = session.UndoCount;

            session.Debug.All.First(c => c.Name == "오크 공격 ×10").Execute();
            Assert.AreEqual(historyBefore + 10, session.UndoCount);
            for (int i = 0; i < 10; i++) session.Undo();

            Assert.AreEqual(before, session.Stats.Get(runKills));
            Assert.AreEqual(totalBefore, session.Data.Get(PlayerDataKeys.Stat(KillsTotal(session))), "Undo한 행동은 누적에 남지 않는다");
        }

        [UnityTest]
        public IEnumerator 일시정지_배속_프레임_넘기기()
        {
            yield return null;
            var clock = GBibleRuntime.Session.Clock;

            GameTime.SetSpeed(2f);
            Assert.AreEqual(2f, UnityEngine.Time.timeScale);
            GameTime.Pause("테스트");
            GameTime.Pause("메뉴");
            Assert.AreEqual(0f, UnityEngine.Time.timeScale);
            // 멈춘 프레임은 이미 시작된 뒤라 끝까지 진행된다(그 프레임의 deltaTime은 멈추기 전 값). 다음 프레임부터 멈춘다.
            yield return null;
            var pausedTime = clock.Time;
            yield return null;
            Assert.AreEqual(pausedTime, clock.Time, "멈춘 동안 게임 시계도 멈춘다");

            GameTime.StepFrame();
            yield return null;
            yield return null;
            Assert.Greater(clock.Time, pausedTime, "1프레임은 진행한다");
            Assert.AreEqual(0f, UnityEngine.Time.timeScale, "넘긴 뒤 다시 멈춘다");

            GameTime.Resume("테스트");
            Assert.IsTrue(GameTime.IsPaused, "다른 이유(메뉴)가 남아 있으면 계속 멈춘다");
            GameTime.Resume("메뉴");
            Assert.AreEqual(2f, UnityEngine.Time.timeScale, "재개하면 배속이 돌아온다");
            GameTime.SetSpeed(1f);
        }
        // 샘플 데이터(메뉴 샘플 › 샘플 데이터 만들기)가 없는 프로젝트에서는 건너뛴다.
        static void RequireSampleData(GameSession session)
        {
            if (session == null || !session.Stats.Definitions.Any(s => s.Id == "killsTotal") || !session.Achievements.Definitions.Any(a => a.Id == "firstHunt"))
                Assert.Ignore("샘플 데이터가 없습니다: Tests › 샘플 › 샘플 데이터 만들기 후 다시 실행하세요.");
        }

        static string KillsTotal(GameSession session) =>
            session.Stats.Definitions.First(s => s.Id == "killsTotal").Id;
    }
}
