# User feedback implementation

The feedback issues #42–#56 refine the existing SSH workflow. Changes are split by domain, with regression tests for input, navigation, credentials and command execution.

| Issues | Behavior |
| --- | --- |
| #42, #67 | Dark uses a black background with bright accents and cyan selection. The Light palette and theme picker have been removed; legacy Light settings load with Dark. |
| #43, #51, #53 | Browse, Favorites and Recent connections open the same server action menu, including commands, macros, transfers, edit and delete. Back restores the caller's navigation frame. |
| #44 | Local and remote path prompts accept balanced Windows Copy as path quotes. Arrow keys, Home/End, Delete/Backspace and Windows console mouse clicks position the caret; Esc cancels. |
| #45 | Settings → Activity and recording combines the activity timeline, event policy, recordings, playback, export and deletion. The CLI has the same common `activity` entry point. |
| #46 | A managed persistent SSH command shell supports explicit macro recording and replay on servers, groups or current tag matches. |
| #47 | TCP probes run by default, with Online, Offline, Checking, Unknown and stale states. A reachable TCP port does not prove SSH authentication. Bastion/jump-host routes are labeled separately. |
| #48 | Dynamic menu rows receive distinct available shortcuts while explicit action keys remain reserved. Lists beyond the available shortcut alphabet remain navigable with arrow keys. |
| #49, #50, #52 | Group management uses the standard menus and breadcrumbs. Enter on an empty group name cancels immediately. Confirmed deletion detaches servers and saved containers while retaining them and their history. |
| #54 | Interactive and CLI group connections launch parallel terminal tabs, including configurations with the old Sequential value. The sequential setting is removed from the UI; the serialized value remains readable for migration. |
| #55 | Tags complement static groups. Case-insensitive tag filters match all selected tags and refresh membership after actions; commands, connections, macros and file transfers use the matching hosts. |
| #56 | Windows can save, update and clear per-server passwords protected by user-scoped DPAPI. Settings can clear all saved local passwords. SSH consumes them through the existing askpass helper. |

## Commands and macros

Choose **Run command / record macro** in any server context menu, or use:

```sh
ssk macros record --server web
```

This opens one persistent POSIX shell. Commands preserve `cd`, exported variables and shell functions. Output arrives while the command runs. Use `:record` to start capturing explicit command lines, `:stop` to stop, and `:save deployment` to save the draft. `:discard` clears the draft and `:exit` ends the command session. Empty lines do not end the session. Ctrl+C cancels the running command and closes the session.

```sh
ssk macros list
ssk macros run deployment --server web
ssk macros run deployment --group production
ssk macros run deployment --tag backend
ssk macros delete deployment --force
```

Replay uses one POSIX shell per target, preserves command order and stops that target on failure. Other targets still run and report independent results. Hosts without a directly usable SSH connection need Bastion tunnel key/password authentication; native AAD command sessions are unavailable.

Macro capture belongs to this explicit command session. Ordinary interactive **Connect** does not capture shell keystrokes. Commands run with stdin closed so a remote program cannot consume the managed shell protocol. Use Connect for full terminal applications and interactive password prompts. SSH authentication is outside the command recording loop. Macro command text is stored in the local configuration; recognized password/token literals are rejected, but arbitrary secret values cannot be detected reliably. Use remote credential references instead of embedding secrets in commands.

## Activity and recording

Audit events and output recordings are complementary: events preserve structured outcomes and privacy-filtered metadata; recordings preserve timed output for playback. A common service, menu and CLI expose both, while separate retention policies keep large recordings from displacing audit events. Existing data and playback requirements are preserved; old CLI aliases continue working.

```sh
ssk activity status
ssk activity events --last 20 --server web --output json
ssk activity audit settings --command-text false
ssk activity recordings list --server web
ssk activity recordings replay SESSION_ID
ssk activity recordings export SESSION_ID --format text --output session.txt
```

Output recording remains opt-in. Managed command sessions respect the same global/per-server recording policy as connections. Cast recordings contain output, not macro controls or credential prompts. Recorded remote output can contain secrets regardless of command-text privacy settings.

## Local passwords

Edit a password-authenticated server and select **Save / update password** or **Clear saved password**. Ciphertext is kept in the configuration directory's `credentials` subdirectory with Windows current-user ACLs, separate from `appconfig.json`. DPAPI binds decryption to the Windows user; additional endpoint binding prevents an edited hostname, username, port, jump route or Bastion target from silently receiving a previously saved password. Re-save or clear the password after changing its endpoint. Deleting a server removes its local credential.

Configured secret references take precedence, then an ephemeral password, then the locally saved password. Passwords do not enter SSH arguments or configuration JSON. The existing askpass helper transfers a resolved password in the child process environment; applications running as the same OS user can access that process, so this is protection at rest, not isolation from a compromised user account. Launch the `ssk` executable/apphost rather than `dotnet ssk.dll` for askpass authentication. Windows DPAPI has no insecure fallback on other platforms; use a configured secret provider there. See Microsoft's [DPAPI documentation](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata).

## Verification

Regression tests cover distinct palettes, literal markup, path normalization/caret editing/mouse decoding, shortcut allocation, loopback TCP checks, navigation frame cleanup, shared menus, group cancellation/deletion, tag membership, streamed output, persistent command shell state, macro failure handling and real Windows DPAPI encryption/endpoint binding.

Run `dotnet test Sshocked.slnx --configuration Release` and publish the Windows Native AOT app before release. Linux CI runs the portable suite; symlink tests need Unix or Windows symlink privileges. Live SSH, Azure and SCP deployment checks still require configured remote targets and credentials.
