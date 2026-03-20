using FluentAssertions;
using Xunit;
using KodaClaw.Contracts;

namespace KodaClaw.UnitTests.Contracts;

public sealed class SessionKindTests
{
    [Fact]
    public void Main_session_kind_should_be_zero_for_stable_defaults()
    {
        ((int)SessionKind.Main).Should().Be(0);
    }
}
