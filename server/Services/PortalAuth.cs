using Microsoft.AspNetCore.Components.Authorization;
namespace CoachingApp.Services;

public static class PortalAuth
{
    // Student portal me logged-in student ki id (cookie claim "sid")
    public static async Task<int> StudentIdAsync(this AuthenticationStateProvider p)
    {
        var u = (await p.GetAuthenticationStateAsync()).User;
        return int.Parse(u.FindFirst("sid")!.Value);
    }
}
