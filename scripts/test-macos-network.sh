#!/bin/bash
# Run only on an isolated macOS test runner. Creates a fake Ethernet pair and
# applies routes restricted to RFC 2544 benchmark networks.
set -euo pipefail
cd "$(dirname "$0")/.."
script=RobotNet.Mac/Platform/network.sh
owner=$(id -u)
client=feth30
device=feth31
default_before=$(route -n get default | grep -E 'gateway:|interface:')
forwarding_before=$(sysctl -n net.inet.ip.forwarding)
created_client=0
created_device=0
owns_transaction=0
cleanup() {
    if [ "$owns_transaction" = 1 ]; then sudo -n /bin/sh "$script" rollback "$owner" || true; fi
    if [ "$created_client" = 1 ]; then sudo -n ifconfig "$client" destroy || true; fi
    if [ "$created_device" = 1 ]; then sudo -n ifconfig "$device" destroy || true; fi
}
trap cleanup EXIT
sudo -n test ! -d /var/run/com.long10222.robotnet/active
sudo -n ifconfig "$client" create
created_client=1
sudo -n ifconfig "$device" create
created_device=1
sudo -n ifconfig "$client" peer "$device"
sudo -n ifconfig "$client" inet 198.18.10.2 netmask 255.255.255.0 up
sudo -n ifconfig "$device" up

owns_transaction=1
sudo -n /bin/sh "$script" client "$owner" 198.19.1.0/24 198.18.10.1 "$client" 198.19.1.10 255.255.255.0
route -n get 198.19.1.10 | grep -E 'gateway: 198\.18\.10\.1'
sudo -n /bin/sh "$script" rollback "$owner"
if route -n get 198.19.1.10 | grep -E 'gateway: 198\.18\.10\.1'; then
    echo "Route still active after rollback" >&2
    exit 1
fi

sudo -n /bin/sh "$script" gateway "$owner" "$device" 198.19.1.101 255.255.255.0 198.19.1.0/24 198.18.10.0/24 yes 0xffffff00 198.19.1.10
ifconfig "$device" inet | grep -F 'inet 198.19.1.101'
sudo -n pfctl -a com.apple/com.long10222.robotnet -s nat | grep -F '198.19.1.101'
test "$(sysctl -n net.inet.ip.forwarding)" = 1
sudo -n /bin/sh "$script" rollback "$owner"
owns_transaction=0
test "$(sysctl -n net.inet.ip.forwarding)" = "$forwarding_before"
test -z "$(sudo -n pfctl -a com.apple/com.long10222.robotnet -s nat)"
if ifconfig "$device" inet | grep -F 'inet 198.19.1.101'; then
    echo "Device alias still active after rollback" >&2
    exit 1
fi
test "$(route -n get default | grep -E 'gateway:|interface:')" = "$default_before"
echo "macOS route, alias, PF NAT, forwarding and rollback verified"
