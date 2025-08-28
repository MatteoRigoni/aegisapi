using Gateway.ControlPlane.Models;
using Gateway.ControlPlane.Stores;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.ControlPlane.Controllers;

[ApiController]
[Route("cp/audit")]
public sealed class AuditController : ControllerBase
{
    private readonly IAuditLog _log;
    public AuditController(IAuditLog log) => _log = log;

    [HttpGet]
    public IEnumerable<AuditEntry> Get() => _log.GetAll();
}
