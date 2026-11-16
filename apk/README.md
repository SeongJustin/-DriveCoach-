# APK

release APK 는 ABI 마다 하나씩, 두 개입니다(두 ABI 를 한 APK 에 넣으면 GitHub 파일 한도 100 MiB 를 넘습니다).

| 쓰는 곳 | 파일 |
|---|---|
| 실제 차량(arm64-v8a) | `성민준과강명수_드라이브코치_release_arm64.apk` |
| Pleos Connect Emulator(x86_64) | `성민준과강명수_드라이브코치_release_x86_64.apk` |

설치: Pleos Connect Emulator v2.1.0 화면에 `_x86_64.apk` 를 끌어다 놓거나 `adb install -r <파일>`

ABI 하나만 넣은 APK 는 `gradlew :app:assembleRelease -Pabi=arm64-v8a`(또는 `-Pabi=x86_64`)로 빌드합니다. 빌드 결과는 이 폴더에 `성민준과강명수_드라이브코치_release.apk` 로 복사되므로 위 이름으로 바꿉니다.
