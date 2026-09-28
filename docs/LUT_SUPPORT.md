# Jellyfin / FFmpeg 的 LUT 能力

需要区分三层：

1. 色调映射算法内部的查找表。例如 HLG 曲线/亮度计算中的 LUT，用来加速某一步数学运算。
2. 自定义调色 LUT 文件。FFmpeg `lut3d` 支持 `.cube`；构建包含 libplacebo 时，`libplacebo=lut=...` 也能加载 `.cube`。
3. Jellyfin 播放集成。仍需决定什么用户/设备/媒体启用、何时分析、如何让 Direct Play 进入视频转码、怎样保持播放会话和颜色信息正确。

本插件使用现成的第二层能力，实现第三层中的有限预览功能。

## PR #706

[jellyfin-ffmpeg #706](https://github.com/jellyfin/jellyfin-ffmpeg/pull/706) 讨论 HLG OOTF 与 tone-mapping 实现，其提出的内部 LUT 不等同于外部 `.cube` 调色接口。该 PR 页面显示 Closed；维护者讨论了现有色调映射方式并采用另外的修改方案，不能把该 PR 的全部提案当作已合并功能。

其中 2.6%～12.8% 是作者在指定转码设置下测得的新增开销，不是自定义 33³ LUT 或 Intel UHD 770 的性能结论。

## 依据

- [FFmpeg lut3d 文档](https://ffmpeg.org/ffmpeg-filters.html#lut3d)
- [FFmpeg libplacebo 文档](https://ffmpeg.org/ffmpeg-filters.html#libplacebo)
- [Jellyfin 插件仓库说明](https://jellyfin.org/posts/plugin-updates/)
- [Jellyfin 12.1 安装器源码](https://github.com/jellyfin/jellyfin/blob/ee91c75e777da41a9c4f4855e70adc604fbf2ef8/Emby.Server.Implementations/Updates/InstallationManager.cs)

API 与运行测试针对 Jellyfin 12.1，不能从此推断未来版本仍然兼容。
