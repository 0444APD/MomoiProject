using System;

namespace GBible
{
    // 코드 줄이기(Managed Code Stripping)가 지우지 않게 하는 표시. Unity 링커(UnityLinker)는 이름이 정확히 PreserveAttribute인 어트리뷰트를
    // 이름공간·어셈블리와 상관없이 [Preserve]로 본다 — 코어는 UnityEngine을 참조하지 않으므로(.NET 서버에서도 컴파일) 같은 이름의 것을 둔다.
    // 생성기(MirrorWeaver)가 리플렉션으로만 찾는 등록 메서드(__gb_register)에 붙인다. 등록 메서드가 나머지 생성 메서드를 가리키므로(ldftn) 함께 남는다.
    // internal: 게임 코드가 using GBible과 using UnityEngine.Scripting을 함께 써도 [Preserve]가 모호해지지 않게(게임 코드는 UnityEngine.Scripting.Preserve를 쓴다).
    //   생성기는 IL에 직접 붙이므로 공개 여부와 상관없고, 링커는 이름만 본다.
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
    sealed class PreserveAttribute : Attribute
    {
    }
}
