using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GBible.EditorTools
{
    // 빌드 직전: 코드 줄이기(Player Settings › Managed Stripping Level)가 Minimal보다 세면(Low·Medium·High) 게임 코드를 지킬 link.xml을 본다.
    // 줄이기는 리플렉션으로만 찾는 게임 코드([BindTo] 함수·Reward·화면 코드·DB가 이름으로 가리키는 행동·이벤트)를 지울 수 있고,
    // 그러면 빌드에서만 조용히 망가진다(행동이 서버에서 "행동이 아닙니다"로 거절·이벤트 빠짐·화면이 동작하지 않음).
    // GBible 자신의 생성 코드(Mirror 등록 메서드)는 [Preserve]로 지켜지므로 여기서는 게임 어셈블리만 본다. 빌드는 막지 않고 경고만 한다.
    // 링크 파일은 시작 설정이 만든다(StartSetup.EnsureLinkXml — Assets/MomoiProject/link.xml).
    sealed class LinkXmlBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var level = StrippingLevelOf(report);
            if (level == null || !StripsGameCode(level.Value)) return;
            var games = UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.PlayerWithoutTestAssemblies)
                .Select(a => a.name).Where(n => n.StartsWith(StartSetup.Prefix + ".", StringComparison.Ordinal));
            var problem = Check(StartSetup.LinkXmlPath, games);
            if (problem != null) Debug.LogWarning($"[MomoiProject] Managed Stripping Level이 {level.Value}입니다. {problem}");
        }

        // Minimal은 클래스 라이브러리·UnityEngine만 줄이고 나머지 어셈블리는 그대로 둔다(Disabled는 줄이지 않는다). Low부터 게임 코드도 줄인다.
        internal static bool StripsGameCode(ManagedStrippingLevel level) =>
            level == ManagedStrippingLevel.Low || level == ManagedStrippingLevel.Medium || level == ManagedStrippingLevel.High;

        // 링크 파일의 문제(없으면 null): 파일이 없음 · 읽을 수 없음 · 빌드에 들어가는 게임 어셈블리(games)가 빠짐.
        internal static string Check(string path, IEnumerable<string> games)
        {
            if (!File.Exists(path))
                return $"{path}가 없어 리플렉션으로만 찾는 게임 코드([BindTo] 함수·Reward·화면 코드·DB가 이름으로 가리키는 행동·이벤트)가 지워질 수 있습니다(빌드에서만 조용히 망가짐). " +
                       "에디터를 다시 열거나 MomoiProject › 개발 › 시작 설정에서 적용하면 만들어집니다.";
            HashSet<string> listed;
            try { listed = Listed(File.ReadAllText(path)); }
            catch (XmlException e) { return $"{path}를 읽지 못했습니다({e.Message}). 고치거나, 지운 뒤 에디터를 다시 여세요(다시 만들어집니다)."; }
            var missing = games.Where(name => !listed.Contains(name)).Distinct().ToList();
            if (missing.Count == 0) return null;
            return $"{path}에 없는 게임 어셈블리: {string.Join(", ", missing)} — 그 어셈블리의 리플렉션으로 찾는 코드가 지워질 수 있습니다. " +
                   $"파일에 <assembly fullname=\"{missing[0]}\" preserve=\"all\" /> 줄을 더하세요.";
        }

        // link.xml에 적힌 어셈블리 이름들.
        static HashSet<string> Listed(string xml)
        {
            var document = new XmlDocument();
            document.LoadXml(xml);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlNode node in document.GetElementsByTagName("assembly"))
                if (node.Attributes?["fullname"]?.Value is { } name) names.Add(name);
            return names;
        }

        // 이번 빌드 대상의 줄이기 단계(전용 서버 빌드는 Server 대상의 값). 알 수 없는 대상이면 null(검사하지 않는다).
        static ManagedStrippingLevel? StrippingLevelOf(BuildReport report)
        {
            try
            {
                var group = report.summary.platformGroup;
                var target = group == BuildTargetGroup.Standalone && report.summary.GetSubtarget<StandaloneBuildSubtarget>() == StandaloneBuildSubtarget.Server
                    ? NamedBuildTarget.Server
                    : NamedBuildTarget.FromBuildTargetGroup(group);
                return PlayerSettings.GetManagedStrippingLevel(target);
            }
            catch (Exception)
            {
                return null; // 이 에디터가 모르는 플랫폼
            }
        }
    }
}
