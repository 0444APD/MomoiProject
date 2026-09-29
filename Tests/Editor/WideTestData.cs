using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameSystems.Tests
{
    public enum WideMood
    {
        Calm,
        Angry,
    }

    [Serializable]
    public sealed class WideNested
    {
        [FieldLabel("안쪽 이름")] public string label = "";
        [FieldLabel("안쪽 값")] public int amount;
    }

    // 상세 패널 점검용: 필드 종류와 개수가 많은 데이터(파일 이름 = 클래스 이름이어야 Unity가 스크립트를 찾는다).
    public sealed class WideTestData : ScriptableObject
    {
        [FieldLabel("정수")] public int integer = 3;
        [FieldLabel("큰 정수")] public long bigInteger = 9_000_000_000;
        [FieldLabel("실수")] public float ratio = 0.25f;
        [FieldLabel("배정밀도")] public double precise = 3.14;
        [FieldLabel("짧은 글")] public string shortText = "짧은 글";
        [FieldLabel("긴 글"), TextArea] public string longText = "";
        [FieldLabel("빈 글")] public string emptyText = "";
        [FieldLabel("참거짓")] public bool toggle = true;
        [FieldLabel("enum")] public WideMood mood = WideMood.Angry;
        [FieldLabel("아이콘")] public Sprite icon;
        [FieldLabel("프리팹")] public GameObject prefab;
        [FieldLabel("색")] public Color tint = Color.magenta;
        [FieldLabel("2D 좌표")] public Vector2 offset = new Vector2(1, 2);
        [FieldLabel("숫자 목록")] public List<int> numbers = new List<int> { 1, 2, 3 };
        [FieldLabel("글자 목록")] public List<string> words = new List<string> { "하나" };
        [FieldLabel("클래스")] public WideNested nested = new WideNested { label = "안쪽", amount = 5 };
        [FieldLabel("클래스 목록")] public List<WideNested> nestedList = new List<WideNested> { new WideNested { label = "첫째" } };
        [FieldLabel("조건"), SerializeReference] public Condition condition;
        [FieldLabel("추가 1")] public int extra1;
        [FieldLabel("추가 2")] public int extra2;
        [FieldLabel("추가 3")] public string extra3 = "추가";
        [FieldLabel("추가 4")] public float extra4;
        [FieldLabel("추가 5")] public bool extra5;
        [FieldLabel("추가 6")] public WideMood extra6;
    }
}
