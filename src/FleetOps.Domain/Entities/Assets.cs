using FleetOps.Domain.Enums;
using FleetOps.Domain.Exceptions;

namespace FleetOps.Domain.Entities;

public class Asset
{
    public Guid Id { get; }
    public string Name { get; private set; }
    public string SerialNumber { get; private set; }

    public AssetStatus Status { get; private set; }

    public Asset(string name, string serialNumber)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Asset name cannot be empty.",
                nameof(name)
            );
        }

        if (string.IsNullOrWhiteSpace(serialNumber))
            throw new ArgumentException("Asset serial number cannot be empty.", nameof(serialNumber));

        Id = Guid.NewGuid();
        Name = name;
        SerialNumber = serialNumber;
        Status = AssetStatus.Operational;
    }

    public void SendToMaintenance()
    {
        if (Status != AssetStatus.Operational)
        {
             throw new DomainRuleException("Asset must be operational to send to maintenance.");
        }

        Status = AssetStatus.Maintenance;
    }

    public void ReturnToService()
    {
        if (Status != AssetStatus.Maintenance)
        {
            throw new DomainRuleException("Asset must be in maintenance to return to service.");
        }
        Status = AssetStatus.Operational;
    }

    public void Retire()
    {
        if (Status != AssetStatus.Retired)
        {
            Status = AssetStatus.Retired;
        }
        else
        {
            throw new DomainRuleException("Asset is already retired.");
        }
    }

    public void UpdateDetails(string name, string serialNumber)
    {
        if (Status == AssetStatus.Retired)
        {
            throw new DomainRuleException(
                "Cannot update a retired asset."
            );
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Asset name cannot be empty.",
                nameof(name)
            );
        }

        // Validate serialNumber
        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            throw new ArgumentException(
                "Asset serial number cannot be empty.",
                nameof(serialNumber)
            );
        }

        Name = name;
        SerialNumber = serialNumber;
    }

}
