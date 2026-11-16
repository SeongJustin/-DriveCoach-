// (3D 주차 연습 — 첫 화면: 탭 3개(핸들 감각 · 주차 · 도로 주행)와 설정.
// 핸들 감각 = 튜토리얼 1(핸들 감각) + 서킷 2개, 주차 = 튜토리얼 2·3 + 25단계, 도로 주행 = 미리보기(설정에서 켜야 보인다).
// 새 모드는 Tabs 에 한 줄, Build 에 한 갈래를 더하면 된다.)
using System;
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DriveCoach.Unity
{
    public partial class MenuScreen
    {
        public readonly Canvas canvas;
        readonly RectTransform root, content, tabsRow;
        readonly GameObject settingsGo;
        readonly RectTransform settingsCard;
        public Action<string> onPick;          // "tut:1", "circuit:2", "stage:7", "road:1", "close"
        public Action onSettingsChanged;
        /// <summary>주행 화면에서 연 설정을 닫을 때(주행을 이어 간다)</summary>
        public Action onSettingsClosed;
        bool inGame;                                             // 주행 화면 위에 설정만 띄운 중
        readonly List<GameObject> pageParts = new List<GameObject>();   // 설정만 띄울 때 숨길 메뉴 화면 조각
        int tab;

        static readonly string[] TabNames = { "스티어링 휠 감각", "주차" };
        // 도로 주행은 주차·핸들 연습에서 빼 앱 첫 화면에서 따로 연다. 맵은 나라 › 도시 › 구 › 동 순서로 고른다.
        bool roadMode;
        readonly Text titleText;
        readonly List<string> roadPath = new List<string>();
        int pickedRegion = -1;                                   // 시작 위치를 고르는 동네(roadPath 5번째 단계)     // 고른 나라·도시·구
        // 권역 › 나라. 완성차 제조사 공식 누리집의 "Global Distributors"(판매망이 있는 나라·지역) 목록을 따랐다.
        // 지금 달릴 수 있는 곳은 한국뿐이고 나머지는 "준비 중"(나라별 교통법을 반영해 추가할 예정).
        static readonly (string area, string[] countries)[] World =
        {
            ("아시아 태평양", new[] { "한국", "일본", "중국", "홍콩", "마카오", "몽골", "인도", "인도네시아", "말레이시아", "싱가포르", "태국", "베트남", "필리핀", "캄보디아", "라오스", "미얀마", "브루나이", "동티모르", "방글라데시", "부탄", "네팔", "스리랑카", "호주", "뉴질랜드", "피지", "괌", "사이판", "미국령 사모아", "사모아", "통가", "바누아투", "솔로몬 제도", "마셜 제도", "미크로네시아", "뉴칼레도니아", "프랑스령 폴리네시아" }),
            ("유럽", new[] { "독일", "프랑스", "영국", "이탈리아", "스페인", "포르투갈", "네덜란드", "벨기에", "룩셈부르크", "스위스", "오스트리아", "아일랜드", "덴마크", "스웨덴", "노르웨이", "핀란드", "아이슬란드", "폴란드", "체코", "슬로바키아", "헝가리", "루마니아", "불가리아", "그리스", "키프로스", "몰타", "크로아티아", "슬로베니아", "세르비아", "보스니아 헤르체고비나", "북마케도니아", "알바니아", "에스토니아", "라트비아", "리투아니아", "우크라이나", "몰도바", "벨라루스", "러시아", "튀르키예", "조지아", "아르메니아", "아제르바이잔", "카자흐스탄", "우즈베키스탄", "키르기스스탄", "타지키스탄", "투르크메니스탄" }),
            ("북미", new[] { "미국", "캐나다", "멕시코" }),
            ("중남미", new[] { "브라질", "아르헨티나", "칠레", "콜롬비아", "페루", "에콰도르", "볼리비아", "파라과이", "우루과이", "베네수엘라", "가이아나", "수리남", "프랑스령 기아나", "파나마", "코스타리카", "니카라과", "온두라스", "엘살바도르", "과테말라", "쿠바", "도미니카 공화국", "아이티", "자메이카", "푸에르토리코", "바하마", "바베이도스", "버뮤다", "케이맨 제도", "아루바", "퀴라소", "트리니다드 토바고", "그레나다", "세인트루시아", "세인트빈센트", "세인트키츠", "세인트마틴", "앤티가", "과들루프", "마르티니크" }),
            ("중동", new[] { "사우디아라비아", "아랍에미리트", "카타르", "쿠웨이트", "바레인", "오만", "요르단", "레바논", "이스라엘", "팔레스타인", "이라크", "예멘", "아프가니스탄", "파키스탄" }),
            ("아프리카", new[] { "남아프리카 공화국", "이집트", "모로코", "알제리", "튀니지", "리비아", "나이지리아", "가나", "케냐", "에티오피아", "앙골라", "모잠비크", "잠비아", "짐바브웨", "마다가스카르", "모리셔스", "세이셸", "레위니옹", "르완다", "수단", "지부티", "세네갈", "감비아", "기니", "기니비사우", "시에라리온", "라이베리아", "코트디부아르", "토고", "베냉", "부르키나파소", "말리", "니제르", "차드", "모리타니", "카메룬", "중앙아프리카 공화국", "콩고", "콩고 민주 공화국", "카나리아 제도", "세우타" }),
        };
        static string AreaOf(string country)
        {
            foreach (var w in World) if (Array.IndexOf(w.countries, country) >= 0) return w.area;
            return World[0].area;
        }

        /// <summary>도로 주행 화면을 주어진 단계로(사진·검수용): RoadGo("아시아·태평양", "한국", "서울", "서초구")</summary>
        public void RoadGo(params string[] path)
        {
            roadMode = true;
            roadPath.Clear();
            roadPath.AddRange(path);
            pickedRegion = -1;
            if (path.Length >= 5)
                for (int i = 0; i < RegionCatalog.Entries.Count; i++)
                    if (RegionCatalog.Entries[i].name == path[4] || RegionCatalog.Entries[i].name + " Beta" == path[4]) pickedRegion = i;
            Refresh();
        }

        public void SetMode(bool road)
        {
            if (roadMode == road) return;
            roadMode = road;
            Refresh();
        }

        public MenuScreen(Transform parent, Camera screenCam)
        {
            canvas = UiKit.ScreenCanvas("Menu", parent, screenCam, 20);
            root = (RectTransform)canvas.transform;
            var bg = UiKit.Blank(root, "bg", DcAssets.Bg);
            UiKit.Stretch(bg.rectTransform);
            var glow = UiKit.Blank(root, "glow", Color.white);       // 오른쪽 아래에서 번지는 라임 빛
            glow.sprite = DcAssets.Glow();
            glow.raycastTarget = false;
            UiKit.Stretch(glow.rectTransform);
            var title = titleText = UiKit.Label(root, "주차 연습", 44, DcAssets.UiText, true, TextAnchor.MiddleLeft);
            UiKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(48, -32), new Vector2(800, 80));
            var close = UiKit.Btn(root, "닫기", 30, UiKit.Kind.Plain, () => onPick?.Invoke("close"), out _);
            UiKit.Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(-48, -28), new Vector2(150, 80));
            var set = UiKit.Btn(root, "설정", 30, UiKit.Kind.Plain, OpenSettings, out _);
            UiKit.Place((RectTransform)set.transform, new Vector2(1, 1), new Vector2(-210, -28), new Vector2(150, 80));
            tabsRow = UiKit.Node("tabs", root);
            UiKit.Place(tabsRow, new Vector2(0, 1), new Vector2(48, -136), new Vector2(900, 72));
            content = UiKit.Node("content", root);
            UiKit.Stretch(content, 48, 236, 48, 40);

            settingsGo = UiKit.Blank(root, "settings", new Color(0f, 0f, 0f, 0.35f)).gameObject;
            UiKit.Stretch((RectTransform)settingsGo.transform);
            var sc = UiKit.Card(settingsGo.transform, "card", DcAssets.Card, DcAssets.UiLine);
            UiKit.Place(sc.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1040, 990));
            settingsCard = sc.rectTransform;
            settingsGo.SetActive(false);
            tab = Mathf.Clamp(Prefs.MenuTab, 0, TabNames.Length - 1);
            foreach (var g in new GameObject[] { bg.gameObject, glow.gameObject, title.gameObject, close.gameObject, set.gameObject, tabsRow.gameObject, content.gameObject }) pageParts.Add(g);
            settingsGo.transform.SetAsLastSibling();
            Refresh();
        }

        /// <summary>주행 화면 위에 설정 창만 띄운다(메뉴 화면은 숨김). 닫으면 onSettingsClosed</summary>
        public void OpenSettingsInGame()
        {
            inGame = true;
            foreach (var g in pageParts) g.SetActive(false);
            if (!canvas.gameObject.activeSelf) canvas.gameObject.SetActive(true);
            OpenSettings();
        }

        public void SetVisible(bool on)
        {
            if (on) Refresh();
            if (canvas.gameObject.activeSelf != on) canvas.gameObject.SetActive(on);
        }

        public bool SettingsOpen => settingsGo.activeSelf;
        public void CloseSettings()
        {
            settingsGo.SetActive(false);
            if (inGame)
            {
                inGame = false;
                foreach (var g in pageParts) g.SetActive(true);
                canvas.gameObject.SetActive(false);
                onSettingsClosed?.Invoke();
                return;
            }
            Refresh();
        }

        int TabCount => 2;

        public void ShowTab(int i)
        {
            tab = Mathf.Clamp(i, 0, TabCount - 1);
            Prefs.MenuTab = tab;
            Refresh();
        }

        /// <summary>탭·내용을 다시 만든다(별점이 바뀌었거나 설정이 바뀌었을 때)</summary>
        public void Refresh()
        {
            if (tab >= TabCount) tab = 0;
            titleText.text = roadMode ? "도로 주행 연습" : "주차 연습";   // 첫 화면 메뉴 이름과 같게
            Clear(tabsRow);
            if (roadMode)
            {
                content.offsetMax = new Vector2(content.offsetMax.x, -236f);
                Crumbs();
                Clear(content);
                BuildRoad();
                return;
            }
            // 스티어링 휠 감각 탭을 뺐다. 주차 단계 25개만 보인다
            content.offsetMax = new Vector2(content.offsetMax.x, -150f);
            Clear(content);
            BuildParking();
        }

        static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Dc.Kill(t.GetChild(i).gameObject);
        }

        /// <summary>큰 카드: 왼쪽 파란 띠, 이름, 설명, 오른쪽 위 별점/완료</summary>
        RectTransform BigCard(Transform parent, string id, string name, string desc, string badge)
        {
            var c = UiKit.Card(parent, "card " + id, DcAssets.Card, DcAssets.UiLine);
            var btn = c.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            bool soon = id.StartsWith("soon:");      // 준비 중 카드: 눌러도 아무 일 없음, 회색 띠
            if (id.StartsWith("path:")) btn.onClick.AddListener(() => { roadPath.Add(id.Substring(5)); Refresh(); });
            else if (id.StartsWith("dong:")) btn.onClick.AddListener(() => { if (int.TryParse(id.Substring(5), out var ri)) { pickedRegion = ri; roadPath.Add(name); Refresh(); } });
            else if (!soon && !id.StartsWith("dong:")) btn.onClick.AddListener(() => onPick?.Invoke(id));
            // 이름과 (준비 중)만 검정으로
            var n = UiKit.Label(c.transform, name, 34, DcAssets.UiText, true, TextAnchor.UpperLeft);
            UiKit.Stretch(n.rectTransform, 30, 28, 28, 0);
            if (!string.IsNullOrEmpty(desc))
            {
                var d = UiKit.Label(c.transform, desc, 26, DcAssets.UiText, false, TextAnchor.UpperLeft);
                UiKit.Stretch(d.rectTransform, 30, 84, 28, 16);
            }
            return c.rectTransform;
        }

        const string Soon = "(준비 중)";

        void BuildSteering()
        {
            var items = new List<(string id, string name, string desc, string badge)>
            {
                ("tut:1", Stages.TutorialNames[0], "", ""),
            };
            for (int i = 1; i <= Stages.CIRCUIT_COUNT; i++)
                items.Add(("circuit:" + i, Stages.CircuitNames[i - 1], "", ""));
            Row(items, 0.42f);
        }

        /// <summary>도로 주행 위쪽 줄: 지금까지 고른 길("전체 › 한국 › 서울 › 서초구"). 누르면 그 단계로 돌아간다.</summary>
        void Crumbs()
        {
            float x = 0f;
            for (int i = 0; i <= roadPath.Count; i++)
            {
                int depth = i;
                string label = i == 0 ? "권역" : roadPath[i - 1];
                bool last = i == roadPath.Count;
                var b = UiKit.Btn(tabsRow, label, 30, last ? UiKit.Kind.Primary : UiKit.Kind.Plain, () =>
                {
                    while (roadPath.Count > depth) roadPath.RemoveAt(roadPath.Count - 1);
                    Refresh();
                }, out var t);
                float w = Mathf.Max(150f, t.preferredWidth + 64f);
                UiKit.Place((RectTransform)b.transform, new Vector2(0, 1), new Vector2(x, 0), new Vector2(w, 72));
                x += w + 24f;
            }
        }

        // 나라 › 도시 › 구 › 동 의 전체 이름. 앱에 든 동네가 있는 곳만 누를 수 있다.
        //   시·도 17곳, 서울 25개 구, 서초구 18개 동·강남구 22개 동(행정동, 구청 누리집 기준 — 강남구 일원2동은 개포3동으로 이름이 바뀌었다)
        static readonly string[] KoreaCities = { "서울", "부산", "대구", "인천", "광주", "대전", "울산", "세종", "경기", "강원", "충북", "충남", "전북", "전남", "경북", "경남", "제주" };
        static readonly string[] SeoulGu = { "종로구", "중구", "용산구", "성동구", "광진구", "동대문구", "중랑구", "성북구", "강북구", "도봉구", "노원구", "은평구", "서대문구", "마포구",
            "양천구", "강서구", "구로구", "금천구", "영등포구", "동작구", "관악구", "서초구", "강남구", "송파구", "강동구" };
        static readonly Dictionary<string, string[]> GuDongs = new Dictionary<string, string[]>
        {
            { "서초구", new[] { "서초1동", "서초2동", "서초3동", "서초4동", "잠원동", "반포본동", "반포1동", "반포2동", "반포3동", "반포4동", "방배본동", "방배1동", "방배2동", "방배3동", "방배4동", "양재1동", "양재2동", "내곡동" } },
            { "강남구", new[] { "신사동", "논현1동", "논현2동", "압구정동", "청담동", "삼성1동", "삼성2동", "대치1동", "대치2동", "대치4동", "역삼1동", "역삼2동", "도곡1동", "도곡2동", "개포1동", "개포2동", "개포3동", "개포4동", "세곡동", "일원본동", "일원1동", "수서동" } },
        };

        void BuildRoad()
        {
            // 권역 › 나라 › 도시 › 구 › 동. 들어 있는 동네(index.json 의 country·city·district)에서 누를 수 있는 곳을 정한다.
            var regions = RegionCatalog.Entries;
            int depth = roadPath.Count;
            if (depth >= 5 && pickedRegion >= 0 && pickedRegion < regions.Count)
            {
                // 지도를 누르면 가장 가까운 길에 출발 자리를 찍는다
                BuildStartMap(pickedRegion);
                return;
            }
            string Key(RegionCatalog.Entry e, int d) => d == 0 ? AreaOf(e.country) : (d == 1 ? e.country : (d == 2 ? e.city : e.district));
            bool Under(RegionCatalog.Entry e)
            {
                for (int d = 0; d < depth; d++) if (Key(e, d) != roadPath[d]) return false;
                return true;
            }
            var open = new HashSet<string>();
            if (depth < 4) foreach (var e in regions) if (Under(e)) open.Add(Key(e, depth));
            if (depth == 0)
            {
                // 권역 6곳: 3칸 × 2줄. 동네가 없는 권역은 이름 아래 (준비 중)
                for (int i = 0; i < World.Length; i++)
                {
                    var w = World[i];
                    bool ok = open.Contains(w.area);
                    var rt = BigCard(content, ok ? "path:" + w.area : "soon:" + w.area, w.area, ok ? "" : Soon, "");
                    int col = i % 3, row = i / 3;
                    rt.anchorMin = new Vector2(col / 3f, 1f - 0.3f * (row + 1));
                    rt.anchorMax = new Vector2((col + 1) / 3f, 1f - 0.3f * row);
                    rt.offsetMin = new Vector2(col == 0 ? 0f : 10f, 10f);
                    rt.offsetMax = new Vector2(col == 2 ? 0f : -10f, -10f);
                }
                return;
            }
            var cells = new List<(string label, Action click)>();
            if (depth == 1)
            {
                string[] cs = null;
                foreach (var w in World) if (w.area == roadPath[0]) cs = w.countries;
                foreach (var c in cs ?? new string[0]) { string k = c; cells.Add((k, open.Contains(k) ? (Action)(() => { roadPath.Add(k); Refresh(); }) : null)); }
                GridCells(cells, 8);
                return;
            }
            if (depth == 2 || depth == 3)
            {
                var names = new List<string>(depth == 2 ? (roadPath[1] == "한국" ? KoreaCities : new string[0]) : (roadPath[2] == "서울" ? SeoulGu : new string[0]));
                foreach (var k in open) if (!names.Contains(k)) names.Add(k);       // 목록에 없는 이름의 동네가 들어와도 보이게
                foreach (var nm in names) { string k = nm; cells.Add((k, open.Contains(k) ? (Action)(() => { roadPath.Add(k); Refresh(); }) : null)); }
                GridCells(cells, depth == 2 ? 6 : 7);
                return;
            }
            // 구 → 동: 그 구의 모든 행정동. 앱에 든 동만 누를 수 있다
            var dongs = new List<string>(GuDongs.TryGetValue(roadPath[3], out var gd) ? gd : new string[0]);
            var have = new Dictionary<string, int>();
            for (int i = 0; i < regions.Count; i++)
                if (Under(regions[i])) { have[regions[i].name] = i; if (!dongs.Contains(regions[i].name)) dongs.Add(regions[i].name); }
            foreach (var nm in dongs)
            {
                if (have.TryGetValue(nm, out int ri))
                {
                    string label = nm + (regions[ri].beta ? " Beta" : "");
                    cells.Add((label, () => { pickedRegion = ri; roadPath.Add(label); Refresh(); }));
                }
                else cells.Add((nm, null));
            }
            GridCells(cells, 6);
            // 출처: 목록(index.json)의 attribution·license 에서(한국 동네처럼 없으면 예전 글 그대로)
            var atts = new List<string>();
            foreach (var e in regions) if (Under(e) && !string.IsNullOrEmpty(e.attribution) && !atts.Contains(e.attribution)) atts.Add(e.attribution);
            Hint(atts.Count > 0 ? "지도 " + string.Join(" · ", atts) : "지도 © OpenStreetMap contributors");
        }

        /// <summary>이름 칸 묶음: 누를 수 있는 곳은 이름만, 없는 곳은 이름 아래 검정 (준비 중). 모두 흰 칸</summary>
        void GridCells(List<(string label, Action click)> cells, int cols)
        {
            int rows = Mathf.Max(4, (cells.Count + cols - 1) / cols);
            var grid = UiKit.Node("cells", content);
            UiKit.Frac(grid, 0f, 0.08f, 1f, 1f);
            for (int i = 0; i < cells.Count; i++)
            {
                var (label, click) = cells[i];
                var card = UiKit.Card(grid, "cell " + label, DcAssets.Card, DcAssets.UiLine);
                if (click != null)
                {
                    var btn = card.gameObject.AddComponent<Button>();
                    btn.transition = Selectable.Transition.None;
                    btn.onClick.AddListener(() => click());
                }
                int col = i % cols, row = i / cols;
                var rt = card.rectTransform;
                rt.anchorMin = new Vector2((float)col / cols, 1f - (float)(row + 1) / rows);
                rt.anchorMax = new Vector2((float)(col + 1) / cols, 1f - (float)row / rows);
                rt.offsetMin = new Vector2(col == 0 ? 0f : 6f, 6f);
                rt.offsetMax = new Vector2(col == cols - 1 ? 0f : -6f, -6f);
                // 띄어쓰기 없는 긴 이름("샤를로텐부르크-빌머스도르프")은 붙임표 뒤에서 줄을 바꿔 보인다(한국 이름은 12자를 넘지 않아 그대로)
                string shown = label.Length > 12 && label.IndexOf(' ') < 0 && label.IndexOf('-') > 0 ? label.Substring(0, label.IndexOf('-') + 1) + "\n" + label.Substring(label.IndexOf('-') + 1) : label;
                var lab = UiKit.Label(card.transform, click != null ? shown : shown + "\n" + Soon, label.Length > 10 ? 22 : 26, DcAssets.UiText, click != null, TextAnchor.MiddleCenter);   // 10자 넘는 이름은 한 단계 작게
                UiKit.Stretch(lab.rectTransform, 8, 4, 8, 4);
            }
        }
    }
}
