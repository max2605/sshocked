# Local diagnostic reports

Open **Local diagnostics → Create diagnostic report** and choose a host or
**Application only**. The preview is a structured JSON report intended for manual
review and sharing. Enter a destination after reviewing it to save the report.
Nothing is uploaded or sent automatically.

```text
ssk diagnostics preview
ssk diagnostics preview --server web
ssk diagnostics preview --server web --probe
ssk diagnostics export --server web --output diagnostic-web.json
```

Reports include application/platform/runtime versions, safe host and route
metadata, cached DNS/reachability state, required tool availability, and recent
failure event types and exit codes. Direct TCP reachability does not prove SSH
authentication; ProxyJump/Bastion status does not test the complete route.
`--probe` explicitly performs a TCP port probe using the existing reachability
service. Report generation does not connect with SSH, run hooks, resolve secret
values, or request a password.

OpenSSH and Azure CLI version checks use fixed argument lists, a three-second
timeout, and bounded output capture. Terminal checks report availability only;
no terminal is opened. Missing prerequisites and timed-out version checks appear
in the report. Azure Bastion additionally requires Azure CLI. Key-file checks only
test whether a configured file exists, without reading its contents.

## Privacy controls

By default the report excludes paths, command text, and free-form failure details.
It preserves safe failure event types, exit codes, and exception type names. The
report never includes credential ciphertext, private-key content, process
environment dumps, session recordings, or raw configuration objects.

Explicit inclusion remains constrained by audit privacy settings:

```text
ssk diagnostics preview --server web --include-paths
ssk diagnostics preview --server web --include-command-text
ssk diagnostics export --server web --include-paths --include-command-text --output diagnostic-web.json
```

Paths require `Audit.LogFilePaths`; command text and free-form details require
`Audit.LogCommandText`. If a setting disallows requested data, the report explains
that it was excluded. Included text is still sanitized for recognized tokens and
password literals, known transient host credentials, and inline private-key
material. Commands included without paths have recognizable local/remote paths
masked. Arbitrary secrets cannot be detected reliably; review a report before
sharing it.

Values are serialized as literal JSON strings. Labels containing brackets, quotes,
or markup are never interpreted as terminal markup or shell commands. Hostnames,
aliases, usernames, and route metadata remain potentially identifying even when
credentials are excluded.

Exports are local UTF-8 JSON files. An existing destination is not overwritten;
choose another filename. Writes use a temporary file and atomic rename. Unix
files are readable/writable only by the owner, and output is limited to 16 MiB.
