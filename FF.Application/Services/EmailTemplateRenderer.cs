// FF.Application/Services/EmailTemplateRenderer.cs   ← fix comment
using FF.Domain.Documents;
using System.Text;

namespace FF.Application.Services;   // ← was FF.Infrastructure.Services

public static class EmailTemplateRenderer
{
    public static string RenderWarRoomBrief(WarRoomBriefDocument brief)
    {
        var sb = new StringBuilder();

        sb.Append("""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:0;background-color:#0A0F1E;font-family:'Segoe UI',Arial,sans-serif;color:#E2E8F0;">
              <table width="100%" cellpadding="0" cellspacing="0" style="background:#0A0F1E;">
                <tr><td align="center" style="padding:24px 16px;">
                  <table width="600" cellpadding="0" cellspacing="0" style="max-width:600px;width:100%;">
            """);

        // Header
        sb.Append($"""
            <tr>
              <td style="background:#0D1526;border-top:3px solid #00C8F0;border-radius:8px 8px 0 0;padding:24px 32px;">
                <div style="font-size:22px;font-weight:700;color:#00C8F0;letter-spacing:1px;">FantasyCombine.AI</div>
                <div style="font-size:13px;color:#94A3B8;margin-top:4px;">
                  War Room Brief &mdash; Season {brief.Season} &middot; Week {brief.Week}
                </div>
              </td>
            </tr>
            """);

        // Coach Riley
        if (!string.IsNullOrWhiteSpace(brief.CoachRileyNarrative))
        {
            sb.Append($"""
                <tr>
                  <td style="background:#111827;padding:20px 32px;border-left:3px solid #00C8F0;">
                    <div style="font-size:12px;color:#00C8F0;text-transform:uppercase;letter-spacing:1px;margin-bottom:8px;">Coach Riley</div>
                    <p style="margin:0;font-size:14px;line-height:1.6;color:#CBD5E1;">{brief.CoachRileyNarrative}</p>
                  </td>
                </tr>
                """);
        }

        // League sections
        if (brief.Leagues.Any())
        {
            sb.Append("""
                <tr>
                  <td style="background:#0D1526;padding:20px 32px;">
                    <div style="font-size:12px;color:#00C8F0;text-transform:uppercase;letter-spacing:1px;margin-bottom:12px;">Your Leagues</div>
                """);
            foreach (var league in brief.Leagues)
            {
                sb.Append($"""
                    <div style="margin-bottom:24px;">
                      <h3 style="color:#00C8F0;font-size:16px;margin:0 0 8px 0;border-bottom:1px solid #1E2A45;padding-bottom:6px;">
                        {league.LeagueName} &mdash; {league.TeamName}
                      </h3>
                      <p style="color:#94A3B8;font-size:13px;margin:4px 0;">{league.LeagueNarrative}</p>
                    </div>
                    """);
            }
            sb.Append("</td></tr>");
        }

        // Boom candidates
        if (brief.TopBoomCandidates.Any())
        {
            sb.Append("""
                <tr>
                  <td style="background:#0D1526;padding:20px 32px;border-top:1px solid #1E2A45;">
                    <div style="font-size:12px;color:#22C55E;text-transform:uppercase;letter-spacing:1px;margin-bottom:12px;">Boom Candidates</div>
                    <table width="100%" cellpadding="0" cellspacing="0" style="font-size:13px;">
                      <tr style="color:#64748B;font-size:11px;text-transform:uppercase;">
                        <th style="padding:6px 12px;text-align:left;">Player</th>
                        <th style="padding:6px 12px;text-align:left;">Pos</th>
                        <th style="padding:6px 12px;text-align:left;">Med</th>
                        <th style="padding:6px 12px;text-align:left;">Ceil</th>
                        <th style="padding:6px 12px;text-align:left;">Boom%</th>
                        <th style="padding:6px 12px;text-align:left;">Why</th>
                      </tr>
                """);
            foreach (var p in brief.TopBoomCandidates)
            {
                sb.Append($"""
                    <tr>
                      <td style="padding:8px 12px;border-bottom:1px solid #1E2A45;">{p.PlayerName}</td>
                      <td style="padding:8px 12px;border-bottom:1px solid #1E2A45;">{p.Position}</td>
                      <td style="padding:8px 12px;border-bottom:1px solid #1E2A45;color:#00C8F0;">{p.Median:F1}</td>
                      <td style="padding:8px 12px;border-bottom:1px solid #1E2A45;color:#22C55E;">{p.Ceiling:F1}</td>
                      <td style="padding:8px 12px;border-bottom:1px solid #1E2A45;">{p.BoomProbability:P0}</td>
                      <td style="padding:8px 12px;border-bottom:1px solid #1E2A45;font-size:11px;color:#94A3B8;">{p.HighlightReason}</td>
                    </tr>
                    """);
            }
            sb.Append("</table></td></tr>");
        }

        // Bust risks
        if (brief.BustRisks.Any())
        {
            sb.Append("""
                <tr>
                  <td style="background:#0D1526;padding:20px 32px;border-top:1px solid #1E2A45;">
                    <div style="font-size:12px;color:#EF4444;text-transform:uppercase;letter-spacing:1px;margin-bottom:12px;">Bust Risks</div>
                    <table width="100%" cellpadding="0" cellspacing="0" style="font-size:13px;">
                      <tr style="color:#64748B;font-size:11px;text-transform:uppercase;">
                        <th style="padding:6px 12px;text-align:left;">Player</th>
                        <th style="padding:6px 12px;text-align:left;">Pos</th>
                        <th style="padding:6px 12px;text-align:left;">Med</th>
                        <th style="padding:6px 12px;text-align:left;">Floor</th>
                        <th style="padding:6px 12px;text-align:left;">Bust%</th>
                        <th style="padding:6px 12px;text-align:left;">Why</th>
                      </tr>
                """);
            foreach (var p in brief.BustRisks)
            {
                sb.Append($"""
                    <tr>
                      <td style="padding:8px 12px;border-bottom:1px solid #1E2A45;">{p.PlayerName}</td>
                      <td style="padding:8px 12px;border-bottom:1px solid #1E2A45;">{p.Position}</td>
                      <td style="padding:8px 12px;border-bottom:1px solid #1E2A45;color:#00C8F0;">{p.Median:F1}</td>
                      <td style="padding:8px 12px;border-bottom:1px solid #1E2A45;color:#EF4444;">{p.Floor:F1}</td>
                      <td style="padding:8px 12px;border-bottom:1px solid #1E2A45;">{p.BustProbability:P0}</td>
                      <td style="padding:8px 12px;border-bottom:1px solid #1E2A45;font-size:11px;color:#94A3B8;">{p.HighlightReason}</td>
                    </tr>
                    """);
            }
            sb.Append("</table></td></tr>");
        }

        // CTA footer
        sb.Append("""
            <tr>
              <td style="background:#0D1526;padding:20px 32px;border-top:1px solid #1E2A45;text-align:center;border-radius:0 0 8px 8px;">
                <a href="https://fantasycombine.ai/my-brief"
                   style="display:inline-block;background:#00C8F0;color:#0A0F1E;font-weight:700;
                          padding:12px 32px;border-radius:6px;text-decoration:none;font-size:14px;letter-spacing:0.5px;">
                  View Full War Room Brief &rarr;
                </a>
                <p style="margin:16px 0 0;font-size:11px;color:#475569;">
                  FantasyCombine.AI &middot; You're receiving this because you opted in to War Room Briefs.
                </p>
              </td>
            </tr>
            """);

        sb.Append("""
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """);

        return sb.ToString();
    }
}