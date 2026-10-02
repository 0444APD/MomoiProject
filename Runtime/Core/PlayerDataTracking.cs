using System;

namespace GBible
{
    // 플레이어 데이터 클래스 표시(선택). [Serializable]과 같은 뜻으로, Mirror 생성기가 비교 코드를 만든다.
    // 붙이지 않아도 [Serializable] 클래스면 같다. 예전 위빙 방식에서 쓰던 표시라 남겨 둔다.
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true)]
    public sealed class PlayerDataAttribute : Attribute
    {
    }
}
