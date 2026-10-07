# Validation evidence

Validated in the Linux cloud workspace on October 7, 2026.

| Check | Outcome |
|---|---|
| Locked dependency restore and EF tool restore | Passed |
| Debug solution build | Passed, zero warnings/errors |
| Shared status calculation | 15 cases passed, including null dates, manual overrides, today, day 30, day 31, and leap day |
| Status + functional API runner | 43 checks passed |
| Browser interaction runner | 18 checks passed, no runtime/resource errors |
| Mobile layout at 390 px | No document overflow; navigation opens and closes; table scroll stays inside its panel |
| EF Core SQL Server idempotent migration script generation | Passed |
| Release publish with compiled Blazor assets | Passed |
| Published client smoke check | Loads, signs in, creates and searches an instrument |
| Reusable cloud install/start commands | Executed successfully |

API checks cover JWT enforcement, bad sign-in, dashboard counts, creation, duplicates, invalid stored statuses, null due dates, repair completion, multiple open repair orders, preserving Out of Service during completion, certificate validation and byte-preserving download, CSV and Excel imports, duplicate/invalid imports, atomic invalid-batch rejection, export, and webhook key enforcement.

The webhook was exercised with a disposable local test key. The final development server runs the default startup instructions, with fresh sample records and no configured webhook key.

## External services not validated

- **SQL Server persistence:** EF provider, migration, and SQL generation are included. A real database was not available. Pulling the Microsoft SQL Server image failed with HTTP 403 at `centralus.data.mcr.microsoft.com`; that domain addition is saved in the environment draft and must be applied before retrying. Saving a draft does not change runtime egress.
- **Windows LocalDB:** requires a Windows development host.
- **Google trigger:** the API accepted an authenticated HTTP submission; a real Apps Script trigger requires the user's Google Form, public HTTPS endpoint, and matching secure configuration.
- **UNC backup:** implemented as administrator-only COPY_ONLY/CHECKSUM backup followed by RESTORE VERIFYONLY/CHECKSUM. It requires a SQL Server instance and a share writable by the SQL Server service account; no Windows share was attached here.
- **Production infrastructure:** database credentials, private JWT key, administrator password hash, TLS hosting, and durable certificate storage must be configured for the deployment.

## Screenshots

- [Desktop dashboard](dashboard-desktop.png)
- [Mobile dashboard](dashboard-mobile.png)

The running demo supports development and UI evaluation. These results do not establish a published environment, a new-task snapshot restore, or a production deployment.
