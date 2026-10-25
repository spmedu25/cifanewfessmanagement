using System.Security.Claims;
using CoachingApp.Data;
using CoachingApp.Services;
namespace CoachingApp.Api;

public record SendText(string? Text);
public record BroadcastReq(int? BatchId, int? CategoryId, string? Text);
public record DeviceReq(string Token, string? Platform);
public record RemindReq(int? BatchId, int? CategoryId);

public static class ChatEndpoints
{
    static object Dto(ChatMessage m) => new { m.Id, m.FromAdmin, m.Text, Attachment = m.AttachmentPath, m.SentAt, m.ReadAt };
    static IResult Err(string m) => Results.BadRequest(new { error = m });
    static int Sid(ClaimsPrincipal u) => int.Parse(u.FindFirstValue("sid")!);

    public static void MapChatApi(this WebApplication app)
    {
        // ---- FCM device token (admin + student dono) ----
        var dev = app.MapGroup("/api/device").RequireAuthorization();
        dev.MapPost("/register", async (DeviceReq r, ClaimsPrincipal u, PushService p) =>
        {
            if (string.IsNullOrWhiteSpace(r.Token) || r.Token.Length > 400) return Err("Invalid token");
            var admin = u.IsInRole("Admin");
            await p.RegisterAsync(r.Token, admin, admin ? null : Sid(u), r.Platform);
            return Results.Ok(new { ok = true });
        });
        dev.MapPost("/unregister", async (DeviceReq r, PushService p) => { await p.UnregisterAsync(r.Token); return Results.Ok(new { ok = true }); });

        // ---- Student chat ----
        var st = app.MapGroup("/api/student/chat").RequireAuthorization(p => p.RequireRole("Student"));
        st.MapGet("", async (ClaimsPrincipal u, int? afterId, ChatService c) =>
        {
            var t = await c.ThreadAsync(Sid(u), afterId ?? 0, false);
            return Results.Ok(new { messages = t.Messages.Select(Dto), otherReadUpTo = t.OtherReadUpTo });
        });
        st.MapGet("/unread", async (ClaimsPrincipal u, ChatService c) => Results.Ok(new { count = await c.UnreadForStudentAsync(Sid(u)) }));
        st.MapPost("", async (ClaimsPrincipal u, SendText r, ChatService c) =>
        {
            try { return Results.Ok(Dto(await c.SendAsync(Sid(u), false, r.Text, null))); }
            catch (InvalidOperationException e) { return Err(e.Message); }
        });
        st.MapPost("/image", async (ClaimsPrincipal u, IFormFile file, FileStore fs, ChatService c) =>
        {
            if (file.Length > 5_000_000) return Err("Image 5MB se choti rakhein");
            try
            {
                await using var s = file.OpenReadStream();
                var path = await fs.SaveAsync(s, file.FileName, "chat");
                return Results.Ok(Dto(await c.SendAsync(Sid(u), false, null, path)));
            }
            catch (InvalidOperationException e) { return Err(e.Message); }
        }).DisableAntiforgery();

        // ---- Admin chat ----
        var ad = app.MapGroup("/api/admin").RequireAuthorization(p => p.RequireRole("Admin"));
        ad.MapGet("/chats", async (ChatService c) => Results.Ok(await c.ConversationsAsync()));
        ad.MapGet("/chats/unread", async (ChatService c) => Results.Ok(new { count = await c.UnreadForAdminAsync() }));
        ad.MapGet("/chats/{studentId:int}", async (int studentId, int? afterId, ChatService c) =>
        {
            var t = await c.ThreadAsync(studentId, afterId ?? 0, true);
            if (t.Student == null) return Results.NotFound();
            return Results.Ok(new
            {
                messages = t.Messages.Select(Dto), otherReadUpTo = t.OtherReadUpTo,
                student = new { t.Student.Id, t.Student.Name, t.Student.RollNo, t.Student.PhotoPath, t.Student.Mobile }
            });
        });
        ad.MapPost("/chats/{studentId:int}", async (int studentId, SendText r, ChatService c) =>
        {
            try { return Results.Ok(Dto(await c.SendAsync(studentId, true, r.Text, null))); }
            catch (InvalidOperationException e) { return Err(e.Message); }
        });
        ad.MapPost("/chats/{studentId:int}/image", async (int studentId, IFormFile file, FileStore fs, ChatService c) =>
        {
            if (file.Length > 5_000_000) return Err("Image 5MB se choti rakhein");
            try
            {
                await using var s = file.OpenReadStream();
                var path = await fs.SaveAsync(s, file.FileName, "chat");
                return Results.Ok(Dto(await c.SendAsync(studentId, true, null, path)));
            }
            catch (InvalidOperationException e) { return Err(e.Message); }
        }).DisableAntiforgery();
        ad.MapPost("/broadcast", async (BroadcastReq r, ChatService c) =>
        {
            try { return Results.Ok(new { sent = await c.BroadcastAsync(r.CategoryId, r.BatchId, r.Text) }); }
            catch (InvalidOperationException e) { return Err(e.Message); }
        });
        ad.MapPost("/dues/remind", async (RemindReq r, ChatService c) => Results.Ok(new { sent = await c.RemindDuesAsync(r.CategoryId, r.BatchId) }));
    }
}
