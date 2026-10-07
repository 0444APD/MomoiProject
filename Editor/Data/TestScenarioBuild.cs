using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace GBible.EditorTools
{
    // 개발 빌드에 테스트 상황을 같이 넣는다: Assets/MomoiProject/Data/Tests/*.json → 빌드의 StreamingAssets/MomoiProject/Tests(읽기 전용).
    // 개발 빌드의 TestScenarios가 기기에 저장한 상황과 함께 이것을 찾는다(테스트 씬의 시작 상황이 개발 빌드에서도 열린다).
    // 출시 빌드에는 넣지 않는다. 프로젝트의 Assets/StreamingAssets는 건드리지 않는다 — Temp의 사본을 이번 빌드에만 더하므로 빌드가 실패해도 남는 것이 없다.
    sealed class TestScenarioBuild : BuildPlayerProcessor
    {
        const string Staging = "Temp/MomoiProject/TestScenarios";

        public override int callbackOrder => 0;

        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if ((buildPlayerContext.BuildPlayerOptions.options & BuildOptions.Development) == 0) return;
            var source = TestScenarios.Folder;
            var files = Directory.Exists(source) ? Directory.GetFiles(source, "*.json") : Array.Empty<string>();
            if (files.Length == 0) return;
            try
            {
                // 지난 빌드의 사본(지운 상황)이 섞이지 않게 비우고 다시 담는다.
                if (Directory.Exists(Staging)) Directory.Delete(Staging, true);
                Directory.CreateDirectory(Staging);
                foreach (var file in files) File.Copy(file, Path.Combine(Staging, Path.GetFileName(file)), true);
                buildPlayerContext.AddAdditionalPathToStreamingAssets(Path.GetFullPath(Staging), TestScenarios.BundledPath);
                Debug.Log($"[MomoiProject] 개발 빌드에 테스트 상황 {files.Length}개를 넣습니다(StreamingAssets/{TestScenarios.BundledPath}, 읽기 전용).");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MomoiProject] 테스트 상황을 개발 빌드에 넣지 못했습니다(빌드는 계속합니다 — 빌드에서는 기기에 저장한 상황만 보입니다): " + e.Message);
            }
        }
    }
}
