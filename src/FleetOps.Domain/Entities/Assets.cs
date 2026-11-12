using FleetOps.Domain.Enums;

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

        // Your turn: validate serialNumber

        Id = Guid.NewGuid();
        Name = name;
        SerialNumber = serialNumber;
        Status = AssetStatus.Operational;
    }

    public void SendToMaintenance()
    {
        if (Status != AssetStatus.Operational)
        {
             throw new InvalidOperationException("Asset must be operational to send to maintenance.");
        }

        Status = AssetStatus.Maintenance;
    }

    public void ReturnToService()
    {
        // TODO: Validate and change status
        if (Status != AssetStatus.Maintenance)
        {
            throw new InvalidOperationException("Asset must be in maintenance to return to service.");
        }
        Status = AssetStatus.Operational;
    }

    public void Retire()
    {
        // TODO: Validate and change status
        if (Status != AssetStatus.Retired)
        {
            Status = AssetStatus.Retired;
        }
        else
        {
            throw new InvalidOperationException("Asset must be operational to retire.");
        }
    }

    public void UpdateDetails(string name, string serialNumber)
    {
        if (Status == AssetStatus.Retired)
        {
            throw new InvalidOperationException(
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