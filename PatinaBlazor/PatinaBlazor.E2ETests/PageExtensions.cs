using Microsoft.Playwright;

namespace PatinaBlazor.E2ETests
{
    public static class PageExtensions
    {
        public static async Task LoginAsync(this IPage page, string baseUrl, string email, string password)
        {
            await page.GotoAsync($"{baseUrl}/Account/Login");
            await page.GetByPlaceholder("name@example.com").FillAsync(email);
            await page.GetByPlaceholder("password").FillAsync(password);
            await page.GetByRole(AriaRole.Button, new() { Name = "Log in" }).ClickAsync();
            await page.WaitForURLAsync(url => !url.Contains("/Account/Login"), new() { Timeout = 10_000 });
        }
    }
}
