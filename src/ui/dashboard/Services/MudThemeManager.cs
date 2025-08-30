using Microsoft.JSInterop;
using MudBlazor;

namespace Dashboard.Services;

public class MudThemeManager
{
    private readonly IJSRuntime _js;
    private const string StorageKey = "aegis-theme";

    public MudThemeManager(IJSRuntime js)
    {
        _js = js;

        LightTheme = new MudTheme
        {
            PaletteLight = new PaletteLight
            {
                Primary = Colors.Blue.Lighten1,
                Secondary = Colors.Pink.Accent2
            }
        };

        DarkTheme = new MudTheme
        {
            PaletteDark = new PaletteDark
            {
                Primary = Colors.Blue.Lighten1,
                Secondary = Colors.Pink.Accent2
            }
        };

        CurrentTheme = DarkTheme;
        IsDarkMode = true;
    }

    public MudTheme LightTheme { get; }
    public MudTheme DarkTheme { get; }
    public MudTheme CurrentTheme { get; private set; }
    public bool IsDarkMode { get; private set; }

    public async Task InitializeAsync()
    {
        var mode = await _js.InvokeAsync<string>("localStorage.getItem", StorageKey);
        if (mode == "light")
        {
            IsDarkMode = false;
            CurrentTheme = LightTheme;
        }
        else if (mode == "dark")
        {
            IsDarkMode = true;
            CurrentTheme = DarkTheme;
        }
    }

    public async Task ToggleThemeAsync()
    {
        IsDarkMode = !IsDarkMode;
        CurrentTheme = IsDarkMode ? DarkTheme : LightTheme;
        await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, IsDarkMode ? "dark" : "light");
    }

    public void SetCustomTheme(MudTheme theme)
    {
        CurrentTheme = theme;
    }
}

