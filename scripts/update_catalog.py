"""Publish a verified Release manifest to the fixed main-branch catalog."""
import base64
import json
import os
from pathlib import Path
import re
import urllib.error
import urllib.request


def merge_catalog(current, incoming):
    if len(incoming) != 1 or not incoming[0].get('versions'):
        raise ValueError('Expected one plugin with released versions')
    plugin = dict(incoming[0])
    existing = next((p for p in current if p['guid'] == plugin['guid']), None)
    versions = {v['version']: v for v in (existing or {}).get('versions', [])}
    versions.update({v['version']: v for v in plugin['versions']})
    if not all(re.fullmatch(r'\d+\.\d+\.\d+\.\d+', v) for v in versions):
        raise ValueError('Unexpected plugin version')
    plugin['versions'] = sorted(versions.values(), key=lambda v: tuple(map(int, v['version'].split('.'))), reverse=True)
    return [plugin] + [p for p in current if p['guid'] != plugin['guid']]


def main():
    repo, tag = os.environ['GH_REPO'], os.environ['TAG']
    if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repo) or not re.fullmatch(r'v\d+\.\d+\.\d+', tag):
        raise ValueError('Invalid repository or tag')
    token = os.environ['GH_TOKEN']

    def call(path, data=None):
        req = urllib.request.Request('https://api.github.com/repos/' + repo + path,
            None if data is None else json.dumps(data).encode(),
            {'Authorization': 'Bearer ' + token, 'Accept': 'application/vnd.github+json',
             'User-Agent': 'AutoLut-catalog', 'X-GitHub-Api-Version': '2022-11-28'},
            method='GET' if data is None else 'PUT')
        with urllib.request.urlopen(req, timeout=60) as response:
            return json.load(response)

    incoming = json.loads(Path('dist/manifest.json').read_text(encoding='utf-8'))
    release = call('/releases/tags/' + tag)
    if release['draft']:
        raise ValueError('Release must be published first')
    assets = {a['browser_download_url']: a for a in release['assets']}
    for plugin in incoming:
        for version in plugin['versions']:
            asset = assets.get(version['sourceUrl'])
            if version['version'] != tag[1:] + '.0' or not asset or asset['size'] <= 0:
                raise ValueError('Catalog must reference the published version and asset')
    for attempt in range(3):
        try:
            previous = call('/contents/manifest.json?ref=main')
        except urllib.error.HTTPError as error:
            if error.code != 404:
                raise
            previous = None
        current = json.loads(base64.b64decode(previous['content'])) if previous else []
        merged = merge_catalog(current, incoming)
        if merged == current:
            print('Catalog already up to date')
            return
        data = {'message': 'Update plugin catalog for ' + tag, 'branch': 'main',
                'content': base64.b64encode((json.dumps(merged, indent=2) + '\n').encode()).decode()}
        if previous:
            data['sha'] = previous['sha']
        try:
            call('/contents/manifest.json', data)
            print('Published fixed catalog for ' + tag)
            return
        except urllib.error.HTTPError as error:
            if error.code != 409 or attempt == 2:
                raise


if __name__ == '__main__':
    main()
