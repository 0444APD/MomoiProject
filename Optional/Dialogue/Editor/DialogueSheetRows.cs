using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using GBible.Dialogue;

namespace GBible.EditorTools.Graph
{
    // 텍스트 시트에 다이얼로그 대본을 더한다: 다이얼로그마다 재생 순서대로 대사·선택지, 문맥 열은 다이얼로그 · 장소 · 화자.
    // 그 밖의 연출 정보는 시트에 내보내지 않는다(필요하면 대사 노드의 '메모'가 메모 칸으로 나간다).
    sealed class DialogueSheetRows : ITextSheetSource
    {
        public const string DialogueColumn = "dialogue";
        public const string LocationColumn = "location";
        public const string SpeakerColumn = "speaker";

        public IEnumerable<TextSheetRow> Rows(Func<string, string> sourceText)
        {
            var db = EditorDatabase.Instance;
            var presets = db.Committed.OfType<EffectPresetData>().ToDictionary(p => p.Id);
            var locations = db.Committed.OfType<LocationData>().ToDictionary(l => l.Id);
            string Name(string id) => string.IsNullOrEmpty(id) ? "" : sourceText(id + ".name") ?? id;

            foreach (var dialogue in db.Committed.OfType<DialogueData>().OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                var rows = DialogueScript.Walk(dialogue, id => id != null && presets.TryGetValue(id, out var p) ? p : null,
                    id => id != null && locations.TryGetValue(id, out var l) ? l : null);
                foreach (var row in rows)
                {
                    var line = row.Line;
                    var context = new Dictionary<string, string>
                    {
                        [DialogueColumn] = dialogue.Id,
                        [LocationColumn] = Name(row.Location),
                        [SpeakerColumn] = row.IsChoice ? "▶ 선택지" : Speaker(line, Name),
                    };
                    yield return new TextSheetRow { Key = row.Key, Source = row.Source, Context = context, Note = row.IsChoice ? "" : line.comment };
                    if (row.IsChoice) continue;
                    if (!string.IsNullOrEmpty(line.speakerName))
                        yield return new TextSheetRow { Key = line.lineId + ".speaker", Source = line.speakerName, Context = WithSpeaker(context, "(화자 이름)") };
                    foreach (var extra in line.extras.Where(e => !string.IsNullOrEmpty(e.key)))
                        yield return new TextSheetRow { Key = line.lineId + "." + extra.key, Source = extra.text, Context = WithSpeaker(context, $"(추가 글: {extra.key})") };
                }
            }

            // 프로필·장소의 추가 글과, 아직 원문 표에 없는 이름.
            foreach (var profile in db.Committed.OfType<StandingProfileData>().OrderBy(p => p.Id, StringComparer.Ordinal))
            {
                if (sourceText(profile.Id + ".name") == null)
                    yield return new TextSheetRow { Key = profile.Id + ".name", Source = profile.Id, Context = new Dictionary<string, string> { [SpeakerColumn] = "(프로필 이름)" }, Note = "원문 표에 이름이 없어 ID를 넣었습니다" };
                foreach (var extra in profile.extras.Where(e => !string.IsNullOrEmpty(e.key)))
                    yield return new TextSheetRow { Key = profile.Id + "." + extra.key, Source = extra.text, Context = new Dictionary<string, string> { [SpeakerColumn] = $"{Name(profile.Id)} (추가 글: {extra.key})" } };
            }
            foreach (var location in db.Committed.OfType<LocationData>().OrderBy(l => l.Id, StringComparer.Ordinal))
            {
                if (sourceText(location.Id + ".name") == null)
                    yield return new TextSheetRow { Key = location.Id + ".name", Source = location.Id, Context = new Dictionary<string, string> { [LocationColumn] = "(장소 이름)" }, Note = "원문 표에 이름이 없어 ID를 넣었습니다" };
                foreach (var extra in location.extras.Where(e => !string.IsNullOrEmpty(e.key)))
                    yield return new TextSheetRow { Key = location.Id + "." + extra.key, Source = extra.text, Context = new Dictionary<string, string> { [LocationColumn] = $"{Name(location.Id)} (추가 글: {extra.key})" } };
            }
        }

        // 시트의 화자 칸: 다른 이름 → 프로필 이름 → 해설. 생각이면 '(생각)'.
        public static string Speaker(LineNode line, Func<string, string> name)
        {
            var who = !string.IsNullOrEmpty(line.speakerName) ? line.speakerName : string.IsNullOrEmpty(line.speaker) ? "해설" : name(line.speaker);
            return line.thought ? who + " (생각)" : who;
        }

        static Dictionary<string, string> WithSpeaker(Dictionary<string, string> context, string speaker) =>
            new Dictionary<string, string>(context) { [SpeakerColumn] = speaker };
    }
}
