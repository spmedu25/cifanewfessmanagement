using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using CoachingApp.Data;
using Microsoft.EntityFrameworkCore;
namespace CoachingApp.Services;

// ID card: 9 cm (width) x 6 cm (height), front + back (Fees Card) - aapke diye hue design jaisa
public class IdCardService(IDbContextFactory<AppDb> f)
{
    static string E(string? v) => WebUtility.HtmlEncode(v ?? "");
    static string Color(string? c, string d) => c != null && Regex.IsMatch(c, "^#[0-9a-fA-F]{6}$") ? c : d;

    const string Css = @"
*{box-sizing:border-box}
body{margin:0;background:#e9ecef;font-family:'Segoe UI',Arial,sans-serif}
.bar{padding:10px 14px;background:#212529;color:#fff;display:flex;gap:12px;align-items:center;font-size:14px}
.bar a{color:#9ec5fe}.bar button{padding:6px 18px;cursor:pointer}
.sheet{display:grid;grid-template-columns:repeat(2,90mm);width:180mm;margin:10px auto}
.sheet.single{display:block;width:90mm}
.card{width:90mm;height:60mm;position:relative;overflow:hidden;padding:2.2mm;border:.35mm solid #111;
 background:linear-gradient(180deg,var(--c1),var(--c2));-webkit-print-color-adjust:exact;print-color-adjust:exact;break-inside:avoid}
.in{background:#fff;height:100%;border-radius:2.2mm;position:relative;overflow:hidden}
.head{height:16.5mm;display:flex;align-items:center;gap:2mm;padding:0 2.5mm;border-bottom:.35mm solid #333}
.head img{width:12mm;height:12mm;object-fit:contain}
.ht{flex:1;text-align:center;line-height:1.15}
.nm{font-weight:800;font-size:3.6mm;color:#222}
.tg{font-weight:700;font-size:3mm}
.ph{font-size:2.6mm;font-weight:700;color:#1a4f9c}
.web{font-size:2.5mm;color:#1a73c9;font-weight:600}
.body{position:relative;height:calc(100% - 16.5mm);padding:1.5mm 2.5mm}
.ln{font-size:2.65mm;line-height:4.2mm;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:61mm}
.ln.wide{max-width:none}.ln b{font-weight:700}
.photo{position:absolute;right:2.5mm;top:1.5mm;width:17mm;height:21mm;border-radius:2mm;border:.2mm solid #999;overflow:hidden;background:#eee}
.photo img{width:100%;height:100%;object-fit:cover}
.stamp{position:absolute;right:14mm;bottom:.5mm;width:16mm;opacity:.85}.stamp img{width:100%}
.sign{position:absolute;right:2mm;bottom:.5mm;width:15mm;text-align:center;font-size:2.4mm;font-weight:700}
.sign img{height:5mm;display:block;margin:0 auto}
.back .in{padding:2.5mm;display:flex;gap:2mm}
.pill{writing-mode:vertical-rl;transform:rotate(180deg);background:#222;color:#fff;font-weight:800;border-radius:3mm;padding:2mm 1.4mm;font-size:3.4mm;letter-spacing:.3mm;display:flex;align-items:center;justify-content:center}
.fc{flex:1;display:flex;flex-direction:column;justify-content:space-between}
.ft{border-collapse:collapse;font-size:2.5mm;width:100%;text-align:center}
.ft td,.ft th{border:.3mm solid #222;padding:0 .8mm;height:5.2mm;font-weight:700}
.ft th{height:8mm;line-height:1.1}
.note{font-size:2.5mm;font-weight:600}
@media print{.no-print{display:none!important}body{background:#fff}}";

    static string Front(Student s, InstituteSettings st)
    {
        var logo = st.LogoPath != null ? $"<img src='{E(st.LogoPath)}'/>" : "";
        var photo = s.PhotoPath != null ? $"<img src='{E(s.PhotoPath)}'/>"
            : "<svg viewBox='0 0 24 24' style='width:100%;height:100%;fill:#bbb'><circle cx='12' cy='8' r='4'/><path d='M4 21c0-5 4-8 8-8s8 3 8 8z'/></svg>";
        var stamp = st.StampPath != null ? $"<div class='stamp'><img src='{E(st.StampPath)}'/></div>" : "";
        var sign = st.SignPath != null ? $"<img src='{E(st.SignPath)}'/>" : "<div style='height:5mm'></div>";
        var dur = s.Batch.DurationMonths + " Month";
        return $@"<div class='card front'><div class='in'>
<div class='head'>{logo}<div class='ht'><div class='nm'>{E(st.Name)}</div>{(string.IsNullOrWhiteSpace(st.Tagline) ? "" : $"<div class='tg'>{E(st.Tagline)}</div>")}
<div class='ph'>&#9990; {E(st.Phones)}</div><div class='web'>{E(st.Website)}</div></div></div>
<div class='body'>
<div class='photo'>{photo}</div>
<div class='ln'><b>Roll No.</b> {s.RollNo}</div>
<div class='ln'><b>Name:-</b> {E(s.Name.ToUpper())}</div>
<div class='ln'><b>Father’s Name:-</b> {E(s.FatherName.ToUpper())}</div>
<div class='ln'><b>Address:-</b> {E(s.Address.ToUpper())}</div>
<div class='ln'><b>Mob. No.</b> {E(s.Mobile)}</div>
<div class='ln wide'><b>Date of Admission:-</b> {s.AdmissionDate:dd-MM-yyyy}</div>
<div class='ln wide'><b>Course:</b> {E(s.Batch.Course.ToUpper())} &nbsp;&nbsp; <b>Duration:-</b> {E(dur)}</div>
{stamp}<div class='sign'>{sign}Auth.Sign.</div>
</div></div></div>";
    }

    static string Back(InstituteSettings st)
    {
        string[] a = { "JAN", "FEB", "MAR", "APR", "MAY", "JUN" }, b = { "JUL", "AUG", "SEPT", "OCT", "NOV", "DEC" };
        var rows = new StringBuilder();
        for (int i = 0; i < 6; i++) rows.Append($"<tr><td>{a[i]}</td><td></td><td>{b[i]}</td><td></td></tr>");
        return $@"<div class='card back'><div class='in'><div class='pill'>FEES CARD</div><div class='fc'>
<table class='ft'><tr><th>Months</th><th>Teacher’s sign.<br/>with Date</th><th>Month</th><th>Teacher’s sign.<br/>with Date</th></tr>{rows}</table>
<div class='note'>*{E(st.FeeCardNote)}*</div></div></div></div>";
    }

    // side: front | back | both
    public async Task<string> RenderAsync(IEnumerable<int> ids, bool sheet, string side, bool embed)
    {
        await using var db = await f.CreateDbContextAsync();
        var st = await db.Settings.FirstAsync();
        var idList = ids.ToList();
        var studs = await db.Students.Include(s => s.Batch).Where(s => idList.Contains(s.Id)).OrderBy(s => s.RollNo).ToListAsync();
        var fronts = studs.Select(s => Front(s, st)).ToList();
        var back = Back(st);
        var sb = new StringBuilder();
        sb.Append($"<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>ID Cards</title><style>:root{{--c1:{Color(st.CardColor1, "#4aa3df")};--c2:{Color(st.CardColor2, "#7fd1c0")}}}{Css}");
        sb.Append(sheet ? "@page{size:A4;margin:10mm 15mm}.sheet{page-break-after:always}" : "@page{size:90mm 60mm;margin:0}.card{page-break-after:always}.sheet.single{margin:0}");
        sb.Append("</style></head><body>");
        if (!embed)
            sb.Append($"<div class='bar no-print'><button onclick='window.print()'>🖨 Print</button><span>{studs.Count} card(s) • 9cm × 6cm</span>" +
                      "<a href='?side=front'>Front</a><a href='?side=back'>Back</a><a href='?side=both'>Both</a></div>");
        bool wantF = side != "back", wantB = side != "front";
        if (sheet)
        {
            if (wantF) foreach (var chunk in fronts.Chunk(8)) sb.Append("<div class='sheet'>" + string.Concat(chunk) + "</div>");
            if (wantB) foreach (var chunk in fronts.Chunk(8)) sb.Append("<div class='sheet'>" + string.Concat(chunk.Select(_ => back)) + "</div>");
        }
        else
        {
            sb.Append("<div class='sheet single'>");
            foreach (var fr in fronts) { if (wantF) sb.Append(fr); if (wantB) sb.Append(back); }
            sb.Append("</div>");
        }
        return sb.Append("</body></html>").ToString();
    }

    public async Task<string> RenderBatchAsync(int batchId, string side)
    {
        await using var db = await f.CreateDbContextAsync();
        var ids = await db.Students.Where(s => s.BatchId == batchId && s.Status == StudentStatus.Active).Select(s => s.Id).ToListAsync();
        return await RenderAsync(ids, true, side, false);
    }

    public async Task<string?> ReceiptAsync(int paymentId)
    {
        await using var db = await f.CreateDbContextAsync();
        var st = await db.Settings.FirstAsync();
        var p = await db.Payments.Include(x => x.Student).ThenInclude(s => s.Batch)
            .Include(x => x.Allocations).ThenInclude(a => a.Installment).FirstOrDefaultAsync(x => x.Id == paymentId);
        if (p == null) return null;
        var bal = await db.FeeInstallments.Where(i => i.StudentId == p.StudentId).SumAsync(i => (decimal?)(i.Amount - i.Discount - i.Waived - i.PaidAmount)) ?? 0;
        var lines = string.Concat(p.Allocations.OrderBy(a => a.Installment.DueDate)
            .Select(a => $"<tr><td>{E(a.Installment.Title)}</td><td style='text-align:right'>₹{a.Amount:N2}</td></tr>"));
        return $@"<!doctype html><html><head><meta charset='utf-8'><title>Receipt {E(p.ReceiptNo)}</title>
<style>body{{font-family:Segoe UI,Arial;max-width:420px;margin:20px auto;padding:16px;border:1px solid #333}}h2,h4{{text-align:center;margin:2px}}
table{{width:100%;border-collapse:collapse;margin-top:8px}}td{{padding:4px;border-bottom:1px dashed #aaa}}@media print{{.np{{display:none}}}}</style></head><body>
<h2>{E(st.Name)}</h2><h4>{E(st.Phones)}</h4><hr/>
<b>Receipt:</b> {E(p.ReceiptNo)}{(p.IsCancelled ? " <span style='color:red'>(CANCELLED)</span>" : "")}<br/><b>Date:</b> {p.PaidOn:dd-MM-yyyy hh:mm tt}<br/>
<b>Roll No:</b> {p.Student.RollNo}<br/><b>Name:</b> {E(p.Student.Name)}<br/><b>Course:</b> {E(p.Student.Batch.Course)} ({E(p.Student.Batch.Name)})
<table>{lines}<tr><td><b>Total Paid ({E(p.Mode)})</b></td><td style='text-align:right'><b>₹{p.Amount:N2}</b></td></tr></table>
<p>Remaining balance (poore course ka): <b>₹{bal:N2}</b></p><p style='text-align:right'>Received by: {E(p.ReceivedBy)}</p>
<button class='np' onclick='window.print()'>Print</button></body></html>";
    }
}
