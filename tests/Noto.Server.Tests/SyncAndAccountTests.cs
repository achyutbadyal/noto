using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Noto.Core.Sync;

namespace Noto.Server.Tests;

public sealed class SyncAndAccountTests : IDisposable
{
    readonly ServerFactory _f = new();
    public void Dispose() => _f.Dispose();

    string Stamp(Guid device, int counter = 0) => new Hlc(_f.Time.GetUtcNow().ToUnixTimeMilliseconds(), counter, device).ToString();

    object WorkspaceInsert(Guid device, Guid ws, string name = "Work") => new
    {
        op_id = Guid.NewGuid(), entity_type = "workspace", entity_id = ws, workspace_id = ws, kind = "insert",
        value = new { id = ws, name, time_zone = "UTC", created_at = "2026-10-01T00:00:00+00:00" }, hlc = Stamp(device), device_id = device,
    };

    static Task<HttpResponseMessage> Sync(ServerFactory.Session s, long cursor, Guid[] workspaces, object[] ops, int limit = 500) =>
        s.Client.PostAsJsonAsync("/v1/sync", new { device_id = s.DeviceId, cursor, workspaces, limit, ops });

    [Fact]
    public async Task Push_then_pull_over_http_round_trips_between_two_devices()
    {
        var a = await _f.RegisterAsync("sync@example.com");
        var bDevice = Guid.NewGuid();
        var login = await _f.CreateClient().PostAsJsonAsync("/v1/auth/login", new { email = "sync@example.com", password = "correct horse battery", device = ServerFactory.Device(bDevice) });
        var b = a with { Client = _f.CreateClient(), DeviceId = bDevice };
        b.Client.DefaultRequestHeaders.Authorization = new("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString());
        var ws = Guid.NewGuid();

        var push = await Sync(a, 0, [ws], [WorkspaceInsert(a.DeviceId, ws)]);
        push.StatusCode.ShouldBe(HttpStatusCode.OK);
        var pushed = await push.Content.ReadFromJsonAsync<JsonElement>();
        pushed.GetProperty("accepted_op_ids").GetArrayLength().ShouldBe(1);
        pushed.GetProperty("ops").GetArrayLength().ShouldBe(0); // own ops are not echoed
        pushed.GetProperty("server_hlc").GetString().ShouldNotBeNullOrEmpty();

        var pull = await (await Sync(b, 0, [ws], [])).Content.ReadFromJsonAsync<JsonElement>();
        var op = pull.GetProperty("ops")[0];
        op.GetProperty("kind").GetString().ShouldBe("insert");
        op.GetProperty("seq").GetInt64().ShouldBeGreaterThan(0);
        op.GetProperty("value").GetProperty("name").GetString().ShouldBe("Work");
        pull.GetProperty("has_more").GetBoolean().ShouldBeFalse();
        pull.GetProperty("next_cursor").GetInt64().ShouldBe(op.GetProperty("seq").GetInt64());
    }

    [Fact]
    public async Task Ops_for_unlisted_workspaces_are_not_returned()
    {
        var a = await _f.RegisterAsync("filter@example.com");
        var bDevice = Guid.NewGuid();
        var login = await _f.CreateClient().PostAsJsonAsync("/v1/auth/login", new { email = "filter@example.com", password = "correct horse battery", device = ServerFactory.Device(bDevice) });
        var b = a with { Client = _f.CreateClient(), DeviceId = bDevice };
        b.Client.DefaultRequestHeaders.Authorization = new("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString());
        var listed = Guid.NewGuid();
        var unlisted = Guid.NewGuid();
        await Sync(a, 0, [], [WorkspaceInsert(a.DeviceId, listed), WorkspaceInsert(a.DeviceId, unlisted)]);

        var pull = await (await Sync(b, 0, [listed], [])).Content.ReadFromJsonAsync<JsonElement>();

        pull.GetProperty("ops").EnumerateArray().Select(o => o.GetProperty("workspace_id").GetGuid()).ShouldBe([listed]);
    }

    [Fact]
    public async Task Pull_is_paged_by_limit_with_has_more()
    {
        var a = await _f.RegisterAsync("page@example.com");
        var bDevice = Guid.NewGuid();
        var login = await _f.CreateClient().PostAsJsonAsync("/v1/auth/login", new { email = "page@example.com", password = "correct horse battery", device = ServerFactory.Device(bDevice) });
        var b = a with { Client = _f.CreateClient(), DeviceId = bDevice };
        b.Client.DefaultRequestHeaders.Authorization = new("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString());
        var ws = Guid.NewGuid();
        var ops = Enumerable.Range(0, 5).Select(n => (object)new
        {
            op_id = Guid.NewGuid(), entity_type = "workspace", entity_id = ws, workspace_id = ws, kind = "set", field = "name", value = $"n{n}",
            hlc = Stamp(a.DeviceId, n), device_id = a.DeviceId,
        }).ToArray();
        await Sync(a, 0, [ws], ops);

        var first = await (await Sync(b, 0, [ws], [], limit: 2)).Content.ReadFromJsonAsync<JsonElement>();
        first.GetProperty("ops").GetArrayLength().ShouldBe(2);
        first.GetProperty("has_more").GetBoolean().ShouldBeTrue();

        var second = await (await Sync(b, first.GetProperty("next_cursor").GetInt64(), [ws], [], limit: 10)).Content.ReadFromJsonAsync<JsonElement>();
        second.GetProperty("ops").GetArrayLength().ShouldBe(3);
        second.GetProperty("has_more").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Cursor_ahead_of_the_server_is_409()
    {
        var a = await _f.RegisterAsync();

        var response = await Sync(a, 1_000_000, [], []);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("CURSOR_AHEAD");
    }

    [Fact]
    public async Task More_than_2000_ops_is_413()
    {
        var a = await _f.RegisterAsync();
        var ws = Guid.NewGuid();
        var ops = Enumerable.Range(0, 2001).Select(_ => (object)WorkspaceInsert(a.DeviceId, ws)).ToArray();

        (await Sync(a, 0, [], ops)).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Pushing_under_another_devices_id_is_forbidden()
    {
        var a = await _f.RegisterAsync();
        var response = await a.Client.PostAsJsonAsync("/v1/sync", new { device_id = Guid.NewGuid(), cursor = 0, workspaces = Array.Empty<Guid>(), limit = 10, ops = Array.Empty<object>() });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task One_bad_op_is_rejected_with_a_code_while_the_rest_apply()
    {
        var a = await _f.RegisterAsync();
        var ws = Guid.NewGuid();
        var bad = new { op_id = Guid.NewGuid(), entity_type = "mystery", entity_id = Guid.NewGuid(), workspace_id = ws, kind = "insert", value = new { }, hlc = Stamp(a.DeviceId), device_id = a.DeviceId };

        var json = await (await Sync(a, 0, [], [bad, WorkspaceInsert(a.DeviceId, ws)])).Content.ReadFromJsonAsync<JsonElement>();

        json.GetProperty("accepted_op_ids").GetArrayLength().ShouldBe(1);
        json.GetProperty("rejected")[0].GetProperty("code").GetString().ShouldBe("UNKNOWN_ENTITY_TYPE");
    }

    [Fact]
    public async Task Users_cannot_read_or_delete_each_others_workspaces()
    {
        var a = await _f.RegisterAsync();
        var b = await _f.RegisterAsync();
        var ws = Guid.NewGuid();
        await Sync(a, 0, [], [WorkspaceInsert(a.DeviceId, ws)]);

        var snapshot = await b.Client.GetAsync($"/v1/sync/snapshot?workspace_id={ws}&entity_type=workspace");
        var delete = await b.Client.DeleteAsync($"/v1/sync/workspaces/{ws}");

        snapshot.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        delete.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await a.Client.GetAsync($"/v1/sync/snapshot?workspace_id={ws}&entity_type=workspace")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Snapshot_returns_rows_with_clocks_and_delete_removes_the_server_copy()
    {
        var a = await _f.RegisterAsync();
        var ws = Guid.NewGuid();
        await Sync(a, 0, [], [WorkspaceInsert(a.DeviceId, ws, "Snap")]);

        var snap = await a.Client.GetFromJsonAsync<JsonElement>($"/v1/sync/snapshot?workspace_id={ws}&entity_type=workspace");
        snap.GetProperty("rows")[0].GetProperty("row").GetProperty("name").GetString().ShouldBe("Snap");
        snap.GetProperty("rows")[0].GetProperty("field_clocks").GetProperty("name").GetString().ShouldNotBeNullOrEmpty();

        (await a.Client.DeleteAsync($"/v1/sync/workspaces/{ws}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await a.Client.GetAsync($"/v1/sync/snapshot?workspace_id={ws}&entity_type=workspace")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await a.Client.GetFromJsonAsync<JsonElement>("/v1/sync/workspaces")).GetProperty("workspaces").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Snapshot_rejects_unknown_entity_types()
    {
        var a = await _f.RegisterAsync();
        var ws = Guid.NewGuid();
        await Sync(a, 0, [], [WorkspaceInsert(a.DeviceId, ws)]);

        (await a.Client.GetAsync($"/v1/sync/snapshot?workspace_id={ws}&entity_type=app_connection")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sync_is_rate_limited_per_device()
    {
        using var limited = new ServerFactory(new() { ["RATE_SYNC_PER_MIN"] = "2" });
        var s = await limited.RegisterAsync();

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++) codes.Add((await Sync(s, 0, [], [])).StatusCode);

        codes.ShouldBe([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests]);
    }

    [Fact]
    public async Task Export_returns_a_json_archive_and_delete_wipes_the_account()
    {
        var a = await _f.RegisterAsync("gone@example.com");
        var ws = Guid.NewGuid();
        await Sync(a, 0, [], [WorkspaceInsert(a.DeviceId, ws, "Exported")]);

        var accepted = await a.Client.GetAsync("/v1/account/export");
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var exportId = (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("export_id").GetGuid();
        var archive = JsonNode.Parse(await a.Client.GetStringAsync($"/v1/account/export/{exportId}"))!;
        archive["email"]!.GetValue<string>().ShouldBe("gone@example.com");
        archive["workspaces"]![0]!["rows"]!["workspace"]![0]!["name"]!.GetValue<string>().ShouldBe("Exported");

        (await a.Client.DeleteAsync("/v1/account")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await a.Client.GetAsync("/v1/devices")).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // device is gone
        (await _f.CreateClient().PostAsJsonAsync("/v1/auth/login", new { email = "gone@example.com", password = "correct horse battery", device = ServerFactory.Device(Guid.NewGuid()) }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.ServerDbContext>();
        db.Ops.Any().ShouldBeFalse();
        db.CurrentRows.Any().ShouldBeFalse();
    }

    [Fact]
    public async Task Exports_belong_to_their_owner()
    {
        var a = await _f.RegisterAsync();
        var b = await _f.RegisterAsync();
        var id = (await (await a.Client.GetAsync("/v1/account/export")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("export_id").GetGuid();

        (await b.Client.GetAsync($"/v1/account/export/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
