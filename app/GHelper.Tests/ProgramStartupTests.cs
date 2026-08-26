using System.Reflection;

namespace GHelper.Tests;

public sealed class ProgramStartupTests
{
    [Fact]
    public void MainUsesStaThreadForWindowsShellDialogs()
    {
        MethodInfo? main = typeof(Program).GetMethod(nameof(Program.Main), BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(main);
        Assert.NotNull(main.GetCustomAttribute<STAThreadAttribute>());
    }
}
