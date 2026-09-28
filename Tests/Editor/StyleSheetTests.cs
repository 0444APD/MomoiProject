using System.Collections.Generic;
using GameSystems.EditorTools.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameSystems.Tests
{
    // USS 문법 오류는 컴파일 오류가 아니라 임포트 경고로만 나오므로 따로 잡는다.
    public class StyleSheetTests
    {
        [TestCase("GsTheme")]
        [TestCase("GsExperimental")]
        public void 스타일시트가_경고_없이_임포트된다(string name)
        {
            var sheet = Gs.LoadSheet(name);
            Assert.IsNotNull(sheet, $"{name}.uss를 찾을 수 없습니다");
            var path = AssetDatabase.GetAssetPath(sheet);

            var messages = new List<string>();
            void Collect(string message, string stackTrace, LogType type)
            {
                if (type != LogType.Log) messages.Add($"{type}: {message}");
            }

            Application.logMessageReceived += Collect;
            try
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }
            finally
            {
                Application.logMessageReceived -= Collect;
            }

            Assert.IsEmpty(messages, string.Join("\n", messages));
        }
    }
}
