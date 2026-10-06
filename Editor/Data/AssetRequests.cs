using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // 넣을 것(관례 18절): AI가 임시 에셋 자리를 최종 경로에 만들고 무엇을 넣을지 적어 둔다. 개발자는 에셋 관리 › 넣을 것에서
    // 그 줄에 진짜 파일을 고르면(교체 — GUID 그대로) 그 자리를 쓰는 모든 곳이 바뀐다.
    //   [DbSeed("1")] static void Art()
    //   {
    //       AssetRequests.Sprite("Assets/Dev/Art/Cards/fireball.png", 256, 256, "카드 '파이어볼' 그림", "주황 계열");
    //       AssetRequests.Audio("Assets/Dev/Audio/Sfx/hit.wav", "맞을 때 효과음(0.3초 안팎)");
    //       AssetRequests.Request("Assets/Dev/Fonts/Main.ttf", "모든 화면의 한글 글꼴");   // 임시 파일 없이 목록만
    //   }
    // 목록: Assets/MomoiProject/Data/AssetRequests.json(프로젝트와 함께 커밋).
    public static class AssetRequests
    {
        public const string FilePath = ProjectFolders.Data + "/AssetRequests.json";

        [Serializable]
        public sealed class Item
        {
            public string path = "";
            public string usage = "";   // 어디에 쓰나
            public string size = "";    // 크기·길이(예: 256×256, 0.3초)
            public string note = "";    // 느낌·참고
            public bool done;           // 진짜 파일을 넣었다(교체)
        }

        [Serializable]
        sealed class Store
        {
            public List<Item> items = new List<Item>();
        }

        public static List<Item> Load()
        {
            try
            {
                if (File.Exists(FilePath)) return JsonUtility.FromJson<Store>(File.ReadAllText(FilePath))?.items ?? new List<Item>();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MomoiProject] {FilePath}를 읽지 못했습니다: {e.Message}");
            }
            return new List<Item>();
        }

        static void Save(List<Item> items)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? "Assets");
            File.WriteAllText(FilePath, JsonUtility.ToJson(new Store { items = items }, true));
            AssetDatabase.ImportAsset(FilePath);
        }

        // 목록에 올린다(같은 경로가 있으면 설명만 고친다 — 넣었는지는 그대로). 임시 파일은 만들지 않는다.
        public static void Request(string path, string usage, string size = "", string note = "")
        {
            var items = Load();
            var item = items.FirstOrDefault(i => i.path == path);
            if (item == null) items.Add(item = new Item { path = path });
            item.usage = usage ?? "";
            item.size = size ?? "";
            item.note = note ?? "";
            Save(items);
        }

        // 임시 그림(단색 사각형, 크기 그대로)을 만들고 목록에 올린다. 이미 파일이 있으면 그대로 둔다(개발자가 넣은 것을 덮지 않는다).
        public static void Sprite(string path, int width, int height, string usage, string note = "")
        {
            if (!File.Exists(path))
            {
                AssetTools.EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
                var texture = new Texture2D(Mathf.Max(1, width), Mathf.Max(1, height), TextureFormat.RGBA32, false);
                var color = Color.HSVToRGB((Mathf.Abs(path.GetHashCode()) % 360) / 360f, 0.35f, 0.85f);
                texture.SetPixels(Enumerable.Repeat(color, texture.width * texture.height).ToArray());
                File.WriteAllBytes(path, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path);
                if (AssetImporter.GetAtPath(path) is TextureImporter importer)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.SaveAndReimport();
                }
            }
            Request(path, usage, $"{width}×{height}", note);
        }

        // 임시 소리(무음 wav)를 만들고 목록에 올린다.
        public static void Audio(string path, string usage, float seconds = 0.5f, string note = "")
        {
            if (!File.Exists(path))
            {
                AssetTools.EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
                File.WriteAllBytes(path, SilentWav(Mathf.Max(0.05f, seconds)));
                AssetDatabase.ImportAsset(path);
            }
            Request(path, usage, $"{seconds:0.##}초", note);
        }

        // 교체되면 넣음 표시(AssetTools.Replace가 부른다).
        public static void MarkDone(string path)
        {
            var items = Load();
            var item = items.FirstOrDefault(i => i.path == path || Path.ChangeExtension(i.path, null) == Path.ChangeExtension(path, null));
            if (item == null) return;
            item.path = path;
            item.done = true;
            Save(items);
        }

        public static void Remove(string path)
        {
            var items = Load();
            if (items.RemoveAll(i => i.path == path) > 0) Save(items);
        }

        static byte[] SilentWav(float seconds)
        {
            const int rate = 22050;
            var samples = (int)(rate * seconds);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + samples * 2);
                writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);       // PCM
                writer.Write((short)1);       // 모노
                writer.Write(rate);
                writer.Write(rate * 2);       // 바이트/초
                writer.Write((short)2);       // 블록
                writer.Write((short)16);      // 비트
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(samples * 2);
                writer.Write(new byte[samples * 2]);
                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
