import java.util.Properties

// 담당: A (Android 앱 모듈. 이 환경에서는 빌드 불가 — Android Studio + Pleos Connect Emulator 에서 빌드한다)
plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

// (담당 S, 11/8) 3D 주차·핸들 연습(Unity). 빌드 도구 이 만든 AAR 이 있을 때만 넣는다(없으면 2D 만, HAS_UNITY = false).
val unityAar = rootProject.file("unity/export/unityLibrary-release.aar")
val hasUnity = unityAar.exists()

android {
    namespace = "kr.drivecoach.app"
    compileSdk = 34

    defaultConfig {
        applicationId = "kr.drivecoach.app"
        minSdk = 28      // minSdk 28 (Pleos getting-started 도 API 28 이상 요구)
        targetSdk = 34
        versionCode = 1
        versionName = "1.0.0"
        // 주차 게임(Godot) 네이티브 라이브러리는 ABI 마다 수십 MB 라 실제 차량(arm64)·에뮬레이터(x86_64) 것만 넣는다.
        ndk { abiFilters += listOf("arm64-v8a", "x86_64") }
    }

    buildTypes {
        release {
            // 공모전 제출용 release APK(실행 가능해야 함): 별도 키스토어 없이 디버그 키로 서명한다. 앱 마켓 출시 때는 출시용 키로 바꿀 것.
            signingConfig = signingConfigs.getByName("debug")
            // 11/7: R8(쓰지 않는 코드 줄이기) + 쓰지 않는 리소스 빼기. Pleos SDK·Godot·우리 앱 코드는 이름째 전부 남기고(proguard-rules.pro),
            // AndroidX·Compose·Material·kotlin-reflect 같은 라이브러리에서 안 쓰는 부분만 뺀다. 난독화는 하지 않는다(-dontobfuscate).
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            // 11/7 결정: 제출용 release APK 는 GitHub 저장소(apk/)에 올라가므로 API 키를 넣지 않는다(아래 defaultConfig 값을 빈 값으로 덮어씀).
            // → release 에서는 코스 자동 생성(네이버 Directions)이 꺼진다. 키가 필요한 시연은 debug 빌드로 한다.
            buildConfigField("String", "NCP_KEY_ID", "\"\"")
            buildConfigField("String", "NCP_KEY", "\"\"")
            buildConfigField("int", "DEMO_SPEED_LIMIT_KMH", "0")
        }
    }
    // APK 크기: minSdk 28 이상이면 .so 와 dex 를 압축 없이 넣는 게 기본이라 306MB 가 됐다(Godot .so 2개만 145MB).
    // 압축해서 넣으면 Godot .so 145→49MB, dex 36→12MB. 설치 때 한 번 풀어 두는 방식이라 실행 속도는 같다.
    packaging {
        jniLibs {
            useLegacyPackaging = true
            // (S, 11/8) 3D(Unity)와 2D(Godot) 엔진이 같은 C++ 표준 라이브러리(libc++_shared.so)를 각각 넣어 겹친다 → 하나만.
            //  두 게임은 따로 도는 프로세스(:game, :game3d)이고, 자동 검수가 둘 다 켜지는지 확인한다.
            pickFirsts += "lib/*/libc++_shared.so"
        }
        dex { useLegacyPackaging = true }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures { compose = true; buildConfig = true }
    sourceSets["main"].kotlin.srcDirs("src/main/kotlin")
    // debug 전용 자가 검사 화면(SelfTestActivity, 빌드 도구)
    sourceSets["debug"].kotlin.srcDirs("src/debug/kotlin")
    // 3D 게임: Unity 가 있으면 진짜 Game3DActivity(src/unity), 없으면 2D 로 넘기는 대체 화면(src/nounity)
    sourceSets["main"].kotlin.srcDirs(if (hasUnity) "src/unity/kotlin" else "src/nounity/kotlin")
    // 주차 게임: godot/ 프로젝트를 export-pack 한 결과를 APK assets 로 넣는다.
    sourceSets["main"].assets.srcDirs("src/main/assets")
    // Godot 는 숨김 폴더(.godot/)를 쓰므로 기본 무시 규칙에서 '.' 시작 항목 제외를 뺀다(공식 문서 'Embedding Godot in Android projects').
    androidResources {
        ignoreAssetsPattern = "!.svn:!.git:!.gitignore:!.ds_store:!*.scc:<dir>_*:!CVS:!thumbs.db:!picasa.ini:!*~"
        // 3D 게임(Unity): 엔진이 APK 안 데이터 파일을 압축 없이 바로 읽는다(Unity 가 내보낸 launcher/build.gradle 과 같은 목록)
        if (hasUnity) noCompress += listOf(".unity3d", ".ress", ".resource", ".obb", ".bundle", ".unityexp")
    }

    // (담당 J) 네이버 클라우드 Directions 15 API 키. 루트 local.properties 의 NCP_KEY_ID / NCP_KEY 를 BuildConfig 로 넣는다.
    // 키가 없으면 빈 문자열이 되고 NaverDirectionsProvider.fromBuildConfig() 가 null 을 돌려줘 코스 생성이 비활성화된다. 키는 커밋하지 않는다.
    val localProps = Properties().apply {
        val f = rootProject.file("local.properties")
        if (f.exists()) f.inputStream().use { load(it) }
    }
    defaultConfig {
        buildConfigField("String", "NCP_KEY_ID", "\"${localProps.getProperty("NCP_KEY_ID", "")}\"")
        buildConfigField("String", "NCP_KEY", "\"${localProps.getProperty("NCP_KEY", "")}\"")
        // Pleos TTS·STT 는 ON_DEVICE 만 쓰므로 Pleos Client ID/Secret 은 앱에 넣지 않는다(11/7, 서버 모드 제거).
        // 시연 빌드 전용: 제한속도를 모를 때 쓸 기본값(km/h). 시연 영상 찍을 때만 local.properties 에 DEMO_SPEED_LIMIT_KMH=50, 평소·제출은 0(끔).
        buildConfigField("int", "DEMO_SPEED_LIMIT_KMH", localProps.getProperty("DEMO_SPEED_LIMIT_KMH", "0").trim().ifEmpty { "0" })
        buildConfigField("boolean", "HAS_UNITY", hasUnity.toString())
        // (S, 11/8) 3D 를 새로 만들면(AAR 시각이 바뀌면) 지난 3D 실패 기록을 잊고 다시 3D 를 연다(GameLauncher)
        buildConfigField("String", "UNITY_STAMP", "\"${if (hasUnity) unityAar.lastModified() / 1000 else 0}\"")
    }
    // android.car (CarPropertyManager, CarUxRestrictionsManager) 는 AAOS 플랫폼 라이브러리라 별도 의존성이 없다.
    // 매니페스트의 <uses-library android:name="android.car"/> 로 연결한다.
    useLibrary("android.car")
}

dependencies {
    implementation(project(":core"))

    // Pleos Connect SDK
    implementation("ai.pleos.playground:Vehicle:2.3.2")
    implementation("ai.pleos.playground:NaviHelper:3.0.5")
    implementation("ai.pleos.playground:TextToSpeech:3.3.1")
    implementation("ai.pleos.playground:SpeechToText:3.3.1")
    implementation("ai.pleos.playground:FusedLocation:1.1.3")

    implementation("androidx.fragment:fragment-ktx:1.8.5")
    implementation("androidx.core:core-splashscreen:1.0.1")

    // 3D 게임(Unity 6, Unity as a Library): 엔진·데이터·unity-classes 가 모두 든 AAR 하나
    if (hasUnity) implementation(files(unityAar))
    // AAR 파일로 넣으면 Unity 가 기대는 라이브러리가 따라오지 않는다: 내보낸 unityLibrary/build.gradle 의 것을 그대로(화면 주기 맞추기, Swappy)
    if (hasUnity) implementation("androidx.games:games-frame-pacing:2.1.2")

    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.8.1")
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.4")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.8.4")
    implementation("androidx.activity:activity-compose:1.9.1")
    implementation(platform("androidx.compose:compose-bom:2024.06.00"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-graphics")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-core") // 설정 톱니바퀴 아이콘(Icons.Filled.Settings)
    debugImplementation("androidx.compose.ui:ui-tooling")
}

// 제출 규칙: 저장소 apk/ 폴더에 `팀명_앱이름_release.apk`. assembleRelease(Android Studio: Build Variants=release → Build APK) 가 끝나면 복사한다.
val copyReleaseApk by tasks.registering(Copy::class) {
    from(layout.buildDirectory.dir("outputs/apk/release")) {
        include("*.apk")
        rename { "성민준과강명수_드라이브코치_release.apk" }
    }
    into(rootProject.layout.projectDirectory.dir("apk"))
}
tasks.matching { it.name == "assembleRelease" }.configureEach { finalizedBy(copyReleaseApk) }
