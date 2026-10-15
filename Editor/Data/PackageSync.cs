using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace GBible.EditorTools
{
    // 패키지 갱신을 저절로 — AI가 Packages/manifest.json을 고친 뒤 개발자가 Unity 창을 누르지 않아도 패키지를 받는다.
    //   Unity는 창이 앞에 있을 때만 manifest를 다시 읽는다(창이 뒤에 있으면 AssetDatabase.Refresh로는 패키지를 다시 풀지 않는다).
    //   manifest.json이 packages-lock.json보다 새롭고 아직 그 manifest로 풀지 않았으면 Client.Resolve()를 부른다:
    //   에디터가 켜질 때 · 도메인을 다시 읽을 때 · 몇 초마다 · AI의 요청(Play · RunTests)을 처리하기 전.
    //   manifest 하나(고친 시각)에 한 번만 부른다 — 풀어도 lock 파일이 바뀌지 않는 경우(내용이 같음)에 되풀이하지 않게.
    //   플레이 중 · 컴파일 중 · 에셋을 읽는 중에는 기다린다. 컴파일이 깨져 있어도 돈다(GBible 에디터 어셈블리는 프로젝트 코드와 따로).
    [InitializeOnLoad]
    public static class PackageSync
    {
        const string HandledKey = "MomoiProject.PackageSync.Handled";
        const string ManifestPath = "Packages/manifest.json";
        const string LockPath = "Packages/packages-lock.json";
        const double Interval = 2.0;
        const double Settle = 1.0;   // 방금 쓴 manifest는 다 써질 때까지 조금 기다린다
        static double _next;

        static PackageSync()
        {
            _next = EditorApplication.timeSinceStartup + Interval;
            EditorApplication.update += Update;
        }

        static void Update()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + Interval;
            ResolveIfChanged();
        }

        // 다시 풀기를 시작했으면 참 — 요청 처리는 이번 확인을 건너뛰고, 패키지가 들어와 다시 컴파일된 뒤 이어서 한다.
        public static bool ResolveIfChanged()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return false;
            var manifest = new FileInfo(ManifestPath);
            if (!manifest.Exists) return false;
            var lockFile = new FileInfo(LockPath);
            if (lockFile.Exists && lockFile.LastWriteTimeUtc >= manifest.LastWriteTimeUtc) return false;
            var stamp = manifest.LastWriteTimeUtc.Ticks.ToString();
            if (SessionState.GetString(HandledKey, "") == stamp) return false;
            if ((DateTime.UtcNow - manifest.LastWriteTimeUtc).TotalSeconds < Settle) return false;
            SessionState.SetString(HandledKey, stamp);
            Debug.Log("[MomoiProject] Packages/manifest.json이 바뀌어 패키지를 다시 풉니다(Unity 창이 뒤에 있어도).");
            Client.Resolve();
            return true;
        }
    }
}
