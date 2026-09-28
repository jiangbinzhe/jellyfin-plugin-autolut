# Mobile request fixtures

These are synthetic, reduced capability profiles, not network captures or a claim about every device.
They retain the codec preference, HLS container and plain-text subtitle delivery differences under test.
Direct-play entries describe only the synthetic H.264/AAC MP4 sample. Device-specific codec conditions
and hardware detection are not reproduced. No user IDs, tokens, IP addresses or private media paths are stored.

Source pins checked 2026-09-28:

- Android 2.7.3: 9a5f9be6a76da47ca07733b982a12dbd0f75539b.
  [Profile](https://github.com/jellyfin/jellyfin-android/blob/v2.7.3/app/src/main/java/org/jellyfin/mobile/player/deviceprofile/DeviceProfileBuilder.kt),
  [Request](https://github.com/jellyfin/jellyfin-android/blob/v2.7.3/app/src/main/java/org/jellyfin/mobile/player/source/MediaSourceResolver.kt).
- Swiftfin 1.6.1: 5b794d2b7d4b54db89b041b8d6355461de7110db.
  [Swiftfin player](https://github.com/jellyfin/Swiftfin/blob/1.6.1/Shared/Objects/VideoPlayerType/VideoPlayerType%2BSwiftfin.swift),
  [Native player](https://github.com/jellyfin/Swiftfin/blob/1.6.1/Shared/Objects/VideoPlayerType/VideoPlayerType%2BNative.swift),
  [Request](https://github.com/jellyfin/Swiftfin/blob/1.6.1/Shared/Objects/MediaPlayerManager/MediaPlayerItem/MediaPlayerItem%2BBuild.swift).

Android enumerates the individual device's codecs. Swiftfin optionally advertises AV1/HEVC; fixtures
include these to exercise selecting the advertised H.264 alternative. Swiftfin forced Direct Play
has no transcoding profiles and is intentionally bypassed.

The HTTP test creates synthetic video, two audio tracks and SRT. It restores plugin configuration on
an explicitly disposable loopback server. It does not run an app, decode on a phone or test a GPU.
