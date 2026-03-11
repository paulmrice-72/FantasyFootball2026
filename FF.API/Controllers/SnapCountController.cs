using FF.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/snapcounts")]
[Authorize]
public class SnapCountController(
    ISnapCountImportService snapCountImportService,
    ISnapCountMergeService snapCountMergeService) : ControllerBase
{
    [HttpPost("import/{season:int}")]
    public async Task<IActionResult> Import(int season, CancellationToken cancellationToken)
    {
        var result = await snapCountImportService.ImportAsync(season, cancellationToken);
        if (!result.Success)
            return BadRequest(new { result.ErrorMessage });

        return Ok(new
        {
            result.Inserted,
            result.Replaced,
            Message = $"Snap count import complete for {season}."
        });
    }

    [HttpPost("merge/{season:int}")]
    public async Task<IActionResult> Merge(int season, CancellationToken cancellationToken)
    {
        var result = await snapCountMergeService.MergeAsync(season, cancellationToken);
        if (!result.Success)
            return BadRequest(new { result.ErrorMessage });

        return Ok(new
        {
            result.Merged,
            result.Unmatched,
            Message = $"Snap count merge complete for {season}."
        });
    }
}