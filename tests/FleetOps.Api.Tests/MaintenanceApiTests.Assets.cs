using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Api.Tests;

public sealed partial class MaintenanceApiTests
{
    [Fact]
    public async Task AssetCrudRoundTripReturnsLocationVersionsAndDurableAudit()
    {
        using var created = await Send(HttpMethod.Post, "/api/assets", new { name = " Hydraulic Pump ", serialNumber = "HP-001" });
        Assert.Equal(201, (int)created.StatusCode);
        var original = await Json(created);
        var id = original.GetProperty("id").GetGuid();
        var path = $"/api/assets/{id}";
        Assert.Equal(path, new Uri(client.BaseAddress!, created.Headers.Location!).AbsolutePath);
        Assert.Equal("Hydraulic Pump", original.GetProperty("name").GetString());
        Assert.Equal(0, original.GetProperty("status").GetInt32());
        Assert.Equal(8, Convert.FromBase64String(original.GetProperty("rowVersion").GetString()!).Length);
        Assert.Equal('"' + original.GetProperty("rowVersion").GetString() + '"', created.Headers.ETag!.ToString());
        using var list = await client.GetAsync("/api/assets");
        var page = await Json(list);
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(id, page.GetProperty("items")[0].GetProperty("id").GetGuid());
        using var read = await client.GetAsync(path);
        Assert.Equal(200, (int)read.StatusCode);
        Assert.Equal("HP-001", (await Json(read)).GetProperty("serialNumber").GetString());
        using var put = await Send(HttpMethod.Put, path, new { name = "Updated Pump", serialNumber = "HP-002" }, created.Headers.ETag.ToString());
        Assert.Equal(204, (int)put.StatusCode);
        Assert.Equal("", await put.Content.ReadAsStringAsync());
        using var updated = await client.GetAsync(path);
        var body = await Json(updated);
        Assert.Equal("Updated Pump", body.GetProperty("name").GetString());
        Assert.Equal("HP-002", body.GetProperty("serialNumber").GetString());
        Assert.Equal(original.GetProperty("createdAt").GetDateTimeOffset(), body.GetProperty("createdAt").GetDateTimeOffset());
        Assert.NotEqual(created.Headers.ETag, updated.Headers.ETag);
        using var deleted = await Send(HttpMethod.Delete, path, tag: updated.Headers.ETag!.ToString());
        Assert.Equal(204, (int)deleted.StatusCode);
        using var missing = await client.GetAsync(path);
        await Problem(missing, 404);
        using var me = await client.GetAsync("/api/auth/me");
        var actor = (await Json(me)).GetProperty("id").GetString();
        await using var db = Db();
        Assert.False(await db.Assets.AnyAsync(a => a.Id == id));
        var audit = await db.AuditLogs.Where(a => a.EntityId == id).ToListAsync();
        Assert.Equal(3, audit.Count);
        Assert.Equal(new[] { "Created", "Deleted", "Updated" }, audit.Select(a => a.Action).OrderBy(a => a).ToArray());
        Assert.All(audit, a => Assert.Equal(actor, a.ActorId));
    }

    public static IEnumerable<object?[]> InvalidAssetBodies()
    {
        yield return [null, "P-1", "name"];
        yield return [" ", "P-1", "name"];
        yield return [new string('n', 201), "P-1", "name"];
        yield return ["Pump", null, "serialNumber"];
        yield return ["Pump", " ", "serialNumber"];
        yield return ["Pump", new string('s', 101), "serialNumber"];
    }

    [Theory]
    [MemberData(nameof(InvalidAssetBodies))]
    public async Task InvalidAssetCreateReturnsFieldErrorsWithoutPersistence(string? name, string? serialNumber, string field)
    {
        using var response = await Send(HttpMethod.Post, "/api/assets", new { name, serialNumber });
        await Problem(response, 400);
        var errors = (await Json(response)).GetProperty("errors");
        Assert.Contains(errors.EnumerateObject(), error => error.Name.Equals(field, StringComparison.OrdinalIgnoreCase));
        await using var db = Db();
        Assert.Empty(await db.Assets.ToListAsync());
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType == "Asset").ToListAsync());
    }

    [Fact]
    public async Task DuplicateAssetCreateReturnsConflictWithoutExtraDataOrAudit()
    {
        var asset = await Asset("HP-001");
        using var duplicate = await Send(HttpMethod.Post, "/api/assets", new { name = "Duplicate", serialNumber = "hp-001" });
        await Problem(duplicate, 409);
        Assert.DoesNotContain("IX_Assets", await duplicate.Content.ReadAsStringAsync());
        await using var db = Db();
        Assert.Single(await db.Assets.ToListAsync());
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityId == asset.Id));
    }

    [Fact]
    public async Task AssetStaleAndInvalidEditsCannotOverwriteOrDeleteCurrentData()
    {
        var asset = await Asset();
        var path = $"/api/assets/{asset.Id}";
        using var update = await Send(HttpMethod.Put, path, new { name = "Current", serialNumber = "P-2" }, asset.Tag);
        Assert.Equal(204, (int)update.StatusCode);
        using var stalePut = await Send(HttpMethod.Put, path, new { name = "Stale", serialNumber = "P-3" }, asset.Tag);
        await Problem(stalePut, 412);
        using var staleDelete = await Send(HttpMethod.Delete, path, tag: asset.Tag);
        await Problem(staleDelete, 412);
        var current = await Tag(path);
        using var invalid = await Send(HttpMethod.Put, path, new { name = "Invalid", serialNumber = "" }, current);
        await Problem(invalid, 400);
        await using var db = Db();
        var stored = await db.Assets.SingleAsync();
        Assert.Equal("Current", stored.Name);
        Assert.Equal("P-2", stored.SerialNumber);
        Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.EntityId == asset.Id));
    }

    [Theory]
    [InlineData(null, 428)]
    [InlineData("not-quoted", 400)]
    [InlineData("W/\"AAAAAAAAAAA=\"", 400)]
    [InlineData("\"bad-base64\"", 400)]
    [InlineData("\"AAAAAAAAAAA=\", \"AAAAAAAAAAA=\"", 400)]
    public async Task AssetEditsRequireOneValidStrongPrecondition(string? tag, int status)
    {
        var asset = await Asset();
        using var response = await Send(HttpMethod.Put, $"/api/assets/{asset.Id}", new { name = "Changed", serialNumber = "P-1" }, tag);
        await Problem(response, status);
        await using var db = Db();
        Assert.Equal("Pump", (await db.Assets.SingleAsync()).Name);
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityId == asset.Id));
    }

    [Fact]
    public async Task NoncanonicalETagIsRejectedRatherThanUsedAsAValidVersion()
    {
        var asset = await Asset();
        var bytes = asset.Tag.Trim('"');
        var noncanonical = '"' + bytes.Insert(4, " ") + '"';
        using var response = await Send(HttpMethod.Put, $"/api/assets/{asset.Id}",
            new { name = "Should not change", serialNumber = "P-1" }, noncanonical);
        await Problem(response, 400);
        await using var db = Db();
        Assert.Equal("Pump", (await db.Assets.SingleAsync()).Name);
    }

    [Fact]
    public async Task MissingAssetAndRestrictedDeletionReturnCorrectErrors()
    {
        var asset = await Asset();
        using var missing = await client.GetAsync($"/api/assets/{Guid.NewGuid()}");
        await Problem(missing, 404);
        using var missingEdit = await Send(HttpMethod.Put, $"/api/assets/{Guid.NewGuid()}",
            new { name = "Missing", serialNumber = "M-1" }, asset.Tag);
        await Problem(missingEdit, 404);
        await Order(asset.Id);
        using var restricted = await Send(HttpMethod.Delete, $"/api/assets/{asset.Id}", tag: asset.Tag);
        await Problem(restricted, 409);
        await using var db = Db();
        Assert.Single(await db.Assets.ToListAsync());
        Assert.Single(await db.WorkOrders.ToListAsync());
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityId == asset.Id));
    }

    [Fact]
    public async Task ServerOwnedAssetFieldsCannotBeAssignedByClients()
    {
        var suppliedId = Guid.NewGuid();
        using var created = await Send(HttpMethod.Post, "/api/assets",
            new { id = suppliedId, name = "Pump", serialNumber = "P-1", status = 2, createdAt = DateTimeOffset.MinValue });
        Assert.Equal(201, (int)created.StatusCode);
        var body = await Json(created);
        Assert.NotEqual(suppliedId, body.GetProperty("id").GetGuid());
        Assert.Equal(0, body.GetProperty("status").GetInt32());
        Assert.NotEqual(DateTimeOffset.MinValue, body.GetProperty("createdAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task EmptyMalformedAndUnsupportedAssetBodiesReturnProblemDetails()
    {
        using var emptyRequest = new HttpRequestMessage(HttpMethod.Post, "/api/assets")
        { Content = new StringContent("", Encoding.UTF8, "application/json") };
        using var empty = await client.SendAsync(emptyRequest);
        await Problem(empty, 400);
        using var malformedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/assets")
        { Content = new StringContent("{broken", Encoding.UTF8, "application/json") };
        using var malformed = await client.SendAsync(malformedRequest);
        await Problem(malformed, 400);
        using var unsupportedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/assets")
        { Content = new StringContent("{\"name\":\"Pump\",\"serialNumber\":\"P-1\"}", Encoding.UTF8, "text/plain") };
        using var unsupported = await client.SendAsync(unsupportedRequest);
        await Problem(unsupported, 415);
        await using var db = Db();
        Assert.Empty(await db.Assets.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentCompletionsCommitExactlyOneRecordAndOneCompletionAudit()
    {
        var asset = await Asset();
        var technician = await Technician();
        var order = await Order(asset.Id);
        var path = $"/api/work-orders/{order.Id}";
        using var assigned = await Send(HttpMethod.Post, path + "/assign", new { technicianId = technician.Id }, order.Tag);
        Assert.Equal(200, (int)assigned.StatusCode);
        using var started = await Send(HttpMethod.Post, path + "/start", tag: assigned.Headers.ETag!.ToString());
        Assert.Equal(200, (int)started.StatusCode);
        writeGate.WorkOrderId = order.Id;
        writeGate.Status = FleetOps.Domain.Enums.WorkOrderStatus.Completed;
        var tag = started.Headers.ETag!.ToString();
        var responses = await Task.WhenAll(
            Send(HttpMethod.Post, path + "/complete", new { notes = "Left repair" }, tag),
            Send(HttpMethod.Post, path + "/complete", new { notes = "Right repair" }, tag));
        try
        {
            var winner = Assert.Single(responses, r => (int)r.StatusCode == 200);
            var loser = Assert.Single(responses, r => (int)r.StatusCode != 200);
            Assert.Contains((int)loser.StatusCode, new[] { 409, 412 });
            await Problem(loser, (int)loser.StatusCode);
            Assert.Equal(2, writeGate.Arrivals);
            Assert.All(writeGate.OriginalVersions, version => Assert.Equal(tag.Trim('"'), version));
            var winnerNotes = (await Json(winner)).GetProperty("maintenanceRecord").GetProperty("notes").GetString();
            await using var db = Db();
            Assert.Equal(3, (int)(await db.WorkOrders.SingleAsync()).Status);
            Assert.Equal(winnerNotes, (await db.MaintenanceRecords.SingleAsync()).Notes);
            Assert.Equal(4, await db.AuditLogs.CountAsync(a => a.EntityId == order.Id));
            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityType == "MaintenanceRecord"));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
}
