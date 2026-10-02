using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using GBible.EditorTools;
using GBible.EditorTools.UI;
using NUnit.Framework;

namespace GBible.Tests
{
    // 계약 테스트: 기능을 추가하면 도움말과 설정 단계도 함께 있어야 한다.
    // 여기서 실패하면 BuiltInHelp(또는 그 기능의 IHelpProvider)·BuiltInSetup에 항목을 추가하세요.
    [Ignore("도움말·시작하기 보류 중(HelpFeature.Enabled = false)")]
    public class HelpCoverageTests
    {
        static bool IsLibrary(Type type) => type.Assembly.GetName().Name.StartsWith("GBible.", StringComparison.Ordinal) &&
                                            !type.Assembly.GetName().Name.StartsWith("GBible.Samples", StringComparison.Ordinal) &&
                                            !type.Assembly.GetName().Name.StartsWith("GBible.Tests", StringComparison.Ordinal);

        [Test]
        public void 기본_도감_종류는_직접_쓴_도움말이_있다()
        {
            var missing = DataTypes.All.Where(i => IsLibrary(i.Type))
                .Where(i => HelpRegistry.Find(HelpIds.Data(i.Type)) is var topic && (topic == null || topic.Generated))
                .Select(i => i.Type.FullName).ToList();
            Assert.IsEmpty(missing, "도움말이 없는 도감 종류:\n" + string.Join("\n", missing));
        }

        [Test]
        public void 모니터_탭마다_도움말이_있다()
        {
            var missing = Enumerable.Range(0, MonitorWindow.TabCount).Select(MonitorWindow.TabKey)
                .Where(key => HelpRegistry.Find(HelpIds.Monitor(key)) == null).ToList();
            Assert.IsEmpty(missing, "도움말이 없는 모니터 탭: " + string.Join(", ", missing));
        }

        [Test]
        public void 배치_블록은_설명이_있다()
        {
            var missing = GsLayoutBlock.Registry.Values.Where(r => string.IsNullOrWhiteSpace(r.info.Description)).Select(r => r.info.Id).ToList();
            Assert.IsEmpty(missing, "[LayoutBlock]에 Description이 없는 블록: " + string.Join(", ", missing));
            foreach (var id in GsLayoutBlock.Registry.Keys)
                Assert.IsNotNull(HelpRegistry.Find(HelpIds.Layout(id)), id);
        }

        [Test]
        public void 조건_보상_필터_블록은_설명이_있다()
        {
            var types = new[] { typeof(Condition), typeof(Reward), typeof(ValueFilter) }.SelectMany(BlockTypes.ConcreteTypes).Where(IsLibrary).ToList();
            var missing = types.Where(t => string.IsNullOrWhiteSpace(BlockTypes.Description(t))).Select(t => t.FullName).ToList();
            Assert.IsEmpty(missing, "[BlockInfo]에 Description이 없는 블록:\n" + string.Join("\n", missing));
            foreach (var type in types)
                Assert.IsNotNull(HelpRegistry.Find(HelpIds.Block(type)), type.FullName);
        }

        [Test]
        public void 모든_도움말_항목은_제목_요약_본문이_있고_연결이_살아있다()
        {
            var ids = new HashSet<string>();
            foreach (var topic in HelpRegistry.All)
            {
                Assert.IsTrue(ids.Add(topic.Id), $"중복 id: {topic.Id}");
                Assert.IsFalse(string.IsNullOrWhiteSpace(topic.Title), $"{topic.Id}: 제목 없음");
                Assert.IsFalse(string.IsNullOrWhiteSpace(topic.Summary), $"{topic.Id}: 요약 없음");
                Assert.IsFalse(string.IsNullOrWhiteSpace(topic.Body), $"{topic.Id}: 본문 없음");
                Assert.IsFalse(string.IsNullOrWhiteSpace(topic.Category), $"{topic.Id}: 분류 없음");
                foreach (var related in topic.Related ?? Array.Empty<string>())
                    Assert.IsNotNull(HelpRegistry.Find(related), $"{topic.Id}: 없는 항목을 가리킴 → {related}");
            }
        }

        [Test]
        public void 설정_단계는_설명과_도움말_연결이_있다()
        {
            var steps = SetupRegistry.All;
            Assert.IsNotEmpty(steps);
            foreach (var step in steps)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(step.Title), $"{step.Id}: 제목 없음");
                Assert.IsFalse(string.IsNullOrWhiteSpace(step.Summary), $"{step.Id}: 요약 없음");
                Assert.IsFalse(string.IsNullOrWhiteSpace(step.Body), $"{step.Id}: 설명 없음");
                Assert.IsNotNull(HelpRegistry.Find(step.HelpTopic), $"{step.Id}: 도움말 연결 없음 → {step.HelpTopic}");
                // 자동 판정이 있으면 예외 없이 상태를 돌려줘야 한다.
                Assert.AreNotEqual(SetupState.Warning, SetupRegistry.StateOf(step).state, $"{step.Id}: 상태 확인 실패");
            }
            Assert.AreEqual(steps.Count, steps.Select(s => s.Order).Distinct().Count(), "설정 단계 순서(Order)가 겹칩니다.");
        }

        [Test]
        public void 설정이_필요한_기본_도감_종류는_설정_단계에서_다룬다()
        {
            // 도움말만 있고 시작하기에서 빠진 종류가 없게: 각 기본 도감 종류는 어떤 단계의 도움말 연결이나 관련 항목으로 이어져야 한다.
            var reachable = new HashSet<string>();
            foreach (var step in SetupRegistry.All)
            {
                reachable.Add(step.HelpTopic);
                var topic = HelpRegistry.Find(step.HelpTopic);
                if (topic?.Related != null) reachable.UnionWith(topic.Related);
            }
            var missing = DataTypes.All.Where(i => IsLibrary(i.Type)).Select(i => HelpIds.Data(i.Type)).Where(id => !reachable.Contains(id)).ToList();
            Assert.IsEmpty(missing, "시작하기에서 이어지지 않는 도감 종류:\n" + string.Join("\n", missing));
        }
    }
}
