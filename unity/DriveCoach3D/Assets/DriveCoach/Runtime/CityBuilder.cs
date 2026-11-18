// (3D 내 동네 도로 연수: 지역 지도(OSM)로 동네를 세운다.)
//  - 노면: 길 띠(아스팔트) + 보도(밝은 포장) + 교차로 원, 중앙선(큰길 겹노랑·동네길 노랑 한 줄)·차로 점선(흰)·정지선·횡단보도
//  - 건물: 윤곽을 층수만큼 세우고, 층 높이(3.2 m)·창 간격에 맞춘 외벽 무늬(아파트·사무실 유리·상가·주택 벽돌·창고), 상가 1층은 유리 + 간판 띠
//    (간판은 색 띠와 글자 모양만, 실제 상호는 쓰지 않는다), 지붕은 오목한 모양도 맞게(귀 자르기)
//  - 거리: 가로수(큰길 보도), 가로등(Quaternius), 신호등(Kenney)
// 같은 재질은 한 메시로 모아 그린다(차량 화면에서도 가볍게).
// Poly Haven(CC0) 사진 질감. 길(아스팔트 두 종)·보도블록·연석(높이 있는 띠)·잔디, 외벽은 사진 벽 + 창을
// 미리 그린 4칸 × 4층 한 장, 옥상 난간·계단실·물탱크, 옥상은 초록 방수 페인트(한국 동네)·회색.
//  건물은 160 m 칸으로 나눠 메시를 만들어 화면 밖·먼 칸은 그리지 않는다. 사진 질감이 없으면 예전 코드 무늬로.
using System;
using System.Collections.Generic;
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    public static partial class CityBuilder
    {
        const float SidewalkW = 2.2f;
        const float FloorH = 3.2f;
        static readonly string[] Kinds = { "apt", "office", "shop", "house", "public", "low", "other" };

        /// <summary>지금 지역의 실제 노면(없으면 null). 신호등 기둥 등을 아스팔트 밖으로 옮길 때 쓴다.</summary>
        public static RoadSurface Current;

        /// <summary>동네를 내릴 때 지을 때만 쓰던 자료(노면·건물 격자)를 잊는다. 다음 동네를 짓는 동안 옛 동네 자료가 남지 않게</summary>
        public static void Forget()
        {
            Current = null;
            bgrid = null;
            bgridOf = null;
        }

        /// <summary>
        /// 실제로 세운 가로수·가로등·보도 시설물의 자리(반폭 m). 장면을 지은 뒤 SceneRig 이 충돌 물체로 넣는다.
        /// </summary>
        public static readonly List<(V2 p, float half, string kind)> Solids = new List<(V2, float, string)>();

        public static void Build(RoadDef r, float h, Transform parent)
        {
            Solids.Clear();
            var node = new GameObject("City").transform;
            node.SetParent(parent, false);
            MeshKit.Built = new List<Mesh>();
            try { BuildAll(r, h, node); }
            finally
            {
                // 동네 메시는 지은 뒤 다시 읽지 않는다 → 그래픽 장치에만 두고 일반 메모리의 사본은 내린다(화면은 그대로, 메모리 절반)
                var built = MeshKit.Built;
                MeshKit.Built = null;
                long vtx = 0; int late = 0;
                // 대부분은 ToObject 에서 이미 내렸다. 남은 것(밤 창 사본을 뜬 외벽 등)만 여기서
                foreach (var m in built) if (m != null) { vtx += m.vertexCount; if (m.isReadable) { m.UploadMeshData(true); late++; } }
                Debug.Log("DC3D city meshes " + built.Count + ", vertices " + vtx + " (사본 내림, 끝에 내린 것 " + late + ")");
            }
        }

        static void BuildAll(RoadDef r, float h, Transform node)
        {
            var surf = r.region != null ? RoadSurface.Load(r.region.surfaceFile) : null;
            Current = surf;
            var junctionR = JunctionRadii(r);
            var inJunction = JunctionTest(junctionR);
            Dc.Lap("city-surface-load");
            // 단계 사이마다 다 쓴 임시 목록을 치운다: 스크립트 힙(Boehm)은 한 번 커지면 잘 줄지 않으므로 최고치를 낮춘다
            BuildAreas(r, h, node); Dc.Trim(); Dc.Lap("city-areas");
            if (surf != null) { BuildRealSurface(surf, h, node, r.region.crosswalks); Dc.Trim(); Dc.Lap("city-realsurface"); }
            else
            {
                BuildSurfaces(r, h, node, junctionR, inJunction);
                BuildMarkings(r, h, node, junctionR, inJunction);
            }
            BuildBuildings(r, h, node); Dc.Trim(); Dc.Lap("city-buildings");
            BuildStreet(r, h, node); Dc.Trim(); Dc.Lap("city-street");
        }
    }
}
