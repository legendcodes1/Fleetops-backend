using FleetOps.Domain.Entities;
using FleetOps.Domain.Enums;

namespace FleetOps.Domain.Tests;

public sealed class TechnicianTests
{
    public static IEnumerable<object?[]> InvalidDetails()
    {
        yield return [null, "alex@example.test", "Hydraulics"];
        yield return [" ", "alex@example.test", "Hydraulics"];
        yield return [new string('x', 201), "alex@example.test", "Hydraulics"];
        yield return ["Alex", null, "Hydraulics"];
        yield return ["Alex", "", "Hydraulics"];
        yield return ["Alex", "invalid", "Hydraulics"];
        yield return ["Alex", "Alex <alex@example.test>", "Hydraulics"];
        yield return ["Alex", new string('x', 255), "Hydraulics"];
        yield return ["Alex", "alex@example.test", null];
        yield return ["Alex", "alex@example.test", " "];
        yield return ["Alex", "alex@example.test", new string('x', 201)];
    }

    [Theory]
    [MemberData(nameof(InvalidDetails))]
    public void ConstructorRejectsInvalidDetails(string? name, string? email, string? specialization) =>
        Assert.Throws<ArgumentException>(() => new Technician(name!, email!, specialization!));

    [Fact]
    public void ValidDetailsAreNormalizedAndDocumentedLengthLimitsAreAccepted()
    {
        var technician = new Technician("  " + new string('n', 200) + "  ", " ALEX@EXAMPLE.TEST ", new string('s', 200));
        Assert.Equal(200, technician.Name.Length);
        Assert.Equal("alex@example.test", technician.Email);
        Assert.Equal(200, technician.Specialization.Length);
        Assert.Equal(TechnicianAvailability.Available, technician.Availability);
        Assert.NotEqual(Guid.Empty, technician.Id);
    }

    [Theory]
    [InlineData("Changed", "invalid", "Electrical", 0)]
    [InlineData("Changed", "new@example.test", "", 0)]
    [InlineData("Changed", "new@example.test", "Electrical", 99)]
    public void FailedUpdateLeavesEveryFieldUnchanged(string name, string email, string specialization, int availability)
    {
        var technician = new Technician("Alex", "alex@example.test", "Hydraulics");
        var id = technician.Id;
        Assert.Throws<ArgumentException>(() => technician.UpdateDetails(name, email, specialization, (TechnicianAvailability)availability));
        Assert.Equal(id, technician.Id);
        Assert.Equal("Alex", technician.Name);
        Assert.Equal("alex@example.test", technician.Email);
        Assert.Equal("Hydraulics", technician.Specialization);
        Assert.Equal(TechnicianAvailability.Available, technician.Availability);
    }
}
