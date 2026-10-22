"""Verify macOS app scripts without touching the user's apps or backup state."""
import json
import os
from pathlib import Path
import plistlib
import shutil
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
with tempfile.TemporaryDirectory(prefix='armor macos ') as temp:
    temp = Path(temp)
    repo = temp / 'repo'
    shutil.copytree(root / 'scripts', repo / 'scripts')
    shutil.copy(root / 'go.sh', repo / 'go.sh')
    (repo / 'src').mkdir()
    shutil.copy(root / 'src/Directory.Build.props', repo / 'src/Directory.Build.props')
    (repo / 'assets').mkdir()
    shutil.copy(root / 'assets/logo.png', repo / 'assets/logo.png')
    bin_dir = temp / 'bin'
    bin_dir.mkdir()
    log = temp / 'calls'
    fake = '''#!/usr/bin/env python3
import json, os, pathlib, sys
name = pathlib.Path(sys.argv[0]).name
args = sys.argv[1:]
with open(os.environ['CALL_LOG'], 'a') as f: f.write(json.dumps([name, *args]) + '\\n')
if name == 'uname': print('Darwin' if args == ['-s'] else os.environ.get('TEST_ARCH', 'arm64'))
if name == 'pgrep': sys.exit(1)
if name == 'launchctl' and args[0] == 'print': sys.exit(1)
if name == 'codesign' and os.environ.get('FAIL_SIGN'): sys.exit(8)
if name == 'dotnet':
    if os.environ.get('FAIL_PUBLISH'): sys.exit(7)
    out = pathlib.Path(args[args.index('-o') + 1])
    out.mkdir(parents=True, exist_ok=True)
    exe = out / pathlib.Path(args[1]).stem
    exe.write_text('#!/bin/sh\\nexit 0\\n')
if name == 'iconutil': pathlib.Path(args[args.index('--output') + 1]).touch()
'''
    for name in ['uname', 'dotnet', 'sips', 'iconutil', 'codesign', 'launchctl', 'pgrep', 'pkill', 'git', 'open']:
        path = bin_dir / name
        path.write_text(fake)
        path.chmod(0o755)
    env = dict(os.environ, HOME=str(temp / 'home'), PATH=f'{bin_dir}:{os.environ["PATH"]}', CALL_LOG=str(log))

    def run(script, *args, **updates):
        log.unlink(missing_ok=True)
        p = subprocess.run(['bash', str(repo / script), *args], env=dict(env, **updates), capture_output=True, text=True)
        calls = [json.loads(line) for line in log.read_text().splitlines()]
        return p, calls

    for script in ['scripts/macos/install.sh', 'scripts/macos/reinstall.sh', 'scripts/macos/run.sh', 'go.sh', 'scripts/macos/update.sh']:
        for tfm, arch in [('net8.0', 'x86_64'), ('net10.0', 'arm64')]:
            p, calls = run(script, tfm, TEST_ARCH=arch)
            assert p.returncode == 0, p.stderr
            app = temp / 'home/Applications/Armor.app'
            with (app / 'Contents/Info.plist').open('rb') as f:
                plist = plistlib.load(f)
            assert plist['CFBundleExecutable'] == 'Armor.Agent' and plist['LSUIElement']
            for component in ['Armor.Agent', 'Armor.Tui']:
                assert os.access(app / f'Contents/MacOS/{component}', os.X_OK)
            publishes = [c for c in calls if c[:2] == ['dotnet', 'publish']]
            assert len(publishes) == 2
            for publish in publishes:
                assert publish[publish.index('-f') + 1] == tfm
                assert publish[publish.index('-r') + 1] == ('osx-arm64' if arch == 'arm64' else 'osx-x64')
    marker = app / 'keep-me'
    marker.touch()
    for failure in ['FAIL_PUBLISH', 'FAIL_SIGN']:
        p, calls = run('scripts/macos/reinstall.sh', **{failure: '1'})
        assert p.returncode != 0 and marker.exists()
    p, calls = run('scripts/macos/run-at-startup.sh')
    assert p.returncode == 0, p.stderr
    plist = temp / 'home/Library/LaunchAgents/com.jchristn.armor.agent.plist'
    with plist.open('rb') as f:
        assert plistlib.load(f)['ProgramArguments'] == [str(app / 'Contents/MacOS/Armor.Agent')]
    print('Armor macOS install/reinstall/run/update, both architectures/frameworks, failure preservation, and login path passed.')
