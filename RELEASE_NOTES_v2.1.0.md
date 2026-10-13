sshocked 2.1.0 implements the fifteen user-feedback issues #42–#56 from [PR #57](https://github.com/ms-sk/sshocked/pull/57), improving everyday SSH navigation, command execution and local credential handling.

## Highlights

- **Persistent command sessions and reusable macros:** stream remote stdout/stderr while commands run, preserve working directory and environment in one POSIX shell, explicitly record commands, and replay saved sequences on servers, groups or tag matches. A failed command stops its target's sequence while other targets still report independent results.
- **Tags:** assign case-insensitive tags alongside groups and operate on the live set of matching hosts for connections, commands, macros and file transfers.
- **Windows local passwords:** save, update and clear per-server passwords with current-user DPAPI encryption, restricted credential-file access and endpoint binding. Passwords stay out of configuration JSON and SSH arguments; changing an endpoint requires saving its password again.
- **Activity and recording:** one settings module and the `ssk activity` CLI cover structured events, timed session playback, export and retention.

## Navigation and usability

- Light and Dark themes now have distinct backgrounds, semantic colors and selection contrast, with changes applied immediately.
- Browse, Favorites and Recent connections share the complete server action menu. Back restores the originating menu and breadcrumbs.
- Path prompts accept quoted Windows paths and support caret movement, Home/End, Delete/Backspace, Escape cancellation and Windows console mouse positioning.
- Dynamic menu rows receive unique available shortcuts while reserving explicit action keys.
- Group management uses consistent menus; an empty name cancels creation, and confirmed group deletion retains its servers, saved containers and history.
- Group connections open parallel terminal tabs, including configurations previously set to Sequential.
- TCP reachability probes are enabled by default and distinguish Checking, Online, Offline, Unknown, expired results and routed endpoints. Reachability describes the TCP port, not SSH authentication.
- UTF-8 handling preserves Unicode passwords and captured remote output on Windows.

## Compatibility and platform notes

Existing configuration, event history and recordings remain readable. The `history`, `audit` and `sessions` CLI commands continue as compatibility aliases. Existing Sequential settings remain readable but group connections now run in parallel.

Saved local passwords require Windows DPAPI and are tied to the Windows user. Linux/macOS continue to use the existing authentication methods and configured secret providers. Remote macro sessions require `sh`; interactive programs should use Connect, and native Azure Bastion AAD command sessions require an SSH key/password tunnel.

Output recording remains opt-in. Macro commands are stored locally and recorded remote output can contain sensitive data; keep secrets in remote credential references.

Native AOT downloads are supplied for Windows x64, Linux x64/ARM64 and macOS x64, with Debian packages for both Linux architectures. OpenSSH Client is still required. The attached installers download the matching latest-release archive; Windows ARM64 and macOS ARM64 archives are not provided by this release workflow.

## Validation

The feature integration passed 253 tests on Linux and 251 tests on Windows with the two platform-dependent symbolic-link tests skipped. Windows Native AOT publishing and Unicode askpass-helper checks succeeded. Live SSH/SCP/Azure targets were not exercised.

See the [user-feedback guide](https://github.com/ms-sk/sshocked/blob/v2.1.0/docs/USER_FEEDBACK.md), [macro guide](https://github.com/ms-sk/sshocked/blob/v2.1.0/docs/MACROS.md) and [activity guide](https://github.com/ms-sk/sshocked/blob/v2.1.0/docs/ACTIVITY_AND_RECORDING.md) for usage and constraints.

**Full changelog:** https://github.com/ms-sk/sshocked/compare/v2.0.0...v2.1.0