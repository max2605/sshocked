# SSH forwarding profiles

Forwarding profiles store one explicit local, remote or dynamic route per server.
Manage them under **Port forwarding** or through `ssk forward`. Profiles use the
existing key/password/secret and Bastion-tunnel credential pipeline. Native Bastion
AAD sessions are unsupported for managed forwarding; use a key/password tunnel.

Local forwarding exposes a listener on this machine and connects to the destination
from the SSH server. Remote forwarding listens on the SSH server and connects to
the destination from this machine. Dynamic forwarding exposes a local SOCKS4/5
proxy and resolves its per-request destinations through the SSH server.

Listeners default to `127.0.0.1`. Numeric IPv4/IPv6 and `localhost` bind addresses
are accepted. Other addresses need an explicit saved `AllowNonLoopbackBind` choice
or CLI `--allow-external`; `--loopback-only` clears that choice. Remote server
`GatewayPorts`, privilege and forwarding policies can still reject or constrain
requested remote bindings. Profile routes show the requested effective direction;
the server remains authoritative for remote listening policy.

```sh
ssk forward create database --server prod-db --kind local --listen-port 15432 --target 127.0.0.1 --target-port 5432
ssk forward create remote-web --server jump --kind remote --listen-port 18080 --target 127.0.0.1 --target-port 8080
ssk forward create socks --server jump --kind dynamic --listen-port 1080
ssk forward list
ssk forward edit database --listen-port 25432
ssk forward start database
ssk forward status --output json
ssk forward stop database
ssk forward delete database --force
```

CLI `start` remains in the foreground until stopped, disconnected or cancelled.
`Ctrl+C` stops its SSH child and releases credentials. Another CLI process using the
same configuration directory can read status and request stop. The owner verifies
its lifetime, handles the request and cleans up; the client never blindly kills a
stored PID. In the TUI, started profiles remain owned by that app until explicitly
stopped or the application exits. Editing/deleting an active profile is rejected.
There is no automatic reconnect or startup on launch.

Local/dynamic listener conflicts are checked before credential preparation. SSH
also receives `ExitOnForwardFailure=yes`, and remote listener failures are handled
independently. Status becomes `Active` after the managed OpenSSH client's DEBUG1
authentication and matching listener channel creation/remote-forward acknowledgement
signals. The pre-bind diagnostic line alone is not considered a successful listener.
Authentication, host-key, bind and startup-timeout errors produce failed status.
Startup diagnostic output is bounded and is not stored verbatim. Active profiles
continue draining routine traffic diagnostics without a lifetime byte quota;
individual diagnostic lines remain bounded and failure exits are still reported.

An established forward proves listener setup, not destination application health:
the eventual target connection may still fail. A remote server may restrict its
listening address through `GatewayPorts`; inspect the server policy when exposing a
remote listener beyond loopback. Privileged ports require appropriate OS/server
permissions.

Profiles launch with `-N`, closed input and strict trusted-host-key checking. SSH
settings/identity/ProxyJump explicitly represented by the application are reused.
`-F none` prevents additional forwarding directives or background behavior from
ordinary SSH configuration from opening unrequested listeners. Any required custom
connection settings should be configured in the application's host options. Extra
forwards, diagnostic redirection, daemonization and multiplex-control options are
rejected for managed profiles. SSH's default identity discovery, agent and known-host
files remain available. Missing credentials or unknown keys fail without competing
interactive prompts.

Runtime state is separate from `appconfig.json`, under the configuration directory's
`forwarding` folder. Current-user access and atomic replacement protect records;
owner PID plus process creation time distinguish stale/reused process IDs. A crashed
owner is reported as unconfirmed/failed rather than active. Normal stop, cancellation
and application disposal clean up the SSH process and credential lease. Abrupt OS
termination can leave resources just as it can for other managed SSH sessions;
status never assumes a stale PID still owns a listener.

Automated tests use fake SSH children and controlled loopback port-conflict fixtures.
They do not create real remote forwards. Optional deployment smoke tests should
use trusted hosts, nonprivileged loopback ports and harmless local services.

See [OpenSSH forwarding options](https://man.openbsd.org/ssh) and
[ExitOnForwardFailure](https://man.openbsd.org/ssh_config#ExitOnForwardFailure).
