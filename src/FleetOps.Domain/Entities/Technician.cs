using System.Net.Mail;
using FleetOps.Domain.Enums;
using FleetOps.Domain.Validation;

namespace FleetOps.Domain.Entities;

public class Technician
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string Specialization { get; private set; } = "";
    public TechnicianAvailability Availability { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private Technician() { }

    public Technician(string name, string email, string specialization)
    {
        UpdateDetails(name, email, specialization, TechnicianAvailability.Available);
    }

    public void UpdateDetails(string name, string email, string specialization, TechnicianAvailability availability)
    {
        var validName = Guard.Text(name, 200, nameof(name));
        var validEmail = Guard.Text(email, 254, nameof(email)).ToLowerInvariant();
        var validSpecialization = Guard.Text(specialization, 200, nameof(specialization));
        Guard.Defined(availability, nameof(availability));
        if (!MailAddress.TryCreate(validEmail, out var parsed) || parsed.Address != validEmail)
            throw new ArgumentException("Email must be a valid email address.", nameof(email));
        Name = validName;
        Email = validEmail;
        Specialization = validSpecialization;
        Availability = availability;
    }
}
