using Newtonsoft.Json;
using System.Text;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

// 1. إعداد المتغيرات من البيئة (بدلاً من Hardcoding)
string authUsername = Environment.GetEnvironmentVariable("AUTH_USERNAME") ?? "admin";
string authPassword = Environment.GetEnvironmentVariable("AUTH_PASSWORD") ?? "MyStrongPassword123!";

// 2. إعداد المسارات والمجلدات
string baseDirectory = AppContext.BaseDirectory;
string dataFolder = Path.Combine(baseDirectory, "Data");
string screenshotsFolder = Path.Combine(baseDirectory, "Screenshots");

// إنشاء المجلدات إذا لم تكن موجودة
Directory.CreateDirectory(dataFolder);
Directory.CreateDirectory(screenshotsFolder);

var app = builder.Build();

// 3. Middleware للمصادقة (Basic Auth)
app.Use(async (context, next) =>
{
    // المسارات المسموح بها بدون مصادقة (مثل الصفحة الرئيسية أو الملفات الثابتة)
    // ملاحظة: تم إزالة التكرار في الشروط
    if (context.Request.Path.StartsWithSegments("/api") ||
        context.Request.Path.StartsWithSegments("/report") ||
        context.Request.Path.StartsWithSegments("/screenshot") ||
        context.Request.Path.StartsWithSegments("/dashboard")) // السماح بالوصول للوحة التحكم
    {
        // التحقق من المصادقة
        string authHeader = context.Request.Headers["Authorization"];
        if (authHeader != null && authHeader.StartsWith("Basic "))
        {
            try
            {
                var encoded = authHeader.Substring("Basic ".Length).Trim();
                var encoding = Encoding.UTF8; // استخدام UTF8 بدلاً من iso-8859-1
                var decoded = encoding.GetString(Convert.FromBase64String(encoded));
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
                // تجاهل الأخطاء في فك التشفير
            }
        }

        // إذا لم يتم التحقق بنجاح
        context.Response.Headers["WWW-Authenticate"] = "Basic realm=\"MyControlPanel\"";
        context.Response.StatusCode = 401; // Unauthorized
        await context.Response.WriteAsync("Unauthorized");
        return;
    }

    // المسارات الأخرى (مثل الملفات الثابتة) تمر مباشرة
    await next();
});

// 4. خدمة الملفات الثابتة للقطات الشاشة
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(screenshotsFolder),
    RequestPath = "/screenshot"
});

// 5. API: استقبال التقارير
app.MapPost("/report", async (HttpContext context) =>
{
    using var reader = new StreamReader(context.Request.Body);
    string json = await reader.ReadToEndAsync();
    
    if (string.IsNullOrWhiteSpace(json)) return Results.BadRequest("Empty body");

    var report = JsonConvert.DeserializeObject<ReportData>(json);
    if (report == null || string.IsNullOrEmpty(report.ChildId)) return Results.BadRequest("Invalid data");

    string filePath = Path.Combine(dataFolder, $"{report.ChildId}.json");
    var reports = new List<ReportData>();

    if (File.Exists(filePath))
    {
        string existing = await File.ReadAllTextAsync(filePath);
        reports = JsonConvert.DeserializeObject<List<ReportData>>(existing) ?? new List<ReportData>();
    }

    reports.Add(report);

    // الاحتفاظ بآخر 1000 تقرير فقط
    if (reports.Count > 1000) 
        reports = reports.OrderByDescending(r => r.Timestamp).Take(1000).ToList();

    await File.WriteAllTextAsync(filePath, JsonConvert.SerializeObject(reports, Formatting.Indented));

    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] تم استلام تقرير من {report.ChildId}");
    return Results.Ok(new { status = "ok" });
});

// 6. API: استقبال لقطات الشاشة
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

    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] تم استلام لقطة شاشة من {childId}");
    return Results.Ok(new { status = "ok" });
});

// 7. لوحة التحكم (Dashboard)
app.MapGet("/dashboard", async () =>
{
    var html = await BuildDashboardHtml(dataFolder, screenshotsFolder);
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapGet("/", () => Results.Redirect("/dashboard"));

// 8. تشغيل التطبيق
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
app.Run($"http://0.0.0.0:{port}");

// 9. دالة بناء لوحة التحكم
static async Task<string> BuildDashboardHtml(string dataFolder, string screenshotsFolder)
{
    var sb = new StringBuilder();
    sb.Append(@"
<!DOCTYPE html>
<html dir='rtl' lang='ar'>
<head>
    <meta charset='UTF-8'>
    <meta http-equiv='refresh' content='30'>
    <title>لوحة المراقبة الأبوية</title>
    <style>
        * { font-family: 'Segoe UI', Tahoma, sans-serif; box-sizing: border-box; }
        body { background: #f0f2f5; margin: 0; padding: 20px; }
        h1 { color: #1a73e8; text-align: center; }
        .card { background: #fff; border-radius: 12px; padding: 20px; margin: 15px auto; max-width: 1000px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
        .alert { background: #ffeeee; border-right: 5px solid #f44336; padding: 15px; margin: 10px 0; border-radius: 8px; }
        .info { color: #666; font-size: 14px; margin: 5px 0; }
        .app-tag { background: #e3f2fd; color: #1976d2; padding: 5px 12px; border-radius: 15px; font-size: 13px; display: inline-block; margin: 3px; }
        .blocked-tag { background: #ffcdd2; color: #c62828; }
        .screenshot { max-width: 100%; border-radius: 8px; margin-top: 10px; }
    </style>
</head>
<body>
    <h1>📊 لوحة المراقبة الأبوية</h1>
");

    if (Directory.Exists(dataFolder))
    {
        // استخدام EnumerateFiles لتحسين الأداء
        foreach (var file in Directory.EnumerateFiles(dataFolder, "*.json"))
        {
            string childId = Path.GetFileNameWithoutExtension(file);
            string json = await File.ReadAllTextAsync(file);
            var reports = JsonConvert.DeserializeObject<List<ReportData>>(json);

            if (reports == null || reports.Count == 0) continue;

            var latest = reports.OrderByDescending(r => r.Timestamp).First();

            sb.Append($@"
    <div class='card'>
        <h2>👤 الطفل: {childId}</h2>
        <p class='info'>⏰ آخر تحديث: {latest.Timestamp}</p>
        <p class='info'>🪟 النافذة النشطة: <b>{latest.ActiveWindow}</b></p>
        <p class='info'>🌐 IP: {latest.IPAddress}</p>
        <p class='info'>📊 عدد التقارير: {reports.Count}</p>
");

            if (latest.BlockedApps != null && latest.BlockedApps.Count > 0)
            {
                sb.Append($"<div class='alert'>⚠️ تطبيقات ممنوعة: {string.Join(", ", latest.BlockedApps)}</div>");
            }

            if (!string.IsNullOrEmpty(latest.TypedText))
            {
                string preview = latest.TypedText.Length > 200 
                    ? latest.TypedText.Substring(latest.TypedText.Length - 200) 
                    : latest.TypedText;
                
                // تصحيح: استخدام HtmlEncode لمنع XSS
                sb.Append($"<div class='alert' style='background:#fff3e0;border-color:#ff9800;'>⌨️ آخر ما تم كتابته: {System.Net.WebUtility.HtmlEncode(preview)}</div>");
            }

            sb.Append("<h3>📱 التطبيقات المفتوحة:</h3><div>");
            if (latest.RunningApps != null)
            {
                foreach (var ap in latest.RunningApps.Take(30))
                {
                    // تصحيح: التحقق من Null قبل الاستخدام
                    string cls = (latest.BlockedApps != null && latest.BlockedApps.Contains(ap)) ? "app-tag blocked-tag" : "app-tag";
                    sb.Append($"<span class='{cls}'>{ap}</span>");
                }
            }
            sb.Append("</div>");

            // البحث عن آخر لقطة شاشة
            var lastShot = Directory.Exists(screenshotsFolder)
                ? Directory.EnumerateFiles(screenshotsFolder, $"{childId}_*.jpg").OrderByDescending(f => f).FirstOrDefault()
                : null;

            if (lastShot != null)
            {
                string fileName = Path.GetFileName(lastShot);
                sb.Append($"<h3>📸 لقطة شاشة:</h3><img src='/screenshot/{fileName}' class='screenshot' />");
            }

            sb.Append("</div>");
        }
    }

    sb.Append("</body></html>");
    return sb.ToString();
}

// 10. نموذج البيانات
public class ReportData
{
    [JsonProperty("child_id")] public string ChildId { get; set; }
    [JsonProperty("timestamp")] public string Timestamp { get; set; }
    [JsonProperty("active_window")] public string ActiveWindow { get; set; }
    [JsonProperty("running_apps")] public List<string> RunningApps { get; set; }
    [JsonProperty("typed_text")] public string TypedText { get; set; }
    [JsonProperty("blocked_apps")] public List<string> BlockedApps { get; set; }
    [JsonProperty("ip")] public string IPAddress { get; set; }
}
