"""Exercise the per-OS scripts in scripts/{linux,macos,windows,wsl,wsl2} with fake platform and build tools.

Run with: python3 scripts/tests/test-scripts.py

Checks the same-set rule (all five directories, Windows included), the OS guards, argument handling,
the install/reinstall/uninstall/kill rules, update/run stop-and-restart, and the login registration
files each platform writes (systemd unit, desktop entry, launchd plist, Windows Startup launcher for
WSL). Nothing real is built, installed or killed: dotnet, uname, pgrep, pkill, systemctl, launchctl,
git, open, sips, iconutil, codesign, cmd.exe, wslpath and wscript.exe are fakes that log their
arguments and keep process/service state in a temporary directory, and the scripts run against a
temporary copy of the repository with a temporary HOME. WSL scripts run with WSL_SKIP_CHECK=1 (except
the guard tests). The .bat files cannot run here; they get static checks (CRLF, setlocal, exit codes,
help and argument handling, no calls into other scripts). Also runs bash -n on every .sh, and
shellcheck when SHELLCHECK points at it or it is on PATH.
"""
import json
import os
import plistlib
import re
import shutil
import subprocess
import tempfile
from pathlib import Path

root = Path(__file__).resolve().parents[2]
UNIX = ['linux', 'macos', 'wsl', 'wsl2']
ALL = UNIX + ['windows']
SCRIPTS = ['install', 'reinstall', 'uninstall', 'run', 'update', 'run-at-startup', 'remove-from-startup', 'kill-armor']
TFM_SCRIPTS = ['install', 'reinstall', 'run', 'update']
LAUNCHD = 'launchd-com.jchristn.armor.agent'
PKG = 'launchd-com.joelchristner.armor'
UID = str(os.getuid())
HELPERS = {'_wsl-common.sh'}

FAKE = r'''#!/usr/bin/env python3
import json, os, pathlib, sys
name = pathlib.Path(sys.argv[0]).name
args = sys.argv[1:]
state = pathlib.Path(os.environ['MOCK_STATE'])
def on(flag): return (state / flag).exists()
def set_(flag, value=True):
    p = state / flag
    if value: p.touch()
    elif p.exists(): p.unlink()
if name == 'uname':
    if args == ['-s']: print(os.environ['MOCK_UNAME'])
    elif args == ['-r']: print(os.environ.get('MOCK_UNAME_R', '6.6.0-generic'))
    else: print(os.environ.get('MOCK_ARCH', 'arm64'))
    sys.exit(0)
if name == 'cmd.exe':
    print('C:\\Users\\test\\AppData\\Roaming\r'); sys.exit(0)
if name == 'wslpath':
    print(os.environ['MOCK_APPDATA'] if args[0] == '-u' else 'C:\\fake\\' + pathlib.Path(args[1]).name); sys.exit(0)
with open(os.environ['CALL_LOG'], 'a') as f: f.write(json.dumps([name, *args]) + '\n')
if name == 'pgrep':
    if '-x' in args: sys.exit(0 if on('proc-' + args[args.index('-x') + 1]) else 1)
    sys.exit(1)
if name == 'pkill':
    if '-x' in args and not os.environ.get('MOCK_STUBBORN'): set_('proc-' + args[args.index('-x') + 1], False)
    sys.exit(0)
if name == 'systemctl':
    rest = [a for a in args if a not in ('--user', '--quiet')]
    if rest[:1] == ['show-environment']: sys.exit(0 if os.environ.get('MOCK_SYSTEMD') else 1)
    if rest == ['is-active', 'graphical-session.target']: sys.exit(0 if os.environ.get('MOCK_SYSTEMD') else 1)
    if rest == ['is-active', 'armor-agent']: sys.exit(0 if on('service') else 1)
    if rest[:1] == ['stop'] or rest[:2] == ['disable', '--now']: set_('service', False); set_('proc-Armor.Agent', False)
    if rest[:1] == ['start']: set_('service'); set_('proc-Armor.Agent')
    sys.exit(0)
if name == 'launchctl':
    label = args[1].rsplit('/', 1)[-1] if len(args) > 1 else ''
    if label.endswith('.plist'): label = label[:-6]
    if args[0] == 'print': sys.exit(0 if on('launchd-' + label) else 113)
    if args[0] == 'bootout':
        if not on('launchd-' + label): sys.exit(3)
        set_('launchd-' + label, False); set_('proc-Armor.Agent', False)
    if args[0] == 'bootstrap':
        label = pathlib.Path(args[2]).stem; set_('launchd-' + label); set_('proc-Armor.Agent')
    sys.exit(0)
if name == 'wscript.exe': set_('proc-Armor.Agent'); sys.exit(0)
if name == 'open': set_('proc-Armor.Agent'); sys.exit(0)
if name == 'git': sys.exit(1 if os.environ.get('MOCK_GIT_FAIL') else 0)
if name == 'iconutil': pathlib.Path(args[args.index('--output') + 1]).touch()
if name == 'codesign' and os.environ.get('MOCK_FAIL_SIGN'): sys.exit(8)
if name == 'dotnet' and args[:1] == ['publish']:
    if os.environ.get('MOCK_FAIL_PUBLISH'): sys.exit(7)
    out = pathlib.Path(args[args.index('-o') + 1])
    out.mkdir(parents=True, exist_ok=True)
    exe = out / pathlib.Path(args[1]).stem
    exe.write_text('#!/bin/sh\necho "ran $(basename "$0") DOTNET_ROOT=${DOTNET_ROOT:-}"\n'); exe.chmod(0o755)
'''
FAKES = ['dotnet', 'uname', 'pgrep', 'pkill', 'systemctl', 'launchctl', 'git', 'open', 'sips', 'iconutil',
         'codesign', 'cmd.exe', 'wslpath', 'wscript.exe']

count = 0


def check(cond, *info):
    global count
    count += 1
    assert cond, info


# ---------------------------------------------------------------------------------------------
# Same set everywhere, and static rules for every file.
names = {}
for d in ALL:
    ext = '.bat' if d == 'windows' else '.sh'
    names[d] = sorted(f.stem for f in (root / 'scripts' / d).iterdir() if f.suffix == ext and f.name not in HELPERS)
    check(names[d] == sorted(SCRIPTS), d, set(names[d]) ^ set(SCRIPTS))
    for f in (root / 'scripts' / d).iterdir():
        check(f.name in HELPERS or f.suffix == ext, 'unexpected file', f)
for d in ['wsl', 'wsl2']:
    check((root / f'scripts/{d}/_wsl-common.sh').read_bytes() == (root / 'scripts/wsl/_wsl-common.sh').read_bytes(), d)

for d in UNIX:
    for name in SCRIPTS:
        path = root / f'scripts/{d}/{name}.sh'
        text = path.read_text()
        check(b'\r' not in path.read_bytes(), 'CRLF', path)
        check(os.access(path, os.X_OK), 'not executable', path)
        check(text.startswith('#!/usr/bin/env bash\n'), 'shebang', path)
        check('\nset -euo pipefail\n' in text, 'strict mode', path)
        check(re.search(r'^\s*die\(\) \{', text, re.M), 'die()', path)
        check(f'# Usage: ./scripts/{d}/{name}.sh' in text, 'usage header', path)
        # Self-contained: sources only the WSL helper, never runs another script.
        for m in re.finditer(r'"\$(SCRIPT_DIR|REPO_ROOT)/([^"]+)"', text):
            target = m.group(2)
            check(not target.endswith('.sh') or target == '_wsl-common.sh', 'calls another script', path, target)
        check('../linux' not in text and 'scripts/common' not in text, 'delegates', path)
        if d.startswith('wsl'):
            check(f'WSL_FLAVOR={d}\n' in text and 'wsl_require\nwsl_sanitize_path\nwsl_check_repo "$REPO_ROOT"\n' in text, 'WSL preamble', path)
        if d == 'linux':
            check('[ -n "${WSL_DISTRO_NAME:-}" ] || [ -d /run/WSL ]' in text, 'Linux WSL guard', path)

for name in SCRIPTS:
    path = root / f'scripts/windows/{name}.bat'
    raw = path.read_bytes()
    text = raw.decode()
    check(b'\r\n' in raw and b'\n' not in raw.replace(b'\r\n', b''), 'bat must be CRLF', path)
    check(text.startswith('@echo off\r\n') and '\r\nsetlocal\r\n' in text, 'setlocal', path)
    if name == 'update':
        check('set "CODE=0"\r\ngoto finish' in text and 'set "CODE=1"\r\ngoto finish' in text, 'exit codes', path)
    elif name == 'run':
        check(':fail\r\nif not "%STOPPED%"=="1" goto fail_done' in text and ':fail_done\r\nendlocal & exit /b 1' in text, 'restore on failure', path)
    else:
        check('endlocal & exit /b 0' in text and ':fail\r\nendlocal & exit /b 1' in text, 'exit codes', path)
    # Only this user's processes.
    for line in [l for l in text.split('\r\n') if l.startswith('tasklist')]:
        check('/FI "USERNAME eq %USERDOMAIN%\\%USERNAME%"' in line, 'tasklist must filter by user', path, line)
    check('taskkill /IM' not in text, 'stop through the PowerShell helper, not taskkill /IM', path)
    if name in ('install', 'reinstall', 'uninstall', 'run', 'update'):
        check('if exist "%DEST%\\Armor.Agent" goto linux_build' in text, 'refuse a WSL build in dist', path)
    check('if /I "%ARG1%"=="-h" goto usage' in text and '"--help" goto usage' in text, 'help', path)
    check('goto unexpected_arg' in text and 'Unexpected argument' in text, 'argument check', path)
    check(not re.search(r'call\s+"[^"]*\.(bat|cmd)"', text, re.I) or name == 'update', 'calls another script', path)
    check('..\\linux' not in text, path)
    if name in TFM_SCRIPTS:
        check('goto bad_framework' in text and 'Supported frameworks: net8.0, net10.0' in text, path)
    if name in ('install', 'reinstall', 'uninstall'):
        check('kill-armor.bat' in text, 'running hint', path)
    # Every step that can fail is checked.
    for line in [l for l in text.split('\r\n') if l.startswith(('dotnet publish', 'move ', 'reg add', 'reg delete'))]:
        following = text.split(line + '\r\n', 1)[1].split('\r\n', 1)[0]
        check(following.startswith(('if errorlevel 1', 'if not errorlevel 1')), 'unchecked step', path, line)
    # No echo of a %VAR% path inside a parenthesized block (breaks on paths containing ')').
    depth = 0
    for line in text.split('\r\n'):
        stripped = line.strip()
        if depth > 0 and stripped.lower().startswith('echo') and re.search(r'%(DEST|REPO_ROOT|STAGE|AGENT_EXE|ARMOR_DIR)%', stripped):
            check(False, 'echo of a path inside ( )', path, line)
        depth += stripped.count('(') - stripped.count('^(') - stripped.count(')') + stripped.count('^)') if not stripped.upper().startswith('REM') and not stripped.lower().startswith('echo') else 0
update_bat = (root / 'scripts/windows/update.bat').read_text()
check('copy /y "%~dpn0.bat" "%UPDATE_COPY%"' in update_bat, 'update.bat must run from a copy')
check('& "%UPDATE_COPY%" %*' in update_bat and 'call "%UPDATE_COPY%"' not in update_bat, 'hand off without call')
check('(goto) 2>nul & del "%~f0"' in update_bat, 'the copy deletes itself after leaving its context')
for name in ['kill-armor', 'run', 'update', 'remove-from-startup']:
    text = (root / f'scripts/windows/{name}.bat').read_text()
    check('taskkill.exe /PID $x.ProcessId /T 2>$null' in text and '/T /F' in text and '$_.SessionId -eq $session' in text, name)
# The PowerShell stop helper parses (when pwsh is available).
pwsh = shutil.which('pwsh') or shutil.which('powershell')
if pwsh:
    text = (root / 'scripts/windows/kill-armor.bat').read_text().replace('\r\n', '\n')
    block = text.split('powershell -NoProfile -ExecutionPolicy Bypass -Command ^\n', 1)[1].split('\nif errorlevel 3', 1)[0]
    code = ' '.join(re.findall(r'^\s*"(.*)"(?: \^)?$', block, re.M))
    r = subprocess.run([pwsh, '-NoProfile', '-Command',
                        '$e = $null; [System.Management.Automation.Language.Parser]::ParseInput($env:PSCODE, [ref]$null, [ref]$e) | Out-Null; '
                        'if ($e.Count) { $e | ForEach-Object { $_.Message }; exit 1 }'],
                       env=dict(os.environ, PSCODE=code), capture_output=True, text=True)
    check(r.returncode == 0 and code.count('Find-Armor') >= 4, 'PowerShell helper must parse', r.stdout, r.stderr)
for name in ['run-at-startup', 'remove-from-startup', 'uninstall']:
    check('/v ArmorAgent' in (root / f'scripts/windows/{name}.bat').read_text(), name)

# ---------------------------------------------------------------------------------------------
with tempfile.TemporaryDirectory(prefix='armor scripts ') as temp:
    temp = Path(temp)
    repo = temp / 'repo'
    shutil.copytree(root / 'scripts', repo / 'scripts', ignore=shutil.ignore_patterns('__pycache__'))
    for project in ['Armor.Agent', 'Armor.Tui']:
        (repo / 'src' / project).mkdir(parents=True)
        (repo / 'src' / project / f'{project}.csproj').write_text('<Project />')
    shutil.copy(root / 'src/Directory.Build.props', repo / 'src/Directory.Build.props')
    (repo / 'assets').mkdir()
    shutil.copy(root / 'assets/logo.png', repo / 'assets/logo.png')
    bin_dir = temp / 'bin'
    bin_dir.mkdir()
    for name in FAKES:
        (bin_dir / name).write_text(FAKE)
        (bin_dir / name).chmod(0o755)
    log = temp / 'calls.jsonl'
    state = temp / 'state'
    home = temp / 'home'
    appdata = temp / 'appdata'
    startup_dir = appdata / 'Microsoft/Windows/Start Menu/Programs/Startup'
    base_env = dict(os.environ, PATH=f'{bin_dir}:/usr/bin:/bin', CALL_LOG=str(log), HOME=str(home), MOCK_STATE=str(state),
                    WSL_SKIP_CHECK='1', WSL_DISTRO_NAME='Test', DISPLAY=':0', MOCK_APPDATA=str(appdata))
    for key in ['DOTNET_ROOT', 'WAYLAND_DISPLAY', 'ARMOR_HOME', 'MOCK_SYSTEMD']:
        base_env.pop(key, None)

    def reset(*running, **flags):
        shutil.rmtree(state, ignore_errors=True)
        state.mkdir()
        for proc in running:
            (state / f'proc-{proc}').touch()
        for flag in flags:
            (state / flag).touch()

    def running():
        return sorted(p.name[5:] for p in state.glob('proc-*'))

    def run(platform, script, *args, unset=(), **env):
        log.unlink(missing_ok=True)
        e = dict(base_env, MOCK_UNAME='Darwin' if platform == 'macos' else 'Linux')
        e.update(env)
        for key in unset:
            e.pop(key, None)
        p = subprocess.run(['bash', str(repo / f'scripts/{platform}/{script}.sh'), *args], env=e,
                           stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=120)
        calls = [json.loads(x) for x in log.read_text().splitlines()] if log.exists() else []
        return p, calls

    def publishes(calls):
        return [c for c in calls if c[:2] == ['dotnet', 'publish']]

    def dest(platform):
        return home / 'Applications/Armor.app' if platform == 'macos' else repo / 'dist'

    def exe(platform, name):
        return dest(platform) / 'Contents/MacOS' / name if platform == 'macos' else dest(platform) / name

    def clean():
        shutil.rmtree(repo / 'dist', ignore_errors=True)
        shutil.rmtree(repo / 'artifacts', ignore_errors=True)
        shutil.rmtree(home, ignore_errors=True)
        shutil.rmtree(appdata, ignore_errors=True)
        home.mkdir()

    clean()
    reset()

    # OS guards.
    for platform, wrong, message in [('linux', 'Darwin', 'This script is for Linux'), ('macos', 'Linux', 'This script is for macOS')]:
        for script in SCRIPTS:
            p, calls = run(platform, script, MOCK_UNAME=wrong)
            check(p.returncode != 0 and message in p.stderr and not publishes(calls), platform, script, p.stderr)
    for platform in ['wsl', 'wsl2']:
        for script in SCRIPTS:
            p, calls = run(platform, script, unset=('WSL_SKIP_CHECK', 'WSL_DISTRO_NAME'))
            check(p.returncode != 0 and 'This script is for WSL' in p.stderr and not calls, platform, script, p.stderr)
        p, _ = run('wsl2', 'install', unset=('WSL_SKIP_CHECK',), MOCK_UNAME_R='4.4.0-19041-Microsoft')
        check(p.returncode != 0 and 'runs WSL 1' in p.stderr and 'scripts/wsl ' in p.stderr, p.stderr)
        p, _ = run('wsl', 'install', unset=('WSL_SKIP_CHECK',), MOCK_UNAME_R='5.15.153.1-microsoft-standard-WSL2')
        check(p.returncode != 0 and 'runs WSL 2' in p.stderr and 'scripts/wsl2' in p.stderr, p.stderr)

    # Argument handling: -h/--help, unexpected arguments, unsupported frameworks. Nothing runs.
    for platform in UNIX:
        for script in SCRIPTS:
            for flag in ['-h', '--help']:
                p, calls = run(platform, script, flag)
                check(p.returncode == 0 and p.stdout.startswith(f'Usage: scripts/{platform}/{script}.sh') and not calls,
                      platform, script, p.stdout, p.stderr)
            p, calls = run(platform, script, 'bogus')
            check(p.returncode != 0 and 'Unexpected argument: bogus' in p.stderr and not publishes(calls), platform, script, p.stderr)
            if script in TFM_SCRIPTS:
                p, calls = run(platform, script, 'net9.0')
                check(p.returncode != 0 and 'Unsupported framework: net9.0' in p.stderr and not calls, platform, script)
                p, calls = run(platform, script, 'net8.0', 'extra')
                check(p.returncode != 0 and 'Unexpected argument: extra' in p.stderr and not calls, platform, script)

    # install / reinstall / uninstall rules, and the files they create and remove.
    for platform in UNIX:
        clean()
        reset()
        kill_hint = f'scripts/{platform}/kill-armor.sh'
        for tfm in ['net8.0', 'net10.0']:
            clean()
            p, calls = run(platform, 'install', tfm, MOCK_ARCH='x86_64')
            check(p.returncode == 0, platform, p.stderr)
            pubs = publishes(calls)
            check([Path(c[2]).stem for c in pubs] == ['Armor.Agent', 'Armor.Tui'], pubs)
            check(all(c[c.index('-f') + 1] == tfm and f'-p:TargetFrameworks={tfm}' in c for c in pubs), pubs)
            if platform == 'macos':
                check(all(c[c.index('-r') + 1] == 'osx-x64' and '--self-contained' in c for c in pubs), pubs)
                with (dest(platform) / 'Contents/Info.plist').open('rb') as f:
                    check(plistlib.load(f)['CFBundleExecutable'] == 'Armor.Agent')
            for name in ['Armor.Agent', 'Armor.Tui']:
                check(os.access(exe(platform, name), os.X_OK), platform, name)
        p, calls = run(platform, 'install')
        check(p.returncode != 0 and f'scripts/{platform}/reinstall.sh' in p.stderr and not publishes(calls), platform, p.stderr)
        for proc in ['Armor.Agent', 'Armor.Tui']:
            reset(proc)
            for script in ['install', 'reinstall', 'uninstall']:
                p, calls = run(platform, script)
                check(p.returncode != 0 and kill_hint in p.stderr and not publishes(calls), platform, script, p.stderr)
                check(running() == [proc] and not any(c[0] == 'pkill' for c in calls), platform, script, calls)
        reset()
        check(exe(platform, 'Armor.Agent').exists(), 'refusals must not touch the install')
        # reinstall: a failed build keeps the existing install.
        marker = dest(platform) / 'keep-me'
        marker.touch()
        for failure in ['MOCK_FAIL_PUBLISH'] + (['MOCK_FAIL_SIGN'] if platform == 'macos' else []):
            p, _ = run(platform, 'reinstall', **{failure: '1'})
            check(p.returncode != 0 and marker.exists() and exe(platform, 'Armor.Agent').exists(), platform, failure)
        p, calls = run(platform, 'reinstall')
        check(p.returncode == 0 and f'Removed {dest(platform)}.' in p.stdout and not marker.exists(), platform, p.stdout, p.stderr)
        order = [l for l in p.stdout.splitlines() if l.startswith('[')]
        check([l.split(']')[0] for l in order] == ['[1/5', '[2/5', '[3/5', '[4/5', '[5/5'], order)
        check('Building' in order[1] and 'Removing' in order[2], 'reinstall builds before removing', order)
        shutil.rmtree(dest(platform))
        p, _ = run(platform, 'reinstall')
        check(p.returncode == 0 and 'Armor is not installed; continuing.' in p.stdout, platform, p.stdout)
        # uninstall removes the install and everything run-at-startup registered.
        p, _ = run(platform, 'run-at-startup', MOCK_SYSTEMD='1')
        check(p.returncode == 0, platform, p.stderr)
        reset()
        p, calls = run(platform, 'uninstall')
        check(p.returncode == 0 and not dest(platform).exists(), platform, p.stderr)
        check(not (repo / 'artifacts/install').exists() and not (repo / 'artifacts/macos-app').exists(), platform)
        leftovers = [x for x in [home / 'Library/LaunchAgents/com.jchristn.armor.agent.plist',
                                 home / '.config/systemd/user/armor-agent.service',
                                 home / '.config/autostart/armor-agent.desktop'] if x.exists()]
        leftovers += list(startup_dir.glob('*.vbs')) if startup_dir.exists() else []
        check(not leftovers, platform, leftovers)
        check(f'run reset.sh at the repository root' in p.stdout and str(home / '.armor') in p.stdout, p.stdout)
        p, _ = run(platform, 'uninstall')
        check(p.returncode == 0 and 'nothing to remove' in p.stdout and 'not registered' in p.stdout, platform, p.stdout)

    # kill-armor: nothing running, then a running agent and console; supervisors are stopped first.
    for platform in UNIX:
        reset()
        p, calls = run(platform, 'kill-armor')
        check(p.returncode == 0 and 'No Armor processes are running.' in p.stdout, platform, p.stdout)
        reset('Armor.Agent', 'Armor.Tui')
        p, calls = run(platform, 'kill-armor')
        check(p.returncode == 0 and running() == [] and 'Done.' in p.stdout, platform, p.stdout, p.stderr)
        check(['pkill', '-TERM', '-u', UID, '-x', 'Armor.Agent'] in calls and ['pkill', '-TERM', '-u', UID, '-x', 'Armor.Tui'] in calls, calls)
    unit = home / '.config/systemd/user/armor-agent.service'
    unit.parent.mkdir(parents=True, exist_ok=True)
    unit.write_text('[Service]\n')
    reset('Armor.Agent', service='1')
    p, calls = run('linux', 'kill-armor')
    check(p.returncode == 0 and ['systemctl', '--user', 'stop', 'armor-agent'] in calls, calls)
    check(calls.index(['systemctl', '--user', 'stop', 'armor-agent']) < min([i for i, c in enumerate(calls) if c[0] == 'pkill'] or [99]), calls)
    reset('Armor.Agent', **{LAUNCHD: '1'})
    p, calls = run('macos', 'kill-armor')
    check(p.returncode == 0 and any(c[:2] == ['launchctl', 'bootout'] for c in calls) and running() == [], calls)
    unit.unlink()
    reset('Armor.Agent')
    p, calls = run('linux', 'kill-armor', MOCK_STUBBORN='1')
    check(p.returncode != 0 and 'could not be stopped' in p.stderr and ['pkill', '-KILL', '-u', UID, '-x', 'Armor.Agent'] in calls, p.stderr)

    # update / run: stop, (pull), rebuild, replace, and restart the agent the way it ran.
    for platform in UNIX:
        clean()
        for script, total in [('update', 6), ('run', 6)]:
            reset()
            p, calls = run(platform, script, 'net8.0')
            check(p.returncode == 0, platform, script, p.stderr)
            steps = [l.split(']')[0] + ']' for l in p.stdout.splitlines() if l.startswith('[')]
            check(steps == [f'[{i}/{total}]' for i in range(1, total + 1)], platform, script, steps)
            check((['git', '-C', str(repo), 'pull', '--ff-only'] in calls) == (script == 'update'), platform, script, calls)
            if script == 'run':
                check(p.stdout.rstrip().splitlines()[-1].startswith('ran Armor.Tui'), 'run must exec the console', p.stdout)
                check('the console starts it' in p.stdout, p.stdout)
            else:
                check('leaving it stopped' in p.stdout, p.stdout)
            reset('Armor.Agent', 'Armor.Tui')
            p, calls = run(platform, script)
            check(p.returncode == 0 and 'Restarted the agent' in p.stdout, platform, script, p.stdout, p.stderr)
            kills = [i for i, c in enumerate(calls) if c[0] == 'pkill']
            first_publish = next(i for i, c in enumerate(calls) if c[:2] == ['dotnet', 'publish'])
            check(kills and max(kills) < first_publish, 'must stop Armor before publishing', calls)
            if platform == 'macos':
                check(['open', str(dest('macos'))] in calls, calls)
        p, _ = run(platform, 'update', MOCK_GIT_FAIL='1')
        check(p.returncode == 0 and 'git pull failed; rebuilding the checkout as-is' in p.stderr, p.stderr)
    unit.parent.mkdir(parents=True, exist_ok=True)
    unit.write_text('[Service]\n')
    reset('Armor.Agent', service='1')
    p, calls = run('linux', 'update')
    check(p.returncode == 0 and ['systemctl', '--user', 'start', 'armor-agent'] in calls and 'systemd user service' in p.stdout, p.stdout)
    unit.unlink()
    reset('Armor.Agent', **{LAUNCHD: '1'})
    (home / 'Library/LaunchAgents').mkdir(parents=True, exist_ok=True)
    (home / 'Library/LaunchAgents/com.jchristn.armor.agent.plist').write_text('<plist/>')
    p, calls = run('macos', 'update')
    check(p.returncode == 0 and any(c[:2] == ['launchctl', 'bootstrap'] for c in calls) and 'through launchd' in p.stdout, p.stdout)
    check(calls.index(next(c for c in calls if c[:2] == ['launchctl', 'bootout'])) < calls.index(next(c for c in calls if c[:2] == ['dotnet', 'publish'])), calls)
    (home / 'Library/LaunchAgents/com.jchristn.armor.agent.plist').unlink()
    for main in ['linux', 'macos', 'wsl', 'wsl2']:
        text = (repo / f'scripts/{main}/update.sh').read_text()
        check('\nmain() {\n' in text and text.rstrip().endswith('main "$@"'), 'update must be read in full before git pull', main)

    # run-at-startup / remove-from-startup on Linux: systemd unit or desktop entry.
    clean()
    reset()
    p, _ = run('linux', 'run-at-startup')
    check(p.returncode != 0 and 'scripts/linux/install.sh first' in p.stderr, p.stderr)
    run('linux', 'install')
    service = home / '.config/systemd/user/armor-agent.service'
    desktop = home / '.config/autostart/armor-agent.desktop'
    p, calls = run('linux', 'run-at-startup', DOTNET_ROOT='/opt/dotnet')
    check(p.returncode == 0 and desktop.exists() and not service.exists(), p.stderr)
    text = desktop.read_text()
    check(f'Exec=env "DOTNET_ROOT=/opt/dotnet" "{repo}/dist/Armor.Agent"' in text and 'Started the agent now.' in p.stdout, text)
    reset()
    p, calls = run('linux', 'run-at-startup', MOCK_SYSTEMD='1', DOTNET_ROOT='/opt/dotnet')
    check(p.returncode == 0 and service.exists() and not desktop.exists(), 'one mechanism only', p.stdout)
    unit = service.read_text()
    for line in [f'ExecStart="{repo}/dist/Armor.Agent"', 'Environment="DOTNET_ROOT=/opt/dotnet"', 'After=graphical-session.target',
                 'PartOf=graphical-session.target', 'WantedBy=graphical-session.target', 'Restart=on-failure']:
        check(line in unit, line, unit)
    check('default.target' not in unit and 'enable-linger' not in p.stdout, unit)
    check(['systemctl', '--user', 'enable', 'armor-agent'] in calls and ['systemctl', '--user', 'start', 'armor-agent'] in calls, calls)
    p, calls = run('linux', 'run-at-startup', MOCK_SYSTEMD='1')
    check(p.returncode == 0 and 'already running' in p.stdout and ['systemctl', '--user', 'start', 'armor-agent'] not in calls, p.stdout)
    p, calls = run('linux', 'remove-from-startup')
    check(p.returncode == 0 and not service.exists() and ['systemctl', '--user', 'disable', '--now', 'armor-agent'] in calls, p.stdout)
    p, _ = run('linux', 'remove-from-startup')
    check(p.returncode == 0 and 'nothing to do' in p.stdout, p.stdout)
    # remove-from-startup stops the running agent when it removed a registration (every platform).
    reset('Armor.Agent')
    p, _ = run('linux', 'remove-from-startup')
    check(p.returncode == 0 and 'nothing to do' in p.stdout and running() == ['Armor.Agent'], 'nothing registered: agent untouched', p.stdout)
    desktop.parent.mkdir(parents=True, exist_ok=True)
    desktop.write_text('[Desktop Entry]\n')
    reset('Armor.Agent', 'Armor.Tui')
    p, calls = run('linux', 'remove-from-startup')
    check(p.returncode == 0 and 'Stopped the running agent.' in p.stdout and running() == ['Armor.Tui'], p.stdout, running())
    # Enablement links go even when systemctl cannot reach a user bus, and the stop does not need is-active.
    wants = home / '.config/systemd/user/graphical-session.target.wants'
    old_wants = home / '.config/systemd/user/default.target.wants'
    for link in [wants, old_wants]:
        link.mkdir(parents=True, exist_ok=True)
        (link / 'armor-agent.service').symlink_to(service)
    service.write_text('[Service]\n')
    reset()
    p, calls = run('linux', 'kill-armor')
    check(['systemctl', '--user', 'stop', 'armor-agent'] in calls, 'stop whenever the unit exists', calls)
    p, _ = run('linux', 'remove-from-startup')
    check(p.returncode == 0 and not service.exists() and not (wants / 'armor-agent.service').is_symlink()
          and not (old_wants / 'armor-agent.service').is_symlink(), p.stdout)
    # Falling back to autostart removes an old unit and its enablement links, so the agent starts once.
    for link in [wants, old_wants]:
        (link / 'armor-agent.service').symlink_to(service)
    service.write_text('[Service]\n')
    reset()
    p, _ = run('linux', 'run-at-startup')
    check(p.returncode == 0 and desktop.exists() and not service.exists()
          and not (wants / 'armor-agent.service').is_symlink() and not (old_wants / 'armor-agent.service').is_symlink(), p.stdout)
    run('linux', 'remove-from-startup')
    # No display: the autostart entry is written, but the GUI agent is not started now.
    reset()
    p, calls = run('linux', 'run-at-startup', unset=('DISPLAY',))
    check(p.returncode == 0 and desktop.exists() and 'was not started now' in p.stdout and 'Started the agent now' not in p.stdout, p.stdout)
    run('linux', 'remove-from-startup')
    # DOTNET_ROOT is derived for a .NET outside the default locations and reaches both login entries.
    dotnet_home = temp / 'dotnet-home'
    (dotnet_home / 'host/fxr').mkdir(parents=True)
    shutil.copy(bin_dir / 'dotnet', dotnet_home / 'dotnet')
    path_with = f'{dotnet_home}:{bin_dir}:/usr/bin:/bin'
    p, _ = run('linux', 'run-at-startup', PATH=path_with)
    check(f'Exec=env "DOTNET_ROOT={os.path.realpath(dotnet_home)}"' in desktop.read_text(), desktop.read_text())
    reset()
    p, _ = run('linux', 'run-at-startup', PATH=path_with, MOCK_SYSTEMD='1')
    check(f'Environment="DOTNET_ROOT={os.path.realpath(dotnet_home)}"' in service.read_text(), service.read_text())
    run('linux', 'remove-from-startup')
    reset()

    # macOS: launchd plist.
    clean()
    reset()
    p, _ = run('macos', 'run-at-startup')
    check(p.returncode != 0 and 'scripts/macos/install.sh first' in p.stderr, p.stderr)
    run('macos', 'install')
    plist = home / 'Library/LaunchAgents/com.jchristn.armor.agent.plist'
    p, calls = run('macos', 'run-at-startup')
    check(p.returncode == 0 and any(c[:2] == ['launchctl', 'bootstrap'] for c in calls), p.stderr)
    with plist.open('rb') as f:
        data = plistlib.load(f)
    check(data['ProgramArguments'] == [str(exe('macos', 'Armor.Agent'))] and data['KeepAlive'] == {'SuccessfulExit': False}, data)
    p, calls = run('macos', 'run-at-startup')
    check(p.returncode == 0 and 'already running' in p.stdout and not any(c[:2] == ['launchctl', 'bootstrap'] for c in calls), p.stdout)
    p, calls = run('macos', 'remove-from-startup')
    check(p.returncode == 0 and not plist.exists() and any(c[:2] == ['launchctl', 'bootout'] for c in calls), p.stdout)
    p, _ = run('macos', 'remove-from-startup')
    check(p.returncode == 0 and 'nothing to do' in p.stdout, p.stdout)

    # WSL: Windows Startup-folder launcher for this distro, started now through wscript.exe.
    launcher = startup_dir / 'armor-agent-wsl-Test.vbs'
    for platform in ['wsl', 'wsl2']:
        clean()
        reset()
        p, _ = run(platform, 'run-at-startup')
        check(p.returncode != 0 and f'scripts/{platform}/install.sh first' in p.stderr, p.stderr)
        run(platform, 'install')
        p, calls = run(platform, 'run-at-startup', unset=('DISPLAY',), WAYLAND_DISPLAY='wayland-0')
        check(p.returncode != 0 and not launcher.exists(), platform, 'no display', p.stderr)
        check(('WSLg' in p.stderr) and (('X server' in p.stderr) == (platform == 'wsl')), p.stderr)
        service.parent.mkdir(parents=True, exist_ok=True)
        service.write_text('[Service]\n')
        desktop.parent.mkdir(parents=True, exist_ok=True)
        desktop.write_text('[Desktop Entry]\n')
        p, calls = run(platform, 'run-at-startup', DOTNET_ROOT='/opt/dotnet')
        check(p.returncode == 0 and launcher.exists(), platform, p.stderr)
        check(not service.exists() and not desktop.exists(), 'Linux leftovers must be removed', platform)
        vbs = launcher.read_bytes().decode()
        check(vbs.startswith('CreateObject("WScript.Shell").Run "wsl.exe -d ""Test"" --cd ~ --exec /bin/sh -lc ""exec'), vbs)
        check(f"'{repo}/dist/Armor.Agent'" in vbs and "'env' 'DOTNET_ROOT=/opt/dotnet'" in vbs and vbs.endswith(', 0, False\r\n'), vbs)
        check(("'DISPLAY=:0'" in vbs) == (platform == 'wsl'), vbs)
        check(['wscript.exe', 'C:\\fake\\armor-agent-wsl-Test.vbs'] in calls and 'Started the agent now.' in p.stdout, calls)
        check(f'scripts/{platform}/kill-armor.sh' in p.stdout, p.stdout)
        # update restarts a running agent through the launcher.
        p, calls = run(platform, 'update')
        check(p.returncode == 0 and ['wscript.exe', 'C:\\fake\\armor-agent-wsl-Test.vbs'] in calls and 'Windows Startup launcher' in p.stdout, p.stdout)
        reset()
        p, _ = run(platform, 'remove-from-startup')
        check(p.returncode == 0 and not launcher.exists(), p.stderr)
        p, _ = run(platform, 'remove-from-startup')
        check(p.returncode == 0 and 'nothing to do' in p.stdout, p.stdout)
        # Never delete a Windows build that shares dist/ with a checkout on a Windows drive.
        (repo / 'dist/Armor.Agent.exe').touch()
        for script in ['install', 'reinstall', 'update', 'run', 'uninstall']:
            p, calls = run(platform, script)
            check(p.returncode != 0 and 'Windows build' in p.stderr and (repo / 'dist/Armor.Agent.exe').exists(), platform, script)
        (repo / 'dist/Armor.Agent.exe').unlink()

    # A failed update/run build restores the agent as it ran; the old install stays in place.
    for platform in UNIX:
        clean()
        reset()
        p, _ = run(platform, 'install')
        check(p.returncode == 0, platform, p.stderr)
        marker = dest(platform) / 'keep-me'
        for script in ['update', 'run']:
            marker.touch()
            reset('Armor.Agent')
            p, calls = run(platform, script, MOCK_FAIL_PUBLISH='1')
            check(p.returncode != 0 and 'restoring the previous state' in p.stderr and 'Restarted the agent' in p.stderr, platform, script, p.stderr)
            check(marker.exists() and exe(platform, 'Armor.Agent').exists(), 'old install kept', platform, script)
        if platform == 'macos':
            (home / 'Library/LaunchAgents').mkdir(parents=True, exist_ok=True)
            (home / 'Library/LaunchAgents/com.jchristn.armor.agent.plist').write_text('<plist/>')
            reset('Armor.Agent', **{LAUNCHD: '1'})
            p, calls = run('macos', 'update', MOCK_FAIL_PUBLISH='1')
            check(p.returncode != 0 and any(c[:2] == ['launchctl', 'bootstrap'] for c in calls) and running() == ['Armor.Agent'], p.stderr)
            (home / 'Library/LaunchAgents/com.jchristn.armor.agent.plist').unlink()
        if platform == 'linux':
            service.parent.mkdir(parents=True, exist_ok=True)
            service.write_text('[Service]\n')
            reset('Armor.Agent', service='1')
            p, calls = run('linux', 'update', MOCK_FAIL_PUBLISH='1')
            check(p.returncode != 0 and ['systemctl', '--user', 'start', 'armor-agent'] in calls and running() == ['Armor.Agent'], p.stderr)
            service.unlink()
    # run passes the derived DOTNET_ROOT to the console it execs.
    clean()
    reset()
    p, _ = run('linux', 'run', PATH=path_with)
    check(p.returncode == 0 and f'ran Armor.Tui DOTNET_ROOT={os.path.realpath(dotnet_home)}' in p.stdout, p.stdout)

    # macOS: the .pkg installer's LaunchAgent is refused by the build scripts and unloaded by kill-armor.
    clean()
    for script in ['install', 'reinstall', 'uninstall', 'run', 'update']:
        reset('Armor.Agent', **{PKG: '1'})
        p, calls = run('macos', script)
        check(p.returncode != 0 and 'packaged Armor' in p.stderr and 'kill-armor.sh' in p.stderr and not publishes(calls), script, p.stderr)
    reset('Armor.Agent', **{PKG: '1'})
    p, calls = run('macos', 'kill-armor')
    check(p.returncode == 0 and ['launchctl', 'bootout', f'gui/{UID}/com.joelchristner.armor'] in calls and 'next login' in p.stdout, p.stdout)

    # Every process check and kill is limited to this user.
    for d in UNIX:
        for name in SCRIPTS:
            for line in (repo / f'scripts/{d}/{name}.sh').read_text().splitlines():
                for m in re.finditer(r'\b(pgrep|pkill)\b(?=\s+["-])', line):
                    check(re.match(r'\s+(?:"\$1"\s+)?-u ', line[m.end():]), 'pgrep/pkill without -u', d, name, line)

    # docker/update.sh checks its arguments and docker itself, like docker/update.bat.
    shutil.copytree(root / 'docker', repo / 'docker', dirs_exist_ok=True)
    r = subprocess.run(['sh', str(repo / 'docker/update.sh'), 'extra'], env=base_env, capture_output=True, text=True)
    check(r.returncode != 0 and 'Usage' in r.stderr, r.stderr)
    r = subprocess.run(['sh', str(repo / 'docker/update.sh')], env=dict(base_env, PATH=f'{bin_dir}:/usr/bin:/bin'), capture_output=True, text=True)
    check(r.returncode != 0 and 'docker was not found' in r.stderr, r.stderr)

    # Root scripts: go.sh/reset.sh no longer pkill -f a bare name.
    for name in ['go.sh', 'reset.sh']:
        text = (root / name).read_text()
        check('pkill -f "Armor.Tui"' not in text and 'pkill -f "Armor.Agent"' not in text, name)
        check('pkill -x' not in text and 'pgrep -x' not in text, 'root scripts must limit kills to this user', name)
    go = (root / 'go.sh').read_text()
    check(go.index('exec ./scripts/macos/run.sh') < go.index('pkill'), 'go.sh must hand off to run.sh before killing anything')
    mode = subprocess.run(['git', '-C', str(root), 'ls-files', '-s', 'reset.sh'], capture_output=True, text=True).stdout
    check(mode.startswith('100755') and os.access(root / 'reset.sh', os.X_OK), 'reset.sh must be executable', mode)

# Shell syntax and shellcheck for every script.
shellcheck = os.environ.get('SHELLCHECK') or shutil.which('shellcheck')
for path in sorted(root.glob('scripts/**/*.sh')) + [root / 'go.sh', root / 'reset.sh']:
    subprocess.run(['bash', '-n', str(path)], check=True)
    if shellcheck:
        r = subprocess.run([shellcheck, '-S', 'warning', '-x', '-P', 'SCRIPTDIR', str(path)], capture_output=True, text=True)
        check(r.returncode == 0, path, r.stdout)
print(f'{count} checks passed: same-set rule, OS guards, arguments, install/reinstall/uninstall/kill rules, '
      f'update/run restarts, login registration files, Windows static checks, bash -n'
      f'{", shellcheck" if shellcheck else " (shellcheck not found)"}.')
