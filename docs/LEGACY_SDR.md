# 隔行 / SMPTE 170M 兼容处理

## 范围

0.1.7 的 `EnableLegacySdr` 默认 false。策略仅增加：BT.709（或另行允许的缺失标记）隔行视频，以及三项标记全部为 `smpte170m` 的 8-bit SDR。后者要求偶数输入尺寸。HDR、10-bit、旋转、字幕及其他策略边界不变；不会修改源文件或共享媒体元数据。

`LutService` 冻结 `ConvertSmpte170m` / `Deinterlace`，`LutTranscodeManager` 按播放会话传给命令补丁；后台改设置不会改变已开始的会话。

## 命令处理

只接受既有 Linux VAAPI 硬解到 H.264 QSV 硬编的简单图。隔行计划要求恰好一个 `deinterlace_vaapi=rate=frame` 或 `rate=field`，且位于缩放之前；不改变 Jellyfin 的场序和输出帧率。不会注入第二个去隔行滤镜，也不会把连续视频回退成 CPU 去隔行。

新增兼容分支的 `scale_vaapi` 只接受已验证命令形式中的 NV12 / 数字尺寸 / extra_hw_frames；拒绝未知色彩转换参数、缺失或重复去隔行、p010、复杂滤镜图等。拒绝时原命令不变。

SMPTE 170M 分支移除硬件前缀中提前写入的 BT.709 标记，保持源色彩声明，下载帧后执行：

```text
format=nv12,format=yuv420p,
colorspace=iall=smpte170m:all=bt709:format=yuv444p10:fast=0,
scale=in_color_matrix=bt709,format=gbrpf32le,
lut3d=file=SESSION.cube:interp=tetrahedral,
scale=out_color_matrix=bt709,format=nv12,
setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709,
hwupload=extra_hw_frames=24,format=qsv
```

`yuv444p10` 仅为色彩转换的内部精度，不表示支持 10-bit 原片。完整转换包括源矩阵、传递曲线、色域与目标矩阵；`fast=0` 保留色域和曲线处理。`setparams` 仅用于正确声明各阶段的色彩，不能替代转换。

分析单帧使用 CPU `bwdif=mode=send_frame:parity=auto:deint=all` 后接同一色彩转换，再缩放为分析尺寸。这一低频抽帧不改变连续播放的核显去隔行。

## 验证依据

- [FFmpeg colorspace 文档](https://ffmpeg.org/ffmpeg-filters.html#colorspace)：完整色彩转换、偶数尺寸要求和 fast 模式区别。
- [FFmpeg setparams 文档](https://ffmpeg.org/ffmpeg-filters.html#setparams)：声明帧参数，不转换像素。
- [FFmpeg colorspace 实现](https://github.com/FFmpeg/FFmpeg/blob/master/libavfilter/vf_colorspace.c)：170M/709 采用分段线性与幂函数传递曲线。
- [zimg 传递函数实现](https://github.com/sekrit-twc/zimg/blob/master/src/zimg/colorspace/gamma.cpp)：默认 display-referred 转换采用 BT.1886；不能直接当成上述分段曲线处理的等价像素参考。

`tests/LegacySdrTests.cs` 用独立标量 YCbCr/传递函数/原色矩阵公式核对肤色、饱和三原色、黑白灰的实际输出，容差为 8-bit Y/U/V 每通道 2 个码值。另验证 identity LUT、转换不同于重标记、移动隔行测试图、输出逐行与 BT.709 标记以及策略与会话边界。策略和命令结构测试无需 GPU，云端构建也会运行；真实像素测试需要 `AUTOLUT_TEST_FFMPEG` 指向含 colorspace/bwdif 的 jellyfin-ffmpeg。

## 目标 NAS 验收

完整硬件链路尚未在此版本上实测。本地结果仅证明 CPU 子图与命令构造，不证明 VAAPI 驱动输出像素或 QSV 实时性能。

1. 开启新选项，先选白名单内单路、无烧录字幕的 1080p 8-bit SDR；手动重新播放。
2. 日志必须同时有 `AutoLut applied` 和所需转换/去隔行后缀；FFmpeg 图保留 `deinterlace_vaapi`、`colorspace`、`lut3d`、`h264_qsv`。
3. 检查无黑绿屏、无明显色偏、运动无梳齿、音画同步和 seek；连续至少 10 分钟，建议稳定转码速度 ≥1.3×。
4. 查看 CPU 配额与负载、核显媒体引擎、掉帧和 FFmpeg 错误。逐场输出时处理帧数通常更高；4K / 多路需分别验收。
5. 异常时关闭本兼容选项并重新播放；默认逐行 BT.709 路径不受该选项影响。运行中的 FFmpeg 失败不会自动无缝重试。
