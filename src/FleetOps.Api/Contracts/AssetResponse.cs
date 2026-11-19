using FleetOps.Application.Models;
using FleetOps.Domain.Enums;
namespace FleetOps.Api.Contracts;

public sealed record AssetResponse(Guid Id, string Name, string SerialNumber, AssetStatus Status)
{
    public static AssetResponse From(AssetDto asset) => new(asset.Id, asset.Name, asset.SerialNumber, asset.Status);
}
