# 快剪双端工程

当前仓库用于维护快剪的 Mac 与 Windows 客户端。

## 工程结构

- `快剪原生版/`：macOS SwiftUI 客户端
- `kuaijian-winui/`：Windows WinUI 客户端（后续从 Windows 电脑上传）
- `kuaijian-server/`：Mac 与 Windows 共用的服务端（如存在）
- `docs/`：功能、接口和跨端开发说明

## 开发约定

- Mac 和 Windows 客户端可以分别开发、分别发布。
- 登录、剪贴板同步、设备和互传接口按同一份服务端协议维护。
- 构建产物和安装包不提交到源码仓库，安装包使用 GitHub Releases 发布。
- 不提交 `.env`、密钥、证书、Cookie、数据库和本地运行数据。
