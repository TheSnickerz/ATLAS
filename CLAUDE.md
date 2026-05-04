# ATLAS — Session Context for Claude

## What This Project Is
ATLAS is an internal IT asset management and vulnerability tracking system built with ASP.NET Core 9 MVC + EF Core (SQLite). It tracks computers, network devices, printers, mobile devices, software applications, and cloud resources, links them to CVEs from the NVD, and imports live scan findings from Greenbone (OpenVAS).

---

## How to Run

```bash
# From /Users/snickers/ClaudeCode/Projects/ATLAS
GREENBONE_PASSWORD='W64FK9ic2Oi#6$RDJw@P' docker compose up --build -d
```

- App runs at **http://localhost:8080**
- Database is persisted in Docker volume `atlas-data` → `/app/data/atlas.db`
- EF migrations run automatically at startup via `db.Database.Migrate()` in `Program.cs`

**Stopping:**
```bash
docker compose down
```

**Viewing logs:**
```bash
docker logs -f atlas-atlas-1
```

---

## Secrets — Never Hardcode These

| Secret | Where it lives |
|---|---|
| Greenbone password | Docker env var `GREENBONE_PASSWORD` — shell only, never in code |
| NVD API key | Docker env var `NVD_API_KEY` — shell only, never in code |

Both are declared in `docker-compose.yml` as `${VAR:-}` (empty default, override in shell).

---

## Git Branches

| Branch | Purpose |
|---|---|
| `main` | Stable, tested releases |
| `feature/greenbone-integration` | **Current active branch** — Greenbone sync feature |
| `develop` | General development |
| `production` | Production deploys |

**Current branch: `feature/greenbone-integration`**
Do not merge to `main` until Greenbone sync is fully tested.

---

## External Integrations

### NVD (NIST National Vulnerability Database)
- Syncs CVE data into local SQLite on demand
- Service: `Services/NvdSyncService.cs`
- Progress tracker singleton: `Services/SyncProgressTracker.cs`
- Triggered from: Vulnerabilities → "Sync from NVD" button
- API key via env var `NVD_API_KEY`

### Greenbone (OpenVAS)
- Host: `10.40.10.88:9390` (GMP protocol — raw XML over TLS)
- Username: `gvmadmin` (in `appsettings.json`)
- Password: Docker env var `GREENBONE_PASSWORD` = `W64FK9ic2Oi#6$RDJw@P`
- TLS cert errors ignored (`IgnoreCertErrors: true`)
- Web UI: `http://10.40.10.88:9392` (for admin verification)
- GMP low-level client: `Services/GmpClient.cs`
- High-level service: `Services/GreenboneService.cs`
- Progress tracker: `Services/GreenboneSyncTracker.cs`
- Controller: `Controllers/GreenboneController.cs`
- Triggered from: Assets page → "Import from Greenbone" button
- Key GMP filter used: `apply_overrides=0 min_qod=0 owner=any first={n} rows=500`
  - `first`/`rows` must be in the **filter string**, not XML attributes — Greenbone ignores XML attributes

---

## Project Structure

```
/Users/snickers/ClaudeCode/Projects/ATLAS/
├── Controllers/
│   ├── AssetsController.cs          # CRUD + PowerShell query apply
│   ├── AssetVulnerabilitiesController.cs
│   ├── VulnerabilitiesController.cs # NVD sync + paginated CVE library
│   ├── GreenboneController.cs       # Sync, Status, TestConnection, AssetDelta
│   └── HomeController.cs
├── Models/
│   ├── Asset.cs                     # Abstract base + 6 subclasses (TPH)
│   ├── AssetFormViewModel.cs        # Flat VM for Create/Edit forms
│   ├── AssetIndexViewModel.cs       # VM for Assets Index (search/filter/paging)
│   ├── AssetVulnerability.cs        # Join table: asset ↔ vulnerability
│   ├── Vulnerability.cs             # CVE record
│   └── VulnerabilityIndexViewModel.cs
├── Services/
│   ├── GmpClient.cs                 # Low-level TLS TCP GMP client
│   ├── GreenboneService.cs          # GetFindingsAsync (IAsyncEnumerable)
│   ├── GreenboneSyncTracker.cs      # Singleton progress state
│   ├── NvdSyncService.cs            # NVD CVE sync
│   └── SyncProgressTracker.cs      # Singleton progress state for NVD
├── Data/
│   ├── AtlasContext.cs              # EF DbContext
│   └── Migrations/                  # Auto-applied at startup
├── Views/
│   ├── Assets/                      # Index, Create, Edit, Details, Delete
│   ├── Vulnerabilities/             # Index, Details
│   ├── AssetVulnerabilities/        # Create, Delete (assign CVE to asset)
│   └── Shared/_Layout.cshtml        # SMG-branded Bootstrap 5 layout
├── appsettings.json                 # Non-secret config (Greenbone host/port/user)
├── docker-compose.yml
└── Dockerfile
```

---

## Data Model

### Asset (TPH — single `Assets` table with `Discriminator` column)
Base fields: `Id`, `Name`, `Description`, `Owner`, `BusinessStakeholder`, `Department`, `Location`, `Vlan`, `Status`, `Notes`, `CreatedAt`, `LastUpdated`

Subclasses: `Computer`, `NetworkDevice`, `Printer`, `SoftwareApplication`, `MobileDevice`, `CloudResource`

### AssetVulnerability (join table)
`AssetId`, `VulnerabilityId`, `Status` (Open/InProgress/Resolved/Accepted), `DetectedAt`, `Notes`, `Source` ("Manual"/"Greenbone"), `LastSeenAt`

### Vulnerability
`Id`, `CveId`, `Title`, `Description`, `Severity` (enum), `CvssScore`, `DiscoveredAt`

---

## Key Patterns

### Pagination (follow VulnerabilitiesController pattern)
```csharp
var query = _db.Table.AsQueryable();
// apply filters...
var total = await query.CountAsync();
var items = await query.OrderBy(...).Skip((page-1)*pageSize).Take(pageSize).ToListAsync();
```

### Background tasks (fire-and-forget with fresh DI scope)
```csharp
_ = Task.Run(async () => {
    using var scope = scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AtlasContext>();
    // ... work ...
});
```

### EF Migrations
```bash
dotnet ef migrations add MigrationName
# migrations auto-apply at startup — no manual dotnet ef database update needed
```

### Asset type mapping
`AssetsController` has 4 private helpers: `BuildAssetFromViewModel`, `ApplyCommonFields`, `ApplyTypeSpecificFields`, `MapToViewModel`. When adding a new common field: update all four + `Asset.cs` + `AssetFormViewModel.cs` + Create/Edit views + migration.

---

## Pending Work

### 1. Greenbone sync testing
- Sync is implemented but needs full end-to-end test with real scan data
- When confirmed working, merge `feature/greenbone-integration` → `main`

### 2. ComputerType editable on Edit form
- Currently the ComputerType dropdown is disabled on Edit
- User reported it should be editable
- File: `Views/Assets/Edit.cshtml` — find the `ComputerType` select and remove `disabled`

### 3. DomainName field (partially done)
- `Details.cshtml` already shows it conditionally ✓
- `Models/Asset.cs` Computer class has `DomainName` ✓
- `AssetFormViewModel.cs` has it ✓
- Migration `AddComputerDomainName` exists ✓
- Create/Edit forms have it ✓
- PS query applies it ✓
- **Status: complete**

### 4. Vlan field (just added)
- Added to base `Asset` class, `AssetFormViewModel`, Create/Edit views
- Migration `AddAssetVlan` created ✓
- Shows as column on Assets Index ✓
- Assets Index VLAN dropdown filter works ✓

---

## UI / Branding
- Bootstrap 5 with SMG (Staffmark Group) brand colors
- Dark navy `#0A1628` primary, gold `#C8A84B` accent
- Layout: `Views/Shared/_Layout.cshtml`
- Nav items: Assets | Vulnerabilities | Home

---

## Common Commands

```bash
# Build only (no Docker)
cd /Users/snickers/ClaudeCode/Projects/ATLAS && dotnet build

# Add EF migration
cd /Users/snickers/ClaudeCode/Projects/ATLAS && dotnet ef migrations add MigrationName

# Start with Greenbone password
GREENBONE_PASSWORD='W64FK9ic2Oi#6$RDJw@P' docker compose up --build -d

# Start with both API keys
NVD_API_KEY='your-key' GREENBONE_PASSWORD='W64FK9ic2Oi#6$RDJw@P' docker compose up --build -d

# Watch logs
docker logs -f atlas-atlas-1 2>&1 | grep -v "Request\|Response\|Executing"

# Check Greenbone GMP logs
docker logs atlas-atlas-1 2>&1 | grep GMP
```
