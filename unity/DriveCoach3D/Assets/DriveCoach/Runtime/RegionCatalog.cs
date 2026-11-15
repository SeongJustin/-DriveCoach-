// 담당: S (3D 내 동네 도로 연수(베타) — 들어 있는 동네 목록(Resources/DC/Regions/index.json)과 읽기.
// 동네 파일은 가 OSM 전국 파일에서 만든다. 나중에 서버에서 받는 동네도 같은 모양(파일 경로만 다름).
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public static class RegionCatalog
    {
        public class Entry
        {
            public string file, name, desc, country = "한국", city = "서울", district = "";
            /// <summary>(11/13 대표) 아직 다듬는 중인 동네: 목록에서 이름 옆에 Beta 를 붙인다</summary>
            public bool beta;
            /// <summary>(11/11 대표) 그 지역에 맞는 차량 통행량·보행자 수 배율(1 = 보통 주거지). index.json 의 traffic·peds</summary>
            public float trafficK = 1f, pedK = 1f;
            /// <summary>지도 크기(MB, 동네 파일 + 노면 파일). 맵 고르기 화면의 "받음 · n MB"</summary>
            public float mb;
            /// <summary>앱에 든 지도가 아니라 기기 저장소(받은 지도 폴더)에서 읽는 동네</summary>
            public bool external;
            /// <summary>아직 앱에 없는 이웃 동네 이름(index.json 의 more) — 맵 고르기 화면의 "동네 더 받기". 동네 파일을 열지 않고도 알 수 있게 목록에 둔다</summary>
            public List<string> more = new List<string>();
        }

        /// <summary>
        /// (11/11 대표: 맵 다운로드) 받은 지도가 놓이는 폴더. 앱에 든 지도와 같은 모양(index.json + 동네.json + 동네_surface.bytes).
        /// 지도 서버는 아직 없다 — 이 폴더에 파일이 있으면 앱을 새로 만들지 않아도 목록에 "받음"으로 나온다.
        /// </summary>
        public static string ExtDir => System.IO.Path.Combine(Application.persistentDataPath, "regions");

        static bool IsExternal(string file)
        {
            if (entries == null) return false;
            foreach (var e in entries) if (e.file == file) return e.external;
            return false;
        }

        /// <summary>동네 파일 글(받은 지도면 저장소에서, 아니면 앱 안에서)</summary>
        public static string ReadText(string file)
        {
            try
            {
                if (IsExternal(file))
                {
                    var f = System.IO.Path.Combine(ExtDir, file + ".json");
                    return System.IO.File.Exists(f) ? System.IO.File.ReadAllText(f) : null;
                }
            }
            catch (System.Exception ex) { Debug.LogWarning("Region " + file + ": " + ex.Message); return null; }
            var ta = Resources.Load<TextAsset>("DC/Regions/" + file);
            return ta != null ? ta.text : null;
        }

        /// <summary>노면 파일(이름은 동네 파일의 surface_file). 받은 지도 폴더에 있으면 그것을 먼저</summary>
        public static byte[] ReadBytes(string file)
        {
            try
            {
                var f = System.IO.Path.Combine(ExtDir, file + ".bytes");
                if (System.IO.File.Exists(f)) return System.IO.File.ReadAllBytes(f);
            }
            catch (System.Exception ex) { Debug.LogWarning("Surface " + file + ": " + ex.Message); }
            var ta = Resources.Load<TextAsset>("DC/Regions/" + file);
            return ta != null ? ta.bytes : null;
        }

        static void AddFrom(string json, bool external)
        {
            // index.json 은 배열: {"list": [...]} 로 감싸 MiniJson 으로 읽는다
            var o = MiniJson.ParseObject("{\"list\":" + json + "}");
            if (o == null || !o.TryGetValue("list", out var l) || !(l is List<object> arr)) return;
            foreach (var it in arr)
                if (it is Dictionary<string, object> e)
                {
                    var en = new Entry { file = MiniJson.Str(e, "file", ""), name = MiniJson.Str(e, "name", ""), desc = MiniJson.Str(e, "desc", "") };
                    if (en.file == "" || entries.Exists(x => x.file == en.file)) continue;      // 앱에 든 동네와 같은 이름은 앱 것을 쓴다
                    en.country = MiniJson.Str(e, "country", "한국");
                    en.city = MiniJson.Str(e, "city", "서울");
                    en.district = MiniJson.Str(e, "district", "");
                    en.trafficK = MiniJson.Num(e, "traffic", 1f);
                    en.pedK = MiniJson.Num(e, "peds", 1f);
                    en.mb = MiniJson.Num(e, "mb", 0f);
                    en.beta = MiniJson.Num(e, "beta", 0f) > 0f;
                    en.external = external;
                    if (e.TryGetValue("more", out var mo) && mo is List<object> ml)
                        foreach (var x in ml) if (x is string ms && ms != "") en.more.Add(ms);
                    // 구가 비어 있으면 설명 앞머리("서울 서초구 · …")에서 읽는다
                    if (en.district == "" && en.desc.Contains(" · "))
                    {
                        var head = en.desc.Substring(0, en.desc.IndexOf(" · ")).Split(' ');
                        if (head.Length >= 2) en.district = head[head.Length - 1];
                    }
                    entries.Add(en);
                }
        }

        static List<Entry> entries;
        static readonly Dictionary<string, RegionData> cache = new Dictionary<string, RegionData>();

        public static List<Entry> Entries
        {
            get
            {
                if (entries != null) return entries;
                entries = new List<Entry>();
                var ta = Resources.Load<TextAsset>("DC/Regions/index");
                if (ta != null) AddFrom(ta.text, false);
                try
                {
                    var f = System.IO.Path.Combine(ExtDir, "index.json");
                    if (System.IO.File.Exists(f)) AddFrom(System.IO.File.ReadAllText(f), true);
                }
                catch (System.Exception ex) { Debug.LogWarning("Regions(ext): " + ex.Message); }
                return entries;
            }
        }

        // (11/11 대표: 동 사이 이동을 자연스럽게) 옆 동이 가까워지면 그 동의 지도 파일을 뒤에서 미리 읽어 둔다(읽기·해석만 — 화면은 그대로).
        //   실제로 넘어갈 때는 읽어 둔 것을 쓰므로 멈춰 있는 시간이 그만큼 준다. 파일 내용 꺼내기(Resources)는 주 스레드에서, 해석은 작업 스레드에서.
        static readonly Dictionary<string, System.Threading.Tasks.Task<RegionData>> pending = new Dictionary<string, System.Threading.Tasks.Task<RegionData>>();

        public static void Preload(int i)
        {
            var list = Entries;
            if (i < 0 || i >= list.Count) return;
            var f = list[i].file;
            lock (cache) { if (cache.ContainsKey(f) || pending.ContainsKey(f)) return; }
            var txt = ReadText(f);
            if (txt == null) return;
            var task = System.Threading.Tasks.Task.Run(() => RegionData.Parse(txt));
            lock (cache) pending[f] = task;
            RoadSurface.Preload(f + "_surface");       // 노면 파일 이름 규칙(make_surface.py): <동네>_surface
            Debug.Log("DC3D preload " + f);
        }

        /// <summary>(11/11) 지금 달리는 동네 것만 남기고 읽어 둔 동네 자료를 잊는다(동을 넘을 때마다 하나씩 쌓이지 않게). 미리 읽는 중인 것은 그대로</summary>
        public static void KeepOnly(string file)
        {
            lock (cache)
            {
                var drop = new List<string>();
                foreach (var k in cache.Keys) if (k != file) drop.Add(k);
                foreach (var k in drop) cache.Remove(k);
            }
            RoadSurface.KeepOnly(file + "_surface");
        }

        public static RegionData Load(int i)
        {
            var list = Entries;
            if (i < 0 || i >= list.Count) return null;
            var f = list[i].file;
            System.Threading.Tasks.Task<RegionData> t;
            lock (cache)
            {
                if (cache.TryGetValue(f, out var have)) return have;
                pending.TryGetValue(f, out t);
            }
            RegionData d = null;
            if (t != null)
            {
                try { d = t.Result; }                    // 아직 읽는 중이면 끝날 때까지 기다린다
                catch (System.Exception ex) { Debug.LogWarning("Region preload " + f + ": " + ex.Message); d = null; }
                lock (cache) pending.Remove(f);
            }
            if (d == null)
            {
                var txt = ReadText(f);
                if (txt == null) return null;
                d = RegionData.Parse(txt);
            }
            lock (cache) cache[f] = d;
            return d;
        }
    }
}
