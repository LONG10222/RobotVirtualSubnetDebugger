"""Read-only host readiness evidence. Never installs or changes networking."""
import json, os, platform, shutil, subprocess
from pathlib import Path

def inspect():
    runtime = {name: shutil.which(name) for name in ('docker', 'podman', 'VBoxManage', 'qemu-system-x86_64')}
    docker_access = False
    detail = 'Docker is not installed'
    if runtime['docker']:
        try:
            p = subprocess.run([runtime['docker'], 'info', '--format', '{{.ServerVersion}}'], capture_output=True, text=True, timeout=10)
            docker_access = p.returncode == 0
            detail = (p.stdout or p.stderr).strip()[:1000]
        except (OSError, subprocess.TimeoutExpired) as e:
            detail = str(e)
    return {'schema_version': 1, 'host': platform.platform(), 'runtime_paths': runtime,
            'docker_daemon_accessible': docker_access, 'docker_detail': detail,
            'kvm_device_exists': Path('/dev/kvm').exists(),
            'free_disk_bytes': shutil.disk_usage('.').free,
            'effective_uid': os.geteuid() if hasattr(os, 'geteuid') else None,
            'status': 'REQUIRES_ISOLATION_AND_VERSION_REVIEW' if docker_access or runtime['VBoxManage'] else 'BLOCKED_NO_USABLE_RUNTIME',
            'simulation_executed': False, 'route_nat_verified': False,
            'physical_robot_verified': False}
if __name__ == '__main__':
    print(json.dumps(inspect(), indent=2, ensure_ascii=False))
