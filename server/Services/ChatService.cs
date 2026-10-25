using CoachingApp.Data;
using Microsoft.EntityFrameworkCore;
namespace CoachingApp.Services;

public record ChatEvent(int StudentId, ChatMessage? Message, string? StudentName);
public record ChatThread(List<ChatMessage> Messages, int OtherReadUpTo, Student? Student);
public record ConversationRow(int StudentId, int RollNo, string Name, string? Photo, string Batch, string? LastText, DateTime LastAt, bool LastFromAdmin, int Unread);

// Web admin pages isse live updates lete hain (Blazor Server, same process)
public class ChatNotifier
{
    public event Action<ChatEvent>? Changed;
    public void Publish(ChatEvent e)
    {
        var h = Changed; if (h == null) return;
        foreach (var d in h.GetInvocationList()) { try { ((Action<ChatEvent>)d)(e); } catch { } }
    }
}

public class ChatService(IDbContextFactory<AppDb> f, ChatNotifier n, PushService push, FeeService fees)
{
    public async Task<ChatMessage> SendAsync(int studentId, bool fromAdmin, string? text, string? attachment)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) && attachment == null) throw new InvalidOperationException("Message khali hai");
        if (text != null && text.Length > 2000) throw new InvalidOperationException("Message bahut lamba hai (max 2000 characters)");
        await using var db = await f.CreateDbContextAsync();
        var s = await db.Students.FindAsync(studentId) ?? throw new InvalidOperationException("Student nahi mila");
        var m = new ChatMessage { StudentId = studentId, FromAdmin = fromAdmin, Text = text, AttachmentPath = attachment, SentAt = DateTime.Now };
        db.ChatMessages.Add(m);
        await db.SaveChangesAsync();
        n.Publish(new ChatEvent(studentId, m, s.Name));
        var body = string.IsNullOrEmpty(text) ? "📷 Photo" : text;
        var data = new Dictionary<string, string> { { "type", "chat" } };
        if (fromAdmin)
        {
            var inst = await db.Settings.Select(x => x.Name).FirstAsync();
            _ = push.ToStudentsAsync(new[] { (studentId, body) }, inst, data);
        }
        else
        {
            data["studentId"] = studentId.ToString();
            _ = push.ToAdminsAsync(s.Name, body, data);
        }
        return m;
    }

    // Thread kholne par samne wale ke messages "read" mark ho jate hain (blue tick)
    public async Task<ChatThread> ThreadAsync(int studentId, int afterId, bool viewerIsAdmin)
    {
        await using var db = await f.CreateDbContextAsync();
        List<ChatMessage> list;
        if (afterId <= 0)
        {
            list = await db.ChatMessages.Where(m => m.StudentId == studentId).OrderByDescending(m => m.Id).Take(100).ToListAsync();
            list.Reverse();
        }
        else list = await db.ChatMessages.Where(m => m.StudentId == studentId && m.Id > afterId).OrderBy(m => m.Id).Take(200).ToListAsync();
        var updated = await db.ChatMessages.Where(m => m.StudentId == studentId && m.FromAdmin != viewerIsAdmin && m.ReadAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(m => m.ReadAt, DateTime.Now));
        if (updated > 0) n.Publish(new ChatEvent(studentId, null, null));
        var readUp = await db.ChatMessages.Where(m => m.StudentId == studentId && m.FromAdmin == viewerIsAdmin && m.ReadAt != null)
            .MaxAsync(m => (int?)m.Id) ?? 0;
        var st = viewerIsAdmin ? await db.Students.FindAsync(studentId) : null;
        return new ChatThread(list, readUp, st);
    }

    public async Task<List<ConversationRow>> ConversationsAsync()
    {
        await using var db = await f.CreateDbContextAsync();
        var g = await db.ChatMessages.GroupBy(m => m.StudentId).Select(x => new
        { StudentId = x.Key, LastId = x.Max(m => m.Id), Unread = x.Count(m => !m.FromAdmin && m.ReadAt == null) }).ToListAsync();
        var lastIds = g.Select(x => x.LastId).ToList(); var ids = g.Select(x => x.StudentId).ToList();
        var last = await db.ChatMessages.Where(m => lastIds.Contains(m.Id)).ToDictionaryAsync(m => m.StudentId);
        var studs = await db.Students.Include(s => s.Batch).Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
        return g.Select(x =>
        {
            var m = last[x.StudentId]; var s = studs[x.StudentId];
            return new ConversationRow(s.Id, s.RollNo, s.Name, s.PhotoPath, s.Batch.Name, m.Text ?? "📷 Photo", m.SentAt, m.FromAdmin, x.Unread);
        }).OrderByDescending(r => r.LastAt).ToList();
    }

    public async Task<int> UnreadForAdminAsync()
    {
        await using var db = await f.CreateDbContextAsync();
        return await db.ChatMessages.CountAsync(m => !m.FromAdmin && m.ReadAt == null);
    }

    public async Task<int> UnreadForStudentAsync(int studentId)
    {
        await using var db = await f.CreateDbContextAsync();
        return await db.ChatMessages.CountAsync(m => m.StudentId == studentId && m.FromAdmin && m.ReadAt == null);
    }

    // Announcement: sabhi / category / batch ke active students ko message + push
    public async Task<int> BroadcastAsync(int? categoryId, int? batchId, string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) throw new InvalidOperationException("Message likhein");
        await using var db = await f.CreateDbContextAsync();
        var q = db.Students.Where(s => s.Status == StudentStatus.Active);
        if (batchId > 0) q = q.Where(s => s.BatchId == batchId);
        else if (categoryId > 0) q = q.Where(s => s.Batch.CategoryId == categoryId);
        var ids = await q.Select(s => s.Id).ToListAsync();
        if (ids.Count == 0) return 0;
        var body = "📢 " + text; var now = DateTime.Now;
        db.ChatMessages.AddRange(ids.Select(id => new ChatMessage { StudentId = id, FromAdmin = true, Text = body, SentAt = now }));
        await db.SaveChangesAsync();
        var inst = await db.Settings.Select(x => x.Name).FirstAsync();
        _ = push.ToStudentsAsync(ids.Select(id => (id, body)), inst, new Dictionary<string, string> { { "type", "chat" } });
        n.Publish(new ChatEvent(0, null, null));
        return ids.Count;
    }

    // Jinki fees due hai un sabko personal reminder (chat + push)
    public async Task<int> RemindDuesAsync(int? categoryId, int? batchId)
    {
        var rows = await fees.DuesAsync(categoryId, batchId, DateTime.Today);
        if (rows.Count == 0) return 0;
        var items = rows.Select(r => (Id: r.StudentId, Body: $"Dear {r.Name}, aapki ₹{r.Due:N0} fees pending hai ({r.Months} month). Kripya jaldi jama karein.")).ToList();
        var now = DateTime.Now;
        await using var db = await f.CreateDbContextAsync();
        db.ChatMessages.AddRange(items.Select(i => new ChatMessage { StudentId = i.Id, FromAdmin = true, Text = i.Body, SentAt = now }));
        await db.SaveChangesAsync();
        _ = push.ToStudentsAsync(items, "Fees reminder", new Dictionary<string, string> { { "type", "chat" } });
        n.Publish(new ChatEvent(0, null, null));
        return items.Count;
    }
}
