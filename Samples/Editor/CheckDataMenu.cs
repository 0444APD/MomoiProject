using System.Linq;
using GBible.Data;
using GBible.EditorTools;
using UnityEditor;

namespace GBible.Samples
{
    // DB 추가 메뉴: 확인용 패널이 쓰는 DB 항목을 만든다(상태 효과 2·밸런스 값·가중치 표·인스턴스 4·설정 4·개발 명령 1).
    // 항목은 DB에 '적용 전'으로 들어간다. DB에서 확인하고 Ctrl+S로 적용한다. 이미 있는 ID는 건너뛴다.
    public static class CheckDataMenu
    {
        [MenuItem("MomoiProject/DB 추가/확인용 항목", priority = 60)]
        public static void Create()
        {
            var made = 0;

            made += Add<StatusData>(CheckIds.Poison, s =>
            {
                var d = CheckDefaults.Poison();
                s.maxStacks = d.MaxStacks;
                s.reapply = d.Reapply;
                s.duration = new StatusDurationSettings { kind = d.Duration, length = d.Length, tickEvent = d.TickEvent };
                s.trigger = d.When;
                s.outcomes.AddRange(d.Outcomes);
            });
            made += Add<StatusData>(CheckIds.Guard, s =>
            {
                var d = CheckDefaults.Guard();
                s.maxStacks = d.MaxStacks;
                s.reapply = d.Reapply;
                s.duration = new StatusDurationSettings { kind = d.Duration, length = d.Length, tickEvent = d.TickEvent };
                s.modifiers.AddRange(d.Modifiers);
                s.trigger = d.When;
                s.holderField = d.HolderField;
                s.outcomes.AddRange(d.Outcomes);
            });
            made += Add<BalanceData>(CheckIds.EnemyHp, b =>
            {
                b.value = 20;
                b.curve = CheckDefaults.EnemyHp();
                b.note = "확인용: 층마다 +5";
            });
            foreach (var (id, name) in new[] { ("checkSword", "검"), ("checkShield", "방패"), ("checkPotion", "물약"), ("checkGem", "보석") })
                made += Add<InstanceData>(id, i =>
                {
                    i.instanceType = typeof(CheckItem).FullName;
                    i.value = new CheckItem { name = name };
                });
            made += Add<WeightTableData>(CheckIds.Loot, t =>
            {
                var d = CheckDefaults.Loot();
                t.emptyWeight = d.EmptyWeight;
                t.entries.AddRange(d.Entries.Select(e => new WeightTableEntry { item = e.item, weight = e.weight, minCount = e.minCount, maxCount = e.maxCount }));
            });
            made += Add<SettingData>("checkMasterVolume", s =>
            {
                s.category = "소리";
                s.target = SettingTarget.MasterVolume;
                s.spec = new SettingSpec { kind = SettingKind.Slider, defaultNumber = 0.8, min = 0, max = 1, step = 0.05 };
            });
            made += Add<SettingData>("checkLanguage", s =>
            {
                s.category = "게임";
                s.target = SettingTarget.Language;
                s.spec = new SettingSpec { kind = SettingKind.Choice };
            });
            made += Add<SettingData>("checkSubtitles", s =>
            {
                s.category = "게임";
                s.spec = new SettingSpec { kind = SettingKind.Toggle, defaultToggle = true };
            });
            made += Add<SettingData>("checkFullscreen", s =>
            {
                s.category = "화면";
                s.target = SettingTarget.Fullscreen;
                s.spec = new SettingSpec { kind = SettingKind.Toggle, defaultToggle = true };
            });
            made += Add<DebugCommandData>("checkChoice", c =>
            {
                c.label = "선택 요구 시험";
                c.category = "확인용";
                c.description = "확인용 유닛 중 하나를 고르게 하고, 고른 쪽에 확인용 피해 3.";
                var choice = new ChoiceOutcome { prompt = "때릴 대상", min = 1, max = 1 };
                choice.candidates.Add(new CheckAllUnits());
                choice.effects.Add(new CheckHurt { amount = 3 });
                c.outcomes.Add(choice);
            });

            EditorDatabase.Staging.Touch();
            DatabaseWindow.Open();
            EditorUtility.DisplayDialog("확인용 항목",
                made == 0 ? "확인용 항목이 이미 모두 있습니다." : $"{made}개를 적용 전으로 만들었습니다. DB에서 확인하고 Ctrl+S로 적용하세요.", "확인");
        }

        static int Add<T>(string id, System.Action<T> fill) where T : GameData
        {
            if (EditorDatabase.Instance.FindById(id) != null) return 0;
            var item = (T)EditorDatabase.Staging.Add(typeof(T), id);
            fill(item);
            return 1;
        }
    }
}
