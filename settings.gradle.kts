// 담당: A (프로젝트 뼈대)
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

// (담당 S, 11/8) 3D 주차·핸들 연습(Unity)은 모듈이 아니라 AAR 파일 하나(unity/export/unityLibrary-release.aar)로 들어간다
// — Unity 6.6 이 내보내는 Gradle 프로젝트는 AGP 9 를 쓰고 한글 경로에서 빌드되지 않아, 빌드 도구 이 영문 경로에서 AAR 로 만들어 가져온다.
