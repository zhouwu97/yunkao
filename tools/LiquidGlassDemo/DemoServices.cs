namespace YunKao;

// The native surface needs only a diagnostic sink and a material status sink. It does not
// initialize the application's database, account store, network clients or task services.
internal static class App
{
    internal static DemoServices Services { get; } = new();
}

internal sealed class DemoServices
{
    public DemoDiagnostics Diagnostics { get; } = new();
    public DemoWorkspace Workspace { get; } = new();
}

internal sealed class DemoDiagnostics
{
    public void Warning(string message) => File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "native-glass.log"), message + "\n");
}

internal sealed class DemoWorkspace
{
    public string NavigationMaterial { get; set; } = "";
}
