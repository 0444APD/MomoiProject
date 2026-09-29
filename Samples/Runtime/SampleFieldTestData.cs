using System;
using System.Collections.Generic;
using GameSystems.Data;
using UnityEngine;

namespace GameSystems.Samples
{
    public enum SampleFieldMood
    {
        Calm,
        Angry,
        Sleepy,
    }

    [Serializable]
    public sealed class SampleFieldNested
    {
        [FieldLabel("이름")] public string label = "";
        [FieldLabel("값")] public int amount;
        [FieldLabel("켜짐")] public bool enabled = true;
    }

    // 상세 패널 점검용: 필드 종류와 개수가 많은 도감 항목. 스크롤, 종류별 입력 칸, 긴 글 5줄 제한, 배치 편집을 눈으로 확인한다.
    // 메뉴 Game Systems › 샘플 › 필드 시험 항목 만들기로 값이 채워진 항목을 만든다.
    [Database("필드 시험", Group = "샘플", Order = 900, IdPrefix = "fieldTest")]
    public sealed class SampleFieldTestData : GameData
    {
        [Header("숫자")]
        [FieldLabel("정수")] public int integer = 3;
        [FieldLabel("큰 정수")] public long bigInteger = 9_000_000_000;
        [FieldLabel("실수")] public float ratio = 0.25f;
        [FieldLabel("배정밀도")] public double precise = 3.14159;
        [FieldLabel("범위 값"), Range(0, 100)] public int ranged = 40;

        [Header("글자")]
        [FieldLabel("짧은 글")] public string shortText = "짧은 글";
        [FieldLabel("긴 글(5줄 넘음)"), TextArea] public string longText = "";
        [FieldLabel("빈 글")] public string emptyText = "";
        [FieldLabel("여러 줄 글"), TextArea] public string multiLine = "첫째 줄\n둘째 줄\n셋째 줄";

        [Header("선택")]
        [FieldLabel("참거짓")] public bool toggle = true;
        [FieldLabel("enum")] public SampleFieldMood mood = SampleFieldMood.Sleepy;
        [FieldLabel("수치 참조"), StatRef] public string stat = "";
        [FieldLabel("도전과제 참조"), AchievementRef] public string achievement = "";
        [FieldLabel("키워드(하나)")] public string keyword = "";
        [FieldLabel("키워드(여러 개)")] public List<string> keywords = new List<string>();

        [Header("Unity")]
        [FieldLabel("아이콘")] public Sprite icon;
        [FieldLabel("프리팹")] public GameObject prefab;
        [FieldLabel("색")] public Color tint = new Color(0.99f, 0.36f, 0.51f);
        [FieldLabel("2D 좌표")] public Vector2 offset = new Vector2(1, 2);
        [FieldLabel("3D 좌표")] public Vector3 position = new Vector3(1, 2, 3);

        [Header("묶음")]
        [FieldLabel("숫자 목록")] public List<int> numbers = new List<int> { 1, 2, 3 };
        [FieldLabel("글자 목록")] public List<string> words = new List<string> { "하나", "둘" };
        [FieldLabel("클래스")] public SampleFieldNested nested = new SampleFieldNested { label = "안쪽", amount = 5 };
        [FieldLabel("클래스 목록")] public List<SampleFieldNested> nestedList = new List<SampleFieldNested>
        {
            new SampleFieldNested { label = "첫째", amount = 1 },
            new SampleFieldNested { label = "둘째", amount = 2 },
        };
        [FieldLabel("조건"), SerializeReference] public Condition condition;

        [Header("더 많은 칸(스크롤 확인)")]
        [FieldLabel("추가 정수 1")] public int extra1 = 1;
        [FieldLabel("추가 정수 2")] public int extra2 = 2;
        [FieldLabel("추가 정수 3")] public int extra3 = 3;
        [FieldLabel("추가 글 1")] public string extraText1 = "추가 글";
        [FieldLabel("추가 글 2")] public string extraText2 = "";
        [FieldLabel("추가 실수 1")] public float extraFloat1 = 1.5f;
        [FieldLabel("추가 참거짓")] public bool extraToggle;
        [FieldLabel("추가 enum")] public SampleFieldMood extraMood;
    }
}
