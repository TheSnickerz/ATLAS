using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ATLAS.Data;
using ATLAS.Models;

namespace ATLAS.Controllers;

public class AssetsController : Controller
{
    private readonly AtlasContext _db;

    public AssetsController(AtlasContext db) => _db = db;

    // ── Index ────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Index(
        string? search = null, string? vlan = null,
        int page = 1, int pageSize = 25)
    {
        if (pageSize != 25 && pageSize != 50 && pageSize != 100) pageSize = 25;
        page = Math.Max(1, page);

        var availableVlans = await _db.Assets
            .Where(a => a.Vlan != null && a.Vlan != "")
            .Select(a => a.Vlan!)
            .Distinct()
            .OrderBy(v => v)
            .ToListAsync();

        var query = _db.Assets.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim();
            query = query.Where(a =>
                a.Name.Contains(t) ||
                (a.Owner      != null && a.Owner.Contains(t)) ||
                (a.Department != null && a.Department.Contains(t)) ||
                (a.Location   != null && a.Location.Contains(t)));
        }

        if (!string.IsNullOrWhiteSpace(vlan))
            query = query.Where(a => a.Vlan == vlan);

        var totalFiltered = await query.CountAsync();

        var assets = await query
            .Include(a => a.AssetVulnerabilities)
            .OrderBy(a => a.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return View(new AssetIndexViewModel
        {
            Assets             = assets,
            Page               = page,
            PageSize           = pageSize,
            TotalFilteredCount = totalFiltered,
            Search             = search,
            VlanFilter         = vlan,
            AvailableVlans     = availableVlans,
        });
    }

    // ── Details ──────────────────────────────────────────────────────────────

    public async Task<IActionResult> Details(int id)
    {
        var asset = await _db.Assets
            .Include(a => a.AssetVulnerabilities)
                .ThenInclude(av => av.Vulnerability)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (asset == null) return NotFound();
        return View(asset);
    }

    // ── Create ───────────────────────────────────────────────────────────────

    public IActionResult Create(AssetCategory? category = null)
    {
        var vm = new AssetFormViewModel
        {
            Category = category ?? AssetCategory.Computer
        };
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AssetFormViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var asset = BuildAssetFromViewModel(vm);
        asset.CreatedAt = DateTime.UtcNow;
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // ── Edit ─────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Edit(int id)
    {
        var asset = await _db.Assets.FindAsync(id);
        if (asset == null) return NotFound();
        return View(MapToViewModel(asset));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, AssetFormViewModel vm)
    {
        if (id != vm.Id) return BadRequest();

        var asset = await _db.Assets.FindAsync(id);
        if (asset == null) return NotFound();

        // Guard against changing asset type in Edit (delete + re-create instead)
        if (asset.Category != vm.Category)
        {
            ModelState.AddModelError("Category",
                "Asset type cannot be changed after creation. Delete this asset and add a new one to change its type.");
            return View(vm);
        }

        if (!ModelState.IsValid) return View(vm);

        ApplyCommonFields(asset, vm);
        ApplyTypeSpecificFields(asset, vm);
        asset.LastUpdated = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    public async Task<IActionResult> Delete(int id)
    {
        var asset = await _db.Assets.FindAsync(id);
        if (asset == null) return NotFound();
        return View(asset);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var asset = await _db.Assets.FindAsync(id);
        if (asset != null)
        {
            _db.Assets.Remove(asset);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    // ── PowerShell Query Apply ────────────────────────────────────────────────

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyPsQuery(int id, [FromForm] string resultsJson)
    {
        var asset = await _db.Assets.FindAsync(id);
        if (asset == null) return NotFound();
        if (asset is not Computer computer)
        {
            TempData["Error"] = "PowerShell query is only supported for Computer assets.";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            using var doc = JsonDocument.Parse(resultsJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("IpAddress", out var ip) && ip.ValueKind == JsonValueKind.String)
                computer.IpAddress = ip.GetString();
            if (root.TryGetProperty("OperatingSystem", out var os) && os.ValueKind == JsonValueKind.String)
                computer.OperatingSystem = os.GetString();
            if (root.TryGetProperty("RamGb", out var ram) && ram.ValueKind == JsonValueKind.Number)
                computer.RamGb = ram.GetInt32();
            if (root.TryGetProperty("CpuModel", out var cpu) && cpu.ValueKind == JsonValueKind.String)
                computer.CpuModel = cpu.GetString()?.Trim();
            if (root.TryGetProperty("SerialNumber", out var sn) && sn.ValueKind == JsonValueKind.String)
                computer.SerialNumber = sn.GetString()?.Trim();
            if (root.TryGetProperty("DomainJoined", out var dj))
                computer.DomainJoined = dj.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("DomainName", out var dn) && dn.ValueKind == JsonValueKind.String)
                computer.DomainName = dn.GetString()?.Trim();
            // Update name from Hostname only when the asset name is still the default placeholder
            if (root.TryGetProperty("Hostname", out var hn) && hn.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(hn.GetString()))
            {
                var hostname = hn.GetString()!;
                if (computer.Name.Equals("New Computer", StringComparison.OrdinalIgnoreCase))
                    computer.Name = hostname;
            }

            computer.LastUpdated = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Asset updated from PowerShell query results.";
        }
        catch (Exception)
        {
            TempData["Error"] = "Could not parse PowerShell results — make sure you pasted the full JSON output.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    // ── Private Helpers ──────────────────────────────────────────────────────

    private static Asset BuildAssetFromViewModel(AssetFormViewModel vm)
    {
        Asset asset = vm.Category switch
        {
            AssetCategory.Computer => new Computer
            {
                ComputerType   = vm.ComputerType,
                IpAddress      = vm.IpAddress,
                OperatingSystem = vm.OperatingSystem,
                RamGb          = vm.RamGb,
                CpuModel       = vm.CpuModel,
                DomainJoined   = vm.DomainJoined,
                DomainName     = vm.DomainName,
                SerialNumber   = vm.SerialNumber,
            },
            AssetCategory.NetworkDevice => new NetworkDevice
            {
                NetworkDeviceType = vm.NetworkDeviceType,
                IpAddress         = vm.IpAddress,
                MacAddress        = vm.MacAddress,
                Firmware          = vm.Firmware,
                PortCount         = vm.PortCount,
                Managed           = vm.Managed,
            },
            AssetCategory.Printer => new Printer
            {
                PrinterType      = vm.PrinterType,
                IpAddress        = vm.IpAddress,
                Model            = vm.PrinterModel,
                ColorCapable     = vm.ColorCapable,
                NetworkConnected = vm.NetworkConnected,
            },
            AssetCategory.SoftwareApplication => new SoftwareApplication
            {
                ApplicationType = vm.ApplicationType,
                Vendor          = vm.Vendor,
                Version         = vm.Version,
                Url             = vm.Url,
                LicenseType     = vm.LicenseType,
            },
            AssetCategory.MobileDevice => new MobileDevice
            {
                MobileDeviceType = vm.MobileDeviceType,
                OperatingSystem  = vm.OperatingSystem,
                Imei             = vm.Imei,
                PhoneNumber      = vm.PhoneNumber,
                Carrier          = vm.Carrier,
            },
            AssetCategory.CloudResource => new CloudResource
            {
                CloudProvider = vm.CloudProvider,
                ResourceType  = vm.ResourceType,
                Region        = vm.Region,
                AccountId     = vm.AccountId,
                ResourceId    = vm.ResourceId,
            },
            _ => throw new InvalidOperationException($"Unknown asset category: {vm.Category}")
        };

        ApplyCommonFields(asset, vm);
        return asset;
    }

    private static void ApplyCommonFields(Asset asset, AssetFormViewModel vm)
    {
        asset.Name                = vm.Name;
        asset.Description         = vm.Description;
        asset.Owner               = vm.Owner;
        asset.BusinessStakeholder = vm.BusinessStakeholder;
        asset.Department          = vm.Department;
        asset.Location            = vm.Location;
        asset.Vlan                = vm.Vlan;
        asset.Status              = vm.Status;
        asset.Notes               = vm.Notes;
    }

    private static void ApplyTypeSpecificFields(Asset asset, AssetFormViewModel vm)
    {
        switch (asset)
        {
            case Computer c:
                c.ComputerType    = vm.ComputerType;
                c.IpAddress       = vm.IpAddress;
                c.OperatingSystem = vm.OperatingSystem;
                c.RamGb           = vm.RamGb;
                c.CpuModel        = vm.CpuModel;
                c.DomainJoined    = vm.DomainJoined;
                c.DomainName      = vm.DomainName;
                c.SerialNumber    = vm.SerialNumber;
                break;
            case NetworkDevice nd:
                nd.NetworkDeviceType = vm.NetworkDeviceType;
                nd.IpAddress         = vm.IpAddress;
                nd.MacAddress        = vm.MacAddress;
                nd.Firmware          = vm.Firmware;
                nd.PortCount         = vm.PortCount;
                nd.Managed           = vm.Managed;
                break;
            case Printer p:
                p.PrinterType      = vm.PrinterType;
                p.IpAddress        = vm.IpAddress;
                p.Model            = vm.PrinterModel;
                p.ColorCapable     = vm.ColorCapable;
                p.NetworkConnected = vm.NetworkConnected;
                break;
            case SoftwareApplication sa:
                sa.ApplicationType = vm.ApplicationType;
                sa.Vendor          = vm.Vendor;
                sa.Version         = vm.Version;
                sa.Url             = vm.Url;
                sa.LicenseType     = vm.LicenseType;
                break;
            case MobileDevice md:
                md.MobileDeviceType = vm.MobileDeviceType;
                md.OperatingSystem  = vm.OperatingSystem;
                md.Imei             = vm.Imei;
                md.PhoneNumber      = vm.PhoneNumber;
                md.Carrier          = vm.Carrier;
                break;
            case CloudResource cr:
                cr.CloudProvider = vm.CloudProvider;
                cr.ResourceType  = vm.ResourceType;
                cr.Region        = vm.Region;
                cr.AccountId     = vm.AccountId;
                cr.ResourceId    = vm.ResourceId;
                break;
        }
    }

    private static AssetFormViewModel MapToViewModel(Asset asset)
    {
        var vm = new AssetFormViewModel
        {
            Id                  = asset.Id,
            Category            = asset.Category,
            Name                = asset.Name,
            Description         = asset.Description,
            Owner               = asset.Owner,
            BusinessStakeholder = asset.BusinessStakeholder,
            Department          = asset.Department,
            Location            = asset.Location,
            Vlan                = asset.Vlan,
            Status              = asset.Status,
            Notes               = asset.Notes,
            CreatedAt           = asset.CreatedAt,
        };

        switch (asset)
        {
            case Computer c:
                vm.ComputerType    = c.ComputerType;
                vm.IpAddress       = c.IpAddress;
                vm.OperatingSystem = c.OperatingSystem;
                vm.RamGb           = c.RamGb;
                vm.CpuModel        = c.CpuModel;
                vm.DomainJoined    = c.DomainJoined;
                vm.DomainName      = c.DomainName;
                vm.SerialNumber    = c.SerialNumber;
                break;
            case NetworkDevice nd:
                vm.NetworkDeviceType = nd.NetworkDeviceType;
                vm.IpAddress         = nd.IpAddress;
                vm.MacAddress        = nd.MacAddress;
                vm.Firmware          = nd.Firmware;
                vm.PortCount         = nd.PortCount;
                vm.Managed           = nd.Managed;
                break;
            case Printer p:
                vm.PrinterType       = p.PrinterType;
                vm.IpAddress         = p.IpAddress;
                vm.PrinterModel      = p.Model;
                vm.ColorCapable      = p.ColorCapable;
                vm.NetworkConnected  = p.NetworkConnected;
                break;
            case SoftwareApplication sa:
                vm.ApplicationType = sa.ApplicationType;
                vm.Vendor          = sa.Vendor;
                vm.Version         = sa.Version;
                vm.Url             = sa.Url;
                vm.LicenseType     = sa.LicenseType;
                break;
            case MobileDevice md:
                vm.MobileDeviceType = md.MobileDeviceType;
                vm.OperatingSystem  = md.OperatingSystem;
                vm.Imei             = md.Imei;
                vm.PhoneNumber      = md.PhoneNumber;
                vm.Carrier          = md.Carrier;
                break;
            case CloudResource cr:
                vm.CloudProvider = cr.CloudProvider;
                vm.ResourceType  = cr.ResourceType;
                vm.Region        = cr.Region;
                vm.AccountId     = cr.AccountId;
                vm.ResourceId    = cr.ResourceId;
                break;
        }

        return vm;
    }
}
