using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using UnityEngine;

namespace GBible.Dialogue
{
    // 다이얼로그 ID → 데이터(받은 행동을 다시 돌릴 때·멀티 서버의 확인). 기본은 게임 데이터베이스. 테스트는 Source를 바꿔 끼운다.
    public static class DialogueLookup
    {
        public static Func<string, DialogueData> Source;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Source = null;

        public static DialogueData Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (Source != null) return Source(id);
            return GameDatabase.Load()?.All<DialogueData>().FirstOrDefault(d => d != null && d.Id == id);
        }
    }

    // 다이얼로그 진행 기록 칸.
    //  · 혼자 하기: dialogue.progress(세션 구역 — 판 저장에 들어간다). 다이얼로그 재생기가 행동 밖에서 바로 쓴다(Undo 이력에 넣지 않는다).
    //  · 멀티: 플레이어마다 dialogue.progress:<번호>. 다이얼로그 재생기가 DialogueProgressAction으로 보내면 서버(방장)가 남기고
    //    그 플레이어에게만 보낸다(DataVisibility.Owner) — 다른 사람의 진행이 섞이거나 덮이지 않는다.
    public static class DialogueProgressKeys
    {
        public static string For(GameSession session, int player) =>
            session == null || session.Role == SessionRole.Local ? DialoguePlayer.ProgressKey : DialoguePlayer.ProgressKey + ":" + player;

        // 칸을 정의한다(멀티면 주인만 보는 칸). 판을 불러온 뒤면 저장된 값이 들어온다.
        public static void Prepare(GameSession session, int player)
        {
            if (session == null) return;
            var key = For(session, player);
            if (session.Data.IsDefined(key)) return;
            session.Data.Define(key, DataZone.Session, "");
            if (session.Role != SessionRole.Local) session.Data.SetVisibility(key, DataVisibility.Owner, player);
        }
    }

    // 다이얼로그 진행 기록을 남긴다(멀티 — 서버가 실행해 보낸 플레이어의 칸에 쓰고, 값 바꾸기 확인의 기준 자리로 삼는다).
    // 혼자 하기에서는 다이얼로그 재생기가 쓰지 않는다(대사마다 Undo 단위가 생기지 않게 행동 밖에서 바로 남긴다).
    [Serializable]
    public sealed class DialogueProgressAction : GameAction
    {
        // DialogueProgress의 JSON(비면 다이얼로그가 끝남).
        public string progress;

        public override ActionVisibility Visibility => ActionVisibility.Private;
        public override string Describe() => "다이얼로그 진행 기록";

        protected override void Run()
        {
            var session = Game.Session;
            DialogueProgressKeys.Prepare(session, actorId);
            session.Data.Set(DialogueProgressKeys.For(session, actorId), progress ?? "");
            DialogueGuard.Moved(session, actorId, progress);
        }
    }

    // 멀티 서버의 다이얼로그 확인: 클라이언트가 보낸 '값 바꾸기'(DialogueSetAction)가 그 플레이어가 마지막으로 알린 자리
    // (DialogueProgressAction — 대사, 또는 대사 전이면 시작)에서 실제로 닿는 노드인지 서버 데이터로 본다.
    //  · 분기·선택지 조건은 서버 값으로 따진다. 프로젝트 흐름 노드는 서버에서 돌리지 않으므로 나가는 점을 모두 본다.
    //  · 앞의 값 바꾸기를 건너뛰고 뒤의 것만 보낼 수 없고, 같은 것을 다시 보낼 수 없다(확인은 마지막으로 한 값 바꾸기 뒤부터).
    //  · 방장 자신의 행동과 혼자 하기는 보지 않는다.
    // 한계: 다이얼로그를 여는 것(어느 다이얼로그·시작 ID)은 게임 코드가 정하므로 확인하지 않고, 진행 기록(자리)도 클라이언트가 보낸 것을 믿는다.
    static class DialogueGuard
    {
        public const string RefusedKey = "momoi.dialogue.outOfStep";

        // 서버만 보는 칸(dialogue.guard:<번호>)에 JSON으로 남긴다.
        [Serializable]
        sealed class Position
        {
            // 지금 다이얼로그 · 대사 전이면 시작 ID · 마지막으로 알린 대사(비면 시작에서).
            public string at;
            public string entry;
            public string line;
            // 이 자리에서 마지막으로 한 값 바꾸기(다음 확인은 그 뒤부터).
            public string afterDialogue;
            public string afterNode;
        }

        static bool Applies(GameSession session, int player) => session != null && session.Role == SessionRole.Server && player != session.LocalPlayer;

        static string KeyOf(int player) => "dialogue.guard:" + player;

        public static void Moved(GameSession session, int player, string progressJson)
        {
            if (!Applies(session, player)) return;
            DialogueProgress progress = null;
            if (!string.IsNullOrEmpty(progressJson))
            {
                try
                {
                    progress = JsonUtility.FromJson<DialogueProgress>(progressJson);
                }
                catch (Exception)
                {
                    progress = null;
                }
            }
            Save(session, player, progress == null
                ? null
                : new Position { at = string.IsNullOrEmpty(progress.at) ? progress.dialogue : progress.at, entry = progress.entry, line = progress.node });
        }

        // 거절할 이유(사람이 읽는 글). 확인할 필요가 없거나 닿으면 null.
        public static string CheckSet(GameSession session, int player, string dialogueId, string nodeGuid)
        {
            if (!Applies(session, player)) return null;
            var position = Load(session, player);
            if (position == null || string.IsNullOrEmpty(position.at)) return "진행 기록 없음";
            var starts = new List<(DialogueData dialogue, GraphNode node)>();
            if (!string.IsNullOrEmpty(position.afterNode))
            {
                var after = DialogueLookup.Find(position.afterDialogue);
                if (!(after?.graph.Find(position.afterNode) is SetNode set)) return "마지막 값 바꾸기를 찾지 못함";
                starts.Add((after, after.graph.Next(set, SetNode.NextPort)));
            }
            else
            {
                var current = DialogueLookup.Find(position.at);
                if (current == null) return $"없는 다이얼로그 '{position.at}'";
                if (!string.IsNullOrEmpty(position.line))
                {
                    if (!(current.graph.Find(position.line) is LineNode line)) return "없는 대사";
                    foreach (var port in ExitPorts(session, line)) starts.Add((current, current.graph.Next(line, port)));
                }
                else
                {
                    var entry = current.Entry(position.entry);
                    if (entry == null) return $"없는 시작 ID '{position.entry}'";
                    starts.Add((current, current.graph.Next(entry, StartNode.NextPort)));
                }
            }
            return Reaches(session, starts, dialogueId, nodeGuid) ? null : "지금 자리에서 닿지 않는 노드";
        }

        // 값 바꾸기를 했다: 다음 확인은 그 뒤부터.
        public static void Granted(GameSession session, int player, string dialogueId, string nodeGuid)
        {
            if (!Applies(session, player)) return;
            var position = Load(session, player);
            if (position == null) return;
            position.afterDialogue = dialogueId;
            position.afterNode = nodeGuid;
            Save(session, player, position);
        }

        // 대사에서 나갈 수 있는 점: 서버 값으로 고를 수 있는 선택지, 하나도 없으면 '고를 선택지 없음'.
        static IEnumerable<string> ExitPorts(GameSession session, LineNode line)
        {
            if (line.options.Count == 0)
            {
                yield return LineNode.NextPort;
                yield break;
            }
            var any = false;
            foreach (var option in line.options)
            {
                if (option.condition != null && !option.condition.Evaluate(session)) continue;
                any = true;
                yield return LineNode.OptionPrefix + option.id;
            }
            if (!any) yield return LineNode.NoChoicePort;
        }

        static bool Reaches(GameSession session, List<(DialogueData dialogue, GraphNode node)> starts, string dialogueId, string nodeGuid)
        {
            var stack = new Stack<(DialogueData dialogue, GraphNode node)>(starts);
            var seen = new HashSet<GraphNode>();
            while (stack.Count > 0)
            {
                var (d, node) = stack.Pop();
                if (d == null || node == null || !seen.Add(node)) continue;
                switch (node)
                {
                    case SetNode set:
                        // 찾던 것이면 닿는다. 다른 값 바꾸기면 여기서 막힌다(그것을 먼저 해야 한다).
                        if (set.guid == nodeGuid && d.Id == dialogueId) return true;
                        break;
                    case BranchNode branch:
                        var pass = branch.condition == null || branch.condition.Evaluate(session);
                        stack.Push((d, d.graph.Next(branch, pass ? BranchNode.TruePort : BranchNode.FalsePort)));
                        break;
                    case SignalNode signal:
                        stack.Push((d, d.graph.Next(signal, SignalNode.NextPort)));
                        break;
                    case JumpNode jump:
                        var target = string.IsNullOrEmpty(jump.dialogue) ? d : DialogueLookup.Find(jump.dialogue);
                        var entry = target?.Entry(jump.entryId);
                        if (entry != null) stack.Push((target, target.graph.Next(entry, StartNode.NextPort)));
                        break;
                    case CustomFlowNode custom:
                        foreach (var port in custom.Ports)
                            if (port.Kind == PortKind.Flow && port.Side == PortSide.Out)
                                stack.Push((d, d.graph.Next(custom, port.Id)));
                        break;
                }
            }
            return false;
        }

        static Position Load(GameSession session, int player)
        {
            var key = KeyOf(player);
            if (!session.Data.IsDefined(key)) return null;
            var json = session.Data.Get<string>(key);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonUtility.FromJson<Position>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        static void Save(GameSession session, int player, Position position)
        {
            var key = KeyOf(player);
            if (!session.Data.IsDefined(key))
            {
                session.Data.Define(key, DataZone.Session, "");
                session.Data.SetVisibility(key, DataVisibility.Server);
            }
            session.Data.Set(key, position == null ? "" : JsonUtility.ToJson(position));
        }
    }
}
