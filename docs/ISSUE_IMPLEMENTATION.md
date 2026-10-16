# Issue implementation guide

The `Max_Codex` branch implements the following open feature requests. Each feature and follow-up correction has its own commit with the reason, behavior, and validation in the commit message.

| Issue | Available behavior |
| --- | --- |
| #27, #39 | Azure Bastion native SSH and managed loopback tunnels; AAD/key/password configuration; per-child tunnel lifetime and cleanup |
| #28 | SCP upload, quick upload, download, Unix remote browser, recursive transfer, and group transfers with independent outcomes |
| #30 | Authentication/status badges, favorites, recent timestamps, container counts, compact tables, breadcrumbs, and footer; a single dark palette after #67 |
| #31 | Discovered SSH identity selection with public fingerprint/comment and agent status |
| #32 | Persistent host/container favorites and bounded recent history with connection counts |
| #33 | Structured audit events, privacy controls, retention, and filtered table/JSON/JSONL/CSV history |
| #34 | Extensible named secret providers with Azure Key Vault, memory cache, temporary key/certificate files, and SSH password helper |
| #35 | Output-only recording core, timed asciinema v2 casts, replay controls, export/delete, compression, and retention; PTY/input capture remains unimplemented |
| #36 | Per-host/global lifecycle hooks, tag filters, local timeouts, remote shell wrapper, and audited outcomes |
| #37 | OpenSSH import/export with conflict resolution, preserved manual content, backups, and optional automatic sync |

## Menus and commands

The main menu adds **Favorites**, **Recent connections**, and **SSH Config Sync**. Server actions include file transfers and remote browsing. Group actions include upload/download to each server. Settings provides secret providers, global hooks, session recording, display settings, and recent tracking. Add/Edit server forms select discovered keys, Azure configuration, secret references, hooks, and the recording policy.

```sh
ssk history --last 20
ssk history --type connection --server web --since 2026-10-01 --output json
ssk audit status
ssk audit settings
ssk sessions status
ssk sessions enable
ssk sessions list --server web --last 10
ssk sessions replay SESSION_ID --speed 2 --no-controls
ssk sessions export SESSION_ID --format text --output session.txt
ssk sessions delete --older 30d
```

Audit settings accept `--retention-days N`, `--max-entries N`, `--command-text true|false`, `--file-paths true|false`, and `--secret-access true|false`. Recording is off initially. Host recording overrides and exclusion rules still apply after disabling the global default. Output may contain sensitive values; input is never captured by this implementation.

Set `SSHOCKED_CONFIG_DIR` to use a separate configuration, log, audit, and session directory. Child terminals receive the same directory. Config read-modify-write, audit append/retention, recording management, and diagnostic writes are serialized between processes. Application configuration is replaced atomically. A full `Save` is still a snapshot replacement API; services use `Update` to retain unrelated concurrent edits.

## Azure Bastion

Azure CLI 2.62 or later, an authenticated account, and the `bastion` extension are checked before connecting. Native SSH also requires the `ssh` extension for every authentication mode. The Bastion deployment must support native clients and its own network/VM permissions must allow the target.

Native sessions delegate to `az network bastion ssh`. Managed tunnels delegate to `az network bastion tunnel`, bind a loopback port, and run normal OpenSSH against that port. Tunnels use a stable VM `HostKeyAlias` so an ephemeral local port does not become the known-host identity. AAD requires native Azure CLI certificate handling; tunnels support SSH keys/passwords.

The Azure CLI native implementation currently disables inner SSH host-key checking and discards the inner SSH exit code. A zero native CLI exit therefore establishes only that Azure CLI returned zero. Use tunnel mode with key/password authentication when host-key verification or checked remote-command exit codes matter. Native remote commands/hooks are rejected on Windows because the upstream extension launches its inner command through a local shell. Native interactive connections remain available; Windows shell metacharacters in usernames/key paths are rejected. See the upstream [Bastion extension implementation](https://github.com/Azure/azure-cli-extensions/blob/main/src/bastion/azext_bastion/custom.py).

Captured Azure CLI certificate verification failures, including `CERTIFICATE_VERIFY_FAILED` during tunnel startup or prerequisite checks, display an actionable setup error. Direct CLI connections exit with a nonzero status without an unhandled-exception stack trace; interactive connections return to the menu. Failed tunnels are disposed before reporting the error.

For a corporate SSL/TLS inspection proxy, obtain the trusted root/intermediate CA certificates from your organization and include them in a PEM bundle alongside the public CAs. Set `REQUESTS_CA_BUNDLE` in the shell that launches Sshocked, then retry. Azure CLI inherits this environment variable:

```powershell
$env:REQUESTS_CA_BUNDLE = 'C:\certs\ca-bundle.pem'
ssk private-vm
```

For Command Prompt, use `set "REQUESTS_CA_BUNDLE=C:\certs\ca-bundle.pem"` before launching Sshocked. See [Azure CLI proxy/certificate troubleshooting](https://learn.microsoft.com/en-us/cli/azure/use-azure-cli-successfully-troubleshooting#work-behind-a-proxy).

## Credentials

Configure named Azure Key Vault providers in Settings and choose key/password/sudo secret references in the host form. Authentication uses Azure Identity's default credential chain. Secret values remain in memory, with a bounded optional TTL cache; setting TTL to zero disables caching. No secret value is serialized to configuration or appended as a process argument by the credential integration.

Key and certificate material is written to a temporary file with current-user ACLs on Windows or mode 0600 on Unix and removed when the connection lease ends. Abruptly terminating the Windows process can leave its protected temporary directory; it must then be removed manually. Certificates must contain an exportable private key in supported PEM/PKCS12 form.

SSH passwords use the executable as an OpenSSH askpass helper and travel through the child environment. Launch `ssk` directly; `dotnet ssk.dll` cannot serve as the helper. Captured Docker scans/group commands send sudo passwords through stdin. Interactive Docker sessions use the ordinary remote sudo prompt; automatic vault sudo input is unavailable in those sessions. Keys with passphrases use OpenSSH or the SSH agent prompt.

Diagnostic and audit logs redact common password/token patterns, but arbitrary command output/scripts are not guaranteed secret-free. Command text/file paths can be omitted in audit settings. Generated hook scripts are excluded from command-text audit. SSH password/key acquisition is not itself recorded unless secret-access auditing is enabled.

## Scope and platform limits

- Remote browsing uses Unix shell tools. SCP uses modern OpenSSH's SFTP transport; legacy `-O` is rejected. AAD does not support SCP through a plain tunnel.
- Local post-connect hooks run after the child process starts, before authentication can be confirmed. Local disconnect hooks run after child exit. Remote post-connect/pre-disconnect hooks run around the remote interactive shell; network loss can prevent remote cleanup. Per-hook timeout is local only.
- Output recording uses pipes, with inherited stdin. It does not emulate a terminal, capture keystrokes, provide script(1) files, or record native Bastion sessions. Full PTY capture remains a follow-up for #35. Cast export is not video encoding.
- Recording retention excludes active/incomplete files so another process's recording survives cleanup. Interrupted recordings require explicit deletion. The size setting is a completed-recording cleanup target, not a live disk quota.
- SSH config export refuses symbolic-link destinations to preserve link identity. Select a physical destination. Import does not evaluate dynamic `Match` blocks; monitoring covers the main file and the selected managed Include file.
- Reachability checks are TCP probes and do not validate SSH credentials. They are enabled by default and can be disabled in Display settings.

## Validation

The test project exercises atomic/concurrent persistence, parsing/sync, literal argument construction, tunnel leases, secret cache/temporary files, protected stdin, process cancellation, lifecycle ordering, file-transfer outcomes, audit retention, recording failure cleanup/replay, real loopback reachability, and rendered markup.

```sh
dotnet test Sshocked.slnx --configuration Release
dotnet publish src/Sshocked/Sshocked.csproj --configuration Release --runtime win-x64 --self-contained true
```

Windows Native-AOT publishing was validated locally. Two symbolic-link regression tests are skipped on Windows because link creation requires privileges unavailable on the test host; they run on Unix. Real remote SSH/SCP/Azure connections require deployment credentials and targets and were not exercised by this local test run.
