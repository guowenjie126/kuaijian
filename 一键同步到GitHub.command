#!/bin/zsh

set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "$0")" && pwd)"
REMOTE_URL="https://github.com/guowenjie126/kuaijian.git"
REPO_SLUG="guowenjie126/kuaijian"
SERVER_HOST="ubuntu@124.221.169.159"
SERVER_KEY="$HOME/.ssh/jingronghao-erp-deploy"
SERVER_DOWNLOAD_DIR="/var/www/kuaijian/downloads"
SERVER_ENV_FILE="/etc/kuaijian/kuaijian.env"
PUBLIC_DOWNLOAD_BASE="https://kuaijian.jingronghao.com/downloads"

pause_before_exit() {
  echo
  read "?按回车关闭窗口…"
}

fail() {
  echo "❌ $1"
  pause_before_exit
  exit 1
}

wait_for_windows_release() {
  local version="$1"
  local commit_sha="$2"
  local run_id=""

  echo "正在等待 Windows GitHub Actions 任务启动…"
  for _ in {1..30}; do
    run_id="$(gh run list --repo "$REPO_SLUG" --workflow windows-release.yml --branch main --limit 10 --json databaseId,headSha --jq ".[] | select(.headSha == \"$commit_sha\") | .databaseId" | head -n 1)"
    [[ -n "$run_id" ]] && break
    sleep 2
  done
  [[ -n "$run_id" ]] || fail "没有找到对应的 Windows GitHub Actions 任务，请打开 Actions 页面检查。"

  echo "正在等待 Windows v$version 构建完成…"
  gh run watch "$run_id" --repo "$REPO_SLUG" --exit-status || fail "Windows 构建失败，请打开 Actions 查看日志。"
}

deploy_windows_release() {
  local version="$1"
  local notes="$2"
  local file_name="KuaiJian-$version-Windows-x64-Setup.exe"
  local public_url="$PUBLIC_DOWNLOAD_BASE/$file_name"
  local temp_dir="$(mktemp -d "${TMPDIR:-/tmp}/kuaijian-win-release.XXXXXX")"

  [[ -f "$SERVER_KEY" ]] || fail "找不到腾讯云 SSH 私钥：$SERVER_KEY"

  echo "正在下载 GitHub Release 安装包…"
  gh release download "win-v$version" \
    --repo "$REPO_SLUG" \
    --pattern "$file_name" \
    --dir "$temp_dir" \
    --clobber || fail "无法下载 GitHub Release 安装包。"

  local installer_path="$temp_dir/$file_name"
  [[ -f "$installer_path" ]] || fail "GitHub Release 中没有找到 $file_name。"
  local sha256="$(shasum -a 256 "$installer_path" | awk '{print $1}')"
  local notes_b64="$(printf '%s' "$notes" | base64 | tr -d '\n')"

  echo "正在上传 Windows 安装包到腾讯云官网…"
  scp -q -i "$SERVER_KEY" -o BatchMode=yes -o ConnectTimeout=15 \
    "$installer_path" "$SERVER_HOST:/tmp/$file_name" \
    || fail "Windows 安装包上传腾讯云失败。"

  ssh -i "$SERVER_KEY" -o BatchMode=yes -o ConnectTimeout=15 "$SERVER_HOST" \
    "sudo -n install -o ubuntu -g ubuntu -m 0644 /tmp/$file_name $SERVER_DOWNLOAD_DIR/$file_name.uploading; \
     sudo -n mv -f $SERVER_DOWNLOAD_DIR/$file_name.uploading $SERVER_DOWNLOAD_DIR/$file_name; \
     sudo -n install -o ubuntu -g ubuntu -m 0644 /tmp/$file_name $SERVER_DOWNLOAD_DIR/KuaiJian-Windows-x64-Setup.exe.uploading; \
     sudo -n mv -f $SERVER_DOWNLOAD_DIR/KuaiJian-Windows-x64-Setup.exe.uploading $SERVER_DOWNLOAD_DIR/KuaiJian-Windows-x64-Setup.exe; \
     sudo -n env KUAJIAN_VERSION=$version KUAJIAN_UPDATE_URL=$public_url KUAJIAN_SHA256=$sha256 KUAJIAN_NOTES_B64=$notes_b64 python3 - <<'PY'
from pathlib import Path
import base64
import os

path = Path('$SERVER_ENV_FILE')
values = {
    'KUAIJIAN_WINDOWS_UPDATE_VERSION': os.environ['KUAJIAN_VERSION'],
    'KUAIJIAN_WINDOWS_UPDATE_URL': os.environ['KUAJIAN_UPDATE_URL'],
    'KUAIJIAN_WINDOWS_UPDATE_SHA256': os.environ['KUAJIAN_SHA256'],
    'KUAIJIAN_WINDOWS_UPDATE_NOTES': base64.b64decode(os.environ['KUAJIAN_NOTES_B64']).decode('utf-8'),
}
lines = path.read_text().splitlines()
seen = set()
updated = []
for line in lines:
    key = line.split('=', 1)[0] if '=' in line else ''
    if key in values:
        updated.append(f'{key}={values[key]}')
        seen.add(key)
    else:
        updated.append(line)
for key, value in values.items():
    if key not in seen:
        updated.append(f'{key}={value}')
path.write_text('\\n'.join(updated) + '\\n')
PY
     sudo -n systemctl restart kuaijian-api.service; \
     sudo -n systemctl is-active kuaijian-api.service; \
     rm -f /tmp/$file_name" \
    || fail "腾讯云官网文件或更新接口部署失败。"

  rm -rf "$temp_dir"
  echo "✅ Windows v$version 已上传官网，更新接口已切换到新版本。SHA-256：$sha256"
}

cd "$ROOT_DIR" || fail "无法进入工程目录：$ROOT_DIR"

command -v git >/dev/null 2>&1 || fail "没有找到 Git，请先安装 Xcode Command Line Tools。"

current_branch="$(git branch --show-current)"
[[ "$current_branch" == "main" ]] || fail "当前分支是 $current_branch，不是 main；为避免误传，脚本已停止。"

configured_remote="$(git remote get-url origin 2>/dev/null || true)"
configured_remote="${configured_remote%.git}.git"
[[ "$configured_remote" == "$REMOTE_URL" ]] || fail "origin 不是预期的快剪 GitHub 仓库：$REMOTE_URL"

echo "正在检查 GitHub 最新状态…"
git fetch origin main || fail "无法访问 GitHub。请先完成 GitHub 登录/凭据配置后再重试。"

remote_before="$(git rev-parse origin/main)"

if ! git merge-base HEAD origin/main >/dev/null 2>&1; then
  fail "本地 Git 历史与 GitHub 历史不一致。请先用 GitHub Desktop 克隆仓库并迁移本地源码，脚本不会强制覆盖远程。"
fi

if ! git diff --quiet || ! git diff --cached --quiet || [[ -n "$(git status --porcelain)" ]]; then
  echo "正在检查本地改动…"
  git diff --check || fail "发现空白字符错误，请先修复。"
  git add -A
  git diff --cached --check || fail "暂存内容存在空白字符错误，请先修复。"

  if ! git diff --cached --quiet; then
    commit_message="sync: mac local changes $(date '+%Y-%m-%d %H:%M')"
    git commit -m "$commit_message" || fail "本地提交失败。"
  fi
else
  echo "没有检测到新的本地代码修改。"
fi

echo "正在合并 GitHub 最新提交…"
git pull --rebase --autostash origin main || fail "同步发生冲突或拉取失败。请人工处理冲突后再运行本脚本。"

echo "正在上传到 GitHub…"
git push origin main || fail "推送失败。请检查 GitHub 登录状态和网络。"

remote_after="$(git rev-parse HEAD)"
changed_paths="$(git diff --name-only "$remote_before" "$remote_after" || true)"

echo
windows_version=""
windows_notes=""
if print -r -- "$changed_paths" | grep -q '^kuaijian-winui/'; then
  echo "✅ 检测到 Windows 工程改动。GitHub Actions 将自动构建 Windows 安装包。"
  echo "   注意：如果 Windows 版本号没有增加，Actions 会跳过已经发布过的版本。"
  windows_version="$(sed -nE 's/.*MyAppVersion "([0-9]+\.[0-9]+\.[0-9]+)".*/\1/p' kuaijian-winui/installer.iss | head -n 1)"
  windows_notes="Windows x64 自动构建版本 $windows_version"
else
  echo "✅ 本次没有修改 Windows C# 工程。Mac 代码已上传，但不会自动改写 Windows 代码。"
  echo "   Mac Swift 与 Windows C# 是两个独立工程，需要分别实现同一个功能。"
fi

if command -v gh >/dev/null 2>&1; then
  if gh auth status >/dev/null 2>&1; then
    if [[ -z "$windows_version" ]]; then
      echo
      read "windows_version?如需同时发布 Windows 新版本，请输入版本号（例如 0.4.2，直接回车跳过）： "
      if [[ -n "$windows_version" ]]; then
        [[ "$windows_version" =~ '^[0-9]+\.[0-9]+\.[0-9]+$' ]] || fail "Windows 版本号必须是 major.minor.patch，例如 0.4.2。"
        read "windows_notes?Windows 更新说明（可直接回车）： "
        windows_notes="${windows_notes:-Windows x64 自动构建版本 $windows_version}"
        gh release view "win-v$windows_version" --repo "$REPO_SLUG" >/dev/null 2>&1 && fail "Windows v$windows_version 已经发布，请换一个新版本号。"
        gh workflow run .github/workflows/windows-release.yml \
          --repo "$REPO_SLUG" \
          --ref main \
          -f "version=$windows_version" \
          -f "notes=$windows_notes" \
          || fail "Windows 发布任务触发失败。"
      fi
    else
      gh release view "win-v$windows_version" --repo "$REPO_SLUG" >/dev/null 2>&1 && {
        echo "Windows v$windows_version 已经发布；请先提高 installer.iss 中的版本号。"
        windows_version=""
      }
    fi

    if [[ -n "$windows_version" ]]; then
      wait_for_windows_release "$windows_version" "$remote_after"
      deploy_windows_release "$windows_version" "$windows_notes"
    fi
  else
    echo "提示：GitHub CLI 尚未登录，跳过手动触发 Windows 发布。"
  fi
else
  echo "提示：未安装 GitHub CLI，跳过手动触发 Windows 发布。"
  echo "如需手动触发 Windows 发布，可安装 gh 后执行：gh auth login"
fi

echo
echo "✅ Mac 代码已同步到 GitHub：$REMOTE_URL"
open "https://github.com/guowenjie126/kuaijian" >/dev/null 2>&1 || true
open "https://github.com/$REPO_SLUG/actions" >/dev/null 2>&1 || true
pause_before_exit
