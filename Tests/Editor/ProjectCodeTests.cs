using System;
using System.Linq;
using GBible.Data;
using GBible.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace GBible.Tests
{
    public sealed class ProjectCodeTests
    {
        [Test]
        public void 라이브러리_유니티_플러그인은_내_코드가_아니다()
        {
            var assemblies = ProjectCode.Assemblies;
            Assert.IsFalse(assemblies["GBible.Core"].included);
            Assert.IsFalse(assemblies["GBible.Unity"].included);
            foreach (var pair in assemblies.Where(p => p.Key.StartsWith("Unity", StringComparison.Ordinal) || p.Key.EndsWith("-firstpass", StringComparison.Ordinal)))
                Assert.IsFalse(pair.Value.included, pair.Key);
            Debug.Log("[내 코드 범위] " + string.Join(", ", assemblies.Select(p => $"{p.Key}={(p.Value.included ? "O" : "X:" + p.Value.reason)}")));
        }

        [Test]
        public void 기능_스크립트와_라이브러리_형식은_데이터_클래스가_아니다()
        {
            Assert.IsFalse(ProjectCode.IsDataClass(typeof(SpriteFrameData)), "라이브러리 기본 DB 종류");
            Assert.IsFalse(ProjectCode.IsDataClass(typeof(GameAudio)), "라이브러리 정적 클래스");
            Assert.IsFalse(ProjectCode.IsDataClass(typeof(Vector3)), "Unity");
            var dg = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("DG.Tweening.Tween")).FirstOrDefault(t => t != null);
            if (dg != null) Assert.IsFalse(ProjectCode.IsProjectType(dg), "DOTween");
        }

        [Test]
        public void 고르는_목록에는_내_코드만_있다()
        {
            var basics = new[] { typeof(double), typeof(int), typeof(long), typeof(float), typeof(bool), typeof(string), typeof(DateTime), typeof(TimeSpan) };
            var storable = StorableTypes.All().Where(t => !basics.Contains(t)).ToList();
            foreach (var type in storable.Where(t => GameFeatures.Of(t) == null)) Assert.IsTrue(ProjectCode.IsProjectType(type), type.FullName);
            // 켜 둔 선택 기능의 클래스(라이브러리)는 기능 그룹으로 따로 들어온다.
            foreach (var type in ClassTypes.All().Where(t => ClassTypes.GroupOf(t) != "이벤트" && GameFeatures.Of(t) == null))
            {
                Assert.IsTrue(ProjectCode.IsDataClass(type), type.FullName);
                Assert.IsFalse(typeof(MonoBehaviour).IsAssignableFrom(type), type.FullName);
            }
            var events = ProjectCode.Events().ToList();
            CollectionAssert.Contains(events, typeof(AchievementUnlocked), "라이브러리 기본 이벤트");
            CollectionAssert.DoesNotContain(events, typeof(TestKill), "테스트 이벤트는 빠진다");
            Debug.Log("[내 코드 범위] 목록: " + string.Join(", ", ClassTypes.All().Select(t => t.Name)));
        }
    }
}
