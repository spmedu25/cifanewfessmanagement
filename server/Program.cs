using System.Security.Claims;
using System.Text;
using CoachingApp.Api;
using CoachingApp.Components;
using CoachingApp.Data;
using CoachingApp.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddDbContextFactory<AppDb>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ChatNotifier>();
builder.Services.AddSingleton<PushService>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddScoped<FeeService>();
builder.Services.AddScoped<StudentService>();
builder.Services.AddScoped<IdCardService>();
builder.Services.AddScoped<FileStore>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddHttpContextAccessor();

var jwt = builder.Configuration.GetSection("Jwt");
// Web admin -> Cookie, Mobile app -> JWT Bearer (automatic select)
builder.Services.AddAuthentication(o => { o.DefaultScheme = "Smart"; o.DefaultChallengeScheme = "Smart"; })
    .AddPolicyScheme("Smart", "Smart", o => o.ForwardDefaultSelector = ctx =>
        ctx.Request.Headers.ContainsKey("Authorization") || ctx.Request.Query.ContainsKey("access_token")
            ? JwtBearerDefaults.AuthenticationScheme : CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login"; o.Cookie.Name = "coaching.auth"; o.ExpireTimeSpan = TimeSpan.FromDays(14);
        // /portal ke liye student login, baaki ke liye admin login
        o.Events.OnRedirectToLogin = c => { c.Response.Redirect(c.Request.Path.StartsWithSegments("/portal") ? "/portal/login" : "/login"); return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
    })
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"], ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!)),
            RoleClaimType = ClaimTypes.Role, NameClaimType = ClaimTypes.Name
        };
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = c => { if (c.Request.Query.TryGetValue("access_token", out var t)) c.Token = t; return Task.CompletedTask; }
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();
await Seed.RunAsync(app.Services);

if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error");
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapAccount();
app.MapPortal();
app.MapPrint();
app.MapApi();
app.MapChatApi();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
