# Auto LUT 0.1.1 — Experimental preview

面向 Jellyfin 12.1 / Linux x64 的 SDR 自动 LUT 插件预发布。

- 加入 Jellyfin 图形安装用的仓库清单与 ZIP 布局。
- 包内 Node 运行时在需要时恢复本文件的用户执行权限。
- 保留用户/媒体/设备白名单、自动/固定 LUT、每次播放会话冻结 LUT。
- 安装后默认关闭。

当前使用 CPU LUT + H.264 QSV 编码，只接入简单 VAAPI 解码 → QSV 编码管线，最大 1080p SDR 8-bit。HDR/DV、10-bit、4K、复杂字幕等不在支持范围。自动模式当前使用中央区域肤色采样，没有人脸检测和播放中周期更新。

每次服务进程启动最多准备 8 个成功调色会话，之后普通播放；本版尚无会话自动回收。

验证记录：本版 47 项检查通过；本地隔离 Jellyfin 的正式仓库安装后端已验证下载校验、安装、重启加载和自动分析流程。目标 NAS 上最终 DLL 的 QSV/HLS 实播仍待验收，因此以预发布形式提供，不宣称生产可用或 4K 性能。

仓库地址固定到本次发布：

https://github.com/jiangbinzhe/jellyfin-plugin-autolut/releases/download/v0.1.1/manifest.json

添加后刷新插件目录，安装 Auto LUT (Preview) 并重启 Jellyfin。优先只允许一个测试用户和一部 SDR 媒体。
