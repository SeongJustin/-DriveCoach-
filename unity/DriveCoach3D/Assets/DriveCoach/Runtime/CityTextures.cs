// 건물 외벽 무늬 질감(11/15 코드 정리로 CityBuilder.cs 에서 나눈 파일).
using System;
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    /// <summary>
    /// 건물 외벽 무늬(코드로 그림). 한 장 = 한 층(3.2 m) × 한 칸(Bay m). 반복해서 붙인다.
    /// </summary>
    public static class CityTextures
    {
        public const int ShopColors = 8;   // 상가 간판 질감 수(make_textures.py SHOP_TEX)
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        public static float Bay(string kind)
        {
            switch (kind)
            {
                case "apt": return 3.4f;
                case "office": return 1.8f;
                case "house": return 3.6f;
                case "low": return 4.0f;
                default: return 3.0f;
            }
        }

        static Texture2D New(string key, int w, int hgt)
        {
            var t = new Texture2D(w, hgt, TextureFormat.RGBA32, true) { name = key, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            return t;
        }

        static Color32 C(string hex) { var c = DcAssets.Hex(hex); return c; }

        static void Fill(Color32[] px, int w, int x0, int y0, int x1, int y1, Color32 c, System.Random rnd = null, int noise = 0)
        {
            for (int y = Math.Max(0, y0); y < Math.Min(px.Length / w, y1); y++)
                for (int x = Math.Max(0, x0); x < Math.Min(w, x1); x++)
                {
                    var cc = c;
                    if (rnd != null && noise > 0)
                    {
                        int dn = rnd.Next(-noise, noise + 1);
                        cc = new Color32((byte)Mathf.Clamp(c.r + dn, 0, 255), (byte)Mathf.Clamp(c.g + dn, 0, 255), (byte)Mathf.Clamp(c.b + dn, 0, 255), 255);
                    }
                    px[y * w + x] = cc;
                }
        }

        /// <summary>유리: 위쪽이 조금 밝게(하늘이 비침)</summary>
        static void Glass(Color32[] px, int w, int x0, int y0, int x1, int y1, Color32 c)
        {
            for (int y = y0; y < y1; y++)
            {
                float k = (float)(y - y0) / Math.Max(1, y1 - y0);
                var cc = new Color32((byte)Mathf.Clamp(c.r + k * 40, 0, 255), (byte)Mathf.Clamp(c.g + k * 45, 0, 255), (byte)Mathf.Clamp(c.b + k * 50, 0, 255), 255);
                for (int x = x0; x < x1; x++) px[y * w + x] = cc;
            }
        }

        public static Texture2D Facade(string kind)
        {
            if (cache.TryGetValue("f" + kind, out var t)) return t;
            const int W = 128, H = 128;
            t = New("facade-" + kind, W, H);
            var px = new Color32[W * H];
            var rnd = new System.Random(kind.GetHashCode());
            switch (kind)
            {
                case "apt":
                    // 아파트: 흰 벽, 넓은 거실 창 + 발코니 난간, 층 사이 띠
                    Fill(px, W, 0, 0, W, H, C("#ECE8E0"), rnd, 4);
                    Glass(px, W, 10, 26, 118, 104, C("#5D7186"));
                    Fill(px, W, 62, 26, 66, 104, C("#E2DED6"));
                    Fill(px, W, 6, 22, 122, 30, C("#F7F6F2"));
                    Fill(px, W, 6, 46, 122, 49, C("#D9D6CF"));
                    Fill(px, W, 0, 0, W, 8, C("#D5D0C6"));
                    break;
                case "office":
                    // 사무실: 유리벽 + 세로 멀리언, 층 사이 짙은 띠
                    Glass(px, W, 0, 0, W, H, C("#4E6982"));
                    Fill(px, W, 0, 0, W, 22, C("#2F3A46"));
                    Fill(px, W, 0, 0, 5, H, C("#39434F"));
                    Fill(px, W, W - 5, 0, W, H, C("#39434F"));
                    break;
                case "house":
                    // 주택: 붉은 벽돌 + 작은 창
                    Fill(px, W, 0, 0, W, H, C("#9C5A3E"), rnd, 10);
                    for (int y = 0; y < H; y += 10) Fill(px, W, 0, y, W, y + 2, C("#7E4630"));
                    Fill(px, W, 40, 40, 88, 92, C("#E8E2D8"));
                    Glass(px, W, 44, 44, 84, 88, C("#4C5D6E"));
                    Fill(px, W, 62, 44, 66, 88, C("#E8E2D8"));
                    break;
                case "low":
                    // 창고·공장: 금속 벽(세로 골), 위쪽 작은 창
                    Fill(px, W, 0, 0, W, H, C("#B5BABF"), rnd, 3);
                    for (int x = 0; x < W; x += 8) Fill(px, W, x, 0, x + 2, H, C("#9DA3A9"));
                    Glass(px, W, 16, 96, 112, 112, C("#56626E"));
                    break;
                case "public":
                    // 학교·관공서: 연한 벽 + 가로로 긴 창
                    Fill(px, W, 0, 0, W, H, C("#E6DDCC"), rnd, 4);
                    Fill(px, W, 6, 36, 122, 96, C("#C9C1B2"));
                    Glass(px, W, 10, 40, 118, 92, C("#55687A"));
                    Fill(px, W, 62, 40, 66, 92, C("#C9C1B2"));
                    break;
                default:
                    // 빌라·상가 위층: 베이지 벽 + 창, 창틀
                    Fill(px, W, 0, 0, W, H, C("#D8D1C4"), rnd, 5);
                    Fill(px, W, 24, 34, 104, 98, C("#9A968E"));
                    Glass(px, W, 28, 38, 100, 94, C("#4F5E6D"));
                    Fill(px, W, 62, 38, 66, 94, C("#9A968E"));
                    Fill(px, W, 0, 0, W, 6, C("#C4BDB0"));
                    break;
            }
            t.SetPixels32(px);
            t.Apply(true, true);   // (11/13) 그린 뒤 다시 읽지 않는다 → 일반 메모리의 사본을 내린다
            cache["f" + kind] = t;
            return t;
        }

        /// <summary>상가 1층(4 m × 4 m 한 칸): 유리문·진열창 + 위쪽 간판 띠(색 n, 글자 모양 흰 덩어리)</summary>
        public static Texture2D Shopfront(int n)
        {
            if (cache.TryGetValue("s" + n, out var t)) return t;
            const int W = 128, H = 128;
            t = New("shop-" + n, W, H);
            var px = new Color32[W * H];
            var rnd = new System.Random(100 + n);
            string[] signs = { "#C8372D", "#1E62C8", "#1E7E3E", "#E08A00", "#5A3E8E" };
            Fill(px, W, 0, 0, W, H, C("#3B3F45"));
            Glass(px, W, 6, 6, W - 6, 88, C("#3E4E5C"));
            Fill(px, W, 60, 6, 68, 88, C("#2C3036"));
            Fill(px, W, 0, 88, W, 92, C("#2C3036"));
            var sign = C(signs[n % signs.Length]);
            Fill(px, W, 0, 92, W, H, sign);
            // 간판 글자 모양(실제 글자 아님): 흰 네모 덩어리 몇 개
            int x = 14 + rnd.Next(10);
            for (int k = 0; k < 4 && x < W - 20; k++)
            {
                int cw = 14 + rnd.Next(6);
                Fill(px, W, x, 100, x + cw, 104, C("#FFFFFF"));
                Fill(px, W, x + cw / 2 - 2, 100, x + cw / 2 + 2, 120, C("#FFFFFF"));
                Fill(px, W, x, 112, x + cw, 116, C("#FFFFFF"));
                x += cw + 8;
            }
            t.SetPixels32(px);
            t.Apply(true, true);   // (11/13) 그린 뒤 다시 읽지 않는다 → 일반 메모리의 사본을 내린다
            cache["s" + n] = t;
            return t;
        }

        /// <summary>(11/11 낮) 연석: 밝은 화강석(잔 얼룩) 1 m × 1 m, 왼쪽 끝에 어두운 이음매 한 줄 — 가로(u)로 1 m 마다 되풀이된다</summary>
        public static Texture2D Curb()
        {
            if (cache.TryGetValue("curb", out var t)) return t;
            const int W = 256, H = 256;
            t = New("curb", W, H);
            var px = new Color32[W * H];
            var rnd = new System.Random(11);
            Fill(px, W, 0, 0, W, H, C("#C2C1BB"), rnd, 13);
            for (int i = 0; i < 900; i++)          // 굵은 알갱이(어두운 점·밝은 점)
            {
                int x = rnd.Next(W), y = rnd.Next(H); bool dark = rnd.Next(3) > 0;
                var c = dark ? C("#8F8E89") : C("#DAD9D4");
                px[y * W + x] = c; px[y * W + (x + 1) % W] = c;
            }
            Fill(px, W, 0, 0, 3, H, C("#77766F"));      // 이음매
            Fill(px, W, 3, 0, 5, H, C("#A9A8A2"));
            t.SetPixels32(px);
            t.Apply(true, true);   // (11/13) 그린 뒤 다시 읽지 않는다 → 일반 메모리의 사본을 내린다
            cache["curb"] = t;
            return t;
        }

        /// <summary>(11/11 낮) 어린이보호구역 빨간 미끄럼 방지 포장: 짙은 벽돌빛 바탕에 굵은 알갱이(단색 페인트처럼 번들거리지 않게)</summary>
        public static Texture2D RedPave()
        {
            if (cache.TryGetValue("redpave", out var t)) return t;
            const int W = 128, H = 128;
            t = New("redpave", W, H);
            var px = new Color32[W * H];
            var rnd = new System.Random(5);
            for (int i = 0; i < px.Length; i++)
            {
                int dn = rnd.Next(-20, 21);
                px[i] = new Color32((byte)Mathf.Clamp(134 + dn, 0, 255), (byte)Mathf.Clamp(44 + dn / 2, 0, 255), (byte)Mathf.Clamp(38 + dn / 2, 0, 255), 255);
            }
            t.SetPixels32(px);
            t.Apply(true, true);   // (11/13) 그린 뒤 다시 읽지 않는다 → 일반 메모리의 사본을 내린다
            cache["redpave"] = t;
            return t;
        }

        public static Texture2D Roof()
        {
            if (cache.TryGetValue("roof", out var t)) return t;
            const int W = 64, H = 64;
            t = New("roof", W, H);
            var px = new Color32[W * H];
            Fill(px, W, 0, 0, W, H, C("#9C9C98"), new System.Random(3), 8);
            t.SetPixels32(px);
            t.Apply(true, true);   // (11/13) 그린 뒤 다시 읽지 않는다 → 일반 메모리의 사본을 내린다
            cache["roof"] = t;
            return t;
        }
    }
}
