using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// 코어는 Unity 없이(.NET 서버)도 돌아야 하므로 Unity 전용 어셈블리 API 대신 AppDomain을 쓴다.
#pragma warning disable UAC0005

namespace GBible
{
    // 게임 코드를 훑는 곳(Mirror 비교 코드 · [BindTo] · 키워드 · 행동 참조)이 볼 어셈블리.
    // 게임을 플레이할 때(GBibleRuntime)는 테스트 어셈블리(NUnit을 참조)를 뺀다 — 테스트용 형식이 게임에 섞여 경고를 내지 않게.
    // 테스트를 돌릴 때는 그대로 본다(SkipTests 거짓 — 테스트가 자기 형식을 훑어야 한다).
    public static class GameAssemblies
    {
        const string Core = "GBible.Core";

        public static bool SkipTests;

        // 불러온 어셈블리(동적 제외, SkipTests면 테스트 제외).
        public static IEnumerable<Assembly> All()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                if (SkipTests && IsTest(assembly)) continue;
                yield return assembly;
            }
        }

        // 코어 자신과 코어를 참조하는 어셈블리(게임 코드가 있을 수 있는 곳).
        public static IEnumerable<Assembly> UsingCore() =>
            All().Where(a => a.GetName().Name == Core || a.GetReferencedAssemblies().Any(r => r.Name == Core));

        public static Type[] TypesOf(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); }
        }

        static bool IsTest(Assembly assembly) =>
            assembly.GetReferencedAssemblies().Any(r => r.Name == "nunit.framework" || r.Name == "UnityEngine.TestRunner");
    }
}
