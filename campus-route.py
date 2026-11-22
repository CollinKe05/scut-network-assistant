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
GROUP = 'Campus-Network'
DOCKER = 'Campus-Docker'


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
    if proxies.get(GROUP, {}).get('type') != 'Selector' or set(proxies[GROUP].get('all', [])) != {'DIRECT', DOCKER}:
        raise RuntimeError('请先完成首次配置向导：导入校园配置或安装全局扩展脚本，然后在 Clash 重新加载配置。')


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
        if settings.get('ServerAddress'):
            return 0 if probe_ssh(settings['ServerAddress'], settings['SshPort']) else 2
        import urllib.request
        try:
            opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
            with opener.open(settings['ProbeUrl'], timeout=6) as response:
                print('HTTP_PROBE_OK ' + str(response.status), flush=True)
                return 0
        except Exception:
            print('HTTP_PROBE_FAILED', flush=True)
            return 2
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
