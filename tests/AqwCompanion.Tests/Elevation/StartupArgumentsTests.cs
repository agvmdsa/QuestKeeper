using AqwCompanion.App.Infrastructure.Elevation;

namespace AqwCompanion.Tests.Elevation;

public class StartupArgumentsTests
{
    private const string Root = @"C:\Users\a b\AppData\Local\AqwCompanion";

    [Fact]
    public void Unquoted_value_is_found()
    {
        Assert.Equal(Root, StartupArguments.TryGetDataRoot(["--data-root=" + Root]));
    }

    [Fact]
    public void Quoted_value_loses_its_quotes()
    {
        Assert.Equal(Root, StartupArguments.TryGetDataRoot(["--data-root=\"" + Root + "\""]));
    }

    [Fact]
    public void Other_arguments_are_ignored()
    {
        Assert.Equal(Root, StartupArguments.TryGetDataRoot(["--verbose", "--data-root=" + Root, "--other=1"]));
    }

    [Fact]
    public void Absent_option_gives_null()
    {
        Assert.Null(StartupArguments.TryGetDataRoot([]));
        Assert.Null(StartupArguments.TryGetDataRoot(["--verbose", "--data-root"]));
    }

    [Theory]
    [InlineData("--data-root=")]
    [InlineData("--data-root=\"\"")]
    [InlineData("--data-root=\"")]
    [InlineData("--data-root=   ")]
    public void Empty_or_only_quote_values_give_null(string argument)
    {
        Assert.Null(StartupArguments.TryGetDataRoot([argument]));
    }

    [Theory]
    [InlineData(@"C:\Users\a b\AppData\Local\AqwCompanion")]
    [InlineData(@"C:\Users\plain\AqwCompanion")]
    [InlineData("C:\\odd\\na\"me\\root")]
    public void Relaunch_argument_round_trips(string path)
    {
        var argument = StartupArguments.ForRelaunch(path);

        Assert.StartsWith(StartupArguments.DataRootOption, argument, StringComparison.Ordinal);
        Assert.Equal(path, StartupArguments.TryGetDataRoot([argument]));
    }

    [Fact]
    public void Relaunch_argument_quotes_the_path()
    {
        Assert.Equal("--data-root=\"" + Root + "\"", StartupArguments.ForRelaunch(Root));
    }
}
