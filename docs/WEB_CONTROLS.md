# 网页开关与媒体库选择 — 0.1.3 预发布版

用于安装测试。只适用于 Jellyfin Server 12.1 / Linux x64；目标 NAS 上的 QSV 像素与实播性能仍需验收。

## 安装后的使用

1. 安装预发布包并重启 Jellyfin 后刷新网页。原有插件 ZIP 布局未改变，按钮脚本嵌入 DLL。
2. 管理后台 → 插件 → Auto LUT：填写允许使用调色的用户 ID，勾选媒体库，保存并启用。
   用户 ID 不是登录用户名；在用户设置地址的 userId 参数中获取。设备 ID 可留空。
3. 媒体库选择覆盖库内电影和每一集；无需复制剧集 ID。单个媒体 ID 可留空，也可额外允许单部影片。
   用户必须匹配，媒体库与单媒体 ID 二者匹配任一即可；设备限制仍然有效。
4. 网页播放符合条件的视频时，设置齿轮旁出现「LUT：开／关」。点击后重新播放当前视频，通常会短暂缓冲。
   进度优先取播放器显示时间（精度为秒），无法读取时用服务端进度；保留音轨、主字幕、媒体源与队列顺序。
5. 暂停时可先选择开关，按钮显示“待续播”，保持暂停；继续播放后自动应用。再次点击可取消待续播选择；退出页面会放弃尚未应用的选择。同步播放、无法定位、连接未就绪或权限/格式不符合时会禁用按钮。
6. 重播期间状态查询可能短暂不可用，按钮保留最后选择并暂时禁用，不会因为一次查询失败消失。

「LUT：开」表示允许服务端尝试调色，不等于实际滤镜已经应用。无肤色、字幕烧录或不支持的转码图仍会跳过。
需在日志中确认 AutoLut applied cpu-lut-qsv，只有 prepared 或正在转码不足以证明调色生效。
测试服务端效果时请关闭原浏览器油猴调色脚本。

## 范围和限制

- 安装服务端插件后自动注入同服务器 /web/index.html 或 /web/ 响应，无需浏览器扩展、不改写网页文件。
- 独立托管的 Jellyfin Web、Android/Swiftfin 原生播放器不注入按钮；此版本不增加 App 原生开关。
- 默认显示网页按钮；后台可单独关闭，刷新页面后移除。卸载插件并重启、刷新后也会移除。
- 按钮只保存当前用户 + 浏览器设备 ID + 媒体 ID 的临时选择，服务器重启后恢复管理员规则；最多保存 512 个关闭选择。
- 同一浏览器多个标签可能共享设备和播放控制会话，测试时仅保留一个播放标签。
- 重播通过官方 WebSocket 播放控制完成，不热改正在运行的 FFmpeg。网络断开时保留错误提示，不宣称已应用。
- 播放队列中的重复媒体、超过 200 项的队列暂不支持切换；SyncPlay 不受干扰。
- 切换不是逐帧无缝 A/B，也不保证外挂第二字幕、特殊播放器设置、浏览器画中画状态完整保留。
- 大媒体库勾选不会突破 SDR BT.709 / 8-bit / 1080p 上限，也不会给用户授予原来没有的播放权限。
- 网页资源或反向代理缓存可能需要强制刷新；服务器的 BaseUrl 会保留。代理自定义 CSP 若禁止同源脚本，需要管理员检查代理规则。

## 实现与依据

- WebStartupFilter / WebInjection：仅缓存网页入口响应，禁用该请求的压缩/条件缓存后插入同源脚本，再计算内容长度；不处理视频流。
- WebController：登录认证，按用户、设备、客户端、版本匹配当前会话；拒绝他人、过时媒体与缺失参数；只向同一会话发送 PlayNow。
- WebPreferences / PlaybackFilter：网页 opt-out 在准备 LUT 前检查，保留管理员规则。媒体库归属使用 ILibraryManager.GetCollectionFolders。
- 旧会话仍由正常 PlaybackStopped 回收；不将 HLS 停止编码误当成播放结束。
- 官方源码检查：Server v12.1 `ee91c75e777da41a9c4f4855e70adc604fbf2ef8`，Web v12.1 `fae41f33eb7cd636a9ef68984adb82bb247a6e1b`。
  [服务器静态网页管线](https://github.com/jellyfin/jellyfin/blob/v12.1/Jellyfin.Server/Startup.cs)，
  [官方网页重播处理](https://github.com/jellyfin/jellyfin-web/blob/v12.1/src/scripts/serverNotifications.js)，
  [媒体库归属实现](https://github.com/jellyfin/jellyfin/blob/v12.1/Emby.Server.Implementations/Library/LibraryManager.cs)。

## 复现验证

运行 scripts/build.sh（需要 Node 22；可用 NODE 指定路径）。其中 tests/web-player.test.mjs 覆盖按钮可见性与暂停选择。完整抽帧验证需设置 AUTOLUT_TEST_FFMPEG 并在测试输出目录提供打包的 Node/worker。
tests/mobile-playback.py 增加媒体库包含、其他库排除、用户限制、空范围拒绝的 HTTP 用例。
tests/web-switch.mjs 使用 Node 22 和真实 HTTP/WebSocket，参数为 --credentials、--item、--fixture、--report，可用 --url 指定隔离回环地址。
测试账号与媒体须已被管理员选中，凭据 JSON 只用于临时本地账号，报告放 .build，不提交凭据。
该测试检查鉴权、设备隔离、重播参数、关闭后恢复 Direct Play、开启后重新准备、暂停保护，不代替 NAS GPU 画面验收。
