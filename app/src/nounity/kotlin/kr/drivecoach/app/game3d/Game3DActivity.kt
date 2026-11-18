// 3D 게임(Unity)이 빌드에 없을 때의 대체 화면: 알림만 띄우고 닫는다.
// unity/export 가 있으면 src/unity 의 진짜 Game3DActivity 가 대신 들어간다(app/build.gradle.kts).
package kr.drivecoach.app.game3d

import android.app.Activity
import android.os.Bundle
import android.widget.Toast

class Game3DActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        Toast.makeText(this, "이 빌드에는 3D 연습이 들어 있지 않습니다", Toast.LENGTH_LONG).show()
        finish()
    }
}
