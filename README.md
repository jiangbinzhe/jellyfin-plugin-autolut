# Jellyfin Auto LUT — Preview

为选定用户、设备和媒体自动生成调色 LUT，并接入 Jellyfin 的服务端转码流程。当前为 **0.1.3 预发布版**，默认关闭。

> 当前范围：Jellyfin 12.1、Linux x64、SDR BT.709、8-bit、最大 1080p；CPU LUT + QSV 编码。最终插件在实际 NAS 上的 QSV/HLS 实播尚待验收，不适合直接全库启用。

## 功能

- 必须匹配用户白名单，并匹配所选媒体库或单个媒体 ID；设备白名单可进一步限制。
- 0.1.3 设置页按名称勾选媒体库，覆盖库内电影和剧集。已有单媒体配置继续生效。
- 0.1.3 同服务器托管的网页播放器自动显示 LUT 开关，切换后重播以应用设置，详见 [网页开关与媒体库选择](docs/WEB_CONTROLS.md)。
- 自动模式抽取请求起点（未提供时用该用户保存进度）的一帧，最长边缩放到 640，分析中央区域肤色并生成 33³ LUT；也支持固定 `.cube` 文件。
- 无可用肤色或准备失败时保留普通播放。
- 生成成功后通过 PlaybackInfo 强制视频转码；每次播放会话冻结 LUT，避免不同用户或不同调色结果串用缓存。
- 随包附带 Node 运行时，无需在容器中另外安装 Node 或启动 sidecar。

## 移动客户端

0.1.2 针对 Android 手机/平板 2.7.3 内置播放器，以及 Swiftfin 1.6.1 的 Swiftfin 和 Native 模式补充服务端协商、独立文本字幕及播放结束额度回收。详情与实机验收见 [移动端兼容性](docs/MOBILE_COMPATIBILITY.md)。此预发布版尚未完成手机实播或 NAS QSV 验收；Android/Swiftfin 原生播放器内仍没有 LUT 按钮；0.1.3 新增的是网页播放器按钮。

## 安装

0.1.3 可在 Jellyfin 管理面板 → 插件 → 仓库中添加：

```text
https://github.com/jiangbinzhe/jellyfin-plugin-autolut/releases/download/v0.1.3/manifest.json
```

刷新插件目录，安装 **Auto LUT (Preview)**，然后重启 Jellyfin。该地址固定到此次预发布，便于手动选择升级；不是自动跟随最新版本的稳定仓库。

填写测试用户 ID，然后勾选媒体库或填写单个媒体 ID。选择媒体库时，单个媒体 ID 可留空。用户 ID 是 Jellyfin 内部 ID，不是登录用户名。固定 LUT 路径使用容器内绝对路径。

先选一部无字幕的 720p/1080p SDR 测试视频。自动模式在黑色片头或没有肤色时会跳过，可从有人物的时间点开始播放。

## 处理链和限制

```text
VAAPI 硬解 → VAAPI 缩放 → 下载帧 → RGB 浮点 CPU lut3d
           → NV12 → 上传 QSV → H.264 QSV 硬编
```

| 项目 | 当前行为 |
|---|---|
| HDR10 / HLG / Dolby Vision / 10-bit / 4K | 跳过 |
| 未明确标记 BT.709 色彩信息 | 跳过 |
| 直播、网络输入、多媒体源、多视频流、隔行、旋转视频 | 跳过 |
| 字幕 | 支持单独交付的 SRT/VTT/TTML；ASS、图片字幕、烧录字幕及复杂图跳过 |
| 客户端 | 带 DeviceProfile 的 POST PlaybackInfo，须声明 HLS/H.264；下载及强制 Direct Play 能力配置不被强制处理 |
| 自动分析 | 中央 50% 区域肤色分析；尚无 MediaPipe 人脸检测 |
| LUT 更新 | 每次播放生成一次，尚无播放中周期更新 |
| 会话额度 | 最多 8 个未关闭会话；正常播放结束后释放，异常退出或仅停止编码的切换可能继续占用 |
| 数据保留 | 抽帧原始数据用后删除；LUT 保存在 `/cache/autolut`，旧运行目录需在无相关播放时清理 |

当前 GPU/Vulkan 映射测试出现过绿屏和 10-bit 损坏，因此此版本没有全 GPU LUT 选项。后台关闭插件只影响新请求；已有会话保持冻结 LUT。0.1.3 网页按钮通过重新建立播放会话切换效果，会短暂缓冲；它不会修改管理员设置或绕过白名单。FFmpeg 实际编码失败时不自动重试，请关闭插件重新播放并检查日志。

## 验证状态

- 原 0.1.0：WSL .NET 10 编译、46 项策略/会话/真实抽帧与 Node 联动检查通过。
- 独立官方 Jellyfin 12.1：插件加载、配置页面和实际 PlaybackInfo 白名单/强制转码流程通过。
- 0.1.1：47 项检查通过；通过 Jellyfin 正式仓库安装后端完成下载校验、安装、重启加载及自动分析验证，详见 [验证记录](docs/VALIDATION.md)。
- 0.1.2：68 项完整本地检查和 13 个移动端 HTTP 协商场景通过；请求样本由客户端源码派生，并非真机实播。
- 0.1.3：83 项程序断言、5 个网页脚本测试、8 组网页 HTTP/WebSocket 检查、17 个移动端及媒体库 HTTP 场景通过；实际网页已验证暂停选择和媒体库勾选保存。
- 本地 WSL 缺少目标 Intel VAAPI/QSV 设备，不能代替 NAS 上的实际播放、持续负载和多用户验收。

## 编译与打包

需要 .NET SDK 10.0.401、Node 22、Python 3 和 Linux x64 环境。

```sh
bash scripts/build.sh
python3 scripts/package.py --repository jiangbinzhe/jellyfin-plugin-autolut
```

打包器下载并核验固定版本 Node 22.23.3；输出位于 `dist/`：插件 ZIP、SHA256 文件和 `manifest.json`。ZIP 中 DLL 位于根目录，Jellyfin 自行创建版本目录。清单里的 MD5 用于兼容 Jellyfin 12.1 的安装校验，同时提供 SHA256 供独立验证。

GitHub Actions 对提交和 PR 执行编译与检查；推送匹配版本号的 tag 后创建预发布 Release。Actions 不改 Jellyfin 部署。

## 验收与回退

1. 未匹配白名单的播放保持原行为。
2. 选定 SDR 视频应先出现 `AutoLut prepared`，实际转码时出现 `AutoLut applied cpu-lut-qsv`；只有 prepared 不表示 LUT 已应用。
3. 检查画面、音画同步和 seek，确认 FFmpeg 同时使用 `lut3d`、`h264_qsv`。
4. 连续播放至少 10 分钟，建议稳定速度不低于 1.3×，无持续掉帧或编码错误。
5. 出现问题先关闭插件并重新开始播放；彻底移除可通过 Jellyfin 插件管理卸载后重启。

## LUT 与 Jellyfin 的关系

FFmpeg 的 `lut3d` 和 `libplacebo` 已能读取自定义 `.cube`；本项目解决分析、启用策略和播放器转码接入。某些 tone-mapping PR 中提到的 LUT 是算法内部查找表，并不等同于用户导入 3D 调色 LUT。详见 [技术说明](docs/LUT_SUPPORT.md)。

## 源码与许可

`src/` 是 Jellyfin 集成层，`worker/` 是从项目提供的 v6.7.5 浏览器脚本提取的纯计算核心；来源校验值记录在 `worker/provenance.json`，没有附带本地文件路径。仓库当前未指定项目开源许可证。附带 Node 的许可证和第三方声明包含在安装包内，见 [第三方说明](THIRD_PARTY_NOTICES.md)。
