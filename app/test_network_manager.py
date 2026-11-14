"""Offline checks for user-controlled routing input and subscription preservation."""
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('network_manager', Path(__file__).resolve().parents[1] / 'network-manager.py')
manager = importlib.util.module_from_spec(spec)
spec.loader.exec_module(manager)


class RulesTest(unittest.TestCase):
    def normalize(self, kind, value, route='campus'):
        return manager.normalize({'rules': [{'enabled': True, 'kind': kind, 'value': value, 'route': route}]})

    def test_url_domain_and_ip_normalization(self):
        self.assertEqual(self.normalize('domain', 'https://WWW.Example.COM:443/watch?v=1')['rules'][0]['value'], 'www.example.com')
        self.assertEqual(self.normalize('suffix', '*.example.com')['rules'][0]['value'], 'example.com')
        self.assertEqual(self.normalize('cidr', '10.48.12.129/16')['rules'][0]['value'], '10.48.0.0/16')
        self.assertEqual(self.normalize('cidr', '10.48.12.129')['rules'][0]['value'], '10.48.12.129/32')

    def test_bad_rules_are_rejected_before_writing(self):
        for kind, value in [('cidr', '300.1.1.1'), ('cidr', '::/0'), ('domain', 'bad_domain.com'),
                            ('domain', 'example.com,DIRECT'), ('domain', '10.48.12.129'), ('domain', '')]:
            with self.subTest(value=value), self.assertRaises(ValueError):
                self.normalize(kind, value)
        a = self.normalize('domain', 'example.com')
        with self.assertRaises(ValueError):
            manager.normalize({'rules': a['rules'] * 2})

    def test_rule_changes_retain_subscription_and_node_definitions(self):
        source = '''mode: rule
external-controller-pipe: old
proxies:
- name: SCUT-EasyConnect-Docker
  type: socks5
  server: 127.0.0.1
  port: 1080
- name: OriginalNode
  type: socks5
  server: 192.0.2.1
  port: 9000
proxy-groups:
- name: SCUT-EasyConnect
  type: select
  proxies:
  - DIRECT
  - SCUT-EasyConnect-Docker
- name: OriginalGroup
  type: select
  proxies:
  - OriginalNode
rules:
- DOMAIN,sslvpn3.scut.edu.cn,DIRECT
- IP-CIDR,10.48.0.0/16,SCUT-EasyConnect,no-resolve
- DOMAIN,example.com,OriginalGroup
- MATCH,OriginalGroup
profile:
  store-selected: true
'''
        old = self.normalize('cidr', '10.48.0.0/16')
        new = self.normalize('domain', 'https://example.com/path', 'direct')
        updated = manager.transform_source(source, old, new, 'test-pipe', {'mode': 'rule'})
        self.assertIn('- DOMAIN,example.com,DIRECT', updated)
        self.assertIn('- DOMAIN,example.com,OriginalGroup', updated)
        self.assertIn('- MATCH,OriginalGroup\nprofile:\n  store-selected: true', updated)
        self.assertNotIn('IP-CIDR,10.48.0.0/16,SCUT-EasyConnect', updated)
        for key in ['proxies:', 'proxy-groups:']:
            original = source.split(key, 1)[1].split('rules:', 1)[0]
            changed = updated.split(key, 1)[1].split('rules:', 1)[0]
            self.assertEqual(original, changed)


if __name__ == '__main__':
    unittest.main()
