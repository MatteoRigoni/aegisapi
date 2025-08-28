using Gateway.ControlPlane.Models;
using Gateway.ControlPlane.Stores;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.ControlPlane.Controllers;

[ApiController]
[Route("cp/routes")]
public sealed class RoutesController : ControllerBase
{
    private readonly IRouteStore _store;
    private readonly IAuditLog _audit;

    public RoutesController(IRouteStore store, IAuditLog audit)
        => (_store, _audit) = (store, audit);

    [HttpGet]
    public IEnumerable<RouteConfig> Get() => _store.GetAll();

    [HttpPost]
    public ActionResult<RouteConfig> Create(RouteConfig route)
    {
        var (created, etag) = _store.Add(route);
        Response.Headers.ETag = $"\"{etag}\"";
        _audit.Log(User.Identity?.Name ?? "anon", "route", created.Id, "create", null, created);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public IActionResult Update(string id, RouteConfig route)
    {
        if (!Request.Headers.TryGetValue("If-Match", out var etag))
            return BadRequest();
        var match = etag!.ToString().Trim('"');
        if (!_store.TryUpdate(id, route with { Id = id }, match, out var newEtag, out var before))
            return Conflict();
        Response.Headers.ETag = $"\"{newEtag}\"";
        _audit.Log(User.Identity?.Name ?? "anon", "route", id, "update", before, route);
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
        _audit.Log(User.Identity?.Name ?? "anon", "route", id, "delete", before, null);
        return NoContent();
    }
}
