# release R8 규칙.
# 원칙: 리플렉션·JNI·AIDL 로 이름을 찾는 코드는 이름째 전부 남기고, 라이브러리에서 아무도 부르지 않는 코드만 뺀다.

# 이름을 바꾸지 않는다: 로그·오류 추적을 그대로 읽을 수 있고, 이름으로 찾는 코드(JNI·리플렉션)가 깨지지 않는다.
-dontobfuscate
-keepattributes SourceFile,LineNumberTable,Signature,InnerClasses,EnclosingMethod,*Annotation*

# Pleos SDK: AIDL 바인더, Gson·kotlinx.serialization·kotlin-reflect 모델, Hilt, JNA/UniFFI(UA 보안 모듈)
-keep class ai.pleos.** { *; }
-keep interface ai.pleos.** { *; }
-keep class kotlin.Metadata { *; }
# JNA: 네이티브(libjnidispatch)가 이름으로 Java 클래스를 찾는다
-keep class com.sun.jna.** { *; }
-dontwarn java.awt.**
-dontwarn com.sun.jna.**

# 3D 게임: 엔진이 JNI 로 Java 클래스·메서드를 이름으로 부른다(unityLibrary 의 proguard-unity.txt 와 같은 취지)
-keep class com.unity3d.** { *; }
-keep interface com.unity3d.** { *; }
-keep class bitter.jnibridge.** { *; }
-keep class org.fmod.** { *; }
-keep class com.google.androidgamesdk.** { *; }
-dontwarn com.unity3d.**
-dontwarn org.fmod.**
-dontwarn com.google.androidgamesdk.**

# 우리 앱 코드(작음): 게임 연결·서비스·화면을 전부 남긴다
-keep class kr.drivecoach.** { *; }

# android.car 는 차량 플랫폼 라이브러리(APK 에 들어가지 않음)
-dontwarn android.car.**
