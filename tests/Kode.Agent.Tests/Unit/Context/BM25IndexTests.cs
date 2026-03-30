using FluentAssertions;
using Kode.Agent.Sdk.Core.Context;
using Xunit;

namespace Kode.Agent.Tests.Unit.Context;

public sealed class BM25IndexTests
{
    // ── Search basics ─────────────────────────────────────────────────────────

    [Fact]
    public void Search_EmptyIndex_ReturnsEmpty()
    {
        var index = new BM25Index();
        index.Search("anything").Should().BeEmpty();
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        var index = new BM25Index();
        index.Add("d1", "some content here", "snippet", 0);

        index.Search("").Should().BeEmpty();
        index.Search("   ").Should().BeEmpty();
    }

    [Fact]
    public void Search_SingleDocument_ReturnsItWhenMatches()
    {
        var index = new BM25Index();
        index.Add("doc1", "the quick brown fox", "snippet", 1000L);

        var results = index.Search("fox");

        results.Should().HaveCount(1);
        results[0].DocumentId.Should().Be("doc1");
        results[0].Timestamp.Should().Be(1000L);
    }

    [Fact]
    public void Search_SingleDocument_ReturnsEmptyWhenNoMatch()
    {
        var index = new BM25Index();
        index.Add("doc1", "the quick brown fox", "snippet", 0);

        index.Search("elephant").Should().BeEmpty();
    }

    [Fact]
    public void Search_MultipleDocuments_RanksMoreRelevantHigher()
    {
        var index = new BM25Index();
        index.Add("low",  "database schema migration plan", "low", 0);
        index.Add("high", "database schema database schema database", "high", 0);

        var results = index.Search("database schema");

        results[0].DocumentId.Should().Be("high",
            because: "higher term frequency should produce higher BM25 score");
    }

    [Fact]
    public void Search_RarerTermsRankedHigher()
    {
        var index = new BM25Index();
        // "common" appears in both docs, "rare" only in doc2
        index.Add("doc1", "common word here", "d1", 0);
        index.Add("doc2", "common word rare term", "d2", 0);

        var results = index.Search("rare");

        results.Should().HaveCount(1);
        results[0].DocumentId.Should().Be("doc2");
    }

    [Fact]
    public void Search_RespectsLimit()
    {
        var index = new BM25Index();
        for (var i = 0; i < 10; i++)
            index.Add($"doc{i}", "matching query term", "snippet", 0);

        var results = index.Search("matching", limit: 3);

        results.Should().HaveCount(3);
    }

    [Fact]
    public void Search_LimitClampedToDocCount()
    {
        var index = new BM25Index();
        index.Add("d1", "fox", "s", 0);
        index.Add("d2", "fox", "s", 0);

        var results = index.Search("fox", limit: 100);

        results.Should().HaveCount(2);
    }

    [Fact]
    public void Search_IsCaseInsensitive()
    {
        var index = new BM25Index();
        index.Add("doc1", "The Quick Brown Fox", "snippet", 0);

        index.Search("quick").Should().HaveCount(1);
        index.Search("QUICK").Should().HaveCount(1);
        index.Search("Quick").Should().HaveCount(1);
    }

    [Fact]
    public void Search_ResultsOrderedByScoreDescending()
    {
        var index = new BM25Index();
        index.Add("weak",   "schema", "w", 0);
        index.Add("strong", "database schema database schema", "s", 0);

        var results = index.Search("database schema");

        results.Should().HaveCountGreaterThan(0);
        results.Should().BeInDescendingOrder(r => r.Score);
    }

    [Fact]
    public void Search_SnippetIsPreservedFromAdd()
    {
        const string expectedSnippet = "my custom snippet text";
        var index = new BM25Index();
        index.Add("d1", "search content here", expectedSnippet, 0);

        var result = index.Search("content");

        result[0].Snippet.Should().Be(expectedSnippet);
    }

    // ── Count ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Count_ReflectsAddedDocuments()
    {
        var index = new BM25Index();
        index.Count.Should().Be(0);
        index.Add("d1", "text", "s", 0);
        index.Count.Should().Be(1);
        index.Add("d2", "text", "s", 0);
        index.Count.Should().Be(2);
    }

    // ── CJK tokenization ─────────────────────────────────────────────────────

    [Fact]
    public void Tokenize_CjkText_ProducesBigrams()
    {
        var tokens = BM25Index.Tokenize("数据库");

        // "数据库" → bigrams: "数据", "据库" + single chars "数","据","库"
        tokens.Should().Contain("数据");
        tokens.Should().Contain("据库");
    }

    [Fact]
    public void Tokenize_MixedText_HandlesBothCjkAndAscii()
    {
        var tokens = BM25Index.Tokenize("用户 database 问题");

        tokens.Should().Contain("database");
        tokens.Should().Contain("用");   // single CJK char
    }

    [Fact]
    public void Search_CjkQuery_MatchesBigramInDocument()
    {
        var index = new BM25Index();
        index.Add("doc1", "这是数据库优化相关的讨论", "snippet", 0);

        var results = index.Search("数据库");

        results.Should().HaveCount(1,
            because: "CJK bigram search should match '数据库' in document text");
    }

    [Fact]
    public void Search_CjkDocumentAndQuery_RanksHigherForMoreMatches()
    {
        var index = new BM25Index();
        index.Add("low",  "数据库相关", "low", 0);
        index.Add("high", "数据库优化 数据库索引 数据库性能", "high", 0);

        var results = index.Search("数据库");

        results[0].DocumentId.Should().Be("high");
    }

    // ── Tokenize edge cases ───────────────────────────────────────────────────

    [Fact]
    public void Tokenize_EmptyString_ReturnsEmpty()
    {
        BM25Index.Tokenize("").Should().BeEmpty();
        BM25Index.Tokenize("   ").Should().BeEmpty();
    }

    [Fact]
    public void Tokenize_PunctuationOnly_ReturnsEmpty()
    {
        BM25Index.Tokenize("!!! ??? ...").Should().BeEmpty();
    }

    [Fact]
    public void Tokenize_NumbersAndLetters_AreIncluded()
    {
        var tokens = BM25Index.Tokenize("v1.2.3 release");

        tokens.Should().Contain("v1");
        tokens.Should().Contain("2");
        tokens.Should().Contain("3");
        tokens.Should().Contain("release");
    }
}
