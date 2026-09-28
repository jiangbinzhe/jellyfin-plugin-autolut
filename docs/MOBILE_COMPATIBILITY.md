# Android 与 Swiftfin — 0.1.2 预发布版

目标为 Jellyfin Android 手机/平板 2.7.3 内置播放器，以及 Swiftfin 1.6.1 的 Swiftfin/VLC 和 Native/AVPlayer 模式。
Jellyfin Mobile iOS 和 Android TV 不属于此次验收范围。服务端仍锁定 Jellyfin 12.1 / Linux x64。

## 实现

- 仅当客户端声明支持 Streaming + HLS + H.264 时启用 LUT，不伪造客户端能力。
- 克隆本次请求的 DeviceProfile，将视频转码候选限制为 H.264，保留客户端 TS/fMP4 容器、音频能力、字幕和分段参数。
- 静态下载或只有 Direct Play 能力的请求不强制转码。
- 对普通 SRT/SubRip、VTT/WebVTT、TTML 字幕预检查；协商后确认选中字幕采用 External 或 HLS 单独交付。
  External 必须有交付地址。ASS、图片字幕、烧录字幕不在支持范围。
- 保留音轨选择、码率上限、起播位置与播放会话，不改 HLS 输出缓存路径。
- 自动分析优先使用客户端 StartTimeTicks；未提供时读取当前授权用户的已保存进度。
  Swiftfin 不一定提供起播时间，服务端保存进度可能稍有延迟，不能保证逐帧精确的当前位置。
- 成功处理 Sessions/Playing/Stopped 后，校验用户、设备、媒体和 PlaySessionId，再释放额度。
  HLS 停止编码可能用于 seek，不会因此删除会话。
- 已关闭会话的 LUT 文件暂不删除，避免与正在结束的 FFmpeg 竞争，由停播后的缓存维护清理。

## 当前限制

仍只处理最高 1080p、明确 BT.709、8-bit、逐行 SDR；不支持 HDR10、HLG、Dolby Vision、4K、多版本源、复杂滤镜图。
视频调色仍是 CPU lut3d，加 VAAPI 硬解和 H.264 QSV 编码。

播放器控制栏没有新增 LUT 按钮。开关在服务器插件设置中，改变后需结束并重新开始播放。

默认最多 8 个未关闭调色会话。断网、客户端未报告结束，或仅停止编码的音轨切换可能留下未关闭会话并占用额度。
达到上限后新请求保留普通播放；重启可恢复额度。此版尚未解决所有异常退出/音轨切换的会话回收。

## 本地复现

先构建，将包安装到仅监听回环地址的独立 Jellyfin 12.1 测试实例，确认加载 0.1.2.0。
准备测试用户和包含 username/password 的 JSON 凭据文件，不要提交凭据。
tests/mobile-playback.py 的参数为 --base-url、--credentials、--media-directory、--ffmpeg、--report。
base-url 仅允许回环地址；report 建议放在忽略的 .build/mobile-http.json。
测试保留合成视频、测试媒体库和 LUT 供诊断，并恢复插件配置。
样本为精简能力配置，不是手机抓包，来源见 tests/fixtures/README.md。

## 真机验收（尚未完成）

1. 选一个测试用户、设备及无字幕的 720p/1080p SDR 视频，先用固定 LUT 便于观察明显差异。
2. Android 选择内置播放器；Swiftfin 使用 Auto 或 Most Compatible，分别测试 Swiftfin 和 Native。
   强制 Direct Play 不提供服务端处理需要的转码能力，会跳过 LUT。
3. 确认出现 AutoLut applied cpu-lut-qsv；只有 prepared 或“正在转码”不代表 LUT 已应用。
4. 各模式连续播放 10 分钟，检查画面、音画同步、暂停/恢复、拖动进度、横竖屏。
5. 测试外挂 SRT/VTT、切换音轨、调整码率、退出续播，确认字幕与调色仍生效。
6. 正常退出并重新播放至少 10 次，确认额度释放；异常关闭仍受上述限制。
7. 自动模式从有人物的时间点播放；无肤色、HDR、未选设备应保持普通播放。
8. 关闭插件并重新播放应恢复原画，记录 App 版本、机型、系统、播放器模式和脱敏日志。

只有真机项目通过后，才能标记相应客户端完成实播验收。
