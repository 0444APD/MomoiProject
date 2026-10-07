using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
    // 줄마다 그 자리 에셋의 GUID를 적어 두고 지금 경로는 GUID로 찾는다 — 개발자가 임시 에셋을 옮기거나 다른 확장자로 바꿔도(hit.wav → hit.ogg)
    // 같은 줄·같은 에셋이다(반영 스크립트가 다시 돌아도 임시 파일·줄을 새로 만들지 않는다).
    // 넣었는지 = GUID의 에셋이 맞는 종류이고 AI가 만든 임시 파일과 내용이 다르다(경로·확장자와 상관없이).
    // 목록: Assets/MomoiProject/Data/AssetRequests.json(프로젝트와 함께 커밋).
    public static class AssetRequests
    {
        public const string FilePath = ProjectFolders.Data + "/AssetRequests.json";

        // 목록 파일 자리(시험이 임시 자리로 바꾼다).
        internal static string StorePath = FilePath;

        [Serializable]
        public sealed class Item
        {
            public string path = "";             // AI가 요청한 경로(반영 스크립트가 다시 부를 때 이 줄을 찾는 열쇠). 지금 자리는 PathOf
            public string guid = "";             // 그 자리 에셋의 GUID — 옮기거나 확장자를 바꿔도 같은 에셋을 가리킨다
            public string usage = "";            // 어디에 쓰나
            public string size = "";             // 크기·길이(예: 256×256, 0.3초)
            public string note = "";             // 느낌·참고
            public bool done;                    // 진짜 파일을 넣었다(교체). 임시 파일 지문이 있으면 지문으로 다시 정한다(StateOf)
            public bool noPlaceholder;           // 임시 파일을 만들지 않은 줄(목록만이거나 처음부터 파일이 있었다) — 맞는 종류의 파일이 있으면 넣은 것
            public string placeholderHash = "";  // AI가 만든 임시 파일의 MD5(지금 파일이 이것과 같으면 아직 임시)
            public long placeholderBytes;        // 그 크기(크기가 다르면 해시를 셈하지 않는다)
        }

        // 줄의 지금 상태.
        public enum State
        {
            Missing,      // 파일이 없다(GUID·요청한 경로·확장자만 다른 같은 종류 어디에도)
            WrongKind,    // 있는 파일이 요청한 종류가 아니다
            Placeholder,  // 아직 AI가 만든 임시 파일
            Done,         // 진짜 파일이 들어왔다
        }

        [Serializable]
        sealed class Store
        {
            public List<Item> items = new List<Item>();
        }

        public static List<Item> Load()
        {
            List<Item> items = null;
            try
            {
                if (File.Exists(StorePath)) items = JsonUtility.FromJson<Store>(File.ReadAllText(StorePath))?.items;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MomoiProject] {StorePath}를 읽지 못했습니다: {e.Message}");
            }
            items ??= new List<Item>();
            items.RemoveAll(i => i == null);
            foreach (var item in items)
            {
                item.path = (item.path ?? "").Replace('\\', '/');
                item.guid ??= "";
                item.usage ??= "";
                item.size ??= "";
                item.note ??= "";
                item.placeholderHash ??= "";
            }
            return items;
        }

        // 바뀐 것이 있을 때만 쓴다(같은 내용이면 다시 가져오기를 부르지 않는다). 읽지 못하는 예전 파일은 덮기 전에 백업으로 옮긴다.
        static void Save(List<Item> items)
        {
            var json = JsonUtility.ToJson(new Store { items = items }, true);
            if (File.Exists(StorePath))
            {
                var old = File.ReadAllText(StorePath);
                if (old == json) return;
                if (!Readable(old)) KeepBroken(old);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath) is string folder && folder.Length > 0 ? folder : ".");
            File.WriteAllText(StorePath, json);
            if (StorePath.StartsWith("Assets/", StringComparison.Ordinal)) AssetDatabase.ImportAsset(StorePath);
        }

        static bool Readable(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            try { return JsonUtility.FromJson<Store>(text) != null; }
            catch (Exception) { return false; }
        }

        static void KeepBroken(string text)
        {
            try
            {
                var folder = Path.Combine(ProjectReset.BackupFolder, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-requests");
                Directory.CreateDirectory(folder);
                var kept = Path.Combine(folder, Path.GetFileName(StorePath));
                File.WriteAllText(kept, text);
                Debug.LogWarning($"[MomoiProject] 읽지 못하는 넣을 것 목록을 새로 쓰기 전에 {kept}에 남겨 두었습니다.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MomoiProject] 읽지 못하는 넣을 것 목록을 백업하지 못했습니다({e.Message}). 원래 내용:\n{text}");
            }
        }

        // ── 지금 경로 · 상태 ─────────────────────────────────────

        // 줄의 에셋이 지금 있는 경로: GUID(옮겼거나 확장자를 바꿨어도) → 요청한 경로 → 같은 자리·같은 이름에 확장자만 다른 같은 종류. 없으면 null.
        public static string PathOf(Item item)
        {
            if (item == null) return null;
            if (!string.IsNullOrEmpty(item.guid))
            {
                var byGuid = AssetDatabase.GUIDToAssetPath(item.guid);
                if (!string.IsNullOrEmpty(byGuid) && File.Exists(byGuid)) return byGuid;
            }
            if (!string.IsNullOrEmpty(item.path) && File.Exists(item.path)) return item.path;
            return Sibling(item.path);
        }

        // GUID를 적기 전에(예전 줄) 지우고 다른 확장자로 새로 넣은 것(hit.wav → hit.ogg)을 찾는다. 기타 종류는 확장자가 곧 형식이라 찾지 않는다.
        static string Sibling(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var kind = AssetTools.KindOf(path);
            if (kind == AssetKind.Other) return null;
            var folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
            var name = Path.GetFileNameWithoutExtension(path);
            return Directory.GetFiles(folder, name + ".*")
                .Select(f => f.Replace('\\', '/'))
                .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && Path.GetFileNameWithoutExtension(f) == name && AssetTools.KindOf(f) == kind)
                .OrderBy(f => f, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        public static State StateOf(Item item) => StateOf(item, out _);

        public static State StateOf(Item item, out string current)
        {
            current = PathOf(item);
            return Evaluate(item, current);
        }

        static State Evaluate(Item item, string current)
        {
            if (item == null || current == null) return State.Missing;
            if (AssetTools.KindOf(current) != AssetTools.KindOf(item.path)) return State.WrongKind;
            if (!string.IsNullOrEmpty(item.placeholderHash)) return SameAsPlaceholder(item, current) ? State.Placeholder : State.Done;
            // 지문이 없는 줄: 임시 파일을 만들지 않았으면 있는 파일이 곧 넣은 것, 예전 줄(지문을 적기 전)은 넣음 표시를 믿는다.
            return item.done || item.noPlaceholder ? State.Done : State.Placeholder;
        }

        static bool SameAsPlaceholder(Item item, string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length != item.placeholderBytes) return false;
                return Hash(File.ReadAllBytes(path)) == item.placeholderHash;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static string Hash(byte[] bytes)
        {
            using (var md5 = MD5.Create())
                return BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        // 목록을 지금 에셋에 맞춘다: GUID가 없던 줄(예전 줄·목록만이던 줄)에 지금 GUID를 적고, 넣음 표시를 지금 상태로. 바뀐 것이 있을 때만 쓴다.
        // 에셋 관리 창이 넣을 것을 그릴 때 부른다(그때부터는 개발자가 옮겨도 따라간다).
        public static List<Item> Refresh()
        {
            var items = Load();
            foreach (var item in items)
            {
                var state = StateOf(item, out var current);
                if (current == null) continue;   // 없으면 적어 둔 것을 그대로 둔다(다시 생기면 이어서 판단)
                var guid = AssetDatabase.AssetPathToGUID(current);
                if (!string.IsNullOrEmpty(guid)) item.guid = guid;
                item.done = state == State.Done;
            }
            // 창을 그리는 중이라 쓰지 못해도(읽기 전용·잠김) 창은 그대로 그린다 — 다음에 다시 맞춘다.
            try { Save(items); }
            catch (Exception e) { Debug.LogWarning($"[MomoiProject] {StorePath}를 지금 상태로 맞춰 쓰지 못했습니다: {e.Message}"); }
            return items;
        }

        // ── 요청(AI — DB 반영 스크립트) ──────────────────────────

        // 목록에만 올린다(임시 파일은 만들지 않는다 — 그 자리에 맞는 종류의 파일이 생기면 넣은 것). 같은 줄이 있으면 설명만 고친다. 지금 경로(없으면 요청한 경로).
        public static string Request(string path, string usage, string size = "", string note = "") => Upsert(path, usage, size, note, null);

        // 임시 그림(단색 사각형, 크기 그대로)을 만들고 목록에 올린다. 이미 있으면(개발자가 옮겼거나 다른 확장자로 바꿨어도 — GUID로 찾는다) 그대로 둔다. 지금 경로.
        public static string Sprite(string path, int width, int height, string usage, string note = "") =>
            Upsert(path, usage, $"{width}×{height}", note, () =>
            {
                var texture = new Texture2D(Mathf.Max(1, width), Mathf.Max(1, height), TextureFormat.RGBA32, false);
                var color = Color.HSVToRGB((((path ?? "").GetHashCode() & 0x7fffffff) % 360) / 360f, 0.35f, 0.85f);
                texture.SetPixels(Enumerable.Repeat(color, texture.width * texture.height).ToArray());
                var bytes = texture.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(texture);
                return bytes;
            }, created =>
            {
                if (AssetImporter.GetAtPath(created) is TextureImporter importer)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.SaveAndReimport();
                }
            });

        // 임시 소리(무음 wav)를 만들고 목록에 올린다. 지금 경로.
        public static string Audio(string path, string usage, float seconds = 0.5f, string note = "") =>
            Upsert(path, usage, $"{seconds:0.##}초", note, () => SilentWav(Mathf.Max(0.05f, seconds)));

        // 줄을 찾거나 만들고 설명을 고친다. placeholder: 에셋이 어디에도 없을 때 만들 임시 파일 내용(null = 목록만). imported: 임시 파일을 가져온 뒤 할 일.
        static string Upsert(string path, string usage, string size, string note, Func<byte[]> placeholder, Action<string> imported = null)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("[MomoiProject] 넣을 것의 경로가 비었습니다.", nameof(path));
            path = path.Replace('\\', '/');
            var items = Load();
            var item = Find(items, path);
            var added = item == null;
            if (added) items.Add(item = new Item { path = path });
            item.usage = usage ?? "";
            item.size = size ?? "";
            item.note = note ?? "";
            var current = PathOf(item);
            if (current == null && placeholder != null && !File.Exists(path))
            {
                AssetTools.EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
                var bytes = placeholder();
                File.WriteAllBytes(path, bytes);
                AssetDatabase.ImportAsset(path);
                imported?.Invoke(path);
                item.path = path;   // 확장자만 다른 예전 줄이었으면 지금 요청한 경로를 열쇠로
                item.placeholderHash = Hash(bytes);
                item.placeholderBytes = bytes.LongLength;
                item.noPlaceholder = false;
                current = path;
            }
            else if (added || (placeholder == null && current == null && string.IsNullOrEmpty(item.placeholderHash)))
            {
                // 새 줄인데 임시 파일을 만들지 않았다(목록만 · 개발자가 먼저 넣어 둠), 또는 아무것도 없는 줄을 목록만으로 다시 요청했다.
                item.noPlaceholder = true;
            }
            if (current != null)
            {
                var guid = AssetDatabase.AssetPathToGUID(current);
                if (!string.IsNullOrEmpty(guid)) item.guid = guid;
                item.done = Evaluate(item, current) == State.Done;
            }
            Save(items);
            return current ?? path;
        }

        // 경로로 줄 찾기: 요청한 경로가 같은 줄 → 그 경로 에셋의 GUID가 같은 줄(옮긴 자리로 부름) →
        // 같은 자리·같은 이름에 확장자만 다른 같은 종류(예전 줄 — 바꾸기가 경로를 새 확장자로 고쳐 적었다).
        static Item Find(List<Item> items, string path)
        {
            var exact = items.FirstOrDefault(i => i.path == path);
            if (exact != null) return exact;
            var guid = File.Exists(path) ? AssetDatabase.AssetPathToGUID(path) : "";
            if (!string.IsNullOrEmpty(guid))
            {
                var moved = items.FirstOrDefault(i => i.guid == guid);
                if (moved != null) return moved;
            }
            var kind = AssetTools.KindOf(path);
            if (kind == AssetKind.Other) return null;
            var stem = Stem(path);
            return items.FirstOrDefault(i => Stem(i.path) == stem && AssetTools.KindOf(i.path) == kind);
        }

        static string Stem(string path) => string.IsNullOrEmpty(path) ? "" : path.Substring(0, path.Length - Path.GetExtension(path).Length);

        // ── 넣기(개발자 — 에셋 관리 창) ──────────────────────────

        // 교체되면 넣음 표시(AssetTools.Replace가 부른다). path = 바꾼 뒤의 에셋 경로 — GUID로 줄을 찾으므로 옮겼거나 확장자가 바뀌어도 된다.
        public static void MarkDone(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            path = path.Replace('\\', '/');
            var items = Load();
            var item = Find(items, path);
            if (item == null) return;
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (!string.IsNullOrEmpty(guid)) item.guid = guid;
            item.done = true;
            Save(items);
        }

        // 파일이 없는 줄(목록만이던 것·지워진 것)에 밖의 파일을 가져온다: 요청한 자리에, 확장자는 넣은 파일 것(같은 종류만 — 기타는 같은 확장자만).
        // 그사이 에셋이 생겼으면 그 자리에 내용을 넣는다(교체). 넣은 에셋 경로.
        public static string Put(Item item, string externalFile)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (string.IsNullOrEmpty(externalFile) || !File.Exists(externalFile)) throw new ArgumentException($"[MomoiProject] 넣을 파일이 없습니다: {externalFile}");
            var kind = AssetTools.KindOf(item.path);
            if (AssetTools.KindOf(externalFile) != kind)
                throw new ArgumentException($"[MomoiProject] 종류가 다릅니다({AssetTools.KindName(kind)} ← {AssetTools.KindName(AssetTools.KindOf(externalFile))}).");
            if (kind == AssetKind.Other && !string.Equals(Path.GetExtension(item.path), Path.GetExtension(externalFile), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"[MomoiProject] 확장자가 다릅니다({Path.GetExtension(item.path)} ← {Path.GetExtension(externalFile)}).");
            var current = PathOf(item);
            if (current != null) return AssetTools.Replace(current, externalFile);
            var target = Path.ChangeExtension(item.path, Path.GetExtension(externalFile)).Replace('\\', '/');
            AssetTools.EnsureFolder(Path.GetDirectoryName(target)?.Replace('\\', '/'));
            File.Copy(externalFile, target, overwrite: false);
            AssetDatabase.ImportAsset(target);
            var items = Load();
            var row = items.FirstOrDefault(i => i.path == item.path) ?? Find(items, target);
            if (row != null)
            {
                var guid = AssetDatabase.AssetPathToGUID(target);
                if (!string.IsNullOrEmpty(guid)) row.guid = guid;
                row.done = true;
                Save(items);
            }
            return target;
        }

        // 줄을 지운다(요청한 경로, 또는 그 경로 에셋의 GUID가 같은 줄). 에셋 파일은 그대로.
        public static void Remove(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            path = path.Replace('\\', '/');
            var items = Load();
            var guid = File.Exists(path) ? AssetDatabase.AssetPathToGUID(path) : "";
            if (items.RemoveAll(i => i.path == path || (!string.IsNullOrEmpty(guid) && i.guid == guid)) > 0) Save(items);
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
