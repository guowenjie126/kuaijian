import SwiftUI
import AppKit
import UniformTypeIdentifiers

// MARK: - Shared UI

private enum KJLayout {
    // 按用户指定的紧凑版尺寸统一主窗口布局。
    static let sidebarWidth: CGFloat = 65
    static let navItemWidth: CGFloat = 60
    static let navItemHeight: CGFloat = 54
    static let pageColumnMaxWidth: CGFloat = 400
    static let windowWidth: CGFloat = sidebarWidth + pageColumnMaxWidth + 1
    // 主窗口按用户给出的 920:2060 竖向比例计算，保留左 65pt + 右 450pt 的宽度约定。
    static let windowHeight: CGFloat = windowWidth * 2060 / 920
    // 登录和注册共用一套外框，切换时只替换表单内容，避免窗口高度跳变。
    static let authWindowWidth: CGFloat = 420
    static let authWindowHeight: CGFloat = 900
}

private struct KJWindowSizeConfigurator: NSViewRepresentable {
    let contentWidth: CGFloat
    let contentHeight: CGFloat
    let resizeWindow: Bool

    final class Coordinator {
        var applied = false
        var lastAuthSize: CGSize?
    }

    func makeCoordinator() -> Coordinator { Coordinator() }

    func makeNSView(context: Context) -> NSView {
        let view = NSView()
        DispatchQueue.main.async { apply(to: view.window, coordinator: context.coordinator) }
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {
        guard resizeWindow || !context.coordinator.applied else { return }
        DispatchQueue.main.async { apply(to: nsView.window, coordinator: context.coordinator) }
    }

    private func apply(to window: NSWindow?, coordinator: Coordinator) {
        guard let window else { return }
        if resizeWindow {
            let target = CGSize(width: contentWidth, height: contentHeight)
            window.standardWindowButton(.zoomButton)?.isHidden = true
            window.standardWindowButton(.zoomButton)?.isEnabled = false
            window.minSize = NSSize(width: contentWidth, height: contentHeight)
            window.maxSize = NSSize(width: contentWidth, height: contentHeight)
            if coordinator.lastAuthSize != target {
                window.setContentSize(NSSize(width: contentWidth, height: contentHeight))
                window.center()
                coordinator.lastAuthSize = target
            }
            return
        }
        window.standardWindowButton(.zoomButton)?.isHidden = false
        window.standardWindowButton(.zoomButton)?.isEnabled = true
        window.minSize = NSSize(width: contentWidth, height: contentHeight)
        window.maxSize = NSSize(width: 4096, height: 4096)
        coordinator.applied = true
        let currentContentSize = window.contentRect(forFrameRect: window.frame).size
        if currentContentSize.width < contentWidth || currentContentSize.height < contentHeight {
            window.setContentSize(NSSize(width: max(currentContentSize.width, contentWidth), height: max(currentContentSize.height, contentHeight)))
            return
        }
        guard window.frame.width > contentWidth else { return }
        var frame = window.frame
        let widthDelta = frame.width - contentWidth
        frame.size.width = contentWidth
        frame.origin.x += widthDelta / 2
        window.setFrame(frame, display: true, animate: false)
    }
}

struct RootView: View {
    @EnvironmentObject private var model: AppModel
    @State private var showingSettings = false

    var body: some View {
        Group {
            if model.isLoggedIn {
                HStack(spacing: 0) {
                    if model.isSidebarCollapsed {
                        Button { model.isSidebarCollapsed = false } label: {
                            Image(systemName: "chevron.right")
                                .foregroundStyle(Color.kjMuted)
                                .frame(width: 24, height: 48)
                        }
                        .buttonStyle(.plain)
                    } else {
                        SidebarView(showingSettings: $showingSettings)
                    }
                    Divider()
                    Group {
                        switch model.selectedSection {
                        case .clipboard: ClipboardPage()
                        case .team: TeamPage()
                        case .qa: QuestionAnswersPage()
                        case .calculator: CalculatorPage()
                        case .downloads: DownloadsPage()
                        case .shortcuts: ShortcutsPage()
                        case .translation: TranslationPage()
                        case .connections: ConnectionsPage()
                        case .transfer: TransferPage()
                        case .favorites: FavoritesPage()
                        }
                    }
                    .frame(maxWidth: KJLayout.pageColumnMaxWidth, maxHeight: .infinity, alignment: .topLeading)
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                    .background(Color.kjCanvas)
                }
            } else {
                AuthPage()
            }
        }
        .background(KJWindowSizeConfigurator(contentWidth: model.isLoggedIn ? KJLayout.windowWidth : KJLayout.authWindowWidth, contentHeight: model.isLoggedIn ? KJLayout.windowHeight : KJLayout.authWindowHeight, resizeWindow: !model.isLoggedIn))
        .sheet(isPresented: $showingSettings) { SettingsPage() }
    }
}

struct AuthPage: View {
    @EnvironmentObject private var model: AppModel
    @FocusState private var focusedField: AuthFieldKey?
    @State private var passwordVisible = false
    @State private var confirmPasswordVisible = false

    private var isRegister: Bool { model.authMode == "register" }
    private let pageHeight = KJLayout.authWindowHeight

    var body: some View {
        ZStack {
            AuthBackground()
            ScrollView(.vertical, showsIndicators: false) {
                VStack(spacing: 0) {
                    // 顶部品牌区在登录/注册之间保持不变，只替换下方表单。
                    AuthBrandHeader(subtitle: "同步和管理你的剪贴板")
                    AuthModeSwitcher(selection: $model.authMode)
                        .padding(.top, 28)
                    if isRegister {
                        RegisterForm(passwordVisible: $passwordVisible, confirmPasswordVisible: $confirmPasswordVisible, focusedField: $focusedField)
                            .padding(.top, 22)
                    } else {
                        LoginForm(passwordVisible: $passwordVisible, focusedField: $focusedField)
                            .padding(.top, 22)
                    }
                }
                .frame(width: KJLayout.authWindowWidth)
                .padding(.bottom, 18)
            }
            .frame(width: KJLayout.authWindowWidth, height: pageHeight)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .onChange(of: model.authMode) { _ in
            focusedField = nil
            model.loginMessage = ""
            model.authCodeMessage = ""
        }
    }
}

private enum AuthFieldKey: Hashable { case phone, code, password, email, confirmPassword }

private struct AuthBackground: View {
    var body: some View {
        ZStack {
            LinearGradient(colors: [Color(red: 0.985, green: 0.995, blue: 1), Color(red: 0.94, green: 0.975, blue: 1)], startPoint: .top, endPoint: .bottom)
            Circle().fill(Color.white.opacity(0.58)).frame(width: 300, height: 300).blur(radius: 2).offset(x: 190, y: -270)
            Circle().fill(Color(red: 0.78, green: 0.89, blue: 1).opacity(0.22)).frame(width: 360, height: 360).blur(radius: 4).offset(x: -215, y: 300)
        }
        .ignoresSafeArea()
    }
}

private struct AuthBrandHeader: View {
    let subtitle: String
    var body: some View {
        VStack(spacing: 10) {
            if let path = Bundle.main.path(forResource: "快剪", ofType: "icns"), let icon = NSImage(contentsOfFile: path) {
                Image(nsImage: icon).resizable().scaledToFit().frame(width: 72, height: 72)
            } else {
                Image(systemName: "arrow.right.circle.fill").font(.system(size: 64)).foregroundStyle(Color(red: 0.30, green: 0.64, blue: 0.98))
            }
            Text("登录快剪")
                .font(.system(size: 28, weight: .bold))
                .foregroundStyle(Color(red: 0.09, green: 0.15, blue: 0.22))
            Text(subtitle)
                .font(.system(size: 14))
                .foregroundStyle(Color(red: 0.45, green: 0.51, blue: 0.60))
        }
        .frame(width: 360)
        .padding(.top, 28)
    }
}

private struct AuthModeSwitcher: View {
    @Binding var selection: String
    var body: some View {
        HStack(spacing: 0) {
            AuthModeButton(title: "登录", selected: selection == "login") { selection = "login" }
            AuthModeButton(title: "注册", selected: selection == "register") { selection = "register" }
        }
        .frame(width: 360, height: 42)
        .background(Color(red: 0.91, green: 0.94, blue: 0.98))
        .clipShape(RoundedRectangle(cornerRadius: 12))
    }
}

private struct AuthModeButton: View {
    let title: String
    let selected: Bool
    let action: () -> Void
    var body: some View {
        Button(action: action) {
            Text(title).font(.system(size: 15, weight: .semibold)).foregroundStyle(selected ? .white : Color(red: 0.25, green: 0.32, blue: 0.42)).frame(maxWidth: .infinity, maxHeight: .infinity)
                .background(selected ? AnyShapeStyle(LinearGradient(colors: [Color(red: 0.14, green: 0.53, blue: 0.97), Color(red: 0.34, green: 0.65, blue: 0.98)], startPoint: .leading, endPoint: .trailing)) : AnyShapeStyle(Color.clear))
                .clipShape(RoundedRectangle(cornerRadius: 10))
        }
        .buttonStyle(.plain)
    }
}

private struct AuthLabel: View {
    let title: String
    var body: some View { Text(title).font(.system(size: 13, weight: .semibold)).foregroundStyle(Color(red: 0.09, green: 0.15, blue: 0.22)).frame(width: 360, alignment: .leading) }
}

private struct AuthField: View {
    @Binding var text: String
    let placeholder: String
    let icon: String
    let field: AuthFieldKey
    var secure = false
    var width: CGFloat = 360
    @Binding var passwordVisible: Bool
    var focusedField: FocusState<AuthFieldKey?>.Binding

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: icon).font(.system(size: 18)).foregroundStyle(Color(red: 0.43, green: 0.51, blue: 0.62)).frame(width: 18)
            Group {
                if secure && !passwordVisible { SecureField(placeholder, text: $text) }
                else { TextField(placeholder, text: $text) }
            }
            .textFieldStyle(.plain)
            .font(.system(size: 14))
            .foregroundStyle(Color(red: 0.09, green: 0.15, blue: 0.22))
            .focused(focusedField, equals: field)
            if secure {
                Button { passwordVisible.toggle() } label: { Image(systemName: passwordVisible ? "eye" : "eye.slash").font(.system(size: 16)).foregroundStyle(Color(red: 0.43, green: 0.51, blue: 0.62)).frame(width: 32, height: 32) }
                    .buttonStyle(.plain).accessibilityLabel(passwordVisible ? "隐藏密码" : "显示密码")
            }
        }
        .padding(.horizontal, 14)
        .frame(width: width, height: 46)
        .background(Color.white.opacity(0.92))
        .clipShape(RoundedRectangle(cornerRadius: 11))
        .overlay(RoundedRectangle(cornerRadius: 11).stroke(focusedField.wrappedValue == field ? Color(red: 0.20, green: 0.55, blue: 0.96) : Color(red: 0.84, green: 0.88, blue: 0.93), lineWidth: focusedField.wrappedValue == field ? 2 : 1))
        .shadow(color: focusedField.wrappedValue == field ? Color.blue.opacity(0.12) : .clear, radius: 0, y: 0)
    }
}

private struct LoginMethodSwitcher: View {
    @Binding var selection: String
    var body: some View {
        HStack(spacing: 0) {
            AuthMethodButton(title: "验证码登录", selected: selection == "sms") { selection = "sms" }
            AuthMethodButton(title: "密码登录", selected: selection == "password") { selection = "password" }
        }
        .frame(width: 360, height: 40)
        .background(Color(red: 0.91, green: 0.94, blue: 0.98))
        .clipShape(RoundedRectangle(cornerRadius: 10))
        .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color(red: 0.82, green: 0.88, blue: 0.96)))
    }
}

private struct AuthMethodButton: View {
    let title: String
    let selected: Bool
    let action: () -> Void
    var body: some View {
        Button(action: action) {
            Text(title).font(.system(size: 13, weight: .semibold))
                .foregroundStyle(Color(red: 0.14, green: 0.49, blue: 0.94))
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .background(selected ? Color.white : Color.clear)
                .clipShape(RoundedRectangle(cornerRadius: 9))
        }
        .buttonStyle(.plain)
    }
}

private struct LoginForm: View {
    @EnvironmentObject private var model: AppModel
    @Binding var passwordVisible: Bool
    var focusedField: FocusState<AuthFieldKey?>.Binding

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            AuthLabel(title: "手机号")
            AuthField(text: $model.loginPhone, placeholder: "请输入手机号", icon: "person", field: .phone, passwordVisible: .constant(false), focusedField: focusedField)
            LoginMethodSwitcher(selection: $model.loginMethod).padding(.top, 18)
            AuthLabel(title: model.loginMethod == "sms" ? "验证码" : "密码").padding(.top, 18)
            if model.loginMethod == "sms" {
                HStack(spacing: 10) {
                    AuthField(text: $model.registerCode, placeholder: "请输入 6 位验证码", icon: "checkmark.shield", field: .code, width: 238, passwordVisible: .constant(false), focusedField: focusedField)
                    AuthSecondaryButton(title: "获取验证码") { model.sendAuthCode() }
                }
            } else {
                AuthField(text: $model.loginPassword, placeholder: "请输入密码", icon: "lock", field: .password, secure: true, passwordVisible: $passwordVisible, focusedField: focusedField)
                HStack { Spacer(); Button("忘记密码?") { model.loginMessage = "请使用短信验证码登录，或联系管理员找回密码" }.font(.system(size: 13, weight: .medium)).foregroundStyle(Color(red: 0.14, green: 0.49, blue: 0.94)).buttonStyle(.plain).frame(minWidth: 44, minHeight: 28) }
            }
            if model.loginMethod == "sms" { Color.clear.frame(height: 28) }
            AuthPrimaryButton(title: "登录") { model.loginToServer() }
            AuthStatus(message: model.loginMessage)
            if model.loginMethod == "sms" {
                AuthStatus(message: model.authCodeMessage)
            }
            AuthFooterNote()
        }
        .frame(width: 360)
        .onChange(of: model.loginMethod) { _ in
            model.loginMessage = ""
            model.authCodeMessage = ""
            model.registerCode = ""
        }
    }
}

private struct RegisterForm: View {
    @EnvironmentObject private var model: AppModel
    @Binding var passwordVisible: Bool
    @Binding var confirmPasswordVisible: Bool
    var focusedField: FocusState<AuthFieldKey?>.Binding

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            AuthLabel(title: "手机号")
            AuthField(text: $model.loginPhone, placeholder: "请输入手机号", icon: "person", field: .phone, passwordVisible: .constant(false), focusedField: focusedField)
            AuthLabel(title: "邮箱（可选）").padding(.top, 15)
            AuthField(text: $model.loginEmail, placeholder: "用于找回密码（可选）", icon: "envelope", field: .email, passwordVisible: .constant(false), focusedField: focusedField)
            AuthLabel(title: "短信验证码").padding(.top, 15)
            HStack(spacing: 10) {
                AuthField(text: $model.registerCode, placeholder: "6 位验证码", icon: "checkmark.shield", field: .code, width: 238, passwordVisible: .constant(false), focusedField: focusedField)
                AuthSecondaryButton(title: "获取") { model.sendAuthCode() }
            }
            AuthLabel(title: "密码").padding(.top, 15)
            AuthField(text: $model.loginPassword, placeholder: "至少 6 位", icon: "lock", field: .password, secure: true, passwordVisible: $passwordVisible, focusedField: focusedField)
            AuthLabel(title: "确认密码").padding(.top, 15)
            AuthField(text: $model.confirmPassword, placeholder: "再次输入密码", icon: "lock", field: .confirmPassword, secure: true, passwordVisible: $confirmPasswordVisible, focusedField: focusedField)
            AuthPrimaryButton(title: "注册") { model.registerToServer() }.padding(.top, 22)
            AuthStatus(message: model.loginMessage)
            AuthStatus(message: model.authCodeMessage)
            AuthFooterNote()
        }
        .frame(width: 360)
    }
}

private struct AuthSecondaryButton: View {
    let title: String
    let action: () -> Void
    var body: some View {
        Button(title, action: action)
            .font(.system(size: 13, weight: .semibold))
            .foregroundStyle(Color(red: 0.14, green: 0.49, blue: 0.94))
            .frame(width: 112, height: 46)
            .background(Color.white.opacity(0.92))
            .clipShape(RoundedRectangle(cornerRadius: 11))
            .overlay(RoundedRectangle(cornerRadius: 11).stroke(Color(red: 0.84, green: 0.88, blue: 0.93)))
            .buttonStyle(.plain)
    }
}

private struct AuthPrimaryButton: View {
    let title: String
    let action: () -> Void
    var body: some View {
        Button(action: action) {
            HStack(spacing: 10) { Text(title); Image(systemName: "arrow.right").font(.system(size: 16, weight: .semibold)) }
                .font(.system(size: 16, weight: .semibold))
                .foregroundStyle(.white)
                .frame(width: 360, height: 48)
                .background(LinearGradient(colors: [Color(red: 0.14, green: 0.53, blue: 0.97), Color(red: 0.39, green: 0.70, blue: 0.99)], startPoint: .leading, endPoint: .trailing))
                .clipShape(RoundedRectangle(cornerRadius: 11))
        }
        .buttonStyle(.plain)
    }
}

private struct AuthStatus: View {
    let message: String
    var body: some View { if !message.isEmpty { Text(message).font(.system(size: 12)).foregroundStyle(Color.orange).frame(width: 360, alignment: .leading).fixedSize(horizontal: false, vertical: true) } }
}

private struct AuthFooterNote: View {
    var body: some View {
        Label("邮箱为可选项；登录支持短信验证码或手机号密码。", systemImage: "info.circle")
            .font(.system(size: 12))
            .foregroundStyle(Color(red: 0.45, green: 0.51, blue: 0.60))
            .frame(width: 360, alignment: .center)
            .padding(.top, 10)
    }
}

struct SidebarView: View {
    @EnvironmentObject private var model: AppModel
    @Binding var showingSettings: Bool

    var body: some View {
        VStack(spacing: 4) {
            Text("快剪")
                .font(.system(size: 20, weight: .bold))
                .foregroundStyle(Color.kjInk)
                .padding(.top, 16)
                .padding(.bottom, 8)
            ScrollView(showsIndicators: false) {
                VStack(spacing: 4) {
                    ForEach(SidebarSection.allCases) { section in
                        Button {
                            model.selectedSection = section
                        } label: {
                            ZStack {
                                VStack(spacing: 5) {
                                    if section == .calculator {
                                        let calculatorIcon = NSWorkspace.shared.icon(forFile: "/System/Applications/Calculator.app")
                                        Image(nsImage: calculatorIcon)
                                            .resizable()
                                            .interpolation(.high)
                                            .scaledToFit()
                                            .frame(width: 22, height: 22)
                                    } else {
                                        Image(systemName: section.icon)
                                            .font(.system(size: 18, weight: .medium))
                                    }
                                    Text(section.title)
                                        .font(.system(size: 11, weight: .medium))
                                }
                                .foregroundStyle(model.selectedSection == section ? Color.kjMint : Color.kjMuted)
                                .frame(width: KJLayout.navItemWidth, height: KJLayout.navItemHeight)
                                .background(model.selectedSection == section ? Color.kjMintSoft : .clear)
                                .clipShape(RoundedRectangle(cornerRadius: 13))
                            }
                            // 视觉卡片仍为 60pt，但整块侧栏内部留白也可以点击。
                            .frame(width: KJLayout.sidebarWidth, height: KJLayout.navItemHeight)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(KJSidebarButtonStyle())
                    }
                }
            }
            Spacer(minLength: 6)
            Button { showingSettings = true } label: {
                Image(systemName: "gearshape.fill")
                    .font(.system(size: 20))
                    .foregroundStyle(Color.kjMuted)
                    .frame(width: KJLayout.navItemWidth, height: 48)
            }
            .buttonStyle(.plain)
            .padding(.bottom, 12)
        }
        .frame(width: KJLayout.sidebarWidth)
        .background(Color(red: 0.95, green: 0.98, blue: 0.96))
        .simultaneousGesture(TapGesture(count: 2).onEnded {
            if model.doubleClickCollapseEnabled { model.isSidebarCollapsed = true }
        })
    }
}

private struct KJSidebarButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .opacity(configuration.isPressed ? 0.68 : 1)
            .scaleEffect(configuration.isPressed ? 0.98 : 1)
            .animation(.easeOut(duration: 0.1), value: configuration.isPressed)
    }
}

struct PageHeader: View {
    let title: String
    let subtitle: String
    var trailing: AnyView? = nil
    var body: some View {
        HStack(alignment: .top, spacing: 16) {
            VStack(alignment: .leading, spacing: 6) {
                Text(title).font(.system(size: 30, weight: .bold)).foregroundStyle(Color.kjInk)
                if !subtitle.isEmpty {
                    Text(subtitle).font(.system(size: 15)).foregroundStyle(Color.kjMuted)
                }
            }
            Spacer()
            if let trailing { trailing }
        }
    }
}

struct SectionCard<Content: View>: View {
    @ViewBuilder let content: Content
    var body: some View {
        content
            .padding(20)
            .background(.white)
            .clipShape(RoundedRectangle(cornerRadius: 18))
            .overlay(RoundedRectangle(cornerRadius: 18).stroke(Color.kjBorder, lineWidth: 1))
    }
}

struct ActionButton: View {
    let title: String
    var icon: String? = nil
    var filled = false
    var action: () -> Void
    var body: some View {
        Button(action: action) {
            HStack(spacing: 7) {
                if let icon { Image(systemName: icon) }
                Text(title)
            }
            .font(.system(size: 14, weight: .semibold))
            .foregroundStyle(filled ? .white : Color.kjInk)
            .padding(.horizontal, 15)
            .frame(height: 38)
            .background(filled ? Color.kjMint : Color.white)
            .clipShape(RoundedRectangle(cornerRadius: 11))
            .overlay(!filled ? RoundedRectangle(cornerRadius: 11).stroke(Color.kjBorder, lineWidth: 1) : nil)
        }
        .buttonStyle(.plain)
    }
}

// MARK: - Clipboard

struct ClipboardPage: View {
    @EnvironmentObject private var model: AppModel
    @State private var filter = "全部"
    @State private var search = ""
    @State private var imageScale = 1.0

    private var filtered: [ClipboardItem] {
        model.items.filter { item in
            let matchesFilter = filter == "全部" || (filter == "文本" && item.kind == .text) || (filter == "图片" && item.kind == .image) || (filter == "网址" && item.kind == .url)
            let matchesSearch = search.isEmpty || item.text.localizedCaseInsensitiveContains(search)
            return matchesFilter && matchesSearch
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            PageHeader(title: "我的剪贴板", subtitle: "")
            HStack(spacing: 8) {
                ForEach(["全部", "文本", "图片", "网址"], id: \.self) { value in
                    Button(value) { filter = value }
                        .font(.system(size: 14, weight: .semibold))
                        .foregroundStyle(filter == value ? .white : Color.kjMuted)
                        .frame(maxWidth: .infinity)
                        .frame(height: 34)
                        .background(filter == value ? Color.kjMint : Color.clear)
                        .clipShape(RoundedRectangle(cornerRadius: 10))
                        .buttonStyle(.plain)
                }
            }
            if !model.cloudMessage.isEmpty { Text(model.cloudMessage).font(.system(size: 12)).foregroundStyle(Color.kjMuted) }
            HStack {
                Image(systemName: "magnifyingglass").foregroundStyle(Color.kjMuted)
                TextField("搜索剪贴板内容", text: $search)
                    .textFieldStyle(.plain)
                if !model.copyMessage.isEmpty {
                    Text(model.copyMessage).foregroundStyle(Color.kjMint).font(.system(size: 13, weight: .semibold))
                }
            }
            .padding(.horizontal, 14).frame(height: 42)
            .background(.white).clipShape(RoundedRectangle(cornerRadius: 12))
            .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.kjBorder, lineWidth: 1))
            if filter == "图片" {
                HStack(spacing: 12) {
                    Image(systemName: "photo.on.rectangle").foregroundStyle(Color.kjMint)
                    Text("图片大小").font(.system(size: 14, weight: .semibold)).foregroundStyle(Color.kjInk)
                    Slider(value: $imageScale, in: 0.6...1.8).tint(Color.kjMint)
                    Text("\(Int(imageScale * 100))%")
                        .font(.system(size: 13, weight: .semibold))
                        .foregroundStyle(Color.kjMuted)
                        .frame(width: 40, alignment: .trailing)
                    Button("重置") { imageScale = 1.0 }
                        .buttonStyle(.plain)
                        .foregroundStyle(Color.kjMint)
                        .font(.system(size: 13, weight: .semibold))
                }
                .padding(.horizontal, 14)
                .frame(height: 42)
                .background(Color.kjMintSoft)
                .clipShape(RoundedRectangle(cornerRadius: 13))
                .accessibilityElement(children: .combine)
                .accessibilityLabel("图片大小")
            }
            ScrollView {
                if filtered.isEmpty {
                    EmptyState(title: "还没有剪贴板内容", message: "复制内容后会自动出现在这里")
                } else if filter == "图片" {
                    LazyVGrid(columns: [GridItem(.flexible(), spacing: 14), GridItem(.flexible(), spacing: 14)], spacing: 14) {
                        ForEach(filtered) { item in ClipboardImageTile(item: item, imageScale: imageScale) }
                    }
                } else {
                    LazyVStack(spacing: 0) {
                        ForEach(filtered) { item in ClipboardRow(item: item) }
                    }
                }
            }
        }
        .padding(.leading, 28)
        .padding(.top, 28)
        .padding(.bottom, 28)
    }
}

private struct ClipboardImageTile: View {
    @EnvironmentObject private var model: AppModel
    let item: ClipboardItem
    let imageScale: Double
    @State private var showingImagePreview = false

    var body: some View {
        Group {
            if let path = item.imagePath, let image = NSImage(contentsOfFile: path) {
                Image(nsImage: image)
                    .resizable()
                    .scaledToFit()
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .padding(10)
            } else {
                Image(systemName: "photo")
                    .font(.system(size: 28))
                    .foregroundStyle(Color.kjMuted)
            }
        }
        .frame(maxWidth: .infinity)
        .frame(height: max(112, min(210, 132 * imageScale)))
        .background(Color.white)
        .clipShape(RoundedRectangle(cornerRadius: 14))
        .overlay(RoundedRectangle(cornerRadius: 14).stroke(Color.kjBorder, lineWidth: 1))
        .contentShape(Rectangle())
        .onTapGesture(count: 2) { model.copy(item) }
        .onTapGesture(count: 1) { showingImagePreview = true }
        .contextMenu {
            Button("复制图片") { model.copy(item) }
            Button("打开大图") { showingImagePreview = true }
            Button(item.favorite ? "取消收藏" : "收藏") { model.toggleFavorite(item) }
            Button("删除", role: .destructive) { model.delete(item) }
        }
        .sheet(isPresented: $showingImagePreview) {
            if let path = item.imagePath, let image = NSImage(contentsOfFile: path) {
                ImagePreviewSheet(image: image, onCopy: { model.copy(item) })
            }
        }
        .help("点击放大，双击复制图片")
    }
}

struct ClipboardRow: View {
    @EnvironmentObject private var model: AppModel
    let item: ClipboardItem

    var body: some View {
        HStack(spacing: 13) {
            Image(systemName: item.kind == .url ? "link" : "doc.text")
                .font(.system(size: 18, weight: .medium)).foregroundStyle(Color.kjMint)
                .frame(width: 32, height: 32)
            VStack(alignment: .leading, spacing: 4) {
                Text(item.text.isEmpty ? item.title : item.text)
                    .font(.system(size: 16, weight: .regular))
                    .lineLimit(1)
                    .truncationMode(.tail)
                    .multilineTextAlignment(.leading)
                    .foregroundStyle(Color.kjInk)
                HStack(spacing: 8) {
                    Text("来源：\(item.source)")
                    Spacer(minLength: 8)
                    Text(item.createdAt.kjRelative)
                }
                .font(.system(size: 13))
                .foregroundStyle(Color.kjMuted)
            }
            Spacer(minLength: 10)
        }
        .padding(.horizontal, 4)
        .frame(minHeight: 68)
        .background(model.copiedItemID == item.id ? Color.kjMintSoft : .white)
        .overlay(alignment: .bottom) { Rectangle().fill(Color.kjBorder.opacity(0.8)).frame(height: 1) }
        .contentShape(Rectangle())
        .onTapGesture { model.copy(item) }
        .contextMenu {
            Button("复制内容") { model.copy(item) }
            Button(item.favorite ? "取消收藏" : "收藏") { model.toggleFavorite(item) }
            Button("删除", role: .destructive) { model.delete(item) }
        }
    }
}

private struct ImagePreviewSheet: View {
    let image: NSImage
    let onCopy: () -> Void
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 14) {
            HStack {
                Text("图片预览").font(.system(size: 18, weight: .semibold))
                Spacer()
                Button("复制图片") { onCopy(); dismiss() }.buttonStyle(.borderedProminent).tint(Color.kjMint)
                Button("关闭") { dismiss() }.buttonStyle(.bordered)
            }
            ScrollView([.horizontal, .vertical]) {
                Image(nsImage: image)
                    .resizable()
                    .scaledToFit()
                    .frame(maxWidth: 760, maxHeight: 620)
            }
            .frame(minWidth: 520, minHeight: 400)
            .background(Color.black.opacity(0.04))
            .clipShape(RoundedRectangle(cornerRadius: 14))
        }
        .padding(20)
        .frame(minWidth: 560, minHeight: 480)
    }
}

// MARK: - Q&A

struct QuestionAnswersPage: View {
    @EnvironmentObject private var model: AppModel
    @State private var showingEditor = false
    @State private var search = ""

    private var entries: [QAEntry] {
        model.qaEntries.filter { search.isEmpty || $0.question.localizedCaseInsensitiveContains(search) || $0.answer.localizedCaseInsensitiveContains(search) }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                Text("问答")
                    .font(.system(size: 30, weight: .bold))
                    .foregroundStyle(Color.kjInk)
                Spacer()
                Image(systemName: "bubble.left.and.bubble.right.fill")
                    .font(.system(size: 30))
                    .foregroundStyle(Color.kjMintSoft)
                    .accessibilityHidden(true)
            }
            .frame(height: 44)
            .padding(.bottom, 22)

            VStack(alignment: .leading, spacing: 8) {
                HStack(spacing: 16) {
                    Text("匹配方式")
                        .font(.system(size: 15, weight: .semibold))
                        .foregroundStyle(Color.kjInk)
                    Picker("", selection: $model.qaMatchMode) {
                        ForEach(QAMatchMode.allCases) { Text($0.title).tag($0) }
                    }
                    .pickerStyle(.segmented)
                    .frame(width: 208, height: 40)
                    .accessibilityLabel("匹配方式")
                }
                HStack(spacing: 9) {
                    Image(systemName: "magnifyingglass")
                        .font(.system(size: 17))
                        .foregroundStyle(Color.kjMuted)
                    TextField("搜索问题…", text: $search)
                        .textFieldStyle(.plain)
                }
                .padding(.horizontal, 13)
                .frame(maxWidth: .infinity, minHeight: 42, maxHeight: 42)
                .background(.white)
                .clipShape(RoundedRectangle(cornerRadius: 12))
                .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.kjBorder, lineWidth: 1))
            }
            .padding(.bottom, 16)

            HStack(spacing: 12) {
                TextField("输入或复制客户问题", text: $model.qaQuestion, axis: .vertical)
                    .lineLimit(1).textFieldStyle(.roundedBorder)
                    .onChange(of: model.qaQuestion) { _ in model.updateQAAnswer() }
                Button { model.readClipboard() } label: {
                    Image(systemName: "arrow.right")
                        .font(.system(size: 20, weight: .semibold))
                        .frame(width: 46, height: 46)
                }
                .buttonStyle(.borderedProminent)
                .tint(Color.kjMint)
                .clipShape(RoundedRectangle(cornerRadius: 12))
                .help("读取剪贴板")
                .accessibilityLabel("读取剪贴板")
            }
            .frame(height: 46)
            .padding(.bottom, 18)

            AnswerCard()
                .padding(.bottom, 24)

            HStack(alignment: .center, spacing: 14) {
                Text("我的问答")
                    .font(.system(size: 21, weight: .bold))
                    .foregroundStyle(Color.kjInk)
                QAIconButton(title: "新建", icon: "plus", filled: true) { showingEditor = true }
                Spacer()
                QAIconButton(title: "下载 CSV 模板", icon: "arrow.down.to.line") { downloadTemplate() }
                QAIconButton(title: "导入 CSV", icon: "arrow.up.to.line") { importQA() }
                QAIconButton(title: "导出", icon: "square.and.arrow.up") { exportQA() }
            }
            .frame(minHeight: 38)
            .padding(.bottom, 12)

            ScrollView {
                LazyVStack(spacing: 8) {
                    ForEach(entries) { entry in QAListRow(entry: entry) }
                }
            }
        }
        .padding(.horizontal, 16)
        .padding(.top, 28)
        .padding(.bottom, 20)
        .sheet(isPresented: $showingEditor) {
            QAEditor { entry in model.qaEntries.insert(entry, at: 0); model.save() }
        }
    }

    private func importQA() {
        let panel = NSOpenPanel(); panel.allowedContentTypes = [.commaSeparatedText, .json, .spreadsheet]; panel.allowsMultipleSelection = false
        if panel.runModal() == .OK, let url = panel.url { model.importQA(url: url) }
    }

    private func downloadTemplate() {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "快剪问答模板.csv"
        panel.allowedContentTypes = [.commaSeparatedText]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        let template = "问题,答案,别名,关键词,分类,备注,是否启用\n"
        try? template.data(using: .utf8)?.write(to: url, options: .atomic)
    }

    private func exportQA() {
        let panel = NSSavePanel(); panel.nameFieldStringValue = "快剪问答.csv"; panel.allowedContentTypes = [.commaSeparatedText]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        let csv = "问题,答案,别名,关键词,分类,备注,是否启用\n" + model.qaEntries.map { entry in
            [entry.question, entry.answer, entry.aliases, entry.keywords, entry.category, entry.note, entry.enabled ? "是" : "否"].map { "\"\($0.replacingOccurrences(of: "\"", with: "\"\""))\"" }.joined(separator: ",")
        }.joined(separator: "\n")
        try? csv.data(using: .utf8)?.write(to: url, options: .atomic)
    }
}

private struct QAIconButton: View {
    let title: String
    let icon: String
    var filled = false
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Group {
                if filled {
                    HStack(spacing: 5) {
                        Image(systemName: icon)
                        Text(title)
                    }
                } else {
                    Image(systemName: icon)
                }
            }
            .font(.system(size: 17, weight: .semibold))
            .foregroundStyle(filled ? .white : Color.kjInk)
            .frame(width: filled ? 76 : 36, height: 36)
                .background(filled ? Color.kjMint : Color.white)
                .clipShape(RoundedRectangle(cornerRadius: 10))
                .overlay(!filled ? RoundedRectangle(cornerRadius: 10).stroke(Color.kjBorder, lineWidth: 1) : nil)
        }
        .buttonStyle(.plain)
        .help(title)
        .accessibilityLabel(title)
    }
}

struct AnswerCard: View {
    @EnvironmentObject private var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .center, spacing: 10) {
                Image(systemName: "sparkles")
                    .font(.system(size: 20, weight: .semibold))
                    .foregroundStyle(Color.kjMint)
                Text(model.qaMatchedQuestion.isEmpty ? "还没有匹配的问题" : "问题：\(model.qaMatchedQuestion)")
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundStyle(Color(red: 0.05, green: 0.41, blue: 0.30))
                    .lineLimit(2)
                Spacer()
                if model.qaMatchScore > 0 {
                    Text("\(model.qaMatchMode.title) · \(model.qaMatchScore)%")
                        .font(.system(size: 12, weight: .semibold))
                        .foregroundStyle(Color(red: 0.05, green: 0.41, blue: 0.30))
                        .padding(.horizontal, 8)
                        .padding(.vertical, 5)
                        .background(Color(red: 0.87, green: 0.96, blue: 0.91))
                        .clipShape(RoundedRectangle(cornerRadius: 8))
                }
            }
            if model.qaAnswer.isEmpty {
                Text("暂时没有匹配答案")
                    .foregroundStyle(Color.kjMuted)
                    .frame(maxWidth: .infinity, minHeight: 140, alignment: .topLeading)
                    .padding(16)
                    .background(.white)
                    .clipShape(RoundedRectangle(cornerRadius: 14))
            } else {
                ScrollView {
                    QAAnswerContent(answer: model.qaAnswer)
                        .textSelection(.enabled)
                }
                .frame(maxWidth: .infinity, minHeight: 140, maxHeight: 200, alignment: .topLeading)
                .padding(16)
                .background(.white)
                .clipShape(RoundedRectangle(cornerRadius: 14))
            }
            HStack(spacing: 10) {
                Spacer()
                Button { model.copyAnswer() } label: {
                    Label("复制答案", systemImage: "doc.on.doc")
                        .frame(width: 112, height: 40)
                }
                .buttonStyle(.bordered)
                .clipShape(RoundedRectangle(cornerRadius: 10))
                .help("复制答案")
                .disabled(model.qaAnswer.isEmpty)
                Button { model.fillAnswerToTarget() } label: {
                    Label("填充到目标软件", systemImage: "arrow.right")
                        .frame(width: 168, height: 40)
                }
                .buttonStyle(.borderedProminent)
                .tint(Color.kjMint)
                .clipShape(RoundedRectangle(cornerRadius: 10))
                .help("填充到目标软件")
                .disabled(model.qaAnswer.isEmpty)
            }
        }
        .padding(18).background(Color.kjMintSoft).clipShape(RoundedRectangle(cornerRadius: 20))
    }
}

private struct QAAnswerContent: View {
    let answer: String

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            ForEach(Array(answer.split(separator: "\n", omittingEmptySubsequences: false).enumerated()), id: \.offset) { _, line in
                let value = String(line)
                if let numbered = numberedLine(value) {
                    HStack(alignment: .top, spacing: 12) {
                        Text(numbered.number)
                            .font(.system(size: 13, weight: .bold))
                            .foregroundStyle(Color(red: 0.03, green: 0.60, blue: 0.42))
                            .frame(width: 26, height: 26)
                            .background(Color(red: 0.87, green: 0.98, blue: 0.92))
                            .clipShape(Circle())
                        Text(numbered.text)
                            .font(.system(size: 15))
                            .foregroundStyle(Color(red: 0.15, green: 0.23, blue: 0.20))
                            .fixedSize(horizontal: false, vertical: true)
                    }
                } else if !value.isEmpty {
                    Text(value)
                        .font(.system(size: 15))
                        .foregroundStyle(Color(red: 0.15, green: 0.23, blue: 0.20))
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }

    private func numberedLine(_ value: String) -> (number: String, text: String)? {
        let trimmed = value.trimmingCharacters(in: .whitespaces)
        let digits = String(trimmed.prefix(while: { $0.isNumber }))
        guard !digits.isEmpty else { return nil }
        let rest = trimmed.dropFirst(digits.count)
        guard let marker = rest.first, [".", "、", ")"].contains(marker) else { return nil }
        return (digits, String(rest.dropFirst()).trimmingCharacters(in: .whitespaces))
    }
}

struct QAListRow: View {
    @EnvironmentObject private var model: AppModel
    let entry: QAEntry
    @State private var editing = false
    @State private var hovering = false
    var body: some View {
        HStack(alignment: .center, spacing: 12) {
            Image(systemName: entry.enabled ? "questionmark.circle.fill" : "pause.circle")
                .foregroundStyle(entry.enabled ? Color.kjMint : Color.kjMuted)
                .font(.system(size: 18))
                .frame(width: 34, height: 34)
                .background(Color.kjMintSoft)
                .clipShape(RoundedRectangle(cornerRadius: 12))
            Text(entry.question)
                .font(.system(size: 15, weight: .semibold))
                .foregroundStyle(Color.kjInk)
                .lineLimit(1)
                .truncationMode(.tail)
            Spacer(minLength: 8)
            if hovering {
                HStack(spacing: 4) {
                    Button { editing = true } label: { Image(systemName: "pencil") }
                        .help("编辑")
                        .accessibilityLabel("编辑")
                    Button { model.deleteQAEntry(entry) } label: { Image(systemName: "trash") }
                        .help("删除")
                        .accessibilityLabel("删除")
                        .foregroundStyle(.red.opacity(0.75))
                }
                .buttonStyle(.plain)
            } else {
                Image(systemName: "chevron.right")
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundStyle(Color.kjMuted)
            }
        }
        .padding(.horizontal, 14).padding(.vertical, 10).frame(minHeight: 60)
        .background(.white).clipShape(RoundedRectangle(cornerRadius: 13))
        .overlay(RoundedRectangle(cornerRadius: 13).stroke(Color.kjBorder, lineWidth: 1))
        .contentShape(Rectangle())
        .onHover { hovering = $0 }
        .onTapGesture { model.setQAQuestion(entry.question) }
        .contextMenu {
            Button("编辑") { editing = true }
            Button("删除", role: .destructive) { model.deleteQAEntry(entry) }
        }
        .sheet(isPresented: $editing) {
            QAEditor(entry: entry) { updated in
                if let index = model.qaEntries.firstIndex(where: { $0.id == entry.id }) { model.qaEntries[index] = updated; model.save(); model.updateQAAnswer() }
            }
        }
    }
}

struct QAEditor: View {
    @Environment(\.dismiss) private var dismiss
    @State private var question: String
    @State private var answer: String
    @State private var aliases: String
    @State private var keywords: String
    @State private var category: String
    @State private var note: String
    @State private var enabled: Bool
    private let existingID: UUID?
    let onSave: (QAEntry) -> Void

    init(entry: QAEntry? = nil, onSave: @escaping (QAEntry) -> Void) {
        _question = State(initialValue: entry?.question ?? "")
        _answer = State(initialValue: entry?.answer ?? "")
        _aliases = State(initialValue: entry?.aliases ?? "")
        _keywords = State(initialValue: entry?.keywords ?? "")
        _category = State(initialValue: entry?.category ?? "")
        _note = State(initialValue: entry?.note ?? "")
        _enabled = State(initialValue: entry?.enabled ?? true)
        existingID = entry?.id
        self.onSave = onSave
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(existingID == nil ? "新建问答" : "编辑问答").font(.system(size: 24, weight: .bold))
            TextField("问题", text: $question).textFieldStyle(.roundedBorder)
            TextEditor(text: $answer).frame(minHeight: 140).overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.kjBorder))
            TextField("别名（用 | 分隔）", text: $aliases).textFieldStyle(.roundedBorder)
            TextField("关键词（用 | 分隔）", text: $keywords).textFieldStyle(.roundedBorder)
            HStack { Toggle("启用", isOn: $enabled); Spacer(); Button("取消") { dismiss() }.buttonStyle(.bordered); Button("保存") { save() }.buttonStyle(.borderedProminent).tint(Color.kjMint) }
        }.padding(24).frame(width: 520, height: 430)
    }

    private func save() {
        guard !question.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return }
        onSave(QAEntry(id: existingID ?? UUID(), question: question, answer: answer, aliases: aliases, keywords: keywords, category: category, note: note, enabled: enabled))
        dismiss()
    }
}

// MARK: - Translation, Calculator and Download

struct TranslationPage: View {
    @EnvironmentObject private var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("翻译")
                .font(.system(size: 30, weight: .bold))
                .foregroundStyle(Color.kjInk)

            HStack(alignment: .bottom, spacing: 10) {
                VStack(alignment: .leading, spacing: 5) {
                    Text("原文语种")
                        .font(.system(size: 14, weight: .semibold))
                        .foregroundStyle(Color.kjMuted)
                    Picker("", selection: $model.translationSourceLanguage) {
                        ForEach(TranslationLanguage.allCases) { Text($0.title).tag($0) }
                    }
                    .pickerStyle(.menu)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .accessibilityLabel("原文语种")
                }
                Image(systemName: "arrow.right")
                    .foregroundStyle(Color.kjMuted)
                    .padding(.bottom, 7)
                VStack(alignment: .leading, spacing: 5) {
                    Text("译文语种")
                        .font(.system(size: 14, weight: .semibold))
                        .foregroundStyle(Color.kjMuted)
                    Picker("", selection: $model.translationTargetLanguage) {
                        ForEach(TranslationLanguage.allCases) { Text($0.title).tag($0) }
                    }
                    .pickerStyle(.menu)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .accessibilityLabel("译文语种")
                }
            }

            HStack(spacing: 8) {
                Text(model.translationInput.isEmpty ? "自动识别" : "\(model.translationDetectedSource.title) → \(model.translationResolvedTarget.title)")
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundStyle(Color.kjMuted)
                Spacer()
                Button { model.readClipboardForTranslation() } label: {
                    Label("读取剪贴板", systemImage: "doc.on.clipboard")
                        .frame(height: 36)
                }
                .buttonStyle(.bordered)
                .help("读取剪贴板")
                .accessibilityLabel("读取剪贴板")
                Button { model.translate() } label: {
                    Label("翻译", systemImage: "character.bubble")
                        .frame(height: 36)
                }
                .buttonStyle(.borderedProminent)
                .tint(Color.kjMint)
                .help("翻译")
                .accessibilityLabel("翻译")
            }

            Text("原文")
                .font(.system(size: 16, weight: .semibold))
                .foregroundStyle(Color.kjInk)
            TextEditor(text: $model.translationInput)
                .frame(minHeight: 170)
                .padding(8)
                .background(.white)
                .clipShape(RoundedRectangle(cornerRadius: 14))
                .overlay(RoundedRectangle(cornerRadius: 14).stroke(Color.kjBorder))
                .onChange(of: model.translationInput) { _ in model.translate() }

            Text("译文")
                .font(.system(size: 16, weight: .semibold))
                .foregroundStyle(Color.kjInk)
            TextEditor(text: $model.translationOutput)
                .frame(minHeight: 170)
                .padding(8)
                .background(.white)
                .clipShape(RoundedRectangle(cornerRadius: 14))
                .overlay(RoundedRectangle(cornerRadius: 14).stroke(Color.kjBorder))
            HStack {
                Text(model.translationMessage)
                    .font(.system(size: 13))
                    .foregroundStyle(Color.kjMuted)
                Spacer()
                ActionButton(title: "复制译文", icon: "doc.on.doc") { model.copyTranslation() }
            }
        }
        .padding(.horizontal, 16)
        .padding(.top, 28)
        .padding(.bottom, 20)
    }
}

struct CalculatorPage: View {
    @EnvironmentObject private var model: AppModel
    private let keys = [["AC", "⌫", "%", "÷"], ["7", "8", "9", "×"], ["4", "5", "6", "-"], ["1", "2", "3", "+"], ["±", "0", ".", "="]]

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            PageHeader(title: "计算机", subtitle: "")
            VStack(spacing: 12) {
                Text(model.calculatorExpression)
                    .foregroundStyle(Color.kjMuted)
                    .frame(maxWidth: .infinity, alignment: .trailing)
                Text(model.calculatorDisplay)
                    .font(.system(size: 38, weight: .bold))
                    .frame(maxWidth: .infinity, alignment: .trailing)
                    .padding(.vertical, 10)
                    .padding(.horizontal, 20)
                    .frame(height: 92)
                    .background(.white)
                    .clipShape(RoundedRectangle(cornerRadius: 18))
                    .overlay(RoundedRectangle(cornerRadius: 18).stroke(Color.kjBorder, lineWidth: 1))
                LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 10), count: 4), spacing: 10) {
                    ForEach(keys.flatMap { $0 }, id: \.self) { key in
                        Button(key) { model.calculatorInput(key) }
                            .font(.system(size: 20, weight: .semibold))
                            .frame(maxWidth: .infinity, minHeight: 48)
                            .background(["÷", "×", "-", "+"].contains(key) ? Color.kjMint : key == "=" ? Color(red: 0.02, green: 0.45, blue: 0.34) : Color.kjMintSoft)
                            .foregroundStyle(["÷", "×", "-", "+", "="].contains(key) ? .white : Color.kjInk)
                            .clipShape(RoundedRectangle(cornerRadius: 13))
                            .buttonStyle(.plain)
                            .help("计算器按键 \(key)")
                    }
                }
            }
            .padding(20)
            .background(.white)
            .clipShape(RoundedRectangle(cornerRadius: 18))
            .overlay(RoundedRectangle(cornerRadius: 18).stroke(Color.kjBorder, lineWidth: 1))
            .frame(maxWidth: 500)
            HStack {
                Text("计算机历史记录").font(.system(size: 20, weight: .bold))
                Spacer()
                Button("清空") { model.clearCalculatorHistory() }.buttonStyle(.plain).foregroundStyle(Color.kjMint)
            }
            if model.calculatorHistory.isEmpty {
                Text("暂无计算记录").foregroundStyle(Color.kjMuted).frame(maxWidth: .infinity, alignment: .leading).padding(.vertical, 12)
            } else {
                LazyVStack(alignment: .leading, spacing: 8) {
                    ForEach(model.calculatorHistory, id: \.self) { Text($0).font(.system(size: 14)).foregroundStyle(Color.kjMuted).frame(maxWidth: .infinity, alignment: .leading) }
                }
            }
        }
        .padding(28)
    }
}

struct DownloadsPage: View {
    @EnvironmentObject private var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            PageHeader(title: "下载", subtitle: "下载视频或文件，任务状态会显示在这里")
            SectionCard {
                HStack { TextField("粘贴下载地址", text: $model.downloadURL).textFieldStyle(.roundedBorder); ActionButton(title: "开始下载", icon: "arrow.down", filled: true) { model.startDownload() }.disabled(model.downloadURL.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty).opacity(model.downloadURL.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? 0.5 : 1) }
                Text(model.downloadMessage).foregroundStyle(Color.kjMuted)
            }
            EmptyState(title: "暂无下载任务", message: "下载资源后会在这里显示进度")
        }.padding(28)
    }
}

// MARK: - Shortcuts

struct ShortcutsPage: View {
    @EnvironmentObject private var model: AppModel
    @State private var showingEditor = false
    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            PageHeader(title: "快捷", subtitle: "用快捷键快速打开常用应用和网页", trailing: AnyView(ActionButton(title: "添加快捷", icon: "plus", filled: true) { showingEditor = true }))
            ScrollView {
                LazyVStack(spacing: 10) {
                    if model.shortcuts.isEmpty { EmptyState(title: "还没有快捷入口", message: "可以添加应用或网页，并设置 ⌥⌘、⌘5、F1-F12 等组合键") }
                    ForEach(model.shortcuts) { item in
                        HStack(spacing: 14) {
                            ShortcutIcon(item: item)
                            VStack(alignment: .leading) { Text(item.name).font(.system(size: 16, weight: .semibold)); Text(item.target).font(.system(size: 12)).foregroundStyle(Color.kjMuted).lineLimit(1) }
                            Spacer(); Text(item.hotkey.isEmpty ? "未设置" : item.hotkey).foregroundStyle(Color.kjMuted).padding(8).background(Color.gray.opacity(0.1)).clipShape(RoundedRectangle(cornerRadius: 8))
                            Toggle("", isOn: Binding(get: { item.enabled }, set: { newValue in if let i = model.shortcuts.firstIndex(where: { $0.id == item.id }) { model.shortcuts[i].enabled = newValue; model.save() } })).labelsHidden()
                            Button("打开") { model.openShortcut(item) }.buttonStyle(.borderedProminent).tint(Color.kjMint)
                            Button { model.deleteShortcut(item) } label: { Image(systemName: "ellipsis.circle") }.buttonStyle(.plain)
                        }.padding(15).background(.white).clipShape(RoundedRectangle(cornerRadius: 14)).overlay(RoundedRectangle(cornerRadius: 14).stroke(Color.kjBorder))
                    }
                }
            }
        }.padding(28).sheet(isPresented: $showingEditor) { ShortcutEditor { model.addShortcut($0) } }
    }
}

private struct ShortcutIcon: View {
    let item: ShortcutItem
    var body: some View {
        Group {
            if item.kind == "app" {
                Image(nsImage: NSWorkspace.shared.icon(forFile: item.target)).resizable().scaledToFit().padding(7)
            } else {
                Image(systemName: item.kind == "app" ? "app.dashed" : "link")
                    .font(.system(size: 21))
                    .foregroundStyle(Color.kjMint)
            }
        }
        .frame(width: 48, height: 48)
        .background(Color.kjMintSoft)
        .clipShape(RoundedRectangle(cornerRadius: 12))
    }
}

struct ShortcutEditor: View {
    @Environment(\.dismiss) private var dismiss
    @EnvironmentObject private var model: AppModel
    @State private var name = ""
    @State private var target = ""
    @State private var hotkey = ""
    @State private var kind = "app"
    var onSave: (ShortcutItem) -> Void
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("添加快捷").font(.system(size: 24, weight: .bold))
            TextField("名称", text: $name).textFieldStyle(.roundedBorder)
            Picker("类型", selection: $kind) { Text("打开应用").tag("app"); Text("打开链接").tag("link") }.pickerStyle(.segmented)
            HStack { TextField(kind == "app" ? "选择应用程序" : "https://", text: $target).textFieldStyle(.roundedBorder); if kind == "app" { Button("选择应用") { if let url = model.chooseApplication() { target = url.path } } } }
            TextField("快捷键，例如 ⌥⌘5 或 F1", text: $hotkey).textFieldStyle(.roundedBorder)
            Text("支持 ⌘、⌥、⌃、⇧、数字字母和 F1-F12。系统级注册会在后续补充。" ).font(.system(size: 12)).foregroundStyle(Color.kjMuted)
            HStack { Spacer(); Button("取消") { dismiss() }.buttonStyle(.bordered); Button("保存") { onSave(ShortcutItem(name: name.isEmpty ? "新快捷" : name, target: target, hotkey: hotkey, kind: kind)); dismiss() }.buttonStyle(.borderedProminent).tint(Color.kjMint) }
        }.padding(24).frame(width: 520, height: 310)
    }
}

// MARK: - Remaining pages

struct TeamPage: View { var body: some View { SimplePage(title: "团队", subtitle: "团队成员和共享剪贴板", message: "登录后可以在这里管理团队空间。") } }

struct ConnectionsPage: View {
    @EnvironmentObject private var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            PageHeader(title: "连接", subtitle: "查看当前账号下的设备")
            HStack { Text(model.deviceMessage).foregroundStyle(Color.kjMuted); Spacer(); ActionButton(title: "刷新设备", icon: "arrow.clockwise", filled: true) { model.fetchDevices() } }
            ScrollView { LazyVStack(spacing: 8) { ForEach(model.devices) { device in DeviceRow(device: device) }; if model.devices.isEmpty { EmptyState(title: "暂无设备", message: "登录云端后点击刷新设备") } } }
        }.padding(28)
    }
}

struct TransferPage: View {
    @EnvironmentObject private var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            PageHeader(title: "互传", subtitle: "把当前剪贴板内容发送到另一台设备")
            Text(model.transferMessage).foregroundStyle(Color.kjMuted)
            ScrollView { LazyVStack(spacing: 8) { ForEach(model.devices) { device in DeviceRow(device: device, actionTitle: "发送") { model.sendClipboardToDevice(device) } }; if model.devices.isEmpty { EmptyState(title: "暂无可发送设备", message: "先在连接页面登录并刷新设备") } } }
        }.padding(28)
    }
}

struct DeviceRow: View {
    let device: DeviceItem
    var actionTitle: String? = nil
    var action: (() -> Void)? = nil
    var body: some View {
        HStack(spacing: 12) { Image(systemName: device.platform.lowercased().contains("mac") ? "laptopcomputer" : "desktopcomputer").foregroundStyle(Color.kjMint).font(.system(size: 21)); VStack(alignment: .leading) { Text(device.name).font(.system(size: 15, weight: .semibold)); Text(device.platform).font(.system(size: 12)).foregroundStyle(Color.kjMuted) }; Spacer(); Circle().fill(device.online ? Color.kjMint : Color.gray).frame(width: 8, height: 8); Text(device.online ? "在线" : "离线").font(.system(size: 12)).foregroundStyle(Color.kjMuted); if let actionTitle, let action { ActionButton(title: actionTitle, icon: "arrow.right", filled: true, action: action) } }.padding(14).background(.white).clipShape(RoundedRectangle(cornerRadius: 14)).overlay(RoundedRectangle(cornerRadius: 14).stroke(Color.kjBorder))
    }
}

struct FavoritesPage: View {
    @EnvironmentObject private var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            PageHeader(title: "收藏", subtitle: "快速访问收藏的剪贴板内容")
            ScrollView { LazyVStack(spacing: 8) { ForEach(model.items.filter(\.favorite)) { ClipboardRow(item: $0) } } }
        }.padding(28)
    }
}

struct SimplePage: View {
    let title: String; let subtitle: String; let message: String
    var body: some View { VStack(alignment: .leading, spacing: 18) { PageHeader(title: title, subtitle: subtitle); EmptyState(title: "功能准备就绪", message: message) }.padding(28) }
}

struct EmptyState: View {
    let title: String; let message: String
    var body: some View { VStack(spacing: 8) { Image(systemName: "tray").font(.system(size: 28)).foregroundStyle(Color.kjMint); Text(title).font(.system(size: 18, weight: .semibold)); Text(message).font(.system(size: 13)).foregroundStyle(Color.kjMuted) }.frame(maxWidth: .infinity, minHeight: 150).background(Color.kjMintSoft.opacity(0.55)).clipShape(RoundedRectangle(cornerRadius: 16)) }
}

// MARK: - Settings

struct SettingsPage: View {
    @Environment(\.dismiss) private var dismiss
    @EnvironmentObject private var model: AppModel
    @State private var selected = "保存"
    private let sections = ["账号", "保存", "问答助手", "快捷键", "下载", "隐藏", "权限", "更新"]
    var body: some View {
        HStack(spacing: 0) {
            VStack(alignment: .leading, spacing: 8) {
                Text("设置").font(.system(size: 26, weight: .bold)).padding(.bottom, 14)
                ForEach(sections, id: \.self) { section in Button { selected = section } label: { HStack { Image(systemName: icon(section)); Text(section) }.foregroundStyle(selected == section ? Color.kjMint : Color.kjMuted).frame(maxWidth: .infinity, alignment: .leading).padding(12).background(selected == section ? Color.kjMintSoft : .clear).clipShape(RoundedRectangle(cornerRadius: 11)) }.buttonStyle(.plain) }
                Spacer(); Button("完成") { dismiss() }.buttonStyle(.borderedProminent).tint(Color.kjMint)
            }.padding(24).frame(width: 180).background(Color(red: 0.95, green: 0.98, blue: 0.96))
            Divider()
            Group { switch selected { case "账号": AccountSettings(); case "保存": SaveSettings(); case "问答助手": QAAssistantSettings(); case "快捷键": ShortcutSettings(); case "下载": DownloadSettings(); case "隐藏": HiddenSettings(); case "权限": PermissionSettings(); case "更新": UpdateSettings(); default: BasicSettings(title: selected) } }.padding(30).frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }.frame(width: 760, height: 520)
    }
    private func icon(_ section: String) -> String { ["账号":"person.crop.circle", "保存":"tray.and.arrow.down", "问答助手":"questionmark.bubble", "快捷键":"command", "下载":"arrow.down.circle", "隐藏":"eye.slash", "权限":"lock.shield", "更新":"arrow.triangle.2.circlepath"][section] ?? "gearshape" }
}

struct AccountSettings: View {
    @EnvironmentObject private var model: AppModel
    @Environment(\.dismiss) private var dismiss
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("账号").font(.system(size: 26, weight: .bold))
            Text(model.isLoggedIn ? "当前已登录快剪云端" : "当前未登录，请在登录页面完成登录")
                .font(.system(size: 16, weight: .semibold)).foregroundStyle(Color.kjInk)
            Text("账号、手机号、验证码和密码只在登录页面填写；这里不重复显示登录表单。")
                .font(.system(size: 13)).foregroundStyle(Color.kjMuted)
            ActionButton(title: "打开登录页面", icon: "person.crop.circle", filled: true) {
                dismiss()
                model.loginMessage = ""
                model.authCodeMessage = ""
                model.isLoggedIn = false
            }
            Spacer()
        }
    }
}

struct SaveSettings: View {
    @EnvironmentObject private var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("保存").font(.system(size: 26, weight: .bold))
            Text("选择需要保存到剪贴板历史的内容").foregroundStyle(Color.kjMuted)
            Toggle("保存文本", isOn: $model.saveText)
            Toggle("保存图片", isOn: $model.saveImages)
            Toggle("保存网址", isOn: $model.saveURLs)
            Toggle("保存富文本格式（HTML / RTF）", isOn: $model.saveRichText)
            Divider()
            HStack(spacing: 10) {
                ActionButton(title: "导入旧版快剪历史", icon: "arrow.down.doc") { model.importLegacyHistory() }
                Text(model.settingsMessage).font(.system(size: 13)).foregroundStyle(Color.kjMuted)
            }
            Spacer()
        }
        .onAppear { model.settingsMessage = "" }
    }
}

struct QAAssistantSettings: View {
    @EnvironmentObject private var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("问答助手").font(.system(size: 26, weight: .bold))
            Toggle("开启问答助手", isOn: $model.qaAssistantEnabled)
            Text("复制客户问题后自动匹配问答并把答案复制到剪贴板，由你在目标软件中粘贴发送。未勾选软件时，对所有前台应用生效。")
                .foregroundStyle(Color.kjMuted)
            Divider()
            Text("匹配方式").font(.system(size: 16, weight: .semibold))
            Picker("", selection: $model.qaMatchMode) { ForEach(QAMatchMode.allCases) { Text($0.title).tag($0) } }
                .pickerStyle(.segmented).frame(width: 280)
            Text("允许自动识别的软件").font(.system(size: 16, weight: .semibold))
            ForEach(["千牛", "Google Chrome"], id: \.self) { app in
                Toggle(app, isOn: Binding(get: { model.qaAllowedApps.contains(app) }, set: { model.setQAAllowedApp(app, enabled: $0) }))
            }
            Spacer()
        }
    }
}

struct ShortcutSettings: View {
    @EnvironmentObject private var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("快捷键").font(.system(size: 26, weight: .bold))
            Text("系统截图快捷键").font(.system(size: 16, weight: .semibold))
            HStack {
                TextField("例如 ⇧⌘4", text: $model.screenshotShortcut).textFieldStyle(.roundedBorder)
                Button("恢复默认") { model.screenshotShortcut = "⇧⌘4" }.buttonStyle(.bordered)
            }
            Text("快捷入口的组合键会在快捷页面中保存，并在快剪运行时生效。建议使用包含 ⌥ 或 ⌃ 的组合键，避免和系统截图冲突。")
                .font(.system(size: 13)).foregroundStyle(Color.kjMuted)
            Spacer()
        }
    }
}
struct DownloadSettings: View {
    @EnvironmentObject private var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("下载").font(.system(size: 26, weight: .bold))
            Text("设置下载位置和登录浏览器").foregroundStyle(Color.kjMuted)
            HStack { Image(systemName: "folder.fill").foregroundStyle(Color.kjMint); Text(model.downloadDirectory.isEmpty ? "~/Downloads" : model.downloadDirectory); Spacer(); Button("选择") { chooseDirectory() }.buttonStyle(.bordered) }
            Picker("下载登录状态", selection: $model.downloadBrowser) {
                Text("不使用登录状态").tag("none")
                Text("Google Chrome").tag("chrome")
                Text("Safari").tag("safari")
            }
            Text("下载时可读取已登录浏览器的本机状态，用于需要登录的公开内容；不会上传 Cookie。")
                .font(.system(size: 13)).foregroundStyle(Color.kjMuted)
            Spacer()
        }
    }
    private func chooseDirectory() {
        let panel = NSOpenPanel(); panel.canChooseDirectories = true; panel.canChooseFiles = false; panel.allowsMultipleSelection = false
        if panel.runModal() == .OK { model.downloadDirectory = panel.url?.path ?? "" }
    }
}
struct HiddenSettings: View {
    @EnvironmentObject private var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("隐藏").font(.system(size: 26, weight: .bold))
            Text("设置窗口收缩和显示方式").foregroundStyle(Color.kjMuted)
            Toggle("双击左侧导航空白处缩小到右侧", isOn: $model.doubleClickCollapseEnabled)
            Toggle("窗口置顶", isOn: Binding(get: { model.alwaysOnTop }, set: { model.alwaysOnTop = $0 }))
            Toggle("开机启动", isOn: Binding(get: { model.launchAtLogin }, set: { model.setLaunchAtLogin($0) }))
            Text(model.settingsMessage).font(.system(size: 13)).foregroundStyle(Color.kjMuted)
            Spacer()
        }
    }
}
struct PermissionSettings: View { @EnvironmentObject private var model: AppModel; var body: some View { VStack(alignment: .leading, spacing: 18) { Text("权限").font(.system(size: 26, weight: .bold)); Text("截图、剪贴板和辅助功能权限由 macOS 管理，已授权后不会重复申请。若系统状态未刷新，请重新打开设置页。" ).foregroundStyle(Color.kjMuted); ActionButton(title: "打开隐私与安全性", icon: "lock.shield", filled: true) { model.openSystemSettings("Privacy_ScreenCapture") }; ActionButton(title: "打开辅助功能", icon: "accessibility", filled: false) { model.openSystemSettings("Privacy_Accessibility") }; Spacer() } } }
struct UpdateSettings: View {
    @EnvironmentObject private var updater: UpdateManager
    @State private var message = "已接入 Sparkle 更新框架"
    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text("更新").font(.system(size: 26, weight: .bold))
            Text("快剪会使用原有更新地址检查新版本；用户数据仍保留在本机。正式发布前需要使用 Developer ID 签名并上传经过公证的安装包。" ).foregroundStyle(Color.kjMuted)
            ActionButton(title: "检查更新", icon: "arrow.triangle.2.circlepath", filled: true) { updater.checkForUpdates(); message = "已发起更新检查" }
            Text(message).foregroundStyle(Color.kjMuted)
            Spacer()
        }
    }
}
struct BasicSettings: View { let title: String; var body: some View { VStack(alignment: .leading, spacing: 18) { Text(title).font(.system(size: 26, weight: .bold)); Text("此设置页已保留入口，具体功能会继续接入现有本地数据和服务。").foregroundStyle(Color.kjMuted); Spacer() } } }
