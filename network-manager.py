"""Local GUI backend. Never emits subscription addresses, tokens, or passwords."""
import argparse
import importlib.util
import ipaddress
import json
import os
from pathlib import Path
import re
import socket
import subprocess
import sys
from urllib.parse import urlsplit, quote

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('campus_route', HERE / 'campus-route.py')
campus = importlib.util.module_from_spec(spec)
spec.loader.exec_module(campus)
ROOT = campus.ROOT
RULES = HERE / 'network-rules.json'
KINDS = {'cidr': 'IP-CIDR', 'domain': 'DOMAIN', 'suffix': 'DOMAIN-SUFFIX'}
TARGETS = {'campus': campus.GROUP, 'direct': 'DIRECT', 'vpn': campus.DOCKER}
PROTECTED = [('DOMAIN', 'sslvpn3.scut.edu.cn'), ('IP-CIDR', '183.62.57.37/32')]


def normalize(payload):
    result, seen = [], set()
    if not isinstance(payload, dict) or not isinstance(payload.get('rules'), list):
        raise ValueError('规则文件格式不正确。')
    if len(payload['rules']) > 300:
        raise ValueError('最多支持 300 条规则。')
    for item in payload['rules']:
        kind, route = item.get('kind'), item.get('route')
        if kind not in KINDS or route not in TARGETS:
            raise ValueError('请选择有效的规则类型与出口。')
        value = str(item.get('value', '')).strip()
        if kind == 'cidr':
            try:
                network = ipaddress.ip_network(value, strict=False)
                if network.version != 4:
                    raise ValueError()
                value = str(network)
            except ValueError:
                raise ValueError('IP 网段格式不正确，例如 10.48.0.0/16；单个 IP 可填 10.48.12.129。')
        else:
            try:
                host = urlsplit(value if '://' in value else 'https://' + value).hostname
                if not host:
                    raise ValueError()
                value = host.rstrip('.').lower()
                if kind == 'suffix' and value.startswith('*.'):
                    value = value[2:]
                value = value.encode('idna').decode('ascii')
                if len(value) > 253 or not all(re.fullmatch(r'[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?', p) for p in value.split('.')):
                    raise ValueError()
                try:
                    ipaddress.ip_address(value)
                except ValueError:
                    pass
                else:
                    raise ValueError()
            except (ValueError, UnicodeError):
                raise ValueError('域名格式不正确，例如 www.scut.edu.cn，或粘贴完整 https 网址。')
        if (kind, value) in seen:
            raise ValueError('存在重复规则，请合并相同类型与地址的规则。')
        if (KINDS[kind], value) in PROTECTED:
            raise ValueError('校园 VPN 登录网关固定直连，无需添加。')
        seen.add((kind, value))
        result.append({'enabled': bool(item.get('enabled', True)), 'kind': kind, 'value': value, 'route': route})
    return {'version': 1, 'rules': result}


def read_rules():
    return normalize(json.loads(RULES.read_text(encoding='utf-8-sig')))


def rule_lines(config):
    lines = ['DOMAIN,sslvpn3.scut.edu.cn,DIRECT', 'IP-CIDR,183.62.57.37/32,DIRECT,no-resolve']
    for item in config['rules']:
        if item['enabled']:
            line = ','.join([KINDS[item['kind']], item['value'], TARGETS[item['route']]])
            lines.append(line + (',no-resolve' if item['kind'] == 'cidr' else ''))
    return lines


def make_script(config):
    entries = json.dumps(rule_lines(config), ensure_ascii=True, indent=2)
    return '''// SCUT routing v3: managed by the Windows network assistant.
function main(config, profileName) {
  const campusName = "SCUT-EasyConnect";
  const dockerName = "SCUT-EasyConnect-Docker";
  config.proxies = (config.proxies || []).filter(p => p.name !== campusName && p.name !== dockerName);
  config.proxies.push({name: dockerName, type: "socks5", server: "127.0.0.1", port: 1080, udp: false});
  config["proxy-groups"] = (config["proxy-groups"] || []).filter(p => p.name !== campusName);
  config["proxy-groups"].push({name: campusName, type: "select", proxies: ["DIRECT", dockerName]});
  const campusRules = ''' + entries + ''';
  config.rules = campusRules.concat((config.rules || []).filter(r => !campusRules.includes(r)));
  return config;
}
'''


def transform_source(source, old_config, new_config, pipe, active):
    # Generated YAML has scalar one-line rules. Preserve every unrelated line.
    source = re.sub(r'(?m)^- name: SCUT-EasyConnect[ \t]*\r?\n(?=[ \t]+type: socks5)',
                    '- name: ' + campus.DOCKER + '\n', source)
    if not re.search(r'(?m)^\s*- name: SCUT-EasyConnect-Docker\s*$', source):
        raise ValueError('当前配置缺少校园 Docker 节点，请先在 Clash 重新加载配置。')
    if not re.search(r'(?m)^\s*- name: SCUT-EasyConnect\s*$', source):
        group = 'proxy-groups:\n- name: SCUT-EasyConnect\n  type: select\n  proxies:\n  - DIRECT\n  - SCUT-EasyConnect-Docker\n'
        source, count = re.subn(r'(?m)^proxy-groups:\s*\r?\n', lambda m: group, source)
        if count != 1:
            raise ValueError('无法识别 Clash 代理组配置。')
    old_entries = set(rule_lines(old_config))
    lines = source.splitlines()
    starts = [i for i, line in enumerate(lines) if line == 'rules:']
    if len(starts) != 1:
        raise ValueError('无法识别 Clash 分流规则。')
    start, end = starts[0], starts[0] + 1
    while end < len(lines) and (not lines[end] or lines[end].startswith(('-', ' ', '#'))):
        end += 1
    retained = []
    for line in lines[start + 1:end]:
        entry = re.sub(r'^\s*-\s*', '', line).strip().strip('\'"')
        fields = entry.split(',')
        owned = len(fields) >= 3 and (entry in old_entries or fields[2] in (campus.GROUP, campus.DOCKER))
        if not owned:
            retained.append(line)
    lines[start + 1:end] = ['- ' + entry for entry in rule_lines(new_config)] + retained
    source = '\n'.join(lines) + '\n'
    source = re.sub(r'(?m)^mode:.*$', 'mode: ' + active['mode'], source)
    source = re.sub(r'(?m)^external-controller-pipe:.*$', lambda m: "external-controller-pipe: '\\\\.\\pipe\\" + pipe + "'", source)
    return source


def apply_rules(config):
    old = read_rules()
    pipe = campus.find_pipe()
    active = campus.api(pipe, 'GET', '/configs')
    proxies = campus.api(pipe, 'GET', '/proxies')['proxies']
    script_path = ROOT / 'profiles/Script.js'
    original_script = script_path.read_text(encoding='utf-8-sig')
    if 'const campusName = "SCUT-EasyConnect";' not in original_script:
        raise ValueError('Clash 全局脚本已被修改，未覆盖；请先检查扩展脚本。')
    generated = ROOT / 'clash-verge.yaml'
    runtime = ROOT / 'scut-campus-runtime.yaml'
    source_path = runtime if runtime.exists() and runtime.stat().st_mtime > generated.stat().st_mtime else generated
    source = transform_source(source_path.read_text(encoding='utf-8-sig'), old, config, pipe, active)
    staged = ROOT / 'scut-assistant-staged.yaml'
    staged.write_text(source, encoding='utf-8')
    core = Path(os.environ['ProgramFiles']) / 'Clash Verge/verge-mihomo.exe'
    checked = subprocess.run([str(core), '-t', '-d', str(ROOT), '-f', str(staged)],
                             capture_output=True, timeout=45, creationflags=0x08000000)
    if checked.returncode:
        staged.unlink(missing_ok=True)
        raise ValueError('Clash 配置检查未通过，现有连接配置保持原样。')
    new_script = make_script(config)
    backup = ROOT / 'scut-backup'
    backup.mkdir(exist_ok=True)
    (backup / 'Script.before-assistant.js').write_text(original_script, encoding='utf-8')
    script_path.write_text(new_script, encoding='utf-8')
    try:
        campus.api(pipe, 'PUT', '/configs?force=true', {'payload': source})
    except Exception:
        script_path.write_text(original_script, encoding='utf-8')
        raise
    restore_errors = 0
    for name, proxy in proxies.items():
        if proxy.get('type') == 'Selector' and proxy.get('now'):
            try:
                campus.api(pipe, 'PUT', '/proxies/' + quote(name, safe=''), {'name': proxy['now']})
            except Exception:
                restore_errors += 1
    staged.replace(runtime)
    (HERE / 'clash-scut.js').write_text(new_script, encoding='utf-8')
    RULES.write_text(json.dumps(config, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    return {'ok': True, 'rules': config['rules'], 'restore_warnings': restore_errors}


def port_open(port):
    try:
        with socket.create_connection(('127.0.0.1', port), timeout=0.4):
            return True
    except OSError:
        return False


def status():
    result = {'ok': True, 'clash': False, 'vpn_port': port_open(1080), 'proxy_port': port_open(7897), 'rules': read_rules()['rules']}
    try:
        pipe = campus.find_pipe()
        group = campus.api(pipe, 'GET', '/proxies/' + campus.GROUP)
        active = campus.api(pipe, 'GET', '/configs')
        result.update(clash=True, selected=group.get('now'), mode=active.get('mode'), tun=active.get('tun', {}).get('enable'))
    except Exception:
        pass
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('command', choices=['status', 'apply', 'validate'])
    parser.add_argument('--input')
    args = parser.parse_args()
    if args.command == 'status':
        return status()
    config = normalize(json.loads(Path(args.input).read_text(encoding='utf-8-sig')))
    return {'ok': True, 'rules': config['rules']} if args.command == 'validate' else apply_rules(config)


if __name__ == '__main__':
    try:
        print(json.dumps(main(), ensure_ascii=True))
    except Exception as error:
        message = str(error) if isinstance(error, ValueError) else '操作未完成：' + type(error).__name__ + '。请检查 Clash 是否正在运行。'
        print(json.dumps({'ok': False, 'error': message}, ensure_ascii=True))
        sys.exit(1)
