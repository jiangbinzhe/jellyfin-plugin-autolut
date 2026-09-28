# Third-party notices

The Linux x64 release archive bundles the official Node.js 22.23.3 binary.
Its complete license and bundled component notices are included in
`runtime/Node-LICENSE.txt`. Source and release information:
https://nodejs.org/dist/v22.23.3/

The plugin builds against Jellyfin.Controller and Jellyfin.Model 12.1.0.
Jellyfin assemblies are provided by the server and are not bundled in this
plugin archive. Upstream: https://github.com/jellyfin/jellyfin

`worker/core.cjs` and `worker/lut.cjs` derive from the project-provided browser
LUT userscript version 6.7.5. The original content hash and extraction ranges
are recorded in `worker/provenance.json`. No additional project-wide license
grant is asserted by this notice.
