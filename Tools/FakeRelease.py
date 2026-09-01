"""Stand up a release on localhost, so the updater can be run end to end.

Publishing a release to try the updater is a bad trade: it is public the moment
it exists, the version number is spent whether or not it worked, and the one
thing you want to watch — an editor replacing itself — is the one thing you
cannot undo from GitHub.

So this builds one locally. It compiles the editor at a version of your
choosing, zips it exactly as a release ships it, writes the JSON the GitHub API
would answer with, and serves all of it over HTTP. Point the editor at it with
one environment variable and everything after that is the real path: the real
download, the real unpack, the real swap, the real restart.

    python Tools/FakeRelease.py --version 9.9.9

Then, in another terminal, from the INSTALLED editor's folder:

    set SMSMODFORGE_UPDATE_FEED=http://127.0.0.1:8099/release.json
    SMSModForge.exe

It should offer 9.9.9, install it, restart, and come back up with 9.9.9 in the
title bar. To go back, unpack the release zip you were on over the folder.

Add --plugin <path to a plugin zip> to exercise the game-folder half as well;
without it the release simply has no plugin asset, which the editor is expected
to handle and says so in the prompt.
"""

import argparse
import functools
import http.server
import io
import json
import os
import shutil
import socketserver
import subprocess
import sys
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
PROJECT = os.path.join(REPO, 'SMSModForge', 'SMSModForge.csproj')


def build(version, out_dir):
    """Build the editor stamped with `version`, into out_dir."""
    print('building %s ...' % version)
    if os.path.isdir(out_dir):
        shutil.rmtree(out_dir)

    result = subprocess.run(
        ['dotnet', 'publish', PROJECT,
         '-c', 'Release',
         '--nologo', '-v', 'quiet',
         '-p:Version=%s' % version,
         '-p:AssemblyVersion=%s.0' % version,
         '-p:FileVersion=%s.0' % version,
         # Framework-dependent, same as what is shipped: the readme names the
         # .NET Desktop Runtime as a prerequisite, and a self-contained build
         # here would be testing a different thing from the one that ships.
         '--self-contained', 'false',
         '-o', out_dir],
        cwd=REPO)
    if result.returncode != 0:
        sys.exit('build failed')

    exe = os.path.join(out_dir, 'SMSModForge.exe')
    if not os.path.exists(exe):
        sys.exit('built, but there is no SMSModForge.exe in ' + out_dir)
    return out_dir


def zip_folder(folder, zip_path):
    """Zip a folder flat, the way the released editor zip is packed."""
    print('packing %s ...' % os.path.basename(zip_path))
    count = 0
    with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as z:
        for root, _dirs, files in os.walk(folder):
            for name in files:
                full = os.path.join(root, name)
                z.write(full, os.path.relpath(full, folder))
                count += 1
    print('   %d files, %.1f MB' % (count, os.path.getsize(zip_path) / 1024 / 1024))


def write_feed(serve_dir, version, base_url, editor_zip, plugin_zip):
    """The subset of GitHub's release JSON the editor reads."""
    assets = []

    def asset(path):
        assets.append({
            'name': os.path.basename(path),
            'size': os.path.getsize(path),
            'browser_download_url': base_url + '/' + os.path.basename(path),
        })

    asset(editor_zip)
    if plugin_zip:
        asset(plugin_zip)

    feed = {
        'tag_name': 'v%s' % version,
        'name': 'ModForge %s' % version,
        'body': (
            '### This is not a real release\r\n\r\n'
            '- Built by Tools/FakeRelease.py to try the updater end to end.\r\n'
            '- The notes box you are reading is the release body, shown as '
            'written.\r\n'
            '- Installing this really will replace your editor with a build '
            'stamped %s.\r\n' % version
        ),
        'assets': assets,
    }

    path = os.path.join(serve_dir, 'release.json')
    with io.open(path, 'w', encoding='utf-8') as f:
        json.dump(feed, f, indent=2)
    return path


def serve(directory, port):
    handler = functools.partial(http.server.SimpleHTTPRequestHandler, directory=directory)

    class Reusable(socketserver.TCPServer):
        allow_reuse_address = True

    with Reusable(('127.0.0.1', port), handler) as httpd:
        print()
        print('serving %s on http://127.0.0.1:%d' % (directory, port))
        print()
        print('  set %s=http://127.0.0.1:%d/release.json'
              % ('SMSMODFORGE_UPDATE_FEED', port))
        print()
        print('then start the installed editor from that same terminal.')
        print('ctrl-c here when you are done.')
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print('\nstopped.')


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--version', default='9.9.9',
                    help='version to build and offer (default 9.9.9)')
    ap.add_argument('--port', type=int, default=8099)
    ap.add_argument('--plugin', default=None,
                    help='an existing plugin zip to offer as the plugin asset')
    ap.add_argument('--no-build', action='store_true',
                    help='reuse the last build in the work folder')
    args = ap.parse_args()

    work = os.path.join(REPO, 'obj', 'fake-release')
    serve_dir = os.path.join(work, 'serve')
    build_dir = os.path.join(work, 'build')
    os.makedirs(serve_dir, exist_ok=True)

    if not args.no_build:
        build(args.version, build_dir)
    elif not os.path.isdir(build_dir):
        sys.exit('nothing to reuse: run once without --no-build')

    editor_zip = os.path.join(serve_dir,
                              'Starmaker - ModForge Editor %s.zip' % args.version)
    zip_folder(build_dir, editor_zip)

    plugin_zip = None
    if args.plugin:
        if not os.path.exists(args.plugin):
            sys.exit('no such plugin zip: ' + args.plugin)
        plugin_zip = os.path.join(serve_dir,
                                  'Starmaker - ModForge Plugin %s.zip' % args.version)
        shutil.copyfile(args.plugin, plugin_zip)

    write_feed(serve_dir, args.version, 'http://127.0.0.1:%d' % args.port,
               editor_zip, plugin_zip)
    serve(serve_dir, args.port)


if __name__ == '__main__':
    main()
