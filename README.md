# 드라이브코치

차가 서 있을 때 차량 조작법, 주차, 도로 주행을 차 안 화면에서 학습하는 Pleos Connect(Android Automotive) 앱입니다.

## 1. 기능

학습 화면은 차가 멈춰 있을 때(0.5 km/h 이하) 기어가 P 이거나 주차 브레이크가 걸려 있으면 표시합니다. 차량이 화면 제한(CarUxRestrictions)을 요구하면 표시하지 않습니다. 주행 중에는 음성으로만 안내합니다.

| 카드 | 내용 |
|---|---|
| 차량 조작법 | 좌석, 안전벨트, 시동, 주차 브레이크, 변속, 등화, 와이퍼, 방향지시등, 창문, 공조, 클러스터, 운전자 보조 등 49개 항목을 음성과 그림(취급설명서 그림 46개 항목, 직접 그린 그림 3개 항목)으로 설명합니다 |
| 도로 법규 | 우회전, 비보호 좌회전, 유턴, 어린이 보호구역 등 12개 주제를 도로교통법 조문 발췌와 장면 그림으로 설명합니다. 화면 위 나라 줄에서 일본, 미국(뉴욕주, 캘리포니아주, 네바다주), 프랑스, 독일, 네덜란드, 싱가포르를 고르면 그 나라 법규 126개 주제(나라마다 15~17개)를 근거 조문과 함께 우리말로 보여 주고 읽어 줍니다 |
| 주차 연습 | 직각 주차와 평행 주차를 단계 영상으로 설명합니다. 주차 게임(3D) 25단계에서는 실제 스티어링 휠과 브레이크로 3D 차를 조작해 주차를 연습합니다 |
| 도로 주행 연습 | 실제 지도로 만든 3D 동네 23곳(서울 강남구 역삼1동, 역삼2동, 논현1동, 논현2동, 대치1동, 대치4동, 삼성2동, 도곡1동, 도곡2동, 개포4동, 신사동, 일원본동, 삼성1동, 압구정동, 서초구 양재1동, 양재2동, 서초2동, 반포본동, 방배4동, 방배본동, 방배1동, 잠원동, 독일 베를린 쿠담·아데나워 광장)을 신호등, 차로, 보행자, 다른 차가 있는 환경에서 주행합니다. 베를린에서는 노면 표시, 안전표지, 신호(초록 직전 빨강+노랑), 버스와 택시 색, 번호판을 독일 것으로 바꿉니다. 역삼1동, 역삼2동, 논현1동, 논현2동, 대치4동, 삼성2동, 도곡1동을 뺀 16곳은 아직 보완 중이어서 목록에 Beta 로 표시합니다. Beta 동네에는 다른 차와 횡단보도 보행자를 두지 않습니다. 서울 동네 골목의 일방통행 방향은 서울시 교통안전시설물 자료의 노면 표시에 맞췄습니다 |
| 코스 주행 | 연습 목적을 고르면 현재 위치와 진행 방향을 기준으로 경유지 2곳과 목적지(출발지에서 직선거리 약 1.5 km)의 좌표를 정해 차량 내비게이션에 경로를 요청합니다. 실제 도로 경로는 차량 내비게이션이 계산합니다. 주행 중에는 위험 상황만 음성으로 경고합니다. 도착 후 P 로 정차하면 주행 리포트를 표시합니다 |

음성 명령은 "다음", "다시", "확인", "이전", "건너뛰기"(또는 "스킵"), "조용히", "세울래요"이며, "한 번 더", "네", "그만", "세워" 같은 비슷한 표현도 인식합니다.

## 2. 실행

1. Android Studio 의 Device Manager 에서 Pleos Connect Emulator v2.1.0 을 실행합니다.
2. `apk/성민준과강명수_드라이브코치_release_x86_64.apk` 를 에뮬레이터 화면에 끌어다 놓거나 `adb install -r <파일>` 로 설치합니다. 실제 차량(arm64-v8a)용은 `apk/성민준과강명수_드라이브코치_release_arm64.apk` 입니다.
3. 앱 목록에서 드라이브코치를 실행하고 권한을 허용합니다.
4. 차량 신호는 Virtual Console(`http://127.0.0.1:8088`)에서 변경합니다. 기어를 P 로 두거나, 정차 상태에서 주차 브레이크를 걸면 학습 화면이 표시됩니다.
5. 음성 명령을 시험하려면 에뮬레이터의 Extended Controls > Microphone > Enable Host Microphone Access 를 활성화합니다.

## 3. 빌드

1. Android Studio(JDK 17, Android SDK API 34)에서 저장소 폴더를 열고 Gradle Sync 를 실행합니다. Pleos SDK 는 `settings.gradle.kts` 의 Playground Maven 저장소에서 내려받습니다.
2. `app` 을 Run 하거나 Build Variants 를 release 로 변경해 APK 를 빌드합니다.
3. 3D 화면(주차 게임, 도로 주행)은 `unity/export/unityLibrary-release.aar` 파일이 있을 때만 앱에 포함됩니다. 파일이 없으면 3D 화면 없이 빌드되고, 3D 메뉴를 누르면 3D 연습이 들어 있지 않다는 안내가 표시됩니다. 이 파일은 Unity 6000.3 으로 다음과 같이 만듭니다.
   - Unity 를 `-batchmode -quit -projectPath <저장소>/unity/DriveCoach3D -executeMethod DriveCoach.Setup.DcSetup.AddPackages` 로 실행해 패키지를 받습니다.
   - `-buildTarget Android` 를 더해 `DriveCoach.EditorTools.DcConfigure.Run`(프로젝트 설정), `DriveCoach.EditorTools.DcBuild.ExportAndroid`(Android Gradle 프로젝트 내보내기)를 차례로 실행합니다. 내보낼 폴더는 `-dcOut <폴더>` 로 정하며, 기본값은 `unity/export` 입니다.
   - 내보낸 프로젝트를 경로에 한글이 없는 폴더에서 `:unityLibrary:assembleRelease` 로 빌드하고, `unityLibrary/build/outputs/aar/unityLibrary-release.aar` 를 `unity/export/unityLibrary-release.aar` 로 복사합니다.

## 4. 환경

| 항목 | 값 |
|---|---|
| 앱 버전 | 1.1.1 |
| 검증 환경 | Pleos Connect Emulator v2.1.0(API 34), Virtual Console v2.1.0. 실차 검증은 수행하지 않았습니다 |
| 화면 규격 | 2560 x 1440(가로) |
| SDK | minSdk 28, targetSdk 34, AGP 8.8.2, Kotlin 2.0, Jetpack Compose |
| 기준 차종 | 현대 아반떼 CN8 |
| 권한 | Pleos Vehicle SDK 읽기 권한, NaviHelper, TTS, STT, FusedLocation, Android Automotive 표준 속성(`CAR_SPEED`, `CAR_POWERTRAIN`, `CAR_INFO`, `READ_CAR_PEDALS`), 인터넷, 포그라운드 서비스와 알림 |
| 네트워크 | 제출 APK(release)에는 길찾기 키가 없어 길찾기 API 를 호출하지 않습니다. `local.properties` 에 키를 넣고 debug 로 빌드하면 네이버 Directions 로 도로 경로를 조회해 코스를 만듭니다 |

## 5. Pleos Playground SDK

| SDK | 쓰는 곳 | 코드 |
|---|---|---|
| Vehicle 2.3.2 | 기어, 브레이크, 조향각, 등화, 방향지시등, 와이퍼, 창문, 도어, 좌석, 공조, 연료, 안전벨트 경고 | `app/.../vehicle/PleosSignalSource.kt` |
| NaviHelper 3.0.5 | 경로와 경유지 요청, 회전 정보, 현재 도로 제한 속도, 도착 상태 | `app/.../navi/PleosNaviAdapter.kt` |
| FusedLocation 1.1.3 | 현재 위치 | `app/.../vehicle/PleosSignalSource.kt` |
| TextToSpeech 3.3.1 | 음성 안내(ON_DEVICE) | `app/.../speech/PleosTtsSpeaker.kt` |
| SpeechToText 3.3.1 | 음성 명령(ON_DEVICE) | `app/.../speech/VoiceCommandListener.kt` |

속도, 주차 브레이크, 시동 상태, 가속 페달, 차종(제조사와 모델명)은 Android Automotive 표준 차량 속성으로 읽습니다.

## 6. 구조

```
apk/     release APK
core/    차량·Android 에 의존하지 않는 Kotlin 로직(신호 모델, 연습 항목, 주차 절차, 코스 생성, 위험 판정, 음성 문장)
app/     Pleos SDK 어댑터, Compose 화면, 포그라운드 서비스
unity/DriveCoach3D/   3D 주차 게임과 도로 주행(Unity 6). Assets/DriveCoach/Core 는 계산, Runtime 은 화면
```

## 7. 자료와 라이선스

- 기기 안에는 설정, 연습 진행 상태, 3D 게임 설정과 단계 기록, 3D 화면 실행 실패 기록을 저장합니다. 주행 기록과 리포트는 저장하지 않습니다. 앱 코드가 외부 서버로 보내는 요청은 키를 넣은 debug 빌드의 길찾기 호출(네이버 Directions)뿐입니다.
- 사용한 라이브러리와 자료: AndroidX, Jetpack Compose, Kotlin Coroutines(모두 Apache 2.0), Asta Sans 글꼴(OFL), Unity 6000.3(Unity 이용 약관). 가져온 3D 모델과 질감은 Poly Haven, Kenney, Quaternius(CC0), EZ-Tree(MIT)입니다. 라이선스 글은 `unity/DriveCoach3D/Assets/Resources/DC/` 아래 `CC0/`, `PolyHaven/`, `PolyHaven/Models/`, `Kenney/CarKit/`, `Kenney/CityRoads/`, `Quaternius/ModularStreet/`, `Trees/` 의 `License.txt` 와 `Fonts/LICENSE-OFL-AstaSans.txt` 에 있습니다.
- 차량 조작 그림: 49개 항목 중 46개 항목은 현대자동차 아반떼(CN8) 2027 취급설명서(https://ownersmanual.hyundai.com/full_webhelp/CN8/2027/ko_KR/index.html)의 그림을 쓰며, 저작권은 현대자동차에 있습니다(경고등 3색 항목은 설명서 그림에 색 설명을 덧붙였습니다). 나머지 3개 항목은 직접 그렸습니다. 공모전 운영사무국의 안내(2026-11-13)에 따라 출품작에 사용했습니다. 그림별 출처는 `docs/manual_figs/출처.md` 에 있습니다.
- 지도 자료: 도로와 건물 윤곽은 © OpenStreetMap contributors(ODbL), 건물 높이와 층수는 국토교통부 GIS건물통합정보(브이월드), 노면 표시, 신호등, 안전표지, 횡단보도 자리는 서울시 교통안전시설물 공개 자료(T-GIS, 서울 열린데이터광장, 공공누리 제1유형), 중앙버스정류장 자리는 서울시 중앙차로 버스정류소 리스트(서울 열린데이터광장 OA-21288, 공공누리 제1유형), 교차로 6곳의 신호 주기와 현시는 경찰청 교차로 신호 운영계획(공공데이터포털)이며, 나머지 교차로는 들어오는 차로 수로 정한 값을 씁니다. 옆 동 경계와 신사동·압구정동 경계는 통계청 통계지리정보서비스(SGIS, https://sgis.kostat.go.kr)가 공공누리 제1유형으로 개방한 행정동 경계를 vuski/admdongkor(https://github.com/vuski/admdongkor)가 가공한 자료(CC BY 4.0, https://creativecommons.org/licenses/by/4.0/)에서 가져와 좌표를 바꾸고 점을 줄여 썼습니다. 행정동 경계와 중앙버스정류장 출처는 출발 자리 고르기 화면에도 표시합니다. 베를린 동네의 도로망과 신호 위치는 © OpenStreetMap contributors(ODbL), 노면 표시, 차도와 보도, 과속방지턱, 교통표지, 신호 기둥은 Geoportal Berlin / Straßenbefahrung 2014, 건물은 Geoportal Berlin / 3D-Gebäudemodelle LoD2(둘 다 Datenlizenz Deutschland – Zero – Version 2.0)입니다. 도로 주행 화면에는 OpenStreetMap 과 브이월드(베를린은 OpenStreetMap 과 Geoportal Berlin) 출처를 표시합니다.
- 3D 지역 자료(`unity/DriveCoach3D/Assets/Resources/DC/Regions/` 폴더, 노면 `.bytes` 포함)는 OpenStreetMap 자료에서 만든 데이터베이스로 ODbL 1.0 을 따릅니다. 앱에서는 3D 도로 주행의 동네 목록 화면과 출발 자리 고르기 화면 맨 아래, 주행 화면 아래 가운데에 출처를 표시합니다. 차량과 건물에 실제 상표나 로고는 사용하지 않았습니다.
