namespace Gooner.Core.Tests.Support;

// A test of something only Windows has - the registry - skipped anywhere else.
[AttributeUsage(AttributeTargets.Method)]
internal sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows only";
    }
}
