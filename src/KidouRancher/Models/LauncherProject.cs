namespace KidouRancher.Models;

public sealed class LauncherProject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public List<string> Folders { get; set; } = [];

    public override string ToString() => Name;
}

public sealed class LauncherSettings
{
    public List<LauncherProject> Projects { get; set; } = [];
}