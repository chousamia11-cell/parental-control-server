using Newtonsoft.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
// ================== بداية كود الحماية ==================
string authUsername = "admin";                    // اسم المستخدم
string authPassword = "MyStrongPassword123!";     // كلمة المرور (غيرها)

app.Use(async (context, next) =>
{
    // استثناء مسارات API حتى لا يتعطل تطبيق هاتف ابنك
   if (context.Request.Path.StartsWithSegments("/api") || 
    context.Request.Path.StartsWithSegments("/report") || 
    context.Request.Path.StartsWithSegments("/screenshot"))
    {
        await next();
        return;
    }

    // التحقق من هيدر المصادقة
    string authHeader = context.Request.Headers["Authorization"];
    if (authHeader != null && authHeader.StartsWith("Basic "))
    {
        var encoded = authHeader.Substring("Basic ".Length).Trim();
        var encoding = System.Text.Encoding.GetEncoding("iso-8859-1");
        var decoded = encoding.GetString(Convert.FromBase64String(encoded));
        var separatorIndex = decoded.IndexOf(':');
        var usernameInput = decoded.Substring(0, separatorIndex);
        var passwordInput = decoded.Substring(separatorIndex + 1);

        if (usernameInput == authUsername && passwordInput == authPassword)
        {
            await next();   // البيانات صحيحة، أكمل الطلب
            return;
        }
    }

    // إذا لم تكن البيانات صحيحة، اطلب المصادقة
    context.Response.Headers["WWW-Authenticate"] = "Basic realm=\"MyControlPanel\"";
    context.Response.StatusCode = 401;   // غير مصرح
});
// ================== نهاية كود الحماية ==================

string dataFolder = Path.Combine(AppContext.BaseDirectory, "Data");
string screenshotsFolder = Path.Combine(AppContext.BaseDirectory, "Screenshots");
Directory.CreateDirectory(dataFolder);
Directory.CreateDirectory(screenshotsFolder);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(screenshotsFolder),
    RequestPath = "/screenshot"
});

// ============ API: استقبال التقارير ============
app.MapPost("/report", async (HttpContext context) =>
{
    using var reader = new StreamReader(context.Request.Body);
    string json = await reader.ReadToEndAsync();
    var report = JsonConvert.DeserializeObject<ReportData>(json);
    if (report == null) return Results.BadRequest();

    string filePath = Path.Combine(dataFolder, $"{report.ChildId}.json");
    var reports = new List<ReportData>();

    if (File.Exists(filePath))
    {
        string existing = await File.ReadAllTextAsync(filePath);
        reports = JsonConvert.DeserializeObject<List<ReportData>>(existing) ?? new List<ReportData>();
    }

    reports.Add(report);
    if (reports.Count > 1000) reports = reports.Skip(reports.Count - 1000).ToList();

    await File.WriteAllTextAsync(filePath, JsonConvert.SerializeObject(reports, Formatting.Indented));

    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] تقرير من {report.ChildId}");
    return Results.Ok(new { status = "ok" });
});

// ============ API: استقبال اللقطات ============
app.MapPost("/screenshot", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();
    string childId = form["child_id"].ToString();
    var file = form.Files["image"];

    if (file == null || string.IsNullOrEmpty(childId)) return Results.BadRequest();

    string fileName = $"{childId}_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
    string filePath = Path.Combine(screenshotsFolder, fileName);

    using (var stream = new FileStream(filePath, FileMode.Create))
        await file.CopyToAsync(stream);

    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] لقطة شاشة من {childId}");
    return Results.Ok(new { status = "ok" });
});

// ============ لوحة التحكم ============
app.MapGet("/dashboard", async () =>
{
    var html = await BuildDashboardHtml(dataFolder, screenshotsFolder);
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapGet("/", () => Results.Redirect("/dashboard"));

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
app.Run($"http://0.0.0.0:{port}");

static async Task<string> BuildDashboardHtml(string dataFolder, string screenshotsFolder)
{
    var sb = new System.Text.StringBuilder();
    sb.Append(@"
<!DOCTYPE html>
<html dir='rtl' lang='ar'>
<head>
<meta charset='UTF-8'>
<meta http-equiv='refresh' content='30'>
<title>لوحة الرقابة الأبوية</title>
<style>
* { font-family: 'Segoe UI', Tahoma, sans-serif; box-sizing: border-box; }
body { background: #f0f2f5; margin: 0; padding: 20px; }
h1 { color: #1a73e8; text-align: center; }
.card { background: #fff; border-radius: 12px; padding: 20px;
        margin: 15px auto; max-width: 1000px;
        box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
.alert { background: #ffebee; border-right: 5px solid #f44336;
         padding: 15px; margin: 10px 0; border-radius: 8px; }
.info { color: #666; font-size: 14px; margin: 5px 0; }
.app-tag { background: #e3f2fd; color: #1976d2; padding: 5px 12px;
           border-radius: 15px; font-size: 13px; display: inline-block; margin: 3px; }
.blocked-tag { background: #ffcdd2; color: #c62828; }
.screenshot { max-width: 100%; border-radius: 8px; margin-top: 10px; }
</style>
</head>
<body>
<h1>👨‍👦 لوحة الرقابة الأبوية</h1>");

    if (Directory.Exists(dataFolder))
    {
        foreach (var file in Directory.GetFiles(dataFolder, "*.json"))
        {
            string childId = Path.GetFileNameWithoutExtension(file);
            string json = await File.ReadAllTextAsync(file);
            var reports = JsonConvert.DeserializeObject<List<ReportData>>(json);
            if (reports == null || reports.Count == 0) continue;

            var latest = reports.Last();
            sb.Append($@"
<div class='card'>
<h2>👦 الطفل: {childId}</h2>
<p class='info'>🕐 آخر تحديث: {latest.Timestamp}</p>
<p class='info'>🖥️ النافذة النشطة: <b>{latest.ActiveWindow}</b></p>
<p class='info'>🌐 IP: {latest.IpAddress}</p>
<p class='info'>📊 عدد التقارير: {reports.Count}</p>");

            if (latest.BlockedApps != null && latest.BlockedApps.Count > 0)
                sb.Append($"<div class='alert'>⚠️ تطبيقات محظورة: {string.Join(", ", latest.BlockedApps)}</div>");

            if (!string.IsNullOrEmpty(latest.TypedText))
            {
                string preview = latest.TypedText.Length > 200
                    ? latest.TypedText.Substring(latest.TypedText.Length - 200) : latest.TypedText;
                sb.Append($"<div class='alert' style='background:#fff3e0;border-color:#ff9800;'>⌨️ آخر ما كُتب: {System.Net.WebUtility.HtmlEncode(preview)}</div>");
            }

            sb.Append("<h3>📱 التطبيقات المفتوحة:</h3><div>");
            if (latest.RunningApps != null)
                foreach (var ap in latest.RunningApps.Take(30))
                {
                    string cls = (latest.BlockedApps != null && latest.BlockedApps.Contains(ap)) ? "app-tag blocked-tag" : "app-tag";
                    sb.Append($"<span class='{cls}'>{ap}</span>");
                }
            sb.Append("</div>");

            var lastShot = Directory.Exists(screenshotsFolder)
                ? Directory.GetFiles(screenshotsFolder, $"{childId}_*.jpg").OrderByDescending(f => f).FirstOrDefault()
                : null;

            if (lastShot != null)
            {
                string fileName = Path.GetFileName(lastShot);
                sb.Append($"<h3>📸 آخر لقطة شاشة:</h3><img src='/screenshot/{fileName}' class='screenshot' />");
            }

            sb.Append("</div>");
        }
    }
    sb.Append("</body></html>");
    return sb.ToString();
}

public class ReportData
{
    [JsonProperty("child_id")] public string ChildId { get; set; }
    [JsonProperty("timestamp")] public string Timestamp { get; set; }
    [JsonProperty("active_window")] public string ActiveWindow { get; set; }
    [JsonProperty("running_apps")] public List<string> RunningApps { get; set; }
    [JsonProperty("typed_text")] public string TypedText { get; set; }
    [JsonProperty("blocked_apps")] public List<string> BlockedApps { get; set; }
    [JsonProperty("ip")] public string IpAddress { get; set; }
}
