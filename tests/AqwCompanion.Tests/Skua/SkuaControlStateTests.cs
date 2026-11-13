using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaControlStateTests
{
    private DateTimeOffset _now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private SkuaControlState NewState() => new(() => _now);

    private void Wait(double seconds) => _now = _now.AddSeconds(seconds);

    private static string? StartError(SkuaControlState state) => state.CanStart(out var error) ? null : error;

    private SkuaControlState Running()
    {
        var state = NewState();
        state.OnStartAccepted(hasOptions: false);
        state.OnScriptRunningChanged(true);
        return state;
    }

    [Fact]
    public void A_new_state_is_idle_and_can_start()
    {
        var state = NewState();

        Assert.Equal("idle", state.State);
        Assert.Null(state.SettleSecondsLeft);
        Assert.False(state.OptionsStuck);
        Assert.True(state.CanStart(out var error));
        Assert.Equal("", error);
    }

    [Fact]
    public void A_start_without_options_runs()
    {
        var state = NewState();

        state.OnStartAccepted(hasOptions: false);

        Assert.Equal("running", state.State);
        Assert.Equal("already running", StartError(state));
    }

    [Fact]
    public void A_script_started_in_skua_itself_is_running()
    {
        var state = NewState();

        state.OnScriptRunningChanged(true);

        Assert.Equal("running", state.State);
        Assert.Equal("already running", StartError(state));
    }

    [Fact]
    public void Stop_goes_to_stopping_then_settling_then_idle_after_10_quiet_seconds()
    {
        var state = Running();

        state.OnStopRequested();
        Assert.Equal("stopping", state.State);
        Assert.Equal("stopping", StartError(state));
        Assert.Null(state.SettleSecondsLeft);

        Wait(4);
        state.OnScriptRunningChanged(false);
        Assert.Equal("settling", state.State);
        Assert.Equal(10, state.SettleSecondsLeft);
        Assert.Equal("settling", StartError(state));

        Wait(9.5);
        state.Tick();
        Assert.Equal("settling", state.State);
        Assert.Equal(1, state.SettleSecondsLeft);

        Wait(0.5);
        state.Tick();
        Assert.Equal("idle", state.State);
        Assert.Null(state.SettleSecondsLeft);
        Assert.True(state.CanStart(out _));
    }

    [Fact]
    public void Settling_ends_on_read_without_a_tick()
    {
        var state = Running();
        state.OnScriptRunningChanged(false);

        Wait(10);

        Assert.Equal("idle", state.State);
        Assert.True(state.CanStart(out _));
    }

    [Fact]
    public void A_script_that_ends_by_itself_settles_too()
    {
        var state = Running();

        state.OnScriptRunningChanged(false);

        Assert.Equal("settling", state.State);
        Assert.Equal("settling", StartError(state));
    }

    [Fact]
    public void A_map_change_while_settling_restarts_the_10_seconds()
    {
        var state = Running();
        state.OnStopRequested();
        state.OnScriptRunningChanged(false);

        Wait(8);
        state.OnMapChanged();
        Assert.Equal(10, state.SettleSecondsLeft);

        Wait(8);
        state.OnMapChanged();
        Wait(9);
        state.Tick();
        Assert.Equal("settling", state.State);
        Assert.Equal(1, state.SettleSecondsLeft);

        Wait(1);
        state.Tick();
        Assert.Equal("idle", state.State);
    }

    [Fact]
    public void A_map_change_outside_settling_changes_nothing()
    {
        var state = NewState();
        state.OnMapChanged();
        Assert.Equal("idle", state.State);

        var running = Running();
        running.OnMapChanged();
        Assert.Equal("running", running.State);
    }

    [Fact]
    public void A_stop_with_nothing_running_changes_nothing()
    {
        var state = NewState();

        state.OnStopRequested();

        Assert.Equal("idle", state.State);
    }

    [Fact]
    public void A_stop_while_settling_stays_settling()
    {
        var state = Running();
        state.OnScriptRunningChanged(false);

        state.OnStopRequested();

        Assert.Equal("settling", state.State);
    }

    [Fact]
    public void A_stop_before_skua_reported_the_script_running_settles_on_the_next_tick()
    {
        var state = NewState();
        state.OnStartAccepted(hasOptions: false);

        state.OnStopRequested();
        Assert.Equal("stopping", state.State);

        state.Tick();
        Assert.Equal("settling", state.State);
        Wait(10);
        Assert.Equal("idle", state.State);
    }

    [Fact]
    public void Stopping_waits_for_the_script_to_end_however_long_it_takes()
    {
        var state = Running();
        state.OnStopRequested();

        Wait(600);
        state.Tick();

        Assert.Equal("stopping", state.State);
    }

    [Fact]
    public void A_script_started_in_skua_while_settling_is_running()
    {
        var state = Running();
        state.OnScriptRunningChanged(false);

        state.OnScriptRunningChanged(true);

        Assert.Equal("running", state.State);
        Assert.Null(state.SettleSecondsLeft);
    }

    [Fact]
    public void A_start_with_options_awaits_the_options_until_the_dialog_closes()
    {
        var state = NewState();

        state.OnStartAccepted(hasOptions: true);
        state.OnScriptRunningChanged(true);
        Assert.Equal("awaitingOptions", state.State);
        Assert.Equal("already running", StartError(state));

        Wait(3600);
        state.Tick();
        Assert.Equal("awaitingOptions", state.State);

        state.OnOptionsDialogClosed();
        Assert.Equal("running", state.State);
    }

    [Fact]
    public void Awaiting_options_ends_when_the_script_is_seen_alive()
    {
        var state = NewState();
        state.OnStartAccepted(hasOptions: true);

        state.OnScriptAlive();

        Assert.Equal("running", state.State);
    }

    [Fact]
    public void Stuck_options_are_reported_until_awaiting_options_ends()
    {
        var state = NewState();
        state.OnStartAccepted(hasOptions: true);

        state.OnOptionsStuck();
        Assert.True(state.OptionsStuck);
        Assert.Equal("awaitingOptions", state.State);

        Wait(3600);
        state.Tick();
        Assert.True(state.OptionsStuck);

        state.OnOptionsDialogClosed();
        Assert.False(state.OptionsStuck);
        Assert.Equal("running", state.State);
    }

    [Fact]
    public void Stuck_options_clear_when_the_script_is_seen_alive_or_stopped()
    {
        var alive = NewState();
        alive.OnStartAccepted(hasOptions: true);
        alive.OnOptionsStuck();
        alive.OnScriptAlive();
        Assert.False(alive.OptionsStuck);

        var stopped = NewState();
        stopped.OnStartAccepted(hasOptions: true);
        stopped.OnScriptRunningChanged(true);
        stopped.OnOptionsStuck();
        stopped.OnStopRequested();
        Assert.Equal("stopping", stopped.State);
        Assert.False(stopped.OptionsStuck);
    }

    [Fact]
    public void Options_events_outside_awaiting_options_change_nothing()
    {
        var state = NewState();
        state.OnOptionsDialogClosed();
        state.OnScriptAlive();
        state.OnOptionsStuck();
        Assert.Equal("idle", state.State);
        Assert.False(state.OptionsStuck);

        var settling = Running();
        settling.OnScriptRunningChanged(false);
        settling.OnOptionsDialogClosed();
        settling.OnScriptAlive();
        Assert.Equal("settling", settling.State);
    }

    [Fact]
    public void A_script_that_ends_while_awaiting_options_settles()
    {
        var state = NewState();
        state.OnStartAccepted(hasOptions: true);
        state.OnScriptRunningChanged(true);
        state.OnOptionsStuck();

        state.OnScriptRunningChanged(false);

        Assert.Equal("settling", state.State);
        Assert.False(state.OptionsStuck);
    }

    [Fact]
    public void A_stop_while_awaiting_options_goes_to_stopping()
    {
        var state = NewState();
        state.OnStartAccepted(hasOptions: true);

        state.OnStopRequested();

        Assert.Equal("stopping", state.State);
    }

    [Fact]
    public void A_repeated_running_report_changes_nothing()
    {
        var state = Running();
        state.OnStopRequested();

        state.OnScriptRunningChanged(true);
        Assert.Equal("stopping", state.State);

        state.OnScriptRunningChanged(false);
        Wait(5);
        state.OnScriptRunningChanged(false);
        Assert.Equal(5, state.SettleSecondsLeft);
    }

    [Fact]
    public void The_state_is_always_a_protocol_control_value()
    {
        var state = Running();
        var seen = new List<string> { state.State };
        state.OnStopRequested();
        seen.Add(state.State);
        state.OnScriptRunningChanged(false);
        seen.Add(state.State);
        Wait(10);
        seen.Add(state.State);
        state.OnStartAccepted(hasOptions: true);
        seen.Add(state.State);

        Assert.All(seen, s => Assert.Contains(s, SkuaProtocol.ControlStates));
        Assert.Equal(SkuaProtocol.ControlStates.Order(), seen.Order());
    }
}
