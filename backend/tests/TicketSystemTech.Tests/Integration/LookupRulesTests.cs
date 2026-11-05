using Microsoft.EntityFrameworkCore;

namespace TicketSystemTech.Tests.Integration;

/// <summary>Rules about branches and sub-branches that nothing checked until the lookup code was moved out of its controller (T-15).</summary>
[Collection(ApiCollection.Name)]
public class LookupRulesTests : IAsyncLifetime
{
    private readonly ApiWorld _world;
    private readonly List<Guid> _branches = new();

    public LookupRulesTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available || _branches.Count == 0) return;
        await _world.Db(async db =>
        {
            await db.SubBranches.Where(s => _branches.Contains(s.DepartmentId)).ExecuteDeleteAsync();
            await db.AuditLogs.Where(a => a.EntityId != null && _branches.Contains(a.EntityId.Value)).ExecuteDeleteAsync();
            return await db.Departments.Where(d => _branches.Contains(d.Id)).ExecuteDeleteAsync();
        });
    }

    private async Task<(Guid Id, string Email)> NewBranchAsync()
    {
        var name = "Lookup test " + Guid.NewGuid().ToString("N")[..6];
        var email = name.Replace(' ', '-').ToLowerInvariant() + "@authz.test";
        var response = await _world.Post(_world.Admin, "/api/departments", new { name, email });
        Assert.Equal(200, response.Code);
        var id = response.Json.GetProperty("id").GetGuid();
        _branches.Add(id);
        return (id, email);
    }

    [SqlFact]
    public async Task Two_branches_cannot_share_an_e_mail_address()
    {
        var (first, email) = await NewBranchAsync();
        var (second, _) = await NewBranchAsync();

        var created = await _world.Post(_world.Admin, "/api/departments", new { name = "Duplikat", email });
        Assert.Equal(409, created.Code);
        Assert.Contains("already exists", created.Text);

        var updated = await _world.Send(_world.Admin, HttpMethod.Put, $"/api/departments/{second}", new { name = "Drugi", email, isActive = true });
        Assert.Equal(409, updated.Code);

        // a branch may keep its own address when it is saved again
        var own = await _world.Send(_world.Admin, HttpMethod.Put, $"/api/departments/{first}", new { name = "Prvi", email, isActive = true });
        Assert.Equal(200, own.Code);
    }

    [SqlFact]
    public async Task A_branch_cannot_have_two_sub_branches_with_the_same_name()
    {
        var (branch, _) = await NewBranchAsync();
        var first = await _world.Post(_world.Admin, $"/api/departments/{branch}/sub-branches", new { name = "Servis" });
        Assert.Equal(200, first.Code);
        var second = await _world.Post(_world.Admin, $"/api/departments/{branch}/sub-branches", new { name = "Prodaja" });
        Assert.Equal(200, second.Code);

        var duplicate = await _world.Post(_world.Admin, $"/api/departments/{branch}/sub-branches", new { name = "Servis" });
        Assert.Equal(409, duplicate.Code);

        var rename = await _world.Send(_world.Admin, HttpMethod.Put, $"/api/sub-branches/{second.Json.GetProperty("id").GetGuid()}", new { name = "Servis", isActive = true });
        Assert.Equal(409, rename.Code);

        Assert.Equal(404, (await _world.Post(_world.Admin, $"/api/departments/{Guid.NewGuid()}/sub-branches", new { name = "Nigdje" })).Code);
    }

    [SqlFact]
    public async Task Only_an_admin_sees_the_branches_that_are_switched_off()
    {
        var (branch, _) = await NewBranchAsync();
        var switchedOff = await _world.Send(_world.Admin, HttpMethod.Put, $"/api/departments/{branch}", new { name = "Ugasena", email = "ugasena-" + branch.ToString("N")[..6] + "@authz.test", isActive = false });
        Assert.Equal(200, switchedOff.Code);

        bool Listed(ApiResponse r) => r.Json.EnumerateArray().Any(d => d.GetProperty("id").GetGuid() == branch);

        Assert.True(Listed(await _world.Get(_world.Admin, "/api/departments?includeInactive=true")));
        Assert.False(Listed(await _world.Get(_world.Admin, "/api/departments")));
        Assert.False(Listed(await _world.Get(_world.EmpA1, "/api/departments?includeInactive=true"))); // asking is not enough
        Assert.False(Listed(await _world.Get(_world.CliX, "/api/departments?includeInactive=true")));
    }
}
