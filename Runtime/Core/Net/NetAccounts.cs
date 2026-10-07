using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace GBible
{
    // 매칭큐 서버의 계정 하나(서버만 가진다 — 재화·점수는 클라이언트 값을 믿지 않는다).
    [Serializable]
    public sealed class NetAccount
    {
        public string id;
        public string name;
        // 실력 점수(매칭이 비슷한 사람끼리 묶는다). 처음 1000.
        public double rating = 1000;
        public int wins;
        public int losses;
        public int draws;
        // 게임이 쓰는 계정 값(재화·해금 등, 글자). 부분유료화의 구매 내역도 여기(나중에 진짜 DB로).
        public Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    // ── 인증(입장 확인) ─────────────────────────────────────────
    // 접속한 사람이 누구인지 확인해 계정 키를 정한다. 끝나면 done을 한 번 부른다(다른 스레드에서 불러도 된다 — 서버가 게임 스레드로 옮긴다).
    public interface INetAuthenticator
    {
        void Authenticate(NetHello hello, Action<NetAdmission> done);
    }

    // 가짜 인증(테스트용 멀티·로컬 매칭큐): 이름을 그대로 계정 키로 믿는다. 보안 없음.
    public sealed class FakeAuthenticator : INetAuthenticator
    {
        public void Authenticate(NetHello hello, Action<NetAdmission> done)
        {
            // 입장 표는 보지 않는다 — Steam 티켓처럼 접속마다 바뀌는 표가 와도 같은 이름이면 같은 계정(다시 접속하면 같은 자리).
            var name = string.IsNullOrWhiteSpace(hello.Name) ? "플레이어" : hello.Name.Trim();
            done(NetAdmission.Accept("name:" + name, name));
        }
    }

    // Steam 인증(매칭큐 출시): 클라이언트가 보낸 인증 티켓(입장 표 = 16진 글자)을 Steam Web API로 확인한다 → 계정 키 "steam:<SteamID>".
    // 클라이언트 쪽 티켓은 Steam 모듈이 GameNet.TokenSource로 채운다(GetAuthTicketForWebApi(DefaultIdentity)).
    // 퍼블리셔 웹 API 키는 서버에만 둔다(클라이언트 빌드에 넣지 않는다). Steamworks 계정·앱이 준비되면 붙인다(그 전에는 확인하지 못함).
    public sealed class SteamWebAuthenticator : INetAuthenticator
    {
        // 티켓을 만들 때와 확인할 때 같은 글이어야 한다(이 서버용 티켓이라는 표시).
        public const string DefaultIdentity = "momoi";

        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        readonly string _publisherKey;
        readonly uint _appId;
        readonly string _identity;

        public SteamWebAuthenticator(string publisherKey, uint appId, string identity = DefaultIdentity)
        {
            _publisherKey = publisherKey ?? throw new ArgumentNullException(nameof(publisherKey));
            _appId = appId;
            _identity = identity ?? "";
        }

        public void Authenticate(NetHello hello, Action<NetAdmission> done)
        {
            if (string.IsNullOrWhiteSpace(hello.Token))
            {
                done(NetAdmission.Refuse("Steam 인증 티켓이 없습니다."));
                return;
            }
            var url = "https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/?key=" + Uri.EscapeDataString(_publisherKey) +
                      "&appid=" + _appId.ToString(CultureInfo.InvariantCulture) + "&ticket=" + Uri.EscapeDataString(hello.Token.Trim()) +
                      (_identity.Length > 0 ? "&identity=" + Uri.EscapeDataString(_identity) : "");
            Task.Run(async () =>
            {
                try
                {
                    var json = await Http.GetStringAsync(url).ConfigureAwait(false);
                    var root = MiniJson.Deserialize(json) as Dictionary<string, object>;
                    var response = root != null && root.TryGetValue("response", out var r) ? r as Dictionary<string, object> : null;
                    var parameters = response != null && response.TryGetValue("params", out var p) ? p as Dictionary<string, object> : null;
                    var steamId = parameters != null && parameters.TryGetValue("steamid", out var id) ? Convert.ToString(id, CultureInfo.InvariantCulture) : null;
                    var result = parameters != null && parameters.TryGetValue("result", out var res) ? Convert.ToString(res, CultureInfo.InvariantCulture) : null;
                    if (string.IsNullOrEmpty(steamId) || result != "OK") done(NetAdmission.Refuse("Steam 인증에 실패했습니다."));
                    else done(NetAdmission.Accept("steam:" + steamId, hello.Name));
                }
                catch (Exception e)
                {
                    done(NetAdmission.Refuse("Steam 인증 서버에 닿지 못했습니다: " + e.Message));
                }
            });
        }
    }

    // ── 저장(계정·진행 중인 매치) ───────────────────────────────
    // 진짜(출시): PostgreSQL(계정·재화·구매) + Redis(진행 중 매치). 가짜: 이 컴퓨터의 파일(FilePlayerStore).
    // 게임 스레드에서 부른다(파일 저장은 짧다 — 진짜 DB 어댑터는 안쪽에서 비동기로 쓰고 바로 돌아온다).
    public interface INetPlayerStore
    {
        // 계정을 읽는다(없으면 null).
        NetAccount Load(string id);
        void Save(NetAccount account);
        // 진행 중인 매치의 판(session.ExportRun JSON + 플레이어 계정 목록). 행동마다 덮어쓴다(서버가 꺼져도 이어 하기).
        void SaveMatch(string matchId, string state);
        string LoadMatch(string matchId);
        IEnumerable<string> OpenMatches();
        // 매치가 끝났다(진행 중 목록에서 뺀다).
        void CloseMatch(string matchId);
    }

    // 가짜 저장: 폴더 아래 accounts/<계정>.json, matches/<매치>.json.
    // 쓰기는 SafeFile(임시 파일에 끝까지 써서 디스크에 내린 뒤 바꿔 넣는다 — 바로 전 파일은 .bak).
    // 읽을 때 본 파일이 없거나 깨졌으면 .tmp(바꿔 넣는 사이에 꺼짐 — 끝까지 쓴 새 내용) → .bak(바로 전 저장) 차례로 되살린다.
    // 깨진 파일은 지우지 않고 옆으로 옮긴다(<파일>.broken-<시각>). 읽을 수 있는 것이 하나도 없을 때만 Load가 null(없는 계정 → 새 계정).
    public sealed class FilePlayerStore : INetPlayerStore
    {
        // 파일 이름에 넣는 계정 키 앞부분의 최대 글자 수. 한글(UTF-8 3바이트) 64자에 해시·확장자·.broken-시각을 붙여도
        // ext4의 이름 한도 255바이트 안에 든다. 해시는 자르기 전 키 전체로 만들어 앞부분이 같은 긴 키끼리도 겹치지 않는다.
        const int NameLength = 64;
        // SafeFile이 쓰는 임시 파일(바꿔 넣기 전).
        const string TempSuffix = ".tmp";

        readonly string _accounts;
        readonly string _matches;

        public FilePlayerStore(string folder)
        {
            if (string.IsNullOrEmpty(folder)) throw new ArgumentException("[MomoiProject] 저장 폴더가 비어 있습니다.", nameof(folder));
            _accounts = Path.Combine(folder, "accounts");
            _matches = Path.Combine(folder, "matches");
            Directory.CreateDirectory(_accounts);
            Directory.CreateDirectory(_matches);
        }

        // 기록(깨진 파일을 옮김 · .tmp/.bak에서 되살림). 서버 기록에 잇는다(예: store.Log += Debug.Log).
        public event Action<string> Log;

        static string Safe(string id)
        {
            var b = new StringBuilder();
            foreach (var c in id ?? "")
            {
                if (b.Length >= NameLength) break;
                b.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            }
            // 다른 글자가 같은 이름이 되지 않게 해시를 붙인다(실행마다 같은 값 — string.GetHashCode는 실행마다 다르다).
            var hash = 2166136261u;
            foreach (var c in id ?? "")
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return b + "-" + hash.ToString("x8", CultureInfo.InvariantCulture);
        }

        string AccountPath(string id) => Path.Combine(_accounts, Safe(id) + ".json");
        string MatchPath(string id) => Path.Combine(_matches, Safe(id) + ".json");

        public NetAccount Load(string id)
        {
            var tree = Read(AccountPath(id), out _, out var broken);
            if (tree == null)
            {
                if (broken) Say($"계정 {id}: 읽을 수 있는 저장이 없어 없는 계정으로 봅니다(깨진 파일은 옆에 남겨 두었습니다).");
                return null;
            }
            var account = DataModel.FromTree(tree, typeof(NetAccount), null, null) as NetAccount;
            // 키가 빠진 파일이면 찾은 키로(매칭·결과 반영이 키로 계정을 찾는다).
            if (account != null && string.IsNullOrEmpty(account.id)) account.id = id;
            return account;
        }

        public void Save(NetAccount account)
        {
            if (account == null) return;
            var json = MiniJson.Serialize(DataModel.ToTree(account, typeof(NetAccount), null, new List<string>()));
            SafeFile.WriteAllText(AccountPath(account.id), json);
        }

        public void SaveMatch(string matchId, string state) => SafeFile.WriteAllText(MatchPath(matchId), state ?? "");

        public string LoadMatch(string matchId)
        {
            Read(MatchPath(matchId), out var text, out _);
            return text;
        }

        public IEnumerable<string> OpenMatches()
        {
            // 본 파일 없이 .tmp·.bak만 남은 매치(바꿔 넣는 사이에 꺼짐)도 찾는다. 옮겨 둔 깨진 파일(.broken-…)은 보지 않는다.
            var paths = Directory.GetFiles(_matches).Select(MainFile).Where(p => p != null).Distinct(StringComparer.Ordinal).ToList();
            foreach (var path in paths)
            {
                string id = null;
                try
                {
                    if (Read(path, out _, out _) is Dictionary<string, object> root && root.TryGetValue("matchId", out var value)) id = value as string;
                }
                catch (Exception e)
                {
                    Say($"매치 파일 {Path.GetFileName(path)}을(를) 읽지 못했습니다: {e.Message}");
                }
                if (id != null) yield return id;
            }
        }

        // 끝난 매치: 본 파일과 .bak·.tmp를 함께 지운다(끝난 매치가 .bak에서 되살아나 다시 열리지 않게).
        public void CloseMatch(string matchId) => SafeFile.Delete(MatchPath(matchId));

        // 저장 파일 하나: 본 파일 → .tmp → .bak 차례로 JSON 객체로 읽히는 첫 내용(없으면 null, text = 그 글).
        // 깨진 것(비었거나 JSON 객체가 아님)은 지우지 않고 옆으로 옮긴다(broken = 옮긴 것이 있다). 디스크 오류(잠김 등)는 그대로 던진다 —
        // 깨진 것으로 보고 옛 내용을 고르면 다음 저장이 그 옛 내용으로 새 내용을 덮어쓴다.
        Dictionary<string, object> Read(string path, out string text, out bool broken)
        {
            text = null;
            broken = false;
            foreach (var file in new[] { path, path + TempSuffix, SafeFile.Backup(path) })
            {
                var content = SafeFile.ReadAllText(file);
                if (content == null) continue;
                if (Parse(content) is Dictionary<string, object> tree)
                {
                    if (file != path) Say($"{Path.GetFileName(path)}이(가) 없거나 깨져 {Path.GetFileName(file)}에서 되살렸습니다.");
                    text = content;
                    return tree;
                }
                Say($"깨진 저장 파일을 옆으로 옮겼습니다(지우지 않음): {Path.GetFileName(file)} → {MoveAside(file)}");
                broken = true;
            }
            return null;
        }

        // 비었거나(SafeFile: 공백뿐이면 "") JSON이 아니면 null.
        static object Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try { return MiniJson.Deserialize(text); }
            catch (FormatException) { return null; }
        }

        // <파일>.broken-<시각>(같은 이름이 있으면 -2, -3 …)으로 옮긴다. 돌려주는 값: 옮긴 이름.
        // 확장자 뒤에 붙여 OpenMatches(.json·.tmp·.bak)가 다시 집지 않는다.
        static string MoveAside(string file)
        {
            var stamp = file + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var target = stamp;
            for (var n = 2; File.Exists(target); n++) target = stamp + "-" + n.ToString(CultureInfo.InvariantCulture);
            File.Move(file, target);
            return Path.GetFileName(target);
        }

        // 매치 파일(.json)과 그 .tmp·.bak → 본 파일 경로. 그 밖(옮겨 둔 깨진 파일 등)은 null.
        static string MainFile(string file)
        {
            if (file.EndsWith(".json", StringComparison.Ordinal)) return file;
            if (file.EndsWith(".json" + TempSuffix, StringComparison.Ordinal) || file.EndsWith(".json.bak", StringComparison.Ordinal)) return file.Substring(0, file.Length - 4);
            return null;
        }

        void Say(string message) => Log?.Invoke("[MomoiProject] " + message);
    }
}
