using NUnit.Framework;

namespace GBible.Tests
{
    // Unity에서 테스트를 돌릴 때: 테스트 어셈블리의 형식([BindTo]·Mirror 생성 코드·키워드)도 훑게 한다.
    // 게임 플레이·에디터는 평소 테스트 어셈블리를 빼므로(GameAssemblies.Use(true)) 테스트 동안만 다시 넣고, 끝나면 되돌린다.
    [SetUpFixture]
    public sealed class TestAssemblyScan
    {
        bool _previous;

        [OneTimeSetUp]
        public void Include()
        {
            _previous = GameAssemblies.SkipTests;
            GameAssemblies.Use(skipTests: false);
        }

        [OneTimeTearDown]
        public void Restore() => GameAssemblies.Use(_previous);
    }
}
