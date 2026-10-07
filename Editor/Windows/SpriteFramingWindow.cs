using System;
using GBible.EditorTools.UI;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // 스프라이트 맞춤 창을 DB 창 밖(틀 인스펙터 등)에서 열 때 담는 창. 맞춤이 끝나면(적용·취소) 저절로 닫힌다.
    // 도메인을 다시 읽으면 내용이 사라지므로 그 전에 닫는다(빈 창이 남지 않게).
    public sealed class SpriteFramingWindow : EditorWindow
    {
        public static void Open(Sprite source, string frame, Action applied = null)
        {
            if (source == null) return;
            var window = GsWindows.Open<SpriteFramingWindow>($"스프라이트 맞춤 · {source.name}", new Vector2(780, 680));
            window.Fill(source, frame, applied);
        }

        void Fill(Sprite source, string frame, Action applied)
        {
            rootVisualElement.Clear();
            var host = Gs.Col();
            Gs.Apply(host);
            host.style.flexGrow = 1;
            rootVisualElement.Add(host);
            var modal = GsSpriteFraming.Open(host, source, frame, applied);
            if (modal == null)
            {
                EditorApplication.delayCall += CloseSelf;
                return;
            }
            modal.Closed += () => EditorApplication.delayCall += CloseSelf;
        }

        void CloseSelf()
        {
            if (this != null) Close();
        }

        void OnEnable() => AssemblyReloadEvents.beforeAssemblyReload += CloseSelf;

        void OnDisable() => AssemblyReloadEvents.beforeAssemblyReload -= CloseSelf;
    }
}
