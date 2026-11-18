import unittest
from unittest.mock import patch
import preflight
class Tests(unittest.TestCase):
    def test_missing_runtimes_cannot_claim_validation(self):
        with patch('preflight.shutil.which', return_value=None):
            r = preflight.inspect()
        self.assertEqual(r['status'], 'BLOCKED_NO_USABLE_RUNTIME')
        self.assertFalse(r['simulation_executed'])
        self.assertFalse(r['route_nat_verified'])
        self.assertFalse(r['physical_robot_verified'])
    def test_runtime_alone_not_success(self):
        with patch('preflight.shutil.which', side_effect=lambda x: '/mock/vbox' if x == 'VBoxManage' else None):
            r = preflight.inspect()
        self.assertEqual(r['status'], 'REQUIRES_ISOLATION_AND_VERSION_REVIEW')
        self.assertFalse(r['simulation_executed'])
if __name__ == '__main__': unittest.main()
