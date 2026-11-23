#!/bin/sh
set -eu
export PATH=/usr/bin:/bin:/usr/sbin:/sbin LC_ALL=C
umask 077

action=${1:?}
owner=${2:?}
state=/var/run/com.long10222.robotnet
journal=$state/active
anchor=com.apple/com.long10222.robotnet

fail() { printf '%s\n' "$*" >&2; exit 1; }
[ "$(id -u)" = 0 ] || fail '请使用管理员权限运行此脚本。'
mkdir -p "$state"
[ "$(stat -f '%u:%Lp' "$state")" = 0:700 ] && [ ! -L "$state" ] ||
    fail '网络操作目录权限异常。'
shlock -f "$state/lock" -p $$ || fail '另一个 RobotNet 网络操作正在运行。'
trap 'rm -f "$state/lock"' EXIT

rollback() (
    failed=0
    undo() {
        file=$1
        shift
        [ -f "$journal/$file" ] || return 0
        if "$@"; then
            rm -f "$journal/$file"
        else
            failed=1
        fi
    }
    drop_route() {
        read -r subnet gateway dev mask < "$journal/route"
        lookup=$(route -n get -net "$subnet" 2>/dev/null || true)
        [ -n "$lookup" ] || return 0
        printf '%s\n' "$lookup" | awk -v g="$gateway" -v i="$dev" -v n="${subnet%/*}" -v m="$mask" '
            $1=="destination:" && $2==n {n_ok=1}
            $1=="mask:" && $2==m {m_ok=1}
            $1=="gateway:" && $2==g {g_ok=1}
            $1=="interface:" && $2==i {i_ok=1}
            $1=="flags:" && $0 ~ /STATIC/ {s_ok=1}
            END {exit !(n_ok && (m_ok || m=="255.255.255.255") && g_ok && i_ok && s_ok)}' || return 0
        route -n delete -net "$subnet" "$gateway"
    }
    drop_alias() {
        read -r dev ip mask < "$journal/alias"
        if ! ifconfig "$dev" inet 2>/dev/null | awk -v ip="$ip" -v mask="$mask" '
            $1=="inet" && $2==ip && $4==mask {found=1} END {exit !found}'; then
            return 0
        fi
        ifconfig "$dev" inet "$ip" -alias
    }
    release_pf() {
        token=$(cat "$journal/pf-token")
        refs=$(pfctl -s References 2>&1) || return 1
        if printf '%s\n' "$refs" | awk -v t="$token" '{for(i=1;i<=NF;i++)if("x"$i=="x"t)found=1}END{exit !found}'; then
            pfctl -X "$token"
        fi
    }
    undo route drop_route
    undo forwarding sysctl -w net.inet.ip.forwarding=0
    undo nat pfctl -a "$anchor" -F nat
    undo pf-token release_pf
    undo alias drop_alias
    [ "$failed" = 0 ] || return 1
    rm -f "$journal/owner"
    rmdir "$journal"
)

if [ "$action" = rollback ]; then
    [ -d "$journal" ] || exit 0
    [ "$owner" = "$(cat "$journal/owner")" ] || fail '此配置由另一个 macOS 用户应用。'
    rollback || fail '部分网络配置回滚失败，可修正系统状态后重试。'
    printf '%s\n' '已恢复 RobotNet 网络配置。'
    exit 0
fi

[ "$action" = client ] || [ "$action" = gateway ] || fail '未知操作。'
mkdir "$journal" 2>/dev/null || fail '已有 RobotNet 网络配置，请先回滚。'
printf '%s\n' "$owner" > "$journal/owner"
finish() {
    status=$?
    if [ "$status" != 0 ]; then
        rollback >&2 || printf '%s\n' '自动回滚未完成，请重试回滚。' >&2
    fi
    rm -f "$state/lock"
    exit "$status"
}
trap finish EXIT
trap 'exit 1' HUP INT TERM
if [ "$action" = client ]; then
    subnet=$3 gateway=$4 dev=$5 target=$6 mask=$7
    route -n get "$gateway" | awk -v i="$dev" '
        $1=="interface:" && $2==i {i_ok=1}
        $1=="flags:" && $0 !~ /<[^>]*GATEWAY/ {direct=1}
        END {exit !(i_ok && direct)}' || fail '主机 A 已不在选定的本地网卡上。'
    route -n add -net "$subnet" "$gateway"
    printf '%s %s %s %s\n' "$subnet" "$gateway" "$dev" "$mask" > "$journal/route"
    route -n get "$target" | awk -v g="$gateway" -v i="$dev" '
        $1=="gateway:" && $2==g {g_ok=1}
        $1=="interface:" && $2==i {i_ok=1}
        END {exit !(g_ok && i_ok)}' || fail '目标设备路由被其他更优先的路由覆盖。'
else
    dev=$3 ip=$4 mask=$5 subnet=$6 lan=$7 nat=$8
    if [ "$nat" = yes ]; then
        pfctl -s nat | grep -F 'nat-anchor "com.apple/*"' >/dev/null ||
            fail '当前 PF 主规则未加载 macOS 默认 NAT 锚点。'
        [ -z "$(pfctl -a "$anchor" -s nat)" ] || fail 'RobotNet PF 锚点已经存在规则。'
    fi
    hexmask=$9 target=${10}
    existing=$(ifconfig "$dev" inet | awk -v ip="$ip" '$1=="inet" && $2==ip {print $4}')
    if [ -z "$existing" ]; then
        ifconfig "$dev" inet "$ip" netmask "$mask" alias
        printf '%s %s %s\n' "$dev" "$ip" "$hexmask" > "$journal/alias"
    else
        [ "$existing" = "$hexmask" ] || fail '该网口地址已存在，子网掩码与计划不同。'
    fi
    route -n get "$target" | awk -v i="$dev" '
        $1=="interface:" && $2==i {i_ok=1}
        $1=="flags:" && $0 !~ /<[^>]*GATEWAY/ {direct=1}
        END {exit !(i_ok && direct)}' || fail '目标设备流量未指向设备网口。'
    if [ "$nat" = yes ]; then
        rule="nat on $dev inet from $lan to $subnet -> $ip"
        printf '%s\n' "$rule" | pfctl -n -a "$anchor" -f -
        : > "$journal/nat"
        printf '%s\n' "$rule" | pfctl -a "$anchor" -f -
        enabled=$(pfctl -E 2>&1) || fail "$enabled"
        token=$(printf '%s\n' "$enabled" | awk '$1=="Token" && $2==":" {print $3} $1=="Token:" {print $2}')
        case "$token" in ''|*[!0-9]*) fail "无法读取 PF 引用令牌：$enabled";; esac
        printf '%s\n' "$token" > "$journal/pf-token"
    fi
    if [ "$(sysctl -n net.inet.ip.forwarding)" = 0 ]; then
        : > "$journal/forwarding"
        sysctl -w net.inet.ip.forwarding=1
    fi
fi
printf '%s\n' 'macOS 网络配置已应用。'
