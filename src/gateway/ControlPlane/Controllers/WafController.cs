using Gateway.ControlPlane.Models;
using Gateway.ControlPlane.Stores;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.ControlPlane.Controllers;

[ApiController]
[Route("cp/waf")]
public sealed class WafController : ControllerBase
{
    private readonly IWafToggleStore _store;
    private readonly IAuditLog _audit;

    public WafController(IWafToggleStore store, IAuditLog audit)
        => (_store, _audit) = (store, audit);

    [HttpGet]
    public IEnumerable<WafToggle> Get() => _store.GetAll();

    [HttpPost]
    public ActionResult<WafToggle> Create(WafToggle toggle)
    {
        var (created, etag) = _store.Add(toggle);
        Response.Headers.ETag = $"\"{etag}\"";
        _audit.Log(User.Identity?.Name ?? "anon", "waf", created.Rule, "create", null, created);
        return CreatedAtAction(nameof(Get), new { id = created.Rule }, created);
    }

    [HttpPut("{rule}")]
    public IActionResult Update(string rule, WafToggle? toggle)
    {
        if (toggle is null)
            return BadRequest();
        if (!Request.Headers.TryGetValue("If-Match", out var etag))
            return BadRequest();
        var match = etag!.ToString().Trim('"');
        var updated = toggle with { Rule = rule };
        if (!_store.TryUpdate(rule, updated, match, out var newEtag, out var before))
            return Conflict();
        Response.Headers.ETag = $"\"{newEtag}\"";
        _audit.Log(User.Identity?.Name ?? "anon", "waf", rule, "update", before, updated);
        return NoContent();
    }

    [HttpDelete("{rule}")]
    public IActionResult Delete(string rule)
    {
        if (!Request.Headers.TryGetValue("If-Match", out var etag))
            return BadRequest();
        var match = etag!.ToString().Trim('"');
        if (!_store.TryRemove(rule, match, out var before))
            return Conflict();
        _audit.Log(User.Identity?.Name ?? "anon", "waf", rule, "delete", before, null);
        return NoContent();
    }
}
