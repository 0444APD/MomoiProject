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

    // 가짜 인증(테스트용 멀티·로컬 매칭큐): 이름(또는 입장 표)을 그대로 계정 키로 믿는다. 보안 없음.
    public sealed class FakeAuthenticator : INetAuthenticator
    {
        public void Authenticate(NetHello hello, Action<NetAdmission> done)
        {
            var name = string.IsNullOrWhiteSpace(hello.Name) ? "플레이어" : hello.Name.Trim();
            var account = string.IsNullOrWhiteSpace(hello.Token) ? "name:" + name : "token:" + hello.Token.Trim();
            done(NetAdmission.Accept(account, name));
        }
    }

    // Steam 인증(매칭큐 출시): 클라이언트가 보낸 인증 티켓(입장 표 = 16진 글자)을 Steam Web API로 확인한다 → 계정 키 "steam:<SteamID>".
    // 퍼블리셔 웹 API 키는 서버에만 둔다(클라이언트 빌드에 넣지 않는다). Steamworks 계정·앱이 준비되면 붙인다(그 전에는 확인하지 못함).
    public sealed class SteamWebAuthenticator : INetAuthenticator
    {
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        readonly string _publisherKey;
        readonly uint _appId;

        public SteamWebAuthenticator(string publisherKey, uint appId)
        {
            _publisherKey = publisherKey ?? throw new ArgumentNullException(nameof(publisherKey));
            _appId = appId;
        }

        public void Authenticate(NetHello hello, Action<NetAdmission> done)
        {
            if (string.IsNullOrWhiteSpace(hello.Token))
            {
                done(NetAdmission.Refuse("Steam 인증 티켓이 없습니다."));
                return;
            }
            var url = "https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/?key=" + Uri.EscapeDataString(_publisherKey) +
                      "&appid=" + _appId.ToString(CultureInfo.InvariantCulture) + "&ticket=" + Uri.EscapeDataString(hello.Token.Trim());
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
    public sealed class FilePlayerStore : INetPlayerStore
    {
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

        static string Safe(string id)
        {
            var b = new StringBuilder();
            foreach (var c in id ?? "")
                b.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
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
            var path = AccountPath(id);
            if (!File.Exists(path)) return null;
            var tree = MiniJson.Deserialize(File.ReadAllText(path, Encoding.UTF8));
            return DataModel.FromTree(tree, typeof(NetAccount), null, null) as NetAccount;
        }

        public void Save(NetAccount account)
        {
            if (account == null) return;
            var json = MiniJson.Serialize(DataModel.ToTree(account, typeof(NetAccount), null, new List<string>()));
            WriteAtomic(AccountPath(account.id), json);
        }

        public void SaveMatch(string matchId, string state) => WriteAtomic(MatchPath(matchId), state ?? "");

        public string LoadMatch(string matchId)
        {
            var path = MatchPath(matchId);
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }

        public IEnumerable<string> OpenMatches()
        {
            foreach (var path in Directory.GetFiles(_matches, "*.json"))
            {
                string id = null;
                try
                {
                    if (MiniJson.Deserialize(File.ReadAllText(path, Encoding.UTF8)) is Dictionary<string, object> root && root.TryGetValue("matchId", out var value))
                        id = value as string;
                }
                catch (Exception) { }
                if (id != null) yield return id;
            }
        }

        public void CloseMatch(string matchId)
        {
            var path = MatchPath(matchId);
            if (File.Exists(path)) File.Delete(path);
        }

        // 쓰다가 꺼져도 반쯤 쓴 파일이 남지 않게: 임시 파일에 쓰고 바꿔 넣는다.
        static void WriteAtomic(string path, string text)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }
    }
}
