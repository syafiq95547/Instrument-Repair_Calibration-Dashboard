# InstrumentHub

A responsive Instrument Repair and Calibration Management System inspired by the supplied equipment-overview reference. Dark slate navigation, restrained teal accents, five live stat cards, a circular compliance indicator, and clear red/amber calibration alerts.

## Included workflows

- **Dashboard:** live totals, overdue / due-soon filters, compliance ring, recent calibration tasks, repair summary, and availability breakdown.
- **Inventory:** add and edit instruments, search by asset/name/model/department/owner, department and status filters, due-date sorting, pagination, CSV export, and CSV/XLSX import.
- **Calibration Schedule:** calculated calibration health alongside the separate task result (Calibrated, Out of Spec, In Progress).
- **Repair Logs:** open work orders, assign technicians, and complete repairs. Open repairs apply the In Repair override; completing the final order restores Active unless the instrument has since been manually taken Out of Service.
- **Certificates:** upload PDF files up to 10 MB, associate them with an asset, and download authenticated documents. Certificate uploads do not silently update calibration dates or task results.
- **Integrations:** protected Google Apps Script webhook and an administrator-only SQL Server backup endpoint writing to a configured UNC share.
- Accessible labels, keyboard dialog handling, mobile navigation, horizontally scrollable tables, loading, empty, success, and error states. Assets and counts come from the API; no fabricated trend percentages.

Screenshots: [desktop](docs/dashboard-desktop.png) · [mobile](docs/dashboard-mobile.png). See [validation evidence](docs/VALIDATION.md) for tested capabilities and external-service limitations.

## Technology versions

| Component | Version |
|---|---|
| .NET / ASP.NET Core | 8.0 (SDK 8.0.425, latest-patch roll-forward) |
| EF Core + SQL Server provider + design tooling | 8.0.10 |
| JWT Bearer | 8.0.10 |
| Swashbuckle / Swagger | 6.8.1 |
| CsvHelper | 33.0.1 |
| ClosedXML | 0.104.2 |
| Blazor WebAssembly + Authorization | 8.0.10 |

The API serves the compiled Blazor client on the same origin. A JWT delegating message handler attaches the access token to API calls. The browser uses sessionStorage, so sign-in lasts for the current tab session; tokens expire after eight hours. All management endpoints require authentication. Production requires a configured administrator password hash and signing key; default demo credentials are limited to Development.

## Run the demo

Install the .NET 8 SDK. From the repository root:

```sh
dotnet restore InstrumentHub.sln --locked-mode
dotnet build InstrumentHub.sln --no-restore
dotnet run --project src/InstrumentHub.Api --no-build
```

Open the application at `http://localhost:5080`. Swagger is available at `/swagger` in Development only. The Development launch profile enables **in-memory demo data** and signs into the demo automatically. Use Sign out to see the login screen. Demo credentials: `admin@instrumenthub.local` / `DemoCalibration!2026`.

The demo has 12 instruments with dates relative to startup day, so overdue and due-soon states remain representative. Metadata changes reset on server restart. Uploaded demo files remain in ignored `App_Data/certificates`; demo metadata is temporary. Demo mode is explicitly rejected outside Development.

For this cloud workspace, run `bash scripts/cloud-install.sh` followed by `bash scripts/cloud-start.sh`. These use writable caches outside the checkout and do not require a Git worktree. The script starts a development process; publish output is separate.

## Windows development with SQL Server LocalDB

LocalDB is a Windows development database and cannot run on Linux. The default persistent connection is:

```text
Server=(localdb)\mssqllocaldb;Database=InstrumentHub;Trusted_Connection=True;MultipleActiveResultSets=true
```

In PowerShell, from the repository root:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DemoMode = 'false'
dotnet run --project src/InstrumentHub.Api --no-launch-profile -- --initialize-database
dotnet run --project src/InstrumentHub.Api --no-launch-profile --urls http://localhost:5080
```

Initialization applies the checked-in EF Core migration and exits. Persistent databases start empty; sample data is never automatically inserted into SQL Server. Add assets through Inventory or import a file.

For a full SQL Server instance, override `ConnectionStrings__Instruments`. Use a valid server certificate in production (`Encrypt=True;TrustServerCertificate=False`) and least-privilege application credentials. Supply database secrets through your deployment’s secret manager or process configuration, not client files or source control.

## Production configuration

Set the following server configuration values through secure deployment settings:

| Setting | Purpose |
|---|---|
| `ASPNETCORE_ENVIRONMENT=Production` | Disables demo mode, development credentials, and Swagger; enables HTTPS redirection/HSTS |
| `DemoMode=false` | Enables EF Core SQL Server persistence |
| `ConnectionStrings__Instruments` | Full SQL Server connection string |
| `Jwt__Key` | Private random signing secret of at least 32 bytes; never use the Development key |
| `Jwt__Issuer`, `Jwt__Audience` | Optional overrides; API/client token validation must agree |
| `Admin__Email` | Administrator sign-in address |
| `Admin__Name` | Display name |
| `Admin__PasswordHash` | PBKDF2-SHA256 hash generated by `python scripts/hash-password.py` |
| `Storage__Certificates` | Optional durable directory; defaults to `App_Data/certificates` relative to API content root |
| `Webhook__Key` | Private shared key required only for Google Forms integration |
| `Backup__UncPath` | Optional backup directory, e.g. `\\fileserver\calibration-backups` |

Generate the password hash interactively; the password is not placed in shell history. Use a separately generated high-entropy webhook key. The webhook is disabled with HTTP 503 until its key is configured. These are server-side secrets, not proxy placeholders.

Apply database migrations as a controlled deployment step using `--initialize-database`, then publish:

```sh
dotnet publish src/InstrumentHub.Api -c Release -o artifacts/publish --no-restore
```

Serve the published application with HTTPS using IIS/ASP.NET Core Hosting Bundle or a configured Kestrel deployment. If using a reverse proxy, configure trusted forwarded headers for that infrastructure. Persist the certificate directory across deployments and back it up independently of SQL Server. Only uploaded PDF files are accepted, names are sanitized, storage names are generated, and file access requires JWT authentication. The sample accepts PDFs by header signature; it does not perform malware scanning or certify the document’s contents.

Authentication currently supports a single configured administrator. Multi-user accounts, roles, refresh tokens, and organizational identity-provider sign-in are separate enhancements. A certificate is an attachment, not proof of a successful calibration: staff must record the due date and task outcome explicitly.

## Status calculation

UTC calendar dates are used consistently by the API. Stored manual state and calculated health are separate:

1. Stored **In Repair** or **Out of Service** takes priority, including when the due date is missing or past.
2. No next due date → **Pending Calibration**.
3. Due date before today → **Overdue**.
4. Due date from today through 30 days ahead, inclusive → **Due Soon**.
5. More than 30 days ahead → **Calibrated**.

Stored states accepted by the editor are Active, In Repair, and Out of Service. Task results are independently Calibrated, Out of Spec, and In Progress. The shared implementation is `src/InstrumentHub.Shared/Models.cs`.

**Compliance** = (Calibrated + Due Soon) / (all assets excluding Out of Service). In Repair, Overdue, and Pending Calibration do not count as compliant. **Availability** uses the same available numerator divided by all assets. A zero denominator gives 0%, not a misleading 100%.

## CSV / Excel format

Import adds new instruments; duplicate IDs reject the request. Limits: 5 MB and 500 records. All rows are validated before saving, and SQL Server saves the batch in a transaction. Column names:

```csv
AssetId,Name,Department,Model,Location,Owner,CurrentStatus,NextDueDate
EQ-00249,Analytical balance,Quality control,Mettler Toledo,Lab A,Sarah Kim,Active,2027-04-01
```

AssetId, Name, and Department are required. Optional columns can be omitted; CurrentStatus defaults to Active. Dates use `yyyy-MM-dd`; native Excel date cells are accepted too. The first worksheet contains the header row. Export includes the entire inventory, irrespective of the displayed filter; cells beginning with spreadsheet formula characters are escaped.

## Google Forms → dashboard

1. Link the Google Form to a response spreadsheet with question titles **Asset ID**, **Instrument Name**, **Department**, **Next Due Date**, and **Owner**.
2. Copy `integrations/google-forms.gs` into that spreadsheet’s Apps Script project.
3. Set Script Properties `DASHBOARD_URL` (your public HTTPS origin) and `WEBHOOK_KEY` (matching server `Webhook__Key`). Never put keys in the script source.
4. Create an installable spreadsheet **On form submit** trigger for `onFormSubmit` and authorize it in Google.
5. Submit a unique asset and confirm it appears in Inventory. A duplicate returns 409; invalid fields return 400. The integration creates an instrument, not a calibration certificate.

The API accepts:

```http
POST /api/webhook/form-submit
Content-Type: application/json
X-Webhook-Key: <configured shared key>
```

```json
{"assetId":"EQ-00249","instrumentName":"Analytical balance","department":"Quality control","nextDueDate":"2027-04-01","owner":"Sarah Kim"}
```

## Windows UNC database backup

Configure `Backup__UncPath` to a directory such as `\\fileserver\calibration-backups`. The **SQL Server service account**, rather than the web server identity, must be able to write to that share. SQL Server creates a unique COPY_ONLY backup with CHECKSUM and performs RESTORE VERIFYONLY with CHECKSUM. Settings → Create database backup invokes `POST /api/backups` with an administrator JWT. Backups never overwrite an existing file. Demo mode refuses database backups.

UNC backup validation requires a real reachable Windows share and the SQL Server service identity. This is independent of the application’s certificate-file backup; schedule both in your operations tooling.

## Validation

```sh
# Boundary cases, leap dates, and manual overrides (no API needed)
dotnet run --project tests/InstrumentHub.Checks

# Functional API checks against a running Development demo server
dotnet run --project tests/InstrumentHub.Checks -- http://localhost:5080
```

The functional runner creates TEST/CSV/XLSX-prefixed records and a PDF, checks authentication, counts, statuses, CRUD, repairs, validation, atomic import behavior, CSV/Excel processing, exports, and certificate byte integrity. Run it against a disposable demo instance. For the webhook tests, configure `Webhook__Key` on that server and matching `TEST_WEBHOOK_KEY` on the runner. No real Google account, SQL Server, or network share is exercised by demo checks.

Browser interaction checks are in `tests/browser-checks.cjs`. With Node, Playwright, and Chromium installed, run:

```sh
node tests/browser-checks.cjs
```

They test search, health filters, navigation, creation/editing, uploads/downloads, repair completion, and mobile layout. Browser tooling is optional; it is not part of the application runtime.

EF tools are pinned in `.config/dotnet-tools.json`:

```sh
dotnet tool restore
dotnet ef migrations script --project src/InstrumentHub.Api --idempotent
```

Database migrations are included. SQL Server persistence, Windows LocalDB, a real Google trigger, and UNC backups need their respective services to validate end to end.
