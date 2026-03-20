using FluentAssertions;
using Xunit;

namespace KodaClaw.ContractTests.Smoke;

public sealed class ContractSkeletonTests
{
    [Fact]
    public void Contract_test_project_should_compile_and_run()
    {
        true.Should().BeTrue();
    }
}
