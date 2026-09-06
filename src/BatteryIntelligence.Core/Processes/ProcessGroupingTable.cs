namespace BatteryIntelligence.Core.Processes;

/// <summary>
/// One rule mapping many processes onto a single application
/// (docs/monitoring-dataflow.md section 5; specification section 15 "intelligent
/// process grouping").
/// </summary>
/// <param name="ApplicationKey">The stable grouping key, e.g. "chrome".</param>
/// <param name="DisplayName">The name shown on the App Usage page.</param>
/// <param name="ProcessNames">Image names (no extension, case-insensitive) that belong to this application.</param>
/// <param name="PathFragments">Executable-path fragments (case-insensitive) that also map here — for helper processes that share a name with something else.</param>
public sealed record ProcessGroupRule(
    string ApplicationKey,
    string DisplayName,
    IReadOnlyList<string> ProcessNames,
    IReadOnlyList<string> PathFragments)
{
    /// <summary>A rule keyed only on one or more process names.</summary>
    public static ProcessGroupRule ByName(string applicationKey, string displayName, params string[] processNames) =>
        new(applicationKey, displayName, processNames, []);
}

/// <summary>
/// The data-driven process-grouping table. The built-in <see cref="Default"/> is
/// the starting point; the running application can replace or extend it from a
/// user-editable file without a rebuild (docs/roadmap.md Phase 7 deviations).
/// </summary>
/// <param name="Rules">Rules tried in order; the first match wins.</param>
public sealed record ProcessGroupingTable(IReadOnlyList<ProcessGroupRule> Rules)
{
    /// <summary>
    /// A conservative built-in table covering common multi-process applications
    /// (browsers especially, where renderer and GPU children must fold into the
    /// parent) and a handful of Windows system groupings.
    /// </summary>
    public static ProcessGroupingTable Default { get; } = new(
    [
        new ProcessGroupRule("chrome", "Google Chrome", ["chrome"], ["\\chrome\\", "\\google\\chrome\\"]),
        new ProcessGroupRule("msedge", "Microsoft Edge", ["msedge", "msedgewebview2"], ["\\microsoft\\edge\\"]),
        new ProcessGroupRule("firefox", "Mozilla Firefox", ["firefox"], ["\\mozilla firefox\\"]),
        new ProcessGroupRule("brave", "Brave", ["brave"], ["\\brave-browser\\"]),
        new ProcessGroupRule("opera", "Opera", ["opera", "opera_gx"], []),

        ProcessGroupRule.ByName("code", "Visual Studio Code", "code"),
        ProcessGroupRule.ByName("devenv", "Visual Studio", "devenv", "servicehub.host.dotnet.x64", "servicehub.host.clr.x64"),
        ProcessGroupRule.ByName("dotnet", ".NET host", "dotnet"),
        ProcessGroupRule.ByName("windowsterminal", "Windows Terminal", "windowsterminal", "openconsole"),

        ProcessGroupRule.ByName("teams", "Microsoft Teams", "ms-teams", "teams"),
        ProcessGroupRule.ByName("slack", "Slack", "slack"),
        ProcessGroupRule.ByName("discord", "Discord", "discord"),
        ProcessGroupRule.ByName("zoom", "Zoom", "zoom"),
        ProcessGroupRule.ByName("spotify", "Spotify", "spotify"),
        ProcessGroupRule.ByName("whatsapp", "WhatsApp", "whatsapp"),

        ProcessGroupRule.ByName("outlook", "Outlook", "outlook", "olk"),
        ProcessGroupRule.ByName("winword", "Word", "winword"),
        ProcessGroupRule.ByName("excel", "Excel", "excel"),
        ProcessGroupRule.ByName("powerpnt", "PowerPoint", "powerpnt"),

        ProcessGroupRule.ByName("explorer", "Windows Explorer", "explorer"),
        ProcessGroupRule.ByName("dwm", "Desktop Window Manager", "dwm"),
        ProcessGroupRule.ByName("windows-shell", "Windows Shell", "searchhost", "startmenuexperiencehost", "shellexperiencehost", "textinputhost", "runtimebroker"),
        ProcessGroupRule.ByName("windows-services", "Windows Services", "svchost"),
        ProcessGroupRule.ByName("antimalware", "Microsoft Defender", "msmpeng", "nissrv"),
        ProcessGroupRule.ByName("system", "System", "system", "registry", "memory compression", "wininit", "csrss", "smss", "services", "lsass"),
    ]);
}
