using System.ComponentModel.DataAnnotations;

namespace FleetOps.Api.Contracts;

public class UpdateAssetRequest
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = "";

    [Required]
    [StringLength(100)]
    public string SerialNumber { get; set; } = "";
}
