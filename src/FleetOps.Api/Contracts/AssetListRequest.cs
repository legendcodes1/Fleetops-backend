using System.ComponentModel.DataAnnotations;
using FleetOps.Application.Models;
using FleetOps.Domain.Enums;
namespace FleetOps.Api.Contracts;

public sealed class AssetListRequest
{
    [Range(1, 1_000_000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
    [StringLength(200)] public string? Search { get; set; }
    [EnumDataType(typeof(AssetStatus))] public AssetStatus? Status { get; set; }
    public string SortBy { get; set; } = "name";
    public bool Descending { get; set; }

    public AssetQuery ToQuery() => new(Page, PageSize, Search, Status, SortBy, Descending);
}
