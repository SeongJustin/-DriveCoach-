// 3D 주차 연습 — 앱이 보내는 평평한 JSON(제원·진행 기록)을 읽고 쓰는 작은 도구. UnityEngine 없음
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DriveCoach.Core
{
    /// <summary>
    /// 아주 작은 JSON 읽기·쓰기. 객체·배열·문자열·숫자·true/false/null 을 읽는다(중첩 가능).
    /// 객체 = Dictionary&lt;string, object&gt;, 배열 = List&lt;object&gt;, 숫자 = double.
    /// </summary>
    public static class MiniJson
    {
        public static Dictionary<string, object> ParseObject(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                int i = 0;
                var v = ParseValue(json, ref i);
                return v as Dictionary<string, object>;
            }
            catch (Exception) { return null; }
        }

        public static float Num(Dictionary<string, object> o, string key, float def)
            => o != null && o.TryGetValue(key, out var v) && v is double d ? (float)d : def;

        public static string Str(Dictionary<string, object> o, string key, string def)
            => o != null && o.TryGetValue(key, out var v) && v is string s ? s : def;

        static void SkipWs(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("end");
            char c = s[i];
            if (c == '{')
            {
                var o = new Dictionary<string, object>();
                i++;
                SkipWs(s, ref i);
                if (s[i] == '}') { i++; return o; }
                while (true)
                {
                    SkipWs(s, ref i);
                    string k = ParseString(s, ref i);
                    SkipWs(s, ref i);
                    if (s[i] != ':') throw new FormatException(":");
                    i++;
                    o[k] = ParseValue(s, ref i);
                    SkipWs(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return o; }
                    throw new FormatException("obj");
                }
            }
            if (c == '[')
            {
                var a = new List<object>();
                i++;
                SkipWs(s, ref i);
                if (s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(ParseValue(s, ref i));
                    SkipWs(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return a; }
                    throw new FormatException("arr");
                }
            }
            if (c == '"') return ParseString(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("str");
            i++;
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c == '\\')
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            i++;
            return sb.ToString();
        }

        /// <summary>문자열 → JSON 문자열(따옴표 포함)</summary>
        public static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
