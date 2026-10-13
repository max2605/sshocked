sshocked 2.1.1 fixes Windows server-table rendering from [#65](https://github.com/ms-sk/sshocked/issues/65) and adds local host resolution from [#66](https://github.com/ms-sk/sshocked/issues/66).

## Highlights

- **Stable Windows tables:** layout follows the visible console window, avoiding broken rows when the scroll buffer is wider. Maximizing, restoring or resizing redraws the complete table view while preserving the selected menu entry.
- **Aligned groups:** corresponding columns use identical widths across the server overview, including Uncategorized. Long hostnames, tags and container names wrap within those shared columns in normal and compact layouts.
- **Host resolution:** a new column beside Host shows locally resolved IPv4/IPv6 addresses or concise DNS errors. Literal IP addresses appear immediately; multiple addresses use separate lines.

## Resolution and display settings

Hostname lookups run in the background, with at most four lookups at a time and a two-second timeout. Results and errors are cached for five minutes. Completed lookups update open table menus automatically without changing the selection; cached results remain visible during a refresh.

The Host resolution column is enabled by default. Hiding it under **Settings → Display settings** stops scheduling lookups. DNS resolution is independent of the TCP reachability setting: resolving a host does not confirm that its SSH port is reachable. Jump-host and Bastion destinations are resolved locally, so the displayed result does not identify the address selected by SSH or resolved by a remote jump host.

Windows startup initializes UTF-8 before rendering, and table layout reserves a small right-edge margin to avoid console wrapping.

## Compatibility

Existing configuration, saved credentials, SSH keys and connection history remain compatible. The new column uses its default setting when reading an existing configuration. No server-side changes are required.

Native AOT downloads remain available for Windows x64, Linux x64/ARM64 and macOS x64, with Debian packages for both Linux architectures. OpenSSH Client is still required for SSH connections.

## Validation

Windows validation passed 304 tests, with two symbolic-link tests skipped because the required platform privilege was unavailable. Another 95 checks exercised an actual Windows console, including maximized/restored layouts, shared column boundaries, background DNS completion and preserved menu selection. Windows Native AOT publishing and the Unicode askpass helper were also checked.

See the [display-settings guide](https://github.com/ms-sk/sshocked/blob/v2.1.1/docs/VISUALS.md) for status indicators and host-resolution behavior.

**Full changelog:** https://github.com/ms-sk/sshocked/compare/v2.1.0...v2.1.1
