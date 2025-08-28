using Gateway.ControlPlane.Models;
using Gateway.ControlPlane.Stores;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.ControlPlane.Controllers;

[ApiController]
[Route("cp/ratelimits")]
public sealed class RateLimitsController : ControllerBase
{
    private readonly IRateLimitPlanStore _store;
    private readonly IAuditLog _audit;

    public RateLimitsController(IRateLimitPlanStore store, IAuditLog audit)
        => (_store, _audit) = (store, audit);

    [HttpGet]
    public IEnumerable<RateLimitPlan> Get() => _store.GetAll();

    [HttpGet("default")]
    public ActionResult<RateLimitPlan> GetDefault()
    {
        var res = _store.Get(IRateLimitPlanStore.DefaultPlan);
        if (res is null)
            return NotFound();
        Response.Headers.ETag = $"\"{res.Value.etag}\"";
        return res.Value.plan;
    }

    [HttpPost]
    public ActionResult<RateLimitPlan> Create(RateLimitPlan plan)
    {
        if (plan.Plan == IRateLimitPlanStore.DefaultPlan || plan.Plan == "default")
            return BadRequest();
        var (created, etag) = _store.Add(plan);
        Response.Headers.ETag = $"\"{etag}\"";
        _audit.Log(User.Identity?.Name ?? "anon", "ratelimit", created.Plan, "create", null, created);
        return CreatedAtAction(nameof(Get), new { id = created.Plan }, created);
    }

    [HttpPut("{plan}")]
    public IActionResult Update(string plan, RateLimitPlan updated)
    {
        if (plan == "default")
            plan = IRateLimitPlanStore.DefaultPlan;
        if (!Request.Headers.TryGetValue("If-Match", out var etag))
            return BadRequest();
        var match = etag!.ToString().Trim('"');
        if (!_store.TryUpdate(plan, updated with { Plan = plan }, match, out var newEtag, out var before))
            return Conflict();
        Response.Headers.ETag = $"\"{newEtag}\"";
        _audit.Log(User.Identity?.Name ?? "anon", "ratelimit", plan, "update", before, updated);
        return NoContent();
    }

    [HttpDelete("{plan}")]
    public IActionResult Delete(string plan)
    {
        if (plan == "default")
            plan = IRateLimitPlanStore.DefaultPlan;
        if (plan == IRateLimitPlanStore.DefaultPlan)
            return BadRequest();
        if (!Request.Headers.TryGetValue("If-Match", out var etag))
            return BadRequest();
        var match = etag!.ToString().Trim('"');
        if (!_store.TryRemove(plan, match, out var before))
            return Conflict();
        _audit.Log(User.Identity?.Name ?? "anon", "ratelimit", plan, "delete", before, null);
        return NoContent();
    }
}
