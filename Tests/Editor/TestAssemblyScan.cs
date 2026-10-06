using NUnit.Framework;

namespace GBible.Tests
{
    // Unity에서 테스트를 돌릴 때: 테스트 어셈블리의 형식([BindTo]·Mirror 생성 코드·키워드)도 훑게 한다.
    // 게임 플레이·에디터는 평소 테스트 어셈블리를 빼므로(GameAssemblies.Use(true)) 테스트 동안만 다시 넣고, 끝나면 되돌린다.
    // 보통은 GBible.Editor의 TestRuns(Test Runner 실행의 시작·끝 — 모든 테스트 어셈블리)가 이미 넣어 두므로 여기서는 바뀌는 것이 없다.
    // TestRuns가 꺼진 때(Test Framework 1.1 미만 → GBIBLE_TEST_FRAMEWORK 없음)에도 이 어셈블리의 테스트는 돌도록 남겨 둔다.
    [SetUpFixture]
    public sealed class TestAssemblyScan
    {
        bool _previous;
        bool _previousKeep;

        [OneTimeSetUp]
        public void Include()
        {
            _previous = GameAssemblies.SkipTests;
            _previousKeep = GameAssemblies.KeepTests;
            // 테스트가 게임 세션을 만들어도(GBibleRuntime이 Use(true)를 부른다) 도중에 빠지지 않게 고정한다.
            GameAssemblies.KeepTests = true;
            GameAssemblies.Use(skipTests: false);
        }

        [OneTimeTearDown]
        public void Restore()
        {
            GameAssemblies.KeepTests = _previousKeep;
            GameAssemblies.Use(_previous);
        }
    }
}
