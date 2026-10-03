// 카드 던전 — 데이터 클래스.
// 모두 평범한 [Serializable] C# 클래스다. 필드를 그냥 고치면 시스템(Mirror)이 Sync할 때 바뀐 곳을 기록한다.
using System;
using System.Collections.Generic;
using GBible;
using GBible.Data;
using GBible.InventorySystem;
using UnityEngine;

namespace CardDungeon
{
    // ── 판(런) 데이터: 플레이어 데이터 "run"(세션 구역). 판이 끝나면 버린다. ──

    // 싸우는 존재(주인공·적)의 공통 부분.
    [Serializable]
    public class Fighter
    {
        public string id;                 // Game.Find로 찾는 id(판 안에서 유일)
        public string name;
        public int hp;
        public int maxHp;
        public int block;                 // 이번 턴 방어도
        [Keyword(typeof(Status))] public List<string> statuses = new List<string>();

        public bool IsDead => hp <= 0;
    }

    [Serializable]
    public class Hero : Fighter
    {
        public int energyPerTurn = 3;
        public int drawPerTurn = 5;
    }

    [Serializable]
    public class Enemy : Fighter
    {
        public string monster;            // 원본 인스턴스 ID(예: "goblin")
        public int intentIndex;           // 공격 패턴에서 이번 턴 차례
    }

    // 덱에 든 카드 한 장(판 데이터): 원본 ID와 이 장만의 바뀌는 값만 든다. 이름·비용·그림·사용 기능 같은 고정 값은
    // 원본(도감 › 인스턴스의 Card)에서 읽는다 — 판 저장·동기화가 작고, 원본을 고치면 모든 장에 바로 반영된다.
    [Serializable]
    public class DeckCard
    {
        public string id;                                           // 장마다 다른 id(Game.NewId("card"))
        [DataRef(typeof(InstanceData))] public string card;         // 원본 인스턴스 ID(능력 이름으로도 쓴다)
        public bool upgraded;
    }

    // 카드 원본(도감 › 인스턴스). 게임 중에는 읽기만 한다.
    [Serializable]
    public class Card
    {
        public string title;
        public int cost = 1;
        [Keyword(typeof(Rarity))] public string rarity = Rarity.Common;
        [Keyword(typeof(Element))] public string element = Element.Neutral;
        [Keyword(typeof(CardTag))] public List<string> tags = new List<string>();
        public Sprite art;
        public bool upgradable;
        [ShowIf(nameof(upgradable)), DataRef(typeof(InstanceData))] public string upgradeTo;   // 강화하면 바뀔 카드(강화 가능할 때만 보인다)
        [Hide] public int balanceScore;   // 밸런스 도구가 채우는 값(기획자 화면에서는 숨긴다)

        // 기능 = 필드 하나. 사용 기능(트리거 + 결과)은 도감에서 조립하거나 [BindTo]로 코드에 쓴다.
        [SerializeReference] public Usable usable;
    }

    // 전투 하나(전투 중일 때만 RunState.battle에 있다).
    [Serializable]
    public class Battle
    {
        public int turn;
        public int energy;
        public List<Enemy> enemies = new List<Enemy>();
        public List<string> drawPile = new List<string>();   // 카드 사본 id
        public List<string> hand = new List<string>();
        public List<string> discard = new List<string>();
        public int damageThisTurn;                            // 로그·도전과제용 집계
    }

    [Serializable]
    public class RunState
    {
        public int floor;
        public int gold;
        public Hero hero = new Hero();
        public List<DeckCard> deck = new List<DeckCard>();
        public Battle battle;                                 // 전투 밖이면 null
        [DataRef(typeof(RelicData))] public List<string> relics = new List<string>();   // 유물 ID(도감 › 유물)
        public Inventory potions = new Inventory { capacity = 3 };

        // 진행 중인 판이 있는지(판이 끝나면 EndRun이 floor를 0으로).
        public bool IsActive => floor > 0;
    }

    // ── 프로필 데이터: 플레이어 데이터 "profile"(메타 구역). 판이 끝나도 남는다. ──

    [Serializable]
    public class Profile
    {
        public int totalGold;
        public int bestFloor;
        [DataRef(typeof(InstanceData))] public List<string> unlocked = new List<string>();   // 해금한 카드 원본 ID(상점·보상 후보에 더한다)
    }

    // ── 도감 › 인스턴스로 원본을 만드는 클래스 ──

    // 몬스터 원본(고블린·슬라임·드래곤 …). 전투를 시작할 때 이 값으로 Enemy를 만든다.
    [Serializable]
    public class MonsterData
    {
        public string title;
        public int maxHp = 20;
        public List<int> attackPattern = new List<int> { 6 };  // 턴마다 차례로 주는 피해
        [Keyword(typeof(Element))] public string element = Element.Neutral;
        public Sprite portrait;
    }

    // 물약 원본. 인벤토리(RunState.potions)에는 원본 ID와 개수만 담는다.
    [Serializable]
    public class Potion
    {
        public string title;
        public int maxStack = 2;
        [SerializeReference] public Usable usable;
    }
}
