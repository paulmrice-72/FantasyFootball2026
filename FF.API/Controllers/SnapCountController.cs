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
    // A Mongo bulk-write failure carries one WriteError per failing row — 26,000+ on a
    // full season. Returning that whole string was almost certainly what killed the
    // connection against production instead of producing a readable 400.
    private const int MaxErrorLength = 2000;

    [HttpPost("import/{season:int}")]
    public async Task<IActionResult> Import(int season, CancellationToken cancellationToken)
    {
        var result = await snapCountImportService.ImportAsync(season, cancellationToken);
        if (!result.Success)
            return BadRequest(new { ErrorMessage = Truncate(result.ErrorMessage) });

        return Ok(new
        {
            result.Inserted,
            result.Replaced,
            Message = result.Inserted == 0 && result.Replaced == 0
                ? $"Snap count import for {season} wrote no rows."
                : $"Snap count import complete for {season}."
        });
    }

    [HttpPost("merge/{season:int}")]
    public async Task<IActionResult> Merge(int season, CancellationToken cancellationToken)
    {
        var result = await snapCountMergeService.MergeAsync(season, cancellationToken);
        if (!result.Success)
            return BadRequest(new { ErrorMessage = Truncate(result.ErrorMessage) });

        return Ok(new
        {
            result.Merged,
            result.Unmatched,
            Message = result.Merged == 0
                ? $"Snap count merge for {season} matched nothing."
                : $"Snap count merge complete for {season}."
        });
    }

    private static string? Truncate(string? message) =>
        message is null || message.Length <= MaxErrorLength
            ? message
            : message[..MaxErrorLength] + $"… [truncated, {message.Length} chars total]";
}
