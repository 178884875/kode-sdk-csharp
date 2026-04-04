using System.Diagnostics;
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

    [Fact]
    public void ModelTtft_histogram_should_emit_measurement_with_model_and_provider_tags()
    {
        KodeAgentMetrics.ModelTtft.Record(312.0,
            new TagList { { "model", "claude-opus" }, { "provider", "anthropic" } });

        _measurements.Should().ContainSingle(m => m.Name == "kode.agent.model.time_to_first_token");
        var m = _measurements.First(m => m.Name == "kode.agent.model.time_to_first_token");
        m.Value.Should().Be(312.0);
        m.Tags.Should().Contain(t => t.Key == "model" && (string?)t.Value == "claude-opus");
        m.Tags.Should().Contain(t => t.Key == "provider" && (string?)t.Value == "anthropic");
    }

    [Fact]
    public void CompressionTokensInput_counter_should_emit_measurement()
    {
        KodeAgentMetrics.CompressionTokensInput.Add(4200);

        _measurements.Should().ContainSingle(m => m.Name == "kode.agent.context.compression.tokens.input");
        _measurements.First(m => m.Name == "kode.agent.context.compression.tokens.input")
            .Value.Should().Be(4200L);
    }

    [Fact]
    public void CompressionTokensOutput_counter_should_emit_measurement()
    {
        KodeAgentMetrics.CompressionTokensOutput.Add(800);

        _measurements.Should().ContainSingle(m => m.Name == "kode.agent.context.compression.tokens.output");
        _measurements.First(m => m.Name == "kode.agent.context.compression.tokens.output")
            .Value.Should().Be(800L);
    }

    [Fact]
    public void TokensInput_should_accept_session_type_and_agent_role_tags()
    {
        KodeAgentMetrics.TokensInput.Add(1000, new TagList
        {
            { "model", "gpt-4o" },
            { "session_type", "main" },
            { "agent_role", "primary" }
        });

        var m = _measurements.First(m => m.Name == "kode.agent.tokens.input");
        m.Tags.Should().Contain(t => t.Key == "session_type" && (string?)t.Value == "main");
        m.Tags.Should().Contain(t => t.Key == "agent_role" && (string?)t.Value == "primary");
    }

    [Fact]
    public void ToolExecutions_should_accept_tool_category_tag()
    {
        KodeAgentMetrics.ToolExecutions.Add(1, new TagList
        {
            { "tool.name", "fs_read" },
            { "tool_category", "filesystem" }
        });

        var m = _measurements.First(m => m.Name == "kode.agent.tool.executions");
        m.Tags.Should().Contain(t => t.Key == "tool_category" && (string?)t.Value == "filesystem");
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
