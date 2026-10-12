import Carbon
import Cocoa
import ServiceManagement
import WebKit

// 往已经退出的子进程（codex app-server）写东西时，系统默认发 SIGPIPE 把整个 App 杀掉：忽略它，写失败只返回错误
signal(SIGPIPE, SIG_IGN)

// 测试用：只问一次 GPT 额度，打印 JSON 就退出（tests/codex_quota_probe.sh）
func probeResult(_ rl: [String: Any]?, _ err: String?, _ ms: Int) -> [String: Any] {
    CodexQuotaSource.payload(source: rl != nil ? "live" : "none", rateLimits: rl, error: rl != nil ? nil : err, ms: ms)
}
if CommandLine.arguments.contains("--codex-quota-probe") {
    let sem = DispatchSemaphore(value: 0); var out: [String: Any] = [:]
    if let bin = CodexQuotaSource.findBinary() {
        // CROSSPET_CODEX_TIMEOUT：测试时把 15 秒超时缩短
        let timeout = Double(ProcessInfo.processInfo.environment["CROSSPET_CODEX_TIMEOUT"] ?? "") ?? 15
        CodexQuotaSource.queryLive(binary: bin, timeout: timeout) { rl, err, ms in
            out = probeResult(rl, err, ms); sem.signal()
        }
        sem.wait()
    } else { out = probeResult(nil, "没找到 codex 程序", 0) }
    let d = try! JSONSerialization.data(withJSONObject: out, options: [.sortedKeys])
    print(String(decoding: d, as: UTF8.self)); exit(0)
}

let app = NSApplication.shared
let delegate = App()
app.delegate = delegate
app.setActivationPolicy(.accessory)
app.run()

/// 只信任本机 127.0.0.1 的自签名证书（Antigravity 后台服务用的是自签名 HTTPS）
