# Application JavaScript

| Folder       | Responsibility                                                |
| ------------ | ------------------------------------------------------------- |
| `shared`     | DOM and request helpers, report previews, workflow lifecycle  |
| `monitoring` | Main screen, filters, machine histories, shared refresh clock |
| `summaries`  | Alert, need-action, MCD, and action summary grids             |
| `records`    | Action cards, raw data, and OCAP records                      |
| `reports`    | PM/TE maintenance records and evidence generation             |

`Pages/Monitoring.cshtml` loads `shared/Common.js`, Monitoring, and
`shared/Workflows.js` before the workflow registrations. Keep these URLs in sync
when moving or adding scripts; retain `Url.VersionedAsset` for cache invalidation.

Use four spaces, single-quoted strings, semicolons, UTF-8, and LF endings.
Each workflow registers an initializer with `Workflows.register` and uses strict
mode. Keep record-specific state inside that initializer.

- Use `context.byId(id)` for cached element lookups scoped to the current partial.
- Use `context.getJson(url, options)` and `context.postJson(url, data, options)`
  for JSON requests. They check HTTP errors, include the AJAX header, and respect
  dialog cancellation. POST requests also include the partial's antiforgery token.
- Use `context.fetch` for HTML or binary responses. Check the response status and
  `context.signal.aborted` after reading the body and before applying the result.
- Use `context.setTimeout`, `context.setInterval`, their matching clear methods,
  and `context.onRefresh` so closing a dialog releases its resources. Handle
  request failures locally; suppress error messages after cancellation.
- Use `Karte.createReportView` for generation status, previews, downloads, and
  object URL cleanup. Pass binary generation responses to `view.showResponse`.
  Generation and local download remain separate user actions; do not add server
  report storage or history.
  Read report bytes with `arrayBuffer()` before constructing the Blob: direct
  `response.blob()` can fail after HTTP 200 when Edge/Chrome temporary disk space
  is low. This path holds the report in browser memory.
- Prefer `textContent` for values. Use `Karte.escapeHtml` for dynamic values in
  HTML templates, and `URLSearchParams` for query strings.
- Build large tables and lists off the DOM, then insert them together. Delegate
  events from stable containers when their contents are repeatedly replaced.

Legacy form mappings, request sequence guards, first-match duplicate handling,
snapshot expiration, and the shared refresh clock are intentional behavior.

Build with `dotnet build MachineKarteWebCore.csproj --no-restore`. With a local
app and a Chromium browser exposing its debugging port, run
`powershell -ExecutionPolicy Bypass -File scripts/Test-Workflows.ps1` with the
matching `-BaseUrl` and `-DevToolsPort`. Its fixtures replace AJAX data while
testing the real Razor partials and assets; no live database is required.
