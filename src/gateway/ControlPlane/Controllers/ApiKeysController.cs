using Gateway.ControlPlane.Models;
using Gateway.ControlPlane.Stores;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.ControlPlane.Controllers;

[ApiController]
[Route("cp/apikeys")]
public sealed class ApiKeysController : ControllerBase
{
    private readonly IApiKeyStore _store;
    private readonly IAuditLog _audit;

    public ApiKeysController(IApiKeyStore store, IAuditLog audit)
        => (_store, _audit) = (store, audit);

    [HttpGet]
    public IEnumerable<ApiKeyRecord> Get() => _store.GetAll();

    [HttpPost]
    public ActionResult<ApiKeyRecord> Create(ApiKeyRecord key)
    {
        var (created, etag) = _store.Add(key);
        Response.Headers.ETag = $"\"{etag}\"";
        _audit.Log(User.Identity?.Name ?? "anon", "apikey", created.Id, "create", null, created);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public IActionResult Update(string id, ApiKeyRecord key)
    {
        if (!Request.Headers.TryGetValue("If-Match", out var etag))
            return BadRequest();
        var match = etag!.ToString().Trim('"');
        if (!_store.TryUpdate(id, key with { Id = id }, match, out var newEtag, out var before))
            return Conflict();
        Response.Headers.ETag = $"\"{newEtag}\"";
        _audit.Log(User.Identity?.Name ?? "anon", "apikey", id, "update", before, key);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        if (!Request.Headers.TryGetValue("If-Match", out var etag))
            return BadRequest();
        var match = etag!.ToString().Trim('"');
        if (!_store.TryRemove(id, match, out var before))
            return Conflict();
        _audit.Log(User.Identity?.Name ?? "anon", "apikey", id, "delete", before, null);
        return NoContent();
    }
}
