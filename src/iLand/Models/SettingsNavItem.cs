namespace iLand.Models;

/// <summary>Satu item di menu samping Settings. IconKey = nama ikon StreamGeometry di Navigation.xaml (misalnya "IconIsland").</summary>
public sealed record SettingsNavItem(SettingsPage Page, string Title, string IconKey);
