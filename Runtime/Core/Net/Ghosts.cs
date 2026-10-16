using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace GBible
{
    // 비동기 대전(고스트): 다른 플레이어가 남겨 둔 데이터(덱 · 보드 · 편성)와 겨룬다 — 실시간 접속이 없다.
    // 올리기 · 받기는 이 자리 하나로 한다(바꿔 끼우는 곳). 기본은 이 기기 안 파일(FileGhostStore — 내가 남긴 것과 DB의 기본 고스트로 혼자 시험).
    // 여러 사람이 나누려면 출시 때 서버(리더보드 첨부 · 자체 서버)를 IGhostStore로 만들어 Ghosts.Store에 넣는다 — 게임 코드는 바뀌지 않는다.
    //   올리기(판 끝 화면 등 행동 밖): Ghosts.Store.Upload(new GhostRecord { kind = "board", rank = run.round, data = 보드 JSON }, ok => …);
    //   받기(라운드 시작 전, 화면 쪽):  Ghosts.Store.Pick("board", round - 1, round + 1, ghost => GameScreens.Do(() => new SetOpponent { ghost = ghost?.data ?? 기본 }));
    // 규칙(행동)은 파일 · 네트워크를 읽지 않는다 — 받은 내용을 행동 필드로 넘긴다(그래야 Undo · 저장 · 다시 돌리기가 맞는다).
    // 공정성(조작한 고스트)은 서버 쪽 일이다: 기본 파일 저장은 검사하지 않는다.
    public interface IGhostStore
    {
        // 올린다. done(성공했나)은 저장이 끝난 뒤(서버면 응답 뒤) — 비우면 기다리지 않는다.
        void Upload(GhostRecord record, Action<bool> done = null);

        // kind가 같고 rank가 min~max인 것 가운데 하나(없으면 null). 고르는 방식(무작위 · 최근)은 저장소가 정한다. exceptOwner의 것은 뺀다(내 고스트와 싸우지 않게).
        void Pick(string kind, double min, double max, Action<GhostRecord> done, string exceptOwner = null);
    }

    [Serializable]
    public sealed class GhostRecord
    {
        public string id = "";
        public string kind = "";     // 갈래(보드 · 시즌 · 모드)
        public double rank;          // 맞춰 고를 숫자(라운드 · 점수 · 등급)
        public string data = "";     // 내용(보통 JSON — 게임이 정한 모양)
        public string owner = "";    // 남긴 사람(빈 글이면 이 기기)
        public string createdAt = ""; // 올린 시각(UTC, ISO)
    }

    public static class Ghosts
    {
        // 지금 쓰는 저장소. 기본은 이 기기 안 파일(경로는 Unity 쪽이 플레이를 시작할 때 정한다 — FileGhostStore.Folder).
        public static IGhostStore Store = new FileGhostStore();
    }

    // 기본 저장소: 이 기기 안의 파일(갈래마다 하나, 최근 Limit개). 고르기는 범위 안에서 무작위(System.Random — 규칙 밖, 받은 결과가 행동에 기록된다).
    public sealed class FileGhostStore : IGhostStore
    {
        public static string Folder = Path.Combine(Path.GetTempPath(), "MomoiProject", "ghosts");
        public int Limit = 200;
        readonly Random _random = new Random();

        string PathOf(string kind) => Path.Combine(Folder, Clean(kind) + ".json");

        static string Clean(string kind) => string.IsNullOrEmpty(kind) ? "default" : new string(kind.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());

        public void Upload(GhostRecord record, Action<bool> done = null)
        {
            if (record == null) { done?.Invoke(false); return; }
            try
            {
                var list = Read(record.kind);
                if (string.IsNullOrEmpty(record.id)) record.id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrEmpty(record.createdAt)) record.createdAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                list.Add(record);
                if (list.Count > Limit) list.RemoveRange(0, list.Count - Limit);
                Directory.CreateDirectory(Folder);
                SafeFile.WriteAllText(PathOf(record.kind), MiniJson.Serialize(list.Select(ToMap).Cast<object>().ToList()));
                done?.Invoke(true);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                done?.Invoke(false);
            }
        }

        public void Pick(string kind, double min, double max, Action<GhostRecord> done, string exceptOwner = null)
        {
            var fits = Read(kind).Where(g => g.rank >= min && g.rank <= max && (exceptOwner == null || g.owner != exceptOwner)).ToList();
            done?.Invoke(fits.Count == 0 ? null : fits[_random.Next(fits.Count)]);
        }

        // 이 기기에 남은 것(시험 · 모니터용).
        public List<GhostRecord> Read(string kind)
        {
            var result = new List<GhostRecord>();
            var path = PathOf(kind);
            if (!SafeFile.Exists(path)) return result;
            try
            {
                if (!(MiniJson.Deserialize(SafeFile.ReadAllText(path)) is List<object> rows)) return result;
                foreach (var row in rows.OfType<Dictionary<string, object>>()) result.Add(FromMap(row));
            }
            catch (Exception e) when (e is IOException || e is FormatException || e is InvalidCastException)
            {
                // 깨진 파일은 빈 목록으로(다음 올리기가 새로 쓴다).
            }
            return result;
        }

        static Dictionary<string, object> ToMap(GhostRecord g) => new Dictionary<string, object>
        {
            ["id"] = g.id, ["kind"] = g.kind, ["rank"] = g.rank, ["data"] = g.data, ["owner"] = g.owner, ["createdAt"] = g.createdAt,
        };

        static GhostRecord FromMap(Dictionary<string, object> row) => new GhostRecord
        {
            id = row.TryGetValue("id", out var id) ? id as string ?? "" : "",
            kind = row.TryGetValue("kind", out var kind) ? kind as string ?? "" : "",
            rank = row.TryGetValue("rank", out var rank) ? Convert.ToDouble(rank, CultureInfo.InvariantCulture) : 0,
            data = row.TryGetValue("data", out var data) ? data as string ?? "" : "",
            owner = row.TryGetValue("owner", out var owner) ? owner as string ?? "" : "",
            createdAt = row.TryGetValue("createdAt", out var at) ? at as string ?? "" : "",
        };
    }
}
