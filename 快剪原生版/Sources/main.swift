import SwiftUI
import AppKit
import Combine
import UniformTypeIdentifiers
import Sparkle
import Foundation
import ServiceManagement

private extension Array {
    subscript(safe index: Int) -> Element? { indices.contains(index) ? self[index] : nil }
}

// MARK: - Models

enum SidebarSection: String, CaseIterable, Identifiable {
    case clipboard, team, qa, calculator, downloads, shortcuts, translation, connections, transfer, favorites
    var id: String { rawValue }
    var title: String {
        switch self {
        case .clipboard: return "剪贴板"
        case .team: return "团队"
        case .qa: return "问答"
        case .calculator: return "计算机"
        case .downloads: return "下载"
        case .shortcuts: return "快捷"
        case .translation: return "翻译"
        case .connections: return "连接"
        case .transfer: return "互传"
        case .favorites: return "收藏"
        }
    }
    var icon: String {
        switch self {
        case .clipboard: return "sidebar-icon-01"
        case .team: return "sidebar-icon-02"
        case .qa: return "sidebar-icon-03"
        case .calculator: return "sidebar-icon-04"
        case .downloads: return "sidebar-icon-05"
        case .shortcuts: return "sidebar-icon-06"
        case .translation: return "sidebar-icon-07"
        case .connections: return "sidebar-icon-08"
        case .transfer: return "sidebar-icon-09"
        case .favorites: return "sidebar-icon-10"
        }
    }
}

enum ClipKind: String, Codable {
    case text, url, image
}

enum QAMatchMode: String, Codable, CaseIterable, Identifiable {
    case exact, fuzzy
    var id: String { rawValue }
    var title: String { self == .exact ? "精准匹配" : "模糊匹配" }
}

enum TranslationLanguage: String, CaseIterable, Identifiable {
    case auto, chinese, english, japanese, korean

    var id: String { rawValue }
    var title: String {
        switch self {
        case .auto: return "自动识别"
        case .chinese: return "中文"
        case .english: return "英文"
        case .japanese: return "日文"
        case .korean: return "韩文"
        }
    }

    var localeIdentifier: String? {
        switch self {
        case .auto: return nil
        case .chinese: return "zh-Hans"
        case .english: return "en"
        case .japanese: return "ja"
        case .korean: return "ko"
        }
    }
}

struct ClipboardItem: Identifiable, Codable {
    let id: UUID
    var title: String
    var text: String
    var kind: ClipKind
    var favorite: Bool
    var pinned: Bool
    var source: String
    var note: String
    var createdAt: Date
    var fileURLs: [String]
    var imagePath: String?
    var imageDataBase64: String?
    var richTextBase64: String?

    init(id: UUID = UUID(), title: String, text: String, kind: ClipKind = .text,
         favorite: Bool = false, pinned: Bool = false, source: String = "来源未知",
         note: String = "", createdAt: Date = Date(), fileURLs: [String] = [],
         imagePath: String? = nil, imageDataBase64: String? = nil, richTextBase64: String? = nil) {
        self.id = id
        self.title = title
        self.text = text
        self.kind = kind
        self.favorite = favorite
        self.pinned = pinned
        self.source = source
        self.note = note
        self.createdAt = createdAt
        self.fileURLs = fileURLs
        self.imagePath = imagePath
        self.imageDataBase64 = imageDataBase64
        self.richTextBase64 = richTextBase64
    }

    enum CodingKeys: String, CodingKey {
        case id, title, text, kind, favorite, pinned, source, note, createdAt, fileURLs, imagePath, imageDataBase64, richTextBase64
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decodeIfPresent(UUID.self, forKey: .id) ?? UUID()
        title = try c.decodeIfPresent(String.self, forKey: .title) ?? ""
        text = try c.decodeIfPresent(String.self, forKey: .text) ?? title
        kind = try c.decodeIfPresent(ClipKind.self, forKey: .kind) ?? .text
        favorite = try c.decodeIfPresent(Bool.self, forKey: .favorite) ?? false
        pinned = try c.decodeIfPresent(Bool.self, forKey: .pinned) ?? false
        source = try c.decodeIfPresent(String.self, forKey: .source) ?? "来源未知"
        note = try c.decodeIfPresent(String.self, forKey: .note) ?? ""
        fileURLs = try c.decodeIfPresent([String].self, forKey: .fileURLs) ?? []
        imagePath = try c.decodeIfPresent(String.self, forKey: .imagePath)
        imageDataBase64 = try c.decodeIfPresent(String.self, forKey: .imageDataBase64)
        richTextBase64 = try c.decodeIfPresent(String.self, forKey: .richTextBase64)
        if let seconds = try c.decodeIfPresent(Double.self, forKey: .createdAt) {
            createdAt = Date(timeIntervalSinceReferenceDate: seconds)
        } else {
            createdAt = Date()
        }
    }

    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(id, forKey: .id)
        try c.encode(title, forKey: .title)
        try c.encode(text, forKey: .text)
        try c.encode(kind, forKey: .kind)
        try c.encode(favorite, forKey: .favorite)
        try c.encode(pinned, forKey: .pinned)
        try c.encode(source, forKey: .source)
        try c.encode(note, forKey: .note)
        try c.encode(createdAt.timeIntervalSinceReferenceDate, forKey: .createdAt)
        try c.encode(fileURLs, forKey: .fileURLs)
        try c.encodeIfPresent(imagePath, forKey: .imagePath)
        try c.encodeIfPresent(imageDataBase64, forKey: .imageDataBase64)
        try c.encodeIfPresent(richTextBase64, forKey: .richTextBase64)
    }
}

struct QAEntry: Identifiable, Codable {
    let id: UUID
    var question: String
    var answer: String
    var aliases: String
    var keywords: String
    var category: String
    var note: String
    var enabled: Bool

    init(id: UUID = UUID(), question: String, answer: String, aliases: String = "",
         keywords: String = "", category: String = "", note: String = "", enabled: Bool = true) {
        self.id = id
        self.question = question
        self.answer = answer
        self.aliases = aliases
        self.keywords = keywords
        self.category = category
        self.note = note
        self.enabled = enabled
    }
}

struct LocalUser: Codable {
    let id: UUID
}

struct ShortcutItem: Identifiable, Codable {
    let id: UUID
    var name: String
    var target: String
    var hotkey: String
    var enabled: Bool
    var kind: String
    init(id: UUID = UUID(), name: String, target: String, hotkey: String = "",
         enabled: Bool = true, kind: String = "link") {
        self.id = id
        self.name = name
        self.target = target
        self.hotkey = hotkey
        self.enabled = enabled
        self.kind = kind
    }
}

private struct LegacyShortcutItem: Codable {
    var id: UUID?
    var name: String
    var target: String
    var kind: String
    var enabled: Bool
    var keyCode: Int?
    var modifiers: UInt?
}

struct DeviceItem: Identifiable, Codable {
    let id: String
    var name: String
    var platform: String
    var online: Bool
}

struct DeviceListEnvelope: Decodable {
    var devices: [DeviceItem]
    init(devices: [DeviceItem] = []) { self.devices = devices }
    private enum CodingKeys: String, CodingKey { case devices, data }
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        if let devices = try c.decodeIfPresent([DeviceItem].self, forKey: .devices) { self.devices = devices; return }
        if let data = try c.decodeIfPresent([DeviceItem].self, forKey: .data) { self.devices = data; return }
        self.devices = []
    }
}

struct ClipboardEnvelope: Decodable {
    var items: [ClipboardItem]
    private enum CodingKeys: String, CodingKey { case items, data }
    init(from decoder: Decoder) throws {
        if let array = try? decoder.singleValueContainer().decode([ClipboardItem].self) { items = array; return }
        let c = try decoder.container(keyedBy: CodingKeys.self)
        items = (try c.decodeIfPresent([ClipboardItem].self, forKey: .items)) ?? (try? c.decodeIfPresent([ClipboardItem].self, forKey: .data)) ?? []
    }
}

private struct ClipboardUploadEnvelope: Encodable {
    let items: [ClipboardItem]
}

final class XLSXReader {
    enum ReaderError: Error { case invalidArchive, missingSheet }

    static func rows(at url: URL) throws -> [[String]] {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("kj-xlsx-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/unzip")
        process.arguments = ["-qq", "-o", url.path, "-d", directory.path]
        try process.run(); process.waitUntilExit()
        guard process.terminationStatus == 0 else { throw ReaderError.invalidArchive }
        let sharedURL = directory.appendingPathComponent("xl/sharedStrings.xml")
        let sheetURL = directory.appendingPathComponent("xl/worksheets/sheet1.xml")
        guard let sheet = try? String(contentsOf: sheetURL, encoding: .utf8) else { throw ReaderError.missingSheet }
        let shared = (try? String(contentsOf: sharedURL, encoding: .utf8)).map(parseSharedStrings) ?? []
        return parseSheet(sheet, sharedStrings: shared)
    }

    private static func parseSharedStrings(_ xml: String) -> [String] {
        let items = matches(xml, pattern: "<si\\b[\\s\\S]*?</si>")
        return items.map { block in
            matches(block, pattern: "<t(?:\\s[^>]*)?>([\\s\\S]*?)</t>").map(unescape).joined()
        }
    }

    private static func parseSheet(_ xml: String, sharedStrings: [String]) -> [[String]] {
        let rowBlocks = matches(xml, pattern: "<row\\b[\\s\\S]*?</row>")
        return rowBlocks.map { row in
            var values: [Int: String] = [:]
            for cell in matches(row, pattern: "<c\\b[\\s\\S]*?</c>") {
                guard let ref = attribute(cell, name: "r"), let value = firstMatch(cell, pattern: "<v>([\\s\\S]*?)</v>") else { continue }
                let type = attribute(cell, name: "t") ?? ""
                let text: String
                if type == "s", let index = Int(value), sharedStrings.indices.contains(index) { text = sharedStrings[index] }
                else { text = unescape(value) }
                values[columnIndex(ref)] = text
            }
            guard let maxColumn = values.keys.max() else { return [] }
            return (0...maxColumn).map { values[$0] ?? "" }
        }.filter { !$0.isEmpty }
    }

    private static func matches(_ string: String, pattern: String) -> [String] {
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return [] }
        let range = NSRange(string.startIndex..., in: string)
        return regex.matches(in: string, range: range).compactMap { match in
            guard let swiftRange = Range(match.range, in: string) else { return nil }
            return String(string[swiftRange])
        }
    }

    private static func firstMatch(_ string: String, pattern: String) -> String? {
        guard let regex = try? NSRegularExpression(pattern: pattern), let match = regex.firstMatch(in: string, range: NSRange(string.startIndex..., in: string)), match.numberOfRanges > 1, let range = Range(match.range(at: 1), in: string) else { return nil }
        return String(string[range])
    }

    private static func attribute(_ string: String, name: String) -> String? {
        firstMatch(string, pattern: "\\b\(name)=\"([^\"]*)\"")
    }

    private static func columnIndex(_ reference: String) -> Int {
        let letters = reference.prefix { $0.isLetter }
        return letters.reduce(0) { $0 * 26 + Int($1.asciiValue ?? 64) - 64 } - 1
    }

    private static func unescape(_ value: String) -> String {
        value.replacingOccurrences(of: "&amp;", with: "&")
            .replacingOccurrences(of: "&lt;", with: "<")
            .replacingOccurrences(of: "&gt;", with: ">")
            .replacingOccurrences(of: "&quot;", with: "\"")
            .replacingOccurrences(of: "&apos;", with: "'")
    }
}

// MARK: - Theme

extension Color {
    static let kjMint = Color(red: 0.08, green: 0.72, blue: 0.59)
    static let kjMintSoft = Color(red: 0.88, green: 0.97, blue: 0.93)
    static let kjInk = Color(red: 0.08, green: 0.13, blue: 0.16)
    static let kjMuted = Color(red: 0.36, green: 0.47, blue: 0.44)
    static let kjCanvas = Color(red: 0.98, green: 0.99, blue: 0.98)
    static let kjBorder = Color(red: 0.87, green: 0.91, blue: 0.89)
}

extension Date {
    var kjRelative: String {
        let seconds = max(0, Date().timeIntervalSince(self))
        if seconds < 60 { return "刚刚" }
        if seconds < 3600 { return "\(Int(seconds / 60)) 分钟前" }
        if seconds < 86400 { return "\(Int(seconds / 3600)) 小时前" }
        return "\(Int(seconds / 86400)) 天前"
    }
}

// MARK: - Store

@MainActor
final class AppModel: ObservableObject {
    @Published var selectedSection: SidebarSection = .clipboard
    @Published var items: [ClipboardItem] = []
    @Published var qaEntries: [QAEntry] = []
    @Published var qaQuestion = ""
    @Published var qaAnswer = ""
    @Published var qaMatchedQuestion = ""
    @Published var qaMatchScore = 0
    @Published var qaMatchMode: QAMatchMode = .fuzzy {
        didSet {
            UserDefaults.standard.set(qaMatchMode.rawValue, forKey: "qaMatchMode")
            updateQAAnswer()
        }
    }
    @Published var qaMessage = ""
    @Published var qaAssistantEnabled = false {
        didSet { UserDefaults.standard.set(qaAssistantEnabled, forKey: "qaAssistantEnabled") }
    }
    @Published var copiedItemID: UUID?
    @Published var copyMessage = ""
    @Published var translationInput = ""
    @Published var translationOutput = ""
    @Published var translationMessage = ""
    @Published var translationSourceLanguage: TranslationLanguage = .auto {
        didSet { UserDefaults.standard.set(translationSourceLanguage.rawValue, forKey: "translationSourceLanguage") }
    }
    @Published var translationTargetLanguage: TranslationLanguage = .auto {
        didSet { UserDefaults.standard.set(translationTargetLanguage.rawValue, forKey: "translationTargetLanguage") }
    }
    @Published var translationDetectedSource: TranslationLanguage = .auto
    @Published var translationResolvedTarget: TranslationLanguage = .auto
    @Published var calculatorDisplay = "0"
    @Published var calculatorExpression = ""
    @Published var calculatorHistory: [String] = []
    @Published var downloadURL = ""
    @Published var downloadMessage = ""
    @Published var downloadDirectory = "" { didSet { UserDefaults.standard.set(downloadDirectory, forKey: "downloadDirectory") } }
    @Published var downloadBrowser = "none" { didSet { UserDefaults.standard.set(downloadBrowser, forKey: "downloadBrowser") } }
    @Published var saveText = true { didSet { UserDefaults.standard.set(saveText, forKey: "captureText") } }
    @Published var saveImages = false { didSet { UserDefaults.standard.set(saveImages, forKey: "captureImages") } }
    @Published var saveURLs = true { didSet { UserDefaults.standard.set(saveURLs, forKey: "captureURLs") } }
    @Published var saveRichText = true { didSet { UserDefaults.standard.set(saveRichText, forKey: "captureRichText") } }
    @Published var screenshotShortcut = "⇧⌘4" { didSet { UserDefaults.standard.set(screenshotShortcut, forKey: "screenshotShortcut") } }
    @Published var showDesktopShortcutEnabled = true { didSet { UserDefaults.standard.set(showDesktopShortcutEnabled, forKey: "showDesktopShortcutEnabled") } }
    @Published var showDesktopShortcutModifier = "command" { didSet { UserDefaults.standard.set(showDesktopShortcutModifier, forKey: "showDesktopShortcutModifier") } }
    @Published var isRecordingShowDesktopShortcut = false
    @Published var showDesktopShortcutRecordingMessage = ""
    @Published var doubleClickCollapseEnabled = true { didSet { UserDefaults.standard.set(doubleClickCollapseEnabled, forKey: "doubleClickCollapseEnabled") } }
    @Published var alwaysOnTop = false { didSet { UserDefaults.standard.set(alwaysOnTop, forKey: "alwaysOnTop"); applyWindowPreferences() } }
    @Published var launchAtLogin = false { didSet { UserDefaults.standard.set(launchAtLogin, forKey: "launchAtLogin") } }
    @Published var isSidebarCollapsed = false
    @Published var qaAllowedApps: [String] = [] { didSet { UserDefaults.standard.set(qaAllowedApps, forKey: "qaAllowedApps") } }
    @Published var settingsMessage = ""
    @Published var devices: [DeviceItem] = []
    @Published var deviceMessage = ""
    @Published var transferMessage = ""
    @Published var cloudMessage = ""
    @Published var shortcuts: [ShortcutItem] = []
    @Published var settingsSection = "账号"
    @Published var isLoggedIn = false
    @Published var loginPhone = ""
    @Published var loginPassword = ""
    @Published var loginEmail = ""
    @Published var confirmPassword = ""
    @Published var loginMessage = ""
    @Published var authMode = "login"
    @Published var loginMethod = "sms"
    @Published var registerCode = ""
    @Published var authCodeMessage = ""
    @Published var authCodeCountdown = 0

    private let pasteboard = NSPasteboard.general
    private var pasteboardTimer: Timer?
    private var cloudSyncTimer: Timer?
    private var authCodeTimer: Timer?
    private var lastChangeCount = NSPasteboard.general.changeCount
    private var ignoreUntil = Date.distantPast
    private var calculatorValue: Double?
    private var calculatorOperator: String?
    private var calculatorStartsNewEntry = true
    private var copyFeedbackTask: Task<Void, Never>?
    private var globalMonitor: Any?
    private var localKeyboardMonitor: Any?
    private var localShortcutMonitor: Any?
    private var shortcutRecordingMonitor: Any?
    private var lastDesktopModifierTapAt = Date.distantPast
    private var lastRecordedModifier: String?

    private var appSupport: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("KuaiJianNative", isDirectory: true)
        try? FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        return base
    }
    private var cloudAccountID: String?
    private var userID: UUID = UUID()
    private var storageUserKey: String { cloudAccountID.map { "cloud-\($0)" } ?? userID.uuidString }
    private var userRoot: URL { appSupport.appendingPathComponent("users", isDirectory: true).appendingPathComponent(storageUserKey, isDirectory: true) }
    private var itemsURL: URL { userRoot.appendingPathComponent("clipboard-items.json") }
    private var qaURL: URL { userRoot.appendingPathComponent("qa-entries.json") }
    private var shortcutsURL: URL { appSupport.appendingPathComponent("shortcuts.json") }
    private var imagesRoot: URL { userRoot.appendingPathComponent("images", isDirectory: true) }

    init() {
        cloudAccountID = UserDefaults.standard.string(forKey: "kjCloudAccountID")
        let savedToken = UserDefaults.standard.string(forKey: "kjAccessToken")
        isLoggedIn = !(savedToken?.isEmpty ?? true) && cloudAccountID != nil
        if let saved = UserDefaults.standard.string(forKey: "kjUserID"), let id = UUID(uuidString: saved) {
            userID = id
        } else if let data = try? Data(contentsOf: appSupport.appendingPathComponent("Auth/users.json")),
                  let users = try? JSONDecoder().decode([LocalUser].self, from: data),
                  let first = users.first {
            userID = first.id
            UserDefaults.standard.set(first.id.uuidString, forKey: "kjUserID")
        } else {
            UserDefaults.standard.set(userID.uuidString, forKey: "kjUserID")
            isLoggedIn = false
        }
        qaAssistantEnabled = UserDefaults.standard.bool(forKey: "qaAssistantEnabled")
        saveText = UserDefaults.standard.object(forKey: "captureText") as? Bool ?? true
        saveImages = UserDefaults.standard.object(forKey: "captureImages") as? Bool ?? false
        saveURLs = UserDefaults.standard.object(forKey: "captureURLs") as? Bool ?? true
        saveRichText = UserDefaults.standard.object(forKey: "captureRichText") as? Bool ?? (UserDefaults.standard.object(forKey: "captureImages") != nil ? true : true)
        screenshotShortcut = UserDefaults.standard.string(forKey: "screenshotShortcut") ?? "⇧⌘4"
        showDesktopShortcutEnabled = UserDefaults.standard.object(forKey: "showDesktopShortcutEnabled") as? Bool ?? true
        showDesktopShortcutModifier = UserDefaults.standard.string(forKey: "showDesktopShortcutModifier") ?? "command"
        doubleClickCollapseEnabled = UserDefaults.standard.object(forKey: "doubleClickCollapseEnabled") as? Bool ?? true
        alwaysOnTop = UserDefaults.standard.object(forKey: "alwaysOnTop") as? Bool ?? false
        launchAtLogin = UserDefaults.standard.object(forKey: "launchAtLogin") as? Bool ?? false
        downloadDirectory = UserDefaults.standard.string(forKey: "downloadDirectory") ?? ""
        downloadBrowser = UserDefaults.standard.string(forKey: "downloadBrowser") ?? "none"
        qaAllowedApps = UserDefaults.standard.stringArray(forKey: "qaAllowedApps") ?? []
        if let mode = UserDefaults.standard.string(forKey: "qaMatchMode"),
           let value = QAMatchMode(rawValue: mode) {
            qaMatchMode = value
        }
        if let raw = UserDefaults.standard.string(forKey: "translationSourceLanguage"),
           let value = TranslationLanguage(rawValue: raw) {
            translationSourceLanguage = value
        }
        if let raw = UserDefaults.standard.string(forKey: "translationTargetLanguage"),
           let value = TranslationLanguage(rawValue: raw) {
            translationTargetLanguage = value
        }
        load()
        migrateLegacyShortcutsIfNeeded()
        installPasteboardMonitor()
        installGlobalShortcutMonitor()
        installLocalShortcutMonitor()
        installCalculatorKeyboardMonitor()
        DispatchQueue.main.async { self.applyWindowPreferences() }
        if !(savedToken?.isEmpty ?? true) {
            restoreCloudSession()
        }
    }

    deinit {
        pasteboardTimer?.invalidate()
        cloudSyncTimer?.invalidate()
        authCodeTimer?.invalidate()
        copyFeedbackTask?.cancel()
        if let globalMonitor { NSEvent.removeMonitor(globalMonitor) }
        if let localKeyboardMonitor { NSEvent.removeMonitor(localKeyboardMonitor) }
        if let localShortcutMonitor { NSEvent.removeMonitor(localShortcutMonitor) }
        if let shortcutRecordingMonitor { NSEvent.removeMonitor(shortcutRecordingMonitor) }
    }

    func load() {
        items = []
        qaEntries = []
        try? FileManager.default.createDirectory(at: userRoot, withIntermediateDirectories: true)
        let decoder = JSONDecoder()
        if let data = try? Data(contentsOf: itemsURL), let decoded = try? decoder.decode([ClipboardItem].self, from: data) {
            items = decoded
        } else if cloudAccountID == nil && !isLoggedIn {
            let legacy = appSupport.appendingPathComponent("clipboard-items.json")
            if let data = try? Data(contentsOf: legacy), let decoded = try? decoder.decode([ClipboardItem].self, from: data) {
                items = decoded
            }
        }
        if let data = try? Data(contentsOf: qaURL), let decoded = try? decoder.decode([QAEntry].self, from: data) {
            qaEntries = decoded
        } else if cloudAccountID == nil && !isLoggedIn {
            let legacy = appSupport.appendingPathComponent("qa-entries.json")
            if let data = try? Data(contentsOf: legacy), let decoded = try? decoder.decode([QAEntry].self, from: data) {
                qaEntries = decoded
            }
        }
        if let data = try? Data(contentsOf: shortcutsURL), let decoded = try? decoder.decode([ShortcutItem].self, from: data) {
            shortcuts = decoded
        }
    }

    func save() {
        let encoder = JSONEncoder()
        if let data = try? encoder.encode(items) { try? data.write(to: itemsURL, options: .atomic) }
        if let data = try? encoder.encode(qaEntries) { try? data.write(to: qaURL, options: .atomic) }
        if let data = try? encoder.encode(shortcuts) { try? data.write(to: shortcutsURL, options: .atomic) }
    }

    // MARK: Clipboard

    private func installPasteboardMonitor() {
        pasteboardTimer = Timer.scheduledTimer(withTimeInterval: 0.3, repeats: true) { [weak self] _ in
            Task { @MainActor [weak self] in self?.pollClipboard() }
        }
        cloudSyncTimer = Timer.scheduledTimer(withTimeInterval: 15, repeats: true) { [weak self] _ in
            Task { @MainActor [weak self] in
                guard let self, self.isLoggedIn else { return }
                self.syncCloudClipboard()
            }
        }
    }

    private func pollClipboard() {
        let count = pasteboard.changeCount
        guard count != lastChangeCount else { return }
        lastChangeCount = count
        // 未登录时只更新系统剪贴板计数，不把内容写入快剪历史。
        guard isLoggedIn, cloudAccountID != nil else { return }
        guard Date() >= ignoreUntil else { return }
        let source = clipboardSourceName()
        if let imageData = pasteboard.data(forType: .tiff) {
            guard saveImages else { return }
            try? FileManager.default.createDirectory(at: imagesRoot, withIntermediateDirectories: true)
            let imageURL = imagesRoot.appendingPathComponent("\(UUID().uuidString).tiff")
            if (try? imageData.write(to: imageURL, options: .atomic)) != nil {
                items.insert(ClipboardItem(title: "图片", text: "", kind: .image, source: source,
                                           imagePath: imageURL.path, imageDataBase64: imageData.base64EncodedString()), at: 0)
                items = Array(items.prefix(500))
                save()
                syncCloudClipboard()
            }
            return
        }
        guard let text = pasteboard.string(forType: .string)?.trimmingCharacters(in: .whitespacesAndNewlines), !text.isEmpty else { return }
        let kind: ClipKind = URL(string: text)?.scheme != nil ? .url : .text
        if kind == .text && !saveText { return }
        if kind == .url && !saveURLs { return }
        if kind == .text {
            routeCopiedText(text)
        }
        guard !items.contains(where: { $0.text == text && Date().timeIntervalSince($0.createdAt) < 0.8 }) else { return }
        let richTextData = saveRichText ? pasteboard.data(forType: .rtf)?.base64EncodedString() : nil
        items.insert(ClipboardItem(title: text, text: text, kind: kind, source: source, richTextBase64: richTextData), at: 0)
        items = Array(items.prefix(500))
        save()
        syncCloudClipboard()
    }

    private func clipboardSourceName() -> String {
        guard let app = NSWorkspace.shared.frontmostApplication else { return "系统剪贴板" }
        if app.bundleIdentifier == Bundle.main.bundleIdentifier { return "快剪" }
        return app.localizedName ?? app.bundleIdentifier ?? "系统剪贴板"
    }

    func copy(_ item: ClipboardItem) {
        ignoreUntil = Date().addingTimeInterval(0.6)
        pasteboard.clearContents()
        if item.kind == .image {
            if let path = item.imagePath, let image = NSImage(contentsOfFile: path) {
                pasteboard.writeObjects([image])
            } else if let encoded = item.imageDataBase64, let data = Data(base64Encoded: encoded), let image = NSImage(data: data) {
                pasteboard.writeObjects([image])
            }
        } else {
            pasteboard.setString(item.text, forType: .string)
            if let encoded = item.richTextBase64, let data = Data(base64Encoded: encoded) {
                pasteboard.setData(data, forType: .rtf)
            }
        }
        lastChangeCount = pasteboard.changeCount
        copiedItemID = item.id
        copyMessage = "已复制"
        copyFeedbackTask?.cancel()
        copyFeedbackTask = Task { @MainActor [weak self] in
            try? await Task.sleep(nanoseconds: 1_300_000_000)
            guard !Task.isCancelled else { return }
            self?.copiedItemID = nil
            self?.copyMessage = ""
        }
        if qaAssistantEnabled && item.kind == .text {
            setQAQuestion(item.text, autoCopy: true)
        }
    }

    func readClipboard() {
        guard let text = pasteboard.string(forType: .string)?.trimmingCharacters(in: .whitespacesAndNewlines), !text.isEmpty else {
            copyMessage = "剪贴板里没有可读取的文本"
            return
        }
        if selectedSection == .translation {
            setTranslationInput(text)
        } else {
            setQAQuestion(text, autoCopy: qaAssistantEnabled || selectedSection == .qa)
        }
        copyMessage = "已读取剪贴板"
        copyFeedbackTask?.cancel()
        copyFeedbackTask = Task { @MainActor [weak self] in
            try? await Task.sleep(nanoseconds: 1_500_000_000)
            guard !Task.isCancelled else { return }
            self?.copyMessage = ""
        }
    }

    func readClipboardForTranslation() {
        guard let text = pasteboard.string(forType: .string) else {
            translationMessage = "剪贴板里没有文本"
            return
        }
        setTranslationInput(text)
        translationMessage = "已读取并自动翻译"
    }

    private func routeCopiedText(_ text: String) {
        switch selectedSection {
        case .translation:
            setTranslationInput(text)
        case .qa:
            setQAQuestion(text, autoCopy: true)
        default:
            if qaAssistantEnabled && shouldAutoAnswerForCurrentApp() { setQAQuestion(text, autoCopy: true) }
        }
    }

    func setQAAllowedApp(_ app: String, enabled: Bool) {
        if enabled, !qaAllowedApps.contains(app) { qaAllowedApps.append(app) }
        if !enabled { qaAllowedApps.removeAll { $0 == app } }
    }

    private func shouldAutoAnswerForCurrentApp() -> Bool {
        qaAllowedApps.isEmpty || qaAllowedApps.contains(clipboardSourceName())
    }

    func setTranslationInput(_ text: String) {
        translationInput = text
        translate()
    }

    func toggleFavorite(_ item: ClipboardItem) {
        guard let index = items.firstIndex(where: { $0.id == item.id }) else { return }
        items[index].favorite.toggle()
        save()
    }

    func togglePinned(_ item: ClipboardItem) {
        guard let index = items.firstIndex(where: { $0.id == item.id }) else { return }
        items[index].pinned.toggle()
        save()
    }

    func orderedClipboardItems(_ source: [ClipboardItem]) -> [ClipboardItem] {
        source.enumerated()
            .sorted {
                if $0.element.pinned != $1.element.pinned {
                    return $0.element.pinned && !$1.element.pinned
                }
                return $0.offset < $1.offset
            }
            .map(\.element)
    }

    func delete(_ item: ClipboardItem) {
        items.removeAll { $0.id == item.id }
        save()
    }

    // MARK: Q&A

    func setQAQuestion(_ text: String, autoCopy: Bool = false) {
        qaQuestion = text
        updateQAAnswer()
        if autoCopy && !qaAnswer.isEmpty { copyAnswer() }
    }

    func updateQAAnswer() {
        let query = normalize(qaQuestion)
        guard !query.isEmpty else {
            qaAnswer = ""; qaMatchedQuestion = ""; qaMatchScore = 0; return
        }
        let enabled = qaEntries.filter(\.enabled)
        if let exact = enabled.first(where: { normalize($0.question) == query || $0.aliases.split(separator: "|").contains { normalize(String($0)) == query } }) {
            qaAnswer = exact.answer; qaMatchedQuestion = exact.question; qaMatchScore = 100; return
        }
        guard qaMatchMode == .fuzzy else {
            qaAnswer = ""; qaMatchedQuestion = ""; qaMatchScore = 0; return
        }
        let ranked = enabled.map { entry -> (QAEntry, Int) in
            let source = normalize(entry.question + " " + entry.aliases + " " + entry.keywords)
            let common = Set(query).intersection(Set(source)).count
            let containsBonus = source.contains(query) || query.contains(source) ? 35 : 0
            let score = min(99, common * 7 + containsBonus)
            return (entry, score)
        }.sorted { $0.1 > $1.1 }
        if let best = ranked.first, best.1 >= 18 {
            qaAnswer = best.0.answer; qaMatchedQuestion = best.0.question; qaMatchScore = best.1
        } else {
            qaAnswer = ""; qaMatchedQuestion = ""; qaMatchScore = 0
        }
    }

    func copyAnswer() {
        guard !qaAnswer.isEmpty else { return }
        ignoreUntil = Date().addingTimeInterval(0.8)
        pasteboard.clearContents()
        pasteboard.setString(qaAnswer, forType: .string)
        lastChangeCount = pasteboard.changeCount
        qaMessage = "答案已复制到剪贴板。"
    }

    func fillAnswerToTarget() {
        guard !qaAnswer.isEmpty else { return }
        copyAnswer()
        NSApp.hide(nil)
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) {
            guard let source = CGEventSource(stateID: .hidSystemState) else { return }
            let keyDown = CGEvent(keyboardEventSource: source, virtualKey: 9, keyDown: true)
            let keyUp = CGEvent(keyboardEventSource: source, virtualKey: 9, keyDown: false)
            keyDown?.flags = .maskCommand
            keyUp?.flags = .maskCommand
            keyDown?.post(tap: .cghidEventTap)
            keyUp?.post(tap: .cghidEventTap)
        }
    }

    func importQA(url: URL) {
        guard let data = try? Data(contentsOf: url) else { return }
        if let values = try? JSONDecoder().decode([QAEntry].self, from: data) {
            qaEntries = values; save(); return
        }
        if url.pathExtension.lowercased() == "xlsx", let rows = try? XLSXReader.rows(at: url) {
            let values = rows.dropFirst().compactMap { row -> QAEntry? in
                guard row.count >= 2, !row[0].trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
                let enabledText = row[safe: 6] ?? ""
                return QAEntry(question: row[safe: 0] ?? "", answer: row[safe: 1] ?? "", aliases: row[safe: 2] ?? "", keywords: row[safe: 3] ?? "", category: row[safe: 4] ?? "", note: row[safe: 5] ?? "", enabled: enabledText.isEmpty || enabledText == "是" || enabledText.lowercased() == "true")
            }
            if !values.isEmpty { qaEntries = values; save() }
            return
        }
        guard let text = String(data: data, encoding: .utf8) else { return }
        let rows = text.split(whereSeparator: \.isNewline)
        var imported: [QAEntry] = []
        for row in rows.dropFirst() {
            let columns = row.split(separator: ",", omittingEmptySubsequences: false).map(String.init)
            guard columns.count >= 2 else { continue }
            imported.append(QAEntry(question: columns[0], answer: columns[1],
                                    aliases: columns.count > 2 ? columns[2] : "",
                                    keywords: columns.count > 3 ? columns[3] : "",
                                    category: columns.count > 4 ? columns[4] : "",
                                    note: columns.count > 5 ? columns[5] : ""))
        }
        if !imported.isEmpty { qaEntries = imported; save() }
    }

    private func normalize(_ value: String) -> String {
        value.lowercased()
            .replacingOccurrences(of: " ", with: "")
            .replacingOccurrences(of: "\n", with: "")
            .replacingOccurrences(of: "，", with: ",")
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }

    // MARK: Translation

    func translate() {
        let source = translationInput.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !source.isEmpty else { translationMessage = "请输入要翻译的文字。"; return }
        let detected = detectLanguage(source)
        let sourceLanguage = translationSourceLanguage == .auto ? detected : translationSourceLanguage
        let targetLanguage: TranslationLanguage
        if translationTargetLanguage == .auto {
            targetLanguage = sourceLanguage == .chinese ? .english : .chinese
        } else {
            targetLanguage = translationTargetLanguage
        }
        translationDetectedSource = detected
        translationResolvedTarget = targetLanguage
        translationOutput = simpleTranslation(source, source: sourceLanguage, target: targetLanguage)
        translationMessage = "已完成翻译：\(sourceLanguage.title) → \(targetLanguage.title)"
    }

    private func detectLanguage(_ text: String) -> TranslationLanguage {
        if text.unicodeScalars.contains(where: { (0xAC00...0xD7AF).contains($0.value) }) { return .korean }
        if text.unicodeScalars.contains(where: { (0x3040...0x30FF).contains($0.value) }) { return .japanese }
        if text.unicodeScalars.contains(where: { (0x4E00...0x9FFF).contains($0.value) }) { return .chinese }
        return .english
    }

    private func simpleTranslation(_ text: String, source: TranslationLanguage, target: TranslationLanguage) -> String {
        guard source != target else { return text }
        let dictionary: [String: String]
        if source == .chinese && target == .english {
            dictionary = ["你好": "Hello", "谢谢": "Thank you", "退款": "Refund", "订单": "Order", "客户": "Customer", "价格": "Price", "怎么申请退款": "How do I request a refund?", "体重过轻": "Underweight", "体重超重": "Overweight", "计算机图标": "Calculator icon", "计算机": "Calculator", "图标": "Icon"]
        } else if source == .english && target == .chinese {
            dictionary = ["hello": "你好", "thank you": "谢谢", "refund": "退款", "order": "订单", "customer": "客户", "price": "价格", "how do i request a refund?": "怎么申请退款？", "underweight": "体重过轻", "overweight": "体重超重"]
        } else {
            dictionary = [:]
        }
        if let exact = dictionary[text.lowercased()] { return exact }
        return text
    }

    func copyTranslation() {
        guard !translationOutput.isEmpty else { return }
        ignoreUntil = Date().addingTimeInterval(0.8)
        pasteboard.clearContents(); pasteboard.setString(translationOutput, forType: .string)
        lastChangeCount = pasteboard.changeCount
        translationMessage = "译文已复制到剪贴板。"
    }

    // MARK: Calculator

    func calculatorInput(_ token: String) {
        if token == "C" || token == "AC" { calculatorDisplay = "0"; calculatorExpression = ""; calculatorValue = nil; calculatorOperator = nil; calculatorStartsNewEntry = true; return }
        if token == "⌫" {
            if !calculatorStartsNewEntry && calculatorDisplay.count > 1 {
                calculatorDisplay.removeLast()
            } else {
                calculatorDisplay = "0"
            }
            return
        }
        if token == "%" {
            let value = (Double(calculatorDisplay) ?? 0) / 100
            calculatorDisplay = format(value)
            calculatorStartsNewEntry = false
            return
        }
        if token == "±" {
            if calculatorDisplay != "0" {
                calculatorDisplay = calculatorDisplay.hasPrefix("-") ? String(calculatorDisplay.dropFirst()) : "-" + calculatorDisplay
            }
            return
        }
        if ["+", "-", "×", "÷"].contains(token) {
            calculatorValue = Double(calculatorDisplay) ?? 0
            calculatorOperator = token
            calculatorExpression = "\(calculatorDisplay) \(token)"
            calculatorStartsNewEntry = true
            return
        }
        if token == "=" {
            guard let value = calculatorValue, let op = calculatorOperator, let right = Double(calculatorDisplay) else { return }
            let result: Double
            switch op { case "+": result = value + right; case "-": result = value - right; case "×": result = value * right; default: result = right == 0 ? 0 : value / right }
            calculatorHistory.insert("\(format(value)) \(op) \(format(right)) = \(format(result))", at: 0)
            calculatorHistory = Array(calculatorHistory.prefix(50))
            calculatorDisplay = format(result); calculatorExpression = ""; calculatorValue = nil; calculatorOperator = nil; calculatorStartsNewEntry = true; return
        }
        if calculatorStartsNewEntry { calculatorDisplay = token == "." ? "0." : token; calculatorStartsNewEntry = false }
        else if token == "." && calculatorDisplay.contains(".") { return }
        else { calculatorDisplay = calculatorDisplay == "0" ? token : calculatorDisplay + token }
    }

    func clearCalculatorHistory() { calculatorHistory.removeAll() }

    private func format(_ value: Double) -> String {
        if value.rounded() == value { return String(Int(value)) }
        return String(format: "%.8f", value).replacingOccurrences(of: "0+$", with: "", options: .regularExpression)
    }

    private func installCalculatorKeyboardMonitor() {
        localKeyboardMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self, self.selectedSection == .calculator else { return event }
            let raw = event.charactersIgnoringModifiers ?? ""
            let token: String?
            switch raw {
            case "0"..."9": token = raw
            case ".", ",": token = "."
            case "+": token = "+"
            case "-": token = "-"
            case "*": token = "×"
            case "/": token = "÷"
            case "%": token = "%"
            case "=", "\r", "\n": token = "="
            case "\u{8}", "\u{7F}": token = "⌫"
            case "c", "C": token = "AC"
            default: token = nil
            }
            guard let token else { return event }
            self.calculatorInput(token)
            return nil
        }
    }

    // MARK: System actions

    func takeSystemScreenshot() {
        let task = Process()
        task.launchPath = "/usr/sbin/screencapture"
        task.arguments = ["-i", "-c"]
        try? task.run()
    }

    func saveSettings() {
        settingsMessage = "设置已保存"
    }

    func importLegacyHistory() {
        let legacyURL = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("KuaiJian/qa/entries-v1.json")
        guard let data = try? Data(contentsOf: legacyURL),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let entriesData = try? JSONSerialization.data(withJSONObject: object["entries"] ?? []),
              let imported = try? JSONDecoder().decode([QAEntry].self, from: entriesData), !imported.isEmpty else {
            settingsMessage = "没有找到可导入的旧版历史"
            return
        }
        qaEntries = imported
        save()
        settingsMessage = "已导入 (imported.count) 条旧版问答"
    }

    func setLaunchAtLogin(_ enabled: Bool) {
        do {
            if enabled { try SMAppService.mainApp.register() }
            else { try SMAppService.mainApp.unregister() }
            launchAtLogin = enabled
            settingsMessage = enabled ? "已开启开机启动" : "已关闭开机启动"
        } catch {
            settingsMessage = "开机启动设置失败，请在系统设置中允许"
        }
    }

    func applyWindowPreferences() {
        for window in NSApp.windows where window.title == "快剪" {
            window.level = alwaysOnTop ? .floating : .normal
        }
    }

    func showAppOnDesktop() {
        isSidebarCollapsed = false
        NSApp.unhide(nil)
        NSApp.activate(ignoringOtherApps: true)
        for window in NSApp.windows where window.canBecomeMain || window.title == "快剪" {
            if window.isMiniaturized {
                window.deminiaturize(nil)
            }
            window.orderFrontRegardless()
            window.makeKeyAndOrderFront(nil)
        }
    }

    func toggleAppOnDesktop() {
        let appWindows = NSApp.windows.filter { $0.canBecomeMain || $0.title == "快剪" }
        if NSApp.isActive,
           let visibleWindow = appWindows.first(where: { $0.isVisible && !$0.isMiniaturized }) {
            visibleWindow.miniaturize(nil)
            return
        }
        showAppOnDesktop()
    }

    var showDesktopShortcutTitle: String {
        "双击 \(showDesktopShortcutModifierTitle)"
    }

    var showDesktopShortcutModifierTitle: String {
        showDesktopShortcutModifierTitle(for: showDesktopShortcutModifier)
    }

    func beginShowDesktopShortcutRecording() {
        if isRecordingShowDesktopShortcut {
            cancelShowDesktopShortcutRecording()
            return
        }
        isRecordingShowDesktopShortcut = true
        lastRecordedModifier = nil
        lastDesktopModifierTapAt = .distantPast
        showDesktopShortcutRecordingMessage = "请连续按两次同一个修饰键"
        shortcutRecordingMonitor = NSEvent.addLocalMonitorForEvents(matching: .flagsChanged) { [weak self] event in
            guard let self else { return event }
            if self.isRecordingShowDesktopShortcut {
                self.handleShowDesktopShortcutRecording(event)
                return nil
            }
            return event
        }
    }

    func cancelShowDesktopShortcutRecording() {
        if let shortcutRecordingMonitor { NSEvent.removeMonitor(shortcutRecordingMonitor) }
        shortcutRecordingMonitor = nil
        isRecordingShowDesktopShortcut = false
        lastRecordedModifier = nil
        lastDesktopModifierTapAt = .distantPast
        showDesktopShortcutRecordingMessage = ""
    }

    func resetShowDesktopShortcut() {
        cancelShowDesktopShortcutRecording()
        showDesktopShortcutModifier = "command"
    }

    private func handleShowDesktopShortcutRecording(_ event: NSEvent) {
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        guard let modifier = desktopModifier(for: event.keyCode),
              flags.contains(desktopModifierFlags(for: modifier)) else {
            return
        }
        guard !event.isARepeat else {
            showDesktopShortcutRecordingMessage = "请按 Command、Option、Control 或 Shift 键"
            return
        }
        let now = Date()
        if modifier == lastRecordedModifier && now.timeIntervalSince(lastDesktopModifierTapAt) <= 0.5 {
            showDesktopShortcutModifier = modifier
            cancelShowDesktopShortcutRecording()
            settingsMessage = "快速显示到桌面的快捷键已更新为 \(showDesktopShortcutTitle)"
        } else {
            lastRecordedModifier = modifier
            lastDesktopModifierTapAt = now
            showDesktopShortcutRecordingMessage = "已记录一次，再按一次 \(showDesktopShortcutModifierTitle(for: modifier))"
        }
    }

    private func desktopModifier(for keyCode: UInt16) -> String? {
        switch keyCode {
        case 54, 55: return "command"
        case 58, 61: return "option"
        case 59, 62: return "control"
        case 56, 60: return "shift"
        default: return nil
        }
    }

    private func showDesktopShortcutModifierTitle(for modifier: String) -> String {
        switch modifier {
        case "option": return "Option ⌥"
        case "control": return "Control ⌃"
        case "shift": return "Shift ⇧"
        default: return "Command ⌘"
        }
    }

    private func desktopModifierFlags(for modifier: String) -> NSEvent.ModifierFlags {
        switch modifier {
        case "option": return .option
        case "control": return .control
        case "shift": return .shift
        default: return .command
        }
    }

    private func configuredDesktopModifierFlags() -> NSEvent.ModifierFlags {
        desktopModifierFlags(for: showDesktopShortcutModifier)
    }

    func openSystemSettings(_ path: String) {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?\(path)") {
            NSWorkspace.shared.open(url)
        }
    }

    func chooseApplication() -> URL? {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.application]
        panel.allowsMultipleSelection = false
        return panel.runModal() == .OK ? panel.url : nil
    }

    private func installGlobalShortcutMonitor() {
        globalMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.keyDown, .flagsChanged]) { [weak self] event in
            Task { @MainActor [weak self] in
                self?.handleGlobalShortcutEvent(event)
            }
        }
    }

    private func installLocalShortcutMonitor() {
        localShortcutMonitor = NSEvent.addLocalMonitorForEvents(matching: [.keyDown, .flagsChanged]) { [weak self] event in
            Task { @MainActor [weak self] in
                self?.handleGlobalShortcutEvent(event)
            }
            return event
        }
    }

    private func handleGlobalShortcutEvent(_ event: NSEvent) {
        if isRecordingShowDesktopShortcut { return }
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        let isDesktopModifierKey = desktopModifier(for: event.keyCode) == showDesktopShortcutModifier
        let configuredModifier = configuredDesktopModifierFlags()
        let allStandardModifiers: NSEvent.ModifierFlags = [.command, .option, .control, .shift]
        let hasOnlyConfiguredModifier = flags.contains(configuredModifier)
            && flags.subtracting(configuredModifier).intersection(allStandardModifiers).isEmpty

        if showDesktopShortcutEnabled && event.type == .flagsChanged && isDesktopModifierKey
            && flags.contains(configuredModifier) && hasOnlyConfiguredModifier {
            let now = Date()
            if now.timeIntervalSince(lastDesktopModifierTapAt) <= 0.4 {
                lastDesktopModifierTapAt = .distantPast
                toggleAppOnDesktop()
            } else {
                lastDesktopModifierTapAt = now
            }
            return
        }

        if event.type == .flagsChanged { return }

        // Do not let a modifier-plus-key shortcut count as a double modifier tap.
        if !isDesktopModifierKey && !flags.intersection(allStandardModifiers).isEmpty {
            lastDesktopModifierTapAt = .distantPast
        }

        let isScreenshot = flags.contains(.command) && flags.contains(.shift) && event.charactersIgnoringModifiers == "4"
        if isScreenshot { takeSystemScreenshot(); return }
        guard flags.contains(.command) || flags.contains(.option) || flags.contains(.control) || flags.contains(.shift) else { return }
        let key = event.charactersIgnoringModifiers?.uppercased() ?? ""
        if hotkeyMatches(screenshotShortcut, key: key, flags: flags) {
            takeSystemScreenshot(); return
        }
        if let item = shortcuts.first(where: { $0.enabled && hotkeyMatches($0.hotkey, key: key, flags: flags) }) {
            openShortcut(item)
        }
    }

    private func hotkeyMatches(_ hotkey: String, key: String, flags: NSEvent.ModifierFlags) -> Bool {
        let value = hotkey.replacingOccurrences(of: " ", with: "").uppercased()
        let wantsCommand = value.contains("⌘") || value.contains("CMD") || value.contains("COMMAND")
        let wantsOption = value.contains("⌥") || value.contains("ALT") || value.contains("OPTION")
        let wantsControl = value.contains("⌃") || value.contains("CTRL") || value.contains("CONTROL")
        let wantsShift = value.contains("⇧") || value.contains("SHIFT")
        guard wantsCommand == flags.contains(.command), wantsOption == flags.contains(.option), wantsControl == flags.contains(.control), wantsShift == flags.contains(.shift) else { return false }
        if let fIndex = value.lastIndex(of: "F"), value[fIndex...].dropFirst().allSatisfy({ $0.isNumber }) {
            return key == String(value[fIndex...])
        }
        return value.hasSuffix(key)
    }

    func addShortcut(_ item: ShortcutItem) {
        shortcuts.append(item)
        save()
    }

    private func migrateLegacyShortcutsIfNeeded() {
        guard shortcuts.isEmpty, let data = UserDefaults.standard.data(forKey: "quickLaunchItems"),
              let legacy = try? JSONDecoder().decode([LegacyShortcutItem].self, from: data), !legacy.isEmpty else { return }
        shortcuts = legacy.map {
            ShortcutItem(id: $0.id ?? UUID(), name: $0.name, target: $0.target,
                         hotkey: legacyHotkey(keyCode: $0.keyCode, modifiers: $0.modifiers),
                         enabled: $0.enabled, kind: $0.kind == "application" ? "app" : $0.kind)
        }
        save()
    }

    private func legacyHotkey(keyCode: Int?, modifiers: UInt?) -> String {
        let keys = [18: "1", 19: "2", 20: "3", 21: "4", 23: "5", 22: "6", 26: "7", 28: "8", 25: "9", 29: "0", 0: "A", 1: "S", 2: "D", 3: "F", 12: "Q", 13: "R", 17: "T"]
        var result = ""
        let flags = NSEvent.ModifierFlags(rawValue: modifiers ?? 0)
        if flags.contains(.control) { result += "⌃" }
        if flags.contains(.option) { result += "⌥" }
        if flags.contains(.shift) { result += "⇧" }
        if flags.contains(.command) { result += "⌘" }
        return result + (keys[keyCode ?? -1] ?? "")
    }

    func deleteShortcut(_ item: ShortcutItem) {
        shortcuts.removeAll { $0.id == item.id }
        save()
    }

    func openShortcut(_ item: ShortcutItem) {
        let url: URL?
        if item.kind == "app" {
            url = URL(fileURLWithPath: item.target)
        } else {
            url = URL(string: item.target)
        }
        guard let url else { return }
        NSWorkspace.shared.open(url)
    }

    private func establishServerSession(from data: Data) -> Bool {
        guard let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let token = object["token"] as? String,
              !token.isEmpty,
              let user = object["user"] as? [String: Any],
              let rawAccountID = user["id"] else { return false }
        let accountID = String(describing: rawAccountID).trimmingCharacters(in: .whitespacesAndNewlines)
        guard !accountID.isEmpty else { return false }
        UserDefaults.standard.set(token, forKey: "kjAccessToken")
        isLoggedIn = true
        activateCloudAccount(accountID)
        return true
    }

    private func activateCloudAccount(_ accountID: String) {
        let normalized = accountID.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !normalized.isEmpty else { return }
        let changed = cloudAccountID != normalized
        cloudAccountID = normalized
        UserDefaults.standard.set(normalized, forKey: "kjCloudAccountID")
        if changed {
            // 切换账号时先清空内存，再从新账号目录加载，避免上一账号内容短暂可见。
            items = []
            qaEntries = []
        }
        load()
    }

    private func restoreCloudSession() {
        guard let token = UserDefaults.standard.string(forKey: "kjAccessToken"), !token.isEmpty,
              let url = URL(string: "https://api.kuaijian.jingronghao.com/v1/auth/me") else { return }
        var request = URLRequest(url: url)
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        Task { @MainActor in
            do {
                let (data, response) = try await URLSession.shared.data(for: request)
                guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode),
                      let object = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                      let user = object["user"] as? [String: Any],
                      let rawAccountID = user["id"] else { throw URLError(.userAuthenticationRequired) }
                let accountID = String(describing: rawAccountID)
                guard !accountID.isEmpty else { throw URLError(.userAuthenticationRequired) }
                isLoggedIn = true
                activateCloudAccount(accountID)
                fetchDevices()
                syncCloudClipboard()
            } catch {
                // 会话失效时不保留旧账号内容，也不把本地旧文件回退到新会话。
                UserDefaults.standard.removeObject(forKey: "kjAccessToken")
                UserDefaults.standard.removeObject(forKey: "kjCloudAccountID")
                cloudAccountID = nil
                isLoggedIn = false
                items = []
                qaEntries = []
                devices = []
            }
        }
    }

    func logoutFromServer() {
        let token = UserDefaults.standard.string(forKey: "kjAccessToken")
        if let token, let url = URL(string: "https://api.kuaijian.jingronghao.com/v1/auth/logout") {
            var request = URLRequest(url: url)
            request.httpMethod = "POST"
            request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.httpBody = Data("{}".utf8)
            Task { _ = try? await URLSession.shared.data(for: request) }
        }
        UserDefaults.standard.removeObject(forKey: "kjAccessToken")
        UserDefaults.standard.removeObject(forKey: "kjCloudAccountID")
        cloudAccountID = nil
        isLoggedIn = false
        items = []
        qaEntries = []
        devices = []
        deviceMessage = ""
        cloudMessage = ""
        transferMessage = ""
        loginMessage = ""
        authCodeMessage = ""
    }

    func loginToServer() {
        let phone = loginPhone.trimmingCharacters(in: .whitespacesAndNewlines)
        if loginMethod == "sms" {
            authCodeMessage = ""
            guard !phone.isEmpty, !registerCode.isEmpty else {
                loginMessage = "请输入手机号和验证码"
                return
            }
        } else {
            // 密码登录是独立渠道：不读取、不校验短信验证码，也不保留短信发送错误。
            authCodeMessage = ""
            guard !phone.isEmpty, !loginPassword.isEmpty else {
                loginMessage = "请输入手机号和密码"
                return
            }
        }
        loginMessage = "正在登录…"
        Task { @MainActor in
            do {
                let endpoint = URL(string: "https://api.kuaijian.jingronghao.com/v1/auth/login")!
                var request = URLRequest(url: endpoint)
                request.httpMethod = "POST"
                request.setValue("application/json", forHTTPHeaderField: "Content-Type")
                var body: [String: String] = ["phone": phone]
                if loginMethod == "sms" {
                    body["smsCode"] = registerCode
                } else {
                    body["password"] = loginPassword
                }
                request.httpBody = try JSONSerialization.data(withJSONObject: body)
                let (data, response) = try await URLSession.shared.data(for: request)
                guard let http = response as? HTTPURLResponse else { throw URLError(.badServerResponse) }
                if (200..<300).contains(http.statusCode) {
                    guard establishServerSession(from: data) else {
                        loginMessage = "登录响应无效，请稍后重试"
                        return
                    }
                    loginMessage = "登录成功"
                    fetchDevices()
                    syncCloudClipboard()
                } else {
                    loginMessage = "登录失败，请检查手机号和登录凭证"
                }
            } catch {
                loginMessage = "云端暂时不可用，请稍后重试"
            }
        }
    }

    func registerToServer() {
        let phone = loginPhone.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !phone.isEmpty, !loginPassword.isEmpty, !registerCode.isEmpty else {
            loginMessage = "请输入手机号、验证码和密码"
            return
        }
        guard loginPassword == confirmPassword else {
            loginMessage = "两次输入的密码不一致"
            return
        }
        loginMessage = "正在注册…"
        Task { @MainActor in
            do {
                let endpoint = URL(string: "https://api.kuaijian.jingronghao.com/v1/auth/register")!
                var request = URLRequest(url: endpoint)
                request.httpMethod = "POST"
                request.setValue("application/json", forHTTPHeaderField: "Content-Type")
                var body: [String: String] = ["phone": phone, "password": loginPassword, "smsCode": registerCode]
                if !loginEmail.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    body["email"] = loginEmail.trimmingCharacters(in: .whitespacesAndNewlines)
                }
                request.httpBody = try JSONSerialization.data(withJSONObject: body)
                let (data, response) = try await URLSession.shared.data(for: request)
                guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
                    loginMessage = "注册失败，手机号可能已注册或验证码无效"
                    return
                }
                guard establishServerSession(from: data) else {
                    loginMessage = "注册响应无效，请稍后重试"
                    return
                }
                loginMessage = "注册成功"
                fetchDevices()
                syncCloudClipboard()
            } catch {
                loginMessage = "云端暂时不可用，请稍后重试"
            }
        }
    }

    func sendAuthCode() {
        let phone = loginPhone.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !phone.isEmpty else { authCodeMessage = "请先输入手机号"; return }
        guard authCodeCountdown == 0 else { return }
        let path = authMode == "register" ? "/v1/auth/register/send-code" : "/v1/auth/login/send-code"
        guard let url = URL(string: "https://api.kuaijian.jingronghao.com\(path)") else { return }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try? JSONSerialization.data(withJSONObject: ["phone": phone])
        authCodeMessage = "正在发送验证码…"
        Task { @MainActor in
            do {
                let (data, response) = try await URLSession.shared.data(for: request)
                guard let http = response as? HTTPURLResponse else { throw URLError(.badServerResponse) }
                if (200..<300).contains(http.statusCode) {
                    authCodeMessage = "验证码已发送，请查收短信"
                    startAuthCodeCountdown()
                } else if let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any], let error = object["error"] as? String {
                    authCodeMessage = error == "sms_cooldown" ? "验证码发送过于频繁，请稍后再试" : "验证码发送失败：\(error)"
                } else {
                    authCodeMessage = "验证码发送失败"
                }
            } catch {
                authCodeMessage = "云端暂时不可用，请稍后重试"
            }
        }
    }

    var authCodeButtonTitle: String {
        authCodeCountdown > 0 ? "\(authCodeCountdown)秒后重试" : "获取验证码"
    }

    private func startAuthCodeCountdown() {
        authCodeTimer?.invalidate()
        authCodeCountdown = 60
        authCodeTimer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] timer in
            Task { @MainActor [weak self] in
                guard let self else { timer.invalidate(); return }
                if self.authCodeCountdown <= 1 {
                    self.authCodeCountdown = 0
                    timer.invalidate()
                    self.authCodeTimer = nil
                } else {
                    self.authCodeCountdown -= 1
                }
            }
        }
    }

    func startDownload() {
        let source = downloadURL.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let url = URL(string: source), url.scheme != nil else {
            downloadMessage = "请输入有效下载地址"
            return
        }
        let tool = Bundle.main.url(forResource: "yt-dlp_macos", withExtension: nil)?.path ?? "/usr/local/bin/yt-dlp"
        guard FileManager.default.isExecutableFile(atPath: tool) || FileManager.default.isExecutableFile(atPath: "/usr/local/bin/yt-dlp") else {
            downloadMessage = "未找到 yt-dlp 下载组件"
            return
        }
        let directory = downloadDirectory.isEmpty ? FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask)[0].path : downloadDirectory
        try? FileManager.default.createDirectory(atPath: directory, withIntermediateDirectories: true)
        let process = Process()
        process.executableURL = URL(fileURLWithPath: tool)
        var arguments = ["-P", directory]
        if downloadBrowser != "none" { arguments += ["--cookies-from-browser", downloadBrowser] }
        process.arguments = arguments + [source]
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = pipe
        do {
            try process.run()
            downloadMessage = "正在下载…"
            process.terminationHandler = { [weak self] task in
                Task { @MainActor [weak self] in
                    self?.downloadMessage = task.terminationStatus == 0 ? "下载完成：\(directory)" : "下载失败，请检查地址或网络"
                }
            }
        } catch {
            downloadMessage = "无法启动下载组件"
        }
    }

    func fetchDevices() {
        guard isLoggedIn, cloudAccountID != nil,
              let token = UserDefaults.standard.string(forKey: "kjAccessToken"), !token.isEmpty,
              let url = URL(string: "https://api.kuaijian.jingronghao.com/v1/devices") else {
            deviceMessage = "登录云端后可查看设备"
            return
        }
        var request = URLRequest(url: url)
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        Task { @MainActor in
            do {
                let (data, response) = try await URLSession.shared.data(for: request)
                guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else { throw URLError(.badServerResponse) }
                let decoded = try JSONDecoder().decode(DeviceListEnvelope.self, from: data)
                devices = decoded.devices
                deviceMessage = devices.isEmpty ? "暂无其他设备" : "已加载 \(devices.count) 台设备"
            } catch {
                deviceMessage = "设备列表读取失败，请稍后重试"
            }
        }
    }

    func sendClipboardToDevice(_ device: DeviceItem) {
        guard isLoggedIn, cloudAccountID != nil,
              let token = UserDefaults.standard.string(forKey: "kjAccessToken"), !token.isEmpty,
              let url = URL(string: "https://api.kuaijian.jingronghao.com/v1/transfers") else {
            transferMessage = "请先登录云端"
            return
        }
        let text = pasteboard.string(forType: .string) ?? ""
        guard !text.isEmpty else { transferMessage = "当前剪贴板没有文本"; return }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try? JSONSerialization.data(withJSONObject: ["targetDeviceId": device.id, "kind": "text", "text": text])
        transferMessage = "正在发送…"
        Task { @MainActor in
            do {
                let (_, response) = try await URLSession.shared.data(for: request)
                guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else { throw URLError(.badServerResponse) }
                transferMessage = "已发送到 \(device.name)"
            } catch {
                transferMessage = "发送失败，请检查设备是否在线"
            }
        }
    }

    private func hydrateRemoteImages(_ remote: [ClipboardItem]) -> [ClipboardItem] {
        remote.map { item in
            guard item.kind == .image,
                  let encoded = item.imageDataBase64,
                  let data = Data(base64Encoded: encoded) else { return item }
            if let path = item.imagePath, FileManager.default.fileExists(atPath: path) { return item }
            try? FileManager.default.createDirectory(at: imagesRoot, withIntermediateDirectories: true)
            let path = imagesRoot.appendingPathComponent("\(item.id.uuidString).tiff").path
            guard (try? data.write(to: URL(fileURLWithPath: path), options: .atomic)) != nil else { return item }
            var hydrated = item
            hydrated.imagePath = path
            return hydrated
        }
    }

    func syncCloudClipboard() {
        guard isLoggedIn, cloudAccountID != nil,
              let token = UserDefaults.standard.string(forKey: "kjAccessToken"), !token.isEmpty,
              let url = URL(string: "https://api.kuaijian.jingronghao.com/v1/clipboard") else {
            cloudMessage = "请先登录云端"
            return
        }
        var request = URLRequest(url: url)
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        cloudMessage = "正在同步云端剪贴板…"
        Task { @MainActor in
            do {
                let (data, response) = try await URLSession.shared.data(for: request)
                guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else { throw URLError(.badServerResponse) }
                let remote = hydrateRemoteImages(try JSONDecoder().decode(ClipboardEnvelope.self, from: data).items)
                var merged = remote
                let remoteIDs = Set(remote.map(\.id))
                merged.append(contentsOf: items.filter { !remoteIDs.contains($0.id) })
                items = merged.sorted { $0.createdAt > $1.createdAt }
                save()
                var upload = URLRequest(url: url)
                upload.httpMethod = "PUT"
                upload.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
                upload.setValue("application/json", forHTTPHeaderField: "Content-Type")
                let uploadItems = items.map { item -> ClipboardItem in
                    var sanitized = item
                    if sanitized.kind == .image { sanitized.imagePath = nil }
                    return sanitized
                }
                upload.httpBody = try JSONEncoder().encode(ClipboardUploadEnvelope(items: uploadItems))
                let (_, uploadResponse) = try await URLSession.shared.data(for: upload)
                guard let uploadHTTP = uploadResponse as? HTTPURLResponse,
                      (200..<300).contains(uploadHTTP.statusCode) else { throw URLError(.cannotWriteToFile) }
                cloudMessage = "已同步云端 \(items.count) 条"
            } catch {
                cloudMessage = "云端剪贴板同步失败，请检查登录状态"
            }
        }
    }

    func appendQAEntry() {
        qaEntries.insert(QAEntry(question: "新问题", answer: "新答案"), at: 0)
        save()
    }

    func deleteQAEntry(_ entry: QAEntry) {
        qaEntries.removeAll { $0.id == entry.id }
        save()
        updateQAAnswer()
    }
}

// MARK: - App entry

@MainActor
final class UpdateManager: NSObject, ObservableObject {
    private let controller: SPUStandardUpdaterController

    override init() {
        controller = SPUStandardUpdaterController(startingUpdater: true, updaterDelegate: nil, userDriverDelegate: nil)
        super.init()
    }

    func checkForUpdates() {
        controller.updater.checkForUpdates()
    }
}

@main
struct KuaiJianRebuiltApp: App {
    @StateObject private var model = AppModel()
    @StateObject private var updater = UpdateManager()
    var body: some Scene {
        WindowGroup("快剪") {
            RootView()
                .environmentObject(model)
                .environmentObject(updater)
                .frame(minWidth: 420, minHeight: 640)
        }
        .commands {
            CommandGroup(replacing: .appInfo) {
                Button("关于快剪") { NSApp.orderFrontStandardAboutPanel(nil) }
            }
            CommandMenu("快剪") {
                Button("系统区域截图") { model.takeSystemScreenshot() }
                    .keyboardShortcut("4", modifiers: [.command, .shift])
            }
        }
    }
}

