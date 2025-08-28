using Gateway.ControlPlane.Models;
using Gateway.ControlPlane.Stores;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.ControlPlane.Controllers;

[ApiController]
[Route("cp/schemas")]
public sealed class SchemaController : ControllerBase
{
    private readonly ISchemaStore _store;
    private readonly IAuditLog _audit;

    public SchemaController(ISchemaStore store, IAuditLog audit)
        => (_store, _audit) = (store, audit);

    [HttpGet]
    public IEnumerable<SchemaRecord> Get() => _store.GetAll();

    [HttpPost]
    public ActionResult<SchemaRecord> Create(SchemaRecord schema)
    {
        var (created, etag) = _store.Add(schema);
        Response.Headers.ETag = etag;
        _audit.Log(User.Identity?.Name ?? "anon", "schema", created.Path, "create", null, created);
        return CreatedAtAction(nameof(Get), new { path = created.Path }, created);
    }

    [HttpPut("{path}")]
    public IActionResult Update(string path, SchemaRecord schema)
    {
        if (!Request.Headers.TryGetValue("If-Match", out var etag))
            return BadRequest();
        if (!_store.TryUpdate(path, schema with { Path = path }, etag!, out var newEtag, out var before))
            return Conflict();
        Response.Headers.ETag = newEtag;
        _audit.Log(User.Identity?.Name ?? "anon", "schema", path, "update", before, schema);
        return NoContent();
    }

    [HttpDelete("{path}")]
    public IActionResult Delete(string path)
    {
        if (!Request.Headers.TryGetValue("If-Match", out var etag))
            return BadRequest();
        if (!_store.TryRemove(path, etag!, out var before))
            return Conflict();
        _audit.Log(User.Identity?.Name ?? "anon", "schema", path, "delete", before, null);
        return NoContent();
    }
}
