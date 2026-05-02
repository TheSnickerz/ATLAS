using System.ComponentModel.DataAnnotations;

namespace ATLAS.Models;

// ── Status & Category Enums ──────────────────────────────────────────────────

public enum AssetStatus   { Active, Inactive, Decommissioned, Unknown }
public enum AssetCategory
{
    Computer,
    [Display(Name = "Network Device")]    NetworkDevice,
    Printer,
    [Display(Name = "Software Application")] SoftwareApplication,
    [Display(Name = "Mobile Device")]     MobileDevice,
    [Display(Name = "Cloud Resource")]    CloudResource
}

// ── Type-specific Sub-Enums ───────────────────────────────────────────────────

public enum ComputerType      { Server, Workstation, Laptop }
public enum NetworkDeviceType { Router, Switch, Firewall, WirelessAccessPoint, LoadBalancer, Other }
public enum PrinterType       { Laser, Inkjet, Thermal, DotMatrix }
public enum ApplicationType   { WebApp, Desktop, Mobile, API, Database, Middleware, Other }
public enum MobileDeviceType  { Phone, Tablet }
public enum CloudProvider     { AWS, Azure, GCP, Oracle, Other }

// ── Abstract Base Class ───────────────────────────────────────────────────────

public abstract class Asset
{
    public int Id { get; set; }

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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdated { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public ICollection<AssetVulnerability> AssetVulnerabilities { get; set; } = new List<AssetVulnerability>();

    /// <summary>Returns the high-level asset category.</summary>
    public abstract AssetCategory Category { get; }

    /// <summary>Returns a human-readable subtype label (e.g. "Server", "Router").</summary>
    public abstract string SubTypeLabel { get; }

    /// <summary>Returns the primary network/location identifier shown in list views.</summary>
    public virtual string? Identifier => null;
}

// ── Computer ──────────────────────────────────────────────────────────────────

public class Computer : Asset
{
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

    public override AssetCategory Category     => AssetCategory.Computer;
    public override string        SubTypeLabel => ComputerType.ToString();
    public override string?       Identifier   => IpAddress;
}

// ── Network Device ────────────────────────────────────────────────────────────

public class NetworkDevice : Asset
{
    [Display(Name = "Device Type")]
    public NetworkDeviceType NetworkDeviceType { get; set; }

    [MaxLength(45), Display(Name = "IP Address")]
    public string? IpAddress { get; set; }

    [MaxLength(20), Display(Name = "MAC Address")]
    public string? MacAddress { get; set; }

    [MaxLength(50), Display(Name = "Firmware Version")]
    public string? Firmware { get; set; }

    [Display(Name = "Port Count")]
    public int? PortCount { get; set; }

    [Display(Name = "Managed Device")]
    public bool Managed { get; set; }

    public override AssetCategory Category     => AssetCategory.NetworkDevice;
    public override string        SubTypeLabel => NetworkDeviceType.ToString();
    public override string?       Identifier   => IpAddress;
}

// ── Printer ───────────────────────────────────────────────────────────────────

public class Printer : Asset
{
    [Display(Name = "Printer Type")]
    public PrinterType PrinterType { get; set; }

    [MaxLength(45), Display(Name = "IP Address")]
    public string? IpAddress { get; set; }

    [MaxLength(100), Display(Name = "Model")]
    public string? Model { get; set; }

    [Display(Name = "Color Capable")]
    public bool ColorCapable { get; set; }

    [Display(Name = "Network Connected")]
    public bool NetworkConnected { get; set; }

    public override AssetCategory Category     => AssetCategory.Printer;
    public override string        SubTypeLabel => PrinterType.ToString();
    public override string?       Identifier   => IpAddress;
}

// ── Software Application ──────────────────────────────────────────────────────

public class SoftwareApplication : Asset
{
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

    public override AssetCategory Category     => AssetCategory.SoftwareApplication;
    public override string        SubTypeLabel => ApplicationType.ToString();
    public override string?       Identifier   => Url;
}

// ── Mobile Device ─────────────────────────────────────────────────────────────

public class MobileDevice : Asset
{
    [Display(Name = "Device Type")]
    public MobileDeviceType MobileDeviceType { get; set; }

    [MaxLength(100), Display(Name = "Operating System")]
    public string? OperatingSystem { get; set; }

    [MaxLength(20), Display(Name = "IMEI")]
    public string? Imei { get; set; }

    [MaxLength(20), Display(Name = "Phone Number")]
    public string? PhoneNumber { get; set; }

    [MaxLength(50), Display(Name = "Carrier")]
    public string? Carrier { get; set; }

    public override AssetCategory Category     => AssetCategory.MobileDevice;
    public override string        SubTypeLabel => MobileDeviceType.ToString();
    public override string?       Identifier   => PhoneNumber ?? Imei;
}

// ── Cloud Resource ────────────────────────────────────────────────────────────

public class CloudResource : Asset
{
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

    public override AssetCategory Category     => AssetCategory.CloudResource;
    public override string        SubTypeLabel => CloudProvider.ToString();
    public override string?       Identifier   => ResourceId;
}
