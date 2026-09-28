# 0.1.4 预发布版 — LUT 性能优化与网页按钮修复

- CPU LUT 改用四面体插值，保留 32 位浮点 RGB 中间精度和原 VAAPI/QSV 链路。
- 修复 Jellyfin Web 缓存多个播放器界面时，LUT 按钮被挂到隐藏旧页面的问题；切换滤镜读取当前界面的播放进度。
- 页面切换时重新挂载按钮，并定期补挂丢失的控件；保留暂停选择、续播应用及断连保护。
- 更新网页脚本版本参数，避免继续使用 0.1.3 缓存。

本地 WSL 合成画面测试中处理速度提高约 40%，同帧数 CPU 耗时减少约 45%。这是 CPU 格式转换和 LUT 测试，不代表目标 NAS 完整转码链路收益；真实影片画质、长期负载及并发仍需验收。

Release 编译无警告、无错误；86 项程序断言（含真实 FFmpeg 恒等 LUT 像素检查）、8 项网页脚本测试、4 项清单测试通过。网页缓存页面场景还通过独立浏览器 DOM 验证，未替代完整 Jellyfin 实播。

## 更新与测试

固定插件仓库地址不变：

https://raw.githubusercontent.com/jiangbinzhe/jellyfin-plugin-autolut/main/manifest.json

在 Jellyfin 检查插件更新并安装 **0.1.4.0** 后，重启 Jellyfin，刷新网页再开始播放。已有白名单配置继续使用。确认 FFmpeg 滤镜含 `interp=tetrahedral`；测试 LUT 开关、切换视频、暂停续播和进度是否正常。

适用范围仍为 Jellyfin 12.1、Linux x64、SDR BT.709、8-bit、最高 1080p。HDR、Dolby Vision、10-bit、4K 不在本版处理范围内；Android/Swiftfin 原生播放器仍无 LUT 按钮。
