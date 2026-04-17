using System.Text.Json;
using SPOTrim.Engine.Models;

namespace SPOTrim.Engine.Http;

/// <summary>
/// Registers all REST API routes on the WebServer.
/// Each route delegates to the Engine facade.
/// </summary>
public static class ApiRoutes
{
    public static void Register(WebServer server, Engine engine)
    {
        // ── Status ──────────────────────────────────────────────
        server.Route("GET", "/api/status", async (ctx, _) =>
        {
            await engine.EnsureSessionRestoredAsync();
            var status = engine.GetStatus();
            await WebServer.WriteJson(ctx.Response, 200, ApiResponse<StatusResponse>.Ok(status));
        });

        // ── Authentication ──────────────────────────────────────
        server.Route("POST", "/api/connect", async (ctx, _) =>
        {
            try
            {
                await engine.ConnectAsync();
                var status = engine.GetStatus();
                await WebServer.WriteJson(ctx.Response, 200, ApiResponse<StatusResponse>.Ok(status));
            }
            catch (Exception ex)
            {
                await WebServer.WriteJson(ctx.Response, 400, ApiResponse.Fail(ex.Message));
            }
        });

        server.Route("POST", "/api/disconnect", async (ctx, _) =>
        {
            engine.Disconnect();
            await WebServer.WriteJson(ctx.Response, 200, ApiResponse.Ok());
        });

        // ── Configuration ───────────────────────────────────────
        server.Route("GET", "/api/config", async (ctx, _) =>
        {
            var config = engine.GetConfig();
            await WebServer.WriteJson(ctx.Response, 200, ApiResponse<AppConfig>.Ok(config));
        });

        server.Route("PUT", "/api/config", async (ctx, _) =>
        {
            var update = await WebServer.ReadJson<Dictionary<string, JsonElement>>(ctx.Request);
            if (update == null)
            {
                await WebServer.WriteJson(ctx.Response, 400, ApiResponse.Fail("Invalid request body"));
                return;
            }
            engine.UpdateConfig(update);
            var config = engine.GetConfig();
            await WebServer.WriteJson(ctx.Response, 200, ApiResponse<AppConfig>.Ok(config));
        });

        // ── Scanning ────────────────────────────────────────────
        server.Route("POST", "/api/scan/start", async (ctx, _) =>
        {
            var body = await WebServer.ReadJson<JsonElement>(ctx.Request);
            var scanType = "Discovery";
            if (body.TryGetProperty("scanType", out var st) && st.GetString() is string s)
                scanType = s;

            try
            {
                var scanId = await engine.StartScanAsync(scanType);
                await WebServer.WriteJson(ctx.Response, 200, ApiResponse<object>.Ok(new { scanId }));
            }
            catch (InvalidOperationException ex)
            {
                await WebServer.WriteJson(ctx.Response, 409, ApiResponse.Fail(ex.Message));
            }
        });

        server.Route("GET", "/api/scan/progress", async (ctx, _) =>
        {
            var progress = engine.GetScanProgress();
            await WebServer.WriteJson(ctx.Response, 200, ApiResponse<AggregatedProgress?>.Ok(progress));
        });

        server.Route("POST", "/api/scan/cancel", async (ctx, _) =>
        {
            engine.CancelScan();
            await WebServer.WriteJson(ctx.Response, 200, ApiResponse.Ok());
        });

        // ── Scan Results ────────────────────────────────────────
        server.Route("GET", "/api/scans", async (ctx, _) =>
        {
            var tenantId = ctx.Request.QueryString["tenantId"];
            var scans = engine.GetScans(tenantId);
            await WebServer.WriteJson(ctx.Response, 200, ApiResponse<List<ScanInfo>>.Ok(scans));
        });

        server.Route("GET", "/api/scans/:id/sites", async (ctx, routeParams) =>
        {
            if (!long.TryParse(routeParams["id"], out var scanId))
            {
                await WebServer.WriteJson(ctx.Response, 400, ApiResponse.Fail("Invalid scan ID"));
                return;
            }
            var sites = engine.GetSites(scanId);
            await WebServer.WriteJson(ctx.Response, 200, ApiResponse<List<SiteInfo>>.Ok(sites));
        });

        server.Route("GET", "/api/scans/:id/export", async (ctx, routeParams) =>
        {
            if (!long.TryParse(routeParams["id"], out var scanId))
            {
                await WebServer.WriteJson(ctx.Response, 400, ApiResponse.Fail("Invalid scan ID"));
                return;
            }
            var format = ctx.Request.QueryString["format"] ?? "xlsx";
            var (bytes, fileName, contentType) = engine.ExportScan(scanId, format);
            ctx.Response.ContentType = contentType;
            ctx.Response.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        });

        // ── Dashboard ───────────────────────────────────────────
        server.Route("GET", "/api/dashboard", async (ctx, _) =>
        {
            var stats = engine.GetDashboardStats();
            await WebServer.WriteJson(ctx.Response, 200, ApiResponse<DashboardStats>.Ok(stats));
        });

        // ── Audit Log ───────────────────────────────────────────
        server.Route("GET", "/api/audit", async (ctx, _) =>
        {
            var limitStr = ctx.Request.QueryString["limit"];
            var limit = int.TryParse(limitStr, out var l) ? l : 100;
            var entries = engine.GetAuditLog(limit);
            await WebServer.WriteJson(ctx.Response, 200, ApiResponse<List<Database.AuditEntry>>.Ok(entries));
        });

        // ── Database ────────────────────────────────────────────
        server.Route("GET", "/api/database", async (ctx, _) =>
        {
            var info = engine.GetDatabaseInfo();
            await WebServer.WriteJson(ctx.Response, 200, ApiResponse<Database.DatabaseInfo>.Ok(info));
        });
    }
}
