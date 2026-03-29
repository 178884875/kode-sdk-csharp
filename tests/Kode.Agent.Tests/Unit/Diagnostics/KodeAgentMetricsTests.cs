using System.Diagnostics.Metrics;
using FluentAssertions;
using Kode.Agent.Sdk.Diagnostics;
using Xunit;

namespace Kode.Agent.Tests.Unit.Diagnostics;

public sealed class KodeAgentMetricsTests : IDisposable
{
    private readonly MeterListener _listener;
    private readonly List<(string Name, object Value, KeyValuePair<string, object?>[] Tags)> _measurements = [];

    public KodeAgentMetricsTests()
    {
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == KodeAgentMetrics.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>(CollectLong);
        _listener.SetMeasurementEventCallback<double>(CollectDouble);
        _listener.Start();
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void MeterName_should_be_Kode_Agent()
    {
        KodeAgentMetrics.MeterName.Should().Be("Kode.Agent");
    }

    [Fact]
    public void RunsStarted_counter_should_emit_measurement()
    {
        KodeAgentMetrics.RunsStarted.Add(1,
            new KeyValuePair<string, object?>("agent.id", "test-agent"));

        _listener.RecordObservableInstruments();

        _measurements.Should().ContainSingle(m => m.Name == "kode.agent.runs.started");
        var m = _measurements.First(m => m.Name == "kode.agent.runs.started");
        m.Value.Should().Be(1L);
        m.Tags.Should().Contain(t => t.Key == "agent.id" && (string?)t.Value == "test-agent");
    }

    [Fact]
    public void RunDuration_histogram_should_emit_measurement()
    {
        KodeAgentMetrics.RunDuration.Record(1234.5,
            new KeyValuePair<string, object?>("agent.id", "test-agent"));

        _measurements.Should().ContainSingle(m => m.Name == "kode.agent.run.duration");
        var m = _measurements.First(m => m.Name == "kode.agent.run.duration");
        m.Value.Should().Be(1234.5);
    }

    [Fact]
    public void TokensInput_counter_should_emit_measurement_with_model_tag()
    {
        KodeAgentMetrics.TokensInput.Add(500,
            new KeyValuePair<string, object?>("model", "claude-sonnet"));

        _measurements.Should().ContainSingle(m => m.Name == "kode.agent.tokens.input");
        var m = _measurements.First(m => m.Name == "kode.agent.tokens.input");
        m.Value.Should().Be(500L);
        m.Tags.Should().Contain(t => t.Key == "model" && (string?)t.Value == "claude-sonnet");
    }

    [Fact]
    public void ToolExecutions_counter_should_emit_measurement_with_tool_name_tag()
    {
        KodeAgentMetrics.ToolExecutions.Add(1,
            new KeyValuePair<string, object?>("tool.name", "fs_read"));

        _measurements.Should().ContainSingle(m => m.Name == "kode.agent.tool.executions");
        var m = _measurements.First(m => m.Name == "kode.agent.tool.executions");
        m.Tags.Should().Contain(t => t.Key == "tool.name" && (string?)t.Value == "fs_read");
    }

    [Fact]
    public void ModelRequestDuration_histogram_should_emit_measurement()
    {
        KodeAgentMetrics.ModelRequestDuration.Record(876.0,
            new KeyValuePair<string, object?>("model", "gpt-4"));

        _measurements.Should().ContainSingle(m => m.Name == "kode.agent.model.request.duration");
    }

    [Fact]
    public void ContextCompressions_counter_should_emit_measurement()
    {
        KodeAgentMetrics.ContextCompressions.Add(1);

        _measurements.Should().ContainSingle(m => m.Name == "kode.agent.context.compressions");
    }

    private void CollectLong(Instrument instrument, long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        _measurements.Add((instrument.Name, measurement, tags.ToArray()));
    }

    private void CollectDouble(Instrument instrument, double measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        _measurements.Add((instrument.Name, measurement, tags.ToArray()));
    }
}
