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

    // 상세 패널 점검용: 필드 종류와 개수가 많은 도감 항목. 스크롤, 종류별 입력 칸, 긴 글 10줄 제한, 배치 편집을 눈으로 확인한다.
    // 칸 이름표는 변수 이름이고, [FieldLabel] 글자는 마우스를 올리면 보이는 설명이다.
    // 메뉴 Game Systems › 샘플 › 필드 시험 항목 만들기로 값이 채워진 항목을 만든다.
    [Database("필드 시험", Group = "샘플", Order = 900, IdPrefix = "fieldTest")]
    public sealed class SampleFieldTestData : GameData
    {
        [FieldLabel("정수")] public int integer = 3;
        [FieldLabel("큰 정수")] public long bigInteger = 9_000_000_000;
        [FieldLabel("실수")] public float ratio = 0.25f;
        [FieldLabel("배정밀도 실수")] public double precise = 3.14159;
        [FieldLabel("범위가 정해진 정수"), Range(0, 100)] public int ranged = 40;

        [FieldLabel("짧은 글")] public string shortText = "짧은 글";
        [FieldLabel("긴 글(10줄 넘음 → 칸 안에서 스크롤)"), TextArea] public string longText = "";
        [FieldLabel("빈 글(비어 있음 모양)")] public string emptyText = "";
        [FieldLabel("여러 줄 글"), TextArea] public string multiLine = "첫째 줄\n둘째 줄\n셋째 줄";

        [FieldLabel("참거짓")] public bool toggle = true;
        [FieldLabel("enum")] public SampleFieldMood mood = SampleFieldMood.Sleepy;
        [FieldLabel("로그(수치) 참조"), StatRef] public string stat = "";
        [FieldLabel("도전과제 참조"), AchievementRef] public string achievement = "";
        [FieldLabel("키워드 하나(string)")] public string keyword = "";
        [FieldLabel("키워드 여러 개(string 리스트)")] public List<string> keywords = new List<string>();

        [FieldLabel("스프라이트")] public Sprite icon;
        [FieldLabel("프리팹")] public GameObject prefab;
        [FieldLabel("색")] public Color tint = new Color(0.99f, 0.36f, 0.51f);
        [FieldLabel("2D 좌표")] public Vector2 offset = new Vector2(1, 2);
        [FieldLabel("3D 좌표")] public Vector3 position = new Vector3(1, 2, 3);

        [FieldLabel("int 배열")] public int[] numberArray = { 1, 2, 3 };
        [FieldLabel("int 리스트")] public List<int> numberList = new List<int> { 4, 5 };
        [FieldLabel("string 리스트")] public List<string> words = new List<string> { "하나", "둘" };
        [FieldLabel("클래스 하나")] public SampleFieldNested nested = new SampleFieldNested { label = "안쪽", amount = 5 };
        [FieldLabel("클래스 배열")] public SampleFieldNested[] nestedArray =
        {
            new SampleFieldNested { label = "배열 첫째", amount = 1 },
        };
        [FieldLabel("클래스 리스트")] public List<SampleFieldNested> nestedList = new List<SampleFieldNested>
        {
            new SampleFieldNested { label = "리스트 첫째", amount = 1 },
            new SampleFieldNested { label = "리스트 둘째", amount = 2 },
        };
        [FieldLabel("조건식"), SerializeReference] public Condition condition;

        // 스크롤 확인용: 패널이 창보다 길어지도록 칸 수를 늘린다(내용에는 의미 없음).
        [FieldLabel("스크롤 확인용")] public int scrollFillerInt1 = 1;
        [FieldLabel("스크롤 확인용")] public int scrollFillerInt2 = 2;
        [FieldLabel("스크롤 확인용")] public string scrollFillerText1 = "스크롤 확인용";
        [FieldLabel("스크롤 확인용")] public string scrollFillerText2 = "";
        [FieldLabel("스크롤 확인용")] public float scrollFillerFloat = 1.5f;
        [FieldLabel("스크롤 확인용")] public bool scrollFillerBool;
        [FieldLabel("스크롤 확인용")] public SampleFieldMood scrollFillerEnum;
    }
}
