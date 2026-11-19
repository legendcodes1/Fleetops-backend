using FleetOps.Domain.Enums;
namespace FleetOps.Application.Models;

public sealed record AssetDto(Guid Id, string Name, string SerialNumber, AssetStatus Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, byte[] RowVersion);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
public sealed record AssetQuery(int Page = 1, int PageSize = 20, string? Search = null,
    AssetStatus? Status = null, string SortBy = "name", bool Descending = false)
{
    public void Validate()
    {
        if (Page < 1 || Page > 1_000_000) throw new ArgumentException("Page must be between 1 and 1000000.");
        if (PageSize < 1 || PageSize > 100) throw new ArgumentException("PageSize must be between 1 and 100.");
        if (Search?.Length > 200) throw new ArgumentException("Search cannot exceed 200 characters.");
        if (Status.HasValue && !Enum.IsDefined(Status.Value)) throw new ArgumentException("Invalid asset status.");
        if (SortBy is not ("name" or "serialNumber" or "createdAt" or "status"))
            throw new ArgumentException("SortBy must be name, serialNumber, createdAt, or status.");
    }
}
