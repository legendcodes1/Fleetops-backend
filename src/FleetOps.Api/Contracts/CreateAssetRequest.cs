using System.ComponentModel.DataAnnotations;

namespace FleetOps.Api.Contracts;

//Contract = What the client is allowed to send or receive.
public class CreateAssetRequest
{
    [Required]
    public string Name { get; set; } = "";

    [Required]
    public string SerialNumber { get; set; } = "";
}