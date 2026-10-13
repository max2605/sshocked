# Host indicators and display settings

Open **Display settings** under **Settings** to choose compact tables, the status, host-resolution and last-connected columns, navigation breadcrumbs, and the version/configuration footer. The terminal uses a single dark palette.

Server tables show favorite stars first, colored authentication badges, Docker container counts, tags, and relative connection times. Counts deduplicate containers that appear in both a scan and the saved list. Normal tables retain favorite indicators and last-connected times on saved-container rows; compact mode shows host rows only. A host configured for Azure Bastion always displays the Azure badge even if its underlying authentication is an SSH key or password.

Status badges mean:

| Badge | Meaning |
| --- | --- |
| `— unknown` | Background checks are disabled or this endpoint has not been checked. |
| `… checking` | A background check is running. |
| `● online` | A TCP connection to the configured endpoint succeeded. |
| `○ offline` | The TCP connection failed or timed out. |
| `◌ expired` | The cached result is older than five minutes; a new check can run. |
| `☁ via Bastion` | This host uses Azure Bastion; its private endpoint is not probed directly. |
| `↗ via jump` | This host uses ProxyJump; its destination is not probed directly. |

TCP checks are **on by default** and can be disabled in Display settings. They queue without waiting during table rendering, with at most four checks at a time and a one-second connection timeout. Results are cached for five minutes and appear the next time the table renders. A stale result changes to Checking while its replacement is being probed. A successful port check does not verify SSH credentials or promise that a login will succeed.

Last-connected values use the connection history: `Never`, `Just now`, minutes, hours, `Yesterday`, or days. Disabling or clearing connection history affects these indicators. Future timestamps caused by clock changes display `Just now`.

**Host resolution** shows locally resolved IPv4 and IPv6 addresses beside the configured Host. Multiple addresses appear on separate lines; literal IP addresses appear immediately. Hostnames resolve in the background, with at most four lookups at a time and a two-second timeout. The column shows a concise DNS error when resolution fails. Results, including errors, are cached for five minutes and refresh automatically while a table menu is open. The previous result remains visible during a refresh. DNS completions update the complete table view without changing the selected menu entry.

The column is on by default. Hiding it in Display settings stops scheduling DNS lookups. DNS resolution is independent of the background TCP setting: resolving a name does not mean its SSH port is reachable. Names for jump-host and Bastion destinations are also resolved locally; this does not identify the address selected by SSH or the address a remote jump host would resolve.

Host pickers include favorite, authentication, and Docker badges, place favorites first, and show the supported keyboard controls: arrow keys, typing to search, Enter to confirm, and a selectable Back entry. Search uses Spectre.Console's search control and highlights the selected result. Edit/Delete/Favorite action shortcuts belong to the host action menu after selection; they are not advertised as direct picker shortcuts.

Breadcrumbs identify the current menu path and escape user-provided names as literal text. The footer shows the application version and the actual configuration and log locations. Its log location is informational; it does not advertise an unavailable log hotkey.

The dark palette uses bright accents and cyan selection on black. Older configurations containing `Light` load normally and use the dark palette. Escaped host metadata remains literal text.

Dynamic menu entries, including recent connections, receive distinct available shortcuts. Explicit action keys such as Back remain reserved; arrow-key navigation handles rows after the shortcut alphabet is exhausted.

**Ctrl+P** opens the searchable command palette. It includes current menu actions and fresh host, group and tag targets. Type multiple words to filter, use arrows to select, Enter to run, and Esc to return to the original selection. F1 shows the actual shortcuts assigned to the current menu.

On Windows, table layout follows the visible console window rather than the width of its scroll buffer. Startup uses UTF-8 so badge characters retain their intended display width. Table views redraw their breadcrumb, tables, footer and menu when the window is maximized, restored or resized, including while waiting for a key. The selected menu entry is preserved. A one-column margin avoids wrapping at the Windows console's right edge.

The server overview measures all displayed groups together and gives corresponding columns identical widths, including Uncategorized. Long hostnames, tags and container names wrap within these shared columns. Maximizing or restoring the window recalculates the layout for the complete overview, so vertical separators remain aligned between groups. Compact mode retains its reduced padding and hides container rows while preserving shared column positions.
