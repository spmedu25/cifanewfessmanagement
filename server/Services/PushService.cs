using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CoachingApp.Data;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
namespace CoachingApp.Services;

public record PushStatus(bool Configured, string? ProjectId, string? Error, int AdminDevices, int StudentDevices);

// Firebase Cloud Messaging (HTTP v1). Fcm:ServiceAccountPath set nahi hai to push chupchap band rehta hai, baaki app chalta rahta hai.
public class PushService(IDbContextFactory<AppDb> f, IHttpClientFactory hf, IConfiguration cfg, ILogger<PushService> log)
{
    GoogleCredential? cred; string? projectId, lastError; bool warned; readonly object lk = new();

    bool Init()
    {
        lock (lk)
        {
            if (cred != null) return true;
            try
            {
                var path = cfg["Fcm:ServiceAccountPath"];
                if (string.IsNullOrWhiteSpace(path)) { lastError = "appsettings me Fcm:ServiceAccountPath khali hai"; Warn(); return false; }
                if (!File.Exists(path)) { lastError = $"Key file nahi mili ya IIS user ko read permission nahi: {path}"; Warn(); return false; }
                cred = GoogleCredential.FromFile(path).CreateScoped("https://www.googleapis.com/auth/firebase.messaging");
                projectId = cfg["Fcm:ProjectId"];
                if (string.IsNullOrWhiteSpace(projectId))
                    projectId = JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("project_id").GetString();
                lastError = null;
                return true;
            }
            catch (Exception e) { lastError = "Key file padhne me error: " + e.Message; log.LogError(e, "FCM init fail"); cred = null; return false; }
        }
    }
    void Warn() { if (!warned) { warned = true; log.LogWarning("FCM band hai: {Err}", lastError); } }

    public async Task RegisterAsync(string token, bool isAdmin, int? studentId, string? platform)
    {
        await using var db = await f.CreateDbContextAsync();
        await db.DeviceTokens.Where(t => t.Token == token).ExecuteDeleteAsync();
        db.DeviceTokens.Add(new DeviceToken { Token = token, IsAdmin = isAdmin, StudentId = studentId, Platform = platform, UpdatedAt = DateTime.Now });
        await db.SaveChangesAsync();
    }

    public async Task UnregisterAsync(string token)
    {
        await using var db = await f.CreateDbContextAsync();
        await db.DeviceTokens.Where(t => t.Token == token).ExecuteDeleteAsync();
    }

    // ---- Diagnostics (Settings page) ----
    public async Task<PushStatus> StatusAsync()
    {
        var ok = Init();
        await using var db = await f.CreateDbContextAsync();
        var a = await db.DeviceTokens.CountAsync(t => t.IsAdmin);
        var s = await db.DeviceTokens.CountAsync(t => !t.IsAdmin);
        return new PushStatus(ok, projectId, ok ? null : lastError, a, s);
    }

    public async Task<List<string>> TestAdminsAsync()
    {
        var res = new List<string>();
        if (!Init()) { res.Add("❌ FCM configure nahi hai: " + lastError); return res; }
        await using var db = await f.CreateDbContextAsync();
        var toks = await db.DeviceTokens.Where(t => t.IsAdmin).Select(t => t.Token).ToListAsync();
        if (toks.Count == 0) { res.Add("❌ Koi admin device registered nahi. Admin app me logout karke dobara login karein (Firebase configure hone ke baad)."); return res; }
        string access;
        try { access = await ((ITokenAccess)cred!).GetAccessTokenForRequestAsync(); }
        catch (Exception e) { res.Add("❌ Google token nahi mila (key galat/internet?): " + e.Message); return res; }
        foreach (var t in toks)
        {
            var r = await SendOneAsync(access, t, "Test push ✔", "Ye admin devices ka test notification hai", new Dictionary<string, string> { { "type", "test" } });
            res.Add($"{(r == "OK" ? "✅" : "❌")} device …{t[^8..]} → {r}");
        }
        return res;
    }

    // Har student ko apni alag body ke saath push (broadcast me sabki body same)
    public async Task ToStudentsAsync(IEnumerable<(int Id, string Body)> items, string title, Dictionary<string, string>? data = null)
    {
        try
        {
            if (!Init()) return;
            var list = items.ToList();
            var ids = list.Select(i => i.Id).ToList();
            await using var db = await f.CreateDbContextAsync();
            var toks = await db.DeviceTokens.Where(t => t.StudentId != null && ids.Contains(t.StudentId.Value))
                .Select(t => new { Sid = t.StudentId!.Value, t.Token }).ToListAsync();
            if (toks.Count == 0) log.LogInformation("Push: in students ke koi device registered nahi ({Count} students)", ids.Count);
            var bodyMap = list.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First().Body);
            var msgs = toks.Select(t =>
            {
                var d = new Dictionary<string, string>(data ?? new Dictionary<string, string>()) { ["studentId"] = t.Sid.ToString() };
                return (t.Token, title, bodyMap[t.Sid], d);
            }).ToList();
            await SendAllAsync(msgs);
        }
        catch (Exception e) { log.LogError(e, "Student push fail"); }
    }

    public async Task ToAdminsAsync(string title, string body, Dictionary<string, string>? data = null)
    {
        try
        {
            if (!Init()) return;
            await using var db = await f.CreateDbContextAsync();
            var toks = await db.DeviceTokens.Where(t => t.IsAdmin).Select(t => t.Token).ToListAsync();
            var d = data ?? new Dictionary<string, string>();
            await SendAllAsync(toks.Select(t => (t, title, body, d)).ToList());
        }
        catch (Exception e) { log.LogError(e, "Admin push fail"); }
    }

    async Task SendAllAsync(List<(string Token, string Title, string Body, Dictionary<string, string> Data)> msgs)
    {
        if (msgs.Count == 0) return;
        var access = await ((ITokenAccess)cred!).GetAccessTokenForRequestAsync();
        var sem = new SemaphoreSlim(10);
        await Task.WhenAll(msgs.Select(async m =>
        {
            await sem.WaitAsync();
            try { var r = await SendOneAsync(access, m.Token, m.Title, m.Body, m.Data); if (r != "OK") log.LogWarning("FCM: {R}", r); }
            catch (Exception e) { log.LogError(e, "FCM send fail"); }
            finally { sem.Release(); }
        }));
    }

    // "OK" ya error detail wapas deta hai
    async Task<string> SendOneAsync(string access, string token, string title, string body, Dictionary<string, string> data)
    {
        var payload = new
        {
            message = new
            {
                token,
                notification = new { title, body },
                data,
                android = new { priority = "HIGH", notification = new { channel_id = "coaching_chat", sound = "default" } },
                apns = new { payload = new { aps = new { sound = "default" } } }
            }
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send")
        { Content = JsonContent.Create(payload) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var res = await hf.CreateClient().SendAsync(req);
        if (res.IsSuccessStatusCode) return "OK";
        var txt = await res.Content.ReadAsStringAsync();
        if ((int)res.StatusCode == 404 || txt.Contains("UNREGISTERED"))
        {
            await using var db = await f.CreateDbContextAsync();   // uninstall ho chuka device - token hata do
            await db.DeviceTokens.Where(t => t.Token == token).ExecuteDeleteAsync();
            return "404 UNREGISTERED (purana token, hata diya - app me dobara login karein)";
        }
        return $"{(int)res.StatusCode} {(txt.Length > 220 ? txt[..220] : txt)}";
    }
}
