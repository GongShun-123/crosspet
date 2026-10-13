import Carbon
import Cocoa
import ServiceManagement
import WebKit

extension App {
    // MARK: 设置
    // 一个独立窗口（web/settings.html，和 Windows 版共用），右键菜单只留常用的几项，其余都在这里
    static let baseSize = NSSize(width: 260, height: 362)
    var petScale: CGFloat {
        let v = defaults.double(forKey: "size")
        return v >= 0.6 && v <= 1.6 ? CGFloat(v) : 1
    }
    var showName: Bool { defaults.object(forKey: "showName") as? Bool ?? true }
    var followApps: Bool { defaults.object(forKey: "followApps") as? Bool ?? true }
    var eggsEnabled: Bool { defaults.object(forKey: "eggs") as? Bool ?? true }

    /// 改大小：窗口和网页一起按比例缩放，底边中点不动（脚下的位置不变）
    func applyScale(_ scale: CGFloat) {
        let old = panel.frame
        let size = NSSize(width: Self.baseSize.width * scale, height: Self.baseSize.height * scale)
        let origin = NSPoint(x: old.midX - size.width / 2, y: old.minY)
        panel.setFrame(NSRect(origin: origin, size: size), display: true)
        web.pageZoom = scale
        defaults.set([origin.x, origin.y], forKey: "origin")
    }

    /// 拖滑块时每次只变一点，直接跟手；一下子变很多（点「还原」）时用 0.2 秒过渡过去
    func animateScale(to target: CGFloat) {
        scaleTimer?.invalidate()
        let from = shownScale > 0 ? shownScale : petScale
        guard abs(target - from) > 0.06 else { shownScale = target; applyScale(target); return }
        let start = Date()
        scaleTimer = Timer.scheduledTimer(withTimeInterval: 1.0 / 60, repeats: true) { [weak self] t in
            guard let self else { t.invalidate(); return }
            let k = min(1, Date().timeIntervalSince(start) / 0.2)
            let eased = 1 - pow(1 - k, 3)
            self.shownScale = from + (target - from) * CGFloat(eased)
            self.applyScale(self.shownScale)
            if k >= 1 { t.invalidate(); if self.dock != nil { self.applyDock() } }   // 贴着边时调完大小重新对齐边缘
        }
    }

    @objc func openSettings() {
        if let w = settingsWindow {
            NSApp.activate(ignoringOtherApps: true)
            w.makeKeyAndOrderFront(nil)
            return
        }
        let config = WKWebViewConfiguration()
        config.userContentController.add(DevBridge(app: self, kind: .settings), name: "settings")
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 520, height: 680),
                         styleMask: [.titled, .closable, .resizable, .miniaturizable], backing: .buffered, defer: false)
        w.title = "CrossPet 设置"
        w.isReleasedWhenClosed = false
        w.center()
        let v = WKWebView(frame: w.contentView!.bounds, configuration: config)
        v.autoresizingMask = [.width, .height]
        v.navigationDelegate = self
        v.loadFileURL(root.appendingPathComponent("web/settings.html"), allowingReadAccessTo: root)
        w.contentView!.addSubview(v)
        settingsWindow = w
        settingsWeb = v
        NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: w, queue: .main) { [weak self] _ in
            self?.settingsWeb = nil
            self?.settingsWindow = nil
        }
        NSApp.activate(ignoringOtherApps: true)
        w.makeKeyAndOrderFront(nil)
    }

    func settingsState() -> [String: Any] {
        let names = defaults.dictionary(forKey: "names") as? [String: String] ?? [:]
        var state: [String: Any] = [
            "version": currentVersion, "platform": "mac",
            "size": Double(petScale), "showName": showName, "eggs": eggsEnabled, "hideFullscreen": hideInFullscreen, "aura": auraMode, "followApps": followApps,
            "gptQuota": defaults.bool(forKey: "gptQuota"), "agyQuota": defaults.bool(forKey: "agyQuota"), "gptQuotaNote": codexNote,
            "login": SMAppService.mainApp.status == .enabled,
            "edgeDock": edgeDockEnabled, "dsQuota": dsQuotaEnabled, "quotaAggregate": quotaAggregate, "powerSave": powerSave,
            "hideHotkey": hideHotkey, "hotkeyFailed": hotkeyFailed, "hotkeys": [["id": "off", "label": "不用快捷键"]] + Self.hotkeyChoices.map { ["id": $0.id, "label": $0.label] },
            "aiAutostart": aiAutostart,
            "character": currentId ?? characterIds.first ?? "",
            "characters": characterIds.map { id -> [String: Any] in
                var c: [String: Any] = ["id": id, "name": displayNames[id] ?? id, "defaultName": defaultNames[id] ?? id, "custom": names[id] ?? ""]
                if let skins = skinChoices[id], !skins.isEmpty {   // 有别的衣服：原版排第一
                    c["skins"] = [["id": "", "name": baseOutfit[id] ?? "原版"]] + skins.map { ["id": $0.id, "name": $0.name] }
                    c["skin"] = (defaults.dictionary(forKey: "skins") as? [String: String])?[id] ?? ""
                }
                return c
            },
            "updating": updating,
        ]
        if let r = latestRelease, canSelfUpdate { state["update"] = r.tag }
        if let list = integrated, integrateScript != nil {
            state["integrations"] = Self.integrationTargets.map {
                ["id": $0.id, "label": $0.label, "note": $0.note, "installed": list.contains($0.id),
                 "lastSeen": lastSeen[Self.seenKeys[$0.id] ?? ""].map { Date().timeIntervalSince1970 - $0 as Any } ?? NSNull()] as [String: Any]
            }
        }
        return state
    }

}
