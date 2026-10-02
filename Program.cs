using Newtonsoft.Json;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// 1. Auth credentials from environment variables
string authUsername = Environment.GetEnvironmentVariable("AUTH_USERNAME") ?? "admin";
string authPassword = Environment.GetEnvironmentVariable("AUTH_PASSWORD") ?? "";

// 2. Paths setup
string baseDirectory = AppContext.BaseDirectory;
string dataFolder = Path.Combine(baseDirectory, "Data");
string screenshotsFolder = Path.Combine(baseDirectory, "Screenshots");

Directory.CreateDirectory(dataFolder);
Directory.CreateDirectory(screenshotsFolder);

// 3. Basic Auth Middleware
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/dashboard") ||
        context.Request.Path.StartsWithSegments("/report") ||
        context.Request.Path.StartsWithSegments("/screenshot"))
    {
        string? authHeader = context.Request.Headers["Authorization"];
        if (authHeader != null && authHeader.StartsWith("Basic "))
        {
            try
            {
                var encoded = authHeader.Substring("Basic ".Length).Trim();
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                var separatorIndex = decoded.IndexOf(':');

                if (separatorIndex != -1)
                {
                    var usernameInput = decoded.Substring(0, separatorIndex);
                    var passwordInput = decoded.Substring(separatorIndex + 1);

                    if (usernameInput == authUsername && passwordInput == authPassword)
                    {
                        await next();
                        return;
                    }
                }
            }
            catch
            {
                // Ignore decoding errors
            }
        }

        context.Response.Headers["WWW-Authenticate"] = "Basic realm=\"MyControlPanel\"";
        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("Unauthorized");
        return;
    }

    await next();
});

// 4. Static files for screenshots
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(screenshotsFolder),
    RequestPath = "/screenshot"
});

// 5. API: Receive reports
app.MapPost("/report", async (HttpContext context) =>
{
    using var reader = new StreamReader(context.Request.Body);
    string json = await reader.ReadToEndAsync();

    if (string.IsNullOrWhiteSpace(json)) return Results.BadRequest("Empty body");

    var report = JsonConvert.DeserializeObject<ReportData>(json);
    if (report == null || string.IsNullOrEmpty(report.ChildId)) 
        return Results.BadRequest("Invalid data");

    string filePath = Path.Combine(dataFolder, $"{report.ChildId}.json");
    var reports = new List<ReportData>();

    if (File.Exists(filePath))
    {
        string existing = await File.ReadAllTextAsync(filePath);
        reports = JsonConvert.DeserializeObject<List<ReportData>>(existing) ?? new List<ReportData>();
    }

    reports.Add(report);

    // Keep only last 1000 reports
    if (reports.Count > 1000)
        reports = reports.OrderByDescending(r => r.Timestamp).Take(1000).ToList();

    await File.WriteAllTextAsync(filePath, JsonConvert.SerializeObject(reports, Formatting.Indented));

    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Report received from {report.ChildId}");
    return Results.Ok(new { status = "ok" });
});

// 6. API: Receive screenshots
app.MapPost("/screenshot", async (HttpContext context) =>
{
    if (!context.Request.HasFormContentType) return Results.BadRequest("Invalid form data");

    var form = await context.Request.ReadFormAsync();
    string childId = form["child_id"].ToString();
    var file = form.Files["image"];

    if (file == null || string.IsNullOrEmpty(childId))
        return Results.BadRequest("Missing child_id or image");

    string fileName = $"{childId}_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
    string filePath = Path.Combine(screenshotsFolder, fileName);

    using (var stream = new FileStream(filePath, FileMode.Create))
    {
        await file.CopyToAsync(stream);
    }

    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Screenshot received from {childId}");
    return Results.Ok(new { status = "ok" });
});

// 7. Dashboard
app.MapGet("/dashboard", async () =>
{
    var html = await BuildDashboardHtml(dataFolder, screenshotsFolder);
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapGet("/", () => Results.Redirect("/dashboard"));

// 8. Debug endpoint
app.MapGet("/debug", () =>
{
    var info = new
    {
        dataFolder,
        dataFolderExists = Directory.Exists(dataFolder),
        jsonFiles = Directory.Exists(dataFolder)
            ? Directory.GetFiles(dataFolder, "*.json").Select(Path.GetFileName).ToArray()
            : new string[0],
        screenshotsFolder,
        screenshotsExists = Directory.Exists(screenshotsFolder),
        jpgFiles = Directory.Exists(screenshotsFolder)
            ? Directory.GetFiles(screenshotsFolder, "*.jpg").Select(Path.GetFileName).Take(20).ToArray()
            : new string[0],
        baseDirectory = AppContext.BaseDirectory,
        currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
    };
    return Results.Json(info);
});

// 9. Run the app
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
app.Run($"http://0.0.0.0:{port}");

// ============================================
// 10. Dashboard HTML builder
// ============================================
static async Task<string> BuildDashboardHtml(string dataFolder, string screenshotsFolder)
{
    Console.WriteLine("========== [DEBUG START] ==========");
    Console.WriteLine($"[DEBUG] dataFolder = {dataFolder}");
    Console.WriteLine($"[DEBUG] Exists = {Directory.Exists(dataFolder)}");
    if (Directory.Exists(dataFolder))
    {
        var files = Directory.GetFiles(dataFolder, "*.json");
        Console.WriteLine($"[DEBUG] JSON files count = {files.Length}");
        foreach (var f in files) Console.WriteLine($"[DEBUG] File: {f}");
    }
    else
    {
        Console.WriteLine("[DEBUG] dataFolder DOES NOT EXIST!");
    }
    Console.WriteLine("========== [DEBUG END] ==========");

    var sb = new StringBuilder();

    sb.Append("<!DOCTYPE html>");
    sb.Append("<html dir='rtl' lang='ar'>");
    sb.Append("<head>");
    sb.Append("<meta charset='UTF-8'>");
    sb.Append("<meta http-equiv='refresh' content='30'>");
    sb.Append("<title>Parental Control Dashboard</title>");
    sb.Append("<style>");
    sb.Append("* { font-family: 'Segoe UI', Tahoma, sans-serif; box-sizing: border-box; }");
    sb.Append("body { background: #f0f2f5; margin: 0; padding: 20px; }");
    sb.Append("h1 { color: #1a73e8; text-align: center; }");
    sb.Append(".card { background: #fff; border-radius: 12px; padding: 20px; margin: 15px auto; max-width: 1000px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }");
    sb.Append(".alert { background: #ffeeee; border-right: 5px solid #f44336; padding: 15px; margin: 10px 0; border-radius: 8px; }");
    sb.Append(".info { color: #666; font-size: 14px; margin: 5px 0; }");
    sb.Append(".app-tag { background: #e3f2fd; color: #1976d2; padding: 5px 12px; border-radius: 15px; font-size: 13px; display: inline-block; margin: 3px; }");
    sb.Append(".blocked-tag { background: #ffcdd2; color: #c62828; }");
    sb.Append(".screenshot { max-width: 100%; border-radius: 8px; margin-top: 10px; }");
    sb.Append("</style>");
    sb.Append("</head>");
    sb.Append("<body>");
    sb.Append("<h1>Parental Control Dashboard</h1>");

    if (Directory.Exists(dataFolder))
    {
        foreach (var file in Directory.EnumerateFiles(dataFolder, "*.json"))
        {
            string childId = Path.GetFileNameWithoutExtension(file);
            string json = await File.ReadAllTextAsync(file);
            var reports = JsonConvert.DeserializeObject<List<ReportData>>(json);

            if (reports == null || reports.Count == 0) continue;

            var latest = reports.OrderByDescending(r => r.Timestamp).First();

            sb.Append("<div class='card'>");
            sb.Append($"<h2>Child: {childId}</h2>");
            sb.Append($"<p class='info'>Last Update: {latest.Timestamp}</p>");
            sb.Append($"<p class='info'>Active Window: <b>{latest.ActiveWindow}</b></p>");
            sb.Append($"<p class='info'>IP: {latest.IPAddress}</p>");
            sb.Append($"<p class='info'>Reports: {reports.Count}</p>");

            if (latest.BlockedApps != null && latest.BlockedApps.Count > 0)
            {
                sb.Append($"<div class='alert'>Blocked Apps: {string.Join(", ", latest.BlockedApps)}</div>");
            }

            if (!string.IsNullOrEmpty(latest.TypedText))
            {
                string preview = latest.TypedText.Length > 200
                    ? latest.TypedText.Substring(latest.TypedText.Length - 200)
                    : latest.TypedText;

                sb.Append($"<div class='alert' style='background:#fff3e0;border-color:#ff9800;'>Typed Text: {System.Net.WebUtility.HtmlEncode(preview)}</div>");
            }

            sb.Append("<h3>Running Apps:</h3><div>");
            if (latest.RunningApps != null)
            {
                foreach (var ap in latest.RunningApps.Take(30))
                {
                    string cls = (latest.BlockedApps != null && latest.BlockedApps.Contains(ap))
                        ? "app-tag blocked-tag"
                        : "app-tag";
                    sb.Append($"<span class='{cls}'>{ap}</span>");
                }
            }
            sb.Append("</div>");

            var lastShot = Directory.Exists(screenshotsFolder)
                ? Directory.EnumerateFiles(screenshotsFolder, $"{childId}_*.jpg")
                    .OrderByDescending(f => f).FirstOrDefault()
                : null;

            if (lastShot != null)
            {
                string fileName = Path.GetFileName(lastShot);
                sb.Append($"<h3>Screenshot:</h3><img src='/screenshot/{fileName}' class='screenshot' />");
            }

            sb.Append("</div>");
        }
    }

    sb.Append("</body></html>");
    return sb.ToString();
}

// ============================================
// 11. Data model
// ============================================
public class ReportData
{
    [JsonProperty("child_id")] public string ChildId { get; set; } = "";
    [JsonProperty("timestamp")] public string Timestamp { get; set; } = "";
    [JsonProperty("active_window")] public string ActiveWindow { get; set; } = "";
    [JsonProperty("running_apps")] public List<string> RunningApps { get; set; } = new();
    [JsonProperty("typed_text")] public string TypedText { get; set; } = "";
    [JsonProperty("blocked_apps")] public List<string> BlockedApps { get; set; } = new();
    [JsonProperty("ip")] public string IPAddress { get; set; } = "";
}
