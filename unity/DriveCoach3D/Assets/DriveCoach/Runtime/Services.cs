// 3D 주차 연습: 앱 연결(안드로이드), 주차 센서 소리, 진행 기록 저장
using System.Collections.Generic;
using System.Text;
using DriveCoach.Core;
using UnityEngine;

namespace DriveCoach.Unity
{
    /// <summary>
    /// 앱(Kotlin Game3DActivity) 쪽으로 부르기. 받는 쪽은 GameMain 의 OnVehicleState/OnCarSpec(UnitySendMessage).
    /// 편집기·PC 에서는 아무것도 하지 않는다.
    /// </summary>
    public static class AndroidLink
    {
        public static void Call(string method)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    act.Call(method);
                }
            }
            catch (System.Exception e) { Debug.LogWarning("AndroidLink " + method + ": " + e.Message); }
#endif
        }

        /// <summary>글 하나를 넘겨 부른다(할 일 음성 안내)</summary>
        public static void Call(string method, string arg)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    act.Call(method, arg);
                }
            }
            catch (System.Exception e) { Debug.LogWarning("AndroidLink " + method + ": " + e.Message); }
#endif
        }

        public static bool OnDevice
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }
    }

    /// <summary>주차 센서 경고음: 가까울수록 빨리, 30 cm 안이면 거의 계속</summary>
    public class Beeper
    {
        readonly AudioSource src;
        float t;

        public Beeper(GameObject host)
        {
            src = host.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.volume = 0.5f;
            const int rate = 22050;
            int n = (int)(rate * 0.075f);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float env = Mathf.Min(1f, Mathf.Min(i / 220f, (n - i) / 330f));
                data[i] = Mathf.Sin(2f * Mathf.PI * 1250f * i / rate) * 0.28f * env;
            }
            var clip = AudioClip.Create("beep", n, 1, rate, false);
            clip.SetData(data, 0);
            src.clip = clip;
        }

        public void Tick(World w, bool active, float dt)
        {
            if (!active || w.state != PlayState.Play) { t = 0f; return; }
            float d = Mathf.Min(w.sensorFront, w.sensorRear);
            if (d > 1.2f) { t = 0f; return; }
            float interval = d < 0.3f ? 0.09f : (d < 0.6f ? 0.22f : (d < 0.9f ? 0.45f : 0.8f));
            t -= dt;
            if (t <= 0f) { src.Play(); t = interval; }
        }
    }

    /// <summary>별점·완료 기록(기기 안 PlayerPrefs). 키 = "stage:7", "tut:2", "circuit:1", "road:1"</summary>
    public static class ProgressStore
    {
        const string Key = "dc3d_progress";
        static Dictionary<string, int> data;

        static void Load()
        {
            if (data != null) return;
            data = new Dictionary<string, int>();
            var o = MiniJson.ParseObject(PlayerPrefs.GetString(Key, "{}"));
            if (o == null) return;
            foreach (var kv in o) if (kv.Value is double d) data[kv.Key] = (int)d;
        }

        public static int Stars(string key) { Load(); return data.TryGetValue(key, out var v) ? v : 0; }

        public static void Record(string key, int stars)
        {
            Load();
            if (Stars(key) >= stars) return;
            data[key] = stars;
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var kv in data)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(MiniJson.Quote(kv.Key)).Append(':').Append(kv.Value);
            }
            sb.Append('}');
            PlayerPrefs.SetString(Key, sb.ToString());
            PlayerPrefs.Save();
        }
    }
}
