// 코스 자동 생성
package kr.drivecoach.core.course

/**
 * 외부 라이브러리 없이 쓰는 아주 작은 JSON 파서.
 * 객체 → Map<String, Any?>, 배열 → List<Any?>, 문자열 → String, 숫자 → Long(정수) 또는 Double, true/false → Boolean, null → null.
 * 길찾기 API 응답(수백 KB)을 한 번 파싱하는 용도이며 스트리밍은 지원하지 않는다.
 */
object MiniJson {
    /** JSON 문자열 전체를 파싱한다. 형식이 틀리면 IllegalArgumentException. */
    fun parse(text: String): Any? {
        val p = Parser(text)
        val v = p.value()
        p.skipWs()
        require(p.i == text.length) { "JSON 끝에 불필요한 문자: 위치 ${p.i}" }
        return v
    }

    private class Parser(val s: String) {
        var i = 0

        fun skipWs() { while (i < s.length && s[i].isWhitespace()) i++ }

        fun value(): Any? {
            skipWs()
            require(i < s.length) { "JSON 이 예기치 않게 끝남" }
            return when (val c = s[i]) {
                '{' -> obj()
                '[' -> arr()
                '"' -> str()
                't' -> literal("true", true)
                'f' -> literal("false", false)
                'n' -> literal("null", null)
                else -> if (c == '-' || c.isDigit()) num() else throw IllegalArgumentException("JSON 위치 $i 의 문자 '$c' 를 해석할 수 없음")
            }
        }

        private fun literal(word: String, v: Any?): Any? {
            require(s.startsWith(word, i)) { "JSON 위치 $i: $word 기대" }
            i += word.length
            return v
        }

        private fun obj(): Map<String, Any?> {
            val m = LinkedHashMap<String, Any?>()
            i++ // {
            skipWs()
            if (i < s.length && s[i] == '}') { i++; return m }
            while (true) {
                skipWs()
                require(i < s.length && s[i] == '"') { "JSON 위치 $i: 객체 키 기대" }
                val k = str()
                skipWs()
                require(i < s.length && s[i] == ':') { "JSON 위치 $i: ':' 기대" }
                i++
                m[k] = value()
                skipWs()
                require(i < s.length) { "JSON 객체가 닫히지 않음" }
                when (s[i]) {
                    ',' -> i++
                    '}' -> { i++; return m }
                    else -> throw IllegalArgumentException("JSON 위치 $i: ',' 또는 '}' 기대")
                }
            }
        }

        private fun arr(): List<Any?> {
            val l = ArrayList<Any?>()
            i++ // [
            skipWs()
            if (i < s.length && s[i] == ']') { i++; return l }
            while (true) {
                l.add(value())
                skipWs()
                require(i < s.length) { "JSON 배열이 닫히지 않음" }
                when (s[i]) {
                    ',' -> i++
                    ']' -> { i++; return l }
                    else -> throw IllegalArgumentException("JSON 위치 $i: ',' 또는 ']' 기대")
                }
            }
        }

        private fun str(): String {
            i++ // opening quote
            val sb = StringBuilder()
            while (true) {
                require(i < s.length) { "JSON 문자열이 닫히지 않음" }
                val c = s[i++]
                when (c) {
                    '"' -> return sb.toString()
                    '\\' -> {
                        require(i < s.length) { "JSON 이스케이프가 끝나지 않음" }
                        when (val e = s[i++]) {
                            '"' -> sb.append('"'); '\\' -> sb.append('\\'); '/' -> sb.append('/')
                            'b' -> sb.append('\b'); 'f' -> sb.append('\u000C'); 'n' -> sb.append('\n')
                            'r' -> sb.append('\r'); 't' -> sb.append('\t')
                            'u' -> {
                                require(i + 4 <= s.length) { "JSON \\u 이스케이프 길이 부족" }
                                sb.append(s.substring(i, i + 4).toInt(16).toChar()); i += 4
                            }
                            else -> throw IllegalArgumentException("JSON 알 수 없는 이스케이프 \\$e")
                        }
                    }
                    else -> sb.append(c)
                }
            }
        }

        private fun num(): Number {
            val start = i
            if (s[i] == '-') i++
            while (i < s.length && s[i].isDigit()) i++
            var isDouble = false
            if (i < s.length && s[i] == '.') { isDouble = true; i++; while (i < s.length && s[i].isDigit()) i++ }
            if (i < s.length && (s[i] == 'e' || s[i] == 'E')) {
                isDouble = true; i++
                if (i < s.length && (s[i] == '+' || s[i] == '-')) i++
                while (i < s.length && s[i].isDigit()) i++
            }
            val t = s.substring(start, i)
            return if (isDouble) t.toDouble() else (t.toLongOrNull() ?: t.toDouble())
        }
    }

    /** Map 에서 중첩 키를 안전하게 꺼낸다. 예: path(m, "route", "traoptimal"). 없으면 null. */
    fun path(root: Any?, vararg keys: String): Any? {
        var cur = root
        for (k in keys) {
            cur = (cur as? Map<*, *>)?.get(k) ?: return null
        }
        return cur
    }

    /** 숫자 값을 Double 로 읽는다. 숫자가 아니면 기본값. */
    fun double(v: Any?, default: Double = 0.0): Double = (v as? Number)?.toDouble() ?: default

    /** 숫자 값을 Int 로 읽는다. 숫자가 아니면 기본값. */
    fun int(v: Any?, default: Int = 0): Int = (v as? Number)?.toInt() ?: default

    /** 숫자 값을 Long 으로 읽는다. 숫자가 아니면 기본값. */
    fun long(v: Any?, default: Long = 0L): Long = (v as? Number)?.toLong() ?: default
}
