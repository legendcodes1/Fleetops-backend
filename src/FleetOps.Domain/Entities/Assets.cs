using FleetOps.Domain.Enums;
using FleetOps.Domain.Exceptions;
using FleetOps.Domain.Validation;

namespace FleetOps.Domain.Entities;

public class Asset
{
    public const int NameMaxLength = 200;
    public const int SerialNumberMaxLength = 100;
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public string SerialNumber { get; private set; }
    public AssetStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public Asset(string name, string serialNumber)
    {
        Name = Guard.Text(name, NameMaxLength, nameof(name));
        SerialNumber = Guard.Text(serialNumber, SerialNumberMaxLength, nameof(serialNumber));
        Id = Guid.NewGuid();
        Status = AssetStatus.Operational;
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SendToMaintenance()
    {
        if (Status != AssetStatus.Operational)
            throw new DomainRuleException("Asset must be operational to send to maintenance.");
        Status = AssetStatus.Maintenance;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ReturnToService()
    {
        if (Status != AssetStatus.Maintenance)
            throw new DomainRuleException("Asset must be in maintenance to return to service.");
        Status = AssetStatus.Operational;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Retire()
    {
        if (Status == AssetStatus.Retired)
            throw new DomainRuleException("Asset is already retired.");
        Status = AssetStatus.Retired;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateDetails(string name, string serialNumber)
    {
        if (Status == AssetStatus.Retired)
            throw new DomainRuleException("Cannot update a retired asset.");
        var validName = Guard.Text(name, NameMaxLength, nameof(name));
        var validSerial = Guard.Text(serialNumber, SerialNumberMaxLength, nameof(serialNumber));
        Name = validName;
        SerialNumber = validSerial;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
