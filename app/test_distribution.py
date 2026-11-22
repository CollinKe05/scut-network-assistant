"""Offline publication checks: no private defaults or installed-software dependency."""
import json
from pathlib import Path
import unittest
import zipfile

ROOT=Path(__file__).resolve().parents[1]


class DistributionTests(unittest.TestCase):
    def test_default_rules_are_school_domain_only(self):
        rules=json.loads((ROOT/'network-rules.json').read_text(encoding='utf-8-sig'))['rules']
        self.assertEqual(rules,[{'enabled':True,'kind':'suffix','value':'scut.edu.cn','route':'campus'}])
        script=(ROOT/'clash-scut.js').read_text()
        self.assertNotIn('IP-CIDR',script)

    def test_personal_fields_are_empty(self):
        settings=json.loads((ROOT/'settings.example.json').read_text(encoding='utf-8-sig'))
        for key in ['ServerAddress','ServerUser','PythonPath','DockerPath','ClashPath','VncViewerPath','TermiusPath']:
            self.assertEqual(settings[key],'',key)
        self.assertFalse(settings['SetupCompleted'])
        self.assertNotIn('Password',settings)

    def test_new_release_has_distinct_data_and_container_names(self):
        compose=(ROOT/'compose.yaml').read_text()
        self.assertIn('container_name: campus-easyconnect',compose)
        self.assertNotIn('container_name: scut-easyconnect',compose)
        self.assertIn('127.0.0.1:6080:8080',compose)
        self.assertIn('USE_NOVNC:',compose)
        runtime=(ROOT/'app/RuntimePack.cs').read_text()
        self.assertIn('CampusNetworkAssistant',runtime)
        self.assertIn('1.1.0',runtime)

    def test_network_actions_require_confirmation(self):
        launch=(ROOT/'Launch-SCUT.ps1').read_text(encoding='utf-8-sig')
        self.assertLess(launch.index('Confirm-CampusNetworkChange $NetworkChangeConfirmed'),launch.index('network-manager.py'))
        self.assertIn('MessageBoxButtons.YesNo',(ROOT/'app/NetworkAssistant.cs').read_text())
        self.assertIn("if ($containerRunning -eq 'true')",(ROOT/'Start-EasyConnect.ps1').read_text(encoding='utf-8-sig'))

    def test_payload_has_helpers_and_never_local_secrets(self):
        archive=ROOT/'app/payload.zip'
        if not archive.exists():
            self.skipTest('Build the EXE first to inspect payload')
        with zipfile.ZipFile(archive) as package:
            names=package.namelist()
            self.assertIn('Common.ps1',names)
            self.assertIn('network-manager.py',names)
            self.assertNotIn('settings.json',names)
            self.assertNotIn('.env',names)
            self.assertTrue(all('..' not in Path(n).parts and not n.startswith('/') for n in names))
            self.assertEqual(len(json.loads(package.read('network-rules.json'))['rules']),1)
            for name in names:
                self.assertNotIn('browser-profile',name)
                self.assertNotIn('.git/',name)
            self.assertNotIn(b'\r\n',package.read('compat-ps.sh'))


if __name__=='__main__':
    unittest.main()
