# 드라이브코치

차가 서 있을 때 차량 조작법, 주차, 도로 주행을 차 안 화면에서 학습하는 Pleos Connect(Android Automotive) 앱입니다.

## 1. 기능

학습 화면은 기어가 P 일 때만 표시됩니다. 주행 중에는 음성으로만 안내합니다.

| 카드 | 내용 |
|---|---|
| 차량 조작법 | 좌석, 안전벨트, 시동, 주차 브레이크, 변속, 등화, 와이퍼, 방향지시등, 창문, 공조, 클러스터, 운전자 보조 등 49개 항목을 음성과 그림(44개 항목)으로 설명합니다 |
| 도로 법규 | 우회전, 비보호 좌회전, 유턴, 어린이 보호구역 등 12개 주제를 도로교통법 조문 발췌와 장면 그림으로 설명합니다 |
| 주차 연습 | 직각 주차와 평행 주차를 단계 영상으로 설명합니다. 주차 게임(3D) 25단계에서는 실제 스티어링 휠과 브레이크로 3D 차를 조작해 주차를 연습합니다 |
| 도로 주행 연습 | 실제 지도로 만든 3D 동네(서울 역삼1동, 양재1동, 양재2동)를 신호등, 차로, 보행자, 다른 차가 있는 환경에서 주행합니다 |
| 코스 주행 | 연습 목적을 고르면 현재 위치 주변의 코스를 생성해 차량 내비게이션에 경로로 전달합니다. 주행 중에는 위험 상황만 음성으로 경고합니다. 도착 후 P 로 정차하면 주행 리포트를 표시합니다 |

음성 명령은 "다음", "다시", "확인", "이전", "조용히", "세울래요"입니다.

## 2. 실행

1. Android Studio 의 Device Manager 에서 Pleos Connect Emulator v2.1.0 을 실행합니다.
2. `apk/성민준과강명수_드라이브코치_release.apk` 를 에뮬레이터 화면에 끌어다 놓거나 `adb install -r <파일>` 로 설치합니다.
3. 앱 목록에서 드라이브코치를 실행하고 권한을 허용합니다.
4. 차량 신호는 Virtual Console(`http://127.0.0.1:8088`)에서 변경합니다. 기어가 P 일 때만 학습 화면이 표시됩니다.
5. 음성 명령을 시험하려면 에뮬레이터의 Extended Controls > Microphone > Enable Host Microphone Access 를 활성화합니다.

## 3. 빌드

1. Android Studio(JDK 17, Android SDK API 34)에서 저장소 폴더를 열고 Gradle Sync 를 실행합니다. Pleos SDK 는 `settings.gradle.kts` 의 Playground Maven 저장소에서 내려받습니다.
2. `app` 을 Run 하거나 Build Variants 를 release 로 변경해 APK 를 빌드합니다.
3. 3D 화면은 `unity/DriveCoach3D`(Unity 6000.3)를 Android 라이브러리로 내보내 `unity/export` 에 두면 앱에 포함됩니다. 없으면 3D 화면을 제외하고 빌드됩니다.

## 4. 환경

| 항목 | 값 |
|---|---|
| 검증 환경 | Pleos Connect Emulator v2.1.0(API 34), Virtual Console v2.1.0. 실차 검증은 수행하지 않았습니다 |
| 화면 규격 | 2560 x 1440(가로) |
| SDK | minSdk 28, targetSdk 34, AGP 8.8.2, Kotlin 2.0, Jetpack Compose |
| 기준 차종 | 현대 아반떼 CN8 |
| 권한 | Pleos Vehicle SDK 읽기 권한, NaviHelper, TTS, STT, FusedLocation, Android Automotive 표준 속성(`CAR_SPEED`, `CAR_POWERTRAIN`, `CAR_INFO`, `READ_CAR_PEDALS`), 인터넷, 포그라운드 서비스와 알림 |
| 네트워크 | 제출 APK 는 길찾기 키를 포함하지 않아 코스 생성용 길찾기 호출을 하지 않습니다 |

## 5. Pleos Playground SDK

| SDK | 쓰는 곳 | 코드 |
|---|---|---|
| Vehicle 2.3.2 | 기어, 브레이크, 조향각, 등화, 방향지시등, 와이퍼, 창문, 도어, 좌석, 공조, 연료, 안전벨트 경고, 차종 | `app/.../vehicle/PleosSignalSource.kt` |
| NaviHelper 3.0.5 | 경로와 경유지 요청, 회전 정보, 현재 도로 제한 속도, 도착 상태 | `app/.../navi/PleosNaviAdapter.kt` |
| FusedLocation 1.1.3 | 현재 위치 | `app/.../vehicle/PleosSignalSource.kt` |
| TextToSpeech 3.3.1 | 음성 안내(ON_DEVICE) | `app/.../speech/PleosTtsSpeaker.kt` |
| SpeechToText 3.3.1 | 음성 명령(ON_DEVICE) | `app/.../speech/VoiceCommandListener.kt` |

속도, 주차 브레이크, 시동 상태는 Android Automotive 표준 차량 속성으로 읽습니다.

## 6. 구조

```
apk/     release APK
core/    차량, Android 와 무관한 Kotlin 로직(신호 모델, 연습 항목, 주차 절차, 코스 생성, 위험 판정, 음성 문장)
app/     Pleos SDK 어댑터, Compose 화면, 포그라운드 서비스
unity/DriveCoach3D/   3D 주차 게임과 도로 주행(Unity 6). Assets/DriveCoach/Core 는 계산, Runtime 은 화면
```

## 7. 자료와 라이선스

- 주행 기록과 리포트는 차 안에만 저장합니다.
- 오픈소스: AndroidX, Jetpack Compose(Apache 2.0), Kotlin Coroutines(Apache 2.0), Noto Sans KR, Asta Sans(OFL), Unity 6. 3D 모델과 질감은 Poly Haven, Kenney, Quaternius(CC0), EZ-Tree(MIT)입니다. 라이선스 글은 `unity/DriveCoach3D/Assets/Resources/DC/*/License.txt` 에 있습니다.
- 차량 조작 그림: 현대자동차 아반떼(CN8) 2027 취급설명서(https://ownersmanual.hyundai.com/full_webhelp/CN8/2027/ko_KR/index.html)의 그림입니다. 저작권은 현대자동차에 있습니다. 공모전 운영사무국의 안내(2026-11-13)에 따라 출품작에 사용했습니다. 그림별 출처는 `docs/manual_figs/출처.md` 에 있습니다.
- 지도 자료: 도로와 건물 윤곽은 © OpenStreetMap contributors(ODbL), 건물 높이와 층수는 국토교통부 GIS건물통합정보(브이월드), 노면 표시, 신호등, 안전표지, 횡단보도 자리는 서울시 교통안전시설물 공개 자료(T-GIS), 신호 시간표는 경찰청 교차로 신호 운영계획(공공데이터포털)입니다. 게임 화면에 출처를 표시합니다. 차량과 건물에 실제 상표나 로고는 사용하지 않았습니다.
