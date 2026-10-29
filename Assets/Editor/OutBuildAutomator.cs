#if UNITY_EDITOR
/// <summary>Compatibility entry points. Standalone implementation: Editor/BuildAutomator.</summary>
public static class OutBuildAutomator
{
    public static void BuildOnly() { OutBuild.BuildDashboard.OpenBuild(); }
    public static void BuildAndRun() { OutBuild.BuildDashboard.OpenRun(); }
    public static string GetAdbPath()
    {
        var state = OutBuild.BuildStorage.MachineStore.Read();
        return OutBuild.BuildStorage.FindAdb(state == null ? "" : state.adbOverride);
    }
    public static void BuildFromCommandLine() { OutBuild.BuildJobs.BuildFromCommandLine(); }
}
#endif
