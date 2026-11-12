using System.ComponentModel.DataAnnotations;

namespace FleetOps.Api.Contracts;

public class UpdateAssetRequest
{
    [Required]
    public string Name { get; set; } = "";

    [Required]
    public string SerialNumber { get; set; } = "";
}