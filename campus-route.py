"""Switch only the SCUT selector; keep subscription URLs and credentials private."""
import argparse
import http.client
import json
import os
from pathlib import Path
import re
import socket
import subprocess
import sys
import time

ROOT = Path(os.environ['APPDATA']) / 'io.github.clash-verge-rev.clash-verge-rev'
HERE = Path(__file__).resolve().parent
GROUP = 'SCUT-EasyConnect'
DOCKER = 'SCUT-EasyConnect-Docker'


class PipeSocket:
    def __init__(self, file):
        self.file = file

    def makefile(self, *args, **kwargs):
        return self.file


def api(pipe_name, method, path, payload=None):
    body = json.dumps(payload).encode() if payload is not None else b''
    headers = (f'{method} {path} HTTP/1.1\r\nHost: localhost\r\n'
               f'Connection: close\r\nContent-Type: application/json\r\n'
               f'Content-Length: {len(body)}\r\n\r\n')
    with open('\\\\.\\pipe\\' + pipe_name, 'r+b', buffering=0) as pipe:
        pipe.write(headers.encode() + body)
        response = http.client.HTTPResponse(PipeSocket(pipe))
        response.begin()
        raw = response.read()
        if response.status >= 400:
            raise RuntimeError(f'Clash API {method} {path}: HTTP {response.status}')
        return json.loads(raw) if raw else {}


def find_pipe():
    pipes = [p for p in os.listdir('\\\\.\\pipe\\') if p.startswith('verge-mihomo-production')]
    if len(pipes) != 1:
        raise RuntimeError('Expected one running Clash service. Start Clash Verge first.')
    return pipes[0]


def install_selector(pipe_name):
    proxies = api(pipe_name, 'GET', '/proxies')['proxies']
    if proxies.get(GROUP, {}).get('type') == 'Selector':
        if set(proxies[GROUP].get('all', [])) != {'DIRECT', DOCKER}:
            raise RuntimeError('SCUT selector differs from the managed configuration.')
        return
    if proxies.get(GROUP, {}).get('type', '').lower() != 'socks5':
        raise RuntimeError('Existing SCUT SOCKS5 configuration was not found.')
    script = ROOT / 'profiles/Script.js'
    original_script = script.read_text(encoding='utf-8-sig')
    if 'const campusName = "SCUT-EasyConnect";' not in original_script:
        raise RuntimeError('Global script has changed; refusing to overwrite it.')
    source = (ROOT / 'clash-verge.yaml').read_text(encoding='utf-8-sig')
    source, n = re.subn(r'(?m)^- name: SCUT-EasyConnect\s*$', '- name: ' + DOCKER, source)
    if n != 1 or re.search(r'(?m)^\s*- name: SCUT-EasyConnect-Docker\s*$', source) is None:
        raise RuntimeError('Expected exactly one generated SCUT proxy.')
    group_yaml = (f'proxy-groups:\n- name: {GROUP}\n  type: select\n'
                  f'  proxies:\n  - DIRECT\n  - {DOCKER}\n')
    source, n = re.subn(r'(?m)^proxy-groups:\s*\r?\n', lambda m: group_yaml, source)
    if n != 1:
        raise RuntimeError('Expected one generated proxy-groups section.')
    active = api(pipe_name, 'GET', '/configs')
    source = re.sub(r'(?m)^mode:.*$', 'mode: ' + active['mode'], source)
    source = re.sub(r'(?m)^external-controller-pipe:.*$',
                    lambda m: "external-controller-pipe: '\\\\.\\pipe\\" + pipe_name + "'", source)
    runtime = ROOT / 'scut-campus-runtime.yaml'
    previous_runtime = runtime.read_bytes() if runtime.exists() else None
    runtime.write_text(source, encoding='utf-8')
    core = Path(os.environ['ProgramFiles']) / 'Clash Verge/verge-mihomo.exe'
    result = subprocess.run([str(core), '-t', '-d', str(ROOT), '-f', str(runtime)],
                            capture_output=True, timeout=45, creationflags=0x08000000)
    if result.returncode:
        if previous_runtime is not None:
            runtime.write_bytes(previous_runtime)
        raise RuntimeError('Clash configuration validation failed; active configuration was unchanged.')
    backup = ROOT / 'scut-backup'
    backup.mkdir(exist_ok=True)
    backup_file = backup / 'Script.before-campus-selector.js'
    if not backup_file.exists():
        backup_file.write_text(original_script, encoding='utf-8')
    script.write_bytes((HERE / 'clash-scut.js').read_bytes())
    try:
        api(pipe_name, 'PUT', '/configs?force=true', {'payload': source})
    except Exception:
        script.write_text(original_script, encoding='utf-8')
        raise
    # Retain user choices in every pre-existing selector.
    for name, proxy in proxies.items():
        if name != GROUP and proxy.get('type') == 'Selector' and proxy.get('now'):
            from urllib.parse import quote
            api(pipe_name, 'PUT', '/proxies/' + quote(name, safe=''), {'name': proxy['now']})
    print('SCUT_SELECTOR_INSTALLED', flush=True)


def probe_ssh(address, port):
    try:
        with socket.create_connection((address, port), timeout=6) as stream:
            deadline = time.monotonic() + 6
            data = b''
            while len(data) < 4096 and time.monotonic() < deadline:
                stream.settimeout(max(0.1, deadline - time.monotonic()))
                chunk = stream.recv(512)
                if not chunk:
                    break
                data += chunk
                if re.search(br'(?m)^SSH-2\.0-', data):
                    print('SSH_BANNER_OK ' + address + ':' + str(port), flush=True)
                    return True
        print('SSH_BANNER_MISSING', flush=True)
    except OSError as error:
        print('SSH_PROBE_FAILED ' + type(error).__name__, flush=True)
    return False


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['direct', 'vpn', 'status'])
    parser.add_argument('--probe', action='store_true')
    args = parser.parse_args()
    pipe_name = find_pipe()
    if args.mode != 'status':
        install_selector(pipe_name)
        selected = 'DIRECT' if args.mode == 'direct' else DOCKER
        api(pipe_name, 'PUT', '/proxies/' + GROUP, {'name': selected})
    group = api(pipe_name, 'GET', '/proxies/' + GROUP)
    active = api(pipe_name, 'GET', '/configs')
    print(json.dumps({'campus_route': group.get('now'), 'clash_mode': active.get('mode'),
                      'tun': active.get('tun', {}).get('enable')}, ensure_ascii=False), flush=True)
    if args.mode != 'status' and active.get('mode') != 'rule':
        raise RuntimeError('Clash must use Rule mode for campus splitting.')
    if args.probe:
        settings = json.loads((HERE / 'settings.json').read_text(encoding='utf-8-sig'))
        return 0 if probe_ssh(settings['ServerAddress'], settings['SshPort']) else 2
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except Exception as error:
        # Never include config contents, remote URLs, or API payloads in diagnostics.
        if isinstance(error, RuntimeError):
            print('ERROR: ' + str(error), file=sys.stderr)
        else:
            print('ERROR: ' + type(error).__name__, file=sys.stderr)
        sys.exit(1)
