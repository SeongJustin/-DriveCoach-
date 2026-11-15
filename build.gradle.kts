// 담당: A (루트 빌드 스크립트)
// 버전은 Pleos 샘플 앱(pleos-sample-vehicle-app, 2026-10 기준)과 맞춘다: AGP 8.8.2, Kotlin 2.0.0, JDK 17.
plugins {
    id("com.android.application") version "8.8.2" apply false
    id("org.jetbrains.kotlin.android") version "2.0.0" apply false
    id("org.jetbrains.kotlin.jvm") version "2.0.0" apply false
    id("org.jetbrains.kotlin.plugin.compose") version "2.0.0" apply false
}
