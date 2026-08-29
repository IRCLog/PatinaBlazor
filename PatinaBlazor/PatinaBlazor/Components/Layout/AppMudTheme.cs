using MudBlazor;

namespace PatinaBlazor.Components.Layout;

public static class AppMudTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteDark = new PaletteDark
        {
            Background = "#0d1117",
            BackgroundGrey = "#161b22",
            Surface = "#161b22",
            DrawerBackground = "#161b22",
            DrawerText = "#f0f6fc",
            AppbarBackground = "#161b22",
            AppbarText = "#f0f6fc",
            TextPrimary = "#f0f6fc",
            TextSecondary = "#8b949e",
            TextDisabled = "#6e7681",
            Primary = "#1f6feb",
            Secondary = "#8b949e",
            Success = "#3fb950",
            Error = "#f85149",
            Warning = "#d29922",
            Info = "#1f6feb",
            LinesDefault = "#30363d",
            LinesInputs = "#30363d",
            Divider = "#21262d",
            TableLines = "#30363d",
            ActionDefault = "#8b949e",
            ActionDisabled = "#6e7681",
        }
    };
}
