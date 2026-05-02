using System.ComponentModel.DataAnnotations;

namespace ATLAS.Models;

/// <summary>
/// Flat view-model used by the Create and Edit forms for all asset types.
/// The controller dispatches to the correct concrete Asset subclass based on <see cref="Category"/>.
/// </summary>
public class AssetFormViewModel
{
    public int  Id     { get; set; }   // 0 on Create, >0 on Edit
    public bool IsEdit => Id > 0;

    // ── Asset Category (determines which section is shown) ───────────────────

    [Required, Display(Name = "Asset Category")]
    public AssetCategory Category { get; set; } = AssetCategory.Computer;

    // ── Common Fields (every asset type) ────────────────────────────────────

    [Required, MaxLength(200), Display(Name = "Name / Hostname")]
    public string Name { get; set; } = "";

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(100)]
    public string? Owner { get; set; }

    [MaxLength(100), Display(Name = "Business Stakeholder")]
    public string? BusinessStakeholder { get; set; }

    [MaxLength(100)]
    public string? Department { get; set; }

    [MaxLength(200)]
    public string? Location { get; set; }

    [MaxLength(50), Display(Name = "VLAN")]
    public string? Vlan { get; set; }

    public AssetStatus Status { get; set; } = AssetStatus.Active;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    // ── Computer Fields ──────────────────────────────────────────────────────

    [Display(Name = "Computer Type")]
    public ComputerType ComputerType { get; set; }

    [MaxLength(45), Display(Name = "IP Address")]
    public string? IpAddress { get; set; }

    [MaxLength(100), Display(Name = "Operating System")]
    public string? OperatingSystem { get; set; }

    [Display(Name = "RAM (GB)")]
    public int? RamGb { get; set; }

    [MaxLength(100), Display(Name = "CPU Model")]
    public string? CpuModel { get; set; }

    [Display(Name = "Domain Joined")]
    public bool DomainJoined { get; set; }

    [MaxLength(100), Display(Name = "Domain")]
    public string? DomainName { get; set; }

    [MaxLength(100), Display(Name = "Serial Number")]
    public string? SerialNumber { get; set; }

    // ── Network Device Fields (IpAddress shared above) ───────────────────────

    [Display(Name = "Device Type")]
    public NetworkDeviceType NetworkDeviceType { get; set; }

    [MaxLength(20), Display(Name = "MAC Address")]
    public string? MacAddress { get; set; }

    [MaxLength(50), Display(Name = "Firmware Version")]
    public string? Firmware { get; set; }

    [Display(Name = "Port Count")]
    public int? PortCount { get; set; }

    [Display(Name = "Managed Device")]
    public bool Managed { get; set; }

    // ── Printer Fields (IpAddress shared above) ──────────────────────────────

    [Display(Name = "Printer Type")]
    public PrinterType PrinterType { get; set; }

    [MaxLength(100), Display(Name = "Printer Model")]
    public string? PrinterModel { get; set; }

    [Display(Name = "Color Capable")]
    public bool ColorCapable { get; set; }

    [Display(Name = "Network Connected")]
    public bool NetworkConnected { get; set; }

    // ── Software Application Fields ──────────────────────────────────────────

    [Display(Name = "Application Type")]
    public ApplicationType ApplicationType { get; set; }

    [MaxLength(100), Display(Name = "Vendor")]
    public string? Vendor { get; set; }

    [MaxLength(50), Display(Name = "Version")]
    public string? Version { get; set; }

    [MaxLength(500), Display(Name = "URL")]
    public string? Url { get; set; }

    [MaxLength(100), Display(Name = "License Type")]
    public string? LicenseType { get; set; }

    // ── Mobile Device Fields (OperatingSystem shared above) ─────────────────

    [Display(Name = "Device Type")]
    public MobileDeviceType MobileDeviceType { get; set; }

    [MaxLength(20), Display(Name = "IMEI")]
    public string? Imei { get; set; }

    [MaxLength(20), Display(Name = "Phone Number")]
    public string? PhoneNumber { get; set; }

    [MaxLength(50), Display(Name = "Carrier")]
    public string? Carrier { get; set; }

    // ── Cloud Resource Fields ────────────────────────────────────────────────

    [Display(Name = "Cloud Provider")]
    public CloudProvider CloudProvider { get; set; }

    [MaxLength(100), Display(Name = "Resource Type")]
    public string? ResourceType { get; set; }

    [MaxLength(100), Display(Name = "Region")]
    public string? Region { get; set; }

    [MaxLength(100), Display(Name = "Account ID")]
    public string? AccountId { get; set; }

    [MaxLength(200), Display(Name = "Resource ID / ARN")]
    public string? ResourceId { get; set; }
}
