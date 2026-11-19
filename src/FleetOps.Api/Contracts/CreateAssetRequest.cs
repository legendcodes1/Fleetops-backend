using System.ComponentModel.DataAnnotations;

namespace FleetOps.Api.Contracts;

//Contract = What the client is allowed to send or receive.
public class CreateAssetRequest
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = "";

    [Required]
    [StringLength(100)]
    public string SerialNumber { get; set; } = "";
}
