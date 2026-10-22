using System;
using System.Collections.Generic;
using System.Linq;
using GBible.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

namespace GBible.EditorTools
{
    // 글꼴: GBible 기본 글꼴(Pretendard, SIL OFL 1.1 — 패키지 Fonts/Pretendard)로 프로젝트에 UI Toolkit 글꼴 에셋 세 벌(Regular · SemiBold · Bold)을 만들고
    // 화면(DB › 화면 설정 '글꼴 에셋' — ScreenSetup.Ensure가 넣는다)과 다이얼로그(DialogueUI 글꼴 — 다이얼로그 모듈이 Current · Changed로 받는다)에 쓴다.
    // 글꼴 에셋은 패키지가 아니라 프로젝트(Assets/MomoiProject/UI/Fonts)에 둔다 — 동적 에셋은 새 글자를 그릴 때마다 파일이 바뀌어, 패키지에 두면 GBible 저장소가 프로젝트마다 바뀐다.
    // 프로젝트가 글꼴을 바꾸면(Replace — 개발자 요청) GBible 기본 에셋은 Fonts/Default로 빼 두고(더미 — 지우지 않는다) 새 글꼴이 Fonts/를 덮는다. Restore가 되돌린다.
    public static class FontSetup
    {
        public const string PackageFonts = "Packages/com.momoi.gbible/Fonts/Pretendard";
        public const string Folder = ProjectFolders.UI + "/Fonts";
        public const string DefaultFolder = Folder + "/Default";
        static readonly string[] DefaultNames = { "Pretendard-Regular", "Pretendard-SemiBold", "Pretendard-Bold" };

        // 글꼴 에셋을 만드는 값. Default = GBible 기본(UI 템플릿 실험실에서 정한 값 — 2026-10-21 기본으로 확정).
        [Serializable]
        public struct AssetSettings
        {
            public int samplingPointSize;
            public int padding;
            public int atlasSize;
            public GlyphRenderMode renderMode;

            public static AssetSettings Default => new AssetSettings { samplingPointSize = 64, padding = 6, atlasSize = 2048, renderMode = GlyphRenderMode.SDFAA };
        }

        // 굵기 세 벌. 화면 글은 Regular(굵게 = Bold), 다이얼로그는 SemiBold(굵게 = Bold) — 굵기는 Regular 에셋의 굵기 표(600 · 700)에 잇는다.
        public sealed class FontSet
        {
            public FontAsset regular, semiBold, bold;
        }

        // 글꼴을 바꾸거나 되돌렸을 때(Replace · Restore). 다이얼로그 모듈이 받아 기본 프리팹의 글꼴을 바꾼다.
        public static event Action<FontSet> Changed;

        // GBible 기본 세 벌(Fonts/에 있으면 그것, 바꾼 프로젝트면 Fonts/Default의 것). 둘 다 없으면 패키지 글꼴로 Fonts/에 만든다.
        public static FontSet Defaults(List<string> made = null)
        {
            var set = Load(Folder) ?? Load(DefaultFolder);
            if (set != null)
            {
                Link(set);
                return set;
            }
            var fonts = DefaultNames.Select(n => AssetDatabase.LoadAssetAtPath<Font>($"{PackageFonts}/{n}.otf")).ToArray();
            if (fonts.Any(f => f == null))
            {
                Debug.LogWarning($"[MomoiProject] GBible 기본 글꼴({PackageFonts})을 찾지 못해 글꼴 에셋을 만들지 않았습니다.");
                return null;
            }
            set = Make(fonts[0], fonts[1], fonts[2], Folder, AssetSettings.Default);
            made?.Add($"{Folder}/Pretendard-Regular · SemiBold · Bold.asset (GBible 기본 글꼴)");
            return set;
        }

        // 지금 쓰는 세 벌: DB › 화면 설정의 '글꼴 에셋'과 그 굵기 표. 칸이 비었으면 GBible 기본.
        public static FontSet Current()
        {
            var regular = ScreenSettings()?.fontAsset;
            if (regular == null) return Defaults();
            var semiBold = regular.fontWeightTable[6].regularTypeface;
            return new FontSet { regular = regular, semiBold = semiBold != null ? semiBold : regular, bold = regular.fontWeightTable[7].regularTypeface };
        }

        // 프로젝트 글꼴로 바꾼다(개발자 요청 — 관례 6절): GBible 기본 에셋을 Fonts/Default로 빼 두고, 새 글꼴로 Fonts/에 세 벌을 만들어
        // 화면 설정 '글꼴 에셋'(작업본 — DB 반영 스크립트 안에서 부르면 돌고 나서 바로 적용된다)과 다이얼로그 글꼴을 그쪽으로 돌린다.
        // semiBold가 없으면 Regular를 쓰고, bold가 없으면 굵은 글은 가짜 굵게로 그려진다.
        public static FontSet Replace(Font regular, Font semiBold = null, Font bold = null, AssetSettings? settings = null)
        {
            if (regular == null) throw new ArgumentNullException(nameof(regular));
            Move(Folder, DefaultFolder);
            var set = Make(regular, semiBold, bold, Folder, settings ?? AssetSettings.Default);
            Assign(set);
            return set;
        }

        // GBible 기본으로 되돌린다: Fonts/Default의 기본 에셋을 Fonts/로 다시 옮기고 칸을 돌린다(바꿔 쓰던 글꼴 에셋은 Fonts/에 그대로 남는다).
        public static FontSet Restore()
        {
            Move(DefaultFolder, Folder);
            var set = Defaults();
            if (set != null) Assign(set);
            return set;
        }

        // 글꼴 세 벌을 folder에 만든다(같은 이름의 에셋이 있으면 그것을 쓴다). 굵기 표까지 잇는다.
        public static FontSet Make(Font regular, Font semiBold, Font bold, string folder, AssetSettings settings)
        {
            EditorDatabase.EnsureFolder(folder);
            var set = new FontSet { regular = MakeOne(regular, folder, settings) };
            set.semiBold = semiBold == null || semiBold == regular ? set.regular : MakeOne(semiBold, folder, settings);
            set.bold = bold == null ? null : MakeOne(bold, folder, settings);
            Link(set);
            AssetDatabase.SaveAssets();
            return set;
        }

        static FontAsset MakeOne(Font font, string folder, AssetSettings settings)
        {
            var path = $"{folder}/{font.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<FontAsset>(path);
            if (existing != null) return existing;
            var asset = FontAsset.CreateFontAsset(font, settings.samplingPointSize, settings.padding, settings.renderMode, settings.atlasSize, settings.atlasSize, AtlasPopulationMode.Dynamic, true);
            asset.name = font.name;
            AssetDatabase.CreateAsset(asset, path);
            asset.atlasTextures[0].name = font.name + " Atlas";
            AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
            asset.material.name = font.name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            return asset;
        }

        // 굵기 표: Regular의 600 = SemiBold · 700 = Bold, SemiBold의 700 = Bold(굵게 쓴 글이 진짜 굵은 글꼴로 그려지게).
        static void Link(FontSet set)
        {
            if (set.semiBold != set.regular) LinkWeight(set.regular, 6, set.semiBold);
            LinkWeight(set.regular, 7, set.bold);
            if (set.semiBold != set.regular) LinkWeight(set.semiBold, 7, set.bold);
        }

        static void LinkWeight(FontAsset asset, int weight, FontAsset face)
        {
            if (asset == null || face == null || asset.fontWeightTable[weight].regularTypeface == face) return;
            asset.fontWeightTable[weight].regularTypeface = face;
            EditorUtility.SetDirty(asset);
        }

        static FontSet Load(string folder)
        {
            var assets = DefaultNames.Select(n => AssetDatabase.LoadAssetAtPath<FontAsset>($"{folder}/{n}.asset")).ToArray();
            return assets.Any(a => a == null) ? null : new FontSet { regular = assets[0], semiBold = assets[1], bold = assets[2] };
        }

        // GBible 기본 세 벌을 옮긴다(MoveAsset — GUID가 그대로라 가리키던 칸이 끊기지 않는다).
        static void Move(string from, string to)
        {
            if (Load(from) == null) return;
            EditorDatabase.EnsureFolder(to);
            foreach (var name in DefaultNames)
            {
                var error = AssetDatabase.MoveAsset($"{from}/{name}.asset", $"{to}/{name}.asset");
                if (!string.IsNullOrEmpty(error)) Debug.LogWarning($"[MomoiProject] 글꼴 에셋 {name}을 {to}로 옮기지 못했습니다: {error}");
            }
        }

        static void Assign(FontSet set)
        {
            var settings = ScreenSettings();
            if (settings != null && settings.fontAsset != set.regular)
            {
                var work = (ScreenSettingsData)EditorDatabase.Staging.Work(settings);
                work.fontAsset = set.regular;
                EditorDatabase.Staging.Touch();
                Debug.Log($"[MomoiProject] DB › 화면 설정 '글꼴 에셋'을 {set.regular.name}(으)로 바꿨습니다(반영 스크립트 안이면 돌고 나서 바로 적용, 밖이면 '적용 전' — Ctrl+S).");
            }
            Changed?.Invoke(set);
        }

        static ScreenSettingsData ScreenSettings() => EditorDatabase.Instance.OfType<ScreenSettingsData>().FirstOrDefault();
    }
}
