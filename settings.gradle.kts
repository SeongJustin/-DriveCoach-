// 프로젝트 뼈대
// Pleos Connect SDK 는 Pleos Playground Maven 저장소에서 받는다.
pluginManagement {
    repositories {
        google {
            content {
                includeGroupByRegex("com\\.android.*")
                includeGroupByRegex("com\\.google.*")
                includeGroupByRegex("androidx.*")
            }
        }
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
        // Pleos Playground SDK 저장소 (문서 확인: Vehicle/NaviHelper/TTS/STT/FusedLocation 모두 여기)
        maven("https://nexus-playground.pleos.ai/repository/maven-releases/")
    }
}

rootProject.name = "driving-coach"
include(":core")
include(":app")

// 3D 주차·도로 주행(Unity)은 모듈이 아니라 AAR 파일 하나(unity/export/unityLibrary-release.aar)로 들어간다(app/build.gradle.kts)
// Unity 가 내보낸 Gradle 프로젝트는 AGP 9 를 쓰고 한글 경로에서 빌드되지 않아, 영문 경로에서 unityLibrary 모듈을 AAR 로 따로 빌드해 가져온다.
