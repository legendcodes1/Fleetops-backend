using FleetOps.Domain.Entities;
using FleetOps.Domain.Enums;
namespace FleetOps.Application.Models;

public sealed record AssetDto(Guid Id, string Name, string SerialNumber, AssetStatus Status)
{
    public static AssetDto From(Asset asset) => new(asset.Id, asset.Name, asset.SerialNumber, asset.Status);
}
