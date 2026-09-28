# 0.1.5 预发布版 — Jellyfin Mobile 网页开关修复

- 修复状态接口只接受 `Jellyfin Web` 客户端名，导致 Jellyfin Mobile 网页播放器按钮停在“连接中”的问题。
- 按登录用户、设备、客户端和版本精确匹配唯一活动视频会话；继续执行白名单、转码权限、播放控制连接和暂停保护。
- 修复移动端关闭 LUT 的选择未被后续播放请求采用的问题。
- 状态查询失败时显示“未连接”和原因并继续重试；更新脚本缓存版本。

本地 Release 编译无警告、无错误；113 项程序断言（含真实 FFmpeg 像素检查）、9 项网页脚本测试、4 项清单测试通过。iOS 真机按钮切换、重播与进度保持仍需验收。

## 更新与验证

固定插件仓库地址不变：

https://raw.githubusercontent.com/jiangbinzhe/jellyfin-plugin-autolut/main/manifest.json

在 Jellyfin 后台安装 **0.1.5.0**，重启 Jellyfin，再完全退出并重新打开 Jellyfin Mobile，重新播放符合条件的视频。现有白名单继续使用。

确认网页播放器按钮能显示开/关；测试关闭、开启、暂停后选择并续播，检查进度与音轨。若显示“不可用”，按提示检查播放控制连接或权限；按钮显示“开”不等于实际滤镜已应用，需结合服务端转码日志确认。

适用范围仍为 Jellyfin 12.1、Linux x64、SDR BT.709、8-bit、最高 1080p。此修复针对加载服务器网页播放器的界面，不为 Swiftfin 或 App 原生播放器增加按钮，也不修复 Swiftfin 默认 VLC 播放器黑屏。
