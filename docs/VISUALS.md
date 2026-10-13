# Host indicators and display settings

Open **Display settings** under **Settings** to choose Dark/Light colors, compact tables, the status and last-connected columns, navigation breadcrumbs, and the version/configuration footer.

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

Host pickers include favorite, authentication, and Docker badges, place favorites first, and show the supported keyboard controls: arrow keys, typing to search, Enter to confirm, and a selectable Back entry. Search uses Spectre.Console's search control and highlights the selected result. Edit/Delete/Favorite action shortcuts belong to the host action menu after selection; they are not advertised as direct picker shortcuts.

Breadcrumbs identify the current menu path and escape user-provided names as literal text. The footer shows the application version and the actual configuration and log locations. Its log location is informational; it does not advertise an unavailable log hotkey.

Light and Dark apply immediately to the terminal background, foreground, table borders, semantic badges, menu text and selection. Light uses dark text and blue selection on white; Dark uses bright accents and cyan selection on black. Theme adaptation preserves escaped host metadata as literal text.

Dynamic menu entries, including recent connections, receive distinct available shortcuts. Explicit action keys such as Back remain reserved; arrow-key navigation handles rows after the shortcut alphabet is exhausted.
