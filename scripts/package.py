"""Package a Jellyfin catalog ZIP and manifest; never uploads or publishes files."""
import argparse
import datetime
import hashlib
import io
import json
from pathlib import Path
import re
import tarfile
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
NODE_VERSION = '22.23.3'
NODE_SHA256 = 'df450af89261115ef9f9e3830c3eeb2cc9213b63c720b1af623cb5dcbe2e02de'
GUID = '70a81c22-b61d-4bdd-bd86-2048e0b29a5a'

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', help='GitHub owner/repo; assets point to its versioned Release')
    parser.add_argument('--base-url', help='Override asset base URL for isolated catalog tests')
    parser.add_argument('--node-archive', type=Path, help='Use an already downloaded, checksum-verified archive')
    args = parser.parse_args()
    if not args.repository and not args.base_url: parser.error('Supply --repository or --base-url')
    if args.repository and not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', args.repository): parser.error('Invalid owner/repo')
    version = ET.parse(ROOT / 'src/Jellyfin.Plugin.AutoLut.csproj').findtext('.//Version')
    tag = 'v' + version
    assembly_version = version + '.0'
    base_url = args.base_url or f'https://github.com/{args.repository}/releases/download/{tag}'
    if not base_url.startswith(('http://', 'https://')): parser.error('Asset URL must be HTTP(S)')
    dll = ROOT / 'src/bin/Release/net10.0/Jellyfin.Plugin.AutoLut.dll'
    if not dll.is_file(): raise SystemExit('Build Release first')
    archive = args.node_archive or ROOT / '.build' / f'node-v{NODE_VERSION}-linux-x64.tar.xz'
    if not archive.exists():
        archive.parent.mkdir(parents=True, exist_ok=True)
        with urllib.request.urlopen(f'https://nodejs.org/dist/v{NODE_VERSION}/node-v{NODE_VERSION}-linux-x64.tar.xz', timeout=120) as response:
            archive.write_bytes(response.read())
    data = archive.read_bytes()
    if hashlib.sha256(data).hexdigest() != NODE_SHA256: raise SystemExit('Node SHA256 mismatch')
    dist = ROOT / 'dist'; dist.mkdir(exist_ok=True)
    name = f'AutoLut_{assembly_version}-linux-x64.zip'
    output = dist / name
    entries = {'Jellyfin.Plugin.AutoLut.dll': dll.read_bytes(), 'README.md': (ROOT / 'README.md').read_bytes(),
               'THIRD_PARTY_NOTICES.md': (ROOT / 'THIRD_PARTY_NOTICES.md').read_bytes()}
    for file in ('core.cjs', 'lut.cjs', 'provenance.json'): entries['worker/' + file] = (ROOT / 'worker' / file).read_bytes()
    # Extract only named regular files from the hash-verified archive, never its directory tree.
    with tarfile.open(fileobj=io.BytesIO(data), mode='r:xz') as tar:
        for source, target in [('bin/node', 'runtime/node'), ('LICENSE', 'runtime/Node-LICENSE.txt')]:
            member = tar.getmember(f'node-v{NODE_VERSION}-linux-x64/' + source)
            if not member.isfile(): raise SystemExit('Unexpected Node archive entry')
            entries[target] = tar.extractfile(member).read()
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as zip:
        for name, content in sorted(entries.items()):
            info = zipfile.ZipInfo(name); info.create_system = 3
            info.external_attr = (0o100755 if name == 'runtime/node' else 0o100644) << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            zip.writestr(info, content)
    content = output.read_bytes()
    sha256 = hashlib.sha256(content).hexdigest()
    # Jellyfin 12.1's InstallationManager requires MD5 in the catalog. Supply SHA256 separately too.
    md5 = hashlib.md5(content, usedforsecurity=False).hexdigest()
    output.with_suffix('.zip.sha256').write_text(f'{sha256}  {output.name}\n', encoding='ascii')
    manifest = [{'guid': GUID, 'name': 'Auto LUT (Preview)',
        'description': 'Experimental SDR automatic LUT. Linux x64, Jellyfin 12.1, CPU LUT with QSV encoding. Disabled by default.',
        'overview': 'Session-scoped automatic or fixed LUT for explicitly selected users and media.',
        'owner': args.repository.split('/')[0] if args.repository else 'Local preview', 'category': 'General',
        'versions': [{'version': assembly_version, 'changelog': 'Use tetrahedral CPU LUT interpolation with float precision; fix missing controls and stale playback positions when Jellyfin caches player pages. Refresh the web client after updating.',
            'targetAbi': '12.1.0.0', 'sourceUrl': base_url.rstrip('/') + '/' + output.name, 'checksum': md5,
            'timestamp': datetime.datetime.now(datetime.timezone.utc).isoformat()}]}]
    (dist / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print(json.dumps({'artifact': output.name, 'sha256': sha256, 'catalog_md5': md5, 'files': sorted(entries)}, indent=2))

if __name__ == '__main__': main()
